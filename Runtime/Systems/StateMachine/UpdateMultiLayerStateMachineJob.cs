using System;
using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Entities;
using Unity.Profiling;

namespace DMotion
{
    /// <summary>
    /// Updates multi-layer state machines. Each layer runs an independent state machine
    /// that is later composed by the LayerCompositionSystem.
    /// 
    /// Phase 1C: Basic override blending with layer weights.
    /// Phase 1D: Avatar masks for per-bone layer filtering.
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(TimelineControlled))]
    [WithNone(typeof(AnimationStateMachine))] // Only for multi-layer entities (no single-layer component)
    internal partial struct UpdateMultiLayerStateMachineJob : IJobEntity
    {
        /// <summary>
        /// Offset used to encode exit transition indices.
        /// Exit transitions are encoded as (ExitTransitionIndexOffset + index) to distinguish
        /// them from regular transitions (0 to 999) and any-state transitions (negative).
        /// </summary>
        private const short ExitTransitionIndexOffset = 1000;

        internal ProfilerMarker Marker;

        internal void Execute(
            ref DynamicBuffer<AnimationStateMachineLayer> layers,
            ref DynamicBuffer<AnimationLayerTransitionRequest> transitionRequests,
            ref DynamicBuffer<SingleClipState> singleClipStates,
            ref DynamicBuffer<LinearBlendStateMachineState> linearBlendStates,
            ref DynamicBuffer<Directional2DBlendStateMachineState> directional2DBlendStates,
            ref DynamicBuffer<ClipSampler> clipSamplers,
            ref DynamicBuffer<AnimationState> animationStates,
            in DynamicBuffer<AnimationLayerCurrentState> currentStates,
            in DynamicBuffer<AnimationLayerTransition> transitions,
            in DynamicBuffer<BoolParameter> boolParameters,
            in DynamicBuffer<IntParameter> intParameters,
            in DynamicBuffer<FloatParameter> floatParameters
        )
        {
            using var scope = Marker.Auto();

            // Bundle buffers into context struct
            var buffers = new AnimationBufferContext
            {
                AnimationStates = animationStates,
                ClipSamplers = clipSamplers,
                SingleClipStates = singleClipStates,
                LinearBlendStates = linearBlendStates,
                Directional2DBlendStates = directional2DBlendStates
            };
            var parameters = new TransitionParameters(boolParameters, intParameters);

            // Process each layer independently
            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var layer = layers[layerIdx];
                if (!layer.IsValid)
                    continue;

                // Find matching current state and transition for this layer
                var currentState = FindLayerCurrentState(currentStates, layer.LayerIndex);
                var transition = FindLayerTransition(transitions, layer.LayerIndex);

                if (!ShouldLayerBeActive(currentState, transition, layer.CurrentState))
                    continue;

                ref var stateMachineBlob = ref layer.StateMachineBlob.Value;

                // Initialize if necessary
                if (!layer.CurrentState.IsValid)
                {
                    layer.CurrentState = CreateLayerState(
                        stateMachineBlob.DefaultStateIndex,
                        layer,
                        ref buffers,
                        floatParameters);

                    // Initial state has no transition curve (instant transition)
                    SetLayerTransitionRequest(
                        ref transitionRequests,
                        layer.LayerIndex,
                        (byte)layer.CurrentState.AnimationStateId,
                        transitionDuration: 0,
                        curveSourceStateIndex: -1,
                        curveSourceTransitionIndex: -1,
                        curveSource: TransitionSource.State);

                    layers[layerIdx] = layer;
                    continue;
                }

                // Evaluate transitions
                var currentStateAnimationState =
                    buffers.AnimationStates.GetWithId((byte)layer.CurrentState.AnimationStateId);

                // Evaluate Any State transitions FIRST (Unity behavior)
                var shouldStartTransition = EvaluateAnyStateTransitions(
                    currentStateAnimationState,
                    ref stateMachineBlob,
                    parameters,
                    (short)layer.CurrentState.StateIndex,
                    out var transitionIndex);

                // If no Any State transition matched, check regular state transitions
                if (!shouldStartTransition)
                {
                    ref var currentStateBlob = ref stateMachineBlob.States[layer.CurrentState.StateIndex];
                    shouldStartTransition = EvaluateTransitions(
                        currentStateAnimationState,
                        ref currentStateBlob,
                        parameters,
                        out transitionIndex);

                    // If no regular transition matched, check exit transitions
                    if (!shouldStartTransition && currentStateBlob.ExitTransitionGroupIndex >= 0)
                    {
                        shouldStartTransition = EvaluateExitTransitions(
                            currentStateAnimationState,
                            ref stateMachineBlob,
                            currentStateBlob.ExitTransitionGroupIndex,
                            parameters,
                            out transitionIndex);
                    }
                }

                if (shouldStartTransition)
                {
                    // Get transition info based on source
                    short toStateIndex;
                    float transitionDuration;
                    float transitionOffset;
                    short curveSourceStateIndex;
                    short curveSourceTransitionIndex;
                    TransitionSource curveSource;

                    if (transitionIndex < 0)
                    {
                        // Any State transition (negative index)
                        var anyTransitionIndex = (short)(-(transitionIndex + 1));
                        ref var anyTransition = ref stateMachineBlob.AnyStateTransitions[anyTransitionIndex];
                        toStateIndex = anyTransition.ToStateIndex;
                        transitionDuration = anyTransition.TransitionDuration;
                        transitionOffset = anyTransition.Offset;
                        curveSourceStateIndex = -1;
                        curveSourceTransitionIndex = anyTransitionIndex;
                        curveSource = TransitionSource.AnyState;
                    }
                    else if (transitionIndex >= ExitTransitionIndexOffset)
                    {
                        // Exit transition (encoded as 1000 + index)
                        var exitTransitionIndex = (short)(transitionIndex - ExitTransitionIndexOffset);
                        ref var currentStateBlob = ref stateMachineBlob.States[layer.CurrentState.StateIndex];
                        var exitGroupIndex = currentStateBlob.ExitTransitionGroupIndex;
                        ref var exitGroup = ref stateMachineBlob.ExitTransitionGroups[exitGroupIndex];
                        ref var exitTransition = ref exitGroup.ExitTransitions[exitTransitionIndex];
                        toStateIndex = exitTransition.ToStateIndex;
                        transitionDuration = exitTransition.TransitionDuration;
                        transitionOffset = exitTransition.Offset;
                        curveSourceStateIndex = -1;
                        curveSourceTransitionIndex = -1;
                        curveSource = TransitionSource.Exit;
                    }
                    else
                    {
                        // Regular transition (positive index)
                        ref var currentStateBlob = ref stateMachineBlob.States[layer.CurrentState.StateIndex];
                        ref var stateTransition = ref currentStateBlob.Transitions[transitionIndex];
                        toStateIndex = stateTransition.ToStateIndex;
                        transitionDuration = stateTransition.TransitionDuration;
                        transitionOffset = stateTransition.Offset;
                        curveSourceStateIndex = (short)layer.CurrentState.StateIndex;
                        curveSourceTransitionIndex = transitionIndex;
                        curveSource = TransitionSource.State;
                    }

#if UNITY_EDITOR || DEBUG
                    layer.PreviousState = layer.CurrentState;
#endif
                    layer.CurrentState = CreateLayerState(
                        toStateIndex,
                        layer,
                        ref buffers,
                        floatParameters,
                        intParameters,
                        transitionOffset);

                    SetLayerTransitionRequest(
                        ref transitionRequests,
                        layer.LayerIndex,
                        (byte)layer.CurrentState.AnimationStateId,
                        transitionDuration,
                        curveSourceStateIndex,
                        curveSourceTransitionIndex,
                        curveSource);

                    layers[layerIdx] = layer;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AnimationLayerCurrentState FindLayerCurrentState(
            in DynamicBuffer<AnimationLayerCurrentState> currentStates,
            byte layerIndex)
        {
            for (int i = 0; i < currentStates.Length; i++)
            {
                if (currentStates[i].LayerIndex == layerIndex)
                    return currentStates[i];
            }
            return AnimationLayerCurrentState.Null(layerIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AnimationLayerTransition FindLayerTransition(
            in DynamicBuffer<AnimationLayerTransition> transitions,
            byte layerIndex)
        {
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i].LayerIndex == layerIndex)
                    return transitions[i];
            }
            return AnimationLayerTransition.Null(layerIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetLayerTransitionRequest(
            ref DynamicBuffer<AnimationLayerTransitionRequest> requests,
            byte layerIndex,
            byte animationStateId,
            float transitionDuration,
            short curveSourceStateIndex,
            short curveSourceTransitionIndex,
            TransitionSource curveSource)
        {
            // Find or add request for this layer
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i].LayerIndex == layerIndex)
                {
                    requests[i] = new AnimationLayerTransitionRequest
                    {
                        LayerIndex = layerIndex,
                        AnimationStateId = (sbyte)animationStateId,
                        TransitionDuration = transitionDuration,
                        CurveSourceStateIndex = curveSourceStateIndex,
                        CurveSourceTransitionIndex = curveSourceTransitionIndex,
                        CurveSource = curveSource
                    };
                    return;
                }
            }

            // Add new request
            requests.Add(new AnimationLayerTransitionRequest
            {
                LayerIndex = layerIndex,
                AnimationStateId = (sbyte)animationStateId,
                TransitionDuration = transitionDuration,
                CurveSourceStateIndex = curveSourceStateIndex,
                CurveSourceTransitionIndex = curveSourceTransitionIndex,
                CurveSource = curveSource
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldLayerBeActive(
            in AnimationLayerCurrentState currentState,
            in AnimationLayerTransition transition,
            in StateMachineStateRef layerCurrentState)
        {
            return !currentState.IsValid ||
                   (
                       layerCurrentState.IsValid && currentState.IsValid &&
                       currentState.AnimationStateId == layerCurrentState.AnimationStateId
                   ) ||
                   (
                       layerCurrentState.IsValid && transition.IsValid &&
                       transition.AnimationStateId == layerCurrentState.AnimationStateId
                   );
        }

        /// <summary>
        /// Creates a new animation state for a layer. Sets LayerIndex on created samplers.
        /// </summary>
        private StateMachineStateRef CreateLayerState(
            short stateIndex,
            in AnimationStateMachineLayer layer,
            ref AnimationBufferContext buffers,
            in DynamicBuffer<FloatParameter> floatParameters,
            in DynamicBuffer<IntParameter> intParameters = default,
            float normalizedOffset = 0f)
        {
            ref var state = ref layer.StateMachineBlob.Value.States[stateIndex];
            var stateRef = new StateMachineStateRef
            {
                StateIndex = (ushort)stateIndex
            };

            float finalSpeed = GetFinalSpeed(ref state, floatParameters);

            // Track sampler start index to set LayerIndex on new samplers
            int samplerStartIndex = buffers.ClipSamplers.Length;

            byte animationStateId;
            switch (state.Type)
            {
                case StateType.Single:
                    var singleClipState = SingleClipStateUtils.NewForStateMachine(
                        (byte)stateIndex,
                        layer.StateMachineBlob,
                        layer.ClipsBlob,
                        layer.ClipEventsBlob,
                        ref buffers.SingleClipStates,
                        ref buffers.AnimationStates,
                        ref buffers.ClipSamplers,
                        finalSpeed,
                        normalizedOffset);
                    animationStateId = singleClipState.AnimationStateId;
                    break;
                case StateType.LinearBlend:
                    var linearClipState = LinearBlendStateUtils.NewForStateMachine(
                        (byte)stateIndex,
                        layer.StateMachineBlob,
                        layer.ClipsBlob,
                        layer.ClipEventsBlob,
                        ref buffers.LinearBlendStates,
                        ref buffers.AnimationStates,
                        ref buffers.ClipSamplers,
                        finalSpeed,
                        floatParameters,
                        intParameters,
                        normalizedOffset);
                    animationStateId = linearClipState.AnimationStateId;
                    break;
                case StateType.Directional2DBlend:
                    var directional2DState = Directional2DBlendStateUtils.NewForStateMachine(
                        (byte)stateIndex,
                        layer.StateMachineBlob,
                        layer.ClipsBlob,
                        layer.ClipEventsBlob,
                        ref buffers.Directional2DBlendStates,
                        ref buffers.AnimationStates,
                        ref buffers.ClipSamplers,
                        finalSpeed,
                        floatParameters,
                        normalizedOffset);
                    animationStateId = directional2DState.AnimationStateId;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Set LayerIndex on all newly created samplers
            for (int i = samplerStartIndex; i < buffers.ClipSamplers.Length; i++)
            {
                var sampler = buffers.ClipSamplers[i];
                sampler.LayerIndex = layer.LayerIndex;
                buffers.ClipSamplers[i] = sampler;
            }

            stateRef.AnimationStateId = (sbyte)animationStateId;
            return stateRef;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetFinalSpeed(ref AnimationStateBlob state, in DynamicBuffer<FloatParameter> floatParameters)
        {
            if (state.SpeedParameterIndex == ushort.MaxValue)
                return state.Speed;

            if (state.SpeedParameterIndex < floatParameters.Length)
            {
                float speedMultiplier = floatParameters[(int)state.SpeedParameterIndex].Value;
                return state.Speed * speedMultiplier;
            }

            return state.Speed;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateAnyStateTransitions(
            in AnimationState animation,
            ref StateMachineBlob stateMachine,
            in TransitionParameters parameters,
            short currentStateIndex,
            out short transitionIndex)
        {
            for (short i = 0; i < stateMachine.AnyStateTransitions.Length; i++)
            {
                if (EvaluateAnyStateTransition(animation, ref stateMachine.AnyStateTransitions[i], parameters, currentStateIndex))
                {
                    transitionIndex = (short)(-(i + 1));
                    return true;
                }
            }

            transitionIndex = -1;
            return false;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateAnyStateTransition(
            in AnimationState animation,
            ref AnyStateTransition transition,
            in TransitionParameters parameters,
            short currentStateIndex)
        {
            if (!transition.CanTransitionToSelf && transition.ToStateIndex == currentStateIndex)
                return false;

            if (transition.HasEndTime && animation.Time < transition.TransitionEndTime)
                return false;

            var shouldTriggerTransition = transition.HasAnyConditions || transition.HasEndTime;

            ref var boolTransitions = ref transition.BoolTransitions;
            for (var i = 0; i < boolTransitions.Length; i++)
            {
                var boolTransition = boolTransitions[i];
                shouldTriggerTransition &= boolTransition.Evaluate(parameters.BoolParameters[boolTransition.ParameterIndex]);
            }

            ref var intTransitions = ref transition.IntTransitions;
            for (var i = 0; i < intTransitions.Length; i++)
            {
                var intTransition = intTransitions[i];
                shouldTriggerTransition &= intTransition.Evaluate(parameters.IntParameters[intTransition.ParameterIndex]);
            }

            return shouldTriggerTransition;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateExitTransitions(
            in AnimationState animation,
            ref StateMachineBlob stateMachine,
            short exitGroupIndex,
            in TransitionParameters parameters,
            out short transitionIndex)
        {
            ref var exitGroup = ref stateMachine.ExitTransitionGroups[exitGroupIndex];

            for (short i = 0; i < exitGroup.ExitTransitions.Length; i++)
            {
                if (EvaluateTransitionGroup(animation, ref exitGroup.ExitTransitions[i], parameters))
                {
                    transitionIndex = (short)(ExitTransitionIndexOffset + i);
                    return true;
                }
            }

            transitionIndex = -1;
            return false;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateTransitions(
            in AnimationState animation,
            ref AnimationStateBlob state,
            in TransitionParameters parameters,
            out short transitionIndex)
        {
            for (short i = 0; i < state.Transitions.Length; i++)
            {
                if (EvaluateTransitionGroup(animation, ref state.Transitions[i], parameters))
                {
                    transitionIndex = i;
                    return true;
                }
            }

            transitionIndex = -1;
            return false;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateTransitionGroup(
            in AnimationState animation,
            ref StateOutTransitionGroup transitionGroup,
            in TransitionParameters parameters)
        {
            if (transitionGroup.HasEndTime && animation.Time < transitionGroup.TransitionEndTime)
                return false;

            var shouldTriggerTransition = transitionGroup.HasAnyConditions || transitionGroup.HasEndTime;

            ref var boolTransitions = ref transitionGroup.BoolTransitions;
            for (var i = 0; i < boolTransitions.Length; i++)
            {
                var transition = boolTransitions[i];
                shouldTriggerTransition &= transition.Evaluate(parameters.BoolParameters[transition.ParameterIndex]);
            }

            ref var intTransitions = ref transitionGroup.IntTransitions;
            for (var i = 0; i < intTransitions.Length; i++)
            {
                var transition = intTransitions[i];
                shouldTriggerTransition &= transition.Evaluate(parameters.IntParameters[transition.ParameterIndex]);
            }

            return shouldTriggerTransition;
        }
    }
}
