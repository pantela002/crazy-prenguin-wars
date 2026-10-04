"""Held weapon models (WeaponGraphic ids) and the textured weapon icons.

Contract (PenguinAvatar / ModelLibrary): Resources/Models/Weapons/<WeaponGraphic id>.fbx, one mesh object named after
the id with a child empty "Muzzle" at the tip; grip at the origin (where the flipper holds it), barrel along +X.
Lengths roughly 0.6-1.4 units (the penguin is 2.6 tall).

Every model is built from bevelled multi-part meshes (lathed tubes with lips and bells, filleted side profiles,
rivets, bands, wrapped decals, swept hoses and guards) and textured materials: weapon_textures.py paints seamless
512px textures (chipped paint, brushed/blued metal, wood grain, knurled rubber, hazard stripes, sci-fi panels, foil,
fur, slime, ...) into Resources/Models/Weapons/Textures, referenced by the FBX (Unity samples them with the planar
object-space UV0 that common._bake_vertex_data writes) and box-projected in the icon renders.

Icons: icons() renders Resources/Icons/WeaponsTextured/<Item id>.png for every Weapon item (Cycles, consistent tilted
3/4 view, outline + drop shadow from render.py). Where the original shop icon shows the ammo rather than the launcher
(rockets, shells, satellite, nuke...) the icon renders a matching textured projectile composition instead.

    python3 Blender/scripts/weapons.py                 # models + icons
    python3 Blender/scripts/weapons.py icons Pistol,Rock
    python3 Blender/scripts/weapons.py models
"""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import math
import os
import random
import shutil
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import common as C  # noqa: E402
import shapes as S  # noqa: E402
import weapon_textures as WT  # noqa: E402

V = Vector
X = (1, 0, 0)
ICON_DIR = os.path.join(C.ICONS, "WeaponsTextured")


# =========================================================================================== materials

_tm = {}
_ship = [False]   # True while building the exported models: materials use the 256px game copies


