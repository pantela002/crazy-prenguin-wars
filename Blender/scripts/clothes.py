"""Clothing models (Bonus ids + the glove items), textured (wear_kit.py: knit, plaid, denim, leather, fur, metal...).
Modeled in the penguin's canonical frame around the real body/head/feet/flipper geometry, then moved so the socket is
the origin and turned by the penguin's yaw, so a model parented to the socket with identity local transform fits.

Sockets: head items -> HeadSocket (head sphere center), chest items -> ChestSocket (body axis),
feet items -> FootSocketL (ankle; feet items are symmetric around the foot axis, so the same model is used on
FootSocketR, no mirroring needed), gloves -> one FBX with two objects: GloveL (origin = GloveSocketL) and GloveR
(origin = GloveSocketR), the left one mirrored.

Head items sit back on the head, above the big eyes (like the original game's hats); face pieces (masks, glasses,
goggles) are placed from the eye positions in penguin.py.
"""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math
import os

from mathutils import Matrix, Vector

import common as C
import penguin as P
import wear_kit as W

H = P.HEAD_C
HR = P.HEAD_R
ANK = P.ANKLE_L
V = Vector

SETS = ["flannel", "paper", "rain", "clown", "sm", "polar", "hockey", "cowboy", "schoolgirl", "wizard", "welder",
        "tuxedo", "bunny", "desert", "specialforce", "football", "space", "gladiator", "king", "dark_assassin", "elvis"]
EXTRA = {"army_helmet_blue": "head", "army_helmet_red": "head", "army_jacket_blue": "chest", "army_jacket_red": "chest",
         "army_boots_blue": "feet", "army_boots_red": "feet", "RedSweater": "chest", "Skates": "feet", "RedHat": "head"}
# glove items (new slot "hands"; ids also listed in Meta/ClothesCatalog.cs)
GLOVES = ["gloves_boxing", "gloves_mittens", "gloves_work", "gloves_cartoon", "gloves_gold"]


def all_items():
    """id -> slot for every clothing model (head, chest, feet, hands)."""
    out = {}
    for s in SETS:
        for slot in ("head", "chest", "feet"):
            out["%s_%s" % (s, slot)] = slot
    out.update(EXTRA)
    for g in GLOVES:
        out[g] = "hands"
    return out


SOCKET = {"head": H, "chest": P.CHEST, "feet": ANK, "hands": P.glove_socket("R")}
SOCKET_NAME = {"head": "HeadSocket", "chest": "ChestSocket", "feet": "FootSocketL", "hands": "GloveSocketR"}


# ------------------------------------------------------------------------------------------ materials

# default texture per palette color (override with M(color, tex))
_TEX_BY_COLOR = {
    "gold": "hammered", "gold_dark": "hammered", "silver": "metal", "steel": "metal", "steel_dark": "metal",
    "gun": "metal", "gun_dark": "leather", "metal": "metal", "metal_dark": "metal", "grey": "metal",
    "brown": "leather", "brown_dark": "leather", "tan": "fur", "cream": "wool", "denim": "denim",
    "khaki": "canvas", "olive": "canvas", "olive_dark": "canvas", "sand_dark": "paper", "plaid_red": "plaid",
    "black": "plastic", "pupil": "plastic", "grey_dark": "felt", "navy": "cotton", "pink": "felt",
    "yellow": "rubber", "purple": "satin", "white": "cotton", "grey_light": "plastic",
}
_ROUGH = {"rubber": 0.35, "plastic": 0.3, "metal": 0.3, "hammered": 0.28, "satin": 0.35, "sequin": 0.2, "glass": 0.1,
          "leather": 0.5}


def M(color, tex=None):
    """Textured material for a palette color (or '#hex'), with the color's default texture unless tex is given."""
    tex = tex or _TEX_BY_COLOR.get(color, "cotton")
    tint = "white" if tex in W.COLORED else color
    return W.tmat(tex, tint, rough=_ROUGH.get(tex, 0.65), spec=0.45 if tex in _ROUGH else 0.2)


def G(name, hexcol, strength=2.0):
    """Glowing material (lights, lenses): flat and bright in Unity (Glow prefix)."""
    return C.mat("Glow_" + name, hexcol, strength)


# ------------------------------------------------------------------------------------------ helpers

def body_r(z):
    prof = P.body_profile()
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            return r0 + (r1 - r0) * t
    return 0.0


YK = 0.95   # garments follow the body's depth (penguin.BODY_DEPTH plus room for the round belly)


def shell(name, z0, z1, mat, k=1.07, segs=24, flare=0.0, n=8, yk=YK, top_close=False):
    """Torso shell following the body profile from z0 (bottom) to z1 (top). flare widens the bottom."""
    prof = []
    for i in range(n + 1):
        z = z0 + (z1 - z0) * i / n
        f = flare * (1 - i / n) ** 2
        prof.append((max(body_r(z), 0.25) * k + f, z))
    if top_close:
        prof.append((0.0, z1 + 0.02))
    ob = C.lathe(name, prof, mat, segs=segs, scale=(1, yk, 1))
    W.uv_cyl(ob, radius=0.7)
    return ob


def front(z, k=1.07, yk=YK, ang=0.0):
    """Point on a shell surface at height z, ang degrees around from the front (+ = towards penguin's left)."""
    r = body_r(z) * k
    a = math.radians(-90 + ang)
    return V((r * math.cos(a), r * math.sin(a) * yk, z))


def cap(name, mat, r=None, lat0=0.0, tilt=0.0, segs=24, center=None, squash=(1.0, 0.97, 1.0), lat1=90.0):
    """Sphere cap around the head: from latitude lat0 (degrees, 0 = equator) to lat1 (90 = top).
    tilt > 0 tips the cap back so the rim is higher at the front."""
    center = H if center is None else center
    r = HR + 0.06 if r is None else r
    prof = []
    steps = 8
    for i in range(steps + 1):
        l = math.radians(lat0 + (lat1 - lat0) * i / steps)
        prof.append((r * math.cos(l), r * math.sin(l)))
    if lat1 >= 89.9:
        prof[-1] = (0.0, r)
    ax = V((0, math.sin(math.radians(tilt)), math.cos(math.radians(tilt))))
    ob = C.lathe(name, prof, mat, segs=segs, center=center, axis=ax, scale=squash)
    W.uv_sphere(ob, center=center, radius=r)
    return ob


def head_pt(lat, lon, r=HR):
    """Point on the head sphere: lat degrees up from equator, lon degrees from the front towards the left."""
    la, lo = math.radians(lat), math.radians(-90 + lon)
    return H + V((r * math.cos(la) * math.cos(lo), r * math.cos(la) * math.sin(lo), r * math.sin(la)))


def eye_c(sx):
    return H + V((P.EYE_OFF.x * sx, P.EYE_OFF.y, P.EYE_OFF.z))


def eye_out(sx):
    """Outward direction of an eye (from the head center)."""
    return (eye_c(sx) - H).normalized()


def disc(name, center, r, h, mat, segs=24):
    return C.cyl(name, center - V((0, 0, h / 2)), center + V((0, 0, h / 2)), r, r, mat, segs)


def ring(name, center, r, thick, mat, axis=(0, 0, 1), segs=24, scale=(1, 1, 1)):
    ob = C.torus(name, center, r, thick, mat, segs, 8, axis=axis, scale=scale)
    W.uv_cyl(ob, center=center, radius=r)
    return ob


def ball(name, c, r, mat, segs=12, rings=8):
    ob = C.sphere(name, c, r, mat, segs, rings)
    W.uv_sphere(ob, center=c)
    return ob


def star(name, center, r, depth, mat, points=5, inner=0.45):
    pts = []
    for i in range(points * 2):
        a = math.pi / 2 + i * math.pi / points
        rr = r if i % 2 == 0 else r * inner
        pts.append((rr * math.cos(a), rr * math.sin(a)))
    ob = C.extrude(name, pts, depth, mat, bevel=min(0.01, depth * 0.3))
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
    W.uv_cyl(ob, radius=0.7)
    return ob


