using System;
using System.Collections.Generic;
using System.Linq;

namespace DMotion.Editor.UnityControllerBridge.Core
{
    /// <summary>
    /// Pure C# conversion engine (Unity-agnostic).
    /// Converts ControllerData to DMotion data structures.
    /// Fully testable without Unity Editor.
    /// </summary>
    public class ConversionEngine
    {
        private readonly ConversionOptions _options;
        private readonly ConversionLog _log;

        public ConversionEngine(ConversionOptions options = null)
        {
            _options = options ?? new ConversionOptions();
            _log = new ConversionLog();
        }

        /// <summary>
        /// Gets the conversion log (warnings, errors, info).
        /// </summary>
        public ConversionLog Log => _log;

        /// <summary>
        /// Converts controller data to DMotion data structures.
        /// Returns conversion result with generated data.
        /// </summary>
        public ConversionResult Convert(ControllerData controller)
        {
            if (controller == null)
            {
                _log.AddError("Controller data is null");
                return new ConversionResult { Success = false };
            }

            var result = new ConversionResult { ControllerName = controller.Name };

            try
            {
                // Phase 1: Convert parameters
                result.Parameters = ConvertParameters(controller.Parameters);

                // Phase 2: Process first layer only (DMotion doesn't support multiple layers yet)
                if (controller.Layers.Count == 0)
                {
                    _log.AddError("Controller has no layers");
                    return new ConversionResult { Success = false };
                }

                if (controller.Layers.Count > 1)
                {
                    _log.AddWarning($"Controller has {controller.Layers.Count} layers. Only first layer will be converted (multiple layers not supported yet)");
                }

                var baseLayer = controller.Layers[0];
                var stateMachine = baseLayer.StateMachine;

                // Phase 3: Convert states
                result.States = ConvertStates(stateMachine.States, result.Parameters);

                // Phase 4: Find default state
                result.DefaultStateName = stateMachine.DefaultStateName;

                // Phase 5: Convert transitions (needs states to be created first)
                ConvertTransitions(stateMachine.States, result.States, result.Parameters);

                result.Success = true;
                _log.AddInfo($"Conversion successful: {result.States.Count} states, {result.Parameters.Count} parameters");
            }
            catch (Exception ex)
            {
                _log.AddError($"Conversion failed: {ex.Message}");
                result.Success = false;
            }

            return result;
        }

        private List<ConvertedParameter> ConvertParameters(List<ParameterData> parameters)
        {
            var converted = new List<ConvertedParameter>();

            foreach (var param in parameters)
            {
                var convertedParam = new ConvertedParameter
                {
                    Name = param.Name,
                    OriginalType = param.Type
                };

                switch (param.Type)
                {
                    case ParameterType.Float:
                        convertedParam.TargetType = DMotionParameterType.Float;
                        convertedParam.DefaultFloatValue = param.DefaultFloat;
                        break;

                    case ParameterType.Int:
                        convertedParam.TargetType = DMotionParameterType.Int;
                        convertedParam.DefaultIntValue = param.DefaultInt;
                        break;

                    case ParameterType.Bool:
                        convertedParam.TargetType = DMotionParameterType.Bool;
                        convertedParam.DefaultBoolValue = param.DefaultBool;
                        break;

                    case ParameterType.Trigger:
                        // Convert trigger to bool
                        convertedParam.TargetType = DMotionParameterType.Bool;
                        convertedParam.DefaultBoolValue = false;
                        _log.AddWarning($"Parameter '{param.Name}' is Trigger type - converted to Bool (auto-reset behavior must be implemented manually)");
                        break;

                    default:
                        _log.AddError($"Unknown parameter type for '{param.Name}'");
                        continue;
                }

                converted.Add(convertedParam);
            }

            return converted;
        }

        private List<ConvertedState> ConvertStates(List<StateData> states, List<ConvertedParameter> parameters)
        {
            var converted = new List<ConvertedState>();

            foreach (var state in states)
            {
                var convertedState = ConvertState(state, parameters);
                if (convertedState != null)
                {
                    converted.Add(convertedState);
                }
            }

            return converted;
        }

