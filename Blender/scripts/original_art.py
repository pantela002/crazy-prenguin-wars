"""Original Crazy Penguin Wars 2D art -> Resources/Textures (run by the textures step after the procedural textures).

Source: the PNG/JPG exports of the original Flash client that ship with the map editor (cpw-mapeditor/assets), found via
$CPW_ORIGINAL_ART, <repo>/OriginalAssets/mapeditor or ../crazy-penguin-wars/cpw-mapeditor/assets. Without a source the
step is skipped and the committed textures stay as they are.

Output (everything resized to power-of-two sizes here so Unity's default import never rescales it; colors are bled into
transparent pixels so bilinear filtering and mipmaps have no dark fringes):
  Textures/Parallax/{Theme}/parallax_A_B.png   level background layers (graphics_export ids of the level files)
  Textures/Sky/{Theme}_Gradient.png             background gradient (whole level height, top row = level top)
  Textures/Terrain/{id}.png                     landmass_bg_tile fill for Wood/Forest, Stone/Mountain, Ice/Winter, Desert
  Textures/Terrain/{id}_Cap.png                 landmass_tile crust composed into the cap strip of TerrainChunk.AddCap
  Textures/Water/{Water,Lava,Mud}.png           water_tile (surface band; the rows below repeat the bottom color)
  Textures/Items/{Material}/{shape}_{size}_{1..3}.png   level object sprites with the three damage stages
  Textures/original_art.txt                     "key widthPx heightPx [extra]" of the original (pre-resize) sizes;
                                                1 world unit = 20 original px (Units.W)
"""
import math
import os

import numpy as np

import common as C

THEMES = ("desert", "forest", "mountain", "winter")
# MaterialTheme.LandmassSWF of the config: which landmass art each terrain material theme used
LANDMASS = {"Wood": "forest", "Forest": "forest", "Stone": "mountain", "Mountain": "mountain",
            "Ice": "winter", "Winter": "winter", "Desert": "desert"}
# liquid (LevelData.liquid) -> water folder (MaterialTheme.WaterSWF)
LIQUIDS = {"Water": "winter", "Lava": "mountain", "Mud": "desert"}
ITEM_MATERIALS = {"wood": "Wood", "stone": "Stone", "ice": "Ice", "metal": "Metal"}


def source_dir():
    cands = [os.environ.get("CPW_ORIGINAL_ART", ""),
             os.path.join(C.ROOT, "OriginalAssets", "mapeditor"),
             os.path.join(C.ROOT, "..", "crazy-penguin-wars", "cpw-mapeditor", "assets"),
             "/home/claude/crazy-penguin-wars/cpw-mapeditor/assets"]
    for c in cands:
        if c and os.path.isdir(os.path.join(c, "parallax")):
            return os.path.abspath(c)
    return None


def load(path):
    from PIL import Image
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0


def bleed(img, passes=12):
    """Spread the color of opaque pixels into transparent ones (alpha unchanged)."""
    rgb = img[..., :3].copy()
    known = img[..., 3] > 0.02
    if known.all() or not known.any():
        return img
    for _ in range(passes):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            k = np.roll(np.roll(known, dy, 0), dx, 1)
            acc += np.roll(np.roll(rgb, dy, 0), dx, 1) * k[..., None]
            cnt += k
        new = (~known) & (cnt > 0)
        rgb[new] = acc[new] / cnt[new][:, None]
        known = known | new
        if known.all():
            break
    if not known.all():
        rgb[~known] = rgb[known].mean(axis=0)
    out = img.copy()
    out[..., :3] = rgb
    return out


def pot(n, lo=4, hi=1024):
    return int(min(hi, max(lo, 2 ** round(math.log2(max(1, n))))))


def resize(img, w, h):
    from PIL import Image
    a = Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    return np.asarray(a.resize((w, h), Image.LANCZOS), dtype=np.float32) / 255.0


def save_pot(img, path, hi=1024):
    h, w = img.shape[:2]
    out = resize(bleed(img), pot(w, hi=hi), pot(h, hi=hi))
    C.write_png(path, out)


def cap_from_tile(tile, per_repeat=4, W=512, H=128):
    """TerrainChunk cap strip (512x128, repeats every 1.6 units, 0.5 units tall) from the 36x24 landmass_tile: tiles
    every 1/4 repeat, each twice that wide (the original steps by half a tile so neighbours overlap), drawn left to
    right like TerrainDisplayObject.drawTopTiles."""
    step = W // per_repeat
    tw = step * 2
    t = bleed(tile)
    t = resize(t, tw, H)
    canvas = np.zeros((H, W * 3, 4), np.float32)
    for i in range(-2, per_repeat * 3 + 1):
        x0 = i * step
        xa, xb = max(0, x0), min(W * 3, x0 + tw)
        if xb <= xa:
            continue
        src = t[:, xa - x0:xb - x0]
        dst = canvas[:, xa:xb]
        a = src[..., 3:4]
        out_a = a + dst[..., 3:4] * (1 - a)
        rgb = (src[..., :3] * a + dst[..., :3] * dst[..., 3:4] * (1 - a)) / np.maximum(out_a, 1e-4)
        canvas[:, xa:xb, :3] = rgb
        canvas[:, xa:xb, 3:4] = out_a
    return bleed(canvas[:, W:2 * W])


