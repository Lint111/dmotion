using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace DMotion.Tests
{
    public static class AnimationStateTestUtils
    {
        public static void AssertNoTransitionRequest(EntityManager manager, Entity entity)
        {
            var animationStateTransitionRequest = manager.GetComponentData<AnimationStateTransitionRequest>(entity);
            Assert.IsFalse(animationStateTransitionRequest.IsValid,
                $"Expected invalid transition request, but requested is to {animationStateTransitionRequest.AnimationStateId}");
        }

        public static void AssertCurrentStateInvalid(EntityManager manager, Entity entity)
        {
            var currentAnimationState = manager.GetComponentData<AnimationCurrentState>(entity);
            Assert.IsFalse(currentAnimationState.IsValid, "Expected Animation state not to be valid");
        }

        public static void AssertCurrentState(EntityManager manager, Entity entity, byte id, bool assertWeight = true)
        {
            var currentAnimationState = manager.GetComponentData<AnimationCurrentState>(entity);
            Assert.IsTrue(currentAnimationState.IsValid, "Expected AnimationCurrentState to be valid");
            Assert.AreEqual(id, currentAnimationState.AnimationStateId);
            if (assertWeight)
            {
                var animationState = GetAnimationStateFromEntity(manager, entity, id);
                Assert.AreEqual(1, animationState.Weight);
            }
        }

        public static void AssertNoOnGoingTransition(EntityManager manager, Entity entity)
        {
            AssertNoTransitionRequest(manager, entity);
            var animationStateTransition = manager.GetComponentData<AnimationStateTransition>(entity);
            Assert.IsFalse(animationStateTransition.IsValid,
                $"Expected invalid transition, but transitioning to {animationStateTransition.AnimationStateId}");
        }

        public static void AssertTransitionRequested(EntityManager manager, Entity entity,
            byte expectedAnimationStateId)
        {
            var animationStateTransitionRequest = manager.GetComponentData<AnimationStateTransitionRequest>(entity);
            Assert.IsTrue(animationStateTransitionRequest.IsValid);
            Assert.AreEqual(animationStateTransitionRequest.AnimationStateId, expectedAnimationStateId);
        }

        public static void AssertOnGoingTransition(EntityManager manager, Entity entity, byte expectedAnimationStateId)
        {
            var animationStateTransitionRequest = manager.GetComponentData<AnimationStateTransitionRequest>(entity);
            Assert.IsFalse(animationStateTransitionRequest.IsValid);

            var animationStateTransition = manager.GetComponentData<AnimationStateTransition>(entity);
            Assert.IsTrue(animationStateTransition.IsValid, "Expect current transition to be active");
            Assert.AreEqual(expectedAnimationStateId, animationStateTransition.AnimationStateId,
                $"Current transition ({animationStateTransition.AnimationStateId}) different from expected it {expectedAnimationStateId}");
        }

        internal static Entity CreateAnimationStateEntity(EntityManager manager)
        {
            var newEntity = manager.CreateEntity(
                typeof(AnimationState),
                typeof(ClipSampler));

            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(manager, newEntity);
            return newEntity;
        }

        internal static void SetAnimationState(EntityManager manager, Entity entity, AnimationState animation)
        {
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var index = animationStates.IdToIndex(animation.Id);
            Assert.GreaterOrEqual(index, 0);
            animationStates[index] = animation;
        }

        internal static void SetInvalidCurrentState(EntityManager manager, Entity entity)
        {
            manager.SetComponentData(entity, AnimationCurrentState.Null);
        }

        internal static void SetCurrentState(EntityManager manager, Entity entity, byte animationStateId)
        {
            manager.SetComponentData(entity, new AnimationCurrentState { AnimationStateId = (sbyte)animationStateId });
            var animationState = GetAnimationStateFromEntity(manager, entity, animationStateId);
            animationState.Weight = 1;
            SetAnimationState(manager, entity, animationState);
        }

        internal static void RequestTransitionTo(EntityManager manager, Entity entity, byte animationStateId,
            float transitionDuration = 0.1f)
        {
            manager.SetComponentData(entity, new AnimationStateTransitionRequest
            {
                AnimationStateId = (sbyte)animationStateId,
                TransitionDuration = transitionDuration
            });
        }

        internal static void SetAnimationStateTransition(EntityManager manager, Entity entity, byte animationStateId,
            float transitionDuration = 0.1f)
        {
            manager.SetComponentData(entity, AnimationStateTransitionRequest.Null);
            manager.SetComponentData(entity, new AnimationStateTransition
            {
                AnimationStateId = (sbyte)animationStateId,
                TransitionDuration = transitionDuration
            });
        }

        internal static AnimationState GetAnimationStateFromEntity(EntityManager manager, Entity entity,
            byte animationStateId)
        {
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            return animationStates.GetWithId(animationStateId);
        }

        internal static AnimationState NewAnimationStateFromEntity(EntityManager manager, Entity entity,
            ClipSampler newSampler,
            float speed = 1, bool loop = true)
        {
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            var animationStateIndex = AnimationState.New(ref animationStates, ref samplers, newSampler, speed, loop);
            Assert.GreaterOrEqual(animationStateIndex, 0);
            Assert.IsTrue(animationStates.ExistsWithId(animationStates[animationStateIndex].Id));
            return animationStates[animationStateIndex];
        }

        internal static AnimationState NewAnimationStateFromEntity(EntityManager manager, Entity entity,
            NativeArray<ClipSampler> newSamplers,
            float speed = 1, bool loop = true)
        {
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            var animationStateIndex = AnimationState.New(ref animationStates, ref samplers, newSamplers, speed, loop);
            Assert.GreaterOrEqual(0, animationStateIndex);
            Assert.IsTrue(animationStates.ExistsWithId(animationStates[animationStateIndex].Id));
            return animationStates[animationStateIndex];
        }

        internal static void SetBlendParameter(in LinearBlendStateMachineState linearBlendState, EntityManager manager,
            Entity entity, float value)
        {
            var blendParams = manager.GetBuffer<FloatParameter>(entity);
            ref var blob = ref linearBlendState.LinearBlendBlob;
            var blendRatio = blendParams[blob.BlendParameterIndex];
            blendRatio.Value = value;
            blendParams[blob.BlendParameterIndex] = blendRatio;
        }

        internal static void FindActiveSamplerIndexesForLinearBlend(
            in LinearBlendStateMachineState linearBlendState,
            EntityManager manager, Entity entity,
            out int firstClipIndex, out int secondClipIndex)
        {
            var floatParams = manager.GetBuffer<FloatParameter>(entity);
            var intParams = manager.GetBuffer<IntParameter>(entity);
            LinearBlendStateUtils.ExtractLinearBlendVariablesFromStateMachine(
                linearBlendState, floatParams, intParams,
                out var blendRatio, out var thresholds, out _);
            LinearBlendStateUtils.FindActiveClipIndexes(blendRatio, thresholds, out firstClipIndex,
                out secondClipIndex);
            var startIndex =
                ClipSamplerTestUtils.AnimationStateStartSamplerIdToIndex(manager, entity,
                    linearBlendState.AnimationStateId);
            firstClipIndex += startIndex;
            secondClipIndex += startIndex;
        }

        internal static LinearBlendStateMachineState CreateLinearBlendForStateMachine(short stateIndex,
            EntityManager manager, Entity entity)
        {
            Assert.GreaterOrEqual(stateIndex, 0);
            var stateMachine = manager.GetComponentData<AnimationStateMachine>(entity);
            Assert.IsTrue(stateIndex < stateMachine.StateMachineBlob.Value.States.Length);
            Assert.AreEqual(StateType.LinearBlend, stateMachine.StateMachineBlob.Value.States[stateIndex].Type);
            var linearBlend = manager.GetBuffer<LinearBlendStateMachineState>(entity);
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            return LinearBlendStateUtils.NewForStateMachine(stateIndex,
                stateMachine.StateMachineBlob,
                stateMachine.ClipsBlob,
                stateMachine.ClipEventsBlob,
                ref linearBlend,
                ref animationStates,
                ref samplers,
                1.0f // default speed
            );
        }

        internal static SingleClipState CreateSingleClipState(EntityManager manager, Entity entity,
            BlobAssetReference<ClipEventsBlob> clipEvents,
            float speed = 1.0f,
            bool loop = false,
            ushort clipIndex = 0)
        {
            var singleClips = manager.GetBuffer<SingleClipState>(entity);
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var samplers = manager.GetBuffer<ClipSampler>(entity);

            var clipsBlob = CreateFakeSkeletonClipSetBlob(1);

            return SingleClipStateUtils.New(
                clipIndex, speed, loop,
                clipsBlob,
                clipEvents,
                ref singleClips,
                ref animationStates,
                ref samplers
            );
        }

        /// <summary>
        /// Creates a SingleClipState using a real baked clips blob.
        /// Use this for tests that need to access clip data (duration, sampling).
        /// </summary>
        internal static SingleClipState CreateSingleClipStateWithRealClips(EntityManager manager, Entity entity,
            BlobAssetReference<SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEvents = default,
            float speed = 1.0f,
            bool loop = false,
            ushort clipIndex = 0)
        {
            var singleClips = manager.GetBuffer<SingleClipState>(entity);
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var samplers = manager.GetBuffer<ClipSampler>(entity);

            return SingleClipStateUtils.New(
                clipIndex, speed, loop,
                clipsBlob,
                clipEvents,
                ref singleClips,
                ref animationStates,
                ref samplers
            );
        }

        internal static SingleClipState CreateSingleClipState(EntityManager manager, Entity entity,
            float speed = 1.0f,
            bool loop = false,
            ushort clipIndex = 0)
        {
            return CreateSingleClipState(manager, entity, BlobAssetReference<ClipEventsBlob>.Null, speed, loop,
                clipIndex);
        }

        /// <summary>
        /// Extracts the SkeletonClipSetBlob from a baked entity that has AnimationStateMachine component.
        /// Useful for tests that need real baked clip data.
        /// </summary>
        internal static BlobAssetReference<SkeletonClipSetBlob> GetClipsBlobFromBakedEntity(EntityManager manager, Entity bakedEntity)
        {
            Assert.IsTrue(manager.HasComponent<AnimationStateMachine>(bakedEntity),
                "Entity must have AnimationStateMachine component (from baked prefab)");
            var stateMachine = manager.GetComponentData<AnimationStateMachine>(bakedEntity);
            return stateMachine.ClipsBlob;
        }

        /// <summary>
        /// Creates a fake SkeletonClipSetBlob for testing purposes.
        ///
        /// WARNING: These fake clips do NOT contain valid ACL compressed data!
        /// Kinemation 0.14+ uses ACL compression which requires properly baked clip data.
        /// Any operation that accesses clip duration or samples the clip will fail with:
        /// "ACL alignment error (compressedClip not aligned to 16 byte boundary)"
        ///
        /// Use cases that work:
        /// - Testing state machine logic that doesn't access clip data
        /// - Testing buffer operations (add/remove samplers, states)
        /// - Testing transition logic
        ///
        /// Use cases that DON'T work:
        /// - Tests that use UpdateAnimationStatesSystem (calls LoopToClipTime)
        /// - Tests that access sampler.Clip.duration
        /// - Actual animation sampling/blending
        ///
        /// For tests requiring real clip data, use ConvertGameObjectPrefab with
        /// properly baked test prefabs (like performance tests do).
        /// </summary>
        internal static BlobAssetReference<SkeletonClipSetBlob> CreateFakeSkeletonClipSetBlob(int clipCount)
        {
            Assert.Greater(clipCount, 0);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SkeletonClipSetBlob>();
            root.boneCount = 1;
            var blobClips = builder.Allocate(ref root.clips, clipCount);
            for (int i = 0; i < clipCount; i++)
            {
                // SkeletonClip properties are read-only in newer Kinemation
                // Use unsafe pointer cast to write directly to blob memory
                unsafe
                {
                    var clipPtr = (SkeletonClip*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref blobClips[i]);
                    *clipPtr = default;
                }
                // Note: Fake clips with default values - actual clip data requires proper Kinemation baking
            }

            return builder.CreateBlobAssetReference<SkeletonClipSetBlob>(Allocator.Temp);
        }

        /// <summary>
        /// Creates a SkeletonClipSetBlob with valid duration/sampleRate values using TestResources.
        /// Unlike CreateFakeSkeletonClipSetBlob, this creates clips with proper timing metadata
        /// that can be used for tests that access clip.duration.
        ///
        /// Note: The clips still don't have valid ACL animation data, so actual sampling won't work.
        /// But timing-based operations (looping, duration checks) will work correctly.
        /// </summary>
        /// <param name="clipCount">Number of clips to create</param>
        /// <param name="clipDuration">Duration for each clip (default 1.0f)</param>
        /// <returns>Blob asset reference - caller must dispose</returns>
        internal static BlobAssetReference<SkeletonClipSetBlob> CreateTestClipsBlob(int clipCount = 1, float clipDuration = 1.0f)
        {
            var resources = TestResources.Instance;
            if (resources != null)
            {
                return resources.CreateTestClipsBlob(clipCount);
            }

            // Fallback: use static method that doesn't require asset instance
            return TestResources.CreateTestClipsBlobStatic(clipCount, clipDuration);
        }

        /// <summary>
        /// Creates a SkeletonClipSetBlob with specified durations for each clip.
        /// </summary>
        internal static BlobAssetReference<SkeletonClipSetBlob> CreateTestClipsBlobWithDurations(float[] durations)
        {
            var resources = TestResources.Instance;
            if (resources != null)
            {
                return resources.CreateTestClipsBlob(durations);
            }

            // Fallback: use static method with first duration value (or 1.0f if empty)
            var duration = durations.Length > 0 ? durations[0] : 1.0f;
            return TestResources.CreateTestClipsBlobStatic(durations.Length, duration);
        }
    }
}