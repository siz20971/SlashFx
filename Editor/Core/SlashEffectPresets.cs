using System.Collections.Generic;
using UnityEngine;
using Ympp.SlashEffectStudio.Editor.Math;

namespace Ympp.SlashEffectStudio.Editor
{
    /// <summary>
    /// Built-in 11 slash presets and curve presets faithfully ported from SlashEffectStudio
    /// into Unity XY-plane (Y-up) coordinates.
    /// </summary>
    public static class SlashEffectPresets
    {
        public static readonly string[] PresetNames =
        {
            "ThinArc",
            "WideSlash",
            "Crescent",
            "StraightSlash",
            "RisingSlash",
            "FallingSlash",
            "ShortTrail",
            "ExpandingSlash",
            "DoubleSlashS",
            "CrossSlashX",
            "WhirlwindSpin"
        };

        public static AnimationCurve CurveCometSlash() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.35f, 0.42f),
            new CurveKeyPair(0.72f, 1f),
            new CurveKeyPair(0.9f, 0.85f),
            new CurveKeyPair(1f, 0f)
        });

        public static AnimationCurve CurveBellCrescent() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.25f, 0.71f),
            new CurveKeyPair(0.5f, 1f),
            new CurveKeyPair(0.75f, 0.71f),
            new CurveKeyPair(1f, 0f)
        });

        public static AnimationCurve CurveTaperTail() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.7f, 1f),
            new CurveKeyPair(1f, 0.8f)
        });

        public static AnimationCurve CurveKatanaRazor() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.4f, 0.25f),
            new CurveKeyPair(0.82f, 1f),
            new CurveKeyPair(0.94f, 0.75f),
            new CurveKeyPair(1f, 0f)
        });

        public static AnimationCurve CurveHeavyGreatsword() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.18f, 0.88f),
            new CurveKeyPair(0.55f, 1f),
            new CurveKeyPair(0.88f, 0.92f),
            new CurveKeyPair(1f, 0f)
        });

        public static AnimationCurve CurveProgressImpact() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0.25f),
            new CurveKeyPair(0.35f, 1f),
            new CurveKeyPair(0.75f, 0.9f),
            new CurveKeyPair(1f, 0.1f)
        });

        public static AnimationCurve CurveProgressCharge() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0.1f),
            new CurveKeyPair(0.75f, 1f),
            new CurveKeyPair(1f, 0.5f)
        });

        public static AnimationCurve CurveEaseInOut() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.3f, 0.1f),
            new CurveKeyPair(0.7f, 0.9f),
            new CurveKeyPair(1f, 1f)
        });

        public static AnimationCurve CurveEaseOutExpo() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 0f),
            new CurveKeyPair(0.15f, 0.65f),
            new CurveKeyPair(0.35f, 0.91f),
            new CurveKeyPair(0.65f, 0.99f),
            new CurveKeyPair(1f, 1f)
        });

        public static AnimationCurve CurveLinearFadeOut() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 1f),
            new CurveKeyPair(1f, 0f)
        }, linear: true);

        public static AnimationCurve CurveFlashHoldDrop() => MonotoneCurveUtility.BuildMonotoneCurve(new[]
        {
            new CurveKeyPair(0f, 1f),
            new CurveKeyPair(0.7f, 0.95f),
            new CurveKeyPair(0.88f, 0.4f),
            new CurveKeyPair(1f, 0f)
        });

        private static Color Hex(string html)
        {
            return ColorUtility.TryParseHtmlString(html, out Color c) ? c : Color.white;
        }

        private static Gradient TwoColorGradient(string startHex, string endHex)
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Hex(startHex), 0f),
                    new GradientColorKey(Hex(endHex), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            return g;
        }

        /// <summary>
        /// Helper converting web Y-down 512x512 coordinates into target canvas Y-up coordinates.
        /// </summary>
        private static Vector2 FromWeb512(float x, float yWeb, int canvasW, int canvasH)
        {
            float sx = canvasW / 512f;
            float sy = canvasH / 512f;
            return new Vector2(x * sx, (512f - yWeb) * sy);
        }

        private static SlashPathData BuildCubicWeb512(
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
            int w, int h)
        {
            var path = new SlashPathData();
            path.anchors.Add(new BezierAnchorData
            {
                point = FromWeb512(p0.x, p0.y, w, h),
                hasHandleIn = false,
                handleIn = FromWeb512(p0.x, p0.y, w, h),
                hasHandleOut = true,
                handleOut = FromWeb512(p1.x, p1.y, w, h)
            });
            path.anchors.Add(new BezierAnchorData
            {
                point = FromWeb512(p3.x, p3.y, w, h),
                hasHandleIn = true,
                handleIn = FromWeb512(p2.x, p2.y, w, h),
                hasHandleOut = false,
                handleOut = FromWeb512(p3.x, p3.y, w, h)
            });
            return path;
        }

        public static void ApplyPreset(SlashEffectAsset asset, string presetName, bool pathOnly)
        {
            if (asset == null) return;
            int w = asset.canvasWidth > 0 ? asset.canvasWidth : 512;
            int h = asset.canvasHeight > 0 ? asset.canvasHeight : 512;

            if (!pathOnly)
            {
                asset.effectName = presetName;
                asset.pivot = new Vector2(0.5f, 0.5f);
                asset.extraSlashes = new List<SlashSubLayerData>();
                asset.width = new SlashWidthData
                {
                    minWidth = 4f,
                    maxWidth = 28f,
                    symmetric = true,
                    innerWidthRatio = 0.35f,
                    widthCurve = CurveCometSlash(),
                    progressWidthCurve = CurveProgressImpact()
                };
                asset.playback = new SlashPlaybackData
                {
                    frameCount = 12,
                    fps = 24f,
                    mode = RibbonMode.Progressive,
                    tailLength = 0.45f,
                    finishMode = FinishMode.Retract,
                    timeCurve = CurveEaseInOut(),
                    fadeCurve = CurveLinearFadeOut(),
                    comboSyncMode = ComboSyncMode.Sequential,
                    mainDurationFrames = 8
                };
                asset.appearance = new SlashAppearanceData
                {
                    colorGradient = TwoColorGradient("#38bdf8", "#6366f1"),
                    baseAlpha = 1f,
                    enableTailAlphaFade = true,
                    tailFadeRatio = 0.45f,
                    enableCore = true,
                    coreColor = Color.white,
                    coreWidthRatio = 0.3f,
                    coreAlpha = 1f,
                    edgeSoftness = 0.06f,
                    blendMode = SlashBlendMode.AlphaBlend,
                    glowBlur = 14f,
                    glowColor = Hex("#38bdf8"),
                    glowIntensity = 1.0f,
                    patternType = SlashPatternType.None,
                    patternIntensity = 0.55f,
                    patternScale = 1.0f,
                    enableToonOutline = false,
                    enablePixelArt = false,
                    enablePhantom = false
                };
            }

            switch (presetName)
            {
                case "WideSlash":
                    asset.path = BuildCubicWeb512(new Vector2(80, 400), new Vector2(160, 90), new Vector2(360, 80), new Vector2(450, 360), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 8f;
                        asset.width.maxWidth = 80f;
                        asset.playback.frameCount = 14;
                        asset.playback.tailLength = 0.55f;
                        asset.appearance.colorGradient = TwoColorGradient("#f97316", "#ef4444");
                        asset.appearance.coreColor = Hex("#fffbeb");
                        asset.appearance.coreWidthRatio = 0.25f;
                        asset.appearance.glowBlur = 20f;
                        asset.appearance.glowColor = Hex("#ea580c");
                    }
                    break;

                case "Crescent":
                    asset.path = BuildCubicWeb512(new Vector2(100, 360), new Vector2(220, 140), new Vector2(300, 140), new Vector2(420, 360), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 2f;
                        asset.width.maxWidth = 50f;
                        asset.playback.frameCount = 10;
                        asset.playback.fps = 20f;
                        asset.playback.mode = RibbonMode.Full;
                        asset.playback.tailLength = 1f;
                        asset.appearance.colorGradient = TwoColorGradient("#a855f7", "#ec4899");
                        asset.appearance.enableTailAlphaFade = false;
                        asset.appearance.coreWidthRatio = 0.2f;
                        asset.appearance.glowBlur = 16f;
                        asset.appearance.glowColor = Hex("#9333ea");
                    }
                    break;

                case "StraightSlash":
                    asset.path = BuildCubicWeb512(new Vector2(60, 320), new Vector2(180, 270), new Vector2(320, 230), new Vector2(460, 180), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 6f;
                        asset.width.maxWidth = 32f;
                        asset.playback.frameCount = 10;
                        asset.playback.tailLength = 0.4f;
                        asset.appearance.colorGradient = TwoColorGradient("#22c55e", "#10b981");
                        asset.appearance.coreColor = Hex("#f0fdf4");
                        asset.appearance.coreWidthRatio = 0.35f;
                        asset.appearance.glowBlur = 12f;
                        asset.appearance.glowColor = Hex("#16a34a");
                    }
                    break;

                case "RisingSlash":
                    asset.path = BuildCubicWeb512(new Vector2(120, 440), new Vector2(200, 340), new Vector2(260, 180), new Vector2(390, 70), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 5f;
                        asset.width.maxWidth = 45f;
                        asset.playback.frameCount = 12;
                        asset.appearance.colorGradient = TwoColorGradient("#eab308", "#f97316");
                        asset.appearance.glowBlur = 18f;
                        asset.appearance.glowColor = Hex("#ca8a04");
                    }
                    break;

                case "FallingSlash":
                    asset.path = BuildCubicWeb512(new Vector2(100, 80), new Vector2(220, 160), new Vector2(310, 300), new Vector2(420, 440), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 5f;
                        asset.width.maxWidth = 45f;
                        asset.playback.frameCount = 12;
                        asset.appearance.colorGradient = TwoColorGradient("#06b6d4", "#3b82f6");
                        asset.appearance.glowBlur = 15f;
                        asset.appearance.glowColor = Hex("#0891b2");
                    }
                    break;

                case "ShortTrail":
                    asset.path = BuildCubicWeb512(new Vector2(140, 360), new Vector2(220, 180), new Vector2(310, 170), new Vector2(380, 350), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 3f;
                        asset.width.maxWidth = 20f;
                        asset.playback.frameCount = 8;
                        asset.playback.tailLength = 0.25f;
                        asset.appearance.colorGradient = TwoColorGradient("#f43f5e", "#fb7185");
                        asset.appearance.coreColor = Hex("#fff1f2");
                        asset.appearance.coreWidthRatio = 0.4f;
                        asset.appearance.glowBlur = 10f;
                        asset.appearance.glowColor = Hex("#e11d48");
                    }
                    break;

                case "ExpandingSlash":
                    asset.path = BuildCubicWeb512(new Vector2(80, 360), new Vector2(190, 180), new Vector2(310, 150), new Vector2(440, 320), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 4f;
                        asset.width.maxWidth = 65f;
                        asset.width.widthCurve = CurveTaperTail();
                        asset.width.progressWidthCurve = CurveProgressCharge();
                        asset.playback.frameCount = 14;
                        asset.playback.tailLength = 0.5f;
                        asset.appearance.colorGradient = TwoColorGradient("#8b5cf6", "#ec4899");
                        asset.appearance.glowBlur = 20f;
                        asset.appearance.glowColor = Hex("#7c3aed");
                    }
                    break;

                case "DoubleSlashS":
                    asset.path = new SlashPathData
                    {
                        anchors = new List<BezierAnchorData>
                        {
                            new BezierAnchorData
                            {
                                point = FromWeb512(80, 140, w, h),
                                hasHandleIn = false,
                                handleIn = FromWeb512(80, 140, w, h),
                                hasHandleOut = true,
                                handleOut = FromWeb512(200, 80, w, h)
                            },
                            new BezierAnchorData
                            {
                                point = FromWeb512(256, 256, w, h),
                                hasHandleIn = true,
                                handleIn = FromWeb512(200, 320, w, h),
                                hasHandleOut = true,
                                handleOut = FromWeb512(312, 192, w, h)
                            },
                            new BezierAnchorData
                            {
                                point = FromWeb512(440, 360, w, h),
                                hasHandleIn = true,
                                handleIn = FromWeb512(320, 420, w, h),
                                hasHandleOut = false,
                                handleOut = FromWeb512(440, 360, w, h)
                            }
                        }
                    };
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 4f;
                        asset.width.maxWidth = 42f;
                        asset.playback.frameCount = 16;
                        asset.playback.tailLength = 0.4f;
                        asset.appearance.colorGradient = TwoColorGradient("#06b6d4", "#3b82f6");
                        asset.appearance.glowBlur = 16f;
                        asset.appearance.glowColor = Hex("#0ea5e9");
                    }
                    break;

                case "CrossSlashX":
                    asset.path = BuildCubicWeb512(new Vector2(100, 120), new Vector2(200, 220), new Vector2(312, 292), new Vector2(412, 392), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 4f;
                        asset.width.maxWidth = 38f;
                        asset.playback.frameCount = 16;
                        asset.playback.comboSyncMode = ComboSyncMode.SyncEnd;
                        asset.appearance.colorGradient = TwoColorGradient("#38bdf8", "#6366f1");
                        asset.appearance.glowBlur = 16f;
                        asset.appearance.glowColor = Hex("#38bdf8");
                        asset.extraSlashes = new List<SlashSubLayerData>
                        {
                            new SlashSubLayerData
                            {
                                id = "cross_sub_1",
                                name = "Cross Counter-Slash",
                                enabled = true,
                                delayFrames = 0,
                                durationFrames = 16,
                                overrideColors = true,
                                colorStart = Hex("#f43f5e"),
                                colorEnd = Hex("#fb923c"),
                                widthScale = 0.95f,
                                path = BuildCubicWeb512(new Vector2(412, 120), new Vector2(312, 220), new Vector2(200, 292), new Vector2(100, 392), w, h)
                            }
                        };
                    }
                    break;

                case "WhirlwindSpin":
                    asset.path = PolyBezierUtility.GenerateSpinSlashPath(
                        new Vector2(w * 0.5f, h * 0.5f),
                        Mathf.Min(w, h) * 0.293f,
                        Mathf.Min(w, h) * 0.293f,
                        startAngleDeg: 90f,
                        sweepAngleDeg: 360f,
                        flattenY: 1f,
                        clockwise: true
                    );
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 4f;
                        asset.width.maxWidth = 40f;
                        asset.playback.frameCount = 18;
                        asset.playback.tailLength = 0.55f;
                        asset.appearance.colorGradient = TwoColorGradient("#10b981", "#06b6d4");
                        asset.appearance.blendMode = SlashBlendMode.Additive;
                        asset.appearance.glowBlur = 18f;
                        asset.appearance.glowColor = Hex("#34d399");
                        asset.appearance.patternType = SlashPatternType.Wind;
                        asset.appearance.patternIntensity = 0.65f;
                    }
                    break;

                case "ThinArc":
                default:
                    asset.path = BuildCubicWeb512(new Vector2(90, 380), new Vector2(180, 120), new Vector2(340, 110), new Vector2(440, 350), w, h);
                    if (!pathOnly)
                    {
                        asset.width.minWidth = 4f;
                        asset.width.maxWidth = 24f;
                        asset.playback.frameCount = 12;
                        asset.playback.tailLength = 0.45f;
                    }
                    break;
            }
        }
    }
}
