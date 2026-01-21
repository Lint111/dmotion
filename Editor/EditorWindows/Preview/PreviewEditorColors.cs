using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Shared color constants for preview editor windows.
    /// Consolidates color definitions to ensure visual consistency and reduce duplication.
    /// </summary>
    internal static class PreviewEditorColors
    {
        #region Backgrounds
        
        /// <summary>Dark background for editor areas (0.15, 0.15, 0.15).</summary>
        public static readonly Color DarkBackground = new Color(0.15f, 0.15f, 0.15f);
        
        /// <summary>Slightly lighter background (0.16, 0.16, 0.16).</summary>
        public static readonly Color FooterBackground = new Color(0.16f, 0.16f, 0.16f);
        
        /// <summary>Medium background (0.18, 0.18, 0.18).</summary>
        public static readonly Color MediumBackground = new Color(0.18f, 0.18f, 0.18f);
        
        #endregion
        
        #region Text
        
        /// <summary>Dim/secondary text color (0.6, 0.6, 0.6).</summary>
        public static readonly Color DimText = new Color(0.6f, 0.6f, 0.6f);
        
        /// <summary>Slightly lighter dim text (0.5, 0.5, 0.5).</summary>
        public static readonly Color LightDimText = new Color(0.5f, 0.5f, 0.5f);
        
        /// <summary>White text with transparency (1, 1, 1, 0.7).</summary>
        public static readonly Color TransparentWhiteText = new Color(1f, 1f, 1f, 0.7f);
        
        /// <summary>Full white text (0.9, 0.9, 0.9).</summary>
        public static readonly Color WhiteText = new Color(0.9f, 0.9f, 0.9f);
        
        #endregion
        
        #region Borders and Grid
        
        /// <summary>Border color for sections (0.3, 0.3, 0.3).</summary>
        public static readonly Color Border = new Color(0.3f, 0.3f, 0.3f);
        
        /// <summary>Grid line color with transparency (0.3, 0.3, 0.3, 0.5).</summary>
        public static readonly Color Grid = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        
        /// <summary>Medium gray for borders and outlines (0.4, 0.4, 0.4).</summary>
        public static readonly Color MediumBorder = new Color(0.4f, 0.4f, 0.4f);
        
        /// <summary>Subtle grid lines (0.5, 0.5, 0.5, 0.5).</summary>
        public static readonly Color SubtleGrid = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        
        #endregion
        
        #region Accents
        
        /// <summary>Curve/accent color - yellow/orange (1, 0.8, 0.2).</summary>
        public static readonly Color CurveAccent = new Color(1f, 0.8f, 0.2f);
        
        /// <summary>Selection/highlight color - orange (1, 0.5, 0.2).</summary>
        public static readonly Color SelectionHighlight = new Color(1f, 0.5f, 0.2f);
        
        /// <summary>Keyframe color - blue (0.2, 0.6, 1).</summary>
        public static readonly Color Keyframe = new Color(0.2f, 0.6f, 1f);
        
        /// <summary>Tangent handle color - gray (0.6, 0.6, 0.6).</summary>
        public static readonly Color Tangent = new Color(0.6f, 0.6f, 0.6f);
        
        /// <summary>Scrubber/playhead color - orange (1, 0.5, 0).</summary>
        public static readonly Color Scrubber = new Color(1f, 0.5f, 0f);
        
        #endregion
        
        #region State Colors
        
        /// <summary>From state color - blue (0.25, 0.45, 0.75).</summary>
        public static readonly Color FromState = new Color(0.25f, 0.45f, 0.75f);
        
        /// <summary>To state color - green (0.25, 0.65, 0.45).</summary>
        public static readonly Color ToState = new Color(0.25f, 0.65f, 0.45f);
        
        /// <summary>From state highlight - lighter blue (0.35, 0.55, 0.85).</summary>
        public static readonly Color FromStateHighlight = new Color(0.35f, 0.55f, 0.85f);
        
        /// <summary>To state highlight - lighter green (0.35, 0.75, 0.55).</summary>
        public static readonly Color ToStateHighlight = new Color(0.35f, 0.75f, 0.55f);
        
        #endregion
    }
}
