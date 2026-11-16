// Map pictures for the main menu's carousel (Resources/StreetLife/MapThumbs): imported at their real size,
// uncompressed and without mipmaps. Unity's defaults squashed the 16:9 pictures to a power of two and compressed
// them (ASTC/ETC while the editor is on Android), which made the carousel look fuzzy. Match chat, 13 Nov.
using UnityEditor;

namespace StreetLife
{
    public class MenuThumbImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/StreetLife/MapThumbs/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.npotScale = TextureImporterNPOTScale.None;
            t.mipmapEnabled = false;
            t.textureCompression = TextureImporterCompression.CompressedHQ;   // 16 Nov: was uncompressed, 4 MB a picture in every build
            t.maxTextureSize = 2048;
            t.filterMode = UnityEngine.FilterMode.Bilinear;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }
    }
}
