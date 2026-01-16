using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Authoring asset for a sub-state machine (state containing a nested state machine).
    /// Visual-only organization (like Unity Mecanim) - flattened during conversion.
    /// At runtime, all nested states become top-level states with remapped indices.
    /// </summary>
    public class SubStateMachineStateAsset : AnimationStateAsset
    {
        [Tooltip("The nested state machine contained within this state")]
        public StateMachineAsset NestedStateMachine;

        [Tooltip("The entry state to transition to when entering this sub-machine")]
        public AnimationStateAsset EntryState;

        [Header("Exit Transitions")]
        [Tooltip("States that can trigger exit transitions (must be in NestedStateMachine)")]
        public List<AnimationStateAsset> ExitStates = new();

        [Tooltip("Transitions to evaluate when exiting the sub-machine (triggered by exit states)")]
        public List<StateOutTransition> ExitTransitions = new();

        /// <summary>
        /// SubStateMachine states are flattened during conversion and have no runtime type.
        /// This property should never be accessed at runtime.
        /// </summary>
        public override StateType Type => throw new InvalidOperationException(
            "SubStateMachineStateAsset.Type should never be accessed - these are flattened during conversion. " +
            "Use StateFlattener to get the actual leaf states.");

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

            // Validate exit states are in nested machine
            if (NestedStateMachine != null && ExitStates != null)
            {
                for (int i = ExitStates.Count - 1; i >= 0; i--)
                {
                    var exitState = ExitStates[i];
                    if (exitState == null)
                        continue;

                    if (!IsStateInNestedMachine(exitState))
                    {
                        Debug.LogWarning($"SubStateMachine '{name}': ExitState '{exitState.name}' is not in NestedStateMachine '{NestedStateMachine.name}'", this);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a state is in the nested machine (recursively handles nested sub-machines).
        /// </summary>
        private bool IsStateInNestedMachine(AnimationStateAsset state)
        {
            if (NestedStateMachine == null)
                return false;

            return IsStateInMachineRecursive(state, NestedStateMachine);
        }

        private static bool IsStateInMachineRecursive(AnimationStateAsset state, StateMachineAsset machine)
        {
            foreach (var s in machine.States)
            {
                if (s == state)
                    return true;

                if (s is SubStateMachineStateAsset nestedSub && nestedSub.NestedStateMachine != null)
                {
                    if (IsStateInMachineRecursive(state, nestedSub.NestedStateMachine))
                        return true;
                }
            }
            return false;
        }
#endif
    }
}