def cape(name, mat, z_top=1.45, z_bot=0.15, a0=110, a1=250, spread=0.28):
    """Cape hanging behind the body. a0..a1: degrees around (0 = front, 180 = back)."""
    rows = []
    nz, na = 7, 12
    for i in range(nz + 1):
        t = i / nz
        z = z_top + (z_bot - z_top) * t
        r = max(body_r(z), 0.45) * 1.12 + spread * t
        row = []
        for j in range(na + 1):
            a = math.radians(-90 + a0 + (a1 - a0) * j / na)
            wav = 0.05 * math.sin(j * 1.7) * t
            row.append(V(((r + wav) * math.cos(a), (r + wav) * math.sin(a), z)))
        rows.append(row)
    return sheet(name, mat, rows, 0.04)


def collar(n, mat, z=1.44, th=0.08):
    return ring(n + "_col", V((0, 0, z)), body_r(z) * 1.04 + th * 0.6, th, mat, scale=(1, YK, 1))


def buttons(n, mat, zs, k=1.08, r=0.045):
    return [ball(n + "_bt%d" % i, front(z, k), r, mat, 10, 6) for i, z in enumerate(zs)]


def shoe(name, mat, length=1.0, height=1.0, width=1.0, shaft=0.0, shaft_mat=None, sole=None, cuff=None,
         toe=None, laces=None, toe_up=0.0):
    """A shoe around the left foot (origin handled later). Returns list of objects."""
    o = []
    c = ANK + V((0, -0.25 * length, -0.02))
    o.append(C.sphere(name + "_b", c, (0.25 * width, 0.44 * length, 0.15 * height), mat, 18, 10))
    if toe_up:
        o.append(C.sphere(name + "_tu", c + V((0, -0.43 * length, 0.08 + toe_up * 0.5)),
                          (0.1 * width, 0.12, 0.08 + toe_up * 0.3), mat, 12, 6))
    if sole:
        o.append(C.sphere(name + "_s", c + V((0, 0, -0.075 * height)), (0.27 * width, 0.46 * length, 0.07),
                          M(sole, "rubber"), 18, 6))
    if toe:
        o.append(C.sphere(name + "_t", c + V((0, -0.25 * length, 0.0)), (0.2 * width, 0.22 * length, 0.135 * height), M(toe), 14, 8))
    if shaft > 0:
        sm = shaft_mat or mat
        sh = C.cyl(name + "_sh", ANK + V((0, 0.02, -0.04)), ANK + V((0, 0.02, shaft)), 0.19 * width, 0.21 * width, sm, 18)
        W.uv_cyl(sh, center=ANK, radius=0.2)
        o.append(sh)
        if cuff:
            o.append(ring(name + "_c", ANK + V((0, 0.02, shaft)), 0.2 * width, 0.05, M(cuff)))
    elif cuff:
        o.append(ring(name + "_c", ANK + V((0, 0.0, 0.03)), 0.17 * width, 0.05, M(cuff)))
    if laces:
        for i in range(3):
            o.append(C.box(name + "_l%d" % i, c + V((0, -0.05 - 0.09 * i, 0.125 * height - 0.012 * i)),
                           (0.16, 0.025, 0.02), M(laces, "cotton")))
    return o


def _tilt(objs, deg, pivot, axis="X"):
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-pivot)
    for ob in objs:
        ob.data.transform(m)
    return objs


def fit_head(objs, back=20.0, lift=0.02, scale=None):
    """Hats are drawn for a head of radius 0.62 centred on H; shrink them to the penguin's head and tip them back
    (front up) so they sit above the eyes."""
    k = HR / 0.62 if scale is None else scale
    m = (Matrix.Translation(H + V((0, 0, lift))) @ Matrix.Rotation(math.radians(-back), 4, "X") @
         Matrix.Diagonal((k, k, k, 1)) @ Matrix.Translation(-H))
    for ob in objs:
        ob.data.transform(m)
    return objs


# ------------------------------------------------------------------------------------------ head items

def hat_brim(n, crown_mat, brim_mat=None, crown_h=0.45, crown_r=0.42, brim_r=0.75, band=None, top_r=None,
             z=0.48, tilt=-8, dent=False, brim_up=0.0):
    brim_mat = brim_mat or crown_mat
    base = H + V((0, 0.02, z))
    br = C.cyl(n + "_br", base - V((0, 0, 0.02)), base + V((0, 0, 0.02)), brim_r, brim_r, brim_mat, 32)
    W.uv_box(br)
    o = [br]
    if brim_up:
        o.append(ring(n + "_bu", base + V((0, 0, 0.03)), brim_r - 0.04, 0.05, brim_mat, scale=(1, 0.75, 1)))
    cr = C.cyl(n + "_cr", base, base + V((0, 0, crown_h)), crown_r, top_r or crown_r * 0.92, crown_mat, 28)
    W.uv_cyl(cr, center=base, radius=crown_r)
    o.append(cr)
    if dent:
        o.append(C.sphere(n + "_dt", base + V((0, 0, crown_h)), (crown_r * 0.85, crown_r * 0.6, 0.08), crown_mat, 16, 6))
    if band:
        bd = C.cyl(n + "_bd", base, base + V((0, 0, 0.1)), crown_r * 1.03, crown_r * 1.02, M(band, "satin"), 28)
        W.uv_cyl(bd, center=base, radius=crown_r)
        o.append(bd)
    return _tilt(o, tilt, base)


def earflaps(n, mat):
    o = []
    for sx in (-1, 1):
        ef = C.sphere(n + "_ef%d" % sx, H + V((0.6 * sx, 0.12, -0.12)), (0.1, 0.24, 0.3), mat, 14, 8)
        o.append(ef)
    return o


def domino_mask(n, mat):
    """Superhero eye mask: a padded ring around each eye joined by a bridge."""
    o = []
    for sx in (-1, 1):
        c = eye_c(sx)
        out = eye_out(sx)
        o.append(ring(n + "_m%d" % sx, c + out * 0.02, 0.2, 0.055, mat, axis=out, scale=(1.0, 1.25, 1.0)))
    o.append(C.cyl(n + "_br", eye_c(-1) + V((0.12, -0.08, 0.02)), eye_c(1) + V((-0.12, -0.08, 0.02)), 0.045, 0.045, mat, 8))
    return o


