using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Flattens hierarchical state machines into a single flat list of states.
    /// SubStateMachine states are visual-only (like Unity Mecanim) - their nested states
    /// are inlined into the parent, and transitions targeting them redirect to entry states.
    /// </summary>
    internal struct FlattenedState
    {
        /// <summary>Original state asset (Single or LinearBlend only)</summary>
        internal AnimationStateAsset Asset;

        /// <summary>Global index in the flattened state list</summary>
        internal int GlobalIndex;

        /// <summary>Offset to add to clip indices for this state's clips</summary>
        internal int ClipIndexOffset;

        /// <summary>Path for debugging (e.g., "Base/Locomotion/Walk")</summary>
        internal string Path;

        /// <summary>
        /// Index into the exit transition groups array.
        /// -1 = not an exit state, >= 0 = exit state for that group.
        /// </summary>
        internal short ExitGroupIndex;

        /// <summary>Whether this state is an exit state for its parent sub-machine.</summary>
        internal bool IsExitState => ExitGroupIndex >= 0;
    }

    /// <summary>
    /// Information about a sub-state machine's exit transitions (collected during flattening).
    /// </summary>
    internal struct ExitTransitionInfo
    {
        /// <summary>The sub-state machine asset this info belongs to.</summary>
        internal SubStateMachineStateAsset SubMachine;

        /// <summary>Flattened indices of states that can trigger exit transitions.</summary>
        internal List<int> ExitStateIndices;

        /// <summary>Exit transitions defined on the sub-machine.</summary>
        internal List<StateOutTransition> ExitTransitions;
    }

    /// <summary>
    /// Handles flattening of hierarchical state machines.
    /// All nested states become top-level states with remapped clip and transition indices.
    /// </summary>
    internal static class StateFlattener
    {
        /// <summary>
        /// Flattens a state machine hierarchy into a single list of leaf states.
        /// Returns the flattened states, a mapping from original assets to global indices,
        /// and exit transition info for sub-state machines.
        /// </summary>
        internal static (List<FlattenedState> states, Dictionary<AnimationStateAsset, int> assetToIndex, List<ExitTransitionInfo> exitTransitionInfos)
            FlattenStates(StateMachineAsset rootMachine)
        {
            var flattenedStates = new List<FlattenedState>();
            var assetToIndex = new Dictionary<AnimationStateAsset, int>();
            var subMachineToEntry = new Dictionary<SubStateMachineStateAsset, AnimationStateAsset>();
            var exitTransitionInfos = new List<ExitTransitionInfo>();

            // First pass: collect all leaf states and build mappings
            CollectStatesRecursive(
                rootMachine,
                "",
                0,
                flattenedStates,
                assetToIndex,
                subMachineToEntry,
                exitTransitionInfos,
                parentSubMachine: null);

            // Second pass: assign exit group indices to flattened states
            AssignExitGroupIndices(flattenedStates, assetToIndex, exitTransitionInfos);

            return (flattenedStates, assetToIndex, exitTransitionInfos);
        }

        /// <summary>
        /// Assigns exit group indices to flattened states based on exit transition info.
        /// </summary>
        private static void AssignExitGroupIndices(
            List<FlattenedState> flattenedStates,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            List<ExitTransitionInfo> exitTransitionInfos)
        {
            // Build a mapping from flattened index to exit group index
            var indexToExitGroup = new Dictionary<int, short>();

            for (short groupIndex = 0; groupIndex < exitTransitionInfos.Count; groupIndex++)
            {
                var info = exitTransitionInfos[groupIndex];
                foreach (var exitStateIndex in info.ExitStateIndices)
                {
                    indexToExitGroup[exitStateIndex] = groupIndex;
                }
            }

            // Update flattened states with exit group indices
            for (int i = 0; i < flattenedStates.Count; i++)
            {
                var state = flattenedStates[i];
                if (indexToExitGroup.TryGetValue(state.GlobalIndex, out var exitGroupIndex))
                {
                    state.ExitGroupIndex = exitGroupIndex;
                    flattenedStates[i] = state;
                }
            }
        }

        /// <summary>
        /// Resolves the target state for a transition, handling SubStateMachine redirection.
        /// If the target is a SubStateMachine, returns the entry state's global index.
        /// </summary>
        internal static int ResolveTransitionTarget(
            AnimationStateAsset targetState,
            Dictionary<AnimationStateAsset, int> assetToIndex)
        {
            // If target is a leaf state, return its index directly
            if (assetToIndex.TryGetValue(targetState, out var index))
            {
                return index;
            }

            // If target is a SubStateMachine, find and return entry state index
            if (targetState is SubStateMachineStateAsset subMachine)
            {
                var entryState = ResolveEntryState(subMachine);
                if (assetToIndex.TryGetValue(entryState, out var entryIndex))
                {
                    return entryIndex;
                }

                Debug.LogError($"[StateFlattener] Could not resolve entry state for SubStateMachine '{subMachine.name}'");
                return 0;
            }

            Debug.LogError($"[StateFlattener] Unknown state type: {targetState?.GetType().Name ?? "null"}");
            return 0;
        }

        /// <summary>
        /// Recursively resolves the entry state of a SubStateMachine.
        /// If the entry state is itself a SubStateMachine, continues recursively.
        /// </summary>
        internal static AnimationStateAsset ResolveEntryState(SubStateMachineStateAsset subMachine)
        {
            if (subMachine.EntryState == null)
            {
                Debug.LogError($"[StateFlattener] SubStateMachine '{subMachine.name}' has null EntryState");
                return null;
            }

            // If entry state is another SubStateMachine, recurse
            if (subMachine.EntryState is SubStateMachineStateAsset nestedSubMachine)
            {
                return ResolveEntryState(nestedSubMachine);
            }

            // Return the leaf entry state
            return subMachine.EntryState;
        }

        /// <summary>
        /// Collects all clips from a state machine hierarchy in traversal order.
        /// Used to build the unified clip list for baking.
        /// </summary>
        internal static List<AnimationClipAsset> CollectAllClips(StateMachineAsset rootMachine)
        {
            var clips = new List<AnimationClipAsset>();
            CollectClipsRecursive(rootMachine, clips);
            return clips;
        }

        /// <summary>
        /// Finds the default state, resolving SubStateMachine to entry state if needed.
        /// </summary>
        internal static AnimationStateAsset ResolveDefaultState(StateMachineAsset machine)
        {
            if (machine.DefaultState is SubStateMachineStateAsset subMachine)
            {
                return ResolveEntryState(subMachine);
            }
            return machine.DefaultState;
        }

        private static void CollectStatesRecursive(
            StateMachineAsset machine,
            string pathPrefix,
            int clipIndexOffset,
            List<FlattenedState> flattenedStates,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            Dictionary<SubStateMachineStateAsset, AnimationStateAsset> subMachineToEntry,
            List<ExitTransitionInfo> exitTransitionInfos,
            SubStateMachineStateAsset parentSubMachine)
        {
            foreach (var state in machine.States)
            {
                var statePath = string.IsNullOrEmpty(pathPrefix)
                    ? state.name
                    : $"{pathPrefix}/{state.name}";

                switch (state)
                {
                    case SubStateMachineStateAsset subMachine:
                        // Validate the sub-machine
                        if (!subMachine.IsValid())
                        {
                            Debug.LogWarning($"[StateFlattener] Skipping invalid SubStateMachine: {statePath}");
                            continue;
                        }

                        // Record the entry state mapping for transition resolution
                        var entryState = ResolveEntryState(subMachine);
                        if (entryState != null)
                        {
                            subMachineToEntry[subMachine] = entryState;
                        }

                        // Recurse into nested machine - clips before this point determine offset
                        var nestedClipOffset = clipIndexOffset + CountClipsBeforeState(machine, state);
                        CollectStatesRecursive(
                            subMachine.NestedStateMachine,
                            statePath,
                            nestedClipOffset,
                            flattenedStates,
                            assetToIndex,
                            subMachineToEntry,
                            exitTransitionInfos,
                            parentSubMachine: subMachine);
                        break;

                    case SingleClipStateAsset:
                    case LinearBlendStateAsset:
                        // Leaf state - add to flattened list
                        var globalIndex = flattenedStates.Count;
                        var stateClipOffset = clipIndexOffset + CountClipsBeforeState(machine, state);

                        flattenedStates.Add(new FlattenedState
                        {
                            Asset = state,
                            GlobalIndex = globalIndex,
                            ClipIndexOffset = stateClipOffset,
                            Path = statePath,
                            ExitGroupIndex = -1 // Will be assigned in second pass
                        });

                        assetToIndex[state] = globalIndex;
                        break;

                    default:
                        Debug.LogError($"[StateFlattener] Unknown state type: {state.GetType().Name}");
                        break;
                }
            }

            // After processing all states in this machine, collect exit transition info
            // if we have a parent sub-machine with exit states/transitions
            if (parentSubMachine != null &&
                parentSubMachine.ExitStates != null &&
                parentSubMachine.ExitStates.Count > 0 &&
                parentSubMachine.ExitTransitions != null &&
                parentSubMachine.ExitTransitions.Count > 0)
            {
                // Only create an exit transition group if we have valid exit states with resolved indices
                var exitStateIndices = new List<int>();
                foreach (var exitState in parentSubMachine.ExitStates)
                {
                    if (exitState == null)
                        continue;

                    // The exit state might be a leaf state or a nested sub-machine's entry state
                    var resolvedExitState = exitState is SubStateMachineStateAsset nestedSub
                        ? ResolveEntryState(nestedSub)
                        : exitState;

                    if (resolvedExitState != null && assetToIndex.TryGetValue(resolvedExitState, out var exitIndex))
                    {
                        exitStateIndices.Add(exitIndex);
                    }
                    else
                    {
                        Debug.LogWarning($"[StateFlattener] Could not resolve exit state '{exitState?.name}' for SubStateMachine '{parentSubMachine.name}'");
                    }
                }

                if (exitStateIndices.Count > 0)
                {
                    exitTransitionInfos.Add(new ExitTransitionInfo
                    {
                        SubMachine = parentSubMachine,
                        ExitStateIndices = exitStateIndices,
                        ExitTransitions = parentSubMachine.ExitTransitions
                    });
                }
            }
        }

        private static void CollectClipsRecursive(StateMachineAsset machine, List<AnimationClipAsset> clips)
        {
            foreach (var state in machine.States)
            {
                switch (state)
                {
                    case SubStateMachineStateAsset subMachine:
                        if (subMachine.IsValid())
                        {
                            CollectClipsRecursive(subMachine.NestedStateMachine, clips);
                        }
                        break;

                    case SingleClipStateAsset:
                    case LinearBlendStateAsset:
                        clips.AddRange(state.Clips);
                        break;
                }
            }
        }

        /// <summary>
        /// Counts clips from states that appear before the given state in the machine's state list.
        /// Used to calculate clip index offsets.
        /// </summary>
        private static int CountClipsBeforeState(StateMachineAsset machine, AnimationStateAsset targetState)
        {
            int count = 0;
            foreach (var state in machine.States)
            {
                if (state == targetState)
                    break;

                count += CountClipsInState(state);
            }
            return count;
        }

        /// <summary>
        /// Counts total clips in a state, including recursively for SubStateMachines.
        /// </summary>
        private static int CountClipsInState(AnimationStateAsset state)
        {
            switch (state)
            {
                case SubStateMachineStateAsset subMachine:
                    if (subMachine.IsValid())
                    {
                        return subMachine.NestedStateMachine.ClipCount;
                    }
                    return 0;

                case SingleClipStateAsset:
                case LinearBlendStateAsset:
                    return state.ClipCount;

                default:
                    return 0;
            }
        }
    }
}
