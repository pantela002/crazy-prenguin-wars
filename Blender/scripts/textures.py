"""Seamless 512x512 cartoon terrain textures (numpy, periodic noise/voronoi so every texture tiles),
512x128 grass/snow/sand cap strips and 4x256 sky gradients.
Output: Resources/Textures/Terrain/{id}.png, Resources/Textures/Terrain/{id}_Cap.png, Resources/Textures/Sky/{theme}.png.

The rock themes reproduce the original landmass art: rounded "pillow" stones with a painted top-left light,
a lighter rim, a thick dark outline and dark gaps (Wood/Forest = mauve cobbles with warm highlights,
Stone/Mountain = grey boulders, Ice = faceted blocks, Winter = snow lumps, Desert = layered sandstone)."""
import os

import numpy as np

import common as C

N = 512


def hexc(h):
    return np.array(C.hex_rgb(h), dtype=np.float32)


def value_noise(n, cells, seed):
    """Periodic smooth value noise in [0,1] (tiles because the lattice wraps)."""
    rnd = np.random.RandomState(seed)
    g = rnd.rand(cells, cells).astype(np.float32)
    t = np.arange(n, dtype=np.float32) * cells / n
    i0 = np.floor(t).astype(int)
    f = t - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    i0 %= cells
    a = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
    b = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
    return a * (1 - f)[:, None] + b * f[:, None]


