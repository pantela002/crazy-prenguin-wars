"""Procedural image textures for the supply (booster) models, painted with numpy/PIL and saved as PNG.

Every generator returns (rgb, height): float32 arrays (n, n, 3) and (n, n) in 0..1. The noise is periodic
(textures.value_noise wraps), so all of them tile. Decals (labels, parchment, wrappers) are drawn with PIL and
return RGBA. supplies.py turns them into Cycles image-texture materials (box / tube projection or UVs).
"""
import os
import tempfile

import numpy as np

import common as C
import textures as T

N = 512
OUT = os.path.join(tempfile.gettempdir(), "cpw_supply_tex")
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_SERIF = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"


def col(h):
    return np.array(C.hex_rgb(h), np.float32)


def noise(seed, base=4, octaves=4, n=N):
    return T._fbm_n(n, seed, octaves, base).astype(np.float32)


def grid(n=N):
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    return xx, yy


def ramp(t, cols):
    return T.ramp(t, [C.hex_rgb(c) for c in cols]).astype(np.float32)


def blend(a, b, m):
    return a * (1 - m[..., None]) + b * m[..., None]


def blur(a, r, passes=2):
    return T.wrap_blur(a, r, passes)


def stamp(n, count, rx, ry, seed, angle=None, jitter=0.25, profile=1.5):
    """Scatter wrapped elliptical domes. Returns (height max-map, mask of the dome footprint, per-dome random)."""
    rnd = np.random.RandomState(seed)
    h = np.zeros((n, n), np.float32)
    val = np.zeros((n, n), np.float32)
    R = int(max(rx, ry) * (1 + jitter)) + 2
    yy, xx = np.mgrid[-R:R + 1, -R:R + 1].astype(np.float32)
    for _ in range(count):
        cx, cy = rnd.randint(0, n, 2)
        a = rnd.uniform(0, np.pi) if angle is None else angle + rnd.uniform(-0.3, 0.3)
        sx = rx * rnd.uniform(1 - jitter, 1 + jitter)
        sy = ry * rnd.uniform(1 - jitter, 1 + jitter)
        u = (xx * np.cos(a) + yy * np.sin(a)) / sx
        v = (-xx * np.sin(a) + yy * np.cos(a)) / sy
        d = 1 - (u * u + v * v)
        dome = np.clip(d, 0, 1) ** (1 / profile)
        iy = (np.arange(-R, R + 1) + cy) % n
        ix = (np.arange(-R, R + 1) + cx) % n
        sub = h[np.ix_(iy, ix)]
        better = dome > sub
        h[np.ix_(iy, ix)] = np.where(better, dome, sub)
        vs = val[np.ix_(iy, ix)]
        val[np.ix_(iy, ix)] = np.where(better, rnd.rand(), vs)
    return h, val


def lines(angle, period, warp=0.0, seed=0, sharp=8.0, n=N):
    """Periodic soft lines across the tile (angle in multiples that keep it periodic: use integer kx, ky)."""
    xx, yy = grid(n)
    kx, ky = angle
    t = (xx * kx + yy * ky) / n * period
    if warp:
        t = t + noise(seed, 2, 3, n) * warp
    s = np.abs(np.sin(t * np.pi))
    return np.clip(1 - s * sharp, 0, 1) if sharp else s


# ------------------------------------------------------------------------------------------- food

def rice():
    h1, v1 = stamp(N, 900, 13, 6.5, 11)
    h2, v2 = stamp(N, 700, 12, 6, 12)
    h = np.maximum(h1, h2 * 0.92)
    rgb = ramp(0.55 + h * 0.45, ["#b8b2a4", "#efe9dc", "#ffffff"])
    rgb *= (0.94 + 0.06 * noise(13, 6))[..., None]
    gap = (h < 0.05).astype(np.float32)
    rgb = blend(rgb, col("#a39d8c") * 0.9, gap * 0.8)
    return rgb, h


def salmon():
    xx, yy = grid()
    w = noise(21, 2, 3)
    t = (xx * 1 + yy * 2) / N * 5 + w * 1.2
    band = np.abs(np.sin(t * np.pi))
    fat = np.clip(1 - band * 5, 0, 1) ** 0.8
    fine = np.clip(1 - np.abs(np.sin(t * np.pi * 3 + w * 3)) * 9, 0, 1) * 0.35
    base = ramp(noise(22, 3, 3), ["#e8552a", "#f46a32", "#ff8a4a"])
    rgb = blend(base, col("#ffd9c2"), np.clip(fat + fine, 0, 1))
    rgb *= (0.92 + 0.12 * noise(23, 12, 2))[..., None]
    return rgb, 0.5 + fat * 0.25 - fine * 0.1


