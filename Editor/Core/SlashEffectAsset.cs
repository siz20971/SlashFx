using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ympp.SlashEffectStudio.Editor
{
    public enum RibbonMode
    {
        Progressive,
        Full
    }

    public enum FinishMode
    {
        Retract,
        Fade
    }

    public enum ComboSyncMode
    {
        Sequential,
        SyncEnd
    }

    public enum SlashBlendMode
    {
        AlphaBlend,
        Additive
    }

    public enum SlashPatternType
    {
        None,
        Wind,
        Fire,
        Lightning
    }

    public enum SheetPackMode
    {
        UniformGrid,
        TightTrim
    }

    public enum SheetPotMode
    {
        None,
        ScaleCellsToPot,
        PadCanvasToPot
    }

    public enum AlphaOutputMode
    {
        StandardAlpha,
        PremultipliedAlpha,
        AdditiveBlackBg,
        GrayscaleMask
    }

    public enum ParticleSheetMode
    {
        Grid,
        Sprites
    }

    public enum ParticlePlaneRenderMode
    {
        MeshQuadXY,
        Billboard
    }

    [Serializable]
    public struct BezierAnchorData
    {
        public Vector2 point;
        public bool hasHandleIn;
        public Vector2 handleIn;
        public bool hasHandleOut;
        public Vector2 handleOut;

        public BezierAnchorData(Vector2 pt)
        {
            point = pt;
            hasHandleIn = false;
            handleIn = pt;
            hasHandleOut = false;
            handleOut = pt;
        }
    }

    [Serializable]
    public class SlashPathData
    {
        public List<BezierAnchorData> anchors = new List<BezierAnchorData>();

        public SlashPathData Clone()
        {
            var copy = new SlashPathData();
            if (anchors != null)
            {
                copy.anchors = new List<BezierAnchorData>(anchors);
            }
            return copy;
        }
    }

    [Serializable]
    public class SlashWidthData
    {
        [Min(0f)] public float minWidth = 4f;
        [Min(1f)] public float maxWidth = 36f;
        public bool symmetric = true;

        [Tooltip("When asymmetric, controls inner-side thickness ratio relative to outer blade edge (0.05 ~ 1.0).")]
        [Range(0.05f, 1f)] public float innerWidthRatio = 0.35f;

        [Tooltip("Head reference width multiplier across slash progress (0=Start ~ 1=End).")]
        public AnimationCurve progressWidthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("Ribbon cross-section profile from Tail (0) to Head (1).")]
        public AnimationCurve widthCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    [Serializable]
    public class SlashPlaybackData
    {
        [Range(2, 128)] public int frameCount = 12;
        [Range(1f, 120f)] public float fps = 24f;
        public RibbonMode mode = RibbonMode.Progressive;
        [Range(0.05f, 1f)] public float tailLength = 0.45f;
        public FinishMode finishMode = FinishMode.Retract;
        public AnimationCurve timeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public ComboSyncMode comboSyncMode = ComboSyncMode.Sequential;
        [Range(2, 128)] public int mainDurationFrames = 8;
    }

    [Serializable]
    public class SlashAppearanceData
    {
        public Gradient colorGradient = CreateDefaultGradient();
        [Range(0.05f, 1f)] public float baseAlpha = 1f;
        public bool enableTailAlphaFade = true;
        [Range(0.05f, 0.95f)] public float tailFadeRatio = 0.45f;

        [Header("Inner Core")]
        public bool enableCore = true;
        public Color coreColor = Color.white;
        [Range(0.05f, 0.8f)] public float coreWidthRatio = 0.28f;
        [Range(0.05f, 1f)] public float coreAlpha = 1f;

        [Header("Edge & Blending")]
        [Range(0f, 0.45f)] public float edgeSoftness = 0.06f;
        public SlashBlendMode blendMode = SlashBlendMode.AlphaBlend;

        [Header("Glow")]
        [Range(0f, 64f)] public float glowBlur = 14f;
        public Color glowColor = new Color(0.22f, 0.74f, 0.97f, 1f);
        [Range(0f, 3f)] public float glowIntensity = 1.0f;

        [Header("Procedural Trail Pattern")]
        public SlashPatternType patternType = SlashPatternType.None;
        [Range(0.05f, 1f)] public float patternIntensity = 0.55f;
        [Range(0.25f, 4f)] public float patternScale = 1.0f;

        [Header("Toon Outline")]
        public bool enableToonOutline = false;
        [Range(1f, 12f)] public float toonOutlineWidth = 2.5f;
        public Color toonOutlineColor = new Color(0.04f, 0.05f, 0.09f, 1f);

        [Header("Pixel Art Quantization")]
        public bool enablePixelArt = false;
        [Range(2, 16)] public int pixelArtSize = 3;
        [Range(0.01f, 0.9f)] public float pixelArtAlphaCutoff = 0.2f;

        [Header("Phantom Echo Trail")]
        public bool enablePhantom = false;
        [Range(1, 3)] public int phantomCount = 1;
        [Range(0.2f, 3f)] public float phantomDelay = 1.0f;
        [Range(0.05f, 0.9f)] public float phantomAlpha = 0.35f;
        public Color phantomColor = new Color(0.66f, 0.33f, 0.97f, 1f);
        [Range(0.4f, 1.6f)] public float phantomScale = 0.85f;

        public static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.22f, 0.74f, 0.97f, 1f), 0f),
                    new GradientColorKey(new Color(0.39f, 0.40f, 0.95f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            return g;
        }
    }

    [Serializable]
    public class SlashSubLayerData
    {
        public string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string name = "Sub Slash";
        public bool enabled = true;
        public SlashPathData path = new SlashPathData();
        [Min(0)] public int delayFrames = 0;
        [Min(2)] public int durationFrames = 8;
        public bool overrideColors = false;
        public Color colorStart = new Color(0.96f, 0.25f, 0.37f, 1f);
        public Color colorEnd = new Color(0.98f, 0.57f, 0.24f, 1f);
        [Range(0.2f, 2.5f)] public float widthScale = 1.0f;
    }

    [Serializable]
    public class SpriteSheetBakeSettings
    {
        [Header("Sheet Layout")]
        [Range(1, 32)] public int columns = 4;
        [Range(0, 32)] public int padding = 0;
        public SheetPackMode packMode = SheetPackMode.UniformGrid;
        public SheetPotMode potMode = SheetPotMode.None;
        public int maxSize = 2048;
        [Tooltip("Automatically skips completely empty leading/trailing frames (e.g. Retract start/end).")]
        public bool skipEmptyBoundaryFrames = true;
        [Tooltip("Dilates RGB colors 1px into transparent border pixels to avoid bilinear dark fringes.")]
        public bool alphaBleedDilation = true;

        [Header("Color & Unity Import")]
        public AlphaOutputMode alphaOutputMode = AlphaOutputMode.StandardAlpha;
        [Min(1f)] public float pixelsPerUnit = 100f;
        public bool disableMipMaps = true;

        [Header("XY Plane ParticleSystem Generation")]
        public ParticleSheetMode particleSheetMode = ParticleSheetMode.Grid;
        public ParticlePlaneRenderMode particleRenderMode = ParticlePlaneRenderMode.MeshQuadXY;
        [Tooltip("Sets Cull Off on the generated Material so horizontal FlipX (scale.x = -1) never hides the slash.")]
        public bool doubleSidedMaterial = true;
        public bool loopParticle = false;
        public ParticleSystemStopAction stopAction = ParticleSystemStopAction.Disable;
        public string sortingLayerName = "Default";
        public int sortingOrder = 10;

        [Header("Output Paths & Hot-Rebake Links")]
        public string outputFolder = "Assets/SlashEffects/Baked";
        [HideInInspector] public string lastBakedTextureGuid;
        [HideInInspector] public string lastBakedMaterialGuid;
        [HideInInspector] public string lastBakedPrefabGuid;
    }

    /// <summary>
    /// Persistent ScriptableObject holding all authored parameters for a 2D XY-plane Slash Effect.
    /// Coordinates are stored in Y-up canvas pixel space (origin at bottom-left, +X right, +Y up).
    /// </summary>
    [CreateAssetMenu(fileName = "NewSlashEffect", menuName = "Slash Effect Studio/Slash Effect Asset", order = 100)]
    public class SlashEffectAsset : ScriptableObject
    {
        public int version = 1;
        public string effectName = "ThinArc";
        [Range(32, 2048)] public int canvasWidth = 512;
        [Range(32, 2048)] public int canvasHeight = 512;

        [Tooltip("Normalized Y-up pivot (0,0 = bottom-left, 0.5,0.5 = center).")]
        public Vector2 pivot = new Vector2(0.5f, 0.5f);

        public SlashPathData path = new SlashPathData();
        public SlashWidthData width = new SlashWidthData();
        public SlashPlaybackData playback = new SlashPlaybackData();
        public SlashAppearanceData appearance = new SlashAppearanceData();
        public List<SlashSubLayerData> extraSlashes = new List<SlashSubLayerData>();
        public SpriteSheetBakeSettings bakeSettings = new SpriteSheetBakeSettings();

        private void Reset()
        {
            SlashEffectPresets.ApplyPreset(this, "ThinArc", pathOnly: false);
        }

        public void EnsureValid()
        {
            if (canvasWidth < 32) canvasWidth = 512;
            if (canvasHeight < 32) canvasHeight = 512;
            if (path == null) path = new SlashPathData();
            if (path.anchors == null || path.anchors.Count < 2)
            {
                SlashEffectPresets.ApplyPreset(this, "ThinArc", pathOnly: true);
            }
            if (width == null) width = new SlashWidthData();
            if (playback == null) playback = new SlashPlaybackData();
            if (appearance == null) appearance = new SlashAppearanceData();
            if (appearance.colorGradient == null) appearance.colorGradient = SlashAppearanceData.CreateDefaultGradient();
            if (extraSlashes == null) extraSlashes = new List<SlashSubLayerData>();
            if (bakeSettings == null) bakeSettings = new SpriteSheetBakeSettings();
        }
    }
}
