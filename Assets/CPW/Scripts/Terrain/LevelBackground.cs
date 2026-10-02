using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Sky gradient and parallax background of a level. Layers come from the level's parallax_layers
    /// (camera_x_pan / camera_y_pan: 0 = moves with the world, 1 = fixed to the screen; gap; tile_horizontally)
    /// and are filled with the Blender environment models "Env/{Theme}_{Name}" when they exist, otherwise with
    /// procedural silhouettes (trees, pines, peaks, cacti, clouds). A few procedural ridges are always added far back.
    ///
    /// Look: a five-stop sky with a sun (or volcano) glow, a cloud bank, three ridges whose facets are lit from the
    /// top-left (light slopes face the sun) with theme features (tree lines, snow caps, mesa strata, volcano glow)
    /// and a mist band at their feet, then the level's layers. Env models in far layers drop the ink outline.
    ///
    /// Everything here renders before the terrain (render queues below 2000, see TerrainStyle) so it can never
    /// cover the playfield. Updates itself from the active camera (Camera.main or the highest-depth enabled one) after other scripts' LateUpdate; call
    /// <see cref="ParallaxUpdate(Camera)"/> yourself and set <see cref="SelfUpdate"/> false to drive it manually.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class LevelBackground : MonoBehaviour
    {
        public static bool SelfUpdate = true;

        class Layer
        {
            public Transform root;
            public float panX, panY, z;
        }

        readonly List<Layer> layers = new List<Layer>();
        readonly List<Object> owned = new List<Object>();   // meshes/materials created here
        LevelData level;
        Vector2 cref;                 // camera position at which layers sit at their nominal level coordinates
        Transform sky, sunGlow;
        Color skyTop, skyBottom;
        Color silhouette;
        System.Random rnd;
        string theme;
        string look;                  // level style (Volcano, IceCave) or theme
        string bgTheme;               // original background art folder (Forest, Winter, Mountain, Desert)
        Texture2D gradient;           // original background gradient (Textures/Sky/{bgTheme}_Gradient)
        Material skyMat;
        Camera cam;
        static readonly Dictionary<string, Texture2D> parallaxTex = new Dictionary<string, Texture2D>();

        // environment models available for this theme, by category
        readonly List<string> envNear = new List<string>(), envFar = new List<string>(), envSky = new List<string>();
        static readonly Dictionary<long, Material> fadedMats = new Dictionary<long, Material>();

        public void Init(LevelData lvl)
        {
            level = lvl;
            theme = string.IsNullOrEmpty(lvl.theme) ? "Forest" : lvl.theme;
            look = TerrainStyle.Look(lvl);
            bgTheme = TerrainStyle.BackgroundTheme(theme);
            rnd = new System.Random((lvl.name ?? "level").GetHashCode());
            cref = lvl.size * 0.5f;
            TerrainStyle.SkyColors(look, out skyTop, out skyBottom);
            silhouette = TerrainStyle.SilhouetteColor(look);
            gradient = Resources.Load<Texture2D>("Textures/Sky/" + bgTheme + "_Gradient");
            if (gradient != null) gradient.wrapMode = TextureWrapMode.Clamp;
            FindEnvModels();
            BuildSky();

            // the level's own layers; far (high pan) first, and among equal pans the later layer behind (the original
            // drew its layer list last to first)
            var ordered = new List<LevelData.ParallaxLayerData>(lvl.parallaxLayers);
            var index = new Dictionary<LevelData.ParallaxLayerData, int>();
            for (int i = 0; i < ordered.Count; i++) index[ordered[i]] = i;
            ordered.Sort((a, b) => a.cameraXPan != b.cameraXPan ? b.cameraXPan.CompareTo(a.cameraXPan) : index[b].CompareTo(index[a]));
            bool originals = false;
            foreach (var pl in ordered)
                foreach (var e in pl.exports)
                    if (ParallaxTexture(pl, e, out _) != null) originals = true;
            int rank = 0;
            if (!originals)
            {
                // no original art for this level: procedural clouds and ridges behind the layers
                AddCloudBank(0.965f, rank++);
                AddRidge(0.93f, 0.78f, rank++, 0);
                AddRidge(0.82f, 0.62f, rank++, 1);
                AddRidge(0.68f, 0.47f, rank++, 2);
            }
            else if (look == "IceCave") AddCaveBack(0.9f, rank++);
            foreach (var pl in ordered) AddLayer(pl, rank++);
            if (look == "IceCave") AddCaveCeiling(0.75f, rank++);
            if (ordered.Count == 0)
            {
                // levels without layers still get some near decoration
                AddLayer(new LevelData.ParallaxLayerData { id = "auto_sky", position = new Vector2(lvl.size.x * 0.3f, lvl.size.y * 0.85f), cameraXPan = 0.97f, cameraYPan = 0.97f, gap = 18, tileHorizontally = true, exports = { "cloud" } }, rank++);
                AddLayer(new LevelData.ParallaxLayerData { id = "auto_near", position = new Vector2(0, lvl.waterY - 1), cameraXPan = 0.45f, cameraYPan = 0.8f, gap = 9, tileHorizontally = true, exports = { "a", "b" } }, rank++);
            }
            ParallaxUpdate(FindCamera());
        }

        // ------------------------------------------------------------------ update

        void LateUpdate()
        {
            if (!SelfUpdate) return;
            if (cam == null || !cam.isActiveAndEnabled) cam = FindCamera();
            ParallaxUpdate(cam);
        }

        static Camera[] camBuffer = new Camera[8];

        /// <summary>Camera.main, or else the enabled camera with the highest depth (the battle camera is untagged).</summary>
        static Camera FindCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            if (camBuffer.Length < Camera.allCamerasCount) camBuffer = new Camera[Camera.allCamerasCount + 4];
            int n = Camera.GetAllCameras(camBuffer);
            Camera best = null;
            for (int i = 0; i < n; i++)
            {
                var c = camBuffer[i];
                if (c != null && c.isActiveAndEnabled && c.targetTexture == null && (c.cullingMask & 1) != 0 && (best == null || c.depth > best.depth)) best = c;
                camBuffer[i] = null;
            }
            return best;
        }

        /// <summary>Position the sky and parallax layers for a camera (perspective or orthographic).</summary>
        public void ParallaxUpdate(Camera c)
        {
            if (c == null) return;
            cam = c;
            Apply(c.transform.position, c.orthographic, c.fieldOfView, c.orthographicSize, c.aspect, c.farClipPlane);
        }

        /// <summary>Position everything for a camera at cameraPosition looking down +Z (perspective, 40° fov assumed).</summary>
        public void ParallaxUpdate(Vector3 cameraPosition) => Apply(cameraPosition, false, 40f, 10f, 16f / 9f, 1000f);

        void Apply(Vector3 cp, bool ortho, float fov, float orthoSize, float aspect, float far)
        {
            float D = Mathf.Max(0.5f, -cp.z);   // distance from the camera to the z = 0 playfield
            Vector2 C = cp;
            foreach (var L in layers)
            {
                if (L.root == null) continue;
                float s = ortho ? 1f : (D + L.z) / D;
                // apparent position A = local + pan * (C - cref); world = C + (A - C) * s
                float rx = C.x + (L.panX * (C.x - cref.x) - C.x) * s;
                float ry = C.y + (L.panY * (C.y - cref.y) - C.y) * s;
                L.root.localPosition = new Vector3(rx, ry, L.z);
                L.root.localScale = new Vector3(s, s, s);
            }
            if (sky != null)
            {
                float zs = Mathf.Min(90f, cp.z + far * 0.95f);
                float dist = zs - cp.z;
                float hgt = ortho ? orthoSize * 2f : 2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                sky.localPosition = new Vector3(cp.x, cp.y, zs);
                sky.localScale = new Vector3(hgt * aspect * 1.3f, hgt * 1.3f, 1);
                if (gradient != null && skyMat != null && level != null)
                {
                    // the original stretched its gradient over the whole level: map the quad's span at the playfield onto
                    // the level height (v = 0 at the bottom of the level, 1 at its top), with a little parallax
                    float view = ortho ? orthoSize * 2f : 2f * D * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                    float lh = Mathf.Max(1f, level.size.y);
                    float cy = Mathf.Lerp(cref.y, cp.y, 0.6f);
                    float span = view * 1.3f * 0.8f;
                    skyMat.mainTextureScale = new Vector2(1f, span / lh);
                    skyMat.mainTextureOffset = new Vector2(0f, (cy - span * 0.5f) / lh);
                }
                // keep the glow round although the sky quad is stretched by the aspect ratio
                if (sunGlow != null) sunGlow.localScale = new Vector3(0.55f / Mathf.Max(0.3f, aspect), 0.55f, 1);
            }
        }

        void OnDestroy()
        {
            foreach (var o in owned) if (o) Destroy(o);
        }

        // ------------------------------------------------------------------ sky

        void BuildSky()
        {
            var go = new GameObject("Sky");
            go.transform.SetParent(transform, false);
            sky = go.transform;
            var m = new Mesh { name = "Sky" };
            owned.Add(m);
            // five rows: a warm glow just below the middle (horizon), then a deeper zenith
            float[] ys = { -0.5f, -0.22f, -0.05f, 0.18f, 0.5f };
            var horizon = Color.Lerp(skyBottom, HorizonGlow(), 0.45f);
            Color[] cs =
            {
                Color.Lerp(skyBottom, skyTop, 0.12f), horizon, Color.Lerp(skyBottom, skyTop, 0.3f),
                Color.Lerp(skyBottom, skyTop, 0.68f), Color.Lerp(skyTop, Color.black, 0.08f)
            };
            if (gradient != null)
            {
                // original gradient texture; styles tint it (a redder, darker volcano sky, a dim ice cave)
                var tint = look == "Volcano" ? new Color(1f, 0.82f, 0.76f) : look == "IceCave" ? new Color(0.5f, 0.64f, 0.8f) : Color.white;
                for (int i = 0; i < cs.Length; i++) cs[i] = tint;
            }
            var verts = new Vector3[ys.Length * 2];
            var cols = new Color[ys.Length * 2];
            var uvs = new Vector2[ys.Length * 2];
            var tris = new int[(ys.Length - 1) * 6];
            for (int r = 0; r < ys.Length; r++)
            {
                verts[r * 2] = new Vector3(-0.5f, ys[r], 0); verts[r * 2 + 1] = new Vector3(0.5f, ys[r], 0);
                cols[r * 2] = cols[r * 2 + 1] = cs[r];
                uvs[r * 2] = new Vector2(0, ys[r] + 0.5f); uvs[r * 2 + 1] = new Vector2(1, ys[r] + 0.5f);
                if (r > 0)
                {
                    int b = (r - 1) * 2, t = (r - 1) * 6;
                    tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                    tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
                }
            }
            m.vertices = verts;
            m.colors = cols;
            m.uv = uvs;
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, new Vector3(1, 1, 1) * 10000f);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Mats.TransparentShader) { color = Color.white, name = "Sky", renderQueue = TerrainStyle.QueueSky };
            if (gradient != null) mat.mainTexture = gradient;
            skyMat = mat;
            owned.Add(mat);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // the original backgrounds have their light painted in; only the volcano gets an extra glow
            if (gradient == null || look == "Volcano") BuildSunGlow();
        }

        Color HorizonGlow()
        {
            switch (look == "Volcano" ? "Mountain" : look == "IceCave" ? "Winter" : theme)
            {
                case "Mountain": return new Color(1f, 0.45f, 0.2f);
                case "Desert": return new Color(1f, 0.93f, 0.7f);
                case "Winter": return new Color(1f, 0.96f, 0.92f);
                default: return new Color(1f, 0.97f, 0.85f);
            }
        }

        /// <summary>Soft additive sun (or volcano) glow, a child of the sky quad.</summary>
        void BuildSunGlow()
        {
            var go = new GameObject("SunGlow");
            go.transform.SetParent(sky, false);
            bool volcano = theme == "Mountain" || look == "Volcano";
            go.transform.localPosition = volcano ? new Vector3(0.12f, -0.2f, -0.001f) : new Vector3(0.24f, 0.2f, -0.001f);
            sunGlow = go.transform;
            var m = new Mesh { name = "SunGlow" };
            owned.Add(m);
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            var c = volcano ? new Color(1f, 0.35f, 0.1f, 0.55f) : theme == "Desert" ? new Color(1f, 0.95f, 0.7f, 0.55f) : new Color(1f, 0.97f, 0.85f, 0.42f);
            var mat = new Material(Mats.AdditiveShader) { mainTexture = Mats.SoftCircle, color = c, name = "SunGlow", renderQueue = TerrainStyle.QueueSky + 1 };
            owned.Add(mat);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ------------------------------------------------------------------ layers

        Layer NewLayer(string name, float panX, float panY, int rank, out MeshBuilder mb)
        {
            var go = new GameObject("Parallax_" + name);
            go.transform.SetParent(transform, false);
            var L = new Layer { root = go.transform, panX = Mathf.Clamp01(panX), panY = Mathf.Clamp01(panY), z = 4f + Mathf.Clamp01(panX) * 30f };
            layers.Add(L);
            mb = new MeshBuilder();
            return L;
        }

        void Finish(Layer L, MeshBuilder mb, int rank)
        {
            if (mb.Empty) return;
            var go = new GameObject("Silhouettes");
            go.transform.SetParent(L.root, false);
            var mesh = mb.ToMesh("Parallax");
            owned.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Mats.TransparentShader) { color = Color.white, name = "Parallax", renderQueue = TerrainStyle.QueueParallax + rank * 2 };
            owned.Add(mat);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        float Fade(float pan) => Mathf.Clamp01((pan - 0.3f) * 1.15f) * 0.8f;
        Color Faded(Color c, float pan) { var r = Color.Lerp(c, skyBottom, Fade(pan)); r.a = c.a; return r; }
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        /// <summary>A continuous procedural hill/mountain/dune ridge across the level.</summary>
        void AddRidge(float pan, float topFrac, int rank, int idx)
        {
            var L = NewLayer("Ridge" + idx, pan, Mathf.Lerp(pan, 1f, 0.4f), rank, out var mb);
            var lvl = level;
            float xMin = lvl.cameraBounds.xMin - 70, xMax = lvl.cameraBounds.xMax + 70;
            float baseY = Mathf.Min(lvl.waterY, 0) - 30f;
            float topY = Mathf.Lerp(lvl.waterY, lvl.size.y, topFrac * 0.85f);
            float amp = (topY - lvl.waterY) * 0.35f;
            Color body = Faded(Color.Lerp(silhouette, Color.black, 0.15f * idx), pan);
            Color top = theme == "Winter" ? Faded(new Color(0.93f, 0.97f, 1f), pan) : theme == "Desert" ? Faded(new Color(0.92f, 0.68f, 0.45f), pan) : Faded(Color.Lerp(silhouette, Color.white, 0.18f), pan);
            float phase = Rand(0, 100);
            float step = theme == "Mountain" ? 3.5f : 1.5f;
            Vector2 prev = Vector2.zero;
            bool first = true;
            // facet lighting: slopes rising to the right face the light (top-left) and get lighter
            Color bodyLit = Faded(Color.Lerp(silhouette, Color.white, 0.22f), pan), bodyDark = Faded(Color.Lerp(silhouette, Color.black, 0.3f + 0.1f * idx), pan);
            Color topLit = Color.Lerp(top, Color.white, 0.35f), topDark = Color.Lerp(top, bodyDark, 0.5f);
            var profile = new List<Vector2>();
            for (float x = xMin; x <= xMax + step; x += step)
            {
                float y;
                switch (theme)
                {
                    case "Mountain":
                        y = topY - amp * 0.5f + Rand(-amp, amp) * 0.9f;
                        break;
                    case "Desert":
                        float mesa = Mathf.Sin((x + phase) * 0.045f);
                        y = topY - amp + amp * 0.5f * Mathf.Sin((x + phase) * 0.11f) + (mesa > 0.55f ? amp * 0.8f : 0);
                        break;
                    default:
                        y = topY - amp * 0.6f + amp * 0.6f * Mathf.Sin((x + phase) * 0.07f) + amp * 0.3f * Mathf.Sin((x + phase * 2) * 0.19f);
                        break;
                }
                var cur = new Vector2(x, y);
                profile.Add(cur);
                if (!first)
                {
                    float lit = Mathf.Clamp((cur.y - prev.y) / step * 0.45f, -1f, 1f);
                    Color b = lit > 0 ? Color.Lerp(body, bodyLit, lit * 0.6f) : Color.Lerp(body, bodyDark, -lit * 0.6f);
                    Color tp = lit > 0 ? Color.Lerp(top, topLit, lit) : Color.Lerp(top, topDark, -lit * 0.7f);
                    // column quad from the base up to the profile (darker at the foot), with a lighter band on top
                    float band = theme == "Mountain" ? 1.4f : theme == "Winter" ? 1.6f : 0.8f;
                    Color foot = Color.Lerp(b, bodyDark, 0.35f);
                    mb.Quad(new Vector2(prev.x, baseY), new Vector2(cur.x, baseY), new Vector2(cur.x, cur.y - band), new Vector2(prev.x, prev.y - band), foot, foot, b, b);
                    mb.Quad(new Vector2(prev.x, prev.y - band), new Vector2(cur.x, cur.y - band), cur, prev, b, b, tp, tp);
                }
                prev = cur;
                first = false;
            }
            RidgeFeatures(mb, profile, pan, idx, body, topY, amp);
            // mist at the ridge's feet (depth between the layers)
            float mist0 = lvl.waterY - 6f, mist1 = Mathf.Lerp(lvl.waterY, topY - amp, 0.55f);
            var mistC = skyBottom; mistC.a = 0.5f; var mistT = skyBottom; mistT.a = 0f;
            mb.Quad(new Vector2(xMin, mist0), new Vector2(xMax, mist0), new Vector2(xMax, mist1), new Vector2(xMin, mist1), mistC, mistC, mistT, mistT);
            Finish(L, mb, rank);
        }

        static float ProfileY(List<Vector2> pr, float x)
        {
            if (pr.Count == 0) return 0;
            if (x <= pr[0].x) return pr[0].y;
            for (int i = 1; i < pr.Count; i++)
                if (x <= pr[i].x) return Mathf.Lerp(pr[i - 1].y, pr[i].y, (x - pr[i - 1].x) / Mathf.Max(1e-4f, pr[i].x - pr[i - 1].x));
            return pr[pr.Count - 1].y;
        }

        /// <summary>Theme details along a ridge profile: tree lines, snow caps, mesa strata, volcano glow.</summary>
        void RidgeFeatures(MeshBuilder mb, List<Vector2> pr, float pan, int idx, Color body, float topY, float amp)
        {
            if (pr.Count < 2) return;
            float x0 = pr[0].x, x1 = pr[pr.Count - 1].x;
            switch (theme)
            {
                case "Forest":
                {
                    if (idx == 0) break;
                    // tree line: dark canopy, lighter lit side, trunks for the nearest ridge
                    var dark = Color.Lerp(body, Color.black, 0.12f);
                    var lit = Faded(Color.Lerp(silhouette, new Color(0.65f, 0.85f, 0.35f), 0.35f), pan);
                    var trunk = Faded(new Color(0.3f, 0.2f, 0.14f), pan);
                    for (float x = x0; x < x1; x += Rand(1.1f, 2.2f) * (idx == 1 ? 1.4f : 1f))
                    {
                        float y = ProfileY(pr, x);
                        float r = Rand(0.8f, 1.6f) * (idx == 1 ? 0.8f : 1f);
                        if (idx == 2) mb.Rect(new Vector2(x - r * 0.12f, y - 0.5f), new Vector2(x + r * 0.12f, y + r * 0.6f), trunk, trunk);
                        bool conifer = Rand(0, 1) < 0.35f;
                        if (conifer)
                        {
                            mb.Tri(new Vector2(x - r * 0.8f, y + r * 0.2f), new Vector2(x + r * 0.8f, y + r * 0.2f), new Vector2(x, y + r * 2.6f), dark);
                            mb.Tri(new Vector2(x - r * 0.8f, y + r * 0.2f), new Vector2(x, y + r * 0.2f), new Vector2(x, y + r * 2.6f), Color.Lerp(dark, lit, 0.45f));
                        }
                        else
                        {
                            mb.Circle(new Vector2(x, y + r * 0.9f), r, dark, 12);
                            mb.Circle(new Vector2(x - r * 0.25f, y + r * 1.15f), r * 0.62f, Color.Lerp(dark, lit, 0.55f), 10);
                        }
                    }
                    break;
                }
                case "Winter":
                {
                    // snow caps on the high points and sparse snowy pines on the nearer ridges
                    var snow = Faded(new Color(0.97f, 0.99f, 1f), pan);
                    var snowShade = Faded(new Color(0.8f, 0.88f, 0.98f), pan);
                    for (int i = 1; i < pr.Count - 1; i++)
                    {
                        if (pr[i].y < pr[i - 1].y || pr[i].y < pr[i + 1].y) continue;
                        float w = Rand(1.2f, 2.4f);
                        mb.Tri(new Vector2(pr[i].x - w, pr[i].y - w * 0.55f), new Vector2(pr[i].x + w, pr[i].y - w * 0.55f), pr[i] + new Vector2(0, 0.05f), snow);
                        mb.Tri(new Vector2(pr[i].x, pr[i].y - w * 0.55f), new Vector2(pr[i].x + w, pr[i].y - w * 0.55f), pr[i] + new Vector2(0, 0.05f), snowShade);
                    }
                    if (idx >= 1)
                        for (float x = x0; x < x1; x += Rand(2.5f, 6f))
                            Pine(mb, new Vector2(x, ProfileY(pr, x) - 0.3f), pan, Rand(2.5f, 4.5f) * (idx == 1 ? 0.8f : 1f), true);
                    break;
                }
                case "Desert":
                {
                    // horizontal strata on the mesas: lighter and darker bands just under the top
                    var light = Faded(new Color(0.95f, 0.72f, 0.5f), pan);
                    var dark = Faded(new Color(0.66f, 0.38f, 0.24f), pan);
                    float baseY = Mathf.Min(level.waterY, 0) - 30f;
                    for (int k = 1; k <= 3; k++)
                    {
                        float off = k * 1.1f + 0.3f;
                        var c = k % 2 == 0 ? light : dark;
                        c.a = 0.55f;
                        var c2 = c; c2.a = 0f;
                        for (int i = 1; i < pr.Count; i++)
                        {
                            Vector2 a = pr[i - 1] - new Vector2(0, off), b = pr[i] - new Vector2(0, off);
                            if (a.y < baseY || b.y < baseY) continue;
                            mb.Quad(a - new Vector2(0, 0.35f), b - new Vector2(0, 0.35f), b, a, c2, c2, c, c);
                        }
                    }
                    if (idx == 2)
                        for (float x = x0; x < x1; x += Rand(4f, 9f))
                            Cactus(mb, new Vector2(x, ProfileY(pr, x) - 0.2f), pan, Rand(2f, 3.4f));
                    break;
                }
                case "Mountain":
                {
                    // glowing lava seams on the far ridge, ember-lit edges on the nearer ones
                    var glow = Faded(new Color(1f, 0.45f, 0.12f), pan * 0.6f);
                    for (int i = 1; i < pr.Count - 1; i++)
                    {
                        if (pr[i].y < pr[i - 1].y || pr[i].y < pr[i + 1].y) continue;
                        if (idx == 0 && Rand(0, 1) < 0.5f)
                        {
                            var g2 = glow; g2.a = 0f;
                            mb.Quad(pr[i] + new Vector2(-0.4f, 0), pr[i] + new Vector2(0.4f, 0), pr[i] + new Vector2(1.2f, -amp * 0.8f), pr[i] + new Vector2(-0.9f, -amp * 0.8f), glow, glow, g2, g2);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>A far cloud bank (smoke over the volcano) across the level: puffy two-tone clouds.</summary>
        void AddCloudBank(float pan, int rank)
        {
            var L = NewLayer("Clouds", pan, pan, rank, out var mb);
            bool smoke = theme == "Mountain";
            Color c = smoke ? new Color(0.36f, 0.26f, 0.3f, 0.7f) : new Color(1f, 1f, 1f, 0.9f);
            Color shade = smoke ? new Color(0.24f, 0.17f, 0.2f, 0.7f) : Color.Lerp(skyBottom, new Color(0.78f, 0.85f, 0.96f), 0.6f);
            shade.a = c.a;
            Color hl = smoke ? new Color(0.55f, 0.32f, 0.28f, 0.6f) : new Color(1f, 1f, 1f, 1f);
            float xMin = level.cameraBounds.xMin - 60, xMax = level.cameraBounds.xMax + 60;
            float y0 = Mathf.Lerp(level.waterY, level.size.y, 0.62f);
            for (float x = xMin; x < xMax; x += Rand(14f, 26f))
            {
                float w = Rand(6f, 11f);
                var p = new Vector2(x, y0 + Rand(-2f, 6f));
                mb.Rect(new Vector2(p.x - w * 0.5f, p.y - 0.7f), new Vector2(p.x + w * 0.5f, p.y + 0.2f), shade, shade);
                int n = 4 + (int)Rand(0, 3);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    float r = Rand(1.0f, 1.8f) * (1f - Mathf.Abs(t - 0.5f) * 0.7f);
                    var cpos = new Vector2(p.x - w * 0.5f + w * t, p.y + r * 0.35f);
                    mb.Circle(cpos + new Vector2(0.12f, -0.18f), r, shade, 14);
                    mb.Circle(cpos, r * 0.94f, c, 14);
                    mb.Circle(cpos + new Vector2(-r * 0.3f, r * 0.3f), r * 0.45f, hl, 10);
                }
            }
            Finish(L, mb, rank);
        }

        /// <summary>A layer from the level file: items every `gap` along x, tiled across the level if asked.</summary>
        void AddLayer(LevelData.ParallaxLayerData pl, int rank)
        {
            if (AddOriginalLayer(pl, rank)) return;
            var L = NewLayer(pl.id, pl.cameraXPan, pl.cameraYPan, rank, out var mb);
            float pan = pl.cameraXPan;
            bool skyLayer = pan >= 0.95f;
            bool nearLayer = pan < 0.55f;
            var models = skyLayer ? envSky : nearLayer ? envNear : envFar;
            if (models.Count == 0 && !skyLayer) models = nearLayer ? envFar : envNear;

            var xs = new List<float>();
            float gap = Mathf.Max(pl.gap, 0f);
            int count = Mathf.Max(1, pl.exports.Count);
            if (pl.tileHorizontally && gap > 0.5f)
            {
                float xMin = level.cameraBounds.xMin - 40, xMax = level.cameraBounds.xMax + 40;
                float start = pl.position.x - Mathf.Ceil((pl.position.x - xMin) / gap) * gap;
                for (float x = start; x <= xMax; x += gap) xs.Add(x);
            }
            else for (int k = 0; k < count; k++) xs.Add(pl.position.x + k * gap);

            for (int k = 0; k < xs.Count; k++)
            {
                string export = pl.exports.Count > 0 ? pl.exports[k % pl.exports.Count] : "item";
                var basePos = new Vector2(xs[k] + Rand(-0.15f, 0.15f) * gap, pl.position.y);
                int hash = (export.GetHashCode() & 0x7fffffff) + k;
                if (models.Count > 0)
                {
                    string model = models[(export.GetHashCode() & 0x7fffffff) % models.Count];
                    float hgt = skyLayer ? Rand(2.5f, 4.5f) : nearLayer ? Rand(5f, 9f) : Rand(7f, 13f);
                    if (skyLayer) basePos.y -= hgt;   // sky layer anchors are near the top
                    PlaceModel(model, L.root, basePos, hgt, pan, rank);
                }
                else ProceduralItem(mb, basePos, pan, skyLayer, nearLayer, hash);
            }
            Finish(L, mb, rank);
        }

        /// <summary>Original art of a layer graphic (Textures/Parallax/{Theme}/{export}) and its size in world units.</summary>
        Texture2D ParallaxTexture(LevelData.ParallaxLayerData pl, string export, out Vector2 size)
        {
            size = Vector2.zero;
            if (string.IsNullOrEmpty(export)) return null;
            string folder = bgTheme;
            // "level_graphics/level_bg_forest.swf" names the art set
            if (!string.IsNullOrEmpty(pl.swf))
            {
                string s = pl.swf.ToLowerInvariant();
                if (s.Contains("winter")) folder = "Winter";
                else if (s.Contains("mountain")) folder = "Mountain";
                else if (s.Contains("desert")) folder = "Desert";
                else if (s.Contains("forest")) folder = "Forest";
            }
            string key = "Parallax/" + folder + "/" + export;
            if (!TerrainStyle.OriginalArt(key, out var px, out _)) return null;
            if (!parallaxTex.TryGetValue(key, out var t) || t == null)
            {
                t = Resources.Load<Texture2D>("Textures/" + key);
                if (t != null) t.wrapMode = TextureWrapMode.Clamp;
                parallaxTex[key] = t;
            }
            size = px / 20f;
            return t;
        }

        /// <summary>
        /// A level layer drawn with the original sprites, placed like ParallaxLayer.as: bottom-center registration at
        /// (x + i * gap, y), scaled by zoom, graphic i = exports[i % count], repeated across the level when tiled.
        /// One mesh (one draw call) per graphic of the layer. False when none of its graphics exist as original art.
        /// </summary>
        bool AddOriginalLayer(LevelData.ParallaxLayerData pl, int rank)
        {
            int n = pl.exports.Count;
            if (n == 0) return false;
            var tex = new Texture2D[n];
            var size = new Vector2[n];
            bool any = false;
            for (int i = 0; i < n; i++) { tex[i] = ParallaxTexture(pl, pl.exports[i], out size[i]); any |= tex[i] != null; }
            if (!any) return false;
            var L = NewLayer(pl.id, pl.cameraXPan, pl.cameraYPan, rank, out _);
            float zoom = pl.zoom > 0.01f ? pl.zoom : 1f;
            var batches = new Dictionary<Texture2D, SpriteBatch>();
            float gap = Mathf.Max(pl.gap, 0f);
            void Put(int i, float x)
            {
                int e = ((i % n) + n) % n;
                if (tex[e] == null) return;
                if (!batches.TryGetValue(tex[e], out var b)) batches[tex[e]] = b = new SpriteBatch();
                var s = size[e] * zoom;
                b.Add(new Vector2(x - s.x * 0.5f, pl.position.y), new Vector2(x + s.x * 0.5f, pl.position.y + s.y));
            }
            if (pl.tileHorizontally && gap > 0.5f)
            {
                // cover everything the camera can see; the layer also slides by (1 - pan) of the camera travel
                float slack = (1f - Mathf.Clamp01(pl.cameraXPan)) * level.cameraBounds.width * 0.5f + 30f;
                float xMin = level.cameraBounds.xMin - slack, xMax = level.cameraBounds.xMax + slack;
                int i0 = Mathf.FloorToInt((xMin - pl.position.x) / gap), i1 = Mathf.CeilToInt((xMax - pl.position.x) / gap);
                for (int i = i0; i <= i1; i++) Put(i, pl.position.x + i * gap);
            }
            else for (int i = 0; i < n; i++) Put(i, pl.position.x + i * gap);
            foreach (var kv in batches)
            {
                var go = new GameObject("Original_" + kv.Key.name);
                go.transform.SetParent(L.root, false);
                var mesh = kv.Value.ToMesh(kv.Key.name);
                owned.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var tint = look == "IceCave" ? Color.Lerp(Color.white, new Color(0.55f, 0.7f, 0.9f), Mathf.Clamp01(pl.cameraXPan)) :
                           look == "Volcano" ? Color.Lerp(Color.white, new Color(1f, 0.72f, 0.62f), Mathf.Clamp01(pl.cameraXPan) * 0.8f) : Color.white;
                var mat = new Material(Mats.TransparentShader) { mainTexture = kv.Key, color = tint, name = "Parallax_" + kv.Key.name, renderQueue = TerrainStyle.QueueParallax + rank * 2 };
                owned.Add(mat);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            return true;
        }

        /// <summary>Dark ice wall behind an ice cave's layers: rows of big faceted blocks.</summary>
        void AddCaveBack(float pan, int rank)
        {
            var L = NewLayer("CaveBack", pan, pan, rank, out var mb);
            float xMin = level.cameraBounds.xMin - 60, xMax = level.cameraBounds.xMax + 60;
            float y0 = level.waterY - 8f, y1 = level.size.y + 20f;
            Color a = new Color(0.16f, 0.30f, 0.46f, 0.55f), b = new Color(0.30f, 0.50f, 0.68f, 0.45f);
            for (float y = y0; y < y1; y += 5f)
            {
                float off = Rand(0f, 6f);
                for (float x = xMin - off; x < xMax; x += Rand(5f, 9f))
                {
                    float w = Rand(4.5f, 8.5f), h = Rand(4f, 5.5f);
                    var c = Color.Lerp(a, b, Rand(0f, 1f));
                    var hl = Color.Lerp(c, new Color(0.7f, 0.88f, 1f, 0.5f), 0.4f);
                    mb.Quad(new Vector2(x, y), new Vector2(x + w, y + 0.3f), new Vector2(x + w - 0.4f, y + h), new Vector2(x + 0.3f, y + h - 0.2f), c, c, hl, hl);
                }
            }
            Finish(L, mb, rank);
        }

        /// <summary>Icicle ceiling of an ice cave: a dark ice band along the level top with hanging icicles.</summary>
        void AddCaveCeiling(float pan, int rank)
        {
            var L = NewLayer("CaveCeiling", pan, Mathf.Lerp(pan, 1f, 0.5f), rank, out var mb);
            float xMin = level.cameraBounds.xMin - 60, xMax = level.cameraBounds.xMax + 60;
            float top = level.size.y + 30f, y = level.size.y + 1.5f;
            Color body = new Color(0.20f, 0.36f, 0.55f), rim = new Color(0.55f, 0.80f, 0.95f), tip = new Color(0.85f, 0.96f, 1f, 0.9f);
            mb.Rect(new Vector2(xMin, y), new Vector2(xMax, top), body, Color.Lerp(body, Color.black, 0.3f));
            for (float x = xMin; x < xMax; x += Rand(1.2f, 3.2f))
            {
                float w = Rand(0.8f, 2.2f), h = Rand(1.5f, 6f);
                mb.Tri(new Vector2(x, y + 0.2f), new Vector2(x + w * 0.5f, y - h), new Vector2(x + w, y + 0.2f), rim);
                mb.Tri(new Vector2(x + w * 0.2f, y), new Vector2(x + w * 0.5f, y - h * 0.92f), new Vector2(x + w * 0.45f, y), tip);
            }
            mb.Rect(new Vector2(xMin, y - 0.3f), new Vector2(xMax, y + 0.5f), rim, body);
            Finish(L, mb, rank);
        }

        /// <summary>Textured quads (uv 0..1 each, white vertex colors) for the original layer sprites.</summary>
        class SpriteBatch
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> c = new List<Color>();
            readonly List<int> t = new List<int>();

            public void Add(Vector2 min, Vector2 max)
            {
                int i = v.Count;
                v.Add(new Vector3(min.x, min.y, 0)); v.Add(new Vector3(max.x, min.y, 0));
                v.Add(new Vector3(max.x, max.y, 0)); v.Add(new Vector3(min.x, max.y, 0));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) c.Add(Color.white);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetUVs(0, uv);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                var b = m.bounds; b.Expand(new Vector3(400, 200, 10)); m.bounds = b;
                return m;
            }
        }

        void PlaceModel(string path, Transform parent, Vector2 basePos, float height, float pan, int rank)
        {
            // holder so the model keeps its own import rotation/scale
            var holder = new GameObject(path).transform;
            holder.SetParent(parent, false);
            holder.localRotation = Quaternion.Euler(0, Rand(-25f, 25f), 0);
            ModelLibrary.Spawn(path, holder, PrimitiveType.Cube, 1f, silhouette);
            var go = holder.gameObject;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float s = height / Mathf.Max(0.05f, b.size.y);
            go.transform.localScale = Vector3.one * s;
            // bounds were measured with the parent at its current transform; convert to local
            var lc = parent.InverseTransformPoint(b.center);
            var lmin = parent.InverseTransformPoint(b.min);
            go.transform.localPosition = new Vector3(basePos.x - lc.x * s, basePos.y - lmin.y * s, -lc.z * s);
            foreach (var r in rs)
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = FadedMaterial(src[i], pan, rank);
                r.sharedMaterials = dst;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// <summary>Copy of a model material faded toward the sky color and drawn in this layer's render queue.</summary>
        Material FadedMaterial(Material src, float pan, int rank)
        {
            if (src == null) return null;
            long key = ((long)src.GetInstanceID() << 16) ^ (rank * 977L) ^ (long)(pan * 1000);
            if (fadedMats.TryGetValue(key, out var m) && m) return m;
            m = new Material(src);
            // far layers: drop the outline pass (one draw per material); near layers keep a sky-faded ink line
            if (pan > 0.6f && m.shader == Mats.ToonOutlineShader) m.shader = Mats.ToonShader;
            m.renderQueue = TerrainStyle.QueueParallax + rank * 2 + 1;
            if (m.HasProperty("_Color")) m.color = Faded(m.color, pan);
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(1, 1, 1, 0.15f));
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", Faded(m.GetColor("_OutlineColor"), pan));
            if (m.HasProperty("_Gloss")) m.SetColor("_Gloss", new Color(1, 1, 1, 0.12f));
            fadedMats[key] = m;
            return m;
        }

        void ProceduralItem(MeshBuilder mb, Vector2 p, float pan, bool skyLayer, bool near, int hash)
        {
            float k = near ? 1f : 1.5f;
            if (skyLayer)
            {
                // cloud (or smoke on Mountain)
                Color c = theme == "Mountain" ? new Color(0.35f, 0.25f, 0.28f, 0.75f) : new Color(1f, 1f, 1f, 0.92f);
                Color shade = theme == "Mountain" ? new Color(0.28f, 0.2f, 0.22f, 0.75f) : new Color(0.86f, 0.92f, 0.98f, 0.92f);
                float w = Rand(4f, 8f);
                p.y -= Rand(2f, 4f);
                mb.Rect(new Vector2(p.x - w * 0.5f, p.y - 0.6f), new Vector2(p.x + w * 0.5f, p.y + 0.3f), shade, shade);
                int n = 3 + hash % 3;
                for (int i = 0; i < n; i++)
                {
                    float t = n == 1 ? 0.5f : i / (float)(n - 1);
                    float r = Rand(0.9f, 1.6f) * (1f - Mathf.Abs(t - 0.5f) * 0.6f);
                    mb.Circle(new Vector2(p.x - w * 0.5f + w * t, p.y + r * 0.4f), r, i % 2 == 0 ? c : shade, 14);
                }
                return;
            }
            switch (theme)
            {
                case "Winter":
                    if (near && hash % 4 == 0) Snowman(mb, p, pan, Rand(2.2f, 3.2f));
                    else Pine(mb, p, pan, Rand(4f, 7f) * k, true);
                    break;
                case "Mountain":
                    if (near && hash % 3 == 0) Spire(mb, p, pan, Rand(4f, 8f));
                    else Peak(mb, p, pan, Rand(6f, 11f) * k, hash % 5 == 0);
                    break;
                case "Desert":
                    if (near || hash % 2 == 0) Cactus(mb, p, pan, Rand(3f, 5.5f) * (near ? 1f : 1.2f));
                    else Mesa(mb, p, pan, Rand(6f, 10f) * k);
                    break;
                default:
                    if (hash % 3 == 0) Pine(mb, p, pan, Rand(4.5f, 7.5f) * k, false);
                    else RoundTree(mb, p, pan, Rand(4f, 7f) * k);
                    break;
            }
        }

        // ------------------------------------------------------------------ procedural shapes

        void RoundTree(MeshBuilder mb, Vector2 p, float pan, float h)
        {
            var trunk = Faded(new Color(0.36f, 0.23f, 0.15f), pan);
            var leaf = Faded(new Color(0.22f, 0.52f, 0.24f), pan);
            var leaf2 = Faded(new Color(0.30f, 0.62f, 0.28f), pan);
            float tw = h * 0.09f;
            mb.Rect(new Vector2(p.x - tw, p.y), new Vector2(p.x + tw, p.y + h * 0.55f), trunk, trunk);
            float r = h * 0.3f;
            var lit = Faded(new Color(0.48f, 0.75f, 0.32f), pan);
            mb.Circle(new Vector2(p.x - r * 0.55f, p.y + h * 0.6f), r * 0.8f, leaf, 14);
            mb.Circle(new Vector2(p.x + r * 0.55f, p.y + h * 0.62f), r * 0.75f, leaf, 14);
            mb.Circle(new Vector2(p.x, p.y + h * 0.75f), r, leaf2, 16);
            mb.Circle(new Vector2(p.x - r * 0.3f, p.y + h * 0.82f), r * 0.55f, lit, 12);
            mb.Circle(new Vector2(p.x - r * 0.75f, p.y + h * 0.64f), r * 0.35f, lit, 10);
        }

        void Pine(MeshBuilder mb, Vector2 p, float pan, float h, bool snowy)
        {
            var trunk = Faded(new Color(0.33f, 0.22f, 0.15f), pan);
            var green = Faded(snowy ? new Color(0.18f, 0.40f, 0.36f) : new Color(0.14f, 0.40f, 0.22f), pan);
            var snow = Faded(new Color(0.94f, 0.97f, 1f), pan);
            float tw = h * 0.06f;
            mb.Rect(new Vector2(p.x - tw, p.y), new Vector2(p.x + tw, p.y + h * 0.25f), trunk, trunk);
            for (int i = 0; i < 3; i++)
            {
                float y0 = p.y + h * (0.18f + i * 0.24f), w = h * (0.34f - i * 0.08f), th = h * 0.36f;
                mb.Tri(new Vector2(p.x - w, y0), new Vector2(p.x + w, y0), new Vector2(p.x, y0 + th), green);
                mb.Tri(new Vector2(p.x - w, y0), new Vector2(p.x - w * 0.1f, y0), new Vector2(p.x, y0 + th), Color.Lerp(green, Color.white, 0.18f));
                if (snowy) mb.Tri(new Vector2(p.x - w * 0.35f, y0 + th * 0.65f), new Vector2(p.x + w * 0.35f, y0 + th * 0.65f), new Vector2(p.x, y0 + th), snow);
            }
        }

        void Snowman(MeshBuilder mb, Vector2 p, float pan, float h)
        {
            var white = Faded(new Color(0.96f, 0.98f, 1f), pan);
            var shade = Faded(new Color(0.82f, 0.9f, 0.98f), pan);
            var dark = Faded(new Color(0.15f, 0.15f, 0.18f), pan);
            var carrot = Faded(new Color(1f, 0.55f, 0.15f), pan);
            float r1 = h * 0.22f, r2 = h * 0.16f, r3 = h * 0.11f;
            mb.Circle(new Vector2(p.x, p.y + r1), r1, shade, 16);
            mb.Circle(new Vector2(p.x, p.y + r1 * 2 + r2 * 0.8f), r2, white, 14);
            Vector2 head = new Vector2(p.x, p.y + r1 * 2 + r2 * 1.6f + r3 * 0.8f);
            mb.Circle(head, r3, white, 12);
            mb.Rect(new Vector2(head.x - r3 * 0.8f, head.y + r3 * 0.7f), new Vector2(head.x + r3 * 0.8f, head.y + r3 * 1.6f), dark, dark);
            mb.Tri(new Vector2(head.x, head.y + r3 * 0.1f), new Vector2(head.x, head.y - r3 * 0.2f), new Vector2(head.x + r3 * 1.1f, head.y - r3 * 0.05f), carrot);
        }

        void Peak(MeshBuilder mb, Vector2 p, float pan, float h, bool volcano)
        {
            var rock = Faded(new Color(0.40f, 0.26f, 0.25f), pan);
            var light = Faded(new Color(0.55f, 0.38f, 0.33f), pan);
            float w = h * Rand(0.55f, 0.8f);
            float tipX = p.x + Rand(-0.15f, 0.15f) * w;
            if (volcano)
            {
                var glow = Faded(new Color(1f, 0.5f, 0.12f), pan * 0.5f);
                mb.Quad(new Vector2(p.x - w, p.y), new Vector2(p.x + w, p.y), new Vector2(tipX + w * 0.15f, p.y + h), new Vector2(tipX - w * 0.15f, p.y + h), rock, rock, light, light);
                mb.Tri(new Vector2(tipX - w * 0.12f, p.y + h), new Vector2(tipX + w * 0.12f, p.y + h), new Vector2(tipX, p.y + h * 0.8f), glow);
            }
            else
            {
                mb.Tri(new Vector2(p.x - w, p.y), new Vector2(p.x + w, p.y), new Vector2(tipX, p.y + h), rock);
                mb.Tri(new Vector2(tipX, p.y + h), new Vector2(p.x + w, p.y), new Vector2(Mathf.Lerp(tipX, p.x + w, 0.35f), p.y + h * 0.3f), light);
            }
        }

        void Spire(MeshBuilder mb, Vector2 p, float pan, float h)
        {
            var rock = Faded(new Color(0.34f, 0.22f, 0.22f), pan);
            float w = h * 0.22f;
            mb.Quad(new Vector2(p.x - w, p.y), new Vector2(p.x + w, p.y), new Vector2(p.x + w * 0.35f, p.y + h), new Vector2(p.x - w * 0.5f, p.y + h * 0.9f), rock, rock, rock, rock);
        }

        void Cactus(MeshBuilder mb, Vector2 p, float pan, float h)
        {
            var g = Faded(new Color(0.30f, 0.56f, 0.30f), pan);
            var g2 = Faded(new Color(0.24f, 0.46f, 0.25f), pan);
            float w = h * 0.1f;
            mb.Rect(new Vector2(p.x - w, p.y), new Vector2(p.x + w, p.y + h), g, g);
            mb.Circle(new Vector2(p.x, p.y + h), w, g, 10);
            // arms
            float ay = p.y + h * 0.4f, ah = h * 0.35f;
            mb.Rect(new Vector2(p.x - w * 3.2f, ay), new Vector2(p.x - w, ay + w * 1.4f), g2, g2);
            mb.Rect(new Vector2(p.x - w * 3.2f, ay), new Vector2(p.x - w * 1.6f, ay + ah), g2, g2);
            mb.Circle(new Vector2(p.x - w * 2.4f, ay + ah), w * 0.8f, g2, 8);
            ay = p.y + h * 0.55f;
            mb.Rect(new Vector2(p.x + w, ay), new Vector2(p.x + w * 3f, ay + w * 1.4f), g2, g2);
            mb.Rect(new Vector2(p.x + w * 1.6f, ay), new Vector2(p.x + w * 3f, ay + ah * 0.8f), g2, g2);
            mb.Circle(new Vector2(p.x + w * 2.3f, ay + ah * 0.8f), w * 0.7f, g2, 8);
        }

        void Mesa(MeshBuilder mb, Vector2 p, float pan, float h)
        {
            var side = Faded(new Color(0.75f, 0.45f, 0.28f), pan);
            var top = Faded(new Color(0.88f, 0.60f, 0.38f), pan);
            float w = h * 1.2f;
            mb.Quad(new Vector2(p.x - w, p.y), new Vector2(p.x + w, p.y), new Vector2(p.x + w * 0.6f, p.y + h), new Vector2(p.x - w * 0.55f, p.y + h), side, side, top, top);
        }

        // ------------------------------------------------------------------ environment models

        void FindEnvModels()
        {
            GameObject[] all;
            try { all = Resources.LoadAll<GameObject>("Models/Env"); }
            catch { all = new GameObject[0]; }
            string prefix = theme + "_";
            foreach (var go in all)
            {
                if (go == null || !go.name.StartsWith(prefix)) continue;
                string path = "Env/" + go.name;
                string n = go.name.Substring(prefix.Length).ToLowerInvariant();
                if (n.Contains("cloud") || n.Contains("sun") || n.Contains("moon") || n.Contains("smoke") || n.Contains("bird")) envSky.Add(path);
                else if (n.Contains("peak") || n.Contains("mountain") || n.Contains("hill") || n.Contains("mesa") || n.Contains("dune") ||
                         n.Contains("volcano") || n.Contains("glacier") || n.Contains("range") || n.Contains("cliff") || n.Contains("berg") ||
                         n.Contains("pyramid") || n.Contains("castle")) envFar.Add(path);
                else envNear.Add(path);
            }
            envNear.Sort(); envFar.Sort(); envSky.Sort();
        }

        // ------------------------------------------------------------------ mesh building

        /// <summary>Tiny vertex-colored 2D mesh builder (z = 0) for silhouettes.</summary>
        class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Color> c = new List<Color>();
            readonly List<int> t = new List<int>();
            public bool Empty => v.Count == 0;

            public void Tri(Vector2 a, Vector2 b, Vector2 d, Color col)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                c.Add(col); c.Add(col); c.Add(col);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Quad(Vector2 a, Vector2 b, Vector2 d, Vector2 e, Color ca, Color cb, Color cd, Color ce)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d); v.Add(e);
                c.Add(ca); c.Add(cb); c.Add(cd); c.Add(ce);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            public void Rect(Vector2 min, Vector2 max, Color bottom, Color top) =>
                Quad(min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y), bottom, bottom, top, top);

            public void Circle(Vector2 center, float r, Color col, int seg)
            {
                int i = v.Count;
                v.Add(center); c.Add(col);
                for (int k = 0; k <= seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2;
                    v.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    c.Add(col);
                    if (k > 0) { t.Add(i); t.Add(i + k); t.Add(i + k + 1); }
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                // layers move/scale with the camera; never cull them by their nominal bounds
                var b = m.bounds; b.Expand(new Vector3(400, 200, 10)); m.bounds = b;
                return m;
            }
        }
    }
}
