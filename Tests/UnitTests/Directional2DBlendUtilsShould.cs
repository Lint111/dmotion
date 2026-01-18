using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DMotion.Tests
{
    /// <summary>
    /// Tests for Directional2DBlendUtils.CalculateWeights() algorithm.
    /// Verifies correct weight calculation for Simple Directional 2D blending.
    /// NOTE: Uses TempJob allocator to avoid long-term memory overhead during test runs.
    /// </summary>
    public class Directional2DBlendUtilsShould
    {
        #region Single Clip Tests

        [Test]
        public void ReturnFullWeight_For_SingleClip_AnyInput()
        {
            using (var test = new Test2DBlend(1))
            {
                test.Positions[0] = new float2(1, 0); // East
                test.Calculate(new float2(1, 1));
                Assert.AreEqual(1f, test.Weights[0], 0.0001f, "Single clip should always have weight 1");
            }
        }

        [Test]
        public void ReturnFullWeight_For_SingleClip_OriginInput()
        {
            using (var test = new Test2DBlend(1))
            {
                test.Positions[0] = new float2(1, 0);
                test.Calculate(new float2(0, 0));
                Assert.AreEqual(1f, test.Weights[0], 0.0001f);
            }
        }

        #endregion

        #region Idle Clip Tests

        [Test]
        public void UseIdleClip_When_InputAtOrigin()
        {
            using (var test = new Test2DBlend(3))
            {
                test.Positions[0] = new float2(0, 0);     // Idle at origin
                test.Positions[1] = new float2(1, 0);     // East
                test.Positions[2] = new float2(0, 1);     // North

                test.Calculate(new float2(0, 0));

                Assert.AreEqual(1f, test.Weights[0], 0.0001f, "Idle should be 100%");
                Assert.AreEqual(0f, test.Weights[1], 0.0001f);
                Assert.AreEqual(0f, test.Weights[2], 0.0001f);
            }
        }

        [Test]
        public void UseClosestClip_When_NoIdleAndInputAtOrigin()
        {
            using (var test = new Test2DBlend(2))
            {
                test.Positions[0] = new float2(1, 0);     // East
                test.Positions[1] = new float2(0, 1);     // North

                test.Calculate(new float2(0, 0));

                // Should pick one (both are equidistant, so either is valid)
                Assert.AreEqual(1f, test.Weights[0] + test.Weights[1], 0.0001f, "Weights should sum to 1");
                Assert.True(test.Weights[0] == 1f || test.Weights[1] == 1f);
            }
        }

        #endregion

        #region Cardinal Direction Tests

        [Test]
        public void BlendCorrectly_For_EastDirection()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);      // East
                test.Positions[1] = new float2(0, 1);      // North
                test.Positions[2] = new float2(-1, 0);     // West
                test.Positions[3] = new float2(0, -1);     // South

                test.Calculate(new float2(1, 0));

                // Should be 100% East
                Assert.AreEqual(1f, test.Weights[0], 0.01f, "Should be mostly East");
                Assert.AreEqual(0f, test.Weights[1], 0.01f, "North weight should be near 0");
                Assert.AreEqual(0f, test.Weights[2], 0.01f, "West weight should be near 0");
                Assert.AreEqual(0f, test.Weights[3], 0.01f, "South weight should be near 0");
            }
        }

        [Test]
        public void BlendCorrectly_For_NorthDirection()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(0, 1));

                Assert.AreEqual(0f, test.Weights[0], 0.01f);
                Assert.AreEqual(1f, test.Weights[1], 0.01f, "Should be mostly North");
                Assert.AreEqual(0f, test.Weights[2], 0.01f);
                Assert.AreEqual(0f, test.Weights[3], 0.01f);
            }
        }

        [Test]
        public void BlendCorrectly_For_WestDirection()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(-1, 0));

                Assert.AreEqual(0f, test.Weights[0], 0.01f);
                Assert.AreEqual(0f, test.Weights[1], 0.01f);
                Assert.AreEqual(1f, test.Weights[2], 0.01f, "Should be mostly West");
                Assert.AreEqual(0f, test.Weights[3], 0.01f);
            }
        }

        [Test]
        public void BlendCorrectly_For_SouthDirection()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(0, -1));

                Assert.AreEqual(0f, test.Weights[0], 0.01f);
                Assert.AreEqual(0f, test.Weights[1], 0.01f);
                Assert.AreEqual(0f, test.Weights[2], 0.01f);
                Assert.AreEqual(1f, test.Weights[3], 0.01f, "Should be mostly South");
            }
        }

        #endregion

        #region Diagonal Direction Tests

        [Test]
        public void BlendBetween_NorthAndEast_For_DiagonalInput()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(1, 1));

                float eastWeight = test.Weights[0];
                float northWeight = test.Weights[1];
                float totalWeight = eastWeight + northWeight;

                Assert.AreEqual(1f, totalWeight, 0.01f, "East + North should be ~100%");
                Assert.Greater(eastWeight, 0.3f, "East should have significant weight");
                Assert.Greater(northWeight, 0.3f, "North should have significant weight");
                Assert.AreEqual(0f, test.Weights[2], 0.01f, "West should be 0");
                Assert.AreEqual(0f, test.Weights[3], 0.01f, "South should be 0");
            }
        }

        [Test]
        public void BlendBetween_NorthAndWest_For_DiagonalInput()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(-1, 1));

                float northWeight = test.Weights[1];
                float westWeight = test.Weights[2];
                float totalWeight = northWeight + westWeight;

                Assert.AreEqual(1f, totalWeight, 0.01f);
                Assert.Greater(northWeight, 0.3f);
                Assert.Greater(westWeight, 0.3f);
                Assert.AreEqual(0f, test.Weights[0], 0.01f);
                Assert.AreEqual(0f, test.Weights[3], 0.01f);
            }
        }

        [Test]
        public void BlendBetween_SouthAndWest_For_DiagonalInput()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(-1, -1));

                float southWeight = test.Weights[3];
                float westWeight = test.Weights[2];
                float totalWeight = southWeight + westWeight;

                Assert.AreEqual(1f, totalWeight, 0.01f);
                Assert.Greater(southWeight, 0.3f);
                Assert.Greater(westWeight, 0.3f);
                Assert.AreEqual(0f, test.Weights[0], 0.01f);
                Assert.AreEqual(0f, test.Weights[1], 0.01f);
            }
        }

        [Test]
        public void BlendBetween_SouthAndEast_For_DiagonalInput()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(1, -1));

                float eastWeight = test.Weights[0];
                float southWeight = test.Weights[3];
                float totalWeight = eastWeight + southWeight;

                Assert.AreEqual(1f, totalWeight, 0.01f);
                Assert.Greater(eastWeight, 0.3f);
                Assert.Greater(southWeight, 0.3f);
                Assert.AreEqual(0f, test.Weights[1], 0.01f);
                Assert.AreEqual(0f, test.Weights[2], 0.01f);
            }
        }

        #endregion

        #region Magnitude Blending with Idle Tests

        [Test]
        public void BlendWithIdle_Based_On_InputMagnitude()
        {
            using (var test = new Test2DBlend(3))
            {
                test.Positions[0] = new float2(0, 0);     // Idle
                test.Positions[1] = new float2(1, 0);     // East
                test.Positions[2] = new float2(0, 1);     // North

                test.Calculate(new float2(0.1f, 0));

                float idleWeight = test.Weights[0];
                Assert.Greater(idleWeight, 0.5f, "Idle should have significant weight for small input");
                Assert.Less(idleWeight, 1f, "Idle should not be 100% for non-zero input");
            }
        }

        [Test]
        public void ReduceIdleWeight_As_MagnitudeIncreases()
        {
            using (var test = new Test2DBlend(3))
            {
                test.Positions[0] = new float2(0, 0);
                test.Positions[1] = new float2(1, 0);
                test.Positions[2] = new float2(0, 1);

                test.Calculate(new float2(0.2f, 0));
                float smallIdleWeight = test.Weights[0];

                test.Calculate(new float2(0.8f, 0));
                float largeIdleWeight = test.Weights[0];

                Assert.Greater(smallIdleWeight, largeIdleWeight, "Idle weight should decrease with magnitude");
            }
        }

        #endregion

        #region Weight Sum Tests

        [Test]
        public void AlwaysSumToOne()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                var testInputs = new[]
                {
                    new float2(0, 0),
                    new float2(1, 0),
                    new float2(1, 1),
                    new float2(0.5f, 0.5f),
                    new float2(0.1f, 0.9f),
                    new float2(-0.7f, 0.3f),
                };

                foreach (var input in testInputs)
                {
                    test.Calculate(input);
                    float sum = 0;
                    for (int i = 0; i < 4; i++)
                        sum += test.Weights[i];
                    
                    Assert.AreEqual(1f, sum, 0.0001f, $"Weights should sum to 1 for input {input}");
                }
            }
        }

        [Test]
        public void AllWeightsNonNegative()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                var testInputs = new[]
                {
                    new float2(2, 2),
                    new float2(-3, 1),
                    new float2(0.1f, -0.1f),
                };

                foreach (var input in testInputs)
                {
                    test.Calculate(input);
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.GreaterOrEqual(test.Weights[i], 0f, $"Weight {i} should be >= 0 for input {input}");
                        Assert.LessOrEqual(test.Weights[i], 1f, $"Weight {i} should be <= 1 for input {input}");
                    }
                }
            }
        }

        #endregion

        #region Edge Cases

        [Test]
        public void HandleTwoClips_Correctly()
        {
            using (var test = new Test2DBlend(2))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);

                test.Calculate(new float2(1, 1));

                float sum = test.Weights[0] + test.Weights[1];
                Assert.AreEqual(1f, sum, 0.01f);
                Assert.Greater(test.Weights[0], 0);
                Assert.Greater(test.Weights[1], 0);
            }
        }

        [Test]
        public void HandleLargeInputMagnitude()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(100, 100));

                float sum = 0;
                for (int i = 0; i < 4; i++)
                    sum += test.Weights[i];
                
                Assert.AreEqual(1f, sum, 0.01f, "Should still sum to 1 with large input");
            }
        }

        [Test]
        public void HandleNegativeInputValues()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(-1, -1));

                float sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    sum += test.Weights[i];
                    Assert.GreaterOrEqual(test.Weights[i], 0, "Weight should not be negative");
                }
                
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        [Test]
        public void HandleNormalizedInput()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                var input = math.normalize(new float2(3, 4));
                test.Calculate(input);

                float sum = 0;
                for (int i = 0; i < 4; i++)
                    sum += test.Weights[i];
                
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        #endregion

        #region Symmetry Tests

        [Test]
        public void SymmetricalWeights_For_SymmetricalInputs()
        {
            using (var test = new Test2DBlend(4))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);

                test.Calculate(new float2(0.5f, 0.5f));
                var w1 = new float[4];
                for (int i = 0; i < 4; i++) w1[i] = test.Weights[i];

                test.Calculate(new float2(0.5f, -0.5f));
                
                Assert.AreEqual(w1[0], test.Weights[0], 0.01f, "East should match");
                Assert.AreEqual(w1[1], test.Weights[3], 0.01f, "North should match South");
                Assert.AreEqual(w1[3], test.Weights[1], 0.01f, "South should match North");
            }
        }

        #endregion

        /// <summary>
        /// Test helper - manages allocation/deallocation for each individual test.
        /// Allocates exactly the required array sizes, properly disposes after use.
        /// </summary>
        private class Test2DBlend : System.IDisposable
        {
            public NativeArray<float2> Positions;
            public NativeArray<float> Weights;

            public Test2DBlend(int clipCount)
            {
                Positions = new NativeArray<float2>(clipCount, Allocator.TempJob);
                Weights = new NativeArray<float>(clipCount, Allocator.TempJob);
            }

            public void Calculate(float2 input)
            {
                Directional2DBlendUtils.CalculateWeights(input, Positions, Weights);
            }

            public void Dispose()
            {
                if (Positions.IsCreated) Positions.Dispose();
                if (Weights.IsCreated) Weights.Dispose();
            }
        }
    }
}
