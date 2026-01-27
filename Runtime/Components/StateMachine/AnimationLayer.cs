using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Blend mode for animation layers.
    /// </summary>
    public enum LayerBlendMode : byte
    {
        /// <summary>Override lower layers (standard blending).</summary>
        Override = 0,
        /// <summary>Add to lower layers (additive blending) - Phase 1D.</summary>
        Additive = 1
    }

    /// <summary>
    /// Animation layer state machine. Multiple layers can exist per entity.
    /// Each layer runs an independent state machine that blends with other layers.
    /// 
    /// Phase 1C: Basic override blending with layer weights.
    /// Phase 1D: Avatar masks for per-bone layer filtering.
    /// 
    /// Inline capacity set to 4 (covers typical use: base, upper body, additive, face).
    /// Exceeding 4 layers triggers heap allocation (still works, minor perf impact).
    /// </summary>
    [BurstCompile]
    [InternalBufferCapacity(4)]
    public struct AnimationStateMachineLayer : IBufferElementData
    {
        /// <summary>Baked skeleton clip set from Latios Kinemation.</summary>
        internal BlobAssetReference<SkeletonClipSetBlob> ClipsBlob;
        /// <summary>Baked clip events.</summary>
        internal BlobAssetReference<ClipEventsBlob> ClipEventsBlob;
        /// <summary>Baked state machine definition.</summary>
        internal BlobAssetReference<StateMachineBlob> StateMachineBlob;
        /// <summary>Current state within this layer's state machine.</summary>
        internal StateMachineStateRef CurrentState;
        
        /// <summary>Layer index (0 = base layer, 1+ = overlay layers).</summary>
        public byte LayerIndex;
        /// <summary>Layer weight (0.0 = no influence, 1.0 = full influence).</summary>
        public float Weight;
        /// <summary>Blend mode for this layer.</summary>
        public LayerBlendMode BlendMode;
        
        /// <summary>
        /// Bone mask for per-bone layer filtering. 
        /// When valid, only masked bones are affected by this layer.
        /// Each bit in the mask corresponds to a bone index.
        /// </summary>
        internal BlobAssetReference<BoneMaskBlob> BoneMask;
        
        /// <summary>Whether this layer has a bone mask (partial body).</summary>
        public bool HasBoneMask => BoneMask.IsCreated;

        #if UNITY_EDITOR || DEBUG
        internal StateMachineStateRef PreviousState;
        #endif

        internal ref AnimationStateBlob CurrentStateBlob
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref StateMachineBlob.Value.States[CurrentState.StateIndex];
        }
        
        /// <summary>Whether this layer has a valid state machine and should be processed.</summary>
        public bool IsValid => StateMachineBlob.IsCreated && StateMachineBlob.Value.States.Length > 0;
    }

    /// <summary>
    /// Per-layer transition state. Matches AnimationStateTransition but with layer tracking.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimationLayerTransition : IBufferElementData
    {
        public byte LayerIndex;
        internal sbyte AnimationStateId;
        internal float TransitionDuration;
        internal float TimeElapsed;
        
        internal short CurveSourceStateIndex;
        internal short CurveSourceTransitionIndex;
        internal TransitionSource CurveSource;

        internal static AnimationLayerTransition Null(byte layerIndex) => new()
        {
            LayerIndex = layerIndex,
            AnimationStateId = -1,
            TimeElapsed = 0,
            CurveSourceStateIndex = -1,
            CurveSourceTransitionIndex = -1,
            CurveSource = TransitionSource.State
        };
        
        internal bool IsValid => AnimationStateId >= 0;
    }

    /// <summary>
    /// Per-layer current state. Matches AnimationCurrentState but with layer tracking.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimationLayerCurrentState : IBufferElementData
    {
        public byte LayerIndex;
        internal sbyte AnimationStateId;
        
        internal bool IsValid => AnimationStateId >= 0;
        
        internal static AnimationLayerCurrentState Null(byte layerIndex) => new()
        {
            LayerIndex = layerIndex,
            AnimationStateId = -1
        };
    }

    /// <summary>
    /// Per-layer transition request. Matches AnimationStateTransitionRequest but with layer tracking.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimationLayerTransitionRequest : IBufferElementData
    {
        public byte LayerIndex;
        internal sbyte AnimationStateId;
        internal float TransitionDuration;
        
        internal short CurveSourceStateIndex;
        internal short CurveSourceTransitionIndex;
        internal TransitionSource CurveSource;

        internal bool IsValid => AnimationStateId >= 0;

        internal static AnimationLayerTransitionRequest Null(byte layerIndex) => new()
        {
            LayerIndex = layerIndex,
            AnimationStateId = -1,
            CurveSourceStateIndex = -1,
            CurveSourceTransitionIndex = -1,
            CurveSource = TransitionSource.State
        };
    }
}