def tuna():
    xx, yy = grid()
    w = noise(31, 2, 3)
    t = (xx * 1 + yy * 2) / N * 7 + w * 1.5
    vein = np.clip(1 - np.abs(np.sin(t * np.pi)) * 6, 0, 1) * 0.6
    base = ramp(noise(32, 3, 3), ["#a8142a", "#c81e34", "#e0364a"])
    rgb = blend(base, col("#f07a86"), vein)
    rgb *= (0.9 + 0.14 * noise(33, 16, 2))[..., None]
    return rgb, 0.5 + vein * 0.2


def nori():
    xx, yy = grid()
    fib = lines((1, 3), 60, 2.0, 41, 3) * 0.3 + lines((3, -1), 45, 2.0, 42, 3) * 0.2
    base = ramp(noise(43, 5, 4) * 0.7 + fib, ["#0b1a10", "#18301c", "#2b4a2a", "#3f6236"])
    rnd = np.random.RandomState(44)
    speck = (rnd.rand(N, N) > 0.993).astype(np.float32)
    speck = np.clip(blur(speck, 1, 1) * 6, 0, 1)
    rgb = blend(base, col("#5b7a46"), speck * 0.7)
    return rgb, noise(45, 8, 3) * 0.6 + fib


def tobiko():
    """Flying-fish roe coating (spicy maki outside): packed shiny red-orange beads."""
    h, v = stamp(N, 2600, 7, 7, 51, jitter=0.15, profile=0.8)
    base = ramp(v * 0.6 + 0.2, ["#b8160c", "#e8301a", "#ff5a24"])
    rgb = base * (0.45 + 0.65 * h)[..., None]
    spec = np.clip((h - 0.85) * 6, 0, 1)
    rgb = blend(rgb, col("#ffe0c8"), spec)
    rgb = blend(rgb, col("#5a0a06"), (h < 0.05).astype(np.float32))
    return rgb, h


def tortilla():
    base = ramp(noise(61, 4, 4), ["#e7c78a", "#efd49a", "#f6e2b0"])
    spots = np.clip((noise(62, 6, 4) - 0.58) * 6, 0, 1)
    char = np.clip((noise(63, 12, 3) - 0.66) * 8, 0, 1) * spots
    rgb = blend(base, col("#c08a44"), spots * 0.75)
    rgb = blend(rgb, col("#6a3c18"), char * 0.8)
    rgb *= (0.95 + 0.08 * noise(64, 24, 2))[..., None]
    return rgb, 0.5 + spots * 0.15 - char * 0.2 + noise(65, 16, 2) * 0.2


def chocolate():
    d1, edge, cid = T.voronoi(N, 60, 71)
    nut = (np.random.RandomState(72).rand(60)[cid] > 0.55).astype(np.float32)
    rgb = ramp(noise(73, 6, 3), ["#3e200e", "#55301a", "#6a3c20"])
    nuts = nut * np.clip(edge / 4, 0, 1)
    rgb = blend(rgb, ramp(noise(74, 10, 2), ["#b08048", "#d8a868"]), nuts * 0.9)
    return rgb, 0.4 + nuts * 0.4 + noise(75, 12, 2) * 0.2


def broth():
    base = ramp(noise(81, 3, 4), ["#c86a1e", "#e08a30", "#f0a848"])
    h, v = stamp(N, 180, 9, 9, 82, jitter=0.6)
    ring = np.clip(h * 3, 0, 1) - np.clip(h * 3 - 1.5, 0, 1)
    rgb = blend(base, col("#ffd27a"), np.clip(h * 1.5, 0, 1) * 0.6)
    rgb = blend(rgb, col("#fff2c0"), np.clip((h - 0.75) * 6, 0, 1))
    return rgb, 0.5 + ring * 0.2


