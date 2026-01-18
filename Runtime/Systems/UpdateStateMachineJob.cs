using System;
using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Entities;
using Unity.Profiling;

namespace DMotion
{
    /// <summary>
    /// Updates state machine transitions and creates animation states.
    /// All states are flattened at conversion time - no runtime hierarchy navigation.
    /// SubStateMachine states from the editor are inlined into the root state machine.
    /// </summary>
    [BurstCompile]
    internal partial struct UpdateStateMachineJob : IJobEntity
    {
        /// <summary>
        /// Offset used to encode exit transition indices.
        /// Exit transitions are encoded as (ExitTransitionIndexOffset + index) to distinguish
        /// them from regular transitions (0 to 999) and any-state transitions (negative).
        /// </summary>
        private const short ExitTransitionIndexOffset = 1000;

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
            ref DynamicBuffer<Directional2DBlendStateMachineState> directional2DBlendStates,
            ref DynamicBuffer<ClipSampler> clipSamplers,
            ref DynamicBuffer<AnimationState> animationStates,
            in AnimationCurrentState animationCurrentState,
            in AnimationStateTransition animationStateTransition,
            in DynamicBuffer<BoolParameter> boolParameters,
            in DynamicBuffer<IntParameter> intParameters,
            in DynamicBuffer<FloatParameter> floatParameters
        )
        {
            using var scope = Marker.Auto();
            ref var stateMachineBlob = ref stateMachine.StateMachineBlob.Value;

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
                LinearBlendStates = linearBlendStates,
                Directional2DBlendStates = directional2DBlendStates
            };
            var parameters = new TransitionParameters(boolParameters, intParameters);

            // Initialize if necessary
            if (!stateMachine.CurrentState.IsValid)
            {
                stateMachine.CurrentState = CreateState(
                    stateMachineBlob.DefaultStateIndex,
                    stateMachine,
                    ref buffers,
                    floatParameters);

                animationStateTransitionRequest = new AnimationStateTransitionRequest
                {
                    AnimationStateId = stateMachine.CurrentState.AnimationStateId,
                    TransitionDuration = 0
                };
            }

            // Evaluate transitions
            var currentStateAnimationState =
                buffers.AnimationStates.GetWithId((byte)stateMachine.CurrentState.AnimationStateId);

            // Evaluate Any State transitions FIRST (Unity behavior)
            // Use negative indices to distinguish Any State from regular transitions
            var shouldStartTransition = EvaluateAnyStateTransitions(
                currentStateAnimationState,
                ref stateMachineBlob,
                parameters,
                (short)stateMachine.CurrentState.StateIndex,
                out var transitionIndex);

            // If no Any State transition matched, check regular state transitions
            if (!shouldStartTransition)
            {
                shouldStartTransition = EvaluateTransitions(
                    currentStateAnimationState,
                    ref stateMachine.CurrentStateBlob,
                    parameters,
                    out transitionIndex);
            }

            // If no regular transition matched, check exit transitions (for sub-state machine exit states)
            if (!shouldStartTransition && stateMachine.CurrentStateBlob.ExitTransitionGroupIndex >= 0)
            {
                shouldStartTransition = EvaluateExitTransitions(
                    currentStateAnimationState,
                    ref stateMachineBlob,
                    stateMachine.CurrentStateBlob.ExitTransitionGroupIndex,
                    parameters,
                    out transitionIndex);
            }

