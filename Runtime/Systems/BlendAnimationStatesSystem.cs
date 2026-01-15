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
        internal partial struct BlendAnimationStatesJob : IJobEntity
        {
            internal float DeltaTime;

            internal void Execute(
                ref AnimationStateTransition animationStateTransition,
                ref AnimationCurrentState animationCurrentState,
                ref AnimationStateTransitionRequest transitionRequest,
                ref DynamicBuffer<AnimationState> animationStates
            )
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
                        };

                        //reset toState time
                        var toState = animationStates[newToStateIndex];
                        toState.Time = 0;
                        animationStates[newToStateIndex] = toState;
                    }

                    transitionRequest = AnimationStateTransitionRequest.Null;
                }

                //Update states
                {
                    for (var i = 0; i < animationStates.Length; i++)
                    {
                        var animationState = animationStates[i];
                        animationState.Time += DeltaTime * animationState.Speed;
                        animationStates[i] = animationState;
                    }
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
                        toAnimationState.Weight = math.clamp(toAnimationState.Time /
                                                             animationStateTransition.TransitionDuration, 0, 1);
                    }

                    animationStates[toAnimationStateIndex] = toAnimationState;

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
                                var equalWeight = (1 - toAnimationState.Weight) / otherStateCount;
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
                            var targetWeight = 1 - toAnimationState.Weight;
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
            }
        }

        [BurstCompile]
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