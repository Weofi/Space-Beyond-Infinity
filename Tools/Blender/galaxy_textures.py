"""
Sky galaxy images for #96: the destination galaxy and the Milky Way on the journey sky (SystemVisuals).

Sources (download them yourself, see Assets/_Project/Art/CREDITS.md):
  --m31     NASA/JPL-Caltech GALEX mosaic of M31, PIA15416.jpg   https://photojournal.jpl.nasa.gov/catalog/PIA15416
  --ngc6822 ESO, Barnard's Galaxy, eso0938a.jpg (large)        https://www.eso.org/public/images/eso0938a/  (CC BY 4.0)
  --lmc     Zdenek Bardon/ESO, magellan-ch17-bardon-cc.jpg     https://www.eso.org/public/images/magellan-ch17-bardon-cc/  (CC BY 4.0)
  --mw      NASA/JPL-Caltech (R. Hurt), PIA10748.jpg           https://photojournal.jpl.nasa.gov/catalog/PIA10748

Each image is cropped around its galaxy, its sky background subtracted and faded out with a soft elliptical mask,
so it can be drawn additively on the sky without a visible frame. Leo I (a faint dwarf spheroidal, no free image) is
generated: a smooth Sersic-like glow plus resolved stars.

Outputs (Assets/_Project/Art/Sky): Galaxy_<Name>.png, RGB, 1024 px (the Milky Way 2048 px: it fills the sky at the end).

Run: blender -b --factory-startup -P Tools/Blender/galaxy_textures.py -- --m31 <jpg> --ngc6822 <jpg> --lmc <jpg> --mw <jpg> [--out Assets/_Project/Art/Sky]
"""
import bpy
import os
import sys
import math
import numpy as np

# crop box (x0, y0, x1, y1 as fractions, y from the top), output size, mask radii (fraction of half size, x / y),
# mask rotation (deg), black level, gain
JOBS = {
    "m31": ("Galaxy_M31", (0.08, 0.0, 0.92, 0.95), 1024, (0.98, 0.98), 0.0, 0.035, 1.0),
    "ngc6822": ("Galaxy_NGC6822", (0.18, 0.18, 0.86, 0.86), 1024, (0.9, 0.9), 0.0, 0.05, 1.1),
    "lmc": ("Galaxy_LMC", (0.27, 0.17, 0.73, 0.86), 1024, (0.95, 0.95), 0.0, 0.07, 1.6),
    "mw": ("Galaxy_MilkyWay", (0.0, 0.0, 1.0, 1.0), 2048, (1.0, 1.0), 0.0, 0.01, 1.0),
}


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"out": "Assets/_Project/Art/Sky"}
    for i in range(0, len(a) - 1, 2):
        opts[a[i].lstrip("-")] = a[i + 1]
    return opts


def load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1, :, :3]  # rows from the top


def save(rgb, path):
    h, w = rgb.shape[:2]
    out = np.ones((h, w, 4), np.float32)
    out[..., :3] = np.clip(rgb, 0.0, 1.0)
    out = out[::-1]  # Blender rows start at the bottom
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    img.pixels.foreach_set(np.ascontiguousarray(out).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print("[galaxy_textures] wrote", path)


def resize(rgb, size):
    """Box-filtered downscale to size x size (area average, so stars do not alias)."""
    h, w = rgb.shape[:2]
    ys = (np.arange(size + 1) * h // size)
    xs = (np.arange(size + 1) * w // size)
    c = np.cumsum(np.cumsum(np.pad(rgb, ((1, 0), (1, 0), (0, 0))), axis=0), axis=1)
    s = c[ys[1:]][:, xs[1:]] - c[ys[:-1]][:, xs[1:]] - c[ys[1:]][:, xs[:-1]] + c[ys[:-1]][:, xs[:-1]]
    area = ((ys[1:] - ys[:-1])[:, None] * (xs[1:] - xs[:-1])[None, :])[..., None]
    return s / np.maximum(area, 1)


def mask(size, rx, ry, rot):
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (x + 0.5) / size * 2.0 - 1.0
    v = (y + 0.5) / size * 2.0 - 1.0
    a = math.radians(rot)
    ur = u * math.cos(a) + v * math.sin(a)
    vr = -u * math.sin(a) + v * math.cos(a)
    d = np.sqrt((ur / rx) ** 2 + (vr / ry) ** 2)
    t = np.clip((1.0 - d) / 0.45, 0.0, 1.0)
    return (t * t * (3.0 - 2.0 * t))[..., None]


def process(path, job, out_dir):
    name, crop, size, (rx, ry), rot, black, gain = job
    rgb = load(path)
    h, w = rgb.shape[:2]
    x0, y0, x1, y1 = int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)
    side = min(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    rgb = rgb[cy - side // 2:cy + side // 2, cx - side // 2:cx + side // 2]
    rgb = resize(rgb, size)
    rgb = np.clip((rgb - black) / (1.0 - black), 0.0, None) * gain
    rgb *= mask(size, rx, ry, rot)
    save(rgb, os.path.join(out_dir, name + ".png"))


def leo_i(out_dir, size=1024, seed=1950):
    """Leo I: a faint, slightly elongated dwarf spheroidal - smooth old-star glow plus resolved yellowish stars."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (x + 0.5) / size * 2.0 - 1.0
    v = (y + 0.5) / size * 2.0 - 1.0
    a = math.radians(-12.0)
    ur = u * math.cos(a) + v * math.sin(a)
    vr = -u * math.sin(a) + v * math.cos(a)
    r = np.sqrt((ur / 0.62) ** 2 + (vr / 0.48) ** 2)
    glow = np.exp(-(r / 0.5) ** 0.9 * 2.0)  # Sersic-like, low concentration
    base = np.array([1.0, 0.86, 0.68], np.float32)
    rgb = glow[..., None] * base * 1.3
    # resolved stars, denser to the centre
    n = 14000
    rr = np.abs(rng.normal(0.0, 0.36, n))
    th = rng.uniform(0, 2 * math.pi, n)
    sx = rr * np.cos(th) * 0.62 / 0.55
    sy = rr * np.sin(th) * 0.48 / 0.55
    px = ((sx * math.cos(-a) - sy * math.sin(-a)) * 0.5 + 0.5) * size
    py = ((sx * math.sin(-a) + sy * math.cos(-a)) * 0.5 + 0.5) * size
    bright = rng.pareto(2.2, n) * 0.08 + 0.05
    tint = rng.uniform(0, 1, n)
    for i in range(n):
        ix, iy = int(px[i]), int(py[i])
        if 1 <= ix < size - 1 and 1 <= iy < size - 1:
            col = np.array([1.0, 0.9 - 0.15 * tint[i], 0.75 - 0.3 * tint[i]], np.float32) * min(bright[i] * 1.6, 2.0)
            rgb[iy, ix] += col
            rgb[iy - 1:iy + 2, ix - 1:ix + 2] += col * 0.25
    rgb *= mask(size, 0.98, 0.98, 0.0)
    save(rgb, os.path.join(out_dir, "Galaxy_LeoI.png"))


def main():
    o = args()
    out_dir = os.path.abspath(o["out"])
    os.makedirs(out_dir, exist_ok=True)
    for key, job in JOBS.items():
        if key in o:
            process(o[key], job, out_dir)
    leo_i(out_dir)


main()
