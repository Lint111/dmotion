using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        protected override void OnEnable()
        {
            base.OnEnable();
            nestedStateMachineProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.NestedStateMachine));
            entryStateProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.EntryState));
        }

        protected override void DrawChildProperties()
        {
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
            EditorGUILayout.PropertyField(nestedStateMachineProperty, new GUIContent("Nested Machine"));
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

        private StateMachineAsset GetParentStateMachine()
        {
            // The SubStateMachineStateAsset is a sub-asset of its parent StateMachineAsset
            var path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.LoadMainAssetAtPath(path) as StateMachineAsset;
        }

        private bool WouldCreateCircularReference(StateMachineAsset candidate, StateMachineAsset parent)
        {
            if (candidate == null || parent == null)
                return false;
            
            // Can't reference the parent directly
            if (candidate == parent)
                return true;
            
            // Check if candidate contains parent (would create cycle)
            return ContainsStateMachine(candidate, parent, new HashSet<StateMachineAsset>());
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
                var stateNames = availableStates.Select(s => s != null ? s.name : "None").ToArray();
                var currentIndex = System.Array.IndexOf(availableStates, subMachine.EntryState);
                if (currentIndex == -1) currentIndex = 0;

                EditorGUI.BeginChangeCheck();
                var newIndex = EditorGUILayout.Popup(currentIndex, stateNames);
                if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < availableStates.Length)
                {
                    entryStateProperty.objectReferenceValue = availableStates[newIndex];
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(0, new[] { "No states available" });
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

        private void CollectLeafStatesRecursive(StateMachineAsset machine, List<AnimationStateAsset> leafStates,
            HashSet<StateMachineAsset> visited = null)
        {
            if (visited == null) visited = new HashSet<StateMachineAsset>();
            if (visited.Contains(machine)) return;
            visited.Add(machine);

            foreach (var state in machine.States)
            {
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
