using System;
using ConvenientLogger;
using ConvenientLogger.Editor;
using DMotion.Authoring;
using CLogger = ConvenientLogger.Logger;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Context-sensitive preview window for animation states and transitions.
    /// Provides real-time visual feedback with 3D preview, timeline, and blend space visualization.
    /// </summary>
    internal partial class AnimationPreviewWindow : EditorWindow
    {
        private const string WindowTitle = "Animation Preview";
        private const string UssFileName = "AnimationPreviewWindow";
        private const float MinInspectorWidth = 380f;
        private const float MinPreviewWidth = 200f;
        private const float MinPreviewHeight = 200f;
        private const float DefaultSplitPosition = 300f;
        private const string PreviewModelPrefKeyPrefix = "DMotion.AnimationPreview.PreviewModel.";
        private const string PreviewModePrefKey = "DMotion.AnimationPreview.PreviewMode";

        #region Serialized State

        [SerializeField] private PlayableGraphPreview.CameraState savedCameraState;
        
        // Persisted across domain reloads
        [SerializeField] private StateMachineAsset serializedStateMachine;
        
        // Split position is stored in PreviewSettings singleton for cross-reload persistence
        private float SplitPosition
        {
            get => PreviewSettings.instance.SplitPosition;
            set => PreviewSettings.instance.SplitPosition = value;
        }

        #endregion

        #region UI Elements

        private TwoPaneSplitView splitView;
        private VisualElement inspectorPanel;
        private VisualElement previewPanel;
        private VisualElement inspectorContent;
        private IMGUIContainer previewContainer;
        private Label previewPlaceholder;
        private ToolbarMenu modeDropdown;
        private ToolbarMenu previewTypeDropdown;
        private Label selectionLabel;
        private ObjectField previewModelField;

        // Timeline (owned by StateInspectorBuilder, but we need reference for playback)
        private double lastUpdateTime;

        #endregion

        #region State

        private StateMachineAsset currentStateMachine;
        
        // Selection state - serialized to survive domain reload
        [SerializeField] private AnimationStateAsset selectedState;
        [SerializeField] private AnimationStateAsset selectedTransitionFrom;
        [SerializeField] private AnimationStateAsset selectedTransitionTo;
        [SerializeField] private bool isAnyStateSelected;
        [SerializeField] private SelectionType currentSelectionType = SelectionType.None;

        // Base state speed (from Speed slider), used to combine with weighted clip speed
        private float currentStateSpeed = 1f;

        // Store subscribed EditorState instance to handle domain reload correctly
        private EditorState _subscribedEditorState;
        
        // Logging - initialized via toolbar.AddLogger() in BuildUI
        private CLogger _logger;

        private enum SelectionType
        {
            None,
            State,
            Transition,
            AnyState,
            AnyStateTransition
        }
        
        private enum PreviewType
        {
            SingleState,
            LayerComposition
        }
        
        private enum InitializationState
        {
            Pending,                    // Waiting for CreateGUI
            WaitingForDependencies,     // UI ready, waiting for CompositionState (if LayerComposition mode)
            Ready                       // Fully initialized
        }
        
        private InitializationState _initState = InitializationState.Pending;

        #endregion

        #region Extracted Components

        private StateInspectorBuilder stateInspectorBuilder;
        private TransitionInspectorBuilder transitionInspectorBuilder;
        private LayerCompositionInspectorBuilder layerCompositionBuilder;
        private PreviewSession previewSession;
        private PreviewType currentPreviewType = PreviewType.SingleState;
        
        /// <summary>
        /// Gets the composition state from EditorState (shared across all editor windows).
        /// This ensures we always use the root state machine context for multi-layer preview.
        /// </summary>
        private ObservableCompositionState CompositionState => EditorState.Instance.CompositionState;

        #endregion

        #region Window Lifecycle

        [MenuItem(ToolMenuConstants.DMotionPath + "/Animation Preview")]
        internal static void ShowWindow()
        {
            var wnd = GetWindow<AnimationPreviewWindow>();
            wnd.titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("d_AnimatorController Icon").image);
            wnd.minSize = new Vector2(MinInspectorWidth + MinPreviewWidth + 10, 400);
        }
        
        /// <summary>
        /// Opens the Animation Preview window docked next to the State Machine Editor.
        /// </summary>
        internal static AnimationPreviewWindow ShowDockedWithStateMachineEditor()
        {
            // Try to dock next to the State Machine Editor window
            var wnd = GetWindow<AnimationPreviewWindow>(
                desiredDockNextTo: new[] { typeof(AnimationStateMachineEditorWindow) });
            
            wnd.titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("d_AnimatorController Icon").image);
            wnd.minSize = new Vector2(MinInspectorWidth + MinPreviewWidth + 10, 400);
            
            return wnd;
        }

        private void OnEnable()
        {
            _initState = InitializationState.Pending;
            
            // Subscribe to Play mode changes to handle preview rebuild
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
            // Subscribe to EditorState events
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.PropertyChanged += OnEditorStatePropertyChanged;
            _subscribedEditorState.StructureChanged += OnEditorStateStructureChanged;
            _subscribedEditorState.PreviewStateChanged += OnPreviewStateChanged;
            _subscribedEditorState.CompositionStateChanged += OnCompositionStatePropertyChanged;

            // Create extracted components
            CreateBuilders();
            
            // Create preview session with saved mode preference
            var savedMode = LoadSavedPreviewMode();
            previewSession = new PreviewSession(savedMode);
            
            // Restore saved camera state
            if (savedCameraState.IsValid)
            {
                previewSession.CameraState = savedCameraState;
            }
            
            // Note: Full initialization is deferred to TryCompleteInitialization()
            // Called from CreateGUI and Update to handle async dependency resolution
        }
        
        /// <summary>
        /// Syncs window state with the current EditorState.
        /// Called on enable to handle windows opening after state machine is already loaded.
        /// </summary>
        private void SyncWithEditorState()
        {
            var editorState = EditorState.Instance;
            
            // IMPORTANT: Sync preview type BEFORE SetContext() because SetContext has auto-switch logic
            // that checks currentPreviewType. If we sync after, the saved preference would be ignored.
            var editorPreviewType = editorState.PreviewType;
            currentPreviewType = editorPreviewType == EditorPreviewType.LayerComposition 
                ? PreviewType.LayerComposition 
                : PreviewType.SingleState;
            UpdatePreviewTypeDropdownText();
            
            // Sync state machine context (use RootStateMachine for proper multi-layer detection)
            // Fall back to serialized state machine if EditorState was reset (domain reload)
            var stateMachine = editorState.RootStateMachine ?? serializedStateMachine;
            if (stateMachine != null)
            {
                SetContext(stateMachine);
            }
            
            // Sync selection - EditorState takes priority, then fall back to serialized selection
            if (editorState.IsTransitionSelected)
            {
                // EditorState has a selection - use it
                selectedState = null;
                selectedTransitionFrom = editorState.SelectedTransitionFrom;
                selectedTransitionTo = editorState.SelectedTransitionTo;
                isAnyStateSelected = editorState.IsAnyStateSelected;
                currentSelectionType = isAnyStateSelected ? SelectionType.AnyStateTransition : SelectionType.Transition;
            }
            else if (editorState.SelectedState != null)
            {
                // EditorState has a state selection - use it
                selectedState = editorState.SelectedState;
                selectedTransitionFrom = null;
                selectedTransitionTo = null;
                isAnyStateSelected = false;
                currentSelectionType = SelectionType.State;
                currentStateSpeed = selectedState.Speed > 0 ? selectedState.Speed : 1f;
            }
            else if (editorState.IsAnyStateSelected)
            {
                // EditorState has AnyState selected
                selectedState = null;
                selectedTransitionFrom = null;
                selectedTransitionTo = null;
                isAnyStateSelected = true;
                currentSelectionType = SelectionType.AnyState;
            }
            else if (selectedState != null || selectedTransitionFrom != null || selectedTransitionTo != null)
            {
                // EditorState is empty (domain reload), but we have serialized selection - restore to EditorState
                if (currentSelectionType == SelectionType.State && selectedState != null)
                {
                    editorState.SelectedState = selectedState;
                    currentStateSpeed = selectedState.Speed > 0 ? selectedState.Speed : 1f;
                }
                else if (currentSelectionType == SelectionType.Transition && selectedTransitionFrom != null && selectedTransitionTo != null)
                {
                    editorState.SelectTransition(selectedTransitionFrom, selectedTransitionTo, false);
                }
                else if (currentSelectionType == SelectionType.AnyStateTransition && selectedTransitionTo != null)
                {
                    editorState.SelectTransition(null, selectedTransitionTo, true); 
                }
            }
            // Note: If nothing is selected anywhere, keep currentSelectionType as None
        }

        private void OnDisable()
        {
            // Unsubscribe from Play mode changes
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            
            // Unsubscribe from the stored EditorState instance
            if (_subscribedEditorState != null)
            {
                _subscribedEditorState.PropertyChanged -= OnEditorStatePropertyChanged;
                _subscribedEditorState.StructureChanged -= OnEditorStateStructureChanged;
                _subscribedEditorState.PreviewStateChanged -= OnPreviewStateChanged;
                _subscribedEditorState.CompositionStateChanged -= OnCompositionStatePropertyChanged;

                // Null check for CompositionState (can be null after domain reload)
                if (_subscribedEditorState.CompositionState != null)
                {
                    _subscribedEditorState.CompositionState.LayerChanged -= OnCompositionLayerChanged;
                }

                _subscribedEditorState = null;
            }

            // Save camera state before disposing
            if (previewSession != null)
            { 
                var camState = previewSession.CameraState;
                if (camState.IsValid)
                {
                    savedCameraState = camState;
                }
            }

            // Dispose resources
            stateInspectorBuilder?.Cleanup();
            transitionInspectorBuilder?.Cleanup();
            layerCompositionBuilder?.Cleanup();
            previewSession?.Dispose();
        }
        
        #endregion
        
        #region Logging
        
        // Logging convenience methods - logger is initialized in BuildUI via extension method
        private void LogTrace(string message) => _logger?.Trace(message);
        private void LogDebug(string message) => _logger?.Debug(message);
        private void LogInfo(string message) => _logger?.Info(message);
        private void LogWarning(string message) => _logger?.Warning(message);
        private void LogError(string message) => _logger?.Error(message);
        
        #endregion
        
        #region Play Mode Handling
        
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Exited Play mode - preview needs to be rebuilt
                // The backend's preview resources were destroyed during Play mode
                LogInfo("Exited Play mode - rebuilding preview");
                
                // Re-sync with EditorState to rebuild preview
                // Use delay to ensure Unity has finished restoring editor state
                EditorApplication.delayCall += () =>
                {
                    if (this == null) return; // Window might have been closed
                    
                    // Dispose and recreate preview session to ensure clean state
                    var savedCamState = previewSession?.CameraState ?? savedCameraState;
                    previewSession?.Dispose();
                    
                    var savedMode = LoadSavedPreviewMode();
                    previewSession = new PreviewSession(savedMode);
                    
                    if (savedCamState.IsValid)
                    {
                        previewSession.CameraState = savedCamState;
                    }
                    
                    // Re-sync with EditorState to restore context
                    SyncWithEditorState();
                    
                    // Reload preview model
                    LoadPreviewModelPreference();
                    
                    // Update UI
                    UpdateSelectionUI();
                    Repaint();
                };
            }
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("preview-window");
            root.focusable = true;

            // Load stylesheets
            var styleSheet = FindStyleSheet(UssFileName);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Load shared UI element styles (IconButton, etc.)
            var sharedStyleSheet = FindStyleSheet("StateInspector");
            if (sharedStyleSheet != null)
            {
                root.styleSheets.Add(sharedStyleSheet);
            }

            BuildUI(root);
            
            // Register window-level keyboard handler for consistent shortcuts
            root.RegisterCallback<KeyDownEvent>(OnWindowKeyDown);
            
            lastUpdateTime = EditorApplication.timeSinceStartup;
            
            // Start deferred initialization - UI is ready, now wait for dependencies
            _initState = InitializationState.WaitingForDependencies;
            TryCompleteInitialization();
        }
        
        /// <summary>
        /// Attempts to complete initialization by checking if all dependencies are ready.
        /// Called from CreateGUI and Update until initialization completes.
        /// </summary>
        private void TryCompleteInitialization()
        {
            if (_initState == InitializationState.Ready) return;
            if (_initState == InitializationState.Pending) return; // UI not ready yet
            
            // Sync basic state from EditorState
            var editorState = EditorState.Instance;
            var editorPreviewType = editorState.PreviewType;
            currentPreviewType = editorPreviewType == EditorPreviewType.LayerComposition 
                ? PreviewType.LayerComposition 
                : PreviewType.SingleState;
            
            // For LayerComposition mode, check if CompositionState is ready
            if (currentPreviewType == PreviewType.LayerComposition)
            {
                var compositionState = editorState.CompositionState;
                bool compositionReady = compositionState?.RootStateMachine != null && compositionState.LayerCount > 0;
                
                if (!compositionReady)
                {
                    // Still waiting - will retry in next Update
                    LogDebug("TryCompleteInitialization: Waiting for CompositionState...");
                    return;
                }
                
                // Subscribe to LayerChanged now that CompositionState is ready
                compositionState.LayerChanged -= OnCompositionLayerChanged; // Prevent double-subscribe
                compositionState.LayerChanged += OnCompositionLayerChanged;
                LogDebug("TryCompleteInitialization: CompositionState ready, subscribed to LayerChanged");
            }
            
            // All dependencies ready - complete initialization
            _initState = InitializationState.Ready;
            LogDebug("TryCompleteInitialization: Initialization complete");
            
            // Now perform full sync
            SyncWithEditorState();
            LoadPreviewModelPreference();
            UpdateSelectionUI();
        }

        private void Update()
        {
            // Calculate delta time
            var currentTime = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(currentTime - lastUpdateTime);
            lastUpdateTime = currentTime;
            
            // Continue initialization if waiting for dependencies
            if (_initState == InitializationState.WaitingForDependencies)
            {
                TryCompleteInitialization();
                // Don't process updates until fully initialized
                if (_initState != InitializationState.Ready) return;
            }

            bool needsRepaint = false;

            // Update smooth blend transitions (do this first so weights are updated)
            if (previewSession != null && previewSession.Tick(deltaTime))
            {
                needsRepaint = true;
            }
            
            // Update state timeline playback speed using state's encapsulated calculation
            var stateTimeline = stateInspectorBuilder?.TimelineScrubber;
            if (stateTimeline != null && selectedState != null)
            {
                var blendPos = PreviewSettings.GetBlendPosition(selectedState);
                stateTimeline.PlaybackSpeed = selectedState.GetEffectiveSpeed(blendPos);
            }

            // Update state timeline playback
            if (stateTimeline != null && stateTimeline.IsPlaying)
            {
                stateTimeline.Tick(deltaTime);
                needsRepaint = true;
            }
            
            // Update transition timeline playback speed using states' encapsulated calculations
            var transitionTimeline = transitionInspectorBuilder?.Timeline;
            if (transitionTimeline != null)
            {
                var fromBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionFrom);
                var toBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionTo);
                
                float fromSpeed = selectedTransitionFrom?.GetEffectiveSpeed(fromBlendPos) ?? 1f;
                float toSpeed = selectedTransitionTo?.GetEffectiveSpeed(toBlendPos) ?? 1f;
                
                // Lerp between from and to speeds based on transition progress
                float progress = transitionTimeline.TransitionProgress;
                transitionTimeline.PlaybackSpeed = Mathf.Lerp(fromSpeed, toSpeed, progress);
            }
            
            if (transitionInspectorBuilder != null && transitionInspectorBuilder.IsPlaying)
            {
                transitionInspectorBuilder.Tick(deltaTime);
                needsRepaint = true;
            }
            
            // Update layer composition playback
            if (layerCompositionBuilder != null && layerCompositionBuilder.IsPlaying)
            {
                layerCompositionBuilder.Tick(deltaTime);
                needsRepaint = true;
            }
            
            if (needsRepaint)
            {
                Repaint();
            }

            // Periodically save camera state (every few frames) to ensure persistence
            // This handles cases where OnDisable isn't called immediately when switching focus
            if (Time.frameCount % 30 == 0) // Save every 30 frames (~0.5 seconds at 60fps)
            {
                SaveCameraState();
            }
        }

        #endregion

        #region Builder Creation

        private void CreateBuilders()
        {
            stateInspectorBuilder = new StateInspectorBuilder();
            stateInspectorBuilder.OnTimeChanged += OnTimelineTimeChanged;
            stateInspectorBuilder.OnRepaintRequested += Repaint;
            stateInspectorBuilder.OnStateSpeedChanged += OnStateSpeedChanged;
            stateInspectorBuilder.OnPlayStateChanged += OnTimelinePlayStateChanged;
            
            transitionInspectorBuilder = new TransitionInspectorBuilder();
            transitionInspectorBuilder.OnTimeChanged += OnTransitionTimelineTimeChanged;
            transitionInspectorBuilder.OnRepaintRequested += Repaint;
            transitionInspectorBuilder.OnPlayStateChanged += OnTimelinePlayStateChanged;
            transitionInspectorBuilder.OnTransitionPropertiesChanged += OnTransitionPropertiesChanged;
            
            layerCompositionBuilder = new LayerCompositionInspectorBuilder();
            layerCompositionBuilder.OnTimeChanged += OnLayerCompositionTimeChanged;
            layerCompositionBuilder.OnRepaintRequested += Repaint;
            layerCompositionBuilder.OnPlayStateChanged += OnTimelinePlayStateChanged;
            layerCompositionBuilder.OnNavigateToLayer += OnNavigateToLayerRequested;
        }
        
        private void OnLayerCompositionTimeChanged(float time)
        {
            previewSession?.SetNormalizedTime(time);
            Repaint();
        }
        
        private void OnNavigateToLayerRequested(int layerIndex, LayerStateAsset layerAsset)
        {
            // Get the layer's current selection before navigating
            var layerState = EditorState.Instance.CompositionState?.GetLayer(layerIndex);

            if (layerState == null) return;

            // Use the centralized navigation system which handles:
            // - Finding the containing sub-state machine
            // - Building the breadcrumb path
            // - Loading the correct view
            // - Framing the selection
            if (layerState.IsTransitionMode && layerState.TransitionFrom != null && layerState.TransitionTo != null)
            {
                // Navigate to transition with full hierarchy traversal
                EditorState.Instance.RequestNavigateToTransition(
                    layerAsset, layerIndex,
                    layerState.TransitionFrom, layerState.TransitionTo);
            }
            else if (layerState.SelectedState != null)
            {
                // Navigate to state with full hierarchy traversal
                EditorState.Instance.RequestNavigateToState(
                    layerAsset, layerIndex,
                    layerState.SelectedState);
            }
        }
        
        private void OnTransitionTimelineTimeChanged(float time)
        {
            // The TransitionInspectorBuilder already raises the centralized event
            // This handler is for any local processing needed
            Repaint();
        }

        #endregion

        #region UI Construction

        private void BuildUI(VisualElement root)
        {
            // Toolbar
            var toolbar = new Toolbar();
            toolbar.AddToClassList("preview-toolbar");

            // Mode dropdown for switching between Authoring and ECS preview
            modeDropdown = new ToolbarMenu();
            UpdateModeDropdownText();
            modeDropdown.menu.AppendAction(
                "Authoring (PlayableGraph)", 
                _ => SetPreviewMode(PreviewMode.Authoring),
                action => previewSession?.Mode == PreviewMode.Authoring 
                    ? DropdownMenuAction.Status.Checked 
                    : DropdownMenuAction.Status.Normal);
            modeDropdown.menu.AppendAction(
                "ECS Runtime", 
                _ => SetPreviewMode(PreviewMode.EcsRuntime),
                action => previewSession?.Mode == PreviewMode.EcsRuntime 
                    ? DropdownMenuAction.Status.Checked 
                    : DropdownMenuAction.Status.Normal);
            modeDropdown.tooltip = "Switch between authoring preview (PlayableGraph) and runtime preview (ECS)";
            toolbar.Add(modeDropdown);

            // Preview type dropdown for switching between Single State and Layer Composition
            previewTypeDropdown = new ToolbarMenu();
            UpdatePreviewTypeDropdownText();
            previewTypeDropdown.menu.AppendAction(
                "Single State", 
                _ => SetPreviewType(PreviewType.SingleState),
                action => currentPreviewType == PreviewType.SingleState 
                    ? DropdownMenuAction.Status.Checked 
                    : DropdownMenuAction.Status.Normal);
            previewTypeDropdown.menu.AppendAction(
                "Layer Composition", 
                _ => SetPreviewType(PreviewType.LayerComposition),
                action => currentPreviewType == PreviewType.LayerComposition 
                    ? DropdownMenuAction.Status.Checked 
                    : DropdownMenuAction.Status.Normal);
            previewTypeDropdown.tooltip = "Switch between single state preview and multi-layer composition preview";
            toolbar.Add(previewTypeDropdown);

            // Spacer
            var spacer = new VisualElement();
            spacer.AddToClassList("toolbar-spacer");
            toolbar.Add(spacer);

            // Selection label
            selectionLabel = new Label("No Selection");
            selectionLabel.AddToClassList("selection-label");
            toolbar.Add(selectionLabel);
            
            // Logger icon - one line setup!
            _logger = toolbar.AddLogger("DMotion/AnimationPreview");

            root.Add(toolbar);

            // Main content with split view
            var mainContent = new VisualElement();
            mainContent.AddToClassList("main-content");

            // Create split view (horizontal: left = inspector, right = preview)
            splitView = new TwoPaneSplitView(0, SplitPosition, TwoPaneSplitViewOrientation.Horizontal);
            splitView.AddToClassList("main-split-view");

            // Left panel - Inspector
            inspectorPanel = new VisualElement();
            inspectorPanel.AddToClassList("inspector-panel");
            inspectorPanel.style.minWidth = MinInspectorWidth;

            var inspectorScroll = new ScrollView(ScrollViewMode.Vertical);
            inspectorScroll.AddToClassList("inspector-scroll");

            inspectorContent = new VisualElement();
            inspectorContent.AddToClassList("inspector-content");
            inspectorScroll.Add(inspectorContent);

            inspectorPanel.Add(inspectorScroll);

            // Right panel - Preview
            previewPanel = new VisualElement();
            previewPanel.AddToClassList("preview-panel");
            previewPanel.style.minWidth = MinPreviewWidth;
            previewPanel.style.minHeight = MinPreviewHeight;

            // Preview toolbar with controls
            var previewToolbar = new VisualElement();
            previewToolbar.AddToClassList("preview-toolbar-overlay");
            
            var resetViewButton = new Button(OnResetViewClicked) { text = "Reset View", tooltip = "Reset camera to default position" };
            resetViewButton.AddToClassList("preview-control-button");
            previewToolbar.Add(resetViewButton);
            
            // Preview model selection (bottom of preview panel)
            var modelSelectionBar = new VisualElement();
            modelSelectionBar.AddToClassList("preview-model-bar");
            
            var modelLabel = new Label("Preview Model:");
            modelLabel.AddToClassList("preview-model-label");
            modelSelectionBar.Add(modelLabel);
            
            previewModelField = new ObjectField();
            previewModelField.objectType = typeof(GameObject);
            previewModelField.allowSceneObjects = false;
            previewModelField.AddToClassList("preview-model-field");
            previewModelField.tooltip = "Drag a model prefab with Animator and SkinnedMeshRenderer";
            previewModelField.RegisterValueChangedCallback(OnPreviewModelChanged);
            modelSelectionBar.Add(previewModelField);

            // Preview placeholder (shown when no preview available)
            previewPlaceholder = new Label("No Preview Available\n\nSelect a state or transition\nin the State Machine Editor");
            previewPlaceholder.AddToClassList("preview-placeholder");

            // IMGUI container for 3D preview
            previewContainer = new IMGUIContainer(OnPreviewGUI);
            previewContainer.AddToClassList("preview-imgui");
            previewContainer.AddToClassList("preview-imgui--hidden");
            
            // Click on 3D preview focuses the timeline for keyboard shortcuts
            previewContainer.RegisterCallback<PointerDownEvent>(OnPreviewClicked);

            previewPanel.Add(previewPlaceholder);
            previewPanel.Add(previewContainer);
            previewPanel.Add(previewToolbar);
            previewPanel.Add(modelSelectionBar);

            // Add panels to split view
            splitView.Add(inspectorPanel);
            splitView.Add(previewPanel);

            // Track split position changes
            splitView.RegisterCallback<GeometryChangedEvent>(OnSplitViewGeometryChanged);

            mainContent.Add(splitView);
            root.Add(mainContent);
        }

        private void OnSplitViewGeometryChanged(GeometryChangedEvent evt)
        {
            // Save the split position when it changes (auto-saved via PreviewSettings)
            if (splitView != null && inspectorPanel != null)
            {
                var newPosition = inspectorPanel.resolvedStyle.width;
                if (newPosition > 0)
                {
                    SplitPosition = newPosition;
                }
            }
        }

        private static StyleSheet FindStyleSheet(string name)
        {
            var guids = AssetDatabase.FindAssets($"t:StyleSheet {name}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith($"{name}.uss"))
                {
                    return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }
            }
            return null;
        }

        #endregion
    }
}
