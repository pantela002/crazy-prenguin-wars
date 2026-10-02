using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Material helpers. All shaders live in Resources/Shaders so they are always included in builds.</summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Shader toon, toonOutline, unlit, unlitT, additive;
        static Texture2D softCircle, whiteTex, detailTex;

        public static Shader ToonShader => toon ? toon : (toon = Shader.Find("CPW/Toon") ?? Shader.Find("Unlit/Color"));
        /// <summary>CPW/Toon with the cartoon ink outline pass (falls back to the plain toon shader).</summary>
        public static Shader ToonOutlineShader => toonOutline ? toonOutline : (toonOutline = Shader.Find("CPW/ToonOutline") ?? ToonShader);
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
        /// Call after instantiating an imported Blender FBX. Models get the ink outline (CPW/ToonOutline), baked AO
        /// (vertex colors) and the painted detail noise; Blender materials named "Glow..." (fire, lasers, plasma)
        /// are drawn flat and bright without outline.
        /// </summary>
        public static void ApplyToon(GameObject go) => ApplyToon(go, true);

        /// <summary>As <see cref="ApplyToon(GameObject)"/>; outline false keeps the plain toon pass (one draw per material).</summary>
        public static void ApplyToon(GameObject go, bool outline)
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
                    bool glow = false;
                    if (s != null)
                    {
                        if (s.HasProperty("_Color")) c = s.color;
                        else if (s.HasProperty("_BaseColor")) c = s.GetColor("_BaseColor");
                        if (s.HasProperty("_MainTex")) t = s.mainTexture;
                        glow = s.name.StartsWith("Glow", System.StringComparison.Ordinal);
                    }
                    bool ol = outline && !glow;
                    var key = "toonfbx" + c + (t ? t.GetInstanceID().ToString() : "") + (glow ? "g" : ol ? "o" : "");
                    if (!cache.TryGetValue(key, out var m) || !m)
                    {
                        m = new Material(ol ? ToonOutlineShader : ToonShader) { color = c, name = s != null ? s.name : "toon" };
                        if (t) m.mainTexture = t;
                        if (glow)
                        {
                            // emissive look: no shadow side, no AO, a hot rim
                            m.SetColor("_ShadowColor", new Color(0.92f, 0.92f, 0.92f, 1));
                            m.SetFloat("_AO", 0f);
                            m.SetColor("_RimColor", new Color(1, 1, 0.85f, 0.6f));
                            m.SetFloat("_Detail", 0f);
                        }
                        else if (m.HasProperty("_DetailTex"))
                        {
                            var fam = DetailFamily(s != null ? s.name : "");
                            var dt = fam != null ? DetailTexture(fam) : null;
                            if (dt != null)
                            {
                                // surface texture by material: wood grain, stone specks/cracks, brushed metal, ice streaks, cloth weave
                                m.SetTexture("_DetailTex", dt);
                                m.SetTextureScale("_DetailTex", fam == "fabric" ? new Vector2(1.2f, 1.2f) : new Vector2(0.45f, 0.45f));
                                m.SetFloat("_Detail", fam == "wood" ? 0.3f : fam == "stone" ? 0.28f : fam == "metal" ? 0.2f : fam == "ice" ? 0.22f : 0.12f);
                            }
                            else
                            {
                                m.SetTexture("_DetailTex", DetailNoise);
                                m.SetTextureScale("_DetailTex", new Vector2(0.9f, 0.9f));
                                // near-black materials: softer specks (they would read as dirt)
                                if (c.grayscale < 0.25f) m.SetFloat("_Detail", 0.07f);
                            }
                        }
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

        static readonly Dictionary<string, Texture2D> detailFamilies = new Dictionary<string, Texture2D>();

        /// <summary>Detail texture family for a Blender material name (wood/stone/metal/ice/fabric) or null.</summary>
        public static string DetailFamily(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return null;
            string n = materialName.ToLowerInvariant();
            if (n.StartsWith("wood") || n.StartsWith("bark") || n.StartsWith("brown") || n.StartsWith("choco")) return "wood";
            if (n.StartsWith("stone") || n.StartsWith("rock") || n.StartsWith("mesa") || n.StartsWith("mountain") || n.StartsWith("sand")) return "stone";
            if (n.StartsWith("metal") || n.StartsWith("steel") || n.StartsWith("gun") || n.StartsWith("silver") || n.StartsWith("grey")) return "metal";
            if (n.StartsWith("ice") || n.StartsWith("glass") || n.StartsWith("snow")) return "ice";
            if (n.StartsWith("canvas") || n.StartsWith("denim") || n.StartsWith("plaid") || n.StartsWith("khaki") || n.StartsWith("olive") || n.StartsWith("team")) return "fabric";
            return null;
        }

        /// <summary>Resources/Textures/Detail/{family}.png (Blender textures step); null when missing.</summary>
        public static Texture2D DetailTexture(string family)
        {
            if (detailFamilies.TryGetValue(family, out var t)) return t;
            t = Resources.Load<Texture2D>("Textures/Detail/" + family);
            if (t != null) t.wrapMode = TextureWrapMode.Repeat;
            detailFamilies[family] = t;
            return t;
        }

        /// <summary>
        /// Tileable 64x64 gray noise (0.5 = neutral) for the toon shader's painted surface variation: soft blotches
        /// plus fine specks. Sampled with the object-space planar UV0 the Blender export bakes.
        /// </summary>
        public static Texture2D DetailNoise
        {
            get
            {
                if (detailTex) return detailTex;
                const int n = 64;
                detailTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "CPW_DetailNoise" };
                var lattice = new float[8 * 8];
                var rnd = new System.Random(1234);
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // periodic value noise on an 8x8 lattice (smooth blotches) plus per-pixel specks
                    float fx = x / 8f, fy = y / 8f;
                    int x0 = (int)fx, y0 = (int)fy;
                    float tx = fx - x0, ty = fy - y0;
                    tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                    float a = lattice[(y0 & 7) * 8 + (x0 & 7)], b = lattice[(y0 & 7) * 8 + ((x0 + 1) & 7)];
                    float c0 = lattice[((y0 + 1) & 7) * 8 + (x0 & 7)], d = lattice[((y0 + 1) & 7) * 8 + ((x0 + 1) & 7)];
                    float blot = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c0, d, tx), ty);
                    float speck = (float)rnd.NextDouble();
                    float v = 0.5f + (blot - 0.5f) * 0.7f + (speck - 0.5f) * 0.3f;
                    byte g = (byte)Mathf.Clamp(v * 255f, 0, 255);
                    px[y * n + x] = new Color32(g, g, g, 255);
                }
                detailTex.SetPixels32(px);
                detailTex.Apply(true, true);
                return detailTex;
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
