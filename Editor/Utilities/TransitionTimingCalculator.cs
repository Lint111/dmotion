using DMotion;
using DMotion.Authoring;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Convenience methods for transition timing calculation from Editor assets.
    /// All calculation delegated to Runtime TransitionCalculator.
    /// </summary>
    public static class TransitionTimingCalculator
    {
        // Re-export constants for convenience
        public const float MinTransitionDuration = TransitionCalculator.MinTransitionDuration;
        public const float MaxTransitionDuration = TransitionCalculator.MaxTransitionDuration;
        public const float MinDurationThreshold = TransitionCalculator.MinDurationThreshold;
        public const int MaxVisualCycles = TransitionCalculator.MaxVisualCycles;
        
        /// <summary>
        /// Calculates timing from state assets and blend positions.
        /// </summary>
        public static TransitionTimingResult Calculate(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            Vector2 fromBlendPos,
            Vector2 toBlendPos,
            float requestedExitTime,
            float requestedTransitionDuration)
        {
            return TransitionCalculator.CalculateTiming(new TransitionTimingInput
            {
                FromStateDuration = fromState != null 
                    ? AnimationStateUtils.GetEffectiveDuration(fromState, fromBlendPos) 
                    : 0f,
                ToStateDuration = AnimationStateUtils.GetEffectiveDuration(toState, toBlendPos),
                RequestedExitTime = requestedExitTime,
                RequestedTransitionDuration = requestedTransitionDuration,
                FromIsBlendState = fromState != null && AnimationStateUtils.IsBlendState(fromState),
                ToIsBlendState = toState != null && AnimationStateUtils.IsBlendState(toState)
            });
        }
        
        /// <summary>
        /// Calculates timing from raw duration values.
        /// </summary>
        public static TransitionTimingResult Calculate(TransitionTimingInput input)
        {
            return TransitionCalculator.CalculateTiming(input);
        }
        
        /// <summary>
        /// Recalculates transition duration based on available space.
        /// </summary>
        public static float RecalculateTransitionDuration(
            float fromStateDuration,
            float toStateDuration,
            float exitTime,
            float requestedTransitionDuration)
        {
            return TransitionCalculator.RecalculateTransitionDuration(
                fromStateDuration, toStateDuration, exitTime, requestedTransitionDuration);
        }
        
        /// <summary>
        /// Calculates clamped exit time.
        /// </summary>
        public static float ClampExitTime(float requestedExitTime, float fromStateDuration, float toStateDuration)
        {
            return TransitionCalculator.ClampExitTime(requestedExitTime, fromStateDuration, toStateDuration);
        }
        
        /// <summary>
        /// Calculates minimum exit time.
        /// </summary>
        public static float GetMinExitTime(float fromStateDuration, float toStateDuration)
        {
            return TransitionCalculator.GetMinExitTime(fromStateDuration, toStateDuration);
        }
    }
}
