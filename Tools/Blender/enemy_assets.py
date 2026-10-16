"""
Enemy + Warden boss models from free Sketchfab assets (#98, all CC BY 4.0, see Assets/_Project/Art/CREDITS.md):
  Enemy_SwarmHunter      "I_Ship" by LaVADraGoN                         (legacy PirateFighter, static PBR)
  Enemy_SwarmBroodship   "E_Ship_A" by LaVADraGoN                       (legacy PirateGunship, static PBR)
  Enemy_Crawler_Insect   "Insectoid Monster Rig" by DM-913              (Crawler, rigged, clip Prowl)
  Enemy_Spitter_Stalker  "Alien Ninja Creature with 45 animations"
                         by Jungle Jim                                  (Spitter, rigged, 4 of its 40 clips)
  Enemy_Critter_Squid    "Space cephalopod" by XennaDemonorph           (FlyingCritter, static PBR, swims procedurally)
  Enemy_Guardian_Beast   "Four-Legged-Frost-Predator." by bensimulator2 (NestGuardian, Rigify, 6 of 7 clips)
  Boss_Warden            "alien droneship" by hoborobot                 (Warden boss, untextured 428k tris in 5 chunks,
                                                                         decimated to 60k, made two-sided, painted
                                                                         ivory/cyan in the vw vertex contract)

Run headless from the repo root:
  ingest (once, from the Sketchfab glTF zips the user downloaded):
    blender -b --factory-startup -P Tools/Blender/enemy_assets.py -- --ingest <Downloads dir>
      -> Tools/Blender/Sources/Enemies/<Name>.zip (scene.gltf + scene.bin + license.txt, no textures)
      -> Assets/_Project/Art/Models/Enemies/<Name>/<material>_<baseColor|normal|metallicRoughness|emissive>.jpg (<= 2K)
  build (reproducible from the repo):
    blender -b --factory-startup -P Tools/Blender/enemy_assets.py -- [--only Name] [--preview dir]
      -> Assets/_Project/Art/Models/Enemies/<Name>.fbx

Contract (AGENTS.md): 1 unit = 1 m, forward = Blender -Y (Unity +Z), up +Z. Static models: origin = bounds centre.
Walkers: origin between the feet (bounds centre in x/y, lowest point of the first idle frame at z 0). Only the skinned
meshes are kept (the Sketchfab scenes carry a scene-sized helper sphere). The placement is baked into the armature's
rest and the meshes (bone translation keys scaled along); rigs whose rest is not the skinned arrangement (the Rigify
predator) are first rebaked onto the idle pose (rebake_rest). Only the used clips are exported, one FBX take each,
weighted bones only. Material names stay the glTF names; EnemyModelBuilder maps them to URP Lit materials from the
texture folder (Enemy*); the boss uses the vertex-contract hull shader.
Blender pitfall: animation only lands on the evaluated copy (`obj.evaluated_get(depsgraph)`), read poses from there.
"""
import sys
import os
import math
import glob
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
SRC_DIR = os.path.join(HERE, "Sources", "Enemies")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Enemies")
MAX_TEX = 2048


def arg(name):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else None


# name: (download zip stem, kind, size measure, target metres, yaw deg (forward -> -Y), extra)
#   kind: static (textured), walker (rigged, origin at the feet), boss (decimate + paint)
#   measure: 'length' (extent along forward), 'height', 'max'
ASSETS = {
    "Enemy_SwarmHunter":     ("i_ship", "static", "length", 7.5, -90.0, {}),
    "Enemy_SwarmBroodship":  ("e_ship_a", "static", "length", 18.0, 0.0, {}),
    "Enemy_Crawler_Insect":  ("insectoid_monster_rig", "walker", "length", 1.9, 0.0, {}),
    "Enemy_Spitter_Stalker": ("alien_ninja_creature_with_45_animations", "walker", "height", 2.4, 0.0,
                              {"clips": ["A_idle", "A_walk_F", "A_run_F", "A_daggerthrow_01"]}),
    "Enemy_Critter_Squid":   ("space_cephalopod", "static", "max", 2.6, 0.0, {}),
    "Enemy_Guardian_Beast":  ("four-legged-frost-predator.", "walker", "length", 6.0, 0.0,
                              {"clips": ["Idle", "walk", "Run", "attack", "Powerfull_attack", "Roar"], "rebake": True}),
    "Boss_Warden":           ("alien_droneship", "boss", "length", 95.0, 180.0, {"tris": 60000}),
}

