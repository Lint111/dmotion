using System;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    internal class EnumTypePopupSelector : TypePopupSelector
    {
        protected override bool TypeFilter(Type t)
        {
            return t != null && t.IsEnum;
        }
    }
    
    internal class TypePopupSelector
    {
        private Type filterType;
        
        // Cached GUIContent to avoid per-frame allocations
        private static readonly GUIContent _tempButtonContent = new GUIContent();
        private static readonly GUIContent NoneContent = new GUIContent("NONE");
        
        protected virtual bool TypeFilter(Type t)
        {
            return filterType.IsAssignableFrom(t);
        }

        internal void DrawSelectionPopup(Rect position, GUIContent label, Type selected, Action<Type> onSelected)
        {
            if (label != GUIContent.none)
            {
                var prevXMax = position.xMax;
                position.width = EditorGUIUtility.labelWidth;
                
                EditorGUI.LabelField(position, label);
                position.xMin += EditorGUIUtility.labelWidth;
                
                position.xMax = prevXMax;
            }

            GUIContent buttonContent;
            if (selected != null)
            {
                _tempButtonContent.text = selected.Name;
                _tempButtonContent.tooltip = null;
                _tempButtonContent.image = null;
                buttonContent = _tempButtonContent;
            }
            else
            {
                buttonContent = NoneContent;
            }
            
            if (EditorGUI.DropdownButton(position, buttonContent, FocusType.Passive))
            {
                var rect = position;
                rect.height = 400;
                rect.width = 250;
                rect = GUIUtility.GUIToScreenRect(rect);
                var w = EditorWindow.GetWindowWithRect<SelectSerializableTypePopup>(rect, true, "Select Type", true);
                w.position = rect;

                w.Show(selected, filterType, onSelected, TypeFilter);
            }
        }       
    }
}