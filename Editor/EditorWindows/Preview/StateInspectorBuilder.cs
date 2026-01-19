using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Coordinates building inspector UI for animation states.
    /// Handles common elements (header, properties, timeline) and delegates
    /// state-specific content to specialized builders.
    /// </summary>
    internal class StateInspectorBuilder
    {
        #region Dependencies
        
        private readonly Func<string, string, VisualElement> createSectionHeader;
        private readonly Func<string, VisualElement> createSection;
        private readonly Func<string, string, VisualElement> createPropertyRow;
        private readonly Func<SerializedObject, string, string, float, float, string, VisualElement> createEditableFloatProperty;
        private readonly Func<SerializedObject, string, string, Action<bool>, VisualElement> createEditableBoolPropertyWithCallback;
        
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
        public UnityEngine.Vector2 PreviewBlendPosition2D => blend2DBuilder?.PreviewBlendValue ?? UnityEngine.Vector2.zero;
        
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
        
        #region Constructor
        
        public StateInspectorBuilder(
            Func<string, string, VisualElement> createSectionHeader,
            Func<string, VisualElement> createSection,
            Func<string, string, VisualElement> createPropertyRow,
            Func<SerializedObject, string, string, float, float, string, VisualElement> createEditableFloatProperty,
            Func<SerializedObject, string, string, Action<bool>, VisualElement> createEditableBoolPropertyWithCallback)
        {
            this.createSectionHeader = createSectionHeader;
            this.createSection = createSection;
            this.createPropertyRow = createPropertyRow;
            this.createEditableFloatProperty = createEditableFloatProperty;
            this.createEditableBoolPropertyWithCallback = createEditableBoolPropertyWithCallback;
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
            
            // Header
            var header = createSectionHeader(GetStateTypeLabel(state), state.name);
            container.Add(header);
            
            // Common properties section
            BuildCommonProperties(container, state);
            
            // State-specific content (delegated to content builders)
            BuildStateContent(container, state);
            
            // Timeline section
            BuildTimeline(container, state);
            
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
        
        #region Private - Common UI
        
        private void BuildCommonProperties(VisualElement container, AnimationStateAsset state)
        {
            var propertiesSection = createSection("Properties");
            
            // Speed property
            var speedContainer = createEditableFloatProperty(
                serializedObject, "Speed", "Speed", 0.0f, 3.0f, "x");
            propertiesSection.Add(speedContainer);
            
            // Loop property (syncs with timeline)
            var loopContainer = createEditableBoolPropertyWithCallback(
                serializedObject, "Loop", "Loop",
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
                
                var speedParamLabel = new Label("Speed Param");
                speedParamLabel.AddToClassList("property-label");
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
                createSectionHeader,
                createSection,
                createPropertyRow,
                () => OnRepaintRequested?.Invoke());
            
            // Build content
            currentContentBuilder.Build(container, context);
        }
        
        private void BuildTimeline(VisualElement container, AnimationStateAsset state)
        {
            var timelineSection = createSection("Timeline");
            
            timelineScrubber = new TimelineScrubber();
            timelineScrubber.IsLooping = state.Loop;
            
            // Configure timeline via content builder
            if (currentContentBuilder != null)
            {
                var context = new StateContentContext(
                    state,
                    serializedObject,
                    createSectionHeader,
                    createSection,
                    createPropertyRow,
                    () => OnRepaintRequested?.Invoke());
                
                currentContentBuilder.ConfigureTimeline(timelineScrubber, context);
            }
            
            timelineScrubber.OnTimeChanged += time => OnTimeChanged?.Invoke(time);
            
            timelineSection.Add(timelineScrubber);
            container.Add(timelineSection);
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
