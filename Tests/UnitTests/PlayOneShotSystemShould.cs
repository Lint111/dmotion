using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;

namespace DMotion.Tests
{
    /// <summary>
    /// Unit tests for PlayOneShotSystem request validation.
    ///
    /// NOTE: PlayOneShotSystem accesses clip.duration which requires valid ACL-baked data.
    /// System behavior tests are in PlayOneShotIntegrationTests which uses real baked clips.
    /// This file only contains tests that work with fake blobs (request validation, invalid requests).
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
