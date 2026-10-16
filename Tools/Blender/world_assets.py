"""
World objects + POIs from free Sketchfab assets (#99, all CC BY 4.0, see Assets/_Project/Art/CREDITS.md):
  World_Station   "Space Station 3" by re1monsen                 (trader station: twin habitat rings, spins)
  World_Wreck     "Empire destroyer Vigilant" by star_conflict   (derelict: a torn mid-section of the destroyer,
                                                                   + World_Wreck_Chunks = loose turrets/plates, also scrap)
  World_Ruin      "Ancient Relic" by FDU_oficial                 (precursor ruin: untextured, painted ivory/cyan in the
                                                                   vw vertex contract like the Warden)
  World_Nest      "Alien Eggs Updated" by jamesstone             (alien nest: the staged eggs without their pedestal)
  World_Crate     "Sci-fi cargo crate" by andreas9343            (loot crate: body + World_Crate_Lid)
  World_Rock      "Gray Big Rock" by 3dhdscan                    (resource node base: photoscan, decimated)
  World_Battery   "Sci-fi Battery" by andreas9343                (fuel node + pickup capsule)
  World_Pad       "Sci-Fi Platform / Teleporter Pad" by DevPoly3D (station landing pad, menu)
  World_Medkit    "Sci-fi Medkit" by alpha_0816                  (medkit tool + item)

Run headless from the repo root:
  ingest (once, from the Sketchfab zips the user downloaded, glTF or the original FBX download):
    blender -b --factory-startup -P Tools/Blender/world_assets.py -- --ingest <Downloads dir>
      -> Tools/Blender/Sources/World/<Name>.zip (mesh file(s) + license.txt, no textures)
      -> Assets/_Project/Art/Models/World/<Name>/<material>_<baseColor|normal|metallicRoughness|emissive|ao>.jpg (<= 2K,
         normal = OpenGL, metallicRoughness = glTF packing: G roughness, B metallic)
  build (reproducible from the repo):
    blender -b --factory-startup -P Tools/Blender/world_assets.py -- [--only Name] [--preview dir]
      -> Assets/_Project/Art/Models/World/<Name>.fbx

Contract (AGENTS.md): 1 unit = 1 m, forward = Blender -Y (Unity +Z), up +Z. Ground objects: origin at the bottom centre
(z 0 = ground); space objects: origin = bounds centre. Material names stay the source names; WorldModelBuilder maps them
to URP Lit materials from the texture folder (the ruin uses the vertex-contract hull shader).
Prints the Unity-space numbers PrefabBuilder needs (sizes, lid hinge, pad top).
"""
import sys
import os
import re
import json
import math
import glob
import shutil
import tempfile
import zipfile
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC_DIR = os.path.join(HERE, "Sources", "World")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "World")
MAX_TEX = 2048

IVORY = vw.srgb("#E2E6EA")
IVORY_SHADE = vw.srgb("#A9B3BE")
SLATE = vw.srgb("#2A323D")
CYAN = vw.srgb("#3FE0FF")


def arg(name):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else None


