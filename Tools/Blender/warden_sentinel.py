"""
Warden Sentinel (#98): the small biomechanical guardian drone (legacy enemy kind DroneSwarm, #51 renames it).
No free asset fit the Warden look (geometric, white ceramic + cyan light, separately moving parts), so it is built
here to match the Warden boss (Tools/Blender/warden_boss.py): a closed seed pod of four ivory ceramic petals around a
cyan core, a forward eye lens, a tail spine with a cyan vent, and a separate halo ring that spins.

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/warden_sentinel.py -- Assets/_Project/Art/Models/Aliens [preview_dir]

Exports (vw_blender vertex contract: Col, surf = gloss/glow, detail = metal/AO; forward Blender -Y = Unity +Z):
  - Enemy_Sentinel.fbx       body (petals, core, eye, fins, tail), origin = core centre, ~2.4 m long
  - Enemy_Sentinel_Ring.fbx  halo ring around the core (PrefabBuilder spins it), same origin
Coordinates below are F(x, forward, up) in metres; F maps to Blender (x, -forward, up).
"""
import sys
import os
import math
import bpy
import bmesh
from mathutils import Vector, Matrix
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Assets/_Project/Art/Models/Aliens"
PREVIEW = ARGS[1] if len(ARGS) > 1 else None
os.makedirs(OUT, exist_ok=True)

IVORY = vw.srgb("#E4E8EC")
IVORY_SHADE = vw.srgb("#B9C2CC")
SLATE = vw.srgb("#262E39")
CYAN = vw.srgb("#3FE0FF")
CYAN_DEEP = vw.srgb("#1597C9")


def F(x, fwd, up):
    return Vector((x, -fwd, up))


