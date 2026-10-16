"""
Planet surfaces, decor rocks and asteroids from free CC0 assets (#100, see Assets/_Project/Art/CREDITS.md).

Terrain texture sets (Poly Haven + ambientCG, CC0), one or more per PlanetDecorStyle:
  Rocky     RockyTerrain02, RockFace03 (cliffs)          Desert    RedSand, SandstoneCracks
  Ice       Ice002, Ice003, Snow006                      Volcanic  DarkRock, Lava004 (emissive)
  Barren    MoonDusted02, MoonMeteor01 (craters)         Jungle    MossyRock, Moss002, BrownMud (toxic)
  Crystal   DarkRock02                                   Ocean     CoastSand01 (shore + seabed)
Models (Poly Haven, CC0):
  Planet_MoonRock01..07   moon rock scans: barren/rocky decor + asteroids (system fields, warp corridor)
  Planet_MossRocks01/02   mossy rock sets (jungle decor)
  Planet_Fern / Planet_Rooibos  fern_02 / wild_rooibos_bush (jungle ferns, desert dry bush; alpha map -> RGBA PNG)
Models (Sketchfab, CC BY 4.0):
  Planet_SpruceTrees     "Spruce Trees" by INT (conifer + two small spruces = rocky/ocean shrubs)
  Planet_Saguaro16/18/26 "Realistic HD Saguaro cactus (16/30, 18/30, 26/30)" by PlantCatalog (desert cactus)
  Planet_DeadTreeSrc     "Dead Tree" by MaX3Dd (volcanic dead tree)
  Planet_BasaltColumn    "Basalt column" by irs1182 (volcanic basalt, basalt organ landmark)
  Planet_DesertRock      "Desert Rock Formation, Makhtesh Ramon, Israel" by Doron Altaratz (hoodoo caps + segments)
  Planet_CrystalCluster01/02   "Crystal Cluster (Free)" / "Crystal Cluster 2 (Free)" by chrismartin1337 (crystal decor)
  Planet_IceCluster01..03      "Ice Cluster (free)", "Ice Cluster2 (free)", "Ice Cluster 4 (Free)" by chrismartin1337
  Planet_AlienPlant            "Alien Plant (Cydrakoniss)" by Blake Stahlbaum (jungle/toxic decor)
  Planet_AlienPlants           "Alien Plants" by Terran Orion (kit: fist fruit, potato, tree, be-mine plants)
  Planet_GlowMushrooms         "Glowing Mushrooms" by caue.oc (jungle decor, emissive)

Run headless from the repo root:
  ingest (once, from the downloaded files):
    blender -b --factory-startup -P Tools/Blender/planet_assets.py -- --ingest <dir>
      <dir>/tex/<polyhaven id>/<id>_<diff|nor_gl|arm>_2k.jpg, <dir>/acg/<ambientCG id>/<id>_2K-JPG.zip,
      <dir>/mod/<polyhaven id>/<id>_2k.gltf + .bin + textures/
    blender -b --factory-startup -P Tools/Blender/planet_assets.py -- --ingest-sketchfab <Downloads dir>
      <dir>/<stem>.zip (the Sketchfab glTF download)
      -> Assets/_Project/Art/Terrain/<Set>/<Set>_<baseColor|normal|arm|emissive>.jpg (2K, normal = OpenGL,
         arm = R ambient occlusion, G roughness, B metallic)
      -> Tools/Blender/Sources/Planet/<Name>.zip (scene.gltf + scene.bin + license.txt, no textures)
      -> Assets/_Project/Art/Models/Planet/<Name>/<material>_<baseColor|normal|metallicRoughness|emissive|ao>.jpg
         (glTF factors baked in; a blended/masked material keeps its alpha in <material>_baseColor.png)
  build (reproducible from the repo):
    blender -b --factory-startup -P Tools/Blender/planet_assets.py -- [--only Name]
      -> Assets/_Project/Art/Models/Planet/<Prop>.fbx (see BUILDS: Planet_Rock01..07, Planet_MossRock01..06,
         Planet_Crystal01/02, Planet_Ice01..03, Planet_AlienTree, Planet_FistPlant, Planet_MinePlant, Planet_AlienPlant,
         Planet_Mushroom01/02); materials keep the source names, the textures stay in the source's folder

Contract (AGENTS.md): 1 unit = 1 m, forward = Blender -Y (Unity +Z), up +Z.
"""
import sys
import os
import re
import json
import shutil
import tempfile
import zipfile
import math
import bpy
import numpy as np
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC_DIR = os.path.join(HERE, "Sources", "Planet")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Planet")
TERRAIN = os.path.join(ROOT, "Assets", "_Project", "Art", "Terrain")
MAX_TEX = 2048
LICENSE = "CC0 1.0 Universal (public domain). Source: %s\n"

