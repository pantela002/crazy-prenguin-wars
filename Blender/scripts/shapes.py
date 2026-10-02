"""Reusable weapon/projectile shapes (game frame: +X forward, +Z up, -Y towards camera).
Every function returns a list of objects (world-space geometry, joined later)."""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math
import random

from mathutils import Matrix, Vector

import common as C

V = Vector
GLOWING = ("fire", "plasma")


def M(name, hexcol=None, emission=0.0):
    """Palette material; fire/plasma become "Glow_*" materials (drawn bright and unoutlined in Unity)."""
    if name in GLOWING and hexcol is None:
        return G(name)
    return C.mat(name, hexcol, emission)


def G(name):
    """Self-lit material for energy/fire (Unity: Mats.ApplyToon detects the Glow prefix)."""
    return C.mat(C.GLOW_PREFIX + "_" + name, C.PALETTE.get(name, "#ffffff"))
X = (1, 0, 0)


def xlathe(n, prof, mat, segs=14, x0=0.0, y=0.0, z=0.0, scale=(1, 1, 1)):
    """Lathe along +X starting at x0; prof = [(radius, x), ...]."""
    return C.lathe(n, prof, mat, segs=segs, center=(x0, y, z), axis=X, scale=scale)


def tube(n, x0, x1, r, mat, y=0.0, z=0.0, segs=14, r1=None):
    return C.cyl(n, (x0, y, z), (x1, y, z), r, r if r1 is None else r1, mat, segs)


def ring_x(n, x, r, th, mat, y=0.0, z=0.0, segs=12):
    return C.torus(n, (x, y, z), r, th, mat, segs, 6, axis=X)


def fins(n, x, r, length, mat, count=4, height=0.12, y=0.0, z=0.0, rot0=45):
    o = []
    for i in range(count):
        a = math.radians(rot0 + i * 360 / count)
        pts = [(0, 0), (length, 0), (length * 0.85, height), (length * 0.15, height * 0.9)]
        f = C.extrude(n + "_f%d" % i, pts, 0.025, mat)  # in xz plane, thin along y
        f.data.transform(Matrix.Translation((x, y, z)) @ Matrix.Rotation(a, 4, "X") @ Matrix.Translation((0, 0, r * 0.8)))
        o.append(f)
    return o


def grip(n, mat, x=0.0, z=0.0, h=0.32, angle=-15, w=0.09):
    g = C.box(n + "_grip", (x, 0, z - h / 2), (w * 1.3, w, h), mat, bevel=0.025)
    g.data.transform(Matrix.Translation((x, 0, z)) @ Matrix.Rotation(math.radians(angle), 4, "Y") @ Matrix.Translation((-x, 0, -z)))
    return [g]


def trigger(n, mat, x=0.1, z=0.0):
    return [C.torus(n + "_tg", (x, 0, z - 0.07), 0.06, 0.012, mat, 12, 4, axis=(0, 1, 0))]


# ----------------------------------------------------------------------------------- projectiles

def rocket(n, body="red", nose="grey_light", fin="gun", length=0.85, r=0.09, stripe=None, x0=None):
    x0 = -length / 2 if x0 is None else x0
    L = length
    o = [xlathe(n + "_b", [(0.0, 0), (r * 0.7, 0.0), (r, 0.06), (r, L * 0.62)], M(body), x0=x0),
         xlathe(n + "_n", [(r, 0), (r * 0.85, L * 0.12), (r * 0.5, L * 0.28), (0.0, L * 0.38)], M(nose), x0=x0 + L * 0.62),
         xlathe(n + "_e", [(r * 0.55, 0), (r * 0.7, -0.06), (r * 0.6, -0.07)], M("gun_dark"), x0=x0 + 0.01)]
    o += fins(n, x0 + 0.02, r, L * 0.28, M(fin), height=r * 1.3)
    if stripe:
        o.append(ring_x(n + "_s", x0 + L * 0.5, r * 1.0, r * 0.12, M(stripe)))
    return o


