"""Supplies (Item.Type "Booster"): detailed, textured 3D models rendered to shop/battle icons.

Usage (repo root):  python3 Blender/scripts/supplies.py [SupplyId ...]
Output: Assets/CPW/Resources/Icons/Supplies/{ItemId}.png (256 px, transparent, the shared icon style of render.py:
3/4 view, three-light rig, dark outline + drop shadow), rendered at 512 px and downsampled.

Every model is built from several bevelled / subdivided parts and every part wears an image-texture material:
procedural textures painted by supplies_tex.py (rice grains, fish flesh, nori, roe, tortilla, chocolate, broth,
wood planks, brushed/painted metal, rubber, fabric weave, leather, hazard stripes, foil, glazed ceramic, parchment)
plus PIL decals (scroll page, wrapper label, headband), mapped by box / tube projection in object space or by UVs,
with a bump map from the texture's height channel.
Env: CPW_SAMPLES (Cycles samples, default 32 from render.py).
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import common as C  # noqa: E402
import render as R  # noqa: E402
import supplies_tex as X  # noqa: E402

V = Vector
OUT = os.path.join(C.ICONS, "Supplies")
SIZE = 256
RENDER = 512

# ------------------------------------------------------------------------------------------ materials

_mats = {}


def _node(nt, kind, loc=(0, 0)):
    n = nt.nodes.new(kind)
    n.location = loc
    return n


def tmat(name, tex=None, rgba=None, proj="BOX", scale=1.0, loc=(0, 0, 0), rot=(0, 0, 0), coords="Object",
         rough=0.55, metal=0.0, bump=0.25, tint=None, color=None, emission=0.0, emit_color=None, coat=0.0,
         alpha=1.0, alpha_tex=False, sheen=0.0, sss=0.0, spec=0.4, blend=0.25):
    """Image-texture material. tex: name of a supplies_tex generator (or (name, fn, args)) giving color+height;
    rgba: path of an RGBA decal (UV mapped unless coords says otherwise). tint multiplies the color."""
    if name in _mats and _mats[name].name in bpy.data.materials:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    try:
        bsdf.inputs["Specular IOR Level"].default_value = spec
    except KeyError:
        pass
    if coat:
        bsdf.inputs["Coat Weight"].default_value = coat
        bsdf.inputs["Coat Roughness"].default_value = 0.08
    if sheen:
        bsdf.inputs["Sheen Weight"].default_value = sheen
    if sss:
        bsdf.inputs["Subsurface Weight"].default_value = sss
        bsdf.inputs["Subsurface Radius"].default_value = (0.4, 0.2, 0.1)
        bsdf.inputs["Subsurface Scale"].default_value = 0.05
    vec = None
    if tex is not None or rgba is not None:
        tc = _node(nt, "ShaderNodeTexCoord", (-900, 0))
        mp = _node(nt, "ShaderNodeMapping", (-700, 0))
        s = scale if isinstance(scale, (tuple, list)) else (scale, scale, scale)
        mp.inputs["Scale"].default_value = s
        mp.inputs["Location"].default_value = loc
        mp.inputs["Rotation"].default_value = rot
        nt.links.new(tc.outputs[coords], mp.inputs["Vector"])
        vec = mp.outputs["Vector"]
    col_out = None
    if tex is not None:
        if isinstance(tex, str):
            tex = (tex, getattr(X, tex), ())
        tname, fn, args = tex
        cpath, hpath = X.get(tname, fn, *args)
        ci = _node(nt, "ShaderNodeTexImage", (-450, 200))
        ci.image = bpy.data.images.load(cpath, check_existing=True)
        ci.projection = proj
        ci.projection_blend = blend
        ci.extension = "REPEAT"
        nt.links.new(vec, ci.inputs["Vector"])
        col_out = ci.outputs["Color"]
        if bump > 0:
            hi = _node(nt, "ShaderNodeTexImage", (-450, -200))
            hi.image = bpy.data.images.load(hpath, check_existing=True)
            hi.image.colorspace_settings.name = "Non-Color"
            hi.projection = proj
            hi.projection_blend = blend
            nt.links.new(vec, hi.inputs["Vector"])
            bp = _node(nt, "ShaderNodeBump", (-200, -200))
            bp.inputs["Strength"].default_value = bump
            bp.inputs["Distance"].default_value = 0.02
            nt.links.new(hi.outputs["Color"], bp.inputs["Height"])
            nt.links.new(bp.outputs["Normal"], bsdf.inputs["Normal"])
    if rgba is not None:
        di = _node(nt, "ShaderNodeTexImage", (-450, 200))
        di.image = bpy.data.images.load(rgba, check_existing=True)
        di.projection = proj if coords != "UV" else "FLAT"
        di.extension = "CLIP" if coords == "UV" else "REPEAT"
        nt.links.new(vec, di.inputs["Vector"])
        col_out = di.outputs["Color"]
        if alpha_tex:
            nt.links.new(di.outputs["Alpha"], bsdf.inputs["Alpha"])
    if col_out is not None and tint is not None:
        mx = _node(nt, "ShaderNodeMix", (-200, 200))
        mx.data_type = "RGBA"
        mx.blend_type = "MULTIPLY"
        mx.inputs["Factor"].default_value = 1.0
        nt.links.new(col_out, mx.inputs[6])
        mx.inputs[7].default_value = (*[C.srgb_to_linear(c) for c in C.hex_rgb(tint)], 1)
        col_out = mx.outputs[2]
    if col_out is not None:
        nt.links.new(col_out, bsdf.inputs["Base Color"])
    elif color is not None:
        bsdf.inputs["Base Color"].default_value = (*[C.srgb_to_linear(c) for c in C.hex_rgb(color)], 1)
    if emission > 0:
        if emit_color:
            bsdf.inputs["Emission Color"].default_value = (*[C.srgb_to_linear(c) for c in C.hex_rgb(emit_color)], 1)
        elif col_out is not None:
            nt.links.new(col_out, bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emission
    if alpha < 1.0 and not alpha_tex:
        bsdf.inputs["Alpha"].default_value = alpha
    if alpha < 1.0 or alpha_tex:
        m.blend_method = "BLEND"
    _mats[name] = m
    return m


def flat_mat(name, color, rough=0.5, metal=0.0, emission=0.0, coat=0.0, alpha=1.0):
    return tmat(name, color=color, rough=rough, metal=metal, emission=emission, emit_color=color if emission else None,
                coat=coat, alpha=alpha)


def energy_mat(name, color, strength=3.0, alpha=1.0):
    return tmat(name, color=color, emission=strength, emit_color=color, rough=0.3, alpha=alpha)


# ------------------------------------------------------------------------------------------ geometry helpers

def assign(ob, m):
    ob.data.materials.clear()
    ob.data.materials.append(m)
    return ob


def bevel(ob, w, segs=2, angle=35):
    mod = ob.modifiers.new("Bevel", "BEVEL")
    mod.width = w
    mod.segments = segs
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(angle)
    C.apply_modifiers(ob)
    return ob


def subsurf(ob, levels=2):
    return C.add_subsurf(ob, levels)


def solidify(ob, thick, offset=0.0):
    mod = ob.modifiers.new("Solid", "SOLIDIFY")
    mod.thickness = thick
    mod.offset = offset
    C.apply_modifiers(ob)
    return ob


def displace(ob, amount, seed=1, scale=4.0):
    """Push vertices along their normals by a sum of 3D sine waves (lumpy organic shapes)."""
    rnd = np.random.RandomState(seed)
    ph = rnd.rand(3, 3) * 10
    me = ob.data
    for v in me.vertices:
        p = v.co * scale
        n = sum(math.sin(p.x * (1 + k) + ph[k, 0]) * math.sin(p.y * (1.3 + k) + ph[k, 1]) * math.sin(p.z * (0.9 + k) + ph[k, 2])
                / (1 + k) for k in range(3))
        v.co += v.normal * n * amount
    me.update()
    return ob


def xform(ob, m):
    ob.data.transform(m)
    ob.data.update()
    return ob


def xform_all(objs, m):
    for o in objs:
        xform(o, m)
    return objs


def rot(deg, axis):
    return Matrix.Rotation(math.radians(deg), 4, axis)


def T(x, y, z):
    return Matrix.Translation((x, y, z))


def S(x, y=None, z=None):
    y = x if y is None else y
    z = x if z is None else z
    return Matrix.Diagonal((x, y, z, 1))


def surface(name, nu, nv, fn, m, closed_u=False, smooth=True):
    """Parametric surface fn(u, v) -> (x, y, z) for u, v in [0, 1], with UVs."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    cols = nu if closed_u else nu + 1
    verts = [[bm.verts.new(fn(i / nu, j / nv)) for j in range(nv + 1)] for i in range(cols)]
    for i in range(nu):
        i2 = (i + 1) % cols if closed_u else i + 1
        for j in range(nv):
            f = bm.faces.new((verts[i][j], verts[i2][j], verts[i2][j + 1], verts[i][j + 1]))
            for lp, (a, b) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                lp[uv].uv = (a / nu, b / nv)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(m)
    if smooth:
        me.shade_smooth()
    ob = bpy.data.objects.new(name, me)
    return C._link(ob)


