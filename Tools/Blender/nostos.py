"""
Home ship Nostos (#49, the SELENE-4 mission ship; the Ark hull of #97 grown into a long-range transport) from free
Sketchfab models (CC BY 4.0, see Assets/_Project/Art/CREDITS.md):
  hull      "SPACESHIP - CARGO" by Kerem Kavalci        https://sketchfab.com/3d-models/spaceship-cargo-e9faa887c3684271b67be00649e11dcf
  interior  "Sci-Fi Corridor - Revisited 2019" by Robert Berrier (Mesh Masters)
            https://sketchfab.com/3d-models/sci-fi-corridor-revisited-2019-e3bc70aa659a4459b8dc29bd563aff08
  cryo pod  "Cryo LOD0" by Nikitos & 3130              https://sketchfab.com/3d-models/cryo-lod0-4e30f902d6434379832ad80da5317eec

Run headless from the repo root:
  ingest (once, from the Sketchfab glTF zips the user downloaded):
    blender -b --factory-startup -P Tools/Blender/nostos.py -- --ingest <Downloads dir>
      -> Tools/Blender/Sources/Nostos/<Name>.zip (scene.gltf/bin + license.txt, no textures)
      -> Assets/_Project/Art/Models/Ships/<Name>/<material>_<baseColor|normal|metallicRoughness|emissive>.jpg (<= 2K)
  build (reproducible from the repo):
    blender -b --factory-startup -P Tools/Blender/nostos.py -- [--preview dir] [--analyze]

The hull source is rigged (parts parented to an armature); the posed parts are baked, joined into one hull with the
original UVs and normalized to the Voidward contract (1 unit = 1 m, nose on Blender -Y = Unity +Z, origin at the bounds
centre) at LENGTH (1.5 x the Ark's 36 m): the fuselage then holds the corridor kit (4.4 m wide, 3.2 m high) and the cab
a roomy bridge. Hull faces inside the walkable volume (internal structure of the source) are removed, the window is cut
out and an airlock door is cut into the left flank between the wings.
Outputs (Assets/_Project/Art/Models/Ships):
  - Ship_Nostos.fbx           hull without the bridge window and internal faces, with the airlock door in the left flank
  - Ship_Nostos_Glass.fbx     the cab's front window (dark painted glass in the source texture) as its own mesh
  - Nostos_Hall.fbx           the corridor kit as the cargo hall: both walls (the source models one side), open doorways,
                              the airlock opening cut into the left wall; origin = floor centre (walkway at y 0)
  - Nostos_CryoPod.fbx        the cryo pod, origin = bottom centre, HALL/POD sizes printed for PrefabBuilder
Prints cross-sections (Unity coordinates) used to place the rooms, seats, clamps and terminals in PrefabBuilder.
Blender (x, y, z) = Unity (-x, z, -y) below; U(...) converts.
"""
import sys
import os
import json
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
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Ships")
SRC = os.path.join(HERE, "Sources", "CargoShip", "CargoShip.fbx")
SRC_DIR = os.path.join(HERE, "Sources", "Nostos")
TEX = os.path.join(OUT, "CargoShip", "CargoShip_Base_color.jpg")
MAX_TEX = 2048
S = 1.5                 # the Nostos is the Ark hull at 1.5 x
LENGTH = 36.0 * S

# download zip stem per kit asset
SKIP_MATERIALS = {"Doorway_door"}   # the hall's doors are removed (open doorways)
KIT = {"Nostos_Hall": "sci-fi_corridor_-_revisited_2019", "Nostos_CryoPod": "cryo_lod0"}


def arg(name):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else None


def U(v):
    return Vector((-v.x, v.z, -v.y))


def B(u):
    return Vector((-u.x, -u.z, u.y))


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])


def export(obj, name):
    vw.activate(obj)
    if obj.data.materials:
        bpy.ops.object.material_slot_remove_unused()
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False, path_mode='STRIP')
    lo, hi = bounds([obj])
    print("[nostos] %s size (unity x/y/z) %.2f %.2f %.2f  y %.2f..%.2f  tris %d  mats %s" % (
        name, hi.x - lo.x, hi.z - lo.z, hi.y - lo.y, lo.z, hi.z, tris(obj), [m.name for m in obj.data.materials if m]))


