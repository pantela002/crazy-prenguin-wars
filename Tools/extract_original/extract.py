#!/usr/bin/env python3
"""Extract the original Crazy Penguin Wars (Flash) art into OriginalAssets/.

Re-run:   python3 Tools/extract_original/extract.py [--src /home/claude/crazy-penguin-wars]
                  [--ffdec path/to/ffdec.jar] [--out OriginalAssets] [--only penguin_animations,ammo] [--jobs 3]

Needs Java 11+ and JPEXS FFDec (tested with 24.1.1, https://github.com/jpexs/jpexs-decompiler/releases).
Pillow is used for PNG post-processing.

For every SWF in SWF_TABLE it
  1. reads the Symbol-Class table (AS3 linkage names) and the SWF tag tree (swf2xml),
  2. renders every linkage-named sprite, all frames, as PNG at the table's zoom (vector art is re-rasterised,
     so 3x/4x is real detail, bitmaps just get upscaled - those use zoom 1),
  3. renders frame 1 as SVG to learn the registration point (pivot) inside the PNG canvas,
  4. exports every embedded bitmap (DefineBits*) at native size, named after its linkage name when it has one,
  5. de-duplicates identical consecutive/repeated frames and writes <category>/<swf>/index.json with frame lists,
     frame labels, pivots, sizes and the named child instances of frame 1 (UI layout / attachment points).
Raster PNGs that already exist in cpw-mapeditor/assets are copied as they are.
"""
import argparse, concurrent.futures as cf, csv, hashlib, io, json, os, re, shutil, subprocess, sys, tempfile
import xml.etree.ElementTree as ET
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))

# (path under the source checkout, output category, zoom, max frames per sprite or 0 = all, export sprites?)
SWF_TABLE = [
    ('cpw-server/assets/flash/characters/penguin_animations.swf', 'characters', 3, 0, True),
    ('cpw-server/assets/flash/characters/penguin_overhead_animations.swf', 'characters', 3, 0, True),
    ('cpw-server/assets/flash/weapons/weapon_animations.swf', 'weapons', 3, 0, True),
    ('cpw-server/assets/flash/weapons/ammo.swf', 'missiles', 3, 0, True),
    ('cpw-server/assets/flash/fx/particles.swf', 'fx', 2, 0, True),
    ('cpw-server/assets/flash/fx/boosters.swf', 'fx', 2, 0, True),
    ('cpw-server/assets/flash/ui/icons_weapons.swf', 'icons', 1, 0, True),
    ('cpw-server/assets/flash/ui/icons_boosters.swf', 'icons', 1, 0, True),
    ('cpw-server/assets/flash/ui/icons_drops.swf', 'icons', 4, 0, True),
    ('cpw-server/assets/flash/ui/icons_maps.swf', 'icons', 4, 0, True),
    ('cpw-server/assets/flash/ui/emotes.swf', 'emotes', 1.5, 0, True),
    ('cpw-server/assets/flash/ui/character_ui.swf', 'ui', 2, 60, True),
    ('cpw-server/assets/flash/ui/home_screen.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/hud_shared.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/ingame.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/popups.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/multiplayer.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/shops_new.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/shops.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/slot_machine.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/top_bar_popups.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/flash/ui/loading_anim.swf', 'ui', 2, 40, True),
    ('cpw-server/assets/GameLauncher.swf', 'ui', 2, 40, False),
    ('cpw-server/assets/level_graphics/level_bg_desert.swf', 'levels', 1, 0, True),
    ('cpw-server/assets/level_graphics/level_bg_forest.swf', 'levels', 1, 0, True),
    ('cpw-server/assets/level_graphics/level_bg_mountain.swf', 'levels', 1, 0, True),
    ('cpw-server/assets/level_graphics/level_bg_winter.swf', 'levels', 1, 0, True),
    ('cpw-server/assets/level_graphics/level_assets_desert.swf', 'terrain', 1, 0, False),
    ('cpw-server/assets/level_graphics/level_assets_forest.swf', 'terrain', 1, 0, False),
    ('cpw-server/assets/level_graphics/level_assets_mountain.swf', 'terrain', 1, 0, False),
    ('cpw-server/assets/level_graphics/level_assets_winter.swf', 'terrain', 1, 0, False),
    ('cpw-server/assets/level_graphics/level_assets_terrain_generic.swf', 'terrain', 1, 0, False),
    ('cpw-server/assets/level_graphics/level_lava_mountain.swf', 'liquids', 2, 0, True),
    ('cpw-server/assets/level_graphics/level_mud_desert.swf', 'liquids', 2, 0, True),
    ('cpw-server/assets/level_graphics/level_water_winter.swf', 'liquids', 2, 0, True),
    ('cpw-server/assets/level_graphics/level_obstacles_wood.swf', 'level_objects', 2, 0, True),
    ('cpw-server/assets/level_graphics/level_obstacles_stone.swf', 'level_objects', 2, 0, True),
    ('cpw-server/assets/level_graphics/level_obstacles_ice.swf', 'level_objects', 2, 0, True),
    ('cpw-server/assets/level_graphics/level_obstacles_metal.swf', 'level_objects', 2, 0, True),
]
# Not exported (documented in MANIFEST.md): 596-byte placeholder SWFs (icons_gear, icons_accessories, icons_customization,
# icons_challenges, icons_ingredients, icons_recipes, icon_mystery_box, daily_news, power_ups), byte-identical
# level_items.swf (= level_obstacles_wood.swf), home_screen_demo.swf / popups-test.swf / cpw-assets/fla/*.swf (older
# builds of the same symbols), *.swc (code libraries; GameLauncher.swc only holds the loader).

