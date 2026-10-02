"""Clothing models (Bonus ids). Modeled in the penguin's canonical frame around the real body/head/feet
geometry, then moved so the socket is the origin and turned by the penguin's yaw, so a model parented to
the socket with identity local transform fits exactly.

Sockets: head items -> HeadSocket (head sphere center), chest items -> ChestSocket (body axis),
feet items -> FootSocketL (ankle). Feet items are symmetric around the foot axis, so the same model is used
on FootSocketR (no mirroring needed).
"""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math
import os

from mathutils import Matrix, Vector

import common as C
import penguin as P

H = P.HEAD_C
HR = P.HEAD_R
ANK = P.ANKLE_L
V = Vector

SETS = ["flannel", "paper", "rain", "clown", "sm", "polar", "hockey", "cowboy", "schoolgirl", "wizard", "welder",
        "tuxedo", "bunny", "desert", "specialforce", "football", "space", "gladiator", "king", "dark_assassin", "elvis"]
EXTRA = {"army_helmet_blue": "head", "army_helmet_red": "head", "army_jacket_blue": "chest", "army_jacket_red": "chest",
         "army_boots_blue": "feet", "army_boots_red": "feet", "RedSweater": "chest", "Skates": "feet", "RedHat": "head"}


def all_items():
    """id -> slot for every clothing Bonus id."""
    out = {}
    for s in SETS:
        for slot in ("head", "chest", "feet"):
            out["%s_%s" % (s, slot)] = slot
    out.update(EXTRA)
    return out


SOCKET = {"head": H, "chest": P.CHEST, "feet": ANK}
SOCKET_NAME = {"head": "HeadSocket", "chest": "ChestSocket", "feet": "FootSocketL"}

M = C.mat


# ------------------------------------------------------------------------------------------ helpers

def body_r(z):
    prof = P.body_profile()
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            return r0 + (r1 - r0) * t
    return 0.0


def shell(name, z0, z1, mat, k=1.07, segs=20, flare=0.0, n=8, yk=1.0, top_close=False):
    """Torso shell following the body profile from z0 (bottom) to z1 (top). flare widens the bottom."""
    prof = []
    for i in range(n + 1):
        z = z0 + (z1 - z0) * i / n
        f = flare * (1 - i / n) ** 2
        prof.append((max(body_r(z), 0.25) * k + f, z))
    if top_close:
        prof.append((0.0, z1 + 0.02))
    return C.lathe(name, prof, mat, segs=segs, scale=(1, yk, 1))


def front(z, k=1.07, yk=1.0, ang=0.0):
    """Point on a shell surface at height z, ang degrees around from the front (+ = towards penguin's left)."""
    r = body_r(z) * k
    a = math.radians(-90 + ang)
    return V((r * math.cos(a), r * math.sin(a) * yk, z))


def cap(name, mat, r=0.68, lat0=0.0, tilt=0.0, segs=20, center=None, squash=(1.0, 0.97, 1.0), lat1=90.0):
    """Sphere cap around the head: from latitude lat0 (degrees, 0 = equator) to lat1 (90 = top).
    tilt > 0 tips the cap back so the rim is higher at the front."""
    center = H if center is None else center
    prof = []
    steps = 7
    for i in range(steps + 1):
        l = math.radians(lat0 + (lat1 - lat0) * i / steps)
        prof.append((r * math.cos(l), r * math.sin(l)))
    if lat1 >= 89.9:
        prof[-1] = (0.0, r)
    ax = V((0, math.sin(math.radians(tilt)), math.cos(math.radians(tilt))))
    return C.lathe(name, prof, mat, segs=segs, center=center, axis=ax, scale=squash)


def head_pt(lat, lon, r=HR):
    """Point on the head sphere: lat degrees up from equator, lon degrees from the front towards the left."""
    la, lo = math.radians(lat), math.radians(-90 + lon)
    return H + V((r * math.cos(la) * math.cos(lo), r * math.cos(la) * math.sin(lo), r * math.sin(la)))


def disc(name, center, r, h, mat, segs=20):
    return C.cyl(name, center - V((0, 0, h / 2)), center + V((0, 0, h / 2)), r, r, mat, segs)


def ring(name, center, r, thick, mat, axis=(0, 0, 1), segs=18, scale=(1, 1, 1)):
    return C.torus(name, center, r, thick, mat, segs, 6, axis=axis, scale=scale)


def ball(name, c, r, mat, segs=10, rings=6):
    return C.sphere(name, c, r, mat, segs, rings)


def star(name, center, r, depth, mat, normal="front", points=5, inner=0.45):
    pts = []
    for i in range(points * 2):
        a = math.pi / 2 + i * math.pi / points
        rr = r if i % 2 == 0 else r * inner
        pts.append((rr * math.cos(a), rr * math.sin(a)))
    ob = C.extrude(name, pts, depth, mat)
    ob.data.transform(Matrix.Translation(center))
    return ob