# name: download zip stem, mesh file inside the zip (None = scene.gltf), texture map for FBX sources
#   texture map: {material: {role: file | (combine, files...)}}; roles baseColor, normal, normalDX (DirectX, flipped),
#   emissive, ao, mr = ("rm", rough, metal) | ("gs", gloss, specular) | ("r", rough) - packed to glTF metallicRoughness;
#   "auto" = read the FBX's own material links (texture files matched by stem) and flat colours (written as 4x4 maps)
ASSETS = {
    "World_Station": ("space_station_3", None, None),
    "World_Wreck": ("empire-destroyer-vigilant", "source/H3_sf.fbx", {
        "Ship": {"baseColor": "r1_h_t3_d.png", "normal": "r1_h_t3_nm.png", "emissive": "r1_h_t3_glow.png", "ao": "r1_h_t3_occl.png",
                 "mr": ("gs", "r1_h_t3_gloss.png", "r1_h_t3_sc.png")},
        "Gun": {"baseColor": "assault_sc.png", "normal": "assault_nm.png", "mr": ("gs", "assault_gloss.png", "assault_sc.png")},
        "BigLightGun": {"baseColor": "biglightninggun_sc.png", "normal": "biglightninggun_nm.png", "emissive": "biglightninggun_glow.png",
                        "ao": "biglightninggun_occl.png", "mr": ("gs", "biglightninggun_gloss.png", "biglightninggun_sc.png")},
        "Push_ray": {"baseColor": "pushray_sc.png", "normal": "pushray_nm.png", "emissive": "pushray_glow.png", "ao": "pushray_occl.png",
                     "mr": ("gs", "pushray_gloss.png", "pushray_sc.png")},
    }),
    "World_Ruin": ("ancient_relic", None, None),
    "World_Nest": ("alien-eggs-updated", "source/EggsStaged.fbx", {
        m: {"baseColor": "T_%s_B.jpg" % t, "normalDX": "T_%s_N.jpg" % t, "ao": "T_%s_AO.jpg" % t, "mr": ("rm", "T_%s_R.jpg" % t, "T_%s_M.jpg" % t)}
        for m, t in (("AlienEggBlue", "AlienEggBlue"), ("AlienEggGreen", "AlienEggGreen"),
                     ("DestroyedEggGreen", "DestroyedEggGreen"), ("DestroyedEggsBlue", "DestroyedEggBlue"))
    }),
    "World_Crate": ("sci-fi_cargo_crate", None, None),
    "World_Rock": ("gray-big-rock", "source/sutr_tmave_sedy.zip", {
        "*": {"baseColor": "sutr_tmave_sedy.jpeg", "normal": "sutr_tmave_sedy_NormalsMap.jpeg", "ao": "sutr_tmave_sedy_ao.jpeg", "mr": ("r", 0.82)},
    }),
    "World_Battery": ("sci-fi-battery", "source/BatteryAnimation.fbx", {
        m: {"baseColor": "%s_albedo.jpg" % m, "normal": "%s_normal.png" % m, "mr": ("rm", "%s_roughness.jpg" % m, "%s_metallic.jpg" % m)}
        for m in ("Battery", "Case")
    }),
    "World_Pad": ("sci-fi-platform-teleporter-padunity-unreal", "source/Ameer Paltform 1.fbx", "auto"),
    "World_Medkit": ("sci-fi_medkit__game_asset", None, None),
}


def safe(name):
    return re.sub(r"[^A-Za-z0-9_]+", "_", name).strip("_")


# ---------------------------------------------------------------- texture helpers

def load_px(path, data):
    img = bpy.data.images.load(path)
    if data:
        img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    if max(w, h) > MAX_TEX:
        s = MAX_TEX / max(w, h)
        img.scale(max(1, int(w * s)), max(1, int(h * s)))
        w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4), w, h