# ================================================================ ingest

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


def ingest(downloads):
    for name, stem in KIT.items():
        z = os.path.join(downloads, stem + ".zip")
        if not os.path.exists(z):
            print("[nostos] ingest: missing", z)
            continue
        tmp = tempfile.mkdtemp(prefix="vw_ingest_")
        with zipfile.ZipFile(z) as src:
            for n in src.namelist():
                base = os.path.basename(n)
                if base in ("scene.gltf", "scene.bin", "license.txt") or base.lower().endswith((".png", ".jpg", ".jpeg")):
                    src.extract(n, tmp)
        os.makedirs(SRC_DIR, exist_ok=True)
        with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as dst:
            for n in ("scene.gltf", "scene.bin", "license.txt"):
                if os.path.exists(os.path.join(tmp, n)):
                    dst.write(os.path.join(tmp, n), n)
        tex_out = os.path.join(OUT, name)
        os.makedirs(tex_out, exist_ok=True)
        g = json.load(open(os.path.join(tmp, "scene.gltf"), encoding="utf-8"))
        images, textures = g.get("images", []), g.get("textures", [])

        def uri(info):
            if not info:
                return None
            src = textures[info["index"]].get("source")
            return os.path.join(tmp, images[src]["uri"].replace("%20", " ")) if src is not None else None

        for m in g.get("materials", []):
            pbr = m.get("pbrMetallicRoughness", {})
            roles = {"baseColor": uri(pbr.get("baseColorTexture")), "metallicRoughness": uri(pbr.get("metallicRoughnessTexture")),
                     "normal": uri(m.get("normalTexture")), "emissive": uri(m.get("emissiveTexture"))}
            if m["name"] in SKIP_MATERIALS:
                continue
            for role, path in roles.items():
                if path and os.path.exists(path):
                    data = role not in ("baseColor", "emissive")
                    save_px(load_px(path, data), os.path.join(tex_out, "%s_%s.jpg" % (m["name"], role)), data)
            if not roles["baseColor"]:
                # untextured material (the kit's pipe frame): a flat dark gunmetal
                px = np.ones((4, 4, 4), dtype=np.float32) * 0.18
                save_px(px, os.path.join(tex_out, "%s_baseColor.jpg" % m["name"]), False)
        shutil.rmtree(tmp, ignore_errors=True)
        print("[nostos] ingest:", name)


def import_kit(name):
    tmp = tempfile.mkdtemp(prefix="vw_src_")
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip")) as z:
        z.extractall(tmp)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(tmp, "scene.gltf"))
    shutil.rmtree(tmp, ignore_errors=True)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons)]
    for o in objs:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']:
        bpy.data.objects.remove(o)
    for o in objs:
        if o.data.users > 1:
            o.data = o.data.copy()
        vw.activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return objs


# ================================================================ hull (the Ark hull at S)

FLOOR_Y = -1.55 * S
ROOMS = [  # (x half width, y min, y max, z min, z max) of the inner volume, Unity coordinates
    (1.62 * S, FLOOR_Y - 0.08, 0.76 * S, -8.6 * S, 6.6 * S),   # corridor (fuselage)
    (1.36 * S, FLOOR_Y - 0.08, 1.6 * S, 6.6 * S, 15.7 * S),    # bridge (cab)
]
DOOR = (-2.5 * S, -0.5 * S, 0.55 * S)  # airlock in the left flank: z range, top (sill = FLOOR_Y)


def in_rooms(u, margin=0.0):
    return any(abs(u.x) < hx - margin and y0 + margin < u.y < y1 - margin and z0 + margin < u.z < z1 - margin
               for hx, y0, y1, z0, z1 in ROOMS)


