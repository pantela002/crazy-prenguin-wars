using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Loads the Blender-made models from Resources/Models (FBX files exported by Blender/scripts).
    /// Every model has a primitive fallback so the game stays playable if a model is missing.
    /// Naming convention (see Docs/ART.md): Models/Penguin/Penguin, Models/Weapons/{WeaponGraphic id},
    /// Models/Missiles/{MissileGraphic id}, Models/Clothes/{Bonus id}, Models/Props/{LevelObject id}, Models/Env/{Theme}_{name}.
    /// </summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        public static GameObject Prefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (cache.TryGetValue(path, out var p)) return p;
            p = Resources.Load<GameObject>("Models/" + path);
            cache[path] = p;
            return p;
        }

        public static bool Exists(string path) => Prefab(path) != null;

        /// <summary>
        /// Instantiate a model with toon materials. If it is missing, builds the given primitive fallback
        /// (scaled to fallbackSize world units) in fallbackColor.
        /// </summary>
        public static GameObject Spawn(string path, Transform parent, PrimitiveType fallback = PrimitiveType.Sphere,
            float fallbackSize = 0.5f, Color? fallbackColor = null)
        {
            var prefab = Prefab(path);
            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab, parent, false);
                go.name = path;
                Mats.ApplyToon(go);
            }
            else
            {
                go = GameObject.CreatePrimitive(fallback);
                go.name = path + " (fallback)";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localScale = Vector3.one * fallbackSize;
                go.GetComponent<Renderer>().sharedMaterial = Mats.Toon(fallbackColor ?? Color.gray);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return go;
        }

        /// <summary>Load an icon PNG rendered by Blender from Resources/Icons/{path}; returns null if missing.</summary>
        public static Sprite Icon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var key = "icon:" + path;
            if (spriteCache.TryGetValue(key, out var s)) return s;
            var t = Resources.Load<Texture2D>("Icons/" + path);
            s = t ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f) : null;
            spriteCache[key] = s;
            return s;
        }

        /// <summary>Load any texture from Resources/Textures/{path} as a sprite.</summary>
        public static Sprite TextureSprite(string path)
        {
            var key = "tex:" + path;
            if (spriteCache.TryGetValue(key, out var s)) return s;
            var t = Resources.Load<Texture2D>("Textures/" + path);
            s = t ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f) : null;
            spriteCache[key] = s;
            return s;
        }

        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    }
}
