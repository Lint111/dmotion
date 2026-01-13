using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMotion.Samples.StateMachine
{
    [RequireMatchingQueriesForUpdate]
    public partial struct SetParametersThroughCodeSystem : ISystem
    {
        private static readonly int IsRunningHash = StateMachineParameterUtils.GetHashCode("IsRunning");

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SetParametersThroughCodeSample>();
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var toggleIsRunning = keyboard.spaceKey.wasPressedThisFrame;

            foreach (var boolParameters in SystemAPI.Query<DynamicBuffer<BoolParameter>>())
            {
                if (toggleIsRunning)
                {
                    var currentValue = boolParameters.GetValue<BoolParameter, bool>(IsRunningHash);
                    boolParameters.SetValue(IsRunningHash, !currentValue);
                }
            }
        }
    }
}