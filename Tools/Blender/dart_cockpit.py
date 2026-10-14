"""
Dart cockpit fittings (#94) from two CC-BY Sketchfab models:
  - "Spacefighter Cockpit (Wasp Interdictor)" by Comrade1280 (consoles, side walls, stick, throttle, pedals)
    https://sketchfab.com/3d-models/spacefighter-cockpit-wasp-interdictor-db4aa67fe1164ec088083d7dac2d273f
  - "Sci-Fi Pilot Chair" by Philip McCloskey
    https://sketchfab.com/3d-models/sci-fi-pilot-chair-66d515be0bf44459a8e56807f5d4f518

Run headless (from the repo root, after fab_spaceship.py):
  blender -b --factory-startup -P Tools/Blender/dart_cockpit.py -- Assets/_Project/Art/Models/Ships [preview_dir]

The Wasp is a narrow, deep fighter tub; the Dart's tub (fab_spaceship.py) is wider and flatter. So the Wasp is
kitbashed into it: uniform scale + offset so the console sits in front of the pilot's knees, stick and throttle
move to the armrests (side stick right, throttle left), the side consoles move outwards to the Dart's walls, everything above the canopy sill (canopy frame, head box) and everything outside
the Dart's hull is cut away. The Wasp's own glass, monitors and placards are dropped (live displays come from
ShipDashboard). Outputs (original UVs, PBR materials in AssetModelBuilder):
  - Ship_Dart_Cockpit_Console.fbx   centre console, pedals, spine ("center" texture set)
  - Ship_Dart_Cockpit_Walls.fbx     side consoles and wall linings ("cockpit" texture set)
  - Ship_Dart_Cockpit_Stick.fbx     flight stick, origin = its pivot (moving part for #40)
  - Ship_Dart_Cockpit_Throttle.fbx  throttle lever, origin = its pivot
  - Ship_Dart_Cockpit_Seat.fbx      pilot chair without its swivel base ("chair" texture set)
  - Ship_Dart_Cockpit_Dash.fbx      own full-width dashboard with three display bays ("cockpit" texture set)
  - Ship_Dart_Cockpit_DashGlow*.fbx its cyan / amber light strips and LEDs (unlit glow, incl. the rear bay's)
  - Ship_Dart_Cockpit_DashSeams.fbx dark panel seams on the dashboard (own dark material)
  - Ship_Dart_Cockpit_Rear.fbx      rear bay behind the seat: avionics rack, lockers, conduits, floor plate
  - Ship_Dart_Cockpit_Frame.fbx     roll bar / canopy bows and sill rails (the canopy's frame seen from inside)
Prints the pivots and the Wasp monitor frames (Unity coordinates) for PrefabBuilder.
With a preview dir it also renders workbench shots from the pilot's eye and from outside.
Blender (x, y, z) = Unity (-x, z, -y); U() / B() convert.
"""
import sys
import os
import math
import bpy
import bmesh
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Assets/_Project/Art/Models/Ships"
PREVIEW = ARGS[1] if len(ARGS) > 1 else None
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SHIPS = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Ships")
TEX = os.path.join(SHIPS, "DartCockpit")
WASP = os.path.join(HERE, "Sources", "WaspCockpit", "WaspCockpit.fbx")
CHAIR = os.path.join(HERE, "Sources", "PilotChair", "scene.gltf")

COCKPIT_SHIFT = 0.75                    # the whole cockpit sits this far forward of the rear bulkhead (pilot mid-canopy)
LIFT = 0.05                             # ... and this much higher (the floor rises towards the nose)
EYE = Vector((0.0, 0.0, 2.92 + COCKPIT_SHIFT))  # seated eye (PrefabBuilder PilotSeat ViewPoint)
TUB_Z = (2.21, 6.26)                    # bulkheads of the Dart's tub
WASP_SCALE = 0.103                      # source units -> metres (stick top ~1.1 m above the floor)
WASP_OFFSET = Vector((0.0, -4.32 + LIFT, 4.78 + COCKPIT_SHIFT))  # Unity offset after scaling: Wasp floor on the Dart floor
SIDE_X0 = 0.36                          # Wasp islands whose centre is further out than this (scaled) are side parts ...
SIDE_SHIFT = 0.42                       # ... and move this far outwards to the Dart's walls
Y_CUT = -0.6                            # canopy sill: nothing above (the view over the nose stays open)
CONSOLE_CUT = -0.645                    # the console tower ends just above the monitor bezels (holo combiner above it)
SEAT_ZONE = (0.62, 3.45 + 0.75)                # Wasp islands inside |x| < a and z < b are its own seat (the chair replaces it)
# moving parts (and the islands around their base, e.g. brackets and boots) go to the armrest consoles:
# throttle left, stick right as a side stick, so the centre monitor stays in view
MOVES = {"throttle": Vector((-0.3, 0.06, 0.0)), "steering": Vector((0.6, 0.07, -0.08))}
MOVE_RADIUS = 0.14
MARGIN = 0.03                           # keep inside the hull skin
CHAIR_SCALE = 0.8
CHAIR_POS = Vector((0.0, -0.98 + LIFT, 2.98 + COCKPIT_SHIFT))  # Unity position of the chair's origin (bounds centre)
CHAIR_CUT = -0.42                       # source z below which the swivel base is dropped


def U(v):
    return Vector((-v.x, v.z, -v.y))


