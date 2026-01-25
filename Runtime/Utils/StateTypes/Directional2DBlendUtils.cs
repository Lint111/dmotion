using Unity.Collections;
using Unity.Mathematics;

namespace DMotion
{
    /// <summary>
    /// Utilities for calculating 2D blend tree weights.
    /// Supports multiple algorithms:
    /// - Simple Directional: Angular interpolation between 2 neighbors (like Unity's Simple Directional 2D)
    /// - Inverse Distance Weighting: All clips contribute based on distance (like Unity's Freeform Directional 2D)
    /// </summary>
    public static class Directional2DBlendUtils
    {
        private const float AngleEpsilon = 0.0001f;
        private const float MagnitudeEpsilon = 0.0001f;
        private const float IdwPower = 2f; // Power factor for inverse distance weighting
        
        /// <summary>
        /// Calculates weights for a 2D blend tree using the specified algorithm.
        /// </summary>
        /// <param name="input">The input blend parameters (X, Y)</param>
        /// <param name="positions">Array of clip positions in 2D space</param>
        /// <param name="weights">Output array for calculated weights (must be same length as positions)</param>
        /// <param name="algorithm">The algorithm to use for weight calculation</param>
        public static void CalculateWeights(float2 input, NativeArray<float2> positions, NativeArray<float> weights, 
            Blend2DAlgorithm algorithm = Blend2DAlgorithm.SimpleDirectional)
        {
            switch (algorithm)
            {
                case Blend2DAlgorithm.SimpleDirectional:
                    CalculateWeightsSimpleDirectional(input, positions, weights);
                    break;
                case Blend2DAlgorithm.InverseDistanceWeighting:
                    CalculateWeightsIDW(input, positions, weights);
                    break;
                default:
                    CalculateWeightsSimpleDirectional(input, positions, weights);
                    break;
            }
        }
        
        #region Simple Directional Algorithm
        
        /// <summary>
        /// Simple Directional algorithm: Finds 2 angle neighbors and interpolates between them.
        /// Best for radial clip arrangements (8-way locomotion). Max 2-3 clips active.
        /// </summary>
        private static void CalculateWeightsSimpleDirectional(float2 input, NativeArray<float2> positions, NativeArray<float> weights)
        {
            int count = positions.Length;
            if (count == 0) return;
            
            // Clear weights
            for (int i = 0; i < count; i++) weights[i] = 0f;

            if (count == 1)
            {
                weights[0] = 1f;
                return;
            }

            // Find idle clip (at or very near origin)
            int idleIndex = FindIdleClip(positions);
            float inputMagnitude = math.length(input);
            
            // If input is at origin, use 100% idle (or closest clip if no idle)
            if (inputMagnitude < MagnitudeEpsilon)
            {
                if (idleIndex >= 0)
                {
                    weights[idleIndex] = 1f;
                }
                else
                {
                    // No idle, find closest clip
                    int closest = FindClosestClip(input, positions);
                    weights[closest] = 1f;
                }
                return;
            }

            // Calculate input angle
            float inputAngle = math.atan2(input.y, input.x);
            
            // Find neighboring clips by angle (excluding idle)
            FindAngleNeighbors(inputAngle, positions, idleIndex, 
                out int leftIndex, out float leftAngle, 
                out int rightIndex, out float rightAngle);

            // If we only have one directional clip (everything else is idle)
            if (leftIndex == rightIndex)
            {
                if (idleIndex >= 0)
                {
                    // Blend between idle and the single directional clip based on magnitude
                    float maxMag = math.length(positions[leftIndex]);
                    float t = math.saturate(inputMagnitude / math.max(maxMag, MagnitudeEpsilon));
                    weights[idleIndex] = 1f - t;
                    weights[leftIndex] = t;
                }
                else
                {
                    weights[leftIndex] = 1f;
                }
                return;
            }

            // Calculate angular interpolation between neighbors
            float angularT = CalculateAngularT(inputAngle, leftAngle, rightAngle);
            
            float leftWeight = 1f - angularT;
            float rightWeight = angularT;

            // If we have an idle clip, blend based on input magnitude vs clip distances
            if (idleIndex >= 0)
            {
                float leftMag = math.length(positions[leftIndex]);
                float rightMag = math.length(positions[rightIndex]);
                float avgMag = leftMag * leftWeight + rightMag * rightWeight;
                
                // t=0 at origin (full idle), t=1 at clip distance (no idle)
                float magnitudeT = math.saturate(inputMagnitude / math.max(avgMag, MagnitudeEpsilon));
                
                weights[idleIndex] = 1f - magnitudeT;
                weights[leftIndex] = leftWeight * magnitudeT;
                weights[rightIndex] = rightWeight * magnitudeT;
            }
            else
            {
                weights[leftIndex] = leftWeight;
                weights[rightIndex] = rightWeight;
            }
        }

