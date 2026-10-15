"""
Mule (cargo hauler) from the free Fab model "DROPSHIP R35" by GrafxBOX (#97):
https://www.fab.com/listings/357a3c30-e07c-4cf0-a189-744ffecca5f8 (CC BY 4.0, 19.4k tris, 4K PBR, Grey livery).

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/r35_dropship.py -- Assets/_Project/Art/Models/Ships [preview_dir]

Normalizes the source FBX (Tools/Blender/Sources/DropshipR35, centimetres, parts parented to the hull) to the Voidward
contract: 1 unit = 1 m, nose on Blender -Y (Unity +Z), origin at the bounding box centre, scaled to LENGTH. All parts
(engines, landing gear, chin laser, rear ramp) are joined into one hull mesh with the original UVs.
The R35 only has small painted windows (a visor on the nose tip and two side slits), too small to fly from. So the
upper nose (the spine and the dark side panels between the bulkheads) becomes one canopy and the pilot sits under it:
  - Ship_Mule_R35.fbx          hull without the canopy (original UVs/normals, PBR material in PrefabBuilder)
  - Ship_Mule_R35_Canopy.fbx   the canopy faces as their own mesh (original UVs, rendered as smoked glass)
  - Ship_Mule_R35_Cockpit.fbx  cockpit tub: convex hull of the nose section between the bulkheads, shrunk inside the
                               skin, open under the canopy, faces pointing inwards (vw_blender vertex contract, VW_Hull)
Prints the cockpit cross-sections (Unity coordinates) used to place seat, dashboard and view in PrefabBuilder.
With a preview dir it renders workbench shots (outside with the canopy highlighted, and from the seated eye).
Blender (x, y, z) = Unity (-x, z, -y) below; U(...) converts.
"""
import sys
import os
import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Assets/_Project/Art/Models/Ships"
PREVIEW = ARGS[1] if len(ARGS) > 1 else None
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(HERE, "Sources", "DropshipR35", "Dropship_R35.fbx")
TEX = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Ships", "DropshipR35", "R35_Base_color.jpg")
LENGTH = 15.0                     # metres, laser tip to the engine pods (DESIGN.md Mule)
# Unity coordinates after normalizing (origin = bounds centre, y up, +z nose)
CANOPY_Z = (2.55, 5.45)           # canopy span along z (rear, front)
CANOPY_X = 1.45                   # half width
CANOPY_Y = 0.25                   # faces above this height (the nose's upper half)
TUB_Z = (2.2, 5.7)                # bulkheads of the cockpit tub (rear, front)
INSET = 0.04                      # tub walls inside the hull skin
EYE = Vector((0.0, 0.35, 3.3))    # seated eye (PrefabBuilder Mule PilotSeat ViewPoint)
os.makedirs(OUT, exist_ok=True)


def U(v):
    return Vector((-v.x, v.z, -v.y))


def B(u):
    return Vector((-u.x, -u.z, u.y))


# ---------------------------------------------------------------- import, join, normalize
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons)]
for o in parts:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
for o in parts:
    vw.activate(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
main_name = "Dropship_R35"
main_part = bpy.data.objects[main_name]
# the main hull's faces stay tagged (face map via an integer attribute) so the tub can use the fuselage only
for o in parts:
    attr = o.data.attributes.new("vw_main", 'INT', 'FACE')
    for i in range(len(o.data.polygons)):
        attr.data[i].value = 1 if o == main_part else 0
hull = vw.join(parts, "Ship_Mule_R35")
for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']:
    bpy.data.objects.remove(o)

pts = [v.co for v in hull.data.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)])
hi = Vector([max(p[i] for p in pts) for i in range(3)])
k = LENGTH / (hi.y - lo.y)
centre = (lo + hi) * 0.5
for v in hull.data.vertices:
    v.co = (v.co - centre) * k
hull.data.update()
size = (hi - lo) * k
print(f"[r35] scale {k:.4f} size (unity x/y/z) {size.x:.2f} {size.z:.2f} {size.y:.2f}")

