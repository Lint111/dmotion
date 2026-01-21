using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Self-contained builder for animation state inspector UI in the preview window.
    /// Handles common elements (header, properties, timeline) and delegates
    /// state-specific content to specialized builders.
    /// </summary>
    internal class StateInspectorBuilder
    {
        #region Constants
        
        private const float MinSpeed = 0f;
        private const float MaxSpeed = 3f;
        
        // Use shared constant
        private const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        
        #endregion
        
        #region State
        
        private TimelineScrubber timelineScrubber;
        private SerializedObject serializedObject;
        private AnimationStateAsset currentState;
        private IStateContentBuilder currentContentBuilder;
        
        // Content builders (reused)
        private SingleClipContentBuilder singleClipBuilder;
        private LinearBlendContentBuilder linearBlendBuilder;
        private Directional2DBlendContentBuilder blend2DBuilder;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when the timeline time changes.
        /// </summary>
        public event Action<float> OnTimeChanged;
        
        /// <summary>
        /// Fired when the builder needs a repaint.
        /// </summary>
        public event Action OnRepaintRequested;
        
        /// <summary>
        /// Fired when the state speed changes (from Speed slider).
        /// </summary>
        public event Action<float> OnStateSpeedChanged;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The timeline scrubber created by this builder.
        /// </summary>
        public TimelineScrubber TimelineScrubber => timelineScrubber;
        
        /// <summary>
        /// Current 1D blend preview position (if applicable).
        /// </summary>
        public float PreviewBlendPosition1D => linearBlendBuilder?.PreviewBlendValue ?? 0f;
        
        /// <summary>
        /// Current 2D blend preview position (if applicable).
        /// </summary>
        public Vector2 PreviewBlendPosition2D => blend2DBuilder?.PreviewBlendValue ?? Vector2.zero;
        
        /// <summary>
        /// Currently selected clip for individual preview (-1 = blended).
        /// </summary>
        public int SelectedClipForPreview
        {
            get
            {
                if (linearBlendBuilder != null) return linearBlendBuilder.SelectedClipForPreview;
                if (blend2DBuilder != null) return blend2DBuilder.SelectedClipForPreview;
                return -1;
            }
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Builds the inspector UI for the given state.
        /// </summary>
        public VisualElement Build(AnimationStateAsset state)
        {
            if (state == null) return null;
            
            Cleanup();
            currentState = state;
            
            // Create/update serialized object
            if (serializedObject == null || serializedObject.targetObject != state)
            {
                serializedObject = new SerializedObject(state);
            }
            serializedObject.Update();
            
            var container = new VisualElement();
            container.AddToClassList("state-inspector");
            
            // Header
            var header = CreateSectionHeader(GetStateTypeLabel(state), state.name);
            container.Add(header);
            
            // Common properties section
            BuildCommonProperties(container, state);
            
            // State-specific content (delegated to content builders)
            BuildStateContent(container, state);
            
            // Timeline section
            BuildTimeline(container, state);
            
            // Outgoing transitions section
            BuildTransitionsSection(container, state);
            
            // Bind serialized object
            container.Bind(serializedObject);
            
            return container;
        }
        
        /// <summary>
        /// Cleans up event subscriptions and resources.
        /// </summary>
        public void Cleanup()
        {
            // Cleanup current content builder
            currentContentBuilder?.Cleanup();
            currentContentBuilder = null;
            
            // Cleanup timeline
            timelineScrubber = null;
            
            currentState = null;
        }
        
        #endregion
        
        #region Private - UI Factories
        
        private VisualElement CreateSectionHeader(string type, string name)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingBottom = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            header.style.marginBottom = 8;

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");
            typeLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            typeLabel.style.marginRight = 8;

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            header.Add(typeLabel);
            header.Add(nameLabel);

            return header;
        }
        
        private Foldout CreateSection(string title)
        {
            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("section-foldout");
            return foldout;
        }
        
        private VisualElement CreatePropertyRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;

            var valueElement = new Label(value);
            valueElement.AddToClassList("property-value");

            row.Add(labelElement);
            row.Add(valueElement);

            return row;
        }
        
        private VisualElement CreateBoundFloatProperty(
            string label, 
            string propertyName, 
            float min, 
            float max, 
            string suffix = "",
            Action<float> onChanged = null)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return CreatePropertyRow(label, "Property not found");
            }

            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;
            container.Add(labelElement);

            var valueContainer = new VisualElement();
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;

            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.bindingPath = propertyName;
            
            var floatField = new FloatField();
            floatField.AddToClassList("property-float-field");
            floatField.style.width = FloatFieldWidth;
            floatField.style.marginLeft = 4;
            floatField.bindingPath = propertyName;
            
            if (onChanged != null)
            {
                slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                floatField.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                // Invoke with initial value
                onChanged(property.floatValue);
            }

            valueContainer.Add(slider);
            valueContainer.Add(floatField);
            
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.AddToClassList("property-suffix");
                suffixLabel.style.marginLeft = 2;
                suffixLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                valueContainer.Add(suffixLabel);
            }

            container.Add(valueContainer);
            return container;
        }
        
        private VisualElement CreateBoundBoolProperty(
            string label, 
            string propertyName,
            Action<bool> onChanged = null)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return CreatePropertyRow(label, "Property not found");
            }

            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;
            container.Add(labelElement);

            var toggle = new Toggle();
            toggle.AddToClassList("property-toggle");
            toggle.bindingPath = propertyName;
            
            if (onChanged != null)
            {
                toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            }

            container.Add(toggle);
            return container;
        }
        
        #endregion
        
        #region Private - Common UI
        
        private void BuildCommonProperties(VisualElement container, AnimationStateAsset state)
        {
            var propertiesSection = CreateSection("Properties");
            
            // Speed property - with callback to notify when speed changes
            var speedContainer = CreateBoundFloatProperty(
                "Speed", "Speed", MinSpeed, MaxSpeed, "x",
                newSpeed => OnStateSpeedChanged?.Invoke(newSpeed));
            propertiesSection.Add(speedContainer);
            
            // Loop property (syncs with timeline)
            var loopContainer = CreateBoundBoolProperty(
                "Loop", "Loop",
                newValue =>
                {
                    if (timelineScrubber != null)
                    {
                        timelineScrubber.IsLooping = newValue;
                    }
                });
            propertiesSection.Add(loopContainer);
            
            // Speed Parameter (optional)
            var speedParamProp = serializedObject.FindProperty("SpeedParameter");
            if (speedParamProp != null)
            {
                var speedParamContainer = new VisualElement();
                speedParamContainer.AddToClassList("property-row");
                speedParamContainer.style.flexDirection = FlexDirection.Row;
                speedParamContainer.style.marginBottom = 2;
                
                var speedParamLabel = new Label("Speed Param");
                speedParamLabel.AddToClassList("property-label");
                speedParamLabel.style.width = 100;
                speedParamLabel.style.minWidth = 100;
                speedParamContainer.Add(speedParamLabel);
                
                var speedParamField = new PropertyField(speedParamProp, "");
                speedParamField.AddToClassList("property-field");
                speedParamField.BindProperty(speedParamProp);
                speedParamContainer.Add(speedParamField);
                
                propertiesSection.Add(speedParamContainer);
            }
            
            container.Add(propertiesSection);
        }
        
        private void BuildStateContent(VisualElement container, AnimationStateAsset state)
        {
            // Get or create the appropriate content builder
            currentContentBuilder = state switch
            {
                SingleClipStateAsset => singleClipBuilder ??= new SingleClipContentBuilder(),
                LinearBlendStateAsset => linearBlendBuilder ??= new LinearBlendContentBuilder(),
                Directional2DBlendStateAsset => blend2DBuilder ??= new Directional2DBlendContentBuilder(),
                _ => null
            };
            
            if (currentContentBuilder == null) return;
            
            // Create context
            var context = new StateContentContext(
                state,
                serializedObject,
                CreateSectionHeader,
                CreateSection,
                CreatePropertyRow,
                () => OnRepaintRequested?.Invoke());
            
            // Build content
            currentContentBuilder.Build(container, context);
        }
        
        private void BuildTimeline(VisualElement container, AnimationStateAsset state)
        {
            var timelineSection = CreateSection("Timeline");
            
            timelineScrubber = new TimelineScrubber();
            timelineScrubber.IsLooping = state.Loop;
            
            // Configure timeline via content builder
            if (currentContentBuilder != null)
            {
                var context = new StateContentContext(
                    state,
                    serializedObject,
                    CreateSectionHeader,
                    CreateSection,
                    CreatePropertyRow,
                    () => OnRepaintRequested?.Invoke());
                
                currentContentBuilder.ConfigureTimeline(timelineScrubber, context);
            }
            
            timelineScrubber.OnTimeChanged += time => OnTimeChanged?.Invoke(time);
            
            timelineSection.Add(timelineScrubber);
            container.Add(timelineSection);
        }
        
        private void BuildTransitionsSection(VisualElement container, AnimationStateAsset state)
        {
            if (state.OutTransitions == null || state.OutTransitions.Count == 0)
                return;
            
            var transitionsSection = CreateSection("Transitions");
            
            foreach (var transition in state.OutTransitions)
            {
                var toState = transition.ToState;
                var transitionRow = CreateClickableTransitionRow(state, toState, transition);
                transitionsSection.Add(transitionRow);
            }
            
            container.Add(transitionsSection);
        }
        
        private VisualElement CreateClickableTransitionRow(AnimationStateAsset fromState, AnimationStateAsset toState, StateOutTransition transition)
        {
            var row = new VisualElement();
            row.AddToClassList("transition-link-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 4;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            
            // Arrow icon
            var arrowLabel = new Label("\u2192"); // Unicode right arrow
            arrowLabel.style.marginRight = 6;
            arrowLabel.style.color = PreviewEditorColors.DimText;
            row.Add(arrowLabel);
            
            // Target state name (clickable)
            string toName = toState?.name ?? "(exit)";
            var targetLabel = new Label(toName);
            targetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            
            if (toState != null)
            {
                targetLabel.style.color = PreviewEditorColors.ToState;
                targetLabel.tooltip = $"Click to preview transition to {toState.name}";
                
                // Hover effect
                targetLabel.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    targetLabel.style.color = PreviewEditorColors.ToStateHighlight;
                    row.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                });
                targetLabel.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    targetLabel.style.color = PreviewEditorColors.ToState;
                    row.style.backgroundColor = Color.clear;
                });
                
                // Click to navigate to transition
                targetLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    AnimationPreviewEvents.RaiseNavigateToTransition(fromState, toState, false);
                    evt.StopPropagation();
                });
            }
            else
            {
                targetLabel.style.color = PreviewEditorColors.DimText;
            }
            row.Add(targetLabel);
            
            // Duration info
            var durationLabel = new Label($" ({transition.TransitionDuration:F2}s)");
            durationLabel.style.color = PreviewEditorColors.DimText;
            row.Add(durationLabel);
            
            // Conditions count (if any)
            if (transition.Conditions != null && transition.Conditions.Count > 0)
            {
                var conditionsLabel = new Label($" [{transition.Conditions.Count} condition{(transition.Conditions.Count > 1 ? "s" : "")}]");
                conditionsLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                conditionsLabel.style.fontSize = 10;
                row.Add(conditionsLabel);
            }
            
            return row;
        }
        
        #endregion
        
        #region Private - Helpers
        
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
        
        #endregion
    }
}
