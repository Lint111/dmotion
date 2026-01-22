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
        /// Fired when any blend state's position changes (unified event for 1D and 2D).
        /// Args: (state, blendPosition) - For 1D states, Y component is 0.
        /// </summary>
        /// <remarks>
        /// Subscribe to this event to react to blend position changes regardless of state type.
        /// Components can use this to update effective duration, weights, or other blend-dependent values.
        /// </remarks>
        public static event Action<AnimationStateAsset, Vector2> OnBlendStateChanged;

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

        #region Transition Events

        /// <summary>
        /// Fired when the transition preview progress changes.
        /// Args: (fromState, toState, progress) - progress is 0-1 where 0 = fully "from" state, 1 = fully "to" state
        /// </summary>
        public static event Action<AnimationStateAsset, AnimationStateAsset, float> OnTransitionProgressChanged;

        /// <summary>
        /// Fired when the transition preview animation time changes.
        /// Args: (fromState, toState, normalizedTime)
        /// </summary>
        public static event Action<AnimationStateAsset, AnimationStateAsset, float> OnTransitionTimeChanged;

        /// <summary>
        /// Fired when the blend position of the "from" state changes during transition preview.
        /// Args: (fromState, blendPosition) - X for 1D, X and Y for 2D
        /// </summary>
        public static event Action<AnimationStateAsset, Vector2> OnTransitionFromBlendPositionChanged;

        /// <summary>
        /// Fired when the blend position of the "to" state changes during transition preview.
        /// Args: (toState, blendPosition) - X for 1D, X and Y for 2D
        /// </summary>
        public static event Action<AnimationStateAsset, Vector2> OnTransitionToBlendPositionChanged;

        /// <summary>
        /// Fired when transition playback state changes.
        /// Args: (isPlaying)
        /// </summary>
        public static event Action<bool> OnTransitionPlayStateChanged;

        #endregion

        #region Mode Events

        /// <summary>
        /// Fired when edit/preview mode changes in a blend space editor.
        /// Args: (state, isEditMode)
        /// </summary>
        public static event Action<AnimationStateAsset, bool> OnBlendSpaceEditModeChanged;

        #endregion

        #region Navigation Events

        /// <summary>
        /// Fired when the user requests to navigate to a state preview.
        /// Args: (state)
        /// </summary>
        public static event Action<AnimationStateAsset> OnNavigateToState;

        /// <summary>
        /// Fired when the user requests to navigate to a transition preview.
        /// Args: (fromState, toState, isAnyState)
        /// </summary>
        public static event Action<AnimationStateAsset, AnimationStateAsset, bool> OnNavigateToTransition;

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

        /// <summary>Raises <see cref="OnBlendPosition1DChanged"/> and <see cref="OnBlendStateChanged"/>.</summary>
        public static void RaiseBlendPosition1DChanged(LinearBlendStateAsset state, float blendValue)
        {
            var blendPos = new Vector2(blendValue, 0);
            OnBlendStateChanged?.Invoke(state, blendPos);
            OnBlendPosition1DChanged?.Invoke(state, blendValue);
        }

        /// <summary>Raises <see cref="OnBlendPosition2DChanged"/> and <see cref="OnBlendStateChanged"/>.</summary>
        public static void RaiseBlendPosition2DChanged(Directional2DBlendStateAsset state, Vector2 blendPosition)
        {
            OnBlendStateChanged?.Invoke(state, blendPosition);
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

        #region Raise Methods - Transition

        /// <summary>Raises <see cref="OnTransitionProgressChanged"/>.</summary>
        public static void RaiseTransitionProgressChanged(AnimationStateAsset fromState, AnimationStateAsset toState, float progress)
        {
            OnTransitionProgressChanged?.Invoke(fromState, toState, progress);
        }

        /// <summary>Raises <see cref="OnTransitionTimeChanged"/>.</summary>
        public static void RaiseTransitionTimeChanged(AnimationStateAsset fromState, AnimationStateAsset toState, float normalizedTime)
        {
            OnTransitionTimeChanged?.Invoke(fromState, toState, normalizedTime);
        }

        /// <summary>Raises <see cref="OnTransitionFromBlendPositionChanged"/> and <see cref="OnBlendStateChanged"/>.</summary>
        public static void RaiseTransitionFromBlendPositionChanged(AnimationStateAsset fromState, Vector2 blendPosition)
        {
            // Also fire unified event so all blend-dependent UI updates
            OnBlendStateChanged?.Invoke(fromState, blendPosition);
            OnTransitionFromBlendPositionChanged?.Invoke(fromState, blendPosition);
        }

        /// <summary>Raises <see cref="OnTransitionToBlendPositionChanged"/> and <see cref="OnBlendStateChanged"/>.</summary>
        public static void RaiseTransitionToBlendPositionChanged(AnimationStateAsset toState, Vector2 blendPosition)
        {
            // Also fire unified event so all blend-dependent UI updates
            OnBlendStateChanged?.Invoke(toState, blendPosition);
            OnTransitionToBlendPositionChanged?.Invoke(toState, blendPosition);
        }

        /// <summary>Raises <see cref="OnTransitionPlayStateChanged"/>.</summary>
        public static void RaiseTransitionPlayStateChanged(bool isPlaying)
        {
            OnTransitionPlayStateChanged?.Invoke(isPlaying);
        }

        #endregion

        #region Raise Methods - Navigation

        /// <summary>Raises <see cref="OnNavigateToState"/>.</summary>
        public static void RaiseNavigateToState(AnimationStateAsset state)
        {
            OnNavigateToState?.Invoke(state);
        }

        /// <summary>Raises <see cref="OnNavigateToTransition"/>.</summary>
        public static void RaiseNavigateToTransition(AnimationStateAsset fromState, AnimationStateAsset toState, bool isAnyState)
        {
            OnNavigateToTransition?.Invoke(fromState, toState, isAnyState);
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
            
            // Preview time events
            OnPreviewTimeChanged = null;
            OnPlayStateChanged = null;
            OnLoopStateChanged = null;
            
            // Blend space events
            OnBlendPosition1DChanged = null;
            OnBlendPosition2DChanged = null;
            OnClipSelectedForPreview = null;
            OnBlendSpaceEditModeChanged = null;
            
            // Transition events
            OnTransitionProgressChanged = null;
            OnTransitionTimeChanged = null;
            OnTransitionFromBlendPositionChanged = null;
            OnTransitionToBlendPositionChanged = null;
            OnTransitionPlayStateChanged = null;
            
            // Navigation events
            OnNavigateToState = null;
            OnNavigateToTransition = null;
            
            // Lifecycle events
            OnPreviewCreated = null;
            OnPreviewDisposed = null;
            OnPreviewError = null;
        }

        #endregion
    }
}
