using Unity.Collections;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Utilities for calculating 2D blend tree weights using Simple Directional algorithm.
    /// This mimics Unity's Simple Directional 2D blending.
    /// </summary>
    public static class Directional2DBlendUtils
    {
        /// <summary>
        /// Calculates weights for a Simple Directional 2D blend tree.
        /// Finds the triangle/quadrant containing the input point and interpolates.
        /// </summary>
        /// <param name="input">The input blend parameters (X, Y)</param>
        /// <param name="positions">Array of clip positions in 2D space</param>
        /// <param name="weights">Output array for calculated weights (must be same length as positions)</param>
        public static void CalculateWeights(float2 input, NativeArray<float2> positions, NativeArray<float> weights)
        {
            // Implementation of Simple Directional 2D blending
            // This is a simplified version - full implementation will handle triangulation
            // For now, we'll implement a basic distance-based falloff as a placeholder
            // TODO: Implement full barycentric coordinates / triangulation logic
            
            int count = positions.Length;
            if (count == 0) return;
            
            // Clear weights
            for (int i = 0; i < count; i++) weights[i] = 0f;

            if (count == 1)
            {
                weights[0] = 1f;
                return;
            }

            // Fallback: Inverse Distance Weighting (temporary until full algorithm)
            // Real implementation requires finding the triangle the point is in
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
            {
                float dist = math.distance(input, positions[i]);
                float w = 1f / (math.max(0.0001f, dist)); // Avoid div by zero
                weights[i] = w;
                totalWeight += w;
            }

            // Normalize
            if (totalWeight > 0.0001f)
            {
                for (int i = 0; i < count; i++)
                {
                    weights[i] /= totalWeight;
                }
            }
            else
            {
                weights[0] = 1f;
            }
        }
    }
}