IVORY = vw.srgb("#E2E6EA")
IVORY_SHADE = vw.srgb("#A9B3BE")
SLATE = vw.srgb("#2A323D")
CYAN = vw.srgb("#3FE0FF")


# ---------------------------------------------------------------- ingest

def ingest(downloads):
    os.makedirs(SRC_DIR, exist_ok=True)
    for name, (stem, *_rest) in ASSETS.items():
        z = os.path.join(downloads, stem + ".zip")
        if not os.path.exists(z):
            print("[enemies] ingest: missing", z)
            continue
        tmp = tempfile.mkdtemp(prefix="vw_ingest_")
        with zipfile.ZipFile(z) as src:
            # only the files we read: the glTF, its buffer, the licence and the texture images
            for n in src.namelist():
                base = os.path.basename(n)
                if base in ("scene.gltf", "scene.bin", "license.txt") or n.startswith("textures/") and base.lower().endswith((".png", ".jpg", ".jpeg")):
                    src.extract(n, tmp)
        with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as dst:
            for n in ("scene.gltf", "scene.bin", "license.txt"):
                dst.write(os.path.join(tmp, n), n)
        tex_out = os.path.join(OUT, name)
        os.makedirs(tex_out, exist_ok=True)
        for f in sorted(glob.glob(os.path.join(tmp, "textures", "*"))):
            base, ext = os.path.splitext(os.path.basename(f))
            if "clearcoat" in base:
                continue
            img = bpy.data.images.load(f)
            w, h = img.size
            if max(w, h) > MAX_TEX:
                s = MAX_TEX / max(w, h)
                img.scale(int(w * s), int(h * s))
            # every source material is opaque (the wings' 85 % alpha is a factor, not a map): JPEG is enough
            scene = bpy.context.scene
            scene.render.image_settings.file_format = 'JPEG'
            scene.render.image_settings.quality = 92
            scene.render.image_settings.color_mode = 'RGB'
            out_ext = ".jpg"
            img.save_render(os.path.join(tex_out, base + out_ext), scene=scene)
            bpy.data.images.remove(img)
        shutil.rmtree(tmp, ignore_errors=True)
        print("[enemies] ingest:", name)


# ---------------------------------------------------------------- import + helpers

def import_source(name):
    tmp = tempfile.mkdtemp(prefix="vw_src_")
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip")) as z:
        z.extractall(tmp)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(tmp, "scene.gltf"))
    shutil.rmtree(tmp, ignore_errors=True)


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons)]


def world_bounds(objs, evaluated=True):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e18,) * 3)
    hi = Vector((-1e18,) * 3)
    for o in objs:
        src = o.evaluated_get(dg) if evaluated else o
        me = src.to_mesh()
        mw = o.matrix_world
        for v in me.vertices:
            w = mw @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
        src.to_mesh_clear()
    return lo, hi


def measure(lo, hi, how):
    size = hi - lo
    return {"length": size.y, "height": size.z, "max": max(size)}[how]


def bake_static(objs, name):
    """Applies every parent/object transform and joins the meshes into one object."""
    for o in objs:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in objs:
        vw.activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj = vw.join(objs, name)
    for o in [o for o in bpy.context.scene.objects if o != obj]:
        bpy.data.objects.remove(o, do_unlink=True)
    return obj


def transform_mesh(obj, m):
    obj.data.transform(m)
    obj.data.update()


def export_static(obj, path, contract=False):
    vw.activate(obj)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False,
        colors_type='SRGB', prioritize_active_color=contract, path_mode='STRIP')


# ---------------------------------------------------------------- static (textured) models