def B(u):
    return Vector((-u.x, -u.z, u.y))


def apply_all(o):
    vw.activate(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------------------------------------------------------- the Dart (clip volume + preview)
dart = {}
for name in ("Ship_Dart_Fab", "Ship_Dart_Fab_Canopy", "Ship_Dart_Fab_Cockpit"):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SHIPS, name + ".fbx"))
    o = [x for x in bpy.context.scene.objects if x not in before and x.type == 'MESH'][0]
    apply_all(o)
    dart[name] = o
hull, canopy, tub = dart["Ship_Dart_Fab"], dart["Ship_Dart_Fab_Canopy"], dart["Ship_Dart_Fab_Cockpit"]


def cast(origin_u, dir_u):
    """Nearest hit on hull or canopy (Unity in/out), None if the ray escapes."""
    o, d = B(origin_u), B(dir_u) - B(Vector())
    best = None
    for ob in (hull, canopy):
        hit, loc, *_ = ob.ray_cast(o, d)
        if hit and (best is None or (loc - o).length < (best - o).length):
            best = loc
    return None if best is None else U(best)


def inside(p):
    """Unity point inside the Dart's cockpit volume (between the bulkheads, inside the side walls, above the floor)."""
    if not (TUB_Z[0] + MARGIN < p.z < TUB_Z[1] - MARGIN):
        return False
    side = cast(Vector((0.0, p.y, p.z)), Vector((1.0 if p.x >= 0 else -1.0, 0, 0)))
    if side is not None and abs(p.x) > abs(side.x) - MARGIN:
        return False
    floor = cast(Vector((p.x, -0.3, p.z)), Vector((0, -1, 0)))
    if floor is not None and p.y < floor.y + MARGIN:
        return False
    return True


# ---------------------------------------------------------------- Wasp
before = set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=WASP)
wasp = {o.name: o for o in bpy.context.scene.objects if o not in before and o.type == 'MESH'}
for o in wasp.values():
    apply_all(o)


def to_dart(v):
    """Wasp source (Blender) -> Dart (Blender): scale + offset in Unity space."""
    return B(U(v) * WASP_SCALE + WASP_OFFSET)


for o in wasp.values():
    for v in o.data.vertices:
        v.co = to_dart(v.co)
    o.data.update()

# monitors: report their frames, then drop them with the glass and the placards
mon = wasp["monitors"]
for p in mon.data.polygons:
    if p.area < 0.005:
        continue  # LED strips
    vs = [U(mon.data.vertices[i].co) for i in p.vertices]
    c = sum(vs, Vector()) / len(vs)
    n = (U(p.normal) - U(Vector())).normalized()
    right = Vector((1, 0, 0)) - n * n.x
    right.normalize()
    up = n.cross(right) * -1
    w = max(v.dot(right) for v in vs) - min(v.dot(right) for v in vs)
    h = max(v.dot(up) for v in vs) - min(v.dot(up) for v in vs)
    print(f"[dart_cockpit] wasp monitor at ({c.x:+.3f}, {c.y:+.3f}, {c.z:+.3f}) facing ({-n.x:+.3f}, {-n.y:+.3f}, {-n.z:+.3f}) size {w:.3f} x {h:.3f}")
for name in ("monitors", "cockpit_glass", "placards"):
    if name in wasp:
        bpy.data.objects.remove(wasp.pop(name), do_unlink=True)

MOVED_FROM = {}
for name, move in MOVES.items():
    o = wasp[name]
    ps = [U(v.co) for v in o.data.vertices]
    MOVED_FROM[name] = sum(ps, Vector()) / len(ps)
    for v in o.data.vertices:
        v.co += B(move) - B(Vector())
    o.data.update()

# pedals belong to the console
console = wasp["center"]
pedals = [wasp.pop(n) for n in ("pedal_L", "pedal_R") if n in wasp]
console = vw.join([console] + pedals, "Ship_Dart_Cockpit_Console")
walls = wasp["cockpit"]
walls.name = walls.data.name = "Ship_Dart_Cockpit_Walls"


def islands(bm):
    seen, out = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, cur = [f], []
        seen.add(f)
        while stack:
            g = stack.pop()
            cur.append(g)
            for v in g.verts:
                for h in v.link_faces:
                    if h not in seen:
                        seen.add(h)
                        stack.append(h)
        out.append(cur)
    return out


def cap(bm, y_cut):
    """Fills the open loops left on the sill plane; caps take the UVs of their rim (a smear of the local colour)."""
    uv = bm.loops.layers.uv.active
    rim = [e for e in bm.edges if e.is_boundary and all(abs(U(v.co).y - y_cut) < 1e-4 for v in e.verts)]
    if not rim:
        return
    rim_uv = {}
    for e in rim:
        for v in e.verts:
            for l in v.link_loops:
                rim_uv.setdefault(v, l[uv].uv.copy())
    res = bmesh.ops.edgeloop_fill(bm, edges=rim)
    for f in res["faces"]:
        f.normal_update()
        if (B(Vector((0, 1, 0))) - B(Vector())).dot(f.normal) < 0:
            f.normal_flip()
        for l in f.loops:
            if l.vert in rim_uv:
                l[uv].uv = rim_uv[l.vert]


