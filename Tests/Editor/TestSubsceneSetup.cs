#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMotion.Authoring;
using DMotion.PerformanceTests;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMotion.Tests
{
    /// <summary>
    /// Sets up test scenes with animation prefabs using proper subscene structure for DOTS baking.
    /// Creates two scenes:
    /// - TestAnimationScene.unity (main scene with SubScene component)
    /// - TestAnimationScene_Content.unity (subscene content with animation prefabs)
    /// </summary>
    public static class TestSubsceneSetup
    {
        private const string TestDataFolder = "Assets/DMotion.TestData";
        private const string TestScenePath = "Assets/DMotion.TestData/TestAnimationScene.unity";
        private const string ContentScenePath = "Assets/DMotion.TestData/TestAnimationScene_Content.unity";
        private const string SourcePrefabsPath = "Packages/com.gamedevpro.dmotion/Tests/Data";
        private const string ModelsPath = "Packages/com.gamedevpro.dmotion/Tests/Data/Models";
        
        // Default avatar to use - from Armature.fbx
        private const string DefaultAvatarPath = "Packages/com.gamedevpro.dmotion/Tests/Data/Models/Armature.fbx";

        [MenuItem("DMotion/Tests/Setup Test Scene", false, 110)]
        public static void SetupTestScene()
        {
            // Delete existing test data folder for clean setup
            DeleteTestDataFolder();

            // Create fresh folder
            EnsureTestDataFolder();

            // Step 0: Validate and fix FBX import settings
            ValidateAndFixFbxImportSettings();

            // Step 1: Create/update the content scene with animation prefabs
            int addedCount = CreateContentScene();

            // Step 2: Create/update the main scene with SubScene reference
            CreateMainSceneWithSubScene();

            Debug.Log($"[TestSubsceneSetup] Setup complete! Added {addedCount} prefabs.");
            Debug.Log("[TestSubsceneSetup] Opening main scene - subscene will bake automatically.");
        }

        private static int CreateContentScene()
        {
            Scene contentScene;
            if (File.Exists(ContentScenePath))
            {
                contentScene = EditorSceneManager.OpenScene(ContentScenePath, OpenSceneMode.Single);
                Debug.Log($"[TestSubsceneSetup] Opened existing content scene: {ContentScenePath}");
            }
            else
            {
                contentScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log($"[TestSubsceneSetup] Creating new content scene: {ContentScenePath}");
            }

            // Find and add test prefabs
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SourcePrefabsPath });
            int addedCount = 0;

            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (prefab == null) continue;

                // Check if it has animation components worth testing
                if (prefab.GetComponent<AnimationStateMachineAuthoring>() == null &&
                    prefab.GetComponent<PlayClipAuthoring>() == null &&
                    prefab.GetComponent<PerformanceTestsAuthoring>() == null)
                {
                    continue;
                }

                // Validate prefab has proper Avatar setup
                if (!ValidateAndFixPrefabAnimator(prefab, out string error))
                {
                    Debug.LogWarning($"[TestSubsceneSetup] Prefab validation warning for {prefab.name}: {error}");
                    // Continue anyway - the prefab might still work for some tests
                }

                // Check if already in scene
                var existingName = prefab.name + "_TestInstance";
                var existing = GameObject.Find(existingName);
                if (existing != null)
                {
                    Debug.Log($"[TestSubsceneSetup] Skipping {prefab.name} - already in scene");
                    continue;
                }

                // Instantiate in scene
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, contentScene);
                instance.name = existingName;
                addedCount++;

                Debug.Log($"[TestSubsceneSetup] Added {prefab.name} to content scene");
            }

            // Save the content scene
            EditorSceneManager.SaveScene(contentScene, ContentScenePath);
            Debug.Log($"[TestSubsceneSetup] Saved content scene with {addedCount} new prefabs");

            return addedCount;
        }

        private static void CreateMainSceneWithSubScene()
        {
            // Create or open the main scene
            Scene mainScene;
            if (File.Exists(TestScenePath))
            {
                mainScene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
                Debug.Log($"[TestSubsceneSetup] Opened existing main scene: {TestScenePath}");
            }
            else
            {
                mainScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log($"[TestSubsceneSetup] Creating new main scene: {TestScenePath}");
            }

            // Check if SubScene already exists
            var existingSubScene = Object.FindFirstObjectByType<SubScene>();
            if (existingSubScene != null)
            {
                Debug.Log("[TestSubsceneSetup] SubScene already exists in main scene");
                // Update the reference in case it changed
                var contentSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ContentScenePath);
                if (contentSceneAsset != null && existingSubScene.SceneAsset != contentSceneAsset)
                {
                    existingSubScene.SceneAsset = contentSceneAsset;
                    Debug.Log("[TestSubsceneSetup] Updated SubScene reference");
                }
            }
            else
            {
                // Create GameObject with SubScene component
                var subSceneGO = new GameObject("TestAnimationSubScene");
                var subScene = subSceneGO.AddComponent<SubScene>();

                // Reference the content scene
                var contentSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ContentScenePath);
                if (contentSceneAsset != null)
                {
                    subScene.SceneAsset = contentSceneAsset;
                    Debug.Log("[TestSubsceneSetup] Created SubScene component referencing content scene");
                }
                else
                {
                    Debug.LogError($"[TestSubsceneSetup] Could not load content scene asset: {ContentScenePath}");
                }
            }

            // Save the main scene
            EditorSceneManager.SaveScene(mainScene, TestScenePath);
            Debug.Log($"[TestSubsceneSetup] Saved main scene: {TestScenePath}");
        }

        [MenuItem("DMotion/Tests/Open Test Scene", false, 111)]
        public static void OpenTestScene()
        {
            if (!File.Exists(TestScenePath))
            {
                Debug.LogError($"[TestSubsceneSetup] No scene found at {TestScenePath}. Run Setup first.");
                return;
            }

            EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            Debug.Log("[TestSubsceneSetup] Opened test scene. SubScene should bake automatically.");
        }

        [MenuItem("DMotion/Tests/Force Reimport Subscene", false, 112)]
        public static void ForceReimportSubscene()
        {
            if (!File.Exists(ContentScenePath))
            {
                Debug.LogError($"[TestSubsceneSetup] No content scene found. Run Setup first.");
                return;
            }

            AssetDatabase.ImportAsset(ContentScenePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[TestSubsceneSetup] Forced reimport of content scene - baking will run.");
        }

        private static void DeleteTestDataFolder()
        {
            if (AssetDatabase.IsValidFolder(TestDataFolder))
            {
                Debug.Log($"[TestSubsceneSetup] Deleting existing folder: {TestDataFolder}");
                AssetDatabase.DeleteAsset(TestDataFolder);
                AssetDatabase.Refresh();
            }
        }

        private static void EnsureTestDataFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestDataFolder))
            {
                AssetDatabase.CreateFolder("Assets", "DMotion.TestData");
                Debug.Log($"[TestSubsceneSetup] Created folder: {TestDataFolder}");
            }
        }

        /// <summary>
        /// Validates FBX import settings and fixes common issues like "Optimize Game Objects".
        /// </summary>
        private static void ValidateAndFixFbxImportSettings()
        {
            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { SourcePrefabsPath });
            bool needsReimport = false;
            var modelsToReimport = new List<string>();

            foreach (var guid in fbxGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                bool modified = false;

                // Fix 1: Disable "Optimize Game Objects" - it causes issues with Kinemation baking
                if (importer.optimizeGameObjects)
                {
                    importer.optimizeGameObjects = false;
                    modified = true;
                    Debug.LogWarning($"[TestSubsceneSetup] Disabled 'Optimize Game Objects' on: {path}");
                }

                // Fix 2: Ensure humanoid models have Avatar setup
                if (importer.animationType == ModelImporterAnimationType.Human && 
                    importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    modified = true;
                    Debug.LogWarning($"[TestSubsceneSetup] Enabled Avatar creation on: {path}");
                }

                if (modified)
                {
                    importer.SaveAndReimport();
                    modelsToReimport.Add(path);
                    needsReimport = true;
                }
            }

            if (needsReimport)
            {
                Debug.Log($"[TestSubsceneSetup] Fixed and reimported {modelsToReimport.Count} FBX files.");
            }
        }

        /// <summary>
        /// Validates prefabs and ensures Animators have proper Avatar references.
        /// Called during content scene creation.
        /// </summary>
        private static bool ValidateAndFixPrefabAnimator(GameObject prefab, out string errorMessage)
        {
            errorMessage = null;
            var animator = prefab.GetComponent<Animator>();
            if (animator == null)
            {
                // Try to find animator in children
                animator = prefab.GetComponentInChildren<Animator>();
            }

            if (animator == null)
            {
                // No animator - might be okay for some prefabs
                return true;
            }

            // Check if Avatar is assigned
            if (animator.avatar == null)
            {
                // Try to find and assign a default avatar
                var defaultAvatar = LoadDefaultAvatar();
                if (defaultAvatar != null)
                {
                    // We can't modify the prefab asset directly here, so warn the user
                    errorMessage = $"Prefab '{prefab.name}' has an Animator without an Avatar assigned. " +
                                   $"Please assign an Avatar (e.g., from Armature.fbx) to the Animator component.";
                    Debug.LogError($"[TestSubsceneSetup] {errorMessage}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Loads the default Avatar from the Armature FBX.
        /// </summary>
        private static Avatar LoadDefaultAvatar()
        {
            var avatars = AssetDatabase.LoadAllAssetsAtPath(DefaultAvatarPath)
                .OfType<Avatar>()
                .ToArray();
            
            if (avatars.Length > 0)
            {
                return avatars[0];
            }

            Debug.LogWarning($"[TestSubsceneSetup] Could not find Avatar in: {DefaultAvatarPath}");
            return null;
        }

        /// <summary>
        /// Menu item to validate all test prefabs have proper Avatar configuration.
        /// </summary>
        [MenuItem("DMotion/Tests/Validate Test Prefabs", false, 113)]
        public static void ValidateTestPrefabs()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SourcePrefabsPath });
            int errorCount = 0;
            int checkedCount = 0;

            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                // Only check animation-related prefabs
                if (prefab.GetComponent<AnimationStateMachineAuthoring>() == null &&
                    prefab.GetComponent<PlayClipAuthoring>() == null &&
                    prefab.GetComponent<PerformanceTestsAuthoring>() == null)
                {
                    continue;
                }

                checkedCount++;
                if (!ValidateAndFixPrefabAnimator(prefab, out string error))
                {
                    errorCount++;
                }
            }

            if (errorCount == 0)
            {
                Debug.Log($"[TestSubsceneSetup] All {checkedCount} test prefabs validated successfully!");
            }
            else
            {
                Debug.LogError($"[TestSubsceneSetup] Found {errorCount} prefab(s) with Avatar issues out of {checkedCount} checked.");
            }
        }

        /// <summary>
        /// Gets the path to the test scene for loading in tests.
        /// </summary>
        public static string GetTestScenePath() => TestScenePath;
    }
}
#endif
