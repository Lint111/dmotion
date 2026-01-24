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
            Debug.Log($"[TimelineControlHelper] SetupTransitionPreview: from={fromState?.name}, to={toState?.name}");
            
            if (!ValidateState())
            {
                Debug.LogWarning("[TimelineControlHelper] ValidateState failed");
                return;
            }
            
            int fromIndex = fromState != null ? GetStateIndex(fromState) : -1;
            int toIndex = GetStateIndex(toState);
            
            Debug.Log($"[TimelineControlHelper] fromIndex={fromIndex}, toIndex={toIndex}");
            
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
                Debug.LogWarning($"[TimelineControlHelper] Failed to set up animation states for transition preview");
                return;
            }
            
            // Extract timing from transition asset
            float requestedTransitionDuration = transition?.TransitionDuration ?? 0.25f;
            float requestedExitTime = transition?.EndTime ?? 0f;
            bool hasExitTime = transition?.HasEndTime ?? false;
            
            // Calculate state durations
            float fromStateDuration = fromState != null ? GetStateDuration(fromState, fromBlendPosition) : 0f;
            float toStateDuration = GetStateDuration(toState, toBlendPosition);
            
            Debug.Log($"[TimelineControlHelper] Timing: reqTransDur={requestedTransitionDuration:F3}, reqExitTime={requestedExitTime:F3}, hasExitTime={hasExitTime}");
            Debug.Log($"[TimelineControlHelper] Durations: fromStateDur={fromStateDuration:F3}, toStateDur={toStateDuration:F3}");
            
            // Clamp values for logic (matching TransitionTimeline)
            float minExitTime = Mathf.Max(0f, fromStateDuration - toStateDuration);
            float exitTime = Mathf.Clamp(requestedExitTime, minExitTime, fromStateDuration);
            float transitionDuration = Mathf.Clamp(requestedTransitionDuration, 0.01f, toStateDuration);
            
            Debug.Log($"[TimelineControlHelper] Clamped: minExitTime={minExitTime:F3}, exitTime={exitTime:F3}, transitionDuration={transitionDuration:F3}");
            
            // Calculate FROM visual cycles (ghost bars LEFT of from-bar)
            int fromVisualCycles = 1;
            if (fromStateDuration > 0.001f)
            {
                if (requestedExitTime > fromStateDuration)
                {
                    fromVisualCycles = Mathf.CeilToInt(requestedExitTime / fromStateDuration);
                }
                else if (exitTime < 0.001f && minExitTime < 0.001f)
                {
                    fromVisualCycles = 2;
                }
            }
            fromVisualCycles = Mathf.Clamp(fromVisualCycles, 1, 4);
            
            // Calculate TO visual cycles (ghost bars RIGHT of to-bar)
            int toVisualCycles = 1;
            if (toStateDuration > 0.001f)
            {
                if (requestedTransitionDuration > toStateDuration)
                {
                    toVisualCycles = Mathf.CeilToInt(requestedTransitionDuration / toStateDuration);
                }
                else
                {
                    bool barsEndTogether = (exitTime + toStateDuration) <= (fromStateDuration + 0.001f);
                    if (barsEndTogether)
                    {
                        toVisualCycles = 2;
                    }
                }
            }
            toVisualCycles = Mathf.Clamp(toVisualCycles, 1, 4);
            
            // Calculate section durations
            float fromBarDuration = exitTime;
            float toBarDuration = Mathf.Max(0f, toStateDuration - transitionDuration);
            float ghostFromDuration = (fromVisualCycles > 1) ? (fromVisualCycles - 1) * fromStateDuration : 0f;
            float ghostToDuration = (toVisualCycles > 1) ? (toVisualCycles - 1) * toStateDuration : 0f;
            
            Debug.Log($"[TimelineControlHelper] Cycles: fromVisualCycles={fromVisualCycles}, toVisualCycles={toVisualCycles}");
            Debug.Log($"[TimelineControlHelper] Sections: ghostFrom={ghostFromDuration:F3}, fromBar={fromBarDuration:F3}, trans={transitionDuration:F3}, toBar={toBarDuration:F3}, ghostTo={ghostToDuration:F3}");
            
            em.SetupTransitionPreview(
                targetEntity,
                fromIndex >= 0 ? (ushort)fromIndex : (ushort)0,
                (ushort)toIndex,
                ghostFromDuration,
                transitionDuration,
                ghostToDuration,
                transitionIndex,
                curveSource,
                fromBlendPosition,
                toBlendPosition,
                fromBarDuration,
                toBarDuration);
            
            em.ActivateTimelineControl(targetEntity, startPaused: true);
            Debug.Log("[TimelineControlHelper] SetupTransitionPreview complete");
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
                        case TimelineSectionType.FromBar:
                            section.BlendPosition = fromBlendPosition;
                            break;
                            
                        case TimelineSectionType.GhostTo:
                        case TimelineSectionType.ToBar:
                            section.BlendPosition = toBlendPosition;
                            break;
                            
                        case TimelineSectionType.Transition:
                            section.BlendPosition = fromBlendPosition;
                            section.ToBlendPosition = toBlendPosition;
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
