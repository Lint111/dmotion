using System;
using System.Linq;
using Latios.Kinemation;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Authoring
{
    public static class AnimationStateMachineConversionUtils
    {
        public static BlobAssetReference<StateMachineBlob> CreateStateMachineBlob(StateMachineAsset stateMachineAsset)
        {
            var converter = CreateConverter(stateMachineAsset);
            try
            {
                return converter.BuildBlob();
            }
            finally
            {
                converter.Dispose();
            }
        }

        internal static StateMachineBlobConverter CreateConverter(StateMachineAsset stateMachineAsset)
        {
            var converter = new StateMachineBlobConverter();
            var defaultStateIndex = stateMachineAsset.States.ToList().IndexOf(stateMachineAsset.DefaultState);
            Assert.IsTrue(defaultStateIndex >= 0,
                $"Couldn't find state {stateMachineAsset.DefaultState.name}, in state machine {stateMachineAsset.name}");
            converter.DefaultStateIndex = (byte)defaultStateIndex;
            BuildStates(stateMachineAsset, ref converter, Allocator.Persistent);

            // NEW: Build Any State transitions
            BuildAnyStateTransitions(stateMachineAsset, ref converter, Allocator.Persistent);

            return converter;
        }

        private static void BuildStates(StateMachineAsset stateMachineAsset, ref StateMachineBlobConverter converter,
            Allocator allocator)
        {
            var singleClipStates = stateMachineAsset.States.OfType<SingleClipStateAsset>().ToArray();
            var linearBlendStates = stateMachineAsset.States.OfType<LinearBlendStateAsset>().ToArray();
            var subStateMachineStates = stateMachineAsset.States.OfType<SubStateMachineStateAsset>().ToArray();

            converter.States =
                new UnsafeList<AnimationStateConversionData>(stateMachineAsset.States.Count, allocator);
            converter.States.Resize(stateMachineAsset.States.Count);

            converter.SingleClipStates =
                new UnsafeList<SingleClipStateBlob>(singleClipStates.Length, allocator);

            converter.LinearBlendStates =
                new UnsafeList<LinearBlendStateConversionData>(linearBlendStates.Length,
                    allocator);

            converter.SubStateMachines =
                new UnsafeList<SubStateMachineConversionData>(subStateMachineStates.Length,
                    allocator);

            ushort clipIndex = 0;
            for (var i = 0; i < converter.States.Length; i++)
            {
                var stateAsset = stateMachineAsset.States[i];
                var stateImplIndex = -1;
                switch (stateAsset)
                {
                    case LinearBlendStateAsset linearBlendStateAsset:
                        stateImplIndex = converter.LinearBlendStates.Length;
                        var blendParameterIndex =
                            stateMachineAsset.Parameters
                                .OfType<FloatParameterAsset>()
                                .ToList()
                                .FindIndex(f => f == linearBlendStateAsset.BlendParameter);

                        Assert.IsTrue(blendParameterIndex >= 0,
                            $"({stateMachineAsset.name}) Couldn't find parameter {linearBlendStateAsset.BlendParameter.name}, for Linear Blend State");

                        var linearBlendState = new LinearBlendStateConversionData()
                        {
                            BlendParameterIndex = (ushort)blendParameterIndex
                        };

                        linearBlendState.ClipsWithThresholds = new UnsafeList<ClipIndexWithThreshold>(
                            linearBlendStateAsset.BlendClips.Length, allocator);

                        linearBlendState.ClipsWithThresholds.Resize(linearBlendStateAsset.BlendClips.Length);
                        for (ushort blendClipIndex = 0;
                             blendClipIndex < linearBlendState.ClipsWithThresholds.Length;
                             blendClipIndex++)
                        {
                            linearBlendState.ClipsWithThresholds[blendClipIndex] = new ClipIndexWithThreshold
                            {
                                ClipIndex = clipIndex,
                                Threshold = linearBlendStateAsset.BlendClips[blendClipIndex].Threshold,
                                Speed = linearBlendStateAsset.BlendClips[blendClipIndex].Speed
                            };
                            clipIndex++;
                        }

                        converter.LinearBlendStates.Add(linearBlendState);
                        break;
                    case SingleClipStateAsset singleClipStateAsset:
                        stateImplIndex = converter.SingleClipStates.Length;
                        converter.SingleClipStates.Add(new SingleClipStateBlob()
                        {
                            ClipIndex = clipIndex,
                        });
                        clipIndex++;
                        break;
                    case SubStateMachineStateAsset subStateMachineAsset:
                        stateImplIndex = converter.SubStateMachines.Length;

                        // Validate sub-state machine
                        Assert.IsTrue(subStateMachineAsset.IsValid(),
                            $"({stateMachineAsset.name}) SubStateMachine state '{stateAsset.name}' has invalid configuration");

                        // Recursively create converter for nested machine
                        var nestedConverter = CreateConverter(subStateMachineAsset.NestedStateMachine);

                        // Find entry state index in nested machine
                        var entryStateIndex = subStateMachineAsset.NestedStateMachine.States
                            .ToList()
                            .IndexOf(subStateMachineAsset.EntryState);
                        Assert.IsTrue(entryStateIndex >= 0,
                            $"({stateMachineAsset.name}) SubStateMachine '{stateAsset.name}': Entry state not found in nested machine");

                        // Build exit transitions
                        var exitTransitions = new UnsafeList<StateOutTransitionConversionData>(
                            subStateMachineAsset.ExitTransitions.Count, allocator);
                        exitTransitions.Resize(subStateMachineAsset.ExitTransitions.Count);

                        for (var exitIndex = 0; exitIndex < exitTransitions.Length; exitIndex++)
                        {
                            var exitTransitionAsset = subStateMachineAsset.ExitTransitions[exitIndex];
                            exitTransitions[exitIndex] = BuildTransitionConversionData(
                                stateMachineAsset, exitTransitionAsset, allocator);
                        }

                        var subMachineData = new SubStateMachineConversionData
                        {
                            NestedConverter = nestedConverter,
                            EntryStateIndex = (short)entryStateIndex,
                            ExitTransitions = exitTransitions,
                            Name = new FixedString64Bytes(stateAsset.name)
                        };

                        converter.SubStateMachines.Add(subMachineData);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(stateAsset));
                }

                Assert.IsTrue(stateImplIndex >= 0, $"Index to state implementation needs to be assigned");
                converter.States[i] =
                    BuildStateConversionData(stateMachineAsset, stateAsset, stateImplIndex, allocator);
            }
        }

        private static StateOutTransitionConversionData BuildTransitionConversionData(
            StateMachineAsset stateMachineAsset,
            StateOutTransition outTransitionAsset,
            Allocator allocator)
        {
            var toStateIndex =
                (short)stateMachineAsset.States.ToList().FindIndex(s => s == outTransitionAsset.ToState);
            Assert.IsTrue(toStateIndex >= 0,
                $"State {outTransitionAsset.ToState.name} not present on State Machine {stateMachineAsset.name}");

            var outTransition = new StateOutTransitionConversionData()
            {
                ToStateIndex = toStateIndex,
                TransitionEndTime = outTransitionAsset.HasEndTime ? Mathf.Max(0, outTransitionAsset.EndTime) : -1f,
                TransitionDuration = outTransitionAsset.TransitionDuration,
            };

            //Create bool transitions
            {
                var boolTransitions = outTransitionAsset.BoolTransitions.ToArray();
                outTransition.BoolTransitions =
                    new UnsafeList<BoolTransition>(boolTransitions.Length, allocator);
                outTransition.BoolTransitions.Resize(boolTransitions.Length);
                for (var boolTransitionIndex = 0;
                     boolTransitionIndex < outTransition.BoolTransitions.Length;
                     boolTransitionIndex++)
                {
                    var boolTransitionAsset = boolTransitions[boolTransitionIndex];
                    var parameterIndex = stateMachineAsset.Parameters
                        .OfType<BoolParameterAsset>()
                        .ToList()
                        .FindIndex(p => p == boolTransitionAsset.BoolParameter);

                    Assert.IsTrue(parameterIndex >= 0,
                        $"({stateMachineAsset.name}) Couldn't find parameter {boolTransitionAsset.BoolParameter.name}, for transition");
                    outTransition.BoolTransitions[boolTransitionIndex] = new BoolTransition
                    {
                        ComparisonValue = boolTransitionAsset.ComparisonValue == BoolConditionComparison.True,
                        ParameterIndex = parameterIndex
                    };
                }
            }

            //Create int transitions
            {
                var intTransitions = outTransitionAsset.IntTransitions.ToArray();
                var intParameters = stateMachineAsset.Parameters
                    .OfType<IntParameterAsset>()
                    .ToList();
                outTransition.IntTransitions =
                    new UnsafeList<IntTransition>(intTransitions.Length, allocator);
                outTransition.IntTransitions.Resize(intTransitions.Length);
                for (var intTransitionIndex = 0;
                     intTransitionIndex < outTransition.IntTransitions.Length;
                     intTransitionIndex++)
                {
                    var intTransitionAsset = intTransitions[intTransitionIndex];
                    var parameterIndex = intParameters.FindIndex(p => p == intTransitionAsset.IntParameter);

                    Assert.IsTrue(parameterIndex >= 0,
                        $"({stateMachineAsset.name}) Couldn't find parameter {intTransitionAsset.IntParameter.name}, for transition");
                    outTransition.IntTransitions[intTransitionIndex] = new IntTransition
                    {
                        ParameterIndex = parameterIndex,
                        ComparisonValue = intTransitionAsset.ComparisonValue,
                        ComparisonMode = intTransitionAsset.ComparisonMode
                    };
                }
            }

            return outTransition;
        }

        private static AnimationStateConversionData BuildStateConversionData(StateMachineAsset stateMachineAsset,
            AnimationStateAsset state, int stateIndex, Allocator allocator)
        {
            var stateConversionData = new AnimationStateConversionData()
            {
                Type = state.Type,
                StateIndex = (ushort)stateIndex,
                Loop = state.Loop,
                Speed = state.Speed,
                SpeedParameterIndex = ushort.MaxValue // Default: no speed parameter
            };

            // Resolve speed parameter index if present
            if (state.SpeedParameter != null)
            {
                var speedParamIndex = stateMachineAsset.Parameters
                    .OfType<FloatParameterAsset>()
                    .ToList()
                    .FindIndex(p => p == state.SpeedParameter);

                if (speedParamIndex >= 0)
                {
                    stateConversionData.SpeedParameterIndex = (ushort)speedParamIndex;
                }
                else
                {
                    Debug.LogWarning($"({stateMachineAsset.name}) Couldn't find speed parameter {state.SpeedParameter?.name} for state {state.name}");
                }
            }

            //Create Transition Groups
            var transitionCount = state.OutTransitions.Count;
            stateConversionData.Transitions =
                new UnsafeList<StateOutTransitionConversionData>(transitionCount, allocator);
            stateConversionData.Transitions.Resize(transitionCount);

            for (var transitionIndex = 0; transitionIndex < stateConversionData.Transitions.Length; transitionIndex++)
            {
                stateConversionData.Transitions[transitionIndex] =
                    BuildTransitionConversionData(stateMachineAsset, state.OutTransitions[transitionIndex], allocator);
            }

            return stateConversionData;
        }

        private static void BuildAnyStateTransitions(StateMachineAsset stateMachineAsset,
            ref StateMachineBlobConverter converter, Allocator allocator)
        {
            var anyStateCount = stateMachineAsset.AnyStateTransitions.Count;

            converter.AnyStateTransitions =
                new UnsafeList<StateOutTransitionConversionData>(anyStateCount, allocator);
            converter.AnyStateTransitions.Resize(anyStateCount);

            for (var i = 0; i < anyStateCount; i++)
            {
                var anyTransitionGroup = stateMachineAsset.AnyStateTransitions[i];

                // Find destination state index
                var toStateIndex =
                    (short)stateMachineAsset.States.ToList().FindIndex(s => s == anyTransitionGroup.ToState);
                Assert.IsTrue(toStateIndex >= 0,
                    $"Any State transition target {anyTransitionGroup.ToState?.name} not present on State Machine {stateMachineAsset.name}");

                var anyTransition = new StateOutTransitionConversionData()
                {
                    ToStateIndex = toStateIndex,
                    TransitionEndTime = anyTransitionGroup.HasEndTime ? Mathf.Max(0, anyTransitionGroup.EndTime) : -1f,
                    TransitionDuration = anyTransitionGroup.TransitionDuration,
                };

                // Create bool transitions
                {
                    var boolTransitions = anyTransitionGroup.BoolTransitions.ToArray();
                    anyTransition.BoolTransitions =
                        new UnsafeList<BoolTransition>(boolTransitions.Length, allocator);
                    anyTransition.BoolTransitions.Resize(boolTransitions.Length);

                    for (var boolTransitionIndex = 0;
                         boolTransitionIndex < anyTransition.BoolTransitions.Length;
                         boolTransitionIndex++)
                    {
                        var boolTransitionAsset = boolTransitions[boolTransitionIndex];
                        var parameterIndex = stateMachineAsset.Parameters
                            .OfType<BoolParameterAsset>()
                            .ToList()
                            .FindIndex(p => p == boolTransitionAsset.BoolParameter);

                        Assert.IsTrue(parameterIndex >= 0,
                            $"({stateMachineAsset.name}) Couldn't find parameter {boolTransitionAsset.BoolParameter?.name}, for Any State transition");

                        anyTransition.BoolTransitions[boolTransitionIndex] = new BoolTransition
                        {
                            ComparisonValue = boolTransitionAsset.ComparisonValue == BoolConditionComparison.True,
                            ParameterIndex = parameterIndex
                        };
                    }
                }

                // Create int transitions
                {
                    var intTransitions = anyTransitionGroup.IntTransitions.ToArray();
                    var intParameters = stateMachineAsset.Parameters
                        .OfType<IntParameterAsset>()
                        .ToList();
                    anyTransition.IntTransitions =
                        new UnsafeList<IntTransition>(intTransitions.Length, allocator);
                    anyTransition.IntTransitions.Resize(intTransitions.Length);

                    for (var intTransitionIndex = 0;
                         intTransitionIndex < anyTransition.IntTransitions.Length;
                         intTransitionIndex++)
                    {
                        var intTransitionAsset = intTransitions[intTransitionIndex];
                        var parameterIndex = intParameters.FindIndex(p => p == intTransitionAsset.IntParameter);

                        Assert.IsTrue(parameterIndex >= 0,
                            $"({stateMachineAsset.name}) Couldn't find parameter {intTransitionAsset.IntParameter?.name}, for Any State transition");

                        anyTransition.IntTransitions[intTransitionIndex] = new IntTransition
                        {
                            ParameterIndex = parameterIndex,
                            ComparisonValue = intTransitionAsset.ComparisonValue,
                            ComparisonMode = intTransitionAsset.ComparisonMode
                        };
                    }
                }

                converter.AnyStateTransitions[i] = anyTransition;
            }

            if (anyStateCount > 0)
            {
                Debug.Log($"[DMotion] Built {anyStateCount} Any State transition(s) for {stateMachineAsset.name}");
            }
        }

        internal static void AddAnimationStateSystemComponents(EntityCommands dstManager, Entity entity)
        {
            dstManager.AddBuffer<AnimationState>(entity);
            dstManager.AddComponent(entity, AnimationStateTransition.Null);
            dstManager.AddComponent(entity, AnimationStateTransitionRequest.Null);
            dstManager.AddComponent(entity, AnimationCurrentState.Null);
            dstManager.AddComponent(entity, AnimationPreserveState.Null);
            var clipSamplers = dstManager.AddBuffer<ClipSampler>(entity);
            clipSamplers.Capacity = 10;
        }

        public static void AddOneShotSystemComponents(EntityCommands dstManager, Entity entity)
        {
            dstManager.AddComponent(entity, PlayOneShotRequest.Null);
            dstManager.AddComponent(entity, OneShotState.Null);
        }

        internal static void AddStateMachineParameters(IBaker dstManager, Entity entity,
            StateMachineAsset stateMachineAsset)
        {
            //Parameters
            {
                var boolParameters = dstManager.AddBuffer<BoolParameter>(entity);
                var intParameters = dstManager.AddBuffer<IntParameter>(entity);
                var floatParameters = dstManager.AddBuffer<FloatParameter>(entity);
                foreach (var p in stateMachineAsset.Parameters)
                {
                    switch (p)
                    {
                        case BoolParameterAsset:
                            boolParameters.Add(new BoolParameter(p.Hash));
                            break;
                        case IntParameterAsset:
                            intParameters.Add(new IntParameter(p.Hash));
                            break;
                        case FloatParameterAsset:
                            floatParameters.Add(new FloatParameter(p.Hash));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(p));
                    }
                }
            }

#if UNITY_EDITOR || DEBUG
            dstManager.AddComponentObject(entity, new AnimationStateMachineDebug
            {
                StateMachineAsset = stateMachineAsset
            });
#endif
        }


        internal static void AddStateMachineParameters(EntityCommands dstManager, Entity entity,
            StateMachineAsset stateMachineAsset)
        {
            //Parameters
            {
                dstManager.AddBuffer<BoolParameter>(entity);
                dstManager.AddBuffer<IntParameter>(entity);
                dstManager.AddBuffer<FloatParameter>(entity);

                var boolParameters = dstManager.GetBuffer<BoolParameter>(entity);
                var intParameters = dstManager.GetBuffer<IntParameter>(entity);
                var floatParameters = dstManager.GetBuffer<FloatParameter>(entity);

                foreach (var p in stateMachineAsset.Parameters)
                {
                    switch (p)
                    {
                        case BoolParameterAsset:
                            boolParameters.Add(new BoolParameter(p.Hash));
                            break;
                        case IntParameterAsset:
                            intParameters.Add(new IntParameter(p.Hash));
                            break;
                        case FloatParameterAsset:
                            floatParameters.Add(new FloatParameter(p.Hash));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(p));
                    }
                }
            }

