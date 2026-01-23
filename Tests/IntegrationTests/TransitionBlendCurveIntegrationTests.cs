using System.Collections;
using DMotion.Authoring;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for Transition Blend Curve feature.
    /// Tests the full pipeline: Authoring → Conversion → Runtime evaluation.
    /// </summary>
    public class TransitionBlendCurveIntegrationTests
    {
        private const float Tolerance = 0.02f; // 2% tolerance for float comparisons
        
        #region Blob Conversion Tests
        
        [Test]
        public void BlobConversion_LinearCurve_ProducesEmptyKeyframes()
        {
            // Create a state machine with linear blend curve (default)
            var stateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            var stateA = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            var stateB = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            stateA.name = "StateA";
            stateB.name = "StateB";
            
            stateMachine.States.Add(stateA);
            stateMachine.States.Add(stateB);
            stateMachine.DefaultState = stateA;
            
            // Add transition with default linear curve
            stateA.OutTransitions.Add(new StateOutTransition(stateB, 0.25f)
            {
                BlendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f) // Default linear
            });
            
            // Build blob
            var blob = AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachine);
            
            try
            {
                // Verify: linear curve should produce empty keyframes (fast-path)
                Assert.AreEqual(2, blob.Value.States.Length, "Should have 2 states");
                Assert.AreEqual(1, blob.Value.States[0].Transitions.Length, "StateA should have 1 transition");
                
                ref var transition = ref blob.Value.States[0].Transitions[0];
                Assert.IsFalse(transition.HasCurve, "Linear curve should NOT have keyframes (fast-path)");
                Assert.AreEqual(0, transition.CurveKeyframes.Length, "Keyframes should be empty");
            }
            finally
            {
                blob.Dispose();
                Object.DestroyImmediate(stateMachine);
                Object.DestroyImmediate(stateA);
                Object.DestroyImmediate(stateB);
            }
        }
        
        [Test]
        public void BlobConversion_EaseInOutCurve_ProducesKeyframes()
        {
            var stateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            var stateA = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            var stateB = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            stateA.name = "StateA";
            stateB.name = "StateB";
            
            stateMachine.States.Add(stateA);
            stateMachine.States.Add(stateB);
            stateMachine.DefaultState = stateA;
            
            // Add transition with ease-in-out curve
            var easeInOut = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            stateA.OutTransitions.Add(new StateOutTransition(stateB, 0.25f)
            {
                BlendCurve = easeInOut
            });
            
            var blob = AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachine);
            
            try
            {
                ref var transition = ref blob.Value.States[0].Transitions[0];
                Assert.IsTrue(transition.HasCurve, "Ease-in-out curve SHOULD have keyframes");
                Assert.AreEqual(2, transition.CurveKeyframes.Length, "Should have 2 keyframes");
                
                // Verify Y-axis inversion (Unity: From weight, DMotion: To weight)
                // First keyframe: Unity (0, 1) → DMotion (0, 0)
                Assert.AreEqual(0f, transition.CurveKeyframes[0].Time, Tolerance, "First keyframe time");
                Assert.AreEqual(0f, transition.CurveKeyframes[0].Value, Tolerance, "First keyframe value (inverted)");
                
                // Last keyframe: Unity (1, 0) → DMotion (1, 1)
                Assert.AreEqual(1f, transition.CurveKeyframes[1].Time, Tolerance, "Last keyframe time");
                Assert.AreEqual(1f, transition.CurveKeyframes[1].Value, Tolerance, "Last keyframe value (inverted)");
            }
            finally
            {
                blob.Dispose();
                Object.DestroyImmediate(stateMachine);
                Object.DestroyImmediate(stateA);
                Object.DestroyImmediate(stateB);
            }
        }
        
        [Test]
        public void BlobConversion_AnyStateTransition_PreservesCurve()
        {
            var stateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            var stateA = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            stateA.name = "StateA";
            
            stateMachine.States.Add(stateA);
            stateMachine.DefaultState = stateA;
            
            // Add Any State transition with custom curve
            var customCurve = new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),   // Ease-in start
                new Keyframe(1f, 0f, -2f, -2f)  // Fast end
            );
            stateMachine.AnyStateTransitions.Add(new StateOutTransition(stateA, 0.3f)
            {
                BlendCurve = customCurve,
                CanTransitionToSelf = true
            });
            
            var blob = AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachine);
            
            try
            {
                Assert.AreEqual(1, blob.Value.AnyStateTransitions.Length, "Should have 1 Any State transition");
                
                ref var anyTransition = ref blob.Value.AnyStateTransitions[0];
                Assert.IsTrue(anyTransition.HasCurve, "Any State transition should have curve");
                Assert.AreEqual(2, anyTransition.CurveKeyframes.Length, "Should have 2 keyframes");
            }
            finally
            {
                blob.Dispose();
                Object.DestroyImmediate(stateMachine);
                Object.DestroyImmediate(stateA);
            }
        }
        
        #endregion
        
        #region Curve Evaluation Accuracy Tests
        
        [Test]
        public void CurveEvaluation_MatchesUnityEvaluate_ForEaseInOut()
        {
            // Create ease-in-out curve and convert
            var unityCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var keyframes = CurveUtils.ConvertAnimationCurveManaged(unityCurve);
            
            Assert.IsNotNull(keyframes, "Ease-in-out should produce keyframes");
            
            // Compare at various points
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                // Unity gives "From" weight, we invert to "To" weight
                float unityValue = unityCurve.Evaluate(t);
                float expectedToWeight = 1f - unityValue;
                
                float actualToWeight = CurveUtils.EvaluateCurveManaged(keyframes, t);
                
                Assert.AreEqual(expectedToWeight, actualToWeight, Tolerance, 
                    $"Curve evaluation mismatch at t={t}: expected {expectedToWeight}, got {actualToWeight}");
            }
        }
        
        [Test]
        public void CurveEvaluation_LinearFastPath_MatchesLinearInterpolation()
        {
            // Empty keyframes = linear fast-path
            CurveKeyframe[] emptyKeyframes = null;
            
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float result = CurveUtils.EvaluateCurveManaged(emptyKeyframes, t);
                Assert.AreEqual(t, result, Tolerance, $"Linear fast-path should return t directly at t={t}");
            }
        }
        
        #endregion
        
        #region Blob vs Managed Consistency Tests
        
        [Test]
        public void BlobEvaluation_MatchesManagedEvaluation()
        {
            var stateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            var stateA = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            var stateB = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            stateA.name = "StateA";
            stateB.name = "StateB";
            
            stateMachine.States.Add(stateA);
            stateMachine.States.Add(stateB);
            stateMachine.DefaultState = stateA;
            
            // Use ease-in-out for non-trivial curve
            var easeInOut = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            stateA.OutTransitions.Add(new StateOutTransition(stateB, 0.25f)
            {
                BlendCurve = easeInOut
            });
            
            var blob = AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachine);
            var managedKeyframes = CurveUtils.ConvertAnimationCurveManaged(easeInOut);
            
            try
            {
                ref var blobKeyframes = ref blob.Value.States[0].Transitions[0].CurveKeyframes;
                
                // Compare evaluation at various points
                for (float t = 0f; t <= 1f; t += 0.05f)
                {
                    float blobResult = CurveUtils.EvaluateCurve(ref blobKeyframes, t);
                    float managedResult = CurveUtils.EvaluateCurveManaged(managedKeyframes, t);
                    
                    Assert.AreEqual(managedResult, blobResult, 0.001f, 
                        $"Blob and managed evaluation should match at t={t}");
                }
            }
            finally
            {
                blob.Dispose();
                Object.DestroyImmediate(stateMachine);
                Object.DestroyImmediate(stateA);
                Object.DestroyImmediate(stateB);
            }
        }
        
        #endregion
        
        #region CurveKeyframe Packing Tests
        
        [Test]
        public void CurveKeyframe_PacksAndUnpacksCorrectly()
        {
            // Test various values
            var testCases = new[]
            {
                (time: 0f, value: 0f, inTan: 0f, outTan: 0f),
                (time: 1f, value: 1f, inTan: 1f, outTan: 1f),
                (time: 0.5f, value: 0.5f, inTan: -1f, outTan: -1f),
                (time: 0.25f, value: 0.75f, inTan: 2f, outTan: -2f),
                (time: 0.9f, value: 0.1f, inTan: -5f, outTan: 5f),
            };
            
            foreach (var (time, value, inTan, outTan) in testCases)
            {
                var keyframe = CurveKeyframe.Create(time, value, inTan, outTan);
                
                // Time and Value should round-trip within byte precision (1/255 ≈ 0.004)
                Assert.AreEqual(time, keyframe.Time, 0.005f, $"Time packing failed for {time}");
                Assert.AreEqual(value, keyframe.Value, 0.005f, $"Value packing failed for {value}");
                
                // Tangents have lower precision (scaled by 10, sbyte range)
                Assert.AreEqual(inTan, keyframe.InTangent, 0.15f, $"InTangent packing failed for {inTan}");
                Assert.AreEqual(outTan, keyframe.OutTangent, 0.15f, $"OutTangent packing failed for {outTan}");
            }
        }
        
        [Test]
        public void CurveKeyframe_Size_Is4Bytes()
        {
            // Verify packed size for memory efficiency
            Assert.AreEqual(4, System.Runtime.InteropServices.Marshal.SizeOf<CurveKeyframe>(),
                "CurveKeyframe should be 4 bytes (1 byte each for time, value, inTangent, outTangent)");
        }
        
        #endregion
        
        #region Edge Cases
        
        [Test]
        public void Evaluation_HandlesZeroDurationSegment()
        {
            // Two keyframes at same time (degenerate segment)
            var keyframes = new[]
            {
                CurveKeyframe.Create(0.5f, 0.3f, 0f, 1f),
                CurveKeyframe.Create(0.5f, 0.7f, 1f, 0f)  // Same time!
            };
            
            // Should not crash, return first keyframe's value
            float result = CurveUtils.EvaluateCurveManaged(keyframes, 0.5f);
            Assert.AreEqual(0.3f, result, Tolerance, "Zero-duration segment should return first keyframe value");
        }
        
        [Test]
        public void Evaluation_ClampsOvershootingCurve()
        {
            // Curve with extreme tangents that could overshoot [0,1]
            var keyframes = new[]
            {
                CurveKeyframe.Create(0f, 0f, 0f, 10f),   // Extreme out tangent
                CurveKeyframe.Create(1f, 1f, 10f, 0f)    // Extreme in tangent
            };
            
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                float result = CurveUtils.EvaluateCurveManaged(keyframes, t);
                Assert.GreaterOrEqual(result, 0f, $"Result should be >= 0 at t={t}");
                Assert.LessOrEqual(result, 1f, $"Result should be <= 1 at t={t}");
            }
        }
        
        #endregion
    }
}
