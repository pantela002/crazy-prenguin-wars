"""Small models used only for icons: boosters, trophies, slot symbols, UI/HUD glyphs, crafting ingredients,
achievement medals and penguin-face emoticons. Game frame (front = -Y). Each builder returns a list of objects."""
import bpy  # noqa: F401
import math
import random

from mathutils import Matrix, Vector

import common as C
import penguin as P
import shapes as S

M = C.mat
V = Vector


# ------------------------------------------------------------------------------------------ helpers

def text(n, s, size, mat, center=(0, 0, 0), depth=0.08, bold=True):
    cu = bpy.data.curves.new(n, "FONT")
    cu.body = s
    cu.size = size
    cu.extrude = depth / 2
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.bevel_depth = depth * 0.15
    cu.bevel_resolution = 1
    cu.resolution_u = 4
    ob = bpy.data.objects.new(n + "_tmp", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    me.transform(Matrix.Translation(center) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    me.materials.clear()
    me.materials.append(mat)
    res = bpy.data.objects.new(n, me)
    return C._link(res)


def star_pts(r, inner=0.45, points=5, rot=0.0):
    return [((r if i % 2 == 0 else r * inner) * math.cos(math.pi / 2 + rot + i * math.pi / points),
             (r if i % 2 == 0 else r * inner) * math.sin(math.pi / 2 + rot + i * math.pi / points)) for i in range(points * 2)]


def flat(n, pts, depth, mat, center=(0, 0, 0), bevel=0.0):
    ob = C.extrude(n, pts, depth, mat, bevel=bevel)
    ob.data.transform(Matrix.Translation(center))
    return ob


def heart_pts(r):
    pts = []
    for i in range(24):
        t = 2 * math.pi * i / 24
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x * r / 16, y * r / 16))
    return pts


def bolt_pts(r):
    return [(-0.1 * r, r), (0.45 * r, r), (0.1 * r, 0.15 * r), (0.5 * r, 0.15 * r), (-0.35 * r, -r), (-0.05 * r, -0.1 * r),
            (-0.45 * r, -0.1 * r)]


def flame_pts(r):
    pts = []
    for i in range(20):
        t = i / 19
        a = math.pi * 2 * t
        x = math.sin(a) * r * 0.65 * (1 - 0.5 * max(0, math.cos(a)))
        y = -math.cos(a) * r
        if math.cos(a) < 0:
            y = -math.cos(a) * r * 1.1 + 0.15 * r * math.sin(3 * a)
        pts.append((x, y * 0.95 - 0.1 * r))
    return pts


def gear_pts(r, teeth=8):
    pts = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4)
        rr = r if (i % 4) in (1, 2) else r * 0.78
        pts.append((rr * math.cos(a), rr * math.sin(a)))
    return pts


def arc(n, center, radius, thick, a0, a1, mat, segs=8, plane="xz"):
    """Tube along an arc in the front plane (angles in degrees, 0 = +X, 90 = +Z)."""
    o = []
    pts = []
    for i in range(segs + 1):
        a = math.radians(a0 + (a1 - a0) * i / segs)
        pts.append(V(center) + V((radius * math.cos(a), 0, radius * math.sin(a))))
    for i in range(segs):
        o.append(C.cyl(n + "_%d" % i, pts[i], pts[i + 1], thick, thick, mat, 6, sharp=False))
    for i in (0, segs):
        o.append(C.sphere(n + "_e%d" % i, pts[i], thick, mat, 6, 4))
    return o


EMBLEM_SHAPES = {
    "star": lambda r: star_pts(r),
    "heart": lambda r: heart_pts(r),
    "bolt": lambda r: bolt_pts(r),
    "flame": lambda r: flame_pts(r),
    "gear": lambda r: gear_pts(r),
    "cross": lambda r: [(-0.3 * r, r), (0.3 * r, r), (0.3 * r, 0.3 * r), (r, 0.3 * r), (r, -0.3 * r), (0.3 * r, -0.3 * r),
                        (0.3 * r, -r), (-0.3 * r, -r), (-0.3 * r, -0.3 * r), (-r, -0.3 * r), (-r, 0.3 * r), (-0.3 * r, 0.3 * r)],
    "diamond": lambda r: [(0, r), (0.7 * r, 0.2 * r), (0, -r), (-0.7 * r, 0.2 * r)],
    "circle": lambda r: [(r * math.cos(2 * math.pi * i / 20), r * math.sin(2 * math.pi * i / 20)) for i in range(20)],
    "drop": lambda r: [(r * 0.7 * math.cos(a), r * 0.7 * math.sin(a) - 0.2 * r) for a in
                       [math.radians(-40 - i * 260 / 16) for i in range(17)]] + [(0, r)],
    "arrow": lambda r: [(-r, 0.25 * r), (0.1 * r, 0.25 * r), (0.1 * r, 0.7 * r), (r, 0), (0.1 * r, -0.7 * r), (0.1 * r, -0.25 * r),
                        (-r, -0.25 * r)],
    "triangle": lambda r: [(0, r), (0.87 * r, -0.5 * r), (-0.87 * r, -0.5 * r)],
}


def emblem(n, kind, r, mat, center, depth=0.06):
    if kind == "target":
        o = []
        for i, (rr, c) in enumerate(((1.0, mat), (0.66, M("white")), (0.33, mat))):
            o.append(C.cyl(n + "_t%d" % i, V(center) + V((0, 0.0 - i * 0.012, 0)), V(center) + V((0, -depth - i * 0.012, 0)),
                           r * rr, r * rr, c, 20))
        return o
    if kind == "eye":
        return [C.sphere(n + "_w", center, (r, depth, r * 0.6), M("white"), 16, 8),
                C.sphere(n + "_p", V(center) + V((0, -depth * 0.6, 0)), (r * 0.4, depth * 0.6, r * 0.4), mat, 12, 6)]
    if kind == "skull":
        o = [C.sphere(n + "_s", center, (r, depth, r * 0.9), M("bone"), 16, 8),
             C.box(n + "_j", V(center) + V((0, 0, -r * 0.8)), (r * 1.0, depth * 1.6, r * 0.5), M("bone"))]
        for sx in (-1, 1):
            o.append(C.sphere(n + "_e%d" % sx, V(center) + V((sx * r * 0.38, -depth * 0.7, -r * 0.05)), (r * 0.25, depth * 0.5, r * 0.28),
                              M("gun_dark"), 10, 6))
        return o
    if kind == "wing":
        o = []
        for i in range(4):
            o.append(C.sphere(n + "_f%d" % i, V(center) + V((-r * 0.2 + i * r * 0.22, 0, r * 0.3 - i * r * 0.2)),
                              (r * 0.6 - i * r * 0.1, depth, r * 0.18), mat, 12, 6,
                              rot=Matrix.Rotation(math.radians(20 + i * 8), 4, "Y")))
        return o
    pts = EMBLEM_SHAPES[kind](r)
    return [flat(n + "_" + kind, pts, depth, mat, (center[0], center[1] - depth / 2, center[2]))]


# ------------------------------------------------------------------------------------------ boosters

def nigiri(n, topping="orange", stripes="white", wasabi=False):
    o = [C.box(n + "_rice", (0, 0, 0.15), (0.75, 0.42, 0.28), M("white"), bevel=0.12, bevel_segs=3)]
    top = C.box(n + "_fish", (0, 0, 0.33), (0.92, 0.5, 0.12), M(topping), bevel=0.05, bevel_segs=2)
    o.append(top)
    for i in range(3):
        o.append(C.box(n + "_st%d" % i, (-0.25 + i * 0.25, 0, 0.395), (0.05, 0.5, 0.01), M(stripes),
                       rot=Matrix.Rotation(0.4, 4, "Z")))
    if wasabi:
        o.append(C.ico(n + "_w", (0.05, -0.05, 0.47), 0.12, M("lime"), subdiv=2, seed=3, jitter=0.15, smooth=True))
    return o


