using System.IO;
using UnityEditor;
using UnityEngine;
using Sizzle.SlashFx.Editor.Baking;

namespace Sizzle.SlashFx.Editor.Generators
{
    /// <summary>
    /// Generates or Hot-Updates XY-plane ParticleSystem GameObjects, Prefabs, and optional 2D Sprite Animation assets
    /// from baked Slash Effect SpriteSheets.
    /// </summary>
    public static class SlashParticleGenerator
    {
        private static Mesh _cachedQuadMesh;

        private static Mesh GetBuiltinQuadMesh()
        {
            if (_cachedQuadMesh == null)
            {
                _cachedQuadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            }
            return _cachedQuadMesh;
        }

        /// <summary>
        /// Bakes the SpriteSheet, builds the Material, and spawns a configured XY-plane ParticleSystem GameObject in the active Scene.
        /// </summary>
        public static GameObject CreateSceneParticleObject(
            SlashEffectAsset asset,
            SpriteSheetBakeResult bake,
            Transform parent = null)
        {
            var material = ParticleMaterialFactory.CreateOrUpdateParticleMaterial(asset, bake);
            string safeName = SpriteSheetBaker.SanitizeFileName(string.IsNullOrEmpty(asset.effectName) ? asset.name : asset.effectName);

            var go = new GameObject($"VFX_{safeName}");
            Undo.RegisterCreatedObjectUndo(go, "Create Slash ParticleSystem");

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
            }

            ConfigureParticleGameObject(go, asset, bake, material);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Simulate(0f, true, true);
                ps.Play(true);
            }

