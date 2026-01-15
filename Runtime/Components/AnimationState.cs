using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Assertions;

namespace DMotion
{
    internal interface IElementWithId
    {
        byte Id { get; set; }
    }

    public struct AnimationCurrentState : IComponentData
    {
        internal sbyte AnimationStateId;
        internal bool IsValid => AnimationStateId >= 0;
        internal static AnimationCurrentState Null => new () { AnimationStateId = -1 };
        internal static AnimationCurrentState New(sbyte animationStateId)
        {
            return new AnimationCurrentState
            {
                AnimationStateId = animationStateId
            };
        }
    }
    
    internal struct AnimationStateTransition : IComponentData
    {
        internal sbyte AnimationStateId;
        internal float TransitionDuration;
        internal static AnimationStateTransition Null => new () { AnimationStateId = -1 };
        internal bool IsValid => AnimationStateId >= 0;

        internal readonly bool HasEnded(in AnimationState animationState)
        {
            Assert.AreEqual(animationState.Id, AnimationStateId);
            return animationState.Time > TransitionDuration;
        }
    }

    public struct AnimationStateTransitionRequest : IComponentData
    {
        internal sbyte AnimationStateId;
        internal float TransitionDuration;
        internal bool IsValid => AnimationStateId >= 0;

        internal static AnimationStateTransitionRequest Null => new AnimationStateTransitionRequest() { AnimationStateId = -1 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static AnimationStateTransitionRequest New(byte animationStateId, float transitionDuration)
        {
            return new AnimationStateTransitionRequest
            {
                AnimationStateId = (sbyte)animationStateId,
                TransitionDuration = transitionDuration
            };
        }
    }
    
    internal struct AnimationPreserveState : IComponentData
    {
        internal sbyte AnimationStateId;
        internal static AnimationPreserveState Null => new () { AnimationStateId = -1 };
        internal bool IsValid => AnimationStateId >= 0;
    }

    [BurstCompile]
    public struct AnimationState : IBufferElementData, IElementWithId
    {
        public byte Id { get; set; }
        internal float Time;
        internal float Weight;
        internal float Speed;
        internal bool Loop;
        internal byte StartSamplerId;
        internal byte ClipCount;

        internal static int New(
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            ClipSampler singleClipSampler,
            float speed, bool loop)
        {
            var newSamplers = new NativeArray<ClipSampler>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            newSamplers[0] = singleClipSampler;
            return AnimationState.New(ref animationStates, ref samplers, newSamplers, speed, loop);
        }

        internal static int New(
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            NativeArray<ClipSampler> newSamplers,
            float speed, bool loop)
        {
            Assert.IsTrue(newSamplers.IsCreated, "Trying to create animationState with Null sampler array");
            Assert.IsTrue(newSamplers.Length > 0, "Trying to create animationState with no samplers");

            var clipCount = (byte)newSamplers.Length;

            // First, check if we can allocate contiguous sampler IDs
            // Do this BEFORE creating the AnimationState to avoid inconsistent state
            if (!samplers.TryFindIdAndInsertIndex(clipCount, out var samplerId, out var insertIndex))
            {
                // ID space too fragmented - cannot allocate contiguous sampler IDs
                Assert.IsTrue(false, "Failed to allocate contiguous sampler IDs - ID space too fragmented");
                return -1;
            }

            // Now we know samplers can be allocated, create the AnimationState
            animationStates.AddWithId(new AnimationState
            {
                Speed = speed,
                Loop = loop,
                ClipCount = clipCount,
                StartSamplerId = samplerId
            }, out _, out var animationStateIndex);

            // Add samplers to buffer - must handle insertion in middle properly
            var oldLength = samplers.Length;
            samplers.Length += clipCount;

            // If inserting in the middle (not appending), shift existing elements first
            if (insertIndex < oldLength)
            {
                // Shift elements from insertIndex..oldLength to insertIndex+clipCount..newLength
                // Must iterate backwards to avoid overwriting
                for (var i = oldLength - 1; i >= insertIndex; i--)
                {
                    samplers[i + clipCount] = samplers[i];
                }
            }

            // Now write new samplers at the insertion point
            for (var i = 0; i < clipCount; i++)
            {
                var sampler = newSamplers[i];
                sampler.Id = (byte)(samplerId + i);
                sampler.PreviousTime = 0;
                sampler.Time = 0;
                sampler.Weight = 0;

                samplers[insertIndex + i] = sampler;
            }

            return animationStateIndex;
        }
    }
}