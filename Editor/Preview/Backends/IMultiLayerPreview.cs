using System;
using DMotion.Authoring;
using Unity.Mathematics;

namespace DMotion.Editor
{
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
    /// Interface for multi-layer animation preview.
    /// Focuses on layer composition: weights, masks, override vs additive blending.
    /// 
    /// Separate from IPreviewBackend because the preview concerns are different:
    /// - IPreviewBackend: Preview state internals (clips, blends, transitions within one state)
    /// - IMultiLayerPreview: Preview layer composition (how multiple layers blend together)
    /// 
    /// A backend that supports multi-layer preview implements both interfaces.
    /// </summary>
    public interface IMultiLayerPreview : IDisposable
    {
        #region Properties
        
        /// <summary>
        /// Whether the multi-layer preview is initialized.
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// Error message if initialization failed.
        /// </summary>
        string ErrorMessage { get; }
        
        /// <summary>
        /// Gets the number of layers in the preview.
        /// </summary>
        int LayerCount { get; }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Creates a multi-layer preview for a state machine.
        /// Each layer starts with its default state.
        /// </summary>
        /// <param name="stateMachine">The root state machine containing layers.</param>
        void Initialize(StateMachineAsset stateMachine);
        
        /// <summary>
        /// Clears the preview.
        /// </summary>
        void Clear();
        
        #endregion
        
        #region Layer Weight Control
        
        /// <summary>
        /// Sets the weight of a layer.
        /// </summary>
        /// <param name="layerIndex">Layer index (0 = base layer).</param>
        /// <param name="weight">Weight value (0-1).</param>
        void SetLayerWeight(int layerIndex, float weight);
        
        /// <summary>
        /// Gets the current weight of a layer.
        /// </summary>
        float GetLayerWeight(int layerIndex);
        
        /// <summary>
        /// Enables or disables a layer.
        /// Disabled layers contribute no animation.
        /// </summary>
        void SetLayerEnabled(int layerIndex, bool enabled);
        
        /// <summary>
        /// Gets whether a layer is enabled.
        /// </summary>
        bool IsLayerEnabled(int layerIndex);
        
        #endregion
        
        #region Per-Layer Animation Control
        
        /// <summary>
        /// Sets which state to preview in a specific layer.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="state">The state to preview (must belong to the layer's state machine).</param>
        void SetLayerState(int layerIndex, AnimationStateAsset state);
        
        /// <summary>
        /// Sets the normalized time for a specific layer's animation.
        /// </summary>
        void SetLayerNormalizedTime(int layerIndex, float normalizedTime);
        
        /// <summary>
        /// Sets the blend position for a specific layer (for blend states).
        /// </summary>
        void SetLayerBlendPosition(int layerIndex, float2 position);
        
        #endregion
        
        #region State Query
        
        /// <summary>
        /// Gets the current state of all layers.
        /// </summary>
        LayerPreviewState[] GetLayerStates();
        
        /// <summary>
        /// Gets the state of a specific layer.
        /// </summary>
        LayerPreviewState GetLayerState(int layerIndex);
        
        #endregion
        
        #region Global Controls
        
        /// <summary>
        /// Sets the global time for all layers simultaneously.
        /// Useful for synchronized playback.
        /// </summary>
        void SetGlobalNormalizedTime(float normalizedTime);
        
        /// <summary>
        /// Sets whether playback is running or paused.
        /// </summary>
        void SetPlaying(bool playing);
        
        /// <summary>
        /// Steps all layers by the given number of frames.
        /// </summary>
        void StepFrames(int frameCount, float fps = 30f);
        
        #endregion
        
        #region Update & Render
        
        /// <summary>
        /// Updates the preview. Call every frame.
        /// </summary>
        /// <param name="deltaTime">Time since last update.</param>
        /// <returns>True if repaint is needed.</returns>
        bool Tick(float deltaTime);
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        void Draw(UnityEngine.Rect rect);
        
        /// <summary>
        /// Handles camera input.
        /// </summary>
        /// <returns>True if input was handled.</returns>
        bool HandleInput(UnityEngine.Rect rect);
        
        /// <summary>
        /// Resets camera to default view.
        /// </summary>
        void ResetCameraView();
        
        #endregion
    }
}
