using System.IO;
using System.Linq;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides context menu actions for creating DMotion StateMachineAssets from armature/rig assets.
    /// Supports Avatar assets and Model assets (FBX, etc.) that contain avatars.
    /// </summary>
    public static class ArmatureContextMenu
    {
        private const string MenuPathAvatar = "Assets/Create/DMotion/State Machine from Avatar";
        private const string MenuPathModel = "Assets/Create/DMotion/State Machine from Model";
        
        #region Avatar Context Menu
        
        /// <summary>
        /// Creates a StateMachineAsset from a selected Avatar asset.
        /// </summary>
        [MenuItem(MenuPathAvatar, false, 100)]
        private static void CreateStateMachineFromAvatar()
        {
            var avatar = Selection.activeObject as Avatar;
            if (avatar == null) return;
            
            CreateStateMachineWithRig(avatar, avatar.name);
        }
        
        [MenuItem(MenuPathAvatar, true)]
        private static bool ValidateCreateStateMachineFromAvatar()
        {
            return Selection.activeObject is Avatar;
        }
        
        #endregion
        
        #region Model Context Menu
        
        /// <summary>
        /// Creates a StateMachineAsset from a selected Model asset (FBX, etc.).
        /// Extracts the avatar and optionally references animation clips.
        /// </summary>
        [MenuItem(MenuPathModel, false, 101)]
        private static void CreateStateMachineFromModel()
        {
            var selectedObject = Selection.activeObject;
            if (selectedObject == null) return;
            
            var assetPath = AssetDatabase.GetAssetPath(selectedObject);
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null) return;
            
            // Get the avatar from the model
            var avatar = GetAvatarFromModel(assetPath);
            if (avatar == null)
            {
                EditorUtility.DisplayDialog(
                    "No Avatar Found",
                    $"The model '{selectedObject.name}' does not have an Avatar.\n\n" +
                    "Ensure the model has a valid Rig configuration in its Import Settings.",
                    "OK");
                return;
            }
            
            // Get animation clips from the model for reference
            var clips = GetAnimationClipsFromModel(assetPath);
            
            CreateStateMachineWithRig(avatar, selectedObject.name, clips);
        }
        
        [MenuItem(MenuPathModel, true)]
        private static bool ValidateCreateStateMachineFromModel()
        {
            var selectedObject = Selection.activeObject;
            if (selectedObject == null) return false;
            
            var assetPath = AssetDatabase.GetAssetPath(selectedObject);
            return AssetImporter.GetAtPath(assetPath) is ModelImporter;
        }
        
        #endregion
        
        #region Implementation
        
        /// <summary>
        /// Creates a StateMachineAsset with the given avatar pre-bound.
        /// </summary>
        private static void CreateStateMachineWithRig(Avatar avatar, string baseName, AnimationClip[] clips = null)
        {
            // Determine save path (same folder as source asset, or Assets/ for sub-assets)
            var avatarPath = AssetDatabase.GetAssetPath(avatar);
            string folderPath;
            
            if (AssetDatabase.IsSubAsset(avatar))
            {
                // Avatar is a sub-asset (e.g., inside FBX) - use parent folder
                folderPath = Path.GetDirectoryName(avatarPath);
            }
            else
            {
                folderPath = Path.GetDirectoryName(avatarPath);
            }
            
            if (string.IsNullOrEmpty(folderPath))
            {
                folderPath = "Assets";
            }
            
            // Generate unique filename
            var assetName = $"{baseName}_StateMachine";
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{assetName}.asset");
            
            // Create the StateMachineAsset
            var stateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            stateMachine.name = Path.GetFileNameWithoutExtension(assetPath);
            
            // Bind the rig
            stateMachine.BindRig(avatar, RigBindingSource.UserSelected);
            
            // Save the asset
            AssetDatabase.CreateAsset(stateMachine, assetPath);
            AssetDatabase.SaveAssets();
            
            // Select the new asset
            Selection.activeObject = stateMachine;
            EditorGUIUtility.PingObject(stateMachine);
            
            // Show info about available clips
            if (clips != null && clips.Length > 0)
            {
                var clipNames = string.Join("\n  - ", clips.Select(c => c.name).Take(10));
                if (clips.Length > 10)
                {
                    clipNames += $"\n  ... and {clips.Length - 10} more";
                }
                
                Debug.Log($"[DMotion] Created StateMachineAsset: {assetPath}\n" +
                          $"Bound to Avatar: {avatar.name}\n" +
                          $"Available clips from source model:\n  - {clipNames}");
            }
            else
            {
                Debug.Log($"[DMotion] Created StateMachineAsset: {assetPath}\n" +
                          $"Bound to Avatar: {avatar.name}");
            }
        }
        
        /// <summary>
        /// Extracts the Avatar from a model asset.
        /// </summary>
        private static Avatar GetAvatarFromModel(string modelPath)
        {
            // Load all assets at the path and find the Avatar
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            return allAssets.OfType<Avatar>().FirstOrDefault();
        }
        
        /// <summary>
        /// Gets all AnimationClips from a model asset.
        /// </summary>
        private static AnimationClip[] GetAnimationClipsFromModel(string modelPath)
        {
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            return allAssets
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")) // Exclude preview clips
                .ToArray();
        }
        
        #endregion
    }
}
