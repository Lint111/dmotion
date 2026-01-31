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
    internal class LayerCompositionInspectorBuilder
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
            double currentTime = UnityEditor.EditorApplication.timeSinceStartup;
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
        
        #region Private - UI Factories
        
        private VisualElement CreateSectionHeader(string type, string name)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");

            header.Add(typeLabel);
            header.Add(nameLabel);

            return header;
        }
        
        private Foldout CreateSection(string title)
        {
            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("section-foldout");
            return foldout;
        }
        
        private VisualElement CreatePropertyRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");

            var valueElement = new Label(value);
            valueElement.AddToClassList("property-value");

            row.Add(labelElement);
            row.Add(valueElement);

            return row;
        }
        
        private VisualElement CreateSliderRow(
            string label, 
            float min, 
            float max, 
            float value,
            Action<float> onChanged,
            out Slider outSlider,
            out Label outValueLabel)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.minWidth = WeightSliderMinWidth;
            row.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.value = value;
            
            var valueLabel = new Label(value.ToString("F2"));
            valueLabel.AddToClassList("value-label");
            valueLabel.style.minWidth = BlendFieldMinWidth;
            
            slider.RegisterValueChangedCallback(evt =>
            {
                valueLabel.text = evt.newValue.ToString("F2");
                onChanged?.Invoke(evt.newValue);
            });
            
            valueContainer.Add(slider);
            valueContainer.Add(valueLabel);
            row.Add(valueContainer);
            
            outSlider = slider;
            outValueLabel = valueLabel;
            
            return row;
        }
        
        #endregion
        
        #region Private - Transition Lookup
        
        /// <summary>
        /// Finds the transition asset between two states, if one exists.
        /// Searches the FROM state's OutTransitions for a transition to the TO state.
        /// </summary>
        /// <remarks>
        /// Note: When multiple transitions exist between the same states (with different conditions),
        /// this returns the first match. The graph view intentionally groups all transitions between
        /// the same states into a single edge, so we don't have access to the specific transition
        /// the user clicked. This is acceptable for preview timing purposes.
        /// TODO: If needed, add a dropdown in the preview UI to select which transition to preview
        /// when multiple exist between the same states.
        /// </remarks>
        /// <param name="fromState">The source state</param>
        /// <param name="toState">The target state</param>
        /// <returns>The first StateOutTransition matching the target, or null if none found</returns>
        private static StateOutTransition FindTransition(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            if (fromState == null || toState == null) return null;
            
            // Search the FROM state's outgoing transitions for one targeting TO state
            // Returns the first match when multiple transitions exist with different conditions
            foreach (var transition in fromState.OutTransitions)
            {
                if (transition.ToState == toState)
                    return transition;
            }
            
            return null;
        }
        
        /// <summary>
        /// Creates a TransitionStateConfig using real transition data if available,
        /// falling back to defaults if no transition asset exists.
        /// </summary>
        /// <param name="fromState">Source state</param>
        /// <param name="toState">Target state</param>
        /// <param name="fromBlendPos">Blend position for FROM state</param>
        /// <param name="toBlendPos">Blend position for TO state</param>
        /// <param name="includeGhostBars">
        /// If true, include ghost bars for context. If false, exclude them
        /// (useful for triggered modes where looping states provide context).
        /// </param>
        private static TransitionStateConfig CreateTransitionConfig(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            Unity.Mathematics.float2 fromBlendPos,
            Unity.Mathematics.float2 toBlendPos,
            bool includeGhostBars = true)
        {
            var transition = FindTransition(fromState, toState);
            var config = TransitionStateCalculator.CreateConfig(
                fromState,
                toState,
                transition,
                fromBlendPos,
                toBlendPos);
            
            // For triggered modes, strip ghost bars since looping states provide context
            if (!includeGhostBars)
            {
                config.Timing.GhostFromDuration = 0f;
                config.Timing.GhostToDuration = 0f;
            }
            
            return config;
        }
        
        #endregion
        
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
        
        #region Private - Layer Sections
        
        private void BuildLayerSections(VisualElement container)
        {
            layerSections.Clear();
            
            if (compositionState?.Layers == null) return;
            
            for (int i = 0; i < compositionState.LayerCount; i++)
            {
                var layerState = compositionState.Layers[i];
                var section = CreateLayerSection(layerState, i);
                container.Add(section.Foldout);
                layerSections.Add(section);
            }
        }
        
        private LayerSectionData CreateLayerSection(LayerStateAsset layerState, int layerIndex)
        {
            var section = new LayerSectionData
            {
                LayerIndex = layerIndex,
                LayerAsset = layerState
            };

            // Use a wrapper element instead of Foldout to have full control over collapse behavior
            // This avoids issues with Foldout's internal toggle manipulation
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList("layer-section");
            section.Foldout.value = true;

            // Keep the foldout's text empty since we're using custom header
            section.Foldout.text = "";

            // Create custom header as part of the Foldout's label area
            var header = CreateLayerHeader(section, layerState);

            // Instead of manipulating internal toggle, put header in the Foldout's toggle area properly
            var toggle = section.Foldout.Q<Toggle>();
            if (toggle != null)
            {
                // Add our header content to the toggle's visual tree directly
                // This keeps the header visible when foldout collapses
                var checkmark = toggle.Q<VisualElement>(className: "unity-foldout__checkmark");
                if (checkmark != null)
                {
                    // Insert header after checkmark, replacing the default label
                    var toggleContainer = checkmark.parent;
                    var defaultLabel = toggle.Q<Label>(className: "unity-foldout__text");
                    if (defaultLabel != null)
                        defaultLabel.style.display = DisplayStyle.None;

                    toggleContainer.Add(header);
                    header.style.flexGrow = 1;
                }
                else
                {
                    // Fallback: add header directly to toggle
                    toggle.Add(header);
                    header.style.flexGrow = 1;
                }
            }

            // Content area - this goes into the Foldout's collapsible section
            section.Content = new VisualElement();
            section.Content.AddToClassList("layer-content");
            section.Content.style.paddingLeft = 15;

            BuildLayerContent(section, layerState);

            section.Foldout.Add(section.Content);

            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSectionData section, LayerStateAsset layerState)
        {
            var header = new VisualElement();
            header.AddToClassList("layer-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = HeaderPaddingVertical;
            header.style.paddingBottom = HeaderPaddingVertical;

            // No manual arrow needed - Foldout's native checkmark handles collapse indicator

            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.value = layerState.IsEnabled;
            section.EnableToggle.style.marginRight = 5;
            section.EnableToggle.style.marginLeft = 5;
            section.EnableToggle.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null) layer.IsEnabled = evt.newValue;
            });
            // Stop click propagation to prevent foldout toggle when clicking enable checkbox
            section.EnableToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            header.Add(section.EnableToggle);

            // Layer name container
            var nameContainer = new VisualElement();
            nameContainer.style.flexDirection = FlexDirection.Column;
            nameContainer.style.flexGrow = 1;

            // Base layer: shift name right since we don't have weight slider
            if (layerState.IsBaseLayer)
            {
                nameContainer.style.marginLeft = BaseLayerNameMarginLeft;
            }

            var nameLabel = new Label($"Layer {layerState.LayerIndex}: {layerState.name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);

            // Blend mode control
            // Base layer: Read-only label (always Override)
            // Other layers: Editable dropdown
            if (layerState.IsBaseLayer)
            {
                var blendModeLabel = new Label("Override");
                blendModeLabel.AddToClassList("layer-blend-mode");
                blendModeLabel.style.fontSize = 10;
                nameContainer.Add(blendModeLabel);
            }
            else
            {
                section.BlendModeField = new EnumField(layerState.BlendMode);
                section.BlendModeField.AddToClassList("layer-blend-mode-field");
                section.BlendModeField.style.fontSize = 10;
                section.BlendModeField.style.maxWidth = 80;
                section.BlendModeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is LayerBlendMode blendMode)
                    {
                        var layer = compositionState?.GetLayer(section.LayerIndex);
                        if (layer != null)
                        {
                            layer.BlendMode = blendMode;
                            // Refresh to update weight slider visibility
                            RefreshLayerSection(section);
                        }
                    }
                    evt.StopPropagation();
                });
                // Stop click propagation to prevent foldout toggle
                section.BlendModeField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                section.BlendModeField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                nameContainer.Add(section.BlendModeField);
            }

            header.Add(nameContainer);

            // Weight slider - only for non-Layer 0 and Additive blend mode
            // Base layer weight locked to 1.0
            // Override blend mode always has effective weight 1.0
            bool shouldShowWeight = ShouldShowWeightSlider(layerState, layerState.BlendMode);

            section.WeightContainer = new VisualElement();
            section.WeightContainer.style.flexDirection = FlexDirection.Row;
            section.WeightContainer.style.alignItems = Align.Center;
            section.WeightContainer.style.minWidth = WeightContainerMinWidth;
            section.WeightContainer.style.display = shouldShowWeight ? DisplayStyle.Flex : DisplayStyle.None;

            var weightLabel = new Label("Weight:");
            weightLabel.style.minWidth = 45;
            section.WeightContainer.Add(weightLabel);

            section.WeightSlider = new Slider(MinWeight, MaxWeight);
            section.WeightSlider.style.flexGrow = 1;
            section.WeightSlider.value = layerState.Weight;

            section.WeightSlider.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                // Base layer weight is locked (safety check)
                if (layer != null && layer.CanModifyWeight)
                    layer.Weight = evt.newValue;
                if (section.WeightLabel != null)
                    section.WeightLabel.text = evt.newValue.ToString("F2");
            });
            // Stop click propagation to prevent foldout toggle when interacting with slider
            section.WeightSlider.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            section.WeightSlider.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            section.WeightContainer.Add(section.WeightSlider);

            section.WeightLabel = new Label(layerState.Weight.ToString("F2"));
            section.WeightLabel.style.minWidth = BlendFieldMinWidth;
            section.WeightContainer.Add(section.WeightLabel);

            header.Add(section.WeightContainer);

            // Navigate button (↗) - jump to the selected state/transition in the graph
            section.NavigateButton = IconButton.CreatePingButton(
                    "Navigate to selection in graph",
                    () => OnNavigateToLayer?.Invoke(section.LayerIndex, section.LayerAsset))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.NavigateButton.style.marginLeft = 5;
            header.Add(section.NavigateButton);
            
            // Clear assignment button (X) - explicit action to unassign layer
            section.ClearButton = IconButton.CreateClearButton(
                    "Clear layer assignment",
                    () => compositionState?.ClearLayerSelection(section.LayerIndex))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.ClearButton.style.marginLeft = 2;
            header.Add(section.ClearButton);

            return header;
        }

        /// <summary>
        /// Determines whether the weight slider should be shown for a layer.
        /// Base layer: Never show (weight locked to 1.0).
        /// Override blend mode: Never show (weight is effectively 1.0).
        /// Additive blend mode: Show (weight is variable).
        /// </summary>
        private static bool ShouldShowWeightSlider(LayerStateAsset layer, LayerBlendMode blendMode)
        {
            // Base layer weight cannot be modified
            if (!layer.CanModifyWeight) return false;

            // Override mode: weight is effectively 1.0
            if (blendMode == LayerBlendMode.Override) return false;

            // Additive mode: weight is variable
            return true;
        }
        
        private void BuildLayerContent(LayerSectionData section, LayerStateAsset layerState)
        {
            // Current selection
            var selectionRow = new VisualElement();
            selectionRow.style.flexDirection = FlexDirection.Row;
            selectionRow.style.alignItems = Align.Center;
            selectionRow.style.marginBottom = SelectionRowMarginBottom;

            section.SelectionLabel = new Label(GetSelectionText(layerState));
            section.SelectionLabel.AddToClassList("selection-label");
            section.SelectionLabel.style.flexGrow = 1;
            section.SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            selectionRow.Add(section.SelectionLabel);

            // Note: Navigate button moved to header row (next to clear button)

            section.Content.Add(selectionRow);
            
            // State controls (shown when a state is selected)
            section.StateControls = new VisualElement();
            section.StateControls.AddToClassList("state-controls");
            BuildLayerStateControls(section, layerState);
            section.Content.Add(section.StateControls);
            
            // Transition controls (shown when a transition is selected)
            section.TransitionControls = new VisualElement();
            section.TransitionControls.AddToClassList("transition-controls");
            BuildLayerTransitionControls(section, layerState);
            section.Content.Add(section.TransitionControls);
            
            // Per-layer timeline
            var timelineSection = new VisualElement();
            timelineSection.style.marginTop = TimelineSectionMarginTop;
            
            section.Timeline = new TimelineScrubber();
            section.Timeline.IsLooping = true;
            // Hide per-layer play button - playback is controlled globally via Global Playback section
            section.Timeline.ShowPlayButton = false;
            
            // Configure timeline based on selected state
            ConfigureLayerTimeline(section, layerState);
            
            // Subscribe to timeline events
            // Store delegate references for proper unsubscription in Cleanup()
            int layerIdx = section.LayerIndex;
            section.TimelineTimeChangedHandler = time => OnLayerTimeChanged(layerIdx, time);
            section.TimelinePlayStateChangedHandler = playing => OnLayerPlayStateChanged(layerIdx, playing);
            section.Timeline.OnTimeChanged += section.TimelineTimeChangedHandler;
            section.Timeline.OnPlayStateChanged += section.TimelinePlayStateChangedHandler;
            
            timelineSection.Add(section.Timeline);
            section.Content.Add(timelineSection);
            
            // Update visibility based on current mode
            RefreshLayerSection(section);
        }
        
        private void BuildLayerStateControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Blend space visual element container - populated dynamically based on selected state
            section.BlendSpaceContainer = new VisualElement();
            section.BlendSpaceContainer.name = "blend-space-container";
            section.StateControls.Add(section.BlendSpaceContainer);

            // Blend slider row with float field - range is updated dynamically
            var blendRow = new VisualElement();
            blendRow.name = "blend-row";
            blendRow.AddToClassList("property-row");

            var blendLabel = new Label("Blend");
            blendLabel.AddToClassList("property-label");
            blendLabel.style.minWidth = 60;
            blendRow.Add(blendLabel);

            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;

            section.BlendSlider = new Slider(0f, 1f);
            section.BlendSlider.AddToClassList("property-slider");
            section.BlendSlider.style.flexGrow = 1;
            section.BlendSlider.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendField?.SetValueWithoutNotify(evt.newValue);
            });

            section.BlendField = new FloatField();
            section.BlendField.AddToClassList("property-float-field");
            section.BlendField.style.minWidth = FloatFieldWidth;
            section.BlendField.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendSlider?.SetValueWithoutNotify(evt.newValue);
            });

            valueContainer.Add(section.BlendSlider);
            valueContainer.Add(section.BlendField);
            blendRow.Add(valueContainer);

            section.StateControls.Add(blendRow);
        }

        /// <summary>
        /// Creates or updates the blend space visual element for a layer based on its selected state.
        /// </summary>
        private void BindBlendSpaceElement(LayerSectionData section, AnimationStateAsset selectedState)
        {
            // Skip if already bound to this state AND element still exists in hierarchy
            if (section.BoundBlendState == selectedState &&
                section.BlendSpaceElement != null &&
                section.BlendSpaceElement.parent != null)
                return;

            // Cleanup previous element (also clears container defensively)
            UnbindBlendSpaceElement(section);

            section.BoundBlendState = selectedState;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(selectedState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persistedValue = result.InitialPosition;

            // Update slider range
            if (section.BlendSlider != null)
            {
                section.BlendSlider.lowValue = result.Range.MinX;
                section.BlendSlider.highValue = result.Range.MaxX;
            }
            section.BlendSlider?.SetValueWithoutNotify(persistedValue.x);
            section.BlendField?.SetValueWithoutNotify(persistedValue.x);

            // Sync to observable
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer != null)
                layer.BlendPosition = new Unity.Mathematics.float2(persistedValue.x, persistedValue.y);

            // Handle preview position changes from the visual element
            section.CachedPreviewPositionHandler = result.Is2D
                ? pos =>
                {
                    SetLayerBlendValue2D(section, pos);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                }
                : pos =>
                {
                    SetLayerBlendValue(section, pos.x);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                };
            element.OnPreviewPositionChanged += section.CachedPreviewPositionHandler;

            // Defensive: ensure container is clear before adding
            section.BlendSpaceContainer.Clear();
            section.BlendSpaceElement = element;
            section.BlendSpaceContainer.Add(element);
        }

        /// <summary>
        /// Removes and cleans up the current blend space visual element for a layer.
        /// </summary>
        private void UnbindBlendSpaceElement(LayerSectionData section)
        {
            if (section.BlendSpaceElement != null && section.CachedPreviewPositionHandler != null)
            {
                section.BlendSpaceElement.OnPreviewPositionChanged -= section.CachedPreviewPositionHandler;
                section.CachedPreviewPositionHandler = null;
            }

            section.BlendSpaceContainer?.Clear();
            section.BlendSpaceElement = null;
            section.BoundBlendState = null;
        }

        /// <summary>
        /// Binds blend space elements for transition from/to states.
        /// </summary>
        private void BindTransitionBlendElements(LayerSectionData section, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            // Bind "from" state blend element
            BindTransitionFromBlendElement(section, fromState);

            // Bind "to" state blend element
            BindTransitionToBlendElement(section, toState);
        }

        private void BindTransitionFromBlendElement(LayerSectionData section, AnimationStateAsset fromState)
        {
            // Skip if already bound to this state
            if (section.BoundFromState == fromState && section.FromBlendSpaceElement?.parent != null)
                return;

            UnbindTransitionFromBlendElement(section);
            section.BoundFromState = fromState;

            var fromBlendRow = section.TransitionControls?.Q<VisualElement>("from-blend-row");
            bool isBlendState = BlendSpaceUIBuilder.IsBlendState(fromState);

            if (fromBlendRow != null)
                fromBlendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;
            if (section.FromBlendSpaceContainer != null)
                section.FromBlendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(fromState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persisted = result.InitialPosition;

            // Wire up position change handler - propagate to backend and update UI
            section.CachedFromPreviewPositionHandler = pos =>
            {
                // Update slider UI
                section.FromBlendSlider?.SetValueWithoutNotify(pos.x);
                if (section.FromBlendLabel != null)
                    section.FromBlendLabel.text = pos.x.ToString("F2");
                
                // Propagate to backend (persists to PreviewSettings and updates playable graph)
                SetTransitionFromBlendValue(section, pos.x);
            };
            element.OnPreviewPositionChanged += section.CachedFromPreviewPositionHandler;

            section.FromBlendSpaceContainer?.Clear();
            section.FromBlendSpaceElement = element;
            section.FromBlendSpaceContainer?.Add(element);

            // Update slider range
            if (section.FromBlendSlider != null)
            {
                section.FromBlendSlider.lowValue = result.Range.MinX;
                section.FromBlendSlider.highValue = result.Range.MaxX;
            }
            section.FromBlendSlider?.SetValueWithoutNotify(persisted.x);
            if (section.FromBlendLabel != null)
                section.FromBlendLabel.text = persisted.x.ToString("F2");
        }

        private void BindTransitionToBlendElement(LayerSectionData section, AnimationStateAsset toState)
        {
            // Skip if already bound to this state
            if (section.BoundToState == toState && section.ToBlendSpaceElement?.parent != null)
                return;

            UnbindTransitionToBlendElement(section);
            section.BoundToState = toState;

            var toBlendRow = section.TransitionControls?.Q<VisualElement>("to-blend-row");
            bool isBlendState = BlendSpaceUIBuilder.IsBlendState(toState);

            if (toBlendRow != null)
                toBlendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;
            if (section.ToBlendSpaceContainer != null)
                section.ToBlendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(toState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persisted = result.InitialPosition;

            // Wire up position change handler - propagate to backend and update UI
            section.CachedToPreviewPositionHandler = pos =>
            {
                // Update slider UI
                section.ToBlendSlider?.SetValueWithoutNotify(pos.x);
                if (section.ToBlendLabel != null)
                    section.ToBlendLabel.text = pos.x.ToString("F2");
                
                // Propagate to backend (persists to PreviewSettings and updates playable graph)
                SetTransitionToBlendValue(section, pos.x);
            };
            element.OnPreviewPositionChanged += section.CachedToPreviewPositionHandler;

            section.ToBlendSpaceContainer?.Clear();
            section.ToBlendSpaceElement = element;
            section.ToBlendSpaceContainer?.Add(element);

            // Update slider range
            if (section.ToBlendSlider != null)
            {
                section.ToBlendSlider.lowValue = result.Range.MinX;
                section.ToBlendSlider.highValue = result.Range.MaxX;
            }
            section.ToBlendSlider?.SetValueWithoutNotify(persisted.x);
            if (section.ToBlendLabel != null)
                section.ToBlendLabel.text = persisted.x.ToString("F2");
        }

        /// <summary>
        /// Unbinds all transition blend elements.
        /// </summary>
        private void UnbindTransitionBlendElements(LayerSectionData section)
        {
            UnbindTransitionFromBlendElement(section);
            UnbindTransitionToBlendElement(section);
        }

        private void UnbindTransitionFromBlendElement(LayerSectionData section)
        {
            if (section.FromBlendSpaceElement != null && section.CachedFromPreviewPositionHandler != null)
            {
                section.FromBlendSpaceElement.OnPreviewPositionChanged -= section.CachedFromPreviewPositionHandler;
                section.CachedFromPreviewPositionHandler = null;
            }

            section.FromBlendSpaceContainer?.Clear();
            section.FromBlendSpaceElement = null;
            section.BoundFromState = null;
        }

        private void UnbindTransitionToBlendElement(LayerSectionData section)
        {
            if (section.ToBlendSpaceElement != null && section.CachedToPreviewPositionHandler != null)
            {
                section.ToBlendSpaceElement.OnPreviewPositionChanged -= section.CachedToPreviewPositionHandler;
                section.CachedToPreviewPositionHandler = null;
            }

            section.ToBlendSpaceContainer?.Clear();
            section.ToBlendSpaceElement = null;
            section.BoundToState = null;
        }

        private void SetLayerBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new Unity.Mathematics.float2(value, layer.BlendPosition.y);

            // Update visual element
            if (section.BlendSpaceElement != null)
                section.BlendSpaceElement.PreviewPosition = new Vector2(value, section.BlendSpaceElement.PreviewPosition.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Persist via PreviewSettings
            var selectedState = layer.SelectedState;
            if (selectedState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(selectedState, value);
            else if (selectedState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(selectedState, new Vector2(value, layer.BlendPosition.y));
            
            // Update timeline duration (changes with blend position for blend states)
            ConfigureLayerTimeline(section, layer);
        }

        private void SetLayerBlendValue2D(LayerSectionData section, Vector2 value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new Unity.Mathematics.float2(value.x, value.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Visual element already updated by the caller
            // Persist via PreviewSettings
            var selectedState = layer.SelectedState;
            if (selectedState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(selectedState, value);
            
            // Update timeline duration (changes with blend position for blend states)
            ConfigureLayerTimeline(section, layer);
        }

        private void BuildLayerTransitionControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Unified controls row with mode dropdown and action button
            var controlsRow = new VisualElement();
            controlsRow.name = "transition-controls-row";
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.alignItems = Align.Center;
            controlsRow.style.marginBottom = 5;
            
            // Loop mode dropdown - compact, no label (button provides context)
            section.LoopModeField = new EnumField(layerState.TransitionLoopMode);
            section.LoopModeField.style.minWidth = 120;
            section.LoopModeField.style.maxWidth = 140;
            section.LoopModeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is TransitionLoopMode mode)
                {
                    var layer = compositionState?.GetLayer(section.LayerIndex);
                    if (layer != null)
                    {
                        layer.TransitionLoopMode = mode;
                        layer.ResetTransition();
                        UpdateTransitionControlsState(section, layer);
                    }
                }
            });
            controlsRow.Add(section.LoopModeField);
            
            // Unified action button - shows current state AND available action
            // e.g., "▶ FROM | Trigger →" or "⟳ Blending..." or "▶ TO | ↺ Reset"
            var capturedSection = section;
            section.TriggerButton = new Button(() => OnTriggerButtonClicked(capturedSection));
            section.TriggerButton.text = GetUnifiedButtonText(layerState);
            section.TriggerButton.style.marginLeft = 8;
            section.TriggerButton.style.minWidth = 140;
            section.TriggerButton.style.flexGrow = 1;
            controlsRow.Add(section.TriggerButton);
            
            // PlayStateLabel no longer used - state shown in button
            section.PlayStateLabel = null;
            
            section.TransitionControls.Add(controlsRow);
            
            // "From" state section
            var fromLabel = new Label("From State");
            fromLabel.AddToClassList("transition-state-label");
            fromLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            fromLabel.style.marginTop = 5;
            section.TransitionControls.Add(fromLabel);

            // From blend space container
            section.FromBlendSpaceContainer = new VisualElement();
            section.FromBlendSpaceContainer.name = "from-blend-space-container";
            section.TransitionControls.Add(section.FromBlendSpaceContainer);

            // From blend slider row
            var fromBlendRow = CreateSliderRow(
                "Blend",
                0f, 1f,
                0f,
                value => SetTransitionFromBlendValue(section, value),
                out section.FromBlendSlider,
                out section.FromBlendLabel);
            fromBlendRow.name = "from-blend-row";
            section.TransitionControls.Add(fromBlendRow);

            // Transition progress slider (only visible in TransitionLoop mode or during transition)
            var progressRow = CreateSliderRow(
                "Progress",
                0f, 1f,
                layerState.TransitionProgress,
                value => OnTransitionProgressChanged(capturedSection, value),
                out section.TransitionProgressSlider,
                out _);
            progressRow.name = "progress-row";
            progressRow.style.marginTop = 10;
            section.TransitionControls.Add(progressRow);

            // "To" state section
            var toLabel = new Label("To State");
            toLabel.AddToClassList("transition-state-label");
            toLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toLabel.style.marginTop = 10;
            section.TransitionControls.Add(toLabel);

            // To blend space container
            section.ToBlendSpaceContainer = new VisualElement();
            section.ToBlendSpaceContainer.name = "to-blend-space-container";
            section.TransitionControls.Add(section.ToBlendSpaceContainer);

            // To blend slider row
            var toBlendRow = CreateSliderRow(
                "Blend",
                0f, 1f,
                0f,
                value => SetTransitionToBlendValue(section, value),
                out section.ToBlendSlider,
                out section.ToBlendLabel);
            toBlendRow.name = "to-blend-row";
            section.TransitionControls.Add(toBlendRow);
            
            // Initial state update
            UpdateTransitionControlsState(section, layerState);
        }
        
        private void OnTriggerButtonClicked(LayerSectionData section)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null || !layer.IsTransitionMode) return;
            
            layer.TriggerTransition();
            UpdateTransitionControlsState(section, layer);
        }
        
        private void UpdateTransitionControlsState(LayerSectionData section, LayerStateAsset layer)
        {
            if (layer == null) return;
            
            bool isTransitionLoop = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
            
            // Update unified button - hide entirely in TransitionLoop mode (no action needed)
            if (section.TriggerButton != null)
            {
                section.TriggerButton.text = GetUnifiedButtonText(layer);
                section.TriggerButton.style.display = isTransitionLoop ? DisplayStyle.None : DisplayStyle.Flex;
            }
            
            // Progress slider: always show in TransitionLoop, show during transition in other modes
            var progressRow = section.TransitionControls?.Q<VisualElement>("progress-row");
            bool showProgress = isTransitionLoop || layer.TransitionPlayState == TransitionPlayState.Transitioning;
            if (progressRow != null)
                progressRow.style.display = showProgress ? DisplayStyle.Flex : DisplayStyle.None;
        }
        
        /// <summary>
        /// Returns unified button text showing current state AND available action.
        /// Format: "State | Action" or just "State" when no action available.
        /// </summary>
        private static string GetUnifiedButtonText(LayerStateAsset layer)
        {
            // TransitionLoop mode: just show current state (button disabled, no action)
            if (layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop)
            {
                return layer.TransitionPlayState switch
                {
                    TransitionPlayState.LoopingFrom => "▶ Looping FROM",
                    TransitionPlayState.Transitioning => "⟳ Blending...",
                    TransitionPlayState.LoopingTo => "▶ Looping TO",
                    _ => "⟳ Looping"
                };
            }
            
            // FromLoop/ToLoop modes: show state + action
            if (layer.TransitionPending)
            {
                return layer.TransitionPlayState switch
                {
                    TransitionPlayState.LoopingFrom => "▶ FROM  ⏳ pending...",
                    TransitionPlayState.LoopingTo => "▶ TO  ⏳ pending...",
                    _ => "⏳ pending..."
                };
            }
            
            return layer.TransitionPlayState switch
            {
                TransitionPlayState.LoopingFrom => "▶ FROM  │  Trigger →",
                TransitionPlayState.Transitioning => "⟳ Blending...",
                TransitionPlayState.LoopingTo => "▶ TO  │  ↺ Reset",
                _ => "▶ Trigger"
            };
        }

        private void SetTransitionFromBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            // Update visual element
            if (section.FromBlendSpaceElement != null)
                section.FromBlendSpaceElement.PreviewPosition = new Vector2(value, section.FromBlendSpaceElement.PreviewPosition.y);

            // Persist via PreviewSettings for the from state
            var fromState = layer.TransitionFrom;
            if (fromState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(fromState, value);
            else if (fromState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(fromState, new Vector2(value, 0));

            // Propagate both blend positions to preview backend for transition
            var fromBlendPos = PreviewSettings.GetBlendPosition(fromState);
            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }

        private void SetTransitionToBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            // Update visual element
            if (section.ToBlendSpaceElement != null)
                section.ToBlendSpaceElement.PreviewPosition = new Vector2(value, section.ToBlendSpaceElement.PreviewPosition.y);

            // Persist via PreviewSettings for the to state
            var toState = layer.TransitionTo;
            if (toState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(toState, value);
            else if (toState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(toState, new Vector2(value, 0));

            // Propagate both blend positions to preview backend for transition
            var fromBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(toState);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }
        
        private void OnTransitionProgressChanged(LayerSectionData section, float progress)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null || !layer.IsTransitionMode) return;
            
            // Update layer state
            layer.TransitionProgress = progress;
            
            // Update timeline to match progress
            if (section.Timeline != null)
            {
                section.Timeline.NormalizedTime = progress;
            }
            
            // Calculate transition state for this progress
            var fromBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
            
            // Use real transition data, exclude ghost bars for triggered modes
            bool includeGhosts = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
            var config = CreateTransitionConfig(
                layer.TransitionFrom,
                layer.TransitionTo,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
                includeGhostBars: includeGhosts);
            
            var snapshot = TransitionCalculator.CalculateStateFromProgress(in config, progress);
            
            // Propagate blend weight to backend
            preview?.SetLayerTransitionProgress(section.LayerIndex, snapshot.BlendWeight);
            
            // Calculate looping normalized times
            float elapsedTime = progress * config.TransitionDuration;
            float fromDuration = config.FromStateDuration;
            float toDuration = config.ToStateDuration;
            float fromNormalized = fromDuration > 0.001f 
                ? (elapsedTime % fromDuration) / fromDuration 
                : 0f;
            float toNormalized = toDuration > 0.001f 
                ? (elapsedTime % toDuration) / toDuration 
                : 0f;
            
            // Propagate to backend
            preview?.SetLayerTransitionNormalizedTimes(section.LayerIndex, fromNormalized, toNormalized);
        }
        
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
                            new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                            new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
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
                                    new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                                    new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
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
                section.SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
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
                        new Unity.Mathematics.float2(fromBlendVec.x, fromBlendVec.y),
                        new Unity.Mathematics.float2(toBlendVec.x, toBlendVec.y),
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
        
        #region Private - Composition State Events
        
        private void SubscribeToCompositionState()
        {
            if (compositionState == null) return;
            
            compositionState.PropertyChanged += OnCompositionStatePropertyChanged;
            compositionState.LayerChanged += OnCompositionLayerChanged;
            LogDebug($"Subscribed to CompositionState events (LayerCount={compositionState.LayerCount})");
        }
        
        private void UnsubscribeFromCompositionState()
        {
            if (compositionState == null) return;
            
            compositionState.PropertyChanged -= OnCompositionStatePropertyChanged;
            compositionState.LayerChanged -= OnCompositionLayerChanged;
        }
        
        private void OnCompositionStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ObservableCompositionState.MasterTime):
                    globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
                    break;
                    
                case nameof(ObservableCompositionState.IsPlaying):
                    isPlaying = compositionState.IsPlaying;
                    UpdatePlayButton();
                    break;
                    
                case nameof(ObservableCompositionState.SyncLayers):
                    syncLayersToggle?.SetValueWithoutNotify(compositionState.SyncLayers);
                    break;
            }
        }
        
        private void OnCompositionLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            // Skip refresh if we're the source of the change (prevents recursive cascade)
            if (_suppressLayerChangeEvents)
            {
                LogTrace($"OnCompositionLayerChanged: Suppressed (Property={e.PropertyName}, Layer={e.LayerIndex})");
                return;
            }
            
            LogTrace($"OnCompositionLayerChanged: Property={e.PropertyName}, Layer={e.LayerIndex}");
            
            var layerIndex = e.LayerIndex;
            if (layerIndex >= 0 && layerIndex < layerSections.Count)
            {
                RefreshLayerSection(layerSections[layerIndex]);
            }
        }
        
        #endregion
        
        #region Logging
        
        private void LogTrace(string message) => _logger?.Trace(message);
        private void LogDebug(string message) => _logger?.Debug(message);
        private void LogInfo(string message) => _logger?.Info(message);
        private void LogWarning(string message) => _logger?.Warning(message);
        private void LogError(string message) => _logger?.Error(message);
        
        #endregion
    }
}
