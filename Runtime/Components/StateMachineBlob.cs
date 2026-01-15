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
        /// Global transitions that can be taken from any state.
        /// Evaluated before regular state transitions.
        /// Empty array if no Any State transitions exist.
        /// </summary>
        internal BlobArray<AnyStateTransition> AnyStateTransitions;
    }
}