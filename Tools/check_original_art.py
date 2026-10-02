#!/usr/bin/env python3
"""Check Resources/Original against the lookups in Assets/CPW/Scripts/Art/OriginalArt.cs.

Mirrors every OriginalArt game-id lookup (weapons, missiles, followers, icons, emotes, slot icons, level objects,
level themes, the parallax layers of every level, penguin animations) over config.json and the level files, and
checks that every path a lookup can return exists: the symbol/bitmap is in its folder's _meta.json and every PNG it
references is on disk. Lookups that return null (missing original art) are listed, not failed.
Usage: python3 Tools/check_original_art.py   (exit 1 on a broken path)
"""
import glob, json, os, sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
RES = os.path.join(REPO, 'Assets/CPW/Resources')
ORIG = os.path.join(RES, 'Original')

catalog = json.load(open(os.path.join(ORIG, '_catalog.json')))
metas = {f: json.load(open(os.path.join(ORIG, f, '_meta.json'))) for f in catalog['folders']}
cfg = json.load(open(os.path.join(RES, 'Data/config.json')))
broken, nulls, ok = [], [], 0


def stem(swf):
    return os.path.splitext(os.path.basename(swf or ''))[0] or None


def find(folder, export):
    m = metas.get(folder)
    if not m:
        return None
    if export.lower().endswith('.png'):
        export = export[:-4]
    if export in m['symbols']:
        return folder + '/' + export
    bm = {k.lower(): v for k, v in m['bitmaps'].items()}
    for e in (export, export + '_1'):
        if e.lower() in bm:
            return folder + '/' + bm[e.lower()]
    return None


def resolve(swf, export):                      # OriginalArt.Resolve
    if not export:
        return None
    s = stem(swf)
    r = find(catalog['swf'][s], export) if s in catalog['swf'] else None
    if r is None:
        for f in catalog['folders']:
            r = find(f, export)
            if r:
                break
    return r


def get(section, id_):
    v = cfg.get(section, {}).get(id_)
    return v if isinstance(v, dict) else None


def graphic(ref):                              # OriginalArt.GraphicPath
    if not ref or not ref.startswith('#') or '.' not in ref:
        return None
    sec, id_ = ref[1:].split('.', 1)
    rec = get(sec, id_)
    return resolve(rec.get('SWF'), rec.get('Export')) if rec else None


def files_of(path):
    """PNG names (relative to the folder) a path stands for, or None when the path does not exist in the metas."""
    folder, rest = '/'.join(path.split('/')[:2]), '/'.join(path.split('/')[2:])
    m = metas.get(folder)
    if m is None:
        return None, folder
    if rest in m['files']:
        return [rest], folder
    if rest in m['bitmaps']:
        return [m['bitmaps'][rest]], folder
    if rest in m['symbols']:
        s = m['symbols'][rest]
        if any(not (0 <= q < len(s['f'])) for q in s['q']):
            return None, folder
        return s['f'], folder
    return None, folder


def check(what, path):
    global ok
    if path is None:
        nulls.append(what)
        return
    files, folder = files_of(path)
    if not files:
        broken.append('%s -> %s (not in _meta.json)' % (what, path))
        return
    for f in files:
        if f not in metas[folder]['files'] or not os.path.exists(os.path.join(ORIG, folder, f + '.png')):
            broken.append('%s -> %s: missing %s.png' % (what, path, f))
            return
    ok += 1


def rows(section):
    return {k: v for k, v in cfg.get(section, {}).items() if k != '$DATA_TYPE' and isinstance(v, dict)}


def ref_or(rec, field, section, id_):           # OriginalArt.Ref
    r = (rec or {}).get(field)
    return r if isinstance(r, str) and r.startswith('#' + section + '.') else '#%s.%s' % (section, id_)


items = rows('Item')
for iid, it in sorted(items.items()):
    t = it.get('Type')
    if t in ('Weapon', 'Booster'):
        check('WeaponAnim(%s)' % iid, graphic(ref_or(it, 'Graphics', 'WeaponGraphic', iid)))
    if t in ('Weapon', 'Booster', 'Emoticon'):
        r = it.get('Icon')
        p = graphic(r) if r else None
        for sec in ('WeaponIcon', 'BoosterIcon', 'EmoticonIcon'):
            p = p or graphic('#%s.%s' % (sec, iid))
        check('Icon(%s)' % iid, p)
for gid in rows('WeaponGraphic'):
    check('WeaponAnim(graphic %s)' % gid, graphic('#WeaponGraphic.' + gid))
for mid, m in sorted(rows('Missile').items()):
    check('MissileAnim(%s)' % mid, graphic(ref_or(m, 'Graphics', 'MissileGraphic', mid)))
for gid in rows('MissileGraphic'):
    check('MissileAnim(graphic %s)' % gid, graphic('#MissileGraphic.' + gid))
for gid in rows('FollowerGraphic'):
    check('FollowerAnim(%s)' % gid, graphic('#FollowerGraphic.' + gid))
for gid in rows('AnimationGraphic'):
    check('AnimationAnim(%s)' % gid, graphic('#AnimationGraphic.' + gid))
