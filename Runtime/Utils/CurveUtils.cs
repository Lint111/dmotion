using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion
{
    /// <summary>
    /// Utility methods for evaluating packed Hermite spline curves.
    /// Used for transition blend curves in both runtime (Burst) and editor (managed).
    /// 
    /// This class provides identical evaluation for:
    /// - Runtime ECS jobs via BlobArray version
    /// - Editor preview via managed array version
    /// 
    /// Both versions use the same Hermite interpolation algorithm to ensure
    /// preview matches runtime behavior exactly.
    /// </summary>
    [BurstCompile]
    internal static class CurveUtils
    {
        /// <summary>
        /// Epsilon for linear curve detection. Used to identify default linear curves
        /// that can skip keyframe storage for zero-cost runtime evaluation.
        /// </summary>
        internal const float LinearCurveEpsilon = 0.001f;
        
        #region Runtime (Burst-compatible) API
        
        /// <summary>
        /// Evaluates a blend curve at the given normalized time.
        /// Returns linear t if keyframes array is empty (fast-path).
        /// </summary>
        /// <param name="keyframes">Packed keyframes from blob. Empty = linear blend.</param>
        /// <param name="t">Normalized time [0, 1] (typically toState.Time / transitionDuration)</param>
        /// <returns>Blend weight for the "To" state [0, 1]</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float EvaluateCurve(ref BlobArray<CurveKeyframe> keyframes, float t)
        {
            // Fast-path: empty keyframes = linear blend
            if (keyframes.Length == 0)
                return t;
            
            // Clamp t to valid range
            t = math.clamp(t, 0f, 1f);
            
            // Single keyframe: return constant value
            if (keyframes.Length == 1)
                return keyframes[0].Value;
            
            // Find the segment containing t
            var segmentIndex = FindSegment(ref keyframes, t);
            
            // Evaluate Hermite spline on the segment
            return EvaluateHermiteSegment(ref keyframes, segmentIndex, t);
        }
        
        /// <summary>
        /// Finds the segment index for the given time.
        /// Returns the index of the keyframe at the START of the segment containing t.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindSegment(ref BlobArray<CurveKeyframe> keyframes, float t)
        {
            // Linear search - curves typically have 2-4 keyframes
            // Binary search would be overkill and add branch misprediction overhead
            var lastIndex = keyframes.Length - 1;
            
            for (var i = 0; i < lastIndex; i++)
            {
                if (t <= keyframes[i + 1].Time)
                    return i;
            }
            
            // t is at or past the last keyframe - return last segment
            return lastIndex - 1;
        }
        
        /// <summary>
        /// Evaluates a cubic Hermite spline segment.
        /// Uses the standard Hermite basis functions:
        /// h00(s) = 2s³ - 3s² + 1
        /// h10(s) = s³ - 2s² + s
        /// h01(s) = -2s³ + 3s²
        /// h11(s) = s³ - s²
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float EvaluateHermiteSegment(ref BlobArray<CurveKeyframe> keyframes, int segmentIndex, float t)
        {
            ref var k0 = ref keyframes[segmentIndex];
            ref var k1 = ref keyframes[segmentIndex + 1];
            
            return EvaluateHermiteCore(
                k0.Time, k0.Value, k0.OutTangent,
                k1.Time, k1.Value, k1.InTangent,
                t);
        }
        
        #endregion
        
        #region Editor (Managed) API
        
        /// <summary>
        /// Evaluates a blend curve at the given normalized time (managed version for editor).
        /// Returns linear t if keyframes array is null or empty (fast-path).
        /// </summary>
        /// <param name="keyframes">Packed keyframes array. Null or empty = linear blend.</param>
        /// <param name="t">Normalized time [0, 1]</param>
        /// <returns>Blend weight for the "To" state [0, 1]</returns>
        internal static float EvaluateCurveManaged(CurveKeyframe[] keyframes, float t)
        {
            // Fast-path: null or empty keyframes = linear blend
            if (keyframes == null || keyframes.Length == 0)
                return Mathf.Clamp01(t);
            
            // Clamp t to valid range
            t = Mathf.Clamp01(t);
            
            // Single keyframe: return constant value
            if (keyframes.Length == 1)
                return keyframes[0].Value;
            
            // Find the segment containing t
            var segmentIndex = FindSegmentManaged(keyframes, t);
            
            // Evaluate Hermite spline on the segment
            return EvaluateHermiteSegmentManaged(keyframes, segmentIndex, t);
        }
        
        /// <summary>
        /// Converts a Unity AnimationCurve to packed CurveKeyframe array for preview evaluation.
        /// Returns null for linear curves (fast-path optimization).
        /// 
        /// IMPORTANT: This applies the same Y-axis inversion as blob conversion.
        /// Unity curves store "From" weight (1→0), DMotion uses "To" weight (0→1).
        /// </summary>
        /// <param name="curve">Unity AnimationCurve to convert</param>
        /// <returns>Packed keyframes array, or null if curve is linear (fast-path)</returns>
        internal static CurveKeyframe[] ConvertAnimationCurveManaged(AnimationCurve curve)
        {
            if (curve == null || IsLinearCurve(curve))
                return null;
            
            var keys = curve.keys;
            var keyframes = new CurveKeyframe[keys.Length];
            
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                // Invert Y-axis: Unity "From" weight → DMotion "To" weight
                // Also negate tangents due to Y inversion
                keyframes[i] = CurveKeyframe.Create(
                    key.time,
                    1f - key.value,
                    -key.inTangent,
                    -key.outTangent);
            }
            
            return keyframes;
        }
        
        /// <summary>
        /// Checks if a curve is effectively linear (default blend curve).
        /// Linear curves don't need keyframe storage - use fast-path instead.
        /// </summary>
        private static bool IsLinearCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length < 2)
                return true;
            
            var keys = curve.keys;
            if (keys.Length != 2)
                return false;
            
            // Check if it's the default linear curve: (0,1) to (1,0) with tangent -1
            var k0 = keys[0];
            var k1 = keys[1];
            
            bool isDefaultLinear =
                Mathf.Abs(k0.time - 0f) < LinearCurveEpsilon &&
                Mathf.Abs(k0.value - 1f) < LinearCurveEpsilon &&
                Mathf.Abs(k1.time - 1f) < LinearCurveEpsilon &&
                Mathf.Abs(k1.value - 0f) < LinearCurveEpsilon &&
                Mathf.Abs(k0.outTangent - (-1f)) < LinearCurveEpsilon &&
                Mathf.Abs(k1.inTangent - (-1f)) < LinearCurveEpsilon;
            
            return isDefaultLinear;
        }
        
        private static int FindSegmentManaged(CurveKeyframe[] keyframes, float t)
        {
            var lastIndex = keyframes.Length - 1;
            
            for (var i = 0; i < lastIndex; i++)
            {
                if (t <= keyframes[i + 1].Time)
                    return i;
            }
            
            return lastIndex - 1;
        }
        
        private static float EvaluateHermiteSegmentManaged(CurveKeyframe[] keyframes, int segmentIndex, float t)
        {
            var k0 = keyframes[segmentIndex];
            var k1 = keyframes[segmentIndex + 1];
            
            return EvaluateHermiteCore(
                k0.Time, k0.Value, k0.OutTangent,
                k1.Time, k1.Value, k1.InTangent,
                t);
        }
        
        #endregion
        
        #region Shared Core (used by both Runtime and Editor)
        
        /// <summary>
        /// Core Hermite spline evaluation shared between Burst and managed code paths.
        /// Uses the standard Hermite basis functions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float EvaluateHermiteCore(
            float t0, float p0, float m0,
            float t1, float p1, float m1,
            float t)
        {
            // Handle degenerate segment (zero duration)
            var dt = t1 - t0;
            if (dt < 1e-6f)
                return p0;
            
            // Normalize t to segment [0, 1]
            var s = (t - t0) / dt;
            
            // Scale tangents by segment duration (Hermite tangents are in segment-local space)
            m0 *= dt;
            m1 *= dt;
            
            // Hermite basis functions (optimized form)
            var s2 = s * s;
            var s3 = s2 * s;
            
            var h00 = 2f * s3 - 3f * s2 + 1f;  // (1 - s)² * (1 + 2s)
            var h10 = s3 - 2f * s2 + s;         // s * (1 - s)²
            var h01 = -2f * s3 + 3f * s2;       // s² * (3 - 2s)
            var h11 = s3 - s2;                   // s² * (s - 1)
            
            var result = h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
            
            // Clamp to valid weight range - use math.clamp for Burst compatibility
            return math.clamp(result, 0f, 1f);
        }
        
        #endregion
    }
}
