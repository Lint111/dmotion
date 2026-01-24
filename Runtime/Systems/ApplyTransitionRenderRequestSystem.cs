using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Applies AnimationTransitionRenderRequest to animation state buffers.
    /// 
    /// Handles transition preview by:
    /// 1. Setting up FROM state with weight = 1 - blendWeight
    /// 2. Setting up TO state with weight = blendWeight
    /// 3. Calculating per-clip weights for blend states based on BlendPosition
    /// 
    /// The blend weight can be driven by:
    /// - Linear interpolation (default)
    /// - Custom blend curve (if TransitionIndex is valid)
    /// </summary>
    /// <summary>
    /// Runs AFTER normal animation systems but BEFORE clip sampling to ensure our preview values are used.
    /// Order: AnimationStateMachine -> BlendAnimationStates -> UpdateAnimationStates -> [Apply systems] -> ClipSampling
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(UpdateAnimationStatesSystem))]
    [UpdateBefore(typeof(ClipSamplingSystem))]
    public partial struct ApplyTransitionRenderRequestSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActiveRenderRequest>();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new ApplyTransitionRenderRequestJob().ScheduleParallel();
        }
    }
    
    [BurstCompile]
    internal partial struct ApplyTransitionRenderRequestJob : IJobEntity
    {
        public void Execute(
            in ActiveRenderRequest activeRequest,
            in AnimationTransitionRenderRequest transitionRequest,
            in AnimationStateMachine stateMachine,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers)
        {
            // Only process if transition request is the active type
            if (activeRequest.Type != RenderRequestType.Transition)
                return;
            
            if (!transitionRequest.IsValid)
                return;
            
            if (!stateMachine.StateMachineBlob.IsCreated)
                return;
            
            ApplyRequest(
                ref stateMachine.StateMachineBlob.Value,
                ref animationStates,
                ref samplers,
                transitionRequest);
        }
        
        private static void ApplyRequest(
            ref StateMachineBlob smBlob,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            in AnimationTransitionRenderRequest request)
        {
            if (animationStates.Length < 2)
            {
                // Need at least 2 states for transition
                if (animationStates.Length == 1)
                {
                    ApplySingleStateFallback(ref smBlob, ref animationStates, ref samplers, request);
                }
                return;
            }
            
            // Calculate weights
            float toWeight = math.saturate(request.BlendWeight);
            float fromWeight = 1f - toWeight;
            
            #if UNITY_EDITOR
            // Diagnostic: trace blend position through the system
            UnityEngine.Debug.Log($"[ApplyTransitionRenderRequest] FromBlendPos={request.FromBlendPosition}, ToBlendPos={request.ToBlendPosition}, BlendWeight={request.BlendWeight:F2}");
            #endif
            
            // Apply FROM state (index 0)
            if (request.FromStateIndex < smBlob.States.Length)
            {
                ApplyStateAtIndex(
                    ref smBlob,
                    ref animationStates,
                    ref samplers,
                    0,
                    request.FromStateIndex,
                    fromWeight,
                    request.FromNormalizedTime,
                    request.FromBlendPosition);
            }
            
            // Apply TO state (index 1)
            if (request.ToStateIndex < smBlob.States.Length)
            {
                ApplyStateAtIndex(
                    ref smBlob,
                    ref animationStates,
                    ref samplers,
                    1,
                    request.ToStateIndex,
                    toWeight,
                    request.ToNormalizedTime,
                    request.ToBlendPosition);
            }
            
            // Zero out any remaining states
            for (int i = 2; i < animationStates.Length; i++)
            {
                var otherState = animationStates[i];
                otherState.Weight = 0f;
                animationStates[i] = otherState;
            }
        }
        
        private static void ApplyStateAtIndex(
            ref StateMachineBlob smBlob,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            int stateBufferIndex,
            ushort stateIndex,
            float stateWeight,
            float normalizedTime,
            float2 blendPosition)
        {
            var animState = animationStates[stateBufferIndex];
            
            int samplerStart = samplers.IdToIndex(animState.StartSamplerId);
            if (samplerStart < 0)
                return;
            
            float stateDuration = GetStateDuration(in samplers, samplerStart, animState.ClipCount);
            
            // Update animation state
            animState.Weight = stateWeight;
            animState.Time = normalizedTime * stateDuration;
            animationStates[stateBufferIndex] = animState;
            
            ref var stateBlob = ref smBlob.States[stateIndex];
            
            // Apply based on state type
            switch (stateBlob.Type)
            {
                case StateType.Single:
                    ApplySingleClipState(ref samplers, samplerStart, animState.ClipCount, normalizedTime, stateWeight);
                    break;
                    
                case StateType.LinearBlend:
                    ApplyLinearBlendState(
                        ref smBlob,
                        ref samplers,
                        samplerStart,
                        animState.ClipCount,
                        stateIndex,
                        blendPosition.x,
                        normalizedTime,
                        stateWeight);
                    break;
                    
                case StateType.Directional2DBlend:
                    ApplyDirectional2DBlendState(
                        ref smBlob,
                        ref samplers,
                        samplerStart,
                        animState.ClipCount,
                        stateIndex,
                        blendPosition,
                        normalizedTime,
                        stateWeight);
                    break;
            }
        }
        
        private static void ApplySingleStateFallback(
            ref StateMachineBlob smBlob,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            in AnimationTransitionRenderRequest request)
        {
            // Use TO state if blend > 0.5, else FROM
            bool useToState = request.BlendWeight >= 0.5f;
            ushort stateIndex = useToState ? request.ToStateIndex : request.FromStateIndex;
            float normalizedTime = useToState ? request.ToNormalizedTime : request.FromNormalizedTime;
            float2 blendPos = useToState ? request.ToBlendPosition : request.FromBlendPosition;
            
            if (stateIndex >= smBlob.States.Length)
                return;
            
            ApplyStateAtIndex(
                ref smBlob,
                ref animationStates,
                ref samplers,
                0,
                stateIndex,
                1f,
                normalizedTime,
                blendPos);
        }
        
        private static void ApplySingleClipState(
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex,
            int count,
            float normalizedTime,
            float stateWeight)
        {
            // Use shared utility for weight
            SingleClipStateUtils.SetSamplerWeight(stateWeight, ref samplers, startIndex);
            // Use shared utility for time
            SingleClipStateUtils.SetSamplerTime(normalizedTime, ref samplers, startIndex);
        }
        
        private static void ApplyLinearBlendState(
            ref StateMachineBlob smBlob,
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex,
            int count,
            ushort stateIndex,
            float blendRatio,
            float normalizedTime,
            float stateWeight)
        {
            ref var linearBlob = ref smBlob.LinearBlendStates[smBlob.States[stateIndex].StateIndex];
            ref var thresholds = ref linearBlob.SortedClipThresholds;
            
            int clipCount = thresholds.Length;
            if (clipCount == 0 || count == 0)
                return;
            
            #if UNITY_EDITOR
            // Diagnostic: trace blend ratio being applied to linear blend
            UnityEngine.Debug.Log($"[ApplyLinearBlendState] StateIndex={stateIndex}, BlendRatio={blendRatio:F2}, StateWeight={stateWeight:F2}, ClipCount={clipCount}, StartIdx={startIndex}");
            #endif
            
            // Use shared utility for weights
            var thresholdsArray = CollectionUtils.AsArray(ref thresholds);
            LinearBlendStateUtils.SetSamplerWeights(blendRatio, thresholdsArray, stateWeight, ref samplers, startIndex);
            
            // Use shared utility for time
            LinearBlendStateUtils.SetSamplerTimes(normalizedTime, ref samplers, startIndex, clipCount);
        }
        
        private static void ApplyDirectional2DBlendState(
            ref StateMachineBlob smBlob,
            ref DynamicBuffer<ClipSampler> samplers,
            int startIndex,
            int count,
            ushort stateIndex,
            float2 blendPosition,
            float normalizedTime,
            float stateWeight)
        {
            ref var blend2DBlob = ref smBlob.Directional2DBlendStates[smBlob.States[stateIndex].StateIndex];
            ref var positions = ref blend2DBlob.ClipPositions;
            
            int clipCount = positions.Length;
            if (clipCount == 0 || count == 0)
                return;
            
            // Use shared utility for weights
            Directional2DBlendStateUtils.SetSamplerWeightsFromPosition(
                blendPosition, ref positions, stateWeight, ref samplers, startIndex, clipCount);
            
            // Use shared utility for time
            Directional2DBlendStateUtils.SetSamplerTimes(normalizedTime, ref samplers, startIndex, clipCount);
        }
        
        private static float GetStateDuration(in DynamicBuffer<ClipSampler> samplers, int startIndex, int count)
        {
            if (startIndex < 0 || startIndex >= samplers.Length || count == 0)
                return 1f;
            
            var sampler = samplers[startIndex];
            return sampler.Duration > 0 ? sampler.Duration : 1f;
        }
    }
}
