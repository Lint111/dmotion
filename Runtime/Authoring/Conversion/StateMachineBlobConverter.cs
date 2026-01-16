using System;
using System.Collections.Generic;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

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
    }

    internal struct ClipIndexWithThreshold
    {
        internal int ClipIndex;
        internal float Threshold;
        internal float Speed;
    }
    
    internal struct LinearBlendStateConversionData
    {
        internal UnsafeList<ClipIndexWithThreshold> ClipsWithThresholds;
        internal ushort BlendParameterIndex;
    }

    internal struct SubStateMachineConversionData
    {
        internal StateMachineBlobConverter NestedConverter;
        internal short EntryStateIndex;
        internal UnsafeList<StateOutTransitionConversionData> ExitTransitions;
        internal FixedString64Bytes Name;
    }

    internal struct StateOutTransitionConversionData
    {
        internal short ToStateIndex;
        internal float TransitionDuration;
        internal float TransitionEndTime;
        internal UnsafeList<BoolTransition> BoolTransitions;
        internal UnsafeList<IntTransition> IntTransitions;
    }
    
    [TemporaryBakingType]
    internal struct StateMachineBlobConverter : IComponentData, IComparer<ClipIndexWithThreshold>, IDisposable
    {
        internal byte DefaultStateIndex;
        internal UnsafeList<AnimationStateConversionData> States;
        internal UnsafeList<SingleClipStateBlob> SingleClipStates;
        internal UnsafeList<LinearBlendStateConversionData> LinearBlendStates;
        internal UnsafeList<SubStateMachineConversionData> SubStateMachines;

        // NEW: Any State transitions (global transitions from any state)
        internal UnsafeList<StateOutTransitionConversionData> AnyStateTransitions;

        public readonly unsafe BlobAssetReference<StateMachineBlob> BuildBlob()
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<StateMachineBlob>();
            root.DefaultStateIndex = DefaultStateIndex;

            // Only call ConstructFromNativeArray if we have items (Ptr may be null for empty lists)
            if (SingleClipStates.Length > 0 && SingleClipStates.Ptr != null)
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
                            TransitionDuration = transitionConversionData.TransitionDuration
                        };
                        
                        if (transitionConversionData.BoolTransitions.Length > 0 && transitionConversionData.BoolTransitions.Ptr != null)
                            builder.ConstructFromNativeArray(
                                ref transitions[transitionIndex].BoolTransitions,
                                transitionConversionData.BoolTransitions.Ptr,
                                transitionConversionData.BoolTransitions.Length);
                        else
                            builder.Allocate(ref transitions[transitionIndex].BoolTransitions, 0);

                        if (transitionConversionData.IntTransitions.Length > 0 && transitionConversionData.IntTransitions.Ptr != null)
                            builder.ConstructFromNativeArray(
                                ref transitions[transitionIndex].IntTransitions,
                                transitionConversionData.IntTransitions.Ptr,
                                transitionConversionData.IntTransitions.Length);
                        else
                            builder.Allocate(ref transitions[transitionIndex].IntTransitions, 0);
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
                        { BlendParameterIndex = linearBlendStateConversionData.BlendParameterIndex };

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

            // NEW: Sub-State Machines (WIP - nested blob not yet supported)
            // TODO: Implement proper nested blob using BlobPtr and same-builder construction
            {
                var subStateMachines = builder.Allocate(ref root.SubStateMachines, SubStateMachines.Length);
                for (ushort i = 0; i < subStateMachines.Length; i++)
                {
                    var subMachineConversionData = SubStateMachines[i];

                    // NOTE: NestedConverter.BuildBlob() is not used - nested runtime execution not yet supported
                    // The nested blob would need to be built inline with BlobPtr, not as separate BlobAssetReference

                    subStateMachines[i].EntryStateIndex = subMachineConversionData.EntryStateIndex;
                    subStateMachines[i].Name = subMachineConversionData.Name;

                    // Build exit transitions for this sub-machine
                    var exitTransitions = builder.Allocate(ref subStateMachines[i].ExitTransitions, subMachineConversionData.ExitTransitions.Length);
                    for (ushort j = 0; j < exitTransitions.Length; j++)
                    {
                        var exitTransitionData = subMachineConversionData.ExitTransitions[j];
                        exitTransitions[j] = new StateOutTransitionGroup()
                        {
                            ToStateIndex = exitTransitionData.ToStateIndex,
                            TransitionEndTime = exitTransitionData.TransitionEndTime,
                            TransitionDuration = exitTransitionData.TransitionDuration
                        };

                        if (exitTransitionData.BoolTransitions.Length > 0 && exitTransitionData.BoolTransitions.Ptr != null)
                            builder.ConstructFromNativeArray(
                                ref exitTransitions[j].BoolTransitions,
                                exitTransitionData.BoolTransitions.Ptr,
                                exitTransitionData.BoolTransitions.Length);
                        else
                            builder.Allocate(ref exitTransitions[j].BoolTransitions, 0);

                        if (exitTransitionData.IntTransitions.Length > 0 && exitTransitionData.IntTransitions.Ptr != null)
                            builder.ConstructFromNativeArray(
                                ref exitTransitions[j].IntTransitions,
                                exitTransitionData.IntTransitions.Ptr,
                                exitTransitionData.IntTransitions.Length);
                        else
                            builder.Allocate(ref exitTransitions[j].IntTransitions, 0);
                    }
                }
            }

            // NEW: Any State transitions
            {
                var anyStateTransitions = builder.Allocate(ref root.AnyStateTransitions, AnyStateTransitions.Length);
                for (ushort i = 0; i < anyStateTransitions.Length; i++)
                {
                    var anyTransitionConversionData = AnyStateTransitions[i];
                    anyStateTransitions[i] = new AnyStateTransition()
                    {
                        ToStateIndex = anyTransitionConversionData.ToStateIndex,
                        TransitionEndTime = anyTransitionConversionData.TransitionEndTime,
                        TransitionDuration = anyTransitionConversionData.TransitionDuration
                    };

                    if (anyTransitionConversionData.BoolTransitions.Length > 0 && anyTransitionConversionData.BoolTransitions.Ptr != null)
                        builder.ConstructFromNativeArray(
                            ref anyStateTransitions[i].BoolTransitions,
                            anyTransitionConversionData.BoolTransitions.Ptr,
                            anyTransitionConversionData.BoolTransitions.Length);
                    else
                        builder.Allocate(ref anyStateTransitions[i].BoolTransitions, 0);

                    if (anyTransitionConversionData.IntTransitions.Length > 0 && anyTransitionConversionData.IntTransitions.Ptr != null)
                        builder.ConstructFromNativeArray(
                            ref anyStateTransitions[i].IntTransitions,
                            anyTransitionConversionData.IntTransitions.Ptr,
                            anyTransitionConversionData.IntTransitions.Length);
                    else
                        builder.Allocate(ref anyStateTransitions[i].IntTransitions, 0);
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

            // NEW: Dispose SubStateMachines (recursive disposal)
            if (SubStateMachines.IsCreated)
            {
                for (int i = 0; i < SubStateMachines.Length; i++)
                {
                    ref var subMachine = ref SubStateMachines.ElementAt(i);

                    // Recursively dispose the nested converter
                    subMachine.NestedConverter.Dispose();

                    // Dispose exit transitions
                    if (subMachine.ExitTransitions.IsCreated)
                    {
                        for (int j = 0; j < subMachine.ExitTransitions.Length; j++)
                        {
                            ref var exitTransition = ref subMachine.ExitTransitions.ElementAt(j);
                            if (exitTransition.BoolTransitions.IsCreated)
                                exitTransition.BoolTransitions.Dispose();
                            if (exitTransition.IntTransitions.IsCreated)
                                exitTransition.IntTransitions.Dispose();
                        }
                        subMachine.ExitTransitions.Dispose();
                    }
                }
                SubStateMachines.Dispose();
            }

            // NEW: Dispose Any State transitions
            if (AnyStateTransitions.IsCreated)
            {
                for (int i = 0; i < AnyStateTransitions.Length; i++)
                {
                    ref var anyTransition = ref AnyStateTransitions.ElementAt(i);
                    if (anyTransition.BoolTransitions.IsCreated)
                        anyTransition.BoolTransitions.Dispose();
                    if (anyTransition.IntTransitions.IsCreated)
                        anyTransition.IntTransitions.Dispose();
                }
                AnyStateTransitions.Dispose();
            }

            // Dispose remaining top-level list
            if (SingleClipStates.IsCreated)
                SingleClipStates.Dispose();
        }
    }
}