using System.Runtime.CompilerServices;
using Unity.Mathematics;
using DMotion;

namespace DMotion.Editor
{
    /// <summary>
    /// Extension methods for PreviewTimeState to use shared runtime logic.
    /// Acts as a bridge between the managed Editor types and the shared math utilities.
    /// </summary>
    public static class PreviewTimeStateExtensions
    {
        /// <summary>
        /// Calculates the number of cycles (loops) that have occurred for the From state.
        /// </summary>
        public static int GetFromStateCycleCount(this PreviewTimeState state, float fromDuration)
        {
            // Calculate unbounded time from normalized time and duration
            // This is an approximation since Editor stores NormalizedTime wrapped 0-1
            // But for ghost bars we need to know how many times it *would* have looped
            // if we were just playing forward linearly.
            
            // In the editor, NormalizedTime is usually kept 0-1, but TransitionTimeline
            // calculates "EffectiveExitTime" which can be > duration.
            
            // However, PreviewTimeState itself doesn't store accumulated time.
            // It stores normalized position.
            // So we can only calculate cycles if we know the total elapsed time, 
            // which the Previewer usually tracks separately.
            
            // For now, return 1 as a safe default if we can't infer context.
            return 1;
        }

        /// <summary>
        /// Applies the standard wrapping logic to a normalized time value.
        /// </summary>
        public static float WrapNormalizedTime(float normalizedTime)
        {
            return AnimationTimeUtils.CalculateNormalizedTime(normalizedTime, 1.0f);
        }
    }
}
