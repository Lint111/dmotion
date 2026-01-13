using Latios.Kinemation;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace DMotion
{
    // Temporarily disabled for leak debugging
    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ClipSamplingSystem))]
    public partial struct AnimationDebugSystem : ISystem
    {
        private int _frameCount;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ClipSampler>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _frameCount++;

            // Only log every 60 frames to avoid spam
            if (_frameCount % 60 != 0) return;

            // Check entities with samplers
            int samplerEntities = 0;
            foreach (var (samplers, states, entity) in
                SystemAPI.Query<DynamicBuffer<ClipSampler>, DynamicBuffer<AnimationState>>()
                    .WithEntityAccess())
            {
                samplerEntities++;
                for (int i = 0; i < samplers.Length; i++)
                {
                    var s = samplers[i];
                }
            }

            // Check entities with OptimizedSkeletonAspect + ClipSampler (what SampleOptimizedBonesJob needs)
            int skeletonEntities = 0;
            foreach (var (skeleton, samplers, entity) in
                SystemAPI.Query<OptimizedSkeletonAspect, DynamicBuffer<ClipSampler>>()
                    .WithEntityAccess())
            {
                skeletonEntities++;
                UnityEngine.Debug.Log($"[AnimDebug] Skeleton Entity {entity.Index}: " +
                    $"BoneCount={skeleton.boneCount}, Samplers={samplers.Length}");
            }

            UnityEngine.Debug.Log($"[AnimDebug] SamplerEntities={samplerEntities}, SkeletonEntities={skeletonEntities}");
        }
    }
}
