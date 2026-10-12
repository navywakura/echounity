using UnityEditor;

namespace EchoWorld.Editor
{
    public sealed class BetaAssetImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/Resources/Art/"))return;
            var texture=(TextureImporter)assetImporter;
            bool terrain=assetPath.Contains("/Terrain/");
            texture.textureType=TextureImporterType.Default;
            texture.wrapMode=terrain?UnityEngine.TextureWrapMode.Repeat:UnityEngine.TextureWrapMode.Clamp;
            texture.filterMode=UnityEngine.FilterMode.Bilinear;
            texture.mipmapEnabled=terrain;texture.sRGBTexture=true;
            texture.alphaIsTransparency=!terrain;texture.maxTextureSize=terrain?1024:4096;
            texture.textureCompression=TextureImporterCompression.Compressed;
        }
    }
}
