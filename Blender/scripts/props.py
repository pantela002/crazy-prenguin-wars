"""Level objects (LevelObject ids) built from the original physics shapes
(cpw-server/assets/dynamicobjects/{Material}.xml), plus pickups (crates, mines, coin).

Convention: the physics polygons are in Flash pixels with y pointing DOWN (Nape). Models use
Unity x = px / 20, Unity y = -py / 20 (so a PolygonCollider2D built the same way matches the mesh).
The model origin is the physics body origin. Depth (Unity z) is centered on 0.
custom_object_tree is not used by any shipped map; it is modeled as an upright tree filling its shape bounds.
"""
import bpy  # noqa: F401
import math
import os
import re

from mathutils import Matrix, Vector

import common as C
import shapes as S

M = C.mat
V = Vector
# copies of the original physics files (cpw-server/assets/dynamicobjects) kept next to the scripts
XML_DIR = os.path.join(C.ROOT, "Blender", "data", "dynamicobjects")
PX = 20.0

MATERIALS = {
    "Wood": ("wood", "wood_dark"),
    "Stone": ("stone", "stone_dark"),
    "Ice": ("ice", "ice_dark"),
    "Metal": ("metal", "metal_dark"),
}
SHAPE_BODY = {"BallSmall": "ball_small", "BallMedium": "ball_medium", "CubeSmall": "cube_small",
              "CubeMedium": "cube_medium", "CubeLarge": "cube_big", "TriangleSmall": "triangle_small",
              "TriangleMedium": "triangle_medium", "RectangleLarge": "rectangle_large",
              "RectangleMedium": "rectangle_medium", "PlankLarge": "plank_large", "PlankMedium": "plank_medium",
              "PlankSmall": "plank_small"}

# fallback shapes (pixels) used when the original XML files are not available
FALLBACK = {"ball_small": ("C", 13.0), "ball_medium": ("C", 26.0), "cube_small": ("B", 24, 24), "cube_medium": ("B", 50, 50),
            "cube_big": ("B", 98, 98), "triangle_small": ("T", 26, 27), "triangle_medium": ("T", 52, 53),
            "rectangle_large": ("B", 192, 51), "rectangle_medium": ("B", 98, 51), "plank_large": ("B", 399, 28),
            "plank_medium": ("B", 192, 28), "plank_small": ("B", 147, 28)}


def load_bodies(material):
    path = os.path.join(XML_DIR, material + ".xml")
    bodies = {}
    if not os.path.exists(path):
        return bodies
    s = open(path, encoding="utf-8").read()
    for m in re.finditer(r'<body name="([^"]+)">(.*?)</body>', s, re.S):
        name, b = m.groups()
        circ = re.search(r'<circle r="([-\d.]+)" x="([-\d.]+)" y="([-\d.]+)"', b)
        polys = [[float(t) for t in re.split(r"[,\s]+", p.strip()) if t] for p in re.findall(r"<polygon>(.*?)</polygon>", b, re.S)]
        bodies[name] = {"circle": tuple(map(float, circ.groups())) if circ else None,
                        "polys": [list(zip(p[0::2], p[1::2])) for p in polys]}
    return bodies


def to_game(pts):
    """pixel (x, y-down) -> game frame (x, z) in units."""
    return [(x / PX, -y / PX) for (x, y) in pts]


def body_shape(material, body):
    b = load_bodies(material).get(body)
    if b is None:
        f = FALLBACK[body]
        if f[0] == "C":
            return {"circle": (f[1], 0.0, 0.0), "polys": []}
        w, h = f[1] / 2, f[2] / 2
        if f[0] == "B":
            return {"circle": None, "polys": [[(-w, -h), (w, -h), (w, h), (-w, h)]]}
        return {"circle": None, "polys": [[(-w, h), (w, h), (w, -h)]]}
    return b


def depth_for(w, h):
    return max(0.9, min(2.2, min(w, h)))


def _ensure_two_mats(ob, face, rim):
    me = ob.data
    me.materials.clear()
    me.materials.append(M(face))
    me.materials.append(M(rim))


