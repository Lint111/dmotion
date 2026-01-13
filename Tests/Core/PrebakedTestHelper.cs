using System.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMotion.Tests
{
    /// <summary>
    /// Helper for tests that need pre-baked animation data with valid ACL clips.
    ///
    /// Usage:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor first
    /// 2. Open the generated TestAnimationScene.unity to trigger baking
    /// 3. In tests, use this helper to load the pre-baked entities
    /// </summary>
    public static class PrebakedTestHelper
    {
        private const string TestSceneName = "TestAnimationScene";
        private const string TestScenePath = "Assets/DMotion.TestData/TestAnimationScene";

        /// <summary>
        /// Loads the test scene with pre-baked animation entities.
        /// Call this in test setup for integration tests.
        /// </summary>
        public static IEnumerator LoadTestScene()
        {
            // Load the scene
            var asyncOp = SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Additive);
            if (asyncOp == null)
            {
                Debug.LogError($"[PrebakedTestHelper] Failed to load scene: {TestSceneName}");
                yield break;
            }

            while (!asyncOp.isDone)
            {
                yield return null;
            }

            // Wait a few frames for entities to be created
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Debug.Log("[PrebakedTestHelper] Test scene loaded");
        }

        /// <summary>
        /// Unloads the test scene. Call in test teardown.
        /// </summary>
        public static IEnumerator UnloadTestScene()
        {
            var scene = SceneManager.GetSceneByName(TestSceneName);
            if (scene.isLoaded)
            {
                var asyncOp = SceneManager.UnloadSceneAsync(scene);
                while (asyncOp != null && !asyncOp.isDone)
                {
                    yield return null;
                }
            }
        }

        /// <summary>
        /// Gets all entities with AnimationStateMachine component from the default world.
        /// </summary>
        public static Entity[] GetAnimationEntities()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return System.Array.Empty<Entity>();
            }

            var query = world.EntityManager.CreateEntityQuery(typeof(AnimationStateMachine));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            var result = entities.ToArray();
            entities.Dispose();
            return result;
        }

        /// <summary>
        /// Checks if the test scene is set up correctly.
        /// </summary>
        public static bool IsTestSceneSetup()
        {
#if UNITY_EDITOR
            return System.IO.File.Exists(TestScenePath + ".unity");
#else
            return Application.CanStreamedLevelBeLoaded(TestSceneName);
#endif
        }
    }
}
