using UnityEditor;
using UnityEngine;
using Sizzle.SlashFx.Editor.Baking;
using Sizzle.SlashFx.Editor.Generators;

namespace Sizzle.SlashFx.Editor.UI
{
    [CustomEditor(typeof(SlashEffectAsset))]
    public class SlashEffectAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (SlashEffectAsset)target;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Slash Effect Studio (XY Plane)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.28f, 0.72f, 0.98f);
            if (GUILayout.Button("Open in Slash Effect Studio Window", GUILayout.Height(30f)))
            {
                Selection.activeObject = asset;
                SlashStudioWindow.OpenWindow();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Bake SpriteSheet", GUILayout.Height(24f)))
            {
                EditorApplication.delayCall += () =>
                {
                    if (asset == null) return;
                    var bake = SpriteSheetBaker.Bake(asset, reuseLinkedTexturePath: true);
                    EditorGUIUtility.PingObject(bake.texture);
                };
            }
            if (GUILayout.Button("Create XY Particle", GUILayout.Height(24f)))
            {
                EditorApplication.delayCall += () =>
                {
                    if (asset == null) return;
                    var bake = SpriteSheetBaker.Bake(asset, reuseLinkedTexturePath: true);
                    SlashParticleGenerator.CreateSceneParticleObject(asset, bake);
                };
            }
            if (GUILayout.Button("Save Prefab", GUILayout.Height(24f)))
            {
                EditorApplication.delayCall += () =>
                {
                    if (asset == null) return;
                    var bake = SpriteSheetBaker.Bake(asset, reuseLinkedTexturePath: true);
                    SlashParticleGenerator.SaveOrUpdatePrefabBundle(asset, bake, reuseLinkedPrefab: true);
                };
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            DrawDefaultInspector();
        }
    }
}
