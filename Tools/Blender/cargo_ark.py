"""
Ark (crew frigate, later the home ship Nostos #49) from the free Sketchfab model "SPACESHIP - CARGO" by Kerem Kavalci (#97):
https://sketchfab.com/3d-models/spaceship-cargo-e9faa887c3684271b67be00649e11dcf (CC BY 4.0, 53k tris, 4K PBR).

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/cargo_ark.py -- Assets/_Project/Art/Models/Ships [preview_dir]

The source is rigged (parts parented to an armature); the posed parts are baked, joined into one hull with the original
UVs, normalized to the Voidward contract (1 unit = 1 m, nose on Blender -Y = Unity +Z, origin at the bounds centre) and
kept at its own scale (LENGTH): the fuselage then is a real corridor (3.8 m wide, 2.9 m high) and the cab a bridge.
Hull faces inside the walkable volume (internal structure of the source) are removed, the window is cut out and an
airlock door is cut into the left flank between the wings.
Outputs:
  - Ship_Ark_Cargo.fbx         hull without the bridge window and internal faces, with the airlock door in the left flank
  - Ship_Ark_Cargo_Glass.fbx   the cab's front window (dark painted glass in the source texture) as its own mesh
Prints cross-sections (Unity coordinates) used to place the walkable interior, seats and terminals in PrefabBuilder.
Blender (x, y, z) = Unity (-x, z, -y) below; U(...) converts.
"""
import sys
import os
import bpy
import bmesh
import numpy as np
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Assets/_Project/Art/Models/Ships"
PREVIEW = ARGS[1] if len(ARGS) > 1 else None
ANALYZE = "--analyze" in ARGS
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(HERE, "Sources", "CargoShip", "CargoShip.fbx")
TEX = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Ships", "CargoShip", "CargoShip_Base_color.jpg")
LENGTH = 36.0
os.makedirs(OUT, exist_ok=True)


def U(v):
    return Vector((-v.x, v.z, -v.y))


def B(u):
    return Vector((-u.x, -u.z, u.y))


# ---------------------------------------------------------------- import, bake the pose, join, normalize
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
names = {o.name: o for o in parts}
hull = vw.join(parts, "Ship_Ark_Cargo")
pts = [v.co for v in hull.data.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)])
hi = Vector([max(p[i] for p in pts) for i in range(3)])
k = LENGTH / (hi.y - lo.y)
centre = (lo + hi) * 0.5
for v in hull.data.vertices:
    v.co = (v.co - centre) * k
hull.data.update()
size = (hi - lo) * k
print(f"[ark] scale {k:.4f} size (unity x/y/z) {size.x:.2f} {size.z:.2f} {size.y:.2f}")


def cast(o_, d_, obs=None):
    best = None
    for ob in (obs or [hull]):
        hit, loc, *_ = ob.ray_cast(B(Vector(o_)), B(Vector(d_)).normalized())
        if hit and (best is None or (U(loc) - Vector(o_)).length < (best - Vector(o_)).length):
            best = U(loc)
    return best


def sections(label, obs=None, y0=0.2):
    for z in range(-18, 19):
        top = cast((0, 30, z), (0, -1, 0), obs)
        bot = cast((0, -30, z), (0, 1, 0), obs)
        side = cast((30, y0, z), (-1, 0, 0), obs)
        roof = cast((0, y0, z), (0, 1, 0), obs)
        floor = cast((0, y0, z), (0, -1, 0), obs)
        wall = cast((0, y0, z), (1, 0, 0), obs)
        f = lambda p, c: f"{getattr(p, c):+6.2f}" if p else "  none"
        print(f"[ark] {label} z {z:+3d}: outer top {f(top, 'y')} bottom {f(bot, 'y')} side {f(side, 'x')} | inside roof {f(roof, 'y')} floor {f(floor, 'y')} wall {f(wall, 'x')}")


img = bpy.data.images.load(TEX)
tw, th = img.size
px = np.array(img.pixels[:]).reshape(th, tw, 4)


def texel(f, uv):
    cs = [px[int((l[uv].uv.y % 1) * (th - 1)), int((l[uv].uv.x % 1) * (tw - 1))] for l in f.loops]
    c = sum(cs) / len(cs)
    return c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11, c


if ANALYZE:
    sections("hull")
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    uv = bm.loops.layers.uv.active
    rows = []
    for f in bm.faces:
        u = U(f.calc_center_median())
        n = U(f.normal)
        if u.z > 12 and n.z > 0.25 and abs(u.x) < 1.9:
            l, c = texel(f, uv)
            rows.append((u, n, f.calc_area(), l, c))
    rows.sort(key=lambda r: -r[2])
    for u, n, a, l, c in rows[:40]:
        print(f"[ark] front face at {u.x:+.2f} {u.y:+.2f} {u.z:+.2f} n {n.x:+.2f} {n.y:+.2f} {n.z:+.2f} area {a:.3f} lum {l:.3f} rgb {c[0]:.2f} {c[1]:.2f} {c[2]:.2f}")
    sys.exit(0)

