using System.Collections.Generic;
using DMotion.Authoring;
using DMotion.Editor;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace DMotion.Tests.Editor
{
    /// <summary>
    /// Tests for parameter linking and dependency analysis.
    /// </summary>
    [TestFixture]
    public class ParameterLinkingTests
    {
        private StateMachineAsset _parentMachine;
        private StateMachineAsset _childMachine;
        private SubStateMachineStateAsset _subMachine;

        [SetUp]
        public void SetUp()
        {
            // Create parent state machine with a parameter
            _parentMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            _parentMachine.name = "ParentMachine";
            _parentMachine.States = new List<AnimationStateAsset>();
            _parentMachine.Parameters = new List<AnimationParameterAsset>();
            _parentMachine.AnyStateTransitions = new List<StateOutTransition>();
            _parentMachine.ExitStates = new List<AnimationStateAsset>();

            // Create child state machine with its own parameter
            _childMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            _childMachine.name = "ChildMachine";
            _childMachine.States = new List<AnimationStateAsset>();
            _childMachine.Parameters = new List<AnimationParameterAsset>();
            _childMachine.AnyStateTransitions = new List<StateOutTransition>();
            _childMachine.ExitStates = new List<AnimationStateAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_parentMachine != null)
                Object.DestroyImmediate(_parentMachine);
            if (_childMachine != null)
                Object.DestroyImmediate(_childMachine);
            if (_subMachine != null)
                Object.DestroyImmediate(_subMachine);
        }

        #region Parameter Analysis Tests

        [Test]
        public void AnalyzeRequiredParameters_EmptySubMachine_ReturnsEmptyList()
        {
            // Arrange
            _subMachine = CreateSubMachine(_childMachine);

            // Act
            var requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(_subMachine);

            // Assert
            Assert.IsNotNull(requirements);
            Assert.AreEqual(0, requirements.Count);
        }

        [Test]
        public void AnalyzeRequiredParameters_WithSpeedParameter_ReturnsSpeedRequirement()
        {
            // Arrange
            var speedParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            speedParam.name = "Speed";
            _childMachine.Parameters.Add(speedParam);

            var state = CreateSingleClipState("WalkState");
            state.SpeedParameter = speedParam;
            _childMachine.States.Add(state);
            _childMachine.DefaultState = state;

            _subMachine = CreateSubMachine(_childMachine);

            // Act
            var requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(_subMachine);

            // Assert
            Assert.AreEqual(1, requirements.Count);
            Assert.AreEqual(speedParam, requirements[0].Parameter);
            Assert.AreEqual(ParameterUsageType.SpeedParameter, requirements[0].UsageType);
        }

        [Test]
        public void AnalyzeRequiredParameters_WithBlendParameter_ReturnsBlendRequirement()
        {
            // Arrange
            var blendParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            blendParam.name = "BlendValue";
            _childMachine.Parameters.Add(blendParam);

            var state = ScriptableObject.CreateInstance<LinearBlendStateAsset>();
            state.name = "BlendState";
            state.BlendParameter = blendParam;
            state.BlendClips = new ClipWithThreshold[0];
            state.OutTransitions = new List<StateOutTransition>();
            _childMachine.States.Add(state);
            _childMachine.DefaultState = state;

            _subMachine = CreateSubMachine(_childMachine);

            // Act
            var requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(_subMachine);

            // Assert
            Assert.AreEqual(1, requirements.Count);
            Assert.AreEqual(blendParam, requirements[0].Parameter);
            Assert.AreEqual(ParameterUsageType.BlendParameter, requirements[0].UsageType);
        }

        [Test]
        public void AnalyzeRequiredParameters_WithTransitionCondition_ReturnsConditionRequirement()
        {
            // Arrange
            var boolParam = ScriptableObject.CreateInstance<BoolParameterAsset>();
            boolParam.name = "IsRunning";
            _childMachine.Parameters.Add(boolParam);

            var state1 = CreateSingleClipState("Idle");
            var state2 = CreateSingleClipState("Run");
            _childMachine.States.Add(state1);
            _childMachine.States.Add(state2);
            _childMachine.DefaultState = state1;

            // Add transition with condition
            var transition = new StateOutTransition(state2, 0.1f);
            transition.Conditions = new List<TransitionCondition>
            {
                new TransitionCondition { Parameter = boolParam }
            };
            state1.OutTransitions.Add(transition);

            _subMachine = CreateSubMachine(_childMachine);

            // Act
            var requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(_subMachine);

            // Assert
            Assert.AreEqual(1, requirements.Count);
            Assert.AreEqual(boolParam, requirements[0].Parameter);
            Assert.AreEqual(ParameterUsageType.TransitionCondition, requirements[0].UsageType);
        }

        #endregion

        #region Parameter Resolution Tests

        [Test]
        public void FindCompatibleParameter_ExactNameMatch_ReturnsParameter()
        {
            // Arrange
            var parentParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            parentParam.name = "Speed";
            _parentMachine.Parameters.Add(parentParam);

            var childParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            childParam.name = "Speed";

            // Act
            var found = ParameterDependencyAnalyzer.FindCompatibleParameter(_parentMachine, childParam);

            // Assert
            Assert.AreEqual(parentParam, found);

            Object.DestroyImmediate(childParam);
        }

        [Test]
        public void FindCompatibleParameter_DifferentCase_ReturnsParameter()
        {
            // Arrange
            var parentParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            parentParam.name = "speed";
            _parentMachine.Parameters.Add(parentParam);

            var childParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            childParam.name = "Speed";

            // Act
            var found = ParameterDependencyAnalyzer.FindCompatibleParameter(_parentMachine, childParam);

            // Assert
            Assert.AreEqual(parentParam, found);

            Object.DestroyImmediate(childParam);
        }

        [Test]
        public void FindCompatibleParameter_DifferentType_ReturnsNull()
        {
            // Arrange
            var parentParam = ScriptableObject.CreateInstance<IntParameterAsset>();
            parentParam.name = "Speed";
            _parentMachine.Parameters.Add(parentParam);

            var childParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            childParam.name = "Speed";

            // Act
            var found = ParameterDependencyAnalyzer.FindCompatibleParameter(_parentMachine, childParam);

            // Assert
            Assert.IsNull(found);

            Object.DestroyImmediate(childParam);
        }

        [Test]
        public void ResolveParameterDependencies_ExistingParameter_CreatesLink()
        {
            // Arrange
            var parentParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            parentParam.name = "Speed";
            _parentMachine.Parameters.Add(parentParam);

            var childParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            childParam.name = "Speed";
            _childMachine.Parameters.Add(childParam);

            var state = CreateSingleClipState("WalkState");
            state.SpeedParameter = childParam;
            _childMachine.States.Add(state);
            _childMachine.DefaultState = state;

            _subMachine = CreateSubMachine(_childMachine);

            // Act
            var result = ParameterDependencyAnalyzer.ResolveParameterDependencies(_parentMachine, _subMachine);

            // Assert
            Assert.IsFalse(result.HasMissingParameters);
            Assert.IsTrue(result.HasLinks);
            Assert.AreEqual(1, result.ParameterLinks.Count);
            Assert.AreEqual(parentParam, result.ParameterLinks[0].SourceParameter);
            Assert.AreEqual(childParam, result.ParameterLinks[0].TargetParameter);
        }

        [Test]
        public void ResolveParameterDependencies_MissingParameter_ReturnsMissing()
        {
            // Arrange - parent has no matching parameter
            var childParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            childParam.name = "Speed";
            _childMachine.Parameters.Add(childParam);

            var state = CreateSingleClipState("WalkState");
            state.SpeedParameter = childParam;
            _childMachine.States.Add(state);
            _childMachine.DefaultState = state;

            _subMachine = CreateSubMachine(_childMachine);

            // Act
            var result = ParameterDependencyAnalyzer.ResolveParameterDependencies(_parentMachine, _subMachine);

            // Assert
            Assert.IsTrue(result.HasMissingParameters);
            Assert.AreEqual(1, result.MissingParameters.Count);
            Assert.AreEqual(childParam, result.MissingParameters[0].Parameter);
        }

        #endregion

        #region Orphan Detection Tests

        [Test]
        public void FindOrphanedParameters_UsedParameter_NotOrphaned()
        {
            // Arrange
            var param = ScriptableObject.CreateInstance<FloatParameterAsset>();
            param.name = "Speed";
            _parentMachine.Parameters.Add(param);

            var state = CreateSingleClipState("WalkState");
            state.SpeedParameter = param;
            _parentMachine.States.Add(state);
            _parentMachine.DefaultState = state;

            // Act
            var orphans = ParameterDependencyAnalyzer.FindOrphanedParameters(_parentMachine);

            // Assert
            Assert.AreEqual(0, orphans.Count);
        }

        [Test]
        public void FindOrphanedParameters_UnusedParameter_IsOrphaned()
        {
            // Arrange
            var usedParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            usedParam.name = "Speed";
            _parentMachine.Parameters.Add(usedParam);

            var unusedParam = ScriptableObject.CreateInstance<BoolParameterAsset>();
            unusedParam.name = "Unused";
            _parentMachine.Parameters.Add(unusedParam);

            var state = CreateSingleClipState("WalkState");
            state.SpeedParameter = usedParam;
            _parentMachine.States.Add(state);
            _parentMachine.DefaultState = state;

            // Act
            var orphans = ParameterDependencyAnalyzer.FindOrphanedParameters(_parentMachine);

            // Assert
            Assert.AreEqual(1, orphans.Count);
            Assert.AreEqual(unusedParam, orphans[0]);
        }

        #endregion

        #region Blob Conversion Tests

        [Test]
        public void CreateStateMachineBlob_WithLinkedParameters_ResolvesCorrectly()
        {
            // Arrange
            var parentParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            parentParam.name = "Speed";
            _parentMachine.Parameters.Add(parentParam);

            var childParam = ScriptableObject.CreateInstance<FloatParameterAsset>();
            childParam.name = "Speed";
            _childMachine.Parameters.Add(childParam);

            // Create child state using child parameter
            var childState = CreateSingleClipState("WalkState");
            childState.SpeedParameter = childParam;
            _childMachine.States.Add(childState);
            _childMachine.DefaultState = childState;

            // Create sub-machine and add to parent
            _subMachine = CreateSubMachine(_childMachine);
            _parentMachine.States.Add(_subMachine);
            _parentMachine.DefaultState = childState; // Default resolves to child's entry

            // Create a link
            var link = ParameterLink.Direct(parentParam, childParam, _subMachine);
            _parentMachine.AddLink(link);

            // Act - should not throw
            var blob = AnimationStateMachineConversionUtils.CreateStateMachineBlob(_parentMachine);

            // Assert
            Assert.IsTrue(blob.IsCreated);
            Assert.AreEqual(1, blob.Value.States.Length);

            // The state should have a valid speed parameter index (0, since there's only one float param)
            ref var stateBlob = ref blob.Value.States[0];
            Assert.AreNotEqual(ushort.MaxValue, stateBlob.SpeedParameterIndex);
            Assert.AreEqual((ushort)0, stateBlob.SpeedParameterIndex);

            blob.Dispose();
        }

        #endregion

        #region Helper Methods

        private SubStateMachineStateAsset CreateSubMachine(StateMachineAsset nested)
        {
            var sub = ScriptableObject.CreateInstance<SubStateMachineStateAsset>();
            sub.name = "SubMachine";
            sub.NestedStateMachine = nested;
            sub.EntryState = nested.DefaultState;
            sub.OutTransitions = new List<StateOutTransition>();
            return sub;
        }

        private SingleClipStateAsset CreateSingleClipState(string name)
        {
            var state = ScriptableObject.CreateInstance<SingleClipStateAsset>();
            state.name = name;
            state.Loop = true;
            state.Speed = 1f;
            state.OutTransitions = new List<StateOutTransition>();
            return state;
        }

        #endregion
    }
}
