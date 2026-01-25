using DMotion.Authoring;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Shared utility methods for animation state calculations.
    /// Used by both ECS timeline (TimelineControlHelper) and visual timeline (TransitionTimeline).
    /// </summary>
    public static class AnimationStateUtils
    {
        /// <summary>
        /// Gets the effective duration of an animation state at the given blend position.
        /// Handles all state types: SingleClip, LinearBlend, Directional2D.
        /// </summary>
        /// <param name="state">The animation state asset.</param>
        /// <param name="blendPosition">The blend position (x,y) for blend states.</param>
        /// <returns>The effective duration in seconds, or 1.0 if state is null.</returns>
        public static float GetEffectiveDuration(AnimationStateAsset state, Vector2 blendPosition)
        {
            if (state == null) return 1f;
            return state.GetEffectiveDuration(blendPosition);
        }
        
        /// <summary>
        /// Gets the effective playback speed of an animation state at the given blend position.
        /// Handles all state types: SingleClip, LinearBlend, Directional2D.
        /// </summary>
        /// <param name="state">The animation state asset.</param>
        /// <param name="blendPosition">The blend position (x,y) for blend states.</param>
        /// <returns>The effective speed multiplier, or 1.0 if state is null.</returns>
        public static float GetEffectiveSpeed(AnimationStateAsset state, Vector2 blendPosition)
        {
            if (state == null) return 1f;
            return state.GetEffectiveSpeed(blendPosition);
        }
        
        /// <summary>
        /// Checks if a state is a blend state (LinearBlend or Directional2D).
        /// Blend states have dynamic duration based on blend position, so exit time
        /// adapts to maintain consistent transition duration.
        /// </summary>
        /// <param name="state">The animation state asset to check.</param>
        /// <returns>True if the state is a blend state, false otherwise.</returns>
        public static bool IsBlendState(AnimationStateAsset state)
        {
            return state is LinearBlendStateAsset or Directional2DBlendStateAsset;
        }
    }
}