def kitbash(o, y_cut):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    # side parts move outwards (whole islands, so nothing tears)
    moved = 0
    for isl in islands(bm):
        vs = {v for f in isl for v in f.verts}
        cx = sum(U(v.co).x for v in vs) / len(vs)
        if abs(cx) > SIDE_X0:
            d = B(Vector((math.copysign(SIDE_SHIFT, cx), 0, 0))) - B(Vector())
            for v in vs:
                v.co += d
            moved += 1
    # the Wasp's own seat goes; the throttle bracket moves with the throttle
    dead = []
    for isl in islands(bm):
        vs = {v for f in isl for v in f.verts}
        c = sum((U(v.co) for v in vs), Vector()) / len(vs)
        if abs(c.x) < SEAT_ZONE[0] and c.z < SEAT_ZONE[1]:
            dead.extend(isl)
        else:
            for name, at in MOVED_FROM.items():
                if (c - at).length < MOVE_RADIUS:
                    d = B(MOVES[name]) - B(Vector())
                    for v in vs:
                        v.co += d
                    break
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    # sill cut: canopy frame and head box go, the view stays open; the cut is capped
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=B(Vector((0, y_cut, 0))), plane_no=B(Vector((0, 1, 0))) - B(Vector()), clear_outer=True)
    cap(bm, y_cut)
    # everything outside the Dart's hull goes
    cache = {}

    def ok(v):
        if v not in cache:
            cache[v] = inside(U(v.co))
        return cache[v]

    dead = [f for f in bm.faces if not all(ok(v) for v in f.verts)]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    print(f"[dart_cockpit] {o.name}: {moved} side islands moved, {len(dead)} faces cut, {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")


kitbash(console, CONSOLE_CUT)
kitbash(walls, Y_CUT)


def set_origin(o, unity_point):
    d = B(unity_point)
    for v in o.data.vertices:
        v.co -= d
    o.data.update()
    o.location = d


def pivot_of(o, name):
    ps = [U(v.co) for v in o.data.vertices]
    lo = Vector([min(p[i] for p in ps) for i in range(3)])
    hi = Vector([max(p[i] for p in ps) for i in range(3)])
    piv = Vector(((lo.x + hi.x) * 0.5, lo.y, (lo.z + hi.z) * 0.5))
    print(f"[dart_cockpit] {name} pivot ({piv.x:+.3f}, {piv.y:+.3f}, {piv.z:+.3f}) top {hi.y:+.3f} size {hi.x - lo.x:.3f} x {hi.y - lo.y:.3f} x {hi.z - lo.z:.3f}")
    return piv


stick = wasp["steering"]
stick.name = stick.data.name = "Ship_Dart_Cockpit_Stick"
throttle = wasp["throttle"]
throttle.name = throttle.data.name = "Ship_Dart_Cockpit_Throttle"
stick_pivot = pivot_of(stick, "stick")
throttle_pivot = pivot_of(throttle, "throttle")

# ---------------------------------------------------------------- chair
before = set(bpy.context.scene.objects)
bpy.ops.import_scene.gltf(filepath=CHAIR)
parts = [o for o in bpy.context.scene.objects if o not in before and o.type == 'MESH']
for o in parts:
    vw.activate(o)
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    apply_all(o)
seat = vw.join(parts, "Ship_Dart_Cockpit_Seat") if len(parts) > 1 else parts[0]
seat.name = seat.data.name = "Ship_Dart_Cockpit_Seat"
for o in [o for o in bpy.context.scene.objects if o not in before and o.type == 'EMPTY']:
    bpy.data.objects.remove(o, do_unlink=True)
bm = bmesh.new()
bm.from_mesh(seat.data)
geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector((0, 0, CHAIR_CUT)), plane_no=Vector((0, 0, -1)), clear_outer=True)
for v in bm.verts:
    v.co = v.co * CHAIR_SCALE + B(CHAIR_POS)
bm.to_mesh(seat.data)
bm.free()
seat.data.update()
ps = [U(v.co) for v in seat.data.vertices]
print(f"[dart_cockpit] seat y {min(p.y for p in ps):+.2f}..{max(p.y for p in ps):+.2f} z {min(p.z for p in ps):+.2f}..{max(p.z for p in ps):+.2f} "
      f"half width {max(abs(p.x) for p in ps):.2f}, {sum(len(p.vertices) - 2 for p in seat.data.polygons)} tris")


# ---------------------------------------------------------------- dashboard (own build, Wasp "cockpit" texture)
# Fills the nose of the tub in front of the pilot so the fittings read as part of the hull: a lofted shell whose
# side edges follow the hull walls at every height, a glare shield that runs from the display face all the way to
# the front bulkhead (sloping down with the nose), and a faceted wrap-around face (centre facet square to the
# pilot, side facets yawed towards him) with three display bays for the live CockpitScreens. UVs are continuous
# (x across, distance along the profile) inside the slate panel region of the recoloured Wasp "cockpit" texture,
# so the dashboard matches the side consoles; light strips and LEDs are separate glow meshes.
import random

Z_SHIFT = COCKPIT_SHIFT
FACE_BOTTOM = (-0.66, 3.93 + Z_SHIFT)            # face bottom edge (Unity y, z) at the centre facet
FACE_TOP = (-0.30, 4.09 + Z_SHIFT)               # face top edge: tilted ~24 deg towards the eye
HINGE = 0.24                                     # centre facet half width
WRAP = math.radians(24)                          # side facets yaw towards the pilot
SHIELD_END = (-0.40, TUB_Z[1] - 0.04)            # glare shield meets the front bulkhead here (y, z)
UNDER_Y = -0.86                                  # underside (knee room below)
DASH_MARGIN = 0.012
COLUMNS = [0.0, 0.12, HINGE, 0.42, 0.6, 0.76]    # inner columns (|x|); the outermost column follows the wall
PANEL_UV = (0.565, 0.535, 0.23, 0.135)          # u0, v0, du, dv: slate panel region of the cockpit texture
PANEL_SCALE = 0.075                              # uv per metre