def tube(name, pts, r, m, segs=10, caps=True, rfn=None, smooth=True):
    """Sweep a circle along a polyline (parallel-transport frames). rfn(t) scales the radius along the path."""
    pts = [V(p) for p in pts]
    n = len(pts)
    tang = []
    for i in range(n):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, n - 1)]
        tang.append((b - a).normalized())
    up = V((0, 0, 1)) if abs(tang[0].z) < 0.9 else V((1, 0, 0))
    nrm = tang[0].cross(up).normalized()
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for i in range(n):
        if i > 0:
            q = tang[i - 1].rotation_difference(tang[i])
            nrm = (q @ nrm).normalized()
        bin_ = tang[i].cross(nrm).normalized()
        rr = r * (rfn(i / (n - 1)) if rfn else 1.0)
        rings.append([bm.verts.new(pts[i] + (nrm * math.cos(2 * math.pi * k / segs) + bin_ * math.sin(2 * math.pi * k / segs)) * rr)
                      for k in range(segs)])
    for i in range(n - 1):
        for k in range(segs):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % segs], rings[i + 1][(k + 1) % segs], rings[i + 1][k]))
            for lp, (a, b) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))):
                lp[uvl].uv = (a / segs, b / (n - 1))
    if caps:
        for ring in (rings[0], rings[-1]):
            try:
                bm.faces.new(ring)
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(m)
    if smooth:
        me.shade_smooth()
    return C._link(bpy.data.objects.new(name, me))


def helix(r, z0, z1, turns, steps_per_turn=24, x=0.0, y=0.0, rfn=None):
    n = int(turns * steps_per_turn) + 1
    pts = []
    for i in range(n):
        t = i / (n - 1)
        a = t * turns * 2 * math.pi
        rr = r * (rfn(t) if rfn else 1)
        pts.append((x + rr * math.cos(a), y + rr * math.sin(a), z0 + (z1 - z0) * t))
    return pts


def disc_decal(name, center, radius, m, normal=(0, -1, 0), segs=32):
    """Flat disc with planar UVs (0..1 across the diameter), facing normal."""
    def fn(u, v):
        a = u * 2 * math.pi
        return (math.cos(a) * radius * v, math.sin(a) * radius * v, 0)
    ob = surface(name, segs, 4, fn, m, closed_u=True)
    me = ob.data
    uvl = me.uv_layers.active
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl.data[li].uv = (0.5 + co.x / (2 * radius), 0.5 + co.y / (2 * radius))
    xform(ob, T(*center) @ C.look_matrix(V(normal)))
    return ob


# ------------------------------------------------------------------------------------------ shared materials

def M_rice():
    return tmat("rice", "rice", scale=2.2, rough=0.6, bump=0.6, sss=0.15)


def M_nori():
    return tmat("nori", "nori", scale=1.5, rough=0.45, bump=0.3, spec=0.6)


def M_steel(name="steel", tint=None, rough=0.32):
    return tmat(name, "steel", scale=1.2, metal=0.85, rough=rough, bump=0.12, tint=tint)


def M_dark_steel():
    return tmat("dark_steel", ("steel_dark", X.steel, (("#24282f", "#3a4048", "#555d68", "#717a86"), 132)), scale=1.4,
                metal=0.8, rough=0.38, bump=0.15)


def M_paint(name, hexcol, seed, chips=0.35, dark=None, scale=1.2):
    return tmat(name, (name, X.painted_metal, (hexcol, seed, chips, dark)), scale=scale, metal=0.25, rough=0.42,
                bump=0.2, coat=0.3)


def M_rubber(name="rubber", hexcol="#2c2f36"):
    return tmat(name, (name, X.rubber, (hexcol,)), scale=2.5, rough=0.75, bump=0.35)


def M_wood(name="wood", cols=("#7a4a22", "#a0682e", "#c4884a", "#d8a464"), scale=1.0, rot=(0, 0, 0)):
    return tmat(name, (name, X.wood, (cols,)), scale=scale, rot=rot, rough=0.6, bump=0.3)


def M_fabric(name, hexcol, scale=1.5, seed=151):
    return tmat(name, (name, X.fabric, (hexcol, seed)), scale=scale, rough=0.85, bump=0.35, sheen=0.4)


# ------------------------------------------------------------------------------------------ models

def grains(prefix, count, seed, center, half, m, size=0.045, top_only=False):
    """Loose rice grains stuck on a rounded block (silhouette detail)."""
    rnd = random.Random(seed)
    out = []
    hx, hy, hz = half
    for i in range(count):
        side = rnd.choice(("x", "y", "z")) if not top_only else "z"
        p = [rnd.uniform(-hx, hx), rnd.uniform(-hy, hy), rnd.uniform(-hz, hz)]
        if side == "x":
            p[0] = hx * rnd.choice((-1, 1)) * 0.98
        elif side == "y":
            p[1] = -hy * 0.98
        else:
            p[2] = hz * 0.98
        g = C.sphere(prefix + "%d" % i, (0, 0, 0), (size, size * 0.48, size * 0.48), m, 8, 5)
        xform(g, T(center[0] + p[0], center[1] + p[1], center[2] + p[2]) @ rot(rnd.uniform(0, 180), "Z")
              @ rot(rnd.uniform(-40, 40), "Y"))
        out.append(g)
    return out


def geta(n):
    """Little wooden sushi board with two feet."""
    wm = M_wood("geta_wood", ("#8a5a2e", "#b07a42", "#cc9a5a", "#e2b878"), scale=1.4)
    top = bevel(C.box(n + "_board", (0, 0, -0.07), (1.35, 0.78, 0.09), wm), 0.03, 2)
    feet = [bevel(C.box(n + "_foot%d" % s, (0.42 * s, 0, -0.19), (0.12, 0.74, 0.16), wm), 0.02, 2) for s in (-1, 1)]
    return [top] + feet


def salmon_sushi(n):
    rice = C.box(n + "_rice", (0, 0, 0.13), (0.92, 0.5, 0.3), M_rice())
    bevel(rice, 0.13, 4, 30)
    subsurf(rice, 1)
    displace(rice, 0.012, 3, 14)
    o = [rice] + grains(n + "_g", 34, 4, (0, 0, 0.13), (0.42, 0.22, 0.12), M_rice())
    fish_m = tmat("salmon", "salmon", scale=(1.6, 1.6, 1.6), rough=0.28, bump=0.25, coat=0.6, sss=0.2)

    def slab(u, v):
        x = -0.6 + 1.2 * u
        y = -0.31 + 0.62 * v
        z = 0.31 - 0.22 * (abs(x) / 0.6) ** 2.4 + 0.02 * math.sin(v * math.pi)
        return (x, y * (1 - 0.15 * (abs(x) / 0.6) ** 2), z)
    fish = surface(n + "_fish", 28, 10, slab, fish_m)
    solidify(fish, 0.075, 1)
    bevel(fish, 0.02, 2, 30)
    subsurf(fish, 1)
    o.append(fish)
    chive = tmat("chive", ("chive", X.fabric, ("#3fa034", 77, 40)), scale=4, rough=0.4, bump=0.1, coat=0.3)
    rnd = random.Random(9)
    for i in range(5):
        x = -0.15 + i * 0.07 + rnd.uniform(-0.02, 0.02)
        c = C.cyl(n + "_ch%d" % i, (x, -0.06 + rnd.uniform(-0.05, 0.05), 0.405), (x + 0.03, 0.02, 0.41), 0.022, 0.022, chive, 8)
        o.append(xform(c, T(0, 0, 0)))
    for i in range(10):
        s = C.sphere(n + "_ses%d" % i, (rnd.uniform(-0.35, 0.35), rnd.uniform(-0.2, 0.2), 0.4 - 0.0), (0.018, 0.011, 0.008),
                     flat_mat("sesame", "#f6ecd0", 0.5), 6, 4)
        o.append(s)
    o += geta(n)
    return o


def maki(n, outer, inner_ring, filling, top):
    """Thick maki/uramaki roll seen from the cut face (axis towards the camera)."""
    o = []
    R0, L = 0.46, 0.52
    out = C.cyl(n + "_out", (0, L / 2, 0), (0, -L / 2, 0), R0, R0, outer, 40)
    bevel(out, 0.05, 3, 30)
    o.append(out)
    if inner_ring is not None:
        o.append(C.cyl(n + "_ring", (0, -L / 2 + 0.01, 0), (0, -L / 2 - 0.012, 0), R0 - 0.035, R0 - 0.035, inner_ring, 40))
    rice = C.cyl(n + "_rice", (0, -L / 2 + 0.02, 0), (0, -L / 2 - 0.03, 0), R0 - 0.06, R0 - 0.06, M_rice(), 40)
    bevel(rice, 0.02, 2)
    o.append(rice)
    o += grains(n + "_g", 26, 21, (0, -L / 2 - 0.03, 0), (0.33, 0.0, 0.33), M_rice(), 0.04)
    o += filling(n, -L / 2 - 0.035)
    o += top(n, R0)
    return o


def _maki_filling_spicy(n, yf):
    tuna = tmat("tuna", "tuna", scale=2.2, rough=0.3, bump=0.2, coat=0.5, sss=0.2)
    cuc = tmat("cucumber", ("cucumber", X.fabric, ("#58b03a", 78, 30)), scale=3, rough=0.35, bump=0.15, coat=0.4)
    avo = tmat("avocado", ("avocado", X.mushroom_stem, ()), scale=2, rough=0.4, bump=0.1, tint="#c8e070")
    o = [bevel(C.box(n + "_tuna", (-0.03, yf, 0.0), (0.26, 0.06, 0.22), tuna), 0.03, 2)]
    o.append(bevel(C.box(n + "_cuc", (0.16, yf, 0.12), (0.11, 0.06, 0.1), cuc), 0.02, 2))
    o.append(bevel(C.box(n + "_avo", (0.11, yf, -0.15), (0.2, 0.06, 0.09), avo), 0.02, 2))
    o.append(bevel(C.box(n + "_cuc2", (-0.19, yf, 0.14), (0.1, 0.06, 0.08), cuc), 0.02, 2))
    return o


