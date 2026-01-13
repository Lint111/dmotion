using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DMotion.Tests
{
    /// <summary>
    /// Caches clip duration data for use in tests.
    /// Stores metadata extracted from prefabs during editor baking.
    /// </summary>
    [CreateAssetMenu(fileName = "TestBakingCache", menuName = "DMotion/Tests/Test Baking Cache")]
    public class TestBakingCache : ScriptableObject
    {
        [System.Serializable]
        public class CachedClipData
        {
            public string PrefabPath;
            public int ClipCount;
            public float[] ClipDurations;
        }

        [SerializeField]
        private List<CachedClipData> _cachedClips = new List<CachedClipData>();

        private static TestBakingCache _instance;

        public static TestBakingCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<TestBakingCache>("TestBakingCache");
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets cached clip data for the specified prefab path.
        /// </summary>
        public bool TryGetCachedData(string prefabPath, out int clipCount, out float[] durations)
        {
            clipCount = 0;
            durations = null;

            var cached = _cachedClips.Find(c => c.PrefabPath == prefabPath);
            if (cached == null)
            {
                return false;
            }

            clipCount = cached.ClipCount;
            durations = cached.ClipDurations;
            return true;
        }

        /// <summary>
        /// Gets all cached entries.
        /// </summary>
        public IReadOnlyList<CachedClipData> GetAllCachedData() => _cachedClips;

#if UNITY_EDITOR
        /// <summary>
        /// Adds or updates cached data for a prefab.
        /// </summary>
        public void CacheClipData(string prefabPath, int clipCount, float[] durations)
        {
            var existing = _cachedClips.Find(c => c.PrefabPath == prefabPath);
            if (existing != null)
            {
                existing.ClipCount = clipCount;
                existing.ClipDurations = durations;
            }
            else
            {
                _cachedClips.Add(new CachedClipData
                {
                    PrefabPath = prefabPath,
                    ClipCount = clipCount,
                    ClipDurations = durations
                });
            }

            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Clears all cached data.
        /// </summary>
        public void ClearCache()
        {
            _cachedClips.Clear();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
