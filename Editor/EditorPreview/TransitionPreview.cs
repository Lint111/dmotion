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
        
        // Track clip playables for time synchronization
        private AnimationClipPlayable[] fromClipPlayables;
        private AnimationClipPlayable[] toClipPlayables;
        private AnimationClipPlayable singleFromClipPlayable;
        private AnimationClipPlayable singleToClipPlayable;
        
        // Cached arrays to avoid per-frame allocations
        private float[] cachedFromWeights;
        private float[] cachedToWeights;
        private float[] cachedDistances;
        private List<AnimationClip> cachedClips;
        
        // Track clip playables for time synchronization
        private AnimationClipPlayable[] fromClipPlayables;
        private AnimationClipPlayable[] toClipPlayables;
        private AnimationClipPlayable singleFromClipPlayable;
        private AnimationClipPlayable singleToClipPlayable;
        
        // Cached arrays to avoid per-frame allocations
        private float[] cachedFromWeights;
        private float[] cachedToWeights;
        private float[] cachedDistances;
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
                // Sync clip times when normalized time changes, BEFORE graph sampling
                SyncClipTimes();
            }
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
            fromPlayable = BuildStatePlayable(graph, fromState, out fromMixer, out singleFromClipPlayable, out fromClipPlayables);
            fromPlayable = BuildStatePlayable(graph, fromState, out fromMixer, out singleFromClipPlayable, out fromClipPlayables);
            if (fromPlayable.IsValid())
            {
                graph.Connect(fromPlayable, 0, transitionMixer, 0);
            }
            
            // Build "to" state playable
            toPlayable = BuildStatePlayable(graph, toState, out toMixer, out singleToClipPlayable, out toClipPlayables);
            toPlayable = BuildStatePlayable(graph, toState, out toMixer, out singleToClipPlayable, out toClipPlayables);
            if (toPlayable.IsValid())
            {
                graph.Connect(toPlayable, 0, transitionMixer, 1);
            }
            
            // Initialize weights and sync times
            // Initialize weights and sync times
            UpdateTransitionWeights();
            UpdateFromBlendWeights();
            UpdateToBlendWeights();
            SyncClipTimes();
            SyncClipTimes();
            
            return graph;
        }
        
        /// <summary>
        /// Builds a playable for a state. Returns the root playable and optionally a mixer for blend states.
        /// </summary>
        private Playable BuildStatePlayable(PlayableGraph graph, AnimationStateAsset state, out AnimationMixerPlayable mixer,
            out AnimationClipPlayable singleClipPlayable, out AnimationClipPlayable[] blendClipPlayables)
        private Playable BuildStatePlayable(PlayableGraph graph, AnimationStateAsset state, out AnimationMixerPlayable mixer,
            out AnimationClipPlayable singleClipPlayable, out AnimationClipPlayable[] blendClipPlayables)
        {
            mixer = default;
            singleClipPlayable = default;
            blendClipPlayables = null;
            singleClipPlayable = default;
            blendClipPlayables = null;
            
            if (state == null)
            {
                // Return an empty playable for null states (e.g., Any State as "from")
                return Playable.Null;
            }
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    return BuildSingleClipPlayable(graph, singleClip, out singleClipPlayable);
                    return BuildSingleClipPlayable(graph, singleClip, out singleClipPlayable);
                    
                case LinearBlendStateAsset linearBlend:
                    return BuildBlendPlayable(graph, GetBlendClipData(linearBlend), out mixer, out blendClipPlayables);
                    return BuildBlendPlayable(graph, GetBlendClipData(linearBlend), out mixer, out blendClipPlayables);
                    
                case Directional2DBlendStateAsset blend2D:
                    return BuildBlendPlayable(graph, GetBlendClipData(blend2D), out mixer, out blendClipPlayables);
                    return BuildBlendPlayable(graph, GetBlendClipData(blend2D), out mixer, out blendClipPlayables);
                    
                default:
                    Debug.LogWarning($"[TransitionPreview] Unsupported state type: {state.GetType().Name}");
                    return Playable.Null;
            }
        }
        
        private Playable BuildSingleClipPlayable(PlayableGraph graph, SingleClipStateAsset state, out AnimationClipPlayable clipPlayable)
        private Playable BuildSingleClipPlayable(PlayableGraph graph, SingleClipStateAsset state, out AnimationClipPlayable clipPlayable)
        {
            clipPlayable = default;
            clipPlayable = default;
            var clip = state.Clip?.Clip;
            if (clip == null) return Playable.Null;
            
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            return clipPlayable;
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            return clipPlayable;
        }
        
        private Playable BuildBlendPlayable(PlayableGraph graph, BlendedClipPreview.BlendClipData[] clipData, 
            out AnimationMixerPlayable mixer, out AnimationClipPlayable[] clipPlayables)
        private Playable BuildBlendPlayable(PlayableGraph graph, BlendedClipPreview.BlendClipData[] clipData, 
            out AnimationMixerPlayable mixer, out AnimationClipPlayable[] clipPlayables)
        {
            mixer = default;
            clipPlayables = null;
            clipPlayables = null;
            
            if (clipData == null || clipData.Length == 0)
            {
                return Playable.Null;
            }
            
            mixer = AnimationMixerPlayable.Create(graph, clipData.Length);
            clipPlayables = new AnimationClipPlayable[clipData.Length];
            clipPlayables = new AnimationClipPlayable[clipData.Length];
            
            for (int i = 0; i < clipData.Length; i++)
            {
                if (clipData[i].Clip != null)
                {
                    var clipPlayable = AnimationClipPlayable.Create(graph, clipData[i].Clip);
                    // Don't set speed - we handle time synchronization manually
                    clipPlayables[i] = clipPlayable;
                    // Don't set speed - we handle time synchronization manually
                    clipPlayables[i] = clipPlayable;
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
            
            // Ensure cached array is correct size
            EnsureArraySize(ref cachedFromWeights, fromClipData.Length);
            CalculateBlendWeights(fromClipData, fromBlendPosition, IsState2D(fromState), cachedFromWeights);
            ApplyWeightsToMixer(fromMixer, cachedFromWeights, fromClipData.Length);
            // Ensure cached array is correct size
            EnsureArraySize(ref cachedFromWeights, fromClipData.Length);
            CalculateBlendWeights(fromClipData, fromBlendPosition, IsState2D(fromState), cachedFromWeights);
            ApplyWeightsToMixer(fromMixer, cachedFromWeights, fromClipData.Length);
        }
        
        private void UpdateToBlendWeights()
        {
            if (!toMixer.IsValid() || toClipData == null) return;
            
            // Ensure cached array is correct size
            EnsureArraySize(ref cachedToWeights, toClipData.Length);
            CalculateBlendWeights(toClipData, toBlendPosition, IsState2D(toState), cachedToWeights);
            ApplyWeightsToMixer(toMixer, cachedToWeights, toClipData.Length);
            // Ensure cached array is correct size
            EnsureArraySize(ref cachedToWeights, toClipData.Length);
            CalculateBlendWeights(toClipData, toBlendPosition, IsState2D(toState), cachedToWeights);
            ApplyWeightsToMixer(toMixer, cachedToWeights, toClipData.Length);
        }
        
        private static void EnsureArraySize(ref float[] array, int requiredSize)
        {
            if (array == null || array.Length < requiredSize)
                array = new float[requiredSize];
        }
        
        private static void ApplyWeightsToMixer(AnimationMixerPlayable mixer, float[] weights, int count)
        private static void EnsureArraySize(ref float[] array, int requiredSize)
        {
            if (array == null || array.Length < requiredSize)
                array = new float[requiredSize];
        }
        
        private static void ApplyWeightsToMixer(AnimationMixerPlayable mixer, float[] weights, int count)
        {
            if (!mixer.IsValid() || weights == null) return;
            
            int inputCount = mixer.GetInputCount();
            for (int i = 0; i < count && i < inputCount; i++)
            int inputCount = mixer.GetInputCount();
            for (int i = 0; i < count && i < inputCount; i++)
            {
                mixer.SetInputWeight(i, weights[i]);
            }
        }
        
        /// <summary>
        /// Calculates blend weights for a set of clips at a given blend position.
        /// Fills the provided weights array in-place to avoid allocation.
        /// Fills the provided weights array in-place to avoid allocation.
        /// </summary>
        private void CalculateBlendWeights(BlendedClipPreview.BlendClipData[] clipData, float2 blendPosition, bool is2D, float[] weights)
        private void CalculateBlendWeights(BlendedClipPreview.BlendClipData[] clipData, float2 blendPosition, bool is2D, float[] weights)
        {
            if (clipData == null || clipData.Length == 0) return;
            if (clipData == null || clipData.Length == 0) return;
            
            // Clear weights
            for (int i = 0; i < clipData.Length; i++)
                weights[i] = 0f;
            // Clear weights
            for (int i = 0; i < clipData.Length; i++)
                weights[i] = 0f;
            
            if (clipData.Length == 1)
            {
                weights[0] = 1f;
                return;
                return;
            }
            
            if (is2D)
            {
                Calculate2DWeights(clipData, blendPosition, weights);
            }
            else
            {
                Calculate1DWeights(clipData, blendPosition.x, weights);
            }
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
            // Inverse distance weighting - use cached distances array
            EnsureArraySize(ref cachedDistances, clipData.Length);
            
            // Inverse distance weighting - use cached distances array
            EnsureArraySize(ref cachedDistances, clipData.Length);
            
            float totalWeight = 0;
            
            for (int i = 0; i < clipData.Length; i++)
            {
                float distance = math.distance(blendPosition, clipData[i].Position);
                
                if (distance < 0.0001f)
                {
                    // Exactly on this clip position
                    for (int j = 0; j < clipData.Length; j++)
                    for (int j = 0; j < clipData.Length; j++)
                    {
                        weights[j] = (j == i) ? 1 : 0;
                    }
                    return;
                }
                
                cachedDistances[i] = distance;
                cachedDistances[i] = distance;
            }
            
            const float power = 2f;
            for (int i = 0; i < clipData.Length; i++)
            {
                float weight = 1f / math.pow(cachedDistances[i], power);
                float weight = 1f / math.pow(cachedDistances[i], power);
                weights[i] = weight;
                totalWeight += weight;
            }
            
            // Normalize
            if (totalWeight > 0.0001f)
            {
                for (int i = 0; i < clipData.Length; i++)
                for (int i = 0; i < clipData.Length; i++)
                {
                    weights[i] /= totalWeight;
                }
            }
        }
        
        #endregion
        
        #region Time Synchronization
        
        /// <summary>
        /// Synchronizes all clip times based on normalized sample time.
        /// Each clip is set to its own time: normalizedTime * clipLength
        /// This ensures all clips are at the same normalized position regardless of their individual lengths.
        /// </summary>
        private void SyncClipTimes()
        {
            // Sync "from" state clips
            SyncStateClipTimes(fromState, singleFromClipPlayable, fromClipPlayables, fromClipData);
            
            // Sync "to" state clips
            SyncStateClipTimes(toState, singleToClipPlayable, toClipPlayables, toClipData);
        }
        
        private void SyncStateClipTimes(AnimationStateAsset state, AnimationClipPlayable singleClipPlayable, 
            AnimationClipPlayable[] blendClipPlayables, BlendedClipPreview.BlendClipData[] clipData)
        {
            if (state == null) return;
            
            // Handle single clip state
            if (state is SingleClipStateAsset singleClipState && singleClipPlayable.IsValid())
            {
                var clip = singleClipState.Clip?.Clip;
                if (clip != null)
                {
                    SetClipTime(singleClipPlayable, clip, normalizedSampleTime);
                }
                return;
            }
            
            // Handle blend state
            if (blendClipPlayables == null || clipData == null) return;
            
            for (int i = 0; i < blendClipPlayables.Length && i < clipData.Length; i++)
            {
                if (!blendClipPlayables[i].IsValid()) continue;
                
                var clip = clipData[i].Clip;
                if (clip == null) continue;
                
                SetClipTime(blendClipPlayables[i], clip, normalizedSampleTime);
            }
        }
        
        private static void SetClipTime(AnimationClipPlayable clipPlayable, AnimationClip clip, float normalizedTime)
        {
            float clipLength = clip.length;
            if (clipLength <= 0) return;
            
            // Each clip's time = normalized position in its own timeline
            // All clips are synchronized by normalized time (0 = start, 1 = end)
            float clipTime = normalizedTime * clipLength;
            
            // Handle looping - wrap time within clip length
            if (clip.isLooping)
            {
                clipTime = clipTime % clipLength;
            }
            else
            {
                // Non-looping: clamp to clip duration
                clipTime = Mathf.Min(clipTime, clipLength);
            }
            
            clipPlayable.SetTime(clipTime);
        }
        
        #endregion
        
        #region Time Synchronization
        
        /// <summary>
        /// Synchronizes all clip times based on normalized sample time.
        /// Each clip is set to its own time: normalizedTime * clipLength
        /// This ensures all clips are at the same normalized position regardless of their individual lengths.
        /// </summary>
        private void SyncClipTimes()
        {
            // Sync "from" state clips
            SyncStateClipTimes(fromState, singleFromClipPlayable, fromClipPlayables, fromClipData);
            
            // Sync "to" state clips
            SyncStateClipTimes(toState, singleToClipPlayable, toClipPlayables, toClipData);
        }
        
        private void SyncStateClipTimes(AnimationStateAsset state, AnimationClipPlayable singleClipPlayable, 
            AnimationClipPlayable[] blendClipPlayables, BlendedClipPreview.BlendClipData[] clipData)
        {
            if (state == null) return;
            
            // Handle single clip state
            if (state is SingleClipStateAsset singleClipState && singleClipPlayable.IsValid())
            {
                var clip = singleClipState.Clip?.Clip;
                if (clip != null)
                {
                    SetClipTime(singleClipPlayable, clip, normalizedSampleTime);
                }
                return;
            }
            
            // Handle blend state
            if (blendClipPlayables == null || clipData == null) return;
            
            for (int i = 0; i < blendClipPlayables.Length && i < clipData.Length; i++)
            {
                if (!blendClipPlayables[i].IsValid()) continue;
                
                var clip = clipData[i].Clip;
                if (clip == null) continue;
                
                SetClipTime(blendClipPlayables[i], clip, normalizedSampleTime);
            }
        }
        
        private static void SetClipTime(AnimationClipPlayable clipPlayable, AnimationClip clip, float normalizedTime)
        {
            float clipLength = clip.length;
            if (clipLength <= 0) return;
            
            // Each clip's time = normalized position in its own timeline
            // All clips are synchronized by normalized time (0 = start, 1 = end)
            float clipTime = normalizedTime * clipLength;
            
            // Handle looping - wrap time within clip length
            if (clip.isLooping)
            {
                clipTime = clipTime % clipLength;
            }
            else
            {
                // Non-looping: clamp to clip duration
                clipTime = Mathf.Min(clipTime, clipLength);
            }
            
            clipPlayable.SetTime(clipTime);
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