#if UNITY_EDITOR || DEBUG
            dstManager.AddComponentObject(entity, new AnimationStateMachineDebug
            {
                StateMachineAsset = stateMachineAsset
            });
#endif
        }

        internal static void AddStateMachineSystemComponents(EntityCommands dstManager, Entity entity,
            StateMachineAsset stateMachineAsset,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEventsBlob)
        {
            //state machine data
            {
                var stateMachine = new AnimationStateMachine
                {
                    StateMachineBlob = stateMachineBlob,
                    ClipsBlob = clipsBlob,
                    ClipEventsBlob = clipEventsBlob,
                    CurrentState = StateMachineStateRef.Null
                };

                dstManager.AddComponent(entity, stateMachine);

                dstManager.AddBuffer<SingleClipState>(entity);
                dstManager.AddBuffer<LinearBlendStateMachineState>(entity);
            }
            AddStateMachineParameters(dstManager, entity, stateMachineAsset);
        }

        internal static void AddStateMachineSystemComponents(EntityCommands dstManager, Entity entity,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEventsBlob)
        {
            //state machine data
            {
                var stateMachine = new AnimationStateMachine
                {
                    StateMachineBlob = stateMachineBlob,
                    ClipsBlob = clipsBlob,
                    ClipEventsBlob = clipEventsBlob,
                    CurrentState = StateMachineStateRef.Null
                };

                dstManager.AddComponent(entity, stateMachine);

                dstManager.AddBuffer<SingleClipState>(entity);
                dstManager.AddBuffer<LinearBlendStateMachineState>(entity);
            }
        }

        public static void AddSingleClipStateComponents(EntityCommands dstManager, Entity ownerEntity, Entity entity,
            bool enableEvents = true, bool enableSingleClipRequest = true,
            RootMotionMode rootMotionMode = RootMotionMode.Disabled)
        {
            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(dstManager, entity);

            dstManager.AddBuffer<SingleClipState>(entity);

            if (enableEvents)
            {
                dstManager.AddBuffer<RaisedAnimationEvent>(entity);
            }

            if (enableSingleClipRequest)
            {
                dstManager.AddComponent(entity, PlaySingleClipRequest.Null);
            }

            if (ownerEntity != entity)
            {
                AnimationStateMachineConversionUtils.AddAnimatorOwnerComponents(dstManager, ownerEntity, entity);
            }

            AnimationStateMachineConversionUtils.AddRootMotionComponents(dstManager, ownerEntity, entity,
                rootMotionMode);
        }

        public static void AddAnimatorOwnerComponents(EntityCommands dstManager, Entity ownerEntity, Entity entity)
        {
            dstManager.AddComponent(ownerEntity, new AnimatorOwner { AnimatorEntity = entity });
            dstManager.AddComponent(entity, new AnimatorEntity { Owner = ownerEntity });
        }

        public static void AddRootMotionComponents(EntityCommands dstManager, Entity ownerEntity, Entity entity,
            RootMotionMode rootMotionMode)
        {
            switch (rootMotionMode)
            {
                case RootMotionMode.Disabled:
                    break;
                case RootMotionMode.EnabledAutomatic:
                    dstManager.AddComponent(entity, new RootDeltaTranslation());
                    dstManager.AddComponent(entity, new RootDeltaRotation());
                    if (ownerEntity != entity)
                    {
                        dstManager.AddComponent(ownerEntity, new TransferRootMotionToOwner());
                    }
                    else
                    {
                        dstManager.AddComponent(entity, new ApplyRootMotionToEntity());
                    }

                    break;
                case RootMotionMode.EnabledManual:
                    dstManager.AddComponent(entity, new RootDeltaTranslation());
                    dstManager.AddComponent(entity, new RootDeltaRotation());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}