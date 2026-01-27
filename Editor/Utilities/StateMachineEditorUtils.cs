using System;
using System.Collections.Generic;
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
            // Build targets array for UndoScope - state machine + all states
            var targets = new UnityEngine.Object[stateMachineAsset.States.Count + 1];
            targets[0] = stateMachineAsset;
            for (int i = 0; i < stateMachineAsset.States.Count; i++)
            {
                targets[i + 1] = stateMachineAsset.States[i];
            }
            
            using (UndoScope.Begin("Delete State", targets))
            {
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
            }
            
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
            
            EditorState.Instance.NotifyParameterAdded(parameter);
            
            return parameter;
        }

        public static void DeleteParameter(this StateMachineAsset stateMachineAsset,
            AnimationParameterAsset parameterAsset, bool recursive = true)
        {
            // Record undo for the state machine before modifying
            Undo.RecordObject(stateMachineAsset, "Delete Parameter");
            
            // Record undo for all states that might be modified
            foreach (var state in stateMachineAsset.States)
            {
                Undo.RecordObject(state, "Delete Parameter");
            }
            
            // Remove all transitions that reference this parameter
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
            
            // Clean up AnyState transitions
            foreach (var anyTransition in stateMachineAsset.AnyStateTransitions)
            {
                if (anyTransition.Conditions == null) continue;
                for (int j = anyTransition.Conditions.Count - 1; j >= 0; j--)
                {
                    if (anyTransition.Conditions[j].Parameter == parameterAsset)
                        anyTransition.Conditions.RemoveAt(j);
                }
            }
            
            // Clean up AnyState exit transition
            if (stateMachineAsset.AnyStateExitTransition?.Conditions != null)
            {
                var exitConditions = stateMachineAsset.AnyStateExitTransition.Conditions;
                for (int j = exitConditions.Count - 1; j >= 0; j--)
                {
                    if (exitConditions[j].Parameter == parameterAsset)
                        exitConditions.RemoveAt(j);
                }
            }
            
            // Clear state references to this parameter (speed, blend params)
            foreach (var state in stateMachineAsset.States)
            {
                if (state.SpeedParameter == parameterAsset)
                    state.SpeedParameter = null;
                    
                if (state is LinearBlendStateAsset blendState && blendState.BlendParameter == parameterAsset)
                    blendState.BlendParameter = null;
                    
                if (state is Directional2DBlendStateAsset blend2D)
                {
                    if (blend2D.BlendParameterX == parameterAsset)
                        blend2D.BlendParameterX = null;
                    if (blend2D.BlendParameterY == parameterAsset)
                        blend2D.BlendParameterY = null;
                }
            }
            
            // Delete linked target parameters (if this param is a link source, targets are also orphaned)
            if (recursive)
            {
                DeleteLinkedTargetParameters(stateMachineAsset, parameterAsset);
            }
            
            // Recursive deletion in nested state machines (by name/type matching)
            if (recursive)
            {
                DeleteParameterRecursive(stateMachineAsset, parameterAsset);
            }
            
            // Remove any links involving this parameter (use existing method on StateMachineAsset)
            stateMachineAsset.RemoveLinksForParameter(parameterAsset);

            stateMachineAsset.Parameters.Remove(parameterAsset);
            
            // Use Undo.DestroyObjectImmediate for proper undo support
            Undo.DestroyObjectImmediate(parameterAsset);
            
            AssetDatabase.SaveAssets();
        }
        
        /// <summary>
        /// Deletes target parameters that are linked from the given source parameter.
        /// If parent param is orphaned, linked child params are also orphaned.
        /// </summary>
        private static void DeleteLinkedTargetParameters(StateMachineAsset machine, AnimationParameterAsset sourceParam)
        {
            if (machine.ParameterLinks == null) return;
            
            // Collect links where this param is the source
            var linksToProcess = new List<ParameterLink>();
            foreach (var link in machine.ParameterLinks)
            {
                if (link.SourceParameter == sourceParam && link.TargetParameter != null)
                {
                    linksToProcess.Add(link);
                }
            }
            
            // Delete target parameters from their nested machines
            foreach (var link in linksToProcess)
            {
                var nestedMachine = link.NestedContainer?.NestedStateMachine;
                if (nestedMachine == null) continue;
                
                var targetParam = link.TargetParameter;
                if (targetParam == null) continue;
                
                // Check if target param actually exists in the nested machine
                if (nestedMachine.Parameters.Contains(targetParam))
                {
                    Undo.RecordObject(nestedMachine, "Delete Linked Parameter");
                    nestedMachine.DeleteParameter(targetParam, recursive: true);
                }
            }
        }
        
        /// <summary>
        /// Recursively deletes matching parameters (by name and type) from nested state machines.
        /// </summary>
        private static void DeleteParameterRecursive(StateMachineAsset machine, AnimationParameterAsset parameterAsset)
        {
            foreach (var state in machine.States)
            {
                StateMachineAsset nestedMachine = state switch
                {
                    SubStateMachineStateAsset sub => sub.NestedStateMachine,
                    LayerStateAsset layer => layer.NestedStateMachine,
                    _ => null
                };
                
                if (nestedMachine == null) continue;
                
                // Find matching parameter in nested machine (same name and type)
                AnimationParameterAsset matchingParam = null;
                foreach (var p in nestedMachine.Parameters)
                {
                    if (p != null && p.name == parameterAsset.name && p.GetType() == parameterAsset.GetType())
                    {
                        matchingParam = p;
                        break;
                    }
                }
                
                if (matchingParam != null)
                {
                    // Delete from nested machine (recursive: true to go deeper)
                    nestedMachine.DeleteParameter(matchingParam, recursive: true);
                }
                else
                {
                    // Still recurse deeper even if no match at this level
                    DeleteParameterRecursive(nestedMachine, parameterAsset);
                }
            }
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
                EditorState.Instance.NotifyDefaultStateChanged(state, previousDefault);
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