MAPEDITOR_DIRS = [  # (dir under cpw-mapeditor/assets, output dir)
    ('parallax', 'levels/mapeditor_parallax'), ('gradients', 'levels/mapeditor_gradients'),
    ('terrain', 'terrain/mapeditor'), ('water', 'liquids/mapeditor_water'), ('items', 'level_objects/mapeditor'),
    ('characters', 'characters/mapeditor'),
]


def run(cmd):
    r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if 'FAIL' in r.stdout or 'Exception' in r.stdout or r.returncode != 0:
        lines = [l for l in r.stdout.splitlines() if l.strip() and not l.lstrip().startswith('at ') and 'JAVA_TOOL' not in l
                 and not l.startswith('Exported')]
        print('   ffdec %s: %s' % (' '.join(c for c in cmd if not c.startswith('/') and len(c) < 40)[-160:], ' | '.join(lines)[:600]), file=sys.stderr)
    return r.stdout


def ffdec(jar, *args):
    return run(['java', '-Xmx3g', '-Djava.awt.headless=true', '-jar', jar, '-cli'] + list(args))


def symbol_classes(jar, swf, tmp):
    d = os.path.join(tmp, 'sc')
    ffdec(jar, '-export', 'symbolClass', d, swf)
    out = {}
    p = os.path.join(d, 'symbols.csv')
    if os.path.exists(p):
        for row in csv.reader(open(p, encoding='utf-8'), delimiter=';'):
            if len(row) >= 2 and row[0].isdigit():
                out[int(row[0])] = row[1]
    return out


def mat(e):
    """Flash MATRIX element -> (a, b, c, d, tx, ty) in pixels."""
    m = e.find('matrix') if e is not None else None
    if m is None:
        return None
    f = lambda k, dflt: float(m.get(k, dflt))
    a = f('scaleX', 1) if m.get('hasScale') == 'true' else 1.0
    d = f('scaleY', 1) if m.get('hasScale') == 'true' else 1.0
    b = f('rotateSkew0', 0) if m.get('hasRotate') == 'true' else 0.0
    c = f('rotateSkew1', 0) if m.get('hasRotate') == 'true' else 0.0
    return (a, b, c, d, f('translateX', 0) / 20.0, f('translateY', 0) / 20.0)


def mul(p, q):
    """p * q (apply q first, then p)."""
    a1, b1, c1, d1, x1, y1 = p
    a2, b2, c2, d2, x2, y2 = q
    return (a1 * a2 + c1 * b2, b1 * a2 + d1 * b2, a1 * c2 + c1 * d2, b1 * c2 + d1 * d2,
            a1 * x2 + c1 * y2 + x1, b1 * x2 + d1 * y2 + y1)


IDENT = (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)


def union(r, b):
    return list(b) if r is None else [min(r[0], b[0]), min(r[1], b[1]), max(r[2], b[2]), max(r[3], b[3])]


