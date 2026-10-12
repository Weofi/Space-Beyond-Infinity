"""
Alien faction creatures for Voidward (#26): Crawler, Spitter, Flying Critter, Nest Guardian.

Run headless (from the repo root):
  blender -b --factory-startup -P Tools/Blender/aliens.py -- Assets/_Project/Art/Models/Aliens

Organic bodies are skin-modifier skeletons (branching limbs blend into the body), subdivided, with a light
voronoi displacement for chitin plating; glowing sacs and eyes are separate ellipsoids. Colours follow
DESIGN.md (acid lime #84CC16, chitin purple #581C87, bioluminescent lime #A3E635), blended with smooth
gradients (no hard per-vertex stripes). AO is baked in Cycles.
Coordinates below are written as F(x, forward, up) in metres; F maps to Blender (x, -forward, up).
"""
import sys
import os
import math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import vw_blender as vw

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Assets/_Project/Art/Models/Aliens"
os.makedirs(OUT, exist_ok=True)

LIME = vw.srgb("#84CC16")
LIME_GLOW = vw.srgb("#7BE82A")
CHITIN = vw.srgb("#6B21A8")
CHITIN_DARK = vw.srgb("#3B0764")
BELLY = vw.srgb("#A78BFA")
GOLD = vw.srgb("#FFC21A")
PINK = vw.srgb("#F472B6")


def F(x, fwd, up):
    return (x, -fwd, up)


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


class Skeleton:
    """Collects skin points/edges/radii; add() returns the point index."""
    def __init__(self):
        self.p, self.e, self.r = [], [], []

    def add(self, pos, radius, parent=None):
        self.p.append(pos)
        self.r.append(radius)
        i = len(self.p) - 1
        if parent is not None:
            self.e.append((parent, i))
        return i

    def chain(self, start, pts):
        prev = start
        for pos, rad in pts:
            prev = self.add(pos, rad, prev)
        return prev

    def build(self, name, subdiv=2):
        return vw.skin_object(name, self.p, self.e, self.r, subdiv=subdiv)


def glow_blob(name, center, radii, color, glow=0.6, rot=(0, 0, 0)):
    o = vw.ellipsoid(name, center, radii, rot, segments=20, rings=12)
    vw.solid(o, color, 0.6, glow)
    return o


def finish(parts, name, ao_distance):
    obj = vw.join(parts, name)
    vw.shade_smooth(obj)
    vw.bake_ao(obj, distance=ao_distance, samples=24)
    vw.export_fbx(obj, os.path.join(OUT, name + ".fbx"))
    print("EXPORTED", name, len(obj.data.vertices))


# ------------------------------------------------------------------ Crawler (1.4 m six-legged insectoid)
def crawler():
    vw.reset_scene()
    sk = Skeleton()
    tip = sk.add(F(0, -0.95, 0.62), 0.07)
    thorax = sk.chain(tip, [
        (F(0, -0.62, 0.72), (0.42, 0.36)), (F(0, -0.27, 0.66), (0.33, 0.29)), (F(0, -0.02, 0.62), 0.15),
        (F(0, 0.25, 0.64), (0.29, 0.25)),
    ])
    neck = sk.add(F(0, 0.5, 0.66), 0.15, thorax)
    head = sk.add(F(0, 0.68, 0.67), (0.21, 0.19), neck)
    snout = sk.add(F(0, 0.86, 0.61), 0.09, head)
    for s in (-1, 1):
        for k in range(3):
            hip = sk.add(F(s * 0.2, 0.3 - 0.16 * k, 0.6), 0.075, thorax)
            sk.chain(hip, [(F(s * 0.72, 0.42 - 0.34 * k, 0.98), 0.06), (F(s * 1.02, 0.55 - 0.6 * k, 0.03), 0.018)])
        shoulder = sk.add(F(s * 0.2, 0.4, 0.7), 0.065, thorax)
        sk.chain(shoulder, [(F(s * 0.46, 0.68, 1.05), 0.055), (F(s * 0.3, 1.15, 0.5), 0.012)])     # scythe foreleg
        m = sk.add(F(s * 0.07, 0.95, 0.56), 0.03, snout)
        sk.add(F(s * 0.02, 1.07, 0.5), 0.008, m)                                                   # mandible
    body = sk.build("crawler_body")
    vw.displace(body, strength=0.01, size=0.1)

    def col(p, n):
        fwd, up = -p.y, p.z
        c = CHITIN
        if up < 0.45:
            c = lerp(CHITIN, CHITIN_DARK, min(1.0, (0.45 - up) * 2.5))      # darker legs towards the claws
        if n.z < -0.4 and up > 0.35:
            c = lerp(c, BELLY, vw.smoothstep(-0.4, -0.8, n.z))             # pale underside
        if fwd < -0.15 and up > 0.6:
            band = vw.smoothstep(0.35, 0.85, math.sin(fwd * 18.0) * 0.5 + 0.5) * vw.smoothstep(0.6, 0.9, up)
            c = lerp(c, LIME, band)                                          # banded abdomen
        return c
    vw.paint(body, col, lambda p, n: (0.72, 0.0))
    parts = [body, glow_blob("sac", F(0, -0.52, 1.02), (0.12, 0.17, 0.07), LIME_GLOW, 0.55)]
    for s in (-1, 1):
        parts.append(glow_blob("eye_a", F(s * 0.08, 0.84, 0.77), (0.032, 0.032, 0.032), GOLD, 0.9))
        parts.append(glow_blob("eye_b", F(s * 0.15, 0.8, 0.72), (0.024, 0.024, 0.024), GOLD, 0.9))
    finish(parts, "Enemy_Crawler", 0.35)


