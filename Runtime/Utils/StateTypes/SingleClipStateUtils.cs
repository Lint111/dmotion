using Unity.Mathematics;
using Latios.Kinemation;
using Unity.Entities;

namespace DMotion
{
    public static class SingleClipStateUtils
    {
        internal static SingleClipState NewForStateMachine(
            short stateIndex,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ref DynamicBuffer<SingleClipState> singleClips,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float finalSpeed,
            float normalizedOffset = 0f,
            byte layerIndex = 0)
        {
            ref var state = ref stateMachineBlob.Value.States[stateIndex];
            var singleClipState = stateMachineBlob.Value.SingleClipStates[state.StateIndex];
            return New(singleClipState.ClipIndex, finalSpeed, state.Loop,
                clips,
                clipEvents,
                ref singleClips,
                ref animationStates,
                ref samplers,
                normalizedOffset,
                layerIndex);
        }

        internal static SingleClipState New(
            ushort clipIndex,
            float speed,
            bool loop,
            BlobAssetReference<SkeletonClipSetBlob> clips,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            ref DynamicBuffer<SingleClipState> singleClips,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            float normalizedOffset = 0f,
            byte layerIndex = 0)
        {
            ref var clip = ref clips.Value.clips[clipIndex];
            
            // Sanitize offset based on loop mode
            if (loop)
            {
                normalizedOffset = normalizedOffset - math.floor(normalizedOffset);
            }
            else
            {
                normalizedOffset = math.clamp(normalizedOffset, 0f, 1f);
            }

            var initialTime = normalizedOffset * clip.duration;

            var newSampler = new ClipSampler
            {
                ClipIndex = clipIndex,
                Clips = clips,
                ClipEventsBlob = clipEvents,
                PreviousTime = initialTime,
                Time = initialTime,
                Weight = 0,
                LayerIndex = layerIndex
            };

            var animationStateIndex = AnimationState.New(ref animationStates, ref samplers, newSampler, speed, loop, initialTime, layerIndex);
            if (animationStateIndex < 0)
            {
                // Failed to allocate - return invalid state (caller should check IsValid)
                return default;
            }

            var singleClipState = new SingleClipState
            {
                AnimationStateId = animationStates[animationStateIndex].Id
            };
            singleClips.Add(singleClipState);
            return singleClipState;
        }

        /// <summary>
        /// Sets sampler weight without advancing time.
        /// Used by both normal playback and preview systems.
        /// </summary>
        internal static void SetSamplerWeight(
            float stateWeight,
            ref DynamicBuffer<ClipSampler> samplers,
            int samplerIndex)
        {
            var sampler = samplers[samplerIndex];
            sampler.Weight = stateWeight;
            samplers[samplerIndex] = sampler;
        }
        
        /// <summary>
        /// Sets sampler time to a specific normalized time (0-1).
        /// Used by preview systems for explicit time control.
        /// </summary>
        internal static void SetSamplerTime(
            float normalizedTime,
            ref DynamicBuffer<ClipSampler> samplers,
            int samplerIndex)
        {
            var sampler = samplers[samplerIndex];
            float clipDuration = sampler.Duration > 0 ? sampler.Duration : 1f;
            sampler.PreviousTime = sampler.Time;
            sampler.Time = normalizedTime * clipDuration;
            samplers[samplerIndex] = sampler;
        }

        internal static void UpdateSamplers(SingleClipState singleClipState, float dt,
            in AnimationState animation,
            ref DynamicBuffer<ClipSampler> samplers)
        {
            var samplerIndex = samplers.IdToIndex(animation.StartSamplerId);
            
            // Set weight using shared utility
            SetSamplerWeight(animation.Weight, ref samplers, samplerIndex);

            // Advance time
            var sampler = samplers[samplerIndex];
            sampler.PreviousTime = sampler.Time;
            sampler.Time += dt * animation.Speed;
            if (animation.Loop)
            {
                sampler.LoopToClipTime();
            }
            samplers[samplerIndex] = sampler;
        }
    }
}