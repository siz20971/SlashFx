using System.Collections.Generic;
using UnityEngine;

namespace Ympp.SlashEffectStudio.Editor.Math
{
    public struct RibbonPoint
    {
        public float t;          // Global arc-length t in [0, 1]
        public float localU;     // Local tail->head [0, 1]
        public Vector2 center;
        public Vector2 left;
        public Vector2 right;
        public float width;
    }

    public struct EvaluatedFrame
    {
        public List<RibbonPoint> points;
        public float alpha;
        public bool isVisible;
        public float tStart;
        public float tEnd;
        public float ribbonPixelLength;
    }

    /// <summary>
    /// Evaluates Dual-Curve slash ribbons and builds UV-mapped triangle-strip Meshes for GPU rendering.
    /// UV0.x = Tail (0) -> Head (1), UV0.y = Left Edge (0) -> Center (0.5) -> Right Edge (1).
    /// UV1.x = Local ribbon pixel width, UV1.y = Total visible ribbon pixel length.
    /// </summary>
    public static class SlashRibbonEvaluator
    {
        public static EvaluatedFrame EvaluateFrame(
            SlashPathData path,
            SlashWidthData width,
            SlashPlaybackData playback,
            float frameIndex,
            int totalFramesOverride = -1,
            float widthScale = 1f,
            int sampleSteps = 96)
        {
            var result = new EvaluatedFrame
            {
                points = new List<RibbonPoint>(sampleSteps + 1),
                alpha = 0f,
                isVisible = false,
                tStart = 0f,
                tEnd = 0f,
                ribbonPixelLength = 0f
            };

            if (path == null || path.anchors == null || path.anchors.Count < 2)
                return result;

            int frameCount = Mathf.Max(1, totalFramesOverride > 0 ? totalFramesOverride : playback.frameCount);
            float u = frameCount <= 1 ? 0f : Mathf.Clamp01(frameIndex / (frameCount - 1f));

            float progress = MonotoneCurveUtility.EvaluateClamped(playback.timeCurve, u, u);
            float alpha = Mathf.Clamp01(MonotoneCurveUtility.EvaluateClamped(playback.fadeCurve, u, 1f));
            float tailLength = Mathf.Clamp(playback.tailLength, 0.05f, 1f);

            float tStart = 0f;
            float tEnd = 1f;
            float currentAlpha = alpha;
            float finishWidthMultiplier = 1f;

            if (playback.mode == RibbonMode.Progressive)
            {
                if (playback.finishMode == FinishMode.Retract)
                {
                    float totalVirtualSpan = 1f + tailLength;
                    float vProgress = progress * totalVirtualSpan;
                    tEnd = Mathf.Min(1f, vProgress);
                    tStart = Mathf.Max(0f, vProgress - tailLength);

                    if (tEnd <= 0.001f || tStart >= 0.999f || (tEnd - tStart) <= 0.001f)
                    {
                        return result;
                    }
                }
                else
                {
                    const float travelPortion = 0.75f;
                    if (progress < travelPortion)
                    {
                        float pNorm = progress / travelPortion;
                        tEnd = pNorm;
                        tStart = Mathf.Max(0f, tEnd - tailLength);
                    }
                    else
                    {
                        tEnd = 1f;
                        tStart = Mathf.Max(0f, 1f - tailLength);
                        float dissolveFrac = (progress - travelPortion) / (1f - travelPortion);
                        currentAlpha *= Mathf.Max(0f, 1f - dissolveFrac);
                        finishWidthMultiplier = Mathf.Max(0.05f, 1f - dissolveFrac * 0.8f);
                    }

                    if (tEnd <= 0.001f || (tEnd - tStart) <= 0.001f || currentAlpha <= 0.005f)
                    {
                        return result;
                    }
                }
            }

            var lut = new PolyBezierLut(path.anchors);
            if (!lut.IsValid) return result;

            // Dual-Curve Step 1: Progress Head Width
            float progressCurveVal = Mathf.Max(0f, MonotoneCurveUtility.EvaluateClamped(width.progressWidthCurve, progress, 1f));
            float headWidth = Mathf.Lerp(width.minWidth, width.maxWidth, progressCurveVal) * Mathf.Max(0.05f, widthScale);

            float leftRatio = width.symmetric ? 0.5f : (1f / (1f + Mathf.Clamp(width.innerWidthRatio, 0.05f, 1f)));
            float rightRatio = 1f - leftRatio;

            Vector2 lastNormal = Vector2.up;
            float visibleLength = 0f;
            Vector2 prevCenter = Vector2.zero;

            for (int s = 0; s <= sampleSteps; s++)
            {
                float frac = s / (float)sampleSteps; // 0 = Tail, 1 = Head
                float t = Mathf.Lerp(tStart, tEnd, frac);

                lut.Evaluate(t, out Vector2 center, out Vector2 deriv);
                PolyBezierUtility.GetTangentAndNormal(deriv, lastNormal, out _, out Vector2 normal);
                lastNormal = normal;

                // Dual-Curve Step 2: Tail-to-Head Shape Profile
                float shapeFactor = Mathf.Max(0f, MonotoneCurveUtility.EvaluateClamped(width.widthCurve, frac, 1f));
                float calculatedWidth = Mathf.Max(0f, headWidth * shapeFactor * finishWidthMultiplier);

                if (s > 0)
                {
                    visibleLength += Vector2.Distance(prevCenter, center);
                }
                prevCenter = center;

                result.points.Add(new RibbonPoint
                {
                    t = t,
                    localU = frac,
                    center = center,
                    left = center + normal * (calculatedWidth * leftRatio),
                    right = center - normal * (calculatedWidth * rightRatio),
                    width = calculatedWidth
                });
            }

            result.alpha = currentAlpha;
            result.tStart = tStart;
            result.tEnd = tEnd;
            result.ribbonPixelLength = Mathf.Max(1f, visibleLength);
            result.isVisible = result.points.Count >= 2 && currentAlpha > 0.001f;
            return result;
        }