def obj_from_bm(bm, name):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def lathe(name, profile, segments, axis_fwd=True):
    """Revolves (radius, forward) points around the forward axis."""
    bm = bmesh.new()
    rings = []
    for r, f in profile:
        ring = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            ring.append(bm.verts.new(F(math.cos(a) * r, f, math.sin(a) * r)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for s in range(segments):
            t = (s + 1) % segments
            bm.faces.new((rings[i][s], rings[i][t], rings[i + 1][t], rings[i + 1][s]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bm(bm, name)


def modifier(obj, kind, **kw):
    m = obj.modifiers.new(kind.lower(), kind)
    for k, v in kw.items():
        setattr(m, k, v)
    vw.apply_modifiers(obj)


# ---------------------------------------------------------------- body
vw.reset_scene()
parts = []

# ceramic pod: an egg along forward, split into four petals by gaps (the core light shows through), open at the eye
GAP = 0.075                                                # half width of a seam (m), the core light shows through
petals = []
for k in range(4):
    petal = vw.ellipsoid("Petal%d" % k, (0, 0, 0), (0.78, 1.05, 0.78), segments=64, rings=40)
    bm = bmesh.new()
    bm.from_mesh(petal.data)
    # keep the 90 deg sector around azimuth k*90 (seams on the diagonals = an X seen from the front), cut by planes
    # parallel to the forward axis and GAP off the seam, so the gaps keep a constant width
    for side in (-1, 1):
        phi = math.radians(k * 90.0 + side * 45.0)
        n = Vector((-math.sin(phi), 0, math.cos(phi))) * (-side)   # points into the kept sector
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=n * GAP, plane_no=n, clear_inner=True)
    for co, no in ((Vector((0, -0.80, 0)), Vector((0, 1, 0))), (Vector((0, 0.92, 0)), Vector((0, -1, 0)))):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_inner=True)  # open eye + tail
    bm.to_mesh(petal.data)
    bm.free()
    petals.append(petal)
shell = vw.join(petals, "Shell")
modifier(shell, 'SOLIDIFY', thickness=0.07, offset=-1.0)
modifier(shell, 'BEVEL', width=0.018, segments=2, limit_method='ANGLE')
vw.shade_smooth(shell)


def shell_col(p, n):
    inward = n.dot(-p.normalized()) > 0.2                   # inner side of the petals
    if inward:
        return SLATE
    t = vw.smoothstep(-0.9, 0.6, -p.y)
    return tuple(IVORY_SHADE[i] + (IVORY[i] - IVORY_SHADE[i]) * t for i in range(3))


vw.paint(shell, shell_col, lambda p, n: (0.82, 0.0), lambda p, n: 0.05)
parts.append(shell)

# cyan core (fills the pod, glows through the seams)
core = vw.ellipsoid("Core", (0, 0, 0), (0.6, 0.78, 0.6), segments=32, rings=20)
vw.shade_smooth(core)
vw.paint(core, lambda p, n: CYAN if -p.y > -0.4 else CYAN_DEEP, lambda p, n: (0.9, 1.0 if -p.y > -0.3 else 0.7))
parts.append(core)

# eye: dark iris collar with a bright lens
iris = lathe("Iris", [(0.30, 0.66), (0.40, 0.74), (0.38, 0.84), (0.26, 0.86), (0.20, 0.80)], 40)
vw.shade_smooth(iris)
vw.solid(iris, SLATE, 0.7, 0.0, 0.6)
parts.append(iris)
lens = vw.ellipsoid("Lens", F(0, 0.80, 0), (0.22, 0.12, 0.22), segments=32, rings=16)
vw.shade_smooth(lens)
vw.solid(lens, (0.85, 0.98, 1.0), 1.0, 1.0)
parts.append(lens)

# tail spine with a cyan vent (the drone's thruster)
tail = lathe("Tail", [(0.0, -1.42), (0.10, -1.36), (0.20, -1.12), (0.30, -0.92), (0.36, -0.80)], 24)
vw.shade_smooth(tail)
vw.paint(tail, lambda p, n: IVORY_SHADE if -p.y > -1.2 else SLATE, lambda p, n: (0.7, 0.0), lambda p, n: 0.1)
parts.append(tail)
vent = lathe("Vent", [(0.0, -1.0), (0.24, -0.98), (0.28, -0.94)], 24)
vw.solid(vent, CYAN, 0.9, 1.0)
parts.append(vent)

# four short antennae fins along the seams
for k in range(4):
    a = math.radians(k * 90.0 + 45.0)
    d = Vector((math.cos(a), 0, math.sin(a)))
    base = F(0, 0.15, 0) + d * 0.74                       # rooted in the seam, swept back along the hull
    tip = F(0, -0.95, 0) + d * 0.92
    fin = vw.skin_object("Fin%d" % k, [base, tip], [(0, 1)], [(0.06, 0.02), (0.012, 0.012)], subdiv=1)
    vw.shade_smooth(fin)
    vw.solid(fin, IVORY, 0.8, 0.0, 0.05)
    parts.append(fin)

body = vw.join(parts, "Enemy_Sentinel")
modifier(body, 'TRIANGULATE')                              # bisect caps are n-gons Unity would drop
vw.bake_ao(body, distance=0.5)
vw.export_fbx(body, os.path.join(OUT, "Enemy_Sentinel.fbx"))
print("[sentinel] body tris", sum(len(p.vertices) - 2 for p in body.data.polygons))

# ---------------------------------------------------------------- halo ring (spins in Unity)
ring_profile = []
for s in range(13):
    a = 2 * math.pi * s / 12
    ring_profile.append((1.18 + 0.055 * math.cos(a), 0.055 * math.sin(a) * 1.6))
ring = lathe("Ring", ring_profile, 72)
ring.rotation_euler = (0, 0, 0)
vw.shade_smooth(ring)
vw.paint(ring, lambda p, n: IVORY if n.dot(Vector(p).normalized()) > -0.3 else SLATE, lambda p, n: (0.85, 0.0), lambda p, n: 0.1)
nodes = [ring]
for k in range(3):
    a = 2 * math.pi * k / 3
    c = F(math.cos(a) * 1.18, 0, math.sin(a) * 1.18)
    node = vw.ellipsoid("Node%d" % k, c, (0.10, 0.16, 0.10), segments=16, rings=10)
    vw.shade_smooth(node)
    vw.solid(node, CYAN, 0.9, 1.0)
    nodes.append(node)
ring = vw.join(nodes, "Enemy_Sentinel_Ring")
vw.bake_ao(ring, distance=0.3)
vw.export_fbx(ring, os.path.join(OUT, "Enemy_Sentinel_Ring.fbx"))
print("[sentinel] ring tris", sum(len(p.vertices) - 2 for p in ring.data.polygons))

# ---------------------------------------------------------------- preview (workbench, vertex colours)
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'VERTEX'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    scene.world = scene.world or bpy.data.worlds.new("World")
    ring.rotation_euler = (0, math.radians(25), 0)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, pos in (("front", F(2.4, 4.6, 1.5)), ("side", F(5.0, -0.6, 1.2)), ("rear", F(-2.6, -4.4, 2.0))):
        cam.location = pos
        cam.rotation_euler = (Vector((0, 0, 0)) - pos).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(PREVIEW, "sentinel_%s.png" % name)
        bpy.ops.render.render(write_still=True)
