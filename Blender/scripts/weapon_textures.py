"""Painted, seamless 512x512 surface textures for the weapon models (numpy only, deterministic).

One tile covers 1x1 Blender/Unity unit. In Unity the weapon FBX's UV0 is the planar object-space (x, z) of each vertex
(common._bake_vertex_data), so a tile maps 1:1 onto the side of the weapon the game camera sees; in the Blender icon
renders the same image is box-projected. Every texture is fully colored (the material color is white), so painted
chips can show bare metal, stripes keep their two colors, etc.

Output: 512px masters in a temp cache (used by the icon renders) and, for the textures the exported models use, 256px
copies in Resources/Models/Weapons/Textures/<name>.png (the FBX references them by relative path; Unity finds them next
to the models). Each function returns float RGB (N, N, 3) in 0..1 (sRGB values)."""
import os
import tempfile

import numpy as np

import common as C
import textures as T

N = 512
OUT = os.path.join(C.MODELS, "Weapons", "Textures")
CACHE = os.path.join(tempfile.gettempdir(), "cpw_weapon_tex")
SHIP = 256

_yy, _xx = np.mgrid[0:N, 0:N].astype(np.float32)
U = _xx / N   # 0..1 across (game x)
Vv = _yy / N  # 0..1 down (game z)


def rgb(c):
    if isinstance(c, str):
        c = C.PALETTE.get(c, c)
        return np.array(C.hex_rgb(c), np.float32)
    return np.array(c, np.float32)


def solid(c):
    return np.broadcast_to(rgb(c), (N, N, 3)).astype(np.float32).copy()


def lum(img):
    return img[..., 0] * 0.3 + img[..., 1] * 0.59 + img[..., 2] * 0.11


def mixc(a, b, m):
    return a * (1 - m[..., None]) + b * m[..., None]


def tint(img, f):
    return np.clip(img * (f[..., None] if np.ndim(f) == 2 else f), 0, 1)


def noise(seed, cells=8, octaves=3):
    return T.fbm(N, seed, octaves, cells)


def _stamp_lines(seed, count, length=(10, 60), angle=(-0.5, 0.5), width=1.0, alpha=(0.4, 1.0)):
    """Mask of short anti-aliased scratches (wraps around the tile)."""
    rnd = np.random.RandomState(seed)
    m = np.zeros((N, N), np.float32)
    for _ in range(count):
        x0, y0 = rnd.rand(2) * N
        ln = rnd.uniform(*length)
        a = rnd.uniform(*angle)
        al = rnd.uniform(*alpha)
        steps = int(ln * 2) + 1
        t = np.linspace(0, 1, steps)
        # slight curve
        bend = rnd.uniform(-0.15, 0.15)
        xs = x0 + np.cos(a) * ln * t - np.sin(a) * bend * ln * t * (1 - t)
        ys = y0 + np.sin(a) * ln * t + np.cos(a) * bend * ln * t * (1 - t)
        fade = np.sin(np.pi * t) ** 0.5 * al
        for ox, oy, w in ((0, 0, 1.0), (0.5, 0.5, 0.5 * width), (-0.5, -0.5, 0.5 * width)):
            xi = np.round(xs + ox).astype(int) % N
            yi = np.round(ys + oy).astype(int) % N
            np.maximum.at(m, (yi, xi), fade * w)
    return np.clip(T.wrap_blur(m, 1, 1) * 2.2, 0, 1)


def _spots(seed, count, rmin, rmax, soft=1.5):
    """Mask of round blobs (wrapping)."""
    rnd = np.random.RandomState(seed)
    m = np.zeros((N, N), np.float32)
    for _ in range(count):
        cx, cy = rnd.rand(2) * N
        r = rnd.uniform(rmin, rmax)
        dx = np.abs(_xx - cx)
        dx = np.minimum(dx, N - dx)
        dy = np.abs(_yy - cy)
        dy = np.minimum(dy, N - dy)
        d = np.sqrt(dx * dx + dy * dy)
        m = np.maximum(m, np.clip((r - d) / soft, 0, 1))
    return m


