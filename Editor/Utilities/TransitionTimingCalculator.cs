using DMotion.Authoring;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Input parameters for transition timing calculation.
    /// </summary>
    public struct TransitionTimingInput
    {
        public float FromStateDuration;
        public float ToStateDuration;
        public float RequestedExitTime;
        public float RequestedTransitionDuration;
        public bool FromIsBlendState;
        public bool ToIsBlendState;
        
        /// <summary>
        /// Creates input from state assets and blend positions.
        /// </summary>
        public static TransitionTimingInput FromStates(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            Vector2 fromBlendPos,
            Vector2 toBlendPos,
            float requestedExitTime,
            float requestedTransitionDuration)
        {
            return new TransitionTimingInput
            {
                FromStateDuration = fromState != null 
                    ? AnimationStateUtils.GetEffectiveDuration(fromState, fromBlendPos) 
                    : 0f,
                ToStateDuration = AnimationStateUtils.GetEffectiveDuration(toState, toBlendPos),
                RequestedExitTime = requestedExitTime,
                RequestedTransitionDuration = requestedTransitionDuration,
                FromIsBlendState = fromState != null && AnimationStateUtils.IsBlendState(fromState),
                ToIsBlendState = toState != null && AnimationStateUtils.IsBlendState(toState)
            };
        }
    }
    
    /// <summary>
    /// Calculated transition timing values.
    /// </summary>
    public struct TransitionTimingResult
    {
        /// <summary>Clamped exit time (when transition starts).</summary>
        public float ExitTime;
        
        /// <summary>Clamped transition duration.</summary>
        public float TransitionDuration;
        
        /// <summary>Duration of FROM state bar before transition.</summary>
        public float FromBarDuration;
        
        /// <summary>Duration of TO state bar after transition.</summary>
        public float ToBarDuration;
        
        /// <summary>Ghost bar duration for FROM state (when transition >= from duration).</summary>
        public float GhostFromDuration;
        
        /// <summary>Ghost bar duration for TO state (when transition >= to duration).</summary>
        public float GhostToDuration;
        
        /// <summary>Minimum valid exit time for this configuration.</summary>
        public float MinExitTime;
        
        /// <summary>Number of FROM bar visual cycles (1 = no ghost, 2+ = ghost bars).</summary>
        public int FromVisualCycles;
        
        /// <summary>Number of TO bar visual cycles (1 = no ghost, 2+ = ghost bars).</summary>
        public int ToVisualCycles;
        
        /// <summary>Whether the FROM ghost is due to duration shrink (vs context ghost).</summary>
        public bool IsFromGhostDurationShrink;
        
        /// <summary>Total timeline duration (all sections).</summary>
        public float TotalDuration => GhostFromDuration + FromBarDuration + TransitionDuration + ToBarDuration + GhostToDuration;
        
        /// <summary>Whether FROM state uses a ghost bar (visual-only indicator).</summary>
        public bool HasFromGhost => GhostFromDuration > 0f;
        
        /// <summary>Whether TO state uses a ghost bar (visual-only indicator).</summary>
        public bool HasToGhost => GhostToDuration > 0f;
    }
    
    /// <summary>
    /// Shared utility for calculating transition timing values.
    /// Used by both ECS timeline (TimelineControlHelper) and visual timeline (TransitionTimeline).
    /// </summary>
    public static class TransitionTimingCalculator
    {
        /// <summary>
        /// Minimum transition duration to avoid zero-length transitions.
        /// </summary>
        public const float MinTransitionDuration = 0.01f;
        
        /// <summary>
        /// Maximum transition duration for UI clamping.
        /// </summary>
        public const float MaxTransitionDuration = 10f;
        
        /// <summary>
        /// Minimum duration threshold for ghost bar calculations.
        /// Roughly 1 frame at 60fps.
        /// </summary>
        public const float MinDurationThreshold = 0.017f;
        
        /// <summary>
        /// Maximum number of visual cycles for ghost bars.
        /// </summary>
        public const int MaxVisualCycles = 4;
        
        /// <summary>
        /// Calculates all transition timing values from input parameters.
        /// </summary>
        public static TransitionTimingResult Calculate(TransitionTimingInput input)
        {
            var result = new TransitionTimingResult();
            
            float fromDur = input.FromStateDuration;
            float toDur = input.ToStateDuration;
            
            // Calculate min exit time and clamp
            result.MinExitTime = Mathf.Max(0f, fromDur - toDur);
            result.ExitTime = Mathf.Clamp(input.RequestedExitTime, result.MinExitTime, fromDur);
            result.TransitionDuration = Mathf.Clamp(input.RequestedTransitionDuration, MinTransitionDuration, toDur);
            
            // Track if FROM ghost is due to duration shrink (requestedExitTime > fromDuration)
            result.IsFromGhostDurationShrink = input.RequestedExitTime > fromDur;
            
            // Calculate section durations based on state types
            if (input.FromIsBlendState || input.ToIsBlendState)
            {
                CalculateBlendStateTiming(ref result, input);
            }
            else
            {
                CalculateSingleClipTiming(ref result, input);
            }
            
            // Calculate TO bar duration
            result.ToBarDuration = toDur - result.TransitionDuration;
            
            if (result.ToBarDuration <= MinDurationThreshold)
            {
                // Transition duration >= toStateDuration
                // ToBar would be too small, use ghost for context
                result.ToBarDuration = 0f;
                if (result.GhostToDuration < MinDurationThreshold)
                {
                    result.GhostToDuration = toDur;
                }
            }
            
            // Calculate visual cycle counts for UI
            result.FromVisualCycles = CalculateFromVisualCycles(input, result);
            result.ToVisualCycles = CalculateToVisualCycles(input, result);
            
            return result;
        }
        
        /// <summary>
        /// Calculates the number of FROM bar visual cycles.
        /// Shows ghost bars (cycles > 1) when:
        /// - requestedExitTime > fromStateDuration (duration shrunk below exit time)
        /// - exitTime is at zero (context ghost to show previous cycle)
        /// </summary>
        private static int CalculateFromVisualCycles(TransitionTimingInput input, TransitionTimingResult result)
        {
            float fromDur = input.FromStateDuration;
            if (fromDur <= 0.001f) return 1;
            
            // Case 1: Duration shrunk - requestedExitTime exceeds fromStateDuration
            if (input.RequestedExitTime > fromDur)
            {
                int cycles = Mathf.CeilToInt(input.RequestedExitTime / fromDur);
                return Mathf.Clamp(cycles, 1, MaxVisualCycles);
            }
            
            // Case 2: Context ghost - exitTime is at zero (full overlap)
            if (result.ExitTime < 0.001f && result.MinExitTime < 0.001f)
            {
                return 2; // Show one previous cycle for context
            }
            
            // Normal case - no ghost
            return 1;
        }
        
        /// <summary>
        /// Calculates the number of TO bar visual cycles.
        /// Shows ghost bars (cycles > 1) when:
        /// - transitionDuration > toStateDuration (to-duration shrunk)
        /// - bars end together (context ghost to show continuation)
        /// </summary>
        private static int CalculateToVisualCycles(TransitionTimingInput input, TransitionTimingResult result)
        {
            float toDur = input.ToStateDuration;
            float fromDur = input.FromStateDuration;
            if (toDur <= 0.001f) return 1;
            
            // Case 1: Duration shrunk - transitionDuration exceeds toStateDuration
            if (input.RequestedTransitionDuration > toDur)
            {
                int cycles = Mathf.CeilToInt(input.RequestedTransitionDuration / toDur);
                return Mathf.Clamp(cycles, 1, MaxVisualCycles);
            }
            
            // Case 2: Context ghost - bars end together
            bool barsEndTogether = (result.ExitTime + toDur) <= (fromDur + 0.001f);
            if (barsEndTogether)
            {
                return 2; // Show one continuation cycle for context
            }
            
            // Normal case - no ghost
            return 1;
        }
        
        /// <summary>
        /// Calculates timing for blend states where exit time adapts to duration.
        /// </summary>
        private static void CalculateBlendStateTiming(ref TransitionTimingResult result, TransitionTimingInput input)
        {
            float fromDur = input.FromStateDuration;
            float toDur = input.ToStateDuration;
            
            // Blend state: exit time adapts to duration
            float adaptedExitTime = fromDur - result.TransitionDuration;
            
            if (adaptedExitTime > MinDurationThreshold)
            {
                // Normal case: FromBar shows FROM state before transition
                result.FromBarDuration = adaptedExitTime;
            }
            else
            {
                // Transition duration >= fromStateDuration
                // Add ghost bar for context
                result.FromBarDuration = 0f;
                result.GhostFromDuration = fromDur;
            }
            
            // TO ghost: when transition significantly exceeds TO duration
            if (toDur > MinDurationThreshold && result.TransitionDuration > toDur + MinDurationThreshold)
            {
                int cycles = Mathf.Clamp(Mathf.CeilToInt(result.TransitionDuration / toDur), 1, 4);
                result.GhostToDuration = (cycles - 1) * toDur;
            }
        }
        
        /// <summary>
        /// Calculates timing for single clip states with explicit exit time.
        /// </summary>
        private static void CalculateSingleClipTiming(ref TransitionTimingResult result, TransitionTimingInput input)
        {
            float fromDur = input.FromStateDuration;
            float toDur = input.ToStateDuration;
            
            // Single clip: use explicit exit time
            result.FromBarDuration = result.ExitTime;
            
            // FROM ghost: only when requestedExitTime exceeds fromStateDuration
            if (fromDur > MinDurationThreshold && input.RequestedExitTime > fromDur + MinDurationThreshold)
            {
                int cycles = Mathf.Clamp(Mathf.CeilToInt(input.RequestedExitTime / fromDur), 1, 4);
                result.GhostFromDuration = (cycles - 1) * fromDur;
            }
            
            // TO ghost: only when transitionDuration exceeds toStateDuration
            if (toDur > MinDurationThreshold && input.RequestedTransitionDuration > toDur + MinDurationThreshold)
            {
                int cycles = Mathf.Clamp(Mathf.CeilToInt(input.RequestedTransitionDuration / toDur), 1, 4);
                result.GhostToDuration = (cycles - 1) * toDur;
            }
        }
        
        /// <summary>
        /// Recalculates transition duration based on available space.
        /// Used when exit time changes and transition needs to fit.
        /// </summary>
        public static float RecalculateTransitionDuration(
            float fromStateDuration,
            float toStateDuration,
            float exitTime,
            float requestedTransitionDuration)
        {
            if (fromStateDuration <= 0.001f || toStateDuration <= 0.001f)
            {
                return MinTransitionDuration;
            }
            
            // Calculate available overlap space
            float fromBarEnd = fromStateDuration;
            float toBarEnd = exitTime + toStateDuration;
            float maxPossibleTransition = Mathf.Max(MinTransitionDuration, Mathf.Min(fromBarEnd, toBarEnd) - exitTime);
            
            // Clamp to available space and bounds
            return Mathf.Clamp(
                requestedTransitionDuration,
                MinTransitionDuration,
                Mathf.Min(MaxTransitionDuration, Mathf.Min(maxPossibleTransition, toStateDuration)));
        }
        
        /// <summary>
        /// Calculates clamped exit time for a given configuration.
        /// </summary>
        public static float ClampExitTime(float requestedExitTime, float fromStateDuration, float toStateDuration)
        {
            float minExitTime = Mathf.Max(0f, fromStateDuration - toStateDuration);
            return Mathf.Clamp(requestedExitTime, minExitTime, fromStateDuration);
        }
        
        /// <summary>
        /// Calculates minimum exit time for a given configuration.
        /// </summary>
        public static float GetMinExitTime(float fromStateDuration, float toStateDuration)
        {
            return Mathf.Max(0f, fromStateDuration - toStateDuration);
        }
    }
}
