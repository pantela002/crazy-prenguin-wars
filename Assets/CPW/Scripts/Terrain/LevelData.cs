using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// A level from the original game (.lvl JSON in Resources/Data/Levels), converted to world units
    /// (1 unit = Units.PX Flash pixels, y up, level spans x in [0, size.x] and y in [0, size.y]).
    /// Every field of the .lvl format is parsed; see LevelLoader/Level/Element.as of the original client.
    /// </summary>
    public class LevelData
    {
        public string id, name, theme;               // theme: Forest, Winter, Mountain, Desert
        /// <summary>
        /// Optional finer look id for remake-only levels ("style" key: Volcano, IceCave). The art code may style a level
        /// by it; anything that doesn't know it falls back to <see cref="theme"/>. Equals theme when the file has none.
        /// </summary>
        public string style;
        /// <summary>What fills the level below waterY: "Water", "Lava" (Mountain levels, burns props) or "Mud" (Desert/OilRig).</summary>
        public string liquid;
        public bool IsLava => liquid == "Lava";
        public string resourcePath;                   // e.g. "Data/Levels/forest_01_easy"
        public float widthPx, heightPx;
        public Vector2 size;                          // world units
        public float waterY;                          // world y of the water surface (physics + visuals)
        public float waterLinePx;                     // original water_line (px from the top)
        public float waterDensity, waterLinearDrag, waterAngularDrag;
        public Vector2 waterVelocity;                 // world units/s (original px/s)
        public string waterTheme;                     // "NotDefined"/"None" in all shipped maps
        public string zoomSide;                       // "width" or "height": which side the original fitted to the screen
        public float powerUpPercentage;
        public List<Vector2> spawnPoints = new List<Vector2>();
        public List<TerrainPolygon> polygons = new List<TerrainPolygon>();
        public List<LevelObjectPlacement> objects = new List<LevelObjectPlacement>();
        public List<PowerUpPlacement> powerUps = new List<PowerUpPlacement>();
        /// <summary>Raw "targets" entries (empty in every shipped map; kept for completeness).</summary>
        public List<Dictionary<string, object>> targets = new List<Dictionary<string, object>>();
        /// <summary>Raw parallax_layers entries (see parallaxLayers for the parsed form).</summary>
        public List<Dictionary<string, object>> parallax = new List<Dictionary<string, object>>();
        public List<ParallaxLayerData> parallaxLayers = new List<ParallaxLayerData>();
        public Vector2 cameraBoundsMarginPx;          // camera_bounds_width / camera_bounds_height
        public Rect cameraBounds;                     // world rect the camera may show

        /// <summary>Height in px of the original water tile surface offset (WaterGraphics.WATER_SURFACE_OFFSET).</summary>
        public const float WaterSurfaceOffsetPx = 106 * 0.7f;

        public class TerrainPolygon
        {
            public string id;
            public List<Vector2> points = new List<Vector2>();   // world units, closed loop (last != first)
            public string materialTheme;                          // Wood, Stone, Ice, Desert (MaterialTheme ids)
            public string grassTheme;                             // MaterialTheme id of the top tiles, null = none
            public bool unbreakable;
            /// <summary>True for decoration terrain without physics (drawn behind, never carved).</summary>
            public bool noFixtures;
            public bool outline = true;                           // draw the border band
            public bool dynamic;                                  // unused by shipped maps
            public int shade, tintValue, red, green, blue;        // original color offsets (0..255)
            public float textureRotation;
            public string textureExport;
            /// <summary>Multiplicative color approximating the original ColorTransform offsets.</summary>
            public Color tint = Color.white;
            public Rect bounds;
        }

        public class LevelObjectPlacement
        {
            public string id, name, fixture, theme;   // e.g. name "CubeMedium", theme "Wood" → LevelObject "CubeMediumWood"
            public Vector2 position;
            public float angleDeg;                     // world (counter-clockwise) degrees
            public bool unbreakable;
            public bool sleep;

            /// <summary>LevelObject record id ("CubeMediumWood", or the bare name for CustomObjects).</summary>
            public string LevelObjectId => theme == "CustomObjects" ? name : name + theme;
        }

        public class PowerUpPlacement
        {
            public string id, exportName;              // exportName: AmmoCrate, PointsCrate, Treasure, HealthCrate, LandMine...
            public Vector2 position;
            public float angleDeg;
            public float appearPercentage;             // chance (0..100) that it is placed at battle start
        }

        public class ParallaxLayerData
        {
            public string id, swf;
            public List<string> exports = new List<string>();
            /// <summary>Bottom-center anchor of the first graphic in world units.</summary>
            public Vector2 position;
            public float cameraXPan, cameraYPan, cameraZ;
            public float gap;                          // world units between graphics
            public bool tileHorizontally;
            public float zoom = 1;
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Load by Level/PracticeLevel section id (e.g. "forest_easy_01") or by resource path ("Data/Levels/forest_01_easy").
        /// Never returns null: a missing file gives a small flat fallback level (and logs an error).
        /// </summary>
        public static LevelData Load(string levelIdOrPath)
        {
            ExtraLevels.Ensure();
            string path = ResolvePath(levelIdOrPath);
            var ta = string.IsNullOrEmpty(path) ? null : Resources.Load<TextAsset>(path);
            if (ta == null)
            {
                Debug.LogError("CPW: level not found: " + levelIdOrPath + " (" + path + ")");
                return Fallback(levelIdOrPath);
            }
            var root = MiniJson.ParseObject(ta.text);
            if (root == null)
            {
                Debug.LogError("CPW: level could not be parsed: " + path);
                return Fallback(levelIdOrPath);
            }
            var d = Parse(root);
            d.id = string.IsNullOrEmpty(levelIdOrPath) ? path : levelIdOrPath;
            d.resourcePath = path;
            return d;
        }

        /// <summary>Resources path of a level id (Level/PracticeLevel LevelFile) or path; null if empty.</summary>
        public static string ResolvePath(string levelIdOrPath)
        {
            if (string.IsNullOrEmpty(levelIdOrPath)) return null;
            ExtraLevels.Ensure();
            var rec = GameData.Get("Level", levelIdOrPath) ?? GameData.Get("PracticeLevel", levelIdOrPath);
            if (rec != null && rec.Has("LevelFile")) return StripExt(rec.Str("LevelFile"));
            if (levelIdOrPath.Contains("/")) return StripExt(levelIdOrPath);
            return "Data/Levels/" + StripExt(levelIdOrPath);
        }

        static string StripExt(string p)
        {
            if (p.EndsWith(".json") || p.EndsWith(".lvl")) return p.Substring(0, p.LastIndexOf('.'));
            return p;
        }

        /// <summary>Parse a .lvl JSON object.</summary>
        public static LevelData Parse(Dictionary<string, object> root)
        {
            var d = new LevelData();
            d.name = Str(root, "level_name", "level");
            d.theme = Str(root, "theme", "Forest");
            d.style = Str(root, "style", d.theme);
            d.widthPx = Num(root, "width", 1600);
            d.heightPx = Num(root, "height", 1200);
            float H = d.heightPx;
            d.size = new Vector2(Units.W(d.widthPx), Units.W(d.heightPx));
            d.zoomSide = Str(root, "zoom_side", "width");

            // Water (Level.as defaults)
            d.waterLinePx = root.ContainsKey("water_line") ? Num(root, "water_line", 0) : H * 0.75f;
            d.waterY = Units.W(H - d.waterLinePx - WaterSurfaceOffsetPx);
            d.waterDensity = Num(root, "water_density", 100);
            d.waterLinearDrag = Num(root, "water_lineardrag", 2);
            d.waterAngularDrag = Num(root, "water_angulardrag", 1);
            d.waterVelocity = new Vector2(Units.W(Num(root, "water_velocity_x", 0)), -Units.W(Num(root, "water_velocity_y", 0)));
            d.waterTheme = Str(root, "water_theme", "NotDefined");
            d.liquid = LiquidFor(Str(root, "liquid", null) ?? d.waterTheme, d.theme);
            d.powerUpPercentage = Num(root, "power_up_percentage", 0);

            foreach (var sp in Objects(root, "spawn_points"))
                d.spawnPoints.Add(Units.LevelToWorld(Num(sp, "x", 0), Num(sp, "y", 0), H));

            foreach (var e in Objects(root, "elements"))
            {
                var type = Str(e, "element_type", "");
                if (type == "TerrainBlockEntity") ParseTerrain(d, e, H);
                else if (type == "DynamicObject")
                {
                    d.objects.Add(new LevelObjectPlacement
                    {
                        id = Str(e, "id", "dynamic"),
                        name = Str(e, "name", "CubeMedium"),
                        fixture = Str(e, "fixture", "cube_medium"),
                        theme = Str(e, "theme", "Wood"),
                        position = Units.LevelToWorld(Num(e, "x", 0), Num(e, "y", 0), H),
                        angleDeg = -Num(e, "angle", 0),
                        unbreakable = Bool(e, "unbreakable", false),
                        sleep = Bool(e, "sleep", false),
                    });
                }
            }

            foreach (var p in Objects(root, "power_ups"))
            {
                d.powerUps.Add(new PowerUpPlacement
                {
                    id = Str(p, "id", "powerup"),
                    exportName = Str(p, "export_name", "AmmoCrate"),
                    position = Units.LevelToWorld(Num(p, "x", 0), Num(p, "y", 0), H),
                    angleDeg = -Num(p, "angle", 0),
                    appearPercentage = Num(p, "appear_percentage", 100),
                });
            }
            d.targets.AddRange(Objects(root, "targets"));

            foreach (var l in Objects(root, "parallax_layers"))
            {
                d.parallax.Add(l);
                var pl = new ParallaxLayerData
                {
                    id = Str(l, "id", "parallax"),
                    swf = Str(l, "graphics_swf", ""),
                    position = Units.LevelToWorld(Num(l, "x", 0), Num(l, "y", 0), H),
                    cameraXPan = Num(l, "camera_x_pan", 0.5f),
                    cameraYPan = Num(l, "camera_y_pan", 0.5f),
                    cameraZ = Num(l, "camera_z", 0.3f),
                    gap = Units.W(Num(l, "gap", 0)),
                    tileHorizontally = Bool(l, "tile_horizontally", false),
                    zoom = Num(l, "zoom", 1),
                };
                if (l.TryGetValue("graphics_export", out var ge))
                {
                    if (ge is List<object> gl) { foreach (var g in gl) if (g != null) pl.exports.Add(g.ToString()); }
                    else if (ge != null) pl.exports.Add(ge.ToString());
                }
                d.parallaxLayers.Add(pl);
            }

            // camera_bounds_* are not used by the shipped client; treat them as px margins around the level.
            d.cameraBoundsMarginPx = new Vector2(Num(root, "camera_bounds_width", 100), Num(root, "camera_bounds_height", 100));
            float mx = Units.W(d.cameraBoundsMarginPx.x), my = Units.W(d.cameraBoundsMarginPx.y);
            float yMin = Mathf.Min(0, d.waterY - 4f);
            d.cameraBounds = Rect.MinMaxRect(-mx, yMin, d.size.x + mx, d.size.y + my);

            if (d.spawnPoints.Count == 0)
            {
                d.spawnPoints.Add(new Vector2(d.size.x * 0.25f, d.size.y * 0.8f));
                d.spawnPoints.Add(new Vector2(d.size.x * 0.75f, d.size.y * 0.8f));
            }
            return d;
        }

        static void ParseTerrain(LevelData d, Dictionary<string, object> e, float H)
        {
            var poly = new TerrainPolygon
            {
                id = Str(e, "id", "terrain"),
                materialTheme = Str(e, "theme", "Stone"),
                unbreakable = Bool(e, "unbreakable", false),
                noFixtures = Bool(e, "no_fixtures", false),
                dynamic = Bool(e, "dynamic", false),
                outline = e.ContainsKey("outline") ? Bool(e, "outline", true) : !Bool(e, "disable_borders", false),
                shade = (int)Num(e, "shade", 0),
                tintValue = (int)Num(e, "tint", 0),
                red = (int)Num(e, "red", 0),
                green = (int)Num(e, "green", 0),
                blue = (int)Num(e, "blue", 0),
                textureRotation = Num(e, "texture_rotation", 0),
                textureExport = Str(e, "texture_export", ""),
            };
            var grass = Str(e, "grass_theme", "[Material]");
            poly.grassTheme = grass == "[None]" ? null : grass == "[Material]" || grass == "" ? poly.materialTheme : grass;

            // ColorTransform offsets (TerrainDisplayObject): channel += color + tint - shade. Approximated as a
            // multiplier (softened: the remake textures are darker than the original landmass art).
            int off = poly.tintValue - poly.shade;
            poly.tint = new Color(OffsetMul(poly.red + off), OffsetMul(poly.green + off), OffsetMul(poly.blue + off), 1);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in Objects(e, "points"))
            {
                var w = Units.LevelToWorld(Num(p, "x", 0), Num(p, "y", 0), H);
                int n = poly.points.Count;
                if (n > 0 && (poly.points[n - 1] - w).sqrMagnitude < 1e-6f) continue;
                poly.points.Add(w);
                minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x);
                minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y);
            }
            if (poly.points.Count > 3 && (poly.points[0] - poly.points[poly.points.Count - 1]).sqrMagnitude < 1e-6f)
                poly.points.RemoveAt(poly.points.Count - 1);
            if (poly.points.Count < 3) return;
            poly.bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            d.polygons.Add(poly);
        }

        /// <summary>Liquid of a level: an explicit Water/Lava/Mud id, else the theme's (Mountain = lava sea, Desert/OilRig = mud).</summary>
        public static string LiquidFor(string explicitLiquid, string theme)
        {
            switch (explicitLiquid)
            {
                case "Water": case "Lava": case "Mud": return explicitLiquid;
            }
            switch (theme)
            {
                case "Mountain": case "Volcano": return "Lava";
                case "Desert": case "OilRig": return "Mud";
                default: return "Water";
            }
        }

        static float OffsetMul(int offset) => Mathf.Clamp(1f + offset / (255f * 1.1f), 0.3f, 1.6f);

        static LevelData Fallback(string id)
        {
            var d = new LevelData { id = id, name = id, theme = "Forest", style = "Forest", liquid = "Water", widthPx = 1600, heightPx = 1200, size = new Vector2(80, 60), waterY = 5, zoomSide = "width" };
            d.waterDensity = 32; d.waterLinearDrag = 15; d.waterAngularDrag = 15;
            var ground = new TerrainPolygon { id = "fallback", materialTheme = "Wood", grassTheme = "Wood" };
            ground.points.AddRange(new[] { new Vector2(8, 3), new Vector2(72, 3), new Vector2(70, 22), new Vector2(55, 26), new Vector2(40, 24), new Vector2(25, 27), new Vector2(10, 22) });
            ground.bounds = Rect.MinMaxRect(8, 3, 72, 27);
            d.polygons.Add(ground);
            d.spawnPoints.Add(new Vector2(20, 30));
            d.spawnPoints.Add(new Vector2(60, 30));
            d.spawnPoints.Add(new Vector2(35, 30));
            d.spawnPoints.Add(new Vector2(48, 30));
            d.cameraBounds = new Rect(-5, 1, 90, 65);
            return d;
        }

        // ------------------------------------------------------------------ level lists

        /// <summary>All battle levels (Level section ids), in config order.</summary>
        public static List<string> AllLevelIds()
        {
            ExtraLevels.Ensure();
            var res = new List<string>();
            foreach (var r in GameData.Section("Level").Values) if (r.Has("LevelFile")) res.Add(r.Id);
            return res;
        }

        /// <summary>Level ids that can be played at a player level (MinLevel ≤ level ≤ MaxLevel), like the original "Play Now".</summary>
        public static List<string> PlayableLevels(int playerLevel)
        {
            ExtraLevels.Ensure();
            var res = new List<string>();
            foreach (var r in GameData.Section("Level").Values)
            {
                if (!r.Has("LevelFile")) continue;
                if (playerLevel >= r.Int("MinLevel", 1) && playerLevel <= r.Int("MaxLevel", 999)) res.Add(r.Id);
            }
            if (res.Count == 0) res.AddRange(UnlockedLevels(playerLevel));
            return res;
        }

        /// <summary>Level ids unlocked at a player level (MinRequired, or MinLevel, ≤ level) — for custom games / the map picker.</summary>
        public static List<string> UnlockedLevels(int playerLevel)
        {
            ExtraLevels.Ensure();
            var res = new List<string>();
            foreach (var r in GameData.Section("Level").Values)
            {
                if (!r.Has("LevelFile")) continue;
                if (playerLevel >= r.Int("MinRequired", r.Int("MinLevel", 1))) res.Add(r.Id);
            }
            return res;
        }

        /// <summary>A random playable level id for a player level (seeded for online games); null if none.</summary>
        public static string RandomPlayable(int playerLevel, int seed)
        {
            var l = PlayableLevels(playerLevel);
            if (l.Count == 0) return null;
            var rnd = new System.Random(seed);
            return l[rnd.Next(l.Count)];
        }

        /// <summary>Theme of a level id without parsing the whole file (from the id/path name, e.g. "winter_easy_01" → Winter).</summary>
        public static string ThemeOf(string levelIdOrPath)
        {
            var p = (ResolvePath(levelIdOrPath) ?? levelIdOrPath ?? "").ToLowerInvariant();
            int slash = p.LastIndexOf('/');
            if (slash >= 0) p = p.Substring(slash + 1);
            if (p.StartsWith("winter")) return "Winter";
            if (p.StartsWith("mountain")) return "Mountain";
            if (p.StartsWith("desert")) return "Desert";
            return "Forest";
        }

        /// <summary>Human readable name, e.g. "forest_easy_01" → "Forest Easy 1" (uses Loc when a string exists).</summary>
        public static string DisplayName(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return "";
            var rec = GameData.Get("Level", levelId);
            var key = rec != null ? rec.Str("Name") : null;
            if (!string.IsNullOrEmpty(key) && Loc.Has(key)) return Loc.T(key);
            var parts = levelId.Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) sb.Append(n);
                else sb.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ JSON helpers

        static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> o, string key)
        {
            if (o == null || !o.TryGetValue(key, out var v) || v == null) yield break;
            if (v is List<object> l) { foreach (var x in l) if (x is Dictionary<string, object> dx) yield return dx; }
            else if (v is Dictionary<string, object> single) yield return single;   // AS3 allowed a single object
        }

        static float Num(Dictionary<string, object> o, string key, float def)
        {
            if (o == null || !o.TryGetValue(key, out var v) || v == null) return def;
            switch (v)
            {
                case double dd: return (float)dd;
                case long l: return l;
                case int i: return i;
                case float f: return f;
                case bool b: return b ? 1 : 0;
                case string s: return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : def;
            }
            return def;
        }

        static string Str(Dictionary<string, object> o, string key, string def)
        {
            if (o == null || !o.TryGetValue(key, out var v) || v == null) return def;
            return v is double d ? d.ToString(CultureInfo.InvariantCulture) : v.ToString();
        }

        static bool Bool(Dictionary<string, object> o, string key, bool def)
        {
            if (o == null || !o.TryGetValue(key, out var v) || v == null) return def;
            switch (v)
            {
                case bool b: return b;
                case string s: return s == "true" || s == "1" || s == "True";
                case double d: return d != 0;
                case long l: return l != 0;
            }
            return def;
        }
    }
}
