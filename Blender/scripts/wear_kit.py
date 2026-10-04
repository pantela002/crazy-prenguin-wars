"""Textured look for the penguin and everything he wears (penguin.py, clothes.py, wear_icons.py).

- Tileable cloth/material textures painted with numpy (periodic FFT noise, so every texture tiles):
  Resources/Textures/Clothes/{name}.png, 256x256. Most are light grey detail (multiplied by the material tint in Unity),
  the patterned ones (plaid, camo, polka, starry, ...) carry their own colors and are used with a white tint.
- Textured materials: Blender material "T_{texture}__{tint}" (tint = palette key or hex without '#'). Unity
  (Art/WearTextures.cs) loads Textures/Clothes/{texture} (or Textures/Penguin/{skin} for "T_penguin") onto it.
- UV helpers that write the real texture coordinates into a UV layer "Tex" (box, cylinder, sphere, torus projections,
  each with a texture density in units per repeat), and export_textured(), which is common.export_collection plus:
  the "Tex" UVs become UV0 in the FBX (common bakes a planar UV0 that only suits the untextured models), and the
  texture links are cut during the export so the FBX diffuse color is the plain tint.
"""
import math
import os

import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import numpy as np
from mathutils import Matrix, Vector

import common as C

TEX_DIR = os.path.join(C.TEXTURES, "Clothes")
PENGUIN_TEX_DIR = os.path.join(C.TEXTURES, "Penguin")
N = 256

# units per texture repeat (how big the weave/pattern is on the model)
TEX_SCALE = {
    "cotton": 0.55, "knit": 0.42, "wool": 0.6, "denim": 0.5, "canvas": 0.45, "leather": 0.8, "fur": 0.5,
    "rubber": 1.0, "satin": 1.0, "metal": 0.8, "hammered": 0.6, "paper": 0.9, "plastic": 1.0, "sequin": 0.36,
    "quilted": 0.62, "felt": 0.7, "plaid": 0.85, "camo": 1.3, "polka": 0.75, "starry": 0.95, "stripes": 0.6,
    "scales": 0.5, "wood": 0.9, "glass": 1.0,
}
# textures that carry their own colors (use a white tint)
COLORED = {"plaid", "camo", "polka", "starry"}


# ------------------------------------------------------------------------------------------ noise

def pnoise(shape, freq, seed, aniso=(1.0, 1.0)):
    """Periodic gaussian-filtered noise, zero mean, unit std. freq = feature frequency in cycles per tile
    (aniso stretches it: (fx, fy) multipliers)."""
    h, w = shape if isinstance(shape, tuple) else (shape, shape)
    rnd = np.random.RandomState(seed)
    wn = rnd.randn(h, w).astype(np.float32)
    fy = np.fft.fftfreq(h)[:, None] * h / max(1e-3, aniso[1])
    fx = np.fft.fftfreq(w)[None, :] * w / max(1e-3, aniso[0])
    k = np.exp(-(fx * fx + fy * fy) / (2 * freq * freq))
    out = np.real(np.fft.ifft2(np.fft.fft2(wn) * k)).astype(np.float32)
    out -= out.mean()
    return out / max(1e-6, out.std())


def fbm(shape, freq, seed, octaves=4, gain=0.5, aniso=(1.0, 1.0)):
    out = 0.0
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        out = out + pnoise(shape, freq * 2 ** o, seed + 31 * o, aniso) * amp
        tot += amp
        amp *= gain
    return out / tot


