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

            // Read states
            foreach (var childState in stateMachine.states)
            {
                var stateData = ReadState(childState.state, childState.position);
                if (stateData != null)
                {
                    data.States.Add(stateData);
                }
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