def build_static(name, how, target, yaw):
    obj = bake_static(meshes(), name)
    transform_mesh(obj, Matrix.Rotation(math.radians(yaw), 4, 'Z'))
    lo, hi = world_bounds([obj], evaluated=False)
    s = target / measure(lo, hi, how)
    c = (lo + hi) * 0.5
    transform_mesh(obj, Matrix.Scale(s, 4) @ Matrix.Translation(-c))
    lo, hi = world_bounds([obj], evaluated=False)
    print("[enemies] %s static size %s tris %d" % (name, tuple(round(v, 2) for v in hi - lo), tris(obj)))
    export_static(obj, os.path.join(OUT, name + ".fbx"))
    return [obj]


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ---------------------------------------------------------------- rigged walkers

def build_walker(name, how, target, yaw, extra):
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if not arms:
        raise RuntimeError(name + ": no armature")
    arm = arms[0]
    # only the skinned meshes: Sketchfab scenes carry stray helpers (an "Icosphere" the size of the whole scene)
    objs = [o for o in meshes() if any(m.type == 'ARMATURE' for m in o.modifiers)]
    keep = extra.get("clips")
    actions = [a for a in bpy.data.actions if keep is None or a.name.split("|")[-1] in keep]
    idle = next((a for a in actions if "idle" in a.name.lower()), actions[0] if actions else None)
    # the importer stacks every clip as an NLA track: drop them (and the unused clips) before any pose is evaluated
    if arm.animation_data is not None:
        for t in list(arm.animation_data.nla_tracks):
            arm.animation_data.nla_tracks.remove(t)
    for a in [a for a in bpy.data.actions if a not in actions]:
        bpy.data.actions.remove(a)
    # morph-target tracks (glTF shape key animations) are not used
    for o in objs:
        sk = o.data.shape_keys
        if sk is not None and sk.animation_data is not None:
            sk.animation_data_clear()
    # flatten the glTF scene graph: the armature (with its skinned meshes) is the only hierarchy
    mw = arm.matrix_world.copy()
    arm.parent = None
    arm.matrix_world = mw
    # skinned meshes stay unparented (the Armature modifier binds them): parent inverses would skew the FBX bind poses
    for o in objs:
        omw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = omw
    for o in [o for o in bpy.context.scene.objects if o not in objs and o != arm]:
        bpy.data.objects.remove(o, do_unlink=True)
    # object-level channels of the clips would override the armature's placement: bones only
    for act in actions:
        for fc in [fc for fc in action_fcurves(act) if not fc.data_path.startswith("pose.bones")]:
            remove_fcurve(act, fc)
    if extra.get("rebake"):
        rebake_rest(arm, objs, actions, idle, extra.get("step", 2))
    # the idle clip's first frame decides size and ground
    if arm.animation_data is None:
        arm.animation_data_create()
    if idle is not None:
        arm.animation_data.action = idle
        assign_slot(arm, idle)
        bpy.context.scene.frame_set(int(idle.frame_range[0]))
    def place(m):
        for o in [arm] + objs:
            o.matrix_world = m @ o.matrix_world
        bpy.context.view_layer.update()
    place(Matrix.Rotation(math.radians(yaw), 4, 'Z'))
    lo, hi = world_bounds(objs)
    s = target / measure(lo, hi, how)
    place(Matrix.Scale(s, 4))
    lo, hi = world_bounds(objs)
    place(Matrix.Translation(Vector((-(lo.x + hi.x) * 0.5, -(lo.y + hi.y) * 0.5, -lo.z))))
    # bake the placement into the rest pose (FBX + Unity handle scaled armatures badly); bone translations scale along
    k = arm.matrix_world.to_scale().x
    for o in [arm] + objs:
        vw.activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for act in actions:
        for fc in action_fcurves(act):
            if fc.data_path.endswith(".location"):
                for kp in fc.keyframe_points:
                    kp.co[1] *= k
                    kp.handle_left[1] *= k
                    kp.handle_right[1] *= k
                fc.update()
    bpy.context.scene.frame_set(int(idle.frame_range[0]) if idle else 0)
    bpy.context.view_layer.update()
    lo, hi = world_bounds(objs)
    print("[enemies] %s walker size %s lo %s tris %d bones %d bake scale %.4f clips %s" % (
        name, tuple(round(v, 2) for v in hi - lo), tuple(round(v, 2) for v in lo), sum(tris(o) for o in objs),
        len(arm.data.bones), k, [a.name for a in actions]))
    return export_walker(name, arm, objs, actions)


