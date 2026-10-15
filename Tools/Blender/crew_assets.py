"""
Crew suit and hand tools (#95) from three CC-BY Sketchfab models:
  - "Spacesuit (Confederation of Planets)" by tnnv (rigged, UE4-mannequin skeleton, A-pose)
    https://sketchfab.com/3d-models/spacesuit-confederation-of-planets-da7b5c14bc634f8283e2d21343afa7cc
  - "AR-H470" by Frostoise (sci-fi assault rifle)
    https://sketchfab.com/3d-models/ar-h470-4b38c160243743d681d6fdaf3bd77ddb
  - "Sci-fi pistol" by Eugen Vahrushin
    https://sketchfab.com/3d-models/sci-fi-pistol-c23394d709d14772adf355da84408edc

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/crew_assets.py -- [preview_dir]

Outputs (textures next to them, materials in CrewBuilder):
  - Art/Models/Crew/Crew_Suit.fbx    skinned suit (Spacesuit, Helmet + Glass, Face) on its own skeleton, rest pose
                                      cleaned for a Unity Humanoid: the right hand and forearm twist move from the
                                      unused IK chain onto lowerarm_r, IK / pole / prop bones go, the helmet (bone-
                                      parented to the head) becomes skinned to it, metres, facing Blender -Y
  - Art/Models/Tools/Tool_Rifle.fbx  AR-H470 scaled to 0.78 m
  - Art/Models/Tools/Tool_Blaster.fbx the pistol
  - Art/Models/Tools/Tool_Drill.fbx  kitbash: the rifle's receiver, grip, stock and sight (barrel and magazine cut
                                      away) with an own drill head (housing, collar, fluted bit, glow ring)
Tools: barrel along Blender -Y (Unity +Z), top up, origin = palm on the grip. The script prints each tool's muzzle
(Unity, relative to the grip) for PrefabBuilder.
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
PREVIEW = ARGS[0] if ARGS else None
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "_Project", "Art", "Models")
SRC = os.path.join(HERE, "Sources")

RIFLE_LENGTH = 0.78
RIFLE_GRIP = Vector((0.0, 1.78, 0.62))    # source units: palm on the pistol grip
RIFLE_MUZZLE_Y = -0.39
PISTOL_GRIP = Vector((0.045, 0.029, -0.035))
DRILL_CUT_FRONT = 0.95                    # rifle source y: everything in front of this goes (barrel, handguard)
DROP_BONES = ("upperarm_ik_r", "lowerarm_ik_r", "foot_ik_r", "foot_ik_l", "knee_pt_r", "knee_pt_l", "Sling01", "Backpack")


def U(v):
    return Vector((-v.x, v.z, -v.y))


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def select(objs, active=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def bounds(objs):
    ws = [o.matrix_world @ v.co for o in objs if o.type == 'MESH' for v in o.data.vertices]
    lo = Vector([min(w[i] for w in ws) for i in range(3)])
    hi = Vector([max(w[i] for w in ws) for i in range(3)])
    return lo, hi


def export(objs, path, armature=False):
    select(objs)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'} if armature else {'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=not armature, use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False,
        bake_anim=False, use_armature_deform_only=False, armature_nodetype='NULL', path_mode='STRIP')
    print(f"[crew_assets] exported {os.path.relpath(path, ROOT)}")


# ------------------------------------------------------------------ suit

def build_suit():
    vw.reset_scene()
    objs = import_fbx(os.path.join(SRC, "CoPSpacesuit", "CoPSpacesuit.fbx"))
    arm = next(o for o in objs if o.type == 'ARMATURE')
    arm.animation_data_clear()
    for o in objs:
        o.animation_data_clear()
    arm.name = arm.data.name = "Crew"
    # the source is saved posed: clearing the action leaves the pose values on the bones, and the FBX export writes
    # them as the bone transforms (the left arm's twist bones ended up 0.7 m off) -> back to the rest pose
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    meshes = {o.name: o for o in objs if o.type == 'MESH'}
    suit, helmet, face = meshes["Spacesuit"], meshes["Helmet"], meshes["Body.005"]

    # the helmet follows the head bone as a parented object: make it a skinned part (head weight 1)
    mw = helmet.matrix_world.copy()
    helmet.parent = None
    helmet.matrix_world = mw
    select([helmet])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    g = helmet.vertex_groups.new(name="head")
    g.add(range(len(helmet.data.vertices)), 1.0, 'REPLACE')
    helmet.parent = arm
    helmet.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = helmet.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm

    # centimetres -> metres on the rig and everything it deforms
    select([arm, suit, helmet, face], arm)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Humanoid-friendly hierarchy: the right arm's deform bones hang under the IK chain in the source
    select([arm])
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for child in ("hand_r", "lowerarm_twist_01_r"):
        eb[child].use_connect = False
        eb[child].parent = eb["lowerarm_r"]
    for n in DROP_BONES:
        if n in eb:
            eb.remove(eb[n])
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in (suit, helmet, face):
        for vg in list(o.vertex_groups):
            if vg.name in DROP_BONES:
                o.vertex_groups.remove(vg)

    suit.name = suit.data.name = "Suit"
    helmet.name = helmet.data.name = "Helmet"
    face.name = face.data.name = "Face"
    for o in (suit, helmet, face):
        o.data.uv_layers[0].name = "UVMap"
    lo, hi = bounds([suit, helmet])
    print(f"[crew_assets] suit {sum(len(p.vertices) - 2 for o in (suit, helmet, face) for p in o.data.polygons)} tris,"
          f" height {hi.z - lo.z:.3f} m, bones {len(arm.data.bones)}, mats {[m.name for o in (suit, helmet, face) for m in o.data.materials]}")
    os.makedirs(os.path.join(MODELS, "Crew"), exist_ok=True)
    export([arm, suit, helmet, face], os.path.join(MODELS, "Crew", "Crew_Suit.fbx"), armature=True)
    if PREVIEW:
        preview([suit, helmet, face], "suit", Vector((0, -1, 0.15)))


# ------------------------------------------------------------------ tools

def normalize(objs, grip, scale, rot=Matrix.Identity(4)):
    """Origin to the grip, then rotate/scale; applied into the mesh data."""
    m = Matrix.Scale(scale, 4) @ rot @ Matrix.Translation(-grip)
    for o in objs:
        o.parent = None
        o.matrix_world = m @ o.matrix_world
    select(objs)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def join(objs, name):
    select(objs)
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    return o


def muzzle(obj):
    """Front-most point of the barrel (Blender -Y), at the barrel's centre height (Unity coordinates)."""
    vs = [v.co for v in obj.data.vertices]
    y = min(v.y for v in vs)
    tip = [v for v in vs if v.y < y + 0.01]
    c = sum(tip, Vector()) / len(tip)
    return U(Vector((c.x, y, c.z)))


