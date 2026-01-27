using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace DMotion
{
    /// <summary>
    /// Handles animation state blending for multi-layer entities.
    /// Each layer's states are blended independently, then composed by LayerCompositionSystem.
    /// 
    /// Phase 1C: Basic per-layer blending with shared AnimationState buffer.
    /// Samplers are tagged with LayerIndex for later composition.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(ClipSamplingSystem))]
    [UpdateAfter(typeof(BlendAnimationStatesSystem))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct BlendMultiLayerAnimationStatesSystem : ISystem
    {
        [BurstCompile]
        [WithNone(typeof(TimelineControlled))]
        [WithNone(typeof(AnimationStateMachine))] // Only for multi-layer entities
        internal partial struct BlendMultiLayerAnimationStatesJob : IJobEntity
        {
            internal float DeltaTime;

            internal void Execute(
                ref DynamicBuffer<AnimationStateMachineLayer> layers,
                ref DynamicBuffer<AnimationLayerTransition> transitions,
                ref DynamicBuffer<AnimationLayerCurrentState> currentStates,
                ref DynamicBuffer<AnimationLayerTransitionRequest> transitionRequests,
                ref DynamicBuffer<AnimationState> animationStates
            )
            {
                // Process each layer's blend independently
                for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
                {
                    var layer = layers[layerIdx];
                    if (!layer.IsValid)
                        continue;

                    // Find or create layer state tracking
                    int transitionIdx = FindOrCreateLayerTransition(ref transitions, layer.LayerIndex);
                    int currentStateIdx = FindOrCreateLayerCurrentState(ref currentStates, layer.LayerIndex);
                    int requestIdx = FindLayerTransitionRequest(transitionRequests, layer.LayerIndex);

                    ref var transition = ref transitions.ElementAt(transitionIdx);
                    ref var currentState = ref currentStates.ElementAt(currentStateIdx);

                    // Check for new transition request
                    if (requestIdx >= 0)
                    {
                        var request = transitionRequests[requestIdx];
                        if (request.IsValid)
                        {
                            var newToStateIndex = animationStates.IdToIndex((byte)request.AnimationStateId);
                            var transitionDuration = currentState.IsValid ? request.TransitionDuration : 0;

                            if (newToStateIndex >= 0)
                            {
                                transition = new AnimationLayerTransition
                                {
                                    LayerIndex = layer.LayerIndex,
                                    AnimationStateId = request.AnimationStateId,
                                    TransitionDuration = transitionDuration,
                                    TimeElapsed = 0,
                                    CurveSourceStateIndex = request.CurveSourceStateIndex,
                                    CurveSourceTransitionIndex = request.CurveSourceTransitionIndex,
                                    CurveSource = request.CurveSource
                                };
                            }

                            // Clear the request
                            transitionRequests[requestIdx] = AnimationLayerTransitionRequest.Null(layer.LayerIndex);
                        }
                    }

                    // Update animation state times for this layer's states
                    UpdateLayerStateTimes(ref animationStates, layer.LayerIndex, DeltaTime);

                    // Update transition timer
                    if (transition.IsValid)
                    {
                        transition.TimeElapsed += DeltaTime;
                    }

                    var toAnimationStateIndex = animationStates.IdToIndex((byte)transition.AnimationStateId);

                    // Execute blend for this layer
                    if (toAnimationStateIndex >= 0)
                    {
                        // Check if transition has ended
                        if (HasTransitionEnded(transition, animationStates[toAnimationStateIndex]))
                        {
                            currentState = new AnimationLayerCurrentState
                            {
                                LayerIndex = layer.LayerIndex,
                                AnimationStateId = transition.AnimationStateId
                            };
                            transition = AnimationLayerTransition.Null(layer.LayerIndex);
                        }

                        var toAnimationState = animationStates[toAnimationStateIndex];

                        if (mathex.iszero(transition.TransitionDuration))
                        {
                            toAnimationState.Weight = 1;
                        }
                        else
                        {
                            var normalizedTime = math.clamp(
                                transition.TimeElapsed / transition.TransitionDuration, 0, 1);

                            // Apply blend curve if available
                            toAnimationState.Weight = EvaluateBlendWeight(
                                normalizedTime,
                                ref transition,
                                ref layer.StateMachineBlob.Value);
                        }

                        animationStates[toAnimationStateIndex] = toAnimationState;

                        // Normalize weights for this layer's states only
                        NormalizeLayerStateWeights(
                            ref animationStates,
                            layer.LayerIndex,
                            toAnimationStateIndex,
                            toAnimationState.Weight);
                    }
                }
            }

            private static int FindOrCreateLayerTransition(
                ref DynamicBuffer<AnimationLayerTransition> transitions,
                byte layerIndex)
            {
                for (int i = 0; i < transitions.Length; i++)
                {
                    if (transitions[i].LayerIndex == layerIndex)
                        return i;
                }

                transitions.Add(AnimationLayerTransition.Null(layerIndex));
                return transitions.Length - 1;
            }

            private static int FindOrCreateLayerCurrentState(
                ref DynamicBuffer<AnimationLayerCurrentState> currentStates,
                byte layerIndex)
            {
                for (int i = 0; i < currentStates.Length; i++)
                {
                    if (currentStates[i].LayerIndex == layerIndex)
                        return i;
                }

                currentStates.Add(AnimationLayerCurrentState.Null(layerIndex));
                return currentStates.Length - 1;
            }

            private static int FindLayerTransitionRequest(
                in DynamicBuffer<AnimationLayerTransitionRequest> requests,
                byte layerIndex)
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    if (requests[i].LayerIndex == layerIndex)
                        return i;
                }
                return -1;
            }

            private static void UpdateLayerStateTimes(
                ref DynamicBuffer<AnimationState> animationStates,
                byte layerIndex,
                float deltaTime)
            {
                // Only update states belonging to this layer
                for (var i = 0; i < animationStates.Length; i++)
                {
                    var animationState = animationStates[i];
                    if (animationState.LayerIndex != layerIndex)
                        continue;
                        
                    animationState.Time += deltaTime * animationState.Speed;
                    animationStates[i] = animationState;
                }
            }

            private static bool HasTransitionEnded(
                in AnimationLayerTransition transition,
                in AnimationState toState)
            {
                if (!transition.IsValid)
                    return false;

                // Transition ends when duration elapsed OR when target state loops
                return transition.TimeElapsed >= transition.TransitionDuration ||
                       (toState.Time >= 1.0f && transition.TimeElapsed > 0);
            }

            [BurstCompile]
            private static float EvaluateBlendWeight(
                float normalizedTime,
                ref AnimationLayerTransition transition,
                ref StateMachineBlob stateMachineBlob)
            {
                // No curve source = linear blend
                if (!HasCurveSource(transition))
                    return normalizedTime;

                switch (transition.CurveSource)
                {
                    case TransitionSource.State:
                    {
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
                        break;
                }

                return normalizedTime;
            }

            private static bool HasCurveSource(in AnimationLayerTransition transition)
            {
                return transition.CurveSourceStateIndex >= 0 || transition.CurveSourceTransitionIndex >= 0;
            }

            /// <summary>
            /// Normalizes weights for states belonging to a specific layer.
            /// Uses AnimationState.LayerIndex to filter states by layer.
            /// </summary>
            private static void NormalizeLayerStateWeights(
                ref DynamicBuffer<AnimationState> animationStates,
                byte layerIndex,
                int toAnimationStateIndex,
                float toStateWeight)
            {
                // Count states in this layer and sum their weights (excluding target)
                var sumWeights = 0.0f;
                var layerStateCount = 0;
                
                for (var i = 0; i < animationStates.Length; i++)
                {
                    var state = animationStates[i];
                    if (state.LayerIndex != layerIndex)
                        continue;
                        
                    layerStateCount++;
                    if (i != toAnimationStateIndex)
                    {
                        sumWeights += state.Weight;
                    }
                }

                // Only normalize if we have more than one state in this layer
                if (layerStateCount <= 1)
                    return;

                if (mathex.iszero(sumWeights))
                {
                    // Fallback: distribute remaining weight equally among other layer states
                    var otherStateCount = layerStateCount - 1;
                    if (otherStateCount > 0)
                    {
                        var equalWeight = (1 - toStateWeight) / otherStateCount;
                        for (var i = 0; i < animationStates.Length; i++)
                        {
                            var state = animationStates[i];
                            if (state.LayerIndex != layerIndex || i == toAnimationStateIndex)
                                continue;
                                
                            state.Weight = equalWeight;
                            animationStates[i] = state;
                        }
                    }
                }
                else
                {
                    var targetWeight = 1 - toStateWeight;
                    var inverseSumWeights = targetWeight / sumWeights;
                    for (var i = 0; i < animationStates.Length; i++)
                    {
                        var state = animationStates[i];
                        if (state.LayerIndex != layerIndex || i == toAnimationStateIndex)
                            continue;
                            
                        state.Weight *= inverseSumWeights;
                        animationStates[i] = state;
                    }
                }
            }
        }

        /// <summary>
        /// Cleans up animation states with zero weight for multi-layer entities.
        /// Checks both layer transitions and current states to avoid removing active states.
        /// </summary>
        [BurstCompile]
        [WithNone(typeof(TimelineControlled))]
        [WithNone(typeof(AnimationStateMachine))]
        internal partial struct CleanMultiLayerAnimationStatesJob : IJobEntity
        {
            internal void Execute(
                in DynamicBuffer<AnimationLayerTransition> transitions,
                in DynamicBuffer<AnimationLayerCurrentState> currentStates,
                in AnimationPreserveState animationPreserveState,
                ref DynamicBuffer<AnimationState> animationStates,
                ref DynamicBuffer<ClipSampler> samplers)
            {
                for (var i = animationStates.Length - 1; i >= 0; i--)
                {
                    var animationState = animationStates[i];
                    
                    // Check if this state is a transition target for its layer
                    var isTransitionTarget = false;
                    for (int t = 0; t < transitions.Length; t++)
                    {
                        if (transitions[t].LayerIndex == animationState.LayerIndex &&
                            transitions[t].AnimationStateId == animationState.Id)
                        {
                            isTransitionTarget = true;
                            break;
                        }
                    }
                    
                    // Check if this state is the current state for its layer
                    var isCurrentState = false;
                    for (int c = 0; c < currentStates.Length; c++)
                    {
                        if (currentStates[c].LayerIndex == animationState.LayerIndex &&
                            currentStates[c].AnimationStateId == animationState.Id)
                        {
                            isCurrentState = true;
                            break;
                        }
                    }

                    var shouldCleanupState = !isTransitionTarget &&
                                             !isCurrentState &&
                                             animationState.Id != animationPreserveState.AnimationStateId &&
                                             mathex.iszero(animationState.Weight);

                    if (shouldCleanupState)
                    {
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
            var blendHandle = new BlendMultiLayerAnimationStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new CleanMultiLayerAnimationStatesJob().ScheduleParallel(blendHandle);
        }
    }
}
