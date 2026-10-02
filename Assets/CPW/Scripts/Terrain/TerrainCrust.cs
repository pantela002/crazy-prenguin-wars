using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The top crust (grass, snow, sand, rock) like the original TerrainDisplayObject.drawTopTiles:
    /// GeomUtils.findLineSegments splits each polygon of a grass theme into runs of edges flatter than the theme's
    /// Angle (|atan2(dy, dx)| &lt; Angle: the left-to-right top edges); the landmass_tile strip follows those runs
    /// centred on the edge, landmass_end_left / landmass_end_right cap both ends of a run and landmass_filler covers
    /// the joints where the surface turns down.
    ///
    /// Here the runs flag the grid samples under them (Grid.cap): chunk meshes draw the tiled strip on surface
    /// segments next to flagged, unscorched samples, so craters erase it like the original erased its bitmap.
    /// End caps and fillers are quads of one level-wide mesh (one draw call per bitmap) that is rebuilt with the
    /// chunks; a piece disappears once the ground under it is carved, scorched or covered.
    /// </summary>
    public partial class BattleTerrain
    {
        /// <summary>World units around a run that get the crust flag (the strip is ±0.6 units around the edge).</summary>
        const float CrustReach = 0.6f;

        struct CrustPiece
        {
            public Vector2 pos, dir;      // centre on the edge, unit direction of the edge (left to right)
            public Vector2 probe;         // point just inside the ground that must stay solid and flagged
            public byte kind;             // 0 end_left, 1 end_right, 2 filler
            public byte pal;              // palette entry (tint and cap style)
        }

        static readonly string[] CrustBitmaps = { "landmass_end_left", "landmass_end_right", "landmass_filler" };

        readonly List<CrustPiece> crustPieces = new List<CrustPiece>();
        Mesh crustMesh;
        MeshRenderer crustRenderer;
        readonly List<Material> crustMats = new List<Material>();
        readonly List<int> crustKeys = new List<int>();                  // cap style index * 4 + kind per submesh
        static readonly List<Vector3> crustV = new List<Vector3>(512);
        static readonly List<Vector2> crustUV = new List<Vector2>(512);
        static readonly List<Color32> crustC = new List<Color32>(512);
        static readonly List<List<int>> crustT = new List<List<int>>();
        static readonly Dictionary<int, Material> crustMatCache = new Dictionary<int, Material>();
        static readonly List<Vector2> runPts = new List<Vector2>(64);

        /// <summary>Flag the samples under each polygon's top runs and collect the end caps / fillers.</summary>
        void ComputeCrust()
        {
            crustPieces.Clear();
            foreach (var poly in Level.polygons)
            {
                if (poly.noFixtures || string.IsNullOrEmpty(poly.grassTheme)) continue;
                int pal = PaletteFor(poly);
                var pe = palette[pal];
                if (pe == null || pe.cap == null) continue;
                var pts = poly.points;
                int n = pts.Count;
                int start = StartIndex(pts);
                if (start < 0) continue;
                float limit = Mathf.Max(1f, pe.cap.capAngle);
                // GeomUtils.findLineSegments: walk the closed outline once from the start index, collecting runs of
                // consecutive edges whose angle passes the test
                int i = start;
                while (i < start + n)
                {
                    runPts.Clear();
                    while (Flat(pts[i % n], pts[(i + 1) % n], limit))
                    {
                        if (runPts.Count == 0) runPts.Add(pts[i % n]);
                        runPts.Add(pts[(i + 1) % n]);
                        i++;
                        if (i >= start + n) break;
                    }
                    if (runPts.Count >= 2) AddRun(pal);
                    i++;
                }
            }
        }

        /// <summary>GeomUtils.findStartIndex: first point whose predecessor is not to its left.</summary>
        static int StartIndex(List<Vector2> pts)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                var prev = pts[i == 0 ? pts.Count - 1 : i - 1];
                if (prev.x >= pts[i].x) return i;
            }
            return -1;
        }

        /// <summary>angleLess: |atan2(dy, dx)| below the theme angle (y flipped vs Flash; |angle| is the same).</summary>
        static bool Flat(Vector2 a, Vector2 b, float limitDeg)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-8f) return false;
            return Mathf.Abs(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg) < limitDeg;
        }

        void AddRun(int pal)
        {
            // only runs that really are a top surface now (all polygons rasterized): ground below, air above.
            // A polygon wound the other way would otherwise put crust under its bottom edges.
            var a0 = runPts[0];
            var d0 = (runPts[1] - a0).normalized;
            var mid = a0 + (runPts[1] - a0) * 0.5f;
            var up0 = new Vector2(-d0.y, d0.x);
            if (DensityAt(mid - up0 * 0.35f) < 128 || DensityAt(mid + up0 * 0.6f) >= 128) return;

            var g = solid;
            for (int k = 0; k + 1 < runPts.Count; k++) FlagSegment(g, runPts[k], runPts[k + 1]);

            float half = CrustHalfTile(pal);
            int last = runPts.Count - 1;
            var dl = (runPts[last] - runPts[last - 1]).normalized;
            var upl = new Vector2(-dl.y, dl.x);
            crustPieces.Add(new CrustPiece { pos = a0, dir = d0, probe = a0 + d0 * 0.3f - up0 * 0.3f, kind = 0, pal = (byte)pal });
            crustPieces.Add(new CrustPiece { pos = runPts[last], dir = dl, probe = runPts[last] - dl * 0.3f - upl * 0.3f, kind = 1, pal = (byte)pal });
            // fillers: joints where the next edge goes down (or stays level) and is at least half a tile long
            for (int k = 1; k < last; k++)
            {
                var p = runPts[k];
                var e = runPts[k + 1] - p;
                float len = e.magnitude;
                if (e.y > 0 || len < half) continue;
                var d = e / len;
                var up = new Vector2(-d.y, d.x);
                var c = p + d * (half * 0.5f);
                crustPieces.Add(new CrustPiece { pos = c, dir = d, probe = c - up * 0.3f, kind = 2, pal = (byte)pal });
            }
        }

        /// <summary>Half the width of the theme's landmass_tile (the original steps its tiles by this), in units.</summary>
        float CrustHalfTile(int pal)
        {
            var pe = palette[pal];
            var s = pe != null && pe.cap != null ? TerrainStyle.LandmassSprite(pe.cap.id, "landmass_tile") : null;
            return s != null ? s.rect.width / s.pixelsPerUnit * 0.5f : 0.9f;
        }

        /// <summary>Set the crust flag on the solid samples within CrustReach of a run edge.</summary>
        void FlagSegment(Grid g, Vector2 a, Vector2 b)
        {
            float r = CrustReach;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r - origin.x) / Cell));
            int i1 = Mathf.Min(g.w - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + r - origin.x) / Cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - r - origin.y) / Cell));
            int j1 = Mathf.Min(g.h - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + r - origin.y) / Cell));
            var ab = b - a;
            float l2 = Mathf.Max(1e-8f, ab.sqrMagnitude);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int gi = j * g.w + i;
                if (g.density[gi] < 128) continue;
                var p = new Vector2(origin.x + i * Cell, origin.y + j * Cell);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                if ((a + ab * t - p).sqrMagnitude <= r * r) g.cap[gi] = 1;
            }
        }

        /// <summary>True while the ground under a crust piece is intact: solid, flagged, unscorched, open above.</summary>
        bool CrustPieceAlive(in CrustPiece c)
        {
            var g = solid;
            int i = Mathf.RoundToInt((c.probe.x - origin.x) / Cell), j = Mathf.RoundToInt((c.probe.y - origin.y) / Cell);
            if (i < 0 || j < 0 || i >= g.w || j >= g.h) return false;
            int gi = j * g.w + i;
            if (g.density[gi] < 128 || g.cap[gi] == 0 || g.scorch[gi] >= 60) return false;
            var up = new Vector2(-c.dir.y, c.dir.x);
            return DensityAt(c.pos + up * 0.55f) < 128;
        }

        /// <summary>(Re)build the end-cap / filler mesh from the pieces whose ground is still there.</summary>
        void RebuildCrust()
        {
            if (crustPieces.Count == 0) return;
            crustV.Clear(); crustUV.Clear(); crustC.Clear();
            crustKeys.Clear();
            foreach (var l in crustT) l.Clear();
            foreach (var c in crustPieces)
            {
                var pe = palette[c.pal];
                if (pe == null || pe.cap == null || !CrustPieceAlive(c)) continue;
                var s = TerrainStyle.LandmassSprite(pe.cap.id, CrustBitmaps[c.kind]);
                if (s == null || !TerrainStyle.RepeatableTexture(s)) continue;
                int key = pe.cap.index * 4 + c.kind;
                int sub = crustKeys.IndexOf(key);
                if (sub < 0)
                {
                    sub = crustKeys.Count;
                    crustKeys.Add(key);
                    if (crustT.Count <= sub) crustT.Add(new List<int>(64));
                }
                // native size, centred on the edge point and rotated with the edge (drawTile)
                float hw = s.rect.width / s.pixelsPerUnit * 0.5f, hh = s.rect.height / s.pixelsPerUnit * 0.5f;
                var ax = c.dir * hw;
                var ay = new Vector2(-c.dir.y, c.dir.x) * hh;
                float shade = Mathf.Lerp(1f, pe.tint.grayscale, 0.6f);
                var col = (Color32)new Color(Mathf.Clamp01(shade), Mathf.Clamp01(shade), Mathf.Clamp01(shade), 1f);
                int b = crustV.Count;
                const float z = -0.03f;   // just in front of the strips (-0.02)
                crustV.Add(new Vector3(c.pos.x - ax.x - ay.x, c.pos.y - ax.y - ay.y, z));
                crustV.Add(new Vector3(c.pos.x + ax.x - ay.x, c.pos.y + ax.y - ay.y, z));
                crustV.Add(new Vector3(c.pos.x + ax.x + ay.x, c.pos.y + ax.y + ay.y, z));
                crustV.Add(new Vector3(c.pos.x - ax.x + ay.x, c.pos.y - ax.y + ay.y, z));
                crustUV.Add(new Vector2(0, 0)); crustUV.Add(new Vector2(1, 0)); crustUV.Add(new Vector2(1, 1)); crustUV.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) crustC.Add(col);
                var t = crustT[sub];
                t.Add(b); t.Add(b + 2); t.Add(b + 1);
                t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            if (crustMesh == null)
            {
                if (crustV.Count == 0) return;
                var go = new GameObject("Crust");
                go.transform.SetParent(transform, false);
                crustMesh = new Mesh { name = "Crust" };
                crustMesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = crustMesh;
                crustRenderer = go.AddComponent<MeshRenderer>();
                crustRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                crustRenderer.receiveShadows = false;
                crustRenderer.sortingOrder = 1;   // after the chunk strips (0), before level objects (2)
            }
            crustMesh.Clear();
            if (crustV.Count == 0) { crustRenderer.enabled = false; return; }
            crustMesh.SetVertices(crustV);
            crustMesh.SetUVs(0, crustUV);
            crustMesh.SetColors(crustC);
            crustMesh.subMeshCount = crustKeys.Count;
            for (int s = 0; s < crustKeys.Count; s++) crustMesh.SetTriangles(crustT[s], s, false);
            crustMesh.RecalculateBounds();
            bool same = crustMats.Count == crustKeys.Count;
            for (int s = 0; s < crustKeys.Count && same; s++) same = crustMats[s] == CrustMaterial(crustKeys[s]);
            if (!same)
            {
                crustMats.Clear();
                for (int s = 0; s < crustKeys.Count; s++) crustMats.Add(CrustMaterial(crustKeys[s]));
                crustRenderer.sharedMaterials = crustMats.ToArray();
            }
            crustRenderer.enabled = true;
        }

        Material CrustMaterial(int key)
        {
            if (crustMatCache.TryGetValue(key, out var m) && m) return m;
            MaterialStyle st = null;
            foreach (var p in palette)
                if (p != null && p.cap != null && p.cap.index == key / 4) { st = p.cap; break; }
            var s = st != null ? TerrainStyle.LandmassSprite(st.id, CrustBitmaps[key % 4]) : null;
            m = new Material(Mats.TransparentShader) { name = "Crust_" + (st != null ? st.id : "?") + "_" + key % 4, color = Color.white };
            if (s != null)
            {
                m.mainTexture = s.texture;
                var uv = TerrainStyle.SpriteUV(s);
                m.mainTextureScale = uv.size;
                m.mainTextureOffset = uv.min;
            }
            crustMatCache[key] = m;
            return m;
        }
    }
}
