using System;
using UnityEngine;
using UnityEngine.Rendering;
using Sizzle.SlashFx.Editor.Math;

namespace Sizzle.SlashFx.Editor.Rendering
{
    /// <summary>
    /// Render-Pipeline-agnostic GPU offscreen renderer for SlashEffectAsset.
    /// Uses CommandBuffer + RenderTexture to render curved UV ribbons, procedural patterns,
    /// inner core, toon outline, separable Gaussian glow, phantom echoes, multi-slash combos, and pixel art.
    /// </summary>
    public sealed class SlashGpuRenderer : IDisposable
    {
        private const string RibbonShaderName = "Hidden/Sizzle/SlashFx/RibbonBake";
        private const string GlowShaderName = "Hidden/Sizzle/SlashFx/GlowBlur";
        private const string PixelArtShaderName = "Hidden/Sizzle/SlashFx/PixelArt";

        private Material _ribbonMat;
        private Material _glowMat;
        private Material _pixelArtMat;
        private Mesh _scratchMesh;
        private Texture2D _gradientLut;
        private readonly Color[] _lutColors = new Color[128];
        private bool _disposed;

        private static readonly int GradientTexId = Shader.PropertyToID("_GradientTex");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int FrameAlphaId = Shader.PropertyToID("_FrameAlpha");
        private static readonly int BaseAlphaId = Shader.PropertyToID("_BaseAlpha");
        private static readonly int EnableTailFadeId = Shader.PropertyToID("_EnableTailFade");
        private static readonly int TailFadeRatioId = Shader.PropertyToID("_TailFadeRatio");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int EnableCoreId = Shader.PropertyToID("_EnableCore");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int CoreWidthRatioId = Shader.PropertyToID("_CoreWidthRatio");
        private static readonly int CoreAlphaId = Shader.PropertyToID("_CoreAlpha");
        private static readonly int PatternTypeId = Shader.PropertyToID("_PatternType");
        private static readonly int PatternIntensityId = Shader.PropertyToID("_PatternIntensity");
        private static readonly int PatternScaleId = Shader.PropertyToID("_PatternScale");
        private static readonly int FrameSeedId = Shader.PropertyToID("_FrameSeed");
        private static readonly int EnableToonOutlineId = Shader.PropertyToID("_EnableToonOutline");
        private static readonly int ToonOutlineWidthId = Shader.PropertyToID("_ToonOutlineWidth");
        private static readonly int ToonOutlineColorId = Shader.PropertyToID("_ToonOutlineColor");
        private static readonly int IsPhantomId = Shader.PropertyToID("_IsPhantom");
        private static readonly int PhantomColorId = Shader.PropertyToID("_PhantomColor");
        private static readonly int GrayscaleMaskId = Shader.PropertyToID("_GrayscaleMask");

        private static readonly int BlurDirId = Shader.PropertyToID("_BlurDir");
        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int GlowTexId = Shader.PropertyToID("_GlowTex");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int OutputModeId = Shader.PropertyToID("_OutputMode");

        private static readonly int CanvasSizeId = Shader.PropertyToID("_CanvasSize");
        private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
        private static readonly int AlphaCutoffId = Shader.PropertyToID("_AlphaCutoff");

