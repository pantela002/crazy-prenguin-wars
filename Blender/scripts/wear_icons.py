"""Wardrobe icons for the textured wearables: Resources/Icons/Clothes/{id}.png.

Hats, outfits (on a grey dress form), shoes and gloves are the textured clothes models; skins (skin_{name}) are the
whole penguin wearing that skin texture. Usage (repo root):
    python3 Blender/scripts/wear_icons.py [id,id,...]
The full build renders the clothes in `icons.py` (section Clothes) and the skins in the `skin_icons` step
(`build_all.py skin_icons`). Env CPW_SAMPLES (default 24 here).
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.environ.setdefault("CPW_SAMPLES", "24")

import bpy  # noqa: E402

import common as C  # noqa: E402
import render as R  # noqa: E402
import icons as I  # noqa: E402
import penguin as P  # noqa: E402
import penguin_skins as S  # noqa: E402
import wear_kit as W  # noqa: E402

SKIN_VIEW = (0.3, -1.0, 0.22)


def skin_ids():
    """Wardrobe ids of the skins (the classic one is the bare penguin, no item)."""
    return ["skin_" + s for s in S.SKINS if s != S.DEFAULT_SKIN]


def _skin_penguin(col, skin):
    o = P.build(col, with_sockets=False, scarf=False)
    m = P.skin_mat().copy()
    m.name = "icon_skin_" + skin
    tex = [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"][0]
    tex.image = bpy.data.images.load(S.skin_path(skin), check_existing=True)
    for ob in o.values():
        if ob.type == "MESH":
            for i in range(len(ob.data.materials)):
                ob.data.materials[i] = m


def skin_icons(only=None):
    W.ensure_textures()
    S.write_skins()
    I._start()
    n = 0
    for sid in skin_ids():
        if only and sid not in only:
            continue
        I.icon("Clothes/" + sid, lambda col, s=sid[len("skin_"):]: _skin_penguin(col, s), view=SKIN_VIEW, pad=0.06)
        n += 1
    return n


def run(only=None):
    import clothes
    t0 = time.time()
    W.ensure_textures()
    I._start()
    n = 0
    for item, slot in clothes.all_items().items():
        if only and item not in only:
            continue
        I.icon("Clothes/" + item, lambda col, i=item, s=slot: I._clothes_with_form(clothes, i, s))
        n += 1
    n += skin_icons(only)
    print("[wear_icons] %d icons in %.0fs" % (n, time.time() - t0), flush=True)
    return n


if __name__ == "__main__":
    run([x for x in sys.argv[1].split(",") if x] if len(sys.argv) > 1 else None)
