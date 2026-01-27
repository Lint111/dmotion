using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Runtime extensions for manipulating animation layers.
    /// </summary>
    public static class AnimationLayerExtensions
    {
        /// <summary>
        /// Sets the weight of a layer by index.
        /// </summary>
        /// <param name="layers">The animation layers buffer.</param>
        /// <param name="layerIndex">The layer index (0 = base layer).</param>
        /// <param name="weight">The new weight (0-1).</param>
        /// <returns>True if the layer was found and modified.</returns>
        public static bool SetLayerWeight(
            this DynamicBuffer<AnimationStateMachineLayer> layers,
            byte layerIndex,
            float weight)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];

                if (layer.LayerIndex != layerIndex) continue; 

                layer.Weight = math.saturate(weight);
                layers[i] = layer;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Gets the weight of a layer by index.
        /// </summary>
        /// <param name="layers">The animation layers buffer.</param>
        /// <param name="layerIndex">The layer index (0 = base layer).</param>
        /// <returns>The layer weight, or 0 if layer not found.</returns>
        public static float GetLayerWeight(
            this DynamicBuffer<AnimationStateMachineLayer> layers,
            byte layerIndex)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].LayerIndex != layerIndex) continue; 
                
                return layers[i].Weight;
            }
            return 0f;
        }
        
        /// <summary>
        /// Sets the blend mode of a layer by index.
        /// </summary>
        /// <param name="layers">The animation layers buffer.</param>
        /// <param name="layerIndex">The layer index (0 = base layer).</param>
        /// <param name="blendMode">The new blend mode.</param>
        /// <returns>True if the layer was found and modified.</returns>
        public static bool SetLayerBlendMode(
            this DynamicBuffer<AnimationStateMachineLayer> layers,
            byte layerIndex,
            LayerBlendMode blendMode)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];

                if (layer.LayerIndex != layerIndex) continue; 
                
                layer.BlendMode = blendMode;
                layers[i] = layer;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Gets the blend mode of a layer by index.
        /// </summary>
        /// <param name="layers">The animation layers buffer.</param>
        /// <param name="layerIndex">The layer index (0 = base layer).</param>
        /// <returns>The layer blend mode, or Override if layer not found.</returns>
        public static LayerBlendMode GetLayerBlendMode(
            this DynamicBuffer<AnimationStateMachineLayer> layers,
            byte layerIndex)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].LayerIndex != layerIndex) continue; 
                
                return layers[i].BlendMode;
            }
            return LayerBlendMode.Override;
        }
        
        /// <summary>
        /// Smoothly interpolates a layer's weight toward a target value.
        /// Call this each frame to fade a layer in/out.
        /// </summary>
        /// <param name="layers">The animation layers buffer.</param>
        /// <param name="layerIndex">The layer index.</param>
        /// <param name="targetWeight">The target weight to interpolate toward.</param>
        /// <param name="speed">The interpolation speed (higher = faster).</param>
        /// <param name="deltaTime">Time since last frame.</param>
        /// <returns>True if the layer was found.</returns>
        public static bool LerpLayerWeight(
            this DynamicBuffer<AnimationStateMachineLayer> layers,
            byte layerIndex,
            float targetWeight,
            float speed,
            float deltaTime)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                
                if (layer.LayerIndex != layerIndex) continue; 

                var t = math.saturate(speed * deltaTime);
                layer.Weight = math.lerp(layer.Weight, math.saturate(targetWeight), t);
                layers[i] = layer;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Gets the number of active layers (those with valid state machines).
        /// </summary>
        public static int GetActiveLayerCount(this DynamicBuffer<AnimationStateMachineLayer> layers)
        {
            int count = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].IsValid)
                    count++;
            }
            return count;
        }
        
        /// <summary>
        /// Checks if a layer exists by index.
        /// </summary>
        public static bool HasLayer(this DynamicBuffer<AnimationStateMachineLayer> layers, byte layerIndex)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].LayerIndex == layerIndex)
                    return true;
            }
            return false;
        }
    }
}