def build_rifle():
    vw.reset_scene()
    objs = [o for o in import_fbx(os.path.join(SRC, "ARH470", "ARH470.fbx")) if o.type == 'MESH']
    lo, hi = bounds(objs)
    normalize(objs, RIFLE_GRIP, RIFLE_LENGTH / (hi.y - lo.y))
    rifle = join(objs, "Tool_Rifle")
    print(f"[crew_assets] rifle {sum(len(p.vertices) - 2 for p in rifle.data.polygons)} tris, mats {[m.name for m in rifle.data.materials]}, muzzle {fmt(muzzle(rifle))}")
    os.makedirs(os.path.join(MODELS, "Tools"), exist_ok=True)
    export([rifle], os.path.join(MODELS, "Tools", "Tool_Rifle.fbx"))
    if PREVIEW:
        preview([rifle], "rifle", Vector((1, 0, 0.2)))


def build_pistol():
    vw.reset_scene()
    objs = [o for o in import_fbx(os.path.join(SRC, "SciFiPistol", "SciFiPistol.fbx")) if o.type == 'MESH']
    # barrel points -X in the source: turn it to -Y
    normalize(objs, PISTOL_GRIP, 1.0, Matrix.Rotation(math.radians(90), 4, 'Z'))
    pistol = join(objs, "Tool_Blaster")
    print(f"[crew_assets] blaster {sum(len(p.vertices) - 2 for p in pistol.data.polygons)} tris, mats {[m.name for m in pistol.data.materials]}, muzzle {fmt(muzzle(pistol))}")
    export([pistol], os.path.join(MODELS, "Tools", "Tool_Blaster.fbx"))
    if PREVIEW:
        preview([pistol], "blaster", Vector((1, 0, 0.2)))


COLORS = {"DrillHead": (0.09, 0.1, 0.11, 1), "DrillBit": (0.35, 0.36, 0.38, 1), "DrillGlow": (1.0, 0.45, 0.1, 1)}


def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = COLORS.get(name, (0.5, 0.5, 0.5, 1))
    return m


def bevel(o, width=0.002):
    b = o.modifiers.new("Bevel", 'BEVEL')
    b.width = width
    b.segments = 2
    b.limit_method = 'ANGLE'
    select([o])
    bpy.ops.object.modifier_apply(modifier=b.name)


