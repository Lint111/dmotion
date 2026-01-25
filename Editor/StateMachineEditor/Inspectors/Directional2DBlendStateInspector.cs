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
        private SerializedProperty algorithmProperty;
        private SubAssetReferencePopupSelector<AnimationParameterAsset> blendParametersSelector;
        
        // Visual editor
        private BlendSpace2DVisualEditor blendSpaceEditor;
        private float visualEditorHeight = 200f;
        private const float MinVisualEditorHeight = 100f;
        private const float MaxVisualEditorHeight = 400f;
        
        // Dockable sections
        private DockablePanelSection parametersSection;
        private DockablePanelSection blendSpaceSection;
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
                blendSpaceEditor.OnClipPositionChanged -= OnClipPositionChanged;
                blendSpaceEditor.OnSelectionChanged -= OnSelectionChanged;
            }
            
            // Close undocked window if inspector is disabled
            if (undockedWindow != null)
            {
                undockedWindow.Close();
                undockedWindow = null;
            }
        }
        
        private void InitializeBlendProperties()
        {
            if (clipsProperty != null) return;
            
            clipsProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendClips));
            parameterXProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendParameterX));
            parameterYProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.BlendParameterY));
            algorithmProperty = serializedObject.FindProperty(nameof(Directional2DBlendStateAsset.Algorithm));
            
            blendParametersSelector = new SubAssetReferencePopupSelector<AnimationParameterAsset>(
                parameterXProperty.serializedObject.targetObject,
                "Add a Float parameter",
                typeof(FloatParameterAsset));
        }

        private void InitializeVisualEditor()
        {
            if (blendSpaceEditor != null) return;
            
            blendSpaceEditor = new BlendSpace2DVisualEditor();
            
            // Configure for editing (not preview)
            blendSpaceEditor.EditMode = true;
            blendSpaceEditor.ShowModeToggle = false;
            blendSpaceEditor.ShowPreviewIndicator = false;
            
            blendSpaceEditor.OnClipPositionChanged += OnClipPositionChanged;
            blendSpaceEditor.OnSelectionChanged += OnSelectionChanged;
        }

        private void InitializeSections()
        {
            if (parametersSection != null) return;
            
            const string prefsPrefix = "DMotion_2DBlend";
            
            parametersSection = new DockablePanelSection("Blend Parameters", prefsPrefix, true);
            
            blendSpaceSection = new DockablePanelSection("Blend Space Preview", prefsPrefix, true);
            blendSpaceSection.OnUndock += OnBlendSpaceUndock;
            blendSpaceSection.OnDock += OnBlendSpaceDock;
            
            motionsSection = new DockablePanelSection("Motions", prefsPrefix, true);
        }

        private void OnClipPositionChanged(int index, Vector2 newPosition)
        {
            Repaint();
        }

        private void OnSelectionChanged(int index)
        {
            Repaint();
        }

        private void OnBlendSpaceUndock(string title)
        {
            var blendState = target as Directional2DBlendStateAsset;
            if (blendState != null)
            {
                blendSpaceEditor.SetTarget(blendState);
                undockedWindow = BlendSpaceEditorWindow.Open(blendSpaceEditor, () =>
                {
                    blendSpaceSection.SetDocked();
                    undockedWindow = null;
                    Repaint();
                });
            }
        }

        private void OnBlendSpaceDock()
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
            
            var blendState = target as Directional2DBlendStateAsset;
            
            // Parameters Section
            if (parametersSection.DrawHeader(showDockButton: false))
            {
                EditorGUI.indentLevel++;
                blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), parameterXProperty, new GUIContent("Parameter X"));
                blendParametersSelector.OnGUI(EditorGUILayout.GetControlRect(), parameterYProperty, new GUIContent("Parameter Y"));
                EditorGUILayout.PropertyField(algorithmProperty, new GUIContent("Algorithm"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            
            // Blend Space Preview Section
            if (blendSpaceSection.DrawHeader(() => DrawBlendSpaceToolbar()))
            {
                DrawVisualEditorContent(blendState);
                EditorGUILayout.Space(5);
            }
            else if (!blendSpaceSection.IsDocked)
            {
                // Show "Open Window" button when undocked
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
                        OnBlendSpaceUndock("Blend Space Preview");
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

        private void DrawBlendSpaceToolbar()
        {
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(45)))
            {
                blendSpaceEditor?.ResetView();
            }
            
            // Height control
            GUILayout.Label("H:", GUILayout.Width(15));
            visualEditorHeight = GUILayout.HorizontalSlider(visualEditorHeight, MinVisualEditorHeight, MaxVisualEditorHeight, GUILayout.Width(60));
        }

        private void DrawVisualEditorContent(Directional2DBlendStateAsset blendState)
        {
            if (blendState == null) return;
            
            var visualRect = GUILayoutUtility.GetRect(
                GUIContent.none, 
                GUIStyle.none, 
                GUILayout.Height(visualEditorHeight),
                GUILayout.ExpandWidth(true));
            
            if (visualRect.width < 100) visualRect.width = 100;
            
            blendSpaceEditor.Draw(visualRect, blendState.BlendClips, serializedObject);
            
            // Help text below the editor
            var helpStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                wordWrap = true
            };
            EditorGUILayout.LabelField(blendSpaceEditor.GetHelpText(), helpStyle);
            
            EditorGUILayout.Space(5);
            if (blendSpaceEditor.DrawSelectedClipFields(blendState.BlendClips, clipsProperty))
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
