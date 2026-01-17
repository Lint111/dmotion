using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Unit tests for SubStateMachineStateAsset validation and behavior.
    /// </summary>
    public class SubStateMachineStateAssetShould
    {
        #region IsValid Tests

        [Test]
        public void IsValid_ReturnsTrue_WhenProperlyConfigured()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var subMachine = CreateSubStateMachine(nested, nested.DefaultState);

            // Act & Assert
            Assert.IsTrue(subMachine.IsValid());
        }

        [Test]
        public void IsValid_ReturnsFalse_WhenNestedStateMachineIsNull()
        {
            // Arrange
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = null;
            subMachine.EntryState = null;

            // Act & Assert
            Assert.IsFalse(subMachine.IsValid());
        }

        [Test]
        public void IsValid_ReturnsFalse_WhenEntryStateIsNull()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = null;

            // Act & Assert
            Assert.IsFalse(subMachine.IsValid());
        }

        [Test]
        public void IsValid_ReturnsFalse_WhenEntryStateNotInNestedMachine()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var otherState = ScriptableObject.CreateInstance<SingleClipStateAsset>();

            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nested;
            subMachine.EntryState = otherState; // Not in nested machine

            // Act & Assert
            Assert.IsFalse(subMachine.IsValid());
        }

        #endregion

        #region Clip Aggregation Tests

        [Test]
        public void ClipCount_ReturnsZero_WhenNestedMachineIsNull()
        {
            // Arrange
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = null;

            // Act & Assert
            Assert.AreEqual(0, subMachine.ClipCount);
        }

        [Test]
        public void ClipCount_AggregatesFromNestedMachine()
        {
            // Arrange: Create nested machine with clips
            var clip1 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            var clip2 = ScriptableObject.CreateInstance<AnimationClipAsset>();

            var state1 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state1.Clip = clip1;
            state1.OutTransitions = new List<StateOutTransition>();

            var state2 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state2.Clip = clip2;
            state2.OutTransitions = new List<StateOutTransition>();

            var nested = ScriptableObject.CreateInstance<StateMachineAsset>();
            nested.States = new List<AnimationStateAsset> { state1, state2 };
            nested.DefaultState = state1;

            var subMachine = CreateSubStateMachine(nested, state1);

            // Act & Assert
            Assert.AreEqual(2, subMachine.ClipCount);
        }

        [Test]
        public void Clips_ReturnsEmpty_WhenNestedMachineIsNull()
        {
            // Arrange
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = null;

            // Act
            var clips = new List<AnimationClipAsset>(subMachine.Clips);

            // Assert
            Assert.AreEqual(0, clips.Count);
        }

        [Test]
        public void Clips_AggregatesFromNestedMachine()
        {
            // Arrange
            var clip1 = ScriptableObject.CreateInstance<AnimationClipAsset>();
            var clip2 = ScriptableObject.CreateInstance<AnimationClipAsset>();

            var state1 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state1.Clip = clip1;
            state1.OutTransitions = new List<StateOutTransition>();

            var state2 = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state2.Clip = clip2;
            state2.OutTransitions = new List<StateOutTransition>();

            var nested = ScriptableObject.CreateInstance<StateMachineAsset>();
            nested.States = new List<AnimationStateAsset> { state1, state2 };
            nested.DefaultState = state1;

            var subMachine = CreateSubStateMachine(nested, state1);

            // Act
            var clips = new List<AnimationClipAsset>(subMachine.Clips);

            // Assert
            Assert.AreEqual(2, clips.Count);
            Assert.Contains(clip1, clips);
            Assert.Contains(clip2, clips);
        }

        #endregion

        #region Type Property Tests

        [Test]
        public void Type_ThrowsInvalidOperationException()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(1);
            var subMachine = CreateSubStateMachine(nested, nested.DefaultState);

            // Act & Assert
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = subMachine.Type;
            });
        }

        #endregion

        #region Exit State Configuration Tests

        [Test]
        public void ExitStates_CanBeConfigured()
        {
            // Arrange - NEW ARCHITECTURE: ExitStates are on the nested StateMachineAsset
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(3);
            var subMachine = CreateSubStateMachine(nested, nested.States[0]);

            // Act - Add to nested machine's ExitStates
            nested.ExitStates.Add(nested.States[2]);

            // Assert
            Assert.AreEqual(1, nested.ExitStates.Count);
            Assert.AreEqual(nested.States[2], nested.ExitStates[0]);
        }

        [Test]
        public void ExitTransitions_CanBeConfigured()
        {
            // Arrange - NEW ARCHITECTURE: Exit transitions are OutTransitions on SubStateMachine
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var parentState = ScriptableObject.CreateInstance<SingleClipStateAsset>();

            var subMachine = CreateSubStateMachine(nested, nested.States[0]);

            // Act - Exit transitions are just OutTransitions
            var exitTransition = new StateOutTransition(parentState, 0.15f);
            subMachine.OutTransitions.Add(exitTransition);

            // Assert
            Assert.AreEqual(1, subMachine.OutTransitions.Count);
            Assert.AreEqual(parentState, subMachine.OutTransitions[0].ToState);
            Assert.AreEqual(0.15f, subMachine.OutTransitions[0].TransitionDuration);
        }

        #endregion

        #region Hierarchy Query API Tests

        [Test]
        public void GetAllLeafStates_ReturnsStatesFromNestedMachine()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(3);
            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.name = "RootState";
            rootState.OutTransitions = new List<StateOutTransition>();

            var subMachine = CreateSubStateMachine(nested, nested.States[0]);
            subMachine.name = "SubMachine";

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { rootState, subMachine };
            machine.DefaultState = rootState;

            // Act
            var leafStates = new List<StateWithPath>(machine.GetAllLeafStates());

            // Assert: 1 root + 3 nested = 4 leaf states
            Assert.AreEqual(4, leafStates.Count);

            // Check paths include sub-machine name
            var nestedLeafs = leafStates.Where(s => s.Path.Contains("SubMachine")).ToList();
            Assert.AreEqual(3, nestedLeafs.Count);
        }

        [Test]
        public void GetStatesInGroup_ReturnsNestedStates()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(3);
            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.OutTransitions = new List<StateOutTransition>();

            var subMachine = CreateSubStateMachine(nested, nested.States[0]);

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { rootState, subMachine };
            machine.DefaultState = rootState;

            // Act
            var statesInGroup = new List<AnimationStateAsset>(machine.GetStatesInGroup(subMachine));

            // Assert
            Assert.AreEqual(3, statesInGroup.Count);
            Assert.Contains(nested.States[0], statesInGroup);
            Assert.Contains(nested.States[1], statesInGroup);
            Assert.Contains(nested.States[2], statesInGroup);
        }

        [Test]
        public void GetStatePath_ReturnsCorrectPath()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            nested.States[0].name = "NestedState0";

            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.name = "RootState";
            rootState.OutTransitions = new List<StateOutTransition>();

            var subMachine = CreateSubStateMachine(nested, nested.States[0]);
            subMachine.name = "CombatGroup";

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { rootState, subMachine };
            machine.DefaultState = rootState;

            // Act
            var path = machine.GetStatePath(nested.States[0]);

            // Assert
            Assert.AreEqual("CombatGroup/NestedState0", path);
        }

        [Test]
        public void GetParentGroup_ReturnsSubMachine()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.OutTransitions = new List<StateOutTransition>();

            var subMachine = CreateSubStateMachine(nested, nested.States[0]);

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { rootState, subMachine };
            machine.DefaultState = rootState;

            // Act
            var parent = machine.GetParentGroup(nested.States[0]);

            // Assert
            Assert.AreEqual(subMachine, parent);
        }

        [Test]
        public void GetParentGroup_ReturnsNull_ForRootLevelState()
        {
            // Arrange
            var nested = SubStateMachineTestUtils.CreateNestedStateMachine(2);
            var rootState = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            rootState.OutTransitions = new List<StateOutTransition>();

            var subMachine = CreateSubStateMachine(nested, nested.States[0]);

            var machine = ScriptableObject.CreateInstance<StateMachineAsset>();
            machine.States = new List<AnimationStateAsset> { rootState, subMachine };
            machine.DefaultState = rootState;

            // Act
            var parent = machine.GetParentGroup(rootState);

            // Assert
            Assert.IsNull(parent);
        }

        [Test]
        public void GetAllGroups_ReturnsAllSubMachines()
        {
            // Arrange: 2-level hierarchy with sub-machines
            var machine = SubStateMachineTestUtils.CreateNestedHierarchy(3);

            // Act
            var groups = new List<SubStateMachineStateAsset>(machine.GetAllGroups());

            // Assert: 2 sub-machines (at level 0 and level 1)
            Assert.AreEqual(2, groups.Count);
        }

        #endregion

        #region Helper Methods

        private static SubStateMachineStateAsset CreateSubStateMachine(
            StateMachineAsset nestedMachine,
            AnimationStateAsset entryState)
        {
            // NEW ARCHITECTURE: ExitStates are on the nested machine, not on SubStateMachine
            nestedMachine.ExitStates ??= new List<AnimationStateAsset>();
            
            var subMachine = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            subMachine.NestedStateMachine = nestedMachine;
            subMachine.EntryState = entryState;
            subMachine.OutTransitions = new List<StateOutTransition>();
            return subMachine;
        }

        #endregion
    }
}
