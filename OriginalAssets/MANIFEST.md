# Original Crazy Penguin Wars art (phase 1: extraction)

Everything here was rendered or copied from the original 2012 Flash game files in the public
github.com/Crazy-Penguin-Wars repos (cloned at `/home/claude/crazy-penguin-wars`, all 7 repos, HEADs current on 2026-10-02).
Nothing in this folder ships yet: it is outside `Assets/`. The art belongs to the original game (Digital Chocolate).

- Re-run: `python3 Tools/extract_original/extract.py --ffdec <ffdec.jar>` (JPEXS FFDec 24.1.1 + Java 11+ + Pillow,
  about 25 min on 4 cores), then `python3 Tools/extract_original/build_tables.py`.
- `INVENTORY.md` (generated): every SWF, zoom, symbol list with frame counts, PNG counts, sizes.
- `ID_MAP.md` (generated): game config ids (`Assets/CPW/Resources/Data/config.json`) -> extracted file
  (290 of 305 graphics references resolve; the 15 misses are listed under "Missing").
- `Tools/extract_original/contact_sheet.py` tiles PNGs for a quick visual check.

## Layout and file format
`OriginalAssets/<category>/<swf name>/`:
- `<linkage>.png` for a one-frame symbol, or `<linkage>/NNN.png` for an animation. Linkage = the AS3 class name the
  game used (`DCResourceManager.getFromSWF(swf, export)`), so config `Export` values map 1:1.