def worley(shape, count, seed):
    """Periodic Worley noise: (F1, F2) distances in pixels and the cell index."""
    h, w = shape if isinstance(shape, tuple) else (shape, shape)
    rnd = np.random.RandomState(seed)
    pts = rnd.rand(count, 2) * (w, h)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d1 = np.full((h, w), 1e9, np.float32)
    d2 = np.full((h, w), 1e9, np.float32)
    cid = np.zeros((h, w), np.int32)
    for k, (px, py) in enumerate(pts):
        dx = np.abs(xx - px)
        dx = np.minimum(dx, w - dx)
        dy = np.abs(yy - py)
        dy = np.minimum(dy, h - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        cid = np.where(closer, k, cid)
        d1 = np.where(closer, d, d1)
    return d1, d2, cid


def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def hexc(h):
    return np.array(C.hex_rgb(C.PALETTE.get(h, h)), np.float32)


def gray(v):
    v = np.clip(v, 0, 1)
    return np.stack([v, v, v], -1)


def rgb_mix(a, b, m):
    m = m[..., None] if np.ndim(m) == 2 else m
    return a * (1 - m) + b * m


# ------------------------------------------------------------------------------------------ texture recipes
# grey textures: mean around 0.9 (Unity multiplies them by the tint), contrast kept soft so colors stay clean

def _grid(n=N):
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    return xx / n, yy / n


def tex_cotton(n=N):
    x, y = _grid(n)
    warp = 0.5 + 0.5 * np.sin(2 * np.pi * x * 48 + pnoise(n, 30, 1) * 0.4)
    weft = 0.5 + 0.5 * np.sin(2 * np.pi * y * 48 + pnoise(n, 30, 2) * 0.4)
    weave = np.where((np.floor(x * 48) + np.floor(y * 48)) % 2 == 0, warp, weft)
    v = 0.9 + 0.04 * (weave - 0.5) + 0.035 * fbm(n, 4, 3) + 0.015 * pnoise(n, 60, 4)
    return gray(v)


def tex_knit(n=N):
    """Stockinette: columns of V stitches (two slanted loops each)."""
    x, y = _grid(n)
    cols, rows = 10, 12
    cx = x * cols
    cy = y * rows
    fx = cx - np.floor(cx)          # 0..1 across a column
    fy = cy - np.floor(cy)
    half = np.where(fx < 0.5, 1.0, -1.0)
    lx = np.where(fx < 0.5, fx, 1 - fx) * 2           # 0 at column edge, 1 at the middle
    # each half of the V is a loop leaning toward the column middle going down
    t = fy - 0.5 + (lx - 0.55) * 0.9
    loop = np.exp(-(t * t) / 0.045) * sstep(0.0, 0.25, lx) * sstep(1.02, 0.85, lx)
    gap = sstep(0.0, 0.12, lx)
    yarn = 0.5 + 0.5 * np.sin((fy * 2 + lx * 1.4 * half) * np.pi * 3)
    v = 0.74 + 0.22 * loop + 0.03 * yarn * loop - 0.08 * (1 - gap)
    v += 0.03 * fbm(n, 6, 7) + 0.02 * pnoise(n, 70, 8)
    return gray(v)


def tex_wool(n=N):
    fibers = pnoise(n, 50, 11, aniso=(1.0, 0.25)) * 0.5 + pnoise(n, 50, 12, aniso=(0.25, 1.0)) * 0.5
    v = 0.89 + 0.045 * fbm(n, 5, 13) + 0.035 * fibers + 0.02 * pnoise(n, 90, 14)
    return gray(v)


def tex_felt(n=N):
    v = 0.9 + 0.05 * fbm(n, 6, 21, octaves=5) + 0.02 * pnoise(n, 100, 22)
    return gray(v)


def tex_denim(n=N):
    x, y = _grid(n)
    twill = 0.5 + 0.5 * np.sin(2 * np.pi * (x + y) * 40 + pnoise(n, 20, 31) * 0.6)
    slub = pnoise(n, 30, 32, aniso=(1.0, 0.12))
    flecks = np.clip(pnoise(n, 110, 33) - 1.6, 0, 1)
    v = 0.8 + 0.1 * twill + 0.05 * slub + 0.25 * flecks + 0.04 * fbm(n, 3, 34)
    return gray(v)


def tex_canvas(n=N):
    x, y = _grid(n)
    a = 0.5 + 0.5 * np.cos(2 * np.pi * x * 36)
    b = 0.5 + 0.5 * np.cos(2 * np.pi * y * 36)
    v = 0.84 + 0.06 * a * b + 0.04 * np.maximum(a, b) + 0.04 * fbm(n, 5, 41) + 0.02 * pnoise(n, 70, 42)
    return gray(v)


def tex_leather(n=N):
    f1, f2, _ = worley(n, 160, 51)
    cell = 0.6 * sstep(0, 3.0, f2 - f1) + 0.4
    creases = np.clip(-pnoise(n, 7, 52, aniso=(1.0, 0.3)) - 1.4, 0, 1)
    v = 0.8 + 0.08 * cell + 0.06 * fbm(n, 4, 53) - 0.12 * creases + 0.02 * pnoise(n, 80, 54)
    return gray(v)


def tex_fur(n=N):
    strands = pnoise(n, 60, 61, aniso=(0.3, 1.6)) * 0.6 + pnoise(n, 25, 62, aniso=(0.4, 1.3)) * 0.4
    clumps = fbm(n, 4, 63)
    v = 0.86 + 0.08 * strands + 0.06 * clumps
    return gray(v)


def tex_rubber(n=N):
    v = 0.93 + 0.025 * fbm(n, 3, 71) + 0.012 * pnoise(n, 90, 72)
    return gray(v)


def tex_plastic(n=N):
    v = 0.95 + 0.02 * fbm(n, 3, 81) + 0.008 * pnoise(n, 100, 82)
    return gray(v)


def tex_satin(n=N):
    x, y = _grid(n)
    sheen = np.sin(2 * np.pi * (x * 2 + 0.15 * pnoise(n, 2, 91)))
    v = 0.9 + 0.05 * sheen + 0.02 * pnoise(n, 90, 92, aniso=(1.0, 0.1))
    return gray(v)


def tex_metal(n=N):
    brushed = pnoise(n, 70, 101, aniso=(0.08, 1.0))
    v = 0.86 + 0.05 * brushed + 0.05 * fbm(n, 3, 102) + 0.03 * np.clip(pnoise(n, 40, 103) - 1.8, 0, 1)
    return gray(v)


def tex_hammered(n=N):
    f1, f2, _ = worley(n, 70, 111)
    t = f1 / np.maximum(1e-3, 0.5 * (f1 + f2))          # 0 at a dimple center, 1 on the ridges
    v = 0.8 + 0.12 * t * t + 0.05 * np.sin(np.clip(t, 0, 1) * np.pi) + 0.03 * fbm(n, 4, 112)
    return gray(v)


def tex_paper(n=N):
    x, y = _grid(n)
    fib = pnoise(n, 80, 121, aniso=(1.0, 0.2))
    corr = 0.5 + 0.5 * np.sin(2 * np.pi * y * 10)
    v = 0.88 + 0.03 * fib + 0.05 * fbm(n, 4, 122) + 0.025 * corr
    return gray(v)


def tex_sequin(n=N):
    x, y = _grid(n)
    k = 16
    gx = x * k
    gy = y * k
    off = (np.floor(gy) % 2) * 0.5
    fx = (gx + off) - np.floor(gx + off) - 0.5
    fy = gy - np.floor(gy) - 0.5
    d = np.sqrt(fx * fx + fy * fy)
    disc = sstep(0.48, 0.4, d)
    # each sequin catches the light differently
    cid = (np.floor(gx + off) * 7 + np.floor(gy) * 13) % 5 / 4.0
    glint = sstep(0.3, 0.0, np.sqrt((fx + 0.15) ** 2 + (fy + 0.15) ** 2)) * cid
    v = 0.62 + 0.3 * disc + 0.25 * glint
    return gray(v)


def tex_quilted(n=N):
    x, y = _grid(n)
    a = (x + y) * 4
    b = (x - y) * 4
    da = np.abs(a - np.round(a))
    db = np.abs(b - np.round(b))
    seam = np.minimum(da, db)
    puff = sstep(0.0, 0.25, seam)
    stitch = (np.sin(2 * np.pi * (x - y) * 64) > 0.3) * (da < 0.02) + (np.sin(2 * np.pi * (x + y) * 64) > 0.3) * (db < 0.02)
    v = 0.7 + 0.24 * puff - 0.12 * stitch + 0.03 * fbm(n, 5, 131)
    return gray(v)


def tex_stripes(n=N):
    x, y = _grid(n)
    s = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * y * 4))
    v = 0.78 + 0.18 * s + 0.03 * fbm(n, 5, 141)
    return gray(v)


