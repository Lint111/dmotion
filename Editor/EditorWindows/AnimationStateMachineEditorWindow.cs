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
        internal const string ToolsPath = "Tools";
        internal const string DMotionPath = ToolsPath + "/DMotion";
    }

    internal struct StateMachineEditorViewModel
    {
        internal StateMachineAsset StateMachineAsset;
        internal EntitySelectionProxyWrapper SelectedEntity;
        internal VisualTreeAsset StateNodeXml;
        internal StateMachineInspectorView InspectorView;
        internal StateMachineInspectorView ParametersInspectorView;
        internal StateMachineInspectorView DependenciesInspectorView;
    }

    internal class AnimationStateMachineEditorWindow : EditorWindow
    {
        [SerializeField] internal VisualTreeAsset StateMachineEditorXml;
        [SerializeField] internal VisualTreeAsset StateNodeXml;
        
        // Persisted across domain reloads
        [SerializeField] private StateMachineAsset lastEditedStateMachine;
        [SerializeField] private string lastEditedAssetGuid;

        private AnimationStateMachineEditorView stateMachineEditorView;
        private StateMachineInspectorView inspectorView;
        private StateMachineInspectorView parametersInspectorView;
        private StateMachineInspectorView dependenciesInspectorView;
        private BreadcrumbBar breadcrumbBar;
        private InspectorController inspectorController;
        private bool hasRestoredAfterDomainReload;

        [MenuItem(ToolMenuConstants.DMotionPath + "/State Machine Editor")]
        internal static void ShowExample()
        {
            var wnd = GetWindow<AnimationStateMachineEditorWindow>();
            wnd.titleContent = new GUIContent("State Machine Editor");
            wnd.OnSelectionChange();
        }

        private void Awake()
        {
            EditorApplication.playModeStateChanged += OnPlaymodeStateChanged;
        }

        private void OnDestroy()
        {
            EditorApplication.playModeStateChanged -= OnPlaymodeStateChanged;
            
            // Unsubscribe inspector controller
            inspectorController?.Unsubscribe();
            
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
                
                // Wire up breadcrumb navigation
                if (breadcrumbBar != null)
                {
                    breadcrumbBar.OnNavigate += OnBreadcrumbNavigate;
                }
                
                // Wire up graph view to notify when entering a sub-state machine
                if (stateMachineEditorView != null)
                {
                    stateMachineEditorView.OnEnterSubStateMachine += OnEnterSubStateMachine;
                }
                
                // Create inspector controller and subscribe to selection events
                if (inspectorView != null && stateMachineEditorView != null)
                {
                    inspectorController = new InspectorController(inspectorView, stateMachineEditorView);
                    inspectorController.Subscribe();
                }
            }
            
            // Restore last edited asset after domain reload
            RestoreAfterDomainReload();
        }
        
        private void OnBreadcrumbNavigate(int index)
        {
            var target = breadcrumbBar?.NavigationStack[index];
            if (target != null)
            {
                LoadStateMachineInternal(target, updateBreadcrumb: false);
            }
        }
        
        private void OnEnterSubStateMachine(StateMachineAsset nestedMachine)
        {
            if (nestedMachine == null) return;
            breadcrumbBar?.Push(nestedMachine);
            LoadStateMachineInternal(nestedMachine, updateBreadcrumb: false);
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
                
                // Repopulate without framing
                stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
                {
                    StateMachineAsset = lastEditedStateMachine,
                    StateNodeXml = StateNodeXml,
                    InspectorView = inspectorView,
                    ParametersInspectorView = parametersInspectorView,
                    DependenciesInspectorView = dependenciesInspectorView
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
            
            // Reset breadcrumb to new root
            breadcrumbBar?.SetRoot(stateMachineAsset);
            
            LoadStateMachineInternal(stateMachineAsset, updateBreadcrumb: false);
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
            
            // Update inspector controller context
            inspectorController?.SetContext(stateMachineAsset);
            
            stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
            {
                StateMachineAsset = stateMachineAsset,
                StateNodeXml = StateNodeXml,
                InspectorView = inspectorView,
                ParametersInspectorView = parametersInspectorView,
                DependenciesInspectorView = dependenciesInspectorView
            });
            WaitAndFrameAll();
        }

        [OnOpenAsset]
        internal static bool OnOpenBehaviourTree(int instanceId, int line)
        {
            if (Selection.activeObject is StateMachineAsset)
            {
                ShowExample();
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
                        
                        stateMachineEditorView.PopulateView(new StateMachineEditorViewModel
                        {
                            StateMachineAsset = stateMachineDebug.StateMachineAsset,
                            SelectedEntity = entitySelectionProxy,
                            StateNodeXml = StateNodeXml,
                            InspectorView = inspectorView,
                            ParametersInspectorView = parametersInspectorView,
                            DependenciesInspectorView = dependenciesInspectorView
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