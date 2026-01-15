using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Type of animation state.
    /// </summary>
    public enum StateType : byte
    {
        Single = 0,
        LinearBlend = 1,
        SubStateMachine = 2, // NEW: State containing nested state machine
    }

    internal struct AnimationStateBlob
    {
        internal StateType Type;
        internal ushort StateIndex; // Index into SingleClipStates, LinearBlendStates, or SubStateMachines
        internal bool Loop;
        internal float Speed;
        internal ushort SpeedParameterIndex; // Parameter index for speed multiplier (ushort.MaxValue = no parameter)
        internal BlobArray<StateOutTransitionGroup> Transitions;
    }
    
    internal struct SingleClipStateBlob
    {
        internal ushort ClipIndex;
    }

    internal struct LinearBlendStateBlob
    {
        internal BlobArray<int> SortedClipIndexes;
        internal BlobArray<float> SortedClipThresholds;
        internal BlobArray<float> SortedClipSpeeds;
        internal ushort BlendParameterIndex;
    }
}