        /// <summary>
        /// Finds the clip at or near the origin (idle clip).
        /// Returns -1 if no clip is within MagnitudeEpsilon of origin.
        /// </summary>
        private static int FindIdleClip(NativeArray<float2> positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (math.lengthsq(positions[i]) < MagnitudeEpsilon * MagnitudeEpsilon)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Finds the clip closest to the input position.
        /// </summary>
        private static int FindClosestClip(float2 input, NativeArray<float2> positions)
        {
            int closest = 0;
            float closestDistSq = float.MaxValue;
            
            for (int i = 0; i < positions.Length; i++)
            {
                float distSq = math.distancesq(input, positions[i]);
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closest = i;
                }
            }
            return closest;
        }

        /// <summary>
        /// Finds the two clips whose angles bracket the input angle.
        /// Left = counter-clockwise neighbor, Right = clockwise neighbor.
        /// </summary>
        private static void FindAngleNeighbors(
            float inputAngle,
            NativeArray<float2> positions,
            int idleIndex,
            out int leftIndex, out float leftAngle,
            out int rightIndex, out float rightAngle)
        {
            leftIndex = -1;
            rightIndex = -1;
            leftAngle = float.MinValue;
            rightAngle = float.MaxValue;
            
            float bestLeftDelta = float.MaxValue;
            float bestRightDelta = float.MaxValue;

            for (int i = 0; i < positions.Length; i++)
            {
                if (i == idleIndex) continue; // Skip idle
                
                float2 pos = positions[i];
                if (math.lengthsq(pos) < MagnitudeEpsilon * MagnitudeEpsilon) continue; // Skip near-origin
                
                float clipAngle = math.atan2(pos.y, pos.x);
                float delta = NormalizeAngleDelta(clipAngle - inputAngle);
                
                // Left neighbor: smallest positive delta (counter-clockwise)
                if (delta >= 0 && delta < bestLeftDelta)
                {
                    bestLeftDelta = delta;
                    leftIndex = i;
                    leftAngle = clipAngle;
                }
                
                // Right neighbor: smallest negative delta (clockwise) - stored as positive
                float negativeDelta = NormalizeAngleDelta(inputAngle - clipAngle);
                if (negativeDelta >= 0 && negativeDelta < bestRightDelta)
                {
                    bestRightDelta = negativeDelta;
                    rightIndex = i;
                    rightAngle = clipAngle;
                }
            }

            // Handle case where we only found clips on one side (wrap around)
            if (leftIndex < 0) leftIndex = rightIndex;
            if (rightIndex < 0) rightIndex = leftIndex;
            
            // Fallback: if still no clips found, use first non-idle clip
            if (leftIndex < 0)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i != idleIndex)
                    {
                        leftIndex = rightIndex = i;
                        leftAngle = rightAngle = math.atan2(positions[i].y, positions[i].x);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes an angle delta to [0, 2*PI) range.
        /// </summary>
        private static float NormalizeAngleDelta(float delta)
        {
            const float TwoPi = 2f * math.PI;
            while (delta < 0) delta += TwoPi;
            while (delta >= TwoPi) delta -= TwoPi;
            return delta;
        }

        /// <summary>
        /// Calculates the interpolation factor between two angles.
        /// Returns 0 if at leftAngle, 1 if at rightAngle.
        /// </summary>
        private static float CalculateAngularT(float inputAngle, float leftAngle, float rightAngle)
        {
            // Calculate the angular span from right to left (going counter-clockwise)
            float spanFromRight = NormalizeAngleDelta(leftAngle - rightAngle);
            
            // If span is tiny, return 0.5
            if (spanFromRight < AngleEpsilon)
                return 0.5f;
            
            // Calculate where input falls within this span
            float inputFromRight = NormalizeAngleDelta(inputAngle - rightAngle);
            
            // t=0 at right, t=1 at left
            float t = inputFromRight / spanFromRight;
            
            // Invert because we want t=0 at left, t=1 at right
            return 1f - math.saturate(t);
        }
        
        #endregion
        
        #region Inverse Distance Weighting Algorithm
        
        /// <summary>
        /// Inverse Distance Weighting algorithm: All clips contribute based on distance from input.
        /// Better for arbitrary clip placements. All clips can be active.
        /// </summary>
        private static void CalculateWeightsIDW(float2 input, NativeArray<float2> positions, NativeArray<float> weights)
        {
            int count = positions.Length;
            if (count == 0) return;
            
            // Clear weights
            for (int i = 0; i < count; i++) weights[i] = 0f;
            
            if (count == 1)
            {
                weights[0] = 1f;
                return;
            }
            
            float totalWeight = 0f;
            
            // Check for exact match first
            for (int i = 0; i < count; i++)
            {
                float distSq = math.distancesq(input, positions[i]);
                if (distSq < MagnitudeEpsilon * MagnitudeEpsilon)
                {
                    // Exactly on this clip position
                    weights[i] = 1f;
                    return;
                }
            }
            
            // Calculate inverse distance weights
            for (int i = 0; i < count; i++)
            {
                float dist = math.distance(input, positions[i]);
                float weight = 1f / math.pow(dist, IdwPower);
                weights[i] = weight;
                totalWeight += weight;
            }
            
            // Normalize
            if (totalWeight > MagnitudeEpsilon)
            {
                for (int i = 0; i < count; i++)
                {
                    weights[i] /= totalWeight;
                }
            }
        }
        
        #endregion
        
        #region Managed Array Overloads (for Editor)
        
        /// <summary>
        /// Calculates weights for a 2D blend tree using managed arrays (for editor/preview).
        /// </summary>
        public static void CalculateWeights(float2 input, float2[] positions, float[] weights,
            Blend2DAlgorithm algorithm = Blend2DAlgorithm.SimpleDirectional)
        {
            switch (algorithm)
            {
                case Blend2DAlgorithm.SimpleDirectional:
                    CalculateWeightsSimpleDirectionalManaged(input, positions, weights);
                    break;
                case Blend2DAlgorithm.InverseDistanceWeighting:
                    CalculateWeightsIDWManaged(input, positions, weights);
                    break;
                default:
                    CalculateWeightsSimpleDirectionalManaged(input, positions, weights);
                    break;
            }
        }
        
        private static void CalculateWeightsSimpleDirectionalManaged(float2 input, float2[] positions, float[] weights)
        {
            int count = positions.Length;
            if (count == 0) return;
            
            // Clear weights
            for (int i = 0; i < count; i++) weights[i] = 0f;

            if (count == 1)
            {
                weights[0] = 1f;
                return;
            }

            // Find idle clip (at or very near origin)
            int idleIndex = FindIdleClipManaged(positions);
            float inputMagnitude = math.length(input);
            
            // If input is at origin, use 100% idle (or closest clip if no idle)
            if (inputMagnitude < MagnitudeEpsilon)
            {
                if (idleIndex >= 0)
                {
                    weights[idleIndex] = 1f;
                }
                else
                {
                    int closest = FindClosestClipManaged(input, positions);
                    weights[closest] = 1f;
                }
                return;
            }

            // Calculate input angle
            float inputAngle = math.atan2(input.y, input.x);
            
            // Find neighboring clips by angle (excluding idle)
            FindAngleNeighborsManaged(inputAngle, positions, idleIndex, 
                out int leftIndex, out float leftAngle, 
                out int rightIndex, out float rightAngle);

            // If we only have one directional clip (everything else is idle)
            if (leftIndex == rightIndex)
            {
                if (idleIndex >= 0)
                {
                    float maxMag = math.length(positions[leftIndex]);
                    float t = math.saturate(inputMagnitude / math.max(maxMag, MagnitudeEpsilon));
                    weights[idleIndex] = 1f - t;
                    weights[leftIndex] = t;
                }
                else
                {
                    weights[leftIndex] = 1f;
                }
                return;
            }

            // Calculate angular interpolation between neighbors
            float angularT = CalculateAngularT(inputAngle, leftAngle, rightAngle);
            
            float leftWeight = 1f - angularT;
            float rightWeight = angularT;

            // If we have an idle clip, blend based on input magnitude vs clip distances
            if (idleIndex >= 0)
            {
                float leftMag = math.length(positions[leftIndex]);
                float rightMag = math.length(positions[rightIndex]);
                float avgMag = leftMag * leftWeight + rightMag * rightWeight;
                float magnitudeT = math.saturate(inputMagnitude / math.max(avgMag, MagnitudeEpsilon));
                
                weights[idleIndex] = 1f - magnitudeT;
                weights[leftIndex] = leftWeight * magnitudeT;
                weights[rightIndex] = rightWeight * magnitudeT;
            }
            else
            {
                weights[leftIndex] = leftWeight;
                weights[rightIndex] = rightWeight;
            }
        }
        
        private static void CalculateWeightsIDWManaged(float2 input, float2[] positions, float[] weights)
        {
            int count = positions.Length;
            if (count == 0) return;
            
            // Clear weights
            for (int i = 0; i < count; i++) weights[i] = 0f;
            
            if (count == 1)
            {
                weights[0] = 1f;
                return;
            }
            
            float totalWeight = 0f;
            
            // Check for exact match first
            for (int i = 0; i < count; i++)
            {
                float distSq = math.distancesq(input, positions[i]);
                if (distSq < MagnitudeEpsilon * MagnitudeEpsilon)
                {
                    weights[i] = 1f;
                    return;
                }
            }
            
            // Calculate inverse distance weights
            for (int i = 0; i < count; i++)
            {
                float dist = math.distance(input, positions[i]);
                float weight = 1f / math.pow(dist, IdwPower);
                weights[i] = weight;
                totalWeight += weight;
            }
            
            // Normalize
            if (totalWeight > MagnitudeEpsilon)
            {
                for (int i = 0; i < count; i++)
                {
                    weights[i] /= totalWeight;
                }
            }
        }
        
        private static int FindIdleClipManaged(float2[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (math.lengthsq(positions[i]) < MagnitudeEpsilon * MagnitudeEpsilon)
                {
                    return i;
                }
            }
            return -1;
        }
        
        private static int FindClosestClipManaged(float2 input, float2[] positions)
        {
            int closest = 0;
            float closestDistSq = float.MaxValue;
            
            for (int i = 0; i < positions.Length; i++)
            {
                float distSq = math.distancesq(input, positions[i]);
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closest = i;
                }
            }
            return closest;
        }
        