def slab(name, pts, depth, face, rim, bevel):
    ob = C.extrude(name, pts, depth, M(face))
    _ensure_two_mats(ob, face, rim)
    mod = ob.modifiers.new("Bevel", "BEVEL")
    mod.width = bevel
    mod.segments = 2
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(30)
    mod.material = 1
    C.apply_modifiers(ob)
    C.smooth(ob, 35)
    return ob


def plate(name, x0, z0, x1, z1, fy, mat, t=0.04, rot=0.0, tilt=None):
    """Thin box on the front face (fy = front face y) from (x0, z0) to (x1, z1); its own outline reads as a groove.
    tilt = (about x, about z) radians tips the plate so each block catches the light differently."""
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    r = Matrix.Rotation(rot, 4, "Y") if rot else None
    if tilt:
        r = Matrix.Rotation(tilt[0], 4, "X") @ Matrix.Rotation(tilt[1], 4, "Z") @ (r or Matrix.Identity(4))
    return C.box(name, (cx, fy - t / 2 + 0.005, cz), (abs(x1 - x0), t, abs(z1 - z0)), mat, rot=r)


def nail(name, x, z, fy, mat="gun", r=0.055):
    return C.sphere(name, (x, fy, z), (r, r * 0.5, r), M(mat), 6, 3)


def _wood_box(name, w, h, cx, cz, fy, rnd):
    o = []
    long_x = w >= h
    L, S = (w, h) if long_x else (h, w)
    m = min(0.14, S * 0.12)
    n = max(1, int(round((S - 2 * m) / 0.5)))
    bw = (S - 2 * m) / n
    gap = min(0.05, bw * 0.12)
    cols = ["wood", "wood_light", "wood_mid"]
    for i in range(n):
        s0 = -S / 2 + m + i * bw + gap / 2
        s1 = s0 + bw - gap
        c = cols[(i + rnd.randint(0, 2)) % 3]
        if long_x:
            o.append(plate(name + "_bd%d" % i, cx - L / 2 + m, cz + s0, cx + L / 2 - m, cz + s1, fy, M(c), 0.04))
        else:
            o.append(plate(name + "_bd%d" % i, cx + s0, cz - L / 2 + m, cx + s1, cz + L / 2 - m, fy, M(c), 0.04))
        # grain: a few thin darker streaks of random length along the board
        for g in range(2 if L > 1.5 else 1):
            gl = rnd.uniform(0.25, 0.6) * (L - 2 * m)
            ga = rnd.uniform(-L / 2 + m, L / 2 - m - gl)
            gs = rnd.uniform(s0 + (s1 - s0) * 0.25, s0 + (s1 - s0) * 0.75)
            if long_x:
                o.append(plate(name + "_gr%d%d" % (i, g), cx + ga, cz + gs - 0.012, cx + ga + gl, cz + gs + 0.012, fy - 0.035,
                               M("wood_dark"), 0.012))
            else:
                o.append(plate(name + "_gr%d%d" % (i, g), cx + gs - 0.012, cz + ga, cx + gs + 0.012, cz + ga + gl, fy - 0.035,
                               M("wood_dark"), 0.012))
        # nails at the board ends (and every ~4 units on long planks)
        k = max(1, int(L / 4))
        for j in range(k + 1):
            a = -L / 2 + m + 0.15 + (L - 2 * m - 0.3) * j / k
            b = (s0 + s1) / 2
            x, z = (cx + a, cz + b) if long_x else (cx + b, cz + a)
            o.append(nail(name + "_n%d%d" % (i, j), x, z, fy - 0.04))
    return o


