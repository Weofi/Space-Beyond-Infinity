"""
Planet surfaces, decor rocks and asteroids from free CC0 assets (#100, see Assets/_Project/Art/CREDITS.md).

Terrain texture sets (Poly Haven + ambientCG, CC0), one or more per PlanetDecorStyle:
  Rocky     RockyTerrain02, RockFace03 (cliffs)          Desert    RedSand, SandstoneCracks
  Ice       Ice002, Ice003, Snow006                      Volcanic  DarkRock, Lava004 (emissive)
  Barren    MoonDusted02, MoonMeteor01 (craters)         Jungle    MossyRock, Moss002, BrownMud (toxic)
  Crystal   DarkRock02                                   Ocean     CoastSand01 (shore + seabed)
Models (Poly Haven, CC0):
  Planet_MoonRock01..07   moon rock scans: barren/rocky decor + asteroids (system fields, warp corridor)
  Planet_MossRocks01/02   mossy rock sets (jungle decor)

Run headless from the repo root:
  ingest (once, from the downloaded files):
    blender -b --factory-startup -P Tools/Blender/planet_assets.py -- --ingest <dir>
      <dir>/tex/<polyhaven id>/<id>_<diff|nor_gl|arm>_2k.jpg, <dir>/acg/<ambientCG id>/<id>_2K-JPG.zip,
      <dir>/mod/<polyhaven id>/<id>_2k.gltf + .bin + textures/
      -> Assets/_Project/Art/Terrain/<Set>/<Set>_<baseColor|normal|arm|emissive>.jpg (2K, normal = OpenGL,
         arm = R ambient occlusion, G roughness, B metallic)
      -> Tools/Blender/Sources/Planet/<Name>.zip (scene.gltf + scene.bin + license.txt, no textures)
      -> Assets/_Project/Art/Models/Planet/<Name>/<material>_<baseColor|normal|metallicRoughness>.jpg
  build (reproducible from the repo):
    blender -b --factory-startup -P Tools/Blender/planet_assets.py -- [--only Name]
      -> Assets/_Project/Art/Models/Planet/<Name>.fbx

Contract (AGENTS.md): 1 unit = 1 m, forward = Blender -Y (Unity +Z), up +Z.
"""
import sys
import os
import re
import json
import shutil
import tempfile
import zipfile
import bpy
import numpy as np

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC_DIR = os.path.join(HERE, "Sources", "Planet")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Planet")
TERRAIN = os.path.join(ROOT, "Assets", "_Project", "Art", "Terrain")
MAX_TEX = 2048
LICENSE = "CC0 1.0 Universal (public domain). Source: %s\n"

# set name: (source, id)
TERRAIN_SETS = {
    "RockyTerrain02": ("polyhaven", "rocky_terrain_02"),
    "RockFace03": ("polyhaven", "rock_face_03"),
    "RedSand": ("polyhaven", "red_sand"),
    "SandstoneCracks": ("polyhaven", "sandstone_cracks"),
    "DarkRock": ("polyhaven", "dark_rock"),
    "DarkRock02": ("polyhaven", "dark_rock_02"),
    "MoonDusted02": ("polyhaven", "moon_dusted_02"),
    "MoonMeteor01": ("polyhaven", "moon_meteor_01"),
    "MossyRock": ("polyhaven", "mossy_rock"),
    "BrownMud": ("polyhaven", "brown_mud"),
    "CoastSand01": ("polyhaven", "coast_sand_01"),
    "Lava004": ("ambientcg", "Lava004"),
    "Ice002": ("ambientcg", "Ice002"),
    "Ice003": ("ambientcg", "Ice003"),
    "Snow006": ("ambientcg", "Snow006"),
    "Moss002": ("ambientcg", "Moss002"),
}

# model name: polyhaven id
MODELS = {"Planet_MoonRock%02d" % i: "moon_rock_%02d" % i for i in range(1, 8)}
MODELS.update({"Planet_MossRocks01": "rock_moss_set_01", "Planet_MossRocks02": "rock_moss_set_02"})


def arg(name):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else None


def safe(name):
    return re.sub(r"[^A-Za-z0-9_]+", "_", name).strip("_")


def source_url(kind, ident):
    return ("https://polyhaven.com/a/" + ident) if kind == "polyhaven" else ("https://ambientcg.com/view?id=" + ident)


# ---------------------------------------------------------------- texture helpers

