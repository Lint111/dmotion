using System;
using System.Collections.Generic;
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
    /// State of a single layer in multi-layer preview.
    /// </summary>
    public struct LayerPreviewState
    {
        /// <summary>Layer index (0 = base layer).</summary>
        public int LayerIndex;
        
        /// <summary>Layer name for display.</summary>
        public string Name;
        
        /// <summary>Current layer weight (0-1).</summary>
        public float Weight;
        
        /// <summary>Layer blend mode.</summary>
        public LayerBlendMode BlendMode;
        
        /// <summary>Whether this layer is enabled in preview.</summary>
        public bool IsEnabled;
        
        /// <summary>Whether this layer has a bone mask (partial body).</summary>
        public bool HasBoneMask;
        
        /// <summary>Current state being previewed in this layer (null if using default).</summary>
        public AnimationStateAsset CurrentState;
        
        /// <summary>Normalized time for this layer's animation (0-1).</summary>
        public float NormalizedTime;
        
        /// <summary>Blend position for this layer's blend state.</summary>
        public float2 BlendPosition;
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
        
        /// <summary>Whether this is a multi-layer preview.</summary>
        public bool IsMultiLayerPreview;
        
        /// <summary>Layer states for multi-layer preview. Null for single-state preview.</summary>
        public LayerPreviewState[] LayerStates;
    }
    
    /// <summary>
    /// Interface for animation preview backends.
    /// Allows switching between Authoring (PlayableGraph) and ECS Runtime preview modes.
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
        /// Creates a multi-layer preview for a state machine with layers.
        /// Each layer's default state will be previewed initially.
        /// </summary>
        /// <param name="stateMachine">The root state machine containing layers.</param>
        void CreateMultiLayerPreview(StateMachineAsset stateMachine);
        
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
        
        #region Multi-Layer Control
        
        /// <summary>
        /// Whether this is a multi-layer preview.
        /// </summary>
        bool IsMultiLayerPreview { get; }
        
        /// <summary>
        /// Gets the number of layers in the current multi-layer preview.
        /// Returns 0 if not a multi-layer preview.
        /// </summary>
        int LayerCount { get; }
        
        /// <summary>
        /// Sets the weight of a layer in multi-layer preview.
        /// </summary>
        /// <param name="layerIndex">Layer index (0 = base layer).</param>
        /// <param name="weight">Weight value (0-1).</param>
        void SetLayerWeight(int layerIndex, float weight);
        
        /// <summary>
        /// Gets the current weight of a layer.
        /// </summary>
        float GetLayerWeight(int layerIndex);
        
        /// <summary>
        /// Enables or disables a layer in preview.
        /// Disabled layers contribute no animation.
        /// </summary>
        void SetLayerEnabled(int layerIndex, bool enabled);
        
        /// <summary>
        /// Gets whether a layer is enabled.
        /// </summary>
        bool IsLayerEnabled(int layerIndex);
        
        /// <summary>
        /// Sets the current state for a specific layer in multi-layer preview.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="state">The state to preview in this layer.</param>
        void SetLayerState(int layerIndex, AnimationStateAsset state);
        
        /// <summary>
        /// Sets the normalized time for a specific layer.
        /// </summary>
        void SetLayerNormalizedTime(int layerIndex, float normalizedTime);
        
        /// <summary>
        /// Sets the blend position for a specific layer (for blend states).
        /// </summary>
        void SetLayerBlendPosition(int layerIndex, float2 position);
        
        /// <summary>
        /// Gets information about all layers in the preview.
        /// </summary>
        LayerPreviewState[] GetLayerStates();
        
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
