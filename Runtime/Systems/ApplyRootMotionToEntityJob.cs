using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Unity.Burst;
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
    [WithAll(typeof(SkeletonRootTag), typeof(ApplyRootMotionToEntity))]
    internal partial struct ApplyRootMotionToEntityJob : IJobEntity
    {
        internal ProfilerMarker Marker;
        internal void Execute(
#if LATIOS_TRANSFORMS_UNITY
            ref LocalTransform localTransform,
#else
            TransformAspect transformAspect,
#endif
            in RootDeltaTranslation rootDeltaTranslation,
            in RootDeltaRotation rootDeltaRotation
        )
        {
            using var scope = Marker.Auto();
#if LATIOS_TRANSFORMS_UNITY
            localTransform.Position += rootDeltaTranslation.Value;
            localTransform.Rotation = math.mul(rootDeltaRotation.Value, localTransform.Rotation);
#else
            transformAspect.worldPosition += rootDeltaTranslation.Value;
            transformAspect.worldRotation = math.mul(rootDeltaRotation.Value, transformAspect.worldRotation);
#endif
        }
    }
}