"""Background environment models for the parallax layers: Models/Env/{Theme}_{Name}.fbx.
Origin at the bottom center, chunky low-poly shapes that read well as silhouettes.
Terrain/LevelBackground sorts them by name: cloud/sun/moon -> sky layer, peak/hill/mesa/dune/berg -> far layer,
everything else -> near layer."""
import bpy  # noqa: F401
import math
import os
import random

from mathutils import Matrix, Vector

import common as C

M = C.mat
V = Vector


def blob(n, c, r, mat, seed, scale=(1, 0.7, 1), jitter=0.08, subdiv=2):
    return C.ico(n, c, r, M(mat), subdiv=subdiv, seed=seed, jitter=jitter, smooth=True, scale=scale)


def round_tree(n, h=4.0, seed=1, leaf="leaf", leaf2="leaf_dark"):
    rnd = random.Random(seed)
    o = [C.cyl(n + "_t", (0, 0, 0), (0, 0, h * 0.55), h * 0.07, h * 0.045, M("bark"), 8),
         C.cyl(n + "_b1", (0, 0, h * 0.35), (h * 0.18, 0, h * 0.55), h * 0.03, h * 0.02, M("bark"), 6)]
    for i in range(5):
        a = i * 2 * math.pi / 5 + rnd.uniform(-0.3, 0.3)
        c = (math.cos(a) * h * 0.2, math.sin(a) * h * 0.08, h * (0.68 + 0.08 * math.sin(a)))
        o.append(blob(n + "_l%d" % i, c, h * rnd.uniform(0.2, 0.26), leaf if i % 2 else leaf2, seed * 10 + i))
    o.append(blob(n + "_lt", (0, 0, h * 0.85), h * 0.25, leaf, seed * 10 + 9))
    return o


def tall_tree(n, h=5.0, seed=2):
    o = [C.cyl(n + "_t", (0, 0, 0), (0, 0, h * 0.5), h * 0.05, h * 0.035, M("bark"), 8)]
    o.append(blob(n + "_c", (0, 0, h * 0.68), h * 0.22, "leaf_light", seed, scale=(0.8, 0.6, 1.45)))
    o.append(blob(n + "_c2", (h * 0.06, -h * 0.05, h * 0.55), h * 0.14, "leaf", seed + 1, scale=(1, 0.6, 1.2)))
    return o


def pine(n, h=5.0, snow=False, seed=3, col="pine", col2="pine_dark"):
    o = [C.cyl(n + "_t", (0, 0, 0), (0, 0, h * 0.25), h * 0.05, h * 0.04, M("bark"), 8)]
    for i in range(4):
        z0 = h * (0.15 + i * 0.19)
        r = h * (0.32 - i * 0.065)
        o.append(C.cyl(n + "_c%d" % i, (0, 0, z0), (0, 0, z0 + h * 0.32), r, 0.0, M(col if i % 2 == 0 else col2), 9))
        if snow:
            o.append(C.cyl(n + "_s%d" % i, (0, 0, z0 + h * 0.17), (0, 0, z0 + h * 0.33), r * 0.5, 0.0, M("snow"), 9))
    return o


def bush(n, w=2.5, seed=4):
    rnd = random.Random(seed)
    o = []
    for i in range(4):
        x = (i - 1.5) * w * 0.22
        o.append(blob(n + "_b%d" % i, (x, 0, w * 0.18), w * rnd.uniform(0.22, 0.3), "leaf" if i % 2 else "leaf_dark", seed + i))
    for i in range(3):
        o.append(C.sphere(n + "_f%d" % i, ((i - 1) * w * 0.25, -w * 0.25, w * 0.32 + 0.1 * i), w * 0.04, M("rose"), 8, 5))
    return o


def hills(n, w=16.0, h=4.0, cols=("hill", "hill_dark"), seed=5, snow=False):
    rnd = random.Random(seed)
    o = []
    for i in range(3):
        x = (i - 1) * w * 0.3 + rnd.uniform(-1, 1)
        r = w * rnd.uniform(0.22, 0.3)
        hh = h * rnd.uniform(0.7, 1.0)
        ob = C.sphere(n + "_h%d" % i, (x, i * 0.3, 0), (r, r * 0.4, hh), M(cols[i % 2]), 20, 10)
        o.append(ob)
        if snow:
            o.append(C.sphere(n + "_s%d" % i, (x, i * 0.3 - 0.05, hh * 0.72), (r * 0.55, r * 0.3, hh * 0.32), M("snow"), 16, 8))
    return _cut_below(o)


