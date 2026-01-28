using System;
using System.Collections.Generic;
using System.ComponentModel;
using DMotion;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Observable state for a single layer in multi-layer preview.
    /// Properties automatically fire PropertyChanged events when modified.
    /// </summary>
    public class ObservableLayerState : ObservableObject
    {
        #region Backing Fields
        
        private int _layerIndex;
        private LayerStateAsset _layerAsset;
        private float _weight = 1f;
        private bool _isEnabled = true;
        private LayerBlendMode _blendMode = LayerBlendMode.Override;
        
        // Composition: each layer has its own preview state
        private readonly ObservablePreviewState _previewState = new();
        
        #endregion
        
        #region Constructor
        
        public ObservableLayerState()
        {
            // Forward preview state changes with layer context
            _previewState.PropertyChanged += OnPreviewStatePropertyChanged;
        }
        
        #endregion
        
        #region Layer Properties
        
        /// <summary>
        /// Index of this layer in the state machine.
        /// </summary>
        public int LayerIndex
        {
            get => _layerIndex;
            set => SetProperty(ref _layerIndex, value);
        }
        
        /// <summary>
        /// The layer asset being previewed.
        /// </summary>
        public LayerStateAsset LayerAsset
        {
            get => _layerAsset;
            set => SetProperty(ref _layerAsset, value);
        }
        
        /// <summary>
        /// Layer weight (0-1).
        /// </summary>
        public float Weight
        {
            get => _weight;
            set => SetProperty(ref _weight, Mathf.Clamp01(value));
        }
        
        /// <summary>
        /// Whether this layer is enabled.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
        
        /// <summary>
        /// Layer blend mode.
        /// </summary>
        public LayerBlendMode BlendMode
        {
            get => _blendMode;
            set => SetProperty(ref _blendMode, value);
        }
        
        /// <summary>
        /// The nested state machine for this layer.
        /// </summary>
        public StateMachineAsset NestedStateMachine => LayerAsset?.NestedStateMachine;
        
        /// <summary>
        /// Layer name for display.
        /// </summary>
        public string Name => LayerAsset?.name ?? $"Layer {LayerIndex}";
        
        #endregion
        
        #region Preview State (Composition)
        
        /// <summary>
        /// The preview state for this layer (selection, time, blend).
        /// </summary>
        public ObservablePreviewState PreviewState => _previewState;
        
        // Convenience accessors that delegate to PreviewState
        
        /// <summary>Currently selected state.</summary>
        public AnimationStateAsset SelectedState
        {
            get => _previewState.SelectedState;
            set => _previewState.SelectedState = value;
        }
        
        /// <summary>Whether in transition mode.</summary>
        public bool IsTransitionMode => _previewState.IsTransitionMode;
        
        /// <summary>Normalized time.</summary>
        public float NormalizedTime
        {
            get => _previewState.NormalizedTime;
            set => _previewState.NormalizedTime = value;
        }
        
        /// <summary>Transition progress.</summary>
        public float TransitionProgress
        {
            get => _previewState.TransitionProgress;
            set => _previewState.TransitionProgress = value;
        }
        
        /// <summary>Blend position.</summary>
        public float2 BlendPosition
        {
            get => _previewState.BlendPosition;
            set => _previewState.BlendPosition = value;
        }
        
        /// <summary>Is playback active.</summary>
        public bool IsPlaying
        {
            get => _previewState.IsPlaying;
            set => _previewState.IsPlaying = value;
        }
        
        /// <summary>
        /// Whether this layer has an assigned state or transition.
        /// Unassigned layers should not contribute to the final animation.
        /// </summary>
        public bool IsAssigned => _previewState.SelectedState != null || _previewState.IsTransitionMode;
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes the layer state from a layer asset.
        /// </summary>
        public void Initialize(LayerStateAsset layerAsset, int layerIndex)
        {
            LayerIndex = layerIndex;
            LayerAsset = layerAsset;
            Weight = layerAsset?.Weight ?? 1f;
            BlendMode = layerAsset?.BlendMode ?? LayerBlendMode.Override;
            IsEnabled = true;
            
            // Initialize preview state with default state
            var defaultState = layerAsset?.NestedStateMachine?.DefaultState;
            if (defaultState != null)
            {
                _previewState.SelectState(defaultState);
            }
            else
            {
                _previewState.ClearSelection();
            }
        }
        
        /// <summary>
        /// Resets the layer to its initial state.
        /// </summary>
        public void Reset()
        {
            Weight = LayerAsset?.Weight ?? 1f;
            BlendMode = LayerAsset?.BlendMode ?? LayerBlendMode.Override;
            IsEnabled = true;
            
            var defaultState = LayerAsset?.NestedStateMachine?.DefaultState;
            if (defaultState != null)
            {
                _previewState.SelectState(defaultState);
            }
            else
            {
                _previewState.ClearSelection();
            }
        }
        
        #endregion
        
        #region Event Forwarding
        
        /// <summary>
        /// Fired when a preview state property changes, with layer context.
        /// </summary>
        public event EventHandler<LayerPropertyChangedEventArgs> LayerPropertyChanged;
        
        private void OnPreviewStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Forward with layer context
            var obsArgs = e as ObservablePropertyChangedEventArgs;
            var args = new LayerPropertyChangedEventArgs(
                e.PropertyName,
                LayerIndex,
                LayerAsset,
                obsArgs?.OldValue,
                obsArgs?.NewValue
            );

            LayerPropertyChanged?.Invoke(this, args);
        }
        
        #endregion
    }
    
    /// <summary>
    /// PropertyChanged event args with layer context.
    /// </summary>
    public class LayerPropertyChangedEventArgs : ObservablePropertyChangedEventArgs
    {
        /// <summary>Index of the layer that changed.</summary>
        public int LayerIndex { get; }
        
        /// <summary>The layer asset.</summary>
        public LayerStateAsset LayerAsset { get; }
        
        public LayerPropertyChangedEventArgs(
            string propertyName, 
            int layerIndex, 
            LayerStateAsset layerAsset,
            object oldValue = null, 
            object newValue = null)
            : base(propertyName, oldValue, newValue)
        {
            LayerIndex = layerIndex;
            LayerAsset = layerAsset;
        }
    }
    
    /// <summary>
    /// Observable state for multi-layer preview composition.
    /// Manages multiple layer states and global playback settings.
    /// Subscribes to EditorState to sync layer selection when navigating inside layers.
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

        private readonly List<ObservableLayerState> _layers = new();

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
        /// Master time when layers are synchronized.
        /// </summary>
        public float MasterTime
        {
            get => _masterTime;
            set
            {
                if (SetProperty(ref _masterTime, Mathf.Clamp01(value)))
                {
                    // Propagate to all layers if synced
                    if (SyncLayers)
                    {
                        foreach (var layer in _layers)
                        {
                            layer.NormalizedTime = value;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Number of layers.
        /// </summary>
        public int LayerCount => _layers.Count;
        
        /// <summary>
        /// Read-only access to layers.
        /// </summary>
        public IReadOnlyList<ObservableLayerState> Layers => _layers;
        
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
            RootStateMachine = stateMachine;

            int layerIndex = 0;
            foreach (var layerAsset in stateMachine.GetLayers())
            {
                var layerState = new ObservableLayerState();
                layerState.Initialize(layerAsset, layerIndex);

                // Subscribe to layer changes
                layerState.PropertyChanged += OnLayerPropertyChanged;
                layerState.LayerPropertyChanged += OnLayerPreviewPropertyChanged;

                _layers.Add(layerState);
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
                if (saved.HasValue)
                {
                    var (selectedState, transitionFrom, transitionTo, weight, enabled) = saved.Value;

                    // Restore weight and enabled state
                    layer.Weight = weight;
                    layer.IsEnabled = enabled;

                    // Restore selection
                    if (transitionFrom != null && transitionTo != null)
                    {
                        layer.PreviewState.SelectTransition(transitionFrom, transitionTo);
                    }
                    else if (selectedState != null)
                    {
                        layer.PreviewState.SelectState(selectedState);
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
                layer.PropertyChanged -= OnLayerPropertyChanged;
                layer.LayerPropertyChanged -= OnLayerPreviewPropertyChanged;
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
        public ObservableLayerState GetLayer(int index)
        {
            if (index < 0 || index >= _layers.Count)
                return null;
            return _layers[index];
        }
        
        /// <summary>
        /// Finds a layer by its asset.
        /// </summary>
        public ObservableLayerState FindLayer(LayerStateAsset layerAsset)
        {
            return _layers.Find(l => l.LayerAsset == layerAsset);
        }
        
        /// <summary>
        /// Gets the index of a layer asset.
        /// </summary>
        public int GetLayerIndex(LayerStateAsset layerAsset)
        {
            return _layers.FindIndex(l => l.LayerAsset == layerAsset);
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
                layer.Reset();
            }
        }
        
        #endregion
        
        #region Events

        /// <summary>
        /// Fired when any layer property changes.
        /// </summary>
        public event EventHandler<LayerPropertyChangedEventArgs> LayerChanged;

        private void OnLayerPreviewPropertyChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            // Forward layer preview changes
            LayerChanged?.Invoke(this, e);

            // Persist selection changes
            if (RootStateMachine != null && sender is ObservableLayerState layer)
            {
                // Save on selection-related property changes
                if (e.PropertyName is nameof(ObservablePreviewState.SelectedState) or
                    nameof(ObservablePreviewState.TransitionFrom) or
                    nameof(ObservablePreviewState.TransitionTo))
                {
                    SaveLayerSelection(layer);
                }
            }
        }

        private void OnLayerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ObservableLayerState layer)
            {
                var obsArgs = e as ObservablePropertyChangedEventArgs;
                var args = new LayerPropertyChangedEventArgs(
                    e.PropertyName,
                    layer.LayerIndex,
                    layer.LayerAsset,
                    obsArgs?.OldValue,
                    obsArgs?.NewValue
                );

                LayerChanged?.Invoke(this, args);

                // Persist weight/enabled changes
                if (RootStateMachine != null)
                {
                    if (e.PropertyName is nameof(ObservableLayerState.Weight) or
                        nameof(ObservableLayerState.IsEnabled))
                    {
                        SaveLayerSelection(layer);
                    }
                }
            }
        }

        private void SaveLayerSelection(ObservableLayerState layer)
        {
            PreviewSettings.instance.SaveLayerSelection(
                RootStateMachine,
                layer.LayerIndex,
                layer.PreviewState.SelectedState,
                layer.PreviewState.TransitionFrom,
                layer.PreviewState.TransitionTo,
                layer.Weight,
                layer.IsEnabled);
        }

        /// <summary>
        /// Handles EditorState property changes to sync layer selection.
        /// Only assigns new selections - never clears implicitly.
        /// Use ClearLayerSelection() for explicit clearing via UI.
        /// </summary>
        private void OnEditorStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_parentEditorState == null) return;

            // Only sync when viewing inside a layer
            if (!_parentEditorState.IsInLayer) return;

            var layerIndex = _parentEditorState.CurrentLayerIndex;
            if (layerIndex < 0 || layerIndex >= _layers.Count) return;

            var layerState = _layers[layerIndex];

            switch (e.PropertyName)
            {
                case nameof(EditorState.SelectedState):
                    // Sync state selection to the active layer (only when selecting, not clearing)
                    var selectedState = _parentEditorState.SelectedState;
                    if (selectedState != null)
                    {
                        layerState.PreviewState.SelectState(selectedState);
                    }
                    // Note: Don't clear on null - user must explicitly clear via ClearLayerSelection()
                    break;

                case nameof(EditorState.IsTransitionSelected):
                    // Sync transition selection to the active layer
                    if (_parentEditorState.IsTransitionSelected)
                    {
                        layerState.PreviewState.SelectTransition(
                            _parentEditorState.SelectedTransitionFrom,
                            _parentEditorState.SelectedTransitionTo);
                    }
                    break;

                // Note: HasSelection=false is intentionally NOT handled
                // Clicking on grid/any-state/exit should NOT clear the layer assignment
                // Use ClearLayerSelection() for explicit clearing
            }
        }

        /// <summary>
        /// Explicitly clears the selection for a specific layer.
        /// Call this from UI when user clicks the clear button.
        /// </summary>
        public void ClearLayerSelection(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Count) return;

            var layerState = _layers[layerIndex];
            layerState.PreviewState.ClearSelection();
        }

        #endregion
    }
}