# set name: (source, id)
TERRAIN_SETS = {
    "RockyTerrain02": ("polyhaven", "rocky_terrain_02"),
    "RockFace03": ("polyhaven", "rock_face_03"),
    "RedSand": ("polyhaven", "red_sand"),
    "SandstoneCracks": ("polyhaven", "sandstone_cracks"),
    "DarkRock": ("polyhaven", "dark_rock"),
    "DarkRock02": ("polyhaven", "dark_rock_02"),
    "MoonDusted02": ("polyhaven", "moon_dusted_02"),
    "MoonMeteor01": ("polyhaven", "moon_meteor_01"),
    "MossyRock": ("polyhaven", "mossy_rock"),
    "BrownMud": ("polyhaven", "brown_mud"),
    "CoastSand01": ("polyhaven", "coast_sand_01"),
    "Lava004": ("ambientcg", "Lava004"),
    "Ice002": ("ambientcg", "Ice002"),
    "Ice003": ("ambientcg", "Ice003"),
    "Snow006": ("ambientcg", "Snow006"),
    "Moss002": ("ambientcg", "Moss002"),
}

# model name: polyhaven id
MODELS = {"Planet_MoonRock%02d" % i: "moon_rock_%02d" % i for i in range(1, 8)}
MODELS.update({"Planet_MossRocks01": "rock_moss_set_01", "Planet_MossRocks02": "rock_moss_set_02",
               "Planet_Fern": "fern_02", "Planet_Rooibos": "wild_rooibos_bush"})

# model name: Sketchfab download stem
SKETCHFAB = {
    "Planet_CrystalCluster01": "crystal_cluster_free",
    "Planet_CrystalCluster02": "crystal_cluster_2_free",
    "Planet_IceCluster01": "ice_cluster_free",
    "Planet_IceCluster02": "ice_cluster2_free",
    "Planet_IceCluster03": "ice_cluster_4_free",
    "Planet_AlienPlant": "alien_plant_cydrakoniss",
    "Planet_AlienPlants": "alien_plants",
    "Planet_GlowMushrooms": "glowing_mushrooms",
    "Planet_SpruceTrees": "spruce_trees",
    "Planet_Saguaro16": "realistic_hd_saguaro_cactus_1630",
    "Planet_Saguaro18": "realistic_hd_saguaro_cactus_1830",
    "Planet_Saguaro26": "realistic_hd_saguaro_cactus_2630",
    "Planet_DeadTreeSrc": "dead_tree",
    "Planet_BasaltColumn": "basalt_column",
    "Planet_DesertRock": "desert_rock_formation_makhtesh_ramon_israel",
}


def arg(name):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else None


def safe(name):
    return re.sub(r"[^A-Za-z0-9_]+", "_", name).strip("_")


def source_url(kind, ident):
    return ("https://polyhaven.com/a/" + ident) if kind == "polyhaven" else ("https://ambientcg.com/view?id=" + ident)


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
    return px.reshape(h, w, 4)


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


