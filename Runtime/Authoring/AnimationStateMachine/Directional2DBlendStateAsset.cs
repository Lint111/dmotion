using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Authoring
{
    [Serializable]
    public struct Directional2DClipWithPosition
    {
        public AnimationClipAsset Clip;
        public float2 Position;
        public float Speed;
    }
    
    public class Directional2DBlendStateAsset : AnimationStateAsset
    {
        [Tooltip("The X-axis parameter used to control horizontal blending.")]
        public FloatParameterAsset BlendParameterX;
        
        [Tooltip("The Y-axis parameter used to control vertical blending.")]
        public FloatParameterAsset BlendParameterY;
        
        public Directional2DClipWithPosition[] BlendClips = Array.Empty<Directional2DClipWithPosition>();

        public override StateType Type => StateType.Directional2DBlend;
        public override int ClipCount => BlendClips.Length;
        public override IEnumerable<AnimationClipAsset> Clips => BlendClips.Select(b => b.Clip);
        
        /// <summary>
        /// Gets the effective speed at the given blend position.
        /// Calculates weighted average of clip speeds using inverse distance weighting.
        /// </summary>
        public override float GetEffectiveSpeed(Vector2 blendPosition)
        {
            if (BlendClips == null || BlendClips.Length == 0)
                return Speed;
            
            if (BlendClips.Length == 1)
            {
                float clipSpeed = BlendClips[0].Speed > 0 ? BlendClips[0].Speed : 1f;
                return Speed * clipSpeed;
            }
            
            var pos = new float2(blendPosition.x, blendPosition.y);
            
            // Calculate inverse distance weights
            float totalWeight = 0f;
            float weightedSpeed = 0f;
            
            // Check for exact match first
            for (int i = 0; i < BlendClips.Length; i++)
            {
                float dist = math.distance(pos, BlendClips[i].Position);
                if (dist < 0.0001f)
                {
                    float clipSpeed = BlendClips[i].Speed > 0 ? BlendClips[i].Speed : 1f;
                    return Speed * clipSpeed;
                }
            }
            
            // Inverse distance weighting
            for (int i = 0; i < BlendClips.Length; i++)
            {
                float dist = math.distance(pos, BlendClips[i].Position);
                float weight = 1f / (dist * dist + 0.0001f); // Inverse square distance
                float clipSpeed = BlendClips[i].Speed > 0 ? BlendClips[i].Speed : 1f;
                
                weightedSpeed += weight * clipSpeed;
                totalWeight += weight;
            }
            
            if (totalWeight > 0.0001f)
            {
                return Speed * (weightedSpeed / totalWeight);
            }
            
            return Speed;
        }
        
        /// <summary>
        /// Gets the effective duration at the given blend position.
        /// Calculates weighted average of clip durations using inverse distance weighting.
        /// </summary>
        public override float GetEffectiveDuration(Vector2 blendPosition)
        {
            if (BlendClips == null || BlendClips.Length == 0)
                return 1f;
            
            if (BlendClips.Length == 1)
            {
                return BlendClips[0].Clip?.Clip != null ? BlendClips[0].Clip.Clip.length : 1f;
            }
            
            var pos = new float2(blendPosition.x, blendPosition.y);
            
            // Check for exact match first
            for (int i = 0; i < BlendClips.Length; i++)
            {
                float dist = math.distance(pos, BlendClips[i].Position);
                if (dist < 0.0001f)
                {
                    return BlendClips[i].Clip?.Clip != null ? BlendClips[i].Clip.Clip.length : 1f;
                }
            }
            
            // Inverse distance weighting
            float totalWeight = 0f;
            float weightedDuration = 0f;
            
            for (int i = 0; i < BlendClips.Length; i++)
            {
                float dist = math.distance(pos, BlendClips[i].Position);
                float weight = 1f / (dist * dist + 0.0001f);
                float clipDuration = BlendClips[i].Clip?.Clip != null ? BlendClips[i].Clip.Clip.length : 1f;
                
                weightedDuration += weight * clipDuration;
                totalWeight += weight;
            }
            
            if (totalWeight > 0.0001f)
            {
                return weightedDuration / totalWeight;
            }
            
            return 1f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure speed is never 0 or negative
            for (int i = 0; i < BlendClips.Length; i++)
            {
                var data = BlendClips[i];
                if (data.Speed <= 0)
                {
                    data.Speed = 1f;
                    BlendClips[i] = data;
                }
            }
        }
#endif
    }
}
