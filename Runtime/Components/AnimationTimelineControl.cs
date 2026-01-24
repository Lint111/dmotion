using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Tag component added when timeline control is active.
    /// Used by animation systems to skip processing on timeline-controlled entities.
    /// This is a public tag so animation systems can use [WithNone(typeof(TimelineControlled))].
    /// </summary>
    public struct TimelineControlled : IComponentData { }
    
    /// <summary>
    /// Marks an entity as controllable by the animation timeline scrubber.
    /// When active, the AnimationTimelineControllerSystem overrides normal
    /// state machine playback with explicit pose requests.
    /// </summary>
    internal struct AnimationScrubberTarget : IComponentData
    {
        /// <summary>Whether timeline control is active. When false, normal playback resumes.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool IsActive;
        
        /// <summary>Whether playback is paused. When paused, time doesn't advance automatically.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool IsPaused;
        
        /// <summary>Playback speed multiplier. 0 = paused, 1 = normal, 0.5 = half-speed.</summary>
        public float TimeScale;
        
        public static AnimationScrubberTarget Default => new()
        {
            IsActive = false,
            IsPaused = false,
            TimeScale = 1f
        };
    }
    
    /// <summary>
    /// Command sent from editor to control the animation timeline.
    /// Processed and cleared by AnimationTimelineControllerSystem.
    /// </summary>
    internal struct AnimationTimelineCommand : IComponentData
    {
        public TimelineCommandType Type;
        
        /// <summary>Target normalized time for state scrubbing (0-1).</summary>
        public float TargetNormalizedTime;
        
        /// <summary>Target transition progress for transition scrubbing (0-1).</summary>
        public float TransitionProgress;
        
        /// <summary>Frame step direction (+1 forward, -1 backward).</summary>
        public int FrameStep;
        
        /// <summary>Frames per second for frame stepping.</summary>
        public float FrameRate;
        
        public static AnimationTimelineCommand None => new() { Type = TimelineCommandType.None };
        
        public static AnimationTimelineCommand Play() => new() { Type = TimelineCommandType.Play };
        public static AnimationTimelineCommand Pause() => new() { Type = TimelineCommandType.Pause };
        
        public static AnimationTimelineCommand ScrubState(float normalizedTime) => new()
        {
            Type = TimelineCommandType.ScrubState,
            TargetNormalizedTime = normalizedTime
        };
        
        public static AnimationTimelineCommand ScrubTransition(float progress) => new()
        {
            Type = TimelineCommandType.ScrubTransition,
            TransitionProgress = progress
        };
        
        public static AnimationTimelineCommand Step(int frames, float fps = 30f) => new()
        {
            Type = TimelineCommandType.StepFrame,
            FrameStep = frames,
            FrameRate = fps
        };
    }
    
    internal enum TimelineCommandType : byte
    {
        /// <summary>No command - system ignores this.</summary>
        None = 0,
        
        /// <summary>Resume normal playback.</summary>
        Play,
        
        /// <summary>Pause playback, freeze at current pose.</summary>
        Pause,
        
        /// <summary>Scrub to specific time within current state.</summary>
        ScrubState,
        
        /// <summary>Scrub transition progress (0 = from state, 1 = to state).</summary>
        ScrubTransition,
        
        /// <summary>Step forward/backward by frames.</summary>
        StepFrame,
        
        /// <summary>Apply a parameter override.</summary>
        SetParameter
    }
    
    /// <summary>
    /// Identifies which section of the preview timeline we're in.
    /// Used for UI rendering and section-specific behavior.
    /// </summary>
    internal enum TimelineSectionType : byte
    {
        /// <summary>Regular state section.</summary>
        State = 0,
        
        /// <summary>Transition blend section.</summary>
        Transition,
        
        /// <summary>Ghost bar before transition (FROM state context).</summary>
        GhostFrom,
        
        /// <summary>Ghost bar after transition (TO state context).</summary>
        GhostTo
    }
    
    /// <summary>
    /// Defines a section of the animation preview timeline.
    /// The timeline is composed of sections: [GhostFrom?] [Transition] [GhostTo?] or just [State].
    /// </summary>
    internal struct TimelineSection : IBufferElementData
    {
        public TimelineSectionType Type;
        
        /// <summary>State index for State/Ghost sections.</summary>
        public ushort StateIndex;
        
        /// <summary>From state for Transition sections.</summary>
        public ushort FromStateIndex;
        
        /// <summary>To state for Transition sections.</summary>
        public ushort ToStateIndex;
        
        /// <summary>Duration of this section in seconds.</summary>
        public float Duration;
        
        /// <summary>Start time of this section relative to timeline start.</summary>
        public float StartTime;
        
        /// <summary>Transition index for curve lookup (Transition sections only).</summary>
        public short TransitionIndex;
        
        /// <summary>Curve source for transition.</summary>
        public TransitionSource CurveSource;
        
        /// <summary>Blend position for this section (states) or "from" blend (transitions).</summary>
        public float2 BlendPosition;
        
        /// <summary>Blend position for "to" state (transitions only).</summary>
        public float2 ToBlendPosition;
        
        public float EndTime => StartTime + Duration;
        
        /// <summary>Creates a state section.</summary>
        public static TimelineSection State(ushort stateIndex, float duration, float startTime, float2 blendPosition = default)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.State,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a ghost "from" section (before transition).</summary>
        public static TimelineSection GhostFrom(ushort stateIndex, float duration, float startTime, float2 blendPosition = default)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.GhostFrom,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a ghost "to" section (after transition).</summary>
        public static TimelineSection GhostTo(ushort stateIndex, float duration, float startTime, float2 blendPosition = default)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.GhostTo,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a transition section.</summary>
        public static TimelineSection Transition(
            ushort fromStateIndex, ushort toStateIndex,
            float duration, float startTime,
            short transitionIndex, TransitionSource curveSource,
            float2 fromBlendPosition = default, float2 toBlendPosition = default)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.Transition,
                FromStateIndex = fromStateIndex,
                ToStateIndex = toStateIndex,
                Duration = duration,
                StartTime = startTime,
                TransitionIndex = transitionIndex,
                CurveSource = curveSource,
                BlendPosition = fromBlendPosition,
                ToBlendPosition = toBlendPosition
            };
        }
    }
    
    /// <summary>
    /// Current position in the animation timeline.
    /// Updated by AnimationTimelineControllerSystem.
    /// </summary>
    internal struct AnimationTimelinePosition : IComponentData
    {
        /// <summary>Current time in seconds from timeline start.</summary>
        public float CurrentTime;
        
        /// <summary>Total timeline duration in seconds.</summary>
        public float TotalDuration;
        
        /// <summary>Index of the current section in TimelineSection buffer.</summary>
        public int CurrentSectionIndex;
        
        /// <summary>Normalized progress within current section (0-1).</summary>
        public float SectionProgress;
        
        /// <summary>Overall timeline progress (0-1).</summary>
        public float NormalizedTime => TotalDuration > 0 ? CurrentTime / TotalDuration : 0;
    }
}
