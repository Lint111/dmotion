using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Type of animation state.
    /// At runtime, all states are either Single or LinearBlend.
    /// SubStateMachine states are flattened during conversion (visual-only hierarchy).
    /// </summary>
    public enum StateType : byte
    {
        Single = 0,
        LinearBlend = 1,
    }

    internal struct AnimationStateBlob
    {
        internal StateType Type;
        internal ushort StateIndex; // Index into SingleClipStates, LinearBlendStates, or SubStateMachines
        internal bool Loop;
        internal float Speed;
        internal ushort SpeedParameterIndex; // Parameter index for speed multiplier (ushort.MaxValue = no parameter)
        internal BlobArray<StateOutTransitionGroup> Transitions;

        /// <summary>
        /// Index into StateMachineBlob.ExitTransitionGroups.
        /// -1 means this state is not an exit state (no exit transitions to evaluate).
        /// >= 0 means this state can trigger exit transitions from its parent sub-machine.
        /// </summary>
        internal short ExitTransitionGroupIndex;
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