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

        #region 8-Way Locomotion Tests

        [Test]
        public void Handle8WaySetup_CardinalDirections()
        {
            using (var test = new Test2DBlend(9))
            {
                // Standard 8-way + idle setup
                test.Positions[0] = new float2(0, 0);      // Idle
                test.Positions[1] = new float2(0, 1);      // N
                test.Positions[2] = new float2(1, 1);      // NE
                test.Positions[3] = new float2(1, 0);      // E
                test.Positions[4] = new float2(1, -1);     // SE
                test.Positions[5] = new float2(0, -1);     // S
                test.Positions[6] = new float2(-1, -1);    // SW
                test.Positions[7] = new float2(-1, 0);     // W
                test.Positions[8] = new float2(-1, 1);     // NW

                // Test North direction - should primarily use North clip
                test.Calculate(new float2(0, 1));
                Assert.AreEqual(1f, test.Weights[1], 0.01f, "North input should use North clip");

                // Weights should sum to 1
                float sum = 0;
                for (int i = 0; i < 9; i++) sum += test.Weights[i];
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        [Test]
        public void Handle8WaySetup_DiagonalDirections()
        {
            using (var test = new Test2DBlend(9))
            {
                // Standard 8-way + idle setup
                test.Positions[0] = new float2(0, 0);      // Idle
                test.Positions[1] = new float2(0, 1);      // N
                test.Positions[2] = new float2(1, 1);      // NE
                test.Positions[3] = new float2(1, 0);      // E
                test.Positions[4] = new float2(1, -1);     // SE
                test.Positions[5] = new float2(0, -1);     // S
                test.Positions[6] = new float2(-1, -1);    // SW
                test.Positions[7] = new float2(-1, 0);     // W
                test.Positions[8] = new float2(-1, 1);     // NW

                // Test NE direction - should primarily use NE clip
                test.Calculate(math.normalize(new float2(1, 1)));
                Assert.Greater(test.Weights[2], 0.8f, "NE input should primarily use NE clip");

                // Weights should sum to 1
                float sum = 0;
                for (int i = 0; i < 9; i++) sum += test.Weights[i];
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        #endregion

        #region Non-Unit Distance Tests

        [Test]
        public void HandleClips_At_VaryingDistances()
        {
            using (var test = new Test2DBlend(4))
            {
                // Clips at different distances from origin
                test.Positions[0] = new float2(2, 0);      // East at distance 2
                test.Positions[1] = new float2(0, 0.5f);   // North at distance 0.5
                test.Positions[2] = new float2(-1, 0);     // West at distance 1
                test.Positions[3] = new float2(0, -3);     // South at distance 3

                test.Calculate(new float2(1, 0));

                // Should still work - East clip should have most weight
                Assert.Greater(test.Weights[0], 0.5f, "East direction should use East clip");
                
                float sum = 0;
                for (int i = 0; i < 4; i++) sum += test.Weights[i];
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        [Test]
        public void HandleClips_WithIdleAndVaryingDistances()
        {
            using (var test = new Test2DBlend(3))
            {
                test.Positions[0] = new float2(0, 0);      // Idle
                test.Positions[1] = new float2(2, 0);      // East at distance 2
                test.Positions[2] = new float2(0, 0.5f);   // North at distance 0.5

                // Small magnitude pointing East - should blend with idle
                test.Calculate(new float2(0.5f, 0));
                
                Assert.Greater(test.Weights[0], 0.5f, "Small magnitude should have significant idle weight");
                Assert.Greater(test.Weights[1], 0.1f, "Should have some East weight");
                
                float sum = 0;
                for (int i = 0; i < 3; i++) sum += test.Weights[i];
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        #endregion

        #region Degenerate Case Tests

        [Test]
        public void HandleClips_AllAtSameAngle()
        {
            using (var test = new Test2DBlend(3))
            {
                // All clips in East direction at different distances
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(2, 0);
                test.Positions[2] = new float2(3, 0);

                test.Calculate(new float2(1, 0));

                // Should still sum to 1 and not crash
                float sum = 0;
                for (int i = 0; i < 3; i++) sum += test.Weights[i];
                Assert.AreEqual(1f, sum, 0.01f, "Weights should sum to 1 even with degenerate setup");
            }
        }

        [Test]
        public void HandleClips_VeryCloseAngles()
        {
            using (var test = new Test2DBlend(3))
            {
                // Clips very close together in angle
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(1, 0.01f);  // Almost East
                test.Positions[2] = new float2(1, -0.01f); // Almost East

                test.Calculate(new float2(1, 0.005f));

                // Should handle gracefully
                float sum = 0;
                for (int i = 0; i < 3; i++)
                {
                    sum += test.Weights[i];
                    Assert.GreaterOrEqual(test.Weights[i], 0f, $"Weight {i} should not be negative");
                }
                Assert.AreEqual(1f, sum, 0.01f);
            }
        }

        [Test]
        public void HandleEmptyPositionsArray()
        {
            using (var test = new Test2DBlend(0))
            {
                // Should not crash with empty array
                test.Calculate(new float2(1, 0));
                // No assertions needed - just verify no crash
            }
        }

        #endregion
        
        #region IDW Algorithm Tests
        
        [Test]
        public void IDW_ReturnFullWeight_For_SingleClip()
        {
            using (var test = new Test2DBlend(1, Blend2DAlgorithm.InverseDistanceWeighting))
            {
                test.Positions[0] = new float2(1, 0);
                test.Calculate(new float2(0.5f, 0.5f));
                Assert.AreEqual(1f, test.Weights[0], 0.0001f);
            }
        }
        
        [Test]
        public void IDW_AllClipsContribute_For_DiagonalInput()
        {
            using (var test = new Test2DBlend(4, Blend2DAlgorithm.InverseDistanceWeighting))
            {
                test.Positions[0] = new float2(1, 0);   // East
                test.Positions[1] = new float2(0, 1);   // North
                test.Positions[2] = new float2(-1, 0);  // West
                test.Positions[3] = new float2(0, -1);  // South
                
                test.Calculate(new float2(0.5f, 0.5f)); // NE diagonal
                
                // All clips should have some weight (unlike SimpleDirectional which only uses 2)
                Assert.Greater(test.Weights[0], 0.1f, "East should have weight");
                Assert.Greater(test.Weights[1], 0.1f, "North should have weight");
                Assert.Greater(test.Weights[2], 0f, "West should have some weight");
                Assert.Greater(test.Weights[3], 0f, "South should have some weight");
                
                // North and East should have more weight (closer)
                Assert.Greater(test.Weights[0], test.Weights[2], "East should have more weight than West");
                Assert.Greater(test.Weights[1], test.Weights[3], "North should have more weight than South");
            }
        }
        
        [Test]
        public void IDW_ExactPosition_Returns_FullWeight()
        {
            using (var test = new Test2DBlend(4, Blend2DAlgorithm.InverseDistanceWeighting))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);
                
                test.Calculate(new float2(1, 0)); // Exactly on East
                
                Assert.AreEqual(1f, test.Weights[0], 0.0001f, "East should have 100%");
                Assert.AreEqual(0f, test.Weights[1], 0.0001f);
                Assert.AreEqual(0f, test.Weights[2], 0.0001f);
                Assert.AreEqual(0f, test.Weights[3], 0.0001f);
            }
        }
        
        [Test]
        public void IDW_WeightsSumToOne()
        {
            using (var test = new Test2DBlend(4, Blend2DAlgorithm.InverseDistanceWeighting))
            {
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);
                
                var inputs = new[]
                {
                    new float2(0.1f, 0.1f),
                    new float2(0.5f, 0.5f),
                    new float2(-0.3f, 0.7f),
                    new float2(0, 0),
                };
                
                foreach (var input in inputs)
                {
                    test.Calculate(input);
                    float sum = 0;
                    for (int i = 0; i < 4; i++) sum += test.Weights[i];
                    Assert.AreEqual(1f, sum, 0.0001f, $"Weights should sum to 1 for input {input}");
                }
            }
        }
        
        [Test]
        public void IDW_CenterInput_DistributesEvenly()
        {
            using (var test = new Test2DBlend(4, Blend2DAlgorithm.InverseDistanceWeighting))
            {
                // All clips at same distance from origin
                test.Positions[0] = new float2(1, 0);
                test.Positions[1] = new float2(0, 1);
                test.Positions[2] = new float2(-1, 0);
                test.Positions[3] = new float2(0, -1);
                
                test.Calculate(new float2(0, 0)); // Center
                
                // All should have equal weight (25% each)
                Assert.AreEqual(0.25f, test.Weights[0], 0.01f);
                Assert.AreEqual(0.25f, test.Weights[1], 0.01f);
                Assert.AreEqual(0.25f, test.Weights[2], 0.01f);
                Assert.AreEqual(0.25f, test.Weights[3], 0.01f);
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
            private readonly Blend2DAlgorithm algorithm;

            public Test2DBlend(int clipCount, Blend2DAlgorithm algorithm = Blend2DAlgorithm.SimpleDirectional)
            {
                Positions = new NativeArray<float2>(clipCount, Allocator.TempJob);
                Weights = new NativeArray<float>(clipCount, Allocator.TempJob);
                this.algorithm = algorithm;
            }

            public void Calculate(float2 input)
            {
                Directional2DBlendUtils.CalculateWeights(input, Positions, Weights, algorithm);
            }

            public void Dispose()
            {
                if (Positions.IsCreated) Positions.Dispose();
                if (Weights.IsCreated) Weights.Dispose();
            }
        }
    }
}
