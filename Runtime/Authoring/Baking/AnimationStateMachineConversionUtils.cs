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
    /// <summary>
    /// Cached parameter lists to avoid repeated LINQ allocations during conversion.
    /// Created once per state machine conversion and passed through the build pipeline.
    /// </summary>
    internal readonly struct ParameterCache
    {
        public readonly List<BoolParameterAsset> BoolParameters;
        public readonly List<IntParameterAsset> IntParameters;
        public readonly List<FloatParameterAsset> FloatParameters;
        public readonly StateMachineAsset RootMachine;

        public ParameterCache(StateMachineAsset rootMachine)
        {
            RootMachine = rootMachine;
            BoolParameters = new List<BoolParameterAsset>();
            IntParameters = new List<IntParameterAsset>();
            FloatParameters = new List<FloatParameterAsset>();

            // Single pass to categorize all parameters (avoids multiple OfType<T>().ToList() calls)
            foreach (var param in rootMachine.Parameters)
            {
                switch (param)
                {
                    case BoolParameterAsset boolParam:
                        BoolParameters.Add(boolParam);
                        break;
                    case IntParameterAsset intParam:
                        IntParameters.Add(intParam);
                        break;
                    case FloatParameterAsset floatParam:
                        FloatParameters.Add(floatParam);
                        break;
                }
            }
        }

        public int FindBoolParameterIndex(AnimationParameterAsset parameter, FlattenedState? flattenedState)
        {
            return FindParameterIndex(BoolParameters, parameter, flattenedState);
        }

        public int FindIntParameterIndex(AnimationParameterAsset parameter, FlattenedState? flattenedState)
        {
            return FindParameterIndex(IntParameters, parameter, flattenedState);
        }

        public int FindFloatParameterIndex(AnimationParameterAsset parameter, FlattenedState? flattenedState)
        {
            return FindParameterIndex(FloatParameters, parameter, flattenedState);
        }

        private int FindParameterIndex<T>(List<T> typedParams, AnimationParameterAsset parameter, FlattenedState? flattenedState)
            where T : AnimationParameterAsset
        {
            if (parameter == null)
                return -1;

            // First try direct lookup
            for (int i = 0; i < typedParams.Count; i++)
            {
                if (typedParams[i] == parameter)
                    return i;
            }

            // Try parameter linking - check if this parameter is linked from a parent parameter
            if (flattenedState.HasValue && flattenedState.Value.SourceSubMachine != null)
            {
                var link = RootMachine.FindLinkForTarget(parameter, flattenedState.Value.SourceSubMachine);
                if (link.HasValue && link.Value.SourceParameter is T sourceParam)
                {
                    for (int i = 0; i < typedParams.Count; i++)
                    {
                        if (typedParams[i] == sourceParam)
                            return i;
                    }
                }
            }

            // Fallback: search by name (for manual setups where links weren't established)
            var paramName = parameter.name.ToLowerInvariant();
            for (int i = 0; i < typedParams.Count; i++)
            {
                if (typedParams[i].name.ToLowerInvariant() == paramName)
                    return i;
            }

            return -1;
        }
    }

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
                converter.DisposeNativeCollections();
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

            // Create parameter cache once for entire conversion (avoids repeated OfType<T>().ToList() calls)
            var parameterCache = new ParameterCache(stateMachineAsset);

            try
            {
                BuildFlattenedStates(parameterCache, flattenedStates, assetToIndex, ref converter, Allocator.Persistent);
                BuildAnyStateTransitions(parameterCache, assetToIndex, ref converter, Allocator.Persistent);
                BuildExitTransitionGroups(parameterCache, exitTransitionInfos, assetToIndex, ref converter, Allocator.Persistent);
            }
            catch
            {
                // Dispose partially-built converter to prevent native memory leaks
                converter.DisposeNativeCollections();
                throw;
            }

            return converter;
        }

        /// <summary>
        /// Builds converter state data from flattened states.
        /// All states are leaf states (Single or LinearBlend) with global indices.
        /// </summary>
        private static void BuildFlattenedStates(
            in ParameterCache parameterCache,
            List<FlattenedState> flattenedStates,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            ref StateMachineBlobConverter converter,
            Allocator allocator)
        {
            // Count state types without LINQ
            int singleCount = 0, linearCount = 0, directional2DCount = 0;
            foreach (var state in flattenedStates)
            {
                switch (state.Asset)
                {
                    case SingleClipStateAsset: singleCount++; break;
                    case LinearBlendStateAsset: linearCount++; break;
                    case Directional2DBlendStateAsset: directional2DCount++; break;
                }
            }

            converter.States = new UnsafeList<AnimationStateConversionData>(flattenedStates.Count, allocator);
            converter.States.Resize(flattenedStates.Count);

            converter.SingleClipStates = new UnsafeList<SingleClipStateBlob>(singleCount, allocator);
            converter.LinearBlendStates = new UnsafeList<LinearBlendStateConversionData>(linearCount, allocator);
            converter.Directional2DBlendStates = new UnsafeList<Directional2DBlendStateConversionData>(directional2DCount, allocator);

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
                            // Find index in Int parameters list (with link resolution)
                            blendParameterIndex = parameterCache.FindIntParameterIndex(
                                linearBlendAsset.BlendParameter, flatState);
                            
                            Assert.IsTrue(blendParameterIndex >= 0,
                                $"({parameterCache.RootMachine.name}) Couldn't find Int blend parameter for state {stateAsset.name}");
                        }
                        else
                        {
                            // Find index in Float parameters list (with link resolution)
                            blendParameterIndex = parameterCache.FindFloatParameterIndex(
                                linearBlendAsset.BlendParameter, flatState);
                            
                            Assert.IsTrue(blendParameterIndex >= 0,
                                $"({parameterCache.RootMachine.name}) Couldn't find Float blend parameter for state {stateAsset.name}");
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
                    
                    case Directional2DBlendStateAsset directional2DAsset:
                        stateImplIndex = converter.Directional2DBlendStates.Length;

                        var blendParamIndexX = parameterCache.FindFloatParameterIndex(
                            directional2DAsset.BlendParameterX, flatState);
                        var blendParamIndexY = parameterCache.FindFloatParameterIndex(
                            directional2DAsset.BlendParameterY, flatState);

                        Assert.IsTrue(blendParamIndexX >= 0,
                            $"({parameterCache.RootMachine.name}) Couldn't find Float blend parameter X for state {stateAsset.name}");
                        Assert.IsTrue(blendParamIndexY >= 0,
                            $"({parameterCache.RootMachine.name}) Couldn't find Float blend parameter Y for state {stateAsset.name}");

                        var dir2DConvData = new Directional2DBlendStateConversionData
                        {
                            BlendParameterIndexX = (ushort)blendParamIndexX,
                            BlendParameterIndexY = (ushort)blendParamIndexY,
                            Algorithm = directional2DAsset.Algorithm,
                            ClipData = new UnsafeList<Directional2DClipData>(directional2DAsset.BlendClips.Length, allocator)
                        };
                        dir2DConvData.ClipData.Resize(directional2DAsset.BlendClips.Length);

                        for (int i = 0; i < directional2DAsset.BlendClips.Length; i++)
                        {
                            dir2DConvData.ClipData[i] = new Directional2DClipData
                            {
                                ClipIndex = runningClipIndex,
                                Position = directional2DAsset.BlendClips[i].Position,
                                Speed = directional2DAsset.BlendClips[i].Speed
                            };
                            runningClipIndex++;
                        }
                        
                        converter.Directional2DBlendStates.Add(dir2DConvData);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(stateAsset),
                            $"Unexpected state type in flattened list: {stateAsset.GetType().Name}");
                }

                // Build state conversion data with transitions
                converter.States[globalIndex] = BuildFlattenedStateConversionData(
                    parameterCache, flatState, stateImplIndex, assetToIndex, allocator);
            }
        }

        /// <summary>
        /// Builds state conversion data with transitions remapped to flattened indices.
        /// </summary>
        private static AnimationStateConversionData BuildFlattenedStateConversionData(
            in ParameterCache parameterCache,
            FlattenedState flatState,
            int stateImplIndex,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            Allocator allocator)
        {
            var state = flatState.Asset;
            var stateData = new AnimationStateConversionData
            {
                Type = state.Type,
                StateIndex = (ushort)stateImplIndex,
                Loop = state.Loop,
                Speed = state.Speed,
                SpeedParameterIndex = ushort.MaxValue,
                ExitTransitionGroupIndex = flatState.ExitGroupIndex
            };

            // Resolve speed parameter (with link resolution for nested states)
            if (state.SpeedParameter != null)
            {
                var speedParamIndex = parameterCache.FindFloatParameterIndex(state.SpeedParameter, flatState);

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
                    parameterCache, transitions[i], flatState, assetToIndex, allocator);
            }

            return stateData;
        }

        /// <summary>
        /// Builds transition data with target index resolved through flattening.
        /// If target is a SubStateMachine, redirects to its entry state.
        /// </summary>
        private static StateOutTransitionConversionData BuildFlattenedTransitionData(
            in ParameterCache parameterCache,
            StateOutTransition transition,
            FlattenedState? sourceState,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            Allocator allocator)
        {
            // Resolve target - may redirect SubStateMachine to entry state
            var toStateIndex = (short)StateFlattener.ResolveTransitionTarget(transition.ToState, assetToIndex);

            var transitionData = new StateOutTransitionConversionData
            {
                ToStateIndex = toStateIndex,
                TransitionEndTime = transition.HasEndTime ? Mathf.Max(0, transition.EndTime) : -1f,
                TransitionDuration = transition.TransitionDuration,
                CanTransitionToSelf = transition.CanTransitionToSelf
            };

            // Bool conditions (with link resolution for nested states)
            // Use foreach to avoid ToArray() allocation
            transitionData.BoolTransitions = new UnsafeList<BoolTransition>(4, allocator);
            foreach (var boolCond in transition.BoolTransitions)
            {
                var paramIndex = parameterCache.FindBoolParameterIndex(boolCond.BoolParameter, sourceState);

                Assert.IsTrue(paramIndex >= 0,
                    $"({parameterCache.RootMachine.name}) Bool parameter not found: {boolCond.BoolParameter?.name}");

                transitionData.BoolTransitions.Add(new BoolTransition
                {
                    ComparisonValue = boolCond.ComparisonValue == BoolConditionComparison.True,
                    ParameterIndex = paramIndex
                });
            }

            // Int conditions (with link resolution for nested states)
            // Use foreach to avoid ToArray() allocation
            transitionData.IntTransitions = new UnsafeList<IntTransition>(4, allocator);
            foreach (var intCond in transition.IntTransitions)
            {
                var paramIndex = parameterCache.FindIntParameterIndex(intCond.IntParameter, sourceState);

                Assert.IsTrue(paramIndex >= 0,
                    $"({parameterCache.RootMachine.name}) Int parameter not found: {intCond.IntParameter?.name}");

                transitionData.IntTransitions.Add(new IntTransition
                {
                    ParameterIndex = paramIndex,
                    ComparisonValue = intCond.ComparisonValue,
                    ComparisonMode = intCond.ComparisonMode
                });
            }

            // Convert blend curve (empty = linear fast-path)
            transitionData.CurveKeyframes = ConvertBlendCurve(transition.BlendCurve, allocator);

            return transitionData;
        }

        /// <summary>
        /// Converts an AnimationCurve to blittable CurveKeyframes.
        /// Returns empty list for linear curves (fast-path optimization).
        /// </summary>
        private static UnsafeList<CurveKeyframe> ConvertBlendCurve(AnimationCurve curve, Allocator allocator)
        {
            // Fast-path: linear curve = empty keyframes = zero storage, zero runtime cost
            if (IsLinearCurve(curve))
            {
                return new UnsafeList<CurveKeyframe>(0, allocator);
            }

            // Custom curve: convert keyframes for Hermite evaluation
            var keyframes = new UnsafeList<CurveKeyframe>(curve.length, allocator);
            keyframes.Resize(curve.length);

            for (int i = 0; i < curve.length; i++)
            {
                var key = curve.keys[i];
                // Invert Y: Unity stores "From" weight (1→0), DMotion uses "To" weight (0→1)
                // Negate tangents due to Y inversion
                keyframes[i] = CurveKeyframe.Create(
                    key.time,
                    1f - key.value,
                    -key.inTangent,
                    -key.outTangent);
            }

            return keyframes;
        }

        /// <summary>
        /// Detects if curve is the default linear transition (0,1) → (1,0).
        /// Linear curves get empty keyframes array for zero-cost runtime.
        /// </summary>
        private static bool IsLinearCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return true;
            if (curve.length != 2) return false;

            var k0 = curve.keys[0];
            var k1 = curve.keys[1];

            // Use shared epsilon constant for consistency with CurveUtils
            const float epsilon = CurveUtils.LinearCurveEpsilon;

            // Check: (0,1) → (1,0) with tangent ≈ -1 (Unity's default linear)
            return Mathf.Abs(k0.time) < epsilon &&
                   Mathf.Abs(k0.value - 1f) < epsilon &&
                   Mathf.Abs(k1.time - 1f) < epsilon &&
                   Mathf.Abs(k1.value) < epsilon &&
                   Mathf.Abs(k0.outTangent + 1f) < epsilon &&
                   Mathf.Abs(k1.inTangent + 1f) < epsilon;
        }

        /// <summary>
        /// Builds Any State transitions with flattened target indices.
        /// Any State transitions are always at root level, so no parameter linking is needed.
        /// </summary>
        private static void BuildAnyStateTransitions(
            in ParameterCache parameterCache,
            Dictionary<AnimationStateAsset, int> assetToIndex,
            ref StateMachineBlobConverter converter,
            Allocator allocator)
        {
            var anyStateCount = parameterCache.RootMachine.AnyStateTransitions.Count;

            converter.AnyStateTransitions = new UnsafeList<StateOutTransitionConversionData>(anyStateCount, allocator);
            converter.AnyStateTransitions.Resize(anyStateCount);

            for (var i = 0; i < anyStateCount; i++)
            {
                converter.AnyStateTransitions[i] = BuildFlattenedTransitionData(
                    parameterCache,
                    parameterCache.RootMachine.AnyStateTransitions[i],
                    null, // Any State transitions are at root level
                    assetToIndex,
                    allocator);
            }
        }

        /// <summary>
        /// Builds exit transition groups from flattened exit state info.
        /// Each SubStateMachine with exit states gets its own group.
        /// </summary>
        private static void BuildExitTransitionGroups(
            in ParameterCache parameterCache,
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
                // Exit transitions are defined on the SubStateMachine, which is in the parent scope
                // so they reference root-level parameters directly (no link resolution needed)
                var exitTransitions = new UnsafeList<StateOutTransitionConversionData>(info.ExitTransitions.Count, allocator);
                exitTransitions.Resize(info.ExitTransitions.Count);
                for (var i = 0; i < info.ExitTransitions.Count; i++)
                {
                    exitTransitions[i] = BuildFlattenedTransitionData(
                        parameterCache,
                        info.ExitTransitions[i],
                        null, // Exit transitions use root-level parameters
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
            dstManager.AddBuffer<Directional2DBlendStateMachineState>(entity);

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
            dstManager.AddBuffer<Directional2DBlendStateMachineState>(entity);
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