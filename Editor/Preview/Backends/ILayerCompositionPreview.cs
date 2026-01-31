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
        
        /// <summary>Current state being previewed in this layer (null if in transition mode).</summary>
        public AnimationStateAsset CurrentState;
        
        /// <summary>Normalized time for this layer's animation (0-1).</summary>
        public float NormalizedTime;
        
        /// <summary>Blend position for this layer (for blend states).</summary>
        public float2 BlendPosition;
        
        /// <summary>Whether this layer is currently showing a transition.</summary>
        public bool IsTransitionMode;
        
        /// <summary>From state in transition mode (null if not in transition).</summary>
        public AnimationStateAsset TransitionFromState;
        
        /// <summary>To state in transition mode (null if not in transition).</summary>
        public AnimationStateAsset TransitionToState;
        
        /// <summary>Transition progress (0 = fully from, 1 = fully to).</summary>
        public float TransitionProgress;
    }
    
    /// <summary>
    /// Preview interface for multi-layer composition.
    /// Focuses on: layer weights, masks, override vs additive blending.
    /// 
    /// Use this to preview how layers COMBINE:
    /// - How weight affects layer influence
    /// - How bone masks create partial-body animation
    /// - How override vs additive layers interact
    /// - How transitions crossfade between states within a layer
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
        
        #region Per-Layer Single State Control
        
        /// <summary>
        /// Sets which state to preview in a specific layer.
        /// Clears any active transition for this layer.
        /// </summary>
        void SetLayerState(int layerIndex, AnimationStateAsset state);
        
        /// <summary>
        /// Sets the normalized time for a specific layer's animation.
        /// In transition mode, this sets the time for both from and to states.
        /// </summary>
        void SetLayerNormalizedTime(int layerIndex, float normalizedTime);
        
        /// <summary>
        /// Sets the blend position for a specific layer (for blend states).
        /// In single-state mode, sets the blend position for the current state.
        /// In transition mode, use SetLayerTransitionBlendPositions instead.
        /// </summary>
        void SetLayerBlendPosition(int layerIndex, float2 position);
        
        #endregion
        
        #region Per-Layer Transition Control
        
        /// <summary>
        /// Sets up a transition preview for a specific layer.
        /// The layer will blend between fromState and toState based on transition progress.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="fromState">State transitioning from (can be null for Any State).</param>
        /// <param name="toState">State transitioning to.</param>
        void SetLayerTransition(int layerIndex, AnimationStateAsset fromState, AnimationStateAsset toState);
        
        /// <summary>
        /// Sets the transition progress for a layer in transition mode.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="progress">Progress from 0 (fully from) to 1 (fully to).</param>
        void SetLayerTransitionProgress(int layerIndex, float progress);
        
        /// <summary>
        /// Sets the blend positions for both states in a transition.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="fromBlendPosition">Blend position for the from state.</param>
        /// <param name="toBlendPosition">Blend position for the to state.</param>
        void SetLayerTransitionBlendPositions(int layerIndex, float2 fromBlendPosition, float2 toBlendPosition);
        
        /// <summary>
        /// Sets the normalized times for both states in a transition independently.
        /// Allows from and to states to be at different points in their animations.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="fromNormalizedTime">Normalized time for the from state (0-1).</param>
        /// <param name="toNormalizedTime">Normalized time for the to state (0-1).</param>
        void SetLayerTransitionNormalizedTimes(int layerIndex, float fromNormalizedTime, float toNormalizedTime);
        
        /// <summary>
        /// Clears any active transition for a layer, returning to single-state mode.
        /// </summary>
        void ClearLayerTransition(int layerIndex);
        
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
