using System;
using System.Collections;
using DMotion.Tests;
using NUnit.Framework;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.PerformanceTesting;

namespace DMotion.PerformanceTests
{
    /// <summary>
    /// Performance tests for the animation state machine using real SmartBlobber-baked ACL data.
    ///
    /// Uses pre-baked entities from the test scene with stress test prefabs.
    /// These tests measure actual performance including ACL decompression and full system execution.
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Ensure Armature_StressTest prefab is included in the scene
    /// 3. Open TestAnimationScene.unity to trigger baking
    /// </summary>
    public class StateMachinePerformanceTests : PerformanceIntegrationTestBase
    {
        protected override Type[] SystemTypes => new[]
        {
            typeof(AnimationStateMachineSystem),
            typeof(PlayOneShotSystem),
            typeof(StateMachinePerformanceTestSystem),
            typeof(ClipSamplingSystem),
            typeof(BlendAnimationStatesSystem),
            typeof(UpdateAnimationStatesSystem)
        };

        // Benchmark asset is optional - set to null if not available
        private PerformanceTestBenchmarksPerMachine avgUpdateTimeBenchmarks = null;

        private static int[] testValues = { 1000, 10_000, 100_000 };
        internal static readonly ProfilerMarker Marker =
            new("StateMachinePerformanceTests (UpdateWorld)");

        protected override IEnumerator OnSetUp()
        {
            // Verify we have the stress test prefab
            if (stressTestPrefabEntity == Unity.Entities.Entity.Null)
            {
                Debug.LogWarning("[StateMachinePerformanceTests] No stress test prefab found - tests will be skipped");
            }
            else
            {
                // Verify required components
                Assert.IsTrue(manager.HasComponent<LinearBlendDirection>(stressTestPrefabEntity) ||
                             !manager.HasComponent<Prefab>(stressTestPrefabEntity),
                    "Stress test prefab should have LinearBlendDirection component");
            }

            yield return base.OnSetUp();
        }

        [UnityTest, Performance]
        public IEnumerator AverageUpdateTime_1000()
        {
            yield return RunPerformanceTest(1000);
        }

        [UnityTest, Performance]
        public IEnumerator AverageUpdateTime_10000()
        {
            yield return RunPerformanceTest(10_000);
        }

        [UnityTest, Performance]
        public IEnumerator AverageUpdateTime_100000()
        {
            yield return RunPerformanceTest(100_000);
        }

        private IEnumerator RunPerformanceTest(int count)
        {
            yield return null;

            if (stressTestPrefabEntity == Unity.Entities.Entity.Null)
            {
                Assert.Ignore("No stress test prefab available. Run 'DMotion/Tests/Setup Test Scene' first.");
                yield break;
            }

            InstantiateEntities(count);

            // Run the performance measurement
            DefaultPerformanceMeasure(Marker).Run();

            if (TryGetBenchmarkForCount(count, avgUpdateTimeBenchmarks, out var benchmark))
            {
                benchmark.AssertWithinBenchmark();
            }
        }

        private bool TryGetBenchmarkForCount(int count, PerformanceTestBenchmarksPerMachine groupsAsset,
            out PerformanceTestBenchmark benchmark)
        {
            benchmark = default;
            if (groupsAsset == null)
                return false;

            var groups = groupsAsset.MachineBenchmarks;
            if (groups == null || groups.Length == 0)
            {
                return false;
            }

            var machineName = SystemInfo.deviceName;
            var groupIndex = Array.FindIndex(groups, g => g.MachineName.Equals(machineName));
            if (groupIndex < 0)
            {
                return false;
            }

            var group = groups[groupIndex];

            Assert.IsNotNull(group.Benchmarks);
            var index = Array.FindIndex(group.Benchmarks, b => b.Count == count);
            if (index < 0)
            {
                return false;
            }

            benchmark = group.Benchmarks[index];
            return true;
        }
    }
}
