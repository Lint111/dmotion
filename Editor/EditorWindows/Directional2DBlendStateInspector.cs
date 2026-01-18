using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    internal class Directional2DBlendStateInspector : AnimationStateInspector
    {
        private SerializedProperty clipsProperty;
        private SerializedProperty parameterXProperty;
        private SerializedProperty parameterYProperty;
        private SubAssetReferencePopupSelector<AnimationParameterAsset> blendParametersSelector;
        
        // Visual editor
        private BlendSpace2DVisualEditor blendSpaceEditor;
        private bool showVisualEditor = true;
        private float visualEditorHeight = 200f;
        private const float MinVisualEditorHeight = 100f;
        private const float MaxVisualEditorHeight = 400f;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (target == null) return;
            InitializeBlendProperties();
            InitializeVisualEditor();
        }

        private void OnDisable()
        {
            if (blendSpaceEditor != null)
            {
                blendSpaceEditor.OnClipPositionChanged -= OnClipPositionChanged;
                blendSpaceEditor.OnSelectionChanged -= OnSelectionChanged;
            }
        }
        
        private void InitializeBlendProperties()
        {
            if (clipsProperty != null) return;
            
            clipsProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendClips));
            parameterXProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendParameterX));
            parameterYProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendParameterY));
            
            blendParametersSelector = new SubAssetReferencePopupSelector<AnimationParameterAsset>(
                parameterXProperty.serializedObject.targetObject,
                "Add a Float parameter",
                typeof(FloatParameterAsset));
        }

        private void InitializeVisualEditor()
        {
            if (blendSpaceEditor != null) return;
            
            blendSpaceEditor = new BlendSpace2DVisualEditor();
            blendSpaceEditor.OnClipPositionChanged += OnClipPositionChanged;
            blendSpaceEditor.OnSelectionChanged += OnSelectionChanged;
        }

        private void OnClipPositionChanged(int index, Vector2 newPosition)
        {
            // Position change is handled by the editor via SerializedProperty
            // This callback can be used for additional effects if needed
            Repaint();
        }

        private void OnSelectionChanged(int index)
        {
            Repaint();
        }

        protected override void DrawChildProperties()
        {
            InitializeBlendProperties();
            InitializeVisualEditor();
            
            // Blend Parameters section
            EditorGUILayout.LabelField("Blend Parameters (Float)", EditorStyles.boldLabel);
            blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), parameterXProperty, new GUIContent("Parameter X"));
            blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), parameterYProperty, new GUIContent("Parameter Y"));
            
            EditorGUILayout.Space();
            
            // Visual Editor section
            DrawVisualEditorSection();
            
            EditorGUILayout.Space();
            
            // Motions list (collapsible)
            EditorGUILayout.LabelField("Motions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(clipsProperty);
        }

        private void DrawVisualEditorSection()
        {
            // Header with foldout and controls
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            showVisualEditor = EditorGUILayout.Foldout(showVisualEditor, "Blend Space Preview", true);
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                blendSpaceEditor?.ResetView();
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (!showVisualEditor) return;
            
            var blendState = target as Directional2DBlendStateAsset;
            if (blendState == null) return;
            
            // Height slider
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Height", GUILayout.Width(40));
            visualEditorHeight = EditorGUILayout.Slider(visualEditorHeight, MinVisualEditorHeight, MaxVisualEditorHeight);
            EditorGUILayout.EndHorizontal();
            
            // Visual editor area
            var visualRect = GUILayoutUtility.GetRect(
                GUIContent.none, 
                GUIStyle.none, 
                GUILayout.Height(visualEditorHeight),
                GUILayout.ExpandWidth(true));
            
            // Ensure minimum width
            if (visualRect.width < 100)
            {
                visualRect.width = 100;
            }
            
            blendSpaceEditor.Draw(visualRect, blendState.BlendClips, serializedObject);
            
            // Selected clip edit fields
            EditorGUILayout.Space(5);
            if (blendSpaceEditor.DrawSelectedClipFields(blendState.BlendClips, clipsProperty))
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
