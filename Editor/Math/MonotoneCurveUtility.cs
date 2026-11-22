using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ympp.SlashEffectStudio.Editor.Math
{
    [Serializable]
    public struct CurveKeyPair
    {
        public float t;
        public float v;

        public CurveKeyPair(float t, float v)
        {
            this.t = t;
            this.v = v;
        }
    }

    /// <summary>
    /// Converts (t, v) keyframes to/from Unity AnimationCurves using exact Fritsch-Carlson
    /// Monotone Cubic Hermite Spline tangents (matching the web SlashEffectStudio math engine).
    /// </summary>
    public static class MonotoneCurveUtility
    {
        public static AnimationCurve BuildMonotoneCurve(IReadOnlyList<CurveKeyPair> keyframes, bool linear = false)
        {
            if (keyframes == null || keyframes.Count == 0)
            {
                return AnimationCurve.Linear(0f, 1f, 1f, 1f);
            }

            if (keyframes.Count == 1)
            {
                return new AnimationCurve(new Keyframe(keyframes[0].t, keyframes[0].v));
            }

            var sorted = new List<CurveKeyPair>(keyframes);
            sorted.Sort((a, b) => a.t.CompareTo(b.t));

            int n = sorted.Count;
            var d = new float[n - 1];
            var m = new float[n];

            // 1. Secant slopes
            for (int i = 0; i < n - 1; i++)
            {
                float h = sorted[i + 1].t - sorted[i].t;
                d[i] = h <= 1e-6f ? 0f : (sorted[i + 1].v - sorted[i].v) / h;
            }

            if (linear)
            {
                var linKeys = new Keyframe[n];
                for (int i = 0; i < n; i++)
                {
                    float inTan = i > 0 ? d[i - 1] : d[0];
                    float outTan = i < n - 1 ? d[i] : d[n - 2];
                    linKeys[i] = new Keyframe(sorted[i].t, sorted[i].v, inTan, outTan);
                }
                return new AnimationCurve(linKeys);
            }

            // 2. Initial tangents
            m[0] = d[0];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0f)
                {
                    m[i] = 0f;
                }
                else
                {
                    m[i] = 0.5f * (d[i - 1] + d[i]);
                }
            }
            m[n - 1] = d[n - 2];

            // 3. Fritsch-Carlson monotonicity clamp
            for (int i = 0; i < n - 1; i++)
            {
                if (Mathf.Approximately(d[i], 0f))
                {
                    m[i] = 0f;
                    m[i + 1] = 0f;
                }
                else
                {
                    float a = m[i] / d[i];
                    float b = m[i + 1] / d[i];
                    if (a < 0f) m[i] = 0f;
                    if (b < 0f) m[i + 1] = 0f;
                    float s = a * a + b * b;
                    if (s > 9f)
                    {
                        float tau = 3f / Mathf.Sqrt(s);
                        m[i] = tau * a * d[i];
                        m[i + 1] = tau * b * d[i];
                    }
                }
            }

            // Unity AnimationCurve evaluates cubic Hermite splines natively using exact (inTangent, outTangent) slopes.
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = new Keyframe(sorted[i].t, sorted[i].v, m[i], m[i]);
            }

            return new AnimationCurve(keys);
        }

        public static List<CurveKeyPair> ExtractKeyframes(AnimationCurve curve)
        {
            var list = new List<CurveKeyPair>();
            if (curve == null || curve.length == 0)
            {
                list.Add(new CurveKeyPair(0f, 1f));
                list.Add(new CurveKeyPair(1f, 1f));
                return list;
            }

            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                list.Add(new CurveKeyPair(
                    Mathf.Clamp01(keys[i].time),
                    Mathf.Clamp01(keys[i].value)
                ));
            }
            return list;
        }

        public static float EvaluateClamped(AnimationCurve curve, float t, float fallback = 1f)
        {
            if (curve == null || curve.length == 0) return fallback;
            return curve.Evaluate(Mathf.Clamp01(t));
        }
    }
}
