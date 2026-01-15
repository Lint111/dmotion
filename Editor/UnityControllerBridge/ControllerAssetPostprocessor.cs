using UnityEditor;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Asset postprocessor that monitors for changes to .controller files.
    /// When AnimatorController assets are modified, triggers change detection on all bridges.
    /// </summary>
    public class ControllerAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool controllerChanged = false;

            // Check imported assets for .controller files
            foreach (var assetPath in importedAssets)
            {
                if (assetPath.EndsWith(".controller"))
                {
                    controllerChanged = true;
                    break;
                }
            }

            // Check deleted assets
            if (!controllerChanged)
            {
                foreach (var assetPath in deletedAssets)
                {
                    if (assetPath.EndsWith(".controller"))
                    {
                        controllerChanged = true;
                        break;
                    }
                }
            }

            // Check moved assets
            if (!controllerChanged)
            {
                foreach (var assetPath in movedAssets)
                {
                    if (assetPath.EndsWith(".controller"))
                    {
                        controllerChanged = true;
                        break;
                    }
                }
            }

            // If any controller changed, trigger check on all bridges
            if (controllerChanged)
            {
                ControllerBridgeDirtyTracker.ForceCheckAllBridges();
            }
        }
    }
}
