using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for timeline controls providing shared playback state and logic.
    /// Subclasses implement specific visual representations (state timeline, transition timeline, etc.).
    /// </summary>
    [UxmlElement]
    internal abstract partial class TimelineBase : VisualElement
    {
        #region Constants
        
        /// <summary>Minimum duration to prevent division by zero.</summary>
        protected const float MinDuration = 0.001f;
        
        /// <summary>Default playback speed.</summary>
        protected const float DefaultPlaybackSpeed = 1f;
        
        /// <summary>Minimum playback speed.</summary>
        protected const float MinPlaybackSpeed = 0.01f;
        
        #endregion
        
        #region State
        
        private float duration = 1f;
        private float currentTime;
        private float playbackSpeed = DefaultPlaybackSpeed;
        private bool isPlaying;
        private bool isLooping;
        private bool isDragging;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when the current time changes (from scrubbing or playback).
        /// Parameter: current time in seconds.
        /// </summary>
        public event Action<float> OnTimeChanged;
        
        /// <summary>
        /// Fired when play/pause state changes.
        /// Parameter: is playing.
        /// </summary>
        public event Action<bool> OnPlayStateChanged;
        
        /// <summary>
        /// Fired when loop state changes.
        /// Parameter: is looping.
        /// </summary>
        public event Action<bool> OnLoopStateChanged;
        
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
                var newDuration = Mathf.Max(MinDuration, value);
                if (Math.Abs(newDuration - duration) > 0.0001f)
                {
                    duration = newDuration;
                    OnDurationChanged();
                }
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
                    OnCurrentTimeChanged();
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
                    OnPlayingStateChanged();
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
                if (isLooping != value)
                {
                    isLooping = value;
                    OnLoopingStateChanged();
                    OnLoopStateChanged?.Invoke(isLooping);
                }
            }
        }
        
        /// <summary>
        /// Playback speed multiplier (1.0 = normal, 2.0 = double speed, 0.5 = half speed).
        /// </summary>
        public float PlaybackSpeed
        {
            get => playbackSpeed;
            set => playbackSpeed = Mathf.Max(MinPlaybackSpeed, value);
        }
        
        /// <summary>
        /// Whether the user is currently dragging the scrubber.
        /// Use this to suppress other input handling (e.g., camera controls).
        /// </summary>
        public bool IsDragging
        {
            get => isDragging;
            protected set => isDragging = value;
        }
        
        #endregion
        
        #region Constructor
        
        protected TimelineBase()
        {
            AddToClassList("timeline-base");
            focusable = true;
            
            // Register for input events
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Advances the timeline by deltaTime. Call from Update loop.
        /// Applies PlaybackSpeed multiplier to control animation speed.
        /// </summary>
        /// <returns>True if the timeline is still playing and needs continued updates.</returns>
        public bool Tick(float deltaTime)
        {
            if (!isPlaying) return false;
            
            var newTime = currentTime + deltaTime * playbackSpeed;
            
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
            return isPlaying;
        }
        
        /// <summary>
        /// Starts or resumes playback.
        /// </summary>
        public void Play()
        {
            // If at the end and not looping, restart from beginning
            if (!isLooping && currentTime >= duration - 0.001f)
            {
                CurrentTime = 0;
            }
            IsPlaying = true;
        }
        
        /// <summary>
        /// Pauses playback.
        /// </summary>
        public void Pause()
        {
            IsPlaying = false;
        }
        
        /// <summary>
        /// Toggles play/pause state.
        /// </summary>
        public void TogglePlayPause()
        {
            if (isPlaying)
                Pause();
            else
                Play();
        }
        
        /// <summary>
        /// Resets the timeline to the beginning and stops playback.
        /// </summary>
        public void Reset()
        {
            CurrentTime = 0;
            IsPlaying = false;
        }
        
        /// <summary>
        /// Steps forward by one frame (based on frame rate).
        /// </summary>
        public void StepForward(float frameRate = 30f)
        {
            CurrentTime = Mathf.Min(duration, currentTime + 1f / frameRate);
        }
        
        /// <summary>
        /// Steps backward by one frame (based on frame rate).
        /// </summary>
        public void StepBackward(float frameRate = 30f)
        {
            CurrentTime = Mathf.Max(0, currentTime - 1f / frameRate);
        }
        
        /// <summary>
        /// Jumps to the start of the timeline.
        /// </summary>
        public void GoToStart()
        {
            CurrentTime = 0;
        }
        
        /// <summary>
        /// Jumps to the end of the timeline.
        /// </summary>
        public void GoToEnd()
        {
            CurrentTime = duration;
        }
        
        #endregion
        
        #region Protected - Virtual Methods for Subclasses
        
        /// <summary>
        /// Called when duration changes. Override to update visuals.
        /// </summary>
        protected virtual void OnDurationChanged()
        {
            MarkDirtyRepaint();
        }
        
        /// <summary>
        /// Called when current time changes. Override to update scrubber position.
        /// </summary>
        protected virtual void OnCurrentTimeChanged()
        {
            MarkDirtyRepaint();
        }
        
        /// <summary>
        /// Called when play/pause state changes. Override to update play button.
        /// </summary>
        protected virtual void OnPlayingStateChanged()
        {
            MarkDirtyRepaint();
        }
        
        /// <summary>
        /// Called when loop state changes. Override to update loop indicator.
        /// </summary>
        protected virtual void OnLoopingStateChanged()
        {
            MarkDirtyRepaint();
        }
        
        /// <summary>
        /// Called when geometry changes. Override to update layout-dependent visuals.
        /// </summary>
        protected virtual void OnGeometryChanged(GeometryChangedEvent evt)
        {
            MarkDirtyRepaint();
        }
        
        /// <summary>
        /// Override to define the draggable track area for scrubbing.
        /// Return the rect in local coordinates.
        /// </summary>
        protected abstract Rect GetTrackRect();
        
        /// <summary>
        /// Converts a local position within the track to a normalized time (0-1).
        /// </summary>
        protected virtual float PositionToNormalizedTime(Vector2 localPosition)
        {
            var trackRect = GetTrackRect();
            if (trackRect.width <= 0) return 0;
            
            var relativeX = localPosition.x - trackRect.x;
            return Mathf.Clamp01(relativeX / trackRect.width);
        }
        
        #endregion
        
        #region Private - Input Handling
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            
            var trackRect = GetTrackRect();
            if (trackRect.Contains(evt.localPosition))
            {
                // Take focus so keyboard shortcuts work
                Focus();
                
                isDragging = true;
                this.CapturePointer(evt.pointerId);
                NormalizedTime = PositionToNormalizedTime(evt.localPosition);
                evt.StopPropagation();
            }
        }
        
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!isDragging) return;
            NormalizedTime = PositionToNormalizedTime(evt.localPosition);
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
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    TogglePlayPause();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.LeftArrow:
                    StepBackward();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    StepForward();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Home:
                    GoToStart();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.End:
                    GoToEnd();
                    evt.StopPropagation();
                    break;
            }
        }
        
        #endregion
    }
}
