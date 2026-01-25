using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for blend state content builders.
    /// Provides shared functionality for 1D and 2D blend space UI.
    /// Uses UIToolkit-based BlendSpaceVisualElement for consistent event handling.
    /// </summary>
    internal abstract class BlendContentBuilderBase<TState, TVisualElement> : IStateContentBuilder
        where TState : AnimationStateAsset
        where TVisualElement : BlendSpaceVisualElement
    {
        #region Constants
        
        private const string UssPath = "Packages/com.gamedevpro.dmotion/Editor/EditorWindows/Preview/StateContent/BlendContent.uss";
        
        // Use shared constants
        protected const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        protected const float SpacingSmall = PreviewEditorConstants.SpacingSmall;
        protected const float SpacingMedium = PreviewEditorConstants.SpacingMedium;
        protected const float SpacingLarge = PreviewEditorConstants.SpacingLarge;
        
        #endregion
        
        #region State
        
        protected TState state;
        protected TVisualElement blendSpaceElement;
        protected SerializedObject serializedObject;
        protected int selectedClipForPreview = -1;
        
        // Timeline scrubber reference for duration updates
        protected TimelineScrubber timelineScrubber;
        
        // Cached event handlers for cleanup
        protected Action<Vector2> cachedPreviewPositionHandler;
        protected Action<int> cachedClipSelectedHandler;
        protected Action<bool> cachedEditModeHandler;
        protected Action<AnimationStateAsset, Vector2> cachedBlendStateChangedHandler;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Currently selected clip for individual preview (-1 = blended).
        /// </summary>
        public int SelectedClipForPreview => selectedClipForPreview;
        
        #endregion
        
        #region Abstract Members
        
        /// <summary>
        /// Gets the CSS class for the blend space preview element.
        /// </summary>
        protected abstract string BlendSpacePreviewClass { get; }
        
        /// <summary>
        /// Gets the section title for this blend space type.
        /// </summary>
        protected abstract string SectionTitle { get; }
        
        /// <summary>
        /// Gets the clips property name in the serialized object.
        /// </summary>
        protected abstract string ClipsPropertyName { get; }
        
        /// <summary>
        /// Creates or gets the blend space visual element instance.
        /// </summary>
        protected abstract TVisualElement GetOrCreateVisualElement();
        
        /// <summary>
        /// Builds the parameter info rows for the section.
        /// </summary>
        protected abstract void BuildParameterInfo(VisualElement section, StateContentContext context);
        
        /// <summary>
        /// Builds the preview slider(s) for controlling blend position.
        /// </summary>
        protected abstract VisualElement BuildPreviewSliders(StateContentContext context);
        
        /// <summary>
        /// Sets up preview position change handlers.
        /// </summary>
        protected abstract void SetupPreviewPositionHandler(StateContentContext context);
        
        /// <summary>
        /// Gets the current blend position for this state type.
        /// </summary>
        protected abstract Vector2 GetCurrentBlendPosition();
        
        /// <summary>
        /// Gets the longest clip duration and frame rate from the state's clips.
        /// </summary>
        protected abstract void GetLongestClipInfo(out float duration, out float frameRate);
        
        /// <summary>
        /// Clears cached UI element references.
        /// </summary>
        protected abstract void ClearCachedUIReferences();
        
        #endregion
        
        #region IStateContentBuilder
        
        public void Build(VisualElement container, StateContentContext context)
        {
            state = context.State as TState;
            if (state == null) return;
            
            serializedObject = context.SerializedObject;
            
            // Load stylesheet if not already loaded
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null && !container.styleSheets.Contains(uss))
            {
                container.styleSheets.Add(uss);
            }
            
            var blendSection = context.CreateSection(SectionTitle);
            
            // Parameter info (abstract - implemented by subclasses)
            BuildParameterInfo(blendSection, context);
            
            // Initialize blend space visual element (UIToolkit-based)
            blendSpaceElement = GetOrCreateVisualElement();
            blendSpaceElement.ShowPreviewIndicator = true;
            blendSpaceElement.EditMode = false;
            
            // Preview sliders (abstract - implemented by subclasses)
            var slidersContainer = BuildPreviewSliders(context);
            blendSection.Add(slidersContainer);
            
            // Setup preview position handler (abstract - implemented by subclasses)
            SetupPreviewPositionHandler(context);
            
            // Clip selection handler
            cachedClipSelectedHandler = clipIndex =>
            {
                selectedClipForPreview = clipIndex;
                AnimationPreviewEvents.RaiseClipSelectedForPreview(state, clipIndex);
                context.RequestRepaint?.Invoke();
            };
            blendSpaceElement.OnClipSelectedForPreview += cachedClipSelectedHandler;
            
            // Add blend space visual element directly (no IMGUIContainer wrapper needed)
            blendSpaceElement.AddToClassList(BlendSpacePreviewClass);
            blendSection.Add(blendSpaceElement);
            
            // Help text (pure UIToolkit Label)
            var helpLabel = CreateHelpTextLabel();
            blendSection.Add(helpLabel);
            
            // Clip editing (pure UIToolkit)
            var clipEditContainer = CreateClipEditContainer(context);
            blendSection.Add(clipEditContainer);
            
            // Edit mode visibility handler
            SetupEditModeHandler(slidersContainer, clipEditContainer, helpLabel, context);
            
            container.Add(blendSection);
        }
        
        public void ConfigureTimeline(TimelineScrubber scrubber, StateContentContext context)
        {
            state = context.State as TState;
            if (state == null) return;
            
            // Store reference for duration updates when blend position changes
            timelineScrubber = scrubber;
            
            // Use effective duration at current blend position
            var blendPos = GetCurrentBlendPosition();
            float effectiveDuration = state.GetEffectiveDuration(blendPos);
            
            // Fallback to longest clip if effective duration is invalid
            if (effectiveDuration <= 0)
            {
                GetLongestClipInfo(out effectiveDuration, out _);
            }
            
            GetLongestClipInfo(out _, out float frameRate);
            
            scrubber.Duration = effectiveDuration > 0 ? effectiveDuration : 1f;
            scrubber.FrameRate = frameRate;
            scrubber.SetEventMarkers(null); // Blend states don't show events from individual clips
            
            // Subscribe to blend state changes for automatic duration updates
            cachedBlendStateChangedHandler = OnBlendStateChanged;
            AnimationPreviewEvents.OnBlendStateChanged += cachedBlendStateChangedHandler;
        }
        
        private void OnBlendStateChanged(AnimationStateAsset changedState, Vector2 blendPos)
        {
            // Only update if the change is for our state
            if (changedState != state || timelineScrubber == null) return;
            
            float effectiveDuration = state.GetEffectiveDuration(blendPos);
            if (effectiveDuration > 0 && Mathf.Abs(effectiveDuration - timelineScrubber.Duration) > 0.001f)
            {
                timelineScrubber.Duration = effectiveDuration;
            }
        }
        
        public void Cleanup()
        {
            // Unsubscribe from global events
            if (cachedBlendStateChangedHandler != null)
            {
                AnimationPreviewEvents.OnBlendStateChanged -= cachedBlendStateChangedHandler;
                cachedBlendStateChangedHandler = null;
            }
            
            if (blendSpaceElement != null)
            {
                if (cachedPreviewPositionHandler != null)
                {
                    blendSpaceElement.OnPreviewPositionChanged -= cachedPreviewPositionHandler;
                    cachedPreviewPositionHandler = null;
                }
                if (cachedClipSelectedHandler != null)
                {
                    blendSpaceElement.OnClipSelectedForPreview -= cachedClipSelectedHandler;
                    cachedClipSelectedHandler = null;
                }
                if (cachedEditModeHandler != null)
                {
                    blendSpaceElement.OnEditModeChanged -= cachedEditModeHandler;
                    cachedEditModeHandler = null;
                }
            }
            
            ClearCachedUIReferences();
            timelineScrubber = null;
            state = default;
            serializedObject = null;
        }
        
        #endregion
        
        #region Protected - Shared UI Creation
        
        /// <summary>
        /// Creates the help text label (pure UIToolkit).
        /// </summary>
        protected Label CreateHelpTextLabel()
        {
            var label = new Label();
            label.AddToClassList("blend-space-help");
            
            // Update text when element is attached
            label.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (blendSpaceElement != null)
                {
                    label.text = blendSpaceElement.GetHelpText();
                }
            });
            
            return label;
        }
        
        /// <summary>
        /// Creates the clip edit container (pure UIToolkit).
        /// Override BuildClipEditContent in subclass to provide clip-specific UI.
        /// </summary>
        protected VisualElement CreateClipEditContainer(StateContentContext context)
        {
            var container = new VisualElement();
            container.AddToClassList("clip-edit-container");
            // Visibility controlled by clip-edit-container--visible class via edit mode handler
            
            // Subclasses populate this via BuildClipEditContent
            BuildClipEditContent(container, context);
            
            return container;
        }
        
        /// <summary>
        /// Builds the clip edit UI content. Override in subclass to provide clip-specific fields.
        /// </summary>
        protected abstract void BuildClipEditContent(VisualElement container, StateContentContext context);
        
        protected void SetupEditModeHandler(
            VisualElement slidersContainer,
            VisualElement clipEditContainer,
            Label helpLabel,
            StateContentContext context)
        {
            cachedEditModeHandler = isEditMode =>
            {
                slidersContainer.EnableInClassList("property-row--hidden", isEditMode);
                clipEditContainer.EnableInClassList("clip-edit-container--visible", isEditMode);
                
                // Update help text for current mode
                if (blendSpaceElement != null && helpLabel != null)
                {
                    helpLabel.text = blendSpaceElement.GetHelpText();
                }
                
                AnimationPreviewEvents.RaiseBlendSpaceEditModeChanged(state, isEditMode);
                context.RequestRepaint?.Invoke();
            };
            blendSpaceElement.OnEditModeChanged += cachedEditModeHandler;
        }
        
        /// <summary>
        /// Creates a slider with float field for preview value control.
        /// </summary>
        protected (VisualElement container, Slider slider, FloatField field) CreateSliderWithField(
            string label, float min, float max, float value)
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.AddToClassList("editable-property");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("blend-content__value-container");
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.AddToClassList("blend-content__slider");
            slider.value = value;
            
            var field = new FloatField();
            field.AddToClassList("property-float-field");
            field.AddToClassList("blend-content__field");
            field.value = value;
            
            valueContainer.Add(slider);
            valueContainer.Add(field);
            container.Add(valueContainer);
            
            return (container, slider, field);
        }
        
        #endregion
    }
}
