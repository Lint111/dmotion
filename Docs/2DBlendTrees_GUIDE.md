# 2D Blend Trees Guide

**Status:** Complete (Phase 1B)  
**Last Updated:** 2026-01-23

## Overview

2D Blend Trees allow you to blend animations based on two parameters (X and Y), commonly used for directional locomotion like 8-way movement systems.

**Use Cases:**
- Character movement in all directions (forward, backward, strafe left/right)
- Aiming with vertical and horizontal angles
- Any animation controlled by 2D input

---

## Table of Contents

1. [Creating a 2D Blend State](#creating-a-2d-blend-state)
2. [Configuring Clip Positions](#configuring-clip-positions)
3. [Connecting Parameters](#connecting-parameters)
4. [Runtime ECS Usage](#runtime-ecs-usage)
5. [Algorithm Details](#algorithm-details)
6. [Mechination Translation](#mechination-translation)
7. [Best Practices](#best-practices)
8. [Performance](#performance)

---

## Creating a 2D Blend State

### In the State Machine Editor

1. **Open** a StateMachineAsset (double-click in Project window)
2. **Right-click** in the graph area
3. Select **Create State > New Blend Tree 2D**
4. Or press **Space** and search for "Blend Tree 2D"

### Via Code (Editor)

```csharp
var blendState = ScriptableObject.CreateInstance<Directional2DBlendStateAsset>();
blendState.name = "Locomotion2D";
stateMachineAsset.States.Add(blendState);
```

---

## Configuring Clip Positions

Each clip in a 2D blend tree has a position in 2D space (X, Y):

### Common Locomotion Setup (8-way)

| Clip | Position (X, Y) | Description |
|------|-----------------|-------------|
| Idle | (0, 0) | Standing still |
| Walk Forward | (0, 1) | Moving forward |
| Walk Back | (0, -1) | Moving backward |
| Walk Right | (1, 0) | Strafing right |
| Walk Left | (-1, 0) | Strafing left |
| Walk Forward-Right | (1, 1) | Diagonal forward-right |
| Walk Forward-Left | (-1, 1) | Diagonal forward-left |
| Walk Back-Right | (1, -1) | Diagonal back-right |
| Walk Back-Left | (-1, -1) | Diagonal back-left |

### In the Inspector

1. Select the 2D Blend State in the graph
2. In the Inspector, you'll see:
   - **Blend Parameter X**: The float parameter for horizontal input
   - **Blend Parameter Y**: The float parameter for vertical input
   - **Blend Clips**: Array of clips with positions

3. For each clip entry:
   - **Clip**: Reference to AnimationClipAsset
   - **Position**: X and Y coordinates
   - **Speed**: Playback speed multiplier (default 1.0)

---

## Connecting Parameters

### Creating Parameters

1. In the Parameters panel, click **+**
2. Select **Float**
3. Name it (e.g., "MoveX", "MoveY")

### Assigning to Blend State

1. Select the 2D Blend State
2. In Inspector:
   - **Blend Parameter X**: Select your X parameter (e.g., "MoveX")
   - **Blend Parameter Y**: Select your Y parameter (e.g., "MoveY")

---

## Runtime ECS Usage

### Setting Blend Parameters

```csharp
using DMotion;
using Unity.Entities;

public partial struct LocomotionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (floatParams, transform) in 
            SystemAPI.Query<DynamicBuffer<FloatParameter>, RefRO<LocalTransform>>())
        {
            // Get movement input (example: from input system)
            float2 moveInput = GetMovementInput();
            
            // Set the X parameter (horizontal)
            int moveXHash = AnimationParameterHash.Get("MoveX");
            StateMachineParameterUtils.SetFloat(ref floatParams, moveXHash, moveInput.x);
            
            // Set the Y parameter (vertical)
            int moveYHash = AnimationParameterHash.Get("MoveY");
            StateMachineParameterUtils.SetFloat(ref floatParams, moveYHash, moveInput.y);
        }
    }
}
```

### Parameter Hash Caching (Recommended)

```csharp
public partial struct LocomotionSystem : ISystem
{
    private int _moveXHash;
    private int _moveYHash;
    
    public void OnCreate(ref SystemState state)
    {
        // Cache hashes once at startup
        _moveXHash = AnimationParameterHash.Get("MoveX");
        _moveYHash = AnimationParameterHash.Get("MoveY");
    }
    
    public void OnUpdate(ref SystemState state)
    {
        foreach (var floatParams in SystemAPI.Query<DynamicBuffer<FloatParameter>>())
        {
            float2 moveInput = GetMovementInput();
            StateMachineParameterUtils.SetFloat(ref floatParams, _moveXHash, moveInput.x);
            StateMachineParameterUtils.SetFloat(ref floatParams, _moveYHash, moveInput.y);
        }
    }
}
```

### Normalized vs Raw Input

The algorithm works best with:
- **Normalized input** (magnitude 0-1): Blends smoothly from idle to full animation
- **Raw input** (any magnitude): Works, but clips beyond position radius get full weight

```csharp
// Normalized (recommended for locomotion)
float2 input = math.normalizesafe(rawInput);
if (math.length(rawInput) < 0.1f)
    input = float2.zero; // Deadzone

// Set parameters
StateMachineParameterUtils.SetFloat(ref floatParams, _moveXHash, input.x);
StateMachineParameterUtils.SetFloat(ref floatParams, _moveYHash, input.y);
```

---

## Algorithm Details

DMotion supports two 2D blend algorithms:

### Simple Directional (Default)

Similar to Unity's Simple Directional 2D. Best for **radial clip arrangements** (8-way locomotion). Maximum 2-3 clips active at once.

**How It Works:**

1. **Input at Origin (0,0)**: Uses 100% idle clip (if one exists at origin)
2. **Directional Input**: 
   - Finds the two clips whose angles bracket the input angle
   - Blends between them based on angular distance
3. **Magnitude Scaling** (with idle):
   - Small magnitude = more idle weight
   - Full magnitude = no idle weight

**Weight Calculation:**

```
For input (x, y):
1. If near origin AND idle clip exists: weight[idle] = 1.0
2. Otherwise:
   a. Calculate input angle: atan2(y, x)
   b. Find left neighbor (counter-clockwise)
   c. Find right neighbor (clockwise)
   d. Interpolate weights based on angular distance
   e. If idle exists: scale by input magnitude
```

### Inverse Distance Weighting (IDW)

Alternative algorithm for **arbitrary clip placements**. All clips can be active simultaneously.

**How It Works:**

1. Calculate distance from input to each clip position
2. Weight = 1 / distance^2 (inverse square)
3. Normalize all weights to sum to 1.0
4. Exact position match = 100% weight on that clip

**When to Use IDW:**

- Non-radial clip arrangements
- Clips at varying distances from origin
- When you want smoother multi-clip blending

### Edge Cases Handled

| Case | Behavior |
|------|----------|
| Single clip | Always weight = 1.0 |
| No idle clip | Uses closest clip for origin input |
| Large magnitude | Clips clamp to their position distance |
| Two clips only | Blends between them by angle |
| Exact position match | 100% weight on matching clip |

---

## Mechination Translation

Mechination automatically translates Unity 2D BlendTrees:

### Supported Unity Types

| Unity BlendTree Type | DMotion Translation |
|---------------------|---------------------|
| Simple Directional 2D | `Directional2DBlendStateAsset` (native) |
| Freeform Directional 2D | `Directional2DBlendStateAsset` (converted) |
| Freeform Cartesian 2D | `Directional2DBlendStateAsset` (converted) |

**Note**: Freeform types are converted to Simple Directional. Behavior may differ slightly for non-radial clip arrangements.

### What Gets Translated

- Blend parameter X name
- Blend parameter Y name
- All child motions with positions
- Per-clip speed/timeScale

### Example Unity Setup

```
Unity Animator:
  State: "Locomotion"
    Motion: BlendTree (2D Simple Directional)
      Blend Parameter: "MoveX"
      Blend Parameter Y: "MoveY"
      Children:
        - Idle @ (0, 0)
        - WalkF @ (0, 1)
        - WalkB @ (0, -1)
        ...

After Mechination:
  DMotion StateMachineAsset:
    State: "Locomotion" (Directional2DBlendStateAsset)
      BlendParameterX: FloatParameterAsset "MoveX"
      BlendParameterY: FloatParameterAsset "MoveY"
      BlendClips:
        - Clip: Idle, Position: (0, 0), Speed: 1
        - Clip: WalkF, Position: (0, 1), Speed: 1
        ...
```

---

## Best Practices

### 1. Clip Arrangement

- Place clips at **consistent distances** from origin
- Use **cardinal directions** (0, 90, 180, 270 degrees) for best results
- Include an **idle clip at origin** for smooth start/stop

### 2. Parameter Naming

```csharp
// Consistent naming convention
"MoveX", "MoveY"     // Locomotion
"AimX", "AimY"       // Aiming
"LookX", "LookY"     // Head look
```

### 3. Deadzone Handling

```csharp
float2 input = GetRawInput();
float magnitude = math.length(input);

if (magnitude < 0.1f)  // Deadzone
{
    input = float2.zero;
}
else
{
    input = math.normalize(input);
}
```

### 4. Smooth Input

```csharp
// Smooth input changes to avoid animation popping
float2 _smoothInput;
float smoothSpeed = 10f;

void Update()
{
    float2 targetInput = GetInput();
    _smoothInput = math.lerp(_smoothInput, targetInput, smoothSpeed * deltaTime);
    SetParameters(_smoothInput);
}
```

---

## Performance

### Runtime Cost

| Operation | Algorithm | Cost |
|-----------|-----------|------|
| Weight calculation (4 clips) | SimpleDirectional | ~150-250 cycles |
| Weight calculation (8 clips) | SimpleDirectional | ~200-350 cycles |
| Weight calculation (9 clips) | SimpleDirectional | ~250-400 cycles |
| Weight calculation (8 clips) | IDW | ~300-500 cycles |
| Sampling (via Kinemation) | - | Standard blend cost |
| Memory per state | - | ~48 bytes + clips |

**Target:** <500 cycles per evaluation (achieved)

### Algorithm Performance Comparison

| Algorithm | Active Clips | Best For |
|-----------|--------------|----------|
| SimpleDirectional | 2-3 max | Radial layouts, locomotion |
| IDW | All clips | Arbitrary layouts, smooth blends |

SimpleDirectional is faster due to early-out after finding angle neighbors.

### Optimization Tips

1. **Cache parameter hashes** in `OnCreate`
2. **Batch parameter updates** when possible
3. **Use normalized input** to avoid extra magnitude calculations
4. **Limit clip count** to 9 or fewer for best performance
5. **Prefer SimpleDirectional** for standard 8-way locomotion

### Comparison to 1D Blend

| Aspect | 1D (Linear) | 2D (Directional) |
|--------|-------------|------------------|
| Parameters | 1 float | 2 floats |
| Typical clips | 2-5 | 5-9 |
| Weight calc cost | Lower | Higher |
| Use case | Speed-based | Direction-based |

### Performance Tests

Run performance benchmarks via Unity Test Runner:
- `DMotion.PerformanceTests.Directional2DBlendPerformanceTests`

Tests include:
- 4-way, 8-way, 9-way (8+idle) configurations
- SimpleDirectional vs IDW comparison
- Scaling behavior with clip count

---

## Troubleshooting

### Animation Not Blending

1. Check parameter names match exactly
2. Verify parameters are being set at runtime
3. Ensure clip positions are correct

### Snapping Between Clips

1. Add intermediate diagonal clips
2. Smooth your input values
3. Check for deadzone issues

### Idle Not Working

1. Ensure idle clip position is exactly (0, 0)
2. Check input is reaching (0, 0) with deadzone
3. Verify idle clip is in the BlendClips array

---

## API Reference

### Directional2DBlendStateAsset

```csharp
public class Directional2DBlendStateAsset : AnimationStateAsset
{
    public FloatParameterAsset BlendParameterX;
    public FloatParameterAsset BlendParameterY;
    public Directional2DClipWithPosition[] BlendClips;
    
    public override StateType Type => StateType.Directional2DBlend;
    public override int ClipCount => BlendClips.Length;
    public override IEnumerable<AnimationClipAsset> Clips => BlendClips.Select(b => b.Clip);
}
```

### Directional2DClipWithPosition

```csharp
public struct Directional2DClipWithPosition
{
    public AnimationClipAsset Clip;
    public float2 Position;
    public float Speed;
}
```

### Directional2DBlendUtils

```csharp
public static class Directional2DBlendUtils
{
    /// <summary>
    /// Calculates weights for 2D blending using the specified algorithm.
    /// </summary>
    /// <param name="input">The 2D blend input (X, Y)</param>
    /// <param name="positions">Clip positions in 2D space</param>
    /// <param name="weights">Output weights (same length as positions)</param>
    /// <param name="algorithm">SimpleDirectional (default) or InverseDistanceWeighting</param>
    public static void CalculateWeights(
        float2 input, 
        NativeArray<float2> positions, 
        NativeArray<float> weights,
        Blend2DAlgorithm algorithm = Blend2DAlgorithm.SimpleDirectional);
    
    // Managed array overload for editor/preview
    public static void CalculateWeights(
        float2 input, 
        float2[] positions, 
        float[] weights,
        Blend2DAlgorithm algorithm = Blend2DAlgorithm.SimpleDirectional);
}
```

### Blend2DAlgorithm

```csharp
public enum Blend2DAlgorithm : byte
{
    SimpleDirectional = 0,      // Angular neighbor blending (2-3 clips active)
    InverseDistanceWeighting = 1 // Distance-based blending (all clips active)
}
```

---

## See Also

- [State Machine Editor Guide](StateMachineEditor_GUIDE.md)
- [ECS API Guide](DMotion_ECS_API_Guide.md)
- [Speed Parameter Guide](SpeedParameter_GUIDE.md)
