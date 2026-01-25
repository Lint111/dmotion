using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Static factory for creating common UI elements in preview windows.
    /// Provides consistent styling and reduces code duplication across builders.
    /// </summary>
    internal static class PreviewUIFactory
    {
        #region Property Rows
        
        /// <summary>
        /// Creates a read-only property row with label and value.
        /// </summary>
        public static VisualElement CreatePropertyRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = PreviewEditorConstants.SpacingSmall;

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = PreviewEditorConstants.PropertyLabelWidth;
            labelElement.style.minWidth = PreviewEditorConstants.PropertyLabelWidth;

            var valueElement = new Label(value);
            valueElement.AddToClassList("property-value");

            row.Add(labelElement);
            row.Add(valueElement);

            return row;
        }
        
        /// <summary>
        /// Creates a slider with float field, with optional output references for bidirectional sync.
        /// </summary>
        public static VisualElement CreateSliderWithField(
            string label, 
            float min, 
            float max, 
            float value,
            out Slider outSlider, 
            out FloatField outField, 
            Action<float> onValueChanged, 
            string suffix = "")
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = PreviewEditorConstants.SpacingSmall;
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = PreviewEditorConstants.PropertyLabelWidth;
            labelElement.style.minWidth = PreviewEditorConstants.PropertyLabelWidth;
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
            field.style.width = PreviewEditorConstants.FloatFieldWidth;
            field.style.marginLeft = PreviewEditorConstants.SpacingMedium;
            field.value = value;
            
            slider.RegisterValueChangedCallback(evt =>
            {
                field.SetValueWithoutNotify(evt.newValue);
                onValueChanged?.Invoke(evt.newValue);
            });
            
            field.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.Clamp(evt.newValue, min, max * 2);
                slider.SetValueWithoutNotify(Mathf.Clamp(clamped, min, max));
                onValueChanged?.Invoke(clamped);
            });
            
            valueContainer.Add(slider);
            valueContainer.Add(field);
            
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.style.marginLeft = PreviewEditorConstants.SpacingSmall;
                suffixLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                valueContainer.Add(suffixLabel);
            }
            
            container.Add(valueContainer);
            
            outSlider = slider;
            outField = field;
            
            return container;
        }
        
        /// <summary>
        /// Creates a slider with float field (convenience overload without output refs).
        /// </summary>
        public static VisualElement CreateSliderWithField(
            string label, 
            float min, 
            float max, 
            float value,
            Action<float> onValueChanged, 
            string suffix = "")
        {
            return CreateSliderWithField(label, min, max, value, out _, out _, onValueChanged, suffix);
        }
        
        /// <summary>
        /// Creates a toggle row with label.
        /// </summary>
        public static VisualElement CreateToggleRow(string label, bool value, Action<bool> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = PreviewEditorConstants.SpacingSmall;
            
            var labelElement = new Label(label);
            labelElement.style.width = PreviewEditorConstants.PropertyLabelWidth;
            labelElement.style.minWidth = PreviewEditorConstants.PropertyLabelWidth;
            container.Add(labelElement);
            
            var toggle = new Toggle();
            toggle.value = value;
            toggle.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            container.Add(toggle);
            
            return container;
        }
        
        #endregion
        
        #region Section Headers
        
        /// <summary>
        /// Creates a section header with type and name labels.
        /// </summary>
        public static VisualElement CreateSectionHeader(string type, string name)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingBottom = PreviewEditorConstants.SpacingMedium;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = PreviewEditorColors.Border;
            header.style.marginBottom = PreviewEditorConstants.SpacingLarge;

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");
            typeLabel.style.color = PreviewEditorColors.DimText;
            typeLabel.style.marginRight = PreviewEditorConstants.SpacingLarge;

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            header.Add(typeLabel);
            header.Add(nameLabel);

            return header;
        }
        
        /// <summary>
        /// Creates a collapsible section (foldout).
        /// </summary>
        public static Foldout CreateSection(string title)
        {
            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("section-foldout");
            return foldout;
        }
        
        #endregion
    }
}
