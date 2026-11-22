using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Sizzle.SlashFx.Editor.Baking;
using Sizzle.SlashFx.Editor.Generators;

namespace Sizzle.SlashFx.Editor
{
    /// <summary>
    /// Verification & smoke-test utility callable via MenuItem or Unity -batchmode -executeMethod.
    /// </summary>
    public static class SlashStudioVerification
    {
        [MenuItem("Tools/Slash Effect Studio (Verify & Build Samples)", false, 201)]
        public static void RunFullVerificationAndBuildSamples()
        {
            Debug.Log("[SlashEffectStudio][Verify] Starting verification suite on Unity " + Application.unityVersion);

            string sampleRoot = "Assets/SlashEffects";
            string bakedRoot = "Assets/SlashEffects/Baked";
            SpriteSheetBaker.EnsureAssetFolderExists(sampleRoot);
            SpriteSheetBaker.EnsureAssetFolderExists(bakedRoot);

            string[] presetsToBuild = { "ThinArc", "WideSlash", "CrossSlashX", "WhirlwindSpin" };
            int builtCount = 0;

            foreach (string presetName in presetsToBuild)
            {
                string assetPath = $"{sampleRoot}/{presetName}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<SlashEffectAsset>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<SlashEffectAsset>();
                    SlashEffectPresets.ApplyPreset(asset, presetName, pathOnly: false);
                    asset.bakeSettings.outputFolder = bakedRoot;
                    asset.bakeSettings.columns = 4;
                    asset.bakeSettings.padding = 0;
                    asset.bakeSettings.skipEmptyBoundaryFrames = true;
                    asset.bakeSettings.doubleSidedMaterial = true;
                    asset.bakeSettings.particleRenderMode = ParticlePlaneRenderMode.MeshQuadXY;
                    AssetDatabase.CreateAsset(asset, assetPath);
                }

                // 1. JSON round-trip test
                string exportedJson = SlashEffectJsonInterop.ExportToJsonString(asset);
                if (string.IsNullOrEmpty(exportedJson) || !exportedJson.Contains(presetName))
                {
                    throw new Exception($"[SlashEffectStudio][Verify] JSON export failed for {presetName}");
                }

                // 2. Bake SpriteSheet PNG + Multiple Sprite Slicing
                var bake = SpriteSheetBaker.Bake(asset, reuseLinkedTexturePath: true);
                if (bake.texture == null)
                {
                    throw new Exception($"[SlashEffectStudio][Verify] Baked texture missing for {presetName}");
                }
                if (bake.sprites == null || bake.sprites.Length == 0)
                {
                    throw new Exception($"[SlashEffectStudio][Verify] Sliced sprites missing for {presetName}");
                }

                // 3. Save XY-Plane ParticleSystem Prefab Bundle
                var prefab = SlashParticleGenerator.SaveOrUpdatePrefabBundle(asset, bake, reuseLinkedPrefab: true);
                if (prefab == null)
                {
                    throw new Exception($"[SlashEffectStudio][Verify] Prefab generation failed for {presetName}");
                }

                var ps = prefab.GetComponent<ParticleSystem>();
                var player = prefab.GetComponent<SlashParticlePlayer>();
                if (ps == null || player == null || !ps.textureSheetAnimation.enabled)
                {
                    throw new Exception($"[SlashEffectStudio][Verify] ParticleSystem components invalid on {prefab.name}");
                }

                builtCount++;
                Debug.Log($"[SlashEffectStudio][Verify] OK '{presetName}': Sheet={bake.sheetWidth}x{bake.sheetHeight}, Sprites={bake.sprites.Length}, Prefab={AssetDatabase.GetAssetPath(prefab)}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SlashEffectStudio][Verify] PASSED! Built & verified {builtCount} XY-plane slash presets + SpriteSheets + ParticleSystem prefabs.");
        }
    }
}
