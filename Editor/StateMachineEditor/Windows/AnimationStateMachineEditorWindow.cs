using System;
using System.Collections.Generic;
using System.Globalization;
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
        
        // Quick navigation overlay (Ctrl+Shift)
        private QuickNavigationOverlay quickNavigationOverlay;

        private bool hasRestoredAfterDomainReload;

        // Store subscribed EditorState instance to handle domain reload correctly
        private EditorState _subscribedEditorState;

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

            // Save view transform before destruction
            if (lastEditedStateMachine != null && stateMachineEditorView != null)
            {
                SaveViewTransform(lastEditedStateMachine);
            }

            // Unsubscribe all panel controllers
            inspectorController?.Unsubscribe();
            parametersPanelController?.Unsubscribe();
            layersPanelController?.Unsubscribe();
            dependenciesPanelController?.Unsubscribe();
            breadcrumbController?.Unsubscribe();

            // Cleanup quick navigation overlay
            if (quickNavigationOverlay != null)
            {
                quickNavigationOverlay.OnNavigate -= OnQuickNavigationRequested;
                quickNavigationOverlay.OnClosed -= OnQuickNavigationClosed;
            }

            // EditorState handles its own cleanup via Dispose pattern
        }
        
        private void OnFocus()
        {
            // When window gains focus (e.g., clicking on tab), focus the editor view
            // so keyboard shortcuts work immediately
            stateMachineEditorView?.Focus();
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
                    
                    // Wire up layer navigation shortcut handler
                    stateMachineEditorView.OnLayerNavigationRequested = OnLayerShortcutNavigation;

                    // Wire up view transform change handler for persistence
                    stateMachineEditorView.OnViewTransformChanged = OnViewTransformChanged;
                }
            }
            
            // Create quick navigation overlay
            quickNavigationOverlay = new QuickNavigationOverlay();
            quickNavigationOverlay.OnNavigate += OnQuickNavigationRequested;
            quickNavigationOverlay.OnClosed += OnQuickNavigationClosed;
            quickNavigationOverlay.style.display = DisplayStyle.None;
            root.Add(quickNavigationOverlay);
            
            // Register keyboard handler for Ctrl+Shift quick navigation
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
            
            // Focus window and editor view when clicking anywhere in the window
            root.RegisterCallback<PointerDownEvent>(OnRootPointerDown);
            
            // Restore last edited asset after domain reload
            RestoreAfterDomainReload();
        }
        
        /// <summary>
        /// Handles keyboard shortcuts at the root level.
        /// </summary>
        private void OnRootKeyDown(KeyDownEvent evt)
        {
            // Ctrl+Shift to toggle quick navigation overlay
            if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.None)
            {
                // Just Ctrl+Shift pressed without another key - might be starting the combo
                return;
            }
            
            // Ctrl+Shift+Space or Ctrl+Shift+N to toggle quick navigation
            if (evt.ctrlKey && evt.shiftKey && (evt.keyCode == KeyCode.Space || evt.keyCode == KeyCode.N))
            {
                ToggleQuickNavigationOverlay();
                evt.StopPropagation();
            }
        }
        
        /// <summary>
        /// Handles pointer down on the window to ensure proper focus.
        /// </summary>
        private void OnRootPointerDown(PointerDownEvent evt)
        {
            // Focus the window and editor view when clicking anywhere
            Focus();
            stateMachineEditorView?.Focus();
        }
        
        /// <summary>
        /// Handles layer navigation via Ctrl+Number shortcut.
        /// </summary>
        private void OnLayerShortcutNavigation(LayerStateAsset layer, int layerIndex)
        {
            if (layer?.NestedStateMachine == null) return;
            
            // Push to breadcrumb and navigate
            breadcrumbController?.Push(layer.NestedStateMachine);
            LoadStateMachineInternal(layer.NestedStateMachine, updateBreadcrumb: false);
        }
        
        /// <summary>
        /// Toggles the quick navigation overlay.
        /// </summary>
        internal void ToggleQuickNavigationOverlay()
        {
            if (quickNavigationOverlay == null) return;
            
            if (quickNavigationOverlay.IsVisible)
            {
                quickNavigationOverlay.Close();
            }
            else if (rootStateMachine != null)
            {
                // Show overlay with current context
                var currentView = breadcrumbBar?.CurrentStateMachine ?? lastEditedStateMachine;
                quickNavigationOverlay.Show(rootStateMachine, currentView);
            }
        }
        
        /// <summary>
        /// Handles navigation request from quick navigation overlay.
        /// </summary>
        private void OnQuickNavigationRequested(NavigationTreeNode node)
        {
            if (node == null || node.StateMachine == null) return;

            // Build the full path from root to target by traversing up the tree
            var pathFromRoot = BuildNavigationPath(node);

            // Find containing layer (if any) by checking ancestors
            var containingLayer = FindContainingLayerNode(node);

            // Set layer context if target is inside a layer
            if (containingLayer != null && containingLayer.LayerAsset != null)
            {
                EditorState.Instance.EnterLayer(containingLayer.LayerAsset, containingLayer.LayerIndex);
            }
            else if (node.NodeType == NavigationNodeType.Root)
            {
                // Exiting layers - clear layer context
                EditorState.Instance.ExitLayer();
            }

            // Reset breadcrumb to root, then push each node in the path
            if (pathFromRoot.Count > 0)
            {
                // First node is root
                breadcrumbController?.SetRoot(pathFromRoot[0].StateMachine);

                // Push remaining nodes in order
                for (int i = 1; i < pathFromRoot.Count; i++)
                {
                    breadcrumbController?.Push(pathFromRoot[i].StateMachine);
                }
            }

            // Load the target state machine
            LoadStateMachineInternal(node.StateMachine, updateBreadcrumb: false);

            // Restore focus to editor view after navigation
            RestoreFocusToEditorView();
        }

        /// <summary>
        /// Builds the navigation path from root to target node.
        /// </summary>
        private List<NavigationTreeNode> BuildNavigationPath(NavigationTreeNode target)
        {
            var path = new List<NavigationTreeNode>();
            var current = target;

            while (current != null)
            {
                path.Insert(0, current); // Insert at beginning to maintain root-to-target order
                current = current.Parent;
            }

            return path;
        }

        /// <summary>
        /// Finds the containing layer node by traversing up the tree.
        /// </summary>
        private NavigationTreeNode FindContainingLayerNode(NavigationTreeNode node)
        {
            var current = node;
            while (current != null)
            {
                if (current.NodeType == NavigationNodeType.Layer)
                {
                    return current;
                }
                current = current.Parent;
            }
            return null;
        }
        
        /// <summary>
        /// Handles quick navigation overlay closed (Escape or outside click).
        /// </summary>
        private void OnQuickNavigationClosed()
        {
            RestoreFocusToEditorView();
        }
        
        /// <summary>
        /// Restores keyboard focus to the editor view.
        /// </summary>
        private void RestoreFocusToEditorView()
        {
            // Schedule focus restoration to ensure UI has updated
            stateMachineEditorView?.schedule.Execute(() =>
            {
                stateMachineEditorView?.Focus();
            });
        }

        /// <summary>
        /// Handles view transform (pan/zoom) changes.
        /// Saves the current view transform for the currently loaded state machine.
        /// </summary>
        private void OnViewTransformChanged()
        {
            if (lastEditedStateMachine != null && stateMachineEditorView != null)
            {
                SaveViewTransform(lastEditedStateMachine);
            }
        }

        /// <summary>
        /// Handles navigation requests from EditorState (e.g., preview window "Navigate" button).
        /// Traverses sub-state machine hierarchy to reach the target state and frames it.
        /// </summary>
        private void OnNavigationRequested(object sender, NavigationRequestedEventArgs e)
        {
            if (e.Container?.NestedStateMachine == null) return;

            var containerStateMachine = e.Container.NestedStateMachine;
            var targetState = e.IsTransition ? e.TransitionFrom : e.TargetState;

            if (targetState == null) return;

            // Find the state machine that directly contains the target state
            var containingMachine = FindContainingStateMachine(containerStateMachine, targetState);

            // Enter the layer if the container is a LayerStateAsset
            if (e.LayerAsset != null && e.LayerIndex >= 0)
            {
                EditorState.Instance.EnterLayer(e.LayerAsset, e.LayerIndex);
            }

            // Navigate to the containing state machine (traverse sub-state machines)
            if (containingMachine != null && containingMachine != containerStateMachine)
            {
                // Build path from container root to containing machine
                var path = BuildPathToStateMachine(containerStateMachine, containingMachine);
                foreach (var machine in path)
                {
                    breadcrumbController?.Push(machine);
                }
                LoadStateMachineInternal(containingMachine, updateBreadcrumb: false);
            }
            else
            {
                // State is directly in the container root
                LoadStateMachineInternal(containerStateMachine, updateBreadcrumb: false);
            }

            // Select the state or transition after graph is loaded
            stateMachineEditorView?.schedule.Execute(() =>
            {
                if (e.IsTransition)
                {
                    EditorState.Instance.SelectTransition(e.TransitionFrom, e.TransitionTo);
                    // Center on the transition source state
                    stateMachineEditorView?.CenterOnState(e.TransitionFrom);
                }
                else
                {
                    EditorState.Instance.SelectedState = e.TargetState;
                    // Center the view on the target state
                    stateMachineEditorView?.CenterOnState(e.TargetState);
                }
            });
        }

        /// <summary>
        /// Finds the state machine that directly contains the target state.
        /// </summary>
        private StateMachineAsset FindContainingStateMachine(StateMachineAsset root, AnimationStateAsset targetState)
        {
            if (root?.States == null) return null;

            // Check if state is directly in this machine
            if (root.States.Contains(targetState))
                return root;

            // Search in sub-state machines
            foreach (var state in root.States)
            {
                if (state is SubStateMachineStateAsset subMachine && subMachine.NestedStateMachine != null)
                {
                    var found = FindContainingStateMachine(subMachine.NestedStateMachine, targetState);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a path of state machines from root to target (excluding root, including target).
        /// </summary>
        private List<StateMachineAsset> BuildPathToStateMachine(StateMachineAsset root, StateMachineAsset target)
        {
            var path = new List<StateMachineAsset>();
            BuildPathRecursive(root, target, path);
            return path;
        }

        private bool BuildPathRecursive(StateMachineAsset current, StateMachineAsset target, List<StateMachineAsset> path)
        {
            if (current?.States == null) return false;

            foreach (var state in current.States)
            {
                if (state is not SubStateMachineStateAsset subMachine || subMachine.NestedStateMachine == null) continue; 
                if (subMachine.NestedStateMachine == target)
                {
                    path.Add(target);
                    return true;
                }

                if (BuildPathRecursive(subMachine.NestedStateMachine, target, path))
                {
                    path.Insert(0, subMachine.NestedStateMachine);
                    return true;
                }
            }

            return false;
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

            // Register for navigation requests (e.g., from preview window "Navigate" button)
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.NavigationRequested += OnNavigationRequested;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;

            if (_subscribedEditorState != null)
            {
                _subscribedEditorState.NavigationRequested -= OnNavigationRequested;
                _subscribedEditorState = null;
            }
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
                dependenciesPanelController?.SetContext(lastEditedStateMachine, rootStateMachine);
                
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

            // CRITICAL: Restore breadcrumb navigation state BEFORE setting EditorState.RootStateMachine
            // Setting RootStateMachine triggers PropertyChanged events that update breadcrumb,
            // which would overwrite the saved navigation state before we restore it
            breadcrumbBar?.RestoreNavigationState();

            // Get the current state machine from breadcrumb (which may have been restored)
            var currentFromBreadcrumb = breadcrumbBar?.CurrentStateMachine;
            if (currentFromBreadcrumb != null)
            {
                // Breadcrumb was restored successfully
                // Only set EditorState.RootStateMachine without triggering breadcrumb updates
                // The breadcrumb is already correct, we just need to sync EditorState

                // Temporarily unsubscribe breadcrumb controller to prevent duplicate entries
                breadcrumbController?.Unsubscribe();

                if (EditorState.Instance.RootStateMachine != rootStateMachine)
                {
                    EditorState.Instance.RootStateMachine = rootStateMachine;
                }

                // Restore CurrentViewStateMachine if we were navigated into a layer/sub-machine
                if (currentFromBreadcrumb != rootStateMachine)
                {
                    EditorState.Instance.CurrentViewStateMachine = currentFromBreadcrumb;
                }

                // Re-subscribe breadcrumb controller
                breadcrumbController?.Subscribe();

                // Load the current state machine without resetting breadcrumb
                LoadStateMachineInternal(currentFromBreadcrumb, updateBreadcrumb: false);

                // Restore view transform for the current state machine
                RestoreViewTransform(currentFromBreadcrumb);
                return;
            }

            // Breadcrumb restoration failed, fall back to setting EditorState.RootStateMachine
            if (rootStateMachine != null && EditorState.Instance.RootStateMachine != rootStateMachine)
            {
                EditorState.Instance.RootStateMachine = rootStateMachine;
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
 
            // Save view transform of previous state machine before switching to new root
            if (lastEditedStateMachine != null && lastEditedStateMachine != stateMachineAsset && stateMachineEditorView != null)
            {
                SaveViewTransform(lastEditedStateMachine);
            }

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

            // Save view transform of previous state machine before switching
            if (lastEditedStateMachine != null && lastEditedStateMachine != stateMachineAsset)
            {
                SaveViewTransform(lastEditedStateMachine);
            }

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
            dependenciesPanelController?.SetContext(stateMachineAsset, rootStateMachine);

            stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
            {
                StateMachineAsset = stateMachineAsset,
                StateNodeXml = StateNodeXml
            });

            // Restore view transform for the new state machine (if one exists)
            // Otherwise fall back to WaitAndFrameAll
            if (!RestoreViewTransform(stateMachineAsset))
            {
                WaitAndFrameAll();
            }
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
                        dependenciesPanelController?.SetContext(stateMachineDebug.StateMachineAsset, stateMachineDebug.StateMachineAsset);
                        
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

        #region View Transform Persistence

        private string GetViewTransformKey(StateMachineAsset stateMachine)
        {
            if (stateMachine == null) return null;
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(stateMachine);
            return $"DMotion.ViewTransform.{globalId}";
        }

        private void SaveViewTransform(StateMachineAsset stateMachine)
        {
            if (stateMachine == null || stateMachineEditorView == null) return;

            var key = GetViewTransformKey(stateMachine);
            if (string.IsNullOrEmpty(key)) return;

            var container = stateMachineEditorView.contentViewContainer;
            var translate = container.resolvedStyle.translate;
            var scale = container.resolvedStyle.scale;

            // Use invariant culture for consistent serialization across locales
            var transformString = string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2}", translate.x, translate.y, scale.value.x);
            SessionState.SetString(key, transformString);
        }
 
        private bool RestoreViewTransform(StateMachineAsset stateMachine)
        {
            if (stateMachine == null || stateMachineEditorView == null) return false;

            var key = GetViewTransformKey(stateMachine);
            if (string.IsNullOrEmpty(key)) return false;

            var transformString = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(transformString)) return false;

            var parts = transformString.Split(',');
            if (parts.Length != 3) return false;

            // Use invariant culture for parsing
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                var position = new Vector3(x, y, 0f);
                var scaleVec = new Vector3(scale, scale, 1f);
                stateMachineEditorView.UpdateViewTransform(position, scaleVec);
                return true;
            }

            return false;
        }

        #endregion
    }
}