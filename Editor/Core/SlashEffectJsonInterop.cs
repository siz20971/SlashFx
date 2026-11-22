using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Sizzle.SlashFx.Editor.Math;

namespace Sizzle.SlashFx.Editor
{
    /// <summary>
    /// Bidirectional JSON serializer compatible with the web SlashEffectStudio and SlashEffectCli (.slash.json).
    /// Handles automatic Y-down (Web Canvas) <-> Y-up (Unity XY) coordinate conversion.
    /// </summary>
    public static class SlashEffectJsonInterop
    {
        [Serializable]
        private class JsonVec2
        {
            public float x;
            public float y;
        }

        [Serializable]
        private class JsonCurveKey
        {
            public float t;
            public float v;
        }

        [Serializable]
        private class JsonColorStop
        {
            public float offset;
            public string color;
        }

        [Serializable]
        private class JsonBezierAnchor
        {
            public JsonVec2 point;
            public JsonVec2 handleIn;
            public JsonVec2 handleOut;
        }

        [Serializable]
        private class JsonPath
        {
            public JsonVec2 p0;
            public JsonVec2 p1;
            public JsonVec2 p2;
            public JsonVec2 p3;
            public List<JsonBezierAnchor> anchors;
        }

        [Serializable]
        private class JsonWidth
        {
            public float minWidth = 4f;
            public float maxWidth = 32f;
            public bool symmetric = true;
            public List<JsonCurveKey> widthCurve;
            public List<JsonCurveKey> progressWidthCurve;
            public string interpolation = "monotone";
            public string progressInterpolation = "monotone";
        }

        [Serializable]
        private class JsonPlayback
        {
            public int frameCount = 12;
            public float fps = 24f;
            public string mode = "progressive";
            public float tailLength = 0.45f;
            public List<JsonCurveKey> timeCurve;
            public List<JsonCurveKey> fadeCurve;
            public string timeInterpolation = "monotone";
            public string fadeInterpolation = "monotone";
            public string finishMode = "retract";
            public string comboSyncMode = "sequential";
            public int mainDurationFrames = 8;
        }

        [Serializable]
        private class JsonAppearance
        {
            public string colorStart = "#38bdf8";
            public string colorEnd = "#6366f1";
            public bool enableGradient = true;
            public bool useMultiGradient = false;
            public List<JsonColorStop> colorStops;
            public float baseAlpha = 1f;
            public bool enableTailAlphaFade = true;
            public float tailFadeRatio = 0.45f;
            public string coreColor = "#ffffff";
            public bool enableCore = true;
            public float coreWidthRatio = 0.3f;
            public float coreAlpha = 1f;
            public string blendMode = "source-over";
            public float glowBlur = 14f;
            public string glowColor = "#38bdf8";
            public string patternType = "none";
            public float patternIntensity = 0.5f;
            public float patternScale = 1f;
            public bool enableToonOutline = false;
            public float toonOutlineWidth = 2.5f;
            public string toonOutlineColor = "#090d16";
            public bool enablePixelArt = false;
            public int pixelArtSize = 3;
            public bool enablePhantom = false;
            public int phantomCount = 1;
            public float phantomDelay = 1f;
            public float phantomAlpha = 0.35f;
            public string phantomColor = "#a855f7";
            public float phantomScale = 0.85f;
        }

        [Serializable]
        private class JsonSubLayer
        {
            public string id;
            public string name;
            public bool enabled = true;
            public JsonPath path;
            public int delayFrames;
            public int durationFrames;
            public string colorStart;
            public string colorEnd;
            public float widthScale = 1f;
        }

        [Serializable]
        private class JsonProject
        {
            public int version = 1;
            public string name = "SlashEffect";
            public int canvasWidth = 512;
            public int canvasHeight = 512;
            public JsonVec2 pivot;
            public JsonPath path;
            public JsonWidth width;
            public JsonPlayback playback;
            public JsonAppearance appearance;
            public List<JsonSubLayer> extraSlashes;
        }

        private static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }

