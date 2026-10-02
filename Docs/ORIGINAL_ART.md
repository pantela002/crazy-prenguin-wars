# Original 2D art in the game (Resources/Original)

The original Flash art (extracted into `OriginalAssets/`, see `OriginalAssets/MANIFEST.md`) ships in
`Assets/CPW/Resources/Original/` and is loaded with `OriginalArt` (`Scripts/Art/OriginalArt.cs`) and played with
`SpriteAnim` / `UISpriteAnim` (`Scripts/Art/SpriteAnim.cs`). Every lookup returns **null when the art is missing**:
keep the 3D/Blender path (`ModelLibrary.Spawn`, `ArtCatalog`) as the fallback.

## Re-import
```
python3 Tools/import_original.py        # ~40 s on 4 cores; deletes and rewrites Resources/Original
python3 Tools/check_original_art.py     # every path the OriginalArt lookups can return must exist (exit 1 if not)
```
With Unity open afterwards: menu **CPW > Reimport Original Art** (the importer reads pivots from `_meta.json`; a plain
refresh does not reimport PNGs whose bytes did not change). The first import of the ~6,700 PNGs takes a few minutes.
To change what is shipped or its resolution edit `GROUPS` / `SCALE` at the top of `import_original.py`.

## Layout
Mirrors `OriginalAssets/<category>/<swf>/`, so the paths in `OriginalAssets/ID_MAP.md` work minus the extension.

| folder | content | resolution (texture px per Flash px) |
|---|---|---|
| characters/penguin_animations | 46 penguin animations + `characters/penguin_rig.json` (slot matrices) | 2.5 |
| characters/penguin_overhead_animations | `chicken_out` | 2.5 |
| weapons/weapon_animations | 53 held weapons (labels draw/aim/fire/out) | 2.5 |
| missiles/ammo | 50 projectiles | 2.5 (orbital_bomb 1.5) |
| fx/particles, fx/boosters | explosions, smoke, flames, laser, teleport, booster_start... | 1.2 (void_generator_explosion 0.8) |
| icons/icons_weapons, icons_boosters, icons_drops, icons_maps | item icons, drop_coins/cash/exp, icon_map_random | 1 (drops/maps 2) |
| emotes/emotes | 16 emote bubbles + 16 `icon_*` | 1.125 |
| ui/home_screen, ingame, character_ui, hud_shared, popups, multiplayer, shops_new, slot_machine, top_bar_popups, loading_anim, GameLauncher | symbols + every embedded bitmap (`_bitmaps/bitmap_<id>`, native size) | symbols 1.5 (full-screen layouts 1, big floaters/messages 1), bitmaps 1 |
| levels/level_bg_{desert,forest,mountain,winter} | parallax bitmaps `parallax_<layer>_<n>`, `background_gradient` | 1 |
| terrain/level_assets_{desert,forest,mountain,winter,terrain_generic} | `landmass_bg_tile`, `landmass_tile`, `landmass_end_left/right`, `landmass_filler`, `particle_1..5` | 1 |
| liquids/level_{water_winter,lava_mountain,mud_desert} | `winter_water_tile`, `mountain_lava_tile`, `desert_mud_tile` | 2 (symbol) / 1 (bitmap) |
| level_objects/level_obstacles_{wood,stone,ice,metal} | `_bitmaps/<shape>_<size>_<1..3>` (1 intact, 2-3 damaged), `object_particle_1..5` | 1 |

About 120 MB of PNG (budget in MANIFEST). Frames that are pixel-identical with the same pivot are stored once, even
across symbols (`idle01` reuses `idle`'s frames, `idle_small_weapon` reuses `idle_punch`'s). Not shipped: the
mapeditor copies, `ui/shops` (old shop), `_bitmaps` of penguin/weapons/missiles/fx/icons/emotes (they are only parts of
the rendered symbols). Use `Tools/extract_original/contact_sheet.py` on `OriginalAssets/ui/<swf>/_bitmaps` to see
which `bitmap_<id>` is which panel/button.

