using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CPW.EditorTools
{
    /// <summary>
    /// Import settings for the original Flash art in Resources/Original/** (written by Tools/import_original.py;
    /// the repo ships no .meta files). Every PNG becomes a Single sprite whose pivot is the Flash registration point
    /// and whose pixelsPerUnit = 20 x zoom (the zoom it was rendered at), so 1 Unity unit stays 20 Flash px and all
    /// frames of a symbol line up. Pivot and zoom come from the folder's _meta.json "files" table
    /// ([px, py, zoom], pixels from the top-left, y down); unknown files get a centre pivot and 20 PPU.
    /// No mipmaps, bilinear, alpha is transparency, ASTC 6x6 on Android/iOS. Terrain, liquid and parallax bitmaps
    /// are full-rect sprites with Repeat wrap so they can tile. No Sprite Atlases: see Docs/ORIGINAL_ART.md.
    /// Menu: CPW/Reimport Original Art (after re-running the import script with Unity open).
    /// </summary>
    public class OriginalSpriteImporter : AssetPostprocessor
    {
        public const string Root = "Assets/CPW/Resources/Original/";

        public override uint GetVersion() => 1;

        // _meta.json path -> (last write time, files table)
        static readonly Dictionary<string, KeyValuePair<long, Dictionary<string, object>>> metas =
            new Dictionary<string, KeyValuePair<long, Dictionary<string, object>>>();

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) || !assetPath.EndsWith(".png")) return;
            var imp = (TextureImporter)assetImporter;
            string rel = assetPath.Substring(Root.Length);       // "weapons/weapon_animations/bazooka/001.png"
            bool tiling = rel.StartsWith("terrain/") || rel.StartsWith("liquids/") || rel.StartsWith("levels/");

            Vector2 pivotPx = new Vector2(float.NaN, float.NaN);
            float zoom = 1f;
            if (FindEntry(rel, out var entry) && entry.Count >= 2)
            {
                pivotPx = new Vector2(ToF(entry[0]), ToF(entry[1]));
                if (entry.Count >= 3) zoom = Mathf.Max(0.01f, ToF(entry[2]));
            }
            Vector2 size = PngSize(assetPath);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            if (!float.IsNaN(pivotPx.x) && size.x > 0 && size.y > 0)
                pivot = new Vector2(pivotPx.x / size.x, 1f - pivotPx.y / size.y);   // may lie outside 0..1 (hand pivots)

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteMode = (int)SpriteImportMode.Single;
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = pivot;
            s.spritePixelsPerUnit = Units.PX * zoom;
            s.spriteMeshType = tiling ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            s.spriteExtrude = 1;
            s.spriteGenerateFallbackPhysicsShape = false;
            s.wrapMode = tiling ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            s.filterMode = FilterMode.Bilinear;
            s.mipmapEnabled = false;
            s.alphaIsTransparency = true;
            s.alphaSource = TextureImporterAlphaSource.FromInput;
            s.readable = false;
            s.npotScale = TextureImporterNPOTScale.None;
            imp.SetTextureSettings(s);
            imp.maxTextureSize = 4096;     // a few UI layouts are wider than 2048
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.compressionQuality = 50;
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var ps = imp.GetPlatformTextureSettings(platform);
                ps.overridden = true;
                ps.maxTextureSize = 4096;
                ps.format = TextureImporterFormat.ASTC_6x6;
                ps.compressionQuality = 50;
                imp.SetPlatformTextureSettings(ps);
            }
        }

        /// <summary>Find the files entry of a PNG: walks up from its folder to the nearest _meta.json.</summary>
        static bool FindEntry(string rel, out List<object> entry)
        {
            entry = null;
            string noExt = rel.Substring(0, rel.Length - 4);
            string dir = Path.GetDirectoryName(noExt)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir))
            {
                string meta = Root + dir + "/_meta.json";
                var files = Files(meta);
                if (files != null)
                {
                    string key = noExt.Substring(dir.Length + 1);
                    if (files.TryGetValue(key, out var v)) entry = v as List<object>;
                    return entry != null;
                }
                int slash = dir.LastIndexOf('/');
                dir = slash > 0 ? dir.Substring(0, slash) : null;
            }
            return false;
        }

        static Dictionary<string, object> Files(string metaPath)
        {
            if (!File.Exists(metaPath)) return null;
            long t = File.GetLastWriteTimeUtc(metaPath).Ticks;
            if (metas.TryGetValue(metaPath, out var c) && c.Key == t) return c.Value;
            Dictionary<string, object> files = null;
            try
            {
                var root = MiniJson.ParseObject(File.ReadAllText(metaPath));
                if (root != null && root.TryGetValue("files", out var f)) files = f as Dictionary<string, object>;
            }
            catch (System.Exception e) { Debug.LogWarning("OriginalSpriteImporter: " + metaPath + ": " + e.Message); }
            files = files ?? new Dictionary<string, object>();
            metas[metaPath] = new KeyValuePair<long, Dictionary<string, object>>(t, files);
            return files;
        }

        /// <summary>Width and height from the PNG IHDR chunk (the texture is not loaded yet in OnPreprocessTexture).</summary>
        static Vector2 PngSize(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var b = new byte[24];
                    if (fs.Read(b, 0, 24) < 24 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return Vector2.zero;
                    int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                    int h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                    return new Vector2(w, h);
                }
            }
            catch (IOException) { return Vector2.zero; }
        }

        static float ToF(object o)
        {
            switch (o)
            {
                case double d: return (float)d;
                case long l: return l;
                case int i: return i;
                default: return 0f;
            }
        }

        [MenuItem("CPW/Reimport Original Art")]
        static void Reimport()
        {
            metas.Clear();
            AssetDatabase.ImportAsset(Root.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("Reimported " + Root);
        }
    }
}
