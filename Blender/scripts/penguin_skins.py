"""The penguin's texture atlas (one 1024x1024 image for the whole bird, so it is one material and one draw call) and
its skins: every skin is the same atlas layout painted with a different palette/pattern, so a skin is only a texture
swap in Unity (Textures/Penguin/{skin}.png on the "T_penguin" material).

Atlas layout (UV space, v up), filled by penguin.py's projections:
    BODY   cylinder around the body axis: u = angle (0 = back seam, 0.5 = front), v = height 0..BODY_TOP
    HEAD   latitude/longitude around HEAD_C (u as the body, v = latitude -90..90)
    FLIP   flipper plane: u across (front edge at 0), v along (shoulder 1 -> tip 0)
    FOOT   top view of a foot: u across, v along (toes at 0)
    BEAKU / BEAKL  top view of the upper / lower bill (tip at 0)
    EYE / PUPIL    front view of an eye white / pupil disc
"""
import os

import numpy as np

import common as C
import wear_kit as W

SIZE = 1024
BODY = (0.0, 0.5, 1.0, 1.0)
HEAD = (0.0, 0.0, 0.5, 0.5)
FLIP = (0.5, 0.25, 0.75, 0.5)
FOOT = (0.75, 0.25, 1.0, 0.5)
BEAKU = (0.5, 0.125, 0.75, 0.25)
BEAKL = (0.5, 0.0, 0.75, 0.125)
EYE = (0.75, 0.125, 0.875, 0.25)
PUPIL = (0.875, 0.125, 1.0, 0.25)
TONGUE = (0.75, 0.0, 0.875, 0.125)

BODY_TOP = 1.85        # body heights 0..BODY_TOP map onto the BODY region
# wrap margins: u = M0 + t * MU (t = angle fraction, may reach ~1.05 on faces that cross the seam)
M0, MU = 0.01, 0.93
V0, VU = 0.02, 0.96


def to_region(region, s, t):
    """Normalized (s, t) in 0..1 -> atlas uv."""
    u0, v0, u1, v1 = region
    s = 0.03 + 0.94 * min(1.0, max(0.0, s))      # keep off the region border (bilinear/mip bleeding)
    t = 0.03 + 0.94 * min(1.0, max(0.0, t))
    return (u0 + s * (u1 - u0), v0 + t * (v1 - v0))


def wrap_uv(region, t_angle, h):
    """Cylindrical/spherical parts: angle fraction t (0..~1.05) and height fraction h (0..1) -> atlas uv."""
    return to_region(region, M0 + t_angle * MU, V0 + h * VU)


# ------------------------------------------------------------------------------------------ skins

def _h(x):
    return W.hexc(x)


SKINS = {
    # id: palette + pattern. back/back2: feathers (back2 = scallop/pattern accent), belly/chest: front, beak, feet
    "classic": dict(back="#1c3a4a", back2="#2b5466", belly="#f7f8fa", belly_shade="#c9d6e2", chest="#ffd970",
                    beak="#ffb11f", beak2="#f07c12", feet="#ffa51c", feet2="#e8740f", cheek="#ff8fa0", pattern="scallop"),
    "emperor": dict(back="#3a4656", back2="#56657a", head="#15181f", belly="#f6f4ee", belly_shade="#d8d2c4",
                    chest="#ffc93a", patch="#ffb020", beak="#262a33", beak2="#ff9a2a", feet="#2a2d36", feet2="#15171c",
                    cheek=None, pattern="scallop"),
    "golden": dict(back="#e3a521", back2="#ffd45a", belly="#fff4d6", belly_shade="#efd9a0", chest="#ffe9a8",
                   beak="#ff8a1a", beak2="#e0600c", feet="#ff9a1a", feet2="#d96a0c", cheek="#ff9a7a", pattern="sparkle"),
    "arctic": dict(back="#e9f1f9", back2="#bcd3ea", belly="#ffffff", belly_shade="#d3e4f4", chest="#dff0ff",
                   beak="#ffb11f", beak2="#f07c12", feet="#ffa51c", feet2="#e8740f", cheek="#ffa8c0", pattern="scallop"),
    "zombie": dict(back="#5d7552", back2="#7a9466", belly="#c2cca4", belly_shade="#98a67e", chest="#d0d8a8",
                   beak="#b8a15c", beak2="#8c7a40", feet="#9aa86c", feet2="#6e7c4c", cheek=None, pattern="zombie"),
    "galaxy": dict(back="#211a4e", back2="#4a2a8a", belly="#d9cff7", belly_shade="#a998e0", chest="#f5d6ff",
                   beak="#ffb11f", beak2="#f07c12", feet="#ffa51c", feet2="#e8740f", cheek="#ff8fd0", pattern="galaxy"),
    "camo": dict(back="#5b6a35", back2="#3b4422", belly="#dcd3a4", belly_shade="#b8ad7e", chest="#e8dfb0",
                 beak="#ffa51c", beak2="#d9700c", feet="#8a6a3a", feet2="#5e4624", cheek=None, pattern="camo"),
    "robot": dict(back="#8e9aab", back2="#b9c3d0", belly="#d7dde5", belly_shade="#a7b1bf", chest="#6af0ff",
                  beak="#ffaa1f", beak2="#d8780e", feet="#5d6676", feet2="#3c434f", cheek="#ff6a6a", pattern="robot"),
    "lava": dict(back="#2a1a1a", back2="#3d2420", belly="#ffd9a0", belly_shade="#e8a060", chest="#ffb347",
                 beak="#ffcc3a", beak2="#ff7a1a", feet="#3a2420", feet2="#20120f", cheek=None, pattern="lava"),
}
DEFAULT_SKIN = "classic"


