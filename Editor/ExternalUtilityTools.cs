#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DMotion.Editor
{
    /// <summary>
    /// Unity Editor glue for external utility tools from UtilityPackage.
    /// Provides menu items under Tools/Util/... to run standalone tools.
    /// 
    /// Features:
    /// - Auto-cleanup: All watchers are killed when Unity closes
    /// - Auto-start: Optionally start watchers on Unity launch
    /// - Custom watch directories with folder picker UI
    /// </summary>
    [InitializeOnLoad]
    public static class ExternalUtilityTools
    {
        // Relative path from Unity project to UtilityPackage (assumes sibling folders in C:\GitHub)
        private const string DefaultUtilityPackagePath = "../UtilityPackage";
        
        // EditorPrefs keys
        private const string UtilityPackagePathPref = "DMotion_UtilityPackagePath";
        private const string NulWatcherAutoStartPref = "DMotion_NulWatcher_AutoStart";
        private const string NulWatcherWatchDirPref = "DMotion_NulWatcher_WatchDir";
        
        // Track spawned processes for cleanup
        private static readonly List<int> spawnedProcessIds = new();

        static ExternalUtilityTools()
        {
            // Register cleanup on editor quit
            EditorApplication.quitting += OnEditorQuitting;
            
            // Auto-start watcher if enabled (delayed to ensure editor is ready)
            EditorApplication.delayCall += OnEditorStartup;
        }

        private static void OnEditorStartup()
        {
            if (NulWatcherAutoStart)
            {
                Debug.Log("[ExternalUtilityTools] Auto-starting NulFileWatcher...");
                NulFileWatcher_StartWatcher();
            }
        }

        private static void OnEditorQuitting()
        {
            Debug.Log("[ExternalUtilityTools] Editor quitting - cleaning up watchers...");
            NulFileWatcher_StopAll();
        }

        private static string UtilityPackagePath
        {
            get
            {
                var custom = EditorPrefs.GetString(UtilityPackagePathPref, "");
                if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
                    return custom;
                
                // Default: sibling folder to Unity project
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.GetFullPath(Path.Combine(projectRoot!, DefaultUtilityPackagePath));
            }
        }

        private static bool NulWatcherAutoStart
        {
            get => EditorPrefs.GetBool(NulWatcherAutoStartPref, false);
            set => EditorPrefs.SetBool(NulWatcherAutoStartPref, value);
        }

        private static string NulWatcherWatchDir
        {
            get
            {
                var custom = EditorPrefs.GetString(NulWatcherWatchDirPref, "");
                if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
                    return custom;
                
                // Default: Unity project root
                return Path.GetDirectoryName(Application.dataPath)!;
            }
            set => EditorPrefs.SetString(NulWatcherWatchDirPref, value);
        }

        #region NulFileWatcher

        private static string NulFileWatcherExe => 
            Path.Combine(UtilityPackagePath, "NulFileWatcher", "bin", "Release", "net8.0", "NulFileWatcher.exe");

        [MenuItem("Tools/Util/NulFileWatcher/Scan Project", false, 100)]
        public static void NulFileWatcher_ScanProject()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            RunNulFileWatcher(projectRoot!, watch: false);
        }

        [MenuItem("Tools/Util/NulFileWatcher/Scan Watch Directory", false, 101)]
        public static void NulFileWatcher_ScanWatchDir()
        {
            RunNulFileWatcher(NulWatcherWatchDir, watch: false);
        }

        [MenuItem("Tools/Util/NulFileWatcher/Start Watcher", false, 120)]
        public static void NulFileWatcher_StartWatcher()
        {
            // Stop existing watchers first to avoid duplicates
            StopAllProcesses("NulFileWatcher", silent: true);
            
            RunNulFileWatcher(NulWatcherWatchDir, watch: true, background: true);
        }

        [MenuItem("Tools/Util/NulFileWatcher/Stop Watcher", false, 121)]
        public static void NulFileWatcher_StopAll()
        {
            StopAllProcesses("NulFileWatcher", silent: false);
        }

        [MenuItem("Tools/Util/NulFileWatcher/Set Watch Directory...", false, 140)]
        public static void NulFileWatcher_SetWatchDir()
        {
            var current = NulWatcherWatchDir;
            var newPath = EditorUtility.OpenFolderPanel("Select Directory to Watch", current, "");
            
            if (!string.IsNullOrEmpty(newPath))
            {
                NulWatcherWatchDir = newPath;
                Debug.Log($"[NulFileWatcher] Watch directory set to: {newPath}");
                
                // Offer to restart watcher if running
                if (IsProcessRunning("NulFileWatcher"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Restart Watcher?",
                        "Watch directory changed. Restart the watcher with the new directory?",
                        "Restart", "Keep Current"))
                    {
                        NulFileWatcher_StartWatcher();
                    }
                }
            }
        }

        [MenuItem("Tools/Util/NulFileWatcher/Auto-Start on Unity Launch", false, 160)]
        public static void NulFileWatcher_ToggleAutoStart()
        {
            NulWatcherAutoStart = !NulWatcherAutoStart;
            Debug.Log($"[NulFileWatcher] Auto-start on Unity launch: {(NulWatcherAutoStart ? "ENABLED" : "DISABLED")}");
        }

        [MenuItem("Tools/Util/NulFileWatcher/Auto-Start on Unity Launch", true)]
        public static bool NulFileWatcher_ToggleAutoStart_Validate()
        {
            Menu.SetChecked("Tools/Util/NulFileWatcher/Auto-Start on Unity Launch", NulWatcherAutoStart);
            return true;
        }

        [MenuItem("Tools/Util/NulFileWatcher/Show Status", false, 180)]
        public static void NulFileWatcher_ShowStatus()
        {
            var isRunning = IsProcessRunning("NulFileWatcher");
            var status = isRunning ? "RUNNING" : "STOPPED";
            
            Debug.Log($"[NulFileWatcher] Status: {status}");
            Debug.Log($"[NulFileWatcher] Watch directory: {NulWatcherWatchDir}");
            Debug.Log($"[NulFileWatcher] Auto-start: {(NulWatcherAutoStart ? "Enabled" : "Disabled")}");
            Debug.Log($"[NulFileWatcher] Exe path: {NulFileWatcherExe}");
            Debug.Log($"[NulFileWatcher] Exe exists: {File.Exists(NulFileWatcherExe)}");
        }

        [MenuItem("Tools/Util/NulFileWatcher/Build Tool", false, 200)]
        public static void BuildNulFileWatcher()
        {
            var csprojPath = Path.Combine(UtilityPackagePath, "NulFileWatcher");
            
            if (!Directory.Exists(csprojPath))
            {
                Debug.LogError($"[ExternalUtilityTools] NulFileWatcher project not found at: {csprojPath}");
                return;
            }

            Debug.Log("[ExternalUtilityTools] Building NulFileWatcher...");
            
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build -c Release",
                WorkingDirectory = csprojPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                {
                    Debug.LogError("[ExternalUtilityTools] Failed to start dotnet build");
                    return;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Debug.Log($"[ExternalUtilityTools] Build succeeded:\n{output}");
                }
                else
                {
                    Debug.LogError($"[ExternalUtilityTools] Build failed:\n{error}\n{output}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ExternalUtilityTools] Build exception: {ex.Message}");
            }
        }

        private static void RunNulFileWatcher(string path, bool watch, bool background = false)
        {
            if (!File.Exists(NulFileWatcherExe))
            {
                var build = EditorUtility.DisplayDialog(
                    "NulFileWatcher Not Built",
                    $"NulFileWatcher.exe not found at:\n{NulFileWatcherExe}\n\nWould you like to build it now?",
                    "Build", "Cancel");

                if (build)
                {
                    BuildNulFileWatcher();
                    if (!File.Exists(NulFileWatcherExe))
                    {
                        Debug.LogError("[ExternalUtilityTools] Build failed. Check console for errors.");
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            var args = $"\"{path}\"";
            if (watch) args += " --watch";

            RunExternalTool(NulFileWatcherExe, args, background, "NulFileWatcher");
        }

        #endregion

        #region Utility Methods

        private static void RunExternalTool(string exePath, string args, bool background, string toolName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = background,
                    CreateNoWindow = !background,
                    WindowStyle = background ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
                };

                if (!background)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                }

                var process = Process.Start(psi);
                
                if (process == null)
                {
                    Debug.LogError($"[ExternalUtilityTools] Failed to start {toolName}");
                    return;
                }

                if (background)
                {
                    spawnedProcessIds.Add(process.Id);
                    Debug.Log($"[ExternalUtilityTools] Started {toolName} in background (PID: {process.Id})");
                }
                else
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                        Debug.Log($"[{toolName}]\n{output}");
                    if (!string.IsNullOrEmpty(error))
                        Debug.LogWarning($"[{toolName}] Errors:\n{error}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ExternalUtilityTools] Error running {toolName}: {ex.Message}");
            }
        }

        private static void StopAllProcesses(string processName, bool silent = false)
        {
            var processes = Process.GetProcessesByName(processName);
            
            if (processes.Length == 0)
            {
                if (!silent)
                    Debug.Log($"[ExternalUtilityTools] No {processName} processes running");
                return;
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(1000);
                    if (!silent)
                        Debug.Log($"[ExternalUtilityTools] Stopped {processName} (PID: {process.Id})");
                }
                catch (System.Exception ex)
                {
                    if (!silent)
                        Debug.LogWarning($"[ExternalUtilityTools] Failed to stop PID {process.Id}: {ex.Message}");
                }
            }
            
            spawnedProcessIds.Clear();
        }

        private static bool IsProcessRunning(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }

        #endregion

        #region Settings

        [MenuItem("Tools/Util/Settings/Set UtilityPackage Path...", false, 300)]
        public static void SetUtilityPackagePath()
        {
            var current = UtilityPackagePath;
            var newPath = EditorUtility.OpenFolderPanel("Select UtilityPackage Folder", current, "");
            
            if (!string.IsNullOrEmpty(newPath))
            {
                EditorPrefs.SetString(UtilityPackagePathPref, newPath);
                Debug.Log($"[ExternalUtilityTools] UtilityPackage path set to: {newPath}");
            }
        }

        [MenuItem("Tools/Util/Settings/Reset All to Defaults", false, 320)]
        public static void ResetAllSettings()
        {
            EditorPrefs.DeleteKey(UtilityPackagePathPref);
            EditorPrefs.DeleteKey(NulWatcherAutoStartPref);
            EditorPrefs.DeleteKey(NulWatcherWatchDirPref);
            Debug.Log("[ExternalUtilityTools] All settings reset to defaults");
        }

        #endregion
    }
}
#endif
