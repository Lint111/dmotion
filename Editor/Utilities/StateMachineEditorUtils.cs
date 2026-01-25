using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace DMotion.Editor
{
    internal static class StateMachineEditorUtils
    {
        public static AnimationStateAsset CreateState(this StateMachineAsset stateMachineAsset, Type type)
        {
            Assert.IsTrue(typeof(AnimationStateAsset).IsAssignableFrom(type));
            
            // Record undo for the state machine before modifying
            Undo.RecordObject(stateMachineAsset, "Create State");
            
            var state = ScriptableObject.CreateInstance(type) as AnimationStateAsset;
            Assert.IsNotNull(state);

            state.name = StringBuilderCache.FormatNewState(stateMachineAsset.States.Count + 1);
            state.StateEditorData.Guid = GUID.Generate().ToString();
            //TODO: Enable this later. Create editor tool to change this as well
            // state.hideFlags = HideFlags.HideInHierarchy;

            stateMachineAsset.States.Add(state);

            if (stateMachineAsset.DefaultState == null)
            {
                stateMachineAsset.SetDefaultState(state);
            }

            AssetDatabase.AddObjectToAsset(state, stateMachineAsset);
            
            // Register the created object with Undo so it can be destroyed on undo
            Undo.RegisterCreatedObjectUndo(state, "Create State");

            AssetDatabase.SaveAssets();
            return state;
        }

        public static void DeleteState(this StateMachineAsset stateMachineAsset, AnimationStateAsset stateAsset)
        {
            Undo.SetCurrentGroupName("Delete State");
            var undoGroup = Undo.GetCurrentGroup();
            
            // Record undo for the state machine
            Undo.RecordObject(stateMachineAsset, "Delete State");
            
            // Record undo for all states that might have transitions modified
            foreach (var state in stateMachineAsset.States)
            {
                Undo.RecordObject(state, "Delete State");
            }
            
            // Remove all transitions TO this state from other states
            foreach (var state in stateMachineAsset.States)
            {
                for (int i = state.OutTransitions.Count - 1; i >= 0; i--)
                {
                    if (state.OutTransitions[i].ToState == stateAsset)
                    {
                        state.OutTransitions.RemoveAt(i);
                    }
                }
            }
            
            // Remove Any State transitions to this state
            var anyTransitions = stateMachineAsset.AnyStateTransitions;
            for (int i = anyTransitions.Count - 1; i >= 0; i--)
            {
                if (anyTransitions[i].ToState == stateAsset)
                {
                    anyTransitions.RemoveAt(i);
                }
            }
            
            // Remove from exit states if present
            stateMachineAsset.ExitStates.Remove(stateAsset);

            stateMachineAsset.States.Remove(stateAsset);
            if (stateMachineAsset.DefaultState == stateAsset)
            {
                stateMachineAsset.DefaultState = stateMachineAsset.States.Count > 0 ? stateMachineAsset.States[0] : null;
            }
            
            // Use Undo.DestroyObjectImmediate for proper undo support
            Undo.DestroyObjectImmediate(stateAsset);
            
            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
        }

        public static AnimationParameterAsset CreateParameter<T>(this StateMachineAsset stateMachineAsset)
            where T : AnimationParameterAsset
        {
            return stateMachineAsset.CreateParameter(typeof(T));
        }

        public static AnimationParameterAsset CreateParameter(this StateMachineAsset stateMachineAsset, Type type)
        {
            Assert.IsTrue(typeof(AnimationParameterAsset).IsAssignableFrom(type));
            
            // Record undo for the state machine before modifying
            Undo.RecordObject(stateMachineAsset, "Create Parameter");
            
            var parameter = ScriptableObject.CreateInstance(type) as AnimationParameterAsset;
            Assert.IsNotNull(parameter);

            parameter.name = StringBuilderCache.FormatNewParameter(stateMachineAsset.Parameters.Count + 1);
            //TODO: Enable this later. Create editor tool to change this as well
            // state.hideFlags = HideFlags.HideInHierarchy;

            stateMachineAsset.Parameters.Add(parameter);
            AssetDatabase.AddObjectToAsset(parameter, stateMachineAsset);
            
            // Register the created object with Undo so it can be destroyed on undo
            Undo.RegisterCreatedObjectUndo(parameter, "Create Parameter");

            AssetDatabase.SaveAssets();
            
            StateMachineEditorEvents.RaiseParameterAdded(stateMachineAsset, parameter);
            
            return parameter;
        }

        public static void DeleteParameter(this StateMachineAsset stateMachineAsset,
            AnimationParameterAsset parameterAsset)
        {
            // Record undo for the state machine before modifying
            Undo.RecordObject(stateMachineAsset, "Delete Parameter");
            
            // Record undo for all states that might be modified
            foreach (var state in stateMachineAsset.States)
            {
                Undo.RecordObject(state, "Delete Parameter");
            }
            
            //Remove all transitions that reference this parameter
            foreach (var state in stateMachineAsset.States)
            {
                for (int i = state.OutTransitions.Count - 1; i >= 0; i--)
                {
                    var transition = state.OutTransitions[i];
                    var conditions = transition.Conditions;
                    for (int j = conditions.Count - 1; j >= 0; j--)
                    {
                        if (conditions[j].Parameter == parameterAsset)
                            conditions.RemoveAt(j);
                    }
                }
            }

            stateMachineAsset.Parameters.Remove(parameterAsset);
            
            // Use Undo.DestroyObjectImmediate for proper undo support
            Undo.DestroyObjectImmediate(parameterAsset);
            
            AssetDatabase.SaveAssets();
        }

        public static bool IsDefaultState(this StateMachineAsset stateMachineAsset, AnimationStateAsset state)
        {
            return stateMachineAsset.DefaultState == state;
        }

        public static void SetDefaultState(this StateMachineAsset stateMachineAsset, AnimationStateAsset state)
        {
            Assert.IsTrue(stateMachineAsset.States.Contains(state),
                $"State {state.name} not present in State machine {stateMachineAsset.name}");
            
            var previousDefault = stateMachineAsset.DefaultState;
            stateMachineAsset.DefaultState = state;
            
            if (previousDefault != state)
            {
                StateMachineEditorEvents.RaiseDefaultStateChanged(stateMachineAsset, state, previousDefault);
            }
        }

        internal static void DrawTransitionSummary(AnimationStateAsset fromState, AnimationStateAsset toState,
            float transitionTime)
        {
            // Guard against null states (can happen with broken references)
            if (fromState == null || toState == null)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    var oldColor = GUI.color;
                    GUI.color = Color.red;
                    EditorGUILayout.LabelField(fromState == null ? "Missing" : fromState.name);
                    EditorGUILayout.LabelField("--->");
                    EditorGUILayout.LabelField(toState == null ? "Missing" : toState.name);
                    GUI.color = oldColor;
                }
                return;
            }
            
            var labelWidth = Mathf.Min(EditorGUIUtility.labelWidth, 80f);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(fromState.name, GUILayout.Width(labelWidth));
                EditorGUILayout.LabelField("--->", GUILayout.Width(40f));
                EditorGUILayout.LabelField(toState.name, GUILayout.Width(labelWidth));
                
                // Format transition time without string interpolation
                var sb = StringBuilderCache.Get();
                sb.Append('(').Append(transitionTime).Append("s)");
                EditorGUILayout.LabelField(sb.ToString(), GUILayout.Width(50f));
            }
        }
    }
}