def build_hull(preview):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    parts = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons)]
    for o in [o for o in bpy.context.scene.objects if o.type == 'MESH' and not len(o.data.polygons)]:
        bpy.data.objects.remove(o)
    for o in parts:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']:
        bpy.data.objects.remove(o)
    for o in parts:
        for m in list(o.modifiers):
            o.modifiers.remove(m)
        vw.activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    hull = vw.join(parts, "Ship_Nostos")
    lo, hi = bounds([hull])
    k = LENGTH / (hi.y - lo.y)
    centre = (lo + hi) * 0.5
    for v in hull.data.vertices:
        v.co = (v.co - centre) * k
    hull.data.update()
    lo, hi = bounds([hull])
    print(f"[nostos] scale {k:.4f} size (unity x/y/z) {hi.x - lo.x:.2f} {hi.z - lo.z:.2f} {hi.y - lo.y:.2f}")

    def cast(o_, d_):
        hit, loc, *_ = hull.ray_cast(B(Vector(o_)), B(Vector(d_)).normalized())
        return U(loc) if hit else None

    img = bpy.data.images.load(TEX)
    tw, th = img.size
    px = np.array(img.pixels[:]).reshape(th, tw, 4)

    def texel(f, uv):
        cs = [px[int((l[uv].uv.y % 1) * (th - 1)), int((l[uv].uv.x % 1) * (tw - 1))] for l in f.loops]
        c = sum(cs) / len(cs)
        return c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11

    if "--analyze" in ARGS:
        for z in range(-27, 28, 2):
            f = lambda p, c: f"{getattr(p, c):+6.2f}" if p else "  none"
            print(f"[nostos] z {z:+3d}: top {f(cast((0, 40, z), (0, -1, 0)), 'y')} bottom {f(cast((0, -40, z), (0, 1, 0)), 'y')} "
                  f"side {f(cast((40, FLOOR_Y + 1.5, z), (-1, 0, 0)), 'x')} | roof {f(cast((0, 0.3, z), (0, 1, 0)), 'y')} "
                  f"floor {f(cast((0, 0.3, z), (0, -1, 0)), 'y')} wall {f(cast((0, 0.3, z), (1, 0, 0)), 'x')}")
        for x in (3, 5, 7, 9, 11, 13):
            for z in (-20, -16, -10, -4, 2, 8, 12, 16):
                t = cast((x, 40, z), (0, -1, 0))
                b = cast((x, -40, z), (0, 1, 0))
                print(f"[nostos] x {x:+3d} z {z:+3d}: top {t.y if t else 0:+6.2f} bottom {b.y if b else 0:+6.2f}")

    bm = bmesh.new()
    bm.from_mesh(hull.data)
    uv = bm.loops.layers.uv.active
    # 1. internal faces: every vertex inside the volume, or big internal planes whose centre is inside it
    inner = [f for f in bm.faces if all(in_rooms(U(v.co)) for v in f.verts) or in_rooms(U(f.calc_center_median()), 0.05)
             or any(in_rooms(U(v.co), 0.15 * S) for v in f.verts)]
    bmesh.ops.delete(bm, geom=inner, context='FACES')
    print(f"[nostos] removed {len(inner)} internal faces")

    # 2. bridge window: the dark sloped panel at the cab front (painted glass)
    glass = set()
    for f in bm.faces:
        u, n = U(f.calc_center_median()), U(f.normal)
        if u.z > 15.0 * S and -0.9 * S < u.y < 1.6 * S and abs(u.x) < 1.6 * S and n.z > 0.25 and n.y > 0.05 and texel(f, uv) < 0.235:
            glass.add(f)
    patches, seen = [], set()
    for f in glass:
        if f in seen:
            continue
        stack, isl = [f], []
        seen.add(f)
        while stack:
            g = stack.pop()
            isl.append(g)
            for e in g.edges:
                for o in e.link_faces:
                    if o in glass and o not in seen:
                        seen.add(o)
                        stack.append(o)
        patches.append(isl)
    window = max(patches, key=lambda p: sum(f.calc_area() for f in p)) if patches else []
    print(f"[nostos] window faces {len(window)} area {sum(f.calc_area() for f in window):.2f} m2 ({len(patches)} patches)")

    # 3. airlock door in the left flank: cut the side skin along the door rectangle
    side = [f for f in bm.faces if U(f.calc_center_median()).x < -1.4 * S and U(f.normal).x < -0.5
            and DOOR[0] - 1.5 * S < U(f.calc_center_median()).z < DOOR[1] + 1.5 * S and -2.6 * S < U(f.calc_center_median()).y < 1.0 * S]
    cut_geom = list({e for f in side for e in f.edges}) + side + list({v for f in side for v in f.verts})
    for co, no in ((B(Vector((0, 0, DOOR[0]))), B(Vector((0, 0, 1)))), (B(Vector((0, 0, DOOR[1]))), B(Vector((0, 0, 1)))),
                   (B(Vector((0, FLOOR_Y, 0))), B(Vector((0, 1, 0)))), (B(Vector((0, DOOR[2], 0))), B(Vector((0, 1, 0))))):
        res = bmesh.ops.bisect_plane(bm, geom=cut_geom, plane_co=co, plane_no=no)
        cut_geom = list({g for g in cut_geom + res["geom"] if g.is_valid})
    door = [f for f in bm.faces if U(f.normal).x < -0.5 and U(f.calc_center_median()).x < -1.4 * S
            and DOOR[0] < U(f.calc_center_median()).z < DOOR[1] and FLOOR_Y < U(f.calc_center_median()).y < DOOR[2]]
    bmesh.ops.delete(bm, geom=door, context='FACES')
    dx = [U(v.co).x for f in side if f.is_valid for v in f.verts]
    print(f"[nostos] airlock door: removed {len(door)} faces, flank skin at x {min(dx) if dx else 0:+.2f}..{max(dx) if dx else 0:+.2f}")

    window_idx = {f.index for f in window if f.is_valid}
    bm.to_mesh(hull.data)
    bm.free()
    hull.data.update()

    vw.activate(hull)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in hull.data.polygons:
        p.select = p.index in window_idx
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    glass_obj = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o != hull][0]
    glass_obj.name = glass_obj.data.name = "Ship_Nostos_Glass"
    gb = [U(v.co) for v in glass_obj.data.vertices]
    print(f"[nostos] window bounds x {min(p.x for p in gb):+.2f}..{max(p.x for p in gb):+.2f} y {min(p.y for p in gb):+.2f}..{max(p.y for p in gb):+.2f} "
          f"z {min(p.z for p in gb):+.2f}..{max(p.z for p in gb):+.2f}")

    # numbers for PrefabBuilder (outer skin per section, spine, belly, wing tops for the docking clamps)
    for z in range(-26, 27, 2):
        t = cast((0, 40, z), (0, -1, 0))
        b = cast((0, -40, z), (0, 1, 0))
        sd = cast((40, FLOOR_Y + 1.5, z), (-1, 0, 0))
        print(f"[nostos] section z {z:+3d}: outer top {t.y if t else 0:+.2f} bottom {b.y if b else 0:+.2f} side {sd.x if sd else 0:.2f}")
    for x in (4, 6, 8, 10, 12, 14):
        for z in (-22, -18, -14, -8, 4, 8, 12, 16):
            t = cast((x, 40, z), (0, -1, 0))
            print(f"[nostos] top at x {x:+3d} z {z:+3d}: {t.y if t else float('nan'):+.2f}")
    for o in (hull, glass_obj):
        vw.export_fbx(o, os.path.join(OUT, o.name + ".fbx"))
        print("[nostos] exported", o.name)
    if preview:
        render(preview, "Ship_Nostos", [hull, glass_obj])