class Timeline:
    """Minimal display-list simulation of the SWF (no ActionScript): enough to know where named instances are."""

    def __init__(self, xml_path):
        root = ET.parse(xml_path).getroot()
        self.sprites, self.kind, self.bitmaps, self.rects = {}, {}, {}, {}
        for e in root.find('tags'):
            t = e.get('type', '')
            cid = e.get('spriteId') or e.get('shapeId') or e.get('characterID') or e.get('buttonId') or e.get('characterId')
            if cid is not None:
                self.kind[int(cid)] = t
                for tag in ('shapeBounds', 'bounds', 'textBounds', 'startBounds', 'endBounds'):
                    r = e.find(tag)
                    if r is not None:
                        b = [float(r.get(k)) / 20.0 for k in ('Xmin', 'Ymin', 'Xmax', 'Ymax')]
                        self.rects[int(cid)] = union(self.rects.get(int(cid)), b)
            if t == 'DefineSpriteTag':
                self.sprites[int(e.get('spriteId'))] = self._frames(e.find('subTags'), int(e.get('frameCount')))
        self.main = self._frames(root.find('tags'), None)
        self.cache = {}

    @staticmethod
    def _frames(tags, count):
        frames, labels, dl = [], {}, {}
        for e in tags:
            t = e.get('type', '')
            if t.startswith('PlaceObject'):
                dep = int(e.get('depth'))
                m = mat(e)
                if e.get('placeFlagHasCharacter') == 'true':
                    old = dl.get(dep)
                    keep = old is not None and e.get('placeFlagMove') == 'true'
                    dl[dep] = {'ch': int(e.get('characterId')), 'm': m or (old['m'] if keep else IDENT),
                               'name': e.get('name') or (old['name'] if keep else None), 'born': len(frames)}
                elif dep in dl:
                    d = dict(dl[dep])
                    if m: d['m'] = m
                    if e.get('name'): d['name'] = e.get('name')
                    dl[dep] = d
            elif t.startswith('RemoveObject'):
                dl.pop(int(e.get('depth')), None)
            elif t == 'FrameLabelTag':
                labels[e.get('name')] = len(frames) + 1
            elif t == 'ShowFrameTag':
                frames.append(dict(dl))
        return {'frames': frames, 'labels': labels}

    def bounds(self, cid, depth=0):
        """Bounding box (Flash px) of a character over all of its frames, like FFDec's sprite canvas."""
        if cid in self.rects:
            return self.rects[cid]
        sp = self.sprites.get(cid)
        if sp is None or depth > 10:
            return None
        self.rects[cid] = None  # recursion guard
        r = None
        for fr in sp['frames']:
            for it in fr.values():
                cb = self.bounds(it['ch'], depth + 1)
                if cb:
                    a, b, c, d, tx, ty = it['m']
                    xs = [a * x + c * y + tx for x in (cb[0], cb[2]) for y in (cb[1], cb[3])]
                    ys = [b * x + d * y + ty for x in (cb[0], cb[2]) for y in (cb[1], cb[3])]
                    r = union(r, [min(xs), min(ys), max(xs), max(ys)])
        self.rects[cid] = r
        return r

    def named(self, sid, frame=0, m=IDENT, path='', depth=0, out=None, z=None):
        """Walk sprite `sid` at 0-based `frame`; returns {path: (matrix, z-order index)} for every named instance."""
        out = {} if out is None else out
        z = z if z is not None else [0]
        sp = self.sprites.get(sid)
        if sp is None or not sp['frames'] or depth > 8:
            return out
        fr = sp['frames'][min(frame, len(sp['frames']) - 1)]
        for dep in sorted(fr):
            it = fr[dep]
            wm = mul(m, it['m'])
            p = path
            if it['name']:
                p = (path + '/' if path else '') + it['name']
                out.setdefault(p, (wm, z[0]))
            z[0] += 1
            child = self.sprites.get(it['ch'])
            if child:
                cf_ = (frame - it['born']) % max(1, len(child['frames']))
                self.named(it['ch'], cf_, wm, p, depth + 1, out, z)
        return out


def r4(m):
    return [round(v, 4) for v in m]


def pivot_from_svg(path, zoom):
    s = open(path, encoding='utf-8').read(4000)
    w = re.search(r'width="([\d.]+)px"', s)
    h = re.search(r'height="([\d.]+)px"', s)
    t = re.search(r'<g transform="matrix\(([^)]*)\)"', s)
    if not (w and h and t):
        return None
    v = [float(x) for x in t.group(1).split(',')]
    return {'svg_size': [float(w.group(1)), float(h.group(1))], 'origin_px': [round(v[4], 2), round(v[5], 2)]}


