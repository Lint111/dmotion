using DMotion.Authoring;
using Unity.Mathematics;

namespace DMotion.Editor
{
    /// <summary>
    /// State of a single layer in composition preview.
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
        
        /// <summary>Current state being previewed in this layer.</summary>
        public AnimationStateAsset CurrentState;
        
        /// <summary>Normalized time for this layer's animation (0-1).</summary>
        public float NormalizedTime;
        
        /// <summary>Blend position for this layer (for blend states).</summary>
        public float2 BlendPosition;
    }
    
    /// <summary>
    /// Preview interface for multi-layer composition.
    /// Focuses on: layer weights, masks, override vs additive blending.
    /// 
    /// Use this to preview how layers COMBINE:
    /// - How weight affects layer influence
    /// - How bone masks create partial-body animation
    /// - How override vs additive layers interact
    /// </summary>
    public interface ILayerCompositionPreview : IAnimationPreview
    {
        #region Layer Info
        
        /// <summary>
        /// Number of layers in the preview.
        /// </summary>
        int LayerCount { get; }
        
        /// <summary>
        /// Gets the state of all layers.
        /// </summary>
        LayerPreviewState[] GetLayerStates();
        
        /// <summary>
        /// Gets the state of a specific layer.
        /// </summary>
        LayerPreviewState GetLayerState(int layerIndex);
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes the preview for a multi-layer state machine.
        /// Each layer starts with its default state.
        /// </summary>
        void Initialize(StateMachineAsset stateMachine);
        
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
        
        #region Global Time Control
        
        /// <summary>
        /// Sets the global time for all layers simultaneously.
        /// Useful for synchronized playback preview.
        /// </summary>
        void SetGlobalNormalizedTime(float normalizedTime);
        
        #endregion
    }
}
