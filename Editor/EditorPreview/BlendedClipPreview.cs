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
    /// Preview for blend states (1D and 2D).
    /// Builds a PlayableGraph with an AnimationMixerPlayable that blends between clips
    /// based on the current blend position.
    /// </summary>
    public class BlendedClipPreview : PlayableGraphPreview
    {
        #region Nested Types
        
        /// <summary>
        /// Clip data for blending.
        /// </summary>
        public struct BlendClipData
        {
            public AnimationClip Clip;
            public float2 Position; // X only for 1D, X and Y for 2D
            public float Speed;
        }
        
        #endregion
        
        #region Fields
        
        private readonly BlendClipData[] clipData;
        private readonly bool is2D;
        private readonly Blend2DAlgorithm algorithm;
        private readonly float2[] cachedPositions; // Cached positions for weight calculation
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] clipPlayables; // Store references to set individual clip times
        private float2 currentBlendPosition;
        private float2 targetBlendPosition;
        private float2 currentVelocity; // For smooth damping blend position
        private float normalizedSampleTime;
        private float[] cachedWeights;
        
        // Individual clip preview mode
        private int soloClipIndex = -1; // -1 = blended mode, >= 0 = solo clip
        
        // Smoothing parameters - using critically damped spring approach
        private const float SmoothTime = 0.08f; // Time to reach target (lower = faster)
        private const float MaxSpeed = 50f; // Maximum velocity cap
        private const float SnapThreshold = 0.0005f; // Snap to target when this close
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The current (smoothed) blend position. X component for 1D, X and Y for 2D.
        /// </summary>
        public float2 BlendPosition
        {
            get => currentBlendPosition;
            set => SetBlendPositionTarget(value);
        }
        
        /// <summary>
        /// The target blend position that we're smoothly transitioning towards.
        /// </summary>
        public float2 TargetBlendPosition => targetBlendPosition;
        
        /// <summary>
        /// Whether the blend position is currently transitioning towards the target.
        /// </summary>
        public bool IsTransitioning => math.distance(currentBlendPosition, targetBlendPosition) > SnapThreshold;
        
        /// <summary>
        /// Gets the current blend weights for each clip.
        /// </summary>
        public float[] CurrentWeights => cachedWeights;
        
        protected override IEnumerable<AnimationClip> Clips => clipData.Select(c => c.Clip).Where(c => c != null);
        
        public override float SampleTime
        {
            get
            {
                // Sync individual clip times before sampling
                SyncClipTimes();
                // Return 0 - we manually set each clip's time based on normalized time
                return 0;
            }
        }
        
        /// <summary>
        /// Gets the effective duration for the current blend (for UI display).
        /// This is the weighted average of clip durations.
        /// </summary>
        public float EffectiveDuration => GetWeightedDuration();
        
        /// <summary>
        /// Gets the weighted average speed based on current blend weights.
        /// This combines individual clip speeds weighted by their blend contribution.
        /// </summary>
        public float WeightedSpeed => GetWeightedSpeed();
        
        public override float NormalizedSampleTime
        {
            get => normalizedSampleTime;
            set => normalizedSampleTime = Mathf.Clamp01(value);
        }
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Creates a 1D blend preview from a LinearBlendStateAsset.
        /// </summary>
        public BlendedClipPreview(LinearBlendStateAsset state) : this(ConvertToBlendData(state), false, Blend2DAlgorithm.SimpleDirectional)
        {
        }
        
        /// <summary>
        /// Creates a 2D blend preview from a Directional2DBlendStateAsset.
        /// </summary>
        public BlendedClipPreview(Directional2DBlendStateAsset state) : this(ConvertToBlendData(state), true, state.Algorithm)
        {
        }
        
        /// <summary>
        /// Creates a blend preview from raw clip data.
        /// </summary>
        public BlendedClipPreview(BlendClipData[] clips, bool is2D, Blend2DAlgorithm algorithm = Blend2DAlgorithm.SimpleDirectional)
        {
            clipData = clips ?? Array.Empty<BlendClipData>();
            this.is2D = is2D;
            this.algorithm = algorithm;
            cachedWeights = new float[clipData.Length];
            cachedPositions = clipData.Select(c => c.Position).ToArray();
            clipPlayables = null; // Initialized in BuildGraph
            normalizedSampleTime = 0;
            currentBlendPosition = float2.zero;
            targetBlendPosition = float2.zero;
            currentVelocity = float2.zero;
        }
        
        #endregion
        
        #region Blend Position Control
        
        /// <summary>
        /// Sets the target blend position for smooth transition.
        /// The current position will interpolate towards this target over time.
        /// </summary>
        public void SetBlendPositionTarget(float2 target)
        {
            targetBlendPosition = target;
        }
        
        /// <summary>
        /// Immediately sets the blend position without smooth transition.
        /// Use this for initial setup or when you want instant changes.
        /// </summary>
        public void SetBlendPositionImmediate(float2 position)
        {
            currentBlendPosition = position;
            targetBlendPosition = position;
            currentVelocity = float2.zero; // Reset velocity to prevent momentum
            UpdateMixerWeights();
        }
        
        /// <summary>
        /// Sets the preview to show only a single clip (solo mode).
        /// Pass -1 to return to blended mode.
        /// </summary>
        /// <param name="clipIndex">Index of the clip to solo, or -1 for blended mode.</param>
        public void SetSoloClip(int clipIndex)
        {
            soloClipIndex = clipIndex;
            UpdateMixerWeights();
        }
        
        /// <summary>
        /// Gets the currently soloed clip index, or -1 if in blended mode.
        /// </summary>
        public int SoloClipIndex => soloClipIndex;
        
        /// <summary>
        /// Updates the smooth blend position transition.
        /// Call this every frame when IsTransitioning is true.
        /// Uses critically damped spring (SmoothDamp) for natural motion that handles rapid target changes.
        /// </summary>
        /// <param name="deltaTime">Time since last update in seconds.</param>
        /// <returns>True if still transitioning, false if reached target.</returns>
        public bool Tick(float deltaTime)
        {
            if (!IsTransitioning)
            {
                currentVelocity = float2.zero;
                return false;
            }
            
            // Clamp deltaTime to prevent instability with large time steps
            deltaTime = math.min(deltaTime, 0.1f);
            
            // Critically damped spring (SmoothDamp) for blend position
            currentBlendPosition = SmoothDamp(
                currentBlendPosition,
                targetBlendPosition,
                ref currentVelocity,
                SmoothTime,
                MaxSpeed,
                deltaTime);
            
            // Snap to target when very close to avoid endless tiny movements
            float distance = math.distance(currentBlendPosition, targetBlendPosition);
            if (distance <= SnapThreshold && math.length(currentVelocity) < 0.01f)
            {
                currentBlendPosition = targetBlendPosition;
                currentVelocity = float2.zero;
            }
            
            UpdateMixerWeights();
            
            return IsTransitioning;
        }
        
        /// <summary>
        /// 2D SmoothDamp implementation using critically damped spring.
        /// Handles rapid target changes without stuttering.
        /// </summary>
        private static float2 SmoothDamp(float2 current, float2 target, ref float2 velocity, 
            float smoothTime, float maxSpeed, float deltaTime)
        {
            // Prevent division by zero
            smoothTime = math.max(0.0001f, smoothTime);
            
            // Calculate spring constants for critical damping
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            
            float2 change = current - target;
            float2 originalTo = target;
            
            // Clamp maximum speed
            float maxChange = maxSpeed * smoothTime;
            float changeLength = math.length(change);
            if (changeLength > maxChange)
            {
                change = change / changeLength * maxChange;
            }
            
            target = current - change;
            
            float2 temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;
            
            float2 result = target + (change + temp) * exp;
            
            // Prevent overshooting
            float2 toOriginal = originalTo - current;
            float2 toResult = result - originalTo;
            
            if (math.dot(toOriginal, toResult) > 0)
            {
                result = originalTo;
                velocity = (result - originalTo) / deltaTime;
            }
            
            return result;
        }
        
        #endregion
        
        #region Static Converters
        
        private static BlendClipData[] ConvertToBlendData(LinearBlendStateAsset state)
        {
            if (state?.BlendClips == null) return Array.Empty<BlendClipData>();
            
            // Base state speed multiplier
            float stateSpeed = state.Speed > 0 ? state.Speed : 1f;
            
            return state.BlendClips
                .Where(c => c.Clip?.Clip != null)
                .Select(c => new BlendClipData
                {
                    Clip = c.Clip.Clip,
                    Position = new float2(c.Threshold, 0),
                    // Combine clip speed with state speed
                    Speed = (c.Speed > 0 ? c.Speed : 1f) * stateSpeed
                })
                .OrderBy(c => c.Position.x)
                .ToArray();
        }
        
        private static BlendClipData[] ConvertToBlendData(Directional2DBlendStateAsset state)
        {
            if (state?.BlendClips == null) return Array.Empty<BlendClipData>();
            
            // Base state speed multiplier
            float stateSpeed = state.Speed > 0 ? state.Speed : 1f;
            
            return state.BlendClips
                .Where(c => c.Clip?.Clip != null)
                .Select(c => new BlendClipData
                {
                    Clip = c.Clip.Clip,
                    Position = c.Position,
                    // Combine clip speed with state speed
                    Speed = (c.Speed > 0 ? c.Speed : 1f) * stateSpeed
                })
                .ToArray();
        }
        
        #endregion
        
        #region PlayableGraph
        
        protected override PlayableGraph BuildGraph()
        {
            var graph = PlayableGraph.Create("BlendedClipPreview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            
            var playableOutput = AnimationPlayableOutput.Create(graph, "Animation", animator);
            
            if (clipData.Length == 0)
            {
                clipPlayables = Array.Empty<AnimationClipPlayable>();
                return graph;
            }
            
            // Create mixer
            mixer = AnimationMixerPlayable.Create(graph, clipData.Length);
            playableOutput.SetSourcePlayable(mixer);
            
            // Create and store clip playables - DO NOT set speed here
            // Speed is handled by adjusting sample time per clip
            clipPlayables = new AnimationClipPlayable[clipData.Length];
            for (int i = 0; i < clipData.Length; i++)
            {
                var clipPlayable = AnimationClipPlayable.Create(graph, clipData[i].Clip);
                clipPlayables[i] = clipPlayable;
                graph.Connect(clipPlayable, 0, mixer, i);
            }
            
            // Initialize weights and sync times
            UpdateMixerWeights();
            SyncClipTimes();
            
            return graph;
        }
        
        /// <summary>
        /// Synchronizes all clip times based on normalized sample time.
        /// Each clip is set to its own time: normalizedTime * clipLength
        /// This ensures all clips are at the same normalized position regardless of their individual lengths.
        /// Speed does NOT affect preview sampling - it only affects runtime playback rate.
        /// </summary>
        private void SyncClipTimes()
        {
            if (clipPlayables == null) return;
            
            for (int i = 0; i < clipPlayables.Length; i++)
            {
                if (!clipPlayables[i].IsValid()) continue;
                
                var clip = clipData[i].Clip;
                if (clip == null) continue;
                
                float clipLength = clip.length;
                if (clipLength <= 0) continue;
                
                // Each clip's time = normalized position in its own timeline
                // All clips are synchronized by normalized time (0 = start, 1 = end)
                float clipTime = normalizedSampleTime * clipLength;
                
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
                
                clipPlayables[i].SetTime(clipTime);
            }
        }
        
        #endregion
        
        #region Weight Calculation
        
        private void UpdateMixerWeights()
        {
            if (clipData.Length == 0) return;
            
            // Check for solo mode (individual clip preview)
            if (soloClipIndex >= 0 && soloClipIndex < clipData.Length)
            {
                // Solo mode: only the selected clip has weight
                for (int i = 0; i < cachedWeights.Length; i++)
                {
                    cachedWeights[i] = (i == soloClipIndex) ? 1f : 0f;
                }
            }
            else
            {
                // Blended mode: calculate weights based on blend position
                if (is2D)
                {
                    Calculate2DWeights();
                }
                else
                {
                    Calculate1DWeights();
                }
            }
            
            // Apply weights to mixer if it exists
            if (mixer.IsValid())
            {
                for (int i = 0; i < clipData.Length; i++)
                {
                    mixer.SetInputWeight(i, cachedWeights[i]);
                }
            }
        }
        
        /// <summary>
        /// Calculates 1D blend weights using linear interpolation between adjacent thresholds.
        /// </summary>
        private void Calculate1DWeights()
        {
            float blendValue = currentBlendPosition.x;
            
            // Reset all weights
            for (int i = 0; i < cachedWeights.Length; i++)
            {
                cachedWeights[i] = 0;
            }
            
            if (clipData.Length == 0) return;
            
            if (clipData.Length == 1)
            {
                cachedWeights[0] = 1;
                return;
            }
            
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
                // Below all thresholds
                cachedWeights[0] = 1;
                return;
            }
            
            if (upperIndex == -1)
            {
                // Above all thresholds
                cachedWeights[clipData.Length - 1] = 1;
                return;
            }
            
            if (lowerIndex == upperIndex)
            {
                // Exactly on a threshold
                cachedWeights[lowerIndex] = 1;
                return;
            }
            
            // Linear interpolation between lower and upper
            float lowerThreshold = clipData[lowerIndex].Position.x;
            float upperThreshold = clipData[upperIndex].Position.x;
            float range = upperThreshold - lowerThreshold;
            
            if (range <= 0.0001f)
            {
                cachedWeights[lowerIndex] = 1;
                return;
            }
            
            float t = (blendValue - lowerThreshold) / range;
            cachedWeights[lowerIndex] = 1 - t;
            cachedWeights[upperIndex] = t;
        }
        
        /// <summary>
        /// Calculates 2D blend weights using the shared utility.
        /// Uses the algorithm specified by the asset (SimpleDirectional or InverseDistanceWeighting).
        /// </summary>
        private void Calculate2DWeights()
        {
            if (clipData.Length == 0) return;
            
            // Use the shared utility with the algorithm from the asset
            Directional2DBlendUtils.CalculateWeights(currentBlendPosition, cachedPositions, cachedWeights, algorithm);
        }
        
        /// <summary>
        /// Gets the weighted average duration based on current blend weights.
        /// Uses weighted average to ensure smooth duration changes when moving through blend space.
        /// </summary>
        private float GetWeightedDuration()
        {
            if (clipData.Length == 0) return 1f;
            
            float weightedDuration = 0f;
            float totalWeight = 0f;
            
            for (int i = 0; i < clipData.Length; i++)
            {
                if (clipData[i].Clip != null && cachedWeights[i] > 0.001f)
                {
                    // Duration is clip length divided by speed (faster speed = shorter effective duration)
                    float clipDuration = clipData[i].Clip.length / clipData[i].Speed;
                    weightedDuration += cachedWeights[i] * clipDuration;
                    totalWeight += cachedWeights[i];
                }
            }
            
            if (totalWeight > 0.001f)
            {
                return weightedDuration / totalWeight;
            }
            
            return 1f;
        }
        
        /// <summary>
        /// Gets the weighted average speed based on current blend weights.
        /// Returns 1.0 if no valid clips or weights.
        /// </summary>
        private float GetWeightedSpeed()
        {
            if (clipData.Length == 0) return 1f;
            
            float weightedSpeed = 0f;
            float totalWeight = 0f;
            
            for (int i = 0; i < clipData.Length; i++)
            {
                if (clipData[i].Clip != null && cachedWeights[i] > 0.001f)
                {
                    weightedSpeed += cachedWeights[i] * clipData[i].Speed;
                    totalWeight += cachedWeights[i];
                }
            }
            
            if (totalWeight > 0.001f)
            {
                return weightedSpeed / totalWeight;
            }
            
            return 1f;
        }
        
        #endregion
    }
}
