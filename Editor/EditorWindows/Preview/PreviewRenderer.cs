using System;
using DMotion.Authoring;
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
        #region State
        
        private SingleClipPreview singleClipPreview;
        private bool previewInitialized;
        private string previewErrorMessage;
        private AnimationStateAsset currentState;
        
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
                    
                case LinearBlendStateAsset:
                    previewErrorMessage = PreviewWindowConstants.BlendPreviewNotAvailable;
                    break;
                    
                case Directional2DBlendStateAsset:
                    previewErrorMessage = PreviewWindowConstants.Blend2DPreviewNotAvailable;
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
            if (singleClipPreview != null && previewInitialized)
            {
                singleClipPreview.NormalizedSampleTime = normalizedTime;
            }
        }
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        public void Draw(Rect rect)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            
            // Draw background
            EditorGUI.DrawRect(rect, PreviewWindowConstants.PreviewBackground);
            
            // Check if we have a valid preview
            if (singleClipPreview != null && previewInitialized)
            {
                // Draw the 3D preview
                singleClipPreview.DrawPreview(rect, GUIStyle.none);
            }
            else if (!string.IsNullOrEmpty(previewErrorMessage))
            {
                // Show error/info message
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = PreviewWindowConstants.MessageTextColor }
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
            if (rect.Contains(evt.mousePosition) && singleClipPreview != null && previewInitialized)
            {
                // Let PlayableGraphPreview handle camera controls
                singleClipPreview.HandleCamera();
                
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
            singleClipPreview?.ResetCameraView();
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
                singleClipPreview = new SingleClipPreview(clipAsset.Clip);
                singleClipPreview.Initialize();
                
                // Check if model was found
                if (singleClipPreview.GameObject == null)
                {
                    DisposePreview();
                    previewErrorMessage = "Could not find model\nfor this animation clip.\n\nEnsure the clip has a\nvalid source avatar.";
                    AnimationPreviewEvents.RaisePreviewError(state, previewErrorMessage);
                    return;
                }
                
                previewInitialized = true;
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
        
        private void DisposePreview()
        {
            var wasInitialized = previewInitialized;
            var previousState = currentState;
            
            if (singleClipPreview != null)
            {
                try
                {
                    singleClipPreview.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PreviewRenderer] Error disposing preview: {e.Message}");
                }
                singleClipPreview = null;
            }
            previewInitialized = false;
            
            // Raise disposed event if we had an active preview
            if (wasInitialized && previousState != null)
            {
                AnimationPreviewEvents.RaisePreviewDisposed(previousState);
            }
        }
        
        #endregion
    }
}
