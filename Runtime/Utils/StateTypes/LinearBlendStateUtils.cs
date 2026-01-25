using System;
using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace DMotion
{
    internal static class LinearBlendStateUtils
    {
        internal static LinearBlendStateMachineState NewForStateMachine(
            short stateIndex,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ref DynamicBuffer<LinearBlendStateMachineState> linearBlendStates,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float finalSpeed,
            in DynamicBuffer<FloatParameter> floatParameters,
            in DynamicBuffer<IntParameter> intParameters,
            float normalizedOffset = 0f)
        {
            var linearBlendState = new LinearBlendStateMachineState
            {
                StateMachineBlob = stateMachineBlob,
                StateIndex = stateIndex
            };

            ref var linearBlendBlob = ref linearBlendState.LinearBlendBlob;
            Assert.AreEqual(linearBlendBlob.SortedClipIndexes.Length, linearBlendBlob.SortedClipThresholds.Length);
            Assert.AreEqual(linearBlendBlob.SortedClipIndexes.Length, linearBlendBlob.SortedClipSpeeds.Length);
            var clipCount = (byte)linearBlendBlob.SortedClipIndexes.Length;

            // Calculate initial time based on offset and current blend state
            float initialTime = 0f;
            if (math.abs(normalizedOffset) > 0.001f)
            {
                // Sanitize offset based on loop mode
                if (linearBlendState.StateBlob.Loop)
                {
                    normalizedOffset = normalizedOffset - math.floor(normalizedOffset);
                }
                else
                {
                    normalizedOffset = math.clamp(normalizedOffset, 0f, 1f);
                }

                // 1. Extract variables (blend ratio, thresholds, speeds)
                ExtractLinearBlendVariablesFromStateMachine(
                    linearBlendState,
                    floatParameters,
                    intParameters,
                    out var blendRatio,
                    out var thresholds,
                    out var speeds);

                // 2. Calculate weights
                var weights = new NativeArray<float>(clipCount, Allocator.Temp);
                CalculateWeights(blendRatio, thresholds, weights);

                // 3. Get clip durations
                var clipDurations = new NativeArray<float>(clipCount, Allocator.Temp);
                for (int i = 0; i < clipCount; i++)
                {
                    var clipIndex = linearBlendBlob.SortedClipIndexes[i];
                    clipDurations[i] = clips.Value.clips[clipIndex].duration;
                }

                // 4. Calculate effective duration (weighted average of clip durations)
                float weightedDuration = 0f;
                float totalWeight = 0f;
                for (int i = 0; i < weights.Length; i++)
                {
                    if (weights[i] > 0.001f)
                    {
                        float speed = speeds[i];
                        if (speed <= 0.0001f) speed = 1f;
                        float duration = clipDurations[i] / speed;
                        weightedDuration += weights[i] * duration;
                        totalWeight += weights[i];
                    }
                }
                float effectiveDuration = totalWeight > 0.001f ? weightedDuration / totalWeight : 1f;

                initialTime = normalizedOffset * effectiveDuration;
            }

            var newSamplers =
                new NativeArray<ClipSampler>(clipCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < clipCount; i++)
            {
                var clipIndex = (ushort)linearBlendBlob.SortedClipIndexes[i];
                newSamplers[i] = new ClipSampler
                {
                    ClipIndex = clipIndex,
                    Clips = clips,
                    ClipEventsBlob = clipEvents,
                    PreviousTime = initialTime,
                    Time = initialTime,
                    Weight = 0
                };
            }

            var animationStateIndex = AnimationState.New(ref animationStates, ref samplers, newSamplers,
                finalSpeed,
                linearBlendState.StateBlob.Loop,
                initialTime);

            if (animationStateIndex < 0)
            {
                // Failed to allocate - return invalid state (caller should check validity)
                return default;
            }

            linearBlendState.AnimationStateId = animationStates[animationStateIndex].Id;
            linearBlendStates.Add(linearBlendState);

            return linearBlendState;
        }

        internal static void ExtractLinearBlendVariablesFromStateMachine(
            in LinearBlendStateMachineState linearBlendState,
            in DynamicBuffer<FloatParameter> floatParameters,
            in DynamicBuffer<IntParameter> intParameters,
            out float blendRatio,
            out NativeArray<float> thresholds,
            out NativeArray<float> speeds)
        {
            ref var linearBlendBlob = ref linearBlendState.LinearBlendBlob;
            
            if (linearBlendBlob.UsesIntParameter)
            {
                // Read Int parameter and normalize to 0-1 range
                var intValue = intParameters[linearBlendBlob.BlendParameterIndex].Value;
                var rangeMin = linearBlendBlob.IntRangeMin;
                var rangeMax = linearBlendBlob.IntRangeMax;
                var range = rangeMax - rangeMin;
                
                // Normalize: (value - min) / (max - min), clamped to 0-1
                blendRatio = range > 0 
                    ? math.saturate((float)(intValue - rangeMin) / range)
                    : 0f;
            }
            else
            {
                // Read Float parameter directly
                blendRatio = floatParameters[linearBlendBlob.BlendParameterIndex].Value;
            }
            
            thresholds = CollectionUtils.AsArray(ref linearBlendBlob.SortedClipThresholds);
            speeds = CollectionUtils.AsArray(ref linearBlendBlob.SortedClipSpeeds);
        }

        /// <summary>
        /// Calculates 1D blend weights for a given blend value and thresholds.
        /// Optimized for ECS (NativeArray).
        /// </summary>
        internal static void CalculateWeights(
            float blendValue,
            in NativeArray<float> thresholds,
            NativeArray<float> weights)
        {
            if (thresholds.Length == 0) return;
            
            // Zero all weights first
            for (int i = 0; i < weights.Length; i++) weights[i] = 0f;
            
            if (thresholds.Length == 1)
            {
                weights[0] = 1f;
                return;
            }
            
            FindActiveClipIndexes(blendValue, thresholds, out int firstIndex, out int secondIndex);
            
            // Handle edge cases (before first or after last)
            if (firstIndex == -1 && secondIndex == -1)
            {
                // Fallback: strictly clamp to range
                if (blendValue <= thresholds[0]) weights[0] = 1f;
                else weights[thresholds.Length - 1] = 1f;
                return;
            }
            
            float lower = thresholds[firstIndex];
            float upper = thresholds[secondIndex];
            float range = upper - lower;
            
            if (range <= 0.0001f)
            {
                weights[firstIndex] = 1f;
                return;
            }
            
            float t = (blendValue - lower) / range;
            weights[firstIndex] = 1f - t;
            weights[secondIndex] = t;
        }

        /// <summary>
        /// Calculates 1D blend weights for a given blend value and thresholds.
        /// Managed array overload for Editor/Preview.
        /// </summary>
        public static void CalculateWeights(
            float blendValue,
            float[] thresholds,
            float[] weights)
        {
            if (thresholds == null || thresholds.Length == 0) return;
            
            // Zero all weights
            Array.Clear(weights, 0, weights.Length);
            
            if (thresholds.Length == 1)
            {
                weights[0] = 1f;
                return;
            }
            
            // Use same logic as ECS
            int firstIndex = -1;
            int secondIndex = -1;
            
            // Clamp blend value
            float clampedBlend = math.clamp(blendValue, thresholds[0], thresholds[thresholds.Length - 1]);
            
            for (var i = 1; i < thresholds.Length; i++)
            {
                var currentThreshold = thresholds[i];
                var prevThreshold = thresholds[i - 1];
                if (clampedBlend >= prevThreshold && clampedBlend <= currentThreshold)
                {
                    firstIndex = i - 1;
                    secondIndex = i;
                    break;
                }
            }
            
            // Handle edges if not found (should be covered by clamp, but for safety)
            if (firstIndex == -1)
            {
                if (blendValue <= thresholds[0]) weights[0] = 1f;
                else weights[thresholds.Length - 1] = 1f;
                return;
            }
            
            float lower = thresholds[firstIndex];
            float upper = thresholds[secondIndex];
            float range = upper - lower;
            
            if (range <= 0.0001f)
            {
                weights[firstIndex] = 1f;
                return;
            }
            
            float t = (clampedBlend - lower) / range;
            weights[firstIndex] = 1f - t;
            weights[secondIndex] = t;
        }

        /// <summary>
        /// Calculates the effective duration of a blend state based on weights.
        /// Weighted average of (ClipLength / ClipSpeed).
        /// </summary>
        public static float CalculateEffectiveDuration(
            float[] weights,
            float[] clipDurations,
            float[] clipSpeeds)
        {
            float weightedDuration = 0f;
            float totalWeight = 0f;
            
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0.001f)
                {
                    float speed = clipSpeeds[i];
                    // Handle 0 or negative speed safely
                    if (speed <= 0.0001f) speed = 1f;
                    
                    float duration = clipDurations[i] / speed;
                    weightedDuration += weights[i] * duration;
                    totalWeight += weights[i];
                }
            }
            
            return totalWeight > 0.001f ? weightedDuration / totalWeight : 1f;
        }

        /// <summary>
        /// Calculates the effective speed of a blend state based on weights.
        /// Weighted average of ClipSpeed.
        /// </summary>
        public static float CalculateEffectiveSpeed(
            float[] weights,
            float[] clipSpeeds)
        {
            float weightedSpeed = 0f;
            float totalWeight = 0f;
            
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0.001f)
                {
                    float speed = clipSpeeds[i];
                    if (speed <= 0.0001f) speed = 1f; // Default for invalid speed
                    
                    weightedSpeed += weights[i] * speed;
                    totalWeight += weights[i];
                }
            }
            
            return totalWeight > 0.001f ? weightedSpeed / totalWeight : 1f;
        }

        /// <summary>
        /// Sets sampler weights based on blend ratio without advancing time.
        /// Used by both normal playback and preview systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetSamplerWeights(
            float blendRatio,
            in NativeArray<float> thresholds,
            float stateWeight,
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex)
        {
            Assert.IsTrue(thresholds.IsCreated);
            
            // Bounds check: ensure we don't access beyond samplers buffer
            int availableCount = samplers.Length - startIndex;
            if (availableCount <= 0)
                return;
            
            int clipCount = math.min(thresholds.Length, availableCount);
            if (clipCount < 2)
            {
                // Need at least 2 clips for blending; fallback to single clip
                if (clipCount == 1 && startIndex < samplers.Length)
                {
                    var sampler = samplers[startIndex];
                    sampler.Weight = stateWeight;
                    samplers[startIndex] = sampler;
                }
                return;
            }
            
            var endIndex = startIndex + clipCount - 1;

            // Clamp blend ratio to threshold range (using available clip count)
            blendRatio = math.clamp(blendRatio, thresholds[0], thresholds[clipCount - 1]);

            FindActiveClipIndexes(blendRatio, thresholds, out var firstClipIndex, out var secondClipIndex);
            
            // Ensure indices are within our bounded clip count
            if (secondClipIndex >= clipCount)
            {
                secondClipIndex = clipCount - 1;
                firstClipIndex = math.max(0, secondClipIndex - 1);
            }

            var firstThreshold = thresholds[firstClipIndex];
            var secondThreshold = thresholds[secondClipIndex];

            var firstSamplerIndex = startIndex + firstClipIndex;
            var secondSamplerIndex = startIndex + secondClipIndex;

            // Zero all weights first
            for (var i = startIndex; i <= endIndex; i++)
            {
                var sampler = samplers[i];
                sampler.Weight = 0;
                samplers[i] = sampler;
            }

            // Calculate blend factor and set active clip weights
            var t = (blendRatio - firstThreshold) / (secondThreshold - firstThreshold);
            
            var firstSampler = samplers[firstSamplerIndex];
            var secondSampler = samplers[secondSamplerIndex];
            
            firstSampler.Weight = (1 - t) * stateWeight;
            secondSampler.Weight = t * stateWeight;
            
            samplers[firstSamplerIndex] = firstSampler;
            samplers[secondSamplerIndex] = secondSampler;
        }
        
        /// <summary>
        /// Sets sampler times to a specific normalized time (0-1).
        /// Used by preview systems for explicit time control.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetSamplerTimes(
            float normalizedTime,
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex,
            int count)
        {
            var endIndex = startIndex + count;
            for (var i = startIndex; i < endIndex && i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                float clipDuration = sampler.Duration > 0 ? sampler.Duration : 1f;
                sampler.PreviousTime = sampler.Time;
                sampler.Time = normalizedTime * clipDuration;
                samplers[i] = sampler;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void UpdateSamplers(
            float dt,
            float blendRatio,
            in NativeArray<float> thresholds,
            in NativeArray<float> speeds,
            in AnimationState animation,
            ref DynamicBuffer<ClipSampler> samplers)
        {
            Assert.IsTrue(thresholds.IsCreated);
            var startIndex = samplers.IdToIndex(animation.StartSamplerId);
            
            // Bounds check: ensure we have enough samplers
            int availableCount = samplers.Length - startIndex;
            if (availableCount < 2 || thresholds.Length < 2)
                return;
            
            int clipCount = math.min(thresholds.Length, availableCount);
            var endIndex = startIndex + clipCount - 1;

            // Set weights using shared utility
            SetSamplerWeights(blendRatio, thresholds, animation.Weight, ref samplers, startIndex);

            // Get the active clip indices for time calculation
            blendRatio = math.clamp(blendRatio, thresholds[0], thresholds[clipCount - 1]);
            FindActiveClipIndexes(blendRatio, thresholds, out var firstClipIndex, out var secondClipIndex);
            
            // Ensure indices are within bounds
            if (secondClipIndex >= clipCount)
            {
                secondClipIndex = clipCount - 1;
                firstClipIndex = math.max(0, secondClipIndex - 1);
            }

            var firstSamplerIndex = startIndex + firstClipIndex;
            var secondSamplerIndex = startIndex + secondClipIndex;

            var firstSampler = samplers[firstSamplerIndex];
            var secondSampler = samplers[secondSamplerIndex];

            // Update clip times (advance by dt)
            {
                var firstClipSpeed = math.select(speeds[firstClipIndex], 1, speeds[firstClipIndex] <= 0);
                var secondClipSpeed = math.select(speeds[secondClipIndex], 1, speeds[secondClipIndex] <= 0);

                var loopDuration = firstSampler.Clip.duration / firstClipSpeed * firstSampler.Weight +
                                   secondSampler.Clip.duration / secondClipSpeed * secondSampler.Weight;

                // We don't want to divide by zero if our weight is zero
                if (!mathex.iszero(loopDuration))
                {
                    var invLoopDuration = 1.0f / loopDuration;
                    var stateSpeed = animation.Speed;
                    for (var i = startIndex; i <= endIndex && i < samplers.Length; i++)
                    {
                        var sampler = samplers[i];
                        var samplerSpeed = stateSpeed * sampler.Clip.duration * invLoopDuration;

                        sampler.PreviousTime = sampler.Time;
                        sampler.Time += dt * samplerSpeed;

                        if (animation.Loop)
                        {
                            sampler.LoopToClipTime();
                        }

                        samplers[i] = sampler;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void FindActiveClipIndexes(
            float blendRatio,
            in NativeArray<float> thresholds,
            out int firstClipIndex, out int secondClipIndex)
        {
            //we assume thresholds are sorted here
            firstClipIndex = -1;
            secondClipIndex = -1;
            for (var i = 1; i < thresholds.Length; i++)
            {
                var currentThreshold = thresholds[i];
                var prevThreshold = thresholds[i - 1];
                if (blendRatio >= prevThreshold && blendRatio <= currentThreshold)
                {
                    firstClipIndex = i - 1;
                    secondClipIndex = i;
                    break;
                }
            }

            Assert.IsTrue(firstClipIndex >= 0 && secondClipIndex >= 0);
        }
    }
}