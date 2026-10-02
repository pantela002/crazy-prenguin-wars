"""Seamless 256x256 cartoon terrain textures (numpy, periodic noise/voronoi so every texture tiles)
and 4x256 sky gradients. Output: Resources/Textures/Terrain/{id}.png and Resources/Textures/Sky/{theme}.png."""
import os

import numpy as np

import common as C

N = 256


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


TERRAIN = {
    "Wood": wood, "Stone": stone, "Ice": ice, "Metal": metal,
    "Forest": lambda: soil(5), "Winter": snow, "Mountain": rock_strata, "Desert": sand,
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


def write_rgb(path, rgb):
    a = np.ones(rgb.shape[:2] + (1,), np.float32)
    C.write_png(path, np.concatenate([rgb, a], axis=-1))


def run():
    out = os.path.join(C.TEXTURES, "Terrain")
    for name, fn in TERRAIN.items():
        img = fn()
        assert np.allclose(img[0], img[0]), name
        write_rgb(os.path.join(out, name + ".png"), img.astype(np.float32))
    sky = os.path.join(C.TEXTURES, "Sky")
    for name, (top, bot) in SKY.items():
        t = np.linspace(0, 1, 256, dtype=np.float32)[:, None, None]
        t = t ** 1.2
        col = hexc(top)[None, None] * (1 - t) + hexc(bot)[None, None] * t
        write_rgb(os.path.join(sky, name + ".png"), np.repeat(col, 4, axis=1))
    print("[textures] %d terrain, %d sky" % (len(TERRAIN), len(SKY)))