def half_width(y, z):
    hit = cast(Vector((0.0, y, z)), Vector((1, 0, 0)))
    return abs(hit.x) if hit is not None else 0.9


def wrap(x):
    """Face z offset of the wrap-around: side facets come towards the pilot."""
    return -max(0.0, abs(x) - HINGE) * math.tan(WRAP)


def face_point(x, b, out=0.0):
    """Point on the faceted face at x, b = 0 bottom .. 1 top, pushed `out` along the facet normal."""
    y = FACE_BOTTOM[0] + (FACE_TOP[0] - FACE_BOTTOM[0]) * b
    z = FACE_BOTTOM[1] + (FACE_TOP[1] - FACE_BOTTOM[1]) * b + wrap(x)
    n = face_normal(x)
    return Vector((x, y, z)) + n * out


def face_normal(x):
    up = Vector((0, FACE_TOP[0] - FACE_BOTTOM[0], FACE_TOP[1] - FACE_BOTTOM[1])).normalized()
    s = 0.0 if abs(x) <= HINGE else math.copysign(1.0, x)
    right = Vector((1, 0, -s * math.tan(WRAP))).normalized()
    n = right.cross(up)
    return n if n.z < 0 else -n


def face_axes(x):
    up = Vector((0, FACE_TOP[0] - FACE_BOTTOM[0], FACE_TOP[1] - FACE_BOTTOM[1])).normalized()
    s = 0.0 if abs(x) <= HINGE else math.copysign(1.0, x)
    right = Vector((1, 0, -s * math.tan(WRAP))).normalized()
    n = face_normal(x)
    up = n.cross(right).normalized()
    return right, up, n


# profile rows, each a function x -> (y, z), with the UV group it belongs to (continuous v inside a group)
def row_face(b):
    return lambda x: tuple(face_point(x, b)[1:])


def row_shield(t):
    def f(x):
        y0, z0 = FACE_TOP[0] + 0.045, FACE_TOP[1] - 0.05 + wrap(x)
        return (y0 + (SHIELD_END[0] - y0) * t, z0 + (SHIELD_END[1] - z0) * t)
    return f


ROWS = (
    [("under", lambda x: (UNDER_Y, SHIELD_END[1])), ("under", lambda x: (UNDER_Y, FACE_BOTTOM[1] + 0.3 + wrap(x)))]
    + [("face", row_face(b)) for b in (0.0, 0.25, 0.5, 0.75, 1.0)]
    + [("face", lambda x: (FACE_TOP[0] + 0.015, FACE_TOP[1] - 0.06 + wrap(x)))]          # sun hood lip
    + [("shield", row_shield(t)) for t in (0.0, 0.08, 0.2, 0.35, 0.5, 0.65, 0.8, 1.0)]
)
xs = sorted({-c for c in COLUMNS} | set(COLUMNS))
dash_bm = bmesh.new()
grid = []
for group, f in ROWS:
    row = []
    for i, x in enumerate([None] + xs + [None]):
        y, z = f(0.0 if x is None else x)
        if x is None:  # outermost columns sit on the hull wall at this height (twice: the wrap moves z with x)
            w = 0.9
            for _ in range(2):
                y, z = f(w)
                w = half_width(y, z) - DASH_MARGIN
            x = -w if i == 0 else w
            y, z = f(x)
        row.append(dash_bm.verts.new(B(Vector((x, y, z)))))
    grid.append((group, row))
faces = []
nr = len(grid)
for r in range(nr):
    a, b = grid[r][1], grid[(r + 1) % nr][1]
    for c in range(len(a) - 1):
        faces.append(dash_bm.faces.new((a[c], a[c + 1], b[c + 1], b[c])))
for side in (0, -1):  # caps against the hull walls
    ring = [grid[r][1][side] for r in range(nr)]
    faces.append(dash_bm.faces.new(ring if side == 0 else list(reversed(ring))))
bmesh.ops.recalc_face_normals(dash_bm, faces=faces)
dash_bm.normal_update()
centre = B(Vector((0, -0.55, (FACE_TOP[1] + SHIELD_END[1]) * 0.5)))
for f in faces:
    if f.normal.dot(f.calc_center_median() - centre) < 0:
        f.normal_flip()
