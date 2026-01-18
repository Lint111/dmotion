using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    public struct Directional2DBlendStateMachineState : IBufferElementData
    {
        public byte AnimationStateId;
        public int StartSampleIndex;
        public int SampleCount;
        public ushort BlendParameterIndexX;
        public ushort BlendParameterIndexY;
        public ushort BlobIndex; // Index into Directional2DBlendStates blob array
    }
}