def weighted_bones(arm, objs):
    """Bones that carry vertex weights, plus their ancestors (the skeleton the FBX keeps)."""
    used = set()
    for o in objs:
        groups = {g.index: g.name for g in o.vertex_groups}
        for v in o.data.vertices:
            for g in v.groups:
                if g.weight > 0.001 and g.group in groups:
                    used.add(groups[g.group])
    keep = set()
    for b in arm.data.bones:
        if b.name in used:
            p = b
            while p is not None:
                keep.add(p.name)
                p = p.parent
    return keep


def rebake_rest(arm, objs, actions, idle, step):
    """
    Rigs whose rest pose is not the skinned arrangement (Rigify via glTF: the rest stands upright, stretch bones scale
    up to 16x) break in FBX. Samples every clip's skeleton in armature space, makes the idle pose the new rest (meshes
    re-bound), and keys the clips again as plain local transforms with full inheritance.
    """
    scene = bpy.context.scene
    keep = weighted_bones(arm, objs)
    order = [b.name for b in arm.data.bones if b.name in keep]          # parents before children
    samples = {}
    for act in actions:
        arm.animation_data.action = act
        assign_slot(arm, act)
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        frames = list(range(f0, f1 + 1, step))
        if frames[-1] != f1:
            frames.append(f1)
        mats = []
        for f in frames:
            scene.frame_set(f)
            ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())   # the animation lives on the evaluated copy
            mats.append({n: ev.pose.bones[n].matrix.copy() for n in order})
        samples[act] = (frames, mats)
    # the idle pose becomes the rest: meshes keep their posed shape, the armature its posed bones
    arm.animation_data.action = idle
    assign_slot(arm, idle)
    scene.frame_set(int(idle.frame_range[0]))
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.data.shape_keys is not None:
            o.shape_key_clear()
        # write the posed (evaluated) vertices into the mesh, then drop the armature binding
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        ev.to_mesh_clear()
        o.data.vertices.foreach_set("co", co)
        o.data.update()
        for m in [m for m in o.modifiers if m.type == 'ARMATURE']:
            o.modifiers.remove(m)
    # the original pose bones hold no animation: copy the evaluated idle pose over before applying it as rest
    ev = arm.evaluated_get(dg)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = ev.pose.bones[pb.name].matrix_basis.copy()
    vw.activate(arm)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='EDIT')
    for eb in arm.data.edit_bones:
        eb.use_connect = False
        eb.inherit_scale = 'FULL'
        eb.use_inherit_rotation = True
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in objs:
        mod = o.modifiers.new("Armature", 'ARMATURE')
        mod.object = arm
    # key the samples as local transforms against the new rest
    arm.animation_data.action = None
    rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    for act in actions:
        frames, mats = samples[act]
        for fc in list(action_fcurves(act)):
            remove_fcurve(act, fc)
        arm.animation_data.action = act
        assign_slot(arm, act)
        for f, pose in zip(frames, mats):
            for n in order:
                b = arm.data.bones[n]
                pb = arm.pose.bones[n]
                if b.parent is not None and b.parent.name in pose:
                    rel = rest[b.parent.name].inverted() @ rest[n]
                    basis = rel.inverted() @ pose[b.parent.name].inverted() @ pose[n]
                else:
                    basis = rest[n].inverted() @ pose[n]
                loc, rot, sca = basis.decompose()
                pb.rotation_mode = 'QUATERNION'
                pb.location, pb.rotation_quaternion, pb.scale = loc, rot, sca
                pb.keyframe_insert("location", frame=f)
                pb.keyframe_insert("rotation_quaternion", frame=f)
                pb.keyframe_insert("scale", frame=f)
    arm.animation_data.action = idle
    assign_slot(arm, idle)
    print("[enemies]   rebaked %d bones x %d clips onto the idle pose as rest" % (len(order), len(actions)))