def head_item(item):
    s = item.rsplit("_", 1)[0] if item.endswith("_head") else item
    n = item
    o = []
    face = []        # pieces placed on the real face (not refitted)
    back, lift = 20.0, 0.02
    if s == "flannel":
        o += [cap(n + "_c", M("plaid_red"), 0.67, -5, tilt=12)]
        o += [ring(n + "_b%d" % i, H + V((0, 0.07, 0.12 + 0.17 * i)), 0.66 * math.cos(math.asin(min(0.95, (0.12 + 0.17 * i) / 0.67))), 0.025,
                   M("red_dark", "knit"), axis=(0, 0.2, 1)) for i in range(1)]
        o += earflaps(n, M("cream", "fur"))
        o += [ball(n + "_p", H + V((0, 0.05, 0.7)), 0.1, M("red_dark", "knit"))]
        back = 34
    elif s == "paper":
        pts = [(-0.7, 0), (0.7, 0), (0.44, 0.2), (0, 0.62), (-0.44, 0.2)]
        hat = C.extrude(n + "_h", pts, 0.5, M("cream", "paper"), bevel=0.015)
        hat.data.transform(Matrix.Translation(H + V((0, 0.05, 0.42))))
        W.uv_box(hat)
        o += [hat]
        for i in range(4):
            o.append(C.box(n + "_t%d" % i, H + V((-0.27 + 0.15 * i, -0.21, 0.6 + 0.06 * (i % 2))), (0.1, 0.01, 0.022), M("grey_dark", "felt")))
        o.append(C.box(n + "_b", H + V((0, -0.21, 0.47)), (1.25, 0.012, 0.06), M("blue", "paper")))
        back = 26
    elif s == "rain":
        o += [cap(n + "_c", M("yellow"), 0.68, 5, tilt=8)]
        brim = C.cyl(n + "_br", H + V((0, 0.08, 0.2)), H + V((0, 0.08, 0.24)), 0.9, 0.8, M("yellow"), 32)
        W.uv_box(brim)
        o += [brim]
        _tilt([brim], 14, H + V((0, 0, 0.22)))
        o += [C.cyl(n + "_s", H + V((0.55, -0.2, 0.0)), H + V((0.4, -0.45, -0.45)), 0.025, 0.025, M("yellow"), 6)]
        back, lift = 34, 0.04
    elif s == "clown":
        cols = ["red", "orange", "yellow", "green", "blue", "purple"]
        k = 0
        for lat in (10, 35, 60):
            for lon in range(60, 320, 40 if lat < 60 else 70):
                p = head_pt(lat, lon, 0.62 + 0.06)
                o.append(ball(n + "_a%d" % k, p, 0.22 if lat < 60 else 0.24, M(cols[k % len(cols)], "fur"), 9, 6))
                k += 1
        o.append(ball(n + "_top", H + V((0, 0.12, 0.72)), 0.25, M("red", "fur"), 14, 9))
        back = 16
        # the red nose sits on the bill tip
        face.append(ball(n + "_nose", H + V((0, -1.06, -0.1)), 0.13, M("red", "plastic"), 14, 9))
    elif s == "sm":
        # secret agent: domino mask over the eyes, cat ears and a tiny antenna curl
        face += domino_mask(n, M("navy", "satin"))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_k%d" % sx, H + V((0.25 * sx, 0.3, 0.52)), H + V((0.36 * sx, 0.42, 0.88)), 0.11, 0.0, M("navy", "satin"), 10))
        o.append(cap(n + "_band", M("navy", "satin"), 0.645, 20, lat1=34, tilt=40))
        back = 6
    elif s == "polar":
        o += [cap(n + "_c", M("cream", "knit"), 0.7, 0, tilt=10)]
        o += [ring(n + "_fur", H + V((0, 0.05, 0.12)), 0.67, 0.12, M("tan"), axis=(0, 0.18, 1))]
        o += earflaps(n, M("tan"))
        o += [ball(n + "_p", H + V((0, 0.05, 0.78)), 0.14, M("tan"))]
        back = 34
    elif s == "hockey":
        o += [cap(n + "_c", M("white", "plastic"), 0.68, -25, tilt=35)]
        for i in range(3):
            o.append(C.box(n + "_v%d" % i, H + V((-0.12 + 0.12 * i, 0.15, 0.62 - 0.03 * abs(i - 1))), (0.05, 0.5, 0.05), M("gun_dark", "plastic"),
                           rot=Matrix.Rotation(0.3, 4, "X")))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_e%d" % sx, H + V((0.62 * sx, 0.05, -0.05)), (0.08, 0.2, 0.2), M("white", "plastic"), 12, 8))
        o.append(C.box(n + "_s", H + V((0, -0.1, 0.66)), (0.12, 0.8, 0.06), M("red", "plastic"), rot=Matrix.Rotation(0.3, 4, "X")))
        o.append(ring(n + "_rim", H + V((0, 0.0, 0.0)), 0.66, 0.035, M("red", "plastic"), axis=(0, 0.6, 1)))
        back = 14
    elif s == "cowboy":
        o += hat_brim(n, M("brown", "felt"), M("brown", "felt"), 0.4, 0.4, 0.82, band="brown_dark", top_r=0.36, dent=True, brim_up=1)
        back, lift = 18, 0.05
    elif s == "schoolgirl":
        c = H + V((0.25, 0.05, 0.6))
        o.append(ball(n + "_k", c, 0.1, M("red", "satin")))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_w%d" % sx, c, c + V((0.38 * sx, 0.05, 0.12)), 0.03, 0.2, M("red", "satin"), 14))
            o.append(C.cyl(n + "_t%d" % sx, c, c + V((0.15 * sx, -0.05, -0.3)), 0.04, 0.08, M("red", "satin"), 10))
        o.append(cap(n + "_band", M("red", "satin"), 0.655, 30, lat1=38, tilt=-20))
        back = 24
    elif s == "wizard":
        base = H + V((0, 0.03, 0.4))
        br = C.cyl(n + "_br", base, base + V((0, 0, 0.04)), 0.85, 0.85, M("white", "starry"), 32)
        W.uv_box(br)
        o.append(br)
        cone = C.lathe(n + "_cone", [(0.5, 0), (0.42, 0.3), (0.28, 0.65), (0.12, 0.95), (0.0, 1.15)], M("white", "starry"), 20,
                       center=base)
        cone.data.transform(Matrix.Translation(base) @ Matrix.Rotation(-0.25, 4, "X") @ Matrix.Translation(-base))
        W.uv_cyl(cone, center=base, radius=0.4)
        o.append(cone)
        bd = C.cyl(n + "_bd", base, base + V((0, 0, 0.1)), 0.51, 0.49, M("yellow", "satin"), 28)
        W.uv_cyl(bd, center=base, radius=0.5)
        o.append(bd)
        back = 16
    elif s == "welder":
        o += [cap(n + "_c", M("grey_dark", "canvas"), 0.67, 10, tilt=-5)]
        mask = C.box(n + "_m", H + V((0, -0.42, 0.62)), (0.85, 0.12, 0.62), M("steel"), bevel=0.06, bevel_segs=2)
        win = C.box(n + "_w", H + V((0, -0.49, 0.64)), (0.5, 0.04, 0.16), G("lens_green", "#3a8a5a", 0.6), bevel=0.02)
        W.uv_box(mask)
        o += _tilt([mask, win], -70, H + V((0, -0.2, 0.45)))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_hg%d" % sx, H + V((0.62 * sx, -0.05, 0.25)), H + V((0.7 * sx, -0.05, 0.25)), 0.08, 0.08, M("gun", "metal"), 12))
        back = 24
    elif s == "tuxedo":
        o += hat_brim(n, M("gun_dark", "felt"), M("gun_dark", "felt"), 0.7, 0.36, 0.56, band="red", top_r=0.4, z=0.5, tilt=-10)
        back, lift = 14, 0.05
    elif s == "bunny":
        o.append(cap(n + "_band", M("pink", "fur"), 0.655, 52, lat1=60, tilt=10))
        for sx in (-1, 1):
            b = head_pt(55, 90 * sx * 0.5, 0.62) + V((0.12 * sx, 0.12, 0.0))
            ear = C.sphere(n + "_e%d" % sx, b + V((0.1 * sx, 0.0, 0.45)), (0.15, 0.08, 0.48), M("white", "fur"), 14, 8,
                           rot=Matrix.Rotation(-0.25 * sx, 4, "Y"))
            inner = C.sphere(n + "_i%d" % sx, b + V((0.1 * sx, -0.05, 0.42)), (0.09, 0.05, 0.36), M("pink", "felt"), 12, 6,
                             rot=Matrix.Rotation(-0.25 * sx, 4, "Y"))
            o += [ear, inner]
        back = 18
    elif s == "desert":
        o += [cap(n + "_c", M("khaki"), 0.7, 0, tilt=5, squash=(1, 1.05, 1.05))]
        br = C.cyl(n + "_br", H + V((0, 0.0, 0.1)), H + V((0, 0.0, 0.16)), 0.86, 0.72, M("khaki"), 32)
        W.uv_box(br)
        o.append(br)
        bd = C.cyl(n + "_bd", H + V((0, 0.0, 0.16)), H + V((0, 0.0, 0.26)), 0.71, 0.7, M("brown", "leather"), 32)
        W.uv_cyl(bd, center=H, radius=0.7)
        o.append(bd)
        o.append(ball(n + "_top", H + V((0, 0.0, 0.73)), 0.07, M("khaki")))
        back, lift = 26, 0.12
    elif s == "specialforce":
        o += [cap(n + "_c", M("white", "camo"), 0.69, -10, tilt=25)]
        o.append(C.box(n + "_mt", H + V((0, -0.52, 0.5)), (0.2, 0.12, 0.14), M("gun_dark", "plastic"), bevel=0.02))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_g%d" % sx, H + V((0.12 * sx, -0.55, 0.55)), H + V((0.13 * sx, -0.78, 0.53)), 0.08, 0.09, M("gun_dark", "plastic"), 14))
            o.append(C.cyl(n + "_gl%d" % sx, H + V((0.13 * sx, -0.78, 0.53)), H + V((0.13 * sx, -0.8, 0.53)), 0.075, 0.075, G("lens_lime", "#b6e83a"), 14))
        o.append(ring(n + "_strap", H + V((0, 0.0, 0.22)), 0.69, 0.035, M("gun_dark", "rubber"), axis=(0, 0.25, 1)))
        back = 22
    elif s == "football":
        o += [cap(n + "_c", M("blue", "plastic"), 0.7, -20, tilt=62)]
        o.append(C.box(n + "_st", H + V((0, 0.0, 0.7)), (0.16, 0.9, 0.05), M("white", "plastic"), rot=Matrix.Rotation(0.15, 4, "X")))
        back = 22
        # face mask bars under the bill, wrapping to the ear pieces
        for i, z in enumerate((-0.36, -0.5)):
            face.append(C.torus(n + "_fm%d" % i, H + V((0, -0.05, z)), 0.66, 0.028, M("grey_light", "metal"), 24, 6,
                                scale=(0.82, 1.0, 1.0)))
        for sx in (-1, 1):
            face.append(C.sphere(n + "_ep%d" % sx, H + V((0.55 * sx, -0.1, -0.18)), (0.07, 0.17, 0.2), M("blue", "plastic"), 12, 8))
    elif s == "space":
        # fishbowl rim around the face and a white shell behind, antenna with a light
        face.append(ring(n + "_rim", H + V((0, -0.52, 0.06)), 0.6, 0.07, M("silver"), axis=(0, -1, 0.15), scale=(1, 1.1, 1)))
        face.append(cap(n + "_back", M("white", "quilted"), 0.74, -10, lat1=90, tilt=80, segs=24))
        face.append(C.cyl(n + "_ant", H + V((0.35, 0.15, 0.62)), H + V((0.45, 0.2, 1.0)), 0.025, 0.02, M("grey", "metal"), 8))
        face.append(C.sphere(n + "_ab", H + V((0.45, 0.2, 1.02)), 0.065, G("light_red", "#ff3a3a"), 10, 6))
        face.append(ring(n + "_neck", H + V((0, 0.0, -0.56)), 0.56, 0.08, M("silver")))
    elif s == "gladiator":
        o += [cap(n + "_c", M("gold_dark"), 0.69, -12, tilt=66)]
        for i in range(9):
            a = math.radians(-70 + i * 17.5)
            p = H + V((0, 0.69 * math.sin(a), 0.69 * math.cos(a)))
            o.append(C.box(n + "_cr%d" % i, p + V((0, 0, 0.14)), (0.08, 0.16, 0.32), M("red", "fur"),
                           rot=Matrix.Rotation(a, 4, "X")))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_ch%d" % sx, H + V((0.6 * sx, -0.12, -0.12)), (0.07, 0.18, 0.24), M("gold_dark"), 12, 8))
        back = 18
    elif s == "king":
        base = H + V((0, 0.03, 0.42))
        r_ = C.cyl(n + "_r", base, base + V((0, 0, 0.18)), 0.48, 0.5, M("gold"), 28)
        W.uv_cyl(r_, center=base, radius=0.5)
        o.append(r_)
        v_ = C.cyl(n + "_v", base + V((0, 0, 0.05)), base + V((0, 0, 0.12)), 0.475, 0.475, M("red", "satin"), 28)
        o.append(ball(n + "_cap", base + V((0, 0, 0.08)), 0.44, M("red", "satin"), 16, 10))
        W.uv_cyl(v_, center=base, radius=0.5)
        o.append(v_)
        for i in range(8):
            a = 2 * math.pi * i / 8
            p = base + V((0.47 * math.cos(a), 0.47 * math.sin(a), 0.18))
            o.append(C.cyl(n + "_s%d" % i, p, p + V((0, 0, 0.22)), 0.08, 0.0, M("gold"), 8))
            o.append(ball(n + "_j%d" % i, p + V((0, 0, 0.24)), 0.035, M("gold"), 8, 5))
            if i % 2 == 0:
                o.append(ball(n + "_g%d" % i, p + V((0, 0, -0.08)), 0.05, M("blue" if i % 4 else "red", "glass"), 10, 6))
        back, lift = 14, 0.04
    elif s == "dark_assassin":
        o += [cap(n + "_hood", M("grey_dark", "canvas"), 0.72, -35, tilt=60, squash=(1, 1.05, 1.0))]
        o.append(ring(n + "_band", H + V((0, 0.0, 0.32)), 0.63, 0.05, M("red", "satin"), axis=(0, 0.3, 1)))
        for sx in (-1, 1):
            o.append(C.box(n + "_t%d" % sx, H + V((0.12 * sx, 0.7, 0.15)), (0.08, 0.04, 0.4), M("red", "satin"),
                           rot=Matrix.Rotation(0.5 * sx, 4, "Y")))
        back = 14
    elif s == "elvis":
        o.append(C.sphere(n + "_hair", H + V((0, 0.08, 0.4)), (0.66, 0.66, 0.36), M("gun_dark", "satin"), 18, 10))
        o.append(C.sphere(n + "_pomp", H + V((0, -0.38, 0.55)), (0.42, 0.3, 0.24), M("gun_dark", "satin"), 16, 9,
                          rot=Matrix.Rotation(0.4, 4, "X")))
        for sx in (-1, 1):
            o.append(C.box(n + "_sb%d" % sx, H + V((0.6 * sx, -0.05, -0.1)), (0.08, 0.2, 0.36), M("gun_dark", "satin"), bevel=0.03))
        back, lift = 14, 0.05
        # gold aviators over the eyes
        for sx in (-1, 1):
            c = eye_c(sx) + eye_out(sx) * 0.12
            lens = C.sphere(n + "_gl%d" % sx, c, (0.19, 0.05, 0.16), M("#3a2a10", "glass"), 16, 8,
                            rot=Vector((0, -1, 0)).rotation_difference(eye_out(sx)).to_matrix().to_4x4())
            face.append(lens)
            face.append(ring(n + "_fr%d" % sx, c, 0.19, 0.02, M("gold"), axis=eye_out(sx), scale=(1, 1, 0.85)))
        face.append(C.cyl(n + "_br", eye_c(-1) + eye_out(-1) * 0.12 + V((0.15, 0, 0.06)),
                          eye_c(1) + eye_out(1) * 0.12 + V((-0.15, 0, 0.06)), 0.018, 0.018, M("gold"), 6))
    elif item.startswith("army_helmet"):
        team = "blue" if item.endswith("blue") else "red"
        o += [cap(n + "_c", M("olive", "canvas"), 0.7, -8, tilt=15, squash=(1, 1.02, 0.95))]
        br = C.cyl(n + "_br", H + V((0, 0.05, -0.02)), H + V((0, 0.05, 0.02)), 0.78, 0.74, M("olive", "canvas"), 32)
        W.uv_box(br)
        o.append(br)
        o.append(ring(n + "_bd", H + V((0, 0.04, 0.2)), 0.69, 0.05, M(team, "canvas"), axis=(0, 0.25, 1)))
        st = star(n + "_st", V((0, 0, 0)), 0.12, 0.03, M(team, "plastic"))
        st.data.transform(Matrix.Translation(H + V((0, -0.66, 0.32))) @ Matrix.Rotation(-0.4, 4, "X"))
        o.append(st)
        back, lift = 30, 0.06
    elif item == "RedHat":
        o += [cap(n + "_c", M("red", "knit"), 0.68, 8, tilt=8, squash=(1, 1, 1.15))]
        o.append(ring(n + "_f", H + V((0, 0.05, 0.12)), 0.67, 0.09, M("white", "fur"), axis=(0, 0.14, 1)))
        o.append(ball(n + "_p", H + V((0, 0.12, 0.85)), 0.15, M("white", "fur"), 14, 9))
        back = 34
    else:
        o += [cap(n + "_c", M("grey"), 0.68, 10)]
    W.uv_auto(o)
    W.uv_auto(face)
    fit_head(o, back, lift)
    return o + face


