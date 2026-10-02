"""Complete clothing slot tracks for every original penguin animation (no bpy needed).

The rebuilt public penguin_animations.swf lost the clothing slots in many animations (walk, idle, jump, fall,
dying, spawn... keep only `facial_expression`, weapon variants keep `tool`; see OriginalAssets/MANIFEST.md).
PenguinAvatar needs head_gear / body_gear / left_foot_gear / right_foot_gear (+ accessory_top/under) on every
frame, so this script writes Resources/Original/clothes/rig_fill.json with the missing tracks:

- a track that exists in the original rig is kept as is (frames where it is null borrow the nearest frame);
- head_gear / accessory_*: follow this animation's facial_expression rigidly (F * inv(Fref) * Href), Fref/Href
  being the rest pose of an animation that has both slots;
- body_gear: follows the face translation (k = BODY_FOLLOW), keeps the rest matrix;
- feet: found in the frame image (the two largest orange blobs below the belly), moved by the blob centroid's
  offset from the rest pose; when the feet cannot be told apart the rest pose (or the previous frame) is used;
- animations without any face track (spawn) copy the idle rest pose on frames where the penguin is full size.

Output: {"note", "animations": {anim: {slot: [[a, b, c, d, tx, ty, z] | null per frame]}}} with only the
filled tracks (the runtime merges it over penguin_rig.json). Run from the repo root:
    python3 Blender/scripts/penguin_rig_fill.py [--preview out.png]
"""
import json
import os
import sys

import numpy as np
from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ORIG = os.path.join(REPO, "Assets", "CPW", "Resources", "Original")
PENG = os.path.join(ORIG, "characters", "penguin_animations")
OUT = os.path.join(ORIG, "clothes", "rig_fill.json")

SLOTS = ["head_gear", "body_gear", "left_foot_gear", "right_foot_gear"]
HEADISH = ["head_gear"]
FEET = ["left_foot_gear", "right_foot_gear"]
REST_ANIM = "damagehit_small_weapon"      # frame 0 = the standing rest pose with every slot
BODY_FOLLOW = 0.8
# the tombstone lands on the penguin at frame 25 of dying: no clothes from there on
HIDE_FROM = {"dying": 25}
# draw order relative to facial_expression (from the animations that have every slot: feet 3/6, body 11, face 16, head 19)
Z_REL = {"right_foot_gear": -13, "left_foot_gear": -10, "body_gear": -5, "head_gear": 3, "accessory_under": 2,
         "accessory_top": 4}


def mul(m1, m2):
    a1, b1, c1, d1, tx1, ty1 = m1[:6]
    a2, b2, c2, d2, tx2, ty2 = m2[:6]
    return [a1 * a2 + c1 * b2, b1 * a2 + d1 * b2, a1 * c2 + c1 * d2, b1 * c2 + d1 * d2,
            a1 * tx2 + c1 * ty2 + tx1, b1 * tx2 + d1 * ty2 + ty1]


def inv(m):
    a, b, c, d, tx, ty = m[:6]
    det = a * d - b * c
    ia, ib, ic, id_ = d / det, -b / det, -c / det, a / det
    return [ia, ib, ic, id_, -(ia * tx + ic * ty), -(ib * tx + id_ * ty)]


def r4(m, z):
    return [round(v, 5) for v in m[:4]] + [round(m[4], 2), round(m[5], 2), z]


def nearest_fill(track):
    """Replace null entries by the nearest non-null frame (None if the track is all null)."""
    idx = [i for i, v in enumerate(track) if v]
    if not idx:
        return None
    return [track[i] if track[i] else track[min(idx, key=lambda j: abs(j - i))] for i in range(len(track))]


# ------------------------------------------------------------------------------------------ feet detection

def load_meta():
    with open(os.path.join(PENG, "_meta.json")) as fh:
        return json.load(fh)


_img_cache = {}


def frame_rgba(meta, anim, f):
    s = meta["symbols"][anim]
    fn = s["f"][s["q"][min(f, len(s["q"]) - 1)]]
    if fn not in _img_cache:
        im = np.asarray(Image.open(os.path.join(PENG, fn + ".png")).convert("RGBA")).astype(np.float32) / 255.0
        _img_cache[fn] = im
    px, py, z = meta["files"][fn]
    return _img_cache[fn], px, py, z