def bomb(n, body="gun", stripe="yellow", length=1.0, r=0.26, fin="gun_dark", sign=True, x0=None):
    x0 = -length / 2 if x0 is None else x0
    L = length
    prof = [(0.0, 0), (r * 0.35, 0.02), (r * 0.75, L * 0.12), (r, L * 0.35), (r, L * 0.62), (r * 0.8, L * 0.84),
            (r * 0.4, L * 0.97), (0.0, L)]
    o = [xlathe(n + "_b", prof, M(body), segs=18, x0=x0)]
    o += fins(n, x0 - L * 0.08, r * 0.45, L * 0.32, M(fin), height=r * 1.0)
    o.append(ring_x(n + "_ft", x0 - L * 0.04, r * 0.75, 0.025, M(fin)))
    if stripe:
        o.append(ring_x(n + "_s", x0 + L * 0.5, r * 1.0, r * 0.07, M(stripe)))
    if sign:
        o += radiation(n, (x0 + L * 0.42, -r * 0.98, 0), r * 0.55)
    return o


def radiation(n, center, size):
    o = [C.cyl(n + "_rb", (center[0], center[1] + 0.005, center[2]), (center[0], center[1] - 0.012, center[2]),
               size, size, M("yellow"), 18)]
    for i in range(3):
        a0 = math.radians(90 + i * 120 - 30)
        pts = [(0, 0)] + [((size * 0.85) * math.cos(a0 + math.radians(60) * t / 5), (size * 0.85) * math.sin(a0 + math.radians(60) * t / 5))
                          for t in range(6)]
        pts = [(size * 0.18 * math.cos(a0 + math.radians(30)), size * 0.18 * math.sin(a0 + math.radians(30)))] + pts[1:]
        w = C.extrude(n + "_rw%d" % i, pts, 0.012, M("black"))
        w.data.transform(Matrix.Translation((center[0], center[1] - 0.015, center[2])))
        o.append(w)
    return o


def grenade(n, body="olive", r=0.17, lever="steel", bumps=True):
    o = [C.sphere(n + "_b", (0, 0, 0), (r, r * 0.95, r * 1.15), M(body), 14, 10)]
    if bumps:
        for i in range(3):
            o.append(C.torus(n + "_g%d" % i, (0, 0, -r * 0.5 + i * r * 0.5), r * (0.95 if i == 1 else 0.82), r * 0.07,
                             M(body + "_dark" if (body + "_dark") in C.PALETTE else body), 14, 4))
    o.append(C.cyl(n + "_top", (0, 0, r * 0.95), (0, 0, r * 1.35), r * 0.35, r * 0.3, M(lever), 10))
    o.append(C.box(n + "_lv", (r * 0.35, 0, r * 0.75), (r * 0.25, r * 0.25, r * 1.1), M(lever),
                   rot=Matrix.Rotation(0.25, 4, "Y")))
    o.append(C.torus(n + "_pin", (-r * 0.45, 0, r * 1.3), r * 0.22, r * 0.04, M("steel_dark"), 12, 4, axis=(0, 1, 0)))
    return o


def lemon(n, r=0.18):
    o = [C.sphere(n + "_b", (0, 0, 0), (r * 1.3, r, r), M("lemon"), 14, 10)]
    for sx in (-1, 1):
        o.append(C.cyl(n + "_t%d" % sx, (r * 1.2 * sx, 0, 0), (r * 1.45 * sx, 0, 0), r * 0.3, r * 0.05, M("lemon"), 8))
    o.append(C.sphere(n + "_lf", (0.02, 0, r * 0.95), (r * 0.5, r * 0.15, r * 0.25), M("leaf"), 8, 5,
                      rot=Matrix.Rotation(0.4, 4, "Y")))
    o.append(C.torus(n + "_pin", (-r * 0.3, 0, r * 1.1), r * 0.25, r * 0.05, M("steel_dark"), 12, 4, axis=(0, 1, 0)))
    return o


def canister(n, body="green", cap="gun", r=0.13, h=0.38, label="yellow"):
    o = [C.cyl(n + "_b", (0, 0, -h / 2), (0, 0, h / 2), r, r, M(body), 16),
         C.cyl(n + "_c", (0, 0, h / 2), (0, 0, h / 2 + 0.06), r * 0.9, r * 0.6, M(cap), 12),
         C.cyl(n + "_l", (0, 0, -h * 0.15), (0, 0, h * 0.15), r * 1.02, r * 1.02, M(label), 16),
         C.torus(n + "_pin", (r * 0.4, 0, h / 2 + 0.08), 0.04, 0.01, M("steel_dark"), 10, 4, axis=(0, 1, 0))]
    return o


