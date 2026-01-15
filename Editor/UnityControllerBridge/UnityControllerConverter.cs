using System.Linq;
using DMotion.Authoring;
using DMotion.Editor.UnityControllerBridge.Adapters;
using DMotion.Editor.UnityControllerBridge.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Main converter that orchestrates the Unity AnimatorController to DMotion StateMachineAsset conversion.
    /// Coordinates adapters and core logic.
    /// </summary>
    public static class UnityControllerConverter
    {
        /// <summary>
        /// Converts a Unity AnimatorController to DMotion StateMachineAsset.
        /// Returns the created asset, or null on failure.
        /// </summary>
        public static StateMachineAsset ConvertController(
            AnimatorController controller,
            string outputPath,
            ControllerBridgeConfig config = null)
        {
            if (controller == null)
            {
                Debug.LogError("[UnityControllerConverter] Controller is null");
                return null;
            }

            config = config ?? ControllerBridgeConfig.Instance;

            Debug.Log($"[UnityControllerConverter] Converting '{controller.name}'...");

            // Phase 1: Read Unity controller data
            var controllerData = UnityControllerAdapter.ReadController(controller);
            if (controllerData == null)
            {
                Debug.LogError("[UnityControllerConverter] Failed to read controller data");
                return null;
            }

            // Phase 2: Convert using pure logic engine
            var options = new ConversionOptions
            {
                IncludeAnimationEvents = config.IncludeAnimationEvents,
                PreserveGraphLayout = config.PreserveGraphLayout,
                LogWarnings = config.LogWarnings,
                VerboseLogging = config.VerboseLogging
            };

            var engine = new ConversionEngine(options);
            var result = engine.Convert(controllerData);

            // Log conversion messages
            LogConversionMessages(engine.Log, config);

            if (!result.Success)
            {
                Debug.LogError($"[UnityControllerConverter] Conversion failed for '{controller.name}'");
                return null;
            }

            // Phase 3: Build DMotion assets
            var stateMachine = DMotionAssetBuilder.BuildStateMachine(result, outputPath);
            if (stateMachine == null)
            {
                Debug.LogError("[UnityControllerConverter] Failed to build DMotion assets");
                return null;
            }

            // Phase 4: Link transitions (second pass needed after all states exist)
            LinkTransitions(stateMachine, result);

            // Save final asset
            EditorUtility.SetDirty(stateMachine);
            AssetDatabase.SaveAssets();

            Debug.Log($"[UnityControllerConverter] Successfully converted '{controller.name}' to '{stateMachine.name}'");
            return stateMachine;
        }

        private static void LinkTransitions(StateMachineAsset stateMachine, ConversionResult result)
        {
            // Build state lookup
            var stateByName = stateMachine.States.ToDictionary(s => s.name, s => s);

            for (int i = 0; i < result.States.Count; i++)
            {
                var convertedState = result.States[i];
                var stateAsset = stateMachine.States[i];

                // Create transitions
                foreach (var transition in convertedState.Transitions)
                {
                    var outTransition = CreateTransition(transition, stateByName, stateMachine.Parameters, convertedState, stateAsset);
                    if (outTransition != null)
                    {
                        stateAsset.OutTransitions.Add(outTransition);
                    }
                }
            }
        }

        private static StateOutTransition CreateTransition(
            ConvertedTransition transition,
            System.Collections.Generic.Dictionary<string, AnimationStateAsset> stateByName,
            System.Collections.Generic.List<AnimationParameterAsset> parameters,
            ConvertedState sourceState,
            AnimationStateAsset sourceStateAsset)
        {
            // Find destination state
            if (!stateByName.TryGetValue(transition.DestinationStateName, out var toState))
            {
                Debug.LogWarning($"[UnityControllerConverter] Transition destination state '{transition.DestinationStateName}' not found");
                return null;
            }

            var outTransition = new StateOutTransition(toState, transition.Duration);

            // Handle exit time
            if (transition.HasEndTime)
            {
                outTransition.HasEndTime = true;

                // Convert normalized exit time to absolute time
                // Need to get clip duration
                float clipDuration = GetStateDuration(sourceState, sourceStateAsset);
                outTransition.EndTime = transition.NormalizedExitTime * clipDuration;
            }

            // Create conditions
            foreach (var condition in transition.Conditions)
            {
                var transitionCondition = CreateCondition(condition, parameters);
                if (transitionCondition != null)
                {
                    outTransition.Conditions.Add(transitionCondition);
                }
            }

            return outTransition;
        }

        private static TransitionCondition CreateCondition(
            ConvertedCondition condition,
            System.Collections.Generic.List<AnimationParameterAsset> parameters)
        {
            AnimationParameterAsset param = parameters.FirstOrDefault(p => p.name == condition.ParameterName);
            if (param == null)
            {
                Debug.LogWarning($"[UnityControllerConverter] Condition parameter '{condition.ParameterName}' not found");
                return null;
            }

            var transitionCondition = new TransitionCondition { Parameter = param };

            switch (condition.ConditionType)
            {
                case ConvertedConditionType.Bool:
                    transitionCondition.ComparisonValue = condition.BoolValue
                        ? BoolConditionComparison.True
                        : BoolConditionComparison.False;
                    break;

                case ConvertedConditionType.Int:
                    transitionCondition.IntComparisonValue = condition.IntValue;
                    transitionCondition.IntComparisonMode = ConvertIntConditionMode(condition.IntMode);
                    break;
            }

            return transitionCondition;
        }

        private static IntConditionComparison ConvertIntConditionMode(ConvertedIntConditionMode mode)
        {
            return mode switch
            {
                ConvertedIntConditionMode.Equal => IntConditionComparison.Equal,
                ConvertedIntConditionMode.NotEqual => IntConditionComparison.NotEqual,
                ConvertedIntConditionMode.Greater => IntConditionComparison.Greater,
                ConvertedIntConditionMode.Less => IntConditionComparison.Less,
                ConvertedIntConditionMode.GreaterOrEqual => IntConditionComparison.GreaterOrEqual,
                ConvertedIntConditionMode.LessOrEqual => IntConditionComparison.LessOrEqual,
                _ => IntConditionComparison.Equal
            };
        }

        private static float GetStateDuration(ConvertedState state, AnimationStateAsset stateAsset)
        {
            if (state.StateType == ConvertedStateType.SingleClip && state.Clip != null)
            {
                return state.Clip.length / state.Speed;
            }

            // For blend trees, use average clip duration (rough approximation)
            if (state.StateType == ConvertedStateType.LinearBlend && state.BlendClips.Count > 0)
            {
                float totalDuration = 0f;
                int validClips = 0;
                foreach (var blendClip in state.BlendClips)
                {
                    if (blendClip.Clip != null)
                    {
                        totalDuration += blendClip.Clip.length / (blendClip.Speed * state.Speed);
                        validClips++;
                    }
                }
                return validClips > 0 ? totalDuration / validClips : 1f;
            }

            return 1f; // Default fallback
        }

        private static void LogConversionMessages(ConversionLog log, ControllerBridgeConfig config)
        {
            if (config.VerboseLogging)
            {
                // Log all messages
                foreach (var message in log.Messages)
                {
                    switch (message.Level)
                    {
                        case MessageLevel.Error:
                            Debug.LogError($"[UnityControllerConverter] {message.Text}");
                            break;
                        case MessageLevel.Warning:
                            if (config.LogWarnings)
                            {
                                Debug.LogWarning($"[UnityControllerConverter] {message.Text}");
                            }
                            break;
                        case MessageLevel.Info:
                            Debug.Log($"[UnityControllerConverter] {message.Text}");
                            break;
                    }
                }
            }
            else
            {
                // Just log summary
                if (log.ErrorCount > 0)
                {
                    Debug.LogError($"[UnityControllerConverter] {log.ErrorCount} error(s) during conversion");
                }
                if (log.WarningCount > 0 && config.LogWarnings)
                {
                    Debug.LogWarning($"[UnityControllerConverter] {log.WarningCount} warning(s) during conversion");
                }
            }
        }
    }
}
