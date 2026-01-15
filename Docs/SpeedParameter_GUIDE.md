# Speed Parameter - User Guide

## Overview

**Speed Parameters** allow you to dynamically control animation playback speed at runtime using Float parameters. This matches Unity AnimatorController's speed parameter behavior.

## Features

- **Dynamic Speed Control**: Multiply animation speed by runtime parameter value
- **Unity Compatible**: Direct 1:1 mapping from Unity AnimatorController speed parameters
- **Zero Overhead**: Burst-compiled for maximum performance
- **Flexible**: Works with Single Clip and Linear Blend states

## How It Works

### Authoring (Unity AnimatorController)

1. Create a Float parameter (e.g., "RunSpeed")
2. Select a state in the Animator window
3. Enable "Parameter" checkbox under "Speed"
4. Select your Float parameter from the dropdown

### Bridge Conversion

The Unity Controller Bridge automatically:
- Detects speed parameters in Unity states
- Links them to DMotion FloatParameterAsset
- Configures runtime speed multiplication

### Runtime Behavior

Final playback speed = **Base Speed × Speed Parameter Value**

**Example**:
- State base speed: `2.0`
- Speed parameter "RunSpeed" value: `1.5`
- **Final speed**: `2.0 × 1.5 = 3.0x`

## Usage Examples

### Example 1: Variable Run Speed

**Unity Setup**:
```
State: "Run"
- Speed: 1.0
- Speed Parameter: "RunSpeed"

Float Parameter: "RunSpeed"
- Default Value: 1.0
```

