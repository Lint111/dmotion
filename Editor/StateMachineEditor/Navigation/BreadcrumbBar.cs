using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Navigation breadcrumb bar showing the current path through nested state machines.
    /// Example: Root > Combat > Attack > Combo
    /// </summary>
    [UxmlElement]
    internal partial class BreadcrumbBar : VisualElement
    {
        private const string UssPath = "Packages/com.gamedevpro.dmotion/Editor/StateMachineEditor/Navigation/BreadcrumbBar.uss";
        
        /// <summary>
        /// Fired when user clicks a breadcrumb to navigate to that level.
        /// Parameter is the index in the navigation stack (0 = root).
        /// </summary>
        internal Action<int> OnNavigate;

        private readonly List<StateMachineAsset> navigationStack = new();
        private readonly VisualElement container;
        
        // SessionState key for persistence across domain reloads
        private const string NavigationStackSessionKey = "DMotion.BreadcrumbBar.NavigationStack";

        public BreadcrumbBar()
        {
            // Load stylesheet
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null)
                styleSheets.Add(uss);
            
            AddToClassList("breadcrumb-bar");

            container = new VisualElement();
            container.AddToClassList("breadcrumb-container");
            Add(container);
        }

        /// <summary>
        /// Gets the current navigation stack.
        /// </summary>
        internal IReadOnlyList<StateMachineAsset> NavigationStack => navigationStack;

        /// <summary>
        /// Gets the currently displayed state machine (top of stack).
        /// </summary>
        internal StateMachineAsset CurrentStateMachine => 
            navigationStack.Count > 0 ? navigationStack[navigationStack.Count - 1] : null;

        /// <summary>
        /// Sets the root state machine and clears navigation history.
        /// </summary>
        internal void SetRoot(StateMachineAsset root)
        {
            navigationStack.Clear();
            if (root != null)
            {
                navigationStack.Add(root);
            }
            Rebuild();
        }

        /// <summary>
        /// Pushes a nested state machine onto the navigation stack.
        /// </summary>
        internal void Push(StateMachineAsset stateMachine)
        {
            if (stateMachine == null) return;
            
            // Avoid duplicates at the top
            if (navigationStack.Count > 0 && navigationStack[navigationStack.Count - 1] == stateMachine)
                return;
                
            navigationStack.Add(stateMachine);
            Rebuild();
        }

        /// <summary>
        /// Navigates to a specific index in the stack, removing everything after it.
        /// </summary>
        internal void NavigateTo(int index)
        {
            if (index < 0 || index >= navigationStack.Count) return;
            
            // Remove everything after the target index
            while (navigationStack.Count > index + 1)
            {
                navigationStack.RemoveAt(navigationStack.Count - 1);
            }
            
            Rebuild();
            OnNavigate?.Invoke(index);
        }

        /// <summary>
        /// Navigates back one level. Returns false if already at root.
        /// </summary>
        internal bool NavigateBack()
        {
            if (navigationStack.Count <= 1) return false;
            
            navigationStack.RemoveAt(navigationStack.Count - 1);
            Rebuild();
            OnNavigate?.Invoke(navigationStack.Count - 1);
            return true;
        }

        private void Rebuild()
        {
            container.Clear();

            for (int i = 0; i < navigationStack.Count; i++)
            {
                var stateMachine = navigationStack[i];
                var isLast = i == navigationStack.Count - 1;
                var index = i;

                // Separator (except for first item)
                if (i > 0)
                {
                    var separator = new Label(">");
                    separator.AddToClassList("breadcrumb-separator");
                    container.Add(separator);
                }

                // Breadcrumb button/label
                var crumb = new Label(stateMachine.name);
                crumb.AddToClassList("breadcrumb-item");
                
                if (isLast)
                {
                    crumb.AddToClassList("breadcrumb-item--current");
                }
                else
                {
                    // Make clickable with hover effect
                    crumb.AddToClassList("breadcrumb-item--link");
                    crumb.pickingMode = PickingMode.Position;
                    crumb.RegisterCallback<ClickEvent>(evt => NavigateTo(index));
                    // Note: :hover pseudo-class in USS handles the hover styling
                }

                container.Add(crumb);
            }

            // Show placeholder if empty
            if (navigationStack.Count == 0)
            {
                var placeholder = new Label("No state machine selected");
                placeholder.AddToClassList("breadcrumb-placeholder");
                container.Add(placeholder);
            }
            
            // Save state after any navigation change
            SaveNavigationState();
        }
        
        #region Persistence
        
        /// <summary>
        /// Saves the navigation stack to SessionState for domain reload persistence.
        /// </summary>
        private void SaveNavigationState()
        {
            if (navigationStack.Count == 0)
            {
                SessionState.EraseString(NavigationStackSessionKey);
                return;
            }
            
            // Convert to GUIDs for persistence
            var guids = new List<string>();
            foreach (var machine in navigationStack)
            {
                if (machine == null) continue;
                var path = AssetDatabase.GetAssetPath(machine);
                if (!string.IsNullOrEmpty(path))
                {
                    guids.Add(AssetDatabase.AssetPathToGUID(path));
                }
            }
            
            // Store as comma-separated GUIDs
            SessionState.SetString(NavigationStackSessionKey, string.Join(",", guids));
        }
        
        /// <summary>
        /// Restores the navigation stack from SessionState after domain reload.
        /// Call this after the BreadcrumbBar is created.
        /// </summary>
        internal void RestoreNavigationState()
        {
            var savedGuids = SessionState.GetString(NavigationStackSessionKey, string.Empty);
            if (string.IsNullOrEmpty(savedGuids)) return;
            
            var guids = savedGuids.Split(',');
            navigationStack.Clear();
            
            foreach (var guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var machine = AssetDatabase.LoadAssetAtPath<StateMachineAsset>(path);
                if (machine != null)
                {
                    navigationStack.Add(machine);
                }
            }
            
            Rebuild();
        }
        
        /// <summary>
        /// Clears the persisted navigation state.
        /// </summary>
        internal void ClearNavigationState()
        {
            SessionState.EraseString(NavigationStackSessionKey);
        }
        
        #endregion
    }
}
