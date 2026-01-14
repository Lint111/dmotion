using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;

namespace DMotion.Tests
{
    /// <summary>
    /// Unit tests for PlayOneShotSystem.
    ///
    /// NOTE: PlayOneShotSystem accesses clip.duration which requires valid ACL-baked data.
    /// Tests that run the system with valid requests are marked [Ignore] because fake blobs
    /// don't have ACL data. Use PlayOneShotIntegrationTests for timing-dependent tests.
    ///
    /// Tests that verify invalid request handling work because the system exits early.
    /// </summary>
    [CreateSystemsForTest(typeof(PlayOneShotSystem))]
    public class PlayOneShotSystemShould : ECSTestBase
    {
        /// <summary>
        /// Creates an entity with all components required by PlayOneShotSystem.
        /// </summary>
        private Entity CreateOneShotEntity()
        {
            var entity = manager.CreateEntity();
            
            // Add core animation state components
            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(manager, entity);
            
            // Add SingleClipState buffer (required for one-shot clips)
            manager.AddBuffer<SingleClipState>(entity);
            
            // Add one-shot specific components
            AnimationStateMachineConversionUtils.AddOneShotSystemComponents(manager, entity);
            
            TrackEntity(entity);
            return entity;
        }

        /// <summary>
        /// Creates an entity with a pre-existing animation state to return to after one-shot.
        /// </summary>
        private Entity CreateOneShotEntityWithExistingState(out byte existingStateId)
        {
            var entity = CreateOneShotEntity();
            
            // Create an initial animation state that we'll return to
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            var singleClipState = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(
                manager, entity, clipsBlob);
            existingStateId = singleClipState.AnimationStateId;
            
            // Set this as the current state
            AnimationStateTestUtils.SetCurrentState(manager, entity, existingStateId);
            
            return entity;
        }

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data because PlayOneShotJob accesses clip.duration.
        /// Fake blobs will cause duration to be 0, making endTime calculation incorrect.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void StartOneShot_WhenValidRequest()
        {
            var entity = CreateOneShotEntityWithExistingState(out var existingStateId);
            
            // Create a different clips blob for the one-shot
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void PreserveCurrentState_WhenStartingOneShot()
        {
            var entity = CreateOneShotEntityWithExistingState(out var existingStateId);
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request);
            
            UpdateWorld();
            
            // Verify: Previous state was preserved
            var preserveState = manager.GetComponentData<AnimationPreserveState>(entity);
            Assert.IsTrue(preserveState.IsValid, "AnimationPreserveState should be valid");
            Assert.AreEqual(existingStateId, preserveState.AnimationStateId, 
                "Preserved state should match the previous current state");
        }

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void SetTransitionRequest_WhenStartingOneShot()
        {
            var entity = CreateOneShotEntityWithExistingState(out _);
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            const float transitionDuration = 0.2f;
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void ClearRequest_AfterProcessing()
        {
            var entity = CreateOneShotEntityWithExistingState(out _);
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        /// <summary>
        /// This test works with fake blobs because the system exits early when request is invalid.
        /// </summary>
        [Test]
        public void NotStartOneShot_WhenInvalidRequest()
        {
            var entity = CreateOneShotEntity();
            
            // Request is already null (invalid) from CreateOneShotEntity
            var request = manager.GetComponentData<PlayOneShotRequest>(entity);
            Assert.IsFalse(request.IsValid);
            
            UpdateWorld();
            
            // Verify: OneShotState remains invalid
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            Assert.IsFalse(oneShotState.IsValid, "OneShotState should remain invalid");
            
            // Verify: No transition was requested
            AnimationStateTestUtils.AssertNoTransitionRequest(manager, entity);
        }

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void NotOverwritePreserveState_WhenAlreadyValid()
        {
            var entity = CreateOneShotEntityWithExistingState(out var existingStateId);
            
            // Manually set a different preserve state (simulating nested one-shots)
            const sbyte originalPreserveStateId = 99;
            manager.SetComponentData(entity, new AnimationPreserveState 
            { 
                AnimationStateId = originalPreserveStateId 
            });
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void CalculateEndTime_BasedOnClipDuration()
        {
            var entity = CreateOneShotEntityWithExistingState(out _);
            
            // Create a clip with known duration (1.0 second from CreateTestClipsBlob default)
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1, clipDuration: 2.0f);
            TrackBlob(oneShotClipsBlob);
            
            const float normalizedEndTime = 0.8f; // 80% of clip duration
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                endTime: normalizedEndTime
            );
            manager.SetComponentData(entity, request);
            
            UpdateWorld();
            
            // Expected end time = normalizedEndTime * clipDuration = 0.8 * 2.0 = 1.6
            var oneShotState = manager.GetComponentData<OneShotState>(entity);
            Assert.IsTrue(oneShotState.IsValid);
            Assert.AreEqual(normalizedEndTime * 2.0f, oneShotState.EndTime, 0.001f, 
                "EndTime should be normalized time * clip duration");
        }

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void RespectSpeedParameter()
        {
            var entity = CreateOneShotEntityWithExistingState(out _);
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            const float expectedSpeed = 1.5f;
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void SetLoopFalse_ForOneShotClip()
        {
            var entity = CreateOneShotEntityWithExistingState(out _);
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        /// <summary>
        /// NOTE: Requires real ACL-baked clip data.
        /// </summary>
        [Test]
        [Ignore("Requires real ACL-baked clip data. Use PlayOneShotIntegrationTests.")]
        public void StoreBlendOutDuration_FromTransitionDuration()
        {
            var entity = CreateOneShotEntityWithExistingState(out _);
            
            var oneShotClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(oneShotClipsBlob);
            
            const float transitionDuration = 0.3f;
            var request = PlayOneShotRequest.New(
                oneShotClipsBlob,
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

        #region Request Validation Tests (Work with fake blobs)
        
        /// <summary>
        /// Tests that PlayOneShotRequest.IsValid correctly validates the request.
        /// </summary>
        [Test]
        public void RequestIsValid_WhenClipsBlobCreatedAndIndexValid()
        {
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(2);
            TrackBlob(clipsBlob);
            
            var request = PlayOneShotRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            
            Assert.IsTrue(request.IsValid, "Request should be valid with created blob and valid index");
        }

        [Test]
        public void RequestIsInvalid_WhenClipsBlobNotCreated()
        {
            var request = new PlayOneShotRequest(
                BlobAssetReference<SkeletonClipSetBlob>.Null,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            
            Assert.IsFalse(request.IsValid, "Request should be invalid when clips blob not created");
        }

        [Test]
        public void RequestIsInvalid_WhenClipIndexNegative()
        {
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            var request = new PlayOneShotRequest(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: -1
            );
            
            Assert.IsFalse(request.IsValid, "Request should be invalid with negative clip index");
        }

        [Test]
        public void NullRequest_IsInvalid()
        {
            var request = PlayOneShotRequest.Null;
            Assert.IsFalse(request.IsValid, "Null request should be invalid");
        }

        #endregion
    }
}