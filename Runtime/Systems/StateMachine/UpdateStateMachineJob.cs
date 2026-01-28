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
    [WithNone(typeof(TimelineControlled))]
    internal partial struct UpdateStateMachineJob : IJobEntity
    {
        /// <summary>
        /// Offset used to encode exit transition indices.
        /// Exit transitions are encoded as (ExitTransitionIndexOffset + index) to distinguish
        /// them from regular transitions (0 to 999) and any-state transitions (negative).
        ///
        /// NOTE: This encoding scheme is deprecated and will be replaced with TransitionRef in a future update.
        /// For now, use the helper methods EncodeTransition/DecodeTransition to make the encoding explicit.
        /// </summary>
        private const short ExitTransitionIndexOffset = 1000;

        internal ProfilerMarker Marker;

        #region Transition Index Encoding Helpers

        /// <summary>
        /// Encodes a state transition index.
        /// Range: 0 to 999
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short EncodeStateTransition(short stateTransitionIndex) => stateTransitionIndex;

        /// <summary>
        /// Encodes an Any State transition index.
        /// Returns negative values: -1 for index 0, -2 for index 1, etc.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short EncodeAnyStateTransition(short anyStateTransitionIndex) => (short)(-(anyStateTransitionIndex + 1));

        /// <summary>
        /// Encodes an Exit transition index.
        /// Range: 1000+
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short EncodeExitTransition(short exitTransitionIndex) => (short)(ExitTransitionIndexOffset + exitTransitionIndex);

        /// <summary>
        /// Decodes an encoded transition index and returns its source type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TransitionSource DecodeTransitionSource(short encodedIndex)
        {
            if (encodedIndex < 0)
                return TransitionSource.AnyState;
            if (encodedIndex >= ExitTransitionIndexOffset)
                return TransitionSource.Exit;
            return TransitionSource.State;
        }

        /// <summary>
        /// Decodes an Any State transition index.
        /// Input: -1, -2, -3... Output: 0, 1, 2...
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short DecodeAnyStateTransition(short encodedIndex) => (short)(-(encodedIndex + 1));

        /// <summary>
        /// Decodes an Exit transition index.
        /// Input: 1000, 1001, 1002... Output: 0, 1, 2...
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short DecodeExitTransition(short encodedIndex) => (short)(encodedIndex - ExitTransitionIndexOffset);

        #endregion

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

                // Initial state has no transition curve (instant transition)
                animationStateTransitionRequest = AnimationStateTransitionRequest.New(
                    (byte)stateMachine.CurrentState.AnimationStateId,
                    transitionDuration: 0,
                    curveSourceStateIndex: -1,
                    curveSourceTransitionIndex: -1,
                    curveSource: TransitionSource.State);
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
                // Get transition info based on source type (decoded from encoded index)
                short toStateIndex;
                float transitionDuration;
                float transitionOffset;
                short curveSourceStateIndex;
                short curveSourceTransitionIndex;
                TransitionSource curveSource;

                var transitionSource = DecodeTransitionSource(transitionIndex);

                if (transitionSource == TransitionSource.AnyState)
                {
                    // Any State transition
                    var anyTransitionIndex = DecodeAnyStateTransition(transitionIndex);
                    ref var anyTransition = ref stateMachineBlob.AnyStateTransitions[anyTransitionIndex];
                    toStateIndex = anyTransition.ToStateIndex;
                    transitionDuration = anyTransition.TransitionDuration;
                    transitionOffset = anyTransition.Offset;

                    // Curve lookup: Any State transitions use AnyStateTransitions array
                    curveSourceStateIndex = -1;  // Not applicable for Any State
                    curveSourceTransitionIndex = anyTransitionIndex;
                    curveSource = TransitionSource.AnyState;
                }
                else if (transitionSource == TransitionSource.Exit)
                {
                    // Exit transition
                    var exitTransitionIndex = DecodeExitTransition(transitionIndex);
                    var exitGroupIndex = stateMachine.CurrentStateBlob.ExitTransitionGroupIndex;
                    ref var exitGroup = ref stateMachineBlob.ExitTransitionGroups[exitGroupIndex];
                    ref var exitTransition = ref exitGroup.ExitTransitions[exitTransitionIndex];
                    toStateIndex = exitTransition.ToStateIndex;
                    transitionDuration = exitTransition.TransitionDuration;
                    transitionOffset = exitTransition.Offset;
                    
                    // Exit transitions have no curve - parent state machine handles the blend
                    curveSourceStateIndex = -1;
                    curveSourceTransitionIndex = -1;
                    curveSource = TransitionSource.Exit;
                }
                else
                {
                    // Regular transition (positive index)
                    ref var transition = ref stateMachine.CurrentStateBlob.Transitions[transitionIndex];
                    toStateIndex = transition.ToStateIndex;
                    transitionDuration = transition.TransitionDuration;
                    transitionOffset = transition.Offset;
                    
                    // Curve lookup: State transitions use States[fromState].Transitions array
                    curveSourceStateIndex = (short)stateMachine.CurrentState.StateIndex;
                    curveSourceTransitionIndex = transitionIndex;
                    curveSource = TransitionSource.State;
                }

                // All states are leaf states (Single or LinearBlend) - no hierarchy navigation needed
#if UNITY_EDITOR || DEBUG
                stateMachine.PreviousState = stateMachine.CurrentState;
#endif
                stateMachine.CurrentState = CreateState(
                    toStateIndex,
                    stateMachine,
                    ref buffers,
                    floatParameters,
                    intParameters,
                    transitionOffset);

                animationStateTransitionRequest = AnimationStateTransitionRequest.New(
                    (byte)stateMachine.CurrentState.AnimationStateId,
                    transitionDuration,
                    curveSourceStateIndex,
                    curveSourceTransitionIndex,
                    curveSource);
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
            in DynamicBuffer<FloatParameter> floatParameters,
            in DynamicBuffer<IntParameter> intParameters = default,
            float normalizedOffset = 0f)
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
                        finalSpeed,
                        normalizedOffset);
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
                        finalSpeed,
                        floatParameters,
                        intParameters,
                        normalizedOffset);
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
                        finalSpeed,
                        floatParameters,
                        normalizedOffset);
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
                    // Encode as Any State transition
                    transitionIndex = EncodeAnyStateTransition(i);
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
        /// Returns encoded transition index to distinguish from regular and any-state transitions.
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
                    // Encode as exit transition
                    transitionIndex = EncodeExitTransition(i);
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
