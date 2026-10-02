using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public partial class BattleTerrain
    {
        /// <summary>Samples of margin used for the border band (distance to the surface is measured up to this).</summary>
        internal const int BandMargin = 3;

        /// <summary>
        /// One ChunkSize² block of marching squares: a mesh (one submesh per material + cap strips) and, for solid
        /// terrain, EdgeCollider2D polylines along the surface. All rebuild buffers are static and reused.
        /// </summary>
        internal class Chunk
        {
            readonly BattleTerrain t;
            readonly int cx, cy;
            readonly bool isDecor;
            public bool dirty;
            readonly GameObject go;
            Mesh mesh;
            readonly MeshFilter mf;
            readonly MeshRenderer mr;
            readonly List<EdgeCollider2D> edges = new List<EdgeCollider2D>();
            Material[] mats = new Material[0];

            public Chunk(BattleTerrain t, int cx, int cy, bool isDecor, Transform parent)
            {
                this.t = t; this.cx = cx; this.cy = cy; this.isDecor = isDecor;
                go = new GameObject((isDecor ? "Decor_" : "Chunk_") + cx + "_" + cy);
                go.transform.SetParent(parent, false);
                mf = go.AddComponent<MeshFilter>();
                mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;
            }

            public void Dispose()
            {
                if (mesh) Object.Destroy(mesh);
            }

            // ---------------------------------------------------------- shared rebuild buffers
            const int R = ChunkSize + 1 + 2 * BandMargin;      // depth region side (samples)
            static readonly int[] depth = new int[R * R];
            static readonly List<Vector3> verts = new List<Vector3>(4096);
            static readonly List<Color32> cols = new List<Color32>(4096);
            static readonly List<Vector2> uvs = new List<Vector2>(4096);
            static readonly List<List<int>> tris = new List<List<int>>();
            static readonly int[] slotOfKey = new int[512];
            static readonly List<int> slotKeys = new List<int>();
            // collider segments
            static readonly List<Vector2> segA = new List<Vector2>(512), segB = new List<Vector2>(512);
            static readonly List<int> segEA = new List<int>(512), segEB = new List<int>(512), segPal = new List<int>(512);
            static readonly List<bool> segUsed = new List<bool>(512);
            static readonly Dictionary<int, int> startOf = new Dictionary<int, int>(512);
            static readonly HashSet<int> endIds = new HashSet<int>();
            static readonly List<Vector2> line = new List<Vector2>(256), simple = new List<Vector2>(256);
            // per-square polygon scratch
            static readonly int[] polyA = new int[8], polyB = new int[8];
            static readonly Vector2[] edgePos = new Vector2[4];
            static readonly int[] edgeId = new int[4];
            static readonly Color32[] edgeCol = new Color32[4];
            static readonly Vector2[] cornerPos = new Vector2[4];
            static readonly Color32[] cornerCol = new Color32[4];
            static readonly float[] bandF = BuildBand();

            static float[] BuildBand()
            {
                var f = new float[3 * BandMargin + 1];
                for (int d = 0; d < f.Length; d++) f[d] = d == 0 ? 1 : Mathf.Pow(Mathf.Clamp01(1f - (d - 2) / 7f), 1.5f);
                return f;
            }

            int Slot(int key, bool cap)
            {
                int k = key + (cap ? 256 : 0);
                int s = slotOfKey[k];
                if (s > 0) return s - 1;
                slotKeys.Add(k);
                if (tris.Count < slotKeys.Count) tris.Add(new List<int>(2048));
                tris[slotKeys.Count - 1].Clear();
                slotOfKey[k] = slotKeys.Count;
                return slotKeys.Count - 1;
            }

            /// <summary>Rebuild mesh and colliders from the grid.</summary>
            public void Rebuild()
            {
                var g = isDecor ? t.decor : t.solid;
                int i0 = cx * ChunkSize, j0 = cy * ChunkSize;
                verts.Clear(); cols.Clear(); uvs.Clear();
                foreach (var k in slotKeys) slotOfKey[k] = 0;
                slotKeys.Clear();
                segA.Clear(); segB.Clear(); segEA.Clear(); segEB.Clear(); segPal.Clear();

                // quick reject: nothing solid in or around the chunk
                bool any = false;
                for (int j = j0; j <= j0 + ChunkSize && !any; j++)
                {
                    int row = j * g.w;
                    for (int i = i0; i <= i0 + ChunkSize; i++) if (g.density[row + i] >= 128) { any = true; break; }
                }
                if (any)
                {
                    ComputeDepth(g, i0, j0);
                    March(g, i0, j0);
                }
                Upload();
                if (!isDecor) BuildColliders();
            }

            /// <summary>Chamfer distance (orthogonal 3, diagonal 4) from each solid sample to the nearest empty one, capped.</summary>
            void ComputeDepth(Grid g, int i0, int j0)
            {
                int cap = 3 * BandMargin;
                int bx = i0 - BandMargin, by = j0 - BandMargin;
                for (int y = 0; y < R; y++)
                {
                    int gj = by + y;
                    for (int x = 0; x < R; x++)
                    {
                        int gi = bx + x;
                        bool s = gi >= 0 && gj >= 0 && gi < g.w && gj < g.h && g.density[gj * g.w + gi] >= 128;
                        depth[y * R + x] = s ? cap : 0;
                    }
                }
                for (int y = 0; y < R; y++)
                for (int x = 0; x < R; x++)
                {
                    int k = y * R + x, d = depth[k];
                    if (d == 0) continue;
                    if (x > 0) d = Mathf.Min(d, depth[k - 1] + 3);
                    if (y > 0)
                    {
                        d = Mathf.Min(d, depth[k - R] + 3);
                        if (x > 0) d = Mathf.Min(d, depth[k - R - 1] + 4);
                        if (x < R - 1) d = Mathf.Min(d, depth[k - R + 1] + 4);
                    }
                    depth[k] = d;
                }
                for (int y = R - 1; y >= 0; y--)
                for (int x = R - 1; x >= 0; x--)
                {
                    int k = y * R + x, d = depth[k];
                    if (d == 0) continue;
                    if (x < R - 1) d = Mathf.Min(d, depth[k + 1] + 3);
                    if (y < R - 1)
                    {
                        d = Mathf.Min(d, depth[k + R] + 3);
                        if (x < R - 1) d = Mathf.Min(d, depth[k + R + 1] + 4);
                        if (x > 0) d = Mathf.Min(d, depth[k + R - 1] + 4);
                    }
                    depth[k] = d;
                }
            }

            int DepthAt(int gi, int gj, int i0, int j0) => depth[(gj - j0 + BandMargin) * R + (gi - i0 + BandMargin)];

            Color32 SampleColor(PaletteEntry p, int d, byte scorch)
            {
                Color32 c = p.inner;
                if (p.outline)
                {
                    float f = bandF[Mathf.Min(d, bandF.Length - 1)];
                    if (f > 0.001f) c = Color32.Lerp(c, p.border, f);
                }
                if (scorch > 0) c = Color32.Lerp(c, p.scorched, scorch / 255f * 0.9f);
                return c;
            }

            void March(Grid g, int i0, int j0)
            {
                var pal = t.palette;
                var dens = g.density;
                var mat = g.mat;
                var scorch = g.scorch;
                var capF = g.cap;
                int w = g.w;
                Vector2 org = t.origin;
                const int Flat = 3 * BandMargin;

                for (int j = j0; j < j0 + ChunkSize; j++)
                {
                    int i = i0;
                    while (i < i0 + ChunkSize)
                    {
                        int s0 = j * w + i, s1 = s0 + 1, s2 = s0 + w + 1, s3 = s0 + w;
                        int d0 = dens[s0], d1 = dens[s1], d2 = dens[s2], d3 = dens[s3];
                        int cs = (d0 >= 128 ? 1 : 0) | (d1 >= 128 ? 2 : 0) | (d2 >= 128 ? 4 : 0) | (d3 >= 128 ? 8 : 0);
                        if (cs == 0) { i++; continue; }

                        // ---- greedy run of fully interior squares with one material: one quad
                        if (cs == 15 && IsFlat(g, s0, w, i, j, i0, j0, Flat))
                        {
                            int m = mat[s0];
                            int e = i + 1;
                            while (e < i0 + ChunkSize)
                            {
                                int q0 = j * w + e;
                                if (dens[q0 + 1] < 128 || dens[q0 + w + 1] < 128 || mat[q0 + 1] != m || mat[q0 + w + 1] != m) break;
                                if (!IsFlat(g, q0, w, e, j, i0, j0, Flat)) break;
                                e++;
                            }
                            var pe = pal[m];
                            int slot = Slot(pe.style.index, false);
                            // grown by a hair so T-junctions with neighbouring squares never show cracks
                            const float eps = 0.003f;
                            float x0 = org.x + i * Cell - eps, x1 = org.x + e * Cell + eps, y0 = org.y + j * Cell - eps, y1 = org.y + (j + 1) * Cell + eps;
                            int b = verts.Count;
                            AddV(x0, y0, pe.inner, pe.style); AddV(x1, y0, pe.inner, pe.style);
                            AddV(x1, y1, pe.inner, pe.style); AddV(x0, y1, pe.inner, pe.style);
                            var tl = tris[slot];
                            tl.Add(b); tl.Add(b + 2); tl.Add(b + 1);
                            tl.Add(b); tl.Add(b + 3); tl.Add(b + 2);
                            i = e;
                            continue;
                        }

                        // ---- general marching square
                        // material: the solid corner with the highest density
                        int best = s0, bd = -1;
                        if (d0 >= 128 && d0 > bd) { bd = d0; best = s0; }
                        if (d1 >= 128 && d1 > bd) { bd = d1; best = s1; }
                        if (d2 >= 128 && d2 > bd) { bd = d2; best = s2; }
                        if (d3 >= 128 && d3 > bd) { bd = d3; best = s3; }
                        var p = pal[mat[best]];
                        if (p == null) { i++; continue; }

                        float bx = org.x + i * Cell, by = org.y + j * Cell;
                        cornerPos[0] = new Vector2(bx, by); cornerPos[1] = new Vector2(bx + Cell, by);
                        cornerPos[2] = new Vector2(bx + Cell, by + Cell); cornerPos[3] = new Vector2(bx, by + Cell);
                        byte sc0 = scorch != null ? scorch[s0] : (byte)0, sc1 = scorch != null ? scorch[s1] : (byte)0;
                        byte sc2 = scorch != null ? scorch[s2] : (byte)0, sc3 = scorch != null ? scorch[s3] : (byte)0;
                        if ((cs & 1) != 0) cornerCol[0] = SampleColor(p, DepthAt(i, j, i0, j0), sc0);
                        if ((cs & 2) != 0) cornerCol[1] = SampleColor(p, DepthAt(i + 1, j, i0, j0), sc1);
                        if ((cs & 4) != 0) cornerCol[2] = SampleColor(p, DepthAt(i + 1, j + 1, i0, j0), sc2);
                        if ((cs & 8) != 0) cornerCol[3] = SampleColor(p, DepthAt(i, j + 1, i0, j0), sc3);
                        // edge points: e0 bottom (c0-c1), e1 right (c1-c2), e2 top (c3-c2), e3 left (c0-c3)
                        if (((cs ^ (cs >> 1)) & 1) != 0) EdgePoint(0, cornerPos[0], cornerPos[1], d0, d1, sc0, sc1, p, (j * w + i) * 2);
                        if ((((cs >> 1) ^ (cs >> 2)) & 1) != 0) EdgePoint(1, cornerPos[1], cornerPos[2], d1, d2, sc1, sc2, p, (j * w + i + 1) * 2 + 1);
                        if ((((cs >> 3) ^ (cs >> 2)) & 1) != 0) EdgePoint(2, cornerPos[3], cornerPos[2], d3, d2, sc3, sc2, p, ((j + 1) * w + i) * 2);
                        if (((cs ^ (cs >> 3)) & 1) != 0) EdgePoint(3, cornerPos[0], cornerPos[3], d0, d3, sc0, sc3, p, (j * w + i) * 2 + 1);

                        int slotT = Slot(p.style.index, false);
                        bool saddleSplit = (cs == 5 || cs == 10) && (d0 + d1 + d2 + d3) * 0.25f < 128;
                        int na = 0, nb = 0;
                        if (!saddleSplit)
                        {
                            // CCW walk: c0 e0 c1 e1 c2 e2 c3 e3 (corners 0..3, edges 4..7)
                            if ((cs & 1) != 0) polyA[na++] = 0;
                            if (((cs ^ (cs >> 1)) & 1) != 0) polyA[na++] = 4;
                            if ((cs & 2) != 0) polyA[na++] = 1;
                            if ((((cs >> 1) ^ (cs >> 2)) & 1) != 0) polyA[na++] = 5;
                            if ((cs & 4) != 0) polyA[na++] = 2;
                            if ((((cs >> 3) ^ (cs >> 2)) & 1) != 0) polyA[na++] = 6;
                            if ((cs & 8) != 0) polyA[na++] = 3;
                            if (((cs ^ (cs >> 3)) & 1) != 0) polyA[na++] = 7;
                        }
                        else if (cs == 5)
                        {
                            polyA[0] = 0; polyA[1] = 4; polyA[2] = 7; na = 3;
                            polyB[0] = 5; polyB[1] = 2; polyB[2] = 6; nb = 3;
                        }
                        else
                        {
                            polyA[0] = 4; polyA[1] = 1; polyA[2] = 5; na = 3;
                            polyB[0] = 6; polyB[1] = 3; polyB[2] = 7; nb = 3;
                        }
                        bool capOk = !isDecor && p.cap != null &&
                                     (((cs & 1) != 0 && capF[s0] != 0) || ((cs & 2) != 0 && capF[s1] != 0) ||
                                      ((cs & 4) != 0 && capF[s2] != 0) || ((cs & 8) != 0 && capF[s3] != 0)) &&
                                     sc0 < 60 && sc1 < 60 && sc2 < 60 && sc3 < 60;
                        EmitPoly(polyA, na, p, slotT, mat[best], capOk);
                        if (nb > 0) EmitPoly(polyB, nb, p, slotT, mat[best], capOk);
                        i++;
                    }
                }
            }

            bool IsFlat(Grid g, int s0, int w, int i, int j, int i0, int j0, int flat)
            {
                int m = g.mat[s0];
                if (g.mat[s0 + 1] != m || g.mat[s0 + w] != m || g.mat[s0 + w + 1] != m) return false;
                var sc = g.scorch;
                if (sc != null && (sc[s0] | sc[s0 + 1] | sc[s0 + w] | sc[s0 + w + 1]) != 0) return false;
                var p = t.palette[m];
                if (p == null) return false;
                if (!p.outline) return true;
                return DepthAt(i, j, i0, j0) >= flat && DepthAt(i + 1, j, i0, j0) >= flat &&
                       DepthAt(i, j + 1, i0, j0) >= flat && DepthAt(i + 1, j + 1, i0, j0) >= flat;
            }

            static void EdgePoint(int e, Vector2 a, Vector2 b, int da, int db, byte sa, byte sb, PaletteEntry p, int id)
            {
                float tt = (127.5f - da) / (float)(db - da);
                edgePos[e] = Vector2.Lerp(a, b, Mathf.Clamp(tt, 0.02f, 0.98f));
                edgeId[e] = id;
                byte sc = da >= 128 ? sa : sb;
                Color32 c = p.outline ? p.border : p.inner;
                if (sc > 0) c = Color32.Lerp(c, p.scorched, sc / 255f * 0.9f);
                edgeCol[e] = c;
            }

            void AddV(float x, float y, Color32 c, MaterialStyle st)
            {
                verts.Add(new Vector3(x, y, 0));
                cols.Add(c);
                uvs.Add(new Vector2(x / st.tileWorld, y / st.tileWorld));
            }

            void EmitPoly(int[] poly, int n, PaletteEntry p, int slot, int palIndex, bool capOk)
            {
                int b = verts.Count;
                for (int k = 0; k < n; k++)
                {
                    int r = poly[k];
                    if (r < 4) AddV(cornerPos[r].x, cornerPos[r].y, cornerCol[r], p.style);
                    else AddV(edgePos[r - 4].x, edgePos[r - 4].y, edgeCol[r - 4], p.style);
                }
                var tl = tris[slot];
                for (int k = 1; k < n - 1; k++) { tl.Add(b); tl.Add(b + k + 1); tl.Add(b + k); }

                // surface segments: consecutive edge points in CCW order (solid on the left of A→B)
                for (int k = 0; k < n; k++)
                {
                    int ra = poly[k], rb = poly[(k + 1) % n];
                    if (ra < 4 || rb < 4) continue;
                    Vector2 A = edgePos[ra - 4], B = edgePos[rb - 4];
                    if (!isDecor)
                    {
                        segA.Add(A); segB.Add(B);
                        segEA.Add(edgeId[ra - 4]); segEB.Add(edgeId[rb - 4]);
                        segPal.Add(palIndex);
                    }
                    if (capOk)
                    {
                        Vector2 d = B - A;
                        float len = d.magnitude;
                        if (len < 1e-4f) continue;
                        float ny = -d.x / len;   // outward normal = right-hand normal (d.y, -d.x)
                        if (ny < Mathf.Cos((p.cap.capAngle + 15f) * Mathf.Deg2Rad)) continue;
                        AddCap(A, B, p);
                    }
                }
            }

            void AddCap(Vector2 A, Vector2 B, PaletteEntry p)
            {
                const float down = 0.16f, up = 0.34f, z = -0.02f, uScale = 1f / 1.6f;
                int slot = Slot(p.cap.index, true);
                int b = verts.Count;
                var c = p.capColor;
                verts.Add(new Vector3(A.x, A.y - down, z)); cols.Add(c); uvs.Add(new Vector2(A.x * uScale, 0));
                verts.Add(new Vector3(B.x, B.y - down, z)); cols.Add(c); uvs.Add(new Vector2(B.x * uScale, 0));
                verts.Add(new Vector3(B.x, B.y + up, z)); cols.Add(c); uvs.Add(new Vector2(B.x * uScale, 1));
                verts.Add(new Vector3(A.x, A.y + up, z)); cols.Add(c); uvs.Add(new Vector2(A.x * uScale, 1));
                var tl = tris[slot];
                tl.Add(b); tl.Add(b + 2); tl.Add(b + 1);
                tl.Add(b); tl.Add(b + 3); tl.Add(b + 2);
            }

            void Upload()
            {
                if (verts.Count == 0)
                {
                    if (mesh) mesh.Clear();
                    mr.enabled = false;
                    return;
                }
                if (!mesh)
                {
                    mesh = new Mesh { name = go.name };
                    mesh.MarkDynamic();
                    mf.sharedMesh = mesh;
                }
                mesh.Clear();
                mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(verts);
                mesh.SetColors(cols);
                mesh.SetUVs(0, uvs);
                // terrain submeshes first, cap strips last (they are alpha blended)
                int n = slotKeys.Count;
                mesh.subMeshCount = n;
                bool same = mats.Length == n;
                for (int s = 0; s < n; s++)
                {
                    mesh.SetTriangles(tris[s], s, false);
                    var m = MaterialForKey(slotKeys[s]);
                    if (same && mats[s] != m) same = false;
                }
                mesh.RecalculateBounds();
                if (!same)
                {
                    mats = new Material[n];
                    for (int s = 0; s < n; s++) mats[s] = MaterialForKey(slotKeys[s]);
                    mr.sharedMaterials = mats;
                }
                mr.enabled = true;
            }

            Material MaterialForKey(int key)
            {
                bool cap = key >= 256;
                var st = StyleByIndex(key & 255);
                if (st == null) return Mats.Unlit(Color.gray);
                if (cap) return TerrainStyle.CapMaterial(st.id, isDecor);
                return isDecor ? st.Decor : st.Solid;
            }

            MaterialStyle StyleByIndex(int idx)
            {
                foreach (var p in t.palette)
                {
                    if (p == null) continue;
                    if (p.style.index == idx) return p.style;
                    if (p.cap != null && p.cap.index == idx) return p.cap;
                }
                return null;
            }

            // ---------------------------------------------------------- colliders

            void BuildColliders()
            {
                int used = 0;
                int n = segA.Count;
                if (n > 0)
                {
                    startOf.Clear(); endIds.Clear(); segUsed.Clear();
                    for (int s = 0; s < n; s++)
                    {
                        startOf[segEA[s]] = s;
                        endIds.Add(segEB[s]);
                        segUsed.Add(false);
                    }
                    // open chains first (they start at the chunk border), then closed loops
                    for (int pass = 0; pass < 2; pass++)
                    for (int s = 0; s < n; s++)
                    {
                        if (segUsed[s]) continue;
                        if (pass == 0 && endIds.Contains(segEA[s])) continue;
                        line.Clear();
                        int cur = s;
                        line.Add(segA[cur]);
                        int pal = segPal[s];
                        while (true)
                        {
                            segUsed[cur] = true;
                            line.Add(segB[cur]);
                            if (!startOf.TryGetValue(segEB[cur], out int next) || segUsed[next]) break;
                            cur = next;
                        }
                        if (line.Count < 2) continue;
                        Simplify();
                        if (simple.Count < 2) continue;
                        var ec = Edge(used++);
                        ec.SetPoints(simple);
                        var pe = t.palette[pal];
                        if (pe != null) ec.sharedMaterial = pe.style.Physics;
                    }
                }
                for (int k = used; k < edges.Count; k++) if (edges[k].enabled) edges[k].enabled = false;
            }

            EdgeCollider2D Edge(int k)
            {
                if (k >= edges.Count) edges.Add(go.AddComponent<EdgeCollider2D>());
                var e = edges[k];
                if (!e.enabled) e.enabled = true;
                return e;
            }

            /// <summary>Drop points that lie (almost) on the line between their neighbors.</summary>
            static void Simplify()
            {
                simple.Clear();
                simple.Add(line[0]);
                for (int k = 1; k < line.Count - 1; k++)
                {
                    Vector2 a = simple[simple.Count - 1], p = line[k], c = line[k + 1];
                    Vector2 ac = c - a;
                    float l = ac.magnitude;
                    if (l < 1e-5f) { if ((p - a).sqrMagnitude < 1e-6f) continue; simple.Add(p); continue; }
                    float dist = Mathf.Abs(ac.x * (p.y - a.y) - ac.y * (p.x - a.x)) / l;
                    if (dist > 0.012f || (p - a).sqrMagnitude > 4f) simple.Add(p);
                }
                var last = line[line.Count - 1];
                if ((last - simple[simple.Count - 1]).sqrMagnitude > 1e-8f || simple.Count == 1) simple.Add(last);
            }
        }
    }
}
