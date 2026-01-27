using System;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace DMotion
{
    /// <summary>
    /// Samples optimized bones for multi-layer animation entities.
    /// Processes layers in order, applying bone masks when present.
    /// 
    /// Key differences from SampleOptimizedBonesJob:
    /// - Samples per-layer with proper override/additive blending
    /// - Applies bone masks (BoneMaskBlob) for partial-body layers
    /// - Uses nextSampleWillOverwrite for proper layer composition
    /// 
    /// Kinemation masked sampling API:
    ///   clip.SamplePose(ref skeleton, mask, time, weight)
    /// where mask is ReadOnlySpan&lt;ulong&gt; (each bit = one bone)
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(AnimationStateMachine))] // Only multi-layer entities
    internal partial struct SampleMultiLayerOptimizedBonesJob : IJobEntity
    {
        internal ProfilerMarker Marker;

        internal void Execute(
            OptimizedSkeletonAspect skeleton,
            in DynamicBuffer<AnimationStateMachineLayer> layers,
            in DynamicBuffer<ClipSampler> samplers)
        {
            using var scope = Marker.Auto();

            if (layers.Length == 0 || samplers.Length == 0)
                return;

            var activeSamplerCount = 0;
            
            // Process layers in order (base layer first, then overlay layers)
            // Layers with lower indices are sampled first, higher layers override
            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var layer = layers[layerIdx];
                if (!layer.IsValid || mathex.iszero(layer.Weight))
                    continue;
                
                // Check if this layer has a bone mask
                var hasMask = layer.HasBoneMask;
                
                // Set blend mode for this layer
                // First layer with content always overwrites, subsequent layers blend based on mode
                var isFirstLayerWithContent = activeSamplerCount == 0;
                if (!isFirstLayerWithContent)
                {
                    // For subsequent layers:
                    // - Override mode: will blend based on weight (handled by sampler weights)
                    // - Additive mode: would need special handling (Phase 1D)
                    skeleton.nextSampleWillOverwrite = false;
                }
                
                // Sample all clips in this layer
                for (int i = 0; i < samplers.Length; i++)
                {
                    var sampler = samplers[i];
                    
                    // Only process samplers belonging to this layer
                    if (sampler.LayerIndex != layer.LayerIndex)
                        continue;
                    
                    if (mathex.iszero(sampler.Weight) || !sampler.Clips.IsCreated)
                        continue;
                    
                    activeSamplerCount++;
                    
                    // First sample in the layer should overwrite if it's the first layer
                    if (isFirstLayerWithContent && activeSamplerCount == 1)
                    {
                        skeleton.nextSampleWillOverwrite = true;
                    }
                    
                    // Sample pose with or without mask
                    if (hasMask)
                    {
                        // Masked sampling: only affects bones where mask bit is set
                        // Kinemation API: SamplePose(ref skeleton, mask, time, weight)
                        var mask = layer.BoneMask.Value.AsSpan();
                        sampler.Clip.SamplePose(ref skeleton, mask, sampler.Time, sampler.Weight);
                    }
                    else
                    {
                        // Full body sampling
                        sampler.Clip.SamplePose(ref skeleton, sampler.Time, sampler.Weight);
                    }
                    
                    // Subsequent samples in this layer blend
                    skeleton.nextSampleWillOverwrite = false;
                }
            }

            if (activeSamplerCount > 0)
            {
                skeleton.EndSamplingAndSync();
            }
        }
    }
}
