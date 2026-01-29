using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Self-contained builder for layer composition inspector UI in the preview window.
    /// Handles multi-layer state machine preview with global playback controls
    /// and per-layer state/transition controls.
    /// </summary>
    internal class LayerCompositionInspectorBuilder
    {
        #region Constants

        // Weight slider range
        private const float MinWeight = 0f;
        private const float MaxWeight = 1f;

        // UI element widths
        private const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        private const float WeightSliderMinWidth = 60f;
        private const float BlendFieldMinWidth = 35f;
        private const float NavigateButtonWidth = 140f;

        // Spacing and margins
        private const float BaseLayerNameMarginLeft = 10f;
        private const float HeaderPaddingVertical = 2f;
        private const float SelectionRowMarginBottom = 5f;
        private const float TimelineSectionMarginTop = 5f;

        #endregion
        
        #region State
        
        private StateMachineAsset currentStateMachine;
        private ObservableCompositionState compositionState;
        private ILayerCompositionPreview preview;
        
        // UI references
        private VisualElement layersContainer;
        private Slider globalTimeSlider;
        private Button playButton;
        private Toggle syncLayersToggle;
        private readonly List<LayerSectionData> layerSections = new();
        
        // Playback
        private bool isPlaying;
        private float playbackSpeed = 1f;
        
        /// <summary>
        /// Data for a single layer's UI section.
        /// </summary>
        private class LayerSectionData
        {
            public int LayerIndex;
            public LayerStateAsset LayerAsset;
            public Foldout Foldout;
            public VisualElement Content;
            public Toggle EnableToggle;
            public VisualElement WeightContainer;
            public Slider WeightSlider;
            public Label WeightLabel;
            public EnumField BlendModeField;
            public Label SelectionLabel;
            public Button NavigateButton;
            public IconButton ClearButton; // Explicit clear assignment button

            // Per-layer timeline
            public TimelineScrubber Timeline;
            public Action<float> TimelineTimeChangedHandler;
            public Action<bool> TimelinePlayStateChangedHandler;

            // State controls
            public VisualElement StateControls;
            public Slider BlendSlider;
            public FloatField BlendField;
            public Label BlendLabel;

            // Blend space visual element (reusable across state changes)
            public VisualElement BlendSpaceContainer;
            public BlendSpaceVisualElement BlendSpaceElement;
            public AnimationStateAsset BoundBlendState; // Track which state the element is bound to
            public Action<Vector2> CachedPreviewPositionHandler;

            // Transition controls
            public VisualElement TransitionControls;
            public Slider TransitionProgressSlider;
        }
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when time changes (global or per-layer).
        /// </summary>
        public event Action<float> OnTimeChanged;
        
        /// <summary>
        /// Fired when the builder needs a repaint.
        /// </summary>
        public event Action OnRepaintRequested;
        
        /// <summary>
        /// Fired when play state changes.
        /// </summary>
        public event Action<bool> OnPlayStateChanged;
        
        /// <summary>
        /// Fired when user requests navigation to a layer.
        /// Parameters: layerIndex, layerAsset
        /// </summary>
        public event Action<int, LayerStateAsset> OnNavigateToLayer;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether global playback is active.
        /// </summary>
        public bool IsPlaying => isPlaying;
        
        /// <summary>
        /// Playback speed multiplier.
        /// </summary>
        public float PlaybackSpeed
        {
            get => playbackSpeed;
            set => playbackSpeed = value;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Builds the inspector UI for a multi-layer state machine.
        /// </summary>
        public VisualElement Build(
            StateMachineAsset stateMachine,
            ObservableCompositionState compositionState,
            ILayerCompositionPreview preview)
        {
            Cleanup();
            
            this.currentStateMachine = stateMachine;
            this.compositionState = compositionState;
            this.preview = preview;
            
            var container = new VisualElement();
            container.AddToClassList("layer-composition-inspector");
            
            // Header
            var header = CreateSectionHeader("Layer Composition", stateMachine?.name ?? "");
            container.Add(header);
            
            // Validation
            if (stateMachine == null || !stateMachine.IsMultiLayer)
            {
                var message = new Label("Select a multi-layer state machine to preview layer composition.");
                message.AddToClassList("info-message");
                container.Add(message);
                return container;
            }
            
            if (compositionState == null || compositionState.RootStateMachine == null)
            {
                var message = new Label("Layer composition state not initialized.");
                message.AddToClassList("info-message");
                container.Add(message);
                return container;
            }
            
            // Global controls section
            var globalSection = CreateSection("Global Playback");
            BuildGlobalControls(globalSection);
            container.Add(globalSection);
            
            // Layers section
            var layersSection = CreateSection($"Layers ({compositionState.LayerCount})");
            layersContainer = new VisualElement();
            layersContainer.AddToClassList("layers-container");
            BuildLayerSections(layersContainer);
            layersSection.Add(layersContainer);
            container.Add(layersSection);
            
            // Subscribe to composition state changes
            SubscribeToCompositionState();
            
            return container;
        }
        
        /// <summary>
        /// Cleans up event subscriptions and resources.
        /// </summary>
        public void Cleanup()
        {
            // Unsubscribe from composition state
            UnsubscribeFromCompositionState();
            
            // Cleanup layer sections
            foreach (var section in layerSections)
            {
                UnbindBlendSpaceElement(section);

                // Unsubscribe from timeline events using stored delegate references
                if (section.Timeline != null)
                {
                    if (section.TimelineTimeChangedHandler != null)
                        section.Timeline.OnTimeChanged -= section.TimelineTimeChangedHandler;
                    if (section.TimelinePlayStateChangedHandler != null)
                        section.Timeline.OnPlayStateChanged -= section.TimelinePlayStateChangedHandler;
                }
            }
            layerSections.Clear();
            
            // Clear references
            currentStateMachine = null;
            compositionState = null;
            preview = null;
            layersContainer = null;
            globalTimeSlider = null;
            playButton = null;
            syncLayersToggle = null;
        }
        
        /// <summary>
        /// Ticks playback. Call from Update loop.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isPlaying || compositionState == null) return;

            var dt = deltaTime * playbackSpeed;

            if (compositionState.SyncLayers)
            {
                // Update global time
                var newTime = compositionState.MasterTime + dt;
                compositionState.MasterTime = newTime % 1f; // Loop

                // Propagate global time to the preview backend
                preview?.SetGlobalNormalizedTime(compositionState.MasterTime);

                // Update all layer timelines to show the synced master time
                foreach (var section in layerSections)
                {
                    if (section.Timeline != null)
                    {
                        section.Timeline.NormalizedTime = compositionState.MasterTime;
                    }
                }
            }
            else
            {
                // Update each layer's timeline independently
                foreach (var section in layerSections)
                {
                    section.Timeline?.Tick(dt);

                    // Propagate layer time to backend
                    var layerState = compositionState.GetLayer(section.LayerIndex);
                    if (layerState != null)
                    {
                        preview?.SetLayerNormalizedTime(section.LayerIndex, layerState.NormalizedTime);
                    }
                }
            }

            OnTimeChanged?.Invoke(compositionState.MasterTime);
            OnRepaintRequested?.Invoke();
        }
        
        /// <summary>
        /// Refreshes the UI to match composition state.
        /// </summary>
        public void Refresh()
        {
            if (compositionState == null) return;
            
            // Update global controls
            globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
            syncLayersToggle?.SetValueWithoutNotify(compositionState.SyncLayers);
            UpdatePlayButton();
            
            // Update layer sections
            foreach (var section in layerSections)
            {
                RefreshLayerSection(section);
            }
        }
        
        #endregion
        
        #region Private - UI Factories
        
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
        
        private Foldout CreateSection(string title)
        {
            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("section-foldout");
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
        
        private VisualElement CreateSliderRow(
            string label, 
            float min, 
            float max, 
            float value,
            Action<float> onChanged,
            out Slider outSlider,
            out Label outValueLabel)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.minWidth = WeightSliderMinWidth;
            row.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.value = value;
            
            var valueLabel = new Label(value.ToString("F2"));
            valueLabel.AddToClassList("value-label");
            valueLabel.style.minWidth = BlendFieldMinWidth;
            
            slider.RegisterValueChangedCallback(evt =>
            {
                valueLabel.text = evt.newValue.ToString("F2");
                onChanged?.Invoke(evt.newValue);
            });
            
            valueContainer.Add(slider);
            valueContainer.Add(valueLabel);
            row.Add(valueContainer);
            
            outSlider = slider;
            outValueLabel = valueLabel;
            
            return row;
        }
        
        #endregion
        
        #region Private - Global Controls
        
        private void BuildGlobalControls(VisualElement section)
        {
            // Playback row
            var playbackRow = new VisualElement();
            playbackRow.AddToClassList("playback-row");
            playbackRow.style.flexDirection = FlexDirection.Row;
            playbackRow.style.alignItems = Align.Center;
            playbackRow.style.marginBottom = SelectionRowMarginBottom;
            
            playButton = new Button(OnPlayButtonClicked) { text = "▶ Play" };
            playButton.AddToClassList("play-button");
            playButton.style.minWidth = 70;
            playbackRow.Add(playButton);
            
            var resetButton = new Button(OnResetButtonClicked) { text = "⟲ Reset" };
            resetButton.AddToClassList("reset-button");
            resetButton.style.minWidth = WeightSliderMinWidth;
            playbackRow.Add(resetButton);
            
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            playbackRow.Add(spacer);
            
            syncLayersToggle = new Toggle("Sync Layers");
            syncLayersToggle.AddToClassList("sync-toggle");
            syncLayersToggle.value = compositionState?.SyncLayers ?? true;
            syncLayersToggle.RegisterValueChangedCallback(evt =>
            {
                if (compositionState != null)
                    compositionState.SyncLayers = evt.newValue;
            });
            playbackRow.Add(syncLayersToggle);
            
            section.Add(playbackRow);
            
            // Global time row
            var timeRow = CreateSliderRow(
                "Master Time",
                0f, 1f,
                compositionState?.MasterTime ?? 0f,
                OnGlobalTimeChanged,
                out globalTimeSlider,
                out _);
            section.Add(timeRow);
        }
        
        private void OnPlayButtonClicked()
        {
            isPlaying = !isPlaying;
            
            if (compositionState != null)
                compositionState.IsPlaying = isPlaying;
            
            UpdatePlayButton();
            OnPlayStateChanged?.Invoke(isPlaying);
        }
        
        private void OnResetButtonClicked()
        {
            isPlaying = false;
            
            if (compositionState != null)
            {
                compositionState.MasterTime = 0f;
                compositionState.IsPlaying = false;
                compositionState.ResetAll();
            }
            
            UpdatePlayButton();
            Refresh();
            OnPlayStateChanged?.Invoke(false);
        }
        
        private void OnGlobalTimeChanged(float time)
        {
            if (compositionState != null)
            {
                compositionState.MasterTime = time;

                // Propagate time to the preview backend
                preview?.SetGlobalNormalizedTime(time);
            }

            OnTimeChanged?.Invoke(time);
        }
        
        private void UpdatePlayButton()
        {
            if (playButton != null)
                playButton.text = isPlaying ? "⏸ Pause" : "▶ Play";
        }
        
        #endregion
        
        #region Private - Layer Sections
        
        private void BuildLayerSections(VisualElement container)
        {
            layerSections.Clear();
            
            if (compositionState?.Layers == null) return;
            
            for (int i = 0; i < compositionState.LayerCount; i++)
            {
                var layerState = compositionState.Layers[i];
                var section = CreateLayerSection(layerState, i);
                container.Add(section.Foldout);
                layerSections.Add(section);
            }
        }
        
        private LayerSectionData CreateLayerSection(LayerStateAsset layerState, int layerIndex)
        {
            var section = new LayerSectionData
            {
                LayerIndex = layerIndex,
                LayerAsset = layerState
            };

            // Use a wrapper element instead of Foldout to have full control over collapse behavior
            // This avoids issues with Foldout's internal toggle manipulation
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList("layer-section");
            section.Foldout.value = true;

            // Keep the foldout's text empty since we're using custom header
            section.Foldout.text = "";

            // Create custom header as part of the Foldout's label area
            var header = CreateLayerHeader(section, layerState);

            // Instead of manipulating internal toggle, put header in the Foldout's toggle area properly
            var toggle = section.Foldout.Q<Toggle>();
            if (toggle != null)
            {
                // Add our header content to the toggle's visual tree directly
                // This keeps the header visible when foldout collapses
                var checkmark = toggle.Q<VisualElement>(className: "unity-foldout__checkmark");
                if (checkmark != null)
                {
                    // Insert header after checkmark, replacing the default label
                    var toggleContainer = checkmark.parent;
                    var defaultLabel = toggle.Q<Label>(className: "unity-foldout__text");
                    if (defaultLabel != null)
                        defaultLabel.style.display = DisplayStyle.None;

                    toggleContainer.Add(header);
                    header.style.flexGrow = 1;
                }
                else
                {
                    // Fallback: add header directly to toggle
                    toggle.Add(header);
                    header.style.flexGrow = 1;
                }
            }

            // Content area - this goes into the Foldout's collapsible section
            section.Content = new VisualElement();
            section.Content.AddToClassList("layer-content");
            section.Content.style.paddingLeft = 15;

            BuildLayerContent(section, layerState);

            section.Foldout.Add(section.Content);

            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSectionData section, LayerStateAsset layerState)
        {
            var header = new VisualElement();
            header.AddToClassList("layer-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = HeaderPaddingVertical;
            header.style.paddingBottom = HeaderPaddingVertical;

            // No manual arrow needed - Foldout's native checkmark handles collapse indicator

            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.value = layerState.IsEnabled;
            section.EnableToggle.style.marginRight = 5;
            section.EnableToggle.style.marginLeft = 5;
            section.EnableToggle.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null) layer.IsEnabled = evt.newValue;
            });
            // Stop click propagation to prevent foldout toggle when clicking enable checkbox
            section.EnableToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            header.Add(section.EnableToggle);

            // Layer name container
            var nameContainer = new VisualElement();
            nameContainer.style.flexDirection = FlexDirection.Column;
            nameContainer.style.flexGrow = 1;

            // Base layer: shift name right since we don't have weight slider
            if (layerState.IsBaseLayer)
            {
                nameContainer.style.marginLeft = BaseLayerNameMarginLeft;
            }

            var nameLabel = new Label($"Layer {layerState.LayerIndex}: {layerState.name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);

            // Blend mode control
            // Base layer: Read-only label (always Override)
            // Other layers: Editable dropdown
            if (layerState.IsBaseLayer)
            {
                var blendModeLabel = new Label("Override");
                blendModeLabel.AddToClassList("layer-blend-mode");
                blendModeLabel.style.fontSize = 10;
                nameContainer.Add(blendModeLabel);
            }
            else
            {
                section.BlendModeField = new EnumField(layerState.BlendMode);
                section.BlendModeField.AddToClassList("layer-blend-mode-field");
                section.BlendModeField.style.fontSize = 10;
                section.BlendModeField.style.maxWidth = 80;
                section.BlendModeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is LayerBlendMode blendMode)
                    {
                        var layer = compositionState?.GetLayer(section.LayerIndex);
                        if (layer != null)
                        {
                            layer.BlendMode = blendMode;
                            // Refresh to update weight slider visibility
                            RefreshLayerSection(section);
                        }
                    }
                    evt.StopPropagation();
                });
                // Stop click propagation to prevent foldout toggle
                section.BlendModeField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                section.BlendModeField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                nameContainer.Add(section.BlendModeField);
            }

            header.Add(nameContainer);

            // Weight slider - only for non-Layer 0 and Additive blend mode
            // Base layer weight locked to 1.0
            // Override blend mode always has effective weight 1.0
            bool shouldShowWeight = ShouldShowWeightSlider(layerState, layerState.BlendMode);

            section.WeightContainer = new VisualElement();
            section.WeightContainer.style.flexDirection = FlexDirection.Row;
            section.WeightContainer.style.alignItems = Align.Center;
            section.WeightContainer.style.minWidth = NavigateButtonWidth;
            section.WeightContainer.style.display = shouldShowWeight ? DisplayStyle.Flex : DisplayStyle.None;

            var weightLabel = new Label("Weight:");
            weightLabel.style.minWidth = 45;
            section.WeightContainer.Add(weightLabel);

            section.WeightSlider = new Slider(MinWeight, MaxWeight);
            section.WeightSlider.style.flexGrow = 1;
            section.WeightSlider.value = layerState.Weight;

            section.WeightSlider.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                // Base layer weight is locked (safety check)
                if (layer != null && layer.CanModifyWeight)
                    layer.Weight = evt.newValue;
                if (section.WeightLabel != null)
                    section.WeightLabel.text = evt.newValue.ToString("F2");
            });
            // Stop click propagation to prevent foldout toggle when interacting with slider
            section.WeightSlider.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            section.WeightSlider.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            section.WeightContainer.Add(section.WeightSlider);

            section.WeightLabel = new Label(layerState.Weight.ToString("F2"));
            section.WeightLabel.style.minWidth = BlendFieldMinWidth;
            section.WeightContainer.Add(section.WeightLabel);

            header.Add(section.WeightContainer);

            // Clear assignment button (X) - explicit action to unassign layer
            section.ClearButton = IconButton.CreateClearButton(
                    "Clear layer assignment",
                    () => compositionState?.ClearLayerSelection(section.LayerIndex))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.ClearButton.style.marginLeft = 5;
            header.Add(section.ClearButton);

            return header;
        }

        /// <summary>
        /// Determines whether the weight slider should be shown for a layer.
        /// Base layer: Never show (weight locked to 1.0).
        /// Override blend mode: Never show (weight is effectively 1.0).
        /// Additive blend mode: Show (weight is variable).
        /// </summary>
        private static bool ShouldShowWeightSlider(LayerStateAsset layer, LayerBlendMode blendMode)
        {
            // Base layer weight cannot be modified
            if (!layer.CanModifyWeight) return false;

            // Override mode: weight is effectively 1.0
            if (blendMode == LayerBlendMode.Override) return false;

            // Additive mode: weight is variable
            return true;
        }
        
        private void BuildLayerContent(LayerSectionData section, LayerStateAsset layerState)
        {
            // Current selection
            var selectionRow = new VisualElement();
            selectionRow.style.flexDirection = FlexDirection.Row;
            selectionRow.style.alignItems = Align.Center;
            selectionRow.style.marginBottom = SelectionRowMarginBottom;

            section.SelectionLabel = new Label(GetSelectionText(layerState));
            section.SelectionLabel.AddToClassList("selection-label");
            section.SelectionLabel.style.flexGrow = 1;
            section.SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            selectionRow.Add(section.SelectionLabel);

            // Navigation button - only show when layer is assigned
            section.NavigateButton = new Button(() => OnNavigateToLayer?.Invoke(section.LayerIndex, section.LayerAsset))
            {
                text = "Navigate →"
            };
            section.NavigateButton.AddToClassList("navigate-button");
            section.NavigateButton.style.display = layerState.IsAssigned ? DisplayStyle.Flex : DisplayStyle.None;
            selectionRow.Add(section.NavigateButton);

            section.Content.Add(selectionRow);
            
            // State controls (shown when a state is selected)
            section.StateControls = new VisualElement();
            section.StateControls.AddToClassList("state-controls");
            BuildLayerStateControls(section, layerState);
            section.Content.Add(section.StateControls);
            
            // Transition controls (shown when a transition is selected)
            section.TransitionControls = new VisualElement();
            section.TransitionControls.AddToClassList("transition-controls");
            BuildLayerTransitionControls(section, layerState);
            section.Content.Add(section.TransitionControls);
            
            // Per-layer timeline
            var timelineSection = new VisualElement();
            timelineSection.style.marginTop = TimelineSectionMarginTop;
            
            section.Timeline = new TimelineScrubber();
            section.Timeline.IsLooping = true;
            
            // Configure timeline based on selected state
            ConfigureLayerTimeline(section, layerState);
            
            // Subscribe to timeline events
            // Store delegate references for proper unsubscription in Cleanup()
            int layerIdx = section.LayerIndex;
            section.TimelineTimeChangedHandler = time => OnLayerTimeChanged(layerIdx, time);
            section.TimelinePlayStateChangedHandler = playing => OnLayerPlayStateChanged(layerIdx, playing);
            section.Timeline.OnTimeChanged += section.TimelineTimeChangedHandler;
            section.Timeline.OnPlayStateChanged += section.TimelinePlayStateChangedHandler;
            
            timelineSection.Add(section.Timeline);
            section.Content.Add(timelineSection);
            
            // Update visibility based on current mode
            RefreshLayerSection(section);
        }
        
        private void BuildLayerStateControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Blend space visual element container - populated dynamically based on selected state
            section.BlendSpaceContainer = new VisualElement();
            section.BlendSpaceContainer.name = "blend-space-container";
            section.StateControls.Add(section.BlendSpaceContainer);

            // Blend slider row with float field - range is updated dynamically
            var blendRow = new VisualElement();
            blendRow.name = "blend-row";
            blendRow.AddToClassList("property-row");

            var blendLabel = new Label("Blend");
            blendLabel.AddToClassList("property-label");
            blendLabel.style.minWidth = 60;
            blendRow.Add(blendLabel);

            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;

            section.BlendSlider = new Slider(0f, 1f);
            section.BlendSlider.AddToClassList("property-slider");
            section.BlendSlider.style.flexGrow = 1;
            section.BlendSlider.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendField?.SetValueWithoutNotify(evt.newValue);
            });

            section.BlendField = new FloatField();
            section.BlendField.AddToClassList("property-float-field");
            section.BlendField.style.minWidth = FloatFieldWidth;
            section.BlendField.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendSlider?.SetValueWithoutNotify(evt.newValue);
            });

            valueContainer.Add(section.BlendSlider);
            valueContainer.Add(section.BlendField);
            blendRow.Add(valueContainer);

            section.StateControls.Add(blendRow);
        }

        /// <summary>
        /// Creates or updates the blend space visual element for a layer based on its selected state.
        /// </summary>
        private void BindBlendSpaceElement(LayerSectionData section, AnimationStateAsset selectedState)
        {
            // Skip if already bound to this state AND element still exists in hierarchy
            if (section.BoundBlendState == selectedState &&
                section.BlendSpaceElement != null &&
                section.BlendSpaceElement.parent != null)
                return;

            // Cleanup previous element (also clears container defensively)
            UnbindBlendSpaceElement(section);

            section.BoundBlendState = selectedState;

            if (selectedState is LinearBlendStateAsset linear)
            {
                var element = new BlendSpace1DVisualElement();
                element.SetTarget(linear);
                element.ShowPreviewIndicator = true;
                element.EditMode = false;
                element.ShowModeToggle = false;
                element.AddToClassList("blend-space-1d-preview");
                element.style.height = 80;

                // Restore persisted blend value
                float persistedValue = PreviewSettings.instance.GetBlendValue1D(linear);
                element.PreviewPosition = new Vector2(persistedValue, 0);

                // Update slider range from clip thresholds
                UpdateBlendSliderRange(section, linear);
                section.BlendSlider?.SetValueWithoutNotify(persistedValue);
                section.BlendField?.SetValueWithoutNotify(persistedValue);

                // Sync to observable
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null)
                    layer.BlendPosition = new Unity.Mathematics.float2(persistedValue, 0);

                // Handle preview position changes from the visual element
                section.CachedPreviewPositionHandler = pos =>
                {
                    SetLayerBlendValue(section, pos.x);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                };
                element.OnPreviewPositionChanged += section.CachedPreviewPositionHandler;

                // Defensive: ensure container is clear before adding
                section.BlendSpaceContainer.Clear();
                section.BlendSpaceElement = element;
                section.BlendSpaceContainer.Add(element);
            }
            else if (selectedState is Directional2DBlendStateAsset blend2D)
            {
                var element = new BlendSpace2DVisualElement();
                element.SetTarget(blend2D);
                element.ShowPreviewIndicator = true;
                element.EditMode = false;
                element.ShowModeToggle = false;
                element.AddToClassList("blend-space-2d-preview");
                element.style.height = 150;

                // Restore persisted blend value
                Vector2 persistedValue = PreviewSettings.instance.GetBlendValue2D(blend2D);
                element.PreviewPosition = persistedValue;

                // Update slider range (X axis)
                UpdateBlendSliderRange2D(section, blend2D);
                section.BlendSlider?.SetValueWithoutNotify(persistedValue.x);
                section.BlendField?.SetValueWithoutNotify(persistedValue.x);

                // Sync to observable
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null)
                    layer.BlendPosition = new Unity.Mathematics.float2(persistedValue.x, persistedValue.y);

                // Handle preview position changes from the visual element
                section.CachedPreviewPositionHandler = pos =>
                {
                    SetLayerBlendValue2D(section, pos);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                };
                element.OnPreviewPositionChanged += section.CachedPreviewPositionHandler;

                // Defensive: ensure container is clear before adding
                section.BlendSpaceContainer.Clear();
                section.BlendSpaceElement = element;
                section.BlendSpaceContainer.Add(element);
            }
        }

        /// <summary>
        /// Removes and cleans up the current blend space visual element for a layer.
        /// </summary>
        private void UnbindBlendSpaceElement(LayerSectionData section)
        {
            if (section.BlendSpaceElement != null && section.CachedPreviewPositionHandler != null)
            {
                section.BlendSpaceElement.OnPreviewPositionChanged -= section.CachedPreviewPositionHandler;
                section.CachedPreviewPositionHandler = null;
            }

            section.BlendSpaceContainer?.Clear();
            section.BlendSpaceElement = null;
            section.BoundBlendState = null;
        }

        private void SetLayerBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new Unity.Mathematics.float2(value, layer.BlendPosition.y);

            // Update visual element
            if (section.BlendSpaceElement != null)
                section.BlendSpaceElement.PreviewPosition = new Vector2(value, section.BlendSpaceElement.PreviewPosition.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Persist via PreviewSettings
            var selectedState = layer.SelectedState;
            if (selectedState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(selectedState, value);
            else if (selectedState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(selectedState, new Vector2(value, layer.BlendPosition.y));
        }

        private void SetLayerBlendValue2D(LayerSectionData section, Vector2 value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new Unity.Mathematics.float2(value.x, value.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Visual element already updated by the caller
            // Persist via PreviewSettings
            var selectedState = layer.SelectedState;
            if (selectedState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(selectedState, value);
        }

        private static void UpdateBlendSliderRange(LayerSectionData section, LinearBlendStateAsset linear)
        {
            if (linear.BlendClips == null || linear.BlendClips.Length == 0) return;

            float min = float.MaxValue, max = float.MinValue;
            foreach (var clip in linear.BlendClips)
            {
                min = Mathf.Min(min, clip.Threshold);
                max = Mathf.Max(max, clip.Threshold);
            }

            var range = max - min;
            if (range < 0.1f) range = 1f;
            min -= range * 0.1f;
            max += range * 0.1f;

            if (section.BlendSlider != null)
            {
                section.BlendSlider.lowValue = min;
                section.BlendSlider.highValue = max;
            }
        }

        private static void UpdateBlendSliderRange2D(LayerSectionData section, Directional2DBlendStateAsset blend2D)
        {
            if (blend2D.BlendClips == null || blend2D.BlendClips.Length == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var clip in blend2D.BlendClips)
            {
                minX = Mathf.Min(minX, clip.Position.x);
                maxX = Mathf.Max(maxX, clip.Position.x);
            }

            var rangeX = maxX - minX;
            if (rangeX < 0.1f) rangeX = 1f;
            minX -= rangeX * 0.1f;
            maxX += rangeX * 0.1f;

            if (section.BlendSlider != null)
            {
                section.BlendSlider.lowValue = minX;
                section.BlendSlider.highValue = maxX;
            }
        }
        
        private void BuildLayerTransitionControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Transition progress
            var progressRow = CreateSliderRow(
                "Progress",
                0f, 1f,
                layerState.TransitionProgress,
                value =>
                {
                    var layer = compositionState?.GetLayer(section.LayerIndex);
                    if (layer != null)
                        layer.TransitionProgress = value;
                },
                out section.TransitionProgressSlider,
                out _);
            section.TransitionControls.Add(progressRow);
        }
        
        private void ConfigureLayerTimeline(LayerSectionData section, LayerStateAsset layerState)
        {
            var selectedState = layerState.SelectedState;
            if (selectedState == null)
            {
                section.Timeline.Duration = 1f;
                return;
            }
            
            // Get effective duration at current blend position
            var blendPos = layerState.BlendPosition;
            var duration = selectedState.GetEffectiveDuration(blendPos);
            if (duration <= 0) duration = 1f;
            
            section.Timeline.Duration = duration;
            section.Timeline.NormalizedTime = layerState.NormalizedTime;
        }
        
        private void RefreshLayerSection(LayerSectionData section)
        {
            if (compositionState == null || section.LayerIndex >= compositionState.LayerCount)
                return;

            var layerState = compositionState.Layers[section.LayerIndex];

            // Update header controls
            section.EnableToggle?.SetValueWithoutNotify(layerState.IsEnabled);

            // Update blend mode field
            section.BlendModeField?.SetValueWithoutNotify(layerState.BlendMode);

            // Update weight slider visibility based on blend mode
            bool shouldShowWeight = ShouldShowWeightSlider(layerState, layerState.BlendMode);
            if (section.WeightContainer != null)
                section.WeightContainer.style.display = shouldShowWeight ? DisplayStyle.Flex : DisplayStyle.None;

            // Update weight values (only if visible)
            if (shouldShowWeight)
            {
                section.WeightSlider?.SetValueWithoutNotify(layerState.Weight);
                if (section.WeightLabel != null)
                    section.WeightLabel.text = layerState.Weight.ToString("F2");
            }

            // Check if layer is unassigned
            bool isAssigned = layerState.IsAssigned;

            // Update selection label text
            if (section.SelectionLabel != null)
            {
                section.SelectionLabel.text = GetSelectionText(layerState);
                section.SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            }

            // Show/hide navigation button based on assignment status
            if (section.NavigateButton != null)
                section.NavigateButton.style.display = isAssigned ? DisplayStyle.Flex : DisplayStyle.None;

            // Show/hide clear button based on assignment status
            section.ClearButton?.SetVisible(isAssigned);

            // Enable/disable controls based on assignment
            if (shouldShowWeight)
                section.WeightSlider?.SetEnabled(isAssigned);
            section.EnableToggle?.SetEnabled(isAssigned);

            if (!isAssigned)
            {
                // Hide all controls for unassigned layers
                if (section.StateControls != null)
                    section.StateControls.style.display = DisplayStyle.None;
                if (section.TransitionControls != null)
                    section.TransitionControls.style.display = DisplayStyle.None;
                if (section.Timeline != null)
                    section.Timeline.style.display = DisplayStyle.None;
                return;
            }

            // Show timeline for assigned layers
            if (section.Timeline != null)
                section.Timeline.style.display = DisplayStyle.Flex;
            
            // Show/hide state vs transition controls
            bool isTransition = layerState.IsTransitionMode;
            bool hasState = layerState.SelectedState != null;
            
            if (section.StateControls != null)
                section.StateControls.style.display = (hasState && !isTransition) ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (section.TransitionControls != null)
                section.TransitionControls.style.display = isTransition ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Update state controls
            if (hasState && !isTransition)
            {
                // Show blend controls only for blend state types
                var selectedState = layerState.SelectedState;
                bool isBlendState = selectedState is LinearBlendStateAsset || selectedState is Directional2DBlendStateAsset;

                var blendRow = section.StateControls?.Q<VisualElement>("blend-row");
                if (blendRow != null)
                    blendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

                var blendSpaceContainer = section.BlendSpaceContainer;
                if (blendSpaceContainer != null)
                    blendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

                if (isBlendState)
                {
                    // Bind/update the blend space visual element for this state
                    BindBlendSpaceElement(section, selectedState);

                    // Read persisted value
                    var persisted = PreviewSettings.GetBlendPosition(selectedState);
                    section.BlendSlider?.SetValueWithoutNotify(persisted.x);
                    section.BlendField?.SetValueWithoutNotify(persisted.x);
                }
                else
                {
                    // Unbind blend space when not a blend state
                    UnbindBlendSpaceElement(section);
                }
            }
            else
            {
                // Hide blend space when not in state mode
                if (section.BlendSpaceContainer != null)
                    section.BlendSpaceContainer.style.display = DisplayStyle.None;
            }
            
            // Update transition controls
            if (isTransition)
            {
                section.TransitionProgressSlider?.SetValueWithoutNotify(layerState.TransitionProgress);
            }
            
            // Update timeline
            ConfigureLayerTimeline(section, layerState);
        }
        
        private static string GetSelectionText(LayerStateAsset layerState)
        {
            if (layerState.IsTransitionMode)
            {
                // Transition: show arrow to indicate flow
                var from = layerState.TransitionFrom?.name ?? "?";
                var to = layerState.TransitionTo?.name ?? "?";
                return $"{from} → {to}";
            }

            if (layerState.SelectedState != null)
            {
                // Simple state: no arrow needed
                return layerState.SelectedState.name;
            }

            // Layer is unassigned - not contributing to animation
            return "Unassigned";
        }
        
        private void OnLayerTimeChanged(int layerIndex, float time)
        {
            var layer = compositionState?.GetLayer(layerIndex);
            if (layer != null)
                layer.NormalizedTime = time;
            
            OnTimeChanged?.Invoke(time);
        }
        
        private void OnLayerPlayStateChanged(int layerIndex, bool playing)
        {
            var layer = compositionState?.GetLayer(layerIndex);
            if (layer != null)
                layer.IsPlaying = playing;
        }
        
        #endregion
        
        #region Private - Composition State Events
        
        private void SubscribeToCompositionState()
        {
            if (compositionState == null) return;
            
            compositionState.PropertyChanged += OnCompositionStatePropertyChanged;
            compositionState.LayerChanged += OnCompositionLayerChanged;
        }
        
        private void UnsubscribeFromCompositionState()
        {
            if (compositionState == null) return;
            
            compositionState.PropertyChanged -= OnCompositionStatePropertyChanged;
            compositionState.LayerChanged -= OnCompositionLayerChanged;
        }
        
        private void OnCompositionStatePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ObservableCompositionState.MasterTime):
                    globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
                    break;
                    
                case nameof(ObservableCompositionState.IsPlaying):
                    isPlaying = compositionState.IsPlaying;
                    UpdatePlayButton();
                    break;
                    
                case nameof(ObservableCompositionState.SyncLayers):
                    syncLayersToggle?.SetValueWithoutNotify(compositionState.SyncLayers);
                    break;
            }
        }
        
        private void OnCompositionLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            var layerIndex = e.LayerIndex;
            if (layerIndex >= 0 && layerIndex < layerSections.Count)
            {
                RefreshLayerSection(layerSections[layerIndex]);
            }
        }
        
        #endregion
    }
}
