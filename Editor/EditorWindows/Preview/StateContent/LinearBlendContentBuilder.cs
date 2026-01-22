using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Builds inspector content for LinearBlendStateAsset.
    /// Shows 1D blend space visualizer with preview controls.
    /// Uses pure UIToolkit for consistent event handling.
    /// </summary>
    internal class LinearBlendContentBuilder : BlendContentBuilderBase<LinearBlendStateAsset, BlendSpace1DVisualElement>
    {
        #region State
        
        private float previewBlendValue;
        
        // Cached UI references
        private Slider cachedBlendSlider;
        private FloatField cachedBlendField;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Current blend preview value.
        /// </summary>
        public float PreviewBlendValue => previewBlendValue;
        
        #endregion
        
        #region Abstract Implementations
        
        protected override float BlendSpaceHeight => BlendSpace1DDefaultHeight;
        
        protected override string SectionTitle => "Blend Space 1D";
        
        protected override string ClipsPropertyName => "BlendClips";
        
        protected override BlendSpace1DVisualElement GetOrCreateVisualElement()
        {
            blendSpaceElement ??= new BlendSpace1DVisualElement();
            blendSpaceElement.SetTarget(state);
            
            // Restore persisted blend value for this state
            previewBlendValue = PreviewSettings.instance.GetBlendValue1D(state, previewBlendValue);
            blendSpaceElement.PreviewPosition = new Vector2(previewBlendValue, 0);
            
            return blendSpaceElement;
        }
        
        protected override void BuildParameterInfo(VisualElement section, StateContentContext context)
        {
            var paramName = state.BlendParameter?.name ?? "(none)";
            section.Add(context.CreatePropertyRow("Parameter", paramName));
            section.Add(context.CreatePropertyRow("Clips", $"{state.BlendClips?.Length ?? 0}"));
        }
        
        protected override VisualElement BuildPreviewSliders(StateContentContext context)
        {
            // Calculate blend range
            float minThreshold = 0f, maxThreshold = 1f;
            CalculateBlendRange(ref minThreshold, ref maxThreshold);
            
            var (container, slider, field) = CreateSliderWithField(
                "Preview Value", minThreshold, maxThreshold, previewBlendValue);
            
            cachedBlendSlider = slider;
            cachedBlendField = field;
            
            // Slider change handler
            slider.RegisterValueChangedCallback(evt =>
            {
                SetBlendValue(evt.newValue, context);
                cachedBlendField?.SetValueWithoutNotify(evt.newValue);
            });
            
            // Field change handler
            field.RegisterValueChangedCallback(evt =>
            {
                SetBlendValue(evt.newValue, context);
                cachedBlendSlider?.SetValueWithoutNotify(evt.newValue);
            });
            
            return container;
        }
        
        protected override void SetupPreviewPositionHandler(StateContentContext context)
        {
            cachedPreviewPositionHandler = pos =>
            {
                SetBlendValue(pos.x, context);
                cachedBlendSlider?.SetValueWithoutNotify(pos.x);
                cachedBlendField?.SetValueWithoutNotify(pos.x);
            };
            blendSpaceElement.OnPreviewPositionChanged += cachedPreviewPositionHandler;
        }
        
        private void SetBlendValue(float value, StateContentContext context)
        {
            previewBlendValue = value;
            
            // Persist to settings (shared across all previews of this state)
            PreviewSettings.instance.SetBlendValue1D(state, value);
            
            // Update visual element
            if (blendSpaceElement != null)
            {
                blendSpaceElement.PreviewPosition = new Vector2(value, 0);
            }
            
            // Notify listeners (timeline duration updates via OnBlendStateChanged event)
            AnimationPreviewEvents.RaiseBlendPosition1DChanged(state, value);
            context.RequestRepaint?.Invoke();
        }
        
        protected override Vector2 GetCurrentBlendPosition()
        {
            return new Vector2(previewBlendValue, 0);
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
            var selectionInfo = new Label("Click a clip on the track to select it.");
            selectionInfo.AddToClassList("clip-edit-hint");
            selectionInfo.style.color = new Color(0.6f, 0.6f, 0.6f);
            selectionInfo.style.unityFontStyleAndWeight = FontStyle.Italic;
            container.Add(selectionInfo);
            
            var clipFields = new VisualElement();
            clipFields.AddToClassList("clip-edit-fields");
            clipFields.style.display = DisplayStyle.None;
            container.Add(clipFields);
            
            // Threshold field
            var thresholdRow = new VisualElement();
            thresholdRow.AddToClassList("property-row");
            thresholdRow.style.flexDirection = FlexDirection.Row;
            var thresholdLabel = new Label("Threshold");
            thresholdLabel.AddToClassList("property-label");
            thresholdLabel.style.width = 80;
            var thresholdField = new FloatField();
            thresholdField.style.flexGrow = 1;
            thresholdRow.Add(thresholdLabel);
            thresholdRow.Add(thresholdField);
            clipFields.Add(thresholdRow);
            
            // Update fields when selection changes
            blendSpaceElement.OnSelectionChanged += clipIndex =>
            {
                if (clipIndex >= 0 && state?.BlendClips != null && clipIndex < state.BlendClips.Length)
                {
                    selectionInfo.style.display = DisplayStyle.None;
                    clipFields.style.display = DisplayStyle.Flex;
                    
                    var clip = state.BlendClips[clipIndex];
                    thresholdField.SetValueWithoutNotify(clip.Threshold);
                }
                else
                {
                    selectionInfo.style.display = DisplayStyle.Flex;
                    clipFields.style.display = DisplayStyle.None;
                }
            };
            
            // Handle threshold changes from field
            thresholdField.RegisterValueChangedCallback(evt =>
            {
                if (blendSpaceElement.SelectedClipIndex >= 0 && serializedObject != null)
                {
                    var clipsProperty = serializedObject.FindProperty("BlendClips");
                    var clipProperty = clipsProperty.GetArrayElementAtIndex(blendSpaceElement.SelectedClipIndex);
                    var thresholdProperty = clipProperty.FindPropertyRelative("Threshold");
                    thresholdProperty.floatValue = evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                    blendSpaceElement.RefreshClips();
                }
            });
            
            // Handle threshold changes from drag
            blendSpaceElement.OnClipThresholdChanged += (clipIndex, newThreshold) =>
            {
                if (serializedObject != null && clipIndex >= 0)
                {
                    var clipsProperty = serializedObject.FindProperty("BlendClips");
                    var clipProperty = clipsProperty.GetArrayElementAtIndex(clipIndex);
                    var thresholdProperty = clipProperty.FindPropertyRelative("Threshold");
                    thresholdProperty.floatValue = newThreshold;
                    serializedObject.ApplyModifiedProperties();
                    
                    // Update field if this is the selected clip
                    if (clipIndex == blendSpaceElement.SelectedClipIndex)
                    {
                        thresholdField.SetValueWithoutNotify(newThreshold);
                    }
                }
            };
        }
        
        protected override void ClearCachedUIReferences()
        {
            cachedBlendSlider = null;
            cachedBlendField = null;
        }
        
        #endregion
        
        #region Private Helpers
        
        private void CalculateBlendRange(ref float min, ref float max)
        {
            if (state?.BlendClips == null || state.BlendClips.Length == 0) return;
            
            min = float.MaxValue;
            max = float.MinValue;
            
            for (int i = 0; i < state.BlendClips.Length; i++)
            {
                min = Mathf.Min(min, state.BlendClips[i].Threshold);
                max = Mathf.Max(max, state.BlendClips[i].Threshold);
            }
            
            var range = max - min;
            if (range < 0.1f) range = 1f;
            min -= range * 0.1f;
            max += range * 0.1f;
        }
        
        #endregion
    }
}
