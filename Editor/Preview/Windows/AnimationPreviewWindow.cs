using System;
using DMotion.Authoring;
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
    internal class AnimationPreviewWindow : EditorWindow
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
        private AnimationStateAsset selectedState;
        private AnimationStateAsset selectedTransitionFrom;
        private AnimationStateAsset selectedTransitionTo;
        private bool isAnyStateSelected;
        private SelectionType currentSelectionType = SelectionType.None;

        // Base state speed (from Speed slider), used to combine with weighted clip speed
        private float currentStateSpeed = 1f;

        // Store subscribed EditorState instance to handle domain reload correctly
        private EditorState _subscribedEditorState;

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
            // Subscribe to EditorState events
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.PropertyChanged += OnEditorStatePropertyChanged;
            _subscribedEditorState.StructureChanged += OnEditorStateStructureChanged;
            _subscribedEditorState.PreviewStateChanged += OnPreviewStateChanged;
            _subscribedEditorState.CompositionStateChanged += OnCompositionStatePropertyChanged;
            _subscribedEditorState.CompositionState.LayerChanged += OnCompositionLayerChanged;
            
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
            
            // Sync with current EditorState (window may open after state machine is already set)
            SyncWithEditorState();
        }
        
        /// <summary>
        /// Syncs window state with the current EditorState.
        /// Called on enable to handle windows opening after state machine is already loaded.
        /// </summary>
        private void SyncWithEditorState()
        {
            var editorState = EditorState.Instance;
            
            // Sync state machine context (use RootStateMachine for proper multi-layer detection)
            // Fall back to serialized state machine if EditorState was reset (domain reload)
            var stateMachine = editorState.RootStateMachine ?? serializedStateMachine;
            if (stateMachine != null)
            {
                SetContext(stateMachine);
            }
            
            // Sync preview type from EditorState (this is the authoritative source)
            var editorPreviewType = editorState.PreviewType;
            currentPreviewType = editorPreviewType == EditorPreviewType.LayerComposition 
                ? PreviewType.LayerComposition 
                : PreviewType.SingleState;
            UpdatePreviewTypeDropdownText();
            
            // Sync selection
            if (editorState.IsTransitionSelected)
            {
                selectedState = null;
                selectedTransitionFrom = editorState.SelectedTransitionFrom;
                selectedTransitionTo = editorState.SelectedTransitionTo;
                isAnyStateSelected = editorState.IsAnyStateSelected;
                currentSelectionType = isAnyStateSelected ? SelectionType.AnyStateTransition : SelectionType.Transition;
            }
            else if (editorState.SelectedState != null)
            {
                selectedState = editorState.SelectedState;
                selectedTransitionFrom = null;
                selectedTransitionTo = null;
                isAnyStateSelected = false;
                currentSelectionType = SelectionType.State;
                currentStateSpeed = selectedState.Speed > 0 ? selectedState.Speed : 1f;
            }
            else if (editorState.IsAnyStateSelected)
            {
                selectedState = null;
                selectedTransitionFrom = null;
                selectedTransitionTo = null;
                isAnyStateSelected = true;
                currentSelectionType = SelectionType.AnyState;
            }
            else
            {
                ClearSelection();
            }
        }

        private void OnDisable()
        {
            // Unsubscribe from the stored EditorState instance
            if (_subscribedEditorState != null)
            {
                _subscribedEditorState.PropertyChanged -= OnEditorStatePropertyChanged;
                _subscribedEditorState.StructureChanged -= OnEditorStateStructureChanged;
                _subscribedEditorState.PreviewStateChanged -= OnPreviewStateChanged;
                _subscribedEditorState.CompositionStateChanged -= OnCompositionStatePropertyChanged;
                _subscribedEditorState.CompositionState.LayerChanged -= OnCompositionLayerChanged;
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
            
            // Load saved preview model for current state machine (if any)
            LoadPreviewModelPreference();
            
            UpdateSelectionUI();
            
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void Update()
        {
            // Calculate delta time
            var currentTime = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(currentTime - lastUpdateTime);
            lastUpdateTime = currentTime;

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
            var previewState = layerState?.PreviewState;

            if (previewState == null) return;

            // Use the centralized navigation system which handles:
            // - Finding the containing sub-state machine
            // - Building the breadcrumb path
            // - Loading the correct view
            // - Framing the selection
            if (previewState.IsTransitionMode && previewState.TransitionFrom != null && previewState.TransitionTo != null)
            {
                // Navigate to transition with full hierarchy traversal
                EditorState.Instance.RequestNavigateToTransition(
                    layerAsset, layerIndex,
                    previewState.TransitionFrom, previewState.TransitionTo);
            }
            else if (previewState.SelectedState != null)
            {
                // Navigate to state with full hierarchy traversal
                EditorState.Instance.RequestNavigateToState(
                    layerAsset, layerIndex,
                    previewState.SelectedState);
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
        
        #region Preview Mode
        
        private void SetPreviewMode(PreviewMode mode)
        {
            if (previewSession == null) return;
            if (previewSession.Mode == mode) return;
            
            previewSession.Mode = mode;
            UpdateModeDropdownText();
            
            // Persist the selection
            EditorPrefs.SetInt(PreviewModePrefKey, (int)mode);
            
            // Recreate preview with new mode
            UpdateSelectionUI();
            Repaint();
        }
        
        private void UpdateModeDropdownText()
        {
            if (modeDropdown == null) return;
            
            var mode = previewSession?.Mode ?? PreviewMode.Authoring;
            modeDropdown.text = mode switch
            {
                PreviewMode.Authoring => "Authoring",
                PreviewMode.EcsRuntime => "ECS Runtime",
                _ => "Preview"
            };
        }
        
        private PreviewMode LoadSavedPreviewMode()
        {
            var savedMode = EditorPrefs.GetInt(PreviewModePrefKey, (int)PreviewMode.Authoring);
            return (PreviewMode)savedMode;
        }
        
        #endregion
        
        #region Preview Type
        
        private void SetPreviewType(PreviewType type)
        {
            if (currentPreviewType == type) return;
            
            // Check if layer composition is valid
            if (type == PreviewType.LayerComposition)
            {
                // Allow layer composition if:
                // 1. Current state machine is multi-layer (opening a new multi-layer machine), OR
                // 2. We're already in a multi-layer context (EditorState tracks the root)
                bool isMultiLayerContext = currentStateMachine?.IsMultiLayer == true ||
                                           (CompositionState?.RootStateMachine != null && 
                                            CompositionState.RootStateMachine.IsMultiLayer);
                
                if (!isMultiLayerContext)
                {
                    Debug.LogWarning("[AnimationPreview] Layer Composition preview requires a multi-layer state machine.");
                    return;
                }
            }
            
            currentPreviewType = type;
            UpdatePreviewTypeDropdownText();
            
            // Note: CompositionState is managed by EditorState - it's automatically initialized
            // when a multi-layer state machine is set as RootStateMachine
            
            // Update the UI to reflect the new preview type
            UpdateSelectionUI();
            Repaint();
        }
        
        private void UpdatePreviewTypeDropdownText()
        {
            if (previewTypeDropdown == null) return;
            
            previewTypeDropdown.text = currentPreviewType switch
            {
                PreviewType.SingleState => "Single State",
                PreviewType.LayerComposition => "Layer Composition",
                _ => "Preview Type"
            };
            
            // Update visibility based on root context (not current view)
            // Show dropdown if either current machine is multi-layer OR we're in a multi-layer context
            bool isMultiLayerContext = currentStateMachine?.IsMultiLayer == true ||
                                       (CompositionState?.RootStateMachine != null && 
                                        CompositionState.RootStateMachine.IsMultiLayer);
            
            previewTypeDropdown.style.display = isMultiLayerContext 
                ? DisplayStyle.Flex 
                : DisplayStyle.None;
        }
        
        #endregion
        
        #region Composition State Event Handling
        
        private void OnCompositionStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            if (currentPreviewType != PreviewType.LayerComposition) return;
            
            switch (e.PropertyName)
            {
                case nameof(ObservableCompositionState.MasterTime):
                    previewSession?.SetNormalizedTime(CompositionState.MasterTime);
                    Repaint();
                    break;
                    
                case nameof(ObservableCompositionState.IsPlaying):
                    previewSession?.SetPlaying(CompositionState.IsPlaying);
                    Repaint();
                    break;
                    
                case nameof(ObservableCompositionState.SyncLayers):
                    // Sync state changed - may need to update layer times
                    Repaint();
                    break;
            }
        }
        
        private void OnCompositionLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            if (currentPreviewType != PreviewType.LayerComposition) return;
            
            // Layer property changed - refresh the builder UI
            switch (e.PropertyName)
            {
                case nameof(ObservableLayerState.SelectedState):
                case nameof(ObservableLayerState.Weight):
                case nameof(ObservableLayerState.IsEnabled):
                case nameof(ObservableLayerState.BlendPosition):
                case nameof(ObservableLayerState.TransitionProgress):
                    layerCompositionBuilder?.Refresh();
                    break;
            }
            
            Repaint();
        }
        
        /// <summary>
        /// Ensures the layer composition preview exists in the backend.
        /// Must be called BEFORE building the inspector UI.
        /// </summary>
        private void EnsureLayerCompositionPreview()
        {
            if (currentPreviewType != PreviewType.LayerComposition) return;
            if (CompositionState?.RootStateMachine == null) return;
            
            // Create layer composition preview in the backend
            var backend = previewSession?.Backend as PlayableGraphBackend;
            backend?.CreateLayerCompositionPreview(CompositionState.RootStateMachine);
        }
        
        #endregion
        
        #region EditorState Event Handlers
        
        private void OnEditorStatePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(EditorState.RootStateMachine):
                    // Root state machine changed - new asset opened
                    SetContext(EditorState.Instance.RootStateMachine);
                    ClearSelection();
                    UpdateSelectionUI();
                    break;
                    
                case nameof(EditorState.PreviewType):
                    // Sync preview type from EditorState
                    var editorPreviewType = EditorState.Instance.PreviewType;
                    var newPreviewType = editorPreviewType == EditorPreviewType.LayerComposition 
                        ? PreviewType.LayerComposition 
                        : PreviewType.SingleState;
                    if (currentPreviewType != newPreviewType)
                    {
                        currentPreviewType = newPreviewType;
                        UpdatePreviewTypeDropdownText();
                        UpdateSelectionUI();
                    }
                    break;
                    
                case nameof(EditorState.SelectedState):
                    // Selection changed - no need to SetContext, root doesn't change
                    var state = EditorState.Instance.SelectedState;
                    selectedState = state;
                    selectedTransitionFrom = null;
                    selectedTransitionTo = null;
                    isAnyStateSelected = false;
                    currentSelectionType = state != null ? SelectionType.State : SelectionType.None;
                    currentStateSpeed = state != null && state.Speed > 0 ? state.Speed : 1f;
                    UpdateSelectionUI();
                    break;
                    
                case nameof(EditorState.SelectedTransitionFrom):
                case nameof(EditorState.SelectedTransitionTo):
                case nameof(EditorState.IsTransitionSelected):
                    if (EditorState.Instance.IsTransitionSelected)
                    {
                        selectedState = null;
                        selectedTransitionFrom = EditorState.Instance.SelectedTransitionFrom;
                        selectedTransitionTo = EditorState.Instance.SelectedTransitionTo;
                        isAnyStateSelected = EditorState.Instance.IsAnyStateSelected;
                        currentSelectionType = isAnyStateSelected ? SelectionType.AnyStateTransition : SelectionType.Transition;
                        UpdateSelectionUI();
                    }
                    break;
                    
                case nameof(EditorState.IsAnyStateSelected):
                    if (EditorState.Instance.IsAnyStateSelected && !EditorState.Instance.IsTransitionSelected)
                    {
                        selectedState = null;
                        selectedTransitionFrom = null;
                        selectedTransitionTo = null;
                        isAnyStateSelected = true;
                        currentSelectionType = SelectionType.AnyState;
                        UpdateSelectionUI();
                    }
                    break;
                    
                case nameof(EditorState.HasSelection):
                    if (!EditorState.Instance.HasSelection)
                    {
                        ClearSelection();
                        UpdateSelectionUI();
                    }
                    break;
            }
        }
        
        private void OnEditorStateStructureChanged(object sender, StructureChangedEventArgs e)
        {
            if (e.ChangeType == StructureChangeType.GeneralChange && 
                EditorState.Instance.RootStateMachine == currentStateMachine)
            {
                // Refresh the UI in case the selected element was modified
                UpdateSelectionUI();
            }
        }
        
        private void OnPreviewStateChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            var previewState = EditorState.Instance.PreviewState;
            
            switch (e.PropertyName)
            {
                case nameof(ObservablePreviewState.NormalizedTime):
                    // Time changed via EditorState
                    Repaint();
                    break;
                    
                case nameof(ObservablePreviewState.BlendPosition):
                    OnBlendPositionChanged(previewState.SelectedState, previewState.BlendPosition);
                    break;
                    
                case nameof(ObservablePreviewState.ToBlendPosition):
                    OnTransitionToBlendPositionChangedInternal(previewState.TransitionTo, 
                        new UnityEngine.Vector2(previewState.ToBlendPosition.x, previewState.ToBlendPosition.y));
                    break;
                    
                case nameof(ObservablePreviewState.TransitionProgress):
                    OnTransitionProgressChangedInternal(previewState.TransitionFrom, previewState.TransitionTo, previewState.TransitionProgress);
                    break;
                    
                case nameof(ObservablePreviewState.SoloClipIndex):
                    OnClipSelectedForPreviewInternal(previewState.SelectedState, previewState.SoloClipIndex);
                    break;
            }
        }

        #endregion
        
        #region Selection Event Handlers (Internal)

        private void SetContext(StateMachineAsset machine)
        {
            if (machine != null && machine != currentStateMachine)
            {
                currentStateMachine = machine;
                serializedStateMachine = machine; // Persist for domain reload
                
                // Check if we're in a multi-layer context by looking at the composition state's root
                // This handles the case where we're navigating inside layers of a multi-layer machine
                bool isInMultiLayerContext = CompositionState?.RootStateMachine != null && 
                                              CompositionState.RootStateMachine.IsMultiLayer;
                
                // Update preview type dropdown visibility based on root context
                UpdatePreviewTypeDropdownText();
                
                // Auto-switch to appropriate preview type
                if (machine.IsMultiLayer && currentPreviewType == PreviewType.SingleState)
                {
                    // Opening a new multi-layer machine - switch to layer composition
                    SetPreviewType(PreviewType.LayerComposition);
                }
                else if (!machine.IsMultiLayer && currentPreviewType == PreviewType.LayerComposition && !isInMultiLayerContext)
                {
                    // Only switch to single state if we're NOT navigating within a multi-layer context
                    // (i.e., this is a completely different single-layer state machine)
                    SetPreviewType(PreviewType.SingleState);
                }
                
                // Note: CompositionState is automatically initialized by EditorState 
                // when a multi-layer state machine is set as RootStateMachine
                
                // Load saved preview model for this state machine
                LoadPreviewModelPreference();
            }
        }
        
        private void SavePreviewModelPreference(GameObject model)
        {
            if (currentStateMachine == null) return;
            
            var assetPath = AssetDatabase.GetAssetPath(currentStateMachine);
            if (string.IsNullOrEmpty(assetPath)) return;
            
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var prefKey = PreviewModelPrefKeyPrefix + guid;
            
            if (model != null)
            {
                var modelPath = AssetDatabase.GetAssetPath(model);
                EditorPrefs.SetString(prefKey, modelPath);
            }
            else
            {
                EditorPrefs.DeleteKey(prefKey);
            }
        }
        
        private void LoadPreviewModelPreference()
        {
            if (currentStateMachine == null)
            {
                UpdatePreviewModelField(null);
                return;
            }
            
            var assetPath = AssetDatabase.GetAssetPath(currentStateMachine);
            if (string.IsNullOrEmpty(assetPath))
            {
                UpdatePreviewModelField(null);
                return;
            }
            
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var prefKey = PreviewModelPrefKeyPrefix + guid;
            
            var modelPath = EditorPrefs.GetString(prefKey, null);
            if (!string.IsNullOrEmpty(modelPath))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                UpdatePreviewModelField(model);
            }
            else
            {
                UpdatePreviewModelField(null);
            }
        }
        
        private void UpdatePreviewModelField(GameObject model)
        {
            if (previewModelField != null)
            {
                previewModelField.SetValueWithoutNotify(model);
            }
            
            if (previewSession != null)
            {
                previewSession.PreviewModel = model;
            }
        }

        private void ClearSelection()
        {
            selectedState = null;
            selectedTransitionFrom = null;
            selectedTransitionTo = null;
            isAnyStateSelected = false;
            currentSelectionType = SelectionType.None;
        }

        #endregion

        #region UI Updates

        private void UpdateSelectionUI()
        {
            UpdateSelectionLabel();
            
            // Handle preview creation based on preview type
            // IMPORTANT: Create backend preview BEFORE building inspector UI
            if (currentPreviewType == PreviewType.LayerComposition)
            {
                // Layer composition mode - create multi-layer preview in backend FIRST
                // (inspector needs the preview to be created)
                EnsureLayerCompositionPreview();
            }
            else
            {
                // Single state mode - create single state/transition preview
                // Note: PreviewSession reads initial blend positions from PreviewSettings internally
                if (currentSelectionType == SelectionType.State && selectedState != null)
                {
                    previewSession.CreatePreviewForState(selectedState);
                }
                else if (currentSelectionType == SelectionType.Transition || currentSelectionType == SelectionType.AnyStateTransition)
                {
                    CreateTransitionPreviewForSelection();
                }
                else
                {
                    var message = currentSelectionType switch
                    {
                        SelectionType.AnyState => 
                            "Select a state to\npreview animation",
                        _ => null
                    };
                    
                    if (message != null)
                        previewSession.SetMessage(message);
                    else
                        previewSession.Clear();
                }
            }
            
            // Build inspector UI AFTER preview is created
            UpdateInspectorContent();
            UpdatePreviewVisibility();
        }

        private void UpdateSelectionLabel()
        {
            if (selectionLabel == null) return;

            if (currentPreviewType == PreviewType.LayerComposition)
            {
                // Show root state machine name when in layer composition mode
                var rootName = CompositionState?.RootStateMachine?.name ?? currentStateMachine?.name;
                selectionLabel.text = rootName ?? "Layer Composition";
            }
            else
            {
                selectionLabel.text = currentSelectionType switch
                {
                    SelectionType.State => selectedState?.name ?? "Unknown State",
                    SelectionType.Transition => $"{selectedTransitionFrom?.name ?? "?"} -> {selectedTransitionTo?.name ?? "?"}",
                    SelectionType.AnyState => "Any State",
                    SelectionType.AnyStateTransition => $"Any State -> {selectedTransitionTo?.name ?? "?"}",
                    _ => "No Selection"
                };
            }
        }

        private void UpdateInspectorContent()
        {
            if (inspectorContent == null) return;
            
            // Cleanup previous builders
            stateInspectorBuilder?.Cleanup();
            transitionInspectorBuilder?.Cleanup();
            layerCompositionBuilder?.Cleanup();
            
            inspectorContent.Clear();

            if (currentPreviewType == PreviewType.LayerComposition)
            {
                BuildLayerCompositionInspector();
            }
            else
            {
                switch (currentSelectionType)
                {
                    case SelectionType.State:
                        BuildStateInspector();
                        break;
                    case SelectionType.Transition:
                    case SelectionType.AnyStateTransition:
                        BuildTransitionInspector();
                        break;
                    case SelectionType.AnyState:
                        BuildAnyStateInspector();
                        break;
                    default:
                        BuildNoSelectionInspector();
                        break;
                }
            }
        }

        private void BuildNoSelectionInspector()
        {
            var message = new Label("Select a state or transition in the State Machine Editor to preview.");
            message.AddToClassList("no-selection-message");
            inspectorContent.Add(message);
        }

        private void BuildStateInspector()
        {
            if (selectedState == null || stateInspectorBuilder == null) return;
            
            var content = stateInspectorBuilder.Build(currentStateMachine, selectedState);
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void BuildTransitionInspector()
        {
            if (transitionInspectorBuilder == null) return;
            
            var content = transitionInspectorBuilder.Build(
                currentStateMachine,
                selectedTransitionFrom,
                selectedTransitionTo,
                isAnyStateSelected);
            
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void BuildAnyStateInspector()
        {
            if (transitionInspectorBuilder == null) return;
            
            var content = transitionInspectorBuilder.BuildAnyState();
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void BuildLayerCompositionInspector()
        {
            if (layerCompositionBuilder == null) return;
            
            // Cleanup previous build
            layerCompositionBuilder.Cleanup();
            
            // Get the layer composition preview from the backend
            var backend = previewSession?.Backend as PlayableGraphBackend;
            var layerPreview = backend?.LayerComposition;
            
            // Build the inspector UI using the builder pattern
            var content = layerCompositionBuilder.Build(
                CompositionState?.RootStateMachine ?? currentStateMachine,
                CompositionState,
                layerPreview);
            
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void CreateTransitionPreviewForSelection()
        {
            if (selectedTransitionTo == null)
            {
                previewSession.SetMessage("No target state\nfor transition");
                return;
            }
            
            // Find the transition to get its duration
            float transitionDuration = 0.25f; // Default
            
            if (isAnyStateSelected && currentStateMachine != null)
            {
                // Find in AnyStateTransitions
                foreach (var t in currentStateMachine.AnyStateTransitions)
                {
                    if (t.ToState == selectedTransitionTo)
                    {
                        transitionDuration = t.TransitionDuration;
                        break;
                    }
                }
            }
            else if (selectedTransitionFrom != null)
            {
                // Find in OutTransitions
                foreach (var t in selectedTransitionFrom.OutTransitions)
                {
                    if (t.ToState == selectedTransitionTo)
                    {
                        transitionDuration = t.TransitionDuration;
                        break;
                    }
                }
            }
            
            // For Any State transitions, fromState is null
            var fromState = isAnyStateSelected ? null : selectedTransitionFrom;
            previewSession.CreateTransitionPreview(fromState, selectedTransitionTo, transitionDuration);
        }
        
        private void UpdatePreviewVisibility()
        {
            if (previewPlaceholder == null || previewContainer == null) return;

            bool hasPreview = previewSession?.HasContent ?? false;

            previewPlaceholder.EnableInClassList("preview-placeholder--hidden", hasPreview);
            previewContainer.EnableInClassList("preview-imgui--hidden", !hasPreview);
        }

        #endregion

        #region Keyboard and Input Handling
        
        private void OnWindowKeyDown(KeyDownEvent evt)
        {
            // Forward keyboard events to the active timeline for consistent behavior
            // This ensures shortcuts work regardless of focus state
            
            // Handle layer composition mode separately
            if (currentPreviewType == PreviewType.LayerComposition)
            {
                HandleLayerCompositionKeyDown(evt);
                return;
            }
            
            TimelineBase activeTimeline = currentSelectionType switch
            {
                SelectionType.State => stateInspectorBuilder?.TimelineScrubber,
                SelectionType.Transition or SelectionType.AnyStateTransition => transitionInspectorBuilder?.Timeline,
                _ => null
            };
            
            if (activeTimeline == null) return;
            
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    activeTimeline.TogglePlayPause();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.LeftArrow:
                    activeTimeline.StepBackward();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    activeTimeline.StepForward();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Home:
                    activeTimeline.GoToStart();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.End:
                    activeTimeline.GoToEnd();
                    evt.StopPropagation();
                    break;
            }
        }
        
        private void HandleLayerCompositionKeyDown(KeyDownEvent evt)
        {
            if (layerCompositionBuilder == null || CompositionState == null) return;
            
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    // Toggle global playback
                    CompositionState.IsPlaying = !CompositionState.IsPlaying;
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.LeftArrow:
                    // Step backward (decrease master time)
                    CompositionState.MasterTime = Mathf.Max(0f, CompositionState.MasterTime - 0.01f);
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    // Step forward (increase master time)
                    CompositionState.MasterTime = Mathf.Min(1f, CompositionState.MasterTime + 0.01f);
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Home:
                    // Go to start
                    CompositionState.MasterTime = 0f;
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.End:
                    // Go to end
                    CompositionState.MasterTime = 1f;
                    evt.StopPropagation();
                    break;
            }
        }
        
        private void OnPreviewClicked(PointerDownEvent evt)
        {
            // Focus the appropriate timeline when clicking on the 3D preview
            // This enables keyboard shortcuts after interacting with the preview
            
            TimelineBase activeTimeline = currentSelectionType switch
            {
                SelectionType.State => stateInspectorBuilder?.TimelineScrubber,
                SelectionType.Transition or SelectionType.AnyStateTransition => transitionInspectorBuilder?.Timeline,
                _ => null
            };
            
            activeTimeline?.Focus();
        }
        
        #endregion

        #region Timeline Events

        private void OnTimelineTimeChanged(float time)
        {
            // Update preview sample time
            var timelineScrubber = stateInspectorBuilder?.TimelineScrubber;
            previewSession?.SetNormalizedTime(timelineScrubber?.NormalizedTime ?? 0);
            
            // Update EditorState for other listeners
            if (selectedState != null)
            {
                EditorState.Instance.PreviewState.NormalizedTime = timelineScrubber?.NormalizedTime ?? 0;
            }
            
            Repaint();
        }
        
        private void OnStateSpeedChanged(float newSpeed)
        {
            // Store the base state speed - will be combined with weighted clip speed in Update
            currentStateSpeed = newSpeed > 0 ? newSpeed : 1f;
        }
        
        private void OnTimelinePlayStateChanged(bool isPlaying)
        {
            // Forward play state to preview session (important for ECS mode)
            previewSession?.SetPlaying(isPlaying);
        }

        #endregion
        
        #region Blend Position Events
        
        private void OnBlendPositionChanged(AnimationStateAsset state, Unity.Mathematics.float2 blendPosition)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            if (state is LinearBlendStateAsset)
            {
                previewSession?.SetBlendPosition1D(blendPosition.x);
            }
            else
            {
                previewSession?.SetBlendPosition2D(blendPosition);
            }
            Repaint();
        }
        
        private void OnClipSelectedForPreviewInternal(AnimationStateAsset state, int clipIndex)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            // Set solo clip mode: -1 = blended, >= 0 = individual clip
            previewSession?.SetSoloClip(clipIndex);
            Repaint();
        }
        
        #endregion
        
        #region Transition Events
        
        private void OnTransitionProgressChangedInternal(AnimationStateAsset fromState, AnimationStateAsset toState, float progress)
        {
            // Only update if this matches the currently selected transition
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // For Any State transitions, fromState will be null
            bool matchesFrom = (isAnyStateSelected && fromState == null) || (fromState == selectedTransitionFrom);
            bool matchesTo = toState == selectedTransitionTo;
            
            if (!matchesFrom || !matchesTo) return;
            
            previewSession?.SetTransitionProgress(progress);
            Repaint();
        }
        
        private void OnTransitionFromBlendPositionChangedInternal(AnimationStateAsset fromState, Vector2 blendPosition)
        {
            // Only update if we're in a transition and this is the from state
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // Check if this matches the from state of the current transition
            var currentFromState = isAnyStateSelected ? null : selectedTransitionFrom;
            if (fromState != currentFromState) return;
            
            previewSession?.SetTransitionFromBlendPosition(new Unity.Mathematics.float2(blendPosition.x, blendPosition.y));
            
            // Update timeline durations to reflect new blend position
            var toBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionTo);
            transitionInspectorBuilder?.Timeline?.UpdateDurationsForBlendPosition(blendPosition, toBlendPos);
            
            Repaint();
        }
        
        private void OnTransitionToBlendPositionChangedInternal(AnimationStateAsset toState, Vector2 blendPosition)
        {
            // Only update if we're in a transition and this is the to state
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // Check if this matches the to state of the current transition
            if (toState != selectedTransitionTo) return;
            
            previewSession?.SetTransitionToBlendPosition(new Unity.Mathematics.float2(blendPosition.x, blendPosition.y));
            
            // Update timeline durations to reflect new blend position
            var fromBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionFrom);
            transitionInspectorBuilder?.Timeline?.UpdateDurationsForBlendPosition(fromBlendPos, blendPosition);
            
            Repaint();
        }
        
        private void OnTransitionPropertiesChanged()
        {
            // Transition duration or exit time changed - rebuild ECS timeline
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // Get current blend positions and trigger rebuild
            var fromBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionTo);
            
            previewSession?.RebuildTransitionTimeline(
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }
        
        #endregion

        #region Preview Rendering

        private void OnPreviewGUI()
        {
            if (previewSession == null) return;
            
            var rect = previewContainer.contentRect;
            
            // Sync time from state timeline scrubber
            // Only send time when paused/scrubbing - when playing, ECS advances time automatically
            var stateTimeline = stateInspectorBuilder?.TimelineScrubber;
            if (stateTimeline != null && currentSelectionType == SelectionType.State)
            {
                // Only sync when not playing (paused or scrubbing) to avoid ECS pause-on-scrub behavior
                if (!stateTimeline.IsPlaying || stateTimeline.IsDragging)
                {
                    previewSession.SetNormalizedTime(stateTimeline.NormalizedTime);
                }
            }
            
            // Sync time and progress from transition timeline
            var transitionTimeline = transitionInspectorBuilder?.Timeline;
            if (transitionTimeline != null && previewSession.IsTransitionPreview)
            {
                // Only sync when not playing (paused or scrubbing) to avoid ECS pause-on-scrub behavior
                // Both SetNormalizedTime and SetTransitionProgress send scrub commands that pause playback
                if (!transitionTimeline.IsPlaying || transitionTimeline.IsDragging)
                {
                    previewSession.SetNormalizedTime(transitionTimeline.NormalizedTime);
                    
                    // Apply blend curve to get actual blend weight
                    // Uses CurveUtils for runtime-identical evaluation
                    float rawProgress = transitionTimeline.TransitionProgress;
                    var keyframes = transitionInspectorBuilder?.BlendCurveKeyframes;
                    float blendWeight = CurveUtils.EvaluateCurveManaged(keyframes, rawProgress);
                    
                    // Set transition progress for blend weights (from→to crossfade)
                    previewSession.SetTransitionProgress(blendWeight);
                }
                
                // Set per-state normalized times for proper clip sampling (PlayableGraph backend only)
                previewSession.SetTransitionStateNormalizedTimes(
                    transitionTimeline.FromStateNormalizedTime,
                    transitionTimeline.ToStateNormalizedTime);
            }
            
            // Draw the preview
            previewSession.Draw(rect);
            
            // Handle camera input (but not if any timeline is dragging)
            bool transitionTimelineDragging = transitionInspectorBuilder?.Timeline?.IsDragging ?? false;
            bool stateTimelineDragging = stateInspectorBuilder?.TimelineScrubber?.IsDragging ?? false;
            if (!transitionTimelineDragging && !stateTimelineDragging && previewSession.HandleInput(rect))
            {
                // Save camera state after user interaction
                SaveCameraState();
                Repaint();
            }
        }

        private void OnResetViewClicked()
        {
            previewSession?.ResetCameraView();
            SaveCameraState();
            Repaint();
        }

        /// <summary>
        /// Saves the current camera state for persistence across focus changes and domain reloads.
        /// </summary>
        private void SaveCameraState()
        {
            if (previewSession != null)
            {
                var camState = previewSession.CameraState;
                if (camState.IsValid)
                {
                    savedCameraState = camState;
                }
            }
        }
        
        private void OnPreviewModelChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            var newModel = evt.newValue as GameObject;
            
            // Validate the model has required components
            if (newModel != null)
            {
                var animator = newModel.GetComponentInChildren<Animator>();
                var skinnedMesh = newModel.GetComponentInChildren<SkinnedMeshRenderer>();
                
                if (animator == null || skinnedMesh == null)
                {
                    Debug.LogWarning("[AnimationPreview] Preview model must have an Animator and SkinnedMeshRenderer.");
                    previewModelField.SetValueWithoutNotify(evt.previousValue);
                    return;
                }
            }
            
            // Update the session
            if (previewSession != null)
            {
                previewSession.PreviewModel = newModel;
            }
            
            // Save to EditorPrefs for this state machine
            SavePreviewModelPreference(newModel);
            
            // Recreate the preview with the new model
            UpdateSelectionUI();
        }

        #endregion


    }
}
