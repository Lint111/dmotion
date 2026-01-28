using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Persistent settings for Animation Preview window that survive domain reloads.
    /// Stores layout preferences and blend parameter values per state asset.
    /// </summary>
    [FilePath("DMotion/PreviewSettings.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal class PreviewSettings : ScriptableSingleton<PreviewSettings>
    {
        #region Layout Settings
        
        [SerializeField] private float splitPosition = 300f;
        
        /// <summary>
        /// The split position (inspector panel width) in the Animation Preview window.
        /// </summary>
        public float SplitPosition
        {
            get => splitPosition;
            set
            {
                if (Math.Abs(splitPosition - value) > 1f)
                {
                    splitPosition = value;
                    Save(true);
                }
            }
        }
        
        #endregion
        
        #region Blend Values
        
        [Serializable]
        private class BlendValueEntry
        {
            public string stateGuid;
            public float blendValue1D;
            public Vector2 blendValue2D;
        }
        
        [SerializeField] private List<BlendValueEntry> blendValues = new();
        
        // Runtime lookup cache (rebuilt after domain reload from serialized list)
        private Dictionary<string, BlendValueEntry> blendValueCache;
        
        private Dictionary<string, BlendValueEntry> BlendValueCache
        {
            get
            {
                if (blendValueCache == null)
                {
                    blendValueCache = new Dictionary<string, BlendValueEntry>();
                    foreach (var entry in blendValues)
                    {
                        if (!string.IsNullOrEmpty(entry.stateGuid))
                        {
                            blendValueCache[entry.stateGuid] = entry;
                        }
                    }
                }
                return blendValueCache;
            }
        }
        
        /// <summary>
        /// Gets the stored 1D blend value for a state asset.
        /// </summary>
        public float GetBlendValue1D(UnityEngine.Object stateAsset, float defaultValue = 0f)
        {
            var guid = GetAssetGuid(stateAsset);
            if (string.IsNullOrEmpty(guid)) return defaultValue;
            
            return BlendValueCache.TryGetValue(guid, out var entry) ? entry.blendValue1D : defaultValue;
        }
        
        /// <summary>
        /// Sets the 1D blend value for a state asset.
        /// </summary>
        public void SetBlendValue1D(UnityEngine.Object stateAsset, float value)
        {
            var guid = GetAssetGuid(stateAsset);
            if (string.IsNullOrEmpty(guid)) return;
            
            if (BlendValueCache.TryGetValue(guid, out var entry))
            {
                if (Math.Abs(entry.blendValue1D - value) > 0.0001f)
                {
                    entry.blendValue1D = value;
                    Save(true);
                }
            }
            else
            {
                entry = new BlendValueEntry { stateGuid = guid, blendValue1D = value };
                blendValues.Add(entry);
                BlendValueCache[guid] = entry;
                Save(true);
            }
        }
        
        /// <summary>
        /// Gets the stored 2D blend value for a state asset.
        /// </summary>
        public Vector2 GetBlendValue2D(UnityEngine.Object stateAsset, Vector2 defaultValue = default)
        {
            var guid = GetAssetGuid(stateAsset);
            if (string.IsNullOrEmpty(guid)) return defaultValue;
            
            return BlendValueCache.TryGetValue(guid, out var entry) ? entry.blendValue2D : defaultValue;
        }
        
        /// <summary>
        /// Sets the 2D blend value for a state asset.
        /// </summary>
        public void SetBlendValue2D(UnityEngine.Object stateAsset, Vector2 value)
        {
            var guid = GetAssetGuid(stateAsset);
            if (string.IsNullOrEmpty(guid)) return;
            
            if (BlendValueCache.TryGetValue(guid, out var entry))
            {
                if (Vector2.Distance(entry.blendValue2D, value) > 0.0001f)
                {
                    entry.blendValue2D = value;
                    Save(true);
                }
            }
            else
            {
                entry = new BlendValueEntry { stateGuid = guid, blendValue2D = value };
                blendValues.Add(entry);
                BlendValueCache[guid] = entry;
                Save(true);
            }
        }
        
        private static string GetAssetGuid(UnityEngine.Object asset)
        {
            if (asset == null) return null;

            // Use GlobalObjectId to uniquely identify sub-assets (states inside state machines)
            // AssetPathToGUID would return the same GUID for all sub-assets in the same file
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(asset);
            if (globalId.identifierType == 0) return null; // Invalid

            return globalId.ToString();
        }
        
        #endregion
        
        #region Blend Position Helpers
        
        /// <summary>
        /// Gets the persisted blend position for a state asset.
        /// Centralizes blend position access to avoid duplication across files.
        /// </summary>
        public static Vector2 GetBlendPosition(AnimationStateAsset state)
        {
            if (state == null) return Vector2.zero;
            
            return state switch
            {
                LinearBlendStateAsset linear => new Vector2(instance.GetBlendValue1D(linear), 0),
                Directional2DBlendStateAsset blend2D => instance.GetBlendValue2D(blend2D),
                _ => Vector2.zero
            };
        }
        
        #endregion
        
        #region Layer Composition State

        [Serializable]
        private class LayerSelectionEntry
        {
            public int layerIndex;
            public string selectedStateGuid;
            public string transitionFromGuid;
            public string transitionToGuid;
            public float weight = 1f;
            public bool enabled = true;
        }

        [Serializable]
        private class CompositionStateEntry
        {
            public string stateMachineGuid;
            public List<LayerSelectionEntry> layerSelections = new();
        }

        [SerializeField] private List<CompositionStateEntry> compositionStates = new();

        // Runtime lookup cache
        private Dictionary<string, CompositionStateEntry> compositionStateCache;

        private Dictionary<string, CompositionStateEntry> CompositionStateCache
        {
            get
            {
                if (compositionStateCache == null)
                {
                    compositionStateCache = new Dictionary<string, CompositionStateEntry>();
                    foreach (var entry in compositionStates)
                    {
                        if (!string.IsNullOrEmpty(entry.stateMachineGuid))
                        {
                            compositionStateCache[entry.stateMachineGuid] = entry;
                        }
                    }
                }
                return compositionStateCache;
            }
        }

        /// <summary>
        /// Saves layer selection state for a multi-layer state machine.
        /// </summary>
        public void SaveLayerSelection(StateMachineAsset rootStateMachine, int layerIndex,
            AnimationStateAsset selectedState, AnimationStateAsset transitionFrom, AnimationStateAsset transitionTo,
            float weight = 1f, bool enabled = true)
        {
            var smGuid = GetAssetGuid(rootStateMachine);
            if (string.IsNullOrEmpty(smGuid)) return;

            if (!CompositionStateCache.TryGetValue(smGuid, out var compositionEntry))
            {
                compositionEntry = new CompositionStateEntry { stateMachineGuid = smGuid };
                compositionStates.Add(compositionEntry);
                CompositionStateCache[smGuid] = compositionEntry;
            }

            // Find or create layer entry
            var layerEntry = compositionEntry.layerSelections.Find(l => l.layerIndex == layerIndex);
            if (layerEntry == null)
            {
                layerEntry = new LayerSelectionEntry { layerIndex = layerIndex };
                compositionEntry.layerSelections.Add(layerEntry);
            }

            layerEntry.selectedStateGuid = GetAssetGuid(selectedState);
            layerEntry.transitionFromGuid = GetAssetGuid(transitionFrom);
            layerEntry.transitionToGuid = GetAssetGuid(transitionTo);
            layerEntry.weight = weight;
            layerEntry.enabled = enabled;

            Save(true);
        }

        /// <summary>
        /// Gets saved layer selection state for a multi-layer state machine.
        /// Returns null if no saved state exists.
        /// </summary>
        public (AnimationStateAsset selectedState, AnimationStateAsset transitionFrom, AnimationStateAsset transitionTo, float weight, bool enabled)?
            GetLayerSelection(StateMachineAsset rootStateMachine, int layerIndex)
        {
            var smGuid = GetAssetGuid(rootStateMachine);
            if (string.IsNullOrEmpty(smGuid)) return null;

            if (!CompositionStateCache.TryGetValue(smGuid, out var compositionEntry))
                return null;

            var layerEntry = compositionEntry.layerSelections.Find(l => l.layerIndex == layerIndex);
            if (layerEntry == null) return null;

            var selectedState = LoadAssetFromGuid<AnimationStateAsset>(layerEntry.selectedStateGuid);
            var transitionFrom = LoadAssetFromGuid<AnimationStateAsset>(layerEntry.transitionFromGuid);
            var transitionTo = LoadAssetFromGuid<AnimationStateAsset>(layerEntry.transitionToGuid);

            return (selectedState, transitionFrom, transitionTo, layerEntry.weight, layerEntry.enabled);
        }

        /// <summary>
        /// Clears saved layer selection state for a state machine.
        /// </summary>
        public void ClearLayerSelections(StateMachineAsset rootStateMachine)
        {
            var smGuid = GetAssetGuid(rootStateMachine);
            if (string.IsNullOrEmpty(smGuid)) return;

            if (CompositionStateCache.TryGetValue(smGuid, out var entry))
            {
                compositionStates.Remove(entry);
                CompositionStateCache.Remove(smGuid);
                Save(true);
            }
        }

        private static T LoadAssetFromGuid<T>(string globalIdString) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(globalIdString)) return null;

            // Parse GlobalObjectId string format (e.g., "GlobalObjectId_V1-2-guid-fileId-0")
            if (GlobalObjectId.TryParse(globalIdString, out var globalId))
            {
                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as T;
            }

            // Fallback: try legacy GUID format for backwards compatibility
            var path = AssetDatabase.GUIDToAssetPath(globalIdString);
            if (!string.IsNullOrEmpty(path))
            {
                return AssetDatabase.LoadAssetAtPath<T>(path);
            }

            return null;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Removes blend values for assets that no longer exist.
        /// </summary>
        public void CleanupOrphanedEntries()
        {
            var toRemove = new List<BlendValueEntry>();
            
            foreach (var entry in blendValues)
            {
                var path = AssetDatabase.GUIDToAssetPath(entry.stateGuid);
                if (string.IsNullOrEmpty(path))
                {
                    toRemove.Add(entry);
                }
            }
            
            if (toRemove.Count > 0)
            {
                foreach (var entry in toRemove)
                {
                    blendValues.Remove(entry);
                    blendValueCache?.Remove(entry.stateGuid);
                }
                Save(true);
            }
        }
        
        #endregion
    }
}