Per folder `_meta.json` (read at runtime and by the importer):
`files` = `{"bazooka/001": [px, py, zoom]}` (pivot in pixels from the PNG's top-left, y down; zoom), `symbols` =
`{"bazooka": {"f": [unique files], "q": [file index per timeline frame], "l": {"aim": 10} (0-based), "o": origin,
"s": canvas, "z": zoom if not the folder's, "c": {child: [a,b,c,d,tx,ty]}}}`, `bitmaps` = `{"parallax_1_1": "_bitmaps/parallax_1_1"}`.
`_catalog.json` maps SWF names to folders.

## Import settings (Editor/OriginalSpriteImporter.cs)
Sprite, Single, **pivot = the Flash registration point** (`origin_px - offset`, can lie outside the frame), **pixelsPerUnit
= 20 x zoom**, so with the transform at scale 1 every symbol is drawn at its Flash size (1 unit = 20 Flash px, `Units.PX`)
whatever resolution it was rendered at, and all frames of a symbol line up on one SpriteRenderer. No mipmaps, bilinear,
alpha is transparency, Tight mesh, max size 4096, ASTC 6x6 on Android and iOS. terrain/, liquids/ and levels/ are
FullRect with wrap mode **Repeat** (tile them via `sprite.texture` with world-space UVs).
`OriginalArt` checks each loaded sprite against the metadata and rebuilds it with `Sprite.Create` if the pivot/PPU do
not match (or the PNG was imported as a plain texture), so the runtime is right even with stale import settings.

**No Sprite Atlases (yet).** Unity 6 uses Sprite Atlas V2 (`.spriteatlasv2`), whose assets cannot be created reliably
from code with the 2021.3 API this repo compiles against, and the penguin alone would need several 4K pages. Each
frame is its own small ASTC texture; one SpriteRenderer draws one frame, so draw calls do not change. Atlases per group
(Penguin, Weapons+Missiles, Fx, Icons+Emotes, UI-Home, UI-Battle, per level theme) can be added in the editor later
without code changes (Resources.Load returns the packed sprite).

## API
```csharp
// generic (paths relative to Resources/Original, no extension)
Sprite s       = OriginalArt.Sprite("missiles/ammo/ammo_bazooka");            // PNG, symbol (frame 0) or bitmap name
SpriteAnimSet a = OriginalArt.Anim("characters/penguin_animations/walk");     // symbol; bitmaps become 1-frame sets
string p       = OriginalArt.Resolve("level_graphics/level_bg_forest.swf", "parallax_1_1");  // config SWF + Export -> path
string q       = OriginalArt.GraphicPath("#WeaponGraphic.BasicNuke");        // config reference -> path

// game ids (config: Item.Graphics -> #WeaponGraphic.X -> SWF + Export); all null when missing
OriginalArt.WeaponAnim("BasicNuke");            // held weapon clip (Item id or WeaponGraphic id)
OriginalArt.MissileAnim("Grenade");  OriginalArt.MissileSprite("Grenade");   // Missile id or MissileGraphic id
OriginalArt.FollowerAnim("Proximity");  OriginalArt.AnimationAnim("VoidGenerator");
OriginalArt.Icon("BasicNuke");                  // item icon: Item.Icon -> WeaponIcon/BoosterIcon/EmoticonIcon
OriginalArt.Emote("EmoticonLaugh");  OriginalArt.EmoteIcon("Laugh");          // item, Emoticon or EmoticonGraphic id
OriginalArt.SlotIcon("Coin");
OriginalArt.LevelObject("Wood", "Cube", "Large", damageStage);   // 1..3, falls back to a lower stage
OriginalArt.LevelObjectSprite("CubeLargeWood", damageStage);     // via LevelObject.Graphics
OriginalArt.BackgroundFolder("Forest"); OriginalArt.Parallax("Forest", "parallax_3_1"); OriginalArt.BackgroundGradient("Forest");
OriginalArt.Landmass("Forest", "landmass_bg_tile");  OriginalArt.LiquidTile("Mountain");   // lava tile
OriginalArt.Penguin("fire_large_weapon");  OriginalArt.PenguinRig();          // rig: parsed penguin_rig.json
OriginalArt.Fx("explosion_cloud");  OriginalArt.Ui("home_screen", "Button_Play");  OriginalArt.UiSprite("ingame", "bitmap_12");
OriginalArt.Sliced(sprite, new Vector4(l, b, r, t));                         // runtime 9-slice copy for Image.type = Sliced
ModelLibrary.SpawnSprite(set, parent, sortingOrder);                         // SpriteAnim child, or null -> use Spawn
```
Inside `TerrainStyle` (it has a method called `OriginalArt`) write `CPW.OriginalArt`.

`SpriteAnimSet`: `Frames`, `Sequence` (frame per Flash frame), `Labels` (0-based, case-insensitive), `Length`,
`Fps` (24), `FrameAt(f)`, `Still("aim")`, `Label("fire")`, `Segment("loop", out a, out b)` (label .. before the next
label), `LabelAt(f)`, `ChildOffset("Text", out v)` (child of frame 1 in units relative to the origin; `ChildMatrix`
for the raw Flash matrix), `CanvasPx`/`OriginPx`/`Zoom`, `SizeUnits`.

`SpriteAnim` (SpriteRenderer) / `UISpriteAnim` (Image):
```csharp
var body = SpriteAnim.Create(root, OriginalArt.Penguin("idle"), "Body", sortingOrder: 10);   // loops
body.Play(OriginalArt.Penguin("walk"), "loop", loop: true);         // just the loop section of walk
body.PlayOnce(OriginalArt.Penguin("fire_large_weapon"), () => body.Play(OriginalArt.Penguin("idle_large_weapon")));
body.Renderer.flipX = facing < 0;                                   // mirrors around the registration point

var gun = SpriteAnim.Create(hand, OriginalArt.WeaponAnim(itemId), "Weapon", 11, loop: false);
gun.Play("draw", onDone: () => gun.Hold("aim"));                    // draw, then hold the aim pose
gun.Label += l => { if (l == "fire") Shoot(); };                    // labels fire as the playhead enters them
gun.PlayFrom("fire", onDone: () => gun.Hold("aim"));                // fire .. end
gun.Speed = 2f; gun.UnscaledTime = true;                            // UISpriteAnim defaults to unscaled time

var bubble = UISpriteAnim.Create(hudParent, OriginalArt.Emote("Laugh"), "Emote", loop: false);
bubble.FlashPx = UISpriteAnim.StageScale;                           // canvas units per Flash px (1080/668)
```
`UISpriteAnim.AlignPivot` (default on) sizes the RectTransform to each frame and sets its pivot to the registration
point, so `anchoredPosition` is the Flash origin; turn it off for frames that should fill a layout rect (buttons).

## Conventions for the follow-up agents
- Scale: transform scale 1 = original size. The 3D penguin is 2.6 units tall; the original penguin canvas is about
  35 x 68 Flash px (1.75 x 3.4 units) for `idle`: scale the sprite root if gameplay sizes must match the 3D model.
- Registration points: penguin = body centre (feet at y ~ +28 Flash px, i.e. 1.4 units below), weapons = the hand
  (they sit in the penguin's `tool` slot), missiles/fx = centre, level objects/bitmaps = centre.
- Y: Flash is y down; `_meta.json` pivots and child matrices are y down, `ChildOffset` already flips to Unity.
- Penguin hold suffix from `Item.AnimationType` (`small_weapon`, `large_weapon`, `small_object`, `large_object`,
  `punch`); `jump_large_weapon` does not exist (use `jump_small_weapon`). Slots per frame in `penguin_rig.json`
  (`animations.<anim>.slots.<slot>[frame] = [a,b,c,d,tx,ty,z]`, see its `note`).
- Sorting: one SpriteRenderer per layer; order by `sortingOrder` (rig z for penguin slots).

## Missing original art (lookups return null: keep the 3D/Blender fallback)
- **Clothes** (all head/body/feet/face/accessory items): their SWFs are empty placeholders in every public repo; the
  naked penguin and its slots are all that exists. Render the Blender clothes to sprites or keep the 3D items.
- Held weapons: `teleport_gun` (PointTeleport), `scythe`, `shield_generator` (ShieldWall), `sticky_bomb`,
  `teleport_grenade`, plus the remake-only GreyGoo; icons: `icon_wpn_scythe` and the remake-only items (GreyGoo,
  HeatSeeker, Innertube, ShieldWall, SpringMine).
- Missiles/items from `level_items.swf`: Mine, FlameMine, Mushroom (`item_landmine/firemine/mushroom`); OrbitalLaser
  missile graphic.
- Level objects: metal planks (all sizes), `custom_object_tree`; combinations that never existed (Ball Large,
  Rectangle Small, Triangle Large).
- Themes: OilRig (background + landmass), CustomObjects background; parallax `parallax_7_1` used by 4 mountain levels.
- `res/ui/popupShop.swf` bundle icons, crafting ingredient icons, achievement icons, daily news, challenges.