def export_walker(name, arm, objs, actions):
    # glTF marks every joint as deforming: keep only weighted bones (and their parents) so the take stays small
    keep = weighted_bones(arm, objs)
    for b in arm.data.bones:
        b.use_deform = b.name in keep
    print("[enemies]   deform bones %d of %d" % (sum(b.use_deform for b in arm.data.bones), len(arm.data.bones)))
    # one take per action, named "<armature>|<clip>" (Blender 5 actions carry slots: assign each in turn for the bake)
    track_all(arm, actions)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [arm] + objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=False, mesh_smooth_type='OFF', add_leaf_bones=False, use_armature_deform_only=True,
        primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True, bake_anim_use_all_bones=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.5, path_mode='STRIP')
    return objs


def action_fcurves(action):
    """F-curves of an action (Blender 5 layered actions: layers -> strips -> channelbags)."""
    layers = getattr(action, "layers", None)
    if layers:
        for layer in layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        yield fc
    else:
        for fc in action.fcurves:
            yield fc


def remove_fcurve(action, fc):
    layers = getattr(action, "layers", None)
    if layers:
        for layer in layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    if fc in bag.fcurves[:]:
                        bag.fcurves.remove(fc)
                        return
    else:
        action.fcurves.remove(fc)


def assign_slot(obj, action):
    slots = getattr(action, "slots", None)
    if slots:
        try:
            obj.animation_data.action_slot = slots[0]
        except Exception:
            pass


def track_all(arm, actions):
    """Every action as its own NLA track/strip so the FBX exporter writes one take per action (named like it)."""
    ad = arm.animation_data
    ad.action = None
    for t in list(ad.nla_tracks):
        ad.nla_tracks.remove(t)
    for a in actions:
        tr = ad.nla_tracks.new()
        tr.name = a.name.split("|")[-1]
        st = tr.strips.new(tr.name, int(a.frame_range[0]), a)
        slots = getattr(a, "slots", None)
        if slots:
            try:
                st.action_slot = slots[0]
            except Exception:
                pass


# ---------------------------------------------------------------- Warden boss

def build_boss(name, how, target, yaw, extra):
    obj = bake_static(meshes(), name)   # five interleaved chunks of one hull
    # align the hull's long axis with Y (the source flies pitched up), then turn the head forward
    pts = np.array([v.co[:] for v in obj.data.vertices], dtype=np.float64)
    c = pts.mean(axis=0)
    w, v = np.linalg.eigh(np.cov((pts - c).T))
    axis = Vector(v[:, int(np.argmax(w))])
    if axis.y < 0:
        axis = -axis
    rot = axis.rotation_difference(Vector((0, 1, 0))).to_matrix().to_4x4()
    transform_mesh(obj, Matrix.Rotation(math.radians(yaw), 4, 'Z') @ rot @ Matrix.Translation(-Vector(c)))
    # keep the roll level: the wider extent stays horizontal
    lo, hi = world_bounds([obj], evaluated=False)
    if (hi - lo).z > (hi - lo).x:
        transform_mesh(obj, Matrix.Rotation(math.radians(90), 4, 'Y'))
    lo, hi = world_bounds([obj], evaluated=False)
    s = target / measure(lo, hi, how)
    transform_mesh(obj, Matrix.Scale(s, 4) @ Matrix.Translation(-(lo + hi) * 0.5))
    # weld the source parts' seams, then decimate to the budget
    vw.activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    ratio = min(1.0, extra.get("tris", 70000) / max(1, tris(obj)))
    m = obj.modifiers.new("dec", 'DECIMATE')
    m.ratio = ratio
    m.use_collapse_triangulate = True
    vw.apply_modifiers(obj)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.clear()
    # ivory ceramic with slate underside; cyan light leaks from the deep crevices (painted after the AO bake)
    lo, hi = world_bounds([obj], evaluated=False)
    mid = (lo.z + hi.z) * 0.5

    def col(p, n):
        t = vw.smoothstep(lo.z, hi.z, p.z) * 0.6 + max(0.0, n.z) * 0.4
        return tuple(IVORY_SHADE[i] + (IVORY[i] - IVORY_SHADE[i]) * t for i in range(3))
    vw.paint(obj, col, lambda p, n: (0.78, 0.0), lambda p, n: 0.08)
    bpy.context.scene.render.engine = 'CYCLES'
    bpy.context.scene.cycles.device = 'CPU'
    vw.bake_ao(obj, distance=4.0, samples=16)
    me = obj.data
    colattr = me.color_attributes["Col"]
    surf = me.uv_layers["surf"]
    det = me.uv_layers["detail"]
    glow = 0
    for poly in me.polygons:
        ao = sum(det.data[li].uv[1] for li in poly.loop_indices) / len(poly.loop_indices)
        if ao < 0.32:
            for li in poly.loop_indices:
                colattr.data[li].color_srgb = (CYAN[0], CYAN[1], CYAN[2], 1.0)
                surf.data[li].uv = (0.9, 0.85)
                det.data[li].uv = (0.0, 1.0)
            glow += 1
        elif ao < 0.5:
            for li in poly.loop_indices:
                colattr.data[li].color_srgb = (SLATE[0], SLATE[1], SLATE[2], 1.0)
    # the source is built from thin single-sided shells (glTF doubleSided): give every face a flipped twin, so the
    # culled hull shader shows no holes and both sides are lit the right way
    bm = bmesh.new()
    bm.from_mesh(me)
    dup = bmesh.ops.duplicate(bm, geom=bm.faces[:])
    bmesh.ops.reverse_faces(bm, faces=[g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)])
    bm.to_mesh(me)
    bm.free()
    me.update()
    lo, hi = world_bounds([obj], evaluated=False)
    print("[enemies] %s boss size %s tris %d glow faces %d (%.1f%%)" % (
        name, tuple(round(v, 1) for v in hi - lo), tris(obj), glow, 200.0 * glow / len(me.polygons)))
    export_static(obj, os.path.join(OUT, name + ".fbx"), contract=True)
    print_profile(obj)
    return [obj]