def _stone_box(name, w, h, cx, cz, fy, rnd):
    """Rows of staggered stone blocks; each block's ink outline doubles as the mortar line."""
    o = []
    m = min(0.16, min(w, h) * 0.1)
    rows = max(1, int(round((h - 2 * m) / 0.85)))
    rh = (h - 2 * m) / rows
    gap = min(0.07, rh * 0.1)
    cols = ["stone", "stone_light", "stone_mid", "stone"]
    k = 0
    for r in range(rows):
        z0 = cz - h / 2 + m + r * rh + gap / 2
        z1 = z0 + rh - gap
        x = cx - w / 2 + m + (rnd.uniform(0.2, 0.6) if r % 2 else 0.0)
        xs = [cx - w / 2 + m]
        if r % 2:
            xs.append(x)
        while True:
            x += rnd.uniform(0.9, 1.6) * max(0.6, rh)
            if x > cx + w / 2 - m - 0.4:
                break
            xs.append(x)
        xs.append(cx + w / 2 - m)
        for a, b in zip(xs, xs[1:]):
            if b - a < 0.15:
                continue
            ta = min(0.12, 0.05 / max(0.3, rh)), min(0.12, 0.05 / max(0.3, b - a))
            o.append(plate(name + "_st%d" % k, a + gap / 2, z0, b - gap / 2, z1, fy - 0.02, M(cols[rnd.randint(0, 3)]),
                           rnd.uniform(0.06, 0.09), tilt=(rnd.uniform(-ta[0], ta[0]), rnd.uniform(-ta[1], ta[1]))))
            k += 1
    # a crack across one block
    if w > 1 and h > 1:
        px, pz = cx + rnd.uniform(-w, w) * 0.2, cz + rnd.uniform(-h, h) * 0.2
        for i in range(2):
            o.append(C.box(name + "_c%d" % i, (px + i * 0.12, fy - 0.08, pz - i * 0.1), (min(w, h) * 0.22, 0.02, 0.035),
                           M("stone_dark"), rot=Matrix.Rotation(rnd.uniform(-0.9, 0.9), 4, "Y")))
    return o


def _metal_box(name, w, h, cx, cz, fy, rnd):
    o = []
    s = min(w, h)
    c = min(0.55, s * 0.32)          # corner bracket size
    t = min(0.16, s * 0.1)           # bracket arm width
    # raised center panel
    pm = min(0.3, s * 0.18)
    o.append(plate(name + "_pn", cx - w / 2 + pm, cz - h / 2 + pm, cx + w / 2 - pm, cz + h / 2 - pm, fy, M("metal_light"), 0.03))
    if w > 2.5 * h:     # long girders: vertical ribs every ~2 units
        n = int(w / 2)
        for i in range(1, n):
            x = cx - w / 2 + w * i / n
            o.append(plate(name + "_rb%d" % i, x - 0.05, cz - h / 2 + pm, x + 0.05, cz + h / 2 - pm, fy - 0.03, M("metal_dark"), 0.03))
    elif s > 1.6:       # cross brace on big boxes
        ln = math.hypot(w - 2 * pm, h - 2 * pm)
        ang = math.atan2(h - 2 * pm, w - 2 * pm)
        for sg in (-1, 1):
            o.append(C.box(name + "_x%d" % sg, (cx, fy - 0.045, cz), (ln * 0.92, 0.03, t * 0.7), M("metal_dark"),
                           rot=Matrix.Rotation(sg * ang, 4, "Y")))
    for sx in (-1, 1):
        for sz in (-1, 1):
            x0, z0 = cx + sx * w / 2, cz + sz * h / 2
            pts = [(x0, z0), (x0 - sx * c, z0), (x0 - sx * c, z0 - sz * t), (x0 - sx * t, z0 - sz * t),
                   (x0 - sx * t, z0 - sz * c), (x0, z0 - sz * c)]
            if sx * sz < 0:
                pts.reverse()
            br = C.extrude(name + "_br%d%d" % (sx, sz), pts, 0.05, M("metal_dark"), y0=fy - 0.05)
            o.append(br)
            o.append(nail(name + "_rv%d%d" % (sx, sz), x0 - sx * t * 0.5 - sx * c * 0.35, z0 - sz * t * 0.5, fy - 0.05, "steel", 0.06))
            o.append(nail(name + "_rw%d%d" % (sx, sz), x0 - sx * t * 0.5, z0 - sz * t * 0.5 - sz * c * 0.35, fy - 0.05, "steel", 0.06))
    return o


