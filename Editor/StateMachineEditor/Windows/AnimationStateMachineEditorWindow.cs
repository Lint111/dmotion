using System;
using DMotion.Authoring;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal static class ToolMenuConstants
    {
        internal const string WindowPath = "Window";
        internal const string DMotionPath = WindowPath + "/DMotion";
    }

    internal struct StateMachineEditorViewModel
    {
        internal StateMachineAsset StateMachineAsset;
        internal EntitySelectionProxyWrapper SelectedEntity;
        internal VisualTreeAsset StateNodeXml;
    }

    internal class AnimationStateMachineEditorWindow : EditorWindow
    {
        [SerializeField] internal VisualTreeAsset StateMachineEditorXml;
        [SerializeField] internal VisualTreeAsset StateNodeXml;
        
        // Persisted across domain reloads
        [SerializeField] private StateMachineAsset lastEditedStateMachine;
        [SerializeField] private string lastEditedAssetGuid;
        
        // Root machine for multi-layer support (may differ from lastEditedStateMachine when inside a layer)
        [SerializeField] private StateMachineAsset rootStateMachine;
        
        // Preference keys
        private const string AutoOpenPreviewPrefKey = "DMotion.StateMachineEditor.AutoOpenPreview";
        
        /// <summary>
        /// Whether to automatically open the Animation Preview window when the editor opens.
        /// </summary>
        internal static bool AutoOpenPreviewWindow
        {
            get => EditorPrefs.GetBool(AutoOpenPreviewPrefKey, true);
            set => EditorPrefs.SetBool(AutoOpenPreviewPrefKey, value);
        }

        private AnimationStateMachineEditorView stateMachineEditorView;
        private StateMachineInspectorView inspectorView;
        private StateMachineInspectorView parametersInspectorView;
        private StateMachineInspectorView layersInspectorView;
        private StateMachineInspectorView dependenciesInspectorView;
        private BreadcrumbBar breadcrumbBar;
        
        // Event-driven panel controllers
        private InspectorController inspectorController;
        private ParametersPanelController parametersPanelController;
        private LayersPanelController layersPanelController;
        private DependenciesPanelController dependenciesPanelController;
        private BreadcrumbController breadcrumbController;
        
        private bool hasRestoredAfterDomainReload;

        [MenuItem(ToolMenuConstants.DMotionPath + "/Open Workspace")]
        internal static void OpenWorkspace()
        {
            var wnd = GetWindow<AnimationStateMachineEditorWindow>();
            wnd.titleContent = new GUIContent("State Machine Editor");
            wnd.Focus();

            // Best-effort: open the Animation Preview docked as a tab next to the editor.
            AnimationPreviewWindow.ShowDockedWithStateMachineEditor();

            // Ensure selection drives loading in the editor window.
            wnd.OnSelectionChange();
        }

        [MenuItem(ToolMenuConstants.DMotionPath + "/State Machine Editor")]
        internal static void ShowWindow()
        {
            var wnd = GetWindow<AnimationStateMachineEditorWindow>();
            wnd.titleContent = new GUIContent("State Machine Editor");
            wnd.Focus();
            wnd.OnSelectionChange();
        }

        /// <summary>
        /// Opens the state machine editor with a specific asset.
        /// If the asset is already open in a window, focuses that window.
        /// Otherwise opens in the main window or creates a new one.
        /// </summary>
        /// <param name="asset">The state machine to edit</param>
        /// <param name="forceNewWindow">If true, always creates a new window instance</param>
        public static void OpenWindow(StateMachineAsset asset, bool forceNewWindow = false)
        {
            if (asset == null) return;
            
            // Try to find existing window with this asset
            if (!forceNewWindow)
            {
                var existingWindows = Resources.FindObjectsOfTypeAll<AnimationStateMachineEditorWindow>();
                foreach (var window in existingWindows)
                {
                    if (window.CurrentAsset == asset)
                    {
                        window.Focus();
                        return;
                    }
                }
            }
            
            // Get or create window
            AnimationStateMachineEditorWindow wnd;
            if (forceNewWindow)
            {
                wnd = CreateInstance<AnimationStateMachineEditorWindow>();
                wnd.Show();
            }
            else
            {
                wnd = GetWindow<AnimationStateMachineEditorWindow>();
            }
            
            wnd.titleContent = new GUIContent($"State Machine: {asset.name}");
            wnd.LoadStateMachine(asset);
            wnd.Focus();
        }
        
        /// <summary>
        /// The currently loaded state machine asset.
        /// </summary>
        public StateMachineAsset CurrentAsset => lastEditedStateMachine;

        
        [MenuItem(ToolMenuConstants.DMotionPath + "/Auto-Open Animation Preview")]
        private static void ToggleAutoOpenPreview()
        {
            AutoOpenPreviewWindow = !AutoOpenPreviewWindow;
        }
        
        [MenuItem(ToolMenuConstants.DMotionPath + "/Auto-Open Animation Preview", true)]
        private static bool ToggleAutoOpenPreviewValidate()
        {
            Menu.SetChecked(ToolMenuConstants.DMotionPath + "/Auto-Open Animation Preview", AutoOpenPreviewWindow);
            return true;
        }

        private void Awake()
        {
            EditorApplication.playModeStateChanged += OnPlaymodeStateChanged;
        }

        private void OnDestroy()
        {
            EditorApplication.playModeStateChanged -= OnPlaymodeStateChanged;
            
            // Unsubscribe all panel controllers
            inspectorController?.Unsubscribe();
            parametersPanelController?.Unsubscribe();
            layersPanelController?.Unsubscribe();
            dependenciesPanelController?.Unsubscribe();
            breadcrumbController?.Unsubscribe();
            
            // EditorState handles its own cleanup via Dispose pattern
        }

        private void OnPlaymodeStateChanged(PlayModeStateChange stateChange)
        {
            switch (stateChange)
            {
                case PlayModeStateChange.EnteredEditMode:
                    stateMachineEditorView?.UpdateView();
                    break;
                case PlayModeStateChange.ExitingEditMode:
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stateChange), stateChange, null);
            }
        }

        internal void CreateGUI()
        {
            var root = rootVisualElement;
            if (StateMachineEditorXml != null)
            {
                StateMachineEditorXml.CloneTree(root);
                stateMachineEditorView = root.Q<AnimationStateMachineEditorView>();
                inspectorView = root.Q<StateMachineInspectorView>("inspector");
                parametersInspectorView = root.Q<StateMachineInspectorView>("parameters-inspector");
                layersInspectorView = root.Q<StateMachineInspectorView>("layers-inspector");
                dependenciesInspectorView = root.Q<StateMachineInspectorView>("dependencies-inspector");
                breadcrumbBar = root.Q<BreadcrumbBar>("breadcrumb-bar");
                
                // Create panel controllers and subscribe to events
                if (stateMachineEditorView != null)
                {
                    if (inspectorView != null)
                    {
                        inspectorController = new InspectorController(inspectorView, stateMachineEditorView);
                        inspectorController.Subscribe();
                    }
                    
                    if (parametersInspectorView != null)
                    {
                        parametersPanelController = new ParametersPanelController(parametersInspectorView);
                        parametersPanelController.Subscribe();
                    }
                    
                    if (layersInspectorView != null)
                    {
                        layersPanelController = new LayersPanelController(layersInspectorView);
                        layersPanelController.OnEditLayerRequested = OnEditLayerRequested;
                        layersPanelController.Subscribe();
                    }
                    
                    if (dependenciesInspectorView != null)
                    {
                        dependenciesPanelController = new DependenciesPanelController(dependenciesInspectorView);
                        dependenciesPanelController.Subscribe();
                    }
                    
                    if (breadcrumbBar != null)
                    {
                        breadcrumbController = new BreadcrumbController(breadcrumbBar);
                        breadcrumbController.OnNavigationRequested += OnNavigationRequested;
                        breadcrumbController.Subscribe();
                    }
                }
            }
            
            // Restore last edited asset after domain reload
            RestoreAfterDomainReload();
        }
        
        /// <summary>
        /// Called by BreadcrumbController when navigation is requested (via events or clicks).
        /// </summary>
        private void OnNavigationRequested(StateMachineAsset targetMachine)
        {
            if (targetMachine == null) return;
            LoadStateMachineInternal(targetMachine, updateBreadcrumb: false);
        }
        
        /// <summary>
        /// Called when user requests to edit a layer's state machine.
        /// Navigates into the layer via breadcrumb.
        /// </summary>
        private void OnEditLayerRequested(LayerStateAsset layer)
        {
            if (layer?.NestedStateMachine == null) return;
            
            // Push to breadcrumb and navigate
            breadcrumbController?.Push(layer.NestedStateMachine);
            LoadStateMachineInternal(layer.NestedStateMachine, updateBreadcrumb: false);
        }
        
        private void OnEnable()
        {
            // Reset the restore flag when window is enabled
            hasRestoredAfterDomainReload = false;
            
            // Register for Undo/Redo to refresh the view
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }
        
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }
        
        private void OnUndoRedoPerformed()
        {
            // Refresh the view after undo/redo to show correct state
            if (lastEditedStateMachine != null && stateMachineEditorView != null)
            {
                // Save current view transform using resolvedStyle (non-deprecated API)
                var container = stateMachineEditorView.contentViewContainer;
                var translate = container.resolvedStyle.translate;
                var scale = container.resolvedStyle.scale;
                var savedPosition = new Vector3(translate.x, translate.y, 0f);
                var savedScale = new Vector3(scale.value.x, scale.value.y, 1f);
                
                // Refresh panel controllers (they will re-render with updated data)
                parametersPanelController?.SetContext(lastEditedStateMachine);
                dependenciesPanelController?.SetContext(lastEditedStateMachine);
                
                // Repopulate graph without framing
                stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
                {
                    StateMachineAsset = lastEditedStateMachine,
                    StateNodeXml = StateNodeXml
                });
                
                // Restore view transform
                stateMachineEditorView.UpdateViewTransform(savedPosition, savedScale);
            }
        }
        
        /// <summary>
        /// Restores the previously edited state machine after domain reload.
        /// </summary>
        private void RestoreAfterDomainReload()
        {
            if (hasRestoredAfterDomainReload) return;
            hasRestoredAfterDomainReload = true;
            
            // Restore EditorState.RootStateMachine from serialized field
            // EditorState singleton is recreated after domain reload, losing RootStateMachine
            if (rootStateMachine != null && EditorState.Instance.RootStateMachine != rootStateMachine)
            {
                EditorState.Instance.RootStateMachine = rootStateMachine;
            }
            
            // Restore breadcrumb navigation state
            breadcrumbBar?.RestoreNavigationState();
            
            // Get the current state machine from breadcrumb (which may have been restored)
            var currentFromBreadcrumb = breadcrumbBar?.CurrentStateMachine;
            if (currentFromBreadcrumb != null)
            {
                // Restore CurrentViewStateMachine if we were navigated into a layer/sub-machine
                if (currentFromBreadcrumb != rootStateMachine)
                {
                    EditorState.Instance.CurrentViewStateMachine = currentFromBreadcrumb;
                }
                
                // Load the current state machine without resetting breadcrumb
                LoadStateMachineInternal(currentFromBreadcrumb, updateBreadcrumb: false);
                return;
            }
            
            // Fall back to serialized references (try lastEdited first, then root)
            if (lastEditedStateMachine != null)
            {
                LoadStateMachine(lastEditedStateMachine);
                return;
            }
            
            if (rootStateMachine != null)
            {
                LoadStateMachine(rootStateMachine);
                return;
            }
            
            // Fall back to GUID if all references were lost
            if (!string.IsNullOrEmpty(lastEditedAssetGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(lastEditedAssetGuid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<StateMachineAsset>(assetPath);
                    if (asset != null)
                    {
                        LoadStateMachine(asset);
                    }
                }
            }
        }
        
        /// <summary>
        /// Loads a state machine into the editor and persists the reference.
        /// Called when selecting a new root state machine (resets breadcrumb).
        /// </summary>
        private void LoadStateMachine(StateMachineAsset stateMachineAsset)
        {
            if (stateMachineAsset == null) return;
            
            // This is a new root machine
            rootStateMachine = stateMachineAsset;
            
            // Update EditorState with the new root state machine
            // This triggers preview type detection (multi-layer vs single) and initializes CompositionState
            EditorState.Instance.RootStateMachine = stateMachineAsset;
            
            // Reset breadcrumb to new root via controller
            breadcrumbController?.SetRoot(stateMachineAsset);
            
            LoadStateMachineInternal(stateMachineAsset, updateBreadcrumb: false);
            
            // Auto-open the Animation Preview window if enabled and not already open
            if (AutoOpenPreviewWindow && !HasOpenInstances<AnimationPreviewWindow>())
            {
                AnimationPreviewWindow.ShowDockedWithStateMachineEditor();
            }
        }
        
        /// <summary>
        /// Internal method to load a state machine without resetting breadcrumb.
        /// </summary>
        private void LoadStateMachineInternal(StateMachineAsset stateMachineAsset, bool updateBreadcrumb = true)
        {
            if (stateMachineAsset == null) return;
            if (stateMachineEditorView == null) return;
            
            // Persist for domain reload recovery
            lastEditedStateMachine = stateMachineAsset;
            string assetPath = AssetDatabase.GetAssetPath(stateMachineAsset);
            if (!string.IsNullOrEmpty(assetPath))
            {
                lastEditedAssetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            }
            
            // Update all panel controllers with new context
            inspectorController?.SetContext(stateMachineAsset);
            parametersPanelController?.SetContext(stateMachineAsset);
            layersPanelController?.SetContext(stateMachineAsset, rootStateMachine);
            dependenciesPanelController?.SetContext(stateMachineAsset);
            
            stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
            {
                StateMachineAsset = stateMachineAsset,
                StateNodeXml = StateNodeXml
            });
            WaitAndFrameAll();
        }

        [OnOpenAsset]
        internal static bool OnOpenBehaviourTree(int instanceId, int line)
        {
            if (Selection.activeObject is StateMachineAsset)
            {
                OpenWorkspace();
                return true;
            }

            return false;
        }

        private void Update()
        {
            if (Application.isPlaying && stateMachineEditorView != null)
            {
                stateMachineEditorView.UpdateView();
            }
        }

        private void OnSelectionChange()
        {
            if (stateMachineEditorView != null && !Application.isPlaying &&
                Selection.activeObject is StateMachineAsset stateMachineAsset)
            {
                LoadStateMachine(stateMachineAsset);
            }

            // Handle play mode entity selection
            if (stateMachineEditorView != null && Application.isPlaying &&
                EntitySelectionProxyUtils.TryExtractEntitySelectionProxy(out var entitySelectionProxy))
            {
                if (entitySelectionProxy.TryGetManagedComponent<AnimationStateMachineDebug>(out var stateMachineDebug))
                {
                    Assert.IsNotNull(stateMachineDebug.StateMachineAsset);
                    if (stateMachineDebug.StateMachineAsset != null)
                    {
                        // Persist for domain reload recovery
                        lastEditedStateMachine = stateMachineDebug.StateMachineAsset;
                        string assetPath = AssetDatabase.GetAssetPath(stateMachineDebug.StateMachineAsset);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            lastEditedAssetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                        }
                        
                        // Update panel controllers
                        inspectorController?.SetContext(stateMachineDebug.StateMachineAsset);
                        parametersPanelController?.SetContext(stateMachineDebug.StateMachineAsset);
                        dependenciesPanelController?.SetContext(stateMachineDebug.StateMachineAsset);
                        
                        stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
                        {
                            StateMachineAsset = stateMachineDebug.StateMachineAsset,
                            SelectedEntity = entitySelectionProxy,
                            StateNodeXml = StateNodeXml
                        });
                        WaitAndFrameAll();
                    }
                }
            }
        }

        private void WaitAndFrameAll()
        {
            // UI Toolkit layout workaround: There's no reliable callback for "layout complete".
            // - VisualElement.RegisterCallback<GeometryChangedEvent> fires before children are laid out
            // - EditorApplication.delayCall fires too early
            // - schedule.Execute with delay is inconsistent across Unity versions
            // Using EditorApplication.update with a small delay is the most reliable approach.
            // See: https://forum.unity.com/threads/how-to-know-when-layout-is-ready.1034hybrid/
            EditorApplication.update += DoFrameAll;
            frameAllTime = EditorApplication.timeSinceStartup + 0.1f;
        }

        private double frameAllTime;

        private void DoFrameAll()
        {
            if (EditorApplication.timeSinceStartup > frameAllTime)
            {
                EditorApplication.update -= DoFrameAll;
                stateMachineEditorView.FrameAll();
            }
        }
    }
}