def load_px(path, data):
    img = bpy.data.images.load(path)
    if data:
        img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    if max(w, h) > MAX_TEX:
        s = MAX_TEX / max(w, h)
        img.scale(max(1, int(w * s)), max(1, int(h * s)))
        w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def save_px(px, path, data):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    if data:
        img.colorspace_settings.name = 'Non-Color'
    px = px.copy()
    px[..., 3] = 1.0
    img.pixels.foreach_set(np.clip(px, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = 'JPEG'
    img.save(filepath=path, quality=92)
    bpy.data.images.remove(img)


def resample(px, h, w):
    sh, sw = px.shape[:2]
    if (sh, sw) == (h, w):
        return px
    ys = (np.arange(h) * sh // h).clip(0, sh - 1)
    xs = (np.arange(w) * sw // w).clip(0, sw - 1)
    return px[ys][:, xs]


# ---------------------------------------------------------------- ingest

def ingest_terrain(name, kind, ident, root):
    out = os.path.join(TERRAIN, name)
    os.makedirs(out, exist_ok=True)
    dst = lambda role: os.path.join(out, "%s_%s.jpg" % (name, role))
    if kind == "polyhaven":
        folder = os.path.join(root, "tex", ident)
        save_px(load_px(os.path.join(folder, ident + "_diff_2k.jpg"), False), dst("baseColor"), False)
        save_px(load_px(os.path.join(folder, ident + "_nor_gl_2k.jpg"), True), dst("normal"), True)
        save_px(load_px(os.path.join(folder, ident + "_arm_2k.jpg"), True), dst("arm"), True)
    else:
        tmp = tempfile.mkdtemp(prefix="vw_acg_")
        with zipfile.ZipFile(os.path.join(root, "acg", ident, ident + "_2K-JPG.zip")) as z:
            for n in z.namelist():
                if n.lower().endswith(".jpg") and "/" not in n:
                    z.extract(n, tmp)
        f = lambda m: os.path.join(tmp, "%s_2K-JPG_%s.jpg" % (ident, m))
        save_px(load_px(f("Color"), False), dst("baseColor"), False)
        save_px(load_px(f("NormalGL"), True), dst("normal"), True)
        rough = load_px(f("Roughness"), True)
        h, w = rough.shape[:2]
        arm = np.zeros_like(rough)
        arm[..., 0] = resample(load_px(f("AmbientOcclusion"), True), h, w)[..., 0] if os.path.exists(f("AmbientOcclusion")) else 1.0
        arm[..., 1] = rough[..., 0]
        save_px(arm, dst("arm"), True)
        if os.path.exists(f("Emission")):
            save_px(load_px(f("Emission"), False), dst("emissive"), False)
        shutil.rmtree(tmp, ignore_errors=True)
    print("[planet] terrain:", name)


def ingest_model(name, ident, root):
    folder = os.path.join(root, "mod", ident)
    gltf = os.path.join(folder, ident + "_2k.gltf")
    g = json.load(open(gltf, encoding="utf-8"))
    bins = [b["uri"] for b in g["buffers"]]
    assert len(bins) == 1, name
    # the source keeps the mesh only: buffer renamed to scene.bin, images dropped (they live in the texture folder)
    images = g.pop("images", [])
    textures = g.pop("textures", [])
    materials = g.get("materials", [])
    g["buffers"][0]["uri"] = "scene.bin"
    for m in materials:
        for k in ("normalTexture", "occlusionTexture", "emissiveTexture"):
            m.pop(k, None)
        pbr = m.get("pbrMetallicRoughness", {})
        pbr.pop("baseColorTexture", None)
        pbr.pop("metallicRoughnessTexture", None)
    g.pop("samplers", None)
    os.makedirs(SRC_DIR, exist_ok=True)
    with zipfile.ZipFile(os.path.join(SRC_DIR, name + ".zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        z.writestr("scene.gltf", json.dumps(g, indent=1))
        z.write(os.path.join(folder, bins[0]), "scene.bin")
        z.writestr("license.txt", LICENSE % source_url("polyhaven", ident))

    src = json.load(open(gltf, encoding="utf-8"))

    def uri(info):
        if not info:
            return None
        s = src["textures"][info["index"]].get("source")
        return os.path.join(folder, src["images"][s]["uri"]) if s is not None else None

    tex_out = os.path.join(OUT, name)
    os.makedirs(tex_out, exist_ok=True)
    for m in src.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        roles = {"baseColor": uri(pbr.get("baseColorTexture")), "normal": uri(m.get("normalTexture")),
                 "metallicRoughness": uri(pbr.get("metallicRoughnessTexture"))}
        mname = safe(m["name"])
        for role, path in roles.items():
            if not path or not os.path.exists(path):
                continue
            data = role != "baseColor"
            px = load_px(path, data)
            if role == "metallicRoughness":
                # moon rocks ship an ARM map, the moss sets a grey roughness map; both rely on metallicFactor 0,
                # so the packed map gets G = roughness, B = the factor (Unity reads B as metal)
                px[..., 2] = pbr.get("metallicFactor", 1.0)
                px[..., 0] = 0.0
            save_px(px, os.path.join(tex_out, "%s_%s.jpg" % (mname, role)), data)
        print("[planet] model %s material %s %s" % (name, mname, sorted(k for k, v in roles.items() if v)))


# ---------------------------------------------------------------- main

if "--ingest" in ARGS:
    root = arg("--ingest")
    only = arg("--only")
    for name, (kind, ident) in TERRAIN_SETS.items():
        if not only or name == only:
            ingest_terrain(name, kind, ident, root)
    for name, ident in MODELS.items():
        if not only or name == only:
            ingest_model(name, ident, root)
else:
    print("[planet] build: FBX export follows with the decor/asteroid step of #100")
