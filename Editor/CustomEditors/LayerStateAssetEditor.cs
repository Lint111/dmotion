using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Custom editor for LayerStateAsset.
    /// Shows layer properties: weight, blend mode, avatar mask, and nested state machine.
    /// </summary>
    [CustomEditor(typeof(LayerStateAsset))]
    public class LayerStateAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty nestedStateMachineProp;
        private SerializedProperty weightProp;
        private SerializedProperty blendModeProp;
        private SerializedProperty avatarMaskProp;
        
        private bool showStatesList = true;

        private void OnEnable()
        {
            nestedStateMachineProp = serializedObject.FindProperty("nestedStateMachine");
            weightProp = serializedObject.FindProperty("Weight");
            blendModeProp = serializedObject.FindProperty("BlendMode");
            avatarMaskProp = serializedObject.FindProperty("avatarMask");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var layer = (LayerStateAsset)target;
            
            EditorGUILayout.LabelField("Animation Layer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            
            // Layer name (asset name)
            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.TextField("Name", layer.name);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName))
            {
                Undo.RecordObject(layer, "Rename Layer");
                layer.name = newName;
                AssetDatabase.SaveAssets();
            }
            
            EditorGUILayout.Space(8);
            
            // Blending Section
            EditorGUILayout.LabelField("Blending", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(weightProp, new GUIContent("Weight", "Layer influence (0 = no effect, 1 = full effect)"));
            EditorGUILayout.PropertyField(blendModeProp, new GUIContent("Blend Mode", "How this layer blends with layers below"));
            
            EditorGUILayout.Space(8);
            
            // Masking Section
            EditorGUILayout.LabelField("Bone Masking", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(avatarMaskProp, new GUIContent("Avatar Mask", "Defines which bones this layer affects"));
            
            if (layer.AvatarMask == null)
            {
                EditorGUILayout.HelpBox(
                    "No mask assigned. This layer will affect all bones (full body).\n\n" +
                    "Assign an Avatar Mask to limit this layer to specific bones (e.g., upper body only).",
                    MessageType.Info);
            }
            else
            {
                // Show mask info
                var mask = layer.AvatarMask;
                int activeCount = 0;
                for (int i = 0; i < mask.transformCount; i++)
                {
                    if (mask.GetTransformActive(i))
                        activeCount++;
                }
                EditorGUILayout.LabelField($"Active transforms: {activeCount}/{mask.transformCount}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.Space(8);
            
            // State Machine Section
            EditorGUILayout.LabelField("State Machine", EditorStyles.boldLabel);
            
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(nestedStateMachineProp, new GUIContent("Nested State Machine"));
            EditorGUI.EndDisabledGroup();
            
            if (layer.NestedStateMachine != null)
            {
                // Open button
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open State Machine Editor", GUILayout.Width(180)))
                {
                    StateMachineEditorWindowLauncher.OpenStateMachine(layer.NestedStateMachine);
                }
                EditorGUILayout.EndHorizontal();
                
                // States list
                showStatesList = EditorGUILayout.Foldout(showStatesList, $"States ({layer.NestedStateMachine.States.Count})", true);
                if (showStatesList)
                {
                    EditorGUI.indentLevel++;
                    foreach (var state in layer.NestedStateMachine.States)
                    {
                        if (state == null) continue;
                        
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(state.name, EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"[{state.Type}]", EditorStyles.miniLabel, GUILayout.Width(80));
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No state machine assigned. Create or assign a state machine for this layer.",
                    MessageType.Warning);
                
                if (GUILayout.Button("Create Nested State Machine"))
                {
                    CreateNestedStateMachine(layer);
                }
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void CreateNestedStateMachine(LayerStateAsset layer)
        {
            Undo.RecordObject(layer, "Create Nested State Machine");
            
            var stateMachine = CreateInstance<StateMachineAsset>();
            stateMachine.name = $"{layer.name}_StateMachine";
            
            // Add as sub-asset
            AssetDatabase.AddObjectToAsset(stateMachine, layer);
            
            // Assign to layer
            layer.NestedStateMachine = stateMachine;
            
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();
        }
    }
}
