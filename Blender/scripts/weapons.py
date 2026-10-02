"""Held weapon models (WeaponGraphic ids). Grip at the origin, barrel along +X, Muzzle empty at the tip.
Lengths roughly 0.6-1.4 units (penguin is 2.6 tall)."""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math
import os

from mathutils import Matrix, Vector

import common as C
import shapes as S

M = S.M   # palette materials; fire/plasma map to self-lit Glow_* materials
V = Vector


# ----------------------------------------------------------------------------------- builders

def launcher(n, tube="olive", accent="olive_dark", length=1.35, r=0.13, z=0.2, warhead=None, x0=-0.5, sight=True,
             bell=True):
    x1 = x0 + length
    o = [S.tube(n + "_t", x0, x1, r, M(tube), z=z, segs=16),
         S.ring_x(n + "_r0", x0 + 0.03, r * 1.05, 0.03, M(accent), z=z),
         S.ring_x(n + "_r1", x1 - 0.03, r * 1.05, 0.03, M(accent), z=z)]
    if bell:
        o.append(S.tube(n + "_bell", x0 - 0.12, x0, r * 1.35, M(accent), z=z, r1=r))
    o += S.grip(n, M(accent), x=0.0, z=z - r * 0.6)
    o += S.grip(n + "b", M(accent), x=0.35, z=z - r * 0.6, h=0.2)
    if sight:
        o.append(C.box(n + "_sg", (0.15, 0, z + r + 0.05), (0.18, 0.05, 0.1), M(accent), bevel=0.01))
    o.append(C.box(n + "_pad", (-0.12, 0, z), (0.35, r * 2.25, r * 1.6), M(accent), bevel=0.03))
    # bolts on the shoulder pad, hazard bands near the muzzle, a lens on the sight
    for i, (bx, bz) in enumerate(((-0.24, 0.5), (0.0, 0.5), (-0.24, -0.5), (0.0, -0.5))):
        o.append(C.sphere(n + "_bt%d" % i, (bx, -r * 1.13, z + bz * r * 1.2), (0.022, 0.012, 0.022), M("steel"), 6, 3))
    for i in range(3):
        hx = x1 - 0.12 - i * 0.045
        o.append(S.tube(n + "_hz%d" % i, hx, hx + 0.045, r * 1.04, M("yellow" if i % 2 == 0 else "gun_dark"), z=z, segs=16))
    if sight:
        o.append(C.cyl(n + "_sl", (0.24, 0, z + r + 0.07), (0.25, 0, z + r + 0.07), 0.03, 0.03, M("glass"), 8))
    tip = x1
    if warhead:
        body, nose = warhead
        o.append(C.cyl(n + "_wh", (x1, 0, z), (x1 + 0.08, 0, z), r * 0.85, r * 0.85, M(body), 14))
        o.append(C.cyl(n + "_wn", (x1 + 0.08, 0, z), (x1 + 0.3, 0, z), r * 0.85, 0.0, M(nose), 14))
        tip = x1 + 0.3
    return o, V((tip, 0, z))


def pistol(n, body="gun", grip_c="brown_dark", length=0.5, barrel_r=0.04, z=0.1, wide=False, accent=None):
    o = [C.box(n + "_sl", (length / 2 - 0.12, 0, z), (length, 0.08, 0.12), M(body), bevel=0.02)]
    br = barrel_r * (2.2 if wide else 1)
    o.append(S.tube(n + "_b", length - 0.15, length - 0.02 + (0.12 if wide else 0), br, M(accent or body), z=z + 0.01))
    o += S.grip(n, M(grip_c), x=0.0, z=z - 0.02, h=0.26, angle=-18)
    o += S.trigger(n, M(body), x=0.08, z=z - 0.02)
    o.append(C.box(n + "_fs", (length - 0.17, 0, z + 0.075), (0.03, 0.02, 0.04), M(body)))
    # slide serrations and grip screws
    for i in range(3):
        o.append(C.box(n + "_sr%d" % i, (-0.06 + i * 0.03, -0.042, z + 0.01), (0.012, 0.01, 0.08), M("gun_dark")))
    o.append(C.sphere(n + "_gs", (-0.04, -0.048, z - 0.12), (0.016, 0.008, 0.016), M("steel"), 6, 3))
    return o, V((length + (0.1 if wide else -0.02), 0, z + 0.01))