def _maki_top_spicy(n, R0):
    mayo = tmat("mayo", color="#ffa45a", rough=0.25, coat=0.6, sss=0.3)
    pts = []
    for i in range(60):
        t = i / 59
        x = -0.36 + 0.72 * t
        y = -0.2 + 0.4 * (0.5 + 0.5 * math.sin(t * math.pi * 5))
        pts.append((x, y, math.sqrt(max(R0 * R0 - x * x, 0)) + 0.012))
    o = [tube(n + "_mayo", pts, 0.022, mayo, 10)]
    chili = tmat("chili_skin", ("chili", X.tuna, ()), scale=3, rough=0.2, coat=0.8, tint="#ff6a50")
    seeds = flat_mat("chili_seed", "#ffe7a0")
    for i, (x, y) in enumerate(((-0.18, 0.05), (0.08, -0.12), (0.26, 0.1))):
        z = math.sqrt(max(R0 * R0 - x * x, 0)) + 0.05
        ring = C.torus(n + "_chr%d" % i, (x, y, z - 0.02), 0.055, 0.016, chili, 18, 8, axis=(x, 0, z))
        o.append(ring)
        o.append(C.sphere(n + "_chs%d" % i, (x, y, z - 0.02), (0.03, 0.03, 0.03), seeds, 8, 4))
    return o


def spicy_sushi(n):
    roe = tmat("tobiko", "tobiko", scale=2.4, rough=0.18, bump=0.5, coat=0.8)
    return maki(n, roe, None, _maki_filling_spicy, _maki_top_spicy)


def _maki_filling_wasabi(n, yf):
    tuna = tmat("tuna", "tuna", scale=2.2, rough=0.3, bump=0.2, coat=0.5, sss=0.2)
    cuc = tmat("cucumber", ("cucumber", X.fabric, ("#58b03a", 78, 30)), scale=3, rough=0.35, bump=0.15, coat=0.4)
    o = [bevel(C.box(n + "_tuna", (0.0, yf, 0.0), (0.24, 0.06, 0.24), tuna), 0.035, 2)]
    for i, a in enumerate((40, 160, 280)):
        x, z = 0.21 * math.cos(math.radians(a)), 0.21 * math.sin(math.radians(a))
        o.append(bevel(C.box(n + "_cuc%d" % i, (x, yf, z), (0.1, 0.06, 0.1), cuc), 0.02, 2))
    return o


def _maki_top_wasabi(n, R0):
    wm = tmat("wasabi", ("wasabi", X.mushroom_cap, ()), scale=3, rough=0.55, bump=0.6, tint="#a8e070", sss=0.2)
    blob = C.lathe(n + "_wasabi", [(0.0, 0.0), (0.2, 0.0), (0.21, 0.05), (0.17, 0.12), (0.1, 0.18), (0.03, 0.24), (0.0, 0.26)], wm,
                   segs=24)
    subsurf(blob, 2)
    displace(blob, 0.02, 7, 12)
    xform(blob, T(0.05, 0.04, R0 - 0.02) @ rot(15, "Y"))
    leaf_m = tmat("shiso", ("shiso", X.fabric, ("#3a9a2a", 79, 24)), scale=2, rough=0.4, bump=0.2, coat=0.4)

    def leaf(u, v):
        x = -0.32 + 0.64 * u
        w = 0.17 * math.sin(math.pi * u) ** 0.8
        y = (v - 0.5) * 2 * w
        return (x, y, 0.04 * (1 - (2 * v - 1) ** 2) + 0.06 * u * u)
    lf = surface(n + "_leaf", 16, 6, leaf, leaf_m)
    solidify(lf, 0.012)
    xform(lf, T(-0.1, 0.14, R0 - 0.04) @ rot(-35, "Z") @ rot(-12, "X"))
    return [blob, lf]


def wasabi_sushi(n):
    return maki(n, M_nori(), None, _maki_filling_wasabi, _maki_top_wasabi)


def shield(n):
    pl = tmat("shield_planks", "planks", proj="FLAT", scale=(0.85, 0.85, 0.85), loc=(0.5, 0.5, 0), rot=(math.pi / 2, 0, 0),
              rough=0.6, bump=0.5)
    R0 = 0.62
    disc = C.cyl(n + "_wood", (0, 0.05, 0), (0, -0.05, 0), R0, R0, pl, 48)
    bevel(disc, 0.025, 2)
    o = [disc]
    rim_m = M_steel("shield_rim", rough=0.3)
    o.append(C.torus(n + "_rim", (0, 0, 0), R0, 0.055, rim_m, 64, 12, axis=(0, 1, 0), scale=(1, 1, 1.35)))
    boss = C.lathe(n + "_boss", [(0.22, 0.0), (0.22, 0.03), (0.19, 0.06), (0.15, 0.13), (0.08, 0.18), (0.0, 0.19)], rim_m, 32)
    xform(boss, T(0, -0.05, 0) @ rot(90, "X"))
    o.append(boss)
    o.append(C.torus(n + "_bossr", (0, -0.055, 0), 0.215, 0.022, M_dark_steel(), 32, 8, axis=(0, 1, 0)))
    rivet = M_dark_steel()
    for i in range(16):
        a = 2 * math.pi * i / 16
        o.append(C.sphere(n + "_rv%d" % i, (math.cos(a) * (R0 - 0.08), -0.055, math.sin(a) * (R0 - 0.08)), (0.03, 0.02, 0.03), rivet,
                          10, 6))
    band = M_dark_steel()
    for z in (0.34, -0.34):
        w = math.sqrt(R0 * R0 - z * z) * 2 - 0.1
        b = bevel(C.box(n + "_band%d" % (z > 0), (0, -0.06, z), (w, 0.03, 0.09), band), 0.01, 2)
        o.append(b)
        for sx in (-1, 1):
            for k in (0.35, 0.8):
                o.append(C.sphere(n + "_bn%d%d%d" % (z > 0, sx, int(k * 10)), (sx * w / 2 * k, -0.08, z), (0.022, 0.015, 0.022), rim_m,
                                  8, 5))
    return xform_all(o, rot(-18, "Z") @ rot(8, "X"))


def umbrella(n):
    pink = M_fabric("umb_pink", "#e84ab8", 2.0, 152)
    pink2 = M_fabric("umb_pink2", "#ff7ad2", 2.0, 153)
    ribs = 8
    Rr, H = 0.85, 0.48

    def canopy(u, v):
        a = u * 2 * math.pi
        k = abs(math.sin(a * ribs / 2))           # 0 on ribs, 1 mid-panel
        r = Rr * v * (1 - 0.07 * k * v)
        z = H * (1 - v ** 1.7) - 0.06 * k * v ** 2 + 0.05 * (1 - k) * v * 0
        return (r * math.cos(a), r * math.sin(a), z)
    cp = surface(n + "_canopy", ribs * 8, 14, canopy, pink, closed_u=True)
    cp.data.materials.append(pink2)
    for p in cp.data.polygons:
        a = math.atan2(p.center.y, p.center.x) % (2 * math.pi)
        p.material_index = int(a / (2 * math.pi / ribs)) % 2
    solidify(cp, 0.012)
    o = [cp]
    rib_m = M_dark_steel()
    for i in range(ribs):
        a = 2 * math.pi * i / ribs
        pts = [canopy(i / ribs, t / 10) for t in range(1, 11)]
        pts = [(x, y, z - 0.012) for (x, y, z) in pts]
        o.append(tube(n + "_rib%d" % i, pts, 0.011, rib_m, 6))
        tip = canopy(i / ribs, 1.0)
        o.append(C.sphere(n + "_tip%d" % i, (tip[0] * 1.01, tip[1] * 1.01, tip[2] - 0.01), 0.025, M_wood("umb_tipw"), 8, 5))
    hem = M_fabric("umb_hem", "#8a1060", 3.0, 154)
    o.append(tube(n + "_hem", [canopy(t / 160, 1.0) for t in range(161)], 0.03, hem, 6, caps=False))
    o.append(tube(n + "_hem2", [tuple(c * 0.98 for c in canopy(t / 160, 0.93)[:2]) + (canopy(t / 160, 0.93)[2] + 0.004,)
                               for t in range(161)], 0.009, hem, 6, caps=False))
    # tie strap with a snap button hanging from one panel
    strap = M_fabric("umb_strap", "#c42a90", 3.0, 159)
    sp = [canopy(0.06, 0.98), (0.55, -0.42, 0.0), (0.42, -0.36, -0.12)]
    o.append(tube(n + "_strap", sp, 0.022, strap, 8))
    o.append(C.sphere(n + "_snap", (0.42, -0.38, -0.12), 0.03, M_steel("umb_snap", rough=0.2), 10, 6))
    shaft = M_steel("umb_shaft")
    o.append(C.cyl(n + "_shaft", (0, 0, -0.9), (0, 0, H + 0.14), 0.022, 0.022, shaft, 12))
    o.append(C.cyl(n + "_ferr", (0, 0, H), (0, 0, H + 0.2), 0.035, 0.008, M_wood("umb_tipw"), 12))
    o.append(C.cyl(n + "_runner", (0, 0, 0.0), (0, 0, 0.1), 0.04, 0.04, rib_m, 12))
    handle = M_wood("umb_handle", ("#5a2e12", "#7a4220", "#9a5a2e", "#b8743c"), 3.0)
    pts = [(0, 0, -0.9)] + [(0.11 - 0.11 * math.cos(t), 0, -0.9 - 0.11 * math.sin(t)) for t in np.linspace(0.15, math.pi, 12)]
    pts = [(0, 0, -0.75)] + pts
    o.append(tube(n + "_handle", pts, 0.042, handle, 12))
    return xform_all(o, rot(22, "Y") @ rot(-10, "X"))


