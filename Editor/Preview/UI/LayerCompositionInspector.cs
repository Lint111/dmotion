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
    /// DEPRECATED: Use LayerCompositionInspectorBuilder instead.
    /// This class is no longer used and will be removed in a future version.
    /// </summary>
    [Obsolete("Use LayerCompositionInspectorBuilder in Editor/Preview/Inspectors/LayerComposition/ instead.")]
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
        private ObservableCompositionState compositionState;
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
            public IconButton ClearButton; // Explicit clear assignment button

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
        public void Bind(ILayerCompositionPreview preview, StateMachineAsset stateMachine, ObservableCompositionState compositionState)
        {
            this.preview = preview;
            this.stateMachine = stateMachine;
            this.compositionState = compositionState;
            
            RebuildLayerSections();
            
            // Subscribe to composition state changes
            if (compositionState != null)
            {
                compositionState.PropertyChanged += OnCompositionStatePropertyChanged;
                compositionState.LayerChanged += OnCompositionLayerChanged;
            }
        }
        
        /// <summary>
        /// Unbinds and clears the inspector.
        /// </summary>
        public void Unbind()
        {
            // Unsubscribe from composition state events
            if (compositionState != null)
            {
                compositionState.PropertyChanged -= OnCompositionStatePropertyChanged;
                compositionState.LayerChanged -= OnCompositionLayerChanged;
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
            globalTimeSlider.RegisterValueChangedCallback(OnGlobalTimeChangedCallback);
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
            
            for (int i = 0; i < compositionState.LayerCount; i++)
            {
                var layerState = compositionState.Layers[i];
                var section = CreateLayerSection(layerState);
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
        
        private LayerSection CreateLayerSection(LayerStateAsset layerState)
        {
            var section = new LayerSection
            {
                LayerIndex = layerState.LayerIndex,
                LayerAsset = layerState
            };

            // Create foldout with custom header
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList(LayerSectionClassName);
            section.Foldout.value = true; // Start expanded
            section.Foldout.text = ""; // Clear default text since we use custom header

            // Custom header with layer info and controls
            var header = CreateLayerHeader(section, layerState);

            // Add header content to the Foldout's toggle area properly
            // This keeps the header visible when foldout collapses
            var toggle = section.Foldout.Q<Toggle>();
            if (toggle != null)
            {
                var checkmark = toggle.Q<VisualElement>(className: "unity-foldout__checkmark");
                if (checkmark != null)
                {
                    var toggleContainer = checkmark.parent;
                    var defaultLabel = toggle.Q<Label>(className: "unity-foldout__text");
                    if (defaultLabel != null)
                        defaultLabel.style.display = DisplayStyle.None;

                    toggleContainer.Add(header);
                    header.style.flexGrow = 1;
                }
                else
                {
                    toggle.Add(header);
                    header.style.flexGrow = 1;
                }
            }

            // Content area
            section.Content = new VisualElement();
            section.Content.AddToClassList(LayerContentClassName);

            BuildLayerContent(section, layerState);

            section.Foldout.Add(section.Content);

            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSection section, LayerStateAsset layerState)
        {
            var header = new VisualElement();
            header.AddToClassList(LayerHeaderClassName);
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            // No manual arrow needed - Foldout's native checkmark handles collapse indicator

            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.AddToClassList("layer-enable-toggle");
            section.EnableToggle.value = layerState.IsEnabled;
            section.EnableToggle.style.marginLeft = 5;
            section.EnableToggle.style.marginRight = 5;
            section.EnableToggle.RegisterValueChangedCallback(evt =>
                OnLayerEnabledChanged?.Invoke(section.LayerIndex, evt.newValue));
            // Stop click propagation to prevent foldout toggle when clicking enable checkbox
            section.EnableToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            header.Add(section.EnableToggle);

            // Layer name and blend mode
            var nameContainer = new VisualElement();
            nameContainer.style.flexGrow = 1;
            nameContainer.style.flexDirection = FlexDirection.Column;

            var nameLabel = new Label($"Layer {layerState.LayerIndex}: {layerState.name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);

            section.BlendModeLabel = new Label(layerState.BlendMode.ToString());
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
            section.WeightSlider.value = layerState.Weight;
            section.WeightSlider.RegisterValueChangedCallback(evt =>
                OnLayerWeightChanged?.Invoke(section.LayerIndex, evt.newValue));
            // Stop click propagation to prevent foldout toggle when interacting with slider
            section.WeightSlider.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            section.WeightSlider.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            weightContainer.Add(section.WeightSlider);

            section.WeightLabel = new Label(layerState.Weight.ToString("F2"));
            section.WeightLabel.style.minWidth = 30;
            weightContainer.Add(section.WeightLabel);

            header.Add(weightContainer);

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
        
        private void BuildLayerContent(LayerSection section, LayerStateAsset layerState)
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
            // Blend position control - visibility managed in RefreshLayerSection
            var blendRow = new VisualElement();
            blendRow.name = "blend-row";
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
            if (compositionState?.Layers == null || section.LayerIndex >= compositionState.LayerCount)
                return;
                
            var layerState = compositionState.Layers[section.LayerIndex];

            // Update header controls
            section.EnableToggle?.SetValueWithoutNotify(layerState.IsEnabled);
            section.WeightSlider?.SetValueWithoutNotify(layerState.Weight);
            section.WeightLabel.text = layerState.Weight.ToString("F2");

            // Show/hide clear button based on assignment status
            section.ClearButton?.SetVisible(layerState.IsAssigned);

            // Check if layer is assigned (has state or transition)
            if (!layerState.IsAssigned)
            {
                // Unassigned layer - show message and hide controls
                section.CurrentSelectionLabel.text = $"⚠ Layer {section.LayerIndex} state is unassigned";
                section.CurrentSelectionLabel.style.color = new StyleColor(new Color(0.8f, 0.6f, 0.2f));
                section.StateControls.style.display = DisplayStyle.None;
                section.TransitionControls.style.display = DisplayStyle.None;
                section.TriggerTransitionButton.style.display = DisplayStyle.None;

                // Disable weight controls for unassigned layers (they don't contribute)
                section.WeightSlider.SetEnabled(false);
                section.EnableToggle.SetEnabled(false);
                return;
            }

            // Layer is assigned - enable controls and restore normal color
            section.CurrentSelectionLabel.style.color = StyleKeyword.Null;
            section.WeightSlider.SetEnabled(true);
            section.EnableToggle.SetEnabled(true);
            section.TriggerTransitionButton.style.display = DisplayStyle.Flex;

            // Update selection display
            if (layerState.IsTransitionMode)
            {
                section.CurrentSelectionLabel.text = $"→ {layerState.TransitionFrom?.name ?? "?"} → {layerState.TransitionTo?.name ?? "?"}";
                section.StateControls.style.display = DisplayStyle.None;
                section.TransitionControls.style.display = DisplayStyle.Flex;

                // Update transition controls with persisted blend values
                section.TransitionProgressSlider?.SetValueWithoutNotify(layerState.TransitionProgress);

                // Restore from state blend from PreviewSettings
                var fromBlend = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
                section.FromBlendSlider?.SetValueWithoutNotify(fromBlend.x);
                if (System.Math.Abs(layerState.BlendPosition.x - fromBlend.x) > 0.0001f)
                {
                    layerState.BlendPosition = new Unity.Mathematics.float2(fromBlend.x, fromBlend.y);
                }

                // Restore to state blend from PreviewSettings
                var toBlend = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
                section.ToBlendSlider?.SetValueWithoutNotify(toBlend.x);
                // Note: ToBlendPosition was removed - using main BlendPosition for transitions
            }
            else if (layerState.SelectedState != null)
            {
                section.CurrentSelectionLabel.text = $"→ {layerState.SelectedState?.name ?? "(None)"}";
                section.StateControls.style.display = DisplayStyle.Flex;
                section.TransitionControls.style.display = DisplayStyle.None;

                // Show blend controls only for blend state types
                var selectedState = layerState.SelectedState;
                bool isBlendState = selectedState is LinearBlendStateAsset || selectedState is Directional2DBlendStateAsset;

                var blendRow = section.StateControls?.Q<VisualElement>("blend-row");
                if (blendRow != null)
                    blendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

                // Update state controls with persisted blend values
                if (isBlendState)
                {
                    var persistedBlend = PreviewSettings.GetBlendPosition(selectedState);
                    section.BlendSlider?.SetValueWithoutNotify(persistedBlend.x);

                    // Sync to asset if different
                    if (System.Math.Abs(layerState.BlendPosition.x - persistedBlend.x) > 0.0001f)
                    {
                        layerState.BlendPosition = new Unity.Mathematics.float2(persistedBlend.x, persistedBlend.y);
                    }
                }
                UpdateClipWeightsDisplay(section, layerState);
            }
        }
        
        private void UpdateClipWeightsDisplay(LayerSection section, LayerStateAsset layerState)
        {
            section.ClipWeights.Clear();
            
            if (preview == null) return;
            
            var layerStates = preview.GetLayerStates();
            if (section.LayerIndex >= layerStates.Length) return;
            
            var state = layerStates[section.LayerIndex];
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
            if (compositionState != null)
            {
                compositionState.MasterTime = 0f;
            }
            OnGlobalTimeChanged?.Invoke(0f);
        }
        
        private void OnSyncLayersChanged(ChangeEvent<bool> evt)
        {
            if (compositionState != null)
            {
                compositionState.SyncLayers = evt.newValue;
            }
        }
        
        private void OnGlobalTimeChangedCallback(ChangeEvent<float> evt)
        {
            if (compositionState != null)
            {
                compositionState.MasterTime = evt.newValue;
            }
            OnGlobalTimeChanged?.Invoke(evt.newValue);
        }
        
        private void OnBlendPositionChanged(LayerSection section, float value)
        {
            var layerState = compositionState?.GetLayer(section.LayerIndex);
            if (layerState == null) return;

            layerState.BlendPosition = new Unity.Mathematics.float2(value, 0);

            // Persist to PreviewSettings
            PreviewSettings.SetBlendPosition(layerState.SelectedState, value);
        }
        
        private void OnTransitionProgressChanged(LayerSection section, float value)
        {
            var layerState = compositionState?.GetLayer(section.LayerIndex);
            if (layerState != null)
            {
                layerState.TransitionProgress = value;
            }
        }
        
        private void OnFromBlendPositionChanged(LayerSection section, float value)
        {
            var layerState = compositionState?.GetLayer(section.LayerIndex);
            if (layerState == null) return;

            // Persist to PreviewSettings
            PreviewSettings.SetBlendPosition(layerState.TransitionFrom, value);
            
            // Propagate both blend positions to preview backend
            var fromBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }

        private void OnToBlendPositionChanged(LayerSection section, float value)
        {
            var layerState = compositionState?.GetLayer(section.LayerIndex);
            if (layerState == null) return;

            // Persist to PreviewSettings
            PreviewSettings.SetBlendPosition(layerState.TransitionTo, value);
            
            // Propagate both blend positions to preview backend
            var fromBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }
        
        private void OnTriggerTransition(LayerSection section)
        {
            // TODO: Implement transition triggering
            // This would show a dropdown of available transitions from the current state
        }
        
        private void OnCompositionStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ObservableCompositionState.MasterTime):
                    globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
                    break;
                    
                case nameof(ObservableCompositionState.IsPlaying):
                    if (playButton != null)
                    {
                        playButton.text = compositionState.IsPlaying ? "⏸ Pause" : "▶ Play";
                    }
                    break;
            }
        }
        
        private void OnCompositionLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            // Refresh the specific layer section
            int layerIndex = e.LayerIndex;
            if (layerIndex >= 0 && layerIndex < layerSections.Count)
            {
                RefreshLayerSection(layerSections[layerIndex]);
            }
        }
        
        #endregion
    }
}