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


def build_head(prefix="", expression="normal", eyes=True):
    """Head parts in the canonical frame. Returns dict name->object (not parented)."""
    o = {}
    black, white = C.mat("black"), C.mat("white")
    head = C.sphere(prefix + "Head", HEAD_C, (HEAD_R, HEAD_R * 0.95, HEAD_R * 0.96), black, 20, 12)
    face = C.sphere(prefix + "Face", HEAD_C + Vector((0, -0.2, -0.08)), (0.46, 0.4, 0.42), white, 18, 10)
    cheekL = C.sphere(prefix + "CheekL", HEAD_C + Vector((0.32, -0.5, -0.2)), (0.1, 0.03, 0.06), C.mat("pink"), 10, 6)
    cheekR = C.sphere(prefix + "CheekR", HEAD_C + Vector((-0.32, -0.5, -0.2)), (0.1, 0.03, 0.06), C.mat("pink"), 10, 6)
    tuft = C.cyl(prefix + "Tuft", HEAD_C + Vector((0, 0.05, HEAD_R * 0.85)), HEAD_C + Vector((0.05, 0.1, HEAD_R + 0.08)),
                 0.1, 0.0, black, 8)
    o["Head"] = C.join([head, face, cheekL, cheekR, tuft], prefix + "Head")
    o["Beak"] = C.cyl(prefix + "Beak", HEAD_C + Vector((0, -0.52, -0.13)), HEAD_C + Vector((0, -0.95, -0.17)),
                      0.17, 0.02, C.mat("orange"), 10)
    o["Beak"].data.transform(Matrix.Translation(HEAD_C + Vector((0, -0.6, -0.13))) @ Matrix.Diagonal((1, 1, 0.75, 1)).to_4x4()
                             @ Matrix.Translation(-(HEAD_C + Vector((0, -0.6, -0.13)))))
    if eyes:
        for side, sx in (("L", 1), ("R", -1)):
            ec = HEAD_C + Vector((0.2 * sx, -0.555, 0.08))
            o["Eye" + side] = C.sphere(prefix + "Eye" + side, ec, (0.17, 0.085, 0.22), C.mat("eye"), 14, 8)
            pc = ec + Vector((0.04, -0.06, -0.02))
            pupil = C.sphere(prefix + "Pupil" + side + "_p", pc, (0.09, 0.04, 0.12), C.mat("pupil"), 12, 8)
            hl = C.sphere(prefix + "Pupil" + side + "_h", pc + Vector((0.03, -0.04, 0.045)), (0.03, 0.012, 0.035),
                          C.mat("eye"), 8, 4)
            o["Pupil" + side] = C.join([pupil, hl], prefix + "Pupil" + side)
            o["Pupil" + side]["pivot"] = list(ec)
            o["Eye" + side]["pivot"] = list(ec)
    return o


def build(col=None, with_sockets=True):
    """Build the penguin rig in col. Returns dict name->object, already turned by YAW, origins at joints."""
    if col is not None:
        C.use_collection(col)
    black, white, orange = C.mat("black"), C.mat("white"), C.mat("orange")
    o = {}
    body = C.lathe("Body", body_profile(), black, segs=22, scale=(1, BODY_DEPTH, 1))
    o["Body"] = body
    o["Belly"] = C.sphere("Belly", Vector((0, -0.3, 0.76)), (0.62, 0.52, 0.7), white, 20, 12)
    o.update(build_head())
    # flippers: flattened ellipsoids hanging from the shoulders, slightly outward
    for side, sh, tip in (("R", SHOULDER_R, HAND_R), ("L", SHOULDER_L, Vector((0.98, -0.12, 0.62)))):
        mid = (sh + tip) / 2
        d = (tip - sh)
        rot = Vector((0, 0, -1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        o["Flipper" + side] = C.sphere("Flipper" + side, mid, (0.12, 0.26, d.length / 2 + 0.1), black, 12, 8, rot=rot)
    for side, ank in (("R", ANKLE_R), ("L", ANKLE_L)):
        foot = C.sphere("Foot" + side + "_m", ank + Vector((0, -0.2, -0.07)), (0.2, 0.36, 0.08), orange, 14, 6)
        toes = [C.sphere("Toe", ank + Vector((dx, -0.5, -0.08)), (0.09, 0.1, 0.06), orange, 8, 5) for dx in (-0.12, 0, 0.12)]
        o["Foot" + side] = C.join([foot] + toes, "Foot" + side)
    # team colored scarf (Unity tints the "Team" material)
    scarf = C.torus("Scarf_r", NECK + Vector((0, -0.02, -0.03)), 0.6, 0.09, C.mat("Team", "#ffffff"), 22, 8,
                    scale=(1, BODY_DEPTH, 1))
    tail = C.box("Scarf_t", NECK + Vector((-0.28, -0.52, -0.27)), (0.2, 0.08, 0.42), C.mat("Team", "#ffffff"),
                 rot=Matrix.Rotation(math.radians(-15), 4, "Y"))
    C.add_bevel(tail, 0.03, 1)
    o["Scarf"] = C.join([scarf, tail], "Scarf")

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
