using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMotion.Samples.BlendTree1D
{
    public struct BlendTree1DSample : IComponentData {}

    [RequireMatchingQueriesForUpdate]
    public partial struct BlendTree1DSampleSystem : ISystem
    {
        private static readonly int SpeedHash = StateMachineParameterUtils.GetHashCode("Speed");
        private static readonly int InAirHash = StateMachineParameterUtils.GetHashCode("InAir");

        private const float MinSpeed = 0f;
        private const float MaxSpeed = 5f;
        private const float SpeedChangeRate = 3f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BlendTree1DSample>();
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float speedDelta = 0f;

            // W/Up = increase speed, S/Down = decrease speed
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                speedDelta = SpeedChangeRate * SystemAPI.Time.DeltaTime;
            else if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                speedDelta = -SpeedChangeRate * SystemAPI.Time.DeltaTime;

            // Space = toggle InAir
            var toggleInAir = keyboard.spaceKey.wasPressedThisFrame;

            foreach (var (floatParams, boolParams) in
                SystemAPI.Query<DynamicBuffer<FloatParameter>, DynamicBuffer<BoolParameter>>())
            {
                // Update speed
                if (math.abs(speedDelta) > float.Epsilon)
                {
                    var currentSpeed = floatParams.GetValue<FloatParameter, float>(SpeedHash);
                    var newSpeed = math.clamp(currentSpeed + speedDelta, MinSpeed, MaxSpeed);
                    floatParams.SetValue(SpeedHash, newSpeed);
                }

                // Toggle InAir
                if (toggleInAir)
                {
                    var inAir = boolParams.GetValue<BoolParameter, bool>(InAirHash);
                    boolParams.SetValue(InAirHash, !inAir);
                }
            }
        }
    }
}
