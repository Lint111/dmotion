using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// A foldout that can be undocked into a separate window.
    /// UIToolkit equivalent of DockablePanelSection for IMGUI.
    /// </summary>
    internal class DockableFoldout : Foldout
    {
        private readonly string prefsKey;
        private bool isDocked = true;
        private Button dockButton;
        private VisualElement toolbarContainer;
        
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
        
        /// <summary>
        /// Creates a new DockableFoldout.
        /// </summary>
        /// <param name="title">The foldout title.</param>
        /// <param name="prefsKeyPrefix">Prefix for EditorPrefs keys (for persistence).</param>
        /// <param name="defaultExpanded">Whether the foldout is expanded by default.</param>
        /// <param name="showDockButton">Whether to show the dock/undock button.</param>
        public DockableFoldout(
            string title, 
            string prefsKeyPrefix, 
            bool defaultExpanded = true,
            bool showDockButton = true)
        {
            this.prefsKey = $"{prefsKeyPrefix}_{title.Replace(" ", "")}";
            
            text = title;
            value = EditorPrefs.GetBool($"{prefsKey}_Expanded", defaultExpanded);
            isDocked = EditorPrefs.GetBool($"{prefsKey}_Docked", true);
            
            AddToClassList("dockable-foldout");
            
            // Track expansion state changes
            this.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool($"{prefsKey}_Expanded", evt.newValue);
            });
            
            // Add dock button to toggle
            if (showDockButton)
            {
                // We need to insert into the toggle's hierarchy
                RegisterCallback<AttachToPanelEvent>(_ => SetupDockButton());
            }
        }
        
        private void SetupDockButton()
        {
            // Find the toggle element (first child of Foldout)
            var toggle = this.Q<Toggle>();
            if (toggle == null) return;

            // Create toolbar container for custom buttons

            toolbarContainer = new VisualElement();
            toolbarContainer.AddToClassList("dockable-foldout__toolbar");
            toolbarContainer.style.flexDirection = FlexDirection.Row;
            toolbarContainer.style.marginLeft = StyleKeyword.Auto;

            // Dock/Undock button

            dockButton = new Button(ToggleDocked);
            dockButton.AddToClassList("dockable-foldout__dock-button");
            UpdateDockButtonContent();
            toolbarContainer.Add(dockButton);

            // Insert into toggle's visual container

            var checkmark = toggle.Q(className: "unity-toggle__checkmark");
            
            if ((checkmark?.parent) == null) return;
            
            checkmark.parent.Add(toolbarContainer);
        }


        /// <summary>
        /// Adds a custom button to the foldout's toolbar.
        /// </summary>
        public Button AddToolbarButton(string text, string tooltip, Action onClick)
        {
            if (toolbarContainer != null)
            {
                var btn = CreateToolbarButton(text, tooltip, onClick);
                // Insert before dock button
                toolbarContainer.Insert(toolbarContainer.childCount - 1, btn);
                return btn;
            }

            // Create toolbar if not already done
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (toolbarContainer == null) return;

                var button = CreateToolbarButton(text, tooltip, onClick);
                // Insert before dock button
                toolbarContainer.Insert(toolbarContainer.childCount - 1, button);
            });

            Debug.LogWarning("Toolbar container not yet created; button will be added when attached to panel.");
            return null;
        }


        private Button CreateToolbarButton(string text, string tooltip, Action onClick)
        {
            var button = new Button(onClick)
            {
                text = text,
                tooltip = tooltip
            };
            button.AddToClassList("dockable-foldout__toolbar-button");
            return button;
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
                UpdateDockButtonContent();
                OnDock?.Invoke();
            }
        }
        
        /// <summary>
        /// Marks the section as undocked.
        /// </summary>
        public void SetUndocked()
        {
            if (!isDocked) return;

            isDocked = false;
            EditorPrefs.SetBool($"{prefsKey}_Docked", false);
            UpdateDockButtonContent();
        }


        private void ToggleDocked()
        {
            isDocked = !isDocked;
            EditorPrefs.SetBool($"{prefsKey}_Docked", isDocked);
            UpdateDockButtonContent();

            if(isDocked)
            {
                OnDock?.Invoke();
            }
            else
            {
                OnUndock?.Invoke(text);
            }
        }
        
        private void UpdateDockButtonContent()
        {
            if (dockButton == null) return;
            
            dockButton.text = isDocked ? "\u25F1" : "\u25F2"; // ◱ or ◲
            dockButton.tooltip = isDocked ? "Undock to separate window" : "Dock back to inspector";
        }
        
        /// <summary>
        /// Whether the content should be shown (expanded AND docked).
        /// </summary>
        public bool ShouldShowContent => value && isDocked;
    }
}
