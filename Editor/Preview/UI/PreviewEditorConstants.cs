namespace DMotion.Editor
{
    /// <summary>
    /// Shared layout constants for preview editor windows.
    /// Only includes values that are truly shared across multiple files.
    /// Context-specific constants should remain in their respective classes.
    /// </summary>
    internal static class PreviewEditorConstants
    {
        #region UI Element Sizes
        
        /// <summary>Width of float input fields in property rows.</summary>
        public const float FloatFieldWidth = 50f;
        
        /// <summary>Standard label width in property rows.</summary>
        public const float PropertyLabelWidth = 100f;
        
        #endregion
        
        #region Spacing
        
        /// <summary>Small spacing between elements (2px).</summary>
        public const float SpacingSmall = 2f;
        
        /// <summary>Medium spacing between elements (4px).</summary>
        public const float SpacingMedium = 4f;
        
        /// <summary>Large spacing between elements (8px).</summary>
        public const float SpacingLarge = 8f;
        
        #endregion
        
        #region Handle Sizes
        
        /// <summary>Default handle/grip size for draggable elements.</summary>
        public const float HandleSize = 10f;
        
        #endregion
    }
}
