using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Destructible terrain built from the level polygons, plus water, level objects and background.
    ///
    /// The terrain is a density grid (one byte per sample every <see cref="Cell"/> world units, iso value 128)
    /// rasterized from the level polygons with anti-aliased edges, plus a material index per sample.
    /// It is split into chunks of <see cref="ChunkSize"/>² squares; each chunk owns a marching-squares mesh
    /// (textured per material, darker border band along surfaces, scorched crater edges, grass/snow cap on
    /// upward surfaces) and EdgeCollider2D polylines. Carve/Fill only touch samples and mark chunks dirty;
    /// dirty chunks are rebuilt once in LateUpdate (or immediately with <see cref="FlushDirty"/>).
    ///
    /// Decoration polygons (no_fixtures) go in a second grid that is drawn behind without colliders and is never carved.
    /// </summary>
    public partial class BattleTerrain : MonoBehaviour
    {
        public static BattleTerrain I { get; private set; }
        public LevelData Level { get; private set; }
        public float WaterY => Level != null ? Level.waterY : 0;
        /// <summary>The level liquid is lava (Mountain/Volcano): burns wood/ice props; penguins die in it like in water.</summary>
        public bool IsLava => Level != null && Level.IsLava;
        /// <summary>Every change made to the terrain this battle, in order (for online snapshots).</summary>
        public readonly List<CraterState> History = new List<CraterState>();

        /// <summary>World units between density samples.</summary>
        public const float Cell = 0.2f;
        /// <summary>Squares per chunk side.</summary>
        public const int ChunkSize = 32;
        /// <summary>Spawn Fx.Debris in the material color when Carve removes terrain.</summary>
        public static bool CarveDebris = true;

        /// <summary>Raised after every Carve (center, radius, removed area in world units²) — e.g. for sounds/challenges.</summary>
        public static event System.Action<Vector2, float, float> Carved;

        public WaterVolume Water { get; private set; }
        public LevelBackground Background { get; private set; }
        public readonly List<DynamicObjectEntity> LevelObjects = new List<DynamicObjectEntity>();

        // ------------------------------------------------------------------ grid state
        internal class Grid
        {
            public int w, h;
            public byte[] density, mat, scorch, cap;
            public Grid(int w, int h, bool full)
            {
                this.w = w; this.h = h;
                density = new byte[w * h];
                mat = new byte[w * h];
                if (full) { scorch = new byte[w * h]; cap = new byte[w * h]; }
            }
        }

        /// <summary>One terrain look: material theme + polygon tint (+ unbreakable). Index 0 is "nothing".</summary>
        internal class PaletteEntry
        {
            public MaterialStyle style;
            public Color tint = Color.white;
            public bool unbreakable, outline = true;
            public MaterialStyle cap;          // null = no grass/snow
            // precomputed vertex colors
            public Color32 inner, border, scorched, capColor;
        }

        internal Vector2 origin;               // world position of sample (0, 0)
        internal Grid solid, decor;
        byte[] origDensity, origMat, origCap;
        internal readonly List<PaletteEntry> palette = new List<PaletteEntry> { null };
        Transform chunkRoot, decorRoot, objectsRoot;
        Chunk[] chunks, decorChunks;
        int chunksX, chunksY;
        readonly List<int> dirty = new List<int>();
        bool anyDirty;
        static readonly Collider2D[] wakeBuffer = new Collider2D[64];

        // ------------------------------------------------------------------ build

        /// <summary>Build the whole level (terrain, water, objects, background) under parent.</summary>
        public static BattleTerrain Build(LevelData level, Transform parent)
        {
            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<BattleTerrain>();
            I.Level = level ?? LevelData.Load(null);
            I.BuildAll();
            return I;
        }

        void BuildAll()
        {
            var lvl = Level;
            // Grid bounds: the level rect grown to the polygons, but at most 25 units outside the level.
            Rect r = new Rect(0, 0, lvl.size.x, lvl.size.y);
            foreach (var p in lvl.polygons)
            {
                r.xMin = Mathf.Min(r.xMin, p.bounds.xMin); r.yMin = Mathf.Min(r.yMin, p.bounds.yMin);
                r.xMax = Mathf.Max(r.xMax, p.bounds.xMax); r.yMax = Mathf.Max(r.yMax, p.bounds.yMax);
            }
            r.xMin = Mathf.Max(r.xMin, -25) - 1; r.yMin = Mathf.Max(r.yMin, -25) - 1;
            r.xMax = Mathf.Min(r.xMax, lvl.size.x + 25) + 1; r.yMax = Mathf.Min(r.yMax, lvl.size.y + 25) + 1;
            origin = new Vector2(Mathf.Floor(r.xMin / Cell) * Cell, Mathf.Floor(r.yMin / Cell) * Cell);
            int w = Mathf.CeilToInt((r.xMax - origin.x) / Cell) + 1;
            int h = Mathf.CeilToInt((r.yMax - origin.y) / Cell) + 1;
            // round up to whole chunks (+1 sample for the last row of squares)
            chunksX = Mathf.CeilToInt((w - 1) / (float)ChunkSize);
            chunksY = Mathf.CeilToInt((h - 1) / (float)ChunkSize);
            w = chunksX * ChunkSize + 1; h = chunksY * ChunkSize + 1;
            solid = new Grid(w, h, true);
            decor = new Grid(w, h, false);

            foreach (var poly in lvl.polygons) Rasterize(poly, poly.noFixtures ? decor : solid, PaletteFor(poly));
            ComputeCaps();
            origDensity = (byte[])solid.density.Clone();
            origMat = (byte[])solid.mat.Clone();
            origCap = (byte[])solid.cap.Clone();

            chunkRoot = new GameObject("Chunks").transform;
            chunkRoot.SetParent(transform, false);
            decorRoot = new GameObject("Decor").transform;
            decorRoot.SetParent(transform, false);
            chunks = new Chunk[chunksX * chunksY];
            decorChunks = new Chunk[chunksX * chunksY];
            for (int cy = 0; cy < chunksY; cy++)
            for (int cx = 0; cx < chunksX; cx++)
            {
                int ci = cy * chunksX + cx;
                chunks[ci] = new Chunk(this, cx, cy, false, chunkRoot);
                decorChunks[ci] = new Chunk(this, cx, cy, true, decorRoot);
                chunks[ci].Rebuild();
                decorChunks[ci].Rebuild();
            }

            var wgo = new GameObject("Water");
            wgo.transform.SetParent(transform, false);
            Water = wgo.AddComponent<WaterVolume>();
            Water.Init(lvl);

            var bgo = new GameObject("Background");
            bgo.transform.SetParent(transform, false);
            Background = bgo.AddComponent<LevelBackground>();
            Background.Init(lvl);

            objectsRoot = new GameObject("LevelObjects").transform;
            objectsRoot.SetParent(transform, false);
            foreach (var o in lvl.objects)
            {
                var e = DynamicObjectEntity.Create(o, objectsRoot);
                if (e != null) LevelObjects.Add(e);
            }
        }

        int PaletteFor(LevelData.TerrainPolygon poly) => PaletteIndex(poly.materialTheme, poly.tint, poly.unbreakable, poly.outline, poly.grassTheme);

        int PaletteIndex(string theme, Color tint, bool unbreakable, bool outline, string capTheme)
        {
            var style = TerrainStyle.Get(theme);
            var cap = string.IsNullOrEmpty(capTheme) ? null : TerrainStyle.Get(capTheme);
            if (cap != null && !cap.hasCap) cap = null;
            for (int i = 1; i < palette.Count; i++)
            {
                var p = palette[i];
                if (p.style == style && p.tint == tint && p.unbreakable == unbreakable && p.outline == outline && p.cap == cap) return i;
            }
            if (palette.Count >= 255) return 1;
            var e = new PaletteEntry { style = style, tint = tint, unbreakable = unbreakable, outline = outline, cap = cap };
            var inner = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), 1);
            e.inner = inner;
            e.border = outline ? (Color32)(TerrainStyle.Compensate(style.border, style.texMean) * Mathf.Lerp(1f, tint.grayscale, 0.5f)) : e.inner;
            e.border.a = 255;
            var scorch = TerrainStyle.Compensate(Color.Lerp(style.explosionBorder, new Color(0.12f, 0.08f, 0.06f), 0.55f), style.texMean);
            e.scorched = scorch;
            if (cap != null) e.capColor = (Color32)(cap.capColor * Mathf.Lerp(1f, tint.grayscale, 0.6f));
            e.capColor.a = 255;
            palette.Add(e);
            return palette.Count - 1;
        }

        // ------------------------------------------------------------------ rasterization

        static readonly List<float> crossings = new List<float>(64);

        /// <summary>
        /// Rasterize a polygon into the grid with an approximate signed distance (from the row and column
        /// crossing distances) so marching squares gives smooth edges. Later polygons draw on top.
        /// </summary>
        void Rasterize(LevelData.TerrainPolygon poly, Grid g, int pal)
        {
            var pts = poly.points;
            int n = pts.Count;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((poly.bounds.xMin - origin.x) / Cell) - 2);
            int i1 = Mathf.Min(g.w - 1, Mathf.CeilToInt((poly.bounds.xMax - origin.x) / Cell) + 2);
            int j0 = Mathf.Max(0, Mathf.FloorToInt((poly.bounds.yMin - origin.y) / Cell) - 2);
            int j1 = Mathf.Min(g.h - 1, Mathf.CeilToInt((poly.bounds.yMax - origin.y) / Cell) + 2);
            if (i1 < i0 || j1 < j0) return;
            int bw = i1 - i0 + 1, bh = j1 - j0 + 1;
            var dh = new float[bw * bh];
            var dv = new float[bw * bh];
            var inside = new bool[bw * bh];

            // rows: horizontal distance to the nearest edge crossing + inside parity
            for (int j = j0; j <= j1; j++)
            {
                float y = origin.y + j * Cell;
                crossings.Clear();
                for (int k = 0; k < n; k++)
                {
                    Vector2 a = pts[k], b = pts[(k + 1) % n];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                        crossings.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                crossings.Sort();
                int c = 0;
                for (int i = i0; i <= i1; i++)
                {
                    float x = origin.x + i * Cell;
                    while (c < crossings.Count && crossings[c] <= x) c++;
                    float d = 1e6f;
                    if (c > 0) d = x - crossings[c - 1];
                    if (c < crossings.Count) d = Mathf.Min(d, crossings[c] - x);
                    int bi = (j - j0) * bw + (i - i0);
                    dh[bi] = d;
                    inside[bi] = (c & 1) == 1;
                }
            }
            // columns: vertical distance to the nearest crossing
            for (int i = i0; i <= i1; i++)
            {
                float x = origin.x + i * Cell;
                crossings.Clear();
                for (int k = 0; k < n; k++)
                {
                    Vector2 a = pts[k], b = pts[(k + 1) % n];
                    if ((a.x <= x && b.x > x) || (b.x <= x && a.x > x))
                        crossings.Add(a.y + (x - a.x) * (b.y - a.y) / (b.x - a.x));
                }
                crossings.Sort();
                int c = 0;
                for (int j = j0; j <= j1; j++)
                {
                    float y = origin.y + j * Cell;
                    while (c < crossings.Count && crossings[c] <= y) c++;
                    float d = 1e6f;
                    if (c > 0) d = y - crossings[c - 1];
                    if (c < crossings.Count) d = Mathf.Min(d, crossings[c] - y);
                    dv[(j - j0) * bw + (i - i0)] = d;
                }
            }
            // combine: distance to a straight edge crossing the axes at dh and dv is dh*dv/sqrt(dh²+dv²)
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int bi = (j - j0) * bw + (i - i0);
                float a = dh[bi], b = dv[bi];
                float dist = a >= 1e5f ? b : b >= 1e5f ? a : a * b / Mathf.Sqrt(Mathf.Max(1e-12f, a * a + b * b));
                float sd = inside[bi] ? dist : -dist;
                byte v = DensityFromDistance(sd);
                int gi = j * g.w + i;
                byte old = g.density[gi];
                if (v >= 128) { g.mat[gi] = (byte)pal; if (v > old) g.density[gi] = v; }
                else if (v > old) { g.density[gi] = v; if (old < 128) g.mat[gi] = (byte)pal; }
            }
        }

        /// <summary>Signed distance (world units, positive inside) → density byte (128 = surface).</summary>
        static byte DensityFromDistance(float sd)
        {
            float t = Mathf.Clamp(sd / Cell, -1f, 1f);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(127.5f + 127.5f * t + (t >= 0 ? 0.5f : -0.5f)), 0, 255);
        }

        /// <summary>Flag solid samples just below an upward-facing original surface (for grass/snow caps).</summary>
        void ComputeCaps()
        {
            var g = solid;
            for (int j = 0; j < g.h; j++)
            for (int i = 0; i < g.w; i++)
            {
                int gi = j * g.w + i;
                if (g.density[gi] < 128) continue;
                var p = palette[g.mat[gi]];
                if (p == null || p.cap == null) continue;
                bool open = false;
                for (int k = 1; k <= 3 && !open; k++)
                {
                    int jj = j + k;
                    if (jj >= g.h || g.density[jj * g.w + i] < 128) open = true;
                }
                if (open) g.cap[gi] = 1;
            }
        }

        // ------------------------------------------------------------------ editing

        /// <summary>Remove terrain in a circle (explosions). Unbreakable terrain is kept.</summary>
        public void Carve(Vector2 center, float radius)
        {
            History.Add(new CraterState { x = center.x, y = center.y, r = radius });
            float removed = CarveRaw(center, radius);
            if (removed > 0.05f)
            {
                WakeBodies(center, radius + 1.5f);
                if (CarveDebris && Application.isPlaying)
                {
                    var c = MaterialColorAt(center, radius);
                    Fx.Debris(center, c, Mathf.Clamp(Mathf.RoundToInt(removed * 1.5f), 3, 16));
                }
            }
            Carved?.Invoke(center, radius, removed);
        }

        /// <summary>Add terrain in a circle (e.g. building weapons).</summary>
        public void Fill(Vector2 center, float radius, string materialTheme = "Stone")
        {
            History.Add(new CraterState { x = center.x, y = center.y, r = radius, add = true });
            FillRaw(center, radius, materialTheme);
            WakeBodies(center, radius + 1f);
        }

        const float ScorchWidth = 0.6f;

        float CarveRaw(Vector2 center, float radius)
        {
            if (solid == null || radius <= 0) return 0;
            var g = solid;
            float outer = radius + ScorchWidth + Cell;
            GridRange(center, outer, out int i0, out int i1, out int j0, out int j1);
            float removed = 0;
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                float dx = origin.x + i * Cell - center.x, dy = origin.y + j * Cell - center.y;
                float sd = Mathf.Sqrt(dx * dx + dy * dy) - radius;   // > 0 outside the crater
                if (sd > outer - radius) continue;
                int gi = j * g.w + i;
                byte old = g.density[gi];
                if (old == 0) continue;
                var pal = palette[g.mat[gi]];
                if (pal != null && pal.unbreakable) continue;
                byte limit = DensityFromDistance(sd);
                if (limit < old)
                {
                    if (old >= 128 && limit < 128) removed += Cell * Cell;
                    g.density[gi] = limit;
                }
                if (sd < ScorchWidth)
                {
                    byte s = (byte)Mathf.Clamp(255f * (1f - Mathf.Max(0, sd) / ScorchWidth), 0, 255);
                    if (s > g.scorch[gi]) g.scorch[gi] = s;
                    g.cap[gi] = 0;
                }
            }
            MarkDirty(i0, i1, j0, j1);
            return removed;
        }

        void FillRaw(Vector2 center, float radius, string materialTheme)
        {
            if (solid == null || radius <= 0) return;
            var g = solid;
            int pal = PaletteIndex(materialTheme, Color.white, false, true, null);
            GridRange(center, radius + Cell * 2, out int i0, out int i1, out int j0, out int j1);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                float dx = origin.x + i * Cell - center.x, dy = origin.y + j * Cell - center.y;
                float sd = radius - Mathf.Sqrt(dx * dx + dy * dy);   // > 0 inside the new blob
                byte v = DensityFromDistance(sd);
                int gi = j * g.w + i;
                byte old = g.density[gi];
                if (v <= old) continue;
                if (v >= 128 || old < 128) { g.mat[gi] = (byte)pal; g.scorch[gi] = 0; }
                g.density[gi] = v;
            }
            MarkDirty(i0, i1, j0, j1);
        }

        void GridRange(Vector2 c, float r, out int i0, out int i1, out int j0, out int j1)
        {
            i0 = Mathf.Clamp(Mathf.FloorToInt((c.x - r - origin.x) / Cell), 0, solid.w - 1);
            i1 = Mathf.Clamp(Mathf.CeilToInt((c.x + r - origin.x) / Cell), 0, solid.w - 1);
            j0 = Mathf.Clamp(Mathf.FloorToInt((c.y - r - origin.y) / Cell), 0, solid.h - 1);
            j1 = Mathf.Clamp(Mathf.CeilToInt((c.y + r - origin.y) / Cell), 0, solid.h - 1);
        }

        /// <summary>Mark every chunk whose mesh depends on samples [i0..i1]x[j0..j1] (incl. border-band margin).</summary>
        void MarkDirty(int i0, int i1, int j0, int j1)
        {
            int m = BandMargin + 1;
            int cx0 = Mathf.Max(0, (i0 - m) / ChunkSize), cx1 = Mathf.Min(chunksX - 1, (i1 + m) / ChunkSize);
            int cy0 = Mathf.Max(0, (j0 - m) / ChunkSize), cy1 = Mathf.Min(chunksY - 1, (j1 + m) / ChunkSize);
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                var c = chunks[cy * chunksX + cx];
                if (c.dirty) continue;
                c.dirty = true;
                dirty.Add(cy * chunksX + cx);
            }
            anyDirty = dirty.Count > 0;
        }

        void WakeBodies(Vector2 center, float radius)
        {
            int n = Physics2D.OverlapCircle(center, radius, Phys.AllFilter, wakeBuffer);
            for (int k = 0; k < n; k++)
            {
                var rb = wakeBuffer[k] != null ? wakeBuffer[k].attachedRigidbody : null;
                if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic) rb.WakeUp();
                wakeBuffer[k] = null;
            }
        }

        /// <summary>Rebuild the meshes/colliders of every chunk changed since the last rebuild (normally done in LateUpdate).</summary>
        public void FlushDirty()
        {
            if (!anyDirty) return;
            for (int k = 0; k < dirty.Count; k++)
            {
                var c = chunks[dirty[k]];
                c.dirty = false;
                c.Rebuild();
            }
            dirty.Clear();
            anyDirty = false;
        }

        void LateUpdate() => FlushDirty();

        /// <summary>Re-apply a terrain history (joining an online match / applying a snapshot).</summary>
        public void ApplyHistory(List<CraterState> craters)
        {
            if (craters == null || solid == null) return;
            // Fast path: our history is a prefix of the given one → only apply the new entries.
            bool prefix = History.Count <= craters.Count;
            for (int k = 0; prefix && k < History.Count; k++)
            {
                var a = History[k]; var b = craters[k];
                if (b == null || a.add != b.add || Mathf.Abs(a.x - b.x) > 0.01f || Mathf.Abs(a.y - b.y) > 0.01f || Mathf.Abs(a.r - b.r) > 0.01f) prefix = false;
            }
            int start = History.Count;
            if (!prefix)
            {
                // Reset to the original level and replay everything.
                System.Array.Copy(origDensity, solid.density, origDensity.Length);
                System.Array.Copy(origMat, solid.mat, origMat.Length);
                System.Array.Copy(origCap, solid.cap, origCap.Length);
                System.Array.Clear(solid.scorch, 0, solid.scorch.Length);
                History.Clear();
                start = 0;
                for (int k = 0; k < chunks.Length; k++)
                    if (!chunks[k].dirty) { chunks[k].dirty = true; dirty.Add(k); }
                anyDirty = true;
            }
            for (int k = start; k < craters.Count; k++)
            {
                var c = craters[k];
                if (c == null) continue;
                History.Add(new CraterState { x = c.x, y = c.y, r = c.r, add = c.add });
                if (c.add) FillRaw(new Vector2(c.x, c.y), c.r, "Stone");
                else CarveRaw(new Vector2(c.x, c.y), c.r);
            }
            FlushDirty();
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Bilinear density at a world point (0..255, ≥ 128 is solid). Outside the grid = 0.</summary>
        public float DensityAt(Vector2 p)
        {
            if (solid == null) return 0;
            float fx = (p.x - origin.x) / Cell, fy = (p.y - origin.y) / Cell;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fy);
            if (i < 0 || j < 0 || i >= solid.w - 1 || j >= solid.h - 1) return 0;
            float tx = fx - i, ty = fy - j;
            int gi = j * solid.w + i;
            var d = solid.density;
            float a = Mathf.Lerp(d[gi], d[gi + 1], tx), b = Mathf.Lerp(d[gi + solid.w], d[gi + solid.w + 1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        public bool IsSolid(Vector2 p) => DensityAt(p) >= 127.5f;

        /// <summary>
        /// Highest solid point at x below fromY (for spawning / AI). Returns false if none. If fromY is just inside
        /// the ground (≤ 0.6 units) the surface above it is returned; deeper inside, the next floor below the block.
        /// </summary>
        public bool GroundBelow(float x, float fromY, out Vector2 ground)
        {
            ground = new Vector2(x, WaterY);
            if (solid == null) return false;
            float fx = (x - origin.x) / Cell;
            int i = Mathf.FloorToInt(fx);
            if (i < 0 || i >= solid.w - 1) return false;
            float tx = fx - i;
            int jStart = Mathf.Min(solid.h - 1, Mathf.FloorToInt((fromY - origin.y) / Cell));
            if (jStart < 0) return false;
            var d = solid.density;
            int w = solid.w;
            float prev = Mathf.Lerp(d[jStart * w + i], d[jStart * w + i + 1], tx);
            if (prev >= 127.5f)
            {
                // fromY is inside terrain: just under a surface → that surface; inside a ceiling/thick block → look
                // for the next floor below the solid run.
                int up = jStart;
                while (up < solid.h - 1 && up - jStart <= 3 && Mathf.Lerp(d[(up + 1) * w + i], d[(up + 1) * w + i + 1], tx) >= 127.5f) up++;
                if (up < solid.h - 1 && up - jStart <= 3)
                {
                    float a = Mathf.Lerp(d[up * w + i], d[up * w + i + 1], tx), b = Mathf.Lerp(d[(up + 1) * w + i], d[(up + 1) * w + i + 1], tx);
                    ground = new Vector2(x, origin.y + (up + Mathf.Clamp01((127.5f - a) / (b - a))) * Cell);
                    return true;
                }
                int j = jStart;
                while (j > 0 && Mathf.Lerp(d[j * w + i], d[j * w + i + 1], tx) >= 127.5f) j--;
                if (j <= 0) return false;
                jStart = j;
                prev = Mathf.Lerp(d[j * w + i], d[j * w + i + 1], tx);
            }
            for (int j = jStart - 1; j >= 0; j--)
            {
                float cur = Mathf.Lerp(d[j * w + i], d[j * w + i + 1], tx);
                if (cur >= 127.5f)
                {
                    float t = (127.5f - cur) / (prev - cur);   // fraction from j toward j+1
                    ground = new Vector2(x, origin.y + (j + Mathf.Clamp01(t)) * Cell);
                    return true;
                }
                prev = cur;
            }
            return false;
        }

        /// <summary>Outward surface normal (from the density gradient) at a point near the surface.</summary>
        public Vector2 NormalAt(Vector2 p)
        {
            const float e = Cell;
            float gx = DensityAt(p + new Vector2(e, 0)) - DensityAt(p - new Vector2(e, 0));
            float gy = DensityAt(p + new Vector2(0, e)) - DensityAt(p - new Vector2(0, e));
            var n = new Vector2(-gx, -gy);
            return n.sqrMagnitude > 1e-6f ? n.normalized : Vector2.up;
        }

        /// <summary>March from a to b; returns true and the first solid point if terrain is hit (line of sight / AI aiming).</summary>
        public bool Raycast(Vector2 a, Vector2 b, out Vector2 hit)
        {
            hit = b;
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.75f)));
            Vector2 prev = a;
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(a, b, s / (float)steps);
                if (IsSolid(p))
                {
                    // refine between prev and p
                    Vector2 lo = prev, hi = p;
                    for (int k = 0; k < 5; k++) { var m = (lo + hi) * 0.5f; if (IsSolid(m)) hi = m; else lo = m; }
                    hit = hi;
                    return true;
                }
                prev = p;
            }
            return false;
        }

        /// <summary>Material theme id of the terrain at p (nearest solid sample within 1 unit), or null.</summary>
        public string MaterialAt(Vector2 p)
        {
            var e = PaletteNear(p, 1f);
            return e != null ? e.style.id : null;
        }

        PaletteEntry PaletteNear(Vector2 p, float radius)
        {
            if (solid == null) return null;
            int ci = Mathf.RoundToInt((p.x - origin.x) / Cell), cj = Mathf.RoundToInt((p.y - origin.y) / Cell);
            int rr = Mathf.CeilToInt(radius / Cell);
            PaletteEntry best = null; int bestD = int.MaxValue;
            for (int j = cj - rr; j <= cj + rr; j++)
            for (int i = ci - rr; i <= ci + rr; i++)
            {
                if (i < 0 || j < 0 || i >= solid.w || j >= solid.h) continue;
                int gi = j * solid.w + i;
                if (solid.mat[gi] == 0) continue;
                int d = (i - ci) * (i - ci) + (j - cj) * (j - cj);
                if (d < bestD) { bestD = d; best = palette[solid.mat[gi]]; }
            }
            return best;
        }

        Color MaterialColorAt(Vector2 p, float radius)
        {
            var e = PaletteNear(p, radius + 0.5f);
            if (e == null) return new Color(0.5f, 0.4f, 0.3f);
            var c = e.style.baseColor * e.tint;
            c.a = 1;
            return c;
        }

        /// <summary>
        /// A random standing spot on the ground (above water, not inside terrain, room for a penguin) — for respawns
        /// and power-up drops. Falls back to a level spawn point.
        /// </summary>
        public Vector2 FindSpawnPoint(System.Random rnd, float clearance = 1.2f, float minDistanceFromPenguins = 3f)
        {
            if (rnd == null) rnd = new System.Random();
            var lvl = Level;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float x = Mathf.Lerp(lvl.size.x * 0.05f, lvl.size.x * 0.95f, (float)rnd.NextDouble());
                if (!GroundBelow(x, lvl.size.y + 10, out var g)) continue;
                if (g.y < WaterY + 1f) continue;
                var stand = g + Vector2.up * (clearance * 0.5f + 0.05f);
                if (IsSolid(stand + Vector2.up * clearance * 0.5f) || IsSolid(stand + Vector2.left * clearance * 0.4f) || IsSolid(stand + Vector2.right * clearance * 0.4f)) continue;
                if (Mathf.Abs(NormalAt(g + Vector2.down * 0.1f).y) < 0.6f) continue;   // too steep
                bool near = false;
                foreach (var p in BattleWorld.Penguins)
                    if (p != null && p.Alive && (p.Position - stand).sqrMagnitude < minDistanceFromPenguins * minDistanceFromPenguins) { near = true; break; }
                if (near) continue;
                return g + Vector2.up * 0.05f;
            }
            var sp = lvl.spawnPoints;
            return sp.Count > 0 ? sp[rnd.Next(sp.Count)] : new Vector2(lvl.size.x * 0.5f, lvl.size.y * 0.8f);
        }

        /// <summary>True only for the terrain's own colliders (not water, objects or background).</summary>
        public bool IsTerrainCollider(Collider2D c) => c != null && chunkRoot != null && c.transform.parent == chunkRoot;

        /// <summary>World rect covered by the terrain grid.</summary>
        public Rect GridRect => solid == null ? new Rect() : new Rect(origin, new Vector2((solid.w - 1) * Cell, (solid.h - 1) * Cell));

        // ------------------------------------------------------------------ level objects (snapshots)

        /// <summary>State of every level object (for BattleSnapshot.objects).</summary>
        public void CaptureObjects(List<DynamicObjectState> into)
        {
            into.Clear();
            foreach (var o in LevelObjects) if (o != null) into.Add(o.Capture());
        }

        /// <summary>Apply level object states from a snapshot (objects missing or dead in it are destroyed silently).</summary>
        public void ApplyObjects(List<DynamicObjectState> states)
        {
            if (states == null) return;
            for (int k = LevelObjects.Count - 1; k >= 0; k--)
            {
                var o = LevelObjects[k];
                if (o == null) { LevelObjects.RemoveAt(k); continue; }
                DynamicObjectState s = null;
                foreach (var st in states) if (st != null && st.id == o.Id) { s = st; break; }
                if (s == null || !s.alive) o.Remove();   // also removes it from LevelObjects
                else o.Apply(s);
            }
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (chunks != null) foreach (var c in chunks) c?.Dispose();
            if (decorChunks != null) foreach (var c in decorChunks) c?.Dispose();
        }
    }
}
