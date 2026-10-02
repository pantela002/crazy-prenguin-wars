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

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/CPW/Resources/Icons/")) return;
            var imp = (TextureImporter)assetImporter;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
