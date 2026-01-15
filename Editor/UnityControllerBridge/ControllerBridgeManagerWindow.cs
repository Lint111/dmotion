using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Editor window for managing Unity Controller Bridges.
    /// Provides centralized view and operations for all bridges.
    /// </summary>
    public class ControllerBridgeManagerWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private string _searchFilter = "";
        private bool _showOnlyDirty = false;
        private List<UnityControllerBridgeAsset> _filteredBridges = new List<UnityControllerBridgeAsset>();

        private GUIStyle _headerStyle;
        private GUIStyle _statusCleanStyle;
        private GUIStyle _statusDirtyStyle;

        [MenuItem("Window/DMotion/Controller Bridge Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<ControllerBridgeManagerWindow>("Controller Bridge Manager");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            // Subscribe to events
            ControllerConversionQueue.OnConversionStarted += OnConversionStarted;
            ControllerConversionQueue.OnConversionFinished += OnConversionFinished;

            RefreshBridgeList();
        }

        private void OnDisable()
        {
            // Unsubscribe from events
            ControllerConversionQueue.OnConversionStarted -= OnConversionStarted;
            ControllerConversionQueue.OnConversionFinished -= OnConversionFinished;
        }

        private void OnConversionStarted(string bridgeId)
        {
            Repaint();
        }

        private void OnConversionFinished(string bridgeId, bool success)
        {
            RefreshBridgeList();
            Repaint();
        }

        private void OnGUI()
        {
            InitializeStyles();

            DrawHeader();
            DrawToolbar();
            DrawStatistics();
            EditorGUILayout.Space();
            DrawBridgeList();
            DrawFooter();
        }

        private void InitializeStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_statusCleanStyle == null)
            {
                _statusCleanStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = Color.green }
                };
            }

            if (_statusDirtyStyle == null)
            {
                _statusDirtyStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = Color.yellow }
                };
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Unity Controller Bridge Manager", _headerStyle);
            EditorGUILayout.Space();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // Search field
                GUILayout.Label("Search:", GUILayout.Width(50));
                string newSearch = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
                if (newSearch != _searchFilter)
                {
                    _searchFilter = newSearch;
                    RefreshBridgeList();
                }

                GUILayout.Space(10);

                // Filter toggle
                bool newShowOnlyDirty = GUILayout.Toggle(_showOnlyDirty, "Show Only Dirty", EditorStyles.toolbarButton);
                if (newShowOnlyDirty != _showOnlyDirty)
                {
                    _showOnlyDirty = newShowOnlyDirty;
                    RefreshBridgeList();
                }

                GUILayout.FlexibleSpace();

                // Refresh button
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                {
                    RefreshBridgeList();
                }
            }
        }

        private void DrawStatistics()
        {
            var allBridges = ControllerBridgeRegistry.GetAllBridges();
            var dirtyBridges = ControllerBridgeDirtyTracker.GetDirtyBridges();
            int queueCount = ControllerConversionQueue.QueueCount;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Total Bridges: {allBridges.Count}", GUILayout.Width(120));
                EditorGUILayout.LabelField($"Dirty: {dirtyBridges.Count}", GUILayout.Width(80));
                EditorGUILayout.LabelField($"Queue: {queueCount}", GUILayout.Width(80));
                EditorGUILayout.LabelField($"Processing: {(ControllerConversionQueue.IsProcessing ? "Yes" : "No")}", GUILayout.Width(100));
            }
        }

        private void DrawBridgeList()
        {
            EditorGUILayout.LabelField("Bridges", EditorStyles.boldLabel);

            if (_filteredBridges.Count == 0)
            {
                EditorGUILayout.HelpBox("No bridges found. Create bridges from AnimatorController assets using the Assets menu.", MessageType.Info);
                return;
            }

            // Column headers
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Name", EditorStyles.toolbarButton, GUILayout.Width(200));
                GUILayout.Label("Status", EditorStyles.toolbarButton, GUILayout.Width(80));
                GUILayout.Label("Refs", EditorStyles.toolbarButton, GUILayout.Width(50));
                GUILayout.Label("Source Controller", EditorStyles.toolbarButton, GUILayout.Width(200));
                GUILayout.Label("Actions", EditorStyles.toolbarButton);
            }

            // Bridge list
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var bridge in _filteredBridges)
            {
                if (bridge == null) continue;

                DrawBridgeRow(bridge);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawBridgeRow(UnityControllerBridgeAsset bridge)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // Name (clickable to select)
                if (GUILayout.Button(bridge.name, EditorStyles.label, GUILayout.Width(200)))
                {
                    Selection.activeObject = bridge;
                    EditorGUIUtility.PingObject(bridge);
                }

                // Status
                GUIStyle statusStyle = bridge.IsDirty ? _statusDirtyStyle : _statusCleanStyle;
                GUILayout.Label(bridge.IsDirty ? "DIRTY" : "CLEAN", statusStyle, GUILayout.Width(80));

                // Reference count
                int refCount = ControllerBridgeRegistry.GetReferenceCount(bridge);
                GUILayout.Label(refCount.ToString(), GUILayout.Width(50));

                // Source controller
                if (bridge.SourceController != null)
                {
                    if (GUILayout.Button(bridge.SourceController.name, EditorStyles.label, GUILayout.Width(200)))
                    {
                        Selection.activeObject = bridge.SourceController;
                        EditorGUIUtility.PingObject(bridge.SourceController);
                    }
                }
                else
                {
                    GUILayout.Label("(missing)", EditorStyles.label, GUILayout.Width(200));
                }

                GUILayout.FlexibleSpace();

                // Actions
                if (GUILayout.Button("Convert", GUILayout.Width(70)))
                {
                    ConvertBridge(bridge);
                }

                if (GUILayout.Button("Check", GUILayout.Width(60)))
                {
                    bridge.CheckForChanges();
                    Repaint();
                }

                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    Selection.activeObject = bridge;
                    EditorGUIUtility.PingObject(bridge);
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Force Check All"))
                {
                    ControllerBridgeDirtyTracker.ForceCheckAllBridges();
                    RefreshBridgeList();
                }

                if (GUILayout.Button("Convert All Dirty"))
                {
                    ConvertAllDirty();
                }

                if (GUILayout.Button("Clear Queue"))
                {
                    ControllerConversionQueue.Clear();
                    Repaint();
                }

                using (new EditorGUI.DisabledScope(!ControllerConversionQueue.IsProcessing))
                {
                    if (GUILayout.Button("Stop Processing"))
                    {
                        ControllerConversionQueue.StopProcessing();
                        Repaint();
                    }
                }
            }
        }

        private void RefreshBridgeList()
        {
            var allBridges = ControllerBridgeRegistry.GetAllBridges();

            // Apply filters
            _filteredBridges = allBridges.Where(bridge =>
            {
                if (bridge == null) return false;

                // Search filter
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool matchesSearch = bridge.name.ToLower().Contains(_searchFilter.ToLower()) ||
                                       (bridge.SourceController != null && bridge.SourceController.name.ToLower().Contains(_searchFilter.ToLower()));

                    if (!matchesSearch) return false;
                }

                // Dirty filter
                if (_showOnlyDirty && !bridge.IsDirty)
                {
                    return false;
                }

                return true;
            }).ToList();
        }

        private void ConvertBridge(UnityControllerBridgeAsset bridge)
        {
            ControllerConversionQueue.Enqueue(bridge);
            if (!ControllerConversionQueue.IsProcessing)
            {
                ControllerConversionQueue.StartProcessing();
            }
            Repaint();
        }

        private void ConvertAllDirty()
        {
            var dirtyBridges = ControllerBridgeDirtyTracker.GetDirtyBridges();

            if (dirtyBridges.Count == 0)
            {
                EditorUtility.DisplayDialog("Info", "No dirty bridges to convert.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "Convert All Dirty Bridges?",
                $"This will convert {dirtyBridges.Count} dirty bridge(s). Continue?",
                "Convert",
                "Cancel"))
            {
                return;
            }

            foreach (var bridge in dirtyBridges)
            {
                ControllerConversionQueue.Enqueue(bridge);
            }

            if (!ControllerConversionQueue.IsProcessing)
            {
                ControllerConversionQueue.StartProcessing();
            }

            Debug.Log($"[Unity Controller Bridge] Enqueued {dirtyBridges.Count} dirty bridge(s) for conversion");
            Repaint();
        }
    }
}