def maki(n, center="red"):
    o = [C.cyl(n + "_n", (0, 0, 0), (0, 0, 0.35), 0.32, 0.32, M("green_dark"), 20),
         C.cyl(n + "_r", (0, 0, 0.35), (0, 0, 0.37), 0.29, 0.29, M("white"), 20),
         C.cyl(n + "_c", (0, 0, 0.36), (0, 0, 0.39), 0.12, 0.12, M(center), 12)]
    o.append(C.sphere(n + "_s", (0.1, -0.05, 0.4), (0.1, 0.1, 0.04), M("red"), 10, 5))
    return o


def umbrella(n):
    o = [C.lathe(n + "_c", [(0.78, 0.5), (0.62, 0.7), (0.32, 0.86), (0.0, 0.92)], M("red"), segs=16)]
    for i in range(4):
        a = 2 * math.pi * i / 4
        o.append(C.sphere(n + "_s%d" % i, (0.45 * math.cos(a), 0.45 * math.sin(a), 0.73), (0.2, 0.2, 0.08), M("white"), 10, 5,
                          rot=Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(-0.6, 4, "Y")))
    o.append(C.cyl(n + "_h", (0, 0, -0.5), (0, 0, 0.98), 0.025, 0.025, M("gun_dark"), 8))
    o += arc(n + "_hook", (0.1, 0, -0.5), 0.1, 0.03, 180, 360, M("brown"))
    return o


def scroll(n):
    o = [C.box(n + "_p", (0, 0, 0), (0.9, 0.04, 0.6), M("cream"), bevel=0.01)]
    for z in (0.32, -0.32):
        o.append(C.cyl(n + "_r%d" % (z > 0), (-0.5, 0, z), (0.5, 0, z), 0.07, 0.07, M("tan"), 14))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_k%d%d" % (z > 0, sx), (0.53 * sx, 0, z), 0.06, M("brown"), 8, 5))
    for i in range(4):
        o.append(C.box(n + "_l%d" % i, (-0.05 + 0.03 * (i % 2), -0.025, 0.17 - i * 0.12), (0.6 - 0.1 * (i % 2), 0.01, 0.03), M("brown")))
    o.append(C.torus(n + "_rib", (0, 0, 0), 0.08, 0.025, M("red"), 12, 4, axis=(0, 1, 0)))
    o.append(C.sphere(n + "_seal", (0.25, -0.03, -0.18), (0.08, 0.03, 0.08), M("red"), 10, 5))
    return o


def pogo(n):
    o = [C.cyl(n + "_s", (0, 0, -0.55), (0, 0, 0.7), 0.04, 0.04, M("steel"), 10),
         C.cyl(n + "_h", (-0.3, 0, 0.7), (0.3, 0, 0.7), 0.04, 0.04, M("steel"), 8)]
    for sx in (-1, 1):
        o.append(C.cyl(n + "_g%d" % sx, (0.18 * sx, 0, 0.7), (0.34 * sx, 0, 0.7), 0.06, 0.06, M("red"), 10))
        o.append(C.box(n + "_f%d" % sx, (0.14 * sx, 0, -0.15), (0.2, 0.1, 0.04), M("gun_dark")))
    for i in range(7):
        o.append(C.torus(n + "_sp%d" % i, (0, 0, -0.45 + i * 0.045), 0.09, 0.018, M("yellow"), 12, 4))
    o.append(C.cyl(n + "_ft", (0, 0, -0.6), (0, 0, -0.55), 0.07, 0.06, M("rubber"), 10))
    o.append(C.cyl(n + "_b", (0, 0, -0.2), (0, 0, 0.4), 0.07, 0.07, M("red"), 12))
    return o


