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

        /// <summary>
        /// The immediate parent SubStateMachine this state came from.
        /// Null if the state is at the root level.
        /// Used for parameter link resolution during blob conversion.
        /// </summary>
        internal SubStateMachineStateAsset SourceSubMachine;

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
    /// Shared context for state flattening operations.
    /// Bundles collections that are passed through recursive calls.
    /// </summary>
    internal class FlatteningContext
    {
        /// <summary>Accumulated flattened states.</summary>
        internal List<FlattenedState> FlattenedStates { get; }
        
        /// <summary>Mapping from original asset to global index.</summary>
        internal Dictionary<AnimationStateAsset, int> AssetToIndex { get; }
        
        /// <summary>Mapping from SubStateMachine to its resolved entry state.</summary>
        internal Dictionary<SubStateMachineStateAsset, AnimationStateAsset> SubMachineToEntry { get; }
        
        /// <summary>Exit transition info for sub-state machines.</summary>
        internal List<ExitTransitionInfo> ExitTransitionInfos { get; }

        internal FlatteningContext()
        {
            FlattenedStates = new List<FlattenedState>();
            AssetToIndex = new Dictionary<AnimationStateAsset, int>();
            SubMachineToEntry = new Dictionary<SubStateMachineStateAsset, AnimationStateAsset>();
            ExitTransitionInfos = new List<ExitTransitionInfo>();
        }
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
            var context = new FlatteningContext();

            // First pass: collect all leaf states and build mappings
            CollectStatesRecursive(rootMachine, "", 0, context, parentSubMachine: null);

            // Second pass: assign exit group indices to flattened states
            AssignExitGroupIndices(context);

            return (context.FlattenedStates, context.AssetToIndex, context.ExitTransitionInfos);
        }

        /// <summary>
        /// Assigns exit group indices to flattened states based on exit transition info.
        /// </summary>
        private static void AssignExitGroupIndices(FlatteningContext context)
        {
            // Build a mapping from flattened index to exit group index
            var indexToExitGroup = new Dictionary<int, short>();

            for (short groupIndex = 0; groupIndex < context.ExitTransitionInfos.Count; groupIndex++)
            {
                var info = context.ExitTransitionInfos[groupIndex];
                foreach (var exitStateIndex in info.ExitStateIndices)
                {
                    indexToExitGroup[exitStateIndex] = groupIndex;
                }
            }

            // Update flattened states with exit group indices
            for (int i = 0; i < context.FlattenedStates.Count; i++)
            {
                var state = context.FlattenedStates[i];
                if (indexToExitGroup.TryGetValue(state.GlobalIndex, out var exitGroupIndex))
                {
                    state.ExitGroupIndex = exitGroupIndex;
                    context.FlattenedStates[i] = state;
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
            FlatteningContext context,
            SubStateMachineStateAsset parentSubMachine)
        {
            foreach (var state in machine.States)
            {
                var statePath = BuildStatePath(pathPrefix, state.name);
                ProcessState(state, statePath, machine, clipIndexOffset, context, parentSubMachine);
            }

            // After processing all states, collect exit transition info for this machine
            CollectExitTransitionInfo(machine, parentSubMachine, context);
        }

        private static string BuildStatePath(string pathPrefix, string stateName)
        {
            return string.IsNullOrEmpty(pathPrefix) ? stateName : $"{pathPrefix}/{stateName}";
        }

        private static void ProcessState(
            AnimationStateAsset state,
            string statePath,
            StateMachineAsset machine,
            int clipIndexOffset,
            FlatteningContext context,
            SubStateMachineStateAsset parentSubMachine)
        {
            if (state is SubStateMachineStateAsset subMachine)
            {
                ProcessSubStateMachine(subMachine, statePath, machine, clipIndexOffset, context);
                return;
            }

            if (state is SingleClipStateAsset or LinearBlendStateAsset or Directional2DBlendStateAsset)
            {
                ProcessLeafState(state, statePath, machine, clipIndexOffset, context, parentSubMachine);
                return;
            }

            Debug.LogError($"[StateFlattener] Unknown state type: {state.GetType().Name}");
        }

        private static void ProcessSubStateMachine(
            SubStateMachineStateAsset subMachine,
            string statePath,
            StateMachineAsset machine,
            int clipIndexOffset,
            FlatteningContext context)
        {
            if (!subMachine.IsValid())
            {
                Debug.LogWarning($"[StateFlattener] Skipping invalid SubStateMachine: {statePath}");
                return;
            }

            // Record the entry state mapping for transition resolution
            var entryState = ResolveEntryState(subMachine);
            if (entryState != null)
            {
                context.SubMachineToEntry[subMachine] = entryState;
            }

            // Recurse into nested machine - clips before this point determine offset
            var nestedClipOffset = clipIndexOffset + CountClipsBeforeState(machine, subMachine);
            CollectStatesRecursive(
                subMachine.NestedStateMachine,
                statePath,
                nestedClipOffset,
                context,
                parentSubMachine: subMachine);
        }

        private static void ProcessLeafState(
            AnimationStateAsset state,
            string statePath,
            StateMachineAsset machine,
            int clipIndexOffset,
            FlatteningContext context,
            SubStateMachineStateAsset parentSubMachine)
        {
            var globalIndex = context.FlattenedStates.Count;
            var stateClipOffset = clipIndexOffset + CountClipsBeforeState(machine, state);

            context.FlattenedStates.Add(new FlattenedState
            {
                Asset = state,
                GlobalIndex = globalIndex,
                ClipIndexOffset = stateClipOffset,
                Path = statePath,
                ExitGroupIndex = -1, // Will be assigned in second pass
                SourceSubMachine = parentSubMachine // Track source for parameter linking
            });

            context.AssetToIndex[state] = globalIndex;
        }

        /// <summary>
        /// Collects exit transition info if this machine has exit states and the parent has out-transitions.
        /// </summary>
        private static void CollectExitTransitionInfo(
            StateMachineAsset machine,
            SubStateMachineStateAsset parentSubMachine,
            FlatteningContext context)
        {
            // Early returns for invalid conditions
            if (parentSubMachine == null) return;
            if (machine.ExitStates == null || machine.ExitStates.Count == 0) return;
            if (parentSubMachine.OutTransitions == null || parentSubMachine.OutTransitions.Count == 0) return;

            var exitStateIndices = ResolveExitStateIndices(machine.ExitStates, context.AssetToIndex, parentSubMachine.name);
            if (exitStateIndices.Count == 0) return;

            context.ExitTransitionInfos.Add(new ExitTransitionInfo
            {
                SubMachine = parentSubMachine,
                ExitStateIndices = exitStateIndices,
                ExitTransitions = parentSubMachine.OutTransitions
            });
        }

        /// <summary>
        /// Resolves exit states to their flattened indices.
        /// </summary>
        private static List<int> ResolveExitStateIndices(
            List<AnimationStateAsset> exitStates,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            string parentName)
        {
            var exitStateIndices = new List<int>();

            foreach (var exitState in exitStates)
            {
                if (exitState == null) continue;

                // The exit state might be a leaf state or a nested sub-machine's entry state
                var resolvedExitState = exitState is SubStateMachineStateAsset nestedSub
                    ? ResolveEntryState(nestedSub)
                    : exitState;

                if (resolvedExitState == null || !assetToIndex.TryGetValue(resolvedExitState, out var exitIndex))
                {
                    Debug.LogWarning($"[StateFlattener] Could not resolve exit state '{exitState.name}' for SubStateMachine '{parentName}'");
                    continue;
                }

                exitStateIndices.Add(exitIndex);
            }

            return exitStateIndices;
        }

        private static void CollectClipsRecursive(StateMachineAsset machine, List<AnimationClipAsset> clips)
        {
            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subMachine)
                {
                    if (subMachine.IsValid())
                        CollectClipsRecursive(subMachine.NestedStateMachine, clips);
                    continue;
                }

                // All leaf state types (Single, LinearBlend, Directional2D)
                if (state is SingleClipStateAsset or LinearBlendStateAsset or Directional2DBlendStateAsset)
                {
                    clips.AddRange(state.Clips);
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
            if (state is SubStateMachineStateAsset subMachine)
            {
                return subMachine.IsValid() ? subMachine.NestedStateMachine.ClipCount : 0;
            }

            // All leaf state types return their clip count
            if (state is SingleClipStateAsset or LinearBlendStateAsset or Directional2DBlendStateAsset)
            {
                return state.ClipCount;
            }

            return 0;
        }
    }
}
