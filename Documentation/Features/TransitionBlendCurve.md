# Feature: Transition Blend Curve Support

## Status: Implemented (Phases 0-4 Complete)
## Priority: High
## Last Updated: January 2026

---

## Problem Statement

The transition blend curve exists in authoring (`StateOutTransition.BlendCurve`) but is:
1. **Not baked** into the runtime blob
2. **Not used** in weight calculations (linear interpolation only)
3. **Only visible** in AnimationPreviewWindow (not in main editor)

This means:
- Preview shows curve-based blending
- Runtime uses linear blending
- **Preview does not match runtime behavior**

### Current Weight Calculation (BlendAnimationStatesSystem.cs:81)

```csharp
toAnimationState.Weight = math.clamp(toAnimationState.Time / animationStateTransition.TransitionDuration, 0, 1);
```

This is pure linear `t / duration` - curve is completely ignored.

---

## Goals

1. **Runtime Parity**: Weight calculation uses the authored curve
2. **Shared UI**: Curve editable in both StateMachineEditor and PreviewWindow
3. **Burst Compatible**: Curve sampled into blob for job access
4. **Default Backward Compatible**: Linear curve as default

---

## Architecture

### Curve Representation in Blob

`AnimationCurve` is managed and can't be used in Burst jobs. 

**Approach Comparison:**

| Aspect | Pre-Sampled (16 floats) | Control Points (Hermite) |
|--------|------------------------|--------------------------|
| **Storage** | 32 bytes fixed | 2-4 keyframes × 8 bytes = 16-32 bytes |
| **Precision** | Lossy between samples | Exact curve reproduction |
| **Sharp curves** | May miss peaks | Perfect representation |
| **Runtime cost** | 1 lerp | Hermite evaluation (~20 ops) |

**Decision: Control Points (Hermite Splines) with Linear Fast-Path**

Storing the actual keyframes provides:
- **Exact curve reproduction** - preview matches runtime perfectly
- **Smaller storage** for typical 2-keyframe curves
- **No precision loss** at sharp transitions
- **Honest data representation** - what you author is what you get

### Linear Fast-Path Optimization

For the degenerate case of a linear curve (the default), we skip Hermite entirely:

| Curve Type | Detection | Blob Storage | Runtime Cost |
|------------|-----------|--------------|--------------|
| **Linear** | Empty keyframes array | 0 bytes | 1 branch, return `t` |
| **Custom** | Non-empty keyframes | 16-64 bytes | Hermite (~20 ops) |

**Trade-off:** Users explicitly opt into Hermite cost by customizing the curve. Default behavior has zero overhead compared to current implementation.

```csharp
// Fast-path: empty keyframes = linear (current behavior, zero cost)
if (keyframes.Length == 0) return t;

// Slow-path: custom curve, pay Hermite cost
return EvaluateHermite(ref keyframes, t);
```

**Linear Detection at Conversion Time:**
```csharp
static bool IsLinearCurve(AnimationCurve curve)
{
    if (curve == null || curve.length == 0) return true;
    if (curve.length != 2) return false;
    
    var k0 = curve.keys[0];
    var k1 = curve.keys[1];
    
    // Check: (0,1) -> (1,0) with tangent = -1 (Unity's default linear)
    const float epsilon = 0.001f;
    return math.abs(k0.time) < epsilon &&
           math.abs(k0.value - 1f) < epsilon &&
           math.abs(k1.time - 1f) < epsilon &&
           math.abs(k1.value) < epsilon &&
           math.abs(k0.outTangent + 1f) < epsilon &&
           math.abs(k1.inTangent + 1f) < epsilon;
}
```

### Blittable Keyframe Structure

```csharp
/// <summary>
/// Blittable keyframe for blob storage. Matches Unity's Hermite spline format.
/// Uses half precision for memory efficiency (sufficient for 0-1 weight curves).
/// </summary>
internal struct CurveKeyframe
{
    public half Time;       // 2 bytes - normalized time [0, 1]
    public half Value;      // 2 bytes - weight value [0, 1]
    public half InTangent;  // 2 bytes - incoming tangent
    public half OutTangent; // 2 bytes - outgoing tangent
}  // 8 bytes per keyframe
```

