"""The penguin: separate parts with pivots at their joints for procedural animation in Unity.

Built in the canonical frame (facing -Y, towards the camera, anatomical right = -X) and then turned by YAW
degrees around Z so the beak points to screen-right (+X) while the belly still faces the camera.
Clothes are modeled in the same canonical frame around the socket points below, so they fit exactly.
"""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math

from mathutils import Matrix, Vector

import common as C

YAW = 40.0          # degrees the penguin is turned from facing the camera towards +X
HEIGHT = 2.6

# canonical joint/socket positions (feet at z=0)
HEAD_C = Vector((0, -0.02, 1.93))      # head sphere center == HeadSocket
HEAD_R = 0.62
NECK = Vector((0, 0, 1.45))            # Head pivot
BODY_PIVOT = Vector((0, 0, 0.1))       # Body pivot (bottom, so squash keeps feet planted)
CHEST = Vector((0, 0, 0.95))           # ChestSocket (body axis)
BODY_R = 0.8                           # max body radius (x); depth scaled by BODY_DEPTH
BODY_DEPTH = 0.9
SHOULDER_R = Vector((-0.66, -0.02, 1.32))
SHOULDER_L = Vector((0.66, -0.02, 1.32))
HAND_R = Vector((-0.98, -0.12, 0.62))
ANKLE_R = Vector((-0.32, -0.05, 0.13))
ANKLE_L = Vector((0.32, -0.05, 0.13))


# child -> parent, applied by PenguinAvatar.cs at runtime
HIERARCHY = {
    "Belly": "Body", "Head": "Body", "FlipperL": "Body", "FlipperR": "Body", "Scarf": "Body", "ChestSocket": "Body",
    "Beak": "Head", "EyeL": "Head", "EyeR": "Head", "HeadSocket": "Head", "PupilL": "EyeL", "PupilR": "EyeR",
    "HandSocket": "FlipperR", "FootSocketL": "FootL", "FootSocketR": "FootR",
}


def yaw_matrix():
    return Matrix.Rotation(math.radians(YAW), 3, "Z")


def body_profile():
    # (radius, z) egg shape
    return [(0.0, 0.08), (0.42, 0.12), (0.66, 0.28), (0.78, 0.55), (0.8, 0.82), (0.76, 1.08),
            (0.66, 1.32), (0.52, 1.52), (0.34, 1.68), (0.0, 1.76)]


def _cap(name, center, radii, material, keep_above, tilt_y=0.0, segs=14, rings=10):
    """Upper part of an ellipsoid (local z > keep_above * radius z), optionally tilted around Y. Eyelids, caps."""
    import bmesh
    ob = C.sphere(name, (0, 0, 0), radii, material, segs, rings)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    cut = keep_above * radii[2]
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < cut - 1e-5], context="VERTS")
    # close the cut with a flat lid so it reads as a solid shell
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.transform(Matrix.Translation(center) @ Matrix.Rotation(tilt_y, 4, "Y"))
    return ob


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


