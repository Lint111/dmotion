using System;
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
    /// </summary>
    public class TransitionPreview : PlayableGraphPreview
    {
        #region Fields
        
        private readonly AnimationStateAsset fromState;
        private readonly AnimationStateAsset toState;
        private readonly float transitionDuration;
        
        private AnimationMixerPlayable transitionMixer;
        private Playable fromPlayable;
        private Playable toPlayable;
        
        private float transitionProgress;
        private float normalizedSampleTime;
        
        // For blend states, we need to track their blend positions
        private float2 fromBlendPosition;
        private float2 toBlendPosition;
        private AnimationMixerPlayable fromMixer;
        private AnimationMixerPlayable toMixer;
        private BlendedClipPreview.BlendClipData[] fromClipData;
        private BlendedClipPreview.BlendClipData[] toClipData;
        
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
                UpdateFromBlendWeights();
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
                UpdateToBlendWeights();
            }
        }
        
        protected override IEnumerable<AnimationClip> Clips
        {
            get
            {
                var clips = new List<AnimationClip>();
                clips.AddRange(GetClipsFromState(fromState));
                clips.AddRange(GetClipsFromState(toState));
                return clips.Where(c => c != null);
            }
        }
        
        public override float SampleTime
        {
            get
            {
                // Blend between from and to durations based on progress
                float fromDuration = GetStateDuration(fromState);
                float toDuration = GetStateDuration(toState);
                float blendedDuration = Mathf.Lerp(fromDuration, toDuration, transitionProgress);
                return NormalizedSampleTime * blendedDuration;
            }
        }
        
        public override float NormalizedSampleTime
        {
            get => normalizedSampleTime;
            set => normalizedSampleTime = Mathf.Clamp01(value);
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
            
            // Initialize blend data for blend states
            fromClipData = GetBlendClipData(fromState);
            toClipData = GetBlendClipData(toState);
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
            
            // Build "from" state playable
            fromPlayable = BuildStatePlayable(graph, fromState, out fromMixer);
            if (fromPlayable.IsValid())
            {
                graph.Connect(fromPlayable, 0, transitionMixer, 0);
            }
            
            // Build "to" state playable
            toPlayable = BuildStatePlayable(graph, toState, out toMixer);
            if (toPlayable.IsValid())
            {
                graph.Connect(toPlayable, 0, transitionMixer, 1);
            }
            
            // Initialize weights
            UpdateTransitionWeights();
            UpdateFromBlendWeights();
            UpdateToBlendWeights();
            
            return graph;
        }
        
        /// <summary>
        /// Builds a playable for a state. Returns the root playable and optionally a mixer for blend states.
        /// </summary>
        private Playable BuildStatePlayable(PlayableGraph graph, AnimationStateAsset state, out AnimationMixerPlayable mixer)
        {
            mixer = default;
            
            if (state == null)
            {
                // Return an empty playable for null states (e.g., Any State as "from")
                return Playable.Null;
            }
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    return BuildSingleClipPlayable(graph, singleClip);
                    
                case LinearBlendStateAsset linearBlend:
                    return BuildBlendPlayable(graph, GetBlendClipData(linearBlend), out mixer);
                    
                case Directional2DBlendStateAsset blend2D:
                    return BuildBlendPlayable(graph, GetBlendClipData(blend2D), out mixer);
                    
                default:
                    Debug.LogWarning($"[TransitionPreview] Unsupported state type: {state.GetType().Name}");
                    return Playable.Null;
            }
        }
        
        private Playable BuildSingleClipPlayable(PlayableGraph graph, SingleClipStateAsset state)
        {
            var clip = state.Clip?.Clip;
            if (clip == null) return Playable.Null;
            
            return AnimationClipPlayable.Create(graph, clip);
        }
        
        private Playable BuildBlendPlayable(PlayableGraph graph, BlendedClipPreview.BlendClipData[] clipData, out AnimationMixerPlayable mixer)
        {
            mixer = default;
            
            if (clipData == null || clipData.Length == 0)
            {
                return Playable.Null;
            }
            
            mixer = AnimationMixerPlayable.Create(graph, clipData.Length);
            
            for (int i = 0; i < clipData.Length; i++)
            {
                if (clipData[i].Clip != null)
                {
                    var clipPlayable = AnimationClipPlayable.Create(graph, clipData[i].Clip);
                    clipPlayable.SetSpeed(clipData[i].Speed);
                    graph.Connect(clipPlayable, 0, mixer, i);
                }
            }
            
            return mixer;
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
        
        private void UpdateFromBlendWeights()
        {
            if (!fromMixer.IsValid() || fromClipData == null) return;
            
            var weights = CalculateBlendWeights(fromClipData, fromBlendPosition, IsState2D(fromState));
            ApplyWeightsToMixer(fromMixer, weights);
        }
        
        private void UpdateToBlendWeights()
        {
            if (!toMixer.IsValid() || toClipData == null) return;
            
            var weights = CalculateBlendWeights(toClipData, toBlendPosition, IsState2D(toState));
            ApplyWeightsToMixer(toMixer, weights);
        }
        
        private void ApplyWeightsToMixer(AnimationMixerPlayable mixer, float[] weights)
        {
            if (!mixer.IsValid() || weights == null) return;
            
            for (int i = 0; i < weights.Length && i < mixer.GetInputCount(); i++)
            {
                mixer.SetInputWeight(i, weights[i]);
            }
        }
        
        /// <summary>
        /// Calculates blend weights for a set of clips at a given blend position.
        /// </summary>
        private float[] CalculateBlendWeights(BlendedClipPreview.BlendClipData[] clipData, float2 blendPosition, bool is2D)
        {
            if (clipData == null || clipData.Length == 0) return Array.Empty<float>();
            
            var weights = new float[clipData.Length];
            
            if (clipData.Length == 1)
            {
                weights[0] = 1f;
                return weights;
            }
            
            if (is2D)
            {
                Calculate2DWeights(clipData, blendPosition, weights);
            }
            else
            {
                Calculate1DWeights(clipData, blendPosition.x, weights);
            }
            
            return weights;
        }
        
        private void Calculate1DWeights(BlendedClipPreview.BlendClipData[] clipData, float blendValue, float[] weights)
        {
            // Find the two clips to blend between
            int lowerIndex = -1;
            int upperIndex = -1;
            
            for (int i = 0; i < clipData.Length; i++)
            {
                float threshold = clipData[i].Position.x;
                
                if (threshold <= blendValue)
                {
                    lowerIndex = i;
                }
                
                if (threshold >= blendValue && upperIndex == -1)
                {
                    upperIndex = i;
                }
            }
            
            // Handle edge cases
            if (lowerIndex == -1)
            {
                weights[0] = 1;
                return;
            }
            
            if (upperIndex == -1)
            {
                weights[clipData.Length - 1] = 1;
                return;
            }
            
            if (lowerIndex == upperIndex)
            {
                weights[lowerIndex] = 1;
                return;
            }
            
            // Linear interpolation
            float lowerThreshold = clipData[lowerIndex].Position.x;
            float upperThreshold = clipData[upperIndex].Position.x;
            float range = upperThreshold - lowerThreshold;
            
            if (range <= 0.0001f)
            {
                weights[lowerIndex] = 1;
                return;
            }
            
            float t = (blendValue - lowerThreshold) / range;
            weights[lowerIndex] = 1 - t;
            weights[upperIndex] = t;
        }
        
        private void Calculate2DWeights(BlendedClipPreview.BlendClipData[] clipData, float2 blendPosition, float[] weights)
        {
            // Inverse distance weighting
            float totalWeight = 0;
            float[] distances = new float[clipData.Length];
            
            for (int i = 0; i < clipData.Length; i++)
            {
                float distance = math.distance(blendPosition, clipData[i].Position);
                
                if (distance < 0.0001f)
                {
                    // Exactly on this clip position
                    for (int j = 0; j < weights.Length; j++)
                    {
                        weights[j] = (j == i) ? 1 : 0;
                    }
                    return;
                }
                
                distances[i] = distance;
            }
            
            const float power = 2f;
            for (int i = 0; i < clipData.Length; i++)
            {
                float weight = 1f / math.pow(distances[i], power);
                weights[i] = weight;
                totalWeight += weight;
            }
            
            // Normalize
            if (totalWeight > 0.0001f)
            {
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] /= totalWeight;
                }
            }
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
        
        private static float GetStateDuration(AnimationStateAsset state)
        {
            if (state == null) return 1f;
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    return singleClip.Clip?.Clip?.length ?? 1f;
                    
                case LinearBlendStateAsset linearBlend:
                    if (linearBlend.BlendClips == null || linearBlend.BlendClips.Length == 0)
                        return 1f;
                    // Return max duration
                    float maxDuration = 0f;
                    foreach (var bc in linearBlend.BlendClips)
                    {
                        if (bc.Clip?.Clip != null)
                        {
                            float duration = bc.Clip.Clip.length / (bc.Speed > 0 ? bc.Speed : 1f);
                            maxDuration = Mathf.Max(maxDuration, duration);
                        }
                    }
                    return maxDuration > 0 ? maxDuration : 1f;
                    
                case Directional2DBlendStateAsset blend2D:
                    if (blend2D.BlendClips == null || blend2D.BlendClips.Length == 0)
                        return 1f;
                    float max2D = 0f;
                    foreach (var bc in blend2D.BlendClips)
                    {
                        if (bc.Clip?.Clip != null)
                        {
                            float duration = bc.Clip.Clip.length / (bc.Speed > 0 ? bc.Speed : 1f);
                            max2D = Mathf.Max(max2D, duration);
                        }
                    }
                    return max2D > 0 ? max2D : 1f;
                    
                default:
                    return 1f;
            }
        }
        
        private static BlendedClipPreview.BlendClipData[] GetBlendClipData(AnimationStateAsset state)
        {
            switch (state)
            {
                case LinearBlendStateAsset linearBlend:
                    return GetBlendClipData(linearBlend);
                case Directional2DBlendStateAsset blend2D:
                    return GetBlendClipData(blend2D);
                default:
                    return null;
            }
        }
        
        private static BlendedClipPreview.BlendClipData[] GetBlendClipData(LinearBlendStateAsset state)
        {
            if (state?.BlendClips == null) return Array.Empty<BlendedClipPreview.BlendClipData>();
            
            return state.BlendClips
                .Where(c => c.Clip?.Clip != null)
                .Select(c => new BlendedClipPreview.BlendClipData
                {
                    Clip = c.Clip.Clip,
                    Position = new float2(c.Threshold, 0),
                    Speed = c.Speed > 0 ? c.Speed : 1f
                })
                .OrderBy(c => c.Position.x)
                .ToArray();
        }
        
        private static BlendedClipPreview.BlendClipData[] GetBlendClipData(Directional2DBlendStateAsset state)
        {
            if (state?.BlendClips == null) return Array.Empty<BlendedClipPreview.BlendClipData>();
            
            return state.BlendClips
                .Where(c => c.Clip?.Clip != null)
                .Select(c => new BlendedClipPreview.BlendClipData
                {
                    Clip = c.Clip.Clip,
                    Position = c.Position,
                    Speed = c.Speed > 0 ? c.Speed : 1f
                })
                .ToArray();
        }
        
        private static bool IsState2D(AnimationStateAsset state)
        {
            return state is Directional2DBlendStateAsset;
        }
        
        #endregion
    }
}