def save_px(px, path, data):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    if data:
        img.colorspace_settings.name = 'Non-Color'
    px = px.copy()
    px[..., 3] = 1.0
    img.pixels.foreach_set(np.clip(px, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = 'JPEG'
    img.save(filepath=path, quality=92)
    bpy.data.images.remove(img)


def lum(px):
    return px[..., 0] * 0.2126 + px[..., 1] * 0.7152 + px[..., 2] * 0.0722


def resample(px, w, h):
    """Nearest resample to (h, w) so maps of different sizes can be combined."""
    sh, sw = px.shape[:2]
    if (sh, sw) == (h, w):
        return px
    ys = (np.arange(h) * sh // h).clip(0, sh - 1)
    xs = (np.arange(w) * sw // w).clip(0, sw - 1)
    return px[ys][:, xs]


def pack_mr(spec, folder):
    kind = spec[0]
    if kind == "r" and not isinstance(spec[1], str):
        px = np.zeros((4, 4, 4), dtype=np.float32)
        px[..., 1] = spec[1]
        return px
    a, w, h = load_px(os.path.join(folder, spec[1]), True)
    out = np.zeros_like(a)
    if kind == "rm":
        m, *_ = load_px(os.path.join(folder, spec[2]), True)
        out[..., 1] = a[..., 0]
        out[..., 2] = resample(m, w, h)[..., 0]
    elif kind == "gs":
        s, *_ = load_px(os.path.join(folder, spec[2]), False)
        s = resample(s, w, h)
        out[..., 1] = 1.0 - a[..., 0]
        # specular workflow: bright, coloured specular = metal; dielectrics sit at ~0.04 (dark grey)
        out[..., 2] = np.clip((lum(s) - 0.22) * 2.5, 0.0, 1.0)
    else:
        out[..., 1] = a[..., 0]
    return out


# ---------------------------------------------------------------- ingest

def ingest_one(name, stem, mesh, texmap, downloads):
    z = os.path.join(downloads, stem + ".zip")
    if not os.path.exists(z):
        print("[world] ingest: missing", z)
        return
    tmp = tempfile.mkdtemp(prefix="vw_ingest_")
    with zipfile.ZipFile(z) as src:
        for n in src.namelist():
            base = os.path.basename(n)
            if not base:
                continue
            if base in ("scene.gltf", "scene.bin", "license.txt") or n == mesh or base.lower().endswith((".png", ".jpg", ".jpeg")):
                src.extract(n, tmp)
    files = []
    if mesh is None:
        files = ["scene.gltf", "scene.bin"]
    elif mesh.lower().endswith(".zip"):
        # the source FBX is itself zipped (3dhdscan): take the FBX out of it
        with zipfile.ZipFile(os.path.join(tmp, mesh)) as inner:
            for n in inner.namelist():
                if n.lower().endswith(".fbx"):
                    inner.extract(n, os.path.join(tmp, "inner"))
                    files.append(os.path.join("inner", n))
    else:
        files = [mesh]
    if os.path.exists(os.path.join(tmp, "license.txt")):
        files.append("license.txt")
    os.makedirs(SRC_DIR, exist_ok=True)
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as dst:
        for n in files:
            arcname = "source.fbx" if n.lower().endswith(".fbx") else os.path.basename(n)
            dst.write(os.path.join(tmp, n), arcname)
    tex_out = os.path.join(OUT, name)
    os.makedirs(tex_out, exist_ok=True)
    tex_dir = os.path.join(tmp, "textures")
    if mesh is None:
        ingest_gltf_textures(os.path.join(tmp, "scene.gltf"), tmp, tex_out)
    elif texmap == "auto":
        ingest_fbx_auto(os.path.join(tmp, files[0]), tex_dir, tex_out)
    elif texmap:
        for mat, roles in texmap.items():
            mname = "Rock" if mat == "*" else safe(mat)
            for role, spec in roles.items():
                out_role = "normal" if role == "normalDX" else "metallicRoughness" if role == "mr" else role
                dst = os.path.join(tex_out, "%s_%s.jpg" % (mname, out_role))
                if role == "mr":
                    save_px(pack_mr(spec, tex_dir), dst, True)
                    continue
                path = os.path.join(tex_dir, spec)
                if not os.path.exists(path):
                    print("[world] ingest: %s missing texture %s" % (name, spec))
                    continue
                data = role not in ("baseColor", "emissive")
                px, w, h = load_px(path, data)
                if role == "normalDX":
                    px[..., 1] = 1.0 - px[..., 1]
                save_px(px, dst, data)
    shutil.rmtree(tmp, ignore_errors=True)
    print("[world] ingest:", name)


def solid(rgb):
    px = np.ones((4, 4, 4), dtype=np.float32)
    px[..., :3] = rgb[:3]
    return px


def ingest_fbx_auto(fbx, tex_dir, tex_out):
    """Texture set per material from the FBX's Principled links; missing files / unlinked inputs become flat 4x4 maps."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    stems = {os.path.splitext(f)[0].lower(): os.path.join(tex_dir, f) for f in os.listdir(tex_dir)} if os.path.isdir(tex_dir) else {}

    def linked_file(sock):
        if not sock.is_linked:
            return None
        n = sock.links[0].from_node
        if n.type == 'NORMAL_MAP':
            sock = n.inputs["Color"]
            if not sock.is_linked:
                return None
            n = sock.links[0].from_node
        if n.type == 'TEX_IMAGE' and n.image:
            return stems.get(os.path.splitext(os.path.basename(n.image.filepath or n.image.name))[0].lower())
        return None

    done = set()
    for m in bpy.data.materials:
        name = safe(m.name)
        if name in done or not m.node_tree:
            continue
        done.add(name)
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            continue
        base = linked_file(bsdf.inputs["Base Color"])
        if base:
            px, *_ = load_px(base, False)
        else:
            c = bsdf.inputs["Base Color"].default_value
            px = solid([c[0] ** (1 / 2.2), c[1] ** (1 / 2.2), c[2] ** (1 / 2.2)])
        save_px(px, os.path.join(tex_out, name + "_baseColor.jpg"), False)
        nrm = linked_file(bsdf.inputs["Normal"])
        if nrm:
            px, *_ = load_px(nrm, True)
            save_px(px, os.path.join(tex_out, name + "_normal.jpg"), True)
        mr = np.zeros((4, 4, 4), dtype=np.float32)
        mr[..., 1] = bsdf.inputs["Roughness"].default_value
        mr[..., 2] = bsdf.inputs["Metallic"].default_value
        save_px(mr, os.path.join(tex_out, name + "_metallicRoughness.jpg"), True)
        e = bsdf.inputs["Emission Color"].default_value
        strength = bsdf.inputs["Emission Strength"].default_value
        if strength > 0 and max(e[:3]) > 0.01:
            save_px(solid([min(1.0, e[i] ** (1 / 2.2)) for i in range(3)]), os.path.join(tex_out, name + "_emissive.jpg"), False)
        elif not base and "lig" in name.lower():
            # the light strips ("Ylo Lig", "Grn lig") lost their emission in the FBX: they glow in their own colour
            save_px(px, os.path.join(tex_out, name + "_emissive.jpg"), False)
        print("[world] auto material %s base %s normal %s" % (name, os.path.basename(base) if base else "flat", bool(nrm)))


def ingest_gltf_textures(gltf_path, root, tex_out):
    g = json.load(open(gltf_path, encoding="utf-8"))
    images = g.get("images", [])
    textures = g.get("textures", [])

    def uri(info):
        if not info:
            return None
        src = textures[info["index"]].get("source")
        return os.path.join(root, images[src]["uri"].replace("%20", " ")) if src is not None else None

    for m in g.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        roles = {
            "baseColor": uri(pbr.get("baseColorTexture")),
            "metallicRoughness": uri(pbr.get("metallicRoughnessTexture")),
            "normal": uri(m.get("normalTexture")),
            "emissive": uri(m.get("emissiveTexture")),
            "ao": uri(m.get("occlusionTexture")),
        }
        if roles["ao"] and roles["ao"] == roles["metallicRoughness"]:
            roles["ao"] = None          # ORM packing: the occlusion sits in R of the same image, already covered
        for role, path in roles.items():
            if not path or not os.path.exists(path):
                continue
            data = role not in ("baseColor", "emissive")
            px, w, h = load_px(path, data)
            save_px(px, os.path.join(tex_out, "%s_%s.jpg" % (safe(m["name"]), role)), data)


# ---------------------------------------------------------------- import + helpers

def import_source(name):
    tmp = tempfile.mkdtemp(prefix="vw_src_")
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip")) as z:
        z.extractall(tmp)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if os.path.exists(os.path.join(tmp, "scene.gltf")):
        bpy.ops.import_scene.gltf(filepath=os.path.join(tmp, "scene.gltf"))
    else:
        bpy.ops.import_scene.fbx(filepath=os.path.join(tmp, "source.fbx"))
    shutil.rmtree(tmp, ignore_errors=True)
    # materials keep their source names, made safe for the texture lookup ("Material.001" -> "Material_001")
    for m in bpy.data.materials:
        m.name = safe(m.name)


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons)]


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def bounds(objs):
    lo = Vector((1e18,) * 3)
    hi = Vector((-1e18,) * 3)
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def bake(objs, name):
    """Applies every parent/object transform and joins the meshes into one object; removes everything else."""
    objs = [o for o in objs if o.type == 'MESH' and len(o.data.polygons)]
    for o in bpy.context.scene.objects:     # animated sources (the battery) would snap back to their keys
        o.animation_data_clear()
    for o in objs:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in objs:
        if o.data.users > 1:
            o.data = o.data.copy()
        vw.activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj = vw.join(objs, name) if len(objs) > 1 else objs[0]
    obj.name = obj.data.name = name
    for o in [o for o in bpy.context.scene.objects if o != obj]:
        bpy.data.objects.remove(o, do_unlink=True)
    return obj


def xf(obj, m):
    obj.data.transform(m)
    obj.data.update()


def normalize(obj, size, measure="max", ground=True):
    """Scales so `measure` (max / x / y / z extent) = size; origin bottom centre (ground) or bounds centre."""
    lo, hi = bounds([obj])
    ext = hi - lo
    s = size / {"max": max(ext), "x": ext.x, "y": ext.y, "z": ext.z}[measure]
    c = (lo + hi) * 0.5
    if ground:
        c.z = lo.z
    xf(obj, Matrix.Scale(s, 4) @ Matrix.Translation(-c))
    return s


def principal_axes(obj):
    pts = np.array([v.co[:] for v in obj.data.vertices], dtype=np.float64)
    c = pts.mean(axis=0)
    w, v = np.linalg.eigh(np.cov((pts - c).T))
    return Vector(c), [Vector(v[:, i]) for i in range(3)], w     # ascending variance


def faces_where(obj, pred):
    return [p.index for p in obj.data.polygons if pred(p)]


def delete_faces(obj, idx):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in idx], context='FACES')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def separate(obj, idx, name):
    vw.activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    sel = set(idx)
    for p in obj.data.polygons:
        p.select = p.index in sel
    before = set(bpy.context.scene.objects)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    o = [x for x in bpy.context.scene.objects if x not in before][0]
    o.name = o.data.name = name
    return o


def loose_parts(obj):
    """Face index lists of the connected islands."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, fs = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            fs.append(g.index)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        out.append(fs)
    bm.free()
    return out


def decimate(obj, target):
    ratio = min(1.0, target / max(1, tris(obj)))
    if ratio < 1.0:
        m = obj.modifiers.new("dec", 'DECIMATE')
        m.ratio = ratio
        m.use_collapse_triangulate = True
        vw.apply_modifiers(obj)


def export(obj, name, contract=False):
    vw.activate(obj)
    if obj.data.materials:
        bpy.ops.object.material_slot_remove_unused()     # deleted decals/glass leave empty slots behind
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False,
        colors_type='SRGB', prioritize_active_color=contract, path_mode='STRIP')
    lo, hi = bounds([obj])
    print("[world] %s size (unity x/y/z) %.2f %.2f %.2f  y %.2f..%.2f  tris %d  mats %s" % (
        name, hi.x - lo.x, hi.z - lo.z, hi.y - lo.y, lo.z, hi.z, tris(obj), [m.name for m in obj.data.materials if m]))


def U(v):
    return Vector((-v.x, v.z, -v.y))


# ---------------------------------------------------------------- builds

def build_station():
    objs = meshes()
    main = [o for o in objs if not any(m and "smalllights" in m.name for m in o.data.materials)]
    lights = [o for o in objs if o not in main]
    obj = bake(objs, "World_Station")
    # the ring axis = least variance of the hull (the source is tilted); the light bits float around far outside
    core_idx = set()
    tmp_lo, tmp_hi = None, None
    c, axes, _ = principal_axes(obj)
    axis = axes[0]
    rot = axis.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()
    xf(obj, rot @ Matrix.Translation(-c))
    # drop the loose light specks floating beyond the rings (keep everything within 1.05 x the ring radius)
    radius = 0.0
    for p in obj.data.polygons:
        if len(p.vertices) and p.material_index < len(obj.data.materials) and "smalllights" not in obj.data.materials[p.material_index].name:
            radius = max(radius, Vector((p.center.x, p.center.y)).length)
    drop = []
    for fs in loose_parts(obj):
        cen = sum((obj.data.polygons[i].center for i in fs), Vector()) / len(fs)
        if Vector((cen.x, cen.y)).length > radius * 1.05 or abs(cen.z) > radius * 0.6:
            drop += fs
    delete_faces(obj, drop)
    print("[world] station ring radius %.2f, dropped %d floating faces" % (radius, len(drop)))
    normalize(obj, 64.0, "max", ground=False)
    # radial profile per height (for the pad arm in PrefabBuilder): outer radius of the hull at each z band
    pts = np.array([v.co[:] for v in obj.data.vertices])
    rad = np.hypot(pts[:, 0], pts[:, 1])
    for z in np.linspace(pts[:, 2].min(), pts[:, 2].max(), 13):
        sel = rad[np.abs(pts[:, 2] - z) < 1.6]
        if len(sel):
            print("[world] station z %6.1f  radius %5.1f..%5.1f  (%d verts)" % (z, sel.min(), sel.max(), len(sel)))
    export(obj, "World_Station")
    return [obj]


def build_wreck():
    obj = bake(meshes(), "World_Wreck")
    # drop decals (faction signs), glass sheets and engine glow cards
    mats = obj.data.materials
    keep = {"Ship", "Gun", "BigLightGun", "Push_ray"}
    delete_faces(obj, faces_where(obj, lambda p: mats[p.material_index] is None or mats[p.material_index].name not in keep))
    # long axis -> Y, flat side down; nose to -Y (the source nose sits on +Y)
    c, axes, _ = principal_axes(obj)
    rot = axes[2].rotation_difference(Vector((0, 1, 0))).to_matrix().to_4x4()
    xf(obj, rot @ Matrix.Translation(-c))
    lo, hi = bounds([obj])
    if (hi - lo).z > (hi - lo).x:
        xf(obj, Matrix.Rotation(math.radians(90), 4, 'Y'))
    normalize(obj, 110.0, "y", ground=False)
    # torn mid-section: everything outside two ragged cut planes goes; loose turrets beyond them become chunks
    def cut(p, a, b):
        y = p.y + 1.6 * math.sin(p.x * 0.45 + p.z * 0.3) + 0.9 * math.sin(p.z * 1.1)
        return a < y < b
    A, B = -12.0, 24.0
    inside = faces_where(obj, lambda p: cut(p.center, A, B))
    inside_set = set(inside)
    parts = loose_parts(obj)
    chunk_faces = []
    for fs in parts:
        if any(i in inside_set for i in fs):
            continue
        cen = sum((obj.data.polygons[i].center for i in fs), Vector()) / len(fs)
        if 60 < len(fs) < 4000 and abs(cen.y - (A + B) * 0.5) < 40:
            chunk_faces.append(fs)
    chunk_faces = sorted(chunk_faces, key=len, reverse=True)
    scrap_faces = chunk_faces[6:10]                  # the next four loose parts: a scrap pile (resource node)
    chunk_faces = chunk_faces[:6]
    chunk_set = set(i for fs in chunk_faces for i in fs)
    scrap_set = set(i for fs in scrap_faces for i in fs)
    # separate both in one go (indices shift after a separate): tag the faces first
    tag = obj.data.attributes.new("part", 'INT', 'FACE')
    tag.data.foreach_set("value", [1 if p.index in chunk_set else 2 if p.index in scrap_set else 0 for p in obj.data.polygons])
    chunks = separate(obj, faces_where(obj, lambda p: obj.data.attributes["part"].data[p.index].value == 1), "World_Wreck_Chunks") if chunk_set else None
    scrap = separate(obj, faces_where(obj, lambda p: obj.data.attributes["part"].data[p.index].value == 2), "World_Scrap") if scrap_set else None
    for o in (obj, chunks, scrap):
        if o is not None and "part" in o.data.attributes:
            o.data.attributes.remove(o.data.attributes["part"])
    if scrap is not None:
        build_scrap_pile(scrap)
    delete_faces(obj, faces_where(obj, lambda p: not cut(p.center, A, B)))
    lo, hi = bounds([obj])
    centre = (lo + hi) * 0.5
    # the section drifts: rolled and pitched a little
    tilt = Matrix.Rotation(math.radians(14), 4, 'Y') @ Matrix.Rotation(math.radians(-6), 4, 'X')
    xf(obj, tilt @ Matrix.Translation(-centre))
    objs = [obj]
    if chunks:
        # scatter the loose parts around the section
        spread = [(-16, -24, 6), (14, -20, -8), (-10, 30, -4), (18, 26, 7), (-22, 4, -10), (24, -2, 9)]
        bm_parts = loose_parts(chunks)
        groups = []
        for fs in bm_parts:
            cen = sum((chunks.data.polygons[i].center for i in fs), Vector()) / len(fs)
            groups.append((fs, cen))
        vw.activate(chunks)
        # move each island on its own (vertex level)
        me = chunks.data
        for k, (fs, cen) in enumerate(groups):
            off = Vector(spread[k % len(spread)])
            r = Matrix.Rotation(k * 1.3, 4, Vector((math.sin(k), math.cos(k * 0.7), 0.5)).normalized())
            verts = set(v for i in fs for v in me.polygons[i].vertices)
            for vi in verts:
                me.vertices[vi].co = r @ (me.vertices[vi].co - cen) + off
        me.update()
        export(chunks, "World_Wreck_Chunks")
        objs.append(chunks)
    export(obj, "World_Wreck")
    return objs


def evaluated_copy(objs, name):
    """One mesh object from the evaluated meshes (armature-skinned parts in their posed place) in world space."""
    dg = bpy.context.evaluated_depsgraph_get()
    copies = []
    for o in objs:
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        me.transform(o.matrix_world)
        c = bpy.data.objects.new(o.name + "_eval", me)
        bpy.context.scene.collection.objects.link(c)
        copies.append(c)
    for o in [o for o in bpy.context.scene.objects if o not in copies]:
        bpy.data.objects.remove(o, do_unlink=True)
    obj = vw.join(copies, name) if len(copies) > 1 else copies[0]
    obj.name = obj.data.name = name
    return obj


def build_scrap_pile(scrap):
    """Piles the loose destroyer parts on the origin: a ~1.8 m heap of plates and gun housings on the ground."""
    me = scrap.data
    islands = loose_parts(scrap)
    for k, fs in enumerate(islands):
        verts = set(v for i in fs for v in me.polygons[i].vertices)
        cen = sum((me.vertices[v].co for v in verts), Vector()) / len(verts)
        ext = max(max(abs((me.vertices[v].co - cen)[a]) for v in verts) for a in range(3))
        s = 0.9 / max(ext, 1e-4)                      # every part ~1.8 m before the pile is normalized
        r = Matrix.Rotation(0.9 + k * 1.7, 4, Vector((math.cos(k * 2.1), math.sin(k * 2.1), 0.6)).normalized())
        off = Vector((math.cos(k * 2.4) * 0.55, math.sin(k * 2.4) * 0.55, k * 0.35))
        for v in verts:
            me.vertices[v].co = r @ ((me.vertices[v].co - cen) * s) + off
    me.update()
    normalize(scrap, 1.9, "max", ground=True)
    xf(scrap, Matrix.Translation((0, 0, -0.08)))
    export(scrap, "World_Scrap")


def build_ruin():
    # the four arms are rigidly skinned to armatures (the relic's "clockwork" animation): take the posed meshes;
    # drop the ground plane and the scene-sized Sketchfab helper spheres (no material)
    objs = [o for o in meshes() if o.data.materials and not any(m and m.name == "Material_001" for m in o.data.materials)]
    obj = evaluated_copy(objs, "World_Ruin")
    # glTF Y-up import is already Z-up in Blender; ground the plaza
    normalize(obj, 16.0, "max", ground=True)
    vw.activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.005)
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in obj.data.polygons:
        p.use_smooth = False
    obj.data.materials.clear()
    lo, hi = bounds([obj])

    def col(p, n):
        t = vw.smoothstep(lo.z, hi.z, p.z) * 0.5 + max(0.0, n.z) * 0.5
        return tuple(IVORY_SHADE[i] + (IVORY[i] - IVORY_SHADE[i]) * t for i in range(3))
    vw.paint(obj, col, lambda p, n: (0.7, 0.0), lambda p, n: 0.05)
    bpy.context.scene.render.engine = 'CYCLES'
    bpy.context.scene.cycles.device = 'CPU'
    vw.bake_ao(obj, distance=1.5, samples=16)
    me = obj.data
    colattr = me.color_attributes["Col"]
    surf = me.uv_layers["surf"]
    det = me.uv_layers["detail"]
    glow = 0
    for poly in me.polygons:
        ao = sum(det.data[li].uv[1] for li in poly.loop_indices) / len(poly.loop_indices)
        lit = ao < 0.22
        if lit:
            for li in poly.loop_indices:
                colattr.data[li].color_srgb = (CYAN[0], CYAN[1], CYAN[2], 1.0)
                surf.data[li].uv = (0.9, 0.9)
                det.data[li].uv = (0.0, 1.0)
            glow += 1
        elif ao < 0.42:
            for li in poly.loop_indices:
                colattr.data[li].color_srgb = (SLATE[0], SLATE[1], SLATE[2], 1.0)
    print("[world] ruin glow faces %d of %d" % (glow, len(me.polygons)))
    export(obj, "World_Ruin", contract=True)
    return [obj]


def build_nest():
    objs = [o for o in meshes() if not any(m and m.name == "Pedestal" for m in o.data.materials)]
    obj = bake(objs, "World_Nest")
    normalize(obj, 4.4, "max", ground=True)
    xf(obj, Matrix.Translation((0, 0, -0.06)))      # sunk a little into the ground
    export(obj, "World_Nest")
    return [obj]


def build_crate():
    obj = bake(meshes(), "World_Crate")
    lo, hi = bounds([obj])
    # long side along Unity x (Blender x), as the legacy crate
    if (hi - lo).y > (hi - lo).x:
        xf(obj, Matrix.Rotation(math.radians(90), 4, 'Z'))
    normalize(obj, 1.45, "x", ground=True)
    mats = obj.data.materials
    lid_idx = faces_where(obj, lambda p: mats[p.material_index] is not None and "Top" in mats[p.material_index].name)
    lid = separate(obj, lid_idx, "World_Crate_Lid") if lid_idx else None
    objs = [obj]
    if lid:
        llo, lhi = bounds([lid])
        hinge = Vector(((llo.x + lhi.x) * 0.5, lhi.y, llo.z))     # Blender +y = Unity -z (the back edge)
        h = U(hinge)
        print("[world] crate lid hinge (unity) (%.3f, %.3f, %.3f) lid bottom %.3f top %.3f" % (h.x, h.y, h.z, llo.z, lhi.z))
        export(lid, "World_Crate_Lid")
        objs.append(lid)
    export(obj, "World_Crate")
    return objs


def build_rock():
    obj = bake(meshes(), "World_Rock")
    for m in obj.data.materials:
        if m:
            m.name = "Rock"
    vw.activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode='OBJECT')
    # the scan stands on a flat cut: lay its flattest axis upright
    c, axes, _ = principal_axes(obj)
    up = axes[0] if axes[0].z >= 0 else -axes[0]
    xf(obj, up.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4() @ Matrix.Translation(-c))
    decimate(obj, 6000)
    for p in obj.data.polygons:
        p.use_smooth = True
    normalize(obj, 2.6, "max", ground=True)
    xf(obj, Matrix.Translation((0, 0, -0.12)))
    export(obj, "World_Rock")
    return [obj]


def build_battery():
    obj = bake(meshes(), "World_Battery")
    c, axes, _ = principal_axes(obj)
    lo, hi = bounds([obj])
    normalize(obj, 0.62, "z", ground=True)
    export(obj, "World_Battery")
    return [obj]


def build_pad():
    obj = bake(meshes(), "World_Pad")
    normalize(obj, 30.0, "max", ground=False)
    lo, hi = bounds([obj])
    # origin = the landing deck at the centre (the console panels around it stand higher)
    hit, loc, *_ = obj.ray_cast(Vector((0, 0, hi.z + 1.0)), Vector((0, 0, -1)))
    deck = loc.z if hit else hi.z
    xf(obj, Matrix.Translation((0, 0, -deck)))
    lo, hi = bounds([obj])
    for r in (5.0, 9.0, 12.0, 14.0):
        h2, l2, *_ = obj.ray_cast(Vector((0, -r, hi.z + 1.0)), Vector((0, 0, -1)))
        print("[world] pad deck at radius %.1f: %s" % (r, "%.2f" % l2.z if h2 else "-"))
    print("[world] pad panels top %.2f bottom %.2f" % (hi.z, lo.z))
    export(obj, "World_Pad")
    return [obj]


def build_medkit():
    obj = bake(meshes(), "World_Medkit")
    normalize(obj, 0.42, "max", ground=True)
    export(obj, "World_Medkit")
    return [obj]


BUILDS = {
    "World_Station": build_station, "World_Wreck": build_wreck, "World_Ruin": build_ruin, "World_Nest": build_nest,
    "World_Crate": build_crate, "World_Rock": build_rock, "World_Battery": build_battery, "World_Pad": build_pad,
    "World_Medkit": build_medkit,
}


# ---------------------------------------------------------------- preview

def preview(name, objs, out):
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE' if name != "World_Ruin" else 'VERTEX'
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    lo, hi = bounds(objs)
    c = (lo + hi) * 0.5
    r = max((hi - lo).length * 0.5, 0.1)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.clip_start, cam_data.clip_end = r * 0.01, r * 20
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for tag, d in (("a", Vector((0.8, -1.0, 0.55))), ("b", Vector((-0.9, 0.7, 0.35)))):
        d.normalize()
        cam.location = c + d * r * 2.4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, tag))
        bpy.ops.render.render(write_still=True)


# ---------------------------------------------------------------- main

if "--ingest" in ARGS:
    only = arg("--only")
    for name, (stem, mesh, texmap) in ASSETS.items():
        if not only or name == only:
            ingest_one(name, stem, mesh, texmap, arg("--ingest"))
else:
    os.makedirs(OUT, exist_ok=True)
    only = arg("--only")
    prev = arg("--preview")
    for name in ASSETS:
        if only and name != only:
            continue
        if not os.path.exists(os.path.join(SRC_DIR, name + ".zip")):
            print("[world] skip (no source):", name)
            continue
        import_source(name)
        objs = BUILDS[name]()
        if prev:
            preview(name, objs, prev)
