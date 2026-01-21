using System;
using DMotion.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// A timeline control for transition preview with interactive editing.
    /// Shows full clip bars that can be positioned to create overlap (transition duration).
    /// 
    /// Visual model:
    /// - From State bar: Full clip duration, positioned at timeline start (top row)
    /// - To State bar: Full clip duration, can slide left/right (bottom row)
    /// - Overlap region: Drawn BETWEEN the bars showing the blend curve
    /// - Transition duration = overlap between the bars
    /// 
    /// Uses pure UIElements for consistent behavior with TimelineScrubber.
    /// </summary>
    [UxmlElement]
    internal partial class TransitionTimeline : TimelineBase
    {
        #region Constants
        
        private const float HandleWidth = 10f;
        private const float ScrubberWidth = 2f;
        private const float Padding = 12f;
        
        /// <summary>Epsilon for float comparisons to detect meaningful changes.</summary>
        private const float ValueChangeEpsilon = 0.0001f;
        
        #endregion
        
        #region Drag State
        
        private enum DragTarget
        {
            None,
            Scrubber,
            ExitTimeHandle,
            ToBar
        }
        
        private DragTarget currentDragTarget = DragTarget.None;
        private DragTarget hoveredTarget = DragTarget.None;
        private float dragStartValue;
        private float dragStartMouseX;
        
        #endregion
        
        #region State
        
        // Transition parameters
        private float exitTime = 0.75f;
        private float transitionDuration = 0.25f;
        private float transitionOffset;
        
        // Clip durations
        private float fromStateDuration = 1f;
        private float toStateDuration = 1f;
        
        // Display names
        private string fromStateName = "From State";
        private string toStateName = "To State";
        
        // Blend curve (default linear)
        private AnimationCurve blendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        
        #endregion
        
        #region UI Elements
        
        // Header
        private readonly Label fromStateLabel;
        private readonly Label toStateLabel;
        
        // Track area
        private readonly VisualElement trackArea;
        private readonly VisualElement fromBar;
        private readonly VisualElement fromBarOverlap;
        private readonly Label fromBarLabel;
        private readonly Label fromBarDuration;
        private readonly VisualElement exitTimeHandle;
        private readonly VisualElement toBar;
        private readonly VisualElement toBarOverlap;
        private readonly Label toBarLabel;
        private readonly Label toBarDuration;
        private readonly VisualElement overlapArea;
        private readonly VisualElement blendCurveElement;
        private readonly Label overlapDurationLabel;
        private readonly VisualElement scrubber;
        private readonly VisualElement timeGridContainer;
        
        // Footer
        private readonly Button playButton;
        private readonly Label timeLabel;
        private readonly Label blendLabel;
        
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
        /// The exit time (0-1 normalized) when the transition starts.
        /// </summary>
        public float ExitTime
        {
            get => exitTime;
            set
            {
                var newValue = Mathf.Clamp01(value);
                if (Math.Abs(newValue - exitTime) > ValueChangeEpsilon)
                {
                    exitTime = newValue;
                    RecalculateTransitionDuration();
                    UpdateDuration();
                    UpdateLayout();
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
                var newValue = Mathf.Max(0.01f, value);
                if (Math.Abs(newValue - transitionDuration) > ValueChangeEpsilon)
                {
                    transitionDuration = newValue;
                    UpdateDuration();
                    UpdateLayout();
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
                UpdateDuration();
                UpdateLayout();
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
                UpdateDuration();
                UpdateLayout();
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
                if (fromBarLabel != null) fromBarLabel.text = fromStateName;
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
                if (toBarLabel != null) toBarLabel.text = toStateName;
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
                blendCurveElement?.MarkDirtyRepaint();
            }
        }
        
        /// <summary>
        /// Gets the current transition progress (0 before exit, 0-1 during transition, 1 after).
        /// </summary>
        public float TransitionProgress
        {
            get
            {
                float totalDuration = GetTotalTimelineDuration();
                float exitTimeSeconds = exitTime * fromStateDuration;
                float currentSeconds = NormalizedTime * totalDuration;
                
                if (currentSeconds < exitTimeSeconds)
                    return 0f;
                
                if (transitionDuration <= 0.001f)
                    return currentSeconds >= exitTimeSeconds ? 1f : 0f;
                
                float progress = (currentSeconds - exitTimeSeconds) / transitionDuration;
                return Mathf.Clamp01(progress);
            }
        }
        
        #endregion
        
        #region Constructor
        
        public TransitionTimeline()
        {
            AddToClassList("transition-timeline");
            
            // Header
            var header = new VisualElement();
            header.AddToClassList("transition-timeline__header");
            
            fromStateLabel = new Label(fromStateName);
            fromStateLabel.AddToClassList("transition-timeline__state-label");
            fromStateLabel.AddToClassList("transition-timeline__state-label--from");
            header.Add(fromStateLabel);
            
            toStateLabel = new Label(toStateName);
            toStateLabel.AddToClassList("transition-timeline__state-label");
            toStateLabel.AddToClassList("transition-timeline__state-label--to");
            header.Add(toStateLabel);
            
            Add(header);
            
            // Track area (contains bars, overlap, scrubber)
            trackArea = new VisualElement();
            trackArea.AddToClassList("transition-timeline__track");
            
            // Time grid (background)
            timeGridContainer = new VisualElement();
            timeGridContainer.AddToClassList("transition-timeline__grid");
            trackArea.Add(timeGridContainer);
            
            // From bar
            fromBar = new VisualElement();
            fromBar.AddToClassList("transition-timeline__bar");
            fromBar.AddToClassList("transition-timeline__bar--from");
            
            fromBarOverlap = new VisualElement();
            fromBarOverlap.AddToClassList("transition-timeline__bar-overlap");
            fromBarOverlap.AddToClassList("transition-timeline__bar-overlap--from");
            fromBar.Add(fromBarOverlap);
            
            fromBarLabel = new Label(fromStateName);
            fromBarLabel.AddToClassList("transition-timeline__bar-label");
            fromBar.Add(fromBarLabel);
            
            fromBarDuration = new Label();
            fromBarDuration.AddToClassList("transition-timeline__bar-duration");
            fromBar.Add(fromBarDuration);
            
            exitTimeHandle = new VisualElement();
            exitTimeHandle.AddToClassList("transition-timeline__handle");
            exitTimeHandle.AddToClassList("transition-timeline__handle--exit-time");
            fromBar.Add(exitTimeHandle);
            
            trackArea.Add(fromBar);
            
            // To bar (added before overlap so overlap draws on top)
            toBar = new VisualElement();
            toBar.AddToClassList("transition-timeline__bar");
            toBar.AddToClassList("transition-timeline__bar--to");
            
            toBarOverlap = new VisualElement();
            toBarOverlap.AddToClassList("transition-timeline__bar-overlap");
            toBarOverlap.AddToClassList("transition-timeline__bar-overlap--to");
            toBar.Add(toBarOverlap);
            
            toBarLabel = new Label(toStateName);
            toBarLabel.AddToClassList("transition-timeline__bar-label");
            toBar.Add(toBarLabel);
            
            toBarDuration = new Label();
            toBarDuration.AddToClassList("transition-timeline__bar-duration");
            toBar.Add(toBarDuration);
            
            trackArea.Add(toBar);
            
            // Overlap area (between bars - added after bars so it draws on top)
            overlapArea = new VisualElement();
            overlapArea.AddToClassList("transition-timeline__overlap");
            overlapArea.generateVisualContent += DrawOverlapGradient;
            
            blendCurveElement = new VisualElement();
            blendCurveElement.AddToClassList("transition-timeline__curve");
            blendCurveElement.generateVisualContent += DrawBlendCurve;
            overlapArea.Add(blendCurveElement);
            
            overlapDurationLabel = new Label();
            overlapDurationLabel.AddToClassList("transition-timeline__overlap-label");
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
            spacer.style.flexGrow = 1;
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
            FromStateName = fromState?.name ?? "Any State";
            ToStateName = toState?.name ?? "To State";
            FromStateDuration = GetStateDuration(fromState);
            ToStateDuration = GetStateDuration(toState);
            this.exitTime = Mathf.Clamp01(exitTime);
            this.transitionDuration = Mathf.Max(0.01f, transitionDuration);
            this.transitionOffset = Mathf.Clamp01(transitionOffset);
            this.blendCurve = blendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
            
            UpdateDuration();
            UpdateLayout();
        }
        
        #endregion
        
        #region TimelineBase Overrides
        
        protected override Rect GetTrackRect()
        {
            return trackArea?.contentRect ?? Rect.zero;
        }
        
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
            if (playButton != null)
            {
                playButton.text = IsPlaying ? "\u275a\u275a" : "\u25b6";
                playButton.tooltip = IsPlaying ? "Pause" : "Play";
            }
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
            
            var fromColor = new Color(0.27f, 0.43f, 0.63f, 0.7f);  // Blue-ish
            var toColor = new Color(0.27f, 0.55f, 0.39f, 0.7f);    // Green-ish
            
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
            
            // Calculate positions
            float exitTimeSeconds = exitTime * fromStateDuration;
            float fromBarWidthPx = fromStateDuration * pixelsPerSecond;
            float toBarWidthPx = toStateDuration * pixelsPerSecond;
            float toBarLeftPx = Padding + exitTimeSeconds * pixelsPerSecond;
            
            // Overlap region
            float overlapStartPx = toBarLeftPx;
            float overlapEndPx = Mathf.Min(Padding + fromBarWidthPx, toBarLeftPx + toBarWidthPx);
            float overlapWidthPx = Mathf.Max(0, overlapEndPx - overlapStartPx);
            
            // From bar (always starts at padding)
            fromBar.style.left = Padding;
            fromBar.style.width = fromBarWidthPx;
            fromBarDuration.text = $"{fromStateDuration:F2}s";
            
            // From bar overlap highlight
            float fromOverlapStart = exitTimeSeconds * pixelsPerSecond;
            fromBarOverlap.style.left = fromOverlapStart;
            fromBarOverlap.style.width = fromBarWidthPx - fromOverlapStart;
            
            // Exit time handle position (relative to from bar)
            exitTimeHandle.style.left = fromOverlapStart - HandleWidth / 2;
            
            // Overlap area
            overlapArea.style.left = overlapStartPx;
            overlapArea.style.width = overlapWidthPx;
            overlapDurationLabel.text = $"{transitionDuration:F2}s";
            
            // To bar
            toBar.style.left = toBarLeftPx;
            toBar.style.width = toBarWidthPx;
            toBarDuration.text = $"{toStateDuration:F2}s";
            
            // To bar overlap highlight
            toBarOverlap.style.width = overlapWidthPx;
            
            // Update scrubber
            UpdateScrubberPosition();
            
            // Update time grid
            UpdateTimeGrid(trackWidth, totalDuration, pixelsPerSecond);
            
            // Trigger curve redraw
            blendCurveElement?.MarkDirtyRepaint();
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
            float totalDuration = GetTotalTimelineDuration();
            float pixelsPerSecond = trackWidth / totalDuration;
            
            // Check exit time handle (highest priority)
            float exitTimeX = Padding + exitTime * fromStateDuration * pixelsPerSecond;
            if (IsNearX(localPos.x, exitTimeX) && IsInBarVerticalRange(localPos.y, fromBar))
            {
                StartDrag(DragTarget.ExitTimeHandle, exitTime, localPos.x, evt.pointerId);
                evt.StopPropagation();
                return;
            }
            
            // Check To bar drag
            if (IsOverElement(localPos, toBar))
            {
                StartDrag(DragTarget.ToBar, exitTime, localPos.x, evt.pointerId);
                evt.StopPropagation();
                return;
            }
            
            // Default: scrub timeline
            StartDrag(DragTarget.Scrubber, NormalizedTime, localPos.x, evt.pointerId);
            float normalizedX = Mathf.Clamp01((localPos.x - Padding) / trackWidth);
            NormalizedTime = normalizedX;
            evt.StopPropagation();
        }
        
        private void OnTrackPointerMove(PointerMoveEvent evt)
        {
            var localPos = evt.localPosition;
            
            if (currentDragTarget != DragTarget.None)
            {
                HandleDrag(localPos.x);
                evt.StopPropagation();
                return;
            }
            
            // Update hover state
            UpdateHoverState(localPos);
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
            if (hoveredTarget != DragTarget.None)
            {
                hoveredTarget = DragTarget.None;
                UpdateHoverVisuals();
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
            currentDragTarget = DragTarget.None;
            trackArea.ReleasePointer(pointerId);
        }
        
        private void HandleDrag(float mouseX)
        {
            float trackWidth = trackArea.resolvedStyle.width - Padding * 2;
            float totalDuration = GetTotalTimelineDuration();
            float pixelsPerSecond = trackWidth / totalDuration;
            float deltaX = mouseX - dragStartMouseX;
            
            switch (currentDragTarget)
            {
                case DragTarget.Scrubber:
                    float normalizedX = Mathf.Clamp01((mouseX - Padding) / trackWidth);
                    NormalizedTime = normalizedX;
                    break;
                    
                case DragTarget.ExitTimeHandle:
                case DragTarget.ToBar:
                    float deltaSeconds = deltaX / pixelsPerSecond;
                    float newExitTimeSeconds = dragStartValue * fromStateDuration + deltaSeconds;
                    float newExitTime = Mathf.Clamp(newExitTimeSeconds / fromStateDuration, 0.05f, 0.95f);
                    
                    if (Math.Abs(newExitTime - exitTime) > ValueChangeEpsilon)
                    {
                        exitTime = newExitTime;
                        RecalculateTransitionDuration();
                        UpdateDuration();
                        UpdateLayout();
                        OnExitTimeChanged?.Invoke(exitTime);
                        OnTransitionDurationChanged?.Invoke(transitionDuration);
                    }
                    break;
            }
        }
        
        private void UpdateHoverState(Vector2 localPos)
        {
            var previousHover = hoveredTarget;
            hoveredTarget = DragTarget.None;
            
            float trackWidth = trackArea.resolvedStyle.width - Padding * 2;
            float totalDuration = GetTotalTimelineDuration();
            float pixelsPerSecond = trackWidth / totalDuration;
            float exitTimeX = Padding + exitTime * fromStateDuration * pixelsPerSecond;
            
            if (IsNearX(localPos.x, exitTimeX) && IsInBarVerticalRange(localPos.y, fromBar))
            {
                hoveredTarget = DragTarget.ExitTimeHandle;
            }
            else if (IsOverElement(localPos, toBar))
            {
                hoveredTarget = DragTarget.ToBar;
            }
            
            if (hoveredTarget != previousHover)
            {
                UpdateHoverVisuals();
            }
        }
        
        private void UpdateHoverVisuals()
        {
            // Update handle hover state
            exitTimeHandle.EnableInClassList("transition-timeline__handle--hover", 
                hoveredTarget == DragTarget.ExitTimeHandle || currentDragTarget == DragTarget.ExitTimeHandle);
            
            // Update To bar hover state
            toBar.EnableInClassList("transition-timeline__bar--hover", 
                hoveredTarget == DragTarget.ToBar || currentDragTarget == DragTarget.ToBar);
        }
        
        private bool IsNearX(float mouseX, float targetX) => Mathf.Abs(mouseX - targetX) <= 10f;
        
        private bool IsInBarVerticalRange(float mouseY, VisualElement bar)
        {
            var barRect = bar.worldBound;
            var trackRect = trackArea.worldBound;
            float localTop = barRect.y - trackRect.y;
            float localBottom = localTop + barRect.height;
            return mouseY >= localTop - 5 && mouseY <= localBottom + 5;
        }
        
        private bool IsOverElement(Vector2 localPos, VisualElement element)
        {
            var rect = element.worldBound;
            var trackRect = trackArea.worldBound;
            var localRect = new Rect(
                rect.x - trackRect.x,
                rect.y - trackRect.y,
                rect.width,
                rect.height
            );
            return localRect.Contains(localPos);
        }
        
        #endregion
        
        #region Helpers
        
        private void RecalculateTransitionDuration()
        {
            if (fromStateDuration <= 0.001f || toStateDuration <= 0.001f)
            {
                transitionDuration = 0.01f;
                return;
            }
            
            float exitTimeSeconds = exitTime * fromStateDuration;
            float fromBarEnd = fromStateDuration;
            float toBarEnd = exitTimeSeconds + toStateDuration;
            
            transitionDuration = Mathf.Max(0.01f, Mathf.Min(fromBarEnd, toBarEnd) - exitTimeSeconds);
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
                timeLabel.text = $"{currentSeconds:F2}s / {totalDuration:F2}s";
            }
            
            if (blendLabel != null)
            {
                blendLabel.text = $"Blend: {TransitionProgress:P0}";
            }
        }
        
        private float GetTotalTimelineDuration()
        {
            float exitTimeSeconds = exitTime * fromStateDuration;
            float toBarEnd = exitTimeSeconds + toStateDuration;
            return Mathf.Max(fromStateDuration, toBarEnd) + 0.1f;
        }
        
        private static float GetStateDuration(AnimationStateAsset state)
        {
            if (state == null) return 1f;
            
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    var clip = singleClip.Clip?.Clip;
                    return clip != null ? clip.length : 1f;
                    
                case LinearBlendStateAsset linearBlend:
                    return GetMaxClipDuration(linearBlend.BlendClips);
                    
                case Directional2DBlendStateAsset blend2D:
                    return GetMaxClipDuration(blend2D.BlendClips);
                    
                default:
                    return 1f;
            }
        }
        
        private static float GetMaxClipDuration(ClipWithThreshold[] clips)
        {
            if (clips == null || clips.Length == 0) return 1f;
            
            float maxDuration = 0f;
            foreach (var blendClip in clips)
            {
                var clip = blendClip.Clip?.Clip;
                if (clip != null)
                    maxDuration = Mathf.Max(maxDuration, clip.length);
            }
            return maxDuration > 0 ? maxDuration : 1f;
        }
        
        private static float GetMaxClipDuration(Directional2DClipWithPosition[] clips)
        {
            if (clips == null || clips.Length == 0) return 1f;
            
            float maxDuration = 0f;
            foreach (var blendClip in clips)
            {
                var clip = blendClip.Clip?.Clip;
                if (clip != null)
                    maxDuration = Mathf.Max(maxDuration, clip.length);
            }
            return maxDuration > 0 ? maxDuration : 1f;
        }
        
        #endregion
    }
}
