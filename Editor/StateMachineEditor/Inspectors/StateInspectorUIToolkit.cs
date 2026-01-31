using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// UIToolkit-based state inspector for the graph editor's inspector panel.
    /// Unlike StateInspectorBuilder (preview window), this focuses on editing 
    /// with graph node synchronization.
    /// </summary>
    internal class StateInspectorUIToolkit
    {
        #region Constants
        
        private const string UssPath = "Packages/com.gamedevpro.dmotion/Editor/UIElements/StateInspector.uss";
        private const float MinSpeed = 0f;
        private const float MaxSpeed = 3f;
        
        #endregion
        
        #region State
        
        private SerializedObject serializedObject;
        private StateMachineAsset currentStateMachine;
        private AnimationStateAsset currentState;
        private StateNodeView stateNodeView;
        
        // Blend space elements (reused)
        private BlendSpace1DVisualElement blendSpace1D;
        private BlendSpace2DVisualElement blendSpace2D;
        
        // Undocked window reference
        private BlendSpaceEditorWindow undockedWindow;
        private DockableFoldout blendSpaceFoldout;
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Builds the inspector UI for the given state.
        /// </summary>
        /// <param name="stateMachine">The state machine containing the state.</param>
        /// <param name="state">The state to inspect.</param>
        /// <param name="nodeView">The node view in the graph (for name sync). Can be null.</param>
        public VisualElement Build(StateMachineAsset stateMachine, AnimationStateAsset state, StateNodeView nodeView = null)
        {
            if (state == null) return null;
            
            Cleanup();
            currentStateMachine = stateMachine;
            currentState = state;
            stateNodeView = nodeView;
            
            // Create/update serialized object
            if (serializedObject == null || serializedObject.targetObject != state)
            {
                serializedObject = new SerializedObject(state);
            }
            serializedObject.Update();
            
            var container = new VisualElement();
            container.AddToClassList("state-inspector");
            
            // Load stylesheet
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null)
            {
                container.styleSheets.Add(uss);
            }
            
            // Header
            BuildHeader(container, state);
            
            // Name field (with graph node sync)
            BuildNameField(container, state);
            
            // Common properties
            BuildCommonProperties(container, state);
            
            // State-specific content
            BuildStateContent(container, state);
            
            // Transitions (if any)
            BuildTransitions(container, state);
            
            // Bind serialized object
            container.Bind(serializedObject);
            
            return container;
        }
        
        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            if (undockedWindow != null)
            {
                undockedWindow.Close();
                undockedWindow = null;
            }
            
            currentStateMachine = null;
            currentState = null;
            stateNodeView = null;
        }
        
        #endregion
        
        #region Private - UI Building
        
        private void BuildHeader(VisualElement container, AnimationStateAsset state)
        {
            var header = new VisualElement();
            header.AddToClassList("state-inspector__header");
            
            var typeLabel = new Label(GetStateTypeLabel(state));
            typeLabel.AddToClassList("state-inspector__header-type");
            header.Add(typeLabel);
            
            container.Add(header);
        }
        
        private void BuildNameField(VisualElement container, AnimationStateAsset state)
        {
            var nameRow = new VisualElement();
            nameRow.AddToClassList("property-row");
            
            var label = new Label("Name");
            label.AddToClassList("property-label");
            nameRow.Add(label);
            
            var nameField = new TextField();
            nameField.AddToClassList("property-field");
            nameField.value = state.name;
            nameField.SetEnabled(!Application.isPlaying);
            
            nameField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != state.name)
                {
                    Undo.RecordObject(state, "Rename State");
                    state.name = evt.newValue;
                    EditorUtility.SetDirty(state);
                    
                    // Sync with graph node if available
                    if (stateNodeView != null)
                    {
                        stateNodeView.title = evt.newValue;
                    }
                }
            });
            
            nameRow.Add(nameField);
            container.Add(nameRow);
        }
        
        private void BuildCommonProperties(VisualElement container, AnimationStateAsset state)
        {
            var foldout = new Foldout { text = "Properties", value = true };
            foldout.AddToClassList("section-foldout");
            
            // Speed slider
            var speedRow = CreateSliderRow("Speed", "Speed", MinSpeed, MaxSpeed, "x");
            foldout.Add(speedRow);
            
            // Loop toggle
            var loopRow = CreateToggleRow("Loop", "Loop");
            foldout.Add(loopRow);
            
            container.Add(foldout);
        }
        
        private void BuildStateContent(VisualElement container, AnimationStateAsset state)
        {
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    BuildSingleClipContent(container, singleClip);
                    break;
                case LinearBlendStateAsset linearBlend:
                    BuildLinearBlendContent(container, linearBlend);
                    break;
                case Directional2DBlendStateAsset blend2D:
                    Build2DBlendContent(container, blend2D);
                    break;
            }
        }
        
        private void BuildSingleClipContent(VisualElement container, SingleClipStateAsset state)
        {
            var foldout = new Foldout { text = "Clip", value = true };
            foldout.AddToClassList("section-foldout");
            
            var clipField = new PropertyField();
            clipField.bindingPath = "Clip";
            foldout.Add(clipField);
            
            container.Add(foldout);
        }
        
        private void BuildLinearBlendContent(VisualElement container, LinearBlendStateAsset state)
        {
            // Blend Parameter Section
            var paramFoldout = new Foldout { text = "Blend Parameter", value = true };
            paramFoldout.AddToClassList("section-foldout");
            
            // Parameter popup
            var paramRow = new VisualElement();
            paramRow.AddToClassList("property-row");
            
            var paramLabel = new Label("Parameter");
            paramLabel.AddToClassList("property-label");
            paramRow.Add(paramLabel);
            
            var paramPopup = new SubAssetPopupField(
                state,
                null,
                "Add a Float or Int parameter",
                typeof(FloatParameterAsset), typeof(IntParameterAsset));
            paramPopup.AddToClassList("property-field");
            paramPopup.BindProperty(serializedObject.FindProperty("BlendParameter"));
            paramRow.Add(paramPopup);
            
            paramFoldout.Add(paramRow);
            
            // Int range (if using int parameter)
            if (state.UsesIntParameter)
            {
                var rangeRow = new VisualElement();
                rangeRow.AddToClassList("property-row");
                rangeRow.AddToClassList("int-range-row");
                
                var minField = new IntegerField("Min");
                minField.AddToClassList("int-range-row__field");
                minField.bindingPath = "IntRangeMin";
                rangeRow.Add(minField);
                
                var maxField = new IntegerField("Max");
                maxField.AddToClassList("int-range-row__field");
                maxField.bindingPath = "IntRangeMax";
                rangeRow.Add(maxField);
                
                paramFoldout.Add(rangeRow);
                
                // Help box
                var helpBox = new VisualElement();
                helpBox.AddToClassList("help-box");
                helpBox.AddToClassList("help-box--info");
                var helpText = new Label($"Maps int range [{state.IntRangeMin}, {state.IntRangeMax}] to blend positions");
                helpText.AddToClassList("help-box__text");
                helpBox.Add(helpText);
                paramFoldout.Add(helpBox);
            }
            
            container.Add(paramFoldout);
            
            // Blend Track Section (with dockable)
            blendSpaceFoldout = new DockableFoldout("Blend Track", "DMotion_1DBlend_Inspector", true, true);
            blendSpaceFoldout.OnUndock += OnBlendSpaceUndock;
            blendSpaceFoldout.OnDock += OnBlendSpaceDock;
            
            // Add reset button to toolbar
            blendSpaceFoldout.AddToolbarButton("Reset", "Reset view", () => blendSpace1D?.ResetView());
            
            // Create blend space element using builder
            var result = BlendSpaceUIBuilder.CreateForEdit(state);
            blendSpace1D = result.Element as BlendSpace1DVisualElement;
            
            blendSpace1D.OnClipThresholdChanged += (index, threshold) =>
            {
                if (state.BlendClips == null || index < 0 || index >= state.BlendClips.Length) return;
                
                var clipsProperty = serializedObject.FindProperty("BlendClips");
                var clipProperty = clipsProperty.GetArrayElementAtIndex(index);
                var thresholdProperty = clipProperty.FindPropertyRelative("Threshold");
                thresholdProperty.floatValue = threshold;
                serializedObject.ApplyModifiedProperties();
            };
            
            blendSpaceFoldout.Add(blendSpace1D);
            
            // Help text
            var blendHelpLabel = new Label(blendSpace1D.GetHelpText());
            blendHelpLabel.AddToClassList("help-box__text");
            blendHelpLabel.style.marginTop = 4;
            blendSpaceFoldout.Add(blendHelpLabel);
            
            container.Add(blendSpaceFoldout);
            
            // Motions Section
            var motionsFoldout = new Foldout { text = "Motions", value = true };
            motionsFoldout.AddToClassList("section-foldout");
            
            var clipsField = new PropertyField();
            clipsField.bindingPath = "BlendClips";
            clipsField.label = "";
            clipsField.AddToClassList("motions-list");
            motionsFoldout.Add(clipsField);
            
            container.Add(motionsFoldout);
        }
        
        private void Build2DBlendContent(VisualElement container, Directional2DBlendStateAsset state)
        {
            // Blend Parameters Section
            var paramFoldout = new Foldout { text = "Blend Parameters", value = true };
            paramFoldout.AddToClassList("section-foldout");
            
            // Parameter X popup
            var paramXRow = new VisualElement();
            paramXRow.AddToClassList("property-row");
            
            var paramXLabel = new Label("Parameter X");
            paramXLabel.AddToClassList("property-label");
            paramXRow.Add(paramXLabel);
            
            var paramXPopup = new SubAssetPopupField(
                state,
                null,
                "Add a Float parameter",
                typeof(FloatParameterAsset));
            paramXPopup.AddToClassList("property-field");
            paramXPopup.BindProperty(serializedObject.FindProperty("BlendParameterX"));
            paramXRow.Add(paramXPopup);
            paramFoldout.Add(paramXRow);
            
            // Parameter Y popup
            var paramYRow = new VisualElement();
            paramYRow.AddToClassList("property-row");
            
            var paramYLabel = new Label("Parameter Y");
            paramYLabel.AddToClassList("property-label");
            paramYRow.Add(paramYLabel);
            
            var paramYPopup = new SubAssetPopupField(
                state,
                null,
                "Add a Float parameter",
                typeof(FloatParameterAsset));
            paramYPopup.AddToClassList("property-field");
            paramYPopup.BindProperty(serializedObject.FindProperty("BlendParameterY"));
            paramYRow.Add(paramYPopup);
            paramFoldout.Add(paramYRow);
            
            // Algorithm
            var algoRow = new VisualElement();
            algoRow.AddToClassList("property-row");
            
            var algoLabel = new Label("Algorithm");
            algoLabel.AddToClassList("property-label");
            algoRow.Add(algoLabel);
            
            var algoField = new EnumField();
            algoField.AddToClassList("property-field");
            algoField.bindingPath = "Algorithm";
            algoRow.Add(algoField);
            paramFoldout.Add(algoRow);
            
            container.Add(paramFoldout);
            
            // Blend Space Section (with dockable)
            blendSpaceFoldout = new DockableFoldout("Blend Space", "DMotion_2DBlend_Inspector", true, true);
            blendSpaceFoldout.OnUndock += OnBlendSpaceUndock;
            blendSpaceFoldout.OnDock += OnBlendSpaceDock;
            
            // Add reset button to toolbar
            blendSpaceFoldout.AddToolbarButton("Reset", "Reset view", () => blendSpace2D?.ResetView());
            
            // Create blend space element using builder
            var result = BlendSpaceUIBuilder.CreateForEdit(state);
            blendSpace2D = result.Element as BlendSpace2DVisualElement;
            
            blendSpace2D.OnClipPositionChanged += (index, position) =>
            {
                if (state.BlendClips == null || index < 0 || index >= state.BlendClips.Length) return;
                
                var clipsProperty = serializedObject.FindProperty("BlendClips");
                var clipProperty = clipsProperty.GetArrayElementAtIndex(index);
                var positionProperty = clipProperty.FindPropertyRelative("Position");
                positionProperty.FindPropertyRelative("x").floatValue = position.x;
                positionProperty.FindPropertyRelative("y").floatValue = position.y;
                serializedObject.ApplyModifiedProperties();
            };
            
            blendSpaceFoldout.Add(blendSpace2D);
            
            // Help text
            var blendHelpLabel = new Label(blendSpace2D.GetHelpText());
            blendHelpLabel.AddToClassList("help-box__text");
            blendHelpLabel.style.marginTop = 4;
            blendSpaceFoldout.Add(blendHelpLabel);
            
            container.Add(blendSpaceFoldout);
            
            // Motions Section
            var motionsFoldout = new Foldout { text = "Motions", value = true };
            motionsFoldout.AddToClassList("section-foldout");
            
            var clipsField = new PropertyField();
            clipsField.bindingPath = "BlendClips";
            clipsField.label = "";
            clipsField.AddToClassList("motions-list");
            motionsFoldout.Add(clipsField);
            
            container.Add(motionsFoldout);
        }
        
        private void BuildTransitions(VisualElement container, AnimationStateAsset state)
        {
            if (state.OutTransitions == null || state.OutTransitions.Count == 0) return;
            
            var foldout = new Foldout { text = $"Out Transitions ({state.OutTransitions.Count})", value = false };
            foldout.AddToClassList("section-foldout");
            
            foreach (var transition in state.OutTransitions)
            {
                var row = new VisualElement();
                row.AddToClassList("property-row");
                
                var arrow = new Label("\u2192"); // →
                arrow.style.width = 20;
                row.Add(arrow);
                
                var targetName = new Label(transition.ToState?.name ?? "(exit)");
                targetName.style.flexGrow = 1;
                row.Add(targetName);
                
                var duration = new Label($"{transition.TransitionDuration:F2}s");
                duration.style.color = new Color(0.6f, 0.6f, 0.6f);
                row.Add(duration);
                
                foldout.Add(row);
            }
            
            container.Add(foldout);
        }
        
        #endregion
        
        #region Private - Helpers
        
        private VisualElement CreateSliderRow(string label, string propertyName, float min, float max, string suffix = "")
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            row.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.bindingPath = propertyName;
            
            var floatField = new FloatField();
            floatField.AddToClassList("property-float-field");
            floatField.bindingPath = propertyName;
            
            valueContainer.Add(slider);
            valueContainer.Add(floatField);
            
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.AddToClassList("suffix-label");
                valueContainer.Add(suffixLabel);
            }
            
            row.Add(valueContainer);
            return row;
        }
        
        private VisualElement CreateToggleRow(string label, string propertyName)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            row.Add(labelElement);
            
            var toggle = new Toggle();
            toggle.bindingPath = propertyName;
            row.Add(toggle);
            
            return row;
        }
        
        private static string GetStateTypeLabel(AnimationStateAsset state)
        {
            return state switch
            {
                SingleClipStateAsset => "Single Clip",
                LinearBlendStateAsset => "1D Blend",
                Directional2DBlendStateAsset => "2D Blend",
                SubStateMachineStateAsset => "Sub-State Machine",
                _ => "State"
            };
        }
        
        private void OnBlendSpaceUndock(string title)
        {
            if (currentState == null) return;
            
            // Create a temporary IMGUI editor for the undocked window
            // (BlendSpaceEditorWindow currently expects IMGUI-based BlendSpaceVisualEditorBase)
            // TODO: Create UIToolkit version of BlendSpaceEditorWindow
            
            blendSpaceFoldout?.SetUndocked();
        }
        
        private void OnBlendSpaceDock()
        {
            if (undockedWindow != null)
            {
                undockedWindow.Close();
                undockedWindow = null;
            }
        }
        
        #endregion
    }
}
