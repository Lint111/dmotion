using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Transforms;

namespace DMotion
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(BlendAnimationStatesSystem))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    internal partial struct UpdateAnimationStatesSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
#if DEBUG || UNITY_EDITOR
            if (JobsUtility.JobDebuggerEnabled)
            {
                OnUpdate_Safe(ref state);
            }
            else
            {
                OnUpdate_Unsafe(ref state);
            }
#else
            OnUpdate_Unsafe(ref state);
#endif
        }

        /// <summary>
        /// Schedules jobs sequentially because both SingleClip and LinearBlend jobs
        /// write to ClipSampler buffer. Even though they query different entity archetypes,
        /// Unity's safety system requires proper dependency chaining for shared buffer types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnUpdate_Safe(ref SystemState state)
        {
            // SingleClip jobs first
            var handle = new UpdateSingleClipStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            handle = new CleanSingleClipStatesJob().ScheduleParallel(handle);

            // LinearBlend jobs must chain after SingleClip (both write ClipSampler)
            handle = new UpdateLinearBlendStateMachineStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(handle);

            handle = new CleanLinearBlendStatesJob().ScheduleParallel(handle);

            // Directional2DBlend jobs
            handle = new UpdateDirectional2DBlendStateMachineStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(handle);

            state.Dependency = new CleanDirectional2DBlendStatesJob().ScheduleParallel(handle);
        }

        /// <summary>
        /// Schedules jobs sequentially because both SingleClip and LinearBlend jobs
        /// write to ClipSampler buffer. Even though they query different entity archetypes,
        /// Unity's safety system requires proper dependency chaining for shared buffer types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnUpdate_Unsafe(ref SystemState state)
        {
            // SingleClip jobs first
            var handle = new UpdateSingleClipStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            handle = new CleanSingleClipStatesJob().ScheduleParallel(handle);

            // LinearBlend jobs must chain after SingleClip (both write ClipSampler)
            handle = new UpdateLinearBlendStateMachineStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(handle);

            handle = new CleanLinearBlendStatesJob().ScheduleParallel(handle);
            
            // Directional2DBlend jobs
            handle = new UpdateDirectional2DBlendStateMachineStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(handle);

            state.Dependency = new CleanDirectional2DBlendStatesJob().ScheduleParallel(handle);
        }
    }
}