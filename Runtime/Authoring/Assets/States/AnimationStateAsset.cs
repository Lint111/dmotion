using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMotion.Authoring
{
    public abstract class StateMachineSubAsset : ScriptableObject{}
    public abstract class AnimationStateAsset : StateMachineSubAsset
    {
        public bool Loop = true;
        public float Speed = 1;

        [Tooltip("Optional parameter to multiply speed at runtime. If null, uses constant Speed value.")]
        public FloatParameterAsset SpeedParameter;

        public List<StateOutTransition> OutTransitions = new(); 

        public abstract StateType Type { get; }
        public abstract int ClipCount { get; }
        public abstract IEnumerable<AnimationClipAsset> Clips { get; }
        
        /// <summary>
        /// Gets the effective playback speed for this state.
        /// For blend states, this accounts for the weighted clip speeds at the given blend position.
        /// </summary>
        /// <param name="blendPosition">The blend position (x for 1D, xy for 2D). Ignored for non-blend states.</param>
        /// <returns>The combined speed (state.Speed × weighted clip speed)</returns>
        public virtual float GetEffectiveSpeed(Vector2 blendPosition) => Speed;
        
        /// <summary>
        /// Gets the effective duration for this state at the given blend position.
        /// For blend states, this is the weighted average of clip durations.
        /// </summary>
        /// <param name="blendPosition">The blend position (x for 1D, xy for 2D). Ignored for non-blend states.</param>
        /// <returns>The weighted duration in seconds</returns>
        public virtual float GetEffectiveDuration(Vector2 blendPosition)
        {
            foreach (var clip in Clips)
            {
                if (clip?.Clip != null)
                    return clip.Clip.length;
            }
            return 1f;
        }

    #if UNITY_EDITOR
        [Serializable]
        internal struct EditorData
        {
            [SerializeField]
            internal Vector2 GraphPosition;
            
            [SerializeField]
            internal string Guid;
        }

        [SerializeField, HideInInspector]
        internal EditorData StateEditorData;
    #endif
    }
}