# ================================================================ interior kit

HALL_HEIGHT = 3.2       # walkway to ceiling (m): the kit fits under the fuselage roof (outer top 1.41, deck -2.33)
HALL_BELOW = 0.45       # what hangs deeper under the walkway is clipped (belly skin at -2.89)


def build_hall(preview):
    objs = import_kit("Nostos_Hall")
    # the doors go (open doorways to the engine room and the cryo bay)
    for o in [o for o in objs if any(m and m.name == "Doorway_door" for m in o.data.materials)]:
        objs.remove(o)
        bpy.data.objects.remove(o)
    # the source models one wall (and its columns) only: mirror them to the other side
    for o in list(objs):
        if any(m and m.name in ("Wall", "Column_B") for m in o.data.materials):
            c = o.copy()
            c.data = o.data.copy()
            bpy.context.scene.collection.objects.link(c)
            c.data.transform(Matrix.Scale(-1, 4, Vector((1, 0, 0))))
            c.data.flip_normals()
            c.data.update()
            objs.append(c)
    hall = vw.join(objs, "Nostos_Hall")
    lo, hi = bounds([hall])
    # walkway height: the floor under the centre line; ceiling straight above it
    cx, cy = (lo.x + hi.x) * 0.5, (lo.y + hi.y) * 0.5
    hit, loc, *_ = hall.ray_cast(Vector((cx, cy, (lo.z + hi.z) * 0.5)), Vector((0, 0, -1)))
    floor = loc.z if hit else lo.z
    hit, loc, *_ = hall.ray_cast(Vector((cx + (hi.x - lo.x) * 0.1, cy, floor + 1.0)), Vector((0, 0, 1)))
    ceiling = loc.z if hit else hi.z
    k = HALL_HEIGHT / (ceiling - floor)
    hall.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-Vector((cx, cy, floor))))
    hall.data.update()
    # the pipe trenches reach far below the deck: clip them at the fuselage belly
    bm = bmesh.new()
    bm.from_mesh(hall.data)
    low = [f for f in bm.faces if all(v.co.z < -HALL_BELOW for v in f.verts)]
    bmesh.ops.delete(bm, geom=low, context='FACES')
    bm.to_mesh(hall.data)
    bm.free()
    hall.data.update()
    lo, hi = bounds([hall])
    print(f"[nostos] hall scale {k:.5f} (source floor {floor:.1f} ceiling {ceiling:.1f}) size {hi.x - lo.x:.2f} x {hi.y - lo.y:.2f}, "
          f"y {lo.z:.2f}..{hi.z:.2f}, clipped {len(low)} faces below")
    for z in (0.05, 0.3, 1.0, 1.6, 2.2, 2.9):
        hl, ll, *_ = hall.ray_cast(Vector((0, 0.3, z)), Vector((-1, 0, 0)))
        hr, lr, *_ = hall.ray_cast(Vector((0, 0.3, z)), Vector((1, 0, 0)))
        print(f"[nostos] hall half width at y {z:.2f}: {(-ll.x) if hl else float('nan'):.2f} / {lr.x if hr else float('nan'):.2f}")
    hf, lf, *_ = hall.ray_cast(Vector((0, 0.3, 1.0)), Vector((0, 0, -1)))
    print(f"[nostos] hall walkway at y {lf.z if hf else float('nan'):.3f}")
    # the doorways at both ends: opening width/height (Unity: z = -Blender y)
    for end in (-1, 1):
        yy = end * (hi.y - 0.15)
        hl, ll, *_ = hall.ray_cast(Vector((0, yy, 1.0)), Vector((-1, 0, 0)))
        ht, lt, *_ = hall.ray_cast(Vector((0, yy, 0.5)), Vector((0, 0, 1)))
        print(f"[nostos] doorway at unity z {-yy:+.2f}: half width {(-ll.x) if hl else float('nan'):.2f} top {lt.z if ht else float('nan'):.2f}")

    # airlock opening in the left wall (Unity x < 0 = Blender x > 0): everything of the wall side inside the door box goes
    z0, z1 = DOOR[0] - HALL_Z, DOOR[1] - HALL_Z           # door z range relative to the hall centre (Unity)
    top = DOOR[2] - FLOOR_Y
    bm = bmesh.new()
    bm.from_mesh(hall.data)
    left = [f for f in bm.faces if f.calc_center_median().x > 0.9]
    geom = list({e for f in left for e in f.edges}) + left + list({v for f in left for v in f.verts})
    for co, no in (((0, -z0, 0), (0, 1, 0)), ((0, -z1, 0), (0, 1, 0)), ((0, 0, top), (0, 0, 1))):
        res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector(co), plane_no=Vector(no))
        geom = list({g for g in geom + res["geom"] if g.is_valid})
    cut = [f for f in bm.faces if f.calc_center_median().x > 0.9 and -z1 < f.calc_center_median().y < -z0
           and -HALL_BELOW < f.calc_center_median().z < top]
    bmesh.ops.delete(bm, geom=cut, context='FACES')
    bm.to_mesh(hall.data)
    bm.free()
    hall.data.update()
    print(f"[nostos] hall airlock cut: {len(cut)} faces (unity z {z0:+.2f}..{z1:+.2f} rel., top {top:.2f})")
    export(hall, "Nostos_Hall")
    if preview:
        render(preview, "Nostos_Hall", [hall], inside=True)