def rifle(n, body="gun", wood="wood", length=1.2, barrel_r=0.035, z=0.1, scope=False, mag=True, double=False,
          stock=True, pump=False):
    x0 = -0.45
    o = [C.box(n + "_rc", (0.05, 0, z), (0.45, 0.09, 0.15), M(body), bevel=0.02)]
    xb = 0.27
    if double:
        for y in (-0.035, 0.035):
            o.append(S.tube(n + "_b%d" % (y > 0), xb, x0 + length, barrel_r, M(body), y=y, z=z + 0.03))
    else:
        o.append(S.tube(n + "_b", xb, x0 + length, barrel_r, M(body), z=z + 0.03))
    if stock:
        st = C.extrude(n + "_st", [(-0.15, 0.06), (-0.15, -0.04), (x0 + 0.02 - 0.05, -0.14), (x0 - 0.05, -0.12), (x0 - 0.05, 0.05)],
                       0.08, M(wood), bevel=0.015)
        st.data.transform(Matrix.Translation((0, 0, z)))
        o.append(st)
    o += S.grip(n, M(wood if stock else body), x=0.0, z=z - 0.06, h=0.2, angle=-20)
    o += S.trigger(n, M(body), x=0.1, z=z - 0.05)
    if mag:
        o.append(C.box(n + "_mg", (0.22, 0, z - 0.13), (0.09, 0.06, 0.18), M(body), bevel=0.01,
                       rot=Matrix.Rotation(0.15, 4, "Y")))
    # barrel bands, receiver screws, ejection port
    for i, bx in enumerate((x0 + length - 0.12, 0.72)):
        o.append(S.tube(n + "_bb%d" % i, bx, bx + 0.03, barrel_r * 1.35, M("steel"), z=z + 0.03, segs=10))
    for i, sx in enumerate((-0.1, 0.18)):
        o.append(C.sphere(n + "_sc%d" % i, (sx, -0.048, z - 0.03), (0.016, 0.008, 0.016), M("steel"), 6, 3))
    o.append(C.box(n + "_ej", (0.08, -0.046, z + 0.035), (0.12, 0.01, 0.035), M("gun_dark")))
    if pump:
        o.append(S.tube(n + "_pump", 0.4, 0.62, barrel_r * 1.9, M(wood), z=z - 0.03))
    else:
        o.append(C.box(n + "_hg", (0.42, 0, z - 0.01), (0.3, 0.08, 0.08), M(wood), bevel=0.02))
    if scope:
        o.append(S.tube(n + "_sc", -0.1, 0.32, 0.045, M("gun_dark"), z=z + 0.15))
        o.append(S.tube(n + "_sc1", 0.28, 0.36, 0.06, M("gun_dark"), z=z + 0.15, r1=0.065))
        o.append(C.cyl(n + "_lens", (0.36, 0, z + 0.15), (0.365, 0, z + 0.15), 0.055, 0.055, M("glass"), 12))
        for x in (0.0, 0.2):
            o.append(C.box(n + "_sm%d" % (x > 0), (x, 0, z + 0.1), (0.04, 0.04, 0.06), M("gun_dark")))
    return o, V((x0 + length, 0, z + 0.03))


def scifi(n, body="white", core="plasma", accent="grey", length=1.0, r=0.1, z=0.15, coils=3, wide=False):
    x0 = -0.35
    o = [S.xlathe(n + "_b", [(0.0, 0), (r * 1.2, 0.0), (r * 1.4, 0.15), (r * 1.3, length * 0.55), (r * 0.9, length * 0.7),
                            (r * 0.9 if not wide else r * 1.4, length * 0.95), (0.0, length * 0.95)], M(body), segs=12,
                   x0=x0, z=z),
         S.tube(n + "_core", x0 + length * 0.2, x0 + length * 0.55, r * 1.45, M(core), z=z)]
    for i in range(coils):
        o.append(S.ring_x(n + "_c%d" % i, x0 + length * (0.24 + 0.1 * i), r * 1.42, 0.03, M(accent), z=z))
    o.append(C.cyl(n + "_m", (x0 + length * 0.95, 0, z), (x0 + length, 0, z), r * (0.6 if not wide else 1.1),
                   r * (0.5 if not wide else 1.0), M(core), 12))
    o += S.grip(n, M(accent), x=0.0, z=z - r * 1.0, h=0.24, angle=-15)
    o.append(C.box(n + "_fin", (x0 + 0.2, 0, z + r * 1.3), (0.25, 0.03, 0.12), M(accent), bevel=0.01))
    return o, V((x0 + length, 0, z))


