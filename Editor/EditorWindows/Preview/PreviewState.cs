using Unity.Mathematics;

namespace DMotion.Editor
{
    /// <summary>
    /// Unified time state for preview playback.
    /// Works for both single state and transition previews.
    /// </summary>
    public struct PreviewTimeState
    {
        /// <summary>
        /// Primary normalized time (0-1).
        /// For states: the state's playback position.
        /// For transitions: the from-state's playback position at transition start.
        /// </summary>
        public float NormalizedTime;
        
        /// <summary>
        /// Transition progress (0-1). 
        /// 0 = fully in from-state, 1 = fully in to-state.
        /// Ignored for single state previews.
        /// </summary>
        public float TransitionProgress;
        
        /// <summary>
        /// From-state normalized time during transition.
        /// Ignored for single state previews.
        /// </summary>
        public float FromStateTime;
        
        /// <summary>
        /// To-state normalized time during transition.
        /// Ignored for single state previews.
        /// </summary>
        public float ToStateTime;
        
        /// <summary>
        /// Whether playback is currently active.
        /// </summary>
        public bool IsPlaying;
        
        /// <summary>
        /// Whether playback should loop.
        /// </summary>
        public bool IsLooping;
        
        /// <summary>
        /// Playback speed multiplier.
        /// </summary>
        public float PlaybackSpeed;
        
        /// <summary>
        /// Default time state for a new preview.
        /// </summary>
        public static PreviewTimeState Default => new()
        {
            NormalizedTime = 0f,
            TransitionProgress = 0f,
            FromStateTime = 0f,
            ToStateTime = 0f,
            IsPlaying = false,
            IsLooping = true,
            PlaybackSpeed = 1f
        };
        
        /// <summary>
        /// Creates a time state for a single state preview.
        /// </summary>
        public static PreviewTimeState ForState(float normalizedTime, bool isPlaying = false, bool isLooping = true)
        {
            return new PreviewTimeState
            {
                NormalizedTime = normalizedTime,
                TransitionProgress = 0f,
                FromStateTime = normalizedTime,
                ToStateTime = 0f,
                IsPlaying = isPlaying,
                IsLooping = isLooping,
                PlaybackSpeed = 1f
            };
        }
        
        /// <summary>
        /// Creates a time state for a transition preview.
        /// </summary>
        public static PreviewTimeState ForTransition(
            float transitionProgress, 
            float fromStateTime, 
            float toStateTime,
            bool isPlaying = false,
            bool isLooping = true)
        {
            return new PreviewTimeState
            {
                NormalizedTime = fromStateTime, // Primary time is from-state
                TransitionProgress = transitionProgress,
                FromStateTime = fromStateTime,
                ToStateTime = toStateTime,
                IsPlaying = isPlaying,
                IsLooping = isLooping,
                PlaybackSpeed = 1f
            };
        }
    }
    
    /// <summary>
    /// Unified parameter state for preview.
    /// Handles blend positions for both states and transitions.
    /// </summary>
    public struct PreviewParameterState
    {
        /// <summary>
        /// Primary blend position (for single state, or from-state in transitions).
        /// X = 1D blend value or 2D X-axis.
        /// Y = 2D Y-axis (0 for 1D blends).
        /// </summary>
        public float2 BlendPosition;
        
        /// <summary>
        /// To-state blend position (for transitions only).
        /// </summary>
        public float2 ToBlendPosition;
        
        /// <summary>
        /// Solo clip index. -1 = blended (normal), >= 0 = solo specific clip.
        /// </summary>
        public int SoloClipIndex;
        
        /// <summary>
        /// Target blend position for smooth interpolation.
        /// </summary>
        public float2 TargetBlendPosition;
        
        /// <summary>
        /// Target to-state blend position for smooth interpolation.
        /// </summary>
        public float2 TargetToBlendPosition;
        
        /// <summary>
        /// Default parameter state.
        /// </summary>
        public static PreviewParameterState Default => new()
        {
            BlendPosition = float2.zero,
            ToBlendPosition = float2.zero,
            SoloClipIndex = -1,
            TargetBlendPosition = float2.zero,
            TargetToBlendPosition = float2.zero
        };
        
        /// <summary>
        /// Creates a parameter state for a single state preview.
        /// </summary>
        public static PreviewParameterState ForState(float2 blendPosition, int soloClip = -1)
        {
            return new PreviewParameterState
            {
                BlendPosition = blendPosition,
                ToBlendPosition = float2.zero,
                SoloClipIndex = soloClip,
                TargetBlendPosition = blendPosition,
                TargetToBlendPosition = float2.zero
            };
        }
        
        /// <summary>
        /// Creates a parameter state for a transition preview.
        /// </summary>
        public static PreviewParameterState ForTransition(float2 fromBlendPosition, float2 toBlendPosition)
        {
            return new PreviewParameterState
            {
                BlendPosition = fromBlendPosition,
                ToBlendPosition = toBlendPosition,
                SoloClipIndex = -1,
                TargetBlendPosition = fromBlendPosition,
                TargetToBlendPosition = toBlendPosition
            };
        }
        
        /// <summary>
        /// Whether blend positions need interpolation.
        /// </summary>
        public bool NeedsInterpolation => 
            math.any(BlendPosition != TargetBlendPosition) || 
            math.any(ToBlendPosition != TargetToBlendPosition);
        
        /// <summary>
        /// Interpolates blend positions toward targets.
        /// </summary>
        /// <param name="speed">Interpolation speed.</param>
        /// <param name="deltaTime">Time since last update.</param>
        /// <returns>True if still interpolating, false if reached targets.</returns>
        public bool Interpolate(float speed, float deltaTime)
        {
            bool stillInterpolating = false;
            
            if (math.any(BlendPosition != TargetBlendPosition))
            {
                var diff = TargetBlendPosition - BlendPosition;
                var maxStep = speed * deltaTime;
                
                if (math.length(diff) <= maxStep)
                {
                    BlendPosition = TargetBlendPosition;
                }
                else
                {
                    BlendPosition += math.normalize(diff) * maxStep;
                    stillInterpolating = true;
                }
            }
            
            if (math.any(ToBlendPosition != TargetToBlendPosition))
            {
                var diff = TargetToBlendPosition - ToBlendPosition;
                var maxStep = speed * deltaTime;
                
                if (math.length(diff) <= maxStep)
                {
                    ToBlendPosition = TargetToBlendPosition;
                }
                else
                {
                    ToBlendPosition += math.normalize(diff) * maxStep;
                    stillInterpolating = true;
                }
            }
            
            return stillInterpolating;
        }
    }
    
    // Note: PreviewSnapshot is defined in IPreviewBackend.cs
    // When migrating to PreviewBackendBase, consider consolidating snapshot types
}