def fish_scales(base=("#3a6a98", "#7ab0d8", "#d8ecf8")):
    """Overlapping scales: rows of arcs with a light rim, darker back to lighter belly (v)."""
    xx, yy = grid()
    sc = N / 16
    row = np.floor(yy / (sc * 0.5))
    xo = xx + (row % 2) * sc * 0.5
    cx = (np.floor(xo / sc) + 0.5) * sc
    cy = (row + 1) * sc * 0.5
    d = np.sqrt((xo - cx) ** 2 + (yy - cy) ** 2) / (sc * 0.55)
    rim = np.clip(1 - np.abs(d - 0.92) * 9, 0, 1)
    rgb = ramp(0.35 + 0.5 * noise(281, 3, 3), list(base))
    rgb = rgb * (0.82 + 0.25 * np.clip(1 - d, 0, 1))[..., None]
    rgb = blend(rgb, col("#ffffff"), rim * 0.45)
    return np.clip(rgb, 0, 1), np.clip(1 - d, 0, 1) * 0.6 + rim * 0.3


def mushroom_cap():
    base = ramp(noise(91, 3, 4), ["#8a9a18", "#b4c41e", "#d0d830"])
    blot = np.clip((noise(92, 5, 4) - 0.5) * 4, 0, 1)
    rgb = blend(base, col("#5e6a10"), blot * 0.45)
    rgb *= (0.92 + 0.12 * noise(93, 20, 2))[..., None]
    return rgb, noise(94, 10, 3)


def mushroom_stem():
    xx, yy = grid()
    fib = lines((0, 1), 24, 1.5, 95, 0) * 0.25
    rgb = ramp(noise(96, 4, 3) * 0.6 + fib, ["#e8dcc0", "#f6eeda", "#fffaf0"])
    return rgb, fib + noise(97, 12, 2) * 0.3


# ------------------------------------------------------------------------------------------- materials

def wood(cols=("#7a4a22", "#a0682e", "#c4884a", "#d8a464"), seed=101):
    g = T.detail_wood(seed).astype(np.float32) if T.D == N else None
    if g is None:
        old = T.D
        T.D = N
        g = T.detail_wood(seed).astype(np.float32)
        T.D = old
    rgb = ramp(g, list(cols))
    return rgb, g


def planks(cols=("#6a3c1a", "#94602e", "#b8823e", "#d29c58"), count=5, seed=111):
    """Vertical planks (x) with grain along y, dark seams and nail holes."""
    xx, yy = grid()
    w = N / count
    idx = np.floor(xx / w)
    lx = xx - idx * w
    rnd = np.random.RandomState(seed)
    off = rnd.rand(count + 1)[idx.astype(int)] * N
    shade_k = (0.85 + 0.3 * rnd.rand(count + 1))[idx.astype(int)]
    warp = noise(seed + 1, 2, 3) * 10
    grain = np.abs(np.sin(((xx + warp * 2) / N * 2 * np.pi * 3) + np.sin((yy + off) / N * 2 * np.pi) * 1.5))
    grain = grain * 0.4 + noise(seed + 2, 16, 3) * 0.5
    streak = np.clip(1 - np.abs(np.sin((xx * 1.0 + warp * 3) / N * 2 * np.pi * 21)) * 7, 0, 1) * 0.3
    g = np.clip(grain - streak, 0, 1)
    rgb = ramp(g, list(cols)) * shade_k[..., None]
    seam = np.clip(1 - np.minimum(lx, w - lx) / 3.0, 0, 1)
    rgb = blend(rgb, col("#2a160a"), seam * 0.9)
    h = g * 0.4 + 0.5 - seam * 0.5
    return rgb, h


def painted_metal(base_hex, seed=121, chips=0.35, dark=None):
    base = col(base_hex)
    v = noise(seed, 4, 4)
    rgb = base[None, None] * (0.88 + 0.2 * v)[..., None]
    if dark:
        rgb = blend(rgb, col(dark), np.clip((noise(seed + 1, 3, 3) - 0.55) * 3, 0, 1) * 0.5)
    chip = np.clip((noise(seed + 2, 10, 4) - (1 - chips * 0.5)) * 12, 0, 1)
    metal = ramp(noise(seed + 3, 30, 2), ["#6a7280", "#a8b0bc"])
    rgb = blend(rgb, metal, chip)
    scratch = np.clip(T.detail_metal(seed + 4) if T.D == N else _detail_metal_n(seed + 4), 0, 1)
    rgb *= (0.92 + 0.16 * scratch)[..., None]
    return rgb, 0.6 - chip * 0.3 + scratch * 0.1


def _detail_metal_n(seed):
    old = T.D
    T.D = N
    try:
        return T.detail_metal(seed).astype(np.float32)
    finally:
        T.D = old


