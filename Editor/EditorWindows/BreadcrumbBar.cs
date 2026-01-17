using System;
using System.Collections.Generic;
using DMotion.Authoring;
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
        /// <summary>
        /// Fired when user clicks a breadcrumb to navigate to that level.
        /// Parameter is the index in the navigation stack (0 = root).
        /// </summary>
        internal Action<int> OnNavigate;

        private readonly List<StateMachineAsset> navigationStack = new();
        private readonly VisualElement container;

        public BreadcrumbBar()
        {
            // Main container styling
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.paddingLeft = 8;
            style.paddingRight = 8;
            style.paddingTop = 4;
            style.paddingBottom = 4;
            style.backgroundColor = new StyleColor(new UnityEngine.Color(0.22f, 0.22f, 0.22f, 1f));
            style.minHeight = 24;
            style.borderBottomWidth = 1;
            style.borderBottomColor = new StyleColor(new UnityEngine.Color(0.1f, 0.1f, 0.1f, 1f));

            container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.flexWrap = Wrap.Wrap;
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
                    separator.style.color = new StyleColor(new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f));
                    separator.style.marginLeft = 6;
                    separator.style.marginRight = 6;
                    separator.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Normal;
                    container.Add(separator);
                }

                // Breadcrumb button/label
                var crumb = new Label(stateMachine.name);
                crumb.style.unityFontStyleAndWeight = isLast 
                    ? UnityEngine.FontStyle.Bold 
                    : UnityEngine.FontStyle.Normal;
                crumb.style.color = isLast
                    ? new StyleColor(new UnityEngine.Color(1f, 1f, 1f, 1f))
                    : new StyleColor(new UnityEngine.Color(0.6f, 0.8f, 1f, 1f));
                
                if (!isLast)
                {
                    // Make clickable - add underline on hover to indicate interactivity
                    crumb.pickingMode = PickingMode.Position;
                    crumb.RegisterCallback<ClickEvent>(evt => NavigateTo(index));
                    crumb.RegisterCallback<MouseEnterEvent>(evt =>
                    {
                        crumb.style.color = new StyleColor(new UnityEngine.Color(0.8f, 0.9f, 1f, 1f));
                        crumb.style.unityFontStyleAndWeight = UnityEngine.FontStyle.BoldAndItalic;
                    });
                    crumb.RegisterCallback<MouseLeaveEvent>(evt =>
                    {
                        crumb.style.color = new StyleColor(new UnityEngine.Color(0.6f, 0.8f, 1f, 1f));
                        crumb.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Normal;
                    });
                }

                container.Add(crumb);
            }

            // Show placeholder if empty
            if (navigationStack.Count == 0)
            {
                var placeholder = new Label("No state machine selected");
                placeholder.style.color = new StyleColor(new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f));
                placeholder.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;
                container.Add(placeholder);
            }
        }
    }
}
