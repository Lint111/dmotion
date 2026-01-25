using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Assertions;

namespace DMotion
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(ClipSamplingSystem))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct BlendAnimationStatesSystem : ISystem
    {
        [BurstCompile]
        [WithNone(typeof(TimelineControlled))]
        internal partial struct BlendAnimationStatesJob : IJobEntity
        {
            internal float DeltaTime;

            internal void Execute(
                ref AnimationStateTransition animationStateTransition,
                ref AnimationCurrentState animationCurrentState,
                ref AnimationStateTransitionRequest transitionRequest,
                ref DynamicBuffer<AnimationState> animationStates,
                in AnimationStateMachine stateMachine
            )
            {
                ExecuteInternal(
                    ref animationStateTransition,
                    ref animationCurrentState,
                    ref transitionRequest,
                    ref animationStates,
                    DeltaTime,
                    ref stateMachine.StateMachineBlob.Value);
            }
        }
        
        /// <summary>
        /// Job for entities without a state machine (e.g., PlayClipAuthoring).
        /// Uses linear blend only (no curve lookup).
        /// </summary>
        [BurstCompile]
        [WithNone(typeof(AnimationStateMachine))]
        [WithNone(typeof(TimelineControlled))]
        internal partial struct BlendAnimationStatesWithoutStateMachineJob : IJobEntity
        {
            internal float DeltaTime;

            internal void Execute(
                ref AnimationStateTransition animationStateTransition,
                ref AnimationCurrentState animationCurrentState,
                ref AnimationStateTransitionRequest transitionRequest,
                ref DynamicBuffer<AnimationState> animationStates
            )
            {
                ExecuteWithoutCurve(
                    ref animationStateTransition,
                    ref animationCurrentState,
                    ref transitionRequest,
                    ref animationStates,
                    DeltaTime);
            }
        }
        
        /// <summary>
        /// Core blend logic with curve support.
        /// </summary>
        [BurstCompile]
        private static void ExecuteInternal(
            ref AnimationStateTransition animationStateTransition,
            ref AnimationCurrentState animationCurrentState,
            ref AnimationStateTransitionRequest transitionRequest,
            ref DynamicBuffer<AnimationState> animationStates,
            float deltaTime,
            ref StateMachineBlob stateMachineBlob)
        {
            //Check for new transition
            if (transitionRequest.IsValid)
            {
                var newToStateIndex = animationStates.IdToIndex((byte)transitionRequest.AnimationStateId);
                //if we don't have a valid state, just transition instantly
                var transitionDuration = animationCurrentState.IsValid ? transitionRequest.TransitionDuration : 0;
                if (newToStateIndex >= 0)
                {
                    animationStateTransition = new AnimationStateTransition
                    {
                        AnimationStateId = transitionRequest.AnimationStateId,
                        TransitionDuration = transitionDuration,
                        TimeElapsed = 0,
                        // Copy curve source info for curve lookup during blend
                        CurveSourceStateIndex = transitionRequest.CurveSourceStateIndex,
                        CurveSourceTransitionIndex = transitionRequest.CurveSourceTransitionIndex,
                        CurveSource = transitionRequest.CurveSource
                    };

                    //reset toState time
                    var toState = animationStates[newToStateIndex];
                    // IMPORTANT: We do NOT reset toState.Time to 0 here.
                    // The UpdateStateMachineJob is responsible for initializing the state,
                    // potentially with an offset if using Sync or Exit Time logic.
                    // By the time we get here, the state is already created and potentially offset.
                    // toState.Time = 0; // REMOVED
                    animationStates[newToStateIndex] = toState;
                }

                transitionRequest = AnimationStateTransitionRequest.Null;
            }

            //Update states
            {
                for (var i = 0; i < animationStates.Length; i++)
                {
                    var animationState = animationStates[i];
                    animationState.Time += deltaTime * animationState.Speed;
                    animationStates[i] = animationState;
                }
            }

            //Update transition timer
            if (animationStateTransition.IsValid)
            {
                animationStateTransition.TimeElapsed += deltaTime;
            }

            var toAnimationStateIndex = animationStates.IdToIndex((byte)animationStateTransition.AnimationStateId);

            //Execute blend
            if (toAnimationStateIndex >= 0)
            {
                //Check if the current transition has ended
                if (animationStateTransition.HasEnded(animationStates[toAnimationStateIndex]))
                {
                    animationCurrentState =
                        AnimationCurrentState.New(animationStateTransition.AnimationStateId);
                    animationStateTransition = AnimationStateTransition.Null;
                }

                var toAnimationState = animationStates[toAnimationStateIndex];

                if (mathex.iszero(animationStateTransition.TransitionDuration))
                {
                    toAnimationState.Weight = 1;
                }
                else
                {
                    // Calculate normalized time
                    var normalizedTime = math.clamp(animationStateTransition.TimeElapsed /
                                                    animationStateTransition.TransitionDuration, 0, 1);
                    
                    // Apply blend curve if available
                    toAnimationState.Weight = EvaluateBlendWeight(
                        normalizedTime,
                        ref animationStateTransition,
                        ref stateMachineBlob);
                }

                animationStates[toAnimationStateIndex] = toAnimationState;

                NormalizeOtherStateWeights(ref animationStates, toAnimationStateIndex, toAnimationState.Weight);
            }
        }
        
        /// <summary>
        /// Core blend logic without curve support (linear only).
        /// Used for entities without a state machine.
        /// </summary>
        [BurstCompile]
        private static void ExecuteWithoutCurve(
            ref AnimationStateTransition animationStateTransition,
            ref AnimationCurrentState animationCurrentState,
            ref AnimationStateTransitionRequest transitionRequest,
            ref DynamicBuffer<AnimationState> animationStates,
            float deltaTime)
        {
            //Check for new transition
            if (transitionRequest.IsValid)
            {
                var newToStateIndex = animationStates.IdToIndex((byte)transitionRequest.AnimationStateId);
                //if we don't have a valid state, just transition instantly
                var transitionDuration = animationCurrentState.IsValid ? transitionRequest.TransitionDuration : 0;
                if (newToStateIndex >= 0)
                {
                    animationStateTransition = new AnimationStateTransition
                    {
                        AnimationStateId = transitionRequest.AnimationStateId,
                        TransitionDuration = transitionDuration,
                        TimeElapsed = 0,
                        CurveSourceStateIndex = transitionRequest.CurveSourceStateIndex,
                        CurveSourceTransitionIndex = transitionRequest.CurveSourceTransitionIndex,
                        CurveSource = transitionRequest.CurveSource
                    };

                    //reset toState time
                    var toState = animationStates[newToStateIndex];
                    // toState.Time = 0; // REMOVED - see note above
                    animationStates[newToStateIndex] = toState;
                }

                transitionRequest = AnimationStateTransitionRequest.Null;
            }

            //Update states
            {
                for (var i = 0; i < animationStates.Length; i++)
                {
                    var animationState = animationStates[i];
                    animationState.Time += deltaTime * animationState.Speed;
                    animationStates[i] = animationState;
                }
            }

            //Update transition timer
            if (animationStateTransition.IsValid)
            {
                animationStateTransition.TimeElapsed += deltaTime;
            }

            var toAnimationStateIndex = animationStates.IdToIndex((byte)animationStateTransition.AnimationStateId);

            //Execute blend
            if (toAnimationStateIndex >= 0)
            {
                //Check if the current transition has ended
                if (animationStateTransition.HasEnded(animationStates[toAnimationStateIndex]))
                {
                    animationCurrentState =
                        AnimationCurrentState.New(animationStateTransition.AnimationStateId);
                    animationStateTransition = AnimationStateTransition.Null;
                }

                var toAnimationState = animationStates[toAnimationStateIndex];

                if (mathex.iszero(animationStateTransition.TransitionDuration))
                {
                    toAnimationState.Weight = 1;
                }
                else
                {
                    // Linear blend (no curve)
                    toAnimationState.Weight = math.clamp(animationStateTransition.TimeElapsed /
                                                         animationStateTransition.TransitionDuration, 0, 1);
                }

                animationStates[toAnimationStateIndex] = toAnimationState;

                NormalizeOtherStateWeights(ref animationStates, toAnimationStateIndex, toAnimationState.Weight);
            }
        }
        
        /// <summary>
        /// Evaluates the blend weight using the transition's curve if available.
        /// Falls back to linear blend if no curve is defined.
        /// </summary>
        [BurstCompile]
        private static float EvaluateBlendWeight(
            float normalizedTime,
            ref AnimationStateTransition transition,
            ref StateMachineBlob stateMachineBlob)
        {
            // No curve source = linear blend
            if (!transition.HasCurveSource)
                return normalizedTime;
            
            // Look up curve based on source type
            switch (transition.CurveSource)
            {
                case TransitionSource.State:
                {
                    // Regular state transition - look up from States[stateIndex].Transitions[transitionIndex]
                    if (transition.CurveSourceStateIndex >= 0 && 
                        transition.CurveSourceStateIndex < stateMachineBlob.States.Length)
                    {
                        ref var state = ref stateMachineBlob.States[transition.CurveSourceStateIndex];
                        if (transition.CurveSourceTransitionIndex >= 0 &&
                            transition.CurveSourceTransitionIndex < state.Transitions.Length)
                        {
                            ref var stateTransition = ref state.Transitions[transition.CurveSourceTransitionIndex];
                            if (stateTransition.HasCurve)
                            {
                                return CurveUtils.EvaluateCurve(ref stateTransition.CurveKeyframes, normalizedTime);
                            }
                        }
                    }
                    break;
                }
                
                case TransitionSource.AnyState:
                {
                    // Any State transition - look up from AnyStateTransitions[transitionIndex]
                    if (transition.CurveSourceTransitionIndex >= 0 &&
                        transition.CurveSourceTransitionIndex < stateMachineBlob.AnyStateTransitions.Length)
                    {
                        ref var anyTransition = ref stateMachineBlob.AnyStateTransitions[transition.CurveSourceTransitionIndex];
                        if (anyTransition.HasCurve)
                        {
                            return CurveUtils.EvaluateCurve(ref anyTransition.CurveKeyframes, normalizedTime);
                        }
                    }
                    break;
                }
                
                case TransitionSource.Exit:
                    // Exit transitions don't have curves - parent handles blend
                    break;
            }
            
            // Fallback to linear
            return normalizedTime;
        }
        
        /// <summary>
        /// Normalizes weights of all states except the target state.
        /// Extracted to reduce code duplication between curve and non-curve paths.
        /// </summary>
        [BurstCompile]
        private static void NormalizeOtherStateWeights(
            ref DynamicBuffer<AnimationState> animationStates,
            int toAnimationStateIndex,
            float toStateWeight)
        {
            //We only blend if we have more than one state
            if (animationStates.Length > 1)
            {
                //normalize weights
                var sumWeights = 0.0f;
                for (var i = 0; i < animationStates.Length; i++)
                {
                    if (i != toAnimationStateIndex)
                    {
                        sumWeights += animationStates[i].Weight;
                    }
                }

                // Handle edge case where remaining weights are zero
                // This can happen if states weren't properly cleaned up
                if (mathex.iszero(sumWeights))
                {
                    // Fallback: distribute remaining weight equally among other states
                    var otherStateCount = animationStates.Length - 1;
                    if (otherStateCount > 0)
                    {
                        var equalWeight = (1 - toStateWeight) / otherStateCount;
                        for (var i = 0; i < animationStates.Length; i++)
                        {
                            if (i != toAnimationStateIndex)
                            {
                                var animationState = animationStates[i];
                                animationState.Weight = equalWeight;
                                animationStates[i] = animationState;
                            }
                        }
                    }
                }
                else
                {
                    var targetWeight = 1 - toStateWeight;
                    var inverseSumWeights = targetWeight / sumWeights;
                    for (var i = 0; i < animationStates.Length; i++)
                    {
                        if (i != toAnimationStateIndex)
                        {
                            var animationState = animationStates[i];
                            animationState.Weight *= inverseSumWeights;
                            animationStates[i] = animationState;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        [WithNone(typeof(TimelineControlled))]
        internal partial struct CleanAnimationStatesJob : IJobEntity
        {
            internal void Execute(
                in AnimationStateTransition transition,
                in AnimationPreserveState animationPreserveState,
                ref DynamicBuffer<AnimationState> animationStates,
                ref DynamicBuffer<ClipSampler> samplers)
            {
                //After all transitions are handled, clean up animationState states with zero Weights
                var toAnimationStateIndex = animationStates.IdToIndex((byte)transition.AnimationStateId);
                for (var i = animationStates.Length - 1; i >= 0; i--)
                {
                    var animationState = animationStates[i];
                    var shouldCleanupState = i != toAnimationStateIndex &&
                                             animationState.Id != animationPreserveState.AnimationStateId &&
                                             mathex.iszero(animationState.Weight);
                    if (shouldCleanupState)
                    {
                        //TODO (perf): Could we improve performance by batching all removes? (we may need to pay for sorting at the end)
                        var removeCount = animationState.ClipCount;
                        Assert.IsTrue(removeCount > 0,
                            "AnimationState doesn't declare clip count to remove. This will lead to sampler leak");
                        samplers.RemoveRangeWithId(animationState.StartSamplerId, removeCount);
                        animationStates.RemoveAt(i);
                    }
                }
            }
        }

        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Blend job must complete before cleanup job runs
            var blendHandle = new BlendAnimationStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
            
            state.Dependency = new CleanAnimationStatesJob().ScheduleParallel(blendHandle);
        }
    }
}