def held(objs, lift=0.05, muzzle=(0.18, 0, 0.05)):
    """Thrown/held item: shift up a bit so the flipper tip grips the bottom."""
    for o in objs:
        o.data.transform(Matrix.Translation((0.08, 0, lift)))
    return objs, V(muzzle)


def minigun(n):
    o = []
    z = 0.15
    for i in range(6):
        a = 2 * math.pi * i / 6
        y, zz = 0.065 * math.cos(a), z + 0.065 * math.sin(a)
        o.append(S.tube(n + "_b%d" % i, 0.25, 1.0, 0.025, M("gun"), y=y, z=zz, segs=8))
    for x in (0.4, 0.95):
        o.append(S.tube(n + "_cl%d" % (x > 0.5), x, x + 0.04, 0.11, M("gun_dark"), z=z))
    o.append(S.tube(n + "_hub", 0.1, 0.27, 0.12, M("gun_dark"), z=z))
    o.append(C.box(n + "_body", (-0.08, 0, z), (0.38, 0.22, 0.24), M("olive"), bevel=0.04))
    o.append(S.tube(n + "_drum", -0.15, 0.05, 0.16, M("olive_dark"), y=0.18, z=z - 0.05))
    o += S.grip(n, M("gun_dark"), x=0.0, z=z - 0.12, h=0.2)
    o.append(C.torus(n + "_h", (-0.05, 0, z + 0.2), 0.1, 0.02, M("gun_dark"), 12, 4, axis=(0, 1, 0)))
    return o, V((1.0, 0, z))


def flamethrower(n):
    z = 0.12
    o = [S.tube(n + "_b", -0.25, 0.75, 0.05, M("steel"), z=z),
         S.tube(n + "_n", 0.75, 0.9, 0.05, M("gun_dark"), z=z, r1=0.08),
         C.cyl(n + "_tank", (-0.25, 0.02, z - 0.08), (0.25, 0.02, z - 0.08), 0.1, 0.1, M("red"), 14),
         S.ring_x(n + "_tr", 0.0, 0.1, 0.02, M("gun_dark"), y=0.02, z=z - 0.08),
         C.sphere(n + "_pl", (0.92, 0, z - 0.06), 0.035, S.G("fire"), 8, 5),
         C.box(n + "_hs", (0.5, 0, z - 0.05), (0.06, 0.05, 0.12), M("gun_dark"))]
    o += S.grip(n, M("gun_dark"), x=0.0, z=z - 0.05, h=0.22)
    return o, V((0.92, 0, z))


def firehose(n):
    z = 0.08
    o = [S.xlathe(n + "_nz", [(0.08, 0), (0.07, 0.25), (0.05, 0.5), (0.035, 0.62)], M("gold"), 14, x0=-0.05, z=z),
         S.ring_x(n + "_r", 0.05, 0.085, 0.02, M("gold_dark"), z=z),
         C.box(n + "_lv", (0.15, 0, z + 0.09), (0.18, 0.03, 0.04), M("red")),
         C.torus(n + "_coil", (-0.2, 0.05, z - 0.05), 0.15, 0.035, M("red"), 16, 6, axis=(0, 1, 0)),
         S.tube(n + "_h", -0.2, -0.05, 0.035, M("red"), z=z)]
    o += S.grip(n, M("red_dark"), x=0.05, z=z - 0.04, h=0.18)
    return o, V((0.57, 0, z))