# ------------------------------------------------------------------ Spitter (2.4 m tripod with throat sac)
def spitter():
    vw.reset_scene()
    sk = Skeleton()
    base = sk.add(F(0, 0, 0.72), 0.24)
    chest = sk.chain(base, [(F(0, 0.0, 1.05), (0.52, 0.5)), (F(0, 0.05, 1.45), (0.46, 0.44))])
    neck = sk.add(F(0, 0.14, 1.82), 0.26, chest)
    head = sk.add(F(0, 0.22, 2.02), (0.24, 0.22), neck)
    sk.chain(head, [(F(0, 0.48, 1.84), 0.17), (F(0, 0.72, 1.7), 0.14)])                          # mortar mouth
    for i in range(3):
        a = math.radians(i * 120 + 60)
        dx, df = math.sin(a), math.cos(a)
        hip = sk.add(F(dx * 0.32, df * 0.32, 0.82), 0.12, base)
        sk.chain(hip, [(F(dx * 0.82, df * 0.82, 1.18), 0.095), (F(dx * 1.0, df * 1.0, 0.02), 0.035)])
    body = sk.build("spitter_body")
    vw.displace(body, strength=0.018, size=0.1)

    def col(p, n):
        up = p.z
        if up < 0.75:
            return lerp(CHITIN_DARK, CHITIN, vw.smoothstep(0.0, 0.6, up))   # legs
        band = vw.smoothstep(0.55, 0.9, math.sin(up * 11.0) * 0.5 + 0.5) * vw.smoothstep(0.8, 1.0, up) * (1.0 - vw.smoothstep(1.6, 1.8, up))
        return lerp(LIME, CHITIN, band)                                      # chitin bands around the bulb
    vw.paint(body, col, lambda p, n: (0.68, 0.0))
    parts = [body, glow_blob("throat", F(0, 0.36, 1.33), (0.27, 0.22, 0.3), LIME_GLOW, 0.5),
             glow_blob("maw", F(0, 0.79, 1.67), (0.08, 0.05, 0.08), LIME_GLOW, 0.9)]
    for s in (-1, 1):
        parts.append(glow_blob("eye", F(s * 0.14, 0.36, 2.08), (0.035, 0.035, 0.035), GOLD, 0.9))
    finish(parts, "Enemy_Spitter", 0.5)


# ------------------------------------------------------------------ Flying Critter (2 m wingspan manta)
def critter():
    vw.reset_scene()
    # One flat diamond body: an ellipsoid warped into swept, upturned wing tips, plus cephalic horns and a whip tail.
    body = vw.ellipsoid("critter_body", (0, 0, 0), (1.0, 0.55, 0.12), segments=48, rings=24)

    def manta(co):
        w = min(1.0, abs(co.x))
        thin = 1.0 - 0.75 * w * w                         # membranes get thin towards the tips
        co.z = co.z * thin + 0.22 * w * w                 # tips curl up
        co.y = co.y + 0.42 * w * w                        # and sweep back (Blender +Y = backwards)
        if co.y < 0.0:
            co.y *= 1.0 - 0.35 * w                        # leading edge stays pointed at the centre
        return co
    vw.warp(body, manta)
    vw.displace(body, strength=0.008, size=0.12)

    def col(p, n):
        c = lerp(PINK, CHITIN, vw.smoothstep(0.25, 0.9, abs(p.x)))
        if n.z < -0.2:
            c = lerp(c, BELLY, 0.6)
        return c
    vw.paint(body, col, lambda p, n: (0.8, 0.0))
    sk = Skeleton()
    t0 = sk.add(F(0, -0.45, 0.0), 0.08)
    sk.chain(t0, [(F(0, -0.95, 0.04), 0.03), (F(0, -1.45, 0.1), 0.008)])
    tail = sk.build("tail", subdiv=1)
    vw.solid(tail, CHITIN_DARK, 0.6)
    parts = [body, tail]
    for s in (-1, 1):
        horn = Skeleton()
        h0 = horn.add(F(s * 0.16, 0.4, 0.0), 0.05)
        horn.chain(h0, [(F(s * 0.2, 0.7, -0.03), 0.03), (F(s * 0.12, 0.86, -0.05), 0.008)])
        ho = horn.build("horn", subdiv=1)
        vw.solid(ho, CHITIN, 0.7)
        parts.append(ho)
        for k in range(4):
            parts.append(glow_blob("spot", F(s * (0.3 + k * 0.17), -0.02 - k * 0.07, 0.1 + k * k * 0.012), (0.035, 0.035, 0.015), LIME_GLOW, 0.6))
        parts.append(glow_blob("eye", F(s * 0.12, 0.42, 0.08), (0.03, 0.03, 0.03), GOLD, 0.9))
    finish(parts, "Enemy_FlyingCritter", 0.3)


