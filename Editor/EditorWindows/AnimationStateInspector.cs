using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    internal class SingleStateInspector : AnimationStateInspector
    {
        private SerializedProperty clipProperty;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (target == null) return;
            InitializeClipProperty();
        }
        
        private void InitializeClipProperty()
        {
            if (clipProperty != null) return;
            clipProperty = serializedObject.FindProperty(nameof(SingleClipStateAsset.Clip));
        }

        protected override void DrawChildProperties()
        {
            InitializeClipProperty();
            EditorGUILayout.PropertyField(clipProperty);
        }
    }

    internal class LinearBlendStateInspector : AnimationStateInspector
    {
        private SerializedProperty blendParameterProperty;
        private SerializedProperty clipsProperty;
        private SerializedProperty intRangeMinProperty;
        private SerializedProperty intRangeMaxProperty;
        private SubAssetReferencePopupSelector<AnimationParameterAsset> blendParametersSelector;
        
        // Visual editor
        private BlendSpace1DVisualEditor blendSpaceEditor;
        private bool showVisualEditor = true;
        private float visualEditorHeight = 100f;
        private const float MinVisualEditorHeight = 60f;
        private const float MaxVisualEditorHeight = 200f;

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
                blendSpaceEditor.OnClipThresholdChanged -= OnClipThresholdChanged;
                blendSpaceEditor.OnSelectionChanged -= OnSelectionChanged;
            }
        }
        
        private void InitializeBlendProperties()
        {
            if (blendParameterProperty != null) return;
            
            blendParameterProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.BlendParameter));
            clipsProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.BlendClips));
            intRangeMinProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.IntRangeMin));
            intRangeMaxProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.IntRangeMax));
            
            // Support both Float and Int parameters for blending
            blendParametersSelector = new SubAssetReferencePopupSelector<AnimationParameterAsset>(
                blendParameterProperty.serializedObject.targetObject,
                "Add a Float or Int parameter",
                typeof(FloatParameterAsset), typeof(IntParameterAsset));
        }

        private void InitializeVisualEditor()
        {
            if (blendSpaceEditor != null) return;
            
            blendSpaceEditor = new BlendSpace1DVisualEditor();
            blendSpaceEditor.OnClipThresholdChanged += OnClipThresholdChanged;
            blendSpaceEditor.OnSelectionChanged += OnSelectionChanged;
        }

        private void OnClipThresholdChanged(int index, float newThreshold)
        {
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
            
            blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), blendParameterProperty,
                GUIContentCache.BlendParameter);
            
            // Show Int range settings if using an Int parameter
            var linearBlendAsset = target as LinearBlendStateAsset;
            if (linearBlendAsset != null && linearBlendAsset.UsesIntParameter)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(intRangeMinProperty, GUIContentCache.Min);
                EditorGUILayout.PropertyField(intRangeMaxProperty, GUIContentCache.Max);
                EditorGUILayout.EndHorizontal();
                
                // Show normalized range info
                EditorGUILayout.HelpBox(
                    StringBuilderCache.FormatIntRange(linearBlendAsset.IntRangeMin, linearBlendAsset.IntRangeMax), 
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space();
            
            // Visual Editor section
            DrawVisualEditorSection(linearBlendAsset);
            
            EditorGUILayout.Space();
            
            // Motions list
            EditorGUILayout.PropertyField(clipsProperty);
        }

        private void DrawVisualEditorSection(LinearBlendStateAsset blendState)
        {
            if (blendState == null) return;
            
            // Header with foldout and controls
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            showVisualEditor = EditorGUILayout.Foldout(showVisualEditor, "Blend Track Preview", true);
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                blendSpaceEditor?.ResetView();
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (!showVisualEditor) return;
            
            // Height slider
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Height", GUILayout.Width(40));
            visualEditorHeight = EditorGUILayout.Slider(visualEditorHeight, MinVisualEditorHeight, MaxVisualEditorHeight);
            EditorGUILayout.EndHorizontal();
            
            // Determine threshold range
            float minThreshold = 0f;
            float maxThreshold = 1f;
            
            // Auto-detect range from clips if available
            if (blendState.BlendClips != null && blendState.BlendClips.Length > 0)
            {
                minThreshold = float.MaxValue;
                maxThreshold = float.MinValue;
                foreach (var clip in blendState.BlendClips)
                {
                    minThreshold = Mathf.Min(minThreshold, clip.Threshold);
                    maxThreshold = Mathf.Max(maxThreshold, clip.Threshold);
                }
                // Add some padding
                var range = maxThreshold - minThreshold;
                if (range < 0.1f) range = 1f;
                minThreshold -= range * 0.1f;
                maxThreshold += range * 0.1f;
            }
            
            // Visual editor area
            var visualRect = GUILayoutUtility.GetRect(
                GUIContent.none, 
                GUIStyle.none, 
                GUILayout.Height(visualEditorHeight),
                GUILayout.ExpandWidth(true));
            
            if (visualRect.width < 100)
            {
                visualRect.width = 100;
            }
            
            blendSpaceEditor.Draw(visualRect, blendState.BlendClips, serializedObject, minThreshold, maxThreshold);
            
            // Selected clip edit fields
            EditorGUILayout.Space(5);
            if (blendSpaceEditor.DrawSelectedClipFields(blendState.BlendClips, clipsProperty))
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }

    internal struct AnimationStateInspectorModel
    {
        internal StateNodeView StateView;
        internal AnimationStateAsset StateAsset => StateView.State;
    }

    internal abstract class AnimationStateInspector : StateMachineInspector<AnimationStateInspectorModel>
    {
        private SerializedProperty loopProperty;
        private SerializedProperty speedProperty;
        private SerializedProperty outTransitionsProperty;

        protected virtual void OnEnable()
        {
            // Guard against null target during Editor creation
            if (target == null) return;
            InitializeProperties();
        }
        
        private void InitializeProperties()
        {
            if (loopProperty != null) return; // Already initialized
            
            loopProperty = serializedObject.FindProperty(nameof(SingleClipStateAsset.Loop));
            speedProperty = serializedObject.FindProperty(nameof(SingleClipStateAsset.Speed));
            outTransitionsProperty = serializedObject.FindProperty(nameof(SingleClipStateAsset.OutTransitions));
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            
            // Lazy initialization if OnEnable was skipped
            InitializeProperties();
            
            // Header
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(GUIContentCache.StateInspector, EditorStyles.boldLabel);
            }
            
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawName();
                DrawLoopProperty();
                DrawSpeedProperty();
                DrawChildProperties();
                DrawTransitions();
            }
        }

        protected abstract void DrawChildProperties();

        protected void DrawName()
        {
            using (var c = new EditorGUI.ChangeCheckScope())
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(EditorGUIUtility.labelWidth));
                model.StateAsset.name = EditorGUILayout.TextField(model.StateAsset.name);
                if (c.changed)
                {
                    model.StateView.title = model.StateAsset.name;
                }
            }
        }

        protected void DrawLoopProperty()
        {
            EditorGUILayout.PropertyField(loopProperty);
        }

        protected void DrawSpeedProperty()
        {
            EditorGUILayout.PropertyField(speedProperty);
        }

        protected void DrawTransitions()
        {
            if (model.StateAsset.OutTransitions.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField(outTransitionsProperty.displayName);
            foreach (var transition in model.StateAsset.OutTransitions)
            {
                StateMachineEditorUtils.DrawTransitionSummary(model.StateAsset, transition.ToState,
                    transition.TransitionDuration);
            }
        }
    }
}