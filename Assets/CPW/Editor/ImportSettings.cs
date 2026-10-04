using System.IO;
using UnityEditor;
using UnityEngine;

namespace CPW.EditorTools
{
    /// <summary>
    /// Import settings for the game's assets (the repo ships no .meta files, so without this every
    /// clone would fall back to Unity defaults).
    /// - Long audio (music, ambient loops) streams instead of being decompressed into RAM.
    /// - Icons keep their real size (no power-of-two rescale, no mipmaps) so they stay sharp in the UI.
    /// - Weapon textures: mipmapped, repeat, ASTC on phones; weapon materials get their texture by name.
    /// </summary>
    public class ImportSettings : AssetPostprocessor
    {
        const long StreamAbove = 200 * 1024;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/CPW/Resources/Audio/")) return;
            var imp = (AudioImporter)assetImporter;
            long size = File.Exists(assetPath) ? new FileInfo(assetPath).Length : 0;
            var s = imp.defaultSampleSettings;
            if (size > StreamAbove)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
            }
            else
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
            }
            imp.defaultSampleSettings = s;
            imp.loadInBackground = size > StreamAbove;
        }

        const string WeaponDir = "Assets/CPW/Resources/Models/Weapons/";
        const string WeaponTexDir = WeaponDir + "Textures/";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(WeaponTexDir)) { WeaponTexture((TextureImporter)assetImporter); return; }
            if (!assetPath.StartsWith("Assets/CPW/Resources/Icons/")) return;
            var imp = (TextureImporter)assetImporter;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Compressed;
        }

        /// <summary>
        /// Weapon surface tiles (Blender/scripts/weapon_textures.py, 256px, seamless, sampled with object-space UV0 at
        /// one tile per unit): repeat, mipmaps (they are minified a lot on a phone), trilinear + light aniso, ASTC 6x6
        /// on Android and iOS.
        /// </summary>
        static void WeaponTexture(TextureImporter imp)
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 2;
            imp.maxTextureSize = 256;
            imp.textureCompression = TextureImporterCompression.Compressed;
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var ps = imp.GetPlatformTextureSettings(platform);
                ps.overridden = true;
                ps.maxTextureSize = 256;
                ps.format = TextureImporterFormat.ASTC_6x6;
                ps.textureCompression = TextureImporterCompression.Compressed;
                imp.SetPlatformTextureSettings(ps);
            }
        }

        /// <summary>Weapon FBX: materials embedded from the FBX material description (they reference Textures/*.png).</summary>
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(WeaponDir) || assetPath.StartsWith(WeaponTexDir)) return;
            var imp = (ModelImporter)assetImporter;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.materialSearch = ModelImporterMaterialSearch.Local;
        }

        /// <summary>
        /// Weapon materials are named W_{texture} / Glow_{texture} (Blender/scripts/weapons.py TM) and their PNG is
        /// Textures/{texture}.png next to the FBX. If the FBX import did not resolve the texture (absolute path from
        /// the build machine, search settings), assign it here; the runtime resolves it again by name as a last resort
        /// (Mats.NamedTexture).
        /// </summary>
        void OnPostprocessMaterial(Material material)
        {
            if (!assetPath.StartsWith(WeaponDir) || material == null || !material.HasProperty("_MainTex")) return;
            if (material.mainTexture != null) return;
            string n = Mats.BaseName(material.name);
            string bare = n.StartsWith("W_") ? n.Substring(2) : n.StartsWith("Glow_") ? n.Substring(5) : n;
            string path = WeaponTexDir + bare + ".png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return;
            material.mainTexture = tex;
            if (material.HasProperty("_Color")) material.color = Color.white;
        }
    }
}
