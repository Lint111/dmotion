using System.Collections.Generic;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Interface for providing bone mask data to animation layers.
    /// Implementations define which bones are affected by a layer.
    /// 
    /// The baking system uses GetIncludedBonePaths to build the runtime bitmask.
    /// </summary>
    public interface ILayerBoneMask
    {
        /// <summary>
        /// Whether this mask has valid data.
        /// </summary>
        bool HasMask { get; }
        
        /// <summary>
        /// Gets the paths of all bones that should be affected by this mask.
        /// Paths are relative to the skeleton root (e.g., "Hips/Spine/Chest").
        /// </summary>
        /// <param name="skeletonRoot">The root transform of the skeleton.</param>
        /// <returns>Enumerable of bone paths that are included in the mask.</returns>
        IEnumerable<string> GetIncludedBonePaths(Transform skeletonRoot);
        
        /// <summary>
        /// Checks if a specific bone path is included in the mask.
        /// </summary>
        /// <param name="bonePath">Path relative to skeleton root.</param>
        /// <returns>True if the bone should be affected by this layer.</returns>
        bool IsBoneIncluded(string bonePath);
    }
    
    /// <summary>
    /// AvatarMask-based bone mask implementation.
    /// Extracts bone paths from Unity's AvatarMask asset.
    /// </summary>
    [System.Serializable]
    public class AvatarMaskBoneMask : ILayerBoneMask
    {
        [SerializeField]
        private AvatarMask avatarMask;
        
        public AvatarMaskBoneMask() { }
        
        public AvatarMaskBoneMask(AvatarMask mask)
        {
            avatarMask = mask;
        }
        
        public bool HasMask => avatarMask != null;
        
        /// <summary>
        /// The underlying AvatarMask asset.
        /// </summary>
        public AvatarMask AvatarMask
        {
            get => avatarMask;
            set => avatarMask = value;
        }
        
        public IEnumerable<string> GetIncludedBonePaths(Transform skeletonRoot)
        {
            if (avatarMask == null)
                yield break;
            
            // AvatarMask stores transform paths with active state
            for (int i = 0; i < avatarMask.transformCount; i++)
            {
                if (avatarMask.GetTransformActive(i))
                {
                    yield return avatarMask.GetTransformPath(i);
                }
            }
        }
        
        public bool IsBoneIncluded(string bonePath)
        {
            if (avatarMask == null)
                return false;
            
            for (int i = 0; i < avatarMask.transformCount; i++)
            {
                if (avatarMask.GetTransformPath(i) == bonePath)
                {
                    return avatarMask.GetTransformActive(i);
                }
            }
            
            return false;
        }
    }
    
    /// <summary>
    /// Custom bone mask that uses explicit bone paths.
    /// Use for programmatic or runtime-defined masks.
    /// </summary>
    [System.Serializable]
    public class ExplicitBoneMask : ILayerBoneMask
    {
        [SerializeField]
        private List<string> includedBonePaths = new();
        
        private HashSet<string> pathLookup;
        
        public ExplicitBoneMask() { }
        
        public ExplicitBoneMask(IEnumerable<string> bonePaths)
        {
            includedBonePaths = new List<string>(bonePaths);
            RebuildLookup();
        }
        
        public bool HasMask => includedBonePaths != null && includedBonePaths.Count > 0;
        
        /// <summary>
        /// The list of included bone paths.
        /// </summary>
        public List<string> IncludedBonePaths
        {
            get => includedBonePaths;
            set
            {
                includedBonePaths = value;
                RebuildLookup();
            }
        }
        
        public IEnumerable<string> GetIncludedBonePaths(Transform skeletonRoot)
        {
            return includedBonePaths != null ? includedBonePaths : System.Array.Empty<string>();
        }
        
        public bool IsBoneIncluded(string bonePath)
        {
            if (pathLookup == null)
                RebuildLookup();
            
            return pathLookup?.Contains(bonePath) ?? false;
        }
        
        private void RebuildLookup()
        {
            pathLookup = includedBonePaths != null 
                ? new HashSet<string>(includedBonePaths) 
                : new HashSet<string>();
        }
    }
}