POD_HEIGHT = 2.35


def build_pod(preview):
    objs = import_kit("Nostos_CryoPod")
    pod = vw.join(objs, "Nostos_CryoPod") if len(objs) > 1 else objs[0]
    pod.name = pod.data.name = "Nostos_CryoPod"
    lo, hi = bounds([pod])
    k = POD_HEIGHT / (hi.z - lo.z)
    pod.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-Vector(((lo.x + hi.x) * 0.5, (lo.y + hi.y) * 0.5, lo.z))))
    pod.data.update()
    lo, hi = bounds([pod])
    # which way the glass faces: the open (shell-less) side has the fewest faces pointing that way
    counts = {}
    for p in pod.data.polygons:
        n = p.normal
        if abs(n.z) < 0.5:
            key = "+x" if n.x > 0.7 else "-x" if n.x < -0.7 else "+y" if n.y > 0.7 else "-y" if n.y < -0.7 else "o"
            counts[key] = counts.get(key, 0) + p.area
    print(f"[nostos] pod {hi.x - lo.x:.2f} x {hi.y - lo.y:.2f} x {hi.z - lo.z:.2f} side areas {counts}")
    export(pod, "Nostos_CryoPod")
    if preview:
        render(preview, "Nostos_CryoPod", [pod])


# ================================================================ preview