def sheet(name, mat, rows, thickness=0.03):
    """A solidified grid surface from rows of points (list of lists of Vector)."""
    import bmesh
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for i in range(len(rows) - 1):
        for j in range(len(rows[0]) - 1):
            bm.faces.new((vs[i][j], vs[i][j + 1], vs[i + 1][j + 1], vs[i + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = C._obj_from_bm(name, bm, mat, True)
    mod = ob.modifiers.new("Solid", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = 0
    C.apply_modifiers(ob)
    return ob


def cape(name, mat, z_top=1.45, z_bot=0.15, a0=110, a1=250, spread=0.28, lining=None):
    """Cape hanging behind the body. a0..a1: degrees around (0 = front, 180 = back)."""
    rows = []
    nz, na = 6, 9
    for i in range(nz + 1):
        t = i / nz
        z = z_top + (z_bot - z_top) * t
        r = max(body_r(z), 0.45) * 1.12 + spread * t
        row = []
        for j in range(na + 1):
            a = math.radians(-90 + a0 + (a1 - a0) * j / na)
            wav = 0.04 * math.sin(j * 1.7) * t
            row.append(V(((r + wav) * math.cos(a), (r + wav) * math.sin(a), z)))
        rows.append(row)
    return sheet(name, mat, rows, 0.04)


def shoe(name, mat, length=1.0, height=1.0, width=1.0, shaft=0.0, shaft_mat=None, sole=None, cuff=None,
         toe=None, laces=None, toe_up=0.0):
    """A shoe around the left foot (origin handled later). Returns list of objects."""
    o = []
    c = ANK + V((0, -0.24 * length, -0.025))
    o.append(C.sphere(name + "_b", c, (0.25 * width, 0.43 * length, 0.15 * height), mat, 16, 10))
    if toe_up:
        o.append(C.sphere(name + "_tu", c + V((0, -0.42 * length, 0.08 + toe_up * 0.5)),
                          (0.1 * width, 0.12, 0.08 + toe_up * 0.3), mat, 10, 6))
    if sole:
        o.append(C.sphere(name + "_s", c + V((0, 0, -0.07 * height)), (0.27 * width, 0.45 * length, 0.07), M(sole), 16, 6))
    if toe:
        o.append(C.sphere(name + "_t", c + V((0, -0.24 * length, 0.0)), (0.2 * width, 0.22 * length, 0.13 * height), M(toe), 12, 8))
    if shaft > 0:
        sm = shaft_mat or mat
        o.append(C.cyl(name + "_sh", ANK + V((0, 0.02, -0.04)), ANK + V((0, 0.02, shaft)), 0.19 * width, 0.21 * width, sm, 16))
        if cuff:
            o.append(ring(name + "_c", ANK + V((0, 0.02, shaft)), 0.2 * width, 0.05, M(cuff)))
    elif cuff:
        o.append(ring(name + "_c", ANK + V((0, 0.0, 0.03)), 0.17 * width, 0.05, M(cuff)))
    if laces:
        for i in range(3):
            o.append(C.box(name + "_l%d" % i, c + V((0, -0.05 - 0.09 * i, 0.12 * height - 0.012 * i)),
                           (0.16, 0.025, 0.02), M(laces)))
    return o


# ------------------------------------------------------------------------------------------ head items

def hat_brim(n, crown_mat, brim_mat=None, crown_h=0.45, crown_r=0.42, brim_r=0.75, band=None, top_r=None,
             z=0.48, tilt=-8, dent=False, brim_up=0.0):
    brim_mat = brim_mat or crown_mat
    base = H + V((0, 0.02, z))
    o = [C.cyl(n + "_br", base - V((0, 0, 0.02)), base + V((0, 0, 0.02)), brim_r, brim_r, brim_mat, 24)]
    if brim_up:
        o.append(ring(n + "_bu", base + V((0, 0, 0.03)), brim_r - 0.04, 0.05, brim_mat, scale=(1, 0.75, 1)))
    o.append(C.cyl(n + "_cr", base, base + V((0, 0, crown_h)), crown_r, top_r or crown_r * 0.92, crown_mat, 20))
    if dent:
        o.append(C.sphere(n + "_dt", base + V((0, 0, crown_h)), (crown_r * 0.85, crown_r * 0.6, 0.08), crown_mat, 14, 6))
    if band:
        o.append(C.cyl(n + "_bd", base, base + V((0, 0, 0.1)), crown_r * 1.03, crown_r * 1.02, M(band), 20))
    return _tilt(o, tilt, base)


def _tilt(objs, deg, pivot, axis="X"):
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-pivot)
    for ob in objs:
        ob.data.transform(m)
    return objs


def earflaps(n, mat):
    o = []
    for sx in (-1, 1):
        o.append(C.sphere(n + "_ef%d" % sx, H + V((0.6 * sx, 0.05, -0.12)), (0.1, 0.24, 0.3), mat, 12, 8))
    return o


def head_item(item):
    s = item.rsplit("_", 1)[0] if item.endswith("_head") else item
    n = item
    o = []
    if s == "flannel":
        o += [cap(n + "_c", M("plaid_red"), 0.67, -5, tilt=12)]
        o += [ring(n + "_b%d" % i, H + V((0, 0.07, 0.12 + 0.17 * i)), 0.66 * math.cos(math.asin(min(0.95, (0.12 + 0.17 * i) / 0.67))), 0.025,
                   M("red_dark"), axis=(0, 0.2, 1)) for i in range(3)]
        o += earflaps(n, M("plaid_red"))
        o += [ball(n + "_p", H + V((0, 0.05, 0.7)), 0.09, M("red_dark"))]
    elif s == "paper":
        pts = [(-0.68, 0), (0.68, 0), (0.42, 0.18), (0, 0.6), (-0.42, 0.18)]
        hat = C.extrude(n + "_h", pts, 0.5, M("cream"), bevel=0.015)
        hat.data.transform(Matrix.Translation(H + V((0, 0.05, 0.42))))
        o += [hat]
        for i in range(3):
            o.append(C.box(n + "_t%d" % i, H + V((-0.15 + 0.15 * i, -0.21, 0.6 + 0.07 * (i % 2))), (0.1, 0.01, 0.025), M("grey")))
        o.append(C.box(n + "_b", H + V((0, -0.21, 0.47)), (1.2, 0.012, 0.06), M("blue")))
    elif s == "rain":
        o += [cap(n + "_c", M("yellow"), 0.68, 5, tilt=8)]
        brim = C.cyl(n + "_br", H + V((0, 0.08, 0.2)), H + V((0, 0.08, 0.24)), 0.88, 0.8, M("yellow"), 24)
        o += [brim]
        _tilt([brim], 14, H + V((0, 0, 0.22)))
        o += [C.cyl(n + "_s", H + V((0.55, -0.2, 0.0)), H + V((0.4, -0.45, -0.45)), 0.025, 0.025, M("yellow"), 6)]
    elif s == "clown":
        cols = ["red", "orange", "yellow", "green", "blue", "purple"]
        k = 0
        for lat in (10, 35, 60):
            for lon in range(40, 330, 45 if lat < 60 else 80):
                p = head_pt(lat, lon, HR + 0.08)
                o.append(ball(n + "_a%d" % k, p, 0.22 if lat < 60 else 0.24, M(cols[k % len(cols)]), 7, 4))
                k += 1
        o.append(ball(n + "_top", H + V((0, 0.05, 0.75)), 0.24, M("red"), 12, 8))
        o.append(ball(n + "_nose", H + V((0, -0.95, -0.15)), 0.12, M("red"), 12, 8))
    elif s == "sm":
        o += [ring(n + "_mask", H + V((0, 0.0, 0.1)), HR * 0.93, 0.07, M("navy"), scale=(1, 0.97, 1.25))]
        for sx in (-1, 1):
            o.append(C.cyl(n + "_k%d" % sx, H + V((0.12 * sx, 0.62, 0.12)), H + V((0.3 * sx, 0.9, 0.0)), 0.06, 0.02, M("navy"), 8))
        o.append(C.cyl(n + "_curl", H + V((0.05, -0.35, 0.5)), H + V((0.12, -0.6, 0.55)), 0.08, 0.0, M("black"), 8))
    elif s == "polar":
        o += [cap(n + "_c", M("cream"), 0.7, 0, tilt=10)]
        o += [ring(n + "_fur", H + V((0, 0.05, 0.12)), 0.67, 0.12, M("tan"), axis=(0, 0.18, 1))]
        o += earflaps(n, M("tan"))
        o += [ball(n + "_p", H + V((0, 0.05, 0.75)), 0.12, M("tan"))]
    elif s in ("hockey",):
        o += [cap(n + "_c", M("white"), 0.68, -25, tilt=35)]
        for i in range(4):
            x = -0.27 + 0.18 * i
            o.append(C.cyl(n + "_v%d" % i, H + V((x, -0.72, 0.28)), H + V((x, -0.72, -0.42)), 0.02, 0.02, M("steel_dark"), 6))
        for i in range(3):
            z = 0.2 - 0.25 * i
            o.append(C.cyl(n + "_h%d" % i, H + V((-0.38, -0.66, z)), H + V((0.38, -0.66, z)), 0.02, 0.02, M("steel_dark"), 6))
        o.append(C.box(n + "_s", H + V((0, -0.1, 0.66)), (0.12, 0.8, 0.06), M("red"), rot=Matrix.Rotation(0.3, 4, "X")))
    elif s == "cowboy":
        o += hat_brim(n, M("brown"), M("brown"), 0.4, 0.4, 0.8, band="brown_dark", top_r=0.36, dent=True, brim_up=1)
    elif s == "schoolgirl":
        c = H + V((0.25, 0.0, 0.55))
        o.append(ball(n + "_k", c, 0.1, M("red")))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_w%d" % sx, c, c + V((0.38 * sx, 0.05, 0.12)), 0.03, 0.2, M("red"), 12))
            o.append(C.cyl(n + "_t%d" % sx, c, c + V((0.15 * sx, -0.05, -0.3)), 0.04, 0.08, M("red"), 8))
        o.append(cap(n + "_band", M("red"), 0.665, 30, lat1=38, tilt=-20))
    elif s == "wizard":
        base = H + V((0, 0.03, 0.4))
        o.append(C.cyl(n + "_br", base, base + V((0, 0, 0.04)), 0.85, 0.85, M("purple"), 24))
        cone = C.lathe(n + "_cone", [(0.5, 0), (0.42, 0.3), (0.28, 0.65), (0.12, 0.95), (0.0, 1.15)], M("purple"), 16,
                       center=base)
        cone.data.transform(Matrix.Translation(base) @ Matrix.Rotation(-0.25, 4, "X") @ Matrix.Translation(-base))
        o.append(cone)
        o.append(C.cyl(n + "_bd", base, base + V((0, 0, 0.1)), 0.51, 0.49, M("yellow"), 20))
        for i, (x, z, r) in enumerate(((0.2, 0.35, 0.09), (-0.12, 0.6, 0.07), (0.05, 0.85, 0.05))):
            st = star(n + "_st%d" % i, V((0, 0, 0)), r, 0.03, M("yellow"))
            st.data.transform(Matrix.Translation(base + V((x, -0.42 + z * 0.33, z))))
            o.append(st)
    elif s == "welder":
        o += [cap(n + "_c", M("grey_dark"), 0.67, 10, tilt=-5)]
        mask = C.box(n + "_m", H + V((0, -0.42, 0.62)), (0.85, 0.12, 0.62), M("steel"), bevel=0.06, bevel_segs=2)
        win = C.box(n + "_w", H + V((0, -0.49, 0.64)), (0.5, 0.04, 0.16), M("gun_dark"), bevel=0.02)
        o += _tilt([mask, win], -55, H + V((0, -0.2, 0.45)))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_hg%d" % sx, H + V((0.62 * sx, -0.05, 0.25)), H + V((0.7 * sx, -0.05, 0.25)), 0.08, 0.08, M("gun"), 10))
    elif s == "tuxedo":
        o += hat_brim(n, M("gun_dark"), M("gun_dark"), 0.7, 0.36, 0.56, band="red", top_r=0.4, z=0.5, tilt=-10)
    elif s == "bunny":
        o.append(cap(n + "_band", M("pink"), 0.665, 52, lat1=60, tilt=25))
        for sx in (-1, 1):
            b = head_pt(55, 90 * sx * 0.5 + 0, HR) + V((0.12 * sx, 0.08, 0.0))
            ear = C.sphere(n + "_e%d" % sx, b + V((0.1 * sx, 0.0, 0.45)), (0.15, 0.08, 0.48), M("white"), 12, 8,
                           rot=Matrix.Rotation(-0.25 * sx, 4, "Y"))
            inner = C.sphere(n + "_i%d" % sx, b + V((0.1 * sx, -0.05, 0.42)), (0.09, 0.05, 0.36), M("pink"), 10, 6,
                             rot=Matrix.Rotation(-0.25 * sx, 4, "Y"))
            o += [ear, inner]
    elif s == "desert":
        o += [cap(n + "_c", M("khaki"), 0.7, 0, tilt=5, squash=(1, 1.05, 1.05))]
        o.append(C.cyl(n + "_br", H + V((0, 0.0, 0.1)), H + V((0, 0.0, 0.16)), 0.86, 0.72, M("khaki"), 24))
        o.append(C.cyl(n + "_bd", H + V((0, 0.0, 0.16)), H + V((0, 0.0, 0.26)), 0.71, 0.7, M("brown"), 24))
        o.append(ball(n + "_top", H + V((0, 0.0, 0.73)), 0.07, M("khaki")))
    elif s == "specialforce":
        o += [cap(n + "_c", M("olive_dark"), 0.69, -10, tilt=25)]
        o.append(C.box(n + "_mt", H + V((0, -0.5, 0.48)), (0.2, 0.12, 0.14), M("gun_dark"), bevel=0.02))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_g%d" % sx, H + V((0.1 * sx, -0.55, 0.52)), H + V((0.12 * sx, -0.78, 0.5)), 0.08, 0.09, M("gun_dark"), 12))
            o.append(C.cyl(n + "_gl%d" % sx, H + V((0.12 * sx, -0.78, 0.5)), H + V((0.12 * sx, -0.8, 0.5)), 0.075, 0.075, M("glow_lime", "#b6e83a", 2.0), 12))
        o.append(ring(n + "_strap", H + V((0, 0.0, 0.22)), 0.69, 0.035, M("gun_dark"), axis=(0, 0.25, 1)))
    elif s == "football":
        o += [cap(n + "_c", M("blue"), 0.7, -30, tilt=30)]
        o.append(C.box(n + "_st", H + V((0, 0.0, 0.7)), (0.16, 0.9, 0.05), M("white"), rot=Matrix.Rotation(0.15, 4, "X")))
        for i in range(2):
            z = -0.05 - 0.22 * i
            o.append(C.torus(n + "_fm%d" % i, H + V((0, -0.05, z)), 0.72, 0.03, M("grey_light"), 20, 6,
                             scale=(0.85, 1.0, 1.0)))
        o.append(C.cyl(n + "_fv", H + V((0, -0.77, 0.05)), H + V((0, -0.77, -0.3)), 0.03, 0.03, M("grey_light"), 6))
    elif s == "space":
        o += [ring(n + "_rim", H + V((0, -0.08, 0.0)), 0.74, 0.07, M("silver"), axis=(0, -1, 0.2), scale=(1, 1.08, 1))]
        o += [cap(n + "_back", M("white"), 0.8, -10, lat1=90, tilt=80, segs=20)]
        o.append(C.cyl(n + "_ant", H + V((0.35, 0.1, 0.6)), H + V((0.45, 0.15, 1.0)), 0.025, 0.02, M("grey"), 6))
        o.append(ball(n + "_ab", H + V((0.45, 0.15, 1.02)), 0.06, M("glow_red", "#ff3a3a", 2.0)))
        o.append(ring(n + "_neck", H + V((0, 0.0, -0.58)), 0.6, 0.08, M("silver")))
    elif s == "gladiator":
        o += [cap(n + "_c", M("gold_dark"), 0.69, -25, tilt=40)]
        for i in range(7):
            a = math.radians(-60 + i * 20)
            p = H + V((0, 0.69 * math.sin(a) + 0.0, 0.69 * math.cos(a)))
            o.append(C.box(n + "_cr%d" % i, p + V((0, 0, 0.14)), (0.08, 0.16, 0.3), M("red")))
        o.append(C.box(n + "_nose", H + V((0, -0.7, 0.18)), (0.08, 0.06, 0.32), M("gold_dark"), bevel=0.02))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_ch%d" % sx, H + V((0.58 * sx, -0.12, -0.12)), (0.07, 0.16, 0.22), M("gold_dark"), 10, 6))
    elif s == "king":
        base = H + V((0, 0.03, 0.42))
        o.append(C.cyl(n + "_r", base, base + V((0, 0, 0.18)), 0.48, 0.5, M("gold"), 20))
        o.append(C.cyl(n + "_v", base + V((0, 0, 0.05)), base + V((0, 0, 0.12)), 0.47, 0.47, M("red"), 20))
        for i in range(8):
            a = 2 * math.pi * i / 8
            p = base + V((0.47 * math.cos(a), 0.47 * math.sin(a), 0.18))
            o.append(C.cyl(n + "_s%d" % i, p, p + V((0, 0, 0.22)), 0.08, 0.0, M("gold"), 6))
            o.append(ball(n + "_j%d" % i, p + V((0, 0, 0.24)), 0.035, M("gold"), 6, 4))
            if i % 2 == 0:
                o.append(ball(n + "_g%d" % i, p + V((0, 0, -0.08)), 0.05, M("blue" if i % 4 else "red"), 8, 5))
    elif s == "dark_assassin":
        o += [cap(n + "_hood", M("grey_dark"), 0.72, -35, tilt=75, squash=(1, 1.05, 1.0))]
        o.append(C.sphere(n + "_m", H + V((0, -0.22, -0.3)), (0.58, 0.48, 0.24), M("grey_dark"), 16, 8))
        o.append(ring(n + "_band", H + V((0, 0.0, 0.32)), 0.62, 0.05, M("red"), axis=(0, 0.3, 1)))
        for sx in (-1, 1):
            o.append(C.box(n + "_t%d" % sx, H + V((0.12 * sx, 0.7, 0.15)), (0.08, 0.04, 0.4), M("red"),
                           rot=Matrix.Rotation(0.5 * sx, 4, "Y")))
    elif s == "elvis":
        o.append(C.sphere(n + "_hair", H + V((0, 0.05, 0.38)), (0.66, 0.66, 0.36), M("gun_dark"), 16, 8))
        o.append(C.sphere(n + "_pomp", H + V((0, -0.45, 0.48)), (0.42, 0.3, 0.24), M("gun_dark"), 14, 8,
                          rot=Matrix.Rotation(0.4, 4, "X")))
        for sx in (-1, 1):
            o.append(C.box(n + "_sb%d" % sx, H + V((0.6 * sx, -0.1, -0.1)), (0.08, 0.2, 0.36), M("gun_dark"), bevel=0.03))
            o.append(C.sphere(n + "_gl%d" % sx, H + V((0.2 * sx, -0.66, 0.1)), (0.17, 0.05, 0.13), M("gold_dark"), 12, 6))
        o.append(C.box(n + "_br", H + V((0, -0.68, 0.16)), (0.5, 0.03, 0.03), M("gold")))
    elif item.startswith("army_helmet"):
        team = "blue" if item.endswith("blue") else "red"
        o += [cap(n + "_c", M("olive"), 0.7, -8, tilt=15, squash=(1, 1.02, 0.95))]
        o.append(C.cyl(n + "_br", H + V((0, 0.05, -0.02)), H + V((0, 0.05, 0.02)), 0.78, 0.74, M("olive"), 24))
        o.append(ring(n + "_bd", H + V((0, 0.04, 0.2)), 0.69, 0.05, M(team), axis=(0, 0.25, 1)))
        st = star(n + "_st", V((0, 0, 0)), 0.11, 0.03, M(team))
        st.data.transform(Matrix.Translation(H + V((0, -0.66, 0.32))) @ Matrix.Rotation(-0.4, 4, "X"))
        o.append(st)
    elif item == "RedHat":
        o += [cap(n + "_c", M("red"), 0.68, 8, tilt=8, squash=(1, 1, 1.15))]
        o.append(ring(n + "_f", H + V((0, 0.05, 0.12)), 0.67, 0.09, M("white"), axis=(0, 0.14, 1)))
        o.append(ball(n + "_p", H + V((0, 0.12, 0.85)), 0.15, M("white"), 12, 8))
    else:
        o += [cap(n + "_c", M("grey"), 0.68, 10)]
    return o


# ------------------------------------------------------------------------------------------ chest items

def buttons(n, mat, zs, k=1.08, r=0.045):
    return [ball(n + "_bt%d" % i, front(z, k), r, mat, 8, 5) for i, z in enumerate(zs)]


def collar(n, mat, z=1.42, r=0.58, th=0.08):
    return ring(n + "_col", V((0, 0, z)), r, th, mat, scale=(1, 0.98, 1))


def chest_item(item):
    s = item.rsplit("_", 1)[0] if item.endswith("_chest") else item
    n = item
    o = []
    if s == "flannel":
        o.append(shell(n + "_s", 0.35, 1.5, M("plaid_red")))
        for i, z in enumerate((0.5, 0.8, 1.1, 1.35)):
            o.append(shell(n + "_b%d" % i, z, z + 0.07, M("red_dark"), k=1.085, n=1))
        o.append(collar(n, M("red_dark")))
        o += buttons(n, M("cream"), (0.6, 0.85, 1.1))
    elif s == "paper":
        o.append(C.box(n + "_box", V((0, -0.05, 0.85)), (1.75, 1.65, 1.15), M("sand_dark"), bevel=0.04))
        o.append(C.box(n + "_tape", V((0, -0.05, 0.85)), (1.77, 1.67, 0.14), M("tan")))
        o.append(C.box(n + "_lbl", V((0.3, -0.89, 0.6)), (0.4, 0.02, 0.28), M("white")))
        for sx in (-1, 1):
            o.append(C.box(n + "_st%d" % sx, V((0.4 * sx, -0.05, 1.47)), (0.12, 1.2, 0.08), M("tan")))
    elif s == "rain":
        o.append(shell(n + "_s", 0.2, 1.5, M("yellow"), flare=0.1))
        o.append(collar(n, M("yellow"), r=0.6, th=0.1))
        for i, z in enumerate((0.45, 0.75, 1.05)):
            o.append(C.box(n + "_tg%d" % i, front(z, 1.1), (0.18, 0.04, 0.05), M("brown_dark"), bevel=0.01))
        o.append(C.box(n + "_pk", front(0.5, 1.09, ang=30), (0.3, 0.03, 0.04), M("orange_dark")))
    elif s == "clown":
        o.append(shell(n + "_s", 0.35, 1.5, M("white")))
        cols = ["red", "blue", "green", "yellow", "purple"]
        k = 0
        for z in (0.5, 0.8, 1.1, 1.35):
            for ang in range(-120 if z < 1.2 else -90, 121, 60):
                o.append(C.sphere(n + "_d%d" % k, front(z + 0.04 * (k % 2), 1.075, ang=ang + 20 * (k % 2)), (0.1, 0.1, 0.1),
                                  M(cols[k % len(cols)]), 6, 3))
                k += 1
        for i in range(14):
            a = 2 * math.pi * i / 14
            o.append(C.sphere(n + "_r%d" % i, V((0.6 * math.cos(a), 0.6 * math.sin(a), 1.45)), (0.14, 0.14, 0.08),
                              M("white" if i % 2 else "yellow"), 6, 4))
        o += buttons(n, M("red"), (0.7, 1.0), r=0.08)
    elif s == "sm":
        o.append(shell(n + "_s", 0.35, 1.5, M("blue")))
        o.append(shell(n + "_belt", 0.42, 0.52, M("yellow"), k=1.09, n=1))
        em = C.extrude(n + "_em", [(0, 0.22), (0.24, 0.02), (0, -0.22), (-0.24, 0.02)], 0.04, M("yellow"))
        em.data.transform(Matrix.Translation(front(1.0, 1.09)))
        o.append(em)
        s2 = C.extrude(n + "_em2", [(-0.07, 0.1), (0.1, 0.1), (0.1, 0.0), (-0.07, -0.05), (0.1, -0.1), (-0.1, -0.1), (-0.1, 0.0), (0.07, 0.05)], 0.02, M("red"))
        s2.data.transform(Matrix.Translation(front(1.0, 1.09) + V((0, -0.03, 0))))
        o.append(s2)
        o.append(cape(n + "_cape", M("red")))
        o.append(collar(n, M("red")))
    elif s == "polar":
        o.append(shell(n + "_s", 0.25, 1.5, M("cream"), flare=0.08, k=1.1))
        o.append(ring(n + "_fur", V((0, 0, 1.45)), 0.6, 0.14, M("tan")))
        o.append(ring(n + "_hem", V((0, 0, 0.27)), body_r(0.27) * 1.1 + 0.08, 0.08, M("tan")))
        o.append(C.box(n + "_zip", front(0.85, 1.1), (0.05, 0.03, 1.0), M("grey")))
    elif s == "hockey":
        o.append(shell(n + "_s", 0.3, 1.5, M("red"), flare=0.06))
        for i, z in enumerate((0.45, 0.6)):
            o.append(shell(n + "_b%d" % i, z, z + 0.08, M("white"), k=1.085, n=1, flare=0.05))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_sp%d" % sx, V((0.55 * sx, 0, 1.38)), (0.36, 0.42, 0.24), M("red_dark"), 12, 8))
        num = C.box(n + "_n", front(1.0, 1.085), (0.12, 0.03, 0.4), M("white"))
        o.append(num)
    elif s == "cowboy":
        o.append(shell(n + "_s", 0.45, 1.45, M("cream"), k=1.06))
        for sx in (-1, 1):
            rows = []
            for i in range(5):
                z = 0.45 + i * 0.25
                row = []
                for j in range(5):
                    ang = sx * (20 + j * 30)
                    row.append(front(z, 1.1, ang=ang))
                rows.append(row)
            o.append(sheet(n + "_v%d" % sx, M("brown"), rows, 0.03))
        o.append(C.cyl(n + "_band", front(1.42, 1.0) + V((0, 0, 0)), front(1.2, 1.12), 0.3, 0.02, M("red"), 4))
        o.append(ring(n + "_neck", V((0, 0, 1.43)), 0.58, 0.07, M("red")))
        st = star(n + "_st", V((0, 0, 0)), 0.12, 0.03, M("gold"))
        st.data.transform(Matrix.Translation(front(1.05, 1.13, ang=35)))
        o.append(st)
    elif s == "schoolgirl":
        o.append(shell(n + "_s", 0.55, 1.5, M("white")))
        o.append(shell(n + "_sk", 0.18, 0.6, M("navy"), flare=0.18, k=1.1))
        o.append(C.box(n + "_col", V((0, 0.55, 1.35)), (0.9, 0.3, 0.4), M("navy"), rot=Matrix.Rotation(-0.35, 4, "X")))
        o.append(ring(n + "_cl", V((0, 0, 1.44)), 0.58, 0.07, M("navy")))
        c = front(1.2, 1.1)
        o.append(ball(n + "_k", c, 0.07, M("red")))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_w%d" % sx, c, c + V((0.25 * sx, -0.02, 0.05)), 0.02, 0.12, M("red"), 10))
            o.append(C.cyl(n + "_t%d" % sx, c, c + V((0.1 * sx, -0.04, -0.3)), 0.03, 0.06, M("red"), 8))
    elif s == "wizard":
        o.append(shell(n + "_s", 0.05, 1.5, M("purple"), flare=0.25))
        o.append(shell(n + "_belt", 0.62, 0.7, M("yellow"), k=1.09, n=1))
        o.append(ring(n + "_col", V((0, 0, 1.43)), 0.6, 0.09, M("yellow")))
        for i, (z, a) in enumerate(((0.3, -30), (0.95, 25), (0.4, 50), (1.2, -40))):
            st = star(n + "_st%d" % i, V((0, 0, 0)), 0.08, 0.02, M("yellow"))
            p = front(z, 1.1, ang=a)
            st.data.transform(Matrix.Translation(p) @ Matrix.Rotation(math.radians(a), 4, "Z"))
            o.append(st)
    elif s == "welder":
        o.append(shell(n + "_s", 0.45, 1.45, M("denim"), k=1.05))
        rows = []
        for i in range(6):
            z = 0.2 + i * 0.22
            rows.append([front(z, 1.12, ang=a) for a in (-55, -30, -10, 10, 30, 55)])
        o.append(sheet(n + "_ap", M("brown"), rows, 0.04))
        o.append(C.box(n + "_pk", front(0.7, 1.16), (0.42, 0.03, 0.22), M("brown_dark"), bevel=0.01))
        o.append(ring(n + "_strap", V((0, 0.0, 1.3)), 0.68, 0.035, M("brown_dark"), axis=(0, -0.4, 1)))
    elif s == "tuxedo":
        o.append(shell(n + "_s", 0.3, 1.5, M("gun_dark")))
        rows = []
        for i in range(5):
            z = 0.55 + i * 0.22
            w = 22 - i * 2
            rows.append([front(z, 1.085, ang=a) for a in (-w, -w / 2, 0, w / 2, w)])
        o.append(sheet(n + "_shirt", M("white"), rows, 0.02))
        c = front(1.42, 1.1)
        o.append(ball(n + "_k", c, 0.06, M("red")))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_w%d" % sx, c, c + V((0.22 * sx, -0.01, 0.0)), 0.02, 0.1, M("red"), 10))
        o += buttons(n, M("gun_dark"), (0.75, 0.95, 1.15), k=1.1, r=0.035)
        o.append(C.box(n + "_tl", V((0, 0.55, 0.2)), (0.6, 0.15, 0.5), M("gun_dark"), bevel=0.04))
    elif s == "bunny":
        o.append(shell(n + "_s", 0.3, 1.5, M("pink")))
        o.append(C.sphere(n + "_belly", front(0.85, 1.0), (0.45, 0.12, 0.5), M("white"), 14, 8))
        o.append(ball(n + "_tail", V((0, 0.82, 0.4)), 0.2, M("white"), 12, 8))
        o.append(ring(n + "_col", V((0, 0, 1.43)), 0.58, 0.08, M("white")))
    elif s == "desert":
        o.append(shell(n + "_s", 0.4, 1.5, M("khaki")))
        o.append(shell(n + "_belt", 0.5, 0.6, M("brown"), k=1.09, n=1))
        for i, a in enumerate((-50, -20, 20, 50)):
            o.append(C.box(n + "_po%d" % i, front(0.55, 1.12, ang=a), (0.16, 0.08, 0.16), M("sand_dark"), bevel=0.02))
        o.append(ring(n + "_scarf", V((0, 0, 1.42)), 0.6, 0.11, M("cream")))
        for sx in (-1, 1):
            o.append(C.box(n + "_pk%d" % sx, front(1.05, 1.09, ang=28 * sx), (0.24, 0.03, 0.2), M("sand_dark"), bevel=0.01))
    elif s == "specialforce":
        o.append(shell(n + "_s", 0.35, 1.5, M("gun")))
        o.append(shell(n + "_vest", 0.6, 1.35, M("olive_dark"), k=1.12, n=4))
        for i, a in enumerate((-35, 0, 35)):
            o.append(C.box(n + "_po%d" % i, front(0.8, 1.17, ang=a), (0.2, 0.1, 0.26), M("olive"), bevel=0.02))
        o.append(shell(n + "_belt", 0.45, 0.55, M("gun_dark"), k=1.1, n=1))
        o.append(C.box(n + "_bk", front(0.5, 1.12), (0.12, 0.03, 0.08), M("grey")))
    elif s == "football":
        o.append(shell(n + "_s", 0.35, 1.45, M("blue")))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_sp%d" % sx, V((0.5 * sx, -0.05, 1.42)), (0.42, 0.5, 0.2), M("blue_dark"), 14, 8))
            o.append(C.box(n + "_sl%d" % sx, V((0.65 * sx, -0.1, 1.3)), (0.05, 0.4, 0.06), M("white")))
        for x in (-0.09, 0.09):
            o.append(C.box(n + "_n%d" % (x > 0), front(0.95, 1.09) + V((x, 0, 0)), (0.07, 0.03, 0.4), M("white")))
    elif s == "space":
        o.append(shell(n + "_s", 0.3, 1.45, M("white"), k=1.1))
        o.append(C.box(n + "_pan", front(0.95, 1.12), (0.5, 0.06, 0.36), M("grey_light"), bevel=0.03))
        for i, c in enumerate(("red", "green", "blue")):
            o.append(C.cyl(n + "_b%d" % i, front(1.0, 1.15) + V((-0.14 + 0.14 * i, 0, 0)),
                           front(1.0, 1.15) + V((-0.14 + 0.14 * i, -0.04, 0)), 0.04, 0.04, M("glow_" + c, C.PALETTE[c], 1.0), 8))
        o.append(C.box(n + "_scr", front(0.86, 1.17), (0.3, 0.02, 0.08), M("glow_cyan", "#4fd8e8", 1.0)))
        o.append(C.box(n + "_pack", V((0, 0.85, 0.95)), (0.9, 0.35, 0.9), M("grey_light"), bevel=0.06))
        o.append(shell(n + "_belt", 0.42, 0.52, M("grey"), k=1.12, n=1))
    elif s == "gladiator":
        o.append(shell(n + "_s", 0.55, 1.45, M("gold_dark"), k=1.08))
        o.append(C.sphere(n + "_pec", front(1.1, 0.95), (0.45, 0.2, 0.25), M("gold_dark"), 14, 8))
        for i in range(12):
            ang = -150 + i * 27
            p = front(0.4, 1.1, ang=ang)
            o.append(C.box(n + "_pt%d" % i, p, (0.16, 0.06, 0.32), M("brown"),
                           rot=Matrix.Rotation(math.radians(ang), 4, "Z")))
        o.append(C.sphere(n + "_pa", V((-0.62, 0, 1.36)), (0.3, 0.34, 0.2), M("gold_dark"), 12, 8))
        o.append(ring(n + "_strap", V((0, 0, 1.0)), 0.8, 0.04, M("brown"), axis=(0.6, 0, 1)))
    elif s == "king":
        o.append(cape(n + "_cape", M("red"), z_top=1.48, z_bot=0.05, a0=60, a1=300, spread=0.32))
        o.append(ring(n + "_erm", V((0, 0, 1.45)), 0.62, 0.15, M("white")))
        for i in range(8):
            a = 2 * math.pi * i / 8
            o.append(ball(n + "_ed%d" % i, V((0.62 * math.cos(a), 0.62 * math.sin(a) - 0.0, 1.55)), 0.035, M("black"), 6, 4))
        o.append(C.cyl(n + "_med", front(1.1, 1.1), front(1.1, 1.1) + V((0, -0.04, 0)), 0.14, 0.14, M("gold"), 14))
        o.append(ball(n + "_gem", front(1.1, 1.1) + V((0, -0.05, 0)), 0.06, M("red"), 8, 5))
        for i in range(5):
            o.append(ball(n + "_ch%d" % i, front(1.42 - i * 0.07, 1.06, ang=-30 + 15 * i) + V((0, 0, -0.08 * (2 - abs(i - 2)))), 0.035,
                          M("gold"), 6, 4))
    elif s == "dark_assassin":
        o.append(shell(n + "_s", 0.15, 1.5, M("grey_dark"), flare=0.15))
        o.append(shell(n + "_sash", 0.55, 0.68, M("red"), k=1.09, n=1, flare=0.03))
        o.append(C.box(n + "_tie", front(0.6, 1.12, ang=30), (0.1, 0.05, 0.4), M("red")))
        st = star(n + "_shu", V((0, 0, 0)), 0.1, 0.02, M("steel"), points=4, inner=0.3)
        st.data.transform(Matrix.Translation(front(0.62, 1.13, ang=-30)))
        o.append(st)
        rows = [[front(z, 1.1, ang=a) for a in (-30, -10, 10, 30)] for z in (1.0, 1.2, 1.4)]
        o.append(sheet(n + "_wrap", M("black"), rows, 0.02))
    elif s == "elvis":
        o.append(shell(n + "_s", 0.2, 1.5, M("white"), flare=0.1))
        o.append(C.cyl(n + "_col", V((0, 0.12, 1.4)), V((0, 0.2, 1.62)), 0.6, 0.66, M("white"), 20))
        o.append(shell(n + "_belt", 0.48, 0.62, M("gold"), k=1.09, n=1))
        o.append(C.box(n + "_bk", front(0.55, 1.12), (0.3, 0.04, 0.2), M("gold"), bevel=0.02))
        for i in range(10):
            ang = -60 + i * 13
            o.append(ball(n + "_r%d" % i, front(1.25 - abs(i - 4.5) * 0.06, 1.09, ang=ang), 0.03, M("gold"), 6, 4))
        rows = []
        for i in range(4):
            z = 0.75 + i * 0.22
            w = 8 + i * 6
            rows.append([front(z, 1.08, ang=a) for a in (-w, 0, w)])
        o.append(sheet(n + "_v", M("cream"), rows, 0.01))
    elif item.startswith("army_jacket"):
        team = "blue" if item.endswith("blue") else "red"
        o.append(shell(n + "_s", 0.3, 1.5, M("olive")))
        o.append(collar(n, M("olive_dark")))
        for sx in (-1, 1):
            o.append(C.box(n + "_pk%d" % sx, front(1.05, 1.09, ang=30 * sx), (0.26, 0.04, 0.22), M("olive_dark"), bevel=0.015))
        o += buttons(n, M("olive_dark"), (0.5, 0.75, 1.0, 1.25))
        o.append(C.cyl(n + "_arm", V((0, 0, 0.32)), V((0, 0, 0.42)), body_r(0.37) * 1.1, body_r(0.37) * 1.1, M(team), 20))
        o.append(C.box(n + "_patch", front(1.25, 1.1, ang=-35), (0.18, 0.04, 0.14), M(team)))
    elif item == "RedSweater":
        o.append(shell(n + "_s", 0.3, 1.5, M("red")))
        o.append(C.cyl(n + "_tn", V((0, 0, 1.38)), V((0, 0, 1.6)), 0.58, 0.55, M("red"), 20))
        for i, z in enumerate((0.85, 1.05)):
            o.append(shell(n + "_b%d" % i, z, z + 0.06, M("white"), k=1.085, n=1))
        for i in range(10):
            ang = -90 + i * 20
            o.append(C.box(n + "_d%d" % i, front(0.95, 1.09, ang=ang), (0.08, 0.02, 0.08), M("white"),
                           rot=Matrix.Rotation(math.radians(ang), 4, "Z") @ Matrix.Rotation(0.785, 4, "Y")))
        o.append(shell(n + "_hem", 0.3, 0.38, M("red_dark"), k=1.09, n=1))
    else:
        o.append(shell(n + "_s", 0.35, 1.5, M("grey")))
    return o


# ------------------------------------------------------------------------------------------ feet items

def feet_item(item):
    s = item.rsplit("_", 1)[0] if item.endswith("_feet") else item
    n = item
    o = []
    if s == "flannel":
        o += shoe(n, M("brown"), shaft=0.2, sole="brown_dark", cuff="red_dark", laces="tan")
    elif s == "paper":
        o += shoe(n, M("cream"), height=0.6, sole="grey_light")
        o.append(C.box(n + "_x", ANK + V((0, -0.35, 0.03)), (0.25, 0.01, 0.02), M("grey")))
    elif s == "rain":
        o += shoe(n, M("green"), shaft=0.32, sole="green_dark", cuff="green_dark")
    elif s == "clown":
        o += shoe(n, M("red"), length=1.35, width=1.25, height=1.15, sole="yellow")
        o.append(ball(n + "_pp", ANK + V((0, -0.3, 0.12)), 0.08, M("yellow")))
    elif s == "sm":
        o += shoe(n, M("red"), shaft=0.28, cuff="yellow")
    elif s == "polar":
        o += shoe(n, M("tan"), shaft=0.22, width=1.1, sole="brown", cuff="cream")
    elif s == "hockey" or item == "Skates":
        o += shoe(n, M("gun_dark") if item == "Skates" else M("white"), shaft=0.25, laces="white" if item == "Skates" else "red")
        o.append(C.box(n + "_bl", ANK + V((0, -0.24, -0.17)), (0.04, 0.85, 0.06), M("silver"), bevel=0.015))
        for y in (0.0, -0.45):
            o.append(C.box(n + "_ps%d" % (y < 0), ANK + V((0, -0.24 + 0.2 + y * 0.9, -0.11)), (0.05, 0.06, 0.1), M("steel")))
    elif s == "cowboy":
        o += shoe(n, M("brown"), shaft=0.32, toe_up=0.2, sole="brown_dark", cuff="tan")
        st = star(n + "_sp", V((0, 0, 0)), 0.08, 0.02, M("gold"), points=6)
        st.data.transform(Matrix.Translation(ANK + V((0, 0.22, 0.0))) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
        o.append(st)
    elif s == "schoolgirl":
        o += shoe(n, M("gun_dark"), height=0.8, shaft=0.18, shaft_mat=M("white"), cuff="white")
        o.append(C.box(n + "_strap", ANK + V((0, -0.2, 0.05)), (0.42, 0.06, 0.03), M("gun_dark")))
    elif s == "wizard":
        o += shoe(n, M("purple"), length=1.2, height=0.8, toe_up=0.5)
        o.append(ball(n + "_bell", ANK + V((0, -0.72, 0.33)), 0.06, M("yellow")))
    elif s == "welder":
        o += shoe(n, M("brown_dark"), shaft=0.15, width=1.1, sole="gun_dark", toe="steel")
    elif s == "tuxedo":
        o += shoe(n, M("gun_dark"), height=0.85, sole="black")
        o.append(ring(n + "_sock", ANK + V((0, 0.0, 0.05)), 0.16, 0.05, M("white")))
    elif s == "bunny":
        o += shoe(n, M("pink"), width=1.15, height=1.1)
        for sx in (-1, 1):
            o.append(C.sphere(n + "_e%d" % sx, ANK + V((0.1 * sx, -0.4, 0.3)), (0.06, 0.04, 0.16), M("white"), 8, 6,
                              rot=Matrix.Rotation(0.4 * sx, 4, "Y")))
        o.append(ball(n + "_n", ANK + V((0, -0.62, 0.08)), 0.05, M("rose")))
    elif s == "desert":
        o.append(C.sphere(n + "_sole", ANK + V((0, -0.24, -0.1)), (0.26, 0.44, 0.04), M("brown"), 14, 6))
        for i, y in enumerate((-0.05, -0.3, -0.5)):
            o.append(C.box(n + "_st%d" % i, ANK + V((0, y, -0.02)), (0.44, 0.06, 0.05), M("brown_dark")))
        o.append(ring(n + "_a", ANK + V((0, 0, 0.02)), 0.17, 0.035, M("brown_dark")))
    elif s == "specialforce":
        o += shoe(n, M("gun_dark"), shaft=0.25, sole="black", laces="grey")
    elif s == "football":
        o += shoe(n, M("white"), height=0.9, sole="gun_dark")
        for i, (x, y) in enumerate(((-0.12, -0.05), (0.12, -0.05), (-0.12, -0.45), (0.12, -0.45), (0, -0.62))):
            o.append(C.cyl(n + "_cl%d" % i, ANK + V((x, y, -0.15)), ANK + V((x, y, -0.22)), 0.04, 0.025, M("gun_dark"), 6))
        o.append(C.box(n + "_s", ANK + V((0.24, -0.25, 0.0)), (0.02, 0.3, 0.05), M("blue")))
    elif s == "space":
        o += shoe(n, M("white"), width=1.25, height=1.4, shaft=0.25, sole="grey", cuff="silver")
    elif s == "gladiator":
        o.append(C.sphere(n + "_sole", ANK + V((0, -0.24, -0.1)), (0.26, 0.44, 0.04), M("brown_dark"), 14, 6))
        for i, z in enumerate((0.0, 0.1, 0.2, 0.3)):
            o.append(ring(n + "_r%d" % i, ANK + V((0, 0.0, z)), 0.18, 0.025, M("brown")))
        o.append(C.box(n + "_t", ANK + V((0, -0.3, -0.02)), (0.05, 0.4, 0.04), M("brown")))
    elif s == "king":
        o += shoe(n, M("gold"), toe_up=0.15, sole="gold_dark")
        o.append(ball(n + "_g", ANK + V((0, -0.4, 0.13)), 0.06, M("red")))
    elif s == "dark_assassin":
        o += shoe(n, M("grey_dark"), height=0.85, shaft=0.2)
        for i, z in enumerate((0.0, 0.08, 0.16)):
            o.append(ring(n + "_w%d" % i, ANK + V((0, 0.0, z)), 0.2, 0.025, M("black")))
    elif s == "elvis":
        o += shoe(n, M("denim"), height=0.85, sole="cream")
        o.append(ring(n + "_sock", ANK + V((0, 0.0, 0.05)), 0.16, 0.05, M("white")))
    elif item.startswith("army_boots"):
        team = "blue" if item.endswith("blue") else "red"
        o += shoe(n, M("olive_dark"), shaft=0.26, sole="gun_dark", laces=team, cuff=team)
    else:
        o += shoe(n, M("grey"))
    return o


# ------------------------------------------------------------------------------------------ build

def build_item(item, slot):
    if slot == "head":
        objs = head_item(item)
    elif slot == "chest":
        objs = chest_item(item)
    else:
        objs = feet_item(item)
    ob = C.join(objs, item)
    sock = SOCKET[slot]
    ob.data.transform(Matrix.Translation(-sock))
    ob.data.transform(P.yaw_matrix().to_4x4())
    ob.data.update()
    return ob


def run(only=None):
    C.reset()
    items = all_items()
    cols = {}
    stats = {}
    for item, slot in items.items():
        if only and item not in only:
            continue
        col = C.new_collection(item)
        C.use_collection(col)
        ob = build_item(item, slot)
        C.use_collection(None)
        stats[item] = C.tri_count([ob])
        C.export_collection(col, os.path.join(C.MODELS, "Clothes", item + ".fbx"))
        cols[item] = col
    C.save_blend("clothes")
    over = {k: v for k, v in stats.items() if v > 1500}
    print("[clothes] %d items, max tris %d, over budget: %s" % (len(stats), max(stats.values()), over))
    return cols


def place_on_penguin(ob, slot, mirror_feet=True):
    """Move an exported-orientation clothing object onto a penguin built by penguin.build (for previews)."""
    ob.location = P.socket_world(SOCKET_NAME[slot])