def steel(cols=("#4c5562", "#7c8794", "#aab3be", "#d0d6de"), seed=131):
    g = _detail_metal_n(seed)
    rgb = ramp(g * 0.8 + noise(seed + 1, 3, 3) * 0.3, list(cols))
    return rgb, g


def rubber(base_hex="#2c2f36", seed=141):
    v = noise(seed, 24, 3)
    rgb = col(base_hex)[None, None] * (0.85 + 0.3 * v)[..., None]
    return rgb, v


def fabric(base_hex, seed=151, weave=96, stripe=None):
    xx, yy = grid()
    wx = np.sin(xx / N * 2 * np.pi * weave) * 0.5 + 0.5
    wy = np.sin(yy / N * 2 * np.pi * weave) * 0.5 + 0.5
    check = ((np.floor(xx / N * weave) + np.floor(yy / N * weave)) % 2)
    w = np.where(check > 0, wx, wy)
    rgb = col(base_hex)[None, None] * (0.78 + 0.28 * w + 0.12 * (noise(seed, 6, 3) - 0.5))[..., None]
    if stripe is not None:
        rgb = blend(rgb, rgb * 0.8, stripe)
    return rgb, w * 0.6 + noise(seed + 1, 8, 2) * 0.3


def leather(base_hex="#7a4a26", seed=161):
    d1, edge, cid = T.voronoi(N, 420, seed)
    crease = np.clip(1 - edge / 1.6, 0, 1) * 0.45
    v = noise(seed + 1, 6, 4)
    rgb = col(base_hex)[None, None] * (0.8 + 0.35 * v)[..., None]
    rgb = blend(rgb, rgb * 0.55, crease * 0.6)
    worn = np.clip((noise(seed + 2, 4, 4) - 0.6) * 4, 0, 1)
    rgb = blend(rgb, rgb * 1.35, worn * 0.5)
    return np.clip(rgb, 0, 1), 0.6 - crease * 0.4 + v * 0.1


def hazard(seed=171, cols=("#ffc21a", "#1e1e22"), stripes=8):
    xx, yy = grid()
    t = (xx + yy) / N * stripes
    s = (np.floor(t) % 2).astype(np.float32)
    rgb = blend(col(cols[0])[None, None].repeat(N, 0).repeat(N, 1), col(cols[1]), s)
    wear = np.clip((noise(seed, 8, 4) - 0.62) * 8, 0, 1)
    rgb = blend(rgb, ramp(noise(seed + 1, 30, 2), ["#5a6270", "#8a929e"]), wear)
    rgb *= (0.9 + 0.15 * noise(seed + 2, 20, 2))[..., None]
    return rgb, 0.6 - wear * 0.3


def foil(base_hex="#6a2aa8", seed=181):
    d1, edge, cid = T.voronoi(N, 90, seed)
    facet = np.random.RandomState(seed).rand(90)[cid]
    crink = np.clip(1 - edge / 2.0, 0, 1)
    rgb = col(base_hex)[None, None] * (0.7 + 0.5 * facet)[..., None]
    rgb = blend(rgb, rgb * 1.6, crink * 0.5)
    return np.clip(rgb, 0, 1), facet * 0.6 + crink * 0.4


def ceramic(base_hex="#f4f1ea", seed=191):
    v = noise(seed, 6, 3)
    rgb = col(base_hex)[None, None] * (0.95 + 0.07 * v)[..., None]
    rnd = np.random.RandomState(seed)
    sp = np.clip(blur((rnd.rand(N, N) > 0.997).astype(np.float32), 1, 1) * 5, 0, 1)
    rgb = blend(rgb, col("#8a8070"), sp * 0.5)
    return rgb, v * 0.2


def bowl_band():
    """Tube-projected pattern for the soup bowl: blue rim band + wave pattern (u around, v up)."""
    xx, yy = grid()
    u, v = xx / N, 1 - yy / N
    rgb, h = ceramic()
    band = ((v > 0.80) & (v < 0.86)) | ((v > 0.30) & (v < 0.33))
    waves = np.abs(v - 0.56 - 0.08 * np.sin(u * 2 * np.pi * 10)) < 0.025
    waves2 = np.abs(v - 0.46 - 0.08 * np.sin(u * 2 * np.pi * 10 + np.pi)) < 0.02
    m = np.clip(blur((band | waves | waves2).astype(np.float32), 1, 1) * 1.3, 0, 1)
    rgb = blend(rgb, col("#1f56b8") * (0.9 + 0.15 * noise(198, 12, 2))[..., None], m)
    return rgb, h + m * 0.15


