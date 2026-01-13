#if UNITY_EDITOR
using System.IO;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMotion.Tests
{
    /// <summary>
    /// Sets up test scenes with animation prefabs.
    /// The scene can be used as a subscene for pre-baked entity tests.
    /// </summary>
    public static class TestSubsceneSetup
    {
        private const string TestDataFolder = "Assets/DMotion.TestData";
        private const string TestScenePath = "Assets/DMotion.TestData/TestAnimationScene.unity";
        private const string SourcePrefabsPath = "Packages/com.gamedevpro.dmotion/Tests/Data";

        [MenuItem("DMotion/Tests/Setup Test Scene", false, 110)]
        public static void SetupTestScene()
        {
            // Ensure folder exists
            EnsureTestDataFolder();

            // Create or open the scene
            Scene scene;
            if (File.Exists(TestScenePath))
            {
                scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
                Debug.Log($"[TestSubsceneSetup] Opened existing scene: {TestScenePath}");
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log($"[TestSubsceneSetup] Creating new scene: {TestScenePath}");
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
                    prefab.GetComponent<PlayClipAuthoring>() == null)
                {
                    continue;
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
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = existingName;
                addedCount++;

                Debug.Log($"[TestSubsceneSetup] Added {prefab.name} to test scene");
            }

            // Save the scene
            EditorSceneManager.SaveScene(scene, TestScenePath);
            Debug.Log($"[TestSubsceneSetup] Saved scene with {addedCount} new prefabs at {TestScenePath}");
            Debug.Log("[TestSubsceneSetup] To use as subscene: Create a new scene, add SubScene component, reference this scene.");
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
        /// Gets the path to the test scene for loading in tests.
        /// </summary>
        public static string GetTestScenePath() => TestScenePath;
    }
}
#endif
