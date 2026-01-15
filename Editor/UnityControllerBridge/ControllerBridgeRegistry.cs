using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Central registry mapping AnimatorControllers to bridge assets.
    /// Ensures only one bridge per controller.
    /// Caches bridge references for fast lookup.
    /// </summary>
    [InitializeOnLoad]
    public static class ControllerBridgeRegistry
    {
        private static Dictionary<AnimatorController, UnityControllerBridgeAsset> _controllerToBridge;
        private static Dictionary<string, UnityControllerBridgeAsset> _guidToBridge;
        private static bool _isInitialized;

        static ControllerBridgeRegistry()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_isInitialized) return;

            _controllerToBridge = new Dictionary<AnimatorController, UnityControllerBridgeAsset>();
            _guidToBridge = new Dictionary<string, UnityControllerBridgeAsset>();

            // Subscribe to Unity events
            EditorApplication.projectChanged += RebuildCache;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Subscribe to bridge events
            UnityControllerBridgeAsset.OnBridgeRegistered += OnBridgeRegistered;
            UnityControllerBridgeAsset.OnBridgeUnregistered += OnBridgeUnregistered;

            RebuildCache();

            _isInitialized = true;
            Debug.Log($"[ControllerBridgeRegistry] Initialized with {_controllerToBridge.Count} bridge(s)");
        }

        #region Public API

        /// <summary>
        /// Gets the bridge asset for a controller. Returns null if no bridge exists.
        /// </summary>
        public static UnityControllerBridgeAsset GetBridge(AnimatorController controller)
        {
            if (controller == null) return null;

            EnsureInitialized();

            _controllerToBridge.TryGetValue(controller, out var bridge);
            return bridge;
        }

        /// <summary>
        /// Gets the bridge asset for a controller, or creates one if it doesn't exist.
        /// </summary>
        public static UnityControllerBridgeAsset GetOrCreateBridge(AnimatorController controller)
        {
            if (controller == null) return null;

            EnsureInitialized();

            if (_controllerToBridge.TryGetValue(controller, out var bridge))
            {
                return bridge;
            }

            // No bridge exists - create one
            return CreateBridgeForController(controller);
        }

        /// <summary>
        /// Registers a bridge asset with the registry.
        /// </summary>
        public static void Register(UnityControllerBridgeAsset bridge)
        {
            if (bridge == null || bridge.SourceController == null) return;

            EnsureInitialized();

            // Check for duplicates
            if (_controllerToBridge.TryGetValue(bridge.SourceController, out var existing))
            {
                if (existing != bridge)
                {
                    Debug.LogWarning(
                        $"[ControllerBridgeRegistry] Multiple bridges found for controller '{bridge.SourceController.name}'. " +
                        $"Using '{AssetDatabase.GetAssetPath(existing)}', ignoring '{AssetDatabase.GetAssetPath(bridge)}'");
                }
                return;
            }

            _controllerToBridge[bridge.SourceController] = bridge;
            _guidToBridge[bridge.BridgeId] = bridge;
        }

        /// <summary>
        /// Unregisters a bridge asset from the registry.
        /// </summary>
        public static void Unregister(UnityControllerBridgeAsset bridge)
        {
            if (bridge == null) return;

            EnsureInitialized();

            if (bridge.SourceController != null)
            {
                _controllerToBridge.Remove(bridge.SourceController);
            }

            _guidToBridge.Remove(bridge.BridgeId);
        }

        /// <summary>
        /// Gets all registered bridges.
        /// </summary>
        public static List<UnityControllerBridgeAsset> GetAllBridges()
        {
            EnsureInitialized();
            return _controllerToBridge.Values.ToList();
        }

        /// <summary>
        /// Finds all AnimationStateMachineAuthoring components in the scene that reference a specific controller.
        /// </summary>
        public static List<Authoring.AnimationStateMachineAuthoring> FindEntitiesUsingController(AnimatorController controller)
        {
            if (controller == null) return new List<Authoring.AnimationStateMachineAuthoring>();

            var bridge = GetBridge(controller);
            if (bridge == null || bridge.GeneratedStateMachine == null)
            {
                return new List<Authoring.AnimationStateMachineAuthoring>();
            }

            // Find all authoring components that reference the generated StateMachineAsset
            var allAuthoring = Object.FindObjectsOfType<Authoring.AnimationStateMachineAuthoring>();
            return allAuthoring
                .Where(auth => auth.StateMachineAsset == bridge.GeneratedStateMachine)
                .ToList();
        }

        /// <summary>
        /// Gets the number of entities using a specific bridge.
        /// </summary>
        public static int GetReferenceCount(UnityControllerBridgeAsset bridge)
        {
            if (bridge?.SourceController == null) return 0;
            return FindEntitiesUsingController(bridge.SourceController).Count;
        }

        /// <summary>
        /// Forces all bridges to check for changes.
        /// </summary>
        public static void ForceCheckAllBridges()
        {
            EnsureInitialized();

            foreach (var bridge in _controllerToBridge.Values)
            {
                if (bridge != null)
                {
                    bridge.CheckForChanges();
                }
            }
        }

        #endregion

        #region Private Methods

        private static void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Rebuilds the cache by scanning the project for all bridge assets.
        /// </summary>
        private static void RebuildCache()
        {
            if (!_isInitialized) return;

            _controllerToBridge.Clear();
            _guidToBridge.Clear();

            // Find all bridge assets in the project
            var guids = AssetDatabase.FindAssets("t:UnityControllerBridgeAsset");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var bridge = AssetDatabase.LoadAssetAtPath<UnityControllerBridgeAsset>(path);

                if (bridge != null && bridge.SourceController != null)
                {
                    // Check for duplicates
                    if (_controllerToBridge.ContainsKey(bridge.SourceController))
                    {
                        var existing = _controllerToBridge[bridge.SourceController];
                        Debug.LogWarning(
                            $"[ControllerBridgeRegistry] Multiple bridges found for controller '{bridge.SourceController.name}'. " +
                            $"Using '{AssetDatabase.GetAssetPath(existing)}', ignoring '{path}'");
                        continue;
                    }

                    _controllerToBridge[bridge.SourceController] = bridge;
                    _guidToBridge[bridge.BridgeId] = bridge;
                }
            }

            Debug.Log($"[ControllerBridgeRegistry] Rebuilt cache with {_controllerToBridge.Count} bridge(s)");
        }

        /// <summary>
        /// Creates a new bridge asset for a controller.
        /// </summary>
        private static UnityControllerBridgeAsset CreateBridgeForController(AnimatorController controller)
        {
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            string controllerDir = Path.GetDirectoryName(controllerPath);
            string controllerName = Path.GetFileNameWithoutExtension(controllerPath);

            // Ensure directory exists
            if (!Directory.Exists(controllerDir))
            {
                Debug.LogError($"[ControllerBridgeRegistry] Directory not found: {controllerDir}");
                return null;
            }

            // Generate unique bridge path
            string bridgePath = Path.Combine(controllerDir, $"{controllerName}_Bridge.asset");
            bridgePath = AssetDatabase.GenerateUniqueAssetPath(bridgePath);

            // Create bridge asset
            var bridge = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();

            // Use reflection to set the private field
            var sourceControllerField = typeof(UnityControllerBridgeAsset)
                .GetField("_sourceController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            sourceControllerField?.SetValue(bridge, controller);

            // Create asset
            AssetDatabase.CreateAsset(bridge, bridgePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ControllerBridgeRegistry] Created bridge for '{controllerName}' at {bridgePath}");

            // Register the new bridge
            Register(bridge);

            // Mark as dirty (needs initial conversion)
            bridge.MarkDirty();

            return bridge;
        }

        #endregion

        #region Event Handlers

        private static void OnBridgeRegistered(UnityControllerBridgeAsset bridge)
        {
            Register(bridge);
        }

        private static void OnBridgeUnregistered(UnityControllerBridgeAsset bridge)
        {
            Unregister(bridge);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Additional logic can be added here if needed
            // For now, the actual blocking happens in ControllerBridgeDirtyTracker
        }

        #endregion
    }
}
