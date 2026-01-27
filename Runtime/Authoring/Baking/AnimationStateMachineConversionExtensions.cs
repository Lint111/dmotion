using System.Collections.Generic;
using System.Linq;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Authoring
{
    public static class AnimationStateMachineConversionExtensions
    {
        public static SmartBlobberHandle<SkeletonClipSetBlob> RequestCreateBlobAsset(
            this IBaker baker,
            Animator animator,
            params AnimationClipAsset[] clipAssets)
        {
            return baker.RequestCreateBlobAsset(animator, (IEnumerable<AnimationClipAsset>)clipAssets);
        }

        public static SmartBlobberHandle<SkeletonClipSetBlob> RequestCreateBlobAsset(
            this IBaker baker,
            Animator animator,
            IEnumerable<AnimationClipAsset> clipAssets)
        {
            var clips = clipAssets.Select(c => new SkeletonClipConfig
            {
                clip = c.Clip,
                settings = SkeletonClipCompressionSettings.kDefaultSettings
            });
            return baker.RequestCreateBlobAsset(animator,
                new NativeArray<SkeletonClipConfig>(clips.ToArray(), Allocator.Temp));
        }

        public static SmartBlobberHandle<ClipEventsBlob> RequestCreateBlobAsset(
            this IBaker baker,
            params AnimationClipAsset[] clips)
        {
            return baker.RequestCreateBlobAsset<ClipEventsBlob, ClipEventSmartBlobberFilter>(
                new ClipEventSmartBlobberFilter()
                {
                    Clips = clips
                });
        }

        public static SmartBlobberHandle<ClipEventsBlob> RequestCreateBlobAsset(
            this IBaker baker,
            IEnumerable<AnimationClipAsset> clips)
        {
            return baker.RequestCreateBlobAsset(clips.ToArray());
        }

        public static SmartBlobberHandle<StateMachineBlob> RequestCreateBlobAsset(this IBaker baker,
            StateMachineAsset stateMachineAsset)
        {
            return baker.RequestCreateBlobAsset<StateMachineBlob, AnimationStateMachineSmartBlobberFilter>(
                new AnimationStateMachineSmartBlobberFilter
                {
                    StateMachine = stateMachineAsset
                });
        }
        
        /// <summary>
        /// Creates a BoneMaskBlob from an ILayerBoneMask.
        /// Converts AvatarMask transform paths to a bitmask of bone indices.
        /// </summary>
        /// <param name="baker">The baker instance.</param>
        /// <param name="animator">The animator with the skeleton hierarchy.</param>
        /// <param name="boneMask">The bone mask interface implementation.</param>
        /// <returns>BlobAssetReference to the created mask, or default if no valid mask.</returns>
        public static BlobAssetReference<BoneMaskBlob> CreateBoneMaskBlob(
            this IBaker baker,
            Animator animator,
            ILayerBoneMask boneMask)
        {
            if (boneMask == null || !boneMask.HasMask || animator == null)
                return default;
            
            // Get the skeleton root transform
            var skeletonRoot = animator.transform;
            
            // Build a dictionary of transform path -> bone index
            var bonePathToIndex = new Dictionary<string, int>();
            var allTransforms = skeletonRoot.GetComponentsInChildren<Transform>();
            
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var path = GetRelativePath(skeletonRoot, allTransforms[i]);
                if (!string.IsNullOrEmpty(path))
                {
                    bonePathToIndex[path] = i;
                }
            }
            
            // Also add root with empty path
            bonePathToIndex[""] = 0;
            
            int boneCount = allTransforms.Length;
            int maskLength = BoneMaskBlob.CalculateMaskLength(boneCount);
            
            // Build the bitmask
            var maskData = new ulong[maskLength];
            
            foreach (var bonePath in boneMask.GetIncludedBonePaths(skeletonRoot))
            {
                if (bonePathToIndex.TryGetValue(bonePath, out int boneIndex))
                {
                    int arrayIndex = boneIndex / 64;
                    int bitIndex = boneIndex % 64;
                    maskData[arrayIndex] |= (1UL << bitIndex);
                }
            }
            
            // Create the blob
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BoneMaskBlob>();
            
            var maskArray = builder.Allocate(ref root.Mask, maskLength);
            for (int i = 0; i < maskLength; i++)
            {
                maskArray[i] = maskData[i];
            }
            root.BoneCount = boneCount;
            
            var blobRef = builder.CreateBlobAssetReference<BoneMaskBlob>(Allocator.Persistent);
            baker.AddBlobAsset(ref blobRef, out _);
            
            return blobRef;
        }
        
        /// <summary>
        /// Gets the path of a transform relative to a root transform.
        /// </summary>
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return "";
            
            var path = target.name;
            var parent = target.parent;
            
            while (parent != null && parent != root)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
    }
}