using Latios.Kinemation;
using Unity.Entities;
using Unity.Mathematics;
#if LATIOS_TRANSFORMS_UNITY
using Unity.Transforms;
#else
using Latios.Transforms;
#endif

namespace DMotion.Samples
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    //must update after ClipSampling in order to have RootMotion deltas
    [UpdateAfter(typeof(ClipSamplingSystem))]
    [RequireMatchingQueriesForUpdate]
    public partial struct CustomRootMotionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
#if LATIOS_TRANSFORMS_UNITY
            foreach (var (localTransform, rootDeltaTranslation, rootDeltaRotation) in SystemAPI
                         .Query<RefRW<LocalTransform>, RootDeltaTranslation, RootDeltaRotation>()
                         .WithAll<CustomRootMotionComponent>()
                         .WithAll<SkeletonRootTag>())
            {
                //RootDeltaTranslation and RootDeltaRotation are calculated by DMotion in the ClipSamplingSystem, so you can just read them here
                var deltaTranslation = -rootDeltaTranslation.Value;
                localTransform.ValueRW.Position += deltaTranslation;
                localTransform.ValueRW.Rotation = math.mul(rootDeltaRotation.Value, localTransform.ValueRW.Rotation);
            }
#else
            foreach (var (transformAspect, rootDeltaTranslation, rootDeltaRotation) in SystemAPI
                         .Query<TransformAspect, RootDeltaTranslation, RootDeltaRotation>()
                         .WithAll<CustomRootMotionComponent>()
                         .WithAll<SkeletonRootTag>())
            {
                //RootDeltaTranslation and RootDeltaRotation are calculated by DMotion in the ClipSamplingSystem, so you can just read them here
                var deltaTranslation = -rootDeltaTranslation.Value;
                transformAspect.worldPosition += deltaTranslation;
                transformAspect.worldRotation = math.mul(rootDeltaRotation.Value, transformAspect.worldRotation);
            }
#endif
        }
    }
}