def hexmean(img):
    m = img[..., :3].reshape(-1, 3).mean(axis=0)
    return "%02x%02x%02x" % tuple(int(c * 255 + 0.5) for c in m)


def water_surface_row(img):
    """First row (from the top) where every column is at least half as opaque as the bottom of the tile."""
    a = img[..., 3]
    body = a[-1].mean()
    for y in range(a.shape[0]):
        if a[y].min() >= body * 0.5:
            return y
    return 0


def run():
    src = source_dir()
    if src is None:
        print("[original_art] no original art found; keeping the committed textures")
        return
    try:
        import PIL  # noqa: F401
    except ImportError:
        print("[original_art] Pillow missing; keeping the committed textures")
        return
    tex = C.TEXTURES
    manifest = []
    n_par = 0
    for th in THEMES:
        Th = th.capitalize()
        d = os.path.join(src, "parallax", th)
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".png") or "_parallax_" not in fn:
                continue
            export = fn.split("_", 1)[1][:-4]          # "10_parallax_1_5.png" -> "parallax_1_5"
            img = load(os.path.join(d, fn))
            h, w = img.shape[:2]
            save_pot(img, os.path.join(tex, "Parallax", Th, export + ".png"))
            manifest.append("Parallax/%s/%s %d %d" % (Th, export, w, h))
            n_par += 1
        g = os.path.join(src, "gradients", th + ".png")
        if os.path.exists(g):
            img = load(g)
            img[..., 3] = 1
            C.write_png(os.path.join(tex, "Sky", Th + "_Gradient.png"), resize(img, 4, 512))
            manifest.append("Sky/%s_Gradient %d %d" % (Th, img.shape[1], img.shape[0]))
    for mid, th in LANDMASS.items():
        d = os.path.join(src, "terrain", th)
        bg = os.path.join(d, "landmass_bg_tile.jpg")
        if os.path.exists(bg):
            img = load(bg)
            h, w = img.shape[:2]
            C.write_png(os.path.join(tex, "Terrain", mid + ".png"), resize(img, pot(w), pot(h)))
            manifest.append("Terrain/%s %d %d %s" % (mid, w, h, hexmean(img)))
        tile = os.path.join(d, "landmass_tile.png")
        if os.path.exists(tile):
            C.write_png(os.path.join(tex, "Terrain", mid + "_Cap.png"), cap_from_tile(load(tile)))
    # mean colors of the other (procedural) terrain textures: TerrainStyle divides the border colors by them
    tdir = os.path.join(tex, "Terrain")
    done = set(LANDMASS)
    for fn in sorted(os.listdir(tdir)):
        mid = fn[:-4]
        if fn.endswith(".png") and "_" not in mid and mid not in done:
            manifest.append("TerrainMean/%s 1 1 %s" % (mid, hexmean(load(os.path.join(tdir, fn)))))
    for liquid, th in LIQUIDS.items():
        f = os.path.join(src, "water", th, "water_tile.png")
        if not os.path.exists(f):
            continue
        img = load(f)
        h, w = img.shape[:2]
        row = water_surface_row(img)
        bottom = img[-1].mean(axis=0)
        save_pot(img, os.path.join(tex, "Water", liquid + ".png"), hi=256)
        manifest.append("Water/%s %d %d %d %02x%02x%02x%02x" % ((liquid, w, h, row) + tuple(int(c * 255 + 0.5) for c in bottom)))
    n_items = 0
    for mat, Mat in ITEM_MATERIALS.items():
        d = os.path.join(src, "items", mat)
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".png") or "object_particle" in fn:
                continue
            name = fn.split("_", 1)[1][:-4]             # "30_cube_large_1.png" -> "cube_large_1"
            img = load(os.path.join(d, fn))
            h, w = img.shape[:2]
            save_pot(img, os.path.join(tex, "Items", Mat, name + ".png"), hi=512)
            manifest.append("Items/%s/%s %d %d" % (Mat, name, w, h))
            n_items += 1
    with open(os.path.join(tex, "original_art.txt"), "w") as f:
        f.write("\n".join(manifest) + "\n")
    print("[original_art] %d parallax, %d items, from %s" % (n_par, n_items, src))


if __name__ == "__main__":
    run()
