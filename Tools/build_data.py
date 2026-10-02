#!/usr/bin/env python3
"""Builds the game's data files from the original Crazy Penguin Wars repositories.

Usage: python3 Tools/build_data.py <path-to-cloned-org-folder>
Reads cpw-server/assets (json config, levels, audio) and writes into Assets/CPW/Resources.
"""
import json, os, shutil, sys, glob

SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser('~/crazy-penguin-wars')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, 'Assets', 'CPW', 'Resources')
A = os.path.join(SRC, 'cpw-server', 'assets')

# Sections only used by the Flash loader / Facebook layer; dropped to keep the file small.
DROP = {'StartUpShared', 'StartUp', 'StartUpTutorial', 'MatchLoading', 'CustomGame', 'Results', 'Battle',
        'BattleShared', 'InboxScreen', 'GiftScreen', 'VIPScreen', 'Shop', 'Equipment', 'Crafting', 'ChallengeUI',
        'Resource', 'PopUps', 'NeighborsScreen', 'HelpScreen', 'MessagePopUp', 'MoneyScreen', 'SlotMachineScreen',
        'QuestionPopUp', 'DailyNewsScreen', 'FriendSelectorScreen', 'LeaderboardScreen', 'TournamentScreen',
        'UiTransition', 'UiTransitionID', 'GenericAssetsLoading', 'SplashStateLoading', 'AccountCreationStateLoading',
        'InGameTutorialStateLoading', 'HomeScreenStateLoading', 'BattleStateLoading', 'ForestThemeLoading',
        'MountainThemeLoading', 'DesertThemeLoading', 'WinterThemeLoading', 'InitialLoadingNormal',
        'GenericAssetsLoading_LowMem', 'SplashStateLoading_LowMem', 'AccountCreationStateLoading_LowMem',
        'InGameTutorialStateLoading_LowMem', 'HomeScreenStateLoading_LowMem', 'BattleStateLoading_LowMem',
        'TutorialBattleStateLoading_LowMem', 'LoadableResource', 'ResourceData', 'Dictionaries', 'EMPTYMembers',
        'ElementsMembers', 'ObjectsMembers', 'ParallaxLayersMembers', 'PolygonsMembers', 'StatsMembers', 'Comment',
        'Comments', 'Profanity', 'Suggestions', 'TID'}

def load(p):
    with open(p, encoding='utf-8') as f:
        return json.load(f)

base = load(os.path.join(A, 'json', 'tuxwars_config_base.json'))
dev = load(os.path.join(A, 'json', 'dev', 'tuxwars_config.json'))
cfg = {}
for k, v in base.items():
    if k not in DROP:
        cfg[k] = v
# The live config only kept a demo level list; the dev config has the full game (levels, achievements, packages...).
for k, v in dev.items():
    if k in DROP:
        continue
    if k not in cfg or (isinstance(v, dict) and len(v) > len(cfg.get(k) or {})):
        if k == 'Item':
            # keep every item from base and add the bundles from dev
            merged = dict(cfg['Item'])
            for ik, iv in v.items():
                merged.setdefault(ik, iv)
            cfg['Item'] = merged
        else:
            cfg[k] = v
for k in ('ItemPrice', 'BundleIcon'):
    if k in dev:
        merged = dict(cfg.get(k, {}))
        for ik, iv in dev[k].items():
            merged.setdefault(ik, iv)
        cfg[k] = merged

# Level files: point at our Resources path
levels_out = os.path.join(RES, 'Data', 'Levels')
os.makedirs(levels_out, exist_ok=True)
for f in glob.glob(os.path.join(A, 'flash', 'levels', 'og_cpw', 'final', '*.lvl')):
    shutil.copy(f, os.path.join(levels_out, os.path.basename(f)[:-4] + '.json'))
shutil.copy(os.path.join(A, 'flash', 'levels', 'forest_1.lvl'), os.path.join(levels_out, 'tutorial_forest_1.json'))
for k, lv in cfg['Level'].items():
    if k == '$DATA_TYPE':
        continue
    lv['LevelFile'] = 'Data/Levels/' + os.path.basename(lv['LevelFile'])[:-4]
for sec in ('PracticeLevel',):
    for k, lv in cfg[sec].items():
        if k != '$DATA_TYPE':
            lv['LevelFile'] = 'Data/Levels/tutorial_forest_1'

with open(os.path.join(RES, 'Data', 'config.json'), 'w', encoding='utf-8') as f:
    json.dump(cfg, f, separators=(',', ':'), ensure_ascii=False)

# Strings: TID id -> english text (newer file wins)
strings = {}
for p in (os.path.join(A, 'json', 'dev', 'tuxwars_config_en_beforecleanup.json'), os.path.join(A, 'json', 'tuxwars_config_en.json')):
    try:
        d = load(p)
    except Exception:
        continue
    for k, v in d.get('TID', {}).items():
        if k != '$DATA_TYPE' and isinstance(v, dict) and v.get('en'):
            strings[k] = v['en']
for k, v in dev.get('TID', {}).items():
    if k != '$DATA_TYPE' and isinstance(v, dict) and v.get('en'):
        strings.setdefault(k, v['en'])
with open(os.path.join(RES, 'Data', 'strings_en.json'), 'w', encoding='utf-8') as f:
    json.dump(strings, f, ensure_ascii=False, indent=0)

# Audio: keep folder layout so config paths (music/...) map to Resources/Audio/music/... without extension
audio_out = os.path.join(RES, 'Audio')
for f in glob.glob(os.path.join(A, 'music', '**', '*.mp3'), recursive=True):
    rel = os.path.relpath(f, A)
    dst = os.path.join(audio_out, rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy(f, dst)

print('config sections:', len(cfg), 'levels:', len(os.listdir(levels_out)), 'strings:', len(strings))
