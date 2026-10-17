"""
Low-poly ship hulls for Voidward (#122, style #120): Quaternius "Ultimate Spaceships" (CC0) recoloured to the shared palette.

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/lowpoly_ships.py -- [--preview <dir>] [ship ...]

Sources: Tools/Blender/Sources/Quaternius/<Model>.gltf (texture embedded, "Orange" variant). Each hull is scaled to its
target length, centred on its bounds, and every face gets one palette colour from the texture under it (orange paint ->
accent, greys -> hull light/mid/dark/trim). Faces inside the canopy box that are dark become the glass mesh
(<Out>_Canopy.fbx). Output: Assets/_Project/Art/Models/Ships/LowPoly/<Out>.fbx in the vw vertex contract (vw_blender.py).
The printed numbers are Unity ship-local coordinates (x = -Blender x, y = Blender z, z = -Blender y), for PrefabBuilder.
"""
import sys
import os
import math
import colorsys
import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC = os.path.join(ROOT, "Tools", "Blender", "Sources", "Quaternius")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Ships", "LowPoly")
PAL, GLOW = vw.load_palette()

# canopy box in source units (Blender coords after the glTF import, nose = -Y): faces inside it that are dark = glass
SHIPS = {
    "Dart": dict(src="Striker", out="Ship_Dart_LP", length=15.0,
                 canopy=((-0.9, 0.9), (-3.6, -0.2), (-0.1, 1.3))),
    "Mule": dict(src="Spitfire", out="Ship_Mule_LP", length=13.0,
                 canopy=((-0.75, 0.75), (-1.2, 0.6), (0.0, 1.1))),
    "Nostos": dict(src="Imperial", out="Ship_Nostos_LP", length=54.0,
                   canopy=None),
}


def to_unity(v):
    return (-v.x, v.z, -v.y)


def fmt(v):
    return "(" + ", ".join(f"{c:.2f}" for c in v) + ")"


def classify(c):
    """Texture colour (sRGB 0..1) -> palette name."""
    h, s, v = colorsys.rgb_to_hsv(*c)
    if s > 0.3 and v > 0.25 and (h < 0.17 or h > 0.95):
        return "accent"
    if v >= 0.62:
        return "hull_light"
    if v >= 0.42:
        return "hull_mid"
    if v >= 0.2:
        return "hull_dark"
    return "trim"


def face_colours(obj):
    me = obj.data
    img = None
    for m in me.materials:
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image:
                    img = n.image
    w, h = img.size
    px = np.array(img.pixels[:]).reshape(h, w, 4)[:, :, :3]
    uv = me.uv_layers.active
    out = []
    for p in me.polygons:
        uvs = [Vector(uv.data[li].uv) for li in p.loop_indices]
        cen = sum(uvs, Vector((0, 0))) / len(uvs)
        samples = uvs + [cen] * 3 + [(u + cen) / 2 for u in uvs]
        cols = [px[int((u.y % 1) * (h - 1)), int((u.x % 1) * (w - 1))] for u in samples]
        out.append(np.mean(cols, axis=0))
    return out


def jitter(i):
    # deterministic per-face brightness variation (+-4 %, DESIGN.md)
    return 1.0 + (((i * 2654435761) % 1000) / 1000.0 - 0.5) * 0.08


def write_contract(obj, names, gloss):
    me = obj.data
    col = me.color_attributes.get("Col") or me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    surf = me.uv_layers.get("surf") or me.uv_layers.new(name="surf")
    det = me.uv_layers.get("detail") or me.uv_layers.new(name="detail")
    for i, p in enumerate(me.polygons):
        name = names[i]
        c = PAL[name]
        j = 1.0 if name in GLOW else jitter(i)
        cc = tuple(min(1.0, x * j) for x in c)
        for li in p.loop_indices:
            col.data[li].color_srgb = (cc[0], cc[1], cc[2], 1.0)
            surf.data[li].uv = (gloss.get(name, 0.3), 1.0 if name in GLOW else 0.0)
            det.data[li].uv = (0.0, 1.0)
        p.use_smooth = False
    # drop the source UVs and material: the contract maps are all Unity reads
    for uvl in [u for u in me.uv_layers if u.name not in ("surf", "detail")]:
        me.uv_layers.remove(uvl)
    me.materials.clear()


