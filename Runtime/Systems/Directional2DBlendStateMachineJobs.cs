using Unity.Burst;
using Unity.Entities;

namespace DMotion
{
    [BurstCompile]
    internal partial struct UpdateDirectional2DBlendStateMachineStatesJob : IJobEntity
    {
        internal float DeltaTime;

        internal void Execute(
            ref DynamicBuffer<ClipSampler> clipSamplers,
            in DynamicBuffer<AnimationState> animationStates,
            in DynamicBuffer<Directional2DBlendStateMachineState> directional2DStates,
            in DynamicBuffer<FloatParameter> floatParameters,
            in AnimationStateMachine stateMachine
        )
        {
            ref var stateMachineBlob = ref stateMachine.StateMachineBlob.Value;
            var weights = new NativeArray<float>(100, Allocator.Temp); // Max 100 clips per blend tree for now

            for (var i = 0; i < directional2DStates.Length; i++)
            {
                var state = directional2DStates[i];
                if (animationStates.TryGetWithId(state.AnimationStateId, out var animationState))
                {
                    Directional2DBlendStateUtils.ExtractVariables(
                        state,
                        ref stateMachineBlob,
                        floatParameters,
                        out var input,
                        out var positions,
                        out var speeds);

                    if (weights.Length < positions.Length)
                    {
                        weights.Dispose();
                        weights = new NativeArray<float>(positions.Length, Allocator.Temp);
                    }
                    
                    var activeWeights = weights.GetSubArray(0, positions.Length);
                    Directional2DBlendUtils.CalculateWeights(input, positions, activeWeights);

                    Directional2DBlendStateUtils.UpdateSamplers(
                        DeltaTime,
                        input,
                        positions,
                        speeds,
                        activeWeights,
                        animationState,
                        ref clipSamplers);
                }
            }
        }
    }

    [BurstCompile]
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