# ------------------------------------------------------------------ Nest Guardian (5.2 m armoured beast)
def guardian():
    vw.reset_scene()
    sk = Skeleton()
    tailend = sk.add(F(0, -1.15, 1.8), (0.55, 0.5))
    rear = sk.add(F(0, -0.85, 1.88), (0.85, 0.8), tailend)
    front = sk.chain(rear, [(F(0, -0.45, 2.02), (1.05, 0.98)), (F(0, -0.05, 2.14), (1.12, 1.04)),
                            (F(0, 0.4, 2.26), (0.98, 0.92)), (F(0, 0.62, 2.4), (0.75, 0.7))])
    neck = sk.add(F(0, 0.86, 2.7), 0.48, front)
    head = sk.add(F(0, 1.04, 3.0), (0.44, 0.4), neck)
    sk.add(F(0, 1.32, 2.8), 0.24, head)                                                             # jaw
    for s in (-1, 1):
        for fwd in (0.5, -0.62):
            hip = sk.add(F(s * 0.82, fwd, 1.95), 0.33, front if fwd > 0 else rear)
            sk.chain(hip, [(F(s * 1.52, fwd + 0.25, 1.62), 0.27), (F(s * 1.42, fwd + 0.35, 0.04), 0.2)])
        horn = sk.add(F(s * 0.24, 1.1, 3.25), 0.12, head)
        sk.chain(horn, [(F(s * 0.5, 1.28, 3.8), 0.07), (F(s * 0.56, 1.05, 4.25), 0.015)])
        for k in range(4):                                                                          # back spikes
            b = sk.add(F(s * 0.32, -0.75 + k * 0.42, 2.85 + k * 0.08), 0.09, rear if k < 2 else front)
            sk.add(F(s * 0.42, -0.85 + k * 0.42, 3.35 + k * 0.08), 0.01, b)
    body = sk.build("guardian_body", subdiv=3)
    vw.displace(body, strength=0.035, size=0.2)
    vw.paint(body, lambda p, n: lerp(CHITIN_DARK, BELLY if n.z < -0.6 else CHITIN, vw.smoothstep(0.3, 1.2, p.z)), lambda p, n: (0.75, 0.0))
    parts = [body]
    # layered frontal armour shells, curved around the chest, lime towards their lower rims
    for fwd, up, rx, rz, tilt in [(1.12, 1.85, 0.95, 0.62, 30), (1.28, 2.38, 0.82, 0.5, 18), (1.3, 3.05, 0.5, 0.3, 8)]:
        plate = vw.ellipsoid("plate", F(0, fwd, up), (rx, 0.12, rz), (math.radians(tilt), 0, 0), segments=32, rings=16)
        vw.warp(plate, lambda co: (co.x, co.y + 0.35 * co.x * co.x, co.z))
        vw.displace(plate, strength=0.02, size=0.15)
        vw.paint(plate, lambda p, n, cz=up, h=rz: lerp(CHITIN, LIME, vw.smoothstep(-0.3, -0.85, (p.z - cz) / h)), lambda p, n: (0.85, 0.0))
        parts.append(plate)
    for e in range(5):
        a = e * 1.26
        parts.append(glow_blob("egg", F(math.cos(a) * 0.42, -1.45 - (e % 2) * 0.12, 1.9 + math.sin(a) * 0.32), (0.22, 0.27, 0.22), LIME_GLOW, 0.55))
    for s in (-1, 1):
        parts.append(glow_blob("eye", F(s * 0.2, 1.42, 3.12), (0.055, 0.055, 0.055), GOLD, 0.9))
    finish(parts, "Enemy_NestGuardian", 1.2)


crawler()
spitter()
critter()
guardian()