- Identical frames are stored once. `index.json` -> `symbols.<linkage>`: `size` (full render canvas, the same for every
  frame), `origin_px` (registration point inside the canvas), `files`, `offsets[k]` (where trimmed file k sits in the
  canvas, so the pivot inside file k is `origin_px - offsets[k]`, y down), `sequence` (file index for every timeline
  frame), `labels` (frame labels, 1-based), `children_frame1` (named child instances and their Flash matrices: UI
  layout, attachment points). `origin_estimated: true` (most UI symbols: FFDec's SVG export crashes on text fields)
  means the origin was computed from the tag bounds and may be off by a few px.
- `_bitmaps/`: every embedded bitmap at its native resolution, named after its linkage name when it has one
  (`landmass_tile.png`, `parallax_3_1`, `cube_large_2`), else `bitmap_<id>.png`. Most UI and icon art is bitmaps
  inside vector frames: `_bitmaps/` is the sharpest source for those.
- Original timelines run at **24 fps** (SWF header). Flash stage 760x668 px. Unity: 1 unit = 20 Flash px (Units.PX).
- Render zoom (vector art is re-rasterised, bitmaps are only resampled): penguin, weapons, missiles 3x; fx, UI,
  liquids, character_ui 2x; emotes 1.5x; obstacles, terrain and parallax are bitmaps at native size; weapon/booster icons 1x (= native bitmap size); drops/map icons 4x. To change one, edit `SWF_TABLE` in extract.py and run with `--only <swf name>`.

## What exists (counts from INVENTORY.md)
| folder | content | maps to |
|---|---|---|
| characters/penguin_animations | 46 penguin animations, 1,531 unique frames (list below) | PlayerGraphic.Default |
| characters/penguin_rig.json | per-frame matrices + draw order of the clothing/weapon slots for all 46 animations | clothes + weapon attachment |
| characters/penguin_overhead_animations | `chicken_out` (chicken that replaces a penguin who quits) | popup_chicken_out |
| characters/mapeditor | idle.png from cpw-mapeditor | - |
| weapons/weapon_animations | 53 held weapons; animated ones have labels `draw` 1, `aim` ~11, `fire` ~12, `out` ~21 (frame `aim` is the static held pose). Small weapons include the penguin flipper holding them | WeaponGraphic.Export (55 ids) |
| missiles/ammo | 50 projectiles (`ammo_*`, `molotovshard`, `orbital_bomb`...) | MissileGraphic.Export, FollowerGraphic |
| fx/particles | 102 particle/explosion symbols: explosion_cloud(_grey), particle_explosion, smoke/cloud/plasma/poison/water/flame/lava_ball/mud_bubble/snow/confetti/foliage/stars, laser_beam/hit, teleport_spinner, void_generator_explosion, wind_fx, cat_* | particles.xml emitters, AnimationGraphic.VoidGenerator |
| fx/boosters | booster_start, shimmer | booster activation fx |
| icons/icons_weapons | 55 `icon_wpn_*` | WeaponIcon.Export |
| icons/icons_boosters | 16 booster icons | BoosterIcon.Export |
| icons/icons_drops, icons_maps | drop_coins/cash/exp, icon_map_random | battle rewards, map select |
| emotes/emotes | 16 animated emote bubbles (`laugh`...) + 16 icons (`icon_laugh`...) | EmoticonGraphic / EmoticonIcon |
| ui/home_screen | home_screen (full layout), background_main (igloo backdrop), Button_Play/Supplies(Shop)/Teams(Leaderboard)/Character(Customize)/Crafting/Friends/Gifts/Help/Inbox/News/Tournament/Custom_Game/cash, Top_Bar_Left/Right, HUD_Level, HUD_money, HUD_Friends_Bar, Options_Bar, Inbox_Animated | home screen |
| ui/ingame | ingame_hud, match_count_00..09, message_your_turn, message_time_alert, player_turn_name, popup_choose_item (weapon picker), booster/emote_ready_fx | battle HUD |
| ui/character_ui | aim_ui, crosshair, attack_power_bar, jump_power_bar, move_ui, action arrows, health bars, player_tag, turn_countdown_1..5, floaters (points/health/damage/combo), weapon_indicator, ButtonActionCircle* | battle HUD over the penguin |
| ui/hud_shared, popups, multiplayer, shops_new, shops, slot_machine, top_bar_popups, loading_anim, GameLauncher | top_bar, Button_Close, tooltip, tutorial arrows, slot transitions; popup_message/winner/looser/loading/chicken_out; multiplayer_custom/private/host + result_screen; shop_screen_new; slot_machine_popup + 6 slot icons; popup_gifts; loading spinner; launcher logo/loading art (bitmaps) | menus, results, shop, slot machine |
| levels/level_bg_{desert,forest,mountain,winter} | `_bitmaps/parallax_<layer>_<n>.png` (14-17 per theme; layer 1 = nearest, 6 = farthest), `background_gradient`. Placement per level: `.lvl` `parallax_layers` (graphics_export, x, y, gap, tile_horizontally, camera_x_pan, camera_z), already parsed by LevelData.parallaxLayers | LevelTheme.BackgroundSWF |
| levels/mapeditor_parallax, mapeditor_gradients | the same parallax layers + sky gradients as loose PNGs from cpw-mapeditor | - |
| terrain/level_assets_{theme} | `landmass_bg_tile` (ground texture), `landmass_tile` (grass/snow top strip), `landmass_end_left/right`, `landmass_filler`, `particle_1..5` (debris) | LevelTheme.LandmassSWF |
| terrain/level_assets_terrain_generic, terrain/mapeditor | generic landmass texture; mapeditor copies | - |
| liquids/level_water_winter, level_lava_mountain, level_mud_desert | `winter_water_tile`, `mountain_lava_tile`, `desert_mud_tile` (+ bitmaps); mapeditor_water has per-theme water PNGs | LevelTheme.WaterSWF/WaterExport |
| level_objects/level_obstacles_{wood,stone,ice,metal} | `_bitmaps/{ball,cube,plank,rectangle,triangle}_{small,medium,large}_{1,2,3}.png` (1 = intact, 2-3 = damaged) + `object_particle_1..5`; mapeditor copies | LevelObjectGraphic (e.g. CubeLargeWood -> cube_large_1) |

Not extracted on purpose: `level_items.swf` (byte-identical to level_obstacles_wood), `home_screen_demo.swf`,
`popups-test.swf`, `cpw-assets/fla/*.swf` (older builds of the same symbols), `*.swc` (code libraries). Embedded
fonts are Berlin Sans FB Demi Bold and Arial subsets (commercial fonts): `extract.py --fonts` exports them for
reference but they are not committed; use a free rounded bold font with a dark-blue outline instead.
Sounds: no SWF embeds sounds. All game audio is `cpw-server/assets/music/**` and already ships in
`Assets/CPW/Resources/Audio` (config `Sound` ids -> `music/...mp3` paths), so nothing new here.

## Missing from every public source
- **Clothing art.** The original loaded each clothing item from its own SWF (config `Graphics` -> swf + export, worn via
  slots). Those SWFs are not in any repo: `icons_gear`, `icons_accessories`, `icons_customization`, `icon_mystery_box`,
  `icons_challenges`, `icons_ingredients`, `icons_recipes`, `daily_news`, `power_ups` are 596-byte empty placeholders,
  and the config's clothing rows were deleted ("cleanup"). Only the naked penguin and its slots survive.
- Held-weapon clips `teleport_gun`, `scythe`, `shield_generator`, `sticky_bomb`, `teleport_grenade` and `icon_wpn_scythe`
  (their ammo/missile art exists); metal planks; `item_firemine/landmine/mushroom` (level_items.swf on the server is a
  copy of the wood obstacles); `level_custom_obstacles.swf` (custom_object_tree), `level_bg_oilrig.swf` / `level_assets_oilrig.swf` (OilRig theme),
  `res/ui/popupShop.swf` (bundle icons), crafting ingredient icons, achievement icons.
- `shops.swf` is truncated (one shape unreadable); the rest of it exports.
- FLA files in cpw-assets/fla are FFDec-decompiled XFL re-saves (2024) of the same SWFs; they add only library names
  (`Button_Play`, `Slider_Blue`, `Player_Tag_1..4`...), no new art. `Tools/extract_original/fla_unpack.py` opens them
  (their zip central directories are damaged).
- Web: no other public copy was reachable. The Fandom wiki (HTTP 402), Lost Media Wiki (403) and web.archive.org (proxy
  403) were blocked from this session; GitHub search finds only this org and the remake. Check by hand: the
  Wayback Machine for the old asset CDN, BlueMaxima's Flashpoint (needs the client), the Fandom wiki image galleries.

## Penguin animations (characters/penguin_animations, 24 fps)
Naming: `<action>[_<hold>]` where hold is `small_weapon`, `large_weapon`, `small_object` (thrown), `large_object`,
`punch` (Item.AnimationType); the plain name is empty-handed.
- idle (loop) + idle01/02/03 (fidgets), idle_<hold>; walk (in 1, loop 4, out 24), walk_<hold>
- jump (jump 1), jump_<hold> (jump 1, loop 11-13, out 25), fall (loop 5), landjump, landjump_<hold>
- fire_<hold> (`fire` 1, `out` 3-14), damagehit, damagehit_<hold>, damagefall (knock-back), stunned (94 frames)
- dying (ghost flies up: 790x4617 canvas, trimmed per frame), spawn, win (loop), lose_01, lose_02, wave01 (wave/emote)
- No separate aim animation: aiming = idle_<hold> + the weapon clip in the `tool` slot rotated to the aim angle
  (`Weapon.aim`: `weaponClip.rotation = angle`), weapon timeline `draw` -> `aim` -> `fire` -> `out`.
- No drown animation (the original used fall + splash particles). Emotes are bubbles (emotes/), not penguin poses.

### How the original layered clothes (cpw-client `com/dchoc/avatar/paperdoll/PaperDoll.as`, `tuxwars/items/Equippable.as`)
1. `PaperDoll.changeAnimation` instantiates the animation symbol (e.g. `walk_small_weapon`) from penguin_animations.swf.
2. Each worn item is a `BodyPart(clipName, exportClass)`; the slot names come from `Equippable.getAnimationClipName`:
   Head -> `head_gear`, Torso -> `body_gear`, Feet -> `left_foot_gear` + `right_foot_gear` (feet items have a separate
   `RightFootExport`), Face -> `facial_expression`, AccessoryOverHat -> `accessory_top`, AccessoryUnderHat ->
   `accessory_under`, Weapon -> `tool`. `addClothes` finds the child with that instance name inside the animation
   (case-insensitive path) and `addChild`s the item MovieClip into it, so the item inherits that slot's per-frame
   transform and its depth (feet behind the body, hats in front...). Every animation change re-attaches all items.
3. Children named `colorable` get the avatar color as a ColorTransform (body color customisation, slot `Color`).
4. `penguin_rig.json` has every slot's matrix and draw index for every frame, so the same attachment works with
   sprites. The rebuilt public SWF lost slots in some animations (walk/idle/jump/fall/dying only keep
   `facial_expression`, weapon variants keep `tool`); copy the nearest animation's slot track there.
   Clothing art was authored about 28x large and scaled ~0.036 by the slot matrix.

## Terrain as the original drew it (`tuxwars/battle/graphics/TerrainDisplayObject.as`, `MaterialTheme.as`)
Polygon filled with `landmass_bg_tile` (repeating bitmap fill). Along the top edge, every segment flatter than the
theme `Angle` (30-45 deg) gets `landmass_tile` strips (grass/snow), `landmass_end_left/right` caps where a flat run
starts/ends and `landmass_filler` between; a `BorderColor` outline. Explosion holes get `ExplosionBorderColor`.
Liquid: `WaterExport` tile repeated along the surface + `WaterColor` body (Winter/Forest water, Mountain lava, Desert mud).

## Integration plan (phase 2): switch the Unity game to the 2D originals
Import: a build step (`Tools/import_original.py`, new) copies the chosen PNGs into `Assets/CPW/Resources/Original/`
(or Addressables later) with Unity Sprite import settings set by an editor script (`Editor/OriginalSpriteImporter.cs`:
Sprite mode Single, pivot from `index.json` (`origin_px - offset`), PPU = 20 x zoom so 1 unit stays 20 Flash px,
no mipmaps, ASTC 6x6 on phones) and packs them into Sprite Atlases per use: Penguin (all 46 animations),
Weapons+Missiles, Fx, Icons+Emotes, UI-Home, UI-Battle, one per level theme (parallax + terrain + liquid + objects).
1. `Art/SpriteAnim.cs` (new): plays a frame list at 24 fps from a generated `SpriteAnimSet` (frames, sequence, labels,
   loop ranges `in/loop/out`), events at labels (`fire`, `out`). One `SpriteRenderer` per layer.
2. `Art/PenguinAvatar.cs`: keep the public API (Create, SetState, SetFacing, SetAim, HoldWeapon, SetClothes, Flash,
   ShowEmote, SetTeamColor, Muzzle, HeadTop, Center) so Battle/Penguin.cs, MenuScene3D, Wardrobe and BattleController
   need no changes; swap the 3D body for a sprite stack: body = SpriteAnim (AvatarState -> animation name + hold
   suffix from Item.AnimationType: Idle->idle/idle0x, Walk->walk, Jump->jump, Fall->fall, Aim->idle_<hold>,
   Fire->fire_<hold>, Hurt->damagehit/damagefall, Dead->dying, Celebrate->win, Sad->lose_01/02, Drown->fall),
   slot children (head/body/feet/face/accessories/tool) positioned each frame from penguin_rig.json with
   sortingOrder = slot z. Facing = flipX on the root. Team color: tint a scarf/ring sprite (the original body is the
   `colorable` dark teal; a colour mask sprite would be needed for full body tint).
3. Weapons: `tool` slot gets the weapon SpriteAnim (`draw`, hold on `aim`, play `fire` on shot, `out` on holster),
   rotated by the aim angle; Muzzle = weapon canvas tip (measure once per weapon from the `aim` frame bounds).
4. Clothes: no original art exists. Keep the Blender clothes but render them to 2D sprites (Blender script, front
   orthographic camera, 3x, same style) attached to the slots, or ship the naked original penguin + 3D-rendered items.
5. `Art/ArtCatalog.cs` + `Core/ModelLibrary.cs`: add `Sprite(id)` / `Anim(id)` lookups next to the model paths
   (WeaponGraphic/MissileGraphic/LevelObjectGraphic/EmoticonGraphic Export -> Original/... path, see ID_MAP.md);
   keep the 3D fallback when a sprite is missing (OilRig, custom_object_tree, clothes).
6. `Weapons/Projectile.cs`, `Deployables.cs`, `Followers.cs`, `Boosters.cs`: replace `ModelLibrary.Spawn` meshes
   with a SpriteRenderer (+ SpriteAnim for animated ammo) rotated to velocity when `AllowRotation`.
   `Weapons/Fx.cs`: explosions = `explosion_cloud`/`particle_explosion` anims + particle sprites from fx/particles
   as ParticleSystem texture-sheet materials (one atlas, additive for flame/plasma/laser).
7. `Terrain/TerrainStyle.cs` + `TerrainChunk.cs`: use `landmass_bg_tile` as the fill texture (world-space UVs) and
   build a top-edge strip mesh from `landmass_tile`/end caps/filler on segments flatter than the theme Angle (as
   above); debris = `particle_1..5`. `DynamicObjectEntity.cs`: object sprites `<shape>_<size>_1..3` swapped by damage.
   `WaterVolume.cs`: scrolling `*_tile` surface strip + flat body color (water/lava/mud per theme).
8. `Terrain/LevelBackground.cs`: replace the procedural silhouettes with the parallax bitmaps placed exactly as
   `LevelData.parallaxLayers` says (x, y, gap, tiling, camera_x_pan/camera_z) over `background_gradient`.
9. UI: `UI/UIKit.cs` gets a `UI.Skin` that loads 9-sliced panels/buttons from ui/_bitmaps (tag the slices once in a
   small json); `Meta/HomeScreen.cs` uses `background_main` + the Button_* art (Play, Shop=Button_Supplies,
   Leaderboard=Button_Teams, Customize=Button_Character, Crafting, Friends, Gifts, Inbox, News, Help) with the
   `children_frame1` positions as layout hints scaled to the safe area; `MenuScene3D` shows the sprite penguin.
   `Battle/BattleHUD*.cs`: character_ui (aim_ui, power bars, crosshair, turn_countdown, floaters, player_tag) and
   ingame (match_count, message_your_turn, popup_choose_item frame); icons from icons/ (replace Resources/Icons).
   Text stays uGUI text (placeholder "Nudge" text in renders must be cropped or the `_bitmaps` used instead).
10. Keep from the 3D pipeline: physics, colliders and level geometry (unchanged), Blender clothes (rendered to
    sprites), fallbacks for missing art, Mats/shaders for terrain. Remove after the switch: Models/Penguin,
    Models/Weapons, Models/Missiles, Models/Props and the Blender icon renders (~22 MB).
Size budget: OriginalAssets is 222 MB on disk (~7,700 PNGs), but the app only needs a subset and atlases compress it:
penguin 46 anims at 3x ~ 4 atlases of 2048^2 (ASTC 6x6 ~ 1.8 MB each), weapons+missiles 2-3 atlases, fx 2, UI 3-4,
icons/emotes 1-2, each level theme ~4 MB -> about 60-90 MB of compressed textures in the APK/IPA, well under the
~300 MB target together with the 25 MB of audio. Drop to 2x for UI/fx (or to 2x penguin on low-end phones via
atlas variants) if needed; skip frames that are visually duplicated (`sequence` already shares them).