# ---------------------------------------------------------------- canopy
bm = bmesh.new()
bm.from_mesh(hull.data)
bm.faces.ensure_lookup_table()
glass_idx = set()
for f in bm.faces:
    u = U(f.calc_center_median())
    n = U(f.normal)
    if not (CANOPY_Z[0] < u.z < CANOPY_Z[1] and abs(u.x) < CANOPY_X and u.y > CANOPY_Y):
        continue
    # outer skin facing up or sideways-out (the walls of insets and the undersides of rails stay hull)
    if n.y > 0.2 or (abs(n.x) > 0.6 and u.x * n.x > 0 and n.y > -0.2):
        glass_idx.add(f.index)
# keep the biggest connected patch (drops stray greebles that happen to lie in the box)
by_index = {f.index: f for f in bm.faces}
patches, seen = [], set()
for i in glass_idx:
    if i in seen:
        continue
    stack, isl = [by_index[i]], []
    seen.add(i)
    while stack:
        g = stack.pop()
        isl.append(g)
        for e in g.edges:
            for o in e.link_faces:
                if o.index in glass_idx and o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
    patches.append(isl)
patches.sort(key=lambda p: -sum(f.calc_area() for f in p))
keep = []
for p in patches:
    if sum(f.calc_area() for f in p) > 0.25:
        keep.extend(p)
glass_idx = {f.index for f in keep}
print(f"[r35] canopy faces {len(glass_idx)} in {len([p for p in patches if sum(f.calc_area() for f in p) > 0.25])} patches, "
      f"area {sum(f.calc_area() for f in keep):.2f} m2 (all candidates {len(patches)})")
bm.free()

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
canopy.name = canopy.data.name = "Ship_Mule_R35_Canopy"

# ---------------------------------------------------------------- cockpit tub
bm = bmesh.new()
bm.from_mesh(hull.data)
main_attr = bm.faces.layers.int.get("vw_main")
bmesh.ops.delete(bm, geom=[f for f in bm.faces if not f[main_attr]], context='FACES')
for z, side in ((TUB_Z[0], 1), (TUB_Z[1], -1)):
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=B(Vector((0, 0, z))), plane_no=Vector((0, -side, 0)), clear_inner=True)
section = [v.co.copy() for v in bm.verts if abs(U(v.co).x) < 1.9]
bm.free()
bm = bmesh.new()
for p in section:
    bm.verts.new(p)
res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
bmesh.ops.delete(bm, geom=list({g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)}), context='VERTS')
bmesh.ops.triangulate(bm, faces=bm.faces[:])
# subdivide so the shrink and the canopy opening follow the hull more closely
bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=2, use_grid_fill=True)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
bm.normal_update()
# shrink: push every vertex inwards along its (outward) normal
for v in bm.verts:
    v.co -= v.normal * INSET
bm.normal_update()
# open under the canopy: drop tub faces whose outward ray meets the canopy before the hull
cbm = bmesh.new()
cbm.from_mesh(canopy.data)
canopy_tree = BVHTree.FromBMesh(cbm)
hbm = bmesh.new()
hbm.from_mesh(hull.data)
hull_tree = BVHTree.FromBMesh(hbm)
drop = []
up = B(Vector((0, 1, 0)))
for f in bm.faces:
    c = f.calc_center_median()
    hc = canopy_tree.ray_cast(c, f.normal, 0.9)
    hh = hull_tree.ray_cast(c, f.normal, 0.9)
    # the convex tub bulges past the concave nose in places: an upward-facing face counts as "under the canopy" when
    # the canopy lies on the vertical through it (above or below)
    vertical = U(f.normal).y > 0.15 and (canopy_tree.ray_cast(c, up, 2.5)[0] is not None or canopy_tree.ray_cast(c, -up, 1.0)[0] is not None)
    if vertical or (hc[0] is not None and (hh[0] is None or hc[3] < hh[3] + 0.02)):
        drop.append(f)
bmesh.ops.delete(bm, geom=drop, context='FACES')
print(f"[r35] tub opening: {len(drop)} faces under the canopy")
for f in bm.faces:
    f.normal_flip()
bm.normal_update()
tub_mesh = bpy.data.meshes.new("Ship_Mule_R35_Cockpit")
bm.to_mesh(tub_mesh)
bm.free()
cbm.free()
hbm.free()
tub = bpy.data.objects.new("Ship_Mule_R35_Cockpit", tub_mesh)
bpy.context.scene.collection.objects.link(tub)
SLATE, PANEL = vw.srgb("#2A3240"), vw.srgb("#3A4454")
vw.paint(tub, lambda p, n: PANEL if n.z > 0.6 else SLATE, lambda p, n: (0.35, 0.0), lambda p, n: 0.35)
print(f"[r35] cockpit tub faces {len(tub_mesh.polygons)}")