def _ice_box(name, w, h, cx, cz, fy, depth, rnd):
    o = []
    # light facets
    for i in range(3 if min(w, h) > 1.2 else 1):
        px, pz = cx + rnd.uniform(-0.3, 0.3) * w, cz + rnd.uniform(-0.3, 0.3) * h
        r = min(w, h) * rnd.uniform(0.12, 0.22)
        a0 = rnd.uniform(0, 6.28)
        pts = [(px + r * math.cos(a0 + k * 2.1), pz + r * math.sin(a0 + k * 2.1)) for k in range(3)]
        tri = C.extrude(name + "_f%d" % i, pts, 0.02, M("ice_light" if i % 2 == 0 else "ice_mid"), y0=fy - 0.02)
        o.append(tri)
    o.append(C.box(name + "_hl", (cx - w * 0.22, fy - 0.02, cz + h * 0.22), (min(w, h) * 0.35, 0.03, 0.07), M("white"),
                   rot=Matrix.Rotation(0.6, 4, "Y")))
    o.append(C.box(name + "_hl2", (cx - w * 0.1, fy - 0.02, cz + h * 0.14), (min(w, h) * 0.15, 0.03, 0.05), M("white"),
                   rot=Matrix.Rotation(0.6, 4, "Y")))
    # lumpy snow cap along the top edge
    top = cz + h / 2
    n = max(3, int(w / 0.35))
    pts = []
    for i in range(n + 1):
        x = cx - w / 2 + w * i / n
        pts.append((x, top + 0.06 + 0.07 * math.sin(i * 1.7 + rnd.uniform(0, 1)) + rnd.uniform(0, 0.05)))
    edge = []
    for i in range(n, -1, -1):
        x = cx - w / 2 + w * i / n
        drip = 0.1 + (0.2 if rnd.random() < 0.3 else 0.0) * min(1.0, h / 1.5)
        edge.append((x, top - drip))
    snow = C.extrude(name + "_snow", pts + edge, depth * 1.04, M("snow"), bevel=0.03)
    o.append(snow)
    return o


def details(name, material, pts, depth, w, h, cx, cz, kind):
    import random
    rnd = random.Random(sum(ord(ch) * (i + 1) for i, ch in enumerate(name)))
    o = []
    fy = -depth / 2
    face, rim = MATERIALS[material]
    inset = []
    for (x, z) in pts:
        d = V((cx - x, 0, cz - z))
        if d.length > 1e-4:
            inset.append(V((x, fy - 0.01, z)) + d.normalized() * min(0.35, d.length * 0.3))
    if kind == "box":
        if material == "Wood":
            return _wood_box(name, w, h, cx, cz, fy, rnd)
        if material == "Stone":
            return _stone_box(name, w, h, cx, cz, fy, rnd)
        if material == "Metal":
            return _metal_box(name, w, h, cx, cz, fy, rnd)
        return _ice_box(name, w, h, cx, cz, fy, depth, rnd)
    # triangles: inset trim plus a few accents
    if material == "Wood":
        for i, p in enumerate(inset):
            o.append(nail(name + "_n%d" % i, p.x, p.z, fy - 0.01))
        o.append(C.box(name + "_g", (cx, fy - 0.01, cz - h * 0.1), (w * 0.5, 0.03, 0.05), M(rim)))
    elif material == "Metal":
        for i, p in enumerate(inset):
            o.append(nail(name + "_r%d" % i, p.x, p.z, fy - 0.01, "metal_dark", 0.09))
    elif material == "Stone":
        for i in range(2 if min(w, h) > 1.5 else 1):
            ln = min(w, h) * 0.3
            px = cx + rnd.uniform(-w, w) * 0.15
            pz = cz + rnd.uniform(-h, h) * 0.15
            o.append(C.box(name + "_c%d" % i, (px, fy - 0.01, pz), (ln, 0.03, 0.05), M(rim),
                           rot=Matrix.Rotation(rnd.uniform(-0.8, 0.8), 4, "Y")))
    elif material == "Ice":
        o.append(C.box(name + "_hl", (cx - w * 0.15, fy - 0.01, cz), (min(w, h) * 0.3, 0.03, 0.06), M("white"),
                       rot=Matrix.Rotation(0.6, 4, "Y")))
    return o