# continuous UVs: u = x, v = distance along the profile within the row's group
uvl = dash_bm.loops.layers.uv.new("UVChannel_1")
group_v = {"face": 0.0, "shield": 0.0, "under": 0.0}
dist = []
prev = None
for r, (group, row) in enumerate(grid):
    p = U(row[len(row) // 2].co)
    if prev is not None and prev[0] == group:
        group_v[group] += (p - prev[1]).length * PANEL_SCALE
    dist.append(group_v[group])
    prev = (group, p)
voff = {"face": 0.0, "shield": 0.02, "under": 0.0}
for f in dash_bm.faces:
    for l in f.loops:
        q = U(l.vert.co)
        r = next(i for i, (g, row) in enumerate(grid) if l.vert in row)
        g = grid[r][0]
        # loops of a quad that wraps from the last row to the first use their own row's value
        l[uvl].uv = (PANEL_UV[0] + PANEL_UV[2] * 0.5 + q.x * PANEL_SCALE, PANEL_UV[1] + voff[g] + min(PANEL_UV[3] - voff[g], dist[r]))


def add_box(bm, centre_u, axes, size, bevel=0.003):
    """Box at a Unity centre with Unity axes (right, up, out) and size; bevelled edges; UVs in the panel region."""
    r, u, o = axes
    vs = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                pnt = centre_u + r * (sx * size[0] * 0.5) + u * (sy * size[1] * 0.5) + o * (sz * size[2] * 0.5)
                vs.append(bm.verts.new(B(pnt)))
    idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    fs = [bm.faces.new([vs[i] for i in q]) for q in idx]
    bm.normal_update()
    c = B(centre_u)
    for f in fs:
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()
    if bevel > 0:
        res = bmesh.ops.bevel(bm, geom=list({e for f in fs for e in f.edges}), offset=bevel, segments=1, affect='EDGES', profile=0.5)
        fs = fs + list(res.get("faces", []))
    layer = bm.loops.layers.uv.active
    if layer is not None:
        for f in fs:
            if not f.is_valid:
                continue
            for l in f.loops:
                q = U(l.vert.co)
                l[layer].uv = (PANEL_UV[0] + 0.02 + (q.x * 0.6 + q.z * 0.3) * PANEL_SCALE % (PANEL_UV[2] - 0.04),
                               PANEL_UV[1] + 0.02 + (q.y * 0.7) * PANEL_SCALE % (PANEL_UV[3] - 0.04))
    return fs


cyan_bm = bmesh.new()
amber_bm = bmesh.new()
cyan_bm.loops.layers.uv.new("UVChannel_1")
amber_bm.loops.layers.uv.new("UVChannel_1")
# display bays: proud frames, a dark back plate, a status strip under each
DISPLAYS = [("Flight", 0.0, 0.40, 0.22), ("Radar", -(HINGE + 0.03 + 0.15), 0.30, 0.22), ("Drive", HINGE + 0.03 + 0.15, 0.30, 0.22)]
screens_out = []
for name, xd, w, h in DISPLAYS:
    # centre of a side display: 0.18 m along its facet from the hinge
    if xd != 0.0:
        along = 0.03 + w * 0.5
        right, up, n = face_axes(xd)
        hinge = face_point(math.copysign(HINGE, xd), 0.5)
        c0 = hinge + right * (along if xd > 0 else -along)
    else:
        right, up, n = face_axes(0.0)
        c0 = face_point(0.0, 0.5)
    axes = (right, up, n)
    fw, depth = 0.016, 0.014
    for (a, b, sw, sh) in ((0, (h + fw) * 0.5, w + fw * 2, fw), (0, -(h + fw) * 0.5, w + fw * 2, fw),
                           (-(w + fw) * 0.5, 0, fw, h), ((w + fw) * 0.5, 0, fw, h)):
        add_box(dash_bm, c0 + right * a + up * b + n * (depth * 0.5), axes, (sw, sh, depth))
    add_box(dash_bm, c0 + n * 0.002, axes, (w + 0.004, h + 0.004, 0.004), bevel=0)
    add_box(cyan_bm, c0 - up * (h * 0.5 + fw + 0.006) + n * 0.003, axes, (w * 0.6, 0.003, 0.004), bevel=0)
    screens_out.append((name, c0 + n * 0.005, -n, w, h))
# button banks with LEDs on the outer facets
for side in (-1, 1):
    right, up, n = face_axes(side)
    base = face_point(side * HINGE, 0.5) + right * (side * (0.03 + 0.30 + 0.07))
    for row in range(3):
        for col in range(3):
            c = base + right * (side * col * 0.05) + up * (0.06 - row * 0.06)
            add_box(dash_bm, c + n * 0.008, (right, up, n), (0.036, 0.03, 0.016), bevel=0.003)
            led = cyan_bm if (row + col) % 3 else amber_bm
            add_box(led, c + up * 0.009 + n * 0.0165, (right, up, n), (0.018, 0.005, 0.002), bevel=0)
# light strips, one segment per facet: cyan just below the sun hood lip (on the face, so the pilot looking down
# over it sees it), a thin amber one along the face bottom
for bm_, b_row, thick in ((cyan_bm, 0.93, 0.007), (amber_bm, 0.03, 0.003)):
    for x0, x1 in ((-0.74, -HINGE), (-HINGE, HINGE), (HINGE, 0.74)):
        p0, p1 = face_point(x0, b_row, 0.004), face_point(x1, b_row, 0.004)
        mid = (p0 + p1) * 0.5
        right = (p1 - p0).normalized()
        _, up, n = face_axes((x0 + x1) * 0.5)
        add_box(bm_, mid, (right, up, n), ((p1 - p0).length - 0.02, thick, 0.004), bevel=0)


def strip(bm, pts, normal, width=0.01, height=0.003):
    """A thin flat strip along a polyline of Unity points lying on a surface with the given normal (seams, rails)."""
    for a, b in zip(pts, pts[1:]):
        d = b - a
        if d.length < 1e-4:
            continue
        right = d.normalized()
        n = (normal - right * normal.dot(right)).normalized()
        up = n.cross(right).normalized()
        add_box(bm, (a + b) * 0.5 + n * (height * 0.5), (right, up, n), (d.length + width, width, height), bevel=0)


# panel seams (dark, own material): facet folds and a cut line under the displays on the face, cross seams and
# two long seams on the glare shield, so the big surfaces read as plates
seam_bm = bmesh.new()
seam_bm.loops.layers.uv.new("UVChannel_1")
for x in (-HINGE, HINGE, -0.62, 0.62):
    strip(seam_bm, [face_point(x, b, 0.001) for b in (0.02, 0.5, 0.9)], face_axes(x * 0.99)[2])
for b in (0.1,):
    for x0, x1 in ((-0.74, -HINGE), (-HINGE, HINGE), (HINGE, 0.74)):
        strip(seam_bm, [face_point(x0, b, 0.001), face_point(x1, b, 0.001)], face_axes((x0 + x1) * 0.5)[2])


def shield_point(x, t, lift=0.0015):
    y, z = row_shield(t)(x)
    return Vector((x, y + lift, z))


def shield_span(t):
    y, z = row_shield(t)(0.0)
    return half_width(y, z) - DASH_MARGIN - 0.03


shield_n = (shield_point(0, 1) - shield_point(0, 0)).cross(Vector((1, 0, 0))).normalized()
if shield_n.y < 0:
    shield_n = -shield_n
for t in (0.3, 0.72):
    w = shield_span(t)
    xs_ = [-w, -0.42, -HINGE, 0.0, HINGE, 0.42, w]
    strip(seam_bm, [shield_point(x, t) for x in xs_], shield_n)
for x in (-0.42, 0.42):
    strip(seam_bm, [shield_point(x, t) for t in (0.04, 0.3, 0.72, 0.97)], shield_n)
# roof vents near the front of the glare shield
for i in range(7):
    t = 0.55 + i * 0.03
    y, z = row_shield(t)(0.0)
    add_box(dash_bm, Vector((0, y + 0.005, z)), (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, -1))), (0.46, 0.008, 0.012), bevel=0.002)


