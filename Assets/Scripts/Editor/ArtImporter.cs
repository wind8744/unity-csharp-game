using UnityEditor;
using UnityEngine;

namespace LaneBattle.Editor
{
    /// <summary>Resources/Sprites 의 PNG 를 픽셀아트 스프라이트로, Resources/Audio 의 WAV 를 알맞게 들여온다. 9분할 UI 는 테두리를 준다.</summary>
    public sealed class ArtImporter : AssetPostprocessor
    {
        const string SpriteDir = "Assets/Resources/Sprites/";
        const string AudioDir = "Assets/Resources/Audio/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SpriteDir)) return;
            var imp = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 64;
            imp.filterMode = FilterMode.Point;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = 2048;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            imp.SetTextureSettings(settings);
            if (name.StartsWith("ui_panel") || name.StartsWith("ui_button")) imp.spriteBorder = new Vector4(14, 14, 14, 14);
            else if (name.StartsWith("ui_card")) imp.spriteBorder = new Vector4(10, 10, 10, 10);
            else if (name == "ui_slot") imp.spriteBorder = new Vector4(8, 8, 8, 8);
            if (name == "title_bg") { imp.filterMode = FilterMode.Bilinear; }
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioDir)) return;
            var imp = (AudioImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            var s = imp.defaultSampleSettings;
            bool music = name.StartsWith("bgm_");
            s.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.loadInBackground = false;
        }

        /// <summary>배치 파이프라인용: 그림·소리를 강제로 다시 들여온다 (스크립트 컴파일 뒤 설정이 적용되도록).</summary>
        [MenuItem("LaneBattle/Reimport Art")]
        public static void Reimport()
        {
            AssetDatabase.ImportAsset("Assets/Resources/Sprites", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Resources/Audio", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log("그림·소리 다시 들여옴");
        }
    }
}