# ---------------------------------------------------------------- cross-sections for PrefabBuilder (Unity coords)
depsgraph = bpy.context.evaluated_depsgraph_get()


def cast(o_, d_):
    best = None
    for ob in (hull, canopy):
        hit, loc, *_ = ob.ray_cast(B(Vector(o_)), B(Vector(d_)))
        if hit and (best is None or (U(loc) - Vector(o_)).length < (best - Vector(o_)).length):
            best = U(loc)
    return best


for z in (TUB_Z[0], 3.0, 3.5, 4.0, 4.5, 5.0, TUB_Z[1]):
    top = cast((0, 10, z), (0, -1, 0))
    bottom = cast((0, -10, z), (0, 1, 0))
    tin = cast((0, 0.8, z), (0, 1, 0))
    bin_ = cast((0, 0.8, z), (0, -1, 0))
    win = cast((0, 0.8, z), (1, 0, 0))
    print(f"[r35] section z {z:.2f}: outer top {top.y if top else 0:+.2f} bottom {bottom.y if bottom else 0:+.2f} | "
          f"from (0,0.8): roof {tin.y if tin else 0:+.2f} floor {bin_.y if bin_ else 0:+.2f} wall {abs(win.x) if win else 0:.2f}")
tb = [U(v.co) for v in tub_mesh.vertices]
print(f"[r35] tub bounds x {min(p.x for p in tb):+.2f}..{max(p.x for p in tb):+.2f} y {min(p.y for p in tb):+.2f}..{max(p.y for p in tb):+.2f} "
      f"z {min(p.z for p in tb):+.2f}..{max(p.z for p in tb):+.2f}")
cb = [U(v.co) for v in canopy.data.vertices]
print(f"[r35] canopy bounds x {min(p.x for p in cb):+.2f}..{max(p.x for p in cb):+.2f} y {min(p.y for p in cb):+.2f}..{max(p.y for p in cb):+.2f} "
      f"z {min(p.z for p in cb):+.2f}..{max(p.z for p in cb):+.2f}")

# ---------------------------------------------------------------- export
for o in (hull, canopy, tub):
    if o.data.attributes.get("vw_main"):
        o.data.attributes.remove(o.data.attributes["vw_main"])
    vw.export_fbx(o, os.path.join(OUT, o.name + ".fbx"))
    print("[r35] exported", o.name)

# ---------------------------------------------------------------- preview (workbench)
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    img = bpy.data.images.load(TEX)
    mat = bpy.data.materials.new("hull")
    mat.use_nodes = True
    t = mat.node_tree.nodes.new('ShaderNodeTexImage')
    t.image = img
    mat.node_tree.links.new(t.outputs[0], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    mat.node_tree.nodes.active = t
    hull.data.materials.clear()
    hull.data.materials.append(mat)
    gm = bpy.data.materials.new("glass")
    gm.diffuse_color = (0.1, 0.5, 1.0, 1.0)
    canopy.data.materials.clear()
    canopy.data.materials.append(gm)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_backface_culling = True   # like Unity: the inward tub is invisible from outside
    sc.render.resolution_x, sc.render.resolution_y = 900, 560
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    shots = {
        "outside_front": (Vector((0, 0.5, 3.5)), Vector((9, 5, 14)), 40),
        "outside_side": (Vector((0, 0.5, 3.5)), Vector((-12, 3, 2)), 40),
        "outside_top": (Vector((0, 0, 3.0)), Vector((0, 14, 0.01)), 40),
        "eye": (Vector((0, 0.0, 14.0)), EYE - Vector((0, 0.0, 14.0)), 18),
        "eye_back": (Vector((0, -0.6, 0.0)), EYE + Vector((0, 0.3, 0.5)) - Vector((0, -0.6, 0.0)), 16),
    }
    for name, (tgt, off, lens) in shots.items():
        cam.location = B(tgt + off)
        cam.data.lens = lens
        cam.rotation_euler = (B(tgt) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(PREVIEW, "r35_" + name + ".png")
        bpy.ops.render.render(write_still=True)
    print("[r35] previews in", PREVIEW)