def ball(name, material, r, cx, cz):
    face, rim = MATERIALS[material]
    o = []
    if material == "Stone":
        o.append(C.ico(name + "_b", (cx, 0, cz), r, M(face), subdiv=2, seed=7, jitter=0.06, smooth=True))
        o.append(C.box(name + "_c", (cx + r * 0.2, -r * 0.98, cz + r * 0.1), (r * 0.5, 0.03, 0.05), M(rim),
                       rot=Matrix.Rotation(0.5, 4, "Y")))
        for i, (a, rr) in enumerate(((2.2, 0.22), (4.0, 0.16), (5.3, 0.12))):
            p = V((cx + r * 0.55 * math.cos(a), -r * 0.82, cz + r * 0.55 * math.sin(a)))
            o.append(C.sphere(name + "_cr%d" % i, p, (r * rr, r * 0.05, r * rr), M("stone_mid"), 8, 4))
    else:
        o.append(C.sphere(name + "_b", (cx, 0, cz), r, M(face), 20, 12))
    if material == "Wood":
        for i in (-1, 1):
            o.append(C.torus(name + "_ring%d" % i, (cx, 0, cz + i * r * 0.45), r * 0.9, 0.04, M(rim), 20, 4))
    elif material == "Metal":
        o.append(C.torus(name + "_eq", (cx, 0, cz), r * 1.0, 0.06, M(rim), 24, 6, axis=(0, 1, 0)))
        for i in range(6):
            a = 2 * math.pi * i / 6
            o.append(C.sphere(name + "_rv%d" % i, (cx + r * 0.75 * math.cos(a), -r * 0.67, cz + r * 0.75 * math.sin(a)),
                              0.07, M(rim), 6, 4))
    elif material == "Ice":
        o.append(C.sphere(name + "_hl", (cx - r * 0.35, -r * 0.85, cz + r * 0.35), (r * 0.18, r * 0.06, r * 0.12),
                          M("white"), 8, 5))
    return o


