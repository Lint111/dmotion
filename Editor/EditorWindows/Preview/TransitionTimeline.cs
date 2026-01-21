using System;
using DMotion.Authoring;
using UnityEditor;
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
    /// A timeline control for transition preview with interactive editing.
    /// Shows full clip bars that can be positioned to create overlap (transition duration).
    /// 
    /// Visual model:
    /// - From State bar: Full clip duration, positioned at timeline start (top row)
    /// - To State bar: Full clip duration, can slide left/right (bottom row)
    /// - Overlap region: Drawn BETWEEN the bars showing the blend curve
    /// - Transition duration = overlap between the bars
    /// </summary>
    [UxmlElement]
    internal partial class TransitionTimeline : TimelineBase
    {
        #region Constants
        
        private const float BarHeight = 26f;
        private const float OverlapAreaHeight = 40f;  // Space between bars for curve
        private const float HeaderHeight = 22f;
        private const float FooterHeight = 32f;
        private const float BarHeight = 26f;
        private const float OverlapAreaHeight = 40f;  // Space between bars for curve
        private const float HeaderHeight = 22f;
        private const float FooterHeight = 32f;
        private const float ScrubberWidth = 2f;
        private const float HandleWidth = 10f;
        private const float DragHitRadius = 10f;
        private const float Padding = 12f;
        
        /// <summary>Epsilon for float comparisons to detect meaningful changes.</summary>
        private const float ValueChangeEpsilon = 0.0001f;
        private const float HandleWidth = 10f;
        private const float DragHitRadius = 10f;
        private const float Padding = 12f;
        
        /// <summary>Epsilon for float comparisons to detect meaningful changes.</summary>
        private const float ValueChangeEpsilon = 0.0001f;
        
        #endregion
        
        #region Colors
        
        // Use shared colors where possible
        private static Color BackgroundColor => PreviewEditorColors.MediumBackground;
        private static Color FromStateColor => PreviewEditorColors.FromState;
        private static Color ToStateColor => PreviewEditorColors.ToState;
        private static Color FromStateHighlightColor => PreviewEditorColors.FromStateHighlight;
        private static Color ToStateHighlightColor => PreviewEditorColors.ToStateHighlight;
        private static Color ScrubberColor => PreviewEditorColors.Scrubber;
        private static Color CurveColor => PreviewEditorColors.CurveAccent;
        private static Color TextColor => PreviewEditorColors.WhiteText;
        private static Color DimTextColor => PreviewEditorColors.DimText;
        private static Color GridColor => PreviewEditorColors.Grid;
        
        // Timeline-specific colors (not shared)
        private static readonly Color FromStateOverlapColor = new(0.35f, 0.55f, 0.85f, 0.6f);
        private static readonly Color ToStateOverlapColor = new(0.35f, 0.75f, 0.55f, 0.6f);
        private static readonly Color OverlapBgColor = new(0.25f, 0.25f, 0.2f, 1f);
        private static readonly Color HandleColor = new(0.9f, 0.9f, 0.9f, 0.8f);
        private static readonly Color HandleHoverColor = new(1f, 0.7f, 0.3f, 1f);
        
        #endregion
        
        #region Drag State
        
        private enum DragMode
        {
            None,
            Scrubber,
            FromBarEnd,     // Drag right edge of From bar (changes overlap start)
            ToBar           // Drag To bar body (changes where To bar starts = overlap)
        }
        
        private DragMode currentDragMode = DragMode.None;
        private DragMode hoveredElement = DragMode.None;
        private float dragStartValue;
        private float dragStartMouseX;
        
        // Cached positions for hit detection (updated each draw)
        private Rect cachedFromBarRect;
        private Rect cachedToBarRect;
        private Rect cachedOverlapAreaRect;
        private float cachedFromBarEndX;
        private float cachedToBarStartX;
        private float cachedOverlapStartX;
        private float cachedOverlapEndX;
        // Use shared colors where possible
        private static Color BackgroundColor => PreviewEditorColors.MediumBackground;
        private static Color FromStateColor => PreviewEditorColors.FromState;
        private static Color ToStateColor => PreviewEditorColors.ToState;
        private static Color FromStateHighlightColor => PreviewEditorColors.FromStateHighlight;
        private static Color ToStateHighlightColor => PreviewEditorColors.ToStateHighlight;
        private static Color ScrubberColor => PreviewEditorColors.Scrubber;
        private static Color CurveColor => PreviewEditorColors.CurveAccent;
        private static Color TextColor => PreviewEditorColors.WhiteText;
        private static Color DimTextColor => PreviewEditorColors.DimText;
        private static Color GridColor => PreviewEditorColors.Grid;
        
        // Timeline-specific colors (not shared)
        private static readonly Color FromStateOverlapColor = new(0.35f, 0.55f, 0.85f, 0.6f);
        private static readonly Color ToStateOverlapColor = new(0.35f, 0.75f, 0.55f, 0.6f);
        private static readonly Color OverlapBgColor = new(0.25f, 0.25f, 0.2f, 1f);
        private static readonly Color HandleColor = new(0.9f, 0.9f, 0.9f, 0.8f);
        private static readonly Color HandleHoverColor = new(1f, 0.7f, 0.3f, 1f);
        
        #endregion
        
        #region Drag State
        
        private enum DragMode
        {
            None,
            Scrubber,
            FromBarEnd,     // Drag right edge of From bar (changes overlap start)
            ToBar           // Drag To bar body (changes where To bar starts = overlap)
        }
        
        private DragMode currentDragMode = DragMode.None;
        private DragMode hoveredElement = DragMode.None;
        private float dragStartValue;
        private float dragStartMouseX;
        
        // Cached positions for hit detection (updated each draw)
        private Rect cachedFromBarRect;
        private Rect cachedToBarRect;
        private Rect cachedOverlapAreaRect;
        private float cachedFromBarEndX;
        private float cachedToBarStartX;
        private float cachedOverlapStartX;
        private float cachedOverlapEndX;
        
        #endregion
        
        #region State
        
        // Transition parameters
        private float exitTime = 0.75f;           // Normalized (0-1) in From state when transition starts
        private float transitionDuration = 0.25f; // Seconds - calculated from overlap
        private float transitionOffset;           // Normalized (0-1) in To state where playback starts
        
        // Clip durations
        // Transition parameters
        private float exitTime = 0.75f;           // Normalized (0-1) in From state when transition starts
        private float transitionDuration = 0.25f; // Seconds - calculated from overlap
        private float transitionOffset;           // Normalized (0-1) in To state where playback starts
        
        // Clip durations
        private float fromStateDuration = 1f;
        private float toStateDuration = 1f;
        
        // Display names
        // Display names
        private string fromStateName = "From State";
        private string toStateName = "To State";
        
        // Blend curve (default linear)
        private AnimationCurve blendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        
        // Blend curve (default linear)
        private AnimationCurve blendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        
        #endregion
        
        #region UI Elements
        
        private readonly VisualElement header;
        private readonly Label fromStateLabel;
        private readonly Label toStateLabel;
        private readonly VisualElement trackArea;
        private readonly IMGUIContainer drawingContainer;
        private readonly VisualElement footer;
        private readonly Button playButton;
        private readonly Label timeLabel;
        private readonly Label blendLabel;
        
        #endregion
        
        #region Cached Drawing Resources
        
        // Cached to avoid per-frame allocation
        private const int CurveSegments = 30;
        // Instance-based to avoid thread safety issues with multiple timelines
        private readonly Vector3[] cachedCurvePoints = new Vector3[CurveSegments + 1];
        private static GUIStyle cachedMiniLabelStyle;
        private static GUIStyle cachedBoldLabelStyle;
        private static GUIStyle cachedFromLabelStyle;
        private static GUIStyle cachedToLabelStyle;
        private static GUIStyle cachedBarLabelStyle;
        private static GUIStyle cachedBarDurationStyle;
        
        #endregion
        
        #region Cached Drawing Resources
        
        // Cached to avoid per-frame allocation
        private const int CurveSegments = 30;
        // Instance-based to avoid thread safety issues with multiple timelines
        private readonly Vector3[] cachedCurvePoints = new Vector3[CurveSegments + 1];
        private static GUIStyle cachedMiniLabelStyle;
        private static GUIStyle cachedBoldLabelStyle;
        private static GUIStyle cachedFromLabelStyle;
        private static GUIStyle cachedToLabelStyle;
        private static GUIStyle cachedBarLabelStyle;
        private static GUIStyle cachedBarDurationStyle;
        
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
        /// Note: Currently not used as offset isn't editable via UI, but subscribed to for future use.
        /// </summary>
#pragma warning disable CS0067 // Event is never used
        public event Action<float> OnTransitionOffsetChanged;
#pragma warning restore CS0067
        
        /// <summary>
        /// Fired when transition offset is changed via dragging.
        /// Note: Currently not used as offset isn't editable via UI, but subscribed to for future use.
        /// </summary>
#pragma warning disable CS0067 // Event is never used
        public event Action<float> OnTransitionOffsetChanged;
#pragma warning restore CS0067
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether the user is currently dragging a timeline element.
        /// </summary>
        public new bool IsDragging => currentDragMode != DragMode.None;
        
        /// <summary>
        /// Whether the user is currently dragging a timeline element.
        /// </summary>
        public new bool IsDragging => currentDragMode != DragMode.None;
        
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
                    MarkDirtyRepaint();
                }
                var newValue = Mathf.Clamp01(value);
                if (Math.Abs(newValue - exitTime) > ValueChangeEpsilon)
                {
                    exitTime = newValue;
                    RecalculateTransitionDuration();
                    UpdateDuration();
                    MarkDirtyRepaint();
                }
            }
        }
        
        /// <summary>
        /// The transition duration in seconds (read from overlap, can be set to adjust To bar position).
        /// The transition duration in seconds (read from overlap, can be set to adjust To bar position).
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
                    MarkDirtyRepaint();
                }
                var newValue = Mathf.Max(0.01f, value);
                if (Math.Abs(newValue - transitionDuration) > ValueChangeEpsilon)
                {
                    transitionDuration = newValue;
                    UpdateDuration();
                    MarkDirtyRepaint();
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
                RecalculateTransitionDuration();
                UpdateDuration();
                MarkDirtyRepaint();
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
                RecalculateTransitionDuration();
                UpdateDuration();
                MarkDirtyRepaint();
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
                    MarkDirtyRepaint();
                }
                var newValue = Mathf.Clamp01(value);
                if (Math.Abs(newValue - transitionOffset) > ValueChangeEpsilon)
                {
                    transitionOffset = newValue;
                    MarkDirtyRepaint();
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
                if (fromStateLabel != null)
                    fromStateLabel.text = fromStateName;
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
                if (toStateLabel != null)
                    toStateLabel.text = toStateName;
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
                MarkDirtyRepaint();
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
                MarkDirtyRepaint();
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
            style.minHeight = HeaderHeight + BarHeight * 2 + OverlapAreaHeight + FooterHeight + 20;
            style.minHeight = HeaderHeight + BarHeight * 2 + OverlapAreaHeight + FooterHeight + 20;
            
            // Header with state names
            header = new VisualElement();
            header.AddToClassList("transition-timeline-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.height = HeaderHeight;
            header.style.paddingLeft = 4;
            header.style.paddingRight = 4;
            
            fromStateLabel = new Label(fromStateName);
            fromStateLabel.style.color = FromStateColor;
            fromStateLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(fromStateLabel);
            
            toStateLabel = new Label(toStateName);
            toStateLabel.style.color = ToStateColor;
            toStateLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(toStateLabel);
            
            Add(header);
            
            // Track area (IMGUI for custom drawing)
            trackArea = new VisualElement();
            trackArea.AddToClassList("transition-timeline-track");
            trackArea.style.flexGrow = 1;
            trackArea.style.minHeight = BarHeight * 2 + OverlapAreaHeight + 20;
            trackArea.style.minHeight = BarHeight * 2 + OverlapAreaHeight + 20;
            
            drawingContainer = new IMGUIContainer(OnDrawTimeline);
            drawingContainer.style.flexGrow = 1;
            trackArea.Add(drawingContainer);
            
            Add(trackArea);
            
            // Footer with controls
            footer = new VisualElement();
            footer.AddToClassList("transition-timeline-footer");
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.height = FooterHeight;
            footer.style.paddingLeft = 4;
            footer.style.paddingRight = 4;
            footer.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            footer.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            
            playButton = new Button(TogglePlayPause);
            playButton.text = "\u25b6";
            playButton.style.width = 28;
            playButton.style.height = 22;
            playButton.style.height = 22;
            footer.Add(playButton);
            
            timeLabel = new Label("0.00s / 0.00s");
            timeLabel.style.marginLeft = 8;
            timeLabel.style.color = TextColor;
            footer.Add(timeLabel);
            
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            footer.Add(spacer);
            
            blendLabel = new Label("Blend: 0%");
            blendLabel.style.color = ScrubberColor;
            blendLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            footer.Add(blendLabel);
            
            Add(footer);
            
            // Initialize duration
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
            this.blendCurve = blendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
            
            UpdateDuration();
            MarkDirtyRepaint();
        }
        
        #endregion
        
        #region TimelineBase Overrides
        
        protected override Rect GetTrackRect()
        {
            if (drawingContainer == null) return Rect.zero;
            return new Rect(0, 0, drawingContainer.contentRect.width, drawingContainer.contentRect.height);
        }
        
        protected override void OnCurrentTimeChanged()
        {
            base.OnCurrentTimeChanged();
            UpdateLabels();
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
        
        #endregion
        
        #region Private - Drawing
        
        private void OnDrawTimeline()
        {
            var rect = drawingContainer.contentRect;
            if (rect.width < 50 || rect.height < 50) return;
            
            // Background
            EditorGUI.DrawRect(rect, BackgroundColor);
            
            // Calculate timeline metrics
            float totalDuration = GetTotalTimelineDuration();
            float trackWidth = rect.width - Padding * 2;
            float pixelsPerSecond = trackWidth / totalDuration;
            
            // Calculate positions in seconds
            float exitTimeSeconds = exitTime * fromStateDuration;
            float toBarStartSeconds = exitTimeSeconds;  // To bar starts at exit time
            float fromBarEndSeconds = fromStateDuration;
            float toBarEndSeconds = toBarStartSeconds + toStateDuration;
            
            // Calculate overlap (intersection of both bars playing)
            float overlapStartSeconds = exitTimeSeconds;
            float overlapEndSeconds = Mathf.Min(fromBarEndSeconds, toBarEndSeconds);
            float currentOverlapDuration = Mathf.Max(0, overlapEndSeconds - overlapStartSeconds);
            
            // Update transition duration based on actual overlap
            transitionDuration = currentOverlapDuration;
            
            // Layout: From bar (top), Overlap area (middle), To bar (bottom)
            float fromBarY = Padding;
            float overlapAreaY = fromBarY + BarHeight;
            float toBarY = overlapAreaY + OverlapAreaHeight;
            
            // From bar rect (full duration)
            cachedFromBarRect = new Rect(
                Padding, 
                fromBarY, 
                fromStateDuration * pixelsPerSecond, 
                BarHeight);
            
            // To bar rect (positioned at exit time)
            cachedToBarRect = new Rect(
                Padding + toBarStartSeconds * pixelsPerSecond,
                toBarY,
                toStateDuration * pixelsPerSecond,
                BarHeight);
            
            // Overlap area rect (between the bars)
            cachedOverlapStartX = Padding + overlapStartSeconds * pixelsPerSecond;
            cachedOverlapEndX = Padding + overlapEndSeconds * pixelsPerSecond;
            cachedOverlapAreaRect = new Rect(
                cachedOverlapStartX,
                overlapAreaY,
                Mathf.Max(0, cachedOverlapEndX - cachedOverlapStartX),
                OverlapAreaHeight);
            
            // Cache for hit detection
            cachedFromBarEndX = Padding + fromBarEndSeconds * pixelsPerSecond;
            cachedToBarStartX = cachedToBarRect.x;
            
            // Draw time grid
            DrawTimeGrid(rect, pixelsPerSecond, totalDuration);
            
            // Draw the overlap area (between bars) with curve
            DrawOverlapArea(overlapStartSeconds, overlapEndSeconds, pixelsPerSecond);
            // Calculate timeline metrics
            float totalDuration = GetTotalTimelineDuration();
            float trackWidth = rect.width - Padding * 2;
            float pixelsPerSecond = trackWidth / totalDuration;
            
            // Calculate positions in seconds
            float exitTimeSeconds = exitTime * fromStateDuration;
            float toBarStartSeconds = exitTimeSeconds;  // To bar starts at exit time
            float fromBarEndSeconds = fromStateDuration;
            float toBarEndSeconds = toBarStartSeconds + toStateDuration;
            
            // Calculate overlap (intersection of both bars playing)
            float overlapStartSeconds = exitTimeSeconds;
            float overlapEndSeconds = Mathf.Min(fromBarEndSeconds, toBarEndSeconds);
            float currentOverlapDuration = Mathf.Max(0, overlapEndSeconds - overlapStartSeconds);
            
            // Update transition duration based on actual overlap
            transitionDuration = currentOverlapDuration;
            
            // Layout: From bar (top), Overlap area (middle), To bar (bottom)
            float fromBarY = Padding;
            float overlapAreaY = fromBarY + BarHeight;
            float toBarY = overlapAreaY + OverlapAreaHeight;
            
            // From bar rect (full duration)
            cachedFromBarRect = new Rect(
                Padding, 
                fromBarY, 
                fromStateDuration * pixelsPerSecond, 
                BarHeight);
            
            // To bar rect (positioned at exit time)
            cachedToBarRect = new Rect(
                Padding + toBarStartSeconds * pixelsPerSecond,
                toBarY,
                toStateDuration * pixelsPerSecond,
                BarHeight);
            
            // Overlap area rect (between the bars)
            cachedOverlapStartX = Padding + overlapStartSeconds * pixelsPerSecond;
            cachedOverlapEndX = Padding + overlapEndSeconds * pixelsPerSecond;
            cachedOverlapAreaRect = new Rect(
                cachedOverlapStartX,
                overlapAreaY,
                Mathf.Max(0, cachedOverlapEndX - cachedOverlapStartX),
                OverlapAreaHeight);
            
            // Cache for hit detection
            cachedFromBarEndX = Padding + fromBarEndSeconds * pixelsPerSecond;
            cachedToBarStartX = cachedToBarRect.x;
            
            // Draw time grid
            DrawTimeGrid(rect, pixelsPerSecond, totalDuration);
            
            // Draw the overlap area (between bars) with curve
            DrawOverlapArea(overlapStartSeconds, overlapEndSeconds, pixelsPerSecond);
            
            // Draw state bars
            DrawFromStateBar(exitTimeSeconds, pixelsPerSecond);
            DrawToStateBar(overlapStartSeconds, overlapEndSeconds, pixelsPerSecond);
            DrawFromStateBar(exitTimeSeconds, pixelsPerSecond);
            DrawToStateBar(overlapStartSeconds, overlapEndSeconds, pixelsPerSecond);
            
            // Draw connection lines from bars to overlap area
            DrawConnectionLines(exitTimeSeconds, overlapEndSeconds, pixelsPerSecond);
            // Draw connection lines from bars to overlap area
            DrawConnectionLines(exitTimeSeconds, overlapEndSeconds, pixelsPerSecond);
            
            // Draw scrubber
            DrawScrubber(rect, pixelsPerSecond);
            DrawScrubber(rect, pixelsPerSecond);
            
            // Handle input
            HandleIMGUIInput(rect);
            // Handle input
            HandleIMGUIInput(rect);
        }
        
        private void DrawTimeGrid(Rect rect, float pixelsPerSecond, float totalDuration)
        private void DrawTimeGrid(Rect rect, float pixelsPerSecond, float totalDuration)
        {
            float interval = 0.5f;
            if (pixelsPerSecond * interval < 30) interval = 1f;
            if (pixelsPerSecond * interval < 30) interval = 2f;
            
            // Cache style outside loop
            cachedMiniLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = DimTextColor },
                alignment = TextAnchor.UpperCenter
            };
            
            Handles.BeginGUI();
            Handles.color = GridColor;
            
            for (float t = 0; t <= totalDuration; t += interval)
            {
                float x = Padding + t * pixelsPerSecond;
                Handles.DrawLine(
                    new Vector3(x, 0),
                    new Vector3(x, rect.height));
                
                GUI.Label(new Rect(x - 20, rect.height - 16, 40, 14), $"{t:F1}s", cachedMiniLabelStyle);
            }
            
            Handles.EndGUI();
        }
        
        private void DrawOverlapArea(float overlapStart, float overlapEnd, float pixelsPerSecond)
        {
            if (cachedOverlapAreaRect.width <= 2) return;
            
            // Draw gradient background based on blend curve
            // Each vertical strip is colored by interpolating From/To colors according to the curve
            DrawBlendGradient(cachedOverlapAreaRect);
            
            // Draw border
            var borderColor = new Color(0.5f, 0.5f, 0.3f, 0.8f);
            // Top border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.x, cachedOverlapAreaRect.y, cachedOverlapAreaRect.width, 1), borderColor);
            // Bottom border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.x, cachedOverlapAreaRect.yMax - 1, cachedOverlapAreaRect.width, 1), borderColor);
            // Left border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.x, cachedOverlapAreaRect.y, 1, cachedOverlapAreaRect.height), borderColor);
            // Right border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.xMax - 1, cachedOverlapAreaRect.y, 1, cachedOverlapAreaRect.height), borderColor);
            
            // Draw blend curve on top
            DrawBlendCurve(cachedOverlapAreaRect);
            
            // Draw duration label - use cached style
            cachedBoldLabelStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = TextColor },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            
            string durationText = $"{transitionDuration:F2}s";
            GUI.Label(cachedOverlapAreaRect, durationText, cachedBoldLabelStyle);
        }
        
        private void DrawBlendGradient(Rect rect)
        {
            if (blendCurve == null || blendCurve.length < 2)
            float interval = 0.5f;
            if (pixelsPerSecond * interval < 30) interval = 1f;
            if (pixelsPerSecond * interval < 30) interval = 2f;
            
            // Cache style outside loop
            cachedMiniLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = DimTextColor },
                alignment = TextAnchor.UpperCenter
            };
            
            Handles.BeginGUI();
            Handles.color = GridColor;
            
            for (float t = 0; t <= totalDuration; t += interval)
            {
                float x = Padding + t * pixelsPerSecond;
                Handles.DrawLine(
                    new Vector3(x, 0),
                    new Vector3(x, rect.height));
                
                GUI.Label(new Rect(x - 20, rect.height - 16, 40, 14), $"{t:F1}s", cachedMiniLabelStyle);
            }
            
            Handles.EndGUI();
        }
        
        private void DrawOverlapArea(float overlapStart, float overlapEnd, float pixelsPerSecond)
        {
            if (cachedOverlapAreaRect.width <= 2) return;
            
            // Draw gradient background based on blend curve
            // Each vertical strip is colored by interpolating From/To colors according to the curve
            DrawBlendGradient(cachedOverlapAreaRect);
            
            // Draw border
            var borderColor = new Color(0.5f, 0.5f, 0.3f, 0.8f);
            // Top border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.x, cachedOverlapAreaRect.y, cachedOverlapAreaRect.width, 1), borderColor);
            // Bottom border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.x, cachedOverlapAreaRect.yMax - 1, cachedOverlapAreaRect.width, 1), borderColor);
            // Left border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.x, cachedOverlapAreaRect.y, 1, cachedOverlapAreaRect.height), borderColor);
            // Right border
            EditorGUI.DrawRect(new Rect(cachedOverlapAreaRect.xMax - 1, cachedOverlapAreaRect.y, 1, cachedOverlapAreaRect.height), borderColor);
            
            // Draw blend curve on top
            DrawBlendCurve(cachedOverlapAreaRect);
            
            // Draw duration label - use cached style
            cachedBoldLabelStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = TextColor },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            
            string durationText = $"{transitionDuration:F2}s";
            GUI.Label(cachedOverlapAreaRect, durationText, cachedBoldLabelStyle);
        }
        
        private void DrawBlendGradient(Rect rect)
        {
            if (blendCurve == null || blendCurve.length < 2)
            {
                // Fallback to static color if no curve
                EditorGUI.DrawRect(rect, OverlapBgColor);
                return;
            }
            
            // Draw vertical strips with interpolated color based on blend curve
            // Fewer strips for performance, but enough for smooth appearance
            int strips = Mathf.Min(40, Mathf.Max(10, (int)rect.width / 4));
            float stripWidth = rect.width / strips;
            
            for (int i = 0; i < strips; i++)
            {
                float t = (i + 0.5f) / strips; // Sample at strip center
                float blendValue = blendCurve.Evaluate(t); // 1 = From, 0 = To
                
                // Interpolate between From color and To color
                // blendValue 1 = full From (blue), blendValue 0 = full To (green)
                Color stripColor = Color.Lerp(ToStateColor, FromStateColor, blendValue);
                // Darken slightly for the overlap area aesthetic
                stripColor = Color.Lerp(stripColor, OverlapBgColor, 0.3f);
                
                var stripRect = new Rect(
                    rect.x + i * stripWidth,
                    rect.y,
                    stripWidth + 1, // +1 to avoid gaps between strips
                    rect.height);
                
                EditorGUI.DrawRect(stripRect, stripColor);
            }
        }
        
        private void DrawBlendCurve(Rect overlapRect)
        {
            if (blendCurve == null || blendCurve.length < 2) return;
            if (overlapRect.width < 10 || overlapRect.height < 10) return;
            
                // Fallback to static color if no curve
                EditorGUI.DrawRect(rect, OverlapBgColor);
                return;
            }
            
            // Draw vertical strips with interpolated color based on blend curve
            // Fewer strips for performance, but enough for smooth appearance
            int strips = Mathf.Min(40, Mathf.Max(10, (int)rect.width / 4));
            float stripWidth = rect.width / strips;
            
            for (int i = 0; i < strips; i++)
            {
                float t = (i + 0.5f) / strips; // Sample at strip center
                float blendValue = blendCurve.Evaluate(t); // 1 = From, 0 = To
                
                // Interpolate between From color and To color
                // blendValue 1 = full From (blue), blendValue 0 = full To (green)
                Color stripColor = Color.Lerp(ToStateColor, FromStateColor, blendValue);
                // Darken slightly for the overlap area aesthetic
                stripColor = Color.Lerp(stripColor, OverlapBgColor, 0.3f);
                
                var stripRect = new Rect(
                    rect.x + i * stripWidth,
                    rect.y,
                    stripWidth + 1, // +1 to avoid gaps between strips
                    rect.height);
                
                EditorGUI.DrawRect(stripRect, stripColor);
            }
        }
        
        private void DrawBlendCurve(Rect overlapRect)
        {
            if (blendCurve == null || blendCurve.length < 2) return;
            if (overlapRect.width < 10 || overlapRect.height < 10) return;
            
            Handles.BeginGUI();
            Handles.color = CurveColor;
            Handles.color = CurveColor;
            
            // Add padding inside the overlap area
            float curvePadding = 4f;
            float curveWidth = overlapRect.width - curvePadding * 2;
            float curveHeight = overlapRect.height - curvePadding * 2;
            
            // Use cached array to avoid per-frame allocation
            for (int i = 0; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float curveValue = blendCurve.Evaluate(t);
                
                float x = overlapRect.x + curvePadding + t * curveWidth;
                // Y: curveValue 1 = top (From state), curveValue 0 = bottom (To state)
                float y = overlapRect.y + curvePadding + (1f - curveValue) * curveHeight;
                
                cachedCurvePoints[i] = new Vector3(x, y, 0);
            }
            
            // Draw curve line with thickness
            Handles.DrawAAPolyLine(3f, cachedCurvePoints);
            // Add padding inside the overlap area
            float curvePadding = 4f;
            float curveWidth = overlapRect.width - curvePadding * 2;
            float curveHeight = overlapRect.height - curvePadding * 2;
            
            // Use cached array to avoid per-frame allocation
            for (int i = 0; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float curveValue = blendCurve.Evaluate(t);
                
                float x = overlapRect.x + curvePadding + t * curveWidth;
                // Y: curveValue 1 = top (From state), curveValue 0 = bottom (To state)
                float y = overlapRect.y + curvePadding + (1f - curveValue) * curveHeight;
                
                cachedCurvePoints[i] = new Vector3(x, y, 0);
            }
            
            // Draw curve line with thickness
            Handles.DrawAAPolyLine(3f, cachedCurvePoints);
            
            Handles.EndGUI();
            
            // Draw "From" label at top-left, "To" label at bottom-left - use cached styles
            cachedFromLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            // Draw "From" label at top-left, "To" label at bottom-left - use cached styles
            cachedFromLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = FromStateColor },
                fontSize = 9
            };
            cachedToLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = ToStateColor },
                fontSize = 9
                normal = { textColor = FromStateColor },
                fontSize = 9
            };
            cachedToLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = ToStateColor },
                fontSize = 9
            };
            
            GUI.Label(new Rect(overlapRect.x + 4, overlapRect.y + 2, 30, 12), "From", cachedFromLabelStyle);
            GUI.Label(new Rect(overlapRect.x + 4, overlapRect.yMax - 14, 20, 12), "To", cachedToLabelStyle);
            
            GUI.Label(new Rect(overlapRect.x + 4, overlapRect.y + 2, 30, 12), "From", cachedFromLabelStyle);
            GUI.Label(new Rect(overlapRect.x + 4, overlapRect.yMax - 14, 20, 12), "To", cachedToLabelStyle);
        }
        
        private void DrawFromStateBar(float exitTimeSeconds, float pixelsPerSecond)
        private void DrawFromStateBar(float exitTimeSeconds, float pixelsPerSecond)
        {
            bool isHovered = hoveredElement == DragMode.FromBarEnd;
            bool isDragging = currentDragMode == DragMode.FromBarEnd;
            var barColor = (isHovered || isDragging) ? FromStateHighlightColor : FromStateColor;
            
            // Draw full bar
            EditorGUI.DrawRect(cachedFromBarRect, barColor);
            
            // Highlight the overlap portion (from exit time to end of from bar)
            float overlapStartX = Padding + exitTimeSeconds * pixelsPerSecond;
            float overlapWidth = cachedFromBarRect.xMax - overlapStartX;
            if (overlapWidth > 0)
            {
                var overlapRect = new Rect(overlapStartX, cachedFromBarRect.y, overlapWidth, cachedFromBarRect.height);
                EditorGUI.DrawRect(overlapRect, FromStateOverlapColor);
            }
            
            // Draw handle at exit time position
            var handleColor = (isHovered || isDragging) ? HandleHoverColor : HandleColor;
            var handleRect = new Rect(overlapStartX - HandleWidth / 2, cachedFromBarRect.y - 2, HandleWidth, cachedFromBarRect.height + 4);
            EditorGUI.DrawRect(handleRect, handleColor);
            
            // State name - use cached styles
            cachedBarLabelStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11
            };
            cachedBarDurationStyle ??= new GUIStyle(EditorStyles.miniLabel)
            bool isHovered = hoveredElement == DragMode.FromBarEnd;
            bool isDragging = currentDragMode == DragMode.FromBarEnd;
            var barColor = (isHovered || isDragging) ? FromStateHighlightColor : FromStateColor;
            
            // Draw full bar
            EditorGUI.DrawRect(cachedFromBarRect, barColor);
            
            // Highlight the overlap portion (from exit time to end of from bar)
            float overlapStartX = Padding + exitTimeSeconds * pixelsPerSecond;
            float overlapWidth = cachedFromBarRect.xMax - overlapStartX;
            if (overlapWidth > 0)
            {
                var overlapRect = new Rect(overlapStartX, cachedFromBarRect.y, overlapWidth, cachedFromBarRect.height);
                EditorGUI.DrawRect(overlapRect, FromStateOverlapColor);
            }
            
            // Draw handle at exit time position
            var handleColor = (isHovered || isDragging) ? HandleHoverColor : HandleColor;
            var handleRect = new Rect(overlapStartX - HandleWidth / 2, cachedFromBarRect.y - 2, HandleWidth, cachedFromBarRect.height + 4);
            EditorGUI.DrawRect(handleRect, handleColor);
            
            // State name - use cached styles
            cachedBarLabelStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11
            };
            cachedBarDurationStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = PreviewEditorColors.TransparentWhiteText },
                alignment = TextAnchor.MiddleRight
            };
            
            var labelRect = new Rect(cachedFromBarRect.x + 6, cachedFromBarRect.y, cachedFromBarRect.width - 12, cachedFromBarRect.height);
            GUI.Label(labelRect, fromStateName, cachedBarLabelStyle);
            GUI.Label(labelRect, $"{fromStateDuration:F2}s", cachedBarDurationStyle);
        }
        
        private void DrawToStateBar(float overlapStart, float overlapEnd, float pixelsPerSecond)
        {
            bool isHovered = hoveredElement == DragMode.ToBar;
            bool isDragging = currentDragMode == DragMode.ToBar;
            var barColor = (isHovered || isDragging) ? ToStateHighlightColor : ToStateColor;
            
            // Draw full bar
            EditorGUI.DrawRect(cachedToBarRect, barColor);
            
            // Highlight the overlap portion (from bar start to overlap end)
            float overlapWidthOnToBar = (overlapEnd - overlapStart) * pixelsPerSecond;
            if (overlapWidthOnToBar > 0)
            {
                var overlapRect = new Rect(cachedToBarRect.x, cachedToBarRect.y, overlapWidthOnToBar, cachedToBarRect.height);
                EditorGUI.DrawRect(overlapRect, ToStateOverlapColor);
            }
            
            // Draw handle at left edge (bar start)
            var handleColor = (isHovered || isDragging) ? HandleHoverColor : HandleColor;
            var handleRect = new Rect(cachedToBarRect.x - HandleWidth / 2, cachedToBarRect.y - 2, HandleWidth, cachedToBarRect.height + 4);
            EditorGUI.DrawRect(handleRect, handleColor);
            
            // State name - use cached styles (initialized in DrawFromStateBar)
            var labelRect = new Rect(cachedToBarRect.x + 6, cachedToBarRect.y, cachedToBarRect.width - 12, cachedToBarRect.height);
            GUI.Label(labelRect, toStateName, cachedBarLabelStyle);
            GUI.Label(labelRect, $"{toStateDuration:F2}s", cachedBarDurationStyle);
        }
        
        private void DrawConnectionLines(float overlapStart, float overlapEnd, float pixelsPerSecond)
        {
            if (cachedOverlapAreaRect.width <= 2) return;
            
            Handles.BeginGUI();
            
            // Draw vertical lines connecting bars to overlap area
            float leftX = cachedOverlapAreaRect.x;
            float rightX = cachedOverlapAreaRect.xMax;
            
            // Left connection line
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Handles.DrawLine(
                new Vector3(leftX, cachedFromBarRect.yMax),
                new Vector3(leftX, cachedOverlapAreaRect.y));
            Handles.DrawLine(
                new Vector3(leftX, cachedOverlapAreaRect.yMax),
                new Vector3(leftX, cachedToBarRect.y));
            
            // Right connection line
            Handles.DrawLine(
                new Vector3(rightX, cachedFromBarRect.yMax),
                new Vector3(rightX, cachedOverlapAreaRect.y));
            Handles.DrawLine(
                new Vector3(rightX, cachedOverlapAreaRect.yMax),
                new Vector3(rightX, cachedToBarRect.y));
                normal = { textColor = PreviewEditorColors.TransparentWhiteText },
                alignment = TextAnchor.MiddleRight
            };
            
            var labelRect = new Rect(cachedFromBarRect.x + 6, cachedFromBarRect.y, cachedFromBarRect.width - 12, cachedFromBarRect.height);
            GUI.Label(labelRect, fromStateName, cachedBarLabelStyle);
            GUI.Label(labelRect, $"{fromStateDuration:F2}s", cachedBarDurationStyle);
        }
        
        private void DrawToStateBar(float overlapStart, float overlapEnd, float pixelsPerSecond)
        {
            bool isHovered = hoveredElement == DragMode.ToBar;
            bool isDragging = currentDragMode == DragMode.ToBar;
            var barColor = (isHovered || isDragging) ? ToStateHighlightColor : ToStateColor;
            
            // Draw full bar
            EditorGUI.DrawRect(cachedToBarRect, barColor);
            
            // Highlight the overlap portion (from bar start to overlap end)
            float overlapWidthOnToBar = (overlapEnd - overlapStart) * pixelsPerSecond;
            if (overlapWidthOnToBar > 0)
            {
                var overlapRect = new Rect(cachedToBarRect.x, cachedToBarRect.y, overlapWidthOnToBar, cachedToBarRect.height);
                EditorGUI.DrawRect(overlapRect, ToStateOverlapColor);
            }
            
            // Draw handle at left edge (bar start)
            var handleColor = (isHovered || isDragging) ? HandleHoverColor : HandleColor;
            var handleRect = new Rect(cachedToBarRect.x - HandleWidth / 2, cachedToBarRect.y - 2, HandleWidth, cachedToBarRect.height + 4);
            EditorGUI.DrawRect(handleRect, handleColor);
            
            // State name - use cached styles (initialized in DrawFromStateBar)
            var labelRect = new Rect(cachedToBarRect.x + 6, cachedToBarRect.y, cachedToBarRect.width - 12, cachedToBarRect.height);
            GUI.Label(labelRect, toStateName, cachedBarLabelStyle);
            GUI.Label(labelRect, $"{toStateDuration:F2}s", cachedBarDurationStyle);
        }
        
        private void DrawConnectionLines(float overlapStart, float overlapEnd, float pixelsPerSecond)
        {
            if (cachedOverlapAreaRect.width <= 2) return;
            
            Handles.BeginGUI();
            
            // Draw vertical lines connecting bars to overlap area
            float leftX = cachedOverlapAreaRect.x;
            float rightX = cachedOverlapAreaRect.xMax;
            
            // Left connection line
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Handles.DrawLine(
                new Vector3(leftX, cachedFromBarRect.yMax),
                new Vector3(leftX, cachedOverlapAreaRect.y));
            Handles.DrawLine(
                new Vector3(leftX, cachedOverlapAreaRect.yMax),
                new Vector3(leftX, cachedToBarRect.y));
            
            // Right connection line
            Handles.DrawLine(
                new Vector3(rightX, cachedFromBarRect.yMax),
                new Vector3(rightX, cachedOverlapAreaRect.y));
            Handles.DrawLine(
                new Vector3(rightX, cachedOverlapAreaRect.yMax),
                new Vector3(rightX, cachedToBarRect.y));
            
            Handles.EndGUI();
        }
        
        private void DrawScrubber(Rect rect, float pixelsPerSecond)
        private void DrawScrubber(Rect rect, float pixelsPerSecond)
        {
            float totalDuration = GetTotalTimelineDuration();
            float scrubberX = Padding + NormalizedTime * totalDuration * pixelsPerSecond;
            float totalDuration = GetTotalTimelineDuration();
            float scrubberX = Padding + NormalizedTime * totalDuration * pixelsPerSecond;
            
            // Scrubber line (full height)
            var scrubberRect = new Rect(scrubberX - ScrubberWidth / 2, 0, ScrubberWidth, rect.height - 20);
            // Scrubber line (full height)
            var scrubberRect = new Rect(scrubberX - ScrubberWidth / 2, 0, ScrubberWidth, rect.height - 20);
            EditorGUI.DrawRect(scrubberRect, ScrubberColor);
            
            // Scrubber head (triangle at top)
            // Scrubber head (triangle at top)
            Handles.BeginGUI();
            Handles.color = ScrubberColor;
            Vector3[] triangle = {
                new Vector3(scrubberX, 0, 0),
                new Vector3(scrubberX - 6, -6, 0),
                new Vector3(scrubberX + 6, -6, 0)
            };
            Handles.DrawAAConvexPolygon(triangle);
            Vector3[] triangle = {
                new Vector3(scrubberX, 0, 0),
                new Vector3(scrubberX - 6, -6, 0),
                new Vector3(scrubberX + 6, -6, 0)
            };
            Handles.DrawAAConvexPolygon(triangle);
            Handles.EndGUI();
        }
        
        #endregion
        
        #region Private - Input Handling
        
        private void HandleIMGUIInput(Rect rect)
        #endregion
        
        #region Private - Input Handling
        
        private void HandleIMGUIInput(Rect rect)
        {
            Event e = Event.current;
            if (e == null) return;
            
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    HandleMouseDown(e, rect);
                    break;
                    
                case EventType.MouseDrag:
                    HandleMouseDrag(e, rect);
                    break;
                    
                case EventType.MouseUp:
                    HandleMouseUp(e);
                    break;
                    
                case EventType.MouseMove:
                    UpdateHoveredElement(e.mousePosition);
                    break;
                    
                case EventType.KeyDown:
                    HandleKeyDown(e);
                    break;
            }
        }
        
        private void HandleKeyDown(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Space:
                    TogglePlayPause();
                    e.Use();
                    break;
                    
                case KeyCode.LeftArrow:
                    StepBackward();
                    e.Use();
                    break;
                    
                case KeyCode.RightArrow:
                    StepForward();
                    e.Use();
                    break;
                    
                case KeyCode.Home:
                    GoToStart();
                    e.Use();
                    break;
                    
                case KeyCode.End:
                    GoToEnd();
                    e.Use();
                    break;
            }
        }
        
        private void HandleMouseDown(Event e, Rect rect)
        {
            // Take focus so keyboard shortcuts work
            Focus();
            
            var mousePos = e.mousePosition;
            float totalDuration = GetTotalTimelineDuration();
            float trackWidth = rect.width - Padding * 2;
            
            // Check hit targets in priority order
            
            // 1. Handle at exit time on From bar
            float exitTimeX = Padding + exitTime * fromStateDuration * (trackWidth / totalDuration);
            if (IsNearX(mousePos.x, exitTimeX) && IsInVerticalRange(mousePos.y, cachedFromBarRect, 10))
            {
                StartDrag(DragMode.FromBarEnd, exitTime, mousePos.x);
                e.Use();
                return;
            }
            
            // 2. To bar (drag entire bar)
            if (IsInRect(mousePos, cachedToBarRect, 10))
            {
                StartDrag(DragMode.ToBar, exitTime, mousePos.x);
                e.Use();
                return;
            }
            
            // 3. Track area - scrub
            var trackRect = new Rect(Padding, 0, trackWidth, rect.height - 20);
            if (trackRect.Contains(mousePos))
            {
                StartDrag(DragMode.Scrubber, NormalizedTime, mousePos.x);
                float normalizedX = Mathf.Clamp01((mousePos.x - Padding) / trackWidth);
                NormalizedTime = normalizedX;
                e.Use();
                StartDrag(DragMode.Scrubber, NormalizedTime, mousePos.x);
                float normalizedX = Mathf.Clamp01((mousePos.x - Padding) / trackWidth);
                NormalizedTime = normalizedX;
                e.Use();
            }
        }
        
        private void HandleMouseDrag(Event e, Rect rect)
        {
            if (currentDragMode == DragMode.None) return;
            
            float totalDuration = GetTotalTimelineDuration();
            float trackWidth = rect.width - Padding * 2;
            float pixelsPerSecond = trackWidth / totalDuration;
            float deltaX = e.mousePosition.x - dragStartMouseX;
            
            switch (currentDragMode)
            {
                case DragMode.Scrubber:
                    float normalizedX = Mathf.Clamp01((e.mousePosition.x - Padding) / trackWidth);
                    NormalizedTime = normalizedX;
                    break;
                    
                case DragMode.FromBarEnd:
                    // Dragging exit time handle (controls where transition starts)
                    float deltaSeconds = deltaX / pixelsPerSecond;
                    float newExitTimeSeconds = dragStartValue * fromStateDuration + deltaSeconds;
                    float newExitTime = Mathf.Clamp(newExitTimeSeconds / fromStateDuration, 0.05f, 0.95f);
                    
                    if (Math.Abs(newExitTime - exitTime) > ValueChangeEpsilon)
                    {
                        exitTime = newExitTime;
                        RecalculateTransitionDuration();
                        UpdateDuration();
                        OnExitTimeChanged?.Invoke(exitTime);
                        OnTransitionDurationChanged?.Invoke(transitionDuration);
                    }
                    break;
                    
                case DragMode.ToBar:
                    // Dragging To bar changes exit time (where the bar starts)
                    float toBarDeltaSeconds = deltaX / pixelsPerSecond;
                    float newToBarStartSeconds = dragStartValue * fromStateDuration + toBarDeltaSeconds;
                    float newExitTimeFromBar = Mathf.Clamp(newToBarStartSeconds / fromStateDuration, 0f, 0.99f);
                    
                    // Don't let To bar go past the end of From bar (need some overlap)
                    float minOverlap = 0.05f * fromStateDuration;
                    float maxStartTime = (fromStateDuration - minOverlap) / fromStateDuration;
                    newExitTimeFromBar = Mathf.Min(newExitTimeFromBar, maxStartTime);
                    
                    if (Math.Abs(newExitTimeFromBar - exitTime) > ValueChangeEpsilon)
                    {
                        exitTime = newExitTimeFromBar;
                        RecalculateTransitionDuration();
                        UpdateDuration();
                        OnExitTimeChanged?.Invoke(exitTime);
                        OnTransitionDurationChanged?.Invoke(transitionDuration);
                    }
                    break;
            }
            
            MarkDirtyRepaint();
            e.Use();
        }
        
        private void HandleMouseUp(Event e)
        {
            if (currentDragMode != DragMode.None)
            {
                currentDragMode = DragMode.None;
                MarkDirtyRepaint();
            }
        }
        
        private void StartDrag(DragMode mode, float startValue, float mouseX)
        {
            currentDragMode = mode;
            dragStartValue = startValue;
            dragStartMouseX = mouseX;
        }
        
        private void UpdateHoveredElement(Vector2 mousePos)
        {
            var previousHovered = hoveredElement;
            hoveredElement = DragMode.None;
            
            // Check exit time handle on From bar
            float exitTimeX = cachedFromBarRect.x + exitTime * fromStateDuration * (cachedFromBarRect.width / fromStateDuration);
            if (IsNearX(mousePos.x, exitTimeX) && IsInVerticalRange(mousePos.y, cachedFromBarRect, 10))
            {
                hoveredElement = DragMode.FromBarEnd;
            }
            else if (IsInRect(mousePos, cachedToBarRect, 5))
            {
                hoveredElement = DragMode.ToBar;
            }
            
            // Update cursor
            if (hoveredElement == DragMode.FromBarEnd)
            {
                EditorGUIUtility.AddCursorRect(drawingContainer.contentRect, MouseCursor.ResizeHorizontal);
            }
            else if (hoveredElement == DragMode.ToBar)
            {
                EditorGUIUtility.AddCursorRect(drawingContainer.contentRect, MouseCursor.Pan);
            }
            
            if (hoveredElement != previousHovered)
            {
                MarkDirtyRepaint();
            }
        }
        
        private bool IsNearX(float mouseX, float targetX)
        {
            return Mathf.Abs(mouseX - targetX) <= DragHitRadius;
        }
        
        private bool IsInVerticalRange(float mouseY, Rect barRect, float padding)
        {
            return mouseY >= barRect.y - padding && mouseY <= barRect.yMax + padding;
        }
        
        private bool IsInRect(Vector2 mousePos, Rect rect, float padding)
        {
            return mousePos.x >= rect.x - padding && mousePos.x <= rect.xMax + padding &&
                   mousePos.y >= rect.y - padding && mousePos.y <= rect.yMax + padding;
        private void HandleMouseDrag(Event e, Rect rect)
        {
            if (currentDragMode == DragMode.None) return;
            
            float totalDuration = GetTotalTimelineDuration();
            float trackWidth = rect.width - Padding * 2;
            float pixelsPerSecond = trackWidth / totalDuration;
            float deltaX = e.mousePosition.x - dragStartMouseX;
            
            switch (currentDragMode)
            {
                case DragMode.Scrubber:
                    float normalizedX = Mathf.Clamp01((e.mousePosition.x - Padding) / trackWidth);
                    NormalizedTime = normalizedX;
                    break;
                    
                case DragMode.FromBarEnd:
                    // Dragging exit time handle (controls where transition starts)
                    float deltaSeconds = deltaX / pixelsPerSecond;
                    float newExitTimeSeconds = dragStartValue * fromStateDuration + deltaSeconds;
                    float newExitTime = Mathf.Clamp(newExitTimeSeconds / fromStateDuration, 0.05f, 0.95f);
                    
                    if (Math.Abs(newExitTime - exitTime) > ValueChangeEpsilon)
                    {
                        exitTime = newExitTime;
                        RecalculateTransitionDuration();
                        UpdateDuration();
                        OnExitTimeChanged?.Invoke(exitTime);
                        OnTransitionDurationChanged?.Invoke(transitionDuration);
                    }
                    break;
                    
                case DragMode.ToBar:
                    // Dragging To bar changes exit time (where the bar starts)
                    float toBarDeltaSeconds = deltaX / pixelsPerSecond;
                    float newToBarStartSeconds = dragStartValue * fromStateDuration + toBarDeltaSeconds;
                    float newExitTimeFromBar = Mathf.Clamp(newToBarStartSeconds / fromStateDuration, 0f, 0.99f);
                    
                    // Don't let To bar go past the end of From bar (need some overlap)
                    float minOverlap = 0.05f * fromStateDuration;
                    float maxStartTime = (fromStateDuration - minOverlap) / fromStateDuration;
                    newExitTimeFromBar = Mathf.Min(newExitTimeFromBar, maxStartTime);
                    
                    if (Math.Abs(newExitTimeFromBar - exitTime) > ValueChangeEpsilon)
                    {
                        exitTime = newExitTimeFromBar;
                        RecalculateTransitionDuration();
                        UpdateDuration();
                        OnExitTimeChanged?.Invoke(exitTime);
                        OnTransitionDurationChanged?.Invoke(transitionDuration);
                    }
                    break;
            }
            
            MarkDirtyRepaint();
            e.Use();
        }
        
        private void HandleMouseUp(Event e)
        {
            if (currentDragMode != DragMode.None)
            {
                currentDragMode = DragMode.None;
                MarkDirtyRepaint();
            }
        }
        
        private void StartDrag(DragMode mode, float startValue, float mouseX)
        {
            currentDragMode = mode;
            dragStartValue = startValue;
            dragStartMouseX = mouseX;
        }
        
        private void UpdateHoveredElement(Vector2 mousePos)
        {
            var previousHovered = hoveredElement;
            hoveredElement = DragMode.None;
            
            // Check exit time handle on From bar
            float exitTimeX = cachedFromBarRect.x + exitTime * fromStateDuration * (cachedFromBarRect.width / fromStateDuration);
            if (IsNearX(mousePos.x, exitTimeX) && IsInVerticalRange(mousePos.y, cachedFromBarRect, 10))
            {
                hoveredElement = DragMode.FromBarEnd;
            }
            else if (IsInRect(mousePos, cachedToBarRect, 5))
            {
                hoveredElement = DragMode.ToBar;
            }
            
            // Update cursor
            if (hoveredElement == DragMode.FromBarEnd)
            {
                EditorGUIUtility.AddCursorRect(drawingContainer.contentRect, MouseCursor.ResizeHorizontal);
            }
            else if (hoveredElement == DragMode.ToBar)
            {
                EditorGUIUtility.AddCursorRect(drawingContainer.contentRect, MouseCursor.Pan);
            }
            
            if (hoveredElement != previousHovered)
            {
                MarkDirtyRepaint();
            }
        }
        
        private bool IsNearX(float mouseX, float targetX)
        {
            return Mathf.Abs(mouseX - targetX) <= DragHitRadius;
        }
        
        private bool IsInVerticalRange(float mouseY, Rect barRect, float padding)
        {
            return mouseY >= barRect.y - padding && mouseY <= barRect.yMax + padding;
        }
        
        private bool IsInRect(Vector2 mousePos, Rect rect, float padding)
        {
            return mousePos.x >= rect.x - padding && mousePos.x <= rect.xMax + padding &&
                   mousePos.y >= rect.y - padding && mousePos.y <= rect.yMax + padding;
        }
        
        #endregion
        
        #region Private - Helpers
        
        private void RecalculateTransitionDuration()
        {
            // Validate state durations to avoid division issues
            if (fromStateDuration <= 0.001f || toStateDuration <= 0.001f)
            {
                transitionDuration = 0.01f;
                return;
            }
            
            // Transition duration is the overlap between From and To bars
            float exitTimeSeconds = exitTime * fromStateDuration;
            float fromBarEnd = fromStateDuration;
            float toBarEnd = exitTimeSeconds + toStateDuration;
            
            transitionDuration = Mathf.Max(0.01f, Mathf.Min(fromBarEnd, toBarEnd) - exitTimeSeconds);
        }
        
        private void RecalculateTransitionDuration()
        {
            // Validate state durations to avoid division issues
            if (fromStateDuration <= 0.001f || toStateDuration <= 0.001f)
            {
                transitionDuration = 0.01f;
                return;
            }
            
            // Transition duration is the overlap between From and To bars
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
            
            // Show the longer of: From bar end, To bar end
            return Mathf.Max(fromStateDuration, toBarEnd) + 0.1f;
            float toBarEnd = exitTimeSeconds + toStateDuration;
            
            // Show the longer of: From bar end, To bar end
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