**Runtime (C#)**:
```csharp
// Walk slowly
stateMachine.SetFloatParameter("RunSpeed", 0.5f); // 0.5x speed

// Normal run
stateMachine.SetFloatParameter("RunSpeed", 1.0f); // 1.0x speed

// Sprint
stateMachine.SetFloatParameter("RunSpeed", 2.0f); // 2.0x speed
```

### Example 2: Time Dilation Effect

**Unity Setup**:
```
States: All combat states
- Speed: 1.0
- Speed Parameter: "TimeScale"

Float Parameter: "TimeScale"
- Default Value: 1.0
```

**Runtime (C#)**:
```csharp
// Slow motion effect
stateMachine.SetFloatParameter("TimeScale", 0.3f); // 30% speed

// Bullet time
stateMachine.SetFloatParameter("TimeScale", 0.1f); // 10% speed

// Speed up time
stateMachine.SetFloatParameter("TimeScale", 2.0f); // 200% speed
```

### Example 3: Exhaustion System

**Unity Setup**:
```
State: "Walk"
- Speed: 1.0
- Speed Parameter: "StaminaMultiplier"
```

**Runtime (C#)**:
```csharp
// Update speed based on stamina
float stamina = player.Stamina; // 0.0 to 1.0
float speedMultiplier = Mathf.Lerp(0.5f, 1.0f, stamina);
stateMachine.SetFloatParameter("StaminaMultiplier", speedMultiplier);

// Full stamina: 1.0x speed
// No stamina: 0.5x speed (50% slower)
```

## Manual Authoring

You can also set speed parameters directly on DMotion assets:

### In Unity Inspector

1. Select your `AnimationStateAsset` (SingleClipState or LinearBlendState)
2. Set **Speed** to base value (e.g., `1.0`)
3. Assign a **Speed Parameter** (FloatParameterAsset)
4. At runtime, the final speed will be: `Speed × SpeedParameter.Value`

### Via Code

```csharp
// Create state with speed parameter
var state = ScriptableObject.CreateInstance<SingleClipStateAsset>();
state.Speed = 1.5f;
state.SpeedParameter = myFloatParameter;
```

## Technical Details

### Architecture

**Authoring Layer**:
- `AnimationStateAsset.Speed` (float): Base speed multiplier
- `AnimationStateAsset.SpeedParameter` (FloatParameterAsset): Optional runtime multiplier

**Baking Layer**:
- `AnimationStateBlob.Speed` (float): Base speed baked into blob
- `AnimationStateBlob.SpeedParameterIndex` (ushort): Parameter index (ushort.MaxValue = none)

**Runtime Layer**:
- `GetFinalSpeed()`: Calculates `Speed × floatParameters[SpeedParameterIndex].Value`
- Called every time a state is created/transitioned to
- Burst-compiled for maximum performance

### Performance

- **Memory**: +2 bytes per state (ushort parameter index)
- **CPU**: ~1 multiply per state transition (negligible)
- **Burst**: Fully burst-compiled, zero overhead

### Limitations

- Speed parameter applies to entire state (not per-clip in blend trees)
- Cannot animate speed parameter over time (set manually in code)
- Negative speed values may cause issues (clamp to positive values)

## Conversion Reports

When converting Unity AnimatorControllers, speed parameters are detected and reported:

### Report Output

```markdown
| Feature | Unity | DMotion | Status |
|---------|-------|---------|--------|
| Speed Parameter | Used | FloatParameterAsset | ✓ Supported |
```

### Console Logs

```
[UnityControllerConverter] State 'Run': Speed parameter 'RunSpeed' will multiply base speed at runtime
```

## Best Practices

### 1. Use Consistent Naming

```csharp
// Good: Clear, descriptive names
"MoveSpeed", "AttackSpeed", "TimeScale"

// Bad: Generic, unclear names
"Float1", "Speed", "Multiplier"
```

### 2. Set Reasonable Base Speed

```csharp
// Good: Base speed of 1.0, use parameter for variation
state.Speed = 1.0f;
state.SpeedParameter = speedParam; // 0.5 to 2.0 range

// Less flexible: High base speed limits range
state.Speed = 5.0f;
state.SpeedParameter = speedParam; // Can't go below 5.0x
```

### 3. Clamp Parameter Values

```csharp
// Prevent negative or extreme speeds
float speedValue = Mathf.Clamp(calculatedSpeed, 0.1f, 3.0f);
stateMachine.SetFloatParameter("MoveSpeed", speedValue);
```

### 4. Share Parameters Across States

```csharp
// Efficient: One parameter affects multiple states
states: ["Walk", "Run", "Sprint"]
- All use Speed Parameter: "MoveSpeed"

// Update once, affects all states
stateMachine.SetFloatParameter("MoveSpeed", 1.5f);
```

## Troubleshooting

### Problem: Speed parameter has no effect

**Causes**:
1. Parameter not linked to state
2. Parameter name mismatch
3. Parameter value is 1.0 (no visible change)

**Solutions**:
```csharp
// Verify parameter exists
bool hasParam = stateMachine.HasFloatParameter("MySpeed");

// Check current value
float currentSpeed = stateMachine.GetFloatParameter("MySpeed");

// Set to obvious value for testing
stateMachine.SetFloatParameter("MySpeed", 0.1f); // Very slow
stateMachine.SetFloatParameter("MySpeed", 5.0f); // Very fast
```

### Problem: Animation plays at wrong speed

**Causes**:
1. Base speed and parameter multiplied incorrectly
2. Parameter initialized to wrong value

**Solutions**:
```csharp
// Check effective speed
float baseSpeed = state.Speed;
float paramValue = stateMachine.GetFloatParameter("MySpeed");
float effectiveSpeed = baseSpeed * paramValue;
Debug.Log($"Effective Speed: {effectiveSpeed}");
```

### Problem: Conversion report shows warning

**Causes**:
- Old version of DMotion before Speed Parameter support

**Solutions**:
- Update to latest DMotion version (Phase 12.1+)
- Re-convert Unity AnimatorController

## FAQ

**Q: Can I animate speed parameters in Unity Animator?**
A: No. Speed parameters are for runtime code control only. Unity's speed curves are not supported.

**Q: Does this work with blend trees?**
A: Yes! Speed parameter applies to the entire blend tree state.

**Q: Can I use multiple speed parameters per state?**
A: No. Each state can have one speed parameter. Use base speed + one multiplier.

**Q: What happens if I don't set a speed parameter?**
A: State uses constant base speed (SpeedParameterIndex = ushort.MaxValue).

**Q: Can I change speed parameter during state playback?**
A: No. Speed is calculated when entering the state. Change takes effect on next transition.

**Q: What's the performance cost?**
A: Negligible. One multiply per state transition, fully burst-compiled.

**Q: Does this work with Sub-State Machines?**
A: No. Speed parameters don't apply to sub-state machine states (they're containers, not animated states).

## Related Documentation

- `ConversionReportGenerator_GUIDE.md` - See speed parameter in conversion reports
- `UnityControllerBridge_ActionPlan.md` - Feature roadmap
- Unity Documentation: [Animator Speed Parameter](https://docs.unity3d.com/Manual/class-State.html)

---

**Added in**: DMotion Phase 12.1
**Status**: ✅ Fully Supported
**Compatible with**: Unity AnimatorController speed parameters
