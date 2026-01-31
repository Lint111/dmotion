using DMotion;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Editor-specific transition section enum.
    /// Maps to Runtime TransitionSectionType for API compatibility.
    /// </summary>
    public enum TransitionSection
    {
        GhostFrom = TransitionSectionType.GhostFrom,
        FromBar = TransitionSectionType.FromBar,
        Transition = TransitionSectionType.Transition,
        ToBar = TransitionSectionType.ToBar,
        GhostTo = TransitionSectionType.GhostTo
    }
    
    /// <summary>
    /// Convenience methods for creating TransitionStateConfig from Editor assets.
    /// All calculation is delegated to Runtime TransitionCalculator.
    /// 
    /// Usage:
    ///     var config = TransitionStateCalculator.CreateConfig(fromState, toState, transition, blendPos);
    ///     var snapshot = TransitionCalculator.CalculateState(config, normalizedTime);
    ///     // snapshot.BlendWeight already has curve applied
    /// </summary>
    public static class TransitionStateCalculator
    {
        /// <summary>
        /// Creates a TransitionStateConfig from Editor assets.
        /// </summary>
        public static TransitionStateConfig CreateConfig(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            float exitTime,
            float transitionDuration,
            float transitionOffset = 0f,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default,
            AnimationCurve blendCurve = null)
        {
            float fromDuration = fromState?.GetEffectiveDuration(fromBlendPosition) ?? 0f;
            float toDuration = toState?.GetEffectiveDuration(toBlendPosition) ?? 1f;
            
            var timing = TransitionCalculator.CalculateTiming(new TransitionTimingInput
            {
                FromStateDuration = fromDuration,
                ToStateDuration = toDuration,
                RequestedExitTime = exitTime,
                RequestedTransitionDuration = transitionDuration,
                FromIsBlendState = fromState != null && AnimationStateUtils.IsBlendState(fromState),
                ToIsBlendState = toState != null && AnimationStateUtils.IsBlendState(toState)
            });
            
            return new TransitionStateConfig
            {
                FromStateDuration = fromDuration,
                ToStateDuration = toDuration,
                ExitTime = exitTime,
                TransitionDuration = transitionDuration,
                TransitionOffset = transitionOffset,
                Timing = timing,
                Curve = CurveUtils.ConvertToBlendCurve(blendCurve)
            };
        }
        
        /// <summary>
        /// Creates a TransitionStateConfig from a StateOutTransition asset.
        /// </summary>
        public static TransitionStateConfig CreateConfig(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            StateOutTransition transition,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            if (transition == null)
            {
                return CreateConfig(fromState, toState, 0f, 0.25f, 0f, fromBlendPosition, toBlendPosition, null);
            }
            
            return CreateConfig(
                fromState,
                toState,
                transition.EndTime,
                transition.TransitionDuration,
                0f,
                fromBlendPosition,
                toBlendPosition,
                transition.BlendCurve);
        }
        
        /// <summary>
        /// Calculates transition state for a normalized timeline position.
        /// Convenience method that creates config and calculates in one call.
        /// </summary>
        public static TransitionStateSnapshot Calculate(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            StateOutTransition transition,
            float normalizedTime,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            var config = CreateConfig(fromState, toState, transition, fromBlendPosition, toBlendPosition);
            return TransitionCalculator.CalculateState(in config, normalizedTime);
        }
        
        /// <summary>
        /// Calculates transition state from a progress value (0-1 within transition).
        /// Convenience method that creates config and calculates in one call.
        /// </summary>
        public static TransitionStateSnapshot CalculateFromProgress(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            StateOutTransition transition,
            float transitionProgress,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            var config = CreateConfig(fromState, toState, transition, fromBlendPosition, toBlendPosition);
            return TransitionCalculator.CalculateStateFromProgress(in config, transitionProgress);
        }
        
        /// <summary>
        /// Calculates transition timing from Editor assets.
        /// </summary>
        public static TransitionTimingResult CalculateTiming(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            float exitTime,
            float transitionDuration,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            return TransitionCalculator.CalculateTiming(new TransitionTimingInput
            {
                FromStateDuration = fromState?.GetEffectiveDuration(fromBlendPosition) ?? 0f,
                ToStateDuration = toState?.GetEffectiveDuration(toBlendPosition) ?? 1f,
                RequestedExitTime = exitTime,
                RequestedTransitionDuration = transitionDuration,
                FromIsBlendState = fromState != null && AnimationStateUtils.IsBlendState(fromState),
                ToIsBlendState = toState != null && AnimationStateUtils.IsBlendState(toState)
            });
        }
    }
}