def save_png(img, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    img.save(dst, optimize=True)


def safe(n):
    return re.sub(r'[^\w.\-]+', '_', n)


def process_swf(args, entry):
    rel, cat, zoom, maxf, do_sprites = entry
    swf = os.path.join(args.src, rel)
    stem = os.path.splitext(os.path.basename(rel))[0]
    out = os.path.join(args.out, cat, stem)
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)
    tmp = tempfile.mkdtemp(prefix='cpwx_' + stem + '_', dir=args.tmp)
    syms = symbol_classes(args.ffdec, swf, tmp)
    xml_path = os.path.join(tmp, 'swf.xml')
    ffdec(args.ffdec, '-swf2xml', swf, xml_path)
    tl = Timeline(xml_path)
    index = {'swf': rel, 'zoom': zoom, 'units': 'size = full render canvas (identical for every frame of a symbol), '
             'origin_px = registration point in that canvas, offsets[k] = where trimmed file k sits in the canvas '
             '(pivot inside file k = origin_px - offsets[k], y down), sequence = file index per timeline frame; '
             'children_frame1 matrices are Flash px at zoom 1 relative to the registration point (a, b, c, d, tx, ty)',
             'symbols': {}, 'bitmaps': {}}

    sprite_ids = sorted(i for i, n in syms.items() if tl.kind.get(i) == 'DefineSpriteTag' and '$' not in n
                        and not n.endswith('MainTimeline'))
    if do_sprites and sprite_ids:
        sel = ','.join(str(i) for i in sprite_ids)
        frames_sel = ','.join('%d:1-%d' % (i, maxf) for i in sprite_ids) if maxf else None
        pd = os.path.join(tmp, 'png')
        a = ['-zoom', str(zoom), '-selectid', sel, '-format', 'sprite:png']
        if frames_sel:
            a += ['-select', frames_sel]
        ffdec(args.ffdec, *a, '-export', 'sprite', pd, swf)
        sd = os.path.join(tmp, 'svg')
        ffdec(args.ffdec, '-zoom', str(zoom), '-selectid', sel, '-select', ','.join('%d:1' % i for i in sprite_ids),
              '-format', 'sprite:svg', '-export', 'sprite', sd, swf)
        done = set(os.listdir(sd)) if os.path.isdir(sd) else set()
        for i in sprite_ids:  # FFDec aborts the whole SVG batch on one bad sprite: retry the missing ones alone
            if not any(d == 'DefineSprite_%d' % i or d.startswith('DefineSprite_%d_' % i) for d in done):
                ffdec(args.ffdec, '-zoom', str(zoom), '-selectid', str(i), '-select', '%d:1' % i,
                      '-format', 'sprite:svg', '-export', 'sprite', sd, swf)
        for i in sprite_ids:
            name = syms[i]
            src = os.path.join(pd, 'DefineSprite_%d_%s' % (i, name.replace(':', '_')))
            if not os.path.isdir(src):
                cand = [d for d in os.listdir(pd) if d.startswith('DefineSprite_%d_' % i) or d == 'DefineSprite_%d' % i] if os.path.isdir(pd) else []
                src = os.path.join(pd, cand[0]) if cand else None
            if not src:
                print('   missing render', stem, name, file=sys.stderr)
                continue
            files = sorted((f for f in os.listdir(src) if f.endswith('.png')), key=lambda f: int(f[:-4]))
            sp = tl.sprites.get(i, {'frames': [], 'labels': {}})
            seen, seq, uniq = {}, [], []
            size = None
            for f in files:
                img = Image.open(os.path.join(src, f)).convert('RGBA')
                size = img.size
                h = hashlib.md5(img.tobytes()).hexdigest()
                if h not in seen:
                    seen[h] = len(uniq)
                    uniq.append(img)
                seq.append(seen[h])
            if not uniq:
                continue
            sn = safe(name)
            # trim transparent borders; offsets[k] = top-left of file k inside the full canvas (`size`)
            offsets = []
            for k, img in enumerate(uniq):
                bb = img.getchannel('A').getbbox() or (0, 0, 1, 1)
                uniq[k] = img.crop(bb)
                offsets.append([bb[0], bb[1]])
            if len(uniq) == 1:
                save_png(uniq[0], os.path.join(out, sn + '.png'))
                flist = [sn + '.png']
            else:
                flist = []
                for k, img in enumerate(uniq):
                    fn = '%s/%03d.png' % (sn, k + 1)
                    save_png(img, os.path.join(out, fn))
                    flist.append(fn)
            sym = {'id': i, 'frames_total': len(sp['frames']) or len(files), 'frames_exported': len(files),
                   'files': flist, 'offsets': offsets, 'sequence': seq if len(uniq) > 1 else None, 'size': list(size),
                   'labels': sp['labels'] or None}
            svg = None
            sdir = os.path.join(sd, os.path.basename(src))
            if os.path.exists(os.path.join(sdir, '1.svg')):
                svg = pivot_from_svg(os.path.join(sdir, '1.svg'), zoom)
            if svg:
                sym['origin_px'] = svg['origin_px']
            else:  # FFDec's SVG export crashes on some sprites (text fields): estimate from the tag bounds
                bb = tl.bounds(i)
                if bb:
                    bw, bh = (bb[2] - bb[0]) * zoom, (bb[3] - bb[1]) * zoom
                    sym['origin_px'] = [round(-bb[0] * zoom + (size[0] - bw) / 2, 2), round(-bb[1] * zoom + (size[1] - bh) / 2, 2)]
                    sym['origin_estimated'] = True
            named = tl.named(i, 0)
            if named:
                sym['children_frame1'] = {k: r4(v[0]) for k, v in named.items()}
            index['symbols'][name] = sym

    # embedded bitmaps at native resolution
    bd = os.path.join(tmp, 'img')
    ffdec(args.ffdec, '-format', 'image:png', '-export', 'image', bd, swf)
    if os.path.isdir(bd):
        for f in sorted(os.listdir(bd)):
            m = re.match(r'(\d+)(?:_(.*))?\.(png|jpg)$', f)
            if not m:
                continue
            cid = int(m.group(1))
            name = syms.get(cid) or m.group(2)
            fn = '_bitmaps/' + (safe(name) if name else 'bitmap_%d' % cid)
            fn = fn if fn.endswith('.png') else fn + '.png'
            img = Image.open(os.path.join(bd, f)).convert('RGBA')
            if not name and img.size[0] * img.size[1] < 64:
                continue  # 1-pixel fills
            save_png(img, os.path.join(out, fn))
            index['bitmaps'][fn] = {'id': cid, 'linkage': syms.get(cid), 'size': list(img.size)}

    # sounds embedded in the SWF (most game audio is in cpw-server/assets/music and already ships in Unity)
    snd = os.path.join(tmp, 'snd')
    ffdec(args.ffdec, '-format', 'sound:mp3_wav', '-export', 'sound', snd, swf)
    if os.path.isdir(snd):
        for f in os.listdir(snd):
            os.makedirs(os.path.join(out, '_sounds'), exist_ok=True)
            shutil.copy(os.path.join(snd, f), os.path.join(out, '_sounds', f))

    json.dump(index, open(os.path.join(out, 'index.json'), 'w'), indent=1, sort_keys=True)
    if args.keep_xml:
        shutil.copy(xml_path, os.path.join(args.tmp, stem + '.xml'))
    shutil.rmtree(tmp, ignore_errors=True)
    n = sum(len(s['files']) for s in index['symbols'].values())
    print('%-34s %3d symbols %5d pngs %4d bitmaps' % (stem, len(index['symbols']), n, len(index['bitmaps'])), flush=True)
    return stem, index