# ---------------------------------------------------------------- rear bay, roll bar, canopy frame
# The ~0.9 m between the seat back and the rear bulkhead: avionics rack in the middle, lockers left and right,
# conduits across the bulkhead, a floor plate; a roll bar right behind the seat that doubles as the canopy's rear
# bow, a second bow on the bulkhead and rails along both sills, so the canopy reads as framed glass from inside.
# Same panel UVs as the dashboard ("cockpit" texture set), LEDs into the glow meshes.
SEAT_BACK_Z = 3.18 + 0.0                        # seat z min (printed above), the bay ends just behind it
REAR_Z = TUB_Z[0] + 0.02
SILL_Y = Y_CUT


def floor_y(x, z):
    hit = cast(Vector((x, -0.3, z)), Vector((0, -1, 0)))
    return hit.y if hit is not None else -1.9


def add_tube(bm, a, b, r, seg=10):
    """Closed cylinder from a to b (Unity points), UVs in the panel region."""
    ax = (b - a).normalized()
    t1 = ax.cross(Vector((0, 1, 0)) if abs(ax.y) < 0.9 else Vector((1, 0, 0))).normalized()
    t2 = ax.cross(t1).normalized()
    rings = []
    for p in (a, b):
        rings.append([bm.verts.new(B(p + (t1 * math.cos(i * 2 * math.pi / seg) + t2 * math.sin(i * 2 * math.pi / seg)) * r)) for i in range(seg)])
    fs = [bm.faces.new((rings[0][i], rings[0][(i + 1) % seg], rings[1][(i + 1) % seg], rings[1][i])) for i in range(seg)]
    fs.append(bm.faces.new(list(reversed(rings[0]))))
    fs.append(bm.faces.new(rings[1]))
    bm.normal_update()
    c = B((a + b) * 0.5)
    for f in fs:
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()
    layer = bm.loops.layers.uv.active
    for k, f in enumerate(fs):
        for l in f.loops:
            q = U(l.vert.co)
            l[layer].uv = (PANEL_UV[0] + 0.02 + ((q - a).dot(ax) * 0.5) * PANEL_SCALE % (PANEL_UV[2] - 0.04),
                           PANEL_UV[1] + 0.02 + (k % seg) / seg * (PANEL_UV[3] - 0.04))
    return fs


def tube_path(bm, pts, r, seg=10):
    for a, b in zip(pts, pts[1:]):
        add_tube(bm, a, b + (b - a).normalized() * r * 0.5, r, seg)


AX = (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))  # Unity axes: boxes facing the pilot (+z towards the nose)
rear_bm = bmesh.new()
rear_bm.loops.layers.uv.new("UVChannel_1")
fl = max(floor_y(x, z) for x in (-0.4, 0.0, 0.4) for z in (REAR_Z + 0.1, SEAT_BACK_Z - 0.1))
print(f"[dart_cockpit] rear bay floor {fl:+.2f}, half width at sill {half_width(SILL_Y, REAR_Z + 0.2):.2f}, at floor {half_width(fl + 0.1, REAR_Z + 0.2):.2f}")