def tex_scales(n=N):
    """Overlapping round scales (armor, dragon-ish trims)."""
    x, y = _grid(n)
    k = 8
    gy = y * k
    row = np.floor(gy)
    gx = x * k + (row % 2) * 0.5
    fx = gx - np.floor(gx) - 0.5
    fy = gy - row
    d = np.sqrt(fx * fx + (fy - 0.0) ** 2)
    v = 0.66 + 0.3 * sstep(0.62, 0.2, d) * (0.6 + 0.4 * fy)
    return gray(v + 0.03 * fbm(n, 5, 151))


def tex_wood(n=N):
    x, y = _grid(n)
    rings = np.sin(2 * np.pi * (x * 6 + 0.6 * fbm(n, 2, 161, aniso=(0.3, 1.0))))
    v = 0.8 + 0.08 * rings + 0.05 * pnoise(n, 60, 162, aniso=(0.1, 1.0))
    return gray(v)


def tex_glass(n=N):
    return gray(0.97 + 0.01 * fbm(n, 2, 171))


def tex_plaid(n=N):
    """Red lumberjack check: red ground, black bands, thin light lines, twill."""
    x, y = _grid(n)
    red, dark, black, light = hexc("#d23a30"), hexc("#8e1e1c"), hexc("#26181a"), hexc("#f0b0a0")

    def bands(t):
        t = t % 1.0
        b = sstep(0.02, 0.05, t) * sstep(0.48, 0.45, t)          # wide black band
        thin = sstep(0.66, 0.675, t) * sstep(0.705, 0.69, t)     # thin light line
        return b, thin
    bx, tx = bands(x * 2)
    by, ty = bands(y * 2)
    base = np.broadcast_to(red, (n, n, 3)).copy()
    base = rgb_mix(base, dark, np.maximum(bx, by) * 0.85)
    base = rgb_mix(base, black, bx * by)
    base = rgb_mix(base, light, np.maximum(tx * (1 - by), ty * (1 - bx)) * 0.55)
    twill = 0.5 + 0.5 * np.sin(2 * np.pi * (x + y) * 64)
    base *= (0.92 + 0.08 * twill)[..., None]
    base *= (0.97 + 0.04 * fbm(n, 5, 181))[..., None]
    return np.clip(base, 0, 1)


