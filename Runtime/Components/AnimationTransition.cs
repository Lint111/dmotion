using System;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Packed blittable keyframe for Hermite spline curves in blob storage.
    /// Uses byte precision for memory efficiency (4 bytes per keyframe).
    /// Sufficient precision for transition blend curves in [0,1] range.
    /// </summary>
    internal struct CurveKeyframe
    {
        /// <summary>Normalized time [0, 1] packed as byte (0-255)</summary>
        internal byte TimeNorm;
        /// <summary>Weight value [0, 1] packed as byte (0-255) - represents "To" state weight</summary>
        internal byte ValueNorm;
        /// <summary>Incoming tangent scaled by TangentScale, clamped to [-128, 127]</summary>
        internal sbyte InTangentScaled;
        /// <summary>Outgoing tangent scaled by TangentScale, clamped to [-128, 127]</summary>
        internal sbyte OutTangentScaled;
        
        /// <summary>Scale factor for tangents. Effective range: -12.8 to +12.7</summary>
        internal const float TangentScale = 10f;
        
        /// <summary>Unpacked time value [0, 1]</summary>
        internal float Time
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => TimeNorm / 255f;
        }
        
        /// <summary>Unpacked value [0, 1]</summary>
        internal float Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ValueNorm / 255f;
        }
        
        /// <summary>Unpacked incoming tangent</summary>
        internal float InTangent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InTangentScaled / TangentScale;
        }
        
        /// <summary>Unpacked outgoing tangent</summary>
        internal float OutTangent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => OutTangentScaled / TangentScale;
        }
        
        /// <summary>Creates a packed keyframe from float values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static CurveKeyframe Create(float time, float value, float inTangent, float outTangent)
        {
            return new CurveKeyframe
            {
                TimeNorm = (byte)math.clamp(time * 255f, 0f, 255f),
                ValueNorm = (byte)math.clamp(value * 255f, 0f, 255f),
                InTangentScaled = (sbyte)math.clamp(inTangent * TangentScale, -128f, 127f),
                OutTangentScaled = (sbyte)math.clamp(outTangent * TangentScale, -128f, 127f)
            };
        }
    }

    internal struct StateOutTransitionGroup
    {
        internal short ToStateIndex;
        internal float TransitionDuration;
        internal float TransitionEndTime;
        internal BlobArray<BoolTransition> BoolTransitions;
        internal BlobArray<IntTransition> IntTransitions;
        
        /// <summary>
        /// Hermite spline keyframes for custom blend curve.
        /// Empty array = linear blend (fast-path, no curve evaluation).
        /// </summary>
        internal BlobArray<CurveKeyframe> CurveKeyframes;
        
        internal bool HasEndTime => TransitionEndTime > 0;
        internal bool HasAnyConditions => BoolTransitions.Length > 0 || IntTransitions.Length > 0;
        
        /// <summary>Whether this transition has a custom blend curve (non-linear).</summary>
        internal bool HasCurve => CurveKeyframes.Length > 0;
    }
    internal struct BoolTransition
    {
        internal int ParameterIndex;
        internal bool ComparisonValue;
        internal bool Evaluate(in BoolParameter parameter)
        {
            return parameter.Value == ComparisonValue;
        }
    }

    public enum IntConditionComparison
    {
        Equal = 0,
        NotEqual,
        Greater,
        Less,
        GreaterOrEqual,
        LessOrEqual
    }
    internal struct IntTransition
    {
        internal int ParameterIndex;
        internal IntConditionComparison ComparisonMode;
        internal int ComparisonValue;
        internal bool Evaluate(in IntParameter parameter)
        {
            return ComparisonMode switch
            {
                IntConditionComparison.Equal => parameter.Value == ComparisonValue,
                IntConditionComparison.NotEqual => parameter.Value != ComparisonValue,
                IntConditionComparison.Greater => parameter.Value > ComparisonValue,
                IntConditionComparison.Less => parameter.Value < ComparisonValue,
                IntConditionComparison.GreaterOrEqual => parameter.Value >= ComparisonValue,
                IntConditionComparison.LessOrEqual => parameter.Value <= ComparisonValue,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    /// <summary>
    /// Global transition that can be taken from any state in the state machine.
    /// Evaluated before regular state transitions, matching Unity's Any State behavior.
    /// </summary>
    internal struct AnyStateTransition
    {
        /// <summary>Destination state index</summary>
        internal short ToStateIndex;

        /// <summary>Blend duration in seconds</summary>
        internal float TransitionDuration;

        /// <summary>
        /// End time in seconds (converted from Unity's normalized exit time).
        /// Only checked if HasEndTime is true.
        /// </summary>
        internal float TransitionEndTime;

        /// <summary>Bool transition conditions (all must be true)</summary>
        internal BlobArray<BoolTransition> BoolTransitions;

        /// <summary>Int transition conditions (all must be true)</summary>
        internal BlobArray<IntTransition> IntTransitions;
        
        /// <summary>
        /// Hermite spline keyframes for custom blend curve.
        /// Empty array = linear blend (fast-path, no curve evaluation).
        /// </summary>
        internal BlobArray<CurveKeyframe> CurveKeyframes;
        
        /// <summary>
        /// Whether this transition can target the current state (self-transition).
        /// If false, the transition won't fire when already in the destination state.
        /// Matches Unity's AnimatorStateTransition.canTransitionToSelf property.
        /// </summary>
        internal bool CanTransitionToSelf;

        /// <summary>Whether this transition requires reaching an end time</summary>
        internal bool HasEndTime => TransitionEndTime > 0;

        /// <summary>Whether this transition has any conditions to evaluate</summary>
        internal bool HasAnyConditions => BoolTransitions.Length > 0 || IntTransitions.Length > 0;
        
        /// <summary>Whether this transition has a custom blend curve (non-linear).</summary>
        internal bool HasCurve => CurveKeyframes.Length > 0;
    }
}