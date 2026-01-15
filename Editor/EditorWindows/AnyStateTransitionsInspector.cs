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
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                // Get the StateMachineAsset
                var stateMachineAsset = target as StateMachineAsset;
                if (stateMachineAsset == null)
                    return;

                var anyStateTransitionsProperty =
                    serializedObject.FindProperty(nameof(StateMachineAsset.AnyStateTransitions));

                // Find all Any State transitions that go to our destination state
                bool foundAny = false;
                var it = anyStateTransitionsProperty.GetEnumerator();
                while (it.MoveNext())
                {
                    var transitionProperty = (SerializedProperty)it.Current;
                    var toStateProperty = transitionProperty.FindPropertyRelative(nameof(StateOutTransition.ToState));

                    if (toStateProperty.objectReferenceValue == model.ToState)
                    {
                        foundAny = true;

                        var hasEndTimeProperty =
                            transitionProperty.FindPropertyRelative(nameof(StateOutTransition.HasEndTime));

                        var endTimeProperty =
                            transitionProperty.FindPropertyRelative(nameof(StateOutTransition.EndTime));

                        var transitionDurationProperty =
                            transitionProperty.FindPropertyRelative(nameof(StateOutTransition.TransitionDuration));

                        var conditionsProperty =
                            transitionProperty.FindPropertyRelative(nameof(StateOutTransition.Conditions));

                        // Draw summary
                        EditorGUILayout.LabelField("Any State Transition", EditorStyles.boldLabel);
                        EditorGUILayout.HelpBox(
                            $"Global transition from any state to '{model.ToState.name}'\n" +
                            $"Evaluated before regular state transitions",
                            MessageType.Info);

                        EditorGUILayout.Space();

                        // Draw properties
                        EditorGUILayout.PropertyField(hasEndTimeProperty, new GUIContent("Has Exit Time"));
                        if (hasEndTimeProperty.boolValue)
                        {
                            EditorGUILayout.PropertyField(endTimeProperty, new GUIContent("Exit Time (s)"));
                            EditorGUILayout.HelpBox(
                                "Exit time for Any State is absolute (not normalized) because the source state is unknown.",
                                MessageType.Warning);
                        }

                        EditorGUILayout.PropertyField(transitionDurationProperty, new GUIContent("Blend Duration (s)"));
                        EditorGUILayout.PropertyField(conditionsProperty, new GUIContent("Conditions (all must pass)"));

                        EditorGUILayout.Space();
                    }
                }

                if (!foundAny)
                {
                    EditorGUILayout.HelpBox("No Any State transition to this state", MessageType.Info);
                }

                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
