using Unity.Burst;
using Unity.Entities;

namespace DMotion
{
    [BurstCompile]
    internal struct Directional2DBlendStateMachineState : IBufferElementData
    {
        internal byte AnimationStateId;
        internal BlobAssetReference<StateMachineBlob> StateMachineBlob;
        internal short StateIndex;
        
        internal readonly ref AnimationStateBlob StateBlob => ref StateMachineBlob.Value.States[StateIndex];
        internal readonly ref Directional2DBlendStateBlob Directional2DBlob =>
            ref StateMachineBlob.Value.Directional2DBlendStates[StateBlob.StateIndex];
    }
}
