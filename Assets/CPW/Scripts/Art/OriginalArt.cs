using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The original 2012 Flash art, imported by Tools/import_original.py into Resources/Original/<category>/<swf>/
    /// (see Docs/ORIGINAL_ART.md). Paths are relative to Resources/Original and mirror OriginalAssets/ID_MAP.md
    /// without the extension: "weapons/weapon_animations/bazooka" (a symbol), "missiles/ammo/ammo_bazooka" (one-frame
    /// symbol), "level_objects/level_obstacles_wood/_bitmaps/cube_large_1" (an embedded bitmap file).
    /// Game-id lookups follow the config (Item.Graphics -> #WeaponGraphic.X -> SWF + Export -> folder + symbol) and
    /// return null when the art is missing, so callers keep their 3D/Blender fallback.
    /// Everything is cached (misses too); nothing allocates after the first lookup of a path.
    /// Note: inside TerrainStyle (which has a method named OriginalArt) write CPW.OriginalArt.
    /// </summary>
    public static class OriginalArt
    {
        public const string Root = "Original/";
        public const string PenguinFolder = "characters/penguin_animations";

        // ------------------------------------------------------------------ folders (_meta.json)

        sealed class Folder
        {
            public string rel;
            public float zoom = 1f;
            public Dictionary<string, object> symbols = new Dictionary<string, object>();
            public Dictionary<string, object> files = new Dictionary<string, object>();
            public Dictionary<string, string> bitmaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static readonly Dictionary<string, Folder> folders = new Dictionary<string, Folder>();
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, SpriteAnimSet> anims = new Dictionary<string, SpriteAnimSet>();
        static readonly Dictionary<string, string> resolved = new Dictionary<string, string>();
        static Dictionary<string, string> swfFolders;   // swf stem -> folder
        static List<string> folderList;
        static Dictionary<string, object> rig;

        /// <summary>True when the import ran (Resources/Original/_catalog.json exists).</summary>
        public static bool Available { get { LoadCatalog(); return folderList.Count > 0; } }

        /// <summary>Imported folders ("weapons/weapon_animations", ...).</summary>
        public static IReadOnlyList<string> Folders { get { LoadCatalog(); return folderList; } }

        static void LoadCatalog()
        {
            if (swfFolders != null) return;
            swfFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            folderList = new List<string>();
            var ta = Resources.Load<TextAsset>(Root + "_catalog");
            var root = ta != null ? MiniJson.ParseObject(ta.text) : null;
            if (root == null) return;
            if (root.TryGetValue("swf", out var s) && s is Dictionary<string, object> sd)
                foreach (var kv in sd) if (kv.Value is string v) swfFolders[kv.Key] = v;
            if (root.TryGetValue("folders", out var f) && f is List<object> fl)
                foreach (var o in fl) if (o is string v) folderList.Add(v);
        }

        static Folder GetFolder(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            if (folders.TryGetValue(rel, out var fo)) return fo;
            fo = null;
            var ta = Resources.Load<TextAsset>(Root + rel + "/_meta");
            var m = ta != null ? MiniJson.ParseObject(ta.text) : null;
            if (m != null)
            {
                fo = new Folder { rel = rel, zoom = F(m, "zoom", 1f) };
                if (m.TryGetValue("symbols", out var s) && s is Dictionary<string, object> sd) fo.symbols = sd;
                if (m.TryGetValue("files", out var fi) && fi is Dictionary<string, object> fd) fo.files = fd;
                if (m.TryGetValue("bitmaps", out var b) && b is Dictionary<string, object> bd)
                    foreach (var kv in bd) if (kv.Value is string v) fo.bitmaps[kv.Key] = v;
            }
            folders[rel] = fo;
            return fo;
        }

        /// <summary>Split "cat/swf/rest..." into the folder "cat/swf" and "rest...".</summary>
        static bool Split(string path, out string folder, out string rest)
        {
            folder = rest = null;
            if (string.IsNullOrEmpty(path)) return false;
            int a = path.IndexOf('/');
            int b = a < 0 ? -1 : path.IndexOf('/', a + 1);
            if (b < 0) return false;
            folder = path.Substring(0, b);
            rest = path.Substring(b + 1);
            return rest.Length > 0;
        }

        // ------------------------------------------------------------------ generic loading

        /// <summary>
        /// A sprite by path: a PNG ("…/bazooka/012", "…/_bitmaps/parallax_1_1"), a symbol (its first frame),
        /// or a bitmap by linkage name ("levels/level_bg_forest/parallax_1_1"). Null when missing.
        /// </summary>
        public static Sprite Sprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (sprites.TryGetValue(path, out var s)) return s;
            s = null;
            if (Split(path, out var folder, out var rest))
            {
                var fo = GetFolder(folder);
                if (fo != null)
                {
                    if (fo.files.ContainsKey(rest)) s = LoadFile(fo, rest);
                    else if (fo.bitmaps.TryGetValue(rest, out var file)) s = LoadFile(fo, file);
                    else if (fo.symbols.ContainsKey(rest)) s = Anim(path)?.FrameAt(0);
                }
            }
            sprites[path] = s;
            return s;
        }

        /// <summary>
        /// A symbol as an animation ("characters/penguin_animations/walk"); bitmaps and single PNGs come back as
        /// one-frame sets. Null when missing.
        /// </summary>
        public static SpriteAnimSet Anim(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (anims.TryGetValue(path, out var set)) return set;
            set = null;
            if (Split(path, out var folder, out var rest))
            {
                var fo = GetFolder(folder);
                if (fo != null && fo.symbols.TryGetValue(rest, out var so) && so is Dictionary<string, object> sym)
                    set = BuildSet(fo, path, sym);
                else if (fo != null)
                {
                    string file = fo.files.ContainsKey(rest) ? rest : fo.bitmaps.TryGetValue(rest, out var bf) ? bf : null;
                    var sp = file != null ? LoadFile(fo, file) : null;
                    if (sp != null) set = SpriteAnimSet.Single(path, sp, FileZoom(fo, file));
                }
            }
            anims[path] = set;
            return set;
        }

        public static bool Exists(string path) => Anim(path) != null;

        static SpriteAnimSet BuildSet(Folder fo, string path, Dictionary<string, object> sym)
        {
            var files = sym.TryGetValue("f", out var fv) ? fv as List<object> : null;
            if (files == null || files.Count == 0) return null;
            var frames = new Sprite[files.Count];
            var pivots = new Vector2[files.Count];
            int loaded = 0;
            for (int i = 0; i < files.Count; i++)
            {
                var name = files[i] as string;
                frames[i] = LoadFile(fo, name);
                if (frames[i] != null) loaded++;
                var p = name != null && fo.files.TryGetValue(name, out var pv) ? pv as List<object> : null;
                if (p != null && p.Count >= 2) pivots[i] = new Vector2(ToF(p[0]), ToF(p[1]));
            }
            if (loaded == 0) return null;
            int[] seq = null;
            if (sym.TryGetValue("q", out var qv) && qv is List<object> ql)
            {
                seq = new int[ql.Count];
                for (int i = 0; i < ql.Count; i++) seq[i] = Mathf.Clamp((int)ToF(ql[i]), 0, frames.Length - 1);
            }
            Dictionary<string, int> labels = null;
            if (sym.TryGetValue("l", out var lv) && lv is Dictionary<string, object> ld)
            {
                labels = new Dictionary<string, int>();
                foreach (var kv in ld) labels[kv.Key] = (int)ToF(kv.Value);
            }
            Dictionary<string, float[]> children = null;
            if (sym.TryGetValue("c", out var cv) && cv is Dictionary<string, object> cd)
            {
                children = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cd)
                {
                    if (!(kv.Value is List<object> ml)) continue;
                    var m = new float[ml.Count];
                    for (int i = 0; i < m.Length; i++) m[i] = ToF(ml[i]);
                    children[kv.Key] = m;
                }
            }
            float zoom = F(sym, "z", fo.zoom);
            return new SpriteAnimSet(path, frames, seq, labels, pivots, zoom, V2(sym, "s"), V2(sym, "o"), children);
        }

        static float FileZoom(Folder fo, string file)
        {
            var p = file != null && fo.files.TryGetValue(file, out var pv) ? pv as List<object> : null;
            return p != null && p.Count >= 3 ? ToF(p[2]) : fo.zoom;
        }

        /// <summary>Load one PNG of a folder. Falls back to building the sprite from the texture (pivot from the
        /// metadata) when it was not imported as a sprite (OriginalSpriteImporter missing or not run yet).</summary>
        static Sprite LoadFile(Folder fo, string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string key = fo.rel + "/" + file;
            if (sprites.TryGetValue("#" + key, out var s)) return s;
            s = Resources.Load<Sprite>(Root + key);
            var p = fo.files.TryGetValue(file, out var pv) ? pv as List<object> : null;
            float z = p != null && p.Count >= 3 ? ToF(p[2]) : fo.zoom;
            if (s == null)
            {
                var t = Resources.Load<Texture2D>(Root + key);
                if (t != null)
                {
                    var piv = p != null && p.Count >= 2
                        ? new Vector2(ToF(p[0]) / t.width, 1f - ToF(p[1]) / t.height) : new Vector2(0.5f, 0.5f);
                    s = UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), piv, Units.PX * z, 0, SpriteMeshType.FullRect);
                    s.name = file;
                }
            }
            else if (p != null && p.Count >= 2)
            {
                // imported by hand with other settings (or a clamped pivot): rebuild with the metadata pivot / PPU
                var r = s.rect;
                var want = new Vector2(ToF(p[0]), r.height - ToF(p[1]));
                if ((s.pivot - want).sqrMagnitude > 1f || Mathf.Abs(s.pixelsPerUnit - Units.PX * z) > 0.01f)
                {
                    s = UnityEngine.Sprite.Create(s.texture, r, new Vector2(want.x / r.width, want.y / r.height), Units.PX * z, 0, SpriteMeshType.FullRect);
                    s.name = file;
                }
            }
            sprites["#" + key] = s;
            return s;
        }

        // ------------------------------------------------------------------ config ids -> paths

        /// <summary>Path of an export in a SWF (config SWF + Export columns), or null. Like ID_MAP.md: symbol, bitmap,
        /// bitmap "_1" (intact level object), then the same export in any other imported SWF.</summary>
        public static string Resolve(string swf, string export)
        {
            if (string.IsNullOrEmpty(export)) return null;
            string key = swf + "|" + export;
            if (resolved.TryGetValue(key, out var r)) return r;
            LoadCatalog();
            r = null;
            string stem = Stem(swf);
            if (stem != null && swfFolders.TryGetValue(stem, out var folder)) r = Find(folder, export);
            if (r == null)
                foreach (var f in folderList)
                    if ((r = Find(f, export)) != null) break;
            resolved[key] = r;
            return r;
        }

        static string Find(string folder, string export)
        {
            var fo = GetFolder(folder);
            if (fo == null) return null;
            export = StripPng(export);
            if (fo.symbols.ContainsKey(export)) return folder + "/" + export;
            if (fo.bitmaps.TryGetValue(export, out var file)) return folder + "/" + file;
            if (fo.bitmaps.TryGetValue(export + "_1", out file)) return folder + "/" + file;
            return null;
        }

        static string Stem(string swf)
        {
            if (string.IsNullOrEmpty(swf)) return null;
            int slash = swf.LastIndexOf('/');
            string n = slash >= 0 ? swf.Substring(slash + 1) : swf;
            int dot = n.LastIndexOf('.');
            return dot > 0 ? n.Substring(0, dot) : n;
        }

        /// <summary>Path for a config graphics reference ("#WeaponGraphic.Bazooka"), or null.</summary>
        public static string GraphicPath(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            var rec = GameData.Resolve(reference);
            return rec != null ? Resolve(rec.Str("SWF"), rec.Str("Export")) : null;
        }

        /// <summary>The reference a record's field points to, else "#{section}.{id}" (so graphic ids work as well).</summary>
        static string Ref(string recordSection, string id, string field, string graphicSection)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("#")) return id;
            var rec = recordSection != null ? GameData.Get(recordSection, id) : null;
            string r = rec != null ? rec.Str(field) : null;
            return !string.IsNullOrEmpty(r) && r.StartsWith("#" + graphicSection + ".") ? r : "#" + graphicSection + "." + id;
        }

        // ------------------------------------------------------------------ game lookups

        /// <summary>Held weapon clip for an Item id (or WeaponGraphic id): labels draw / aim / fire / out.
        /// Null for the five clips that are missing (teleport gun, scythe, shield generator, sticky bomb, teleport grenade).</summary>
        public static SpriteAnimSet WeaponAnim(string itemId) => Anim(GraphicPath(Ref("Item", itemId, "Graphics", "WeaponGraphic")));

        /// <summary>Projectile art for a Missile id (or MissileGraphic id); animated for a few (drill, artillery, orbital bomb).</summary>
        public static SpriteAnimSet MissileAnim(string missileId) => Anim(GraphicPath(Ref("Missile", missileId, "Graphics", "MissileGraphic")));

        /// <summary>First frame of MissileAnim.</summary>
        public static Sprite MissileSprite(string missileId) => MissileAnim(missileId)?.FrameAt(0);

        /// <summary>Follower art by FollowerGraphic id ("Proximity", "Broom").</summary>
        public static SpriteAnimSet FollowerAnim(string followerGraphicId) => Anim(GraphicPath(Ref(null, followerGraphicId, null, "FollowerGraphic")));

        /// <summary>AnimationGraphic id ("VoidGenerator").</summary>
        public static SpriteAnimSet AnimationAnim(string animationGraphicId) => Anim(GraphicPath(Ref(null, animationGraphicId, null, "AnimationGraphic")));

        /// <summary>Icon of an Item id (weapon, booster or emoticon item) via its Icon reference, else by id in
        /// WeaponIcon / BoosterIcon / EmoticonIcon. Null when missing (clothes, crafting, Scythe).</summary>
        public static Sprite Icon(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            var rec = GameData.Item(itemId);
            string r = rec != null ? rec.Str("Icon") : null;
            var s = string.IsNullOrEmpty(r) ? null : Sprite(GraphicPath(r));
            return s ?? Sprite(GraphicPath("#WeaponIcon." + itemId)) ?? Sprite(GraphicPath("#BoosterIcon." + itemId))
                   ?? Sprite(GraphicPath("#EmoticonIcon." + itemId));
        }

        /// <summary>Emote bubble animation (labels Hidden, Hidden_To_Visible, Visible) for an emoticon Item id
        /// ("EmoticonLaugh"), Emoticon id or EmoticonGraphic id ("Laugh").</summary>
        public static SpriteAnimSet Emote(string id) => Anim(GraphicPath(EmoteRef(id, "Graphics", "EmoticonGraphic")));

        /// <summary>Small emote icon (emote picker) for the same ids as Emote.</summary>
        public static Sprite EmoteIcon(string id) => Sprite(GraphicPath(EmoteRef(id, "Icon", "EmoticonIcon")));

        static string EmoteRef(string id, string field, string section)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var rec = GameData.Item(id) ?? GameData.Get("Emoticon", id);
            string r = rec != null ? rec.Str(field) : null;
            if (!string.IsNullOrEmpty(r)) return r;
            return "#" + section + "." + (id.StartsWith("Emoticon") && id.Length > 8 ? id.Substring(8) : id);
        }

        /// <summary>Slot machine symbol (SlotMachineGraphic id: Ammo, Bolt, Cash, Coin, Lemon, Xp).</summary>
        public static Sprite SlotIcon(string id) => Sprite(GraphicPath(Ref(null, id, null, "SlotMachineGraphic")));

        /// <summary>
        /// Level object sprite by parts: material Wood/Stone/Ice/Metal, shape Cube/Ball/Plank/Rectangle/Triangle,
        /// size Small/Medium/Large, damage stage 1 (intact) .. 3. A missing damaged stage falls back to the lower
        /// stages; null when the object has no art at all (metal planks).
        /// </summary>
        public static Sprite LevelObject(string material, string shape, string size, int damageStage = 1)
        {
            if (string.IsNullOrEmpty(material) || string.IsNullOrEmpty(shape) || string.IsNullOrEmpty(size)) return null;
            string b = "level_objects/level_obstacles_" + material.ToLowerInvariant() + "/_bitmaps/" +
                       shape.ToLowerInvariant() + "_" + size.ToLowerInvariant() + "_";
            for (int st = Mathf.Clamp(damageStage, 1, 3); st >= 1; st--)
            {
                var s = Sprite(b + st);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>Level object sprite by LevelObject id ("CubeLargeWood") via LevelObject.Graphics -> LevelObjectGraphic.</summary>
        public static Sprite LevelObjectSprite(string levelObjectId, int damageStage = 1)
        {
            var gr = GameData.Resolve(Ref("LevelObject", levelObjectId, "Graphics", "LevelObjectGraphic"));
            if (gr == null) return null;
            LoadCatalog();
            string stem = Stem(gr.Str("SWF")), export = gr.Str("Export");
            if (stem == null || export == null || !swfFolders.TryGetValue(stem, out var folder)) return null;
            for (int st = Mathf.Clamp(damageStage, 1, 3); st >= 1; st--)
            {
                var s = Sprite(folder + "/" + export + "_" + st);
                if (s != null) return s;
            }
            return null;
        }

        // ------------------------------------------------------------------ level themes

        /// <summary>Background folder of a LevelTheme ("Forest" -> "levels/level_bg_forest"), or null (OilRig).</summary>
        public static string BackgroundFolder(string theme) => ThemeFolder(theme, "BackgroundSWF");

        /// <summary>Terrain folder of a LevelTheme ("Forest" -> "terrain/level_assets_forest"), or null.</summary>
        public static string LandmassFolder(string theme) => ThemeFolder(theme, "LandmassSWF");

        static string ThemeFolder(string theme, string field)
        {
            var rec = GameData.Get("LevelTheme", theme);
            string stem = rec != null ? Stem(rec.Str(field)) : null;
            LoadCatalog();
            return stem != null && swfFolders.TryGetValue(stem, out var f) ? f : null;
        }

        /// <summary>Parallax layer bitmap of a theme by the .lvl graphics_export name ("parallax_3_1").</summary>
        public static Sprite Parallax(string theme, string export)
        {
            var f = BackgroundFolder(theme);
            return f != null && !string.IsNullOrEmpty(export) ? Sprite(f + "/" + StripPng(export)) : null;
        }

        /// <summary>Vertical sky gradient of a theme (symbol background_gradient), or null.</summary>
        public static Sprite BackgroundGradient(string theme)
        {
            var f = BackgroundFolder(theme);
            return f != null ? Sprite(f + "/background_gradient") : null;
        }

        /// <summary>Terrain bitmap of a theme: landmass_bg_tile (fill), landmass_tile (top strip),
        /// landmass_end_left/right, landmass_filler, particle_1..5. Textures are imported with wrap mode Repeat.</summary>
        public static Sprite Landmass(string theme, string name)
        {
            var f = LandmassFolder(theme);
            return f != null && !string.IsNullOrEmpty(name) ? Sprite(f + "/" + name) : null;
        }

        /// <summary>Liquid surface tile of a theme (WaterSWF + WaterExport: water, lava or mud), or null.</summary>
        public static Sprite LiquidTile(string theme)
        {
            var rec = GameData.Get("LevelTheme", theme);
            return rec != null ? Sprite(Resolve(rec.Str("WaterSWF"), rec.Str("WaterExport"))) : null;
        }

        static string StripPng(string s) => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4) : s;

        // ------------------------------------------------------------------ penguin, fx, ui

        /// <summary>Penguin body animation ("idle", "walk_small_weapon", "fire_large_weapon", "dying"...).</summary>
        public static SpriteAnimSet Penguin(string animation) => string.IsNullOrEmpty(animation) ? null : Anim(PenguinFolder + "/" + animation);

        /// <summary>characters/penguin_rig.json parsed (slots[name][frame] = [a, b, c, d, tx, ty, z]; see its "note").</summary>
        public static Dictionary<string, object> PenguinRig()
        {
            if (rig != null) return rig;
            var ta = Resources.Load<TextAsset>(Root + "characters/penguin_rig");
            rig = (ta != null ? MiniJson.ParseObject(ta.text) : null) ?? new Dictionary<string, object>();
            return rig;
        }

        /// <summary>Particle / explosion symbol from fx/particles (or fx/boosters): "explosion_cloud", "smoke"...</summary>
        public static SpriteAnimSet Fx(string name) => string.IsNullOrEmpty(name) ? null : Anim("fx/particles/" + name) ?? Anim("fx/boosters/" + name);

        /// <summary>UI symbol or bitmap: Ui("home_screen", "Button_Play"), Ui("character_ui", "crosshair").</summary>
        public static SpriteAnimSet Ui(string swf, string name) => string.IsNullOrEmpty(name) ? null : Anim("ui/" + swf + "/" + name);

        /// <summary>First frame of a UI symbol or a UI bitmap.</summary>
        public static Sprite UiSprite(string swf, string name) => string.IsNullOrEmpty(name) ? null : Sprite("ui/" + swf + "/" + name);

        static readonly Dictionary<string, Sprite> sliced = new Dictionary<string, Sprite>();

        /// <summary>
        /// A 9-sliced copy of a sprite for Image.type = Sliced: border = left, bottom, right, top in texture pixels.
        /// Built at runtime from the same texture (no import settings needed), cached per sprite + border.
        /// </summary>
        public static Sprite Sliced(Sprite s, Vector4 border)
        {
            if (s == null) return null;
            string key = s.GetInstanceID() + "|" + border;
            if (sliced.TryGetValue(key, out var r) && r != null) return r;
            var rect = s.rect;
            r = UnityEngine.Sprite.Create(s.texture, rect, new Vector2(s.pivot.x / rect.width, s.pivot.y / rect.height),
                s.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            r.name = s.name + "_sliced";
            sliced[key] = r;
            return r;
        }

        // ------------------------------------------------------------------ json helpers

        static float ToF(object o)
        {
            switch (o)
            {
                case double d: return (float)d;
                case long l: return l;
                case int i: return i;
                case float f: return f;
                default: return 0f;
            }
        }

        static float F(Dictionary<string, object> d, string key, float def) => d.TryGetValue(key, out var v) && v != null ? ToF(v) : def;

        static Vector2 V2(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v is List<object> l && l.Count >= 2 ? new Vector2(ToF(l[0]), ToF(l[1])) : Vector2.zero;
        }
    }
}
