using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Sizzle.SlashFx.Editor.Baking;

namespace Sizzle.SlashFx.Editor.Generators
{
    /// <summary>
    /// Automatically creates or updates a Render-Pipeline-aware Particle Material (Built-in / URP / HDRP)
    /// configured with Double-Sided (Cull Off) rendering for XY-plane horizontal FlipX side-scrollers.
    /// Preserves Material GUID on Hot-Rebake.
    /// </summary>
    public static class ParticleMaterialFactory
    {
        public static Material CreateOrUpdateParticleMaterial(
            SlashEffectAsset asset,
            SpriteSheetBakeResult bake,
            bool reuseLinkedMaterial = true)
        {
            var settings = asset.bakeSettings;
            string safeName = SpriteSheetBaker.SanitizeFileName(string.IsNullOrEmpty(asset.effectName) ? asset.name : asset.effectName);

            Material mat = null;
            string matPath = null;

            if (reuseLinkedMaterial && !string.IsNullOrEmpty(settings.lastBakedMaterialGuid))
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(settings.lastBakedMaterialGuid);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    mat = AssetDatabase.LoadAssetAtPath<Material>(existingPath);
                    matPath = existingPath;
                }
            }

            Shader shader = ResolveParticleShader(out bool isUrp, out bool isHdrp);
            if (mat == null)
            {
                string folder = Path.GetDirectoryName(bake.textureAssetPath)?.Replace('\\', '/') ?? "Assets/SlashEffects/Baked";
                SpriteSheetBaker.EnsureAssetFolderExists(folder);
                matPath = $"{folder}/{safeName}_ParticleMat.mat";
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            ConfigureMaterialProperties(mat, bake.texture, settings, asset.appearance, isUrp, isHdrp);

            EditorUtility.SetDirty(mat);
            settings.lastBakedMaterialGuid = AssetDatabase.AssetPathToGUID(matPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return mat;
        }

        private static Shader ResolveParticleShader(out bool isUrp, out bool isHdrp)
        {
            isUrp = false;
            isHdrp = false;

            var rp = GraphicsSettings.defaultRenderPipeline;
            string rpTypeName = rp != null ? rp.GetType().Name : string.Empty;

            if (rpTypeName.Contains("Universal"))
            {
                var urpShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (urpShader != null)
                {
                    isUrp = true;
                    return urpShader;
                }
            }
            else if (rpTypeName.Contains("HDRenderPipeline"))
            {
                var hdrpShader = Shader.Find("HDRP/Unlit");
                if (hdrpShader != null)
                {
                    isHdrp = true;
                    return hdrpShader;
                }
            }

            // Built-in RP standard particle unlit (supports Alpha, Additive, Premultiply, Cull Off)
            var stdParticle = Shader.Find("Particles/Standard Unlit");
            if (stdParticle != null) return stdParticle;

            var legacy = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (legacy != null) return legacy;

            return Shader.Find("Sprites/Default");
        }

        private static void ConfigureMaterialProperties(
            Material mat,
            Texture2D sheetTex,
            SpriteSheetBakeSettings settings,
            SlashAppearanceData appearance,
            bool isUrp,
            bool isHdrp)
        {
            bool useAdditive = settings.alphaOutputMode == AlphaOutputMode.AdditiveBlackBg ||
                               appearance.blendMode == SlashBlendMode.Additive;
            bool usePremul = settings.alphaOutputMode == AlphaOutputMode.PremultipliedAlpha;
            float cullValue = settings.doubleSidedMaterial ? (float)CullMode.Off : (float)CullMode.Back;

            if (isUrp)
            {
                // URP Particles/Unlit
                mat.SetTexture("_BaseMap", sheetTex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", cullValue);

                if (useAdditive)
                {
                    mat.SetFloat("_Blend", 2f); // Additive
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.One);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_BLENDMODE_ADD");
                    mat.DisableKeyword("_BLENDMODE_PREMULTIPLY");
                }
                else if (usePremul)
                {
                    mat.SetFloat("_Blend", 1f); // Premultiply
                    mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_BLENDMODE_PREMULTIPLY");
                    mat.DisableKeyword("_BLENDMODE_ADD");
                }
                else
                {
                    mat.SetFloat("_Blend", 0f); // Alpha
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.DisableKeyword("_BLENDMODE_ADD");
                    mat.DisableKeyword("_BLENDMODE_PREMULTIPLY");
                }

                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (isHdrp)
            {
                mat.SetTexture("_UnlitColorMap", sheetTex);
                mat.SetColor("_UnlitColor", Color.white);
                mat.SetFloat("_SurfaceType", 1f);
                mat.SetFloat("_CullMode", cullValue);
                mat.SetFloat("_BlendMode", useAdditive ? 1f : 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                // Built-in Particles/Standard Unlit
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", sheetTex);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", cullValue);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

                if (mat.HasProperty("_Mode"))
                {
                    // 0=Opaque, 1=Cutout, 2=Fade, 3=Transparent, 4=Additive, 5=Subtractive, 6=Modulate
                    mat.SetFloat("_Mode", useAdditive ? 4f : (usePremul ? 3f : 2f));
                }

                if (useAdditive)
                {
                    if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                }
                else if (usePremul)
                {
                    if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                    if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                }
                else
                {
                    if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                }

                mat.renderQueue = (int)RenderQueue.Transparent;
            }
        }
    }
}
