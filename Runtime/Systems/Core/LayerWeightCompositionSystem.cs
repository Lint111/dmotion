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
    /// Phase 1C: Override blending only
    /// Phase 1D: Additive blending support
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

                // Calculate effective influence for each layer based on override blending
                // Higher index layers have priority (they're rendered "on top")
                // Using a simple stack-based approach:
                // - Layer N gets its full weight
                // - Layer N-1 gets weight * (1 - layer N opacity)
                // - And so on...
                
                // First, collect layer weights indexed by layer index
                // Use stackalloc for small fixed-size array (max 8 layers typical)
                const int MaxLayers = 16;
                var layerWeights = stackalloc float[MaxLayers];
                var layerBlendModes = stackalloc LayerBlendMode[MaxLayers];
                int maxLayerIndex = -1;
                
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

                // Calculate effective layer influences using override blending
                // Process from top to bottom, tracking remaining opacity
                var layerInfluences = stackalloc float[MaxLayers];
                float remainingOpacity = 1.0f;
                
                for (int i = maxLayerIndex; i >= 0; i--)
                {
                    if (layerBlendModes[i] == LayerBlendMode.Override)
                    {
                        // Override: this layer takes its weight from remaining opacity
                        float layerWeight = layerWeights[i];
                        layerInfluences[i] = layerWeight * remainingOpacity;
                        remainingOpacity *= (1.0f - layerWeight);
                    }
                    else // LayerBlendMode.Additive
                    {
                        // Phase 1D: Additive layers don't reduce remaining opacity
                        // They add on top without displacing lower layers
                        // For now, treat as override (Phase 1C limitation)
                        float layerWeight = layerWeights[i];
                        layerInfluences[i] = layerWeight * remainingOpacity;
                        remainingOpacity *= (1.0f - layerWeight);
                    }
                }

                // Now scale each sampler's weight by its layer's influence
                for (int i = 0; i < samplers.Length; i++)
                {
                    var sampler = samplers[i];
                    var layerIdx = sampler.LayerIndex;
                    
                    if (layerIdx < MaxLayers)
                    {
                        // Scale sampler weight by layer influence
                        sampler.Weight *= layerInfluences[layerIdx];
                        samplers[i] = sampler;
                    }
                }
            }
        }
    }
}
