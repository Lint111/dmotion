using System.IO;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Utility for creating AvatarMask assets alongside other assets.
    /// </summary>
    public static class AvatarMaskCreator
    {
        /// <summary>
        /// Creates a new AvatarMask asset in the same directory as the source asset.
        /// </summary>
        /// <param name="sourceAsset">The asset to co-locate the mask with.</param>
        /// <param name="maskName">Name for the new mask asset.</param>
        /// <returns>The created AvatarMask, or null if creation failed.</returns>
        public static AvatarMask CreateMaskForAsset(Object sourceAsset, string maskName)
        {
            if (sourceAsset == null)
            {
                Debug.LogError("Cannot create mask: source asset is null.");
                return null;
            }
            
            var assetPath = AssetDatabase.GetAssetPath(sourceAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("Cannot create mask: source asset has no path.");
                return null;
            }
            
            var directory = Path.GetDirectoryName(assetPath);
            var sanitizedName = SanitizeFileName(maskName);
            var maskPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{sanitizedName}.mask");
            
            var mask = new AvatarMask();
            
            // Initialize with all body parts enabled by default
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            }
            
            AssetDatabase.CreateAsset(mask, maskPath);
            AssetDatabase.SaveAssets();
            
            // Ping in project to show user where it was created
            EditorGUIUtility.PingObject(mask);
            
            Debug.Log($"Created AvatarMask at: {maskPath}");
            
            return mask;
        }
        
        /// <summary>
        /// Sanitizes a filename by removing invalid characters.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
