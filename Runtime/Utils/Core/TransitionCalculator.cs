using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Input parameters for transition timing calculation.
    /// Burst-compatible struct - no managed types.
    /// </summary>
    public struct TransitionTimingInput
    {
        public float FromStateDuration;
        public float ToStateDuration;
        public float RequestedExitTime;
        public float RequestedTransitionDuration;
        [MarshalAs(UnmanagedType.U1)]
        public bool FromIsBlendState;
        [MarshalAs(UnmanagedType.U1)]
        public bool ToIsBlendState;
    }
    
    /// <summary>
    /// Calculated transition timing values.
    /// Burst-compatible struct - no managed types.
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
        [MarshalAs(UnmanagedType.U1)]
        public bool IsFromGhostDurationShrink;
        
        /// <summary>Total timeline duration (all sections).</summary>
        public readonly float TotalDuration => GhostFromDuration + FromBarDuration + TransitionDuration + ToBarDuration + GhostToDuration;
        
        /// <summary>Whether FROM state uses a ghost bar (visual-only indicator).</summary>
        public readonly bool HasFromGhost => GhostFromDuration > 0f;
        
        /// <summary>Whether TO state uses a ghost bar (visual-only indicator).</summary>
        public readonly bool HasToGhost => GhostToDuration > 0f;
    }
    
    /// <summary>
    /// Identifies which section of the transition timeline we're in.
    /// </summary>
    public enum TransitionSectionType : byte
    {
        /// <summary>Ghost region showing FROM state context (before main from bar).</summary>
        GhostFrom = 0,
        /// <summary>Main FROM state bar (before transition starts).</summary>
        FromBar,
        /// <summary>Transition/blend region (both states active).</summary>
        Transition,
        /// <summary>Main TO state bar (after transition completes).</summary>
        ToBar,
        /// <summary>Ghost region showing TO state context (after main to bar).</summary>
        GhostTo
    }
    
    /// <summary>
    /// Immutable snapshot of transition state at a specific point in time.
    /// Contains all calculated values needed for rendering a transition preview.
    /// Burst-compatible struct - no managed types.
    /// </summary>
    public readonly struct TransitionStateSnapshot
    {
        /// <summary>Normalized time (0-1) within the FROM state's clip.</summary>
        public readonly float FromStateNormalizedTime;
        
        /// <summary>Normalized time (0-1) within the TO state's clip.</summary>
        public readonly float ToStateNormalizedTime;
        
        /// <summary>Raw transition progress (0-1), before curve application.</summary>
        public readonly float RawProgress;
        
        /// <summary>Blend weight (0-1), after curve application. 0=fully FROM, 1=fully TO.</summary>
        public readonly float BlendWeight;
        
        /// <summary>Current section of the transition timeline.</summary>
        public readonly TransitionSectionType CurrentSection;
        
        /// <summary>Current time in seconds from timeline start.</summary>
        public readonly float CurrentTimeSeconds;
        
        /// <summary>Total timeline duration in seconds.</summary>
        public readonly float TotalDurationSeconds;
        
        /// <summary>Normalized timeline position (0-1).</summary>
        public readonly float NormalizedTime;
        
        /// <summary>Whether the transition is complete (past the blend region).</summary>
        public bool IsTransitionComplete => CurrentSection == TransitionSectionType.ToBar || 
                                            CurrentSection == TransitionSectionType.GhostTo;
        
        /// <summary>Whether we're currently in the blend region.</summary>
        public bool IsInTransition => CurrentSection == TransitionSectionType.Transition;
        
        /// <summary>Whether we're before the transition starts.</summary>
        public bool IsBeforeTransition => CurrentSection == TransitionSectionType.GhostFrom || 
                                          CurrentSection == TransitionSectionType.FromBar;
        
        public TransitionStateSnapshot(
            float fromStateNormalizedTime,
            float toStateNormalizedTime,
            float rawProgress,
            float blendWeight,
            TransitionSectionType currentSection,
            float currentTimeSeconds,
            float totalDurationSeconds,
            float normalizedTime)
        {
            FromStateNormalizedTime = fromStateNormalizedTime;
            ToStateNormalizedTime = toStateNormalizedTime;
            RawProgress = rawProgress;
            BlendWeight = blendWeight;
            CurrentSection = currentSection;
            CurrentTimeSeconds = currentTimeSeconds;
            TotalDurationSeconds = totalDurationSeconds;
            NormalizedTime = normalizedTime;
        }
        
        /// <summary>Creates an empty/default snapshot.</summary>
        public static TransitionStateSnapshot Empty => new TransitionStateSnapshot(
            0f, 0f, 0f, 0f, TransitionSectionType.FromBar, 0f, 1f, 0f);
    }
    
    /// <summary>
    /// Fixed-size blend curve for transition weight interpolation.
    /// Burst-compatible, thread-safe for read-only access.
    /// 
    /// Curve values represent FROM weight (Unity convention):
    /// - Implicit start: (t=0, value=1) - 100% FROM, 0% TO
    /// - Implicit end: (t=1, value=0) - 0% FROM, 100% TO
    /// 
    /// To get TO weight (BlendWeight): toWeight = 1 - curveValue
    /// 
    /// Up to 4 explicit interior keyframes for non-linear curves.
    /// KeyframeCount=0 means linear blend (fast path, no curve evaluation).
    /// 
    /// Total size: 17 bytes (4 keyframes × 4 bytes + 1 byte count)
    /// </summary>
    public struct BlendCurve
    {
        /// <summary>Maximum number of explicit interior keyframes.</summary>
        public const int MaxKeyframes = 4;
        
        // Fixed-size storage for up to 4 interior keyframes
        // Each CurveKeyframe is 4 bytes, total 16 bytes
        public CurveKeyframe K0;
        public CurveKeyframe K1;
        public CurveKeyframe K2;
        public CurveKeyframe K3;
        
        /// <summary>Number of explicit keyframes used (0-4). 0 = linear blend.</summary>
        public byte KeyframeCount;
        
        /// <summary>Whether this is a linear blend (no custom curve).</summary>
        public readonly bool IsLinear => KeyframeCount == 0;
        
        /// <summary>Linear blend curve (default). Returns t unchanged.</summary>
        public static BlendCurve Linear => default;
        
        /// <summary>
        /// Creates a blend curve from explicit keyframes.
        /// </summary>
        public static BlendCurve Create(CurveKeyframe k0 = default, CurveKeyframe k1 = default, 
                                        CurveKeyframe k2 = default, CurveKeyframe k3 = default,
                                        byte count = 0)
        {
            return new BlendCurve
            {
                K0 = k0,
                K1 = k1,
                K2 = k2,
                K3 = k3,
                KeyframeCount = count
            };
        }
        
        /// <summary>
        /// Gets keyframe by index (0-3). Returns default for out-of-range.
        /// </summary>
        public readonly CurveKeyframe GetKeyframe(int index)
        {
            return index switch
            {
                0 => K0,
                1 => K1,
                2 => K2,
                3 => K3,
                _ => default
            };
        }
    }
    
    /// <summary>
    /// Configuration parameters for transition state calculation.
    /// Burst-compatible struct - no managed types.
    /// </summary>
    public struct TransitionStateConfig
    {
        /// <summary>Duration of the FROM state in seconds.</summary>
        public float FromStateDuration;
        
        /// <summary>Duration of the TO state in seconds.</summary>
        public float ToStateDuration;
        
        /// <summary>Exit time in seconds (when transition should start).</summary>
        public float ExitTime;
        
        /// <summary>Transition duration in seconds.</summary>
        public float TransitionDuration;
        
        /// <summary>Offset into TO state when transition begins (0-1).</summary>
        public float TransitionOffset;
        
        /// <summary>Pre-calculated timing result.</summary>
        public TransitionTimingResult Timing;
        
        /// <summary>Blend curve for weight interpolation. Default = linear.</summary>
        public BlendCurve Curve;
    }
    
    /// <summary>
    /// Burst-compatible transition calculation utilities.
    /// Used by both ECS runtime systems and Editor preview.
    /// 
    /// This is the single source of truth for transition calculations.
    /// All consumers (ECS, PlayableGraph preview, UI) use these methods.
    /// </summary>
    [BurstCompile]
    public static class TransitionCalculator
    {
        #region Constants
        
        /// <summary>Minimum transition duration to avoid zero-length transitions.</summary>
        public const float MinTransitionDuration = 0.01f;
        
        /// <summary>Maximum transition duration for UI clamping.</summary>
        public const float MaxTransitionDuration = 10f;
        
        /// <summary>Minimum duration threshold for ghost bar calculations (~1 frame at 60fps).</summary>
        public const float MinDurationThreshold = 0.017f;
        
        /// <summary>Maximum number of visual cycles for ghost bars.</summary>
        public const int MaxVisualCycles = 4;
        
        #endregion
        
        #region Timing Calculation
        
        /// <summary>
        /// Calculates all transition timing values (section durations) from input parameters.
        /// This is called once during setup, not per-frame.
        /// Note: No [BurstCompile] as returning structs isn't supported. Still callable from Burst jobs.
        /// </summary>
        public static TransitionTimingResult CalculateTiming(in TransitionTimingInput input)
        {
            var result = new TransitionTimingResult();
            
            float fromDur = input.FromStateDuration;
            float toDur = input.ToStateDuration;
            
            // Calculate min exit time and clamp
            result.MinExitTime = math.max(0f, fromDur - toDur);
            result.ExitTime = math.clamp(input.RequestedExitTime, result.MinExitTime, fromDur);
            result.TransitionDuration = math.clamp(input.RequestedTransitionDuration, MinTransitionDuration, toDur);
            
            // Track if FROM ghost is due to duration shrink (requestedExitTime > fromDuration)
            result.IsFromGhostDurationShrink = input.RequestedExitTime > fromDur;
            
            // Calculate section durations based on state types
            if (input.FromIsBlendState || input.ToIsBlendState)
            {
                CalculateBlendStateTiming(ref result, in input);
            }
            else
            {
                CalculateSingleClipTiming(ref result, in input);
            }
            
            // Calculate TO bar duration
            result.ToBarDuration = toDur - result.TransitionDuration;
            
            if (result.ToBarDuration <= MinDurationThreshold)
            {
                result.ToBarDuration = 0f;
                if (result.GhostToDuration < MinDurationThreshold)
                {
                    result.GhostToDuration = toDur;
                }
            }
            
            // Calculate visual cycle counts for UI
            result.FromVisualCycles = CalculateFromVisualCycles(in input, in result);
            result.ToVisualCycles = CalculateToVisualCycles(in input, in result);
            
            return result;
        }
        
        [BurstCompile]
        private static int CalculateFromVisualCycles(in TransitionTimingInput input, in TransitionTimingResult result)
        {
            float fromDur = input.FromStateDuration;
            if (fromDur <= 0.001f) return 1;
            
            // Case 1: Duration shrunk - requestedExitTime exceeds fromStateDuration
            if (input.RequestedExitTime > fromDur)
            {
                int cycles = (int)math.ceil(input.RequestedExitTime / fromDur);
                return math.clamp(cycles, 1, MaxVisualCycles);
            }
            
            // Case 2: Context ghost - exitTime is at zero (full overlap)
            if (result.ExitTime < 0.001f && result.MinExitTime < 0.001f)
            {
                return 2;
            }
            
            return 1;
        }
        
        [BurstCompile]
        private static int CalculateToVisualCycles(in TransitionTimingInput input, in TransitionTimingResult result)
        {
            float toDur = input.ToStateDuration;
            float fromDur = input.FromStateDuration;
            if (toDur <= 0.001f) return 1;
            
            // Case 1: Duration shrunk - transitionDuration exceeds toStateDuration
            if (input.RequestedTransitionDuration > toDur)
            {
                int cycles = (int)math.ceil(input.RequestedTransitionDuration / toDur);
                return math.clamp(cycles, 1, MaxVisualCycles);
            }
            
            // Case 2: Context ghost - bars end together
            bool barsEndTogether = (result.ExitTime + toDur) <= (fromDur + 0.001f);
            if (barsEndTogether)
            {
                return 2;
            }
            
            return 1;
        }
        
        [BurstCompile]
        private static void CalculateBlendStateTiming(ref TransitionTimingResult result, in TransitionTimingInput input)
        {
            float fromDur = input.FromStateDuration;
            float toDur = input.ToStateDuration;
            
            float adaptedExitTime = fromDur - result.TransitionDuration;
            
            if (adaptedExitTime > MinDurationThreshold)
            {
                result.FromBarDuration = adaptedExitTime;
            }
            else
            {
                result.FromBarDuration = 0f;
                result.GhostFromDuration = fromDur;
            }
            
            if (toDur > MinDurationThreshold && result.TransitionDuration > toDur + MinDurationThreshold)
            {
                int cycles = math.clamp((int)math.ceil(result.TransitionDuration / toDur), 1, 4);
                result.GhostToDuration = (cycles - 1) * toDur;
            }
        }
        
        [BurstCompile]
        private static void CalculateSingleClipTiming(ref TransitionTimingResult result, in TransitionTimingInput input)
        {
            float fromDur = input.FromStateDuration;
            float toDur = input.ToStateDuration;
            
            result.FromBarDuration = result.ExitTime;
            
            if (fromDur > MinDurationThreshold && input.RequestedExitTime > fromDur + MinDurationThreshold)
            {
                int cycles = math.clamp((int)math.ceil(input.RequestedExitTime / fromDur), 1, 4);
                result.GhostFromDuration = (cycles - 1) * fromDur;
            }
            
            if (toDur > MinDurationThreshold && input.RequestedTransitionDuration > toDur + MinDurationThreshold)
            {
                int cycles = math.clamp((int)math.ceil(input.RequestedTransitionDuration / toDur), 1, 4);
                result.GhostToDuration = (cycles - 1) * toDur;
            }
        }
        
        #endregion
        
        #region State Calculation
        
        /// <summary>
        /// Calculates transition state snapshot for a given timeline position.
        /// Note: No [BurstCompile] as returning structs isn't supported as entry points.
        /// Still callable from Burst jobs (will be inlined).
        /// </summary>
        /// <param name="config">Transition configuration.</param>
        /// <param name="normalizedTime">Current position on timeline (0-1).</param>
        /// <returns>Snapshot with all calculated values for rendering.</returns>
        public static TransitionStateSnapshot CalculateState(in TransitionStateConfig config, float normalizedTime)
        {
            ref readonly var timing = ref config.Timing;
            
            float totalDuration = timing.TotalDuration;
            if (totalDuration <= 0.001f) totalDuration = 1f;
            
            float currentSeconds = math.saturate(normalizedTime) * totalDuration;
            
            // Calculate section boundaries
            float ghostFromEnd = timing.GhostFromDuration;
            float fromBarEnd = ghostFromEnd + timing.FromBarDuration;
            float transitionEnd = fromBarEnd + timing.TransitionDuration;
            float toBarEnd = transitionEnd + timing.ToBarDuration;
            
            // Determine current section
            TransitionSectionType section;
            if (currentSeconds < ghostFromEnd)
                section = TransitionSectionType.GhostFrom;
            else if (currentSeconds < fromBarEnd)
                section = TransitionSectionType.FromBar;
            else if (currentSeconds < transitionEnd)
                section = TransitionSectionType.Transition;
            else if (currentSeconds < toBarEnd)
                section = TransitionSectionType.ToBar;
            else
                section = TransitionSectionType.GhostTo;
            
            // Calculate transition progress
            float rawProgress = CalculateTransitionProgress(in config, in timing, currentSeconds, fromBarEnd);
            
            // Apply blend curve to get final weight
            float blendWeight = EvaluateBlendCurve(in config.Curve, rawProgress);
            
            // Calculate from/to state normalized times
            float fromNormalized = CalculateFromStateNormalizedTime(in config, in timing, currentSeconds);
            float toNormalized = CalculateToStateNormalizedTime(in config, in timing, currentSeconds, ghostFromEnd);
            
            return new TransitionStateSnapshot(
                fromNormalized,
                toNormalized,
                rawProgress,
                blendWeight,
                section,
                currentSeconds,
                totalDuration,
                normalizedTime);
        }
        
        /// <summary>
        /// Calculates transition state from progress value (0-1 within transition).
        /// Note: No [BurstCompile] as returning structs isn't supported as entry points.
        /// Still callable from Burst jobs (will be inlined).
        /// </summary>
        public static TransitionStateSnapshot CalculateStateFromProgress(in TransitionStateConfig config, float transitionProgress)
        {
            ref readonly var timing = ref config.Timing;
            
            float totalDuration = timing.TotalDuration;
            if (totalDuration <= 0.001f) totalDuration = 1f;
            
            // Calculate exit time position on timeline
            float exitTimeOnTimeline = timing.GhostFromDuration + timing.FromBarDuration;
            
            // Calculate current time from progress
            float currentSeconds = exitTimeOnTimeline + (transitionProgress * timing.TransitionDuration);
            float normalizedTime = currentSeconds / totalDuration;
            
            return CalculateState(in config, normalizedTime);
        }
        
        [BurstCompile]
        private static float CalculateTransitionProgress(
            in TransitionStateConfig config,
            in TransitionTimingResult timing,
            float currentSeconds,
            float exitTimeOnTimeline)
        {
            if (currentSeconds < exitTimeOnTimeline)
                return 0f;
            
            float transitionDuration = timing.TransitionDuration;
            if (transitionDuration <= 0.001f)
                return currentSeconds >= exitTimeOnTimeline ? 1f : 0f;
            
            float progress = (currentSeconds - exitTimeOnTimeline) / transitionDuration;
            return math.saturate(progress);
        }
        
        [BurstCompile]
        private static float CalculateFromStateNormalizedTime(
            in TransitionStateConfig config,
            in TransitionTimingResult timing,
            float currentSeconds)
        {
            if (config.FromStateDuration <= 0.001f)
                return 0f;
            
            int fromCycles = timing.FromVisualCycles > 0 ? timing.FromVisualCycles : 1;
            float totalFromTime = fromCycles * config.FromStateDuration;
            float timeInFromCycles = math.min(currentSeconds, totalFromTime);
            
            return CalculateNormalizedTime(timeInFromCycles, config.FromStateDuration);
        }
        
        [BurstCompile]
        private static float CalculateToStateNormalizedTime(
            in TransitionStateConfig config,
            in TransitionTimingResult timing,
            float currentSeconds,
            float ghostFromDuration)
        {
            if (config.ToStateDuration <= 0.001f)
                return 0f;
            
            // Calculate when TO state starts on timeline
            // TO state starts when the transition begins, which is after GhostFrom + FromBar
            // This correctly handles both single-clip states (where FromBar = ExitTime)
            // and blend states (where FromBar = fromDur - transDur, regardless of ExitTime)
            float toStateStartInTimeline = timing.GhostFromDuration + timing.FromBarDuration;
            
            float toStateElapsed = currentSeconds - toStateStartInTimeline;
            
            if (toStateElapsed < 0)
                return config.TransitionOffset;
            
            float toClipTime = config.TransitionOffset * config.ToStateDuration + toStateElapsed;
            
            int toCycles = timing.ToVisualCycles > 0 ? timing.ToVisualCycles : 1;
            if (toCycles > 1)
            {
                float totalToTime = toCycles * config.ToStateDuration;
                toClipTime = math.min(toClipTime, totalToTime);
                return CalculateNormalizedTime(toClipTime, config.ToStateDuration);
            }
            
            return math.saturate(toClipTime / config.ToStateDuration);
        }
        
        /// <summary>
        /// Calculates normalized time (0-1) with wrapping for looping clips.
        /// </summary>
        [BurstCompile]
        public static float CalculateNormalizedTime(float timeSeconds, float clipDuration)
        {
            if (clipDuration <= 0.001f) return 0f;
            
            float normalizedTime = timeSeconds / clipDuration;
            
            // Handle wrapping (fmod behavior)
            normalizedTime = normalizedTime - math.floor(normalizedTime);
            
            return math.saturate(normalizedTime);
        }
        
        #endregion
        
        #region Curve Evaluation
        
        /// <summary>
        /// Evaluates the blend curve at the given progress (0-1).
        /// Returns TO weight (0 = fully FROM, 1 = fully TO).
        /// 
        /// Curve values represent FROM weight:
        /// - Implicit start: (t=0, fromWeight=1)
        /// - Implicit end: (t=1, fromWeight=0)
        /// 
        /// For linear curves, returns t directly (fast path).
        /// For custom curves, evaluates Hermite spline and converts: toWeight = 1 - fromWeight
        /// </summary>
        [BurstCompile]
        public static float EvaluateBlendCurve(in BlendCurve curve, float t)
        {
            // Fast path: linear curve returns t directly (equivalent to 1 - (1-t) = t)
            if (curve.IsLinear)
                return math.saturate(t);
            
            t = math.saturate(t);
            
            // Evaluate curve to get FROM weight, then convert to TO weight
            float fromWeight = EvaluateBlendCurveRaw(in curve, t);
            return 1f - fromWeight;
        }
        
        /// <summary>
        /// Evaluates the blend curve to get raw FROM weight (internal use).
        /// Implicit endpoints: (0, 1) at start, (1, 0) at end.
        /// </summary>
        [BurstCompile]
        private static float EvaluateBlendCurveRaw(in BlendCurve curve, float t)
        {
            int explicitCount = math.min(curve.KeyframeCount, BlendCurve.MaxKeyframes);
            
            // Find the segment containing t
            // Implicit start: (0, 1) with tangent -1 (linear default)
            float prevTime = 0f;
            float prevValue = 1f;
            float prevOutTangent = -1f; // Linear tangent from (0,1) to (1,0)
            
            for (int i = 0; i <= explicitCount; i++)
            {
                float nextTime, nextValue, nextInTangent;
                
                if (i < explicitCount)
                {
                    // Explicit keyframe
                    var kf = curve.GetKeyframe(i);
                    nextTime = kf.Time;
                    nextValue = kf.Value;
                    nextInTangent = kf.InTangent;
                }
                else
                {
                    // Implicit end point (1, 0)
                    nextTime = 1f;
                    nextValue = 0f;
                    nextInTangent = -1f; // Linear tangent
                }
                
                if (t <= nextTime)
                {
                    // t is in this segment [prevTime, nextTime]
                    return EvaluateHermiteSegment(
                        prevTime, prevValue, prevOutTangent,
                        nextTime, nextValue, nextInTangent,
                        t);
                }
                
                // Move to next segment
                prevTime = nextTime;
                prevValue = nextValue;
                if (i < explicitCount)
                {
                    prevOutTangent = curve.GetKeyframe(i).OutTangent;
                }
            }
            
            // t >= 1, return end value (0 = 0% FROM)
            return 0f;
        }
        
        /// <summary>
        /// Evaluates a Hermite spline segment between two keyframes.
        /// </summary>
        [BurstCompile]
        private static float EvaluateHermiteSegment(
            float t0, float v0, float outTangent0,
            float t1, float v1, float inTangent1,
            float t)
        {
            float dt = t1 - t0;
            if (dt <= 0.0001f)
                return v0;
            
            // Normalize t to [0, 1] within segment
            float u = (t - t0) / dt;
            
            // Hermite basis functions
            float u2 = u * u;
            float u3 = u2 * u;
            
            float h00 = 2f * u3 - 3f * u2 + 1f;  // value at start
            float h10 = u3 - 2f * u2 + u;        // tangent at start
            float h01 = -2f * u3 + 3f * u2;      // value at end
            float h11 = u3 - u2;                  // tangent at end
            
            // Scale tangents by segment duration
            float m0 = outTangent0 * dt;
            float m1 = inTangent1 * dt;
            
            return h00 * v0 + h10 * m0 + h01 * v1 + h11 * m1;
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Recalculates transition duration based on available space.
        /// </summary>
        [BurstCompile]
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
            
            float fromBarEnd = fromStateDuration;
            float toBarEnd = exitTime + toStateDuration;
            float maxPossibleTransition = math.max(MinTransitionDuration, math.min(fromBarEnd, toBarEnd) - exitTime);
            
            return math.clamp(
                requestedTransitionDuration,
                MinTransitionDuration,
                math.min(MaxTransitionDuration, math.min(maxPossibleTransition, toStateDuration)));
        }
        
        /// <summary>
        /// Calculates clamped exit time for a given configuration.
        /// </summary>
        [BurstCompile]
        public static float ClampExitTime(float requestedExitTime, float fromStateDuration, float toStateDuration)
        {
            float minExitTime = math.max(0f, fromStateDuration - toStateDuration);
            return math.clamp(requestedExitTime, minExitTime, fromStateDuration);
        }
        
        /// <summary>
        /// Calculates minimum exit time for a given configuration.
        /// </summary>
        [BurstCompile]
        public static float GetMinExitTime(float fromStateDuration, float toStateDuration)
        {
            return math.max(0f, fromStateDuration - toStateDuration);
        }
        
        #endregion
    }
}
