"""Renders the Blender clothes (clothes.py) to 2D sprites that sit on the ORIGINAL Flash penguin's clothing slots.

No original clothing art survived (OriginalAssets/MANIFEST.md), so every item is drawn from the 3D model: the item
is put on the Blender penguin (whose parts are Cycles holdouts, so whatever the body hides is cut away), rendered
with a front orthographic camera, squeezed per slot so the Blender proportions (big round head, fat egg body)
match the original penguin's (small head, tall egg), and finished like the Flash art: thick dark outline, no shadow.

Per slot the mapping is an axis-aligned affine from the camera plane (Blender units, u right / v up) to Flash
px (x right / y down): x = KX * (u - anchor_u) + X0, y = -KY * (v - anchor_v) + Y0, where the anchor is the
item's socket on the Blender penguin and (X0, Y0) the slot origin in the rest pose of the original rig
(characters/penguin_rig.json, damagehit_small_weapon frame 0 == idle frame 0). The sprite pivot is that slot
origin, so PenguinAvatar can place it with slot(frame) * inverse(slot(rest)).

Output (Resources/Original/clothes/):
    <id>.png (head / body items), <id>_l.png + <id>_r.png (feet: left_foot_gear / right_foot_gear)
    _meta.json  {"zoom": 2.5, "files": {"<file>": [pivot_px_x, pivot_px_y, zoom]},
                 "items": {"<id>": {"slot": "head", "parts": {"head_gear": "<file>"}}}}
_meta.json uses the same "files" format as the imported original art, so Editor/OriginalSpriteImporter sets the
pivot and pixelsPerUnit (20 x zoom) on import.

Usage (repo root): python3 Blender/scripts/clothes_sprites.py [item ...]      (CPW_SAMPLES=24 by default)
                   python3 Blender/scripts/clothes_sprites.py --calib out.png  (naked Blender parts over idle 0)
                   python3 Blender/scripts/clothes_sprites.py --preview out.png [item ...]
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.environ.setdefault("CPW_SAMPLES", "24")

import bpy  # noqa: E402,F401
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import common as C  # noqa: E402
import render as R  # noqa: E402
import penguin as P  # noqa: E402
import clothes as CL  # noqa: E402

ORIG = os.path.join(C.RES, "Original")
OUT = os.path.join(ORIG, "clothes")
ZOOM = 2.5                      # texture px per Flash px, as the penguin animations
SS = 4                          # render supersampling before the squeeze
VIEW = Vector((0.0, -1.0, 0.16)).normalized()   # camera looks along -VIEW (from the front, a little above)
OUTLINE_PX = 3                  # outline radius in output px (the Flash penguin's line is ~1.2 Flash px)
OUTLINE_RGB = (0.06, 0.09, 0.11)

# per slot: socket on the Blender penguin, slot origin in the original rest pose (Flash px, y down) and squeeze
CAL = {   # tuned with --calib against idle frame 0
    # head items sit 2.5 px higher than the fitted head: the original's eyes fill the top half of its head
    "head": {"socket": "HeadSocket", "slots": ["head_gear"], "kx": 11.6, "ky": 11.6, "dx": 0.4, "dy": -1.7},
    # chest items stop at the original's chin (y -21)
    "chest": {"socket": "ChestSocket", "slots": ["body_gear"], "kx": 16.3, "ky": 27.0, "dx": 0.85, "dy": 4.35, "top": -21.0},
    "feet": {"socket": None, "slots": ["left_foot_gear", "right_foot_gear"], "kx": 19.0, "ky": 26.0, "dx": 0.0, "dy": 0.0,
             "d": {"left_foot_gear": (-1.7, -3.3), "right_foot_gear": (-2.8, -4.5)}},
}
# Blender foot -> rig slot: FootSocketL turns to the screen right and away from the camera = the rig's right_foot_gear
FOOT_SOCKET = {"left_foot_gear": "FootSocketR", "right_foot_gear": "FootSocketL"}
REST_ANIM = "damagehit_small_weapon"
if os.environ.get("CPW_CAL"):        # tuning: CPW_CAL='{"head": {"kx": 12}}'
    for _k, _v in json.loads(os.environ["CPW_CAL"]).items():
        CAL[_k].update(_v)


def rest_slots():
    with open(os.path.join(ORIG, "characters", "penguin_rig.json")) as fh:
        rig = json.load(fh)["animations"]
    return {k: v[0] for k, v in rig[REST_ANIM]["slots"].items()}


REST = rest_slots()


def cam_axes():
    d = VIEW
    right = Vector((0, 0, 1)).cross(d).normalized()
    up = d.cross(right).normalized()
    return right, up, d


def project(p):
    right, up, _ = cam_axes()
    return p.dot(right), p.dot(up)


def setup():
    C.reset()
    R.setup_scene(transparent=True)
    sc = bpy.context.scene
    sc.cycles.samples = int(os.environ.get("CPW_SAMPLES", "24"))
    col = C.new_collection("Penguin")
    parts = P.build(col, with_sockets=False)
    return col, parts


def set_holdout(col, on):
    for o in list(col.all_objects):     # copy: changing objects invalidates the all_objects cache
        o.is_holdout = on


def render_region(cols, umin, umax, vmin, vmax, ppu):
    """Render the camera-plane rectangle [umin,umax] x [vmin,vmax] (units) at ppu px per unit; RGBA float."""
    sc = bpy.context.scene
    cam = sc.camera
    right, up, d = cam_axes()
    w, h = umax - umin, vmax - vmin
    rx, ry = max(4, int(math.ceil(w * ppu))), max(4, int(math.ceil(h * ppu)))
    w, h = rx / ppu, ry / ppu
    cam.data.ortho_scale = max(w, h)
    cu, cv = umin + w / 2, vmin + h / 2
    cam.location = right * cu + up * cv + d * 12
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = 0.01
    cam.data.clip_end = 40
    sc.render.resolution_x, sc.render.resolution_y = rx, ry
    for c in sc.collection.children:
        if c.name != "IconRig":
            c.hide_render = c not in cols
    img = R.render_raw(os.path.join(R._tmp, "cs.png"))
    return img, umin, vmin + h       # image, u of the left edge, v of the top edge


def resize(img, sx, sy):
    """Anisotropic resample of a float RGBA image (premultiplied for clean edges)."""
    from PIL import Image
    h, w = img.shape[:2]
    nw, nh = max(1, int(round(w * sx))), max(1, int(round(h * sy)))
    pm = img.copy()
    pm[..., :3] *= pm[..., 3:4]
    chans = [np.asarray(Image.fromarray(pm[..., i].astype(np.float32), "F").resize((nw, nh), Image.LANCZOS)) for i in range(4)]
    out = np.stack(chans, axis=-1)
    out = np.clip(out, 0, 1)
    a = out[..., 3:4]
    out[..., :3] = np.where(a > 1e-4, out[..., :3] / np.maximum(a, 1e-4), 0)
    return np.clip(out, 0, 1)


def outline(img, r=OUTLINE_PX):
    a = img[..., 3]
    o = R._dilate(a, r)
    o = np.clip(R._blur(o, 1, 1) * 1.2, 0, 1)
    ol = np.zeros_like(img)
    ol[..., :3] = OUTLINE_RGB
    ol[..., 3] = o
    return R._over(img, ol)


def crop(img, piv, pad=2):
    a = img[..., 3] > 0.02
    ys, xs = np.where(a)
    if len(xs) == 0:
        return None, piv
    x0, x1 = max(0, xs.min() - pad), min(img.shape[1], xs.max() + 1 + pad)
    y0, y1 = max(0, ys.min() - pad), min(img.shape[0], ys.max() + 1 + pad)
    return img[y0:y1, x0:x1], (piv[0] - x0, piv[1] - y0)


def socket_pos(name):
    return P.socket_world(name)


def render_part(item_col, peng_col, cal, socket_name, rslot, margin=0.25):
    """Render the item collection for one slot. Returns (rgba, pivot_px) with the pivot at the slot origin."""
    pts = R.visible_points([item_col])
    if not pts:
        return None, None
    uv = [project(p) for p in pts]
    us, vs = [p[0] for p in uv], [p[1] for p in uv]
    umin, umax, vmin, vmax = min(us) - margin, max(us) + margin, min(vs) - margin, max(vs) + margin
    kx, ky = cal["kx"], cal["ky"]
    ppu = max(kx, ky) * ZOOM * SS
    if peng_col is not None:
        set_holdout(peng_col, True)
    img, u_left, v_top = render_region([item_col] + ([peng_col] if peng_col is not None else []), umin, umax, vmin, vmax, ppu)
    sx, sy = kx * ZOOM / ppu, ky * ZOOM / ppu
    img = resize(img, sx, sy)
    # anchor (socket) in output px; the slot origin sits there (+ calibration nudge in Flash px)
    au, av = project(socket_pos(socket_name))
    dx, dy = cal.get("d", {}).get(rslot, (cal["dx"], cal["dy"]))
    piv = ((au - u_left) * kx * ZOOM - dx * ZOOM, (v_top - av) * ky * ZOOM - dy * ZOOM)
    pad = OUTLINE_PX + 2
    img = np.pad(img, ((pad, pad), (pad, pad), (0, 0)))
    piv = (piv[0] + pad, piv[1] + pad)
    if cal.get("top") is not None:
        # fade out what rises above the original penguin's neck (Flash y of the rest pose)
        rest_y = REST[rslot][5]
        ys = (np.arange(img.shape[0]) - piv[1]) / ZOOM + rest_y
        img[..., 3] *= np.clip((ys - cal["top"]) / 1.5, 0, 1)[:, None]
    img = outline(img)
    return crop(img, piv)


def item_collection(item, slot, socket_name):
    col = C.new_collection("Item_" + item + "_" + socket_name)
    C.use_collection(col)
    ob = CL.build_item(item, slot)
    C.use_collection(None)
    ob.location = socket_pos(socket_name)
    return col


def clear(col):
    for o in list(col.all_objects):
        bpy.data.objects.remove(o)
    bpy.data.collections.remove(col)


def run(only=None):
    peng_col, _ = setup()
    rest = rest_slots()
    meta_path = os.path.join(OUT, "_meta.json")
    meta = {"zoom": ZOOM, "files": {}, "items": {}}
    if only and os.path.exists(meta_path):
        with open(meta_path) as fh:
            meta = json.load(fh)
    C.ensure_dir(OUT)
    n = 0
    for item, slot in CL.all_items().items():
        if only and item not in only:
            continue
        cal = CAL[slot]
        parts = {}
        for rslot in cal["slots"]:
            sock = cal["socket"] or FOOT_SOCKET[rslot]
            col = item_collection(item, slot, sock)
            img, piv = render_part(col, peng_col, cal, sock, rslot)
            clear(col)
            if img is None:
                continue
            fname = item if slot != "feet" else item + ("_l" if rslot == "left_foot_gear" else "_r")
            C.write_png(os.path.join(OUT, fname + ".png"), img)
            meta["files"][fname] = [round(piv[0], 2), round(piv[1], 2), ZOOM]
            parts[rslot] = fname
        if parts:
            meta["items"][item] = {"slot": slot, "parts": parts}
            n += 1
        print("[clothes_sprites] %s -> %s" % (item, ",".join(parts.values())), flush=True)
    meta["note"] = ("Clothes rendered from the Blender models by Blender/scripts/clothes_sprites.py; files = "
                    "[pivot px x, pivot px y (from the top-left), zoom]; the pivot is the clothing slot origin of the "
                    "original rig rest pose (characters/penguin_rig.json, %s frame 0)." % REST_ANIM)
    with open(meta_path, "w") as fh:
        json.dump(meta, fh, indent=1, sort_keys=True)
    print("[clothes_sprites] %d items -> %s" % (n, OUT))


# ------------------------------------------------------------------------------------------ previews

def _paste(base, sprite, piv, m, ox, oy, s):
    """Draw sprite (PIL RGBA, pivot px, zoom ZOOM) with flash matrix m relative to (ox, oy) at s px per flash px."""
    from PIL import Image
    a, b, c, d, tx, ty = m[:6]
    k = s / ZOOM
    A = [[a * k, c * k], [b * k, d * k]]
    off = (s * tx + ox - (A[0][0] * piv[0] + A[0][1] * piv[1]), s * ty + oy - (A[1][0] * piv[0] + A[1][1] * piv[1]))
    det = A[0][0] * A[1][1] - A[0][1] * A[1][0]
    ia, ib, ic, id_ = A[1][1] / det, -A[0][1] / det, -A[1][0] / det, A[0][0] / det
    t = sprite.transform(base.size, Image.AFFINE, (ia, ib, -(ia * off[0] + ib * off[1]), ic, id_, -(ic * off[0] + id_ * off[1])),
                         resample=Image.BILINEAR)
    base.alpha_composite(t)


def preview(out_path, items, anims=("idle", "walk", "jump_small_weapon", "fire_small_weapon", "win", "damagefall")):
    """Composite rendered clothes over original animation frames (needs a previous run)."""
    from PIL import Image, ImageDraw
    with open(os.path.join(OUT, "_meta.json")) as fh:
        meta = json.load(fh)
    peng = os.path.join(ORIG, "characters", "penguin_animations")
    with open(os.path.join(peng, "_meta.json")) as fh:
        pm = json.load(fh)
    with open(os.path.join(ORIG, "characters", "penguin_rig.json")) as fh:
        rig = json.load(fh)["animations"]
    with open(os.path.join(OUT, "rig_fill.json")) as fh:
        fill = json.load(fh)["animations"]
    rest = rest_slots()
    S, W, H = 3, 200, 250
    tiles = []
    for outfit in items:
        for anim in anims:
            n = rig[anim]["frames"]
            for f in (0, n // 3, 2 * n // 3):
                tile = Image.new("RGBA", (W, H), (190, 215, 240, 255))
                ox, oy = W // 2, H // 2 + 10
                s = pm["symbols"][anim]
                fn = s["f"][s["q"][f]]
                px, py, z = pm["files"][fn]
                body = Image.open(os.path.join(peng, fn + ".png")).convert("RGBA")
                _paste(tile, body, (px, py), [z / ZOOM * 1, 0, 0, z / ZOOM * 1, 0, 0], ox, oy, S * ZOOM / z)
                layers = []
                for item in outfit.split("+"):
                    info = meta["items"].get(item)
                    if not info:
                        continue
                    for rslot, fname in info["parts"].items():
                        tr = rig[anim]["slots"].get(rslot) or fill.get(anim, {}).get(rslot)
                        if not tr or not tr[f]:
                            continue
                        m = tr[f]
                        r = rest[rslot]
                        # m * inverse(rest) * (identity at the rest origin): rel = m[:4] * inv(r[:4]), at m's origin
                        det = r[0] * r[3] - r[1] * r[2]
                        ia, ib, ic, id_ = r[3] / det, -r[1] / det, -r[2] / det, r[0] / det
                        ra = m[0] * ia + m[2] * ib
                        rb = m[1] * ia + m[3] * ib
                        rc = m[0] * ic + m[2] * id_
                        rd = m[1] * ic + m[3] * id_
                        layers.append((m[6], fname, [ra, rb, rc, rd, m[4], m[5]]))
                for _, fname, mm in sorted(layers):
                    spr = Image.open(os.path.join(OUT, fname + ".png")).convert("RGBA")
                    _paste(tile, spr, meta["files"][fname][:2], mm, ox, oy, S)
                ImageDraw.Draw(tile).text((3, 3), "%s %s %d" % (outfit[:18], anim, f), fill="black")
                tiles.append(tile)
    cols = 9
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGBA", (W * cols, H * rows), "white")
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % cols) * W, (i // cols) * H))
    sheet.save(out_path)
    print("preview ->", out_path)


def calib(out_path):
    """Naked Blender head/body/feet rendered with each slot's mapping (tinted) over the original idle frame 0,
    one tile per slot, grid every 5 Flash px (bold through the registration point)."""
    from PIL import Image, ImageDraw
    peng_col, parts = setup()
    rest = rest_slots()
    peng = os.path.join(ORIG, "characters", "penguin_animations")
    with open(os.path.join(peng, "_meta.json")) as fh:
        pm = json.load(fh)
    S = 6
    W, H = 60 * S, 80 * S
    sheet = Image.new("RGBA", (W * 3, H), (255, 255, 255, 255))
    fn = pm["symbols"]["idle"]["f"][0]
    px, py, z = pm["files"][fn]
    body = Image.open(os.path.join(peng, fn + ".png")).convert("RGBA")
    groups = {"head": ["Head", "Beak"], "chest": ["Body", "Belly"], "feet": ["FootL", "FootR"]}
    for i, (slot, names) in enumerate(groups.items()):
        base = Image.new("RGBA", (W, H), (255, 255, 255, 255))
        ox, oy = W // 2, int(H * 0.47)
        d = ImageDraw.Draw(base)
        for k in range(-12, 13):
            c = (120, 120, 120, 255) if k == 0 else (220, 220, 220, 255)
            d.line([(ox + k * 5 * S, 0), (ox + k * 5 * S, H)], fill=c)
            d.line([(0, oy + k * 5 * S), (W, oy + k * 5 * S)], fill=c)
        _paste(base, body, (px, py), [1, 0, 0, 1, 0, 0], ox, oy, S * ZOOM / z)
        cal = CAL[slot]
        col = C.new_collection("Calib_" + slot)
        for nm in names:
            o = parts[nm]
            for c in list(o.users_collection):
                c.objects.unlink(o)
            col.objects.link(o)
        over = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        for rslot in cal["slots"]:
            sock = cal["socket"] or FOOT_SOCKET[rslot]
            img, piv = render_part(col, None, dict(cal), sock, rslot)   # the naked part itself
            if img is None:
                continue
            img = img.copy()
            img[..., :3] = {"head": (1, 0, 0), "chest": (0, 0.7, 0), "feet": (0, 0, 1)}[slot]
            img[..., 3] = (img[..., 3] > 0.5) * 0.4
            spr = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGBA")
            r = rest[rslot]
            _paste(over, spr, piv, [1, 0, 0, 1, r[4], r[5]], ox, oy, S)
        base.alpha_composite(over)
        ImageDraw.Draw(base).text((4, 4), "%s kx=%s ky=%s dx=%s dy=%s" % (slot, cal["kx"], cal["ky"], cal["dx"], cal["dy"]), fill="black")
        sheet.paste(base, (i * W, 0))
    sheet.save(out_path)
    print("calib ->", out_path)


if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "--calib":
        calib(args[1])
    elif args and args[0] == "--preview":
        preview(args[1], args[2:] or ["flannel_head+flannel_chest+flannel_feet"])
    else:
        run(args or None)
