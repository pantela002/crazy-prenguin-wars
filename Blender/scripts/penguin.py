"""The penguin: a simple, clean cartoon bird (in the spirit of the original game's penguin: tall egg body, big googly
eyes, wide orange bill, dark teal feathers with a white belly and a warm chest) that clothes, gloves and skins attach to.

Separate parts with pivots at their joints for procedural animation in Unity. Built in the canonical frame (facing -Y,
towards the camera, anatomical right = -X) and then turned by YAW degrees around Z so the beak points to screen-right
(+X) while the belly still faces the camera. Clothes are modeled in the same canonical frame around the socket points
below, so they fit exactly.

Look: every visible part uses ONE textured material "T_penguin__white" whose image is a 1024x1024 atlas painted by
penguin_skins.py (feathers with scallops, belly with the yellow chest band, cheeks, bill, feet, eyes). A skin is the
same atlas painted differently (Textures/Penguin/{skin}.png), so Unity swaps one texture to reskin the bird.
"""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math

from mathutils import Matrix, Vector

import common as C
import penguin_skins as S
import wear_kit as W

YAW = 40.0          # degrees the penguin is turned from facing the camera towards +X
HEIGHT = 2.6

# canonical joint/socket positions (feet at z=0)
HEAD_C = Vector((0, -0.02, 1.98))      # head sphere center == HeadSocket
HEAD_R = 0.58
NECK = Vector((0, 0, 1.5))             # Head pivot
BODY_PIVOT = Vector((0, 0, 0.1))       # Body pivot (bottom, so squash keeps feet planted)
CHEST = Vector((0, 0, 0.95))           # ChestSocket (body axis)
BELLY = Vector((0, -0.66, 0.85))       # Belly (empty on the belly surface: badges, the front of the bird)
BODY_R = 0.7                           # max body radius (x); depth scaled by BODY_DEPTH
BODY_DEPTH = 0.88
SHOULDER_R = Vector((-0.6, -0.02, 1.3))
SHOULDER_L = Vector((0.6, -0.02, 1.3))
HAND_R = Vector((-0.9, -0.14, 0.6))
HAND_L = Vector((0.9, -0.14, 0.6))
GLOVE_T = 0.74                         # GloveSocket = this far from the shoulder to the flipper tip
ANKLE_R = Vector((-0.3, -0.05, 0.12))
ANKLE_L = Vector((0.3, -0.05, 0.12))
EYE_OFF = Vector((0.19, -0.38, 0.27))  # eye center from HEAD_C (x mirrored)
EYE_R = (0.17, 0.13, 0.23)


def glove_socket(side):
    sh, hand = (SHOULDER_R, HAND_R) if side == "R" else (SHOULDER_L, HAND_L)
    return sh + (hand - sh) * GLOVE_T


# child -> parent, applied by PenguinAvatar.cs at runtime
HIERARCHY = {
    "Head": "Body", "FlipperL": "Body", "FlipperR": "Body", "ChestSocket": "Body", "Belly": "Body",
    "Beak": "Head", "EyeL": "Head", "EyeR": "Head", "HeadSocket": "Head", "PupilL": "EyeL", "PupilR": "EyeR",
    "HandSocket": "FlipperR", "GloveSocketL": "FlipperL", "GloveSocketR": "FlipperR",
    "FootSocketL": "FootL", "FootSocketR": "FootR",
}


def yaw_matrix():
    return Matrix.Rotation(math.radians(YAW), 3, "Z")


def body_profile():
    # (radius, z): a tall egg, widest a bit below the middle
    return [(0.0, 0.06), (0.34, 0.085), (0.54, 0.18), (0.65, 0.34), (0.7, 0.55), (0.705, 0.75), (0.68, 0.97),
            (0.62, 1.18), (0.53, 1.37), (0.42, 1.53), (0.29, 1.66), (0.15, 1.75), (0.0, 1.79)]


def _taper(ob, a, b, amount):
    """Shrink the cross-section of a mesh toward b (point a keeps full size): flippers, feathers."""
    a, b = Vector(a), Vector(b)
    d = b - a
    L2 = max(1e-6, d.length_squared)
    for v in ob.data.vertices:
        t = max(0.0, min(1.0, (v.co - a).dot(d) / L2))
        axis = a + d * t
        v.co = axis + (v.co - axis) * (1.0 - amount * t)
    return ob


