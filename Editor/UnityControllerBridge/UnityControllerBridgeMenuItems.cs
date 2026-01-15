using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Unity Editor menu items for Unity Controller Bridge operations.
    /// </summary>
    public static class UnityControllerBridgeMenuItems
    {
        private const string MenuPath = "Assets/DMotion/Unity Controller Bridge/";

        /// <summary>
        /// Creates a Unity Controller Bridge for the selected AnimatorController(s).
        /// </summary>
        [MenuItem(MenuPath + "Create Bridge", false, 1)]
        private static void CreateBridgeForSelectedControllers()
        {
            var selectedControllers = Selection.GetFiltered<AnimatorController>(SelectionMode.Assets);

            if (selectedControllers.Length == 0)
            {
                Debug.LogWarning("[Unity Controller Bridge] No AnimatorController selected. Please select one or more .controller assets.");
                return;
            }

            foreach (var controller in selectedControllers)
            {
                CreateBridgeForController(controller);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Unity Controller Bridge] Created {selectedControllers.Length} bridge(s)");
        }

        [MenuItem(MenuPath + "Create Bridge", true)]
        private static bool ValidateCreateBridge()
        {
            // Only show menu item if at least one AnimatorController is selected
            return Selection.GetFiltered<AnimatorController>(SelectionMode.Assets).Length > 0;
        }

        /// <summary>
        /// Converts the selected AnimatorController(s) immediately.
        /// </summary>
        [MenuItem(MenuPath + "Convert Now", false, 2)]
        private static void ConvertSelectedControllers()
        {
            var selectedControllers = Selection.GetFiltered<AnimatorController>(SelectionMode.Assets);

            if (selectedControllers.Length == 0)
            {
                Debug.LogWarning("[Unity Controller Bridge] No AnimatorController selected.");
                return;
            }

            foreach (var controller in selectedControllers)
            {
                var bridge = ControllerBridgeRegistry.GetOrCreateBridge(controller);
                ControllerConversionQueue.Enqueue(bridge);
            }

            ControllerConversionQueue.StartProcessing();

            Debug.Log($"[Unity Controller Bridge] Enqueued {selectedControllers.Length} controller(s) for conversion");
        }

        [MenuItem(MenuPath + "Convert Now", true)]
        private static bool ValidateConvertNow()
        {
            return Selection.GetFiltered<AnimatorController>(SelectionMode.Assets).Length > 0;
        }

        /// <summary>
        /// Opens the Controller Bridge Manager window.
        /// </summary>
        [MenuItem(MenuPath + "Open Bridge Manager", false, 20)]
        private static void OpenBridgeManager()
        {
            ControllerBridgeManagerWindow.ShowWindow();
        }

        /// <summary>
        /// Forces a check of all registered bridges for changes.
        /// </summary>
        [MenuItem(MenuPath + "Force Check All Bridges", false, 21)]
        private static void ForceCheckAllBridges()
        {
            ControllerBridgeDirtyTracker.ForceCheckAllBridges();
            Debug.Log("[Unity Controller Bridge] Force check completed");
        }

        private static void CreateBridgeForController(AnimatorController controller)
        {
            // Check if bridge already exists
            var existingBridge = ControllerBridgeRegistry.GetBridge(controller);
            if (existingBridge != null)
            {
                Debug.Log($"[Unity Controller Bridge] Bridge already exists for '{controller.name}' at {AssetDatabase.GetAssetPath(existingBridge)}");
                Selection.activeObject = existingBridge;
                return;
            }

            // Create bridge via registry (automatically saves and tracks it)
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(controller);

            Debug.Log($"[Unity Controller Bridge] Created bridge for '{controller.name}' at {AssetDatabase.GetAssetPath(bridge)}");

            // Select the bridge in the project window
            Selection.activeObject = bridge;
            EditorGUIUtility.PingObject(bridge);

            // Optionally enqueue for immediate conversion
            if (EditorUtility.DisplayDialog(
                "Convert Now?",
                $"Bridge created for '{controller.name}'.\n\nWould you like to convert it now?",
                "Convert Now",
                "Convert Later"))
            {
                ControllerConversionQueue.Enqueue(bridge);
                ControllerConversionQueue.StartProcessing();
            }
        }
    }
}
