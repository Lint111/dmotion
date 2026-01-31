using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Blend Space Binding
        
        /// <summary>
        /// Creates or updates the blend space visual element for a layer based on its selected state.
        /// </summary>
        private void BindBlendSpaceElement(LayerSectionData section, AnimationStateAsset selectedState)
        {
            // Skip if already bound to this state AND element still exists in hierarchy
            if (section.BoundBlendState == selectedState &&
                section.BlendSpaceElement != null &&
                section.BlendSpaceElement.parent != null)
                return;

            // Cleanup previous element (also clears container defensively)
            UnbindBlendSpaceElement(section);

            section.BoundBlendState = selectedState;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(selectedState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persistedValue = result.InitialPosition;

            // Update slider range
            if (section.BlendSlider != null)
            {
                section.BlendSlider.lowValue = result.Range.MinX;
                section.BlendSlider.highValue = result.Range.MaxX;
            }
            section.BlendSlider?.SetValueWithoutNotify(persistedValue.x);
            section.BlendField?.SetValueWithoutNotify(persistedValue.x);

            // Sync to observable
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer != null)
                layer.BlendPosition = new float2(persistedValue.x, persistedValue.y);

            // Handle preview position changes from the visual element
            section.CachedPreviewPositionHandler = result.Is2D
                ? pos =>
                {
                    SetLayerBlendValue2D(section, pos);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                }
                : pos =>
                {
                    SetLayerBlendValue(section, pos.x);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                };
            element.OnPreviewPositionChanged += section.CachedPreviewPositionHandler;

            // Defensive: ensure container is clear before adding
            section.BlendSpaceContainer.Clear();
            section.BlendSpaceElement = element;
            section.BlendSpaceContainer.Add(element);
        }

        /// <summary>
        /// Removes and cleans up the current blend space visual element for a layer.
        /// </summary>
        private void UnbindBlendSpaceElement(LayerSectionData section)
        {
            if (section.BlendSpaceElement != null && section.CachedPreviewPositionHandler != null)
            {
                section.BlendSpaceElement.OnPreviewPositionChanged -= section.CachedPreviewPositionHandler;
                section.CachedPreviewPositionHandler = null;
            }

            section.BlendSpaceContainer?.Clear();
            section.BlendSpaceElement = null;
            section.BoundBlendState = null;
        }

        /// <summary>
        /// Binds blend space elements for transition from/to states.
        /// </summary>
        private void BindTransitionBlendElements(LayerSectionData section, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            // Bind "from" state blend element
            BindTransitionFromBlendElement(section, fromState);

            // Bind "to" state blend element
            BindTransitionToBlendElement(section, toState);
        }

        private void BindTransitionFromBlendElement(LayerSectionData section, AnimationStateAsset fromState)
        {
            // Skip if already bound to this state
            if (section.BoundFromState == fromState && section.FromBlendSpaceElement?.parent != null)
                return;

            UnbindTransitionFromBlendElement(section);
            section.BoundFromState = fromState;

            var fromBlendRow = section.TransitionControls?.Q<VisualElement>("from-blend-row");
            bool isBlendState = BlendSpaceUIBuilder.IsBlendState(fromState);

            if (fromBlendRow != null)
                fromBlendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;
            if (section.FromBlendSpaceContainer != null)
                section.FromBlendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(fromState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persisted = result.InitialPosition;

            // Wire up position change handler - propagate to backend and update UI
            section.CachedFromPreviewPositionHandler = pos =>
            {
                // Update slider UI
                section.FromBlendSlider?.SetValueWithoutNotify(pos.x);
                if (section.FromBlendLabel != null)
                    section.FromBlendLabel.text = pos.x.ToString("F2");
                
                // Propagate to backend (persists to PreviewSettings and updates playable graph)
                SetTransitionFromBlendValue(section, pos.x);
            };
            element.OnPreviewPositionChanged += section.CachedFromPreviewPositionHandler;

            section.FromBlendSpaceContainer?.Clear();
            section.FromBlendSpaceElement = element;
            section.FromBlendSpaceContainer?.Add(element);

            // Update slider range
            if (section.FromBlendSlider != null)
            {
                section.FromBlendSlider.lowValue = result.Range.MinX;
                section.FromBlendSlider.highValue = result.Range.MaxX;
            }
            section.FromBlendSlider?.SetValueWithoutNotify(persisted.x);
            if (section.FromBlendLabel != null)
                section.FromBlendLabel.text = persisted.x.ToString("F2");
        }

        private void BindTransitionToBlendElement(LayerSectionData section, AnimationStateAsset toState)
        {
            // Skip if already bound to this state
            if (section.BoundToState == toState && section.ToBlendSpaceElement?.parent != null)
                return;

            UnbindTransitionToBlendElement(section);
            section.BoundToState = toState;

            var toBlendRow = section.TransitionControls?.Q<VisualElement>("to-blend-row");
            bool isBlendState = BlendSpaceUIBuilder.IsBlendState(toState);

            if (toBlendRow != null)
                toBlendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;
            if (section.ToBlendSpaceContainer != null)
                section.ToBlendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(toState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persisted = result.InitialPosition;

            // Wire up position change handler - propagate to backend and update UI
            section.CachedToPreviewPositionHandler = pos =>
            {
                // Update slider UI
                section.ToBlendSlider?.SetValueWithoutNotify(pos.x);
                if (section.ToBlendLabel != null)
                    section.ToBlendLabel.text = pos.x.ToString("F2");
                
                // Propagate to backend (persists to PreviewSettings and updates playable graph)
                SetTransitionToBlendValue(section, pos.x);
            };
            element.OnPreviewPositionChanged += section.CachedToPreviewPositionHandler;

            section.ToBlendSpaceContainer?.Clear();
            section.ToBlendSpaceElement = element;
            section.ToBlendSpaceContainer?.Add(element);

            // Update slider range
            if (section.ToBlendSlider != null)
            {
                section.ToBlendSlider.lowValue = result.Range.MinX;
                section.ToBlendSlider.highValue = result.Range.MaxX;
            }
            section.ToBlendSlider?.SetValueWithoutNotify(persisted.x);
            if (section.ToBlendLabel != null)
                section.ToBlendLabel.text = persisted.x.ToString("F2");
        }

        /// <summary>
        /// Unbinds all transition blend elements.
        /// </summary>
        private void UnbindTransitionBlendElements(LayerSectionData section)
        {
            UnbindTransitionFromBlendElement(section);
            UnbindTransitionToBlendElement(section);
        }

        private void UnbindTransitionFromBlendElement(LayerSectionData section)
        {
            if (section.FromBlendSpaceElement != null && section.CachedFromPreviewPositionHandler != null)
            {
                section.FromBlendSpaceElement.OnPreviewPositionChanged -= section.CachedFromPreviewPositionHandler;
                section.CachedFromPreviewPositionHandler = null;
            }

            section.FromBlendSpaceContainer?.Clear();
            section.FromBlendSpaceElement = null;
            section.BoundFromState = null;
        }

        private void UnbindTransitionToBlendElement(LayerSectionData section)
        {
            if (section.ToBlendSpaceElement != null && section.CachedToPreviewPositionHandler != null)
            {
                section.ToBlendSpaceElement.OnPreviewPositionChanged -= section.CachedToPreviewPositionHandler;
                section.CachedToPreviewPositionHandler = null;
            }

            section.ToBlendSpaceContainer?.Clear();
            section.ToBlendSpaceElement = null;
            section.BoundToState = null;
        }

        #endregion

        #region Blend Value Setters

        private void SetLayerBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new float2(value, layer.BlendPosition.y);

            // Update visual element
            if (section.BlendSpaceElement != null)
                section.BlendSpaceElement.PreviewPosition = new Vector2(value, section.BlendSpaceElement.PreviewPosition.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Persist via PreviewSettings (preserves Y for 2D blend states)
            PreviewSettings.SetBlendPositionX(layer.SelectedState, value);
            
            // Update timeline duration (changes with blend position for blend states)
            ConfigureLayerTimeline(section, layer);
        }

        private void SetLayerBlendValue2D(LayerSectionData section, Vector2 value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new float2(value.x, value.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Visual element already updated by the caller
            // Persist via PreviewSettings
            PreviewSettings.SetBlendPosition(layer.SelectedState, value);
            
            // Update timeline duration (changes with blend position for blend states)
            ConfigureLayerTimeline(section, layer);
        }

        private void SetTransitionFromBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer?.TransitionFrom == null) return;

            // Update visual element's preview position
            if (section.FromBlendSpaceElement != null)
            {
                var pos = section.FromBlendSpaceElement.PreviewPosition;
                section.FromBlendSpaceElement.PreviewPosition = new Vector2(value, pos.y);
            }

            // Persist via PreviewSettings
            PreviewSettings.SetBlendPositionX(layer.TransitionFrom, value);

            // Propagate to preview backend (transition blend positions)
            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex, 
                new float2(value, 0),
                new float2(toBlendPos.x, toBlendPos.y));
        }

        private void SetTransitionToBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer?.TransitionTo == null) return;

            // Update visual element's preview position
            if (section.ToBlendSpaceElement != null)
            {
                var pos = section.ToBlendSpaceElement.PreviewPosition;
                section.ToBlendSpaceElement.PreviewPosition = new Vector2(value, pos.y);
            }

            // Persist via PreviewSettings
            PreviewSettings.SetBlendPositionX(layer.TransitionTo, value);

            // Propagate to preview backend (transition blend positions)
            var fromBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex, 
                new float2(fromBlendPos.x, fromBlendPos.y),
                new float2(value, 0));
        }

        #endregion
    }
}