def part(op, mat, loc, rot=(0, 0, 0), bev=0.002, **kw):
    op(location=loc, rotation=rot, **kw)
    o = bpy.context.view_layer.objects.active
    o.data.materials.clear()
    o.data.materials.append(material(mat))
    if bev:
        bevel(o, bev)
    return o


def cut(obj, y, keep_below):
    """Bisects at source y and keeps one side, capping the cut."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    r = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, y, 0), plane_no=(0, 1, 0),
                               clear_outer=keep_below, clear_inner=not keep_below)
    edges = [e for e in r["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    if edges:
        try:
            bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        except Exception:
            pass
    bm.to_mesh(obj.data)
    bm.free()


def build_drill():
    vw.reset_scene()
    objs = [o for o in import_fbx(os.path.join(SRC, "ARH470", "ARH470.fbx")) if o.type == 'MESH']
    lo, hi = bounds(objs)
    scale = RIFLE_LENGTH / (hi.y - lo.y)
    body = next(o for o in objs if any(m.name == "MainWeapon" for m in o.data.materials))
    sight = next(o for o in objs if any(m.name == "Scope" for m in o.data.materials))
    for o in objs:
        if o not in (body, sight):
            bpy.data.objects.remove(o, do_unlink=True)
    for o in (body, sight):   # cut in world (source) coordinates
        o.parent = None
    select([body, sight])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    cut(body, DRILL_CUT_FRONT, keep_below=False)
    normalize([body, sight], RIFLE_GRIP, scale)
    # the front face of the cut receiver, in normalized space
    front = (DRILL_CUT_FRONT - RIFLE_GRIP.y) * scale
    zc = (0.955 - RIFLE_GRIP.z) * scale          # barrel axis height
    head = []
    y = front + 0.005
    # housing: a short drum on the receiver's nose, collar, then the fluted bit
    head.append(part(bpy.ops.mesh.primitive_cylinder_add, "DrillHead", (0, y - 0.045, zc), (math.radians(90), 0, 0), vertices=24, radius=0.05, depth=0.09))
    head.append(part(bpy.ops.mesh.primitive_cylinder_add, "DrillHead", (0, y - 0.1, zc), (math.radians(90), 0, 0), vertices=24, radius=0.038, depth=0.03))
    head.append(part(bpy.ops.mesh.primitive_torus_add, "DrillGlow", (0, y - 0.093, zc), (math.radians(90), 0, 0), bev=0,
                     major_radius=0.045, minor_radius=0.0045, major_segments=32, minor_segments=8))
    head.append(part(bpy.ops.mesh.primitive_cone_add, "DrillBit", (0, y - 0.19, zc), (math.radians(90), 0, 0), bev=0.001,
                     vertices=16, radius1=0.026, radius2=0.004, depth=0.15))
    # flutes: thin twisted ribs along the bit
    for i in range(3):
        a = i * 2 * math.pi / 3
        for k in range(5):
            t = k / 5.0
            r = 0.024 - t * 0.018
            ang = a + t * 2.4
            head.append(part(bpy.ops.mesh.primitive_cube_add, "DrillBit", (math.cos(ang) * r, y - 0.13 - t * 0.12, zc + math.sin(ang) * r),
                             (0, ang + math.radians(30), 0), bev=0, size=1.0, scale=(0.006, 0.026, 0.006)))
    # side vents on the drum: glowing slots
    for s in (-1, 1):
        head.append(part(bpy.ops.mesh.primitive_cube_add, "DrillGlow", (s * 0.049, y - 0.045, zc), bev=0, size=1.0, scale=(0.003, 0.05, 0.008)))
    for o in head:
        select([o])
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(island_margin=0.02)
        bpy.ops.object.mode_set(mode='OBJECT')
    drill = join([body, sight] + head, "Tool_Drill")
    for poly in drill.data.polygons:
        poly.use_smooth = drill.data.materials[poly.material_index].name.startswith("Drill")
    print(f"[crew_assets] drill {sum(len(p.vertices) - 2 for p in drill.data.polygons)} tris, mats {[m.name for m in drill.data.materials]}, muzzle {fmt(muzzle(drill))}")
    export([drill], os.path.join(MODELS, "Tools", "Tool_Drill.fbx"))
    if PREVIEW:
        preview([drill], "drill", Vector((1, 0, 0.2)))


def fmt(v):
    return f"({v.x:+.3f}, {v.y:+.3f}, {v.z:+.3f})"


def preview(objs, name, direction):
    sc = bpy.context.scene
    lo, hi = bounds(objs)
    c, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = size * 1.05
    d = direction.normalized()
    cam.location = c + d * size * 2
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x, sc.render.resolution_y = 900, 600
    sc.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)


build_suit()
build_rifle()
build_pistol()
build_drill()
