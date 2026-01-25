using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DMotion.Authoring
{
    [Serializable]
    public struct ClipWithThreshold
    {
        public AnimationClipAsset Clip;
        public float Threshold;
        public float Speed;
    }
    
    public class LinearBlendStateAsset : AnimationStateAsset
    {
        public ClipWithThreshold[] BlendClips = Array.Empty<ClipWithThreshold>();
        
        [Tooltip("The parameter used to control blending. Can be Float or Int.")]
        public AnimationParameterAsset BlendParameter;
        
        [Tooltip("For Int parameters: minimum value of the range (maps to 0.0)")]
        public int IntRangeMin = 0;
        
        [Tooltip("For Int parameters: maximum value of the range (maps to 1.0)")]
        public int IntRangeMax = 10;
        
        /// <summary>
        /// Returns the Float parameter if assigned, for backwards compatibility.
        /// </summary>
        public FloatParameterAsset FloatBlendParameter => BlendParameter as FloatParameterAsset;
        
        /// <summary>
        /// Returns the Int parameter if assigned.
        /// </summary>
        public IntParameterAsset IntBlendParameter => BlendParameter as IntParameterAsset;
        
        /// <summary>
        /// Whether the blend parameter is an Int (requires range normalization).
        /// </summary>
        public bool UsesIntParameter => BlendParameter is IntParameterAsset;
        
        /// <summary>
        /// Returns true if the Int range is valid (Max > Min).
        /// </summary>
        public bool HasValidIntRange => IntRangeMax > IntRangeMin;
        
        public override StateType Type => StateType.LinearBlend;
        public override int ClipCount => BlendClips.Length;
        public override IEnumerable<AnimationClipAsset> Clips => BlendClips.Select(b => b.Clip);
        
        /// <summary>
        /// Gets the effective speed at the given blend position.
        /// Calculates weighted average of clip speeds based on 1D blend weights.
        /// </summary>
        public override float GetEffectiveSpeed(Vector2 blendPosition)
        {
            if (BlendClips == null || BlendClips.Length == 0)
                return Speed;
            
            float blendValue = blendPosition.x;
            
            // Find the two clips we're blending between
            int lowerIndex = -1, upperIndex = -1;
            
            for (int i = 0; i < BlendClips.Length; i++)
            {
                float threshold = BlendClips[i].Threshold;
                if (threshold <= blendValue)
                    lowerIndex = i;
                if (threshold >= blendValue && upperIndex == -1)
                    upperIndex = i;
            }
            
            // Handle edge cases
            if (lowerIndex == -1) lowerIndex = 0;
            if (upperIndex == -1) upperIndex = BlendClips.Length - 1;
            
            if (lowerIndex == upperIndex)
            {
                // Exactly on a threshold or outside range
                float clipSpeed = BlendClips[lowerIndex].Speed > 0 ? BlendClips[lowerIndex].Speed : 1f;
                return Speed * clipSpeed;
            }
            
            // Interpolate between the two clips
            float lowerThreshold = BlendClips[lowerIndex].Threshold;
            float upperThreshold = BlendClips[upperIndex].Threshold;
            float range = upperThreshold - lowerThreshold;
            
            float lowerSpeed = BlendClips[lowerIndex].Speed > 0 ? BlendClips[lowerIndex].Speed : 1f;
            float upperSpeed = BlendClips[upperIndex].Speed > 0 ? BlendClips[upperIndex].Speed : 1f;
            
            if (range > 0.0001f)
            {
                float t = (blendValue - lowerThreshold) / range;
                return Speed * Mathf.Lerp(lowerSpeed, upperSpeed, t);
            }
            
            return Speed * lowerSpeed;
        }
        
        /// <summary>
        /// Gets the effective duration at the given blend position.
        /// Interpolates between clip durations based on 1D blend weights.
        /// </summary>
        public override float GetEffectiveDuration(Vector2 blendPosition)
        {
            if (BlendClips == null || BlendClips.Length == 0)
                return 1f;
            
            float blendValue = blendPosition.x;
            
            // Find the two clips we're blending between
            int lowerIndex = -1, upperIndex = -1;
            
            for (int i = 0; i < BlendClips.Length; i++)
            {
                float threshold = BlendClips[i].Threshold;
                if (threshold <= blendValue)
                    lowerIndex = i;
                if (threshold >= blendValue && upperIndex == -1)
                    upperIndex = i;
            }
            
            // Handle edge cases
            if (lowerIndex == -1) lowerIndex = 0;
            if (upperIndex == -1) upperIndex = BlendClips.Length - 1;
            
            float lowerDuration = BlendClips[lowerIndex].Clip?.Clip != null ? BlendClips[lowerIndex].Clip.Clip.length : 1f;
            float upperDuration = BlendClips[upperIndex].Clip?.Clip != null ? BlendClips[upperIndex].Clip.Clip.length : 1f;
            
            if (lowerIndex == upperIndex)
                return lowerDuration;
            
            // Interpolate between the two clips
            float lowerThreshold = BlendClips[lowerIndex].Threshold;
            float upperThreshold = BlendClips[upperIndex].Threshold;
            float range = upperThreshold - lowerThreshold;
            
            if (range > 0.0001f)
            {
                float t = (blendValue - lowerThreshold) / range;
                return Mathf.Lerp(lowerDuration, upperDuration, t);
            }
            
            return lowerDuration;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure Int range is valid to prevent division by zero at runtime
            if (IntRangeMax <= IntRangeMin)
            {
                Debug.LogWarning($"[LinearBlendStateAsset] '{name}': IntRangeMax ({IntRangeMax}) must be greater than IntRangeMin ({IntRangeMin}). Adjusting.");
                IntRangeMax = IntRangeMin + 1;
            }
        }
#endif
    }
}