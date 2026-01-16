using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Runtime blob for a state machine.
    /// All states are flattened - SubStateMachine hierarchy is visual-only (like Unity Mecanim).
    /// </summary>
    public struct StateMachineBlob
    {
        internal short DefaultStateIndex;
        internal BlobArray<AnimationStateBlob> States;
        internal BlobArray<SingleClipStateBlob> SingleClipStates;
        internal BlobArray<LinearBlendStateBlob> LinearBlendStates;

        /// <summary>
        /// Global transitions that can be taken from any state.
        /// Evaluated before regular state transitions.
        /// Empty array if no Any State transitions exist.
        /// </summary>
        internal BlobArray<AnyStateTransition> AnyStateTransitions;

        /// <summary>
        /// Exit transition groups for sub-state machines.
        /// When a state has ExitTransitionGroupIndex >= 0, its exit transitions are evaluated
        /// after normal and any-state transitions.
        /// </summary>
        internal BlobArray<ExitTransitionGroup> ExitTransitionGroups;
    }

    /// <summary>
    /// Groups exit transitions for a sub-state machine.
    /// Exit states (designated by SubStateMachineStateAsset.ExitStates) can trigger these transitions
    /// to leave the conceptual sub-machine group.
    /// </summary>
    public struct ExitTransitionGroup
    {
        /// <summary>
        /// Flattened indices of states that can trigger this group's exit transitions.
        /// Only these states will evaluate the exit transitions.
        /// </summary>
        internal BlobArray<short> ExitStateIndices;

        /// <summary>
        /// Exit transitions for this group.
        /// Evaluated when the current state is in ExitStateIndices.
        /// </summary>
        internal BlobArray<StateOutTransitionGroup> ExitTransitions;
    }
}