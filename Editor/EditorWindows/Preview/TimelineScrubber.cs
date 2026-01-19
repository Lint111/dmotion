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
    internal partial class TimelineScrubber : VisualElement
    {

        private const float ScrubberWidth = 12f;
        private const float TrackHeight = 20f;
        private const float MarkerSize = 8f;
        private const float MinTickSpacing = 4f; // Minimum pixels between frame ticks
        private const int MaxPooledTicks = 256;
        
        private static readonly int[] NiceFrameSteps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };

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

        private float duration = 1f;
        private float currentTime;
        private float frameRate = 30f;
        private bool isDragging;
        private bool isPlaying;
        private bool isLooping;
        private bool showFrameTicks = true;
        private readonly List<EventMarker> eventMarkers = new(16);
        private readonly List<VisualElement> frameTicks = new(MaxPooledTicks);
        
        // Cached delegates to avoid allocations
        private Action cachedUpdateScrubberAction;
        private Action cachedUpdateMarkersAction;

        #endregion

        #region Events

        /// <summary>
        /// Fired when the current time changes (from scrubbing or playback).
        /// </summary>
        public event Action<float> OnTimeChanged;

        /// <summary>
        /// Fired when play/pause state changes.
        /// </summary>
        public event Action<bool> OnPlayStateChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Total duration of the timeline in seconds.
        /// </summary>
        public float Duration
        {
            get => duration;
            set
            {
                duration = Mathf.Max(0.001f, value);
                UpdateTimeDisplay();
                UpdateScrubberPosition();
                UpdateFrameTicks();
            }
        }

        /// <summary>
        /// Current time position in seconds.
        /// </summary>
        public float CurrentTime
        {
            get => currentTime;
            set
            {
                var newTime = Mathf.Clamp(value, 0, duration);
                if (Math.Abs(newTime - currentTime) > 0.0001f)
                {
                    currentTime = newTime;
                    UpdateTimeDisplay();
                    UpdateScrubberPosition();
                    OnTimeChanged?.Invoke(currentTime);
                }
            }
        }

        /// <summary>
        /// Current time as normalized value (0-1).
        /// </summary>
        public float NormalizedTime
        {
            get => duration > 0 ? currentTime / duration : 0;
            set => CurrentTime = value * duration;
        }

        /// <summary>
        /// Whether the timeline is currently playing.
        /// </summary>
        public bool IsPlaying
        {
            get => isPlaying;
            set
            {
                if (isPlaying != value)
                {
                    isPlaying = value;
                    UpdatePlayButton();
                    OnPlayStateChanged?.Invoke(isPlaying);
                }
            }
        }

        /// <summary>
        /// Whether the timeline should loop.
        /// </summary>
        public bool IsLooping
        {
            get => isLooping;
            set
            {
                isLooping = value;
                UpdateLoopIndicator();
            }
        }

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
            get => Mathf.RoundToInt(currentTime * frameRate);
            set => CurrentTime = value / frameRate;
        }

        /// <summary>
        /// Total number of frames in the timeline.
        /// </summary>
        public int TotalFrames => Mathf.Max(1, Mathf.RoundToInt(duration * frameRate));

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
            playButton.text = "▶";
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

            // Register for drag events
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            
            // Register for keyboard events
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            focusable = true;
            
            // Update frame ticks when layout changes
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // Initial state
            UpdatePlayButton();
            UpdateTimeDisplay();
        }
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    // Previous frame
                    CurrentFrame = Mathf.Max(0, CurrentFrame - 1);
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    // Next frame
                    CurrentFrame = Mathf.Min(TotalFrames, CurrentFrame + 1);
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Home:
                    // Go to start
                    CurrentTime = 0;
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.End:
                    // Go to end
                    CurrentTime = duration;
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Space:
                    // Toggle play/pause (reuse button logic for consistent behavior)
                    OnPlayButtonClicked();
                    evt.StopPropagation();
                    break;
            }
        }
        
        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateFrameTicks();
            UpdateMarkerPositions();
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

        #region Input Handling

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            // Check if clicking on track area
            var localPos = evt.localPosition;
            var trackBounds = trackContainer.worldBound;
            
            if (trackContainer.ContainsPoint(trackContainer.WorldToLocal(evt.position)))
            {
                isDragging = true;
                this.CapturePointer(evt.pointerId);
                UpdateTimeFromPosition(evt.localPosition);
                evt.StopPropagation();
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!isDragging) return;
            UpdateTimeFromPosition(evt.localPosition);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!isDragging) return;
            
            isDragging = false;
            this.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            isDragging = false;
        }

        private void UpdateTimeFromPosition(Vector2 localPosition)
        {
            var trackBounds = trackContainer.contentRect;
            var worldPos = this.LocalToWorld(localPosition);
            var trackLocalPos = trackContainer.WorldToLocal(worldPos);
            
            // Calculate normalized position within track
            var normalizedX = Mathf.Clamp01(trackLocalPos.x / trackBounds.width);
            NormalizedTime = normalizedX;
        }

        #endregion

        #region Playback

        private void OnPlayButtonClicked()
        {
            // If at the end and not looping, restart from beginning when pressing play
            if (!isPlaying && !isLooping && currentTime >= duration - 0.001f)
            {
                CurrentTime = 0;
            }
            
            IsPlaying = !IsPlaying;
        }

        /// <summary>
        /// Advances the timeline by deltaTime. Call from Update loop.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isPlaying) return;

            var newTime = currentTime + deltaTime;
            
            if (newTime >= duration)
            {
                if (isLooping)
                {
                    newTime = newTime % duration;
                }
                else
                {
                    newTime = duration;
                    IsPlaying = false;
                }
            }

            CurrentTime = newTime;
        }

        /// <summary>
        /// Resets the timeline to the beginning.
        /// </summary>
        public void Reset()
        {
            CurrentTime = 0;
            IsPlaying = false;
        }

        #endregion

        #region UI Updates

        private void UpdateTimeDisplay()
        {
            if (timeLabel == null) return;
            timeLabel.text = $"{currentTime:F2}s / {duration:F2}s";
            
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
            playButton.text = isPlaying ? "❚❚" : "▶";
            playButton.tooltip = isPlaying ? "Pause" : "Play";
        }

        private void UpdateLoopIndicator()
        {
            // Could add a visual indicator for loop state
            EnableInClassList("timeline-looping", isLooping);
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
