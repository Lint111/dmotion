using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Represents a leaf state with its hierarchical path.
    /// Used for querying the visual grouping structure.
    /// </summary>
    public readonly struct StateWithPath
    {
        /// <summary>The leaf state asset (Single or LinearBlend)</summary>
        public readonly AnimationStateAsset State;

        /// <summary>Hierarchical path (e.g., "Combat/Attack/Slash")</summary>
        public readonly string Path;

        /// <summary>The immediate parent group, or null if at root level</summary>
        public readonly SubStateMachineStateAsset ParentGroup;

        public StateWithPath(AnimationStateAsset state, string path, SubStateMachineStateAsset parentGroup)
        {
            State = state;
            Path = path;
            ParentGroup = parentGroup;
        }
    }

    [CreateAssetMenu(menuName = StateMachineEditorConstants.DMotionPath + "/State Machine")]
    public class StateMachineAsset : ScriptableObject
    {
        public AnimationStateAsset DefaultState;
        public List<AnimationStateAsset> States = new();
        public List<AnimationParameterAsset> Parameters = new();

        [Header("Any State Transitions")]
        [Tooltip("Global transitions that can be taken from any state. Evaluated before regular state transitions.")]
        public List<StateOutTransition> AnyStateTransitions = new();
        
        [Tooltip("Optional transition that allows exiting the state machine from any state when conditions are met. " +
                 "Useful for animation canceling or global interrupts in nested state machines.")]
        [SerializeField]
        private StateOutTransition _anyStateExitTransition;
        
        /// <summary>
        /// Gets or sets the Any State exit transition.
        /// Use HasAnyStateExitTransition to check if one exists (handles Unity serialization quirks).
        /// </summary>
        public StateOutTransition AnyStateExitTransition
        {
            get => _anyStateExitTransition;
            set => _anyStateExitTransition = value;
        }
        
        /// <summary>
        /// Returns true if there is an active Any State exit transition.
        /// More reliable than checking AnyStateExitTransition == null due to Unity serialization.
        /// </summary>
        public bool HasAnyStateExitTransition => _anyStateExitTransition != null && _anyStateExitTransition.Conditions != null;

        [Header("Exit States")]
        [Tooltip("States that can trigger an exit when this machine is used as a nested state machine. " +
                 "Draw transitions TO the Exit node in the graph editor to define these.")]
        public List<AnimationStateAsset> ExitStates = new();

        #region Rig Binding

        [Header("Rig Binding")]
        [Tooltip("The armature data (typically a Unity Avatar) that this state machine is bound to. " +
                 "Used for deterministic rig selection in preview and conversion workflows.")]
        [SerializeField]
        private UnityEngine.Object _boundArmatureData;

        [SerializeField, HideInInspector]
        private RigBindingStatus _rigBindingStatus = RigBindingStatus.Unresolved;

        [SerializeField, HideInInspector]
        private RigBindingSource _rigBindingSource = RigBindingSource.None;

        [SerializeField, HideInInspector]
        private string _rigBindingFingerprint;

        /// <summary>
        /// The armature data bound to this state machine.
        /// Typically a Unity Avatar, but can be other armature types via adapters.
        /// </summary>
        public UnityEngine.Object BoundArmatureData
        {
            get => _boundArmatureData;
            set => _boundArmatureData = value;
        }

        /// <summary>
        /// The current status of rig binding resolution.
        /// </summary>
        public RigBindingStatus RigBindingStatus
        {
            get => _rigBindingStatus;
            set => _rigBindingStatus = value;
        }

        /// <summary>
        /// How the rig binding was determined (for diagnostics and Mechination integration).
        /// </summary>
        public RigBindingSource RigBindingSource
        {
            get => _rigBindingSource;
            set => _rigBindingSource = value;
        }

        /// <summary>
        /// Hash fingerprint for change detection during Mechination conversion.
        /// Used to detect when re-prompting is needed after source changes.
        /// </summary>
        public string RigBindingFingerprint
        {
            get => _rigBindingFingerprint;
            set => _rigBindingFingerprint = value;
        }

        /// <summary>
        /// Returns true if a rig is bound and resolved.
        /// </summary>
        public bool HasResolvedRig => _rigBindingStatus == RigBindingStatus.Resolved && _boundArmatureData != null;

        /// <summary>
        /// Binds an armature to this state machine with full metadata.
        /// </summary>
        /// <param name="armatureData">The armature data (typically Avatar)</param>
        /// <param name="source">How the binding was determined</param>
        /// <param name="fingerprint">Optional fingerprint for change detection</param>
        public void BindRig(UnityEngine.Object armatureData, RigBindingSource source, string fingerprint = null)
        {
            _boundArmatureData = armatureData;
            _rigBindingStatus = armatureData != null ? RigBindingStatus.Resolved : RigBindingStatus.Unresolved;
            _rigBindingSource = source;
            _rigBindingFingerprint = fingerprint;
        }

        /// <summary>
        /// Clears the rig binding, optionally marking as opted-out.
        /// </summary>
        /// <param name="optOut">If true, marks as UserOptedOut to prevent re-prompting</param>
        /// <param name="fingerprint">Fingerprint to remember what was opted out of</param>
        public void ClearRigBinding(bool optOut = false, string fingerprint = null)
        {
            _boundArmatureData = null;
            _rigBindingStatus = optOut ? RigBindingStatus.UserOptedOut : RigBindingStatus.Unresolved;
            _rigBindingSource = RigBindingSource.None;
            _rigBindingFingerprint = optOut ? fingerprint : null;
        }

        #endregion

        #region Parameter Dependency Tracking

        /// <summary>
        /// All parameter dependencies for SubStateMachines in this state machine.
        /// Tracks which parameters are required by which SubStateMachines.
        /// </summary>
        [SerializeField, HideInInspector]
        private List<ParameterDependency> _parameterDependencies = new();

        /// <summary>
        /// Parameter links/aliases that map parent parameters to child SubStateMachine parameters.
        /// Used during blob conversion to resolve parameter indices.
        /// </summary>
        [SerializeField, HideInInspector]
        private List<ParameterLink> _parameterLinks = new();

        /// <summary>Gets all parameter dependencies for this state machine.</summary>
        public IReadOnlyList<ParameterDependency> ParameterDependencies => _parameterDependencies;

        /// <summary>Gets all parameter links/aliases.</summary>
        public IReadOnlyList<ParameterLink> ParameterLinks => _parameterLinks;

        /// <summary>Adds a parameter dependency.</summary>
        internal void AddDependency(ParameterDependency dependency)
        {
            // Avoid duplicates
            for (int i = 0; i < _parameterDependencies.Count; i++)
            {
                var existing = _parameterDependencies[i];
                if (existing.RequiredParameter == dependency.RequiredParameter &&
                    existing.RequiringSubMachine == dependency.RequiringSubMachine &&
                    existing.UsageType == dependency.UsageType)
                {
                    return;
                }
            }
            _parameterDependencies.Add(dependency);
        }

        /// <summary>Removes all dependencies for a specific SubStateMachine.</summary>
        internal void RemoveDependenciesForSubMachine(SubStateMachineStateAsset subMachine)
        {
            for (int i = _parameterDependencies.Count - 1; i >= 0; i--)
            {
                if (_parameterDependencies[i].RequiringSubMachine == subMachine)
                    _parameterDependencies.RemoveAt(i);
            }
        }

        /// <summary>Adds a parameter link.</summary>
        internal void AddLink(ParameterLink link)
        {
            // Avoid duplicates
            for (int i = 0; i < _parameterLinks.Count; i++)
            {
                var existing = _parameterLinks[i];
                if (existing.SourceParameter == link.SourceParameter &&
                    existing.TargetParameter == link.TargetParameter &&
                    existing.SubMachine == link.SubMachine)
                {
                    return;
                }
            }
            _parameterLinks.Add(link);
        }

        /// <summary>Adds multiple parameter links.</summary>
        internal void AddParameterLinks(IEnumerable<ParameterLink> links)
        {
            foreach (var link in links)
            {
                AddLink(link);
            }
        }

        /// <summary>Removes all links for a specific SubStateMachine.</summary>
        internal void RemoveLinksForSubMachine(SubStateMachineStateAsset subMachine)
        {
            for (int i = _parameterLinks.Count - 1; i >= 0; i--)
            {
                if (_parameterLinks[i].SubMachine == subMachine)
                    _parameterLinks.RemoveAt(i);
            }
        }

        /// <summary>Removes all links that reference a specific parameter (as source or target).</summary>
        internal void RemoveLinksForParameter(AnimationParameterAsset param)
        {
            for (int i = _parameterLinks.Count - 1; i >= 0; i--)
            {
                var link = _parameterLinks[i];
                if (link.SourceParameter == param || link.TargetParameter == param)
                    _parameterLinks.RemoveAt(i);
            }
        }

        /// <summary>Removes all exclusion markers for a specific SubStateMachine.</summary>
        internal void RemoveExclusionsForSubMachine(SubStateMachineStateAsset subMachine)
        {
            for (int i = _parameterLinks.Count - 1; i >= 0; i--)
            {
                var link = _parameterLinks[i];
                if (link.SubMachine == subMachine && link.IsExclusion)
                    _parameterLinks.RemoveAt(i);
            }
        }

        /// <summary>Removes a specific link matching source, target, and subMachine.</summary>
        internal void RemoveLink(AnimationParameterAsset source, AnimationParameterAsset target, SubStateMachineStateAsset subMachine)
        {
            for (int i = _parameterLinks.Count - 1; i >= 0; i--)
            {
                var link = _parameterLinks[i];
                if (link.SubMachine == subMachine && 
                    link.SourceParameter == source && 
                    link.TargetParameter == target)
                {
                    _parameterLinks.RemoveAt(i);
                }
            }
        }

        /// <summary>Finds a parameter link for a target parameter in a SubStateMachine.</summary>
        public ParameterLink? FindLinkForTarget(AnimationParameterAsset targetParam, SubStateMachineStateAsset subMachine)
        {
            foreach (var link in _parameterLinks)
            {
                if (link.TargetParameter == targetParam && link.SubMachine == subMachine)
                    return link;
            }
            return null;
        }

        /// <summary>Clears all dependency and link data.</summary>
        internal void ClearAllDependencyData()
        {
            _parameterDependencies.Clear();
            _parameterLinks.Clear();
        }

        #endregion

        public IEnumerable<AnimationClipAsset> Clips => States.SelectMany(s => s.Clips);
        public int ClipCount => States.Sum(s => s.ClipCount);

        #region Multi-Layer Support

        /// <summary>
        /// Returns true if this state machine contains layer assets (multi-layer mode).
        /// Single-layer mode has regular states at root; multi-layer has LayerStateAssets.
        /// </summary>
        public bool IsMultiLayer => States.OfType<LayerStateAsset>().Any();

        /// <summary>
        /// Gets all layer assets in this state machine.
        /// Returns empty if not in multi-layer mode.
        /// </summary>
        public IEnumerable<LayerStateAsset> GetLayers()
        {
            return States.OfType<LayerStateAsset>();
        }

        /// <summary>
        /// Gets the number of layers. Returns 0 for single-layer state machines.
        /// </summary>
        public int LayerCount => States.OfType<LayerStateAsset>().Count();

        /// <summary>
        /// Gets a layer by index. Returns null if index is out of range or not multi-layer.
        /// </summary>
        public LayerStateAsset GetLayer(int index)
        {
            var layers = States.OfType<LayerStateAsset>().ToList();
            if (index < 0 || index >= layers.Count)
                return null;
            return layers[index];
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Converts this single-layer state machine to multi-layer by:
        /// 1. Creating a new LayerStateAsset ("Base Layer")
        /// 2. Moving all existing states into the layer's nested state machine
        /// 3. Clearing root states and adding only the layer
        /// 
        /// Call this before adding additional layers.
        /// </summary>
        public LayerStateAsset ConvertToMultiLayer()
        {
            if (IsMultiLayer)
            {
                Debug.LogWarning($"[{name}] Already in multi-layer mode.", this);
                return GetLayers().FirstOrDefault();
            }

            // Create the base layer asset
            var baseLayer = ScriptableObject.CreateInstance<LayerStateAsset>();
            baseLayer.name = "Base Layer";
            baseLayer.Weight = 1f;
            baseLayer.BlendMode = LayerBlendMode.Override;

            // Create nested state machine for the layer
            var nestedMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            nestedMachine.name = $"{name}_BaseLayer";

            // Move all existing states to the nested machine
            nestedMachine.States = new List<AnimationStateAsset>(States);
            nestedMachine.DefaultState = DefaultState;
            nestedMachine.Parameters = new List<AnimationParameterAsset>(Parameters);
            nestedMachine.AnyStateTransitions = new List<StateOutTransition>(AnyStateTransitions);
            nestedMachine.ExitStates = new List<AnimationStateAsset>(ExitStates);

            // Copy rig binding
            nestedMachine._boundArmatureData = _boundArmatureData;
            nestedMachine._rigBindingStatus = _rigBindingStatus;
            nestedMachine._rigBindingSource = _rigBindingSource;
            nestedMachine._rigBindingFingerprint = _rigBindingFingerprint;

            // Assign nested machine to layer
            baseLayer.NestedStateMachine = nestedMachine;

            // Add as sub-assets
            var path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                UnityEditor.AssetDatabase.AddObjectToAsset(baseLayer, this);
                UnityEditor.AssetDatabase.AddObjectToAsset(nestedMachine, this);
            }

            // Clear root and add only the layer
            States.Clear();
            States.Add(baseLayer);
            DefaultState = null; // Layers don't have a default state at root
            AnyStateTransitions.Clear(); // Any State is per-layer
            ExitStates.Clear();

            UnityEditor.EditorUtility.SetDirty(this);
            if (!string.IsNullOrEmpty(path))
            {
                UnityEditor.AssetDatabase.SaveAssets();
            }

            Debug.Log($"[{name}] Converted to multi-layer mode. Existing states moved to 'Base Layer'.", this);
            return baseLayer;
        }

        /// <summary>
        /// Adds a new layer to a multi-layer state machine.
        /// If not already multi-layer, converts first.
        /// </summary>
        /// <param name="layerName">Name for the new layer</param>
        /// <returns>The created layer asset</returns>
        public LayerStateAsset AddLayer(string layerName = null)
        {
            // Convert to multi-layer if needed
            if (!IsMultiLayer)
            {
                ConvertToMultiLayer();
            }

            var layerIndex = LayerCount;
            var actualLayerName = layerName ?? $"Layer {layerIndex}";

            // Create the layer asset
            var layer = ScriptableObject.CreateInstance<LayerStateAsset>();
            layer.name = actualLayerName;
            layer.Weight = 1f;
            layer.BlendMode = LayerBlendMode.Override;

            // Create nested state machine
            var nestedMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            nestedMachine.name = $"{this.name}_{actualLayerName.Replace(" ", "")}";

            // Copy rig binding from root
            nestedMachine._boundArmatureData = _boundArmatureData;
            nestedMachine._rigBindingStatus = _rigBindingStatus;
            nestedMachine._rigBindingSource = _rigBindingSource;
            nestedMachine._rigBindingFingerprint = _rigBindingFingerprint;

            layer.NestedStateMachine = nestedMachine;

            // Add as sub-assets
            var path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                UnityEditor.AssetDatabase.AddObjectToAsset(layer, this);
                UnityEditor.AssetDatabase.AddObjectToAsset(nestedMachine, this);
            }

            States.Add(layer);

            UnityEditor.EditorUtility.SetDirty(this);
            if (!string.IsNullOrEmpty(path))
            {
                UnityEditor.AssetDatabase.SaveAssets();
            }

            return layer;
        }

        /// <summary>
        /// Removes a layer from the state machine.
        /// Cannot remove the last layer - use single-layer mode instead.
        /// </summary>
        public bool RemoveLayer(LayerStateAsset layer)
        {
            if (layer == null || !States.Contains(layer))
                return false;

            if (LayerCount <= 1)
            {
                Debug.LogWarning($"[{name}] Cannot remove the last layer. Convert back to single-layer mode instead.", this);
                return false;
            }

            States.Remove(layer);

            // Remove sub-assets
            var path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                if (layer.NestedStateMachine != null)
                {
                    UnityEditor.AssetDatabase.RemoveObjectFromAsset(layer.NestedStateMachine);
                }
                UnityEditor.AssetDatabase.RemoveObjectFromAsset(layer);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            else
            {
                if (layer.NestedStateMachine != null)
                {
                    DestroyImmediate(layer.NestedStateMachine);
                }
                DestroyImmediate(layer);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }
        #endif

        #endregion

        #region Hierarchy Query APIs

        /// <summary>
        /// Gets all leaf states (Single/LinearBlend) with their hierarchical paths.
        /// SubStateMachines are traversed recursively - their nested states are included with full paths.
        /// </summary>
        public IEnumerable<StateWithPath> GetAllLeafStates()
        {
            return CollectLeafStatesRecursive(this, "", null);
        }

        /// <summary>
        /// Gets all leaf states belonging to a specific SubStateMachine group.
        /// Includes states in nested SubStateMachines within the group.
        /// </summary>
        public IEnumerable<AnimationStateAsset> GetStatesInGroup(SubStateMachineStateAsset group)
        {
            if (group == null || group.NestedStateMachine == null)
                yield break;

            foreach (var state in group.NestedStateMachine.States)
            {
                if (state is SubStateMachineStateAsset nestedGroup)
                {
                    foreach (var nestedState in GetStatesInGroup(nestedGroup))
                        yield return nestedState;
                }
                else
                {
                    yield return state;
                }
            }
        }

        /// <summary>
        /// Gets the hierarchical path for a specific state (e.g., "Combat/Attack/Slash").
        /// Returns null if the state is not found in this machine.
        /// </summary>
        public string GetStatePath(AnimationStateAsset state)
        {
            return FindStatePathRecursive(this, state, "");
        }

        /// <summary>
        /// Gets the parent SubStateMachine group for a state.
        /// Returns null if the state is at the root level or not found.
        /// </summary>
        public SubStateMachineStateAsset GetParentGroup(AnimationStateAsset state)
        {
            return FindParentGroupRecursive(this, state, null);
        }

        /// <summary>
        /// Finds states matching a path pattern.
        /// Supports wildcards: * matches any single segment, ** matches any number of segments.
        /// Examples: "Combat/*", "*/Attack", "**/Slash", "Combat/**"
        /// </summary>
        public IEnumerable<AnimationStateAsset> FindStatesByPath(string pattern)
        {
            var allStates = GetAllLeafStates().ToList();
            var regex = PatternToRegex(pattern);

            foreach (var stateWithPath in allStates)
            {
                if (regex.IsMatch(stateWithPath.Path))
                    yield return stateWithPath.State;
            }
        }

        /// <summary>
        /// Gets all SubStateMachine groups in the hierarchy (depth-first order).
        /// </summary>
        public IEnumerable<SubStateMachineStateAsset> GetAllGroups()
        {
            return CollectGroupsRecursive(this);
        }

        /// <summary>
        /// Gets direct child groups (SubStateMachines) at the root level.
        /// </summary>
        public IEnumerable<SubStateMachineStateAsset> GetRootGroups()
        {
            return States.OfType<SubStateMachineStateAsset>();
        }

        /// <summary>
        /// Gets the group hierarchy as a tree structure.
        /// Returns tuples of (group, depth) for tree visualization.
        /// </summary>
        public IEnumerable<(SubStateMachineStateAsset group, int depth)> GetGroupHierarchy()
        {
            return CollectGroupHierarchyRecursive(this, 0);
        }

        #endregion

        #region Private Helpers

        private static IEnumerable<StateWithPath> CollectLeafStatesRecursive(
            StateMachineAsset machine,
            string pathPrefix,
            SubStateMachineStateAsset currentParent)
        {
            foreach (var state in machine.States)
            {
                var statePath = string.IsNullOrEmpty(pathPrefix)
                    ? state.name
                    : $"{pathPrefix}/{state.name}";

                if (state is SubStateMachineStateAsset subMachine)
                {
                    if (subMachine.NestedStateMachine != null)
                    {
                        foreach (var nested in CollectLeafStatesRecursive(
                            subMachine.NestedStateMachine, statePath, subMachine))
                        {
                            yield return nested;
                        }
                    }
                }
                else
                {
                    yield return new StateWithPath(state, statePath, currentParent);
                }
            }
        }

        private static string FindStatePathRecursive(StateMachineAsset machine, AnimationStateAsset target, string pathPrefix)
        {
            foreach (var state in machine.States)
            {
                var statePath = string.IsNullOrEmpty(pathPrefix)
                    ? state.name
                    : $"{pathPrefix}/{state.name}";

                if (state == target)
                    return statePath;

                if (state is SubStateMachineStateAsset subMachine && subMachine.NestedStateMachine != null)
                {
                    var found = FindStatePathRecursive(subMachine.NestedStateMachine, target, statePath);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static SubStateMachineStateAsset FindParentGroupRecursive(
            StateMachineAsset machine,
            AnimationStateAsset target,
            SubStateMachineStateAsset currentParent)
        {
            foreach (var state in machine.States)
            {
                if (state == target)
                    return currentParent;

                if (state is SubStateMachineStateAsset subMachine && subMachine.NestedStateMachine != null)
                {
                    var found = FindParentGroupRecursive(subMachine.NestedStateMachine, target, subMachine);
                    if (found != null || subMachine.NestedStateMachine.States.Contains(target))
                        return found ?? subMachine;
                }
            }

            return null;
        }

        private static IEnumerable<SubStateMachineStateAsset> CollectGroupsRecursive(StateMachineAsset machine)
        {
            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subMachine)
                {
                    yield return subMachine;

                    if (subMachine.NestedStateMachine != null)
                    {
                        foreach (var nested in CollectGroupsRecursive(subMachine.NestedStateMachine))
                            yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<(SubStateMachineStateAsset, int)> CollectGroupHierarchyRecursive(
            StateMachineAsset machine, int depth)
        {
            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subMachine)
                {
                    yield return (subMachine, depth);

                    if (subMachine.NestedStateMachine != null)
                    {
                        foreach (var nested in CollectGroupHierarchyRecursive(subMachine.NestedStateMachine, depth + 1))
                            yield return nested;
                    }
                }
            }
        }

        private static Regex PatternToRegex(string pattern)
        {
            // Escape regex special chars except our wildcards
            var escaped = Regex.Escape(pattern);

            // Replace escaped wildcards with regex patterns
            // ** matches any path (including slashes)
            escaped = escaped.Replace(@"\*\*", ".*");
            // * matches single segment (no slashes)
            escaped = escaped.Replace(@"\*", @"[^/]*");

            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase);
        }

        #endregion
    }
}