def _cut_below(objs, z=0.0):
    import bmesh
    for ob in objs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        for v in bm.verts:
            if v.co.z < z:
                v.co.z = z
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        bm.to_mesh(ob.data)
        bm.free()
    return objs


def peak(n, h=10.0, w=12.0, seed=6, cols=("mountain", "mountain_dark"), snow=True):
    rnd = random.Random(seed)
    import bmesh
    bm = bmesh.new()
    pts = [(-w / 2, 0)]
    k = 7
    for i in range(1, k):
        t = i / k
        x = -w / 2 + w * t
        base = (1 - abs(t - 0.5) * 2) * h
        pts.append((x, max(0.5, base + rnd.uniform(-h * 0.08, h * 0.08))))
    pts.append((w / 2, 0))
    o = [C.extrude(n + "_m", pts, w * 0.25, M(cols[0]), bevel=0.0)]
    # darker side face
    top = max(pts, key=lambda p: p[1])
    side = [top, (w / 2, 0), (top[0] + w * 0.1, 0)]
    o.append(C.extrude(n + "_sd", side, w * 0.25 + 0.05, M(cols[1])))
    if snow:
        sn = [(top[0] - h * 0.18, top[1] * 0.72), (top[0], top[1] + 0.05), (top[0] + h * 0.18, top[1] * 0.72),
              (top[0] + h * 0.07, top[1] * 0.78), (top[0] - h * 0.02, top[1] * 0.7), (top[0] - h * 0.1, top[1] * 0.77)]
        o.append(C.extrude(n + "_sn", sn, w * 0.25 + 0.1, M("snow")))
    for ob in o:
        C.flat(ob)
    return o


def rock(n, s=2.0, seed=7, mat="rock"):
    return [C.ico(n + "_r", (0, 0, s * 0.35), s * 0.5, M(mat), subdiv=1, seed=seed, jitter=0.18, scale=(1.3, 0.8, 0.85)),
            C.ico(n + "_r2", (s * 0.45, -0.1, s * 0.2), s * 0.28, M("rock_dark"), subdiv=1, seed=seed + 1, jitter=0.2)]


def snowman(n, h=3.0):
    o = [C.sphere(n + "_b", (0, 0, h * 0.2), h * 0.22, M("snow"), 16, 10),
         C.sphere(n + "_m", (0, 0, h * 0.52), h * 0.16, M("snow"), 16, 10),
         C.sphere(n + "_h", (0, 0, h * 0.77), h * 0.12, M("snow"), 14, 8),
         C.cyl(n + "_n", (0, -h * 0.11, h * 0.77), (0, -h * 0.25, h * 0.75), h * 0.025, 0.0, M("orange"), 8),
         C.cyl(n + "_hat", (0, 0, h * 0.86), (0, 0, h * 1.0), h * 0.08, h * 0.08, M("gun_dark"), 12),
         C.cyl(n + "_hb", (0, 0, h * 0.86), (0, 0, h * 0.87), h * 0.13, h * 0.13, M("gun_dark"), 14),
         C.torus(n + "_sc", (0, 0, h * 0.66), h * 0.11, h * 0.03, M("red"), 14, 6)]
    for sx in (-1, 1):
        o.append(C.sphere(n + "_e%d" % sx, (sx * h * 0.04, -h * 0.105, h * 0.8), h * 0.015, M("pupil"), 6, 4))
        o.append(C.cyl(n + "_a%d" % sx, (sx * h * 0.14, 0, h * 0.55), (sx * h * 0.36, 0, h * 0.68), h * 0.012, h * 0.008, M("bark"), 5))
    for i in range(3):
        o.append(C.sphere(n + "_bt%d" % i, (0, -h * 0.155, h * (0.45 + 0.07 * i)), h * 0.015, M("pupil"), 6, 4))
    return o


