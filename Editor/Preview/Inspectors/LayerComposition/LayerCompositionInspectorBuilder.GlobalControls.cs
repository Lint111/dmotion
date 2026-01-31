using DMotion;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Private - Global Controls
        
        private void BuildGlobalControls(VisualElement section)
        {
            // Playback row
            var playbackRow = new VisualElement();
            playbackRow.AddToClassList("playback-row");
            playbackRow.style.flexDirection = FlexDirection.Row;
            playbackRow.style.alignItems = Align.Center;
            playbackRow.style.marginBottom = SelectionRowMarginBottom;
            
            playButton = new Button(OnPlayButtonClicked) { text = "▶ Play" };
            playButton.AddToClassList("play-button");
            playButton.style.minWidth = 70;
            playbackRow.Add(playButton);
            
            var resetButton = new Button(OnResetButtonClicked) { text = "⟲ Reset" };
            resetButton.AddToClassList("reset-button");
            resetButton.style.minWidth = WeightSliderMinWidth;
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
            
            // Global time row - unbounded game time in seconds (0-10s range for scrubbing)
            var timeRow = CreateSliderRow(
                "Master Time (s)",
                0f, 10f,
                compositionState?.MasterTime ?? 0f,
                OnGlobalTimeChanged,
                out globalTimeSlider,
                out _);
            section.Add(timeRow);
        }
        
        private void OnPlayButtonClicked()
        {
            isPlaying = !isPlaying;
            LogInfo($"Playback {(isPlaying ? "started" : "paused")}");
            
            // Reset time tracking when starting playback
            if (isPlaying)
                lastTickTime = UnityEditor.EditorApplication.timeSinceStartup;
            
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
            if (compositionState == null) return;
            
            compositionState.MasterTime = time;
            
            // When scrubbing, update all layers based on the new master time
            foreach (var section in layerSections)
            {
                var layerState = compositionState.GetLayer(section.LayerIndex);
                if (layerState == null) continue;
                
                if (layerState.IsTransitionMode)
                {
                    // Get blend positions from PreviewSettings for each state
                    var fromBlend = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
                    var toBlend = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
                    
                    // Use ghost bars only for TransitionLoop mode
                    bool includeGhosts = layerState.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
                    var config = CreateTransitionConfig(
                        layerState.TransitionFrom,
                        layerState.TransitionTo,
                        new Unity.Mathematics.float2(fromBlend.x, fromBlend.y),
                        new Unity.Mathematics.float2(toBlend.x, toBlend.y),
                        includeGhostBars: includeGhosts);
                    
                    // Calculate layer's normalized time from master time
                    float layerTotalDuration = config.Timing.TotalDuration;
                    float layerTime = layerTotalDuration > 0.001f 
                        ? (time % layerTotalDuration) 
                        : 0f;
                    float layerNormalizedTime = layerTotalDuration > 0.001f 
                        ? layerTime / layerTotalDuration 
                        : 0f;
                    
                    var snapshot = TransitionCalculator.CalculateState(in config, layerNormalizedTime);
                    
                    // Update UI - timeline shows transition progress (0=start, 1=complete)
                    layerState.TransitionProgress = snapshot.RawProgress;
                    if (section.Timeline != null)
                    {
                        section.Timeline.NormalizedTime = snapshot.RawProgress;
                    }
                    section.TransitionProgressSlider?.SetValueWithoutNotify(snapshot.RawProgress);
                    
                    preview?.SetLayerTransitionProgress(section.LayerIndex, snapshot.BlendWeight);
                    
                    // Calculate looping normalized times for continuous animation
                    float fromDuration = config.FromStateDuration;
                    float toDuration = config.ToStateDuration;
                    float fromNormalized = fromDuration > 0.001f 
                        ? (time % fromDuration) / fromDuration 
                        : 0f;
                    float toNormalized = toDuration > 0.001f 
                        ? (time % toDuration) / toDuration 
                        : 0f;
                    
                    preview?.SetLayerTransitionNormalizedTimes(
                        section.LayerIndex,
                        fromNormalized,
                        toNormalized);
                }
                else
                {
                    // Single-state mode: get actual clip duration from selected state
                    var blendPos = new Vector2(layerState.BlendPosition.x, layerState.BlendPosition.y);
                    float clipDuration = AnimationStateUtils.GetEffectiveDuration(layerState.SelectedState, blendPos);
                    float layerNormalizedTime = clipDuration > 0.001f 
                        ? (time % clipDuration) / clipDuration 
                        : 0f;
                    
                    layerState.NormalizedTime = layerNormalizedTime;
                    
                    if (section.Timeline != null)
                    {
                        section.Timeline.NormalizedTime = layerNormalizedTime;
                    }
                    
                    preview?.SetLayerNormalizedTime(section.LayerIndex, layerNormalizedTime);
                }
            }
            
            // Propagate to backend (use first layer's normalized time for global display)
            float displayNormalizedTime = layerSections.Count > 0 && layerSections[0].Timeline != null
                ? layerSections[0].Timeline.NormalizedTime
                : 0f;
            preview?.SetGlobalNormalizedTime(displayNormalizedTime);

            OnTimeChanged?.Invoke(time);
        }
        
        private void UpdatePlayButton()
        {
            if (playButton != null)
                playButton.text = isPlaying ? "⏸ Pause" : "▶ Play";
        }
        
        #endregion
    }
}
