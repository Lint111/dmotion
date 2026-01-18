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
        internal static AnimationState NewForStateMachine(
            byte stateIndex,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ref DynamicBuffer<Directional2DBlendStateMachineState> directional2DStates,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float finalSpeed)
        {
            ref var stateBlob = ref stateMachineBlob.Value.Directional2DBlendStates[stateMachineBlob.Value.States[stateIndex].StateIndex];
            
            var startSampleIndex = samplers.Length;
            var sampleCount = stateBlob.ClipIndexes.Length;
            
            for (int i = 0; i < sampleCount; i++)
            {
                samplers.Add(new ClipSampler
                {
                    ClipIndex = (short)stateBlob.ClipIndexes[i],
                    Clips = clips,
                    ClipEventsBlob = clipEvents,
                    PreviousTime = 0,
                    Time = 0,
                    Weight = 0,
                    MixerIndex = -1
                });
            }

            var animationStateIndex = AnimationState.New(ref animationStates, ref samplers, startSampleIndex, sampleCount,
                finalSpeed,
                stateMachineBlob.Value.States[stateIndex].Loop);

            if (animationStateIndex < 0) return default;

            var state = new Directional2DBlendStateMachineState
            {
                AnimationStateId = animationStates[animationStateIndex].Id,
                StartSampleIndex = startSampleIndex,
                SampleCount = sampleCount,
                BlendParameterIndexX = stateBlob.BlendParameterIndexX,
                BlendParameterIndexY = stateBlob.BlendParameterIndexY,
                BlobIndex = stateMachineBlob.Value.States[stateIndex].StateIndex
            };
            
            directional2DStates.Add(state);
            return animationStates[animationStateIndex];
        }

        internal static void ExtractVariables(
            in Directional2DBlendStateMachineState state,
            ref StateMachineBlob stateMachineBlob,
            in DynamicBuffer<FloatParameter> floatParameters,
            out float2 input,
            out NativeArray<float2> positions,
            out NativeArray<float> speeds)
        {
            ref var blob = ref stateMachineBlob.Directional2DBlendStates[state.BlobIndex];
            
            float x = floatParameters[state.BlendParameterIndexX].Value;
            float y = floatParameters[state.BlendParameterIndexY].Value;
            input = new float2(x, y);
            
            positions = CollectionUtils.AsArray(ref blob.ClipPositions);
            speeds = CollectionUtils.AsArray(ref blob.ClipSpeeds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void UpdateSamplers(
            float dt,
            float2 input,
            in NativeArray<float2> positions,
            in NativeArray<float> speeds, // per-clip speeds
            in NativeArray<float> weights,
            in AnimationState animation,
            ref DynamicBuffer<ClipSampler> samplers)
        {
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
                loopDuration += (sampler.Clip.duration / clipSpeed) * weights[i - startIndex];
            }

            // Update clip times
            if (!mathex.iszero(loopDuration))
            {
                var invLoopDuration = 1.0f / loopDuration;
                var stateSpeed = animation.Speed; // Global state speed

                for (var i = startIndex; i <= endIndex; i++)
                {
                    var sampler = samplers[i];
                    // Effective speed to sync loop: stateSpeed * clipDuration / loopDuration
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
