using DMotion.Authoring;
using Unity.Mathematics;

namespace DMotion.Editor
{
    /// <summary>
    /// Snapshot of state preview for UI display.
    /// </summary>
    public struct StatePreviewSnapshot
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
    }
    
    /// <summary>
    /// Preview interface for single state or transition internals.
    /// Focuses on: clips, blend trees, state transitions.
    /// 
    /// Use this to preview what happens WITHIN a state:
    /// - How clips blend in a LinearBlend or BlendSpace
    /// - How a transition interpolates between two states
    /// - Solo clip mode for debugging individual clips
    /// </summary>
    public interface IStatePreview : IAnimationPreview
    {
        #region State
        
        /// <summary>
        /// The currently previewed state (null if previewing a transition).
        /// </summary>
        AnimationStateAsset CurrentState { get; }
        
        /// <summary>
        /// Whether currently previewing a transition between two states.
        /// </summary>
        bool IsTransitionPreview { get; }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Creates a preview for a single state.
        /// </summary>
        void CreatePreviewForState(AnimationStateAsset state);
        
        /// <summary>
        /// Creates a preview for a transition between two states.
        /// </summary>
        void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration);
        
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
        /// </summary>
        void RebuildTransitionTimeline(float2 fromBlendPos, float2 toBlendPos);
        
        /// <summary>
        /// Sets the solo clip index (-1 for blended mode).
        /// Solo mode plays only one clip, useful for debugging.
        /// </summary>
        void SetSoloClip(int clipIndex);
        
        #endregion
        
        #region Snapshot
        
        /// <summary>
        /// Gets a snapshot of the current preview state.
        /// </summary>
        StatePreviewSnapshot GetSnapshot();
        
        #endregion
    }
}
