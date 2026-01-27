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
        
        private const float MinWeight = 0f;
        private const float MaxWeight = 1f;
        private const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        
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
            public Slider WeightSlider;
            public Label WeightLabel;
            public Label BlendModeLabel;
            public Label SelectionLabel;
            public Button NavigateButton;
            
            // Per-layer timeline
            public TimelineScrubber Timeline;
            
            // State controls
            public VisualElement StateControls;
            public Slider BlendSlider;
            public Label BlendLabel;
            
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
                if (section.Timeline != null)
                {
                    section.Timeline.OnTimeChanged -= time => OnLayerTimeChanged(section.LayerIndex, time);
                    section.Timeline.OnPlayStateChanged -= playing => OnLayerPlayStateChanged(section.LayerIndex, playing);
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
            }
            else
            {
                // Update each layer's timeline independently
                foreach (var section in layerSections)
                {
                    section.Timeline?.Tick(dt);
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
            labelElement.style.minWidth = 60;
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
            valueLabel.style.minWidth = 35;
            
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
            playbackRow.style.marginBottom = 5;
            
            playButton = new Button(OnPlayButtonClicked) { text = "▶ Play" };
            playButton.AddToClassList("play-button");
            playButton.style.minWidth = 70;
            playbackRow.Add(playButton);
            
            var resetButton = new Button(OnResetButtonClicked) { text = "⟲ Reset" };
            resetButton.AddToClassList("reset-button");
            resetButton.style.minWidth = 60;
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
                compositionState.MasterTime = time;
            
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
        
        private LayerSectionData CreateLayerSection(ObservableLayerState layerState, int layerIndex)
        {
            var section = new LayerSectionData
            {
                LayerIndex = layerIndex,
                LayerAsset = layerState.LayerAsset
            };
            
            // Create foldout
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList("layer-section");
            section.Foldout.value = true;
            
            // Build custom header
            var header = CreateLayerHeader(section, layerState);
            
            // Replace default toggle with custom header
            var toggle = section.Foldout.Q<Toggle>();
            if (toggle != null)
            {
                toggle.style.display = DisplayStyle.None;
                toggle.parent.Insert(0, header);
            }
            
            // Content area
            section.Content = new VisualElement();
            section.Content.AddToClassList("layer-content");
            section.Content.style.paddingLeft = 15;
            
            BuildLayerContent(section, layerState);
            
            section.Foldout.Add(section.Content);
            
            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSectionData section, ObservableLayerState layerState)
        {
            var header = new VisualElement();
            header.AddToClassList("layer-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = 2;
            header.style.paddingBottom = 2;
            
            // Foldout arrow
            var arrow = new Label("▼");
            arrow.AddToClassList("foldout-arrow");
            arrow.style.minWidth = 16;
            header.Add(arrow);
            
            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.value = layerState.IsEnabled;
            section.EnableToggle.style.marginRight = 5;
            section.EnableToggle.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null) layer.IsEnabled = evt.newValue;
            });
            header.Add(section.EnableToggle);
            
            // Layer name
            var nameContainer = new VisualElement();
            nameContainer.style.flexDirection = FlexDirection.Column;
            nameContainer.style.flexGrow = 1;
            
            var nameLabel = new Label($"Layer {layerState.LayerIndex}: {layerState.Name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);
            
            section.BlendModeLabel = new Label(layerState.BlendMode.ToString());
            section.BlendModeLabel.AddToClassList("layer-blend-mode");
            section.BlendModeLabel.style.fontSize = 10;
            section.BlendModeLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            nameContainer.Add(section.BlendModeLabel);
            
            header.Add(nameContainer);
            
            // Weight slider
            var weightContainer = new VisualElement();
            weightContainer.style.flexDirection = FlexDirection.Row;
            weightContainer.style.alignItems = Align.Center;
            weightContainer.style.minWidth = 140;
            
            var weightLabel = new Label("Weight:");
            weightLabel.style.minWidth = 45;
            weightContainer.Add(weightLabel);
            
            section.WeightSlider = new Slider(MinWeight, MaxWeight);
            section.WeightSlider.style.flexGrow = 1;
            section.WeightSlider.value = layerState.Weight;
            section.WeightSlider.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null) layer.Weight = evt.newValue;
                section.WeightLabel.text = evt.newValue.ToString("F2");
            });
            weightContainer.Add(section.WeightSlider);
            
            section.WeightLabel = new Label(layerState.Weight.ToString("F2"));
            section.WeightLabel.style.minWidth = 35;
            weightContainer.Add(section.WeightLabel);
            
            header.Add(weightContainer);
            
            // Click to toggle foldout
            header.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == section.EnableToggle || evt.target == section.WeightSlider) return;
                section.Foldout.value = !section.Foldout.value;
                arrow.text = section.Foldout.value ? "▼" : "►";
            });
            
            return header;
        }
        
        private void BuildLayerContent(LayerSectionData section, ObservableLayerState layerState)
        {
            // Current selection
            var selectionRow = new VisualElement();
            selectionRow.style.flexDirection = FlexDirection.Row;
            selectionRow.style.alignItems = Align.Center;
            selectionRow.style.marginBottom = 5;
            
            section.SelectionLabel = new Label(GetSelectionText(layerState));
            section.SelectionLabel.AddToClassList("selection-label");
            section.SelectionLabel.style.flexGrow = 1;
            selectionRow.Add(section.SelectionLabel);
            
            section.NavigateButton = new Button(() => OnNavigateToLayer?.Invoke(section.LayerIndex, section.LayerAsset))
            {
                text = "Navigate →"
            };
            section.NavigateButton.AddToClassList("navigate-button");
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
            timelineSection.style.marginTop = 5;
            
            section.Timeline = new TimelineScrubber();
            section.Timeline.IsLooping = true;
            
            // Configure timeline based on selected state
            ConfigureLayerTimeline(section, layerState);
            
            // Subscribe to timeline events
            int layerIdx = section.LayerIndex;
            section.Timeline.OnTimeChanged += time => OnLayerTimeChanged(layerIdx, time);
            section.Timeline.OnPlayStateChanged += playing => OnLayerPlayStateChanged(layerIdx, playing);
            
            timelineSection.Add(section.Timeline);
            section.Content.Add(timelineSection);
            
            // Update visibility based on current mode
            RefreshLayerSection(section);
        }
        
        private void BuildLayerStateControls(LayerSectionData section, ObservableLayerState layerState)
        {
            var selectedState = layerState.SelectedState;
            
            // Blend position (for blend states)
            if (selectedState is LinearBlendStateAsset || selectedState is Directional2DBlendStateAsset)
            {
                var blendRow = CreateSliderRow(
                    "Blend",
                    0f, 1f,
                    layerState.BlendPosition.x,
                    value =>
                    {
                        var layer = compositionState?.GetLayer(section.LayerIndex);
                        if (layer != null)
                            layer.BlendPosition = new Unity.Mathematics.float2(value, layer.BlendPosition.y);
                    },
                    out section.BlendSlider,
                    out section.BlendLabel);
                section.StateControls.Add(blendRow);
            }
        }
        
        private void BuildLayerTransitionControls(LayerSectionData section, ObservableLayerState layerState)
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
        
        private void ConfigureLayerTimeline(LayerSectionData section, ObservableLayerState layerState)
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
            section.WeightSlider?.SetValueWithoutNotify(layerState.Weight);
            if (section.WeightLabel != null)
                section.WeightLabel.text = layerState.Weight.ToString("F2");
            
            // Update selection label
            if (section.SelectionLabel != null)
                section.SelectionLabel.text = GetSelectionText(layerState);
            
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
                section.BlendSlider?.SetValueWithoutNotify(layerState.BlendPosition.x);
                if (section.BlendLabel != null)
                    section.BlendLabel.text = layerState.BlendPosition.x.ToString("F2");
            }
            
            // Update transition controls
            if (isTransition)
            {
                section.TransitionProgressSlider?.SetValueWithoutNotify(layerState.TransitionProgress);
            }
            
            // Update timeline
            ConfigureLayerTimeline(section, layerState);
        }
        
        private static string GetSelectionText(ObservableLayerState layerState)
        {
            if (layerState.IsTransitionMode)
            {
                var from = layerState.PreviewState.TransitionFrom?.name ?? "?";
                var to = layerState.PreviewState.TransitionTo?.name ?? "?";
                return $"→ {from} → {to}";
            }
            
            if (layerState.SelectedState != null)
            {
                return $"→ {layerState.SelectedState.name}";
            }
            
            return "→ (No Selection)";
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
