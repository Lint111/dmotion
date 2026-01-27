using System;
using System.Collections.Generic;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Tracks the current state of multi-layer preview composition.
    /// Maintains per-layer selections and global playback state.
    /// </summary>
    internal class LayerCompositionState
    {
        #region Properties
        
        /// <summary>
        /// The root multi-layer state machine being previewed.
        /// </summary>
        public StateMachineAsset RootStateMachine { get; private set; }
        
        /// <summary>
        /// Per-layer preview configurations.
        /// </summary>
        public LayerPreviewSlot[] Layers { get; private set; }
        
        /// <summary>
        /// Master playback time (normalized 0-1).
        /// </summary>
        public float MasterTime { get; set; }
        
        /// <summary>
        /// Is playback running across all layers?
        /// </summary>
        public bool IsPlaying { get; set; }
        
        /// <summary>
        /// Whether layers are time-synchronized (true) or independent (false).
        /// </summary>
        public bool SyncLayers { get; set; } = true;
        
        #endregion
        
        #region Events
        
        // Events are now handled through PreviewEventSystem.PropertyChanged
        // This class raises property change events instead of custom events
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes the composition state for the given multi-layer state machine.
        /// </summary>
        public void Initialize(StateMachineAsset stateMachine)
        {
            if (stateMachine == null || !stateMachine.IsMultiLayer)
            {
                throw new ArgumentException("State machine must be multi-layer", nameof(stateMachine));
            }
            
            RootStateMachine = stateMachine;
            
            // Create layer slots
            var layerAssets = stateMachine.GetLayers();
            Layers = new LayerPreviewSlot[layerAssets.Count];
            
            for (int i = 0; i < layerAssets.Count; i++)
            {
                var layerAsset = layerAssets[i];
                Layers[i] = new LayerPreviewSlot
                {
                    LayerIndex = i,
                    LayerAsset = layerAsset,
                    Weight = layerAsset.Weight,
                    IsEnabled = true,
                    
                    // Default to the layer's default state
                    SelectedState = layerAsset.NestedStateMachine?.DefaultState,
                    SelectedTransition = null,
                    
                    BlendPosition = float2.zero,
                    ToBlendPosition = float2.zero,
                    TransitionProgress = 0f
                };
            }
            
            // Reset playback state
            MasterTime = 0f;
            IsPlaying = false;
            SyncLayers = true;
        }
        
        /// <summary>
        /// Clears the composition state.
        /// </summary>
        public void Clear()
        {
            RootStateMachine = null;
            Layers = null;
            MasterTime = 0f;
            IsPlaying = false;
        }
        
        #endregion
        
        #region Layer Management
        
        /// <summary>
        /// Gets the layer slot for the given layer index.
        /// </summary>
        public LayerPreviewSlot GetLayer(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= Layers?.Length)
                return null;
            return Layers[layerIndex];
        }
        
        /// <summary>
        /// Finds the layer index for the given layer asset.
        /// </summary>
        public int FindLayerIndex(LayerStateAsset layerAsset)
        {
            if (Layers == null || layerAsset == null) return -1;
            
            for (int i = 0; i < Layers.Length; i++)
            {
                if (Layers[i].LayerAsset == layerAsset)
                    return i;
            }
            return -1;
        }
        
        /// <summary>
        /// Sets the selected state for a layer.
        /// </summary>
        public void SetLayerState(int layerIndex, AnimationStateAsset state)
        {
            var layer = GetLayer(layerIndex);
            if (layer == null) return;
            
            var oldState = layer.SelectedState;
            layer.SelectedState = state;
            layer.SelectedTransition = null; // Clear transition when selecting state
            layer.TransitionFrom = null;
            layer.TransitionTo = null;
            layer.TransitionProgress = 0f;
            
            // Raise property changed event
            PreviewEventSystem.RaiseStateSelected(RootStateMachine, layer.LayerAsset, layerIndex, null, state);
        }
        
        /// <summary>
        /// Sets the selected transition for a layer.
        /// </summary>
        public void SetLayerTransition(int layerIndex, StateOutTransition transition, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            var layer = GetLayer(layerIndex);
            if (layer == null) return;
            
            layer.SelectedState = null; // Clear state when selecting transition
            layer.SelectedTransition = transition;
            layer.TransitionFrom = fromState;
            layer.TransitionTo = toState;
            layer.TransitionProgress = 0f;
            
            // Raise property changed event
            PreviewEventSystem.RaiseTransitionSelected(RootStateMachine, layer.LayerAsset, layerIndex, null, fromState, toState);
        }
        
        /// <summary>
        /// Sets the layer weight.
        /// </summary>
        public void SetLayerWeight(int layerIndex, float weight)
        {
            var layer = GetLayer(layerIndex);
            if (layer == null) return;
            
            var oldWeight = layer.Weight;
            var newWeight = Mathf.Clamp01(weight);
            layer.Weight = newWeight;
            
            // Raise property changed event
            PreviewEventSystem.RaiseLayerWeightChanged(RootStateMachine, layer.LayerAsset, layerIndex, oldWeight, newWeight);
        }
        
        /// <summary>
        /// Sets whether a layer is enabled.
        /// </summary>
        public void SetLayerEnabled(int layerIndex, bool enabled)
        {
            var layer = GetLayer(layerIndex);
            if (layer == null) return;
            
            var oldEnabled = layer.IsEnabled;
            layer.IsEnabled = enabled;
            
            // Raise property changed event
            PreviewEventSystem.RaiseLayerEnabledChanged(RootStateMachine, layer.LayerAsset, layerIndex, oldEnabled, enabled);
        }
        
        /// <summary>
        /// Sets the blend position for a layer's current state.
        /// </summary>
        public void SetLayerBlendPosition(int layerIndex, float2 position)
        {
            var layer = GetLayer(layerIndex);
            if (layer == null) return;
            
            var oldPosition = layer.BlendPosition;
            layer.BlendPosition = position;
            
            // Raise appropriate blend position event based on dimensionality
            if (position.y == 0 && oldPosition.y == 0)
            {
                // 1D blend
                PreviewEventSystem.RaiseBlendPosition1DChanged(RootStateMachine, layer.LayerAsset, layerIndex, layer.GetEffectiveTarget(), oldPosition.x, position.x);
            }
            else
            {
                // 2D blend
                PreviewEventSystem.RaiseBlendPosition2DChanged(RootStateMachine, layer.LayerAsset, layerIndex, layer.GetEffectiveTarget(), oldPosition, position);
            }
        }
        
        /// <summary>
        /// Sets the transition progress for a layer.
        /// </summary>
        public void SetLayerTransitionProgress(int layerIndex, float progress)
        {
            var layer = GetLayer(layerIndex);
            if (layer == null) return;
            
            var oldProgress = layer.TransitionProgress;
            var newProgress = Mathf.Clamp01(progress);
            layer.TransitionProgress = newProgress;
            
            // Raise property changed event
            PreviewEventSystem.RaiseTransitionProgressChanged(RootStateMachine, layer.LayerAsset, layerIndex, layer.TransitionFrom, layer.TransitionTo, oldProgress, newProgress);
        }
        
        #endregion
        
        #region Playback Control
        
        /// <summary>
        /// Sets the master time and optionally syncs all layers.
        /// </summary>
        public void SetMasterTime(float normalizedTime)
        {
            var oldTime = MasterTime;
            var newTime = Mathf.Clamp01(normalizedTime);
            MasterTime = newTime;
            
            // Raise property changed event
            PreviewEventSystem.RaiseNormalizedTimeChanged(RootStateMachine, oldTime, newTime);
        }
        
        /// <summary>
        /// Sets the playback state.
        /// </summary>
        public void SetPlaying(bool playing)
        {
            if (IsPlaying != playing)
            {
                var oldPlaying = IsPlaying;
                IsPlaying = playing;
                
                // Raise property changed event
                PreviewEventSystem.RaisePlaybackStateChanged(RootStateMachine, oldPlaying, IsPlaying);
            }
        }
        
        /// <summary>
        /// Toggles playback state.
        /// </summary>
        public void TogglePlayback()
        {
            SetPlaying(!IsPlaying);
        }
        
        #endregion
        
        #region Selection Tracking
        
        /// <summary>
        /// Updates layer selection based on editor events.
        /// </summary>
        public void HandleLayerStateSelected(LayerStateAsset layer, StateMachineAsset layerMachine, AnimationStateAsset state)
        {
            int layerIndex = FindLayerIndex(layer);
            if (layerIndex >= 0)
            {
                SetLayerState(layerIndex, state);
            }
        }
        
        /// <summary>
        /// Updates layer selection based on editor events.
        /// </summary>
        public void HandleLayerTransitionSelected(LayerStateAsset layer, StateMachineAsset layerMachine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            int layerIndex = FindLayerIndex(layer);
            if (layerIndex >= 0)
            {
                // Find the transition object
                StateOutTransition transition = null;
                if (fromState != null)
                {
                    foreach (var t in fromState.OutTransitions)
                    {
                        if (t.ToState == toState)
                        {
                            transition = t;
                            break;
                        }
                    }
                }
                
                SetLayerTransition(layerIndex, transition, fromState, toState);
            }
        }
        
        /// <summary>
        /// Clears selection for a specific layer.
        /// </summary>
        public void HandleLayerSelectionCleared(LayerStateAsset layer)
        {
            int layerIndex = FindLayerIndex(layer);
            if (layerIndex >= 0)
            {
                // Reset to default state
                var defaultState = layer.NestedStateMachine?.DefaultState;
                SetLayerState(layerIndex, defaultState);
            }
        }
        
        #endregion
        
        #region Utility
        
        /// <summary>
        /// Gets a summary of the current composition state for debugging.
        /// </summary>
        public string GetDebugSummary()
        {
            if (RootStateMachine == null) return "No state machine";
            
            var summary = $"Root: {RootStateMachine.name}\n";
            summary += $"Playing: {IsPlaying}, Time: {MasterTime:F2}, Sync: {SyncLayers}\n";
            
            if (Layers != null)
            {
                for (int i = 0; i < Layers.Length; i++)
                {
                    var layer = Layers[i];
                    var mode = layer.SelectedTransition != null ? "Transition" : "State";
                    var target = layer.SelectedTransition != null 
                        ? $"{layer.TransitionFrom?.name} → {layer.TransitionTo?.name}"
                        : layer.SelectedState?.name ?? "(None)";
                    
                    summary += $"Layer {i}: {mode} = {target}, Weight = {layer.Weight:F2}, Enabled = {layer.IsEnabled}\n";
                }
            }
            
            return summary;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Configuration for a single layer in the preview composition.
    /// </summary>
    internal class LayerPreviewSlot
    {
        /// <summary>Layer index in the state machine.</summary>
        public int LayerIndex;
        
        /// <summary>The layer asset being previewed.</summary>
        public LayerStateAsset LayerAsset;
        
        // Selection (mutually exclusive)
        /// <summary>Currently selected state (null if transition is selected).</summary>
        public AnimationStateAsset SelectedState;
        
        /// <summary>Currently selected transition (null if state is selected).</summary>
        public StateOutTransition SelectedTransition;
        
        // Transition data (only valid when SelectedTransition != null)
        /// <summary>From state for transition preview.</summary>
        public AnimationStateAsset TransitionFrom;
        
        /// <summary>To state for transition preview.</summary>
        public AnimationStateAsset TransitionTo;
        
        /// <summary>Transition progress (0-1).</summary>
        public float TransitionProgress;
        
        // Blend positions (works for both state and transition)
        /// <summary>Blend position for state, or "from" state in transition.</summary>
        public float2 BlendPosition;
        
        /// <summary>Blend position for "to" state in transition.</summary>
        public float2 ToBlendPosition;
        
        // Layer controls
        /// <summary>Layer weight (0-1).</summary>
        public float Weight;
        
        /// <summary>Whether this layer is enabled.</summary>
        public bool IsEnabled;
        
        /// <summary>
        /// Whether this layer is currently in transition mode.
        /// </summary>
        public bool IsTransitionMode => SelectedTransition != null;
        
        /// <summary>
        /// Whether this layer is currently in state mode.
        /// </summary>
        public bool IsStateMode => SelectedState != null;
        
        /// <summary>
        /// Gets the effective target for preview (state or transition target).
        /// </summary>
        public AnimationStateAsset GetEffectiveTarget()
        {
            return IsTransitionMode ? TransitionTo : SelectedState;
        }
    }
}