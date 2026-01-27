using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview mode for animation preview.
    /// </summary>
    public enum PreviewMode
    {
        /// <summary>
        /// Authoring preview using Unity's PlayableGraph.
        /// Fast, works without ECS setup, but may differ from runtime behavior.
        /// </summary>
        Authoring,
        
        /// <summary>
        /// Runtime preview using actual DMotion ECS systems.
        /// Accurate to runtime behavior, requires ECS world setup.
        /// </summary>
        EcsRuntime
    }
    
    /// <summary>
    /// Snapshot of preview state for UI display.
    /// </summary>
    public struct PreviewSnapshot
    {
        /// <summary>Current normalized time (0-1).</summary>
        public float NormalizedTime;
        
        /// <summary>Current blend position (X for 1D, X/Y for 2D).</summary>
        public float2 BlendPosition;
        
        /// <summary>Current blend weights per clip.</summary>
        public float[] BlendWeights;
        
        /// <summary>Transition progress (0-1) if in transition, -1 otherwise.</summary>
        public float TransitionProgress;
        
        /// <summary>Whether the preview is currently playing.</summary>
        public bool IsPlaying;
        
        /// <summary>Error message if preview failed.</summary>
        public string ErrorMessage;
        
        /// <summary>Whether the preview is successfully initialized.</summary>
        public bool IsInitialized;
    }
    
    /// <summary>
    /// Core interface for animation preview backends.
    /// Focuses on previewing state internals: clips, blend trees, transitions.
    /// 
    /// For multi-layer composition preview, see IMultiLayerPreview.
    /// A backend may implement both interfaces if it supports both preview modes.
    /// </summary>
    public interface IPreviewBackend : IDisposable
    {
        #region Properties
        
        /// <summary>
        /// The preview mode this backend implements.
        /// </summary>
        PreviewMode Mode { get; }
        
        /// <summary>
        /// Whether the backend is initialized and ready for preview.
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// Error message if initialization or preview failed.
        /// </summary>
        string ErrorMessage { get; }
        
        /// <summary>
        /// The currently previewed state (null if previewing a transition).
        /// </summary>
        AnimationStateAsset CurrentState { get; }
        
        /// <summary>
        /// Whether currently previewing a transition.
        /// </summary>
        bool IsTransitionPreview { get; }
        
        /// <summary>
        /// Camera state for persistence across backend switches.
        /// </summary>
        PlayableGraphPreview.CameraState CameraState { get; set; }
        
        /// <summary>
        /// Returns the multi-layer preview interface if supported, null otherwise.
        /// Use this to check if the backend supports multi-layer preview.
        /// </summary>
        IMultiLayerPreview MultiLayer { get; }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Creates a preview for the given state.
        /// </summary>
        void CreatePreviewForState(AnimationStateAsset state);
        
        /// <summary>
        /// Creates a preview for a transition between two states.
        /// </summary>
        void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration);
        
        /// <summary>
        /// Sets the preview model (armature with Animator).
        /// </summary>
        void SetPreviewModel(GameObject model);
        
        /// <summary>
        /// Clears the current preview.
        /// </summary>
        void Clear();
        
        /// <summary>
        /// Sets an info/error message without a preview.
        /// </summary>
        void SetMessage(string message);
        
        #endregion
        
        #region Time Control
        
        /// <summary>
        /// Sets the normalized sample time (0-1).
        /// </summary>
        void SetNormalizedTime(float normalizedTime);
        
        /// <summary>
        /// Sets per-state normalized times for transition preview.
        /// </summary>
        void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized);
        
        /// <summary>
        /// Sets the transition progress (0 = from state, 1 = to state).
        /// </summary>
        void SetTransitionProgress(float progress);
        
        /// <summary>
        /// Sets the playback state. When paused, animation time is controlled by the preview.
        /// </summary>
        void SetPlaying(bool playing);
        
        /// <summary>
        /// Steps the animation by the given number of frames.
        /// </summary>
        void StepFrames(int frameCount, float fps = 30f);
        
        #endregion
        
        #region Blend Control
        
        /// <summary>
        /// Sets the 1D blend position with smooth transition.
        /// </summary>
        void SetBlendPosition1D(float value);
        
        /// <summary>
        /// Sets the 2D blend position with smooth transition.
        /// </summary>
        void SetBlendPosition2D(float2 position);
        
        /// <summary>
        /// Sets the 1D blend position immediately (no smoothing).
        /// </summary>
        void SetBlendPosition1DImmediate(float value);
        
        /// <summary>
        /// Sets the 2D blend position immediately (no smoothing).
        /// </summary>
        void SetBlendPosition2DImmediate(float2 position);
        
        /// <summary>
        /// Sets the blend position for the "from" state in transition preview.
        /// </summary>
        void SetTransitionFromBlendPosition(float2 position);
        
        /// <summary>
        /// Sets the blend position for the "to" state in transition preview.
        /// </summary>
        void SetTransitionToBlendPosition(float2 position);
        
        /// <summary>
        /// Rebuilds the transition timeline with current blend positions.
        /// Called when transition properties (duration, exit time) change.
        /// </summary>
        void RebuildTransitionTimeline(float2 fromBlendPos, float2 toBlendPos);
        
        /// <summary>
        /// Sets the solo clip index (-1 for blended mode).
        /// </summary>
        void SetSoloClip(int clipIndex);
        
        #endregion
        
        #region Update & Render
        
        /// <summary>
        /// Updates smooth transitions. Call every frame.
        /// </summary>
        /// <param name="deltaTime">Time since last update.</param>
        /// <returns>True if any transition is still in progress.</returns>
        bool Tick(float deltaTime);
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        void Draw(Rect rect);
        
        /// <summary>
        /// Handles camera input for the preview.
        /// </summary>
        /// <returns>True if input was handled and repaint is needed.</returns>
        bool HandleInput(Rect rect);
        
        /// <summary>
        /// Resets the camera to the default view.
        /// </summary>
        void ResetCameraView();
        
        /// <summary>
        /// Gets a snapshot of the current preview state.
        /// </summary>
        PreviewSnapshot GetSnapshot();
        
        #endregion
    }
}
