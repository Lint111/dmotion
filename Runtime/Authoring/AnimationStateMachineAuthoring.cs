using System.Linq;
using Latios.Authoring;
using Latios.Kinemation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace DMotion.Authoring
{
    public static class StateMachineEditorConstants
    {
        public const string DMotionPath = "DMotion";
    }

    [DisallowMultipleComponent]
    public class AnimationStateMachineAuthoring : MonoBehaviour
    {
        public GameObject Owner;
        public Animator Animator;

        [Tooltip("Reference to a DMotion StateMachineAsset")]
        public StateMachineAsset StateMachineAsset;

        public RootMotionMode RootMotionMode;
        public bool EnableEvents = true;

        private void Reset()
        {
            if (Animator == null)
            {
                Animator = GetComponent<Animator>();
            }

            if (Animator != null && Owner == null)
            {
                Owner = Animator.gameObject;
            }
        }
    }
    class AnimationStateMachineBaker : SmartBaker<AnimationStateMachineAuthoring, AnimationStateMachineBakeItem>{}
    struct AnimationStateMachineBakeItem : ISmartBakeItem<AnimationStateMachineAuthoring>
    {
        private Entity Owner;
        private RootMotionMode RootMotionMode;
        private bool EnableEvents;
        private SmartBlobberHandle<SkeletonClipSetBlob> clipsBlobHandle;
        private SmartBlobberHandle<StateMachineBlob> stateMachineBlobHandle;
        private SmartBlobberHandle<ClipEventsBlob> clipEventsBlobHandle;

        public bool Bake(AnimationStateMachineAuthoring authoring, IBaker baker)
        {
            var stateMachine = authoring.StateMachineAsset;
            ValidateStateMachine(authoring, stateMachine);

            Owner = baker.GetEntity(authoring.Owner, TransformUsageFlags.Dynamic);
            RootMotionMode = authoring.RootMotionMode;
            EnableEvents = authoring.EnableEvents && stateMachine.Clips.Any(c => c.Events.Length > 0);
            clipsBlobHandle = baker.RequestCreateBlobAsset(authoring.Animator, stateMachine.Clips);
            stateMachineBlobHandle = baker.RequestCreateBlobAsset(stateMachine);
            clipEventsBlobHandle = baker.RequestCreateBlobAsset(stateMachine.Clips);
            AnimationStateMachineConversionUtils.AddStateMachineParameters(baker,
                baker.GetEntity(TransformUsageFlags.Dynamic),
                stateMachine);
            return true;
        }

        public void PostProcessBlobRequests(EntityManager dstManager, Entity entity)
        {
            var stateMachineBlob = stateMachineBlobHandle.Resolve(dstManager);
            var clipsBlob = clipsBlobHandle.Resolve(dstManager);
            var clipEventsBlob = clipEventsBlobHandle.Resolve(dstManager);

            AnimationStateMachineConversionUtils.AddStateMachineSystemComponents(dstManager, entity,
                stateMachineBlob,
                clipsBlob,
                clipEventsBlob);
            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(dstManager, entity);

            if (EnableEvents)
            {
                dstManager.GetOrCreateBuffer<RaisedAnimationEvent>(entity);
            }

            if (Owner == Entity.Null)
            {
                Owner = entity;
            }

            if (Owner != entity)
            {
                AnimationStateMachineConversionUtils.AddAnimatorOwnerComponents(dstManager, Owner, entity);
            }

            AnimationStateMachineConversionUtils.AddRootMotionComponents(dstManager, Owner, entity,
                RootMotionMode);
        }

        private void ValidateStateMachine(AnimationStateMachineAuthoring authoring, StateMachineAsset stateMachine)
        {
            if (stateMachine != null)
            {
                foreach (var s in stateMachine.States)
                {
                    foreach (var c in s.Clips)
                    {
                        Assert.IsTrue(c != null && c.Clip != null,
                            $"State ({s.name}) in State Machine {stateMachine.name} has invalid clips");
                    }
                }
            }
            else
            {
                Assert.IsTrue(false, $"AnimationStateMachineAuthoring on {authoring.gameObject.name}: StateMachineAsset is null");
            }
        }
    }
}
