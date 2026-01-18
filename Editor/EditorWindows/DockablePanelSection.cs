using System;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// A collapsible panel section that can optionally be undocked into its own window.
    /// </summary>
    internal class DockablePanelSection
    {
        private readonly string title;
        private readonly string prefsKey;
        private bool isExpanded;
        private bool isDocked = true;
        
        /// <summary>
        /// Whether the section is currently expanded (when docked).
        /// </summary>
        public bool IsExpanded => isExpanded;
        
        /// <summary>
        /// Whether the section is docked in the inspector or in its own window.
        /// </summary>
        public bool IsDocked => isDocked;
        
        /// <summary>
        /// Event fired when the section is undocked. Parameter is the title.
        /// </summary>
        public event Action<string> OnUndock;
        
        /// <summary>
        /// Event fired when the section is re-docked.
        /// </summary>
        public event Action OnDock;

        public DockablePanelSection(string title, string prefsKeyPrefix, bool defaultExpanded = true)
        {
            this.title = title;
            this.prefsKey = $"{prefsKeyPrefix}_{title.Replace(" ", "")}";
            this.isExpanded = EditorPrefs.GetBool($"{prefsKey}_Expanded", defaultExpanded);
            this.isDocked = EditorPrefs.GetBool($"{prefsKey}_Docked", true);
        }

        /// <summary>
        /// Draws the section header with foldout and dock/undock button.
        /// Returns true if the content should be drawn (expanded and docked).
        /// </summary>
        public bool DrawHeader(bool showDockButton = true)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Foldout
            var newExpanded = EditorGUILayout.Foldout(isExpanded, title, true);
            if (newExpanded != isExpanded)
            {
                isExpanded = newExpanded;
                EditorPrefs.SetBool($"{prefsKey}_Expanded", isExpanded);
            }
            
            GUILayout.FlexibleSpace();
            
            // Dock/Undock button
            if (showDockButton)
            {
                var buttonContent = isDocked 
                    ? new GUIContent("◱", "Undock to separate window") 
                    : new GUIContent("◲", "Dock back to inspector");
                    
                if (GUILayout.Button(buttonContent, EditorStyles.toolbarButton, GUILayout.Width(24)))
                {
                    ToggleDocked();
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            return isExpanded && isDocked;
        }

        /// <summary>
        /// Draws a header with additional toolbar buttons.
        /// </summary>
        public bool DrawHeader(Action drawToolbarButtons, bool showDockButton = true)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            var newExpanded = EditorGUILayout.Foldout(isExpanded, title, true);
            if (newExpanded != isExpanded)
            {
                isExpanded = newExpanded;
                EditorPrefs.SetBool($"{prefsKey}_Expanded", isExpanded);
            }
            
            GUILayout.FlexibleSpace();
            
            // Custom toolbar buttons
            drawToolbarButtons?.Invoke();
            
            // Dock/Undock button
            if (showDockButton)
            {
                var buttonContent = isDocked 
                    ? new GUIContent("◱", "Undock to separate window") 
                    : new GUIContent("◲", "Dock back to inspector");
                    
                if (GUILayout.Button(buttonContent, EditorStyles.toolbarButton, GUILayout.Width(24)))
                {
                    ToggleDocked();
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            return isExpanded && isDocked;
        }

        /// <summary>
        /// Marks the section as docked (called when window is closed).
        /// </summary>
        public void SetDocked()
        {
            if (!isDocked)
            {
                isDocked = true;
                EditorPrefs.SetBool($"{prefsKey}_Docked", true);
                OnDock?.Invoke();
            }
        }

        /// <summary>
        /// Marks the section as undocked.
        /// </summary>
        public void SetUndocked()
        {
            if (isDocked)
            {
                isDocked = false;
                EditorPrefs.SetBool($"{prefsKey}_Docked", false);
            }
        }

        private void ToggleDocked()
        {
            if (isDocked)
            {
                isDocked = false;
                EditorPrefs.SetBool($"{prefsKey}_Docked", false);
                OnUndock?.Invoke(title);
            }
            else
            {
                isDocked = true;
                EditorPrefs.SetBool($"{prefsKey}_Docked", true);
                OnDock?.Invoke();
            }
        }
    }
}