def dynamite(n):
    o = []
    for i, (y, z) in enumerate(((0.0, 0.0), (0.03, 0.11), (0.03, -0.11))):
        o.append(C.cyl(n + "_s%d" % i, (-0.22, y, z), (0.22, y, z), 0.06, 0.06, M("red"), 12))
    for x in (-0.12, 0.12):
        o.append(C.box(n + "_t%d" % (x > 0), (x, 0.0, 0), (0.06, 0.15, 0.34), M("tan")))
    o.append(C.cyl(n + "_fuse", (0.22, 0, 0), (0.36, 0, 0.12), 0.012, 0.012, M("grey_dark"), 6))
    o.append(C.sphere(n + "_sp", (0.37, 0, 0.13), 0.035, M("yellow"), 8, 5))
    return o


def molotov(n):
    o = [C.lathe(n + "_b", [(0.0, -0.22), (0.11, -0.22), (0.12, -0.16), (0.12, 0.02), (0.05, 0.1), (0.04, 0.2), (0.0, 0.2)],
                 M("green_dark"), 14),
         C.sphere(n + "_rag", (0, 0, 0.24), (0.06, 0.06, 0.09), M("cream"), 8, 6),
         C.sphere(n + "_fl", (0.01, 0, 0.33), (0.05, 0.05, 0.08), M("fire"), 8, 6)]
    return o


def mine(n, light="red", body="gun"):
    return [C.cyl(n + "_b", (0, 0, 0), (0, 0, 0.1), 0.3, 0.26, M(body), 18),
            C.cyl(n + "_t", (0, 0, 0.1), (0, 0, 0.15), 0.14, 0.11, M("grey"), 14),
            C.sphere(n + "_l", (0, 0, 0.16), 0.06, M(light), 10, 6),
            C.torus(n + "_r", (0, 0, 0.03), 0.3, 0.025, M("yellow"), 18, 4)]


def spring_mine(n):
    """Spring mine: a blue mine with a coiled spring and a bounce pad on top."""
    import math as _m
    o = [C.cyl(n + "_b", (0, 0, 0), (0, 0, 0.1), 0.3, 0.26, M("blue"), 18),
         C.torus(n + "_r", (0, 0, 0.03), 0.3, 0.025, M("yellow"), 18, 4),
         C.cyl(n + "_base", (0, 0, 0.1), (0, 0, 0.13), 0.13, 0.12, M("grey"), 14)]
    for i in range(4):
        o.append(C.torus(n + "_c%d" % i, (0, 0, 0.15 + i * 0.045), 0.1 - 0.008 * (i % 2), 0.016, M("silver"), 14, 4))
    o.append(C.cyl(n + "_pad", (0, 0, 0.32), (0, 0, 0.36), 0.15, 0.15, M("red"), 16))
    o.append(C.sphere(n + "_l", (0.19, -0.1, 0.1), 0.045, G("lime"), 8, 5))
    return o


def orbital_beam(n, length=1.3, r=0.13):
    """Orbital laser strike: a long glowing beam segment along x with a white core and energy rings."""
    o = [C.sphere(n + "_b", (0, 0, 0), (length / 2, r, r), G("cyan"), 14, 6),
         C.sphere(n + "_c", (length * 0.04, 0, 0), (length * 0.42, r * 0.5, r * 0.5), G("white"), 12, 5)]
    for i, x in enumerate((-0.32, 0.0, 0.32)):
        o.append(C.torus(n + "_g%d" % i, (x * length, 0, 0), r * (1.25 - 0.15 * abs(i - 1)), r * 0.12, G("sky"), 14, 3,
                         axis=(1, 0, 0)))
    return o


def rock(n, r=0.22, seed=1, mat="stone"):
    return [C.ico(n + "_r", (0, 0, 0), r, M(mat), subdiv=1, seed=seed, jitter=0.2, scale=(1.1, 0.95, 0.9))]


def snowball(n, r=0.18):
    return [C.ico(n + "_r", (0, 0, 0), r, M("snow"), subdiv=2, seed=3, jitter=0.06, smooth=True)]


