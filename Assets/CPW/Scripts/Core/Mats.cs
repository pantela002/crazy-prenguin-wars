using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Material helpers. All shaders live in Resources/Shaders so they are always included in builds.</summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Shader toon, unlit, unlitT, additive;
        static Texture2D softCircle, whiteTex;

        public static Shader ToonShader => toon ? toon : (toon = Shader.Find("CPW/Toon") ?? Shader.Find("Unlit/Color"));
        public static Shader UnlitShader => unlit ? unlit : (unlit = Shader.Find("CPW/Unlit") ?? Shader.Find("Unlit/Texture"));
        public static Shader TransparentShader => unlitT ? unlitT : (unlitT = Shader.Find("CPW/UnlitTransparent") ?? Shader.Find("Sprites/Default"));
        public static Shader AdditiveShader => additive ? additive : (additive = Shader.Find("CPW/Additive") ?? Shader.Find("Sprites/Default"));

        static Material Cached(string key, Shader sh, Color c, Texture tex = null)
        {
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = new Material(sh) { color = c };
            if (tex) m.mainTexture = tex;
            cache[key] = m;
            return m;
        }

        public static Material Toon(Color c) => Cached("toon" + c, ToonShader, c);
        public static Material Unlit(Color c) => Cached("unlit" + c, UnlitShader, c);
        public static Material Transparent(Color c) => Cached("tr" + c, TransparentShader, c);
        public static Material Additive(Color c) => Cached("add" + c, AdditiveShader, c);
        public static Material UnlitTex(Texture t, Color c) => Cached("ut" + t.GetInstanceID() + c, UnlitShader, c, t);
        public static Material TransparentTex(Texture t, Color c) => Cached("tt" + t.GetInstanceID() + c, TransparentShader, c, t);
        public static Material AdditiveTex(Texture t, Color c) => Cached("at" + t.GetInstanceID() + c, AdditiveShader, c, t);

        /// <summary>
        /// Swap every renderer under go to the toon shader, keeping each original material's color and texture.
        /// Call after instantiating an imported Blender FBX.
        /// </summary>
        public static void ApplyToon(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var s = src[i];
                    Color c = Color.white;
                    Texture t = null;
                    if (s != null)
                    {
                        if (s.HasProperty("_Color")) c = s.color;
                        else if (s.HasProperty("_BaseColor")) c = s.GetColor("_BaseColor");
                        if (s.HasProperty("_MainTex")) t = s.mainTexture;
                    }
                    var key = "toonfbx" + c + (t ? t.GetInstanceID().ToString() : "");
                    if (!cache.TryGetValue(key, out var m) || !m)
                    {
                        m = new Material(ToonShader) { color = c };
                        if (t) m.mainTexture = t;
                        cache[key] = m;
                    }
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        public static Texture2D White
        {
            get
            {
                if (whiteTex) return whiteTex;
                whiteTex = new Texture2D(2, 2);
                whiteTex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                whiteTex.Apply();
                return whiteTex;
            }
        }

        /// <summary>A soft round 64x64 texture used for particles, glows and shadows.</summary>
        public static Texture2D SoftCircle
        {
            get
            {
                if (softCircle) return softCircle;
                const int n = 64;
                softCircle = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - d);
                    px[y * n + x] = new Color(1, 1, 1, a * a);
                }
                softCircle.SetPixels(px);
                softCircle.Apply();
                return softCircle;
            }
        }
    }
}
