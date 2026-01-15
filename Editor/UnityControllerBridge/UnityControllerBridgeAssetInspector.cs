using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Custom inspector for UnityControllerBridgeAsset.
    /// Shows bridge status, conversion controls, and statistics.
    /// </summary>
    [CustomEditor(typeof(UnityControllerBridgeAsset))]
    public class UnityControllerBridgeAssetInspector : Editor
    {
        private UnityControllerBridgeAsset _bridge;

        private void OnEnable()
        {
            _bridge = (UnityControllerBridgeAsset)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            EditorGUILayout.Space();

            DrawBridgeInfo();
            EditorGUILayout.Space();

            DrawSourceSection();
            EditorGUILayout.Space();

            DrawGeneratedSection();
            EditorGUILayout.Space();

            DrawStatusSection();
            EditorGUILayout.Space();

            DrawActionsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Unity Controller Bridge", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Automatically converts Unity AnimatorController to DMotion StateMachineAsset. " +
                "The bridge monitors the source controller for changes and re-converts as needed.",
                MessageType.Info);
        }

        private void DrawBridgeInfo()
        {
            EditorGUILayout.LabelField("Bridge Info", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Bridge ID", _bridge.BridgeId);
            }
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

            SerializedProperty sourceControllerProp = serializedObject.FindProperty("_sourceController");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(sourceControllerProp, new GUIContent("Source Controller"));
            }

            if (_bridge.SourceController != null)
            {
                EditorGUILayout.LabelField("Controller Path", AssetDatabase.GetAssetPath(_bridge.SourceController));

                // Show parameters count
                int paramCount = _bridge.SourceController.parameters?.Length ?? 0;
                EditorGUILayout.LabelField("Parameters", paramCount.ToString());

                // Show layers count
                int layerCount = _bridge.SourceController.layers?.Length ?? 0;
                EditorGUILayout.LabelField("Layers", layerCount.ToString());

                // Show states count (base layer only)
                if (layerCount > 0)
                {
                    int stateCount = _bridge.SourceController.layers[0].stateMachine?.states?.Length ?? 0;
                    EditorGUILayout.LabelField("States (Base Layer)", stateCount.ToString());
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Source controller is missing!", MessageType.Error);
            }
        }

        private void DrawGeneratedSection()
        {
            EditorGUILayout.LabelField("Generated", EditorStyles.boldLabel);

            SerializedProperty generatedStateMachineProp = serializedObject.FindProperty("_generatedStateMachine");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(generatedStateMachineProp, new GUIContent("Generated State Machine"));
            }

            if (_bridge.GeneratedStateMachine != null)
            {
                EditorGUILayout.LabelField("Asset Path", AssetDatabase.GetAssetPath(_bridge.GeneratedStateMachine));

                // Show statistics
                EditorGUILayout.LabelField("Parameters", _bridge.GeneratedStateMachine.Parameters?.Count.ToString() ?? "0");
                EditorGUILayout.LabelField("States", _bridge.GeneratedStateMachine.States?.Count.ToString() ?? "0");

                if (_bridge.GeneratedStateMachine.DefaultState != null)
                {
                    EditorGUILayout.LabelField("Default State", _bridge.GeneratedStateMachine.DefaultState.name);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No generated state machine. Click 'Convert Now' to generate.", MessageType.Warning);
            }
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            // Dirty status
            string statusText = _bridge.IsDirty ? "DIRTY (needs conversion)" : "CLEAN (up to date)";
            MessageType statusType = _bridge.IsDirty ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(statusText, statusType);

            // Reference count
            int refCount = ControllerBridgeRegistry.GetReferenceCount(_bridge);
            EditorGUILayout.LabelField("Reference Count", refCount.ToString());

            if (refCount > 0)
            {
                if (GUILayout.Button("Find Entities Using This Bridge"))
                {
                    var entities = ControllerBridgeRegistry.FindEntitiesUsingController(_bridge.SourceController);
                    if (entities.Count > 0)
                    {
                        Debug.Log($"[Unity Controller Bridge] Found {entities.Count} entities using bridge '{_bridge.BridgeId}':");
                        foreach (var entity in entities)
                        {
                            Debug.Log($"  - {entity.gameObject.name}", entity.gameObject);
                        }
                    }
                    else
                    {
                        Debug.Log($"[Unity Controller Bridge] No entities found using bridge '{_bridge.BridgeId}'");
                    }
                }
            }
        }

        private void DrawActionsSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Convert Now"))
                {
                    ConvertBridge();
                }

                if (GUILayout.Button("Mark Dirty"))
                {
                    _bridge.MarkDirty();
                    EditorUtility.SetDirty(_bridge);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check for Changes"))
                {
                    bool hasChanges = _bridge.CheckForChanges();
                    if (hasChanges)
                    {
                        Debug.Log($"[Unity Controller Bridge] Bridge '{_bridge.BridgeId}' has changes and is now dirty");
                    }
                    else
                    {
                        Debug.Log($"[Unity Controller Bridge] Bridge '{_bridge.BridgeId}' has no changes");
                    }
                }

                using (new EditorGUI.DisabledScope(_bridge.IsDirty))
                {
                    if (GUILayout.Button("Mark Clean"))
                    {
                        _bridge.MarkClean();
                        EditorUtility.SetDirty(_bridge);
                    }
                }
            }
        }

        private void ConvertBridge()
        {
            if (_bridge.SourceController == null)
            {
                EditorUtility.DisplayDialog("Error", "Source controller is missing!", "OK");
                return;
            }

            ControllerConversionQueue.Enqueue(_bridge);

            // Track completion
            bool completed = false;
            bool success = false;

            void OnFinished(string bridgeId, bool s)
            {
                if (bridgeId == _bridge.BridgeId)
                {
                    completed = true;
                    success = s;
                }
            }

            ControllerConversionQueue.OnConversionFinished += OnFinished;

            // Start processing if not already
            if (!ControllerConversionQueue.IsProcessing)
            {
                ControllerConversionQueue.StartProcessing();
            }

            // Wait for completion (with timeout)
            double startTime = EditorApplication.timeSinceStartup;
            double timeout = 10.0;

            while (!completed && EditorApplication.timeSinceStartup - startTime < timeout)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                    "Converting Controller",
                    $"Converting '{_bridge.SourceController.name}'...",
                    0.5f))
                {
                    break;
                }
            }

            EditorUtility.ClearProgressBar();
            ControllerConversionQueue.OnConversionFinished -= OnFinished;

            if (completed)
            {
                if (success)
                {
                    EditorUtility.DisplayDialog("Success", "Controller converted successfully!", "OK");
                    Repaint();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Conversion failed. Check console for details.", "OK");
                }
            }
            else
            {
                Debug.LogWarning($"[Unity Controller Bridge] Conversion timed out or was cancelled");
            }
        }
    }
}