def tex_camo(n=N):
    cols = [hexc("#7d8a4a"), hexc("#55602f"), hexc("#a39a6a"), hexc("#2f3420")]
    base = np.broadcast_to(cols[0], (n, n, 3)).copy()
    for i, c in enumerate(cols[1:]):
        m = sstep(0.55, 0.7, fbm(n, 3, 191 + i, octaves=3) * 0.5 + 0.5 + 0.08 * i - 0.05)
        base = rgb_mix(base, c, m)
    base *= (0.95 + 0.06 * tex_canvas(n)[..., 0])[..., None]
    return np.clip(base, 0, 1)


def tex_polka(n=N):
    x, y = _grid(n)
    k = 4
    gy = y * k
    row = np.floor(gy)
    gx = x * k + (row % 2) * 0.5
    col = np.floor(gx)
    fx = gx - col - 0.5
    fy = gy - row - 0.5
    d = np.sqrt(fx * fx + fy * fy)
    dot = sstep(0.27, 0.24, d)
    palette = [hexc(h) for h in ("#e83a3a", "#3a7be8", "#ffd23a", "#4cbb3f", "#8a4fd8")]
    idx = (((col % k) * 3 + (row % k) * 2) % 5).astype(int)
    pal = np.array(palette)[idx]
    base = np.broadcast_to(hexc("#fbf7ee"), (n, n, 3)).copy()
    base = rgb_mix(base, pal, dot)
    base *= (0.97 + 0.04 * tex_cotton(n)[..., 0])[..., None]
    return np.clip(base, 0, 1)


