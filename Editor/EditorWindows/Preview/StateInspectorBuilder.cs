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
        private StateMachineAsset currentStateMachine;
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
        public VisualElement Build(StateMachineAsset stateMachine, AnimationStateAsset state)
        {
            if (state == null) return null;
            
            Cleanup();
            currentStateMachine = stateMachine;
            currentState = state;
            
            // Create/update serialized object
            if (serializedObject == null || serializedObject.targetObject != state)
            {
                serializedObject = new SerializedObject(state);
            }
            serializedObject.Update();
            
            var container = new VisualElement();
            container.AddToClassList("state-inspector");
            
            // Header with type, name, and duration
            var duration = GetStateDuration(state);
            var durationText = duration > 0 ? $" ({duration:F2}s)" : "";
            var header = CreateSectionHeader(GetStateTypeLabel(state), $"{state.name}{durationText}");
            container.Add(header);
            
            // Common properties section
            BuildCommonProperties(container, state);
            
            // State-specific content (delegated to content builders)
            BuildStateContent(container, state);
            
            // Timeline section
            BuildTimeline(container, state);
            
            // Navigation section at bottom (collapsible)
            BuildNavigationSection(container, stateMachine, state);
            
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
            
            currentStateMachine = null;
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
            
            // Speed Parameter (only show if assigned)
            var speedParamProp = serializedObject.FindProperty("SpeedParameter");
            if (speedParamProp != null && speedParamProp.objectReferenceValue != null)
            {
                var speedParamContainer = new VisualElement();
                speedParamContainer.AddToClassList("property-row");
                speedParamContainer.style.flexDirection = FlexDirection.Row;
                speedParamContainer.style.marginBottom = 2;
                
                var speedParamLabel = new Label("Speed Param");
                speedParamLabel.AddToClassList("property-label");
                speedParamLabel.style.width = 100;
                speedParamLabel.style.minWidth = 100;
                speedParamLabel.tooltip = "Parameter that controls playback speed at runtime";
                speedParamContainer.Add(speedParamLabel);
                
                var speedParamValue = new Label(speedParamProp.objectReferenceValue.name);
                speedParamValue.style.color = PreviewEditorColors.DimText;
                speedParamContainer.Add(speedParamValue);
                
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
        
        /// <summary>
        /// Builds a collapsible navigation section with sub-foldouts for Out/In transitions.
        /// </summary>
        private void BuildNavigationSection(VisualElement container, StateMachineAsset stateMachine, AnimationStateAsset state)
        {
            // Collect transitions
            var outTransitions = state.OutTransitions ?? new System.Collections.Generic.List<StateOutTransition>();
            var incomingStates = stateMachine != null ? FindIncomingTransitions(stateMachine, state) : new System.Collections.Generic.List<AnimationStateAsset>();
            var anyStateTransitions = stateMachine != null ? FindAnyStateTransitionsTo(stateMachine, state) : new System.Collections.Generic.List<StateOutTransition>();
            
            // Don't show section if no transitions
            int totalCount = outTransitions.Count + incomingStates.Count + anyStateTransitions.Count;
            if (totalCount == 0) return;
            
            // Main navigation foldout
            var navigationFoldout = new Foldout { text = $"Navigation ({totalCount})", value = false };
            navigationFoldout.AddToClassList("navigation-foldout");
            
            // Out Transitions sub-foldout
            if (outTransitions.Count > 0)
            {
                var outFoldout = new Foldout { text = $"Out Transitions ({outTransitions.Count})", value = true };
                outFoldout.AddToClassList("navigation-sub-foldout");
                outFoldout.style.marginLeft = 8;
                
                foreach (var transition in outTransitions)
                {
                    var row = CreateTransitionRow(
                        "\u2192", // →
                        transition.ToState?.name ?? "(exit)",
                        $"{transition.TransitionDuration:F2}s",
                        transition.ToState != null ? $"Click to preview transition to {transition.ToState.name}" : null,
                        PreviewEditorColors.ToState,
                        PreviewEditorColors.ToStateHighlight,
                        transition.ToState != null ? () => AnimationPreviewEvents.RaiseNavigateToTransition(state, transition.ToState, false) : null);
                    outFoldout.Add(row);
                }
                
                navigationFoldout.Add(outFoldout);
            }
            
            // In Transitions sub-foldout (incoming from other states + any state)
            int inCount = incomingStates.Count + anyStateTransitions.Count;
            if (inCount > 0)
            {
                var inFoldout = new Foldout { text = $"In Transitions ({inCount})", value = true };
                inFoldout.AddToClassList("navigation-sub-foldout");
                inFoldout.style.marginLeft = 8;
                
                // Regular incoming transitions
                foreach (var fromState in incomingStates)
                {
                    var row = CreateTransitionRow(
                        "\u2190", // ←
                        fromState.name,
                        null,
                        $"Click to preview transition from {fromState.name}",
                        PreviewEditorColors.FromState,
                        PreviewEditorColors.FromStateHighlight,
                        () => AnimationPreviewEvents.RaiseNavigateToTransition(fromState, state, false));
                    inFoldout.Add(row);
                }
                
                // Any State transitions
                foreach (var transition in anyStateTransitions)
                {
                    var row = CreateTransitionRow(
                        "\u2190", // ←
                        "Any State",
                        $"{transition.TransitionDuration:F2}s",
                        "Click to preview Any State transition",
                        new Color(0.7f, 0.5f, 0.8f), // Purple
                        new Color(0.9f, 0.7f, 1f),
                        () => AnimationPreviewEvents.RaiseNavigateToTransition(null, state, true));
                    inFoldout.Add(row);
                }
                
                navigationFoldout.Add(inFoldout);
            }
            
            container.Add(navigationFoldout);
        }
        
        /// <summary>
        /// Creates a single clickable transition row.
        /// </summary>
        private VisualElement CreateTransitionRow(
            string arrow, string stateName, string duration, string tooltip,
            Color normalColor, Color hoverColor, Action onClick)
        {
            var row = new VisualElement();
            row.AddToClassList("transition-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.paddingLeft = 4;
            
            // Arrow
            var arrowLabel = new Label(arrow);
            arrowLabel.style.color = PreviewEditorColors.DimText;
            arrowLabel.style.marginRight = 6;
            arrowLabel.style.width = 14;
            row.Add(arrowLabel);
            
            // State name (clickable)
            var nameLabel = new Label(stateName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            
            if (onClick != null)
            {
                nameLabel.style.color = normalColor;
                nameLabel.tooltip = tooltip;
                
                // Hover effects on entire row
                row.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    nameLabel.style.color = hoverColor;
                    row.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                });
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    nameLabel.style.color = normalColor;
                    row.style.backgroundColor = Color.clear;
                });
                row.RegisterCallback<MouseDownEvent>(evt =>
                {
                    onClick();
                    evt.StopPropagation();
                });
            }
            else
            {
                nameLabel.style.color = PreviewEditorColors.DimText;
            }
            row.Add(nameLabel);
            
            // Duration (if provided)
            if (!string.IsNullOrEmpty(duration))
            {
                var durationLabel = new Label($" ({duration})");
                durationLabel.style.color = PreviewEditorColors.DimText;
                durationLabel.style.fontSize = 10;
                row.Add(durationLabel);
            }
            
            return row;
        }
        
        private static System.Collections.Generic.List<AnimationStateAsset> FindIncomingTransitions(StateMachineAsset stateMachine, AnimationStateAsset targetState)
        {
            var result = new System.Collections.Generic.List<AnimationStateAsset>();
            
            if (stateMachine?.States == null) return result;
            
            foreach (var state in stateMachine.States)
            {
                if (state == targetState || state.OutTransitions == null) continue;
                
                foreach (var transition in state.OutTransitions)
                {
                    if (transition.ToState == targetState)
                    {
                        result.Add(state);
                        break; // Only add each state once
                    }
                }
            }
            
            return result;
        }
        
        private static System.Collections.Generic.List<StateOutTransition> FindAnyStateTransitionsTo(StateMachineAsset stateMachine, AnimationStateAsset targetState)
        {
            var result = new System.Collections.Generic.List<StateOutTransition>();
            
            if (stateMachine?.AnyStateTransitions == null) return result;
            
            foreach (var transition in stateMachine.AnyStateTransitions)
            {
                if (transition.ToState == targetState)
                {
                    result.Add(transition);
                }
            }
            
            return result;
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
        
        /// <summary>
        /// Gets the duration of the state's animation (longest clip for blend states).
        /// </summary>
        private static float GetStateDuration(AnimationStateAsset state)
        {
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    return singleClip.Clip?.Clip != null ? singleClip.Clip.Clip.length : 0f;
                    
                case LinearBlendStateAsset linearBlend:
                    float maxDuration1D = 0f;
                    if (linearBlend.BlendClips != null)
                    {
                        foreach (var clip in linearBlend.BlendClips)
                        {
                            if (clip.Clip?.Clip != null)
                                maxDuration1D = Mathf.Max(maxDuration1D, clip.Clip.Clip.length);
                        }
                    }
                    return maxDuration1D;
                    
                case Directional2DBlendStateAsset blend2D:
                    float maxDuration2D = 0f;
                    if (blend2D.BlendClips != null)
                    {
                        foreach (var clip in blend2D.BlendClips)
                        {
                            if (clip.Clip?.Clip != null)
                                maxDuration2D = Mathf.Max(maxDuration2D, clip.Clip.Clip.length);
                        }
                    }
                    return maxDuration2D;
                    
                default:
                    return 0f;
            }
        }
        
        #endregion
    }
}