# ------------------------------------------------------------------------------------------ atlas UVs

def _layer(ob):
    me = ob.data
    return me.uv_layers.get("Tex") or me.uv_layers.new(name="Tex")


def _uv_each(ob, fn):
    """fn(world point) -> atlas uv, per corner."""
    me = ob.data
    lay = _layer(ob)
    off = C.world_loc(ob)
    for p in me.polygons:
        for li in p.loop_indices:
            lay.data[li].uv = fn(me.vertices[me.loops[li].vertex_index].co + off)


def _uv_wrap(ob, region, angle_h):
    """Cylindrical/spherical parts: angle_h(point) -> (angle fraction or None at a pole, height fraction).
    Faces crossing the back seam get continuous u (the painter repeats the pattern past 1)."""
    me = ob.data
    lay = _layer(ob)
    off = C.world_loc(ob)
    for p in me.polygons:
        vals = [angle_h(me.vertices[me.loops[li].vertex_index].co + off) for li in p.loop_indices]
        known = [a for a, _ in vals if a is not None]
        fill = sum(known) / len(known) if known else 0.5
        us = W._fix_wrap([fill if a is None else a for a, _ in vals])
        for li, u, (_, h) in zip(p.loop_indices, us, vals):
            lay.data[li].uv = S.wrap_uv(region, u, h)


def _ang(x, y):
    if abs(x) < 1e-6 and abs(y) < 1e-6:
        return None
    return (math.atan2(x, y) / (2 * math.pi)) % 1.0     # 0 = back (+Y), 0.5 = front (-Y)


def uv_body(ob):
    _uv_wrap(ob, S.BODY, lambda p: (_ang(p.x, p.y / BODY_DEPTH), max(0.0, min(1.0, p.z / S.BODY_TOP))))


def uv_head(ob, center=None):
    c = HEAD_C if center is None else center

    def f(p):
        d = p - c
        r = max(1e-6, d.length)
        return _ang(d.x, d.y), math.asin(max(-1.0, min(1.0, d.z / r))) / math.pi + 0.5
    _uv_wrap(ob, S.HEAD, f)


def uv_disc(ob, region, center, rx, rz):
    """Front view (x, z) of an eye/pupil, normalized by its radii."""
    def f(p):
        s = 0.5 + 0.5 * max(-1.0, min(1.0, (p.x - center.x) / rx))
        t = 0.5 + 0.5 * max(-1.0, min(1.0, (p.z - center.z) / rz))
        return S.to_region(region, s, t)
    _uv_each(ob, f)


def uv_top(ob, region, center, half_x, y_tip, y_back):
    """Top view: s across (x), t from the tip (y_tip -> 0) to the back (y_back -> 1)."""
    def f(p):
        s = 0.5 + 0.5 * max(-1.0, min(1.0, (p.x - center.x) / half_x))
        t = max(0.0, min(1.0, (p.y - y_tip) / (y_back - y_tip)))
        return S.to_region(region, s, t)
    _uv_each(ob, f)


def uv_flipper(ob, sh, tip):
    d = tip - sh
    L = d.length
    dn = d / L
    mid = (sh + tip) / 2

    def f(p):
        along = max(0.0, min(1.0, (p - sh).dot(dn) / L))
        s = max(0.0, min(1.0, 0.5 + (p.y - mid.y) / 0.56))
        return S.to_region(S.FLIP, s, 1.0 - along)
    _uv_each(ob, f)


# ------------------------------------------------------------------------------------------ parts

def skin_mat():
    return W.tmat("penguin", "white", image_path=S.skin_path(S.DEFAULT_SKIN), rough=0.55, spec=0.3)


