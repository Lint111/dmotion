using System;
using System.Linq;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DMotion.Editor
{
    /// <summary>
    /// Data structure holding all playable information for a state.
    /// Used by preview systems to manage state playables without duplicating build logic.
    /// </summary>
    public struct StatePlayableResult
    {
        /// <summary>Root playable to connect to parent mixer.</summary>
        public Playable RootPlayable;
        
        /// <summary>Mixer playable for blend states (invalid for single clip).</summary>
        public AnimationMixerPlayable Mixer;
        
        /// <summary>Individual clip playables.</summary>
        public AnimationClipPlayable[] ClipPlayables;
        
        /// <summary>Duration of each clip in seconds.</summary>
        public float[] ClipDurations;
        
        /// <summary>Threshold/position X value for each clip (1D blend).</summary>
        public float[] ClipThresholds;
        
        /// <summary>Position for each clip (2D blend).</summary>
        public float2[] ClipPositions;
        
        /// <summary>Current weight of each clip.</summary>
        public float[] ClipWeights;
        
        /// <summary>Whether this is a 2D blend state.</summary>
        public bool Is2DBlend;
        
        /// <summary>The algorithm for 2D blending.</summary>
        public Blend2DAlgorithm Algorithm;
        
        /// <summary>Whether this result has valid playables.</summary>
        public bool IsValid => RootPlayable.IsValid();
        
        /// <summary>Number of clips in this state.</summary>
        public int ClipCount => ClipPlayables?.Length ?? 0;
    }
    
    /// <summary>
    /// Shared utility for building playable structures for animation states.
    /// Used by TransitionPreview and LayerCompositionPreview to avoid code duplication.
    /// </summary>
    public static class StatePlayableBuilder
    {
        #region Build Methods
        
        /// <summary>
        /// Builds a playable structure for any animation state type.
        /// </summary>
        /// <param name="graph">The playable graph to create playables in.</param>
        /// <param name="state">The animation state asset.</param>
        /// <returns>Result containing all playable data, or invalid result if state is null.</returns>
        public static StatePlayableResult BuildForState(PlayableGraph graph, AnimationStateAsset state)
        {
            if (state == null)
            {
                return new StatePlayableResult
                {
                    RootPlayable = Playable.Null,
                    ClipPlayables = Array.Empty<AnimationClipPlayable>(),
                    ClipDurations = Array.Empty<float>(),
                    ClipThresholds = Array.Empty<float>(),
                    ClipPositions = Array.Empty<float2>(),
                    ClipWeights = Array.Empty<float>()
                };
            }
            
            return state switch
            {
                SingleClipStateAsset singleClip => BuildSingleClip(graph, singleClip),
                LinearBlendStateAsset linearBlend => BuildLinearBlend(graph, linearBlend),
                Directional2DBlendStateAsset blend2D => Build2DBlend(graph, blend2D),
                _ => BuildEmpty(graph, $"Unsupported state type: {state.GetType().Name}")
            };
        }
        
        private static StatePlayableResult BuildSingleClip(PlayableGraph graph, SingleClipStateAsset state)
        {
            var clip = state.Clip?.Clip;
            
            if (clip == null)
            {
                return BuildEmpty(graph, "No clip assigned");
            }
            
            var clipPlayable = AnimationClipPlayable.Create(graph, clip);
            
            return new StatePlayableResult
            {
                RootPlayable = clipPlayable,
                Mixer = default,
                ClipPlayables = new[] { clipPlayable },
                ClipDurations = new[] { clip.length },
                ClipThresholds = new[] { 0f },
                ClipPositions = new[] { float2.zero },
                ClipWeights = new[] { 1f },
                Is2DBlend = false,
                Algorithm = Blend2DAlgorithm.SimpleDirectional
            };
        }
        
        private static StatePlayableResult BuildLinearBlend(PlayableGraph graph, LinearBlendStateAsset state)
        {
            var blendClips = state.BlendClips?
                .Where(c => c.Clip?.Clip != null)
                .OrderBy(c => c.Threshold)
                .ToArray() ?? Array.Empty<ClipWithThreshold>();
            
            if (blendClips.Length == 0)
            {
                return BuildEmpty(graph, "No clips in blend state");
            }
            
            var mixer = AnimationMixerPlayable.Create(graph, blendClips.Length);
            var clipPlayables = new AnimationClipPlayable[blendClips.Length];
            var clipDurations = new float[blendClips.Length];
            var clipThresholds = new float[blendClips.Length];
            var clipPositions = new float2[blendClips.Length];
            var clipWeights = new float[blendClips.Length];
            
            for (int i = 0; i < blendClips.Length; i++)
            {
                var clip = blendClips[i].Clip.Clip;
                var clipPlayable = AnimationClipPlayable.Create(graph, clip);
                
                clipPlayables[i] = clipPlayable;
                clipDurations[i] = clip.length;
                clipThresholds[i] = blendClips[i].Threshold;
                clipPositions[i] = new float2(blendClips[i].Threshold, 0);
                
                graph.Connect(clipPlayable, 0, mixer, i);
            }
            
            return new StatePlayableResult
            {
                RootPlayable = mixer,
                Mixer = mixer,
                ClipPlayables = clipPlayables,
                ClipDurations = clipDurations,
                ClipThresholds = clipThresholds,
                ClipPositions = clipPositions,
                ClipWeights = clipWeights,
                Is2DBlend = false,
                Algorithm = Blend2DAlgorithm.SimpleDirectional
            };
        }
        
        private static StatePlayableResult Build2DBlend(PlayableGraph graph, Directional2DBlendStateAsset state)
        {
            var blendClips = state.BlendClips?
                .Where(c => c.Clip?.Clip != null)
                .ToArray() ?? Array.Empty<Directional2DClipWithPosition>();
            
            if (blendClips.Length == 0)
            {
                return BuildEmpty(graph, "No clips in 2D blend state");
            }
            
            var mixer = AnimationMixerPlayable.Create(graph, blendClips.Length);
            var clipPlayables = new AnimationClipPlayable[blendClips.Length];
            var clipDurations = new float[blendClips.Length];
            var clipThresholds = new float[blendClips.Length];
            var clipPositions = new float2[blendClips.Length];
            var clipWeights = new float[blendClips.Length];
            
            for (int i = 0; i < blendClips.Length; i++)
            {
                var clip = blendClips[i].Clip.Clip;
                var clipPlayable = AnimationClipPlayable.Create(graph, clip);
                
                clipPlayables[i] = clipPlayable;
                clipDurations[i] = clip.length;
                clipThresholds[i] = blendClips[i].Position.x;
                clipPositions[i] = blendClips[i].Position;
                
                graph.Connect(clipPlayable, 0, mixer, i);
            }
            
            return new StatePlayableResult
            {
                RootPlayable = mixer,
                Mixer = mixer,
                ClipPlayables = clipPlayables,
                ClipDurations = clipDurations,
                ClipThresholds = clipThresholds,
                ClipPositions = clipPositions,
                ClipWeights = clipWeights,
                Is2DBlend = true,
                Algorithm = state.Algorithm
            };
        }
        
        private static StatePlayableResult BuildEmpty(PlayableGraph graph, string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                Debug.LogWarning($"[StatePlayableBuilder] {reason}");
            }
            
            return new StatePlayableResult
            {
                RootPlayable = Playable.Null,
                Mixer = default,
                ClipPlayables = Array.Empty<AnimationClipPlayable>(),
                ClipDurations = Array.Empty<float>(),
                ClipThresholds = Array.Empty<float>(),
                ClipPositions = Array.Empty<float2>(),
                ClipWeights = Array.Empty<float>(),
                Is2DBlend = false
            };
        }
        
        #endregion
        
        #region Weight Updates
        
        /// <summary>
        /// Updates blend weights for a state based on blend position.
        /// Applies weights to the mixer if present.
        /// </summary>
        /// <param name="result">The state playable result to update.</param>
        /// <param name="blendPosition">The blend position (X for 1D, X/Y for 2D).</param>
        public static void UpdateBlendWeights(ref StatePlayableResult result, float2 blendPosition)
        {
            if (result.ClipWeights == null || result.ClipWeights.Length == 0)
                return;
            
            // Single clip - always weight 1
            if (result.ClipWeights.Length == 1)
            {
                result.ClipWeights[0] = 1f;
                ApplyWeightsToMixer(ref result);
                return;
            }
            
            // Calculate weights based on blend type
            if (result.Is2DBlend)
            {
                Directional2DBlendUtils.CalculateWeights(
                    blendPosition,
                    result.ClipPositions,
                    result.ClipWeights,
                    result.Algorithm);
            }
            else
            {
                LinearBlendStateUtils.CalculateWeights(
                    blendPosition.x,
                    result.ClipThresholds,
                    result.ClipWeights);
            }
            
            ApplyWeightsToMixer(ref result);
        }
        
        /// <summary>
        /// Applies the current weights to the mixer playable.
        /// </summary>
        private static void ApplyWeightsToMixer(ref StatePlayableResult result)
        {
            if (!result.Mixer.IsValid())
                return;
            
            int inputCount = result.Mixer.GetInputCount();
            for (int i = 0; i < result.ClipWeights.Length && i < inputCount; i++)
            {
                result.Mixer.SetInputWeight(i, result.ClipWeights[i]);
            }
        }
        
        #endregion
        
        #region Time Synchronization
        
        /// <summary>
        /// Synchronizes all clip playables to a normalized time (0-1).
        /// Each clip is set to its own duration * normalizedTime.
        /// </summary>
        /// <param name="result">The state playable result.</param>
        /// <param name="normalizedTime">Normalized time (0 = start, 1 = end).</param>
        public static void SyncClipTimes(ref StatePlayableResult result, float normalizedTime)
        {
            if (result.ClipPlayables == null)
                return;
            
            normalizedTime = Mathf.Clamp01(normalizedTime);
            
            for (int i = 0; i < result.ClipPlayables.Length; i++)
            {
                if (!result.ClipPlayables[i].IsValid())
                    continue;
                
                float duration = result.ClipDurations[i];
                if (duration <= 0)
                    continue;
                
                float clipTime = normalizedTime * duration;
                
                // Handle looping
                var clip = result.ClipPlayables[i].GetAnimationClip();
                if (clip != null && clip.isLooping)
                {
                    clipTime = clipTime % duration;
                }
                else
                {
                    clipTime = Mathf.Min(clipTime, duration);
                }
                
                result.ClipPlayables[i].SetTime(clipTime);
            }
        }
        
        #endregion
        
        #region Disposal
        
        /// <summary>
        /// Destroys all playables in the result.
        /// Call this before rebuilding or when disposing the preview.
        /// </summary>
        /// <param name="result">The result to dispose.</param>
        public static void Dispose(ref StatePlayableResult result)
        {
            if (result.ClipPlayables != null)
            {
                foreach (var clip in result.ClipPlayables)
                {
                    if (clip.IsValid())
                        clip.Destroy();
                }
            }
            
            if (result.Mixer.IsValid())
            {
                result.Mixer.Destroy();
            }
            
            // Note: Don't destroy RootPlayable separately - it's either the Mixer or a ClipPlayable
            // which we already destroyed above
            
            result = default;
        }
        
        #endregion
    }
}
