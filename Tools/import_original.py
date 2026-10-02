#!/usr/bin/env python3
"""Copy the original Flash art the game uses from OriginalAssets/ into Assets/CPW/Resources/Original/.

Usage: python3 Tools/import_original.py [--src OriginalAssets] [--dst Assets/CPW/Resources/Original] [--dry-run]

The layout mirrors OriginalAssets (<category>/<swf name>/...), so the paths in OriginalAssets/ID_MAP.md work after
dropping the extension: OriginalAssets/weapons/weapon_animations/bazooka/001.png ->
Resources.Load("Original/weapons/weapon_animations/bazooka/001").

Per folder it writes `_meta.json` (read by Art/OriginalArt.cs at runtime and Editor/OriginalSpriteImporter.cs):
  {"zoom": z,                                   render zoom of the symbols (Flash px -> texture px)
   "files": {"bazooka/001": [px, py, zoom]},    pivot inside that PNG in pixels from its top-left (y down) and zoom
                                                (pixels per Flash px: 1 for embedded bitmaps), every PNG of the folder
   "symbols": {"bazooka": {"f": [files], "q": [file index per timeline frame], "l": {"aim": 10} (0-based),
                           "o": [x, y] registration point in the canvas, "s": [w, h] canvas, "c": {children frame 1}}},
   "bitmaps": {"parallax_1_1": "_bitmaps/parallax_1_1"}}   embedded bitmaps (pivot = centre)
and `_catalog.json` at the root: {"swf": {"weapon_animations": "weapons/weapon_animations"}, "folders": [...]}.

Size: frames are downscaled where the full render is too heavy for phones (GROUPS/SCALE below: penguin, weapons
and missiles 2.5x, UI 1.5x, fx 1.2x, emotes 1.125x; see the total printed at the end), identical frames are
stored once even across symbols (idle == idle01), and scaled PNGs are re-encoded with Pillow's optimiser. Re-run after re-extracting; it deletes and rewrites the destination.
"""
import argparse, hashlib, io, json, multiprocessing, os, shutil, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..'))

# (folder under OriginalAssets, scale of the rendered symbols (1 = as extracted), copy _bitmaps/?)
GROUPS = [
    ('characters/penguin_animations', 2.5 / 3, False),      # 3x -> 2.5x
    ('characters/penguin_overhead_animations', 2.5 / 3, False),
    ('weapons/weapon_animations', 2.5 / 3, False),          # 3x -> 2.5x
    ('missiles/ammo', 2.5 / 3, False),                      # 3x -> 2.5x
    ('fx/particles', 0.6, False),                           # 2x -> 1.2x (soft art)
    ('fx/boosters', 0.6, False),
    ('icons/icons_weapons', 1.0, False),                    # 1x = native bitmap
    ('icons/icons_boosters', 1.0, False),
    ('icons/icons_drops', 0.5, False),                      # 4x -> 2x
    ('icons/icons_maps', 0.5, False),
    ('emotes/emotes', 0.75, False),                         # 1.5x -> 1.125x
    ('ui/home_screen', 0.75, True),                         # 2x -> 1.5x + native bitmaps
    ('ui/ingame', 0.75, True),
    ('ui/character_ui', 0.75, True),
    ('ui/hud_shared', 0.75, True),
    ('ui/popups', 0.75, True),
    ('ui/multiplayer', 0.75, True),
    ('ui/shops_new', 0.75, True),
    ('ui/slot_machine', 0.75, True),
    ('ui/top_bar_popups', 0.75, True),
    ('ui/loading_anim', 0.75, True),
    ('ui/GameLauncher', 0.75, True),
    ('levels/level_bg_desert', 1.0, True),
    ('levels/level_bg_forest', 1.0, True),
    ('levels/level_bg_mountain', 1.0, True),
    ('levels/level_bg_winter', 1.0, True),
    ('terrain/level_assets_desert', 1.0, True),
    ('terrain/level_assets_forest', 1.0, True),
    ('terrain/level_assets_mountain', 1.0, True),
    ('terrain/level_assets_winter', 1.0, True),
    ('terrain/level_assets_terrain_generic', 1.0, True),
    ('liquids/level_lava_mountain', 1.0, True),
    ('liquids/level_mud_desert', 1.0, True),
    ('liquids/level_water_winter', 1.0, True),
    ('level_objects/level_obstacles_wood', 1.0, True),
    ('level_objects/level_obstacles_stone', 1.0, True),
    ('level_objects/level_obstacles_ice', 1.0, True),
    ('level_objects/level_obstacles_metal', 1.0, True),
]
# per-symbol scale overrides (big, soft or full-screen animations)
SCALE = {
    'fx/particles/void_generator_explosion': 0.4,
    'ui/ingame/message_your_turn': 0.5, 'ui/ingame/message_time_alert': 0.5,
    'ui/character_ui/combo_floater': 0.5, 'ui/character_ui/action_points_floater': 0.5,
    'ui/character_ui/weapon_indicator': 0.6,
    'missiles/ammo/orbital_bomb': 0.5,
    # full-screen layouts with placeholder text: reference/backdrop only (rebuild from the _bitmaps + children)
    'ui/home_screen/home_screen': 0.5, 'ui/ingame/ingame_hud': 0.5, 'ui/ingame/popup_choose_item': 0.5,
    'ui/ingame/choose_item_content': 0.5, 'ui/shops_new/shop_screen_new': 0.5,
    'ui/slot_machine/slot_machine_popup': 0.5, 'ui/multiplayer/result_screen': 0.5,
    'ui/multiplayer/multiplayer_custom': 0.5, 'ui/multiplayer/multiplayer_private': 0.5,
    'ui/multiplayer/multiplayer_private_host': 0.5,
}
# extra files copied as they are (src under OriginalAssets -> dst under Original)
EXTRA = [('characters/penguin_rig.json', 'characters/penguin_rig.json')]


