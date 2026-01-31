using System.Collections.Generic;
using System.Linq;
using Latios.Authoring;
using Latios.Kinemation;
using Unity.Collections;
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
    
    /// <summary>
    /// Per-layer blob handles for multi-layer baking.
    /// Must be unmanaged for SmartBaker compatibility.
    /// </summary>
    struct LayerBlobHandles
    {
        public SmartBlobberHandle<SkeletonClipSetBlob> ClipsBlobHandle;
        public SmartBlobberHandle<StateMachineBlob> StateMachineBlobHandle;
        public SmartBlobberHandle<ClipEventsBlob> ClipEventsBlobHandle;
        // BoneMask is created directly during baking, not via SmartBlobber
        public BlobAssetReference<BoneMaskBlob> BoneMaskBlob;
        public float Weight;
        public LayerBlendMode BlendMode;
    }
    
    struct AnimationStateMachineBakeItem : ISmartBakeItem<AnimationStateMachineAuthoring>
    {
        /// <summary>
        /// Maximum number of layers supported per state machine.
        /// </summary>
        private const int MaxLayers = 8;
        
        private Entity Owner;
        private RootMotionMode RootMotionMode;
        private bool EnableEvents;
        private bool IsMultiLayer;
        
        // Single-layer handles
        private SmartBlobberHandle<SkeletonClipSetBlob> clipsBlobHandle;
        private SmartBlobberHandle<StateMachineBlob> stateMachineBlobHandle;
        private SmartBlobberHandle<ClipEventsBlob> clipEventsBlobHandle;
        
        // Multi-layer handles - fixed size unmanaged storage
        // Using 4096 bytes to fit up to 8 layers (~100 bytes each)
        private FixedList4096Bytes<LayerBlobHandles> layerHandles;

        public bool Bake(AnimationStateMachineAuthoring authoring, IBaker baker)
        {
            var stateMachine = authoring.StateMachineAsset;
            ValidateStateMachine(authoring, stateMachine);

            Owner = baker.GetEntity(authoring.Owner, TransformUsageFlags.Dynamic);
            RootMotionMode = authoring.RootMotionMode;
            IsMultiLayer = stateMachine.IsMultiLayer;
            
            if (IsMultiLayer)
            {
                // Multi-layer: create separate blob handles for each layer
                var layers = stateMachine.GetLayers().ToList();
                layerHandles = default; // FixedList is a struct, no allocation needed
                EnableEvents = false;
                
                if (layers.Count > MaxLayers)
                {
                    Debug.LogWarning($"State machine has {layers.Count} layers but max supported is {MaxLayers}. Extra layers will be ignored.");
                }
                
                var layerCount = System.Math.Min(layers.Count, MaxLayers);
                for (int i = 0; i < layerCount; i++)
                {
                    var layer = layers[i];
                    if (!layer.HasValidStateMachine)
                    {
                        Debug.LogWarning($"Layer '{layer.name}' has no valid state machine, skipping.");
                        continue;
                    }
                    
                    var layerMachine = layer.NestedStateMachine;
                    var layerClips = layerMachine.Clips;
                    
                    // Check for events in this layer
                    if (authoring.EnableEvents && layerClips.Any(c => c.Events.Length > 0))
                    {
                        EnableEvents = true;
                    }
                    
                    // Create mask blob if layer has a mask
                    var boneMaskBlob = layer.HasMask 
                        ? baker.CreateBoneMaskBlob(authoring.Animator, layer.AvatarMask)
                        : default;
                    
                    layerHandles.Add(new LayerBlobHandles
                    {
                        ClipsBlobHandle = baker.RequestCreateBlobAsset(authoring.Animator, layerClips),
                        StateMachineBlobHandle = baker.RequestCreateBlobAsset(layerMachine),
                        ClipEventsBlobHandle = baker.RequestCreateBlobAsset(layerClips),
                        BoneMaskBlob = boneMaskBlob,
                        Weight = layer.Weight,
                        BlendMode = layer.BlendMode
                    });
                }
            }
            else
            {
                // Single-layer: existing behavior
                EnableEvents = authoring.EnableEvents && stateMachine.Clips.Any(c => c.Events.Length > 0);
                clipsBlobHandle = baker.RequestCreateBlobAsset(authoring.Animator, stateMachine.Clips);
                stateMachineBlobHandle = baker.RequestCreateBlobAsset(stateMachine);
                clipEventsBlobHandle = baker.RequestCreateBlobAsset(stateMachine.Clips);
            }
            
            AnimationStateMachineConversionUtils.AddStateMachineParameters(baker,
                baker.GetEntity(TransformUsageFlags.Dynamic),
                stateMachine);
            return true;
        }

        public void PostProcessBlobRequests(EntityManager dstManager, Entity entity)
        {
            if (IsMultiLayer)
            {
                PostProcessMultiLayer(dstManager, entity);
            }
            else
            {
                PostProcessSingleLayer(dstManager, entity);
            }
            
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
        
        private void PostProcessSingleLayer(EntityManager dstManager, Entity entity)
        {
            var stateMachineBlob = stateMachineBlobHandle.Resolve(dstManager);
            var clipsBlob = clipsBlobHandle.Resolve(dstManager);
            var clipEventsBlob = clipEventsBlobHandle.Resolve(dstManager);

            AnimationStateMachineConversionUtils.AddStateMachineSystemComponents(dstManager, entity,
                stateMachineBlob,
                clipsBlob,
                clipEventsBlob);
            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(dstManager, entity);
        }
        
        private void PostProcessMultiLayer(EntityManager dstManager, Entity entity)
        {
            // Add layer buffer
            var layerBuffer = dstManager.GetOrCreateBuffer<AnimationStateMachineLayer>(entity);
            layerBuffer.Capacity = layerHandles.Length;
            
            for (int i = 0; i < layerHandles.Length; i++)
            {
                var handles = layerHandles[i];
                var stateMachineBlob = handles.StateMachineBlobHandle.Resolve(dstManager);
                var clipsBlob = handles.ClipsBlobHandle.Resolve(dstManager);
                var clipEventsBlob = handles.ClipEventsBlobHandle.Resolve(dstManager);
                
                layerBuffer.Add(new AnimationStateMachineLayer
                {
                    StateMachineBlob = stateMachineBlob,
                    ClipsBlob = clipsBlob,
                    ClipEventsBlob = clipEventsBlob,
                    BoneMask = handles.BoneMaskBlob, // Already created during baking
                    LayerIndex = (byte)i,
                    Weight = handles.Weight,
                    BlendMode = handles.BlendMode,
                    CurrentState = StateMachineStateRef.Null
                });
            }
            
            // Add per-layer tracking buffers
            var transitionBuffer = dstManager.GetOrCreateBuffer<AnimationLayerTransition>(entity);
            transitionBuffer.Capacity = layerHandles.Length;
            
            var currentStateBuffer = dstManager.GetOrCreateBuffer<AnimationLayerCurrentState>(entity);
            currentStateBuffer.Capacity = layerHandles.Length;
            
            var requestBuffer = dstManager.GetOrCreateBuffer<AnimationLayerTransitionRequest>(entity);
            requestBuffer.Capacity = layerHandles.Length;
            
            // Initialize tracking buffers for each layer
            for (int i = 0; i < layerHandles.Length; i++)
            {
                transitionBuffer.Add(AnimationLayerTransition.Null((byte)i));
                currentStateBuffer.Add(AnimationLayerCurrentState.Null((byte)i));
                requestBuffer.Add(AnimationLayerTransitionRequest.Null((byte)i));
            }
            
            // Add shared animation state components (used by both single and multi-layer)
            AnimationStateMachineConversionUtils.AddAnimationStateSystemComponents(dstManager, entity);
            
            // Add state type buffers (shared across layers)
            dstManager.GetOrCreateBuffer<SingleClipState>(entity);
            dstManager.GetOrCreateBuffer<LinearBlendStateMachineState>(entity);
            dstManager.GetOrCreateBuffer<Directional2DBlendStateMachineState>(entity);
        }

        private void ValidateStateMachine(AnimationStateMachineAuthoring authoring, StateMachineAsset stateMachine)
        {
            if (stateMachine == null)
            {
                Assert.IsTrue(false, $"AnimationStateMachineAuthoring on {authoring.gameObject.name}: StateMachineAsset is null");
                return;
            }
            
            if (stateMachine.IsMultiLayer)
            {
                // Validate multi-layer structure
                foreach (var layer in stateMachine.GetLayers())
                {
                    if (!layer.HasValidStateMachine)
                    {
                        Debug.LogWarning($"Layer '{layer.name}' in {stateMachine.name} has no state machine assigned.");
                        continue;
                    }
                    
                    ValidateLayerStateMachine(authoring, layer.NestedStateMachine, layer.name);
                }
            }
            else
            {
                // Validate single-layer structure
                foreach (var s in stateMachine.States)
                {
                    foreach (var c in s.Clips)
                    {
                        Assert.IsTrue(c != null && c.Clip != null,
                            $"State ({s.name}) in State Machine {stateMachine.name} has invalid clips");
                    }
                }
            }
        }
        
        private void ValidateLayerStateMachine(AnimationStateMachineAuthoring authoring, StateMachineAsset layerMachine, string layerName)
        {
            foreach (var s in layerMachine.States)
            {
                foreach (var c in s.Clips)
                {
                    Assert.IsTrue(c != null && c.Clip != null,
                        $"State ({s.name}) in Layer {layerName} on {authoring.gameObject.name} has invalid clips");
                }
            }
        }
    }
}