def scroll(n):
    sheet = X.get_rgba("scroll_sheet", X.scroll_sheet)
    pm = tmat("scroll_page", rgba=sheet, coords="UV", rough=0.7, alpha_tex=True)
    W, Hh = 1.05, 0.66

    def page(u, v):
        x = (u - 0.5) * W
        z = (v - 0.5) * Hh
        y = 0.03 * math.sin(u * math.pi * 2.0) + 0.02 * math.sin(v * math.pi * 3)
        return (x, y, z)
    pg = surface(n + "_page", 24, 14, page, pm)
    solidify(pg, 0.006)
    o = [pg]
    rollm = tmat("scroll_roll", "parchment", scale=2.5, rough=0.7, bump=0.3)
    rod = M_wood("scroll_rod", ("#4a2410", "#6a3818", "#8a4c24", "#a8622e"), 3.0)
    for z, r in ((Hh / 2 + 0.06, 0.075), (-Hh / 2 - 0.05, 0.065)):
        o.append(C.cyl(n + "_roll%d" % (z > 0), (-W / 2 - 0.02, 0, z), (W / 2 + 0.02, 0, z), r, r, rollm, 24))
        o.append(C.cyl(n + "_rod%d" % (z > 0), (-W / 2 - 0.12, 0, z), (W / 2 + 0.12, 0, z), 0.03, 0.03, rod, 12))
        for sx in (-1, 1):
            k = C.lathe(n + "_knob%d%d" % (z > 0, sx), [(0.0, 0.0), (0.05, 0.0), (0.06, 0.03), (0.045, 0.07), (0.0, 0.08)], rod, 16)
            xform(k, T(sx * (W / 2 + 0.1), 0, z) @ rot(90 * sx, "Y"))
            o.append(k)
    ribbon = tmat("scroll_ribbon", ("ribbon_red", X.fabric, ("#c8202a", 155, 70)), scale=3, rough=0.5, sheen=0.6)
    pts = [(W / 2 - 0.2 + 0.05 * math.sin(t * 6), -0.05 - 0.03 * t, -Hh / 2 - 0.05 - 0.3 * t) for t in np.linspace(0, 1, 14)]
    o.append(tube(n + "_rib1", pts, 0.025, ribbon, 8))
    seal = tmat("wax", ("wax", X.mushroom_cap, ()), scale=4, rough=0.3, bump=0.5, tint="#d02a20", coat=0.5)
    sl = C.cyl(n + "_seal", (W / 2 - 0.2, -0.02, -Hh / 2 + 0.02), (W / 2 - 0.2, -0.07, -Hh / 2 + 0.02), 0.09, 0.08, seal, 20)
    displace(sl, 0.01, 4, 20)
    o.append(sl)
    return xform_all(o, rot(-12, "Y") @ rot(-20, "Z"))


def pogo_stick(n):
    orange = M_paint("pogo_orange", "#ff6a1a", 301, 0.35, "#c84a10")
    o = []
    o.append(C.cyl(n + "_shaft", (0, 0, -0.3), (0, 0, 0.82), 0.075, 0.075, orange, 20))
    o.append(C.cyl(n + "_tube", (0, 0, -0.85), (0, 0, -0.3), 0.045, 0.045, M_steel("pogo_steel"), 16))
    o.append(C.cyl(n + "_bar", (-0.4, 0, 0.82), (0.4, 0, 0.82), 0.05, 0.05, orange, 16))
    o.append(bevel(C.box(n + "_tee", (0, 0, 0.82), (0.2, 0.16, 0.16), orange), 0.04, 2))
    o.append(bevel(C.box(n + "_label", (0, -0.075, 0.35), (0.07, 0.02, 0.3), M_paint("pogo_label", "#ffd23a", 302, 0.3)), 0.008, 1))
    grip = M_rubber("grip", "#202228")
    for s in (-1, 1):
        g = C.cyl(n + "_grip%d" % s, (0.22 * s, 0, 0.82), (0.52 * s, 0, 0.82), 0.08, 0.08, grip, 20)
        o.append(g)
        for k in range(5):
            o.append(C.torus(n + "_gr%d%d" % (s, k), (s * (0.26 + 0.055 * k), 0, 0.82), 0.081, 0.012, grip, 20, 6, axis=(1, 0, 0)))
        o.append(C.sphere(n + "_gend%d" % s, (0.52 * s, 0, 0.82), (0.035, 0.08, 0.08), grip, 14, 6))
    # foot pegs with rubber tread
    tread = tmat("tread", ("tread", X.hazard, (311, ("#2a2c32", "#3a3d45"), 14)), scale=2.5, rough=0.8, bump=0.6)
    for s in (-1, 1):
        o.append(bevel(C.box(n + "_peg%d" % s, (0.2 * s, 0, -0.32), (0.3, 0.2, 0.07), tread), 0.02, 2))
        o.append(C.cyl(n + "_pegarm%d" % s, (0, 0, -0.22), (0.12 * s, 0, -0.32), 0.035, 0.035, orange, 10))
    spring = M_steel("spring", rough=0.25)
    o.append(tube(n + "_spring", helix(0.12, -0.84, -0.4, 7, 24), 0.026, spring, 10))
    o.append(C.cyl(n + "_foot", (0, 0, -0.98), (0, 0, -0.84), 0.1, 0.08, M_rubber(), 20))
    o.append(C.torus(n + "_collar", (0, 0, -0.32), 0.085, 0.022, M_dark_steel(), 20, 6))
    return xform_all(o, rot(14, "Y"))


def bandage(n):
    fab = tmat("bandage", "bandage_fabric", scale=2.2, rough=0.85, bump=0.35, sheen=0.5)
    fab_tube = tmat("bandage_tube", "bandage_fabric", proj="TUBE", scale=(1, 1, 4), rough=0.85, bump=0.3, sheen=0.5)
    R0, Wd = 0.34, 0.42
    roll = C.cyl(n + "_roll", (0, -Wd / 2, 0), (0, Wd / 2, 0), R0, R0, fab, 40)
    bevel(roll, 0.02, 2)
    o = [roll]
    # spiral layers on the visible end
    layers = flat_mat("bandage_edge", "#c8a888", 0.9)
    pts = [(r * math.cos(a), -Wd / 2 - 0.004, r * math.sin(a)) for a, r in
           ((t * 2 * math.pi * 5, 0.1 + 0.24 * t) for t in np.linspace(0, 1, 160))]
    o.append(tube(n + "_spiral", pts, 0.006, layers, 6))
    o.append(C.cyl(n + "_core", (0, -Wd / 2 - 0.006, 0), (0, Wd / 2 + 0.006, 0), 0.1, 0.1, tmat("cardboard", ("cardboard", X.planks, (("#a07a4a", "#b88c58", "#c89c68", "#d8b07a"), 3)), scale=3, bump=0.2), 24))
    o.append(C.cyl(n + "_hole", (0, -Wd / 2 - 0.01, 0), (0, Wd / 2 + 0.01, 0), 0.075, 0.075, flat_mat("dark_hole", "#2a1e14"), 24))

    # unrolled tail strip leaving the roll at the bottom front and curling on the ground
    def strip(u, v):
        t = u
        x = 0.9 * t
        z = -R0 + 0.0 + 0.12 * math.sin(t * math.pi * 1.3) * (1 - t) - 0.02 * t
        y = (v - 0.5) * Wd * 0.98
        return (x - 0.02, y, z)
    st = surface(n + "_strip", 30, 6, strip, fab)
    solidify(st, 0.012)
    o.append(st)
    o.append(bevel(C.box(n + "_pad", (0.56, 0, -R0 + 0.06), (0.22, Wd * 0.62, 0.025), tmat("pad_fab", "bandage_fabric", scale=3, rough=0.9,
                                                                                                tint="#ffffff", bump=0.2)), 0.008, 2))
    clip = M_steel("clip")
    o.append(bevel(C.box(n + "_clip", (0.84, 0, -R0 + 0.035), (0.06, Wd * 0.7, 0.012), clip), 0.004, 1))
    return xform_all(o, rot(-28, "Z"))