def print_profile(obj):
    """Cross-sections (Unity coordinates: x = -bx, y = bz, z = -by) for placing pods, muzzles and colliders."""
    pts = np.array([v.co[:] for v in obj.data.vertices])
    ys = -pts[:, 1]
    for z in np.linspace(ys.min(), ys.max(), 11):
        sel = pts[np.abs(ys - z) < (ys.max() - ys.min()) / 20]
        if len(sel):
            print("[enemies]   z %6.1f  x %6.1f..%6.1f  y %6.1f..%6.1f" % (z, -sel[:, 0].max(), -sel[:, 0].min(), sel[:, 2].min(), sel[:, 2].max()))


# ---------------------------------------------------------------- preview

def preview(name, objs, out):
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'VERTEX' if name.startswith("Boss") else 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x, scene.render.resolution_y = 800, 500
    lo, hi = world_bounds(objs)
    c = (lo + hi) * 0.5
    r = (hi - lo).length
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.clip_end = r * 20
    # forward marker: a red cone ahead of the nose (Blender -Y)
    bpy.ops.mesh.primitive_cone_add(radius1=r * 0.03, depth=r * 0.08, location=(c.x, lo.y - r * 0.08, c.z), rotation=(math.radians(90), 0, 0))
    mk = bpy.context.object
    mat = bpy.data.materials.new("Marker")
    mat.diffuse_color = (1, 0.1, 0.05, 1)
    mk.data.materials.append(mat)
    for tag, d in (("front34", Vector((0.8, -1.0, 0.5))), ("side", Vector((1, 0, 0.15))), ("top", Vector((0.001, 0.2, 1)))):
        d.normalize()
        cam.location = c + d * r * 1.4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, tag))
        bpy.ops.render.render(write_still=True)


# ---------------------------------------------------------------- main

if "--ingest" in ARGS:
    ingest(arg("--ingest"))
else:
    os.makedirs(OUT, exist_ok=True)
    only = arg("--only")
    prev = arg("--preview")
    for name, (stem, kind, how, target, yaw, extra) in ASSETS.items():
        if only and name != only:
            continue
        if not os.path.exists(os.path.join(SRC_DIR, name + ".zip")):
            print("[enemies] skip (no source):", name)
            continue
        import_source(name)
        if kind == "static":
            objs = build_static(name, how, target, yaw)
        elif kind == "walker":
            objs = build_walker(name, how, target, yaw, extra)
        else:
            objs = build_boss(name, how, target, yaw, extra)
        if prev:
            preview(name, objs, prev)