def bandage(n):
    o = [C.box(n + "_b", (0, 0, 0), (1.0, 0.06, 0.32), M("peach"), bevel=0.14, bevel_segs=4)]
    o.append(C.box(n + "_pad", (0, -0.035, 0), (0.32, 0.02, 0.24), M("cream"), bevel=0.03))
    for i in range(6):
        for sx in (-1, 1):
            o.append(C.sphere(n + "_h%d%d" % (i, sx), (sx * (0.25 + 0.05 * (i % 3)), -0.035, -0.06 + 0.06 * (i // 3)), 0.012,
                              M("orange_dark"), 4, 3))
    return o


def burrito(n):
    o = [C.cyl(n + "_t", (-0.45, 0, 0), (0.45, 0, 0), 0.22, 0.24, M("tan"), 16)]
    o.append(C.sphere(n + "_e", (-0.45, 0, 0), (0.06, 0.22, 0.22), M("tan"), 12, 6))
    o.append(C.sphere(n + "_fill", (0.47, 0, 0), (0.05, 0.2, 0.2), M("brown"), 12, 6))
    for i, c in enumerate(("red", "green", "yellow", "white")):
        a = i * 1.6
        o.append(C.sphere(n + "_f%d" % i, (0.5, 0.1 * math.cos(a), 0.1 * math.sin(a)), 0.06, M(c), 8, 5))
    o.append(C.cyl(n + "_wrap", (-0.4, 0, 0), (-0.05, 0, 0), 0.245, 0.245, M("white"), 16))
    return o


def kamikaze(n):
    o = [C.torus(n + "_band", (0, 0, 0), 0.45, 0.08, M("white"), 24, 6, scale=(1, 1, 2.0))]
    o.append(C.cyl(n + "_sun", (0, -0.5, 0), (0, -0.54, 0), 0.11, 0.11, M("red"), 16))
    for sx in (-1, 1):
        o.append(C.box(n + "_t%d" % sx, (0.15 * sx, 0.6, -0.2), (0.08, 0.03, 0.5), M("white"),
                       rot=Matrix.Rotation(0.4 * sx, 4, "Y")))
    return o


def protein_bar(n):
    o = [C.box(n + "_b", (0, 0, 0), (1.0, 0.3, 0.22), M("blue"), bevel=0.04)]
    for sx in (-1, 1):
        o.append(C.box(n + "_c%d" % sx, (0.53 * sx, 0, 0), (0.08, 0.32, 0.18), M("silver"), bevel=0.01))
    o.append(C.box(n + "_l", (0, -0.155, 0), (0.5, 0.02, 0.14), M("yellow")))
    o.append(text(n + "_tx", "PRO", 0.12, M("red"), (0, -0.17, -0.005), 0.02))
    return o


def confetti(n):
    o = [C.cyl(n + "_cone", (0, 0, -0.5), (0.15, 0, 0.15), 0.04, 0.22, M("magenta"), 14)]
    for i in range(3):
        o.append(C.torus(n + "_st%d" % i, (0.04 + 0.05 * i, 0, -0.35 + 0.2 * i), 0.08 + 0.05 * i, 0.015, M("yellow"), 14, 4,
                         axis=(-0.23, 0, 1)))
    rnd = random.Random(5)
    cols = ["yellow", "cyan", "lime", "red", "blue", "orange"]
    for i in range(16):
        p = (0.2 + rnd.uniform(-0.35, 0.45), rnd.uniform(-0.2, 0.2), 0.3 + rnd.uniform(0, 0.55))
        o.append(C.box(n + "_c%d" % i, p, (0.07, 0.015, 0.04), M(cols[i % 6]),
                       rot=Matrix.Rotation(rnd.uniform(0, 3), 4, "Y") @ Matrix.Rotation(rnd.uniform(0, 3), 4, "Z")))
    return o


def shield(n):
    pts = [(-0.45, 0.45), (0.45, 0.45), (0.45, 0.0), (0.3, -0.3), (0.0, -0.55), (-0.3, -0.3), (-0.45, 0.0)]
    o = [flat(n + "_s", pts, 0.12, M("blue"), (0, 0.06, 0), bevel=0.04)]
    inner = [(x * 0.78, y * 0.78 + 0.02) for (x, y) in pts]
    o.append(flat(n + "_i", inner, 0.04, M("silver"), (0, -0.03, 0)))
    o += emblem(n + "_e", "star", 0.2, M("yellow"), (0, -0.08, 0.02), 0.04)
    return o


BOOSTERS = {
    "SalmonSushi": lambda n: nigiri(n, "orange", "white"),
    "SpicySushi": lambda n: maki(n, "red"),
    "WasabiSushi": lambda n: nigiri(n, "rose", "white", wasabi=True),
    "Shield": shield,
    "Umbrella": umbrella,
    "Scroll": scroll,
    "PogoStick": pogo,
    "Bandage": bandage,
    "Burrito": burrito,
    "Kamikaze": kamikaze,
    "Caltrops": lambda n: _caltrops(n),
    "Mine": lambda n: S.mine(n, "red"),
    "FlameMine": lambda n: S.mine(n, "fire", "orange_dark"),
    "ProteinBar": protein_bar,
    "Mushroom": lambda n: S.mushroom(n),
    "Confetti": confetti,
    "SpringMine": lambda n: S.spring_mine(n),
    "Innertube": lambda n: innertube(n),
}


def innertube(n):
    """Swim ring: a red/white striped inflatable torus with a valve."""
    ring = C.torus(n + "_t", (0, 0, 0), 0.36, 0.15, M("red"), 32, 10, axis=(0, 1, 0))
    ring.data.materials.append(M("white"))
    for p in ring.data.polygons:
        c = p.center
        a = math.atan2(c.z, c.x) % (2 * math.pi)
        p.material_index = int(a / (math.pi / 4)) % 2
    return [ring, C.cyl(n + "_v", (0.0, -0.1, 0.48), (0.0, -0.1, 0.56), 0.03, 0.025, M("grey"), 8)]


def _caltrops(n):
    o = []
    for i, (x, y) in enumerate(((0, 0), (0.3, 0.15), (-0.25, 0.2))):
        for p in S.caltrop(n + "_%d" % i, 0.16):
            p.data.transform(Matrix.Translation((x, y, 0)) @ Matrix.Rotation(i * 0.9, 4, "Z"))
            o.append(p)
    return o


# ------------------------------------------------------------------------------------------ trophies

def cup(n, col="gold", col2="gold_dark", em=None, em_col="red"):
    o = [C.lathe(n + "_cup", [(0.0, 0.25), (0.12, 0.27), (0.3, 0.45), (0.38, 0.75), (0.4, 0.95), (0.36, 0.95), (0.0, 0.9)],
                 M(col), segs=20),
         C.cyl(n + "_stem", (0, 0, 0.05), (0, 0, 0.3), 0.06, 0.08, M(col), 12),
         C.cyl(n + "_base", (0, 0, -0.15), (0, 0, 0.05), 0.3, 0.26, M(col2), 16),
         C.box(n + "_plate", (0, -0.27, -0.05), (0.3, 0.02, 0.1), M("cream"))]
    for sx in (-1, 1):
        o.append(C.torus(n + "_h%d" % sx, (0.4 * sx, 0, 0.68), 0.15, 0.035, M(col), 14, 6, axis=(0, 1, 0)))
    if em:
        o += emblem(n + "_em", em, 0.14, M(em_col), (0, -0.36, 0.65), 0.04)
    return o


def medal(n, col="gold", ribbon=("red", "white"), em=None, em_col="red", shape="circle"):
    o = []
    if shape == "circle":
        o.append(C.cyl(n + "_d", (0, 0.03, 0), (0, -0.05, 0), 0.32, 0.32, M(col), 24))
        o.append(C.torus(n + "_rim", (0, -0.05, 0), 0.3, 0.025, M(col + "_dark" if col + "_dark" in C.PALETTE else col), 24, 4,
                         axis=(0, 1, 0)))
    elif shape == "star":
        o.append(flat(n + "_d", star_pts(0.38, 0.55), 0.08, M(col), (0, -0.01, 0), bevel=0.02))
    elif shape == "heart":
        o.append(flat(n + "_d", heart_pts(0.36), 0.08, M(col), (0, -0.01, 0), bevel=0.02))
    elif shape == "cross":
        o.append(flat(n + "_d", EMBLEM_SHAPES["cross"](0.36), 0.08, M(col), (0, -0.01, 0), bevel=0.02))
    # ribbon (V shape)
    for i, sx in enumerate((-1, 1)):
        o.append(C.box(n + "_r%d" % i, (0.12 * sx, 0.06, 0.55), (0.2, 0.03, 0.6), M(ribbon[i % 2]),
                       rot=Matrix.Rotation(-0.3 * sx, 4, "Y")))
    o.append(C.box(n + "_bar", (0, 0.05, 0.82), (0.5, 0.05, 0.12), M(ribbon[0]), bevel=0.02))
    if em:
        o += emblem(n + "_em", em, 0.16, M(em_col), (0, -0.07, 0), 0.04)
    return o


def badge(n, col="blue", rim="silver", em=None, em_col="yellow", shape="shield"):
    if shape == "shield":
        pts = [(-0.38, 0.4), (0.38, 0.4), (0.38, 0.0), (0.25, -0.28), (0.0, -0.48), (-0.25, -0.28), (-0.38, 0.0)]
    elif shape == "hex":
        pts = [(0.42 * math.cos(math.radians(30 + 60 * i)), 0.42 * math.sin(math.radians(30 + 60 * i))) for i in range(6)]
    else:
        pts = EMBLEM_SHAPES["circle"](0.4)
    o = [flat(n + "_b", pts, 0.1, M(rim), (0, 0.04, 0), bevel=0.02),
         flat(n + "_i", [(x * 0.8, y * 0.8) for x, y in pts], 0.06, M(col), (0, -0.02, 0))]
    if em:
        o += emblem(n + "_em", em, 0.18, M(em_col), (0, -0.07, 0), 0.04)
    return o


def certificate(n, col="cream", seal="red", em=None):
    o = [C.box(n + "_p", (0, 0, 0), (1.0, 0.03, 0.72), M(col), bevel=0.01),
         C.box(n + "_f", (0, -0.016, 0), (0.9, 0.01, 0.62), M("gold"))]
    o.append(C.box(n + "_fi", (0, -0.02, 0), (0.84, 0.01, 0.56), M(col)))
    for i in range(3):
        o.append(C.box(n + "_l%d" % i, (-0.1, -0.03, 0.15 - i * 0.12), (0.5, 0.01, 0.03), M("grey")))
    o.append(C.cyl(n + "_seal", (0.28, -0.02, -0.18), (0.28, -0.06, -0.18), 0.13, 0.13, M(seal), 16))
    if em:
        o += emblem(n + "_em", em, 0.07, M("gold"), (0.28, -0.07, -0.18), 0.02)
    for sx in (-1, 1):
        o.append(C.box(n + "_rb%d" % sx, (0.28 + 0.06 * sx, -0.03, -0.35), (0.06, 0.01, 0.2), M(seal),
                       rot=Matrix.Rotation(0.3 * sx, 4, "Y")))
    return o


def license_card(n):
    o = [C.box(n + "_c", (0, 0, 0), (1.0, 0.04, 0.64), M("sky"), bevel=0.05),
         C.box(n + "_ph", (-0.28, -0.025, 0.02), (0.3, 0.01, 0.36), M("white"))]
    head = P.HEAD_C
    o.append(C.sphere(n + "_pg", (-0.28, -0.035, 0.02), (0.11, 0.01, 0.12), M("black"), 12, 6))
    o.append(C.sphere(n + "_pgf", (-0.28, -0.045, 0.0), (0.07, 0.01, 0.07), M("white"), 12, 6))
    for i in range(3):
        o.append(C.box(n + "_l%d" % i, (0.18, -0.03, 0.14 - i * 0.12), (0.45, 0.01, 0.04), M("navy")))
    o += emblem(n + "_w", "wing", 0.15, M("gold"), (0.3, -0.04, -0.22), 0.02)
    return o


def rosette(n, col="blue", em="star"):
    o = []
    for i in range(12):
        a = 2 * math.pi * i / 12
        o.append(C.sphere(n + "_p%d" % i, (0.28 * math.cos(a), 0.0, 0.28 * math.sin(a)), (0.13, 0.04, 0.13), M(col), 8, 5))
    o.append(C.cyl(n + "_c", (0, -0.02, 0), (0, -0.07, 0), 0.22, 0.22, M("gold"), 20))
    for i, sx in enumerate((-1, 1)):
        o.append(C.box(n + "_t%d" % i, (0.12 * sx, 0.03, -0.45), (0.16, 0.03, 0.55), M(col), rot=Matrix.Rotation(0.25 * sx, 4, "Y")))
    o += emblem(n + "_em", em, 0.13, M(col), (0, -0.09, 0), 0.03)
    return o


def pin(n):
    o = [C.cyl(n + "_d", (0, 0.03, 0), (0, -0.06, 0), 0.32, 0.3, M("teal"), 24)]
    o += emblem(n + "_g", "gear", 0.22, M("silver"), (0, -0.08, 0), 0.04)
    o.append(C.cyl(n + "_c", (0, -0.1, 0), (0, -0.13, 0), 0.07, 0.07, M("teal"), 12))
    return o


TROPHIES = {
    "BandaidBadge": lambda n: badge(n, "peach", "silver", "cross", "red", "circle"),
    "MedalofPain": lambda n: medal(n, "silver", ("red", "gun_dark"), "skull", "bone"),
    "OverkillTrophy": lambda n: cup(n, "gold", "gold_dark", "skull"),
    "TerraformerCertificate": lambda n: certificate(n, seal="green", em="triangle"),
    "UnderdogBadge": lambda n: badge(n, "brown", "gold", "heart", "red", "shield"),
    "ExplosivesExpertMedal": lambda n: medal(n, "gold", ("orange", "red"), "flame", "red"),
    "WeaponsExpertMedal": lambda n: medal(n, "silver", ("blue", "white"), "target", "red"),
    "EfficiencyTrophy": lambda n: cup(n, "silver", "steel", "bolt", "yellow"),
    "TrophyofWar": lambda n: cup(n, "gold", "gun_dark", "cross", "red"),
    "FlameBadge": lambda n: badge(n, "red", "gold", "flame", "yellow", "hex"),
    "EagleEyeBadge": lambda n: badge(n, "navy", "silver", "eye", "blue", "shield"),
    "MarineCertificate": lambda n: certificate(n, seal="blue", em="star"),
    "TrapMasterTrophy": lambda n: cup(n, "silver", "gun_dark", "gear", "gun"),
    "GrenadierMedal": lambda n: medal(n, "gold", ("olive", "olive_dark"), "circle", "olive"),
    "CreativityMedal": lambda n: medal(n, "gold", ("magenta", "cyan"), "star", "magenta", "star"),
    "PilotsLicense": license_card,
    "SharpshooterTrophy": lambda n: cup(n, "gold", "gold_dark", "target", "red"),
    "EliteTrophy": lambda n: cup(n, "purple", "purple_dark", "star", "gold"),
    "TrophyofWealth": lambda n: cup(n, "gold", "green_dark", "circle", "yellow"),
    "TrophyofVeteran": lambda n: cup(n, "silver", "navy", "star", "blue"),
    "RibbonofExpertise": lambda n: rosette(n, "blue", "star"),
    "MarkofAssassin": lambda n: badge(n, "gun_dark", "red", "skull", "bone", "hex"),
    "MedalofVeteran": lambda n: medal(n, "gold", ("navy", "red"), "star", "navy"),
    "PurpleHeart": lambda n: medal(n, "purple", ("purple", "white"), None, "gold", "heart"),
    "SnackTrophy": lambda n: cup(n, "orange", "orange_dark", "heart", "red"),
    "Pinofcrafting": pin,
    "TrophyofPerseverance": lambda n: cup(n, "steel", "gun", "diamond", "cyan"),
    "TelekinesisMedal": lambda n: medal(n, "violet", ("purple", "cyan"), "eye", "purple"),
    "IndomitableMedal": lambda n: medal(n, "steel", ("red", "gun_dark"), "cross", "red", "cross"),
    "InsanityMedal": lambda n: medal(n, "lime", ("magenta", "lime"), "bolt", "magenta", "star"),
    "ThreadsofFateMedal": lambda n: medal(n, "gold", ("red", "gold"), "drop", "red"),
    "TrophyoftheMaster": lambda n: cup(n, "gold", "purple_dark", "star", "purple"),
}


def trophy_ids():
    return [k for k in C.ids("Bonus") if C.config()["Bonus"][k].keys() and _is_trophy(k)]


def _is_trophy(k):
    import clothes
    return k in TROPHIES or (k not in clothes.all_items() and k[0].isupper() and any(
        w in k for w in ("Medal", "Trophy", "Badge", "Certificate", "License", "Ribbon", "Mark", "Heart", "Pin")))


# ------------------------------------------------------------------------------------------ emoticons

HC = P.HEAD_C
EYE = {1: HC + V((0.2, -0.555, 0.08)), -1: HC + V((-0.2, -0.555, 0.08))}


def eye(n, sx, kind):
    c = EYE[sx]
    w, p, b = M("eye"), M("pupil"), M("black")
    o = []
    if kind in ("open", "wide", "small", "half", "angry", "sad", "side", "up", "big"):
        sc = {"wide": 1.25, "big": 1.4, "small": 0.8}.get(kind, 1.0)
        o.append(C.sphere(n + "_w", c, (0.17 * sc, 0.085, 0.22 * sc), w, 14, 8))
        ps = {"wide": 0.55, "big": 1.1, "small": 0.9}.get(kind, 1.0)
        off = {"side": V((0.07, 0, 0)), "up": V((0, 0, 0.08)), "sad": V((0, 0, -0.04))}.get(kind, V((0, 0, -0.01)))
        pc = c + V((0, -0.065, 0)) + off
        o.append(C.sphere(n + "_p", pc, (0.09 * ps, 0.04, 0.12 * ps), p, 12, 6))
        o.append(C.sphere(n + "_h", pc + V((0.03, -0.04, 0.045)), (0.03, 0.012, 0.035), w, 8, 4))
        if kind == "half":
            o.append(C.box(n + "_lid", c + V((0, -0.06, 0.11)), (0.38, 0.1, 0.22), b))
        if kind == "angry":
            o.append(C.box(n + "_br", c + V((-0.02 * sx, -0.08, 0.27)), (0.32, 0.06, 0.08), M("red_dark"),
                           rot=Matrix.Rotation(math.radians(-25 * sx), 4, "Y")))
        if kind == "sad":
            o.append(C.box(n + "_br", c + V((0.0, -0.08, 0.28)), (0.3, 0.06, 0.07), M("blue_dark"),
                           rot=Matrix.Rotation(math.radians(20 * sx), 4, "Y")))
    if kind in ("happy", "closed", "flat", "x", "wink"):
        o.append(C.sphere(n + "_sock", c + V((0, 0.01, 0)), (0.17, 0.08, 0.21), w, 14, 8))
    if kind == "happy":
        o += arc(n + "_a", c + V((0, -0.1, -0.05)), 0.12, 0.045, 15, 165, b)
    elif kind == "closed":
        o += arc(n + "_a", c + V((0, -0.1, 0.07)), 0.12, 0.045, 195, 345, b)
    elif kind == "flat":
        o.append(C.box(n + "_l", c + V((0, -0.08, 0)), (0.24, 0.04, 0.05), b))
    elif kind == "x":
        for r in (45, -45):
            o.append(C.box(n + "_x%d" % r, c + V((0, -0.08, 0)), (0.26, 0.05, 0.06), b, rot=Matrix.Rotation(math.radians(r), 4, "Y")))
    elif kind == "spiral":
        o.append(C.sphere(n + "_w", c, (0.17, 0.085, 0.22), w, 14, 8))
        for i, r in enumerate((0.13, 0.08, 0.035)):
            o.append(C.torus(n + "_s%d" % i, c + V((0.01 * i, -0.08, 0)), r, 0.018, b, 16, 4, axis=(0, 1, 0)))
    elif kind == "wink":
        o += arc(n + "_a", c + V((0, -0.1, -0.03)), 0.11, 0.045, 15, 165, b)
    return o


def beak(n, kind="closed"):
    o = []
    org = HC + V((0, -0.52, -0.13))
    if kind == "closed":
        b = C.cyl(n + "_b", org, HC + V((0, -0.95, -0.17)), 0.17, 0.02, M("orange"), 10)
        b.data.transform(Matrix.Translation(org) @ Matrix.Diagonal((1, 1, 0.75, 1)) @ Matrix.Translation(-org))
        o.append(b)
    else:
        gap = {"open": 0.12, "wide": 0.25, "small": 0.06}.get(kind, 0.12)
        up = C.cyl(n + "_u", org + V((0, 0, 0.03)), HC + V((0, -0.92, -0.08 + gap * 0.3)), 0.15, 0.02, M("orange"), 10)
        lo = C.cyl(n + "_l", org + V((0, 0, -0.05)), HC + V((0, -0.85, -0.25 - gap)), 0.12, 0.02, M("orange_dark"), 10)
        o += [up, lo]
        o.append(C.sphere(n + "_m", org + V((0, -0.08, -0.08 - gap * 0.3)), (0.12, 0.05, 0.05 + gap * 0.4), M("red_dark"), 10, 6))
    return o


def emote_head(n, eyes=("open", "open"), mouth="closed", extras=()):
    o = []
    parts = P.build_head(prefix=n + "_", eyes=False)
    o += list(parts.values())
    o.remove(parts["Beak"])
    bpy.data.objects.remove(parts["Beak"])
    o += eye(n + "_eL", 1, eyes[0])
    o += eye(n + "_eR", -1, eyes[1])
    o += beak(n + "_bk", mouth)
    for e in extras:
        o += EXTRAS[e](n + "_x" + e)
    return o


def _tears(n):
    o = []
    for sx in (-1, 1):
        o.append(C.sphere(n + "%d" % sx, EYE[sx] + V((0.05 * sx, -0.09, -0.32)), (0.06, 0.04, 0.16), M("sky"), 10, 6))
        o.append(C.sphere(n + "d%d" % sx, EYE[sx] + V((0.12 * sx, -0.06, -0.55)), (0.05, 0.04, 0.07), M("sky"), 8, 5))
    return o


def _joy_tears(n):
    return [C.sphere(n + "%d" % sx, EYE[sx] + V((0.22 * sx, -0.02, 0.0)), (0.05, 0.04, 0.08), M("sky"), 8, 5) for sx in (-1, 1)]


def _sweat(n):
    return [C.sphere(n + "_d", HC + V((0.5, -0.35, 0.3)), (0.07, 0.04, 0.1), M("sky"), 10, 6)]


def _stars(n):
    o = []
    for i in range(3):
        a = math.radians(30 + i * 60)
        o.append(flat(n + "%d" % i, star_pts(0.1), 0.04, M("yellow"), HC + V((0.75 * math.cos(a), -0.2, 0.55 + 0.25 * math.sin(a)))))
    return o


def _steam(n):
    o = []
    for sx in (-1, 1):
        for i in range(2):
            o.append(C.sphere(n + "%d%d" % (sx, i), HC + V((0.6 * sx + 0.1 * sx * i, -0.1, 0.55 + 0.15 * i)), 0.08 - 0.02 * i,
                              M("white"), 8, 5))
    o.append(C.sphere(n + "_vein", HC + V((0.35, -0.48, 0.35)), (0.08, 0.03, 0.08), M("red"), 8, 5))
    return o


def _blush(n):
    return [C.sphere(n + "%d" % sx, HC + V((0.33 * sx, -0.52, -0.18)), (0.13, 0.03, 0.08), M("red"), 10, 5) for sx in (-1, 1)]


def _dots(n):
    return [C.sphere(n + "%d" % i, HC + V((0.45 + i * 0.16, -0.4, -0.45)), 0.05, M("gun_dark"), 8, 5) for i in range(3)]


def _tongue(n):
    return [C.sphere(n, HC + V((0.04, -0.75, -0.3)), (0.08, 0.06, 0.11), M("rose"), 10, 6)]


def _shades(n):
    o = []
    for sx in (-1, 1):
        o.append(C.box(n + "%d" % sx, EYE[sx] + V((0, -0.11, 0.0)), (0.3, 0.04, 0.2), M("gun_dark"), bevel=0.04))
    o.append(C.box(n + "_b", HC + V((0, -0.67, 0.08)), (0.2, 0.03, 0.04), M("gun_dark")))
    return o


def _flipper(n):
    rot = Matrix.Rotation(math.radians(70), 4, "Y")
    return [C.sphere(n, HC + V((0.05, -0.72, 0.12)), (0.14, 0.1, 0.5), M("black"), 12, 8, rot=rot)]


def _grin(n):
    o = arc(n + "_g", HC + V((0, -0.6, -0.05)), 0.4, 0.07, 200, 340, M("white"), segs=10)
    return o


def _question(n):
    return [text(n, "?!", 0.45, M("yellow"), HC + V((0.75, -0.2, 0.4)), 0.08)]


def _hat(n):
    o = [C.cyl(n + "_c", HC + V((0.1, 0, 0.5)), HC + V((0.2, 0, 1.05)), 0.25, 0.0, M("magenta"), 12)]
    o.append(C.sphere(n + "_p", HC + V((0.2, 0, 1.07)), 0.07, M("yellow"), 8, 5))
    return o


def _clock(n):
    c = HC + V((0.75, -0.2, 0.45))
    return [C.cyl(n + "_c", c, c + V((0, -0.05, 0)), 0.2, 0.2, M("white"), 16),
            C.torus(n + "_r", c, 0.2, 0.03, M("red"), 16, 4, axis=(0, 1, 0)),
            C.box(n + "_h", c + V((0, -0.06, 0.05)), (0.025, 0.02, 0.12), M("gun_dark")),
            C.box(n + "_m", c + V((0.04, -0.06, 0)), (0.1, 0.02, 0.025), M("gun_dark"))]


EXTRAS = {"tears": _tears, "joy": _joy_tears, "sweat": _sweat, "stars": _stars, "steam": _steam, "blush": _blush,
          "dots": _dots, "tongue": _tongue, "shades": _shades, "flipper": _flipper, "grin": _grin, "question": _question,
          "hat": _hat, "clock": _clock}

EMOTES = {
    "Laugh": (("happy", "happy"), "wide", ("joy",)),
    "Wow": (("wide", "wide"), "open", ()),
    "Srsly": (("half", "half"), "closed", ()),
    "Crying": (("sad", "sad"), "small", ("tears",)),
    "Dizzy": (("spiral", "spiral"), "small", ("stars",)),
    "Scream": (("wide", "wide"), "wide", ("sweat",)),
    "Ouch": (("x", "x"), "open", ()),
    "Phew": (("closed", "closed"), "small", ("sweat",)),
    "Woot": (("happy", "happy"), "open", ("hat",)),
    "Angry": (("angry", "angry"), "small", ("steam",)),
    "Waiting": (("side", "side"), "closed", ("clock",)),
    "Taunt": (("wink", "open"), "closed", ("tongue",)),
    "Wtf": (("big", "small"), "small", ("question",)),
    "Nice": (("open", "open"), "closed", ("shades",)),
    "Facepalm": (("closed", "closed"), "closed", ("flipper",)),
    "Trollface": (("happy", "happy"), "closed", ("grin", "blush")),
}


def emoticon(name):
    eyes, mouth, extras = EMOTES.get(name, (("open", "open"), "closed", ()))
    return lambda n: emote_head(n, eyes, mouth, extras)


# ------------------------------------------------------------------------------------------ slot, UI, HUD

def coin(n, r=0.45):
    o = [C.cyl(n + "_c", (0, 0.07, 0), (0, -0.07, 0), r, r, M("gold"), 28),
         C.torus(n + "_r", (0, -0.07, 0), r * 0.92, 0.035, M("gold_dark"), 28, 4, axis=(0, 1, 0))]
    o += emblem(n + "_s", "star", r * 0.55, M("yellow"), (0, -0.075, 0), 0.04)
    return o


def fish(n, col="cyan", col2="blue"):
    o = [C.sphere(n + "_b", (0, 0, 0), (0.45, 0.13, 0.26), M(col), 18, 10),
         C.sphere(n + "_belly", (0.05, -0.05, -0.08), (0.3, 0.09, 0.13), M("white"), 14, 8)]
    o.append(flat(n + "_t", [(0, 0), (-0.3, 0.25), (-0.22, 0), (-0.3, -0.25)], 0.06, M(col2), (-0.38, 0, 0)))
    o.append(flat(n + "_f", [(0, 0), (-0.2, 0.2), (0.1, 0.05)], 0.04, M(col2), (0.0, 0, 0.2)))
    o.append(C.sphere(n + "_e", (0.28, -0.1, 0.06), (0.06, 0.03, 0.06), M("white"), 8, 5))
    o.append(C.sphere(n + "_p", (0.29, -0.13, 0.06), (0.03, 0.02, 0.03), M("pupil"), 8, 5))
    return o


def xp_star(n):
    o = [flat(n + "_s", star_pts(0.5, 0.5), 0.16, M("blue"), (0, 0, 0), bevel=0.04)]
    o.append(text(n + "_t", "XP", 0.32, M("white"), (0, -0.1, -0.02), 0.06))
    return o


def lock(n):
    o = [C.box(n + "_b", (0, 0, -0.15), (0.62, 0.22, 0.5), M("gold"), bevel=0.06)]
    o += arc(n + "_sh", (0, 0, 0.1), 0.21, 0.06, 0, 180, M("silver"), segs=10)
    o.append(C.sphere(n + "_kh", (0, -0.12, -0.1), (0.06, 0.02, 0.06), M("gun_dark"), 8, 5))
    o.append(C.box(n + "_ks", (0, -0.12, -0.2), (0.04, 0.02, 0.12), M("gun_dark")))
    return o


def crown(n, label=True):
    base = V((0, 0, -0.1))
    o = [C.cyl(n + "_r", base, base + V((0, 0, 0.25)), 0.42, 0.45, M("gold"), 20)]
    for i in range(5):
        a = math.radians(-90 + (i - 2) * 30)
        p = base + V((0.45 * math.cos(a), 0.45 * math.sin(a), 0.25))
        o.append(C.cyl(n + "_s%d" % i, p, p + V((0, 0, 0.3)), 0.1, 0.0, M("gold"), 6))
        o.append(C.sphere(n + "_j%d" % i, p + V((0, 0, 0.32)), 0.05, M("gold"), 8, 5))
    for i in range(3):
        a = math.radians(-90 + (i - 1) * 35)
        o.append(C.sphere(n + "_g%d" % i, base + V((0.45 * math.cos(a), 0.45 * math.sin(a), 0.12)), 0.06,
                          M(("red", "blue", "green")[i]), 8, 5))
    if label:
        o.append(text(n + "_t", "VIP", 0.3, M("purple"), (0, -0.55, -0.32), 0.08))
    return o


def gift(n):
    o = [C.box(n + "_b", (0, 0, -0.1), (0.7, 0.7, 0.55), M("magenta"), bevel=0.03),
         C.box(n + "_l", (0, 0, 0.22), (0.8, 0.8, 0.14), M("magenta"), bevel=0.03),
         C.box(n + "_r1", (0, 0, 0.0), (0.16, 0.82, 0.82), M("yellow")),
         C.box(n + "_r2", (0, 0, 0.0), (0.82, 0.16, 0.82), M("yellow"))]
    for sx in (-1, 1):
        o.append(C.torus(n + "_bow%d" % sx, (0.14 * sx, 0, 0.38), 0.12, 0.04, M("yellow"), 12, 5, axis=(0, 1, 0.3), scale=(1.2, 1, 0.8)))
    return o


def gear(n):
    o = [flat(n + "_g", gear_pts(0.5, 8), 0.2, M("steel"), (0, 0, 0), bevel=0.03)]
    o.append(C.cyl(n + "_h", (0, -0.11, 0), (0, -0.12, 0), 0.17, 0.17, M("gun_dark"), 16))
    return o


def star_icon(n):
    return [flat(n + "_s", star_pts(0.5, 0.48), 0.18, M("yellow"), (0, 0, 0), bevel=0.05)]


def slot_machine(n):
    o = [C.box(n + "_b", (0, 0, 0), (0.8, 0.45, 0.9), M("red"), bevel=0.06),
         C.box(n + "_w", (0, -0.23, 0.08), (0.62, 0.02, 0.32), M("white"))]
    for i, c in enumerate(("lemon", "cherry", "gold")):
        o.append(C.sphere(n + "_s%d" % i, (-0.2 + 0.2 * i, -0.25, 0.08), (0.07, 0.02, 0.08), M({"cherry": "red"}.get(c, c)), 8, 5))
    o.append(C.box(n + "_t", (0, 0, 0.5), (0.86, 0.5, 0.12), M("gold"), bevel=0.03))
    o.append(C.cyl(n + "_lv", (0.42, 0, 0.0), (0.5, 0, 0.45), 0.025, 0.025, M("silver"), 8))
    o.append(C.sphere(n + "_lb", (0.5, 0, 0.47), 0.07, M("red"), 10, 6))
    o.append(C.box(n + "_tray", (0, -0.2, -0.36), (0.5, 0.12, 0.08), M("gold_dark")))
    return o


def podium(n):
    o = []
    for i, (x, h, c) in enumerate(((-0.42, 0.45, "silver"), (0.0, 0.7, "gold"), (0.42, 0.3, "orange_dark"))):
        o.append(C.box(n + "_p%d" % i, (x, 0, h / 2 - 0.4), (0.4, 0.4, h), M(c), bevel=0.03))
        o.append(text(n + "_n%d" % i, ("2", "1", "3")[i], 0.22, M("white"), (x, -0.21, h - 0.55), 0.04))
    o.append(flat(n + "_st", star_pts(0.14), 0.05, M("yellow"), (0.0, 0, 0.5)))
    return o


def bag(n):
    o = [C.box(n + "_b", (0, 0, -0.12), (0.7, 0.35, 0.7), M("orange"), bevel=0.06)]
    o += arc(n + "_h", (0, 0, 0.23), 0.2, 0.04, 0, 180, M("brown"), segs=8)
    o += emblem(n + "_e", "star", 0.16, M("white"), (0, -0.18, -0.1), 0.03)
    return o


def shirt(n):
    pts = [(-0.18, 0.42), (-0.48, 0.28), (-0.6, 0.0), (-0.4, -0.08), (-0.35, 0.08), (-0.35, -0.45), (0.35, -0.45), (0.35, 0.08),
           (0.4, -0.08), (0.6, 0.0), (0.48, 0.28), (0.18, 0.42), (0.0, 0.3)]
    o = [flat(n + "_s", pts, 0.12, M("red"), (0, 0, 0), bevel=0.03)]
    o.append(C.box(n + "_st", (0, -0.065, -0.1), (0.7, 0.02, 0.08), M("white")))
    o += arc(n + "_hg", (0, 0, 0.55), 0.09, 0.025, -60, 200, M("silver"))
    return o


def tools(n):
    o = [C.box(n + "_wh", (0, 0, 0), (0.1, 0.06, 0.9), M("steel"), rot=Matrix.Rotation(math.radians(40), 4, "Y"))]
    o.append(C.torus(n + "_wr", (0.3, 0, 0.36), 0.12, 0.05, M("steel"), 12, 5, axis=(0, 1, 0)))
    o.append(C.box(n + "_hh", (0, 0.05, 0), (0.08, 0.06, 0.9), M("wood"), rot=Matrix.Rotation(math.radians(-40), 4, "Y")))
    o.append(C.box(n + "_hd", (-0.27, 0.05, 0.3), (0.38, 0.14, 0.14), M("gun"), bevel=0.02,
                   rot=Matrix.Rotation(math.radians(-40), 4, "Y")))
    return o


def lightning_swords(n):
    o = [flat(n + "_b", bolt_pts(0.5), 0.12, M("yellow"), (0, 0, 0), bevel=0.02)]
    return o


def target(n):
    o = emblem(n + "_t", "target", 0.5, M("red"), (0, 0.05, 0), 0.08)
    o.append(C.cyl(n + "_a", (0.6, -0.4, 0.45), (0.05, -0.1, 0.03), 0.03, 0.03, M("wood"), 8))
    o.append(C.cyl(n + "_ah", (0.08, -0.12, 0.05), (-0.02, -0.06, -0.01), 0.07, 0.0, M("steel"), 8))
    return o


def sliders(n):
    o = []
    for i in range(3):
        z = 0.3 - i * 0.3
        o.append(C.box(n + "_t%d" % i, (0, 0, z), (0.9, 0.06, 0.07), M("grey_light"), bevel=0.02))
        o.append(C.cyl(n + "_k%d" % i, ((-0.25, 0.2, -0.05)[i], 0.0, z), ((-0.25, 0.2, -0.05)[i], -0.12, z), 0.1, 0.1,
                       M(("red", "green", "blue")[i]), 14))
    return o


def globe(n):
    o = [C.sphere(n + "_g", (0, 0, 0), 0.45, M("sky"), 20, 12)]
    rnd = random.Random(3)
    for i in range(5):
        a = rnd.uniform(-1.4, 1.4)
        b = rnd.uniform(-0.8, 0.8)
        p = V((0.44 * math.sin(a) * math.cos(b), -0.44 * math.cos(a) * math.cos(b), 0.44 * math.sin(b)))
        o.append(C.sphere(n + "_l%d" % i, p, (0.15, 0.06, 0.1), M("green"), 10, 6,
                          rot=V((0, -1, 0)).rotation_difference(p.normalized()).to_matrix().to_4x4()))
    o.append(C.torus(n + "_o", (0, 0, 0), 0.6, 0.03, M("yellow"), 24, 4, axis=(0.3, 0, 1)))
    return o


def book(n):
    o = [C.box(n + "_c", (0, 0, 0), (0.7, 0.18, 0.9), M("blue"), bevel=0.03),
         C.box(n + "_p", (0.03, 0, 0), (0.66, 0.14, 0.84), M("cream"))]
    o.append(text(n + "_q", "?", 0.5, M("yellow"), (0, -0.1, -0.02), 0.06))
    return o


def bundle(n, big=False):
    from props import crate_mesh
    o = crate_mesh(n + "_cr", size=0.75)
    for p in o:
        p.data.transform(Matrix.Translation((0, 0, -0.1)))
    items = [("rocket", (0.1, -0.1, 0.45)), ("grenade", (-0.32, -0.15, 0.4))]
    if big:
        items += [("nuke", (0.05, 0.2, 0.55)), ("grenade2", (0.42, -0.2, 0.35))]
    for kind, pos in items:
        if kind == "rocket":
            ps = S.rocket(n + "_r", length=0.8, r=0.08, stripe="yellow")
            m = Matrix.Translation(pos) @ Matrix.Rotation(0.6, 4, "Y")
        elif kind == "nuke":
            ps = S.bomb(n + "_n", length=0.7, r=0.16)
            m = Matrix.Translation(pos) @ Matrix.Rotation(-0.4, 4, "Y")
        else:
            ps = S.grenade(n + "_" + kind, "olive" if kind == "grenade" else "red", 0.14)
            m = Matrix.Translation(pos)
        for p in ps:
            p.data.transform(m)
        o += ps
    if big:
        o += coin(n + "_coin", 0.2)
        for p in o[-3:]:
            p.data.transform(Matrix.Translation((-0.45, -0.3, -0.25)))
    return o


def arrow(n, direction=1):
    pts = EMBLEM_SHAPES["arrow"](0.5)
    if direction < 0:
        pts = [(-x, y) for (x, y) in reversed(pts)]
    return [flat(n + "_a", pts, 0.16, M("white"), (0, 0, 0), bevel=0.04)]


def jump_icon(n):
    pts = [(y, x) for (x, y) in EMBLEM_SHAPES["arrow"](0.42)]
    o = [flat(n + "_a", pts, 0.16, M("white"), (0, 0, 0.12), bevel=0.04)]
    for i in range(3):
        o.append(C.box(n + "_s%d" % i, (0, 0, -0.32 - i * 0.08), (0.36 - i * 0.06, 0.08, 0.04), M("white"), bevel=0.015))
    return o


def fire_icon(n):
    o = [flat(n + "_f", star_pts(0.55, 0.55, 8), 0.1, M("orange"), (0, 0.02, 0), bevel=0.02),
         flat(n + "_f2", star_pts(0.35, 0.6, 8, 0.2), 0.1, M("yellow"), (0, -0.04, 0), bevel=0.02)]
    o.append(C.torus(n + "_cr", (0, -0.1, 0), 0.22, 0.035, M("white"), 20, 4, axis=(0, 1, 0)))
    for r in (0, 90, 180, 270):
        a = math.radians(r)
        o.append(C.box(n + "_t%d" % r, (0.3 * math.cos(a), -0.1, 0.3 * math.sin(a)), (0.16 if r % 180 == 0 else 0.05, 0.05,
                                                                                         0.05 if r % 180 == 0 else 0.16), M("white")))
    return o


def pause_icon(n):
    return [C.box(n + "_%d" % sx, (0.18 * sx, 0, 0), (0.18, 0.12, 0.7), M("white"), bevel=0.05) for sx in (-1, 1)]


def emote_icon(n):
    return emote_head(n, ("happy", "happy"), "open", ())


def app_penguin(n):
    """Penguin holding a bazooka, for the app icon."""
    o = []
    rig = P.build(None, with_sockets=False)
    # aim the right flipper forward and attach a bazooka
    import weapons
    fl = rig["FlipperR"]
    fl.data.transform(Matrix.Rotation(math.radians(80), 4, V((0, -1, 0))))
    hand = P.yaw_matrix() @ P.HAND_R
    sh = P.yaw_matrix() @ P.SHOULDER_R
    tip = sh + Matrix.Rotation(math.radians(80), 3, V((0, -1, 0))) @ (hand - sh)
    objs, mz = weapons.launcher(n + "_bz", "olive", "olive_dark", warhead=("red", "yellow"))
    for p in objs:
        p.data.transform(Matrix.Translation(tip + V((0.0, -0.3, -0.1))) @ Matrix.Rotation(math.radians(12), 4, "Y"))
    rig["Scarf"].data.materials[0] = M("red")
    for k, ob in rig.items():
        if ob.type == "MESH":
            me = ob.data
            me.transform(Matrix.Translation(ob.location))
            ob.location = (0, 0, 0)
            o.append(ob)
        else:
            bpy.data.objects.remove(ob)
    return o + objs


def ingredient(name):
    def scrap(n):
        o = []
        rnd = random.Random(4)
        for i in range(4):
            o.append(C.box(n + "_%d" % i, (rnd.uniform(-0.3, 0.3), 0, rnd.uniform(-0.25, 0.25)), (0.45, 0.06, 0.2), M("steel" if i % 2 else "metal_dark"),
                           bevel=0.02, rot=Matrix.Rotation(rnd.uniform(-1, 1), 4, "Y") @ Matrix.Rotation(rnd.uniform(-0.5, 0.5), 4, "X")))
        o.append(C.sphere(n + "_bolt", (0.1, -0.12, 0.1), (0.07, 0.04, 0.07), M("gun"), 6, 4))
        return o

    def gunpowder(n):
        o = [C.lathe(n + "_bag", [(0.0, -0.4), (0.38, -0.35), (0.42, -0.05), (0.25, 0.25), (0.12, 0.32), (0.18, 0.45), (0.0, 0.45)],
                     M("tan"), 16)]
        o.append(C.torus(n + "_tie", (0, 0, 0.3), 0.13, 0.03, M("red"), 12, 4))
        o += emblem(n + "_sk", "skull", 0.14, M("gun_dark"), (0, -0.4, -0.08), 0.03)
        return o

    def fishbones(n):
        o = [C.cyl(n + "_sp", (-0.4, 0, 0), (0.25, 0, 0), 0.03, 0.03, M("bone"), 8),
             C.sphere(n + "_h", (0.36, 0, 0), (0.16, 0.06, 0.14), M("bone"), 12, 6),
             C.sphere(n + "_e", (0.4, -0.06, 0.04), 0.03, M("pupil"), 6, 4),
             flat(n + "_t", [(0, 0), (-0.2, 0.18), (-0.15, 0), (-0.2, -0.18)], 0.04, M("bone"), (-0.38, 0, 0))]
        for i in range(5):
            x = -0.3 + 0.12 * i
            for sz in (-1, 1):
                o.append(C.cyl(n + "_r%d%d" % (i, sz), (x, 0, 0), (x - 0.05, 0, 0.2 * sz), 0.018, 0.012, M("bone"), 6))
        return o

    def iceshard(n):
        o = [C.cyl(n + "_c", (0, 0, -0.45), (0, 0, 0.45), 0.2, 0.0, M("ice_dark"), 6)]
        o.append(C.cyl(n + "_c2", (0.15, 0, -0.4), (0.3, 0, 0.1), 0.12, 0.0, M("ice"), 6))
        o.append(C.cyl(n + "_c3", (-0.12, 0, -0.42), (-0.28, 0, 0.0), 0.1, 0.0, M("glass"), 6))
        for ob in o:
            C.flat(ob)
        return o

    def duck(n):
        o = [C.sphere(n + "_b", (0, 0, 0), (0.4, 0.3, 0.27), M("yellow"), 16, 10),
             C.sphere(n + "_h", (0.22, 0, 0.33), 0.2, M("yellow"), 14, 8),
             C.sphere(n + "_bk", (0.43, 0, 0.29), (0.12, 0.08, 0.04), M("orange"), 10, 6),
             C.sphere(n + "_t", (-0.38, 0, 0.12), (0.1, 0.12, 0.12), M("yellow"), 8, 6),
             C.sphere(n + "_w", (-0.02, -0.25, 0.04), (0.2, 0.06, 0.13), M("gold"), 10, 6)]
        o.append(C.sphere(n + "_e", (0.3, -0.15, 0.4), (0.04, 0.02, 0.05), M("pupil"), 8, 5))
        return o

    def plasma(n):
        o = S.orb(n, "violet", 0.28, cage="steel")
        o.append(C.cyl(n + "_b", (0, 0, -0.45), (0, 0, -0.3), 0.25, 0.2, M("gun_dark"), 14))
        o.append(C.cyl(n + "_t", (0, 0, 0.3), (0, 0, 0.42), 0.12, 0.15, M("gun_dark"), 14))
        return o

    def feather(n):
        o = [C.cyl(n + "_q", (-0.45, 0, -0.35), (0.4, 0, 0.35), 0.02, 0.012, M("gold_dark"), 6)]
        for i in range(7):
            t = 0.2 + i * 0.1
            p = V((-0.45 + 0.85 * t, 0, -0.35 + 0.7 * t))
            for s in (-1, 1):
                o.append(C.sphere(n + "_v%d%d" % (i, s), p + V((-0.06 * s, 0, 0.08 * s)), (0.16 - abs(i - 3) * 0.02, 0.02, 0.06),
                                  M("gold" if s > 0 else "yellow"), 8, 4, rot=Matrix.Rotation(math.radians(-40 + 45 * s), 4, "Y")))
        return o

    def uranium(n):
        o = S.rock(n, 0.35, 12, "lime")
        o.append(C.ico(n + "_g", (0.1, -0.15, 0.1), 0.12, M("green"), subdiv=1, seed=13, jitter=0.2))
        return o

    return {"ScrapMetal": scrap, "Gunpowder": gunpowder, "FishBones": fishbones, "IceShard": iceshard,
            "RubberDuck": duck, "PlasmaCore": plasma, "GoldenFeather": feather, "UraniumPebble": uranium}.get(name)


def crate_icon(n):
    from props import crate_mesh
    return crate_mesh(n, 0.9)


def trophy_icon(n):
    return cup(n, "gold", "gold_dark", "star", "red")


UI = {
    "coin": coin, "cash": fish, "xp": xp_star, "lock": lock, "vip": crown, "gift": gift, "settings": gear,
    "star": star_icon, "trophy": trophy_icon, "crate": crate_icon, "slot": slot_machine, "leaderboard": podium,
    "shop": bag, "wardrobe": shirt, "crafting": tools, "quickmatch": lightning_swords, "practice": target,
    "custom": sliders, "online": globe, "tutorial": book, "StarterBundle": lambda n: bundle(n, False),
    "MidBundle": lambda n: bundle(n, True),
}

SLOT = {
    "Lemon": lambda n: S.lemon(n, 0.3)[:3],
    "Ammo": lambda n: [p for i in range(3) for p in _ammo(n, i)],
    "Xp": xp_star,
    "Coin": coin,
    "Bolt": lambda n: [flat(n + "_b", bolt_pts(0.5), 0.14, M("yellow"), (0, 0, 0), bevel=0.03)],
    "Cash": fish,
}


def _ammo(n, i):
    p = S.bullet(n + "_%d" % i, "gold", 0.8, 0.11)
    for o in p:
        o.data.transform(Matrix.Translation((-0.25 + 0.25 * i, -0.05 * (i == 1), 0)) @ Matrix.Rotation(-math.pi / 2, 4, "Y"))
    return p


HUD = {
    "WalkLeft": lambda n: arrow(n, -1),
    "WalkRight": lambda n: arrow(n, 1),
    "Jump": jump_icon,
    "Fire": fire_icon,
    "Pause": pause_icon,
    "Emote": emote_icon,
}

# achievements: group emblem + tier color (1..5)
ACH_TIER = ["orange_dark", "silver", "gold", "cyan", "violet"]
ACH_GROUP = {
    "Quack": "heart", "Victory": "star", "Rematch": "arrow", "Levels": "triangle", "Weapons": "target",
    "Destroyice": "diamond", "Destroywood": "gear", "Destroystone": "circle", "Weaponwin_grenade": "flame",
    "Weaponwin_bazooka": "bolt", "Weaponwin_impactcannon": "drop", "Weaponwin_pistol": "target",
    "Weaponwin_shotgun": "cross", "Using_Ammo": "bolt", "Earn_coins_matches": "circle",
    "Complete_x_turns_in_minutes": "gear",
}


def achievement(aid):
    import re
    m = re.match(r"(.*?)(\d+)$", aid)
    group, tier = (m.group(1), int(m.group(2))) if m else (aid, 1)
    em = ACH_GROUP.get(group, "star")
    col = ACH_TIER[(tier - 1) % 5]
    shape = "circle" if tier < 5 else "star"

    def build(n):
        o = medal(n, col, ("blue", "white") if tier < 3 else ("red", "gold"), em, "white" if tier >= 3 else "navy", shape)
        return o
    return build
