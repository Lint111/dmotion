using Latios.Kinemation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace DMotion
{
    [BurstCompile]
    internal partial struct SampleOptimizedBonesJob : IJobEntity
    {
        internal ProfilerMarker Marker;

        internal void Execute(
            OptimizedSkeletonAspect skeleton,
            in DynamicBuffer<ClipSampler> samplers)
        {
            using var scope = Marker.Auto();

            var activeSamplerCount = 0;

            for (byte i = 0; i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                if (!mathex.iszero(sampler.Weight) && sampler.Clips.IsCreated)
                {
                    activeSamplerCount++;
                    sampler.Clip.SamplePose(ref skeleton, sampler.Time, sampler.Weight);
                }
            }

            if (activeSamplerCount > 0)
            {
                skeleton.EndSamplingAndSync();
            }
        }
    }
}