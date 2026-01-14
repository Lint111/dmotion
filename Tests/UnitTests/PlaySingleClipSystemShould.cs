using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;

namespace DMotion.Tests
{
    [CreateSystemsForTest(typeof(PlaySingleClipSystem))]
    public class PlaySingleClipSystemShould : ECSTestBase
    {
        /// <summary>
        /// Creates an entity with all components required by PlaySingleClipSystem.
        /// </summary>
        private Entity CreatePlaySingleClipEntity()
        {
            var entity = manager.CreateEntity();
            
            // Add core animation state components
            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(manager, entity);
            
            // Add SingleClipState buffer (required by PlaySingleClipSystem)
            manager.AddBuffer<SingleClipState>(entity);
            
            // Add PlaySingleClipRequest component
            manager.AddComponentData(entity, PlaySingleClipRequest.Null);
            
            TrackEntity(entity);
            return entity;
        }

        [Test]
        public void CreateSingleClipState_WhenValidRequest()
        {
            var entity = CreatePlaySingleClipEntity();
            
            // Create a test clips blob
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            // Set up a valid request
            var request = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                speed: 1.0f,
                loop: true
            );
            manager.SetComponentData(entity, request);
            
            // Run the system
            UpdateWorld();
            
            // Verify: SingleClipState was created
            var singleClipStates = manager.GetBuffer<SingleClipState>(entity);
            Assert.AreEqual(1, singleClipStates.Length, "Expected one SingleClipState to be created");
            
            // Verify: AnimationState was created
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            Assert.AreEqual(1, animationStates.Length, "Expected one AnimationState to be created");
            
            // Verify: ClipSampler was created
            var clipSamplers = manager.GetBuffer<ClipSampler>(entity);
            Assert.AreEqual(1, clipSamplers.Length, "Expected one ClipSampler to be created");
        }

        [Test]
        public void SetTransitionRequest_WhenValidRequest()
        {
            var entity = CreatePlaySingleClipEntity();
            
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            const float transitionDuration = 0.25f;
            var request = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: transitionDuration,
                speed: 1.0f,
                loop: true
            );
            manager.SetComponentData(entity, request);
            
            UpdateWorld();
            
            // Verify: Transition was requested
            var transitionRequest = manager.GetComponentData<AnimationStateTransitionRequest>(entity);
            Assert.IsTrue(transitionRequest.IsValid, "Expected a valid transition request");
            Assert.AreEqual(transitionDuration, transitionRequest.TransitionDuration, 0.001f);
            
            // Verify: Transition targets the created animation state
            var singleClipStates = manager.GetBuffer<SingleClipState>(entity);
            Assert.AreEqual(transitionRequest.AnimationStateId, singleClipStates[0].AnimationStateId);
        }

        [Test]
        public void ClearRequest_AfterProcessing()
        {
            var entity = CreatePlaySingleClipEntity();
            
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            var request = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request);
            
            // Verify request is valid before update
            var requestBefore = manager.GetComponentData<PlaySingleClipRequest>(entity);
            Assert.IsTrue(requestBefore.IsValid, "Request should be valid before system runs");
            
            UpdateWorld();
            
            // Verify request is cleared after processing
            var requestAfter = manager.GetComponentData<PlaySingleClipRequest>(entity);
            Assert.IsFalse(requestAfter.IsValid, "Request should be cleared after processing");
        }

        [Test]
        public void NotCreateState_WhenInvalidRequest()
        {
            var entity = CreatePlaySingleClipEntity();
            
            // Request is already null (invalid) from CreatePlaySingleClipEntity
            var request = manager.GetComponentData<PlaySingleClipRequest>(entity);
            Assert.IsFalse(request.IsValid, "Request should be invalid (null)");
            
            UpdateWorld();
            
            // Verify: No state was created
            var singleClipStates = manager.GetBuffer<SingleClipState>(entity);
            Assert.AreEqual(0, singleClipStates.Length, "No SingleClipState should be created for invalid request");
            
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            Assert.AreEqual(0, animationStates.Length, "No AnimationState should be created for invalid request");
            
            // Verify: No transition was requested
            AnimationStateTestUtils.AssertNoTransitionRequest(manager, entity);
        }

        [Test]
        public void NotCreateState_WhenClipsBlobNotCreated()
        {
            var entity = CreatePlaySingleClipEntity();
            
            // Create request with invalid (uncreated) clips blob
            var request = new PlaySingleClipRequest(
                BlobAssetReference<SkeletonClipSetBlob>.Null, // Not created
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request);
            
            // Request should be invalid because Clips.IsCreated is false
            Assert.IsFalse(request.IsValid, "Request with uncreated clips blob should be invalid");
            
            UpdateWorld();
            
            // Verify: No state was created
            var singleClipStates = manager.GetBuffer<SingleClipState>(entity);
            Assert.AreEqual(0, singleClipStates.Length);
        }

        [Test]
        public void RespectSpeedParameter()
        {
            var entity = CreatePlaySingleClipEntity();
            
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            const float expectedSpeed = 2.5f;
            var request = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                speed: expectedSpeed,
                loop: true
            );
            manager.SetComponentData(entity, request);
            
            UpdateWorld();
            
            // Verify: AnimationState has correct speed
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            Assert.AreEqual(1, animationStates.Length);
            Assert.AreEqual(expectedSpeed, animationStates[0].Speed, 0.001f);
        }

        [Test]
        public void RespectLoopParameter()
        {
            var entity = CreatePlaySingleClipEntity();
            
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(1);
            TrackBlob(clipsBlob);
            
            // Test with loop = false
            var request = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0,
                transitionDuration: 0.15f,
                speed: 1.0f,
                loop: false
            );
            manager.SetComponentData(entity, request);
            
            UpdateWorld();
            
            // Verify: AnimationState has correct loop setting
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            Assert.AreEqual(1, animationStates.Length);
            Assert.IsFalse(animationStates[0].Loop, "AnimationState should not loop");
        }

        [Test]
        public void CreateMultipleStates_WhenMultipleRequestsProcessed()
        {
            var entity = CreatePlaySingleClipEntity();
            
            var clipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(2);
            TrackBlob(clipsBlob);
            
            // First request
            var request1 = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 0
            );
            manager.SetComponentData(entity, request1);
            UpdateWorld();
            
            // Second request
            var request2 = PlaySingleClipRequest.New(
                clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null,
                clipIndex: 1
            );
            manager.SetComponentData(entity, request2);
            UpdateWorld();
            
            // Verify: Two SingleClipStates exist
            var singleClipStates = manager.GetBuffer<SingleClipState>(entity);
            Assert.AreEqual(2, singleClipStates.Length, "Expected two SingleClipStates after two requests");
            
            // Verify: Two AnimationStates exist
            var animationStates = manager.GetBuffer<AnimationState>(entity);
            Assert.AreEqual(2, animationStates.Length, "Expected two AnimationStates after two requests");
        }
    }
}