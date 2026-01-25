using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Manages 3D preview rendering for animation states.
    /// Handles preview creation, disposal, and rendering.
    /// </summary>
    internal class PreviewRenderer : IDisposable
    {
        #region Constants
        
        /// <summary>Background color for the 3D preview area.</summary>
        private static readonly Color PreviewBackground = new(0.15f, 0.15f, 0.15f);
        
        /// <summary>Text color for error/info messages.</summary>
        private static readonly Color MessageTextColor = new(0.7f, 0.7f, 0.7f);
        
        #endregion
        
        #region State
        
        private PlayableGraphPreview activePreview;
        private BlendedClipPreview blendedPreview; // Cast reference for blend-specific operations
        private TransitionPreview transitionPreview; // Cast reference for transition-specific operations
        private bool previewInitialized;
        private string previewErrorMessage;
        private AnimationStateAsset currentState;
        
        // Transition state
        private AnimationStateAsset transitionFromState;
        private AnimationStateAsset transitionToState;
        
        // Preview model (manually assigned)
        private GameObject previewModel;
        
        // Camera state for persistence
        private PlayableGraphPreview.CameraState savedCameraState;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether a preview is currently initialized and ready to render.
        /// </summary>
        public bool IsInitialized => previewInitialized;
        
        /// <summary>
        /// Error message if preview creation failed.
        /// </summary>
        public string ErrorMessage => previewErrorMessage;
        
        /// <summary>
        /// Whether there's content to display (preview or error message).
        /// </summary>
        public bool HasContent => previewInitialized || !string.IsNullOrEmpty(previewErrorMessage);
        
        /// <summary>
        /// The current state being previewed.
        /// </summary>
        public AnimationStateAsset CurrentState => currentState;
        
        /// <summary>
        /// The preview model (armature with Animator and SkinnedMeshRenderer).
        /// When set, this model will be used instead of auto-detecting from clips.
        /// </summary>
        public GameObject PreviewModel
        {
            get => previewModel;
            set
            {
                if (previewModel == value) return;
                previewModel = value;
                
                // If we have an active preview, update its model
                if (activePreview != null && previewInitialized)
                {
                    activePreview.GameObject = previewModel;
                }
            }
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Creates a preview for the given state.
        /// </summary>
        public void CreatePreviewForState(AnimationStateAsset state)
        {
            DisposePreview();
            previewErrorMessage = null;
            currentState = state;
            
            if (state == null)
            {
                previewErrorMessage = "No state selected";
                return;
            }
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    CreateSingleClipPreview(singleClip);
                    break;
                    
                case LinearBlendStateAsset linearBlend:
                    CreateLinearBlendPreview(linearBlend);
                    break;
                    
                case Directional2DBlendStateAsset blend2D:
                    Create2DBlendPreview(blend2D);
                    break;
                    
                default:
                    previewErrorMessage = $"Preview not supported\nfor {state.GetType().Name}";
                    break;
            }
        }
        
        /// <summary>
        /// Sets an error/info message without a preview (e.g., for transitions).
        /// </summary>
        public void SetMessage(string message)
        {
            DisposePreview();
            previewErrorMessage = message;
            currentState = null;
        }
        
        /// <summary>
        /// Clears the preview and any messages.
        /// </summary>
        public void Clear()
        {
            DisposePreview();
            previewErrorMessage = null;
            currentState = null;
        }
        
        /// <summary>
        /// Updates the normalized sample time for the preview.
        /// </summary>
        public void SetNormalizedTime(float normalizedTime)
        {
            if (activePreview != null && previewInitialized)
            {
                activePreview.NormalizedSampleTime = normalizedTime;
            }
        }
        
        /// <summary>
        /// Updates the 1D blend position for LinearBlendStateAsset previews (smooth transition).
        /// </summary>
        public void SetBlendPosition1D(float blendValue)
        {
            if (blendedPreview != null && previewInitialized)
            {
                blendedPreview.SetBlendPositionTarget(new float2(blendValue, 0));
            }
        }
        
        /// <summary>
        /// Updates the 2D blend position for Directional2DBlendStateAsset previews (smooth transition).
        /// </summary>
        public void SetBlendPosition2D(Vector2 blendPosition)
        {
            if (blendedPreview != null && previewInitialized)
            {
                blendedPreview.SetBlendPositionTarget(new float2(blendPosition.x, blendPosition.y));
            }
        }
        
        /// <summary>
        /// Immediately sets the 1D blend position without smooth transition.
        /// </summary>
        public void SetBlendPosition1DImmediate(float blendValue)
        {
            if (blendedPreview != null && previewInitialized)
            {
                blendedPreview.SetBlendPositionImmediate(new float2(blendValue, 0));
            }
        }
        
        /// <summary>
        /// Immediately sets the 2D blend position without smooth transition.
        /// </summary>
        public void SetBlendPosition2DImmediate(Vector2 blendPosition)
        {
            if (blendedPreview != null && previewInitialized)
            {
                blendedPreview.SetBlendPositionImmediate(new float2(blendPosition.x, blendPosition.y));
            }
        }
        
        /// <summary>
        /// Whether the blend position is currently transitioning.
        /// </summary>
        public bool IsBlendTransitioning => blendedPreview?.IsTransitioning ?? false;
        
        /// <summary>
        /// Updates smooth transitions. Call every frame.
        /// </summary>
        /// <param name="deltaTime">Time since last update.</param>
        /// <returns>True if any transition is still in progress.</returns>
        public bool Tick(float deltaTime)
        {
            if (blendedPreview != null && previewInitialized)
            {
                return blendedPreview.Tick(deltaTime);
            }
            return false;
        }
        
        /// <summary>
        /// Gets the current blend weights (for UI visualization).
        /// Returns null if not a blend preview.
        /// </summary>
        public float[] GetCurrentBlendWeights()
        {
            return blendedPreview?.CurrentWeights;
        }
        
        /// <summary>
        /// Sets the preview to show only a single clip from a blend state (solo mode).
        /// Pass -1 to return to blended preview mode.
        /// </summary>
        /// <param name="clipIndex">Index of the clip to solo, or -1 for blended mode.</param>
        public void SetSoloClip(int clipIndex)
        {
            if (blendedPreview != null && previewInitialized)
            {
                blendedPreview.SetSoloClip(clipIndex);
            }
        }
        
        /// <summary>
        /// Gets the currently soloed clip index, or -1 if in blended mode.
        /// </summary>
        public int SoloClipIndex => blendedPreview?.SoloClipIndex ?? -1;
        
        /// <summary>
        /// Creates a preview for a transition between two states.
        /// </summary>
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration)
        {
            DisposePreview();
            previewErrorMessage = null;
            currentState = null;
            transitionFromState = fromState;
            transitionToState = toState;
            
            // Validate that we have at least a "to" state
            if (toState == null)
            {
                previewErrorMessage = "No target state for transition";
                return;
            }
            
            // Check if states have valid clips
            if (!HasValidClips(fromState) && !HasValidClips(toState))
            {
                previewErrorMessage = "No valid animation clips\nin transition states";
                return;
            }
            
            try
            {
                var preview = new TransitionPreview(fromState, toState, transitionDuration);
                
                // Set manual model if available
                if (previewModel != null)
                {
                    preview.GameObject = previewModel;
                }
                
                preview.Initialize();
                
                // Check if model was found
                if (preview.GameObject == null)
                {
                    preview.Dispose();
                    previewErrorMessage = "Could not find model\nfor transition states.\n\nDrag a model prefab to\nthe Preview Model field.";
                    return;
                }
                
                activePreview = preview;
                transitionPreview = preview;
                blendedPreview = null;
                previewInitialized = true;
                RestoreCameraState();
                
                // Set initial blend positions from persisted settings
                if (fromState != null)
                {
                    var fromBlend = PreviewSettings.GetBlendPosition(fromState);
                    transitionPreview.FromBlendPosition = fromBlend;
                }
                if (toState != null)
                {
                    var toBlend = PreviewSettings.GetBlendPosition(toState);
                    transitionPreview.ToBlendPosition = toBlend;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PreviewRenderer] Failed to create transition preview: {e.Message}");
                DisposePreview();
                previewErrorMessage = $"Preview error:\n{e.Message}";
            }
        }
        

        
        /// <summary>
        /// Sets the transition progress (0 = fully "from" state, 1 = fully "to" state).
        /// </summary>
        public void SetTransitionProgress(float progress)
        {
            if (transitionPreview != null && previewInitialized)
            {
                transitionPreview.TransitionProgress = progress;
            }
        }
        
        /// <summary>
        /// Gets the current transition progress.
        /// </summary>
        public float GetTransitionProgress()
        {
            return transitionPreview?.TransitionProgress ?? 0f;
        }
        
        /// <summary>
        /// Whether the current preview is a transition preview.
        /// </summary>
        public bool IsTransitionPreview => transitionPreview != null && previewInitialized;
        
        /// <summary>
        /// Sets the blend position for the "from" state in a transition preview.
        /// Only applicable when the from state is a blend state.
        /// </summary>
        public void SetTransitionFromBlendPosition(Vector2 blendPosition)
        {
            if (transitionPreview != null && previewInitialized)
            {
                transitionPreview.FromBlendPosition = new Unity.Mathematics.float2(blendPosition.x, blendPosition.y);
            }
        }
        
        /// <summary>
        /// Sets the blend position for the "to" state in a transition preview.
        /// Only applicable when the to state is a blend state.
        /// </summary>
        public void SetTransitionToBlendPosition(Vector2 blendPosition)
        {
            if (transitionPreview != null && previewInitialized)
            {
                transitionPreview.ToBlendPosition = new Unity.Mathematics.float2(blendPosition.x, blendPosition.y);
            }
        }
        
        /// <summary>
        /// Gets the "from" state of the current transition preview.
        /// </summary>
        public AnimationStateAsset TransitionFromState => transitionPreview?.FromState;
        
        /// <summary>
        /// Gets the "to" state of the current transition preview.
        /// </summary>
        public AnimationStateAsset TransitionToState => transitionPreview?.ToState;
        
        /// <summary>
        /// Sets the normalized sample time for transition previews (legacy - same time for both states).
        /// Prefer using SetTransitionStateNormalizedTimes for proper per-state timing.
        /// </summary>
        public void SetTransitionNormalizedTime(float normalizedTime)
        {
            if (transitionPreview != null && previewInitialized)
            {
                transitionPreview.NormalizedSampleTime = normalizedTime;
            }
        }
        
        /// <summary>
        /// Sets the normalized sample times for both states in a transition independently.
        /// This allows proper time synchronization where each state advances at its own rate.
        /// </summary>
        public void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            if (transitionPreview != null && previewInitialized)
            {
                transitionPreview.SetStateNormalizedTimes(fromNormalized, toNormalized);
            }
        }
        
        /// <summary>
        /// Gets the current normalized sample time for transition previews.
        /// </summary>
        public float GetTransitionNormalizedTime()
        {
            return transitionPreview?.NormalizedSampleTime ?? 0f;
        }
        
        /// <summary>
        /// Gets or sets the camera state for persistence across domain reloads.
        /// </summary>
        public PlayableGraphPreview.CameraState CameraState
        {
            get => activePreview?.CurrentCameraState ?? savedCameraState;
            set
            {
                savedCameraState = value;
                if (activePreview != null && previewInitialized)
                {
                    activePreview.CurrentCameraState = value;
                }
            }
        }
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        public void Draw(Rect rect)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            
            // Draw background
            EditorGUI.DrawRect(rect, PreviewBackground);
            
            // Check if we have a valid preview
            if (activePreview != null && previewInitialized)
            {
                // Draw the 3D preview
                activePreview.DrawPreview(rect, GUIStyle.none);
            }
            else if (!string.IsNullOrEmpty(previewErrorMessage))
            {
                // Show error/info message
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = MessageTextColor }
                };
                GUI.Label(rect, previewErrorMessage, style);
            }
        }
        
        /// <summary>
        /// Handles camera input for the preview.
        /// </summary>
        /// <returns>True if input was handled and repaint is needed.</returns>
        public bool HandleInput(Rect rect)
        {
            var evt = Event.current;
            if (evt == null) return false;
            
            // Handle camera orbit when mouse is over the preview area
            if (rect.Contains(evt.mousePosition) && activePreview != null && previewInitialized)
            {
                // Let PlayableGraphPreview handle camera controls
                activePreview.HandleCamera();
                
                if (evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Resets the camera to the default view.
        /// </summary>
        public void ResetCameraView()
        {
            activePreview?.ResetCameraView();
        }
        
        /// <summary>
        /// Disposes the preview resources.
        /// </summary>
        public void Dispose()
        {
            DisposePreview();
        }
        
        #endregion
        
        #region Private - Preview Creation
        
        private void CreateSingleClipPreview(SingleClipStateAsset state)
        {
            var clipAsset = state.Clip;
            if (clipAsset == null || clipAsset.Clip == null)
            {
                previewErrorMessage = "No animation clip assigned";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                return;
            }
            
            try
            {
                var preview = new SingleClipPreview(clipAsset.Clip);
                
                // Set manual model if available
                if (previewModel != null)
                {
                    preview.GameObject = previewModel;
                }
                
                preview.Initialize();
                
                // Check if model was found
                if (preview.GameObject == null)
                {
                    preview.Dispose();
                    previewErrorMessage = "Could not find model\nfor this animation clip.\n\nDrag a model prefab to\nthe Preview Model field.";
                    AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                    return;
                }
                
                activePreview = preview;
                blendedPreview = null;
                previewInitialized = true;
                RestoreCameraState();
                AnimationPreviewEvents.RaisePreviewCreated(state);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PreviewRenderer] Failed to create preview: {e.Message}");
                DisposePreview();
                previewErrorMessage = $"Preview error:\n{e.Message}";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
            }
        }
        
        private void CreateLinearBlendPreview(LinearBlendStateAsset state)
        {
            if (state.BlendClips == null || state.BlendClips.Length == 0)
            {
                previewErrorMessage = "No clips assigned to blend state";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                return;
            }
            
            // Check if any clips have valid animation clips
            bool hasValidClip = false;
            foreach (var clip in state.BlendClips)
            {
                if (clip.Clip?.Clip != null)
                {
                    hasValidClip = true;
                    break;
                }
            }
            
            if (!hasValidClip)
            {
                previewErrorMessage = "No valid animation clips\nin blend state";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                return;
            }
            
            try
            {
                var preview = new BlendedClipPreview(state);
                
                // Set manual model if available
                if (previewModel != null)
                {
                    preview.GameObject = previewModel;
                }
                
                preview.Initialize();
                
                // Check if model was found
                if (preview.GameObject == null)
                {
                    preview.Dispose();
                    previewErrorMessage = "Could not find model\nfor blend state clips.\n\nDrag a model prefab to\nthe Preview Model field.";
                    AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                    return;
                }
                
                activePreview = preview;
                blendedPreview = preview;
                previewInitialized = true;
                RestoreCameraState();
                
                // Set initial blend position from persisted settings
                var initialBlend = PreviewSettings.instance.GetBlendValue1D(state);
                blendedPreview.SetBlendPositionImmediate(new Unity.Mathematics.float2(initialBlend, 0));
                
                AnimationPreviewEvents.RaisePreviewCreated(state);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PreviewRenderer] Failed to create blend preview: {e.Message}");
                DisposePreview();
                previewErrorMessage = $"Preview error:\n{e.Message}";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
            }
        }
        
        private void Create2DBlendPreview(Directional2DBlendStateAsset state)
        {
            if (state.BlendClips == null || state.BlendClips.Length == 0)
            {
                previewErrorMessage = "No clips assigned to\n2D blend state";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                return;
            }
            
            // Check if any clips have valid animation clips
            bool hasValidClip = false;
            foreach (var clip in state.BlendClips)
            {
                if (clip.Clip?.Clip != null)
                {
                    hasValidClip = true;
                    break;
                }
            }
            
            if (!hasValidClip)
            {
                previewErrorMessage = "No valid animation clips\nin 2D blend state";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                return;
            }
            
            try
            {
                var preview = new BlendedClipPreview(state);
                
                // Set manual model if available
                if (previewModel != null)
                {
                    preview.GameObject = previewModel;
                }
                
                preview.Initialize();
                
                // Check if model was found
                if (preview.GameObject == null)
                {
                    preview.Dispose();
                    previewErrorMessage = "Could not find model\nfor 2D blend state clips.\n\nDrag a model prefab to\nthe Preview Model field.";
                    AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                    return;
                }
                
                activePreview = preview;
                blendedPreview = preview;
                previewInitialized = true;
                RestoreCameraState();
                
                // Set initial blend position from persisted settings
                var initialBlend = PreviewSettings.instance.GetBlendValue2D(state);
                blendedPreview.SetBlendPositionImmediate(new Unity.Mathematics.float2(initialBlend.x, initialBlend.y));
                
                AnimationPreviewEvents.RaisePreviewCreated(state);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PreviewRenderer] Failed to create 2D blend preview: {e.Message}");
                DisposePreview();
                previewErrorMessage = $"Preview error:\n{e.Message}";
                AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
            }
        }
        
        private void DisposePreview()
        {
            var wasInitialized = previewInitialized;
            var previousState = currentState;
            
            if (activePreview != null)
            {
                // Save camera state before disposing
                var currentCamState = activePreview.CurrentCameraState;
                if (currentCamState.IsValid)
                {
                    savedCameraState = currentCamState;
                }
                
                try
                {
                    activePreview.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PreviewRenderer] Error disposing preview: {e.Message}");
                }
                activePreview = null;
                blendedPreview = null;
                transitionPreview = null;
            }
            previewInitialized = false;
            transitionFromState = null;
            transitionToState = null;
            
            // Raise disposed event if we had an active preview
            if (wasInitialized && previousState != null)
            {
                AnimationPreviewEvents.RaisePreviewDisposed(previousState);
            }
        }
        
        /// <summary>
        /// Restores the saved camera state to the active preview.
        /// </summary>
        private void RestoreCameraState()
        {
            if (activePreview != null && savedCameraState.IsValid)
            {
                activePreview.CurrentCameraState = savedCameraState;
            }
        }
        
        /// <summary>
        /// Checks if a state has valid animation clips.
        /// </summary>
        private static bool HasValidClips(AnimationStateAsset state)
        {
            if (state == null) return false;
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    return singleClip.Clip?.Clip != null;
                    
                case LinearBlendStateAsset linearBlend:
                    if (linearBlend.BlendClips == null) return false;
                    foreach (var clip in linearBlend.BlendClips)
                    {
                        if (clip.Clip?.Clip != null) return true;
                    }
                    return false;
                    
                case Directional2DBlendStateAsset blend2D:
                    if (blend2D.BlendClips == null) return false;
                    foreach (var clip in blend2D.BlendClips)
                    {
                        if (clip.Clip?.Clip != null) return true;
                    }
                    return false;
                    
                default:
                    return false;
            }
        }
        
        #endregion
    }
}
