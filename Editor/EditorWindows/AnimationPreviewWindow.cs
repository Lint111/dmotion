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
        private const string SplitPositionPrefKey = "DMotion.AnimationPreview.SplitPosition";

        #region Serialized State

        [SerializeField] private float splitPosition = DefaultSplitPosition;

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
        private PreviewRenderer previewRenderer;

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
            // Load persisted split position
            splitPosition = EditorPrefs.GetFloat(SplitPositionPrefKey, DefaultSplitPosition);

            // Subscribe to state machine editor events
            StateMachineEditorEvents.OnStateSelected += OnGraphStateSelected;
            StateMachineEditorEvents.OnTransitionSelected += OnGraphTransitionSelected;
            StateMachineEditorEvents.OnAnyStateSelected += OnGraphAnyStateSelected;
            StateMachineEditorEvents.OnAnyStateTransitionSelected += OnGraphAnyStateTransitionSelected;
            StateMachineEditorEvents.OnSelectionCleared += OnGraphSelectionCleared;
            StateMachineEditorEvents.OnStateMachineChanged += OnStateMachineChanged;
            
            // Create extracted components
            CreateBuilders();
            previewRenderer = new PreviewRenderer();
        }

        private void OnDisable()
        {
            // Save split position
            EditorPrefs.SetFloat(SplitPositionPrefKey, splitPosition);

            // Unsubscribe from events
            StateMachineEditorEvents.OnStateSelected -= OnGraphStateSelected;
            StateMachineEditorEvents.OnTransitionSelected -= OnGraphTransitionSelected;
            StateMachineEditorEvents.OnAnyStateSelected -= OnGraphAnyStateSelected;
            StateMachineEditorEvents.OnAnyStateTransitionSelected -= OnGraphAnyStateTransitionSelected;
            StateMachineEditorEvents.OnSelectionCleared -= OnGraphSelectionCleared;
            StateMachineEditorEvents.OnStateMachineChanged -= OnStateMachineChanged;

            // Clear preview event subscriptions (safety net for external subscribers)
            AnimationPreviewEvents.ClearAllSubscriptions();

            // Dispose resources
            stateInspectorBuilder?.Cleanup();
            previewRenderer?.Dispose();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("preview-window");

            // Load stylesheet
            var styleSheet = FindStyleSheet(UssFileName);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            BuildUI(root);
            UpdateSelectionUI();
            
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void Update()
        {
            // Calculate delta time
            var currentTime = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(currentTime - lastUpdateTime);
            lastUpdateTime = currentTime;

            // Update timeline playback
            var timelineScrubber = stateInspectorBuilder?.TimelineScrubber;
            if (timelineScrubber != null && timelineScrubber.IsPlaying)
            {
                timelineScrubber.Tick(deltaTime);
                Repaint();
            }
        }

        #endregion

        #region Builder Creation

        private void CreateBuilders()
        {
            stateInspectorBuilder = new StateInspectorBuilder(
                CreateSectionHeader,
                CreateSection,
                CreatePropertyRow,
                CreateEditableFloatProperty,
                CreateEditableBoolPropertyWithCallback);
            
            stateInspectorBuilder.OnTimeChanged += OnTimelineTimeChanged;
            stateInspectorBuilder.OnRepaintRequested += Repaint;
            
            transitionInspectorBuilder = new TransitionInspectorBuilder(
                CreateSectionHeader,
                CreateSection,
                CreatePropertyRow,
                CreateEditableSerializedFloatProperty);
        }

        #endregion

        #region UI Construction

        private void BuildUI(VisualElement root)
        {
            // Toolbar
            var toolbar = new Toolbar();
            toolbar.AddToClassList("preview-toolbar");

            // Mode dropdown (placeholder for future implementation)
            modeDropdown = new ToolbarMenu { text = "Preview" };
            modeDropdown.menu.AppendAction("Preview Mode", _ => { }, DropdownMenuAction.Status.Checked);
            toolbar.Add(modeDropdown);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            // Selection label
            selectionLabel = new Label("No Selection");
            selectionLabel.AddToClassList("selection-label");
            toolbar.Add(selectionLabel);

            root.Add(toolbar);

            // Main content with split view
            var mainContent = new VisualElement();
            mainContent.AddToClassList("main-content");
            mainContent.style.flexGrow = 1;

            // Create split view (horizontal: left = inspector, right = preview)
            splitView = new TwoPaneSplitView(0, splitPosition, TwoPaneSplitViewOrientation.Horizontal);
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
            previewToolbar.style.flexDirection = FlexDirection.Row;
            previewToolbar.style.position = Position.Absolute;
            previewToolbar.style.top = 4;
            previewToolbar.style.right = 4;
            
            var resetViewButton = new Button(OnResetViewClicked) { text = "Reset View", tooltip = "Reset camera to default position" };
            resetViewButton.AddToClassList("preview-control-button");
            previewToolbar.Add(resetViewButton);

            // Preview placeholder (shown when no preview available)
            previewPlaceholder = new Label("No Preview Available\n\nSelect a state or transition\nin the State Machine Editor");
            previewPlaceholder.AddToClassList("preview-placeholder");

            // IMGUI container for 3D preview
            previewContainer = new IMGUIContainer(OnPreviewGUI);
            previewContainer.AddToClassList("preview-imgui");
            previewContainer.style.display = DisplayStyle.None;

            previewPanel.Add(previewPlaceholder);
            previewPanel.Add(previewContainer);
            previewPanel.Add(previewToolbar);

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
            // Save the split position when it changes
            if (splitView != null && inspectorPanel != null)
            {
                var newPosition = inspectorPanel.resolvedStyle.width;
                if (newPosition > 0 && Math.Abs(newPosition - splitPosition) > 1f)
                {
                    splitPosition = newPosition;
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

        #region Selection Event Handlers

        private void OnGraphStateSelected(StateMachineAsset machine, AnimationStateAsset state)
        {
            SetContext(machine);
            selectedState = state;
            selectedTransitionFrom = null;
            selectedTransitionTo = null;
            isAnyStateSelected = false;
            currentSelectionType = state != null ? SelectionType.State : SelectionType.None;
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

        private void SetContext(StateMachineAsset machine)
        {
            if (machine != null && machine != currentStateMachine)
            {
                currentStateMachine = machine;
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
            
            // Create 3D preview for the selected state
            if (currentSelectionType == SelectionType.State && selectedState != null)
            {
                previewRenderer.CreatePreviewForState(selectedState);
            }
            else
            {
                var message = currentSelectionType switch
                {
                    SelectionType.Transition or SelectionType.AnyStateTransition => 
                        PreviewWindowConstants.TransitionPreviewNotAvailable,
                    SelectionType.AnyState => 
                        "Select a state to\npreview animation",
                    _ => null
                };
                
                if (message != null)
                    previewRenderer.SetMessage(message);
                else
                    previewRenderer.Clear();
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
            
            // Cleanup previous state inspector
            stateInspectorBuilder?.Cleanup();
            
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
            
            var content = stateInspectorBuilder.Build(selectedState);
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

        private void UpdatePreviewVisibility()
        {
            if (previewPlaceholder == null || previewContainer == null) return;

            bool hasPreview = previewRenderer?.HasContent ?? false;

            previewPlaceholder.style.display = hasPreview ? DisplayStyle.None : DisplayStyle.Flex;
            previewContainer.style.display = hasPreview ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion

        #region Timeline Events

        private void OnTimelineTimeChanged(float time)
        {
            // Update preview sample time
            var timelineScrubber = stateInspectorBuilder?.TimelineScrubber;
            previewRenderer?.SetNormalizedTime(timelineScrubber?.NormalizedTime ?? 0);
            
            // Raise centralized event for other listeners
            if (selectedState != null)
            {
                AnimationPreviewEvents.RaisePreviewTimeChanged(selectedState, timelineScrubber?.NormalizedTime ?? 0);
            }
            
            Repaint();
        }

        #endregion

        #region Preview Rendering

        private void OnPreviewGUI()
        {
            if (previewRenderer == null) return;
            
            var rect = previewContainer.contentRect;
            
            // Sync time from timeline scrubber
            var timelineScrubber = stateInspectorBuilder?.TimelineScrubber;
            if (timelineScrubber != null)
            {
                previewRenderer.SetNormalizedTime(timelineScrubber.NormalizedTime);
            }
            
            // Draw the preview
            previewRenderer.Draw(rect);
            
            // Handle camera input
            if (previewRenderer.HandleInput(rect))
            {
                Repaint();
            }
        }

        private void OnResetViewClicked()
        {
            previewRenderer?.ResetCameraView();
            Repaint();
        }

        #endregion

        #region UI Helpers

        private VisualElement CreateSectionHeader(string type, string name)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");

            header.Add(typeLabel);
            header.Add(nameLabel);

            return header;
        }

        private VisualElement CreateSection(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("inspector-section");

            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("section-foldout");

            section.Add(foldout);
            return foldout;
        }

        private VisualElement CreatePropertyRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");

            var valueElement = new Label(value);
            valueElement.AddToClassList("property-value");

            row.Add(labelElement);
            row.Add(valueElement);

            return row;
        }

        /// <summary>
        /// Creates an editable float property with slider and numeric field.
        /// </summary>
        private VisualElement CreateEditableFloatProperty(
            SerializedObject serializedObject, 
            string propertyName, 
            string label, 
            float min, 
            float max,
            string suffix = "")
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return CreatePropertyRow(label, "Property not found");
            }

            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.AddToClassList("editable-property");

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);

            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("property-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;

            // Slider
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.bindingPath = propertyName;
            
            // Float field
            var floatField = new FloatField();
            floatField.AddToClassList("property-float-field");
            floatField.style.width = 50;
            floatField.style.marginLeft = 4;
            floatField.bindingPath = propertyName;

            // Suffix label
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.AddToClassList("property-suffix");
                suffixLabel.style.marginLeft = 2;
                suffixLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                valueContainer.Add(slider);
                valueContainer.Add(floatField);
                valueContainer.Add(suffixLabel);
            }
            else
            {
                valueContainer.Add(slider);
                valueContainer.Add(floatField);
            }

            container.Add(valueContainer);
            return container;
        }

        /// <summary>
        /// Creates an editable bool property with toggle and optional change callback.
        /// </summary>
        private VisualElement CreateEditableBoolPropertyWithCallback(
            SerializedObject serializedObject,
            string propertyName,
            string label,
            Action<bool> onValueChanged)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return CreatePropertyRow(label, "Property not found");
            }

            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.AddToClassList("editable-property");

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);

            var toggle = new Toggle();
            toggle.AddToClassList("property-toggle");
            toggle.bindingPath = propertyName;
            
            if (onValueChanged != null)
            {
                toggle.RegisterValueChangedCallback(evt => onValueChanged(evt.newValue));
            }

            container.Add(toggle);
            return container;
        }

        /// <summary>
        /// Creates an editable float property from a SerializedProperty.
        /// </summary>
        private VisualElement CreateEditableSerializedFloatProperty(
            SerializedProperty property,
            string label,
            float min,
            float max,
            string suffix = "")
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.AddToClassList("editable-property");

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);

            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("property-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;

            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.BindProperty(property);

            var floatField = new FloatField();
            floatField.AddToClassList("property-float-field");
            floatField.style.width = 50;
            floatField.style.marginLeft = 4;
            floatField.BindProperty(property);

            valueContainer.Add(slider);
            valueContainer.Add(floatField);

            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.AddToClassList("property-suffix");
                suffixLabel.style.marginLeft = 2;
                valueContainer.Add(suffixLabel);
            }

            container.Add(valueContainer);
            return container;
        }

        #endregion
    }
}
