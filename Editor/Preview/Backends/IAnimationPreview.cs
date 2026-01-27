using System;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Base interface for all animation preview types.
    /// Contains common lifecycle, rendering, and camera functionality.
    /// 
    /// Specialized interfaces:
    /// - IStatePreview: Preview state internals (clips, blends, transitions)
    /// - ILayerCompositionPreview: Preview layer composition (weights, masks, blending)
    /// </summary>
    public interface IAnimationPreview : IDisposable
    {
        #region Lifecycle
        
        /// <summary>
        /// Whether the preview is initialized and ready.
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// Error message if initialization or preview failed.
        /// </summary>
        string ErrorMessage { get; }
        
        /// <summary>
        /// Clears the current preview.
        /// </summary>
        void Clear();
        
        /// <summary>
        /// Sets the preview model (armature with Animator).
        /// </summary>
        void SetPreviewModel(GameObject model);
        
        #endregion
        
        #region Playback
        
        /// <summary>
        /// Sets whether playback is running or paused.
        /// </summary>
        void SetPlaying(bool playing);
        
        /// <summary>
        /// Steps the animation by the given number of frames.
        /// </summary>
        void StepFrames(int frameCount, float fps = 30f);
        
        #endregion
        
        #region Rendering
        
        /// <summary>
        /// Updates the preview. Call every frame.
        /// </summary>
        /// <param name="deltaTime">Time since last update.</param>
        /// <returns>True if repaint is needed.</returns>
        bool Tick(float deltaTime);
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        void Draw(Rect rect);
        
        /// <summary>
        /// Handles camera input.
        /// </summary>
        /// <returns>True if input was handled.</returns>
        bool HandleInput(Rect rect);
        
        /// <summary>
        /// Resets camera to default view.
        /// </summary>
        void ResetCameraView();
        
        /// <summary>
        /// Camera state for persistence across preview switches.
        /// </summary>
        PlayableGraphPreview.CameraState CameraState { get; set; }
        
        #endregion
    }
}