def tex_starry(n=N):
    """Wizard cloth: purple with tiny gold stars and moons."""
    x, y = _grid(n)
    base = np.broadcast_to(hexc("#6a3cc0"), (n, n, 3)).copy()
    base = rgb_mix(base, hexc("#4a2690"), sstep(-0.2, 0.9, fbm(n, 3, 201)) * 0.7)
    rnd = np.random.RandomState(202)
    gold = hexc("#ffd84a")
    star = np.zeros((n, n), np.float32)
    for _ in range(14):
        cx, cy, r = rnd.rand() * n, rnd.rand() * n, rnd.uniform(5, 10)
        dx = (np.mgrid[0:n, 0:n][1] - cx + n / 2) % n - n / 2
        dy = (np.mgrid[0:n, 0:n][0] - cy + n / 2) % n - n / 2
        a = np.arctan2(dy, dx)
        rr = np.sqrt(dx * dx + dy * dy)
        lim = r * (0.55 + 0.45 * np.cos(5 * a)) ** 2 * 1.2 + r * 0.25
        star = np.maximum(star, sstep(lim + 1, lim - 0.5, rr))
    for _ in range(40):
        cx, cy = rnd.rand() * n, rnd.rand() * n
        dx = (np.mgrid[0:n, 0:n][1] - cx + n / 2) % n - n / 2
        dy = (np.mgrid[0:n, 0:n][0] - cy + n / 2) % n - n / 2
        star = np.maximum(star, sstep(2.2, 0.8, np.sqrt(dx * dx + dy * dy)) * 0.8)
    base = rgb_mix(base, gold, star)
    base *= (0.95 + 0.05 * tex_satin(n)[..., 0])[..., None]
    return np.clip(base, 0, 1)


RECIPES = {
    "cotton": tex_cotton, "knit": tex_knit, "wool": tex_wool, "felt": tex_felt, "denim": tex_denim,
    "canvas": tex_canvas, "leather": tex_leather, "fur": tex_fur, "rubber": tex_rubber, "plastic": tex_plastic,
    "satin": tex_satin, "metal": tex_metal, "hammered": tex_hammered, "paper": tex_paper, "sequin": tex_sequin,
    "quilted": tex_quilted, "stripes": tex_stripes, "scales": tex_scales, "wood": tex_wood, "glass": tex_glass,
    "plaid": tex_plaid, "camo": tex_camo, "polka": tex_polka, "starry": tex_starry,
}


def tex_path(name):
    return os.path.join(TEX_DIR, name + ".png")


def write_textures(only=None):
    C.ensure_dir(TEX_DIR)
    for name, fn in RECIPES.items():
        if only and name not in only:
            continue
        img = fn()
        rgba = np.concatenate([img, np.ones(img.shape[:2] + (1,), np.float32)], -1)
        C.write_png(tex_path(name), rgba)
    print("[wear_kit] %d cloth textures -> %s" % (len(RECIPES), TEX_DIR))


def ensure_textures():
    if any(not os.path.exists(tex_path(n)) for n in RECIPES):
        write_textures()


# ------------------------------------------------------------------------------------------ materials

_tmats = {}
_images = {}


def _image(path):
    img = _images.get(path)
    try:
        if img is not None and img.name in bpy.data.images:
            return img
    except ReferenceError:
        pass
    img = bpy.data.images.load(path, check_existing=True)
    _images[path] = img
    return img


def tmat(tex, tint="white", image_path=None, rough=0.6, spec=0.25):
    """Textured material T_{tex}__{tint}. tint: palette key or '#rrggbb'. The Base Color is linked to
    image * tint for Blender renders; the export cuts the link so the FBX carries the tint only."""
    tkey = tint.lstrip("#") if tint.startswith("#") else tint
    name = "T_%s__%s" % (tex, tkey)
    m = _tmats.get(name)
    try:
        if m is not None and m.name in bpy.data.materials:
            return m
    except ReferenceError:
        pass
    hexcol = tint if tint.startswith("#") else C.PALETTE.get(tint, "#ffffff")
    m = C.mat(name, hexcol)
    m["tex"] = tex
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = rough
    try:
        bsdf.inputs["Specular IOR Level"].default_value = spec
    except KeyError:
        pass
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "Tex"
    it = nt.nodes.new("ShaderNodeTexImage")
    it.image = _image(image_path or tex_path(tex))
    it.interpolation = "Linear"
    tintn = nt.nodes.new("ShaderNodeRGB")
    tintn.name = "Tint"
    lin = [C.srgb_to_linear(c) for c in C.hex_rgb(hexcol)]
    tintn.outputs[0].default_value = (*lin, 1)
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.name = "TexMul"
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(uv.outputs["UV"], it.inputs["Vector"])
    nt.links.new(it.outputs["Color"], mul.inputs[6])
    nt.links.new(tintn.outputs[0], mul.inputs[7])
    nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    _tmats[name] = m
    return m


