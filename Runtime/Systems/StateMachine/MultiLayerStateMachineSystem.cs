using Unity.Burst;
using Unity.Entities;
using Unity.Profiling;

namespace DMotion
{
    /// <summary>
    /// System that updates multi-layer animation state machines.
    /// Runs before BlendAnimationStatesSystem to ensure transitions are evaluated
    /// before blend weights are calculated.
    /// 
    /// For entities with AnimationStateMachineLayer buffers (multi-layer mode).
    /// Single-layer entities use AnimationStateMachineSystem instead.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(BlendAnimationStatesSystem))]
    [UpdateAfter(typeof(AnimationStateMachineSystem))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct MultiLayerStateMachineSystem : ISystem
    {
        internal static readonly ProfilerMarker Marker_UpdateMultiLayerStateMachineJob =
            new ProfilerMarker("UpdateMultiLayerStateMachineJob");

        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new UpdateMultiLayerStateMachineJob
            {
                Marker = Marker_UpdateMultiLayerStateMachineJob
            }.ScheduleParallel(state.Dependency);
        }
    }
}
