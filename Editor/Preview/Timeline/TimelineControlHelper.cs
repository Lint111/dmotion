using System;
using DMotion;
using DMotion.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Editor-side helper for interacting with the AnimationTimelineControllerSystem.
    /// Provides a clean API for the preview backend to control animation timeline.
    /// </summary>
    internal class TimelineControlHelper : IDisposable
    {
        private Entity targetEntity = Entity.Null;
        private World targetWorld;
        private StateMachineAsset stateMachineAsset;
        private bool isInitialized;
        
        public bool IsInitialized => isInitialized;
        public Entity TargetEntity => targetEntity;
        public World TargetWorld => targetWorld;
        
        /// <summary>
        /// Initializes timeline control for an entity.
        /// Adds necessary components if missing.
        /// </summary>
        public bool Initialize(Entity entity, World world, StateMachineAsset stateMachine)
        {
            if (entity == Entity.Null || world == null || !world.IsCreated)
            {
                return false;
            }
            
            targetEntity = entity;
            targetWorld = world;
            stateMachineAsset = stateMachine;
            
            var em = world.EntityManager;
            
            // Add timeline control components if missing
            try
            {
                em.AddTimelineControl(entity);
                isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TimelineControlHelper] Failed to initialize: {e.Message}");
                isInitialized = false;
                return false;
            }
        }
        
        /// <summary>
        /// Sets up the timeline for single state preview.
        /// </summary>
        public void SetupStatePreview(AnimationStateAsset state, float2 blendPosition = default)
        {
            if (!ValidateState()) return;
            
            int stateIndex = GetStateIndex(state);
            if (stateIndex < 0)
            {
                Debug.LogWarning($"[TimelineControlHelper] State {state?.name} not found in state machine");
                return;
            }
            
            float duration = GetStateDuration(state, blendPosition);
            float speed = GetStateSpeed(state, blendPosition);
            
            var em = targetWorld.EntityManager;
            
            // Set up the AnimationState and ClipSampler buffers for this state
            // This ensures the samplers match the state being previewed
            if (!em.SetupAnimationStateForPreview(targetEntity, (ushort)stateIndex))
            {
                Debug.LogWarning($"[TimelineControlHelper] Failed to set up animation state for preview");
                return;
            }
            
            em.SetupStatePreview(targetEntity, (ushort)stateIndex, duration, speed, blendPosition);
            em.ActivateTimelineControl(targetEntity, startPaused: true); // Start paused - user must click play
        }
        
        /// <summary>
        /// Sets up the timeline for transition preview with ghost bars.
        /// Extracts timing info directly from the transition asset to match TransitionTimeline behavior.
        /// 
        /// Ghost bar rules (matching TransitionTimeline):
        /// - FROM ghost (LEFT of from-bar): appears when exitTime==0 OR requestedExitTime > fromStateDuration
        /// - TO ghost (RIGHT of to-bar): appears when transitionDuration > toStateDuration OR bars end together
        /// </summary>
        public void SetupTransitionPreview(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            StateOutTransition transition,
            short transitionIndex,
            TransitionSource curveSource,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            if (!ValidateState())
            {
                Debug.LogWarning("[TimelineControlHelper] ValidateState failed");
                return;
            }
            
            int fromIndex = fromState != null ? GetStateIndex(fromState) : -1;
            int toIndex = GetStateIndex(toState);
            
            if (toIndex < 0)
            {
                Debug.LogWarning($"[TimelineControlHelper] To-state {toState?.name} not found in state machine");
                return;
            }
            
            var em = targetWorld.EntityManager;
            
            // Set up animation states for BOTH FROM and TO states
            ushort fromIdx = fromIndex >= 0 ? (ushort)fromIndex : (ushort)toIndex;
            if (!em.SetupTransitionStatesForPreview(targetEntity, fromIdx, (ushort)toIndex))
            {
                Debug.LogWarning("[TimelineControlHelper] Failed to set up animation states for transition preview");
                return;
            }
            
            // Calculate durations from state assets
            var fromBlendVec = new Vector2(fromBlendPosition.x, fromBlendPosition.y);
            var toBlendVec = new Vector2(toBlendPosition.x, toBlendPosition.y);
            float fromDuration = fromState != null ? AnimationStateUtils.GetEffectiveDuration(fromState, fromBlendVec) : 0f;
            float toDuration = AnimationStateUtils.GetEffectiveDuration(toState, toBlendVec);
            
            // Calculate timing using shared utility
            var timing = TransitionTimingCalculator.Calculate(
                fromState, toState,
                fromBlendVec, toBlendVec,
                transition?.EndTime ?? 0f,
                transition?.TransitionDuration ?? 0.25f);
            
            // Get speeds for animation playback
            float fromSpeed = fromState != null ? GetStateSpeed(fromState, fromBlendPosition) : 1f;
            float toSpeed = GetStateSpeed(toState, toBlendPosition);
            
            // Build config struct for cleaner ECS call
            var config = new TransitionPreviewConfig
            {
                FromState = StateNode.Create(
                    fromIndex >= 0 ? (ushort)fromIndex : (ushort)0,
                    fromDuration,
                    fromSpeed,
                    fromBlendPosition),
                ToState = StateNode.Create(
                    (ushort)toIndex,
                    toDuration,
                    toSpeed,
                    toBlendPosition),
                Sections = new TransitionSectionDurations
                {
                    GhostFromDuration = timing.GhostFromDuration,
                    FromBarDuration = timing.FromBarDuration,
                    TransitionDuration = timing.TransitionDuration,
                    ToBarDuration = timing.ToBarDuration,
                    GhostToDuration = timing.GhostToDuration
                },
                Transition = TransitionInfo.Create(transitionIndex, curveSource)
            };
            
            em.SetupTransitionPreview(targetEntity, in config);
            em.ActivateTimelineControl(targetEntity, startPaused: true);
        }
        
        /// <summary>
        /// Plays the timeline.
        /// </summary>
        public void Play()
        {
            if (!ValidateState()) return;
            var em = targetWorld.EntityManager;
            em.SendTimelineCommand(targetEntity, AnimationTimelineCommand.Play());
        }
        
        /// <summary>
        /// Pauses the timeline.
        /// </summary>
        public void Pause()
        {
            if (!ValidateState()) return;
            var em = targetWorld.EntityManager;
            em.SendTimelineCommand(targetEntity, AnimationTimelineCommand.Pause());
        }
        
        /// <summary>
        /// Scrubs to a normalized time position (0-1).
        /// </summary>
        public void ScrubToTime(float normalizedTime)
        {
            if (!ValidateState()) return;
            var em = targetWorld.EntityManager;
            em.SendTimelineCommand(targetEntity, AnimationTimelineCommand.ScrubState(normalizedTime));
        }
        
        /// <summary>
        /// Scrubs transition progress (0 = from state, 1 = to state).
        /// </summary>
        public void ScrubTransition(float progress)
        {
            if (!ValidateState()) return;
            var em = targetWorld.EntityManager;
            em.SendTimelineCommand(targetEntity, AnimationTimelineCommand.ScrubTransition(progress));
        }
        
        /// <summary>
        /// Steps forward or backward by frames.
        /// </summary>
        public void StepFrames(int frames, float fps = 30f)
        {
            if (!ValidateState()) return;
            var em = targetWorld.EntityManager;
            em.SendTimelineCommand(targetEntity, AnimationTimelineCommand.Step(frames, fps));
        }
        
        /// <summary>
        /// Updates blend position for state preview.
        /// Rebuilds sections since blend position affects duration and speed.
        /// </summary>
        public void UpdateBlendPosition(AnimationStateAsset state, float2 blendPosition)
        {
            if (!ValidateState()) return;
            
            // Rebuild - state asset accessors handle duration/speed correctly for all state types
            SetupStatePreview(state, blendPosition);
        }
        
        /// <summary>
        /// Updates blend positions for transition preview.
        /// Always rebuilds sections since blend position affects duration and speed
        /// (handled correctly by state asset's GetEffectiveDuration/GetEffectiveSpeed).
        /// </summary>
        public void UpdateTransitionBlendPositions(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            StateOutTransition transition,
            short transitionIndex,
            TransitionSource curveSource,
            float2 fromBlendPosition,
            float2 toBlendPosition)
        {
            if (!ValidateState()) return;
            
            // Always rebuild - state asset accessors handle all state types correctly
            SetupTransitionPreview(fromState, toState, transition, transitionIndex, curveSource, fromBlendPosition, toBlendPosition);
        }
        
        /// <summary>
        /// Gets the current timeline position.
        /// </summary>
        public AnimationTimelinePosition GetPosition()
        {
            if (!ValidateState()) return default;
            
            var em = targetWorld.EntityManager;
            if (em.HasComponent<AnimationTimelinePosition>(targetEntity))
            {
                return em.GetComponentData<AnimationTimelinePosition>(targetEntity);
            }
            return default;
        }
        
        /// <summary>
        /// Gets the current section type based on current position.
        /// </summary>
        public TimelineSectionType GetCurrentSectionType()
        {
            if (!ValidateState()) return TimelineSectionType.State;
            
            var em = targetWorld.EntityManager;
            if (!em.HasComponent<AnimationTimelinePosition>(targetEntity)) return TimelineSectionType.State;
            if (!em.HasBuffer<TimelineSection>(targetEntity)) return TimelineSectionType.State;
            
            var position = em.GetComponentData<AnimationTimelinePosition>(targetEntity);
            var sections = em.GetBuffer<TimelineSection>(targetEntity);
            
            if (position.CurrentSectionIndex >= 0 && position.CurrentSectionIndex < sections.Length)
            {
                return sections[position.CurrentSectionIndex].Type;
            }
            
            return TimelineSectionType.State;
        }
        
        /// <summary>
        /// Gets the active render request type.
        /// </summary>
        public ActiveRenderRequest GetActiveRequest()
        {
            if (!ValidateState()) return ActiveRenderRequest.None;
            
            var em = targetWorld.EntityManager;
            if (em.HasComponent<ActiveRenderRequest>(targetEntity))
            {
                return em.GetComponentData<ActiveRenderRequest>(targetEntity);
            }
            return ActiveRenderRequest.None;
        }
        
        /// <summary>
        /// Gets the current state render request (if active).
        /// </summary>
        public AnimationStateRenderRequest GetStateRequest()
        {
            if (!ValidateState()) return AnimationStateRenderRequest.None;
            
            var em = targetWorld.EntityManager;
            if (em.HasComponent<AnimationStateRenderRequest>(targetEntity))
            {
                return em.GetComponentData<AnimationStateRenderRequest>(targetEntity);
            }
            return AnimationStateRenderRequest.None;
        }
        
        /// <summary>
        /// Gets the current transition render request (if active).
        /// </summary>
        public AnimationTransitionRenderRequest GetTransitionRequest()
        {
            if (!ValidateState()) return AnimationTransitionRenderRequest.None;
            
            var em = targetWorld.EntityManager;
            if (em.HasComponent<AnimationTransitionRenderRequest>(targetEntity))
            {
                return em.GetComponentData<AnimationTransitionRenderRequest>(targetEntity);
            }
            return AnimationTransitionRenderRequest.None;
        }
        
        /// <summary>
        /// Deactivates timeline control, returning to normal state machine behavior.
        /// </summary>
        public void Deactivate()
        {
            if (!ValidateState()) return;
            var em = targetWorld.EntityManager;
            em.DeactivateTimelineControl(targetEntity);
        }
        
        #region Private Helpers
        
        private bool ValidateState()
        {
            if (!isInitialized) return false;
            if (targetWorld == null || !targetWorld.IsCreated) return false;
            if (!targetWorld.EntityManager.Exists(targetEntity)) return false;
            return true;
        }
        
        private int GetStateIndex(AnimationStateAsset state)
        {
            if (state == null || stateMachineAsset == null) return -1;
            
            for (int i = 0; i < stateMachineAsset.States.Count; i++)
            {
                if (stateMachineAsset.States[i] == state)
                {
                    return i;
                }
            }
            return -1;
        }
        
        private static float GetStateDuration(AnimationStateAsset state, float2 blendPosition)
        {
            return AnimationStateUtils.GetEffectiveDuration(state, new Vector2(blendPosition.x, blendPosition.y));
        }
        
        private static float GetStateSpeed(AnimationStateAsset state, float2 blendPosition)
        {
            return AnimationStateUtils.GetEffectiveSpeed(state, new Vector2(blendPosition.x, blendPosition.y));
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            if (ValidateState())
            {
                Deactivate();
            }
            
            targetEntity = Entity.Null;
            targetWorld = null;
            stateMachineAsset = null;
            isInitialized = false;
        }
        
        #endregion
    }
}
