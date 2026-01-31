using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview for transitions between two states.
    /// Builds a PlayableGraph that blends between the "from" state pose and "to" state pose
    /// based on the transition progress (0 = fully "from", 1 = fully "to").
    /// 
    /// Uses StatePlayableBuilder for shared playable construction logic.
    /// </summary>
    public class TransitionPreview : PlayableGraphPreview
    {
        #region Fields
        
        private readonly AnimationStateAsset fromState;
        private readonly AnimationStateAsset toState;
        private readonly float transitionDuration;
        
        // Main transition mixer (2 inputs: from and to)
        private AnimationMixerPlayable transitionMixer;
        
        // State playable results (built using shared StatePlayableBuilder)
        private StatePlayableResult fromResult;
        private StatePlayableResult toResult;
        
        // Transition and blend state
        private float transitionProgress;
        private float normalizedSampleTime;
        private float2 fromBlendPosition;
        private float2 toBlendPosition;
        
        // Cached clip list for model discovery
        private List<AnimationClip> cachedClips;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The transition progress from 0 (fully "from" state) to 1 (fully "to" state).
        /// </summary>
        public float TransitionProgress
        {
            get => transitionProgress;
            set
            {
                transitionProgress = Mathf.Clamp01(value);
                UpdateTransitionWeights();
            }
        }
        
        /// <summary>
        /// The blend position for the "from" state (if it's a blend state).
        /// </summary>
        public float2 FromBlendPosition
        {
            get => fromBlendPosition;
            set
            {
                fromBlendPosition = value;
                StatePlayableBuilder.UpdateBlendWeights(ref fromResult, value);
            }
        }
        
        /// <summary>
        /// The blend position for the "to" state (if it's a blend state).
        /// </summary>
        public float2 ToBlendPosition
        {
            get => toBlendPosition;
            set
            {
                toBlendPosition = value;
                StatePlayableBuilder.UpdateBlendWeights(ref toResult, value);
            }
        }
        
        protected override IEnumerable<AnimationClip> Clips
        {
            get
            {
                // Use cached list to avoid allocation
                if (cachedClips == null)
                {
                    cachedClips = new List<AnimationClip>();
                    foreach (var clip in GetClipsFromState(fromState))
                        if (clip != null) cachedClips.Add(clip);
                    foreach (var clip in GetClipsFromState(toState))
                        if (clip != null) cachedClips.Add(clip);
                }
                return cachedClips;
            }
        }
        
        public override float SampleTime
        {
            get
            {
                // Return 0 - we manually set each clip's time in NormalizedSampleTime setter
                // DO NOT call SyncClipTimes here - modifying playables during graph sampling corrupts state
                return 0;
            }
        }
        
        public override float NormalizedSampleTime
        {
            get => normalizedSampleTime;
            set
            {
                normalizedSampleTime = Mathf.Clamp01(value);
                // Legacy: sync both clips to same time (for backwards compatibility)
                // Prefer using SetStateNormalizedTimes for proper per-state timing
                SyncClipTimesInternal(normalizedSampleTime, normalizedSampleTime);
            }
        }
        
        /// <summary>
        /// Normalized time for the "from" state's clip (0-1).
        /// </summary>
        public float FromStateNormalizedTime { get; private set; }
        
        /// <summary>
        /// Normalized time for the "to" state's clip (0-1).
        /// </summary>
        public float ToStateNormalizedTime { get; private set; }
        
        /// <summary>
        /// Sets the normalized sample times for both states independently.
        /// This allows proper time synchronization where each state advances at its own rate.
        /// </summary>
        public void SetStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            FromStateNormalizedTime = Mathf.Clamp01(fromNormalized);
            ToStateNormalizedTime = Mathf.Clamp01(toNormalized);
            normalizedSampleTime = fromNormalized; // Keep legacy field in sync
            SyncClipTimesInternal(FromStateNormalizedTime, ToStateNormalizedTime);
        }
        
        /// <summary>
        /// The "from" state of the transition.
        /// </summary>
        public AnimationStateAsset FromState => fromState;
        
        /// <summary>
        /// The "to" state of the transition.
        /// </summary>
        public AnimationStateAsset ToState => toState;
        
        #endregion
        
        #region Constructor
        
        /// <summary>
        /// Creates a transition preview between two states.
        /// </summary>
        /// <param name="fromState">The state transitioning from (can be null for Any State transitions).</param>
        /// <param name="toState">The state transitioning to.</param>
        /// <param name="transitionDuration">The duration of the transition in seconds.</param>
        public TransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration)
        {
            this.fromState = fromState;
            this.toState = toState;
            this.transitionDuration = transitionDuration;
            this.transitionProgress = 0f;
            this.normalizedSampleTime = 0f;
        }
        
        #endregion
        
        #region PlayableGraph
        
        protected override PlayableGraph BuildGraph()
        {
            var graph = PlayableGraph.Create("TransitionPreview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            
            var playableOutput = AnimationPlayableOutput.Create(graph, "Animation", animator);
            
            // Create the main transition mixer (2 inputs: from and to)
            transitionMixer = AnimationMixerPlayable.Create(graph, 2);
            playableOutput.SetSourcePlayable(transitionMixer);
            
            // Build "from" state playable using shared builder
            fromResult = StatePlayableBuilder.BuildForState(graph, fromState);
            if (fromResult.IsValid)
            {
                graph.Connect(fromResult.RootPlayable, 0, transitionMixer, 0);
            }
            
            // Build "to" state playable using shared builder
            toResult = StatePlayableBuilder.BuildForState(graph, toState);
            if (toResult.IsValid)
            {
                graph.Connect(toResult.RootPlayable, 0, transitionMixer, 1);
            }
            
            // Initialize weights and sync times
            UpdateTransitionWeights();
            StatePlayableBuilder.UpdateBlendWeights(ref fromResult, fromBlendPosition);
            StatePlayableBuilder.UpdateBlendWeights(ref toResult, toBlendPosition);
            SyncClipTimes();
            
            return graph;
        }
        
        #endregion
        
        #region Weight Updates
        
        private void UpdateTransitionWeights()
        {
            if (!transitionMixer.IsValid()) return;
            
            // Simple crossfade: from weight decreases, to weight increases
            float fromWeight = 1f - transitionProgress;
            float toWeight = transitionProgress;
            
            transitionMixer.SetInputWeight(0, fromWeight);
            transitionMixer.SetInputWeight(1, toWeight);
        }
        
        #endregion
        
        #region Time Synchronization
        
        /// <summary>
        /// Synchronizes all clip times using stored per-state normalized times.
        /// Called during graph initialization.
        /// </summary>
        private void SyncClipTimes()
        {
            // Use stored per-state times (default to normalizedSampleTime for backwards compatibility)
            float fromTime = FromStateNormalizedTime > 0 ? FromStateNormalizedTime : normalizedSampleTime;
            float toTime = ToStateNormalizedTime > 0 ? ToStateNormalizedTime : normalizedSampleTime;
            SyncClipTimesInternal(fromTime, toTime);
        }
        
        /// <summary>
        /// Synchronizes clip times with separate normalized times for each state.
        /// This allows the from and to states to be at different positions in their respective clips.
        /// </summary>
        private void SyncClipTimesInternal(float fromNormalizedTime, float toNormalizedTime)
        {
            StatePlayableBuilder.SyncClipTimes(ref fromResult, fromNormalizedTime);
            StatePlayableBuilder.SyncClipTimes(ref toResult, toNormalizedTime);
        }
        
        #endregion
        
        #region Helpers
        
        private static IEnumerable<AnimationClip> GetClipsFromState(AnimationStateAsset state)
        {
            if (state == null) yield break;
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    if (singleClip.Clip?.Clip != null)
                        yield return singleClip.Clip.Clip;
                    break;
                    
                case LinearBlendStateAsset linearBlend:
                    if (linearBlend.BlendClips != null)
                    {
                        foreach (var bc in linearBlend.BlendClips)
                        {
                            if (bc.Clip?.Clip != null)
                                yield return bc.Clip.Clip;
                        }
                    }
                    break;
                    
                case Directional2DBlendStateAsset blend2D:
                    if (blend2D.BlendClips != null)
                    {
                        foreach (var bc in blend2D.BlendClips)
                        {
                            if (bc.Clip?.Clip != null)
                                yield return bc.Clip.Clip;
                        }
                    }
                    break;
            }
        }
        
        #endregion
    }
}