### Hermite Interpolation at Runtime

```csharp
/// <summary>
/// Evaluates a Hermite spline curve at normalized time t.
/// Burst-compatible, no managed allocations.
/// </summary>
static float EvaluateCurve(ref BlobArray<CurveKeyframe> keyframes, float t)
{
    int count = keyframes.Length;
    
    // Fallbacks
    if (count == 0) return t;  // Linear if no curve data
    if (count == 1) return keyframes[0].Value;
    
    // Clamp t to valid range
    t = math.clamp(t, 0f, 1f);
    
    // Find the segment containing t
    int segmentIndex = 0;
    for (int i = 0; i < count - 1; i++)
    {
        if (t < keyframes[i + 1].Time)
        {
            segmentIndex = i;
            break;
        }
        segmentIndex = i;
    }
    
    var k0 = keyframes[segmentIndex];
    var k1 = keyframes[math.min(segmentIndex + 1, count - 1)];
    
    // Calculate local t within segment
    float segmentDuration = k1.Time - k0.Time;
    if (segmentDuration < 0.0001f) return k0.Value;
    
    float localT = (t - k0.Time) / segmentDuration;
    
    // Hermite basis functions
    float t2 = localT * localT;
    float t3 = t2 * localT;
    
    float h00 = 2f * t3 - 3f * t2 + 1f;  // Position at k0
    float h10 = t3 - 2f * t2 + localT;   // Tangent at k0
    float h01 = -2f * t3 + 3f * t2;       // Position at k1
    float h11 = t3 - t2;                  // Tangent at k1
    
    // Scale tangents by segment duration (Hermite convention)
    float m0 = (float)k0.OutTangent * segmentDuration;
    float m1 = (float)k1.InTangent * segmentDuration;
    
    return h00 * k0.Value + h10 * m0 + h01 * k1.Value + h11 * m1;
}
```

### Typical Curve Storage

| Curve Type | Keyframes | Storage |
|------------|-----------|---------|
| Linear | 2 | 16 bytes |
| Ease In | 2 | 16 bytes |
| Ease Out | 2 | 16 bytes |
| Ease In-Out | 2 | 16 bytes |
| S-Curve | 3 | 24 bytes |
| Custom complex | 4-8 | 32-64 bytes |

---

## Design Clarifications

### Uniform Curve Evaluation

Curve evaluation is identical regardless of transition source:
- **State → State**: Curve controls blend shape ✓
- **Any State → State**: Same curve evaluation ✓

The curve is simply `f(normalizedTime) → toStateWeight`. Source doesn't matter.

### Exit Transitions Have No Curves

Exit transitions signal "sub-graph is done" to the parent state machine. The **parent's** transition controls the blend when entering/exiting a sub-graph:

```
Parent StateMachine
├── State A
├── SubStateMachine B ◄── Parent's transition TO here has the curve
│   ├── State B1
│   └── State B2 (exit) ◄── Exit transition has NO curve
└── State C ◄── Parent's transition FROM SubStateMachine has the curve
```

Exit transitions only need: destination + conditions. No `CurveKeyframes`.

---

## Implementation Plan

### Phase 0: Transition Curve Reference Infrastructure

**Goal:** Enable runtime curve lookup by tracking which transition is active.

**Problem:** Current `AnimationStateTransition` only stores `AnimationStateId` and `TransitionDuration`. To evaluate the curve, we need to know *which* transition triggered this blend.

**Files to modify:**
- `Runtime/Components/AnimationState.cs`
- `Runtime/Systems/UpdateStateMachineJob.cs`

**Changes to `AnimationStateTransition`:**

```csharp
public struct AnimationStateTransition : IComponentData
{
    public byte AnimationStateId;
    public float TransitionDuration;
    
    // NEW: Curve lookup info
    public short CurveSourceStateIndex;     // -1 for Any State transitions
    public short CurveSourceTransitionIndex; // Index into Transitions or AnyStateTransitions
    public bool IsFromAnyState;              // Which array to look up
}
```

**Changes to transition initiation (`UpdateStateMachineJob`):**

