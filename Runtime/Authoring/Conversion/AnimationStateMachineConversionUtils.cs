using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Creates a converter with flattened states.
        /// SubStateMachine states are expanded inline - their nested states become top-level states.
        /// </summary>
        internal static StateMachineBlobConverter CreateConverter(StateMachineAsset stateMachineAsset)
        {
            // Flatten the state hierarchy (now also returns exit transition info)
            var (flattenedStates, assetToIndex, exitTransitionInfos) = StateFlattener.FlattenStates(stateMachineAsset);

            // Resolve default state (may be inside a SubStateMachine)
            var resolvedDefault = StateFlattener.ResolveDefaultState(stateMachineAsset);
            Assert.IsTrue(resolvedDefault != null && assetToIndex.ContainsKey(resolvedDefault),
                $"Couldn't resolve default state for {stateMachineAsset.name}");

            var converter = new StateMachineBlobConverter
            {
                DefaultStateIndex = (byte)assetToIndex[resolvedDefault]
            };

            BuildFlattenedStates(stateMachineAsset, flattenedStates, assetToIndex, ref converter, Allocator.Persistent);
            BuildAnyStateTransitions(stateMachineAsset, assetToIndex, ref converter, Allocator.Persistent);
            BuildExitTransitionGroups(stateMachineAsset, exitTransitionInfos, assetToIndex, ref converter, Allocator.Persistent);

            return converter;
        }

        /// <summary>
        /// Builds converter state data from flattened states.
        /// All states are leaf states (Single or LinearBlend) with global indices.
        /// </summary>
        private static void BuildFlattenedStates(
            StateMachineAsset rootMachine,
            List<FlattenedState> flattenedStates,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            ref StateMachineBlobConverter converter,
            Allocator allocator)
        {
            // Count state types
            int singleCount = flattenedStates.Count(s => s.Asset is SingleClipStateAsset);
            int linearCount = flattenedStates.Count(s => s.Asset is LinearBlendStateAsset);

            converter.States = new UnsafeList<AnimationStateConversionData>(flattenedStates.Count, allocator);
            converter.States.Resize(flattenedStates.Count);

            converter.SingleClipStates = new UnsafeList<SingleClipStateBlob>(singleCount, allocator);
            converter.LinearBlendStates = new UnsafeList<LinearBlendStateConversionData>(linearCount, allocator);

            // Track clip index as we build states
            ushort runningClipIndex = 0;

            foreach (var flatState in flattenedStates)
            {
                var stateAsset = flatState.Asset;
                var globalIndex = flatState.GlobalIndex;
                int stateImplIndex;

                switch (stateAsset)
                {
                    case SingleClipStateAsset:
                        stateImplIndex = converter.SingleClipStates.Length;
                        converter.SingleClipStates.Add(new SingleClipStateBlob
                        {
                            ClipIndex = runningClipIndex
                        });
                        runningClipIndex++;
                        break;

                    case LinearBlendStateAsset linearBlendAsset:
                        stateImplIndex = converter.LinearBlendStates.Length;
                        
                        // Determine if using Float or Int parameter
                        var usesIntParameter = linearBlendAsset.UsesIntParameter;
                        int blendParameterIndex;
                        
                        if (usesIntParameter)
                        {
                            // Find index in Int parameters list
                            blendParameterIndex = rootMachine.Parameters
                                .OfType<IntParameterAsset>()
                                .ToList()
                                .FindIndex(p => p == linearBlendAsset.BlendParameter);
                            
                            Assert.IsTrue(blendParameterIndex >= 0,
                                $"({rootMachine.name}) Couldn't find Int blend parameter for state {stateAsset.name}");
                        }
                        else
                        {
                            // Find index in Float parameters list
                            blendParameterIndex = rootMachine.Parameters
                                .OfType<FloatParameterAsset>()
                                .ToList()
                                .FindIndex(p => p == linearBlendAsset.BlendParameter);
                            
                            Assert.IsTrue(blendParameterIndex >= 0,
                                $"({rootMachine.name}) Couldn't find Float blend parameter for state {stateAsset.name}");
                        }

                        var linearBlendData = new LinearBlendStateConversionData
                        {
                            BlendParameterIndex = (ushort)blendParameterIndex,
                            UsesIntParameter = usesIntParameter,
                            IntRangeMin = linearBlendAsset.IntRangeMin,
                            IntRangeMax = linearBlendAsset.IntRangeMax,
                            ClipsWithThresholds = new UnsafeList<ClipIndexWithThreshold>(
                                linearBlendAsset.BlendClips.Length, allocator)
                        };
                        linearBlendData.ClipsWithThresholds.Resize(linearBlendAsset.BlendClips.Length);

                        for (ushort i = 0; i < linearBlendAsset.BlendClips.Length; i++)
                        {
                            linearBlendData.ClipsWithThresholds[i] = new ClipIndexWithThreshold
                            {
                                ClipIndex = runningClipIndex,
                                Threshold = linearBlendAsset.BlendClips[i].Threshold,
                                Speed = linearBlendAsset.BlendClips[i].Speed
                            };
                            runningClipIndex++;
                        }

                        converter.LinearBlendStates.Add(linearBlendData);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(stateAsset),
                            $"Unexpected state type in flattened list: {stateAsset.GetType().Name}");
                }

                // Build state conversion data with transitions
                converter.States[globalIndex] = BuildFlattenedStateConversionData(
                    rootMachine, stateAsset, stateImplIndex, flatState.ExitGroupIndex, assetToIndex, allocator);
            }
        }

        /// <summary>
        /// Builds state conversion data with transitions remapped to flattened indices.
        /// </summary>
        private static AnimationStateConversionData BuildFlattenedStateConversionData(
            StateMachineAsset rootMachine,
            AnimationStateAsset state,
            int stateImplIndex,
            short exitTransitionGroupIndex,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            Allocator allocator)
        {
            var stateData = new AnimationStateConversionData
            {
                Type = state.Type,
                StateIndex = (ushort)stateImplIndex,
                Loop = state.Loop,
                Speed = state.Speed,
                SpeedParameterIndex = ushort.MaxValue,
                ExitTransitionGroupIndex = exitTransitionGroupIndex
            };

            // Resolve speed parameter
            if (state.SpeedParameter != null)
            {
                var speedParamIndex = rootMachine.Parameters
                    .OfType<FloatParameterAsset>()
                    .ToList()
                    .FindIndex(p => p == state.SpeedParameter);

                if (speedParamIndex >= 0)
                {
                    stateData.SpeedParameterIndex = (ushort)speedParamIndex;
                }
            }

            // Build transitions with remapped target indices
            var transitions = state.OutTransitions;
            stateData.Transitions = new UnsafeList<StateOutTransitionConversionData>(transitions.Count, allocator);
            stateData.Transitions.Resize(transitions.Count);

            for (var i = 0; i < transitions.Count; i++)
            {
                stateData.Transitions[i] = BuildFlattenedTransitionData(
                    rootMachine, transitions[i], assetToIndex, allocator);
            }

            return stateData;
        }

        /// <summary>
        /// Builds transition data with target index resolved through flattening.
        /// If target is a SubStateMachine, redirects to its entry state.
        /// </summary>
        private static StateOutTransitionConversionData BuildFlattenedTransitionData(
            StateMachineAsset rootMachine,
            StateOutTransition transition,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            Allocator allocator)
        {
            // Resolve target - may redirect SubStateMachine to entry state
            var toStateIndex = (short)StateFlattener.ResolveTransitionTarget(transition.ToState, assetToIndex);

            var transitionData = new StateOutTransitionConversionData
            {
                ToStateIndex = toStateIndex,
                TransitionEndTime = transition.HasEndTime ? Mathf.Max(0, transition.EndTime) : -1f,
                TransitionDuration = transition.TransitionDuration
            };

            // Bool conditions
            var boolConditions = transition.BoolTransitions.ToArray();
            transitionData.BoolTransitions = new UnsafeList<BoolTransition>(boolConditions.Length, allocator);
            transitionData.BoolTransitions.Resize(boolConditions.Length);

            for (var i = 0; i < boolConditions.Length; i++)
            {
                var boolCond = boolConditions[i];
                var paramIndex = rootMachine.Parameters
                    .OfType<BoolParameterAsset>()
                    .ToList()
                    .FindIndex(p => p == boolCond.BoolParameter);

                Assert.IsTrue(paramIndex >= 0,
                    $"({rootMachine.name}) Bool parameter not found: {boolCond.BoolParameter?.name}");

                transitionData.BoolTransitions[i] = new BoolTransition
                {
                    ComparisonValue = boolCond.ComparisonValue == BoolConditionComparison.True,
                    ParameterIndex = paramIndex
                };
            }

            // Int conditions
            var intConditions = transition.IntTransitions.ToArray();
            var intParams = rootMachine.Parameters.OfType<IntParameterAsset>().ToList();
            transitionData.IntTransitions = new UnsafeList<IntTransition>(intConditions.Length, allocator);
            transitionData.IntTransitions.Resize(intConditions.Length);

            for (var i = 0; i < intConditions.Length; i++)
            {
                var intCond = intConditions[i];
                var paramIndex = intParams.FindIndex(p => p == intCond.IntParameter);

                Assert.IsTrue(paramIndex >= 0,
                    $"({rootMachine.name}) Int parameter not found: {intCond.IntParameter?.name}");

                transitionData.IntTransitions[i] = new IntTransition
                {
                    ParameterIndex = paramIndex,
                    ComparisonValue = intCond.ComparisonValue,
                    ComparisonMode = intCond.ComparisonMode
                };
            }

            return transitionData;
        }

        /// <summary>
        /// Builds Any State transitions with flattened target indices.
        /// </summary>
        private static void BuildAnyStateTransitions(
            StateMachineAsset stateMachineAsset,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            ref StateMachineBlobConverter converter,
            Allocator allocator)
        {
            var anyStateCount = stateMachineAsset.AnyStateTransitions.Count;

            converter.AnyStateTransitions = new UnsafeList<StateOutTransitionConversionData>(anyStateCount, allocator);
            converter.AnyStateTransitions.Resize(anyStateCount);

            for (var i = 0; i < anyStateCount; i++)
            {
                converter.AnyStateTransitions[i] = BuildFlattenedTransitionData(
                    stateMachineAsset,
                    stateMachineAsset.AnyStateTransitions[i],
                    assetToIndex,
                    allocator);
            }
        }

        /// <summary>
        /// Builds exit transition groups from flattened exit state info.
        /// Each SubStateMachine with exit states gets its own group.
        /// </summary>
        private static void BuildExitTransitionGroups(
            StateMachineAsset rootMachine,
            List<ExitTransitionInfo> exitTransitionInfos,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            ref StateMachineBlobConverter converter,
            Allocator allocator)
        {
            var groupCount = exitTransitionInfos.Count;

            converter.ExitTransitionGroups = new UnsafeList<ExitTransitionGroupConversionData>(groupCount, allocator);
            converter.ExitTransitionGroups.Resize(groupCount);

            for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                var info = exitTransitionInfos[groupIndex];

                // Build exit state indices
                var exitStateIndices = new UnsafeList<short>(info.ExitStateIndices.Count, allocator);
                exitStateIndices.Resize(info.ExitStateIndices.Count);
                for (var i = 0; i < info.ExitStateIndices.Count; i++)
                {
                    exitStateIndices[i] = (short)info.ExitStateIndices[i];
                }

                // Build exit transitions
                var exitTransitions = new UnsafeList<StateOutTransitionConversionData>(info.ExitTransitions.Count, allocator);
                exitTransitions.Resize(info.ExitTransitions.Count);
                for (var i = 0; i < info.ExitTransitions.Count; i++)
                {
                    exitTransitions[i] = BuildFlattenedTransitionData(
                        rootMachine,
                        info.ExitTransitions[i],
                        assetToIndex,
                        allocator);
                }

                converter.ExitTransitionGroups[groupIndex] = new ExitTransitionGroupConversionData
                {
                    ExitStateIndices = exitStateIndices,
                    ExitTransitions = exitTransitions
                };
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

            AddStateMachineParameters(dstManager, entity, stateMachineAsset);
        }

        internal static void AddStateMachineSystemComponents(EntityCommands dstManager, Entity entity,
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEventsBlob)
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