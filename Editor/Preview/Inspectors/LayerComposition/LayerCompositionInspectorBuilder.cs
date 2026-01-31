using System;
using System.Collections.Generic;
using ConvenientLogger;
using ConvenientLogger.Editor;
using DMotion;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using CLogger = ConvenientLogger.Logger;

namespace DMotion.Editor
{
    /// <summary>
    /// Self-contained builder for layer composition inspector UI in the preview window.
    /// Handles multi-layer state machine preview with global playback controls
    /// and per-layer state/transition controls.
    /// </summary>
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Constants

        // Weight slider range
        private const float MinWeight = 0f;
        private const float MaxWeight = 1f;

        // UI element widths
        private const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        private const float WeightSliderMinWidth = 60f;
        private const float BlendFieldMinWidth = 35f;
        private const float WeightContainerMinWidth = 140f;

        // Spacing and margins
        private const float BaseLayerNameMarginLeft = 10f;
        private const float HeaderPaddingVertical = 2f;
        private const float SelectionRowMarginBottom = 5f;
        private const float TimelineSectionMarginTop = 5f;

        #endregion
        
        #region State
        
        private StateMachineAsset currentStateMachine;
        private ObservableCompositionState compositionState;
        private ILayerCompositionPreview preview;
        
        // UI references
        private VisualElement layersContainer;
        private Slider globalTimeSlider;
        private Button playButton;
        private Toggle syncLayersToggle;
        private readonly List<LayerSectionData> layerSections = new();
        
        // Playback
        private bool isPlaying;
        private float playbackSpeed = 1f;
        private double lastTickTime; // For smooth time advancement
        
        // Reentrancy guards to prevent recursive event cascades during tick/scrub:
        // Timeline.NormalizedTime → OnTimeChanged → OnLayerTimeChanged → layer.Property → PropertyChanged → RefreshLayerSection → ConfigureLayerTimeline → Timeline.NormalizedTime
        //
        // NOTE: This is separate from LayerStateAsset.SuppressNotifications() which batches property changes.
        // - Builder suppression: Prevents callback loops, wants ZERO events during tick
        // - LayerStateAsset suppression: Batches changes, fires ONE consolidated event at end
        private bool _suppressLayerChangeEvents;
        private bool _isRefreshingLayer;
        private bool _isTickingLayers;
        
        /// <summary>
        /// Creates a scope that suppresses layer change events and timeline callbacks.
        /// Use with 'using' statement to automatically restore state on scope exit.
        /// </summary>
        private EventSuppressionScope SuppressEvents() => new(this);
        
        /// <summary>
        /// Disposable scope for suppressing event cascades during batch updates.
        /// </summary>
        private readonly struct EventSuppressionScope : IDisposable
        {
            private readonly LayerCompositionInspectorBuilder _builder;
            private readonly bool _previousSuppressLayerEvents;
            private readonly bool _previousIsTickingLayers;
            
            public EventSuppressionScope(LayerCompositionInspectorBuilder builder)
            {
                _builder = builder;
                _previousSuppressLayerEvents = builder._suppressLayerChangeEvents;
                _previousIsTickingLayers = builder._isTickingLayers;
                builder._suppressLayerChangeEvents = true;
                builder._isTickingLayers = true;
            }
            
            public void Dispose()
            {
                _builder._suppressLayerChangeEvents = _previousSuppressLayerEvents;
                _builder._isTickingLayers = _previousIsTickingLayers;
            }
        }
        
        // Logging - child of parent window's logger
        private CLogger _logger;
        
        /// <summary>
        /// Data for a single layer's UI section.
        /// </summary>
        private class LayerSectionData
        {
            public int LayerIndex;
            public LayerStateAsset LayerAsset;
            public Foldout Foldout;
            public VisualElement Content;
            public Toggle EnableToggle;
            public VisualElement WeightContainer;
            public Slider WeightSlider;
            public Label WeightLabel;
            public EnumField BlendModeField;
            public Label SelectionLabel;
            public IconButton NavigateButton; // Navigate to selection in graph (↗)
            public IconButton ClearButton; // Clear layer assignment (×)