def build_head(prefix="", expression="normal", eyes=True):
    """Head parts in the canonical frame. Returns dict name->object (not parented).
    Original look: dark teal head, white face mask around the eyes, big flat duck-style beak, heavy eyelids."""
    o = {}
    skin, white = C.mat("penguin"), C.mat("white")
    head = C.sphere(prefix + "Head", HEAD_C, (HEAD_R, HEAD_R * 0.95, HEAD_R * 0.96), skin, 20, 12)
    parts = [head]
    # face mask: two eye patches and a muzzle, merged into one white shape
    for sx in (1, -1):
        parts.append(C.sphere(prefix + "Mask%d" % sx, HEAD_C + Vector((0.2 * sx, -0.3, 0.04)), (0.29, 0.32, 0.37), white, 14, 8))
    parts.append(C.sphere(prefix + "Muzzle", HEAD_C + Vector((0, -0.28, -0.2)), (0.4, 0.33, 0.27), white, 14, 8))
    for sx in (1, -1):
        parts.append(C.sphere(prefix + "Cheek%d" % sx, HEAD_C + Vector((0.34 * sx, -0.5, -0.2)), (0.09, 0.03, 0.05),
                              C.mat("pink"), 10, 6))
    # three-feather tuft on the crown
    for i, (dx, lean, ln) in enumerate(((0.0, 0.05, 0.26), (-0.09, -0.25, 0.2), (0.09, 0.3, 0.18))):
        base = HEAD_C + Vector((dx, 0.08, HEAD_R * 0.86))
        tip = base + Vector((lean * 0.6, 0.12, ln))
        parts.append(C.cyl(prefix + "Tuft%d" % i, base, tip, 0.075, 0.0, skin, 8))
    # back-of-head highlight streak (lighter teal), gives the round head some painted volume
    parts.append(C.sphere(prefix + "Sheen", HEAD_C + Vector((-0.18, 0.05, 0.42)), (0.2, 0.25, 0.1), C.mat("penguin_light"), 8, 5,
                          rot=Matrix.Rotation(math.radians(-25), 4, "Y")))
    if eyes:
        # heavy upper eyelids, slanted toward the beak (the original's cocky look)
        for sx in (1, -1):
            ec = HEAD_C + Vector((0.2 * sx, -0.555, 0.08))
            parts.append(_cap(prefix + "Lid%d" % sx, ec + Vector((0, 0.005, 0.0)), (0.19, 0.1, 0.24), skin, 0.42,
                              tilt_y=math.radians(-14 * sx)))
    o["Head"] = C.join(parts, prefix + "Head")
    # beak: wide flat upper bill, smaller darker lower bill, nostrils
    bc = HEAD_C + Vector((0, -0.6, -0.13))
    upper = C.sphere(prefix + "BeakU", bc + Vector((0, -0.2, 0.0)), (0.22, 0.33, 0.085), C.mat("beak"), 16, 8,
                     rot=Matrix.Rotation(math.radians(-6), 4, "X"))
    lower = C.sphere(prefix + "BeakL", bc + Vector((0, -0.15, -0.075)), (0.18, 0.27, 0.06), C.mat("beak_dark"), 14, 6,
                     rot=Matrix.Rotation(math.radians(4), 4, "X"))
    nos = [C.sphere(prefix + "Nos%d" % sx, bc + Vector((0.06 * sx, -0.3, 0.07)), (0.025, 0.035, 0.015), C.mat("beak_dark"), 6, 4)
           for sx in (1, -1)]
    o["Beak"] = C.join([upper, lower] + nos, prefix + "Beak")
    if eyes:
        for side, sx in (("L", 1), ("R", -1)):
            ec = HEAD_C + Vector((0.2 * sx, -0.555, 0.08))
            o["Eye" + side] = C.sphere(prefix + "Eye" + side, ec, (0.17, 0.085, 0.22), C.mat("eye"), 14, 8)
            pc = ec + Vector((0.04, -0.06, -0.02))
            pupil = C.sphere(prefix + "Pupil" + side + "_p", pc, (0.09, 0.04, 0.12), C.mat("pupil"), 10, 6)
            hl = C.sphere(prefix + "Pupil" + side + "_h", pc + Vector((0.03, -0.04, 0.045)), (0.03, 0.012, 0.035),
                          C.mat("eye"), 8, 4)
            hl2 = C.sphere(prefix + "Pupil" + side + "_h2", pc + Vector((-0.035, -0.04, -0.05)), (0.014, 0.008, 0.016),
                           C.mat("eye"), 6, 4)
            o["Pupil" + side] = C.join([pupil, hl, hl2], prefix + "Pupil" + side)
            o["Pupil" + side]["pivot"] = list(ec)
            o["Eye" + side]["pivot"] = list(ec)
    return o