def png_bytes(img):
    b = io.BytesIO()
    img.save(b, 'PNG', optimize=True)
    return b.getvalue()


def import_group(job):
    """Copy one OriginalAssets folder; returns (rel, swf stem, bytes written, frames shared)."""
    src, dst, dry, rel, gscale, with_bitmaps = job
    sdir = os.path.join(src, rel)
    idx = json.load(open(os.path.join(sdir, 'index.json')))
    zoom = float(idx['zoom'])
    meta = {'zoom': round(zoom * gscale, 4), 'files': {}, 'symbols': {}, 'bitmaps': {}}
    seen = {}          # (sha1, pivot, zoom) -> stored name: dedupes frames across symbols
    stats = [0, 0]     # bytes written, frames shared

    def store(src_png, name, pivot, z, scale):
        """Write one PNG (scaled) unless an identical one with the same pivot exists; returns its stored name."""
        if scale == 1.0:
            data = open(src_png, 'rb').read()
        else:
            im = Image.open(src_png)
            im.load()
            w = max(1, round(im.width * scale))
            h = max(1, round(im.height * scale))
            pivot = (pivot[0] * w / im.width, pivot[1] * h / im.height)
            # BOX (area average) downscales cleanly and compresses ~25% better than LANCZOS (no ringing)
            data = png_bytes(im.resize((w, h), Image.BOX))
        pivot = [round(pivot[0], 2), round(pivot[1], 2)]
        key = (hashlib.sha1(data).hexdigest(), pivot[0], pivot[1], z)
        if key in seen:
            stats[1] += 1
            return seen[key]
        seen[key] = name
        meta['files'][name] = [pivot[0], pivot[1], z]
        stats[0] += len(data)
        if not dry:
            out = os.path.join(dst, rel, name + '.png')
            os.makedirs(os.path.dirname(out), exist_ok=True)
            open(out, 'wb').write(data)
        return name

    for sym, s in sorted(idx['symbols'].items()):
        scale = SCALE.get(rel + '/' + sym, gscale)
        z = round(zoom * scale, 4)
        ox, oy = s['origin_px']
        names = []
        for k, f in enumerate(s['files']):
            off = s['offsets'][k] if k < len(s['offsets']) else [0, 0]
            names.append(store(os.path.join(sdir, f), os.path.splitext(f)[0], (ox - off[0], oy - off[1]), z, scale))
        seq = s.get('sequence')
        if not seq:
            seq = [0] * max(1, s.get('frames_total') or 1) if len(names) == 1 else list(range(len(names)))
        # dedupe may have mapped several files to one name: re-index the sequence on unique names
        uniq = list(dict.fromkeys(names))
        seq = [uniq.index(names[i]) for i in seq]
        # labels 0-based; some UI timelines were exported truncated (40 of 57 frames): clamp
        labels = {k: max(0, min(v - 1, len(seq) - 1)) for k, v in (s.get('labels') or {}).items()}
        e = {'f': uniq, 'q': seq, 'l': labels, 'o': [round(ox * scale, 2), round(oy * scale, 2)],
             's': [round(s['size'][0] * scale), round(s['size'][1] * scale)]}
        if z != meta['zoom']:
            e['z'] = z
        if s.get('children_frame1'):
            e['c'] = s['children_frame1']
        meta['symbols'][sym] = e

    if with_bitmaps:
        for f, b in sorted(idx.get('bitmaps', {}).items()):
            p = os.path.join(sdir, f)
            if not os.path.exists(p):
                continue
            w, h = b['size']
            name = store(p, os.path.splitext(f)[0], (w / 2.0, h / 2.0), 1.0, 1.0)
            key = b.get('linkage') or os.path.basename(f)
            if key.lower().endswith('.png'):            # terrain linkage names carry the extension
                key = key[:-4]
            meta['bitmaps'][key] = name

    if not dry:
        os.makedirs(os.path.join(dst, rel), exist_ok=True)
        with open(os.path.join(dst, rel, '_meta.json'), 'w') as fh:
            json.dump(meta, fh, separators=(',', ':'), sort_keys=True)
    return rel, os.path.splitext(os.path.basename(idx['swf']))[0], len(meta['files']), stats[0], stats[1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default=os.path.join(REPO, 'OriginalAssets'))
    ap.add_argument('--dst', default=os.path.join(REPO, 'Assets/CPW/Resources/Original'))
    ap.add_argument('--dry-run', action='store_true')
    ap.add_argument('--jobs', type=int, default=os.cpu_count() or 2)
    a = ap.parse_args()
    if not a.dry_run:
        # clear everything this script made, but keep folders generated elsewhere (clothes: Blender/scripts/clothes_sprites.py; ui_skin.json is hand-made)
        keep = {'clothes', 'ui_skin.json'}
        if os.path.isdir(a.dst):
            for name in os.listdir(a.dst):
                if name in keep: continue
                p = os.path.join(a.dst, name)
                shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
        os.makedirs(a.dst, exist_ok=True)
    catalog = {'swf': {}, 'folders': []}
    total = shared = 0
    jobs = [(a.src, a.dst, a.dry_run, rel, sc, bm) for rel, sc, bm in GROUPS]
    with multiprocessing.Pool(max(1, a.jobs)) as pool:
        for rel, stem, nfiles, written, nshared in pool.imap(import_group, jobs):
            catalog['swf'][stem] = rel
            catalog['folders'].append(rel)
            total += written
            shared += nshared
            print('%-42s %4d files %6.1f MB' % (rel, nfiles, written / 1e6))

    for s, d in EXTRA:
        total += os.path.getsize(os.path.join(a.src, s))
        if not a.dry_run:
            shutil.copyfile(os.path.join(a.src, s), os.path.join(a.dst, d))
    if not a.dry_run:
        with open(os.path.join(a.dst, '_catalog.json'), 'w') as fh:
            json.dump(catalog, fh, separators=(',', ':'), sort_keys=True)
    print('total %.1f MB (%d duplicate frames shared)' % (total / 1e6, shared))


if __name__ == '__main__':
    sys.exit(main())