        private static void FindAngleNeighborsManaged(
            float inputAngle,
            float2[] positions,
            int idleIndex,
            out int leftIndex, out float leftAngle,
            out int rightIndex, out float rightAngle)
        {
            leftIndex = -1;
            rightIndex = -1;
            leftAngle = float.MinValue;
            rightAngle = float.MaxValue;
            
            float bestLeftDelta = float.MaxValue;
            float bestRightDelta = float.MaxValue;

            for (int i = 0; i < positions.Length; i++)
            {
                if (i == idleIndex) continue;
                
                float2 pos = positions[i];
                if (math.lengthsq(pos) < MagnitudeEpsilon * MagnitudeEpsilon) continue;
                
                float clipAngle = math.atan2(pos.y, pos.x);
                float delta = NormalizeAngleDelta(clipAngle - inputAngle);
                
                if (delta >= 0 && delta < bestLeftDelta)
                {
                    bestLeftDelta = delta;
                    leftIndex = i;
                    leftAngle = clipAngle;
                }
                
                float negativeDelta = NormalizeAngleDelta(inputAngle - clipAngle);
                if (negativeDelta >= 0 && negativeDelta < bestRightDelta)
                {
                    bestRightDelta = negativeDelta;
                    rightIndex = i;
                    rightAngle = clipAngle;
                }
            }

            if (leftIndex < 0) leftIndex = rightIndex;
            if (rightIndex < 0) rightIndex = leftIndex;
            
            if (leftIndex < 0)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i != idleIndex)
                    {
                        leftIndex = rightIndex = i;
                        leftAngle = rightAngle = math.atan2(positions[i].y, positions[i].x);
                        break;
                    }
                }
            }
        }
        
        #endregion
    }
}
