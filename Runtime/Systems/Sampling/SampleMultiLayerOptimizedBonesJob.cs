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
    /// Sampling order:
    /// 1. Override layers (lowest index first) - first sample overwrites, rest blend
    /// 2. Additive layers - always blend on top without overwrite
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
            var isFirstSample = true;
            
            // Pass 1: Sample all OVERRIDE layers (these form the base pose)
            // Lower index layers are sampled first, higher layers override
            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var layer = layers[layerIdx];
                if (!layer.IsValid || mathex.iszero(layer.Weight))
                    continue;
                
                // Skip additive layers in first pass
                if (layer.BlendMode == LayerBlendMode.Additive)
                    continue;
                
                SampleLayerClips(
                    ref skeleton,
                    in layer,
                    in samplers,
                    ref activeSamplerCount,
                    ref isFirstSample);
            }
            
            // Pass 2: Sample all ADDITIVE layers (blend on top of base pose)
            // Additive layers never overwrite - they always blend
            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var layer = layers[layerIdx];
                if (!layer.IsValid || mathex.iszero(layer.Weight))
                    continue;
                
                // Only additive layers in second pass
                if (layer.BlendMode != LayerBlendMode.Additive)
                    continue;
                
                // Additive layers never overwrite (even if they're the first layer)
                // This means if ONLY additive layers exist, we'd blend with identity pose
                // In practice, there should always be a base layer
                SampleLayerClips(
                    ref skeleton,
                    in layer,
                    in samplers,
                    ref activeSamplerCount,
                    ref isFirstSample);
            }

            if (activeSamplerCount > 0)
            {
                skeleton.EndSamplingAndSync();
            }
        }
        
        private static void SampleLayerClips(
            ref OptimizedSkeletonAspect skeleton,
            in AnimationStateMachineLayer layer,
            in DynamicBuffer<ClipSampler> samplers,
            ref int activeSamplerCount,
            ref bool isFirstSample)
        {
            var hasMask = layer.HasBoneMask;
            
            for (int i = 0; i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                
                // Only process samplers belonging to this layer
                if (sampler.LayerIndex != layer.LayerIndex)
                    continue;
                
                if (mathex.iszero(sampler.Weight) || !sampler.Clips.IsCreated)
                    continue;
                
                activeSamplerCount++;
                
                // First sample ever overwrites, all subsequent samples blend
                // For additive layers, isFirstSample is already false after override pass
                skeleton.nextSampleWillOverwrite = isFirstSample;
                isFirstSample = false;
                
                // Sample pose with or without mask
                if (hasMask)
                {
                    var mask = layer.BoneMask.Value.AsSpan();
                    sampler.Clip.SamplePose(ref skeleton, mask, sampler.Time, sampler.Weight);
                }
                else
                {
                    sampler.Clip.SamplePose(ref skeleton, sampler.Time, sampler.Weight);
                }
            }
        }
    }
}