When a transition is triggered, capture the source indices:
```csharp
// State → State transition
animationStateTransition = new AnimationStateTransition
{
    AnimationStateId = toStateId,
    TransitionDuration = transition.TransitionDuration,
    CurveSourceStateIndex = currentStateIndex,
    CurveSourceTransitionIndex = transitionIndex,
    IsFromAnyState = false
};

// Any State transition  
animationStateTransition = new AnimationStateTransition
{
    AnimationStateId = toStateId,
    TransitionDuration = anyTransition.TransitionDuration,
    CurveSourceStateIndex = -1,
    CurveSourceTransitionIndex = anyTransitionIndex,
    IsFromAnyState = true
};
```

**Acceptance Criteria:**
- [ ] `AnimationStateTransition` captures curve source indices
- [ ] State transitions populate `CurveSourceStateIndex` and `CurveSourceTransitionIndex`
- [ ] Any State transitions set `IsFromAnyState = true` with correct index
- [ ] Existing transition behavior unchanged

---

### Phase 1: Blob Data Model

**Files to modify:**
- `Runtime/Components/AnimationTransition.cs`

**Changes:**

```csharp
/// <summary>
/// Blittable keyframe for Hermite spline curves in blob storage.
/// </summary>
internal struct CurveKeyframe
{
    internal half Time;       // Normalized time [0, 1]
    internal half Value;      // Weight value [0, 1]  
    internal half InTangent;  // Incoming tangent
    internal half OutTangent; // Outgoing tangent
}

internal struct StateOutTransitionGroup
{
    internal short ToStateIndex;
    internal float TransitionDuration;
    internal float TransitionEndTime;
    internal BlobArray<BoolTransition> BoolTransitions;
    internal BlobArray<IntTransition> IntTransitions;
    internal BlobArray<CurveKeyframe> CurveKeyframes;  // NEW - Hermite spline keyframes
    internal bool HasEndTime => TransitionEndTime > 0;
    internal bool HasAnyConditions => BoolTransitions.Length > 0 || IntTransitions.Length > 0;
    internal bool HasCurve => CurveKeyframes.Length > 0;
}

internal struct AnyStateTransition
{
    // ... existing fields ...
    internal BlobArray<CurveKeyframe> CurveKeyframes;  // NEW
    internal bool HasCurve => CurveKeyframes.Length > 0;
}

// NOTE: Exit transitions (StateOutTransitionGroup in ExitTransitionGroup) 
// do NOT get CurveKeyframes - parent state machine handles the blend curve.
```

**Acceptance Criteria:**
- [ ] `CurveKeyframe` struct compiles (8 bytes)
- [ ] Blob structs compile with new field
- [ ] Existing functionality unchanged

### Phase 2: Conversion (Baking Keyframes)

**Files to modify:**
- `Runtime/Authoring/Conversion/StateMachineBlobConverter.cs`

**Changes:**

1. Add keyframe data to `StateOutTransitionConversionData`:
   ```csharp
   internal struct StateOutTransitionConversionData
   {
       // ... existing fields ...
       internal UnsafeList<CurveKeyframe> CurveKeyframes;  // NEW
   }
   ```

2. Convert `AnimationCurve` keyframes during baking (with linear fast-path):
   ```csharp
   /// <summary>
   /// Converts Unity's AnimationCurve to blittable keyframes.
   /// Returns empty list for linear curves (fast-path optimization).
   /// Inverts Y values for non-linear curves (Unity: From weight, DMotion: To weight).
   /// </summary>
   static UnsafeList<CurveKeyframe> ConvertCurveKeyframes(
       AnimationCurve curve, 
       Allocator allocator)
   {
       // Fast-path: linear curve = empty keyframes = zero storage, zero runtime cost
       if (IsLinearCurve(curve))
       {
           return new UnsafeList<CurveKeyframe>(0, allocator);
       }
       
       // Custom curve: bake keyframes for Hermite evaluation
       var keyframes = new UnsafeList<CurveKeyframe>(curve.length, allocator);
       for (int i = 0; i < curve.length; i++)
       {
           var key = curve.keys[i];
           keyframes.Add(new CurveKeyframe
           {
               Time = (half)key.time,
               // Invert: Unity stores "From" weight, we want "To" weight
               Value = (half)(1f - key.value),
               // Negate tangents due to Y inversion
               InTangent = (half)(-key.inTangent),
               OutTangent = (half)(-key.outTangent)
           });
       }
       return keyframes;
   }
   
   /// <summary>
   /// Detects if curve is the default linear transition (0,1) -> (1,0).
   /// Linear curves get empty keyframes array for zero-cost runtime.
   /// </summary>
   static bool IsLinearCurve(AnimationCurve curve)
   {
       if (curve == null || curve.length == 0) return true;
       if (curve.length != 2) return false;
       
       var k0 = curve.keys[0];
       var k1 = curve.keys[1];
       
       const float epsilon = 0.01f;
       
       // Check: (0,1) -> (1,0) with tangent ≈ -1
       return Mathf.Abs(k0.time) < epsilon &&
              Mathf.Abs(k0.value - 1f) < epsilon &&
              Mathf.Abs(k1.time - 1f) < epsilon &&
              Mathf.Abs(k1.value) < epsilon &&
              Mathf.Abs(k0.outTangent + 1f) < epsilon &&
              Mathf.Abs(k1.inTangent + 1f) < epsilon;
   }
   ```