def build_head(prefix="", expression="normal", eyes=True, mat=None):
    """Head parts in the canonical frame. Returns dict name->object (not parented): Head, Beak and, with eyes,
    EyeL/R + PupilL/R (with a "pivot" custom property at the eye center)."""
    m = mat or skin_mat()
    o = {}
    head = C.sphere(prefix + "Head_m", HEAD_C, (HEAD_R, HEAD_R * 0.96, HEAD_R * 0.97), m, 28, 18)
    # a three-feather tuft on the crown (the original's cowlick), small enough to hide under hats
    tuft = []
    for i, (dx, lean, ln) in enumerate(((0.0, 0.04, 0.2), (-0.075, -0.22, 0.15), (0.075, 0.26, 0.13))):
        base = HEAD_C + Vector((dx, 0.1, HEAD_R * 0.9))
        tip = base + Vector((lean * 0.55, 0.1, ln))
        tuft.append(C.cyl(prefix + "Tuft%d" % i, base, tip, 0.06, 0.0, m, 8))
    o["Head"] = C.join([head] + tuft, prefix + "Head")
    uv_head(o["Head"])
    # bill: wide flat upper bill, smaller lower bill (top-view UVs into the BEAKU / BEAKL regions)
    bc = HEAD_C + Vector((0, -0.5, -0.12))
    up_c = bc + Vector((0, -0.24, -0.01))
    upper = C.sphere(prefix + "BeakU", up_c, (0.25, 0.37, 0.1), m, 18, 10, rot=Matrix.Rotation(math.radians(-7), 4, "X"))
    uv_top(upper, S.BEAKU, up_c, 0.27, up_c.y - 0.38, up_c.y + 0.38)
    lo_c = bc + Vector((0, -0.18, -0.09))
    lower = C.sphere(prefix + "BeakL", lo_c, (0.2, 0.3, 0.07), m, 16, 8, rot=Matrix.Rotation(math.radians(5), 4, "X"))
    uv_top(lower, S.BEAKL, lo_c, 0.22, lo_c.y - 0.31, lo_c.y + 0.31)
    o["Beak"] = C.join([upper, lower], prefix + "Beak")
    if eyes:
        for side, sx in (("L", 1), ("R", -1)):
            ec = HEAD_C + Vector((EYE_OFF.x * sx, EYE_OFF.y, EYE_OFF.z))
            eye = C.sphere(prefix + "Eye" + side + "_m", ec, EYE_R, m, 18, 12)
            uv_disc(eye, S.EYE, ec, EYE_R[0], EYE_R[2])
            o["Eye" + side] = C.join([eye], prefix + "Eye" + side)
            pc = ec + Vector((0.045, -0.1, -0.015))   # looking forward, a little toward the screen right
            pr = (0.088, 0.045, 0.118)
            pupil = C.sphere(prefix + "Pupil" + side + "_m", pc, pr, m, 14, 8)
            uv_disc(pupil, S.PUPIL, pc, pr[0], pr[2])
            o["Pupil" + side] = C.join([pupil], prefix + "Pupil" + side)
            o["Pupil" + side]["pivot"] = list(ec)
            o["Eye" + side]["pivot"] = list(ec)
    return o


def _body(m):
    body = C.lathe("Body_m", body_profile(), m, segs=32, scale=(1, BODY_DEPTH, 1))
    # a rounder belly: push the front forward around the middle
    for v in body.data.vertices:
        r = math.hypot(v.co.x, v.co.y / BODY_DEPTH)
        if r < 1e-4 or v.co.y >= 0:
            continue
        front = (-v.co.y / BODY_DEPTH / r) ** 2
        bump = math.exp(-((v.co.z - 0.78) / 0.42) ** 2)
        v.co.y -= 0.06 * front * bump
    # short tail feathers at the back bottom
    tail = C.cyl("BodyTail", Vector((0, 0.45, 0.3)), Vector((0, 0.8, 0.1)), 0.2, 0.02, m, 12)
    tail.data.transform(Matrix.Translation((0, 0.62, 0.2)) @ Matrix.Diagonal((1.5, 1, 0.5, 1)) @ Matrix.Translation((0, -0.62, -0.2)))
    ob = C.join([body, tail], "Body")
    uv_body(ob)
    return ob


