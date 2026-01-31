using System;
using System.Collections.Generic;
using DMotion;
using DMotion.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// A timeline control for transition preview with interactive editing.
    /// Shows full clip bars that can be positioned to create overlap (transition duration).
    /// 
    /// Visual layout (vertical):
    /// - From State bar: Top row, fixed at timeline start
    /// - To State bar: Bottom row, draggable left/right to change exit time
    /// - Overlap region: Sits ON TOP of both bars covering the intersection
    /// 
    /// The overlap gradient transitions from From color (blue) to To color (green),
    /// clearly showing the transition/blend region.
    /// 
    /// Drag the To bar horizontally to adjust exit time and transition duration.
    /// 
    /// Uses pure UIElements for consistent behavior with TimelineScrubber.
    /// </summary>
    [UxmlElement]
    internal partial class TransitionTimeline : TimelineBase
    {
        #region Constants
        
        private const float BarHeight = 20f;
        private const float BarSpacing = 4f;      // Vertical gap between bars
        private const float TrackPadding = 8f;    // Vertical padding in track
        private const float ScrubberWidth = 2f;
        private const float Padding = 12f;
        private const float ToBarHitZone = 10f;   // Pixels around To bar for hit detection
        
        /// <summary>Epsilon for float comparisons to detect meaningful changes.</summary>
        private const float ValueChangeEpsilon = 0.0001f;
        
        #endregion
        
        #region Drag State
        
        private enum DragTarget
        {
            None,
            Scrubber,
            ToBar
        }
        
        private DragTarget currentDragTarget = DragTarget.None;
        private float dragStartValue;
        private float dragStartMouseX;
        private bool isToBarHovered;
        
        #endregion
        
        #region State
        
        // State references for type checking and duration calculation
        private AnimationStateAsset fromStateAsset;
        private AnimationStateAsset toStateAsset;
        
        // Cached effective durations (updated when blend position changes)
        private float fromStateDuration = 1f;
        private float toStateDuration = 1f;
        
        // Transition offset (for starting to-state at a specific point)
        private float transitionOffset;
        
        // Display names
        private string fromStateName = "From State";
        private string toStateName = "To State";
        
        // Cached blend curve (read from asset on configure)
        private AnimationCurve blendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        
        // Min/max bounds - use shared calculator constants
        private const float MinTransitionDuration = TransitionTimingCalculator.MinTransitionDuration;
        private const float MaxTransitionDuration = TransitionTimingCalculator.MaxTransitionDuration;
        private const int MaxVisualCycles = TransitionTimingCalculator.MaxVisualCycles;
        
        // Transition timing values (requested = user intent, effective = clamped for logic)
        private float requestedExitTime = 0.75f;
        private float exitTime = 0.75f;
        private float requestedTransitionDuration = 0.25f;
        private float transitionDuration = 0.25f;
        
        // Cached timing result from TransitionTimingCalculator
        private TransitionTimingResult cachedTimingResult;
        
        // Cached TransitionStateConfig for calculator (updated when timing changes)
        private TransitionStateConfig cachedTransitionConfig;
        
        // Cached snapshot for current frame (invalidated when NormalizedTime changes)
        private TransitionStateSnapshot cachedSnapshot;
        private float cachedSnapshotNormalizedTime = -1f;
        
        #endregion
        
        #region UI Elements
        
        // Header (state names + durations)
        private readonly Label fromStateLabel;
        private readonly Label fromDurationLabel;
        private readonly Label toStateLabel;
        private readonly Label toDurationLabel;
        
        // Track area
        private readonly VisualElement trackArea;
        private readonly VisualElement fromBar;
        private readonly VisualElement toBar;
        private readonly VisualElement overlapArea;
        private readonly VisualElement blendCurveElement;
        private readonly Label overlapDurationLabel;
        private readonly VisualElement scrubber;
        private readonly VisualElement timeGridContainer;
        
        // Ghost cycle bars for visualizing context (preview only)
        // fromGhostBars: LEFT of from-bar (when exitTime==0 or from-duration shrunk)
        // toGhostBars: RIGHT of to-bar (when to-duration shrunk below transition duration)
        private readonly List<VisualElement> fromGhostBars = new List<VisualElement>();
        private readonly List<VisualElement> toGhostBars = new List<VisualElement>();
        
        // Footer
        private readonly Button playButton;
        private readonly Label timeLabel;
        private readonly Label blendLabel;
        
        // Cached label values to avoid string allocations
        private float cachedTimeSeconds = -1f;
        private float cachedTotalDuration = -1f;
        private float cachedBlendWeight = -1f;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when the transition progress changes.
        /// </summary>
        public event Action<float> OnTransitionProgressChanged;
        
        /// <summary>
        /// Fired when exit time is changed via dragging.
        /// </summary>
        public event Action<float> OnExitTimeChanged;
        
        /// <summary>
        /// Fired when transition duration is changed via dragging.
        /// </summary>
        public event Action<float> OnTransitionDurationChanged;
        
        /// <summary>
        /// Fired when transition offset is changed via dragging.
        /// </summary>
#pragma warning disable CS0067
        public event Action<float> OnTransitionOffsetChanged;
#pragma warning restore CS0067
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether the user is currently dragging a timeline element.
        /// </summary>
        public new bool IsDragging => currentDragTarget != DragTarget.None;
        
        /// <summary>
        /// The exit time in seconds (when the transition starts).
        /// The value is stored as requestedExitTime (can exceed duration for ghost bar display),
        /// but exitTime is clamped to [0, fromStateDuration] for actual logic.
        /// </summary>
        public float ExitTime
        {
            get => exitTime;
            set
            {
                var newValue = Mathf.Max(0f, value);
                if (Math.Abs(newValue - requestedExitTime) > ValueChangeEpsilon)
                {
                    requestedExitTime = newValue;
                    // Clamp for logic - always within one cycle
                    exitTime = Mathf.Clamp(requestedExitTime, 0f, fromStateDuration);
                    RecalculateTransitionDuration();
                    RefreshLayout();
                }
            }
        }
        
        /// <summary>
        /// The transition duration in seconds.
        /// </summary>
        public float TransitionDuration
        {
            get => transitionDuration;
            set
            {
                var newValue = Mathf.Clamp(value, MinTransitionDuration, MaxTransitionDuration);
                if (Math.Abs(newValue - requestedTransitionDuration) > ValueChangeEpsilon)
                {
                    requestedTransitionDuration = newValue;
                    transitionDuration = Mathf.Clamp(newValue, MinTransitionDuration, toStateDuration);
                    RefreshLayout();
                }
            }
        }
        
        /// <summary>
        /// Duration of the from state in seconds.
        /// </summary>
        public float FromStateDuration
        {
            get => fromStateDuration;
            set
            {
                fromStateDuration = Mathf.Max(0.01f, value);
                RecalculateTransitionDuration();
                RefreshLayout();
            }
        }
        
        /// <summary>
        /// Duration of the to state in seconds.
        /// </summary>
        public float ToStateDuration
        {
            get => toStateDuration;
            set
            {
                toStateDuration = Mathf.Max(0.01f, value);
                RecalculateTransitionDuration();
                RefreshLayout();
            }
        }
        
        /// <summary>
        /// Offset into the to state when transition begins (0-1).
        /// </summary>
        public float TransitionOffset
        {
            get => transitionOffset;
            set
            {
                var newValue = Mathf.Clamp01(value);
                if (Math.Abs(newValue - transitionOffset) > ValueChangeEpsilon)
                {
                    transitionOffset = newValue;
                    UpdateLayout();
                }
            }
        }
        
        /// <summary>
        /// Name of the from state (for display).
        /// </summary>
        public string FromStateName
        {
            get => fromStateName;
            set
            {
                fromStateName = value ?? "From State";
                if (fromStateLabel != null) fromStateLabel.text = fromStateName;
            }
        }
        
        /// <summary>
        /// Name of the to state (for display).
        /// </summary>
        public string ToStateName
        {
            get => toStateName;
            set
            {
                toStateName = value ?? "To State";
                if (toStateLabel != null) toStateLabel.text = toStateName;
            }
        }
        
        /// <summary>
        /// The blend curve for the transition.
        /// </summary>
        public AnimationCurve BlendCurve
        {
            get => blendCurve;
            set
            {
                blendCurve = value ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
                
                // Update the cached config's curve so calculations use the new curve
                cachedTransitionConfig.Curve = CurveUtils.ConvertToBlendCurve(blendCurve);
                
                // Invalidate cached snapshot to force recalculation with new curve
                cachedSnapshotNormalizedTime = -1f;
                
                blendCurveElement?.MarkDirtyRepaint();
            }
        }
        
        /// <summary>
        /// Computed property: number of FROM bar cycles to display.
        /// Uses cached timing result from TransitionTimingCalculator.
        /// </summary>
        private int FromVisualCycles => cachedTimingResult.FromVisualCycles > 0 ? cachedTimingResult.FromVisualCycles : 1;
        
        /// <summary>
        /// Computed property: number of TO bar cycles to display.
        /// Uses cached timing result from TransitionTimingCalculator.
        /// </summary>
        private int ToVisualCycles => cachedTimingResult.ToVisualCycles > 0 ? cachedTimingResult.ToVisualCycles : 1;
        
        /// <summary>
        /// Computed property: whether the FROM ghost is due to duration shrink (vs context).
        /// This affects how the to-bar position is calculated.
        /// </summary>
        private bool IsFromGhostDurationShrink => cachedTimingResult.IsFromGhostDurationShrink;
        
        /// <summary>
        /// Computed property: the effective exit time clamped to valid range.
        /// </summary>
        private float EffectiveExitTime
        {
            get
            {
                float minExitTime = Mathf.Max(0f, fromStateDuration - toStateDuration);
                return Mathf.Clamp(requestedExitTime, minExitTime, fromStateDuration);
            }
        }
        
        /// <summary>
        /// Gets the current transition state snapshot using the unified calculator.
        /// Cached per-frame to avoid recalculating multiple times.
        /// </summary>
        private TransitionStateSnapshot CurrentSnapshot
        {
            get
            {
                // Return cached snapshot if NormalizedTime hasn't changed
                if (Mathf.Approximately(cachedSnapshotNormalizedTime, NormalizedTime))
                    return cachedSnapshot;
                
                // Calculate new snapshot using Runtime calculator (curve already applied)
                cachedSnapshot = TransitionCalculator.CalculateState(in cachedTransitionConfig, NormalizedTime);
                cachedSnapshotNormalizedTime = NormalizedTime;
                return cachedSnapshot;
            }
        }
        
        /// <summary>
        /// Gets the current transition progress (0 before exit, 0-1 during transition, 1 after).
        /// Uses the unified TransitionStateCalculator for consistent behavior across all preview systems.
        /// </summary>
        public float TransitionProgress => CurrentSnapshot.RawProgress;
        
        /// <summary>
        /// Gets the normalized time (0-1) within the "from" state's clip.
        /// This is the position the from-state animation should be sampled at.
        /// Uses the unified TransitionStateCalculator for consistent behavior.
        /// </summary>
        public float FromStateNormalizedTime => CurrentSnapshot.FromStateNormalizedTime;
        
        /// <summary>
        /// Gets the normalized time (0-1) within the "to" state's clip.
        /// This is the position the to-state animation should be sampled at.
        /// Uses the unified TransitionStateCalculator for consistent behavior.
        /// </summary>
        public float ToStateNormalizedTime => CurrentSnapshot.ToStateNormalizedTime;
        
        /// <summary>
        /// Gets the blend weight (0-1) with curve applied.
        /// 0 = fully "from" state, 1 = fully "to" state.
        /// Uses the unified TransitionStateCalculator for consistent behavior.
        /// </summary>
        public float BlendWeight => CurrentSnapshot.BlendWeight;
        
        /// <summary>
        /// Gets the current section of the transition timeline.
        /// </summary>
        public TransitionSection CurrentSection => (TransitionSection)CurrentSnapshot.CurrentSection;
        
        #endregion
        
        #region Constructor
        
        public TransitionTimeline()
        {
            AddToClassList("transition-timeline");
            
            // Header with state names and durations
            var header = new VisualElement();
            header.AddToClassList("transition-timeline__header");
            
            // Left side: From state info
            var fromInfo = new VisualElement();
            fromInfo.AddToClassList("transition-timeline__header-info");
            
            fromStateLabel = new Label(fromStateName);
            fromStateLabel.AddToClassList("transition-timeline__state-label");
            fromStateLabel.AddToClassList("transition-timeline__state-label--from");
            fromInfo.Add(fromStateLabel);
            
            fromDurationLabel = new Label();
            fromDurationLabel.AddToClassList("transition-timeline__duration-label");
            fromDurationLabel.AddToClassList("transition-timeline__duration-label--from");
            fromInfo.Add(fromDurationLabel);
            
            header.Add(fromInfo);
            
            // Right side: To state info
            var toInfo = new VisualElement();
            toInfo.AddToClassList("transition-timeline__header-info");
            toInfo.AddToClassList("transition-timeline__header-info--right");
            
            toStateLabel = new Label(toStateName);
            toStateLabel.AddToClassList("transition-timeline__state-label");
            toStateLabel.AddToClassList("transition-timeline__state-label--to");
            toInfo.Add(toStateLabel);
            
            toDurationLabel = new Label();
            toDurationLabel.AddToClassList("transition-timeline__duration-label");
            toDurationLabel.AddToClassList("transition-timeline__duration-label--to");
            toInfo.Add(toDurationLabel);
            
            header.Add(toInfo);
            
            Add(header);
            
            // Track area (contains bars, overlap, scrubber)
            trackArea = new VisualElement();
            trackArea.AddToClassList("transition-timeline__track");
            
            // Time grid (background)
            timeGridContainer = new VisualElement();
            timeGridContainer.AddToClassList("transition-timeline__grid");
            trackArea.Add(timeGridContainer);
            
            // From bar (top row)
            fromBar = new VisualElement();
            fromBar.AddToClassList("transition-timeline__bar");
            fromBar.AddToClassList("transition-timeline__bar--from");
            trackArea.Add(fromBar);
            
            // To bar (bottom row, draggable to change exit time)
            toBar = new VisualElement();
            toBar.AddToClassList("transition-timeline__bar");
            toBar.AddToClassList("transition-timeline__bar--to");
            trackArea.Add(toBar);
            
            // Overlap/transition area - sits ON TOP of both bars (added last for z-order)
            // Spans from top of From bar to bottom of To bar, covering intersection
            overlapArea = new VisualElement();
            overlapArea.AddToClassList("transition-timeline__overlap");
            overlapArea.generateVisualContent += DrawOverlapGradient;
            overlapArea.pickingMode = PickingMode.Ignore; // Clicks pass through to bars
            
            blendCurveElement = new VisualElement();
            blendCurveElement.AddToClassList("transition-timeline__curve");
            blendCurveElement.generateVisualContent += DrawBlendCurve;
            blendCurveElement.pickingMode = PickingMode.Ignore;
            overlapArea.Add(blendCurveElement);
            
            overlapDurationLabel = new Label();
            overlapDurationLabel.AddToClassList("transition-timeline__overlap-label");
            overlapDurationLabel.pickingMode = PickingMode.Ignore;
            overlapArea.Add(overlapDurationLabel);
            
            trackArea.Add(overlapArea);
            
            // Scrubber (on very top)
            scrubber = new VisualElement();
            scrubber.AddToClassList("transition-timeline__scrubber");
            trackArea.Add(scrubber);
            
            Add(trackArea);
            
            // Footer
            var footer = new VisualElement();
            footer.AddToClassList("transition-timeline__footer");
            
            playButton = new Button(TogglePlayPause);
            playButton.AddToClassList("transition-timeline__play-button");
            playButton.text = "\u25b6";
            footer.Add(playButton);
            
            timeLabel = new Label("0.00s / 0.00s");
            timeLabel.AddToClassList("transition-timeline__time-label");
            footer.Add(timeLabel);
            
            var spacer = new VisualElement();
            spacer.AddToClassList("toolbar-spacer");
            footer.Add(spacer);
            
            blendLabel = new Label("Blend: 0%");
            blendLabel.AddToClassList("transition-timeline__blend-label");
            footer.Add(blendLabel);
            
            Add(footer);
            
            // Register for layout changes
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // Register custom pointer handling for dragging bars/handles
            trackArea.RegisterCallback<PointerDownEvent>(OnTrackPointerDown);
            trackArea.RegisterCallback<PointerMoveEvent>(OnTrackPointerMove);
            trackArea.RegisterCallback<PointerUpEvent>(OnTrackPointerUp);
            trackArea.RegisterCallback<PointerLeaveEvent>(OnTrackPointerLeave);
            
            // Hover effects for To bar - also highlight overlap
            toBar.RegisterCallback<PointerEnterEvent>(OnToBarPointerEnter);
            toBar.RegisterCallback<PointerLeaveEvent>(OnToBarPointerLeave);
            
            // Initialize
            UpdateDuration();
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Configures the timeline for a specific transition.
        /// </summary>
        public void Configure(
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            float exitTime,
            float transitionDuration,
            float transitionOffset = 0f,
            AnimationCurve blendCurve = null)
        {
            // Store state references for dynamic duration updates
            fromStateAsset = fromState;
            toStateAsset = toState;
            
            FromStateName = fromState?.name ?? "Any State";
            ToStateName = toState?.name ?? "To State";
            
            // Get initial durations using persisted blend positions
            var fromBlendPos = PreviewSettings.GetBlendPosition(fromState);
            var toBlendPos = PreviewSettings.GetBlendPosition(toState);
            fromStateDuration = fromState?.GetEffectiveDuration(fromBlendPos) ?? 1f;
            toStateDuration = toState?.GetEffectiveDuration(toBlendPos) ?? 1f;
            
            // Store requested values, clamp for logic
            this.requestedExitTime = Mathf.Max(0f, exitTime);
            this.exitTime = Mathf.Clamp(this.requestedExitTime, 0f, fromStateDuration);
            this.requestedTransitionDuration = Mathf.Max(0.01f, transitionDuration);
            this.transitionDuration = Mathf.Clamp(this.requestedTransitionDuration, 0f, toStateDuration);
            this.transitionOffset = Mathf.Clamp01(transitionOffset);
            this.blendCurve = blendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
            
            RefreshLayout();
        }
        
        /// <summary>
        /// Updates the from/to state durations based on blend positions.
        /// Call this when blend positions change to reflect accurate clip durations.
        /// 
        /// Strategy: Keep transition duration constant (time-based), adjust exit time to fit.
        /// This ensures transitions like walk→fall take the same time regardless of animation speed.
        /// Only adjust transition duration if it exceeds min/max bounds.
        /// </summary>
        public void UpdateDurationsForBlendPosition(Vector2 fromBlendPos, Vector2 toBlendPos)
        {
            bool changed = false;
            float oldFromDuration = fromStateDuration;
            
            if (fromStateAsset != null)
            {
                float newDuration = fromStateAsset.GetEffectiveDuration(fromBlendPos);
                if (Mathf.Abs(newDuration - fromStateDuration) > 0.001f)
                {
                    FromStateDuration = newDuration;
                    changed = true;
                }
            }
            
            if (toStateAsset != null)
            {
                float newDuration = toStateAsset.GetEffectiveDuration(toBlendPos);
                if (Mathf.Abs(newDuration - toStateDuration) > 0.001f)
                {
                    ToStateDuration = newDuration;
                    changed = true;
                }
            }
            
            if (changed)
            {
                // Recalculate layout while keeping transition duration constant
                AdjustTimingsForDurationChange(oldFromDuration);
                RefreshLayout();
            }
        }
        
        /// <summary>
        /// Recalculates the cached timing result using TransitionTimingCalculator.
        /// Also updates the TransitionStateConfig for the unified calculator.
        /// Should be called whenever timing values change.
        /// </summary>
        private void RecalculateTimingResult()
        {
            var input = new TransitionTimingInput
            {
                FromStateDuration = fromStateDuration,
                ToStateDuration = toStateDuration,
                RequestedExitTime = requestedExitTime,
                RequestedTransitionDuration = requestedTransitionDuration,
                FromIsBlendState = fromStateAsset != null && AnimationStateUtils.IsBlendState(fromStateAsset),
                ToIsBlendState = toStateAsset != null && AnimationStateUtils.IsBlendState(toStateAsset)
            };
            
            cachedTimingResult = TransitionTimingCalculator.Calculate(input);
            
            // Update TransitionStateConfig for the unified calculator
            cachedTransitionConfig = new TransitionStateConfig
            {
                FromStateDuration = fromStateDuration,
                ToStateDuration = toStateDuration,
                ExitTime = requestedExitTime,
                TransitionDuration = requestedTransitionDuration,
                TransitionOffset = transitionOffset,
                Timing = cachedTimingResult,
                Curve = CurveUtils.ConvertToBlendCurve(blendCurve)
            };
            
            // Invalidate cached snapshot since config changed
            cachedSnapshotNormalizedTime = -1f;
        }
        
        /// <summary>
        /// Adjusts exit time when state durations change.
        /// TRANSITION DURATION is preserved (what users care about - how long the blend takes).
        /// EXIT TIME is adjusted to maintain the overlap: exitTime = fromDuration - transitionDuration.
        /// </summary>
        private void AdjustTimingsForDurationChange(float oldFromDuration)
        {
            float oldExitTime = exitTime;

            // Preserve transition duration, adjust exit time to maintain overlap
            // exitTime = fromStateDuration - transitionDuration (so overlap = transitionDuration)
            float desiredExitTime = fromStateDuration - requestedTransitionDuration;

            // Minimum exit time ensures to-bar ends at or after from-bar
            float minExitTime = Mathf.Max(0f, fromStateDuration - toStateDuration);

            // Clamp exit time to valid range
            exitTime = Mathf.Clamp(desiredExitTime, minExitTime, fromStateDuration);
            requestedExitTime = exitTime; // Update requested to match (no ghost bars from exit time)

            // Transition duration stays as requested, clamped to to-state duration
            transitionDuration = Mathf.Clamp(requestedTransitionDuration, MinTransitionDuration, toStateDuration);

            // Notify listeners that exit time changed (so inspector updates)
            if (Math.Abs(exitTime - oldExitTime) <= ValueChangeEpsilon) return; 
            
            OnExitTimeChanged?.Invoke(exitTime);
        }

        #endregion

        #region TimelineBase Overrides

        protected override Rect GetTrackRect() => trackArea?.contentRect ?? Rect.zero;

        protected override void OnCurrentTimeChanged()
        {
            base.OnCurrentTimeChanged();
            UpdateLabels();
            UpdateScrubberPosition();
            OnTransitionProgressChanged?.Invoke(TransitionProgress);
        }
        
        protected override void OnPlayingStateChanged()
        {
            base.OnPlayingStateChanged();
            
            if (playButton == null) return;

            playButton.text = IsPlaying ? "\u275a\u275a" : "\u25b6";
            playButton.tooltip = IsPlaying ? "Pause" : "Play";
        }

        protected override void OnGeometryChanged(GeometryChangedEvent evt)
        {
            base.OnGeometryChanged(evt);
            UpdateLayout();
        }
        
        #endregion
        
        #region Custom Drawing
        
        private void DrawBlendCurve(MeshGenerationContext ctx)
        {
            if (blendCurve == null || blendCurve.length < 2) return;
            
            var rect = blendCurveElement.contentRect;
            if (rect.width < 10 || rect.height < 10) return;
            
            var painter = ctx.painter2D;
            painter.strokeColor = PreviewEditorColors.CurveAccent;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            
            const int segments = 30;
            const float curvePadding = 4f;
            float curveWidth = rect.width - curvePadding * 2;
            float curveHeight = rect.height - curvePadding * 2;
            
            painter.BeginPath();
            
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float curveValue = blendCurve.Evaluate(t);
                
                float x = curvePadding + t * curveWidth;
                float y = curvePadding + (1f - curveValue) * curveHeight;
                
                if (i == 0)
                    painter.MoveTo(new Vector2(x, y));
                else
                    painter.LineTo(new Vector2(x, y));
            }
            
            painter.Stroke();
        }
        
        private void DrawOverlapGradient(MeshGenerationContext ctx)
        {
            var rect = overlapArea.contentRect;
            if (rect.width < 2 || rect.height < 2) return;
            
            var painter = ctx.painter2D;
            
            // Draw gradient from "from" color (blue) to "to" color (green)
            // Using vertical strips to simulate a horizontal gradient
            const int strips = 20;
            float stripWidth = rect.width / strips;
            
            // Determine colors based on hover/drag state
            Color fromColor, toColor;
            bool isDragging = currentDragTarget == DragTarget.ToBar;
            
            if (isDragging)
            {
                // Dragging: brightest colors - matches .transition-timeline__bar--dragging
                fromColor = new Color(100f/255f, 140f/255f, 190f/255f, 1f);
                toColor = new Color(100f/255f, 180f/255f, 130f/255f, 1f);
            }
            else if (isToBarHovered)
            {
                // Hover: brighter colors - matches .transition-timeline__bar--hover
                fromColor = new Color(85f/255f, 125f/255f, 175f/255f, 1f);
                toColor = new Color(85f/255f, 160f/255f, 115f/255f, 1f);
            }
            else
            {
                // Normal: matches bar USS exactly - rgb(70,110,160) and rgb(70,140,100)
                fromColor = new Color(70f/255f, 110f/255f, 160f/255f, 1f);
                toColor = new Color(70f/255f, 140f/255f, 100f/255f, 1f);
            }
            
            for (int i = 0; i < strips; i++)
            {
                float t = i / (float)(strips - 1);
                var color = Color.Lerp(fromColor, toColor, t);
                
                painter.fillColor = color;
                painter.BeginPath();
                
                float x = i * stripWidth;
                float w = stripWidth + 1; // +1 to avoid gaps
                
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x + w, 0));
                painter.LineTo(new Vector2(x + w, rect.height));
                painter.LineTo(new Vector2(x, rect.height));
                painter.ClosePath();
                painter.Fill();
            }
        }
        
        #endregion
        
        #region Layout
        
        private void UpdateLayout()
        {
            if (trackArea == null || trackArea.resolvedStyle.width <= 0) return;
            
            float trackWidth = trackArea.resolvedStyle.width - Padding * 2;
            float totalDuration = GetTotalTimelineDuration();
            if (totalDuration <= 0 || trackWidth <= 0) return;
            
            float pixelsPerSecond = trackWidth / totalDuration;
            
            // Calculate bar widths
            float fromBarWidthPx = fromStateDuration * pixelsPerSecond;
            float toBarWidthPx = toStateDuration * pixelsPerSecond;
            
            // Vertical layout: From bar on top, To bar below
            float fromBarTop = TrackPadding;
            float toBarTop = fromBarTop + BarHeight + BarSpacing;
            float totalBarsHeight = BarHeight * 2 + BarSpacing;  // Both bars stacked
            
            // Calculate FROM ghost bar offset (cycles BEFORE the main from-bar)
            int fromGhostCount = FromVisualCycles - 1;
            float fromGhostWidthPx = fromGhostCount * fromBarWidthPx;
            
            // Calculate TO ghost bar count (cycles AFTER the main to-bar)
            int toGhostCount = ToVisualCycles - 1;
            
            // Update FROM ghost bars (LEFT of from-bar)
            UpdateFromGhostBars(fromGhostCount, fromBarWidthPx, fromBarTop);
            
            // From bar (top row) - positioned after FROM ghost bars
            fromBar.style.top = fromBarTop;
            fromBar.style.height = BarHeight;
            fromBar.style.left = Padding + fromGhostWidthPx;
            fromBar.style.width = fromBarWidthPx;
            
            // To bar (bottom row) positioning depends on ghost bar type:
            // - Duration shrink ghost: use requestedExitTime (to-bar at original intended position)
            // - Context ghost or no ghost: use ghostOffset + exitTime
            float toBarLeftPx;
            if (IsFromGhostDurationShrink)
            {
                // Duration shrink: position to-bar at the requestedExitTime on the timeline
                // This places it within the appropriate cycle at the correct position
                toBarLeftPx = Padding + requestedExitTime * pixelsPerSecond;
            }
            else
            {
                // Context ghost or no ghost: position relative to ghost offset + exit time
                toBarLeftPx = Padding + fromGhostWidthPx + exitTime * pixelsPerSecond;
            }
            toBar.style.top = toBarTop;
            toBar.style.height = BarHeight;
            toBar.style.left = toBarLeftPx;
            toBar.style.width = toBarWidthPx;
            
            // Update TO ghost bars (RIGHT of to-bar)
            float toBarRightPx = toBarLeftPx + toBarWidthPx;
            UpdateToGhostBars(toGhostCount, toBarWidthPx, toBarTop, toBarRightPx);
            
            // Overlap region - intersection between from-bar (including ghosts) and to-bar
            // For duration-shrink ghost: from-bar spans multiple cycles
            float totalFromBarEndPx = Padding + FromVisualCycles * fromBarWidthPx;
            float overlapStartPx = toBarLeftPx;
            float naturalOverlapEndPx = Mathf.Min(totalFromBarEndPx, toBarLeftPx + toBarWidthPx);
            float naturalOverlapWidthPx = Mathf.Max(0, naturalOverlapEndPx - overlapStartPx);
            
            // If transition duration exceeds natural overlap, extend the overlap bar
            // This shows that the transition continues beyond what the bars naturally cover
            float naturalOverlapDuration = naturalOverlapWidthPx / pixelsPerSecond;
            float effectiveTransitionDuration = Mathf.Max(naturalOverlapDuration, requestedTransitionDuration);
            float overlapWidthPx = effectiveTransitionDuration * pixelsPerSecond;
            
            // Overlap area - spans BOTH bars vertically, may extend past bars if transition is longer
            overlapArea.style.top = fromBarTop;
            overlapArea.style.height = totalBarsHeight;
            overlapArea.style.left = overlapStartPx;
            overlapArea.style.width = overlapWidthPx;
            overlapDurationLabel.text = $"{effectiveTransitionDuration:F2}s";
            
            // Update header duration labels
            if (fromDurationLabel != null)
                fromDurationLabel.text = $"({fromStateDuration:F2}s)";
            if (toDurationLabel != null)
                toDurationLabel.text = $"({toStateDuration:F2}s)";
            
            // Update scrubber
            UpdateScrubberPosition();
            
            // Update time grid
            UpdateTimeGrid(trackWidth, totalDuration, pixelsPerSecond);
            
            // Trigger repaints
            overlapArea?.MarkDirtyRepaint();
            blendCurveElement?.MarkDirtyRepaint();
        }
        
        /// <summary>
        /// Updates FROM ghost bar elements (LEFT of from-bar).
        /// Shows previous cycles when exitTime==0 or from-duration shrunk below exit time.
        /// </summary>
        private void UpdateFromGhostBars(int count, float barWidthPx, float barTop)
        {
            // Ensure we have the right number of ghost bars
            while (fromGhostBars.Count < count)
            {
                var ghostBar = new VisualElement();
                ghostBar.AddToClassList("transition-timeline__bar");
                ghostBar.AddToClassList("transition-timeline__bar--from");
                ghostBar.AddToClassList("transition-timeline__bar--ghost");
                
                // Insert before the main fromBar to maintain z-order
                int insertIndex = trackArea.IndexOf(fromBar);
                if (insertIndex >= 0)
                    trackArea.Insert(insertIndex, ghostBar);
                else
                    trackArea.Add(ghostBar);
                    
                fromGhostBars.Add(ghostBar);
            }
            
            // Hide extra ghost bars if we have too many
            for (int i = 0; i < fromGhostBars.Count; i++)
            {
                bool visible = i < count;
                fromGhostBars[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (!visible) continue;

                // Position ghost bar to the LEFT of main from-bar
                fromGhostBars[i].style.top = barTop;
                fromGhostBars[i].style.height = BarHeight;
                fromGhostBars[i].style.left = Padding + i * barWidthPx;
                fromGhostBars[i].style.width = barWidthPx;
            }
        }
        
        /// <summary>
        /// Updates TO ghost bar elements (RIGHT of to-bar).
        /// Shows continuation cycles when to-duration shrunk below transition duration.
        /// </summary>
        private void UpdateToGhostBars(int count, float barWidthPx, float barTop, float toBarRightPx)
        {
            // Ensure we have the right number of ghost bars
            while (toGhostBars.Count < count)
            {
                var ghostBar = new VisualElement();
                ghostBar.AddToClassList("transition-timeline__bar");
                ghostBar.AddToClassList("transition-timeline__bar--to");
                ghostBar.AddToClassList("transition-timeline__bar--ghost");
                
                // Insert after the main toBar to maintain z-order
                int insertIndex = trackArea.IndexOf(toBar);
                if (insertIndex >= 0)
                    trackArea.Insert(insertIndex + 1, ghostBar);
                else
                    trackArea.Add(ghostBar);
                    
                toGhostBars.Add(ghostBar);
            }
            
            // Hide extra ghost bars if we have too many
            for (int i = 0; i < toGhostBars.Count; i++)
            {
                bool visible = i < count;
                toGhostBars[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (!visible) continue; 

                // Position ghost bar to the RIGHT of main to-bar
                toGhostBars[i].style.top = barTop;
                toGhostBars[i].style.height = BarHeight;
                toGhostBars[i].style.left = toBarRightPx + i * barWidthPx;
                toGhostBars[i].style.width = barWidthPx;
            }
        }
        
        private void UpdateScrubberPosition()
        {
            if (scrubber == null || trackArea == null) return;
            
            float trackWidth = trackArea.resolvedStyle.width - Padding * 2;
            float totalDuration = GetTotalTimelineDuration();
            if (totalDuration <= 0 || trackWidth <= 0) return;
            
            float scrubberX = Padding + NormalizedTime * trackWidth * (totalDuration / Duration);
            scrubber.style.left = scrubberX - ScrubberWidth / 2;
        }
        
        private void UpdateTimeGrid(float trackWidth, float totalDuration, float pixelsPerSecond)
        {
            if (timeGridContainer == null) return;
            
            timeGridContainer.Clear();
            
            // Determine tick interval based on available space
            float interval = 0.5f;
            if (pixelsPerSecond * interval < 30) interval = 1f;
            if (pixelsPerSecond * interval < 30) interval = 2f;
            
            for (float t = 0; t <= totalDuration; t += interval)
            {
                var tick = new VisualElement();
                tick.AddToClassList("transition-timeline__grid-line");
                tick.style.left = Padding + t * pixelsPerSecond;
                timeGridContainer.Add(tick);
                
                var tickLabel = new Label($"{t:F1}s");
                tickLabel.AddToClassList("transition-timeline__grid-label");
                tickLabel.style.left = Padding + t * pixelsPerSecond - 15;
                timeGridContainer.Add(tickLabel);
            }
        }
        
        #endregion
        
        #region Input Handling
        
        private void OnTrackPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            
            Focus();
            
            var localPos = evt.localPosition;
            float trackWidth = trackArea.resolvedStyle.width - Padding * 2;
            
            // Check if clicking on the To bar (for dragging to change exit time)
            if (IsPointOverToBar(localPos))
            {
                StartDrag(DragTarget.ToBar, exitTime, localPos.x, evt.pointerId);
                toBar.AddToClassList("transition-timeline__bar--dragging");
                overlapArea.AddToClassList("transition-timeline__overlap--dragging");
                overlapArea.MarkDirtyRepaint();  // Redraw gradient with drag colors
                evt.StopPropagation();
                return;
            }
            
            // Otherwise scrub timeline
            StartDrag(DragTarget.Scrubber, NormalizedTime, localPos.x, evt.pointerId);
            float normalizedX = Mathf.Clamp01((localPos.x - Padding) / trackWidth);
            NormalizedTime = normalizedX;
            evt.StopPropagation();
        }
        
        private bool IsPointOverToBar(Vector2 localPos)
        {
            // Get To bar bounds
            float toBarLeft = toBar.resolvedStyle.left;
            float toBarTop = toBar.resolvedStyle.top;
            float toBarWidth = toBar.resolvedStyle.width;
            float toBarHeight = toBar.resolvedStyle.height;
            
            // Expand hit zone slightly for easier grabbing
            var hitRect = new Rect(
                toBarLeft - ToBarHitZone / 2,
                toBarTop,
                toBarWidth + ToBarHitZone,
                toBarHeight
            );
            
            return hitRect.Contains(localPos);
        }
        
        private void OnTrackPointerMove(PointerMoveEvent evt)
        {
            if (currentDragTarget != DragTarget.None)
            {
                HandleDrag(evt.localPosition.x);
                evt.StopPropagation();
            }
        }
        
        private void OnTrackPointerUp(PointerUpEvent evt)
        {
            if (currentDragTarget != DragTarget.None)
            {
                EndDrag(evt.pointerId);
                evt.StopPropagation();
            }
        }
        
        private void OnTrackPointerLeave(PointerLeaveEvent evt)
        {
            // No-op
        }
        
        private void OnToBarPointerEnter(PointerEnterEvent evt)
        {
            // Highlight both To bar and overlap together
            isToBarHovered = true;
            toBar.AddToClassList("transition-timeline__bar--hover");
            overlapArea.AddToClassList("transition-timeline__overlap--hover");
            overlapArea.MarkDirtyRepaint();  // Redraw gradient with hover colors
        }
        
        private void OnToBarPointerLeave(PointerLeaveEvent evt)
        {
            // Remove highlight from both (unless dragging)
            if (currentDragTarget != DragTarget.ToBar)
            {
                isToBarHovered = false;
                toBar.RemoveFromClassList("transition-timeline__bar--hover");
                overlapArea.RemoveFromClassList("transition-timeline__overlap--hover");
                overlapArea.MarkDirtyRepaint();  // Redraw gradient with normal colors
            }
        }
        
        private void StartDrag(DragTarget target, float startValue, float mouseX, int pointerId)
        {
            currentDragTarget = target;
            dragStartValue = startValue;
            dragStartMouseX = mouseX;
            trackArea.CapturePointer(pointerId);
        }
        
        private void EndDrag(int pointerId)
        {
            if (currentDragTarget == DragTarget.ToBar)
            {
                toBar.RemoveFromClassList("transition-timeline__bar--dragging");
                overlapArea.RemoveFromClassList("transition-timeline__overlap--dragging");
                // Also remove hover since pointer might have left during drag
                isToBarHovered = false;
                toBar.RemoveFromClassList("transition-timeline__bar--hover");
                overlapArea.RemoveFromClassList("transition-timeline__overlap--hover");
                overlapArea.MarkDirtyRepaint();  // Redraw gradient with normal colors
            }
            
            currentDragTarget = DragTarget.None;
            trackArea.ReleasePointer(pointerId);
        }
        
        private void HandleDrag(float mouseX)
        {
            float trackWidth = trackArea.resolvedStyle.width - Padding * 2;
            float totalDuration = GetTotalTimelineDuration();
            float pixelsPerSecond = trackWidth / totalDuration;
            
            if (currentDragTarget == DragTarget.Scrubber)
            {
                float normalizedX = Mathf.Clamp01((mouseX - Padding) / trackWidth);
                NormalizedTime = normalizedX;
            }
            else if (currentDragTarget == DragTarget.ToBar)
            {
                // Dragging To bar changes exit time AND transition duration
                float deltaX = mouseX - dragStartMouseX;
                float deltaSeconds = deltaX / pixelsPerSecond;
                float newExitTime = dragStartValue + deltaSeconds;

                // Minimum exit time ensures to-bar ends at or after from-bar (must end in to-state)
                float minExitTime = Mathf.Max(0f, fromStateDuration - toStateDuration);
                newExitTime = Mathf.Clamp(newExitTime, minExitTime, fromStateDuration);

                if (Math.Abs(newExitTime - exitTime) <= ValueChangeEpsilon) return; 

                // Update exit time
                requestedExitTime = newExitTime;
                exitTime = newExitTime;

                // Update transition duration to match the new overlap
                // Overlap = fromStateDuration - exitTime
                float newTransitionDuration = fromStateDuration - exitTime;
                requestedTransitionDuration = Mathf.Max(MinTransitionDuration, newTransitionDuration);
                transitionDuration = Mathf.Clamp(requestedTransitionDuration, MinTransitionDuration, toStateDuration);

                RefreshLayout();
                OnExitTimeChanged?.Invoke(exitTime);
                // Fire event with the actual overlap duration (not clamped)
                // This is what gets stored in the asset
                OnTransitionDurationChanged?.Invoke(requestedTransitionDuration);
            }
        }
        
        #endregion
        
        #region Helpers
        
        private static bool IsBlendState(AnimationStateAsset state)
        {
            return AnimationStateUtils.IsBlendState(state);
        }
        
        private void RecalculateTransitionDuration()
        {
            if (fromStateDuration <= 0.001f || toStateDuration <= 0.001f)
            {
                transitionDuration = MinTransitionDuration;
                return;
            }
            
            // Calculate available overlap space
            float fromBarEnd = fromStateDuration;
            float toBarEnd = exitTime + toStateDuration;
            float maxPossibleTransition = Mathf.Max(0.01f, Mathf.Min(fromBarEnd, toBarEnd) - exitTime);
            
            // Try to maintain requested duration, but clamp to available space and bounds
            // Note: transitionDuration is also clamped to toStateDuration for logic
            // (TO ghost bars will show if requestedTransitionDuration > toStateDuration)
            transitionDuration = Mathf.Clamp(requestedTransitionDuration, MinTransitionDuration, 
                Mathf.Min(MaxTransitionDuration, Mathf.Min(maxPossibleTransition, toStateDuration)));
        }
        
        /// <summary>
        /// Refreshes timing calculations and layout. Call after any timing value changes.
        /// </summary>
        private void RefreshLayout()
        {
            RecalculateTimingResult();
            UpdateDuration();
            UpdateLayout();
        }
        
        private void UpdateDuration()
        {
            Duration = GetTotalTimelineDuration();
            UpdateLabels();
        }
        
        private void UpdateLabels()
        {
            if (timeLabel != null)
            {
                float totalDuration = GetTotalTimelineDuration();
                float currentSeconds = NormalizedTime * totalDuration;

                // Only update if values changed (avoid string allocations)
                if (Mathf.Abs(currentSeconds - cachedTimeSeconds) > 0.005f ||
                    Mathf.Abs(totalDuration - cachedTotalDuration) > 0.005f)
                {
                    cachedTimeSeconds = currentSeconds;
                    cachedTotalDuration = totalDuration;
                    timeLabel.text = $"{currentSeconds:F2}s / {totalDuration:F2}s";
                }
            }

            if (blendLabel == null) return;

            // Use the unified calculator's blend weight (already has curve applied)
            float blendWeight = CurrentSnapshot.BlendWeight;

            // Only update if value changed (avoid string allocations)
            if (Mathf.Abs(blendWeight - cachedBlendWeight) <= 0.005f) return; 
            
            cachedBlendWeight = blendWeight;
            blendLabel.text = $"Blend: {blendWeight:P0}";
        }

        private float GetTotalTimelineDuration()
        {
            // Total FROM bar end (all cycles)
            float fromBarEnd = FromVisualCycles * fromStateDuration;
            
            // Account for TO ghost bars (RIGHT of to-bar)
            float toGhostWidth = (ToVisualCycles - 1) * toStateDuration;
            
            // Calculate to-bar start position (same logic as UpdateLayout)
            float toBarStart;
            if (IsFromGhostDurationShrink)
            {
                toBarStart = requestedExitTime;
            }
            else
            {
                float fromGhostWidth = (FromVisualCycles - 1) * fromStateDuration;
                toBarStart = fromGhostWidth + exitTime;
            }
            
            float toBarEnd = toBarStart + toStateDuration + toGhostWidth;
            
            return Mathf.Max(fromBarEnd, toBarEnd) + 0.1f;
        }
        
        #endregion
    }
}
