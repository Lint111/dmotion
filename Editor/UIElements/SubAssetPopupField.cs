using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DMotion.Editor
{
    /// <summary>
    /// UIToolkit popup field for selecting sub-assets from a parent asset.
    /// Supports type filtering to show only specific sub-asset types.
    /// </summary>
    internal class SubAssetPopupField : PopupField<Object>
    {
        private readonly Object parentAsset;
        private readonly Type[] filterTypes;
        private readonly string noOptionsMessage;
        private SerializedProperty boundProperty;
        
        // Cached choices to avoid re-collecting on every access
        private bool choicesDirty = true;
        private static bool globalDirtyFlag;
        
        static SubAssetPopupField()
        {
            // Mark dirty when assets change
            ObjectReferencePopupSelector.IsDirty = true;
        }
        
        /// <summary>
        /// Creates a new SubAssetPopupField.
        /// </summary>
        /// <param name="parentAsset">The parent asset containing sub-assets.</param>
        /// <param name="label">Field label.</param>
        /// <param name="noOptionsMessage">Message to display when no options available.</param>
        /// <param name="filterTypes">Optional types to filter sub-assets by.</param>
        public SubAssetPopupField(
            Object parentAsset,
            string label = null,
            string noOptionsMessage = "No options available",
            params Type[] filterTypes)
            : base(label ?? "", new List<Object>(), 0, FormatSelectedValue, FormatListItem)
        {
            this.parentAsset = parentAsset;
            this.filterTypes = filterTypes;
            this.noOptionsMessage = noOptionsMessage;
            
            AddToClassList("sub-asset-popup-field");
            
            // Refresh choices when opening dropdown
            RegisterCallback<AttachToPanelEvent>(_ => RefreshChoices());
            
            // Track changes
            this.RegisterValueChangedCallback(OnValueChanged);
        }
        
        /// <summary>
        /// Binds this field to a SerializedProperty.
        /// </summary>
        public void BindProperty(SerializedProperty property)
        {
            boundProperty = property;

            // Set initial value

            RefreshChoices();


            if ((property?.objectReferenceValue) == null) return;

            var index = choices.IndexOf(property.objectReferenceValue);

            if (index < 0) return;
            
            SetValueWithoutNotify(property.objectReferenceValue);
        }


        private void OnValueChanged(ChangeEvent<Object> evt)
        {
            if (boundProperty == null || evt.newValue == boundProperty.objectReferenceValue) return;

            boundProperty.objectReferenceValue = evt.newValue;
            boundProperty.serializedObject.ApplyModifiedProperties();
        }


        /// <summary>
        /// Refreshes the available choices from the parent asset.
        /// </summary>
        public void RefreshChoices()
        {
            if (parentAsset == null) return;
            
            var newChoices = CollectSubAssets();
            
            // Update choices
            choices = newChoices;
            
            // Handle no options case
            if (newChoices.Count == 0)
            {
                SetEnabled(false);
                // Show placeholder - can't easily set text on disabled popup
            }
            else
            {
                SetEnabled(true);
                
                // Restore selection if possible
                if (boundProperty?.objectReferenceValue != null)
                {
                    var index = newChoices.IndexOf(boundProperty.objectReferenceValue);
                    if (index >= 0)
                    {
                        SetValueWithoutNotify(newChoices[index]);
                    }
                    else if (newChoices.Count > 0)
                    {
                        SetValueWithoutNotify(newChoices[0]);
                    }
                }
                else if (newChoices.Count > 0 && value == null)
                {
                    // Don't auto-select, leave as null
                }
            }
            
            choicesDirty = false;
        }
        
        private List<Object> CollectSubAssets()
        {
            var result = new List<Object>();
            
            if (parentAsset == null) return result;
            
            var assetPath = AssetDatabase.GetAssetPath(parentAsset);
            if (string.IsNullOrEmpty(assetPath)) return result;
            
            var allRepresentations = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            
            foreach (var asset in allRepresentations)
            {
                if (asset == null) continue;
                
                // Check filter types if specified
                if (filterTypes != null && filterTypes.Length > 0)
                {
                    bool passesFilter = false;
                    foreach (var filterType in filterTypes)
                    {
                        if (filterType.IsInstanceOfType(asset))
                        {
                            passesFilter = true;
                            break;
                        }
                    }
                    if (!passesFilter) continue;
                }
                
                result.Add(asset);
            }
            
            return result;
        }


        private static string FormatSelectedValue(Object obj) => obj != null ? obj.name : "None";


        private static string FormatListItem(Object obj) => obj != null ? obj.name : "None";
        
    }
    
    /// <summary>
    /// Factory extension for creating SubAssetPopupField in UXML/C#.
    /// </summary>
    internal static class SubAssetPopupFieldExtensions
    {
        /// <summary>
        /// Creates a property row containing a SubAssetPopupField with label.
        /// </summary>
        public static VisualElement CreateSubAssetPopupRow(
            this VisualElement container,
            string label,
            Object parentAsset,
            SerializedProperty property,
            string noOptionsMessage = "Add a parameter",
            params Type[] filterTypes)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            row.Add(labelElement);
            
            var popup = new SubAssetPopupField(parentAsset, null, noOptionsMessage, filterTypes);
            popup.AddToClassList("property-field");
            popup.BindProperty(property);
            row.Add(popup);
            
            container.Add(row);
            return row;
        }
    }
}
