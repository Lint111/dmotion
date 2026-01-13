using System.Collections;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for SingleClipState system using real SmartBlobber-baked ACL clip data.
    ///
    /// These tests cover use cases that require real ACL-compressed animation data from SmartBlobber.
    /// The integration tests use pre-baked entities from the TestAnimationScene.
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Open TestAnimationScene.unity to trigger baking
    /// </summary>
    public class SingleClipStateIntegrationTests : IntegrationTestBase
    {
        protected override System.Type[] SystemTypes => new[]
        {
            typeof(BlendAnimationStatesSystem),
            typeof(UpdateAnimationStatesSystem)
        };

        [UnityTest]
        public IEnumerator UpdateSamplers_WithRealACLData()
        {
            yield return null; // Wait a frame for systems

            var entity = CreateSingleClipStateEntity();
            var singleClip = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, clipsBlob);
            AnimationStateTestUtils.SetCurrentState(manager, entity, singleClip.AnimationStateId);

            var sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.AreEqual(0, sampler.Weight);
            Assert.AreEqual(0, sampler.Time);
            Assert.AreEqual(0, sampler.PreviousTime);

            UpdateWorld();

            sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.Greater(sampler.Weight, 0, "Weight should increase after update");
            Assert.Greater(sampler.Time, 0, "Time should increase after update");
            Assert.AreEqual(0, sampler.PreviousTime);

            var prevTime = sampler.Time;

            UpdateWorld();

            sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.Greater(sampler.Time, prevTime, "Time should continue increasing");
            Assert.AreEqual(prevTime, sampler.PreviousTime, "PreviousTime should be set");
        }

        [UnityTest]
        public IEnumerator LoopToClipTime_WithRealACLData()
        {
            yield return null;

            var entity = CreateSingleClipStateEntity();
            var singleClip = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, clipsBlob,
                speed: 1, loop: true);
            AnimationStateTestUtils.SetCurrentState(manager, entity, singleClip.AnimationStateId);

            UpdateWorld();

            var sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            Assert.Greater(sampler.Weight, 0);
            Assert.Greater(sampler.Time, 0);
            Assert.AreEqual(0, sampler.PreviousTime);

            var prevTime = sampler.Time;

            // Get actual clip duration from the real baked data
            var clipDuration = GetClipDuration(0);
            Assert.Greater(clipDuration, 0, "Clip duration should be positive from real ACL data");

            // Update past the clip duration to trigger looping
            UpdateWorld(clipDuration - prevTime * 0.5f);

            sampler = ClipSamplerTestUtils.GetFirstSamplerForAnimationState(manager, entity, singleClip.AnimationStateId);
            // Clip time should have looped
            Assert.Less(sampler.Time, prevTime, "Time should loop back to start of clip");
            Assert.AreEqual(prevTime, sampler.PreviousTime, "PreviousTime should be preserved");
        }

        [UnityTest]
        public IEnumerator CleanupStates_WithRealACLData()
        {
            yield return null;

            var entity = CreateSingleClipStateEntity();
            var s1 = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, clipsBlob);
            var s2 = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, clipsBlob);
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
            Assert.AreEqual(1, singleClips.Length, "Old state should be cleaned up");
            Assert.AreEqual(s2.AnimationStateId, singleClips[0].AnimationStateId);
            Assert.AreEqual(1, samplers.Length);
            Assert.AreEqual(1, samplers[0].Weight, 0.01f, "Weight should be fully transitioned");
        }

        private Entity CreateSingleClipStateEntity()
        {
            var entity = AnimationStateTestUtils.CreateAnimationStateEntity(manager);
            manager.AddBuffer<SingleClipState>(entity);
            TrackEntity(entity);
            return entity;
        }
    }
}
