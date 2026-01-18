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