            // Per-layer timeline
            public TimelineScrubber Timeline;
            public Action<float> TimelineTimeChangedHandler;
            public Action<bool> TimelinePlayStateChangedHandler;

            // State controls
            public VisualElement StateControls;
            public Slider BlendSlider;
            public FloatField BlendField;
            public Label BlendLabel;

            // Blend space visual element (reusable across state changes)
            public VisualElement BlendSpaceContainer;
            public BlendSpaceVisualElement BlendSpaceElement;
            public AnimationStateAsset BoundBlendState; // Track which state the element is bound to
            public Action<Vector2> CachedPreviewPositionHandler;

            // Transition controls
            public VisualElement TransitionControls;
            public Slider TransitionProgressSlider;
            public EnumField LoopModeField;
            public Button TriggerButton;
            public Label PlayStateLabel;

            // Transition "from" state blend controls
            public VisualElement FromBlendSpaceContainer;
            public BlendSpaceVisualElement FromBlendSpaceElement;
            public AnimationStateAsset BoundFromState;
            public Action<Vector2> CachedFromPreviewPositionHandler;
            public Slider FromBlendSlider;
            public Label FromBlendLabel;

            // Transition "to" state blend controls
            public VisualElement ToBlendSpaceContainer;
            public BlendSpaceVisualElement ToBlendSpaceElement;
            public AnimationStateAsset BoundToState;
            public Action<Vector2> CachedToPreviewPositionHandler;
            public Slider ToBlendSlider;
            public Label ToBlendLabel;
        }
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when time changes (global or per-layer).
        /// </summary>
        public event Action<float> OnTimeChanged;
        
        /// <summary>
        /// Fired when the builder needs a repaint.
        /// </summary>
        public event Action OnRepaintRequested;
        
        /// <summary>
        /// Fired when play state changes.
        /// </summary>
        public event Action<bool> OnPlayStateChanged;
        
        /// <summary>
        /// Fired when user requests navigation to a layer.
        /// Parameters: layerIndex, layerAsset
        /// </summary>
        public event Action<int, LayerStateAsset> OnNavigateToLayer;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether global playback is active.
        /// </summary>
        public bool IsPlaying => isPlaying;
        
        /// <summary>
        /// Playback speed multiplier.
        /// </summary>
        public float PlaybackSpeed
        {
            get => playbackSpeed;
            set => playbackSpeed = value;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Builds the inspector UI for a multi-layer state machine.
        /// </summary>
        /// <param name="stateMachine">The state machine asset</param>
        /// <param name="compositionState">Observable composition state</param>
        /// <param name="preview">Layer composition preview backend</param>
        /// <param name="parentLogger">Optional parent logger for hierarchical logging</param>
        public VisualElement Build(
            StateMachineAsset stateMachine,
            ObservableCompositionState compositionState,
            ILayerCompositionPreview preview,
            CLogger parentLogger = null)
        {
            Cleanup();
            
            this.currentStateMachine = stateMachine;
            this.compositionState = compositionState;
            this.preview = preview;
            
            // Setup child logger if parent provided
            if (parentLogger != null && _logger == null)
            {
                _logger = parentLogger.CreateChild("LayerComposition");
                // Child inherits parent's settings but can be independently controlled
                // EffectiveEnabled will automatically respect parent state
                _logger.Enabled = true;  // Default to enabled - will be blocked if parent disabled
                _logger.ConsoleOutput = parentLogger.ConsoleOutput;
                _logger.LevelMask = parentLogger.LevelMask;
                LogDebug("Child logger created");
            }
            
            LogInfo($"Building UI for {stateMachine?.name ?? "null"}, Layers={compositionState?.LayerCount ?? 0}");
            
            var container = new VisualElement();
            container.AddToClassList("layer-composition-inspector");
            
            // Load stylesheet
            var guids = AssetDatabase.FindAssets("t:StyleSheet LayerCompositionInspector");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("LayerCompositionInspector.uss"))
                {
                    var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (uss != null)
                        container.styleSheets.Add(uss);
                    break;
                }
            }
            
