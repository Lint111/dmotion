using Unity.Collections;
using Unity.Entities;

namespace DMotion
{
    public struct StateMachineBlob
    {
        internal short DefaultStateIndex;
        internal BlobArray<AnimationStateBlob> States;
        internal BlobArray<SingleClipStateBlob> SingleClipStates;
        internal BlobArray<LinearBlendStateBlob> LinearBlendStates;

        /// <summary>
        /// Sub-state machines (states containing nested state machines).
        /// Indexed by AnimationStateBlob.TypeIndex when Type == SubStateMachine.
        /// Empty array if no sub-state machines exist.
        /// </summary>
        internal BlobArray<SubStateMachineBlob> SubStateMachines;

        /// <summary>
        /// Global transitions that can be taken from any state.
        /// Evaluated before regular state transitions.
        /// Empty array if no Any State transitions exist.
        /// </summary>
        internal BlobArray<AnyStateTransition> AnyStateTransitions;

        /// <summary>
        /// Total number of states across all levels (including nested).
        /// Useful for validation and debugging.
        /// </summary>
        internal short TotalStateCount;
    }

    /// <summary>
    /// Runtime blob for a sub-state machine (state containing nested states).
    /// Stores the nested state machine data and entry/exit configuration.
    ///
    /// NOTE: Sub-state machines are WIP. The architecture needs redesign because
    /// BlobAssetReference cannot be stored inside blobs (contains pointers).
    /// Options: 1) Use BlobPtr with same-builder construction, 2) Flatten hierarchy,
    /// 3) Store refs on component instead of blob.
    /// </summary>
    internal struct SubStateMachineBlob
    {
        // TODO: Implement proper nested blob structure using BlobPtr<StateMachineBlob>
        // For now, sub-state machines are not supported at runtime.

        /// <summary>
        /// Index of the entry state within the nested machine.
        /// </summary>
        internal short EntryStateIndex;

        /// <summary>
        /// Transitions to evaluate when exiting the sub-machine.
        /// </summary>
        internal BlobArray<StateOutTransitionGroup> ExitTransitions;

        /// <summary>
        /// Name of this sub-machine (for debugging).
        /// </summary>
        internal FixedString64Bytes Name;
    }
}