def wear(img, seed, scratches=60, chips=0.5, grime=0.5, metal="#7d8590", edge_dark=0.35):
    """Weathering for painted surfaces: grime, chipped paint showing metal, light scratches."""
    g = noise(seed + 1, 3, 4)
    img = tint(img, 1.0 - grime * 0.22 * np.clip((g - 0.35) * 2.2, 0, 1))
    fine = noise(seed + 2, 32, 2)
    img = tint(img, 0.96 + fine * 0.08)
    if chips > 0:
        n = noise(seed + 3, 40, 3)
        thr = 0.79 - chips * 0.06
        chip = np.clip((n - thr) * 25, 0, 1)
        rim = np.clip((n - thr + 0.035) * 25, 0, 1) - chip
        img = tint(img, 1.0 - rim * edge_dark)
        mc = solid(metal) * (0.85 + 0.25 * noise(seed + 4, 24, 2))[..., None]
        img = mixc(img, mc, chip)
    if scratches:
        s = _stamp_lines(seed + 5, scratches, (6, 30), (-0.6, 0.6))
        img = mixc(img, np.clip(img * 0.45 + 0.55, 0, 1), s * 0.55)
    return np.clip(img, 0, 1)


# ------------------------------------------------------------------------------------------------ kinds

def paint(col, seed=1, chips=0.45, scratches=50, grime=0.5, metal="#7d8590"):
    img = solid(col)
    m = noise(seed, 4, 3)
    img = tint(img, 0.95 + m * 0.1)
    return wear(img, seed, scratches, chips, grime, metal)


def plastic(col, seed=2):
    """Toy plastic: clean, slight mottling and a few fine scuffs."""
    img = solid(col)
    img = tint(img, 0.97 + noise(seed, 6, 3) * 0.06)
    s = _stamp_lines(seed + 9, 25, (6, 26), (-1.5, 1.5))
    return np.clip(mixc(img, np.clip(img * 0.6 + 0.4, 0, 1), s * 0.3), 0, 1)


