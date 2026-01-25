using Unity.Entities;
using UnityEngine;

namespace DMotion
{
    /// <summary>
    /// Diagnostic system to verify timeline control is working.
    /// Only runs in editor. Logs sampler state after Apply systems run.
    /// </summary>
#if UNITY_EDITOR
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ApplyStateRenderRequestSystem))]
    [UpdateAfter(typeof(ApplyTransitionRenderRequestSystem))]
    [UpdateBefore(typeof(ClipSamplingSystem))]
    public partial class TimelineControlDiagnosticSystem : SystemBase
    {
        private int frameCount;
        private const int LogInterval = 60; // Log every 60 frames
        
        protected override void OnCreate()
        {
            RequireForUpdate<AnimationScrubberTarget>();
        }
        
        protected override void OnUpdate()
        {
            frameCount++;
            bool shouldLog = frameCount % LogInterval == 0;
            
            foreach (var (scrubber, activeRequest, stateRequest, animStates, samplers, entity) in
                SystemAPI.Query<
                    RefRO<AnimationScrubberTarget>,
                    RefRO<ActiveRenderRequest>,
                    RefRO<AnimationStateRenderRequest>,
                    DynamicBuffer<AnimationState>,
                    DynamicBuffer<ClipSampler>>()
                .WithEntityAccess())
            {
                if (!scrubber.ValueRO.IsActive)
                    continue;
                
                if (shouldLog)
                {
                    Debug.Log($"[TimelineDiag] Entity {entity.Index}: " +
                        $"RequestType={activeRequest.ValueRO.Type}, " +
                        $"StateReq.Valid={stateRequest.ValueRO.IsValid}, " +
                        $"AnimStates={animStates.Length}, " +
                        $"Samplers={samplers.Length}");
                    
                    if (samplers.Length > 0)
                    {
                        float totalWeight = 0;
                        for (int i = 0; i < samplers.Length; i++)
                        {
                            var s = samplers[i];
                            totalWeight += s.Weight;
                            if (s.Weight > 0.01f)
                            {
                                Debug.Log($"  Sampler[{i}]: Id={s.Id}, Weight={s.Weight:F3}, Time={s.Time:F3}, ClipsValid={s.Clips.IsCreated}");
                            }
                        }
                        Debug.Log($"  TotalWeight={totalWeight:F3}");
                    }
                }
            }
        }
    }
#endif
}
