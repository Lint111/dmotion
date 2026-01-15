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
            ref DynamicBuffer<StateMachineContext> stackContext,
            in AnimationCurrentState animationCurrentState,
            in AnimationStateTransition animationStateTransition,
            in DynamicBuffer<BoolParameter> boolParameters,
            in DynamicBuffer<IntParameter> intParameters,
            in DynamicBuffer<FloatParameter> floatParameters
        )
        {
            using var scope = Marker.Auto();
            ref var rootBlob = ref stateMachine.StateMachineBlob.Value;

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

            // Get the current blob based on hierarchy depth
            ref var stateMachineBlob = ref GetCurrentBlob(ref rootBlob, stackContext);

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

                // Update the stack context with the initial state
                if (stackContext.Length > 0)
                {
                    ref var currentContext = ref stackContext.GetCurrent();
                    currentContext.CurrentStateIndex = stateMachineBlob.DefaultStateIndex;
                }
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

            if (shouldStartTransition)
            {
                // Get transition (from Any State if negative index, from regular if positive)
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
                else
                {
                    // Regular transition (positive index)
                    ref var transition = ref stateMachine.CurrentStateBlob.Transitions[transitionIndex];
                    toStateIndex = transition.ToStateIndex;
                    transitionDuration = transition.TransitionDuration;
                }

                // Check if destination is a SubStateMachine
                ref var destinationState = ref stateMachineBlob.States[toStateIndex];

                if (destinationState.Type == StateType.SubStateMachine)
                {
                    // Enter the sub-state machine
                    var subMachineIndex = destinationState.StateIndex;
                    EnterSubStateMachine(ref stackContext, (short)subMachineIndex, ref stateMachineBlob);

                    // Get the nested blob and create state for its entry state
                    ref var nestedBlob = ref stateMachineBlob.SubStateMachines[subMachineIndex].NestedStateMachine;
                    var entryStateIndex = stateMachineBlob.SubStateMachines[subMachineIndex].EntryStateIndex;

#if UNITY_EDITOR || DEBUG
                    stateMachine.PreviousState = stateMachine.CurrentState;
#endif
                    stateMachine.CurrentState = CreateState(
                        entryStateIndex,
                        stateMachine,
                        ref buffers,
                        floatParameters);

                    animationStateTransitionRequest = new AnimationStateTransitionRequest
                    {
                        AnimationStateId = stateMachine.CurrentState.AnimationStateId,
                        TransitionDuration = transitionDuration,
                    };
                }
                else
                {
                    // Regular state transition (Single or LinearBlend)
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

                    // Update the stack context with the new state
                    if (stackContext.Length > 0)
                    {
                        ref var currentContext = ref stackContext.GetCurrent();
                        currentContext.CurrentStateIndex = toStateIndex;
                    }
                }
            }
        }

        /// <summary>
        /// Traverse the hierarchy to get the StateMachineBlob at the current depth.
        /// Philosophy: Follow the relationship chain - each context points to its parent sub-machine.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref StateMachineBlob GetCurrentBlob(
            ref StateMachineBlob rootBlob,
            in DynamicBuffer<StateMachineContext> stackContext)
        {
            if (stackContext.Length == 0)
            {
                return ref rootBlob;
            }

            ref var currentBlob = ref rootBlob;

            // Traverse down the hierarchy following the context chain
            for (int i = 1; i < stackContext.Length; i++)
            {
                var context = stackContext[i];
                var subMachineIndex = context.ParentSubMachineIndex;

                if (subMachineIndex >= 0)
                {
                    ref var subMachine = ref currentBlob.SubStateMachines[subMachineIndex];
                    currentBlob = ref subMachine.NestedStateMachine;
                }
            }

            return ref currentBlob;
        }

        /// <summary>
        /// Enter a sub-state machine by pushing a new context onto the stack.
        /// </summary>
        private void EnterSubStateMachine(
            ref DynamicBuffer<StateMachineContext> stackContext,
            short subMachineIndex,
            ref StateMachineBlob parentBlob)
        {
            ref var subMachine = ref parentBlob.SubStateMachines[subMachineIndex];

            var newContext = new StateMachineContext
            {
                CurrentStateIndex = subMachine.EntryStateIndex,
                ParentSubMachineIndex = subMachineIndex,
                Level = (byte)(stackContext.Length)
            };

            stackContext.Push(newContext);
        }

        /// <summary>
        /// Exit the current sub-state machine by popping the context.
        /// Returns true if exit transitions should be evaluated.
        /// </summary>
        private bool ExitSubStateMachine(
            ref DynamicBuffer<StateMachineContext> stackContext,
            out short exitedSubMachineIndex)
        {
            if (stackContext.Length <= 1)
            {
                // Can't exit from root level
                exitedSubMachineIndex = -1;
                return false;
            }

            var exitedContext = stackContext.Pop();
            exitedSubMachineIndex = exitedContext.ParentSubMachineIndex;
            return true;
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
                case StateType.SubStateMachine:
                    throw new InvalidOperationException(
                        "SubStateMachine states should be entered via EnterSubStateMachine, not created directly. " +
                        "This indicates a bug in transition handling.");
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
            out short transitionIndex)
        {
            for (short i = 0; i < stateMachine.AnyStateTransitions.Length; i++)
            {
                if (EvaluateAnyStateTransition(animation, ref stateMachine.AnyStateTransitions[i], parameters))
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
        /// Evaluates a single Any State transition (same logic as regular transitions).
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EvaluateAnyStateTransition(
            in AnimationState animation,
            ref AnyStateTransition transition,
            in TransitionParameters parameters)
        {
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
