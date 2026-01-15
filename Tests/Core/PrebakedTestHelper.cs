using System.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace DMotion.Tests
{
    /// <summary>
    /// Helper for tests that need pre-baked animation data with valid ACL clips.
    ///
    /// Usage:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor first (creates subscene structure)
    /// 2. Wait for subscene baking to complete
    /// 3. In tests, use this helper to load the pre-baked entities
    /// </summary>
    public static class PrebakedTestHelper
    {
        private const string TestSceneName = "TestAnimationScene";
        private const string TestScenePath = "Assets/DMotion.TestData/TestAnimationScene";
        private const int MaxWaitFrames = 300; // ~5 seconds at 60fps

        /// <summary>
        /// Loads the test scene with pre-baked animation entities.
        /// Call this in test setup for integration tests.
        /// </summary>
        public static IEnumerator LoadTestScene()
        {
#if UNITY_EDITOR
            var scenePath = TestScenePath + ".unity";
            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogError($"[PrebakedTestHelper] Scene not found: {scenePath}. Run 'DMotion/Tests/Setup Test Scene' first.");
                yield break;
            }

            if (Application.isPlaying)
            {
                var loadParams = new LoadSceneParameters(LoadSceneMode.Additive);
                var asyncOp = EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, loadParams);
                if (asyncOp == null)
                {
                    Debug.LogError($"[PrebakedTestHelper] Failed to start loading scene: {scenePath}");
                    yield break;
                }

                while (!asyncOp.isDone)
                {
                    yield return null;
                }
            }
            else
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.isLoaded)
                {
                    Debug.LogError($"[PrebakedTestHelper] Failed to load scene: {scenePath}");
                    yield break;
                }
            }
#else
            var asyncOp = SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Additive);
            if (asyncOp == null)
            {
                Debug.LogError($"[PrebakedTestHelper] Failed to load scene: {TestSceneName}. Add to build settings.");
                yield break;
            }

            while (!asyncOp.isDone)
            {
                yield return null;
            }
#endif

            yield return WaitForSubsceneStreaming();

            Debug.Log("[PrebakedTestHelper] Test scene loaded");
        }

        /// <summary>
        /// Waits for all subscenes to finish streaming/loading.
        /// </summary>
        private static IEnumerator WaitForSubsceneStreaming()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogWarning("[PrebakedTestHelper] No default world - skipping subscene wait");
                yield break;
            }

            int frameCount = 0;
            bool foundEntities = false;

            while (frameCount < MaxWaitFrames && !foundEntities)
            {
                frameCount++;
                yield return null;

                var entities = GetAnimationEntities();
                if (entities.Length > 0)
                {
                    foundEntities = true;
                    Debug.Log($"[PrebakedTestHelper] Found {entities.Length} animation entities after {frameCount} frames");
                }

                if (frameCount % 30 == 0)
                {
                    Debug.Log($"[PrebakedTestHelper] Waiting for subscene streaming... (frame {frameCount})");
                }
            }

            if (!foundEntities)
            {
                Debug.LogWarning($"[PrebakedTestHelper] No animation entities found after {MaxWaitFrames} frames. " +
                               "Subscene may not have baked data - run 'DMotion/Tests/Setup Test Scene' and ensure baking completes.");
            }
        }

        /// <summary>
        /// Unloads the test scene. Uses synchronous operations to avoid
        /// Kinemation race conditions during teardown.
        /// </summary>
        public static IEnumerator UnloadTestScene()
        {
            // Complete all jobs first
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                world.EntityManager.CompleteAllTrackedJobs();
            }

            // Unload scene
            var scene = SceneManager.GetSceneByName(TestSceneName);
            if (scene.isLoaded)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
                {
                    var asyncOp = SceneManager.UnloadSceneAsync(scene);
                    while (asyncOp != null && !asyncOp.isDone)
                    {
                        yield return null;
                    }
                }
                else
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
#else
                var asyncOp = SceneManager.UnloadSceneAsync(scene);
                while (asyncOp != null && !asyncOp.isDone)
                {
                    yield return null;
                }
#endif
            }

            // GC after scene unload
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();

            yield return null;
        }

        /// <summary>
        /// Gets all entities with AnimationStateMachine component.
        /// </summary>
        public static Entity[] GetAnimationEntities()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return System.Array.Empty<Entity>();
            }

            var query = world.EntityManager.CreateEntityQuery(typeof(AnimationStateMachine));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.TempJob);
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
