using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Timeline Configuration
        
        private void ConfigureLayerTimeline(LayerSectionData section, LayerStateAsset layerState)
        {
            if (layerState.IsTransitionMode)
            {
                // Get blend positions for config
                var fromBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
                var toBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
                
                // Timeline configuration depends on loop mode and play state
                switch (layerState.TransitionLoopMode)
                {
                    case TransitionLoopMode.TransitionLoop:
                        // Show full transition timeline with ghost bars
                        var loopConfig = CreateTransitionConfig(
                            layerState.TransitionFrom,
                            layerState.TransitionTo,
                            new float2(fromBlendPos.x, fromBlendPos.y),
                            new float2(toBlendPos.x, toBlendPos.y),
                            includeGhostBars: true);
                        section.Timeline.Duration = loopConfig.Timing.TotalDuration;
                        section.Timeline.NormalizedTime = layerState.NormalizedTime;
                        break;
                        
                    case TransitionLoopMode.FromLoop:
                    case TransitionLoopMode.ToLoop:
                        // Show different timeline based on play state
                        switch (layerState.TransitionPlayState)
                        {
                            case TransitionPlayState.LoopingFrom:
                                float fromDuration = AnimationStateUtils.GetEffectiveDuration(layerState.TransitionFrom, fromBlendPos);
                                section.Timeline.Duration = fromDuration > 0 ? fromDuration : 1f;
                                section.Timeline.NormalizedTime = layerState.NormalizedTime;
                                break;
                                
                            case TransitionPlayState.Transitioning:
                                // Show transition timeline without ghost bars (looping states provide context)
                                var transConfig = CreateTransitionConfig(
                                    layerState.TransitionFrom,
                                    layerState.TransitionTo,
                                    new float2(fromBlendPos.x, fromBlendPos.y),
                                    new float2(toBlendPos.x, toBlendPos.y),
                                    includeGhostBars: false);
                                section.Timeline.Duration = transConfig.Timing.TotalDuration;
                                section.Timeline.NormalizedTime = layerState.NormalizedTime;
                                break;
                                
                            case TransitionPlayState.LoopingTo:
                                float toDuration = AnimationStateUtils.GetEffectiveDuration(layerState.TransitionTo, toBlendPos);
                                section.Timeline.Duration = toDuration > 0 ? toDuration : 1f;
                                section.Timeline.NormalizedTime = layerState.NormalizedTime;
                                break;
                        }
                        break;
                }
                return;
            }
            
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
        
        #endregion
        
        #region Layer Refresh
        
        private void RefreshLayerSection(LayerSectionData section)
        {
            if (compositionState == null || section.LayerIndex >= compositionState.LayerCount)
                return;
            
            // Prevent recursive refresh (additional safety guard)
            if (_isRefreshingLayer) return;
            _isRefreshingLayer = true;
            try
            {
                RefreshLayerSectionCore(section);
            }
            finally
            {
                _isRefreshingLayer = false;
            }
        }
        
        private void RefreshLayerSectionCore(LayerSectionData section)
        {
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
            }

            // Show/hide navigation and clear buttons based on assignment status
            section.NavigateButton?.SetVisible(isAssigned);
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
                bool isBlendState = BlendSpaceUIBuilder.IsBlendState(selectedState);

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
                section.LoopModeField?.SetValueWithoutNotify(layerState.TransitionLoopMode);

                // Bind blend space elements for transition from/to states
                BindTransitionBlendElements(section, layerState.TransitionFrom, layerState.TransitionTo);
                
                // Update loop mode controls state
                UpdateTransitionControlsState(section, layerState);
            }
            else
            {
                // Unbind transition blend elements when not in transition mode
                UnbindTransitionBlendElements(section);
            }

            // Update timeline
            ConfigureLayerTimeline(section, layerState);
        }
        
        #endregion
        
        #region Utility Methods
        
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
        
        #endregion
        
        #region Timeline Event Handlers
        
        private void OnLayerTimeChanged(int layerIndex, float time)
        {
            // Skip if we're ticking - the tick method is driving the updates
            if (_isTickingLayers) return;
            
            var layer = compositionState?.GetLayer(layerIndex);
            if (layer == null) return;
            
            // Suppress events to prevent recursive cascade
            using (SuppressEvents())
            {
                if (layer.IsTransitionMode)
                {
                    // Timeline now represents full timeline position (NormalizedTime)
                    layer.NormalizedTime = time;
                    
                    var fromBlendVec = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
                    var toBlendVec = PreviewSettings.GetBlendPosition(layer.TransitionTo);
                    
                    // Use ghost bars only for TransitionLoop mode
                    bool includeGhosts = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
                    var config = CreateTransitionConfig(
                        layer.TransitionFrom,
                        layer.TransitionTo,
                        new float2(fromBlendVec.x, fromBlendVec.y),
                        new float2(toBlendVec.x, toBlendVec.y),
                        includeGhostBars: includeGhosts);
                    
                    // Calculate state at this timeline position
                    var snapshot = TransitionCalculator.CalculateState(in config, time);
                    
                    // Update progress slider
                    layer.TransitionProgress = snapshot.RawProgress;
                    var section = layerSections.Find(s => s.LayerIndex == layerIndex);
                    section?.TransitionProgressSlider?.SetValueWithoutNotify(snapshot.RawProgress);
                    
                    // Propagate to backend
                    preview?.SetLayerTransitionProgress(layerIndex, snapshot.BlendWeight);
                    preview?.SetLayerTransitionNormalizedTimes(
                        layerIndex, 
                        snapshot.FromStateNormalizedTime, 
                        snapshot.ToStateNormalizedTime);
                }
                else
                {
                    // Single-state mode: timeline represents normalized time
                    layer.NormalizedTime = time;
                    preview?.SetLayerNormalizedTime(layerIndex, time);
                }
            }
            
            OnTimeChanged?.Invoke(time);
        }
        
        private void OnLayerPlayStateChanged(int layerIndex, bool playing)
        {
            var layer = compositionState?.GetLayer(layerIndex);
            if (layer != null)
                layer.IsPlaying = playing;
        }
        
        #endregion
    }
}
