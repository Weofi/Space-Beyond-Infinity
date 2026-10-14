"""
Player ship (Dart) from the free Fab model "SpaceShip" by JazOone3D (#91):
https://www.fab.com/listings/14265e80-7cc8-4238-873c-8b970f44f552 (Fab Standard License, 22k tris, 4K PBR).

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/fab_spaceship.py -- Assets/_Project/Art/Models/Ships

Normalizes the source FBX (Tools/Blender/Sources/FabSpaceShip, centimetre scale, rotated) to the Voidward
contract: 1 unit = 1 m, nose on Blender -Y (Unity +Z), origin at the bounding box centre, scaled to LENGTH.
The original UVs and normals stay untouched; the PBR textures live next to the export in
Assets/_Project/Art/Models/Ships/FabSpaceShip and the material is built by PrefabBuilder (FabShipMaterial).
Prints a top-surface profile along the centre line (Unity coordinates) to place the pilot view.
"""
import sys
import os
import bpy
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Assets/_Project/Art/Models/Ships"
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Sources", "FabSpaceShip", "SpaceShip.fbx")
LENGTH = 10.5  # metres nose to engine rings (the Dart was 9.8 m)
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
obj.name = obj.data.name = "Ship_Dart_Fab"
vw.activate(obj)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

pts = [v.co for v in obj.data.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)])
hi = Vector([max(p[i] for p in pts) for i in range(3)])
k = LENGTH / (hi.y - lo.y)
centre = (lo + hi) * 0.5
for v in obj.data.vertices:
    v.co = (v.co - centre) * k
obj.data.update()
size = (hi - lo) * k
print(f"[fab_spaceship] scale {k:.4f} size (unity x/y/z) {size.x:.2f} {size.z:.2f} {size.y:.2f}")

# Top profile on the centre line, in Unity coordinates (z forward = -blender y, y up = blender z).
depsgraph = bpy.context.evaluated_depsgraph_get()
for i in range(0, 23):
    by = -LENGTH * 0.5 + i * LENGTH / 22.0
    hit, loc, *_ = obj.ray_cast(Vector((0.0, by, 10.0)), Vector((0, 0, -1)))
    if hit:
        print(f"[fab_spaceship] profile z {-by:+.2f} top y {loc.z:+.2f}")

vw.export_fbx(obj, os.path.join(OUT, "Ship_Dart_Fab.fbx"))
print("[fab_spaceship] exported", os.path.join(OUT, "Ship_Dart_Fab.fbx"))