def wand(n):
    o = [C.cyl(n + "_st", (-0.15, 0, 0), (0.55, 0, 0.08), 0.025, 0.018, M("brown_dark"), 8)]
    st = S.C.extrude(n + "_star", [(0.12 * (1 if i % 2 == 0 else 0.45) * math.cos(math.pi / 2 + i * math.pi / 5),
                                    0.12 * (1 if i % 2 == 0 else 0.45) * math.sin(math.pi / 2 + i * math.pi / 5)) for i in range(10)],
                     0.04, M("yellow"))
    st.data.transform(Matrix.Translation((0.6, 0, 0.09)))
    o.append(st)
    o.append(C.sphere(n + "_gl", (0.6, -0.03, 0.09), 0.04, M("mint"), 8, 5))
    return o, V((0.62, 0, 0.09))


def scythe(n):
    o = [C.cyl(n + "_h", (-0.4, 0, -0.1), (0.75, 0, 0.25), 0.03, 0.03, M("wood_dark"), 8)]
    pts = []
    for i in range(9):
        t = i / 8
        a = math.radians(-10 - 120 * t)
        pts.append((0.38 * math.cos(a) * (1.0), 0.38 * math.sin(a) * (1 - 0.2 * t)))
    inner = [(p[0] * 0.75 + 0.06, p[1] * 0.6) for p in reversed(pts)]
    blade = C.extrude(n + "_bl", [(x, -y) for (x, y) in pts + inner], 0.025, M("silver"))
    blade.data.transform(Matrix.Translation((0.72, 0, 0.24)) @ Matrix.Rotation(math.radians(180), 4, "Z"))
    blade.data.transform(Matrix.Rotation(math.radians(180), 4, "Z") @ Matrix.Translation((0, 0, 0)))
    blade.data.transform(Matrix.Translation((1.44, 0, 0)))
    o.append(blade)
    o.append(C.box(n + "_c", (0.73, 0, 0.25), (0.08, 0.06, 0.08), M("gun_dark")))
    o.append(C.box(n + "_g", (0.1, 0, 0.05), (0.04, 0.12, 0.04), M("wood_dark")))
    return o, V((0.9, 0, 0.1))


def broom(n):
    o = [C.cyl(n + "_h", (-0.35, 0, 0), (0.6, 0, 0.05), 0.025, 0.025, M("wood"), 8),
         C.cyl(n + "_b", (0.55, 0, 0.05), (0.9, 0, 0.07), 0.05, 0.16, M("yellow"), 12),
         S.ring_x(n + "_bd", 0.6, 0.06, 0.02, M("red"), z=0.05)]
    return o, V((0.9, 0, 0.07))


def gadget(n, body="gun", top="red", antenna=True, screen=None, orb=None):
    z = 0.12
    o = [C.box(n + "_b", (0.05, 0, z), (0.28, 0.14, 0.3), M(body), bevel=0.04)]
    o.append(C.cyl(n + "_btn", (0.05, 0, z + 0.15), (0.05, 0, z + 0.2), 0.07, 0.065, M(top), 14))
    if antenna:
        o.append(C.cyl(n + "_an", (-0.05, 0, z + 0.15), (-0.08, 0, z + 0.48), 0.012, 0.01, M("grey"), 6))
        o.append(C.sphere(n + "_ab", (-0.08, 0, z + 0.5), 0.03, M(top), 8, 5))
    if screen:
        o.append(C.box(n + "_sc", (0.05, -0.075, z + 0.03), (0.18, 0.01, 0.1), M(screen)))
    if orb:
        o.append(C.sphere(n + "_orb", (0.24, 0, z + 0.05), 0.09, M(orb), 12, 8))
    return o, V((0.25, 0, z + 0.05))


def mortar_tube(n):
    z = 0.18
    o = [S.tube(n + "_t", -0.25, 0.55, 0.11, M("olive"), z=z),
         S.ring_x(n + "_r", 0.52, 0.12, 0.03, M("olive_dark"), z=z),
         C.cyl(n + "_bp", (-0.28, 0, z), (-0.28, 0, z - 0.16), 0.14, 0.14, M("olive_dark"), 12),
         C.cyl(n + "_leg", (0.25, 0, z - 0.1), (0.4, 0, z - 0.32), 0.02, 0.02, M("gun"), 6)]
    o += S.grip(n, M("olive_dark"), x=0.0, z=z - 0.1, h=0.18)
    return o, V((0.55, 0, z))


