using System.Collections;
using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for PlayOneShotSystem using real SmartBlobber-baked ACL clip data.
    ///
    /// These tests cover use cases that require real ACL-compressed animation data,
    /// specifically the clip.duration access in PlayOneShotJob.
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Open TestAnimationScene.unity to trigger baking
    /// </summary>
    public class PlayOneShotIntegrationTests : IntegrationTestBase
    {
        protected override System.Type[] SystemTypes => new[]
        {
            typeof(PlayOneShotSystem),
            typeof(BlendAnimationStatesSystem),
            typeof(UpdateAnimationStatesSystem)
        };

        /// <summary>
        /// Creates an entity with all components required for one-shot playback.
        /// </summary>
        private Entity CreateOneShotEntity()
        {
            var entity = AnimationStateTestUtils.CreateAnimationStateEntity(manager);
            manager.AddBuffer<SingleClipState>(entity);
            AnimationStateMachineConversionUtils.AddOneShotSystemComponents(manager, entity);
            TrackEntity(entity);
            return entity;
        }

        /// <summary>
        /// Creates an entity with an existing animation state that can be preserved during one-shot.
        /// </summary>
        private Entity CreateOneShotEntityWithExistingState(out byte existingStateId)
        {
            var entity = CreateOneShotEntity();

            // Create an initial animation state using real baked clips
            var singleClipState = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(
                manager, entity, clipsBlob, speed: 1, loop: true);
            existingStateId = singleClipState.AnimationStateId;

            // Set this as the current state with full weight
            AnimationStateTestUtils.SetCurrentState(manager, entity, existingStateId);

            return entity;
        }

        [UnityTest]
        public IEnumerator StartOneShot_WhenValidRequest()
        {
            yield return null; // Wait a frame for systems

            var entity = CreateOneShotEntityWithExistingState(out var existingStateId);

            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                endTime: 0.8f,
                speed: 1.0f
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // Verify: OneShotState was set
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            Assert.IsTrue(oneShotState.IsValid, "OneShotState should be valid after request");

            // Verify: SingleClipState was created (one-shot is a single clip)
            var singleClipStates = manager.GetBuffer<SingleClipState>(entity);
            Assert.AreEqual(2, singleClipStates.Length, "Expected two SingleClipStates (existing + one-shot)");
        }

        [UnityTest]
        public IEnumerator PreserveCurrentState_WhenStartingOneShot()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out var existingStateId);

            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // Verify: Previous state was preserved
            var preserveState = manager.GetComponentData<AnimationPreserveState>(entity);
            Assert.IsTrue(preserveState.IsValid, "AnimationPreserveState should be valid");
            Assert.AreEqual(existingStateId, preserveState.AnimationStateId,
                "Preserved state should match the previous current state");
        }

        [UnityTest]
        public IEnumerator SetTransitionRequest_WhenStartingOneShot()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            const float transitionDuration = 0.2f;
            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: transitionDuration
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // Verify: Transition was requested to the one-shot state
            var transitionRequest = manager.GetComponentData<AnimationStateTransitionRequest>(entity);
            Assert.IsTrue(transitionRequest.IsValid, "Transition should be requested");
            Assert.AreEqual(transitionDuration, transitionRequest.TransitionDuration, 0.001f);

            // Verify: Transition targets the one-shot animation state
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            Assert.AreEqual(transitionRequest.AnimationStateId, oneShotState.AnimationStateId);
        }

        [UnityTest]
        public IEnumerator ClearRequest_AfterProcessing()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request);

            // Verify request is valid before update
            Assert.IsTrue(manager.GetComponentData<PlayOneShotRequest>(entity).IsValid);

            UpdateWorld();

            // Verify request is cleared
            Assert.IsFalse(manager.GetComponentData<PlayOneShotRequest>(entity).IsValid,
                "Request should be cleared after processing");
        }

        [UnityTest]
        public IEnumerator CalculateEndTime_BasedOnClipDuration()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            // Get the actual clip duration from baked data
            var clipDuration = GetClipDuration(0);
            Assert.Greater(clipDuration, 0, "Clip duration should be positive from real ACL data");

            const float normalizedEndTime = 0.8f; // 80% of clip duration
            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                endTime: normalizedEndTime
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // Expected end time = normalizedEndTime * clipDuration
            var expectedEndTime = normalizedEndTime * clipDuration;
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            Assert.IsTrue(oneShotState.IsValid);
            Assert.AreEqual(expectedEndTime, oneShotState.EndTime, 0.001f,
                $"EndTime should be {normalizedEndTime} * {clipDuration} = {expectedEndTime}");
        }

        [UnityTest]
        public IEnumerator RespectSpeedParameter()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            const float expectedSpeed = 1.5f;
            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                endTime: 0.8f,
                speed: expectedSpeed
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // Find the one-shot animation state and verify speed
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var oneShotAnimState = animationStates.GetWithId((byte)oneShotState.AnimationStateId);

            Assert.AreEqual(expectedSpeed, oneShotAnimState.Speed, 0.001f);
        }

        [UnityTest]
        public IEnumerator SetLoopFalse_ForOneShotClip()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // One-shot clips should never loop
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            var oneShotAnimState = animationStates.GetWithId((byte)oneShotState.AnimationStateId);

            Assert.IsFalse(oneShotAnimState.Loop, "One-shot animation should not loop");
        }

        [UnityTest]
        public IEnumerator StoreBlendOutDuration_FromTransitionDuration()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            const float transitionDuration = 0.3f;
            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: transitionDuration
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            Assert.AreEqual(transitionDuration, oneShotState.BlendOutDuration, 0.001f,
                "BlendOutDuration should match the transition duration");
        }

        [UnityTest]
        public IEnumerator NotOverwritePreserveState_WhenAlreadyValid()
        {
            yield return null;

            var entity = CreateOneShotEntityWithExistingState(out _);

            // Manually set a different preserve state (simulating nested one-shots)
            const sbyte originalPreserveStateId = 99;
            manager.SetComponentData(entity, new AnimationPreserveState
            {
                AnimationStateId = originalPreserveStateId
            });

            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request);

            UpdateWorld();

            // Verify: Original preserve state is NOT overwritten
            var preserveState = manager.GetComponentData<AnimationPreserveState>(entity);
            Assert.AreEqual(originalPreserveStateId, preserveState.AnimationStateId,
                "Preserve state should not be overwritten when already valid");
        }
    }
}
