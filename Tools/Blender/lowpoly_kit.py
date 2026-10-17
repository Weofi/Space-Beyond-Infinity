"""
Kenney "Space Station Kit" (CC0) converted to Voidward low-poly parts (#122, style #120): cockpit seats, consoles, screens,
and the walls/floors/doors/beds/containers the Nostos deck is built from.

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/lowpoly_kit.py -- [name ...]

Source: Tools/Blender/Sources/KenneyStationKit/kenney_space-station-kit.zip (GLB + colormap). Every face takes one palette
colour from the colormap swatch under it (greys -> hull tones, orange -> accent, pale blue screens -> cyan glow).
Kenney's own origins (bottom centre of a tile) and scale are kept; PrefabBuilder scales the parts where it places them.
Output: Assets/_Project/Art/Models/Kit/Kit_<name>.fbx in the vw vertex contract (vw_blender.py). Prints each part's size.
"""
import sys
import os
import colorsys
import tempfile
import zipfile
import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ZIP = os.path.join(ROOT, "Tools", "Blender", "Sources", "KenneyStationKit", "kenney_space-station-kit.zip")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Kit")
PAL, GLOW = vw.load_palette()
GLOSS = {"glass": 0.9, "glow_cyan": 0.6, "screen": 0.8}


def classify(c):
    h, s, v = colorsys.rgb_to_hsv(*c)
    if v < 0.06:
        return "trim"
    if s < 0.3:
        if v > 0.85:
            return "hull_light"
        if v > 0.55:
            return "hull_mid" if v < 0.72 else "hull_light"
        if v > 0.3:
            return "hull_dark"
        return "trim"
    if 0.5 <= h <= 0.72:
        return "glow_cyan" if v > 0.85 and s < 0.35 else "glass"
    if 0.72 < h <= 0.85:
        return "crystal_violet"
    if 0.25 <= h < 0.5:
        return "acid"
    if (h < 0.06 or h > 0.95) and s > 0.45:
        return "accent"
    if h < 0.12 and s > 0.55 and v > 0.85:
        return "accent"
    # peach/brown (rocks, crates)
    if v > 0.85:
        return "rock_light"
    return "ore" if s > 0.45 else "rock_mid"


def convert(path, name):
    vw.reset_scene()
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        return
    obj = vw.join(meshes, "Kit_" + name) if len(meshes) > 1 else meshes[0]
    obj.name = "Kit_" + name
    vw.activate(obj)
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = obj.data
    img = None
    for m in me.materials:
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image:
                    img = n.image
    uv = me.uv_layers.active
    names = []
    if img is not None and uv is not None:
        w, h = img.size
        px = np.array(img.pixels[:]).reshape(h, w, 4)[:, :, :3]
        for p in me.polygons:
            uvs = [Vector(uv.data[li].uv) for li in p.loop_indices]
            cen = sum(uvs, Vector((0, 0))) / len(uvs)
            c = px[int((cen.y % 1) * (h - 1)), int((cen.x % 1) * (w - 1))]
            names.append(classify(c))
    else:
        names = ["hull_mid"] * len(me.polygons)
    col = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    surf = me.uv_layers.new(name="surf")
    det = me.uv_layers.new(name="detail")
    for i, p in enumerate(me.polygons):
        c = PAL[names[i]]
        for li in p.loop_indices:
            col.data[li].color_srgb = (c[0], c[1], c[2], 1.0)
            surf.data[li].uv = (GLOSS.get(names[i], 0.3), 1.0 if names[i] in GLOW else 0.0)
            det.data[li].uv = (0.0, 1.0)
        p.use_smooth = False
    for uvl in [u for u in me.uv_layers if u.name not in ("surf", "detail")]:
        me.uv_layers.remove(uvl)
    me.materials.clear()
    d = obj.dimensions
    used = sorted(set(names))
    print(f"[kit] {name}: tris {sum(len(p.vertices) - 2 for p in me.polygons)} size (w {d.x:.2f}, h {d.z:.2f}, d {d.y:.2f}) colours {used}")
    vw.export_fbx(obj, os.path.join(OUT, "Kit_" + name + ".fbx"))


if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        with zipfile.ZipFile(ZIP) as z:
            members = [m for m in z.namelist() if m.startswith("Models/GLB format/") and m.endswith((".glb", ".png"))]
            z.extractall(tmp, members)
        src = os.path.join(tmp, "Models", "GLB format")
        for f in sorted(os.listdir(src)):
            if not f.endswith(".glb"):
                continue
            name = f[:-4]
            if only and name not in only:
                continue
            convert(os.path.join(src, f), name)
