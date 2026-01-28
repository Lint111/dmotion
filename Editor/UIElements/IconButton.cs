using System;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// A compact icon button for use in toolbars and headers.
    /// Provides predefined icons for common actions.
    /// Styles defined in StateInspector.uss (.icon-button class).
    /// </summary>
    internal class IconButton : Button
    {
        #region USS Class Names

        public const string UssClassName = "icon-button";
        public const string UssSmallClassName = "icon-button--small";
        public const string UssLargeClassName = "icon-button--large";
        public const string UssClearClassName = "icon-button--clear";
        public const string UssNavigateClassName = "icon-button--navigate";
        public const string UssAddClassName = "icon-button--add";
        public const string UssRemoveClassName = "icon-button--remove";
        public const string UssMenuClassName = "icon-button--menu";
        public const string UssSettingsClassName = "icon-button--settings";

        #endregion

        #region Icon Constants

        /// <summary>Unicode X mark (✕)</summary>
        public const string IconClear = "\u2715";

        /// <summary>Unicode right arrow (→)</summary>
        public const string IconNavigate = "\u2192";

        /// <summary>Unicode plus (+)</summary>
        public const string IconAdd = "\u002B";

        /// <summary>Unicode minus (−)</summary>
        public const string IconRemove = "\u2212";

        /// <summary>Unicode refresh (⟳)</summary>
        public const string IconRefresh = "\u27F3";

        /// <summary>Unicode expand (▼)</summary>
        public const string IconExpand = "\u25BC";

        /// <summary>Unicode collapse (▶)</summary>
        public const string IconCollapse = "\u25B6";

        /// <summary>Unicode menu (☰)</summary>
        public const string IconMenu = "\u2630";

        /// <summary>Unicode settings/gear (⚙)</summary>
        public const string IconSettings = "\u2699";

        /// <summary>Unicode play (▶)</summary>
        public const string IconPlay = "\u25B6";

        /// <summary>Unicode pause (⏸)</summary>
        public const string IconPause = "\u23F8";

        /// <summary>Unicode stop (■)</summary>
        public const string IconStop = "\u25A0";

        #endregion

        #region Size Presets

        public enum Size
        {
            Small,
            Standard,
            Large
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an icon button with the specified icon and action.
        /// </summary>
        /// <param name="icon">The icon character to display.</param>
        /// <param name="tooltip">Tooltip text shown on hover.</param>
        /// <param name="onClick">Action to invoke when clicked.</param>
        /// <param name="size">Button size (default: Standard).</param>
        public IconButton(string icon, string tooltip, Action onClick, Size size = Size.Standard)
            : base(onClick)
        {
            text = icon;
            this.tooltip = tooltip;
            ApplyClasses(size);
        }

        /// <summary>
        /// Creates an icon button without a click handler (for deferred binding).
        /// </summary>
        /// <param name="icon">The icon character to display.</param>
        /// <param name="tooltip">Tooltip text shown on hover.</param>
        /// <param name="size">Button size (default: Standard).</param>
        public IconButton(string icon, string tooltip, Size size = Size.Standard)
            : base()
        {
            text = icon;
            this.tooltip = tooltip;
            ApplyClasses(size);
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a clear/close button (X icon).
        /// </summary>
        public static IconButton CreateClearButton(string tooltip, Action onClick, Size size = Size.Standard)
        {
            var button = new IconButton(IconClear, tooltip, onClick, size);
            button.AddToClassList(UssClearClassName);
            return button;
        }

        /// <summary>
        /// Creates a navigate button (→ icon).
        /// </summary>
        public static IconButton CreateNavigateButton(string tooltip, Action onClick, Size size = Size.Standard)
        {
            var button = new IconButton(IconNavigate, tooltip, onClick, size);
            button.AddToClassList(UssNavigateClassName);
            return button;
        }

        /// <summary>
        /// Creates an add button (+ icon).
        /// </summary>
        public static IconButton CreateAddButton(string tooltip, Action onClick, Size size = Size.Standard)
        {
            var button = new IconButton(IconAdd, tooltip, onClick, size);
            button.AddToClassList(UssAddClassName);
            return button;
        }

        /// <summary>
        /// Creates a remove button (− icon).
        /// </summary>
        public static IconButton CreateRemoveButton(string tooltip, Action onClick, Size size = Size.Standard)
        {
            var button = new IconButton(IconRemove, tooltip, onClick, size);
            button.AddToClassList(UssRemoveClassName);
            return button;
        }

        /// <summary>
        /// Creates a menu button (☰ icon).
        /// </summary>
        public static IconButton CreateMenuButton(string tooltip, Action onClick, Size size = Size.Standard)
        {
            var button = new IconButton(IconMenu, tooltip, onClick, size);
            button.AddToClassList(UssMenuClassName);
            return button;
        }

        /// <summary>
        /// Creates a settings button (⚙ icon).
        /// </summary>
        public static IconButton CreateSettingsButton(string tooltip, Action onClick, Size size = Size.Standard)
        {
            var button = new IconButton(IconSettings, tooltip, onClick, size);
            button.AddToClassList(UssSettingsClassName);
            return button;
        }

        #endregion

        #region Styling

        private void ApplyClasses(Size size)
        {
            AddToClassList(UssClassName);

            switch (size)
            {
                case Size.Small:
                    AddToClassList(UssSmallClassName);
                    break;
                case Size.Large:
                    AddToClassList(UssLargeClassName);
                    break;
                // Standard uses base .icon-button class only
            }
        }

        /// <summary>
        /// Configures the button to stop click propagation (useful when inside foldouts/headers).
        /// </summary>
        public IconButton StopClickPropagation()
        {
            RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            return this;
        }

        /// <summary>
        /// Sets the button visibility based on a condition.
        /// </summary>
        public IconButton SetVisible(bool visible)
        {
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            return this;
        }

        #endregion
    }
}
