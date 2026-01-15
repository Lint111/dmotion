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

        #region Any State Expansion Tests

        [Test]
        public void AnyStateExpansion_WithAnyStateTransitions_ExpendsToAllStates()
        {
            // This test simulates what UnityControllerAdapter does when it reads Any State transitions
            // In reality, the adapter expands them before passing to the engine
            var controller = CreateMockController();

            // Create three states
            var idle = CreateMockState("Idle");
            var walk = CreateMockState("Walk");
            var run = CreateMockState("Run");

            // Add an "Any State" transition by manually adding it to all states
            // (simulating what ExpandAnyStateTransitions does)
            var anyStateTransition = new TransitionData
            {
                DestinationStateName = "Idle",
                Duration = 0.2f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "Hit", Mode = ConditionMode.If }
                }
            };

            // Add to ALL states (including Idle for self-transition)
            idle.Transitions.Add(new TransitionData
            {
                DestinationStateName = anyStateTransition.DestinationStateName,
                Duration = anyStateTransition.Duration,
                Conditions = new System.Collections.Generic.List<ConditionData>(anyStateTransition.Conditions)
            });

            walk.Transitions.Add(new TransitionData
            {
                DestinationStateName = anyStateTransition.DestinationStateName,
                Duration = anyStateTransition.Duration,
                Conditions = new System.Collections.Generic.List<ConditionData>(anyStateTransition.Conditions)
            });

            run.Transitions.Add(new TransitionData
            {
                DestinationStateName = anyStateTransition.DestinationStateName,
                Duration = anyStateTransition.Duration,
                Conditions = new System.Collections.Generic.List<ConditionData>(anyStateTransition.Conditions)
            });

            controller.Parameters.Add(new ParameterData { Name = "Hit", Type = ParameterType.Bool });
            controller.Layers[0].StateMachine.States.Add(idle);
            controller.Layers[0].StateMachine.States.Add(walk);
            controller.Layers[0].StateMachine.States.Add(run);
            controller.Layers[0].StateMachine.DefaultStateName = "Idle";

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);

            // Verify Walk has the expanded transition
            var walkState = result.States.First(s => s.Name == "Walk");
            Assert.AreEqual(1, walkState.Transitions.Count);
            Assert.AreEqual("Idle", walkState.Transitions[0].DestinationStateName);

            // Verify Run has the expanded transition
            var runState = result.States.First(s => s.Name == "Run");
            Assert.AreEqual(1, runState.Transitions.Count);
            Assert.AreEqual("Idle", runState.Transitions[0].DestinationStateName);

            // Verify Idle HAS self-transition (Unity supports this)
            var idleState = result.States.First(s => s.Name == "Idle");
            Assert.AreEqual(1, idleState.Transitions.Count);
            Assert.AreEqual("Idle", idleState.Transitions[0].DestinationStateName);
        }

        [Test]
        public void AnyStateExpansion_PreservesConditions()
        {
            var controller = CreateMockController();

            var idle = CreateMockState("Idle");
            var combat = CreateMockState("Combat");

            // Simulated expanded Any State transition with conditions
            combat.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Idle",
                Duration = 0.3f,
                HasExitTime = false,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "Health", Mode = ConditionMode.Less, Threshold = 0.1f },
                    new ConditionData { ParameterName = "Dead", Mode = ConditionMode.If }
                }
            });

            controller.Parameters.Add(new ParameterData { Name = "Health", Type = ParameterType.Float });
            controller.Parameters.Add(new ParameterData { Name = "Dead", Type = ParameterType.Bool });
            controller.Layers[0].StateMachine.States.Add(idle);
            controller.Layers[0].StateMachine.States.Add(combat);
            controller.Layers[0].StateMachine.DefaultStateName = "Idle";

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);

            // Verify conditions were preserved
            var combatState = result.States.First(s => s.Name == "Combat");
            Assert.AreEqual(1, combatState.Transitions.Count);

            var transition = combatState.Transitions[0];
            Assert.AreEqual(2, transition.Conditions.Count);

            // Check first condition
            Assert.IsTrue(transition.Conditions.Any(c =>
                c.ParameterIndex >= 0 &&
                c.Comparison.ToString().Contains("Less")));

            // Check second condition
            Assert.IsTrue(transition.Conditions.Any(c =>
                c.ParameterIndex >= 0 &&
                c.Comparison.ToString().Contains("If")));
        }

        [Test]
        public void AnyStateExpansion_MultipleAnyStateTransitions_AllExpanded()
        {
            var controller = CreateMockController();

            var idle = CreateMockState("Idle");
            var walk = CreateMockState("Walk");
            var hit = CreateMockState("Hit");
            var death = CreateMockState("Death");

            // Simulate two Any State transitions expanded to Walk state
            // Any State → Hit (on Hit trigger)
            walk.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Hit",
                Duration = 0.1f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "Hit", Mode = ConditionMode.If }
                }
            });

            // Any State → Death (on Death trigger)
            walk.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Death",
                Duration = 0.05f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "Death", Mode = ConditionMode.If }
                }
            });

            controller.Parameters.Add(new ParameterData { Name = "Hit", Type = ParameterType.Bool });
            controller.Parameters.Add(new ParameterData { Name = "Death", Type = ParameterType.Bool });
            controller.Layers[0].StateMachine.States.Add(idle);
            controller.Layers[0].StateMachine.States.Add(walk);
            controller.Layers[0].StateMachine.States.Add(hit);
            controller.Layers[0].StateMachine.States.Add(death);
            controller.Layers[0].StateMachine.DefaultStateName = "Idle";

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);

            // Verify Walk has both expanded transitions
            var walkState = result.States.First(s => s.Name == "Walk");
            Assert.AreEqual(2, walkState.Transitions.Count);

            Assert.IsTrue(walkState.Transitions.Any(t => t.DestinationStateName == "Hit"));
            Assert.IsTrue(walkState.Transitions.Any(t => t.DestinationStateName == "Death"));
        }

        [Test]
        public void AnyStateExpansion_SelfTransitions_WorkCorrectly()
        {
            var controller = CreateMockController();

            var reload = CreateMockState("Reload");
            var idle = CreateMockState("Idle");

            // Simulate Any State → Reload (useful for interrupt-and-restart reloads)
            // This includes Reload → Reload self-transition
            reload.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Reload",  // Self-transition
                Duration = 0.0f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "StartReload", Mode = ConditionMode.If }
                }
            });

            idle.Transitions.Add(new TransitionData
            {
                DestinationStateName = "Reload",
                Duration = 0.1f,
                Conditions = new System.Collections.Generic.List<ConditionData>
                {
                    new ConditionData { ParameterName = "StartReload", Mode = ConditionMode.If }
                }
            });

            controller.Parameters.Add(new ParameterData { Name = "StartReload", Type = ParameterType.Bool });
            controller.Layers[0].StateMachine.States.Add(reload);
            controller.Layers[0].StateMachine.States.Add(idle);
            controller.Layers[0].StateMachine.DefaultStateName = "Idle";

            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);

            // Verify Reload HAS self-transition (Unity supports this for animation restarts)
            var reloadState = result.States.First(s => s.Name == "Reload");
            Assert.AreEqual(1, reloadState.Transitions.Count, "Reload should have self-transition");
            Assert.AreEqual("Reload", reloadState.Transitions[0].DestinationStateName,
                "Self-transition should loop back to same state");
        }

        [Test]
        public void AnyStateExpansion_WithManyStates_PerformanceTest()
        {
            var controller = CreateMockController();

            // Create 50 states
            for (int i = 0; i < 50; i++)
            {
                var state = CreateMockState($"State{i}");

                // Simulate one Any State transition expanded to ALL states (including State0)
                state.Transitions.Add(new TransitionData
                {
                    DestinationStateName = "State0",
                    Duration = 0.2f,
                    Conditions = new System.Collections.Generic.List<ConditionData>
                    {
                        new ConditionData { ParameterName = "Reset", Mode = ConditionMode.If }
                    }
                });

                controller.Layers[0].StateMachine.States.Add(state);
            }

            controller.Parameters.Add(new ParameterData { Name = "Reset", Type = ParameterType.Bool });
            controller.Layers[0].StateMachine.DefaultStateName = "State0";

            // Conversion should complete without timeout
            var result = _engine.Convert(controller);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(50, result.States.Count);

            // Verify ALL states (including State0) have the transition
            for (int i = 0; i < 50; i++)
            {
                var state = result.States.First(s => s.Name == $"State{i}");
                Assert.AreEqual(1, state.Transitions.Count, $"State{i} should have 1 transition");
                Assert.AreEqual("State0", state.Transitions[0].DestinationStateName);
            }

            // State0 NOW has self-transition (correct Unity behavior)
            var state0 = result.States.First(s => s.Name == "State0");
            Assert.AreEqual(1, state0.Transitions.Count, "State0 should have self-transition");
            Assert.AreEqual("State0", state0.Transitions[0].DestinationStateName);
        }

        #endregion

        #endregion
    }
}
