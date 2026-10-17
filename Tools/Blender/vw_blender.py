"""
Shared helpers for Voidward Blender asset scripts (run headless, see AGENTS.md "Blender (asset scripts)").

Vertex data contract with PrefabBuilder.ImportModel (Unity side):
  - colour attribute "Col" (BYTE_COLOR, corner domain, authored in sRGB) = base colour
  - UV map "surf"   : u = gloss (0..1), v = glow (emission)
  - UV map "detail" : u = metal (0..1), v = baked ambient occlusion (1 = open)
Forward is Blender -Y, up is +Z, 1 unit = 1 m. Export: FBX with axis_forward='-Z', axis_up='Y'.
"""
import bpy
import bmesh
import math
import random
from mathutils import Vector, Matrix


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 24
    return scene


def srgb(hex_or_tuple):
    if isinstance(hex_or_tuple, str):
        h = hex_or_tuple.lstrip('#')
        return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0)
    return tuple(hex_or_tuple)


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_modifiers(obj):
    activate(obj)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def skin_object(name, points, edges, radii, subdiv=2, root=0):
    """Skin-modifier creature body: points (x,y,z), edges (i,j), radii (rx, ry) per point."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([Vector(p) for p in points], edges, [])
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    skin = obj.modifiers.new("skin", 'SKIN')
    skin.use_smooth_shade = True
    sv = mesh.skin_vertices[0].data
    for i, r in enumerate(radii):
        sv[i].radius = r if isinstance(r, (tuple, list)) else (r, r)
        sv[i].use_root = (i == root)
    if subdiv > 0:
        sub = obj.modifiers.new("sub", 'SUBSURF')
        sub.levels = subdiv
        sub.render_levels = subdiv
    apply_modifiers(obj)
    return obj


def ellipsoid(name, center, radii, rot=(0, 0, 0), segments=24, rings=14):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.scale = radii
    obj.rotation_euler = rot
    obj.location = center
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def displace(obj, strength=0.02, size=0.12, kind='VORONOI', seed=0):
    tex = bpy.data.textures.new(obj.name + "_tex", type=kind)
    if kind == 'VORONOI':
        tex.noise_scale = size
        tex.distance_metric = 'DISTANCE'
    else:
        tex.noise_scale = size
    mod = obj.modifiers.new("disp", 'DISPLACE')
    mod.texture = tex
    mod.strength = strength
    mod.mid_level = 0.5
    mod.texture_coords = 'LOCAL'
    apply_modifiers(obj)


def warp(obj, fn):
    """Moves every vertex: fn(co) -> new co (bends, sweeps, flattening)."""
    for v in obj.data.vertices:
        v.co = fn(v.co.copy())
    obj.data.update()


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def shade_smooth(obj):
    for p in obj.data.polygons:
        p.use_smooth = True


def paint(obj, color_fn, surf_fn, metal_fn=None):
    """Writes Col (sRGB), surf (gloss, glow) and detail (metal, ao=1) per corner from vertex position/normal."""
    mesh = obj.data
    col = mesh.color_attributes.get("Col") or mesh.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    surf = mesh.uv_layers.get("surf") or mesh.uv_layers.new(name="surf")
    det = mesh.uv_layers.get("detail") or mesh.uv_layers.new(name="detail")
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            v = mesh.vertices[mesh.loops[li].vertex_index]
            c = color_fn(v.co, v.normal)
            col.data[li].color_srgb = (c[0], c[1], c[2], 1.0)
            g, gl = surf_fn(v.co, v.normal)
            surf.data[li].uv = (g, gl)
            det.data[li].uv = (metal_fn(v.co, v.normal) if metal_fn else 0.0, 1.0)


def solid(obj, color, gloss, glow=0.0, metal=0.0):
    c = srgb(color)
    paint(obj, lambda p, n: c, lambda p, n: (gloss, glow), lambda p, n: metal)


def join(objs, name):
    activate(objs[0])
    for o in objs:
        o.select_set(True)
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.name = name
    return obj


def bake_ao(obj, distance=0.6, samples=24):
    """Cycles AO bake into a temporary colour attribute, copied into detail.v (UV1.y on the Unity side)."""
    mesh = obj.data
    mat = bpy.data.materials.new(obj.name + "_bake")
    mat.use_nodes = True
    mesh.materials.clear()
    mesh.materials.append(mat)
    ao = mesh.color_attributes.new("AO", 'FLOAT_COLOR', 'CORNER')
    keep = mesh.color_attributes.get("Col")
    mesh.color_attributes.active_color = ao
    scene = bpy.context.scene
    scene.cycles.samples = samples
    scene.world = scene.world or bpy.data.worlds.new("World")
    scene.render.bake.target = 'VERTEX_COLORS'
    scene.render.bake.use_selected_to_active = False
    scene.world.light_settings.distance = distance
    activate(obj)
    bpy.ops.object.bake(type='AO')
    det = mesh.uv_layers["detail"]
    for i, d in enumerate(ao.data):
        a = d.color[0]
        det.data[i].uv = (det.data[i].uv[0], max(0.0, min(1.0, 0.15 + a * 0.85)))
    mesh.color_attributes.remove(ao)
    if keep is not None:
        mesh.color_attributes.active_color = keep
        mesh.color_attributes.render_color_index = mesh.color_attributes.find("Col")
    mesh.materials.clear()


def export_fbx(obj, path):
    activate(obj)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False,
        colors_type='SRGB', prioritize_active_color=True)


def load_palette(path=None):
    """Shared low-poly palette (#120): {name: (r, g, b) sRGB 0..1} from Assets/_Project/Art/Palette.json, plus the glow names."""
    import json
    import os
    if path is None:
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "_Project", "Art", "Palette.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return {k: srgb(v) for k, v in data["colors"].items()}, set(data.get("glow", []))
