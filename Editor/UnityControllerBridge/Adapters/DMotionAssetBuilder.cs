using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMotion.Authoring;
using DMotion.Editor.UnityControllerBridge.Core;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge.Adapters
{
    /// <summary>
    /// Adapter that creates DMotion assets from ConversionResult.
    /// This is the "view" layer that writes to Unity assets.
    /// </summary>
    public static class DMotionAssetBuilder
    {
        /// <summary>
        /// Creates a complete StateMachineAsset from conversion result.
        /// Saves asset and all sub-assets to disk.
        /// </summary>
        public static StateMachineAsset BuildStateMachine(ConversionResult result, string outputPath)
        {
            if (!result.Success)
            {
                Debug.LogError("[DMotionAssetBuilder] Cannot build from failed conversion");
                return null;
            }

            // Ensure output directory exists
            string directory = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create main asset
            var stateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            stateMachine.name = result.ControllerName;

            // Create parameters
            var parameterAssets = CreateParameters(result.Parameters);

            // Create animation clip assets
            var clipAssetLookup = CreateClipAssets(result.States);

            // Create state assets
            var stateAssets = CreateStates(result.States, clipAssetLookup, parameterAssets);

            // Find default state
            var defaultState = stateAssets.FirstOrDefault(s => s.name == result.DefaultStateName);
            if (defaultState == null && stateAssets.Count > 0)
            {
                defaultState = stateAssets[0];
                Debug.LogWarning($"[DMotionAssetBuilder] Default state '{result.DefaultStateName}' not found, using '{defaultState.name}'");
            }

            // Assign to state machine
            stateMachine.Parameters = parameterAssets;
            stateMachine.States = stateAssets;
            stateMachine.DefaultState = defaultState;

            // Save main asset
            AssetDatabase.CreateAsset(stateMachine, outputPath);

            // Add all sub-assets
            foreach (var param in parameterAssets)
            {
                AssetDatabase.AddObjectToAsset(param, stateMachine);
            }

            foreach (var clipAsset in clipAssetLookup.Values)
            {
                AssetDatabase.AddObjectToAsset(clipAsset, stateMachine);
            }

            foreach (var state in stateAssets)
            {
                AssetDatabase.AddObjectToAsset(state, stateMachine);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DMotionAssetBuilder] Created StateMachineAsset at {outputPath}");
            return stateMachine;
        }

        private static List<AnimationParameterAsset> CreateParameters(List<ConvertedParameter> parameters)
        {
            var assets = new List<AnimationParameterAsset>();

            foreach (var param in parameters)
            {
                AnimationParameterAsset asset = param.TargetType switch
                {
                    DMotionParameterType.Float => CreateFloatParameter(param),
                    DMotionParameterType.Int => CreateIntParameter(param),
                    DMotionParameterType.Bool => CreateBoolParameter(param),
                    _ => null
                };

                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        private static FloatParameterAsset CreateFloatParameter(ConvertedParameter param)
        {
            var asset = ScriptableObject.CreateInstance<FloatParameterAsset>();
            asset.name = param.Name;
            asset.Value = param.DefaultFloatValue;
            return asset;
        }

        private static IntParameterAsset CreateIntParameter(ConvertedParameter param)
        {
            var asset = ScriptableObject.CreateInstance<IntParameterAsset>();
            asset.name = param.Name;
            asset.Value = param.DefaultIntValue;
            return asset;
        }

        private static BoolParameterAsset CreateBoolParameter(ConvertedParameter param)
        {
            var asset = ScriptableObject.CreateInstance<BoolParameterAsset>();
            asset.name = param.Name;
            asset.Value = param.DefaultBoolValue;
            return asset;
        }

        private static Dictionary<string, AnimationClipAsset> CreateClipAssets(List<ConvertedState> states)
        {
            var lookup = new Dictionary<string, AnimationClipAsset>();

            foreach (var state in states)
            {
                if (state.StateType == ConvertedStateType.SingleClip)
                {
                    if (!lookup.ContainsKey(state.ClipName) && state.Clip != null)
                    {
                        var clipAsset = CreateClipAsset(state.Clip, state.AnimationEvents);
                        lookup[state.ClipName] = clipAsset;
                    }
                }
                else if (state.StateType == ConvertedStateType.LinearBlend)
                {
                    foreach (var blendClip in state.BlendClips)
                    {
                        if (!lookup.ContainsKey(blendClip.ClipName) && blendClip.Clip != null)
                        {
                            var clipAsset = CreateClipAsset(blendClip.Clip, new List<ConvertedAnimationEvent>());
                            lookup[blendClip.ClipName] = clipAsset;
                        }
                    }
                }
            }

            return lookup;
        }

        private static AnimationClipAsset CreateClipAsset(AnimationClip unityClip, List<ConvertedAnimationEvent> events)
        {
            var asset = ScriptableObject.CreateInstance<AnimationClipAsset>();
            asset.name = unityClip.name;
            asset.Clip = unityClip;

            // Convert animation events
            if (events.Count > 0)
            {
                var eventArray = new AnimationClipEvent[events.Count];
                for (int i = 0; i < events.Count; i++)
                {
                    eventArray[i] = new AnimationClipEvent
                    {
                        EventName = new AnimationEventName(events[i].FunctionName),
                        NormalizedTime = events[i].NormalizedTime
                    };
                }
                asset.Events = eventArray;
            }

            return asset;
        }

        private static List<AnimationStateAsset> CreateStates(
            List<ConvertedState> states,
            Dictionary<string, AnimationClipAsset> clipLookup,
            List<AnimationParameterAsset> parameters)
        {
            var assets = new List<AnimationStateAsset>();

            foreach (var state in states)
            {
                AnimationStateAsset asset = state.StateType switch
                {
                    ConvertedStateType.SingleClip => CreateSingleClipState(state, clipLookup),
                    ConvertedStateType.LinearBlend => CreateLinearBlendState(state, clipLookup, parameters),
                    _ => null
                };

                if (asset != null)
                {
                    // Set common properties
                    asset.name = state.Name;
                    asset.Speed = state.Speed;
                    asset.Loop = state.Loop;

                    // Create transitions
                    CreateTransitions(asset, state, parameters);

                    assets.Add(asset);
                }
            }

            return assets;
        }

        private static SingleClipStateAsset CreateSingleClipState(ConvertedState state, Dictionary<string, AnimationClipAsset> clipLookup)
        {
            if (!clipLookup.TryGetValue(state.ClipName, out var clipAsset))
            {
                Debug.LogError($"[DMotionAssetBuilder] Clip '{state.ClipName}' not found");
                return null;
            }

            var asset = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            asset.Clip = clipAsset;
            return asset;
        }

        private static LinearBlendStateAsset CreateLinearBlendState(
            ConvertedState state,
            Dictionary<string, AnimationClipAsset> clipLookup,
            List<AnimationParameterAsset> parameters)
        {
            var asset = ScriptableObject.CreateInstance<LinearBlendStateAsset>();

            // Find blend parameter
            var blendParam = parameters.OfType<FloatParameterAsset>().FirstOrDefault(p => p.name == state.BlendParameterName);
            if (blendParam == null)
            {
                Debug.LogError($"[DMotionAssetBuilder] Blend parameter '{state.BlendParameterName}' not found");
                return null;
            }

            asset.BlendParameter = blendParam;

            // Create blend clips
            var blendClips = new List<ClipWithThreshold>();
            foreach (var blendClip in state.BlendClips)
            {
                if (clipLookup.TryGetValue(blendClip.ClipName, out var clipAsset))
                {
                    blendClips.Add(new ClipWithThreshold
                    {
                        Clip = clipAsset,
                        Threshold = blendClip.Threshold,
                        Speed = blendClip.Speed
                    });
                }
            }

            asset.BlendClips = blendClips.ToArray();
            return asset;
        }

        private static void CreateTransitions(
            AnimationStateAsset stateAsset,
            ConvertedState state,
            List<AnimationParameterAsset> parameters)
        {
            // Note: We can't set ToState references yet because states aren't all created
            // This will need to be done in a second pass by UnityControllerConverter
            // For now, we'll store transition data but defer linking

            // Transitions will be linked in UnityControllerConverter.LinkTransitions()
        }
    }
}
