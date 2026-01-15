using System.IO;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// ScriptableObject configuration for automatic Unity Controller Bridge conversions.
    /// Create via Assets > Create > DMotion > Controller Bridge Config.
    /// </summary>
    [CreateAssetMenu(fileName = "ControllerBridgeConfig", menuName = "DMotion/Controller Bridge Config")]
    public class ControllerBridgeConfig : ScriptableObject
    {
        private static ControllerBridgeConfig _instance;

        [Header("Conversion Triggers")]
        [Tooltip("Automatically convert dirty bridges before entering play mode")]
        public bool BakeOnPlayMode = true;

        [Tooltip("Automatically queue conversion after changes (with debounce)")]
        public bool BakeOnDirtyDebounced = true;

        [Tooltip("Debounce duration in seconds after last change before auto-convert")]
        [Range(1f, 60f)]
        public float DebounceDuration = 2f;

        [Header("Conversion Options")]
        [Tooltip("Include animation events from Unity AnimationClips")]
        public bool IncludeAnimationEvents = true;

        [Tooltip("Preserve graph layout (state positions) from Unity Animator window")]
        public bool PreserveGraphLayout = true;

        [Tooltip("Log warnings for unsupported features during conversion")]
        public bool LogWarnings = true;

        [Tooltip("Log detailed conversion info (for debugging)")]
        public bool VerboseLogging = false;

        [Header("Output Settings")]
        [Tooltip("Output folder for generated StateMachineAssets (relative to Assets/)")]
        public string OutputPath = "DMotion/Generated";

        [Tooltip("Naming pattern for output files. {0}=ControllerName")]
        public string NamingPattern = "{0}_Generated";

        [Header("Performance")]
        [Tooltip("Maximum concurrent conversion operations")]
        [Range(1, 4)]
        public int MaxConcurrentConversions = 1;

        /// <summary>
        /// Gets the full output path (Assets/...).
        /// </summary>
        public string FullOutputPath => Path.Combine("Assets", OutputPath);

        /// <summary>
        /// Generates the output filename for a controller.
        /// </summary>
        public string GetOutputFilename(string controllerName)
        {
            return string.Format(NamingPattern, controllerName) + ".asset";
        }

        /// <summary>
        /// Gets the full output path for a controller's generated StateMachineAsset.
        /// </summary>
        public string GetOutputPath(string controllerName)
        {
            return Path.Combine(FullOutputPath, GetOutputFilename(controllerName));
        }

        /// <summary>
        /// Ensures the output directory exists.
        /// </summary>
        public void EnsureOutputDirectory()
        {
            string fullPath = FullOutputPath;
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                AssetDatabase.Refresh();
            }
        }

        private void OnValidate()
        {
            // Sanitize output path
            OutputPath = OutputPath.TrimStart('/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                OutputPath = "DMotion/Generated";
            }

            // Validate naming pattern has placeholder
            if (!NamingPattern.Contains("{0}"))
            {
                Debug.LogWarning("[ControllerBridgeConfig] NamingPattern should contain {0} for controller name", this);
            }

            // Clamp debounce duration
            DebounceDuration = Mathf.Clamp(DebounceDuration, 1f, 60f);
        }

        /// <summary>
        /// Gets or creates the default config instance.
        /// </summary>
        public static ControllerBridgeConfig GetOrCreateDefault()
        {
            // Return cached instance if available
            if (_instance != null) return _instance;

            // Try to find existing config
            var guids = AssetDatabase.FindAssets("t:ControllerBridgeConfig");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _instance = AssetDatabase.LoadAssetAtPath<ControllerBridgeConfig>(path);

                if (_instance != null)
                {
                    if (guids.Length > 1)
                    {
                        Debug.LogWarning(
                            $"[ControllerBridgeConfig] Multiple configs found, using: {path}");
                    }
                    return _instance;
                }
            }

            // Create default config
            _instance = CreateInstance<ControllerBridgeConfig>();

            string configPath = "Assets/DMotion/ControllerBridgeConfig.asset";
            string directory = Path.GetDirectoryName(configPath);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AssetDatabase.CreateAsset(_instance, configPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ControllerBridgeConfig] Created default config at {configPath}");
            return _instance;
        }

        /// <summary>
        /// Gets the singleton instance (read-only, use GetOrCreateDefault to ensure it exists).
        /// </summary>
        public static ControllerBridgeConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GetOrCreateDefault();
                }
                return _instance;
            }
        }

        [MenuItem("DMotion/Open Controller Bridge Config")]
        private static void OpenConfig()
        {
            var config = GetOrCreateDefault();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }
    }
}
