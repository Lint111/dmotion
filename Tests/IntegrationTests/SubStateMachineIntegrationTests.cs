using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for sub-state machine flattening and blob conversion.
    /// Tests the full pipeline from authoring assets to runtime blob without requiring
    /// the pre-baked test scene (no ACL data needed - tests structural correctness).
    /// </summary>
    public class SubStateMachineIntegrationTests
    {
        #region Flattening to Blob Pipeline Tests

        [Test]
        public void FlattenAndConvert_SimpleStateMachine_ProducesCorrectBlob()
        {
            // Arrange: Create a simple state machine with 2 states
            var builder = AnimationStateMachineAssetBuilder.New();
            var state1 = builder.AddState<SingleClipStateAsset>();
            state1.name = "Idle";
            var state2 = builder.AddState<SingleClipStateAsset>();
            state2.name = "Walk";
            var machine = builder.Build();

            // Act: Flatten and get indices
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert
            Assert.AreEqual(2, flattenedStates.Count);
            Assert.IsTrue(assetToIndex.ContainsKey(state1));
            Assert.IsTrue(assetToIndex.ContainsKey(state2));
            Assert.AreEqual(state1, flattenedStates[assetToIndex[state1]].Asset);
            Assert.AreEqual(state2, flattenedStates[assetToIndex[state2]].Asset);
        }

        [Test]
        public void FlattenAndConvert_WithSubMachine_FlattenedStatesAreLeafOnly()
        {
            // Arrange: Create state machine with sub-machine
            var nested = CreateNestedStateMachine(2);
            var machine = CreateMachineWithSubMachine(nested);

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert: Only leaf states in flattened list
            Assert.AreEqual(3, flattenedStates.Count); // 1 root + 2 nested
            foreach (var flat in flattenedStates)
            {
                Assert.IsFalse(flat.Asset is SubStateMachineStateAsset,
                    "SubStateMachineStateAsset should not be in flattened list");
            }
        }

        [Test]
        public void FlattenAndConvert_PreservesStateOrder()
        {
            // Arrange: Create state machine with known state order
            var nested = CreateNestedStateMachine(2);
            var machine = CreateMachineWithSubMachine(nested);

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert: Root state should have index 0
            var rootState = machine.States.First(s => !(s is SubStateMachineStateAsset));
            Assert.AreEqual(0, assetToIndex[rootState]);

            // Nested states should follow
            Assert.IsTrue(assetToIndex[nested.States[0]] > 0);
            Assert.IsTrue(assetToIndex[nested.States[1]] > 0);
        }

        #endregion

        #region Default State Resolution Tests

        [Test]
        public void DefaultStateResolution_WhenDefaultIsLeafState_ReturnsDirectly()
        {
            // Arrange
            var builder = AnimationStateMachineAssetBuilder.New();
            var state1 = builder.AddState<SingleClipStateAsset>();
            state1.name = "Default";
            var machine = builder.Build();

            // Act
            var resolved = StateFlattener.ResolveDefaultState(machine);

            // Assert
            Assert.AreEqual(state1, resolved);
        }

        [Test]
        public void DefaultStateResolution_WhenDefaultIsSubMachine_ResolvesToEntryState()
        {
            // Arrange: Create machine where default is a sub-machine
            var nested = CreateNestedStateMachine(2);
            nested.States[1].name = "NestedEntry";

            nested.ExitStates = new List<AnimationStateAsset>();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.name = "SubMachine";
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nested.States[1]; // Second state is entry
            subMachine.OutTransitions = new List<StateOutTransition>();

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { subMachine };
            machine.DefaultState = subMachine; // Default is the sub-machine

            // Act
            var resolved = StateFlattener.ResolveDefaultState(machine);

            // Assert: Should resolve to the entry state of the sub-machine
            Assert.AreEqual(nested.States[1], resolved);
            Assert.AreEqual("NestedEntry", resolved.name);
        }

        [Test]
        public void DefaultStateResolution_WithNestedSubMachines_ResolvesRecursively()
        {
            // Arrange: Create 2-level nested sub-machines
            var deepNested = CreateNestedStateMachine(1);
            deepNested.States[0].name = "DeepEntry";

            deepNested.ExitStates = new List<AnimationStateAsset>();

            var innerSubMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            innerSubMachine.NestedStateMachine = deepNested;
            innerSubMachine.EntryState = deepNested.States[0];
            innerSubMachine.OutTransitions = new List<StateOutTransition>();

            var outerNested = ScriptableObject.CreateInstance<StateMachineAsset>();
            outerNested.States = new List<AnimationStateAsset> { innerSubMachine };
            outerNested.DefaultState = innerSubMachine;
            outerNested.ExitStates = new List<AnimationStateAsset>();

            var outerSubMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            outerSubMachine.NestedStateMachine = outerNested;
            outerSubMachine.EntryState = innerSubMachine; // Entry is another sub-machine
            outerSubMachine.OutTransitions = new List<StateOutTransition>();

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { outerSubMachine };
            machine.DefaultState = outerSubMachine;

            // Act
            var resolved = StateFlattener.ResolveDefaultState(machine);

            // Assert: Should recursively resolve to deepest leaf state
            Assert.AreEqual(deepNested.States[0], resolved);
            Assert.IsFalse(resolved is SubStateMachineStateAsset);
        }

        #endregion

        #region Transition Target Resolution Tests

        [Test]
        public void TransitionTargetResolution_ToLeafState_ReturnsDirectIndex()
        {
            // Arrange
            var builder = AnimationStateMachineAssetBuilder.New();
            var state1 = builder.AddState<SingleClipStateAsset>();
            var state2 = builder.AddState<SingleClipStateAsset>();
            var machine = builder.Build();

            var (_, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Act
            var resolved = StateFlattener.ResolveTransitionTarget(state2, assetToIndex);

            // Assert
            Assert.AreEqual(assetToIndex[state2], resolved);
        }

        [Test]
        public void TransitionTargetResolution_ToSubMachine_ResolvesToEntryState()
        {
            // Arrange
            var nested = CreateNestedStateMachine(2);
            var machine = CreateMachineWithSubMachine(nested);
            var subMachine = machine.States.First(s => s is SubStateMachineStateAsset) as SubStateMachineStateAsset;
            subMachine.EntryState = nested.States[1]; // Second nested state is entry

            var (_, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Act
            var resolved = StateFlattener.ResolveTransitionTarget(subMachine, assetToIndex);

            // Assert: Should resolve to entry state index
            Assert.AreEqual(assetToIndex[nested.States[1]], resolved);
        }

        #endregion

        #region Exit State Tracking Tests

        [Test]
        public void ExitStateTracking_MarksExitStatesCorrectly()
        {
            // Arrange
            var nested = CreateNestedStateMachine(3);
            var machine = CreateMachineWithSubMachine(nested);
            var subMachine = machine.States.First(s => s is SubStateMachineStateAsset) as SubStateMachineStateAsset;

            // Configure exit state
            nested.ExitStates.Add(nested.States[2]); // Third state is exit
            subMachine.OutTransitions.Add(new StateOutTransition(
                machine.States.First(s => !(s is SubStateMachineStateAsset)), 0.1f));

            // Act
            var (flattenedStates, assetToIndex, exitInfos) = StateFlattener.FlattenStates(machine);

            // Assert
            Assert.AreEqual(1, exitInfos.Count, "Should have one exit group");

            var exitStateIndex = assetToIndex[nested.States[2]];
            var exitState = flattenedStates[exitStateIndex];
            Assert.AreEqual(0, exitState.ExitGroupIndex, "Exit state should reference group 0");

            // Non-exit states should have -1
            var nonExitIndex = assetToIndex[nested.States[0]];
            Assert.AreEqual(-1, flattenedStates[nonExitIndex].ExitGroupIndex);
        }

        [Test]
        public void ExitStateTracking_MultipleExitStates_AllMarkedCorrectly()
        {
            // Arrange
            var nested = CreateNestedStateMachine(4);
            var machine = CreateMachineWithSubMachine(nested);
            var subMachine = machine.States.First(s => s is SubStateMachineStateAsset) as SubStateMachineStateAsset;

            // Configure multiple exit states
            nested.ExitStates.Add(nested.States[2]);
            nested.ExitStates.Add(nested.States[3]);
            var targetState = machine.States.First(s => !(s is SubStateMachineStateAsset));
            subMachine.OutTransitions.Add(new StateOutTransition(targetState, 0.1f));

            // Act
            var (flattenedStates, assetToIndex, exitInfos) = StateFlattener.FlattenStates(machine);

            // Assert
            Assert.AreEqual(1, exitInfos.Count);
            Assert.AreEqual(2, exitInfos[0].ExitStateIndices.Count);

            // Both exit states should be marked with same group
            Assert.AreEqual(0, flattenedStates[assetToIndex[nested.States[2]]].ExitGroupIndex);
            Assert.AreEqual(0, flattenedStates[assetToIndex[nested.States[3]]].ExitGroupIndex);
        }

        #endregion

        #region Path Generation Tests

        [Test]
        public void PathGeneration_RootLevelState_HasSimplePath()
        {
            // Arrange
            var builder = AnimationStateMachineAssetBuilder.New();
            var state = builder.AddState<SingleClipStateAsset>();
            state.name = "Idle";
            var machine = builder.Build();

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert
            var path = flattenedStates[assetToIndex[state]].Path;
            Assert.AreEqual("Idle", path);
        }

        [Test]
        public void PathGeneration_NestedState_IncludesParentPath()
        {
            // Arrange
            var nested = CreateNestedStateMachine(2);
            nested.States[0].name = "Attack";
            var machine = CreateMachineWithSubMachine(nested);
            var subMachine = machine.States.First(s => s is SubStateMachineStateAsset) as SubStateMachineStateAsset;
            subMachine.name = "Combat";

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(machine);

            // Assert
            var path = flattenedStates[assetToIndex[nested.States[0]]].Path;
            Assert.IsTrue(path.Contains("Combat"), "Path should include sub-machine name");
            Assert.IsTrue(path.Contains("Attack"), "Path should include state name");
        }

        [Test]
        public void PathGeneration_DeeplyNestedState_HasFullPath()
        {
            // Arrange: 3-level hierarchy
            var deep = CreateNestedStateMachine(1);
            deep.States[0].name = "DeepState";

            var middle = ScriptableObject.CreateInstance<StateMachineAsset>();
            middle.States = new List<AnimationStateAsset>();

            deep.ExitStates = new List<AnimationStateAsset>();

            var innerSubMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            innerSubMachine.name = "Inner";
            innerSubMachine.NestedStateMachine = deep;
            innerSubMachine.EntryState = deep.States[0];
            innerSubMachine.OutTransitions = new List<StateOutTransition>();

            middle.States.Add(innerSubMachine);
            middle.DefaultState = innerSubMachine;
            middle.ExitStates = new List<AnimationStateAsset>();

            var outerSubMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            outerSubMachine.name = "Outer";
            outerSubMachine.NestedStateMachine = middle;
            outerSubMachine.EntryState = innerSubMachine;
            outerSubMachine.OutTransitions = new List<StateOutTransition>();

            var root = ScriptableObject.CreateInstance<StateMachineAsset>();
            root.States = new List<AnimationStateAsset> { outerSubMachine };
            root.DefaultState = outerSubMachine;

            // Act
            var (flattenedStates, assetToIndex, _) = StateFlattener.FlattenStates(root);

            // Assert
            var path = flattenedStates[assetToIndex[deep.States[0]]].Path;
            Assert.IsTrue(path.Contains("Outer"));
            Assert.IsTrue(path.Contains("Inner"));
            Assert.IsTrue(path.Contains("DeepState"));
        }

        #endregion

        #region Clip Collection Tests

        [Test]
        public void ClipCollection_IncludesClipsFromNestedMachines()
        {
            // Arrange
            var clip1 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            clip1.name = "RootClip";
            var clip2 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            clip2.name = "NestedClip1";
            var clip3 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            clip3.name = "NestedClip2";

            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.Clip = clip1;
            rootState.OutTransitions = new List<StateOutTransition>();

            var nestedState1 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            nestedState1.Clip = clip2;
            nestedState1.OutTransitions = new List<StateOutTransition>();

            var nestedState2 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            nestedState2.Clip = clip3;
            nestedState2.OutTransitions = new List<StateOutTransition>();

            var nested = ScriptableObject.CreateInstance<StateMachineAsset>();
            nested.States = new List<AnimationStateAsset> { nestedState1, nestedState2 };
            nested.DefaultState = nestedState1;

            nested.ExitStates = new List<AnimationStateAsset>();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = nestedState1;
            subMachine.OutTransitions = new List<StateOutTransition>();

            var root = ScriptableObject.CreateInstance<StateMachineAsset>();
            root.States = new List<AnimationStateAsset> { rootState, subMachine };
            root.DefaultState = rootState;

            // Act
            var clips = StateFlattener.CollectAllClips(root);

            // Assert
            Assert.AreEqual(3, clips.Count);
            Assert.Contains(clip1, clips);
            Assert.Contains(clip2, clips);
            Assert.Contains(clip3, clips);
        }

        #endregion

        #region Helper Methods

        private StateMachineAsset CreateNestedStateMachine(int stateCount)
        {
            var nested = ScriptableObject.CreateInstance<StateMachineAsset>();
            nested.States = new List<AnimationStateAsset>();
            nested.Parameters = new List<AnimationParameterAsset>();
            nested.ExitStates = new List<AnimationStateAsset>(); // NEW: Initialize ExitStates

            for (int i = 0; i < stateCount; i++)
            {
                var state = ScriptableObject.CreateInstance<SingleClipStateAsset>();
                state.name = $"NestedState_{i}";
                state.OutTransitions = new List<StateOutTransition>();
                nested.States.Add(state);
            }

            if (stateCount > 0)
            {
                nested.DefaultState = nested.States[0];
            }

            return nested;
        }

        private StateMachineAsset CreateMachineWithSubMachine(StateMachineAsset nestedMachine)
        {
            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.name = "RootState";
            rootState.OutTransitions = new List<StateOutTransition>();

            nestedMachine.ExitStates = new List<AnimationStateAsset>();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.name = "SubMachine";
            subMachine.NestedStateMachine = nestedMachine;
            subMachine.EntryState = nestedMachine.DefaultState;
            subMachine.OutTransitions = new List<StateOutTransition>();

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { rootState, subMachine };
            machine.DefaultState = rootState;
            machine.Parameters = new List<AnimationParameterAsset>();

            return machine;
        }

        #endregion
    }
}