        private bool EnsureResources()
        {
            if (_ribbonMat == null)
            {
                var shader = Shader.Find(RibbonShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[SlashEffectStudio] Missing shader: {RibbonShaderName}");
                    return false;
                }
                _ribbonMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (_glowMat == null)
            {
                var shader = Shader.Find(GlowShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[SlashEffectStudio] Missing shader: {GlowShaderName}");
                    return false;
                }
                _glowMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (_pixelArtMat == null)
            {
                var shader = Shader.Find(PixelArtShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[SlashEffectStudio] Missing shader: {PixelArtShaderName}");
                    return false;
                }
                _pixelArtMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (_scratchMesh == null)
            {
                _scratchMesh = new Mesh { name = "SlashRibbonScratch", hideFlags = HideFlags.HideAndDontSave };
                _scratchMesh.MarkDynamic();
            }

            if (_gradientLut == null)
            {
                _gradientLut = new Texture2D(128, 1, TextureFormat.RGBA32, false, linear: false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            return true;
        }

        private void UpdateGradientLut(Gradient grad, bool overrideTwoColor, Color cStart, Color cEnd)
        {
            for (int i = 0; i < _lutColors.Length; i++)
            {
                float t = i / (float)(_lutColors.Length - 1);
                _lutColors[i] = overrideTwoColor ? Color.Lerp(cStart, cEnd, t) : grad.Evaluate(t);
            }
            _gradientLut.SetPixels(_lutColors);
            _gradientLut.Apply(false, false);
        }

        /// <summary>
        /// Renders a single frame of a SlashEffectAsset into targetRT.
        /// targetRT should have dimensions matching asset.canvasWidth x asset.canvasHeight (or desired output size).
        /// </summary>
        public void RenderFrame(
            SlashEffectAsset asset,
            int frameIndex,
            RenderTexture targetRT,
            AlphaOutputMode outputMode = AlphaOutputMode.StandardAlpha)
        {
            if (asset == null || targetRT == null || !EnsureResources()) return;
            asset.EnsureValid();

            // Always preserve active RenderTexture & GL matrix stack so calling RenderFrame
            // never corrupts EditorWindow / SceneView rendering state.
            RenderTexture prevActive = RenderTexture.active;
            GL.PushMatrix();

            int w = targetRT.width;
            int h = targetRT.height;
            bool grayscale = outputMode == AlphaOutputMode.GrayscaleMask;

            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0)
            {
                sRGB = false,
                useMipMap = false,
                msaaSamples = 1
            };

            RenderTexture baseRT = RenderTexture.GetTemporary(desc);
            RenderTexture compositeRT = RenderTexture.GetTemporary(desc);
            try
            {
                baseRT.filterMode = FilterMode.Bilinear;
                baseRT.wrapMode = TextureWrapMode.Clamp;

                var cmd = new CommandBuffer { name = "SlashGpuRenderFrame" };
                cmd.SetRenderTarget(baseRT);
                cmd.ClearRenderTarget(false, true, Color.clear);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                Matrix4x4 proj = Matrix4x4.Ortho(0f, asset.canvasWidth, 0f, asset.canvasHeight, -10f, 10f);

                DrawAllSlashLayers(cmd, baseRT, proj, asset, frameIndex, grayscale);
                cmd.Release();

                // Glow + Output mode composite pass
                compositeRT.filterMode = FilterMode.Bilinear;
                compositeRT.wrapMode = TextureWrapMode.Clamp;

                float glowBlur = asset.appearance.glowBlur;
                float glowIntensity = asset.appearance.glowIntensity;

                if (glowBlur > 0.25f && glowIntensity > 0.001f)
                {
                    RenderTexture blurTempA = RenderTexture.GetTemporary(desc);
                    RenderTexture blurTempB = RenderTexture.GetTemporary(desc);
                    try
                    {
                        blurTempA.filterMode = FilterMode.Bilinear;
                        blurTempB.filterMode = FilterMode.Bilinear;
                        blurTempA.wrapMode = TextureWrapMode.Clamp;
                        blurTempB.wrapMode = TextureWrapMode.Clamp;

                        // 2 iterations of separable Gaussian blur for smooth wide glow
                        _glowMat.SetFloat(BlurRadiusId, glowBlur * 0.65f);
                        _glowMat.SetVector(BlurDirId, new Vector2(1f, 0f));
                        Graphics.Blit(baseRT, blurTempA, _glowMat, 0);

                        _glowMat.SetVector(BlurDirId, new Vector2(0f, 1f));
                        Graphics.Blit(blurTempA, blurTempB, _glowMat, 0);

                        if (glowBlur > 10f)
                        {
                            _glowMat.SetFloat(BlurRadiusId, glowBlur * 1.15f);
                            _glowMat.SetVector(BlurDirId, new Vector2(1f, 0f));
                            Graphics.Blit(blurTempB, blurTempA, _glowMat, 0);

                            _glowMat.SetVector(BlurDirId, new Vector2(0f, 1f));
                            Graphics.Blit(blurTempA, blurTempB, _glowMat, 0);
                        }

                        _glowMat.SetTexture(GlowTexId, blurTempB);
                        _glowMat.SetColor(GlowColorId, asset.appearance.glowColor);
                        _glowMat.SetFloat(GlowIntensityId, glowIntensity);
                        _glowMat.SetFloat(OutputModeId, (int)outputMode);
                        Graphics.Blit(baseRT, compositeRT, _glowMat, 1);
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(blurTempA);
                        RenderTexture.ReleaseTemporary(blurTempB);
                    }
                }
                else
                {
                    _glowMat.SetTexture(GlowTexId, Texture2D.blackTexture);
                    _glowMat.SetColor(GlowColorId, Color.black);
                    _glowMat.SetFloat(GlowIntensityId, 0f);
                    _glowMat.SetFloat(OutputModeId, (int)outputMode);
                    Graphics.Blit(baseRT, compositeRT, _glowMat, 1);
                }

                // Optional Pixel Art Quantization Pass
                if (asset.appearance.enablePixelArt && asset.appearance.pixelArtSize >= 2)
                {
                    _pixelArtMat.SetVector(CanvasSizeId, new Vector4(w, h, 0f, 0f));
                    _pixelArtMat.SetFloat(PixelSizeId, asset.appearance.pixelArtSize);
                    _pixelArtMat.SetFloat(AlphaCutoffId, asset.appearance.pixelArtAlphaCutoff);
                    Graphics.Blit(compositeRT, targetRT, _pixelArtMat, 0);
                }
                else
                {
                    Graphics.Blit(compositeRT, targetRT);
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(baseRT);
                RenderTexture.ReleaseTemporary(compositeRT);
                RenderTexture.active = prevActive;
                GL.PopMatrix();
            }
        }

        private void DrawAllSlashLayers(
            CommandBuffer cmd,
            RenderTexture baseRT,
            Matrix4x4 proj,
            SlashEffectAsset asset,
            int frameIndex,
            bool grayscale)
        {
            int totalFrames = Mathf.Max(1, asset.playback.frameCount);
            bool hasExtra = asset.extraSlashes != null && asset.extraSlashes.Count > 0;
            var syncMode = asset.playback.comboSyncMode;

            // 1. Extra Sub-Layers (Combo / Cross Slash)
            if (hasExtra)
            {
                foreach (var sub in asset.extraSlashes)
                {
                    if (sub == null || !sub.enabled) continue;
                    int subDelay = Mathf.Max(0, sub.delayFrames);
                    int subDuration = syncMode == ComboSyncMode.SyncEnd
                        ? Mathf.Max(2, totalFrames - subDelay)
                        : Mathf.Max(2, sub.durationFrames > 0 ? sub.durationFrames : Mathf.Min(totalFrames - subDelay, 8));

                    int subFrameIdx = frameIndex - subDelay;
                    if (subFrameIdx < 0 || subFrameIdx >= subDuration) continue;

                    DrawSingleLayerWithPhantom(
                        cmd,
                        baseRT,
                        proj,
                        sub.path,
                        asset.width,
                        asset.playback,
                        asset.appearance,
                        subFrameIdx,
                        subDuration,
                        sub.widthScale,
                        sub.overrideColors,
                        sub.colorStart,
                        sub.colorEnd,
                        grayscale);
                }
            }

            // 2. Main Layer
            int mainDuration = totalFrames;
            if (hasExtra && syncMode == ComboSyncMode.Sequential)
            {
                mainDuration = Mathf.Max(2, asset.playback.mainDurationFrames > 0
                    ? asset.playback.mainDurationFrames
                    : Mathf.Min(totalFrames, 8));
            }

            if (frameIndex < mainDuration)
            {
                DrawSingleLayerWithPhantom(
                    cmd,
                    baseRT,
                    proj,
                    asset.path,
                    asset.width,
                    asset.playback,
                    asset.appearance,
                    frameIndex,
                    mainDuration,
                    1f,
                    false,
                    Color.white,
                    Color.white,
                    grayscale);
            }
        }

        private void DrawSingleLayerWithPhantom(
            CommandBuffer cmd,
            RenderTexture baseRT,
            Matrix4x4 proj,
            SlashPathData path,
            SlashWidthData width,
            SlashPlaybackData playback,
            SlashAppearanceData app,
            float frameIdx,
            int totalFrames,
            float widthScale,
            bool overrideTwoColor,
            Color cStart,
            Color cEnd,
            bool grayscale)
        {
            // Phantom Echo behind main ribbon
            if (app.enablePhantom)
            {
                int pCount = Mathf.Clamp(app.phantomCount, 1, 3);
                float pDelay = Mathf.Clamp(app.phantomDelay, 0.2f, 3f);
                for (int c = pCount; c >= 1; c--)
                {
                    float lagFrame = frameIdx - c * pDelay;
                    if (lagFrame < 0f) continue;

                    var ghostEval = SlashRibbonEvaluator.EvaluateFrame(
                        path, width, playback, lagFrame, totalFrames, widthScale * app.phantomScale);
                    if (!ghostEval.isVisible) continue;

                    Color pCol = app.phantomColor;
                    pCol.a = Mathf.Clamp01(app.phantomAlpha / c);
                    DrawEvaluatedRibbonImmediate(cmd, baseRT, proj, ghostEval, app, true, pCol, false, Color.white, Color.white, grayscale);
                }
            }

            // Primary ribbon
            var eval = SlashRibbonEvaluator.EvaluateFrame(path, width, playback, frameIdx, totalFrames, widthScale);
            if (eval.isVisible)
            {
                DrawEvaluatedRibbonImmediate(cmd, baseRT, proj, eval, app, false, Color.white, overrideTwoColor, cStart, cEnd, grayscale);
            }
        }

        private void DrawEvaluatedRibbonImmediate(
            CommandBuffer cmd,
            RenderTexture baseRT,
            Matrix4x4 proj,
            in EvaluatedFrame frame,
            SlashAppearanceData app,
            bool isPhantom,
            Color phantomColor,
            bool overrideTwoColor,
            Color cStart,
            Color cEnd,
            bool grayscale)
        {
            SlashRibbonEvaluator.BuildRibbonMesh(_scratchMesh, frame);
            if (_scratchMesh.vertexCount < 4) return;

            UpdateGradientLut(app.colorGradient, overrideTwoColor, cStart, cEnd);

            bool additive = app.blendMode == SlashBlendMode.Additive;
            _ribbonMat.SetTexture(GradientTexId, _gradientLut);
            _ribbonMat.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            _ribbonMat.SetFloat(DstBlendId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));

            _ribbonMat.SetFloat(FrameAlphaId, frame.alpha);
            _ribbonMat.SetFloat(BaseAlphaId, app.baseAlpha);
            _ribbonMat.SetFloat(EnableTailFadeId, app.enableTailAlphaFade ? 1f : 0f);
            _ribbonMat.SetFloat(TailFadeRatioId, app.tailFadeRatio);
            _ribbonMat.SetFloat(EdgeSoftnessId, app.edgeSoftness);

            _ribbonMat.SetFloat(EnableCoreId, app.enableCore ? 1f : 0f);
            _ribbonMat.SetColor(CoreColorId, app.coreColor);
            _ribbonMat.SetFloat(CoreWidthRatioId, app.coreWidthRatio);
            _ribbonMat.SetFloat(CoreAlphaId, app.coreAlpha);

            _ribbonMat.SetFloat(PatternTypeId, (float)app.patternType);
            _ribbonMat.SetFloat(PatternIntensityId, app.patternIntensity);
            _ribbonMat.SetFloat(PatternScaleId, app.patternScale);
            _ribbonMat.SetFloat(FrameSeedId, frame.tEnd * 10f);

            _ribbonMat.SetFloat(EnableToonOutlineId, app.enableToonOutline ? 1f : 0f);
            _ribbonMat.SetFloat(ToonOutlineWidthId, app.toonOutlineWidth);
            _ribbonMat.SetColor(ToonOutlineColorId, app.toonOutlineColor);

            _ribbonMat.SetFloat(IsPhantomId, isPhantom ? 1f : 0f);
            _ribbonMat.SetColor(PhantomColorId, phantomColor);
            _ribbonMat.SetFloat(GrayscaleMaskId, grayscale ? 1f : 0f);

            cmd.SetRenderTarget(baseRT);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, proj);
            cmd.DrawMesh(_scratchMesh, Matrix4x4.identity, _ribbonMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        /// <summary>
        /// Renders a single frame and reads it back as a CPU Texture2D (RGBA32, sRGB).
        /// Caller is responsible for destroying the returned Texture2D.
        /// </summary>
        public Texture2D RenderFrameToTexture2D(SlashEffectAsset asset, int frameIndex, AlphaOutputMode outputMode)
        {
            int w = Mathf.Max(32, asset.canvasWidth);
            int h = Mathf.Max(32, asset.canvasHeight);

            var rtDesc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = true,
                useMipMap = false,
                msaaSamples = 1
            };

            RenderTexture rt = RenderTexture.GetTemporary(rtDesc);
            RenderFrame(asset, frameIndex, rt, outputMode);

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prevActive;

            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_ribbonMat != null) UnityEngine.Object.DestroyImmediate(_ribbonMat);
            if (_glowMat != null) UnityEngine.Object.DestroyImmediate(_glowMat);
            if (_pixelArtMat != null) UnityEngine.Object.DestroyImmediate(_pixelArtMat);
            if (_scratchMesh != null) UnityEngine.Object.DestroyImmediate(_scratchMesh);
            if (_gradientLut != null) UnityEngine.Object.DestroyImmediate(_gradientLut);

            _ribbonMat = null;
            _glowMat = null;
            _pixelArtMat = null;
            _scratchMesh = null;
            _gradientLut = null;
        }
    }
}
