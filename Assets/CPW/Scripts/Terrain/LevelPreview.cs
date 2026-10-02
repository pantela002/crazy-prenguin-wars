using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Small thumbnails of levels for the map picker: sky gradient, decoration and solid terrain polygons in their
    /// material colors with a darker outline and grass/snow tops, water, and level objects. Rendered on the CPU
    /// (a 256x160 thumbnail takes a few milliseconds) and cached per level and size.
    /// </summary>
    public static class LevelPreview
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        /// <summary>Render (or fetch from cache) a thumbnail of a level id or resource path.</summary>
        public static Texture2D Render(string levelIdOrPath, int width = 256, int height = 160)
        {
            width = Mathf.Clamp(width, 8, 2048);
            height = Mathf.Clamp(height, 8, 2048);
            string key = levelIdOrPath + "@" + width + "x" + height;
            if (cache.TryGetValue(key, out var tex) && tex) return tex;
            tex = Render(LevelData.Load(levelIdOrPath), width, height);
            cache[key] = tex;
            return tex;
        }

        /// <summary>Same as Render but as a UI sprite.</summary>
        public static Sprite RenderSprite(string levelIdOrPath, int width = 256, int height = 160)
        {
            string key = levelIdOrPath + "@" + width + "x" + height;
            if (spriteCache.TryGetValue(key, out var s) && s) return s;
            var t = Render(levelIdOrPath, width, height);
            s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
            spriteCache[key] = s;
            return s;
        }

        /// <summary>Render a thumbnail of already loaded level data (not cached).</summary>
        public static Texture2D Render(LevelData lvl, int width, int height)
        {
            var px = new Color32[width * height];
            var owner = new short[width * height];    // polygon index + 1 that colored the pixel (solid only)

            // view: the level rect (plus a little margin), fitted and centered
            Rect view = new Rect(-1, Mathf.Min(0, lvl.waterY - 2), lvl.size.x + 2, 0);
            view.yMax = lvl.size.y + 1;
            float scale = Mathf.Min(width / view.width, height / view.height);
            Vector2 off = new Vector2((width - view.width * scale) * 0.5f - view.xMin * scale, (height - view.height * scale) * 0.5f - view.yMin * scale);

            // sky
            TerrainStyle.SkyColors(lvl.theme, out var top, out var bottom);
            for (int y = 0; y < height; y++)
            {
                Color32 c = Color.Lerp(bottom, top, Mathf.Clamp01((y / (float)height - 0.2f) / 0.8f));
                for (int x = 0; x < width; x++) px[y * width + x] = c;
            }
            // far silhouette band for depth
            var sil = Color.Lerp(TerrainStyle.SilhouetteColor(lvl.theme), bottom, 0.55f);
            float phase = (lvl.name ?? "").Length * 1.7f;
            for (int x = 0; x < width; x++)
            {
                float wy = (lvl.waterY + (lvl.size.y - lvl.waterY) * 0.45f) + Mathf.Sin(x * 0.06f + phase) * lvl.size.y * 0.06f;
                int yTop = Mathf.Clamp(Mathf.RoundToInt(wy * scale + off.y), 0, height);
                for (int y = 0; y < yTop; y++) px[y * width + x] = Color32.Lerp(px[y * width + x], sil, 0.85f);
            }

            // decoration first (darker), then solid terrain in file order
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < lvl.polygons.Count; k++)
                {
                    var poly = lvl.polygons[k];
                    if (poly.noFixtures != (pass == 0)) continue;
                    var st = TerrainStyle.Info(poly.materialTheme);
                    Color c = st.baseColor * poly.tint;
                    if (pass == 0) c = Color.Lerp(c, bottom, 0.25f) * 0.85f;
                    c.a = 1;
                    FillPolygon(poly.points, px, pass == 1 ? owner : null, (short)(k + 1), width, height, scale, off, c);
                }
            }

            // outlines and grass/snow tops on solid terrain
            var outline = new Color32[lvl.polygons.Count + 1];
            var capCol = new Color32[lvl.polygons.Count + 1];
            var hasCap = new bool[lvl.polygons.Count + 1];
            for (int k = 0; k < lvl.polygons.Count; k++)
            {
                var poly = lvl.polygons[k];
                var st = TerrainStyle.Info(poly.materialTheme);
                outline[k + 1] = poly.outline ? (Color32)st.border : (Color32)(st.baseColor * poly.tint * 0.8f);
                if (!string.IsNullOrEmpty(poly.grassTheme))
                {
                    var cs = TerrainStyle.Info(poly.grassTheme);
                    hasCap[k + 1] = cs.hasCap;
                    capCol[k + 1] = cs.capColor;
                }
            }
            int capPx = Mathf.Max(1, Mathf.RoundToInt(0.35f * scale));
            var final = (Color32[])px.Clone();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                int o = owner[i];
                if (o == 0) continue;
                bool edge = x == 0 || x == width - 1 || y == 0 || y == height - 1 ||
                            owner[i - 1] == 0 || owner[i + 1] == 0 || owner[i - width] == 0 || owner[i + width] == 0;
                if (edge) final[i] = outline[o];
                if (hasCap[o])
                {
                    // open sky within capPx above → top surface
                    for (int d = 1; d <= capPx; d++)
                    {
                        int yy = y + d;
                        if (yy >= height || owner[yy * width + x] == 0) { final[i] = capCol[o]; break; }
                    }
                }
            }
            px = final;

            // level objects as small blocks
            foreach (var ob in lvl.objects)
            {
                var def = DynamicObjectDef.Find(ob.theme, ob.fixture);
                Vector2 half = new Vector2(0.6f, 0.6f);
                if (def != null && def.circle != null) half = Vector2.one * Units.W(def.circle[0]);
                else if (def != null && def.polys != null)
                {
                    float mx = 0, my = 0;
                    foreach (var pl in def.polys) for (int q = 0; q < pl.Length; q += 2) { mx = Mathf.Max(mx, Mathf.Abs(pl[q])); my = Mathf.Max(my, Mathf.Abs(pl[q + 1])); }
                    half = new Vector2(Units.W(mx), Units.W(my));
                    if (Mathf.Abs(Mathf.DeltaAngle(ob.angleDeg, 90)) < 30 || Mathf.Abs(Mathf.DeltaAngle(ob.angleDeg, -90)) < 30) half = new Vector2(half.y, half.x);
                }
                Color32 c = DynamicObjectEntity.ColorFor(ob.theme);
                Color32 dark = Color.Lerp(DynamicObjectEntity.ColorFor(ob.theme), Color.black, 0.45f);
                int x0 = Mathf.RoundToInt((ob.position.x - half.x) * scale + off.x), x1 = Mathf.RoundToInt((ob.position.x + half.x) * scale + off.x);
                int y0 = Mathf.RoundToInt((ob.position.y - half.y) * scale + off.y), y1 = Mathf.RoundToInt((ob.position.y + half.y) * scale + off.y);
                if (x1 <= x0) x1 = x0 + 1;
                if (y1 <= y0) y1 = y0 + 1;
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(height, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(width, x1); x++)
                    px[y * width + x] = (x == x0 || x == x1 - 1 || y == y0 || y == y1 - 1) ? dark : c;
            }

            // water
            TerrainStyle.WaterColors(lvl.theme, out var water, out var surface, out _);
            int wy0 = Mathf.Clamp(Mathf.RoundToInt(lvl.waterY * scale + off.y), 0, height);
            for (int y = 0; y < wy0; y++)
            {
                bool surf = y >= wy0 - Mathf.Max(1, Mathf.RoundToInt(scale * 0.25f));
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    px[i] = surf ? (Color32)surface : Color32.Lerp(px[i], water, 0.78f);
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelPreview_" + lvl.name };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        static readonly List<float> xs = new List<float>();

        /// <summary>Scanline polygon fill (even-odd) at pixel centers.</summary>
        static void FillPolygon(List<Vector2> pts, Color32[] px, short[] owner, short id, int w, int h, float scale, Vector2 off, Color32 c)
        {
            int n = pts.Count;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int k = 0; k < n; k++) { float y = pts[k].y * scale + off.y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY));
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f;
                xs.Clear();
                for (int k = 0; k < n; k++)
                {
                    Vector2 a = pts[k] * scale + off, b = pts[(k + 1) % n] * scale + off;
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) xs.Add(a.x + (sy - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Mathf.Max(0, Mathf.CeilToInt(xs[k] - 0.5f)), xb = Mathf.Min(w - 1, Mathf.FloorToInt(xs[k + 1] - 0.5f));
                    for (int x = xa; x <= xb; x++)
                    {
                        px[y * w + x] = c;
                        if (owner != null) owner[y * w + x] = id;
                    }
                }
            }
        }
    }
}