        private static string ToHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();
        }

        private static Vector2 FromWeb(JsonVec2 v, float canvasH)
        {
            if (v == null) return Vector2.zero;
            return new Vector2(v.x, canvasH - v.y);
        }

        private static JsonVec2 ToWeb(Vector2 v, float canvasH)
        {
            return new JsonVec2 { x = Mathf.Round(v.x * 100f) / 100f, y = Mathf.Round((canvasH - v.y) * 100f) / 100f };
        }

        private static SlashPathData ImportPath(JsonPath jp, float canvasH)
        {
            var result = new SlashPathData();
            if (jp == null) return result;

            if (jp.anchors != null && jp.anchors.Count >= 2)
            {
                for (int i = 0; i < jp.anchors.Count; i++)
                {
                    var ja = jp.anchors[i];
                    Vector2 pt = FromWeb(ja.point, canvasH);
                    // Note: JsonUtility instantiates empty objects for missing fields when zero, so check handle indices too
                    bool hasIn = ja.handleIn != null && i > 0 && (Mathf.Abs(ja.handleIn.x) > 1e-4f || Mathf.Abs(ja.handleIn.y) > 1e-4f);
                    bool hasOut = ja.handleOut != null && i < jp.anchors.Count - 1 && (Mathf.Abs(ja.handleOut.x) > 1e-4f || Mathf.Abs(ja.handleOut.y) > 1e-4f);
                    result.anchors.Add(new BezierAnchorData
                    {
                        point = pt,
                        hasHandleIn = hasIn,
                        handleIn = hasIn ? FromWeb(ja.handleIn, canvasH) : pt,
                        hasHandleOut = hasOut,
                        handleOut = hasOut ? FromWeb(ja.handleOut, canvasH) : pt
                    });
                }
            }
            else if (jp.p0 != null && jp.p3 != null)
            {
                Vector2 p0 = FromWeb(jp.p0, canvasH);
                Vector2 p1 = jp.p1 != null ? FromWeb(jp.p1, canvasH) : p0;
                Vector2 p3 = FromWeb(jp.p3, canvasH);
                Vector2 p2 = jp.p2 != null ? FromWeb(jp.p2, canvasH) : p3;

                result.anchors.Add(new BezierAnchorData { point = p0, hasHandleIn = false, handleIn = p0, hasHandleOut = true, handleOut = p1 });
                result.anchors.Add(new BezierAnchorData { point = p3, hasHandleIn = true, handleIn = p2, hasHandleOut = false, handleOut = p3 });
            }

            return result;
        }

        private static JsonPath ExportPath(SlashPathData path, float canvasH)
        {
            var jp = new JsonPath { anchors = new List<JsonBezierAnchor>() };
            if (path == null || path.anchors == null || path.anchors.Count < 2) return jp;

            int last = path.anchors.Count - 1;
            var first = path.anchors[0];
            var end = path.anchors[last];

            jp.p0 = ToWeb(first.point, canvasH);
            jp.p1 = ToWeb(first.hasHandleOut ? first.handleOut : first.point, canvasH);
            jp.p2 = ToWeb(end.hasHandleIn ? end.handleIn : end.point, canvasH);
            jp.p3 = ToWeb(end.point, canvasH);

            for (int i = 0; i < path.anchors.Count; i++)
            {
                var a = path.anchors[i];
                jp.anchors.Add(new JsonBezierAnchor
                {
                    point = ToWeb(a.point, canvasH),
                    handleIn = a.hasHandleIn ? ToWeb(a.handleIn, canvasH) : null,
                    handleOut = a.hasHandleOut ? ToWeb(a.handleOut, canvasH) : null
                });
            }

            return jp;
        }

        private static AnimationCurve ImportCurve(List<JsonCurveKey> keys, string interp, AnimationCurve fallback)
        {
            if (keys == null || keys.Count == 0) return fallback;
            var list = new List<CurveKeyPair>(keys.Count);
            foreach (var k in keys) list.Add(new CurveKeyPair(k.t, k.v));
            return MonotoneCurveUtility.BuildMonotoneCurve(list, linear: interp == "linear");
        }

        private static List<JsonCurveKey> ExportCurve(AnimationCurve curve)
        {
            var pairs = MonotoneCurveUtility.ExtractKeyframes(curve);
            var list = new List<JsonCurveKey>(pairs.Count);
            foreach (var p in pairs) list.Add(new JsonCurveKey { t = p.t, v = p.v });
            return list;
        }

        public static bool ImportFromJsonFile(SlashEffectAsset target, string filePath)
        {
            if (target == null || string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
            string json = File.ReadAllText(filePath);
            return ImportFromJsonString(target, json);
        }

        public static bool ImportFromJsonString(SlashEffectAsset target, string json)
        {
            if (target == null || string.IsNullOrEmpty(json)) return false;
            var jp = JsonUtility.FromJson<JsonProject>(json);
            if (jp == null) return false;

            target.version = jp.version > 0 ? jp.version : 1;
            target.effectName = string.IsNullOrEmpty(jp.name) ? target.name : jp.name;
            target.canvasWidth = Mathf.Clamp(jp.canvasWidth > 0 ? jp.canvasWidth : 512, 32, 2048);
            target.canvasHeight = Mathf.Clamp(jp.canvasHeight > 0 ? jp.canvasHeight : 512, 32, 2048);

            if (jp.pivot != null)
            {
                target.pivot = new Vector2(Mathf.Clamp01(jp.pivot.x), Mathf.Clamp01(1f - jp.pivot.y));
            }
            else
            {
                target.pivot = new Vector2(0.5f, 0.5f);
            }

            target.path = ImportPath(jp.path, target.canvasHeight);

            if (jp.width != null)
            {
                target.width.minWidth = jp.width.minWidth;
                target.width.maxWidth = Mathf.Max(1f, jp.width.maxWidth);
                target.width.symmetric = jp.width.symmetric;
                target.width.widthCurve = ImportCurve(jp.width.widthCurve, jp.width.interpolation, SlashEffectPresets.CurveCometSlash());
                target.width.progressWidthCurve = ImportCurve(jp.width.progressWidthCurve, jp.width.progressInterpolation, SlashEffectPresets.CurveProgressImpact());
            }

            if (jp.playback != null)
            {
                target.playback.frameCount = Mathf.Clamp(jp.playback.frameCount > 0 ? jp.playback.frameCount : 12, 2, 128);
                target.playback.fps = Mathf.Clamp(jp.playback.fps > 0 ? jp.playback.fps : 24f, 1f, 120f);
                target.playback.mode = jp.playback.mode == "full" ? RibbonMode.Full : RibbonMode.Progressive;
                target.playback.tailLength = Mathf.Clamp(jp.playback.tailLength > 0 ? jp.playback.tailLength : 0.45f, 0.05f, 1f);
                target.playback.finishMode = jp.playback.finishMode == "fade" ? FinishMode.Fade : FinishMode.Retract;
                target.playback.comboSyncMode = jp.playback.comboSyncMode == "sync_end" ? ComboSyncMode.SyncEnd : ComboSyncMode.Sequential;
                target.playback.mainDurationFrames = jp.playback.mainDurationFrames > 0 ? jp.playback.mainDurationFrames : 8;
                target.playback.timeCurve = ImportCurve(jp.playback.timeCurve, jp.playback.timeInterpolation, SlashEffectPresets.CurveEaseInOut());
                target.playback.fadeCurve = ImportCurve(jp.playback.fadeCurve, jp.playback.fadeInterpolation, SlashEffectPresets.CurveLinearFadeOut());
            }

            if (jp.appearance != null)
            {
                var app = target.appearance;
                var grad = new Gradient();
                if (jp.appearance.useMultiGradient && jp.appearance.colorStops != null && jp.appearance.colorStops.Count >= 2)
                {
                    int stopCount = Mathf.Min(8, jp.appearance.colorStops.Count);
                    var colorKeys = new GradientColorKey[stopCount];
                    for (int i = 0; i < stopCount; i++)
                    {
                        colorKeys[i] = new GradientColorKey(
                            ParseHex(jp.appearance.colorStops[i].color, Color.white),
                            Mathf.Clamp01(jp.appearance.colorStops[i].offset));
                    }
                    grad.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                }
                else
                {
                    Color cStart = ParseHex(jp.appearance.colorStart, new Color(0.22f, 0.74f, 0.97f));
                    Color cEnd = jp.appearance.enableGradient ? ParseHex(jp.appearance.colorEnd, new Color(0.39f, 0.40f, 0.95f)) : cStart;
                    grad.SetKeys(
                        new[] { new GradientColorKey(cStart, 0f), new GradientColorKey(cEnd, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                    );
                }
                app.colorGradient = grad;
                app.baseAlpha = Mathf.Clamp(jp.appearance.baseAlpha > 0 ? jp.appearance.baseAlpha : 1f, 0.05f, 1f);
                app.enableTailAlphaFade = jp.appearance.enableTailAlphaFade;
                app.tailFadeRatio = Mathf.Clamp(jp.appearance.tailFadeRatio > 0 ? jp.appearance.tailFadeRatio : 0.45f, 0.05f, 0.95f);
                app.enableCore = jp.appearance.enableCore;
                app.coreColor = ParseHex(jp.appearance.coreColor, Color.white);
                app.coreWidthRatio = Mathf.Clamp(jp.appearance.coreWidthRatio > 0 ? jp.appearance.coreWidthRatio : 0.28f, 0.05f, 0.8f);
                app.coreAlpha = Mathf.Clamp(jp.appearance.coreAlpha > 0 ? jp.appearance.coreAlpha : 1f, 0.05f, 1f);
                app.blendMode = jp.appearance.blendMode == "lighter" ? SlashBlendMode.Additive : SlashBlendMode.AlphaBlend;
                app.glowBlur = Mathf.Clamp(jp.appearance.glowBlur, 0f, 64f);
                app.glowColor = ParseHex(jp.appearance.glowColor, Color.cyan);
                app.patternType = jp.appearance.patternType switch
                {
                    "wind" => SlashPatternType.Wind,
                    "fire" => SlashPatternType.Fire,
                    "lightning" => SlashPatternType.Lightning,
                    _ => SlashPatternType.None
                };
                app.patternIntensity = Mathf.Clamp(jp.appearance.patternIntensity > 0 ? jp.appearance.patternIntensity : 0.5f, 0.05f, 1f);
                app.patternScale = Mathf.Clamp(jp.appearance.patternScale > 0 ? jp.appearance.patternScale : 1f, 0.25f, 4f);
                app.enableToonOutline = jp.appearance.enableToonOutline;
                app.toonOutlineWidth = Mathf.Clamp(jp.appearance.toonOutlineWidth > 0 ? jp.appearance.toonOutlineWidth : 2.5f, 1f, 12f);
                app.toonOutlineColor = ParseHex(jp.appearance.toonOutlineColor, new Color(0.04f, 0.05f, 0.09f));
                app.enablePixelArt = jp.appearance.enablePixelArt;
                app.pixelArtSize = Mathf.Clamp(jp.appearance.pixelArtSize > 1 ? jp.appearance.pixelArtSize : 3, 2, 16);
                app.enablePhantom = jp.appearance.enablePhantom;
                app.phantomCount = Mathf.Clamp(jp.appearance.phantomCount > 0 ? jp.appearance.phantomCount : 1, 1, 3);
                app.phantomDelay = Mathf.Clamp(jp.appearance.phantomDelay > 0 ? jp.appearance.phantomDelay : 1f, 0.2f, 3f);
                app.phantomAlpha = Mathf.Clamp(jp.appearance.phantomAlpha > 0 ? jp.appearance.phantomAlpha : 0.35f, 0.05f, 0.9f);
                app.phantomColor = ParseHex(jp.appearance.phantomColor, new Color(0.66f, 0.33f, 0.97f));
                app.phantomScale = Mathf.Clamp(jp.appearance.phantomScale > 0 ? jp.appearance.phantomScale : 0.85f, 0.4f, 1.6f);
            }

            target.extraSlashes = new List<SlashSubLayerData>();
            if (jp.extraSlashes != null)
            {
                foreach (var sub in jp.extraSlashes)
                {
                    if (sub == null) continue;
                    bool hasColor = !string.IsNullOrEmpty(sub.colorStart) || !string.IsNullOrEmpty(sub.colorEnd);
                    target.extraSlashes.Add(new SlashSubLayerData
                    {
                        id = string.IsNullOrEmpty(sub.id) ? Guid.NewGuid().ToString("N").Substring(0, 8) : sub.id,
                        name = string.IsNullOrEmpty(sub.name) ? "Sub Slash" : sub.name,
                        enabled = sub.enabled,
                        path = ImportPath(sub.path, target.canvasHeight),
                        delayFrames = Mathf.Max(0, sub.delayFrames),
                        durationFrames = sub.durationFrames > 0 ? sub.durationFrames : target.playback.frameCount,
                        overrideColors = hasColor,
                        colorStart = ParseHex(sub.colorStart, Color.red),
                        colorEnd = ParseHex(sub.colorEnd, Color.yellow),
                        widthScale = sub.widthScale > 0 ? sub.widthScale : 1f
                    });
                }
            }

            target.EnsureValid();
            return true;
        }

        public static string ExportToJsonString(SlashEffectAsset source)
        {
            if (source == null) return "{}";
            source.EnsureValid();

            var colorKeys = source.appearance.colorGradient.colorKeys;
            bool multi = colorKeys != null && colorKeys.Length > 2;
            var stops = new List<JsonColorStop>();
            if (colorKeys != null)
            {
                foreach (var ck in colorKeys)
                {
                    stops.Add(new JsonColorStop { offset = ck.time, color = ToHex(ck.color) });
                }
            }

            var jp = new JsonProject
            {
                version = source.version,
                name = string.IsNullOrEmpty(source.effectName) ? source.name : source.effectName,
                canvasWidth = source.canvasWidth,
                canvasHeight = source.canvasHeight,
                pivot = new JsonVec2 { x = source.pivot.x, y = 1f - source.pivot.y },
                path = ExportPath(source.path, source.canvasHeight),
                width = new JsonWidth
                {
                    minWidth = source.width.minWidth,
                    maxWidth = source.width.maxWidth,
                    symmetric = source.width.symmetric,
                    widthCurve = ExportCurve(source.width.widthCurve),
                    progressWidthCurve = ExportCurve(source.width.progressWidthCurve),
                    interpolation = "monotone",
                    progressInterpolation = "monotone"
                },
                playback = new JsonPlayback
                {
                    frameCount = source.playback.frameCount,
                    fps = source.playback.fps,
                    mode = source.playback.mode == RibbonMode.Full ? "full" : "progressive",
                    tailLength = source.playback.tailLength,
                    finishMode = source.playback.finishMode == FinishMode.Fade ? "fade" : "retract",
                    comboSyncMode = source.playback.comboSyncMode == ComboSyncMode.SyncEnd ? "sync_end" : "sequential",
                    mainDurationFrames = source.playback.mainDurationFrames,
                    timeCurve = ExportCurve(source.playback.timeCurve),
                    fadeCurve = ExportCurve(source.playback.fadeCurve)
                },
                appearance = new JsonAppearance
                {
                    colorStart = ToHex(source.appearance.colorGradient.Evaluate(0f)),
                    colorEnd = ToHex(source.appearance.colorGradient.Evaluate(1f)),
                    enableGradient = true,
                    useMultiGradient = multi,
                    colorStops = stops,
                    baseAlpha = source.appearance.baseAlpha,
                    enableTailAlphaFade = source.appearance.enableTailAlphaFade,
                    tailFadeRatio = source.appearance.tailFadeRatio,
                    coreColor = ToHex(source.appearance.coreColor),
                    enableCore = source.appearance.enableCore,
                    coreWidthRatio = source.appearance.coreWidthRatio,
                    coreAlpha = source.appearance.coreAlpha,
                    blendMode = source.appearance.blendMode == SlashBlendMode.Additive ? "lighter" : "source-over",
                    glowBlur = source.appearance.glowBlur,
                    glowColor = ToHex(source.appearance.glowColor),
                    patternType = source.appearance.patternType.ToString().ToLowerInvariant(),
                    patternIntensity = source.appearance.patternIntensity,
                    patternScale = source.appearance.patternScale,
                    enableToonOutline = source.appearance.enableToonOutline,
                    toonOutlineWidth = source.appearance.toonOutlineWidth,
                    toonOutlineColor = ToHex(source.appearance.toonOutlineColor),
                    enablePixelArt = source.appearance.enablePixelArt,
                    pixelArtSize = source.appearance.pixelArtSize,
                    enablePhantom = source.appearance.enablePhantom,
                    phantomCount = source.appearance.phantomCount,
                    phantomDelay = source.appearance.phantomDelay,
                    phantomAlpha = source.appearance.phantomAlpha,
                    phantomColor = ToHex(source.appearance.phantomColor),
                    phantomScale = source.appearance.phantomScale
                },
                extraSlashes = new List<JsonSubLayer>()
            };

            if (source.extraSlashes != null)
            {
                foreach (var sub in source.extraSlashes)
                {
                    jp.extraSlashes.Add(new JsonSubLayer
                    {
                        id = sub.id,
                        name = sub.name,
                        enabled = sub.enabled,
                        path = ExportPath(sub.path, source.canvasHeight),
                        delayFrames = sub.delayFrames,
                        durationFrames = sub.durationFrames,
                        colorStart = sub.overrideColors ? ToHex(sub.colorStart) : "",
                        colorEnd = sub.overrideColors ? ToHex(sub.colorEnd) : "",
                        widthScale = sub.widthScale
                    });
                }
            }

            return JsonUtility.ToJson(jp, prettyPrint: true);
        }

        public static void ExportToJsonFile(SlashEffectAsset source, string filePath)
        {
            if (source == null || string.IsNullOrEmpty(filePath)) return;
            File.WriteAllText(filePath, ExportToJsonString(source));
        }
    }
}
