using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    #region Composable Config Structs
    
    /// <summary>
    /// Animation time range within a section (normalized 0-1).
    /// </summary>
    public struct AnimTimeRange
    {
        public float Start;
        public float End;
        
        public static AnimTimeRange FullClip => new() { Start = 0f, End = 1f };
        
        public AnimTimeRange(float start, float end)
        {
            Start = start;
            End = end;
        }
    }
    
    /// <summary>
    /// Represents a state node - the identity and playback parameters of a single state.
    /// Used as a building block for section configs.
    /// </summary>
    public struct StateNode
    {
        /// <summary>Index into the state machine's States array.</summary>
        public ushort StateIndex;
        
        /// <summary>The state's natural clip duration (for normalized time calculations).</summary>
        public float ClipDuration;
        
        /// <summary>Playback speed multiplier.</summary>
        public float Speed;
        
        /// <summary>Blend position for blend states.</summary>
        public float2 BlendPosition;
        
        public static StateNode Create(ushort index, float clipDuration, float speed, float2 blendPos)
        {
            return new StateNode
            {
                StateIndex = index,
                ClipDuration = clipDuration,
                Speed = speed,
                BlendPosition = blendPos
            };
        }
    }
    
    /// <summary>
    /// Render parameters for a timeline section - timing and animation range.
    /// </summary>
    public struct SectionRenderParams
    {
        /// <summary>Start time of this section in the timeline (seconds).</summary>
        public float StartTime;
        
        /// <summary>Duration of this section (seconds).</summary>
        public float SectionDuration;
        
        /// <summary>Animation time range to play during this section.</summary>
        public AnimTimeRange AnimRange;
        
        public static SectionRenderParams Create(float startTime, float sectionDuration, AnimTimeRange animRange)
        {
            return new SectionRenderParams
            {
                StartTime = startTime,
                SectionDuration = sectionDuration,
                AnimRange = animRange
            };
        }
        
        public static SectionRenderParams FullClip(float startTime, float sectionDuration)
        {
            return new SectionRenderParams
            {
                StartTime = startTime,
                SectionDuration = sectionDuration,
                AnimRange = AnimTimeRange.FullClip
            };
        }
    }
    
    /// <summary>
    /// Transition-specific metadata.
    /// </summary>
    public struct TransitionInfo
    {
        public short TransitionIndex;
        public TransitionSource CurveSource;
        
        public static TransitionInfo Create(short index, TransitionSource source)
        {
            return new TransitionInfo { TransitionIndex = index, CurveSource = source };
        }
    }
    
    #endregion
    
    #region Section Config Structs (Composed)
    
    /// <summary>
    /// Configuration for a single-state timeline section.
    /// Used by State, GhostFrom, GhostTo, FromBar, ToBar sections.
    /// Composes: StateNode + SectionRenderParams
    /// </summary>
    public struct StateSectionConfig
    {
        public StateNode State;
        public SectionRenderParams Render;
        
        // Convenience accessors
        public readonly ushort StateIndex => State.StateIndex;
        public readonly float Speed => State.Speed;
        public readonly float2 BlendPosition => State.BlendPosition;
        public readonly float StartTime => Render.StartTime;
        public readonly float Duration => Render.SectionDuration;
        public readonly AnimTimeRange AnimRange => Render.AnimRange;
        
        public static StateSectionConfig Create(StateNode state, SectionRenderParams render)
        {
            return new StateSectionConfig { State = state, Render = render };
        }
        
        /// <summary>Creates config for a full-clip section (anim 0→1).</summary>
        public static StateSectionConfig FullClip(ushort stateIndex, float clipDuration, float speed, float2 blendPos, float startTime, float sectionDuration)
        {
            return new StateSectionConfig
            {
                State = StateNode.Create(stateIndex, clipDuration, speed, blendPos),
                Render = SectionRenderParams.FullClip(startTime, sectionDuration)
            };
        }
    }
    
    /// <summary>
    /// Configuration for a transition timeline section.
    /// Composes: FromState + ToState + RenderParams + TransitionInfo
    /// </summary>
    public struct TransitionSectionConfig
    {
        public StateNode FromState;
        public StateNode ToState;
        public SectionRenderParams Render;
        public AnimTimeRange ToAnimRange;  // FROM uses Render.AnimRange, TO has separate range
        public TransitionInfo Transition;
        
        // Convenience accessors
        public readonly ushort FromStateIndex => FromState.StateIndex;
        public readonly ushort ToStateIndex => ToState.StateIndex;
        public readonly float StartTime => Render.StartTime;
        public readonly float Duration => Render.SectionDuration;
        public readonly AnimTimeRange FromAnimRange => Render.AnimRange;
        public readonly short TransitionIndex => Transition.TransitionIndex;
        public readonly TransitionSource CurveSource => Transition.CurveSource;
    }
    
    #endregion
    
    #region Preview Config Structs
    
    /// <summary>
    /// Section durations for transition preview timeline.
    /// Matches the layout: [GhostFrom?] [FromBar] [Transition] [ToBar] [GhostTo?]
    /// </summary>
    public struct TransitionSectionDurations
    {
        public float GhostFromDuration;
        public float FromBarDuration;
        public float TransitionDuration;
        public float ToBarDuration;
        public float GhostToDuration;
        
        public readonly float TotalDuration => GhostFromDuration + FromBarDuration + TransitionDuration + ToBarDuration + GhostToDuration;
    }
    
    /// <summary>
    /// Complete configuration for transition preview setup.
    /// Bundles all parameters needed by SetupTransitionPreview.
    /// </summary>
    public struct TransitionPreviewConfig
    {
        public StateNode FromState;
        public StateNode ToState;
        public TransitionSectionDurations Sections;
        public TransitionInfo Transition;
        
        // Legacy accessors for compatibility
        public readonly short TransitionIndex => Transition.TransitionIndex;
        public readonly TransitionSource CurveSource => Transition.CurveSource;
        
        /// <summary>
        /// Factory method for creating TransitionPreviewConfig from individual parameters.
        /// </summary>
        public static TransitionPreviewConfig Create(
            ushort fromStateIndex, float fromStateDuration, float fromSpeed, float2 fromBlendPosition,
            ushort toStateIndex, float toStateDuration, float toSpeed, float2 toBlendPosition,
            float transitionDuration, float fromBarDuration, float toBarDuration,
            float ghostFromDuration, float ghostToDuration,
            short transitionIndex, TransitionSource curveSource)
        {
            return new TransitionPreviewConfig
            {
                FromState = StateNode.Create(fromStateIndex, fromStateDuration, fromSpeed, fromBlendPosition),
                ToState = StateNode.Create(toStateIndex, toStateDuration, toSpeed, toBlendPosition),
                Sections = new TransitionSectionDurations
                {
                    GhostFromDuration = ghostFromDuration,
                    FromBarDuration = fromBarDuration,
                    TransitionDuration = transitionDuration,
                    ToBarDuration = toBarDuration,
                    GhostToDuration = ghostToDuration
                },
                Transition = TransitionInfo.Create(transitionIndex, curveSource)
            };
        }
    }
    
    #endregion
    
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
    /// 
    /// Transition layout:
    ///   [GhostFrom?] [FromBar] [Transition/Overlap] [ToBar] [GhostTo?]
    ///        ↓           ↓              ↓              ↓         ↓
    ///    Optional    FROM@100%     Blend zone     TO@100%    Optional
    /// </summary>
    internal enum TimelineSectionType : byte
    {
        /// <summary>Regular state section (single state preview).</summary>
        State = 0,
        
        /// <summary>Transition blend/overlap section (crossfade between FROM and TO).</summary>
        Transition,
        
        /// <summary>Ghost bar before transition (FROM state context, optional).</summary>
        GhostFrom,
        
        /// <summary>Ghost bar after transition (TO state context, optional).</summary>
        GhostTo,
        
        /// <summary>FROM state bar at 100% weight (before transition overlap begins).</summary>
        FromBar,
        
        /// <summary>TO state bar at 100% weight (after transition overlap ends).</summary>
        ToBar
    }
    
    /// <summary>
    /// Defines a section of the animation preview timeline.
    /// Each section is a self-contained rendering block with its own 0→1 progress
    /// that maps to a specific animation time range.
    /// </summary>
    internal struct TimelineSection : IBufferElementData
    {
        public TimelineSectionType Type;
        
        /// <summary>State index for single-state sections (State/Ghost/FromBar/ToBar).</summary>
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
        
        /// <summary>Playback speed for this section's state.</summary>
        public float Speed;
        
        /// <summary>Playback speed for TO state (transitions only, for blending).</summary>
        public float ToSpeed;
        
        /// <summary>Animation normalized time at section progress=0.</summary>
        public float AnimStartTime;
        
        /// <summary>Animation normalized time at section progress=1.</summary>
        public float AnimEndTime;
        
        /// <summary>TO state animation normalized time at section progress=0 (transitions only).</summary>
        public float ToAnimStartTime;
        
        /// <summary>TO state animation normalized time at section progress=1 (transitions only).</summary>
        public float ToAnimEndTime;
        
        public float EndTime => StartTime + Duration;
        
        /// <summary>Gets the animation normalized time for a given section progress (0-1).</summary>
        public float GetAnimTime(float progress) => math.lerp(AnimStartTime, AnimEndTime, progress);
        
        /// <summary>Gets the TO state animation normalized time for a given section progress (transitions only).</summary>
        public float GetToAnimTime(float progress) => math.lerp(ToAnimStartTime, ToAnimEndTime, progress);
        
        #region Factory Methods (Config-based - Preferred)
        
        /// <summary>Creates a state section from config.</summary>
        public static TimelineSection State(in StateSectionConfig config)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.State,
                StateIndex = config.StateIndex,
                Duration = config.Duration,
                StartTime = config.StartTime,
                BlendPosition = config.BlendPosition,
                Speed = config.Speed,
                AnimStartTime = config.AnimRange.Start,
                AnimEndTime = config.AnimRange.End,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a ghost "from" section from config.</summary>
        public static TimelineSection GhostFrom(in StateSectionConfig config)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.GhostFrom,
                StateIndex = config.StateIndex,
                Duration = config.Duration,
                StartTime = config.StartTime,
                BlendPosition = config.BlendPosition,
                Speed = config.Speed,
                AnimStartTime = config.AnimRange.Start,
                AnimEndTime = config.AnimRange.End,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a ghost "to" section from config.</summary>
        public static TimelineSection GhostTo(in StateSectionConfig config)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.GhostTo,
                StateIndex = config.StateIndex,
                Duration = config.Duration,
                StartTime = config.StartTime,
                BlendPosition = config.BlendPosition,
                Speed = config.Speed,
                AnimStartTime = config.AnimRange.Start,
                AnimEndTime = config.AnimRange.End,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a FROM bar section from config.</summary>
        public static TimelineSection FromBar(in StateSectionConfig config)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.FromBar,
                StateIndex = config.StateIndex,
                Duration = config.Duration,
                StartTime = config.StartTime,
                BlendPosition = config.BlendPosition,
                Speed = config.Speed,
                AnimStartTime = config.AnimRange.Start,
                AnimEndTime = config.AnimRange.End,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a TO bar section from config.</summary>
        public static TimelineSection ToBar(in StateSectionConfig config)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.ToBar,
                StateIndex = config.StateIndex,
                Duration = config.Duration,
                StartTime = config.StartTime,
                BlendPosition = config.BlendPosition,
                Speed = config.Speed,
                AnimStartTime = config.AnimRange.Start,
                AnimEndTime = config.AnimRange.End,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a transition section from config.</summary>
        public static TimelineSection Transition(in TransitionSectionConfig config)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.Transition,
                FromStateIndex = config.FromState.StateIndex,
                StateIndex = config.FromState.StateIndex,
                ToStateIndex = config.ToState.StateIndex,
                Duration = config.Duration,
                StartTime = config.StartTime,
                TransitionIndex = config.TransitionIndex,
                CurveSource = config.CurveSource,
                BlendPosition = config.FromState.BlendPosition,
                ToBlendPosition = config.ToState.BlendPosition,
                Speed = config.FromState.Speed,
                ToSpeed = config.ToState.Speed,
                AnimStartTime = config.FromAnimRange.Start,
                AnimEndTime = config.FromAnimRange.End,
                ToAnimStartTime = config.ToAnimRange.Start,
                ToAnimEndTime = config.ToAnimRange.End
            };
        }
        
        #endregion
        
        #region Factory Methods (Legacy - Parameter-based)
        
        /// <summary>Creates a state section (full clip, 0→1).</summary>
        public static TimelineSection State(ushort stateIndex, float duration, float startTime, float2 blendPosition, float speed)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.State,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                Speed = speed,
                AnimStartTime = 0f,
                AnimEndTime = 1f,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a ghost "from" section (previous cycle, 0→1).</summary>
        public static TimelineSection GhostFrom(ushort stateIndex, float duration, float startTime, float2 blendPosition, float speed)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.GhostFrom,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                Speed = speed,
                AnimStartTime = 0f,
                AnimEndTime = 1f,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a ghost "to" section (continuation cycle).</summary>
        public static TimelineSection GhostTo(ushort stateIndex, float duration, float startTime, float2 blendPosition, float speed, float animStartTime, float animEndTime)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.GhostTo,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                Speed = speed,
                AnimStartTime = animStartTime,
                AnimEndTime = animEndTime,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a transition section with animation time ranges for both states.</summary>
        public static TimelineSection Transition(
            ushort fromStateIndex, ushort toStateIndex,
            float duration, float startTime,
            short transitionIndex, TransitionSource curveSource,
            float2 fromBlendPosition, float2 toBlendPosition,
            float fromSpeed, float toSpeed,
            float fromAnimStart, float fromAnimEnd,
            float toAnimStart, float toAnimEnd)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.Transition,
                FromStateIndex = fromStateIndex,
                StateIndex = fromStateIndex, // Also set StateIndex for consistency
                ToStateIndex = toStateIndex,
                Duration = duration,
                StartTime = startTime,
                TransitionIndex = transitionIndex,
                CurveSource = curveSource,
                BlendPosition = fromBlendPosition,
                ToBlendPosition = toBlendPosition,
                Speed = fromSpeed,
                ToSpeed = toSpeed,
                AnimStartTime = fromAnimStart,
                AnimEndTime = fromAnimEnd,
                ToAnimStartTime = toAnimStart,
                ToAnimEndTime = toAnimEnd
            };
        }
        
        /// <summary>Creates a FROM bar section with animation time range.</summary>
        public static TimelineSection FromBar(ushort stateIndex, float duration, float startTime, float2 blendPosition, float speed, float animStartTime, float animEndTime)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.FromBar,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                Speed = speed,
                AnimStartTime = animStartTime,
                AnimEndTime = animEndTime,
                TransitionIndex = -1
            };
        }
        
        /// <summary>Creates a TO bar section with animation time range.</summary>
        public static TimelineSection ToBar(ushort stateIndex, float duration, float startTime, float2 blendPosition, float speed, float animStartTime, float animEndTime)
        {
            return new TimelineSection
            {
                Type = TimelineSectionType.ToBar,
                StateIndex = stateIndex,
                Duration = duration,
                StartTime = startTime,
                BlendPosition = blendPosition,
                Speed = speed,
                AnimStartTime = animStartTime,
                AnimEndTime = animEndTime,
                TransitionIndex = -1
            };
        }
        
        #endregion
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
