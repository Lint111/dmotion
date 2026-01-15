using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace DMotion
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct ClipSamplingSystem : ISystem
    {
        internal static readonly ProfilerMarker Marker_SampleOptimizedBonesJob =
            new("SampleOptimizedBonesJob");

        internal static readonly ProfilerMarker Marker_SampleNonOptimizedBonesJob =
            new("SampleNonOptimizedBonesJob");

        internal static readonly ProfilerMarker Marker_SampleRootDeltasJob =
            new("SampleRootDeltasJob");

        internal static readonly ProfilerMarker Marker_ApplyRootMotionToEntityJob =
            new("ApplyRootMotionToEntityJob");

        internal static readonly ProfilerMarker Marker_TransferRootMotionJob =
            new("TransferRootMotionJob");

        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        /// <summary>
        /// Schedules all clip sampling jobs with proper dependency management.
        /// Jobs run in parallel where possible, with root motion jobs depending on delta sampling.
        /// </summary>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Sample bones - these jobs can run in parallel from initial dependency
            var sampleOptimizedHandle = new SampleOptimizedBonesJob
            {
                Marker = Marker_SampleOptimizedBonesJob
            }.ScheduleParallel(state.Dependency);

            var sampleNonOptimizedHandle = new SampleNonOptimizedBones
            {
                BfeClipSampler = SystemAPI.GetBufferLookup<ClipSampler>(true),
                Marker = Marker_SampleNonOptimizedBonesJob
            }.ScheduleParallel(state.Dependency);

            var sampleRootDeltasHandle = new SampleRootDeltasJob
            {
                Marker = Marker_SampleRootDeltasJob
            }.ScheduleParallel(state.Dependency);

            // Root motion jobs depend on delta sampling
            var applyRootMotionHandle = new ApplyRootMotionToEntityJob
            {
                Marker = Marker_ApplyRootMotionToEntityJob
            }.ScheduleParallel(sampleRootDeltasHandle);

            var transferRootMotionHandle = new TransferRootMotionJob
            {
                CfeDeltaPosition = SystemAPI.GetComponentLookup<RootDeltaTranslation>(true),
                CfeDeltaRotation = SystemAPI.GetComponentLookup<RootDeltaRotation>(true),
                Marker = Marker_TransferRootMotionJob
            }.ScheduleParallel(sampleRootDeltasHandle);

            // Combine all job handles for proper dependency tracking
            state.Dependency = JobHandle.CombineDependencies(sampleOptimizedHandle, sampleNonOptimizedHandle,
                transferRootMotionHandle);
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, applyRootMotionHandle);
        }
    }
}