"""Preview renders of the textured penguin, skins and outfits (for checking the art, not shipped).

Usage (repo root):
    python3 Blender/scripts/wear_preview.py out.png [--skin Galaxy] [--view front|34|side|back] [--size 512]
        [--wear flannel_head,flannel_chest,flannel_feet,boxing_gloves] [--wardrobe]
--wardrobe renders a grid with every outfit set (one penguin per set) instead.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.environ.setdefault("CPW_SAMPLES", "20")

import bpy  # noqa: E402,F401
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import common as C  # noqa: E402
import render as R  # noqa: E402
import penguin as P  # noqa: E402
import penguin_skins as S  # noqa: E402
import wear_kit as W  # noqa: E402

VIEWS = {"front": (0.0, -1.0, 0.12), "34": (0.42, -1.0, 0.3), "side": (1.0, -0.15, 0.12), "back": (0.3, 1.0, 0.25),
         "game": (0.0, -1.0, 0.0)}


def set_skin(skin):
    m = bpy.data.materials.get("T_penguin__white")
    if m is None:
        return
    it = [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"][0]
    it.image = bpy.data.images.load(S.skin_path(skin), check_existing=True)


def dress(col, items):
    import clothes as CL
    C.use_collection(col)
    for item in items:
        for ob in CL.place_items(item):
            pass
    C.use_collection(None)


def penguin(col, skin="classic", items=(), offset=(0, 0, 0)):
    C.use_collection(col)
    o = P.build(col, with_sockets=False, scarf=False)
    C.use_collection(None)
    set_skin(skin)
    objs = list(o.values())
    if items:
        import clothes as CL
        C.use_collection(col)
        for item in items:
            objs += CL.place_items(item)
        C.use_collection(None)
    if any(offset):
        for ob in objs:
            ob.location = ob.location + Vector(offset)
    return objs


def render(out, cols, view="34", size=(512, 512), pad=0.06):
    R.frame_camera(cols, VIEWS.get(view, view), pad=pad, size=size)
    for c in bpy.context.scene.collection.children:
        if c.name != "IconRig":
            c.hide_render = c not in cols
    img = R.render_raw(os.path.join(R._tmp, "pv.png"))
    img = R.stylize(img, outline=True, shadow=False)
    bg = np.ones_like(img)
    bg[..., :3] = (0.78, 0.86, 0.95)
    out_img = R._over(img, bg)
    out_img[..., 3] = 1
    C.write_png(out, out_img)


def main(argv):
    out = argv[0]
    skin = "classic"
    view = "34"
    size = 512
    items = []
    grid = None
    i = 1
    while i < len(argv):
        a = argv[i]
        if a == "--skin":
            skin = argv[i + 1]; i += 1
        elif a == "--view":
            view = argv[i + 1]; i += 1
        elif a == "--size":
            size = int(argv[i + 1]); i += 1
        elif a == "--wear":
            items = [x for x in argv[i + 1].split(",") if x]; i += 1
        elif a == "--skins":
            grid = "skins"
        elif a == "--sets":
            grid = argv[i + 1].split(","); i += 1
        i += 1
    W.ensure_textures()
    if not os.path.exists(S.skin_path(skin)):
        S.write_skins()
    C.reset()
    R.setup_scene()
    col = C.new_collection("PV")
    if grid == "skins":
        names = list(S.SKINS)
        for k, sk in enumerate(names):
            c = C.new_collection("PV%d" % k)
            o = P.build(c, with_sockets=False, scarf=False)
            m = P.skin_mat().copy()
            m.name = "skin_" + sk
            it = [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"][0]
            it.image = bpy.data.images.load(S.skin_path(sk), check_existing=True)
            for ob in o.values():
                if ob.type == "MESH":
                    ob.data.materials[0] = m
                    ob.location = ob.location + Vector(((k % 5) * 2.2, 0, -(k // 5) * 3.0))
        cols = [c for c in bpy.context.scene.collection.children if c.name.startswith("PV")]
        render(out, cols, view, (size * 5 // 2, size * 6 // 5 * 2 // 2), pad=0.03)
        return
    if grid:
        import clothes as CL
        for k, s in enumerate(grid):
            c = C.new_collection("PV%d" % k)
            its = [x for x in CL.all_items() if x.startswith(s + "_") or x == s]
            penguin(c, skin, its, offset=((k % 4) * 2.6, 0, -(k // 4) * 3.4))
        cols = [c for c in bpy.context.scene.collection.children if c.name.startswith("PV")]
        rows = (len(grid) + 3) // 4
        render(out, cols, view, (size * 2, int(size * 2 * rows * 3.4 / (4 * 2.6))), pad=0.03)
        return
    penguin(col, skin, items)
    render(out, [col], view, (size, size))


if __name__ == "__main__":
    main(sys.argv[1:])
