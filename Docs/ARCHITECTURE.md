# Crazy Penguin Wars (Unity remake): architecture

Unity 6, built-in render pipeline, legacy Input Manager, uGUI. There are **no scenes or prefabs to edit**:
`GameManager` boots itself (`RuntimeInitializeOnLoadMethod`) in any scene and builds everything from code.
Models, icons, textures, sounds and data load from `Assets/CPW/Resources/`.

## Original game material
The original Crazy Penguin Wars (Tuxwars) repos from github.com/Crazy-Penguin-Wars:
- `cpw-client/tuxwars/**` and `cpw-client/com/dchoc/**`: decompiled ActionScript 3 game client (battle rules, weapons, UI logic).
- `cpw-server/assets/json/*.json`: game config (items, weapons, missiles, explosions, followers, boosters, clothes stats, levels, XP table, slot machine, achievements...).
- `cpw-server/assets/flash/levels/og_cpw/final/*.lvl`: the 23 shipped maps (JSON polygons).
- `cpw-server/assets/music/**`: sounds and music.
- `cpw-battleserver/*.py`: turn/match server (turn order, respawn queue, rewards, ranking).

`Tools/build_data.py` copies the config (merged), English strings, levels and audio into `Assets/CPW/Resources`.

## Units
1 Unity unit = `Units.PX` (20) Flash pixels. Levels: x in [0, width/PX], y in [0, height/PX] (y flipped).
Config times are milliseconds unless the field says seconds. Gravity etc. via `Tuning`.

## Code map (Assets/CPW/Scripts)
| Folder | What |
|---|---|
| Core | GameManager (boot, menu camera, battle start/end), GameData/Record (config access), Loc (strings), ProfileService/PlayerProfile (save), AudioManager, Mats + shaders, ModelLibrary, MiniJson, Units, Tuning, MetaHooks |
| UI | UIKit (`UI.*` widget builders, Theme), ScreenManager/UIScreen |
| Battle | BattleController (partial), contracts (BattleConfig, PlayerSlot, BattleResult, TurnAction, BattleSnapshot, IBattleNetwork, BattleEvents), interfaces (IDamageable, IPenguin, StatBlock, BattleWorld), Penguin, turns/match rules, camera, HUD, AI |
| Terrain | LevelData (level parsing), BattleTerrain (destructible terrain, water, level objects, background), LevelPreview |
| Weapons | WeaponSystem (data-driven Item→Emitter→Missile/Explosion/Follower), boosters, status effects, Fx |
| Art | PenguinAvatar (3D penguin, clothes, procedural animation), ArtCatalog (id → model/icon paths) |
| Meta | MetaRegistry, menus: home, play setup, shop, wardrobe, crafting, slot machine, achievements, challenges, leaderboard, daily gift, settings, results, rewards |
| Online | IOnlineService/Online, Firebase REST implementation, online lobby, IBattleNetwork over Firebase |
| Editor | Project setup and build scripts |

Blender sources: `Blender/scripts/*.py` (generate everything with `bpy`) and `Blender/blend/*.blend`.

## Rules for contributors
- C# compiles outside Unity with `Tools/compile_check.sh` (Unity 2021.3 reference assemblies + uGUI).
  Avoid Unity-6-only APIs; use `rb.Vel()`, `rb.SetVel()`, `rb.SetDrag()` from `Phys` for Rigidbody2D.
- No custom physics layers/tags (they need ProjectSettings); identify things by component.
- Everything shown must fall back gracefully when a model/icon is missing (`ModelLibrary.Spawn` fallback primitive, `ModelLibrary.Icon` null).
- Mobile first: landscape, touch controls, 60 fps on mid-range phones, no per-frame allocations in hot paths.
