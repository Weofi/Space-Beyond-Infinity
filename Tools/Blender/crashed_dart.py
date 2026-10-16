"""
Crashed Dart POI (#99): the normalized Fab Dart (Ship_Dart_Fab*.fbx from fab_spaceship.py), wrecked and posed on the ground.

Run headless (from the repo root), after fab_spaceship.py:
  blender -b --factory-startup -P Tools/Blender/crashed_dart.py -- Assets/_Project/Art/Models/Ships

Damage: the left wing (Unity -x = Blender +x) torn off along a ragged line and lying beside the ship, the right wing
tip bent down, the nose crumpled, the two left nose guns ripped off and scattered as debris. Pose: nose dug into the
ground (z = 0 is the ground, origin = the crash site centre), pitched down, rolled onto the torn side.
Exports (original UVs, the Fab PBR material; the scorch is a generated texture in AssetModelBuilder):
  - Ship_Dart_Crashed.fbx         hull
  - Ship_Dart_Crashed_Canopy.fbx  canopy glass in the same pose
  - Ship_Dart_Crashed_Wing.fbx    the torn wing on the ground
  - Ship_Dart_Crashed_Debris.fbx  the ripped-off guns on the ground
Prints the Unity-space points PrefabBuilder needs (wing tear = fire/smoke, bounds = colliders).
Blender (x, y, z) = Unity (-x, z, -y); U(...) converts.
"""
import sys
import os
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Assets/_Project/Art/Models/Ships"
SRC = os.path.join(OUT, "Ship_Dart_Fab.fbx")
SRC_GLASS = os.path.join(OUT, "Ship_Dart_Fab_Canopy.fbx")
WING_CUT = 5.0                   # Blender x where the left wing tears off (outside the engine pod)
TIP_BEND = (-6.4, 0.42)          # right wing: bend starts at this x, angle (rad) at the tip
NOSE = (-5.8, 1.7)               # crumple zone: starts at this y, depth to the tip
PITCH, ROLL, YAW = 10.0, 9.0, 15.0
BURY = 0.8                       # the nose tip this deep under the ground


def U(v):
    return Vector((-v.x, v.z, -v.y))