def resample(px, h, w):
    sh, sw = px.shape[:2]
    if (sh, sw) == (h, w):
        return px
    ys = (np.arange(h) * sh // h).clip(0, sh - 1)
    xs = (np.arange(w) * sw // w).clip(0, sw - 1)
    return px[ys][:, xs]


# ---------------------------------------------------------------- ingest

def ingest_terrain(name, kind, ident, root):
    out = os.path.join(TERRAIN, name)
    os.makedirs(out, exist_ok=True)
    dst = lambda role: os.path.join(out, "%s_%s.jpg" % (name, role))
    if kind == "polyhaven":
        folder = os.path.join(root, "tex", ident)
        save_px(load_px(os.path.join(folder, ident + "_diff_2k.jpg"), False), dst("baseColor"), False)
        save_px(load_px(os.path.join(folder, ident + "_nor_gl_2k.jpg"), True), dst("normal"), True)
        save_px(load_px(os.path.join(folder, ident + "_arm_2k.jpg"), True), dst("arm"), True)
    else:
        tmp = tempfile.mkdtemp(prefix="vw_acg_")
        with zipfile.ZipFile(os.path.join(root, "acg", ident, ident + "_2K-JPG.zip")) as z:
            for n in z.namelist():
                if n.lower().endswith(".jpg") and "/" not in n:
                    z.extract(n, tmp)
        f = lambda m: os.path.join(tmp, "%s_2K-JPG_%s.jpg" % (ident, m))
        save_px(load_px(f("Color"), False), dst("baseColor"), False)
        save_px(load_px(f("NormalGL"), True), dst("normal"), True)
        rough = load_px(f("Roughness"), True)
        h, w = rough.shape[:2]
        arm = np.zeros_like(rough)
        arm[..., 0] = resample(load_px(f("AmbientOcclusion"), True), h, w)[..., 0] if os.path.exists(f("AmbientOcclusion")) else 1.0
        arm[..., 1] = rough[..., 0]
        save_px(arm, dst("arm"), True)
        if os.path.exists(f("Emission")):
            save_px(load_px(f("Emission"), False), dst("emissive"), False)
        shutil.rmtree(tmp, ignore_errors=True)
    print("[planet] terrain:", name)


def ingest_model(name, ident, root):
    folder = os.path.join(root, "mod", ident)
    gltf = os.path.join(folder, ident + "_2k.gltf")
    g = json.load(open(gltf, encoding="utf-8"))
    bins = [b["uri"] for b in g["buffers"]]
    assert len(bins) == 1, name
    # the source keeps the mesh only: buffer renamed to scene.bin, images dropped (they live in the texture folder)
    images = g.pop("images", [])
    textures = g.pop("textures", [])
    materials = g.get("materials", [])
    g["buffers"][0]["uri"] = "scene.bin"
    for m in materials:
        for k in ("normalTexture", "occlusionTexture", "emissiveTexture"):
            m.pop(k, None)
        pbr = m.get("pbrMetallicRoughness", {})
        pbr.pop("baseColorTexture", None)
        pbr.pop("metallicRoughnessTexture", None)
    g.pop("samplers", None)
    os.makedirs(SRC_DIR, exist_ok=True)
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        z.writestr("scene.gltf", json.dumps(g, indent=1))
        z.write(os.path.join(folder, bins[0]), "scene.bin")
        z.writestr("license.txt", LICENSE % source_url("polyhaven", ident))

    src = json.load(open(gltf, encoding="utf-8"))

    def uri(info):
        if not info:
            return None
        s = src["textures"][info["index"]].get("source")
        return os.path.join(folder, src["images"][s]["uri"]) if s is not None else None

    tex_out = os.path.join(OUT, name)
    os.makedirs(tex_out, exist_ok=True)
    for m in src.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        roles = {"baseColor": uri(pbr.get("baseColorTexture")), "normal": uri(m.get("normalTexture")),
                 "metallicRoughness": uri(pbr.get("metallicRoughnessTexture"))}
        mname = safe(m["name"])
        for role, path in roles.items():
            if not path or not os.path.exists(path):
                continue
            data = role != "baseColor"
            px = load_px(path, data)
            alpha = os.path.join(folder, "textures", "%s_alpha_2k.png" % ident)
            if role == "baseColor" and m.get("alphaMode", "OPAQUE") != "OPAQUE" and os.path.exists(alpha):
                # Poly Haven foliage: the cut-out lives in its own map -> one RGBA base colour (PNG = alpha clip in Unity)
                a = resample(load_px(alpha, True), px.shape[0], px.shape[1])
                px[..., 3] = a[..., 0]
                h, w = px.shape[:2]
                img = bpy.data.images.new(mname + "_baseColor", w, h, alpha=True)
                img.pixels.foreach_set(np.clip(px, 0, 1).astype(np.float32).ravel())
                img.filepath_raw = os.path.join(tex_out, "%s_baseColor.png" % mname)
                img.file_format = 'PNG'
                img.save()
                bpy.data.images.remove(img)
                continue
            if role == "metallicRoughness":
                # moon rocks ship an ARM map, the moss sets a grey roughness map; both rely on metallicFactor 0,
                # so the packed map gets G = roughness, B = the factor (Unity reads B as metal)
                px[..., 2] = pbr.get("metallicFactor", 1.0)
                px[..., 0] = 0.0
            save_px(px, os.path.join(tex_out, "%s_%s.jpg" % (mname, role)), data)
        print("[planet] model %s material %s %s" % (name, mname, sorted(k for k, v in roles.items() if v)))


def flat(rgba):
    px = np.ones((4, 4, 4), dtype=np.float32)
    px[..., :len(rgba)] = rgba
    return px


def ingest_sketchfab(name, stem, downloads):
    z = os.path.join(downloads, stem + ".zip")
    if not os.path.exists(z):
        print("[planet] ingest: missing", z)
        return
    tmp = tempfile.mkdtemp(prefix="vw_sf_")
    with zipfile.ZipFile(z) as src:
        for n in src.namelist():
            base = os.path.basename(n)
            if base in ("scene.gltf", "scene.bin", "license.txt") or base.lower().endswith((".png", ".jpg", ".jpeg")):
                src.extract(n, tmp)
    os.makedirs(SRC_DIR, exist_ok=True)
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as dst:
        for n in ("scene.gltf", "scene.bin", "license.txt"):
            dst.write(os.path.join(tmp, n), n)
    g = json.load(open(os.path.join(tmp, "scene.gltf"), encoding="utf-8"))

    def image(info, data):
        if not info:
            return None
        s = g["textures"][info["index"]].get("source")
        path = os.path.join(tmp, g["images"][s]["uri"].replace("%20", " ")) if s is not None else None
        return load_px(path, data) if path and os.path.exists(path) else None

    tex_out = os.path.join(OUT, name)
    os.makedirs(tex_out, exist_ok=True)
    for m in g.get("materials", []):
        mname = safe(m["name"])
        pbr = m.get("pbrMetallicRoughness", {})
        dst = lambda role, ext="jpg": os.path.join(tex_out, "%s_%s.%s" % (mname, role, ext))
        # base colour x factor; a blended/masked material keeps its alpha (PNG)
        base = image(pbr.get("baseColorTexture"), False)
        factor = np.array(pbr.get("baseColorFactor", [1, 1, 1, 1]), dtype=np.float32)
        base = flat(factor) if base is None else base * factor
        if m.get("alphaMode", "OPAQUE") != "OPAQUE" and base[..., 3].min() < 0.98:
            h, w = base.shape[:2]
            img = bpy.data.images.new(mname + "_baseColor", w, h, alpha=True)
            img.pixels.foreach_set(np.clip(base, 0, 1).astype(np.float32).ravel())
            img.filepath_raw = dst("baseColor", "png")
            img.file_format = 'PNG'
            img.save()
            bpy.data.images.remove(img)
        else:
            save_px(base, dst("baseColor"), False)
        # metallicRoughness x factors (G roughness, B metallic), flat from the factors without a texture
        mr = image(pbr.get("metallicRoughnessTexture"), True)
        rf, mf = pbr.get("roughnessFactor", 1.0), pbr.get("metallicFactor", 1.0)
        if mr is None:
            mr = flat((0.0, rf, mf, 1.0))
        else:
            mr[..., 0] = 0.0
            mr[..., 1] *= rf
            mr[..., 2] *= mf
        save_px(mr, dst("metallicRoughness"), True)
        nrm = image(m.get("normalTexture"), True)
        if nrm is not None:
            save_px(nrm, dst("normal"), True)
        emi = image(m.get("emissiveTexture"), False)
        ef = np.array(m.get("emissiveFactor", [0, 0, 0]) + [1], dtype=np.float32)
        if emi is not None or ef[:3].max() > 0:
            emi = flat(ef) if emi is None else emi * ef
            save_px(emi, dst("emissive"), False)
        occ = m.get("occlusionTexture")
        if occ and occ.get("index") != (pbr.get("metallicRoughnessTexture") or {}).get("index"):
            save_px(image(occ, True), dst("ao"), True)
        print("[planet] sketchfab %s material %s %s" % (name, mname, m.get("alphaMode", "OPAQUE")))
    shutil.rmtree(tmp, ignore_errors=True)


# ---------------------------------------------------------------- build (FBX export)
#
# Every prop is exported normalized so the game scales it by one number:
#   rocks   origin = bounds centre, largest half-extent = 1 m (radius 1); decor sinks them, asteroids use them as is
#   others  origin = bottom centre, height = 1 m
# Budgets are per instance: decor chunks combine many of them, asteroid fields up to a few hundred.

def import_source(name):
    tmp = tempfile.mkdtemp(prefix="vw_src_")
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip")) as z:
        z.extractall(tmp)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(tmp, "scene.gltf"))
    shutil.rmtree(tmp, ignore_errors=True)
    for m in bpy.data.materials:
        m.name = safe(m.name)            # material names = texture prefixes (ingest writes safe names)


def meshes(match=None):
    return [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons)
            and (match is None or any(k in o.name for k in match))]


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def bounds(obj):
    pts = np.array([v.co[:] for v in obj.data.vertices])
    return Vector(pts.min(axis=0)), Vector(pts.max(axis=0))


