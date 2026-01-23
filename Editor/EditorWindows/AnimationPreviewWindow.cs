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
        private const float MinInspectorWidth = 250f;
        private const float MinPreviewWidth = 200f;
        private const float MinPreviewHeight = 200f;
        private const float DefaultSplitPosition = 300f;
        private const string PreviewModelPrefKeyPrefix = "DMotion.AnimationPreview.PreviewModel.";
        private const string PreviewModePrefKey = "DMotion.AnimationPreview.PreviewMode";

        #region Serialized State

        [SerializeField] private PlayableGraphPreview.CameraState savedCameraState;
        
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

        private enum SelectionType
        {
            None,
            State,
            Transition,
            AnyState,
            AnyStateTransition
        }

        #endregion

        #region Extracted Components

        private StateInspectorBuilder stateInspectorBuilder;
        private TransitionInspectorBuilder transitionInspectorBuilder;
        private PreviewSession previewSession;

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
            // Subscribe to state machine editor events
            StateMachineEditorEvents.OnStateSelected += OnGraphStateSelected;
            StateMachineEditorEvents.OnTransitionSelected += OnGraphTransitionSelected;
            StateMachineEditorEvents.OnAnyStateSelected += OnGraphAnyStateSelected;
            StateMachineEditorEvents.OnAnyStateTransitionSelected += OnGraphAnyStateTransitionSelected;
            StateMachineEditorEvents.OnSelectionCleared += OnGraphSelectionCleared;
            StateMachineEditorEvents.OnStateMachineChanged += OnStateMachineChanged;
            
            // Subscribe to blend position events
            AnimationPreviewEvents.OnBlendPosition1DChanged += OnBlendPosition1DChanged;
            AnimationPreviewEvents.OnBlendPosition2DChanged += OnBlendPosition2DChanged;
            AnimationPreviewEvents.OnClipSelectedForPreview += OnClipSelectedForPreview;
            
            // Subscribe to transition events
            AnimationPreviewEvents.OnTransitionProgressChanged += OnTransitionProgressChanged;
            AnimationPreviewEvents.OnTransitionFromBlendPositionChanged += OnTransitionFromBlendPositionChanged;
            AnimationPreviewEvents.OnTransitionToBlendPositionChanged += OnTransitionToBlendPositionChanged;
            AnimationPreviewEvents.OnTransitionTimeChanged += OnTransitionTimeChanged;
            
            // Subscribe to navigation events
            AnimationPreviewEvents.OnNavigateToState += OnNavigateToState;
            AnimationPreviewEvents.OnNavigateToTransition += OnNavigateToTransition;
            
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
        }

        private void OnDisable()
        {
            // Unsubscribe from events
            StateMachineEditorEvents.OnStateSelected -= OnGraphStateSelected;
            StateMachineEditorEvents.OnTransitionSelected -= OnGraphTransitionSelected;
            StateMachineEditorEvents.OnAnyStateSelected -= OnGraphAnyStateSelected;
            StateMachineEditorEvents.OnAnyStateTransitionSelected -= OnGraphAnyStateTransitionSelected;
            StateMachineEditorEvents.OnSelectionCleared -= OnGraphSelectionCleared;
            StateMachineEditorEvents.OnStateMachineChanged -= OnStateMachineChanged;
            
            // Unsubscribe from blend position events
            AnimationPreviewEvents.OnBlendPosition1DChanged -= OnBlendPosition1DChanged;
            AnimationPreviewEvents.OnBlendPosition2DChanged -= OnBlendPosition2DChanged;
            AnimationPreviewEvents.OnClipSelectedForPreview -= OnClipSelectedForPreview;
            
            // Unsubscribe from transition events
            AnimationPreviewEvents.OnTransitionProgressChanged -= OnTransitionProgressChanged;
            AnimationPreviewEvents.OnTransitionFromBlendPositionChanged -= OnTransitionFromBlendPositionChanged;
            AnimationPreviewEvents.OnTransitionToBlendPositionChanged -= OnTransitionToBlendPositionChanged;
            AnimationPreviewEvents.OnTransitionTimeChanged -= OnTransitionTimeChanged;
            
            // Unsubscribe from navigation events
            AnimationPreviewEvents.OnNavigateToState -= OnNavigateToState;
            AnimationPreviewEvents.OnNavigateToTransition -= OnNavigateToTransition;

            // Clear preview event subscriptions (safety net for external subscribers)
            AnimationPreviewEvents.ClearAllSubscriptions();

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
            previewSession?.Dispose();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("preview-window");
            root.focusable = true;

            // Load stylesheet
            var styleSheet = FindStyleSheet(UssFileName);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
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
            
            if (needsRepaint)
            {
                Repaint();
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

        #region Selection Event Handlers

        private void OnGraphStateSelected(StateMachineAsset machine, AnimationStateAsset state)
        {
            SetContext(machine);
            selectedState = state;
            selectedTransitionFrom = null;
            selectedTransitionTo = null;
            isAnyStateSelected = false;
            currentSelectionType = state != null ? SelectionType.State : SelectionType.None;
            
            // Initialize state speed from the selected state
            currentStateSpeed = state != null && state.Speed > 0 ? state.Speed : 1f;
            
            UpdateSelectionUI();
        }

        private void OnGraphTransitionSelected(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            SetContext(machine);
            selectedState = null;
            selectedTransitionFrom = fromState;
            selectedTransitionTo = toState;
            isAnyStateSelected = false;
            currentSelectionType = SelectionType.Transition;
            UpdateSelectionUI();
        }

        private void OnGraphAnyStateSelected(StateMachineAsset machine)
        {
            SetContext(machine);
            selectedState = null;
            selectedTransitionFrom = null;
            selectedTransitionTo = null;
            isAnyStateSelected = true;
            currentSelectionType = SelectionType.AnyState;
            UpdateSelectionUI();
        }

        private void OnGraphAnyStateTransitionSelected(StateMachineAsset machine, AnimationStateAsset toState)
        {
            SetContext(machine);
            selectedState = null;
            selectedTransitionFrom = null;
            selectedTransitionTo = toState;
            isAnyStateSelected = true;
            currentSelectionType = SelectionType.AnyStateTransition;
            UpdateSelectionUI();
        }

        private void OnGraphSelectionCleared(StateMachineAsset machine)
        {
            SetContext(machine);
            ClearSelection();
            UpdateSelectionUI();
        }

        private void OnStateMachineChanged(StateMachineAsset machine)
        {
            if (machine == currentStateMachine)
            {
                // Refresh the UI in case the selected element was modified
                UpdateSelectionUI();
            }
        }
        
        private void OnNavigateToState(AnimationStateAsset state)
        {
            if (state == null || currentStateMachine == null) return;
            
            // Update local selection state
            selectedState = state;
            selectedTransitionFrom = null;
            selectedTransitionTo = null;
            isAnyStateSelected = false;
            currentSelectionType = SelectionType.State;
            
            // Initialize state speed from the selected state
            currentStateSpeed = state.Speed > 0 ? state.Speed : 1f;
            
            // Update UI
            UpdateSelectionUI();
            
            // Also notify the state machine editor to sync selection
            StateMachineEditorEvents.RaiseStateSelected(currentStateMachine, state);
        }
        
        private void OnNavigateToTransition(AnimationStateAsset fromState, AnimationStateAsset toState, bool isAnyState)
        {
            if (currentStateMachine == null) return;
            
            // Update local selection state
            selectedState = null;
            selectedTransitionFrom = fromState;
            selectedTransitionTo = toState;
            isAnyStateSelected = isAnyState;
            currentSelectionType = isAnyState ? SelectionType.AnyStateTransition : SelectionType.Transition;
            
            // Update UI
            UpdateSelectionUI();
            
            // Also notify the state machine editor to sync selection
            if (isAnyState)
            {
                StateMachineEditorEvents.RaiseAnyStateTransitionSelected(currentStateMachine, toState);
            }
            else
            {
                StateMachineEditorEvents.RaiseTransitionSelected(currentStateMachine, fromState, toState);
            }
        }

        private void SetContext(StateMachineAsset machine)
        {
            if (machine != null && machine != currentStateMachine)
            {
                currentStateMachine = machine;
                
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
            UpdateInspectorContent();
            
            // Create 3D preview for the selected state or transition
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
            
            UpdatePreviewVisibility();
        }

        private void UpdateSelectionLabel()
        {
            if (selectionLabel == null) return;

            selectionLabel.text = currentSelectionType switch
            {
                SelectionType.State => selectedState?.name ?? "Unknown State",
                SelectionType.Transition => $"{selectedTransitionFrom?.name ?? "?"} -> {selectedTransitionTo?.name ?? "?"}",
                SelectionType.AnyState => "Any State",
                SelectionType.AnyStateTransition => $"Any State -> {selectedTransitionTo?.name ?? "?"}",
                _ => "No Selection"
            };
        }

        private void UpdateInspectorContent()
        {
            if (inspectorContent == null) return;
            
            // Cleanup previous builders
            stateInspectorBuilder?.Cleanup();
            transitionInspectorBuilder?.Cleanup();
            
            inspectorContent.Clear();

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
            
            // Raise centralized event for other listeners
            if (selectedState != null)
            {
                AnimationPreviewEvents.RaisePreviewTimeChanged(selectedState, timelineScrubber?.NormalizedTime ?? 0);
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
        
        private void OnBlendPosition1DChanged(LinearBlendStateAsset state, float blendValue)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            previewSession?.SetBlendPosition1D(blendValue);
            Repaint();
        }
        
        private void OnBlendPosition2DChanged(Directional2DBlendStateAsset state, Vector2 blendPosition)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            previewSession?.SetBlendPosition2D(new Unity.Mathematics.float2(blendPosition.x, blendPosition.y));
            Repaint();
        }
        
        private void OnClipSelectedForPreview(AnimationStateAsset state, int clipIndex)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            // Set solo clip mode: -1 = blended, >= 0 = individual clip
            previewSession?.SetSoloClip(clipIndex);
            Repaint();
        }
        
        #endregion
        
        #region Transition Events
        
        private void OnTransitionProgressChanged(AnimationStateAsset fromState, AnimationStateAsset toState, float progress)
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
        
        private void OnTransitionFromBlendPositionChanged(AnimationStateAsset fromState, Vector2 blendPosition)
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
        
        private void OnTransitionToBlendPositionChanged(AnimationStateAsset toState, Vector2 blendPosition)
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
        
        private void OnTransitionTimeChanged(AnimationStateAsset fromState, AnimationStateAsset toState, float normalizedTime)
        {
            // Only update if this matches the currently selected transition
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // For Any State transitions, fromState will be null
            bool matchesFrom = (isAnyStateSelected && fromState == null) || (fromState == selectedTransitionFrom);
            bool matchesTo = toState == selectedTransitionTo;
            
            if (!matchesFrom || !matchesTo) return;
            
            // Get per-state normalized times from the timeline for proper playback
            var timeline = transitionInspectorBuilder?.Timeline;
            if (timeline != null)
            {
                previewSession?.SetTransitionStateNormalizedTimes(
                    timeline.FromStateNormalizedTime,
                    timeline.ToStateNormalizedTime);
            }
            Repaint();
        }
        
        #endregion

        #region Preview Rendering

        private void OnPreviewGUI()
        {
            if (previewSession == null) return;
            
            var rect = previewContainer.contentRect;
            
            // Sync time from state timeline scrubber
            var stateTimeline = stateInspectorBuilder?.TimelineScrubber;
            if (stateTimeline != null && currentSelectionType == SelectionType.State)
            {
                previewSession.SetNormalizedTime(stateTimeline.NormalizedTime);
            }
            
            // Sync time and progress from transition timeline
            var transitionTimeline = transitionInspectorBuilder?.Timeline;
            if (transitionTimeline != null && previewSession.IsTransitionPreview)
            {
                // Set per-state normalized times for proper clip sampling
                previewSession.SetTransitionStateNormalizedTimes(
                    transitionTimeline.FromStateNormalizedTime,
                    transitionTimeline.ToStateNormalizedTime);
                
                // Apply blend curve to get actual blend weight
                // Uses CurveUtils for runtime-identical evaluation
                float rawProgress = transitionTimeline.TransitionProgress;
                var keyframes = transitionInspectorBuilder?.BlendCurveKeyframes;
                float blendWeight = CurveUtils.EvaluateCurveManaged(keyframes, rawProgress);
                
                // Set transition progress for blend weights (from→to crossfade)
                previewSession.SetTransitionProgress(blendWeight);
            }
            
            // Draw the preview
            previewSession.Draw(rect);
            
            // Handle camera input (but not if any timeline is dragging)
            bool transitionTimelineDragging = transitionInspectorBuilder?.Timeline?.IsDragging ?? false;
            bool stateTimelineDragging = stateInspectorBuilder?.TimelineScrubber?.IsDragging ?? false;
            if (!transitionTimelineDragging && !stateTimelineDragging && previewSession.HandleInput(rect))
            {
                Repaint();
            }
        }

        private void OnResetViewClicked()
        {
            previewSession?.ResetCameraView();
            Repaint();
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