            if (shouldStartTransition)
            {
                // Get transition info based on source:
                // - Negative index (-1 to -N): Any State transition (index = -(transitionIndex + 1))
                // - Zero to 999: Regular state transition
                // - 1000+: Exit transition (encoded as 1000 + exitTransitionIndex)
                short toStateIndex;
                float transitionDuration;

                if (transitionIndex < 0)
                {
                    // Any State transition (negative index)
                    var anyTransitionIndex = (short)(-(transitionIndex + 1));
                    ref var anyTransition = ref stateMachineBlob.AnyStateTransitions[anyTransitionIndex];
                    toStateIndex = anyTransition.ToStateIndex;
                    transitionDuration = anyTransition.TransitionDuration;
                }
                else if (transitionIndex >= ExitTransitionIndexOffset)
                {
                    // Exit transition (encoded as 1000 + index)
                    var exitTransitionIndex = (short)(transitionIndex - ExitTransitionIndexOffset);
                    var exitGroupIndex = stateMachine.CurrentStateBlob.ExitTransitionGroupIndex;
                    ref var exitGroup = ref stateMachineBlob.ExitTransitionGroups[exitGroupIndex];
                    ref var exitTransition = ref exitGroup.ExitTransitions[exitTransitionIndex];
                    toStateIndex = exitTransition.ToStateIndex;
                    transitionDuration = exitTransition.TransitionDuration;
                }
                else
                {
                    // Regular transition (positive index)
                    ref var transition = ref stateMachine.CurrentStateBlob.Transitions[transitionIndex];
                    toStateIndex = transition.ToStateIndex;
                    transitionDuration = transition.TransitionDuration;
                }

                // All states are leaf states (Single or LinearBlend) - no hierarchy navigation needed
#if UNITY_EDITOR || DEBUG
                stateMachine.PreviousState = stateMachine.CurrentState;
#endif
                stateMachine.CurrentState = CreateState(
                    toStateIndex,
                    stateMachine,
                    ref buffers,
                    floatParameters);

                animationStateTransitionRequest = new AnimationStateTransitionRequest
                {
                    AnimationStateId = stateMachine.CurrentState.AnimationStateId,
                    TransitionDuration = transitionDuration,
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
        /// Creates a new animation state. Uses AnimationBufferContext to reduce parameter count.
        /// All states are leaf states (Single or LinearBlend) - SubStateMachines are flattened at conversion.
        /// </summary>
        private StateMachineStateRef CreateState(
            short stateIndex,
            in AnimationStateMachine stateMachine,
            ref AnimationBufferContext buffers,
            in DynamicBuffer<FloatParameter> floatParameters)
        {
            ref var state = ref stateMachine.StateMachineBlob.Value.States[stateIndex];
            var stateRef = new StateMachineStateRef
            {
                StateIndex = (ushort)stateIndex
            };

            // Calculate final speed (base speed * speed parameter if present)
            float finalSpeed = GetFinalSpeed(ref state, floatParameters);

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
                        ref buffers.ClipSamplers,
                        finalSpeed);
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
                        ref buffers.ClipSamplers,
                        finalSpeed);
                    animationStateId = linearClipState.AnimationStateId;
                    break;
                case StateType.Directional2DBlend:
                    var directional2DState = Directional2DBlendStateUtils.NewForStateMachine(
                        (byte)stateIndex,
                        stateMachine.StateMachineBlob,
                        stateMachine.ClipsBlob,
                        stateMachine.ClipEventsBlob,
                        ref buffers.Directional2DBlendStates,
                        ref buffers.AnimationStates,
                        ref buffers.ClipSamplers,
                        finalSpeed);
                    animationStateId = directional2DState.AnimationStateId;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            stateRef.AnimationStateId = (sbyte)animationStateId;
            return stateRef;
        }

        /// <summary>
        /// Calculates final animation speed by multiplying base speed with optional speed parameter.
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetFinalSpeed(ref AnimationStateBlob state, in DynamicBuffer<FloatParameter> floatParameters)
        {
            // If no speed parameter is set, use base speed
            if (state.SpeedParameterIndex == ushort.MaxValue)
            {
                return state.Speed;
            }

            // Get speed multiplier from parameter
            if (state.SpeedParameterIndex < floatParameters.Length)
            {
                float speedMultiplier = floatParameters[(int)state.SpeedParameterIndex].Value;
                return state.Speed * speedMultiplier;
            }

            // Fallback if parameter index is invalid
            return state.Speed;
        }

        /// <summary>
        /// Evaluates Any State transitions (global transitions from any state).
        /// Returns negative index (-1, -2, -3...) to distinguish from regular transitions.
        /// </summary>
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
                    // Return negative index to indicate Any State transition
                    // -1 for index 0, -2 for index 1, etc.
                    transitionIndex = (short)(-(i + 1));
                    return true;
                }
            }

            transitionIndex = -1;
            return false;
        }

        /// <summary>
        /// Evaluates a single Any State transition.
        /// Checks CanTransitionToSelf to prevent self-transitions when not allowed.
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateAnyStateTransition(
            in AnimationState animation,
            ref AnyStateTransition transition,
            in TransitionParameters parameters,
            short currentStateIndex)
        {
            // Check if this would be a self-transition and if that's allowed
            if (!transition.CanTransitionToSelf && transition.ToStateIndex == currentStateIndex)
            {
                return false;
            }
            
            // Check end time if required
            if (transition.HasEndTime && animation.Time < transition.TransitionEndTime)
            {
                return false;
            }

            var shouldTriggerTransition = transition.HasAnyConditions || transition.HasEndTime;

            // Evaluate bool conditions (all must be true)
            {
                ref var boolTransitions = ref transition.BoolTransitions;
                for (var i = 0; i < boolTransitions.Length; i++)
                {
                    var boolTransition = boolTransitions[i];
                    shouldTriggerTransition &= boolTransition.Evaluate(parameters.BoolParameters[boolTransition.ParameterIndex]);
                }
            }

            // Evaluate int conditions (all must be true)
            {
                ref var intTransitions = ref transition.IntTransitions;
                for (var i = 0; i < intTransitions.Length; i++)
                {
                    var intTransition = intTransitions[i];
                    shouldTriggerTransition &= intTransition.Evaluate(parameters.IntParameters[intTransition.ParameterIndex]);
                }
            }

            return shouldTriggerTransition;
        }

        /// <summary>
        /// Evaluates exit transitions for states that are designated as exit states.
        /// Returns encoded transition index (ExitTransitionIndexOffset + index) to distinguish
        /// from regular and any-state transitions.
        /// </summary>
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
                    // Encode as exit transition (offset + index)
                    transitionIndex = (short)(ExitTransitionIndexOffset + i);
                    return true;
                }
            }

            transitionIndex = -1;
            return false;
        }

        /// <summary>
        /// Evaluates all transitions for the current state.
        /// Uses TransitionParameters to bundle parameter buffers.
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