def bake(objs, name):
    """Applies parent/object transforms, joins `objs` into one object and deletes everything else."""
    for o in bpy.context.scene.objects:
        o.animation_data_clear()
    for o in objs:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
        if o.data.users > 1:
            o.data = o.data.copy()
        activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(objs) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
    obj = objs[0]
    obj.name = obj.data.name = name
    for o in [o for o in bpy.context.scene.objects if o != obj]:
        bpy.data.objects.remove(o, do_unlink=True)
    return obj


def decimate(obj, target):
    ratio = min(1.0, target / max(1, tris(obj)))
    if ratio < 1.0:
        m = obj.modifiers.new("dec", 'DECIMATE')
        m.ratio = ratio
        m.use_collapse_triangulate = True
        activate(obj)
        bpy.ops.object.modifier_apply(modifier=m.name)


def normalize(obj, rock):
    lo, hi = bounds(obj)
    ext = hi - lo
    c = (lo + hi) * 0.5
    if rock:
        s = 2.0 / max(ext)
    else:
        s = 1.0 / ext.z
        c.z = lo.z
    obj.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation(-c))
    obj.data.update()


def smooth(obj, angle):
    activate(obj)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))


# foliage materials (alpha cards): their normals are bent outwards + up from the plant's axis, the usual foliage
# trick, so cards lit from behind do not turn black and a bush reads as one soft volume
FOLIAGE = ("branch", "leaves", "twigs", "fern", "spines", "Leaves")


