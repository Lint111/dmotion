using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Centralized cache for IMGUI GUIStyles used across the editor.
    /// Styles are lazily initialized on first access to avoid issues with GUI.skin being null.
    /// 
    /// Note: This is for IMGUI styles (GUIStyle). For UIElements styling, use USS files instead.
    /// </summary>
    internal static class EditorStyleCache
    {
        #region Icon Styles
        
        private static GUIStyle _centeredIcon;
        /// <summary>
        /// Style for icons that should be centered both horizontally and vertically.
        /// Use with GUILayout.Label(icon, CenteredIcon, GUILayout.Width(18), GUILayout.Height(18))
        /// </summary>
        public static GUIStyle CenteredIcon => _centeredIcon ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };
        
        #endregion
        
        #region Index/Badge Styles
        
        private static GUIStyle _indexNormal;
        /// <summary>
        /// Style for index badges (e.g., layer indices). Normal weight, centered.
        /// </summary>
        public static GUIStyle IndexNormal => _indexNormal ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };
        
        private static GUIStyle _indexBold;
        /// <summary>
        /// Style for emphasized index badges (e.g., base layer). Bold, centered.
        /// </summary>
        public static GUIStyle IndexBold => _indexBold ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        
        #endregion
        
        #region Label Styles
        
        private static GUIStyle _miniLabelRight;
        /// <summary>
        /// Mini label aligned to the right. Useful for status text in headers.
        /// </summary>
        public static GUIStyle MiniLabelRight => _miniLabelRight ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight
        };
        
        private static GUIStyle _miniLabelCenter;
        /// <summary>
        /// Mini label centered. Useful for status indicators.
        /// </summary>
        public static GUIStyle MiniLabelCenter => _miniLabelCenter ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };
        
        #endregion
        
        #region Header Styles
        
        private static GUIStyle _sectionHeader;
        /// <summary>
        /// Style for section headers. Bold with slight padding.
        /// </summary>
        public static GUIStyle SectionHeader => _sectionHeader ??= new GUIStyle(EditorStyles.boldLabel)
        {
            padding = new RectOffset(2, 2, 4, 4)
        };
        
        #endregion
        
        /// <summary>
        /// Clears all cached styles. Call this if styles need to be recreated
        /// (e.g., after skin changes).
        /// </summary>
        public static void ClearCache()
        {
            _centeredIcon = null;
            _indexNormal = null;
            _indexBold = null;
            _miniLabelRight = null;
            _miniLabelCenter = null;
            _sectionHeader = null;
        }
    }
}
