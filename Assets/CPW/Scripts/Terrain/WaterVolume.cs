using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The water (lava on Mountain levels, mud on Desert) across the level below Level.waterY: an animated surface
    /// with ripples, buoyancy + drag for Rigidbody2D bodies under the surface and splashes when something enters.
    /// Drowning is decided by the battle code (penguin y below WaterY); this only does visuals and physics.
    ///
    /// Detection uses a trigger BoxCollider2D on a kinematic body (useFullKinematicContacts so kinematic
    /// projectiles are seen too), on the built-in Ignore Raycast layer so default queries skip it.
    /// </summary>
    public class WaterVolume : MonoBehaviour
    {
        /// <summary>
        /// Buoyancy for bodies that are not level objects (penguins, projectiles), as a fraction of their weight
        /// when fully submerged. Below 1 they sink slowly through the drag.
        /// </summary>
        public static float OtherBodyBuoyancy = 0.85f;
        /// <summary>Converts the level's water_lineardrag (Nape) to a Unity drag coefficient (1/s).</summary>
        public static float DragScale = 0.25f;
        /// <summary>Optional filter: return false to leave a body alone (e.g. a penguin that already drowned).</summary>
        public static System.Func<Rigidbody2D, bool> Affects;
        /// <summary>Raised when a body enters the water (body, entry point, downward speed).</summary>
        public static event System.Action<Rigidbody2D, Vector2, float> Entered;

        public float SurfaceY { get; private set; }
        public bool IsLava { get; private set; }

        LevelData level;
        float waterDensity, linDrag, angDrag;
        Vector2 flow;

        class Tracked { public Rigidbody2D rb; public Collider2D col; public int count; public float area; public DynamicObjectEntity obj; }
        readonly List<Tracked> bodies = new List<Tracked>();
        readonly Dictionary<Rigidbody2D, Tracked> byBody = new Dictionary<Rigidbody2D, Tracked>();
        readonly Stack<Tracked> pool = new Stack<Tracked>();

        // surface simulation: columns every ColW units with a spring each, spread to neighbours
        const float ColW = 0.5f;
        int cols;
        float x0;
        float[] h, v;
        Mesh front, back, foamMesh;
        readonly List<Object> owned = new List<Object>();   // meshes/materials/textures created here, destroyed with the water
        Vector3[] fv, bv, foamV;
        Material waterMat, foamMat;
        string kind = "water";   // water, lava, mud
        const float Deep = 60f;
        float time;

        // original water_tile art (Textures/Water/{liquid}): the surface band repeats every tileW units along x
        Texture2D tile;
        float tileW = 9.9f, bandTop = 0.4f, bandH = 5.45f;

        // lava: rising bubbles that pop at the surface and embers drifting up (one additive mesh)
        const int FxCount = 26;
        Mesh fxMesh;
        Vector3[] fxV;
        Color[] fxC;
        readonly float[] fxX = new float[FxCount], fxT = new float[FxCount], fxLife = new float[FxCount], fxSize = new float[FxCount];
        Material glowMat;
        Color glowBase;

        public void Init(LevelData lvl)
        {
            level = lvl;
            SurfaceY = lvl.waterY;
            waterDensity = lvl.waterDensity * DynamicObjectEntity.DensityScale;
            linDrag = lvl.waterLinearDrag * DragScale;
            angDrag = lvl.waterAngularDrag * DragScale;
            flow = lvl.waterVelocity;
            string liquid = string.IsNullOrEmpty(lvl.liquid) ? TerrainStyle.LiquidFor(lvl.theme) : lvl.liquid;
            TerrainStyle.WaterColors(lvl.theme, liquid, out var body, out var surface, out bool lava);
            IsLava = lava;
            kind = lava ? "lava" : liquid == "Mud" ? "mud" : "water";
            if (TerrainStyle.OriginalArt("Water/" + liquid, out var tilePx, out var tileF))
            {
                tile = Resources.Load<Texture2D>("Textures/Water/" + liquid);
                if (tile != null)
                {
                    tile.wrapModeU = TextureWrapMode.Repeat;
                    tile.wrapModeV = TextureWrapMode.Clamp;
                    tileW = tilePx.x / 20f;
                    bandH = tilePx.y / 20f;
                    // the tile's first fully covered row sits on the physics surface; the wave crests above it
                    int row = 0;
                    if (tileF.Length > 3) int.TryParse(tileF[3], out row);
                    bandTop = row / 20f;
                    body = TerrainStyle.OriginalColor(tileF, 4, body);
                }
            }

            float xMin = Mathf.Min(0, lvl.cameraBounds.xMin) - 40, xMax = Mathf.Max(lvl.size.x, lvl.cameraBounds.xMax) + 40;
            x0 = xMin;
            cols = Mathf.CeilToInt((xMax - xMin) / ColW) + 1;
            h = new float[cols];
            v = new float[cols];

            // physics trigger on the built-in "Ignore Raycast" layer so default raycasts/overlaps never hit the water
            gameObject.layer = 2;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(xMax - xMin, Deep);
            box.offset = new Vector2((xMin + xMax) * 0.5f, SurfaceY - Deep * 0.5f);

            // visuals: a darker strip behind the playfield and a translucent front surface
            Color bodyA = body; bodyA.a = lava ? 0.92f : 0.72f;
            Color deepA = Color.Lerp(body, Color.black, 0.45f); deepA.a = 0.95f;
            Color foam = surface; foam.a = lava ? 1f : 0.9f;
            if (tile != null)
            {
                // the original water tile: its own wave crest, highlights and body gradient; the back copy is darker
                waterMat = new Material(Mats.TransparentShader) { mainTexture = tile, color = Color.white, name = "Water_" + kind };
                var backMat = new Material(Mats.TransparentShader) { mainTexture = tile, color = new Color(0.62f, 0.62f, 0.7f, 1f), name = "WaterBack_" + kind };
                owned.Add(waterMat); owned.Add(backMat);
                back = BuildStrip("WaterBack", 0.9f, Color.white, Color.white, Color.white, out bv, 0.25f);
                back.uv = TileUVs(tileW * 0.37f);
                front = BuildStrip("WaterFront", -0.7f, Color.white, Color.white, Color.white, out fv, 0f);
                front.uv = TileUVs(0f);
                transform.Find("WaterBack").GetComponent<MeshRenderer>().sharedMaterial = backMat;
            }
            else
            {
                waterMat = new Material(Mats.TransparentShader) { mainTexture = BodyTexture(kind), color = new Color(1.16f, 1.16f, 1.16f, 1f), name = "Water_" + kind };
                foamMat = new Material(Mats.TransparentShader) { mainTexture = FoamTexture(kind), color = Color.white, name = "WaterFoam_" + kind };
                owned.Add(waterMat); owned.Add(foamMat);
                back = BuildStrip("WaterBack", 0.9f, Color.Lerp(body, surface, 0.25f) * 0.8f, Color.Lerp(body, Color.black, 0.3f), Color.Lerp(body, Color.black, 0.5f), out bv, 0.18f);
                // the front surface row is the water color brightened toward the foam; the painted foam strip sits on top
                Color lip = Color.Lerp(bodyA, foam, lava ? 0.85f : 0.45f); lip.a = lava ? 1f : 0.85f;
                front = BuildStrip("WaterFront", -0.7f, lip, bodyA, deepA, out fv, 0f);
                Color foamC = lava ? new Color(1f, 0.85f, 0.35f, 1f) : kind == "mud" ? new Color(0.62f, 0.5f, 0.36f, 0.95f) : new Color(0.93f, 0.98f, 1f, 0.95f);
                foamMesh = BuildFoam(foamC, out foamV);
            }
            if (lava)
            {
                // a soft additive glow above the lava
                var glow = new GameObject("LavaGlow");
                glow.transform.SetParent(transform, false);
                var mf = glow.AddComponent<MeshFilter>();
                var mr = glow.AddComponent<MeshRenderer>();
                var m = new Mesh { name = "LavaGlow" };
                owned.Add(m);
                m.vertices = new[] { new Vector3(xMin, SurfaceY - 0.2f, -0.75f), new Vector3(xMax, SurfaceY - 0.2f, -0.75f), new Vector3(xMax, SurfaceY + 2.5f, -0.75f), new Vector3(xMin, SurfaceY + 2.5f, -0.75f) };
                var gc = new Color(1f, 0.45f, 0.1f, 0.35f); var gt = new Color(1f, 0.45f, 0.1f, 0f);
                m.colors = new[] { gc, gc, gt, gt };
                m.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mf.sharedMesh = m;
                glowBase = Color.white;
                glowMat = new Material(Mats.AdditiveShader) { color = glowBase, name = "LavaGlow" };
                owned.Add(glowMat);
                mr.sharedMaterial = glowMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                BuildLavaFx();
            }
            UpdateMeshes();
        }

        /// <summary>UVs of a strip for the original tile: u along x, v from the band top (1) to its bottom (0) and below.</summary>
        Vector2[] TileUVs(float uOffset)
        {
            var uv = new Vector2[cols * 3];
            for (int k = 0; k < cols; k++)
            {
                float u = (x0 + k * ColW + uOffset) / tileW;
                uv[k * 3] = new Vector2(u, 1f);
                uv[k * 3 + 1] = new Vector2(u, 0f);
                uv[k * 3 + 2] = new Vector2(u, 0f);
            }
            return uv;
        }

        // ------------------------------------------------------------------ lava bubbles and embers

        void BuildLavaFx()
        {
            var go = new GameObject("LavaFx");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = new Material(Mats.AdditiveShader) { mainTexture = Mats.SoftCircle, color = Color.white, name = "LavaFx" };
            owned.Add(mat);
            mr.sharedMaterial = mat;
            fxMesh = new Mesh { name = "LavaFx" };
            fxMesh.MarkDynamic();
            owned.Add(fxMesh);
            fxV = new Vector3[FxCount * 4];
            fxC = new Color[FxCount * 4];
            var uv = new Vector2[FxCount * 4];
            var tris = new int[FxCount * 6];
            for (int i = 0; i < FxCount; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(1, 0); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(0, 1);
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
                tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 2;
                fxT[i] = -Random.value * 3f;   // staggered starts
            }
            fxMesh.vertices = fxV;
            fxMesh.uv = uv;
            fxMesh.colors = fxC;
            fxMesh.triangles = tris;
            fxMesh.bounds = new Bounds(new Vector3((x0 + x0 + cols * ColW) * 0.5f, SurfaceY, -0.8f), new Vector3(cols * ColW + 2, 12, 2));
            mf.sharedMesh = fxMesh;
        }

        void UpdateLavaFx(float dt)
        {
            if (fxMesh == null) return;
            var cam = Camera.main;
            float cx = cam != null ? cam.transform.position.x : x0 + cols * ColW * 0.5f;
            float half = cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect + 2f : 22f;
            for (int i = 0; i < FxCount; i++)
            {
                fxT[i] += dt;
                bool ember = i % 3 == 0;
                if (fxT[i] >= fxLife[i])
                {
                    // respawn somewhere on screen
                    fxX[i] = cx + (Random.value * 2f - 1f) * half;
                    fxLife[i] = ember ? Random.Range(1.6f, 2.8f) : Random.Range(0.7f, 1.4f);
                    fxSize[i] = ember ? Random.Range(0.12f, 0.22f) : Random.Range(0.25f, 0.6f);
                    fxT[i] = fxT[i] > 0 ? 0 : fxT[i];
                }
                float t = Mathf.Clamp01(fxT[i] / Mathf.Max(0.01f, fxLife[i]));
                float x = fxX[i], y, r, a;
                Color c;
                if (fxT[i] < 0) { r = 0; y = SurfaceY; a = 0; c = Color.black; }
                else if (ember)
                {
                    x += Mathf.Sin(fxT[i] * 3f + i) * 0.25f;
                    y = SurfaceAt(fxX[i]) + t * 3.2f;
                    r = fxSize[i] * (1f - t * 0.5f);
                    a = Mathf.Sin(t * Mathf.PI);
                    c = new Color(1f, 0.55f, 0.15f) * a;
                }
                else
                {
                    // a bubble swells on the surface, then pops in a quick bright flash
                    y = SurfaceAt(fxX[i]) - 0.08f;
                    float pop = Mathf.Clamp01((t - 0.8f) / 0.2f);
                    r = fxSize[i] * (0.4f + 0.6f * Mathf.Sqrt(Mathf.Min(t / 0.8f, 1f))) * (1f + pop * 0.8f);
                    a = (0.5f + pop * 0.5f) * (1f - pop);
                    c = new Color(1f, 0.78f, 0.3f) * a;
                }
                int b = i * 4;
                fxV[b] = new Vector3(x - r, y - r, -0.8f); fxV[b + 1] = new Vector3(x + r, y - r, -0.8f);
                fxV[b + 2] = new Vector3(x + r, y + r, -0.8f); fxV[b + 3] = new Vector3(x - r, y + r, -0.8f);
                c.a = 1f;
                fxC[b] = fxC[b + 1] = fxC[b + 2] = fxC[b + 3] = c;
            }
            fxMesh.vertices = fxV;
            fxMesh.colors = fxC;
            // heat shimmer stand-in: the glow above the lava breathes
            if (glowMat != null) glowMat.color = glowBase * (0.8f + 0.2f * Mathf.Sin(time * 2.1f) + 0.08f * Mathf.Sin(time * 5.3f));
        }

        /// <summary>A strip of columns: surface row (foam color), a row just below, and the deep bottom row.</summary>
        Mesh BuildStrip(string name, float z, Color top, Color mid, Color bottom, out Vector3[] verts, float lift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = waterMat;
            var m = new Mesh { name = name };
            owned.Add(m);
            m.MarkDynamic();
            verts = new Vector3[cols * 3];
            var colors = new Color[cols * 3];
            var uv = new Vector2[cols * 3];
            var tris = new int[(cols - 1) * 12];
            for (int k = 0; k < cols; k++)
            {
                float x = x0 + k * ColW;
                verts[k * 3] = new Vector3(x, SurfaceY + lift, z);
                verts[k * 3 + 1] = new Vector3(x, SurfaceY + lift - 0.3f, z);
                verts[k * 3 + 2] = new Vector3(x, SurfaceY - Deep, z);
                colors[k * 3] = top; colors[k * 3 + 1] = mid; colors[k * 3 + 2] = bottom;
                // world-space UVs (the body texture repeats every 6 units; v follows depth)
                uv[k * 3] = new Vector2(x / 6f, 0f); uv[k * 3 + 1] = new Vector2(x / 6f, -0.3f / 6f); uv[k * 3 + 2] = new Vector2(x / 6f, -Deep / 6f);
                if (k == cols - 1) continue;
                int t = k * 12, a = k * 3, b = (k + 1) * 3;
                tris[t] = a; tris[t + 1] = b; tris[t + 2] = a + 1;
                tris[t + 3] = b; tris[t + 4] = b + 1; tris[t + 5] = a + 1;
                tris[t + 6] = a + 1; tris[t + 7] = b + 1; tris[t + 8] = a + 2;
                tris[t + 9] = b + 1; tris[t + 10] = b + 2; tris[t + 11] = a + 2;
            }
            m.vertices = verts;
            m.colors = colors;
            m.uv = uv;
            m.triangles = tris;
            m.bounds = new Bounds(new Vector3((x0 + x0 + cols * ColW) * 0.5f, SurfaceY - Deep * 0.5f, z), new Vector3(cols * ColW + 2, Deep + 4, 1));
            mf.sharedMesh = m;
            return m;
        }

        /// <summary>Foam/crust strip that rides on the front surface (scalloped bubbles with an ink edge).</summary>
        Mesh BuildFoam(Color c, out Vector3[] verts)
        {
            var go = new GameObject("WaterFoam");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = foamMat;
            var m = new Mesh { name = "WaterFoam" };
            owned.Add(m);
            m.MarkDynamic();
            verts = new Vector3[cols * 2];
            var colors = new Color[cols * 2];
            var uv = new Vector2[cols * 2];
            var tris = new int[(cols - 1) * 6];
            for (int k = 0; k < cols; k++)
            {
                float x = x0 + k * ColW;
                verts[k * 2] = new Vector3(x, SurfaceY + FoamUp, -0.72f);
                verts[k * 2 + 1] = new Vector3(x, SurfaceY - FoamDown, -0.72f);
                colors[k * 2] = colors[k * 2 + 1] = c;
                uv[k * 2] = new Vector2(x / 3.2f, 1f);
                uv[k * 2 + 1] = new Vector2(x / 3.2f, 0f);
                if (k == cols - 1) continue;
                int t = k * 6, a = k * 2, b = (k + 1) * 2;
                tris[t] = a; tris[t + 1] = b; tris[t + 2] = a + 1;
                tris[t + 3] = b; tris[t + 4] = b + 1; tris[t + 5] = a + 1;
            }
            m.vertices = verts;
            m.colors = colors;
            m.uv = uv;
            m.triangles = tris;
            m.bounds = new Bounds(new Vector3((x0 + x0 + cols * ColW) * 0.5f, SurfaceY, -0.72f), new Vector3(cols * ColW + 2, 6, 1));
            mf.sharedMesh = m;
            return m;
        }

        const float FoamUp = 0.16f, FoamDown = 0.34f;

        /// <summary>64x64 tileable body texture: soft light streaks/caustics (water), crust cells (lava), bubbles (mud).</summary>
        Texture2D BodyTexture(string k)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "WaterBody_" + k };
            owned.Add(t);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float w1 = Mathf.Sin((u * 2f + Mathf.Sin(v * Mathf.PI * 2f * 2f) * 0.12f) * Mathf.PI * 2f * 2f);
                float w2 = Mathf.Sin((u * 3f - v * 1f + Mathf.Sin(u * Mathf.PI * 2f) * 0.2f) * Mathf.PI * 2f * 2f);
                float g;
                if (k == "lava")
                {
                    // dark crust plates with glowing seams
                    float seam = Mathf.Abs(w1 * 0.6f + w2 * 0.4f);
                    g = seam < 0.18f ? 1.15f : Mathf.Lerp(0.72f, 0.9f, Mathf.PerlinNoise(u * 6f, v * 6f));
                }
                else if (k == "mud")
                {
                    float b = Mathf.PerlinNoise(u * 8f + 3f, v * 8f + 1f);
                    g = 0.85f + 0.12f * b + (b > 0.72f ? 0.12f : 0f);
                }
                else
                {
                    // long horizontal glints that read as gentle waves
                    float glint = Mathf.Clamp01((w1 * 0.55f + w2 * 0.45f - 0.72f) * 4f);
                    g = 0.86f + 0.08f * Mathf.PerlinNoise(u * 5f, v * 5f) + glint * 0.32f;
                }
                byte gb = (byte)Mathf.Clamp(g * 220f, 0, 255);   // 220 = 1.0 (headroom for highlights is in the vertex color)
                px[y * n + x] = new Color32(gb, gb, gb, 255);
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>128x32 foam strip: solid at the top, scalloped bubbly lower edge with a darker ink line (alpha).</summary>
        Texture2D FoamTexture(string k)
        {
            const int W = 128, H = 32;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "WaterFoam_" + k };
            owned.Add(t);
            var px = new Color32[W * H];
            float surf = FoamDown / (FoamUp + FoamDown);           // v of the water line
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                // scallops: bubbles of a few sizes hanging below the line
                float sc = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 7f)) * 0.55f + Mathf.Abs(Mathf.Sin(u * Mathf.PI * 11f + 1.3f)) * 0.45f;
                float lower = surf - 0.18f - sc * 0.32f;
                float upper = surf + 0.22f + 0.12f * Mathf.Sin(u * Mathf.PI * 2f * 3f) + 0.06f * Mathf.Sin(u * Mathf.PI * 2f * 8f + 2f);
                for (int y = 0; y < H; y++)
                {
                    float v = (y + 0.5f) / H;
                    float a = Mathf.Clamp01(Mathf.Min(v - lower, upper - v) * H * 0.8f + 0.5f);
                    float shade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01((v - lower) / Mathf.Max(0.01f, upper - lower) * 1.6f));
                    // ink along the scalloped lower edge
                    if (v - lower < 2.2f / H) shade *= k == "lava" ? 0.75f : 0.62f;
                    // small bubble holes
                    float bx = u * 24f, by = v * 6f;
                    float bd = Mathf.Abs(bx - Mathf.Round(bx)) + Mathf.Abs(by - Mathf.Round(by) - 0.1f);
                    if (bd < 0.18f && v < upper - 0.15f && v > lower + 0.12f && ((int)Mathf.Round(bx) * 7 + (int)Mathf.Round(by) * 3) % 5 == 0) shade *= 0.82f;
                    byte s8 = (byte)Mathf.Clamp(shade * 255f, 0, 255);
                    px[y * W + x] = new Color32(s8, s8, s8, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Visual surface height at x (waves + ripples).</summary>
        public float SurfaceAt(float x)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt((x - x0) / ColW), 0, cols - 1);
            return SurfaceY + h[k] + Wave(x0 + k * ColW, 0);
        }

        float Wave(float x, float phase)
        {
            float amp = IsLava ? 0.06f : 0.1f;
            float sp = IsLava ? 0.6f : 1.4f;
            return amp * Mathf.Sin(x * 0.55f + time * sp + phase) + amp * 0.6f * Mathf.Sin(x * 1.3f - time * sp * 1.3f + phase * 2);
        }

        /// <summary>Push the surface down/up at x (ripples). Called on entry; weapons may call it for underwater explosions.</summary>
        public void Disturb(float x, float strength)
        {
            int k = Mathf.RoundToInt((x - x0) / ColW);
            for (int d = -2; d <= 2; d++)
            {
                int kk = k + d;
                if (kk < 0 || kk >= cols) continue;
                v[kk] -= strength * (1f - Mathf.Abs(d) * 0.3f);
            }
        }

        void Update()
        {
            time += Time.deltaTime;
            UpdateMeshes();
            // drift the textures (no mesh upload): body glints slide slowly, the foam a bit faster
            float sp = IsLava ? 0.35f : 1f;
            if (IsLava) UpdateLavaFx(Time.deltaTime);
            if (tile != null)
            {
                // the original tile drifts sideways slowly (with the level's flow)
                if (waterMat != null) waterMat.mainTextureOffset = new Vector2(time * 0.012f * sp + flow.x * time * 0.01f, 0f);
                return;
            }
            if (waterMat != null) waterMat.mainTextureOffset = new Vector2(time * 0.025f * sp + flow.x * time * 0.01f, Mathf.Sin(time * 0.5f) * 0.01f);
            if (foamMat != null) foamMat.mainTextureOffset = new Vector2(-time * 0.06f * sp, 0f);
        }

        void UpdateMeshes()
        {
            if (front == null) return;
            for (int k = 0; k < cols; k++)
            {
                float x = x0 + k * ColW;
                float y = SurfaceY + h[k] + Wave(x, 0);
                float yb = SurfaceY + 0.18f + h[k] * 0.5f + Wave(x, 1.7f) * 1.3f;
                if (tile != null)
                {
                    // original tile band: its crest above the surface, the body below, the bottom color further down
                    fv[k * 3].y = y + bandTop;
                    fv[k * 3 + 1].y = y + bandTop - bandH;
                    bv[k * 3].y = yb + 0.25f + bandTop;
                    bv[k * 3 + 1].y = yb + 0.25f + bandTop - bandH;
                    continue;
                }
                fv[k * 3].y = y;
                fv[k * 3 + 1].y = y - (IsLava ? 0.45f : 0.3f);
                bv[k * 3].y = yb;
                bv[k * 3 + 1].y = yb - 0.3f;
                if (foamV != null)
                {
                    foamV[k * 2].y = y + FoamUp;
                    foamV[k * 2 + 1].y = y - FoamDown;
                }
            }
            front.vertices = fv;
            back.vertices = bv;
            if (foamMesh != null) foamMesh.vertices = foamV;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // ripple springs
            const float k = 60f, damp = 3.5f, spread = 0.25f;
            for (int i = 0; i < cols; i++)
            {
                v[i] += (-k * h[i] - damp * v[i]) * dt;
                h[i] += v[i] * dt;
            }
            for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < cols - 1; i++)
            {
                float d = spread * (h[i - 1] + h[i + 1] - 2 * h[i]);
                v[i] += d * 8f * dt;
            }

            // buoyancy and drag
            float g = Mathf.Abs(Physics2D.gravity.y);
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var t = bodies[i];
                if (t.rb == null || t.col == null || !t.col.enabled || !t.rb.simulated) { Untrack(i); continue; }
                var rb = t.rb;
                if (rb.bodyType != RigidbodyType2D.Dynamic) continue;
                if (Affects != null && !Affects(rb)) continue;
                var b = t.col.bounds;
                float f = Mathf.Clamp01((SurfaceY - b.min.y) / Mathf.Max(0.05f, b.size.y));
                if (f <= 0) continue;
                float weight = rb.mass * g * rb.gravityScale;
                float buoy = t.obj != null ? waterDensity * t.area * g * rb.gravityScale * f : weight * OtherBodyBuoyancy * f;
                Vector2 rel = rb.Vel() - flow;
                Vector2 force = new Vector2(0, buoy) - rel * (rb.mass * linDrag * f);
                rb.AddForce(force);
                rb.angularVelocity *= 1f - Mathf.Clamp01(angDrag * f * dt);
            }
        }

        void OnTriggerEnter2D(Collider2D c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null || c.isTrigger) return;
            if (byBody.TryGetValue(rb, out var t)) { t.count++; return; }
            t = pool.Count > 0 ? pool.Pop() : new Tracked();
            t.rb = rb; t.col = c; t.count = 1;
            t.obj = rb.GetComponent<DynamicObjectEntity>();
            var b = c.bounds;
            t.area = t.obj != null ? t.obj.Area : b.size.x * b.size.y * 0.8f;
            bodies.Add(t);
            byBody[rb] = t;

            Vector2 vel = rb.Vel();
            float speed = -vel.y;
            if (speed > 1.2f && Application.isPlaying)
            {
                var p = new Vector2(rb.position.x, SurfaceY);
                float size = Mathf.Clamp(b.size.x * 0.6f + speed * 0.04f, 0.4f, 3f);
                Fx.Splash(p, size);
                Disturb(p.x, Mathf.Min(6f, speed * 0.35f) * Mathf.Clamp(b.size.x, 0.4f, 2f));
                PlaySplash(b.size.x, speed);
                Entered?.Invoke(rb, p, speed);
            }
        }

        void OnTriggerExit2D(Collider2D c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null || c.isTrigger || !byBody.TryGetValue(rb, out var t)) return;
            if (--t.count > 0)
            {
                if (t.col == c)
                {
                    // the collider we measured left; pick another one of the body
                    foreach (var other in rb.GetComponentsInChildren<Collider2D>()) if (other != c && !other.isTrigger) { t.col = other; break; }
                }
                return;
            }
            int i = bodies.IndexOf(t);
            if (i >= 0) Untrack(i);
        }

        void Untrack(int i)
        {
            var t = bodies[i];
            if (t.rb != null) byBody.Remove(t.rb);
            else
            {
                // destroyed body: remove its dead key
                foreach (var kv in byBody) if (kv.Value == t) { byBody.Remove(kv.Key); break; }
            }
            bodies.RemoveAt(i);
            t.rb = null; t.col = null; t.obj = null;
            pool.Push(t);
        }

        static readonly string[] speedNames = { "VerySlow", "Slow", "Medium", "Fast", "VeryFast", "SuperFast" };

        void PlaySplash(float width, float speed)
        {
            string size = width < 0.9f ? "Small" : width < 2.2f ? "Medium" : "Big";
            int s = speed < 3 ? 0 : speed < 6 ? 1 : speed < 10 ? 2 : speed < 15 ? 3 : speed < 22 ? 4 : 5;
            AudioManager.Sfx("WaterHit" + size + speedNames[s], 0.8f);
        }

        void OnDestroy()
        {
            foreach (var o in owned) if (o) Destroy(o);
        }

        /// <summary>True if the collider belongs to the water (for code that raycasts with triggers enabled).</summary>
        public static bool IsWater(Collider2D c) => c != null && c.GetComponent<WaterVolume>() != null;
    }
}