def build_level_object(oid, col):
    shape = next(k for k in SHAPE_BODY if oid.startswith(k))
    material = oid[len(shape):]
    body = body_shape(material, SHAPE_BODY[shape])
    C.use_collection(col)
    face, rim = MATERIALS[material]
    objs = []
    if body["circle"] and shape.startswith("Ball"):
        r, x, y = body["circle"]
        objs += ball(oid, material, r / PX, x / PX, -y / PX)
    elif shape.startswith("Ball"):
        pts = to_game(body["polys"][0])
        xs, zs = [p[0] for p in pts], [p[1] for p in pts]
        r = (max(xs) - min(xs) + max(zs) - min(zs)) / 4
        objs += ball(oid, material, r, (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2)
    else:
        allp = [to_game(p) for p in body["polys"]]
        pts = allp[0]
        xs, zs = [p[0] for p in pts], [p[1] for p in pts]
        w, h = max(xs) - min(xs), max(zs) - min(zs)
        cx, cz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2
        depth = depth_for(w, h)
        bevel = {"Stone": 0.16, "Wood": 0.1, "Ice": 0.12, "Metal": 0.08}[material]
        bevel = min(bevel, min(w, h) * 0.12)
        objs.append(slab(oid + "_slab", pts, depth, face, rim, bevel))
        objs += details(oid, material, pts, depth, w, h, cx, cz, "tri" if shape.startswith("Triangle") else "box")
    ob = C.join(objs, oid)
    C.use_collection(None)
    return ob


def build_tree(col):
    C.use_collection(col)
    b = body_shape("CustomObjects", "custom_object_tree") if os.path.exists(os.path.join(XML_DIR, "CustomObjects.xml")) else None
    pts = [p for poly in (b["polys"] if b else []) for p in to_game(poly)] or [(-6, -7.3), (6, 7.3)]
    xs, zs = [p[0] for p in pts], [p[1] for p in pts]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    w, h = x1 - x0, z1 - z0
    cx = (x0 + x1) / 2
    o = [C.cyl("tree_trunk", (cx, 0, z0), (cx, 0, z0 + h * 0.55), w * 0.09, w * 0.06, M("bark"), 10)]
    o.append(C.cyl("tree_root", (cx, 0, z0), (cx, 0, z0 + h * 0.08), w * 0.16, w * 0.09, M("bark"), 10))
    o.append(C.cyl("tree_br", (cx, 0, z0 + h * 0.4), (cx + w * 0.22, 0, z0 + h * 0.58), w * 0.04, w * 0.025, M("bark"), 8))
    blobs = [(0.0, 0.72, 0.3), (-0.22, 0.6, 0.22), (0.22, 0.62, 0.22), (-0.12, 0.85, 0.2), (0.14, 0.86, 0.19),
             (0.0, 0.5, 0.18)]
    for i, (dx, dz, r) in enumerate(blobs):
        o.append(C.ico("tree_l%d" % i, (cx + dx * w, -0.2 * (i % 2), z0 + dz * h), r * w * 0.95,
                       M("leaf" if i % 2 == 0 else "leaf_dark"), subdiv=2, seed=i + 20, jitter=0.07, smooth=True,
                       scale=(1, 0.6, 1)))
    ob = C.join(o, "custom_object_tree")
    C.use_collection(None)
    return ob


# ------------------------------------------------------------------------------------------ pickups

def crate_mesh(n, size=1.3, wood="wood", dark="wood_dark", emblem=None):
    """Crate centered at the origin (Battle/PowerUpCrate uses a 1.3 x 1.3 collider around the pivot)."""
    s = size
    t = s * 0.09
    o = [C.box(n + "_b", (0, 0, 0), (s, s, s), M(wood), bevel=0.04)]
    for sy in (-1, 1):
        for x in (-1, 1):
            o.append(C.box(n + "_e%d%d" % (sy, x), (x * (s / 2 - t / 2), sy * (s / 2 + 0.01), 0), (t, 0.03, s), M(dark)))
            o.append(C.box(n + "_h%d%d" % (sy, x), (0, sy * (s / 2 + 0.01), x * (s / 2 - t / 2)), (s, 0.03, t), M(dark)))
        if not emblem:
            o.append(C.box(n + "_d%d" % sy, (0, sy * (s / 2 + 0.015), 0), (s * 1.2, 0.03, t), M(dark),
                           rot=Matrix.Rotation(math.radians(45), 4, "Y")))
    for sx in (-1, 1):
        o.append(C.box(n + "_sx%d" % sx, (sx * (s / 2 + 0.01), 0, 0), (0.03, s * 0.98, t), M(dark)))
    # board grooves between the frame and nails at the frame corners (front face)
    for i in (-1, 1):
        o.append(C.box(n + "_gv%d" % i, (0, -s / 2 - 0.004, i * s * 0.17), (s - 2 * t, 0.012, 0.022), M(dark)))
    for x in (-1, 1):
        for z in (-1, 1):
            o.append(C.sphere(n + "_nl%d%d" % (x, z), (x * (s / 2 - t / 2), -s / 2 - 0.03, z * (s / 2 - t / 2)),
                              (0.035, 0.015, 0.035), M("steel"), 6, 3))
    f = -s / 2 - 0.03
    if emblem:
        kind, col = emblem
        if kind == "cross":
            o.append(C.box(n + "_xb", (0, f + 0.01, 0), (s * 0.55, 0.03, s * 0.55), M("white"), bevel=0.03))
            for r in (0, 90):
                o.append(C.box(n + "_x%d" % r, (0, f - 0.01, 0), (s * 0.4, 0.03, s * 0.13), M(col),
                               rot=Matrix.Rotation(math.radians(r), 4, "Y")))
        elif kind == "ammo":
            o.append(C.box(n + "_xb", (0, f + 0.01, 0), (s * 0.55, 0.03, s * 0.55), M("olive_dark"), bevel=0.03))
            for i in range(3):
                p = S.bullet(n + "_am%d" % i, "gold", s * 0.38, s * 0.05)[0]
                p.data.transform(Matrix.Translation((-s * 0.14 + s * 0.14 * i, f - 0.05, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "Y"))
                o.append(p)
        elif kind == "star":
            o.append(C.box(n + "_xb", (0, f + 0.01, 0), (s * 0.55, 0.03, s * 0.55), M("white"), bevel=0.03))
            pts = [((0.22 if i % 2 == 0 else 0.09) * s * math.cos(math.pi / 2 + i * math.pi / 5),
                    (0.22 if i % 2 == 0 else 0.09) * s * math.sin(math.pi / 2 + i * math.pi / 5)) for i in range(10)]
            st = C.extrude(n + "_st", pts, 0.04, M(col))
            st.data.transform(Matrix.Translation((0, f - 0.02, 0)))
            o.append(st)
    return o


def treasure_mesh(n, s=1.3):
    o = [C.box(n + "_b", (0, 0, -s * 0.12), (s * 1.1, s * 0.75, s * 0.7), M("brown"), bevel=0.03)]
    lid = C.cyl(n + "_lid", (-s * 0.55, 0, s * 0.23), (s * 0.55, 0, s * 0.23), s * 0.375, s * 0.375, M("brown"), 16)
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(lid.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < s * 0.23 - 1e-4], context="VERTS")
    bm.to_mesh(lid.data)
    bm.free()
    o.append(lid)
    o.append(C.box(n + "_lb", (0, 0, s * 0.235), (s * 1.1, s * 0.75, 0.04), M("brown_dark")))
    for x in (-0.42, 0.0, 0.42):
        o.append(C.box(n + "_band%d" % int(x * 10), (x * s, 0, -s * 0.12), (s * 0.08, s * 0.77, s * 0.72), M("gold")))
        o.append(C.torus(n + "_lband%d" % int(x * 10), (x * s, 0, s * 0.23), s * 0.38, s * 0.03, M("gold"), 16, 4, axis=(1, 0, 0)))
    o.append(C.box(n + "_lock", (0, -s * 0.39, s * 0.12), (s * 0.16, 0.05, s * 0.2), M("gold"), bevel=0.02))
    o.append(C.box(n + "_kh", (0, -s * 0.42, s * 0.1), (s * 0.03, 0.02, s * 0.07), M("gun_dark")))
    for i, (x, z) in enumerate(((-0.3, 0.55), (0.1, 0.6), (0.3, 0.5))):
        o.append(C.cyl(n + "_coin%d" % i, (x * s, -0.05, z * s), (x * s, 0.05, z * s), s * 0.11, s * 0.11, M("yellow"), 12))
    return o


def parachute(n, r=1.35, dome=0.85, drop=1.55, col=("red", "white")):
    """Canopy rim at z=0 (origin), strings converge at z=-drop (top of the crate)."""
    import bmesh
    o = []
    segs = 8
    for i in range(segs):
        a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
        bm = bmesh.new()
        rows = []
        for j in range(5):
            lat = math.radians(80 * j / 4)
            row = []
            for k in range(3):
                a = a0 + (a1 - a0) * k / 2
                rr = r * math.cos(lat) * (1 - 0.06 * math.sin(math.pi * k / 2))
                row.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), dome * math.sin(lat))))
            rows.append(row)
        top = bm.verts.new((0, 0, dome))
        for j in range(4):
            for k in range(2):
                bm.faces.new((rows[j][k], rows[j][k + 1], rows[j + 1][k + 1], rows[j + 1][k]))
        for k in range(2):
            bm.faces.new((rows[4][k], rows[4][k + 1], top))
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = C._obj_from_bm(n + "_p%d" % i, bm, M(col[i % 2]), True)
        mod = ob.modifiers.new("Solid", "SOLIDIFY")
        mod.thickness = 0.04
        C.apply_modifiers(ob)
        o.append(ob)
        o.append(C.cyl(n + "_s%d" % i, (r * 0.97 * math.cos(a0), r * 0.97 * math.sin(a0), 0.0),
                       (0.3 * math.cos(a0), 0.3 * math.sin(a0), -drop), 0.012, 0.012, M("rope"), 4))
    return o


