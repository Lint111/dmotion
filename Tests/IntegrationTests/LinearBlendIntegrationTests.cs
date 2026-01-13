using System.Collections;
using System.Linq;
using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for LinearBlend state machine using real SmartBlobber-baked ACL clip data.
    ///
    /// These tests cover use cases that require real ACL-compressed animation data from SmartBlobber.
    /// </summary>
    public class LinearBlendIntegrationTests : IntegrationTestBase
    {
        private static readonly float[] Thresholds = { 0.0f, 0.5f, 0.8f };

        protected override System.Type[] SystemTypes => new[]
        {
            typeof(BlendAnimationStatesSystem),
            typeof(UpdateAnimationStatesSystem)
        };

        [UnityTest]
        public IEnumerator Update_All_Samplers_WithRealACLData()
        {
            yield return null;

            var entity = CreateLinearBlendEntity();
            var linearBlendState = AnimationStateTestUtils.CreateLinearBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, linearBlendState.AnimationStateId);

            var animationState =
                AnimationStateTestUtils.GetAnimationStateFromEntity(manager, entity, linearBlendState.AnimationStateId);
            var startSamplerIndex =
                ClipSamplerTestUtils.AnimationStateStartSamplerIdToIndex(manager, entity,
                    linearBlendState.AnimationStateId);

            var samplerIndexes = Enumerable.Range(startSamplerIndex, animationState.ClipCount).ToArray();

            // Assert everything is zero
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            foreach (var i in samplerIndexes)
            {
                var sampler = samplers[i];
                Assert.AreEqual(0, sampler.Weight);
                Assert.AreEqual(0, sampler.Time);
                Assert.AreEqual(0, sampler.PreviousTime);
            }

            UpdateWorld();

            samplers = manager.GetBuffer<ClipSampler>(entity);
            foreach (var i in samplerIndexes)
            {
                var sampler = samplers[i];
                Assert.Greater(sampler.Time, 0, $"Sampler {i} time should increase");
                Assert.AreEqual(0, sampler.PreviousTime);
            }
        }

        [UnityTest]
        public IEnumerator BlendBetweenTwoClips_WithRealACLData()
        {
            yield return null;

            var entity = CreateLinearBlendEntity();
            var linearBlendState = AnimationStateTestUtils.CreateLinearBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, linearBlendState.AnimationStateId);

            // Set our blend parameter after the first clip, but closer to the first clip
            var blendParameterValue = Thresholds[0] + (Thresholds[1] - Thresholds[0]) * 0.2f;
            AnimationStateTestUtils.SetBlendParameter(linearBlendState, manager, entity, blendParameterValue);

            UpdateWorld();

            var allSamplers =
                ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                    linearBlendState.AnimationStateId).ToArray();

            Assert.Greater(allSamplers[0].Weight, 0, "First clip should have weight");
            Assert.Greater(allSamplers[1].Weight, 0, "Second clip should have weight");
            Assert.Zero(allSamplers[2].Weight, "Third clip should have zero weight");
            // Expect first clip weight to be greater since we are closer to it
            Assert.Greater(allSamplers[0].Weight, allSamplers[1].Weight,
                "First clip should have more weight (closer to blend param)");
        }

        [UnityTest]
        public IEnumerator Keep_WeightSum_EqualOne_WithRealACLData()
        {
            yield return null;

            var entity = CreateLinearBlendEntity();
            var linearBlendState = AnimationStateTestUtils.CreateLinearBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, linearBlendState.AnimationStateId);
            AnimationStateTestUtils.SetBlendParameter(linearBlendState, manager, entity, Thresholds[0] + 0.1f);

            UpdateWorld();

            var allSamplers =
                ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                    linearBlendState.AnimationStateId);

            var sumWeight = allSamplers.Sum(s => s.Weight);
            Assert.AreEqual(1, sumWeight, 0.01f, "Sum of weights should equal 1");
        }

        [UnityTest]
        public IEnumerator LoopToClipTime_WithRealACLData()
        {
            yield return null;

            var entity = CreateLinearBlendEntity();
            var linearBlendState = AnimationStateTestUtils.CreateLinearBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, linearBlendState.AnimationStateId);

            // Run one update to establish the system's initial state
            UpdateWorld();

            var animationState =
                AnimationStateTestUtils.GetAnimationStateFromEntity(manager, entity, linearBlendState.AnimationStateId);
            var startSamplerIndex =
                ClipSamplerTestUtils.AnimationStateStartSamplerIdToIndex(manager, entity,
                    linearBlendState.AnimationStateId);

            // Get actual clip duration from real baked data (use the first sampler's clip)
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            var firstSampler = samplers[startSamplerIndex];
            var clipDuration = firstSampler.Duration;
            Assert.Greater(clipDuration, 0, "Clip duration should be positive from real ACL data");

            // Test only the first sampler (which has non-zero weight at blendRatio=0)
            {
                // Reset and set up the loop scenario
                var sampler = samplers[startSamplerIndex];
                var originalTime = sampler.Time;
                
                // Set Time just past duration to guarantee clip will loop on next frame
                sampler.Time = clipDuration + 0.01f;
                sampler.PreviousTime = clipDuration - 0.01f;
                samplers[startSamplerIndex] = sampler;

                UpdateWorld();

                var updatedSamplers = manager.GetBuffer<ClipSampler>(entity);
                var updatedSampler = updatedSamplers[startSamplerIndex];
                
                // After update, Time should have looped back to a small value
                Assert.Less(updatedSampler.Time, clipDuration, 
                    $"Time should have looped (duration={clipDuration}, was {sampler.Time}, now {updatedSampler.Time})");
            }
        }

        [UnityTest]
        public IEnumerator CleanupStates_WithRealACLData()
        {
            yield return null;

            var entity = CreateLinearBlendEntity();
            var linearBlendState = AnimationStateTestUtils.CreateLinearBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, linearBlendState.AnimationStateId);
            var anotherState = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, clipsBlob);

            var linearBlendStates = manager.GetBuffer<LinearBlendStateMachineState>(entity);
            Assert.AreEqual(1, linearBlendStates.Length);

            const float transitionDuration = 0.2f;
            AnimationStateTestUtils.RequestTransitionTo(manager, entity, anotherState.AnimationStateId,
                transitionDuration);

            UpdateWorld();

            linearBlendStates = manager.GetBuffer<LinearBlendStateMachineState>(entity);
            // We should still have both clips since we're transitioning
            Assert.AreEqual(1, linearBlendStates.Length);

            UpdateWorld(transitionDuration);

            // Should have cleanup the linear blend state
            linearBlendStates = manager.GetBuffer<LinearBlendStateMachineState>(entity);
            Assert.Zero(linearBlendStates.Length, "Linear blend state should be cleaned up after transition");
        }

        private Entity CreateLinearBlendEntity()
        {
            var stateMachineBuilder = AnimationStateMachineAssetBuilder.New();
            var linearBlendState = stateMachineBuilder.AddState<LinearBlendStateAsset>();

            linearBlendState.BlendParameter = stateMachineBuilder.AddParameter<FloatParameterAsset>("blend");
            linearBlendState.BlendClips = new[]
            {
                new ClipWithThreshold { Clip = null, Speed = 1, Threshold = Thresholds[0] },
                new ClipWithThreshold { Clip = null, Speed = 1, Threshold = Thresholds[1] },
                new ClipWithThreshold { Clip = null, Speed = 1, Threshold = Thresholds[2] }
            };

            var stateMachineAsset = stateMachineBuilder.Build();

            var stateMachineBlob =
                AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachineAsset);
            TrackBlob(stateMachineBlob);

            var entity = manager.CreateStateMachineEntity(stateMachineAsset, stateMachineBlob, clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null);

            TrackEntity(entity);

            Assert.IsTrue(manager.HasComponent<LinearBlendStateMachineState>(entity));
            Assert.IsTrue(manager.HasComponent<FloatParameter>(entity));
            return entity;
        }
    }
}