# ---------------------------------------------------------------- the walkable volume (PrefabBuilder ArkDeck)
# corridor through the fuselage and the bridge in the cab, one deck at FLOOR_Y; hull faces inside it are internal
# structure of the source model (the cab block, pod mounts) and would show inside: they go.
FLOOR_Y = -1.55
ROOMS = [  # (x half width, y min, y max, z min, z max) of the inner volume, Unity coordinates
    (1.62, FLOOR_Y - 0.08, 0.76, -8.6, 6.6),   # corridor (fuselage: outer side 1.83, top 0.94)
    (1.36, FLOOR_Y - 0.08, 1.6, 6.6, 15.7),    # bridge (cab: outer side 1.47-1.83, top 1.74-2.5; a bottom notch at -1.68 stays)
]
DOOR = (-2.5, -0.5, 0.55)  # airlock in the left flank: z range, top (sill = FLOOR_Y); a boarding ramp leads down from it


def in_rooms(u, margin=0.0):
    return any(abs(u.x) < hx - margin and y0 + margin < u.y < y1 - margin and z0 + margin < u.z < z1 - margin
               for hx, y0, y1, z0, z1 in ROOMS)


bm = bmesh.new()
bm.from_mesh(hull.data)
uv = bm.loops.layers.uv.active
# 1. internal faces: every vertex inside the volume, or big internal planes whose centre is inside it
inner = [f for f in bm.faces if all(in_rooms(U(v.co)) for v in f.verts) or in_rooms(U(f.calc_center_median()), 0.05)
         or any(in_rooms(U(v.co), 0.15) for v in f.verts)]  # brackets of the crate mounts hanging into the corridor
bmesh.ops.delete(bm, geom=inner, context='FACES')
print(f"[ark] removed {len(inner)} internal faces")

# 2. bridge window: the dark sloped panel at the cab front (painted glass, luminance ~0.2 against ~0.27-0.44 hull)
glass = set()
for f in bm.faces:
    u, n = U(f.calc_center_median()), U(f.normal)
    if u.z > 15.0 and -0.9 < u.y < 1.6 and abs(u.x) < 1.6 and n.z > 0.25 and n.y > 0.05 and texel(f, uv)[0] < 0.235:
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
print(f"[ark] window faces {len(window)} area {sum(f.calc_area() for f in window):.2f} m2 ({len(patches)} patches)")
window_set = set(window)

# 3. airlock door in the left flank (between the front and rear wings): cut the side skin along the door rectangle
side = [f for f in bm.faces if U(f.calc_center_median()).x < -1.4 and U(f.normal).x < -0.5
        and DOOR[0] - 1.5 < U(f.calc_center_median()).z < DOOR[1] + 1.5 and -2.6 < U(f.calc_center_median()).y < 1.0]
cut_geom = list({e for f in side for e in f.edges}) + side + list({v for f in side for v in f.verts})
for co, no in ((B(Vector((0, 0, DOOR[0]))), B(Vector((0, 0, 1)))), (B(Vector((0, 0, DOOR[1]))), B(Vector((0, 0, 1)))),
               (B(Vector((0, FLOOR_Y, 0))), B(Vector((0, 1, 0)))), (B(Vector((0, DOOR[2], 0))), B(Vector((0, 1, 0))))):
    res = bmesh.ops.bisect_plane(bm, geom=cut_geom, plane_co=co, plane_no=no)
    cut_geom = list({g for g in cut_geom + res["geom"] if g.is_valid})
door = [f for f in bm.faces if U(f.normal).x < -0.5 and U(f.calc_center_median()).x < -1.4
        and DOOR[0] < U(f.calc_center_median()).z < DOOR[1] and FLOOR_Y < U(f.calc_center_median()).y < DOOR[2]]
bmesh.ops.delete(bm, geom=door, context='FACES')
dx = [U(v.co).x for f in side if f.is_valid for v in f.verts]
print(f"[ark] airlock door: removed {len(door)} faces, flank skin at x {min(dx) if dx else 0:+.2f}..{max(dx) if dx else 0:+.2f}")

window_idx = {f.index for f in window_set if f.is_valid}
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
glass_obj.name = glass_obj.data.name = "Ship_Ark_Cargo_Glass"
gb = [U(v.co) for v in glass_obj.data.vertices]
print(f"[ark] window bounds x {min(p.x for p in gb):+.2f}..{max(p.x for p in gb):+.2f} y {min(p.y for p in gb):+.2f}..{max(p.y for p in gb):+.2f} "
      f"z {min(p.z for p in gb):+.2f}..{max(p.z for p in gb):+.2f}")

# ---------------------------------------------------------------- numbers for PrefabBuilder
for z in (-8, -6, -4, -2, 0, 2, 4, 6, 8, 10, 12, 14, 15):
    top = cast((0, 30, z), (0, -1, 0))
    bot = cast((0, -30, z), (0, 1, 0))
    side = cast((30, FLOOR_Y + 1.0, z), (-1, 0, 0))
    print(f"[ark] section z {z:+3d}: outer top {top.y if top else 0:+.2f} bottom {bot.y if bot else 0:+.2f} side {side.x if side else 0:.2f}")
for o_, d_, label in (((0, 30, 8.6), (0, -1, 0), "cab roof"), ((0, 30, -12.5), (0, -1, 0), "tail top"), ((0, 30, 0), (0, -1, 0), "spine"),
                      ((0, -30, 12), (0, 1, 0), "cab bottom"), ((0, -30, -4.5), (0, 1, 0), "belly at hatch")):
    p = cast(o_, d_)
    print(f"[ark] {label}: {p.x if p else 0:+.2f} {p.y if p else 0:+.2f} {p.z if p else 0:+.2f}")

for o in (hull, glass_obj):
    vw.export_fbx(o, os.path.join(OUT, o.name + ".fbx"))
    print("[ark] exported", o.name)
