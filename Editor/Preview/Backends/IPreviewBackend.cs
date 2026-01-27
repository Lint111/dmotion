using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview mode for animation preview backends.
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
    /// Combined preview backend interface.
    /// Extends IStatePreview (single state/transition preview) and optionally
    /// provides ILayerCompositionPreview for multi-layer animation preview.
    /// 
    /// Interface hierarchy:
    /// <code>
    ///   IAnimationPreview (base - lifecycle, rendering, camera)
    ///       ├── IStatePreview (single state/transition internals)
    ///       └── ILayerCompositionPreview (multi-layer blending)
    /// 
    ///   IPreviewBackend : IStatePreview
    ///       + PreviewMode Mode
    ///       + ILayerCompositionPreview LayerComposition (optional)
    /// </code>
    /// </summary>
    public interface IPreviewBackend : IStatePreview
    {
        /// <summary>
        /// The preview mode this backend implements.
        /// </summary>
        PreviewMode Mode { get; }
        
        /// <summary>
        /// Returns the layer composition preview interface if supported, null otherwise.
        /// Use this to preview multi-layer animation blending.
        /// </summary>
        ILayerCompositionPreview LayerComposition { get; }
    }
    
    #region Legacy Compatibility
    
    /// <summary>
    /// Legacy snapshot type. Use StatePreviewSnapshot instead.
    /// </summary>
    public struct PreviewSnapshot
    {
        public float NormalizedTime;
        public float2 BlendPosition;
        public float[] BlendWeights;
        public float TransitionProgress;
        public bool IsPlaying;
        public string ErrorMessage;
        public bool IsInitialized;
        
        public static PreviewSnapshot FromStateSnapshot(StatePreviewSnapshot s, string error = null, bool initialized = true)
        {
            return new PreviewSnapshot
            {
                NormalizedTime = s.NormalizedTime,
                BlendPosition = s.BlendPosition,
                BlendWeights = s.BlendWeights,
                TransitionProgress = s.TransitionProgress,
                IsPlaying = s.IsPlaying,
                ErrorMessage = error,
                IsInitialized = initialized
            };
        }
    }
    
    #endregion
}