def hand_cannon(n):
    z = 0.16
    o = [S.xlathe(n + "_b", [(0.0, 0), (0.12, 0), (0.15, 0.08), (0.12, 0.2), (0.09, 0.62), (0.11, 0.66), (0.11, 0.72),
                            (0.07, 0.72), (0.0, 0.72)], M("gun_dark"), 16, x0=-0.2, z=z),
         S.ring_x(n + "_r", 0.15, 0.11, 0.02, M("gold"), z=z),
         C.sphere(n + "_kn", (-0.23, 0, z), 0.05, M("gun_dark"), 8, 5)]
    o += S.grip(n, M("wood_dark"), x=0.0, z=z - 0.08, h=0.2)
    return o, V((0.52, 0, z))


def choco_cannon(n):
    """Easter chocolate cannon: chocolate barrel with frosting drips, sprinkles and a candy fuse."""
    z = 0.16
    o = [S.xlathe(n + "_b", [(0.0, 0), (0.13, 0), (0.16, 0.08), (0.13, 0.2), (0.1, 0.62), (0.125, 0.66), (0.125, 0.74),
                            (0.08, 0.74), (0.0, 0.74)], M("choco"), 16, x0=-0.2, z=z),
         S.ring_x(n + "_r", 0.15, 0.115, 0.022, M("pink"), z=z),
         S.ring_x(n + "_r2", 0.5, 0.106, 0.018, M("pink"), z=z),
         C.sphere(n + "_kn", (-0.24, 0, z), 0.055, M("choco_dark"), 8, 5),
         C.cyl(n + "_fuse", (-0.12, 0, z + 0.12), (-0.18, 0, z + 0.24), 0.018, 0.014, M("red"), 6),
         C.sphere(n + "_spark", (-0.185, 0, z + 0.25), 0.03, S.G("yellow"), 6, 4)]
    # white frosting drips along the top of the barrel
    for i, x in enumerate((0.0, 0.14, 0.3, 0.44)):
        o.append(C.sphere(n + "_fr%d" % i, (x, -0.02, z + 0.105 - 0.01 * i), (0.045, 0.06, 0.03 + 0.012 * (i % 2)), M("cream"), 8, 4))
    import random
    rnd = random.Random(5)
    for i in range(8):
        x = rnd.uniform(-0.1, 0.5)
        o.append(C.box(n + "_sp%d" % i, (x, -0.11, z + rnd.uniform(-0.05, 0.08)), (0.035, 0.012, 0.012),
                       M(("yellow", "pink", "cyan", "lime")[i % 4]), rot=Matrix.Rotation(rnd.uniform(0, 3), 4, "Y")))
    o += S.grip(n, M("choco_dark"), x=0.0, z=z - 0.08, h=0.2)
    return o, V((0.54, 0, z))


def seeker_launcher(n):
    """Heat-seeking missile launcher: white tube with a red sensor dome, targeting screen and handle."""
    o, tip = launcher(n, "white", "red", length=1.25, r=0.12, warhead=("white", "red"))
    z = tip.z
    o.append(C.box(n + "_scr", (0.1, -0.15, z + 0.17), (0.2, 0.03, 0.13), M("gun_dark"), bevel=0.01))
    o.append(C.box(n + "_scg", (0.1, -0.168, z + 0.17), (0.15, 0.01, 0.08), S.G("lime")))
    o.append(C.sphere(n + "_eye", (tip.x - 0.12, -0.08, z + 0.07), (0.04, 0.03, 0.04), S.G("red"), 8, 5))
    return o, tip


def goo_gun(n):
    """Grey Goo: a canister gun with a glass tank of grey nanite goo."""
    o, tip = scifi(n, "steel", "grey", "gun_dark", 0.9, r=0.09, coils=2)
    z = tip.z
    o.append(C.cyl(n + "_tank", (-0.2, 0, z + 0.2), (0.15, 0, z + 0.2), 0.08, 0.08, M("glass"), 14))
    for i, x in enumerate((-0.12, 0.0, 0.1)):
        o.append(C.sphere(n + "_g%d" % i, (x, -0.02, z + 0.18), 0.05, M("grey_dark"), 8, 5))
    for x in (-0.21, 0.16):
        o.append(S.ring_x(n + "_tr%d" % (x > 0), x, 0.085, 0.015, M("gun_dark"), z=z + 0.2))
    return o, tip


