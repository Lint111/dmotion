using System;
using System.Collections;
using System.Diagnostics;
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
using Debug = UnityEngine.Debug;

namespace DMotion.Tests
{
    /// <summary>
    /// Base class for performance integration tests that need real SmartBlobber-baked ACL clip data.
    ///
    /// DESIGN PRINCIPLE: Never call world.Update() during setup/teardown.
    /// - Setup only loads scene and finds prefab (no system execution)
    /// - Burst compilation happens lazily during actual test execution
    /// - Teardown only destroys entities (no system execution)
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Wait for subscene baking to complete
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

        // Cached Burst settings to restore after test
        private bool cachedBurstSynchronous;
        private bool cachedBurstSafetyChecks;

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
            var setupStopwatch = Stopwatch.StartNew();

            var memAtStart = GC.GetTotalMemory(false) / (1024 * 1024);
            Debug.Log($"[PerformanceIntegrationTestBase] SetUp starting. Managed memory: {memAtStart}MB");

            // CRITICAL: Disable synchronous Burst compilation to prevent freezes
            // Burst will compile asynchronously in the background
            cachedBurstSynchronous = BurstCompiler.Options.EnableBurstCompileSynchronously;
            cachedBurstSafetyChecks = BurstCompiler.Options.EnableBurstSafetyChecks;
            BurstCompiler.Options.EnableBurstCompileSynchronously = false;

            // Check if test scene exists
            if (!PrebakedTestHelper.IsTestSceneSetup())
            {
                // In CI/batch mode, fail loudly so broken test setup is noticed
                if (Application.isBatchMode || IsContinuousIntegration())
                {
                    Assert.Fail("CRITICAL: Test scene not set up. " +
                               "CI must run 'DMotion/Tests/Setup Test Scene' before running performance tests.");
                }
                else
                {
                    Assert.Ignore("Test scene not set up. Run 'DMotion/Tests/Setup Test Scene' first.");
                }
                yield break;
            }

            // Load the pre-baked test scene
            yield return PrebakedTestHelper.LoadTestScene();
            Debug.Log($"[PerformanceIntegrationTestBase] Scene loaded in {setupStopwatch.Elapsed.TotalSeconds:F1}s");

            // Get the default world with baked entities
            world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Assert.Fail("DefaultGameObjectInjectionWorld is null after loading test scene");
                yield break;
            }

            manager = world.EntityManager;

            // Find the stress test prefab - synchronous, no world.Update()
            FindStressTestPrefabSync();

            // Create the requested systems (but don't run them yet)
            if (SystemTypes.Length > 0)
            {
                allSystems = new NativeArray<SystemHandle>(SystemTypes.Length, Allocator.Persistent);
                for (int i = 0; i < SystemTypes.Length; i++)
                {
                    allSystems[i] = world.CreateSystem(SystemTypes[i]);
                }
            }

            elapsedTime = Time.time;

            Debug.Log($"[PerformanceIntegrationTestBase] Setup completed in {setupStopwatch.Elapsed.TotalSeconds:F1}s");

            // Allow subclass setup
            yield return OnSetUp();
        }

        /// <summary>
        /// Finds the stress test prefab entity synchronously without any world.Update() calls.
        /// </summary>
        private void FindStressTestPrefabSync()
        {
            // Look for prefab with StressTestOneShotClip
            var prefabQuery = manager.CreateEntityQuery(
                typeof(StressTestOneShotClip),
                typeof(Prefab));

            using (var entities = prefabQuery.ToEntityArray(Allocator.TempJob))
            {
                if (entities.Length > 0)
                {
                    stressTestPrefabEntity = entities[0];
                    var stressTest = manager.GetComponentData<StressTestOneShotClip>(stressTestPrefabEntity);
                    clipsBlob = stressTest.Clips;
                    Debug.Log($"[PerformanceIntegrationTestBase] Found stress test prefab: {stressTestPrefabEntity}, clips: {clipsBlob.Value.clips.Length}");
                    return;
                }
            }

            // Fallback: look for non-prefab entity
            var nonPrefabQuery = manager.CreateEntityQuery(typeof(StressTestOneShotClip));
            using (var entities = nonPrefabQuery.ToEntityArray(Allocator.TempJob))
            {
                if (entities.Length > 0)
                {
                    stressTestPrefabEntity = entities[0];
                    var stressTest = manager.GetComponentData<StressTestOneShotClip>(stressTestPrefabEntity);
                    clipsBlob = stressTest.Clips;
                    Debug.Log($"[PerformanceIntegrationTestBase] Found stress test entity (non-prefab): {stressTestPrefabEntity}");
                    return;
                }
            }

            Debug.LogWarning("[PerformanceIntegrationTestBase] No stress test prefab found. " +
                           "Ensure prefab with PerformanceTestsAuthoring is in the test scene.");
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
            Debug.Log("[PerformanceIntegrationTestBase] TearDown starting...");

            // Allow subclass teardown first
            yield return OnTearDown();

            // Complete jobs but DON'T destroy entities yet - they need to exist during scene unload
            // Otherwise Kinemation jobs scheduled during unload yields will crash
            if (world != null && world.IsCreated)
            {
                manager.CompleteAllTrackedJobs();
            }

            // Dispose systems array
            if (allSystems.IsCreated)
            {
                allSystems.Dispose();
            }

            // Restore Burst settings
            BurstCompiler.Options.EnableBurstCompileSynchronously = cachedBurstSynchronous;
            BurstCompiler.Options.EnableBurstSafetyChecks = cachedBurstSafetyChecks;

            // Clear references
            clipsBlob = default;
            stressTestPrefabEntity = Entity.Null;

            // Unload test scene FIRST (while entities still exist)
            // This allows Kinemation to process entities during unload yields
            yield return PrebakedTestHelper.UnloadTestScene();

            // NOW destroy remaining entities - after all yields are done
            // No more frame updates will happen after this point
            if (world != null && world.IsCreated)
            {
                manager.CompleteAllTrackedJobs();
                DestroyTestEntities();
                tracker.Cleanup(manager);
            }

            Debug.Log("[PerformanceIntegrationTestBase] TearDown complete");
        }

        /// <summary>
        /// Destroys test entities synchronously. May cause Kinemation race condition
        /// errors during scene unload, which are expected and suppressed.
        /// </summary>
        private void DestroyTestEntities()
        {
            if (world == null || !world.IsCreated)
                return;

            var query = manager.CreateEntityQuery(typeof(AnimationStateMachine));
            var count = query.CalculateEntityCount();

            if (count > 0)
            {
                Debug.Log($"[PerformanceIntegrationTestBase] Destroying {count} test entities...");
                manager.DestroyEntity(query);
                manager.CompleteAllTrackedJobs();
            }
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
        /// This is the ONLY place where systems should run.
        /// </summary>
        protected void UpdateWorld(float deltaTime = 1.0f / 60.0f)
        {
            if (world == null || !world.IsCreated)
                return;

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

            Debug.Log($"[PerformanceIntegrationTestBase] Instantiating {count} entities...");

            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            {
                for (int i = 0; i < count; i++)
                {
                    ecb.Instantiate(stressTestPrefabEntity);
                }
                ecb.Playback(manager);
            }

            Debug.Log($"[PerformanceIntegrationTestBase] Instantiated {count} entities");
        }

        /// <summary>
        /// Creates a default performance measurement with profiler marker.
        /// Burst compilation will happen lazily during warmup iterations.
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

        /// <summary>
        /// Detects if running in a CI environment.
        /// </summary>
        private static bool IsContinuousIntegration()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEAMCITY_VERSION"));
        }
    }
}