def label(mask):
    """4-connected components; returns list of (count, xs, ys)."""
    h, w = mask.shape
    lab = np.zeros((h, w), np.int32)
    comps = []
    n = 0
    for y0 in range(h):
        for x0 in range(w):
            if not mask[y0, x0] or lab[y0, x0]:
                continue
            n += 1
            stack = [(y0, x0)]
            lab[y0, x0] = n
            xs, ys = [], []
            while stack:
                y, x = stack.pop()
                xs.append(x)
                ys.append(y)
                for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                    if 0 <= yy < h and 0 <= xx < w and mask[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n
                        stack.append((yy, xx))
            comps.append((len(xs), np.array(xs), np.array(ys)))
    return comps


def feet_blobs(meta, anim, f):
    """Centroids (flash px, y down, relative to the registration point) of the two feet, sorted by x, or None."""
    im, px, py, z = frame_rgba(meta, anim, f)
    r, g, b, a = im[..., 0], im[..., 1], im[..., 2], im[..., 3]
    orange = (a > 0.6) & (r > 0.75) & (g > 0.45) & (g < 0.9) & (b < 0.4) & (r - b > 0.45)
    ys = (np.arange(im.shape[0]) - py) / z
    orange &= (ys > 8)[:, None]            # below the belly (the beak is at y ~ -20)
    comps = [c for c in label(orange) if c[0] > 25]
    comps.sort(key=lambda c: -c[0])
    if not comps:
        return None
    if len(comps) == 1 or comps[1][0] < comps[0][0] * 0.25:
        # feet touching: split the blob at its median x when it is wide enough
        n, xs, ys_ = comps[0]
        if (xs.max() - xs.min()) / z < 14:
            return None
        m = np.median(xs)
        left, right = xs < m, xs >= m
        pts = [(xs[left].mean(), ys_[left].mean()), (xs[right].mean(), ys_[right].mean())]
    else:
        pts = [(c[1].mean(), c[2].mean()) for c in comps[:2]]
    pts = [((x - px) / z, (y - py) / z) for x, y in pts]
    pts.sort()
    return pts


# ------------------------------------------------------------------------------------------ fill

def main():
    meta = load_meta()
    with open(os.path.join(ORIG, "characters", "penguin_rig.json")) as fh:
        rig = json.load(fh)["animations"]
    rest = {k: v[0] for k, v in rig[REST_ANIM]["slots"].items()}
    face_ref = rest["facial_expression"]
    rest_feet = feet_blobs(meta, REST_ANIM, 0)
    # blob centroid -> slot origin offsets in the rest pose (left blob = smaller x = left_foot_gear)
    foot_off = {"left_foot_gear": (rest["left_foot_gear"][4] - rest_feet[0][0], rest["left_foot_gear"][5] - rest_feet[0][1]),
                "right_foot_gear": (rest["right_foot_gear"][4] - rest_feet[1][0], rest["right_foot_gear"][5] - rest_feet[1][1])}
    out = {}
    report = []
    for anim, data in sorted(rig.items()):
        n = data["frames"]
        slots = data.get("slots", {})
        face = nearest_fill(slots.get("facial_expression", [None] * n))
        filled = {}
        for slot in SLOTS:
            have = slots.get(slot)
            if have and any(have):
                if all(have):
                    continue
                filled[slot] = nearest_fill(have)          # only gaps
                continue
            track = []
            for f in range(n):
                F = face[f] if face else None
                z = (F[6] if F else 16) + Z_REL[slot]
                if slot in HEADISH:
                    if F is None:
                        track.append(None)
                        continue
                    m = mul(mul(F, inv(face_ref)), rest[slot])
                    track.append(r4(m, z))
                elif slot == "body_gear":
                    if F is None:
                        track.append(None)
                        continue
                    B = rest[slot]
                    track.append(r4(B[:4] + [B[4] + (F[4] - face_ref[4]) * BODY_FOLLOW,
                                             B[5] + (F[5] - face_ref[5]) * BODY_FOLLOW], z))
                else:
                    if F is None:
                        track.append(None)
                        continue
                    blobs = feet_blobs(meta, anim, f)
                    i = 0 if slot == "left_foot_gear" else 1
                    if blobs is None:
                        prev = track[-1] if track else None
                        track.append(prev if prev else r4(rest[slot], z))
                        continue
                    ox, oy = foot_off[slot]
                    track.append(r4(rest[slot][:4] + [blobs[i][0] + ox, blobs[i][1] + oy], z))
            if face is None:
                # no face at all (spawn): idle rest pose once the penguin is full size
                track = []
                for f in range(n):
                    im, px, py, zz = frame_rgba(meta, anim, f)
                    a = im[..., 3] > 0.5
                    rows = np.where(a.any(axis=1))[0]
                    tall = (rows.max() - rows.min()) / zz if len(rows) else 0
                    track.append(r4(rest[slot], 16 + Z_REL[slot]) if tall > 58 else None)
            for f in range(HIDE_FROM.get(anim, n), n):
                track[f] = None
            filled[slot] = track
        if filled:
            out[anim] = filled
            report.append("%-24s %s" % (anim, ",".join(sorted(filled))))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w") as fh:
        json.dump({"note": "Missing clothing slot tracks of penguin_rig.json, generated by "
                           "Blender/scripts/penguin_rig_fill.py (same format: [a,b,c,d,tx,ty,z] per frame, null = hidden). "
                           "PenguinAvatar uses a track from here only when penguin_rig.json has none.",
                   "animations": out}, fh, separators=(",", ":"))
    print("\n".join(report))
    print("wrote", OUT)
    return out


def check_feet():
    """Compare detected feet with the real tracks of animations that have them (pixel error per frame)."""
    meta = load_meta()
    with open(os.path.join(ORIG, "characters", "penguin_rig.json")) as fh:
        rig = json.load(fh)["animations"]
    rest = rig[REST_ANIM]["slots"]
    rb = feet_blobs(meta, REST_ANIM, 0)
    offl = (rest["left_foot_gear"][0][4] - rb[0][0], rest["left_foot_gear"][0][5] - rb[0][1])
    offr = (rest["right_foot_gear"][0][4] - rb[1][0], rest["right_foot_gear"][0][5] - rb[1][1])
    for anim in ["win", "stunned", "lose_01", "jump_small_weapon", "landjump_punch", "fire_punch"]:
        errs = []
        for f in range(rig[anim]["frames"]):
            L, Rr = rig[anim]["slots"]["left_foot_gear"][f], rig[anim]["slots"]["right_foot_gear"][f]
            bl = feet_blobs(meta, anim, f)
            if not (L and Rr):
                continue
            if bl is None:
                errs.append(-1)
                continue
            e = max(abs(bl[0][0] + offl[0] - L[4]) + abs(bl[0][1] + offl[1] - L[5]),
                    abs(bl[1][0] + offr[0] - Rr[4]) + abs(bl[1][1] + offr[1] - Rr[5]))
            errs.append(round(e, 1))
        print(anim, errs)


if __name__ == "__main__":
    if "--check" in sys.argv:
        check_feet()
    else:
        main()