def skin_path(skin):
    return os.path.join(W.PENGUIN_TEX_DIR, skin + ".png")


# ------------------------------------------------------------------------------------------ painting helpers

def _region_px(region, n=SIZE):
    """Pixel rectangle (x0, x1, y0, y1) of a region in an image with rows top-first."""
    u0, v0, u1, v1 = region
    x0, x1 = int(round(u0 * n)), int(round(u1 * n))
    y0, y1 = int(round((1 - v1) * n)), int(round((1 - v0) * n))
    return x0, x1, y0, y1


def _coords(region, n=SIZE, wrap=False):
    """Normalized (s, t) per pixel of a region (t up), shapes (h, w). Non-wrapping regions undo to_region's border."""
    x0, x1, y0, y1 = _region_px(region, n)
    w, h = x1 - x0, y1 - y0
    s = (np.arange(w, dtype=np.float32) + 0.5) / w
    t = 1.0 - (np.arange(h, dtype=np.float32) + 0.5) / h
    if not wrap:
        s = np.clip((s - 0.03) / 0.94, 0, 1)
        t = np.clip((t - 0.03) / 0.94, 0, 1)
    return np.meshgrid(s, t)


def _put(img, region, rgb, n=SIZE):
    x0, x1, y0, y1 = _region_px(region, n)
    img[y0:y1, x0:x1, :3] = np.clip(rgb, 0, 1)


def _lerp(a, b, m):
    m = np.asarray(m, np.float32)
    return a * (1 - m[..., None]) + b * m[..., None]


def _scallops(tu, z, ku, kv, seed=0):
    """Overlapping feather scallops on (tu, z); tu in turns (periodic with integer ku), z in units. -> 0..1 shading."""
    gy = z * kv
    row = np.floor(gy)
    gx = tu * ku + (row % 2) * 0.5
    fx = gx - np.floor(gx) - 0.5
    fy = gy - row
    # distance from the feather root (top of the cell); the rounded tip edge catches the light
    d = np.sqrt(fx * fx * 1.4 + (fy - 1.0) ** 2 * 0.8)
    body = W.sstep(1.05, 0.6, d) * 0.5
    tip = W.sstep(0.82, 0.92, d) * W.sstep(1.08, 0.96, d)
    return np.clip(body + tip, 0, 1)


def _periodic_noise(shape, freq, seed, octaves=3):
    """FFT noise that tiles in x (the angle) and y."""
    return W.fbm(shape, freq, seed, octaves=octaves)


