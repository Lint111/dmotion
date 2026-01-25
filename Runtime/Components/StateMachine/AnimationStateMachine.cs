using System;
using System.Runtime.CompilerServices;
using DMotion.Authoring;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Entities;

namespace DMotion
{
    [BurstCompile]
    internal struct StateMachineStateRef
    {
        internal ushort StateIndex;
        internal sbyte AnimationStateId;
        internal bool IsValid => AnimationStateId >= 0;

        internal static StateMachineStateRef Null => new() { AnimationStateId = -1 };
    }

    /// <summary>
    /// Main animation state machine component containing blob references and current state.
    ///
    /// Blob Lifecycle: These BlobAssetReferences are created during SmartBlobber baking
    /// (see ClipEventsBlobConverter and StateMachineBlobConverter). Their lifecycle is
    /// managed by Unity's baking system - they are automatically disposed when the
    /// subscene is unloaded. No manual disposal is required or recommended.
    /// </summary>
    [BurstCompile]
    internal struct AnimationStateMachine : IComponentData
    {
        /// <summary>Baked skeleton clip set from Latios Kinemation. Lifecycle managed by baking system.</summary>
        internal BlobAssetReference<SkeletonClipSetBlob> ClipsBlob;
        /// <summary>Baked clip events. Lifecycle managed by baking system.</summary>
        internal BlobAssetReference<ClipEventsBlob> ClipEventsBlob;
        /// <summary>Baked state machine definition. Lifecycle managed by baking system.</summary>
        internal BlobAssetReference<StateMachineBlob> StateMachineBlob;
        internal StateMachineStateRef CurrentState;
        
        //for now we don't use PreviousState for anything other than debugging
        #if UNITY_EDITOR || DEBUG
        internal StateMachineStateRef PreviousState;
        #endif

        internal ref AnimationStateBlob CurrentStateBlob
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref StateMachineBlob.Value.States[CurrentState.StateIndex];
        }
    }
    
    #if UNITY_EDITOR || DEBUG
    internal class AnimationStateMachineDebug : IComponentData, ICloneable
    {
        internal StateMachineAsset StateMachineAsset;
        public object Clone()
        {
            return new AnimationStateMachineDebug
            {
                StateMachineAsset = StateMachineAsset
            };
        }
    }
    #endif
}