# ------------------------------------------------------------------------------------------ chest items

def chest_item(item):
    s = item.rsplit("_", 1)[0] if item.endswith("_chest") else item
    n = item
    o = []
    if s == "flannel":
        o.append(shell(n + "_s", 0.35, 1.5, M("plaid_red")))
        o.append(collar(n, M("red_dark", "cotton")))
        o += buttons(n, M("cream", "plastic"), (0.6, 0.85, 1.1))
        for sx in (-1, 1):
            o.append(C.box(n + "_pk%d" % sx, front(1.12, 1.09, ang=30 * sx), (0.24, 0.03, 0.2), M("plaid_red"), bevel=0.01))
    elif s == "paper":
        o.append(C.box(n + "_box", V((0, -0.05, 0.85)), (1.65, 1.55, 1.15), M("sand_dark"), bevel=0.04))
        o.append(C.box(n + "_tape", V((0, -0.05, 0.85)), (1.67, 1.57, 0.14), M("tan", "paper")))
        o.append(C.box(n + "_lbl", V((0.3, -0.84, 0.6)), (0.4, 0.02, 0.28), M("white", "paper")))
        for sx in (-1, 1):
            o.append(C.box(n + "_st%d" % sx, V((0.4 * sx, -0.05, 1.47)), (0.12, 1.15, 0.08), M("tan", "paper")))
    elif s == "rain":
        o.append(shell(n + "_s", 0.2, 1.5, M("yellow"), flare=0.1))
        o.append(collar(n, M("yellow"), th=0.1))
        for i, z in enumerate((0.45, 0.75, 1.05)):
            o.append(C.box(n + "_tg%d" % i, front(z, 1.1), (0.18, 0.04, 0.05), M("brown_dark"), bevel=0.01))
        o.append(C.box(n + "_pk", front(0.5, 1.09, ang=30), (0.3, 0.03, 0.04), M("orange_dark", "rubber")))
    elif s == "clown":
        o.append(shell(n + "_s", 0.35, 1.5, M("white", "polka")))
        for i in range(14):
            a = 2 * math.pi * i / 14
            r = body_r(1.45) * 1.1
            o.append(C.sphere(n + "_r%d" % i, V((r * math.cos(a), r * math.sin(a) * YK, 1.45)), (0.15, 0.15, 0.08),
                              M("white" if i % 2 else "yellow", "satin"), 8, 5))
        o += buttons(n, M("red", "plastic"), (0.7, 1.0), r=0.08)
    elif s == "sm":
        o.append(shell(n + "_s", 0.35, 1.5, M("blue", "satin")))
        o.append(shell(n + "_belt", 0.42, 0.52, M("yellow", "leather"), k=1.09, n=1))
        em = C.extrude(n + "_em", [(0, 0.22), (0.24, 0.02), (0, -0.22), (-0.24, 0.02)], 0.04, M("yellow", "satin"))
        em.data.transform(Matrix.Translation(front(1.0, 1.09)))
        o.append(em)
        s2 = C.extrude(n + "_em2", [(-0.07, 0.1), (0.1, 0.1), (0.1, 0.0), (-0.07, -0.05), (0.1, -0.1), (-0.1, -0.1), (-0.1, 0.0), (0.07, 0.05)], 0.02, M("red", "satin"))
        s2.data.transform(Matrix.Translation(front(1.0, 1.09) + V((0, -0.03, 0))))
        o.append(s2)
        o.append(cape(n + "_cape", M("red", "satin")))
        o.append(collar(n, M("red", "satin")))
    elif s == "polar":
        o.append(shell(n + "_s", 0.25, 1.5, M("cream", "quilted"), flare=0.08, k=1.1))
        o.append(ring(n + "_fur", V((0, 0, 1.45)), body_r(1.45) * 1.08 + 0.05, 0.14, M("tan"), scale=(1, YK, 1)))
        o.append(ring(n + "_hem", V((0, 0, 0.27)), body_r(0.27) * 1.1 + 0.08, 0.08, M("tan"), scale=(1, YK, 1)))
        o.append(C.box(n + "_zip", front(0.85, 1.1), (0.05, 0.03, 1.0), M("grey", "metal")))
    elif s == "hockey":
        o.append(shell(n + "_s", 0.3, 1.5, M("red", "cotton"), flare=0.06))
        for i, z in enumerate((0.45, 0.6)):
            o.append(shell(n + "_b%d" % i, z, z + 0.08, M("white", "cotton"), k=1.085, n=1, flare=0.05))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_sp%d" % sx, V((0.5 * sx, 0, 1.36)), (0.34, 0.4, 0.22), M("red_dark", "plastic"), 14, 8))
        o.append(C.box(n + "_n", front(1.0, 1.085), (0.12, 0.03, 0.4), M("white", "cotton")))
    elif s == "cowboy":
        o.append(shell(n + "_s", 0.45, 1.45, M("cream", "denim"), k=1.06))
        for sx in (-1, 1):
            rows = [[front(0.45 + i * 0.25, 1.1, ang=sx * (20 + j * 30)) for j in range(5)] for i in range(5)]
            o.append(sheet(n + "_v%d" % sx, M("brown", "leather"), rows, 0.03))
        o.append(C.cyl(n + "_band", front(1.42, 1.0), front(1.2, 1.12), 0.3, 0.02, M("red", "cotton"), 4))
        o.append(collar(n, M("red", "cotton"), z=1.43, th=0.07))
        st = star(n + "_st", V((0, 0, 0)), 0.12, 0.03, M("gold"))
        st.data.transform(Matrix.Translation(front(1.05, 1.13, ang=35)))
        o.append(st)
    elif s == "schoolgirl":
        o.append(shell(n + "_s", 0.55, 1.5, M("white", "cotton")))
        o.append(shell(n + "_sk", 0.18, 0.6, M("navy", "wool"), flare=0.18, k=1.1))
        o.append(C.box(n + "_col", V((0, 0.48, 1.38)), (0.85, 0.3, 0.4), M("navy", "wool"), rot=Matrix.Rotation(-0.35, 4, "X")))
        o.append(collar(n, M("navy", "wool"), th=0.07))
        c = front(1.2, 1.1)
        o.append(ball(n + "_k", c, 0.07, M("red", "satin")))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_w%d" % sx, c, c + V((0.25 * sx, -0.02, 0.05)), 0.02, 0.12, M("red", "satin"), 12))
            o.append(C.cyl(n + "_t%d" % sx, c, c + V((0.1 * sx, -0.04, -0.3)), 0.03, 0.06, M("red", "satin"), 10))
    elif s == "wizard":
        o.append(shell(n + "_s", 0.05, 1.5, M("white", "starry"), flare=0.25))
        o.append(shell(n + "_belt", 0.62, 0.7, M("yellow", "satin"), k=1.09, n=1))
        o.append(collar(n, M("yellow", "satin"), th=0.09))
    elif s == "welder":
        o.append(shell(n + "_s", 0.45, 1.45, M("denim"), k=1.05))
        rows = [[front(0.2 + i * 0.22, 1.12, ang=a) for a in (-55, -30, -10, 10, 30, 55)] for i in range(6)]
        o.append(sheet(n + "_ap", M("brown", "leather"), rows, 0.04))
        o.append(C.box(n + "_pk", front(0.7, 1.16), (0.42, 0.03, 0.22), M("brown_dark"), bevel=0.01))
        o.append(ring(n + "_strap", V((0, 0.0, 1.3)), body_r(1.3) * 1.12, 0.035, M("brown_dark"), axis=(0, -0.4, 1)))
    elif s == "tuxedo":
        o.append(shell(n + "_s", 0.3, 1.5, M("gun_dark", "wool")))
        rows = []
        for i in range(5):
            z = 0.55 + i * 0.22
            w = 22 - i * 2
            rows.append([front(z, 1.085, ang=a) for a in (-w, -w / 2, 0, w / 2, w)])
        o.append(sheet(n + "_shirt", M("white", "cotton"), rows, 0.02))
        for sx in (-1, 1):
            rows = [[front(0.75 + i * 0.18, 1.095, ang=sx * a) for a in (20 - i * 2, 30 - i * 2, 40 - i)] for i in range(5)]
            o.append(sheet(n + "_lap%d" % sx, M("gun_dark", "satin"), rows, 0.02))
        c = front(1.42, 1.1)
        o.append(ball(n + "_k", c, 0.06, M("red", "satin")))
        for sx in (-1, 1):
            o.append(C.cyl(n + "_w%d" % sx, c, c + V((0.22 * sx, -0.01, 0.0)), 0.02, 0.1, M("red", "satin"), 12))
        o += buttons(n, M("gun_dark", "plastic"), (0.75, 0.95, 1.15), k=1.1, r=0.035)
        o.append(C.box(n + "_tl", V((0, 0.55, 0.2)), (0.6, 0.15, 0.5), M("gun_dark", "wool"), bevel=0.04))
    elif s == "bunny":
        o.append(shell(n + "_s", 0.3, 1.5, M("pink", "fur")))
        o.append(C.sphere(n + "_belly", front(0.85, 1.0), (0.42, 0.12, 0.5), M("white", "fur"), 16, 8))
        o.append(ball(n + "_tail", V((0, 0.72, 0.4)), 0.2, M("white", "fur"), 14, 9))
        o.append(collar(n, M("white", "fur")))
    elif s == "desert":
        o.append(shell(n + "_s", 0.4, 1.5, M("khaki")))
        o.append(shell(n + "_belt", 0.5, 0.6, M("brown", "leather"), k=1.09, n=1))
        for i, a in enumerate((-50, -20, 20, 50)):
            o.append(C.box(n + "_po%d" % i, front(0.55, 1.12, ang=a), (0.16, 0.08, 0.16), M("sand_dark", "canvas"), bevel=0.02))
        o.append(collar(n, M("cream", "cotton"), th=0.11))
        for sx in (-1, 1):
            o.append(C.box(n + "_pk%d" % sx, front(1.05, 1.09, ang=28 * sx), (0.24, 0.03, 0.2), M("sand_dark", "canvas"), bevel=0.01))
    elif s == "specialforce":
        o.append(shell(n + "_s", 0.35, 1.5, M("gun", "canvas")))
        o.append(shell(n + "_vest", 0.6, 1.35, M("white", "camo"), k=1.12, n=4))
        for i, a in enumerate((-35, 0, 35)):
            o.append(C.box(n + "_po%d" % i, front(0.8, 1.17, ang=a), (0.2, 0.1, 0.26), M("olive"), bevel=0.02))
        o.append(shell(n + "_belt", 0.45, 0.55, M("gun_dark", "rubber"), k=1.1, n=1))
        o.append(C.box(n + "_bk", front(0.5, 1.12), (0.12, 0.03, 0.08), M("grey", "metal")))
    elif s == "football":
        o.append(shell(n + "_s", 0.35, 1.45, M("blue", "cotton")))
        for sx in (-1, 1):
            o.append(C.sphere(n + "_sp%d" % sx, V((0.45 * sx, -0.05, 1.4)), (0.4, 0.46, 0.2), M("blue_dark", "plastic"), 16, 8))
            o.append(C.box(n + "_sl%d" % sx, V((0.6 * sx, -0.1, 1.3)), (0.05, 0.4, 0.06), M("white", "cotton")))
        for x in (-0.09, 0.09):
            o.append(C.box(n + "_n%d" % (x > 0), front(0.95, 1.09) + V((x, 0, 0)), (0.07, 0.03, 0.4), M("white", "cotton")))
    elif s == "space":
        o.append(shell(n + "_s", 0.3, 1.45, M("white", "quilted"), k=1.1))
        o.append(C.box(n + "_pan", front(0.95, 1.12), (0.5, 0.06, 0.36), M("grey_light", "metal"), bevel=0.03))
        for i, c in enumerate(("red", "green", "blue")):
            o.append(C.cyl(n + "_b%d" % i, front(1.0, 1.15) + V((-0.14 + 0.14 * i, 0, 0)),
                           front(1.0, 1.15) + V((-0.14 + 0.14 * i, -0.04, 0)), 0.04, 0.04, G("btn_" + c, C.PALETTE[c], 1.0), 10))
        o.append(C.box(n + "_scr", front(0.86, 1.17), (0.3, 0.02, 0.08), G("screen", "#4fd8e8", 1.0)))
        o.append(C.box(n + "_pack", V((0, 0.78, 0.95)), (0.85, 0.35, 0.9), M("grey_light", "metal"), bevel=0.06))
        o.append(shell(n + "_belt", 0.42, 0.52, M("grey", "metal"), k=1.12, n=1))
    elif s == "gladiator":
        o.append(shell(n + "_s", 0.55, 1.45, M("gold_dark"), k=1.08))
        o.append(C.sphere(n + "_pec", front(1.1, 0.95), (0.42, 0.2, 0.25), M("gold_dark"), 16, 8))
        for i in range(12):
            ang = -150 + i * 27
            p = front(0.4, 1.1, ang=ang)
            o.append(C.box(n + "_pt%d" % i, p, (0.16, 0.06, 0.32), M("brown", "leather"),
                           rot=Matrix.Rotation(math.radians(ang), 4, "Z")))
        o.append(C.sphere(n + "_pa", V((-0.58, 0, 1.34)), (0.3, 0.34, 0.2), M("gold_dark"), 14, 8))
        o.append(ring(n + "_strap", V((0, 0, 1.0)), 0.76, 0.04, M("brown", "leather"), axis=(0.6, 0, 1)))
    elif s == "king":
        o.append(cape(n + "_cape", M("red", "satin"), z_top=1.48, z_bot=0.05, a0=60, a1=300, spread=0.32))
        o.append(ring(n + "_erm", V((0, 0, 1.45)), body_r(1.45) * 1.1 + 0.06, 0.15, M("white", "fur"), scale=(1, YK, 1)))
        r = body_r(1.45) * 1.1 + 0.06
        for i in range(10):
            a = 2 * math.pi * i / 10
            o.append(ball(n + "_ed%d" % i, V((r * math.cos(a), r * math.sin(a) * YK, 1.57)), 0.035, M("black"), 6, 4))
        o.append(C.cyl(n + "_med", front(1.1, 1.1), front(1.1, 1.1) + V((0, -0.04, 0)), 0.14, 0.14, M("gold"), 16))
        o.append(ball(n + "_gem", front(1.1, 1.1) + V((0, -0.05, 0)), 0.06, M("red", "glass"), 10, 6))
        for i in range(5):
            o.append(ball(n + "_ch%d" % i, front(1.42 - i * 0.07, 1.06, ang=-30 + 15 * i) + V((0, 0, -0.08 * (2 - abs(i - 2)))), 0.035,
                          M("gold"), 6, 4))
    elif s == "dark_assassin":
        o.append(shell(n + "_s", 0.15, 1.5, M("grey_dark", "canvas"), flare=0.15))
        o.append(shell(n + "_sash", 0.55, 0.68, M("red", "satin"), k=1.09, n=1, flare=0.03))
        o.append(C.box(n + "_tie", front(0.6, 1.12, ang=30), (0.1, 0.05, 0.4), M("red", "satin")))
        st = star(n + "_shu", V((0, 0, 0)), 0.1, 0.02, M("steel"), points=4, inner=0.3)
        st.data.transform(Matrix.Translation(front(0.62, 1.13, ang=-30)))
        o.append(st)
        o.append(ring(n + "_strap", V((0, 0, 1.0)), 0.74, 0.035, M("black", "leather"), axis=(-0.6, 0, 1), scale=(1, 0.95, 1)))
    elif s == "elvis":
        o.append(shell(n + "_s", 0.2, 1.5, M("white", "sequin"), flare=0.1))
        col = C.cyl(n + "_col", V((0, 0.12, 1.4)), V((0, 0.2, 1.62)), 0.56, 0.62, M("white", "satin"), 24)
        W.uv_cyl(col, radius=0.6)
        o.append(col)
        o.append(shell(n + "_belt", 0.48, 0.62, M("gold"), k=1.09, n=1))
        o.append(C.box(n + "_bk", front(0.55, 1.12), (0.3, 0.04, 0.2), M("gold"), bevel=0.02))
        for i in range(10):
            ang = -60 + i * 13
            o.append(ball(n + "_r%d" % i, front(1.25 - abs(i - 4.5) * 0.06, 1.09, ang=ang), 0.03, M("gold"), 6, 4))
        rows = [[front(0.75 + i * 0.22, 1.08, ang=a) for a in (-(8 + i * 6), 0, 8 + i * 6)] for i in range(4)]
        o.append(sheet(n + "_v", M("cream", "satin"), rows, 0.01))
    elif item.startswith("army_jacket"):
        team = "blue" if item.endswith("blue") else "red"
        o.append(shell(n + "_s", 0.3, 1.5, M("white", "camo")))
        o.append(collar(n, M("olive_dark")))
        for sx in (-1, 1):
            o.append(C.box(n + "_pk%d" % sx, front(1.05, 1.09, ang=30 * sx), (0.26, 0.04, 0.22), M("olive_dark"), bevel=0.015))
        o += buttons(n, M("olive_dark", "plastic"), (0.5, 0.75, 1.0, 1.25))
        o.append(shell(n + "_arm", 0.32, 0.42, M(team, "canvas"), k=1.1, n=1))
        o.append(C.box(n + "_patch", front(1.25, 1.1, ang=-35), (0.18, 0.04, 0.14), M(team, "canvas")))
    elif item == "RedSweater":
        o.append(shell(n + "_s", 0.3, 1.5, M("red", "knit")))
        tn = C.cyl(n + "_tn", V((0, 0, 1.38)), V((0, 0, 1.6)), body_r(1.38) * 1.1, body_r(1.5) * 1.15, M("red", "knit"), 24)
        tn.data.transform(Matrix.Diagonal((1, YK, 1, 1)))
        W.uv_cyl(tn, radius=0.6)
        o.append(tn)
        for i, z in enumerate((0.85, 1.05)):
            o.append(shell(n + "_b%d" % i, z, z + 0.06, M("white", "knit"), k=1.085, n=1))
        for i in range(10):
            ang = -90 + i * 20
            o.append(C.box(n + "_d%d" % i, front(0.95, 1.09, ang=ang), (0.08, 0.02, 0.08), M("white", "knit"),
                           rot=Matrix.Rotation(math.radians(ang), 4, "Z") @ Matrix.Rotation(0.785, 4, "Y")))
        o.append(shell(n + "_hem", 0.3, 0.38, M("red_dark", "knit"), k=1.09, n=1))
    else:
        o.append(shell(n + "_s", 0.35, 1.5, M("grey")))
    W.uv_auto(o)
    return o