def grenade_launcher(n):
    z = 0.14
    o = [S.tube(n + "_b", 0.1, 0.75, 0.07, M("gun"), z=z),
         C.cyl(n + "_dr", (-0.08, 0, z - 0.02), (0.18, 0, z - 0.02), 0.13, 0.13, M("gun_dark"), 6),
         S.ring_x(n + "_dr1", 0.05, 0.13, 0.02, M("olive"), z=z - 0.02)]
    for i in range(6):
        a = 2 * math.pi * i / 6
        o.append(C.cyl(n + "_h%d" % i, (0.181, 0.08 * math.cos(a), z - 0.02 + 0.08 * math.sin(a)),
                       (0.185, 0.08 * math.cos(a), z - 0.02 + 0.08 * math.sin(a)), 0.03, 0.03, M("olive"), 8))
    st = C.extrude(n + "_st", [(-0.08, 0.05), (-0.45, -0.05), (-0.45, -0.15), (-0.08, -0.08)], 0.07, M("olive"), bevel=0.01)
    st.data.transform(Matrix.Translation((0, 0, z)))
    o.append(st)
    o += S.grip(n, M("gun_dark"), x=0.0, z=z - 0.1, h=0.2)
    return o, V((0.75, 0, z))


def fireworks_launcher(n):
    z = 0.2
    o = [C.box(n + "_box", (0.15, 0, z), (0.75, 0.24, 0.24), M("red"), bevel=0.03),
         C.box(n + "_bd", (0.15, -0.121, z), (0.7, 0.01, 0.06), M("yellow"))]
    cols = ["yellow", "blue", "green", "magenta"]
    for i in range(4):
        y, zz = (-0.06 if i % 2 else 0.06), z + (0.06 if i < 2 else -0.06)
        o.append(C.cyl(n + "_r%d" % i, (0.52, y, zz), (0.6, y, zz), 0.045, 0.045, M(cols[i]), 10))
        o.append(C.cyl(n + "_n%d" % i, (0.6, y, zz), (0.68, y, zz), 0.045, 0.0, M(cols[(i + 1) % 4]), 10))
    o += S.grip(n, M("gun_dark"), x=0.0, z=z - 0.12, h=0.2)
    return o, V((0.68, 0, z))


def shield_emitter(n):
    z = 0.1
    o = [C.cyl(n + "_b", (-0.1, 0, z), (0.15, 0, z), 0.08, 0.1, M("grey"), 14),
         C.sphere(n + "_d", (0.17, 0, z), (0.08, 0.2, 0.32), M("cyan"), 14, 8),
         C.torus(n + "_r", (0.17, 0, z), 0.2, 0.025, M("grey_dark"), 16, 4, axis=(1, 0, 0), scale=(1, 1, 1.5))]
    return o, V((0.25, 0, z))


def void_gen(n):
    z = 0.15
    o = [C.cyl(n + "_h", (-0.15, 0, z - 0.05), (0.15, 0, z - 0.05), 0.05, 0.06, M("grey_dark"), 10)]
    for p in S.orb(n + "_o", "purple_dark", r=0.14, cage="silver"):
        p.data.transform(Matrix.Translation((0.32, 0, z)))
        o.append(p)
    return o, V((0.45, 0, z))


def plasma_bomb_held(n):
    o = []
    for p in S.orb(n + "_o", "plasma", r=0.16, cage="grey_dark"):
        p.data.transform(Matrix.Translation((0.12, 0, 0.16)))
        o.append(p)
    o.append(C.cyl(n + "_base", (0.12, 0, -0.02), (0.12, 0, 0.04), 0.14, 0.12, M("grey_dark"), 12))
    return o, V((0.2, 0, 0.16))


def nuke_held(n, big=False):
    o = S.bomb(n, body="olive" if not big else "gun", stripe="yellow" if not big else "red",
               length=1.0 if not big else 1.35, r=0.22 if not big else 0.3)
    for p in o:
        p.data.transform(Matrix.Translation((0.2, 0, 0.18 if not big else 0.24)))
    return o, V((0.7 if not big else 0.9, 0, 0.18))