def burrito(n):
    tort = tmat("tortilla", "tortilla", scale=1.6, rough=0.65, bump=0.5)
    L, R0 = 1.0, 0.27

    def body(u, v):
        x = -L / 2 + L * u
        a = v * 2 * math.pi
        rr = R0 * (1 - 0.65 * max(0, (-x - L / 2 + 0.22) / 0.22) ** 2)
        fold = 0.015 * math.sin(a * 3 + u * 9)
        return (x, (rr + fold) * math.cos(a), (rr + fold) * math.sin(a))
    b = surface(n + "_body", 30, 32, body, tort)
    solidify(b, 0.02)
    o = [b]
    foil = tmat("foil_silver", ("foil_silver", X.foil, ("#b8c0cc", 182)), scale=2.0, metal=0.9, rough=0.22, bump=0.6)

    def wrap(u, v):
        x = -L / 2 - 0.02 + 0.45 * u
        a = v * 2 * math.pi
        rr = R0 * (1 - 0.65 * max(0, (-x - L / 2 + 0.22) / 0.22) ** 2) + 0.025
        edge = 0.04 * math.sin(a * 7) * u ** 4
        return (x + edge, rr * math.cos(a), rr * math.sin(a))
    o.append(solidify(surface(n + "_foil", 18, 40, wrap, foil), 0.008))
    # filling at the open end
    fx = L / 2 - 0.01
    rice = M_rice()
    beans = tmat("beans", ("beans", X.chocolate, ()), scale=4, rough=0.3, coat=0.5, tint="#a04a3a")
    lettuce = tmat("lettuce", ("lettuce", X.mushroom_cap, ()), scale=3, rough=0.45, tint="#80d050", bump=0.4)
    tomato = tmat("tomato", "tuna", scale=4, rough=0.25, coat=0.6, tint="#ff6a50")
    cheese = tmat("cheese", color="#ffc83a", rough=0.4, sss=0.2)
    o.append(C.sphere(n + "_fill", (fx, 0, 0), (0.05, R0 * 0.92, R0 * 0.92), rice, 20, 10))
    rnd = random.Random(3)
    for i in range(22):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0, R0 * 0.8)
        p = (fx + 0.04 + rnd.uniform(0, 0.03), r * math.cos(a), r * math.sin(a))
        kind = i % 4
        if kind == 0:
            o.append(C.sphere(n + "_bn%d" % i, p, (0.04, 0.03, 0.025), beans, 10, 6))
        elif kind == 1:
            o.append(bevel(C.box(n + "_tm%d" % i, p, (0.05, 0.05, 0.05), tomato), 0.01, 1))
        elif kind == 2:
            l = C.ico(n + "_lt%d" % i, p, 0.06, lettuce, 1, True, (0.6, 1, 0.5), seed=i, jitter=0.3)
            o.append(l)
        else:
            o.append(C.cyl(n + "_ch%d" % i, p, (p[0] + 0.02, p[1] + 0.07, p[2] + 0.02), 0.012, 0.012, cheese, 6))
    # stink: wobbly translucent green wisps (the original burrito icon reeks)
    stink = tmat("stink", color="#7ae84a", emission=0.6, emit_color="#7ae84a", alpha=0.55, rough=0.5)
    for k, (x0, ph) in enumerate(((-0.15, 0.0), (0.12, 1.5), (0.35, 3.0))):
        pts = [(x0 + 0.07 * math.sin(t * 9 + ph), 0.02 * math.cos(t * 5), R0 + 0.06 + 0.5 * t) for t in np.linspace(0, 1, 30)]
        o.append(tube(n + "_stink%d" % k, pts, 0.04, stink, 10, rfn=lambda t: 1.0 - 0.75 * t))
    return xform_all(o, rot(-20, "Z") @ rot(-14, "Y"))


def kamikaze(n):
    lea = tmat("leather", "leather", scale=2.0, rough=0.55, bump=0.6, coat=0.15)
    o = []
    # aviator cap: hemisphere + ear flaps
    cap = C.sphere(n + "_cap", (0, 0, 0), (0.48, 0.5, 0.46), lea, 40, 20)
    me = cap.data
    for v in me.vertices:
        if v.co.z < -0.02:
            v.co.z = -0.02 + (v.co.z + 0.02) * 0.15
    me.update()
    o.append(cap)
    seam = flat_mat("stitch", "#e8d4a8", 0.8)
    # raised seams running front-to-back over the crown
    for k, a in enumerate((-0.32, 0.32)):
        pts = []
        for t in np.linspace(0.15, math.pi - 0.15, 40):
            x = math.sin(a) * math.sin(t)
            y = -math.cos(t)
            z = math.cos(a) * math.sin(t)
            pts.append((x * 0.485, y * 0.505, z * 0.465))
        o.append(tube(n + "_seam%d" % k, pts, 0.014, lea, 6))
    for s in (-1, 1):
        flap = C.sphere(n + "_flap%d" % s, (0.44 * s, 0.02, -0.18), (0.08, 0.17, 0.24), lea, 16, 10)
        o.append(flap)
        o.append(C.torus(n + "_flapr%d" % s, (0.45 * s, 0.02, -0.18), 0.12, 0.012, seam, 20, 5, axis=(1, 0, 0), scale=(1.3, 1.85, 1)))
        o.append(tube(n + "_strap%d" % s, [(0.45 * s, 0.0, -0.4), (0.3 * s, -0.12, -0.52), (0.12 * s, -0.2, -0.56)], 0.022,
                      M_rubber("strap", "#4a2a14"), 8))
    o.append(bevel(C.box(n + "_buckle", (0.0, -0.22, -0.56), (0.12, 0.04, 0.08), M_steel("buckle")), 0.01, 1))
    # goggles on the forehead
    frame = M_steel("goggle_frame", rough=0.25)
    glass = tmat("goggle_glass", color="#5ac8ff", rough=0.05, metal=0.2, coat=1.0, emission=0.15, emit_color="#8adfff")
    for s in (-1, 1):
        cx, cz = 0.17 * s, 0.17
        pos = V((cx, -0.43, cz))
        nrm = V((0.25 * s, -1, 0.35)).normalized()
        ring = C.torus(n + "_gring%d" % s, (0, 0, 0), 0.12, 0.03, frame, 28, 10)
        xform(ring, T(*pos) @ C.look_matrix(nrm))
        lens = C.sphere(n + "_lens%d" % s, (0, 0, 0), (0.115, 0.115, 0.04), glass, 24, 10)
        xform(lens, T(*(pos + nrm * 0.01)) @ C.look_matrix(nrm))
        o += [ring, lens]
    o.append(tube(n + "_bridge", [(-0.06, -0.47, 0.19), (0, -0.49, 0.2), (0.06, -0.47, 0.19)], 0.02, frame, 8))
    # hachimaki headband with the rising sun across the front, knotted at the back with flying tails
    sun = X.get_rgba("hachimaki", X.rising_sun)
    hm = tmat("hachimaki", rgba=sun, coords="UV", rough=0.8, sheen=0.5)

    def hb(u, v):
        a = -math.pi / 2 + (u - 0.5) * 2 * math.pi * 0.98
        z = 0.0 + (v - 0.5) * 0.13
        rr = 0.5 * math.sqrt(max(0.0, 1 - (z / 0.48) ** 2)) + 0.025
        return (rr * math.cos(a) * 0.98, rr * math.sin(a) * 1.02, z)
    o.append(solidify(surface(n + "_band", 64, 4, hb, hm), 0.01))
    cloth = M_fabric("hachi_cloth", "#f6f3ee", 3.0, 156)
    for k, s in enumerate((-1, 1)):
        def tail(u, v, s=s):
            x = 0.06 * s + 0.35 * u * s
            y = 0.5 + 0.25 * u
            z = -0.02 - 0.25 * u + 0.05 * math.sin(u * 7 + s)
            return (x + (v - 0.5) * 0.02, y, z + (v - 0.5) * 0.11)
        o.append(solidify(surface(n + "_tail%d" % k, 14, 3, tail, cloth), 0.01))
    o.append(C.sphere(n + "_knot", (0, 0.52, 0.0), (0.07, 0.05, 0.07), cloth, 12, 8))
    return xform_all(o, rot(18, "Z") @ rot(-8, "X"))


def caltrop(prefix, m, size=0.22):
    """Forged caltrop: four tapered square-section spikes welded at a knobbly core."""
    o = [C.ico(prefix + "_c", (0, 0, 0), size * 0.24, m, 2, True, seed=1, jitter=0.08)]
    for i, d in enumerate(((1, 1, 1), (-1, -1, 1), (-1, 1, -1), (1, -1, -1))):
        d = V(d).normalized()
        sp = C.cyl(prefix + "_s%d" % i, d * size * 0.05, d * size, size * 0.3, size * 0.02, m, 8, smooth=True)
        o.append(sp)
        o.append(C.torus(prefix + "_w%d" % i, d * size * 0.2, size * 0.2, size * 0.045, m, 12, 6, axis=d))
    return o


def caltrops(n):
    m = tmat("caltrop_iron", ("iron", X.painted_metal, ("#727a86", 401, 0.6, "#7a4a28")), scale=3, metal=0.55, rough=0.4,
             bump=0.6)
    o = []
    rnd = random.Random(5)
    for i, (x, y, z, r) in enumerate(((0.0, 0.0, 0.0, 0.45), (0.5, 0.3, -0.05, 0.38), (0.36, -0.42, -0.06, 0.36),
                                     (-0.42, -0.3, -0.06, 0.34))):
        parts = caltrop(n + "_%d" % i, m, r)
        up = V((1, 1, 1)).normalized().rotation_difference(V((0, 0, 1))).to_matrix().to_4x4()
        rm = T(x, y, z) @ rot(rnd.uniform(0, 360), "Z") @ rot(rnd.uniform(-12, 12), "X") @ up
        o += xform_all(parts, rm)
    return o


