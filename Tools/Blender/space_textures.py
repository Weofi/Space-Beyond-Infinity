"""
Sky + VFX textures for #96 (light, post, sky, VFX). Converts the downloaded free sources into the repo textures.

Sources (download them yourself, see Assets/_Project/Art/CREDITS.md):
  --nasa   NASA SVS "Deep Star Maps 2020", celestial plate carree, starmap_2020_8k.exr
           https://svs.gsfc.nasa.gov/4851 (NASA/Goddard Space Flight Center Scientific Visualization Studio, ESA/Gaia/DPAC)
  --labs   folder with the Unity Labs Paris flipbooks (CC0), the *-flipbooks.zip files unzipped, e.g. Explosion02_5x5.tga
           https://unity.com/blog/engine-platform/free-vfx-image-sequences-flipbooks
  --kenney Kenney "Particle Pack" (CC0), its "PNG (Transparent)" folder
           https://kenney.nl/assets/particle-pack

Outputs (Assets/_Project/Art):
  Sky/Starmap_8k.jpg        8192x4096 sRGB star map (values are 0..1, so an 8-bit JPG q95 keeps them; 13 MB instead of 130)
  VFX/FB_<name>.png         flipbook sheets as RGBA PNG (straight alpha)
  VFX/K_<name>.png          single Kenney sprites (white, alpha)
  VFX/K_MuzzleStrip.png     muzzle_02..05 (rotated: flame along +U, the stretched billboard's velocity axis) as a 4x1 strip
  VFX/K_SmokeSheet.png      smoke_01..08 as a 4x2 sheet (random frame per particle)

Run: blender -b --factory-startup -P Tools/Blender/space_textures.py -- --nasa <exr> --labs <dir> --kenney <dir> [--out Assets/_Project/Art]
"""
import bpy
import os
import sys
import numpy as np

FLIPBOOKS = ["Explosion02_5x5", "Explosion01_5x5", "WispySmoke01_8x8"]
SPRITES = {"Flash": "star_09", "Glint": "star_06", "Ring": "circle_03", "Ripple": "light_03", "Spark": "trace_04", "Dot": "star_05"}
MUZZLES = ["muzzle_02_rotated", "muzzle_03_rotated", "muzzle_04_rotated", "muzzle_05_rotated"]
SMOKES = ["smoke_%02d" % i for i in range(1, 9)]


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"out": "Assets/_Project/Art"}
    for i in range(0, len(a) - 1, 2):
        opts[a[i].lstrip("-")] = a[i + 1]
    return opts


def scene_standard():
    sc = bpy.context.scene
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.display_settings.display_device = "sRGB"
    return sc


def load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4), img


def save_png(px, path):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(px, np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print("[space_textures] wrote", path)


def resize(px, size):
    h, w = px.shape[:2]
    ys = np.arange(size) * h // size
    xs = np.arange(size) * w // size
    return px[ys][:, xs]


def starmap(src, out):
    sc = scene_standard()
    img = bpy.data.images.load(src)
    s = sc.render.image_settings
    s.file_format = "JPEG"
    s.color_mode = "RGB"
    s.quality = 95
    img.save_render(out, scene=sc)
    print("[space_textures] wrote", out)


def strip(paths, cols, rows, tile, out):
    sheet = np.zeros((rows * tile, cols * tile, 4), np.float32)
    for i, p in enumerate(paths):
        px, _ = load(p)
        r = rows - 1 - i // cols  # Blender pixel rows start at the bottom; frame 0 = top-left (Unity texture sheet order)
        c = i % cols
        sheet[r * tile:(r + 1) * tile, c * tile:(c + 1) * tile] = resize(px, tile)
    save_png(sheet, out)


def main():
    o = args()
    out = os.path.abspath(o["out"])
    os.makedirs(os.path.join(out, "Sky"), exist_ok=True)
    os.makedirs(os.path.join(out, "VFX"), exist_ok=True)
    if "nasa" in o:
        starmap(o["nasa"], os.path.join(out, "Sky", "Starmap_8k.jpg"))
    if "labs" in o:
        for name in FLIPBOOKS:
            found = None
            for root, _, files in os.walk(o["labs"]):
                if name + ".tga" in files:
                    found = os.path.join(root, name + ".tga")
            if found is None:
                print("[space_textures] missing flipbook", name)
                continue
            px, _ = load(found)
            save_png(px, os.path.join(out, "VFX", "FB_" + name + ".png"))
    if "kenney" in o:
        k = o["kenney"]
        for key, name in SPRITES.items():
            px, _ = load(os.path.join(k, name + ".png"))
            save_png(resize(px, 256), os.path.join(out, "VFX", "K_" + key + ".png"))
        strip([os.path.join(k, "Rotated", n + ".png") for n in MUZZLES], 4, 1, 256, os.path.join(out, "VFX", "K_MuzzleStrip.png"))
        strip([os.path.join(k, n + ".png") for n in SMOKES], 4, 2, 256, os.path.join(out, "VFX", "K_SmokeSheet.png"))


main()