def build(key, cfg, preview):
    vw.reset_scene()
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, cfg["src"] + ".gltf"))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    obj = vw.join(meshes, cfg["out"]) if len(meshes) > 1 else meshes[0]
    obj.name = cfg["out"]
    vw.activate(obj)
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = obj.data

    cols = face_colours(obj)
    names = [classify(c) for c in cols]
    # canopy: dark faces inside the box (source units), split off before scaling
    glass = []
    if cfg["canopy"]:
        (x0, x1), (y0, y1), (z0, z1) = cfg["canopy"]
        for i, p in enumerate(me.polygons):
            c = p.center
            if x0 <= c.x <= x1 and y0 <= c.y <= y1 and z0 <= c.z <= z1 and names[i] in ("hull_dark", "trim") and p.normal.z > -0.3:
                glass.append(i)
    gl = set(glass)
    for i in gl:
        names[i] = "glass"

    # scale to length (Y) and centre on the bounds
    bb = [Vector(c) for c in obj.bound_box]
    mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    k = cfg["length"] / (mx.y - mn.y)
    ctr = (mn + mx) / 2
    for v in me.vertices:
        v.co = (v.co - ctr) * k
    me.update()
    write_contract(obj, names, {"glass": 0.9, "accent": 0.35, "hull_light": 0.3, "hull_mid": 0.3, "hull_dark": 0.25, "trim": 0.2})

    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    size = (mx - mn) * k
    print(f"[ship] {key}: {cfg['src']} x{k:.3f} -> {cfg['out']} tris {tris} size (w {size.x:.2f}, h {size.z:.2f}, l {size.y:.2f})")
    counts = {}
    for n in names:
        counts[n] = counts.get(n, 0) + 1
    print(f"[ship] {key} colours {counts}")

    canopy = None
    if gl:
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='DESELECT')
        bpy.ops.object.mode_set(mode='OBJECT')
        for i in gl:
            me.polygons[i].select = True
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.separate(type='SELECTED')
        bpy.ops.object.mode_set(mode='OBJECT')
        canopy = [o for o in bpy.context.selected_objects if o != obj][0]
        canopy.name = cfg["out"] + "_Canopy"
        cb = [canopy.matrix_world @ Vector(c) for c in canopy.bound_box]
        cmn = Vector((min(v.x for v in cb), min(v.y for v in cb), min(v.z for v in cb)))
        cmx = Vector((max(v.x for v in cb), max(v.y for v in cb), max(v.z for v in cb)))
        u0, u1 = to_unity(cmn), to_unity(cmx)
        lo = tuple(min(a, b) for a, b in zip(u0, u1)); hi = tuple(max(a, b) for a, b in zip(u0, u1))
        print(f"[ship] {key} canopy faces {len(gl)} unity min {fmt(lo)} max {fmt(hi)}")
        # cross-sections under the canopy: hull roof/floor at the centre line (for seat + eye placement)
        for zf in np.linspace(lo[2], hi[2], 5):
            ys = [to_unity(obj.matrix_world @ v.co)[1] for v in obj.data.vertices
                  if abs(-v.co.y - zf) < 0.35 and abs(v.co.x) < 0.5]
            if ys:
                print(f"[ship] {key} section z {zf:.2f}: hull y {min(ys):.2f} .. {max(ys):.2f}")

    os.makedirs(OUT, exist_ok=True)
    vw.export_fbx(obj, os.path.join(OUT, cfg["out"] + ".fbx"))
    if canopy is not None:
        vw.export_fbx(canopy, os.path.join(OUT, canopy.name + ".fbx"))
    if preview:
        render_preview(key, [obj] + ([canopy] if canopy else []), preview)


def render_preview(key, objs, out_dir):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 1000, 640
    scn.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("W"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.04, 0.06, 1)
    for o in objs:
        m = bpy.data.materials.new("Flat"); m.use_nodes = True
        vc = m.node_tree.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
        m.node_tree.links.new(vc.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
        o.data.materials.append(m)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); scn.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam); scn.camera = cam
    r = max(objs[0].dimensions) * 0.75
    for nm, d in (("front", Vector((0.9, -1.3, 0.7))), ("rear", Vector((-1.0, 1.2, 0.8)))):
        cam.location = d.normalized() * r * 2.6
        cam.rotation_euler = (-cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(out_dir, f"{key}_{nm}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = None
    if "--preview" in args:
        i = args.index("--preview"); preview = args[i + 1]; del args[i:i + 2]
        os.makedirs(preview, exist_ok=True)
    for key in (args or list(SHIPS)):
        build(key, SHIPS[key], preview)
