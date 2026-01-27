using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Adjusts sampler weights based on layer composition before ClipSamplingSystem runs.
    /// 
    /// For Override blending:
    /// - Higher layers (higher index) override lower layers
    /// - Each sampler's final weight = intra-layer weight * layer influence
    /// - Layer influence = layer weight * (1 - sum of higher layer opacities)
    /// 
    /// For Additive blending:
    /// - Additive layers do NOT reduce remaining opacity for lower layers
    /// - They add their contribution on top of the base pose
    /// - Additive layer influence = layer weight (full, not reduced by opacity)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(ClipSamplingSystem))]
    [UpdateAfter(typeof(UpdateAnimationStatesSystem))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct LayerWeightCompositionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new CompositeLayerWeightsJob().ScheduleParallel(state.Dependency);
        }

        /// <summary>
        /// Composites layer weights into final sampler weights.
        /// Only runs on multi-layer entities (has AnimationStateMachineLayer buffer).
        /// </summary>
        [BurstCompile]
        [WithNone(typeof(AnimationStateMachine))] // Only multi-layer entities
        private unsafe partial struct CompositeLayerWeightsJob : IJobEntity
        {
            internal void Execute(
                in DynamicBuffer<AnimationStateMachineLayer> layers,
                ref DynamicBuffer<ClipSampler> samplers)
            {
                if (layers.Length == 0 || samplers.Length == 0)
                    return;

                // Calculate effective influence for each layer based on blend mode
                // Higher index layers have priority (they're rendered "on top")
                // 
                // Override layers use opacity stacking:
                // - Layer N gets weight * remainingOpacity
                // - remainingOpacity reduced by (1 - layerWeight)
                //
                // Additive layers bypass opacity:
                // - Get their full weight without reducing remainingOpacity
                // - Add on top of the base pose from override layers
                
                const int MaxLayers = 16;
                var layerWeights = stackalloc float[MaxLayers];
                var layerBlendModes = stackalloc LayerBlendMode[MaxLayers];
                int maxLayerIndex = -1;
                
                // Initialize to zero
                for (int i = 0; i < MaxLayers; i++)
                {
                    layerWeights[i] = 0f;
                    layerBlendModes[i] = LayerBlendMode.Override;
                }
                
                for (int i = 0; i < layers.Length && i < MaxLayers; i++)
                {
                    var layer = layers[i];
                    var idx = layer.LayerIndex;
                    if (idx < MaxLayers)
                    {
                        layerWeights[idx] = layer.Weight;
                        layerBlendModes[idx] = layer.BlendMode;
                        if (idx > maxLayerIndex) maxLayerIndex = idx;
                    }
                }

                if (maxLayerIndex < 0)
                    return;

                // Calculate effective layer influences
                // Process from top to bottom for override stacking
                var layerInfluences = stackalloc float[MaxLayers];
                float remainingOpacity = 1.0f;
                
                for (int i = maxLayerIndex; i >= 0; i--)
                {
                    float layerWeight = layerWeights[i];
                    
                    if (layerBlendModes[i] == LayerBlendMode.Override)
                    {
                        // Override: takes from remaining opacity, reduces it for lower layers
                        layerInfluences[i] = layerWeight * remainingOpacity;
                        remainingOpacity *= (1.0f - layerWeight);
                    }
                    else // LayerBlendMode.Additive
                    {
                        // Additive: gets full weight, does NOT reduce remaining opacity
                        // This allows additive layers to add on top without displacing base
                        layerInfluences[i] = layerWeight;
                        // remainingOpacity unchanged - additive doesn't "consume" opacity
                    }
                }

                // Scale each sampler's weight by its layer's influence
                for (int i = 0; i < samplers.Length; i++)
                {
                    var sampler = samplers[i];
                    var layerIdx = sampler.LayerIndex;
                    
                    if (layerIdx < MaxLayers)
                    {
                        sampler.Weight *= layerInfluences[layerIdx];
                        samplers[i] = sampler;
                    }
                }
            }
        }
    }
}
