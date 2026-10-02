using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Paints a level object model (Models/Props/{Shape}{Size}{Material}) with the original game's sprite for it
    /// (Textures/Items/{Material}/{shape}_{size}_{stage}.png, see Blender/scripts/original_art.py): the sprite is
    /// projected along the view axis through the model's planar UV0 (Unity object-space x/y baked by the Blender
    /// export), so the camera sees the original 2D art on the front face and the sides carry its edge colors.
    /// Follows the object's damage: DynamicObjectEntity.DamageStage 1..3 selects the original _1/_2/_3 sprite.
    /// </summary>
    public class PropSkin : MonoBehaviour
    {
        static readonly string[] Shapes = { "Cube", "Ball", "Plank", "Rectangle", "Triangle" };
        static readonly string[] Sizes = { "Small", "Medium", "Large" };
        static readonly string[] Materials = { "Wood", "Stone", "Ice", "Metal" };
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        string key;                    // "Items/Wood/cube_large"
        Renderer[] renderers;
        Material[][] stageMats;        // per renderer: material for stage 1..3
        DynamicObjectEntity owner;
        bool searched;
        int shown;

        /// <summary>Texture key of a props model path ("Props/CubeLargeWood" -> "Items/Wood/cube_large"), or null.</summary>
        public static string ItemKey(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Props/")) return null;
            string id = path.Substring(6);
            foreach (var sh in Shapes)
            {
                if (!id.StartsWith(sh)) continue;
                foreach (var sz in Sizes)
                {
                    if (string.CompareOrdinal(id, sh.Length, sz, 0, sz.Length) != 0) continue;
                    string mat = id.Substring(sh.Length + sz.Length);
                    if (System.Array.IndexOf(Materials, mat) < 0) return null;
                    return "Items/" + mat + "/" + sh.ToLowerInvariant() + "_" + sz.ToLowerInvariant();
                }
            }
            return null;
        }

        /// <summary>Skin a freshly spawned props model when its original sprite exists. Returns the component or null.</summary>
        public static PropSkin Attach(GameObject model, string path)
        {
            var k = ItemKey(path);
            if (k == null || Texture(k, 1) == null) return null;
            var rs = model.GetComponentsInChildren<Renderer>(true);
            var mats = new List<Material[]>();
            var used = new List<Renderer>();
            foreach (var r in rs)
            {
                Mesh mesh = null;
                if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf) mesh = mf.sharedMesh; }
                else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var per = new Material[3];
                for (int s = 0; s < 3; s++) per[s] = StageMaterial(k, s + 1, mesh);
                mats.Add(per);
                used.Add(r);
            }
            if (used.Count == 0) return null;
            var skin = model.AddComponent<PropSkin>();
            skin.key = k;
            skin.renderers = used.ToArray();
            skin.stageMats = mats.ToArray();
            skin.Show(1);
            return skin;
        }

        static Texture2D Texture(string k, int stage)
        {
            string p = k + "_" + stage;
            if (textures.TryGetValue(p, out var t)) return t;
            t = TerrainStyle.OriginalArt(p, out _, out _) ? Resources.Load<Texture2D>("Textures/" + p) : null;
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            textures[p] = t;
            return t;
        }

        /// <summary>Toon material showing the sprite of a damage stage, its UV transform fitted to the mesh's x/y bounds.</summary>
        static Material StageMaterial(string k, int stage, Mesh mesh)
        {
            var tex = Texture(k, stage) ?? Texture(k, stage - 1) ?? Texture(k, 1);
            var b = mesh.bounds;
            string ck = k + "_" + stage + "/" + mesh.GetInstanceID();
            if (cache.TryGetValue(ck, out var m) && m) return m;
            m = new Material(Mats.ToonOutlineShader) { mainTexture = tex, color = Color.white, name = "Prop_" + k.Replace('/', '_') + "_" + stage };
            float sx = Mathf.Max(1e-4f, b.size.x), sy = Mathf.Max(1e-4f, b.size.y);
            m.mainTextureScale = new Vector2(1f / sx, 1f / sy);
            m.mainTextureOffset = new Vector2(-b.min.x / sx, -b.min.y / sy);
            // the sprite carries the painted light and ink: keep the toon light soft, no procedural detail
            if (m.HasProperty("_Detail")) m.SetFloat("_Detail", 0f);
            if (m.HasProperty("_AO")) m.SetFloat("_AO", 0.6f);
            if (m.HasProperty("_ShadowColor")) m.SetColor("_ShadowColor", new Color(0.78f, 0.8f, 0.88f, 1f));
            if (m.HasProperty("_Highlight")) m.SetFloat("_Highlight", 0.03f);
            if (m.HasProperty("_Gloss")) m.SetColor("_Gloss", new Color(1f, 1f, 1f, k.Contains("/Ice/") || k.Contains("/Metal/") ? 0.3f : 0.12f));
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.22f));
            cache[ck] = m;
            return m;
        }

        void Show(int stage)
        {
            shown = stage;
            int s = Mathf.Clamp(stage, 1, 3) - 1;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var arr = r.sharedMaterials;
                for (int j = 0; j < arr.Length; j++) arr[j] = stageMats[i][s];
                r.sharedMaterials = arr;
            }
        }

        void LateUpdate()
        {
            if (!searched)
            {
                searched = true;
                owner = GetComponentInParent<DynamicObjectEntity>();
            }
            if (owner == null) { enabled = false; return; }
            int st = owner.DamageStage;
            if (st != shown) Show(st);
        }
    }
}
