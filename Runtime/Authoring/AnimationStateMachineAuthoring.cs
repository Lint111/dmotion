using System.Linq;
using DMotion.Authoring.UnityControllerBridge;
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

    /// <summary>
    /// Determines how the AnimationStateMachineAuthoring component sources its state machine.
    /// </summary>
    public enum StateMachineSourceMode
    {
        /// <summary>
        /// Directly reference a DMotion StateMachineAsset.
        /// </summary>
        Direct,

        /// <summary>
        /// Use a UnityControllerBridgeAsset that automatically converts a Unity AnimatorController.
        /// </summary>
        UnityControllerBridge
    }

    [DisallowMultipleComponent]
    public class AnimationStateMachineAuthoring : MonoBehaviour
    {
        public GameObject Owner;
        public Animator Animator;

        [Tooltip("How to source the state machine asset")]
        public StateMachineSourceMode SourceMode = StateMachineSourceMode.Direct;

        [Tooltip("Direct reference to a DMotion StateMachineAsset (used when SourceMode is Direct)")]
        public StateMachineAsset StateMachineAsset;

        [Tooltip("Bridge asset that automatically converts Unity AnimatorController (used when SourceMode is UnityControllerBridge)")]
        public UnityControllerBridgeAsset ControllerBridge;

        public RootMotionMode RootMotionMode;
        public bool EnableEvents = true;

        /// <summary>
        /// Resolves the StateMachineAsset based on the current SourceMode.
        /// </summary>
        public StateMachineAsset GetStateMachine()
        {
            switch (SourceMode)
            {
                case StateMachineSourceMode.Direct:
                    return StateMachineAsset;

                case StateMachineSourceMode.UnityControllerBridge:
                    return ControllerBridge != null ? ControllerBridge.GeneratedStateMachine : null;

                default:
                    return null;
            }
        }

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
            // Add dependency on bridge asset if using bridge mode
            if (authoring.SourceMode == StateMachineSourceMode.UnityControllerBridge && authoring.ControllerBridge != null)
            {
                baker.DependsOn(authoring.ControllerBridge);

                // Also add dependency on the generated StateMachine asset
                if (authoring.ControllerBridge.GeneratedStateMachine != null)
                {
                    baker.DependsOn(authoring.ControllerBridge.GeneratedStateMachine);
                }
            }

            // Resolve the StateMachine through GetStateMachine()
            var stateMachine = authoring.GetStateMachine();
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
                // Log error if no state machine is available
                string errorMessage = authoring.SourceMode switch
                {
                    StateMachineSourceMode.Direct => "StateMachineAsset is null",
                    StateMachineSourceMode.UnityControllerBridge => authoring.ControllerBridge == null
                        ? "ControllerBridge is null"
                        : "ControllerBridge has no GeneratedStateMachine (may not have been converted yet)",
                    _ => "Unknown source mode"
                };

                Assert.IsTrue(false, $"AnimationStateMachineAuthoring on {authoring.gameObject.name}: {errorMessage}");
            }
        }
    }
}