def build(col=None, with_sockets=True):
    """Build the penguin rig in col. Returns dict name->object, already turned by YAW, origins at joints."""
    if col is not None:
        C.use_collection(col)
    black, white, orange = C.mat("penguin"), C.mat("white"), C.mat("beak")
    o = {}
    body = C.lathe("Body", body_profile(), black, segs=24, scale=(1, BODY_DEPTH, 1))
    # short tail feathers at the back
    tail = C.cyl("BodyTail", Vector((0, 0.5, 0.32)), Vector((0, 0.86, 0.12)), 0.2, 0.02, black, 10)
    tail.data.transform(Matrix.Translation((0, 0.68, 0.22)) @ Matrix.Diagonal((1.5, 1, 0.55, 1)) @ Matrix.Translation((0, -0.68, -0.22)))
    o["Body"] = C.join([body, tail], "Body")
    belly = C.sphere("Belly_m", Vector((0, -0.3, 0.76)), (0.62, 0.52, 0.7), white, 20, 12)
    o["Belly"] = C.join([belly], "Belly")
    o.update(build_head())
    # flippers: flattened ellipsoids hanging from the shoulders, slightly outward
    for side, sh, tip in (("R", SHOULDER_R, HAND_R), ("L", SHOULDER_L, Vector((0.98, -0.12, 0.62)))):
        mid = (sh + tip) / 2
        d = (tip - sh)
        rot = Vector((0, 0, -1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        fl = C.sphere("Flipper" + side + "_m", mid, (0.12, 0.27, d.length / 2 + 0.1), black, 14, 10, rot=rot)
        _taper(fl, sh, tip + d.normalized() * 0.1, 0.42)
        # lighter flipper edge (the original's flippers catch a pale highlight on their leading edge)
        edge = C.cyl("FlipEdge" + side, sh + Vector((0, -0.21, -0.05)), tip + Vector((0, -0.12, 0.08)), 0.035, 0.012,
                     C.mat("penguin_light"), 6)
        o["Flipper" + side] = C.join([fl, edge], "Flipper" + side)
    for side, ank in (("R", ANKLE_R), ("L", ANKLE_L)):
        foot = C.sphere("Foot" + side + "_m", ank + Vector((0, -0.2, -0.07)), (0.2, 0.36, 0.08), orange, 14, 6)
        toes = [C.sphere("Toe", ank + Vector((dx, -0.5, -0.08)), (0.09, 0.1, 0.06), orange, 7, 4) for dx in (-0.12, 0, 0.12)]
        # webbing creases and little dark claws
        toes += [C.cyl("Web", ank + Vector((dx * 0.5, -0.12, -0.0)), ank + Vector((dx, -0.48, -0.03)), 0.014, 0.01,
                       C.mat("beak_dark"), 5) for dx in (-0.12, 0.12)]
        toes += [C.sphere("Claw", ank + Vector((dx, -0.6, -0.08)), (0.035, 0.03, 0.025), C.mat("gun_dark"), 5, 3)
                 for dx in (-0.12, 0, 0.12)]
        o["Foot" + side] = C.join([foot] + toes, "Foot" + side)
    # team colored scarf (Unity tints the "Team" material)
    scarf = C.torus("Scarf_r", NECK + Vector((0, -0.02, -0.03)), 0.6, 0.09, C.mat("Team", "#ffffff"), 22, 8,
                    scale=(1, BODY_DEPTH, 1))
    tail = C.box("Scarf_t", NECK + Vector((-0.28, -0.52, -0.27)), (0.2, 0.08, 0.42), C.mat("Team", "#ffffff"),
                 rot=Matrix.Rotation(math.radians(-15), 4, "Y"))
    C.add_bevel(tail, 0.03, 1)
    team = C.mat("Team", "#ffffff")
    rib = C.mat("TeamShade", "#d4d8e0")   # slightly darker knit stripes (tinted with the team color in Unity too)
    stripes = [C.torus("Scarf_s%d" % i, NECK + Vector((0, -0.02, -0.03 + dz)), 0.6 + 0.085 * math.cos(math.asin(dz / 0.09)),
                       0.012, rib, 18, 3, scale=(1, BODY_DEPTH, 1)) for i, dz in enumerate((-0.05, 0.05))]
    fringe = []
    tb = NECK + Vector((-0.28, -0.52, -0.27))
    rot = Matrix.Rotation(math.radians(-15), 3, "Y")
    for i in range(4):
        x = -0.075 + 0.05 * i
        a = tb + rot @ Vector((x, -0.0, -0.21))
        b = tb + rot @ Vector((x, -0.0, -0.31))
        fringe.append(C.cyl("Scarf_f%d" % i, a, b, 0.02, 0.012, team, 5))
    for i, dz in enumerate((-0.1, 0.1)):
        fringe.append(C.box("Scarf_b%d" % i, tb + rot @ Vector((0, -0.045, dz)), (0.21, 0.02, 0.03), rib, rot=rot.to_4x4()))
    o["Scarf"] = C.join([scarf, tail] + stripes + fringe, "Scarf")

    # pivots (canonical)
    piv = {"Body": BODY_PIVOT, "Belly": CHEST, "Head": NECK, "Beak": HEAD_C + Vector((0, -0.55, -0.12)),
           "FlipperR": SHOULDER_R, "FlipperL": SHOULDER_L, "FootR": ANKLE_R, "FootL": ANKLE_L, "Scarf": NECK}
    for side in "LR":
        piv["Eye" + side] = Vector(o["Eye" + side]["pivot"])
        piv["Pupil" + side] = Vector(o["Pupil" + side]["pivot"])
    for k, p in piv.items():
        C.set_origin(o[k], p)
    if with_sockets:
        o["HeadSocket"] = C.empty("HeadSocket", HEAD_C)
        o["ChestSocket"] = C.empty("ChestSocket", CHEST)
        o["FootSocketL"] = C.empty("FootSocketL", ANKLE_L)
        o["FootSocketR"] = C.empty("FootSocketR", ANKLE_R)
        o["HandSocket"] = C.empty("HandSocket", HAND_R)
    # turn the whole penguin (all objects are still unparented, identity rotation)
    C.transform_objects(list(o.values()), yaw_matrix())
    # NOTE: no Blender parenting. Blender 4.2's FBX exporter writes wrong local transforms for objects nested
    # two or more levels deep when bake_space_transform=True, so every part is a root object (origin at its
    # joint) and PenguinAvatar rebuilds the hierarchy below at runtime (SetParent with worldPositionStays).
    C.use_collection(None)
    return o


def socket_world(name):
    """Socket position after the yaw turn (game frame, feet at origin)."""
    p = {"HeadSocket": HEAD_C, "ChestSocket": CHEST, "FootSocketL": ANKLE_L, "FootSocketR": ANKLE_R,
         "HandSocket": HAND_R}[name]
    return yaw_matrix() @ p


def run(icons=True):
    import os
    C.reset()
    col = C.new_collection("Penguin")
    o = build(col)
    tris = C.tri_count(C.col_objects(col))
    path = os.path.join(C.MODELS, "Penguin", "Penguin.fbx")
    C.export_collection(col, path)
    C.save_blend("penguin")
    print("[penguin] tris=%d -> %s" % (tris, path))
    return tris