def iceberg(n, w=10.0, h=5.0, seed=8):
    rnd = random.Random(seed)
    pts = [(-w / 2, 0)]
    for i in range(1, 6):
        t = i / 6
        pts.append((-w / 2 + w * t, h * (0.55 + rnd.uniform(-0.15, 0.45)) * (1 - abs(t - 0.5))))
    pts.append((w / 2, 0))
    o = [C.extrude(n + "_i", pts, w * 0.3, M("ice"), bevel=0.15)]
    o.append(C.box(n + "_w", (0, 0, 0.1), (w * 1.05, w * 0.32, 0.2), M("ice_dark")))
    for ob in o:
        C.flat(ob)
    return o


def cactus(n, h=3.5, arms=2, seed=9):
    rnd = random.Random(seed)
    o = [C.cyl(n + "_t", (0, 0, 0), (0, 0, h * 0.85), h * 0.1, h * 0.1, M("cactus"), 10),
         C.sphere(n + "_tt", (0, 0, h * 0.85), h * 0.1, M("cactus"), 10, 6)]
    for i in range(arms):
        sx = 1 if i % 2 == 0 else -1
        z = h * rnd.uniform(0.35, 0.55)
        x = sx * h * 0.25
        o.append(C.cyl(n + "_ah%d" % i, (0, 0, z), (x, 0, z), h * 0.06, h * 0.06, M("cactus_dark"), 8))
        o.append(C.cyl(n + "_av%d" % i, (x, 0, z), (x, 0, z + h * 0.25), h * 0.06, h * 0.06, M("cactus_dark"), 8))
        o.append(C.sphere(n + "_at%d" % i, (x, 0, z + h * 0.25), h * 0.06, M("cactus_dark"), 8, 5))
        o.append(C.sphere(n + "_aj%d" % i, (x, 0, z), h * 0.06, M("cactus_dark"), 8, 5))
    o.append(C.sphere(n + "_fl", (0, -h * 0.02, h * 0.95), h * 0.05, M("rose"), 8, 5))
    return o


def mesa(n, w=14.0, h=6.0):
    pts = [(-w / 2, 0), (-w * 0.36, h * 0.85), (-w * 0.3, h), (w * 0.25, h), (w * 0.32, h * 0.8), (w * 0.36, h * 0.55),
           (w / 2, 0)]
    o = [C.extrude(n + "_m", pts, w * 0.3, M("mesa"))]
    for i, z in enumerate((0.3, 0.62)):
        o.append(C.box(n + "_s%d" % i, (0, -w * 0.15 - 0.03, h * z), (w * 0.75, 0.05, h * 0.06), M("mesa_dark")))
    for ob in o:
        C.flat(ob)
    return o


def dune(n, w=16.0, h=3.0):
    return _cut_below([C.sphere(n + "_d", (0, 0, 0), (w * 0.5, w * 0.15, h), M("sand"), 20, 10),
                       C.sphere(n + "_d2", (w * 0.25, 0.4, 0), (w * 0.3, w * 0.12, h * 0.75), M("sand_dark"), 16, 8)])


def palm(n, h=5.0):
    o = []
    pts = []
    for i in range(6):
        t = i / 5
        pts.append(V((math.sin(t * 1.2) * h * 0.25, 0, t * h * 0.85)))
    for i in range(5):
        o.append(C.cyl(n + "_t%d" % i, pts[i], pts[i + 1], h * (0.06 - i * 0.006), h * (0.054 - i * 0.006),
                       M("bark" if i % 2 else "brown"), 8))
    top = pts[-1]
    for i in range(6):
        a = i * math.pi * 2 / 6
        d = V((math.cos(a), math.sin(a) * 0.4, 0))
        tip = top + d * h * 0.42 + V((0, 0, -h * 0.18))
        mid = top + d * h * 0.22 + V((0, 0, h * 0.06))
        o.append(C.sphere(n + "_f%d" % i, (top + mid) / 2 * 0.5 + (mid + tip) / 2 * 0.5, (h * 0.22, h * 0.07, h * 0.035),
                          M("leaf" if i % 2 else "leaf_dark"), 10, 6,
                          rot=V((1, 0, 0)).rotation_difference((tip - top).normalized()).to_matrix().to_4x4()))
    for i in range(3):
        o.append(C.sphere(n + "_co%d" % i, top + V((math.cos(i * 2.1) * h * 0.05, -h * 0.03, -h * 0.05)), h * 0.045,
                          M("brown_dark"), 8, 5))
    return o


