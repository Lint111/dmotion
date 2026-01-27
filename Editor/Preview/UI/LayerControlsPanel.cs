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
    /// UI panel for controlling layer preview settings.
    /// Displays layer list with weight sliders, enable toggles, and state selectors.
    /// </summary>
    internal class LayerControlsPanel : VisualElement
    {
        #region Constants
        
        private const string UssClassName = "layer-controls-panel";
        private const string LayerRowClassName = "layer-row";
        private const string LayerNameClassName = "layer-name";
        private const string LayerWeightClassName = "layer-weight";
        private const string LayerToggleClassName = "layer-toggle";
        private const string LayerStateClassName = "layer-state";
        private const string LayerBlendModeClassName = "layer-blend-mode";
        
        #endregion
        
        #region Events
        
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
        /// Fired when a layer's preview state changes.
        /// Parameters: layerIndex, newState
        /// </summary>
        public event Action<int, AnimationStateAsset> OnLayerStateChanged;
        
        /// <summary>
        /// Fired when the global time slider changes.
        /// Parameters: normalizedTime
        /// </summary>
        public event Action<float> OnGlobalTimeChanged;
        
        #endregion
        
        #region State
        
        private ILayerCompositionPreview preview;
        private StateMachineAsset stateMachine;
        private List<LayerRowElements> layerRows = new();
        private Slider globalTimeSlider;
        
        private class LayerRowElements
        {
            public int LayerIndex;
            public Toggle EnableToggle;
            public Label NameLabel;
            public Slider WeightSlider;
            public Label BlendModeLabel;
            public PopupField<AnimationStateAsset> StatePopup;
        }
        
        #endregion
        
        #region Constructor
        
        public LayerControlsPanel()
        {
            AddToClassList(UssClassName);
            
            // Header
            var header = new Label("Layer Composition");
            header.AddToClassList("section-header");
            Add(header);
            
            // Global time control
            var timeRow = new VisualElement();
            timeRow.AddToClassList("global-time-row");
            
            var timeLabel = new Label("Time:");
            timeLabel.AddToClassList("time-label");
            timeRow.Add(timeLabel);
            
            globalTimeSlider = new Slider(0f, 1f);
            globalTimeSlider.AddToClassList("global-time-slider");
            globalTimeSlider.RegisterValueChangedCallback(OnGlobalTimeSliderChanged);
            timeRow.Add(globalTimeSlider);
            
            Add(timeRow);
            
            // Layers container
            var layersContainer = new VisualElement();
            layersContainer.name = "layers-container";
            layersContainer.AddToClassList("layers-container");
            Add(layersContainer);
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Binds the panel to a layer composition preview.
        /// </summary>
        public void Bind(ILayerCompositionPreview preview, StateMachineAsset stateMachine)
        {
            this.preview = preview;
            this.stateMachine = stateMachine;
            
            RebuildLayerRows();
        }
        
        /// <summary>
        /// Unbinds and clears the panel.
        /// </summary>
        public void Unbind()
        {
            preview = null;
            stateMachine = null;
            ClearLayerRows();
        }
        
        /// <summary>
        /// Refreshes the UI to match the current preview state.
        /// </summary>
        public void Refresh()
        {
            if (preview == null) return;
            
            var states = preview.GetLayerStates();
            for (int i = 0; i < layerRows.Count && i < states.Length; i++)
            {
                var row = layerRows[i];
                var state = states[i];
                
                row.EnableToggle.SetValueWithoutNotify(state.IsEnabled);
                row.WeightSlider.SetValueWithoutNotify(state.Weight);
                row.NameLabel.text = state.Name;
                row.BlendModeLabel.text = state.BlendMode.ToString();
                
                if (row.StatePopup != null)
                {
                    row.StatePopup.SetValueWithoutNotify(state.CurrentState);
                }
            }
        }
        
        /// <summary>
        /// Sets the global time slider value without triggering events.
        /// </summary>
        public void SetGlobalTime(float normalizedTime)
        {
            globalTimeSlider?.SetValueWithoutNotify(normalizedTime);
        }
        
        #endregion
        
        #region Private Methods
        
        private void RebuildLayerRows()
        {
            ClearLayerRows();
            
            if (preview == null || stateMachine == null) return;
            
            var layersContainer = this.Q<VisualElement>("layers-container");
            if (layersContainer == null) return;
            
            var states = preview.GetLayerStates();
            
            for (int i = 0; i < states.Length; i++)
            {
                var layerState = states[i];
                var row = CreateLayerRow(i, layerState);
                layersContainer.Add(row);
            }
        }
        
        private void ClearLayerRows()
        {
            layerRows.Clear();
            
            var layersContainer = this.Q<VisualElement>("layers-container");
            layersContainer?.Clear();
        }
        
        private VisualElement CreateLayerRow(int layerIndex, LayerPreviewState state)
        {
            var row = new VisualElement();
            row.AddToClassList(LayerRowClassName);
            
            var elements = new LayerRowElements { LayerIndex = layerIndex };
            
            // Enable toggle
            elements.EnableToggle = new Toggle();
            elements.EnableToggle.AddToClassList(LayerToggleClassName);
            elements.EnableToggle.value = state.IsEnabled;
            elements.EnableToggle.tooltip = "Enable/disable this layer";
            int capturedIndex = layerIndex;
            elements.EnableToggle.RegisterValueChangedCallback(evt => 
                OnLayerToggleChanged(capturedIndex, evt.newValue));
            row.Add(elements.EnableToggle);
            
            // Layer name
            elements.NameLabel = new Label(state.Name);
            elements.NameLabel.AddToClassList(LayerNameClassName);
            row.Add(elements.NameLabel);
            
            // Blend mode indicator
            elements.BlendModeLabel = new Label(state.BlendMode.ToString());
            elements.BlendModeLabel.AddToClassList(LayerBlendModeClassName);
            elements.BlendModeLabel.tooltip = state.BlendMode == LayerBlendMode.Additive 
                ? "Additive: Adds to layers below" 
                : "Override: Replaces layers below";
            row.Add(elements.BlendModeLabel);
            
            // Weight slider
            elements.WeightSlider = new Slider(0f, 1f);
            elements.WeightSlider.AddToClassList(LayerWeightClassName);
            elements.WeightSlider.value = state.Weight;
            elements.WeightSlider.tooltip = "Layer weight (0 = no influence, 1 = full influence)";
            elements.WeightSlider.RegisterValueChangedCallback(evt => 
                OnLayerWeightSliderChanged(capturedIndex, evt.newValue));
            row.Add(elements.WeightSlider);
            
            // State selector (if we have available states)
            var availableStates = GetAvailableStatesForLayer(layerIndex);
            if (availableStates.Count > 0)
            {
                elements.StatePopup = new PopupField<AnimationStateAsset>(
                    availableStates, 
                    state.CurrentState,
                    FormatStateName,
                    FormatStateName);
                elements.StatePopup.AddToClassList(LayerStateClassName);
                elements.StatePopup.tooltip = "Select state to preview in this layer";
                elements.StatePopup.RegisterValueChangedCallback(evt => 
                    OnLayerStatePopupChanged(capturedIndex, evt.newValue));
                row.Add(elements.StatePopup);
            }
            
            layerRows.Add(elements);
            return row;
        }
        
        private List<AnimationStateAsset> GetAvailableStatesForLayer(int layerIndex)
        {
            var states = new List<AnimationStateAsset>();
            
            if (stateMachine == null) return states;
            
            // Get the layer asset
            var layer = stateMachine.GetLayer(layerIndex);
            if (layer?.NestedStateMachine == null) return states;
            
            // Add all leaf states from the layer's state machine
            foreach (var stateWithPath in layer.NestedStateMachine.GetAllLeafStates())
            {
                states.Add(stateWithPath.State);
            }
            
            // Also add direct states (non-SubStateMachine)
            foreach (var state in layer.NestedStateMachine.States)
            {
                if (state is not SubStateMachineStateAsset && !states.Contains(state))
                {
                    states.Add(state);
                }
            }
            
            return states;
        }
        
        private string FormatStateName(AnimationStateAsset state)
        {
            return state != null ? state.name : "(None)";
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnLayerToggleChanged(int layerIndex, bool enabled)
        {
            preview?.SetLayerEnabled(layerIndex, enabled);
            OnLayerEnabledChanged?.Invoke(layerIndex, enabled);
        }
        
        private void OnLayerWeightSliderChanged(int layerIndex, float weight)
        {
            preview?.SetLayerWeight(layerIndex, weight);
            OnLayerWeightChanged?.Invoke(layerIndex, weight);
        }
        
        private void OnLayerStatePopupChanged(int layerIndex, AnimationStateAsset state)
        {
            preview?.SetLayerState(layerIndex, state);
            OnLayerStateChanged?.Invoke(layerIndex, state);
        }
        
        private void OnGlobalTimeSliderChanged(ChangeEvent<float> evt)
        {
            preview?.SetGlobalNormalizedTime(evt.newValue);
            OnGlobalTimeChanged?.Invoke(evt.newValue);
        }
        
        #endregion
    }
}
