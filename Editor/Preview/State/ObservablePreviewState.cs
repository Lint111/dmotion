using System;
using System.Collections.Generic;
using System.ComponentModel;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Observable state for a single animation preview (state or transition).
    /// Properties automatically fire PropertyChanged events when modified.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <code>
    /// var state = new ObservablePreviewState();
    /// state.PropertyChanged += (sender, e) =>
    /// {
    ///     switch (e.PropertyName)
    ///     {
    ///         case nameof(ObservablePreviewState.NormalizedTime):
    ///             UpdateTimeline(state.NormalizedTime);
    ///             break;
    ///         case nameof(ObservablePreviewState.BlendPosition):
    ///             UpdateBlendSpace(state.BlendPosition);
    ///             break;
    ///     }
    /// };
    /// 
    /// // Setting properties fires events automatically
    /// state.NormalizedTime = 0.5f;
    /// state.BlendPosition = new float2(0.3f, 0.7f);
    /// </code>
    /// </remarks>
    public class ObservablePreviewState : ObservableObject
    {
        #region Backing Fields
        
        private AnimationStateAsset _selectedState;
        private AnimationStateAsset _transitionFrom;
        private AnimationStateAsset _transitionTo;
        private bool _isTransitionMode;
        
        private float _normalizedTime;
        private float _transitionProgress;
        private float _fromStateTime;
        private float _toStateTime;
        
        private bool _isPlaying;
        private bool _isLooping = true;
        private float _playbackSpeed = 1f;
        
        private float2 _blendPosition;
        private float2 _toBlendPosition;
        private int _soloClipIndex = -1;
        
        #endregion
        
        #region Selection Properties
        
        /// <summary>
        /// Currently selected state (null if in transition mode).
        /// </summary>
        public AnimationStateAsset SelectedState
        {
            get => _selectedState;
            set => SetProperty(ref _selectedState, value);
        }
        
        /// <summary>
        /// From state for transition preview.
        /// </summary>
        public AnimationStateAsset TransitionFrom
        {
            get => _transitionFrom;
            set => SetProperty(ref _transitionFrom, value);
        }
        
        /// <summary>
        /// To state for transition preview.
        /// </summary>
        public AnimationStateAsset TransitionTo
        {
            get => _transitionTo;
            set => SetProperty(ref _transitionTo, value);
        }
        
        /// <summary>
        /// Whether currently previewing a transition (true) or single state (false).
        /// </summary>
        public bool IsTransitionMode
        {
            get => _isTransitionMode;
            set => SetProperty(ref _isTransitionMode, value);
        }
        
        /// <summary>
        /// Gets the effective target state for preview.
        /// </summary>
        public AnimationStateAsset EffectiveState => IsTransitionMode ? TransitionTo : SelectedState;
        
        #endregion
        
        #region Time Properties
        
        /// <summary>
        /// Primary normalized time (0-1).
        /// For states: the state's playback position.
        /// For transitions: the from-state's playback position.
        /// </summary>
        public float NormalizedTime
        {
            get => _normalizedTime;
            set => SetProperty(ref _normalizedTime, Mathf.Clamp01(value));
        }
        
        /// <summary>
        /// Transition progress (0-1).
        /// 0 = fully in from-state, 1 = fully in to-state.
        /// </summary>
        public float TransitionProgress
        {
            get => _transitionProgress;
            set => SetProperty(ref _transitionProgress, Mathf.Clamp01(value));
        }
        
        /// <summary>
        /// From-state normalized time during transition.
        /// </summary>
        public float FromStateTime
        {
            get => _fromStateTime;
            set => SetProperty(ref _fromStateTime, Mathf.Clamp01(value));
        }
        
        /// <summary>
        /// To-state normalized time during transition.
        /// </summary>
        public float ToStateTime
        {
            get => _toStateTime;
            set => SetProperty(ref _toStateTime, Mathf.Clamp01(value));
        }
        
        #endregion
        
        #region Playback Properties
        
        /// <summary>
        /// Whether playback is currently active.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }
        
        /// <summary>
        /// Whether playback should loop.
        /// </summary>
        public bool IsLooping
        {
            get => _isLooping;
            set => SetProperty(ref _isLooping, value);
        }
        
        /// <summary>
        /// Playback speed multiplier.
        /// </summary>
        public float PlaybackSpeed
        {
            get => _playbackSpeed;
            set => SetProperty(ref _playbackSpeed, Mathf.Max(0.01f, value));
        }
        
        #endregion
        
        #region Blend Properties
        
        /// <summary>
        /// Primary blend position (for single state, or from-state in transitions).
        /// X = 1D blend value or 2D X-axis.
        /// Y = 2D Y-axis (0 for 1D blends).
        /// </summary>
        public float2 BlendPosition
        {
            get => _blendPosition;
            set => SetProperty(ref _blendPosition, value, Float2Comparer.Instance);
        }
        
        /// <summary>
        /// To-state blend position (for transitions only).
        /// </summary>
        public float2 ToBlendPosition
        {
            get => _toBlendPosition;
            set => SetProperty(ref _toBlendPosition, value, Float2Comparer.Instance);
        }
        
        /// <summary>
        /// Solo clip index. -1 = blended (normal), >= 0 = solo specific clip.
        /// </summary>
        public int SoloClipIndex
        {
            get => _soloClipIndex;
            set => SetProperty(ref _soloClipIndex, value);
        }
        
        /// <summary>
        /// Whether a specific clip is soloed.
        /// </summary>
        public bool IsSoloing => SoloClipIndex >= 0;
        
        #endregion
        
        #region State Management
        
        /// <summary>
        /// Selects a state for preview.
        /// </summary>
        public void SelectState(AnimationStateAsset state)
        {
            using (SuppressNotifications())
            {
                IsTransitionMode = false;
                TransitionFrom = null;
                TransitionTo = null;
                TransitionProgress = 0f;
            }
            
            SelectedState = state;
            ResetPlayback();
        }
        
        /// <summary>
        /// Selects a transition for preview.
        /// </summary>
        public void SelectTransition(AnimationStateAsset from, AnimationStateAsset to)
        {
            using (SuppressNotifications())
            {
                SelectedState = null;
                TransitionFrom = from;
                TransitionTo = to;
                TransitionProgress = 0f;
            }
            
            IsTransitionMode = true;
            ResetPlayback();
        }
        
        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection()
        {
            SelectedState = null;
            TransitionFrom = null;
            TransitionTo = null;
            IsTransitionMode = false;
            ResetPlayback();
        }
        
        /// <summary>
        /// Resets playback to initial state.
        /// </summary>
        public void ResetPlayback()
        {
            NormalizedTime = 0f;
            FromStateTime = 0f;
            ToStateTime = 0f;
            TransitionProgress = 0f;
            IsPlaying = false;
        }
        
        /// <summary>
        /// Toggles playback state.
        /// </summary>
        public void TogglePlayback()
        {
            IsPlaying = !IsPlaying;
        }
        
        /// <summary>
        /// Resets blend position to center.
        /// </summary>
        public void ResetBlendPosition()
        {
            BlendPosition = float2.zero;
            ToBlendPosition = float2.zero;
        }
        
        #endregion
        
        #region Utility
        
        /// <summary>
        /// Creates a snapshot of the current state.
        /// </summary>
        public PreviewStateSnapshot CreateSnapshot()
        {
            return new PreviewStateSnapshot
            {
                SelectedState = SelectedState,
                TransitionFrom = TransitionFrom,
                TransitionTo = TransitionTo,
                IsTransitionMode = IsTransitionMode,
                NormalizedTime = NormalizedTime,
                TransitionProgress = TransitionProgress,
                FromStateTime = FromStateTime,
                ToStateTime = ToStateTime,
                BlendPosition = BlendPosition,
                ToBlendPosition = ToBlendPosition,
                SoloClipIndex = SoloClipIndex
            };
        }
        
        /// <summary>
        /// Restores state from a snapshot.
        /// </summary>
        public void RestoreSnapshot(PreviewStateSnapshot snapshot)
        {
            SelectedState = snapshot.SelectedState;
            TransitionFrom = snapshot.TransitionFrom;
            TransitionTo = snapshot.TransitionTo;
            IsTransitionMode = snapshot.IsTransitionMode;
            NormalizedTime = snapshot.NormalizedTime;
            TransitionProgress = snapshot.TransitionProgress;
            FromStateTime = snapshot.FromStateTime;
            ToStateTime = snapshot.ToStateTime;
            BlendPosition = snapshot.BlendPosition;
            ToBlendPosition = snapshot.ToBlendPosition;
            SoloClipIndex = snapshot.SoloClipIndex;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Snapshot of preview state for save/restore operations.
    /// </summary>
    public struct PreviewStateSnapshot
    {
        public AnimationStateAsset SelectedState;
        public AnimationStateAsset TransitionFrom;
        public AnimationStateAsset TransitionTo;
        public bool IsTransitionMode;
        public float NormalizedTime;
        public float TransitionProgress;
        public float FromStateTime;
        public float ToStateTime;
        public float2 BlendPosition;
        public float2 ToBlendPosition;
        public int SoloClipIndex;
    }
    
    /// <summary>
    /// Equality comparer for float2 that handles floating point comparison.
    /// </summary>
    internal class Float2Comparer : IEqualityComparer<float2>
    {
        public static readonly Float2Comparer Instance = new();
        
        private const float Epsilon = 0.0001f;
        
        public bool Equals(float2 x, float2 y)
        {
            return math.abs(x.x - y.x) < Epsilon && math.abs(x.y - y.y) < Epsilon;
        }
        
        public int GetHashCode(float2 obj)
        {
            return obj.GetHashCode();
        }
    }
}
