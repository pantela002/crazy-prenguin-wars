using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Look of a terrain material theme (MaterialTheme section: Wood, Stone, Ice, Desert, Metal...):
    /// tileable texture (Resources/Textures/Terrain/{id}.png or a procedural one), border colors, grass/snow cap.
    /// </summary>
    public class MaterialStyle
    {
        public string id;
        public int index;                 // small stable index (submesh grouping)
        public Texture2D texture;
        public Color texMean = new Color(0.7f, 0.7f, 0.7f);   // average texture color (vertex colors divide by it to hit exact colors)
        public Color baseColor;           // representative flat color (previews, debris)
        public Color border, explosionBorder;
        public bool hasCap;
        public Color capColor;
        public float capAngle = 30;       // MaterialTheme.Angle: surfaces flatter than this get top tiles
        public float tileWorld = 6f;      // world units per texture repeat
        public string sound;              // Sound record (Wood/Stone/Ice/Metal)
        public float friction = 1;

        Material solidMat, decorMat;
        PhysicsMaterial2D physMat;

        /// <summary>Opaque terrain material (CPW/Unlit, texture * vertex color).</summary>
        public Material Solid => solidMat ? solidMat : (solidMat = new Material(Mats.UnlitShader) { mainTexture = texture, color = Color.white, name = "Terrain_" + id });

        /// <summary>Decoration terrain: drawn before opaque geometry without depth writes so 3D models never clip into it.</summary>
        public Material Decor
        {
            get
            {
                if (decorMat) return decorMat;
                decorMat = new Material(Mats.TransparentShader) { mainTexture = texture, color = Color.white, name = "TerrainDecor_" + id };
                decorMat.renderQueue = TerrainStyle.QueueDecor;
                return decorMat;
            }
        }

        public PhysicsMaterial2D Physics => physMat ? physMat : (physMat = new PhysicsMaterial2D("Terrain_" + id) { friction = friction, bounciness = 0 });
    }

    /// <summary>Theme colors, procedural textures and shared materials for the terrain, water and background.</summary>
    public static class TerrainStyle
    {
        public const int QueueSky = 1000;
        public const int QueueParallax = 1500;   // + layer index
        public const int QueueDecor = 1900;

        static readonly Dictionary<string, MaterialStyle> styles = new Dictionary<string, MaterialStyle>();
        static readonly Dictionary<string, Texture2D> capTextures = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> paintedCaps = new HashSet<string>();   // cap textures with final colors (Blender-made)
        static readonly Dictionary<string, Material> capMats = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ original art manifest

        static Dictionary<string, string[]> original;

        /// <summary>
        /// Entry of Resources/Textures/original_art.txt (Blender/scripts/original_art.py): the original pixel size of an
        /// imported piece of the Flash game's art ("Parallax/Forest/parallax_1_5", "Terrain/Wood", "Water/Lava",
        /// "Items/Wood/cube_large_1"...) plus extra fields. 20 px = 1 world unit. False when the art is not there.
        /// </summary>
        public static bool OriginalArt(string key, out Vector2 sizePx, out string[] fields)
        {
            if (original == null)
            {
                original = new Dictionary<string, string[]>();
                var ta = Resources.Load<TextAsset>("Textures/original_art");
                if (ta != null)
                {
                    foreach (var line in ta.text.Split('\n'))
                    {
                        var f = line.Trim().Split(' ');
                        if (f.Length >= 3) original[f[0]] = f;
                    }
                }
            }
            if (original.TryGetValue(key, out fields))
            {
                float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float w);
                float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float h);
                sizePx = new Vector2(w, h);
                return w > 0 && h > 0;
            }
            sizePx = Vector2.zero;
            return false;
        }

        /// <summary>Hex color field ("rrggbb" or "rrggbbaa") of a manifest entry, or def.</summary>
        public static Color OriginalColor(string[] fields, int index, Color def)
        {
            if (fields == null || index >= fields.Length) return def;
            var s = fields[index];
            if (s.Length < 6 || !int.TryParse(s.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return def;
            float a = 1;
            if (s.Length >= 8 && int.TryParse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int av)) a = av / 255f;
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, a);
        }

        /// <summary>The look of a level: its "style" (Volcano, IceCave) when set, else its theme.</summary>
        public static string Look(LevelData lvl) =>
            lvl == null ? "Forest" : !string.IsNullOrEmpty(lvl.style) ? lvl.style : string.IsNullOrEmpty(lvl.theme) ? "Forest" : lvl.theme;

        /// <summary>Original background art folder (Forest, Winter, Mountain, Desert) for a level theme or style.</summary>
        public static string BackgroundTheme(string themeOrStyle)
        {
            switch (themeOrStyle)
            {
                case "Winter": case "Ice": case "IceCave": return "Winter";
                case "Mountain": case "Stone": case "Volcano": case "Lava": return "Mountain";
                case "Desert": case "Metal": case "OilRig": case "Mud": return "Desert";
                default: return "Forest";
            }
        }

        /// <summary>Parse "0x5c2c36" / "#5c2c36" (invalid values such as "1xf4ec0a" give def).</summary>
        public static Color Hex(string s, Color def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            if (s.StartsWith("0x") || s.StartsWith("0X")) s = s.Substring(2);
            else if (s.StartsWith("#")) s = s.Substring(1);
            else return def;
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return def;
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1);
        }

        static Record ThemeRecord(string id) => GameData.Get("MaterialTheme", id) ?? GameData.Get("LevelTheme", id);

        /// <summary>Style with its texture (creates the procedural texture on first use).</summary>
        public static MaterialStyle Get(string materialTheme)
        {
            var st = Info(materialTheme);
            if (st.texture) return st;
            var tex = Resources.Load<Texture2D>("Textures/Terrain/" + st.id);
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Repeat;
                st.texture = tex;
                st.texMean = AverageColor(tex, new Color(0.7f, 0.7f, 0.7f));
                if (OriginalArt("Terrain/" + st.id, out var px, out var f))
                {
                    // the original landmass_bg_tile: 20 px per unit like the Flash game
                    st.tileWorld = px.x / 20f;
                    st.texMean = OriginalColor(f, 3, st.texMean);
                }
                else if (OriginalArt("TerrainMean/" + st.id, out _, out var fm)) st.texMean = OriginalColor(fm, 3, st.texMean);
            }
            else
            {
                Palette(st.id, out var baseC, out var dark, out var light, out _, out _, out _);
                st.texture = ProceduralTexture(st.id, baseC, dark, light);
                st.texMean = Color.Lerp(dark, light, 0.5f);
            }
            return st;
        }

        /// <summary>Colors and parameters of a material theme without creating its texture (previews, debris colors).</summary>
        public static MaterialStyle Info(string materialTheme)
        {
            if (string.IsNullOrEmpty(materialTheme)) materialTheme = "Stone";
            if (styles.TryGetValue(materialTheme, out var st)) return st;
            st = new MaterialStyle { id = materialTheme, index = styles.Count };
            Palette(materialTheme, out var baseC, out var dark, out _, out var capC, out st.hasCap, out st.sound);
            st.baseColor = baseC;
            st.capColor = capC;
            var rec = ThemeRecord(materialTheme);
            Color defBorder = Color.Lerp(dark, Color.black, 0.45f);
            st.border = Hex(rec?.Str("BorderColor"), defBorder);
            st.explosionBorder = Hex(rec?.Str("ExplosionBorderColor"), new Color(0.22f, 0.15f, 0.12f));
            st.capAngle = rec != null ? rec.Float("Angle", 30) : 30;
            var def = DynamicObjectDef.Find(materialTheme, "cube_medium");
            st.friction = def != null ? def.friction : 1;
            styles[materialTheme] = st;
            return st;
        }

        /// <summary>Base, dark, light and cap colors per material theme (from the original landmass art).</summary>
        static void Palette(string id, out Color baseC, out Color dark, out Color light, out Color cap, out bool hasCap, out string sound)
        {
            hasCap = true;
            switch (id)
            {
                case "Wood":
                case "Forest":
                    baseC = new Color(0.56f, 0.37f, 0.23f); dark = new Color(0.37f, 0.22f, 0.14f); light = new Color(0.70f, 0.50f, 0.31f);
                    cap = new Color(0.42f, 0.74f, 0.20f); sound = "Wood"; break;
                case "Ice":
                case "Winter":
                    baseC = new Color(0.63f, 0.83f, 0.93f); dark = new Color(0.40f, 0.64f, 0.82f); light = new Color(0.90f, 0.97f, 1f);
                    cap = new Color(0.96f, 0.98f, 1f); sound = "Ice"; break;
                case "Desert":
                    baseC = new Color(0.87f, 0.62f, 0.36f); dark = new Color(0.70f, 0.44f, 0.24f); light = new Color(0.96f, 0.78f, 0.50f);
                    cap = new Color(0.97f, 0.85f, 0.58f); sound = "Stone"; break;
                case "Metal":
                case "OilRig":
                    baseC = new Color(0.55f, 0.58f, 0.63f); dark = new Color(0.36f, 0.38f, 0.43f); light = new Color(0.74f, 0.77f, 0.81f);
                    cap = Color.white; hasCap = false; sound = "Metal"; break;
                default: // Stone, Mountain
                    baseC = new Color(0.55f, 0.49f, 0.46f); dark = new Color(0.36f, 0.31f, 0.30f); light = new Color(0.70f, 0.64f, 0.59f);
                    cap = new Color(0.50f, 0.62f, 0.27f); sound = "Stone"; break;
            }
        }

        static Color AverageColor(Texture2D t, Color def)
        {
            try
            {
                var px = t.GetPixels32(Mathf.Clamp(t.mipmapCount - 1, 0, 3));
                if (px == null || px.Length == 0) return def;
                double r = 0, g = 0, b = 0;
                for (int i = 0; i < px.Length; i++) { r += px[i].r; g += px[i].g; b += px[i].b; }
                double n = px.Length * 255.0;
                return new Color(Mathf.Max(0.1f, (float)(r / n)), Mathf.Max(0.1f, (float)(g / n)), Mathf.Max(0.1f, (float)(b / n)), 1);
            }
            catch { return def; }   // texture not readable
        }

        /// <summary>Vertex color that makes texture * color come out as the given flat color on average.</summary>
        public static Color Compensate(Color want, Color texMean) =>
            new Color(Mathf.Clamp01(want.r / texMean.r), Mathf.Clamp01(want.g / texMean.g), Mathf.Clamp01(want.b / texMean.b), 1);

        // ------------------------------------------------------------------ grass / snow cap

        /// <summary>
        /// Cap strip texture: Resources/Textures/Terrain/{id}_Cap.png (painted grass/moss/snow/sand with an ink edge, see
        /// Blender/scripts/textures.py) or a procedural white fringe that the vertex color tints.
        /// </summary>
        public static Texture2D CapTexture(string id)
        {
            if (capTextures.TryGetValue(id, out var t) && t) return t;
            t = Resources.Load<Texture2D>("Textures/Terrain/" + id + "_Cap");
            if (t != null) paintedCaps.Add(id);
            if (t == null)
            {
                const int W = 128, H = 32;
                t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Cap_" + id };
                var px = new Color32[W * H];
                bool snow = id == "Ice" || id == "Winter";
                bool sand = id == "Desert";
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W;
                    // periodic blade height profile
                    float h;
                    if (snow) h = 0.62f + 0.12f * Mathf.Sin(u * Mathf.PI * 2 * 3) + 0.06f * Mathf.Sin(u * Mathf.PI * 2 * 7 + 1);
                    else if (sand) h = 0.55f + 0.08f * Mathf.Sin(u * Mathf.PI * 2 * 2) + 0.05f * Mathf.Sin(u * Mathf.PI * 2 * 5 + 2);
                    else
                    {
                        float blade = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 22)) * 0.6f + Mathf.Abs(Mathf.Sin(u * Mathf.PI * 13 + 0.7f)) * 0.4f;
                        h = 0.5f + 0.42f * blade * blade;
                    }
                    for (int y = 0; y < H; y++)
                    {
                        float v = (y + 0.5f) / H;
                        float a = Mathf.Clamp01((h - v) * H * 0.5f);
                        float shade = Mathf.Lerp(0.78f, 1.05f, Mathf.Clamp01(v / Mathf.Max(0.01f, h)));
                        if (!snow && !sand && v > 0.45f) shade *= 0.95f + 0.1f * Mathf.Sin(u * 90);
                        byte s = (byte)Mathf.Clamp(shade * 255, 0, 255);
                        px[y * W + x] = new Color32(s, s, s, (byte)(a * 255));
                    }
                }
                t.SetPixels32(px);
                t.Apply(true, false);
            }
            t.wrapMode = TextureWrapMode.Repeat;
            capTextures[id] = t;
            return t;
        }

        /// <summary>Material for cap strips (alpha blended, drawn after opaque terrain).</summary>
        public static Material CapMaterial(string id, bool decor)
        {
            var key = id + (decor ? "/d" : "/s");
            if (capMats.TryGetValue(key, out var m) && m) return m;
            var tex = CapTexture(id);
            var col = Color.white;
            if (paintedCaps.Contains(id))
            {
                // painted textures carry their own colors: cancel the theme cap color the terrain mesh puts in the vertex
                // colors (only the polygon tint's darkening stays)
                var cc = Info(id).capColor;
                col = new Color(1f / Mathf.Max(0.05f, cc.r), 1f / Mathf.Max(0.05f, cc.g), 1f / Mathf.Max(0.05f, cc.b), 1f);
            }
            m = new Material(Mats.TransparentShader) { mainTexture = tex, color = col, name = "Cap_" + key };
            if (decor) m.renderQueue = QueueDecor + 1;
            capMats[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ level theme colors

        public static void SkyColors(string levelTheme, out Color top, out Color bottom)
        {
            switch (levelTheme)
            {
                case "Volcano": case "Lava": top = new Color(0.20f, 0.10f, 0.14f); bottom = new Color(0.95f, 0.38f, 0.16f); break;
                case "IceCave": top = new Color(0.10f, 0.20f, 0.34f); bottom = new Color(0.45f, 0.70f, 0.86f); break;
                case "Winter": top = new Color(0.47f, 0.66f, 0.90f); bottom = new Color(0.90f, 0.95f, 1f); break;
                case "Mountain": top = new Color(0.33f, 0.24f, 0.40f); bottom = new Color(0.98f, 0.62f, 0.36f); break;
                case "Desert": top = new Color(0.36f, 0.62f, 0.93f); bottom = new Color(1f, 0.88f, 0.62f); break;
                default: top = new Color(0.38f, 0.68f, 0.95f); bottom = new Color(0.86f, 0.95f, 0.98f); break;
            }
        }

        /// <summary>Water body color (LevelTheme.WaterColor), surface/foam color and whether it glows (lava).</summary>
        public static void WaterColors(string levelTheme, out Color body, out Color surface, out bool lava) =>
            WaterColors(levelTheme, LiquidFor(levelTheme), out body, out surface, out lava);

        /// <summary>Liquid of a level theme when the level does not say (Mountain = the original lava sea, Desert = mud).</summary>
        public static string LiquidFor(string levelTheme)
        {
            switch (levelTheme)
            {
                case "Mountain": case "Volcano": case "Lava": return "Lava";
                case "Desert": case "OilRig": case "Mud": return "Mud";
                default: return "Water";
            }
        }

        /// <summary>Colors for a level's liquid ("Water", "Lava", "Mud": LevelData.liquid).</summary>
        public static void WaterColors(string levelTheme, string liquid, out Color body, out Color surface, out bool lava)
        {
            var rec = GameData.Get("LevelTheme", levelTheme) ?? GameData.Get("MaterialTheme", levelTheme);
            lava = liquid == "Lava";
            bool mud = liquid == "Mud";
            body = lava ? new Color(0.56f, 0.07f, 0f) : mud ? new Color(0.20f, 0.15f, 0.10f) : new Color(0.07f, 0.30f, 0.59f);
            if (rec != null && LiquidFor(levelTheme) == liquid) body = Hex(rec.Str("WaterColor"), body);
            if (mud)
            {
                // original mud is 0x1a1a1a; keep it dark but brownish so it reads as mud, not a hole
                body = new Color(0.20f, 0.15f, 0.10f);
                surface = new Color(0.42f, 0.33f, 0.22f);
            }
            else if (lava) surface = new Color(1f, 0.62f, 0.12f);
            else surface = Color.Lerp(body, Color.white, 0.55f);
        }

        /// <summary>Color of far background silhouettes for a level theme.</summary>
        public static Color SilhouetteColor(string levelTheme)
        {
            switch (levelTheme)
            {
                case "Volcano": case "Lava": return new Color(0.24f, 0.13f, 0.14f);
                case "IceCave": return new Color(0.30f, 0.48f, 0.64f);
                case "Winter": return new Color(0.55f, 0.70f, 0.85f);
                case "Mountain": return new Color(0.38f, 0.24f, 0.26f);
                case "Desert": return new Color(0.80f, 0.55f, 0.36f);
                default: return new Color(0.25f, 0.50f, 0.32f);
            }
        }

        // ------------------------------------------------------------------ procedural textures

        const int TexSize = 256;

        static Texture2D ProceduralTexture(string id, Color baseC, Color dark, Color light)
        {
            var t = new Texture2D(TexSize, TexSize, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "TerrainProc_" + id, anisoLevel = 1 };
            var px = new Color32[TexSize * TexSize];
            int seed = id.GetHashCode() & 0xffff;
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize, v = y / (float)TexSize;
                float n = Fbm(u, v, 4, 4, seed);              // 0..1 large blotches
                float fine = Fbm(u, v, 32, 2, seed + 7);      // grain
                Color c;
                switch (id)
                {
                    case "Ice":
                    case "Winter":
                    {
                        float streak = Mathf.Sin((u + v * 0.6f) * Mathf.PI * 2 * 3 + n * 4) * 0.5f + 0.5f;
                        c = Color.Lerp(dark, light, Mathf.Clamp01(n * 0.8f + streak * 0.35f));
                        float crack = Voronoi(u, v, 5, seed + 3);
                        if (crack < 0.035f) c = Color.Lerp(c, light, 0.6f);
                        c *= 0.94f + fine * 0.12f;
                        break;
                    }
                    case "Desert":
                    {
                        float band = Mathf.Sin((v * 9 + n * 1.6f) * Mathf.PI * 2) * 0.5f + 0.5f;
                        c = Color.Lerp(dark, light, Mathf.Clamp01(0.25f + band * 0.45f + (n - 0.5f) * 0.5f));
                        c *= 0.92f + fine * 0.16f;
                        float peb = Voronoi(u, v, 10, seed + 9);
                        if (peb > 0.19f) c = Color.Lerp(c, dark, 0.25f);
                        break;
                    }
                    case "Metal":
                    case "OilRig":
                    {
                        float gx = (u * 4) % 1f, gy = (v * 4) % 1f;
                        c = Color.Lerp(baseC, light, n * 0.5f);
                        if (gx < 0.03f || gy < 0.03f) c = dark;
                        float rx = gx - 0.12f, ry = gy - 0.12f;
                        if (rx * rx + ry * ry < 0.0025f) c = light;
                        c *= 0.95f + fine * 0.1f;
                        break;
                    }
                    case "Wood":
                    case "Forest":
                    {
                        // earth with darker blotches, small stones and roots
                        c = Color.Lerp(dark, light, Mathf.Clamp01(n * 0.9f + 0.1f));
                        c *= 0.9f + fine * 0.2f;
                        float stone = Voronoi(u, v, 9, seed + 5);
                        float stoneCell = CellHash(u, v, 9, seed + 5);
                        if (stoneCell > 0.82f && stone > 0.12f) c = Color.Lerp(c, new Color(0.62f, 0.56f, 0.5f), 0.65f);
                        float root = Mathf.Abs(Mathf.Sin((u * 2 + Fbm(u, v, 8, 2, seed + 11) * 0.8f) * Mathf.PI * 2 * 2));
                        if (root < 0.05f) c = Color.Lerp(c, dark * 0.8f, 0.6f);
                        break;
                    }
                    default:
                    {
                        // rock: cells with dark cracks
                        float edge = Voronoi(u, v, 6, seed + 1);
                        float cell = CellHash(u, v, 6, seed + 1);
                        c = Color.Lerp(baseC, light, cell * 0.6f + (n - 0.5f) * 0.6f);
                        c *= 0.9f + fine * 0.2f;
                        if (edge < 0.045f) c = Color.Lerp(c, dark, 0.8f * (1 - edge / 0.045f));
                        break;
                    }
                }
                px[y * TexSize + x] = (Color32)c;
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        static int Wrap(int a, int p) { a %= p; return a < 0 ? a + p : a; }

        /// <summary>Periodic value noise (period = cells across the unit square).</summary>
        static float ValueNoise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(Wrap(x0, cells), Wrap(y0, cells), seed), b = Hash(Wrap(x0 + 1, cells), Wrap(y0, cells), seed);
            float c = Hash(Wrap(x0, cells), Wrap(y0 + 1, cells), seed), d = Hash(Wrap(x0 + 1, cells), Wrap(y0 + 1, cells), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int cells, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u, v, cells << o, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>Periodic Voronoi: distance difference F2-F1 (small near cell borders), in cell units.</summary>
        static float Voronoi(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float f1 = 9, f2 = 9;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                float px = gx + Hash(Wrap(gx, cells), Wrap(gy, cells), seed);
                float py = gy + Hash(Wrap(gx, cells), Wrap(gy, cells), seed + 1);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
            return f2 - f1;
        }

        /// <summary>Random value of the Voronoi cell containing (u, v).</summary>
        static float CellHash(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float best = 9; int bx = 0, by = 0;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                float px = gx + Hash(Wrap(gx, cells), Wrap(gy, cells), seed);
                float py = gy + Hash(Wrap(gx, cells), Wrap(gy, cells), seed + 1);
                float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < best) { best = d; bx = gx; by = gy; }
            }
            return Hash(Wrap(bx, cells), Wrap(by, cells), seed + 77);
        }
    }
}
