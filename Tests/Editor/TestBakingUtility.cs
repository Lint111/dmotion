#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Editor utility for caching test prefab clip data.
    /// Extracts clip durations from prefabs for use in tests.
    /// </summary>
    public static class TestBakingUtility
    {
        private const string TestPrefabsPath = "Packages/com.gamedevpro.dmotion/Tests/Data";
        private const string CacheAssetPath = "Assets/DMotion.TestData/Resources/TestBakingCache.asset";

        [MenuItem("DMotion/Tests/Cache Test Clip Data", false, 100)]
        public static void CacheTestClipData()
        {
            var cache = GetOrCreateCache();
            if (cache == null)
            {
                Debug.LogError("[TestBakingUtility] Failed to get or create TestBakingCache");
                return;
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { TestPrefabsPath });
            var prefabPaths = prefabGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".prefab"))
                .ToList();

            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning($"[TestBakingUtility] No prefabs found in {TestPrefabsPath}");
                return;
            }

            Debug.Log($"[TestBakingUtility] Found {prefabPaths.Count} prefabs to analyze");

            int cachedCount = 0;
            foreach (var prefabPath in prefabPaths)
            {
                try
                {
                    if (CachePrefabClipData(prefabPath, cache))
                    {
                        cachedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TestBakingUtility] Error caching {prefabPath}: {ex.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TestBakingUtility] Cached clip data for {cachedCount} prefabs");
        }

        [MenuItem("DMotion/Tests/Clear Cache", false, 101)]
        public static void ClearCache()
        {
            var cache = GetOrCreateCache();
            if (cache != null)
            {
                cache.ClearCache();
                AssetDatabase.SaveAssets();
                Debug.Log("[TestBakingUtility] Cache cleared");
            }
        }

        private static TestBakingCache GetOrCreateCache()
        {
            var cache = AssetDatabase.LoadAssetAtPath<TestBakingCache>(CacheAssetPath);
            if (cache != null)
            {
                return cache;
            }

            // Try Resources folder
            cache = Resources.Load<TestBakingCache>("TestBakingCache");
            if (cache != null)
            {
                return cache;
            }

            // Create new cache in Assets folder
            Debug.Log("[TestBakingUtility] Creating new TestBakingCache asset");

            // Ensure directory structure exists
            var directory = System.IO.Path.GetDirectoryName(CacheAssetPath);
            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                // Create directories recursively
                var parts = directory.Replace('\\', '/').Split('/');
                var currentPath = parts[0]; // "Assets"
                for (int i = 1; i < parts.Length; i++)
                {
                    var nextPath = currentPath + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(nextPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    }
                    currentPath = nextPath;
                }
            }

            cache = ScriptableObject.CreateInstance<TestBakingCache>();
            AssetDatabase.CreateAsset(cache, CacheAssetPath);
            AssetDatabase.SaveAssets();

            return cache;
        }

        private static bool CachePrefabClipData(string prefabPath, TestBakingCache cache)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[TestBakingUtility] Could not load prefab at {prefabPath}");
                return false;
            }

            // Check if prefab has animation state machine component
            var authoring = prefab.GetComponent<AnimationStateMachineAuthoring>();
            if (authoring == null)
            {
                // Not an animation prefab, skip
                return false;
            }

            // Extract clip information from authoring
            var clipAssets = authoring.StateMachineAsset?.Clips?.ToArray();
            if (clipAssets == null || clipAssets.Length == 0)
            {
                Debug.LogWarning($"[TestBakingUtility] No clips in {prefabPath}");
                return false;
            }

            // Get durations from Unity AnimationClips
            var durations = new List<float>();
            foreach (var clipAsset in clipAssets)
            {
                if (clipAsset?.Clip != null)
                {
                    durations.Add(clipAsset.Clip.length);
                }
                else
                {
                    durations.Add(1.0f); // Default duration
                }
            }

            Debug.Log($"[TestBakingUtility] Caching {prefabPath}: {durations.Count} clips");
            cache.CacheClipData(prefabPath, durations.Count, durations.ToArray());

            return true;
        }
    }
}
#endif