def doomsday(n):
    z = 0.15
    o = [C.box(n + "_case", (0.1, 0, z), (0.5, 0.2, 0.32), M("gun_dark"), bevel=0.04),
         C.box(n + "_panel", (0.1, -0.101, z + 0.02), (0.42, 0.01, 0.24), M("grey_dark")),
         C.cyl(n + "_btn", (0.1, -0.1, z + 0.05), (0.1, -0.16, z + 0.05), 0.07, 0.065, M("red"), 14),
         C.box(n + "_hz", (0.1, -0.105, z - 0.08), (0.36, 0.01, 0.04), M("yellow")),
         C.cyl(n + "_an", (0.3, 0, z + 0.16), (0.36, 0, z + 0.5), 0.012, 0.01, M("grey"), 6),
         C.sphere(n + "_ab", (0.36, 0, z + 0.52), 0.035, M("red"), 8, 5),
         C.torus(n + "_hd", (0.1, 0, z + 0.2), 0.08, 0.02, M("gun"), 12, 4, axis=(0, 1, 0))]
    for i, c in enumerate(("green", "yellow", "cyan")):
        o.append(C.sphere(n + "_l%d" % i, (-0.05 + 0.1 * i, -0.105, z + 0.11), 0.025, M(c), 8, 4))
    return o, V((0.36, 0, z))


def orbital_remote(n):
    o, mz = gadget(n, body="silver", top="red", antenna=False, screen="cyan")
    o.append(C.cyl(n + "_dish", (0.0, 0, 0.3), (0.05, 0, 0.42), 0.0, 0.14, M("grey_light"), 14))
    o.append(C.cyl(n + "_dst", (0.0, 0, 0.27), (0.03, 0, 0.36), 0.015, 0.015, M("grey"), 6))
    return o, mz


def punch(n):
    o = S.glove(n, "red")
    for p in o:
        p.data.transform(Matrix.Translation((0.15, 0, 0.0)))
    return o, V((0.45, 0, 0))


# ----------------------------------------------------------------------------------- weapon table

def _held(shape_fn, *a, **k):
    return lambda n: held(shape_fn(n, *a, **k))


