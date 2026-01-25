using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// A timeline scrubber control for animation preview.
    /// Displays a horizontal track with draggable playhead, time display, frame ticks, and event markers.
    /// </summary>
    [UxmlElement]
    internal partial class TimelineScrubber : TimelineBase
    {
        #region Constants
        
        private const float ScrubberWidth = 12f;
        private const float TrackHeight = 20f;
        private const float MarkerSize = 8f;
        private const float MinTickSpacing = 4f;
        private const int MaxPooledTicks = 256;
        
        private static readonly int[] NiceFrameSteps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        
        #endregion

        #region UI Elements

        private readonly VisualElement trackContainer;
        private readonly VisualElement track;
        private readonly VisualElement scrubber;
        private readonly VisualElement progressFill;
        private readonly Label timeLabel;
        private readonly VisualElement markersContainer;
        private readonly VisualElement frameTicksContainer;
        private readonly Button playButton;
        private readonly IntegerField frameField;

        #endregion

        #region State

        private float frameRate = 30f;
        private bool showFrameTicks = true;
        private readonly List<EventMarker> eventMarkers = new(16);
        private readonly List<VisualElement> frameTicks = new(MaxPooledTicks);
        
        // Cached delegates to avoid allocations
        private Action cachedUpdateScrubberAction;
        private Action cachedUpdateMarkersAction;

        #endregion

        #region Properties

        /// <summary>
        /// Frame rate for frame calculations (frames per second).
        /// </summary>
        public float FrameRate
        {
            get => frameRate;
            set
            {
                frameRate = Mathf.Max(1f, value);
                UpdateTimeDisplay();
                UpdateFrameTicks();
            }
        }

        /// <summary>
        /// Current frame number (0-based).
        /// </summary>
        public int CurrentFrame
        {
            get => Mathf.RoundToInt(CurrentTime * frameRate);
            set => CurrentTime = value / frameRate;
        }

        /// <summary>
        /// Total number of frames in the timeline.
        /// </summary>
        public int TotalFrames => Mathf.Max(1, Mathf.RoundToInt(Duration * frameRate));

        /// <summary>
        /// Whether to show frame tick marks on the timeline.
        /// </summary>
        public bool ShowFrameTicks
        {
            get => showFrameTicks;
            set
            {
                showFrameTicks = value;
                UpdateFrameTicks();
            }
        }

        #endregion

        public TimelineScrubber()
        {
            AddToClassList("timeline-scrubber");

            // Top row: Play button + time display + frame input
            var topRow = new VisualElement();
            topRow.AddToClassList("timeline-top-row");

            playButton = new Button(OnPlayButtonClicked);
            playButton.AddToClassList("timeline-play-button");
            playButton.text = "\u25b6"; // Play symbol
            topRow.Add(playButton);

            timeLabel = new Label("0.00s / 0.00s");
            timeLabel.AddToClassList("timeline-time-label");
            topRow.Add(timeLabel);

            // Frame input field
            var frameContainer = new VisualElement();
            frameContainer.AddToClassList("timeline-frame-container");
            
            var frameLabel = new Label("Frame:");
            frameLabel.AddToClassList("timeline-frame-label");
            frameContainer.Add(frameLabel);
            
            frameField = new IntegerField();
            frameField.AddToClassList("timeline-frame-field");
            frameField.value = 0;
            frameField.RegisterValueChangedCallback(OnFrameFieldChanged);
            frameContainer.Add(frameField);
            
            topRow.Add(frameContainer);

            Add(topRow);

            // Track container
            trackContainer = new VisualElement();
            trackContainer.AddToClassList("timeline-track-container");

            // Track background
            track = new VisualElement();
            track.AddToClassList("timeline-track");

            // Progress fill
            progressFill = new VisualElement();
            progressFill.AddToClassList("timeline-progress-fill");
            track.Add(progressFill);

            // Frame ticks container (below progress, above markers)
            frameTicksContainer = new VisualElement();
            frameTicksContainer.AddToClassList("timeline-frame-ticks-container");
            track.Add(frameTicksContainer);

            // Event markers container
            markersContainer = new VisualElement();
            markersContainer.AddToClassList("timeline-markers-container");
            track.Add(markersContainer);

            // Scrubber handle
            scrubber = new VisualElement();
            scrubber.AddToClassList("timeline-scrubber-handle");
            track.Add(scrubber);

            trackContainer.Add(track);
            Add(trackContainer);

            // Initial state
            UpdatePlayButton();
            UpdateTimeDisplay();
        }

        #region TimelineBase Overrides
        
        protected override Rect GetTrackRect()
        {
            return trackContainer.contentRect;
        }
        
        protected override void OnDurationChanged()
        {
            base.OnDurationChanged();
            UpdateTimeDisplay();
            UpdateScrubberPosition();
            UpdateFrameTicks();
        }
        
        protected override void OnCurrentTimeChanged()
        {
            base.OnCurrentTimeChanged();
            UpdateTimeDisplay();
            UpdateScrubberPosition();
        }
        
        protected override void OnPlayingStateChanged()
        {
            base.OnPlayingStateChanged();
            UpdatePlayButton();
        }
        
        protected override void OnLoopingStateChanged()
        {
            base.OnLoopingStateChanged();
            EnableInClassList("timeline-looping", IsLooping);
        }
        
        protected override void OnGeometryChanged(GeometryChangedEvent evt)
        {
            base.OnGeometryChanged(evt);
            UpdateFrameTicks();
            UpdateMarkerPositions();
        }
        
        #endregion

        #region Frame Stepping
        
        /// <summary>
        /// Steps forward by one frame using the configured frame rate.
        /// </summary>
        public void StepForward()
        {
            CurrentFrame = Mathf.Min(TotalFrames, CurrentFrame + 1);
        }
        
        /// <summary>
        /// Steps backward by one frame using the configured frame rate.
        /// </summary>
        public void StepBackward()
        {
            CurrentFrame = Mathf.Max(0, CurrentFrame - 1);
        }
        
        #endregion
        
        #region Event Markers

        /// <summary>
        /// Sets the event markers to display on the timeline.
        /// </summary>
        public void SetEventMarkers(List<(float normalizedTime, string name)> markers)
        {
            int markerCount = markers?.Count ?? 0;
            
            // Reuse or create markers as needed
            for (int i = 0; i < markerCount; i++)
            {
                var (normalizedTime, name) = markers[i];
                
                EventMarker marker;
                if (i < eventMarkers.Count)
                {
                    marker = eventMarkers[i];
                    marker.Configure(normalizedTime, name);
                    marker.style.display = DisplayStyle.Flex;
                }
                else
                {
                    marker = new EventMarker();
                    marker.Configure(normalizedTime, name);
                    eventMarkers.Add(marker);
                    markersContainer.Add(marker);
                }
            }
            
            // Hide unused markers
            for (int i = markerCount; i < eventMarkers.Count; i++)
            {
                eventMarkers[i].style.display = DisplayStyle.None;
            }

            // Update positions after layout (use cached delegate to avoid allocation)
            cachedUpdateMarkersAction ??= UpdateMarkerPositions;
            schedule.Execute(cachedUpdateMarkersAction);
        }

        /// <summary>
        /// Clears all event markers (hides them).
        /// </summary>
        public void ClearEventMarkers()
        {
            for (int i = 0; i < eventMarkers.Count; i++)
            {
                eventMarkers[i].style.display = DisplayStyle.None;
            }
        }

        private void UpdateMarkerPositions()
        {
            var trackWidth = track.resolvedStyle.width;
            if (trackWidth <= 0) return;

            for (int i = 0; i < eventMarkers.Count; i++)
            {
                var marker = eventMarkers[i];
                if (marker.style.display == DisplayStyle.None) continue;
                
                var xPos = marker.NormalizedTime * trackWidth - MarkerSize / 2;
                marker.style.left = xPos;
            }
        }

        #endregion

        #region Private - UI
        
        private void OnPlayButtonClicked()
        {
            TogglePlayPause();
        }
        
        private void OnFrameFieldChanged(ChangeEvent<int> evt)
        {
            // Clamp to valid range and update time
            var frame = Mathf.Clamp(evt.newValue, 0, TotalFrames);
            if (frame != CurrentFrame)
            {
                CurrentFrame = frame;
            }
        }

        private void UpdateTimeDisplay()
        {
            if (timeLabel == null) return;
            timeLabel.text = $"{CurrentTime:F2}s / {Duration:F2}s";
            
            // Update frame field without triggering change event
            if (frameField != null)
            {
                frameField.SetValueWithoutNotify(CurrentFrame);
            }
        }
        
        private void UpdateFrameTicks()
        {
            if (frameTicksContainer == null) return;
            
            if (!showFrameTicks)
            {
                // Hide all pooled ticks
                for (int i = 0; i < frameTicks.Count; i++)
                {
                    frameTicks[i].style.display = DisplayStyle.None;
                }
                return;
            }
            
            var trackWidth = track.resolvedStyle.width;
            if (trackWidth <= 0) return;
            
            var totalFrames = TotalFrames;
            if (totalFrames <= 1) return;
            
            // Calculate tick spacing and determine step size
            var pixelsPerFrame = trackWidth / totalFrames;
            var frameStep = 1;
            
            // If ticks would be too close, increase step to show every Nth frame
            if (pixelsPerFrame < MinTickSpacing)
            {
                frameStep = Mathf.CeilToInt(MinTickSpacing / pixelsPerFrame);
                frameStep = GetNiceFrameStep(frameStep);
            }
            
            // Count required ticks
            int tickIndex = 0;
            for (int frame = 0; frame <= totalFrames; frame += frameStep)
            {
                var normalizedPos = (float)frame / totalFrames;
                var xPos = normalizedPos * trackWidth;
                
                // Get or create tick from pool
                VisualElement tick;
                if (tickIndex < frameTicks.Count)
                {
                    tick = frameTicks[tickIndex];
                    tick.style.display = DisplayStyle.Flex;
                }
                else if (frameTicks.Count < MaxPooledTicks)
                {
                    tick = new VisualElement();
                    tick.AddToClassList("timeline-frame-tick");
                    frameTicksContainer.Add(tick);
                    frameTicks.Add(tick);
                }
                else
                {
                    break; // Pool exhausted
                }
                
                // Major ticks at every 5th step, or at start/end
                var isMajor = (frame % (frameStep * 5) == 0) || frame == 0 || frame == totalFrames;
                tick.EnableInClassList("timeline-frame-tick-major", isMajor);
                
                tick.style.left = xPos;
                tickIndex++;
            }
            
            // Hide unused pooled ticks
            for (int i = tickIndex; i < frameTicks.Count; i++)
            {
                frameTicks[i].style.display = DisplayStyle.None;
            }
        }
        
        private static int GetNiceFrameStep(int minStep)
        {
            foreach (var n in NiceFrameSteps)
            {
                if (n >= minStep) return n;
            }
            return minStep;
        }

        private void UpdateScrubberPosition()
        {
            if (scrubber == null || track == null) return;

            // Schedule for after layout (use cached delegate to avoid allocation)
            cachedUpdateScrubberAction ??= UpdateScrubberPositionInternal;
            schedule.Execute(cachedUpdateScrubberAction);
        }
        
        private void UpdateScrubberPositionInternal()
        {
            var trackWidth = track.resolvedStyle.width;
            if (trackWidth <= 0) return;

            var xPos = NormalizedTime * trackWidth - ScrubberWidth / 2;
            scrubber.style.left = xPos;
            
            // Update progress fill
            progressFill.style.width = new Length(NormalizedTime * 100, LengthUnit.Percent);
        }

        private void UpdatePlayButton()
        {
            if (playButton == null) return;
            playButton.text = IsPlaying ? "\u275a\u275a" : "\u25b6"; // Pause or Play
            playButton.tooltip = IsPlaying ? "Pause" : "Play";
        }

        #endregion

        #region Event Marker Class

        private class EventMarker : VisualElement
        {
            public float NormalizedTime { get; private set; }
            public string EventName { get; private set; }

            public EventMarker()
            {
                AddToClassList("timeline-event-marker");

                // Visual dot
                var dot = new VisualElement();
                dot.AddToClassList("timeline-event-marker-dot");
                Add(dot);
            }
            
            public void Configure(float normalizedTime, string name)
            {
                NormalizedTime = normalizedTime;
                EventName = name;
                tooltip = name;
            }
        }

        #endregion
    }
}
