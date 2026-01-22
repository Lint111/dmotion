using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Builds inspector content for Directional2DBlendStateAsset.
    /// Shows 2D blend space visualizer with X/Y preview controls.
    /// Uses pure UIToolkit for consistent event handling.
    /// </summary>
    internal class Directional2DBlendContentBuilder : BlendContentBuilderBase<Directional2DBlendStateAsset, BlendSpace2DVisualElement>
    {
        #region State
        
        private Vector2 previewBlendValue;
        
        // Cached UI references
        private Slider cachedXSlider;
        private FloatField cachedXField;
        private Slider cachedYSlider;
        private FloatField cachedYField;
        private VisualElement xSliderContainer;
        private VisualElement ySliderContainer;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Current 2D blend preview value.
        /// </summary>
        public Vector2 PreviewBlendValue => previewBlendValue;
        
        #endregion
        
        #region Abstract Implementations
        
        protected override float BlendSpaceHeight => BlendSpace2DDefaultHeight;
        
        protected override string SectionTitle => "Blend Space 2D";
        
        protected override string ClipsPropertyName => "BlendClips";
        
        protected override BlendSpace2DVisualElement GetOrCreateVisualElement()
        {
            blendSpaceElement ??= new BlendSpace2DVisualElement();
            blendSpaceElement.SetTarget(state);
            
            // Restore persisted blend value for this state
            previewBlendValue = PreviewSettings.instance.GetBlendValue2D(state, previewBlendValue);
            blendSpaceElement.PreviewPosition = previewBlendValue;
            
            return blendSpaceElement;
        }
        
        protected override void BuildParameterInfo(VisualElement section, StateContentContext context)
        {
            var paramXName = state.BlendParameterX?.name ?? "(none)";
            var paramYName = state.BlendParameterY?.name ?? "(none)";
            section.Add(context.CreatePropertyRow("Parameter X", paramXName));
            section.Add(context.CreatePropertyRow("Parameter Y", paramYName));
            section.Add(context.CreatePropertyRow("Clips", $"{state.BlendClips?.Length ?? 0}"));
        }
        
        protected override VisualElement BuildPreviewSliders(StateContentContext context)
        {
            // Calculate bounds
            Vector2 minBounds = Vector2.zero, maxBounds = Vector2.one;
            CalculateBlendBounds(ref minBounds, ref maxBounds);
            
            // Container for both sliders
            var container = new VisualElement();
            
            // X slider
            var (xContainer, xSlider, xField) = CreateSliderWithField(
                "Preview X", minBounds.x, maxBounds.x, previewBlendValue.x);
            xSliderContainer = xContainer;
            cachedXSlider = xSlider;
            cachedXField = xField;
            
            xSlider.RegisterValueChangedCallback(evt =>
            {
                SetBlendValueX(evt.newValue, context);
                cachedXField?.SetValueWithoutNotify(evt.newValue);
            });
            
            xField.RegisterValueChangedCallback(evt =>
            {
                SetBlendValueX(evt.newValue, context);
                cachedXSlider?.SetValueWithoutNotify(evt.newValue);
            });
            
            container.Add(xContainer);
            
            // Y slider
            var (yContainer, ySlider, yField) = CreateSliderWithField(
                "Preview Y", minBounds.y, maxBounds.y, previewBlendValue.y);
            ySliderContainer = yContainer;
            cachedYSlider = ySlider;
            cachedYField = yField;
            
            ySlider.RegisterValueChangedCallback(evt =>
            {
                SetBlendValueY(evt.newValue, context);
                cachedYField?.SetValueWithoutNotify(evt.newValue);
            });
            
            yField.RegisterValueChangedCallback(evt =>
            {
                SetBlendValueY(evt.newValue, context);
                cachedYSlider?.SetValueWithoutNotify(evt.newValue);
            });
            
            container.Add(yContainer);
            
            return container;
        }
        
        protected override void SetupPreviewPositionHandler(StateContentContext context)
        {
            cachedPreviewPositionHandler = pos =>
            {
                SetBlendValue(pos, context);
                cachedXSlider?.SetValueWithoutNotify(pos.x);
                cachedXField?.SetValueWithoutNotify(pos.x);
                cachedYSlider?.SetValueWithoutNotify(pos.y);
                cachedYField?.SetValueWithoutNotify(pos.y);
            };
            blendSpaceElement.OnPreviewPositionChanged += cachedPreviewPositionHandler;
        }
        
        private void SetBlendValueX(float x, StateContentContext context)
        {
            previewBlendValue.x = x;
            SetBlendValue(previewBlendValue, context);
        }
        
        private void SetBlendValueY(float y, StateContentContext context)
        {
            previewBlendValue.y = y;
            SetBlendValue(previewBlendValue, context);
        }
        
        private void SetBlendValue(Vector2 value, StateContentContext context)
        {
            previewBlendValue = value;
            
            // Persist to settings (shared across all previews of this state)
            PreviewSettings.instance.SetBlendValue2D(state, value);
            
            // Update visual element
            if (blendSpaceElement != null)
            {
                blendSpaceElement.PreviewPosition = value;
            }
            
            // Notify listeners (timeline duration updates via OnBlendStateChanged event)
            AnimationPreviewEvents.RaiseBlendPosition2DChanged(state, value);
            context.RequestRepaint?.Invoke();
        }
        
        protected override Vector2 GetCurrentBlendPosition()
        {
            return previewBlendValue;
        }
        
        protected override void GetLongestClipInfo(out float duration, out float frameRate)
        {
            duration = 1f;
            frameRate = 30f;
            
            if (state?.BlendClips == null) return;
            
            // Use raw clip length for duration - speed is applied via TimelineScrubber.PlaybackSpeed
            float maxDuration = 0f;
            for (int i = 0; i < state.BlendClips.Length; i++)
            {
                var blendClip = state.BlendClips[i];
                var clip = blendClip.Clip?.Clip;
                if (clip != null && clip.length > maxDuration)
                {
                    maxDuration = clip.length;
                    frameRate = clip.frameRate;
                }
            }
            if (maxDuration > 0) duration = maxDuration;
        }
        
        protected override void BuildClipEditContent(VisualElement container, StateContentContext context)
        {
            // Create a container that updates based on selection
            var selectionInfo = new Label("Click a clip in the blend space to select it.");
            selectionInfo.AddToClassList("clip-edit-hint");
            selectionInfo.style.color = new Color(0.6f, 0.6f, 0.6f);
            selectionInfo.style.unityFontStyleAndWeight = FontStyle.Italic;
            container.Add(selectionInfo);
            
            var clipFields = new VisualElement();
            clipFields.AddToClassList("clip-edit-fields");
            clipFields.style.display = DisplayStyle.None;
            container.Add(clipFields);
            
            // Position X field
            var posXRow = new VisualElement();
            posXRow.AddToClassList("property-row");
            posXRow.style.flexDirection = FlexDirection.Row;
            var posXLabel = new Label("Position X");
            posXLabel.AddToClassList("property-label");
            posXLabel.style.width = 80;
            var posXField = new FloatField();
            posXField.style.flexGrow = 1;
            posXRow.Add(posXLabel);
            posXRow.Add(posXField);
            clipFields.Add(posXRow);
            
            // Position Y field
            var posYRow = new VisualElement();
            posYRow.AddToClassList("property-row");
            posYRow.style.flexDirection = FlexDirection.Row;
            var posYLabel = new Label("Position Y");
            posYLabel.AddToClassList("property-label");
            posYLabel.style.width = 80;
            var posYField = new FloatField();
            posYField.style.flexGrow = 1;
            posYRow.Add(posYLabel);
            posYRow.Add(posYField);
            clipFields.Add(posYRow);
            
            // Update fields when selection changes
            blendSpaceElement.OnSelectionChanged += clipIndex =>
            {
                if (clipIndex >= 0 && state?.BlendClips != null && clipIndex < state.BlendClips.Length)
                {
                    selectionInfo.style.display = DisplayStyle.None;
                    clipFields.style.display = DisplayStyle.Flex;
                    
                    var clip = state.BlendClips[clipIndex];
                    posXField.SetValueWithoutNotify(clip.Position.x);
                    posYField.SetValueWithoutNotify(clip.Position.y);
                }
                else
                {
                    selectionInfo.style.display = DisplayStyle.Flex;
                    clipFields.style.display = DisplayStyle.None;
                }
            };
            
            // Handle position changes from fields
            posXField.RegisterValueChangedCallback(evt =>
            {
                if (blendSpaceElement.SelectedClipIndex >= 0 && serializedObject != null)
                {
                    var clipsProperty = serializedObject.FindProperty("BlendClips");
                    var clipProperty = clipsProperty.GetArrayElementAtIndex(blendSpaceElement.SelectedClipIndex);
                    var positionProperty = clipProperty.FindPropertyRelative("Position");
                    positionProperty.FindPropertyRelative("x").floatValue = evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                    blendSpaceElement.RefreshClips();
                }
            });
            
            posYField.RegisterValueChangedCallback(evt =>
            {
                if (blendSpaceElement.SelectedClipIndex >= 0 && serializedObject != null)
                {
                    var clipsProperty = serializedObject.FindProperty("BlendClips");
                    var clipProperty = clipsProperty.GetArrayElementAtIndex(blendSpaceElement.SelectedClipIndex);
                    var positionProperty = clipProperty.FindPropertyRelative("Position");
                    positionProperty.FindPropertyRelative("y").floatValue = evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                    blendSpaceElement.RefreshClips();
                }
            });
            
            // Handle clip position changes from drag
            blendSpaceElement.OnClipPositionChanged += (clipIndex, newPos) =>
            {
                if (serializedObject != null && clipIndex >= 0)
                {
                    var clipsProperty = serializedObject.FindProperty("BlendClips");
                    var clipProperty = clipsProperty.GetArrayElementAtIndex(clipIndex);
                    var positionProperty = clipProperty.FindPropertyRelative("Position");
                    positionProperty.FindPropertyRelative("x").floatValue = newPos.x;
                    positionProperty.FindPropertyRelative("y").floatValue = newPos.y;
                    serializedObject.ApplyModifiedProperties();
                    
                    // Update fields if this is the selected clip
                    if (clipIndex == blendSpaceElement.SelectedClipIndex)
                    {
                        posXField.SetValueWithoutNotify(newPos.x);
                        posYField.SetValueWithoutNotify(newPos.y);
                    }
                }
            };
        }
        
        protected override void ClearCachedUIReferences()
        {
            cachedXSlider = null;
            cachedXField = null;
            cachedYSlider = null;
            cachedYField = null;
            xSliderContainer = null;
            ySliderContainer = null;
        }
        
        #endregion
        
        #region Private Helpers
        
        private void CalculateBlendBounds(ref Vector2 min, ref Vector2 max)
        {
            if (state?.BlendClips == null || state.BlendClips.Length == 0) return;
            
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            
            for (int i = 0; i < state.BlendClips.Length; i++)
            {
                var pos = state.BlendClips[i].Position;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
            }
            
            var range = max - min;
            if (range.x < 0.1f) range.x = 1f;
            if (range.y < 0.1f) range.y = 1f;
            min -= range * 0.1f;
            max += range * 0.1f;
        }
        
        #endregion
    }
}
