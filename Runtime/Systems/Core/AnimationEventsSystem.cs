using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace DMotion
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(AnimationStateMachineSystem))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct AnimationEventsSystem : ISystem
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
            state.Dependency = new RaiseAnimationEventsJob().ScheduleParallel(state.Dependency);
        }
    }
}