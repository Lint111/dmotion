using System;
using System.Reflection;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// ScriptableObject containing test resources for DMotion unit tests.
    /// Provides pre-configured animation data that can be used to create blob assets
    /// without requiring SmartBlobber baking.
    ///
    /// Only active when UNITY_INCLUDE_TESTS is defined.
    /// </summary>
    [CreateAssetMenu(fileName = "TestResources", menuName = "DMotion/Tests/Test Resources")]
    public class TestResources : ScriptableObject
    {
#if UNITY_INCLUDE_TESTS
        [Header("Test Animation Clips")]
        [Tooltip("Animation clips to use for testing. Duration and sample rate will be extracted.")]
        public AnimationClip[] TestClips;

        [Header("Default Test Values")]
        [Tooltip("Default clip duration if no clips are assigned")]
        public float DefaultClipDuration = 1.0f;

        [Tooltip("Default sample rate if no clips are assigned")]
        public float DefaultSampleRate = 30.0f;

        [Tooltip("Default bone count for test skeletons")]
        public int DefaultBoneCount = 1;

        // Cached field info for SkeletonClip fields
        private static FieldInfo _durationField;
        private static FieldInfo _sampleRateField;
        private static bool _fieldsInitialized;

        // Singleton instance
        private static TestResources _instance;

        /// <summary>
        /// Gets the singleton TestResources instance from Resources folder.
        /// Returns null in non-test builds.
        /// </summary>
        public static TestResources Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<TestResources>("TestResources");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[TestResources] No TestResources asset found in Resources folder. " +
                                        "Create one via Assets > Create > DMotion > Tests > Test Resources");
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Creates a SkeletonClipSetBlob with real duration/sampleRate values.
        /// The blob will have valid timing data but no actual ACL animation data.
        /// Suitable for tests that check timing, looping, and state machine logic.
        /// </summary>
        /// <param name="clipCount">Number of clips to include</param>
        /// <returns>A blob asset reference that must be disposed by the caller</returns>
        public BlobAssetReference<SkeletonClipSetBlob> CreateTestClipsBlob(int clipCount = -1)
        {
            if (clipCount < 0)
                clipCount = TestClips != null && TestClips.Length > 0 ? TestClips.Length : 1;

            var durations = new float[clipCount];
            var sampleRates = new float[clipCount];

            for (int i = 0; i < clipCount; i++)
            {
                if (TestClips != null && i < TestClips.Length && TestClips[i] != null)
                {
                    durations[i] = TestClips[i].length;
                    sampleRates[i] = TestClips[i].frameRate;
                }
                else
                {
                    durations[i] = DefaultClipDuration;
                    sampleRates[i] = DefaultSampleRate;
                }
            }

            return CreateTestClipsBlobInternal(durations, sampleRates);
        }

        /// <summary>
        /// Creates a SkeletonClipSetBlob with specified durations.
        /// </summary>
        public BlobAssetReference<SkeletonClipSetBlob> CreateTestClipsBlob(float[] durations)
        {
            var sampleRates = new float[durations.Length];
            for (int i = 0; i < durations.Length; i++)
                sampleRates[i] = DefaultSampleRate;

            return CreateTestClipsBlobInternal(durations, sampleRates);
        }

        private BlobAssetReference<SkeletonClipSetBlob> CreateTestClipsBlobInternal(float[] durations, float[] sampleRates)
        {
            InitializeFieldInfo();

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SkeletonClipSetBlob>();
            root.boneCount = (short)DefaultBoneCount;

            var blobClips = builder.Allocate(ref root.clips, durations.Length);

            for (int i = 0; i < durations.Length; i++)
            {
                WriteSkeletonClipData(ref blobClips[i], durations[i], sampleRates[i]);
            }

            return builder.CreateBlobAssetReference<SkeletonClipSetBlob>(Allocator.Persistent);
        }

        private static void InitializeFieldInfo()
        {
            if (_fieldsInitialized) return;
            _fieldsInitialized = true;

            var clipType = typeof(SkeletonClip);
            var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // Try common field name patterns
            var durationNames = new[] { "duration", "_duration", "m_duration", "Duration" };
            var sampleRateNames = new[] { "sampleRate", "_sampleRate", "m_sampleRate", "SampleRate" };

            foreach (var name in durationNames)
            {
                _durationField = clipType.GetField(name, bindingFlags);
                if (_durationField != null) break;
            }

            foreach (var name in sampleRateNames)
            {
                _sampleRateField = clipType.GetField(name, bindingFlags);
                if (_sampleRateField != null) break;
            }

            // Log available fields for debugging if not found
            if (_durationField == null || _sampleRateField == null)
            {
                var fields = clipType.GetFields(bindingFlags);
                Debug.Log($"[TestResources] SkeletonClip fields: {string.Join(", ", Array.ConvertAll(fields, f => $"{f.Name}:{f.FieldType.Name}"))}");
            }
        }

        /// <summary>
        /// Writes duration and sampleRate to a SkeletonClip using reflection or unsafe code.
        /// </summary>
        private static unsafe void WriteSkeletonClipData(ref SkeletonClip clip, float duration, float sampleRate)
        {
            // First, zero out the entire struct to ensure clean state
            var clipPtr = (byte*)UnsafeUtility.AddressOf(ref clip);
            UnsafeUtility.MemClear(clipPtr, UnsafeUtility.SizeOf<SkeletonClip>());

            // Try reflection approach first (works if fields are accessible)
            if (_durationField != null && _sampleRateField != null)
            {
                // Box the struct, set fields, unbox back
                object boxed = clip;
                _durationField.SetValue(boxed, duration);
                _sampleRateField.SetValue(boxed, sampleRate);
                clip = (SkeletonClip)boxed;
            }
            else
            {
                // Fallback: try direct memory write for common layouts
                // SkeletonClip typically has duration and sampleRate as first fields
                // This is a best-effort approach for when reflection fails
                var floatPtr = (float*)clipPtr;

                // Common layouts put duration first, then sampleRate
                // Offset 0: duration (float)
                // Offset 4: sampleRate (float)
                floatPtr[0] = duration;
                floatPtr[1] = sampleRate;

                Debug.LogWarning($"[TestResources] Using fallback memory write for SkeletonClip. " +
                               $"Duration={duration}, SampleRate={sampleRate}. Verify test results.");
            }
        }

        /// <summary>
        /// Resets the singleton instance. Call this in test teardown if needed.
        /// </summary>
        public static void ResetInstance()
        {
            _instance = null;
        }

        /// <summary>
        /// Static method to create test clips blob without requiring an asset instance.
        /// This is a fallback when TestResources.asset doesn't exist.
        /// </summary>
        public static BlobAssetReference<SkeletonClipSetBlob> CreateTestClipsBlobStatic(int clipCount, float clipDuration, float sampleRate = 30.0f)
        {
            InitializeFieldInfo();

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SkeletonClipSetBlob>();
            root.boneCount = 1;

            var blobClips = builder.Allocate(ref root.clips, clipCount);

            for (int i = 0; i < clipCount; i++)
            {
                WriteSkeletonClipData(ref blobClips[i], clipDuration, sampleRate);
            }

            return builder.CreateBlobAssetReference<SkeletonClipSetBlob>(Allocator.Persistent);
        }
#endif
    }
}
