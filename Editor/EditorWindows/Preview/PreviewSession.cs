using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Dirty flags for preview state tracking.
    /// Used to optimize updates - only refresh when something changes.
    /// </summary>
    [Flags]
    public enum PreviewDirtyFlags
    {
        None = 0,
        Selection = 1 << 0,      // Selected state/transition changed
        Parameters = 1 << 1,     // Blend parameters changed
        Time = 1 << 2,           // Normalized time changed
        Rig = 1 << 3,            // Preview model changed
        TransitionProgress = 1 << 4, // Transition progress changed
        All = Selection | Parameters | Time | Rig | TransitionProgress
    }
    
    /// <summary>
    /// Manages the animation preview session, including backend selection and dirty tracking.
    /// This is the main interface for the AnimationPreviewWindow to interact with preview backends.
    /// </summary>
    public class PreviewSession : IDisposable
    {
        #region State
        
        private IPreviewBackend activeBackend;
        private PreviewMode currentMode = PreviewMode.Authoring;
        private PreviewDirtyFlags dirtyFlags = PreviewDirtyFlags.All;
        
        // Cached state for dirty detection
        private AnimationStateAsset cachedState;
        private AnimationStateAsset cachedTransitionFrom;
        private AnimationStateAsset cachedTransitionTo;
        private float cachedTransitionDuration;
        private GameObject cachedPreviewModel;
        private float cachedNormalizedTime;
        private float2 cachedBlendPosition;
        private float cachedTransitionProgress;
        
        // Camera state preserved across backend switches
        private PlayableGraphPreview.CameraState savedCameraState;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The current preview mode.
        /// </summary>
        public PreviewMode Mode
        {
            get => currentMode;
            set
            {
                if (currentMode == value) return;
                SwitchMode(value);
            }
        }
        
        /// <summary>
        /// Whether the session has an active, initialized backend.
        /// </summary>
        public bool IsActive => activeBackend?.IsInitialized ?? false;
        
        /// <summary>
        /// The active preview backend.
        /// </summary>
        public IPreviewBackend Backend => activeBackend;
        
        /// <summary>
        /// Current dirty flags indicating what needs updating.
        /// </summary>
        public PreviewDirtyFlags DirtyFlags => dirtyFlags;
        
        /// <summary>
        /// Whether the preview is currently showing a transition.
        /// </summary>
        public bool IsTransitionPreview => activeBackend?.IsTransitionPreview ?? false;
        
        /// <summary>
        /// Whether there's content to display (preview or error message).
        /// </summary>
        public bool HasContent
        {
            get
            {
                if (activeBackend == null) return false;
                return activeBackend.IsInitialized || !string.IsNullOrEmpty(activeBackend.ErrorMessage);
            }
        }
        
        /// <summary>
        /// The preview model (armature with Animator).
        /// </summary>
        public GameObject PreviewModel
        {
            get => cachedPreviewModel;
            set
            {
                if (cachedPreviewModel == value) return;
                cachedPreviewModel = value;
                MarkDirty(PreviewDirtyFlags.Rig);
                activeBackend?.SetPreviewModel(value);
            }
        }
        
        #endregion
        
        #region Constructor
        
        /// <summary>
        /// Creates a new preview session with the specified initial mode.
        /// </summary>
        public PreviewSession(PreviewMode initialMode = PreviewMode.Authoring)
        {
            currentMode = initialMode;
            CreateBackendForMode(initialMode);
        }
        
        #endregion
        
        #region Mode Switching
        
        private void SwitchMode(PreviewMode newMode)
        {
            // Save camera state from current backend
            if (activeBackend != null)
            {
                savedCameraState = activeBackend.CameraState;
            }
            
            // Dispose current backend
            activeBackend?.Dispose();
            activeBackend = null;
            
            currentMode = newMode;
            CreateBackendForMode(newMode);
            
            // Restore camera state to new backend
            if (activeBackend != null && savedCameraState.IsValid)
            {
                activeBackend.CameraState = savedCameraState;
            }
            
            // Mark everything dirty to rebuild preview
            MarkDirty(PreviewDirtyFlags.All);
            
            // Recreate the preview with cached state
            RefreshPreview();
        }
        
        private void CreateBackendForMode(PreviewMode mode)
        {
            switch (mode)
            {
                case PreviewMode.Authoring:
                    activeBackend = new PlayableGraphBackend();
                    break;
                    
                case PreviewMode.EcsRuntime:
                    activeBackend = new EcsPreviewBackend();
                    break;
                    
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
            
            // Apply cached preview model
            if (cachedPreviewModel != null)
            {
                activeBackend.SetPreviewModel(cachedPreviewModel);
            }
        }
        
        #endregion
        
        #region Preview Creation
        
        /// <summary>
        /// Creates a preview for the given state.
        /// </summary>
        public void CreatePreviewForState(AnimationStateAsset state)
        {
            cachedState = state;
            cachedTransitionFrom = null;
            cachedTransitionTo = null;
            
            activeBackend?.CreatePreviewForState(state);
            ClearDirty(PreviewDirtyFlags.Selection);
        }
        
        /// <summary>
        /// Creates a preview for a transition between two states.
        /// </summary>
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration)
        {
            cachedState = null;
            cachedTransitionFrom = fromState;
            cachedTransitionTo = toState;
            cachedTransitionDuration = transitionDuration;
            
            activeBackend?.CreateTransitionPreview(fromState, toState, transitionDuration);
            ClearDirty(PreviewDirtyFlags.Selection);
        }
        
        /// <summary>
        /// Clears the current preview.
        /// </summary>
        public void Clear()
        {
            cachedState = null;
            cachedTransitionFrom = null;
            cachedTransitionTo = null;
            
            activeBackend?.Clear();
        }
        
        /// <summary>
        /// Sets an info/error message without a preview.
        /// </summary>
        public void SetMessage(string message)
        {
            cachedState = null;
            cachedTransitionFrom = null;
            cachedTransitionTo = null;
            
            activeBackend?.SetMessage(message);
        }
        
        /// <summary>
        /// Recreates the preview with the current cached state.
        /// Called after mode switch or when dirty flags indicate a refresh is needed.
        /// </summary>
        private void RefreshPreview()
        {
            if (activeBackend == null) return;
            
            if (cachedTransitionFrom != null || cachedTransitionTo != null)
            {
                activeBackend.CreateTransitionPreview(cachedTransitionFrom, cachedTransitionTo, cachedTransitionDuration);
            }
            else if (cachedState != null)
            {
                activeBackend.CreatePreviewForState(cachedState);
            }
        }
        
        #endregion
        
        #region Time Control
        
        /// <summary>
        /// Sets the normalized sample time (0-1).
        /// </summary>
        public void SetNormalizedTime(float normalizedTime)
        {
            if (Math.Abs(cachedNormalizedTime - normalizedTime) < 0.0001f) return;
            
            cachedNormalizedTime = normalizedTime;
            activeBackend?.SetNormalizedTime(normalizedTime);
            MarkDirty(PreviewDirtyFlags.Time);
        }
        
        /// <summary>
        /// Sets per-state normalized times for transition preview.
        /// </summary>
        public void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            activeBackend?.SetTransitionStateNormalizedTimes(fromNormalized, toNormalized);
            MarkDirty(PreviewDirtyFlags.Time);
        }
        
        /// <summary>
        /// Sets the transition progress (0 = from state, 1 = to state).
        /// </summary>
        public void SetTransitionProgress(float progress)
        {
            if (Math.Abs(cachedTransitionProgress - progress) < 0.0001f) return;
            
            cachedTransitionProgress = progress;
            activeBackend?.SetTransitionProgress(progress);
            MarkDirty(PreviewDirtyFlags.TransitionProgress);
        }
        
        /// <summary>
        /// Sets the playback state. When paused, animation time is controlled by the preview.
        /// </summary>
        public void SetPlaying(bool playing)
        {
            activeBackend?.SetPlaying(playing);
        }
        
        /// <summary>
        /// Steps the animation by the given number of frames.
        /// </summary>
        public void StepFrames(int frameCount, float fps = 30f)
        {
            activeBackend?.StepFrames(frameCount, fps);
            MarkDirty(PreviewDirtyFlags.Time);
        }
        
        #endregion
        
        #region Blend Control
        
        /// <summary>
        /// Sets the 1D blend position with smooth transition.
        /// </summary>
        public void SetBlendPosition1D(float value)
        {
            cachedBlendPosition = new float2(value, 0);
            activeBackend?.SetBlendPosition1D(value);
            MarkDirty(PreviewDirtyFlags.Parameters);
        }
        
        /// <summary>
        /// Sets the 2D blend position with smooth transition.
        /// </summary>
        public void SetBlendPosition2D(float2 position)
        {
            cachedBlendPosition = position;
            activeBackend?.SetBlendPosition2D(position);
            MarkDirty(PreviewDirtyFlags.Parameters);
        }
        
        /// <summary>
        /// Sets the 1D blend position immediately (no smoothing).
        /// </summary>
        public void SetBlendPosition1DImmediate(float value)
        {
            cachedBlendPosition = new float2(value, 0);
            activeBackend?.SetBlendPosition1DImmediate(value);
            MarkDirty(PreviewDirtyFlags.Parameters);
        }
        
        /// <summary>
        /// Sets the 2D blend position immediately (no smoothing).
        /// </summary>
        public void SetBlendPosition2DImmediate(float2 position)
        {
            cachedBlendPosition = position;
            activeBackend?.SetBlendPosition2DImmediate(position);
            MarkDirty(PreviewDirtyFlags.Parameters);
        }
        
        /// <summary>
        /// Sets the blend position for the "from" state in transition preview.
        /// </summary>
        public void SetTransitionFromBlendPosition(float2 position)
        {
            activeBackend?.SetTransitionFromBlendPosition(position);
            MarkDirty(PreviewDirtyFlags.Parameters);
        }
        
        /// <summary>
        /// Sets the blend position for the "to" state in transition preview.
        /// </summary>
        public void SetTransitionToBlendPosition(float2 position)
        {
            activeBackend?.SetTransitionToBlendPosition(position);
            MarkDirty(PreviewDirtyFlags.Parameters);
        }
        
        /// <summary>
        /// Sets the solo clip index (-1 for blended mode).
        /// </summary>
        public void SetSoloClip(int clipIndex)
        {
            activeBackend?.SetSoloClip(clipIndex);
        }
        
        #endregion
        
        #region Update & Render
        
        /// <summary>
        /// Updates smooth transitions. Call every frame.
        /// </summary>
        /// <param name="deltaTime">Time since last update.</param>
        /// <returns>True if any transition is still in progress.</returns>
        public bool Tick(float deltaTime)
        {
            return activeBackend?.Tick(deltaTime) ?? false;
        }
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        public void Draw(Rect rect)
        {
            activeBackend?.Draw(rect);
        }
        
        /// <summary>
        /// Handles camera input for the preview.
        /// </summary>
        /// <returns>True if input was handled and repaint is needed.</returns>
        public bool HandleInput(Rect rect)
        {
            return activeBackend?.HandleInput(rect) ?? false;
        }
        
        /// <summary>
        /// Resets the camera to the default view.
        /// </summary>
        public void ResetCameraView()
        {
            activeBackend?.ResetCameraView();
        }
        
        /// <summary>
        /// Gets a snapshot of the current preview state.
        /// </summary>
        public PreviewSnapshot GetSnapshot()
        {
            return activeBackend?.GetSnapshot() ?? new PreviewSnapshot
            {
                ErrorMessage = "No preview backend active",
                IsInitialized = false
            };
        }
        
        #endregion
        
        #region Dirty Tracking
        
        /// <summary>
        /// Marks the specified flags as dirty.
        /// </summary>
        public void MarkDirty(PreviewDirtyFlags flags)
        {
            dirtyFlags |= flags;
        }
        
        /// <summary>
        /// Clears the specified dirty flags.
        /// </summary>
        public void ClearDirty(PreviewDirtyFlags flags)
        {
            dirtyFlags &= ~flags;
        }
        
        /// <summary>
        /// Clears all dirty flags.
        /// </summary>
        public void ClearAllDirty()
        {
            dirtyFlags = PreviewDirtyFlags.None;
        }
        
        /// <summary>
        /// Checks if the specified flags are dirty.
        /// </summary>
        public bool IsDirty(PreviewDirtyFlags flags)
        {
            return (dirtyFlags & flags) != 0;
        }
        
        #endregion
        
        #region Camera State
        
        /// <summary>
        /// Gets or sets the camera state for persistence.
        /// </summary>
        public PlayableGraphPreview.CameraState CameraState
        {
            get => activeBackend?.CameraState ?? savedCameraState;
            set
            {
                savedCameraState = value;
                if (activeBackend != null)
                {
                    activeBackend.CameraState = value;
                }
            }
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            activeBackend?.Dispose();
            activeBackend = null;
        }
        
        #endregion
    }
}
