using Latios.Kinemation;
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
using TransformQvvs = Latios.Transforms.TransformQvvs;

namespace DMotion
{
    [BurstCompile]
    [WithNone(typeof(SkeletonRootTag))]
    internal partial struct SampleNonOptimizedBones : IJobEntity
    {
        [ReadOnly] internal BufferLookup<ClipSampler> BfeClipSampler;
        internal ProfilerMarker Marker;

        internal void Execute(
#if LATIOS_TRANSFORMS_UNITY
            ref LocalTransform localTransform,
#else
            TransformAspect transformAspect,
#endif
            in BoneOwningSkeletonReference skeletonRef,
            in BoneIndex boneIndex
        )
        {
            using var scope = Marker.Auto();
            var samplers = BfeClipSampler[skeletonRef.skeletonRoot];

            if (samplers.Length > 0 && TryFindFirstActiveSamplerIndex(samplers, out var firstSamplerIndex))
            {
                var firstSampler = samplers[firstSamplerIndex];
                var bone = ClipSamplingUtils.SampleWeightedFirstIndex(
                    boneIndex.index, ref firstSampler.Clip,
                    firstSampler.Time,
                    firstSampler.Weight);

                for (var i = firstSamplerIndex + 1; i < samplers.Length; i++)
                {
                    var sampler = samplers[i];
                    if (!mathex.iszero(sampler.Weight))
                    {
                        ClipSamplingUtils.SampleWeightedNIndex(
                            ref bone, boneIndex.index, ref sampler.Clip,
                            sampler.Time, sampler.Weight);
                    }
                }

                if (samplers.Length - firstSamplerIndex > 1)
                {
                    bone.rotation = math.normalize(bone.rotation);
                }

#if LATIOS_TRANSFORMS_UNITY
                localTransform.Position = bone.position;
                localTransform.Rotation = bone.rotation;
                localTransform.Scale = bone.scale; // TransformQvvs.scale is already uniform (float)
#else
                transformAspect.localPosition = bone.position;
                transformAspect.localRotation = bone.rotation;
                transformAspect.localScale = bone.scale;
#endif
            }
        }

        private bool TryFindFirstActiveSamplerIndex(in DynamicBuffer<ClipSampler> samplers, out byte samplerIndex)
        {
            for (byte i = 0; i < samplers.Length; i++)
            {
                if (!mathex.iszero(samplers[i].Weight))
                {
                    samplerIndex = i;
                    return true;
                }
            }

            samplerIndex = 0;
            return false;
        }
    }
}