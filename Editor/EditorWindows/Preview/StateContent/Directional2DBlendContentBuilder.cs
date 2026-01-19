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
    /// </summary>
    internal class Directional2DBlendContentBuilder : BlendContentBuilderBase<Directional2DBlendStateAsset, BlendSpace2DVisualEditor>
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
        
        protected override float BlendSpaceHeight => PreviewWindowConstants.BlendSpace2DHeight;
        
        protected override string SectionTitle => "Blend Space 2D";
        
        protected override string ClipsPropertyName => "BlendClips";
        
        protected override BlendSpace2DVisualEditor GetOrCreateEditor()
        {
            blendSpaceEditor ??= new BlendSpace2DVisualEditor();
            blendSpaceEditor.SetTarget(state);
            blendSpaceEditor.PreviewPosition = previewBlendValue;
            return blendSpaceEditor;
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
                previewBlendValue.x = evt.newValue;
                cachedXField?.SetValueWithoutNotify(evt.newValue);
                if (blendSpaceEditor != null)
                {
                    blendSpaceEditor.PreviewPosition = previewBlendValue;
                }
                AnimationPreviewEvents.RaiseBlendPosition2DChanged(state, previewBlendValue);
                context.RequestRepaint?.Invoke();
            });
            
            xField.RegisterValueChangedCallback(evt =>
            {
                previewBlendValue.x = evt.newValue;
                cachedXSlider?.SetValueWithoutNotify(evt.newValue);
                if (blendSpaceEditor != null)
                {
                    blendSpaceEditor.PreviewPosition = previewBlendValue;
                }
                AnimationPreviewEvents.RaiseBlendPosition2DChanged(state, previewBlendValue);
                context.RequestRepaint?.Invoke();
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
                previewBlendValue.y = evt.newValue;
                cachedYField?.SetValueWithoutNotify(evt.newValue);
                if (blendSpaceEditor != null)
                {
                    blendSpaceEditor.PreviewPosition = previewBlendValue;
                }
                AnimationPreviewEvents.RaiseBlendPosition2DChanged(state, previewBlendValue);
                context.RequestRepaint?.Invoke();
            });
            
            yField.RegisterValueChangedCallback(evt =>
            {
                previewBlendValue.y = evt.newValue;
                cachedYSlider?.SetValueWithoutNotify(evt.newValue);
                if (blendSpaceEditor != null)
                {
                    blendSpaceEditor.PreviewPosition = previewBlendValue;
                }
                AnimationPreviewEvents.RaiseBlendPosition2DChanged(state, previewBlendValue);
                context.RequestRepaint?.Invoke();
            });
            
            container.Add(yContainer);
            
            return container;
        }
        
        protected override void SetupPreviewPositionHandler(StateContentContext context)
        {
            cachedPreviewPositionHandler = pos =>
            {
                previewBlendValue = pos;
                cachedXSlider?.SetValueWithoutNotify(pos.x);
                cachedXField?.SetValueWithoutNotify(pos.x);
                cachedYSlider?.SetValueWithoutNotify(pos.y);
                cachedYField?.SetValueWithoutNotify(pos.y);
                AnimationPreviewEvents.RaiseBlendPosition2DChanged(state, pos);
                context.RequestRepaint?.Invoke();
            };
            blendSpaceEditor.OnPreviewPositionChanged += cachedPreviewPositionHandler;
        }
        
        protected override void GetLongestClipInfo(out float duration, out float frameRate)
        {
            duration = 1f;
            frameRate = 30f;
            
            if (state?.BlendClips == null) return;
            
            float maxDuration = 0f;
            for (int i = 0; i < state.BlendClips.Length; i++)
            {
                var clip = state.BlendClips[i].Clip?.Clip;
                if (clip != null && clip.length > maxDuration)
                {
                    maxDuration = clip.length;
                    frameRate = clip.frameRate;
                }
            }
            if (maxDuration > 0) duration = maxDuration;
        }
        
        protected override void DrawSelectedClipFields(SerializedProperty clipsProperty)
        {
            if (state?.BlendClips != null)
            {
                if (blendSpaceEditor.DrawSelectedClipFields(state.BlendClips, clipsProperty))
                {
                    serializedObject.ApplyModifiedProperties();
                }
            }
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