def balloon(n, mat="blue", r=0.2):
    return [C.sphere(n + "_b", (0, 0, 0.02), (r, r, r * 1.15), M(mat), 14, 10),
            C.cyl(n + "_k", (0, 0, -r * 1.1), (0, 0, -r * 1.35), 0.02, 0.05, M(mat), 8)]


def egg(n, colors=("pink", "yellow", "cyan"), r=0.17):
    o = [C.lathe(n + "_b", [(0.0, -r * 1.1), (r * 0.7, -r * 0.95), (r, -r * 0.3), (r * 0.9, r * 0.4), (r * 0.55, r * 1.0),
                            (0.0, r * 1.25)], M(colors[0]), 14)]
    for i, z in enumerate((-0.35, 0.25)):
        rad = r * (0.98 if z < 0 else 0.86)
        o.append(C.torus(n + "_s%d" % i, (0, 0, z * r), rad, r * 0.08, M(colors[1 + i % 2]), 16, 4))
    return o


def glove(n, color="red"):
    return [C.sphere(n + "_f", (0.08, 0, 0), (0.2, 0.17, 0.16), M(color), 14, 10),
            C.sphere(n + "_th", (0.06, -0.12, 0.08), (0.1, 0.06, 0.07), M(color), 10, 6),
            C.cyl(n + "_c", (-0.18, 0, 0), (-0.06, 0, 0), 0.13, 0.14, M("white"), 14)]


def orb(n, mat, r=0.15, cage=None):
    o = [C.sphere(n + "_o", (0, 0, 0), r, M(mat), 14, 10)]
    if cage:
        for i, ax in enumerate(((1, 0, 0), (0, 0, 1), (0.7, 0, 0.7))):
            o.append(C.torus(n + "_c%d" % i, (0, 0, 0), r * 1.15, r * 0.08, M(cage), 18, 4, axis=ax))
    return o


def _lighter(name):
    r, g, b = C.hex_rgb(C.PALETTE.get(name, "#ffffff"))
    return "#%02x%02x%02x" % tuple(int(min(255, (c * 0.5 + 0.5) * 255)) for c in (r, g, b))


def bolt(n, mat, length=0.5, r=0.06):
    """Energy bolt / laser projectile (capsule)."""
    return [C.sphere(n + "_b", (0, 0, 0), (length / 2, r, r), G(mat), 12, 6),
            C.sphere(n + "_c", (length * 0.05, 0, 0), (length * 0.35, r * 0.55, r * 0.55), G("white"), 10, 5)]


def bullet(n, mat="gold", length=0.22, r=0.045):
    return [xlathe(n + "_b", [(0.0, 0), (r, 0), (r, length * 0.55), (r * 0.6, length * 0.85), (0.0, length)], M(mat), 10,
                   x0=-length / 2)]


def shell(n, body="olive", tip="red", length=0.42, r=0.09):
    return [xlathe(n + "_b", [(0.0, 0), (r, 0), (r, length * 0.55), (r * 0.7, length * 0.8), (0.0, length)], M(body), 14,
                   x0=-length / 2),
            ring_x(n + "_s", -length / 2 + length * 0.2, r, r * 0.12, M(tip))]


def cannonball(n, r=0.16, mat="gun_dark"):
    return [C.sphere(n + "_b", (0, 0, 0), r, M(mat), 14, 10),
            C.sphere(n + "_h", (-r * 0.35, -r * 0.55, r * 0.45), r * 0.18, M("grey"), 6, 4)]


def shard(n, mat, r=0.1, seed=2):
    return [C.ico(n + "_s", (0, 0, 0), r, M(mat), subdiv=0, seed=seed, jitter=0.35, scale=(1.4, 0.8, 0.8))]


def blob(n, mat, r=0.16, seed=4, lumps=4):
    rnd = random.Random(seed)
    o = [C.sphere(n + "_b", (0, 0, 0), r, M(mat), 12, 8)]
    for i in range(lumps):
        d = V((rnd.uniform(-1, 1), rnd.uniform(-0.5, 0.5), rnd.uniform(-1, 1))).normalized() * r * 0.7
        o.append(C.sphere(n + "_l%d" % i, d, r * rnd.uniform(0.45, 0.65), M(mat), 10, 6))
    return o