def _sparkle(shape, count, seed, size=1.6):
    h, w = shape
    rnd = np.random.RandomState(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    out = np.zeros(shape, np.float32)
    for _ in range(count):
        cx, cy, r = rnd.rand() * w, rnd.rand() * h, rnd.uniform(0.6, 1.0) * size
        dx = (xx - cx + w / 2) % w - w / 2
        dy = yy - cy
        cross = np.exp(-(dx * dx) / (r * r * 0.15) - (dy * dy) / (r * r * 6)) + np.exp(-(dy * dy) / (r * r * 0.15) - (dx * dx) / (r * r * 6))
        out = np.maximum(out, np.clip(cross, 0, 1) * rnd.uniform(0.5, 1.0))
    return out


def _feathers(shape, tu, z, sk, seed):
    """Feather color field for the back/head: base + pattern."""
    pat = sk["pattern"]
    back, back2 = _h(sk["back"]), _h(sk["back2"])
    h, w = shape
    nz = _periodic_noise(shape, 3, seed)
    fine = W.pnoise(shape, 60, seed + 5)
    base = np.broadcast_to(back, shape + (3,)).copy()
    if pat in ("scallop", "sparkle", "zombie", "lava"):
        sc = _scallops(tu, z, 40, 10)
        base = _lerp(base, back2, sc * 0.45)
    if pat == "sparkle":
        base = _lerp(base, _h("#fff3b0"), np.clip(nz, 0, 1) * 0.25)
        base = _lerp(base, _h("#ffffff"), _sparkle(shape, 70, seed + 9, 3.0) * 0.9)
    elif pat == "galaxy":
        neb = W.fbm(shape, 2.5, seed + 11, octaves=4)
        base = _lerp(base, back2, np.clip(neb * 0.6 + 0.3, 0, 1))
        base = _lerp(base, _h("#c04ad8"), np.clip(W.fbm(shape, 4, seed + 12) - 0.6, 0, 1) * 0.8)
        stars = _sparkle(shape, 90, seed + 13, 2.2)
        rnd = np.random.RandomState(seed + 14)
        dots = (rnd.rand(*shape) > 0.9975).astype(np.float32)
        base = _lerp(base, _h("#ffffff"), np.clip(stars + dots, 0, 1))
    elif pat == "camo":
        cols = [_h("#7d8a4a"), _h("#3b4422"), _h("#a39a6a"), _h("#2a2f1c")]
        base = np.broadcast_to(cols[0], shape + (3,)).copy()
        for i, c in enumerate(cols[1:]):
            m = W.sstep(0.55, 0.62, W.fbm(shape, 3, seed + 20 + i, octaves=3) * 0.5 + 0.5 + 0.06 * i - 0.04)
            base = _lerp(base, c, m)
    elif pat == "robot":
        # brushed plates with seams and rivets
        brushed = W.pnoise(shape, 80, seed + 30, aniso=(0.1, 1.0))
        base = _lerp(base, back2, np.clip(brushed * 0.15 + 0.2, 0, 1))
        ku, kv = 8, 4.0
        fu = (tu * ku) % 1.0
        fv = (z * kv) % 1.0
        seam = np.minimum(np.minimum(fu, 1 - fu) * w / ku, np.minimum(fv, 1 - fv) * h / (kv * BODY_TOP) * 0.7)
        base = base * (1 - 0.45 * W.sstep(2.2, 0.6, seam))[..., None]
        rv = np.sqrt(((fu - 0.08) * w / ku) ** 2 + ((fv - 0.12) * h / kv / BODY_TOP) ** 2)
        base = _lerp(base, _h("#e8eef5"), W.sstep(3.2, 1.8, rv))
    if pat == "zombie":
        # torn darker patches and stitches
        patch = W.sstep(0.62, 0.66, W.fbm(shape, 2.5, seed + 40) * 0.5 + 0.5)
        base = _lerp(base, _h("#3e4f38"), patch * 0.85)
        st = np.abs(((tu * 6 + z * 0.8) % 1.0) - 0.5) < 0.012
        tick = (np.sin(z * 120) > 0.2) & (np.abs(((tu * 6 + z * 0.8) % 1.0) - 0.5) < 0.03)
        band = (np.abs(z - 1.1) < 0.35)
        base = _lerp(base, _h("#2a2216"), ((st | tick) & band).astype(np.float32) * 0.9)
    if pat == "lava":
        f1, f2, _ = W.worley(shape, 70, seed + 50)
        crack = W.sstep(2.8, 0.6, f2 - f1)
        glow = _lerp(np.broadcast_to(_h("#ff5a1a"), shape + (3,)), _h("#ffd24a"), W.sstep(1.2, 0.0, f2 - f1))
        base = _lerp(base, glow, crack)
    base *= (1.0 + 0.05 * nz + 0.025 * fine)[..., None]
    return base


# ------------------------------------------------------------------------------------------ the atlas

def paint(skin=None):
    skin = skin or DEFAULT_SKIN
    sk = SKINS[skin]
    img = np.ones((SIZE, SIZE, 4), np.float32)
    seed = sum(map(ord, skin))

    # ---------------- body
    s, t = _coords(BODY, wrap=True)
    tu = (s - M0) / MU                     # angle fraction, 0.5 = front
    z = (t - V0) / VU * BODY_TOP
    feathers = _feathers(s.shape, tu, z, sk, seed)
    d = np.abs(((tu - 0.5 + 0.5) % 1.0) - 0.5)          # angular distance from the front, in turns
    # belly: a tall oval on the front, widest at z~0.7, narrowing to the neck
    zc, zr = 0.8, 0.86
    k = np.clip(1 - ((z - zc) / zr) ** 2, 0, 1)
    width = 0.205 * np.sqrt(k) * (1.0 - 0.3 * W.sstep(0.8, 1.55, z)) * W.sstep(1.68, 1.5, z)
    edge_wave = 0.0025 * np.abs(np.sin(z * 22)) + 0.0015 * W.pnoise(s.shape, 12, seed + 3)
    dist = width - edge_wave - d                         # > 0 inside the belly
    bel = W.sstep(-0.003, 0.003, dist)
    belly = np.broadcast_to(_h(sk["belly"]), s.shape + (3,)).copy()
    # warm chest band at the top of the belly (the original's yellow chest) fading to white, cool shade low and at the rim
    belly = _lerp(belly, _h(sk["chest"]), W.sstep(1.0, 1.45, z) * 0.85)
    belly = _lerp(belly, _h(sk["belly_shade"]), np.clip(W.sstep(0.05, 0.0, dist) * 0.55 + W.sstep(0.55, 0.15, z) * 0.45, 0, 1))
    belly *= (1.0 + 0.01 * W.pnoise(s.shape, 40, seed + 4) + 0.012 * _periodic_noise(s.shape, 4, seed + 6))[..., None]
    soft = _scallops(tu, z, 56, 13)
    belly = belly * (1 - 0.035 * soft)[..., None]
    if sk["pattern"] == "robot":
        # glowing chest light on the belly
        r = np.sqrt(((d) * 6.0) ** 2 + (z - 1.15) ** 2)
        belly = _lerp(belly, _h(sk["chest"]), W.sstep(0.16, 0.12, r))
        belly = _lerp(belly, _h("#ffffff"), W.sstep(0.07, 0.03, r) * 0.7)
        belly = _lerp(belly, _h(sk["belly_shade"]), W.sstep(0.19, 0.17, r) * W.sstep(0.15, 0.17, r))
    body = _lerp(feathers, belly, bel)
    # thin darker line where the feathers meet the belly (painted ink)
    rim = W.sstep(0.012, 0.0, np.abs(dist)) * (1 - bel) * W.sstep(0.01, 0.03, width)
    body = body * (1 - 0.25 * rim)[..., None]
    _put(img, BODY, body)

    # ---------------- head
    s, t = _coords(HEAD, wrap=True)
    tu = (s - M0) / MU
    lat = ((t - V0) / VU - 0.5) * np.pi
    zz = lat * 0.58 + 1.98                   # pseudo height so the scallops continue from the body
    hsk = dict(sk)
    if "head" in sk:
        hsk["back"] = sk["head"]
        hsk["back2"] = sk["back2"]
    head = _feathers(s.shape, tu, zz, hsk, seed + 100)
    d = np.abs(((tu - 0.5 + 0.5) % 1.0) - 0.5)
    if sk.get("patch"):
        # emperor: golden ear patches on both sides, fading into the chest
        for side in (0.25, 0.75):
            ds = np.abs(((tu - side + 0.5) % 1.0) - 0.5)
            m = W.sstep(0.09, 0.05, np.sqrt((ds * 1.2) ** 2 + ((lat + 0.45) * 0.3) ** 2))
            head = _lerp(head, _h(sk["patch"]), m)
    if sk.get("cheek"):
        for side in (0.5 - 0.115, 0.5 + 0.115):
            ds = np.abs(((tu - side + 0.5) % 1.0) - 0.5)
            r = np.sqrt((ds * 3.2) ** 2 + ((lat + 0.22)) ** 2)
            head = _lerp(head, _h(sk["cheek"]), W.sstep(0.2, 0.05, r) * 0.55)
    # a soft lighter crown sheen
    head = _lerp(head, _h(sk["back2"]), W.sstep(0.9, 1.4, lat) * 0.25)
    # chin: the belly color peeks under the beak (keeps the face friendly)
    chin = W.sstep(0.012, -0.012, np.sqrt((d * 1.9) ** 2 + ((lat + 0.95) * 0.55) ** 2) - 0.2)
    head = _lerp(head, _h(sk["chest"]) * 0.5 + _h(sk["belly"]) * 0.5, chin * 0.9)
    _put(img, HEAD, head)

    # ---------------- flippers: feather color, lighter leading edge and tip, soft underside sheen
    s, t = _coords(FLIP)
    shape = s.shape
    fl = _feathers(shape, s, t * 1.2, sk, seed + 200)
    fl = _lerp(fl, _h(sk["back2"]), W.sstep(0.18, 0.02, s) * 0.55 + W.sstep(0.18, 0.0, t) * 0.3)
    _put(img, FLIP, fl)

    # ---------------- feet: rounded toes with creases, darker toward the heel and sole edge
    s, t = _coords(FOOT)
    foot = _lerp(np.broadcast_to(_h(sk["feet"]), s.shape + (3,)).copy(), _h(sk["feet2"]), W.sstep(0.35, 1.0, t) * 0.6)
    for xc in (0.39, 0.61):
        crease = W.sstep(0.022, 0.0, np.abs(s - xc - (0.35 - t) * 0.12 * np.sign(xc - 0.5))) * W.sstep(0.5, 0.28, t)
        foot = foot * (1 - 0.35 * crease)[..., None]
    # scaly skin rings
    rings = 0.5 + 0.5 * np.sin(t * 90)
    foot *= (0.97 + 0.04 * rings * W.sstep(0.25, 0.6, t) + 0.03 * W.pnoise(s.shape, 30, seed + 300))[..., None]
    # toe nails
    for xc in (0.3, 0.5, 0.7):
        nail = W.sstep(0.06, 0.04, np.sqrt(((s - xc) * 1.4) ** 2 + (t - 0.05) ** 2))
        foot = _lerp(foot, _h(sk["feet2"]) * 0.75, nail * 0.8)
    _put(img, FOOT, foot)

    # ---------------- bill
    for region, lower in ((BEAKU, False), (BEAKL, True)):
        s, t = _coords(region)
        c1, c2 = _h(sk["beak"]), _h(sk["beak2"])
        if lower:
            c1, c2 = c2, c2 * 0.85
        beak = _lerp(np.broadcast_to(c1, s.shape + (3,)).copy(), c2, W.sstep(0.55, 0.0, t) * 0.7)
        beak = _lerp(beak, _h("#ffffff"), W.sstep(0.25, 0.0, np.abs(s - 0.5) * 2 + np.abs(t - 0.55)) * 0.18)   # glossy ridge
        if sk.get("patch") and not lower:
            beak = _lerp(beak, _h(sk["beak2"]), W.sstep(0.75, 0.9, t))
        if not lower:
            for xc in (0.42, 0.58):
                nos = W.sstep(0.03, 0.015, np.sqrt(((s - xc) * 1.0) ** 2 + ((t - 0.72) * 0.6) ** 2))
                beak = beak * (1 - 0.5 * nos)[..., None]
        beak *= (0.98 + 0.03 * W.pnoise(s.shape, 24, seed + 400))[..., None]
        _put(img, region, beak)

    # ---------------- eye white with a soft cool rim; pupil with highlights
    s, t = _coords(EYE)
    r = np.sqrt((s - 0.5) ** 2 + (t - 0.5) ** 2) * 2
    eye = _lerp(np.broadcast_to(_h("#ffffff"), s.shape + (3,)).copy(), _h("#c6d6ea"), W.sstep(0.55, 1.0, r) * 0.8)
    _put(img, EYE, eye)
    s, t = _coords(PUPIL)
    pup = np.broadcast_to(_h("#11131a"), s.shape + (3,)).copy()
    r = np.sqrt((s - 0.5) ** 2 + (t - 0.5) ** 2) * 2
    pup = _lerp(pup, _h("#2c3446"), W.sstep(0.2, 0.95, r) * 0.6 * W.sstep(0.6, 0.2, t))
    if skin == "robot":
        pup = _lerp(pup, _h("#6af0ff"), W.sstep(0.6, 0.3, r) * 0.9)
    if skin == "zombie":
        pup = _lerp(pup, _h("#c8d84a"), W.sstep(0.45, 0.2, r) * 0.6)
    hl = W.sstep(0.16, 0.1, np.sqrt((s - 0.66) ** 2 + (t - 0.7) ** 2))
    hl2 = W.sstep(0.075, 0.045, np.sqrt((s - 0.36) ** 2 + (t - 0.32) ** 2))
    pup = _lerp(pup, _h("#ffffff"), np.clip(hl + hl2 * 0.85, 0, 1))
    _put(img, PUPIL, pup)
    s, t = _coords(TONGUE)
    _put(img, TONGUE, np.broadcast_to(_h("#e8506a"), s.shape + (3,)))
    return img


def write_skins(only=None):
    C.ensure_dir(W.PENGUIN_TEX_DIR)
    for sk in SKINS:
        if only and sk not in only:
            continue
        C.write_png(skin_path(sk), paint(sk))
    print("[penguin_skins] %d skins -> %s" % (len(SKINS), W.PENGUIN_TEX_DIR))
