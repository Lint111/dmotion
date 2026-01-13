using Latios.Kinemation;
using Latios.Transforms;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    [BurstCompile]
    internal static class ClipSamplingUtils
    {
        public static TransformQvvs SampleWeightedFirstIndex(int boneIndex, ref SkeletonClip clip, float time, float weight)
        {
            var bone = clip.SampleBone(boneIndex, time);
            bone.position *= weight;
            var rot = bone.rotation;
            rot.value *= weight;
            bone.rotation = rot;
            bone.scale *= weight;
            return bone;
        }

        public static void SampleWeightedNIndex(ref TransformQvvs bone, int boneIndex, ref SkeletonClip clip, float time, float weight)
        {
            var otherBone = clip.SampleBone(boneIndex, time);
            bone.position += otherBone.position * weight;

            //blends rotation. Negates opposing quaternions to be sure to choose the shortest path
            var otherRot = otherBone.rotation;
            var dot = math.dot(otherRot, bone.rotation);
            if (dot < 0)
            {
                otherRot.value = -otherRot.value;
            }

            var rot = bone.rotation;
            rot.value += otherRot.value * weight;
            bone.rotation = rot;

            bone.scale += otherBone.scale * weight;
        }
    }
}
