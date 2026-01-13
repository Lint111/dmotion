using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;

namespace DMotion.Tests
{
    /// <summary>
    /// Tests for SingleClipState system behavior.
    /// DISABLED: UpdateAnimationStatesSystem accesses SkeletonClip.compressedClipDataAligned16
    /// which requires ACL-compressed data from SmartBlobber baking. BakingUtility.BakeGameObjects
    /// doesn't invoke ICustomBakingBootstrap, so SmartBlobber systems don't run in tests.
    /// </summary>
    [Ignore("Requires SmartBlobber-baked ACL clip data - not available in test context")]
    [CreateSystemsForTest(typeof(BlendAnimationStatesSystem), typeof(UpdateAnimationStatesSystem))]
    public class SingleClipStateSystemShould : ECSTestBase
    {
        private const float TestClipDuration = 1.0f;

        private BlobAssetReference<SkeletonClipSetBlob> _testClipsBlob;

        [SetUp]
        public new void Setup()
        {
            base.Setup();
            // Create test clips blob with valid duration
            _testClipsBlob = AnimationStateTestUtils.CreateTestClipsBlob(3, TestClipDuration);
        }

        [TearDown]
        public new void TearDown()
        {
            if (_testClipsBlob.IsCreated)
                _testClipsBlob.Dispose();
            base.TearDown();
        }

        [Test]
        public void UpdateSamplers()
        {
            var entity = CreateSingleClipStateEntity();
            var singleClip = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, _testClipsBlob);
            AnimationStateTestUtils.SetCurrentState(manager, entity, singleClip.AnimationStateId);

            var sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.AreEqual(0, sampler.Weight);
            Assert.AreEqual(0, sampler.Time);
            Assert.AreEqual(0, sampler.PreviousTime);

            UpdateWorld();

            sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.Greater(sampler.Weight, 0);
            Assert.Greater(sampler.Time, 0);
            Assert.AreEqual(0, sampler.PreviousTime);

            var prevTime = sampler.Time;

            UpdateWorld();

            sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.Greater(sampler.Time, prevTime);
            Assert.AreEqual(prevTime, sampler.PreviousTime);
        }

        [Test]
        public void LoopToClipTime()
        {
            var entity = CreateSingleClipStateEntity();
            var singleClip = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, _testClipsBlob,
                speed: 1, loop: true);
            AnimationStateTestUtils.SetCurrentState(manager, entity, singleClip.AnimationStateId);

            UpdateWorld();

            var sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.Greater(sampler.Weight, 0);
            Assert.Greater(sampler.Time, 0);
            Assert.AreEqual(0, sampler.PreviousTime);

            var prevTime = sampler.Time;

            // Use TestClipDuration since we know the clip duration
            UpdateWorld(TestClipDuration - prevTime * 0.5f);

            sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            // Clip time should have looped
            Assert.Less(sampler.Time, prevTime);
            Assert.AreEqual(prevTime, sampler.PreviousTime);
        }

        [Test]
        public void CleanupStates()
        {
            var entity = CreateSingleClipStateEntity();
            var s1 = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, _testClipsBlob);
            var s2 = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, _testClipsBlob);
            AnimationStateTestUtils.SetCurrentState(manager, entity, s1.AnimationStateId);

            var singleClips = manager.GetBuffer<SingleClipState>(entity);
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            Assert.AreEqual(2, singleClips.Length);
            Assert.AreEqual(2, samplers.Length);

            const float transitionDuration = 0.2f;
            AnimationStateTestUtils.RequestTransitionTo(manager, entity, s2.AnimationStateId, transitionDuration);

            UpdateWorld();

            singleClips = manager.GetBuffer<SingleClipState>(entity);
            samplers = manager.GetBuffer<ClipSampler>(entity);
            // We should still have both clips since we're transitioning
            Assert.AreEqual(2, singleClips.Length);
            Assert.AreEqual(2, samplers.Length);

            UpdateWorld(transitionDuration);

            // Should have cleanup s1 after transition
            singleClips = manager.GetBuffer<SingleClipState>(entity);
            samplers = manager.GetBuffer<ClipSampler>(entity);
            Assert.AreEqual(1, singleClips.Length);
            Assert.AreEqual(s2.AnimationStateId, singleClips[0].AnimationStateId);
            Assert.AreEqual(1, samplers.Length);
            Assert.AreEqual(1, samplers[0].Weight);
        }

        private Entity CreateSingleClipStateEntity()
        {
            var entity = AnimationStateTestUtils.CreateAnimationStateEntity(manager);
            manager.AddBuffer<SingleClipState>(entity);
            return entity;
        }
    }
}