def build_crate(col, name="Crate", emblem=None, wood="wood", chute=True):
    C.use_collection(col)
    crate = C.join(crate_mesh(name, emblem=emblem, wood=wood), name)
    if chute:
        para = C.join(parachute(name + "P"), "Parachute_" + name)
        para["export_name"] = "Parachute"
        para.location = (0, 0, 2.2)
        C.parent(para, crate)
    C.use_collection(None)
    return crate


def build_parachute(col):
    C.use_collection(col)
    ob = C.join(parachute("ParachuteP"), "Parachute")
    C.use_collection(None)
    return ob


def build_treasure(col):
    C.use_collection(col)
    ob = C.join(treasure_mesh("Treasure"), "Treasure")
    C.use_collection(None)
    return ob


def build_mine(col, name="Mine", light="red", body="gun"):
    C.use_collection(col)
    o = S.mine(name, light, body)
    for p in o:
        p.data.transform(Matrix.Diagonal((3.3, 3.3, 3.0, 1)))
    ob = C.join(o, name)
    C.use_collection(None)
    return ob


def build_coin(col, name="Coin"):
    C.use_collection(col)
    o = [C.cyl(name + "_c", (0, 0.06, 0), (0, -0.06, 0), 0.42, 0.42, M("gold"), 24),
         C.torus(name + "_r", (0, 0, 0), 0.42, 0.05, M("gold_dark"), 24, 6, axis=(0, 1, 0))]
    for sy in (-1, 1):
        pts = []
        for i in range(10):
            a = math.pi / 2 + i * math.pi / 5
            rr = 0.24 if i % 2 == 0 else 0.1
            pts.append((rr * math.cos(a), rr * math.sin(a)))
        st = C.extrude(name + "_s%d" % sy, pts, 0.03, M("yellow"))
        st.data.transform(Matrix.Translation((0, sy * 0.07, 0)))
        o.append(st)
    ob = C.join(o, name)
    C.use_collection(None)
    return ob


