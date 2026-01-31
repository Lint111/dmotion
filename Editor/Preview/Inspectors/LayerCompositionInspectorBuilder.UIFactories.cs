using System;
using DMotion;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Private - UI Factories
        
        private VisualElement CreateSectionHeader(string type, string name)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");

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

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");

            var valueElement = new Label(value);
            valueElement.AddToClassList("property-value");

            row.Add(labelElement);
            row.Add(valueElement);

            return row;
        }
        
        private VisualElement CreateSliderRow(
            string label, 
            float min, 
            float max, 
            float value,
            Action<float> onChanged,
            out Slider outSlider,
            out Label outValueLabel)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.minWidth = WeightSliderMinWidth;
            row.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.value = value;
            
            var valueLabel = new Label(value.ToString("F2"));
            valueLabel.AddToClassList("value-label");
            valueLabel.style.minWidth = BlendFieldMinWidth;
            
            slider.RegisterValueChangedCallback(evt =>
            {
                valueLabel.text = evt.newValue.ToString("F2");
                onChanged?.Invoke(evt.newValue);
            });
            
            valueContainer.Add(slider);
            valueContainer.Add(valueLabel);
            row.Add(valueContainer);
            
            outSlider = slider;
            outValueLabel = valueLabel;
            
            return row;
        }
        
        #endregion
        
        #region Private - Transition Lookup
        
        /// <summary>
        /// Finds the transition asset between two states, if one exists.
        /// Searches the FROM state's OutTransitions for a transition to the TO state.
        /// </summary>
        /// <remarks>
        /// Note: When multiple transitions exist between the same states (with different conditions),
        /// this returns the first match. The graph view intentionally groups all transitions between
        /// the same states into a single edge, so we don't have access to the specific transition
        /// the user clicked. This is acceptable for preview timing purposes.
        /// TODO: If needed, add a dropdown in the preview UI to select which transition to preview
        /// when multiple exist between the same states.
        /// </remarks>
        /// <param name="fromState">The source state</param>
        /// <param name="toState">The target state</param>
        /// <returns>The first StateOutTransition matching the target, or null if none found</returns>
        private static StateOutTransition FindTransition(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            if (fromState == null || toState == null || fromState.OutTransitions == null)
                return null;

            foreach (var transition in fromState.OutTransitions)
            {
                if (transition.ToState == toState)
                    return transition;
            }

            return null;
        }
        
        /// <summary>
        /// Creates a TransitionStateConfig for the given from/to states with their blend positions.
        /// Uses TransitionStateCalculator for proper timing calculation.
        /// </summary>
        /// <param name="fromState">Source state</param>
        /// <param name="toState">Target state</param>
        /// <param name="fromBlendPos">Blend position for from state</param>
        /// <param name="toBlendPos">Blend position for to state</param>
        /// <param name="includeGhostBars">If false, ghost bar durations are zeroed out</param>
        private static TransitionStateConfig CreateTransitionConfig(
            AnimationStateAsset fromState, 
            AnimationStateAsset toState, 
            float2 fromBlendPos,
            float2 toBlendPos,
            bool includeGhostBars = true)
        {
            var transition = FindTransition(fromState, toState);
            var config = TransitionStateCalculator.CreateConfig(fromState, toState, transition, fromBlendPos, toBlendPos);
            
            // If ghost bars should be excluded, zero them out
            if (!includeGhostBars)
            {
                var timing = config.Timing;
                timing.GhostFromDuration = 0f;
                timing.GhostToDuration = 0f;
                config.Timing = timing;
            }
            
            return config;
        }
        
        #endregion
    }
}
