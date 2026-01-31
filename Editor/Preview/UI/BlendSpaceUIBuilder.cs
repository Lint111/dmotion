using System;
using DMotion.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Configuration for blend space UI element creation.
    /// Use predefined configurations or create custom ones.
    /// </summary>
    public readonly struct BlendSpaceConfig
    {
        #region Properties
        
        /// <summary>Show the preview position indicator (orange dot).</summary>
        public readonly bool ShowPreviewIndicator;
        
        /// <summary>Enable edit mode for threshold/position editing.</summary>
        public readonly bool EditMode;
        
        /// <summary>Show the preview/edit mode toggle button.</summary>
        public readonly bool ShowModeToggle;
        
        /// <summary>CSS class to apply to the element.</summary>
        public readonly string CssClass1D;
        
        /// <summary>CSS class to apply to 2D elements.</summary>
        public readonly string CssClass2D;
        
        /// <summary>Element height for 1D blend space.</summary>
        public readonly float Height1D;
        
        /// <summary>Element height for 2D blend space.</summary>
        public readonly float Height2D;
        
        /// <summary>Restore persisted blend position from PreviewSettings.</summary>
        public readonly bool RestorePersistedPosition;
        
        #endregion
        
        #region Constructor
        
        public BlendSpaceConfig(
            bool showPreviewIndicator,
            bool editMode,
            bool showModeToggle,
            string cssClass1D,
            string cssClass2D,
            float height1D,
            float height2D,
            bool restorePersistedPosition)
        {
            ShowPreviewIndicator = showPreviewIndicator;
            EditMode = editMode;
            ShowModeToggle = showModeToggle;
            CssClass1D = cssClass1D;
            CssClass2D = cssClass2D;
            Height1D = height1D;
            Height2D = height2D;
            RestorePersistedPosition = restorePersistedPosition;
        }
        
        #endregion
        
        #region Predefined Configurations
        
        /// <summary>
        /// Configuration for preview mode (read-only blend position display).
        /// Used in Animation Preview window, transition preview, layer composition.
        /// </summary>
        public static BlendSpaceConfig Preview => new(
            showPreviewIndicator: true,
            editMode: false,
            showModeToggle: false,
            cssClass1D: "blend-space-1d-preview",
            cssClass2D: "blend-space-2d-preview",
            height1D: 80f,
            height2D: 150f,
            restorePersistedPosition: true
        );
        
        /// <summary>
        /// Configuration for edit mode (editable thresholds/positions).
        /// Used in State Machine Editor node inspector.
        /// </summary>
        public static BlendSpaceConfig Edit => new(
            showPreviewIndicator: false,
            editMode: true,
            showModeToggle: false,
            cssClass1D: "blend-space-inspector-1d",
            cssClass2D: "blend-space-inspector-2d",
            height1D: 80f,
            height2D: 150f,
            restorePersistedPosition: false
        );
        
        /// <summary>
        /// Configuration for hybrid mode (preview with edit toggle).
        /// Allows switching between preview and edit modes.
        /// </summary>
        public static BlendSpaceConfig Hybrid => new(
            showPreviewIndicator: true,
            editMode: false,
            showModeToggle: true,
            cssClass1D: "blend-space-1d-hybrid",
            cssClass2D: "blend-space-2d-hybrid",
            height1D: 80f,
            height2D: 150f,
            restorePersistedPosition: true
        );
        
        #endregion
        
        #region Helpers
        
        /// <summary>Gets the appropriate CSS class based on dimensionality.</summary>
        public string GetCssClass(bool is2D) => is2D ? CssClass2D : CssClass1D;
        
        /// <summary>Gets the appropriate height based on dimensionality.</summary>
        public float GetHeight(bool is2D) => is2D ? Height2D : Height1D;
        
        #endregion
    }
    
    /// <summary>
    /// Factory for creating and configuring blend space UI elements.
    /// Provides predefined configurations for common use cases.
    /// </summary>
    /// <example>
    /// // Preview mode (default)
    /// var result = BlendSpaceUIBuilder.CreateForPreview(state);
    /// 
    /// // Edit mode for inspector
    /// var result = BlendSpaceUIBuilder.CreateForEdit(state);
    /// 
    /// // Custom configuration
    /// var result = BlendSpaceUIBuilder.Create(state, myConfig);
    /// </example>
    internal static class BlendSpaceUIBuilder
    {
        #region Result Types
        
        /// <summary>
        /// Result of creating a blend space element with all necessary data.
        /// </summary>
        public readonly struct BlendSpaceResult
        {
            public readonly BlendSpaceVisualElement Element;
            public readonly BlendRange Range;
            public readonly Vector2 InitialPosition;
            public readonly bool Is2D;
            public readonly ParameterInfo Parameters;
            public readonly BlendSpaceConfig Config;
            
            public BlendSpaceResult(
                BlendSpaceVisualElement element, 
                BlendRange range, 
                Vector2 initialPosition, 
                bool is2D,
                ParameterInfo parameters,
                BlendSpaceConfig config)
            {
                Element = element;
                Range = range;
                InitialPosition = initialPosition;
                Is2D = is2D;
                Parameters = parameters;
                Config = config;
            }
            
            public bool IsValid => Element != null;
        }
        
        /// <summary>
        /// Blend parameter range for slider configuration.
        /// </summary>
        public readonly struct BlendRange
        {
            public readonly float MinX, MaxX;
            public readonly float MinY, MaxY;
            
            public BlendRange(float minX, float maxX, float minY = 0f, float maxY = 0f)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }
            
            public static BlendRange Default1D => new(0f, 1f);
            public static BlendRange Default2D => new(-1f, 1f, -1f, 1f);
        }
        
        /// <summary>
        /// Parameter information for blend states.
        /// </summary>
        public readonly struct ParameterInfo
        {
            public readonly string ParameterX;
            public readonly string ParameterY;
            
            public ParameterInfo(string parameterX, string parameterY = null)
            {
                ParameterX = parameterX;
                ParameterY = parameterY;
            }
            
            public bool HasY => ParameterY != null;
        }
        
        #endregion
        
        #region Factory Methods
        
        /// <summary>
        /// Creates a blend space element configured for preview mode.
        /// Shows preview indicator, read-only, restores persisted position.
        /// </summary>
        public static BlendSpaceResult CreateForPreview(AnimationStateAsset state, Action<Vector2> onPositionChanged = null)
        {
            return Create(state, BlendSpaceConfig.Preview, onPositionChanged);
        }
        
        /// <summary>
        /// Creates a blend space element configured for edit mode.
        /// Editable thresholds/positions, no preview indicator.
        /// </summary>
        public static BlendSpaceResult CreateForEdit(AnimationStateAsset state, Action<Vector2> onPositionChanged = null)
        {
            return Create(state, BlendSpaceConfig.Edit, onPositionChanged);
        }
        
        /// <summary>
        /// Creates a blend space element with hybrid mode (preview + edit toggle).
        /// </summary>
        public static BlendSpaceResult CreateHybrid(AnimationStateAsset state, Action<Vector2> onPositionChanged = null)
        {
            return Create(state, BlendSpaceConfig.Hybrid, onPositionChanged);
        }
        
        /// <summary>
        /// Creates a blend space element with the specified configuration.
        /// </summary>
        /// <param name="state">The animation state (must be LinearBlendStateAsset or Directional2DBlendStateAsset)</param>
        /// <param name="config">Configuration for the element</param>
        /// <param name="onPositionChanged">Callback when the preview position changes</param>
        public static BlendSpaceResult Create(AnimationStateAsset state, BlendSpaceConfig config, Action<Vector2> onPositionChanged = null)
        {
            if (state == null)
                return default;
            
            return state switch
            {
                LinearBlendStateAsset linear => CreateLinearBlend(linear, config, onPositionChanged),
                Directional2DBlendStateAsset blend2D => CreateDirectional2D(blend2D, config, onPositionChanged),
                _ => default
            };
        }
        
        /// <summary>
        /// Creates a blend space element with default preview configuration.
        /// Convenience overload for backward compatibility.
        /// </summary>
        public static BlendSpaceResult Create(AnimationStateAsset state, Action<Vector2> onPositionChanged = null)
        {
            return CreateForPreview(state, onPositionChanged);
        }
        
        #endregion
        
        #region Type-Specific Creation
        
        /// <summary>
        /// Creates a 1D blend space element for a LinearBlendStateAsset.
        /// </summary>
        public static BlendSpaceResult CreateLinearBlend(
            LinearBlendStateAsset state, 
            BlendSpaceConfig config,
            Action<Vector2> onPositionChanged = null)
        {
            if (state == null)
                return default;
            
            var element = new BlendSpace1DVisualElement();
            element.SetTarget(state);
            ConfigureElement(element, config, is2D: false);
            
            var range = CalculateRange1D(state);
            var initialPosition = config.RestorePersistedPosition 
                ? new Vector2(PreviewSettings.instance.GetBlendValue1D(state), 0f)
                : Vector2.zero;
            element.PreviewPosition = initialPosition;
            
            if (onPositionChanged != null)
                element.OnPreviewPositionChanged += onPositionChanged;
            
            var parameters = new ParameterInfo(state.BlendParameter?.name ?? "(none)");
            return new BlendSpaceResult(element, range, initialPosition, is2D: false, parameters, config);
        }
        
        /// <summary>
        /// Creates a 2D blend space element for a Directional2DBlendStateAsset.
        /// </summary>
        public static BlendSpaceResult CreateDirectional2D(
            Directional2DBlendStateAsset state, 
            BlendSpaceConfig config,
            Action<Vector2> onPositionChanged = null)
        {
            if (state == null)
                return default;
            
            var element = new BlendSpace2DVisualElement();
            element.SetTarget(state);
            ConfigureElement(element, config, is2D: true);
            
            var range = CalculateRange2D(state);
            var initialPosition = config.RestorePersistedPosition 
                ? PreviewSettings.instance.GetBlendValue2D(state)
                : Vector2.zero;
            element.PreviewPosition = initialPosition;
            
            if (onPositionChanged != null)
                element.OnPreviewPositionChanged += onPositionChanged;
            
            var parameters = new ParameterInfo(
                state.BlendParameterX?.name ?? "(none)",
                state.BlendParameterY?.name ?? "(none)");
            return new BlendSpaceResult(element, range, initialPosition, is2D: true, parameters, config);
        }
        
        #endregion
        
        #region Range Calculation
        
        /// <summary>
        /// Calculates the blend parameter range for a 1D blend state.
        /// </summary>
        public static BlendRange CalculateRange1D(LinearBlendStateAsset state)
        {
            if (state?.BlendClips == null || state.BlendClips.Length == 0)
                return BlendRange.Default1D;
            
            float min = float.MaxValue, max = float.MinValue;
            foreach (var clip in state.BlendClips)
            {
                min = Mathf.Min(min, clip.Threshold);
                max = Mathf.Max(max, clip.Threshold);
            }
            
            // Add 10% padding
            var range = max - min;
            if (range < 0.1f) range = 1f;
            min -= range * 0.1f;
            max += range * 0.1f;
            
            return new BlendRange(min, max);
        }
        
        /// <summary>
        /// Calculates the blend parameter range for a 2D blend state.
        /// </summary>
        public static BlendRange CalculateRange2D(Directional2DBlendStateAsset state)
        {
            if (state?.BlendClips == null || state.BlendClips.Length == 0)
                return BlendRange.Default2D;
            
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            
            foreach (var clip in state.BlendClips)
            {
                minX = Mathf.Min(minX, clip.Position.x);
                maxX = Mathf.Max(maxX, clip.Position.x);
                minY = Mathf.Min(minY, clip.Position.y);
                maxY = Mathf.Max(maxY, clip.Position.y);
            }
            
            // Add 10% padding
            var rangeX = maxX - minX;
            var rangeY = maxY - minY;
            if (rangeX < 0.1f) rangeX = 1f;
            if (rangeY < 0.1f) rangeY = 1f;
            minX -= rangeX * 0.1f;
            maxX += rangeX * 0.1f;
            minY -= rangeY * 0.1f;
            maxY += rangeY * 0.1f;
            
            return new BlendRange(minX, maxX, minY, maxY);
        }
        
        #endregion
        
        #region Utilities
        
        /// <summary>
        /// Updates a slider's range from a blend state.
        /// </summary>
        public static void UpdateSliderRange(Slider slider, AnimationStateAsset state)
        {
            if (slider == null) return;
            
            var range = state switch
            {
                LinearBlendStateAsset linear => CalculateRange1D(linear),
                Directional2DBlendStateAsset blend2D => CalculateRange2D(blend2D),
                _ => BlendRange.Default1D
            };
            
            slider.lowValue = range.MinX;
            slider.highValue = range.MaxX;
        }
        
        /// <summary>
        /// Checks if a state is a blend state (1D or 2D).
        /// </summary>
        public static bool IsBlendState(AnimationStateAsset state)
        {
            return AnimationStateUtils.IsBlendState(state);
        }
        
        #endregion
        
        #region Private Helpers
        
        private static void ConfigureElement(BlendSpaceVisualElement element, BlendSpaceConfig config, bool is2D)
        {
            element.ShowPreviewIndicator = config.ShowPreviewIndicator;
            element.EditMode = config.EditMode;
            element.ShowModeToggle = config.ShowModeToggle;
            element.AddToClassList(config.GetCssClass(is2D));
            element.style.height = config.GetHeight(is2D);
        }
        
        #endregion
    }
}
