using System.Collections.Generic;
using UnityEngine;

namespace Sizzle.SlashFx.Editor.Math
{
    /// <summary>
    /// High-precision Arc-Length Lookup Table (LUT) for multi-anchor cubic Poly-Bezier splines.
    /// Guarantees truly uniform speed both across segments and within asymmetric control-handle segments.
    /// </summary>
    public sealed class PolyBezierLut
    {
        private const int SamplesPerSegment = 64;

        private readonly List<BezierAnchorData> _anchors;
        private readonly float[] _cumLengths;
        private readonly int _segmentCount;
        private readonly float _totalLength;

        public float TotalLength => _totalLength;
        public bool IsValid => _anchors != null && _anchors.Count >= 2;

        public PolyBezierLut(List<BezierAnchorData> anchors)
        {
            _anchors = anchors;
            _segmentCount = anchors != null ? Mathf.Max(0, anchors.Count - 1) : 0;

            if (_segmentCount == 0)
            {
                _cumLengths = new float[1];
                _totalLength = 0f;
                return;
            }

            int totalSamples = _segmentCount * SamplesPerSegment;
            _cumLengths = new float[totalSamples + 1];
            _cumLengths[0] = 0f;

            float accum = 0f;
            int idx = 0;

            for (int seg = 0; seg < _segmentCount; seg++)
            {
                GetSegmentPoints(seg, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);
                Vector2 prev = p0;

                for (int s = 1; s <= SamplesPerSegment; s++)
                {
                    float u = s / (float)SamplesPerSegment;
                    Vector2 curr = PolyBezierUtility.EvaluateCubic(p0, p1, p2, p3, u);
                    accum += Vector2.Distance(prev, curr);
                    idx++;
                    _cumLengths[idx] = accum;
                    prev = curr;
                }
            }

            _totalLength = accum;
        }

        private void GetSegmentPoints(int seg, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3)
        {
            var a0 = _anchors[seg];
            var a1 = _anchors[seg + 1];
            p0 = a0.point;
            p1 = a0.hasHandleOut ? a0.handleOut : p0;
            p2 = a1.hasHandleIn ? a1.handleIn : a1.point;
            p3 = a1.point;
        }

        /// <summary>
        /// Evaluates position and first derivative at normalized arc-length parameter t in [0, 1].
        /// </summary>
        public void Evaluate(float t, out Vector2 position, out Vector2 derivative)
        {
            if (_segmentCount == 0)
            {
                position = (_anchors != null && _anchors.Count > 0) ? _anchors[0].point : Vector2.zero;
                derivative = Vector2.right;
                return;
            }

            float clampedT = Mathf.Clamp01(t);

            if (_totalLength <= 1e-5f)
            {
                position = _anchors[0].point;
                derivative = Vector2.right;
                return;
            }

            float targetDist = clampedT * _totalLength;
            int totalSamples = _segmentCount * SamplesPerSegment;

            // Binary search in cumulative arc-length table
            int lo = 0;
            int hi = totalSamples;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cumLengths[mid] < targetDist)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            int upperIdx = Mathf.Clamp(lo, 1, totalSamples);
            int lowerIdx = upperIdx - 1;

            float segLen = _cumLengths[upperIdx] - _cumLengths[lowerIdx];
            float frac = segLen > 1e-6f ? Mathf.Clamp01((targetDist - _cumLengths[lowerIdx]) / segLen) : 0f;

            int seg = lowerIdx / SamplesPerSegment;
            int localStep = lowerIdx % SamplesPerSegment;
            float u = (localStep + frac) / SamplesPerSegment;