PICKUPS = {
    "Crate": lambda col: build_crate(col, "Crate"),
    "HealthCrate": lambda col: build_crate(col, "HealthCrate", ("cross", "red"), "cream", chute=False),
    "AmmoCrate": lambda col: build_crate(col, "AmmoCrate", ("ammo", "gold"), "olive", chute=False),
    "PointsCrate": lambda col: build_crate(col, "PointsCrate", ("star", "blue"), "sky", chute=False),
    "Treasure": build_treasure,
    "Parachute": build_parachute,
    "Mine": lambda col: build_mine(col, "Mine"),
    "FireMine": lambda col: build_mine(col, "FireMine", "fire", "orange_dark"),
    "Coin": lambda col: build_coin(col),
}


def level_object_ids():
    ids = C.ids("LevelObject")
    if not ids:
        ids = [s + m for m in MATERIALS for s in SHAPE_BODY] + ["custom_object_tree"]
    return ids


def build(oid, col):
    if oid == "custom_object_tree":
        return build_tree(col)
    if oid in PICKUPS:
        return PICKUPS[oid](col)
    return build_level_object(oid, col)


def run():
    C.reset()
    out = os.path.join(C.MODELS, "Props")
    cols = {}
    stats = {}
    for oid in level_object_ids() + list(PICKUPS):
        col = C.new_collection("P_" + oid)
        build(oid, col)
        stats[oid] = C.tri_count(C.col_objects(col))
        C.export_collection(col, os.path.join(out, oid + ".fbx"))
        cols[oid] = col
    C.save_blend("props")
    over = {k: v for k, v in stats.items() if v > 1500}
    print("[props] %d models, max tris %d, over budget: %s" % (len(stats), max(stats.values()), over))
    return cols
