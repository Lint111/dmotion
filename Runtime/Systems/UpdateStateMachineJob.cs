using System;
using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Entities;
using Unity.Profiling;

namespace DMotion
{
    [BurstCompile]
    internal partial struct UpdateStateMachineJob : IJobEntity
    {
        internal ProfilerMarker Marker;

        /// <summary>
        /// Execute processes state machine updates. While the parameter list appears long,
        /// this is required by IJobEntity. Internal methods use AnimationBufferContext and
        /// TransitionParameters to reduce coupling.
        /// </summary>
        internal void Execute(
            ref AnimationStateMachine stateMachine,
            ref AnimationStateTransitionRequest animationStateTransitionRequest,
            ref DynamicBuffer<SingleClipState> singleClipStates,
            ref DynamicBuffer<LinearBlendStateMachineState> linearBlendStates,
            ref DynamicBuffer<ClipSampler> clipSamplers,
            ref DynamicBuffer<AnimationState> animationStates,
            in AnimationCurrentState animationCurrentState,
            in AnimationStateTransition animationStateTransition,
            in DynamicBuffer<BoolParameter> boolParameters,
            in DynamicBuffer<IntParameter> intParameters
        )
        {
            using var scope = Marker.Auto();

            if (!ShouldStateMachineBeActive(animationCurrentState, animationStateTransition, stateMachine.CurrentState))
            {
                return;
            }

            // Bundle buffers into context structs to reduce coupling in internal methods
            var buffers = new AnimationBufferContext
            {
                AnimationStates = animationStates,
                ClipSamplers = clipSamplers,
                SingleClipStates = singleClipStates,
                LinearBlendStates = linearBlendStates
            };
            var parameters = new TransitionParameters(boolParameters, intParameters);

            // Initialize if necessary
            if (!stateMachine.CurrentState.IsValid)
            {
                stateMachine.CurrentState = CreateState(
                    stateMachine.StateMachineBlob.Value.DefaultStateIndex,
                    stateMachine,
                    ref buffers);

                animationStateTransitionRequest = new AnimationStateTransitionRequest
                {
                    AnimationStateId = stateMachine.CurrentState.AnimationStateId,
                    TransitionDuration = 0
                };
            }

            // Evaluate transitions
            var currentStateAnimationState =
                buffers.AnimationStates.GetWithId((byte)stateMachine.CurrentState.AnimationStateId);

            if (EvaluateTransitions(currentStateAnimationState, ref stateMachine.CurrentStateBlob, parameters, out var transitionIndex))
            {
                ref var transition = ref stateMachine.CurrentStateBlob.Transitions[transitionIndex];

#if UNITY_EDITOR || DEBUG
                stateMachine.PreviousState = stateMachine.CurrentState;
#endif
                stateMachine.CurrentState = CreateState(
                    transition.ToStateIndex,
                    stateMachine,
                    ref buffers);

                animationStateTransitionRequest = new AnimationStateTransitionRequest
                {
                    AnimationStateId = stateMachine.CurrentState.AnimationStateId,
                    TransitionDuration = transition.TransitionDuration,
                };
            }
        }

        public static bool ShouldStateMachineBeActive(in AnimationCurrentState animationCurrentState,
            in AnimationStateTransition animationStateTransition,
            in StateMachineStateRef currentState)
        {
            return !animationCurrentState.IsValid ||
                   (
                       currentState.IsValid && animationCurrentState.IsValid &&
                       animationCurrentState.AnimationStateId ==
                       currentState.AnimationStateId
                   ) ||
                   (
                       currentState.IsValid && animationStateTransition.IsValid &&
                       animationStateTransition.AnimationStateId ==
                       currentState.AnimationStateId
                   );
        }


        /// <summary>
        /// Creates a new animation state. Uses AnimationBufferContext to reduce parameter count
        /// from 8 parameters to 3.
        /// </summary>
        private StateMachineStateRef CreateState(
            short stateIndex,
            in AnimationStateMachine stateMachine,
            ref AnimationBufferContext buffers)
        {
            ref var state = ref stateMachine.StateMachineBlob.Value.States[stateIndex];
            var stateRef = new StateMachineStateRef
            {
                StateIndex = (ushort)stateIndex
            };

            byte animationStateId;
            switch (state.Type)
            {
                case StateType.Single:
                    var singleClipState = SingleClipStateUtils.NewForStateMachine(
                        (byte)stateIndex,
                        stateMachine.StateMachineBlob,
                        stateMachine.ClipsBlob,
                        stateMachine.ClipEventsBlob,
                        ref buffers.SingleClipStates,
                        ref buffers.AnimationStates,
                        ref buffers.ClipSamplers);
                    animationStateId = singleClipState.AnimationStateId;
                    break;
                case StateType.LinearBlend:
                    var linearClipState = LinearBlendStateUtils.NewForStateMachine(
                        (byte)stateIndex,
                        stateMachine.StateMachineBlob,
                        stateMachine.ClipsBlob,
                        stateMachine.ClipEventsBlob,
                        ref buffers.LinearBlendStates,
                        ref buffers.AnimationStates,
                        ref buffers.ClipSamplers);
                    animationStateId = linearClipState.AnimationStateId;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            stateRef.AnimationStateId = (sbyte)animationStateId;
            return stateRef;
        }

        /// <summary>
        /// Evaluates all transitions for the current state.
        /// Uses TransitionParameters to bundle parameter buffers (2 params instead of 4).
        /// </summary>
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
            {
                return false;
            }

            var shouldTriggerTransition = transitionGroup.HasAnyConditions || transitionGroup.HasEndTime;

            // Evaluate bool transitions
            ref var boolTransitions = ref transitionGroup.BoolTransitions;
            for (var i = 0; i < boolTransitions.Length; i++)
            {
                var transition = boolTransitions[i];
                shouldTriggerTransition &= transition.Evaluate(parameters.BoolParameters[transition.ParameterIndex]);
            }

            // Evaluate int transitions
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