def _cut_links():
    """Disconnect the texture from the Base Color of every T_ material (FBX diffuse = tint). Returns a restore fn."""
    cut = []
    for m in bpy.data.materials:
        if "tex" not in m or not m.node_tree:
            continue
        nt = m.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        mul = nt.nodes.get("TexMul")
        for l in list(nt.links):
            if l.to_node == bsdf and l.to_socket.name == "Base Color":
                nt.links.remove(l)
                cut.append((nt, mul, bsdf))

    def restore():
        for nt, mul, bsdf in cut:
            nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    return restore


# ------------------------------------------------------------------------------------------ UVs

def _uv_layer(me):
    lay = me.uv_layers.get("Tex")
    if lay is None:
        lay = me.uv_layers.new(name="Tex")
    return lay


def has_uv(ob):
    return ob.type == "MESH" and ob.data.uv_layers.get("Tex") is not None


def _tex_of(ob):
    for m in ob.data.materials:
        if m is not None and "tex" in m:
            return m["tex"]
    return None


def scale_for(ob, scale=None):
    if scale is not None:
        return scale
    return TEX_SCALE.get(_tex_of(ob) or "", 0.6)


def _world_corners(ob):
    me = ob.data
    off = C.world_loc(ob)
    return me, off


def uv_box(ob, scale=None, center=(0, 0, 0)):
    """Per-face box projection (dominant normal axis), 1 repeat per `scale` units."""
    me, off = _world_corners(ob)
    s = 1.0 / scale_for(ob, scale)
    lay = _uv_layer(me)
    c = Vector(center)
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co + off - c
            if ax == 0:
                u, v = co.y * (1 if n.x > 0 else -1), co.z
            elif ax == 1:
                u, v = co.x * (-1 if n.y > 0 else 1), co.z
            else:
                u, v = co.x, co.y * (1 if n.z > 0 else -1)
            lay.data[li].uv = (u * s, v * s)
    return ob


def _fix_wrap(us):
    """Make the u values of one face continuous across the 0/1 seam."""
    if max(us) - min(us) > 0.5:
        return [u + 1.0 if u < 0.5 else u for u in us]
    return us


def uv_cyl(ob, center=(0, 0, 0), scale=None, radius=None, axis="Z", front_seam=False):
    """Cylindrical projection around an axis through center. u = arc length (radius * angle, so the pattern keeps its
    size), v = height; seam at the back (+Y) unless front_seam."""
    me, off = _world_corners(ob)
    s = 1.0 / scale_for(ob, scale)
    lay = _uv_layer(me)
    c = Vector(center)
    for p in me.polygons:
        ang, hs, rs = [], [], []
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co + off - c
            if axis == "Z":
                a, b, h = co.x, co.y, co.z
            elif axis == "Y":
                a, b, h = co.x, co.z, co.y
            else:
                a, b, h = co.y, co.z, co.x
            # atan2(a, b): 0 at the back (+Y), 0.5 at the front (-Y)
            t = (math.atan2(a, b) / (2 * math.pi)) % 1.0 if not front_seam else (math.atan2(a, -b) / (2 * math.pi)) % 1.0
            if abs(a) < 1e-6 and abs(b) < 1e-6:
                t = None
            ang.append(t)
            hs.append(h)
            rs.append(math.hypot(a, b))
        known = [t for t in ang if t is not None]
        fill = sum(known) / len(known) if known else 0.0
        ang = _fix_wrap([fill if t is None else t for t in ang])
        R = radius if radius is not None else max(0.05, sum(rs) / len(rs))
        for li, t, h in zip(p.loop_indices, ang, hs):
            lay.data[li].uv = (t * 2 * math.pi * R * s, h * s)
    return ob