def penguin_rig(args):
    """Per-frame transforms of the attachment slots in every penguin animation (Head_Gear, Body_Gear, feet, tool,
    Facial_Expression, accessories) - what PaperDoll.addClothes used to parent clothes/weapons to."""
    swf = os.path.join(args.src, 'cpw-server/assets/flash/characters/penguin_animations.swf')
    tmp = tempfile.mkdtemp(prefix='cpwx_rig_', dir=args.tmp)
    syms = symbol_classes(args.ffdec, swf, tmp)
    xml_path = os.path.join(tmp, 'swf.xml')
    ffdec(args.ffdec, '-swf2xml', swf, xml_path)
    tl = Timeline(xml_path)
    rig = {'note': 'slots[name][frame] = [a, b, c, d, tx, ty, z] in Flash px (zoom 1, y down) relative to the animation '
           'registration point (penguin body centre; feet at y ~ +28, head gear at y ~ -25); z = draw order index (higher = in front); null = slot absent. '
           'Slot names are case-insensitive in the original (DCUtils.getChildByPath).', 'animations': {}}
    for i, name in sorted(syms.items(), key=lambda x: x[1]):
        sp = tl.sprites.get(i)
        if not sp:
            continue
        nf = len(sp['frames'])
        slots = {}
        for f in range(nf):
            for path, (m, z) in tl.named(i, f).items():
                key = path.split('/')[-1].lower()
                slots.setdefault(key, [None] * nf)
                if slots[key][f] is None:
                    slots[key][f] = r4(m) + [z]
        rig['animations'][name] = {'frames': nf, 'labels': sp['labels'], 'slots': slots}
    os.makedirs(os.path.join(args.out, 'characters'), exist_ok=True)
    json.dump(rig, open(os.path.join(args.out, 'characters', 'penguin_rig.json'), 'w'), separators=(',', ':'))
    shutil.rmtree(tmp, ignore_errors=True)
    print('penguin_rig.json: %d animations' % len(rig['animations']))