        private ConvertedState ConvertState(StateData state, List<ConvertedParameter> parameters)
        {
            if (state.Motion == null)
            {
                _log.AddWarning($"State '{state.Name}' has no motion, skipping");
                return null;
            }

            var converted = new ConvertedState
            {
                Name = state.Name,
                Speed = state.Speed,
                GraphPosition = state.GraphPosition,
                Loop = true // Default, will be refined based on motion type
            };

            // Handle speed parameter
            if (state.SpeedParameterActive && !string.IsNullOrEmpty(state.SpeedParameter))
            {
                _log.AddWarning($"State '{state.Name}' uses speed parameter '{state.SpeedParameter}' - not directly supported, speed will be constant");
            }

            // Handle cycle offset
            if (state.CycleOffset != 0f)
            {
                _log.AddWarning($"State '{state.Name}' has cycle offset {state.CycleOffset} - not supported, will be ignored");
            }

            // Convert motion
            switch (state.Motion.Type)
            {
                case MotionType.Clip:
                    ConvertClipMotion(state.Motion as ClipMotionData, converted);
                    break;

                case MotionType.BlendTree1D:
                    ConvertBlendTree1D(state.Motion as BlendTree1DData, converted, parameters);
                    break;

                case MotionType.BlendTree2D:
                case MotionType.Direct:
                    _log.AddError($"State '{state.Name}' uses {state.Motion.Type} which is not supported yet");
                    return null;

                default:
                    _log.AddError($"State '{state.Name}' has unknown motion type");
                    return null;
            }

            return converted;
        }

        private void ConvertClipMotion(ClipMotionData clipMotion, ConvertedState state)
        {
            state.StateType = ConvertedStateType.SingleClip;
            state.ClipName = clipMotion.Name;
            state.Clip = clipMotion.Clip; // Store Unity clip reference

            if (_options.IncludeAnimationEvents && clipMotion.Clip != null)
            {
                // Extract animation events from Unity clip
                var events = clipMotion.Clip.events;
                foreach (var evt in events)
                {
                    state.AnimationEvents.Add(new ConvertedAnimationEvent
                    {
                        FunctionName = evt.functionName,
                        NormalizedTime = clipMotion.Clip.length > 0 ? evt.time / clipMotion.Clip.length : 0f
                    });
                }

                if (events.Length > 0)
                {
                    _log.AddInfo($"State '{state.Name}': Extracted {events.Length} animation events");
                }
            }
        }

        private void ConvertBlendTree1D(BlendTree1DData blendTree, ConvertedState state, List<ConvertedParameter> parameters)
        {
            state.StateType = ConvertedStateType.LinearBlend;

            // Find blend parameter
            var blendParam = parameters.FirstOrDefault(p => p.Name == blendTree.BlendParameter);
            if (blendParam == null)
            {
                _log.AddError($"State '{state.Name}': Blend parameter '{blendTree.BlendParameter}' not found");
                return;
            }

            if (blendParam.TargetType != DMotionParameterType.Float)
            {
                _log.AddError($"State '{state.Name}': Blend parameter '{blendTree.BlendParameter}' is not Float type");
                return;
            }

            state.BlendParameterName = blendTree.BlendParameter;

            // Convert children
            foreach (var child in blendTree.Children)
            {
                if (child.Motion is ClipMotionData clipMotion)
                {
                    state.BlendClips.Add(new ConvertedBlendClip
                    {
                        ClipName = clipMotion.Name,
                        Clip = clipMotion.Clip,
                        Threshold = child.Threshold,
                        Speed = child.TimeScale
                    });
                }
                else
                {
                    _log.AddWarning($"State '{state.Name}': Blend tree child with non-clip motion not supported, skipping");
                }
            }

            if (state.BlendClips.Count == 0)
            {
                _log.AddError($"State '{state.Name}': Blend tree has no valid clips");
            }

            _log.AddInfo($"State '{state.Name}': Converted 1D blend tree with {state.BlendClips.Count} clips");
        }

