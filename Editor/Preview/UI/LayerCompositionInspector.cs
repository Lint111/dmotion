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
    /// Inspector UI for multi-layer animation composition preview.
    /// Provides collapsible layer sections with state/transition controls.
    /// </summary>
    internal class LayerCompositionInspector : VisualElement
    {
        #region Constants
        
        private const string UssClassName = "layer-composition-inspector";
        private const string LayerSectionClassName = "layer-section";
        private const string LayerHeaderClassName = "layer-header";
        private const string LayerContentClassName = "layer-content";
        private const string LayerControlsClassName = "layer-controls";
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when user requests navigation to a layer's state machine.
        /// Parameters: layerIndex, layerAsset
        /// </summary>
        public event Action<int, LayerStateAsset> OnNavigateToLayer;
        
        /// <summary>
        /// Fired when a layer's weight changes.
        /// Parameters: layerIndex, newWeight
        /// </summary>
        public event Action<int, float> OnLayerWeightChanged;
        
        /// <summary>
        /// Fired when a layer's enabled state changes.
        /// Parameters: layerIndex, isEnabled
        /// </summary>
        public event Action<int, bool> OnLayerEnabledChanged;
        
        /// <summary>
        /// Fired when the global time changes.
        /// Parameters: normalizedTime
        /// </summary>
        public event Action<float> OnGlobalTimeChanged;
        
        /// <summary>
        /// Fired when playback state changes.
        /// Parameters: isPlaying
        /// </summary>
        public event Action<bool> OnPlaybackStateChanged;
        
        #endregion
        
        #region State
        
        private ILayerCompositionPreview preview;
        private StateMachineAsset stateMachine;
        private LayerCompositionState compositionState;
        private List<LayerSection> layerSections = new();
        
        // Global controls
        private Slider globalTimeSlider;
        private Button playButton;
        private Button resetButton;
        private Toggle syncLayersToggle;
        
        private class LayerSection
        {
            public int LayerIndex;
            public LayerStateAsset LayerAsset;
            public Foldout Foldout;
            public VisualElement Content;
            public Toggle EnableToggle;
            public Slider WeightSlider;
            public Label WeightLabel;
            public Label BlendModeLabel;
            public Label CurrentSelectionLabel;
            public Button NavigateButton;
            public Button TriggerTransitionButton;
            
            // State-specific controls (shown when state is selected)
            public VisualElement StateControls;
            public Slider BlendSlider;
            public Label BlendLabel;
            public VisualElement ClipWeights;
            
            // Transition-specific controls (shown when transition is selected)
            public VisualElement TransitionControls;
            public Slider TransitionProgressSlider;
            public Slider FromBlendSlider;
            public Slider ToBlendSlider;
        }
        
        #endregion
        
        #region Constructor
        
        public LayerCompositionInspector()
        {
            AddToClassList(UssClassName);
            BuildUI();
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Binds the inspector to a layer composition preview.
        /// </summary>
        public void Bind(ILayerCompositionPreview preview, StateMachineAsset stateMachine, LayerCompositionState compositionState)
        {
            this.preview = preview;
            this.stateMachine = stateMachine;
            this.compositionState = compositionState;
            
            RebuildLayerSections();
            
            // Subscribe to composition state changes
            if (compositionState != null)
            {
                compositionState.OnLayerChanged += OnLayerStateChanged;
                compositionState.OnMasterTimeChanged += OnMasterTimeChanged;
                compositionState.OnPlaybackStateChanged += OnPlaybackStateChanged;
            }
        }
        
        /// <summary>
        /// Unbinds and clears the inspector.
        /// </summary>
        public void Unbind()
        {
            if (compositionState != null)
            {
                compositionState.OnLayerChanged -= OnLayerStateChanged;
                compositionState.OnMasterTimeChanged -= OnMasterTimeChanged;
                compositionState.OnPlaybackStateChanged -= OnPlaybackStateChanged;
            }
            
            preview = null;
            stateMachine = null;
            compositionState = null;
            ClearLayerSections();
        }
        
        /// <summary>
        /// Refreshes the UI to match the current composition state.
        /// </summary>
        public void Refresh()
        {
            if (compositionState == null) return;
            
            // Update global controls
            globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
            syncLayersToggle?.SetValueWithoutNotify(compositionState.SyncLayers);
            
            if (playButton != null)
            {
                playButton.text = compositionState.IsPlaying ? "⏸ Pause" : "▶ Play";
            }
            
            // Update layer sections
            for (int i = 0; i < layerSections.Count; i++)
            {
                RefreshLayerSection(layerSections[i]);
            }
        }
        
        #endregion
        
        #region Private Methods - UI Construction
        
        private void BuildUI()
        {
            // Header
            var header = new Label("Layer Composition");
            header.AddToClassList("section-header");
            Add(header);
            
            // Global controls
            BuildGlobalControls();
            
            // Layers container
            var layersContainer = new VisualElement();
            layersContainer.name = "layers-container";
            layersContainer.AddToClassList("layers-container");
            Add(layersContainer);
        }
        
        private void BuildGlobalControls()
        {
            var globalSection = new VisualElement();
            globalSection.AddToClassList("global-controls");
            
            // Playback controls row
            var playbackRow = new VisualElement();
            playbackRow.AddToClassList("playback-row");
            playbackRow.style.flexDirection = FlexDirection.Row;
            
            playButton = new Button(OnPlayButtonClicked) { text = "▶ Play" };
            playButton.AddToClassList("play-button");
            playbackRow.Add(playButton);
            
            resetButton = new Button(OnResetButtonClicked) { text = "⟲ Reset" };
            resetButton.AddToClassList("reset-button");
            playbackRow.Add(resetButton);
            
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            playbackRow.Add(spacer);
            
            syncLayersToggle = new Toggle("Sync Layers");
            syncLayersToggle.AddToClassList("sync-toggle");
            syncLayersToggle.RegisterValueChangedCallback(OnSyncLayersChanged);
            playbackRow.Add(syncLayersToggle);
            
            globalSection.Add(playbackRow);
            
            // Time control row
            var timeRow = new VisualElement();
            timeRow.AddToClassList("time-row");
            timeRow.style.flexDirection = FlexDirection.Row;
            
            var timeLabel = new Label("Time:");
            timeLabel.AddToClassList("time-label");
            timeLabel.style.minWidth = 40;
            timeRow.Add(timeLabel);
            
            globalTimeSlider = new Slider(0f, 1f);
            globalTimeSlider.AddToClassList("global-time-slider");
            globalTimeSlider.style.flexGrow = 1;
            globalTimeSlider.RegisterValueChangedCallback(OnGlobalTimeChanged);
            timeRow.Add(globalTimeSlider);
            
            globalSection.Add(timeRow);
            
            Add(globalSection);
        }
        
        private void RebuildLayerSections()
        {
            ClearLayerSections();
            
            if (stateMachine == null || compositionState?.Layers == null) return;
            
            var layersContainer = this.Q<VisualElement>("layers-container");
            if (layersContainer == null) return;
            
            for (int i = 0; i < compositionState.Layers.Length; i++)
            {
                var layerSlot = compositionState.Layers[i];
                var section = CreateLayerSection(layerSlot);
                layersContainer.Add(section.Foldout);
                layerSections.Add(section);
            }
        }
        
        private void ClearLayerSections()
        {
            layerSections.Clear();
            
            var layersContainer = this.Q<VisualElement>("layers-container");
            layersContainer?.Clear();
        }
        
        private LayerSection CreateLayerSection(LayerPreviewSlot layerSlot)
        {
            var section = new LayerSection
            {
                LayerIndex = layerSlot.LayerIndex,
                LayerAsset = layerSlot.LayerAsset
            };
            
            // Create foldout with custom header
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList(LayerSectionClassName);
            section.Foldout.value = true; // Start expanded
            
            // Custom header with layer info and controls
            var header = CreateLayerHeader(section, layerSlot);
            section.Foldout.Q<Toggle>().parent.Insert(1, header);
            section.Foldout.Q<Toggle>().style.display = DisplayStyle.None; // Hide default toggle
            
            // Content area
            section.Content = new VisualElement();
            section.Content.AddToClassList(LayerContentClassName);
            
            BuildLayerContent(section, layerSlot);
            
            section.Foldout.Add(section.Content);
            
            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSection section, LayerPreviewSlot layerSlot)
        {
            var header = new VisualElement();
            header.AddToClassList(LayerHeaderClassName);
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            
            // Foldout arrow (manual)
            var arrow = new Label("▼");
            arrow.AddToClassList("foldout-arrow");
            arrow.style.minWidth = 16;
            header.Add(arrow);
            
            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.AddToClassList("layer-enable-toggle");
            section.EnableToggle.value = layerSlot.IsEnabled;
            section.EnableToggle.RegisterValueChangedCallback(evt => 
                OnLayerEnabledChanged?.Invoke(section.LayerIndex, evt.newValue));
            header.Add(section.EnableToggle);
            
            // Layer name and blend mode
            var nameContainer = new VisualElement();
            nameContainer.style.flexGrow = 1;
            nameContainer.style.flexDirection = FlexDirection.Column;
            
            var nameLabel = new Label($"Layer {layerSlot.LayerIndex}: {layerSlot.LayerAsset.name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);
            
            section.BlendModeLabel = new Label(layerSlot.LayerAsset.BlendMode.ToString());
            section.BlendModeLabel.AddToClassList("layer-blend-mode");
            section.BlendModeLabel.style.fontSize = 10;
            nameContainer.Add(section.BlendModeLabel);
            
            header.Add(nameContainer);
            
            // Weight control
            var weightContainer = new VisualElement();
            weightContainer.style.flexDirection = FlexDirection.Row;
            weightContainer.style.alignItems = Align.Center;
            weightContainer.style.minWidth = 120;
            
            var weightLabel = new Label("Weight:");
            weightLabel.style.minWidth = 45;
            weightContainer.Add(weightLabel);
            
            section.WeightSlider = new Slider(0f, 1f);
            section.WeightSlider.AddToClassList("layer-weight-slider");
            section.WeightSlider.style.flexGrow = 1;
            section.WeightSlider.value = layerSlot.Weight;
            section.WeightSlider.RegisterValueChangedCallback(evt => 
                OnLayerWeightChanged?.Invoke(section.LayerIndex, evt.newValue));
            weightContainer.Add(section.WeightSlider);
            
            section.WeightLabel = new Label(layerSlot.Weight.ToString("F2"));
            section.WeightLabel.style.minWidth = 30;
            weightContainer.Add(section.WeightLabel);
            
            header.Add(weightContainer);
            
            // Manual foldout toggle
            header.RegisterCallback<ClickEvent>(evt =>
            {
                section.Foldout.value = !section.Foldout.value;
                arrow.text = section.Foldout.value ? "▼" : "►";
            });
            
            return header;
        }
        
        private void BuildLayerContent(LayerSection section, LayerPreviewSlot layerSlot)
        {
            // Current selection display
            var selectionRow = new VisualElement();
            selectionRow.style.flexDirection = FlexDirection.Row;
            selectionRow.style.alignItems = Align.Center;
            selectionRow.style.marginBottom = 5;
            
            section.CurrentSelectionLabel = new Label("→ No Selection");
            section.CurrentSelectionLabel.AddToClassList("current-selection");
            section.CurrentSelectionLabel.style.flexGrow = 1;
            selectionRow.Add(section.CurrentSelectionLabel);
            
            section.NavigateButton = new Button(() => OnNavigateToLayer?.Invoke(section.LayerIndex, section.LayerAsset))
            {
                text = "Navigate"
            };
            section.NavigateButton.AddToClassList("navigate-button");
            selectionRow.Add(section.NavigateButton);
            
            section.Content.Add(selectionRow);
            
            // State-specific controls
            section.StateControls = new VisualElement();
            section.StateControls.AddToClassList("state-controls");
            BuildStateControls(section);
            section.Content.Add(section.StateControls);
            
            // Transition-specific controls
            section.TransitionControls = new VisualElement();
            section.TransitionControls.AddToClassList("transition-controls");
            BuildTransitionControls(section);
            section.Content.Add(section.TransitionControls);
            
            // Trigger transition button
            section.TriggerTransitionButton = new Button(() => OnTriggerTransition(section))
            {
                text = "▷ Trigger Transition"
            };
            section.TriggerTransitionButton.AddToClassList("trigger-button");
            section.Content.Add(section.TriggerTransitionButton);
            
            RefreshLayerSection(section);
        }
        
        private void BuildStateControls(LayerSection section)
        {
            // Blend position control
            var blendRow = new VisualElement();
            blendRow.style.flexDirection = FlexDirection.Row;
            blendRow.style.alignItems = Align.Center;
            
            section.BlendLabel = new Label("Blend:");
            section.BlendLabel.style.minWidth = 45;
            blendRow.Add(section.BlendLabel);
            
            section.BlendSlider = new Slider(0f, 1f);
            section.BlendSlider.style.flexGrow = 1;
            section.BlendSlider.RegisterValueChangedCallback(evt => OnBlendPositionChanged(section, evt.newValue));
            blendRow.Add(section.BlendSlider);
            
            section.StateControls.Add(blendRow);
            
            // Clip weights display
            section.ClipWeights = new VisualElement();
            section.ClipWeights.AddToClassList("clip-weights");
            section.StateControls.Add(section.ClipWeights);
        }
        
        private void BuildTransitionControls(LayerSection section)
        {
            // Transition progress
            var progressRow = new VisualElement();
            progressRow.style.flexDirection = FlexDirection.Row;
            progressRow.style.alignItems = Align.Center;
            
            var progressLabel = new Label("Progress:");
            progressLabel.style.minWidth = 60;
            progressRow.Add(progressLabel);
            
            section.TransitionProgressSlider = new Slider(0f, 1f);
            section.TransitionProgressSlider.style.flexGrow = 1;
            section.TransitionProgressSlider.RegisterValueChangedCallback(evt => 
                OnTransitionProgressChanged(section, evt.newValue));
            progressRow.Add(section.TransitionProgressSlider);
            
            section.TransitionControls.Add(progressRow);
            
            // From blend position
            var fromBlendRow = new VisualElement();
            fromBlendRow.style.flexDirection = FlexDirection.Row;
            fromBlendRow.style.alignItems = Align.Center;
            
            var fromLabel = new Label("From Blend:");
            fromLabel.style.minWidth = 70;
            fromBlendRow.Add(fromLabel);
            
            section.FromBlendSlider = new Slider(0f, 1f);
            section.FromBlendSlider.style.flexGrow = 1;
            section.FromBlendSlider.RegisterValueChangedCallback(evt => 
                OnFromBlendPositionChanged(section, evt.newValue));
            fromBlendRow.Add(section.FromBlendSlider);
            
            section.TransitionControls.Add(fromBlendRow);
            
            // To blend position
            var toBlendRow = new VisualElement();
            toBlendRow.style.flexDirection = FlexDirection.Row;
            toBlendRow.style.alignItems = Align.Center;
            
            var toLabel = new Label("To Blend:");
            toLabel.style.minWidth = 70;
            toBlendRow.Add(toLabel);
            
            section.ToBlendSlider = new Slider(0f, 1f);
            section.ToBlendSlider.style.flexGrow = 1;
            section.ToBlendSlider.RegisterValueChangedCallback(evt => 
                OnToBlendPositionChanged(section, evt.newValue));
            toBlendRow.Add(section.ToBlendSlider);
            
            section.TransitionControls.Add(toBlendRow);
        }
        
        #endregion
        
        #region Private Methods - State Management
        
        private void RefreshLayerSection(LayerSection section)
        {
            if (compositionState?.Layers == null || section.LayerIndex >= compositionState.Layers.Length)
                return;
                
            var layerSlot = compositionState.Layers[section.LayerIndex];
            
            // Update header controls
            section.EnableToggle?.SetValueWithoutNotify(layerSlot.IsEnabled);
            section.WeightSlider?.SetValueWithoutNotify(layerSlot.Weight);
            section.WeightLabel.text = layerSlot.Weight.ToString("F2");
            
            // Update selection display
            if (layerSlot.IsTransitionMode)
            {
                section.CurrentSelectionLabel.text = $"→ {layerSlot.TransitionFrom?.name ?? "?"} → {layerSlot.TransitionTo?.name ?? "?"}";
                section.StateControls.style.display = DisplayStyle.None;
                section.TransitionControls.style.display = DisplayStyle.Flex;
                
                // Update transition controls
                section.TransitionProgressSlider?.SetValueWithoutNotify(layerSlot.TransitionProgress);
                section.FromBlendSlider?.SetValueWithoutNotify(layerSlot.BlendPosition.x);
                section.ToBlendSlider?.SetValueWithoutNotify(layerSlot.ToBlendPosition.x);
            }
            else if (layerSlot.IsStateMode)
            {
                section.CurrentSelectionLabel.text = $"→ {layerSlot.SelectedState?.name ?? "(None)"}";
                section.StateControls.style.display = DisplayStyle.Flex;
                section.TransitionControls.style.display = DisplayStyle.None;
                
                // Update state controls
                section.BlendSlider?.SetValueWithoutNotify(layerSlot.BlendPosition.x);
                UpdateClipWeightsDisplay(section, layerSlot);
            }
            else
            {
                section.CurrentSelectionLabel.text = "→ No Selection";
                section.StateControls.style.display = DisplayStyle.None;
                section.TransitionControls.style.display = DisplayStyle.None;
            }
        }
        
        private void UpdateClipWeightsDisplay(LayerSection section, LayerPreviewSlot layerSlot)
        {
            section.ClipWeights.Clear();
            
            if (preview == null) return;
            
            var layerStates = preview.GetLayerStates();
            if (section.LayerIndex >= layerStates.Length) return;
            
            var layerState = layerStates[section.LayerIndex];
            // TODO: Get clip weights from preview and display them
            // This would require extending ILayerCompositionPreview to expose clip weights
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnPlayButtonClicked()
        {
            compositionState?.TogglePlayback();
            OnPlaybackStateChanged?.Invoke(compositionState?.IsPlaying ?? false);
        }
        
        private void OnResetButtonClicked()
        {
            compositionState?.SetMasterTime(0f);
            OnGlobalTimeChanged?.Invoke(0f);
        }
        
        private void OnSyncLayersChanged(ChangeEvent<bool> evt)
        {
            if (compositionState != null)
            {
                compositionState.SyncLayers = evt.newValue;
            }
        }
        
        private void OnGlobalTimeChanged(ChangeEvent<float> evt)
        {
            compositionState?.SetMasterTime(evt.newValue);
            OnGlobalTimeChanged?.Invoke(evt.newValue);
        }
        
        private void OnBlendPositionChanged(LayerSection section, float value)
        {
            compositionState?.SetLayerBlendPosition(section.LayerIndex, new Unity.Mathematics.float2(value, 0));
        }
        
        private void OnTransitionProgressChanged(LayerSection section, float value)
        {
            compositionState?.SetLayerTransitionProgress(section.LayerIndex, value);
        }
        
        private void OnFromBlendPositionChanged(LayerSection section, float value)
        {
            var layerSlot = compositionState?.GetLayer(section.LayerIndex);
            if (layerSlot != null)
            {
                layerSlot.BlendPosition = new Unity.Mathematics.float2(value, layerSlot.BlendPosition.y);
            }
        }
        
        private void OnToBlendPositionChanged(LayerSection section, float value)
        {
            var layerSlot = compositionState?.GetLayer(section.LayerIndex);
            if (layerSlot != null)
            {
                layerSlot.ToBlendPosition = new Unity.Mathematics.float2(value, layerSlot.ToBlendPosition.y);
            }
        }
        
        private void OnTriggerTransition(LayerSection section)
        {
            // TODO: Implement transition triggering
            // This would show a dropdown of available transitions from the current state
            Debug.Log($"Trigger transition for layer {section.LayerIndex}");
        }
        
        private void OnLayerStateChanged(int layerIndex)
        {
            if (layerIndex >= 0 && layerIndex < layerSections.Count)
            {
                RefreshLayerSection(layerSections[layerIndex]);
            }
        }
        
        private void OnMasterTimeChanged(float normalizedTime)
        {
            globalTimeSlider?.SetValueWithoutNotify(normalizedTime);
        }
        
        private void OnPlaybackStateChanged(bool isPlaying)
        {
            if (playButton != null)
            {
                playButton.text = isPlaying ? "⏸ Pause" : "▶ Play";
            }
        }
        
        #endregion
    }
}