def render(out, name, objs, inside=False):
    import math
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.render.resolution_x, scene.render.resolution_y = 900, 560
    lo, hi = bounds(objs)
    c = (lo + hi) * 0.5
    r = max((hi - lo).length * 0.5, 0.1)
    cd = bpy.data.cameras.new("cam")
    cd.clip_start, cd.clip_end = 0.05, r * 20
    cam = bpy.data.objects.new("cam", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    views = [("a", Vector((0.8, -1.0, 0.55))), ("b", Vector((-0.9, 0.7, 0.35))), ("side", Vector((1.0, 0.0, 0.05)))]
    for tag, d in views:
        d.normalize()
        cam.location = c + d * r * 2.2
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, tag))
        bpy.ops.render.render(write_still=True)
    if inside:
        cd.lens = 16
        cam.location = Vector((0.0, hi.y - 0.6, 1.62))
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        scene.render.filepath = os.path.join(out, "%s_in.png" % name)
        bpy.ops.render.render(write_still=True)


# ================================================================ main

# the hall's centre along the ship (Unity z): between the engine room and the cryo bay (PrefabBuilder NostosHallZ)
HALL_Z = -1.5

if "--ingest" in ARGS:
    ingest(arg("--ingest"))
else:
    os.makedirs(OUT, exist_ok=True)
    prev = arg("--preview")
    only = arg("--only")
    if not only or only == "hull":
        build_hull(prev)
    if (not only or only == "hall") and os.path.exists(os.path.join(SRC_DIR, "Nostos_Hall.zip")):
        build_hall(prev)
    if (not only or only == "pod") and os.path.exists(os.path.join(SRC_DIR, "Nostos_CryoPod.zip")):
        build_pod(prev)