def bandage_fabric():
    rgb, h = fabric("#efd6b8", 201, weave=110)
    xx, yy = grid()
    holes = ((xx % 32 - 16) ** 2 + (yy % 32 - 16) ** 2) < 7
    rgb = blend(rgb, col("#b48e6c"), blur(holes.astype(np.float32), 1, 1) * 0.9)
    return rgb, h - holes * 0.5


def parchment(seed=211):
    base = ramp(noise(seed, 3, 5), ["#d8b77a", "#ead2a0", "#f6e8c4"])
    stain = np.clip((noise(seed + 1, 4, 4) - 0.55) * 3, 0, 1)
    rgb = blend(base, col("#b08a4a"), stain * 0.5)
    fib = lines((1, 5), 80, 1.0, seed + 2, 4) * 0.08
    rgb *= (0.96 + fib)[..., None]
    return rgb, noise(seed + 3, 10, 3) * 0.5


def stripes_rainbow(seed=221, count=6):
    xx, yy = grid()
    cols = [col(c) for c in ("#ff3a3a", "#ff9a1a", "#ffe02a", "#3ccf4a", "#2a9aff", "#a24aff")]
    t = ((xx * 1 + yy * 1) / N * count) % len(cols)
    i = np.floor(t).astype(int)
    rgb = np.array(cols, np.float32)[i]
    edge = np.clip(1 - np.abs(t - np.round(t)) * 20, 0, 1)
    rgb = blend(rgb, col("#ffffff"), edge * 0.6)
    rgb *= (0.92 + 0.12 * noise(seed, 16, 2))[..., None]
    return rgb, 0.5 + edge * 0.1


def hexgrid(seed=231, scale=10):
    """Energy lattice for the repulse dome: cyan lines (color) and line strength (height, also used as glow)."""
    xx, yy = grid()
    s = N / scale
    q = xx / s
    r = yy / s
    # three families of lines at 60 degrees: a triangle lattice of energy lines
    a = np.abs(np.sin(np.pi * q * 1.0))
    b = np.abs(np.sin(np.pi * (q * 0.5 + r * np.sqrt(3) / 2)))
    c = np.abs(np.sin(np.pi * (q * 0.5 - r * np.sqrt(3) / 2)))
    m = np.minimum(np.minimum(a, b), c)
    line = np.clip(1 - m * 7, 0, 1)
    glow = np.clip(1 - m * 2.5, 0, 1) * 0.35
    rgb = blend(np.repeat(np.repeat(col("#2a8cff")[None, None], N, 0), N, 1), col("#c8f4ff"), line)
    return rgb, np.clip(line + glow, 0, 1)


# ------------------------------------------------------------------------------------------- decals (PIL)

def _pil():
    from PIL import Image, ImageDraw, ImageFont, ImageFilter
    return Image, ImageDraw, ImageFont, ImageFilter


def font(size, serif=False):
    _, _, ImageFont, _ = _pil()
    try:
        return ImageFont.truetype(FONT_SERIF if serif else FONT_BOLD, size)
    except Exception:
        return ImageFont.load_default()


def to_np(im):
    return np.asarray(im.convert("RGBA"), np.float32) / 255.0


def scroll_sheet(w=1024, h=640):
    """Parchment page with an ink explosion sketch and runes, burnt edges (RGBA)."""
    Image, ImageDraw, ImageFont, ImageFilter = _pil()
    rgb, _ = parchment()
    base = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).resize((w, h))
    d = ImageDraw.Draw(base, "RGBA")
    import math
    import random
    rnd = random.Random(7)
    cx, cy = w * 0.36, h * 0.46
    for i in range(22):           # explosion rays
        a = i / 22 * 2 * math.pi + rnd.uniform(-0.08, 0.08)
        r0, r1 = 34, rnd.uniform(110, 190)
        d.line([(cx + math.cos(a) * r0, cy + math.sin(a) * r0), (cx + math.cos(a) * r1, cy + math.sin(a) * r1)],
               fill=(70, 34, 14, 230), width=rnd.choice((5, 7, 9)))
    pts = []
    for i in range(24):
        a = i / 24 * 2 * math.pi
        r = 70 if i % 2 else 40
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    d.polygon(pts, fill=(200, 60, 20, 200), outline=(70, 34, 14, 255))
    d.ellipse([cx - 22, cy - 22, cx + 22, cy + 22], fill=(255, 200, 60, 230))
    d.text((w * 0.64, h * 0.22), "KA-", font=font(78, True), fill=(80, 36, 14, 235))
    d.text((w * 0.64, h * 0.42), "BOOM", font=font(78, True), fill=(80, 36, 14, 235))
    for i in range(4):             # scribbled lines of text
        y = h * 0.66 + i * 34
        x = w * 0.6
        while x < w * 0.92:
            ln = rnd.uniform(20, 60)
            d.line([(x, y), (x + ln, y + rnd.uniform(-2, 2))], fill=(90, 50, 20, 200), width=6)
            x += ln + 14
    arr = to_np(base)
    # burnt, ragged edges
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    nz = np.asarray(Image.fromarray((noise(241, 6, 4) * 255).astype(np.uint8)).resize((w, h)), np.float32) / 255
    e = edge - nz * 40
    burn = np.clip(1 - (e - 10) / 30, 0, 1)
    arr[..., :3] = blend(arr[..., :3], np.array([0.35, 0.18, 0.06], np.float32), burn * 0.85)
    arr[..., 3] = np.clip((e + 2) / 4, 0, 1)
    return arr


