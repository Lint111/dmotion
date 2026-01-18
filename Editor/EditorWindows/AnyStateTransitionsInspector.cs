using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    internal struct AnyStateInspectorModel
    {
        internal AnimationStateAsset ToState;
    }

    /// <summary>
    /// Inspector for Any State transitions in the visual editor.
    /// Shows all Any State transitions that lead to a specific destination state.
    /// </summary>
    internal class AnyStateTransitionsInspector : StateMachineInspector<AnyStateInspectorModel>
    {
        // Cache array size to maintain consistent GUI layout between events
        private int _cachedTransitionCount;
        private bool _showAll;
        private bool _hasExitTransition;
        
        public override void OnInspectorGUI()
        {
            // Guard against null target - must be done before any GUI calls
            if (target == null || serializedObject?.targetObject == null)
                return;
            
            serializedObject.Update();
            
            // Header
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Any State Transitions", EditorStyles.boldLabel);
            }
            
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                var anyStateTransitionsProperty =
                    serializedObject.FindProperty(nameof(StateMachineAsset.AnyStateTransitions));
                var anyStateExitProperty =
                    serializedObject.FindProperty("_anyStateExitTransition");
                
                if (anyStateTransitionsProperty == null)
                    return;

                // Cache values on Layout event for consistent GUI
                if (Event.current.type == EventType.Layout)
                {
                    _cachedTransitionCount = anyStateTransitionsProperty.arraySize;
                    _showAll = model.ToState == null;
                    var stateMachine = target as StateMachineAsset;
                    _hasExitTransition = stateMachine != null && stateMachine.HasAnyStateExitTransition;
                }

                bool foundAny = false;
                
                for (int i = 0; i < _cachedTransitionCount && i < anyStateTransitionsProperty.arraySize; i++)
                {
                    var transitionProperty = anyStateTransitionsProperty.GetArrayElementAtIndex(i);
                    var toStateProperty = transitionProperty.FindPropertyRelative(nameof(StateOutTransition.ToState));
                    var toState = toStateProperty.objectReferenceValue as AnimationStateAsset;

                    // Show this transition if showing all, or if it matches the specific ToState
                    if (_showAll || toState == model.ToState)
                    {
                        foundAny = true;
                        DrawTransition(transitionProperty, toState != null ? toState.name : "(none)");
                    }
                }
                
                // Show Any State -> Exit transition when showing all
                if (_showAll && _hasExitTransition)
                {
                    foundAny = true;
                    EditorGUILayout.Space(4);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("-> [EXIT]", EditorStyles.boldLabel);
                        EditorGUILayout.HelpBox(
                            "Conditional exit from any state. Add conditions below to control when this sub-machine can be exited.",
                            MessageType.Info);
                        
                        if (anyStateExitProperty != null)
                        {
                            var conditionsProperty = anyStateExitProperty.FindPropertyRelative(nameof(StateOutTransition.Conditions));
                            if (conditionsProperty != null)
                            {
                                EditorGUILayout.PropertyField(conditionsProperty, GUIContentCache.Conditions);
                            }
                        }
                    }
                    EditorGUILayout.Space(8);
                }

                if (!foundAny)
                {
                    EditorGUILayout.HelpBox(
                        _showAll ? "No Any State transitions defined.\nRight-click drag from Any State to a state or Exit node to create one." 
                                 : "No Any State transition to this state.", 
                        MessageType.Info);
                }

                serializedObject.ApplyModifiedProperties();
            }
        }
        
        private void DrawTransition(SerializedProperty transitionProperty, string targetName)
        {
            var hasEndTimeProperty =
                transitionProperty.FindPropertyRelative(nameof(StateOutTransition.HasEndTime));
            var endTimeProperty =
                transitionProperty.FindPropertyRelative(nameof(StateOutTransition.EndTime));
            var transitionDurationProperty =
                transitionProperty.FindPropertyRelative(nameof(StateOutTransition.TransitionDuration));
            var conditionsProperty =
                transitionProperty.FindPropertyRelative(nameof(StateOutTransition.Conditions));

            EditorGUILayout.LabelField(StringBuilderCache.FormatTransitionTarget(targetName), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(hasEndTimeProperty, GUIContentCache.HasExitTime);
            using (new EditorGUI.DisabledScope(!hasEndTimeProperty.boolValue))
            {
                EditorGUILayout.PropertyField(endTimeProperty, GUIContentCache.ExitTime);
            }
            EditorGUILayout.PropertyField(transitionDurationProperty, GUIContentCache.BlendDuration);
            EditorGUILayout.PropertyField(conditionsProperty, GUIContentCache.Conditions);
            
            EditorGUILayout.Space(8);
        }
    }
}
