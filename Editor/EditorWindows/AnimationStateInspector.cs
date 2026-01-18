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
        private float visualEditorHeight = 100f;
        private const float MinVisualEditorHeight = 60f;
        private const float MaxVisualEditorHeight = 200f;
        
        // Dockable sections
        private DockablePanelSection parametersSection;
        private DockablePanelSection blendTrackSection;
        private DockablePanelSection motionsSection;
        
        // Undocked window reference
        private BlendSpaceEditorWindow undockedWindow;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (target == null) return;
            InitializeBlendProperties();
            InitializeVisualEditor();
            InitializeSections();
        }

        private void OnDisable()
        {
            if (blendSpaceEditor != null)
            {
                blendSpaceEditor.OnClipThresholdChanged -= OnClipThresholdChanged;
                blendSpaceEditor.OnSelectionChanged -= OnSelectionChanged;
            }
            
            if (undockedWindow != null)
            {
                undockedWindow.Close();
                undockedWindow = null;
            }
        }
        
        private void InitializeBlendProperties()
        {
            if (blendParameterProperty != null) return;
            
            blendParameterProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.BlendParameter));
            clipsProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.BlendClips));
            intRangeMinProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.IntRangeMin));
            intRangeMaxProperty = serializedObject.FindProperty(nameof(LinearBlendStateAsset.IntRangeMax));
            
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

        private void InitializeSections()
        {
            if (parametersSection != null) return;
            
            const string prefsPrefix = "DMotion_1DBlend";
            
            parametersSection = new DockablePanelSection("Blend Parameter", prefsPrefix, true);
            
            blendTrackSection = new DockablePanelSection("Blend Track Preview", prefsPrefix, true);
            blendTrackSection.OnUndock += OnBlendTrackUndock;
            blendTrackSection.OnDock += OnBlendTrackDock;
            
            motionsSection = new DockablePanelSection("Motions", prefsPrefix, true);
        }

        private void OnClipThresholdChanged(int index, float newThreshold)
        {
            Repaint();
        }

        private void OnSelectionChanged(int index)
        {
            Repaint();
        }

        private void OnBlendTrackUndock(string title)
        {
            var blendState = target as LinearBlendStateAsset;
            if (blendState != null)
            {
                blendSpaceEditor.SetTarget(blendState);
                undockedWindow = BlendSpaceEditorWindow.Open(blendSpaceEditor, () =>
                {
                    blendTrackSection.SetDocked();
                    undockedWindow = null;
                    Repaint();
                });
            }
        }

        private void OnBlendTrackDock()
        {
            if (undockedWindow != null)
            {
                undockedWindow.Close();
                undockedWindow = null;
            }
        }

        protected override void DrawChildProperties()
        {
            InitializeBlendProperties();
            InitializeVisualEditor();
            InitializeSections();
            
            var linearBlendAsset = target as LinearBlendStateAsset;
            
            // Parameters Section
            if (parametersSection.DrawHeader(showDockButton: false))
            {
                EditorGUI.indentLevel++;
                blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), blendParameterProperty,
                    GUIContentCache.BlendParameter);
                
                if (linearBlendAsset != null && linearBlendAsset.UsesIntParameter)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(intRangeMinProperty, GUIContentCache.Min);
                    EditorGUILayout.PropertyField(intRangeMaxProperty, GUIContentCache.Max);
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.HelpBox(
                        StringBuilderCache.FormatIntRange(linearBlendAsset.IntRangeMin, linearBlendAsset.IntRangeMax), 
                        MessageType.Info);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            
            // Blend Track Preview Section
            if (blendTrackSection.DrawHeader(() => DrawBlendTrackToolbar()))
            {
                DrawVisualEditorContent(linearBlendAsset);
                EditorGUILayout.Space(5);
            }
            else if (!blendTrackSection.IsDocked)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Focus Window", GUILayout.Width(100)))
                {
                    if (undockedWindow != null)
                    {
                        undockedWindow.Focus();
                    }
                    else
                    {
                        OnBlendTrackUndock("Blend Track Preview");
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
            }
            
            // Motions Section
            if (motionsSection.DrawHeader(showDockButton: false))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(clipsProperty, GUIContent.none);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawBlendTrackToolbar()
        {
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(45)))
            {
                blendSpaceEditor?.ResetView();
            }
            
            GUILayout.Label("H:", GUILayout.Width(15));
            visualEditorHeight = GUILayout.HorizontalSlider(visualEditorHeight, MinVisualEditorHeight, MaxVisualEditorHeight, GUILayout.Width(60));
        }

        private void DrawVisualEditorContent(LinearBlendStateAsset blendState)
        {
            if (blendState == null) return;
            
            var visualRect = GUILayoutUtility.GetRect(
                GUIContent.none, 
                GUIStyle.none, 
                GUILayout.Height(visualEditorHeight),
                GUILayout.ExpandWidth(true));
            
            if (visualRect.width < 100) visualRect.width = 100;
            
            blendSpaceEditor.Draw(visualRect, blendState.BlendClips, serializedObject);
            
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