def uv_sphere(ob, center=(0, 0, 0), scale=None, radius=None):
    """Latitude/longitude projection (sizes in arc length at `radius`)."""
    me, off = _world_corners(ob)
    s = 1.0 / scale_for(ob, scale)
    lay = _uv_layer(me)
    c = Vector(center)
    for p in me.polygons:
        ang, lat, rs = [], [], []
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co + off - c
            r = co.length
            rs.append(r)
            if math.hypot(co.x, co.y) < 1e-6:
                ang.append(None)
            else:
                ang.append((math.atan2(co.x, co.y) / (2 * math.pi)) % 1.0)
            lat.append(math.asin(max(-1, min(1, co.z / max(r, 1e-6)))))
        known = [t for t in ang if t is not None]
        fill = sum(known) / len(known) if known else 0.0
        ang = _fix_wrap([fill if t is None else t for t in ang])
        R = radius if radius is not None else max(0.05, sum(rs) / len(rs))
        for li, t, la in zip(p.loop_indices, ang, lat):
            lay.data[li].uv = (t * 2 * math.pi * R * s, la * R * s)
    return ob


def uv_auto(objs, scale=None):
    """Box-project every mesh that has no Tex UVs yet."""
    for o in objs:
        if o is not None and o.type == "MESH" and not has_uv(o):
            uv_box(o, scale)
    return objs


# ------------------------------------------------------------------------------------------ export

def export_textured(col, path, aliases=()):
    """common.export_collection, keeping the "Tex" UVs as UV0 and exporting the plain tint as diffuse color."""
    import shutil
    C.ensure_dir(os.path.dirname(path))
    objs = C.col_objects(col)
    renamed = []
    for o in objs:
        want = o.get("export_name")
        if want and o.name != want:
            other = bpy.data.objects.get(want)
            if other is not None:
                other.name = want + "__tmp"
                renamed.append((other, want))
            old = o.name
            o.name = want
            renamed.insert(0, (o, old))
    C.transform_objects(objs, C.ROT_EXPORT)
    C.set_color_mode("srgb")
    restore_links = _cut_links()
    swapped = C._clean_meshes(objs)
    try:
        saved = {}
        for o in objs:
            if o.type == "MESH" and o.data.uv_layers.get("Tex") is not None:
                lay = o.data.uv_layers["Tex"]
                buf = [0.0] * (len(o.data.loops) * 2)
                lay.data.foreach_get("uv", buf)
                saved[o.name] = buf
        C._bake_vertex_data(objs)
        for o in objs:
            if o.name in saved and o.type == "MESH" and o.data.uv_layers.get("UVMap") is not None:
                o.data.uv_layers["UVMap"].data.foreach_set("uv", saved[o.name])
        lc = C._find_layer_collection(bpy.context.view_layer.layer_collection, col.name)
        bpy.context.view_layer.active_layer_collection = lc
        bpy.ops.export_scene.fbx(
            filepath=path, use_active_collection=True, use_selection=False,
            object_types={"MESH", "EMPTY"}, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_mesh_modifiers=True,
            mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, use_custom_props=False,
            path_mode="AUTO", embed_textures=False, use_tspace=False,
            colors_type="LINEAR", prioritize_active_color=False)
    finally:
        C._restore_meshes(swapped)
        restore_links()
        C.set_color_mode("linear")
        C.transform_objects(objs, C.ROT_EXPORT.inverted())
        for o, name in renamed:
            o.name = name
    for a in aliases:
        C.ensure_dir(os.path.dirname(a))
        shutil.copyfile(path, a)


def contact_sheet(out, names=None, cell=128):
    """Preview of the cloth textures (2x2 tiles each) for checking."""
    names = names or list(RECIPES)
    cols = 6
    rows = (len(names) + cols - 1) // cols
    sheet = np.ones((rows * cell, cols * cell, 3), np.float32)
    for i, nm in enumerate(names):
        img = RECIPES[nm]()
        t = np.tile(img, (2, 2, 1))
        step = t.shape[0] // cell
        t = t[::step, ::step][:cell, :cell]
        r, c = divmod(i, cols)
        sheet[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell] = t
    from PIL import Image
    Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8)).save(out)
