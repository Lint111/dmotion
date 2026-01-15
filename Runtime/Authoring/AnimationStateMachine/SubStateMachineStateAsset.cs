using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Authoring asset for a sub-state machine (state containing a nested state machine).
    /// Supports unlimited depth through natural relationship-based connections.
    /// </summary>
    public class SubStateMachineStateAsset : AnimationStateAsset
    {
        [Tooltip("The nested state machine contained within this state")]
        public StateMachineAsset NestedStateMachine;

        [Tooltip("The entry state to transition to when entering this sub-machine")]
        public AnimationStateAsset EntryState;

        [Header("Exit Transitions")]
        [Tooltip("Transitions to evaluate when exiting the sub-machine (triggered by special exit states)")]
        public List<StateOutTransition> ExitTransitions = new();

        public override StateType Type => StateType.SubStateMachine;

        // Aggregate clip count from nested machine
        public override int ClipCount => NestedStateMachine != null ? NestedStateMachine.ClipCount : 0;

        // Aggregate clips from nested machine
        public override IEnumerable<AnimationClipAsset> Clips =>
            NestedStateMachine != null ? NestedStateMachine.Clips : Enumerable.Empty<AnimationClipAsset>();

        /// <summary>
        /// Validates that the nested machine and entry state are properly configured.
        /// </summary>
        public bool IsValid()
        {
            if (NestedStateMachine == null)
                return false;

            if (EntryState == null)
                return false;

            // Entry state must be in the nested machine's states
            if (!NestedStateMachine.States.Contains(EntryState))
                return false;

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-set entry state to default if not set
            if (NestedStateMachine != null && EntryState == null)
            {
                EntryState = NestedStateMachine.DefaultState;
            }

            // Warn if entry state is not in nested machine
            if (NestedStateMachine != null && EntryState != null)
            {
                if (!NestedStateMachine.States.Contains(EntryState))
                {
                    Debug.LogWarning($"SubStateMachine '{name}': EntryState '{EntryState.name}' is not in NestedStateMachine '{NestedStateMachine.name}'", this);
                }
            }
        }
#endif
    }
}
