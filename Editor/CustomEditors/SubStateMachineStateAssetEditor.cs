using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Custom editor for SubStateMachineStateAsset when selected in the Project view.
    /// Exit transitions are simply OutTransitions - create a transition FROM this node TO another.
    /// </summary>
    [CustomEditor(typeof(SubStateMachineStateAsset))]
    internal class SubStateMachineStateAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty nestedStateMachineProperty;
        private SerializedProperty entryStateProperty;
        
        // Common state properties
        private SerializedProperty loopProperty;
        private SerializedProperty speedProperty;
        private SerializedProperty outTransitionsProperty;
        
        // Performance caching
        private StateMachineAsset cachedNestedMachine;
        private AnimationStateAsset[] cachedAvailableStates;
        private string[] cachedStateNames;
        
        // Cached popup options to avoid per-frame allocations
        private static readonly string[] NoStatesInNestedMachineOptions = { "No states in nested machine" };
        private static readonly string[] NoNestedMachineOptions = { "No nested machine" };

        private void OnEnable()
        {
            nestedStateMachineProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.NestedStateMachine));
            entryStateProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.EntryState));
            
            // Common AnimationStateAsset properties
            loopProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.Loop));
            speedProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.Speed));
            outTransitionsProperty = serializedObject.FindProperty(nameof(SubStateMachineStateAsset.OutTransitions));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawBasicProperties();
                EditorGUILayout.Space();
                DrawNestedStateMachineConfiguration();
                EditorGUILayout.Space();
                DrawTransitions();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBasicProperties()
        {
            EditorGUILayout.LabelField("Basic Properties", EditorStyles.boldLabel);
            
            // Name (read-only, managed by graph view)
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Name", target.name);
            }
            
            EditorGUILayout.PropertyField(loopProperty);
            EditorGUILayout.PropertyField(speedProperty);
        }

        private void DrawNestedStateMachineConfiguration()
        {
            EditorGUILayout.LabelField("Sub-State Machine Configuration", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(nestedStateMachineProperty, new GUIContent("Nested State Machine"));
            
            // Entry State Picker
            DrawEntryStatePicker();
            
            // Validation messages
            var subMachine = target as SubStateMachineStateAsset;
            if (subMachine.NestedStateMachine == null)
            {
                EditorGUILayout.HelpBox("A nested state machine must be assigned.", MessageType.Error);
            }
            else if (subMachine.EntryState == null)
            {
                EditorGUILayout.HelpBox("An entry state must be selected from the nested state machine.", MessageType.Warning);
            }
            else if (!IsStateInNestedMachine(subMachine.EntryState, subMachine.NestedStateMachine))
            {
                EditorGUILayout.HelpBox($"Entry state '{subMachine.EntryState.name}' is not in the nested state machine.", MessageType.Error);
            }
            
            // Help about exit transitions
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Exit transitions: Draw a transition FROM this SubStateMachine TO another state in the parent graph. " +
                "These OutTransitions below are evaluated when exiting this sub-machine.", 
                MessageType.Info);
        }

        private void DrawEntryStatePicker()
        {
            var subMachine = target as SubStateMachineStateAsset;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Entry State");
            
            EditorGUI.BeginChangeCheck();
            
            if (subMachine.NestedStateMachine != null)
            {
                var availableStates = GetAvailableStatesFromNestedMachine(subMachine.NestedStateMachine);
                if (availableStates.Length > 0)
                {
                    // Build state names array without LINQ
                    var stateNames = GetStateNamesArray(availableStates);
                    var currentIndex = System.Array.IndexOf(availableStates, subMachine.EntryState);
                    
                    if (currentIndex == -1) currentIndex = 0;
                    
                    var newIndex = EditorGUILayout.Popup(currentIndex, stateNames);
                    if (newIndex != currentIndex && newIndex >= 0 && newIndex < availableStates.Length)
                    {
                        entryStateProperty.objectReferenceValue = availableStates[newIndex];
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Popup(0, NoStatesInNestedMachineOptions);
                    }
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(0, NoNestedMachineOptions);
                }
            }
            
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTransitions()
        {
            var subMachine = target as SubStateMachineStateAsset;
            
            EditorGUILayout.LabelField("Exit Transitions (OutTransitions)", EditorStyles.boldLabel);
            
            if (subMachine.OutTransitions.Count > 0)
            {
                foreach (var transition in subMachine.OutTransitions)
                {
                    StateMachineEditorUtils.DrawTransitionSummary(subMachine, transition.ToState, transition.TransitionDuration);
                }
            }
            else
            {
                EditorGUILayout.LabelField("No exit transitions defined.", EditorStyles.miniLabel);
            }
        }

        private AnimationStateAsset[] GetAvailableStatesFromNestedMachine(StateMachineAsset nestedMachine)
        {
            if (nestedMachine == null) return System.Array.Empty<AnimationStateAsset>();
            
            if (cachedNestedMachine != nestedMachine || cachedAvailableStates == null)
            {
                cachedNestedMachine = nestedMachine;
                var leafStates = new List<AnimationStateAsset>();
                CollectLeafStatesRecursive(nestedMachine, leafStates);
                cachedAvailableStates = leafStates.ToArray();
                cachedStateNames = null; // Invalidate state names cache
            }
            
            return cachedAvailableStates;
        }
        
        private string[] GetStateNamesArray(AnimationStateAsset[] states)
        {
            // Rebuild names array if states changed
            if (cachedStateNames == null || cachedStateNames.Length != states.Length)
            {
                cachedStateNames = new string[states.Length];
                for (int i = 0; i < states.Length; i++)
                {
                    cachedStateNames[i] = states[i] != null ? states[i].name : "None";
                }
            }
            return cachedStateNames;
        }

        private void CollectLeafStatesRecursive(StateMachineAsset machine, List<AnimationStateAsset> leafStates, HashSet<StateMachineAsset> visited = null)
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

        private bool IsStateInNestedMachine(AnimationStateAsset state, StateMachineAsset machine, HashSet<StateMachineAsset> visited = null)
        {
            if (machine == null || state == null) return false;
            
            visited ??= new HashSet<StateMachineAsset>();
            if (visited.Contains(machine)) return false;
            visited.Add(machine);
            
            foreach (var s in machine.States)
            {
                if (s == state)
                {
                    visited.Remove(machine);
                    return true;
                }
                
                if (s is SubStateMachineStateAsset nestedSub && nestedSub.NestedStateMachine != null)
                {
                    if (IsStateInNestedMachine(state, nestedSub.NestedStateMachine, visited))
                    {
                        visited.Remove(machine);
                        return true;
                    }
                }
            }
            
            visited.Remove(machine);
            return false;
        }
    }
}
