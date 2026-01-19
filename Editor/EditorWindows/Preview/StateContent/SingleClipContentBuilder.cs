using System.Collections.Generic;
using DMotion.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Builds inspector content for SingleClipStateAsset.
    /// Shows clip info (name, duration, frame rate).
    /// </summary>
    internal class SingleClipContentBuilder : IStateContentBuilder
    {
        private SingleClipStateAsset state;
        
        // Cached for timeline configuration
        private readonly List<(float normalizedTime, string name)> cachedEventMarkers = new(32);
        
        public void Build(VisualElement container, StateContentContext context)
        {
            state = context.State as SingleClipStateAsset;
            if (state == null) return;
            
            var clipSection = context.CreateSection("Clip Info");
            
            var clipName = state.Clip?.name ?? "(none)";
            clipSection.Add(context.CreatePropertyRow("Clip", clipName));
            
            var animClip = state.Clip?.Clip;
            if (animClip != null)
            {
                clipSection.Add(context.CreatePropertyRow("Duration", $"{animClip.length:F2}s"));
                clipSection.Add(context.CreatePropertyRow("Frame Rate", $"{animClip.frameRate} fps"));
            }
            
            container.Add(clipSection);
        }
        
        public void ConfigureTimeline(TimelineScrubber scrubber, StateContentContext context)
        {
            state = context.State as SingleClipStateAsset;
            if (state == null) return;
            
            float duration = 1f;
            float frameRate = 30f;
            cachedEventMarkers.Clear();
            
            var clip = state.Clip?.Clip;
            if (clip != null)
            {
                duration = clip.length;
                frameRate = clip.frameRate;
                CollectAnimationEvents(clip, cachedEventMarkers);
            }
            
            scrubber.Duration = duration;
            scrubber.FrameRate = frameRate;
            scrubber.SetEventMarkers(cachedEventMarkers);
        }
        
        public void Cleanup()
        {
            state = null;
            cachedEventMarkers.Clear();
        }
        
        private static void CollectAnimationEvents(AnimationClip clip, List<(float normalizedTime, string name)> markers)
        {
            if (clip == null || clip.events == null) return;
            
            foreach (var evt in clip.events)
            {
                var normalizedTime = clip.length > 0 ? evt.time / clip.length : 0;
                markers.Add((normalizedTime, evt.functionName));
            }
        }
    }
}
