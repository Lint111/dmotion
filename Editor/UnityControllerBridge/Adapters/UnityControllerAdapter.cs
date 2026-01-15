using DMotion.Editor.UnityControllerBridge.Core;
using UnityEditor.Animations;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge.Adapters
{
    /// <summary>
    /// Adapter that reads Unity AnimatorController and converts to Unity-agnostic ControllerData.
    /// This is the "view" layer that isolates Unity API dependencies.
    /// </summary>
    public static class UnityControllerAdapter
    {
        /// <summary>
        /// Reads a Unity AnimatorController and extracts data into ControllerData.
        /// </summary>
        public static ControllerData ReadController(AnimatorController controller)
        {
            if (controller == null)
            {
                return null;
            }

            var data = new ControllerData
            {
                Name = controller.name
            };

            // Read parameters
            foreach (var param in controller.parameters)
            {
                data.Parameters.Add(new ParameterData
                {
                    Name = param.name,
                    Type = ConvertParameterType(param.type),
                    DefaultFloat = param.defaultFloat,
                    DefaultInt = param.defaultInt,
                    DefaultBool = param.defaultBool
                });
            }

            // Read layers
            foreach (var layer in controller.layers)
            {
                var layerData = new LayerData
                {
                    Name = layer.name,
                    DefaultWeight = layer.defaultWeight,
                    StateMachine = ReadStateMachine(layer.stateMachine)
                };
                data.Layers.Add(layerData);
            }

            return data;
        }

        private static StateMachineData ReadStateMachine(AnimatorStateMachine stateMachine)
        {
            var data = new StateMachineData();

            if (stateMachine.defaultState != null)
            {
                data.DefaultStateName = stateMachine.defaultState.name;
            }

            // Read regular animation states
            foreach (var childState in stateMachine.states)
            {
                var stateData = ReadState(childState.state, childState.position);
                if (stateData != null)
                {
                    data.States.Add(stateData);
                }
            }

            // NEW: Read sub-state machines (states containing nested machines)
            if (stateMachine.stateMachines != null && stateMachine.stateMachines.Length > 0)
            {
                foreach (var childStateMachine in stateMachine.stateMachines)
                {
                    var subMachineState = ReadSubStateMachine(childStateMachine.stateMachine, childStateMachine.position);
                    if (subMachineState != null)
                    {
                        data.States.Add(subMachineState);
                    }
                }

                UnityEngine.Debug.Log(
                    $"[Unity Controller Bridge] Converted {stateMachine.stateMachines.Length} sub-state machine(s) " +
                    $"(native DMotion support - relationship-based hierarchy)"
                );
            }

            // NEW: Read Any State transitions (pure 1:1 translation, no expansion!)
            if (stateMachine.anyStateTransitions != null && stateMachine.anyStateTransitions.Length > 0)
            {
                foreach (var anyTransition in stateMachine.anyStateTransitions)
                {
                    var transitionData = ReadTransition(anyTransition);
                    if (transitionData != null)
                    {
                        data.AnyStateTransitions.Add(transitionData);
                    }
                }

                UnityEngine.Debug.Log(
                    $"[Unity Controller Bridge] Converted {data.AnyStateTransitions.Count} Any State transition(s) " +
                    $"(native DMotion support - no expansion needed)"
                );
            }

            return data;
        }

        private static StateData ReadState(AnimatorState state, Vector3 position)
        {
            if (state == null)
            {
                return null;
            }

            var data = new StateData
            {
                Name = state.name,
                Speed = state.speed,
                SpeedParameterActive = state.speedParameterActive,
                SpeedParameter = state.speedParameter,
                CycleOffset = state.cycleOffset,
                GraphPosition = new Vector2(position.x, position.y),
                Motion = ReadMotion(state.motion)
            };

            // Read transitions
            foreach (var transition in state.transitions)
            {
                var transitionData = ReadTransition(transition);
                if (transitionData != null)
                {
                    data.Transitions.Add(transitionData);
                }
            }

            return data;
        }

        /// <summary>
        /// Reads a Unity sub-state machine and converts it to StateData with SubStateMachineData.
        /// Supports unlimited depth through recursive ReadStateMachine calls.
        /// </summary>
        private static StateData ReadSubStateMachine(AnimatorStateMachine nestedMachine, Vector3 position)
        {
            if (nestedMachine == null)
            {
                return null;
            }

            // Recursively read the nested state machine
            var nestedMachineData = ReadStateMachine(nestedMachine);

            // Find entry state (Unity's default state)
            string entryStateName = nestedMachineData.DefaultStateName;
            if (string.IsNullOrEmpty(entryStateName))
            {
                Debug.LogWarning($"[Unity Controller Bridge] Sub-state machine '{nestedMachine.name}' has no default state. Using first state as entry.");
                entryStateName = nestedMachineData.States.Count > 0 ? nestedMachineData.States[0].Name : null;
            }

            // Read exit transitions (transitions from Entry state to states outside the sub-machine)
            // Unity represents these as transitions from the nested machine itself
            var exitTransitions = new System.Collections.Generic.List<TransitionData>();
            if (nestedMachine.entryTransitions != null && nestedMachine.entryTransitions.Length > 0)
            {
                foreach (var exitTransition in nestedMachine.entryTransitions)
                {
                    // Entry transitions in Unity are actually exit transitions when looking from parent
                    // These go to states in the PARENT machine
                    var transitionData = ReadTransition(exitTransition);
                    if (transitionData != null)
                    {
                        exitTransitions.Add(transitionData);
                    }
                }
            }

            var subMachineData = new SubStateMachineData
            {
                NestedStateMachine = nestedMachineData,
                EntryStateName = entryStateName,
                ExitTransitions = exitTransitions
            };

            var stateData = new StateData
            {
                Name = nestedMachine.name,
                GraphPosition = new Vector2(position.x, position.y),
                SubStateMachine = subMachineData
            };

            // Transitions from this sub-state machine node to other states in the parent
            // These are stored on the ChildAnimatorStateMachine's parent connection
            // We'll handle these in the conversion phase

            return stateData;
        }

        private static MotionData ReadMotion(Motion motion)
        {
            if (motion == null)
            {
                return null;
            }

            // Check if it's a blend tree
            if (motion is BlendTree blendTree)
            {
                return ReadBlendTree(blendTree);
            }

            // Otherwise it's a regular clip
            if (motion is AnimationClip clip)
            {
                return new ClipMotionData
                {
                    Name = clip.name,
                    Clip = clip
                };
            }

            return null;
        }

        private static MotionData ReadBlendTree(BlendTree blendTree)
        {
            // Only support 1D blend trees for now
            if (blendTree.blendType == BlendTreeType.Simple1D)
            {
                var data = new BlendTree1DData
                {
                    Name = blendTree.name,
                    BlendParameter = blendTree.blendParameter
                };

                foreach (var child in blendTree.children)
                {
                    var childData = new BlendTreeChildData
                    {
                        Threshold = child.threshold,
                        TimeScale = child.timeScale,
                        Motion = ReadMotion(child.motion)
                    };
                    data.Children.Add(childData);
                }

                return data;
            }

            // Unsupported blend tree types return null (engine will log error)
            return null;
        }

        private static TransitionData ReadTransition(AnimatorStateTransition transition)
        {
            if (transition == null)
            {
                return null;
            }

            var data = new TransitionData
            {
                Duration = transition.duration,
                Offset = transition.offset,
                HasExitTime = transition.hasExitTime,
                ExitTime = transition.exitTime,
                HasFixedDuration = transition.hasFixedDuration
            };

            // Destination state
            if (transition.destinationState != null)
            {
                data.DestinationStateName = transition.destinationState.name;
            }
            else if (transition.isExit)
            {
                // Exit transition (not supported)
                return null;
            }

            // Read conditions
            foreach (var condition in transition.conditions)
            {
                data.Conditions.Add(new ConditionData
                {
                    ParameterName = condition.parameter,
                    Mode = (ConditionMode)condition.mode,
                    Threshold = condition.threshold
                });
            }

            return data;
        }

        /// <summary>
        /// Reads an AnimatorTransition (used by Any State transitions).
        /// Similar to AnimatorStateTransition but with slightly different API.
        /// </summary>
        private static TransitionData ReadTransition(AnimatorTransition transition)
        {
            if (transition == null)
            {
                return null;
            }

            var data = new TransitionData
            {
                Duration = transition.duration,
                Offset = transition.offset,
                HasExitTime = transition.hasExitTime,
                ExitTime = transition.exitTime,
                HasFixedDuration = transition.hasFixedDuration
            };

            // Destination state
            if (transition.destinationState != null)
            {
                data.DestinationStateName = transition.destinationState.name;
            }
            else if (transition.isExit)
            {
                // Exit transition (not supported)
                return null;
            }

            // Read conditions
            foreach (var condition in transition.conditions)
            {
                data.Conditions.Add(new ConditionData
                {
                    ParameterName = condition.parameter,
                    Mode = (ConditionMode)condition.mode,
                    Threshold = condition.threshold
                });
            }

            return data;
        }

        private static ParameterType ConvertParameterType(AnimatorControllerParameterType unityType)
        {
            return unityType switch
            {
                AnimatorControllerParameterType.Float => ParameterType.Float,
                AnimatorControllerParameterType.Int => ParameterType.Int,
                AnimatorControllerParameterType.Bool => ParameterType.Bool,
                AnimatorControllerParameterType.Trigger => ParameterType.Trigger,
                _ => ParameterType.Bool
            };
        }
    }
}
