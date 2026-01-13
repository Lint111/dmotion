using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMotion.Samples.EnumTransitions
{
    public struct EnumTransitionsSample : IComponentData {}

    [RequireMatchingQueriesForUpdate]
    public partial struct EnumTransitionsSampleSystem : ISystem
    {
        private static readonly int IdleModeHash = StateMachineParameterUtils.GetHashCode("IdleMode");

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnumTransitionsSample>();
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            int? newMode = null;

            // 1 = Normal, 2 = Alert, 3 = Combat
            if (keyboard.digit1Key.wasPressedThisFrame)
                newMode = (int)IdleMode.Normal;
            else if (keyboard.digit2Key.wasPressedThisFrame)
                newMode = (int)IdleMode.Alert;
            else if (keyboard.digit3Key.wasPressedThisFrame)
                newMode = (int)IdleMode.Combat;

            if (!newMode.HasValue) return;

            foreach (var intParams in SystemAPI.Query<DynamicBuffer<IntParameter>>())
            {
                intParams.SetValue(IdleModeHash, newMode.Value);
            }
        }
    }
}
