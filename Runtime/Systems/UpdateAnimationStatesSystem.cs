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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnUpdate_Safe(ref SystemState state)
        {
            // Use same dependency management as OnUpdate_Unsafe for consistency
            var singleClipHandle = new UpdateSingleClipStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            singleClipHandle = new CleanSingleClipStatesJob().ScheduleParallel(singleClipHandle);

            var linearBlendHandle = new UpdateLinearBlendStateMachineStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            linearBlendHandle = new CleanLinearBlendStatesJob().ScheduleParallel(linearBlendHandle);

            state.Dependency = JobHandle.CombineDependencies(singleClipHandle, linearBlendHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnUpdate_Unsafe(ref SystemState state)
        {
            var singleClipHandle = new UpdateSingleClipStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            singleClipHandle = new CleanSingleClipStatesJob().ScheduleParallel(singleClipHandle);

            var linearBlendHandle = new UpdateLinearBlendStateMachineStatesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            linearBlendHandle = new CleanLinearBlendStatesJob().ScheduleParallel(linearBlendHandle);

            state.Dependency = JobHandle.CombineDependencies(singleClipHandle, linearBlendHandle);
        }
    }
}