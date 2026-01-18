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

        protected override void OnEnable()
        {
            base.OnEnable();
            if (target == null) return;
            InitializeBlendProperties();
        }
        
        private void InitializeBlendProperties()
        {
            if (clipsProperty != null) return;
            
            clipsProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.clips));
            parameterXProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendParameterX));
            parameterYProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendParameterY));
            
            blendParametersSelector = new SubAssetReferencePopupSelector<AnimationParameterAsset>(
                parameterXProperty.serializedObject.targetObject,
                "Add a Float parameter",
                typeof(FloatParameterAsset));
        }

        protected override void DrawChildProperties()
        {
            InitializeBlendProperties();
            
            EditorGUILayout.LabelField("Blend Parameters (Float)", EditorStyles.boldLabel);
            blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), parameterXProperty, new GUIContent("Parameter X"));
            blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), parameterYProperty, new GUIContent("Parameter Y"));
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Motions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(clipsProperty);
            
            // TODO: Add visual graph editor for 2D blend space
        }
    }
}
