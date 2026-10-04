using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Real textures for the penguin and his wearables (Blender/scripts/wear_kit.py, penguin_skins.py).
    ///
    /// The FBX files carry no texture references: a textured Blender material is named "T_{texture}__{tint}" and its
    /// diffuse color is the tint, so after ModelLibrary.Spawn (Mats.ApplyToon keeps that name and color) this swaps
    /// each such material for a copy with the texture on _MainTex (multiplied by the tint in CPW/Toon) and the
    /// generic detail noise off. UV0 of these models is the real texture layout.
    ///   T_penguin__white -> Textures/Penguin/{skin} (one 1024 atlas per skin; "classic" by default)
    ///   T_{name}__{tint} -> Textures/Clothes/{name} (256 tiles: knit, plaid, denim, leather, fur, metal, camo...)
    /// Missing textures leave the flat toon material in place.
    /// </summary>
    public static class WearTextures
    {
        public const string DefaultSkin = "classic";
        const string Prefix = "T_";

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly int DetailId = Shader.PropertyToID("_Detail");
        static readonly List<Material> tmp = new List<Material>();

        /// <summary>Texture of a skin (texture name under Textures/Penguin, "" = classic), or null.</summary>
        public static Texture2D Skin(string skin) => Load("Textures/Penguin/" + (string.IsNullOrEmpty(skin) ? DefaultSkin : skin));

        static Texture2D Load(string path)
        {
            if (textures.TryGetValue(path, out var t)) return t;
            t = Resources.Load<Texture2D>(path);
            if (t != null)
            {
                t.wrapMode = TextureWrapMode.Repeat;
                t.anisoLevel = Mathf.Max(t.anisoLevel, 2);
            }
            textures[path] = t;
            return t;
        }

        /// <summary>"T_knit__red (Instance)" -> "knit"; null when the material is not a textured one.</summary>
        public static string TextureName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName) || !materialName.StartsWith(Prefix, System.StringComparison.Ordinal)) return null;
            string n = Mats.BaseName(materialName);
            int end = n.IndexOf("__", Prefix.Length, System.StringComparison.Ordinal);
            return end > Prefix.Length ? n.Substring(Prefix.Length, end - Prefix.Length) : n.Substring(Prefix.Length);
        }

        /// <summary>Put the textures on every textured material under go. skin = penguin skin texture name ("" = classic).</summary>
        public static void Apply(GameObject go, string skin = null)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is SpriteRenderer || r is TrailRenderer || r is LineRenderer) continue;
                r.GetSharedMaterials(tmp);
                bool changed = false;
                for (int i = 0; i < tmp.Count; i++)
                {
                    var m = tmp[i];
                    if (m == null) continue;
                    var src = m;
                    // a material we made earlier: rebuild from its source (skin swaps)
                    if (Origin.TryGetValue(m, out var orig)) src = orig;
                    string tex = TextureName(src.name);
                    if (tex == null) continue;
                    var t = tex == "penguin" ? (Skin(skin) ?? Skin(DefaultSkin)) : Load("Textures/Clothes/" + tex);
                    if (t == null) continue;
                    var k = src.GetInstanceID() + "|" + t.GetInstanceID();
                    if (!materials.TryGetValue(k, out var made) || made == null)
                    {
                        made = new Material(src) { name = src.name, mainTexture = t };
                        if (made.HasProperty(DetailId)) made.SetFloat(DetailId, 0f);
                        materials[k] = made;
                        Origin[made] = src;
                    }
                    if (made != m) { tmp[i] = made; changed = true; }
                }
                if (changed) r.sharedMaterials = tmp.ToArray();
            }
        }

        static readonly Dictionary<Material, Material> Origin = new Dictionary<Material, Material>();
    }
}