def mine_base(n, paint, hz=True):
    o = []
    body = C.lathe(n + "_body", [(0.0, -0.04), (0.5, -0.04), (0.55, 0.0), (0.56, 0.16), (0.53, 0.21), (0.44, 0.25), (0.0, 0.25)], paint, 48)
    o.append(body)
    if hz:
        hzm = tmat("hazard", "hazard", proj="TUBE", scale=(1, 1, 2.8), rough=0.45, bump=0.2, coat=0.3)
        o.append(bevel(C.cyl(n + "_hz", (0, 0, 0.03), (0, 0, 0.14), 0.575, 0.575, hzm, 48), 0.01, 1))
    ribs = M_dark_steel()
    for i in range(12):
        a = 2 * math.pi * i / 12 + 0.13
        o.append(bevel(C.box(n + "_rib%d" % i, (0.58 * math.cos(a), 0.58 * math.sin(a), 0.085), (0.06, 0.04, 0.16), ribs,
                             rot=rot(math.degrees(a), "Z")), 0.01, 1))
    plate = M_steel("mine_plate", rough=0.35)
    o.append(bevel(C.cyl(n + "_plate", (0, 0, 0.24), (0, 0, 0.29), 0.33, 0.3, plate, 40), 0.012, 2))
    for i in range(8):
        a = 2 * math.pi * i / 8
        o.append(C.sphere(n + "_bolt%d" % i, (0.4 * math.cos(a), 0.4 * math.sin(a), 0.25), (0.028, 0.028, 0.02), ribs, 10, 5))
    return o


def mine(n):
    paint = M_paint("mine_paint", "#6a7458", 501, 0.4, "#4a5238")
    o = mine_base(n, paint)
    red = tmat("button_red", color="#e81e24", rough=0.12, coat=1.0, emission=0.25, emit_color="#ff2a20")
    o.append(C.cyl(n + "_bring", (0, 0, 0.29), (0, 0, 0.32), 0.17, 0.16, M_dark_steel(), 32))
    btn = C.lathe(n + "_btn", [(0.0, 0.31), (0.14, 0.31), (0.14, 0.36), (0.12, 0.4), (0.07, 0.425), (0.0, 0.43)], red, 32)
    o.append(btn)
    return xform_all(o, rot(8, "X"))


FLAME_OUTLINE = [(0.04, 1.62), (0.2, 1.18), (0.34, 1.02), (0.55, 1.24), (0.5, 0.82), (0.55, 0.45), (0.46, 0.13), (0.25, -0.04),
                 (0.0, -0.08), (-0.25, -0.04), (-0.46, 0.13), (-0.55, 0.45), (-0.47, 0.8), (-0.6, 1.08), (-0.32, 0.96),
                 (-0.18, 1.2)]


def catmull(pts, per=5):
    """Closed Catmull-Rom resample of a 2D outline."""
    out = []
    n = len(pts)
    for i in range(n):
        p0, p1, p2, p3 = (V(pts[(i + k) % n]) for k in (-1, 0, 1, 2))
        for j in range(per):
            t = j / per
            out.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                                    + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)))
    return out


def flame(n, h=0.6, r=0.17):
    """Cartoon fire like the original art: three nested puffy flame shapes (red-orange, orange, yellow), emissive."""
    out = []
    base = catmull(FLAME_OUTLINE, 5)
    layers = (("#ff4410", 2.6, 1.0, 0.0), ("#ff9418", 3.4, 0.68, -0.06), ("#fff07a", 5.0, 0.4, -0.11))
    for k, (c, e, sc, dy) in enumerate(layers):
        m = tmat("flame%d" % k, color=c, emission=e, emit_color=c, rough=0.5)
        pts = [(x * sc * h / 1.6 * 1.25, z * sc * h / 1.6) for (x, z) in base]
        depth = 0.16 * sc
        ob = C.extrude(n + "_fl%d" % k, pts, depth, m)
        bevel(ob, depth * 0.48, 4, 25)
        xform(ob, T(0, dy, 0))
        out.append(ob)
    return out


def flame_mine(n):
    paint = M_paint("fmine_paint", "#a83020", 511, 0.45, "#5a1a10")
    o = mine_base(n, paint)
    o.append(C.cyl(n + "_neck", (0, 0, 0.28), (0, 0, 0.35), 0.12, 0.1, M_dark_steel(), 24))
    noz = C.lathe(n + "_noz", [(0.1, 0.35), (0.09, 0.39), (0.06, 0.41), (0.08, 0.45), (0.065, 0.45), (0.0, 0.44)], M_steel("nozzle"), 24)
    o.append(noz)
    o += xform_all(flame(n, 0.72, 0.2), T(0, 0, 0.42) @ rot(-20, "Z"))
    # fuel canister strapped on the side
    can = M_paint("fuel_can", "#e8b81a", 512, 0.3, "#a87a10")
    o.append(bevel(C.cyl(n + "_can", (0.42, -0.25, 0.25), (0.42, -0.25, 0.53), 0.09, 0.09, can, 24), 0.015, 2))
    o.append(C.cyl(n + "_cancap", (0.42, -0.25, 0.53), (0.42, -0.25, 0.58), 0.04, 0.035, M_dark_steel(), 16))
    o.append(tube(n + "_hose", [(0.42, -0.25, 0.56), (0.35, -0.25, 0.64), (0.18, -0.15, 0.58), (0.1, -0.06, 0.39)], 0.018,
                  M_rubber(), 8))
    return xform_all(o, rot(8, "X"))


def spring_mine(n):
    paint = M_paint("smine_paint", "#2a6ad0", 521, 0.4, "#1a3a8a")
    o = mine_base(n, paint)
    o.append(tube(n + "_spring", helix(0.17, 0.29, 0.64, 6, 28, rfn=lambda t: 1 - 0.18 * math.sin(t * math.pi)), 0.025,
                  M_steel("spring", rough=0.25), 10))
    pad = tmat("pad_tread", ("pad_tread", X.hazard, (522, ("#d82a2a", "#a81818"), 10)), scale=2.5, rough=0.6, bump=0.4, coat=0.3)
    o.append(bevel(C.cyl(n + "_pad", (0, 0, 0.64), (0, 0, 0.72), 0.3, 0.3, pad, 40), 0.02, 2))
    o.append(bevel(C.cyl(n + "_padb", (0, 0, 0.62), (0, 0, 0.65), 0.2, 0.2, M_dark_steel(), 24), 0.008, 1))
    led = tmat("led_green", color="#6aff4a", emission=4.0, emit_color="#6aff4a", rough=0.2)
    o.append(C.sphere(n + "_led", (0.3, -0.4, 0.21), 0.04, led, 12, 6))
    o.append(C.sphere(n + "_led2", (-0.3, -0.4, 0.21), 0.04, led, 12, 6))
    return xform_all(o, rot(8, "X"))


def protein_bar(n):
    lab = X.get_rgba("wrapper", X.wrapper_label)
    wm = tmat("wrapper", rgba=lab, coords="UV", rough=0.25, metal=0.4, coat=0.5)
    L, Wd, Hh = 0.9, 0.42, 0.2

    def wrap(u, v):
        a = v * 2 * math.pi
        # rounded-rectangle cross section (superellipse)
        c, s = math.cos(a), math.sin(a)
        p = 0.25
        y = Wd / 2 * math.copysign(abs(c) ** p, c)
        z = Hh / 2 * math.copysign(abs(s) ** p, s)
        x = -L / 2 + (L - 0.12) * u
        return (x, y, z)
    body = surface(n + "_wrap", 24, 48, wrap, wm, closed_u=False)
    # UV: label runs along the front: remap v so the front face (-Y) is the label centre
    me = body.data
    uvl = me.uv_layers.active
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            vv = 0.5 + co.y / (Wd * 1.08) if co.z >= 0 else 0.5 - co.y / (Wd * 1.08)
            uvl.data[li].uv = ((co.x + L / 2) / (L - 0.12), vv)
    solidify(body, 0.006)
    o = [body]
    # crimped end (closed) on the left
    crimp_m = tmat("wrapper_crimp", ("foil_purple", X.foil, ("#5a1e98", 183)), scale=3, metal=0.5, rough=0.25, bump=0.5)

    def crimp(u, v):
        y = (u - 0.5) * Wd * 1.05
        x = -L / 2 - 0.12 * v
        z = 0.012 * math.sin(u * 40) + Hh / 2 * (1 - v) * 0.0
        return (x, y, z * (0.5 + v))
    o.append(solidify(surface(n + "_crimp", 40, 3, crimp, crimp_m), 0.01))
    # open end: torn foil petals and the chocolate bar sticking out, bitten
    choc = tmat("chocolate", "chocolate", scale=2.5, rough=0.4, bump=0.6, coat=0.2)
    bar = bevel(C.box(n + "_bar", (L / 2 - 0.02, 0, 0), (0.42, Wd * 0.86, Hh * 0.86), choc), 0.03, 3)
    o.append(bar)
    # bite: carve with a few spheres approximated by moving vertices
    for v in bar.data.vertices:
        d = (V((v.co.x, v.co.y, 0)) - V((L / 2 + 0.2, 0.1, 0))).length
        if d < 0.14:
            v.co.x -= (0.14 - d) * 0.9
    bar.data.update()
    nuts = tmat("nuts", ("nuts", X.tortilla, ()), scale=4, rough=0.5, bump=0.5, tint="#e0b070")
    rnd = random.Random(4)
    for i in range(8):
        o.append(C.ico(n + "_nut%d" % i, (L / 2 + rnd.uniform(-0.1, 0.12), rnd.uniform(-0.14, 0.14), Hh * 0.43 + 0.01), 0.03, nuts, 1,
                       True, (1.2, 1, 0.6), seed=i, jitter=0.25))
    for k in range(6):
        a = 2 * math.pi * k / 6

        def petal(u, v, a=a):
            ca, sa = math.cos(a), math.sin(a)
            rr = 1.0 + 0.25 * u
            x = L / 2 - 0.13 + 0.12 * u
            y = Wd / 2 * 1.02 * rr * (ca + (v - 0.5) * 0.9 * -sa)
            z = Hh / 2 * 1.05 * rr * (sa + (v - 0.5) * 0.9 * ca) * 1.4
            return (x, max(min(y, Wd), -Wd), z)
        o.append(solidify(surface(n + "_petal%d" % k, 4, 4, petal, crimp_m), 0.006))
    return xform_all(o, rot(24, "Z") @ rot(-12, "Y"))