def cloud(n, w=5.0, seed=10):
    rnd = random.Random(seed)
    o = []
    for i in range(5):
        x = (i - 2) * w * 0.17
        r = w * (0.22 if i in (1, 2, 3) else 0.15) * rnd.uniform(0.85, 1.15)
        o.append(C.sphere(n + "_c%d" % i, (x, 0, r * 0.6 + (0.2 if i == 2 else 0)), (r, r * 0.6, r), M("white"), 14, 8))
    o.append(C.sphere(n + "_base", (0, 0, w * 0.06), (w * 0.45, w * 0.12, w * 0.07), M("white"), 16, 8))
    return o


def sun(n, r=2.0):
    o = [C.sphere(n + "_s", (0, 0, r), r, M("yellow"), 20, 12)]
    for i in range(10):
        a = i * 2 * math.pi / 10
        o.append(C.cyl(n + "_r%d" % i, (math.cos(a) * r * 1.15, 0.2, r + math.sin(a) * r * 1.15),
                       (math.cos(a) * r * 1.55, 0.2, r + math.sin(a) * r * 1.55), r * 0.12, 0.0, M("gold"), 6))
    return o


ENV = {
    "Forest_Tree1": lambda n: round_tree(n, 4.5, 1),
    "Forest_Tree2": lambda n: tall_tree(n, 5.5, 2),
    "Forest_Tree3": lambda n: pine(n, 5.5, False, 3, "leaf_dark", "pine"),
    "Forest_Bush1": lambda n: bush(n, 2.6, 4),
    "Forest_Hills1": lambda n: hills(n, 16, 4.5, ("hill", "hill_dark"), 5),
    "Forest_Cloud1": lambda n: cloud(n, 5, 10),
    "Winter_PineSnow1": lambda n: pine(n, 5.5, True, 11),
    "Winter_PineSnow2": lambda n: pine(n, 4.0, True, 12, "pine_dark", "pine"),
    "Winter_Snowman1": lambda n: snowman(n, 3.0),
    "Winter_Iceberg1": lambda n: iceberg(n, 10, 5, 13),
    "Winter_Hills1": lambda n: hills(n, 16, 4, ("snow", "ice"), 14),
    "Winter_Cloud1": lambda n: cloud(n, 5, 15),
    "Mountain_Peak1": lambda n: peak(n, 10, 12, 16),
    "Mountain_Peak2": lambda n: peak(n, 7, 11, 17, ("rock", "rock_dark")),
    "Mountain_Rock1": lambda n: rock(n, 2.2, 18),
    "Mountain_Pine1": lambda n: pine(n, 5.0, False, 19),
    "Mountain_Cloud1": lambda n: cloud(n, 6, 20),
    "Desert_Cactus1": lambda n: cactus(n, 3.5, 2, 21),
    "Desert_Cactus2": lambda n: cactus(n, 2.6, 1, 22),
    "Desert_Mesa1": lambda n: mesa(n, 14, 6),
    "Desert_Dune1": lambda n: dune(n, 16, 3),
    "Desert_Palm1": lambda n: palm(n, 5.5),
    "Desert_Rock1": lambda n: rock(n, 2.0, 23, "sand_dark"),
    "Desert_Sun1": lambda n: sun(n, 2.0),
}


def build(name, col):
    C.use_collection(col)
    ob = C.join(ENV[name](name), name)
    C.use_collection(None)
    return ob


def run():
    C.reset()
    cols = {}
    stats = {}
    for name in ENV:
        col = C.new_collection("E_" + name)
        ob = build(name, col)
        stats[name] = C.tri_count([ob])
        C.export_collection(col, os.path.join(C.MODELS, "Env", name + ".fbx"))
        cols[name] = col
    C.save_blend("env")
    print("[env] %d models, max tris %d" % (len(stats), max(stats.values())))
    return cols