        /// <summary>
        /// Builds a 2D ribbon Mesh in pixel coordinates [0..canvasWidth, 0..canvasHeight].
        /// Uses 3 vertices per cross-section (Left, Center, Right) so center line stays crisp on tight curves.
        /// </summary>
        public static void BuildRibbonMesh(
            Mesh targetMesh,
            in EvaluatedFrame frame,
            float widthMultiplier = 1f,
            float outlineExpandPx = 0f)
        {
            targetMesh.Clear();
            if (!frame.isVisible || frame.points == null || frame.points.Count < 2) return;

            var pts = frame.points;
            int count = pts.Count;

            // 2 vertices per sample (Left & Right) gives clean quad strip with exact linear V interpolation 0..1
            int vertCount = count * 2;
            int triCount = (count - 1) * 6;

            var vertices = new Vector3[vertCount];
            var uv0 = new Vector2[vertCount];
            var uv1 = new Vector2[vertCount];
            var triangles = new int[triCount];

            float totalLen = Mathf.Max(1f, frame.ribbonPixelLength);

            for (int i = 0; i < count; i++)
            {
                var p = pts[i];
                Vector2 leftVec = (p.left - p.center) * widthMultiplier;
                Vector2 rightVec = (p.right - p.center) * widthMultiplier;

                if (outlineExpandPx > 0f)
                {
                    if (leftVec.sqrMagnitude > 1e-5f) leftVec += leftVec.normalized * outlineExpandPx;
                    if (rightVec.sqrMagnitude > 1e-5f) rightVec += rightVec.normalized * outlineExpandPx;
                }

                Vector2 leftPos = p.center + leftVec;
                Vector2 rightPos = p.center + rightVec;
                float effectiveWidth = Mathf.Max(0.5f, (leftPos - rightPos).magnitude);

                int vIdx = i * 2;
                vertices[vIdx] = new Vector3(leftPos.x, leftPos.y, 0f);
                vertices[vIdx + 1] = new Vector3(rightPos.x, rightPos.y, 0f);

                uv0[vIdx] = new Vector2(p.localU, 0f);
                uv0[vIdx + 1] = new Vector2(p.localU, 1f);

                uv1[vIdx] = new Vector2(effectiveWidth, totalLen);
                uv1[vIdx + 1] = new Vector2(effectiveWidth, totalLen);

                if (i < count - 1)
                {
                    int tIdx = i * 6;
                    int next = vIdx + 2;

                    triangles[tIdx] = vIdx;
                    triangles[tIdx + 1] = next;
                    triangles[tIdx + 2] = vIdx + 1;

                    triangles[tIdx + 3] = vIdx + 1;
                    triangles[tIdx + 4] = next;
                    triangles[tIdx + 5] = next + 1;
                }
            }

            targetMesh.vertices = vertices;
            targetMesh.uv = uv0;
            targetMesh.uv2 = uv1;
            targetMesh.triangles = triangles;
            targetMesh.RecalculateBounds();
        }
    }
}
