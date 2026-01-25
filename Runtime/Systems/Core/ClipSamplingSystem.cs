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
        /// Sampling jobs run in parallel (read-only on ClipSampler), then root motion jobs
        /// run after ALL sampling completes (due to LocalTransform write conflicts).
        /// </summary>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Phase 1: Sample bones - these jobs can run in parallel (all read ClipSampler)
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

            // Combine ALL sampling handles - root motion jobs must wait for all sampling
            // because SampleNonOptimizedBones and ApplyRootMotionToEntityJob both write LocalTransform
            var allSamplingComplete = JobHandle.CombineDependencies(
                sampleOptimizedHandle, sampleNonOptimizedHandle, sampleRootDeltasHandle);

            // Phase 2: Root motion jobs - must wait for ALL sampling to complete
            var applyRootMotionHandle = new ApplyRootMotionToEntityJob
            {
                Marker = Marker_ApplyRootMotionToEntityJob
            }.ScheduleParallel(allSamplingComplete);

            var transferRootMotionHandle = new TransferRootMotionJob
            {
                CfeDeltaPosition = SystemAPI.GetComponentLookup<RootDeltaTranslation>(true),
                CfeDeltaRotation = SystemAPI.GetComponentLookup<RootDeltaRotation>(true),
                Marker = Marker_TransferRootMotionJob
            }.ScheduleParallel(allSamplingComplete);

            // Final dependency includes all jobs
            state.Dependency = JobHandle.CombineDependencies(
                allSamplingComplete, applyRootMotionHandle, transferRootMotionHandle);
        }
    }
}