def foliage_normals(obj):
    mesh = obj.data
    mats = [m.name if m else "" for m in mesh.materials]
    if not any(any(k in n for k in FOLIAGE) for n in mats):
        return
    lo, hi = bounds(obj)
    height = max(hi.z - lo.z, 1e-4)
    normals = [Vector(n.vector) for n in mesh.corner_normals]
    for poly in mesh.polygons:
        if not any(k in mats[poly.material_index] for k in FOLIAGE):
            continue
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            out = Vector((co.x, co.y, (co.z - lo.z - height * 0.35) * 0.5))
            if out.length < 1e-5:
                out = Vector((0, 0, 1))
            normals[li] = (out.normalized() * 0.7 + Vector((0, 0, 0.6))).normalized()
    mesh.normals_split_custom_set(normals)


def export(obj, name):
    activate(obj)
    bpy.ops.object.material_slot_remove_unused()
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False, path_mode='STRIP')
    lo, hi = bounds(obj)
    print("[planet] %s size (unity x/y/z) %.2f %.2f %.2f  y %.2f..%.2f  tris %d  mats %s" % (
        name, hi.x - lo.x, hi.z - lo.z, hi.y - lo.y, lo.z, hi.z, tris(obj), [m.name for m in obj.data.materials if m]))


def centre_x(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return sum(p.x for p in pts) / 8.0


def prop(src, out, match, target, rock, angle=60.0, drop=None):
    """
    One exported prop from the objects of `src` whose name contains any of `match` (None = all; a ("x", lo, hi)
    tuple = objects whose bounds centre lies in that Blender x range, for packs laid out side by side).
    """
    import_source(src)
    if isinstance(match, tuple):
        objs = [o for o in meshes() if match[1] <= centre_x(o) <= match[2]]
    else:
        objs = meshes(match)
    objs = [o for o in objs if not (drop and any(k in o.name for k in drop))]
    obj = bake(objs, out)
    decimate(obj, target)
    normalize(obj, rock)
    smooth(obj, angle)
    foliage_normals(obj)
    export(obj, out)


# name: (source, objects (name contains), triangle budget, rock?, smoothing angle, dropped objects)
BUILDS = {}
for i in range(1, 8):
    # moon rock 01 ships LODs: start from LOD2 (2.5k)
    BUILDS["Planet_Rock%02d" % i] = ("Planet_MoonRock%02d" % i, ["LOD2"] if i == 1 else None, 900, True, 60.0, None)
for i in range(1, 7):
    BUILDS["Planet_MossRock%02d" % i] = ("Planet_MossRocks01", ["rock%02d" % i], 900, True, 60.0, None)
BUILDS.update({
    "Planet_Crystal01": ("Planet_CrystalCluster01", None, 2500, False, 30.0, None),
    "Planet_Crystal02": ("Planet_CrystalCluster02", None, 2500, False, 30.0, None),
    "Planet_Ice01": ("Planet_IceCluster01", None, 1500, False, 30.0, None),
    "Planet_Ice02": ("Planet_IceCluster02", None, 1500, False, 30.0, None),
    "Planet_Ice03": ("Planet_IceCluster03", None, 1500, False, 30.0, None),
    # the kit's tree: eight trunks under one canopy (20 m wide in the source), its branches
    "Planet_AlienTree": ("Planet_AlienPlants", ["Trunk_", "Branch_", "Canopy_"], 3000, False, 45.0, None),
    "Planet_FistPlant": ("Planet_AlienPlants", ["FruitLeaves_", "AlienFruit_", "AlienFist_"], 2500, False, 45.0, None),
    "Planet_MinePlant": ("Planet_AlienPlants", ["MineBulb_", "MineStem_"], 1200, False, 45.0, None),
    # Cydrakoniss without its ground disc (Cylinder001)
    "Planet_AlienPlant": ("Planet_AlienPlant", None, 4500, False, 45.0, ["Cylinder001"]),
    "Planet_Mushroom01": ("Planet_GlowMushrooms", ["TallMush1_"], 2000, False, 45.0, None),
    "Planet_Mushroom02": ("Planet_GlowMushrooms", ["TallMush2_"], 2000, False, 45.0, None),
    # the spruce pack: the tall tree in the middle, two small spruces (cross cards) on the left = shrubs
    "Planet_Spruce": ("Planet_SpruceTrees", ("x", -22.0, 30.0), 4000, False, 45.0, None),
    "Planet_SpruceBush01": ("Planet_SpruceTrees", ("x", -46.0, -36.0), 1000, False, 45.0, None),
    "Planet_SpruceBush02": ("Planet_SpruceTrees", ("x", -34.0, -24.0), 1000, False, 45.0, None),
    # saguaro scans keep their spine cards, except the 22k one with the arm (bark only)
    "Planet_Saguaro01": ("Planet_Saguaro16", None, 8000, False, 60.0, None),
    "Planet_Saguaro02": ("Planet_Saguaro26", None, 8000, False, 60.0, None),
    "Planet_Saguaro03": ("Planet_Saguaro18", ["Carneg"], 5000, False, 60.0, None),
    "Planet_DeadTree": ("Planet_DeadTreeSrc", None, 3300, False, 45.0, None),
    "Planet_Basalt": ("Planet_BasaltColumn", None, 1200, False, 30.0, None),
    "Planet_DesertRock01": ("Planet_DesertRock", None, 2000, True, 60.0, None),
    "Planet_Fern01": ("Planet_Fern", ["fern_02_b"], 2400, False, 60.0, None),
    "Planet_Fern02": ("Planet_Fern", ["fern_02_c"], 2400, False, 60.0, None),
    "Planet_Fern03": ("Planet_Fern", ["fern_02_a"], 2400, False, 60.0, None),
    "Planet_Fern04": ("Planet_Fern", ["fern_02_d"], 2400, False, 60.0, None),
    "Planet_Rooibos01": ("Planet_Rooibos", ["wild_rooibos_bush_c"], 2500, False, 60.0, None),
    "Planet_Rooibos02": ("Planet_Rooibos", ["wild_rooibos_bush_d"], 2500, False, 60.0, None),
    "Planet_Rooibos03": ("Planet_Rooibos", ["wild_rooibos_bush_e"], 2500, False, 60.0, None),
})

# the texture folder each built prop reads its materials from (the ingest folder of its source)
TEXTURE_SET = {name: spec[0] for name, spec in BUILDS.items()}


# ---------------------------------------------------------------- main

if "--ingest-sketchfab" in ARGS:
    only = arg("--only")
    for name, stem in SKETCHFAB.items():
        if not only or name == only:
            ingest_sketchfab(name, stem, arg("--ingest-sketchfab"))
elif "--ingest" in ARGS:
    root = arg("--ingest")
    only = arg("--only")
    for name, (kind, ident) in TERRAIN_SETS.items():
        if not only or name == only:
            ingest_terrain(name, kind, ident, root)
    for name, ident in MODELS.items():
        if not only or name == only:
            ingest_model(name, ident, root)
else:
    os.makedirs(OUT, exist_ok=True)
    only = arg("--only")
    for name, (src, match, target, rock, angle, drop) in BUILDS.items():
        if only and name != only:
            continue
        if not os.path.exists(os.path.join(SRC_DIR, src + ".zip")):
            print("[planet] skip (no source):", name)
            continue
        prop(src, name, match, target, rock, angle, drop)