3. Bake keyframes into blob in `BuildBlob()`:
   ```csharp
   // In transition building loop
   if (transitionConversionData.CurveKeyframes.IsCreated && 
       transitionConversionData.CurveKeyframes.Length > 0)
   {
       builder.ConstructFromNativeArray(
           ref transitions[transitionIndex].CurveKeyframes,
           transitionConversionData.CurveKeyframes.Ptr,
           transitionConversionData.CurveKeyframes.Length);
   }
   else
   {
       builder.Allocate(ref transitions[transitionIndex].CurveKeyframes, 0);
   }
   ```

**Files to modify:**
- `Runtime/Authoring/Conversion/AnimationStateMachineConversionUtils.cs` (where transitions are converted)

**Acceptance Criteria:**
- [ ] Linear curves produce empty keyframes array (0 bytes, fast-path)
- [ ] Non-linear curves baked with correct keyframe count
- [ ] Y values inverted correctly (Unity "From" -> DMotion "To")
- [ ] Tangents negated for Y inversion
- [ ] `IsLinearCurve()` correctly detects default linear curve

### Phase 3: Runtime Weight Calculation

**Files to add:**
- `Runtime/Utils/CurveUtils.cs` - Hermite evaluation utility

**Files to modify:**
- `Runtime/Systems/BlendAnimationStatesSystem.cs` (use curve lookup from Phase 0 indices)

**New utility file (`Runtime/Utils/CurveUtils.cs`):**

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DMotion
{
    [BurstCompile]
    internal static class CurveUtils
    {
        /// <summary>
        /// Evaluates a Hermite spline curve at normalized time t.
        /// Returns linear t if no keyframes provided.
        /// </summary>
        [BurstCompile]
        public static float EvaluateCurve(ref BlobArray<CurveKeyframe> keyframes, float t)
        {
            int count = keyframes.Length;
            
            // Fallbacks
            if (count == 0) return math.clamp(t, 0f, 1f);
            if (count == 1) return keyframes[0].Value;
            
            t = math.clamp(t, 0f, 1f);
            
            // Find segment (binary search for larger curves, linear for small)
            int seg = 0;
            for (int i = 0; i < count - 1; i++)
            {
                if (t < keyframes[i + 1].Time) { seg = i; break; }
                seg = i;
            }
            
            var k0 = keyframes[seg];
            var k1 = keyframes[math.min(seg + 1, count - 1)];
            
            float dt = k1.Time - k0.Time;
            if (dt < 0.0001f) return k0.Value;
            
            float localT = (t - k0.Time) / dt;
            float t2 = localT * localT;
            float t3 = t2 * localT;
            
            // Hermite basis
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + localT;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            
            float m0 = (float)k0.OutTangent * dt;
            float m1 = (float)k1.InTangent * dt;
            
            return math.clamp(h00 * k0.Value + h10 * m0 + h01 * k1.Value + h11 * m1, 0f, 1f);
        }
    }
}
```

**Weight calculation change (`BlendAnimationStatesSystem.cs`):**

Uses the curve reference indices from Phase 0 to look up keyframes:

```csharp
// Before
toAnimationState.Weight = math.clamp(toAnimationState.Time / 
    animationStateTransition.TransitionDuration, 0, 1);

