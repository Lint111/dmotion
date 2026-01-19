using System;
using DMotion.Authoring;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Centralized event system for Animation Preview.
    /// Provides a public API for extensions and tools to react to preview state changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture:</b> Preview components raise events through this system,
    /// allowing other windows and tools to react to preview state changes without direct coupling.</para>
    /// 
    /// <para><b>Event Categories:</b></para>
    /// <list type="bullet">
    ///   <item><b>Preview State Events</b> - Time, blend position, clip selection changes</item>
    ///   <item><b>Playback Events</b> - Play/pause state changes</item>
    ///   <item><b>Mode Events</b> - Edit/preview mode changes</item>
    /// </list>
    /// 
    /// <para><b>Usage Example:</b></para>
    /// <code>
    /// public class MyPreviewExtension
    /// {
    ///     public void Initialize()
    ///     {
    ///         AnimationPreviewEvents.OnPreviewTimeChanged += OnTimeChanged;
    ///         AnimationPreviewEvents.OnBlendPositionChanged += OnBlendChanged;
    ///     }
    ///     
    ///     public void Cleanup()
    ///     {
    ///         AnimationPreviewEvents.OnPreviewTimeChanged -= OnTimeChanged;
    ///         AnimationPreviewEvents.OnBlendPositionChanged -= OnBlendChanged;
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public static class AnimationPreviewEvents
    {
        #region Preview Time Events

        /// <summary>
        /// Fired when the preview time changes (from scrubbing or playback).
        /// Args: (state, normalizedTime)
        /// </summary>
        public static event Action<AnimationStateAsset, float> OnPreviewTimeChanged;

        /// <summary>
        /// Fired when preview playback state changes.
        /// Args: (isPlaying)
        /// </summary>
        public static event Action<bool> OnPlayStateChanged;

        /// <summary>
        /// Fired when looping state changes.
        /// Args: (isLooping)
        /// </summary>
        public static event Action<bool> OnLoopStateChanged;

        #endregion

        #region Blend Space Events

        /// <summary>
        /// Fired when the 1D blend position changes.
        /// Args: (state, blendValue)
        /// </summary>
        public static event Action<LinearBlendStateAsset, float> OnBlendPosition1DChanged;

        /// <summary>
        /// Fired when the 2D blend position changes.
        /// Args: (state, blendPosition)
        /// </summary>
        public static event Action<Directional2DBlendStateAsset, Vector2> OnBlendPosition2DChanged;

        /// <summary>
        /// Fired when a specific clip is selected for individual preview in a blend state.
        /// Args: (state, clipIndex) - clipIndex is -1 for blended preview
        /// </summary>
        public static event Action<AnimationStateAsset, int> OnClipSelectedForPreview;

        #endregion

        #region Mode Events

        /// <summary>
        /// Fired when edit/preview mode changes in a blend space editor.
        /// Args: (state, isEditMode)
        /// </summary>
        public static event Action<AnimationStateAsset, bool> OnBlendSpaceEditModeChanged;

        #endregion

        #region Preview Lifecycle Events

        /// <summary>
        /// Fired when a preview is created for a state.
        /// Args: (state)
        /// </summary>
        public static event Action<AnimationStateAsset> OnPreviewCreated;

        /// <summary>
        /// Fired when a preview is disposed.
        /// Args: (state)
        /// </summary>
        public static event Action<AnimationStateAsset> OnPreviewDisposed;

        /// <summary>
        /// Fired when preview creation fails.
        /// Args: (state, errorMessage)
        /// </summary>
        public static event Action<AnimationStateAsset, string> OnPreviewError;

        #endregion

        #region Raise Methods - Preview Time

        /// <summary>Raises <see cref="OnPreviewTimeChanged"/>.</summary>
        public static void RaisePreviewTimeChanged(AnimationStateAsset state, float normalizedTime)
        {
            OnPreviewTimeChanged?.Invoke(state, normalizedTime);
        }

        /// <summary>Raises <see cref="OnPlayStateChanged"/>.</summary>
        public static void RaisePlayStateChanged(bool isPlaying)
        {
            OnPlayStateChanged?.Invoke(isPlaying);
        }

        /// <summary>Raises <see cref="OnLoopStateChanged"/>.</summary>
        public static void RaiseLoopStateChanged(bool isLooping)
        {
            OnLoopStateChanged?.Invoke(isLooping);
        }

        #endregion

        #region Raise Methods - Blend Space

        /// <summary>Raises <see cref="OnBlendPosition1DChanged"/>.</summary>
        public static void RaiseBlendPosition1DChanged(LinearBlendStateAsset state, float blendValue)
        {
            OnBlendPosition1DChanged?.Invoke(state, blendValue);
        }

        /// <summary>Raises <see cref="OnBlendPosition2DChanged"/>.</summary>
        public static void RaiseBlendPosition2DChanged(Directional2DBlendStateAsset state, Vector2 blendPosition)
        {
            OnBlendPosition2DChanged?.Invoke(state, blendPosition);
        }

        /// <summary>Raises <see cref="OnClipSelectedForPreview"/>.</summary>
        public static void RaiseClipSelectedForPreview(AnimationStateAsset state, int clipIndex)
        {
            OnClipSelectedForPreview?.Invoke(state, clipIndex);
        }

        #endregion

        #region Raise Methods - Mode

        /// <summary>Raises <see cref="OnBlendSpaceEditModeChanged"/>.</summary>
        public static void RaiseBlendSpaceEditModeChanged(AnimationStateAsset state, bool isEditMode)
        {
            OnBlendSpaceEditModeChanged?.Invoke(state, isEditMode);
        }

        #endregion

        #region Raise Methods - Lifecycle

        /// <summary>Raises <see cref="OnPreviewCreated"/>.</summary>
        public static void RaisePreviewCreated(AnimationStateAsset state)
        {
            OnPreviewCreated?.Invoke(state);
        }

        /// <summary>Raises <see cref="OnPreviewDisposed"/>.</summary>
        public static void RaisePreviewDisposed(AnimationStateAsset state)
        {
            OnPreviewDisposed?.Invoke(state);
        }

        /// <summary>Raises <see cref="OnPreviewError"/>.</summary>
        public static void RaisePreviewError(AnimationStateAsset state, string errorMessage)
        {
            OnPreviewError?.Invoke(state, errorMessage);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Clears all event subscriptions. Only call when the preview window closes.
        /// </summary>
        /// <remarks>
        /// <b>Warning:</b> This is a safety net for window disposal. It will remove ALL subscribers,
        /// including any external tools that may have subscribed. External subscribers should 
        /// unsubscribe explicitly in their own cleanup code rather than relying on this method.
        /// </remarks>
        internal static void ClearAllSubscriptions()
        {
            // Log if there were active subscriptions (helps debug subscription leaks)
            var hadSubscriptions = OnPreviewTimeChanged != null || OnPlayStateChanged != null ||
                                   OnBlendPosition1DChanged != null || OnBlendPosition2DChanged != null ||
                                   OnPreviewCreated != null || OnPreviewDisposed != null;
            
            if (hadSubscriptions)
            {
                UnityEngine.Debug.Log("[AnimationPreviewEvents] Clearing all subscriptions on window close.");
            }
            
            OnPreviewTimeChanged = null;
            OnPlayStateChanged = null;
            OnLoopStateChanged = null;
            OnBlendPosition1DChanged = null;
            OnBlendPosition2DChanged = null;
            OnClipSelectedForPreview = null;
            OnBlendSpaceEditModeChanged = null;
            OnPreviewCreated = null;
            OnPreviewDisposed = null;
            OnPreviewError = null;
        }

        #endregion
    }
}
