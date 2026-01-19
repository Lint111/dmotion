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
    /// </summary>
    internal class LinearBlendContentBuilder : BlendContentBuilderBase<LinearBlendStateAsset, BlendSpace1DVisualEditor>
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
        
        protected override float BlendSpaceHeight => PreviewWindowConstants.BlendSpace1DHeight;
        
        protected override string SectionTitle => "Blend Space 1D";
        
        protected override string ClipsPropertyName => "BlendClips";
        
        protected override BlendSpace1DVisualEditor GetOrCreateEditor()
        {
            blendSpaceEditor ??= new BlendSpace1DVisualEditor();
            blendSpaceEditor.SetTarget(state);
            blendSpaceEditor.PreviewPosition = new Vector2(previewBlendValue, 0);
            return blendSpaceEditor;
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
                previewBlendValue = evt.newValue;
                cachedBlendField?.SetValueWithoutNotify(evt.newValue);
                if (blendSpaceEditor != null)
                {
                    blendSpaceEditor.PreviewPosition = new Vector2(evt.newValue, 0);
                }
                AnimationPreviewEvents.RaiseBlendPosition1DChanged(state, evt.newValue);
                context.RequestRepaint?.Invoke();
            });
            
            // Field change handler
            field.RegisterValueChangedCallback(evt =>
            {
                previewBlendValue = evt.newValue;
                cachedBlendSlider?.SetValueWithoutNotify(evt.newValue);
                if (blendSpaceEditor != null)
                {
                    blendSpaceEditor.PreviewPosition = new Vector2(evt.newValue, 0);
                }
                AnimationPreviewEvents.RaiseBlendPosition1DChanged(state, evt.newValue);
                context.RequestRepaint?.Invoke();
            });
            
            return container;
        }
        
        protected override void SetupPreviewPositionHandler(StateContentContext context)
        {
            cachedPreviewPositionHandler = pos =>
            {
                previewBlendValue = pos.x;
                cachedBlendSlider?.SetValueWithoutNotify(pos.x);
                cachedBlendField?.SetValueWithoutNotify(pos.x);
                AnimationPreviewEvents.RaiseBlendPosition1DChanged(state, pos.x);
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