            return go;
        }

        /// <summary>
        /// Saves or Hot-Updates a reusable ParticleSystem Prefab (preserving Prefab GUID on Re-Bake).
        /// </summary>
        public static GameObject SaveOrUpdatePrefabBundle(
            SlashEffectAsset asset,
            SpriteSheetBakeResult bake,
            bool reuseLinkedPrefab = true)
        {
            var settings = asset.bakeSettings;
            var material = ParticleMaterialFactory.CreateOrUpdateParticleMaterial(asset, bake);
            string safeName = SpriteSheetBaker.SanitizeFileName(string.IsNullOrEmpty(asset.effectName) ? asset.name : asset.effectName);

            string prefabPath = null;
            if (reuseLinkedPrefab && !string.IsNullOrEmpty(settings.lastBakedPrefabGuid))
            {
                string existing = AssetDatabase.GUIDToAssetPath(settings.lastBakedPrefabGuid);
                if (!string.IsNullOrEmpty(existing) && existing.EndsWith(".prefab"))
                {
                    prefabPath = existing;
                }
            }

            if (string.IsNullOrEmpty(prefabPath))
            {
                string folder = Path.GetDirectoryName(bake.textureAssetPath)?.Replace('\\', '/') ?? "Assets/SlashEffects/Baked";
                SpriteSheetBaker.EnsureAssetFolderExists(folder);
                prefabPath = $"{folder}/VFX_{safeName}.prefab";
            }

            GameObject savedPrefab;
            if (File.Exists(Path.GetFullPath(prefabPath)))
            {
                // In-place Prefab update preserves GUID and any custom child components users added!
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    ConfigureParticleGameObject(root, asset, bake, material);
                    savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            else
            {
                var temp = new GameObject($"VFX_{safeName}");
                try
                {
                    ConfigureParticleGameObject(temp, asset, bake, material);
                    savedPrefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(temp);
                }
            }

            settings.lastBakedPrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(savedPrefab);
            return savedPrefab;
        }

        /// <summary>
        /// Configures ParticleSystem + ParticleSystemRenderer + SlashParticlePlayer on a GameObject for XY-plane playback.
        /// </summary>
        public static void ConfigureParticleGameObject(
            GameObject go,
            SlashEffectAsset asset,
            SpriteSheetBakeResult bake,
            Material material)
        {
            var settings = asset.bakeSettings;

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.AddComponent<ParticleSystem>();

            var player = go.GetComponent<SlashParticlePlayer>();
            if (player == null) player = go.AddComponent<SlashParticlePlayer>();

            var renderer = go.GetComponent<ParticleSystemRenderer>();

            float ppu = Mathf.Max(1f, settings.pixelsPerUnit);
            float worldW = bake.cellWidth / ppu;
            float worldH = bake.cellHeight / ppu;
            float duration = Mathf.Max(0.02f, bake.durationSeconds);

            // 1. Main Module
            var main = ps.main;
            main.duration = duration;
            main.loop = settings.loopParticle;
            main.startDelay = 0f;
            main.startLifetime = duration;
            main.startSpeed = 0f;
            main.playOnAwake = true;
            main.maxParticles = 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = settings.loopParticle ? ParticleSystemStopAction.None : settings.stopAction;

            if (Mathf.Approximately(worldW, worldH))
            {
                main.startSize3D = false;
                main.startSize = worldW;
            }
            else
            {
                main.startSize3D = true;
                main.startSizeX = worldW;
                main.startSizeY = worldH;
                main.startSizeZ = 1f;
            }

            if (settings.alphaOutputMode == AlphaOutputMode.GrayscaleMask)
            {
                // Apply authored head color as default tint on Grayscale Mask particles
                Color defaultTint = asset.appearance.colorGradient.Evaluate(0.75f);
                main.startColor = new ParticleSystem.MinMaxGradient(defaultTint);
                player.overrideStartColor = false;
                player.tintColor = defaultTint;
            }
            else
            {
                main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            }

            // 2. Emission Module (1-shot Burst at t=0)
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            // 3. Shape Module (disabled so particle stays pinned to XY pivot)
            var shape = ps.shape;
            shape.enabled = false;

            // 4. Texture Sheet Animation Module
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;

            bool useSpritesMode = settings.particleSheetMode == ParticleSheetMode.Sprites
                                  || !bake.gridUvExact
                                  || !bake.isUniformGrid;

            if (useSpritesMode && bake.sprites != null && bake.sprites.Length > 0)
            {
                tsa.mode = ParticleSystemAnimationMode.Sprites;
                while (tsa.spriteCount > 0) tsa.RemoveSprite(tsa.spriteCount - 1);
                for (int i = 0; i < bake.sprites.Length; i++)
                {
                    tsa.AddSprite(bake.sprites[i]);
                }

                tsa.timeMode = ParticleSystemAnimationTimeMode.FPS;
                tsa.fps = bake.fps;
                tsa.cycleCount = 1;
            }
            else
            {
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = Mathf.Max(1, bake.columns);
                tsa.numTilesY = Mathf.Max(1, bake.rows);
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                // FPS timeMode guarantees exact playback speed even when cols * rows > effectiveFrameCount!
                tsa.timeMode = ParticleSystemAnimationTimeMode.FPS;
                tsa.fps = bake.fps;
                tsa.cycleCount = 1;
            }

            // 5. Renderer Module (XY Plane Optimized)
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.sortingLayerName = settings.sortingLayerName;
                renderer.sortingOrder = settings.sortingOrder;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                // Map normalized Y-up pivot (0.5, 0.5 = center) into ParticleSystemRenderer pivot offset
                renderer.pivot = new Vector3(
                    0.5f - bake.normalizedPivot.x,
                    0.5f - bake.normalizedPivot.y,
                    0f
                );

                if (settings.particleRenderMode == ParticlePlaneRenderMode.MeshQuadXY)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    renderer.mesh = GetBuiltinQuadMesh();
                    renderer.alignment = ParticleSystemRenderSpace.Local;
                }
                else
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.alignment = ParticleSystemRenderSpace.View;
                }
            }
        }

        /// <summary>
        /// Bonus generator: creates an XY-plane 2D SpriteRenderer GameObject + AnimationClip from the sliced sprites.
        /// </summary>
        public static GameObject CreateSpriteAnimationObject(SlashEffectAsset asset, SpriteSheetBakeResult bake)
        {
            if (bake.sprites == null || bake.sprites.Length == 0)
            {
                Debug.LogWarning("[SlashEffectStudio] No sliced sprites found to build a 2D Sprite Animation.");
                return null;
            }

            string safeName = SpriteSheetBaker.SanitizeFileName(string.IsNullOrEmpty(asset.effectName) ? asset.name : asset.effectName);
            string folder = Path.GetDirectoryName(bake.textureAssetPath)?.Replace('\\', '/') ?? "Assets/SlashEffects/Baked";
            string clipPath = $"{folder}/{safeName}_Anim.anim";

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, clipPath);
            }

            clip.frameRate = bake.fps;
            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };

            var keyframes = new ObjectReferenceKeyframe[bake.sprites.Length];
            for (int i = 0; i < bake.sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / Mathf.Max(1f, bake.fps),
                    value = bake.sprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            var go = new GameObject($"Sprite_{safeName}");
            Undo.RegisterCreatedObjectUndo(go, "Create 2D Slash Sprite Object");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = bake.sprites[Mathf.Min(bake.sprites.Length / 2, bake.sprites.Length - 1)];
            sr.sortingLayerName = asset.bakeSettings.sortingLayerName;
            sr.sortingOrder = asset.bakeSettings.sortingOrder;

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(clip);
            return go;
        }
    }
}
