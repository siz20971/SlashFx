using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Sizzle.SlashFx.Editor.Baking
{
    public struct BakedSpriteSlice
    {
        public string name;
        public Rect rect;         // Unity bottom-left Y-up pixel rect
        public Vector2 pivot;     // Normalized [0..1] Y-up pivot inside rect
    }

    /// <summary>
    /// Configures Unity TextureImporter for baked SpriteSheets using Unity 6's ISpriteEditorDataProvider API.
    /// </summary>
    public static class TextureImporterUtility
    {
        public static Sprite[] ConfigureAndSliceSpriteSheet(
            string assetPath,
            IReadOnlyList<BakedSpriteSlice> slices,
            SpriteSheetBakeSettings settings,
            bool isPixelArt)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[SlashEffectStudio] TextureImporter not found at: {assetPath}");
                return System.Array.Empty<Sprite>();
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = Mathf.Max(1f, settings.pixelsPerUnit);
            importer.mipmapEnabled = !settings.disableMipMaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = settings.alphaOutputMode != AlphaOutputMode.AdditiveBlackBg;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(settings.maxSize), 256, 8192);

            if (isPixelArt)
            {
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else
            {
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
            }

            importer.SaveAndReimport();

            // Apply Multiple Sprite Rects & Custom Pivots via Unity 2D Sprite Data Provider
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider != null)
            {
                dataProvider.InitSpriteEditorDataProvider();

                var existingRects = dataProvider.GetSpriteRects();
                var spriteRects = new SpriteRect[slices.Count];

                for (int i = 0; i < slices.Count; i++)
                {
                    var s = slices[i];
                    // Preserve existing SpriteID if name matches (prevents broken Sprite references on Hot-Rebake!)
                    GUID spriteId = GUID.Generate();
                    if (existingRects != null)
                    {
                        for (int e = 0; e < existingRects.Length; e++)
                        {
                            if (existingRects[e].name == s.name)
                            {
                                spriteId = existingRects[e].spriteID;
                                break;
                            }
                        }
                    }

                    spriteRects[i] = new SpriteRect
                    {
                        name = s.name,
                        spriteID = spriteId,
                        rect = s.rect,
                        alignment = SpriteAlignment.Custom,
                        pivot = s.pivot,
                        border = Vector4.zero
                    };
                }

                dataProvider.SetSpriteRects(spriteRects);
                dataProvider.Apply();
                importer.SaveAndReimport();
            }
            else
            {
                Debug.LogWarning("[SlashEffectStudio] ISpriteEditorDataProvider unavailable. Ensure com.unity.2d.sprite is installed.");
            }

            // Collect ordered Sprites from the reimported asset
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var spriteMap = new Dictionary<string, Sprite>();
            foreach (var obj in allAssets)
            {
                if (obj is Sprite sp)
                {
                    spriteMap[sp.name] = sp;
                }
            }

            var orderedSprites = new List<Sprite>(slices.Count);
            for (int i = 0; i < slices.Count; i++)
            {
                if (spriteMap.TryGetValue(slices[i].name, out var sp))
                {
                    orderedSprites.Add(sp);
                }
            }

            return orderedSprites.ToArray();
        }
    }
}
