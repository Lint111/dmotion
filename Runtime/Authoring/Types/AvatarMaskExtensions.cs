using System.Collections.Generic;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Extension methods for Unity's AvatarMask.
    /// Provides helper methods for extracting bone path information for baking.
    /// </summary>
    public static class AvatarMaskExtensions
    {
        /// <summary>
        /// Gets all bone paths that are active in this mask.
        /// Used by the baking system to convert AvatarMask to BoneMaskBlob.
        /// </summary>
        /// <param name="mask">The avatar mask.</param>
        /// <returns>Enumerable of active bone paths.</returns>
        public static IEnumerable<string> GetIncludedBonePaths(this AvatarMask mask)
        {
            if (mask == null)
                yield break;
            
            for (int i = 0; i < mask.transformCount; i++)
            {
                if (mask.GetTransformActive(i))
                {
                    yield return mask.GetTransformPath(i);
                }
            }
        }
        
        /// <summary>
        /// Checks if a specific bone path is included in the mask.
        /// </summary>
        /// <param name="mask">The avatar mask.</param>
        /// <param name="bonePath">Path relative to skeleton root.</param>
        /// <returns>True if the bone is active in this mask.</returns>
        public static bool IsBoneIncluded(this AvatarMask mask, string bonePath)
        {
            if (mask == null)
                return false;
            
            for (int i = 0; i < mask.transformCount; i++)
            {
                if (mask.GetTransformPath(i) == bonePath)
                {
                    return mask.GetTransformActive(i);
                }
            }
            
            return false;
        }
    }
}
