using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Blob storing a bone mask as a bitmask.
    /// Each bit represents whether a bone should be sampled (1) or skipped (0).
    /// Used for partial-body animation layers (e.g., upper body only).
    /// </summary>
    public struct BoneMaskBlob
    {
        /// <summary>
        /// Bitmask where each bit corresponds to a bone index.
        /// Bit 0 of element 0 = bone 0, bit 1 of element 0 = bone 1, etc.
        /// A set bit (1) means the bone IS affected by the layer.
        /// </summary>
        public BlobArray<ulong> Mask;
        
        /// <summary>
        /// Number of bones this mask covers.
        /// </summary>
        public int BoneCount;
        
        /// <summary>
        /// Gets the mask as a ReadOnlySpan for use with Kinemation's SamplePose.
        /// </summary>
        public unsafe ReadOnlySpan<ulong> AsSpan()
        {
            return new ReadOnlySpan<ulong>(Mask.GetUnsafePtr(), Mask.Length);
        }
        
        /// <summary>
        /// Checks if a specific bone is included in the mask.
        /// </summary>
        public bool IsBoneIncluded(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= BoneCount)
                return false;
            
            int arrayIndex = boneIndex / 64;
            int bitIndex = boneIndex % 64;
            
            if (arrayIndex >= Mask.Length)
                return false;
            
            return (Mask[arrayIndex] & (1UL << bitIndex)) != 0;
        }
        
        /// <summary>
        /// Calculates the number of ulong elements needed for a given bone count.
        /// </summary>
        public static int CalculateMaskLength(int boneCount)
        {
            return (boneCount + 63) / 64;
        }
    }
}
