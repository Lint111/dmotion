using System.Collections;
using System.Linq;
using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for Directional2DBlend state machine using real SmartBlobber-baked ACL clip data.
    ///
    /// These tests cover use cases that require real ACL-compressed animation data from SmartBlobber.
    /// Tests the Simple Directional 2D blending algorithm with actual ECS systems.
    /// </summary>
    public class Directional2DBlendIntegrationTests : IntegrationTestBase
    {
        // Standard 4-direction setup (cardinal directions)
        private static readonly float2[] CardinalPositions = 
        {
            new float2(1, 0),   // East
            new float2(0, 1),   // North
            new float2(-1, 0),  // West
            new float2(0, -1)   // South
        };

        // 5-direction setup with idle at origin
        private static readonly float2[] CardinalWithIdlePositions = 
        {
            new float2(0, 0),   // Idle
            new float2(1, 0),   // East
            new float2(0, 1),   // North
            new float2(-1, 0),  // West
            new float2(0, -1)   // South
        };

        protected override System.Type[] SystemTypes => new[]
        {
            typeof(BlendAnimationStatesSystem),
            typeof(UpdateAnimationStatesSystem)
        };

        [UnityTest]
        public IEnumerator Update_All_Samplers_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalPositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);

            var animationState =
                AnimationStateTestUtils.GetAnimationStateFromEntity(manager, entity, directional2DState.AnimationStateId);
            var startSamplerIndex =
                ClipSamplerTestUtils.AnimationStateStartSamplerIdToIndex(manager, entity,
                    directional2DState.AnimationStateId);

            var samplerIndexes = Enumerable.Range(startSamplerIndex, animationState.ClipCount).ToArray();

            // Assert everything is zero initially
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
        public IEnumerator BlendToEast_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalPositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);

            // Set blend parameters to point East
            AnimationStateTestUtils.SetBlendParameter2D(directional2DState, manager, entity, 1f, 0f);

            UpdateWorld();

            var allSamplers =
                ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                    directional2DState.AnimationStateId).ToArray();

            // East clip (index 0) should have full weight
            Assert.AreEqual(1f, allSamplers[0].Weight, 0.01f, "East clip should have full weight");
            Assert.AreEqual(0f, allSamplers[1].Weight, 0.01f, "North clip should have zero weight");
            Assert.AreEqual(0f, allSamplers[2].Weight, 0.01f, "West clip should have zero weight");
            Assert.AreEqual(0f, allSamplers[3].Weight, 0.01f, "South clip should have zero weight");
        }

        [UnityTest]
        public IEnumerator BlendDiagonal_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalPositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);

            // Set blend parameters to point NE (between North and East)
            AnimationStateTestUtils.SetBlendParameter2D(directional2DState, manager, entity, 1f, 1f);

            UpdateWorld();

            var allSamplers =
                ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                    directional2DState.AnimationStateId).ToArray();

            // Both East and North should have weight, others should be zero
            Assert.Greater(allSamplers[0].Weight, 0.3f, "East clip should have significant weight");
            Assert.Greater(allSamplers[1].Weight, 0.3f, "North clip should have significant weight");
            Assert.AreEqual(0f, allSamplers[2].Weight, 0.01f, "West clip should have zero weight");
            Assert.AreEqual(0f, allSamplers[3].Weight, 0.01f, "South clip should have zero weight");

            // Weights should sum to 1 (approximately)
            float sum = allSamplers.Sum(s => s.Weight);
            Assert.AreEqual(1f, sum, 0.01f, "Weights should sum to 1");
        }

        [UnityTest]
        public IEnumerator BlendWithIdle_AtOrigin_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalWithIdlePositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);

            // Set blend parameters to origin
            AnimationStateTestUtils.SetBlendParameter2D(directional2DState, manager, entity, 0f, 0f);

            UpdateWorld();

            var allSamplers =
                ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                    directional2DState.AnimationStateId).ToArray();

            // Idle clip (index 0) should have full weight
            Assert.AreEqual(1f, allSamplers[0].Weight, 0.01f, "Idle clip should have full weight at origin");
            Assert.AreEqual(0f, allSamplers[1].Weight, 0.01f, "East clip should have zero weight");
            Assert.AreEqual(0f, allSamplers[2].Weight, 0.01f, "North clip should have zero weight");
            Assert.AreEqual(0f, allSamplers[3].Weight, 0.01f, "West clip should have zero weight");
            Assert.AreEqual(0f, allSamplers[4].Weight, 0.01f, "South clip should have zero weight");
        }

        [UnityTest]
        public IEnumerator BlendWithIdle_SmallMagnitude_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalWithIdlePositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);

            // Set blend parameters to small magnitude pointing East
            AnimationStateTestUtils.SetBlendParameter2D(directional2DState, manager, entity, 0.3f, 0f);

            UpdateWorld();

            var allSamplers =
                ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                    directional2DState.AnimationStateId).ToArray();

            // Both Idle and East should have weight
            Assert.Greater(allSamplers[0].Weight, 0.3f, "Idle clip should have significant weight for small input");
            Assert.Greater(allSamplers[1].Weight, 0.1f, "East clip should have some weight");

            // Weights should sum to 1
            float sum = allSamplers.Sum(s => s.Weight);
            Assert.AreEqual(1f, sum, 0.01f, "Weights should sum to 1");
        }

        [UnityTest]
        public IEnumerator Keep_WeightSum_EqualOne_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalPositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);

            // Test multiple input values
            var testInputs = new[]
            {
                new float2(0, 0),
                new float2(1, 0),
                new float2(0, 1),
                new float2(1, 1),
                new float2(0.5f, 0.5f),
                new float2(-0.7f, 0.3f),
            };

            foreach (var input in testInputs)
            {
                AnimationStateTestUtils.SetBlendParameter2D(directional2DState, manager, entity, input.x, input.y);
                UpdateWorld();

                var allSamplers =
                    ClipSamplerTestUtils.GetAllSamplersForAnimationState(manager, entity,
                        directional2DState.AnimationStateId);

                var sumWeight = allSamplers.Sum(s => s.Weight);
                Assert.AreEqual(1f, sumWeight, 0.01f, $"Sum of weights should equal 1 for input {input}");
            }
        }

        [UnityTest]
        public IEnumerator CleanupStates_WithRealACLData()
        {
            yield return null;

            var entity = CreateDirectional2DBlendEntity(CardinalPositions);
            var directional2DState = AnimationStateTestUtils.CreateDirectional2DBlendForStateMachine(0, manager, entity);
            AnimationStateTestUtils.SetCurrentState(manager, entity, directional2DState.AnimationStateId);
            var anotherState = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(manager, entity, clipsBlob);

            var directional2DStates = manager.GetBuffer<Directional2DBlendStateMachineState>(entity);
            Assert.AreEqual(1, directional2DStates.Length);

            const float transitionDuration = 0.2f;
            AnimationStateTestUtils.RequestTransitionTo(manager, entity, anotherState.AnimationStateId,
                transitionDuration);

            UpdateWorld();

            directional2DStates = manager.GetBuffer<Directional2DBlendStateMachineState>(entity);
            // We should still have the state since we're transitioning
            Assert.AreEqual(1, directional2DStates.Length);

            UpdateWorld(transitionDuration);

            // Should have cleaned up the directional 2D state after transition
            directional2DStates = manager.GetBuffer<Directional2DBlendStateMachineState>(entity);
            Assert.Zero(directional2DStates.Length, "Directional2D state should be cleaned up after transition");
        }

        private Entity CreateDirectional2DBlendEntity(float2[] positions)
        {
            var stateMachineBuilder = AnimationStateMachineAssetBuilder.New();
            var directional2DState = stateMachineBuilder.AddState<Directional2DBlendStateAsset>();

            directional2DState.BlendParameterX = stateMachineBuilder.AddParameter<FloatParameterAsset>("blendX");
            directional2DState.BlendParameterY = stateMachineBuilder.AddParameter<FloatParameterAsset>("blendY");
            directional2DState.BlendClips = new Directional2DClipWithPosition[positions.Length];
            
            for (int i = 0; i < positions.Length; i++)
            {
                directional2DState.BlendClips[i] = new Directional2DClipWithPosition
                {
                    Clip = null,
                    Speed = 1,
                    Position = positions[i]
                };
            }

            var stateMachineAsset = stateMachineBuilder.Build();

            var stateMachineBlob =
                AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachineAsset);
            TrackBlob(stateMachineBlob);

            var entity = manager.CreateStateMachineEntity(stateMachineAsset, stateMachineBlob, clipsBlob,
                BlobAssetReference<ClipEventsBlob>.Null);

            TrackEntity(entity);

            Assert.IsTrue(manager.HasComponent<Directional2DBlendStateMachineState>(entity));
            Assert.IsTrue(manager.HasComponent<FloatParameter>(entity));
            return entity;
        }
    }
}
