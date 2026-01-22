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
    /// </summary>
    internal abstract class BlendContentBuilderBase<TState, TEditor> : IStateContentBuilder
        where TState : AnimationStateAsset
        where TEditor : BlendSpaceVisualEditorBase
    {
        #region Constants
        
        /// <summary>Height of the 1D blend space visualizer.</summary>
        protected const float BlendSpace1DDefaultHeight = 120f;
        
        /// <summary>Height of the 2D blend space visualizer.</summary>
        protected const float BlendSpace2DDefaultHeight = 180f;
        
        // Use shared constants
        protected const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        protected const float SpacingSmall = PreviewEditorConstants.SpacingSmall;
        protected const float SpacingMedium = PreviewEditorConstants.SpacingMedium;
        protected const float SpacingLarge = PreviewEditorConstants.SpacingLarge;
        
        /// <summary>Text color for help/hint text.</summary>
        protected static Color HelpTextColor => PreviewEditorColors.DimText;
        
        #endregion
        
        #region State
        
        protected TState state;
        protected TEditor blendSpaceEditor;
        protected SerializedObject serializedObject;
        protected int selectedClipForPreview = -1;
        
        // Timeline scrubber reference for duration updates
        protected TimelineScrubber timelineScrubber;
        
        // Cached event handlers for cleanup
        protected Action<Vector2> cachedPreviewPositionHandler;
        protected Action<int> cachedClipSelectedHandler;
        protected Action<bool> cachedEditModeHandler;
        protected Action cachedRepaintHandler;
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
        /// Gets the height for the blend space visualizer.
        /// </summary>
        protected abstract float BlendSpaceHeight { get; }
        
        /// <summary>
        /// Gets the section title for this blend space type.
        /// </summary>
        protected abstract string SectionTitle { get; }
        
        /// <summary>
        /// Gets the clips property name in the serialized object.
        /// </summary>
        protected abstract string ClipsPropertyName { get; }
        
        /// <summary>
        /// Creates or gets the blend space editor instance.
        /// </summary>
        protected abstract TEditor GetOrCreateEditor();
        
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
            
            var blendSection = context.CreateSection(SectionTitle);
            
            // Parameter info (abstract - implemented by subclasses)
            BuildParameterInfo(blendSection, context);
            
            // Initialize blend space editor
            blendSpaceEditor = GetOrCreateEditor();
            blendSpaceEditor.ShowPreviewIndicator = true;
            blendSpaceEditor.EditMode = false;
            
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
            blendSpaceEditor.OnClipSelectedForPreview += cachedClipSelectedHandler;
            
            // Repaint handler
            cachedRepaintHandler = () => context.RequestRepaint?.Invoke();
            blendSpaceEditor.OnRepaintRequested += cachedRepaintHandler;
            
            // Blend space visualizer
            var blendSpaceContainer = CreateBlendSpaceContainer(context);
            blendSection.Add(blendSpaceContainer);
            
            // Help text
            var helpContainer = CreateHelpTextContainer();
            blendSection.Add(helpContainer);
            
            // Clip editing
            var clipEditContainer = CreateClipEditContainer(context);
            blendSection.Add(clipEditContainer);
            
            // Edit mode visibility handler
            SetupEditModeHandler(slidersContainer, clipEditContainer, context);
            
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
            
            if (blendSpaceEditor != null)
            {
                if (cachedPreviewPositionHandler != null)
                {
                    blendSpaceEditor.OnPreviewPositionChanged -= cachedPreviewPositionHandler;
                    cachedPreviewPositionHandler = null;
                }
                if (cachedClipSelectedHandler != null)
                {
                    blendSpaceEditor.OnClipSelectedForPreview -= cachedClipSelectedHandler;
                    cachedClipSelectedHandler = null;
                }
                if (cachedEditModeHandler != null)
                {
                    blendSpaceEditor.OnEditModeChanged -= cachedEditModeHandler;
                    cachedEditModeHandler = null;
                }
                if (cachedRepaintHandler != null)
                {
                    blendSpaceEditor.OnRepaintRequested -= cachedRepaintHandler;
                    cachedRepaintHandler = null;
                }
            }
            
            ClearCachedUIReferences();
            timelineScrubber = null;
            state = default;
            serializedObject = null;
        }
        
        #endregion
        
        #region Protected - Shared UI Creation
        
        protected IMGUIContainer CreateBlendSpaceContainer(StateContentContext context)
        {
            IMGUIContainer container = null;
            var height = BlendSpaceHeight;
            
            container = new IMGUIContainer(() =>
            {
                if (state != null && serializedObject != null)
                {
                    var rect = new Rect(0, 0, container.contentRect.width, height);
                    if (rect.width > 10)
                    {
                        blendSpaceEditor.Draw(rect, serializedObject);
                    }
                }
            });
            container.style.height = height;
            container.style.marginTop = SpacingLarge;
            container.focusable = true;
            container.pickingMode = PickingMode.Position;
            
            // Mouse event handlers for pan
            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                container.Focus();
                if (evt.button == 2 || (evt.button == 0 && evt.altKey))
                {
                    blendSpaceEditor?.StartExternalPan(evt.localMousePosition);
                    evt.StopPropagation();
                }
                container.MarkDirtyRepaint();
            });
            container.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (blendSpaceEditor?.IsExternalPanning == true)
                {
                    blendSpaceEditor.UpdateExternalPan(evt.localMousePosition);
                    container.MarkDirtyRepaint();
                    evt.StopPropagation();
                }
                else if (evt.pressedButtons != 0)
                {
                    container.MarkDirtyRepaint();
                }
            });
            container.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (blendSpaceEditor?.IsExternalPanning == true)
                {
                    blendSpaceEditor.EndExternalPan();
                    container.MarkDirtyRepaint();
                }
            });
            container.RegisterCallback<WheelEvent>(evt => container.MarkDirtyRepaint());
            
            return container;
        }
        
        protected IMGUIContainer CreateHelpTextContainer()
        {
            var container = new IMGUIContainer(() =>
            {
                if (blendSpaceEditor != null)
                {
                    var helpStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = HelpTextColor },
                        wordWrap = true
                    };
                    GUILayout.Label(blendSpaceEditor.GetHelpText(), helpStyle);
                }
            });
            container.style.marginTop = SpacingSmall;
            return container;
        }
        
        protected IMGUIContainer CreateClipEditContainer(StateContentContext context)
        {
            var clipsProperty = serializedObject.FindProperty(ClipsPropertyName);
            var container = new IMGUIContainer(() =>
            {
                if (blendSpaceEditor != null && blendSpaceEditor.EditMode)
                {
                    DrawSelectedClipFields(clipsProperty);
                }
            });
            container.style.marginTop = SpacingMedium;
            container.style.display = DisplayStyle.None;
            return container;
        }
        
        /// <summary>
        /// Draws the selected clip fields. Override in subclass to call the correct method.
        /// </summary>
        protected abstract void DrawSelectedClipFields(SerializedProperty clipsProperty);
        
        protected void SetupEditModeHandler(
            VisualElement slidersContainer,
            VisualElement clipEditContainer,
            StateContentContext context)
        {
            cachedEditModeHandler = isEditMode =>
            {
                slidersContainer.style.display = isEditMode ? DisplayStyle.None : DisplayStyle.Flex;
                clipEditContainer.style.display = isEditMode ? DisplayStyle.Flex : DisplayStyle.None;
                AnimationPreviewEvents.RaiseBlendSpaceEditModeChanged(state, isEditMode);
                context.RequestRepaint?.Invoke();
            };
            blendSpaceEditor.OnEditModeChanged += cachedEditModeHandler;
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
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.value = value;
            
            var field = new FloatField();
            field.AddToClassList("property-float-field");
            field.style.width = FloatFieldWidth;
            field.style.marginLeft = SpacingMedium;
            field.value = value;
            
            valueContainer.Add(slider);
            valueContainer.Add(field);
            container.Add(valueContainer);
            
            return (container, slider, field);
        }
        
        #endregion
    }
}
