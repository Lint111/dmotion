using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Factory interface for creating UI elements in the preview window.
    /// Abstracts UI creation for testability and cleaner dependency injection.
    /// </summary>
    internal interface IUIElementFactory
    {
        /// <summary>
        /// Creates a section header with type and name labels.
        /// </summary>
        VisualElement CreateSectionHeader(string type, string name);
        
        /// <summary>
        /// Creates a collapsible section (foldout).
        /// </summary>
        VisualElement CreateSection(string title);
        
        /// <summary>
        /// Creates a read-only property row with label and value.
        /// </summary>
        VisualElement CreatePropertyRow(string label, string value);
        
        /// <summary>
        /// Creates an editable float property with slider and numeric field.
        /// </summary>
        VisualElement CreateEditableFloatProperty(
            SerializedObject serializedObject,
            string propertyName,
            string label,
            float min,
            float max,
            string suffix = "");
        
        /// <summary>
        /// Creates an editable bool property with toggle and optional change callback.
        /// </summary>
        VisualElement CreateEditableBoolProperty(
            SerializedObject serializedObject,
            string propertyName,
            string label,
            Action<bool> onValueChanged = null);
        
        /// <summary>
        /// Creates an editable float property from a SerializedProperty.
        /// </summary>
        VisualElement CreateEditableSerializedFloatProperty(
            SerializedProperty property,
            string label,
            float min,
            float max,
            string suffix = "");
    }
}
