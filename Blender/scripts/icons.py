"""Renders every icon with Cycles (see render.py for the look) into Resources/Icons/{Category}/{id}.png."""
import bpy  # noqa: F401
import os
import time

import numpy as np
from mathutils import Matrix

import common as C
import render as R
import icon_models as IM

OUT = C.ICONS


def _clear(col):
    for o in list(col.all_objects):
        bpy.data.objects.remove(o)
    bpy.data.collections.remove(col)
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)


def icon(path, build, view=R.VIEW_34, size=256, pad=0.1):
    """build(col) creates objects in col. Renders to Icons/{path}.png."""
    col = C.new_collection("ICON")
    C.use_collection(col)
    try:
        build(col)
    finally:
        C.use_collection(None)
    out = os.path.join(OUT, path + ".png")
    R.render_icon([col], out, view=view, size=size, pad=pad)
    _clear(col)
    return out


def objs_builder(fn, name):
    def b(col):
        objs = fn(name + "_")
        C.join([o for o in objs if o is not None], name)
    return b


def _start():
    C.reset()
    R.setup_scene()


def weapons_icons(only=None):
    import weapons
    _start()
    n = 0
    for wid in C.ids("WeaponIcon") + [w for w in ("HeatSeeker", "GreyGoo", "ShieldWall", "ChocoCannon")
                                       if w not in C.ids("WeaponIcon")]:
        if only and wid not in only:
            continue
        gid = wid if wid in weapons.WEAPONS else wid
        icon("Weapons/" + wid, lambda col, g=gid: weapons.build(g, col), view=R.VIEW_SIDE)
        n += 1
    return n


def booster_icons():
    _start()
    n = 0
    for bid in C.ids("BoosterIcon"):
        fn = IM.BOOSTERS.get(bid)
        if fn is None:
            continue
        icon("Boosters/" + bid, objs_builder(fn, bid))
        n += 1
    return n


def clothes_icons(only=None):
    import clothes
    _start()
    n = 0
    for item, slot in clothes.all_items().items():
        if only and item not in only:
            continue
        icon("Clothes/" + item, lambda col, i=item, s=slot: _clothes_with_form(clothes, i, s))
        n += 1
    return n


def _clothes_with_form(clothes, item, slot):
    """Chest items get a light grey dress form inside so the icon reads as a garment, not a pot."""
    import penguin as P
    ob = clothes.build_item(item, slot)
    if slot == "chest":
        prof = [(r * 0.98, z) for (r, z) in P.body_profile() if 0.2 <= z <= 1.6] + [(0.0, 1.6)]
        form = C.lathe("Form", prof, C.mat("form", "#c9ced8"), segs=20, scale=(1, P.BODY_DEPTH, 1))
        form.data.transform(Matrix.Translation(-P.CHEST))
        form.data.transform(P.yaw_matrix().to_4x4())
    return ob


def trophy_icons():
    _start()
    n = 0
    for tid, fn in IM.TROPHIES.items():
        icon("Trophies/" + tid, objs_builder(fn, tid), view=(0.25, -1.0, 0.25))
        n += 1
    return n


def emoticon_icons():
    _start()
    n = 0
    for eid in C.ids("Emoticon"):
        short = eid[len("Emoticon"):] if eid.startswith("Emoticon") else eid
        icon("Emoticons/" + eid, objs_builder(IM.emoticon(short), eid), view=(0.12, -1.0, 0.18), pad=0.06)
        n += 1
    return n


def slot_icons():
    _start()
    for sid, fn in IM.SLOT.items():
        icon("Slot/" + sid, objs_builder(fn, sid), view=(0.2, -1.0, 0.25))
    return len(IM.SLOT)


def ui_icons():
    _start()
    for uid, fn in IM.UI.items():
        icon("Ui/" + uid, objs_builder(fn, uid), view=(0.3, -1.0, 0.35))
    for hid, fn in IM.HUD.items():
        icon("Hud/" + hid, objs_builder(fn, hid), view=(0.0, -1.0, 0.05) if hid != "Emote" else (0.12, -1.0, 0.18))
    return len(IM.UI) + len(IM.HUD)


def crafting_icons():
    _start()
    names = ["ScrapMetal", "Gunpowder", "FishBones", "IceShard", "RubberDuck", "PlasmaCore", "GoldenFeather", "UraniumPebble"]
    for nm in names:
        icon("Crafting/" + nm, objs_builder(IM.ingredient(nm), nm), view=(0.3, -1.0, 0.35))
    return len(names)


def achievement_icons(size=192):
    _start()
    ids = C.ids("Achievements")
    for aid in ids:
        icon("Achievements/" + aid, objs_builder(IM.achievement(aid), aid), view=(0.2, -1.0, 0.2), size=size)
    return len(ids)


def app_icon(size=1024):
    _start()
    col = C.new_collection("ICON")
    C.use_collection(col)
    IM.app_penguin("App")
    C.use_collection(None)
    R.frame_camera([col], (0.15, -1.0, 0.22), pad=0.06, size=(size, size))
    for c in bpy.context.scene.collection.children:
        if c.name != "IconRig":
            c.hide_render = c is not col
    img = R.render_raw(os.path.join(R._tmp, "app.png"))
    img = R.stylize(img, outline=True, shadow=True)
    # opaque background: radial sky gradient with a sunburst
    h, w = img.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    cx, cy = w * 0.5, h * 0.42
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (w * 0.75)
    inner, outer = np.array(C.hex_rgb("#ffe27a")), np.array(C.hex_rgb("#ff8a2a"))
    bg = inner[None, None] * (1 - np.clip(d, 0, 1)[..., None]) + outer[None, None] * np.clip(d, 0, 1)[..., None]
    ang = np.arctan2(yy - cy, xx - cx)
    rays = (np.sin(ang * 12) > 0.3).astype(np.float32) * 0.12 * np.clip(d * 2, 0, 1)
    bg = np.clip(bg + rays[..., None], 0, 1)
    base = np.concatenate([bg, np.ones((h, w, 1), np.float32)], axis=-1)
    out = R._over(img, base)
    out[..., 3] = 1
    C.write_png(os.path.join(OUT, "Ui", "app_icon.png"), out)
    _clear(col)
    return 1


def run(sections=None):
    t0 = time.time()
    counts = {}
    todo = [("Weapons", weapons_icons), ("Boosters", booster_icons), ("Clothes", clothes_icons), ("Trophies", trophy_icons),
            ("Emoticons", emoticon_icons), ("Slot", slot_icons), ("Ui+Hud", ui_icons), ("Crafting", crafting_icons),
            ("Achievements", achievement_icons), ("AppIcon", app_icon)]
    for name, fn in todo:
        if sections and name not in sections:
            continue
        t = time.time()
        counts[name] = fn()
        print("[icons] %-12s %3d in %.0fs" % (name, counts[name], time.time() - t), flush=True)
    print("[icons] total %d in %.0fs" % (sum(counts.values()), time.time() - t0))
    return counts
