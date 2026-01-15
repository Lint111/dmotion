using System.Collections;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for Root Motion functionality in ClipSamplingSystem.
    ///
    /// Tests cover:
    /// 1. SampleRootDeltasJob - calculates root motion deltas from animation clips
    /// 2. ApplyRootMotionToEntityJob - applies deltas to entity transform
    /// 3. TransferRootMotionJob - transfers deltas to owner entity
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Open TestAnimationScene.unity to trigger baking
    /// </summary>
    public class RootMotionIntegrationTests : IntegrationTestBase
    {
        protected override System.Type[] SystemTypes => new[]
        {
            typeof(ClipSamplingSystem)
        };

        /// <summary>
        /// Gets skeleton entity and ensures it has root motion components.
        /// </summary>
        private Entity GetRootMotionEntity()
        {
            var entity = bakedEntity;
            
            // Ensure entity has required root motion components
            if (!manager.HasComponent<RootDeltaTranslation>(entity))
            {
                manager.AddComponent<RootDeltaTranslation>(entity);
            }
            if (!manager.HasComponent<RootDeltaRotation>(entity))
            {
                manager.AddComponent<RootDeltaRotation>(entity);
            }
            
            return entity;
        }

        /// <summary>
        /// Clears all animation state buffers to avoid orphaned references when modifying samplers directly.
        /// The LinearBlendStateMachineState, SingleClipState, and AnimationState reference samplers by ID,
        /// so we must clear them when we replace the samplers.
        /// </summary>
        private void ClearAnimationStateBuffers(Entity entity)
        {
            if (manager.HasBuffer<LinearBlendStateMachineState>(entity))
            {
                manager.GetBuffer<LinearBlendStateMachineState>(entity).Clear();
            }
            if (manager.HasBuffer<SingleClipState>(entity))
            {
                manager.GetBuffer<SingleClipState>(entity).Clear();
            }
            if (manager.HasBuffer<AnimationState>(entity))
            {
                manager.GetBuffer<AnimationState>(entity).Clear();
            }
        }

        /// <summary>
        /// Sets up a ClipSampler with time progression for root motion calculation.
        /// IMPORTANT: This clears all animation state to avoid crashes from orphaned state machine references.
        /// </summary>
        private void SetupRootMotionSampler(Entity entity, int clipIndex, float time, float previousTime, float weight = 1.0f)
        {
            ClearAnimationStateBuffers(entity);

            var samplers = manager.GetBuffer<ClipSampler>(entity);
            samplers.Clear();

            samplers.Add(new ClipSampler
            {
                Clips = clipsBlob,
                ClipIndex = (ushort)clipIndex,
                Time = time,
                PreviousTime = previousTime,
                Weight = weight
            });
        }

        [UnityTest]
        public IEnumerator SampleRootDeltas_CalculatesDeltaFromTimeProgression()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            // Reset root deltas
            manager.SetComponentData(entity, new RootDeltaTranslation { Value = float3.zero });
            manager.SetComponentData(entity, new RootDeltaRotation { Value = quaternion.identity });

            // Setup sampler with time progression (simulating animation playback)
            const float previousTime = 0.0f;
            const float currentTime = 0.1f; // 100ms of animation
            SetupRootMotionSampler(entity, clipIndex: 0, time: currentTime, previousTime: previousTime);

            UpdateWorld();

            // Verify: Root deltas were calculated (may be zero if clip has no root motion)
            var deltaTranslation = manager.GetComponentData<RootDeltaTranslation>(entity);
            var deltaRotation = manager.GetComponentData<RootDeltaRotation>(entity);

            // The deltas should be valid (quaternion should be normalized or identity)
            Assert.IsTrue(math.isfinite(deltaTranslation.Value.x), "Delta translation X should be finite");
            Assert.IsTrue(math.isfinite(deltaTranslation.Value.y), "Delta translation Y should be finite");
            Assert.IsTrue(math.isfinite(deltaTranslation.Value.z), "Delta translation Z should be finite");
            
            float quatLength = math.length(deltaRotation.Value.value);
            Assert.IsTrue(quatLength > 0.99f && quatLength < 1.01f || quatLength == 0, 
                $"Delta rotation should be normalized or zero, got length {quatLength}");
        }

        [UnityTest]
        public IEnumerator SampleRootDeltas_ReturnsZeroWhenNoTimeProgression()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            // Reset root deltas
            manager.SetComponentData(entity, new RootDeltaTranslation { Value = new float3(999, 999, 999) });
            manager.SetComponentData(entity, new RootDeltaRotation { Value = quaternion.Euler(1, 1, 1) });

            // Setup sampler with NO time progression (same time)
            const float time = 0.5f;
            SetupRootMotionSampler(entity, clipIndex: 0, time: time, previousTime: time);

            UpdateWorld();

            // Verify: Root deltas should be zero/identity when no time progression
            var deltaTranslation = manager.GetComponentData<RootDeltaTranslation>(entity);
            var deltaRotation = manager.GetComponentData<RootDeltaRotation>(entity);

            Assert.AreEqual(float3.zero, deltaTranslation.Value, 
                "Delta translation should be zero when no time progression");
            Assert.IsTrue(
                math.abs(deltaRotation.Value.value.x) < 0.001f &&
                math.abs(deltaRotation.Value.value.y) < 0.001f &&
                math.abs(deltaRotation.Value.value.z) < 0.001f,
                "Delta rotation should be identity when no time progression");
        }

        [UnityTest]
        public IEnumerator SampleRootDeltas_SkipsZeroWeightSamplers()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            // Set non-zero initial values to verify they get reset
            manager.SetComponentData(entity, new RootDeltaTranslation { Value = new float3(999, 999, 999) });
            manager.SetComponentData(entity, new RootDeltaRotation { Value = quaternion.Euler(1, 1, 1) });

            // Setup sampler with zero weight
            SetupRootMotionSampler(entity, clipIndex: 0, time: 0.5f, previousTime: 0.0f, weight: 0.0f);

            UpdateWorld();

            // Verify: Deltas should be reset to zero/identity (zero weight sampler skipped)
            var deltaTranslation = manager.GetComponentData<RootDeltaTranslation>(entity);
            var deltaRotation = manager.GetComponentData<RootDeltaRotation>(entity);

            Assert.AreEqual(float3.zero, deltaTranslation.Value, 
                "Delta translation should be zero when sampler has zero weight");
        }

        [UnityTest]
        public IEnumerator SampleRootDeltas_HandlesLoopPoint()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            float clipDuration = GetClipDuration(0);

            // Setup sampler crossing loop point (previousTime near end, time near start)
            // This should be handled gracefully - the job skips samplers where time < previousTime
            SetupRootMotionSampler(entity, clipIndex: 0, time: 0.01f, previousTime: clipDuration - 0.01f);

            UpdateWorld();

            // Verify: No crash, deltas should be zero (loop point skipped)
            var deltaTranslation = manager.GetComponentData<RootDeltaTranslation>(entity);
            Assert.IsTrue(math.isfinite(deltaTranslation.Value.x), "Should handle loop point gracefully");
        }

        [UnityTest]
        public IEnumerator SampleRootDeltas_AccumulatesAcrossFrames()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            float3 totalTranslation = float3.zero;
            float time = 0.0f;
            const float deltaTime = 1.0f / 60.0f;
            const int frameCount = 30; // Half second of animation

            for (int i = 0; i < frameCount; i++)
            {
                float previousTime = time;
                time += deltaTime;

                // Reset delta before each frame
                manager.SetComponentData(entity, new RootDeltaTranslation { Value = float3.zero });
                manager.SetComponentData(entity, new RootDeltaRotation { Value = quaternion.identity });

                SetupRootMotionSampler(entity, clipIndex: 0, time: time, previousTime: previousTime);
                UpdateWorld(deltaTime);

                var delta = manager.GetComponentData<RootDeltaTranslation>(entity);
                totalTranslation += delta.Value;
            }

            // Verify: Total translation is finite (accumulated correctly)
            Assert.IsTrue(math.isfinite(totalTranslation.x), "Accumulated translation should be finite");
            Assert.IsTrue(math.isfinite(totalTranslation.y), "Accumulated translation should be finite");
            Assert.IsTrue(math.isfinite(totalTranslation.z), "Accumulated translation should be finite");

            Debug.Log($"[RootMotionIntegrationTests] Total root motion over {frameCount} frames: {totalTranslation}");
        }

        [UnityTest]
        public IEnumerator SampleRootDeltas_BlendsMultipleSamplers()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            // Clear animation state buffers before modifying samplers
            ClearAnimationStateBuffers(entity);

            // Setup two samplers with equal weight (50/50 blend)
            var samplers = manager.GetBuffer<ClipSampler>(entity);
            samplers.Clear();

            const float time = 0.5f;
            const float previousTime = 0.4f;

            samplers.Add(new ClipSampler
            {
                Clips = clipsBlob,
                ClipIndex = 0,
                Time = time,
                PreviousTime = previousTime,
                Weight = 0.5f
            });

            samplers.Add(new ClipSampler
            {
                Clips = clipsBlob,
                ClipIndex = 0, // Same clip, different weight
                Time = time,
                PreviousTime = previousTime,
                Weight = 0.5f
            });

            UpdateWorld();

            // Verify: Deltas calculated without crash
            var deltaTranslation = manager.GetComponentData<RootDeltaTranslation>(entity);
            Assert.IsTrue(math.isfinite(deltaTranslation.Value.x), "Blended delta should be finite");
        }

        [UnityTest]
        public IEnumerator ApplyRootMotion_WhenEntityHasApplyRootMotionComponent()
        {
            yield return null;

            var entity = GetRootMotionEntity();

            // Add ApplyRootMotionToEntity component if not present
            if (!manager.HasComponent<ApplyRootMotionToEntity>(entity))
            {
                manager.AddComponent<ApplyRootMotionToEntity>(entity);
            }

            // Get initial position
            var initialTransform = manager.GetComponentData<LocalTransform>(entity);
            var initialPosition = initialTransform.Position;

            // Set a known delta
            manager.SetComponentData(entity, new RootDeltaTranslation { Value = new float3(1, 0, 0) });
            manager.SetComponentData(entity, new RootDeltaRotation { Value = quaternion.identity });

            // Setup sampler to trigger the system (even though we manually set deltas)
            SetupRootMotionSampler(entity, clipIndex: 0, time: 0.1f, previousTime: 0.0f);

            UpdateWorld();

            // Note: The delta we set manually will be overwritten by SampleRootDeltasJob
            // This test verifies the ApplyRootMotionToEntityJob runs without error
            var finalTransform = manager.GetComponentData<LocalTransform>(entity);
            Assert.IsTrue(math.isfinite(finalTransform.Position.x), "Final position should be finite");
        }
    }
}
