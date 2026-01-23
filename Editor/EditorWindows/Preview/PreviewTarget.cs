using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Unified abstraction for preview targets.
    /// Eliminates the need for separate CreatePreviewForState and CreateTransitionPreview methods.
    /// </summary>
    public abstract class PreviewTarget
    {
        /// <summary>
        /// Display name for UI.
        /// </summary>
        public abstract string DisplayName { get; }
        
        /// <summary>
        /// Whether this target has valid data for preview.
        /// </summary>
        public abstract bool IsValid { get; }
        
        /// <summary>
        /// Whether this is a transition preview (vs single state).
        /// </summary>
        public abstract bool IsTransition { get; }
        
        /// <summary>
        /// The owning state machine asset.
        /// </summary>
        public abstract StateMachineAsset StateMachine { get; }
        
        /// <summary>
        /// Total duration of the preview in seconds.
        /// For transitions, this is the transition duration.
        /// For states, this is the state's effective duration.
        /// </summary>
        public abstract float Duration { get; }
        
        /// <summary>
        /// Gets all animation clips required for this preview.
        /// </summary>
        public abstract IEnumerable<AnimationClip> GetRequiredClips();
        
        /// <summary>
        /// Gets the primary state (for states) or "from" state (for transitions).
        /// </summary>
        public abstract AnimationStateAsset PrimaryState { get; }
        
        /// <summary>
        /// Gets the secondary state (null for states, "to" state for transitions).
        /// </summary>
        public virtual AnimationStateAsset SecondaryState => null;
        
        /// <summary>
        /// Whether this target supports blend position control.
        /// </summary>
        public virtual bool SupportsBlendControl => PrimaryState is LinearBlendStateAsset or Directional2DBlendStateAsset;
        
        /// <summary>
        /// Whether this is a 2D blend (vs 1D).
        /// </summary>
        public virtual bool Is2DBlend => PrimaryState is Directional2DBlendStateAsset;
    }
    
    /// <summary>
    /// Preview target for a single animation state.
    /// </summary>
    public class StatePreviewTarget : PreviewTarget
    {
        private readonly AnimationStateAsset state;
        private readonly StateMachineAsset stateMachine;
        private readonly float2 blendPosition;
        
        public StatePreviewTarget(AnimationStateAsset state, StateMachineAsset stateMachine, float2 blendPosition = default)
        {
            this.state = state;
            this.stateMachine = stateMachine;
            this.blendPosition = blendPosition;
        }
        
        public override string DisplayName => state?.name ?? "No State";
        
        public override bool IsValid => state != null && stateMachine != null;
        
        public override bool IsTransition => false;
        
        public override StateMachineAsset StateMachine => stateMachine;
        
        public override float Duration => state?.GetEffectiveDuration(blendPosition) ?? 1f;
        
        public override AnimationStateAsset PrimaryState => state;
        
        public AnimationStateAsset State => state;
        
        public override IEnumerable<AnimationClip> GetRequiredClips()
        {
            if (state == null) yield break;
            
            switch (state)
            {
                case SingleClipStateAsset single:
                    if (single.Clip?.Clip != null) yield return single.Clip.Clip;
                    break;
                    
                case LinearBlendStateAsset linear:
                    foreach (var entry in linear.BlendClips)
                        if (entry.Clip?.Clip != null) yield return entry.Clip.Clip;
                    break;
                    
                case Directional2DBlendStateAsset directional:
                    foreach (var entry in directional.BlendClips)
                        if (entry.Clip?.Clip != null) yield return entry.Clip.Clip;
                    break;
            }
        }
    }
    
    /// <summary>
    /// Preview target for an animation transition between two states.
    /// </summary>
    public class TransitionPreviewTarget : PreviewTarget
    {
        private readonly AnimationStateAsset fromState;
        private readonly AnimationStateAsset toState;
        private readonly StateMachineAsset stateMachine;
        private readonly float transitionDuration;
        private readonly float exitTime;
        private readonly float2 fromBlendPosition;
        private readonly float2 toBlendPosition;
        
        public TransitionPreviewTarget(
            AnimationStateAsset fromState, 
            AnimationStateAsset toState, 
            StateMachineAsset stateMachine,
            float transitionDuration,
            float exitTime = 1f,
            float2 fromBlendPosition = default,
            float2 toBlendPosition = default)
        {
            this.fromState = fromState;
            this.toState = toState;
            this.stateMachine = stateMachine;
            this.transitionDuration = transitionDuration;
            this.exitTime = exitTime;
            this.fromBlendPosition = fromBlendPosition;
            this.toBlendPosition = toBlendPosition;
        }
        
        public override string DisplayName
        {
            get
            {
                var from = fromState?.name ?? "Any";
                var to = toState?.name ?? "None";
                return $"{from} -> {to}";
            }
        }
        
        public override bool IsValid => toState != null && stateMachine != null;
        
        public override bool IsTransition => true;
        
        public override StateMachineAsset StateMachine => stateMachine;
        
        /// <summary>
        /// Duration of the transition itself (not including exit time).
        /// </summary>
        public override float Duration => transitionDuration;
        
        /// <summary>
        /// Total preview duration including exit time in the from-state.
        /// </summary>
        public float TotalDuration
        {
            get
            {
                var fromDuration = fromState?.GetEffectiveDuration(fromBlendPosition) ?? 1f;
                return (fromDuration * exitTime) + transitionDuration;
            }
        }
        
        public override AnimationStateAsset PrimaryState => fromState;
        
        public override AnimationStateAsset SecondaryState => toState;
        
        public AnimationStateAsset FromState => fromState;
        
        public AnimationStateAsset ToState => toState;
        
        public float TransitionDuration => transitionDuration;
        
        public float ExitTime => exitTime;
        
        public float2 FromBlendPosition => fromBlendPosition;
        
        public float2 ToBlendPosition => toBlendPosition;
        
        /// <summary>
        /// Whether the from-state supports blend control.
        /// </summary>
        public bool FromSupportsBlend => fromState is LinearBlendStateAsset or Directional2DBlendStateAsset;
        
        /// <summary>
        /// Whether the to-state supports blend control.
        /// </summary>
        public bool ToSupportsBlend => toState is LinearBlendStateAsset or Directional2DBlendStateAsset;
        
        public override bool SupportsBlendControl => FromSupportsBlend || ToSupportsBlend;
        
        public override IEnumerable<AnimationClip> GetRequiredClips()
        {
            var clips = new HashSet<AnimationClip>();
            
            // From state clips
            if (fromState != null)
            {
                foreach (var clip in GetClipsFromState(fromState))
                    clips.Add(clip);
            }
            
            // To state clips
            if (toState != null)
            {
                foreach (var clip in GetClipsFromState(toState))
                    clips.Add(clip);
            }
            
            return clips;
        }
        
        private static IEnumerable<AnimationClip> GetClipsFromState(AnimationStateAsset state)
        {
            switch (state)
            {
                case SingleClipStateAsset single:
                    if (single.Clip?.Clip != null) yield return single.Clip.Clip;
                    break;
                    
                case LinearBlendStateAsset linear:
                    foreach (var entry in linear.BlendClips)
                        if (entry.Clip?.Clip != null) yield return entry.Clip.Clip;
                    break;
                    
                case Directional2DBlendStateAsset directional:
                    foreach (var entry in directional.BlendClips)
                        if (entry.Clip?.Clip != null) yield return entry.Clip.Clip;
                    break;
            }
        }
        
        /// <summary>
        /// Creates a new target with updated blend positions.
        /// </summary>
        public TransitionPreviewTarget WithBlendPositions(float2 newFromBlend, float2 newToBlend)
        {
            return new TransitionPreviewTarget(
                fromState, toState, stateMachine, transitionDuration, exitTime, newFromBlend, newToBlend);
        }
    }
}
