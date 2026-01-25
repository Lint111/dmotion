using Latios.Kinemation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Processes animation timeline commands and generates render requests.
    /// 
    /// This system enables editor-driven animation preview by:
    /// 1. Processing commands (play, pause, scrub, step)
    /// 2. Managing timeline position across sections
    /// 3. Outputting specific render requests (State or Transition) for downstream systems
    /// 
    /// When AnimationScrubberTarget.IsActive is true, normal state machine
    /// playback is overridden by the render request systems.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(AnimationStateMachineSystem))]
    public partial struct AnimationTimelineControllerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AnimationScrubberTarget>();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            
            var stateMachineLookup = SystemAPI.GetComponentLookup<AnimationStateMachine>(true);
            
            foreach (var (scrubber, command, position, activeRequest, stateRequest, transitionRequest, sections, entity) in
                SystemAPI.Query<
                    RefRW<AnimationScrubberTarget>,
                    RefRW<AnimationTimelineCommand>,
                    RefRW<AnimationTimelinePosition>,
                    RefRW<ActiveRenderRequest>,
                    RefRW<AnimationStateRenderRequest>,
                    RefRW<AnimationTransitionRenderRequest>,
                    DynamicBuffer<TimelineSection>>()
                .WithEntityAccess())
            {
                bool hasTag = state.EntityManager.HasComponent<TimelineControlled>(entity);
                
                if (!scrubber.ValueRO.IsActive)
                {
                    // Timeline control not active - clear requests so normal systems take over
                    activeRequest.ValueRW = ActiveRenderRequest.None;
                    stateRequest.ValueRW = AnimationStateRenderRequest.None;
                    transitionRequest.ValueRW = AnimationTransitionRenderRequest.None;
                    
                    // Remove tag if present
                    if (hasTag)
                    {
                        ecb.RemoveComponent<TimelineControlled>(entity);
                    }
                    continue;
                }
                
                // Add tag if not present (timeline is active)
                if (!hasTag)
                {
                    ecb.AddComponent<TimelineControlled>(entity);
                }
                
                // Process command
                ProcessCommand(
                    ref scrubber.ValueRW,
                    ref command.ValueRW,
                    ref position.ValueRW,
                    sections,
                    deltaTime);
                
                // If not paused, advance time
                if (!scrubber.ValueRO.IsPaused && scrubber.ValueRO.TimeScale > 0)
                {
                    AdvanceTime(ref position.ValueRW, sections, deltaTime * scrubber.ValueRO.TimeScale);
                }
                
                // Generate render request from current position
                var stateMachine = stateMachineLookup.HasComponent(entity) 
                    ? stateMachineLookup[entity] 
                    : default;
                GenerateRenderRequest(
                    ref activeRequest.ValueRW,
                    ref stateRequest.ValueRW,
                    ref transitionRequest.ValueRW,
                    position.ValueRO,
                    sections,
                    stateMachine);
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
        
        [BurstCompile]
        private static void ProcessCommand(
            ref AnimationScrubberTarget scrubber,
            ref AnimationTimelineCommand command,
            ref AnimationTimelinePosition position,
            in DynamicBuffer<TimelineSection> sections,
            float deltaTime)
        {
            switch (command.Type)
            {
                case TimelineCommandType.None:
                    return;
                    
                case TimelineCommandType.Play:
                    scrubber.IsPaused = false;
                    // If at end of timeline, restart from beginning
                    if (position.TotalDuration > 0 && position.CurrentTime >= position.TotalDuration - 0.001f)
                    {
                        position.CurrentTime = 0f;
                        position.CurrentSectionIndex = 0;
                        position.SectionProgress = 0f;
                    }
                    break;
                    
                case TimelineCommandType.Pause:
                    scrubber.IsPaused = true;
                    break;
                    
                case TimelineCommandType.ScrubState:
                    ScrubToNormalizedTime(ref position, sections, command.TargetNormalizedTime);
                    scrubber.IsPaused = true; // Scrubbing implies pause
                    break;
                    
                case TimelineCommandType.ScrubTransition:
                    ScrubTransitionProgress(ref position, sections, command.TransitionProgress);
                    scrubber.IsPaused = true;
                    break;
                    
                case TimelineCommandType.StepFrame:
                    float frameDuration = command.FrameRate > 0 ? 1f / command.FrameRate : 1f / 30f;
                    float stepTime = command.FrameStep * frameDuration;
                    AdvanceTime(ref position, sections, stepTime);
                    scrubber.IsPaused = true;
                    break;
            }
            
            // Clear command after processing
            command.Type = TimelineCommandType.None;
        }
        
        [BurstCompile]
        private static void ScrubToNormalizedTime(
            ref AnimationTimelinePosition position,
            in DynamicBuffer<TimelineSection> sections,
            float normalizedTime)
        {
            if (sections.Length == 0) return;
            
            // Calculate total duration
            float totalDuration = 0f;
            for (int i = 0; i < sections.Length; i++)
            {
                totalDuration += sections[i].Duration;
            }
            
            position.TotalDuration = totalDuration;
            position.CurrentTime = math.saturate(normalizedTime) * totalDuration;
            
            // Find which section we're in
            UpdateCurrentSection(ref position, sections);
        }
        
        [BurstCompile]
        private static void ScrubTransitionProgress(
            ref AnimationTimelinePosition position,
            in DynamicBuffer<TimelineSection> sections,
            float progress)
        {
            if (sections.Length == 0) return;
            
            // Find the transition section
            float timeOffset = 0f;
            for (int i = 0; i < sections.Length; i++)
            {
                var section = sections[i];
                if (section.Type == TimelineSectionType.Transition)
                {
                    // Found transition - set time to be within this section at given progress
                    position.CurrentTime = timeOffset + math.saturate(progress) * section.Duration;
                    position.CurrentSectionIndex = i;
                    position.SectionProgress = math.saturate(progress);
                    return;
                }
                timeOffset += section.Duration;
            }
        }
        
        [BurstCompile]
        private static void AdvanceTime(
            ref AnimationTimelinePosition position,
            in DynamicBuffer<TimelineSection> sections,
            float deltaTime)
        {
            if (sections.Length == 0) return;
            
            position.CurrentTime += deltaTime;
            
            // Clamp to timeline bounds
            position.CurrentTime = math.clamp(position.CurrentTime, 0f, position.TotalDuration);
            
            UpdateCurrentSection(ref position, sections);
        }
        
        [BurstCompile]
        private static void UpdateCurrentSection(
            ref AnimationTimelinePosition position,
            in DynamicBuffer<TimelineSection> sections)
        {
            float time = position.CurrentTime;
            float accumulated = 0f;
            
            for (int i = 0; i < sections.Length; i++)
            {
                var section = sections[i];
                float sectionEnd = accumulated + section.Duration;
                
                if (time <= sectionEnd || i == sections.Length - 1)
                {
                    position.CurrentSectionIndex = i;
                    float localTime = time - accumulated;
                    position.SectionProgress = section.Duration > 0 
                        ? math.saturate(localTime / section.Duration) 
                        : 0f;
                    return;
                }
                
                accumulated = sectionEnd;
            }
        }
        
        [BurstCompile]
        private static void GenerateRenderRequest(
            ref ActiveRenderRequest activeRequest,
            ref AnimationStateRenderRequest stateRequest,
            ref AnimationTransitionRenderRequest transitionRequest,
            in AnimationTimelinePosition position,
            in DynamicBuffer<TimelineSection> sections,
            in AnimationStateMachine stateMachine)
        {
            if (sections.Length == 0 || position.CurrentSectionIndex < 0 || position.CurrentSectionIndex >= sections.Length)
            {
                activeRequest = ActiveRenderRequest.None;
                stateRequest = AnimationStateRenderRequest.None;
                transitionRequest = AnimationTransitionRenderRequest.None;
                return;
            }
            
            var section = sections[position.CurrentSectionIndex];
            float progress = position.SectionProgress;
            
            switch (section.Type)
            {
                case TimelineSectionType.State:
                    activeRequest = ActiveRenderRequest.State;
                    stateRequest = AnimationStateRenderRequest.Create(
                        section.StateIndex,
                        section.GetAnimTime(progress),
                        section.BlendPosition,
                        TimelineSectionType.State);
                    transitionRequest = AnimationTransitionRenderRequest.None;
                    break;
                    
                case TimelineSectionType.GhostFrom:
                    activeRequest = ActiveRenderRequest.State;
                    stateRequest = AnimationStateRenderRequest.GhostFrom(
                        section.StateIndex,
                        section.GetAnimTime(progress),
                        section.BlendPosition);
                    transitionRequest = AnimationTransitionRenderRequest.None;
                    break;
                    
                case TimelineSectionType.GhostTo:
                    activeRequest = ActiveRenderRequest.State;
                    stateRequest = AnimationStateRenderRequest.GhostTo(
                        section.StateIndex,
                        section.GetAnimTime(progress),
                        section.BlendPosition);
                    transitionRequest = AnimationTransitionRenderRequest.None;
                    break;
                    
                case TimelineSectionType.FromBar:
                    activeRequest = ActiveRenderRequest.State;
                    stateRequest = AnimationStateRenderRequest.Create(
                        section.StateIndex,
                        section.GetAnimTime(progress),
                        section.BlendPosition,
                        TimelineSectionType.FromBar);
                    transitionRequest = AnimationTransitionRenderRequest.None;
                    break;
                    
                case TimelineSectionType.ToBar:
                    activeRequest = ActiveRenderRequest.State;
                    stateRequest = AnimationStateRenderRequest.Create(
                        section.StateIndex,
                        section.GetAnimTime(progress),
                        section.BlendPosition,
                        TimelineSectionType.ToBar);
                    transitionRequest = AnimationTransitionRenderRequest.None;
                    break;
                    
                case TimelineSectionType.Transition:
                    activeRequest = ActiveRenderRequest.Transition;
                    stateRequest = AnimationStateRenderRequest.None;
                    
                    // Evaluate blend weight using transition curve if available
                    float blendWeight = EvaluateTransitionBlendWeight(
                        progress,
                        section.TransitionIndex,
                        section.CurveSource,
                        section.FromStateIndex,
                        stateMachine);
                    
                    // Both states use their own AnimStart→AnimEnd ranges
                    float fromNormalizedTime = section.GetAnimTime(progress);
                    float toNormalizedTime = section.GetToAnimTime(progress);
                    
                    transitionRequest = AnimationTransitionRenderRequest.Create(
                        section.FromStateIndex,
                        section.ToStateIndex,
                        fromNormalizedTime,
                        toNormalizedTime,
                        blendWeight: blendWeight,
                        section.BlendPosition,
                        section.ToBlendPosition,
                        section.TransitionIndex,
                        section.CurveSource);
                    break;
                    
                default:
                    activeRequest = ActiveRenderRequest.None;
                    stateRequest = AnimationStateRenderRequest.None;
                    transitionRequest = AnimationTransitionRenderRequest.None;
                    break;
            }
        }
        
        /// <summary>
        /// Evaluates transition blend weight using the curve if available.
        /// Falls back to linear interpolation if no curve is defined.
        /// </summary>
        [BurstCompile]
        private static float EvaluateTransitionBlendWeight(
            float normalizedTime,
            short transitionIndex,
            TransitionSource curveSource,
            ushort fromStateIndex,
            in AnimationStateMachine stateMachine)
        {
            // No curve = linear blend
            if (transitionIndex < 0 || !stateMachine.StateMachineBlob.IsCreated)
                return normalizedTime;
            
            ref var smBlob = ref stateMachine.StateMachineBlob.Value;
            
            switch (curveSource)
            {
                case TransitionSource.State:
                {
                    // State transition - look up from States[fromStateIndex].Transitions[transitionIndex]
                    if (fromStateIndex < smBlob.States.Length)
                    {
                        ref var state = ref smBlob.States[fromStateIndex];
                        if (transitionIndex < state.Transitions.Length)
                        {
                            ref var transition = ref state.Transitions[transitionIndex];
                            if (transition.HasCurve)
                            {
                                return CurveUtils.EvaluateCurve(ref transition.CurveKeyframes, normalizedTime);
                            }
                        }
                    }
                    break;
                }
                
                case TransitionSource.AnyState:
                {
                    // Any State transition - look up from AnyStateTransitions[transitionIndex]
                    if (transitionIndex < smBlob.AnyStateTransitions.Length)
                    {
                        ref var transition = ref smBlob.AnyStateTransitions[transitionIndex];
                        if (transition.HasCurve)
                        {
                            return CurveUtils.EvaluateCurve(ref transition.CurveKeyframes, normalizedTime);
                        }
                    }
                    break;
                }
            }
            
            // Fallback to linear
            return normalizedTime;
        }
    }
    
    /// <summary>
    /// Helper methods for setting up timeline control on entities.
    /// </summary>
    internal static class AnimationTimelineControlExtensions
    {
        /// <summary>
        /// Adds timeline control components to an entity.
        /// </summary>
        public static void AddTimelineControl(this EntityManager em, Entity entity)
        {
            if (!em.HasComponent<AnimationScrubberTarget>(entity))
                em.AddComponent<AnimationScrubberTarget>(entity);
            
            if (!em.HasComponent<AnimationTimelineCommand>(entity))
                em.AddComponent<AnimationTimelineCommand>(entity);
            
            if (!em.HasComponent<AnimationTimelinePosition>(entity))
                em.AddComponent<AnimationTimelinePosition>(entity);
            
            if (!em.HasComponent<ActiveRenderRequest>(entity))
                em.AddComponent<ActiveRenderRequest>(entity);
            
            if (!em.HasComponent<AnimationStateRenderRequest>(entity))
                em.AddComponent<AnimationStateRenderRequest>(entity);
            
            if (!em.HasComponent<AnimationTransitionRenderRequest>(entity))
                em.AddComponent<AnimationTransitionRenderRequest>(entity);
            
            if (!em.HasBuffer<TimelineSection>(entity))
                em.AddBuffer<TimelineSection>(entity);
            
            em.SetComponentData(entity, AnimationScrubberTarget.Default);
            em.SetComponentData(entity, AnimationTimelineCommand.None);
            em.SetComponentData(entity, ActiveRenderRequest.None);
            em.SetComponentData(entity, AnimationStateRenderRequest.None);
            em.SetComponentData(entity, AnimationTransitionRenderRequest.None);
        }
        
        /// <summary>
        /// Configures timeline for single state preview.
        /// </summary>
        public static void SetupStatePreview(
            this EntityManager em, 
            Entity entity,
            ushort stateIndex,
            float stateDuration,
            float2 blendPosition = default)
        {
            var sections = em.GetBuffer<TimelineSection>(entity);
            sections.Clear();
            
            // Single state section
            sections.Add(TimelineSection.State(stateIndex, stateDuration, 0f, blendPosition));
            
            // Reset position
            em.SetComponentData(entity, new AnimationTimelinePosition
            {
                CurrentTime = 0f,
                TotalDuration = stateDuration,
                CurrentSectionIndex = 0,
                SectionProgress = 0f
            });
        }
        
        /// <summary>
        /// Configures timeline for transition preview with all sections.
        /// Each section is a self-contained rendering block with its own animation time range.
        /// 
        /// Full layout: [GhostFrom?] [FromBar] [Transition] [ToBar] [GhostTo?]
        /// </summary>
        /// <param name="fromStateDuration">Full duration of FROM state clip (for normalized time calculation)</param>
        /// <param name="toStateDuration">Full duration of TO state clip (for normalized time calculation)</param>
        /// <param name="fromBarDuration">Duration of FromBar section in seconds (usually = exitTime)</param>
        /// <param name="toBarDuration">Duration of ToBar section in seconds (usually = toStateDuration - transitionDuration)</param>
        public static void SetupTransitionPreview(
            this EntityManager em,
            Entity entity,
            ushort fromStateIndex,
            ushort toStateIndex,
            float fromStateDuration,
            float toStateDuration,
            float transitionDuration,
            short transitionIndex,
            TransitionSource curveSource,
            float2 fromBlendPosition,
            float2 toBlendPosition,
            float fromBarDuration,
            float toBarDuration,
            float ghostFromDuration = 0f,
            float ghostToDuration = 0f)
        {
            var sections = em.GetBuffer<TimelineSection>(entity);
            sections.Clear();
            
            float time = 0f;
            
            // Track animation time progression for FROM and TO states
            float fromAnimTime = 0f;  // Current position in FROM animation (normalized)
            float toAnimTime = 0f;    // Current position in TO animation (normalized)
            
            // 1. Ghost FROM (previous cycle, plays 0→1)
            if (ghostFromDuration > 0 && fromStateDuration > 0)
            {
                sections.Add(TimelineSection.GhostFrom(fromStateIndex, ghostFromDuration, time, fromBlendPosition));
                time += ghostFromDuration;
                // Ghost plays full cycle, FROM animation resets to 0 for FromBar
                fromAnimTime = 0f;
            }
            
            // 2. FROM bar (FROM state at 100%, plays from 0 to exitTime/fromStateDuration)
            if (fromBarDuration > 0 && fromStateDuration > 0)
            {
                float fromBarAnimStart = fromAnimTime;
                float fromBarAnimEnd = fromAnimTime + (fromBarDuration / fromStateDuration);
                UnityEngine.Debug.Log($"[SetupTransitionPreview] FromBar: duration={fromBarDuration:F3}, stateDur={fromStateDuration:F3}, animStart={fromBarAnimStart:F3}, animEnd={fromBarAnimEnd:F3}");
                sections.Add(TimelineSection.FromBar(fromStateIndex, fromBarDuration, time, fromBlendPosition, fromBarAnimStart, fromBarAnimEnd));
                time += fromBarDuration;
                fromAnimTime = fromBarAnimEnd;
            }
            
            // 3. Transition (FROM continues, TO starts at 0)
            if (transitionDuration > 0)
            {
                float transFromAnimStart = fromAnimTime;
                float transFromAnimEnd = fromStateDuration > 0 ? fromAnimTime + (transitionDuration / fromStateDuration) : fromAnimTime;
                float transToAnimStart = toAnimTime;
                float transToAnimEnd = toStateDuration > 0 ? toAnimTime + (transitionDuration / toStateDuration) : toAnimTime;
                
                UnityEngine.Debug.Log($"[SetupTransitionPreview] Transition: duration={transitionDuration:F3}, fromAnim={transFromAnimStart:F3}→{transFromAnimEnd:F3}, toAnim={transToAnimStart:F3}→{transToAnimEnd:F3}");
                
                sections.Add(TimelineSection.Transition(
                    fromStateIndex, toStateIndex,
                    transitionDuration, time,
                    transitionIndex, curveSource,
                    fromBlendPosition, toBlendPosition,
                    transFromAnimStart, transFromAnimEnd,
                    transToAnimStart, transToAnimEnd));
                time += transitionDuration;
                fromAnimTime = transFromAnimEnd;
                toAnimTime = transToAnimEnd;
            }
            
            // 4. TO bar (TO state at 100%, continues from where transition ended)
            if (toBarDuration > 0 && toStateDuration > 0)
            {
                float toBarAnimStart = toAnimTime;
                float toBarAnimEnd = toAnimTime + (toBarDuration / toStateDuration);
                sections.Add(TimelineSection.ToBar(toStateIndex, toBarDuration, time, toBlendPosition, toBarAnimStart, toBarAnimEnd));
                time += toBarDuration;
                toAnimTime = toBarAnimEnd;
            }
            
            // 5. Ghost TO (continuation cycle, continues from where ToBar ended or starts new cycle)
            if (ghostToDuration > 0 && toStateDuration > 0)
            {
                // If we've already played a full cycle, start fresh; otherwise continue
                float ghostToAnimStart = toAnimTime >= 1f ? 0f : toAnimTime;
                float ghostToAnimEnd = ghostToAnimStart + (ghostToDuration / toStateDuration);
                sections.Add(TimelineSection.GhostTo(toStateIndex, ghostToDuration, time, toBlendPosition, ghostToAnimStart, ghostToAnimEnd));
                time += ghostToDuration;
            }
            
            em.SetComponentData(entity, new AnimationTimelinePosition
            {
                CurrentTime = 0f,
                TotalDuration = time,
                CurrentSectionIndex = 0,
                SectionProgress = 0f
            });
        }
        
        /// <summary>
        /// Activates timeline control on an entity.
        /// </summary>
        public static void ActivateTimelineControl(this EntityManager em, Entity entity, bool startPaused = true)
        {
            em.SetComponentData(entity, new AnimationScrubberTarget
            {
                IsActive = true,
                IsPaused = startPaused,
                TimeScale = 1f
            });
        }
        
        /// <summary>
        /// Deactivates timeline control, returning to normal state machine behavior.
        /// </summary>
        public static void DeactivateTimelineControl(this EntityManager em, Entity entity)
        {
            em.SetComponentData(entity, new AnimationScrubberTarget
            {
                IsActive = false,
                IsPaused = false,
                TimeScale = 1f
            });
        }
        
        /// <summary>
        /// Sends a command to the timeline controller.
        /// </summary>
        public static void SendTimelineCommand(this EntityManager em, Entity entity, AnimationTimelineCommand command)
        {
            em.SetComponentData(entity, command);
        }
        
        /// <summary>
        /// Sets up the AnimationState and ClipSampler buffers for previewing a specific state.
        /// This ensures the samplers match the state being previewed.
        /// </summary>
        public static bool SetupAnimationStateForPreview(this EntityManager em, Entity entity, ushort stateIndex)
        {
            if (!em.HasComponent<AnimationStateMachine>(entity))
                return false;
            
            var sm = em.GetComponentData<AnimationStateMachine>(entity);
            if (!sm.StateMachineBlob.IsCreated || !sm.ClipsBlob.IsCreated)
                return false;
            
            ref var smBlob = ref sm.StateMachineBlob.Value;
            if (stateIndex >= smBlob.States.Length)
                return false;
            
            ref var stateBlob = ref smBlob.States[stateIndex];
            
            var animationStates = em.GetBuffer<AnimationState>(entity);
            var samplers = em.GetBuffer<ClipSampler>(entity);
            
            // Clear existing states and samplers
            animationStates.Clear();
            samplers.Clear();
            
            // Set up based on state type
            switch (stateBlob.Type)
            {
                case StateType.Single:
                    SetupSingleClipState(ref smBlob, sm.ClipsBlob, sm.ClipEventsBlob, stateIndex, ref animationStates, ref samplers, stateBlob.Speed, stateBlob.Loop);
                    break;
                    
                case StateType.LinearBlend:
                    SetupLinearBlendState(ref smBlob, sm.ClipsBlob, sm.ClipEventsBlob, stateIndex, ref animationStates, ref samplers, stateBlob.Speed, stateBlob.Loop);
                    break;
                    
                case StateType.Directional2DBlend:
                    SetupDirectional2DState(ref smBlob, sm.ClipsBlob, sm.ClipEventsBlob, stateIndex, ref animationStates, ref samplers, stateBlob.Speed, stateBlob.Loop);
                    break;
                    
                default:
                    return false;
            }
            
            return animationStates.Length > 0;
        }
        
        /// <summary>
        /// Sets up the AnimationState and ClipSampler buffers for previewing a transition between two states.
        /// Creates AnimationState entries for both FROM and TO states with their respective samplers.
        /// </summary>
        public static bool SetupTransitionStatesForPreview(this EntityManager em, Entity entity, ushort fromStateIndex, ushort toStateIndex)
        {
            if (!em.HasComponent<AnimationStateMachine>(entity))
                return false;
            
            var sm = em.GetComponentData<AnimationStateMachine>(entity);
            if (!sm.StateMachineBlob.IsCreated || !sm.ClipsBlob.IsCreated)
                return false;
            
            ref var smBlob = ref sm.StateMachineBlob.Value;
            if (fromStateIndex >= smBlob.States.Length || toStateIndex >= smBlob.States.Length)
                return false;
            
            var animationStates = em.GetBuffer<AnimationState>(entity);
            var samplers = em.GetBuffer<ClipSampler>(entity);
            
            // Clear existing states and samplers ONCE
            animationStates.Clear();
            samplers.Clear();
            
            // Set up FROM state (index 0 in AnimationStates buffer)
            ref var fromStateBlob = ref smBlob.States[fromStateIndex];
            SetupStateForTransition(ref smBlob, sm.ClipsBlob, sm.ClipEventsBlob, fromStateIndex, 
                ref animationStates, ref samplers, fromStateBlob.Speed, fromStateBlob.Loop);
            
            // Set up TO state (index 1 in AnimationStates buffer)
            ref var toStateBlob = ref smBlob.States[toStateIndex];
            SetupStateForTransition(ref smBlob, sm.ClipsBlob, sm.ClipEventsBlob, toStateIndex, 
                ref animationStates, ref samplers, toStateBlob.Speed, toStateBlob.Loop);
            
            return animationStates.Length >= 2;
        }
        
        /// <summary>
        /// Helper to set up a single state's samplers for transition preview (doesn't clear buffers).
        /// </summary>
        private static void SetupStateForTransition(
            ref StateMachineBlob smBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ushort stateIndex,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float speed,
            bool loop)
        {
            ref var stateBlob = ref smBlob.States[stateIndex];
            
            switch (stateBlob.Type)
            {
                case StateType.Single:
                    SetupSingleClipState(ref smBlob, clips, clipEvents, stateIndex, ref animationStates, ref samplers, speed, loop);
                    break;
                    
                case StateType.LinearBlend:
                    SetupLinearBlendState(ref smBlob, clips, clipEvents, stateIndex, ref animationStates, ref samplers, speed, loop);
                    break;
                    
                case StateType.Directional2DBlend:
                    SetupDirectional2DState(ref smBlob, clips, clipEvents, stateIndex, ref animationStates, ref samplers, speed, loop);
                    break;
            }
        }
        
        private static void SetupSingleClipState(
            ref StateMachineBlob smBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ushort stateIndex,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float speed,
            bool loop)
        {
            ref var singleClipBlob = ref smBlob.SingleClipStates[smBlob.States[stateIndex].StateIndex];
            
            var sampler = new ClipSampler
            {
                ClipIndex = singleClipBlob.ClipIndex,
                Clips = clips,
                ClipEventsBlob = clipEvents,
                PreviousTime = 0,
                Time = 0,
                Weight = 1f
            };
            
            AnimationState.New(ref animationStates, ref samplers, sampler, speed, loop);
        }
        
        private static void SetupLinearBlendState(
            ref StateMachineBlob smBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ushort stateIndex,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float speed,
            bool loop)
        {
            ref var linearBlob = ref smBlob.LinearBlendStates[smBlob.States[stateIndex].StateIndex];
            int clipCount = linearBlob.SortedClipIndexes.Length;
            
            var newSamplers = new NativeArray<ClipSampler>(clipCount, Allocator.Temp);
            for (int i = 0; i < clipCount; i++)
            {
                newSamplers[i] = new ClipSampler
                {
                    ClipIndex = (ushort)linearBlob.SortedClipIndexes[i],
                    Clips = clips,
                    ClipEventsBlob = clipEvents,
                    PreviousTime = 0,
                    Time = 0,
                    Weight = 0
                };
            }
            
            AnimationState.New(ref animationStates, ref samplers, newSamplers, speed, loop);
            newSamplers.Dispose();
        }
        
        private static void SetupDirectional2DState(
            ref StateMachineBlob smBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ushort stateIndex,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float speed,
            bool loop)
        {
            ref var blend2DBlob = ref smBlob.Directional2DBlendStates[smBlob.States[stateIndex].StateIndex];
            int clipCount = blend2DBlob.ClipIndexes.Length;
            
            var newSamplers = new NativeArray<ClipSampler>(clipCount, Allocator.Temp);
            for (int i = 0; i < clipCount; i++)
            {
                newSamplers[i] = new ClipSampler
                {
                    ClipIndex = (ushort)blend2DBlob.ClipIndexes[i],
                    Clips = clips,
                    ClipEventsBlob = clipEvents,
                    PreviousTime = 0,
                    Time = 0,
                    Weight = 0
                };
            }
            
            AnimationState.New(ref animationStates, ref samplers, newSamplers, speed, loop);
            newSamplers.Dispose();
        }
    }
}
