using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace DMotion
{
    [BurstCompile]
    [WithNone(typeof(TimelineControlled))]
    internal partial struct UpdateDirectional2DBlendStateMachineStatesJob : IJobEntity
    {
        internal float DeltaTime;

        internal void Execute(
            ref DynamicBuffer<ClipSampler> clipSamplers,
            in DynamicBuffer<AnimationState> animationStates,
            in DynamicBuffer<Directional2DBlendStateMachineState> directional2DStates,
            in DynamicBuffer<FloatParameter> floatParameters
        )
        {
            // Pre-allocate weights array for reuse (max clips per blend tree)
            var weights = new NativeArray<float>(64, Allocator.Temp);

            for (var i = 0; i < directional2DStates.Length; i++)
            {
                var state = directional2DStates[i];
                if (animationStates.TryGetWithId(state.AnimationStateId, out var animationState))
                {
                    Directional2DBlendStateUtils.ExtractVariables(
                        state,
                        floatParameters,
                        out var input,
                        out var positions,
                        out var speeds);

                    // Resize weights if needed
                    if (weights.Length < positions.Length)
                    {
                        weights.Dispose();
                        weights = new NativeArray<float>(positions.Length, Allocator.Temp);
                    }
                    
                    var activeWeights = weights.GetSubArray(0, positions.Length);
                    var algorithm = state.Directional2DBlob.Algorithm;
                    Directional2DBlendUtils.CalculateWeights(input, positions, activeWeights, algorithm);

                    Directional2DBlendStateUtils.UpdateSamplers(
                        DeltaTime,
                        positions,
                        speeds,
                        activeWeights,
                        animationState,
                        ref clipSamplers);
                }
            }
            
            weights.Dispose();
        }
    }

    [BurstCompile]
    [WithNone(typeof(TimelineControlled))]
    internal partial struct CleanDirectional2DBlendStatesJob : IJobEntity
    {
        internal void Execute(
            ref DynamicBuffer<Directional2DBlendStateMachineState> directional2DStates,
            in DynamicBuffer<AnimationState> animationStates
        )
        {
            for (int i = directional2DStates.Length - 1; i >= 0; i--)
            {
                if (!animationStates.TryGetWithId(directional2DStates[i].AnimationStateId, out _))
                {
                    directional2DStates.RemoveAtSwapBack(i);
                }
            }
        }
    }
}