WEAPONS = {
    "BasicNuke": lambda n: nuke_held(n, False),
    "MegaNuke": lambda n: nuke_held(n, True),
    "Molotov": _held(S.molotov),
    "Rock": _held(S.rock),
    "Minigun": minigun,
    "Grenade": _held(S.grenade, "olive"),
    "Mine": _held(S.mine, "red"),
    "OrbitalLaser": orbital_remote,
    "Pistol": lambda n: pistol(n),
    "Dynamite": _held(S.dynamite),
    "Mortar": mortar_tube,
    "ClusterGrenade": _held(S.grenade, "red", 0.18),
    "Shotgun": lambda n: rifle(n, length=1.15, barrel_r=0.04, double=True, mag=False, pump=False),
    "FlameMine": _held(S.mine, "fire", "orange_dark"),
    "GasGrenade": _held(S.canister, "green", "gun", label="yellow"),
    "PlasmaCannon": lambda n: scifi(n, "white", "plasma", "steel", 1.05),
    "Punch": punch,
    "ClusterRocket": lambda n: launcher(n, "red", "red_dark", warhead=("yellow", "red")),
    "DoomsdayDevice": doomsday,
    "VoidGenerator": void_gen,
    "FragmentationMissile": lambda n: launcher(n, "steel", "steel_dark", length=1.4, warhead=("olive", "gun")),
    "StickyBomb": _held(S.blob, "slime", 0.15),
    "Railgun": lambda n: scifi(n, "gun", "blue", "cyan", 1.35, r=0.08, coils=5),
    "ArmorPiercingRocket": lambda n: launcher(n, "gun", "gun_dark", length=1.4, warhead=("silver", "steel_dark")),
    "Beanbag": _held(S.blob, "tan", 0.14, 9, 3),
    "Flamethrower": flamethrower,
    "LemonGrenade": _held(S.lemon),
    "TeleportationGrenade": _held(S.grenade, "purple", 0.16, "silver", False),
    "Drill": lambda n: _drill_launcher(n),
    "PointTeleport": lambda n: gadget(n, body="purple_dark", top="cyan", orb="violet"),
    "MiniBazooka": lambda n: launcher(n, "green", "green_dark", length=0.95, r=0.1, warhead=("green", "red")),
    "Napalm": _held(S.canister, "orange", "gun_dark", 0.15, 0.4, "red"),
    "SniperRifle": lambda n: rifle(n, length=1.45, barrel_r=0.03, scope=True, wood="brown"),
    "CinderGrenade": _held(S.rock, 0.17, 5, "rock_dark"),
    "ImpactCannon": lambda n: scifi(n, "orange", "yellow", "gun_dark", 0.95, r=0.12, coils=2, wide=True),
    "ShieldWall": shield_emitter,
    "FlareGun": lambda n: pistol(n, body="orange", grip_c="orange_dark", length=0.45, wide=True, accent="orange_dark"),
    "EasterEgg": _held(S.egg),
    "Cannon": hand_cannon,
    "LaserPistol": lambda n: scifi(n, "red", "rose", "gun_dark", 0.6, r=0.07, coils=2),
    "PlasmaMortar": lambda n: scifi(n, "grey_light", "plasma", "steel_dark", 0.85, r=0.13, coils=3, wide=True),
    "FireHose": firehose,
    "MiningLaser": lambda n: scifi(n, "yellow", "orange", "gun_dark", 0.9, r=0.1, coils=3),
    "PlasmaBomb": plasma_bomb_held,
    "GrenadeLauncher": grenade_launcher,
    "FuelAirBomb": lambda n: _held_bomb(n),
    "ArtilleryStrike": lambda n: gadget(n, body="olive", top="green", antenna=True, screen="lime"),
    "WaterBalloon": _held(S.balloon, "blue"),
    "WandWind": wand,
    "Scythe": scythe,
    "Cat": lambda n: held(S.cat(n), 0.12, (0.35, 0, 0.15)),
    "Broom": broom,
    "Snowball": _held(S.snowball),
    "Fireworks": fireworks_launcher,
    # weapons without a WeaponGraphic record in the config (added for the restored original weapons)
    "HeatSeeker": seeker_launcher,
    "GreyGoo": goo_gun,
    "ChocoCannon": choco_cannon,
}
EXTRA_IDS = ["HeatSeeker", "GreyGoo", "ChocoCannon"]


def _drill_launcher(n):
    o, tip = launcher(n, "yellow", "gun", length=1.1, r=0.12, warhead=None)
    for p in S.drill_bit(n + "_d", length=0.35, r=0.1, x0=tip.x):
        p.data.transform(Matrix.Translation((0, 0, tip.z)))
        o.append(p)
    return o, V((tip.x + 0.35, 0, tip.z))


def _held_bomb(n):
    o = S.bomb(n, body="steel", stripe="red", length=0.7, r=0.15, sign=False)
    for p in o:
        p.data.transform(Matrix.Translation((0.15, 0, 0.14)))
    return o, V((0.5, 0, 0.14))


def build(wid, col):
    C.use_collection(col)
    fn = WEAPONS.get(wid)
    if fn is None:
        fn = lambda n: launcher(n)
    objs, tip = fn(wid + "_")
    ob = C.join(objs, wid)
    mz = C.empty("Muzzle", tip, 0.1)
    C.parent(mz, ob)
    C.use_collection(None)
    return ob


def run():
    C.reset()
    ids = C.ids("WeaponGraphic")
    ids += [w for w in EXTRA_IDS if w not in ids]
    cols = {}
    stats = {}
    for wid in ids:
        col = C.new_collection("W_" + wid)
        ob = build(wid, col)
        stats[wid] = C.tri_count([ob])
        C.export_collection(col, os.path.join(C.MODELS, "Weapons", wid + ".fbx"))
        cols[wid] = col
    C.save_blend("weapons")
    over = {k: v for k, v in stats.items() if v > 1500}
    print("[weapons] %d models, max tris %d, over budget: %s" % (len(stats), max(stats.values()), over))
    return cols