// After  
float normalizedTime = math.clamp(toAnimationState.Time / 
    animationStateTransition.TransitionDuration, 0, 1);

// Look up curve from blob using indices captured in Phase 0
ref var curveKeyframes = ref GetTransitionCurve(
    ref stateMachineBlob.Value, 
    animationStateTransition);

if (curveKeyframes.Length > 0)  // Has custom curve
{
    normalizedTime = CurveUtils.EvaluateCurve(ref curveKeyframes, normalizedTime);
}
// else: linear fast-path, normalizedTime unchanged

toAnimationState.Weight = normalizedTime;
```

**Helper for curve lookup:**

```csharp
static ref BlobArray<CurveKeyframe> GetTransitionCurve(
    ref StateMachineBlob blob,
    in AnimationStateTransition transition)
{
    if (transition.IsFromAnyState)
    {
        // Any State transition
        return ref blob.AnyStateTransitions[transition.CurveSourceTransitionIndex].CurveKeyframes;
    }
    else
    {
        // State → State transition
        ref var fromState = ref blob.States[transition.CurveSourceStateIndex];
        return ref fromState.Transitions[transition.CurveSourceTransitionIndex].CurveKeyframes;
    }
}
```

**Acceptance Criteria:**
- [ ] Hermite evaluation produces mathematically correct results
- [ ] Non-linear curves (ease-in, ease-out) produce correct weights
- [ ] Default linear curve unchanged behavior (identical to current)
- [ ] No significant performance regression (benchmark ~20 math ops vs 1 division)

### Phase 4: Editor UI - State Machine Editor Integration

**Goal:** Curve editable when selecting a transition in the graph editor

**Files to modify:**
- `Editor/EditorWindows/TransitionGroupInspector.cs` (or wherever transition properties are shown)

**Changes:**
1. Add `CurvePreviewElement` to transition inspector
2. Wire to `StateOutTransition.BlendCurve` SerializedProperty
3. Use same USS styling as PreviewWindow

**Acceptance Criteria:**
- [ ] Curve visible when selecting transition edge
- [ ] Editing curve updates asset
- [ ] Undo/Redo works

### Phase 5: Shared UI Component

**Goal:** Single source of truth for curve editing UI

**Files:**
- `Editor/EditorWindows/Preview/CurvePreviewElement.cs` (already exists)

**Changes:**
1. Ensure `CurvePreviewElement` can be used standalone
2. Add factory method for common setup:
   ```csharp
   public static CurvePreviewElement CreateForTransition(
       SerializedProperty curveProperty,
       Action onChanged = null)
   ```
3. Both StateMachineEditor and PreviewWindow use this

**Acceptance Criteria:**
- [ ] Same visual appearance in both locations
- [ ] Changes sync between windows

---

## Data Flow

```
Authoring                    Conversion                  Runtime
┌──────────────────┐        ┌──────────────────┐        ┌──────────────────┐
│ StateOutTransition│        │ BlobConverter    │        │ BlendJob         │
│   .BlendCurve    │───────▶│ ConvertKeyframes │───────▶│ EvaluateCurve()  │
│   (AnimationCurve)│        │ (2-8 keyframes)  │        │ (Hermite interp) │
│                   │        │ Y-axis inverted  │        │                  │
└──────────────────┘        └──────────────────┘        └──────────────────┘
        │
        │ SerializedProperty
        ▼
