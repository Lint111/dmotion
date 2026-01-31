using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    internal partial class AnimationPreviewWindow
    {
        #region Composition State Event Handling
        
        private void OnCompositionStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ObservableCompositionState.RootStateMachine):
                    // CompositionState was (re-)initialized with a new state machine
                    // Re-subscribe to LayerChanged to handle domain reload restoration
                    LogDebug("OnCompositionStatePropertyChanged: RootStateMachine changed");
                    if (_subscribedEditorState?.CompositionState != null)
                    {
                        _subscribedEditorState.CompositionState.LayerChanged -= OnCompositionLayerChanged;
                        int count = _subscribedEditorState.CompositionState.LayerCount;
                        LogDebug($"OnCompositionStatePropertyChanged: LayerCount={count}");
                        if (count > 0)
                        {
                            _subscribedEditorState.CompositionState.LayerChanged += OnCompositionLayerChanged;
                            LogDebug("OnCompositionStatePropertyChanged: Subscribed to LayerChanged");
                        }
                    }
                    
                    // IMPORTANT: Create backend preview BEFORE rebuilding the builder UI
                    // This ensures the builder gets a valid preview reference for state sync
                    if (currentPreviewType == PreviewType.LayerComposition)
                    {
                        EnsureLayerCompositionPreview();
                    }
                    
                    // Rebuild layer composition UI with the now-valid preview
                    var backend = previewSession?.Backend as PlayableGraphBackend;
                    var layerPreview = backend?.LayerComposition;
                    layerCompositionBuilder?.Build(
                        CompositionState?.RootStateMachine,
                        CompositionState,
                        layerPreview,
                        _logger);  // Pass parent logger for hierarchical logging
                    Repaint();
                    break;
            }

            if (currentPreviewType != PreviewType.LayerComposition) return; 

            switch (e.PropertyName) 
            {
                case nameof(ObservableCompositionState.MasterTime):
                    previewSession?.SetNormalizedTime(CompositionState.MasterTime); 
                    Repaint();
                    break; 

                case nameof(ObservableCompositionState.IsPlaying):
                    previewSession?.SetPlaying(CompositionState.IsPlaying);
                    Repaint();
                    break;

                case nameof(ObservableCompositionState.SyncLayers):
                    // Sync state changed - may need to update layer times
                    Repaint();
                    break;
            }
        }
        
        private void OnCompositionLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            LogTrace($"OnCompositionLayerChanged: Property={e.PropertyName}, LayerIndex={e.LayerIndex}, PreviewType={currentPreviewType}");
            
            if (currentPreviewType != PreviewType.LayerComposition)
            {
                LogTrace($"OnCompositionLayerChanged: Skipping - PreviewType is {currentPreviewType}");
                return;
            }

            // Get the backend's layer composition preview to propagate changes
            var backend = previewSession?.Backend as PlayableGraphBackend;
            var layerPreview = backend?.LayerComposition;
            
            // Ensure preview exists before trying to update it
            // This handles cases where LayerChanged fires before the preview is created
            if (layerPreview == null)
            {
                EnsureLayerCompositionPreview();
                // Re-fetch backend AND layerPreview after ensuring preview exists
                backend = previewSession?.Backend as PlayableGraphBackend;
                layerPreview = backend?.LayerComposition;
            }
            
            var layer = CompositionState?.GetLayer(e.LayerIndex);

            // Debug: Log if we have valid references
            if (layerPreview == null)
            {
                LogWarning($"OnCompositionLayerChanged: layerPreview is null for property {e.PropertyName}");
            }
            if (layer == null)
            {
                LogWarning($"OnCompositionLayerChanged: layer is null for index {e.LayerIndex}");
            }

            if (layer != null && layerPreview != null)
            {
                // Propagate observable state changes to the backend preview
                switch (e.PropertyName)
                {
                    case nameof(LayerStateAsset.Weight):
                        layerPreview.SetLayerWeight(e.LayerIndex, layer.Weight);
                        break;

                    case nameof(LayerStateAsset.IsEnabled):
                        layerPreview.SetLayerEnabled(e.LayerIndex, layer.IsEnabled);
                        break;

                    case nameof(LayerStateAsset.SelectedState):
                        // Update backend state - handles both state selection and clearing
                        // When SelectedState is null (cleared or in transition mode), 
                        // SetLayerState with null will zero the layer's weight in the mixer
                        LogDebug($"OnCompositionLayerChanged: Setting layer {e.LayerIndex} state to {layer.SelectedState?.name ?? "null"}");
                        layerPreview.SetLayerState(e.LayerIndex, layer.SelectedState);
                        
                        // Also sync blend position for the new state
                        if (layer.SelectedState != null)
                        {
                            var blendPos = PreviewSettings.GetBlendPosition(layer.SelectedState);
                            layer.BlendPosition = new float2(blendPos.x, blendPos.y);
                            layerPreview.SetLayerBlendPosition(e.LayerIndex, layer.BlendPosition);
                        }
                        break;

                    case nameof(LayerStateAsset.TransitionFrom):
                    case nameof(LayerStateAsset.TransitionTo):
                        // Transition mode: use the new transition API for proper crossfade preview
                        if (layer.IsTransitionMode && layer.TransitionTo != null)
                        {
                            LogDebug($"OnCompositionLayerChanged: Setting layer {e.LayerIndex} transition from {layer.TransitionFrom?.name ?? "null"} to {layer.TransitionTo?.name}");
                            layerPreview.SetLayerTransition(e.LayerIndex, layer.TransitionFrom, layer.TransitionTo);
                            
                            // Sync blend positions for both states
                            var fromBlendPos = layer.TransitionFrom != null 
                                ? PreviewSettings.GetBlendPosition(layer.TransitionFrom) 
                                : Vector2.zero;
                            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
                            layerPreview.SetLayerTransitionBlendPositions(
                                e.LayerIndex, 
                                new float2(fromBlendPos.x, fromBlendPos.y),
                                new float2(toBlendPos.x, toBlendPos.y));
                            
                            // Sync initial transition progress
                            layerPreview.SetLayerTransitionProgress(e.LayerIndex, layer.TransitionProgress);
                        }
                        break;

                    case nameof(LayerStateAsset.TransitionProgress):
                        // Update transition progress in backend
                        if (layer.IsTransitionMode)
                        {
                            layerPreview.SetLayerTransitionProgress(e.LayerIndex, layer.TransitionProgress);
                        }
                        break;

                    case nameof(LayerStateAsset.BlendPosition):
                        if (layer.IsTransitionMode)
                        {
                            // In transition mode, BlendPosition is used for the "from" state
                            // The "to" state blend position is managed separately
                            // For now, update both with the same position
                            layerPreview.SetLayerTransitionBlendPositions(
                                e.LayerIndex, 
                                layer.BlendPosition,
                                layer.BlendPosition);
                        }
                        else
                        {
                            layerPreview.SetLayerBlendPosition(e.LayerIndex, layer.BlendPosition);
                        }
                        break;
                }
            }

            // Refresh the builder UI for any layer property change
            switch (e.PropertyName)
            {
                case nameof(LayerStateAsset.SelectedState):
                case nameof(LayerStateAsset.TransitionFrom):
                case nameof(LayerStateAsset.Weight):
                case nameof(LayerStateAsset.IsEnabled):
                case nameof(LayerStateAsset.BlendPosition):
                case nameof(LayerStateAsset.TransitionProgress):
                    layerCompositionBuilder?.Refresh();
                    break;
            }

            Repaint();
        }
        
        /// <summary>
        /// Ensures the layer composition preview exists in the backend.
        /// Must be called BEFORE building the inspector UI.
        /// Only syncs initial state on FIRST creation (or when state machine changes) to avoid 
        /// overwriting state changes from ObservableCompositionState during selection updates.
        /// Also updates the builder's preview reference for playback time propagation.
        /// </summary>
        private void EnsureLayerCompositionPreview()
        {
            if (currentPreviewType != PreviewType.LayerComposition) return;
            if (CompositionState?.RootStateMachine == null) return;

            var backend = previewSession?.Backend as PlayableGraphBackend;
            var targetStateMachine = CompositionState.RootStateMachine;
            
            // Check if preview already exists FOR THE SAME STATE MACHINE
            var existingPreview = backend?.LayerComposition as LayerCompositionPreview;
            if (existingPreview != null && existingPreview.IsInitialized && 
                existingPreview.StateMachine == targetStateMachine)
            {
                // Preview already exists for this state machine - don't re-sync state 
                // (would overwrite correct state with stale data during selection changes)
                // Just ensure the builder has the reference
                layerCompositionBuilder?.SetPreviewBackend(existingPreview);
                return;
            }
            
            // Create/recreate layer composition preview in the backend
            // This happens on first creation OR when switching to a different state machine
            backend?.CreateLayerCompositionPreview(targetStateMachine);

            // Sync initial state selections from composition state to backend
            var layerPreview = backend?.LayerComposition;
            if (layerPreview != null && CompositionState != null)
            {
                for (int i = 0; i < CompositionState.Layers.Count; i++)
                {
                    var layer = CompositionState.GetLayer(i);
                    if (layer == null) continue;

                    if (layer.IsTransitionMode && layer.TransitionTo != null)
                    {
                        // Transition mode: use the transition API for proper crossfade preview
                        layerPreview.SetLayerTransition(i, layer.TransitionFrom, layer.TransitionTo);
                        
                        // Sync blend positions for both states
                        var fromBlendPos = layer.TransitionFrom != null 
                            ? PreviewSettings.GetBlendPosition(layer.TransitionFrom) 
                            : Vector2.zero;
                        var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
                        layerPreview.SetLayerTransitionBlendPositions(
                            i, 
                            new float2(fromBlendPos.x, fromBlendPos.y),
                            new float2(toBlendPos.x, toBlendPos.y));
                        
                        // Sync transition progress
                        layerPreview.SetLayerTransitionProgress(i, layer.TransitionProgress);
                    }
                    else if (layer.SelectedState != null)
                    {
                        // Single-state mode: play the selected state
                        layerPreview.SetLayerState(i, layer.SelectedState);
                        
                        // Sync blend position from persisted settings
                        var blendPos = PreviewSettings.GetBlendPosition(layer.SelectedState);
                        layer.BlendPosition = new float2(blendPos.x, blendPos.y);
                        layerPreview.SetLayerBlendPosition(i, layer.BlendPosition);
                    }

                    // Sync weight and enabled state
                    layerPreview.SetLayerWeight(i, layer.Weight);
                    layerPreview.SetLayerEnabled(i, layer.IsEnabled);
                }
            }
            
            // Update the builder's preview reference so Tick() can propagate time
            layerCompositionBuilder?.SetPreviewBackend(layerPreview);
        }
        
        #endregion
        
        #region EditorState Event Handlers
        
        private void OnEditorStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(EditorState.RootStateMachine):
                    // Root state machine changed
                    var newRootMachine = EditorState.Instance.RootStateMachine;
                    bool isSameMachine = newRootMachine == currentStateMachine;
                    
                    SetContext(newRootMachine);
                    
                    // Only clear selection when switching to a DIFFERENT state machine
                    // On domain reload, the same machine is re-set - preserve restored selection
                    if (!isSameMachine)
                    {
                        ClearSelection();
                    } 
                    UpdateSelectionUI();
                    break;
                    
                case nameof(EditorState.PreviewType):
                    // Sync preview type from EditorState
                    var editorPreviewType = EditorState.Instance.PreviewType;
                    var newPreviewType = editorPreviewType == EditorPreviewType.LayerComposition 
                        ? PreviewType.LayerComposition 
                        : PreviewType.SingleState;
                    if (currentPreviewType != newPreviewType)
                    {
                        currentPreviewType = newPreviewType;
                        UpdatePreviewTypeDropdownText();
                        UpdateSelectionUI();
                    }
                    break;
                    
                case nameof(EditorState.SelectedState):
                    // Selection changed - no need to SetContext, root doesn't change
                    var state = EditorState.Instance.SelectedState;
                    // Only update UI if selection actually changed to prevent unnecessary rebuilds
                    if (selectedState != state)
                    {
                        selectedState = state;
                        selectedTransitionFrom = null;
                        selectedTransitionTo = null;
                        isAnyStateSelected = false;
                        currentSelectionType = state != null ? SelectionType.State : SelectionType.None;
                        currentStateSpeed = state != null && state.Speed > 0 ? state.Speed : 1f;
                        UpdateSelectionUI();
                    }
                    break;
                    
                case nameof(EditorState.SelectedTransitionFrom):
                case nameof(EditorState.SelectedTransitionTo):
                case nameof(EditorState.IsTransitionSelected):
                    if (EditorState.Instance.IsTransitionSelected)
                    {
                        var newTransitionFrom = EditorState.Instance.SelectedTransitionFrom;
                        var newTransitionTo = EditorState.Instance.SelectedTransitionTo;

                        // Only update UI if transition actually changed
                        if (selectedTransitionFrom != newTransitionFrom || selectedTransitionTo != newTransitionTo)
                        {
                            selectedState = null;
                            selectedTransitionFrom = newTransitionFrom;
                            selectedTransitionTo = newTransitionTo;
                            isAnyStateSelected = EditorState.Instance.IsAnyStateSelected;
                            currentSelectionType = isAnyStateSelected ? SelectionType.AnyStateTransition : SelectionType.Transition;
                            UpdateSelectionUI();
                        }
                    }
                    break;
                    
                case nameof(EditorState.IsAnyStateSelected):
                    if (EditorState.Instance.IsAnyStateSelected && !EditorState.Instance.IsTransitionSelected)
                    {
                        selectedState = null;
                        selectedTransitionFrom = null;
                        selectedTransitionTo = null;
                        isAnyStateSelected = true;
                        currentSelectionType = SelectionType.AnyState;
                        UpdateSelectionUI();
                    }
                    break;

                // Note: We don't clear preview selection when EditorState.HasSelection becomes false
                // This allows the preview to maintain the last state/transition even when clicking
                // on non-selectable elements (empty space, parameters, etc.) in the state machine editor
            }
        }
        
        private void OnEditorStateStructureChanged(object sender, StructureChangedEventArgs e)
        {
            if (e.ChangeType == StructureChangeType.GeneralChange && 
                EditorState.Instance.RootStateMachine == currentStateMachine)
            {
                // Refresh the UI in case the selected element was modified
                UpdateSelectionUI();
            }
        }
        
        private void OnPreviewStateChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            var previewState = EditorState.Instance.PreviewState;
            
            switch (e.PropertyName)
            {
                case nameof(ObservablePreviewState.NormalizedTime):
                    // Time changed via EditorState
                    Repaint();
                    break;
                    
                case nameof(ObservablePreviewState.BlendPosition):
                    // BlendPosition is used for both single-state preview and transition FROM state
                    if (currentSelectionType == SelectionType.Transition || currentSelectionType == SelectionType.AnyStateTransition)
                    {
                        // In transition mode, this is the FROM state's blend position
                        OnTransitionFromBlendPositionChangedInternal(previewState.TransitionFrom,
                            new Vector2(previewState.BlendPosition.x, previewState.BlendPosition.y));
                    }
                    else
                    {
                        // Single state mode
                        OnBlendPositionChanged(previewState.SelectedState, previewState.BlendPosition);
                    }
                    break;
                    
                case nameof(ObservablePreviewState.ToBlendPosition):
                    OnTransitionToBlendPositionChangedInternal(previewState.TransitionTo, 
                        new Vector2(previewState.ToBlendPosition.x, previewState.ToBlendPosition.y));
                    break;
                    
                case nameof(ObservablePreviewState.TransitionProgress):
                    OnTransitionProgressChangedInternal(previewState.TransitionFrom, previewState.TransitionTo, previewState.TransitionProgress);
                    break;
                    
                case nameof(ObservablePreviewState.SoloClipIndex):
                    OnClipSelectedForPreviewInternal(previewState.SelectedState, previewState.SoloClipIndex);
                    break;
            }
        }

        #endregion

        #region Timeline Events

        private void OnTimelineTimeChanged(float time)
        {
            // Update preview sample time
            var timelineScrubber = stateInspectorBuilder?.TimelineScrubber;
            previewSession?.SetNormalizedTime(timelineScrubber?.NormalizedTime ?? 0);
            
            // Update EditorState for other listeners
            if (selectedState != null)
            {
                EditorState.Instance.PreviewState.NormalizedTime = timelineScrubber?.NormalizedTime ?? 0;
            }
            
            Repaint();
        }
        
        private void OnStateSpeedChanged(float newSpeed)
        {
            // Store the base state speed - will be combined with weighted clip speed in Update
            currentStateSpeed = newSpeed > 0 ? newSpeed : 1f;
        }
        
        private void OnTimelinePlayStateChanged(bool isPlaying)
        {
            // Forward play state to preview session (important for ECS mode)
            previewSession?.SetPlaying(isPlaying);
        }

        #endregion
        
        #region Blend Position Events
        
        private void OnBlendPositionChanged(AnimationStateAsset state, float2 blendPosition)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            if (state is LinearBlendStateAsset)
            {
                previewSession?.SetBlendPosition1D(blendPosition.x);
            }
            else
            {
                previewSession?.SetBlendPosition2D(blendPosition);
            }
            Repaint();
        }
        
        private void OnClipSelectedForPreviewInternal(AnimationStateAsset state, int clipIndex)
        {
            // Only update if this is the currently selected state
            if (selectedState != state) return;
            
            // Set solo clip mode: -1 = blended, >= 0 = individual clip
            previewSession?.SetSoloClip(clipIndex);
            Repaint();
        }
        
        #endregion
        
        #region Transition Events
        
        private void OnTransitionProgressChangedInternal(AnimationStateAsset fromState, AnimationStateAsset toState, float progress)
        {
            // Only update if this matches the currently selected transition
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // For Any State transitions, fromState will be null
            bool matchesFrom = (isAnyStateSelected && fromState == null) || (fromState == selectedTransitionFrom);
            bool matchesTo = toState == selectedTransitionTo;
            
            if (!matchesFrom || !matchesTo) return;
            
            previewSession?.SetTransitionProgress(progress);
            Repaint();
        }
        
        private void OnTransitionFromBlendPositionChangedInternal(AnimationStateAsset fromState, Vector2 blendPosition)
        {
            // Only update if we're in a transition and this is the from state
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // Check if this matches the from state of the current transition
            var currentFromState = isAnyStateSelected ? null : selectedTransitionFrom;
            if (fromState != currentFromState) return;
            
            previewSession?.SetTransitionFromBlendPosition(new float2(blendPosition.x, blendPosition.y));
            
            // Update timeline durations to reflect new blend position
            var toBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionTo);
            transitionInspectorBuilder?.Timeline?.UpdateDurationsForBlendPosition(blendPosition, toBlendPos);
            
            Repaint();
        }
        
        private void OnTransitionToBlendPositionChangedInternal(AnimationStateAsset toState, Vector2 blendPosition)
        {
            // Only update if we're in a transition and this is the to state
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // Check if this matches the to state of the current transition
            if (toState != selectedTransitionTo) return;
            
            previewSession?.SetTransitionToBlendPosition(new float2(blendPosition.x, blendPosition.y));
            
            // Update timeline durations to reflect new blend position
            var fromBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionFrom);
            transitionInspectorBuilder?.Timeline?.UpdateDurationsForBlendPosition(fromBlendPos, blendPosition);
            
            Repaint();
        }
        
        private void OnTransitionPropertiesChanged()
        {
            // Transition duration or exit time changed - rebuild ECS timeline
            if (currentSelectionType != SelectionType.Transition && currentSelectionType != SelectionType.AnyStateTransition)
                return;
            
            // Get current blend positions and trigger rebuild
            var fromBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(selectedTransitionTo);
            
            previewSession?.RebuildTransitionTimeline(
                new float2(fromBlendPos.x, fromBlendPos.y),
                new float2(toBlendPos.x, toBlendPos.y));
        }
        
        #endregion
    }
}