def metal(col, seed=3, brushed=0.6, scratches=70, rust=0.0):
    img = solid(col)
    # brushed streaks along x: noise stretched horizontally
    rnd = np.random.RandomState(seed)
    streak = np.zeros((N, N), np.float32)
    for k, amp in ((256, 0.5), (128, 0.3), (64, 0.2)):
        line = rnd.rand(k).astype(np.float32)
        idx = (np.arange(N) * k // N) % k
        streak += line[idx][:, None] * amp
    streak = T.wrap_blur(np.broadcast_to(streak, (N, N)).copy(), 1, 1)
    n = noise(seed + 1, 5, 4)
    img = tint(img, 0.86 + brushed * 0.22 * streak + 0.12 * n)
    s = _stamp_lines(seed + 2, scratches, (10, 70), (-0.35, 0.35))
    img = mixc(img, np.clip(img * 0.5 + 0.5, 0, 1), s * 0.5)
    g = noise(seed + 3, 3, 4)
    img = tint(img, 1.0 - 0.18 * np.clip((g - 0.45) * 3, 0, 1))
    if rust > 0:
        r = noise(seed + 4, 8, 4)
        rm = np.clip((r - (1 - rust * 0.4)) * 8, 0, 1)
        img = mixc(img, solid("#8a4a24") * (0.8 + 0.4 * noise(seed + 5, 40, 2))[..., None], rm * 0.8)
    return np.clip(img, 0, 1)


def gunmetal(col="#3c4350", seed=4):
    """Blued steel: dark, satin, edge-worn lighter streaks."""
    img = metal(col, seed, brushed=0.5, scratches=90)
    hl = noise(seed + 7, 6, 3)
    return np.clip(tint(img, 0.92 + hl * 0.16), 0, 1)


def wood(col="#a4683a", seed=5, dark=0.55, rings=7.0):
    """Varnished wood, grain along x."""
    base = rgb(col)
    warp = noise(seed, 3, 3) * 0.35 + noise(seed + 1, 8, 2) * 0.08
    v = Vv + warp
    g = np.sin((v * rings + np.sin(U * 2 * np.pi * 2 + seed) * 0.12) * 2 * np.pi)
    grain = (g * 0.5 + 0.5) ** 3
    fibers = noise(seed + 2, 64, 2)
    fib = np.clip(np.broadcast_to(T.value_noise(N, 128, seed + 3).mean(axis=1, keepdims=True), (N, N)), 0, 1)
    f = 1.0 - dark * 0.45 * grain - 0.12 * (fibers - 0.5) - 0.18 * (fib - 0.5)
    img = solid(base) * f[..., None]
    # pores
    pores = _stamp_lines(seed + 4, 220, (3, 12), (-0.08, 0.08), alpha=(0.3, 0.7))
    img = tint(img, 1 - pores * 0.35)
    # knots
    rnd = np.random.RandomState(seed)
    for _ in range(2):
        kx, ky = rnd.rand(2) * N
        dx = np.minimum(np.abs(_xx - kx), N - np.abs(_xx - kx))
        dy = np.minimum(np.abs(_yy - ky), N - np.abs(_yy - ky))
        d = np.sqrt(dx * dx * 0.25 + dy * dy * 2.5)
        ring = (np.sin(d * 0.8) * 0.5 + 0.5) * np.clip(1 - d / 26, 0, 1)
        img = tint(img, 1 - ring * 0.45)
    img = wear(img, seed + 10, scratches=25, chips=0.0, grime=0.4)
    return np.clip(img, 0, 1)


def rubber(col="#2a2d33", seed=6, pattern="knurl", scale=28):
    """Grip rubber: diamond knurl or horizontal ribs, matte."""
    img = solid(col)
    if pattern == "knurl":
        a = np.sin((U + Vv) * np.pi * scale)
        b = np.sin((U - Vv) * np.pi * scale)
        bump = np.clip(np.abs(a * b) * 1.4, 0, 1)
        f = 0.78 + 0.32 * bump
    elif pattern == "ribs":
        r = np.sin(Vv * np.pi * 2 * scale * 0.5) * 0.5 + 0.5
        f = 0.75 + 0.35 * r ** 2
    else:
        f = np.ones((N, N), np.float32)
    img = tint(img, f * (0.94 + 0.12 * noise(seed, 8, 3)))
    dust = _spots(seed + 1, 40, 0.6, 1.6, 1.0)
    img = mixc(img, np.clip(img + 0.18, 0, 1), dust * 0.4)
    return np.clip(img, 0, 1)


def stripes(c1, c2, seed=7, period=8, diagonal=True, chips=0.5):
    """Hazard / candy stripes (period stripes per tile, integer so it tiles)."""
    t = (U + Vv) if diagonal else U
    s = (np.sin(t * np.pi * 2 * period) > 0).astype(np.float32)
    s = T.wrap_blur(s, 1, 1)
    img = mixc(solid(c1), solid(c2), s)
    return paint_over(img, seed, chips)


def bands(cols, seed=8, period=4, chips=0.3, zig=0.0):
    """Horizontal bands of colors along v (optionally zig-zag)."""
    k = len(cols)
    v = Vv * period * k + (np.abs(((U * period * 8) % 2) - 1) - 0.5) * zig
    idx = np.floor(v).astype(int) % k
    img = np.array([rgb(c) for c in cols], np.float32)[idx]
    img = np.stack([T.wrap_blur(img[..., i], 1, 1) for i in range(3)], -1)
    return paint_over(img, seed, chips)


def paint_over(img, seed, chips=0.4):
    img = tint(img, 0.95 + noise(seed, 4, 3) * 0.1)
    return wear(img, seed, 40, chips, 0.45)


def panels(col, seed=9, line="#000000", grid=(5, 4), rivets=True, chips=0.25):
    """Sci-fi panelling: seams every 1/grid tile (with random splits), rivets, light wear."""
    img = paint(col, seed, chips=chips, scratches=35, grime=0.35)
    rnd = np.random.RandomState(seed)
    gx, gy = grid
    m = np.zeros((N, N), np.float32)
    cw, ch = N // gx, N // gy
    for j in range(gy):
        off = rnd.randint(0, cw)
        y = j * ch
        m[y:y + 2, :] = 1
        for i in range(gx):
            x = (i * cw + off) % N
            m[y:y + ch, x:x + 2] = 1
    m = T.wrap_blur(m, 1, 1)
    img = tint(img, 1 - m * 0.55)
    hl = np.roll(np.roll(m, 2, 0), 2, 1)
    img = mixc(img, np.clip(img * 1.25 + 0.05, 0, 1), np.clip(hl - m, 0, 1) * 0.6)
    if rivets:
        rv = np.zeros((N, N), np.float32)
        for j in range(gy):
            for i in range(gx * 2):
                cx, cy = (i * cw // 2 + cw // 4) % N, (j * ch + 9) % N
                d = np.sqrt((_xx - cx) ** 2 + (_yy - cy) ** 2)
                rv = np.maximum(rv, np.clip(4.0 - d, 0, 1))
        img = mixc(img, np.clip(img * 1.35 + 0.08, 0, 1), rv * 0.8)
        img = tint(img, 1 - np.clip(np.roll(rv, -2, 0) - rv, 0, 1) * 0.5)
    return np.clip(img, 0, 1)


def glow(col, seed=10, swirl=1.0):
    """Energy: bright core with swirly lighter streaks (Blender emission; Unity draws Glow* unlit)."""
    n = noise(seed, 4, 4)
    w = np.sin((U * 3 + n * 2.5 * swirl) * 2 * np.pi) * 0.5 + 0.5
    base = rgb(col)
    hot = np.clip(base * 0.4 + 0.75, 0, 1)
    img = mixc(solid(base), np.broadcast_to(hot, (N, N, 3)).copy(), w ** 3 * 0.8)
    return np.clip(img, 0, 1)


def glass(col, seed=11):
    img = solid(col)
    n = noise(seed, 3, 3)
    img = mixc(img, np.ones((N, N, 3), np.float32), np.clip((n - 0.55) * 3, 0, 1) * 0.35)
    s = _stamp_lines(seed + 1, 15, (10, 40), (-1.5, 1.5), alpha=(0.2, 0.5))
    return np.clip(mixc(img, np.ones((N, N, 3), np.float32), s * 0.4), 0, 1)


def fabric(col, seed=12, weave=96):
    """Burlap / canvas weave."""
    a = np.sin(U * np.pi * weave) ** 2
    b = np.sin(Vv * np.pi * weave) ** 2
    checker = (np.sin(U * np.pi * weave / 2) * np.sin(Vv * np.pi * weave / 2) > 0)
    f = np.where(checker, 0.75 + 0.3 * a, 0.75 + 0.3 * b)
    img = tint(solid(col), f * (0.9 + 0.2 * noise(seed, 16, 3)))
    fuzz = _stamp_lines(seed + 1, 300, (3, 10), (-3, 3), alpha=(0.2, 0.6))
    img = mixc(img, np.clip(img * 1.3, 0, 1), fuzz * 0.4)
    g = noise(seed + 2, 3, 4)
    return np.clip(tint(img, 1 - 0.2 * np.clip((g - 0.4) * 2, 0, 1)), 0, 1)


def stone(col="#8a8a86", seed=13, cracks=0.5, specks=True):
    n = noise(seed, 6, 5)
    img = tint(solid(col), 0.72 + n * 0.5)
    d1, edge, cid = T.voronoi(N, 18, seed)
    cr = np.clip(1.0 - edge / 2.2, 0, 1) * np.clip((noise(seed + 1, 6, 3) - 0.35) * 4, 0, 1) * cracks
    img = tint(img, 1 - cr * 0.6)
    if specks:
        sp = _spots(seed + 2, 260, 0.6, 1.8, 0.8)
        img = mixc(img, np.clip(img * 1.4, 0, 1), sp * 0.5)
        sp2 = _spots(seed + 3, 200, 0.6, 1.5, 0.8)
        img = tint(img, 1 - sp2 * 0.35)
    return np.clip(img, 0, 1)


def cinder(seed=14):
    """Black volcanic rock with glowing orange cracks."""
    img = stone("#4a403c", seed, cracks=0.0)
    d1, edge, cid = T.voronoi(N, 14, seed + 5)
    lava = np.clip(1.0 - edge / 6.0, 0, 1) ** 0.7
    hot = mixc(solid("#ff5a10"), solid("#ffd040"), np.clip(1.0 - edge / 1.6, 0, 1))
    return np.clip(mixc(img, hot, lava), 0, 1)


def snow(seed=15):
    n = noise(seed, 6, 5)
    img = mixc(solid("#b8d4ec"), solid("#ffffff"), np.clip(n * 1.6 - 0.3, 0, 1))
    d1, edge, cid = T.voronoi(N, 60, seed + 2)
    img = tint(img, 0.96 + 0.04 * np.clip(edge / 6.0, 0, 1))
    sp = _spots(seed + 1, 300, 0.5, 1.6, 0.6)
    img = mixc(img, solid("#7fb0dc"), sp * 0.45)
    sp2 = _spots(seed + 3, 200, 0.5, 1.2, 0.6)
    img = mixc(img, solid("#ffffff"), sp2)
    return np.clip(img, 0, 1)


def slime(col, seed=16):
    n = noise(seed, 4, 4)
    img = tint(solid(col), 0.85 + n * 0.3)
    d1, edge, cid = T.voronoi(N, 40, seed)
    bub = np.clip(1 - d1 / 10, 0, 1)
    ring = np.clip(1 - np.abs(d1 - 8) / 1.5, 0, 1) * (np.random.RandomState(seed).rand(40)[cid] > 0.55)
    img = mixc(img, np.clip(img * 1.35 + 0.1, 0, 1), ring * 0.7 + bub * 0.1)
    return np.clip(img, 0, 1)


def latex(col, seed=17):
    """Balloon rubber: smooth with faint stretch streaks."""
    img = tint(solid(col), 0.94 + noise(seed, 3, 3) * 0.12)
    s = _stamp_lines(seed + 1, 40, (30, 120), (-0.2, 0.2), alpha=(0.1, 0.3))
    return np.clip(mixc(img, np.clip(img * 1.3, 0, 1), s * 0.5), 0, 1)


def peel(col, seed=18):
    """Citrus peel: dense dimples."""
    d1, edge, cid = T.voronoi(N, 900, seed)
    dimple = np.clip(d1 / 6.0, 0, 1)
    img = tint(solid(col), 0.82 + dimple * 0.22 + noise(seed, 5, 3) * 0.1)
    return np.clip(img, 0, 1)


def paper(col, seed=19, print_col=None):
    """Wrapping paper (dynamite): fibers, creases, faint print bands."""
    img = tint(solid(col), 0.93 + noise(seed, 32, 2) * 0.1)
    cr = _stamp_lines(seed + 1, 40, (40, 140), (-1.6, 1.6), alpha=(0.2, 0.5))
    img = tint(img, 1 - cr * 0.25)
    if print_col:
        b = ((Vv * 8) % 1 < 0.06).astype(np.float32)
        img = mixc(img, solid(print_col), T.wrap_blur(b, 1, 1) * 0.6)
    return wear(img, seed + 3, 10, 0.0, 0.5)


def chocolate(seed=20):
    n = noise(seed, 4, 4)
    img = mixc(solid("#4a2412"), solid("#7a4422"), n)
    s = _stamp_lines(seed + 1, 30, (20, 80), (-0.4, 0.4), alpha=(0.2, 0.5))
    return np.clip(mixc(img, solid("#9a5a30"), s * 0.4), 0, 1)


def foil(col="#f2b632", seed=21):
    """Crinkled metal foil: facets of different brightness."""
    d1, edge, cid = T.voronoi(N, 160, seed)
    rnd = np.random.RandomState(seed)
    br = (0.7 + rnd.rand(160) * 0.55).astype(np.float32)[cid]
    img = tint(solid(col), br)
    img = tint(img, 1 - np.clip(1 - edge / 1.5, 0, 1) * 0.25)
    return np.clip(img, 0, 1)


def fur(col, seed=22, stripe=None):
    st = _stamp_lines(seed, 2600, (4, 14), (-1.9, -1.2), alpha=(0.3, 1.0))
    img = tint(solid(col), 0.8 + 0.2 * noise(seed + 1, 24, 2))
    img = mixc(img, np.clip(img * 1.35, 0, 1), st * 0.5)
    st2 = _stamp_lines(seed + 2, 1800, (4, 12), (-1.9, -1.2), alpha=(0.3, 1.0))
    img = tint(img, 1 - st2 * 0.35)
    if stripe is not None:
        sm = (np.sin((U * 6 + noise(seed + 3, 4, 2) * 0.6) * 2 * np.pi) > 0.55).astype(np.float32)
        img = mixc(img, solid(stripe), T.wrap_blur(sm, 2, 1) * 0.8)
    return np.clip(img, 0, 1)


def egg(cols, seed=23):
    """Easter egg paint: zig-zag bands plus polka dots."""
    img = bands(cols[:3], seed, period=3, chips=0.0, zig=0.5)
    dots = _spots(seed + 1, 30, 7, 11, 1.2)
    img = mixc(img, solid(cols[3] if len(cols) > 3 else "#ffffff"), dots)
    return np.clip(img, 0, 1)


def stars(col, star_cols, seed=24):
    """Painted fireworks tube: base with stars."""
    img = paint(col, seed, chips=0.2, scratches=20)
    rnd = np.random.RandomState(seed)
    for k in range(14):
        cx, cy = rnd.rand(2) * N
        r = rnd.uniform(14, 24)
        rot = rnd.uniform(0, 2 * np.pi)
        dx = (_xx - cx + N / 2) % N - N / 2
        dy = (_yy - cy + N / 2) % N - N / 2
        a = np.arctan2(dy, dx) + rot
        d = np.sqrt(dx * dx + dy * dy)
        fr = (a * 5 / (2 * np.pi)) % 1.0
        rr = r * (0.42 + 0.58 * (1 - 2 * np.abs(fr - 0.5)) ** 1.6)
        m = np.clip((rr - d) / 1.2, 0, 1)
        img = mixc(img, solid(star_cols[k % len(star_cols)]), m)
    return np.clip(img, 0, 1)


def screen(col="#1a3a2a", fg="#7dff6a", seed=25):
    """Dark display glass with glowing scanlines / digits-ish blocks."""
    img = solid(col)
    sl = ((Vv * 128) % 1 < 0.5).astype(np.float32)
    img = tint(img, 0.85 + 0.25 * sl)
    rnd = np.random.RandomState(seed)
    m = np.zeros((N, N), np.float32)
    for _ in range(26):
        x, y = rnd.randint(0, N, 2)
        w, h = rnd.randint(8, 40), rnd.randint(4, 8)
        m[y:y + h, x:x + w] = rnd.uniform(0.5, 1)
    m = T.wrap_blur(m, 2, 1)
    return np.clip(mixc(img, solid(fg), m), 0, 1)


# ------------------------------------------------------------------------------------------------ registry

KINDS = {
    "paint": paint, "plastic": plastic, "metal": metal, "gunmetal": gunmetal, "wood": wood, "rubber": rubber,
    "stripes": stripes, "bands": bands, "panels": panels, "glow": glow, "glass": glass, "fabric": fabric,
    "stone": stone, "cinder": cinder, "snow": snow, "slime": slime, "latex": latex, "peel": peel, "paper": paper,
    "chocolate": chocolate, "foil": foil, "fur": fur, "egg": egg, "stars": stars, "screen": screen,
}

_done = {}
_shipped = set()


def texture(name, kind, *args, **kw):
    """Generate (once per run) the 512px master of a texture; returns its path."""
    path = os.path.join(CACHE, name + ".png")
    if name in _done:
        return path
    img = KINDS[kind](*args, **kw).astype(np.float32)
    C.write_png(path, img)
    _done[name] = img
    return path


def ship(name):
    """Write the 256px game copy of a generated texture to Resources; returns its path."""
    path = os.path.join(OUT, name + ".png")
    if name not in _shipped:
        img = _done[name]
        k = N // SHIP
        small = img.reshape(SHIP, k, SHIP, k, 3).mean(axis=(1, 3))
        C.write_png(path, small)
        _shipped.add(name)
    return path


def prune():
    """Delete game textures no exported model uses any more."""
    if not os.path.isdir(OUT):
        return 0
    n = 0
    for f in os.listdir(OUT):
        if f.endswith(".png") and f[:-4] not in _shipped:
            os.remove(os.path.join(OUT, f))
            n += 1
    return n
