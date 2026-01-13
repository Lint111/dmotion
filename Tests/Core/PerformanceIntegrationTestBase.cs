using System;
using System.Collections;
using DMotion.PerformanceTests;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Base class for performance integration tests that need real SmartBlobber-baked ACL clip data.
    ///
    /// Uses the same pre-baked test scene as IntegrationTestBase but adds performance measurement
    /// capabilities and entity instantiation for stress testing.
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Open TestAnimationScene.unity to trigger baking
    /// 3. Ensure stress test prefabs (with PerformanceTestsAuthoring) are included
    /// </summary>
    public abstract class PerformanceIntegrationTestBase
    {
        protected World world;
        protected EntityManager manager;
        protected Entity stressTestPrefabEntity;
        protected BlobAssetReference<SkeletonClipSetBlob> clipsBlob;

        private float elapsedTime;
        private NativeArray<SystemHandle> allSystems;
        private readonly BlobAndEntityTracker tracker = new BlobAndEntityTracker("PerformanceIntegrationTestBase");
        private BurstAndJobConfigsCache burstCache;

        /// <summary>
        /// Override to specify which systems to create for testing.
        /// </summary>
        protected virtual Type[] SystemTypes => Array.Empty<Type>();

        /// <summary>
        /// Default warmup count for performance measurements.
        /// </summary>
        protected virtual int WarmupCount => 20;

        /// <summary>
        /// Default measurement count for performance measurements.
        /// </summary>
        protected virtual int MeasurementCount => 50;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Cache and set max performance Burst parameters
            burstCache.Cache();
            PerformanceTestUtils.SetMaxPerformanceBurstParameters();

            // Check if test scene exists
            if (!PrebakedTestHelper.IsTestSceneSetup())
            {
                Assert.Ignore("Test scene not set up. Run 'DMotion/Tests/Setup Test Scene' first.");
                yield break;
            }

            // Load the pre-baked test scene
            yield return PrebakedTestHelper.LoadTestScene();

            // Get the default world with baked entities
            world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Assert.Fail("DefaultGameObjectInjectionWorld is null after loading test scene");
                yield break;
            }

            manager = world.EntityManager;

            // Find the stress test prefab entity (has StressTestOneShotClip component)
            yield return FindStressTestPrefab();

            // Create and track the requested systems
            if (SystemTypes.Length > 0)
            {
                allSystems = new NativeArray<SystemHandle>(SystemTypes.Length, Allocator.Persistent);
                for (int i = 0; i < SystemTypes.Length; i++)
                {
                    allSystems[i] = world.CreateSystem(SystemTypes[i]);
                }
            }

            elapsedTime = Time.time;

            // Warmup phase: trigger Burst compilation before measurements
            // This ensures synchronous compilation happens during setup, not during test
            yield return WarmupBurstCompilation();

            // Allow subclass setup
            yield return OnSetUp();
        }

        /// <summary>
        /// Finds the stress test prefab entity from the baked scene.
        /// </summary>
        private IEnumerator FindStressTestPrefab()
        {
            var query = manager.CreateEntityQuery(
                typeof(DMotion.PerformanceTests.StressTestOneShotClip),
                typeof(Prefab));

            int attempts = 0;
            const int maxAttempts = 60; // ~1 second at 60fps

            while (attempts < maxAttempts)
            {
                var entities = query.ToEntityArray(Allocator.Temp);
                if (entities.Length > 0)
                {
                    stressTestPrefabEntity = entities[0];
                    Debug.Log($"[PerformanceIntegrationTestBase] Found stress test prefab entity: {stressTestPrefabEntity}");

                    // Also get the clips blob from the entity
                    if (manager.HasComponent<DMotion.PerformanceTests.StressTestOneShotClip>(stressTestPrefabEntity))
                    {
                        var stressTest = manager.GetComponentData<DMotion.PerformanceTests.StressTestOneShotClip>(stressTestPrefabEntity);
                        clipsBlob = stressTest.Clips;
                        Debug.Log($"[PerformanceIntegrationTestBase] Got clips blob with {clipsBlob.Value.clips.Length} clips");
                    }

                    entities.Dispose();
                    yield break;
                }
                entities.Dispose();

                attempts++;
                yield return null;
            }

            // If no prefab entity found, try to find non-prefab stress test entity
            query = manager.CreateEntityQuery(typeof(DMotion.PerformanceTests.StressTestOneShotClip));
            var nonPrefabEntities = query.ToEntityArray(Allocator.Temp);
            if (nonPrefabEntities.Length > 0)
            {
                // Use first entity as template - we'll need to copy it
                var templateEntity = nonPrefabEntities[0];
                Debug.Log($"[PerformanceIntegrationTestBase] Found stress test entity (non-prefab): {templateEntity}");

                if (manager.HasComponent<DMotion.PerformanceTests.StressTestOneShotClip>(templateEntity))
                {
                    var stressTest = manager.GetComponentData<DMotion.PerformanceTests.StressTestOneShotClip>(templateEntity);
                    clipsBlob = stressTest.Clips;
                    stressTestPrefabEntity = templateEntity;
                    Debug.Log($"[PerformanceIntegrationTestBase] Using non-prefab entity as template");
                }

                nonPrefabEntities.Dispose();
                yield break;
            }
            nonPrefabEntities.Dispose();

            Debug.LogWarning("[PerformanceIntegrationTestBase] No stress test prefab found. " +
                           "Ensure prefab with PerformanceTestsAuthoring is in the test scene.");
        }

        /// <summary>
        /// Warmup phase to trigger Burst compilation before measurements.
        /// Uses async compilation during warmup to prevent editor freeze,
        /// then enables sync compilation for accurate measurements.
        /// </summary>
        private IEnumerator WarmupBurstCompilation()
        {
            if (stressTestPrefabEntity == Entity.Null || SystemTypes.Length == 0)
            {
                yield break;
            }

            Debug.Log("[PerformanceIntegrationTestBase] Starting Burst warmup phase (async compilation)...");

            // Temporarily disable synchronous compilation during warmup
            // This prevents editor freeze while Burst compiles in background
            var wasSyncCompilation = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = false;

            // Instantiate a small number of entities to trigger all code paths
            const int warmupEntityCount = 10;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < warmupEntityCount; i++)
            {
                ecb.Instantiate(stressTestPrefabEntity);
            }
            ecb.Playback(manager);
            ecb.Dispose();

            // Run several update cycles to trigger Burst compilation (async)
            // More frames to give Burst time to compile in background
            const int warmupFrames = 30;
            for (int i = 0; i < warmupFrames; i++)
            {
                UpdateWorld();
                yield return null;
                
                // Log progress every 10 frames
                if ((i + 1) % 10 == 0)
                {
                    Debug.Log($"[PerformanceIntegrationTestBase] Warmup frame {i + 1}/{warmupFrames}");
                }
            }

            // Clean up warmup entities - but NOT the template entity we need for actual tests
            var query = manager.CreateEntityQuery(
                ComponentType.ReadOnly<DMotion.PerformanceTests.StressTestOneShotClip>(),
                ComponentType.Exclude<Prefab>());
            var warmupEntities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < warmupEntities.Length; i++)
            {
                // Don't destroy our template entity - we need it for the actual test
                if (warmupEntities[i] != stressTestPrefabEntity)
                {
                    manager.DestroyEntity(warmupEntities[i]);
                }
            }
            warmupEntities.Dispose();

            // Restore synchronous compilation for accurate measurements
            BurstCompiler.Options.EnableBurstCompileSynchronously = wasSyncCompilation;

            Debug.Log("[PerformanceIntegrationTestBase] Burst warmup complete.");
        }

        /// <summary>
        /// Override for additional setup after scene load.
        /// </summary>
        protected virtual IEnumerator OnSetUp()
        {
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Allow subclass teardown
            yield return OnTearDown();

            // Complete all jobs before cleanup to avoid race conditions
            if (manager.World != null && manager.World.IsCreated)
            {
                manager.CompleteAllTrackedJobs();
            }

            // Destroy all instantiated stress test entities (non-prefabs)
            if (manager.World != null && manager.World.IsCreated)
            {
                var query = manager.CreateEntityQuery(
                    ComponentType.ReadOnly<DMotion.PerformanceTests.StressTestOneShotClip>(),
                    ComponentType.Exclude<Prefab>());
                var entitiesToDestroy = query.ToEntityArray(Allocator.Temp);
                if (entitiesToDestroy.Length > 0)
                {
                    manager.DestroyEntity(entitiesToDestroy);
                }
                entitiesToDestroy.Dispose();
                
                // Complete jobs again after entity destruction
                manager.CompleteAllTrackedJobs();
            }

            // Clean up tracked blobs and entities
            tracker.Cleanup(manager);

            // Dispose systems array
            if (allSystems.IsCreated)
            {
                allSystems.Dispose();
            }

            // Restore Burst settings
            burstCache.SetCachedValues();

            // Let the world stabilize for a few frames before unloading
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            // Unload test scene
            yield return PrebakedTestHelper.UnloadTestScene();
        }

        /// <summary>
        /// Override for additional teardown before scene unload.
        /// </summary>
        protected virtual IEnumerator OnTearDown()
        {
            yield break;
        }

        /// <summary>
        /// Tracks a BlobAssetReference for disposal during teardown.
        /// </summary>
        protected void TrackBlob<T>(BlobAssetReference<T> blob) where T : unmanaged
        {
            tracker.TrackBlob(blob);
        }

        /// <summary>
        /// Tracks an entity for cleanup during teardown.
        /// </summary>
        protected void TrackEntity(Entity entity)
        {
            tracker.TrackEntity(entity);
        }

        /// <summary>
        /// Updates the world by the specified delta time.
        /// </summary>
        protected void UpdateWorld(float deltaTime = 1.0f / 60.0f)
        {
            if (world != null && world.IsCreated)
            {
                elapsedTime += deltaTime;
                world.SetTime(new TimeData(elapsedTime, deltaTime));

                if (allSystems.IsCreated)
                {
                    foreach (var s in allSystems)
                    {
                        s.Update(world.Unmanaged);
                    }
                }

                manager.CompleteAllTrackedJobs();
            }
        }

        /// <summary>
        /// Instantiates multiple copies of the stress test entity for performance testing.
        /// </summary>
        protected void InstantiateEntities(int count)
        {
            if (stressTestPrefabEntity == Entity.Null)
            {
                Assert.Fail("No stress test prefab entity available. Ensure test scene is set up correctly.");
                return;
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                ecb.Instantiate(stressTestPrefabEntity);
            }

            ecb.Playback(manager);
            ecb.Dispose();

            // Run one update to initialize the entities
            UpdateWorld();

            Debug.Log($"[PerformanceIntegrationTestBase] Instantiated {count} entities");
        }

        /// <summary>
        /// Creates a default performance measurement with profiler marker.
        /// </summary>
        protected Unity.PerformanceTesting.Measurements.MethodMeasurement DefaultPerformanceMeasure(ProfilerMarker profilerMarker)
        {
            return Measure.Method(() => UpdateWorldWithMarker(profilerMarker))
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1);
        }

        private void UpdateWorldWithMarker(ProfilerMarker profilerMarker)
        {
            using var scope = profilerMarker.Auto();
            UpdateWorld();
        }
    }
}
