using System.Collections.Generic;
using System.IO;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Inspector for SubStateMachineStateAsset shown in the embedded inspector view.
    /// Exit transitions are simply the OutTransitions (inherited) - draw a transition FROM
    /// the SubStateMachine node TO another node to create an exit transition.
    /// </summary>
    internal class SubStateMachineInspector : AnimationStateInspector
    {
        private SerializedProperty nestedStateMachineProperty;
        private SerializedProperty entryStateProperty;

        // Performance caching
        private StateMachineAsset cachedNestedMachine;
        private AnimationStateAsset[] cachedAvailableStates;
        
        // Cached labels
        private static readonly GUIContent AutoResolveLabel = new GUIContent("Auto-Resolve");
        private static readonly string[] NoStatesAvailable = { "No states available" };
        
        // Cached state names array (resized as needed)
        private string[] _cachedStateNames;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (target == null) return;
            InitializeSubMachineProperties();
        }
        
        private void InitializeSubMachineProperties()
        {
            if (nestedStateMachineProperty != null) return;
            
            nestedStateMachineProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.NestedStateMachine));
            entryStateProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.EntryState));
        }

        protected override void DrawChildProperties()
        {
            InitializeSubMachineProperties();
            EditorGUILayout.Space();
            DrawNestedStateMachineSection();
        }

        private void DrawNestedStateMachineSection()
        {
            EditorGUILayout.LabelField("Sub-State Machine", EditorStyles.boldLabel);

            var subMachine = target as SubStateMachineStateAsset;
            var parentStateMachine = GetParentStateMachine();

            // Nested State Machine field with Create New button
            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(nestedStateMachineProperty, GUIContentCache.NestedMachine);
            if (EditorGUI.EndChangeCheck())
            {
                // Validate the assigned value
                var newValue = nestedStateMachineProperty.objectReferenceValue as StateMachineAsset;
                if (newValue != null && WouldCreateCircularReference(newValue, parentStateMachine))
                {
                    Debug.LogWarning($"Cannot assign '{newValue.name}' - it would create a circular reference.");
                    nestedStateMachineProperty.objectReferenceValue = null;
                }
            }
            
            if (GUILayout.Button("New", GUILayout.Width(40)))
            {
                CreateNewNestedStateMachine();
            }
            EditorGUILayout.EndHorizontal();

            // Entry State Picker
            if (subMachine.NestedStateMachine != null)
            {
                DrawEntryStatePicker(subMachine);
                
                EditorGUILayout.Space(4);
                DrawParameterDependencies(subMachine, parentStateMachine);
            }

            // Validation messages
            if (subMachine.NestedStateMachine == null)
            {
                EditorGUILayout.HelpBox("Assign a nested state machine or create a new one.", MessageType.Warning);
            }
            else if (subMachine.EntryState == null)
            {
                EditorGUILayout.HelpBox("Select an entry state from the nested machine.", MessageType.Warning);
            }
        }

        private void DrawParameterDependencies(SubStateMachineStateAsset subMachine, StateMachineAsset parentMachine)
        {
            if (parentMachine == null) return;

            var requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(subMachine);
            if (requirements.Count == 0) return;

            EditorGUILayout.LabelField("Parameter Dependencies", EditorStyles.boldLabel);

            // Analyze resolution status
            var resolved = 0;
            var missing = 0;
            foreach (var req in requirements)
            {
                var existing = ParameterDependencyAnalyzer.FindCompatibleParameter(parentMachine, req.Parameter);
                if (existing != null)
                    resolved++;
                else
                    missing++;
            }

            // Summary line
            using (new EditorGUILayout.HorizontalScope())
            {
                var statusIcon = missing > 0 ? IconCache.WarnIcon : IconCache.CheckmarkIcon;
                GUILayout.Label(statusIcon, GUILayout.Width(18));
                
                EditorGUILayout.LabelField(StringBuilderCache.FormatParametersResolved(resolved, requirements.Count), EditorStyles.miniLabel);

                if (missing > 0)
                {
                    if (GUILayout.Button(AutoResolveLabel, EditorStyles.miniButton, GUILayout.Width(80)))
                    {
                        AutoResolveParameters(subMachine, parentMachine);
                    }
                }
            }

            // Show missing parameters
            if (missing > 0)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < requirements.Count; i++)
                {
                    var req = requirements[i];
                    var existing = ParameterDependencyAnalyzer.FindCompatibleParameter(parentMachine, req.Parameter);
                    if (existing == null)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(IconCache.WarnIcon, GUILayout.Width(16));
                            EditorGUILayout.LabelField(StringBuilderCache.FormatNameWithType(req.Parameter.name, req.Parameter.ParameterTypeName), 
                                EditorStyles.miniLabel);
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        private void AutoResolveParameters(SubStateMachineStateAsset subMachine, StateMachineAsset parentMachine)
        {
            Undo.RecordObject(parentMachine, "Auto-Resolve Parameters");

            var result = ParameterDependencyAnalyzer.ResolveParameterDependencies(parentMachine, subMachine);

            // Register links
            if (result.HasLinks)
            {
                parentMachine.AddParameterLinks(result.ParameterLinks);
            }

            // Create missing parameters
            if (result.HasMissingParameters)
            {
                var assetPath = AssetDatabase.GetAssetPath(parentMachine);
                var created = ParameterDependencyAnalyzer.CreateMissingParameters(
                    parentMachine, subMachine, result.MissingParameters, assetPath);

                if (created.Count > 0)
                {
                    var sb = StringBuilderCache.Get();
                    for (int i = 0; i < created.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(created[i].name);
                    }
                    Debug.Log($"[DMotion] Created {created.Count} parameter(s) for '{subMachine.name}': {sb}");
                }
            }

            EditorUtility.SetDirty(parentMachine);
        }

        private StateMachineAsset GetParentStateMachine()
        {
            // The SubStateMachineStateAsset is a sub-asset of its parent StateMachineAsset
            var path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.LoadMainAssetAtPath(path) as StateMachineAsset;
        }

        // Cached HashSet for circular reference checks
        private HashSet<StateMachineAsset> _circularCheckVisited;

        private bool WouldCreateCircularReference(StateMachineAsset candidate, StateMachineAsset parent)
        {
            if (candidate == null || parent == null)
                return false;
            
            // Can't reference the parent directly
            if (candidate == parent)
                return true;
            
            // Check if candidate contains parent (would create cycle)
            if (_circularCheckVisited == null)
                _circularCheckVisited = new HashSet<StateMachineAsset>();
            else
                _circularCheckVisited.Clear();
            
            return ContainsStateMachine(candidate, parent, _circularCheckVisited);
        }

        private static bool ContainsStateMachine(StateMachineAsset machine, StateMachineAsset target, HashSet<StateMachineAsset> visited)
        {
            if (machine == null || target == null)
                return false;
            
            if (visited.Contains(machine))
                return false;
            visited.Add(machine);

            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subState && subState.NestedStateMachine != null)
                {
                    if (subState.NestedStateMachine == target)
                        return true;
                    
                    if (ContainsStateMachine(subState.NestedStateMachine, target, visited))
                        return true;
                }
            }

            return false;
        }

        private void DrawEntryStatePicker(SubStateMachineStateAsset subMachine)
        {
            var availableStates = GetAvailableStatesFromNestedMachine(subMachine.NestedStateMachine);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Entry State");

            if (availableStates.Length > 0)
            {
                // Build state names without LINQ allocation
                if (_cachedStateNames == null || _cachedStateNames.Length < availableStates.Length)
                {
                    _cachedStateNames = new string[availableStates.Length];
                }
                for (int i = 0; i < availableStates.Length; i++)
                {
                    _cachedStateNames[i] = availableStates[i] != null ? availableStates[i].name : "None";
                }
                
                var currentIndex = System.Array.IndexOf(availableStates, subMachine.EntryState);
                if (currentIndex == -1) currentIndex = 0;

                EditorGUI.BeginChangeCheck();
                var newIndex = EditorGUILayout.Popup(currentIndex, _cachedStateNames, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < availableStates.Length)
                {
                    entryStateProperty.objectReferenceValue = availableStates[newIndex];
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(0, NoStatesAvailable);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void CreateNewNestedStateMachine()
        {
            // Get the path of the current state machine asset
            var currentAssetPath = AssetDatabase.GetAssetPath(target);
            var directory = string.IsNullOrEmpty(currentAssetPath) 
                ? "Assets" 
                : Path.GetDirectoryName(currentAssetPath);

            var subMachine = target as SubStateMachineStateAsset;
            var defaultName = $"{subMachine.name}_StateMachine";

            var path = EditorUtility.SaveFilePanelInProject(
                "Create New State Machine",
                defaultName,
                "asset",
                "Choose a location for the new State Machine asset",
                directory);

            if (string.IsNullOrEmpty(path))
                return;

            // Create new StateMachineAsset
            var newStateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            AssetDatabase.CreateAsset(newStateMachine, path);
            AssetDatabase.SaveAssets();

            // Assign to this SubStateMachine
            nestedStateMachineProperty.objectReferenceValue = newStateMachine;
            serializedObject.ApplyModifiedProperties();

            // Ping the new asset
            EditorGUIUtility.PingObject(newStateMachine);
        }

        private AnimationStateAsset[] GetAvailableStatesFromNestedMachine(StateMachineAsset nestedMachine)
        {
            if (nestedMachine == null) return new AnimationStateAsset[0];

            if (cachedNestedMachine != nestedMachine || cachedAvailableStates == null)
            {
                cachedNestedMachine = nestedMachine;
                var leafStates = new List<AnimationStateAsset>();
                CollectLeafStatesRecursive(nestedMachine, leafStates);
                cachedAvailableStates = leafStates.ToArray();
            }

            return cachedAvailableStates;
        }

        // Cached HashSet for leaf state collection
        private HashSet<StateMachineAsset> _leafCollectionVisited;

        private void CollectLeafStatesRecursive(StateMachineAsset machine, List<AnimationStateAsset> leafStates,
            HashSet<StateMachineAsset> visited = null)
        {
            if (visited == null)
            {
                if (_leafCollectionVisited == null)
                    _leafCollectionVisited = new HashSet<StateMachineAsset>();
                else
                    _leafCollectionVisited.Clear();
                visited = _leafCollectionVisited;
            }
            
            if (visited.Contains(machine)) return;
            visited.Add(machine);

            var states = machine.States;
            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (state is SubStateMachineStateAsset subState && subState.NestedStateMachine != null)
                {
                    CollectLeafStatesRecursive(subState.NestedStateMachine, leafStates, visited);
                }
                else
                {
                    leafStates.Add(state);
                }
            }

            visited.Remove(machine);
        }
    }
}
