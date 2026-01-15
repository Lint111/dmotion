using Unity.Burst;
using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Initializes the StateMachineContext buffer for entities with AnimationStateMachine.
    /// Runs once per entity on creation to set up the hierarchy navigation stack.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct InitializeStateMachineStackSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AnimationStateMachine>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            // Find entities with AnimationStateMachine but no StateMachineContext buffer
            foreach (var (stateMachine, entity) in SystemAPI.Query<RefRO<AnimationStateMachine>>()
                .WithNone<StateMachineContext>()
                .WithEntityAccess())
            {
                // Add the buffer component
                var buffer = ecb.AddBuffer<StateMachineContext>(entity);

                // Initialize with root context
                buffer.Add(new StateMachineContext
                {
                    CurrentStateIndex = stateMachine.ValueRO.CurrentState.StateIndex,
                    ParentSubMachineIndex = -1, // Root level
                    Level = 0
                });
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
