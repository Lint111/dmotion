using System;
using System.Collections.Generic;
using System.Linq;
using DMotion.Authoring;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// PropertyChanged event args with layer context.
    /// </summary>
    public class LayerPropertyChangedEventArgs : EventArgs
    {
        /// <summary>Name of the property that changed.</summary>
        public string PropertyName { get; }

        /// <summary>Index of the layer that changed.</summary>
        public int LayerIndex { get; }

        /// <summary>The layer asset.</summary>
        public LayerStateAsset LayerAsset { get; }

        public LayerPropertyChangedEventArgs(
            string propertyName,
            int layerIndex,
            LayerStateAsset layerAsset)
        {
            PropertyName = propertyName;
            LayerIndex = layerIndex;
            LayerAsset = layerAsset;
        }
    }
    
    /// <summary>
    /// Observable state for multi-layer preview composition.
    /// Manages multiple layer states and global playback settings.
    /// Subscribes to EditorState to sync layer selection when navigating inside layers.
    ///
    /// Now works directly with LayerStateAsset (single source of truth).
    /// </summary>
    public class ObservableCompositionState : ObservableObject
    {
        #region Backing Fields

        private StateMachineAsset _rootStateMachine;
        private bool _isPlaying;
        private bool _syncLayers = true;
        private float _masterTime;
        private bool _isSubscribedToEditorState;
        private EditorState _parentEditorState;
        private bool _isBatchUpdating;

        private readonly List<LayerStateAsset> _layers = new();

        #endregion
        
        #region Global Properties
        
        /// <summary>
        /// The root multi-layer state machine being previewed.
        /// </summary>
        public StateMachineAsset RootStateMachine
        {
            get => _rootStateMachine;
            private set => SetProperty(ref _rootStateMachine, value);
        }
        
        /// <summary>
        /// Is global playback running?
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    // Propagate to all layers if synced
                    if (SyncLayers)
                    {
                        foreach (var layer in _layers)
                        {
                            layer.IsPlaying = value;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Whether layers are time-synchronized.
        /// </summary>
        public bool SyncLayers
        {
            get => _syncLayers;
            set => SetProperty(ref _syncLayers, value);
        }
        
        /// <summary>
        /// Master time in seconds (unbounded game time).
        /// When SyncLayers is true, each layer calculates its own normalized time from this value.
        /// </summary>
        public float MasterTime
        {
            get => _masterTime;
            set
            {
                // No clamping - MasterTime is unbounded game time in seconds
                // Each layer handles its own looping based on clip/transition duration
                if (SetProperty(ref _masterTime, Mathf.Max(0f, value)))
                {
                    // Note: We no longer propagate MasterTime directly to layer.NormalizedTime
                    // Instead, LayerCompositionInspectorBuilder.Tick() calculates each layer's
                    // normalized time from MasterTime based on the layer's timeline duration.
                    // This allows layers with different durations to loop at different rates.
                }
            }
        }
        
        /// <summary>
        /// Number of layers.
        /// </summary>
        public int LayerCount => _layers.Count;

        /// <summary>
        /// Read-only access to layers (direct LayerStateAsset references).
        /// </summary>
        public IReadOnlyList<LayerStateAsset> Layers => _layers;
        
        #endregion
        
        #region Initialization

        /// <summary>
        /// Initializes the composition state for a multi-layer state machine.
        /// </summary>
        /// <param name="stateMachine">The multi-layer state machine to preview.</param>
        /// <param name="parentEditorState">The parent EditorState that owns this composition state.</param>
        public void Initialize(StateMachineAsset stateMachine, EditorState parentEditorState)
        {

            if (stateMachine == null)
                throw new ArgumentNullException(nameof(stateMachine));

            if (!stateMachine.IsMultiLayer)
                throw new ArgumentException("State machine must be multi-layer", nameof(stateMachine));

            Clear();

            _parentEditorState = parentEditorState;
            
            // Store reference WITHOUT firing PropertyChanged yet
            // We need to add layers first so LayerCount > 0 when listeners receive the event
            _rootStateMachine = stateMachine;

            int layerIndex = 0;
            foreach (var layerAsset in stateMachine.GetLayers())
            {
                // Initialize layer with metadata
                layerAsset.LayerIndex = layerIndex;
                layerAsset.IsEnabled = true;

                // Subscribe to asset's PropertyChanged events
                // Defensive unsubscribe first to prevent accumulating handlers on re-initialization
                layerAsset.PropertyChanged -= OnLayerAssetPropertyChanged;
                layerAsset.PropertyChanged += OnLayerAssetPropertyChanged;

                _layers.Add(layerAsset);
                layerIndex++;
            }

            // Subscribe to EditorState for selection sync
            SubscribeToEditorState();

            // Restore saved layer selections from persistent storage
            RestoreLayerSelections();

            // Reset global state
            MasterTime = 0f;
            IsPlaying = false;
            SyncLayers = true;
            
            // NOW fire PropertyChanged for RootStateMachine - layers are ready, LayerCount > 0
            OnPropertyChanged(nameof(RootStateMachine));
        }

        /// <summary>
        /// Restores layer selections from PreviewSettings persistence.
        /// </summary>
        private void RestoreLayerSelections()
        {
            if (RootStateMachine == null) return;

            foreach (var layer in _layers)
            {
                var saved = PreviewSettings.instance.GetLayerSelection(RootStateMachine, layer.LayerIndex);
                if (!saved.HasValue)
                    continue;

                var (selectedState, transitionFrom, transitionTo, weight, enabled) = saved.Value;

                // Restore weight and enabled state (base layer weight is locked to 1.0)
                layer.Weight = layer.IsBaseLayer ? 1f : weight;
                layer.IsEnabled = enabled;

                // Restore selection directly on asset with validation
                var stateMachine = layer.NestedStateMachine;
                if (stateMachine == null)
                {
                    Debug.LogWarning($"[CompositionState] Layer {layer.LayerIndex}: NestedStateMachine is null!");
                    continue;
                }

                if (transitionFrom != null && transitionTo != null)
                {
                    // Validate both transition states exist in the state machine hierarchy
                    bool fromExists = false;
                    bool toExists = false;

                    foreach (var stateWithPath in stateMachine.GetAllLeafStates())
                    {
                        if (stateWithPath.State == transitionFrom) fromExists = true;
                        if (stateWithPath.State == transitionTo) toExists = true;
                        if (fromExists && toExists) break;
                    }

                    if (fromExists && toExists)
                    {
                        layer.SetTransitionSelection(transitionFrom, transitionTo);
                    }
                    else
                    {
                        Debug.LogWarning($"[CompositionState] Layer {layer.LayerIndex}: Transition states not found in hierarchy (from={fromExists}, to={toExists})");
                    }
                }
                else if (selectedState != null)
                {
                    // Validate selected state exists in the state machine hierarchy
                    bool stateExists = stateMachine.GetAllLeafStates().Any(s => s.State == selectedState);

                    if (stateExists)
                    {
                        layer.SetStateSelection(selectedState);
                    }
                    else
                    {
                        Debug.LogWarning($"[CompositionState] Layer {layer.LayerIndex}: State {selectedState.name} not found in hierarchy");
                    }
                }
            }
        }

        /// <summary>
        /// Clears the composition state.
        /// </summary>
        public void Clear()
        {
            UnsubscribeFromEditorState();

            foreach (var layer in _layers)
            {
                layer.PropertyChanged -= OnLayerAssetPropertyChanged;
            }

            _layers.Clear();
            RootStateMachine = null;
            MasterTime = 0f;
            IsPlaying = false;
        }

        private void SubscribeToEditorState()
        {
            if (_isSubscribedToEditorState || _parentEditorState == null) return;

            _parentEditorState.PropertyChanged += OnEditorStatePropertyChanged;
            _isSubscribedToEditorState = true;
        }

        private void UnsubscribeFromEditorState()
        {
            if (!_isSubscribedToEditorState || _parentEditorState == null) return;

            _parentEditorState.PropertyChanged -= OnEditorStatePropertyChanged;
            _isSubscribedToEditorState = false;
        }

        #endregion
        
        #region Layer Access

        /// <summary>
        /// Gets a layer by index.
        /// </summary>
        public LayerStateAsset GetLayer(int index)
        {
            if (index < 0 || index >= _layers.Count)
                return null;
            return _layers[index];
        }

        /// <summary>
        /// Finds a layer by its asset reference (returns itself).
        /// </summary>
        public LayerStateAsset FindLayer(LayerStateAsset layerAsset)
        {
            return _layers.Find(l => l == layerAsset);
        }

        /// <summary>
        /// Gets the index of a layer asset.
        /// </summary>
        public int GetLayerIndex(LayerStateAsset layerAsset)
        {
            return _layers.IndexOf(layerAsset);
        }

        #endregion
        
        #region Playback Control
        
        /// <summary>
        /// Toggles global playback.
        /// </summary>
        public void TogglePlayback()
        {
            IsPlaying = !IsPlaying;
        }
        
        /// <summary>
        /// Resets all layers to their initial state.
        /// </summary>
        public void ResetAll()
        {
            MasterTime = 0f;
            IsPlaying = false;

            foreach (var layer in _layers)
            {
                // Reset layer to initial state
                layer.NormalizedTime = 0f;
                layer.TransitionProgress = 0f;
                layer.IsPlaying = false;
                layer.BlendPosition = default;

                // Restore default state selection
                var defaultState = layer.NestedStateMachine?.DefaultState;
                if (defaultState != null)
                {
                    layer.SetStateSelection(defaultState);
                }
            }
        }
        
        #endregion
        
        #region Events

        /// <summary>
        /// Fired when any layer property changes.
        /// </summary>
        public event EventHandler<LayerPropertyChangedEventArgs> LayerChanged;

        private void OnLayerAssetPropertyChanged(LayerStateAsset layer, string propertyName)
        {
            if (layer == null) return;

            // Skip firing LayerChanged for NormalizedTime during batch updates (MasterTime sync)
            // This prevents event storms when MasterTime updates all layers simultaneously
            if (_isBatchUpdating && propertyName == nameof(LayerStateAsset.NormalizedTime))
                return;

            var args = new LayerPropertyChangedEventArgs(
                propertyName,
                layer.LayerIndex,
                layer
            );

            LayerChanged?.Invoke(this, args);

            // Persist changes on relevant property updates
            if (RootStateMachine == null) return;

            if (propertyName is nameof(LayerStateAsset.Weight) or
                nameof(LayerStateAsset.IsEnabled) or
                nameof(LayerStateAsset.SelectedState) or
                nameof(LayerStateAsset.TransitionFrom) or
                nameof(LayerStateAsset.TransitionTo))
            {
                SaveLayerSelection(layer);
            }

            // Notify EditorState for asset property changes that affect UI elsewhere
            if (propertyName is nameof(LayerStateAsset.Weight) or
                nameof(LayerStateAsset.BlendMode))
            {
                EditorState.Instance?.NotifyStateMachineChanged();
            }
        }

        private void SaveLayerSelection(LayerStateAsset layer)
        {
            if (RootStateMachine == null) return;

            PreviewSettings.instance.SaveLayerSelection(
                RootStateMachine,
                layer.LayerIndex,
                layer.SelectedState,
                layer.TransitionFrom,
                layer.TransitionTo,
                layer.Weight,
                layer.IsEnabled);
        }

        /// <summary>
        /// Handles EditorState property changes to sync layer selection.
        /// Only assigns new selections - never clears implicitly.
        /// Use ClearLayerSelection() for explicit clearing via UI.
        /// </summary>
        private void OnEditorStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            if (_parentEditorState == null) return;

            switch (e.PropertyName)
            {
                case nameof(EditorState.SelectedState):
                    SyncStateSelection();
                    break;

                case nameof(EditorState.IsTransitionSelected):
                    SyncTransitionSelection();
                    break;

                // Note: HasSelection=false is intentionally NOT handled
                // Clicking on grid/any-state/exit should NOT clear the layer assignment
                // Use ClearLayerSelection() for explicit clearing
            }
        }

        /// <summary>
        /// Syncs state selection from EditorState to the appropriate layer.
        /// Works both when inside a layer and when viewing from root level.
        /// Uses batch update to fire a single PropertyChanged event.
        /// </summary>
        private void SyncStateSelection()
        {
            var selectedState = _parentEditorState.SelectedState;
            if (selectedState == null) return;

            // Find the layer containing this state
            var layer = FindLayerContainingState(selectedState);
            if (layer == null) return;

            // Use batch update to set state and clear transition atomically
            layer.SetStateSelection(selectedState);
        }

        /// <summary>
        /// Syncs transition selection from EditorState to the appropriate layer.
        /// Works both when inside a layer and when viewing from root level.
        /// Uses batch update to fire a single PropertyChanged event.
        /// </summary>
        private void SyncTransitionSelection()
        {
            if (!_parentEditorState.IsTransitionSelected) return;

            var fromState = _parentEditorState.SelectedTransitionFrom;
            var toState = _parentEditorState.SelectedTransitionTo;
            if (fromState == null || toState == null) return;

            // Find the layer containing both states (they must be in the same layer)
            var layer = FindLayerContainingState(fromState);
            if (layer == null) return;

            // Verify toState is also in this layer
            bool toExists = false;
            var nestedStateMachine = layer.NestedStateMachine;
            if (nestedStateMachine != null)
            {
                foreach (var stateWithPath in nestedStateMachine.GetAllLeafStates())
                {
                    if (stateWithPath.State == toState)
                    {
                        toExists = true;
                        break;
                    }
                }
            }

            if (toExists)
            {
                // Use batch update to set transition and clear state atomically
                layer.SetTransitionSelection(fromState, toState);
            }
        }

        /// <summary>
        /// Finds which layer contains the given state.
        /// </summary>
        private LayerStateAsset FindLayerContainingState(AnimationStateAsset state)
        {
            if (state == null) return null;

            foreach (var layer in _layers)
            {
                var nestedStateMachine = layer.NestedStateMachine;
                if (nestedStateMachine == null) continue;

                foreach (var stateWithPath in nestedStateMachine.GetAllLeafStates())
                {
                    if (stateWithPath.State == state)
                    {
                        return layer;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Explicitly clears the selection for a specific layer.
        /// Call this from UI when user clicks the clear button.
        /// </summary>
        public void ClearLayerSelection(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Count) return;

            var layer = _layers[layerIndex];
            layer.ClearSelection();
        }

        #endregion
    }
}
