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
        /// Generates a comprehensive conversion report with statistics, feature usage, and recommendations.
        /// </summary>
        public ConversionReport GenerateReport(ConversionResult result)
        {
            var report = new ConversionReport
            {
                Result = result
            };

            // Generate statistics
            report.Statistics = GenerateStatistics(result);

            // Generate feature usage info
            report.FeatureUsage = GenerateFeatureUsage(result);

            // Generate recommendations
            report.Recommendations = GenerateRecommendations(result);

            return report;
        }

        private ConversionStatistics GenerateStatistics(ConversionResult result)
        {
            var stats = new ConversionStatistics
            {
                ParametersConverted = result.Parameters.Count,
                AnyStateTransitions = result.AnyStateTransitions.Count,
                WarningsCount = _log.WarningCount,
                ErrorsCount = _log.ErrorCount
            };

            // Count states recursively (including nested sub-machines)
            CountStatesRecursively(result.States, stats);

            return stats;
        }

        private void CountStatesRecursively(List<ConvertedState> states, ConversionStatistics stats)
        {
            foreach (var state in states)
            {
                stats.StatesConverted++;

                // Count transitions
                stats.TransitionsCreated += state.Transitions.Count;

                // Count by type
                switch (state.StateType)
                {
                    case ConvertedStateType.SingleClip:
                        stats.AnimationClipsUsed++;
                        break;

                    case ConvertedStateType.LinearBlend:
                        stats.BlendTreesConverted++;
                        stats.AnimationClipsUsed += state.BlendClips.Count;
                        break;

                    case ConvertedStateType.SubStateMachine:
                        stats.SubStateMachinesConverted++;
                        // Recursively count nested states
                        if (state.NestedStateMachine != null && state.NestedStateMachine.Success)
                        {
                            CountStatesRecursively(state.NestedStateMachine.States, stats);
                        }
                        break;
                }
            }
        }

        private FeatureUsageInfo GenerateFeatureUsage(ConversionResult result)
        {
            var info = new FeatureUsageInfo();

            // Analyze states to detect feature usage
            bool hasSubStateMachines = CheckHasSubStateMachines(result.States);
            bool hasBlendTrees1D = CheckHasBlendTrees1D(result.States);
            bool hasAnyState = result.AnyStateTransitions.Count > 0;
            bool hasExitTime = CheckHasExitTime(result.States) || result.AnyStateTransitions.Any(t => t.HasEndTime);
            bool hasTriggers = result.Parameters.Any(p => p.OriginalType == ParameterType.Trigger);
            bool hasSpeedParameter = CheckHasSpeedParameter(result.States);
            bool hasMultipleLayers = false; // Would need to track this from controller data

            // Single Clip States
            info.AddFeature(
                "Single Clip States",
                true,
                "SingleClipStateAsset",
                ConversionStatus.Supported,
                "Full support for single animation clips"
            );

            // 1D Blend Trees
            info.AddFeature(
                "1D Blend Trees",
                hasBlendTrees1D,
                "LinearBlendStateAsset",
                hasBlendTrees1D ? ConversionStatus.Supported : ConversionStatus.NotUsed,
                hasBlendTrees1D ? "Converted to LinearBlendStateAsset" : ""
            );

            // Sub-State Machines
            info.AddFeature(
                "Sub-State Machines",
                hasSubStateMachines,
                "SubStateMachineStateAsset",
                hasSubStateMachines ? ConversionStatus.Supported : ConversionStatus.NotUsed,
                hasSubStateMachines ? "Native hierarchical support with unlimited depth" : ""
            );

            // Any State Transitions
            info.AddFeature(
                "Any State Transitions",
                hasAnyState,
                "Native Support",
                hasAnyState ? ConversionStatus.Supported : ConversionStatus.NotUsed,
                hasAnyState ? "Converted as global transitions" : ""
            );

            // Parameters
            info.AddFeature(
                "Bool Parameters",
                result.Parameters.Any(p => p.TargetType == DMotionParameterType.Bool),
                "BoolParameterAsset",
                ConversionStatus.Supported,
                "Full support"
            );

            info.AddFeature(
                "Int Parameters",
                result.Parameters.Any(p => p.TargetType == DMotionParameterType.Int),
                "IntParameterAsset",
                ConversionStatus.Supported,
                "Full support"
            );

            info.AddFeature(
                "Float Parameters",
                result.Parameters.Any(p => p.TargetType == DMotionParameterType.Float),
                "FloatParameterAsset",
                ConversionStatus.Supported,
                "Full support"
            );

            info.AddFeature(
                "Trigger Parameters",
                hasTriggers,
                "BoolParameterAsset",
                hasTriggers ? ConversionStatus.PartiallySupported : ConversionStatus.NotUsed,
                hasTriggers ? "Converted to Bool - auto-reset must be implemented manually" : ""
            );

            // Exit Time
            info.AddFeature(
                "Exit Time Transitions",
                hasExitTime,
                "EndTime",
                hasExitTime ? ConversionStatus.Supported : ConversionStatus.NotUsed,
                hasExitTime ? "Converted to absolute EndTime" : ""
            );

            // Speed Parameter (not yet supported)
            info.AddFeature(
                "Speed Parameter",
                hasSpeedParameter,
                "Not Supported",
                hasSpeedParameter ? ConversionStatus.NotSupported : ConversionStatus.NotUsed,
                hasSpeedParameter ? "Speed parameters are not yet supported - speed will be constant" : ""
            );

            // 2D Blend Trees (not supported)
            info.AddFeature(
                "2D Blend Trees",
                false, // Would need to track from controller data
                "Not Supported",
                ConversionStatus.NotSupported,
                "2D blend trees are not yet supported"
            );

            // Direct Blend Trees (not supported)
            info.AddFeature(
                "Direct Blend Trees",
                false,
                "Not Supported",
                ConversionStatus.NotSupported,
                "Direct blend trees are not yet supported"
            );

            // Multiple Layers (not supported)
            info.AddFeature(
                "Multiple Layers",
                hasMultipleLayers,
                "Not Supported",
                hasMultipleLayers ? ConversionStatus.NotSupported : ConversionStatus.NotUsed,
                hasMultipleLayers ? "Only first layer is converted - multiple layers not supported" : ""
            );

            // Float Conditions (not supported)
            info.AddFeature(
                "Float Parameter Conditions",
                false, // Would be logged as error if encountered
                "Not Supported",
                ConversionStatus.NotSupported,
                "Float parameter conditions not yet supported - use Int or Bool"
            );

            return info;
        }

        private bool CheckHasSubStateMachines(List<ConvertedState> states)
        {
            foreach (var state in states)
            {
                if (state.StateType == ConvertedStateType.SubStateMachine)
                    return true;

                if (state.NestedStateMachine != null && CheckHasSubStateMachines(state.NestedStateMachine.States))
                    return true;
            }
            return false;
        }

        private bool CheckHasBlendTrees1D(List<ConvertedState> states)
        {
            foreach (var state in states)
            {
                if (state.StateType == ConvertedStateType.LinearBlend)
                    return true;

                if (state.NestedStateMachine != null && CheckHasBlendTrees1D(state.NestedStateMachine.States))
                    return true;
            }
            return false;
        }

        private bool CheckHasExitTime(List<ConvertedState> states)
        {
            foreach (var state in states)
            {
                if (state.Transitions.Any(t => t.HasEndTime))
                    return true;

                if (state.NestedStateMachine != null && CheckHasExitTime(state.NestedStateMachine.States))
                    return true;
            }
            return false;
        }

        private bool CheckHasSpeedParameter(List<ConvertedState> states)
        {
            // This would require tracking from StateData.SpeedParameterActive
            // For now, check if we logged a warning about it
            return _log.Messages.Any(m => m.Text.Contains("speed parameter"));
        }

        private List<ConversionRecommendation> GenerateRecommendations(ConversionResult result)
        {
            var recommendations = new List<ConversionRecommendation>();

            // Check for triggers
            if (result.Parameters.Any(p => p.OriginalType == ParameterType.Trigger))
            {
                recommendations.Add(new ConversionRecommendation
                {
                    Title = "Trigger Parameters Converted to Bool",
                    Description = "Trigger parameters have been converted to Bool. You'll need to manually reset them after use. Consider using a custom system to auto-reset triggers after consumption.",
                    Priority = RecommendationPriority.Medium
                });
            }

            // Check for warnings
            if (_log.WarningCount > 0)
            {
                recommendations.Add(new ConversionRecommendation
                {
                    Title = $"Review {_log.WarningCount} Warning(s)",
                    Description = "The conversion completed with warnings. Check the Unity Console for details about features that were skipped or converted with limitations.",
                    Priority = RecommendationPriority.High
                });
            }

            // Check for unsupported features in log
            if (_log.Messages.Any(m => m.Text.Contains("speed parameter")))
            {
                recommendations.Add(new ConversionRecommendation
                {
                    Title = "Speed Parameters Not Supported",
                    Description = "Some states use speed parameters which are not yet supported. Animation speed will be constant. This feature is planned for a future release.",
                    Priority = RecommendationPriority.Medium
                });
            }

            if (_log.Messages.Any(m => m.Text.Contains("cycle offset")))
            {
                recommendations.Add(new ConversionRecommendation
                {
                    Title = "Cycle Offset Ignored",
                    Description = "Some states have cycle offset values which are not supported and will be ignored. Consider using normalized time adjustments in code if needed.",
                    Priority = RecommendationPriority.Low
                });
            }

            if (_log.Messages.Any(m => m.Text.Contains("Offset") && m.Text.Contains("transition")))
            {
                recommendations.Add(new ConversionRecommendation
                {
                    Title = "Transition Offsets Not Supported",
                    Description = "Some transitions have offset values which are not supported. Transitions will start from the beginning of the destination animation.",
                    Priority = RecommendationPriority.Low
                });
            }

            // Success with no issues
            if (result.Success && _log.WarningCount == 0 && _log.ErrorCount == 0)
            {
                recommendations.Add(new ConversionRecommendation
                {
                    Title = "Conversion Completed Successfully",
                    Description = "All features were converted without issues. The DMotion state machine should behave identically to the Unity AnimatorController.",
                    Priority = RecommendationPriority.Low
                });
            }

            return recommendations;
        }

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

                // Phase 6: Convert Any State transitions (native DMotion support)
                result.AnyStateTransitions = ConvertAnyStateTransitions(stateMachine.AnyStateTransitions, result.Parameters);

                result.Success = true;
                _log.AddInfo($"Conversion successful: {result.States.Count} states, {result.Parameters.Count} parameters, {result.AnyStateTransitions.Count} Any State transitions");
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
            var converted = new ConvertedState
            {
                Name = state.Name,
                Speed = state.Speed,
                GraphPosition = state.GraphPosition,
                Loop = true // Default, will be refined based on motion type
            };

            // Check if this is a sub-state machine
            if (state.IsSubStateMachine)
            {
                ConvertSubStateMachine(state.SubStateMachine, converted, parameters);
                return converted;
            }

            // Regular animation state - must have a motion
            if (state.Motion == null)
            {
                _log.AddWarning($"State '{state.Name}' has no motion, skipping");
                return null;
            }

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

        private void ConvertSubStateMachine(SubStateMachineData subMachine, ConvertedState converted, List<ConvertedParameter> parameters)
        {
            converted.StateType = ConvertedStateType.SubStateMachine;
            converted.EntryStateName = subMachine.EntryStateName;

            // Recursively convert the nested state machine
            // Create a new ControllerData with just this sub-machine
            var nestedControllerData = new ControllerData
            {
                Name = $"{converted.Name}_Nested",
                Parameters = new List<ParameterData>() // Parameters are shared at root level
            };

            var nestedLayer = new LayerData
            {
                Name = "Base",
                StateMachine = subMachine.NestedStateMachine
            };
            nestedControllerData.Layers.Add(nestedLayer);

            // Recursively convert the nested machine
            var nestedEngine = new ConversionEngine(_options);
            var nestedResult = nestedEngine.Convert(nestedControllerData);

            if (!nestedResult.Success)
            {
                _log.AddError($"Failed to convert nested state machine '{converted.Name}'");
                // Copy errors from nested conversion
                foreach (var message in nestedEngine.Log.Messages)
                {
                    if (message.Level == MessageLevel.Error)
                    {
                        _log.AddError($"[Nested:{converted.Name}] {message.Text}");
                    }
                }
                return;
            }

            converted.NestedStateMachine = nestedResult;

            // Convert exit transitions
            foreach (var exitTransitionData in subMachine.ExitTransitions)
            {
                var exitTransition = ConvertTransitionData(exitTransitionData, parameters);
                if (exitTransition != null)
                {
                    converted.ExitTransitions.Add(exitTransition);
                }
            }

            _log.AddInfo($"Converted sub-state machine '{converted.Name}' with {nestedResult.States.Count} nested states (native DMotion support)");
        }

        /// <summary>
        /// Converts a single transition data (used for exit transitions).
        /// </summary>
        private ConvertedTransition ConvertTransitionData(TransitionData transition, List<ConvertedParameter> parameters)
        {
            var converted = new ConvertedTransition
            {
                DestinationStateName = transition.DestinationStateName,
                Duration = transition.Duration
            };

            // Handle exit time
            if (transition.HasExitTime)
            {
                converted.HasEndTime = true;
                converted.NormalizedExitTime = transition.ExitTime;
            }

            // Convert conditions
            foreach (var condition in transition.Conditions)
            {
                var convertedCondition = ConvertCondition(condition, parameters);
                if (convertedCondition != null)
                {
                    converted.Conditions.Add(convertedCondition);
                }
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

        private List<ConvertedTransition> ConvertAnyStateTransitions(List<TransitionData> anyStateTransitions, List<ConvertedParameter> parameters)
        {
            var converted = new List<ConvertedTransition>();

            foreach (var transition in anyStateTransitions)
            {
                // For Any State transitions, we don't have a source state
                var convertedTransition = new ConvertedTransition
                {
                    DestinationStateName = transition.DestinationStateName,
                    Duration = transition.Duration
                };

                // Handle exit time (for Any State, this is less common)
                if (transition.HasExitTime)
                {
                    convertedTransition.HasEndTime = true;
                    convertedTransition.NormalizedExitTime = transition.ExitTime;
                    _log.AddInfo($"Any State transition to '{transition.DestinationStateName}': Exit time {transition.ExitTime:F2}");
                }

                // Convert conditions
                foreach (var condition in transition.Conditions)
                {
                    var convertedCondition = ConvertCondition(condition, parameters);
                    if (convertedCondition != null)
                    {
                        convertedTransition.Conditions.Add(convertedCondition);
                    }
                }

                converted.Add(convertedTransition);
                _log.AddInfo($"Converted Any State transition to '{transition.DestinationStateName}' with {convertedTransition.Conditions.Count} conditions");
            }

            return converted;
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

        // Overload for Any State transitions (no source state)
        private ConvertedCondition ConvertCondition(ConditionData condition, List<ConvertedParameter> parameters)
        {
            return ConvertCondition(condition, parameters, "Any State", "");
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
