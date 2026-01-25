using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Bundles animation-related buffers to reduce parameter coupling in state machine operations.
    /// DynamicBuffer is a value type that wraps a pointer, so passing by value is efficient.
    /// 
    /// Used by UpdateStateMachineJob and state utility methods to reduce the number of
    /// individual buffer parameters that need to be passed around.
    /// </summary>
    internal struct AnimationBufferContext
    {
        public DynamicBuffer<AnimationState> AnimationStates;
        public DynamicBuffer<ClipSampler> ClipSamplers;
        public DynamicBuffer<SingleClipState> SingleClipStates;
        public DynamicBuffer<LinearBlendStateMachineState> LinearBlendStates;
        public DynamicBuffer<Directional2DBlendStateMachineState> Directional2DBlendStates;
    }

    /// <summary>
    /// Read-only parameter buffers for transition evaluation.
    /// </summary>
    internal readonly struct TransitionParameters
    {
        public readonly DynamicBuffer<BoolParameter> BoolParameters;
        public readonly DynamicBuffer<IntParameter> IntParameters;

        public TransitionParameters(
            DynamicBuffer<BoolParameter> boolParameters,
            DynamicBuffer<IntParameter> intParameters)
        {
            BoolParameters = boolParameters;
            IntParameters = intParameters;
        }
    }
}
