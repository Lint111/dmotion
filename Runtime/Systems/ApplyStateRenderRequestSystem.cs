using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Applies AnimationStateRenderRequest to animation state buffers.
    /// 
    /// Handles:
    /// - Single clip state preview
    /// - Linear blend state preview (with blend position)
    /// - Directional 2D blend state preview (with blend position)
    /// - Ghost FROM/TO bars for transition context
    /// 
    /// For blend states, calculates per-clip weights based on BlendPosition.
    /// </summary>
    /// <summary>
    /// Runs AFTER normal animation systems but BEFORE clip sampling to ensure our preview values are used.
    /// Order: AnimationStateMachine -> BlendAnimationStates -> UpdateAnimationStates -> [Apply systems] -> ClipSampling
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(UpdateAnimationStatesSystem))]
    [UpdateBefore(typeof(ClipSamplingSystem))]
    public partial struct ApplyStateRenderRequestSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActiveRenderRequest>();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new ApplyStateRenderRequestJob().ScheduleParallel();
        }
    }
    
    [BurstCompile]
    internal partial struct ApplyStateRenderRequestJob : IJobEntity
    {
        public void Execute(
            in ActiveRenderRequest activeRequest,
            in AnimationStateRenderRequest stateRequest,
            in AnimationStateMachine stateMachine,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers)
        {
            // Only process if state request is the active type
            if (activeRequest.Type != RenderRequestType.State)
                return;
            
            if (!stateRequest.IsValid)
                return;
            
            if (!stateMachine.StateMachineBlob.IsCreated)
                return;
            
            ApplyRequest(
                ref stateMachine.StateMachineBlob.Value,
                ref animationStates,
                ref samplers,
                stateRequest);
        }
        
        private static void ApplyRequest(
            ref StateMachineBlob smBlob,
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            in AnimationStateRenderRequest request)
        {
            if (animationStates.Length == 0)
                return;
            
            // Validate state index
            if (request.StateIndex >= smBlob.States.Length)
                return;
            
            ref var stateBlob = ref smBlob.States[request.StateIndex];
            
            // Use first animation state slot
            int stateBufferIndex = 0;
            var animState = animationStates[stateBufferIndex];
            
            int samplerStart = samplers.IdToIndex(animState.StartSamplerId);
            if (samplerStart < 0)
                return;
            
            // Calculate state duration and update based on state type
            float stateDuration = GetStateDuration(ref samplers, samplerStart, animState.ClipCount);
            
            // Update animation state time
            animState.Weight = 1f;
            animState.Time = request.NormalizedTime * stateDuration;
            animationStates[stateBufferIndex] = animState;
            
            // Zero out all other states
            for (int i = 1; i < animationStates.Length; i++)
            {
                var otherState = animationStates[i];
                otherState.Weight = 0f;
                animationStates[i] = otherState;
            }
            
            // Apply based on state type
            switch (stateBlob.Type)
            {
                case StateType.Single:
                    ApplySingleClipState(ref samplers, samplerStart, animState.ClipCount, request.NormalizedTime, animState.Weight);
                    break;
                    
                case StateType.LinearBlend:
                    ApplyLinearBlendState(
                        ref smBlob,
                        ref samplers,
                        samplerStart,
                        animState.ClipCount,
                        request.StateIndex,
                        request.BlendPosition.x,
                        request.NormalizedTime,
                        animState.Weight);
                    break;
                    
                case StateType.Directional2DBlend:
                    ApplyDirectional2DBlendState(
                        ref smBlob,
                        ref samplers,
                        samplerStart,
                        animState.ClipCount,
                        request.StateIndex,
                        request.BlendPosition,
                        request.NormalizedTime,
                        animState.Weight);
                    break;
            }
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
            
            // Use shared utility for weights (converts BlobArray to NativeArray temporarily)
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
        
        private static float GetStateDuration(ref DynamicBuffer<ClipSampler> samplers, int startIndex, int count)
        {
            if (startIndex < 0 || startIndex >= samplers.Length || count == 0)
                return 1f;
            
            var sampler = samplers[startIndex];
            return sampler.Duration > 0 ? sampler.Duration : 1f;
        }
    }
}
