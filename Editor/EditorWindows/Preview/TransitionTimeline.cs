using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// A timeline control for transition preview.
    /// Displays from/to state bars with crossfade visualization, similar to Unity's Animator transition inspector.
    /// </summary>
    [UxmlElement]
    internal partial class TransitionTimeline : TimelineBase
    {
        #region Constants
        
        private const float BarHeight = 24f;
        private const float BarSpacing = 4f;
        private const float HeaderHeight = 20f;
        private const float FooterHeight = 28f;
        private const float ScrubberWidth = 2f;
        private const float MarkerTriangleSize = 6f;
        private const float HatchLineSpacing = 6f;
        private const float BoundaryMarkerWidth = 3f;
        
        #endregion
        
        #region Colors
        
        private static readonly Color BackgroundColor = new(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color FromStateColor = new(0.3f, 0.5f, 0.8f, 1f);
        private static readonly Color ToStateColor = new(0.3f, 0.7f, 0.5f, 1f);
        private static readonly Color HatchColor = new(1f, 1f, 1f, 0.35f);
        private static readonly Color ScrubberColor = new(1f, 0.5f, 0f, 1f);
        private static readonly Color ExitTimeMarkerColor = new(0.9f, 0.9f, 0.3f, 1f);
        private static readonly Color TextColor = new(0.9f, 0.9f, 0.9f, 1f);
        
        #endregion
        
        #region State
        
        private float exitTime = 0.75f;
        private float transitionDuration = 0.25f;
        private float fromStateDuration = 1f;
        private float toStateDuration = 1f;
        private float transitionOffset;
        
        private string fromStateName = "From State";
        private string toStateName = "To State";
        
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
        
        #region Events
        
        /// <summary>
        /// Fired when the transition progress changes.
        /// Progress is 0 before exit time, 0-1 during transition, 1 after.
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
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The exit time (0-1 normalized) when the transition starts.
        /// </summary>
        public float ExitTime
        {
            get => exitTime;
            set
            {
                exitTime = Mathf.Clamp01(value);
                UpdateDuration();
                MarkDirtyRepaint();
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
                transitionDuration = Mathf.Max(0.01f, value);
                UpdateDuration();
                MarkDirtyRepaint();
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
                transitionOffset = Mathf.Clamp01(value);
                MarkDirtyRepaint();
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
                
                float progress = (currentSeconds - exitTimeSeconds) / transitionDuration;
                return Mathf.Clamp01(progress);
            }
        }
        
        #endregion
        
        #region Constructor
        
        public TransitionTimeline()
        {
            AddToClassList("transition-timeline");
            style.minHeight = HeaderHeight + BarHeight * 2 + BarSpacing + FooterHeight + 20;
            
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
            trackArea.style.minHeight = BarHeight * 2 + BarSpacing + 20;
            
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
            footer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            
            playButton = new Button(TogglePlayPause);
            playButton.text = "\u25b6";
            playButton.style.width = 28;
            playButton.style.height = 20;
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
            float transitionOffset = 0f)
        {
            FromStateName = fromState?.name ?? "Any State";
            ToStateName = toState?.name ?? "To State";
            FromStateDuration = GetStateDuration(fromState);
            ToStateDuration = GetStateDuration(toState);
            this.exitTime = Mathf.Clamp01(exitTime);
            this.transitionDuration = Mathf.Max(0.01f, transitionDuration);
            this.transitionOffset = Mathf.Clamp01(transitionOffset);
            
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
            
            // Calculate bar positions
            float padding = 10f;
            var fromBarRect = new Rect(padding, padding, rect.width - padding * 2, BarHeight);
            var toBarRect = new Rect(padding, padding + BarHeight + BarSpacing, rect.width - padding * 2, BarHeight);
            
            // Draw state bars
            DrawFromStateBar(fromBarRect);
            DrawToStateBar(toBarRect);
            
            // Draw transition blend region (hatched area)
            DrawTransitionBlendRegion(fromBarRect, toBarRect);
            
            // Draw scrubber
            DrawScrubber(fromBarRect, toBarRect);
            
            // Handle input for IMGUI portion
            HandleIMGUIInput(rect, fromBarRect, toBarRect);
        }
        
        private void DrawFromStateBar(Rect barRect)
        {
            float totalDuration = GetTotalTimelineDuration();
            float transitionEndSeconds = exitTime * fromStateDuration + transitionDuration;
            float fromBarEndNormalized = Mathf.Min(transitionEndSeconds / totalDuration, 1f);
            
            // Draw from state bar (from start to end of transition)
            float fromBarWidth = fromBarEndNormalized * barRect.width;
            var fromBarActualRect = new Rect(barRect.x, barRect.y, fromBarWidth, barRect.height);
            EditorGUI.DrawRect(fromBarActualRect, FromStateColor);
            
            // Draw exit time marker (yellow vertical line)
            float exitTimeNormalized = (exitTime * fromStateDuration) / totalDuration;
            float exitX = barRect.x + exitTimeNormalized * barRect.width;
            var exitMarkerRect = new Rect(exitX - 1, barRect.y - 2, 2, barRect.height + 4);
            EditorGUI.DrawRect(exitMarkerRect, ExitTimeMarkerColor);
        }
        
        private void DrawToStateBar(Rect barRect)
        {
            float totalDuration = GetTotalTimelineDuration();
            float exitTimeSeconds = exitTime * fromStateDuration;
            float toStateStartNormalized = exitTimeSeconds / totalDuration;
            
            float toBarStartX = barRect.x + toStateStartNormalized * barRect.width;
            float toBarWidth = barRect.width - (toBarStartX - barRect.x);
            
            if (toBarWidth > 0)
            {
                var toBarRect = new Rect(toBarStartX, barRect.y, toBarWidth, barRect.height);
                EditorGUI.DrawRect(toBarRect, ToStateColor);
            }
        }
        
        private void DrawTransitionBlendRegion(Rect fromBarRect, Rect toBarRect)
        {
            float totalDuration = GetTotalTimelineDuration();
            float exitTimeSeconds = exitTime * fromStateDuration;
            float transitionEndSeconds = exitTimeSeconds + transitionDuration;
            
            float blendStartNormalized = exitTimeSeconds / totalDuration;
            float blendEndNormalized = Mathf.Min(transitionEndSeconds / totalDuration, 1f);
            
            float blendStartX = fromBarRect.x + blendStartNormalized * fromBarRect.width;
            float blendEndX = fromBarRect.x + blendEndNormalized * fromBarRect.width;
            float blendWidth = blendEndX - blendStartX;
            
            if (blendWidth <= 0) return;
            
            // Draw hatching lines over the blend region
            var blendRect = new Rect(blendStartX, fromBarRect.y, blendWidth, toBarRect.yMax - fromBarRect.y);
            DrawHatchingLines(blendRect);
            
            // Draw transition boundary markers
            EditorGUI.DrawRect(new Rect(blendStartX - BoundaryMarkerWidth / 2, fromBarRect.y - 2, BoundaryMarkerWidth, blendRect.height + 4), ScrubberColor);
            EditorGUI.DrawRect(new Rect(blendEndX - BoundaryMarkerWidth / 2, fromBarRect.y - 2, BoundaryMarkerWidth, blendRect.height + 4), ScrubberColor);
            
            // Draw triangular markers at top
            Handles.BeginGUI();
            Handles.color = ScrubberColor;
            
            // Start marker triangle
            Vector3[] startTriangle = {
                new Vector3(blendStartX, fromBarRect.y - 4, 0),
                new Vector3(blendStartX - MarkerTriangleSize, fromBarRect.y - 4 - MarkerTriangleSize, 0),
                new Vector3(blendStartX + MarkerTriangleSize, fromBarRect.y - 4 - MarkerTriangleSize, 0)
            };
            Handles.DrawAAConvexPolygon(startTriangle);
            
            // End marker triangle
            Vector3[] endTriangle = {
                new Vector3(blendEndX, fromBarRect.y - 4, 0),
                new Vector3(blendEndX - MarkerTriangleSize, fromBarRect.y - 4 - MarkerTriangleSize, 0),
                new Vector3(blendEndX + MarkerTriangleSize, fromBarRect.y - 4 - MarkerTriangleSize, 0)
            };
            Handles.DrawAAConvexPolygon(endTriangle);
            
            Handles.EndGUI();
            
            // Duration label centered in blend region
            var durationLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = TextColor },
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(blendStartX, blendRect.yMax + 2, blendWidth, 14), 
                $"{transitionDuration:F2}s", durationLabelStyle);
        }
        
        private void DrawHatchingLines(Rect rect)
        {
            Handles.BeginGUI();
            Handles.color = HatchColor;
            
            float totalLines = (rect.width + rect.height) / HatchLineSpacing;
            
            for (int i = 0; i < totalLines; i++)
            {
                float offset = i * HatchLineSpacing;
                
                float startX = rect.x + offset;
                float startY = rect.y;
                if (startX > rect.xMax)
                {
                    startY = rect.y + (startX - rect.xMax);
                    startX = rect.xMax;
                }
                
                float endX = rect.x + offset - rect.height;
                float endY = rect.yMax;
                if (endX < rect.x)
                {
                    endY = rect.yMax - (rect.x - endX);
                    endX = rect.x;
                }
                
                if (startY < rect.yMax && endY > rect.y)
                {
                    Handles.DrawLine(new Vector3(startX, startY), new Vector3(endX, endY));
                }
            }
            
            Handles.EndGUI();
        }
        
        private void DrawScrubber(Rect fromBarRect, Rect toBarRect)
        {
            float scrubberX = fromBarRect.x + NormalizedTime * fromBarRect.width;
            
            // Scrubber line
            var scrubberRect = new Rect(scrubberX - ScrubberWidth / 2, fromBarRect.y - 4, ScrubberWidth, toBarRect.yMax - fromBarRect.y + 8);
            EditorGUI.DrawRect(scrubberRect, ScrubberColor);
            
            // Scrubber handle (circle at top)
            Handles.BeginGUI();
            Handles.color = ScrubberColor;
            Handles.DrawSolidDisc(new Vector3(scrubberX, fromBarRect.y - 6, 0), Vector3.forward, 5);
            Handles.EndGUI();
        }
        
        private void HandleIMGUIInput(Rect rect, Rect fromBarRect, Rect toBarRect)
        {
            Event e = Event.current;
            
            // Create a combined track rect for scrubbing
            var trackRect = new Rect(fromBarRect.x, fromBarRect.y - 10, fromBarRect.width, toBarRect.yMax - fromBarRect.y + 20);
            
            if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(e.mousePosition))
            {
                IsDragging = true;
                UpdateTimeFromMouse(e.mousePosition, fromBarRect);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && IsDragging)
            {
                UpdateTimeFromMouse(e.mousePosition, fromBarRect);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                IsDragging = false;
            }
        }
        
        private void UpdateTimeFromMouse(Vector2 mousePos, Rect trackRect)
        {
            float normalizedX = Mathf.Clamp01((mousePos.x - trackRect.x) / trackRect.width);
            NormalizedTime = normalizedX;
        }
        
        #endregion
        
        #region Private - Helpers
        
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
            return exitTimeSeconds + transitionDuration;
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
