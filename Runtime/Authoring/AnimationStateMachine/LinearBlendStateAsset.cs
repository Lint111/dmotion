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