using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Unit tests for StateFlattener - the component that converts hierarchical
    /// sub-state machines into a flat runtime representation.
    /// </summary>
    public class StateFlattenerShould
    {
        #region Basic Flattening Tests

        [Test]
        public void FlattenSimpleStateMachine_WithNoSubMachines()
        {
            // Arrange: Simple machine with 3 leaf states
            var builder = AnimationStateMachineAssetBuilder.New();
            var state1 = builder.AddState<SingleClipStateAsset>();
            var state2 = builder.AddState<SingleClipStateAsset>();
            var state3 = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            // Act
            var (flattenedStates, assetToIndex, exitInfos) = StateFlattener.FlattenStates(machine);

            // Assert
            Assert.AreEqual(3, flattenedStates.Count);
            Assert.AreEqual(3, assetToIndex.Count);
            Assert.AreEqual(0, exitInfos.Count); // No sub-machines, no exit groups
            Assert.IsTrue(assetToIndex.ContainsKey(state1));
            Assert.IsTrue(assetToIndex.ContainsKey(state2));
            Assert.IsTrue(assetToIndex.ContainsKey(state3));
        }

        [Test]
        public void FlattenStateMachine_WithOneSubMachine()
        {
            // Arrange: Root with 1 state + sub-machine containing 2 states
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2, "Nested");
            var builder = AnimationStateMachineAssetBuilder.New();
            var rootState = builder.AddState<SingleClipStateAsset>();
            rootState.name = "RootState";
            var machine = builder.Build();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.name = "SubMachine";
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.DefaultState;
            subMachine.ExitStates = new List<AnimationStateAsset>();
            subMachine.ExitTransitions = new List<StateOutTransition>();
            subMachine.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(subMachine);

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert: Should have 3 leaf states (1 root + 2 nested)
            Assert.AreEqual(3, flattenedStates.Count);
            Assert.IsFalse(assetToIndex.ContainsKey(subMachine)); // SubMachine itself not in flat list
            Assert.IsTrue(assetToIndex.ContainsKey(nested.States[0])); // Nested states are
            Assert.IsTrue(assetToIndex.ContainsKey(nested.States[1]));
        }

        [Test]
        public void FlattenStateMachine_WithNestedSubMachines_ThreeLevels()
        {
            // Arrange: 3-level hierarchy
            var machine = SubStateMachineTestUtils.CreateNestedHierarchy(3);

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert: Each level has 2 leaf states, 3 levels = 6 leaf states
            Assert.AreEqual(6, flattenedStates.Count);

            // Verify paths are correct
            foreach (var flat in flattenedStates)
            {
                Assert.IsFalse(string.IsNullOrEmpty(flat.Path));
                Assert.IsNotNull(flat.Asset);
                Assert.IsFalse(flat.Asset is SubStateMachineStateAsset); // No sub-machines in flat list
            }
        }

        #endregion

        #region Entry State Resolution Tests

        [Test]
        public void ResolveEntryState_ReturnsLeafState()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.States[0];

            // Act
            var resolved = StateFlattener.ResolveEntryState(subMachine);

            // Assert
            Assert.AreEqual(nested.States[0], resolved);
            Assert.IsFalse(resolved is SubStateMachineStateAsset);
        }

        [Test]
        public void ResolveEntryState_HandlesNestedSubMachine()
        {
            // Arrange: SubMachine with entry state that is another SubMachine
            var deepNested = SubStateMachineTestUtils.CreateNestedStateMachine(2, "Deep");

            var innerSubMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            innerSubMachine.NestedStateMachine = deepNested;
            innerSubMachine.EntryState = deepNested.DefaultState;
            innerSubMachine.ExitStates = new List<AnimationStateAsset>();
            innerSubMachine.ExitTransitions = new List<StateOutTransition>();
            innerSubMachine.OutTransitions = new List<StateOutTransition>();

            var outerNested = ScriptableObject.CreateInstance<StateMachineAsset>();
            outerNested.States = new List<AnimationStateAsset> { innerSubMachine };
            outerNested.DefaultState = innerSubMachine;

            var outerSubMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            outerSubMachine.NestedStateMachine = outerNested;
            outerSubMachine.EntryState = innerSubMachine; // Entry is another sub-machine

            // Act
            var resolved = StateFlattener.ResolveEntryState(outerSubMachine);

            // Assert: Should recursively resolve to the leaf entry state
            Assert.AreEqual(deepNested.DefaultState, resolved);
            Assert.IsFalse(resolved is SubStateMachineStateAsset);
        }

        #endregion

        #region Transition Target Resolution Tests

        [Test]
        public void ResolveTransitionTarget_ReturnsDirectIndex_ForLeafState()
        {
            // Arrange
            var builder = AnimationStateMachineAssetBuilder.New();
            var state1 = builder.AddState<SingleClipStateAsset>();
            var state2 = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            var (_, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Act
            var resolvedIndex = StateFlattener.ResolveTransitionTarget(state2, assetToIndex);

            // Assert
            Assert.AreEqual(assetToIndex[state2], resolvedIndex);
        }

        [Test]
        public void ResolveTransitionTarget_RedirectsToEntryState_ForSubMachine()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var builder = AnimationStateMachineAssetBuilder.New();
            var rootState = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.States[1]; // Second state is entry
            subMachine.ExitStates = new List<AnimationStateAsset>();
            subMachine.ExitTransitions = new List<StateOutTransition>();
            subMachine.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(subMachine);

            var (_, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Act
            var resolvedIndex = StateFlattener.ResolveTransitionTarget(subMachine, assetToIndex);

            // Assert: Should resolve to entry state (nested.States[1])
            Assert.AreEqual(assetToIndex[nested.States[1]], resolvedIndex);
        }

        #endregion

        #region Exit State Tests

        [Test]
        public void FlattenStates_CreatesExitTransitionInfo_ForSubMachineWithExitStates()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var builder = AnimationStateMachineAssetBuilder.New();
            var rootState = builder.AddState<SingleClipStateAsset>();
            rootState.name = "RootState";
            var machine = builder.Build();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.name = "SubMachine";
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.States[0];
            subMachine.ExitStates = new List<AnimationStateAsset> { nested.States[1] }; // Second state is exit
            subMachine.ExitTransitions = new List<StateOutTransition>
            {
                new StateOutTransition(rootState, 0.1f)
            };
            subMachine.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(subMachine);

            // Act
            var (flattenedStates, assetToIndex, exitInfos) = StateFlattener.FlattenStates(machine);

            // Assert
            Assert.AreEqual(1, exitInfos.Count);
            Assert.AreEqual(1, exitInfos[0].ExitStateIndices.Count);
            Assert.AreEqual(assetToIndex[nested.States[1]], exitInfos[0].ExitStateIndices[0]);
            Assert.AreEqual(1, exitInfos[0].ExitTransitions.Count);
        }

        [Test]
        public void FlattenStates_MarksExitStates_WithCorrectGroupIndex()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(3);
            var builder = AnimationStateMachineAssetBuilder.New();
            var rootState = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.States[0];
            subMachine.ExitStates = new List<AnimationStateAsset> { nested.States[2] }; // Third state is exit
            subMachine.ExitTransitions = new List<StateOutTransition>
            {
                new StateOutTransition(rootState, 0.1f)
            };
            subMachine.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(subMachine);

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert
            var exitStateIndex = assetToIndex[nested.States[2]];
            var exitState = flattenedStates[exitStateIndex];
            Assert.AreEqual(0, exitState.ExitGroupIndex); // First (and only) exit group

            // Non-exit states should have -1
            var nonExitState1 = flattenedStates[assetToIndex[nested.States[0]]];
            var nonExitState2 = flattenedStates[assetToIndex[nested.States[1]]];
            Assert.AreEqual(-1, nonExitState1.ExitGroupIndex);
            Assert.AreEqual(-1, nonExitState2.ExitGroupIndex);
        }

        #endregion

        #region Default State Resolution Tests

        [Test]
        public void ResolveDefaultState_ReturnsLeafState()
        {
            // Arrange
            var builder = AnimationStateMachineAssetBuilder.New();
            var state1 = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            // Act
            var resolved = StateFlattener.ResolveDefaultState(machine);

            // Assert
            Assert.AreEqual(state1, resolved);
        }

        [Test]
        public void ResolveDefaultState_ResolvesSubMachine_ToEntryState()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset>();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.States[1]; // Second state is entry
            subMachine.ExitStates = new List<AnimationStateAsset>();
            subMachine.ExitTransitions = new List<StateOutTransition>();
            subMachine.OutTransitions = new List<StateOutTransition>();

            machine.States.Add(subMachine);
            machine.DefaultState = subMachine; // Default is the sub-machine

            // Act
            var resolved = StateFlattener.ResolveDefaultState(machine);

            // Assert
            Assert.AreEqual(nested.States[1], resolved);
        }

        #endregion

        #region Clip Collection Tests

        [Test]
        public void CollectAllClips_AggregatesFromNestedMachines()
        {
            // Arrange: Create machine with clips
            var clip1 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            var clip2 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            var clip3 = ScriptableObject.CreateInstance<AnimationClipAsset>();

            var state1 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state1.Clip = clip1;
            state1.OutTransitions = new List<StateOutTransition>();

            var nestedState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            nestedState.Clip = clip2;
            nestedState.OutTransitions = new List<StateOutTransition>();

            var nestedState2 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            nestedState2.Clip = clip3;
            nestedState2.OutTransitions = new List<StateOutTransition>();

            var nested = ScriptableObject.CreateInstance<StateMachineAsset>();
            nested.States = new List<AnimationStateAsset> { nestedState, nestedState2 };
            nested.DefaultState = nestedState;

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nestedState;
            subMachine.ExitStates = new List<AnimationStateAsset>();
            subMachine.ExitTransitions = new List<StateOutTransition>();
            subMachine.OutTransitions = new List<StateOutTransition>();

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { state1, subMachine };
            machine.DefaultState = state1;

            // Act
            var clips = StateFlattener.CollectAllClips(machine);

            // Assert: Should have 3 clips (1 from root + 2 from nested)
            Assert.AreEqual(3, clips.Count);
            Assert.Contains(clip1, clips);
            Assert.Contains(clip2, clips);
            Assert.Contains(clip3, clips);
        }

        #endregion

        #region Path Generation Tests

        [Test]
        public void FlattenStates_GeneratesCorrectPaths()
        {
            // Arrange: 2-level hierarchy
            var machine = SubStateMachineTestUtils.CreateNestedHierarchy(2);

            // Act
            var (flattenedStates, _, _) = StateFlattener.FlattenStates(machine);

            // Assert: Check that paths contain the hierarchy
            foreach (var flat in flattenedStates)
            {
                Assert.IsFalse(string.IsNullOrEmpty(flat.Path));
                // Nested states should have "/" in their path
                if (flat.Path.Contains("SubMachine"))
                {
                    Assert.IsTrue(flat.Path.Contains("/"));
                }
            }
        }

        #endregion

        #region Edge Cases

        [Test]
        public void FlattenStates_HandlesEmptySubMachine()
        {
            // Arrange: SubMachine with no states
            var emptyNested = ScriptableObject.CreateInstance<StateMachineAsset>();
            emptyNested.States = new List<AnimationStateAsset>();

            var builder = AnimationStateMachineAssetBuilder.New();
            var rootState = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = emptyNested;
            subMachine.EntryState = null;
            subMachine.ExitStates = new List<AnimationStateAsset>();
            subMachine.ExitTransitions = new List<StateOutTransition>();
            subMachine.OutTransitions = new List<StateOutTransition>();
            machine.States.Add(subMachine);

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert: Should only have the root state
            Assert.AreEqual(1, flattenedStates.Count);
            Assert.IsTrue(assetToIndex.ContainsKey(rootState));
        }

        [Test]
        public void FlattenStates_HandlesDeeplyNestedHierarchy_FiveLevels()
        {
            // Arrange: 5-level deep hierarchy
            var machine = SubStateMachineTestUtils.CreateNestedHierarchy(5);

            // Act
            var (flattenedStates, _, _) = StateFlattener.FlattenStates(machine);

            // Assert: 5 levels * 2 leaf states = 10 states
            Assert.AreEqual(10, flattenedStates.Count);

            // All should be leaf states
            foreach (var flat in flattenedStates)
            {
                Assert.IsFalse(flat.Asset is SubStateMachineStateAsset);
            }
        }

        #endregion
    }
}