def fbm(n, seed, octaves=4, base=4):
    out = np.zeros((n, n), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        out += value_noise(n, base * 2 ** o, seed + o * 17) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def voronoi(n, count, seed, jitter=1.0):
    """Periodic voronoi: returns (F1 distance, F2 - F1 edge distance, cell id), distances in pixels."""
    rnd = np.random.RandomState(seed)
    pts = rnd.rand(count, 2) * n
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    d1 = np.full((n, n), 1e9, np.float32)
    d2 = np.full((n, n), 1e9, np.float32)
    cid = np.zeros((n, n), np.int32)
    for k, (px, py) in enumerate(pts):
        dx = np.abs(xx - px)
        dx = np.minimum(dx, n - dx)
        dy = np.abs(yy - py)
        dy = np.minimum(dy, n - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        cid = np.where(closer, k, cid)
        d1 = np.where(closer, d, d1)
    return d1, d2 - d1, cid


def ramp(t, cols):
    """Map t in [0,1] through a list of colors."""
    t = np.clip(t, 0, 1) * (len(cols) - 1)
    i = np.minimum(np.floor(t).astype(int), len(cols) - 2)
    f = (t - i)[..., None]
    c = np.array(cols, np.float32)
    return c[i] * (1 - f) + c[i + 1] * f


def shade(img, amount):
    return np.clip(img * amount[..., None], 0, 1)


def mix(a, b, m):
    return a * (1 - m[..., None]) + b * m[..., None]


def wood(seed=1):
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    plank_h = N / 4
    row = np.floor(yy / plank_h)
    off = (row * 97) % N
    warp = fbm(N, seed, 3, 2) * 8
    grain = np.sin((yy + warp * 3) / plank_h * np.pi * 6 + np.sin((xx + off) / N * 2 * np.pi) * 2) * 0.5 + 0.5
    n = fbm(N, seed + 5, 4, 4)
    base = ramp(n * 0.6 + grain * 0.4, [hexc("#b8783e"), hexc("#cf9150"), hexc("#e0a865")])
    gy = (yy % plank_h)
    gap = (gy < 3) | (gy > plank_h - 2)
    # vertical joints, staggered per row
    jx = (xx + off * 1.0) % (N / 2)
    joint = jx < 3
    img = mix(base, hexc("#6b3f1c"), (gap | joint).astype(np.float32) * 0.9)
    edge = ((gy >= 3) & (gy < 6)).astype(np.float32)
    img = mix(img, hexc("#f0c080"), edge * 0.35)
    # knots
    rnd = np.random.RandomState(seed)
    for _ in range(4):
        kx, ky = rnd.rand(2) * N
        dx = np.minimum(np.abs(xx - kx), N - np.abs(xx - kx))
        dy = np.minimum(np.abs(yy - ky), N - np.abs(yy - ky))
        d = np.sqrt(dx * dx * 0.5 + dy * dy * 2)
        ring = (np.sin(d * 0.9) * 0.5 + 0.5) * (d < 12)
        img = mix(img, hexc("#8a5326"), ring * 0.6)
    return img


def stone(seed=2, cols=("#7d7b75", "#9b9890", "#b5b2aa"), mortar="#4f4d49", count=22):
    d1, edge, cid = voronoi(N, count, seed)
    n = fbm(N, seed + 3, 4, 4)
    rnd = np.random.RandomState(seed)
    tint = rnd.rand(count).astype(np.float32)[cid]
    base = ramp(n * 0.5 + tint * 0.5, [hexc(c) for c in cols])
    # bevel: lighter on top-left of each cell
    hl = np.clip(1 - edge / 10, 0, 1)
    img = shade(base, 1 - 0.15 * np.clip(d1 / 30, 0, 1) + 0.0)
    img = mix(img, hexc(mortar), np.clip(1 - edge / 2.5, 0, 1))
    img = mix(img, hexc("#ffffff"), np.clip(1 - np.abs(edge - 3.5) / 1.5, 0, 1) * 0.12)
    return img


def ice(seed=3):
    n = fbm(N, seed, 4, 3)
    base = ramp(n, [hexc("#8fd2f0"), hexc("#b9e6f8"), hexc("#e2f6ff")])
    d1, edge, cid = voronoi(N, 9, seed + 1)
    crack = np.clip(1 - edge / 1.6, 0, 1)
    img = mix(base, hexc("#ffffff"), crack * 0.75)
    streak = np.clip(np.sin((np.mgrid[0:N, 0:N][1] + np.mgrid[0:N, 0:N][0]) / N * 2 * np.pi * 3) - 0.85, 0, 1) * 4
    img = mix(img, hexc("#ffffff"), streak * 0.3)
    rnd = np.random.RandomState(seed)
    for _ in range(12):
        x, y = rnd.randint(0, N, 2)
        img[y, x] = 1.0
        img[(y + 1) % N, x] = img[y, (x + 1) % N] = img[(y - 1) % N, x] = img[y, (x - 1) % N] = 0.95
    return img


def metal(seed=4, base_cols=("#7e8794", "#9aa3b0", "#b2bac6"), rust=0.0):
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    n = fbm(N, seed, 4, 4)
    img = ramp(n * 0.5 + (yy % 128) / 128 * 0.5, [hexc(c) for c in base_cols])
    px, py = xx % 128, yy % 128
    seam = ((px < 2) | (py < 2)).astype(np.float32)
    img = mix(img, hexc("#4a5260"), seam * 0.9)
    hl = (((px >= 2) & (px < 4)) | ((py >= 2) & (py < 4))).astype(np.float32)
    img = mix(img, hexc("#d6dde6"), hl * 0.5)
    for cx in (10, 118):
        for cy in (10, 118):
            d = np.sqrt((px - cx) ** 2 + (py - cy) ** 2)
            img = mix(img, hexc("#5a6270"), (d < 5).astype(np.float32))
            img = mix(img, hexc("#dfe5ee"), ((d < 2.5) & (px < cx) & (py < cy)).astype(np.float32) * 0.8)
    if rust > 0:
        r = np.clip((fbm(N, seed + 9, 4, 3) - (1 - rust)) * 5, 0, 1)
        img = mix(img, hexc("#9a5a2a"), r * 0.8)
    return img


def soil(seed=5, cols=("#5c3a22", "#734a2c", "#8a5c38"), pebble="#a08060", count=60):
    n = fbm(N, seed, 5, 4)
    img = ramp(n, [hexc(c) for c in cols])
    d1, edge, cid = voronoi(N, count, seed + 1)
    rnd = np.random.RandomState(seed)
    keep = (rnd.rand(count) < 0.35)[cid]
    peb = np.clip(1 - d1 / 5.5, 0, 1) * keep
    img = mix(img, hexc(pebble), (peb > 0.05).astype(np.float32))
    img = mix(img, hexc("#ffffff"), (peb > 0.6).astype(np.float32) * 0.25)
    return img


def snow(seed=6):
    n = fbm(N, seed, 4, 3)
    img = ramp(n, [hexc("#cfe0f2"), hexc("#e6f0fa"), hexc("#ffffff")])
    d1, edge, cid = voronoi(N, 14, seed)
    img = mix(img, hexc("#b8cce4"), np.clip(1 - edge / 3, 0, 1) * 0.35)
    return img


def rock_strata(seed=7):
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    warp = fbm(N, seed, 3, 2) * 30
    bands = np.sin((yy + warp) / N * 2 * np.pi * 5) * 0.5 + 0.5
    n = fbm(N, seed + 2, 4, 4)
    img = ramp(bands * 0.6 + n * 0.4, [hexc("#6e6258"), hexc("#857868"), hexc("#a09080")])
    d1, edge, cid = voronoi(N, 16, seed + 1)
    img = mix(img, hexc("#4c433b"), np.clip(1 - edge / 2, 0, 1) * 0.8)
    return img


def sand(seed=8):
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    warp = fbm(N, seed, 3, 2) * 40
    rip = np.sin((yy + warp + xx * 0.25) / N * 2 * np.pi * 8) * 0.5 + 0.5
    n = fbm(N, seed + 1, 4, 6)
    img = ramp(rip * 0.45 + n * 0.55, [hexc("#d9ae68"), hexc("#e8c47a"), hexc("#f4d898")])
    rnd = np.random.RandomState(seed)
    dots = (rnd.rand(N, N) < 0.01).astype(np.float32)
    img = mix(img, hexc("#b88a48"), dots * 0.7)
    return img


def mud(seed=9):
    n = fbm(N, seed, 5, 3)
    img = ramp(n, [hexc("#3e2a1a"), hexc("#5a3e26"), hexc("#6e4e30")])
    p = np.clip((fbm(N, seed + 4, 3, 3) - 0.6) * 8, 0, 1)
    img = mix(img, hexc("#2c1e14"), p * 0.8)
    img = mix(img, hexc("#8a6a48"), np.clip((p - 0.9) * 10, 0, 1) * 0.0)
    return img


def lava(seed=10):
    d1, edge, cid = voronoi(N, 18, seed)
    n = fbm(N, seed + 1, 4, 4)
    rock = ramp(n, [hexc("#2a1e1e"), hexc("#3c2a26"), hexc("#4e3830")])
    glow = np.clip(1 - edge / 6, 0, 1) ** 1.5
    img = mix(rock, ramp(glow, [hexc("#ff3a10"), hexc("#ff8a1a"), hexc("#ffe060")]), np.clip(glow * 1.4, 0, 1))
    return img


def _roll_grad(h):
    gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    gy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    return gx, gy


def wrap_blur(a, r, passes=2):
    """Periodic box blur (keeps the texture seamless)."""
    r = int(max(1, r))
    for _ in range(passes):
        for ax in (0, 1):
            acc = np.zeros_like(a)
            for k in range(-r, r + 1):
                acc += np.roll(a, k, axis=ax)
            a = acc / (2 * r + 1)
    return a


def cells(n, grid, seed, jitter=0.85, sx=1.0, sy=1.0):
    """Periodic jittered-grid voronoi (even cell sizes). sx/sy < 1 stretch cells along that axis.
    Returns (F1, edge = F2 - F1, cell id) in pixels."""
    rnd = np.random.RandomState(seed)
    gxn, gyn = grid if isinstance(grid, tuple) else (grid, grid)
    pts = []
    for j in range(gyn):
        for i in range(gxn):
            pts.append(((i + 0.5 + (rnd.rand() - 0.5) * jitter) * n / gxn, (j + 0.5 + (rnd.rand() - 0.5) * jitter) * n / gyn))
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    d1 = np.full((n, n), 1e9, np.float32)
    d2 = np.full((n, n), 1e9, np.float32)
    cid = np.zeros((n, n), np.int32)
    for k, (px, py) in enumerate(pts):
        dx = np.abs(xx - px)
        dx = np.minimum(dx, n - dx) * sx
        dy = np.abs(yy - py)
        dy = np.minimum(dy, n - dy) * sy
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        cid = np.where(closer, k, cid)
        d1 = np.where(closer, d, d1)
    return d1, d2 - d1, cid, len(pts)


def cobble(seed, grid, cols, gap, outline, hl, rim=None, pillow=0.38, ow=3.2, sx=1.0, sy=1.0, round_pow=2.0,
           speck=0.0, crack=0.0, streak=None, light_gain=0.62, light_amt=0.6, soften=0.35):
    """Rounded painted stones: per-stone color, dome shading from a top-left light, warm highlight on the lit top,
    lighter rim inside the outline, thick dark outline and dark gaps."""
    d1, edge, cid, count = cells(N, grid, seed, sx=sx, sy=sy)
    rnd = np.random.RandomState(seed + 1)
    cell = N / max(grid if not isinstance(grid, tuple) else max(grid), 1)
    R = cell * pillow
    e = edge * 0.5                                      # distance to the stone border (px)
    t = np.clip((e - ow) / R, 0, 1)
    h = 1 - (1 - t) ** round_pow                        # dome profile
    if soften > 0:                                      # round off the ridge along the cell's medial axis
        h = np.maximum(wrap_blur(h, R * soften), 0) * (t > 0)
    h = h + (fbm(N, seed + 2, 4, 8) - 0.5) * 0.25       # bumpy surface
    gx, gy = _roll_grad(h * R * 0.9)
    nz = np.ones_like(gx)
    L = np.array([-0.5, -0.55, 0.67], np.float32)       # light from the top-left (image y points down)
    L /= np.linalg.norm(L)
    nl = (-gx * L[0] - gy * L[1] + nz * L[2]) / np.sqrt(gx * gx + gy * gy + 1)
    shade = np.clip(nl, 0, 1)
    tint = rnd.rand(count).astype(np.float32)[cid]
    n = fbm(N, seed + 3, 4, 6)
    base = ramp(np.clip(tint * 0.75 + n * 0.35, 0, 1), [hexc(c) for c in cols])
    img = base * (light_gain + light_amt * shade[..., None])
    img = mix(img, hexc(hl), np.clip((shade - 0.88) * 7, 0, 1) * 0.75)
    if streak is not None:
        # painted highlight strokes on the upper part of each stone
        st = np.clip((fbm(N, seed + 6, 3, 16) - 0.55) * 5, 0, 1) * np.clip((shade - 0.75) * 4, 0, 1)
        img = mix(img, hexc(streak), st * 0.7)
    if rim is not None:
        rimm = np.clip(1 - np.abs(e - ow - 2.2) / 1.6, 0, 1) * np.clip((shade - 0.5) * 3, 0, 1)
        img = mix(img, hexc(rim), rimm * 0.55)
    if speck > 0:
        sp = (np.random.RandomState(seed + 4).rand(N, N) < speck).astype(np.float32)
        sp = np.clip(sp + np.roll(sp, 1, 0) + np.roll(sp, 1, 1), 0, 1)
        img = mix(img, hexc(outline), sp * 0.35 * (t > 0.3))
    if crack > 0:
        cd1, cedge, _, _ = cells(N, max(2, int((grid if not isinstance(grid, tuple) else grid[0]) * 0.7)), seed + 9)
        cr = np.clip(1 - cedge / 1.4, 0, 1) * (fbm(N, seed + 10, 2, 4) > 0.55) * (t > 0.25)
        img = mix(img, hexc(outline), cr * crack)
    # ambient darkening toward the border (contact shadow), then the outline and the gap
    img = img * (0.78 + 0.22 * np.clip(e / (ow * 3), 0, 1))[..., None]
    img = mix(img, hexc(outline), np.clip(1 - (e - ow) / 1.2, 0, 1))
    img = mix(img, hexc(gap), np.clip(1 - (e - ow * 0.45) / 1.0, 0, 1))
    return np.clip(img, 0, 1)


def forest_cobble(seed=21):
    return cobble(seed, 4, ("#7e4448", "#94525a", "#a5605c", "#8a4a56"), gap="#4a1f28", outline="#5c2c36",
                  hl="#f0a060", rim="#c47a5c", streak="#e08850", pillow=0.42, speck=0.002)


def grey_boulders(seed=22):
    return cobble(seed, 5, ("#5e5f63", "#6e6f72", "#7e7d7c", "#67686d"), gap="#2a2324", outline="#4b3736",
                  hl="#c8c6c0", rim="#9a9894", pillow=0.36, crack=0.8, speck=0.004)


def mountain_rock(seed=23):
    return cobble(seed, 4, ("#5a4a46", "#6a5650", "#76625a"), gap="#241a1a", outline="#4b3736",
                  hl="#d8a070", rim="#9a7a68", pillow=0.3, round_pow=1.0, crack=0.9, speck=0.003, soften=0.15)


def ice_blocks(seed=24):
    img = cobble(seed, 3, ("#7cc6ea", "#94d4f2", "#aadff6"), gap="#3a7ca8", outline="#4f9eb2",
                 hl="#ffffff", rim="#e6f8ff", pillow=0.22, round_pow=1.0, light_gain=0.72, soften=0.0)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    streak = np.clip(np.sin((xx + yy) / N * 2 * np.pi * 4 + fbm(N, seed, 2, 3) * 3) - 0.9, 0, 1) * 10
    return mix(img, hexc("#ffffff"), streak * 0.25)


def snow_lumps(seed=25):
    img = cobble(seed, 4, ("#d4e6f6", "#e2eefa", "#eef6ff"), gap="#8fb4d8", outline="#a8c6e4",
                 hl="#ffffff", rim="#ffffff", pillow=0.5, ow=2.0, light_gain=0.86, light_amt=0.22, soften=0.5)
    return mix(img, hexc("#b8d4f0"), np.clip(0.9 - img.mean(axis=-1), 0, 1) * 0.6)


def sandstone(seed=26):
    img = cobble(seed, (4, 7), ("#d89a5c", "#e2a866", "#cf8e52", "#e8b878"), gap="#6a3a22", outline="#864e36",
                 hl="#fff0c0", rim="#f4c890", pillow=0.3, sx=0.55, sy=1.0, speck=0.006)
    yy = np.mgrid[0:N, 0:N][0].astype(np.float32)
    bands = np.sin((yy + fbm(N, seed + 1, 3, 4) * 40) / N * 2 * np.pi * 14) * 0.5 + 0.5
    return img * (0.94 + 0.08 * bands)[..., None]


TERRAIN = {
    "Wood": forest_cobble, "Stone": grey_boulders, "Ice": ice_blocks, "Metal": metal,
    "Forest": lambda: forest_cobble(31), "Winter": snow_lumps, "Mountain": mountain_rock, "Desert": sandstone,
    "Mud": mud, "Lava": lava,
    "OilRig": lambda: metal(11, ("#5e6670", "#737c88", "#87909c"), rust=0.35),
    "CustomObjects": lambda: wood(12),
}

SKY = {
    "Forest": ("#5aa8ec", "#cdeeff"),
    "Winter": ("#78a8e6", "#e6f2ff"),
    "Mountain": ("#543d66", "#fa9e5c"),
    "Desert": ("#5c9eed", "#ffe09e"),
    "OilRig": ("#3a5a8a", "#c8d8e8"),
}


# ------------------------------------------------------------------------------------------ cap strips
# Unity (TerrainChunk.AddCap): a strip from 0.16 below the surface to 0.34 above it, 1.6 units per repeat.
# v = 0 is the bottom row (written last), the surface line sits at v = 0.32. The colors are final:
# TerrainStyle.CapMaterial cancels the per-theme vertex tint for these textures.
CW, CH = 512, 128
SURF = 0.32


def _periodic(x, freqs, seed):
    rnd = np.random.RandomState(seed)
    out = np.zeros_like(x)
    for f, a in freqs:
        out += a * np.sin(2 * np.pi * (f * x + rnd.rand()))
    return out


def cap_strip(kind, seed=40):
    u = (np.arange(CW, dtype=np.float32) + 0.5) / CW
    v = 1 - (np.arange(CH, dtype=np.float32) + 0.5) / CH          # row 0 = top (v = 1)
    U, Vv = np.meshgrid(u, v)
    rgba = np.zeros((CH, CW, 4), np.float32)
    px = 1.0 / CH
    if kind in ("grass", "moss"):
        if kind == "grass":
            top, mid, low, line = hexc("#a6e45a"), hexc("#6cc23e"), hexc("#3f9a32"), hexc("#1f4f22")
        else:
            top, mid, low, line = hexc("#b4c860"), hexc("#86a040"), hexc("#5c7a2e"), hexc("#2c3a1a")
        # turf lower edge: wavy line below the surface with scalloped bumps
        lower = 0.1 + 0.05 * np.abs(np.sin(np.pi * 9 * U)) + _periodic(U, ((3, 0.02), (7, 0.012)), seed)
        # blade tips: many sharp blades of varying height
        rnd = np.random.RandomState(seed)
        tips = np.full(CW, SURF + 0.04, np.float32)
        x = np.arange(CW, dtype=np.float32)
        for k in range(46):
            c = rnd.rand() * CW
            w = rnd.uniform(5, 11)
            hgt = rnd.uniform(0.18, 0.42)
            lean = rnd.uniform(-0.4, 0.4)
            for off in (-CW, 0, CW):
                d = np.abs(x - c - off - lean * 6)
                blade = SURF + hgt * np.clip(1 - d / w, 0, 1) ** 1.6
                tips = np.maximum(tips, blade)
        upper = np.broadcast_to(tips, U.shape)
        inside = (Vv > lower) & (Vv < upper)
        a = np.clip(np.minimum(Vv - lower, upper - Vv) / px + 0.5, 0, 1)
        g = np.clip((Vv - lower) / (upper - lower + 1e-4), 0, 1)
        col = ramp(g, [low, mid, top])
        # blade separation lines and a darker underside band
        stripes = np.clip(np.sin(np.pi * 60 * U + _periodic(U, ((5, 2.0),), seed + 1)) - 0.7, 0, 1) * 3 * (Vv > SURF)
        col = mix(col, low, stripes * 0.5)
        col = mix(col, line, np.clip(1 - (Vv - lower) / (2.5 * px), 0, 1))           # ink line along the turf edge
        edge = np.clip(1 - (upper - Vv) / (1.8 * px), 0, 1) * (Vv > SURF + 0.05)
        col = mix(col, line, edge * 0.8)                                              # ink on blade tips
        # a few flowers/clover dots
        for k in range(6):
            cx, cy = rnd.rand(), rnd.uniform(SURF + 0.02, SURF + 0.1)
            d = np.sqrt(((U - cx + 0.5) % 1 - 0.5) ** 2 * (CW / CH) ** 2 + (Vv - cy) ** 2)
            col = mix(col, hexc(["#ffffff", "#ffe23a", "#ff8fa8"][k % 3]), np.clip(1 - (d - 0.022) / px, 0, 1))
        rgba[..., :3] = col
        rgba[..., 3] = a * inside + a * 0
    elif kind == "snow":
        lower = 0.1 + _periodic(U, ((2, 0.03), (5, 0.02)), seed)
        # icicles hanging below the snow edge
        rnd = np.random.RandomState(seed)
        x = np.arange(CW, dtype=np.float32)
        drip = np.zeros(CW, np.float32)
        for k in range(9):
            c = rnd.rand() * CW
            w = rnd.uniform(5, 9)
            ln = rnd.uniform(0.05, 0.1)
            for off in (-CW, 0, CW):
                d = np.abs(x - c - off)
                drip = np.maximum(drip, ln * np.clip(1 - d / w, 0, 1))
        lower = lower - np.broadcast_to(drip, U.shape)
        upper = SURF + 0.16 + _periodic(U, ((2, 0.05), (4, 0.03), (9, 0.015)), seed + 2)
        a = np.clip(np.minimum(Vv - lower, upper - Vv) / px + 0.5, 0, 1)
        g = np.clip((Vv - lower) / (upper - lower + 1e-4), 0, 1)
        col = ramp(g, [hexc("#a8cbe8"), hexc("#dfeefb"), hexc("#ffffff")])
        icy = np.broadcast_to(drip, U.shape) > 0.002
        col = mix(col, hexc("#bfe6fa"), (icy & (Vv < 0.1 + 0.02)).astype(np.float32) * 0.8)
        sparkle = (np.random.RandomState(seed + 3).rand(CH, CW) < 0.004).astype(np.float32) * (g > 0.5)
        col = mix(col, hexc("#ffffff"), sparkle)
        col = mix(col, hexc("#6f9cc6"), np.clip(1 - (Vv - lower) / (2.0 * px), 0, 1) * 0.9)
        col = mix(col, hexc("#9ec0e0"), np.clip(1 - (upper - Vv) / (1.6 * px), 0, 1) * 0.6)
        rgba[..., :3] = col
        rgba[..., 3] = a
    else:  # sand
        lower = 0.16 + _periodic(U, ((2, 0.03), (6, 0.015)), seed)
        upper = SURF + 0.09 + _periodic(U, ((1, 0.04), (3, 0.03), (8, 0.01)), seed + 1)
        a = np.clip(np.minimum(Vv - lower, upper - Vv) / px + 0.5, 0, 1)
        g = np.clip((Vv - lower) / (upper - lower + 1e-4), 0, 1)
        col = ramp(g, [hexc("#d09a56"), hexc("#ecc47e"), hexc("#fbe0a4")])
        dots = (np.random.RandomState(seed + 2).rand(CH, CW) < 0.012).astype(np.float32)
        col = mix(col, hexc("#b88048"), dots * 0.6)
        col = mix(col, hexc("#864e36"), np.clip(1 - (Vv - lower) / (2.2 * px), 0, 1) * 0.8)
        # a few pebbles and dry grass tufts on top
        rnd = np.random.RandomState(seed + 5)
        for k in range(5):
            cx = rnd.rand()
            d = np.sqrt(((U - cx + 0.5) % 1 - 0.5) ** 2 * (CW / CH) ** 2 + ((Vv - SURF - 0.06) * 1.6) ** 2)
            m = np.clip(1 - (d - 0.035) / px, 0, 1)
            a = np.maximum(a, m)
            col = mix(col, hexc("#9a7a5a"), m)
        rgba[..., :3] = col
        rgba[..., 3] = a
    rgba[..., 3] = np.clip(rgba[..., 3], 0, 1)
    return rgba


# ------------------------------------------------------------------------------------------ model detail
# Gray (0.5 = neutral) 256x256 tiles that CPW/Toon multiplies into the flat Blender colors of the 3D models
# (Mats.ApplyToon picks one by material name; UV0 = Unity object-space x/y baked by the FBX export).
D = 256


def _dn(seed, cells, octaves=3):
    return fbm(D, seed, octaves, cells) if D == N else _fbm_n(D, seed, octaves, cells)


def _fbm_n(n, seed, octaves, base):
    out = np.zeros((n, n), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        out += value_noise(n, base * 2 ** o, seed + o * 17) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def detail_wood(seed=60):
    yy, xx = np.mgrid[0:D, 0:D].astype(np.float32)
    warp = _fbm_n(D, seed, 3, 2) * 18 + np.sin(xx / D * 2 * np.pi * 2) * 4
    rings = np.sin((yy + warp) / D * 2 * np.pi * 9) * 0.5 + 0.5
    fine = _fbm_n(D, seed + 1, 3, 16)
    g = 0.5 + (rings - 0.5) * 0.35 + (fine - 0.5) * 0.25
    # dark grain streaks
    streak = np.clip(1 - np.abs(np.sin((yy + warp * 1.3) / D * 2 * np.pi * 23)) * 8, 0, 1) * (_fbm_n(D, seed + 2, 2, 4) > 0.45)
    g -= streak * 0.22
    # a knot
    d = np.sqrt(((xx - D * 0.7 + D / 2) % D - D / 2) ** 2 * 0.6 + ((yy - D * 0.35 + D / 2) % D - D / 2) ** 2 * 2.2)
    g -= (np.sin(d * 0.8) * 0.5 + 0.5) * (d < 16) * 0.25
    return np.clip(g, 0, 1)


def detail_stone(seed=61):
    g = 0.5 + (_fbm_n(D, seed, 4, 4) - 0.5) * 0.5
    rnd = np.random.RandomState(seed)
    sp = rnd.rand(D, D)
    g -= (sp < 0.012) * 0.25
    g += (sp > 0.992) * 0.18
    d1, edge, cid, cnt = cells(D, 4, seed + 3) if False else (None, None, None, None)
    # hairline cracks from a periodic voronoi of a few cells
    yy, xx = np.mgrid[0:D, 0:D].astype(np.float32)
    rnd2 = np.random.RandomState(seed + 4)
    pts = rnd2.rand(7, 2) * D
    f1 = np.full((D, D), 1e9, np.float32)
    f2 = np.full((D, D), 1e9, np.float32)
    for px, py in pts:
        dx = np.minimum(np.abs(xx - px), D - np.abs(xx - px))
        dy = np.minimum(np.abs(yy - py), D - np.abs(yy - py))
        dd = np.sqrt(dx * dx + dy * dy)
        f2 = np.where(dd < f1, f1, np.minimum(f2, dd))
        f1 = np.minimum(f1, dd)
    crack = np.clip(1 - (f2 - f1) / 1.3, 0, 1) * (_fbm_n(D, seed + 5, 2, 3) > 0.5)
    g -= crack * 0.35
    return np.clip(g, 0, 1)


def detail_metal(seed=62):
    yy, xx = np.mgrid[0:D, 0:D].astype(np.float32)
    rnd = np.random.RandomState(seed)
    brushed = np.repeat(rnd.rand(D, 1).astype(np.float32), D, axis=1)
    brushed = (brushed + np.roll(brushed, 1, 0) + np.roll(brushed, -1, 0)) / 3
    g = 0.5 + (brushed - 0.5) * 0.18 + (_fbm_n(D, seed + 1, 3, 3) - 0.5) * 0.3
    for _ in range(9):   # scratches
        x0, y0 = rnd.rand(2) * D
        a = rnd.uniform(-0.5, 0.5)
        ln = rnd.uniform(20, 60)
        t = (xx - x0) * np.cos(a) + (yy - y0) * np.sin(a)
        dist = np.abs(-(xx - x0) * np.sin(a) + (yy - y0) * np.cos(a))
        g += ((dist < 0.8) & (t > 0) & (t < ln)) * 0.22
    # grime toward blotches
    g -= np.clip((_fbm_n(D, seed + 2, 3, 4) - 0.62) * 2, 0, 1) * 0.2
    return np.clip(g, 0, 1)


def detail_ice(seed=63):
    yy, xx = np.mgrid[0:D, 0:D].astype(np.float32)
    streak = np.clip(np.sin((xx + yy * 0.6) / D * 2 * np.pi * 3 + _fbm_n(D, seed, 2, 3) * 4) - 0.8, 0, 1) * 5
    g = 0.5 + (_fbm_n(D, seed + 1, 3, 3) - 0.5) * 0.3 + streak * 0.2
    rnd = np.random.RandomState(seed)
    g += (rnd.rand(D, D) > 0.996) * 0.3     # tiny bubbles/sparkles
    return np.clip(g, 0, 1)


def detail_fabric(seed=64):
    yy, xx = np.mgrid[0:D, 0:D].astype(np.float32)
    weave = (np.sin(xx / D * 2 * np.pi * 64) * np.sin(yy / D * 2 * np.pi * 64)) * 0.5
    return np.clip(0.5 + weave * 0.12 + (_fbm_n(D, seed, 3, 4) - 0.5) * 0.25, 0, 1)


DETAIL = {"wood": detail_wood, "stone": detail_stone, "metal": detail_metal, "ice": detail_ice, "fabric": detail_fabric}


CAPS = {"Wood": "grass", "Forest": "grass", "Stone": "moss", "Mountain": "moss", "Winter": "snow", "Ice": "snow",
        "Desert": "sand"}


def write_rgb(path, rgb):
    a = np.ones(rgb.shape[:2] + (1,), np.float32)
    C.write_png(path, np.concatenate([rgb, a], axis=-1))


def run():
    out = os.path.join(C.TEXTURES, "Terrain")
    for name, fn in TERRAIN.items():
        img = fn()
        assert np.allclose(img[0], img[0]), name
        write_rgb(os.path.join(out, name + ".png"), img.astype(np.float32))
    for name, kind in CAPS.items():
        C.write_png(os.path.join(out, name + "_Cap.png"), cap_strip(kind, 40 + len(name)))
    det = os.path.join(C.TEXTURES, "Detail")
    for name, fn in DETAIL.items():
        g = fn().astype(np.float32)
        write_rgb(os.path.join(det, name + ".png"), np.repeat(g[..., None], 3, axis=-1))
    sky = os.path.join(C.TEXTURES, "Sky")
    for name, (top, bot) in SKY.items():
        t = np.linspace(0, 1, 256, dtype=np.float32)[:, None, None]
        t = t ** 1.2
        col = hexc(top)[None, None] * (1 - t) + hexc(bot)[None, None] * t
        write_rgb(os.path.join(sky, name + ".png"), np.repeat(col, 4, axis=1))
    print("[textures] %d terrain, %d caps, %d detail, %d sky" % (len(TERRAIN), len(CAPS), len(DETAIL), len(SKY)))
    # the original game's own art replaces the procedural fill/cap textures where it exists (see original_art.py)
    import original_art
    original_art.run()
