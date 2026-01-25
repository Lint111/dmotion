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
    /// 
    /// Exit behavior: The NestedStateMachine defines its own ExitStates (states that can trigger exit).
    /// When used as a nested machine, those exit states trigger the OutTransitions on this SubStateMachine.
    /// </summary>
    public class SubStateMachineStateAsset : AnimationStateAsset
    {
        [Tooltip("The nested state machine contained within this state")]
        public StateMachineAsset NestedStateMachine;

        [Tooltip("The entry state to transition to when entering this sub-machine")]
        public AnimationStateAsset EntryState;

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
            // Check for circular reference - get parent state machine
            var parentMachine = GetParentStateMachine();
            if (NestedStateMachine != null && parentMachine != null)
            {
                if (NestedStateMachine == parentMachine || ContainsStateMachineRecursive(NestedStateMachine, parentMachine))
                {
                    Debug.LogError($"SubStateMachine '{name}': Circular reference detected! Clearing NestedStateMachine.", this);
                    NestedStateMachine = null;
                    return;
                }
            }
            
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

        private StateMachineAsset GetParentStateMachine()
        {
            var path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path))
                return null;
            return UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) as StateMachineAsset;
        }

        private static bool ContainsStateMachineRecursive(StateMachineAsset machine, StateMachineAsset target, HashSet<StateMachineAsset> visited = null)
        {
            if (machine == null || target == null)
                return false;
            
            visited ??= new HashSet<StateMachineAsset>();
            if (visited.Contains(machine))
                return false;
            visited.Add(machine);

            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subState && subState.NestedStateMachine != null)
                {
                    if (subState.NestedStateMachine == target)
                        return true;
                    
                    if (ContainsStateMachineRecursive(subState.NestedStateMachine, target, visited))
                        return true;
                }
            }

            return false;
        }
#endif
    }
}