# ------------------------------------------------------------------------------------------ feet items

def feet_item(item):
    s = item.rsplit("_", 1)[0] if item.endswith("_feet") else item
    n = item
    o = []
    if s == "flannel":
        o += shoe(n, M("brown"), shaft=0.2, sole="brown_dark", cuff="red_dark", laces="tan")
    elif s == "paper":
        o += shoe(n, M("cream", "paper"), height=0.6, sole="grey_light")
        o.append(C.box(n + "_x", ANK + V((0, -0.35, 0.03)), (0.25, 0.01, 0.02), M("grey", "felt")))
    elif s == "rain":
        o += shoe(n, M("green", "rubber"), shaft=0.32, sole="green_dark", cuff="green_dark")
    elif s == "clown":
        o += shoe(n, M("red", "plastic"), length=1.35, width=1.25, height=1.15, sole="yellow")
        o.append(ball(n + "_pp", ANK + V((0, -0.3, 0.12)), 0.08, M("yellow", "fur")))
    elif s == "sm":
        o += shoe(n, M("red", "leather"), shaft=0.28, cuff="yellow")
    elif s == "polar":
        o += shoe(n, M("tan"), shaft=0.22, width=1.1, sole="brown", cuff="cream")
    elif s == "hockey" or item == "Skates":
        o += shoe(n, M("gun_dark", "leather") if item == "Skates" else M("white", "leather"), shaft=0.25,
                  laces="white" if item == "Skates" else "red")
        o.append(C.box(n + "_bl", ANK + V((0, -0.24, -0.17)), (0.04, 0.85, 0.06), M("silver"), bevel=0.015))
        for y in (0.0, -0.45):
            o.append(C.box(n + "_ps%d" % (y < 0), ANK + V((0, -0.24 + 0.2 + y * 0.9, -0.11)), (0.05, 0.06, 0.1), M("steel")))
    elif s == "cowboy":
        o += shoe(n, M("brown"), shaft=0.32, toe_up=0.2, sole="brown_dark", cuff="tan")
        st = star(n + "_sp", V((0, 0, 0)), 0.08, 0.02, M("gold"), points=6)
        st.data.transform(Matrix.Translation(ANK + V((0, 0.22, 0.0))) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
        o.append(st)
    elif s == "schoolgirl":
        o += shoe(n, M("gun_dark", "leather"), height=0.8, shaft=0.18, shaft_mat=M("white", "knit"), cuff="white")
        o.append(C.box(n + "_strap", ANK + V((0, -0.2, 0.05)), (0.42, 0.06, 0.03), M("gun_dark", "leather")))
    elif s == "wizard":
        o += shoe(n, M("purple", "satin"), length=1.2, height=0.8, toe_up=0.5)
        o.append(ball(n + "_bell", ANK + V((0, -0.72, 0.33)), 0.06, M("gold")))
    elif s == "welder":
        o += shoe(n, M("brown_dark"), shaft=0.15, width=1.1, sole="gun_dark", toe="steel")
    elif s == "tuxedo":
        o += shoe(n, M("gun_dark", "plastic"), height=0.85, sole="black")
        o.append(ring(n + "_sock", ANK + V((0, 0.0, 0.05)), 0.16, 0.05, M("white", "cotton")))
    elif s == "bunny":
        o += shoe(n, M("pink", "fur"), width=1.15, height=1.1)
        for sx in (-1, 1):
            o.append(C.sphere(n + "_e%d" % sx, ANK + V((0.1 * sx, -0.4, 0.3)), (0.06, 0.04, 0.16), M("white", "fur"), 10, 6,
                              rot=Matrix.Rotation(0.4 * sx, 4, "Y")))
        o.append(ball(n + "_n", ANK + V((0, -0.62, 0.08)), 0.05, M("rose", "felt")))
    elif s == "desert":
        o.append(C.sphere(n + "_sole", ANK + V((0, -0.24, -0.1)), (0.26, 0.45, 0.04), M("brown"), 16, 6))
        for i, y in enumerate((-0.05, -0.3, -0.5)):
            o.append(C.box(n + "_st%d" % i, ANK + V((0, y, -0.02)), (0.44, 0.06, 0.05), M("brown_dark")))
        o.append(ring(n + "_a", ANK + V((0, 0, 0.02)), 0.17, 0.035, M("brown_dark")))
    elif s == "specialforce":
        o += shoe(n, M("gun_dark", "leather"), shaft=0.25, sole="black", laces="grey")
    elif s == "football":
        o += shoe(n, M("white", "leather"), height=0.9, sole="gun_dark")
        for i, (x, y) in enumerate(((-0.12, -0.05), (0.12, -0.05), (-0.12, -0.45), (0.12, -0.45), (0, -0.62))):
            o.append(C.cyl(n + "_cl%d" % i, ANK + V((x, y, -0.15)), ANK + V((x, y, -0.22)), 0.04, 0.025, M("gun_dark", "rubber"), 6))
        o.append(C.box(n + "_s", ANK + V((0.24, -0.25, 0.0)), (0.02, 0.3, 0.05), M("blue", "leather")))
    elif s == "space":
        o += shoe(n, M("white", "quilted"), width=1.25, height=1.4, shaft=0.25, sole="grey", cuff="silver")
    elif s == "gladiator":
        o.append(C.sphere(n + "_sole", ANK + V((0, -0.24, -0.1)), (0.26, 0.45, 0.04), M("brown_dark"), 16, 6))
        for i, z in enumerate((0.0, 0.1, 0.2, 0.3)):
            o.append(ring(n + "_r%d" % i, ANK + V((0, 0.0, z)), 0.18, 0.025, M("brown")))
        o.append(C.box(n + "_t", ANK + V((0, -0.3, -0.02)), (0.05, 0.4, 0.04), M("brown")))
    elif s == "king":
        o += shoe(n, M("gold"), toe_up=0.15, sole="gold_dark")
        o.append(ball(n + "_g", ANK + V((0, -0.4, 0.13)), 0.06, M("red", "glass")))
    elif s == "dark_assassin":
        o += shoe(n, M("grey_dark", "canvas"), height=0.85, shaft=0.2)
        for i, z in enumerate((0.0, 0.08, 0.16)):
            o.append(ring(n + "_w%d" % i, ANK + V((0, 0.0, z)), 0.2, 0.025, M("black", "canvas")))
    elif s == "elvis":
        o += shoe(n, M("denim", "satin"), height=0.85, sole="cream")
        o.append(ring(n + "_sock", ANK + V((0, 0.0, 0.05)), 0.16, 0.05, M("white", "cotton")))
    elif item.startswith("army_boots"):
        team = "blue" if item.endswith("blue") else "red"
        o += shoe(n, M("olive_dark", "leather"), shaft=0.26, sole="gun_dark", laces=team, cuff=team)
    else:
        o += shoe(n, M("grey"))
    W.uv_auto(o)
    return o


# ------------------------------------------------------------------------------------------ gloves

def _flipper_frame():
    """Right flipper: (socket point, along (towards the tip), across (flat side, ~ -Y = front), normal (thickness))."""
    sh, tip = P.SHOULDER_R, P.HAND_R
    along = (tip - sh).normalized()
    across = V((0, -1, 0))
    across = (across - along * across.dot(along)).normalized()
    normal = along.cross(across).normalized()
    return P.glove_socket("R"), along, across, normal


def _local(p0, along, across, normal):
    """Matrix from glove-local (x = thickness, y = across/front, z = along to the tip) to canonical."""
    m = Matrix((normal, across, along)).transposed().to_4x4()
    return Matrix.Translation(p0) @ m


def glove_parts(item):
    """Right glove in the canonical frame around the right flipper tip."""
    p0, along, across, normal = _flipper_frame()
    L = _local(p0, along, across, normal)
    n = item
    o = []

    def sph(name, c, r, mat, rot=None, segs=16, rings=10):
        ob = C.sphere(name, (0, 0, 0), r, mat, segs, rings)
        m = Matrix.Translation(V(c))
        if rot is not None:
            m = m @ rot
        ob.data.transform(L @ m)
        return ob

    def cuff(name, z, r, th, mat, scale=(0.8, 1.0, 1.0)):
        ob = C.torus(name, (0, 0, 0), r, th, mat, 14, 6, scale=scale)
        ob.data.transform(L @ Matrix.Translation(V((0, 0, z))))
        return ob

    def tube(name, z0, z1, r0, r1, mat, sc=(0.8, 1.0)):
        ob = C.cyl(name, (0, 0, z0), (0, 0, z1), r0, r1, mat, 14)
        ob.data.transform(L @ Matrix.Diagonal((sc[0], sc[1], 1, 1)))
        return ob

    if item == "gloves_boxing":
        red = M("red", "leather")
        o.append(sph(n + "_fist", (0.0, -0.02, 0.17), (0.2, 0.24, 0.24), red))
        o.append(sph(n + "_th", (0.04, -0.2, 0.08), (0.08, 0.08, 0.13), red, rot=Matrix.Rotation(0.4, 4, "X")))
        o.append(tube(n + "_wr", -0.2, 0.02, 0.15, 0.17, M("white", "leather")))
        o.append(cuff(n + "_cf", -0.2, 0.15, 0.04, M("white", "leather")))
        o.append(sph(n + "_lace", (0.15, 0.0, -0.08), (0.03, 0.09, 0.12), M("white", "cotton")))
    elif item == "gloves_mittens":
        knit = M("#3a8ad8", "knit")
        o.append(sph(n + "_mit", (0.0, 0.0, 0.12), (0.13, 0.2, 0.25), knit))
        o.append(sph(n + "_th", (0.0, -0.17, 0.02), (0.07, 0.07, 0.11), knit, rot=Matrix.Rotation(0.5, 4, "X")))
        o.append(tube(n + "_cuff", -0.2, -0.04, 0.15, 0.14, M("white", "knit")))
        o.append(cuff(n + "_st", -0.1, 0.145, 0.025, M("red", "knit")))
        o.append(sph(n + "_pom", (0.0, 0.15, -0.16), (0.06, 0.06, 0.06), M("white", "fur"), segs=10, rings=6))
    elif item == "gloves_work":
        lea = M("tan", "leather")
        o.append(sph(n + "_hand", (0.0, 0.0, 0.1), (0.12, 0.19, 0.23), lea))
        for i, y in enumerate((-0.11, -0.04, 0.03, 0.1)):
            o.append(sph(n + "_f%d" % i, (0.0, y, 0.3 - abs(y) * 0.3), (0.055, 0.045, 0.1), lea, segs=10, rings=6))
        o.append(sph(n + "_th", (0.0, -0.17, 0.0), (0.06, 0.06, 0.1), lea, rot=Matrix.Rotation(0.6, 4, "X"), segs=10, rings=6))
        o.append(tube(n + "_cuff", -0.22, -0.02, 0.16, 0.14, M("brown_dark", "leather")))
        o.append(cuff(n + "_st", -0.05, 0.14, 0.018, M("brown_dark", "leather")))
    elif item == "gloves_cartoon":
        wh = M("white", "cotton")
        o.append(sph(n + "_hand", (0.0, 0.0, 0.12), (0.13, 0.2, 0.22), wh))
        for i, y in enumerate((-0.09, 0.0, 0.09)):
            o.append(sph(n + "_f%d" % i, (0.0, y, 0.3), (0.06, 0.05, 0.1), wh, segs=10, rings=6))
            o.append(C.box(n + "_ln%d" % i, (0, 0, 0), (0.02, 0.012, 0.16), M("gun_dark", "cotton")))
            o[-1].data.transform(L @ Matrix.Translation(V((0.125, y * 0.9, 0.1))))
        o.append(sph(n + "_th", (0.0, -0.17, 0.04), (0.06, 0.06, 0.1), wh, rot=Matrix.Rotation(0.5, 4, "X"), segs=10, rings=6))
        o.append(cuff(n + "_cf", -0.09, 0.15, 0.06, wh, scale=(0.85, 1.05, 1.0)))
    elif item == "gloves_gold":
        gold = M("gold")
        o.append(sph(n + "_hand", (0.0, 0.0, 0.11), (0.13, 0.2, 0.24), gold))
        for i in range(3):
            o.append(cuff(n + "_pl%d" % i, -0.02 + 0.09 * i, 0.15 - 0.02 * i, 0.03, M("gold_dark")))
        o.append(sph(n + "_gem", (0.13, 0.0, 0.08), (0.03, 0.06, 0.06), M("red", "glass"), segs=10, rings=6))
        o.append(tube(n + "_g", -0.24, -0.04, 0.18, 0.15, gold))
        o.append(cuff(n + "_rim", -0.24, 0.18, 0.035, M("gold_dark")))
    else:
        o.append(sph(n + "_m", (0, 0, 0.12), (0.13, 0.2, 0.24), M("grey", "cotton")))
    W.uv_auto(o)
    return o


def _mirror_x(ob):
    me = ob.data
    me.transform(Matrix.Diagonal((-1, 1, 1, 1)))
    me.flip_normals()
    me.update()
    return ob


def build_gloves(item):
    """Two objects, "GloveR" and "GloveL", each relative to its own socket and turned by the yaw."""
    right = C.join(glove_parts(item), "GloveR")
    left = C.join(glove_parts(item), "GloveL")
    _mirror_x(left)
    out = []
    for ob, side in ((right, "R"), (left, "L")):
        ob.data.transform(Matrix.Translation(-P.glove_socket(side)))
        ob.data.transform(P.yaw_matrix().to_4x4())
        ob.data.update()
        ob["export_name"] = "Glove" + side
        out.append(ob)
    return out


# ------------------------------------------------------------------------------------------ build

def build_item(item, slot):
    """One object at its socket (gloves: the right glove only, for icons)."""
    if slot == "hands":
        ob = C.join(glove_parts(item), item)
        ob.data.transform(Matrix.Translation(-SOCKET[slot]))
        # icon pose: fingers up, cuff down (on the penguin it hangs from the flipper)
        ob.data.transform(Matrix.Rotation(math.pi + 0.35, 4, "Y"))
        ob.data.transform(P.yaw_matrix().to_4x4())
        ob.data.update()
        return ob
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


def place_items(item):
    """Objects of an item placed on a penguin built by penguin.build (previews/icons): both feet, both gloves."""
    slot = all_items()[item]
    if slot == "hands":
        objs = build_gloves(item)
        for ob in objs:
            ob.location = P.socket_world("GloveSocket" + ob["export_name"][-1])
        return objs
    ob = build_item(item, slot)
    ob.location = P.socket_world(SOCKET_NAME[slot])
    if slot != "feet":
        return [ob]
    other = ob.copy()
    other.data = ob.data.copy()
    C._link(other)
    other.location = P.socket_world("FootSocketR")
    return [ob, other]


def run(only=None):
    W.ensure_textures()
    C.reset()
    items = all_items()
    stats = {}
    for item, slot in items.items():
        if only and item not in only:
            continue
        col = C.new_collection(item)
        C.use_collection(col)
        if slot == "hands":
            objs = build_gloves(item)
        else:
            objs = [build_item(item, slot)]
        C.use_collection(None)
        stats[item] = C.tri_count(objs)
        W.export_textured(col, os.path.join(C.MODELS, "Clothes", item + ".fbx"))
    C.save_blend("clothes")
    over = {k: v for k, v in stats.items() if v > 2500}
    print("[clothes] %d items, max tris %d, over budget: %s" % (len(stats), max(stats.values()), over))
    return stats


def place_on_penguin(ob, slot, mirror_feet=True):
    """Move an exported-orientation clothing object onto a penguin built by penguin.build (for previews)."""
    ob.location = P.socket_world(SOCKET_NAME[slot])
