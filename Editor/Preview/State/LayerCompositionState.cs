using System;
using System.Collections.Generic;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Tracks the preview configuration for all layers in a multi-layer state machine.
    /// Updated when user navigates and selects elements in the graph editor.
    /// </summary>
    internal class LayerCompositionState
    {
        #region Properties
        
        /// <summary>
        /// The root multi-layer state machine.
        /// </summary>
        public StateMachineAsset RootStateMachine { get; private set; }
        
        /// <summary>
        /// Per-layer preview slots.
        /// </summary>
        public LayerPreviewSlot[] Layers { get; private set; }
        
        /// <summary>
        /// Master playback time (0-1 normalized).
        /// </summary>
        public float MasterTime { get; set; }
        
        /// <summary>
        /// Is playback running?
        /// </summary>
        public bool IsPlaying { get; set; }
        
        /// <summary>
        /// The currently active layer (where the user is navigated to).
        /// -1 means at root level (no layer selected).
        /// </summary>
        public int ActiveLayerIndex { get; private set; } = -1;
        
        /// <summary>
        /// Event fired when a layer's preview configuration changes.
        /// </summary>
        public event Action<int> OnLayerChanged;
        
        /// <summary>
        /// Event fired when the active layer changes.
        /// </summary>
        public event Action<int> OnActiveLayerChanged;
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes the state for a multi-layer state machine.
        /// </summary>
        public void Initialize(StateMachineAsset stateMachine)
        {
            RootStateMachine = stateMachine;
            MasterTime = 0f;
            IsPlaying = false;
            ActiveLayerIndex = -1;
            
            if (stateMachine == null || !stateMachine.IsMultiLayer)
            {
                Layers = Array.Empty<LayerPreviewSlot>();
                return;
            }
            
            // Create slots for each layer
            var layerAssets = new List<LayerStateAsset>(stateMachine.GetLayers());
            Layers = new LayerPreviewSlot[layerAssets.Count];
            
            for (int i = 0; i < layerAssets.Count; i++)
            {
                var layer = layerAssets[i];
                Layers[i] = new LayerPreviewSlot
                {
                    LayerIndex = i,
                    LayerAsset = layer,
                    SelectedState = layer.NestedStateMachine?.DefaultState,
                    SelectedTransition = null,
                    TransitionFrom = null,
                    TransitionTo = null,
                    TransitionProgress = 0f,
                    BlendPosition = float2.zero,
                    ToBlendPosition = float2.zero,
                    Weight = layer.Weight,
                    IsEnabled = true
                };
            }
        }
        
        /// <summary>
        /// Clears all state.
        /// </summary>
        public void Clear()
        {
            RootStateMachine = null;
            Layers = Array.Empty<LayerPreviewSlot>();
            MasterTime = 0f;
            IsPlaying = false;
            ActiveLayerIndex = -1;
        }
        
        #endregion
        
        #region Layer Access
        
        /// <summary>
        /// Gets the preview slot for a layer by index.
        /// </summary>
        public LayerPreviewSlot GetLayer(int index)
        {
            if (index < 0 || index >= Layers.Length)
                return null;
            return Layers[index];
        }
        
        /// <summary>
        /// Gets the preview slot for a layer asset.
        /// </summary>
        public LayerPreviewSlot GetLayer(LayerStateAsset layer)
        {
            if (layer == null) return null;
            
            for (int i = 0; i < Layers.Length; i++)
            {
                if (Layers[i].LayerAsset == layer)
                    return Layers[i];
            }
            return null;
        }
        
        /// <summary>
        /// Finds the layer index for a given layer asset.
        /// </summary>
        public int FindLayerIndex(LayerStateAsset layer)
        {
            if (layer == null) return -1;
            
            for (int i = 0; i < Layers.Length; i++)
            {
                if (Layers[i].LayerAsset == layer)
                    return i;
            }
            return -1;
        }
        
        #endregion
        
        #region Selection Handling
        
        /// <summary>
        /// Updates a layer's selection to a state.
        /// </summary>
        public void SetLayerState(int layerIndex, AnimationStateAsset state)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            var slot = Layers[layerIndex];
            slot.SelectedState = state;
            slot.SelectedTransition = null;
            slot.TransitionFrom = null;
            slot.TransitionTo = null;
            slot.TransitionProgress = 0f;
            
            // Initialize blend position from saved settings
            slot.BlendPosition = PreviewSettings.GetBlendPosition(state);
            
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        /// <summary>
        /// Updates a layer's selection to a transition.
        /// </summary>
        public void SetLayerTransition(int layerIndex, AnimationStateAsset fromState, AnimationStateAsset toState, StateOutTransition transition = null)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            var slot = Layers[layerIndex];
            slot.SelectedState = null;
            slot.SelectedTransition = transition;
            slot.TransitionFrom = fromState;
            slot.TransitionTo = toState;
            slot.TransitionProgress = 0f;
            
            // Initialize blend positions from saved settings
            slot.BlendPosition = PreviewSettings.GetBlendPosition(fromState);
            slot.ToBlendPosition = PreviewSettings.GetBlendPosition(toState);
            
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        /// <summary>
        /// Sets the active layer (where the user is currently navigated).
        /// </summary>
        public void SetActiveLayer(int layerIndex)
        {
            if (layerIndex == ActiveLayerIndex) return;
            
            ActiveLayerIndex = layerIndex;
            OnActiveLayerChanged?.Invoke(layerIndex);
        }
        
        /// <summary>
        /// Sets the active layer by layer asset.
        /// </summary>
        public void SetActiveLayer(LayerStateAsset layer)
        {
            SetActiveLayer(FindLayerIndex(layer));
        }
        
        #endregion
        
        #region Blend Control
        
        /// <summary>
        /// Sets the blend position for a layer's current state/transition.
        /// </summary>
        public void SetLayerBlendPosition(int layerIndex, float2 position)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            Layers[layerIndex].BlendPosition = position;
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        /// <summary>
        /// Sets the "to" blend position for a layer's transition.
        /// </summary>
        public void SetLayerToBlendPosition(int layerIndex, float2 position)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            Layers[layerIndex].ToBlendPosition = position;
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        /// <summary>
        /// Sets the transition progress for a layer.
        /// </summary>
        public void SetLayerTransitionProgress(int layerIndex, float progress)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            Layers[layerIndex].TransitionProgress = Mathf.Clamp01(progress);
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        #endregion
        
        #region Weight Control
        
        /// <summary>
        /// Sets the weight for a layer.
        /// </summary>
        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            Layers[layerIndex].Weight = Mathf.Clamp01(weight);
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        /// <summary>
        /// Sets whether a layer is enabled.
        /// </summary>
        public void SetLayerEnabled(int layerIndex, bool enabled)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            
            Layers[layerIndex].IsEnabled = enabled;
            OnLayerChanged?.Invoke(layerIndex);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Preview configuration for a single layer.
    /// </summary>
    internal class LayerPreviewSlot
    {
        /// <summary>Layer index in the state machine.</summary>
        public int LayerIndex;
        
        /// <summary>The layer asset.</summary>
        public LayerStateAsset LayerAsset;
        
        /// <summary>Selected state (null if transition is selected).</summary>
        public AnimationStateAsset SelectedState;
        
        /// <summary>Selected transition (null if state is selected).</summary>
        public StateOutTransition SelectedTransition;
        
        /// <summary>Transition source state.</summary>
        public AnimationStateAsset TransitionFrom;
        
        /// <summary>Transition target state.</summary>
        public AnimationStateAsset TransitionTo;
        
        /// <summary>Current transition progress (0-1).</summary>
        public float TransitionProgress;
        
        /// <summary>Blend position for state, or "from" state in transition.</summary>
        public float2 BlendPosition;
        
        /// <summary>Blend position for "to" state in transition.</summary>
        public float2 ToBlendPosition;
        
        /// <summary>Layer weight (0-1).</summary>
        public float Weight;
        
        /// <summary>Whether the layer is enabled for preview.</summary>
        public bool IsEnabled;
        
        /// <summary>
        /// Returns true if a transition is being previewed.
        /// </summary>
        public bool IsTransitionPreview => SelectedTransition != null || (TransitionFrom != null && TransitionTo != null);
        
        /// <summary>
        /// Returns true if a state is being previewed.
        /// </summary>
        public bool IsStatePreview => SelectedState != null && !IsTransitionPreview;
        
        /// <summary>
        /// Gets the display name for the current selection.
        /// </summary>
        public string SelectionDisplayName
        {
            get
            {
                if (IsTransitionPreview)
                {
                    var fromName = TransitionFrom?.name ?? "Any";
                    var toName = TransitionTo?.name ?? "?";
                    return $"{fromName} → {toName}";
                }
                
                return SelectedState?.name ?? "(None)";
            }
        }
    }
}