def export_fonts(args):
    """Embedded fonts (TTF) of the UI SWFs - the original bold outlined UI font."""
    n = 0
    for rel, cat, _, _, _ in SWF_TABLE:
        if cat not in ('ui', 'icons', 'emotes'):
            continue
        stem = os.path.splitext(os.path.basename(rel))[0]
        tmp = tempfile.mkdtemp(prefix='cpwx_font_', dir=args.tmp)
        ffdec(args.ffdec, '-format', 'font:ttf', '-export', 'font', tmp, os.path.join(args.src, rel))
        for root, _, fs in os.walk(tmp):
            for f in fs:
                if f.endswith('.ttf'):
                    os.makedirs(os.path.join(args.out, 'fonts'), exist_ok=True)
                    dst = os.path.join(args.out, 'fonts', stem + '__' + safe(f))
                    shutil.copy(os.path.join(root, f), dst)
                    n += 1
        shutil.rmtree(tmp, ignore_errors=True)
    print('fonts exported: %d' % n)


def copy_mapeditor(args):
    base = os.path.join(args.src, 'cpw-mapeditor/assets')
    n = 0
    for sub, dst in MAPEDITOR_DIRS:
        s = os.path.join(base, sub)
        d = os.path.join(args.out, dst)
        if os.path.isdir(d):
            shutil.rmtree(d)
        if os.path.isdir(s):
            shutil.copytree(s, d)
            n += sum(len(f) for _, _, f in os.walk(d))
    print('mapeditor rasters copied: %d files' % n)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default='/home/claude/crazy-penguin-wars')
    ap.add_argument('--ffdec', default=os.environ.get('FFDEC_JAR', os.path.join(HERE, 'ffdec', 'ffdec.jar')))
    ap.add_argument('--out', default=os.path.join(REPO, 'OriginalAssets'))
    ap.add_argument('--tmp', default=tempfile.gettempdir())
    ap.add_argument('--only', default='')
    ap.add_argument('--jobs', type=int, default=3)
    ap.add_argument('--keep-xml', action='store_true')
    ap.add_argument('--fonts', action='store_true', help='also export the embedded fonts (Berlin Sans FB Demi Bold, '
                    'Arial subsets: commercial fonts, not committed)')
    ap.add_argument('--skip', default='', help='comma list of steps to skip: swf,rig,mapeditor')
    args = ap.parse_args()
    if not os.path.exists(args.ffdec):
        sys.exit('FFDec jar not found: %s (pass --ffdec or set FFDEC_JAR)' % args.ffdec)
    os.makedirs(args.out, exist_ok=True)
    skip = set(filter(None, args.skip.split(',')))
    only = set(filter(None, args.only.split(',')))
    table = [e for e in SWF_TABLE if not only or os.path.splitext(os.path.basename(e[0]))[0] in only]
    if 'swf' not in skip:
        with cf.ThreadPoolExecutor(args.jobs) as ex:
            for fut in [ex.submit(process_swf, args, e) for e in table]:
                fut.result()
    if 'rig' not in skip and (not only or 'penguin_animations' in only):
        penguin_rig(args)
    if args.fonts:
        export_fonts(args)
    if 'mapeditor' not in skip and not only:
        copy_mapeditor(args)


if __name__ == '__main__':
    main()
