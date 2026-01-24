using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Shared utility for calculating animation timing, cycles, and loops.
    /// Used by both Runtime jobs and Editor timeline previews to ensure parity.
    /// </summary>
    public static class AnimationTimeUtils
    {
        /// <summary>
        /// Calculates the number of cycles (loops) that have occurred for a given accumulated time.
        /// </summary>
        /// <param name="accumulatedTime">Total time elapsed for the state (unbounded)</param>
        /// <param name="duration">Duration of a single clip cycle</param>
        /// <returns>Number of cycles started (minimum 1)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CalculateCycleCount(float accumulatedTime, float duration)
        {
            if (duration <= 0.001f) return 1;
            // E.g. 1.5s / 1.0s = 1.5 -> Ceil(1.5) = 2 cycles (0..1 and 1..2)
            // But we clamp to at least 1
            float cycles = accumulatedTime / duration;
            return math.max(1, (int)math.ceil(cycles));
        }

        /// <summary>
        /// Calculates the normalized time (0-1) within the current cycle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateNormalizedTime(float accumulatedTime, float duration)
        {
            if (duration <= 0.001f) return 0f;
            
            // Loop logic: accumulatedTime % duration
            // But we want 0-1 range
            float timeInCycle = accumulatedTime % duration;
            
            // Handle edge case where % returns negative (shouldn't happen with positive time but safe to handle)
            if (timeInCycle < 0) timeInCycle += duration;
            
            return math.clamp(timeInCycle / duration, 0f, 1f);
        }

        /// <summary>
        /// Calculates the effective duration of a blend state based on weights.
        /// Weighted average of (ClipLength / ClipSpeed).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateEffectiveDuration(
            float[] weights,
            float[] clipDurations,
            float[] clipSpeeds)
        {
            return LinearBlendStateUtils.CalculateEffectiveDuration(weights, clipDurations, clipSpeeds);
        }

        /// <summary>
        /// Calculates the effective speed of a blend state based on weights.
        /// Weighted average of ClipSpeed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateEffectiveSpeed(
            float[] weights,
            float[] clipSpeeds)
        {
            return LinearBlendStateUtils.CalculateEffectiveSpeed(weights, clipSpeeds);
        }

        /// <summary>
        /// Calculates the effective duration of a blend state based on weights (NativeArray version).
        /// Weighted average of (ClipLength / ClipSpeed).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateEffectiveDuration(
            in NativeArray<float> weights,
            in NativeArray<float> clipDurations,
            in NativeArray<float> clipSpeeds)
        {
            float weightedDuration = 0f;
            float totalWeight = 0f;
            
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0.001f)
                {
                    float speed = clipSpeeds[i];
                    // Handle 0 or negative speed safely
                    if (speed <= 0.0001f) speed = 1f;
                    
                    float duration = clipDurations[i] / speed;
                    weightedDuration += weights[i] * duration;
                    totalWeight += weights[i];
                }
            }
            
            return totalWeight > 0.001f ? weightedDuration / totalWeight : 1f;
        }

        /// <summary>
        /// Calculates the effective speed of a blend state based on weights (NativeArray version).
        /// Weighted average of ClipSpeed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateEffectiveSpeed(
            in NativeArray<float> weights,
            in NativeArray<float> clipSpeeds)
        {
            float weightedSpeed = 0f;
            float totalWeight = 0f;
            
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0.001f)
                {
                    float speed = clipSpeeds[i];
                    if (speed <= 0.0001f) speed = 1f;
                    
                    weightedSpeed += weights[i] * speed;
                    totalWeight += weights[i];
                }
            }
            
            return totalWeight > 0.001f ? weightedSpeed / totalWeight : 1f;
        }
    }
}
