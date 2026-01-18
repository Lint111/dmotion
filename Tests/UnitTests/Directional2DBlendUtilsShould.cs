using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DMotion.Tests
{
    /// <summary>
    /// Tests for Directional2DBlendUtils.CalculateWeights() algorithm.
    /// Verifies correct weight calculation for Simple Directional 2D blending.
    /// </summary>
    public class Directional2DBlendUtilsShould
    {
        private NativeArray<float2> _positions;
        private NativeArray<float> _weights;

        [SetUp]
        public void Setup()
        {
            // Default allocation - will be resized per test
            _positions = new NativeArray<float2>(0, Allocator.Temp);
            _weights = new NativeArray<float>(0, Allocator.Temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_positions.IsCreated) _positions.Dispose();
            if (_weights.IsCreated) _weights.Dispose();
        }

        #region Single Clip Tests

        [Test]
        public void ReturnFullWeight_For_SingleClip_AnyInput()
        {
            CreateArrays(1);
            _positions[0] = new float2(1, 0); // East

            Directional2DBlendUtils.CalculateWeights(new float2(1, 1), _positions, _weights);

            Assert.AreEqual(1f, _weights[0], 0.0001f, "Single clip should always have weight 1");
        }

        [Test]
        public void ReturnFullWeight_For_SingleClip_OriginInput()
        {
            CreateArrays(1);
            _positions[0] = new float2(1, 0);

            Directional2DBlendUtils.CalculateWeights(new float2(0, 0), _positions, _weights);

            Assert.AreEqual(1f, _weights[0], 0.0001f);
        }

        #endregion

        #region Idle Clip Tests

        [Test]
        public void UseIdleClip_When_InputAtOrigin()
        {
            CreateArrays(3);
            _positions[0] = new float2(0, 0);     // Idle at origin
            _positions[1] = new float2(1, 0);     // East
            _positions[2] = new float2(0, 1);     // North

            Directional2DBlendUtils.CalculateWeights(new float2(0, 0), _positions, _weights);

            Assert.AreEqual(1f, _weights[0], 0.0001f, "Idle should be 100%");
            Assert.AreEqual(0f, _weights[1], 0.0001f);
            Assert.AreEqual(0f, _weights[2], 0.0001f);
        }

        [Test]
        public void UseClosestClip_When_NoIdleAndInputAtOrigin()
        {
            CreateArrays(2);
            _positions[0] = new float2(1, 0);     // East
            _positions[1] = new float2(0, 1);     // North

            Directional2DBlendUtils.CalculateWeights(new float2(0, 0), _positions, _weights);

            // Should pick one (both are equidistant, so either is valid)
            Assert.AreEqual(1f, _weights[0] + _weights[1], 0.0001f, "Weights should sum to 1");
            Assert.True(_weights[0] == 1f || _weights[1] == 1f);
        }

        #endregion

        #region Cardinal Direction Tests

        [Test]
        public void BlendCorrectly_For_EastDirection()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);      // East
            _positions[1] = new float2(0, 1);      // North
            _positions[2] = new float2(-1, 0);     // West
            _positions[3] = new float2(0, -1);     // South

            Directional2DBlendUtils.CalculateWeights(new float2(1, 0), _positions, _weights);

            // Should be 100% East
            Assert.AreEqual(1f, _weights[0], 0.01f, "Should be mostly East");
            Assert.AreEqual(0f, _weights[1], 0.01f, "North weight should be near 0");
            Assert.AreEqual(0f, _weights[2], 0.01f, "West weight should be near 0");
            Assert.AreEqual(0f, _weights[3], 0.01f, "South weight should be near 0");
        }

        [Test]
        public void BlendCorrectly_For_NorthDirection()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);      // East
            _positions[1] = new float2(0, 1);      // North
            _positions[2] = new float2(-1, 0);     // West
            _positions[3] = new float2(0, -1);     // South

            Directional2DBlendUtils.CalculateWeights(new float2(0, 1), _positions, _weights);

            Assert.AreEqual(0f, _weights[0], 0.01f);
            Assert.AreEqual(1f, _weights[1], 0.01f, "Should be mostly North");
            Assert.AreEqual(0f, _weights[2], 0.01f);
            Assert.AreEqual(0f, _weights[3], 0.01f);
        }

        [Test]
        public void BlendCorrectly_For_WestDirection()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);    // West
            _positions[3] = new float2(0, -1);

            Directional2DBlendUtils.CalculateWeights(new float2(-1, 0), _positions, _weights);

            Assert.AreEqual(0f, _weights[0], 0.01f);
            Assert.AreEqual(0f, _weights[1], 0.01f);
            Assert.AreEqual(1f, _weights[2], 0.01f, "Should be mostly West");
            Assert.AreEqual(0f, _weights[3], 0.01f);
        }

        [Test]
        public void BlendCorrectly_For_SouthDirection()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);    // South

            Directional2DBlendUtils.CalculateWeights(new float2(0, -1), _positions, _weights);

            Assert.AreEqual(0f, _weights[0], 0.01f);
            Assert.AreEqual(0f, _weights[1], 0.01f);
            Assert.AreEqual(0f, _weights[2], 0.01f);
            Assert.AreEqual(1f, _weights[3], 0.01f, "Should be mostly South");
        }

        #endregion

        #region Diagonal Direction Tests

        [Test]
        public void BlendBetween_NorthAndEast_For_DiagonalInput()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);      // East
            _positions[1] = new float2(0, 1);      // North
            _positions[2] = new float2(-1, 0);     // West
            _positions[3] = new float2(0, -1);     // South

            Directional2DBlendUtils.CalculateWeights(new float2(1, 1), _positions, _weights);

            // Should blend between East and North roughly equally
            float eastWeight = _weights[0];
            float northWeight = _weights[1];
            float totalWeight = eastWeight + northWeight;

            Assert.AreEqual(1f, totalWeight, 0.01f, "East + North should be ~100%");
            Assert.Greater(eastWeight, 0.3f, "East should have significant weight");
            Assert.Greater(northWeight, 0.3f, "North should have significant weight");
            Assert.AreEqual(0f, _weights[2], 0.01f, "West should be 0");
            Assert.AreEqual(0f, _weights[3], 0.01f, "South should be 0");
        }

        [Test]
        public void BlendBetween_NorthAndWest_For_DiagonalInput()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            Directional2DBlendUtils.CalculateWeights(new float2(-1, 1), _positions, _weights);

            float northWeight = _weights[1];
            float westWeight = _weights[2];
            float totalWeight = northWeight + westWeight;

            Assert.AreEqual(1f, totalWeight, 0.01f);
            Assert.Greater(northWeight, 0.3f);
            Assert.Greater(westWeight, 0.3f);
            Assert.AreEqual(0f, _weights[0], 0.01f);
            Assert.AreEqual(0f, _weights[3], 0.01f);
        }

        [Test]
        public void BlendBetween_SouthAndWest_For_DiagonalInput()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            Directional2DBlendUtils.CalculateWeights(new float2(-1, -1), _positions, _weights);

            float southWeight = _weights[3];
            float westWeight = _weights[2];
            float totalWeight = southWeight + westWeight;

            Assert.AreEqual(1f, totalWeight, 0.01f);
            Assert.Greater(southWeight, 0.3f);
            Assert.Greater(westWeight, 0.3f);
            Assert.AreEqual(0f, _weights[0], 0.01f);
            Assert.AreEqual(0f, _weights[1], 0.01f);
        }

        [Test]
        public void BlendBetween_SouthAndEast_For_DiagonalInput()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            Directional2DBlendUtils.CalculateWeights(new float2(1, -1), _positions, _weights);

            float eastWeight = _weights[0];
            float southWeight = _weights[3];
            float totalWeight = eastWeight + southWeight;

            Assert.AreEqual(1f, totalWeight, 0.01f);
            Assert.Greater(eastWeight, 0.3f);
            Assert.Greater(southWeight, 0.3f);
            Assert.AreEqual(0f, _weights[1], 0.01f);
            Assert.AreEqual(0f, _weights[2], 0.01f);
        }

        #endregion

        #region Magnitude Blending with Idle Tests

        [Test]
        public void BlendWithIdle_Based_On_InputMagnitude()
        {
            CreateArrays(3);
            _positions[0] = new float2(0, 0);     // Idle
            _positions[1] = new float2(1, 0);     // East
            _positions[2] = new float2(0, 1);     // North

            // Small input near idle
            Directional2DBlendUtils.CalculateWeights(new float2(0.1f, 0), _positions, _weights);

            float idleWeight = _weights[0];
            Assert.Greater(idleWeight, 0.5f, "Idle should have significant weight for small input");
            Assert.Less(idleWeight, 1f, "Idle should not be 100% for non-zero input");
        }

        [Test]
        public void ReduceIdleWeight_As_MagnitudeIncreases()
        {
            CreateArrays(3);
            _positions[0] = new float2(0, 0);
            _positions[1] = new float2(1, 0);
            _positions[2] = new float2(0, 1);

            // Small input
            Directional2DBlendUtils.CalculateWeights(new float2(0.2f, 0), _positions, _weights);
            float smallIdleWeight = _weights[0];

            // Larger input in same direction
            Directional2DBlendUtils.CalculateWeights(new float2(0.8f, 0), _positions, _weights);
            float largeIdleWeight = _weights[0];

            Assert.Greater(smallIdleWeight, largeIdleWeight, "Idle weight should decrease with magnitude");
        }

        #endregion

        #region Weight Sum Tests

        [Test]
        public void AlwaysSumToOne()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            // Test various inputs
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
                Directional2DBlendUtils.CalculateWeights(input, _positions, _weights);
                float sum = 0;
                for (int i = 0; i < _weights.Length; i++)
                    sum += _weights[i];
                
                Assert.AreEqual(1f, sum, 0.0001f, $"Weights should sum to 1 for input {input}");
            }
        }

        [Test]
        public void AllWeightsNonNegative()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            var testInputs = new[]
            {
                new float2(2, 2),
                new float2(-3, 1),
                new float2(0.1f, -0.1f),
            };

            foreach (var input in testInputs)
            {
                Directional2DBlendUtils.CalculateWeights(input, _positions, _weights);
                for (int i = 0; i < _weights.Length; i++)
                {
                    Assert.GreaterOrEqual(_weights[i], 0f, $"Weight {i} should be >= 0 for input {input}");
                    Assert.LessOrEqual(_weights[i], 1f, $"Weight {i} should be <= 1 for input {input}");
                }
            }
        }

        #endregion

        #region Edge Cases

        [Test]
        public void HandleTwoClips_Correctly()
        {
            CreateArrays(2);
            _positions[0] = new float2(1, 0);     // East
            _positions[1] = new float2(0, 1);     // North

            Directional2DBlendUtils.CalculateWeights(new float2(1, 1), _positions, _weights);

            float sum = _weights[0] + _weights[1];
            Assert.AreEqual(1f, sum, 0.01f);
            Assert.Greater(_weights[0], 0);
            Assert.Greater(_weights[1], 0);
        }

        [Test]
        public void HandleLargeInputMagnitude()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            // Very large magnitude input
            Directional2DBlendUtils.CalculateWeights(new float2(100, 100), _positions, _weights);

            float sum = 0;
            for (int i = 0; i < _weights.Length; i++)
                sum += _weights[i];
            
            Assert.AreEqual(1f, sum, 0.01f, "Should still sum to 1 with large input");
        }

        [Test]
        public void HandleNegativeInputValues()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            Directional2DBlendUtils.CalculateWeights(new float2(-1, -1), _positions, _weights);

            float sum = 0;
            for (int i = 0; i < _weights.Length; i++)
            {
                sum += _weights[i];
                Assert.GreaterOrEqual(_weights[i], 0, "Weight should not be negative");
            }
            
            Assert.AreEqual(1f, sum, 0.01f);
        }

        [Test]
        public void HandleNormalizedInput()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            // Normalized input
            var input = math.normalize(new float2(3, 4));
            Directional2DBlendUtils.CalculateWeights(input, _positions, _weights);

            float sum = 0;
            for (int i = 0; i < _weights.Length; i++)
                sum += _weights[i];
            
            Assert.AreEqual(1f, sum, 0.01f);
        }

        #endregion

        #region Symmetry Tests

        [Test]
        public void SymmetricalWeights_For_SymmetricalInputs()
        {
            CreateArrays(4);
            _positions[0] = new float2(1, 0);
            _positions[1] = new float2(0, 1);
            _positions[2] = new float2(-1, 0);
            _positions[3] = new float2(0, -1);

            // First quadrant
            Directional2DBlendUtils.CalculateWeights(new float2(0.5f, 0.5f), _positions, _weights);
            var w1 = new float[4];
            for (int i = 0; i < 4; i++) w1[i] = _weights[i];

            // Fourth quadrant (should be mirror)
            Directional2DBlendUtils.CalculateWeights(new float2(0.5f, -0.5f), _positions, _weights);
            
            Assert.AreEqual(w1[0], _weights[0], 0.01f, "East should match");
            Assert.AreEqual(w1[1], _weights[3], 0.01f, "North should match South");
            Assert.AreEqual(w1[3], _weights[1], 0.01f, "South should match North");
        }

        #endregion

        private void CreateArrays(int size)
        {
            if (_positions.IsCreated) _positions.Dispose();
            if (_weights.IsCreated) _weights.Dispose();
            
            _positions = new NativeArray<float2>(size, Allocator.Temp);
            _weights = new NativeArray<float>(size, Allocator.Temp);
        }
    }
}
