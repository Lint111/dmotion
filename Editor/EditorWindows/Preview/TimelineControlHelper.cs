using System;
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
            
            var em = targetWorld.EntityManager;
            
            // Set up the AnimationState and ClipSampler buffers for this state
            // This ensures the samplers match the state being previewed
            if (!em.SetupAnimationStateForPreview(targetEntity, (ushort)stateIndex))
            {
                Debug.LogWarning($"[TimelineControlHelper] Failed to set up animation state for preview");
                return;
            }
            
            em.SetupStatePreview(targetEntity, (ushort)stateIndex, duration, blendPosition);
            em.ActivateTimelineControl(targetEntity, startPaused: false); // Start playing immediately
            
            // Debug: verify setup
            var animStates = em.GetBuffer<AnimationState>(targetEntity);
            var samplers = em.GetBuffer<ClipSampler>(targetEntity);
            Debug.Log($"[TimelineControlHelper] Setup complete: StateIndex={stateIndex}, AnimStates={animStates.Length}, Samplers={samplers.Length}");
            if (samplers.Length > 0)
            {
                var s = samplers[0];
                Debug.Log($"[TimelineControlHelper] Sampler[0]: Id={s.Id}, Weight={s.Weight}, Time={s.Time}, ClipsValid={s.Clips.IsCreated}");
            }
        }
        
        /// <summary>
        /// Sets up the timeline for transition preview with ghost bars.
        /// Extracts timing info directly from the transition asset to match TransitionTimeline behavior.
        /// </summary>
        /// <param name="fromState">The state we're transitioning from (can be null for AnyState)</param>
        /// <param name="toState">The state we're transitioning to</param>
        /// <param name="transition">The transition definition (contains duration, exit time, curve)</param>
        /// <param name="transitionIndex">Index of transition for curve lookup</param>
        /// <param name="curveSource">Source of transition (State or AnyState)</param>
        /// <param name="fromBlendPosition">Blend position for from-state (for blend trees)</param>
        /// <param name="toBlendPosition">Blend position for to-state (for blend trees)</param>
        public void SetupTransitionPreview(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            StateOutTransition transition,
            short transitionIndex,
            TransitionSource curveSource,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            if (!ValidateState()) return;
            
            int fromIndex = fromState != null ? GetStateIndex(fromState) : -1;
            int toIndex = GetStateIndex(toState);
            
            if (toIndex < 0)
            {
                Debug.LogWarning($"[TimelineControlHelper] To-state {toState?.name} not found");
                return;
            }
            
            var em = targetWorld.EntityManager;
            
            // Set up animation states for BOTH FROM and TO states
            // Transition preview requires samplers for both states to blend between them
            ushort fromIdx = fromIndex >= 0 ? (ushort)fromIndex : (ushort)toIndex; // Fallback to TO if no FROM
            if (!em.SetupTransitionStatesForPreview(targetEntity, fromIdx, (ushort)toIndex))
            {
                Debug.LogWarning($"[TimelineControlHelper] Failed to set up animation states for transition preview");
                return;
            }
            
            // Debug: verify transition setup
            var animStates = em.GetBuffer<AnimationState>(targetEntity);
            var samplers = em.GetBuffer<ClipSampler>(targetEntity);
            Debug.Log($"[TimelineControlHelper] Transition setup: FROM={fromIdx}, TO={toIndex}, AnimStates={animStates.Length}, Samplers={samplers.Length}");
            for (int i = 0; i < samplers.Length; i++)
            {
                var s = samplers[i];
                Debug.Log($"[TimelineControlHelper] Sampler[{i}]: Id={s.Id}, ClipIndex={s.ClipIndex}, ClipsValid={s.Clips.IsCreated}");
            }
            
            // Extract timing from transition asset (matching TransitionTimeline logic)
            float transitionDuration = transition?.TransitionDuration ?? 0.25f;
            float exitTime = transition?.EndTime ?? 0f;
            bool hasExitTime = transition?.HasEndTime ?? false;
            
            // Calculate effective durations (matching TransitionTimeline.Configure logic)
            float fromStateDuration = fromState != null ? GetStateDuration(fromState, fromBlendPosition) : 0f;
            float toStateDuration = GetStateDuration(toState, toBlendPosition);
            
            // Clamp transition duration to to-state duration (can't blend longer than target state)
            float effectiveTransitionDuration = Mathf.Min(transitionDuration, toStateDuration);
            
            // Calculate ghost-from duration (time shown before transition starts)
            // Matches TransitionTimeline: if hasExitTime, show from start to exitTime
            // Otherwise show a small context before immediate transition
            float ghostFromDuration;
            if (hasExitTime && fromState != null && exitTime > 0)
            {
                // Exit time is when transition starts - show from-state up to that point
                // Clamp to from-state duration (can wrap/loop if exitTime > duration)
                ghostFromDuration = Mathf.Min(exitTime, fromStateDuration);
            }
            else if (fromState != null)
            {
                // No exit time or exitTime=0 - show small context (25% of from state)
                ghostFromDuration = fromStateDuration * 0.25f;
            }
            else
            {
                ghostFromDuration = 0f;
            }
            
            // Ghost-to shows continuation after transition completes (25% for context)
            float ghostToDuration = toStateDuration * 0.25f;
            
            em.SetupTransitionPreview(
                targetEntity,
                fromIndex >= 0 ? (ushort)fromIndex : (ushort)0,
                (ushort)toIndex,
                ghostFromDuration,
                effectiveTransitionDuration,
                ghostToDuration,
                transitionIndex,
                curveSource,
                fromBlendPosition,
                toBlendPosition);
            
            em.ActivateTimelineControl(targetEntity, startPaused: false); // Start playing immediately
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
        /// Directly updates the TimelineSection's blend position without rebuilding.
        /// </summary>
        public void UpdateBlendPosition(AnimationStateAsset state, float2 blendPosition)
        {
            if (!ValidateState()) return;
            
            var em = targetWorld.EntityManager;
            
            // Directly update the blend position in existing TimelineSection(s)
            if (em.HasBuffer<TimelineSection>(targetEntity))
            {
                var sections = em.GetBuffer<TimelineSection>(targetEntity);
                for (int i = 0; i < sections.Length; i++)
                {
                    var section = sections[i];
                    section.BlendPosition = blendPosition;
                    sections[i] = section;
                }
            }
        }
        
        /// <summary>
        /// Updates blend positions for transition preview.
        /// Directly updates the TimelineSection blend positions without rebuilding.
        /// </summary>
        public void UpdateTransitionBlendPositions(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            float transitionDuration,
            short transitionIndex,
            TransitionSource curveSource,
            float2 fromBlendPosition,
            float2 toBlendPosition)
        {
            if (!ValidateState()) return;
            
            var em = targetWorld.EntityManager;
            
            Debug.Log($"[TimelineControlHelper] UpdateTransitionBlendPositions: fromBlendPos={fromBlendPosition}, toBlendPos={toBlendPosition}");
            
            // Directly update blend positions in existing TimelineSection(s)
            if (em.HasBuffer<TimelineSection>(targetEntity))
            {
                var sections = em.GetBuffer<TimelineSection>(targetEntity);
                for (int i = 0; i < sections.Length; i++)
                {
                    var section = sections[i];
                    
                    switch (section.Type)
                    {
                        case TimelineSectionType.GhostFrom:
                        case TimelineSectionType.State:
                            section.BlendPosition = fromBlendPosition;
                            break;
                            
                        case TimelineSectionType.GhostTo:
                            section.BlendPosition = toBlendPosition;
                            break;
                            
                        case TimelineSectionType.Transition:
                            section.BlendPosition = fromBlendPosition;
                            section.ToBlendPosition = toBlendPosition;
                            Debug.Log($"[TimelineControlHelper] Updated Transition section[{i}]: BlendPos={section.BlendPosition}, ToBlendPos={section.ToBlendPosition}");
                            break;
                    }
                    
                    sections[i] = section;
                }
            }
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
        
        private float GetStateDuration(AnimationStateAsset state, float2 blendPosition)
        {
            if (state == null) return 1f;
            
            // Get duration based on state type
            if (state is SingleClipStateAsset singleClip)
            {
                return singleClip.Clip?.Clip?.length ?? 1f;
            }
            else if (state is LinearBlendStateAsset linearBlend)
            {
                // Get weighted duration based on blend position (similar logic to GetEffectiveSpeed)
                return GetLinearBlendDuration(linearBlend, blendPosition.x);
            }
            else if (state is Directional2DBlendStateAsset blend2D)
            {
                // For 2D blend, get average of contributing clips
                return GetDirectional2DBlendDuration(blend2D, blendPosition);
            }
            
            return 1f;
        }
        
        private static float GetLinearBlendDuration(LinearBlendStateAsset linearBlend, float blendValue)
        {
            if (linearBlend.BlendClips == null || linearBlend.BlendClips.Length == 0)
                return 1f;
            
            // Find the two clips we're blending between
            int lowerIndex = -1, upperIndex = -1;
            
            for (int i = 0; i < linearBlend.BlendClips.Length; i++)
            {
                float threshold = linearBlend.BlendClips[i].Threshold;
                if (threshold <= blendValue)
                    lowerIndex = i;
                if (threshold >= blendValue && upperIndex == -1)
                    upperIndex = i;
            }
            
            // Handle edge cases
            if (lowerIndex == -1) lowerIndex = 0;
            if (upperIndex == -1) upperIndex = linearBlend.BlendClips.Length - 1;
            
            float lowerDuration = linearBlend.BlendClips[lowerIndex].Clip?.Clip?.length ?? 1f;
            
            if (lowerIndex == upperIndex)
            {
                return lowerDuration;
            }
            
            float upperDuration = linearBlend.BlendClips[upperIndex].Clip?.Clip?.length ?? 1f;
            
            float lowerThreshold = linearBlend.BlendClips[lowerIndex].Threshold;
            float upperThreshold = linearBlend.BlendClips[upperIndex].Threshold;
            float range = upperThreshold - lowerThreshold;
            
            if (range > 0.0001f)
            {
                float t = (blendValue - lowerThreshold) / range;
                return Mathf.Lerp(lowerDuration, upperDuration, t);
            }
            
            return lowerDuration;
        }
        
        private static float GetDirectional2DBlendDuration(Directional2DBlendStateAsset blend2D, float2 blendPosition)
        {
            if (blend2D.BlendClips == null || blend2D.BlendClips.Length == 0)
                return 1f;
            
            // Simple approach: return duration of the nearest clip
            float minDistance = float.MaxValue;
            float nearestDuration = 1f;
            
            foreach (var clip in blend2D.BlendClips)
            {
                var clipPos = new float2(clip.Position.x, clip.Position.y);
                float distance = math.length(blendPosition - clipPos);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestDuration = clip.Clip?.Clip?.length ?? 1f;
                }
            }
            
            return nearestDuration;
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
