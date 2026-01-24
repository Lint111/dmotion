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
    /// </summary>
    [TemporaryBakingType]
    internal struct StateMachineBlobConverter : IComponentData, IComparer<ClipIndexWithThreshold>, IDisposable
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

            // Only call ConstructFromNativeArray if we have items
            if (SingleClipStates.IsCreated && SingleClipStates.Length > 0)
            {
                builder.ConstructFromNativeArray(ref root.SingleClipStates, SingleClipStates.Ptr, SingleClipStates.Length);
            }
            else
            {
                builder.Allocate(ref root.SingleClipStates, 0);
            }

            //States
            {
                var states = builder.Allocate(ref root.States, States.Length);
                for (ushort stateIndex = 0; stateIndex < states.Length; stateIndex++)
                {
                    var stateConversionData = States[stateIndex];
                    states[stateIndex] = new AnimationStateBlob()
                    {
                        Type = stateConversionData.Type,
                        StateIndex = stateConversionData.StateIndex,
                        Loop = stateConversionData.Loop,
                        Speed = stateConversionData.Speed,
                        SpeedParameterIndex = stateConversionData.SpeedParameterIndex,
                        ExitTransitionGroupIndex = stateConversionData.ExitTransitionGroupIndex,
                    };

                    //transitions
                    var transitions = builder.Allocate(ref states[stateIndex].Transitions, stateConversionData.Transitions.Length);
                    for (ushort transitionIndex = 0; transitionIndex < transitions.Length; transitionIndex++)
                    {
                        var transitionConversionData = stateConversionData.Transitions[transitionIndex];
                        transitions[transitionIndex] = new StateOutTransitionGroup()
                        {
                            ToStateIndex = transitionConversionData.ToStateIndex,
                            TransitionEndTime = transitionConversionData.TransitionEndTime,
                            TransitionDuration = transitionConversionData.TransitionDuration,
                            Offset = transitionConversionData.Offset
                        };

                        if (transitionConversionData.BoolTransitions.IsCreated && transitionConversionData.BoolTransitions.Length > 0)
                            builder.ConstructFromNativeArray(
                                ref transitions[transitionIndex].BoolTransitions,
                                transitionConversionData.BoolTransitions.Ptr,
                                transitionConversionData.BoolTransitions.Length);
                        else
                            builder.Allocate(ref transitions[transitionIndex].BoolTransitions, 0);

                        if (transitionConversionData.IntTransitions.IsCreated && transitionConversionData.IntTransitions.Length > 0)
                            builder.ConstructFromNativeArray(
                                ref transitions[transitionIndex].IntTransitions,
                                transitionConversionData.IntTransitions.Ptr,
                                transitionConversionData.IntTransitions.Length);
                        else
                            builder.Allocate(ref transitions[transitionIndex].IntTransitions, 0);
                        
                        // Curve keyframes (empty = linear fast-path)
                        if (transitionConversionData.CurveKeyframes.IsCreated && transitionConversionData.CurveKeyframes.Length > 0)
                            builder.ConstructFromNativeArray(
                                ref transitions[transitionIndex].CurveKeyframes,
                                transitionConversionData.CurveKeyframes.Ptr,
                                transitionConversionData.CurveKeyframes.Length);
                        else
                            builder.Allocate(ref transitions[transitionIndex].CurveKeyframes, 0);
                    }
                }
            }
            
            //Linear Blend state
            {
                var linearBlendStates = builder.Allocate(ref root.LinearBlendStates, LinearBlendStates.Length);
                for (ushort i = 0; i < linearBlendStates.Length; i++)
                {
                    var linearBlendStateConversionData = LinearBlendStates[i];
                    linearBlendStates[i] = new LinearBlendStateBlob
                    { 
                        BlendParameterIndex = linearBlendStateConversionData.BlendParameterIndex,
                        UsesIntParameter = linearBlendStateConversionData.UsesIntParameter,
                        IntRangeMin = linearBlendStateConversionData.IntRangeMin,
                        IntRangeMax = linearBlendStateConversionData.IntRangeMax
                    };

                    //TODO: Actually sort things first
                    //Make sure clips are sorted by threshold
                    var clipsArray = CollectionUtils.AsArray(linearBlendStateConversionData.ClipsWithThresholds);
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

            // Directional 2D Blend state
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

            // Any State transitions
            {
                var anyStateTransitions = builder.Allocate(ref root.AnyStateTransitions, AnyStateTransitions.Length);
                for (ushort i = 0; i < anyStateTransitions.Length; i++)
                {
                    var anyTransitionConversionData = AnyStateTransitions[i];
                    anyStateTransitions[i] = new AnyStateTransition()
                    {
                        ToStateIndex = anyTransitionConversionData.ToStateIndex,
                        TransitionEndTime = anyTransitionConversionData.TransitionEndTime,
                        TransitionDuration = anyTransitionConversionData.TransitionDuration,
                        Offset = anyTransitionConversionData.Offset,
                        CanTransitionToSelf = anyTransitionConversionData.CanTransitionToSelf
                    };

                    if (anyTransitionConversionData.BoolTransitions.IsCreated && anyTransitionConversionData.BoolTransitions.Length > 0)
                        builder.ConstructFromNativeArray(
                            ref anyStateTransitions[i].BoolTransitions,
                            anyTransitionConversionData.BoolTransitions.Ptr,
                            anyTransitionConversionData.BoolTransitions.Length);
                    else
                        builder.Allocate(ref anyStateTransitions[i].BoolTransitions, 0);

                    if (anyTransitionConversionData.IntTransitions.IsCreated && anyTransitionConversionData.IntTransitions.Length > 0)
                        builder.ConstructFromNativeArray(
                            ref anyStateTransitions[i].IntTransitions,
                            anyTransitionConversionData.IntTransitions.Ptr,
                            anyTransitionConversionData.IntTransitions.Length);
                    else
                        builder.Allocate(ref anyStateTransitions[i].IntTransitions, 0);
                    
                    // Curve keyframes (empty = linear fast-path)
                    if (anyTransitionConversionData.CurveKeyframes.IsCreated && anyTransitionConversionData.CurveKeyframes.Length > 0)
                        builder.ConstructFromNativeArray(
                            ref anyStateTransitions[i].CurveKeyframes,
                            anyTransitionConversionData.CurveKeyframes.Ptr,
                            anyTransitionConversionData.CurveKeyframes.Length);
                    else
                        builder.Allocate(ref anyStateTransitions[i].CurveKeyframes, 0);
                }
            }

            // Exit transition groups
            {
                var exitGroups = builder.Allocate(ref root.ExitTransitionGroups, ExitTransitionGroups.Length);
                for (ushort groupIndex = 0; groupIndex < exitGroups.Length; groupIndex++)
                {
                    var groupData = ExitTransitionGroups[groupIndex];

                    // Build exit state indices
                    if (groupData.ExitStateIndices.IsCreated && groupData.ExitStateIndices.Length > 0)
                        builder.ConstructFromNativeArray(
                            ref exitGroups[groupIndex].ExitStateIndices,
                            groupData.ExitStateIndices.Ptr,
                            groupData.ExitStateIndices.Length);
                    else
                        builder.Allocate(ref exitGroups[groupIndex].ExitStateIndices, 0);

                    // Build exit transitions
                    var exitTransitions = builder.Allocate(ref exitGroups[groupIndex].ExitTransitions, groupData.ExitTransitions.Length);
                    for (ushort transitionIndex = 0; transitionIndex < exitTransitions.Length; transitionIndex++)
                    {
                        var transitionData = groupData.ExitTransitions[transitionIndex];
                        exitTransitions[transitionIndex] = new StateOutTransitionGroup()
                        {
                            ToStateIndex = transitionData.ToStateIndex,
                            TransitionEndTime = transitionData.TransitionEndTime,
                            TransitionDuration = transitionData.TransitionDuration,
                            Offset = transitionData.Offset
                        };

                        if (transitionData.BoolTransitions.IsCreated && transitionData.BoolTransitions.Length > 0)
                            builder.ConstructFromNativeArray(
                                ref exitTransitions[transitionIndex].BoolTransitions,
                                transitionData.BoolTransitions.Ptr,
                                transitionData.BoolTransitions.Length);
                        else
                            builder.Allocate(ref exitTransitions[transitionIndex].BoolTransitions, 0);

                        if (transitionData.IntTransitions.IsCreated && transitionData.IntTransitions.Length > 0)
                            builder.ConstructFromNativeArray(
                                ref exitTransitions[transitionIndex].IntTransitions,
                                transitionData.IntTransitions.Ptr,
                                transitionData.IntTransitions.Length);
                        else
                            builder.Allocate(ref exitTransitions[transitionIndex].IntTransitions, 0);
                    }
                }
            }

            return builder.CreateBlobAssetReference<StateMachineBlob>(Allocator.Persistent);
        }

        public int Compare(ClipIndexWithThreshold x, ClipIndexWithThreshold y)
        {
            return x.Threshold.CompareTo(y.Threshold);
        }

        public unsafe void Dispose()
        {
            // Dispose nested lists in States (must use pointers to avoid struct copies)
            if (States.IsCreated)
            {
                for (int i = 0; i < States.Length; i++)
                {
                    ref var state = ref States.ElementAt(i);
                    if (state.Transitions.IsCreated)
                    {
                        // Dispose nested lists in each transition
                        for (int j = 0; j < state.Transitions.Length; j++)
                        {
                            ref var transition = ref state.Transitions.ElementAt(j);
                            if (transition.BoolTransitions.IsCreated)
                                transition.BoolTransitions.Dispose();
                            if (transition.IntTransitions.IsCreated)
                                transition.IntTransitions.Dispose();
                            if (transition.CurveKeyframes.IsCreated)
                                transition.CurveKeyframes.Dispose();
                        }
                        state.Transitions.Dispose();
                    }
                }
                States.Dispose();
            }

            // Dispose nested lists in LinearBlendStates
            if (LinearBlendStates.IsCreated)
            {
                for (int i = 0; i < LinearBlendStates.Length; i++)
                {
                    ref var linearBlend = ref LinearBlendStates.ElementAt(i);
                    if (linearBlend.ClipsWithThresholds.IsCreated)
                        linearBlend.ClipsWithThresholds.Dispose();
                }
                LinearBlendStates.Dispose();
            }

            // Dispose nested lists in Directional2DBlendStates
            if (Directional2DBlendStates.IsCreated)
            {
                for (int i = 0; i < Directional2DBlendStates.Length; i++)
                {
                    ref var dir2D = ref Directional2DBlendStates.ElementAt(i);
                    if (dir2D.ClipData.IsCreated)
                        dir2D.ClipData.Dispose();
                }
                Directional2DBlendStates.Dispose();
            }

            // Dispose Any State transitions
            if (AnyStateTransitions.IsCreated)
            {
                for (int i = 0; i < AnyStateTransitions.Length; i++)
                {
                    ref var anyTransition = ref AnyStateTransitions.ElementAt(i);
                    if (anyTransition.BoolTransitions.IsCreated)
                        anyTransition.BoolTransitions.Dispose();
                    if (anyTransition.IntTransitions.IsCreated)
                        anyTransition.IntTransitions.Dispose();
                    if (anyTransition.CurveKeyframes.IsCreated)
                        anyTransition.CurveKeyframes.Dispose();
                }
                AnyStateTransitions.Dispose();
            }

            // Dispose Exit Transition Groups
            if (ExitTransitionGroups.IsCreated)
            {
                for (int i = 0; i < ExitTransitionGroups.Length; i++)
                {
                    ref var exitGroup = ref ExitTransitionGroups.ElementAt(i);
                    if (exitGroup.ExitStateIndices.IsCreated)
                        exitGroup.ExitStateIndices.Dispose();

                    if (exitGroup.ExitTransitions.IsCreated)
                    {
                        for (int j = 0; j < exitGroup.ExitTransitions.Length; j++)
                        {
                            ref var exitTransition = ref exitGroup.ExitTransitions.ElementAt(j);
                            if (exitTransition.BoolTransitions.IsCreated)
                                exitTransition.BoolTransitions.Dispose();
                            if (exitTransition.IntTransitions.IsCreated)
                                exitTransition.IntTransitions.Dispose();
                            if (exitTransition.CurveKeyframes.IsCreated)
                                exitTransition.CurveKeyframes.Dispose();
                        }
                        exitGroup.ExitTransitions.Dispose();
                    }
                }
                ExitTransitionGroups.Dispose();
            }

            // Dispose remaining top-level list
            if (SingleClipStates.IsCreated)
                SingleClipStates.Dispose();
        }
    }
}