# avionics rack: body, four module drawers with LED strips, a vent grille on top
RACK_W, RACK_D, RACK_TOP = 0.78, 0.36, SILL_Y + 0.08
rack_h = RACK_TOP - fl
add_box(rear_bm, Vector((0, fl + rack_h * 0.5, REAR_Z + RACK_D * 0.5)), AX, (RACK_W, rack_h, RACK_D), bevel=0.008)
front = REAR_Z + RACK_D
mod_h = (rack_h - 0.16) / 4
for i in range(4):
    y = fl + 0.06 + mod_h * (i + 0.5)
    add_box(rear_bm, Vector((0, y, front + 0.012)), AX, (RACK_W - 0.08, mod_h - 0.025, 0.024), bevel=0.004)
    for sx in (-1, 1):  # drawer handles
        add_box(rear_bm, Vector((sx * (RACK_W * 0.5 - 0.09), y, front + 0.032)), AX, (0.018, mod_h * 0.55, 0.018), bevel=0.003)
    add_box(cyan_bm if i % 2 else amber_bm, Vector((0.12, y + mod_h * 0.28, front + 0.0255)), AX, (0.22 if i % 2 else 0.05, 0.006, 0.002), bevel=0)
    for k in range(3):
        add_box(cyan_bm, Vector((-0.2 + k * 0.035, y + mod_h * 0.28, front + 0.0255)), AX, (0.014, 0.008, 0.002), bevel=0)
for k in range(9):  # vent slats on the rack top
    add_box(rear_bm, Vector((-0.28 + k * 0.07, RACK_TOP + 0.006, REAR_Z + RACK_D * 0.5)), AX, (0.03, 0.012, RACK_D - 0.08), bevel=0.002)
add_box(cyan_bm, Vector((0, fl + 0.025, front + 0.004)), AX, (RACK_W - 0.04, 0.006, 0.004), bevel=0)  # kick light

# lockers left and right of the rack, up to the walls (which lean in towards the sill)
for sx in (-1, 1):
    top = SILL_Y - 0.02
    x_in = RACK_W * 0.5 + 0.04
    x_out = min(half_width(y, REAR_Z + 0.3) for y in (fl + 0.15, (fl + top) * 0.5, top)) - 0.04
    if x_out - x_in < 0.12:
        continue
    w, h, d = x_out - x_in, top - fl - 0.02, 0.30
    cx = sx * (x_in + w * 0.5)
    add_box(rear_bm, Vector((cx, fl + 0.02 + h * 0.5, REAR_Z + d * 0.5)), AX, (w, h, d), bevel=0.008)
    add_box(rear_bm, Vector((cx, fl + 0.02 + h * 0.5, REAR_Z + d + 0.008)), AX, (w - 0.05, h - 0.06, 0.016), bevel=0.004)  # door
    add_box(rear_bm, Vector((cx - sx * (w * 0.5 - 0.07), fl + 0.02 + h * 0.55, REAR_Z + d + 0.03)), AX, (0.02, 0.16, 0.022), bevel=0.003)  # handle
    add_box(amber_bm, Vector((cx, fl + 0.02 + h - 0.06, REAR_Z + d + 0.0165)), AX, (w - 0.12, 0.01, 0.002), bevel=0)  # accent

# conduits across the bulkhead above the rack, bending down into the corners
for k, (y, r) in enumerate(((SILL_Y + 0.2, 0.03), (SILL_Y + 0.27, 0.022), (SILL_Y + 0.33, 0.018))):
    z = REAR_Z + 0.05 + k * 0.035
    w = half_width(y, z) - 0.05
    if w < 0.3:
        continue
    tube_path(rear_bm, [Vector((-w, y - 0.0, z)), Vector((-w * 0.7, y, z)), Vector((w * 0.7, y, z)), Vector((w, y, z))], r)
    for sx in (-1, 1):  # clamps
        for xx in (0.25, 0.55):
            add_box(rear_bm, Vector((sx * w * xx, y, z)), AX, (0.03, r * 2.6, r * 2.6), bevel=0.003)

# floor plate with ribs between the bay and the seat
pl_z0, pl_z1 = REAR_Z + 0.3, SEAT_BACK_Z
add_box(rear_bm, Vector((0, fl + 0.012, (pl_z0 + pl_z1) * 0.5)), AX, (1.0, 0.024, pl_z1 - pl_z0), bevel=0.004)
for k in range(6):
    add_box(rear_bm, Vector((0, fl + 0.028, pl_z0 + 0.05 + k * (pl_z1 - pl_z0 - 0.1) / 5)), AX, (0.9, 0.01, 0.025), bevel=0.002)


def bow(z, inset, steps=24):
    """Points of a canopy bow at z: the inner canopy/hull surface from sill to sill, pushed `inset` inwards."""
    pts = []
    centre = Vector((0.0, SILL_Y, z))
    for i in range(steps + 1):
        a = math.pi * i / steps
        d = Vector((math.cos(a), math.sin(a), 0.0))
        hit = cast(centre, d)
        if hit is None:
            continue
        pts.append(hit - d * inset)
    for _ in range(3):  # smooth out the steps where the rays switch between glass and hull
        pts = [pts[0]] + [(pts[i - 1] + pts[i] * 2 + pts[i + 1]) * 0.25 for i in range(1, len(pts) - 1)] + [pts[-1]]
    return pts