def mushroom(n):
    capm = tmat("mush_cap", "mushroom_cap", scale=2.5, rough=0.35, bump=0.4, coat=0.5, sss=0.1)
    cap = C.lathe(n + "_cap", [(0.0, 0.28), (0.38, 0.26), (0.55, 0.32), (0.6, 0.4), (0.55, 0.55), (0.4, 0.7), (0.2, 0.78), (0.0, 0.8)],
                  capm, 48)
    subsurf(cap, 1)
    displace(cap, 0.025, 11, 5)
    o = [cap]
    gill = tmat("gills", ("gills", X.mushroom_stem, ()), proj="TUBE", scale=(1, 1, 1), rough=0.7, bump=0.4, tint="#f4e0c0")
    gl = C.lathe(n + "_gills", [(0.07, 0.24), (0.56, 0.33), (0.38, 0.27)], gill, 48)
    o.append(gl)
    for i in range(36):
        a = 2 * math.pi * i / 36
        o.append(tube(n + "_gl%d" % i, [(0.1 * math.cos(a), 0.1 * math.sin(a), 0.26), (0.52 * math.cos(a), 0.52 * math.sin(a), 0.32)],
                      0.008, gill, 4, smooth=False))
    stem_m = tmat("mush_stem", "mushroom_stem", proj="TUBE", scale=(1, 1, 3), rough=0.6, bump=0.4, sss=0.2)
    stem = C.lathe(n + "_stem", [(0.0, -0.42), (0.2, -0.42), (0.23, -0.36), (0.18, -0.2), (0.14, 0.0), (0.13, 0.18), (0.12, 0.3)], stem_m, 32)
    displace(stem, 0.01, 12, 8)
    o.append(stem)
    skirt = C.lathe(n + "_skirt", [(0.13, 0.12), (0.2, 0.08), (0.24, 0.0), (0.22, -0.03)], stem_m, 32)
    displace(skirt, 0.012, 13, 10)
    o.append(solidify(skirt, 0.01))
    wart = tmat("warts", ("warts", X.mushroom_stem, ()), scale=4, rough=0.5, bump=0.5, tint="#ffe84a", sss=0.2)
    rnd = random.Random(6)
    for i in range(16):
        a = rnd.uniform(0, 2 * math.pi)
        t = rnd.uniform(0.0, 0.85)
        # point on the cap profile
        r = 0.55 * (1 - t) ** 0.7 + 0.02
        z = 0.4 + 0.4 * t ** 0.9
        s = rnd.uniform(0.05, 0.1)
        w = C.ico(n + "_w%d" % i, (r * math.cos(a), r * math.sin(a), z), s, wart, 2, True, (1, 1, 0.55), seed=i, jitter=0.2)
        o.append(w)
    grass = tmat("moss", ("moss", X.mushroom_cap, ()), scale=3, rough=0.7, bump=0.5, tint="#6ac040")
    mound = C.sphere(n + "_moss", (0, 0, -0.45), (0.42, 0.42, 0.1), grass, 24, 10)
    displace(mound, 0.02, 3, 10)
    o.append(mound)
    o.append(C.lathe(n + "_baby", [(0.0, -0.37), (0.04, -0.37), (0.035, -0.25)], stem_m, 12))
    o.append(xform(C.lathe(n + "_babycap", [(0.0, -0.25), (0.12, -0.26), (0.1, -0.2), (0.0, -0.17)], capm, 20), T(0, 0, 0)))
    for ob in o[-2:]:
        xform(ob, T(0.32, -0.12, 0.0))
    return xform_all(o, rot(-10, "Y"))


def confetti(n):
    paper = tmat("popper", "stripes_rainbow", proj="TUBE", scale=(1, 1, 1.5), rough=0.35, bump=0.15, coat=0.5)
    cone = C.lathe(n + "_cone", [(0.0, -0.55), (0.08, -0.55), (0.38, 0.3), (0.38, 0.33)], paper, 40)
    o = [cone]
    gold = tmat("gold_foil", ("gold_foil", X.foil, ("#e8b02a", 184)), scale=3, metal=0.95, rough=0.2, bump=0.4)
    o.append(C.torus(n + "_rim", (0, 0, 0.33), 0.38, 0.04, gold, 48, 10))
    o.append(C.cyl(n + "_inside", (0, 0, 0.3), (0, 0, 0.32), 0.35, 0.35, flat_mat("popper_in", "#2a1a3a"), 32))
    o.append(C.cyl(n + "_base", (0, 0, -0.6), (0, 0, -0.5), 0.1, 0.08, gold, 16))
    o.append(tube(n + "_string", [(0, 0, -0.58), (0.03, 0, -0.68), (-0.02, 0, -0.75), (0.02, 0, -0.82)], 0.012,
                  tmat("string", ("rope", X.fabric, ("#d8c08a", 158, 30)), scale=6, rough=0.9), 6))
    o.append(C.torus(n + "_pull", (0.02, 0, -0.88), 0.05, 0.014, gold, 16, 6, axis=(0, 1, 0)))
    rnd = random.Random(8)
    cols = ["#ff3a3a", "#ffd23a", "#3ccf4a", "#2a9aff", "#ff4ac8", "#ff9a1a", "#a24aff"]
    for i in range(34):
        c = cols[i % len(cols)]
        m = tmat("conf_%s" % c, color=c, rough=0.3, metal=0.3 if i % 3 == 0 else 0.0, coat=0.5)
        t = rnd.uniform(0.1, 1.0)
        a = rnd.uniform(0, 2 * math.pi)
        spread = 0.15 + 0.6 * t
        p = (spread * math.cos(a) * 0.9, spread * math.sin(a) * 0.5, 0.45 + 0.85 * t)
        if i % 5 == 4:
            from icon_models import star_pts
            pts = star_pts(0.07)
            st = C.extrude(n + "_st%d" % i, pts, 0.02, m)
            xform(st, T(*p) @ rot(rnd.uniform(0, 360), "Z") @ rot(rnd.uniform(-40, 40), "X"))
            o.append(st)
        else:
            o.append(C.box(n + "_c%d" % i, p, (0.08, 0.05, 0.008), m,
                           rot=rot(rnd.uniform(0, 360), "Z") @ rot(rnd.uniform(0, 360), "X")))
    for k in range(5):
        c = cols[(k * 2) % len(cols)]
        m = tmat("conf_%s" % c, color=c, rough=0.3, coat=0.5)
        a = 2 * math.pi * k / 5 + 0.3
        pts = []
        for t in np.linspace(0, 1, 40):
            r = 0.05 + 0.08 * t
            cx = 0.5 * t * math.cos(a) * 0.9
            cy = 0.5 * t * math.sin(a) * 0.4
            pts.append((cx + r * math.cos(t * 18), cy + r * math.sin(t * 18), 0.35 + 0.75 * t))
        o.append(tube(n + "_strm%d" % k, pts, 0.016, m, 6))
    return xform_all(o, rot(-28, "Y"))


def innertube(n):
    a_m = tmat("tube_red", ("tube_red", X.rubber, ("#e8302a",)), scale=2.5, rough=0.35, bump=0.25, coat=0.6)
    b_m = tmat("tube_white", ("tube_white", X.rubber, ("#f4f2ee",)), scale=2.5, rough=0.35, bump=0.25, coat=0.6)
    ring = C.torus(n + "_t", (0, 0, 0), 0.5, 0.22, a_m, 64, 24)
    ring.data.materials.append(b_m)
    for p in ring.data.polygons:
        a = math.atan2(p.center.y, p.center.x) % (2 * math.pi)
        p.material_index = int(a / (math.pi / 4)) % 2
    o = [ring]
    o.append(C.cyl(n + "_valve", (0.0, -0.66, 0.12), (0.0, -0.72, 0.2), 0.04, 0.035, M_rubber(), 12))
    o.append(C.cyl(n + "_vcap", (0.0, -0.715, 0.195), (0.0, -0.735, 0.225), 0.03, 0.03, M_steel("vcap"), 12))
    rope = tmat("rope", ("rope", X.fabric, ("#d8c08a", 158, 30)), scale=6, rough=0.9, bump=0.5)
    pts = []
    for t in np.linspace(0, 1, 160):
        a = t * 2 * math.pi
        sag = 0.06 * abs(math.sin(a * 2)) ** 1.5
        pts.append(((0.74 + 0.02 - sag * 0.2) * math.cos(a), (0.74 + 0.02 - sag * 0.2) * math.sin(a), 0.0 - sag))
    o.append(tube(n + "_rope", pts, 0.02, rope, 8, caps=False))
    for k in range(4):
        a = 2 * math.pi * k / 4
        o.append(C.torus(n + "_tie%d" % k, (0.72 * math.cos(a), 0.72 * math.sin(a), 0.0), 0.05, 0.02, rope, 14, 6,
                         axis=(-math.sin(a), math.cos(a), 0)))
    return xform_all(o, rot(55, "X") @ rot(10, "Y"))