            GetSegmentPoints(seg, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);
            position = PolyBezierUtility.EvaluateCubic(p0, p1, p2, p3, u);
            derivative = PolyBezierUtility.EvaluateCubicDerivative(p0, p1, p2, p3, u);
        }
    }

    public static class PolyBezierUtility
    {
        public static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float mt = 1f - t;
            float mt2 = mt * mt;
            float mt3 = mt2 * mt;
            float t2 = t * t;
            float t3 = t2 * t;

            return mt3 * p0 + 3f * mt2 * t * p1 + 3f * mt * t2 * p2 + t3 * p3;
        }

        public static Vector2 EvaluateCubicDerivative(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float mt = 1f - t;
            float mt2 = mt * mt;
            float t2 = t * t;

            return 3f * mt2 * (p1 - p0) + 6f * mt * t * (p2 - p1) + 3f * t2 * (p3 - p2);
        }

        public static void GetTangentAndNormal(Vector2 derivative, Vector2 fallbackNormal, out Vector2 tangent, out Vector2 normal)
        {
            float len = derivative.magnitude;
            if (len < 1e-5f)
            {
                normal = fallbackNormal.sqrMagnitude > 1e-6f ? fallbackNormal.normalized : Vector2.up;
                tangent = new Vector2(normal.y, -normal.x);
                return;
            }

            tangent = derivative / len;
            // +90 degrees CCW in XY plane
            normal = new Vector2(-tangent.y, tangent.x);
        }

        /// <summary>
        /// Procedurally generates 360~720 degree circular, elliptical, or spiral spin slash paths in Y-up XY space.
        /// </summary>
        public static SlashPathData GenerateSpinSlashPath(
            Vector2 center,
            float startRadius,
            float endRadius,
            float startAngleDeg = 90f,
            float sweepAngleDeg = 360f,
            float flattenY = 1.0f,
            bool clockwise = true)
        {
            float totalSweepRad = sweepAngleDeg * Mathf.Deg2Rad;
            float startRad = startAngleDeg * Mathf.Deg2Rad;
            // In Y-up coordinates, clockwise decreases angle
            float dir = clockwise ? -1f : 1f;

            int segmentCount = Mathf.Max(3, Mathf.CeilToInt(Mathf.Abs(sweepAngleDeg) / 90f));
            float stepRad = totalSweepRad / segmentCount;
            float handleRatio = (4f / 3f) * Mathf.Tan(stepRad / 4f);

            var path = new SlashPathData();
            for (int i = 0; i <= segmentCount; i++)
            {
                float frac = i / (float)segmentCount;
                float angle = startRad + dir * stepRad * i;
                float r = Mathf.Lerp(startRadius, endRadius, frac);

                float px = center.x + Mathf.Cos(angle) * r;
                float py = center.y + Mathf.Sin(angle) * r * flattenY;

                float tx = -Mathf.Sin(angle) * dir;
                float ty = Mathf.Cos(angle) * dir * flattenY;
                float handleLen = r * handleRatio;

                var anchor = new BezierAnchorData
                {
                    point = new Vector2(Mathf.Round(px), Mathf.Round(py)),
                    hasHandleIn = i > 0,
                    handleIn = new Vector2(Mathf.Round(px - tx * handleLen), Mathf.Round(py - ty * handleLen)),
                    hasHandleOut = i < segmentCount,
                    handleOut = new Vector2(Mathf.Round(px + tx * handleLen), Mathf.Round(py + ty * handleLen))
                };

                path.anchors.Add(anchor);
            }

            return path;
        }

        public static void FlipHorizontal(SlashPathData path, float canvasWidth)
        {
            if (path == null || path.anchors == null) return;
            for (int i = 0; i < path.anchors.Count; i++)
            {
                var a = path.anchors[i];
                a.point.x = canvasWidth - a.point.x;
                if (a.hasHandleIn) a.handleIn.x = canvasWidth - a.handleIn.x;
                if (a.hasHandleOut) a.handleOut.x = canvasWidth - a.handleOut.x;
                path.anchors[i] = a;
            }
        }

        public static void FlipVertical(SlashPathData path, float canvasHeight)
        {
            if (path == null || path.anchors == null) return;
            for (int i = 0; i < path.anchors.Count; i++)
            {
                var a = path.anchors[i];
                a.point.y = canvasHeight - a.point.y;
                if (a.hasHandleIn) a.handleIn.y = canvasHeight - a.handleIn.y;
                if (a.hasHandleOut) a.handleOut.y = canvasHeight - a.handleOut.y;
                path.anchors[i] = a;
            }
        }

        public static void RotateAroundPivot(SlashPathData path, Vector2 pivotPx, float degreesCcw)
        {
            if (path == null || path.anchors == null) return;
            float rad = degreesCcw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            Vector2 Rot(Vector2 pt)
            {
                Vector2 d = pt - pivotPx;
                return new Vector2(
                    Mathf.Round(pivotPx.x + d.x * cos - d.y * sin),
                    Mathf.Round(pivotPx.y + d.x * sin + d.y * cos)
                );
            }

            for (int i = 0; i < path.anchors.Count; i++)
            {
                var a = path.anchors[i];
                a.point = Rot(a.point);
                if (a.hasHandleIn) a.handleIn = Rot(a.handleIn);
                if (a.hasHandleOut) a.handleOut = Rot(a.handleOut);
                path.anchors[i] = a;
            }
        }

        public static void ScalePath(SlashPathData path, Vector2 scale)
        {
            if (path == null || path.anchors == null) return;
            for (int i = 0; i < path.anchors.Count; i++)
            {
                var a = path.anchors[i];
                a.point = Vector2.Scale(a.point, scale);
                if (a.hasHandleIn) a.handleIn = Vector2.Scale(a.handleIn, scale);
                if (a.hasHandleOut) a.handleOut = Vector2.Scale(a.handleOut, scale);
                path.anchors[i] = a;
            }
        }
    }
}
