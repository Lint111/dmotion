using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
#if LATIOS_TRANSFORMS_UNITY
using Unity.Transforms;
#else
using Latios.Transforms;
#endif

namespace DMotion
{
    [BurstCompile]
    [WithOptions(EntityQueryOptions.FilterWriteGroup)]
    [WithAll(typeof(TransferRootMotionToOwner))]
    internal partial struct TransferRootMotionJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<RootDeltaTranslation> CfeDeltaPosition;
        [ReadOnly] public ComponentLookup<RootDeltaRotation> CfeDeltaRotation;
        internal ProfilerMarker Marker;

#if LATIOS_TRANSFORMS_UNITY
        public void Execute(ref LocalTransform localTransform, in AnimatorOwner owner)
        {
            using var scope = Marker.Auto();
            var deltaPos = CfeDeltaPosition[owner.AnimatorEntity];
            var deltaRot = CfeDeltaRotation[owner.AnimatorEntity];
            localTransform.Rotation = math.mul(deltaRot.Value, localTransform.Rotation);
            localTransform.Position += deltaPos.Value;
        }
#else
        public void Execute(TransformAspect transformAspect, in AnimatorOwner owner)
        {
            using var scope = Marker.Auto();
            var deltaPos = CfeDeltaPosition[owner.AnimatorEntity];
            var deltaRot = CfeDeltaRotation[owner.AnimatorEntity];
            transformAspect.worldRotation = math.mul(deltaRot.Value, transformAspect.worldRotation);
            transformAspect.worldPosition += deltaPos.Value;
        }
#endif
    }
}