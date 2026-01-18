using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Authoring
{
    [CreateAssetMenu(fileName = "New Directional 2D Blend State", menuName = "DMotion/Directional 2D Blend State")]
    public class Directional2DBlendStateAsset : AnimationStateAsset
    {
        [Serializable]
        public struct BlendClipData
        {
            public AnimationClip clip;
            public float2 position;
            public float speedMultiplier;
        }

        public FloatParameterAsset BlendParameterX;
        public FloatParameterAsset BlendParameterY;
        public List<BlendClipData> clips = new List<BlendClipData>();

        private void OnValidate()
        {
            // Ensure speed is never 0 or negative
            for (int i = 0; i < clips.Count; i++)
            {
                var data = clips[i];
                if (data.speedMultiplier <= 0)
                {
                    data.speedMultiplier = 1f;
                    clips[i] = data;
                }
            }
        }
    }
}