for eid, e in sorted(list(rows('Emoticon').items()) + [(k, v) for k, v in items.items() if v.get('Type') == 'Emoticon']):
    check('Emote(%s)' % eid, graphic(e.get('Graphics')))
    check('EmoteIcon(%s)' % eid, graphic(e.get('Icon')))
for gid in rows('EmoticonGraphic'):
    check('Emote(graphic %s)' % gid, graphic('#EmoticonGraphic.' + gid))
for gid in rows('SlotMachineGraphic'):
    check('SlotIcon(%s)' % gid, graphic('#SlotMachineGraphic.' + gid))

# level objects, by id (LevelObjectSprite) and by parts (LevelObject), all damage stages with the lower-stage fallback
for lid, lo in sorted(rows('LevelObject').items()):
    gr = graphic_rec = get('LevelObjectGraphic', ref_or(lo, 'Graphics', 'LevelObjectGraphic', lid).split('.', 1)[1])
    for st in (1, 2, 3):
        p = None
        if gr and stem(gr.get('SWF')) in catalog['swf']:
            folder = catalog['swf'][stem(gr['SWF'])]
            for s in range(st, 0, -1):
                if find(folder, '%s_%d' % (gr['Export'], s)):
                    p = folder + '/' + gr['Export'] + '_%d' % s
                    break
        check('LevelObjectSprite(%s, %d)' % (lid, st), p)
for mat in ('Wood', 'Stone', 'Ice', 'Metal'):
    for shape in ('Cube', 'Ball', 'Plank', 'Rectangle', 'Triangle'):
        for size in ('Small', 'Medium', 'Large'):
            for st in (1, 2, 3):
                base = 'level_objects/level_obstacles_%s/_bitmaps/%s_%s_' % (mat.lower(), shape.lower(), size.lower())
                p = next((base + str(s) for s in range(st, 0, -1) if files_of(base + str(s))[0]), None)
                if p or st == 1:
                    check('LevelObject(%s, %s, %s, %d)' % (mat, shape, size, st), p)

# themes
for th, t in sorted(rows('LevelTheme').items()):
    bg = catalog['swf'].get(stem(t.get('BackgroundSWF')))
    lm = catalog['swf'].get(stem(t.get('LandmassSWF')))
    check('BackgroundGradient(%s)' % th, bg and bg + '/background_gradient')
    for n in ('landmass_bg_tile', 'landmass_tile', 'landmass_end_left', 'landmass_end_right', 'landmass_filler',
              'particle_1', 'particle_2', 'particle_3', 'particle_4', 'particle_5'):
        check('Landmass(%s, %s)' % (th, n), lm and find(lm, n) and lm + '/' + n)
    check('LiquidTile(%s)' % th, resolve(t.get('WaterSWF'), t.get('WaterExport')))

# parallax layers of every level (LevelBackground: Resolve(graphics_swf, export))
lv = glob.glob(os.path.join(RES, 'Data/Levels/*.json'))
extra = os.path.join(RES, 'Data/extra_levels.json')
levels = [(os.path.basename(f), json.load(open(f))) for f in lv]
if os.path.exists(extra):
    ex = json.load(open(extra))
    levels += list(ex.items()) if isinstance(ex, dict) else [('extra%d' % i, l) for i, l in enumerate(ex)]
for name, l in levels:
    for layer in (l.get('parallax_layers') or []) if isinstance(l, dict) else []:
        exps = layer.get('graphics_export')
        for e in exps if isinstance(exps, list) else [exps]:
            check('Parallax(%s %s)' % (name, e), resolve(layer.get('graphics_swf'), e))

# penguin animations named in Docs/ORIGINAL_ART.md
for a in ['idle', 'idle01', 'idle02', 'idle03', 'walk', 'jump', 'fall', 'landjump', 'damagehit', 'damagefall', 'stunned',
          'dying', 'spawn', 'win', 'lose_01', 'lose_02', 'wave01']:
    check('Penguin(%s)' % a, 'characters/penguin_animations/' + a)
for h in ['small_weapon', 'large_weapon', 'small_object', 'large_object', 'punch']:
    for a in ['idle', 'walk', 'jump', 'landjump', 'fire', 'damagehit']:
        if (a, h) == ('jump', 'large_weapon'):
            continue                            # not in the SWF: use jump_small_weapon or jump
        check('Penguin(%s_%s)' % (a, h), 'characters/penguin_animations/%s_%s' % (a, h))
if os.path.exists(os.path.join(ORIG, 'characters/penguin_rig.json')):
    ok += 1
else:
    broken.append('PenguinRig: characters/penguin_rig.json missing')

# every symbol of every folder must be loadable too
for folder, m in metas.items():
    for s in m['symbols']:
        check('Anim(%s/%s)' % (folder, s), folder + '/' + s)

print('%d lookups ok, %d return null (missing original art), %d broken' % (ok, len(nulls), len(broken)))
if nulls:
    print('null:', ', '.join(sorted(set(nulls))))
for b in broken:
    print('BROKEN', b)
sys.exit(1 if broken else 0)
