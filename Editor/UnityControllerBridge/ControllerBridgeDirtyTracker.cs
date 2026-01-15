using System;
using System.Collections.Generic;
using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Editor singleton that monitors bridge changes and tracks which bridges need reconversion.
    /// Implements debounced change detection to prevent thrashing during rapid edits.
    /// </summary>
    [InitializeOnLoad]
    public static class ControllerBridgeDirtyTracker
    {
        private static readonly Dictionary<string, BridgeTrackingData> _trackedBridges = new();
        private static readonly HashSet<string> _pendingConversionQueue = new();
        private static double _lastChangeTime;
        private static bool _isDebouncing;
        private static ControllerBridgeConfig _config;
        private static bool _isInitialized;

        /// <summary>
        /// Event fired when a bridge is queued for conversion (after debounce).
        /// </summary>
        public static event Action<string> OnBridgeQueuedForConversion;

        /// <summary>
        /// Event fired when debounce timer starts.
        /// </summary>
        public static event Action OnDebounceStarted;

        /// <summary>
        /// Event fired when debounce timer completes and conversion queue is processed.
        /// </summary>
        public static event Action OnDebounceCompleted;

        static ControllerBridgeDirtyTracker()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_isInitialized) return;

            // Subscribe to Unity events
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;

            // Subscribe to bridge events
            UnityControllerBridgeAsset.OnBridgeRegistered += OnBridgeRegistered;
            UnityControllerBridgeAsset.OnBridgeUnregistered += OnBridgeUnregistered;
            UnityControllerBridgeAsset.OnBridgeDirty += OnBridgeMarkedDirty;

            _isInitialized = true;
            Debug.Log("[ControllerBridgeDirtyTracker] Initialized");
        }

        /// <summary>
        /// Gets the current config.
        /// </summary>
        public static ControllerBridgeConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = ControllerBridgeConfig.GetOrCreateDefault();
                }
                return _config;
            }
        }

        /// <summary>
        /// Gets all tracked bridges.
        /// </summary>
        public static IReadOnlyDictionary<string, BridgeTrackingData> TrackedBridges => _trackedBridges;

        /// <summary>
        /// Gets all bridges pending conversion.
        /// </summary>
        public static IReadOnlyCollection<string> PendingConversionQueue => _pendingConversionQueue;

        /// <summary>
        /// Whether a debounce is currently active.
        /// </summary>
        public static bool IsDebouncing => _isDebouncing;

        /// <summary>
        /// Time remaining on debounce timer (in seconds).
        /// </summary>
        public static float DebounceTimeRemaining
        {
            get
            {
                if (!_isDebouncing) return 0f;
                float elapsed = (float)(EditorApplication.timeSinceStartup - _lastChangeTime);
                return Mathf.Max(0f, Config.DebounceDuration - elapsed);
            }
        }

        /// <summary>
        /// Gets all dirty bridges.
        /// </summary>
        public static List<UnityControllerBridgeAsset> GetDirtyBridges()
        {
            var result = new List<UnityControllerBridgeAsset>();
            foreach (var kvp in _trackedBridges)
            {
                if (kvp.Value.Bridge != null && kvp.Value.Bridge.IsDirty)
                {
                    result.Add(kvp.Value.Bridge);
                }
            }
            return result;
        }

        /// <summary>
        /// Forces an immediate check for changes on all bridges.
        /// </summary>
        public static void ForceCheckAllBridges()
        {
            foreach (var kvp in _trackedBridges)
            {
                if (kvp.Value.Bridge != null)
                {
                    kvp.Value.Bridge.CheckForChanges();
                }
            }
        }

        /// <summary>
        /// Clears the pending conversion queue.
        /// </summary>
        public static void ClearPendingQueue()
        {
            _pendingConversionQueue.Clear();
            _isDebouncing = false;
        }

        /// <summary>
        /// Forces all dirty bridges to be queued for immediate conversion.
        /// </summary>
        public static void QueueAllDirtyBridgesNow()
        {
            foreach (var bridge in GetDirtyBridges())
            {
                QueueBridgeForConversion(bridge.BridgeId, immediate: true);
            }
        }

        private static void OnBridgeRegistered(UnityControllerBridgeAsset bridge)
        {
            if (bridge == null) return;

            string id = bridge.BridgeId;
            if (!_trackedBridges.ContainsKey(id))
            {
                _trackedBridges[id] = new BridgeTrackingData
                {
                    Bridge = bridge,
                    LastModifiedTime = EditorApplication.timeSinceStartup
                };
                Debug.Log($"[ControllerBridgeDirtyTracker] Registered bridge: {id}");
            }
        }

        private static void OnBridgeUnregistered(UnityControllerBridgeAsset bridge)
        {
            if (bridge == null) return;

            string id = bridge.BridgeId;
            if (_trackedBridges.Remove(id))
            {
                _pendingConversionQueue.Remove(id);
                Debug.Log($"[ControllerBridgeDirtyTracker] Unregistered bridge: {id}");
            }
        }

        private static void OnBridgeMarkedDirty(UnityControllerBridgeAsset bridge)
        {
            if (bridge == null) return;

            string id = bridge.BridgeId;
            if (_trackedBridges.TryGetValue(id, out var data))
            {
                data.LastModifiedTime = EditorApplication.timeSinceStartup;
            }

            // Start debounce timer
            StartDebounce(id);
        }

        private static void OnHierarchyChanged()
        {
            // Check all tracked bridges for changes
            // This catches changes made through inspector, etc.
            foreach (var kvp in _trackedBridges)
            {
                // This will fire OnBridgeDirty if changes detected
                kvp.Value.Bridge?.CheckForChanges();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_isDebouncing || _pendingConversionQueue.Count == 0) return;

            // Check if debounce time has elapsed
            float elapsed = (float)(EditorApplication.timeSinceStartup - _lastChangeTime);
            if (elapsed >= Config.DebounceDuration)
            {
                ProcessDebouncedQueue();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // Block play mode entry if we have dirty bridges and config says to convert first
                if (Config.BakeOnPlayMode)
                {
                    var dirtyBridges = GetDirtyBridges();
                    if (dirtyBridges.Count > 0)
                    {
                        // Cancel the pending play mode entry
                        EditorApplication.isPlaying = false;

                        Debug.Log($"[ControllerBridgeDirtyTracker] Blocking play mode entry - {dirtyBridges.Count} dirty bridge(s) need conversion");

                        // Queue all dirty bridges for immediate conversion
                        foreach (var bridge in dirtyBridges)
                        {
                            QueueBridgeForConversion(bridge.BridgeId, immediate: true);
                        }

                        // Signal that we're about to convert
                        ControllerConversionQueue.OnConversionComplete += ResumePlayMode;
                        ControllerConversionQueue.StartProcessing();
                    }
                }
            }
        }

        private static void ResumePlayMode()
        {
            ControllerConversionQueue.OnConversionComplete -= ResumePlayMode;

            // Check if all bridges are now clean
            var stillDirty = GetDirtyBridges();
            if (stillDirty.Count == 0)
            {
                Debug.Log("[ControllerBridgeDirtyTracker] All bridges converted, entering play mode");
                EditorApplication.isPlaying = true;
            }
            else
            {
                Debug.LogWarning($"[ControllerBridgeDirtyTracker] {stillDirty.Count} bridge(s) still dirty after conversion. Play mode entry cancelled.");
            }
        }

        private static void StartDebounce(string bridgeId)
        {
            _pendingConversionQueue.Add(bridgeId);
            _lastChangeTime = EditorApplication.timeSinceStartup;

            if (!_isDebouncing)
            {
                _isDebouncing = true;
                OnDebounceStarted?.Invoke();
                Debug.Log($"[ControllerBridgeDirtyTracker] Debounce started ({Config.DebounceDuration}s)");
            }
        }

        private static void ProcessDebouncedQueue()
        {
            _isDebouncing = false;
            OnDebounceCompleted?.Invoke();

            if (_pendingConversionQueue.Count == 0) return;

            Debug.Log($"[ControllerBridgeDirtyTracker] Debounce complete, queueing {_pendingConversionQueue.Count} bridge(s) for conversion");

            foreach (var bridgeId in _pendingConversionQueue)
            {
                OnBridgeQueuedForConversion?.Invoke(bridgeId);

                // Add to ControllerConversionQueue
                if (_trackedBridges.TryGetValue(bridgeId, out var data) && data.Bridge != null)
                {
                    ControllerConversionQueue.Enqueue(data.Bridge);
                }
            }

            _pendingConversionQueue.Clear();

            // Auto-start processing if configured
            if (Config.BakeOnDirtyDebounced)
            {
                ControllerConversionQueue.StartProcessing();
            }
        }

        private static void QueueBridgeForConversion(string bridgeId, bool immediate = false)
        {
            if (!_trackedBridges.TryGetValue(bridgeId, out var data) || data.Bridge == null)
            {
                Debug.LogWarning($"[ControllerBridgeDirtyTracker] Cannot queue unknown bridge: {bridgeId}");
                return;
            }

            if (immediate)
            {
                _pendingConversionQueue.Remove(bridgeId);
                ControllerConversionQueue.Enqueue(data.Bridge);
                OnBridgeQueuedForConversion?.Invoke(bridgeId);
            }
            else
            {
                StartDebounce(bridgeId);
            }
        }

        /// <summary>
        /// Tracking data for a single bridge.
        /// </summary>
        public class BridgeTrackingData
        {
            public UnityControllerBridgeAsset Bridge;
            public double LastModifiedTime;
        }
    }
}
