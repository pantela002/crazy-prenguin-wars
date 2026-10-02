using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// The home screen's igloo. With the original art: the original igloo interior (home_screen background_main) on a
    /// quad in the MenuCamera's world just behind the penguin (so the 3D/sprite penguin stands in front of it and
    /// MenuScene3D's own backdrop is hidden), its round window framed around the penguin's head, with the original
    /// character spotlight (Character_Frame) under its feet. Without it: a full-screen light-blue ice-brick interior
    /// painted at runtime with an arched doorway cut out where the penguin stands, plus a snowy podium. Either way the
    /// penguin's feet/head are projected through GameManager.MenuCamera, so the layout keeps working wherever
    /// MenuScene3D places it; nothing is rebuilt unless the screen size, safe area or the penguin's position changes.
    /// </summary>
    public class HomeStage
    {
        /// <summary>Where the penguin is drawn when there is no 3D scene (and the layout we design for).</summary>
        public static readonly Vector2 DefaultFeet = new Vector2(0.5f, 0.40f);
        public const float DefaultHeadVp = 0.76f;

        // ---- results, in the screen's Root rect (pixels from its bottom-left) ----
        public Vector2 FeetPx { get; private set; }
        public float HeadPx { get; private set; }
        public float HalfWidthPx { get; private set; }
        public Vector2 RootSize { get; private set; }
        public float PodiumHeight { get; private set; }
        /// <summary>Half the doorway width including its ice rim (widgets beside the penguin start outside it).</summary>
        public float DoorHalfPx => HalfWidthPx * 1.45f * 1.2f + 10;

        readonly RectTransform root;
        readonly Image backdrop, podium, podiumRim;
        Texture2D tex;
        Sprite sprite;
        PenguinAvatar avatar;
        float findCooldown;
        float feetDepth = 10f;
        readonly Sprite wallArt, spotArt;
        GameObject wall, spot;
        Vector2 feetVp, lastFeetVp = new Vector2(-1, -1);
        float headVp, halfVp, lastHeadVp, lastHalfVp;
        int lastW, lastH;
        Rect lastSafe;

        public HomeStage(RectTransform root)
        {
            this.root = root;
            backdrop = UI.Image(root, null, Color.white, false, "Igloo");
            podiumRim = UI.Image(root, UI.Circle, new Color32(120, 176, 226, 255), false, "PodiumRim");
            podium = UI.Image(root, UI.Circle, new Color32(236, 248, 255, 255), false, "Podium");
            var shine = UI.Image(podium.transform, UI.Circle, new Color(1, 1, 1, 0.7f), false, "Shine");
            UI.Anchor(shine.rectTransform, 0.18f, 0.45f, 0.82f, 0.9f);
            wallArt = UI.Skin.Bitmap("home_screen", 1);    // background_main (igloo, round window)
            spotArt = UI.Skin.Bitmap("home_screen", 4);    // Character_Frame spotlight + floor ring
            if (wallArt != null)
            {
                // the wall lives in the 3D world behind the penguin; the UI must not cover the penguin
                backdrop.enabled = false;
                podium.enabled = false; podiumRim.enabled = false; shine.enabled = false;
            }
        }

        /// <summary>True when the original igloo art is drawn behind the penguin.</summary>
        public bool OriginalArt => wallArt != null;

        /// <summary>Re-project the penguin; returns true when the layout changed (caller re-places its widgets).</summary>
        public bool Update(float dt)
        {
            Locate(dt);
            var safe = Screen.safeArea;
            var size = root.rect.size;
            bool changed = Screen.width != lastW || Screen.height != lastH || safe != lastSafe || (size - RootSize).sqrMagnitude > 1f ||
                           (feetVp - lastFeetVp).sqrMagnitude > 0.00004f || Mathf.Abs(headVp - lastHeadVp) > 0.006f || Mathf.Abs(halfVp - lastHalfVp) > 0.004f;
            if (!changed || size.x < 10 || size.y < 10 || Screen.width <= 0 || Screen.height <= 0) return false;
            lastW = Screen.width; lastH = Screen.height; lastSafe = safe;
            lastFeetVp = feetVp; lastHeadVp = headVp; lastHalfVp = halfVp;
            RootSize = size;
            Layout();
            return true;
        }

        /// <summary>Penguin feet/head/half-width in viewport units (0..1 of the whole screen).</summary>
        void Locate(float dt)
        {
            var cam = GameManager.I != null ? GameManager.I.MenuCamera : null;
            if (avatar == null && cam != null)
            {
                findCooldown -= dt;
                if (findCooldown <= 0)
                {
                    findCooldown = 0.1f;
                    var scene = GameObject.Find("MenuScene3D");
                    if (scene != null) avatar = scene.GetComponentInChildren<PenguinAvatar>();
                }
            }
            if (avatar == null || cam == null || !avatar.gameObject.activeInHierarchy)
            {
                feetVp = DefaultFeet; headVp = DefaultHeadVp;
                halfVp = 0.105f * Screen.height / Mathf.Max(1f, Screen.width);
                return;
            }
            // the penguin's origin is at its feet (MenuScene3D stands it on the snow); the pivot spins it, so use world up
            // MenuScene3D places the avatar's parent (its spin pivot); the avatar itself bobs and jumps inside it
            var t = avatar.transform;
            var feet = t.parent != null ? t.parent.position : t.position;
            float h = avatar.Height * Mathf.Max(0.01f, t.lossyScale.y);
            var f = cam.WorldToViewportPoint(feet);
            var top = cam.WorldToViewportPoint(feet + Vector3.up * h);
            var side = cam.WorldToViewportPoint(feet + Vector3.up * h * 0.5f + cam.transform.right * h * 0.42f);
            var mid = cam.WorldToViewportPoint(feet + Vector3.up * h * 0.5f);
            if (f.z <= 0) { feetVp = DefaultFeet; headVp = DefaultHeadVp; halfVp = 0.1f; return; }
            feetVp = new Vector2(f.x, f.y);
            headVp = top.y;
            halfVp = Mathf.Abs(side.x - mid.x);
            feetDepth = f.z;
        }

        void Layout()
        {
            // viewport (whole screen) -> Root (safe area) conversion
            var sMin = UI.Safe != null ? UI.Safe.anchorMin : Vector2.zero;
            var sMax = UI.Safe != null ? UI.Safe.anchorMax : Vector2.one;
            var sSize = new Vector2(Mathf.Max(0.01f, sMax.x - sMin.x), Mathf.Max(0.01f, sMax.y - sMin.y));
            Vector2 ToRoot(Vector2 vp) => new Vector2((vp.x - sMin.x) / sSize.x * RootSize.x, (vp.y - sMin.y) / sSize.y * RootSize.y);

            FeetPx = ToRoot(feetVp);
            HeadPx = ToRoot(new Vector2(feetVp.x, headVp)).y;
            HalfWidthPx = halfVp / sSize.x * RootSize.x;

            // the backdrop covers the whole screen (also behind notches)
            var brt = backdrop.rectTransform;
            brt.anchorMin = new Vector2(-sMin.x / sSize.x, -sMin.y / sSize.y);
            brt.anchorMax = new Vector2((1 - sMin.x) / sSize.x, (1 - sMin.y) / sSize.y);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            if (wallArt != null) PlaceWall();
            else Paint();

            // snowy podium in front of the feet
            float pw = Mathf.Max(HalfWidthPx * 2.9f + 40, 160), ph = pw * 0.2f;
            PodiumHeight = ph;
            Place(podiumRim.rectTransform, new Vector2(FeetPx.x, FeetPx.y - ph * 0.22f), new Vector2(pw + 16, ph + 14));
            Place(podium.rectTransform, new Vector2(FeetPx.x, FeetPx.y - ph * 0.12f), new Vector2(pw, ph));
        }

        // ------------------------------------------------------------------ original igloo (3D quads)

        /// <summary>Where the round window sits in background_main (0..1 from the bottom-left) and its share of the width.</summary>
        static readonly Vector2 WindowUv = new Vector2(0.515f, 0.73f);
        const float WallGap = 0.7f;    // world units behind the penguin's feet (in front of MenuScene3D's props)

        void PlaceWall()
        {
            var cam = GameManager.I != null ? GameManager.I.MenuCamera : null;
            if (cam == null) return;
            float depth = Mathf.Max(cam.nearClipPlane + 1f, feetDepth) + WallGap;
            float hh = depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), hw = hh * Mathf.Max(0.1f, cam.aspect);
            float aspect = wallArt.rect.width / Mathf.Max(1f, wallArt.rect.height);
            // cover the view (a bit larger so the window can move toward the penguin), then frame the window
            float h = Mathf.Max(2 * hh, 2 * hw / aspect) * 1.12f, w = h * aspect;
            // target: the window centred over the penguin's chest/head
            var target = new Vector2((feetVp.x - 0.5f) * 2 * hw, (Mathf.Lerp(feetVp.y, headVp, 0.72f) - 0.5f) * 2 * hh);
            float cx = target.x - (WindowUv.x - 0.5f) * w, cy = target.y - (WindowUv.y - 0.5f) * h;
            cx = Mathf.Clamp(cx, -(w / 2 - hw), w / 2 - hw);
            cy = Mathf.Clamp(cy, -(h / 2 - hh), h / 2 - hh);
            if (wall == null) wall = Quad("HomeIglooWall", wallArt, Mats.UnlitTex(wallArt.texture, Color.white), cam);
            wall.transform.localPosition = new Vector3(cx, cy, depth);
            wall.transform.localScale = new Vector3(w, h, 1);

            // the spotlight: ring under the feet, cone up behind the penguin, a hair in front of the wall
            if (spotArt == null) return;
            if (spot == null) spot = Quad("HomeSpotlight", spotArt, Mats.TransparentTex(spotArt.texture, new Color(1, 1, 1, 0.9f)), cam);
            float sd = depth - 0.05f, k = sd / depth;
            float penguinH = Mathf.Max(0.2f, (headVp - feetVp.y) * 2 * hh * k);
            float sh = penguinH * 1.45f, sw = sh * spotArt.rect.width / Mathf.Max(1f, spotArt.rect.height);
            // the ring is at ~88% down the bitmap: put it at the feet
            spot.transform.localPosition = new Vector3(target.x * k, (feetVp.y - 0.5f) * 2 * hh * k + sh * (0.5f - 0.12f), sd);
            spot.transform.localScale = new Vector3(sw, sh, 1);
        }

        /// <summary>A unit quad facing the camera, child of it, showing a sprite's texture rect.</summary>
        static GameObject Quad(string name, Sprite art, Material mat, Camera cam)
        {
            var go = new GameObject(name);
            go.transform.SetParent(cam.transform, false);
            var r = art.textureRect;
            float tw = Mathf.Max(1, art.texture.width), th = Mathf.Max(1, art.texture.height);
            float u0 = r.xMin / tw, u1 = r.xMax / tw, v0 = r.yMin / th, v1 = r.yMax / th;
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        static void Place(RectTransform rt, Vector2 centerPx, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = centerPx;
        }

        // ------------------------------------------------------------------ painting

        static readonly Color WallTop = new Color32(118, 184, 240, 255), WallBottom = new Color32(200, 230, 252, 255);
        static readonly Color FloorCol = new Color32(222, 241, 253, 255), RimIn = new Color32(246, 252, 255, 255), RimOut = new Color32(158, 208, 246, 255);

        void Paint()
        {
            int w = 640;
            int h = Mathf.Clamp(Mathf.RoundToInt(w * (float)Screen.height / Mathf.Max(1, Screen.width)), 240, 900);
            if (tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null) Object.Destroy(tex);
                if (sprite != null) Object.Destroy(sprite);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HomeIgloo" };
                sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                backdrop.sprite = sprite;
            }
            // doorway in texture pixels: from just above the podium to a bit over the head (room for the cheer jump)
            float cx = feetVp.x * w, r = Mathf.Max(halfVp * w * 1.45f, 24);
            float yb = (feetVp.y - 0.01f) * h, yt = Mathf.Max(headVp + 0.07f, feetVp.y + 0.2f) * h;
            float cy = Mathf.Max(yb, yt - r);
            float rim = Mathf.Max(10, r * 0.2f);
            float floorY = feetVp.y * h;
            float brick = h * 0.1f;
            var domeC = new Vector2(cx, floorY - h * 0.2f);

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float ty = y / (float)(h - 1);
                Color wall = Color.Lerp(WallBottom, WallTop, ty * ty);
                for (int x = 0; x < w; x++)
                {
                    // signed distance to the arch (box below cy + circle on top), negative inside the doorway
                    float dx = Mathf.Abs(x - cx);
                    float boxD = Mathf.Max(dx - r, Mathf.Max(yb - y, y - cy));
                    float circD = Mathf.Sqrt(dx * dx + (y - cy) * (y - cy)) - r;
                    float sd = y > cy ? circD : boxD;
                    if (y < yb) sd = Mathf.Max(sd, rim + (yb - y));   // open at the bottom: the podium covers the sill
                    Color c;
                    if (sd < rim)
                    {
                        // ice blocks framing the doorway
                        float k = Mathf.Clamp01(sd / rim);
                        c = Color.Lerp(RimIn, RimOut, k * k);
                        float along = y > cy ? Mathf.Atan2(y - cy, x - cx) * r / (rim * 1.3f) : (y - yb) / (rim * 1.3f);
                        float fj = along - Mathf.Floor(along);
                        if (fj < 0.06f || k > 0.9f) c = Color.Lerp(c, RimOut * 0.85f, 0.6f);
                        c.a = Mathf.Clamp01(sd + 0.5f);
                    }
                    else if (y < floorY - 4)
                    {
                        // floor with faint rink rings around the podium
                        float ex = (x - cx) / w, ey = (y - floorY) / h * 2.6f;
                        float e = Mathf.Sqrt(ex * ex + ey * ey) / 0.11f;
                        float fe = e - Mathf.Floor(e);
                        c = Color.Lerp(FloorCol, WallBottom, Mathf.Clamp01(e * 0.12f));
                        if (fe < 0.04f) c = Color.Lerp(c, RimOut, 0.25f);
                    }
                    else
                    {
                        // igloo wall: rings of ice bricks around a dome centre below the floor
                        float ddx = x - domeC.x, ddy = y - domeC.y;
                        float rr = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                        float ring = rr / brick;
                        int ri = (int)ring;
                        float fr = ring - ri;
                        float ang = Mathf.Atan2(ddy, ddx) * rr / (brick * 1.9f) + (ri & 1) * 0.5f;
                        int ci = Mathf.FloorToInt(ang);
                        float fa = ang - ci;
                        uint hsh = (uint)(ri * 73856093 ^ ci * 19349663);
                        float jitter = ((hsh % 1000) / 1000f - 0.5f) * 0.06f;
                        c = wall * (1 + jitter);
                        if (fr < 0.05f || fa < 0.025f) c = Color.Lerp(c, RimOut * 0.82f, 0.55f);
                        else if (fr > 0.9f) c = Color.Lerp(c, Color.white, 0.25f);
                        if (y < floorY + 4) c = Color.Lerp(c, FloorCol, (floorY + 4 - y) / 8f);
                        // soft light around the doorway
                        float glow = Mathf.Clamp01(1 - (sd - rim) / (r * 1.6f));
                        c = Color.Lerp(c, Color.white, glow * 0.22f);
                    }
                    // soft clouds in the bottom corners
                    float cl = Cloud(x / (float)w, ty, h / (float)w);
                    if (cl > 0 && sd >= rim) c = Color.Lerp(c, Color.white, cl);
                    c.a = sd < rim ? c.a : 1f;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        static readonly Vector3[] Puffs =
        {
            new Vector3(0.02f, 0.10f, 0.09f), new Vector3(0.09f, 0.06f, 0.07f), new Vector3(0.15f, 0.03f, 0.06f),
            new Vector3(0.98f, 0.12f, 0.09f), new Vector3(0.91f, 0.06f, 0.07f), new Vector3(0.85f, 0.02f, 0.06f),
            new Vector3(-0.01f, 0.34f, 0.07f), new Vector3(1.01f, 0.38f, 0.07f)
        };

        /// <summary>0..0.9 whiteness of the cloud puffs at (u, v) (v scaled by aspect so puffs stay round).</summary>
        static float Cloud(float u, float v, float aspect)
        {
            if (v > 0.5f) return 0;
            float best = 0;
            foreach (var p in Puffs)
            {
                float du = u - p.x, dv = (v - p.y) * aspect;
                float d = Mathf.Sqrt(du * du + dv * dv) / p.z;
                if (d < 1) best = Mathf.Max(best, Mathf.Clamp01((1 - d) * 4f) * 0.9f);
            }
            return best;
        }

        /// <summary>Free the generated texture (screen closed).</summary>
        public void Dispose()
        {
            if (sprite != null) Object.Destroy(sprite);
            if (tex != null) Object.Destroy(tex);
            sprite = null; tex = null;
            foreach (var go in new[] { wall, spot })
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
                Object.Destroy(go);
            }
            wall = spot = null;
        }
    }
}
