using System;
using System.Collections.Generic;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion.Authoring
{
    internal struct AnimationStateConversionData
    {
        internal StateType Type;
        internal ushort StateIndex;
        internal bool Loop;
        internal float Speed;
        internal ushort SpeedParameterIndex; // ushort.MaxValue = no parameter
        internal UnsafeList<StateOutTransitionConversionData> Transitions;

        /// <summary>
        /// Index into ExitTransitionGroups, or -1 if not an exit state.
        /// </summary>
        internal short ExitTransitionGroupIndex;
    }

    internal struct ClipIndexWithThreshold
    {
        internal int ClipIndex;
        internal float Threshold;
        internal float Speed;
    }
    
    internal struct Directional2DClipData
    {
        internal int ClipIndex;
        internal float2 Position;
        internal float Speed;
    }
    
    internal struct LinearBlendStateConversionData
    {
        internal UnsafeList<ClipIndexWithThreshold> ClipsWithThresholds;
        internal ushort BlendParameterIndex;
        internal bool UsesIntParameter;
        internal int IntRangeMin;
        internal int IntRangeMax;
    }
    
    internal struct Directional2DBlendStateConversionData
    {
        internal UnsafeList<Directional2DClipData> ClipData;
        internal ushort BlendParameterIndexX;
        internal ushort BlendParameterIndexY;
        internal Blend2DAlgorithm Algorithm;
    }

    internal struct StateOutTransitionConversionData
    {
        internal short ToStateIndex;
        internal float TransitionDuration;
        internal float TransitionEndTime;
        internal float Offset;
        internal UnsafeList<BoolTransition> BoolTransitions;
        internal UnsafeList<IntTransition> IntTransitions;
        internal bool CanTransitionToSelf;
        
        /// <summary>
        /// Hermite spline keyframes for custom blend curve.
        /// Empty = linear (fast-path). Populated only for non-linear curves.
        /// </summary>
        internal UnsafeList<CurveKeyframe> CurveKeyframes;
    }


    /// <summary>
    /// Conversion data for an exit transition group (one per SubStateMachine with exit states).
    /// </summary>
    internal struct ExitTransitionGroupConversionData
    {
        /// <summary>
        /// Flattened indices of states that can trigger these exit transitions.
        /// </summary>
        internal UnsafeList<short> ExitStateIndices;

        /// <summary>
        /// Exit transitions for this group.
        /// </summary>
        internal UnsafeList<StateOutTransitionConversionData> ExitTransitions;
    }
    
    /// <summary>
    /// Temporary conversion data for building StateMachineBlob.
    /// SubStateMachine states are flattened during conversion - the final blob contains only leaf states.
    /// NOTE: This struct only holds data references. The actual disposal is managed by the baking system.
    /// </summary>
    [TemporaryBakingType]
    internal struct StateMachineBlobConverter : IComponentData, IComparer<ClipIndexWithThreshold>
    {
        internal byte DefaultStateIndex;
        internal UnsafeList<AnimationStateConversionData> States;
        internal UnsafeList<SingleClipStateBlob> SingleClipStates;
        internal UnsafeList<LinearBlendStateConversionData> LinearBlendStates;
        internal UnsafeList<Directional2DBlendStateConversionData> Directional2DBlendStates;

        /// <summary>
        /// Any State transitions (global transitions from any state).
        /// </summary>
        internal UnsafeList<StateOutTransitionConversionData> AnyStateTransitions;

        /// <summary>
        /// Exit transition groups (one per SubStateMachine with exit states/transitions).
        /// </summary>
        internal UnsafeList<ExitTransitionGroupConversionData> ExitTransitionGroups;

        public readonly unsafe BlobAssetReference<StateMachineBlob> BuildBlob()
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<StateMachineBlob>();
            root.DefaultStateIndex = DefaultStateIndex;

            BuildSingleClipStates(ref builder, ref root);
            BuildAnimationStates(ref builder, ref root);
            BuildLinearBlendStates(ref builder, ref root);
            BuildDirectional2DBlendStates(ref builder, ref root);
            BuildAnyStateTransitions(ref builder, ref root);
            BuildExitTransitionGroups(ref builder, ref root);

            return builder.CreateBlobAssetReference<StateMachineBlob>(Allocator.Persistent);
        }

        private readonly unsafe void BuildSingleClipStates(ref BlobBuilder builder, ref StateMachineBlob root)
        {
            if (SingleClipStates.IsCreated && SingleClipStates.Length > 0)
            {
                builder.ConstructFromNativeArray(ref root.SingleClipStates, SingleClipStates.Ptr, SingleClipStates.Length);
            }
            else
            {
                builder.Allocate(ref root.SingleClipStates, 0);
            }
        }

        private readonly unsafe void BuildAnimationStates(ref BlobBuilder builder, ref StateMachineBlob root)
        {
            var states = builder.Allocate(ref root.States, States.Length);
            for (ushort stateIndex = 0; stateIndex < states.Length; stateIndex++)
            {
                var stateConversionData = States[stateIndex];
                states[stateIndex] = new AnimationStateBlob
                {
                    Type = stateConversionData.Type,
                    StateIndex = stateConversionData.StateIndex,
                    Loop = stateConversionData.Loop,
                    Speed = stateConversionData.Speed,
                    SpeedParameterIndex = stateConversionData.SpeedParameterIndex,
                    ExitTransitionGroupIndex = stateConversionData.ExitTransitionGroupIndex,
                };

                BuildStateTransitions(ref builder, ref states[stateIndex], stateConversionData);
            }
        }

        private static unsafe void BuildStateTransitions(
            ref BlobBuilder builder,
            ref AnimationStateBlob stateBlob,
            in AnimationStateConversionData stateData)
        {
            var transitions = builder.Allocate(ref stateBlob.Transitions, stateData.Transitions.Length);
            for (ushort i = 0; i < transitions.Length; i++)
            {
                var transitionData = stateData.Transitions[i];
                transitions[i] = new StateOutTransitionGroup
                {
                    ToStateIndex = transitionData.ToStateIndex,
                    TransitionEndTime = transitionData.TransitionEndTime,
                    TransitionDuration = transitionData.TransitionDuration,
                    Offset = transitionData.Offset
                };

                BuildTransitionConditions(ref builder, ref transitions[i], transitionData);
            }
        }

        private static unsafe void BuildTransitionConditions(
            ref BlobBuilder builder,
            ref StateOutTransitionGroup transition,
            in StateOutTransitionConversionData transitionData)
        {
            // Bool conditions
            if (transitionData.BoolTransitions.IsCreated && transitionData.BoolTransitions.Length > 0)
                builder.ConstructFromNativeArray(ref transition.BoolTransitions, transitionData.BoolTransitions.Ptr, transitionData.BoolTransitions.Length);
            else
                builder.Allocate(ref transition.BoolTransitions, 0);

            // Int conditions
            if (transitionData.IntTransitions.IsCreated && transitionData.IntTransitions.Length > 0)
                builder.ConstructFromNativeArray(ref transition.IntTransitions, transitionData.IntTransitions.Ptr, transitionData.IntTransitions.Length);
            else
                builder.Allocate(ref transition.IntTransitions, 0);

            // Curve keyframes (empty = linear fast-path)
            if (transitionData.CurveKeyframes.IsCreated && transitionData.CurveKeyframes.Length > 0)
                builder.ConstructFromNativeArray(ref transition.CurveKeyframes, transitionData.CurveKeyframes.Ptr, transitionData.CurveKeyframes.Length);
            else
                builder.Allocate(ref transition.CurveKeyframes, 0);
        }

        private readonly void BuildLinearBlendStates(ref BlobBuilder builder, ref StateMachineBlob root)
        {
            var linearBlendStates = builder.Allocate(ref root.LinearBlendStates, LinearBlendStates.Length);
            for (ushort i = 0; i < linearBlendStates.Length; i++)
            {
                var data = LinearBlendStates[i];
                linearBlendStates[i] = new LinearBlendStateBlob
                {
                    BlendParameterIndex = data.BlendParameterIndex,
                    UsesIntParameter = data.UsesIntParameter,
                    IntRangeMin = data.IntRangeMin,
                    IntRangeMax = data.IntRangeMax
                };

                // Sort clips by threshold
                var clipsArray = CollectionUtils.AsArray(data.ClipsWithThresholds);
                clipsArray.Sort(this);

                var sortedIndexes = builder.Allocate(ref linearBlendStates[i].SortedClipIndexes, clipsArray.Length);
                var sortedThresholds = builder.Allocate(ref linearBlendStates[i].SortedClipThresholds, clipsArray.Length);
                var sortedSpeeds = builder.Allocate(ref linearBlendStates[i].SortedClipSpeeds, clipsArray.Length);

                for (var clipIndex = 0; clipIndex < clipsArray.Length; clipIndex++)
                {
                    var clip = clipsArray[clipIndex];
                    sortedIndexes[clipIndex] = clip.ClipIndex;
                    sortedThresholds[clipIndex] = clip.Threshold;
                    sortedSpeeds[clipIndex] = clip.Speed;
                }
            }
        }

        private readonly void BuildDirectional2DBlendStates(ref BlobBuilder builder, ref StateMachineBlob root)
        {
            var directional2DStates = builder.Allocate(ref root.Directional2DBlendStates, Directional2DBlendStates.Length);
            for (ushort i = 0; i < directional2DStates.Length; i++)
            {
                var data = Directional2DBlendStates[i];
                directional2DStates[i] = new Directional2DBlendStateBlob
                {
                    BlendParameterIndexX = data.BlendParameterIndexX,
                    BlendParameterIndexY = data.BlendParameterIndexY,
                    Algorithm = data.Algorithm
                };

                var count = data.ClipData.Length;
                var clipIndexes = builder.Allocate(ref directional2DStates[i].ClipIndexes, count);
                var clipPositions = builder.Allocate(ref directional2DStates[i].ClipPositions, count);
                var clipSpeeds = builder.Allocate(ref directional2DStates[i].ClipSpeeds, count);

                for (int j = 0; j < count; j++)
                {
                    var clip = data.ClipData[j];
                    clipIndexes[j] = clip.ClipIndex;
                    clipPositions[j] = clip.Position;
                    clipSpeeds[j] = clip.Speed;
                }
            }
        }

        private readonly unsafe void BuildAnyStateTransitions(ref BlobBuilder builder, ref StateMachineBlob root)
        {
            var anyStateTransitions = builder.Allocate(ref root.AnyStateTransitions, AnyStateTransitions.Length);
            for (ushort i = 0; i < anyStateTransitions.Length; i++)
            {
                var data = AnyStateTransitions[i];
                anyStateTransitions[i] = new AnyStateTransition
                {
                    ToStateIndex = data.ToStateIndex,
                    TransitionEndTime = data.TransitionEndTime,
                    TransitionDuration = data.TransitionDuration,
                    Offset = data.Offset,
                    CanTransitionToSelf = data.CanTransitionToSelf
                };

                BuildAnyStateTransitionConditions(ref builder, ref anyStateTransitions[i], data);
            }
        }

        private static unsafe void BuildAnyStateTransitionConditions(
            ref BlobBuilder builder,
            ref AnyStateTransition transition,
            in StateOutTransitionConversionData transitionData)
        {
            // Bool conditions
            if (transitionData.BoolTransitions.IsCreated && transitionData.BoolTransitions.Length > 0)
                builder.ConstructFromNativeArray(ref transition.BoolTransitions, transitionData.BoolTransitions.Ptr, transitionData.BoolTransitions.Length);
            else
                builder.Allocate(ref transition.BoolTransitions, 0);

            // Int conditions
            if (transitionData.IntTransitions.IsCreated && transitionData.IntTransitions.Length > 0)
                builder.ConstructFromNativeArray(ref transition.IntTransitions, transitionData.IntTransitions.Ptr, transitionData.IntTransitions.Length);
            else
                builder.Allocate(ref transition.IntTransitions, 0);

            // Curve keyframes (empty = linear fast-path)
            if (transitionData.CurveKeyframes.IsCreated && transitionData.CurveKeyframes.Length > 0)
                builder.ConstructFromNativeArray(ref transition.CurveKeyframes, transitionData.CurveKeyframes.Ptr, transitionData.CurveKeyframes.Length);
            else
                builder.Allocate(ref transition.CurveKeyframes, 0);
        }

        private readonly unsafe void BuildExitTransitionGroups(ref BlobBuilder builder, ref StateMachineBlob root)
        {
            var exitGroups = builder.Allocate(ref root.ExitTransitionGroups, ExitTransitionGroups.Length);
            for (ushort groupIndex = 0; groupIndex < exitGroups.Length; groupIndex++)
            {
                var groupData = ExitTransitionGroups[groupIndex];
                BuildExitTransitionGroup(ref builder, ref exitGroups[groupIndex], groupData);
            }
        }

        private static unsafe void BuildExitTransitionGroup(
            ref BlobBuilder builder,
            ref ExitTransitionGroup groupBlob,
            in ExitTransitionGroupConversionData groupData)
        {
            // Build exit state indices
            if (groupData.ExitStateIndices.IsCreated && groupData.ExitStateIndices.Length > 0)
                builder.ConstructFromNativeArray(ref groupBlob.ExitStateIndices, groupData.ExitStateIndices.Ptr, groupData.ExitStateIndices.Length);
            else
                builder.Allocate(ref groupBlob.ExitStateIndices, 0);

            // Build exit transitions
            var exitTransitions = builder.Allocate(ref groupBlob.ExitTransitions, groupData.ExitTransitions.Length);
            for (ushort i = 0; i < exitTransitions.Length; i++)
            {
                var transitionData = groupData.ExitTransitions[i];
                exitTransitions[i] = new StateOutTransitionGroup
                {
                    ToStateIndex = transitionData.ToStateIndex,
                    TransitionEndTime = transitionData.TransitionEndTime,
                    TransitionDuration = transitionData.TransitionDuration,
                    Offset = transitionData.Offset
                };

                BuildExitTransitionConditions(ref builder, ref exitTransitions[i], transitionData);
            }
        }

        private static unsafe void BuildExitTransitionConditions(
            ref BlobBuilder builder,
            ref StateOutTransitionGroup transition,
            in StateOutTransitionConversionData transitionData)
        {
            // Bool conditions
            if (transitionData.BoolTransitions.IsCreated && transitionData.BoolTransitions.Length > 0)
                builder.ConstructFromNativeArray(ref transition.BoolTransitions, transitionData.BoolTransitions.Ptr, transitionData.BoolTransitions.Length);
            else
                builder.Allocate(ref transition.BoolTransitions, 0);

            // Int conditions
            if (transitionData.IntTransitions.IsCreated && transitionData.IntTransitions.Length > 0)
                builder.ConstructFromNativeArray(ref transition.IntTransitions, transitionData.IntTransitions.Ptr, transitionData.IntTransitions.Length);
            else
                builder.Allocate(ref transition.IntTransitions, 0);
        }

        public int Compare(ClipIndexWithThreshold x, ClipIndexWithThreshold y)
        {
            return x.Threshold.CompareTo(y.Threshold);
        }

        /// <summary>
        /// Disposes all native collections held by this converter.
        /// NOTE: This must be called explicitly by the baking system after blob creation.
        /// </summary>
        internal unsafe void DisposeNativeCollections()
        {
            DisposeStates();
            DisposeLinearBlendStates();
            DisposeDirectional2DBlendStates();
            DisposeAnyStateTransitions();
            DisposeExitTransitionGroups();
            DisposeSingleClipStates();
        }

        private void DisposeStates()
        {
            if (!States.IsCreated) return;

            for (int i = 0; i < States.Length; i++)
            {
                DisposeStateTransitions(ref States.ElementAt(i));
            }
            States.Dispose();
        }

        private static void DisposeStateTransitions(ref AnimationStateConversionData state)
        {
            if (!state.Transitions.IsCreated) return;

            for (int j = 0; j < state.Transitions.Length; j++)
            {
                DisposeTransitionConditions(ref state.Transitions.ElementAt(j));
            }
            state.Transitions.Dispose();
        }

        private static void DisposeTransitionConditions(ref StateOutTransitionConversionData transition)
        {
            if (transition.BoolTransitions.IsCreated)
                transition.BoolTransitions.Dispose();
            if (transition.IntTransitions.IsCreated)
                transition.IntTransitions.Dispose();
            if (transition.CurveKeyframes.IsCreated)
                transition.CurveKeyframes.Dispose();
        }

        private void DisposeLinearBlendStates()
        {
            if (!LinearBlendStates.IsCreated) return;

            for (int i = 0; i < LinearBlendStates.Length; i++)
            {
                ref var linearBlend = ref LinearBlendStates.ElementAt(i);
                if (linearBlend.ClipsWithThresholds.IsCreated)
                    linearBlend.ClipsWithThresholds.Dispose();
            }
            LinearBlendStates.Dispose();
        }

        private void DisposeDirectional2DBlendStates()
        {
            if (!Directional2DBlendStates.IsCreated) return;

            for (int i = 0; i < Directional2DBlendStates.Length; i++)
            {
                ref var dir2D = ref Directional2DBlendStates.ElementAt(i);
                if (dir2D.ClipData.IsCreated)
                    dir2D.ClipData.Dispose();
            }
            Directional2DBlendStates.Dispose();
        }

        private void DisposeAnyStateTransitions()
        {
            if (!AnyStateTransitions.IsCreated) return;

            for (int i = 0; i < AnyStateTransitions.Length; i++)
            {
                DisposeTransitionConditions(ref AnyStateTransitions.ElementAt(i));
            }
            AnyStateTransitions.Dispose();
        }

        private void DisposeExitTransitionGroups()
        {
            if (!ExitTransitionGroups.IsCreated) return;

            for (int i = 0; i < ExitTransitionGroups.Length; i++)
            {
                DisposeExitTransitionGroup(ref ExitTransitionGroups.ElementAt(i));
            }
            ExitTransitionGroups.Dispose();
        }

        private static void DisposeExitTransitionGroup(ref ExitTransitionGroupConversionData exitGroup)
        {
            if (exitGroup.ExitStateIndices.IsCreated)
                exitGroup.ExitStateIndices.Dispose();

            if (!exitGroup.ExitTransitions.IsCreated) return;

            for (int j = 0; j < exitGroup.ExitTransitions.Length; j++)
            {
                DisposeTransitionConditions(ref exitGroup.ExitTransitions.ElementAt(j));
            }
            exitGroup.ExitTransitions.Dispose();
        }

        private void DisposeSingleClipStates()
        {
            if (SingleClipStates.IsCreated)
                SingleClipStates.Dispose();
        }
    }
}