def load(path):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    o = [x for x in bpy.context.scene.objects if x not in before and x.type == 'MESH'][0]
    vw.activate(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def islands(bm):
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
    out.sort(key=len, reverse=True)
    return out


def separate(obj, faces, name):
    vw.activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in obj.data.polygons:
        p.select = p.index in faces
    before = set(bpy.context.scene.objects)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    o = [x for x in bpy.context.scene.objects if x not in before][0]
    o.name = o.data.name = name
    return o


def transform(obj, m):
    obj.data.transform(m)
    obj.data.update()


def centre(obj):
    pts = [v.co for v in obj.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    return (lo + hi) * 0.5


def min_z(objs):
    return min(v.co.z for o in objs for v in o.data.vertices)


bpy.ops.wm.read_factory_settings(use_empty=True)
hull = load(SRC)
hull.name = hull.data.name = "Ship_Dart_Crashed"
glass = load(SRC_GLASS)
glass.name = glass.data.name = "Ship_Dart_Crashed_Canopy"

# ---------------------------------------------------------------- tear off the left wing and the left nose guns
bm = bmesh.new()
bm.from_mesh(hull.data)
isl = islands(bm)
main = set(isl[0])
wing, guns = set(), set()
for f in bm.faces:
    c = f.calc_center_median()
    ragged = WING_CUT + 0.35 * math.sin(c.y * 2.3) + 0.2 * math.sin(c.y * 5.1 + 1.0)
    if f.index in main and c.x > ragged:
        wing.add(f.index)
for fs in isl[1:]:
    c = sum((bm.faces[i].calc_center_median() for i in fs), Vector()) / len(fs)
    if len(fs) > 400 and c.x > 0 and c.y < -2.0:       # the two left gun barrels under the nose
        guns |= set(fs)
# tear line = the vertices the wing shares with the rest of the hull (fire/smoke point)
seam = [v.co.copy() for v in bm.verts if any(f.index in wing for f in v.link_faces) and any(f.index not in wing for f in v.link_faces)]
tear_pt = sum(seam, Vector()) / max(1, len(seam))
bm.free()
print(f"[crashed_dart] wing faces {len(wing)} gun faces {len(guns)}")
wing_obj = separate(hull, wing, "Ship_Dart_Crashed_Wing")
debris = separate(hull, guns, "Ship_Dart_Crashed_Debris")

# ---------------------------------------------------------------- deform: bent right wing tip, crumpled nose
for v in hull.data.vertices:
    p = v.co
    if p.x < TIP_BEND[0]:
        d = TIP_BEND[0] - p.x
        a = TIP_BEND[1] * min(1.0, d / 2.5)
        p.x = TIP_BEND[0] - d * math.cos(a)
        p.z -= d * math.sin(a)
for o in (hull, glass):
    for v in o.data.vertices:
        p = v.co
        if p.y < NOSE[0]:
            t = min(1.0, (NOSE[0] - p.y) / NOSE[1])
            n = noise.noise(p * 1.7)
            p.y += t * t * 0.9
            p.x *= 1.0 + 0.08 * t
            p.z += n * 0.14 * t - 0.12 * t * t
    o.data.update()

# ---------------------------------------------------------------- pose the ship: nose dug in, rolled onto the torn side
pose = Matrix.Rotation(math.radians(YAW), 4, 'Z') @ Matrix.Rotation(math.radians(ROLL), 4, 'Y') @ Matrix.Rotation(math.radians(PITCH), 4, 'X')
for o in (hull, glass):
    transform(o, pose)
tear_pt = pose @ tear_pt
lift = Matrix.Translation((0, 0, -BURY - min_z([hull])))
for o in (hull, glass):
    transform(o, lift)
tear_pt = lift @ tear_pt

# ---------------------------------------------------------------- the torn wing on the ground beside the ship
c = centre(wing_obj)
transform(wing_obj, Matrix.Translation(-c))
transform(wing_obj, Matrix.Rotation(math.radians(38), 4, 'Z') @ Matrix.Rotation(math.radians(-5), 4, 'Y') @ Matrix.Rotation(math.radians(4), 4, 'X'))
transform(wing_obj, Matrix.Translation((9.5, 1.8, 0)))
transform(wing_obj, Matrix.Translation((0, 0, -0.06 - min_z([wing_obj]))))

# ---------------------------------------------------------------- the ripped-off guns, scattered in front of the nose
vw.activate(debris)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.context.scene.objects if o.name.startswith("Ship_Dart_Crashed_Debris")]
spots = [(4.2, -9.5, 70.0, 80.0), (-2.5, -11.5, -25.0, 95.0), (6.0, -6.0, 120.0, 85.0), (1.0, -13.0, 10.0, 75.0)]
for i, o in enumerate(parts):
    x, y, yaw, tilt = spots[i % len(spots)]
    c = centre(o)
    transform(o, Matrix.Translation(-c))
    transform(o, Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Rotation(math.radians(tilt), 4, 'Y'))
    transform(o, Matrix.Translation((x, y, 0)))
    transform(o, Matrix.Translation((0, 0, -0.03 - min_z([o]))))
debris = vw.join(parts, "Ship_Dart_Crashed_Debris") if len(parts) > 1 else parts[0]
debris.name = debris.data.name = "Ship_Dart_Crashed_Debris"

# ---------------------------------------------------------------- numbers for PrefabBuilder (Unity coordinates)
def bounds(o):
    pts = [U(v.co) for v in o.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    return lo, hi


for o in (hull, wing_obj, debris):
    lo, hi = bounds(o)
    print(f"[crashed_dart] {o.name} bounds ({lo.x:.2f}, {lo.y:.2f}, {lo.z:.2f}) .. ({hi.x:.2f}, {hi.y:.2f}, {hi.z:.2f})")
t = U(tear_pt)
print(f"[crashed_dart] wing tear ({t.x:.2f}, {t.y:.2f}, {t.z:.2f})")
inv = pose.to_3x3()
for name, local in (("forward", Vector((0, -1, 0))), ("up", Vector((0, 0, 1)))):
    w = U(inv @ local)
    print(f"[crashed_dart] hull {name} ({w.x:.3f}, {w.y:.3f}, {w.z:.3f})")

for o in (hull, glass, wing_obj, debris):
    vw.export_fbx(o, os.path.join(OUT, o.name + ".fbx"))
    print("[crashed_dart] exported", o.name)
