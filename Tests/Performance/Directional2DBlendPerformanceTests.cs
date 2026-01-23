using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.PerformanceTests
{
    /// <summary>
    /// Performance tests for 2D blend weight calculation algorithms.
    /// Target: Less than 500 cycles per evaluation for SimpleDirectional.
    /// 
    /// Tests common locomotion configurations:
    /// - 4-way: Forward, Back, Left, Right
    /// - 8-way: N, NE, E, SE, S, SW, W, NW
    /// - 8-way + Idle: Standard locomotion with center idle
    /// </summary>
    public class Directional2DBlendPerformanceTests
    {
        private static readonly ProfilerMarker MarkerSimpleDirectional4Way = 
            new("2DBlend.SimpleDirectional.4Way");
        private static readonly ProfilerMarker MarkerSimpleDirectional8Way = 
            new("2DBlend.SimpleDirectional.8Way");
        private static readonly ProfilerMarker MarkerSimpleDirectional8WayIdle = 
            new("2DBlend.SimpleDirectional.8Way+Idle");
        private static readonly ProfilerMarker MarkerIDW4Way = 
            new("2DBlend.IDW.4Way");
        private static readonly ProfilerMarker MarkerIDW8Way = 
            new("2DBlend.IDW.8Way");
        
        private const int WarmupCount = 100;
        private const int MeasurementCount = 500;
        private const int IterationsPerMeasurement = 100;
        
        // 4-way positions (Cardinal directions)
        private static readonly float2[] Positions4Way = new float2[]
        {
            new float2(0, 1),   // Forward
            new float2(0, -1),  // Back
            new float2(-1, 0),  // Left
            new float2(1, 0)    // Right
        };
        
        // 8-way positions (Cardinal + Diagonal)
        private static readonly float2[] Positions8Way = new float2[]
        {
            new float2(0, 1),              // N
            new float2(0.707f, 0.707f),    // NE
            new float2(1, 0),              // E
            new float2(0.707f, -0.707f),   // SE
            new float2(0, -1),             // S
            new float2(-0.707f, -0.707f),  // SW
            new float2(-1, 0),             // W
            new float2(-0.707f, 0.707f)    // NW
        };
        
        // 8-way + Idle positions
        private static readonly float2[] Positions8WayIdle = new float2[]
        {
            new float2(0, 0),              // Idle (center)
            new float2(0, 1),              // N
            new float2(0.707f, 0.707f),    // NE
            new float2(1, 0),              // E
            new float2(0.707f, -0.707f),   // SE
            new float2(0, -1),             // S
            new float2(-0.707f, -0.707f),  // SW
            new float2(-1, 0),             // W
            new float2(-0.707f, 0.707f)    // NW
        };
        
        // Sample inputs covering all quadrants
        private static readonly float2[] SampleInputs = new float2[]
        {
            new float2(0, 0),              // Center
            new float2(0.5f, 0.5f),        // NE quadrant
            new float2(-0.3f, 0.8f),       // NW quadrant
            new float2(0.9f, -0.2f),       // SE quadrant
            new float2(-0.7f, -0.6f),      // SW quadrant
            new float2(0, 0.75f),          // N axis
            new float2(1, 0),              // E edge
            new float2(0.2f, 0.1f),        // Near center
        };
        
        private NativeArray<float2> nativePositions4Way;
        private NativeArray<float2> nativePositions8Way;
        private NativeArray<float2> nativePositions8WayIdle;
        private NativeArray<float> weights4Way;
        private NativeArray<float> weights8Way;
        private NativeArray<float> weights8WayIdle;
        
        [SetUp]
        public void SetUp()
        {
            // Allocate native arrays for Burst-compatible testing
            nativePositions4Way = new NativeArray<float2>(Positions4Way, Allocator.Persistent);
            nativePositions8Way = new NativeArray<float2>(Positions8Way, Allocator.Persistent);
            nativePositions8WayIdle = new NativeArray<float2>(Positions8WayIdle, Allocator.Persistent);
            
            weights4Way = new NativeArray<float>(4, Allocator.Persistent);
            weights8Way = new NativeArray<float>(8, Allocator.Persistent);
            weights8WayIdle = new NativeArray<float>(9, Allocator.Persistent);
        }
        
        [TearDown]
        public void TearDown()
        {
            if (nativePositions4Way.IsCreated) nativePositions4Way.Dispose();
            if (nativePositions8Way.IsCreated) nativePositions8Way.Dispose();
            if (nativePositions8WayIdle.IsCreated) nativePositions8WayIdle.Dispose();
            if (weights4Way.IsCreated) weights4Way.Dispose();
            if (weights8Way.IsCreated) weights8Way.Dispose();
            if (weights8WayIdle.IsCreated) weights8WayIdle.Dispose();
        }
        
        #region Simple Directional Algorithm Tests
        
        [Test, Performance]
        public void SimpleDirectional_4Way_Performance()
        {
            Measure.Method(() =>
            {
                using var scope = MarkerSimpleDirectional4Way.Auto();
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions4Way, 
                        weights4Way,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        [Test, Performance]
        public void SimpleDirectional_8Way_Performance()
        {
            Measure.Method(() =>
            {
                using var scope = MarkerSimpleDirectional8Way.Auto();
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions8Way, 
                        weights8Way,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        [Test, Performance]
        public void SimpleDirectional_8WayWithIdle_Performance()
        {
            Measure.Method(() =>
            {
                using var scope = MarkerSimpleDirectional8WayIdle.Auto();
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions8WayIdle, 
                        weights8WayIdle,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        #endregion
        
        #region Inverse Distance Weighting Algorithm Tests
        
        [Test, Performance]
        public void IDW_4Way_Performance()
        {
            Measure.Method(() =>
            {
                using var scope = MarkerIDW4Way.Auto();
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions4Way, 
                        weights4Way,
                        Blend2DAlgorithm.InverseDistanceWeighting);
                }
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        [Test, Performance]
        public void IDW_8Way_Performance()
        {
            Measure.Method(() =>
            {
                using var scope = MarkerIDW8Way.Auto();
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions8Way, 
                        weights8Way,
                        Blend2DAlgorithm.InverseDistanceWeighting);
                }
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        #endregion
        
        #region Algorithm Comparison Tests
        
        /// <summary>
        /// Compares both algorithms side-by-side with 8-way configuration.
        /// </summary>
        [Test, Performance]
        public void AlgorithmComparison_8Way()
        {
            // SimpleDirectional
            Measure.Method(() =>
            {
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions8Way, 
                        weights8Way,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .SampleGroup("SimpleDirectional")
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
            
            // IDW
            Measure.Method(() =>
            {
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, 
                        nativePositions8Way, 
                        weights8Way,
                        Blend2DAlgorithm.InverseDistanceWeighting);
                }
            })
            .SampleGroup("IDW")
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        #endregion
        
        #region Scaling Tests
        
        /// <summary>
        /// Tests how performance scales with clip count.
        /// </summary>
        [Test, Performance]
        public void SimpleDirectional_ScalingWithClipCount()
        {
            // 4 clips
            Measure.Method(() =>
            {
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, nativePositions4Way, weights4Way,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .SampleGroup("4 clips")
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
            
            // 8 clips
            Measure.Method(() =>
            {
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, nativePositions8Way, weights8Way,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .SampleGroup("8 clips")
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
            
            // 9 clips (8 + idle)
            Measure.Method(() =>
            {
                for (int i = 0; i < IterationsPerMeasurement; i++)
                {
                    var input = SampleInputs[i % SampleInputs.Length];
                    Directional2DBlendUtils.CalculateWeights(
                        input, nativePositions8WayIdle, weights8WayIdle,
                        Blend2DAlgorithm.SimpleDirectional);
                }
            })
            .SampleGroup("9 clips")
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();
        }
        
        #endregion
        
        #region Correctness Verification (Sanity Checks)
        
        /// <summary>
        /// Verifies weights sum to 1.0 and are non-negative.
        /// Not a performance test, but ensures algorithm correctness.
        /// </summary>
        [Test]
        public void WeightsSumToOne_SimpleDirectional()
        {
            foreach (var input in SampleInputs)
            {
                Directional2DBlendUtils.CalculateWeights(
                    input, nativePositions8WayIdle, weights8WayIdle,
                    Blend2DAlgorithm.SimpleDirectional);
                
                float sum = 0f;
                for (int i = 0; i < weights8WayIdle.Length; i++)
                {
                    Assert.GreaterOrEqual(weights8WayIdle[i], 0f, 
                        $"Weight {i} is negative for input {input}");
                    sum += weights8WayIdle[i];
                }
                
                Assert.AreEqual(1f, sum, 0.001f, 
                    $"Weights don't sum to 1.0 for input {input}. Sum: {sum}");
            }
        }
        
        [Test]
        public void WeightsSumToOne_IDW()
        {
            foreach (var input in SampleInputs)
            {
                // Skip center for IDW (would be exact match on idle)
                if (math.lengthsq(input) < 0.001f) continue;
                
                Directional2DBlendUtils.CalculateWeights(
                    input, nativePositions8Way, weights8Way,
                    Blend2DAlgorithm.InverseDistanceWeighting);
                
                float sum = 0f;
                for (int i = 0; i < weights8Way.Length; i++)
                {
                    Assert.GreaterOrEqual(weights8Way[i], 0f, 
                        $"Weight {i} is negative for input {input}");
                    sum += weights8Way[i];
                }
                
                Assert.AreEqual(1f, sum, 0.001f, 
                    $"Weights don't sum to 1.0 for input {input}. Sum: {sum}");
            }
        }
        
        #endregion
    }
}
