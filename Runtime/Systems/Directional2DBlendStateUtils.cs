using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace DMotion
{
    internal static class Directional2DBlendStateUtils
    {
        internal static Directional2DBlendStateMachineState NewForStateMachine(
            short stateIndex,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ref DynamicBuffer<Directional2DBlendStateMachineState> directional2DStates,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float finalSpeed,
            in DynamicBuffer<FloatParameter> floatParameters,
            float normalizedOffset = 0f)
        {
            var directional2DState = new Directional2DBlendStateMachineState
            {
                StateMachineBlob = stateMachineBlob,
                StateIndex = stateIndex
            };

            ref var directional2DBlob = ref directional2DState.Directional2DBlob;
            var clipCount = (byte)directional2DBlob.ClipIndexes.Length;

            // Calculate initial time based on offset
            float initialTime = 0f;
            if (math.abs(normalizedOffset) > 0.001f)
            {
                // Sanitize offset based on loop mode
                if (directional2DState.StateBlob.Loop)
                {
                    normalizedOffset = normalizedOffset - math.floor(normalizedOffset);
                }
                else
                {
                    normalizedOffset = math.clamp(normalizedOffset, 0f, 1f);
                }

                // 1. Extract variables
                ExtractVariables(directional2DState, floatParameters, out var input, out var positions, out var speeds);
                
                // 2. Calculate weights
                var weights = new NativeArray<float>(clipCount, Allocator.Temp);
                Directional2DBlendUtils.CalculateWeights(input, positions, weights, directional2DBlob.Algorithm);
                
                // 3. Get clip durations
                var clipDurations = new NativeArray<float>(clipCount, Allocator.Temp);
                for (int i = 0; i < clipCount; i++)
                {
                    var clipIndex = directional2DBlob.ClipIndexes[i];
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
                var clipIndex = (ushort)directional2DBlob.ClipIndexes[i];
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
                directional2DState.StateBlob.Loop,
                initialTime);

            if (animationStateIndex < 0)
            {
                // Failed to allocate - return invalid state (caller should check validity)
                return default;
            }

            directional2DState.AnimationStateId = animationStates[animationStateIndex].Id;
            directional2DStates.Add(directional2DState);

            return directional2DState;
        }

        internal static void ExtractVariables(
            in Directional2DBlendStateMachineState state,
            in DynamicBuffer<FloatParameter> floatParameters,
            out float2 input,
            out NativeArray<float2> positions,
            out NativeArray<float> speeds)
        {
            ref var blob = ref state.Directional2DBlob;
            
            float x = floatParameters[blob.BlendParameterIndexX].Value;
            float y = floatParameters[blob.BlendParameterIndexY].Value;
            input = new float2(x, y);
            
            positions = CollectionUtils.AsArray(ref blob.ClipPositions);
            speeds = CollectionUtils.AsArray(ref blob.ClipSpeeds);
        }

        /// <summary>
        /// Sets sampler weights from pre-calculated weights without advancing time.
        /// Used by both normal playback and preview systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetSamplerWeights(
            in NativeArray<float> weights,
            float stateWeight,
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex,
            int clipCount)
        {
            var endIndex = startIndex + clipCount - 1;
            for (var i = startIndex; i <= endIndex && i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                sampler.Weight = weights[i - startIndex] * stateWeight;
                samplers[i] = sampler;
            }
        }
        
        /// <summary>
        /// Calculates weights using inverse distance and sets them directly.
        /// Alternative to using pre-calculated weights array.
        /// Used by preview systems that don't have Directional2DBlendStateMachineState.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetSamplerWeightsFromPosition(
            float2 blendPosition,
            ref BlobArray<float2> positions,
            float stateWeight,
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex,
            int clipCount)
        {
            // Two-pass inverse distance weighting
            float totalWeight = 0f;
            int posCount = positions.Length;
            
            for (int i = 0; i < posCount; i++)
            {
                float2 clipPos = positions[i];
                float dist = math.length(blendPosition - clipPos);
                float weight = 1f / (dist + 0.001f);
                totalWeight += weight;
            }
            
            if (totalWeight > 0.0001f)
            {
                float invTotal = 1f / totalWeight;
                for (int i = 0; i < posCount && (startIndex + i) < samplers.Length; i++)
                {
                    float2 clipPos = positions[i];
                    float dist = math.length(blendPosition - clipPos);
                    float weight = 1f / (dist + 0.001f);
                    
                    var sampler = samplers[startIndex + i];
                    sampler.Weight = weight * invTotal * stateWeight;
                    samplers[startIndex + i] = sampler;
                }
            }
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
            int clipCount)
        {
            var endIndex = startIndex + clipCount;
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
            in NativeArray<float2> positions,
            in NativeArray<float> speeds,
            in NativeArray<float> weights,
            in AnimationState animation,
            ref DynamicBuffer<ClipSampler> samplers)
        {
            Assert.IsTrue(positions.IsCreated);
            var startIndex = samplers.IdToIndex(animation.StartSamplerId);
            var endIndex = startIndex + positions.Length - 1;

            // Set weights using shared utility
            SetSamplerWeights(weights, animation.Weight, ref samplers, startIndex, positions.Length);

            // Calculate weighted loop duration to synchronize clips
            // Similar to LinearBlend: loopDuration = sum(duration_i / speed_i * weight_i)
            float loopDuration = 0f;
            for (var i = startIndex; i <= endIndex; i++)
            {
                var sampler = samplers[i];
                var clipSpeed = math.select(speeds[i - startIndex], 1f, speeds[i - startIndex] <= 0f);
                var weight = weights[i - startIndex];
                if (weight > 0f)
                {
                    loopDuration += (sampler.Clip.duration / clipSpeed) * weight;
                }
            }

            // Update clip times (advance by dt)
            if (!mathex.iszero(loopDuration))
            {
                var invLoopDuration = 1.0f / loopDuration;
                var stateSpeed = animation.Speed;

                for (var i = startIndex; i <= endIndex; i++)
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
}