            // Header with logger icon
            var header = CreateSectionHeader("Layer Composition", stateMachine?.name ?? "");
            if (_logger != null)
            {
                header.AddLogger(_logger);
            }
            container.Add(header);
            
            // Validation
            if (stateMachine == null || !stateMachine.IsMultiLayer)
            {
                var message = new Label("Select a multi-layer state machine to preview layer composition.");
                message.AddToClassList("info-message");
                container.Add(message);
                return container;
            }
            
            if (compositionState == null || compositionState.RootStateMachine == null)
            {
                var message = new Label("Layer composition state not initialized.");
                message.AddToClassList("info-message");
                container.Add(message);
                return container;
            }
            
            // Global controls section
            var globalSection = CreateSection("Global Playback");
            BuildGlobalControls(globalSection);
            container.Add(globalSection);
            
            // Layers section
            var layersSection = CreateSection($"Layers ({compositionState.LayerCount})");
            layersContainer = new VisualElement();
            layersContainer.AddToClassList("layers-container");
            BuildLayerSections(layersContainer);
            layersSection.Add(layersContainer);
            container.Add(layersSection);
            
            // Subscribe to composition state changes
            SubscribeToCompositionState();
            
            return container;
        }
        
        /// <summary>
        /// Cleans up event subscriptions and resources.
        /// </summary>
        public void Cleanup()
        {
            // Unsubscribe from composition state
            UnsubscribeFromCompositionState();
            
            // Cleanup layer sections
            foreach (var section in layerSections)
            {
                UnbindBlendSpaceElement(section);
                UnbindTransitionBlendElements(section);

                // Unsubscribe from timeline events using stored delegate references
                if (section.Timeline != null)
                {
                    if (section.TimelineTimeChangedHandler != null)
                        section.Timeline.OnTimeChanged -= section.TimelineTimeChangedHandler;
                    if (section.TimelinePlayStateChangedHandler != null)
                        section.Timeline.OnPlayStateChanged -= section.TimelinePlayStateChangedHandler;
                }
            }
            layerSections.Clear();
            
            // Clear references
            currentStateMachine = null;
            compositionState = null;
            preview = null;
            layersContainer = null;
            globalTimeSlider = null;
            playButton = null;
            syncLayersToggle = null;
        }
        
        /// <summary>
        /// Ticks playback. Call from Update loop.
        /// Uses normalized time (0-1) for consistent playback across all clip durations.
        /// The timeline always completes one cycle in approximately 1 second at speed 1.0.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isPlaying || compositionState == null) return;

            // Suppress events during tick to prevent feedback loops
            using (SuppressEvents())
            {
                TickInternal(deltaTime);
            }
        }
        
        private void TickInternal(float deltaTime)
        {
            // Use EditorApplication.timeSinceStartup for smooth time tracking
            // This avoids frame rate jitter from variable deltaTime
            double currentTime = EditorApplication.timeSinceStartup;
            float dt;
            
            if (lastTickTime <= 0)
            {
                // First tick - use provided deltaTime
                dt = deltaTime * playbackSpeed;
            }
            else
            {
                // Calculate actual elapsed time
                dt = (float)(currentTime - lastTickTime) * playbackSpeed;
                // Clamp to prevent large jumps when window loses focus
                dt = Mathf.Min(dt, 0.1f);
            }
            lastTickTime = currentTime;

            if (compositionState.SyncLayers)
            {
                // Sync mode: MasterTime advances continuously like game time (unbounded).
                // Each layer calculates its own normalized time based on its timeline duration.
                // This simulates real game time behavior where layers loop independently.
                compositionState.MasterTime += dt;

                // Update all layer timelines and transition progress
                foreach (var section in layerSections)
                {
                    var layerState = compositionState.GetLayer(section.LayerIndex);
                    if (layerState == null) continue;
                    
                    // If layer is in transition mode, handle based on loop mode
                    if (layerState.IsTransitionMode)
                    {
                        TickTransitionLayer(section, layerState, dt);
                    }
                    else
                    {
                        // Single-state mode: calculate normalized time from master time
                        // Get actual clip duration from selected state (falls back to 1s if no state)
                        var blendPos = new Vector2(layerState.BlendPosition.x, layerState.BlendPosition.y);
                        float clipDuration = AnimationStateUtils.GetEffectiveDuration(layerState.SelectedState, blendPos);
                        float layerNormalizedTime = clipDuration > 0.001f 
                            ? (compositionState.MasterTime % clipDuration) / clipDuration 
                            : 0f;
                        
                        layerState.NormalizedTime = layerNormalizedTime;
                        
                        if (section.Timeline != null)
                        {
                            section.Timeline.NormalizedTime = layerNormalizedTime;
                        }
                        
                        // Propagate layer time to backend
                        preview?.SetLayerNormalizedTime(section.LayerIndex, layerNormalizedTime);
                    }
                }
                
                // Propagate global time to the preview backend (use first layer's normalized time for display)
                // This is for any global UI that needs a 0-1 value
                float displayNormalizedTime = layerSections.Count > 0 && layerSections[0].Timeline != null
                    ? layerSections[0].Timeline.NormalizedTime
                    : 0f;
                preview?.SetGlobalNormalizedTime(displayNormalizedTime);
                
                // Update the global time slider (clamp to slider range for display)
                globalTimeSlider?.SetValueWithoutNotify(Mathf.Clamp(compositionState.MasterTime, 0f, globalTimeSlider.highValue));
            }
            else
            {
                // Update each layer's timeline independently
                foreach (var section in layerSections)
                {
                    var layerState = compositionState.GetLayer(section.LayerIndex);
                    if (layerState == null) continue;
                    
                    if (layerState.IsTransitionMode)
                    {
                        TickTransitionLayer(section, layerState, dt);
                    }
                    else
                    {
                        // Single-state mode: advance normalized time
                        float newTime = (layerState.NormalizedTime + dt) % 1f;
                        layerState.NormalizedTime = newTime;
                        
                        // Update timeline UI
                        if (section.Timeline != null)
                        {
                            section.Timeline.NormalizedTime = newTime;
                        }

                        // Propagate layer time to backend
                        preview?.SetLayerNormalizedTime(section.LayerIndex, layerState.NormalizedTime);
                    }
                }
            }

            OnTimeChanged?.Invoke(compositionState.MasterTime);
            OnRepaintRequested?.Invoke();
        }
        
        /// <summary>
        /// Ticks a layer in transition mode, handling all loop modes.
        /// </summary>
        private void TickTransitionLayer(LayerSectionData section, LayerStateAsset layerState, float dt)
        {
            // Get blend positions for both states
            var fromBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
            
            // Events already suppressed by parent Tick() scope
            switch (layerState.TransitionLoopMode)
            {
                case TransitionLoopMode.TransitionLoop:
                    // TransitionLoop: include ghost bars for full context visualization
                    var loopConfig = CreateTransitionConfig(
                        layerState.TransitionFrom,
                        layerState.TransitionTo,
                        new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                        new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
                        includeGhostBars: true);
                    TickTransitionLoopMode(section, layerState, dt, loopConfig);
                    break;
                    
                case TransitionLoopMode.FromLoop:
                case TransitionLoopMode.ToLoop:
                    // Triggered modes: exclude ghost bars since looping states provide context
                    var triggeredConfig = CreateTransitionConfig(
                        layerState.TransitionFrom,
                        layerState.TransitionTo,
                        new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                        new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
                        includeGhostBars: false);
                    TickTriggeredTransitionMode(section, layerState, dt, triggeredConfig);
                    break;
            }
        }
        
        /// <summary>
        /// Handles TransitionLoop mode - continuously loops the full transition timeline.
        /// Timeline: [Ghost FROM][FROM bar][TRANSITION][TO bar][Ghost TO]
        /// Ghost bars provide before/after context within the looping timeline.
        /// </summary>
        private void TickTransitionLoopMode(
            LayerSectionData section, 
            LayerStateAsset layerState, 
            float dt,
            in TransitionStateConfig config)
        {
            float totalDuration = config.Timing.TotalDuration;
            
            // Advance normalized time over full timeline
            float timeDelta = totalDuration > 0.001f ? dt / totalDuration : dt;
            float newNormalizedTime = (layerState.NormalizedTime + timeDelta) % 1f;
            layerState.NormalizedTime = newNormalizedTime;
            
            // Calculate state at current timeline position
            var snapshot = TransitionCalculator.CalculateState(in config, newNormalizedTime);
            
            // Update UI
            layerState.TransitionProgress = snapshot.RawProgress;
            if (section.Timeline != null)
            {
                section.Timeline.Duration = totalDuration;
                section.Timeline.NormalizedTime = newNormalizedTime;
            }
            section.TransitionProgressSlider?.SetValueWithoutNotify(snapshot.RawProgress);
            
            // Propagate to backend
            preview?.SetLayerTransitionProgress(section.LayerIndex, snapshot.BlendWeight);
            preview?.SetLayerTransitionNormalizedTimes(
                section.LayerIndex, 
                snapshot.FromStateNormalizedTime, 
                snapshot.ToStateNormalizedTime);
        }
        
        /// <summary>
        /// Handles FromLoop and ToLoop modes - waits for trigger, plays transition timeline once.
        /// Timeline: [FROM bar][TRANSITION][TO bar] (no ghost bars - looping states provide context).
        /// </summary>
        private void TickTriggeredTransitionMode(
            LayerSectionData section, 
            LayerStateAsset layerState, 
            float dt,
            in TransitionStateConfig config)
        {
            switch (layerState.TransitionPlayState)
            {
                case TransitionPlayState.LoopingFrom:
                    TickLoopingFromState(section, layerState, dt, config.FromStateDuration);
                    break;
                    
                case TransitionPlayState.Transitioning:
                    TickTransitioningState(section, layerState, dt, in config);
                    break;
                    
                case TransitionPlayState.LoopingTo:
                    TickLoopingToState(section, layerState, dt, config.ToStateDuration);
                    break;
            }
        }
        
        /// <summary>
        /// Ticks the FROM state looping, waiting for trigger.
        /// When a pending trigger exists, executes it at loop boundary for smooth transitions.
        /// </summary>
        private void TickLoopingFromState(LayerSectionData section, LayerStateAsset layerState, float dt, float fromDuration)
        {
            // Advance FROM state normalized time
            float timeDelta = fromDuration > 0.001f ? dt / fromDuration : dt;
            float rawNewTime = layerState.NormalizedTime + timeDelta;
            
            // Check for loop wrap (crossed 1.0 boundary)
            if (rawNewTime >= 1f && layerState.TransitionPending)
            {
                // Loop completed with pending trigger - execute it now
                layerState.ExecutePendingTransition();
                UpdateTransitionControlsState(section, layerState);
                return; // State changed, next tick will handle Transitioning state
            }
            
            float newTime = rawNewTime % 1f;
            layerState.NormalizedTime = newTime;
            
            // Timeline shows FROM state progress
            if (section.Timeline != null)
            {
                section.Timeline.Duration = fromDuration;
                section.Timeline.NormalizedTime = newTime;
            }
            
            // Blend weight = 0 (full FROM state)
            preview?.SetLayerTransitionProgress(section.LayerIndex, 0f);
            preview?.SetLayerTransitionNormalizedTimes(section.LayerIndex, newTime, 0f);
        }
        
        /// <summary>
        /// Ticks the transition timeline (triggered mode).
        /// Plays: [FROM bar][TRANSITION][TO bar] (ghost bars excluded in triggered mode).
        /// </summary>
        private void TickTransitioningState(
            LayerSectionData section, 
            LayerStateAsset layerState, 
            float dt,
            in TransitionStateConfig config)
        {
            float totalDuration = config.Timing.TotalDuration;
            
            // Advance normalized time over full timeline
            float timeDelta = totalDuration > 0.001f ? dt / totalDuration : dt;
            float newNormalizedTime = layerState.NormalizedTime + timeDelta;
            
            // Check for completion of full timeline
            if (newNormalizedTime >= 1f)
            {
                newNormalizedTime = 1f;
                layerState.NormalizedTime = newNormalizedTime;
                layerState.TransitionProgress = 1f;
                layerState.OnTransitionComplete();
                UpdateTransitionControlsState(section, layerState);
                return;
            }
            
            layerState.NormalizedTime = newNormalizedTime;
            
            // Calculate state at current timeline position
            var snapshot = TransitionCalculator.CalculateState(in config, newNormalizedTime);
            
            // Update UI
            layerState.TransitionProgress = snapshot.RawProgress;
            if (section.Timeline != null)
            {
                section.Timeline.Duration = totalDuration;
                section.Timeline.NormalizedTime = newNormalizedTime;
            }
            section.TransitionProgressSlider?.SetValueWithoutNotify(snapshot.RawProgress);
            
            // Propagate to backend
            preview?.SetLayerTransitionProgress(section.LayerIndex, snapshot.BlendWeight);
            preview?.SetLayerTransitionNormalizedTimes(
                section.LayerIndex, 
                snapshot.FromStateNormalizedTime, 
                snapshot.ToStateNormalizedTime);
        }
        
        /// <summary>
        /// Ticks the TO state looping (ToLoop mode only).
        /// When a pending trigger exists (reset), executes it at loop boundary for smooth transitions.
        /// </summary>
        private void TickLoopingToState(LayerSectionData section, LayerStateAsset layerState, float dt, float toDuration)
        {
            // Advance TO state normalized time
            float timeDelta = toDuration > 0.001f ? dt / toDuration : dt;
            float rawNewTime = layerState.NormalizedTime + timeDelta;
            
            // Check for loop wrap (crossed 1.0 boundary)
            if (rawNewTime >= 1f && layerState.TransitionPending)
            {
                // Loop completed with pending trigger - execute it now (resets to LoopingFrom)
                layerState.ExecutePendingTransition();
                UpdateTransitionControlsState(section, layerState);
                return; // State changed, next tick will handle LoopingFrom state
            }
            
            float newTime = rawNewTime % 1f;
            layerState.NormalizedTime = newTime;
            
            // Timeline shows TO state progress
            if (section.Timeline != null)
            {
                section.Timeline.Duration = toDuration;
                section.Timeline.NormalizedTime = newTime;
            }
            
            // Blend weight = 1 (full TO state)
            preview?.SetLayerTransitionProgress(section.LayerIndex, 1f);
            preview?.SetLayerTransitionNormalizedTimes(section.LayerIndex, 0f, newTime);
        }
        
        /// <summary>
        /// Refreshes the UI to match composition state.
        /// </summary>
        public void Refresh()
        {
            if (compositionState == null) return;
            
            // Update global controls
            globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
            syncLayersToggle?.SetValueWithoutNotify(compositionState.SyncLayers);
            UpdatePlayButton();
            
            // Update layer sections
            foreach (var section in layerSections)
            {
                RefreshLayerSection(section);
            }
        }
        
        /// <summary>
        /// Updates the preview backend reference.
        /// Call this after the backend preview is created to ensure time/blend propagation works.
        /// </summary>
        public void SetPreviewBackend(ILayerCompositionPreview newPreview)
        {
            preview = newPreview;
        }
        
        #endregion
    }
}