def flame(n, r=0.16):
    return [C.lathe(n + "_o", [(0.0, -r), (r * 0.9, -r * 0.6), (r, 0), (r * 0.6, r * 0.9), (0.0, r * 1.9)], M("fire"), 12),
            C.lathe(n + "_i", [(0.0, -r * 0.75), (r * 0.6, -r * 0.4), (r * 0.55, r * 0.3), (0.0, r * 1.3)], G("yellow"), 10,
                    center=(0, -r * 0.35, 0))]


def drill_bit(n, mat="steel", length=0.45, r=0.13, x0=0.0):
    o = [tube(n + "_c", x0, x0 + length * 0.15, r * 1.1, M("gun"))]
    o.append(C.cyl(n + "_bit", (x0 + length * 0.15, 0, 0), (x0 + length, 0, 0), r, 0.0, M(mat), 10))
    for i in range(4):
        t = 0.25 + i * 0.17
        o.append(ring_x(n + "_th%d" % i, x0 + length * t, r * (1 - (t - 0.15) / 0.85) + 0.01, 0.015, M("steel_dark")))
    return o


def cat(n, s=1.0):
    c = M("cat")
    o = [C.sphere(n + "_body", (0, 0, 0), (0.24 * s, 0.15 * s, 0.15 * s), c, 14, 8),
         C.sphere(n + "_head", (0.24 * s, 0, 0.12 * s), 0.13 * s, c, 14, 8),
         C.sphere(n + "_m", (0.33 * s, -0.06 * s, 0.09 * s), (0.05 * s, 0.05 * s, 0.04 * s), M("white"), 8, 5)]
    for sy in (-1, 1):
        o.append(C.cyl(n + "_ear%d" % sy, (0.22 * s, 0.07 * s * sy, 0.21 * s), (0.22 * s, 0.09 * s * sy, 0.32 * s), 0.05 * s, 0.0, c, 6))
    for sx in (-1, 1):
        for sy in (-1, 1):
            o.append(C.cyl(n + "_leg%d%d" % (sx, sy), (0.13 * s * sx, 0.08 * s * sy, -0.05 * s),
                           (0.15 * s * sx, 0.08 * s * sy, -0.2 * s), 0.045 * s, 0.04 * s, c, 8))
    o.append(C.cyl(n + "_tail", (-0.22 * s, 0, 0.03 * s), (-0.36 * s, 0, 0.25 * s), 0.04 * s, 0.025 * s, M("cat_dark"), 8))
    for sy in (-1, 1):
        o.append(C.sphere(n + "_eye%d" % sy, (0.34 * s, -0.04 * s if sy < 0 else 0.06 * s, 0.16 * s), 0.03 * s,
                          M("pupil"), 8, 5))
    for i in range(3):
        o.append(C.box(n + "_st%d" % i, (-0.05 * s + i * 0.08 * s, -0.14 * s, 0.05 * s), (0.03 * s, 0.02 * s, 0.12 * s),
                       M("cat_dark")))
    return o


def wind(n, r=0.22):
    o = []
    for i in range(3):
        o.append(C.torus(n + "_w%d" % i, (i * 0.08, 0, i * 0.04), r * (1 - i * 0.25), 0.03, M("mint"), 18, 4,
                         axis=(1, 0, 0.2)))
    return o


def mushroom(n):
    return [C.cyl(n + "_st", (0, 0, -0.12), (0, 0, 0.05), 0.07, 0.08, M("cream"), 10),
            C.lathe(n + "_cap", [(0.0, 0.02), (0.2, 0.02), (0.19, 0.08), (0.12, 0.16), (0.0, 0.19)], M("red"), 14)] + \
        [C.sphere(n + "_d%d" % i, (0.13 * math.cos(a), 0.13 * math.sin(a), 0.12), (0.04, 0.04, 0.02), M("white"), 6, 4)
         for i, a in enumerate((0.5, 2.0, 3.6, 5.0))]


def caltrop(n, r=0.12):
    o = []
    for d in ((1, 1, 1), (-1, -1, 1), (-1, 1, -1), (1, -1, -1)):
        o.append(C.cyl(n + "_s%d" % len(o), (0, 0, 0), V(d).normalized() * r, 0.03, 0.0, M("steel"), 6))
    return o
