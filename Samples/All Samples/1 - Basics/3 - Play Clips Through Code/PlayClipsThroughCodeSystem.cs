using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMotion.Samples.PlayClipsThroughCode
{
    [RequireMatchingQueriesForUpdate]
    public partial struct PlayClipsThroughCodeSystem : ISystem
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

            var playWalk = keyboard.digit1Key.wasPressedThisFrame;
            var playRun = keyboard.digit2Key.wasPressedThisFrame;

            foreach (var (playSingleClipRequest, playClipsComponent) in
                     SystemAPI.Query<RefRW<PlaySingleClipRequest>, PlayClipsThroughCodeComponent>())
            {
                if (playWalk)
                {
                    playSingleClipRequest.ValueRW = PlaySingleClipRequest.New(playClipsComponent.WalkClip,
                        loop: true,
                        playClipsComponent.TransitionDuration);
                }
                else if (playRun)
                {
                    playSingleClipRequest.ValueRW = PlaySingleClipRequest.New(playClipsComponent.RunClip,
                        loop: true,
                        playClipsComponent.TransitionDuration);
                }
            }
        }
    }
}