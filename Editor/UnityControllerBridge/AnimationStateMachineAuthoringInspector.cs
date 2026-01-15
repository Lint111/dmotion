using DMotion.Authoring;
using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Custom inspector for AnimationStateMachineAuthoring with Unity Controller Bridge support.
    /// </summary>
    [CustomEditor(typeof(AnimationStateMachineAuthoring))]
    public class AnimationStateMachineAuthoringInspector : Editor
    {
        private AnimationStateMachineAuthoring _authoring;

        private SerializedProperty _ownerProp;
        private SerializedProperty _animatorProp;
        private SerializedProperty _sourceModeProp;
        private SerializedProperty _stateMachineAssetProp;
        private SerializedProperty _controllerBridgeProp;
        private SerializedProperty _rootMotionModeProp;
        private SerializedProperty _enableEventsProp;

        private void OnEnable()
        {
            _authoring = (AnimationStateMachineAuthoring)target;

            _ownerProp = serializedObject.FindProperty("Owner");
            _animatorProp = serializedObject.FindProperty("Animator");
            _sourceModeProp = serializedObject.FindProperty("SourceMode");
            _stateMachineAssetProp = serializedObject.FindProperty("StateMachineAsset");
            _controllerBridgeProp = serializedObject.FindProperty("ControllerBridge");
            _rootMotionModeProp = serializedObject.FindProperty("RootMotionMode");
            _enableEventsProp = serializedObject.FindProperty("EnableEvents");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            EditorGUILayout.Space();

            DrawBasicFields();
            EditorGUILayout.Space();

            DrawStateMachineSource();
            EditorGUILayout.Space();

            DrawAnimationSettings();
            EditorGUILayout.Space();

            DrawValidation();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Animation State Machine Authoring", EditorStyles.boldLabel);
        }

        private void DrawBasicFields()
        {
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_ownerProp);
            EditorGUILayout.PropertyField(_animatorProp);
        }

        private void DrawStateMachineSource()
        {
            EditorGUILayout.LabelField("State Machine Source", EditorStyles.boldLabel);

            // Source mode dropdown
            EditorGUILayout.PropertyField(_sourceModeProp);

            StateMachineSourceMode mode = (StateMachineSourceMode)_sourceModeProp.enumValueIndex;

            EditorGUILayout.Space(5);

            // Show appropriate field based on mode
            switch (mode)
            {
                case StateMachineSourceMode.Direct:
                    DrawDirectMode();
                    break;

                case StateMachineSourceMode.UnityControllerBridge:
                    DrawBridgeMode();
                    break;
            }
        }

        private void DrawDirectMode()
        {
            EditorGUILayout.HelpBox(
                "Direct mode: Manually assign a DMotion StateMachineAsset.",
                MessageType.Info);

            EditorGUILayout.PropertyField(_stateMachineAssetProp, new GUIContent("State Machine Asset"));

            if (_authoring.StateMachineAsset != null)
            {
                // Show quick stats
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Parameters", _authoring.StateMachineAsset.Parameters?.Count.ToString() ?? "0");
                    EditorGUILayout.LabelField("States", _authoring.StateMachineAsset.States?.Count.ToString() ?? "0");
                }
            }
        }

        private void DrawBridgeMode()
        {
            EditorGUILayout.HelpBox(
                "Bridge mode: Automatically converts Unity AnimatorController to DMotion StateMachineAsset. " +
                "The bridge monitors for controller changes and re-converts automatically.",
                MessageType.Info);

            EditorGUILayout.PropertyField(_controllerBridgeProp, new GUIContent("Controller Bridge"));

            if (_authoring.ControllerBridge != null)
            {
                var bridge = _authoring.ControllerBridge;

                // Show bridge status
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Bridge Status", bridge.IsDirty ? "DIRTY" : "CLEAN");

                    if (bridge.SourceController != null)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField("Source Controller", bridge.SourceController, typeof(UnityEditor.Animations.AnimatorController), false);
                        }
                    }

                    if (bridge.GeneratedStateMachine != null)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField("Generated State Machine", bridge.GeneratedStateMachine, typeof(StateMachineAsset), false);
                        }

                        EditorGUILayout.LabelField("Parameters", bridge.GeneratedStateMachine.Parameters?.Count.ToString() ?? "0");
                        EditorGUILayout.LabelField("States", bridge.GeneratedStateMachine.States?.Count.ToString() ?? "0");
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Bridge has no generated state machine yet.", MessageType.Warning);
                    }
                }

                // Bridge actions
                EditorGUILayout.Space(5);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Convert Bridge Now"))
                    {
                        ControllerConversionQueue.Enqueue(bridge);
                        if (!ControllerConversionQueue.IsProcessing)
                        {
                            ControllerConversionQueue.StartProcessing();
                        }
                        Debug.Log($"[Unity Controller Bridge] Enqueued bridge '{bridge.BridgeId}' for conversion");
                    }

                    if (GUILayout.Button("Select Bridge"))
                    {
                        Selection.activeObject = bridge;
                        EditorGUIUtility.PingObject(bridge);
                    }
                }

                // Show warning if bridge is dirty
                if (bridge.IsDirty)
                {
                    EditorGUILayout.HelpBox(
                        "Bridge is dirty and needs re-conversion. It will be automatically converted before entering play mode, " +
                        "or you can click 'Convert Bridge Now' to convert immediately.",
                        MessageType.Warning);
                }
            }
            else
            {
                // Help user create a bridge
                EditorGUILayout.HelpBox("No bridge assigned. Create a bridge from an AnimatorController asset.", MessageType.Warning);

                if (_authoring.Animator != null && _authoring.Animator.runtimeAnimatorController is UnityEditor.Animations.AnimatorController controller)
                {
                    if (GUILayout.Button("Create Bridge from Animator Controller"))
                    {
                        var bridge = ControllerBridgeRegistry.GetOrCreateBridge(controller);
                        _controllerBridgeProp.objectReferenceValue = bridge;
                        serializedObject.ApplyModifiedProperties();

                        Debug.Log($"[Unity Controller Bridge] Created bridge from Animator's controller");

                        // Ask if user wants to convert now
                        if (EditorUtility.DisplayDialog(
                            "Convert Now?",
                            "Would you like to convert the bridge now?",
                            "Convert Now",
                            "Later"))
                        {
                            ControllerConversionQueue.Enqueue(bridge);
                            ControllerConversionQueue.StartProcessing();
                        }
                    }
                }
            }
        }

        private void DrawAnimationSettings()
        {
            EditorGUILayout.LabelField("Animation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_rootMotionModeProp);
            EditorGUILayout.PropertyField(_enableEventsProp);
        }

        private void DrawValidation()
        {
            // Validate configuration
            var stateMachine = _authoring.GetStateMachine();

            if (stateMachine == null)
            {
                string message = _authoring.SourceMode switch
                {
                    StateMachineSourceMode.Direct => "No StateMachineAsset assigned",
                    StateMachineSourceMode.UnityControllerBridge => _authoring.ControllerBridge == null
                        ? "No ControllerBridge assigned"
                        : "Bridge has no generated StateMachine (needs conversion)",
                    _ => "Unknown source mode"
                };

                EditorGUILayout.HelpBox(message, MessageType.Error);
            }

            if (_authoring.Animator == null)
            {
                EditorGUILayout.HelpBox("No Animator assigned", MessageType.Warning);
            }
        }
    }
}