def repulse_shield(n):
    """Repulse Shield generator: a heavy emitter puck with three charged prongs under a hex energy dome."""
    o = []
    base_m = M_paint("rs_paint", "#3a4a66", 601, 0.35, "#222a3a")
    body = C.lathe(n + "_base", [(0.0, -0.1), (0.48, -0.1), (0.54, -0.05), (0.55, 0.05), (0.48, 0.12), (0.3, 0.16), (0.0, 0.16)], base_m, 48)
    o.append(body)
    hzm = tmat("hazard", "hazard", proj="TUBE", scale=(1, 1, 2.8), rough=0.45, bump=0.2, coat=0.3)
    o.append(C.cyl(n + "_hz", (0, 0, -0.06), (0, 0, 0.02), 0.555, 0.555, hzm, 48))
    core = tmat("rs_core", color="#5ad8ff", emission=6.0, emit_color="#5ad8ff", rough=0.2)
    o.append(C.cyl(n + "_ring", (0, 0, 0.15), (0, 0, 0.19), 0.24, 0.24, M_steel("rs_steel"), 32))
    o.append(C.sphere(n + "_core", (0, 0, 0.23), (0.15, 0.15, 0.12), core, 24, 12))
    coil = M_steel("rs_coil", tint="#e0a060", rough=0.25)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.5
        x, y = 0.36 * math.cos(a), 0.36 * math.sin(a)
        o.append(C.cyl(n + "_prong%d" % k, (x, y, 0.1), (x * 0.8, y * 0.8, 0.5), 0.03, 0.022, M_dark_steel(), 12))
        o.append(tube(n + "_coil%d" % k, helix(0.045, 0.16, 0.38, 6, 16, x=x * 0.93, y=y * 0.93), 0.01, coil, 6))
        o.append(C.sphere(n + "_tip%d" % k, (x * 0.8, y * 0.8, 0.52), 0.045, core, 12, 6))
    dome_m = tmat("hexdome", "hexgrid", proj="SPHERE", scale=(1.0, 1.0, 1.0), emission=1.6, rough=0.2, bump=0.0, alpha=0.38)
    dome = C.sphere(n + "_dome", (0, 0, 0.0), (0.82, 0.82, 0.82), dome_m, 48, 24)
    me = dome.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z < -0.02], context="FACES")
    bm.to_mesh(me)
    bm.free()
    o.append(dome)
    o.append(C.torus(n + "_domerim", (0, 0, 0.0), 0.82, 0.02, core, 64, 8))
    return xform_all(o, rot(6, "X"))


def fish_soup(n):
    """Fish Soup: a glazed bowl of orange broth with a fish, carrot coins, scallions and rising steam."""
    bowl_m = tmat("bowl", "bowl_band", proj="TUBE", scale=(1.6, 1.6, 2.6), loc=(0, 0, -0.1), rough=0.15, coat=0.8, bump=0.1)
    bowl = C.lathe(n + "_bowl", [(0.0, -0.32), (0.22, -0.32), (0.24, -0.27), (0.42, -0.18), (0.56, 0.0), (0.62, 0.17), (0.6, 0.2),
                                 (0.55, 0.06), (0.4, -0.12), (0.0, -0.2)], bowl_m, 48)
    o = [bowl]
    brm = tmat("broth", "broth", scale=1.6, rough=0.1, coat=0.6, bump=0.2)
    o.append(C.cyl(n + "_broth", (0, 0, 0.09), (0, 0, 0.12), 0.565, 0.565, brm, 48))
    carrot = tmat("carrot", ("carrot", X.mushroom_stem, ()), scale=4, rough=0.35, coat=0.4, tint="#ff7a1a", bump=0.3)
    rnd = random.Random(12)
    for i in range(6):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.15, 0.42)
        c = bevel(C.cyl(n + "_car%d" % i, (r * math.cos(a), r * math.sin(a), 0.115), (r * math.cos(a), r * math.sin(a), 0.14), 0.06, 0.06,
                        carrot, 16), 0.01, 1)
        o.append(c)
    scal = tmat("scallion", ("scallion", X.fabric, ("#4ac03a", 80, 40)), scale=4, rough=0.35, coat=0.4, bump=0.2)
    for i in range(10):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.1, 0.45)
        p = V((r * math.cos(a), r * math.sin(a), 0.13))
        ring = C.torus(n + "_sc%d" % i, p, 0.025, 0.008, scal, 12, 6)
        o.append(ring)
    # the fish: body, tail fin poking up out of the broth, head with an eye
    fishm = tmat("fish_skin", "fish_scales", scale=3.5, rough=0.25, metal=0.3, coat=0.8, bump=0.4)
    finm = tmat("fish_fin", ("fish_fin", X.fabric, ("#e8783a", 82, 30)), scale=4, rough=0.35, coat=0.5, bump=0.2)
    body = C.sphere(n + "_fish", (0, 0, 0), (0.3, 0.1, 0.12), fishm, 24, 12)
    for v in body.data.vertices:
        if v.co.x < 0:
            k = -v.co.x / 0.3
            v.co.y *= 1 - 0.55 * k
            v.co.z *= 1 - 0.45 * k
    body.data.update()
    fin = bevel(C.extrude(n + "_tail", [(-0.27, 0.0), (-0.5, 0.2), (-0.43, 0.0), (-0.5, -0.17)], 0.025, finm), 0.008, 1)
    dfin = bevel(C.extrude(n + "_dfin", [(-0.14, 0.09), (-0.02, 0.22), (0.1, 0.1)], 0.02, finm), 0.006, 1)
    eye_w = flat_mat("eye_white", "#ffffff", 0.2, coat=1.0)
    eye_b = flat_mat("eye_black", "#101014", 0.1, coat=1.0)
    eye = [C.sphere(n + "_eye", (0.2, -0.075, 0.035), 0.035, eye_w, 12, 8), C.sphere(n + "_pup", (0.21, -0.1, 0.035), 0.018, eye_b, 10, 6)]
    fish_parts = [body, fin, dfin] + eye
    o += xform_all(fish_parts, T(0.06, -0.08, 0.15) @ rot(-18, "Z") @ rot(-10, "Y") @ S(1.4))
    # chopsticks resting across the rim
    sticks = M_wood("chopsticks", ("#3a1a0e", "#5a2a14", "#7a3a1e", "#9a4e28"), 4.0)
    tipm = tmat("chop_tip", color="#d8b07a", rough=0.5)
    for k, dy in enumerate((-0.05, 0.05)):
        p0, p1 = V((-0.75, 0.25 + dy, 0.24 + k * 0.02)), V((0.45, -0.1 + dy * 1.6, 0.3 + k * 0.02))
        mid = p0.lerp(p1, 0.78)
        o.append(C.cyl(n + "_stick%d" % k, p0, mid, 0.028, 0.02, sticks, 10))
        o.append(C.cyl(n + "_sticktip%d" % k, mid, p1, 0.02, 0.012, tipm, 10))
    steam = tmat("steam", color="#ffffff", emission=0.6, emit_color="#ffffff", alpha=0.5, rough=0.5)
    for k, (x0, ph) in enumerate(((-0.2, 0.0), (0.05, 2.0), (0.28, 4.0))):
        pts = [(x0 + 0.08 * math.sin(t * 8 + ph), 0.05 * math.cos(t * 6 + ph), 0.25 + 0.6 * t) for t in np.linspace(0, 1, 30)]
        o.append(tube(n + "_steam%d" % k, pts, 0.05, steam, 10, rfn=lambda t: 0.4 + 0.6 * math.sin(math.pi * t)))
    return xform_all(o, rot(4, "X"))


SUPPLIES = {
    "SalmonSushi": salmon_sushi,
    "SpicySushi": spicy_sushi,
    "WasabiSushi": wasabi_sushi,
    "Shield": shield,
    "Umbrella": umbrella,
    "Scroll": scroll,
    "PogoStick": pogo_stick,
    "Bandage": bandage,
    "Burrito": burrito,
    "Kamikaze": kamikaze,
    "Caltrops": caltrops,
    "Mine": mine,
    "FlameMine": flame_mine,
    "ProteinBar": protein_bar,
    "Mushroom": mushroom,
    "Confetti": confetti,
    "SpringMine": spring_mine,
    "Innertube": innertube,
    "RepulseShield": repulse_shield,
    "FishSoup": fish_soup,
}


# ------------------------------------------------------------------------------------------ render

def _clear(col):
    for o in list(col.all_objects):
        bpy.data.objects.remove(o)
    bpy.data.collections.remove(col)
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)


def render_one(sid, out_dir=OUT, size=SIZE):
    from PIL import Image
    col = C.new_collection("SUPPLY")
    C.use_collection(col)
    try:
        SUPPLIES[sid](sid)
    finally:
        C.use_collection(None)
    big = os.path.join(X.OUT, "render_" + sid + ".png")
    R.render_icon([col], big, view=R.VIEW_34, size=RENDER, pad=0.07)
    _clear(col)
    _mats.clear()
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, sid + ".png")
    Image.open(big).resize((size, size), Image.LANCZOS).save(out)
    return out


def run(only=None):
    import time
    C.reset()
    R.setup_scene()
    n = 0
    for sid in SUPPLIES:
        if only and sid not in only:
            continue
        t = time.time()
        render_one(sid)
        print("[supplies] %-14s %.0fs" % (sid, time.time() - t), flush=True)
        n += 1
    return n


if __name__ == "__main__":
    run(sys.argv[1:] or None)