def _flipper(side, m):
    sh, tip = (SHOULDER_R, HAND_R) if side == "R" else (SHOULDER_L, HAND_L)
    mid = (sh + tip) / 2
    d = tip - sh
    rot = Vector((0, 0, -1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    fl = C.sphere("Flipper" + side + "_m", mid, (0.11, 0.25, d.length / 2 + 0.09), m, 16, 12, rot=rot)
    _taper(fl, sh, tip + d.normalized() * 0.09, 0.45)
    ob = C.join([fl], "Flipper" + side)
    uv_flipper(ob, sh, tip)
    return ob


def _foot(side, m):
    ank = ANKLE_R if side == "R" else ANKLE_L
    parts = [C.sphere("Foot" + side + "_m", ank + Vector((0, -0.22, -0.06)), (0.2, 0.36, 0.075), m, 16, 8)]
    parts += [C.sphere("Toe", ank + Vector((dx, -0.5, -0.065)), (0.085, 0.11, 0.06), m, 10, 6) for dx in (-0.12, 0, 0.12)]
    ob = C.join(parts, "Foot" + side)
    uv_top(ob, S.FOOT, ank, 0.32, ank.y - 0.66, ank.y + 0.2)
    return ob


def build(col=None, with_sockets=True, scarf=None):
    """Build the penguin rig in col. Returns dict name->object, already turned by YAW, origins at joints.
    scarf (default: only without sockets, i.e. for icons) adds the old team scarf "Scarf" (material "Team")."""
    if col is not None:
        C.use_collection(col)
    m = skin_mat()
    o = {"Body": _body(m)}
    o.update(build_head(mat=m))
    for side in "RL":
        o["Flipper" + side] = _flipper(side, m)
        o["Foot" + side] = _foot(side, m)
    if scarf is None:
        scarf = not with_sockets
    if scarf:
        team = C.mat("Team", "#ffffff")
        ring = C.torus("Scarf_r", NECK + Vector((0, -0.02, -0.03)), 0.55, 0.09, team, 22, 8, scale=(1, BODY_DEPTH, 1))
        tail = C.box("Scarf_t", NECK + Vector((-0.26, -0.48, -0.27)), (0.2, 0.08, 0.42), team,
                     rot=Matrix.Rotation(math.radians(-15), 4, "Y"))
        o["Scarf"] = C.join([ring, tail], "Scarf")

    # pivots (canonical)
    piv = {"Body": BODY_PIVOT, "Head": NECK, "Beak": HEAD_C + Vector((0, -0.5, -0.12)),
           "FlipperR": SHOULDER_R, "FlipperL": SHOULDER_L, "FootR": ANKLE_R, "FootL": ANKLE_L, "Scarf": NECK}
    for side in "LR":
        piv["Eye" + side] = Vector(o["Eye" + side]["pivot"])
        piv["Pupil" + side] = Vector(o["Pupil" + side]["pivot"])
    for k, p in piv.items():
        if k in o:
            C.set_origin(o[k], p)
    if with_sockets:
        for name, p in SOCKETS.items():
            o[name] = C.empty(name, p)
    # turn the whole penguin (all objects are still unparented, identity rotation)
    C.transform_objects(list(o.values()), yaw_matrix())
    # NOTE: no Blender parenting. Blender 4.2's FBX exporter writes wrong local transforms for objects nested
    # two or more levels deep when bake_space_transform=True, so every part is a root object (origin at its
    # joint) and PenguinAvatar rebuilds the hierarchy below at runtime (SetParent with worldPositionStays).
    C.use_collection(None)
    return o


SOCKETS = {"HeadSocket": HEAD_C, "ChestSocket": CHEST, "Belly": BELLY, "FootSocketL": ANKLE_L, "FootSocketR": ANKLE_R,
           "HandSocket": HAND_R, "GloveSocketL": glove_socket("L"), "GloveSocketR": glove_socket("R")}


def socket_world(name):
    """Socket position after the yaw turn (game frame, feet at origin)."""
    return yaw_matrix() @ SOCKETS[name]


def run(icons=True):
    import os
    W.ensure_textures()
    S.write_skins()
    C.reset()
    col = C.new_collection("Penguin")
    o = build(col)
    tris = C.tri_count(C.col_objects(col))
    path = os.path.join(C.MODELS, "Penguin", "Penguin.fbx")
    W.export_textured(col, path)
    C.save_blend("penguin")
    print("[penguin] tris=%d -> %s" % (tris, path))
    return tris
