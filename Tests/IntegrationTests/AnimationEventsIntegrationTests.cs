using System.Collections;
using DMotion.Authoring;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Integration tests for animation events using real SmartBlobber-baked ACL clip data.
    ///
    /// This tests the looping event case that requires SkeletonClip.duration access.
    /// This use case cannot be tested in unit tests because it requires real ACL-compressed data.
    /// </summary>
    public class AnimationEventsIntegrationTests : IntegrationTestBase
    {
        private float[] eventTimes = new[] { 0.2f, 0.5f };

        protected override System.Type[] SystemTypes => new[]
        {
            typeof(AnimationEventsSystem)
        };

        [UnityTest]
        public IEnumerator Raise_When_EventTime_BetweenCurrentAndPreviousTime_WithRealACLData()
        {
            yield return null;

            // This test validates events are raised when the clip loops.
            // Example: EventTime = 0.1f, Previous Time = 0.9f, Time = 0.2f
            // The event should fire because we've looped past it.

            CreateEntityWithClipPlayingRealClips(out var entity, out var samplerIndex, out var events);
            UpdateWorld();

            AssertNoRaisedEvents(entity);

            var eventToRaise = events[0];

            // Get actual clip duration from real baked ACL data
            var clipDuration = GetClipDuration(0);
            Assert.Greater(clipDuration, 0, "Clip duration should be positive from real ACL data");

            // Set up loop scenario: previous time near end, current time after loop
            var prevTime = clipDuration - math.EPSILON;
            var time = eventToRaise.ClipTime + math.EPSILON;

            Assert.Greater(prevTime, time,
                "This test expects Previous time is greater than Clip Time (clip loops)");
            Assert.Less(time, clipDuration,
                "Event is too near the end of the clip");

            SetTimeAndPreviousTimeForSampler(entity, samplerIndex, prevTime, time);

            UpdateWorld();

            AssertEventRaised(entity, eventToRaise.EventHash);
        }

        private void CreateEntityWithClipPlayingRealClips(out Entity entity, out int samplerIndex,
            out AnimationClipEvent[] events)
        {
            entity = CreateEntity();
            AnimationStateMachineConversionUtils.AddSingleClipStateComponents(manager, entity, entity,
                true, false, RootMotionMode.Disabled);

            // Get the actual clip duration from the real baked clips
            var realClipDuration = clipsBlob.Value.clips[0].duration;
            Assert.Greater(realClipDuration, 0, "Real clip duration should be positive");

            // Create animation clip asset with events
            // IMPORTANT: The clip length must match the real ACL clip duration because
            // ClipEventsAuthoringUtils.CreateClipEventsBlob calculates ClipTime as NormalizedTime * clip.length
            var animationClipAsset = ScriptableObject.CreateInstance<AnimationClipAsset>();
            {
                var animationEventName1 = ScriptableObject.CreateInstance<AnimationEventName>();
                var animationEventName2 = ScriptableObject.CreateInstance<AnimationEventName>();
                animationEventName1.name = "Event1";
                animationEventName2.name = "Event2";
                animationClipAsset.Events = new[]
                {
                    new Authoring.AnimationClipEvent { Name = animationEventName1, NormalizedTime = eventTimes[0] },
                    new Authoring.AnimationClipEvent { Name = animationEventName2, NormalizedTime = eventTimes[1] }
                };

                // Create a clip with the same duration as the real ACL clip
                var clip = new AnimationClip();
                var curve = AnimationCurve.Linear(0, 0, realClipDuration, 1);
                clip.SetCurve("", typeof(Transform), "localPosition.x", curve);
                clip.name = "Move";
                clip.legacy = true;
                animationClipAsset.Clip = clip;
            }

            var clipEventsBlob = ClipEventsAuthoringUtils.CreateClipEventsBlob(new[] { animationClipAsset });
            TrackBlob(clipEventsBlob);

            Assert.AreEqual(1, clipEventsBlob.Value.ClipEvents.Length);
            Assert.AreEqual(2, clipEventsBlob.Value.ClipEvents[0].Events.Length);
            events = clipEventsBlob.Value.ClipEvents[0].Events.ToArray();
            
            // Verify the event ClipTimes are within the clip duration
            Assert.Less(events[0].ClipTime, realClipDuration, 
                $"Event 0 ClipTime ({events[0].ClipTime}) should be less than clip duration ({realClipDuration})");

            // Use real baked clips blob from pre-baked scene
            var singleState = AnimationStateTestUtils.CreateSingleClipStateWithRealClips(
                manager, entity, clipsBlob, clipEventsBlob);
            AnimationStateTestUtils.SetCurrentState(manager, entity, singleState.AnimationStateId);

            var samplers = manager.GetBuffer<ClipSampler>(entity);
            Assert.AreEqual(1, samplers.Length);
            samplerIndex = 0;
        }

        private void SetTimeAndPreviousTimeForSampler(Entity entity, int samplerIndex, float prevTime, float time)
        {
            var clipSamplers = manager.GetBuffer<ClipSampler>(entity);
            var clipSampler = clipSamplers[samplerIndex];
            clipSampler.PreviousTime = prevTime;
            clipSampler.Time = time;
            // Ensure sampler has weight > 0 so events can be raised
            // (Normally UpdateAnimationStatesSystem sets this from AnimationState.Weight)
            if (mathex.iszero(clipSampler.Weight))
            {
                clipSampler.Weight = 1.0f;
            }
            clipSamplers[samplerIndex] = clipSampler;
        }

        private void AssertNoRaisedEvents(Entity entity)
        {
            var raisedEvents = manager.GetBuffer<RaisedAnimationEvent>(entity);
            Assert.IsEmpty(raisedEvents.AsNativeArray().ToArray(), "Expected no raised events");
        }

        private void AssertEventRaised(Entity entity, int eventHash)
        {
            var raisedEvents = manager.GetBuffer<RaisedAnimationEvent>(entity);
            Assert.IsNotEmpty(raisedEvents.AsNativeArray().ToArray(), "Expected at least one raised event");

            bool found = false;
            foreach (var e in raisedEvents)
            {
                if (e.EventHash == eventHash)
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, $"Expected event with hash {eventHash} to be raised");
        }
    }
}
