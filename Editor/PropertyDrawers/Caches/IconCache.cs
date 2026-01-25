using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Caches EditorGUIUtility.IconContent results to avoid per-frame allocations.
    /// Icons are lazily loaded on first access.
    /// </summary>
    internal static class IconCache
    {
        // Warning/Info icons
        private static GUIContent _warnIcon;
        private static GUIContent _infoIcon;
        private static GUIContent _errorIcon;

        // Status icons
        private static GUIContent _linkedIcon;
        private static GUIContent _validIcon;
        private static GUIContent _checkmarkIcon;
        
        // Asset type icons
        private static GUIContent _prefabIcon;
        private static GUIContent _scriptableObjectIcon;
        private static GUIContent _filterByTypeIcon;
        private static GUIContent _toggleIcon;
        private static GUIContent _gridIcon;
        private static GUIContent _blendTreeIcon;

        // Warning icon (yellow triangle)
        public static GUIContent WarnIcon => _warnIcon ??= EditorGUIUtility.IconContent("console.warnicon.sml");
        
        // Info icon (blue i)
        public static GUIContent InfoIcon => _infoIcon ??= EditorGUIUtility.IconContent("console.infoicon.sml");
        
        // Error icon (red x)
        public static GUIContent ErrorIcon => _errorIcon ??= EditorGUIUtility.IconContent("console.erroricon.sml");

        // Link icon (chain links)
        public static GUIContent LinkedIcon => _linkedIcon ??= EditorGUIUtility.IconContent("d_Linked");
        
        // Valid/checkmark icon
        public static GUIContent ValidIcon => _validIcon ??= EditorGUIUtility.IconContent("d_Valid");
        
        // Green checkmark
        public static GUIContent CheckmarkIcon => _checkmarkIcon ??= EditorGUIUtility.IconContent("d_GreenCheckmark");

        // Prefab icon
        public static GUIContent PrefabIcon => _prefabIcon ??= EditorGUIUtility.IconContent("d_Prefab Icon");
        
        // ScriptableObject icon
        public static GUIContent ScriptableObjectIcon => _scriptableObjectIcon ??= EditorGUIUtility.IconContent("d_ScriptableObject Icon");
        
        // Filter by type icon (for enum)
        public static GUIContent FilterByTypeIcon => _filterByTypeIcon ??= EditorGUIUtility.IconContent("d_FilterByType");
        
        // Toggle icon (for bool)
        public static GUIContent ToggleIcon => _toggleIcon ??= EditorGUIUtility.IconContent("d_Toggle Icon");
        
        // Grid icon (for int)
        public static GUIContent GridIcon => _gridIcon ??= EditorGUIUtility.IconContent("d_Grid.Default");
        
        // Blend tree icon (for float)
        public static GUIContent BlendTreeIcon => _blendTreeIcon ??= EditorGUIUtility.IconContent("d_BlendTree Icon");

        // Textures only (for when you need just the image)
        public static Texture WarnTexture => WarnIcon.image;
        public static Texture LinkedTexture => LinkedIcon.image;
        public static Texture ValidTexture => ValidIcon.image;
        public static Texture CheckmarkTexture => CheckmarkIcon.image;
        public static Texture PrefabTexture => PrefabIcon.image;
        public static Texture FilterByTypeTexture => FilterByTypeIcon.image;
        public static Texture ToggleTexture => ToggleIcon.image;
        public static Texture GridTexture => GridIcon.image;
        public static Texture BlendTreeTexture => BlendTreeIcon.image;
        public static Texture ScriptableObjectTexture => ScriptableObjectIcon.image;

        // Reusable GUIContent with custom tooltip (cycles through pool)
        private static readonly GUIContent _tempIcon1 = new GUIContent();
        private static readonly GUIContent _tempIcon2 = new GUIContent();
        private static int _tempIndex;

        /// <summary>
        /// Gets a temporary GUIContent with the specified icon and tooltip.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent TempIcon(Texture image, string tooltip = null)
        {
            _tempIndex = (_tempIndex + 1) % 2;
            var temp = _tempIndex == 0 ? _tempIcon1 : _tempIcon2;
            temp.image = image;
            temp.tooltip = tooltip;
            temp.text = null;
            return temp;
        }

        /// <summary>
        /// Gets warn icon with custom tooltip.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent WarnIconWithTooltip(string tooltip)
        {
            return TempIcon(WarnTexture, tooltip);
        }

        /// <summary>
        /// Gets linked icon with custom tooltip.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent LinkedIconWithTooltip(string tooltip)
        {
            return TempIcon(LinkedTexture, tooltip);
        }

        /// <summary>
        /// Gets valid icon with custom tooltip.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent ValidIconWithTooltip(string tooltip)
        {
            return TempIcon(ValidTexture, tooltip);
        }
    }
}
