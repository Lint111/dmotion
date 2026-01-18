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
            float finalSpeed)
        {
            var directional2DState = new Directional2DBlendStateMachineState
            {
                StateMachineBlob = stateMachineBlob,
                StateIndex = stateIndex
            };

            ref var directional2DBlob = ref directional2DState.Directional2DBlob;
            var clipCount = (byte)directional2DBlob.ClipIndexes.Length;

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
                    PreviousTime = 0,
                    Time = 0,
                    Weight = 0
                };
            }

            var animationStateIndex = AnimationState.New(ref animationStates, ref samplers, newSamplers,
                finalSpeed,
                directional2DState.StateBlob.Loop);

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

            // Update clip weights
            for (var i = startIndex; i <= endIndex; i++)
            {
                var sampler = samplers[i];
                sampler.Weight = weights[i - startIndex] * animation.Weight;
                samplers[i] = sampler;
            }

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

            // Update clip times
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