def TM(name, kind, *args, rough=0.45, metal=0.0, coat=0.0, bump=0.25, glow=0.0, trans=0.0, emit=0.0, **kw):
    """Textured material (one PNG per name). glow > 0 makes a self-lit Glow_* material (unshaded in Unity); emit > 0
    only makes the bright parts of the texture glow in the renders (dark parts emit next to nothing)."""
    m = _tm.get(name)
    try:
        if m is not None and m.name in bpy.data.materials and m.get("ship") == _ship[0]:
            return m
    except ReferenceError:
        pass
    path = WT.texture(name, kind, *args, **kw)
    if _ship[0]:
        path = WT.ship(name)
    m = bpy.data.materials.new((C.GLOW_PREFIX + "_" if glow > 0 else "W_") + name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    img = bpy.data.images.load(path, check_existing=True)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.projection = "BOX"
    tex.projection_blend = 0.3
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(tc.outputs["Object"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Base Color"].default_value = (1, 1, 1, 1)   # FBX diffuse color: white x texture
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    try:
        b.inputs["Specular IOR Level"].default_value = 0.45
    except KeyError:
        pass
    if coat > 0:
        b.inputs["Coat Weight"].default_value = coat
        b.inputs["Coat Roughness"].default_value = 0.12
    if trans > 0:
        b.inputs["Transmission Weight"].default_value = trans
    if glow > 0 or emit > 0:
        if emit > 0:
            # emission = color * color: only the hot, bright parts of the texture (lava cracks) light up
            pw = nt.nodes.new("ShaderNodeMix")
            pw.data_type = "RGBA"
            pw.blend_type = "MULTIPLY"
            pw.inputs["Factor"].default_value = 1.0
            nt.links.new(tex.outputs["Color"], pw.inputs[6])
            nt.links.new(tex.outputs["Color"], pw.inputs[7])
            nt.links.new(pw.outputs[2], b.inputs["Emission Color"])
        else:
            nt.links.new(tex.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = glow or emit
    # rounded edge highlights + texture relief (render only; not exported)
    bev = nt.nodes.new("ShaderNodeBevel")
    bev.samples = 6
    bev.inputs["Radius"].default_value = 0.008
    if bump > 0:
        bw = nt.nodes.new("ShaderNodeRGBToBW")
        bp = nt.nodes.new("ShaderNodeBump")
        bp.inputs["Strength"].default_value = bump
        bp.inputs["Distance"].default_value = 0.002
        nt.links.new(tex.outputs["Color"], bw.inputs["Color"])
        nt.links.new(bw.outputs["Val"], bp.inputs["Height"])
        nt.links.new(bev.outputs["Normal"], bp.inputs["Normal"])
        nt.links.new(bp.outputs["Normal"], b.inputs["Normal"])
    else:
        nt.links.new(bev.outputs["Normal"], b.inputs["Normal"])
    import numpy as np
    px = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(px)
    avg = px.reshape(-1, 4)[:, :3].mean(axis=0)
    m.diffuse_color = (*avg, 1)
    m["ship"] = _ship[0]
    _tm[name] = m
    return m


def _hx(c):
    return C.PALETTE.get(c, c)


# Shared material library: name -> (kind, args, kwargs, shading)
def paint(c, seed=None, chips=0.45, coat=0.35):
    return TM("paint_" + c.strip("#"), "paint", _hx(c), seed or (sum(map(ord, c)) % 97), chips=chips, coat=coat, rough=0.38)


def plastic(c):
    return TM("plastic_" + c.strip("#"), "plastic", _hx(c), rough=0.3, coat=0.4, bump=0.1)


def steel(c="#a4adb8", rust=0.0):
    return TM("metal_" + c.strip("#") + ("_r" if rust else ""), "metal", _hx(c), rust=rust, rough=0.32, metal=0.75,
              bump=0.15)


def gunmetal(c="#3c4350"):
    return TM("gunmetal_" + c.strip("#"), "gunmetal", _hx(c), rough=0.34, metal=0.6, bump=0.15)


def wood(c="#a4683a", seed=5):
    return TM("wood_" + c.strip("#"), "wood", _hx(c), seed, rough=0.42, coat=0.25, bump=0.35)


def rubber(c="#2a2d33", pattern="knurl"):
    return TM("rubber_%s_%s" % (pattern, c.strip("#")), "rubber", _hx(c), pattern=pattern, rough=0.75, bump=0.6)


def hazard(c1="#ffd23a", c2="#262b34", period=8):
    return TM("hazard_%s_%s" % (c1.strip("#"), c2.strip("#")), "stripes", _hx(c1), _hx(c2), period=period, rough=0.4,
              coat=0.2)


def panels(c, grid=(5, 4)):
    return TM("panels_" + c.strip("#"), "panels", _hx(c), grid=grid, rough=0.35, coat=0.3, bump=0.4)


def glow(c, strength=3.0):
    return TM("glow_" + c.strip("#"), "glow", _hx(c), glow=strength, rough=0.2, bump=0.0)


def glass(c):
    return TM("glass_" + c.strip("#"), "glass", _hx(c), rough=0.05, trans=0.55, coat=0.5, bump=0.0)


def dark():
    return gunmetal("#16181d")


# =========================================================================================== geometry helpers

def lathe(n, prof, mat, segs=24, x0=0.0, y=0.0, z=0.0):
    """Revolve [(radius, x)] around the X axis (radius 0 closes an end)."""
    return S.xlathe(n, prof, mat, segs, x0, y, z)


def tube(n, x0, x1, r, mat, y=0.0, z=0.0, segs=24, r1=None):
    return C.cyl(n, (x0, y, z), (x1, y, z), r, r if r1 is None else r1, mat, segs)


def cyl(n, p0, p1, r0, r1=None, mat=None, segs=16):
    return C.cyl(n, p0, p1, r0, r1, mat, segs)


def ring(n, x, r, th, mat, y=0.0, z=0.0, segs=24, rsegs=8):
    return C.torus(n, (x, y, z), r, th, mat, segs, rsegs, axis=X)


def rbox(n, c, s, mat, bev=0.012, segs=2, rot=None):
    return C.box(n, c, s, mat, bevel=bev, bevel_segs=segs, rot=rot)


def ball(n, c, r, mat, segs=20, rings=12):
    return C.sphere(n, c, r, mat, segs, rings)


def fillet(pts, segs=4):
    """Closed polygon [(x, z) or (x, z, radius)] -> points with each corner rounded (quadratic Bezier fillet)."""
    out = []
    k = len(pts)
    for i in range(k):
        p = pts[i]
        rad = p[2] if len(p) > 2 else 0.0
        P = V((p[0], p[1]))
        if rad <= 0:
            out.append((P.x, P.y))
            continue
        A = V(pts[i - 1][:2])
        B = V(pts[(i + 1) % k][:2])
        da, db = (A - P), (B - P)
        t1 = min(rad, da.length * 0.48)
        t2 = min(rad, db.length * 0.48)
        T1 = P + da.normalized() * t1
        T2 = P + db.normalized() * t2
        for j in range(segs + 1):
            t = j / segs
            q = T1 * (1 - t) ** 2 + P * 2 * t * (1 - t) + T2 * t * t
            out.append((q.x, q.y))
    return out


def prof(n, pts, depth, mat, y=0.0, bev=0.012, segs=2, fsegs=4, smooth=25):
    """Side-profile slab: filleted outline in the xz plane, extruded along Y, bevelled rim."""
    p = fillet(pts, fsegs)
    # drop near-duplicates
    q = [p[0]]
    for a in p[1:]:
        if (V(a) - V(q[-1])).length > 1e-4:
            q.append(a)
    if (V(q[0]) - V(q[-1])).length < 1e-4:
        q.pop()
    ob = C.extrude(n, q, depth, mat, bevel=bev, bevel_segs=segs)
    if y:
        ob.data.transform(Matrix.Translation((0, y, 0)))
    if smooth:
        C.smooth(ob, smooth)
    return ob


def sweep(n, pts, r, mat, segs=10, r_end=None, caps=True, smooth_k=0):
    """Tube along a 3D polyline (optionally Catmull-Rom smoothed with smooth_k points per segment)."""
    pts = [V(p) for p in pts]
    if smooth_k:
        pts = catmull(pts, smooth_k)
    bm = bmesh.new()
    rings_ = []
    cnt = len(pts)
    prev_a = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, cnt - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = V((0, -1, 0)) if abs(t.y) < 0.9 else V((0, 0, 1))
        a = t.cross(ref).normalized()
        if prev_a is not None and a.dot(prev_a) < 0:
            a = -a
        prev_a = a
        b = t.cross(a).normalized()
        rr = r if r_end is None else r + (r_end - r) * i / max(1, cnt - 1)
        rings_.append([bm.verts.new(p + (a * math.cos(2 * math.pi * k / segs) + b * math.sin(2 * math.pi * k / segs)) * rr)
                       for k in range(segs)])
    for ra, rb in zip(rings_, rings_[1:]):
        for k in range(segs):
            bm.faces.new((ra[k], ra[(k + 1) % segs], rb[(k + 1) % segs], rb[k]))
    if caps:
        bm.faces.new(rings_[0])
        bm.faces.new(list(reversed(rings_[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = C._obj_from_bm(n, bm, mat, True)
    ob.data.set_sharp_from_angle(angle=math.radians(60))
    return ob


def catmull(pts, k):
    pts = [V(p) for p in pts]
    out = []
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for j in range(k):
            t = j / k
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return out


def rivet(n, pos, r, mat, normal=(0, -1, 0)):
    p = V(pos)
    d = V(normal).normalized()
    return C.cyl(n, p - d * r * 0.3, p + d * r * 0.45, r, r * 0.55, mat, 8)


def rivets_ring(n, x, r, mat, count=8, z=0.0, y=0.0, size=0.012, front_only=True):
    o = []
    for i in range(count):
        a = 2 * math.pi * i / count
        ny, nz = -math.cos(a), math.sin(a)
        if front_only and ny > 0.35:
            continue
        o.append(rivet(n + "%d" % i, (x, y + ny * r, z + nz * r), size, mat, (0, ny, nz)))
    return o


def xf(objs, m):
    """Transform the geometry of objects (world space, objects stay at their location)."""
    for o in objs:
        loc = C.world_loc(o)
        o.data.transform(Matrix.Translation(-loc) @ m @ Matrix.Translation(loc))
    return objs


def move(objs, d):
    return xf(objs, Matrix.Translation(d))


def rot_x(objs, ang, z0=0.0, y0=0.0):
    """Rotate around the X axis line through (y0, z0)."""
    m = Matrix.Translation((0, y0, z0)) @ Matrix.Rotation(ang, 4, "X") @ Matrix.Translation((0, -y0, -z0))
    return xf(objs, m)


def scale(objs, s, center=(0, 0, 0)):
    c = V(center)
    return xf(objs, Matrix.Translation(c) @ Matrix.Diagonal((s, s, s, 1)) @ Matrix.Translation(-c))


# ------------------------------------------------------------------------- decals (thin raised shapes)

def _poly_obj(n, polys, mat, mapper, t=0.004):
    """polys: list of 2D (u, v) loops. mapper(u, v, w) -> 3D point, w in [0, 1] (0 = surface, 1 = raised t)."""
    bm = bmesh.new()
    for poly in polys:
        if len(poly) < 3:
            continue
        bot = [bm.verts.new(mapper(u, v, 0.0)) for (u, v) in poly]
        top = [bm.verts.new(mapper(u, v, 1.0)) for (u, v) in poly]
        try:
            bm.faces.new(top)
            bm.faces.new(list(reversed(bot)))
        except ValueError:
            pass
        k = len(poly)
        for i in range(k):
            j = (i + 1) % k
            try:
                bm.faces.new((bot[i], bot[j], top[j], top[i]))
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return C._obj_from_bm(n, bm, mat, False)


def tube_decal(n, polys, x, r, mat, z=0.0, y=0.0, theta=0.0, t=0.004, s=1.0):
    """2D shapes (u along +X, v up around the tube) wrapped onto a tube along X, centered at the front (-Y) + theta."""
    def mp(u, v, w):
        rr = r + t * (0.15 + w)
        a = v * s / r + theta
        return V((x + u * s, y - rr * math.cos(a), z + rr * math.sin(a)))
    return _poly_obj(n, polys, mat, mp, t)


def flat_decal(n, polys, center, mat, y=None, t=0.004, s=1.0):
    """2D shapes on a plane facing -Y at (center.x, y, center.z)."""
    cx, cy, cz = center

    def mp(u, v, w):
        return V((cx + u * s, cy - t * (0.15 + w), cz + v * s))
    return _poly_obj(n, polys, mat, mp, t)


def circle(r, k=20, cx=0.0, cy=0.0):
    return [(cx + r * math.cos(2 * math.pi * i / k), cy + r * math.sin(2 * math.pi * i / k)) for i in range(k)]


def trefoil(r=1.0):
    """Radiation symbol: center dot + three 60 degree blades."""
    polys = [circle(0.18 * r, 14)]
    for b in range(3):
        a0 = math.radians(90 + 120 * b - 30)
        a1 = math.radians(90 + 120 * b + 30)
        k = 8
        outer = [(r * math.cos(a0 + (a1 - a0) * i / k), r * math.sin(a0 + (a1 - a0) * i / k)) for i in range(k + 1)]
        inner = [(0.3 * r * math.cos(a1 - (a1 - a0) * i / k), 0.3 * r * math.sin(a1 - (a1 - a0) * i / k)) for i in range(k + 1)]
        polys.append(outer + inner)
    return polys


def triangle(r=1.0):
    return [[(0, r), (-r * 0.9, -r * 0.6), (r * 0.9, -r * 0.6)]]


def bang(r=1.0):
    return [[(-0.08 * r, 0.5 * r), (0.08 * r, 0.5 * r), (0.05 * r, -0.12 * r), (-0.05 * r, -0.12 * r)], circle(0.08 * r, 8, 0, -0.32 * r)]


def bolt_shape(r=1.0):
    return [[(0.15 * r, r), (-0.45 * r, -0.05 * r), (-0.02 * r, -0.05 * r), (-0.2 * r, -r), (0.45 * r, 0.12 * r), (0.04 * r, 0.12 * r)]]


def star(r=1.0, k=5, inner=0.45):
    return [[((r if i % 2 == 0 else r * inner) * math.cos(math.pi / 2 + i * math.pi / k),
              (r if i % 2 == 0 else r * inner) * math.sin(math.pi / 2 + i * math.pi / k)) for i in range(2 * k)]]


def arrow(r=1.0):
    return [[(-r, 0.25 * r), (0.1 * r, 0.25 * r), (0.1 * r, 0.6 * r), (r, 0), (0.1 * r, -0.6 * r), (0.1 * r, -0.25 * r),
             (-r, -0.25 * r)]]


def cross_shape(r=1.0, w=0.22):
    a = []
    for ang in (45, -45):
        c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        pts = [(-r, -w * r), (r, -w * r), (r, w * r), (-r, w * r)]
        a.append([(x * c - y * s, x * s + y * c) for (x, y) in pts])
    return a


def rect(w, h, cx=0.0, cy=0.0):
    return [(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h / 2), (cx - w / 2, cy + h / 2)]


def warning_sign(n, x, r, z=0.0, size=0.05, theta=0.0, y=0.0):
    """Yellow triangle with a black ! wrapped on a tube."""
    o = [tube_decal(n + "_wt", triangle(size), x, r, paint("#262b34"), z, y, theta, 0.003),
         tube_decal(n + "_wy", triangle(size * 0.78), x, r, paint("#ffd23a", chips=0.2), z, y, theta, 0.0055),
         tube_decal(n + "_wb", bang(size * 0.62), x, r, paint("#262b34"), z, y, theta, 0.0075)]
    return o


def rad_sign(n, x, r, z=0.0, size=0.06, theta=0.0, y=0.0, bg="#ffd23a"):
    return [tube_decal(n + "_rbg", [circle(size, 24)], x, r, paint(bg, chips=0.2), z, y, theta, 0.003),
            tube_decal(n + "_rtf", trefoil(size * 0.82), x, r, paint("#262b34"), z, y, theta, 0.006)]


# ------------------------------------------------------------------------- common gun parts

def pistol_grip(n, mat, x=0.0, z=0.0, h=0.26, w=0.095, depth=0.075, slant=0.3, grooves=True):
    """Grip hanging down from (x, z): filleted profile with finger grooves, slanted back by slant (tan)."""
    pts = [(x - w * 0.55, z + 0.02, 0.0), (x + w * 0.5, z + 0.02, 0.0)]
    if grooves:
        for i in range(3):
            zz = z - h * (0.22 + 0.25 * i)
            pts.append((x + w * 0.5 + 0.012, zz + 0.03, 0.015))
            pts.append((x + w * 0.5 - 0.004, zz, 0.012))
    pts += [(x + w * 0.52, z - h, 0.03), (x - w * 0.55, z - h - 0.01, 0.035), (x - w * 0.62, z - h * 0.5, 0.06)]
    pts = [(px + (pz - z) * slant, pz, rr) for (px, pz, rr) in pts]
    o = [prof(n + "_grip", pts, depth, mat, bev=0.014)]
    # side panel screw
    o.append(rivet(n + "_gscr", (x - 0.008 + (-h * 0.45) * slant, -depth / 2 - 0.002, z - h * 0.45), 0.011, steel()))
    return o


def trigger_guard(n, mat, x=0.0, z=0.0, w=0.13, h=0.085, r=0.008):
    pts = [(x + 0.01, 0, z), (x + 0.012, 0, z - h * 0.75), (x + w * 0.45, 0, z - h), (x + w * 0.9, 0, z - h * 0.6),
           (x + w, 0, z)]
    o = [sweep(n + "_tg", pts, r, mat, 8, smooth_k=4)]
    trig = [(x + w * 0.35, z + 0.01, 0), (x + w * 0.52, z + 0.01, 0), (x + w * 0.5, z - h * 0.35, 0.02),
            (x + w * 0.38, z - h * 0.7, 0.01), (x + w * 0.33, z - h * 0.62, 0.0), (x + w * 0.4, z - h * 0.3, 0.02)]
    o.append(prof(n + "_tr", trig, 0.022, mat, bev=0.004, segs=1))
    return o


def scope(n, x0, x1, z, r=0.042, body=None, lens="#5fb8ff"):
    body = body or gunmetal("#2a2f38")
    L = x1 - x0
    o = [lathe(n + "_sc", [(0, x0), (r * 1.25, x0), (r * 1.35, x0 + 0.03), (r * 1.3, x0 + 0.08), (r, x0 + 0.12),
                           (r, x1 - 0.12), (r * 1.4, x1 - 0.05), (r * 1.45, x1), (r * 1.2, x1), (r * 1.2, x1 - 0.01),
                           (0, x1 - 0.01)], body, 24, z=z),
         ball(n + "_lens", (x1 - 0.012, 0, z), (0.012, r * 1.2, r * 1.2), glass(lens), 16, 10),
         ball(n + "_lens0", (x0 + 0.004, 0, z), (0.008, r * 1.0, r * 1.0), glass("#4a8ad8"), 12, 8),
         ]
    o.append(cyl(n + "_tu", (x0 + L * 0.5, 0, z), (x0 + L * 0.5, 0, z + r * 1.8), r * 0.55, r * 0.5, body, 12))
    o.append(cyl(n + "_ts", (x0 + L * 0.5, 0, z), (x0 + L * 0.5, -r * 1.8, z), r * 0.5, r * 0.45, body, 12))
    for i, xx in enumerate((x0 + L * 0.25, x0 + L * 0.72)):
        o.append(rbox(n + "_mt%d" % i, (xx, 0, z - r * 1.1), (0.035, r * 1.2, r * 1.0), body, 0.006))
    return o


# =========================================================================================== launchers & rockets

def launcher(n, body, trim, length=1.35, r=0.13, z=0.2, x0=-0.5, rear=None, muzzle=None, haz=True, sight=True,
             decal="warning", sleeve=True, mscale=1.0, grip_mat=None, band_mat=None):
    """Shoulder launcher: body tube, hollow flared rear bell, hollow muzzle with a lip, bands with rivets, hazard band,
    rubber sleeve, pistol grip + guard, fore grip, sight with lens, decal."""
    x1 = x0 + length
    rear = rear or body
    muzzle = muzzle or body
    band_mat = band_mat or steel()
    grip_mat = grip_mat or rubber()
    o = [lathe(n + "_body", [(0, x0 + 0.1), (r * 0.97, x0 + 0.1), (r, x0 + 0.13), (r, x1 - 0.2), (r * 0.97, x1 - 0.17),
                             (0, x1 - 0.17)], body, 28, z=z)]
    rb = r
    o.append(lathe(n + "_rear", [(0, x0 + 0.05), (rb * 0.8, x0 + 0.05), (rb * 1.12, x0 - 0.09), (rb * 1.3, x0 - 0.09),
                                 (rb * 1.34, x0 - 0.07), (rb * 1.12, x0 + 0.04), (rb * 1.07, x0 + 0.06), (rb * 1.07, x0 + 0.2),
                                 (rb * 1.0, x0 + 0.21), (0, x0 + 0.21)], rear, 28, z=z))
    o.append(tube(n + "_rdk", x0 + 0.045, x0 + 0.06, rb * 0.79, dark(), z=z))
    m = r * mscale
    o.append(lathe(n + "_muz", [(0, x1 - 0.25), (r * 1.0, x1 - 0.25), (m * 1.1, x1 - 0.21), (m * 1.1, x1 - 0.07),
                                (m * 1.2, x1 - 0.05), (m * 1.22, x1 - 0.015), (m * 1.18, x1), (m * 0.86, x1),
                                (m * 0.84, x1 - 0.13), (0, x1 - 0.13)], muzzle, 28, z=z))
    o.append(tube(n + "_mdk", x1 - 0.14, x1 - 0.125, m * 0.83, dark(), z=z))
    for i, bx in enumerate((x0 + 0.27, x1 - 0.31)):
        o.append(ring(n + "_bd%d" % i, bx, r * 1.02, 0.016, band_mat, z=z))
        o += rivets_ring(n + "_rv%d_" % i, bx + 0.03, r * 1.0, band_mat, 10, z=z, size=0.01)
    if haz:
        o.append(lathe(n + "_hz", [(0, x1 - 0.44), (r * 1.012, x1 - 0.44), (r * 1.012, x1 - 0.35), (0, x1 - 0.35)],
                       hazard(), 28, z=z))
    if sleeve:
        a, b = x0 + 0.33, x0 + 0.47
        o.append(lathe(n + "_slv", [(0, a), (r * 1.0, a), (r * 1.06, a + 0.015), (r * 1.06, b - 0.015), (r * 1.0, b),
                                    (0, b)], rubber("#24272d", "ribs"), 28, z=z))
    gz = z - r * 0.8
    o.append(rbox(n + "_gm", (0.05, 0, gz - 0.005), (0.2, 0.075, 0.06), trim, 0.012))
    o += pistol_grip(n + "_g", grip_mat, x=-0.0, z=gz - 0.02, h=0.24)
    o += trigger_guard(n + "_t", gunmetal(), x=0.035, z=gz - 0.035)
    fx = min(0.45, x1 - 0.55)
    o.append(rbox(n + "_fm", (fx, 0, gz - 0.005), (0.08, 0.06, 0.05), trim, 0.01))
    o += pistol_grip(n + "_fg", grip_mat, x=fx, z=gz - 0.02, h=0.15, w=0.07, depth=0.06, slant=-0.05, grooves=False)
    if sight:
        sz = z + r + 0.02
        o.append(rbox(n + "_rail", (0.12, 0, sz - 0.01), (0.3, 0.05, 0.025), gunmetal(), 0.006))
        for i in range(6):
            o.append(rbox(n + "_rl%d" % i, (0.0 + i * 0.048, 0, sz + 0.006), (0.022, 0.054, 0.01), gunmetal(), 0.002, 1))
        o.append(rbox(n + "_sp0", (0.05, 0, sz + 0.035), (0.03, 0.03, 0.06), gunmetal(), 0.006))
        o.append(rbox(n + "_sp1", (0.22, 0, sz + 0.035), (0.03, 0.03, 0.06), gunmetal(), 0.006))
        o.append(lathe(n + "_sh", [(0, 0.02), (0.03, 0.02), (0.034, 0.03), (0.034, 0.24), (0.04, 0.26), (0.034, 0.27),
                                   (0, 0.265)], gunmetal("#2a2f38"), 18, z=sz + 0.075))
        o.append(ball(n + "_sl", (0.265, 0, sz + 0.075), (0.008, 0.03, 0.03), glass("#ff5a3a"), 14, 8))
    if decal == "warning":
        o += warning_sign(n, x0 + length * 0.52, r, z=z, size=r * 0.42, theta=0.25)
    elif decal == "rad":
        o += rad_sign(n, x0 + length * 0.52, r, z=z, size=r * 0.42, theta=0.25)
    elif decal == "bolt":
        o.append(tube_decal(n + "_bl", bolt_shape(r * 0.45), x0 + length * 0.52, r, paint("#ffd23a"), z, 0, 0.25))
    elif decal == "arrow":
        o.append(tube_decal(n + "_ar", arrow(r * 0.5), x0 + length * 0.52, r, paint("#f5f7fb"), z, 0, 0.2))
    return o, V((x1, 0, z))


def rocket(n, body, nose, fin_mat, L=0.8, r=0.1, nose_len=0.3, nfins=4, band=None, fin_h=None, fin_len=None,
           nozzle=True, blunt=0.0, fin_rot=45.0, stripe=None):
    """Projectile along +X starting at x=0 (tail) on the X axis. Ogive nose, nozzle, swept fins, optional band."""
    xb = L - nose_len
    o = [lathe(n + "_rb", [(0, 0.05), (r * 0.72, 0.05), (r * 0.92, 0.08), (r, 0.14), (r, xb), (0, xb)], body, 28)]
    pts = [(0, xb - 0.001)]
    for i in range(11):
        t = i / 10
        rr = r * max(0.0, (1 - t ** 2.0)) ** 0.55
        rr = max(rr, r * blunt)
        pts.append((rr, xb + t * nose_len))
    pts.append((0, L))
    o.append(lathe(n + "_rn", pts, nose, 28))
    o.append(ring(n + "_rj", xb, r * 1.0, 0.012, band or steel()))
    if stripe is not None:
        o.append(lathe(n + "_rs", [(0, xb - 0.12), (r * 1.01, xb - 0.12), (r * 1.01, xb - 0.06), (0, xb - 0.06)], stripe, 28))
    if nozzle:
        o.append(lathe(n + "_rz", [(0, 0.06), (r * 0.6, 0.06), (r * 0.7, -0.05), (r * 0.62, -0.05), (r * 0.45, 0.02),
                                   (0, 0.02)], gunmetal("#2a2f38"), 20))
    fh = fin_h or r * 1.0
    fl = fin_len or L * 0.32
    for i in range(nfins):
        f = prof(n + "_rf%d" % i, [(0.02, r * 0.8, 0), (fl, r * 0.8, 0.0), (fl * 0.6, r + fh * 0.4, 0.04),
                                   (0.0, r + fh, 0.02), (-0.06, r + fh * 0.95, 0.02)], 0.022, fin_mat, bev=0.006, segs=1)
        rot_x([f], math.radians(fin_rot + 360.0 * i / nfins))
        o.append(f)
    return o


def place(objs, at=(0, 0, 0), yaw=0.0, pitch=0.0, s=1.0):
    """Scale, pitch (around Y, positive lifts +X), yaw (around Z) then move a group built at the origin."""
    m = Matrix.Translation(at) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(-pitch, 4, "Y") @ Matrix.Diagonal((s, s, s, 1))
    return xf(objs, m)


# =========================================================================================== guns

def revolver(n, frame, grip, barrel=None, length=0.52, z=0.1, bore=0.032):
    barrel = barrel or frame
    o = [prof(n + "_fr", [(-0.1, z + 0.06, 0.02), (0.13, z + 0.06, 0.01), (0.14, z - 0.05, 0.0), (0.04, z - 0.06, 0.01),
                          (0.0, z - 0.09, 0.02), (-0.09, z - 0.06, 0.02), (-0.13, z + 0.0, 0.03)], 0.07, frame)]
    cx0, cx1 = -0.035, 0.11
    o.append(lathe(n + "_cy", [(0, cx0), (0.055, cx0), (0.065, cx0 + 0.01), (0.065, cx1 - 0.01), (0.055, cx1), (0, cx1)],
                   steel("#9aa3ae"), 24, z=z))
    for i in range(6):
        a = 2 * math.pi * i / 6 + math.pi / 6
        o.append(rbox(n + "_fl%d" % i, ((cx0 + cx1) / 2, -0.066 * math.cos(a), z + 0.066 * math.sin(a)),
                      (0.09, 0.012, 0.022), dark(), 0.004, 1, rot=Matrix.Rotation(a, 4, "X")))
    o.append(lathe(n + "_br", [(0, 0.1), (0.04, 0.1), (0.036, 0.13), (0.034, length - 0.03), (0.04, length - 0.02),
                               (0.04, length), (bore * 0.6, length), (bore * 0.6, length - 0.03), (0, length - 0.03)],
                   barrel, 20, z=z + 0.025))
    o.append(rbox(n + "_rib", ((0.13 + length) / 2, 0, z + 0.065), (length - 0.13, 0.022, 0.02), barrel, 0.006))
    o.append(rbox(n + "_ejr", ((0.13 + length) / 2 - 0.02, 0, z - 0.012), (length - 0.2, 0.03, 0.03), barrel, 0.01))
    o.append(prof(n + "_fs", [(length - 0.05, z + 0.07, 0), (length - 0.01, z + 0.07, 0), (length - 0.02, z + 0.1, 0.01),
                              (length - 0.045, z + 0.1, 0)], 0.014, barrel, bev=0.003, segs=1))
    o.append(prof(n + "_hm", [(-0.1, z + 0.04, 0), (-0.07, z + 0.06, 0), (-0.11, z + 0.12, 0.012), (-0.15, z + 0.12, 0.01),
                              (-0.13, z + 0.07, 0.0)], 0.03, gunmetal(), bev=0.005, segs=1))
    o += pistol_grip(n + "_g", grip, x=-0.07, z=z - 0.05, h=0.22, w=0.1, depth=0.08, slant=0.45)
    o += trigger_guard(n + "_t", frame, x=-0.03, z=z - 0.06, w=0.12, h=0.08)
    o.append(rivet(n + "_pin", (0.05, -0.036, z - 0.035), 0.01, steel()))
    o.append(rivet(n + "_pin2", (-0.08, -0.036, z + 0.02), 0.01, steel()))
    return o, V((length, 0, z + 0.025))


def rifle(n, body, stock, length=1.25, z=0.1, bore=0.03, double=False, scope_on=False, mag=True, pump=False,
          barrel=None):
    """Long gun: receiver profile, barrel(s), wooden stock + fore-end, guard, optional scope / magazine / pump."""
    barrel = barrel or gunmetal()
    x0 = -0.5
    x1 = x0 + length
    o = [prof(n + "_rc", [(-0.17, z + 0.05, 0.02), (0.26, z + 0.05, 0.01), (0.28, z - 0.02, 0.0), (0.2, z - 0.06, 0.01),
                          (-0.1, z - 0.06, 0.01), (-0.17, z - 0.03, 0.02)], 0.085, body)]
    bz = z + 0.02
    if double:
        for k, yy in enumerate((-0.033, 0.033)):
            o.append(lathe(n + "_b%d" % k, [(0, 0.2), (bore * 1.2, 0.2), (bore * 1.1, 0.25), (bore * 1.05, x1 - 0.03),
                                            (bore * 1.25, x1 - 0.02), (bore * 1.25, x1), (bore * 0.75, x1),
                                            (bore * 0.75, x1 - 0.04), (0, x1 - 0.04)], barrel, 18, y=yy, z=bz))
        o.append(rbox(n + "_rib", ((0.25 + x1) / 2, 0, bz + bore + 0.004), (x1 - 0.27, 0.025, 0.012), barrel, 0.004))
    else:
        o.append(lathe(n + "_b", [(0, 0.2), (bore * 1.5, 0.2), (bore * 1.2, 0.28), (bore * 1.05, x1 - 0.1),
                                  (bore * 1.6, x1 - 0.08), (bore * 1.6, x1), (bore * 0.7, x1), (bore * 0.7, x1 - 0.04),
                                  (0, x1 - 0.04)], barrel, 18, z=bz))
        for i in range(2):
            o.append(rbox(n + "_mb%d" % i, (x1 - 0.06 + i * 0.03, -bore * 1.5, bz), (0.012, 0.01, bore * 1.6), dark(), 0.002, 1))
        o.append(prof(n + "_fsight", [(x1 - 0.12, bz + bore, 0), (x1 - 0.09, bz + bore, 0), (x1 - 0.095, bz + bore + 0.04, 0.005),
                                      (x1 - 0.11, bz + bore + 0.04, 0)], 0.012, barrel, bev=0.003, segs=1))
    o.append(prof(n + "_st", [(-0.15, z + 0.045, 0.02), (-0.15, z - 0.05, 0.02), (-0.3, z - 0.11, 0.06),
                              (x0 - 0.06, z - 0.15, 0.02), (x0 - 0.08, z + 0.04, 0.02), (x0 + 0.05, z + 0.05, 0.03),
                              (-0.3, z + 0.02, 0.05)], 0.075, stock, bev=0.016))
    o.append(prof(n + "_bp", [(x0 - 0.1, z - 0.155, 0.01), (x0 - 0.06, z - 0.155, 0), (x0 - 0.04, z + 0.05, 0),
                              (x0 - 0.085, z + 0.05, 0.01)], 0.08, rubber("#2a2420", "ribs"), bev=0.01))
    o += pistol_grip(n + "_g", stock, x=-0.12, z=z - 0.04, h=0.17, w=0.085, depth=0.07, slant=0.55, grooves=False)
    o += trigger_guard(n + "_t", body, x=-0.07, z=z - 0.055, w=0.13, h=0.075)
    fe0, fe1 = 0.3, min(0.72, x1 - 0.25)
    if pump:
        o.append(lathe(n + "_pump", [(0, fe0), (0.045, fe0), (0.055, fe0 + 0.02), (0.055, fe1 - 0.02), (0.045, fe1), (0, fe1)],
                       stock, 20, z=bz - 0.055))
        for i in range(6):
            xx = fe0 + 0.04 + i * (fe1 - fe0 - 0.08) / 5
            o.append(ring(n + "_pr%d" % i, xx, 0.056, 0.006, stock, z=bz - 0.055, segs=20, rsegs=4))
        o.append(tube(n + "_mt", 0.25, x1 - 0.12, 0.028, barrel, z=bz - 0.055))
    else:
        o.append(prof(n + "_fe", [(fe0 - 0.04, bz + 0.005, 0.01), (fe1, bz + 0.005, 0.0), (fe1 + 0.02, bz - 0.04, 0.03),
                                  (fe0, bz - 0.07, 0.03), (fe0 - 0.06, bz - 0.05, 0.01)], 0.08, stock, bev=0.014))
    for i, xx in enumerate((fe1 + 0.04, x1 - 0.2)):
        if xx < x1 - 0.1:
            rr = bore * 1.4 + (0.035 if double else 0)
            o.append(lathe(n + "_bb%d" % i, [(0, xx), (rr, xx), (rr, xx + 0.025), (0, xx + 0.025)], steel(), 16, z=bz))
    if mag:
        o.append(prof(n + "_mg", [(0.1, z - 0.05, 0), (0.2, z - 0.05, 0), (0.22, z - 0.22, 0.02), (0.13, z - 0.24, 0.02)],
                      0.06, gunmetal("#2a2f38"), bev=0.01))
    o.append(rbox(n + "_ej", (0.08, -0.043, z + 0.015), (0.12, 0.006, 0.035), dark(), 0.004, 1))
    o.append(cyl(n + "_bh", (0.02, -0.04, z + 0.02), (0.0, -0.11, z - 0.0), 0.011, 0.011, steel(), 8))
    o.append(ball(n + "_bk", (0.0, -0.115, z - 0.002), 0.022, steel(), 12, 8))
    for i, xx in enumerate((-0.12, 0.2)):
        o.append(rivet(n + "_sc%d" % i, (xx, -0.044, z - 0.02), 0.011, steel()))
    if scope_on:
        o += scope(n, -0.12, 0.3, z + 0.12)
    return o, V((x1, 0, bz))


def blaster(n, shell, core, trim, length=0.95, r=0.1, z=0.15, x0=-0.32, coils=3, emitter="cone", tank=None,
            fins=True, grip_mat=None):
    """Sci-fi gun: panelled lathe body, glowing core window with coils, emitter, fin, grip."""
    x1 = x0 + length
    o = [lathe(n + "_body", [(0, x0), (r * 0.9, x0), (r * 1.15, x0 + 0.04), (r * 1.3, x0 + 0.16), (r * 1.28, x0 + length * 0.5),
                             (r * 1.0, x0 + length * 0.62), (r * 0.82, x0 + length * 0.66), (0, x0 + length * 0.66)],
                   shell, 28, z=z)]
    cx0, cx1 = x0 + length * 0.18, x0 + length * 0.5
    o.append(tube(n + "_core", cx0, cx1, r * 1.34, core, z=z))
    for i in range(coils):
        xx = cx0 + 0.02 + (cx1 - cx0 - 0.04) * (i + 0.5) / coils
        o.append(ring(n + "_c%d" % i, xx, r * 1.36, 0.022, trim, z=z))
    o.append(ring(n + "_ce0", cx0, r * 1.36, 0.02, trim, z=z))
    o.append(ring(n + "_ce1", cx1, r * 1.36, 0.02, trim, z=z))
    bx0 = x0 + length * 0.62
    o.append(lathe(n + "_br", [(0, bx0), (r * 0.62, bx0), (r * 0.6, x1 - 0.12), (r * 0.7, x1 - 0.1), (0, x1 - 0.1)],
                   trim, 24, z=z))
    if emitter == "cone":
        o.append(lathe(n + "_em", [(0, x1 - 0.12), (r * 0.72, x1 - 0.12), (r * 0.95, x1 - 0.02), (r * 0.98, x1),
                                   (r * 0.7, x1), (r * 0.5, x1 - 0.05), (0, x1 - 0.05)], shell, 24, z=z))
        o.append(ball(n + "_emg", (x1 - 0.05, 0, z), (0.03, r * 0.6, r * 0.6), core, 16, 10))
    elif emitter == "ring":
        for i in range(3):
            o.append(ring(n + "_er%d" % i, x1 - 0.1 + i * 0.045, r * (0.75 + 0.1 * i), 0.018, trim, z=z))
        o.append(ball(n + "_emg", (x1 - 0.02, 0, z), (0.04, r * 0.55, r * 0.55), core, 16, 10))
    elif emitter == "wide":
        o.append(lathe(n + "_em", [(0, x1 - 0.2), (r * 0.9, x1 - 0.2), (r * 1.4, x1 - 0.04), (r * 1.45, x1), (r * 1.1, x1),
                                   (r * 0.8, x1 - 0.08), (0, x1 - 0.08)], shell, 28, z=z))
        o.append(tube(n + "_emd", x1 - 0.085, x1 - 0.075, r * 0.8, core, z=z))
    o.append(lathe(n + "_cap", [(0, x0 - 0.04), (r * 0.6, x0 - 0.04), (r * 0.9, x0 + 0.0), (0, x0 + 0.0)], trim, 24, z=z))
    for i in range(4):
        o.append(rbox(n + "_v%d" % i, (x0 + length * 0.56 + i * 0.022, -r * 1.05, z + r * 0.35), (0.01, 0.02, r * 0.5),
                      dark(), 0.003, 1))
    if fins:
        o.append(prof(n + "_fin", [(x0 + 0.06, z + r * 1.1, 0), (x0 + length * 0.42, z + r * 1.1, 0),
                                   (x0 + length * 0.3, z + r * 1.9, 0.03), (x0 + 0.02, z + r * 1.75, 0.03)], 0.03, trim,
                      bev=0.008))
    o += pistol_grip(n + "_g", grip_mat or rubber(), x=0.0, z=z - r * 1.0, h=0.22, w=0.09, slant=0.3)
    o += trigger_guard(n + "_t", trim, x=0.035, z=z - r * 1.05, w=0.12, h=0.075)
    if tank:
        o.append(lathe(n + "_tk", [(0, x0 + 0.02), (0.05, x0 + 0.02), (0.06, x0 + 0.04), (0.06, x0 + 0.26), (0.05, x0 + 0.28),
                                   (0, x0 + 0.28)], tank, 18, z=z + r * 1.6))
        o.append(rbox(n + "_tkm", (x0 + 0.15, 0, z + r * 1.25), (0.08, 0.03, 0.05), trim, 0.008))
    return o, V((x1, 0, z))


# =========================================================================================== throwables

def frag_grenade(n, body_mat, lever_mat=None, r=0.15, c=(0.1, 0, 0.12), pin=True, segs=True):
    lever_mat = lever_mat or steel("#b8c0ca")
    cx, cy, cz = c
    pr = [(0, -r * 1.1)]
    for i in range(1, 16):
        a = math.pi * i / 16
        pr.append((r * math.sin(a) * (1.0 - 0.06 * math.cos(a)), -r * 1.1 * math.cos(a) * (1.0 if a < math.pi / 2 else 0.95)))
    pr.append((0, r * 1.05))
    o = [C.lathe(n + "_body", pr, body_mat, 28, center=(cx, cy, cz), axis=(0, 0, 1))]
    if segs:
        for k in range(3):
            zz = (k - 1) * r * 0.55
            rr = r * math.sqrt(max(0.0, 1 - ((k - 1) * 0.5) ** 2))
            o.append(C.torus(n + "_gr%d" % k, (cx, cy, cz + zz), rr * 0.988, 0.0045, dark(), 28, 4))
    top = cz + r * 1.0
    o.append(cyl(n + "_neck", (cx, cy, top - 0.02), (cx, cy, top + 0.05), r * 0.32, r * 0.3, steel("#8d99a8"), 16))
    o.append(cyl(n + "_cap", (cx, cy, top + 0.05), (cx, cy, top + 0.08), r * 0.36, r * 0.33, steel("#8d99a8"), 16))
    lv = [(cx - r * 0.1, top + 0.075), (cx + r * 0.42, top + 0.07), (cx + r * 0.85, top - 0.02), (cx + r * 1.08, cz + r * 0.3),
          (cx + r * 1.12, cz - r * 0.35)]
    o.append(sweep(n + "_lv", [(p[0], cy, p[1]) for p in lv], 0.016, lever_mat, 6, smooth_k=4))
    o.append(rbox(n + "_lvb", (cx + r * 0.2, cy, top + 0.075), (0.09, 0.05, 0.015), lever_mat, 0.005))
    if pin:
        o.append(C.torus(n + "_pin", (cx - r * 0.55, cy - 0.02, top + 0.06), r * 0.33, 0.009, steel("#d0d6de"), 20, 6,
                         axis=(0.3, 1, 0.2)))
        o.append(cyl(n + "_pn", (cx - r * 0.25, cy, top + 0.06), (cx + r * 0.1, cy, top + 0.06), 0.008, 0.008, steel(), 6))
    return o


def bottle(n, glass_mat, label_mat, c=(0.1, 0, 0.0), h=0.42, r=0.11, rag=True, cork=None, mark=True):
    cx, cy, cz = c
    pr = [(0, 0), (r * 0.92, 0), (r, 0.02), (r, h * 0.55), (r * 0.82, h * 0.66), (r * 0.36, h * 0.76), (r * 0.32, h * 0.95),
          (r * 0.38, h * 0.97), (r * 0.36, h), (0, h)]
    o = [C.lathe(n + "_bt", pr, glass_mat, 28, center=(cx, cy, cz), axis=(0, 0, 1))]
    if label_mat is not None:
        o.append(C.lathe(n + "_lb", [(0, h * 0.15), (r * 1.01, h * 0.15), (r * 1.01, h * 0.45), (0, h * 0.45)], label_mat, 28,
                         center=(cx, cy, cz), axis=(0, 0, 1)))
        if mark:
            o.append(flat_decal(n + "_x", cross_shape(r * 0.45, 0.2), (cx, cy - r * 1.01, cz + h * 0.3),
                                paint("#3a2414", chips=0.1)))
    if rag:
        top = cz + h
        o.append(sweep(n + "_rag", [(cx, cy, top - 0.03), (cx + 0.005, cy, top + 0.04), (cx + 0.05, cy - 0.01, top + 0.09),
                                    (cx + 0.11, cy, top + 0.08), (cx + 0.15, cy + 0.01, top + 0.03)], 0.03,
                        TM("rag", "fabric", "#e8dcc0", rough=0.9, bump=0.5), 8, r_end=0.012, smooth_k=3))
    if cork:
        o.append(cyl(n + "_ck", (cx, cy, cz + h - 0.02), (cx, cy, cz + h + 0.05), r * 0.36, r * 0.4, cork, 14))
    return o


def blob_ball(n, c, r, mat, seed=3, lumps=0.08, segs=28, rings=18, squash=1.0):
    """Lumpy sphere (snow, slime, rock-ish)."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    ph = [rnd.uniform(0, 6.28) for _ in range(6)]
    for v in bm.verts:
        p = v.co.copy()
        d = 1.0 + lumps * (math.sin(p.x * 3.1 + ph[0]) * math.sin(p.y * 2.7 + ph[1]) +
                           0.6 * math.sin(p.z * 4.3 + ph[2]) * math.cos(p.x * 3.7 + ph[3]) +
                           0.4 * math.sin((p.x + p.y + p.z) * 6.1 + ph[4]))
        v.co = V((p.x * r * d, p.y * r * d, p.z * r * d * squash))
    bmesh.ops.translate(bm, vec=V(c), verts=bm.verts)
    return C._obj_from_bm(n, bm, mat, True)


def rock_mesh(n, c, r, mat, seed=1, sub=2, jitter=0.18, scale=(1.15, 0.9, 0.95)):
    o = C.ico(n, c, r, mat, subdiv=sub, smooth=False, scale=scale, seed=seed, jitter=jitter)
    C.smooth(o, 32)
    return o


# =========================================================================================== parts used by several weapons

def spiked_ball(n, c, r, mat, spike_mat=None, spike=0.6):
    spike_mat = spike_mat or mat
    o = [ball(n + "_core", c, r, mat, 20, 14)]
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    dirs = [v.co.copy().normalized() for v in bm.verts]
    bm.free()
    for i, d in enumerate(dirs):
        p0 = V(c) + d * r * 0.85
        o.append(cyl(n + "_sp%d" % i, p0, p0 + d * r * spike, r * 0.2, 0.0, spike_mat, 8))
    return o


def drill_cone(n, x0, length, r, mat, flute_mat=None, turns=3.0):
    """Conical drill bit along +X with a raised spiral flute."""
    flute_mat = flute_mat or mat
    o = [lathe(n + "_cone", [(0, x0), (r, x0), (r * 0.98, x0 + length * 0.1), (0.0, x0 + length)], mat, 24)]
    pts = []
    k = 60
    for i in range(k + 1):
        t = i / k
        a = t * turns * 2 * math.pi
        rr = r * (1.0 - t * 0.95) * 0.98 + 0.004
        pts.append((x0 + length * t * 0.97, -rr * math.cos(a), rr * math.sin(a)))
    o.append(sweep(n + "_fl", pts, r * 0.11, flute_mat, 8, r_end=r * 0.03))
    return o


def egg_mesh(n, c, r, mat, tilt=0.0):
    pr = [(0, -r)]
    for i in range(1, 16):
        a = math.pi * i / 16
        rr = r * 0.78 * math.sin(a) * (1 + 0.12 * math.cos(a))
        pr.append((rr, -r * math.cos(a)))
    pr.append((0, r))
    ob = C.lathe(n, pr, mat, 28, center=(0, 0, 0), axis=(0, 0, 1))
    m = Matrix.Translation(c) @ Matrix.Rotation(tilt, 4, "Y")
    ob.data.transform(m)
    return ob


def alarm_clock(n, c, r, body):
    cx, cy, cz = c
    o = [cyl(n + "_ck", (cx, cy + 0.03, cz), (cx, cy - 0.03, cz), r, r, body, 24),
         cyl(n + "_ckf", (cx, cy - 0.03, cz), (cx, cy - 0.034, cz), r * 0.85, r * 0.85, paint("#f5f2e6", chips=0.1), 24)]
    for i, a in enumerate((-0.6, 0.6)):
        o.append(ball(n + "_bell%d" % i, (cx + math.sin(a) * r * 1.05, cy, cz + math.cos(a) * r * 1.05), r * 0.38, body, 14, 8))
    o.append(rbox(n + "_h1", (cx + r * 0.2, cy - 0.04, cz + r * 0.1), (r * 0.45, 0.006, 0.012), dark(), 0.002, 1,
                  rot=Matrix.Rotation(0.5, 4, "Y")))
    o.append(rbox(n + "_h2", (cx - r * 0.0, cy - 0.04, cz + r * 0.28), (0.012, 0.006, r * 0.6), dark(), 0.002, 1))
    for i in range(12):
        a = 2 * math.pi * i / 12
        o.append(rbox(n + "_tk%d" % i, (cx + math.cos(a) * r * 0.7, cy - 0.036, cz + math.sin(a) * r * 0.7),
                      (0.008, 0.004, 0.008), dark(), 0.0, 1))
    return o


def glove_mesh(n, mat, cuff_mat):
    """Boxing glove pointing +X, centered around (0.22, 0, 0.08)."""
    o = [blob_ball(n + "_fist", (0.3, 0, 0.08), 0.17, mat, seed=7, lumps=0.03, squash=0.9)]
    o[0].data.transform(Matrix.Translation((0.3, 0, 0.08)) @ Matrix.Diagonal((1.15, 0.95, 1.0, 1)) @
                        Matrix.Translation((-0.3, 0, -0.08)))
    o.append(blob_ball(n + "_thumb", (0.3, -0.13, 0.02), 0.07, mat, seed=3, lumps=0.02))
    o[-1].data.transform(Matrix.Translation((0.3, -0.13, 0.02)) @ Matrix.Diagonal((1.6, 0.9, 0.9, 1)) @
                         Matrix.Translation((-0.3, 0.13, -0.02)))
    o.append(lathe(n + "_cuff", [(0, -0.08), (0.11, -0.08), (0.12, -0.06), (0.12, 0.1), (0.13, 0.13), (0.0, 0.13)],
                   mat, 24, z=0.06))
    o.append(lathe(n + "_band", [(0, -0.05), (0.124, -0.05), (0.124, 0.02), (0, 0.02)], cuff_mat, 24, z=0.06))
    for i in range(4):
        o.append(cyl(n + "_lc%d" % i, (0.02 + i * 0.03, -0.1, 0.12), (0.02 + i * 0.03 + 0.025, -0.1, 0.0), 0.007, 0.007,
                     paint("#f5f2e6", chips=0.0), 6))
    return o


def satellite(n):
    body = panels("#c9d1dc", grid=(4, 4))
    o = [rbox(n + "_bd", (0, 0, 0), (0.26, 0.2, 0.22), body, 0.02),
         rbox(n + "_gd", (0, 0, 0.13), (0.2, 0.16, 0.04), TM("foil_gold", "foil", "#f2b632", metal=0.7, rough=0.25), 0.01)]
    sol = TM("solar", "panels", "#2a4ab8", grid=(8, 8), rivets=False, rough=0.2, metal=0.3, coat=0.6)
    for s in (-1, 1):
        o.append(cyl(n + "_arm%d" % s, (s * 0.13, 0, 0), (s * 0.24, 0, 0), 0.015, 0.015, steel(), 8))
        o.append(rbox(n + "_sp%d" % s, (s * 0.38, 0, 0), (0.28, 0.02, 0.22), sol, 0.006))
        o.append(rbox(n + "_sf%d" % s, (s * 0.38, 0.0, 0), (0.29, 0.012, 0.23), steel(), 0.004))
    # dish facing down-forward
    dish = lathe(n + "_dish", [(0, 0.0), (0.06, 0.004), (0.12, 0.02), (0.17, 0.055), (0.175, 0.065), (0.16, 0.06),
                               (0.11, 0.03), (0.0, 0.02)], steel("#d0d6de"), 28)
    feed = cyl(n + "_feed", (0.02, 0, 0), (0.17, 0, 0), 0.012, 0.008, steel(), 8)
    tipb = ball(n + "_tipb", (0.17, 0, 0), 0.02, paint("#d8302c"), 10, 6)
    grp = [dish, feed, tipb]
    xf(grp, Matrix.Translation((0, 0, -0.11)) @ Matrix.Rotation(math.radians(-90), 4, "Y"))
    o += grp
    o.append(cyl(n + "_ant", (0.06, 0, 0.11), (0.1, 0, 0.3), 0.008, 0.005, steel(), 6))
    o.append(ball(n + "_antb", (0.1, 0, 0.3), 0.018, paint("#d8302c"), 10, 6))
    return o


def shell(n, body, nose, fin_mat, L=0.62, r=0.13, band=None):
    """Mortar shell (teardrop) along +X from x=0."""
    pr = [(0, 0.12), (r * 0.45, 0.12), (r * 0.7, 0.2), (r, 0.32), (r * 1.02, 0.42), (r * 0.95, 0.5)]
    nose_x0 = 0.5
    o = [lathe(n + "_sb", pr + [(0, 0.5)], body, 28)]
    npts = [(0, 0.499)]
    for i in range(9):
        t = i / 8
        npts.append((r * 0.95 * (1 - t ** 1.6) ** 0.6, nose_x0 + t * (L - nose_x0)))
    npts.append((0, L))
    o.append(lathe(n + "_sn", npts, nose, 28))
    o.append(ring(n + "_sj", nose_x0, r * 0.95, 0.012, band or steel()))
    o.append(lathe(n + "_st", [(0, -0.12), (r * 0.3, -0.12), (r * 0.32, 0.14), (0, 0.14)], fin_mat, 20))
    for i in range(4):
        f = prof(n + "_sf%d" % i, [(-0.12, 0.0, 0), (0.08, 0.0, 0), (0.0, r * 0.95, 0.02), (-0.12, r * 0.95, 0.01)], 0.016,
                 fin_mat, bev=0.004, segs=1)
        rot_x([f], math.radians(45 + 90 * i))
        o.append(f)
    o.append(ring(n + "_sfr", -0.05, r * 0.9, 0.01, fin_mat))
    return o


# =========================================================================================== the weapons

def w_basic_nuke(n):
    return launcher(n, paint("#d8302c"), paint("#3c4350", chips=0.3), length=1.35, r=0.125, decal="rad")


def w_ap_rocket(n):
    return launcher(n, paint("#23a69a"), paint("#e8ecf0", chips=0.3), length=1.4, r=0.13, rear=paint("#4a4e56"),
                    muzzle=paint("#4a4e56"), decal="arrow")


def w_cluster_rocket(n):
    return launcher(n, paint("#8a5ad8"), paint("#4a4e56", chips=0.3), length=1.35, r=0.125, muzzle=paint("#9ad0f5"),
                    mscale=1.25, decal="warning")


def w_frag_missile(n):
    return launcher(n, paint("#f08a24"), paint("#8d99a8", chips=0.2), length=1.4, r=0.13, rear=paint("#5a6472"),
                    muzzle=paint("#5a6472"), decal="warning")


def w_mega_nuke(n):
    return launcher(n, paint("#f08a24"), paint("#5a6472", chips=0.3), length=1.5, r=0.15, mscale=1.3, decal="rad")


def w_heat_seeker(n):
    o, tip = launcher(n, paint("#e0402a"), paint("#ffb424", chips=0.3), length=1.3, r=0.12, decal="warning")
    z = tip.z
    o.append(rbox(n + "_scr", (0.1, -0.16, z + 0.12), (0.2, 0.03, 0.14), gunmetal(), 0.012))
    o.append(rbox(n + "_scg", (0.1, -0.177, z + 0.12), (0.16, 0.006, 0.1),
                  TM("screen_red", "screen", "#2a0a0a", "#ff5a3a", glow=1.5, bump=0.0), 0.002, 1))
    o.append(cyl(n + "_scm", (0.1, -0.06, z + 0.07), (0.1, -0.14, z + 0.11), 0.012, 0.012, gunmetal(), 8))
    return o, tip


def w_mini_bazooka(n):
    """Green launcher with a three-tube orange rotary head."""
    o, tip = launcher(n, paint("#4cab3f"), paint("#3c4350", chips=0.3), length=0.95, r=0.115, decal="bolt", haz=False)
    z = tip.z
    x1 = tip.x
    org = paint("#f0802a")
    o.append(lathe(n + "_hub", [(0, x1 - 0.05), (0.15, x1 - 0.05), (0.16, x1 - 0.02), (0.16, x1 + 0.03), (0, x1 + 0.03)],
                   org, 28, z=z))
    for i in range(3):
        a = 2 * math.pi * i / 3 + math.pi / 2
        yy, zz = 0.085 * math.cos(a), z + 0.085 * math.sin(a)
        o.append(lathe(n + "_bt%d" % i, [(0, x1), (0.055, x1), (0.055, x1 + 0.2), (0.065, x1 + 0.21), (0.065, x1 + 0.25),
                                         (0.04, x1 + 0.25), (0.04, x1 + 0.2), (0, x1 + 0.2)], org, 20, y=yy, z=zz))
        o.append(ring(n + "_btr%d" % i, x1 + 0.1, 0.057, 0.008, steel(), y=yy, z=zz, segs=16, rsegs=4))
    o.append(ball(n + "_hubc", (x1 + 0.04, 0, z), (0.03, 0.05, 0.05), steel(), 14, 8))
    return o, V((x1 + 0.25, 0, z))


def w_mortar(n):
    o, tip = launcher(n, paint("#4f7a34"), paint("#3c4350", chips=0.3), length=1.1, r=0.14, mscale=1.15, sight=False,
                      decal="warning")
    z = tip.z
    return o, tip


def w_plasma_mortar(n):
    o, tip = launcher(n, paint("#3a6ae0"), paint("#24489c", chips=0.3), length=1.15, r=0.14, mscale=1.2,
                      muzzle=panels("#5a8af0"), decal="arrow", haz=False)
    z = tip.z
    o.append(ball(n + "_lens", (tip.x - 0.13, 0, z), (0.03, 0.15, 0.15), glow("#6af0ff", 2.5), 20, 12))
    return o, tip


def w_drill(n):
    """Drill bot: armoured hazard-striped body, rivets, spiral drill cone, carry handle and grip."""
    z = 0.2
    body = paint("#5a6472", chips=0.4)
    o = [lathe(n + "_bd", [(0, -0.35), (0.1, -0.35), (0.14, -0.3), (0.15, -0.1), (0.17, 0.1), (0.17, 0.2), (0, 0.2)],
               body, 28, z=z),
         lathe(n + "_hz", [(0, 0.18), (0.18, 0.18), (0.185, 0.2), (0.185, 0.3), (0.18, 0.32), (0, 0.32)],
               hazard(period=10), 28, z=z),
         ring(n + "_r0", 0.18, 0.18, 0.015, steel(), z=z), ring(n + "_r1", 0.32, 0.18, 0.015, steel(), z=z)]
    o += rivets_ring(n + "_rv", 0.25, 0.185, steel(), 12, z=z, size=0.011)
    o += [ob for ob in drill_cone(n + "_d", 0.32, 0.45, 0.165, steel("#b8c0ca"), steel("#8d99a8"), 2.5)]
    for ob in o[-2:]:
        ob.data.transform(Matrix.Translation((0, 0, z)))
    o.append(sweep(n + "_hd", [(-0.2, 0, z + 0.14), (-0.18, 0, z + 0.24), (0.0, 0, z + 0.26), (0.08, 0, z + 0.17)], 0.018,
                   rubber(), 8, smooth_k=4))
    o.append(rbox(n + "_gm", (0.0, 0, z - 0.15), (0.16, 0.07, 0.05), gunmetal(), 0.01))
    o += pistol_grip(n + "_g", rubber(), x=-0.02, z=z - 0.16, h=0.22)
    o += trigger_guard(n + "_t", gunmetal(), x=0.02, z=z - 0.17)
    o += warning_sign(n, -0.12, 0.155, z=z, size=0.06, theta=0.2)
    return o, V((0.77, 0, z))


def w_pistol(n):
    return revolver(n, steel("#9aa3ae"), wood("#6a4126"), barrel=steel("#b6bec8"))


def w_orbital(n):
    """Target designator pistol with a dish on top."""
    o, tip = revolver(n, steel("#c9d1dc"), rubber(), barrel=steel("#e0e5ea"), length=0.46)
    z = 0.1
    dish = lathe(n + "_dish", [(0, 0.0), (0.04, 0.003), (0.08, 0.015), (0.105, 0.04), (0.1, 0.045), (0.07, 0.025),
                               (0.0, 0.016)], steel("#e0e5ea"), 24)
    feed = cyl(n + "_feed", (0.0, 0, 0), (0.09, 0, 0), 0.008, 0.006, steel(), 8)
    knob = ball(n + "_fk", (0.09, 0, 0), 0.014, paint("#d8302c"), 8, 6)
    grp = [dish, feed, knob]
    xf(grp, Matrix.Translation((0.0, 0, z + 0.17)) @ Matrix.Rotation(math.radians(-60), 4, "Y"))
    o += grp
    o.append(cyl(n + "_dm", (0.0, 0, z + 0.07), (0.0, 0, z + 0.17), 0.015, 0.015, gunmetal(), 10))
    o.append(ball(n + "_lz", (tip.x + 0.005, 0, tip.z), (0.01, 0.022, 0.022), glow("#ff3a3a", 3), 12, 8))
    return o, tip


def w_shotgun(n):
    return rifle(n, gunmetal(), wood("#8a5530"), length=1.2, bore=0.032, double=True, mag=False)


def w_sniper(n):
    return rifle(n, gunmetal("#3c4350"), gunmetal("#4a4e56"), length=1.45, bore=0.026, scope_on=True, mag=True)


def w_grenade_launcher(n):
    o, tip = rifle(n, gunmetal(), wood("#9a6234"), length=1.15, bore=0.045, mag=False, pump=True)
    z = 0.1
    o.append(cyl(n + "_dr", (0.12, 0, z - 0.03), (0.3, 0, z - 0.03), 0.11, 0.11, gunmetal("#5a6472"), 8))
    for i in range(6):
        a = 2 * math.pi * i / 6
        o.append(cyl(n + "_dh%d" % i, (0.301, 0.07 * math.cos(a), z - 0.03 + 0.07 * math.sin(a)),
                     (0.305, 0.07 * math.cos(a), z - 0.03 + 0.07 * math.sin(a)), 0.026, 0.026, dark(), 10))
        o.append(rbox(n + "_df%d" % i, (0.21, 0.105 * math.cos(a + 0.52), z - 0.03 + 0.105 * math.sin(a + 0.52)),
                      (0.13, 0.012, 0.012), dark(), 0.003, 1))
    o.append(tube_decal(n + "_bs", [rect(0.6, 0.03)], 0.0, 0.0425, paint("#3a7be8"), z=z, theta=0.0))
    return o, tip


def w_minigun(n):
    """'Jatimatic' SMG: chunky blue receiver, shrouded barrel, angled magazine, rubber grip."""
    z = 0.12
    blue = paint("#4a6ad0")
    o = [prof(n + "_rc", [(-0.2, z + 0.08, 0.03), (0.3, z + 0.08, 0.02), (0.34, z + 0.0, 0.01), (0.32, z - 0.07, 0.02),
                          (-0.05, z - 0.07, 0.01), (-0.2, z - 0.03, 0.03)], 0.1, blue, bev=0.016),
         prof(n + "_top", [(-0.15, z + 0.08, 0), (0.25, z + 0.08, 0), (0.22, z + 0.12, 0.02), (-0.1, z + 0.12, 0.02)],
              0.07, paint("#24489c"), bev=0.01),
         lathe(n + "_sh", [(0, 0.3), (0.06, 0.3), (0.06, 0.6), (0.05, 0.62), (0, 0.62)], gunmetal(), 20, z=z + 0.02),
         lathe(n + "_br", [(0, 0.6), (0.03, 0.6), (0.03, 0.7), (0.04, 0.71), (0.04, 0.74), (0.018, 0.74), (0.018, 0.72),
                           (0, 0.72)], gunmetal("#2a2f38"), 16, z=z + 0.02)]
    for i in range(5):
        o.append(rbox(n + "_hole%d" % i, (0.34 + i * 0.05, -0.058, z + 0.02), (0.025, 0.01, 0.03), dark(), 0.006, 1))
    o.append(prof(n + "_mg", [(0.12, z - 0.06, 0), (0.22, z - 0.06, 0), (0.26, z - 0.3, 0.02), (0.17, z - 0.32, 0.02)],
                  0.06, gunmetal("#2a2f38"), bev=0.01))
    o += pistol_grip(n + "_g", rubber(), x=-0.08, z=z - 0.06, h=0.22, slant=0.35)
    o += trigger_guard(n + "_t", gunmetal(), x=-0.04, z=z - 0.07, w=0.13)
    o.append(prof(n + "_stk", [(-0.2, z + 0.05, 0), (-0.42, z + 0.03, 0.02), (-0.44, z - 0.12, 0.02), (-0.38, z - 0.12, 0),
                               (-0.36, z - 0.0, 0.02), (-0.2, z - 0.02, 0)], 0.05, gunmetal(), bev=0.01))
    o.append(rbox(n + "_ej", (0.1, -0.052, z + 0.03), (0.12, 0.006, 0.04), dark(), 0.004, 1))
    o.append(cyl(n + "_ch", (0.0, -0.05, z + 0.06), (0.0, -0.1, z + 0.06), 0.012, 0.014, steel(), 8))
    for i, xx in enumerate((-0.12, 0.05, 0.24)):
        o.append(rivet(n + "_sc%d" % i, (xx, -0.053, z - 0.04), 0.011, steel()))
    o.append(flat_decal(n + "_st", star(0.035), (0.0, -0.051, z + 0.0), paint("#ffd23a", chips=0.2)))
    return o, V((0.74, 0, z + 0.02))


def w_flamethrower(n):
    z = 0.13
    red = paint("#d0322a")
    o = [prof(n + "_bd", [(-0.25, z + 0.07, 0.03), (0.25, z + 0.07, 0.03), (0.3, z - 0.0, 0.02), (0.25, z - 0.07, 0.02),
                          (-0.25, z - 0.07, 0.03)], 0.11, red, bev=0.016),
         lathe(n + "_br", [(0, 0.25), (0.04, 0.25), (0.035, 0.62), (0.045, 0.64), (0, 0.64)], steel("#8d99a8"), 20, z=z),
         lathe(n + "_nz", [(0, 0.62), (0.05, 0.62), (0.07, 0.74), (0.075, 0.78), (0.05, 0.78), (0.035, 0.72), (0, 0.72)],
               gunmetal(), 20, z=z),
         lathe(n + "_hs", [(0, 0.3), (0.055, 0.3), (0.055, 0.56), (0, 0.56)], TM("heatshield", "panels", "#5a6472",
                                                                                 grid=(10, 1), rivets=False, metal=0.6), 20, z=z)]
    for i in range(6):
        o.append(ring(n + "_fin%d" % i, 0.33 + i * 0.04, 0.058, 0.009, gunmetal(), z=z, segs=20, rsegs=4))
    # tank under the gun
    o.append(lathe(n + "_tk", [(0, -0.3), (0.06, -0.3), (0.08, -0.28), (0.08, 0.12), (0.06, 0.14), (0, 0.14)],
                   paint("#e0402a"), 24, y=0.0, z=z + 0.15))
    o.append(ring(n + "_tkb0", -0.2, 0.082, 0.01, steel(), z=z + 0.15))
    o.append(ring(n + "_tkb1", 0.05, 0.082, 0.01, steel(), z=z + 0.15))
    o += warning_sign(n + "_tw", -0.08, 0.08, z=z + 0.15, size=0.045)
    o.append(cyl(n + "_vl", (0.17, 0, z + 0.15), (0.24, 0, z + 0.06), 0.015, 0.015, steel(), 8))
    o.append(ball(n + "_pl", (0.79, 0, z - 0.05), (0.03, 0.02, 0.04), glow("#4f9dff", 3), 10, 6))
    o.append(ball(n + "_plf", (0.8, 0, z - 0.01), (0.025, 0.018, 0.035), glow("#ff8a2a", 3), 10, 6))
    o.append(cyl(n + "_pt", (0.6, 0, z - 0.045), (0.78, 0, z - 0.05), 0.01, 0.01, steel(), 6))
    o += pistol_grip(n + "_g", rubber(), x=-0.05, z=z - 0.07, h=0.22)
    o += trigger_guard(n + "_t", gunmetal(), x=-0.01, z=z - 0.08)
    o += pistol_grip(n + "_fg", rubber(), x=0.18, z=z - 0.07, h=0.15, w=0.07, depth=0.06, slant=-0.05, grooves=False)
    return o, V((0.78, 0, z))


def w_flare_gun(n):
    z = 0.12
    red = paint("#e8452a")
    o = [prof(n + "_fr", [(-0.12, z + 0.06, 0.02), (0.1, z + 0.06, 0.01), (0.1, z - 0.06, 0.01), (-0.1, z - 0.07, 0.02)],
              0.09, red, bev=0.014),
         lathe(n + "_br", [(0, 0.05), (0.06, 0.05), (0.06, 0.25), (0.075, 0.3), (0.11, 0.38), (0.12, 0.4), (0.09, 0.4),
                           (0.06, 0.33), (0, 0.33)], red, 28, z=z + 0.01),
         tube(n + "_bdk", 0.32, 0.335, 0.059, dark(), z=z + 0.01),
         ring(n + "_hb", 0.07, 0.063, 0.01, gunmetal(), z=z + 0.01),
         ring(n + "_hb2", 0.24, 0.063, 0.01, gunmetal(), z=z + 0.01)]
    o.append(prof(n + "_hm", [(-0.1, z + 0.04, 0), (-0.06, z + 0.06, 0), (-0.11, z + 0.12, 0.012), (-0.15, z + 0.11, 0.01)],
                  0.03, gunmetal(), bev=0.005, segs=1))
    o += pistol_grip(n + "_g", rubber("#3a3d44"), x=-0.05, z=z - 0.05, h=0.22, w=0.1, slant=0.4)
    o += trigger_guard(n + "_t", gunmetal(), x=-0.02, z=z - 0.06, w=0.12)
    o.append(flat_decal(n + "_wt", triangle(0.03), (-0.0, -0.046, z + 0.0), paint("#ffd23a", chips=0.1)))
    o.append(rivet(n + "_pv", (0.06, -0.046, z - 0.03), 0.011, steel()))
    return o, V((0.4, 0, z + 0.01))


def w_laser_pistol(n):
    z = 0.12
    yel = paint("#ffcc2a")
    o = [prof(n + "_bd", [(-0.12, z + 0.07, 0.03), (0.2, z + 0.07, 0.02), (0.24, z + 0.02, 0.02), (0.2, z - 0.06, 0.01),
                          (-0.1, z - 0.06, 0.02)], 0.1, yel, bev=0.016),
         prof(n + "_top", [(-0.08, z + 0.07, 0), (0.18, z + 0.07, 0), (0.15, z + 0.1, 0.02), (-0.06, z + 0.1, 0.02)], 0.06,
              gunmetal("#3a2a5a"), bev=0.008)]
    # emitter block with a grid of glowing dots
    o.append(rbox(n + "_em", (0.33, 0, z + 0.03), (0.2, 0.12, 0.12), paint("#ff9b21"), 0.025, 2))
    for i in range(3):
        for j in range(3):
            o.append(ball(n + "_dot%d%d" % (i, j), (0.26 + i * 0.05, -0.062, z + 0.0 + j * 0.035), 0.012,
                          glow("#ffe060", 2.5), 8, 6))
            o.append(ball(n + "_dtf%d%d" % (i, j), (0.432, -0.035 + i * 0.035, z + 0.0 + j * 0.035), 0.012,
                          glow("#ffe060", 2.5), 8, 6))
    o.append(ring(n + "_rg", 0.23, 0.065, 0.012, gunmetal("#3a2a5a"), z=z + 0.03))
    o += pistol_grip(n + "_g", rubber("#3a2a5a", "ribs"), x=-0.04, z=z - 0.05, h=0.22, slant=0.35)
    o += trigger_guard(n + "_t", gunmetal("#3a2a5a"), x=-0.0, z=z - 0.06, w=0.12)
    for i in range(3):
        o.append(rbox(n + "_v%d" % i, (0.0 + i * 0.04, -0.051, z + 0.03), (0.016, 0.006, 0.05), dark(), 0.003, 1))
    return o, V((0.44, 0, z + 0.03))


def w_mining_laser(n):
    o, tip = blaster(n, hazard("#ffd23a", "#262b34", 6), glow("#ff9b21", 2.5), gunmetal(), length=1.0, r=0.1,
                     emitter="ring", coils=2, grip_mat=rubber())
    z = tip.z
    for i, dz in enumerate((-0.05, 0.05)):
        o.append(lathe(n + "_side%d" % i, [(0, 0.2), (0.035, 0.2), (0.035, 0.5), (0.04, 0.52), (0, 0.52)],
                       paint("#ff9b21"), 16, y=-0.12, z=z + dz))
    return o, tip


def w_plasma_cannon(n):
    return blaster(n, panels("#c8d8dc"), glow("#4fd8ff", 3.0), steel("#7a8a96"), length=1.05, r=0.105, emitter="cone",
                   coils=3)


def w_impact_cannon(n):
    o, tip = blaster(n, panels("#3a9a4a"), glow("#ffd23a", 2.0), gunmetal(), length=0.85, r=0.12, emitter="wide",
                     coils=2, fins=False)
    z = tip.z
    x1 = tip.x
    for i in range(6):
        a = 2 * math.pi * i / 6
        o.append(cyl(n + "_h%d" % i, (x1 + 0.001, 0.11 * math.cos(a), z + 0.11 * math.sin(a)),
                     (x1 + 0.005, 0.11 * math.cos(a), z + 0.11 * math.sin(a)), 0.028, 0.028, dark(), 12))
    o.append(prof(n + "_tp", [(-0.2, z + 0.14, 0), (0.25, z + 0.14, 0), (0.2, z + 0.2, 0.03), (-0.15, z + 0.2, 0.03)], 0.06,
                  paint("#ff9b21"), bev=0.01))
    return o, tip


def w_railgun(n):
    z = 0.14
    body = panels("#78b4e8")
    o = [prof(n + "_bd", [(-0.32, z + 0.07, 0.03), (0.3, z + 0.07, 0.02), (0.34, z - 0.0, 0.01), (0.3, z - 0.07, 0.02),
                          (-0.28, z - 0.07, 0.03), (-0.36, z, 0.03)], 0.13, body, bev=0.02)]
    org = paint("#ff9b21")
    for k, dz in enumerate((0.05, -0.05)):
        o.append(rbox(n + "_rail%d" % k, (0.55, 0, z + dz), (0.7, 0.06, 0.035), org, 0.01))
        for i in range(5):
            o.append(rbox(n + "_rv%d%d" % (k, i), (0.3 + i * 0.12, -0.031, z + dz), (0.03, 0.004, 0.02), steel(), 0.003, 1))
    o.append(tube(n + "_core", 0.3, 0.88, 0.025, glow("#6af0ff", 3), z=z))
    for i in range(4):
        o.append(ring(n + "_cl%d" % i, 0.38 + i * 0.13, 0.075, 0.012, gunmetal(), z=z))
    o.append(lathe(n + "_cap", [(0, -0.42), (0.05, -0.42), (0.07, -0.36), (0, -0.36)], gunmetal(), 20, z=z))
    o += pistol_grip(n + "_g", rubber(), x=-0.05, z=z - 0.07, h=0.22)
    o += trigger_guard(n + "_t", gunmetal(), x=-0.01, z=z - 0.08)
    o.append(lathe(n + "_scp", [(0, -0.15), (0.03, -0.15), (0.03, 0.1), (0.04, 0.12), (0, 0.12)], gunmetal(), 16,
                   z=z + 0.11))
    o.append(ball(n + "_scl", (0.12, 0, z + 0.11), (0.006, 0.035, 0.035), glass("#ff5a3a"), 12, 8))
    o.append(rbox(n + "_scm", (-0.02, 0, z + 0.085), (0.06, 0.03, 0.04), gunmetal(), 0.006))
    o.append(flat_decal(n + "_bolt", bolt_shape(0.04), (-0.15, -0.066, z), paint("#ffd23a", chips=0.1)))
    return o, V((0.9, 0, z))


def w_point_teleport(n):
    o, tip = blaster(n, paint("#ff8a24"), glow("#b07ae8", 2.0), gunmetal("#4a3a6a"), length=0.75, r=0.09, emitter="ring",
                     coils=2, fins=False)
    z = tip.z
    o.append(ball(n + "_orb", (0.0, 0, z + 0.17), 0.085, glass("#4fe0e0"), 24, 16))
    o.append(ball(n + "_orbc", (0.0, 0, z + 0.17), 0.045, glow("#7ae8ff", 2.5), 16, 10))
    o.append(cyl(n + "_orbm", (0.0, 0, z + 0.08), (0.0, 0, z + 0.11), 0.06, 0.05, gunmetal("#4a3a6a"), 16))
    return o, tip


def w_fire_hose(n):
    """Super-soaker: pink and white toy body, orange nozzle, pump, water tank and hose loop."""
    z = 0.12
    pink = plastic("#e83aa8")
    white = plastic("#f5f7fb")
    o = [prof(n + "_bd", [(-0.3, z + 0.07, 0.04), (0.3, z + 0.07, 0.03), (0.34, z + 0.0, 0.02), (0.3, z - 0.06, 0.02),
                          (-0.28, z - 0.06, 0.04)], 0.12, pink, bev=0.025),
         prof(n + "_wh", [(-0.15, z + 0.07, 0), (0.2, z + 0.07, 0), (0.18, z + 0.15, 0.03), (-0.1, z + 0.15, 0.03)], 0.08,
              white, bev=0.02),
         lathe(n + "_nz", [(0, 0.3), (0.06, 0.3), (0.07, 0.34), (0.07, 0.42), (0.05, 0.47), (0.04, 0.5), (0.02, 0.5),
                           (0, 0.48)], plastic("#ff9b21"), 24, z=z),
         lathe(n + "_tk", [(0, -0.25), (0.07, -0.25), (0.09, -0.22), (0.09, 0.05), (0.07, 0.08), (0, 0.08)],
               glass("#5fb8ff"), 24, z=z + 0.2),
         cyl(n + "_tkc", (-0.08, 0, z + 0.2), (-0.08, 0, z + 0.31), 0.03, 0.03, plastic("#ff9b21"), 12),
         prof(n + "_pump", [(0.06, z - 0.06, 0), (0.28, z - 0.06, 0), (0.28, z - 0.12, 0.02), (0.06, z - 0.12, 0.02)],
              0.1, white, bev=0.02)]
    for i in range(4):
        o.append(rbox(n + "_pr%d" % i, (0.1 + i * 0.05, -0.051, z - 0.09), (0.018, 0.004, 0.05), plastic("#e83aa8"), 0.002, 1))
    o.append(sweep(n + "_hose", [(-0.3, 0, z + 0.0), (-0.36, -0.02, z - 0.06), (-0.33, -0.04, z - 0.16),
                                 (-0.2, -0.03, z - 0.18), (-0.12, 0, z - 0.1)], 0.018, rubber("#30333a", "ribs"), 10,
                   smooth_k=4))
    o += pistol_grip(n + "_g", white, x=-0.12, z=z - 0.05, h=0.22, slant=0.35)
    o += trigger_guard(n + "_t", plastic("#ff9b21"), x=-0.08, z=z - 0.06, w=0.13)
    o.append(flat_decal(n + "_dr", [circle(0.03, 16)], (0.0, -0.061, z + 0.01), plastic("#4fd8e8")))
    return o, V((0.5, 0, z))


def w_cannon(n):
    """Blunderbuss: light wood stock, dark iron flared barrel with brass bands and rope wrap."""
    z = 0.12
    iron = gunmetal("#3a3e46")
    brass = steel("#d8a845")
    wd = wood("#c88a42", 8)
    o = [lathe(n + "_br", [(0, -0.05), (0.05, -0.05), (0.045, 0.1), (0.045, 0.45), (0.06, 0.55), (0.1, 0.63),
                           (0.115, 0.66), (0.09, 0.66), (0.07, 0.6), (0, 0.6)], iron, 28, z=z + 0.04),
         tube(n + "_bdk", 0.598, 0.61, 0.07, dark(), z=z + 0.04),
         ring(n + "_b0", 0.05, 0.05, 0.012, brass, z=z + 0.04), ring(n + "_b1", 0.44, 0.05, 0.012, brass, z=z + 0.04),
         ring(n + "_b2", 0.65, 0.112, 0.012, brass, z=z + 0.04),
         prof(n + "_stk", [(0.35, z + 0.02, 0.02), (0.35, z - 0.02, 0.02), (-0.05, z - 0.03, 0.02), (-0.2, z - 0.14, 0.05),
                           (-0.48, z - 0.24, 0.02), (-0.5, z - 0.08, 0.03), (-0.2, z + 0.03, 0.04)], 0.08, wd, bev=0.016)]
    for i in range(6):
        o.append(ring(n + "_rope%d" % i, 0.2 + i * 0.022, 0.05, 0.011, TM("rope", "fabric", "#d8c08a", weave=140,
                                                                          rough=0.9, bump=0.5), z=z + 0.04, segs=20, rsegs=6))
    o.append(prof(n + "_lock", [(-0.06, z + 0.03, 0.01), (0.08, z + 0.03, 0.01), (0.08, z - 0.03, 0.01),
                                (-0.06, z - 0.03, 0.01)], 0.095, brass, bev=0.008))
    o.append(prof(n + "_hm", [(-0.04, z + 0.03, 0), (-0.0, z + 0.04, 0), (-0.05, z + 0.11, 0.012), (-0.09, z + 0.1, 0.01)],
                  0.03, iron, bev=0.005, segs=1))
    o += trigger_guard(n + "_t", brass, x=-0.02, z=z - 0.035, w=0.12)
    o.append(rbox(n + "_bp", (-0.49, 0, z - 0.16), (0.03, 0.085, 0.17), brass, 0.008, rot=Matrix.Rotation(-0.1, 4, "Y")))
    return o, V((0.66, 0, z + 0.04))


def w_choco_cannon(n):
    """Easter chocolate cannon: gold-foil wrapped barrel, chocolate dripping from the muzzle, candy stock."""
    z = 0.12
    foil = TM("foil_gold", "foil", "#f2b632", metal=0.7, rough=0.25)
    choco = TM("chocolate", "chocolate", rough=0.3, coat=0.5, bump=0.2)
    o = [lathe(n + "_br", [(0, -0.12), (0.07, -0.12), (0.09, -0.08), (0.09, 0.2), (0.11, 0.42), (0.13, 0.5), (0.14, 0.55),
                           (0.1, 0.55), (0, 0.5)], foil, 28, z=z + 0.05),
         C.torus(n + "_lip", (0.56, 0, z + 0.05), 0.12, 0.03, choco, 28, 8, axis=X),
         tube(n + "_dk", 0.51, 0.53, 0.11, choco, z=z + 0.05),
         ball(n + "_knob", (-0.14, 0, z + 0.05), 0.06, foil, 16, 10)]
    rnd = random.Random(4)
    for i in range(7):
        a = math.radians(-100 + i * 32 + rnd.uniform(-8, 8))
        yy, zz = -0.13 * math.cos(a), z + 0.05 + 0.13 * math.sin(a)
        ln = rnd.uniform(0.04, 0.1) * (1.2 if abs(math.sin(a)) < 0.6 else 0.6)
        o.append(sweep(n + "_dr%d" % i, [(0.55, yy, zz), (0.52, yy * 1.05, zz - ln * 0.5), (0.505, yy * 1.05, zz - ln)],
                       0.018, choco, 8, r_end=0.012))
        o.append(ball(n + "_drb%d" % i, (0.505, yy * 1.05, zz - ln), 0.017, choco, 10, 6))
    o.append(prof(n + "_stk", [(0.0, z + 0.03, 0.02), (-0.05, z - 0.02, 0.02), (-0.22, z - 0.12, 0.05), (-0.48, z - 0.2, 0.02),
                               (-0.5, z - 0.05, 0.03), (-0.2, z + 0.04, 0.04)], 0.08,
                  TM("candy_zig", "bands", ["#4cbb3f", "#2c7a2a", "#ffd23a"], period=5, zig=1.2, chips=0.0, rough=0.3,
                     coat=0.5), bev=0.016))
    for i in range(3):
        o.append(ring(n + "_rb%d" % i, 0.08 + i * 0.035, 0.093, 0.011, plastic("#8a4fd8"), z=z + 0.05))
    o += trigger_guard(n + "_t", foil, x=-0.06, z=z - 0.02, w=0.12)
    return o, V((0.56, 0, z + 0.05))


def w_beanbag(n):
    """Bean bag gun: round hopper drum, stubby flared barrel with a burlap bag peeking out, grip."""
    z = 0.15
    body = gunmetal("#5a6068")
    o = [ball(n + "_drum", (-0.12, 0, z + 0.02), 0.16, body, 28, 18),
         ring(n + "_dr", -0.12, 0.16, 0.014, steel(), z=z + 0.02),
         lathe(n + "_br", [(0, -0.02), (0.08, -0.02), (0.075, 0.25), (0.1, 0.33), (0.115, 0.38), (0.09, 0.38),
                           (0.07, 0.32), (0, 0.3)], body, 28, z=z),
         ring(n + "_b0", 0.06, 0.08, 0.012, steel(), z=z), ring(n + "_b1", 0.2, 0.08, 0.012, steel(), z=z),
         blob_ball(n + "_bag", (0.355, 0, z), 0.075, TM("burlap", "fabric", "#c8a070", rough=0.95, bump=0.6), seed=5,
                   lumps=0.12)]
    o += rivets_ring(n + "_rv", -0.12, 0.155, steel(), 12, z=z + 0.02, size=0.012)
    o.append(cyl(n + "_hp", (-0.15, 0, z + 0.17), (-0.15, 0, z + 0.25), 0.06, 0.07, body, 16))
    o.append(cyl(n + "_hpc", (-0.15, 0, z + 0.25), (-0.15, 0, z + 0.27), 0.075, 0.075, steel(), 16))
    o += pistol_grip(n + "_g", wood("#6a4126"), x=0.02, z=z - 0.08, h=0.2, slant=0.3)
    o += trigger_guard(n + "_t", body, x=0.06, z=z - 0.08, w=0.12)
    o.append(rbox(n + "_gm", (0.07, 0, z - 0.07), (0.16, 0.07, 0.05), body, 0.01))
    return o, V((0.4, 0, z))


def w_grey_goo(n):
    o, tip = blaster(n, panels("#8d99a8"), glow("#9ad8b0", 1.6), gunmetal(), length=0.9, r=0.09, emitter="cone",
                     coils=2, fins=False)
    z = tip.z
    o.append(lathe(n + "_tk", [(0, -0.22), (0.075, -0.22), (0.085, -0.2), (0.085, 0.12), (0.075, 0.14), (0, 0.14)],
                   glass("#c8e0ec"), 24, z=z + 0.2))
    for i, xx in enumerate((-0.12, -0.02, 0.07)):
        o.append(blob_ball(n + "_g%d" % i, (xx, -0.03, z + 0.185), 0.07, TM("goo", "slime", "#5a606c", rough=0.15, coat=0.6),
                           seed=i, lumps=0.15))
    for xx in (-0.215, 0.135):
        o.append(ring(n + "_tr%d" % (xx > 0), xx, 0.087, 0.014, gunmetal(), z=z + 0.2))
    o.append(rbox(n + "_tm", (-0.04, 0, z + 0.11), (0.2, 0.04, 0.05), gunmetal(), 0.01))
    return o, tip


def w_shield(n):
    z = 0.12
    o = [lathe(n + "_bd", [(0, -0.12), (0.06, -0.12), (0.08, -0.08), (0.09, 0.1), (0.13, 0.16), (0.14, 0.2), (0, 0.2)],
               panels("#c9d1dc"), 28, z=z),
         ball(n + "_dome", (0.2, 0, z), (0.09, 0.13, 0.13), glass("#4fd8e8"), 28, 16),
         ball(n + "_core", (0.2, 0, z), (0.05, 0.07, 0.07), glow("#6af0ff", 3), 16, 10),
         ring(n + "_r", 0.2, 0.14, 0.018, gunmetal(), z=z)]
    for i in range(3):
        a = 2 * math.pi * i / 3 + math.pi / 2
        p0 = V((0.17, -0.14 * math.cos(a), z + 0.14 * math.sin(a)))
        o.append(cyl(n + "_pr%d" % i, p0, p0 + V((0.12, -0.06 * math.cos(a), 0.06 * math.sin(a))), 0.012, 0.008, steel(), 8))
        o.append(ball(n + "_pb%d" % i, p0 + V((0.12, -0.06 * math.cos(a), 0.06 * math.sin(a))), 0.016, glow("#6af0ff", 3), 8, 6))
    o += pistol_grip(n + "_g", rubber(), x=-0.02, z=z - 0.07, h=0.2)
    return o, V((0.3, 0, z))


def w_void(n):
    """Void generator: steel blade body with a blue band and a black claw holding a dark void orb."""
    z = 0.12
    o = [prof(n + "_bl", [(-0.1, z + 0.05, 0.02), (0.35, z + 0.06, 0.04), (0.55, z + 0.0, 0.0), (0.35, z - 0.06, 0.04),
                          (-0.1, z - 0.05, 0.02)], 0.06, steel("#b8c0ca"), bev=0.02),
         lathe(n + "_band", [(0, -0.12), (0.07, -0.12), (0.075, -0.1), (0.075, 0.0), (0.07, 0.02), (0, 0.02)],
               paint("#3a7be8"), 24, z=z),
         lathe(n + "_hd", [(0, -0.3), (0.06, -0.3), (0.08, -0.26), (0.08, -0.12), (0, -0.12)], gunmetal("#22252c"), 24, z=z)]
    for i in range(3):
        a = 2 * math.pi * i / 3 + math.pi / 2
        pts = [(-0.28, -0.07 * math.cos(a), z + 0.07 * math.sin(a)), (-0.36, -0.13 * math.cos(a), z + 0.13 * math.sin(a)),
               (-0.45, -0.08 * math.cos(a), z + 0.08 * math.sin(a))]
        o.append(sweep(n + "_cl%d" % i, pts, 0.018, gunmetal("#22252c"), 8, r_end=0.006, smooth_k=4))
    o.append(ball(n + "_void", (-0.38, 0, z), 0.07, glow("#5a2c9c", 1.5), 20, 12))
    o.append(tube_decal(n + "_bb", bolt_shape(0.04), -0.05, 0.075, paint("#ffd23a", chips=0.1), z=z))
    o += pistol_grip(n + "_g", rubber(), x=0.02, z=z - 0.05, h=0.2)
    return o, V((0.55, 0, z))


def w_wand(n):
    """Wind wand: twisted blue staff with a curly ribbon swirl at the tip and a gem."""
    z = 0.06
    stick = TM("wand_wood", "wood", "#2a5aa8", 3, rough=0.35, coat=0.4)
    pts = [(-0.2, 0, z - 0.04), (0.1, 0, z), (0.4, 0, z + 0.05), (0.58, 0, z + 0.08)]
    o = [sweep(n + "_st", pts, 0.025, stick, 10, r_end=0.018, smooth_k=6),
         ball(n + "_end", (-0.2, 0, z - 0.04), 0.03, steel("#d8a845"), 12, 8)]
    for i in range(3):
        o.append(ring(n + "_wr%d" % i, -0.1 + i * 0.05, 0.027, 0.008, steel("#d8a845"), z=z - 0.025 + i * 0.006, segs=16,
                      rsegs=4))
    # curly ribbon swirls
    rib = plastic("#4f9de8")
    for k, (ph, rr) in enumerate(((0.0, 0.11), (2.1, 0.09), (4.2, 0.075))):
        sp = []
        for i in range(40):
            t = i / 39
            a = ph + t * 2.2 * math.pi
            r_ = rr * (1 - 0.6 * t)
            sp.append((0.6 + r_ * math.cos(a) * 0.8 + t * 0.12, -r_ * math.sin(a) * 0.5, z + 0.09 + r_ * math.sin(a) + t * 0.05))
        o.append(sweep(n + "_cu%d" % k, sp, 0.012, rib, 8, r_end=0.006))
    o.append(ball(n + "_gem", (0.6, 0, z + 0.09), 0.035, glass("#7ae8ff"), 16, 10))
    return o, V((0.66, 0, z + 0.1))


def w_broom(n):
    z = 0.05
    handle = wood("#7a4e2a", 9)
    o = [sweep(n + "_h", [(-0.35, 0, z - 0.03), (0.0, 0, z), (0.3, 0, z + 0.01), (0.55, 0, z + 0.04)], 0.03, handle, 12,
               smooth_k=4)]
    bristle = TM("bristle", "fur", "#7a3ad8", rough=0.8, bump=0.6)
    o.append(lathe(n + "_br", [(0, 0.5), (0.05, 0.5), (0.09, 0.56), (0.16, 0.7), (0.21, 0.88), (0.23, 0.98), (0.17, 1.02),
                               (0.0, 1.0)], bristle, 28, z=z + 0.04))
    rnd = random.Random(2)
    for i in range(22):
        a = 2 * math.pi * i / 22
        rr = 0.19 + rnd.uniform(-0.03, 0.02)
        p0 = V((0.9, -rr * math.cos(a), z + 0.04 + rr * math.sin(a)))
        o.append(cyl(n + "_bs%d" % i, p0, p0 + V((0.12 + rnd.uniform(0, 0.07), -0.06 * math.cos(a), 0.06 * math.sin(a))),
                     0.022, 0.004, bristle, 6))
    twine = TM("rope", "fabric", "#d8c08a", weave=140, rough=0.9, bump=0.5)
    for i in range(3):
        o.append(ring(n + "_tw%d" % i, 0.55 + i * 0.03, 0.045 + i * 0.008, 0.01, twine, z=z + 0.04, segs=20, rsegs=6))
    o.append(ring(n + "_tw3", 0.7, 0.165, 0.013, twine, z=z + 0.04, segs=28, rsegs=6))
    return o, V((1.05, 0, z + 0.06))


def w_scythe(n):
    handle = wood("#6a4126", 11)
    o = [sweep(n + "_h", [(-0.42, 0, -0.14), (0.0, 0, -0.02), (0.4, 0, 0.13), (0.74, 0, 0.29)], 0.03, handle, 12, smooth_k=4)]
    grip = rubber("#3a2a20", "ribs")
    o.append(sweep(n + "_gw", [(-0.4, 0, -0.135), (-0.22, 0, -0.075)], 0.034, grip, 12))
    o.append(cyl(n + "_g1", (0.12, 0, 0.02), (0.13, -0.14, 0.07), 0.02, 0.018, handle, 10))
    o.append(ball(n + "_g1b", (0.13, -0.14, 0.07), 0.024, handle, 10, 6))

    def bez(a, c, b, k):
        return [(a[0] * (1 - t) ** 2 + c[0] * 2 * t * (1 - t) + b[0] * t * t,
                 a[1] * (1 - t) ** 2 + c[1] * 2 * t * (1 - t) + b[1] * t * t) for t in [i / k for i in range(k + 1)]]
    outer = bez((0.68, 0.37), (1.3, 0.62), (1.38, -0.08), 16)
    inner = bez((1.38, -0.08), (1.1, 0.27), (0.74, 0.23), 16)[1:-1]
    pts = [(x, z, 0.0) for (x, z) in outer + inner]
    o.append(prof(n + "_bl", pts, 0.02, steel("#c9d1dc", rust=0.4), bev=0.007, segs=1, smooth=0))
    edge = bez((0.75, 0.245), (1.1, 0.29), (1.375, -0.07), 16)
    o.append(sweep(n + "_edge", [(x, 0, z) for (x, z) in edge], 0.007, steel("#e8eef4"), 6))
    o.append(rbox(n + "_c", (0.73, 0, 0.3), (0.1, 0.06, 0.1), gunmetal(), 0.012))
    o.append(rivet(n + "_cr", (0.73, -0.031, 0.3), 0.014, steel()))
    return o, V((1.1, 0, 0.2))


def w_punch(n):
    o = glove_mesh(n, paint("#d8302c", chips=0.0, coat=0.7), paint("#f5f2e6", chips=0.0))
    return o, V((0.5, 0, 0.08))


def w_fireworks(n):
    z = 0.18
    tube_m = TM("fw_stars", "stars", "#7a3ad8", ["#ffd23a", "#4cbb3f", "#e83a3a", "#4fd8e8"], rough=0.4, coat=0.3)
    o = [lathe(n + "_bd", [(0, -0.1), (0.09, -0.1), (0.1, -0.08), (0.1, 0.38), (0, 0.38)], tube_m, 24, z=z),
         lathe(n + "_bands", [(0, 0.1), (0.102, 0.1), (0.102, 0.16), (0, 0.16)],
               TM("candy_ry", "stripes", "#ffd23a", "#e83a3a", period=10, chips=0.1, rough=0.35), 24, z=z),
         lathe(n + "_cone", [(0, 0.37), (0.12, 0.37), (0.12, 0.39), (0.06, 0.5), (0.0, 0.58)], paint("#e83a3a", chips=0.2), 24, z=z),
         cyl(n + "_stick", (-0.1, 0, z - 0.07), (-0.5, 0, z - 0.07), 0.016, 0.014, wood("#c8913a", 12), 8),
         cyl(n + "_fuse", (-0.1, 0, z), (-0.18, 0, z - 0.03), 0.008, 0.008, TM("rope", "fabric", "#d8c08a", weave=140,
                                                                                 rough=0.9, bump=0.5), 6)]
    for i in range(3):
        o.append(ring(n + "_tie%d" % i, -0.05 + i * 0.1, 0.102, 0.009, TM("rope", "fabric", "#d8c08a", weave=140, rough=0.9,
                                                                         bump=0.5), z=z, segs=20, rsegs=4))
    o.append(rbox(n + "_gm", (-0.0, 0, z - 0.1), (0.1, 0.05, 0.05), gunmetal(), 0.01))
    o += pistol_grip(n + "_g", rubber(), x=0.0, z=z - 0.12, h=0.18, w=0.08)
    return o, V((0.58, 0, z))


def w_artillery(n):
    """Signal flare: glowing glass bulb in a cage on a ribbed green grip."""
    z = 0.0
    o = [lathe(n + "_hd", [(0, -0.12), (0.05, -0.12), (0.055, -0.1), (0.055, 0.2), (0.06, 0.22), (0, 0.22)],
               rubber("#3a8a5a", "ribs"), 24, z=z + 0.04),
         lathe(n + "_col", [(0, 0.2), (0.07, 0.2), (0.08, 0.24), (0.08, 0.3), (0, 0.3)], steel("#c9d1dc"), 24, z=z + 0.04),
         ball(n + "_bulb", (0.42, 0, z + 0.04), (0.14, 0.1, 0.1), glow("#ffd23a", 3.0), 24, 14),
         ball(n + "_bulbg", (0.42, 0, z + 0.04), (0.15, 0.11, 0.11), glass("#fff3a0"), 24, 14),
         lathe(n + "_cap", [(0, 0.55), (0.04, 0.55), (0.03, 0.6), (0, 0.6)], steel(), 16, z=z + 0.04)]
    for i in range(4):
        a = 2 * math.pi * i / 4 + math.pi / 4
        pts = [(0.3, -0.08 * math.cos(a), z + 0.04 + 0.08 * math.sin(a)), (0.42, -0.115 * math.cos(a), z + 0.04 + 0.115 * math.sin(a)),
               (0.56, -0.04 * math.cos(a), z + 0.04 + 0.04 * math.sin(a))]
        o.append(sweep(n + "_cg%d" % i, pts, 0.008, steel(), 6, smooth_k=4))
    o.append(lathe(n + "_pc", [(0, -0.16), (0.05, -0.16), (0.06, -0.12), (0, -0.12)], plastic("#2c7a2a"), 20, z=z + 0.04))
    return o, V((0.6, 0, z + 0.04))


# ------------------------------------------------------------------------------------------- throwables

def w_grenade(n):
    return frag_grenade(n, paint("#5d7a32", chips=0.5)), V((0.18, 0, 0.15))


def w_cluster_grenade(n):
    return frag_grenade(n, paint("#2a5ad8", chips=0.4, coat=0.5), r=0.16, segs=False), V((0.18, 0, 0.15))


def w_teleport_grenade(n):
    c = (0.1, 0, 0.1)
    o = [ball(n + "_fl", c, 0.15, glass("#4fd8c8"), 28, 18),
         ball(n + "_liq", (0.1, 0, 0.07), (0.12, 0.12, 0.1), glow("#7ae8d0", 1.5), 20, 12),
         cyl(n + "_nk", (0.1, 0, 0.22), (0.1, 0, 0.3), 0.05, 0.045, glass("#4fd8c8"), 16),
         cyl(n + "_cork", (0.1, 0, 0.28), (0.1, 0, 0.35), 0.05, 0.055, steel("#d8803a"), 16),
         C.torus(n + "_cr", (0.1, 0, 0.29), 0.055, 0.012, steel("#d8803a"), 20, 6)]
    o.append(C.torus(n + "_pin", (0.03, -0.02, 0.36), 0.04, 0.008, steel("#d0d6de"), 16, 6, axis=(0.3, 1, 0.2)))
    return o, V((0.18, 0, 0.15))


def w_lemon(n):
    c = (0.1, 0, 0.1)
    peel_m = TM("lemon_peel", "peel", "#ffe03a", rough=0.35, coat=0.4, bump=0.6)
    o = [blob_ball(n + "_lm", c, 0.15, peel_m, seed=2, lumps=0.02)]
    o[0].data.transform(Matrix.Translation(c) @ Matrix.Diagonal((1.25, 1.0, 1.0, 1)) @ Matrix.Translation(-V(c)))
    o.append(ball(n + "_tip", (0.29, 0, 0.1), (0.04, 0.035, 0.035), peel_m, 12, 8))
    o.append(cyl(n + "_cap", (0.02, 0, 0.2), (0.0, 0, 0.28), 0.06, 0.05, plastic("#8a4fd8"), 18))
    o.append(ball(n + "_cpk", (0.0, 0, 0.28), 0.05, plastic("#8a4fd8"), 16, 8))
    o.append(ball(n + "_eye", (0.1, -0.15, 0.12), (0.045, 0.02, 0.045), glow("#4cbb3f", 2.0), 14, 8))
    o.append(C.torus(n + "_eyr", (0.1, -0.15, 0.12), 0.048, 0.01, gunmetal(), 20, 6, axis=(0, 1, 0)))
    return o, V((0.2, 0, 0.15))


def w_cinder(n):
    c = (0.1, 0, 0.1)
    o = [rock_mesh(n + "_rk", c, 0.15, TM("cinder", "cinder", rough=0.7, bump=0.8, emit=1.2), seed=5, sub=2, jitter=0.16,
                   scale=(1.0, 0.95, 1.05))]
    top = 0.25
    o.append(cyl(n + "_neck", (0.1, 0, top - 0.04), (0.1, 0, top + 0.04), 0.045, 0.04, steel("#8d99a8"), 16))
    o.append(cyl(n + "_cap", (0.1, 0, top + 0.04), (0.1, 0, top + 0.07), 0.05, 0.048, steel("#c9d1dc"), 16))
    o.append(C.torus(n + "_pin", (0.03, -0.02, top + 0.06), 0.045, 0.009, steel("#d0d6de"), 20, 6, axis=(0.3, 1, 0.2)))
    return o, V((0.2, 0, 0.15))


def w_gas(n):
    c = (0.1, 0, 0.0)
    can = steel("#8a9aa4")
    o = [C.lathe(n + "_can", [(0, 0), (0.12, 0), (0.125, 0.01), (0.13, 0.3), (0.135, 0.31), (0, 0.31)], can, 28,
                 center=c, axis=(0, 0, 1)),
         C.lathe(n + "_lid", [(0, 0.3), (0.14, 0.3), (0.14, 0.34), (0.08, 0.36), (0, 0.36)], gunmetal(), 28, center=c,
                 axis=(0, 0, 1))]
    for k in range(3):
        o.append(C.torus(n + "_rib%d" % k, (0.1, 0, 0.06 + k * 0.09), 0.128, 0.008, can, 28, 4))
    o.append(C.lathe(n + "_lb", [(0, 0.12), (0.131, 0.12), (0.131, 0.2), (0, 0.2)], paint("#8ee84a", chips=0.3), 28,
                     center=c, axis=(0, 0, 1)))
    o.append(flat_decal(n + "_skull", [circle(0.025, 14, 0, 0.005), rect(0.03, 0.02, 0, -0.022)], (0.1, -0.132, 0.16),
                        paint("#262b34", chips=0.0)))
    o.append(sweep(n + "_bail", [(-0.03, 0, 0.32), (0.0, 0, 0.42), (0.2, 0, 0.42), (0.23, 0, 0.32)], 0.01, steel(), 6,
                   smooth_k=4))
    o.append(cyl(n + "_vlv", (0.1, 0, 0.36), (0.1, 0, 0.4), 0.025, 0.02, steel("#d8a845"), 12))
    return o, V((0.2, 0, 0.2))


def w_sticky(n):
    c = (0.1, 0, 0.1)
    sl = TM("slime", "slime", "#5ed83a", rough=0.12, coat=0.8, bump=0.3)
    o = [blob_ball(n + "_b", c, 0.16, sl, seed=4, lumps=0.07)]
    for i, (dx, ln) in enumerate(((0.06, 0.09), (-0.06, 0.06), (0.0, 0.12))):
        y0 = -math.sqrt(max(0.0, 0.16 ** 2 - dx * dx)) * 0.92
        o.append(sweep(n + "_dr%d" % i, [(0.1 + dx, y0, 0.12), (0.1 + dx, y0 - 0.012, 0.08), (0.1 + dx, y0 - 0.008, 0.08 - ln)],
                       0.026, sl, 10, r_end=0.017, smooth_k=3))
        o.append(ball(n + "_drb%d" % i, (0.1 + dx, y0 - 0.008, 0.08 - ln), 0.024, sl, 12, 8))
    o.append(cyl(n + "_kn", (0.1, 0, 0.24), (0.1, 0, 0.31), 0.03, 0.025, gunmetal(), 12))
    o.append(ball(n + "_lt", (0.1, 0, 0.32), 0.022, glow("#ff3a3a", 2.5), 10, 6))
    return o, V((0.2, 0, 0.15))


def w_molotov(n):
    o = bottle(n, glass("#8a5020"), TM("label_cream", "paper", "#efe2c0", rough=0.8), c=(0.1, 0, -0.08))
    return o, V((0.25, 0, 0.36))


def w_dynamite(n):
    z = 0.06
    paper_m = TM("dyn_paper", "paper", "#d02a24", print_col="#8a1410", rough=0.6, bump=0.3)
    o = [lathe(n + "_st", [(0, -0.1), (0.05, -0.1), (0.055, -0.09), (0.055, 0.32), (0.05, 0.33), (0, 0.33)], paper_m, 24,
               z=z),
         lathe(n + "_lb", [(0, 0.06), (0.057, 0.06), (0.057, 0.16), (0, 0.16)], TM("label_cream", "paper", "#efe2c0",
                                                                                   rough=0.8), 24, z=z),
         tube(n + "_end", 0.33, 0.335, 0.045, TM("dyn_end", "paper", "#e8d8b0", rough=0.9), z=z)]
    o += warning_sign(n + "_ws", 0.11, 0.057, z=z, size=0.032)
    o.append(sweep(n + "_fuse", [(0.33, 0, z), (0.38, 0, z + 0.03), (0.42, 0, z + 0.1), (0.4, 0, z + 0.15)], 0.009,
                   TM("rope", "fabric", "#d8c08a", weave=140, rough=0.9, bump=0.5), 6, smooth_k=4))
    o.append(ball(n + "_sp", (0.4, 0, z + 0.16), 0.03, glow("#ffd040", 4.0), 10, 6))
    return place(o, (0.0, 0, 0.0)), V((0.4, 0, z + 0.16))


def w_plasma_bomb(n):
    z = 0.06
    o = []
    for i, (yy, zz) in enumerate(((-0.05, 0.0), (0.05, 0.0), (0.0, 0.085))):
        o.append(lathe(n + "_t%d" % i, [(0, -0.1), (0.045, -0.1), (0.05, -0.08), (0.05, 0.3), (0.045, 0.32), (0, 0.32)],
                       glass("#5f9dff"), 20, y=yy, z=z + zz))
        o.append(tube(n + "_c%d" % i, -0.08, 0.3, 0.032, glow("#4fa8ff", 3.0), y=yy, z=z + zz, segs=12))
        for e, xx in enumerate((-0.1, 0.32)):
            o.append(lathe(n + "_e%d%d" % (i, e), [(0, xx - 0.02), (0.052, xx - 0.02), (0.052, xx + 0.02), (0, xx + 0.02)],
                           steel("#c9d1dc"), 20, y=yy, z=z + zz))
    for k, xx in enumerate((0.0, 0.22)):
        o.append(rbox(n + "_strap%d" % k, (xx, 0, z + 0.04), (0.04, 0.22, 0.2), rubber("#30333a", "ribs"), 0.03, 2))
    o += alarm_clock(n + "_clk", (0.11, -0.12, z + 0.06), 0.065, paint("#d8302c", chips=0.2))
    o.append(sweep(n + "_wr", [(0.06, -0.13, z + 0.0), (0.0, -0.13, z - 0.06), (-0.05, -0.1, z - 0.04)], 0.008,
                   plastic("#ffd23a"), 6, smooth_k=3))
    return o, V((0.32, 0, z + 0.04))


def w_napalm(n):
    c = (0.1, 0, -0.06)
    body = TM("napalm_stripes", "stripes", "#d8302c", "#ffb424", period=6, diagonal=False, chips=0.4, rough=0.35, coat=0.3)
    o = [C.lathe(n + "_cn", [(0, 0), (0.1, 0), (0.11, 0.02), (0.11, 0.34), (0, 0.34)], body, 28, center=c, axis=(0, 0, 1)),
         C.lathe(n + "_cap", [(0, 0.33), (0.112, 0.33), (0.105, 0.38), (0.07, 0.42), (0.0, 0.43)], paint("#f2f4f8", chips=0.3),
                 28, center=c, axis=(0, 0, 1)),
         C.lathe(n + "_base", [(0, -0.01), (0.115, -0.01), (0.115, 0.03), (0, 0.03)], gunmetal(), 28, center=c, axis=(0, 0, 1))]
    o.append(C.torus(n + "_r", (0.1, 0, -0.06 + 0.33), 0.112, 0.01, steel(), 28, 6))
    o.append(flat_decal(n + "_wf", triangle(0.045), (0.1, -0.112, 0.1), paint("#262b34", chips=0.0)))
    o.append(flat_decal(n + "_wf2", triangle(0.034), (0.1, -0.115, 0.1), paint("#ffd23a", chips=0.0)))
    o.append(cyl(n + "_nz", (0.1, 0, 0.36), (0.1, 0, 0.42), 0.02, 0.016, steel(), 10))
    return o, V((0.2, 0, 0.2))


def w_fuel_air(n):
    c = (0.1, 0, -0.04)
    frame = paint("#c8302c", chips=0.4)
    o = [C.lathe(n + "_gl", [(0, 0.02), (0.085, 0.02), (0.095, 0.04), (0.095, 0.3), (0.085, 0.32), (0, 0.32)],
                 glass("#ffb040"), 28, center=c, axis=(0, 0, 1)),
         C.lathe(n + "_core", [(0, 0.03), (0.07, 0.03), (0.07, 0.31), (0, 0.31)], glow("#ff9b21", 2.5), 20, center=c,
                 axis=(0, 0, 1))]
    for e, zz in enumerate((0.0, 0.31)):
        o.append(C.lathe(n + "_cp%d" % e, [(0, zz), (0.11, zz), (0.115, zz + 0.01), (0.115, zz + 0.04), (0.1, zz + 0.05), (0, zz + 0.05)],
                         frame, 28, center=c, axis=(0, 0, 1)))
    for i in range(4):
        a = 2 * math.pi * i / 4 + math.pi / 4
        o.append(cyl(n + "_bar%d" % i, (0.1 + 0.1 * math.cos(a), 0.1 * math.sin(a), -0.02),
                     (0.1 + 0.1 * math.cos(a), 0.1 * math.sin(a), 0.3), 0.012, 0.012, frame, 8))
    o.append(cyl(n + "_vl", (0.1, 0, 0.31), (0.1, 0, 0.36), 0.03, 0.025, steel(), 12))
    return o, V((0.2, 0, 0.2))


def w_doomsday(n):
    c = (0.12, 0, 0.14)
    body = TM("doom_metal", "metal", "#5a5f68", rust=0.3, rough=0.35, metal=0.7)
    o = [ball(n + "_sp", c, 0.2, body, 32, 20)]
    o.append(C.torus(n + "_eq", c, 0.2, 0.014, gunmetal(), 32, 6))
    o.append(C.torus(n + "_mer", c, 0.2, 0.012, gunmetal(), 32, 6, axis=(1, 0, 0)))
    org = paint("#f08a24", chips=0.4)
    for i, d in enumerate(((1, 0, 0.0), (-1, 0, 0.0), (0, 0, 1), (0, 0, -1), (0.7, -0.7, 0.2))):
        dv = V(d).normalized()
        p = V(c) + dv * 0.19
        o.append(cyl(n + "_pad%d" % i, p, p + dv * 0.05, 0.06, 0.055, org, 20))
    # LED display
    disp = TM("screen_led", "screen", "#2a0606", "#ff2a2a", glow=1.5, bump=0.0)
    o.append(rbox(n + "_dsb", (0.12, -0.19, 0.14), (0.2, 0.06, 0.09), gunmetal("#22252c"), 0.012))
    o.append(rbox(n + "_ds", (0.12, -0.221, 0.14), (0.17, 0.004, 0.065), disp, 0.001, 1))
    for i in range(4):
        o.append(rbox(n + "_dg%d" % i, (0.06 + i * 0.04, -0.224, 0.14), (0.026, 0.004, 0.045), glow("#ff3a2a", 3.0), 0.0, 1))
    o += [rivet(n + "_bt%d" % i, (0.12 + 0.17 * math.cos(a), -0.17 * math.sin(a) * 0.2 - 0.1, 0.14 + 0.17 * math.sin(a)),
                0.014, steel(), (math.cos(a), -0.6, math.sin(a))) for i, a in enumerate((0.6, 2.5, 3.8, 5.5))]
    o.append(cyl(n + "_an", (0.2, 0, 0.32), (0.26, 0, 0.5), 0.01, 0.007, steel(), 6))
    o.append(ball(n + "_ab", (0.26, 0, 0.51), 0.025, glow("#ff3a2a", 3.0), 10, 6))
    return [x for x in o if x is not None], V((0.32, 0, 0.14))


def w_egg(n):
    m = TM("egg_a", "egg", ["#ff8fa8", "#ffd23a", "#7ae8c0", "#ffffff"], rough=0.3, coat=0.6, bump=0.1)
    return [egg_mesh(n + "_e", (0.1, 0, 0.12), 0.17, m, -0.3)], V((0.18, 0, 0.15))


def w_snowball(n):
    o = [blob_ball(n + "_s", (0.1, 0, 0.1), 0.16, TM("snow", "snow", rough=0.6, bump=0.8), seed=6, lumps=0.07)]
    return o, V((0.18, 0, 0.12))


def w_rock(n):
    o = [rock_mesh(n + "_r", (0.1, 0, 0.1), 0.17, TM("rock_grey", "stone", "#80807c", cracks=1.0, rough=0.7, bump=0.9),
                   seed=3, sub=1, jitter=0.22)]
    C.flat(o[0])
    return o, V((0.18, 0, 0.12))


def w_water_balloon(n):
    c = (0.1, 0, 0.1)
    lat = TM("latex_blue", "latex", "#3a8ae8", rough=0.12, coat=0.8, bump=0.1)
    pr = [(0, -0.17)]
    for i in range(1, 14):
        a = math.pi * i / 14
        pr.append((0.16 * math.sin(a) * (1 + 0.1 * math.cos(a)), -0.17 * math.cos(a) * (1.0 if a < 1.57 else 0.95)))
    pr += [(0.03, 0.17), (0.02, 0.2), (0, 0.2)]
    o = [C.lathe(n + "_b", pr, lat, 28, center=c, axis=(0, 0, 1))]
    o.append(C.lathe(n + "_knot", [(0, 0.185), (0.03, 0.19), (0.04, 0.21), (0.022, 0.235), (0.032, 0.25), (0.03, 0.265),
                                   (0, 0.27)], lat, 20, center=c, axis=(0, 0, 1)))
    return o, V((0.18, 0, 0.12))


def w_cat(n):
    """Halloween cat: grey tabby with big eyes, curled tail."""
    fur_m = TM("fur_tabby", "fur", "#6a6a76", stripe="#33333c", rough=0.85, bump=0.6)
    white = TM("fur_white", "fur", "#e8e6ec", rough=0.85, bump=0.4)
    o = [blob_ball(n + "_body", (0.12, 0, 0.1), 0.15, fur_m, seed=1, lumps=0.02)]
    o[0].data.transform(Matrix.Translation((0.12, 0, 0.1)) @ Matrix.Diagonal((1.15, 0.9, 0.85, 1)) @ Matrix.Translation((-0.12, 0, -0.1)))
    o.append(blob_ball(n + "_head", (0.3, -0.02, 0.27), 0.12, fur_m, seed=2, lumps=0.02))
    o.append(blob_ball(n + "_muz", (0.33, -0.11, 0.24), 0.05, white, seed=3, lumps=0.02))
    for s in (-1, 1):
        o.append(cyl(n + "_ear%d" % s, (0.3 + s * 0.06, -0.02, 0.35), (0.3 + s * 0.09, -0.02, 0.45), 0.045, 0.0, fur_m, 10))
        o.append(cyl(n + "_eari%d" % s, (0.3 + s * 0.06, -0.04, 0.355), (0.3 + s * 0.085, -0.045, 0.43), 0.03, 0.0,
                     paint("#ff8fa8", chips=0.0), 8))
        o.append(ball(n + "_eye%d" % s, (0.3 + s * 0.05, -0.115, 0.3), (0.035, 0.02, 0.04), paint("#f5f7fb", chips=0.0, coat=0.8), 14, 8))
        o.append(ball(n + "_pup%d" % s, (0.3 + s * 0.05, -0.132, 0.3), (0.015, 0.008, 0.024), dark(), 10, 6))
        o.append(blob_ball(n + "_paw%d" % s, (0.12 + s * 0.09, -0.08, -0.03), 0.045, white, seed=4 + s, lumps=0.02))
    o.append(ball(n + "_nose", (0.33, -0.16, 0.255), 0.014, paint("#ff6a8a", chips=0.0), 8, 6))
    o.append(sweep(n + "_tail", [(-0.03, 0.03, 0.08), (-0.12, 0.05, 0.12), (-0.14, 0.04, 0.25), (-0.08, 0.02, 0.32)],
                   0.03, fur_m, 10, r_end=0.02, smooth_k=5))
    for s in (-1, 1):
        for k in range(2):
            o.append(cyl(n + "_wh%d%d" % (s, k), (0.33 + s * 0.03, -0.15, 0.24 + k * 0.015),
                         (0.33 + s * 0.14, -0.14, 0.23 + k * 0.035), 0.003, 0.002, paint("#f5f7fb", chips=0.0), 4))
    return o, V((0.35, 0, 0.15))


def w_molotov_like(n):
    return w_molotov(n)


# ------------------------------------------------------------------------------------------- icon compositions

def i_basic_nuke(n):
    return place(rocket(n, paint("#f2f4f8", chips=0.25), paint("#d8302c"), paint("#d8302c"), L=0.9, r=0.11,
                        stripe=paint("#d8302c")), (-0.45, 0, 0))


def i_ap_rocket(n):
    return place(rocket(n, steel("#a4adb8"), paint("#23a69a"), steel("#8d99a8"), L=1.0, r=0.1, nose_len=0.38,
                        band=paint("#23a69a")), (-0.5, 0, 0))


def i_cluster_rocket(n):
    o = []
    for k, (dx, dz, s) in enumerate(((0.0, 0.14, 1.0), (-0.12, -0.08, 0.85), (0.14, -0.2, 0.75))):
        o += place(rocket(n + "%d" % k, paint("#eef0f4", chips=0.2), paint("#8a5ad8"), paint("#8a5ad8"), L=0.6, r=0.075,
                          nose_len=0.24), (dx - 0.3, 0.02 * k, dz), s=s)
    return o


def i_mini_bazooka(n):
    o = []
    for k, (dx, dz, s) in enumerate(((0.0, 0.16, 1.0), (-0.15, -0.06, 0.85), (0.12, -0.2, 0.7))):
        o += place(rocket(n + "%d" % k, paint("#f08a24", chips=0.2), paint("#f2f4f8", chips=0.2), paint("#e0402a"),
                          L=0.5, r=0.075, nose_len=0.2), (dx - 0.25, 0.02 * k, dz), s=s)
    return o


def i_frag(n):
    m = TM("spike_metal", "metal", "#8a8aa0", rough=0.3, metal=0.8)
    o = spiked_ball(n + "a", (0.12, 0, 0.14), 0.15, m)
    o += spiked_ball(n + "b", (-0.15, 0.05, -0.08), 0.09, m)
    o += spiked_ball(n + "c", (0.08, 0.08, -0.17), 0.075, m)
    return o


def i_mortar(n):
    return place(shell(n, paint("#4f7a34"), steel("#a4adb8"), gunmetal()), (-0.3, 0, 0))


def i_plasma_mortar(n):
    o = place(shell(n, paint("#3a6ae0"), glow("#b07ae8", 1.5), gunmetal("#24489c")), (-0.3, 0, 0))
    return o


def i_mega_nuke(n):
    org = paint("#f08a24", chips=0.4)
    o = [lathe(n + "_b", [(0, -0.3), (0.12, -0.3), (0.2, -0.22), (0.24, -0.05), (0.24, 0.15), (0.2, 0.3), (0.12, 0.4),
                          (0.06, 0.44), (0, 0.45)], org, 32)]
    for xx in (-0.12, 0.08):
        o.append(lathe(n + "_bd%d" % (xx > 0), [(0, xx), (0.242, xx), (0.242, xx + 0.05), (0, xx + 0.05)], steel("#a4adb8"), 32))
    o.append(ball(n + "_win", (0.27, -0.06, 0.0), (0.02, 0.07, 0.07), glass("#e0f4ff"), 18, 10))
    o.append(ball(n + "_port", (0.33, -0.1, 0.0), (0.035, 0.05, 0.05), glass("#e0f4ff"), 16, 10))
    o += rad_sign(n + "_rs", 0.0, 0.24, size=0.07, theta=0.0)
    for i in range(3):
        f = prof(n + "_f%d" % i, [(-0.4, 0.18, 0.02), (-0.1, 0.2, 0.0), (-0.2, 0.33, 0.03), (-0.42, 0.32, 0.02)], 0.03,
                 steel("#8d99a8"), bev=0.008)
        rot_x([f], math.radians(30 + 120 * i))
        o.append(f)
    o.append(lathe(n + "_nz", [(0, -0.42), (0.1, -0.42), (0.12, -0.3), (0, -0.3)], gunmetal(), 24))
    return o


def i_orbital(n):
    return satellite(n)


def i_easter(n):
    o = [egg_mesh(n + "_a", (0.0, 0.05, 0.05), 0.17, TM("egg_a", "egg", ["#ff8fa8", "#ffd23a", "#7ae8c0", "#ffffff"],
                                                          rough=0.3, coat=0.6, bump=0.1), -0.25),
         egg_mesh(n + "_b", (0.2, 0.12, 0.08), 0.16, TM("egg_b", "egg", ["#ffd23a", "#ff6a8a", "#b07ae8", "#ffffff"],
                                                        rough=0.3, coat=0.6, bump=0.1), 0.2),
         egg_mesh(n + "_c", (0.1, -0.12, -0.03), 0.15, TM("egg_c", "egg", ["#7ae8c0", "#ffd23a", "#3a7be8", "#ff8fa8"],
                                                         rough=0.3, coat=0.6, bump=0.1), 0.1)]
    return o


WEAPONS = {
    "BasicNuke": w_basic_nuke,
    "MegaNuke": w_mega_nuke,
    "Molotov": w_molotov,
    "Rock": w_rock,
    "Minigun": w_minigun,
    "Grenade": w_grenade,
    "Mine": w_molotov_like,
    "OrbitalLaser": w_orbital,
    "Pistol": w_pistol,
    "Dynamite": w_dynamite,
    "Mortar": w_mortar,
    "ClusterGrenade": w_cluster_grenade,
    "Shotgun": w_shotgun,
    "FlameMine": w_molotov_like,
    "GasGrenade": w_gas,
    "PlasmaCannon": w_plasma_cannon,
    "Punch": w_punch,
    "ClusterRocket": w_cluster_rocket,
    "DoomsdayDevice": w_doomsday,
    "VoidGenerator": w_void,
    "FragmentationMissile": w_frag_missile,
    "StickyBomb": w_sticky,
    "Railgun": w_railgun,
    "ArmorPiercingRocket": w_ap_rocket,
    "Beanbag": w_beanbag,
    "Flamethrower": w_flamethrower,
    "LemonGrenade": w_lemon,
    "TeleportationGrenade": w_teleport_grenade,
    "Drill": w_drill,
    "PointTeleport": w_point_teleport,
    "MiniBazooka": w_mini_bazooka,
    "Napalm": w_napalm,
    "SniperRifle": w_sniper,
    "CinderGrenade": w_cinder,
    "ImpactCannon": w_impact_cannon,
    "ShieldWall": w_shield,
    "FlareGun": w_flare_gun,
    "EasterEgg": w_egg,
    "Cannon": w_cannon,
    "LaserPistol": w_laser_pistol,
    "PlasmaMortar": w_plasma_mortar,
    "FireHose": w_fire_hose,
    "MiningLaser": w_mining_laser,
    "PlasmaBomb": w_plasma_bomb,
    "GrenadeLauncher": w_grenade_launcher,
    "FuelAirBomb": w_fuel_air,
    "ArtilleryStrike": w_artillery,
    "WaterBalloon": w_water_balloon,
    "WandWind": w_wand,
    "Scythe": w_scythe,
    "Cat": w_cat,
    "Broom": w_broom,
    "Snowball": w_snowball,
    "Fireworks": w_fireworks,
    # weapons without a WeaponGraphic record in the config (restored original weapons)
    "HeatSeeker": w_heat_seeker,
    "GreyGoo": w_grey_goo,
    "ChocoCannon": w_choco_cannon,
}
EXTRA_IDS = ["HeatSeeker", "GreyGoo", "ChocoCannon"]

# the original shop icon shows the ammo / a different subject than the held launcher
ICONS = {
    "BasicNuke": i_basic_nuke,
    "ArmorPiercingRocket": i_ap_rocket,
    "ClusterRocket": i_cluster_rocket,
    "MiniBazooka": i_mini_bazooka,
    "FragmentationMissile": i_frag,
    "Mortar": i_mortar,
    "PlasmaMortar": i_plasma_mortar,
    "MegaNuke": i_mega_nuke,
    "OrbitalLaser": i_orbital,
    "EasterEgg": i_easter,
}
# per-icon tilt (degrees); round throwables stay upright
ICON_TILTS = {k: 0.0 for k in ("Grenade", "ClusterGrenade", "TeleportationGrenade", "LemonGrenade", "CinderGrenade",
                               "GasGrenade", "StickyBomb", "Molotov", "Napalm", "FuelAirBomb", "DoomsdayDevice",
                               "EasterEgg", "Snowball", "Rock", "WaterBalloon", "Cat", "FragmentationMissile",
                               "Punch")}
ICON_TILTS.update({k: 38.0 for k in ("Shotgun", "SniperRifle", "GrenadeLauncher", "WandWind", "Broom", "Fireworks",
                                     "Scythe", "Railgun", "PlasmaCannon", "MiningLaser")})
ICON_TILTS.update({"OrbitalLaser": 20.0, "Dynamite": 40.0, "Molotov": -15.0, "MegaNuke": 35.0, "ClusterRocket": 25.0, "MiniBazooka": 25.0})


# =========================================================================================== build / export / icons

def build(wid, col):
    """Held model for a WeaponGraphic id: one joined mesh named wid with a Muzzle empty child."""
    C.use_collection(col)
    fn = WEAPONS.get(wid) or WEAPONS["MiniBazooka"]
    objs, tip = fn(wid + "_")
    ob = C.join(objs, wid)
    mz = C.empty("Muzzle", tip, 0.1)
    C.parent(mz, ob)
    C.use_collection(None)
    return ob


def build_icon(wid, col):
    """Icon composition: ICONS[wid] when the original icon shows something else than the held model."""
    fn = ICONS.get(wid)
    if fn is None:
        ob = build(wid, col)
        for ch in list(ob.children):
            bpy.data.objects.remove(ch)
        return ob
    C.use_collection(col)
    objs = fn(wid + "_")
    ob = C.join(objs, wid)
    C.use_collection(None)
    return ob


def run():
    C.reset()
    _ship[0] = True
    ids = C.ids("WeaponGraphic")
    ids += [w for w in EXTRA_IDS if w not in ids]
    stats = {}
    for wid in ids:
        col = C.new_collection("W_" + wid)
        ob = build(wid, col)
        stats[wid] = C.tri_count([ob])
        C.export_collection(col, os.path.join(C.MODELS, "Weapons", wid + ".fbx"))
    C.save_blend("weapons")
    _ship[0] = False
    pruned = WT.prune()
    big = sorted(stats.items(), key=lambda kv: -kv[1])[:5]
    print("[weapons] %d models, %d game textures (%d stale removed), largest: %s" % (len(stats), len(WT._shipped), pruned, big))


ICON_VIEW = (-0.28, -1.0, 0.36)   # from front-left-above (same for every weapon icon)
ICON_TILT = 28.0                  # degrees the barrel is raised (the original icons are drawn diagonally)


def icon_items():
    """[(item id, WeaponGraphic id)] for every Weapon item (Featured* copies share their graphic)."""
    out = []
    items = C.config().get("Item", {})
    for k, v in items.items():
        if k.startswith("$") or not isinstance(v, dict) or v.get("Type") != "Weapon":
            continue
        g = (v.get("Graphics") or ("#WeaponGraphic." + k)).split(".")[-1]
        out.append((k, g))
    return out


def icons(only=None, size=384, held=False, out_dir=None):
    """Render the weapon icons (held=True renders the held models instead, for reviewing them)."""
    import render as R
    C.reset()
    R.setup_scene()
    sc = bpy.context.scene
    sc.world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    done = {}
    out_dir = out_dir or ICON_DIR
    os.makedirs(out_dir, exist_ok=True)
    for item, gid in icon_items():
        if only and item not in only and gid not in only:
            continue
        out = os.path.join(out_dir, item + ".png")
        if gid in done:
            shutil.copyfile(done[gid], out)
            continue
        col = C.new_collection("ICON")
        if held:
            ob = build(gid, col)
            for ch in list(ob.children):
                bpy.data.objects.remove(ch)
        else:
            ob = build_icon(gid, col)
        tilt = 0.0 if held else ICON_TILTS.get(gid, ICON_TILT)
        ob.rotation_euler = (0, math.radians(-tilt), 0)
        bpy.context.view_layer.update()
        R.render_icon([col], out, view=ICON_VIEW, size=size, pad=0.07)
        done[gid] = out
        for o in list(col.all_objects):
            bpy.data.objects.remove(o)
        bpy.data.collections.remove(col)
        print("[icon] %s" % item, flush=True)
    return len(done)


if __name__ == "__main__":
    args = sys.argv[1:]
    what = args[0] if args else "all"
    only = args[1].split(",") if len(args) > 1 else None
    if what in ("all", "models"):
        run()
    if what in ("all", "icons"):
        icons(only)
    if what == "held":   # review renders of the held models (not shipped)
        icons(only, 256, held=True, out_dir=os.environ.get("CPW_HELD_DIR", "/tmp/cpw_held"))
