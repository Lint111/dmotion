using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides non-boxing enum popup methods by caching enum names.
    /// Use instead of EditorGUI.EnumPopup to avoid GC allocations.
    /// </summary>
    internal static class EnumPopupCache
    {
        // Cached display names for each enum type
        private static readonly Dictionary<Type, string[]> _enumNames = new();
        private static readonly Dictionary<Type, GUIContent[]> _enumContents = new();
        
        // Pre-cached common enums used in the editor
        private static readonly string[] LayerBlendModeNames = { "Override", "Additive" };
        private static readonly string[] BoolConditionNames = { "True", "False" };
        private static readonly string[] IntConditionNames = { "==", "!=", ">", "<", ">=", "<=" };
        
        /// <summary>
        /// Draws a popup for LayerBlendMode without boxing.
        /// </summary>
        public static LayerBlendMode LayerBlendModePopup(Rect rect, LayerBlendMode current)
        {
            return (LayerBlendMode)EditorGUI.Popup(rect, (int)current, LayerBlendModeNames);
        }
        
        /// <summary>
        /// Draws a popup for LayerBlendMode without boxing (layout version).
        /// </summary>
        public static LayerBlendMode LayerBlendModePopup(string label, LayerBlendMode current)
        {
            return (LayerBlendMode)EditorGUILayout.Popup(label, (int)current, LayerBlendModeNames);
        }
        
        /// <summary>
        /// Draws a popup for BoolConditionComparison without boxing.
        /// </summary>
        public static BoolConditionComparison BoolConditionPopup(Rect rect, BoolConditionComparison current)
        {
            return (BoolConditionComparison)EditorGUI.Popup(rect, (int)current, BoolConditionNames);
        }
        
        /// <summary>
        /// Draws a popup for IntConditionComparison without boxing.
        /// Uses operator symbols for compact display.
        /// </summary>
        public static IntConditionComparison IntConditionPopup(Rect rect, IntConditionComparison current)
        {
            return (IntConditionComparison)EditorGUI.Popup(rect, (int)current, IntConditionNames);
        }
        
        /// <summary>
        /// Generic popup for any enum type. Caches names on first use.
        /// Falls back to Enum.GetNames which allocates on first call only.
        /// </summary>
        public static T Popup<T>(Rect rect, T current) where T : Enum
        {
            var type = typeof(T);
            if (!_enumNames.TryGetValue(type, out var names))
            {
                names = Enum.GetNames(type);
                _enumNames[type] = names;
            }
            
            var index = Convert.ToInt32(current);
            var newIndex = EditorGUI.Popup(rect, index, names);
            return (T)Enum.ToObject(type, newIndex);
        }
        
        /// <summary>
        /// Generic popup for any enum type (layout version).
        /// </summary>
        public static T Popup<T>(string label, T current) where T : Enum
        {
            var type = typeof(T);
            if (!_enumNames.TryGetValue(type, out var names))
            {
                names = Enum.GetNames(type);
                _enumNames[type] = names;
            }
            
            var index = Convert.ToInt32(current);
            var newIndex = EditorGUILayout.Popup(label, index, names);
            return (T)Enum.ToObject(type, newIndex);
        }
    }
}
