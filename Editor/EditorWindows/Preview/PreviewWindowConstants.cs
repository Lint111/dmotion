using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Constants for the Animation Preview Window UI.
    /// Centralizes magic numbers for maintainability.
    /// </summary>
    internal static class PreviewWindowConstants
    {
        #region Layout Dimensions
        
        /// <summary>Height of the 1D blend space visualizer.</summary>
        public const float BlendSpace1DHeight = 120f;
        
        /// <summary>Height of the 2D blend space visualizer.</summary>
        public const float BlendSpace2DHeight = 180f;
        
        /// <summary>Width of float input fields.</summary>
        public const float FloatFieldWidth = 50f;
        
        /// <summary>Small spacing between elements (2px).</summary>
        public const float SpacingSmall = 2f;
        
        /// <summary>Medium spacing between elements (4px).</summary>
        public const float SpacingMedium = 4f;
        
        /// <summary>Large spacing between elements (8px).</summary>
        public const float SpacingLarge = 8f;
        
        #endregion
        
        #region Transition Properties
        
        /// <summary>Maximum transition duration for slider.</summary>
        public const float MaxTransitionDuration = 1f;
        
        #endregion
        
        #region Colors
        
        /// <summary>Background color for the 3D preview area.</summary>
        public static readonly Color PreviewBackground = new(0.15f, 0.15f, 0.15f);
        
        /// <summary>Text color for help/hint text.</summary>
        public static readonly Color HelpTextColor = new(0.6f, 0.6f, 0.6f);
        
        /// <summary>Text color for error/info messages.</summary>
        public static readonly Color MessageTextColor = new(0.7f, 0.7f, 0.7f);
        
        #endregion
        
        #region Placeholder Messages
        
        /// <summary>Message when blend state preview is not available.</summary>
        public const string BlendPreviewNotAvailable = "Blend state preview\nnot yet available";
        
        /// <summary>Message when 2D blend state preview is not available.</summary>
        public const string Blend2DPreviewNotAvailable = "2D Blend state preview\nnot yet available";
        
        /// <summary>Message when transition preview is not available.</summary>
        public const string TransitionPreviewNotAvailable = "Transition preview\nnot yet available";
        
        /// <summary>Placeholder for transition progress section.</summary>
        public const string TransitionProgressPlaceholder = "Transition preview not yet available";
        
        #endregion
    }
}
