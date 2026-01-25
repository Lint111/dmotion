using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Base interface for animation render requests.
    /// Each request type knows how to apply itself to animation state.
    /// </summary>
    internal interface IAnimationRenderRequest
    {
        bool IsValid { get; }
        TimelineSectionType SectionType { get; }
    }
    
    /// <summary>
    /// Request to render a single animation state at a specific time.
    /// Used for: state preview, ghost-from bars, ghost-to bars.
    /// </summary>
    internal struct AnimationStateRenderRequest : IComponentData, IAnimationRenderRequest
    {
        /// <summary>State index in the state machine.</summary>
        public ushort StateIndex;
        
        /// <summary>Normalized time within the state (0-1).</summary>
        public float NormalizedTime;
        
        /// <summary>Blend position for blend states (x for 1D, xy for 2D).</summary>
        public float2 BlendPosition;
        
        /// <summary>Timeline section type for UI context.</summary>
        public TimelineSectionType SectionType;
        
        /// <summary>Whether this request is valid and should be processed.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool IsActive;
        
        public bool IsValid => IsActive;
        TimelineSectionType IAnimationRenderRequest.SectionType => SectionType;
        
        public static AnimationStateRenderRequest None => new() { IsActive = false };
        
        /// <summary>Creates a state render request.</summary>
        public static AnimationStateRenderRequest Create(
            ushort stateIndex,
            float normalizedTime,
            float2 blendPosition = default,
            TimelineSectionType sectionType = TimelineSectionType.State)
        {
            return new AnimationStateRenderRequest
            {
                StateIndex = stateIndex,
                NormalizedTime = normalizedTime,
                BlendPosition = blendPosition,
                SectionType = sectionType,
                IsActive = true
            };
        }
        
        /// <summary>Creates a ghost "from" state request (context before transition).</summary>
        public static AnimationStateRenderRequest GhostFrom(
            ushort stateIndex,
            float normalizedTime,
            float2 blendPosition = default)
        {
            return Create(stateIndex, normalizedTime, blendPosition, TimelineSectionType.GhostFrom);
        }
        
        /// <summary>Creates a ghost "to" state request (context after transition).</summary>
        public static AnimationStateRenderRequest GhostTo(
            ushort stateIndex,
            float normalizedTime,
            float2 blendPosition = default)
        {
            return Create(stateIndex, normalizedTime, blendPosition, TimelineSectionType.GhostTo);
        }
    }
    
    /// <summary>
    /// Request to render a transition blend between two states.
    /// Handles crossfade with configurable blend curve.
    /// </summary>
    internal struct AnimationTransitionRenderRequest : IComponentData, IAnimationRenderRequest
    {
        /// <summary>Source state index.</summary>
        public ushort FromStateIndex;
        
        /// <summary>Target state index.</summary>
        public ushort ToStateIndex;
        
        /// <summary>Normalized time in the "from" state (typically near end).</summary>
        public float FromNormalizedTime;
        
        /// <summary>Normalized time in the "to" state (typically starting from 0).</summary>
        public float ToNormalizedTime;
        
        /// <summary>Blend position for "from" state.</summary>
        public float2 FromBlendPosition;
        
        /// <summary>Blend position for "to" state.</summary>
        public float2 ToBlendPosition;
        
        /// <summary>Blend weight (0 = fully from, 1 = fully to).</summary>
        public float BlendWeight;
        
        /// <summary>Transition index for curve lookup (-1 if no curve).</summary>
        public short TransitionIndex;
        
        /// <summary>Source of transition curve data.</summary>
        public TransitionSource CurveSource;
        
        /// <summary>Whether this request is valid and should be processed.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool IsActive;
        
        public bool IsValid => IsActive;
        public TimelineSectionType SectionType => TimelineSectionType.Transition;
        
        public static AnimationTransitionRenderRequest None => new() { IsActive = false };
        
        /// <summary>Creates a transition render request.</summary>
        public static AnimationTransitionRenderRequest Create(
            ushort fromStateIndex,
            ushort toStateIndex,
            float fromNormalizedTime,
            float toNormalizedTime,
            float blendWeight,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default,
            short transitionIndex = -1,
            TransitionSource curveSource = TransitionSource.State)
        {
            return new AnimationTransitionRenderRequest
            {
                FromStateIndex = fromStateIndex,
                ToStateIndex = toStateIndex,
                FromNormalizedTime = fromNormalizedTime,
                ToNormalizedTime = toNormalizedTime,
                FromBlendPosition = fromBlendPosition,
                ToBlendPosition = toBlendPosition,
                BlendWeight = blendWeight,
                TransitionIndex = transitionIndex,
                CurveSource = curveSource,
                IsActive = true
            };
        }
    }
    
    /// <summary>
    /// Aggregate component that tracks which render request type is active.
    /// Systems check this first to determine which request component to read.
    /// </summary>
    internal struct ActiveRenderRequest : IComponentData
    {
        public RenderRequestType Type;
        
        public bool HasActiveRequest => Type != RenderRequestType.None;
        
        public static ActiveRenderRequest None => new() { Type = RenderRequestType.None };
        public static ActiveRenderRequest State => new() { Type = RenderRequestType.State };
        public static ActiveRenderRequest Transition => new() { Type = RenderRequestType.Transition };
    }
    
    internal enum RenderRequestType : byte
    {
        /// <summary>No active request - use normal state machine behavior.</summary>
        None = 0,
        
        /// <summary>Single state render request is active.</summary>
        State,
        
        /// <summary>Transition render request is active.</summary>
        Transition
    }
}
