using System.Collections.Generic;
using DMotion.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Test utilities for creating and testing sub-state machines.
    /// </summary>
    public static class SubStateMachineTestUtils
    {
        #region Asset Builder Extensions

        /// <summary>
        /// Creates a sub-state machine with a nested state machine containing the specified states.
        /// Note: Exit states are now defined on the NestedStateMachine.ExitStates, not on SubStateMachine.
        /// Exit transitions are just OutTransitions on the SubStateMachine.
        /// </summary>
        public static SubStateMachineStateAsset AddSubStateMachine(
            this ref AnimationStateMachineAssetBuilder builder,
            StateMachineAsset nestedMachine,
            string name = "SubStateMachine")
        {
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.name = name;
            subMachine.NestedStateMachine = nestedMachine;
            subMachine.EntryState = nestedMachine.DefaultState;
            subMachine.OutTransitions = new List<StateOutTransition>();
            
            // Ensure nested machine has ExitStates list initialized
            nestedMachine.ExitStates ??= new List<AnimationStateAsset>();

            builder.AddExistingState(subMachine);
            return subMachine;
        }

        /// <summary>
        /// Adds an existing state to the state machine (used for sub-state machines).
        /// </summary>
        public static void AddExistingState(this ref AnimationStateMachineAssetBuilder builder, AnimationStateAsset state)
        {
            // Access the internal state machine asset and add the state
            var asset = builder.Build();
            asset.States.Add(state);
        }

        /// <summary>
        /// Creates a simple nested state machine with the specified number of states.
        /// </summary>
        public static StateMachineAsset CreateNestedStateMachine(int stateCount, string namePrefix = "NestedState")
        {
            var nested = ScriptableObject.CreateInstance<StateMachineAsset>();
            nested.States = new List<AnimationStateAsset>();
            nested.Parameters = new List<AnimationParameterAsset>();
            nested.ExitStates = new List<AnimationStateAsset>(); // Initialize ExitStates

            for (int i = 0; i < stateCount; i++)
            {
                var state = ScriptableObject.CreateInstance<SingleClipStateAsset>();
                state.name = $"{namePrefix}_{i}";
                state.Loop = true;
                state.Speed = 1f;
                state.OutTransitions = new List<StateOutTransition>();
                nested.States.Add(state);

                if (i == 0)
                {
                    nested.DefaultState = state;
                }
            }

            return nested;
        }

        /// <summary>
        /// Creates a nested hierarchy with the specified depth.
        /// Each level contains 2 leaf states and optionally a sub-machine to the next level.
        /// </summary>
        public static StateMachineAsset CreateNestedHierarchy(int depth, string namePrefix = "Level")
        {
            return CreateNestedHierarchyRecursive(depth, 0, namePrefix);
        }

        private static StateMachineAsset CreateNestedHierarchyRecursive(int maxDepth, int currentDepth, string namePrefix)
        {
            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.name = $"{namePrefix}_{currentDepth}";
            machine.States = new List<AnimationStateAsset>();
            machine.Parameters = new List<AnimationParameterAsset>();

            // Add two leaf states
            var state1 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state1.name = $"{namePrefix}_{currentDepth}_State1";
            state1.Loop = true;
            state1.Speed = 1f;
            state1.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(state1);
            machine.DefaultState = state1;

            var state2 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state2.name = $"{namePrefix}_{currentDepth}_State2";
            state2.Loop = true;
            state2.Speed = 1f;
            state2.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(state2);

            // Add sub-machine if not at max depth
            if (currentDepth < maxDepth - 1)
            {
                var nestedMachine = CreateNestedHierarchyRecursive(maxDepth, currentDepth + 1, namePrefix);
                nestedMachine.ExitStates = new List<AnimationStateAsset>();

                var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
                subMachine.name = $"{namePrefix}_{currentDepth}_SubMachine";
                subMachine.NestedStateMachine = nestedMachine;
                subMachine.EntryState = nestedMachine.DefaultState;
                subMachine.OutTransitions = new List<StateOutTransition>();
                machine.States.Add(subMachine);
            }

            return machine;
        }

        #endregion

        #region Exit State Configuration

        /// <summary>
        /// Marks a state as an exit state for its parent sub-machine.
        /// NEW ARCHITECTURE: Exit states are now stored on the nested StateMachineAsset, not on SubStateMachine.
        /// </summary>
        public static void MarkAsExitState(SubStateMachineStateAsset subMachine, AnimationStateAsset exitState)
        {
            subMachine.NestedStateMachine.ExitStates ??= new List<AnimationStateAsset>();
            if (!subMachine.NestedStateMachine.ExitStates.Contains(exitState))
            {
                subMachine.NestedStateMachine.ExitStates.Add(exitState);
            }
        }

        /// <summary>
        /// Adds an exit transition from the sub-machine to a target state in the parent machine.
        /// NEW ARCHITECTURE: Exit transitions are just OutTransitions on the SubStateMachine.
        /// </summary>
        public static StateOutTransition AddExitTransition(
            SubStateMachineStateAsset subMachine,
            AnimationStateAsset targetState,
            float transitionDuration = 0.1f)
        {
            var transition = new StateOutTransition(targetState, transitionDuration);
            subMachine.OutTransitions ??= new List<StateOutTransition>();
            subMachine.OutTransitions.Add(transition);
            return transition;
        }

        #endregion

        #region Assertions

        /// <summary>
        /// Asserts that the flattened state list has the expected count.
        /// </summary>
        public static void AssertFlattenedStateCount(StateMachineAsset machine, int expectedCount)
        {
            var (flattenedStates, _, _) = StateFlattener.FlattenStates(machine);
            Assert.AreEqual(expectedCount, flattenedStates.Count,
                $"Expected {expectedCount} flattened states, got {flattenedStates.Count}");
        }

        /// <summary>
        /// Asserts that a state has the expected path in the flattened list.
        /// </summary>
        public static void AssertStatePath(StateMachineAsset machine, AnimationStateAsset state, string expectedPath)
        {
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            Assert.IsTrue(assetToIndex.ContainsKey(state),
                $"State '{state.name}' not found in flattened state list");

            var index = assetToIndex[state];
            var flatState = flattenedStates[index];
            Assert.AreEqual(expectedPath, flatState.Path,
                $"Expected path '{expectedPath}', got '{flatState.Path}'");
        }

        /// <summary>
        /// Asserts that a state is marked as an exit state with the expected group index.
        /// </summary>
        public static void AssertIsExitState(StateMachineAsset machine, AnimationStateAsset state, short expectedGroupIndex)
        {
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            Assert.IsTrue(assetToIndex.ContainsKey(state),
                $"State '{state.name}' not found in flattened state list");

            var index = assetToIndex[state];
            var flatState = flattenedStates[index];
            Assert.AreEqual(expectedGroupIndex, flatState.ExitGroupIndex,
                $"Expected exit group index {expectedGroupIndex}, got {flatState.ExitGroupIndex}");
        }

        /// <summary>
        /// Asserts that a state is NOT an exit state.
        /// </summary>
        public static void AssertNotExitState(StateMachineAsset machine, AnimationStateAsset state)
        {
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            Assert.IsTrue(assetToIndex.ContainsKey(state),
                $"State '{state.name}' not found in flattened state list");

            var index = assetToIndex[state];
            var flatState = flattenedStates[index];
            Assert.AreEqual(-1, flatState.ExitGroupIndex,
                $"State '{state.name}' should not be an exit state, but has group index {flatState.ExitGroupIndex}");
        }

        /// <summary>
        /// Asserts that a transition targeting a sub-machine resolves to the entry state.
        /// </summary>
        public static void AssertTransitionResolvesToEntryState(
            StateMachineAsset machine,
            SubStateMachineStateAsset subMachine)
        {
            var (_, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            var resolvedIndex = StateFlattener.ResolveTransitionTarget(subMachine, assetToIndex);
            var entryState = StateFlattener.ResolveEntryState(subMachine);

            Assert.IsTrue(assetToIndex.ContainsKey(entryState),
                $"Entry state '{entryState.name}' not found in flattened state list");

            var expectedIndex = assetToIndex[entryState];
            Assert.AreEqual(expectedIndex, resolvedIndex,
                $"Transition to sub-machine should resolve to entry state index {expectedIndex}, got {resolvedIndex}");
        }

        /// <summary>
        /// Asserts that the total clip count is correct for a machine with sub-machines.
        /// </summary>
        public static void AssertTotalClipCount(StateMachineAsset machine, int expectedCount)
        {
            var clips = StateFlattener.CollectAllClips(machine);
            Assert.AreEqual(expectedCount, clips.Count,
                $"Expected {expectedCount} clips, got {clips.Count}");
        }

        #endregion

        #region State Machine Validation

        /// <summary>
        /// Validates that a sub-state machine is properly configured.
        /// </summary>
        public static void ValidateSubStateMachine(SubStateMachineStateAsset subMachine)
        {
            Assert.IsNotNull(subMachine.NestedStateMachine,
                $"SubStateMachine '{subMachine.name}' has null NestedStateMachine");

            Assert.IsNotNull(subMachine.EntryState,
                $"SubStateMachine '{subMachine.name}' has null EntryState");

            Assert.IsTrue(subMachine.NestedStateMachine.States.Contains(subMachine.EntryState),
                $"SubStateMachine '{subMachine.name}' EntryState '{subMachine.EntryState.name}' is not in NestedStateMachine");

            // Exit states are now on the nested machine
            var exitStates = subMachine.NestedStateMachine.ExitStates ?? new List<AnimationStateAsset>();
            foreach (var exitState in exitStates)
            {
                Assert.IsTrue(subMachine.NestedStateMachine.States.Contains(exitState) ||
                              IsStateInNestedMachinesRecursive(subMachine.NestedStateMachine, exitState),
                    $"Exit state '{exitState.name}' is not in SubStateMachine '{subMachine.name}' or its nested machines");
            }
        }

        private static bool IsStateInNestedMachinesRecursive(StateMachineAsset machine, AnimationStateAsset state)
        {
            foreach (var s in machine.States)
            {
                if (s == state) return true;
                if (s is SubStateMachineStateAsset sub && sub.NestedStateMachine != null)
                {
                    if (IsStateInNestedMachinesRecursive(sub.NestedStateMachine, state))
                        return true;
                }
            }
            return false;
        }

        #endregion
    }
}
