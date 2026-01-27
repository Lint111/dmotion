using System.Linq;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Custom editor section for multi-layer state machine management.
    /// Adds layer controls to the StateMachineAsset inspector when in multi-layer mode.
    /// </summary>
    [CustomEditor(typeof(StateMachineAsset))]
    public class StateMachineAssetLayerEditor : UnityEditor.Editor
    {
        private SerializedProperty statesProp;
        private SerializedProperty parametersProp;
        private bool showLayers = true;

        void OnEnable()
        {
            statesProp = serializedObject.FindProperty("States");
            parametersProp = serializedObject.FindProperty("Parameters");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var asset = (StateMachineAsset)target;
            
            if (asset.IsMultiLayer)
            {
                DrawMultiLayerInspector(asset);
            }
            else
            {
                DrawSingleLayerInspector(asset);
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSingleLayerInspector(StateMachineAsset asset)
        {
            // Standard inspector with "Convert to Multi-Layer" button
            EditorGUILayout.LabelField("Single-Layer State Machine", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            
            // Convert button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Convert to Multi-Layer", GUILayout.Width(160)))
            {
                if (EditorUtility.DisplayDialog(
                    "Convert to Multi-Layer",
                    "This will move all existing states into 'Base Layer' and enable multi-layer mode.\n\n" +
                    "You can then add additional layers for upper body, face, etc.\n\n" +
                    "Continue?",
                    "Convert", "Cancel"))
                {
                    Undo.RecordObject(asset, "Convert to Multi-Layer");
                    asset.ConvertToMultiLayer();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(8);
            
            // Draw default inspector for single-layer mode
            DrawDefaultInspector();
        }

        private void DrawMultiLayerInspector(StateMachineAsset asset)
        {
            EditorGUILayout.LabelField("Multi-Layer State Machine", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            
            // Capacity warning
            var layers = asset.GetLayers().ToList();
            if (layers.Count > 4)
            {
                EditorGUILayout.HelpBox(
                    $"Layer count ({layers.Count}) exceeds inline capacity (4). " +
                    "Layers beyond 4 use heap allocation (minor performance impact).",
                    MessageType.Warning);
            }
            
            EditorGUILayout.Space(4);
            
            // Layers section
            showLayers = EditorGUILayout.BeginFoldoutHeaderGroup(showLayers, $"Layers ({layers.Count})");
            if (showLayers)
            {
                EditorGUI.indentLevel++;
                
                for (int i = 0; i < layers.Count; i++)
                {
                    DrawLayerElement(i, layers[i], asset);
                }
                
                // Add layer button
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Add Layer", GUILayout.Width(100)))
                {
                    Undo.RecordObject(asset, "Add Layer");
                    asset.AddLayer();
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            EditorGUILayout.Space(8);
            
            // Shared Parameters section
            EditorGUILayout.LabelField("Shared Parameters", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Parameters defined here are shared across all layers. " +
                "Each layer can also define its own local parameters.",
                MessageType.Info);
            EditorGUILayout.PropertyField(parametersProp, true);
            
            EditorGUILayout.Space(8);
            
            // Rig binding section
            EditorGUILayout.LabelField("Rig Binding", EditorStyles.boldLabel);
            var rigProp = serializedObject.FindProperty("_boundArmatureData");
            EditorGUILayout.PropertyField(rigProp, new GUIContent("Bound Armature"));
        }

        private void DrawLayerElement(int index, LayerStateAsset layer, StateMachineAsset asset)
        {
            if (layer == null) return;
            
            bool isBaseLayer = index == 0;
            
            // Layer box
            var boxStyle = new GUIStyle(EditorStyles.helpBox);
            EditorGUILayout.BeginVertical(boxStyle);
            
            // Header row
            EditorGUILayout.BeginHorizontal();
            
            // Index badge
            var indexStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isBaseLayer ? FontStyle.Bold : FontStyle.Normal
            };
            GUILayout.Label(index.ToString(), indexStyle, GUILayout.Width(20));
            
            // Layer name
            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.TextField(layer.name);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Rename Layer");
                layer.name = newName;
                EditorUtility.SetDirty(layer);
            }
            
            // Open in editor button
            if (GUILayout.Button("Edit", GUILayout.Width(40)))
            {
                if (layer.NestedStateMachine != null)
                {
                    // Open in state machine editor window
                    // Support multiple windows by using OpenNewWindow pattern
                    StateMachineEditorWindowLauncher.OpenStateMachine(layer.NestedStateMachine);
                }
            }
            
            // Delete button
            EditorGUI.BeginDisabledGroup(asset.LayerCount <= 1);
            if (GUILayout.Button("x", GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog(
                    "Remove Layer",
                    $"Remove layer '{layer.name}'?\n\nThis will delete all states in this layer.",
                    "Remove", "Cancel"))
                {
                    Undo.RecordObject(asset, "Remove Layer");
                    asset.RemoveLayer(layer);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndHorizontal();
            
            // State machine reference (read-only)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("State Machine", layer.NestedStateMachine, typeof(StateMachineAsset), false);
            EditorGUI.EndDisabledGroup();
            
            // Weight and blend mode
            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginChangeCheck();
            var newWeight = EditorGUILayout.Slider("Weight", layer.Weight, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Change Layer Weight");
                layer.Weight = newWeight;
                EditorUtility.SetDirty(layer);
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Blend mode (disabled for base layer)
            EditorGUI.BeginDisabledGroup(isBaseLayer);
            EditorGUI.BeginChangeCheck();
            var newBlendMode = (LayerBlendMode)EditorGUILayout.EnumPopup("Blend Mode", layer.BlendMode);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Change Layer Blend Mode");
                layer.BlendMode = newBlendMode;
                EditorUtility.SetDirty(layer);
            }
            EditorGUI.EndDisabledGroup();
            
            if (isBaseLayer)
            {
                EditorGUILayout.LabelField("Base layer always uses Override", EditorStyles.miniLabel);
            }
            
            // Avatar Mask (disabled for base layer - base should be full body)
            EditorGUI.BeginDisabledGroup(isBaseLayer);
            EditorGUI.BeginChangeCheck();
            var newMask = (AvatarMask)EditorGUILayout.ObjectField(
                "Avatar Mask", 
                layer.AvatarMask, 
                typeof(AvatarMask), 
                false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Change Layer Avatar Mask");
                layer.AvatarMask = newMask;
                EditorUtility.SetDirty(layer);
            }
            EditorGUI.EndDisabledGroup();
            
            if (isBaseLayer)
            {
                EditorGUILayout.LabelField("Base layer affects full body", EditorStyles.miniLabel);
            }
            else if (layer.AvatarMask == null)
            {
                EditorGUILayout.LabelField("No mask = full body", EditorStyles.miniLabel);
            }
            
            // State count info
            var stateCount = layer.NestedStateMachine != null ? layer.NestedStateMachine.States.Count : 0;
            EditorGUILayout.LabelField($"States: {stateCount}", EditorStyles.miniLabel);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }
    }

    /// <summary>
    /// Helper to launch state machine editor windows.
    /// Supports opening multiple windows for side-by-side layer editing.
    /// </summary>
    public static class StateMachineEditorWindowLauncher
    {
        /// <summary>
        /// Opens a state machine in an editor window.
        /// Reuses existing window if the asset is already open.
        /// </summary>
        public static void OpenStateMachine(StateMachineAsset asset)
        {
            AnimationStateMachineEditorWindow.OpenWindow(asset);
        }
        
        /// <summary>
        /// Opens a state machine in a new window instance.
        /// Use for side-by-side layer editing.
        /// </summary>
        public static void OpenStateMachineInNewWindow(StateMachineAsset asset)
        {
            AnimationStateMachineEditorWindow.OpenWindow(asset, forceNewWindow: true);
        }
    }
}