def wrapper_label(w=1024, h=512):
    """Protein bar wrapper: purple foil with a yellow lightning badge and PROTEIN lettering."""
    Image, ImageDraw, ImageFont, ImageFilter = _pil()
    rgb, _ = foil("#6a24b0", 251)
    base = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).resize((w, h))
    d = ImageDraw.Draw(base, "RGBA")
    d.rectangle([0, h * 0.08, w, h * 0.16], fill=(255, 200, 40, 255))
    d.rectangle([0, h * 0.84, w, h * 0.92], fill=(255, 200, 40, 255))
    cx, cy = w * 0.26, h * 0.5
    d.ellipse([cx - 120, cy - 120, cx + 120, cy + 120], fill=(255, 210, 40, 255), outline=(120, 40, 10, 255), width=10)
    bolt = [(cx + 20, cy - 95), (cx - 55, cy + 15), (cx - 5, cy + 15), (cx - 25, cy + 95), (cx + 55, cy - 20),
            (cx + 5, cy - 20)]
    d.polygon(bolt, fill=(232, 60, 30, 255), outline=(90, 20, 10, 255))
    f = font(120)
    d.text((w * 0.47 + 5, h * 0.27 + 6), "PRO", font=f, fill=(40, 10, 60, 200))
    d.text((w * 0.47, h * 0.27), "PRO", font=f, fill=(255, 255, 255, 255))
    d.text((w * 0.48, h * 0.56), "POWER BAR", font=font(54), fill=(255, 210, 60, 255))
    return to_np(base)


def rising_sun(w=2400, h=110):
    """Hachimaki headband (wraps the whole head: ~24:1): white cloth with a red sun disc in the middle."""
    Image, ImageDraw, ImageFont, ImageFilter = _pil()
    rgb, _ = fabric("#f6f3ee", 261, weave=80)
    tile = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).resize((h * 2, h * 2))
    base = Image.new("RGB", (w, h))
    for x in range(0, w, h * 2):
        base.paste(tile, (x, -h // 2))
    d = ImageDraw.Draw(base, "RGBA")
    cx, cy, r = w * 0.5, h * 0.5, h * 0.42
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(222, 30, 40, 255))
    d.line([(0, 3), (w, 3)], fill=(200, 190, 180, 255), width=4)
    d.line([(0, h - 4), (w, h - 4)], fill=(200, 190, 180, 255), width=4)
    return to_np(base)


def save(name, arr):
    """Write arr (h,w,3|4 or h,w gray) to OUT/name.png and return the path."""
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name + ".png")
    a = np.asarray(arr, np.float32)
    if a.ndim == 2:
        a = np.repeat(a[..., None], 3, axis=-1)
    if a.shape[-1] == 3:
        a = np.concatenate([a, np.ones(a.shape[:2] + (1,), np.float32)], axis=-1)
    C.write_png(p, a)
    return p


_cache = {}


def get(name, fn, *args, **kw):
    """Generate once per run: returns (rgb_path, height_path)."""
    if name in _cache:
        return _cache[name]
    rgb, h = fn(*args, **kw)
    res = (save(name, rgb), save(name + "_h", np.clip(h, 0, 1)))
    _cache[name] = res
    return res


def get_rgba(name, fn, *args, **kw):
    if name in _cache:
        return _cache[name]
    p = save(name, fn(*args, **kw))
    _cache[name] = p
    return p
