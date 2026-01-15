using DMotion.Editor.UnityControllerBridge.Core;
using NUnit.Framework;
using System.Linq;

namespace DMotion.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for Unity Controller Bridge conversion engine.
    /// These tests use mock data and can run without Unity Editor.
    /// </summary>
    [TestFixture]
    public class ControllerConverterTests
    {
        private ConversionEngine _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ConversionEngine(new ConversionOptions
            {
                IncludeAnimationEvents = true,
                PreserveGraphLayout = true,
                LogWarnings = true
            });
        }

        #region Parameter Conversion Tests

        [Test]
        public void ConvertParameters_FloatParameter_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData
            {
                Name = "Speed",
                Type = ParameterType.Float,
                DefaultFloat = 1.5f
            });

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Parameters.Count);
            var param = result.Parameters[0];
            Assert.AreEqual("Speed", param.Name);
            Assert.AreEqual(DMotionParameterType.Float, param.TargetType);
            Assert.AreEqual(1.5f, param.DefaultFloatValue);
        }

        [Test]
        public void ConvertParameters_BoolParameter_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData
            {
                Name = "IsGrounded",
                Type = ParameterType.Bool,
                DefaultBool = true
            });

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Parameters.Count);
            var param = result.Parameters[0];
            Assert.AreEqual("IsGrounded", param.Name);
            Assert.AreEqual(DMotionParameterType.Bool, param.TargetType);
            Assert.IsTrue(param.DefaultBoolValue);
        }

        [Test]
        public void ConvertParameters_IntParameter_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData
            {
                Name = "StateIndex",
                Type = ParameterType.Int,
                DefaultInt = 5
            });

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Parameters.Count);
            var param = result.Parameters[0];
            Assert.AreEqual("StateIndex", param.Name);
            Assert.AreEqual(DMotionParameterType.Int, param.TargetType);
            Assert.AreEqual(5, param.DefaultIntValue);
        }

        [Test]
        public void ConvertParameters_TriggerParameter_ConvertsToBool()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData
            {
                Name = "Jump",
                Type = ParameterType.Trigger
            });

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Parameters.Count);
            var param = result.Parameters[0];
            Assert.AreEqual("Jump", param.Name);
            Assert.AreEqual(DMotionParameterType.Bool, param.TargetType);
            Assert.IsFalse(param.DefaultBoolValue);

            // Should have warning about trigger conversion
            Assert.Greater(_engine.Log.WarningCount, 0);
            Assert.IsTrue(_engine.Log.Messages.Any(m => m.Text.Contains("Trigger")));
        }

        [Test]
        public void ConvertParameters_MultipleParameters_ConvertsAll()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData { Name = "Speed", Type = ParameterType.Float });
            controller.Parameters.Add(new ParameterData { Name = "Jump", Type = ParameterType.Bool });
            controller.Parameters.Add(new ParameterData { Name = "StateID", Type = ParameterType.Int });

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.Parameters.Count);
            Assert.IsTrue(result.Parameters.Any(p => p.Name == "Speed"));
            Assert.IsTrue(result.Parameters.Any(p => p.Name == "Jump"));
            Assert.IsTrue(result.Parameters.Any(p => p.Name == "StateID"));
        }

        #endregion

        #region State Conversion Tests

        [Test]
        public void ConvertState_SingleClipState_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            var state = new StateData
            {
                Name = "Idle",
                Speed = 1.0f,
                Motion = new ClipMotionData
                {
                    Name = "IdleClip",
                    Clip = null // Mock: would be Unity AnimationClip
                }
            };
            controller.Layers[0].StateMachine.States.Add(state);
            controller.Layers[0].StateMachine.DefaultStateName = "Idle";

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.States.Count);
            var convertedState = result.States[0];
            Assert.AreEqual("Idle", convertedState.Name);
            Assert.AreEqual(ConvertedStateType.SingleClip, convertedState.StateType);
            Assert.AreEqual("IdleClip", convertedState.ClipName);
            Assert.AreEqual(1.0f, convertedState.Speed);
        }

        [Test]
        public void ConvertState_BlendTree1D_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData { Name = "Speed", Type = ParameterType.Float });

            var blendTree = new BlendTree1DData
            {
                Name = "Locomotion",
                BlendParameter = "Speed"
            };
            blendTree.Children.Add(new BlendTreeChildData
            {
                Threshold = 0f,
                TimeScale = 1f,
                Motion = new ClipMotionData { Name = "Idle", Clip = null }
            });
            blendTree.Children.Add(new BlendTreeChildData
            {
                Threshold = 5f,
                TimeScale = 1f,
                Motion = new ClipMotionData { Name = "Run", Clip = null }
            });

            var state = new StateData
            {
                Name = "Movement",
                Speed = 1.0f,
                Motion = blendTree
            };
            controller.Layers[0].StateMachine.States.Add(state);

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.States.Count);
            var convertedState = result.States[0];
            Assert.AreEqual("Movement", convertedState.Name);
            Assert.AreEqual(ConvertedStateType.LinearBlend, convertedState.StateType);
            Assert.AreEqual("Speed", convertedState.BlendParameterName);
            Assert.AreEqual(2, convertedState.BlendClips.Count);
            Assert.AreEqual(0f, convertedState.BlendClips[0].Threshold);
            Assert.AreEqual(5f, convertedState.BlendClips[1].Threshold);
        }

        [Test]
        public void ConvertState_WithoutMotion_LogsWarning()
        {
            var controller = CreateMockController();
            var state = new StateData
            {
                Name = "EmptyState",
                Motion = null
            };
            controller.Layers[0].StateMachine.States.Add(state);

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.States.Count); // State skipped
            Assert.Greater(_engine.Log.WarningCount, 0);
            Assert.IsTrue(_engine.Log.Messages.Any(m => m.Text.Contains("EmptyState") && m.Text.Contains("no motion")));
        }

        #endregion

        #region Transition Conversion Tests

        [Test]
        public void ConvertTransition_BoolCondition_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData { Name = "IsGrounded", Type = ParameterType.Bool });

            var idleState = CreateMockState("Idle");
            var jumpState = CreateMockState("Jump");

            idleState.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Jump",
                Duration = 0.25f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData
                    {
                        ParameterName = "IsGrounded",
                        Mode = ConditionMode.IfNot
                    }
                }
            });

            controller.Layers[0].StateMachine.States.Add(idleState);
            controller.Layers[0].StateMachine.States.Add(jumpState);

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            var convertedIdle = result.States.First(s => s.Name == "Idle");
            Assert.AreEqual(1, convertedIdle.Transitions.Count);
            var transition = convertedIdle.Transitions[0];
            Assert.AreEqual("Jump", transition.DestinationStateName);
            Assert.AreEqual(0.25f, transition.Duration);
            Assert.AreEqual(1, transition.Conditions.Count);
            var condition = transition.Conditions[0];
            Assert.AreEqual(ConvertedConditionType.Bool, condition.ConditionType);
            Assert.IsFalse(condition.BoolValue); // IfNot = false
        }

        [Test]
        public void ConvertTransition_IntCondition_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            controller.Parameters.Add(new ParameterData { Name = "StateIndex", Type = ParameterType.Int });

            var state1 = CreateMockState("State1");
            var state2 = CreateMockState("State2");

            state1.Transitions.Add(new TransitionData
            {
                DestinationStateName = "State2",
                Duration = 0.1f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData
                    {
                        ParameterName = "StateIndex",
                        Mode = ConditionMode.Equals,
                        Threshold = 2f
                    }
                }
            });

            controller.Layers[0].StateMachine.States.Add(state1);
            controller.Layers[0].StateMachine.States.Add(state2);

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            var convertedState1 = result.States.First(s => s.Name == "State1");
            var transition = convertedState1.Transitions[0];
            var condition = transition.Conditions[0];
            Assert.AreEqual(ConvertedConditionType.Int, condition.ConditionType);
            Assert.AreEqual(ConvertedIntConditionMode.Equal, condition.IntMode);
            Assert.AreEqual(2, condition.IntValue);
        }

        [Test]
        public void ConvertTransition_ExitTime_ConvertsCorrectly()
        {
            var controller = CreateMockController();
            var state1 = CreateMockState("State1");
            var state2 = CreateMockState("State2");

            state1.Transitions.Add(new TransitionData
            {
                DestinationStateName = "State2",
                Duration = 0.2f,
                HasExitTime = true,
                ExitTime = 0.8f // 80% through animation
            });

            controller.Layers[0].StateMachine.States.Add(state1);
            controller.Layers[0].StateMachine.States.Add(state2);

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            var convertedState1 = result.States.First(s => s.Name == "State1");
            var transition = convertedState1.Transitions[0];
            Assert.IsTrue(transition.HasEndTime);
            Assert.AreEqual(0.8f, transition.NormalizedExitTime);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void Convert_CompleteController_ConvertsSuccessfully()
        {
            var controller = CreateComplexMockController();

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("TestController", result.ControllerName);
            Assert.AreEqual(3, result.Parameters.Count); // Speed, Jump, Grounded
            Assert.AreEqual(3, result.States.Count); // Idle, Walk, Jump
            Assert.AreEqual("Idle", result.DefaultStateName);

            // Verify log
            Assert.AreEqual(0, _engine.Log.ErrorCount, "Should have no errors");

            // Print log for inspection
            UnityEngine.Debug.Log(_engine.Log.ToString());
        }

        [Test]
        public void Convert_ControllerWithMultipleLayers_LogsWarning()
        {
            var controller = CreateMockController();
            controller.Layers.Add(new LayerData
            {
                Name = "UpperBody",
                StateMachine = new StateMachineData()
            });

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.Greater(_engine.Log.WarningCount, 0);
            Assert.IsTrue(_engine.Log.Messages.Any(m => m.Text.Contains("layers")));
        }

        #endregion

        #region Helper Methods

        private ControllerData CreateMockController()
        {
            return new ControllerData
            {
                Name = "TestController",
                Layers = new System.Collections.Generic.List<LayerData>
                {
                    new LayerData
                    {
                        Name = "Base Layer",
                        StateMachine = new StateMachineData()
                    }
                }
            };
        }

        private StateData CreateMockState(string name)
        {
            return new StateData
            {
                Name = name,
                Speed = 1.0f,
                Motion = new ClipMotionData
                {
                    Name = $"{name}Clip",
                    Clip = null
                }
            };
        }

        private ControllerData CreateComplexMockController()
        {
            var controller = CreateMockController();

            // Parameters
            controller.Parameters.Add(new ParameterData { Name = "Speed", Type = ParameterType.Float });
            controller.Parameters.Add(new ParameterData { Name = "Jump", Type = ParameterType.Bool });
            controller.Parameters.Add(new ParameterData { Name = "Grounded", Type = ParameterType.Bool, DefaultBool = true });

            // States
            var idle = CreateMockState("Idle");
            var walk = CreateMockState("Walk");
            var jump = CreateMockState("Jump");

            // Transitions
            idle.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Jump",
                Duration = 0.1f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "Jump", Mode = ConditionMode.If }
                }
            });

            jump.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Idle",
                Duration = 0.2f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "Grounded", Mode = ConditionMode.If }
                }
            });

            controller.Layers[0].StateMachine.States.Add(idle);
            controller.Layers[0].StateMachine.States.Add(walk);
            controller.Layers[0].StateMachine.States.Add(jump);
            controller.Layers[0].StateMachine.DefaultStateName = "Idle";

            return controller;
        }

        #endregion
    }
}
