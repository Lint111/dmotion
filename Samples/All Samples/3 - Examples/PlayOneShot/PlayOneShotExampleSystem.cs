using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMotion.Samples.PlayOneShot
{
    [RequireMatchingQueriesForUpdate]
    public partial struct PlayOneShotExampleSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var shouldPlayOneShot = keyboard.spaceKey.wasPressedThisFrame;

            foreach (var (playOneShotRequest, playOneShotExampleComponent) in SystemAPI
                         .Query<RefRW<PlayOneShotRequest>, PlayOneShotExampleComponent>())
            {
                if (shouldPlayOneShot)
                {
                    playOneShotRequest.ValueRW = PlayOneShotRequest.New(playOneShotExampleComponent.OneShotClipRef,
                        playOneShotExampleComponent.TransitionDuration, playOneShotExampleComponent.NormalizedEndTime);
                }
            }
        }
    }
}