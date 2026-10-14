"""
Player ship (Dart) from the free Fab model "SpaceShip" by JazOone3D (#91):
https://www.fab.com/listings/14265e80-7cc8-4238-873c-8b970f44f552 (Fab Standard License, 22k tris, 4K PBR).

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/fab_spaceship.py -- Assets/_Project/Art/Models/Ships

Normalizes the source FBX (Tools/Blender/Sources/FabSpaceShip, centimetre scale, rotated) to the Voidward
contract: 1 unit = 1 m, nose on Blender -Y (Unity +Z), origin at the bounding box centre, scaled to LENGTH.
Then opens the cockpit so the pilot sits inside:
  - Ship_Dart_Fab.fbx         hull without the canopy glass (original UVs/normals, PBR material in PrefabBuilder)
  - Ship_Dart_Fab_Canopy.fbx  the glass faces (painted dark glass in the source texture) as their own mesh
  - Ship_Dart_Fab_Cockpit.fbx cockpit tub: the nose section between two bulkheads, duplicated, flipped and pushed
                              inwards (so the walls follow the hull exactly), capped by both bulkheads; vertex
                              contract of vw_blender (Col / surf / detail) for VW_Hull
Prints the cockpit cross-sections (Unity coordinates) used to place seat, dashboard and view in PrefabBuilder.
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

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Assets/_Project/Art/Models/Ships"
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "Sources", "FabSpaceShip", "SpaceShip.fbx")
TEX = os.path.join(HERE, "..", "..", "Assets", "_Project", "Art", "Models", "Ships", "FabSpaceShip", "FabShip_Base_color.jpg")
LENGTH = 15.0                    # metres nose to engine rings: room for a seated pilot under the canopy
S = LENGTH / 10.5                # the numbers below were measured at 10.5 m
GLASS_Z = (1.75 * S, 4.35 * S)   # canopy span along Unity z
GLASS_X, GLASS_Y = 0.9 * S, -0.6 * S
TUB_Z = (1.55 * S, 4.32 * S)     # bulkheads: rear (seat back) and front (right at the glass front edge, the nose stays visible)
INSET = 0.025                    # tub walls inside the hull skin
os.makedirs(OUT, exist_ok=True)


def U(v):
    return Vector((-v.x, v.z, -v.y))


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
hull = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
hull.name = hull.data.name = "Ship_Dart_Fab"
vw.activate(hull)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

pts = [v.co for v in hull.data.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)])
hi = Vector([max(p[i] for p in pts) for i in range(3)])
k = LENGTH / (hi.y - lo.y)
centre = (lo + hi) * 0.5
for v in hull.data.vertices:
    v.co = (v.co - centre) * k
hull.data.update()
size = (hi - lo) * k
print(f"[fab_spaceship] scale {k:.4f} size (unity x/y/z) {size.x:.2f} {size.z:.2f} {size.y:.2f}")

# ---------------------------------------------------------------- canopy glass (dark painted panes)
img = bpy.data.images.load(os.path.abspath(TEX))
w, h = img.size
px = np.array(img.pixels[:]).reshape(h, w, 4)
bm = bmesh.new()
bm.from_mesh(hull.data)
uv = bm.loops.layers.uv.active
bm.faces.ensure_lookup_table()


def lum(f):
    t = 0.0
    for l in f.loops:
        p = px[int((l[uv].uv.y % 1) * (h - 1)), int((l[uv].uv.x % 1) * (w - 1))]
        t += p[0] * 0.3 + p[1] * 0.59 + p[2] * 0.11
    return t / len(f.loops)


glass = set()
for f in bm.faces:
    c = f.calc_center_median()
    u = U(c)
    if not (GLASS_Z[0] < u.z < GLASS_Z[1] and abs(u.x) < GLASS_X and u.y > GLASS_Y):
        continue
    n = f.normal
    outward = n.z > 0.3 or (abs(n.x) > 0.5 and c.x * n.x > 0)
    if outward and lum(f) < 0.5:
        glass.add(f)
# keep only big connected panes (drops dark vent slits that happen to lie in the box)
panes, seen = [], set()
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
    if sum(g.calc_area() for g in isl) > 0.5 * S * S:
        panes.extend(isl)
glass_idx = {f.index for f in panes}
print(f"[fab_spaceship] canopy faces {len(glass_idx)} area {sum(f.calc_area() for f in panes):.2f} m2")
bm.free()

# split the glass into its own object
vw.activate(hull)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='DESELECT')
bpy.ops.object.mode_set(mode='OBJECT')
for p in hull.data.polygons:
    p.select = p.index in glass_idx
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.separate(type='SELECTED')
bpy.ops.object.mode_set(mode='OBJECT')
canopy = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o != hull][0]
canopy.name = canopy.data.name = "Ship_Dart_Fab_Canopy"

# ---------------------------------------------------------------- cockpit tub
# main fuselage island only (guns and greebles are separate islands), nose section between the bulkheads
bm = bmesh.new()
bm.from_mesh(hull.data)
bm.faces.ensure_lookup_table()
islands, seen = [], set()
for f in bm.faces:
    if f in seen:
        continue
    stack, isl = [f], []
    seen.add(f)
    while stack:
        g = stack.pop()
        isl.append(g)
        for e in g.edges:
            for o in e.link_faces:
                if o not in seen:
                    seen.add(o)
                    stack.append(o)
    islands.append(isl)
main = max(islands, key=lambda i: sum(f.calc_area() for f in i))
keep = {f for f in main}
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
for z, side in ((TUB_Z[0], 1), (TUB_Z[1], -1)):
    # Unity z -> Blender -y; keep the part between the planes
    co = Vector((0, -z, 0))
    no = Vector((0, -side, 0))
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_inner=True)
# the inner faces (beyond the fuselage width/height, e.g. the inner walls of the pods) are not ours
bmesh.ops.delete(bm, geom=[f for f in bm.faces if abs(U(f.calc_center_median()).x) > 1.6 * S], context='FACES')
bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
# caps: fill the boundary loops lying on each bulkhead plane
for z in TUB_Z:
    edges = [e for e in bm.edges if e.is_boundary and all(abs(-v.co.y - z) < 1e-3 for v in e.verts)]
    if edges:
        res = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=edges)
        print(f"[fab_spaceship] bulkhead z {z:.2f}: {len(edges)} edges -> {len(res['geom'])} faces")
bm.normal_update()
mid = Vector((0, -(TUB_Z[0] + TUB_Z[1]) * 0.5, -0.4 * S))
for f in bm.faces:  # all faces point into the tub
    if f.normal.dot(f.calc_center_median() - mid) > 0:
        f.normal_flip()
bm.normal_update()
for v in bm.verts:  # push inwards: average of the (already inward) face normals
    if v.link_faces:
        n = sum((f.normal for f in v.link_faces), Vector()).normalized()
        v.co += n * INSET
tub_mesh = bpy.data.meshes.new("Ship_Dart_Fab_Cockpit")
bm.to_mesh(tub_mesh)
bm.free()
tub = bpy.data.objects.new("Ship_Dart_Fab_Cockpit", tub_mesh)
bpy.context.scene.collection.objects.link(tub)
for name in [l.name for l in tub_mesh.uv_layers]:
    tub_mesh.uv_layers.remove(tub_mesh.uv_layers[name])
SLATE, PANEL = vw.srgb("#2A3240"), vw.srgb("#3A4454")
vw.paint(tub, lambda p, n: PANEL if n.z > 0.6 else SLATE, lambda p, n: (0.35, 0.0), lambda p, n: 0.35)
print(f"[fab_spaceship] cockpit tub faces {len(tub_mesh.polygons)}")

# ---------------------------------------------------------------- cross-sections for PrefabBuilder (Unity coords)
depsgraph = bpy.context.evaluated_depsgraph_get()
for z in (TUB_Z[0], 2.2 * S, 3.0 * S, 3.8 * S, TUB_Z[1]):
    by = -z
    out = []
    for o_, d_ in (((0, by, 10), (0, 0, -1)), ((0, by, -10), (0, 0, 1)), ((10, by, -0.3 * S), (-1, 0, 0))):
        hit, loc, *_ = hull.ray_cast(Vector(o_), Vector(d_))
        hitc, locc, *_ = canopy.ray_cast(Vector(o_), Vector(d_))
        best = None
        if hit:
            best = loc
        if hitc and (best is None or (locc - Vector(o_)).length < (best - Vector(o_)).length):
            best = locc
        out.append(None if best is None else U(best))
    top, bottom, side = out
    print(f"[fab_spaceship] section z {z:.2f}: top {top.y if top else 0:+.2f} bottom {bottom.y if bottom else 0:+.2f} half width {abs(side.x) if side else 0:.2f}")

for o in (hull, canopy, tub):
    vw.export_fbx(o, os.path.join(OUT, o.name + ".fbx"))
    print("[fab_spaceship] exported", o.name)