frame_bm = bmesh.new()
frame_bm.loops.layers.uv.new("UVChannel_1")
ROLL_Z = SEAT_BACK_Z - 0.1
for z, r, inset in ((ROLL_Z, 0.035, 0.06), (REAR_Z + 0.06, 0.025, 0.045)):
    pts = bow(z, inset)
    if len(pts) > 2:
        tube_path(frame_bm, pts, r, 12)
        if z == ROLL_Z:  # twin bar + cross braces down to the bay
            tube_path(frame_bm, [p + Vector((0, 0, -0.08)) for p in pts], r * 0.6, 8)
            print(f"[dart_cockpit] roll bar at z {z:.2f}: top {max(p.y for p in pts):+.2f}, {len(pts)} points")
# sill rails, front bulkhead to rear, hugging the walls just above the cut
for sx in (-1, 1):
    pts = []
    for i in range(17):
        z = REAR_Z + 0.05 + (TUB_Z[1] - 0.08 - REAR_Z - 0.05) * i / 16
        pts.append(Vector((sx * (half_width(SILL_Y + 0.03, z) - 0.03), SILL_Y + 0.03, z)))
    tube_path(frame_bm, pts, 0.02, 8)


def to_object(bm_, name):
    me = bpy.data.meshes.new(name)
    bm_.to_mesh(me)
    bm_.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


dash = to_object(dash_bm, "Ship_Dart_Cockpit_Dash")
dash_cyan = to_object(cyan_bm, "Ship_Dart_Cockpit_DashGlowCyan")
dash_amber = to_object(amber_bm, "Ship_Dart_Cockpit_DashGlowAmber")
dash_seams = to_object(seam_bm, "Ship_Dart_Cockpit_DashSeams")
rear = to_object(rear_bm, "Ship_Dart_Cockpit_Rear")
frame = to_object(frame_bm, "Ship_Dart_Cockpit_Frame")
print(f"[dart_cockpit] rear {sum(len(p.vertices) - 2 for p in rear.data.polygons)} tris, frame {sum(len(p.vertices) - 2 for p in frame.data.polygons)} tris")
print(f"[dart_cockpit] dash {sum(len(p.vertices) - 2 for p in dash.data.polygons)} tris")
for name, c, look, w, h in screens_out:
    print(f"[dart_cockpit] dash screen {name} at ({c.x:+.3f}, {c.y:+.3f}, {c.z:+.3f}) look ({look.x:+.3f}, {look.y:+.3f}, {look.z:+.3f}) size {w:.2f} x {h:.2f}")

# ---------------------------------------------------------------- preview (workbench, albedo only)
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)

    def albedo(o, path):
        m = bpy.data.materials.new(o.name)
        m.use_nodes = True
        nt = m.node_tree
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(path)
        nt.links.new(t.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
        o.data.materials.clear()
        o.data.materials.append(m)

    albedo(console, os.path.join(TEX, "Wasp_center_albedo.jpg"))
    albedo(stick, os.path.join(TEX, "Wasp_center_albedo.jpg"))
    albedo(throttle, os.path.join(TEX, "Wasp_center_albedo.jpg"))
    albedo(walls, os.path.join(TEX, "Wasp_cockpit_albedo.jpg"))
    albedo(dash, os.path.join(TEX, "Wasp_cockpit_albedo_vw.png"))
    albedo(rear, os.path.join(TEX, "Wasp_cockpit_albedo_vw.png"))
    albedo(frame, os.path.join(TEX, "Wasp_cockpit_albedo_vw.png"))
    albedo(seat, os.path.join(TEX, "Chair_baseColor.jpg"))
    albedo(hull, os.path.join(SHIPS, "FabSpaceShip", "FabShip_Base_color.jpg"))
    canopy.hide_render = True
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 960, 540
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.clip_start = 0.05
    shots = {
        "eye": (EYE, EYE + Vector((0, -0.35, 1)), 80),
        "eye_down": (EYE, EYE + Vector((0, -1.1, 1)), 80),
        "eye_left": (EYE, EYE + Vector((-1, -0.6, 0.6)), 80),
        "back": (EYE + Vector((0, 0.1, 0.3)), EYE + Vector((0, -0.6, -1)), 90),
        "up": (EYE, EYE + Vector((0, 1, -0.3)), 100),
        "above": (Vector((0, 3.2, 4.6)), Vector((0, -1.1, 4.7)), 60),
        "side": (Vector((-3.2, 0.6, 4.3)), Vector((0, -0.9, 4.6)), 60),
        "outside": (Vector((-6, 2.5, 12)), Vector((0, -0.6, 4.5)), 35),
    }
    for k, (pos, tgt, fov) in shots.items():
        hull.hide_render = k in ("above", "side")
        cam.location = B(pos)
        cam.rotation_euler = (B(tgt) - B(pos)).to_track_quat('-Z', 'Y').to_euler()
        cam.data.angle = math.radians(fov)
        sc.render.filepath = os.path.join(PREVIEW, "cockpit_" + k + ".png")
        bpy.ops.render.render(write_still=True)
    print("[dart_cockpit] previews in", PREVIEW)

# ---------------------------------------------------------------- export
set_origin(stick, stick_pivot)
set_origin(throttle, throttle_pivot)
os.makedirs(OUT, exist_ok=True)
for o in (console, walls, stick, throttle, seat, dash, dash_cyan, dash_amber, dash_seams, rear, frame):
    o.data.materials.clear()
    vw.export_fbx(o, os.path.join(OUT, o.name + ".fbx"))
    print("[dart_cockpit] exported", o.name)
