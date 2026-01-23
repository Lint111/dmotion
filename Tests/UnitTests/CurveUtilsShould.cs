using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Tests for CurveUtils Hermite spline evaluation.
    /// Verifies that runtime curve evaluation matches Unity's AnimationCurve.Evaluate().
    /// </summary>
    public class CurveUtilsShould
    {
        private const float Tolerance = 0.01f; // 1% tolerance for float comparisons
        
        #region Empty/Null Keyframes (Linear Fast-Path)
        
        [Test]
        public void ReturnLinearT_When_KeyframesEmpty()
        {
            using var keyframes = new BlobBuilder(Allocator.Temp);
            ref var root = ref keyframes.ConstructRoot<BlobArray<CurveKeyframe>>();
            keyframes.Allocate(ref root, 0);
            var blobRef = keyframes.CreateBlobAssetReference<BlobArray<CurveKeyframe>>(Allocator.Temp);
            
            // Test various t values
            Assert.AreEqual(0f, CurveUtils.EvaluateCurve(ref blobRef.Value, 0f), Tolerance);
            Assert.AreEqual(0.25f, CurveUtils.EvaluateCurve(ref blobRef.Value, 0.25f), Tolerance);
            Assert.AreEqual(0.5f, CurveUtils.EvaluateCurve(ref blobRef.Value, 0.5f), Tolerance);
            Assert.AreEqual(0.75f, CurveUtils.EvaluateCurve(ref blobRef.Value, 0.75f), Tolerance);
            Assert.AreEqual(1f, CurveUtils.EvaluateCurve(ref blobRef.Value, 1f), Tolerance);
            
            blobRef.Dispose();
        }
        
        [Test]
        public void ReturnLinearT_When_KeyframesNull_Managed()
        {
            CurveKeyframe[] keyframes = null;
            
            Assert.AreEqual(0f, CurveUtils.EvaluateCurveManaged(keyframes, 0f), Tolerance);
            Assert.AreEqual(0.5f, CurveUtils.EvaluateCurveManaged(keyframes, 0.5f), Tolerance);
            Assert.AreEqual(1f, CurveUtils.EvaluateCurveManaged(keyframes, 1f), Tolerance);
        }
        
        [Test]
        public void ReturnLinearT_When_KeyframesEmptyArray_Managed()
        {
            var keyframes = new CurveKeyframe[0];
            
            Assert.AreEqual(0f, CurveUtils.EvaluateCurveManaged(keyframes, 0f), Tolerance);
            Assert.AreEqual(0.5f, CurveUtils.EvaluateCurveManaged(keyframes, 0.5f), Tolerance);
            Assert.AreEqual(1f, CurveUtils.EvaluateCurveManaged(keyframes, 1f), Tolerance);
        }
        
        #endregion
        
        #region Single Keyframe (Constant)
        
        [Test]
        public void ReturnConstant_When_SingleKeyframe()
        {
            var keyframes = new[] { CurveKeyframe.Create(0.5f, 0.75f, 0f, 0f) };
            
            // Should return constant value regardless of t
            Assert.AreEqual(0.75f, CurveUtils.EvaluateCurveManaged(keyframes, 0f), Tolerance);
            Assert.AreEqual(0.75f, CurveUtils.EvaluateCurveManaged(keyframes, 0.5f), Tolerance);
            Assert.AreEqual(0.75f, CurveUtils.EvaluateCurveManaged(keyframes, 1f), Tolerance);
        }
        
        #endregion
        
        #region Linear Curve (Two Keyframes, Linear Tangents)
        
        [Test]
        public void MatchUnity_For_LinearCurve_ToWeight()
        {
            // DMotion stores "To" weight (0→1), so create a linear 0→1 curve
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0f, 1f, 1f),  // t=0, value=0, tangent=1
                CurveKeyframe.Create(1f, 1f, 1f, 1f)   // t=1, value=1, tangent=1
            };
            
            // Unity curve for comparison (also 0→1)
            var unityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            
            // Test at various points
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float expected = unityCurve.Evaluate(t);
                float actual = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.AreEqual(expected, actual, Tolerance, $"Mismatch at t={t}");
            }
        }
        
        #endregion
        
        #region Ease-In Curve
        
        [Test]
        public void MatchUnity_For_EaseInCurve()
        {
            // Ease-in: starts slow, ends fast (quadratic-like)
            var unityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            // Modify to be ease-in only
            var keys = unityCurve.keys;
            keys[0].outTangent = 0f;  // Start with zero slope
            keys[1].inTangent = 2f;   // End with steep slope
            unityCurve.keys = keys;
            
            // Convert to DMotion keyframes (already in "To" weight format)
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0f, 0f, 0f),
                CurveKeyframe.Create(1f, 1f, 2f, 2f)
            };
            
            // Test at various points
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float expected = unityCurve.Evaluate(t);
                float actual = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.AreEqual(expected, actual, Tolerance, $"Ease-in mismatch at t={t}");
            }
        }
        
        #endregion
        
        #region Ease-Out Curve
        
        [Test]
        public void MatchUnity_For_EaseOutCurve()
        {
            // Ease-out: starts fast, ends slow
            var unityCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 2f, 2f),  // Start with steep slope
                new Keyframe(1f, 1f, 0f, 0f)   // End with zero slope
            );
            
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0f, 2f, 2f),
                CurveKeyframe.Create(1f, 1f, 0f, 0f)
            };
            
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float expected = unityCurve.Evaluate(t);
                float actual = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.AreEqual(expected, actual, Tolerance, $"Ease-out mismatch at t={t}");
            }
        }
        
        #endregion
        
        #region Ease-In-Out Curve
        
        [Test]
        public void MatchUnity_For_EaseInOutCurve()
        {
            // Standard ease-in-out (S-curve)
            var unityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            var keys = unityCurve.keys;
            
            var keyframes = new[]
            {
                CurveKeyframe.Create(keys[0].time, keys[0].value, keys[0].inTangent, keys[0].outTangent),
                CurveKeyframe.Create(keys[1].time, keys[1].value, keys[1].inTangent, keys[1].outTangent)
            };
            
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float expected = unityCurve.Evaluate(t);
                float actual = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.AreEqual(expected, actual, Tolerance, $"Ease-in-out mismatch at t={t}");
            }
        }
        
        #endregion
        
        #region Clamping Behavior
        
        [Test]
        public void ClampT_WhenBelowZero()
        {
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0.2f, 0f, 1f),
                CurveKeyframe.Create(1f, 0.8f, 1f, 0f)
            };
            
            // t < 0 should clamp to t = 0
            float atZero = CurveUtils.EvaluateCurveManaged(keyframes, 0f);
            float atNegative = CurveUtils.EvaluateCurveManaged(keyframes, -0.5f);
            Assert.AreEqual(atZero, atNegative, Tolerance, "Negative t should clamp to 0");
        }
        
        [Test]
        public void ClampT_WhenAboveOne()
        {
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0.2f, 0f, 1f),
                CurveKeyframe.Create(1f, 0.8f, 1f, 0f)
            };
            
            // t > 1 should clamp to t = 1
            float atOne = CurveUtils.EvaluateCurveManaged(keyframes, 1f);
            float atAboveOne = CurveUtils.EvaluateCurveManaged(keyframes, 1.5f);
            Assert.AreEqual(atOne, atAboveOne, Tolerance, "t > 1 should clamp to 1");
        }
        
        [Test]
        public void ClampResult_ToValidWeightRange()
        {
            // Curve with steep tangents that could overshoot
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0f, 0f, 5f),   // Very steep out tangent
                CurveKeyframe.Create(1f, 1f, 5f, 0f)    // Very steep in tangent
            };
            
            // Result should always be clamped to [0, 1]
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                float result = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.GreaterOrEqual(result, 0f, $"Result at t={t} should be >= 0");
                Assert.LessOrEqual(result, 1f, $"Result at t={t} should be <= 1");
            }
        }
        
        #endregion
        
        #region Multi-Segment Curves
        
        [Test]
        public void HandleMultipleSegments()
        {
            // 3-keyframe curve (2 segments)
            var unityCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 1f),
                new Keyframe(0.5f, 0.8f, 1f, -0.5f),  // Peak at middle
                new Keyframe(1f, 0.5f, -0.5f, 0f)
            );
            
            var keys = unityCurve.keys;
            var keyframes = new[]
            {
                CurveKeyframe.Create(keys[0].time, keys[0].value, keys[0].inTangent, keys[0].outTangent),
                CurveKeyframe.Create(keys[1].time, keys[1].value, keys[1].inTangent, keys[1].outTangent),
                CurveKeyframe.Create(keys[2].time, keys[2].value, keys[2].inTangent, keys[2].outTangent)
            };
            
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float expected = unityCurve.Evaluate(t);
                float actual = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.AreEqual(expected, actual, Tolerance, $"Multi-segment mismatch at t={t}");
            }
        }
        
        #endregion
        
        #region Blob vs Managed Consistency
        
        [Test]
        public void BlobAndManaged_ProduceSameResults()
        {
            // Create keyframes
            var managedKeyframes = new[]
            {
                CurveKeyframe.Create(0f, 0f, 0f, 0f),
                CurveKeyframe.Create(1f, 1f, 0f, 0f)
            };
            
            // Create blob version
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BlobArray<CurveKeyframe>>();
            var array = builder.Allocate(ref root, managedKeyframes.Length);
            for (int i = 0; i < managedKeyframes.Length; i++)
            {
                array[i] = managedKeyframes[i];
            }
            var blobRef = builder.CreateBlobAssetReference<BlobArray<CurveKeyframe>>(Allocator.Temp);
            
            // Compare results
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float blobResult = CurveUtils.EvaluateCurve(ref blobRef.Value, t);
                float managedResult = CurveUtils.EvaluateCurveManaged(managedKeyframes, t);
                Assert.AreEqual(blobResult, managedResult, 0.0001f, 
                    $"Blob and managed should produce identical results at t={t}");
            }
            
            blobRef.Dispose();
        }
        
        #endregion
        
        #region ConvertAnimationCurveManaged Tests
        
        [Test]
        public void ConvertAnimationCurve_ReturnsNull_ForLinearCurve()
        {
            var linearCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(linearCurve);
            Assert.IsNull(keyframes, "Linear curve should return null (fast-path)");
        }
        
        [Test]
        public void ConvertAnimationCurve_ReturnsKeyframes_ForCustomCurve()
        {
            var customCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(customCurve);
            Assert.IsNotNull(keyframes, "Custom curve should return keyframes");
            Assert.AreEqual(2, keyframes.Length, "Should have 2 keyframes");
        }
        
        [Test]
        public void ConvertAnimationCurve_InvertsYAxis()
        {
            // Unity stores "From" weight (1→0), DMotion uses "To" weight (0→1)
            var unityCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            // Make it non-linear so it doesn't return null
            var keys = unityCurve.keys;
            keys[0].outTangent = -2f; // Not default -1
            unityCurve.keys = keys;
            
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(unityCurve);
            Assert.IsNotNull(keyframes);
            
            // First keyframe: Unity value=1 → DMotion value=0
            Assert.AreEqual(0f, keyframes[0].Value, Tolerance, "First keyframe should be inverted (1→0)");
            // Last keyframe: Unity value=0 → DMotion value=1
            Assert.AreEqual(1f, keyframes[1].Value, Tolerance, "Last keyframe should be inverted (0→1)");
        }
        
        #endregion
        
        #region IsLinearCurve Detection (via ConvertAnimationCurveManaged)
        
        [Test]
        public void DetectLinear_NullCurve()
        {
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(null);
            Assert.IsNull(keyframes, "Null curve should be treated as linear (fast-path)");
        }
        
        [Test]
        public void DetectLinear_EmptyCurve()
        {
            var emptyCurve = new AnimationCurve();
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(emptyCurve);
            Assert.IsNull(keyframes, "Empty curve should be treated as linear (fast-path)");
        }
        
        [Test]
        public void DetectLinear_DefaultLinearCurve()
        {
            // Unity's default linear: (0,1) → (1,0) with tangent -1
            var defaultLinear = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(defaultLinear);
            Assert.IsNull(keyframes, "Default linear curve should return null (fast-path)");
        }
        
        [Test]
        public void DetectNonLinear_EaseInCurve()
        {
            var easeIn = new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),   // Start slow (tangent 0)
                new Keyframe(1f, 0f, -2f, -2f)  // End fast (tangent -2)
            );
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(easeIn);
            Assert.IsNotNull(keyframes, "Ease-in curve should NOT be detected as linear");
        }
        
        [Test]
        public void DetectNonLinear_EaseOutCurve()
        {
            var easeOut = new AnimationCurve(
                new Keyframe(0f, 1f, -2f, -2f), // Start fast
                new Keyframe(1f, 0f, 0f, 0f)    // End slow
            );
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(easeOut);
            Assert.IsNotNull(keyframes, "Ease-out curve should NOT be detected as linear");
        }
        
        [Test]
        public void DetectNonLinear_EaseInOutCurve()
        {
            var easeInOut = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(easeInOut);
            Assert.IsNotNull(keyframes, "Ease-in-out curve should NOT be detected as linear");
        }
        
        [Test]
        public void DetectNonLinear_ThreeKeyframeCurve()
        {
            var threeKey = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.5f, 0.3f),  // Middle keyframe
                new Keyframe(1f, 0f)
            );
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(threeKey);
            Assert.IsNotNull(keyframes, "3-keyframe curve should NOT be detected as linear");
            Assert.AreEqual(3, keyframes.Length);
        }
        
        [Test]
        public void DetectNonLinear_DifferentEndpoints()
        {
            // Linear slope but different endpoints than default
            var differentEndpoints = AnimationCurve.Linear(0f, 0.8f, 1f, 0.2f);
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(differentEndpoints);
            Assert.IsNotNull(keyframes, "Curve with different endpoints should NOT be detected as linear");
        }
        
        [Test]
        public void DetectNonLinear_SlightlyDifferentTangent()
        {
            // Almost linear but tangent is slightly off
            var almostLinear = new AnimationCurve(
                new Keyframe(0f, 1f, -1f, -1.1f),  // outTangent slightly different
                new Keyframe(1f, 0f, -1f, -1f)
            );
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(almostLinear);
            Assert.IsNotNull(keyframes, "Curve with slightly different tangent should NOT be detected as linear");
        }
        
        #endregion
    }
}