        private void ConvertTransitions(List<StateData> sourceStates, List<ConvertedState> convertedStates, List<ConvertedParameter> parameters)
        {
            // Build state name lookup
            var stateNameToConverted = convertedStates.ToDictionary(s => s.Name, s => s);

            foreach (var sourceState in sourceStates)
            {
                if (!stateNameToConverted.TryGetValue(sourceState.Name, out var convertedState))
                {
                    continue; // State wasn't converted (error already logged)
                }

                foreach (var transition in sourceState.Transitions)
                {
                    var convertedTransition = ConvertTransition(transition, sourceState, parameters);
                    if (convertedTransition != null)
                    {
                        convertedState.Transitions.Add(convertedTransition);
                    }
                }
            }
        }

        private ConvertedTransition ConvertTransition(TransitionData transition, StateData sourceState, List<ConvertedParameter> parameters)
        {
            var converted = new ConvertedTransition
            {
                DestinationStateName = transition.DestinationStateName,
                Duration = transition.Duration
            };

            // Handle exit time
            if (transition.HasExitTime)
            {
                // Need to calculate absolute time from normalized exit time
                // This requires knowing the clip duration, which we'll need to resolve later
                converted.HasEndTime = true;
                converted.NormalizedExitTime = transition.ExitTime;
                _log.AddInfo($"Transition from '{sourceState.Name}' to '{transition.DestinationStateName}': Exit time {transition.ExitTime:F2} (will be converted to absolute time during asset creation)");
            }

            // Handle transition offset
            if (transition.Offset != 0f)
            {
                _log.AddWarning($"Transition from '{sourceState.Name}' to '{transition.DestinationStateName}': Offset {transition.Offset:F2} is not supported, will be ignored");
            }

            // Convert conditions
            foreach (var condition in transition.Conditions)
            {
                var convertedCondition = ConvertCondition(condition, parameters, sourceState.Name, transition.DestinationStateName);
                if (convertedCondition != null)
                {
                    converted.Conditions.Add(convertedCondition);
                }
            }

            return converted;
        }

        private ConvertedCondition ConvertCondition(ConditionData condition, List<ConvertedParameter> parameters, string fromState, string toState)
        {
            // Find parameter
            var param = parameters.FirstOrDefault(p => p.Name == condition.ParameterName);
            if (param == null)
            {
                _log.AddError($"Transition from '{fromState}' to '{toState}': Parameter '{condition.ParameterName}' not found");
                return null;
            }

            var converted = new ConvertedCondition
            {
                ParameterName = condition.ParameterName
            };

            // Convert condition based on parameter type
            switch (param.TargetType)
            {
                case DMotionParameterType.Bool:
                    converted.ConditionType = ConvertedConditionType.Bool;
                    converted.BoolValue = condition.Mode == ConditionMode.If;
                    break;

                case DMotionParameterType.Int:
                    converted.ConditionType = ConvertedConditionType.Int;
                    converted.IntMode = ConvertIntConditionMode(condition.Mode);
                    converted.IntValue = (int)condition.Threshold;
                    break;

                case DMotionParameterType.Float:
                    _log.AddError($"Transition from '{fromState}' to '{toState}': Float parameter conditions not supported (parameter '{condition.ParameterName}')");
                    return null;

                default:
                    _log.AddError($"Transition from '{fromState}' to '{toState}': Unknown parameter type for '{condition.ParameterName}'");
                    return null;
            }

            return converted;
        }

        private ConvertedIntConditionMode ConvertIntConditionMode(ConditionMode mode)
        {
            return mode switch
            {
                ConditionMode.Equals => ConvertedIntConditionMode.Equal,
                ConditionMode.NotEqual => ConvertedIntConditionMode.NotEqual,
                ConditionMode.Greater => ConvertedIntConditionMode.Greater,
                ConditionMode.Less => ConvertedIntConditionMode.Less,
                _ => ConvertedIntConditionMode.Equal
            };
        }
    }
}
