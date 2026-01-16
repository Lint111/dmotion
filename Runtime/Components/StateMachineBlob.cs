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
    /// </summary>
    internal struct SubStateMachineBlob
    {
        /// <summary>
        /// Reference to nested state machine blob.
        /// Contains states, transitions, parameters, and potentially more sub-machines (recursive).
        /// Stored as a reference rather than inline to allow recursive structures.
        /// </summary>
        internal BlobAssetReference<StateMachineBlob> NestedStateMachine;

        /// <summary>
        /// Index of the entry state within the nested machine.
        /// This is the default state to transition to when entering the sub-machine.
        /// Analogous to StateMachineBlob.DefaultStateIndex for the nested machine.
        /// </summary>
        internal short EntryStateIndex;

        /// <summary>
        /// Transitions to evaluate when exiting the sub-machine.
        /// Triggered when a state within the nested machine reaches "exit".
        /// Evaluated at the parent level (where this sub-machine node exists).
        /// </summary>
        internal BlobArray<StateOutTransitionGroup> ExitTransitions;

        /// <summary>
        /// Name of this sub-machine (for debugging).
        /// Helps identify which sub-machine is active during runtime debugging.
        /// </summary>
        internal FixedString64Bytes Name;
    }
}