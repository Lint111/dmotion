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
        private StateMachineInspectorView dependenciesInspectorView;
        private BreadcrumbBar breadcrumbBar;
        
        // Event-driven panel controllers
        private InspectorController inspectorController;
        private ParametersPanelController parametersPanelController;
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
            dependenciesPanelController?.Unsubscribe();
            breadcrumbController?.Unsubscribe();
            
            // Clear all event subscriptions to prevent memory leaks
            StateMachineEditorEvents.ClearAllSubscriptions();
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
            
            // Try to restore from serialized reference first
            if (lastEditedStateMachine != null)
            {
                LoadStateMachine(lastEditedStateMachine);
                return;
            }
            
            // Fall back to GUID if reference was lost
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