┌──────────────────┐
│ CurvePreviewElement│
│ (Editor UI)       │
└──────────────────┘
```

---

## Curve Convention

**Unity AnimationCurve (Authoring):**
- X axis: Normalized time (0 to 1)
- Y axis: "From" state weight (1 at start, 0 at end)
- Tangents: Slope of the curve at keyframe

**DMotion Blob (Runtime):**
- X axis: Normalized time (0 to 1)
- Y axis: "To" state weight (0 at start, 1 at end) - **INVERTED**
- Tangents: Negated due to Y inversion

**Conversion:**
```csharp
blobValue = 1 - unityValue
blobTangent = -unityTangent
```

---

## Memory Impact

Per transition with linear fast-path optimization:

| Curve Type | Keyframes | Storage | Runtime Cost |
|------------|-----------|---------|--------------|
| **Linear (default)** | 0 (empty) | **0 bytes** | 1 branch |
| Ease In/Out | 2 | 16 bytes | Hermite |
| S-Curve | 3 | 24 bytes | Hermite |
| Complex custom | 4-8 | 32-64 bytes | Hermite |

**Typical state machine (mostly linear):** 
- 50 transitions, 45 linear + 5 custom
- Storage: 45 × 0 bytes + 5 × 16 bytes = **80 bytes total**

**Fully customized state machine:**
- 50 transitions × 16-32 bytes = 800 bytes - 1.6 KB

Negligible compared to animation clip data. Linear fast-path means default usage has **zero memory overhead**.

---

## Performance Considerations

### Linear Fast-Path (Default)

```csharp
if (keyframes.Length == 0) return t;  // 1 branch, identical to current
```

- **Cost**: 1 integer comparison + 1 branch
- **Identical** to current linear implementation
- **Zero overhead** for default usage

### Hermite Evaluation (Custom Curves Only)

1. **Hermite evaluation cost**: ~20 math operations per evaluation
   - Segment search: O(n) for small n, could add binary search for n > 8
   - Basis function calculation: 12 multiplications, 8 additions
   - Final interpolation: 4 multiplications, 3 additions
   
2. **Only paid when needed**: Users explicitly opt in by customizing curve
   - Typical project: 90% linear transitions → 90% zero overhead
   - At 60 FPS with 10 custom transitions: 600 Hermite evaluations/frame
   - Modern CPU: negligible impact (< 0.01ms)

3. **Memory access**: 
   - Keyframes are contiguous in blob - cache friendly
   - Typical 2-keyframe curve fits in single cache line (16 bytes)

4. **Burst optimization**: 
   - All operations are Burst-compatible
   - SIMD potential for basis function calculation

### Performance Trade-off Summary

| Usage | Memory | Runtime | Notes |
|-------|--------|---------|-------|
| Linear (default) | 0 bytes | ~Current | No regression |
| Custom curve | 16-64 bytes | +~20 ops | Explicit opt-in |

---

## Mechination Impact

Mechination needs to:
1. Read Unity's transition curves (if Unity exposes them)
2. Or use linear default if Unity doesn't provide curve data
3. Write to `StateOutTransition.BlendCurve`

Unity's `AnimatorStateTransition` does NOT expose a curve API - transitions are always linear in Mecanim. This is a DMotion-specific enhancement.

---

## Testing Strategy

### Unit Tests (`CurveUtilsTests.cs`)
- Hermite evaluation accuracy vs Unity's `AnimationCurve.Evaluate()`
- Edge cases: t=0, t=1, t < 0, t > 1 (clamp behavior)
- Single keyframe → constant value
- Empty keyframes → linear fallback
- Curve shapes: linear, ease-in, ease-out, ease-in-out, S-curve
- Tangent handling: steep tangents, zero tangents, negative tangents

### Conversion Tests
- `IsLinearCurve()` returns true for default `AnimationCurve.Linear(0,1,1,0)`
- `IsLinearCurve()` returns false for ease-in, ease-out, custom curves
- Linear curves → empty keyframes array (fast-path)
- Non-linear curves → correct keyframe count baked
- Y-axis inversion: `blobValue = 1 - unityValue`
- Tangent negation: `blobTangent = -unityTangent`

### Integration Tests
- Full pipeline: Author curve → Bake → Runtime evaluation
- Verify: `CurveUtils.EvaluateCurve(baked, t) ≈ 1 - unityCurve.Evaluate(t)`
- Runtime weight progression matches expected curve shape

### Visual Validation
- Preview window curve matches authored curve visually
- Runtime playback: weight progression matches preview exactly
- A/B test: linear vs custom curve, verify visible difference

### Performance Tests
- **Fast-path benchmark**: Empty keyframes (linear) vs current implementation
  - Should be identical (within noise margin)
- **Hermite benchmark**: 2-keyframe curve vs current linear
  - Establish baseline overhead (~20 ops)
- **Mixed workload**: 90 linear + 10 custom transitions
  - Verify negligible overall impact
- Profile: 100+ simultaneous transitions
- Verify: < 0.1ms total overhead per frame

---

## Files Summary

### New Files
| File | Purpose |
|------|---------|
| `Runtime/Utils/CurveUtils.cs` | Hermite spline evaluation utility |

### Runtime Changes (by Phase)

**Phase 0: Transition Reference Infrastructure**
| File | Change |
|------|--------|
| `AnimationState.cs` | Add curve source indices to `AnimationStateTransition` |
| `UpdateStateMachineJob.cs` | Populate curve indices when transition starts |

**Phase 1: Blob Data Model**
| File | Change |
|------|--------|
| `AnimationTransition.cs` | Add `CurveKeyframe` struct, add `CurveKeyframes` to transitions |

**Phase 2: Conversion**
| File | Change |
|------|--------|
| `StateMachineBlobConverter.cs` | Bake keyframes with `IsLinearCurve()` fast-path |
| `AnimationStateMachineConversionUtils.cs` | Convert `AnimationCurve` to blittable keyframes |

**Phase 3: Runtime Evaluation**
| File | Change |
|------|--------|
| `BlendAnimationStatesSystem.cs` | Use `CurveUtils.EvaluateCurve()` for weight |

### Editor Changes (Phases 4-5)
| File | Change |
|------|--------|
| `TransitionGroupInspector.cs` | Add curve UI to state machine editor |
| `CurvePreviewElement.cs` | Add factory method for reuse |
| `TransitionInspectorBuilder.cs` | Already has curve UI (verify sync) |

---

## Open Questions

1. **Half precision**: Is `half` sufficient for tangents? 
   - Range: ±65504, precision: 0.001 at value 1.0
   - Should be fine for typical tangent values (-10 to +10)
   - Can upgrade to `float` if precision issues arise

2. **Maximum keyframes**: Should we limit keyframe count?
   - Unity allows unlimited, but typical curves have 2-4
   - Consider max 16 keyframes (128 bytes) as sanity limit
   - Log warning during conversion if exceeded

3. **Segment search optimization**: Linear search vs binary search?
   - For 2-4 keyframes: linear is faster (no branch overhead)
   - For 8+ keyframes: binary search would be better
   - Current implementation uses linear (sufficient for typical use)

4. **Weighted tangent mode**: Unity supports `WeightedMode` for bezier-like control
   - Current implementation ignores weights (Hermite only)
   - Can add later if needed (additional 2 halfs per keyframe)

---

## Related Documents

- **[UIToolkitMigration.md](./UIToolkitMigration.md)** - CurvePreviewElement is now UIToolkit-based
- **[AnimationPreviewWindow.md](./AnimationPreviewWindow.md)** - Existing curve preview location

---

## Definition of Done

### Phase 0: Infrastructure
- [x] `AnimationStateTransition` captures curve source indices
- [x] `UpdateStateMachineJob` populates indices for state and Any State transitions
- [x] Existing transition behavior unchanged

### Phase 1-2: Blob & Conversion  
- [x] `CurveKeyframe` struct added to `AnimationTransition.cs`
- [x] `IsLinearCurve()` detects default linear curves
- [x] Linear curves → empty keyframes (0 bytes, fast-path)
- [x] Non-linear curves baked with Y-axis/tangent inversion

### Phase 3: Runtime
- [x] `CurveUtils.EvaluateCurve()` with Hermite interpolation
- [x] `BlendAnimationStatesSystem` uses curve lookup for weight
- [x] Linear fast-path: empty keyframes returns `t` directly

### Phase 4-5: Editor
- [x] Curve editable in State Machine Editor (transition inspector)
- [x] Curve editable in Animation Preview Window (existing)
- [x] Changes sync between windows (via shared SerializedProperty)

### Validation
- [x] Preview weight progression matches runtime exactly (shared CurveUtils)
- [x] Unit tests for Hermite evaluation pass
- [x] `IsLinearCurve()` tests pass
- [x] Integration tests for full pipeline pass
- [ ] Performance: linear transitions have zero overhead vs current
- [ ] Performance: < 0.1ms overhead for 100 custom curve transitions
- [x] Documentation updated
