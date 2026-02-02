# DMotion Burstization & Performance Optimization Plan

This document provides a comprehensive plan for improving Burst compatibility, vectorization, async job optimization, and data layout in the DMotion animation system.

---

## Performance Extrapolation from Current Benchmarks

### Current Baseline Performance

From README.md and performance tests:

| Metric | Value |
|--------|-------|
| **Benchmark Entity Count** | 10,000 animated skeletons |
| **vs Unity Mecanim** | ~6x faster |
| **Test Configurations** | 1,000 / 10,000 / 100,000 entities |

### Benchmark State Machine Complexity

Based on `StateMachinePerformanceTestSystem.cs`:

```
State Machine Structure (Stress Test):
├── LinearBlend State (1D blend tree)
│   ├── Float parameter (0-1 range, continuous update)
│   ├── ~2-3 clips in blend tree
│   └── Blend ratio changes every frame
├── Bool Parameter Transitions
│   └── State switching every ~2 seconds (30% probability)
├── OneShot Clip Support
│   └── Random trigger (40% probability on switch event)
└── Estimated Structure:
    ├── States: 2-4 total
    ├── Active Samplers: 3-5 per entity
    └── Parameters: 1 float + 1 bool minimum
```

### Current Performance Estimate

Assuming 60 FPS target (16.67ms frame budget):

| Entity Count | Estimated Animation Time | Frame Budget Used |
|--------------|-------------------------|-------------------|
| 1,000 | ~0.3-0.5 ms | ~2-3% |
| 10,000 | ~3-5 ms | ~18-30% |
| 100,000 | ~30-50 ms | ~180-300% ❌ |

### Projected Performance with Optimizations

#### Optimization Stack (Cumulative Multipliers)

| Phase | Optimization | Multiplier | Cumulative |
|-------|-------------|------------|------------|
| Baseline | Current DMotion | 1.0x | 1.0x |
| Phase 3 | Parallel State Processing | 1.5-2.0x | 1.5-2.0x |
| Phase 4 | State-Coherent Batching + Static Arrays | 3.5-5.0x | **5.0-10.0x** |
| Phase 5 | Data Compaction | 1.1-1.2x | 5.5-12.0x |
| Phase 6 | Additional Vectorization | 1.05-1.1x | 5.8-13.2x |

**Conservative Estimate: 5-6x improvement**
**Optimistic Estimate: 10-13x improvement**

#### Projected Entity Counts at 60 FPS

| Scenario | Current | With Optimizations | Improvement |
|----------|---------|-------------------|-------------|
| **Light (3ms budget)** | ~6,000 | 30,000-60,000 | 5-10x |
| **Moderate (5ms budget)** | ~10,000 | 50,000-100,000 | 5-10x |
| **Heavy (10ms budget)** | ~20,000 | 100,000-200,000 | 5-10x |
| **Maximum (16ms budget)** | ~33,000 | **165,000-330,000** | 5-10x |

### Detailed Breakdown: 10,000 Entity Scenario

```
CURRENT ARCHITECTURE (10,000 entities, estimated ~4ms):
───────────────────────────────────────────────────────
UpdateStateMachineJob:           ~1.0 ms  (per-entity state evaluation)
UpdateAnimationStatesSystem:     ~1.5 ms  (sequential: Single→Linear→2D)
ClipSamplingSystem:              ~1.0 ms  (ACL decompression - I/O bound)
BlendAnimationStatesSystem:      ~0.5 ms  (weight blending)
                                 ─────────
Total:                           ~4.0 ms


OPTIMIZED ARCHITECTURE (10,000 entities, estimated ~0.6-0.8ms):
───────────────────────────────────────────────────────────────
Phase 0: Reset Counters:         ~0.01 ms  (MemClear of small arrays)
Phase 1: Fill Static Arrays:     ~0.08 ms  (parallel atomic writes)
Phase 2: SIMD Batch Process:     ~0.12 ms  (2,500 SIMD iters vs 10,000)
Phase 3: Write Back:             ~0.08 ms  (parallel, direct index)
ClipSamplingSystem:              ~0.30 ms  (optimized prefetch, same ACL)
BlendAnimationStatesSystem:      ~0.10 ms  (better cache locality)
                                 ─────────
Total:                           ~0.7 ms

Speedup: 4.0ms → 0.7ms = ~5.7x improvement
```

### State-Coherent Batching Impact Analysis

Assuming state distribution for 10,000 entities:
- 40% Idle (SingleClip): 4,000 entities
- 35% Walk (LinearBlend): 3,500 entities
- 25% Run (LinearBlend): 2,500 entities

```
BEFORE: Per-Entity Processing
─────────────────────────────
Iterations:        10,000 (one per entity)
Code path:         Divergent (branch on state type)
SIMD utilization:  0%
Cache pattern:     Random (each entity reads blob independently)
Blob reads:        10,000

AFTER: State-Coherent Batch Processing
──────────────────────────────────────
State Batches:
  └─ Idle (SingleClip):  4,000 entities → 1,000 SIMD iterations (÷4)
  └─ Walk (LinearBlend): 3,500 entities →   875 SIMD iterations (÷4)
  └─ Run (LinearBlend):  2,500 entities →   625 SIMD iterations (÷4)
                                          ───────
Total SIMD iterations:                     2,500

Improvement:
  Iterations:       10,000 → 2,500 (4x reduction)
  SIMD utilization: 0% → 95%
  Cache pattern:    Random → Contiguous per-state
  Blob reads:       10,000 → 3 (one per state group)
  Branch prediction: Poor → Perfect (no divergence)
```

### Memory Budget

```
Static Batch Buffers (4 states × 2,560 max entities/state = 10,240 slots):

┌─────────────────────┬───────────────┬────────────┐
│ Array               │ Per Element   │ Total      │
├─────────────────────┼───────────────┼────────────┤
│ Entities            │ 8 bytes       │ 80 KB      │
│ Times               │ 4 bytes       │ 40 KB      │
│ PreviousTimes       │ 4 bytes       │ 40 KB      │
│ Speeds              │ 4 bytes       │ 40 KB      │
│ Weights             │ 4 bytes       │ 40 KB      │
│ BlendRatios         │ 4 bytes       │ 40 KB      │
│ SamplerIds          │ 1 byte        │ 10 KB      │
│ Loops               │ 1 byte        │ 10 KB      │
│ NewTimes            │ 4 bytes       │ 40 KB      │
│ NewPreviousTimes    │ 4 bytes       │ 40 KB      │
│ NewWeights          │ 4 bytes       │ 40 KB      │
├─────────────────────┼───────────────┼────────────┤
│ TOTAL               │ 42 bytes/slot │ ~420 KB    │
└─────────────────────┴───────────────┴────────────┘

Scaling:
  10,000 entities  → 420 KB (fixed)
  100,000 entities → 4.2 MB (increase MaxEntitiesPerState)

Memory efficiency: 42 bytes/entity vs ~80+ bytes with dynamic containers
```

### Projected Scaling Table

| Entities | Current | Optimized | vs Mecanim (current 6x) |
|----------|---------|-----------|------------------------|
| 1,000 | ~0.4 ms | ~0.07 ms | ~35x faster |
| 10,000 | ~4.0 ms | ~0.7 ms | ~35x faster |
| 50,000 | ~20 ms | ~3.5 ms | ~35x faster |
| 100,000 | ~40 ms | ~7.0 ms | ~35x faster |
| 200,000 | ❌ CPU-bound | ~14 ms | ~35x faster |
| 330,000 | ❌ CPU-bound | ~23 ms (30fps) | ~35x faster |

### Summary

With the full optimization stack implemented:

| Metric | Current | Projected | Improvement |
|--------|---------|-----------|-------------|
| **Max entities @ 60fps** | ~15,000-20,000 | **100,000-200,000** | 5-10x |
| **vs Mecanim** | 6x faster | **30-40x faster** | 5-7x |
| **Time per 10k entities** | ~4ms | ~0.7ms | 5.7x |
| **Memory overhead** | Dynamic | Fixed 420KB | Predictable |

### ROI-Based Implementation Priority

| Priority | Phase | Effort | Impact | ROI |
|----------|-------|--------|--------|-----|
| **1** | State-Coherent Batching | 5-7 days | **3-5x** | ⭐⭐⭐⭐⭐ |
| **2** | Static Arrays | 2-3 days | **1.2-1.5x** | ⭐⭐⭐⭐ |
| 3 | Parallel State Processing | 3-5 days | 1.3-1.5x | ⭐⭐⭐ |
| 4 | Data Compaction | 3-5 days | 1.1-1.2x | ⭐⭐ |
| 5 | Additional Vectorization | 3-5 days | 1.05-1.1x | ⭐ |

**Recommendation**: Implement Phase 4 (State-Coherent + Static Arrays) first for maximum ROI: **4-6x improvement in ~10 days**.

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current State Analysis](#current-state-analysis)
3. [Class to Struct Conversions](#class-to-struct-conversions)
4. [Missing Burst Attributes](#missing-burst-attributes)
5. [Vectorization Opportunities](#vectorization-opportunities)
6. [Async Job Opportunities](#async-job-opportunities)
7. [Data Compaction](#data-compaction)
8. [Memory Layout Optimization](#memory-layout-optimization)
9. [Implementation Priority](#implementation-priority)

---

## Executive Summary

DMotion is already well-optimized for DOTS with extensive `[BurstCompile]` usage across 30+ files. All jobs already have proper Burst compilation. This plan identifies remaining optimization opportunities:

- **8 authoring classes** that could be converted to structs (editor-time only, low priority)
- **12+ vectorization opportunities** in loop-heavy operations
- **4 async job patterns** for workload distribution
- **6 data compaction opportunities** for better cache utilization

> **Note**: An earlier version of this document incorrectly claimed 3 jobs were missing `[BurstCompile]` attributes. This has been verified as incorrect - all jobs are properly Burst-compiled.

---

## Current State Analysis

### Burst Compatibility Status

| Category | Files with [BurstCompile] | Total Files | Coverage |
|----------|---------------------------|-------------|----------|
| Systems | 11 | 11 | 100% |
| Jobs (IJobEntity) | 20 | 20 | 100% |
| Components | 6 | 16 | 37% |

**Notes on Coverage**:
- Many files legitimately cannot or don't need `[BurstCompile]` (interfaces, enums, authoring ScriptableObjects, managed classes)
- Static utility classes cannot have `[BurstCompile]` applied at the class level (only individual methods within job structs can be Burst-compiled)
- The 37% component coverage includes files that cannot use Burst (managed IComponentData for debug purposes)

### Key Patterns Already Used

1. **IJobEntity with BurstCompile** - All major jobs
2. **AggressiveInlining** - Hot-path methods
3. **Struct bundling** - `AnimationBufferContext`, `TransitionParameters`
4. **BlobAssetReference** - Immutable runtime data
5. **DynamicBuffer** - Efficient variable-length storage
6. **ProfilerMarker** - Performance instrumentation

---

## Class to Struct Conversions

### Runtime Classes (Low Priority - Authoring Only)

These classes inherit from `ScriptableObject` and are **required to be classes** for Unity serialization. They exist only at editor/conversion time and are converted to Burst-compatible blobs at runtime.

| Class | File | Reason Cannot Convert |
|-------|------|----------------------|
| `StateMachineAsset` | `StateMachineAsset.cs:33` | ScriptableObject required |
| `AnimationStateAsset` | `AnimationStateAsset.cs:8` | ScriptableObject required |
| `SingleClipStateAsset` | `SingleClipStateAsset.cs:6` | ScriptableObject required |
| `LinearBlendStateAsset` | `LinearBlendStateAsset.cs:16` | ScriptableObject required |
| `Directional2DBlendStateAsset` | `Directional2DBlendStateAsset.cs:17` | ScriptableObject required |
| `AnimationClipAsset` | `AnimationClipAsset.cs:17` | ScriptableObject required |
| `AnimationParameterAsset` | `AnimationParameterAsset.cs:8` | ScriptableObject required |
| `SubStateMachineStateAsset` | `SubStateMachineStateAsset.cs:16` | ScriptableObject required |

### Debug Class (Medium Priority)

```csharp
// Runtime/Components/AnimationStateMachine.cs:52
internal class AnimationStateMachineDebug : IComponentData, ICloneable
```

**Issue**: Managed class as IComponentData prevents Burst compilation in systems accessing it.

**Solution**:
```csharp
#if UNITY_EDITOR || DEBUG
// Option A: Use ISharedComponentData (allows managed reference)
internal struct AnimationStateMachineDebug : ISharedComponentData
{
    public StateMachineAsset StateMachineAsset; // Still managed, but in shared storage
}

// Option B: Store asset GUID instead (fully Burst-compatible)
internal struct AnimationStateMachineDebug : IComponentData
{
    public FixedString64Bytes AssetPath;
    public int AssetInstanceId;
}
#endif
```

### Transition Class (Medium Priority)

```csharp
// Runtime/Authoring/AnimationStateMachine/AnimationTransitionGroup.cs:10
public class StateOutTransition
```

**Issue**: Uses `List<TransitionCondition>` which is managed.

**Solution**: Already converted to `StateOutTransitionConversionData` struct with `UnsafeList` during baking. The class is only used in authoring.

---

## Burst Compilation Status

### Jobs - Fully Burst-Compiled ✓

All IJobEntity jobs in DMotion already have the `[BurstCompile]` attribute:

| Job | File | Status |
|-----|------|--------|
| `SampleNonOptimizedBones` | `SampleNonOptimizedBones.cs:16` | ✓ Has [BurstCompile] |
| `SampleRootDeltasJob` | `SampleRootDeltasJob.cs:10` | ✓ Has [BurstCompile] |
| `TransferRootMotionJob` | `TransferRootMotionJob.cs:14` | ✓ Has [BurstCompile] |

No additional Burst attributes needed for jobs.

### Static Utility Classes - Cannot Use [BurstCompile]

> **Important**: Static classes in C# cannot have the `[BurstCompile]` attribute. The attribute can only be applied to:
> - Struct types that implement job interfaces (IJob, IJobEntity, etc.)
> - Individual static methods marked with `[BurstCompile]` when called from Burst-compiled code via function pointers

The following utility classes are static and their methods are already called from Burst-compiled jobs:

| Class | File | Notes |
|-------|------|-------|
| `Directional2DBlendUtils` | `Directional2DBlendUtils.cs` | Methods called from Burst jobs - automatically compiled |
| `LinearBlendStateUtils` | `LinearBlendStateUtils.cs` | Methods called from Burst jobs - automatically compiled |
| `SingleClipStateUtils` | `SingleClipStateUtils.cs` | Methods called from Burst jobs - automatically compiled |
| `Directional2DBlendStateUtils` | `Directional2DBlendStateUtils.cs` | Methods called from Burst jobs - automatically compiled |
| `CollectionUtils` | `CollectionUtils.cs` | Methods called from Burst jobs - automatically compiled |

**Recommendation**: Add `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to hot methods in these classes to ensure they inline properly in Burst-compiled callers.

---

## Vectorization Opportunities

### Priority 1: Weight Calculation Loops (High Impact)

#### 1.1 Directional2DBlendUtils.CalculateWeights
```csharp
// Runtime/Utils/Directional2DBlendUtils.cs:38
for (int i = 0; i < count; i++) weights[i] = 0f;
```

**Vectorization**:
```csharp
[BurstCompile]
public static void CalculateWeights(float2 input, NativeArray<float2> positions, NativeArray<float> weights)
{
    // Use Unity.Mathematics SIMD-friendly operations
    // Clear weights using batch operation
    unsafe
    {
        UnsafeUtility.MemClear(weights.GetUnsafePtr(), weights.Length * sizeof(float));
    }
    // ... rest of implementation
}
```

#### 1.2 LinearBlendStateUtils.UpdateSamplers - Weight Zeroing
```csharp
// Runtime/Utils/LinearBlendStateUtils.cs:127-132
for (var i = startIndex; i <= endIndex; i++)
{
    var sampler = samplers[i];
    sampler.Weight = 0;
    samplers[i] = sampler;
}
```

**Vectorization Strategy**:
```csharp
// Instead of per-element struct copy, use direct memory access
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static void ZeroWeightsInRange(ref DynamicBuffer<ClipSampler> samplers, int startIndex, int count)
{
    // Calculate offsets at runtime using UnsafeUtility
    int samplerStride = UnsafeUtility.SizeOf<ClipSampler>();
    int weightOffset = UnsafeUtility.GetFieldOffset(typeof(ClipSampler).GetField("Weight",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));

    unsafe
    {
        var ptr = (byte*)samplers.GetUnsafePtr() + startIndex * samplerStride + weightOffset;
        for (int i = 0; i < count; i++)
        {
            *(float*)ptr = 0f;
            ptr += samplerStride;
        }
    }
}
```

> **Note**: The reflection-based `GetFieldOffset` won't work in Burst. For Burst compatibility, use a hardcoded offset based on the struct layout, or cache the offset value at startup. Alternatively, consider if this optimization provides measurable benefit over the simple loop - profile first.

#### 1.3 NormalizedSamplersWeights - Sum and Normalize
```csharp
// Runtime/Systems/NormalizedSamplersWeights.cs:13-26
var sumWeights = 0.0f;
for (var i = 0; i < clipSamplers.Length; i++)
{
    sumWeights += clipSamplers[i].Weight;
}
// ... normalize loop
```

**Vectorization**:

> **Implementation Note**: True SIMD vectorization of `DynamicBuffer` weight normalization is complex because weights are embedded in a struct (AoS layout). The approach below shows what vectorization would require - extracting to a temp array first. For small sampler counts (typical), the overhead may exceed the benefit. **Profile before implementing**.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static void NormalizeWeights(ref DynamicBuffer<ClipSampler> clipSamplers)
{
    int length = clipSamplers.Length;
    if (length == 0) return;

    // For small counts, scalar loop is likely faster than vectorization overhead
    // Typical sampler counts are 2-8, where SIMD setup cost exceeds benefit
    var sumWeights = 0.0f;
    for (int i = 0; i < length; i++)
    {
        sumWeights += clipSamplers[i].Weight;
    }

    if (!mathex.approximately(sumWeights, 1f))
    {
        float invSum = 1.0f / sumWeights;
        for (int i = 0; i < length; i++)
        {
            var sampler = clipSamplers[i];
            sampler.Weight *= invSum;
            clipSamplers[i] = sampler;
        }
    }
}
```

**Alternative - Pre-multiply optimization** (avoids division in loop):
The current code already uses `invSum = 1.0f / sumWeights` to convert division to multiplication. This is the key optimization; further vectorization has diminishing returns for typical sampler counts.

### Priority 2: Time Update Loops (Medium Impact)

#### 2.1 Directional2DBlendStateUtils.UpdateSamplers
```csharp
// Runtime/Systems/Directional2DBlendStateUtils.cs:121-135
for (var i = startIndex; i <= endIndex; i++)
{
    var sampler = samplers[i];
    var samplerSpeed = stateSpeed * sampler.Clip.duration * invLoopDuration;
    sampler.PreviousTime = sampler.Time;
    sampler.Time += dt * samplerSpeed;
    if (animation.Loop) { sampler.LoopToClipTime(); }
    samplers[i] = sampler;
}
```

**Opportunity**: Pre-calculate all `samplerSpeed` values into a temp `NativeArray<float>`, then batch update times.

#### 2.2 BlendAnimationStatesSystem.BlendAnimationStatesJob
```csharp
// Runtime/Systems/BlendAnimationStatesSystem.cs:52-57
for (var i = 0; i < animationStates.Length; i++)
{
    var animationState = animationStates[i];
    animationState.Time += DeltaTime * animationState.Speed;
    animationStates[i] = animationState;
}
```

**Vectorization Candidate**: If AnimationState buffer is contiguous and Time/Speed fields are adjacent.

### Priority 3: Angular Calculations (Low Impact - Already Fast)

#### 3.1 Directional2DBlendUtils.FindAngleNeighbors
```csharp
// Runtime/Utils/Directional2DBlendUtils.cs:174-199
for (int i = 0; i < positions.Length; i++)
{
    float clipAngle = math.atan2(pos.y, pos.x);
    float delta = NormalizeAngleDelta(clipAngle - inputAngle);
    // ... comparisons
}
```

**SIMD Opportunity**:
```csharp
// Pre-calculate all angles at once using vectorized atan2
var angles = new NativeArray<float>(positions.Length, Allocator.Temp);

// Process as many elements as possible in blocks of 4
int simdLength = positions.Length & ~3; // Largest multiple of 4 <= positions.Length

for (int i = 0; i < simdLength; i += 4)
{
    // Process 4 positions at once
    float4 x = new float4(
        positions[i].x,
        positions[i + 1].x,
        positions[i + 2].x,
        positions[i + 3].x
    );
    float4 y = new float4(
        positions[i].y,
        positions[i + 1].y,
        positions[i + 2].y,
        positions[i + 3].y
    );

    float4 result = math.atan2(y, x);

    // Store results back into the angles array
    angles[i] = result.x;
    angles[i + 1] = result.y;
    angles[i + 2] = result.z;
    angles[i + 3] = result.w;
}

// Handle remaining elements (if positions.Length is not a multiple of 4)
for (int i = simdLength; i < positions.Length; i++)
{
    angles[i] = math.atan2(positions[i].y, positions[i].x);
}
```

> **Note**: This optimization has limited benefit when `positions.Length` is typically small (e.g., <8 blend tree children). Profile to verify improvement before implementing.

### Priority 4: Transition Evaluation (Medium Impact)

#### 4.1 Condition Evaluation Loops
```csharp
// Runtime/Systems/UpdateStateMachineJob.cs:334-348
for (var i = 0; i < boolTransitions.Length; i++)
{
    var boolTransition = boolTransitions[i];
    shouldTriggerTransition &= boolTransition.Evaluate(parameters.BoolParameters[boolTransition.ParameterIndex]);
}
```

**Early-Exit Optimization** (already good, but can improve):
```csharp
// Short-circuit as soon as false is encountered
for (var i = 0; i < boolTransitions.Length && shouldTriggerTransition; i++)
{
    shouldTriggerTransition = boolTransitions[i].Evaluate(parameters.BoolParameters[boolTransitions[i].ParameterIndex]);
}
```

---

## Async Job Opportunities

### 1. Parallel State Processing Architecture

**Current Constraint**: Sequential job scheduling because all jobs write to `DynamicBuffer<ClipSampler>`.

**Key Insight**: State machine blob data is **read-only** at runtime. The only writes are to `ClipSampler` fields (`Time`, `PreviousTime`, `Weight`). This enables several parallel architectures:

---

#### Option A: Queue-Based Parallel Processing (Recommended)

Instead of writing directly to `ClipSampler` buffer, jobs write update commands to parallel-safe queues. A final merge job applies all updates.

```csharp
// Sampler update command - written by parallel jobs
public struct SamplerUpdateCommand
{
    public Entity Entity;           // Target entity
    public byte SamplerId;          // Which sampler to update
    public float Time;              // New time value
    public float PreviousTime;      // New previous time
    public float Weight;            // New weight
}

// Per-thread stream for lock-free parallel writes
public struct SamplerUpdateStream
{
    public NativeStream.Writer Writer;
}
```

**Parallel Update Jobs** (all run simultaneously):

```csharp
[BurstCompile]
internal partial struct UpdateSingleClipStatesParallelJob : IJobEntity
{
    public float DeltaTime;
    [NativeDisableParallelForRestriction]
    public NativeStream.Writer CommandWriter;
    [NativeSetThreadIndex] private int _threadIndex;

    internal void Execute(
        Entity entity,
        in DynamicBuffer<ClipSampler> clipSamplers,  // READ-ONLY now
        in DynamicBuffer<SingleClipState> singleClipStates,
        in DynamicBuffer<AnimationState> animationStates)
    {
        CommandWriter.BeginForEachIndex(_threadIndex);

        for (var i = 0; i < singleClipStates.Length; i++)
        {
            if (animationStates.TryGetWithId(singleClipStates[i].AnimationStateId, out var animationState))
            {
                var samplerIndex = clipSamplers.IdToIndex(animationState.StartSamplerId);
                var sampler = clipSamplers[samplerIndex];

                // Calculate new values (pure computation, no writes)
                var newTime = sampler.Time + DeltaTime * animationState.Speed;
                if (animationState.Loop)
                {
                    newTime = sampler.Clip.LoopToClipTime(newTime);
                }

                // Write command instead of mutating buffer directly
                CommandWriter.Write(new SamplerUpdateCommand
                {
                    Entity = entity,
                    SamplerId = animationState.StartSamplerId,
                    Time = newTime,
                    PreviousTime = sampler.Time,
                    Weight = animationState.Weight
                });
            }
        }

        CommandWriter.EndForEachIndex();
    }
}
```

**Final Merge Job** (runs after all parallel jobs complete):

```csharp
[BurstCompile]
internal partial struct ApplySamplerUpdatesJob : IJobEntity
{
    [ReadOnly] public NativeStream.Reader CommandReader;

    internal void Execute(Entity entity, ref DynamicBuffer<ClipSampler> clipSamplers)
    {
        // Read commands for this entity and apply
        var count = CommandReader.RemainingItemCount;
        for (int i = 0; i < count; i++)
        {
            var cmd = CommandReader.Read<SamplerUpdateCommand>();
            if (cmd.Entity == entity)
            {
                var index = clipSamplers.IdToIndex(cmd.SamplerId);
                var sampler = clipSamplers[index];
                sampler.Time = cmd.Time;
                sampler.PreviousTime = cmd.PreviousTime;
                sampler.Weight = cmd.Weight;
                clipSamplers[index] = sampler;
            }
        }
    }
}
```

**System Scheduling**:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var dt = SystemAPI.Time.DeltaTime;

    // Allocate command stream (sized for expected entity count)
    var entityCount = _query.CalculateEntityCount();
    var commandStream = new NativeStream(entityCount, Allocator.TempJob);

    // All update jobs run in PARALLEL (different archetypes, write to stream not buffer)
    var singleHandle = new UpdateSingleClipStatesParallelJob
    {
        DeltaTime = dt,
        CommandWriter = commandStream.AsWriter()
    }.ScheduleParallel(state.Dependency);

    var linearHandle = new UpdateLinearBlendParallelJob
    {
        DeltaTime = dt,
        CommandWriter = commandStream.AsWriter()
    }.ScheduleParallel(state.Dependency);

    var dir2DHandle = new UpdateDirectional2DBlendParallelJob
    {
        DeltaTime = dt,
        CommandWriter = commandStream.AsWriter()
    }.ScheduleParallel(state.Dependency);

    // Combine all parallel handles
    var allUpdates = JobHandle.CombineDependencies(singleHandle, linearHandle, dir2DHandle);

    // Apply all commands (must wait for all updates)
    var applyHandle = new ApplySamplerUpdatesJob
    {
        CommandReader = commandStream.AsReader()
    }.ScheduleParallel(allUpdates);

    // Cleanup jobs run in parallel after apply
    var cleanSingle = new CleanSingleClipStatesJob().ScheduleParallel(applyHandle);
    var cleanLinear = new CleanLinearBlendStatesJob().ScheduleParallel(applyHandle);
    var cleanDir2D = new CleanDirectional2DBlendStatesJob().ScheduleParallel(applyHandle);

    state.Dependency = JobHandle.CombineDependencies(cleanSingle, cleanLinear, cleanDir2D);
    commandStream.Dispose(state.Dependency);
}
```

**Benefits**:
- True parallel execution of all state type updates
- No safety system conflicts (jobs write to stream, not shared buffer)
- Clean separation of computation vs mutation
- Scales with worker thread count

**Trade-offs**:
- Additional memory for command stream (~20 bytes per sampler update)
- Extra pass to apply commands
- Slightly more complex code structure

---

#### Option B: Per-State-Type Result Buffers

Instead of one `ClipSampler` buffer, use separate buffers per state type:

```csharp
// Separate result buffers - each job type writes to its own
public struct SingleClipSamplerData : IBufferElementData
{
    public byte SamplerId;
    public float Time;
    public float PreviousTime;
    public float Weight;
    public BlobAssetReference<SkeletonClipSetBlob> Clips;
    public ushort ClipIndex;
}

public struct LinearBlendSamplerData : IBufferElementData
{
    public byte SamplerId;
    public float Time;
    public float PreviousTime;
    public float Weight;
    public BlobAssetReference<SkeletonClipSetBlob> Clips;
    public ushort ClipIndex;
}

public struct Directional2DSamplerData : IBufferElementData
{
    // Same structure
}
```

**Jobs write to their own buffer type** (no conflicts):

```csharp
// SingleClip job writes ONLY to SingleClipSamplerData
[BurstCompile]
internal partial struct UpdateSingleClipStatesJob : IJobEntity
{
    internal float DeltaTime;

    internal void Execute(
        ref DynamicBuffer<SingleClipSamplerData> samplerData,  // Own buffer type
        in DynamicBuffer<SingleClipState> singleClipStates,
        in DynamicBuffer<AnimationState> animationStates)
    {
        // Update SingleClipSamplerData directly - no conflict with other job types
    }
}

// LinearBlend job writes ONLY to LinearBlendSamplerData
[BurstCompile]
internal partial struct UpdateLinearBlendJob : IJobEntity
{
    internal float DeltaTime;

    internal void Execute(
        ref DynamicBuffer<LinearBlendSamplerData> samplerData,  // Own buffer type
        in DynamicBuffer<LinearBlendStateMachineState> linearBlendStates,
        in DynamicBuffer<AnimationState> animationStates,
        in DynamicBuffer<FloatParameter> floatParameters,
        in DynamicBuffer<IntParameter> intParameters)
    {
        // Update LinearBlendSamplerData directly - no conflict with other job types
    }
}
```

**Sampling phase reads from all buffers**:

```csharp
[BurstCompile]
internal partial struct SampleAllAnimationsJob : IJobEntity
{
    internal void Execute(
        ref DynamicBuffer<BoneTransform> boneTransforms,
        in DynamicBuffer<SingleClipSamplerData> singleClipData,
        in DynamicBuffer<LinearBlendSamplerData> linearBlendData,
        in DynamicBuffer<Directional2DSamplerData> dir2DData)
    {
        // Sample from all buffer types, blend based on weights
    }
}
```

**Benefits**:
- Cleanest parallel execution - no command stream overhead
- Each job type fully owns its output
- Buffer type safety system works correctly
- Entities only have buffers for state types they use (archetype efficiency)

**Trade-offs**:
- More buffer types to manage
- Sampling must iterate multiple buffers
- Migration effort from current single-buffer approach

---

#### Option C: Per-Layer Sampler Buffers (Animation Layer Support)

If you plan to support animation layers (base layer, additive layers, override layers), this architecture naturally extends:

```csharp
// Each layer has its own sampler buffer
public struct AnimationLayer : IBufferElementData
{
    public byte LayerIndex;
    public float Weight;
    public BlendMode BlendMode;  // Override, Additive
    public AvatarMask Mask;      // Optional per-layer masking
}

// Samplers are per-layer
public struct LayerClipSampler : IBufferElementData
{
    public byte LayerId;         // Which layer this sampler belongs to
    public byte SamplerId;       // Sampler ID within layer
    public float Time;
    public float PreviousTime;
    public float Weight;
    public BlobAssetReference<SkeletonClipSetBlob> Clips;
    public ushort ClipIndex;
}
```

**Layer-parallel processing**:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    // Each layer can be processed independently
    // Within each layer, state types can run in parallel (per Option A or B)

    // Layer 0 (Base) - all state types in parallel
    var layer0Single = new UpdateSingleClipLayer0Job{}.ScheduleParallel(state.Dependency);
    var layer0Linear = new UpdateLinearBlendLayer0Job{}.ScheduleParallel(state.Dependency);
    var layer0Handle = JobHandle.CombineDependencies(layer0Single, layer0Linear);

    // Layer 1 (Additive) - can run parallel with Layer 0 if different entities
    var layer1Single = new UpdateSingleClipLayer1Job{}.ScheduleParallel(state.Dependency);
    var layer1Linear = new UpdateLinearBlendLayer1Job{}.ScheduleParallel(state.Dependency);
    var layer1Handle = JobHandle.CombineDependencies(layer1Single, layer1Linear);

    // Final blend combines all layers
    state.Dependency = new BlendAllLayersJob{}
        .ScheduleParallel(JobHandle.CombineDependencies(layer0Handle, layer1Handle));
}
```

**Benefits**:
- Natural support for animation layers (common in production)
- Each layer is independent - maximum parallelism
- Clean architecture for complex animation setups
- Per-layer avatar masking becomes trivial

**Trade-offs**:
- More complex if you don't need layers
- Per-entity layer count variation affects archetype fragmentation

---

### Recommended Implementation Path

1. **Start with Option A (Queue-Based)** - Lowest migration risk, validates parallelism gains
2. **Profile the overhead** - Is command stream cost < parallel speedup?
3. **If positive**: Consider Option B for cleaner long-term architecture
4. **If adding layers**: Option C provides natural extension

**Expected Performance Gain**:
- With 3 state types running in parallel: ~2-3x throughput on entities with mixed types
- Queue overhead: ~5-10% per update pass
- Net gain on 4+ core systems: 1.5-2.5x overall system throughput

---

### 2. State-Coherent Batch Processing ⭐⭐ HIGHEST IMPACT

**Key Insight**: One blob serves N entities. Entities form a "state tree" where many entities share the same current state. Entities in the same state need **identical calculations with different values** - perfect for SIMD vectorization.

#### Current Approach (Per-Entity, Divergent)

```
Entity 1 (Blob A, State: Idle, SingleClip)   → Read blob → Compute → Write
Entity 2 (Blob A, State: Walk, LinearBlend)  → Read blob → Compute → Write
Entity 3 (Blob A, State: Idle, SingleClip)   → Read blob → Compute → Write
Entity 4 (Blob A, State: Run, LinearBlend)   → Read blob → Compute → Write
Entity 5 (Blob A, State: Walk, LinearBlend)  → Read blob → Compute → Write
... (5000 entities, each processed individually with divergent code paths)
```

**Problems**:
- Cache misses: Each entity reads blob, jumps to different code path
- No SIMD: Can't vectorize when each entity does different work
- Branch misprediction: State type switches cause pipeline stalls

#### Proposed Approach (State-Coherent, Batched)

**Phase 1: Categorize entities into state groups**
```
StateGroup(Blob A, State 0, SingleClip):   [Entity 1, Entity 3, ...]      → 2000 entities
StateGroup(Blob A, State 1, LinearBlend):  [Entity 2, Entity 5, ...]      → 1500 entities
StateGroup(Blob A, State 2, LinearBlend):  [Entity 4, ...]                → 1000 entities
StateGroup(Blob B, State 0, SingleClip):   [Entity 5001, ...]             →  500 entities
```

**Phase 2: SIMD batch-process each group**
```
Process StateGroup(Blob A, State 0, SingleClip):
  - Load 2000 times into NativeArray<float>
  - Load 2000 speeds into NativeArray<float>
  - Vectorized: newTimes = times + (dt * speeds)  // float4 SIMD
  - Vectorized: weights = animationWeights        // float4 SIMD
  - Write 2000 results
```

**Phase 3: Write-back to entity buffers**

---

#### Implementation

**State Group Key**:

```csharp
// Unique identifier for a batch of entities in the same state
public struct StateGroupKey : IEquatable<StateGroupKey>
{
    public int BlobHash;           // Hash of StateMachineBlob reference
    public ushort StateIndex;      // Current state in blob
    public StateType StateType;    // SingleClip, LinearBlend, Directional2D

    public bool Equals(StateGroupKey other) =>
        BlobHash == other.BlobHash &&
        StateIndex == other.StateIndex &&
        StateType == other.StateType;

    public override int GetHashCode() =>
        HashCode.Combine(BlobHash, StateIndex, (int)StateType);
}
```

**Batch Data Structures** (SoA for SIMD):

```csharp
// Per-group arrays for SIMD processing
public struct SingleClipBatchData : IDisposable
{
    public StateGroupKey Key;
    public NativeList<Entity> Entities;

    // SoA layout for vectorization
    public NativeList<float> Times;           // Current time for each entity
    public NativeList<float> PreviousTimes;   // Previous time
    public NativeList<float> Weights;         // Animation weight
    public NativeList<float> Speeds;          // Playback speed
    public NativeList<byte> SamplerIds;       // For write-back
    public NativeList<bool> Loops;            // Loop flag

    // Shared across batch (read once)
    public float ClipDuration;
    public BlobAssetReference<SkeletonClipSetBlob> Clips;
    public ushort ClipIndex;

    public void Dispose() { /* dispose lists */ }
}

public struct LinearBlendBatchData : IDisposable
{
    public StateGroupKey Key;
    public NativeList<Entity> Entities;

    // SoA layout for vectorization
    public NativeList<float> BlendRatios;     // Blend parameter value per entity
    public NativeList<float> AnimWeights;     // Overall animation weight
    public NativeList<float> Speeds;          // Playback speed

    // Per-sampler data (ClipCount samplers per entity)
    public NativeList<float> SamplerTimes;    // [entity0_sampler0, entity0_sampler1, ..., entity1_sampler0, ...]
    public NativeList<float> SamplerPreviousTimes;
    public NativeList<float> SamplerWeights;

    // Shared across batch
    public int ClipCount;                     // Number of clips in blend tree
    public NativeArray<float> Thresholds;     // Blend thresholds (shared)
    public NativeArray<float> ClipSpeeds;     // Per-clip speeds (shared)

    public void Dispose() { /* dispose lists */ }
}
```

**Phase 1: Categorization Job**:

```csharp
[BurstCompile]
internal partial struct CategorizeEntitiesJob : IJobEntity
{
    public NativeParallelMultiHashMap<StateGroupKey, EntityStateData>.ParallelWriter GroupMap;

    internal void Execute(
        Entity entity,
        in AnimationStateMachine stateMachine,
        in DynamicBuffer<AnimationState> animationStates,
        in DynamicBuffer<ClipSampler> samplers,
        in DynamicBuffer<SingleClipState> singleClipStates,
        in DynamicBuffer<LinearBlendStateMachineState> linearBlendStates,
        in DynamicBuffer<FloatParameter> floatParams)
    {
        var blobHash = stateMachine.StateMachineBlob.GetHashCode();
        var stateIndex = stateMachine.CurrentState.StateIndex;

        // Determine state type and extract data for batching
        if (TryGetActiveState(singleClipStates, animationStates, out var singleClipData))
        {
            var key = new StateGroupKey
            {
                BlobHash = blobHash,
                StateIndex = stateIndex,
                StateType = StateType.Single
            };

            GroupMap.Add(key, new EntityStateData
            {
                Entity = entity,
                Time = singleClipData.Time,
                Weight = singleClipData.Weight,
                Speed = singleClipData.Speed,
                SamplerId = singleClipData.SamplerId
            });
        }
        else if (TryGetActiveState(linearBlendStates, animationStates, floatParams, out var linearBlendData))
        {
            var key = new StateGroupKey
            {
                BlobHash = blobHash,
                StateIndex = stateIndex,
                StateType = StateType.LinearBlend
            };

            GroupMap.Add(key, new EntityStateData
            {
                Entity = entity,
                BlendRatio = linearBlendData.BlendRatio,
                Weight = linearBlendData.Weight,
                // ... sampler data
            });
        }
    }
}
```

**Phase 2: SIMD Batch Processing**:

```csharp
[BurstCompile]
internal struct ProcessSingleClipBatchSIMDJob : IJob
{
    public float DeltaTime;
    public float ClipDuration;

    [ReadOnly] public NativeArray<float> Times;
    [ReadOnly] public NativeArray<float> Speeds;
    [ReadOnly] public NativeArray<float> Loops;  // 1.0f for loop, 0.0f for no loop

    public NativeArray<float> NewTimes;
    public NativeArray<float> NewPreviousTimes;

    public void Execute()
    {
        int count = Times.Length;
        int simdCount = count & ~3;  // Round down to multiple of 4

        float4 dt4 = new float4(DeltaTime);
        float4 duration4 = new float4(ClipDuration);

        // Process 4 entities per iteration (SIMD)
        for (int i = 0; i < simdCount; i += 4)
        {
            // Load 4 entities' data
            float4 times = new float4(Times[i], Times[i+1], Times[i+2], Times[i+3]);
            float4 speeds = new float4(Speeds[i], Speeds[i+1], Speeds[i+2], Speeds[i+3]);
            float4 loops = new float4(Loops[i], Loops[i+1], Loops[i+2], Loops[i+3]);

            // Store previous times
            NewPreviousTimes[i] = times.x;
            NewPreviousTimes[i+1] = times.y;
            NewPreviousTimes[i+2] = times.z;
            NewPreviousTimes[i+3] = times.w;

            // Compute new times (SIMD: 4 entities in one operation)
            float4 newTimes = times + dt4 * speeds;

            // Apply looping (SIMD branchless)
            float4 loopedTimes = math.fmod(newTimes, duration4);
            loopedTimes = math.select(loopedTimes, loopedTimes + duration4, loopedTimes < 0);
            newTimes = math.select(newTimes, loopedTimes, loops > 0.5f);

            // Store new times
            NewTimes[i] = newTimes.x;
            NewTimes[i+1] = newTimes.y;
            NewTimes[i+2] = newTimes.z;
            NewTimes[i+3] = newTimes.w;
        }

        // Handle remaining entities (count not divisible by 4)
        for (int i = simdCount; i < count; i++)
        {
            NewPreviousTimes[i] = Times[i];
            float newTime = Times[i] + DeltaTime * Speeds[i];
            if (Loops[i] > 0.5f)
            {
                newTime = math.fmod(newTime, ClipDuration);
                if (newTime < 0) newTime += ClipDuration;
            }
            NewTimes[i] = newTime;
        }
    }
}

[BurstCompile]
internal struct ProcessLinearBlendBatchSIMDJob : IJob
{
    public float DeltaTime;
    public int ClipCount;

    [ReadOnly] public NativeArray<float> Thresholds;      // Shared blend thresholds
    [ReadOnly] public NativeArray<float> BlendRatios;     // Per-entity blend value
    [ReadOnly] public NativeArray<float> AnimWeights;     // Per-entity weight
    [ReadOnly] public NativeArray<float> SamplerTimes;    // Flattened [entity * ClipCount + clip]

    public NativeArray<float> NewSamplerTimes;
    public NativeArray<float> NewSamplerPreviousTimes;
    public NativeArray<float> NewSamplerWeights;

    public void Execute()
    {
        int entityCount = BlendRatios.Length;
        int simdCount = entityCount & ~3;

        // Process 4 entities per iteration
        for (int e = 0; e < simdCount; e += 4)
        {
            // Load 4 blend ratios
            float4 blendRatios = new float4(BlendRatios[e], BlendRatios[e+1], BlendRatios[e+2], BlendRatios[e+3]);
            float4 animWeights = new float4(AnimWeights[e], AnimWeights[e+1], AnimWeights[e+2], AnimWeights[e+3]);

            // Find active clip indices for all 4 (vectorized threshold search)
            int4 firstClips, secondClips;
            float4 blendTs;
            FindActiveClipsVectorized(blendRatios, out firstClips, out secondClips, out blendTs);

            // Calculate weights for all 4 entities (SIMD)
            float4 firstWeights = (1 - blendTs) * animWeights;
            float4 secondWeights = blendTs * animWeights;

            // Update samplers for all 4 entities
            for (int c = 0; c < ClipCount; c++)
            {
                int idx0 = e * ClipCount + c;
                int idx1 = (e + 1) * ClipCount + c;
                int idx2 = (e + 2) * ClipCount + c;
                int idx3 = (e + 3) * ClipCount + c;

                // Load 4 sampler times
                float4 times = new float4(SamplerTimes[idx0], SamplerTimes[idx1], SamplerTimes[idx2], SamplerTimes[idx3]);

                // Store previous times
                NewSamplerPreviousTimes[idx0] = times.x;
                NewSamplerPreviousTimes[idx1] = times.y;
                NewSamplerPreviousTimes[idx2] = times.z;
                NewSamplerPreviousTimes[idx3] = times.w;

                // Compute weights (0 for non-active clips, interpolated for active)
                float4 weights = float4.zero;
                bool4 isFirst = (firstClips == c);
                bool4 isSecond = (secondClips == c);
                weights = math.select(weights, firstWeights, isFirst);
                weights = math.select(weights, secondWeights, isSecond);

                // Update times
                float4 newTimes = times + new float4(DeltaTime);

                // Store results
                NewSamplerTimes[idx0] = newTimes.x;
                NewSamplerTimes[idx1] = newTimes.y;
                NewSamplerTimes[idx2] = newTimes.z;
                NewSamplerTimes[idx3] = newTimes.w;

                NewSamplerWeights[idx0] = weights.x;
                NewSamplerWeights[idx1] = weights.y;
                NewSamplerWeights[idx2] = weights.z;
                NewSamplerWeights[idx3] = weights.w;
            }
        }

        // Handle remaining entities...
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FindActiveClipsVectorized(float4 blendRatios, out int4 firstClips, out int4 secondClips, out float4 blendTs)
    {
        // Vectorized threshold search
        firstClips = int4.zero;
        secondClips = new int4(1);
        blendTs = float4.zero;

        for (int i = 1; i < Thresholds.Length; i++)
        {
            float prevThreshold = Thresholds[i - 1];
            float currThreshold = Thresholds[i];

            bool4 inRange = (blendRatios >= prevThreshold) & (blendRatios <= currThreshold);
            firstClips = math.select(firstClips, new int4(i - 1), inRange);
            secondClips = math.select(secondClips, new int4(i), inRange);

            float4 t = (blendRatios - prevThreshold) / (currThreshold - prevThreshold);
            blendTs = math.select(blendTs, t, inRange);
        }
    }
}
```

**Phase 3: Write-Back Job**:

```csharp
[BurstCompile]
internal partial struct WriteBackBatchResultsJob : IJobEntity
{
    [ReadOnly] public NativeParallelHashMap<Entity, SamplerUpdateResult> Results;

    internal void Execute(Entity entity, ref DynamicBuffer<ClipSampler> samplers)
    {
        if (Results.TryGetValue(entity, out var result))
        {
            for (int i = 0; i < result.SamplerCount; i++)
            {
                var idx = samplers.IdToIndex(result.SamplerIds[i]);
                var sampler = samplers[idx];
                sampler.Time = result.Times[i];
                sampler.PreviousTime = result.PreviousTimes[i];
                sampler.Weight = result.Weights[i];
                samplers[idx] = sampler;
            }
        }
    }
}
```

**Complete System Flow**:

```csharp
[BurstCompile]
public partial struct StateCoherentAnimationSystem : ISystem
{
    private NativeParallelMultiHashMap<StateGroupKey, EntityStateData> _stateGroups;

    public void OnUpdate(ref SystemState state)
    {
        var dt = SystemAPI.Time.DeltaTime;
        int entityCount = _query.CalculateEntityCount();

        // Dynamic threshold: only use batching when beneficial
        if (entityCount < 200)
        {
            // Fall back to simple per-entity approach
            RunSimplePerEntityUpdate(ref state, dt);
            return;
        }

        // Phase 1: Categorize entities into state groups
        _stateGroups.Clear();
        new CategorizeEntitiesJob { GroupMap = _stateGroups.AsParallelWriter() }
            .ScheduleParallel(state.Dependency).Complete();

        // Phase 2: Build batches and schedule SIMD jobs (all in parallel)
        var batchHandles = new NativeList<JobHandle>(Allocator.Temp);

        foreach (var key in _stateGroups.GetKeyArray(Allocator.Temp))
        {
            var batch = BuildBatchForGroup(key);

            JobHandle handle = key.StateType switch
            {
                StateType.Single => new ProcessSingleClipBatchSIMDJob
                {
                    DeltaTime = dt,
                    ClipDuration = batch.ClipDuration,
                    Times = batch.Times,
                    Speeds = batch.Speeds,
                    Loops = batch.Loops,
                    NewTimes = batch.NewTimes,
                    NewPreviousTimes = batch.NewPreviousTimes
                }.Schedule(),

                StateType.LinearBlend => new ProcessLinearBlendBatchSIMDJob
                {
                    DeltaTime = dt,
                    ClipCount = batch.ClipCount,
                    Thresholds = batch.Thresholds,
                    BlendRatios = batch.BlendRatios,
                    AnimWeights = batch.AnimWeights,
                    SamplerTimes = batch.SamplerTimes,
                    NewSamplerTimes = batch.NewSamplerTimes,
                    NewSamplerPreviousTimes = batch.NewSamplerPreviousTimes,
                    NewSamplerWeights = batch.NewSamplerWeights
                }.Schedule(),

                _ => default
            };

            batchHandles.Add(handle);
        }

        // All batches run in parallel!
        var allBatchesHandle = JobHandle.CombineDependencies(batchHandles.AsArray());

        // Phase 3: Write back results
        state.Dependency = new WriteBackBatchResultsJob { Results = _resultsMap }
            .ScheduleParallel(allBatchesHandle);
    }
}
```

---

#### Performance Analysis

**Scenario**: 5000 animated entities, 1 state machine blob

| State | Type | Entity Count | SIMD Iterations |
|-------|------|--------------|-----------------|
| Idle | SingleClip | 2000 | 500 (÷4) |
| Walk | LinearBlend | 1750 | 438 (÷4) |
| Run | LinearBlend | 1250 | 313 (÷4) |
| **Total** | | **5000** | **1251** |

**Comparison**:

| Metric | Per-Entity | State-Coherent | Improvement |
|--------|------------|----------------|-------------|
| Compute iterations | 5000 | 1251 | **4x fewer** |
| SIMD utilization | 0% | ~95% | **4x throughput/iter** |
| Cache coherence | Poor | Excellent | **~3x fewer misses** |
| Branch prediction | Poor | Perfect | **No mispredictions** |
| Blob reads | 5000 | ~3 | **1666x fewer** |
| **Overall** | Baseline | **3-5x faster** | Scales with entity count |

**When to Use**:
- Entity count > 500 (categorization overhead amortized)
- Multiple entities share same blob and state (typical in games with crowds/NPCs)
- CPU-bound animation updates

**When NOT to Use**:
- Very few entities (<100)
- Every entity in a unique state (no batching opportunity)
- Memory-constrained environments (~40 bytes overhead per entity)

---

#### Advanced: Pre-Baked Static Array Architecture

Since the state machine blob is **baked at build time**, we know the complete state space ahead of time. This enables a fully static, zero-allocation runtime architecture.

##### Key Insight

```
BAKE TIME (known):                    RUNTIME (dynamic):
─────────────────                     ─────────────────
• Number of states                    • Which entities are in which state
• State types (Single/Linear/2D)      • Entity count per state
• Clip counts, thresholds, speeds     • Actual parameter values
• All structural metadata
```

We can **pre-allocate fixed-size arrays** for all possible states, eliminating:
- Runtime hashmap lookups
- Dynamic memory allocation
- Capacity checks and resizing
- Container overhead

##### Pre-Baked Group Registry (Blob Extension)

```csharp
// Added to StateMachineBlob at bake time
public struct StateMachineBlob
{
    // Existing fields...
    internal BlobArray<AnimationStateBlob> States;
    internal BlobArray<SingleClipStateBlob> SingleClipStates;
    internal BlobArray<LinearBlendStateBlob> LinearBlendStates;

    // NEW: Pre-baked group metadata (one per state)
    internal BlobArray<StateGroupMetadata> GroupMetadata;

    // NEW: Flattened shared data for all blend states
    internal BlobArray<float> AllThresholds;  // All thresholds concatenated
    internal BlobArray<float> AllClipSpeeds;  // All clip speeds concatenated
}

public struct StateGroupMetadata
{
    public StateType Type;
    public byte ClipCount;              // For LinearBlend/Directional2D
    public ushort ThresholdsOffset;     // Index into AllThresholds
    public ushort SpeedsOffset;         // Index into AllClipSpeeds
    public float ClipDuration;          // For SingleClip (pre-fetched)
    public ushort ClipIndex;            // For SingleClip
}
```

##### Static Batch Buffers (Zero-Allocation Runtime)

```csharp
/// <summary>
/// Pre-allocated fixed-size buffers for state-coherent batch processing.
/// Allocated once per blob type at load time, reused every frame.
///
/// Memory Layout:
/// ┌─────────────────┬─────────────────┬─────────────────┐
/// │  State 0 slots  │  State 1 slots  │  State 2 slots  │
/// │  [0..1023]      │  [1024..2047]   │  [2048..3071]   │
/// └─────────────────┴─────────────────┴─────────────────┘
/// Direct index: State N, Entity M → Array[N * MaxPerState + M]
/// </summary>
public struct StaticBatchBuffers : IDisposable
{
    // Configuration (set at creation, never changes)
    public readonly int StateCount;           // From blob.States.Length
    public readonly int MaxEntitiesPerState;  // Configurable cap (e.g., 1024)
    public readonly int TotalCapacity;        // StateCount × MaxEntitiesPerState

    // Per-state counters (fixed size = StateCount)
    public NativeArray<int> EntityCounts;     // How many entities in each state this frame
    public NativeArray<int> WriteIndices;     // Atomic counter for parallel fill

    // SoA data arrays (fixed size = TotalCapacity)
    // Layout: [State0 entities...][State1 entities...][StateN entities...]
    public NativeArray<Entity> Entities;
    public NativeArray<float> Times;
    public NativeArray<float> PreviousTimes;
    public NativeArray<float> Speeds;
    public NativeArray<float> Weights;
    public NativeArray<float> BlendRatios;    // For blend states
    public NativeArray<byte> SamplerIds;
    public NativeArray<byte> Loops;           // 1 = loop, 0 = no loop

    // Output arrays (same layout, written by SIMD jobs)
    public NativeArray<float> NewTimes;
    public NativeArray<float> NewPreviousTimes;
    public NativeArray<float> NewWeights;

    /// <summary>
    /// Creates static buffers sized to match the blob's state count.
    /// Call once at blob load time, reuse for lifetime of blob.
    /// </summary>
    public static StaticBatchBuffers Create(
        BlobAssetReference<StateMachineBlob> blob,
        int maxEntitiesPerState = 1024)
    {
        int stateCount = blob.Value.States.Length;
        int totalCapacity = stateCount * maxEntitiesPerState;

        return new StaticBatchBuffers
        {
            StateCount = stateCount,
            MaxEntitiesPerState = maxEntitiesPerState,
            TotalCapacity = totalCapacity,

            // Small per-state arrays
            EntityCounts = new NativeArray<int>(stateCount, Allocator.Persistent),
            WriteIndices = new NativeArray<int>(stateCount, Allocator.Persistent),

            // Large data arrays (contiguous allocation)
            Entities = new NativeArray<Entity>(totalCapacity, Allocator.Persistent),
            Times = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            PreviousTimes = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            Speeds = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            Weights = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            BlendRatios = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            SamplerIds = new NativeArray<byte>(totalCapacity, Allocator.Persistent),
            Loops = new NativeArray<byte>(totalCapacity, Allocator.Persistent),

            NewTimes = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            NewPreviousTimes = new NativeArray<float>(totalCapacity, Allocator.Persistent),
            NewWeights = new NativeArray<float>(totalCapacity, Allocator.Persistent),
        };
    }

    /// <summary>
    /// Direct index calculation - no container overhead.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetSliceStart(int stateIndex) => stateIndex * MaxEntitiesPerState;

    /// <summary>
    /// Reset counters for new frame. Fast - just zeros small arrays.
    /// </summary>
    public void ResetForFrame()
    {
        UnsafeUtility.MemClear(EntityCounts.GetUnsafePtr(), StateCount * sizeof(int));
        UnsafeUtility.MemClear(WriteIndices.GetUnsafePtr(), StateCount * sizeof(int));
    }

    public void Dispose()
    {
        EntityCounts.Dispose();
        WriteIndices.Dispose();
        Entities.Dispose();
        Times.Dispose();
        PreviousTimes.Dispose();
        Speeds.Dispose();
        Weights.Dispose();
        BlendRatios.Dispose();
        SamplerIds.Dispose();
        Loops.Dispose();
        NewTimes.Dispose();
        NewPreviousTimes.Dispose();
        NewWeights.Dispose();
    }
}
```

##### Runtime Flow (Zero Allocation)

```csharp
[BurstCompile]
public partial struct StaticBatchAnimationSystem : ISystem
{
    // Persistent buffer storage - one per unique blob, allocated at load
    private NativeHashMap<int, StaticBatchBuffers> _blobBuffers;

    public void OnCreate(ref SystemState state)
    {
        _blobBuffers = new NativeHashMap<int, StaticBatchBuffers>(8, Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (var kvp in _blobBuffers)
            kvp.Value.Dispose();
        _blobBuffers.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        // ═══════════════════════════════════════════════════════════════
        // PHASE 0: Reset counters (fast MemClear, no allocation)
        // ═══════════════════════════════════════════════════════════════
        foreach (var kvp in _blobBuffers)
            kvp.Value.ResetForFrame();

        // ═══════════════════════════════════════════════════════════════
        // PHASE 1: Fill static arrays (parallel, atomic write index)
        // ═══════════════════════════════════════════════════════════════
        var fillHandle = new FillStaticArraysJob
        {
            BlobBuffers = _blobBuffers
        }.ScheduleParallel(state.Dependency);

        fillHandle.Complete();  // Need counts for job scheduling

        // ═══════════════════════════════════════════════════════════════
        // PHASE 2: Schedule SIMD jobs per state (all run in parallel)
        // ═══════════════════════════════════════════════════════════════
        var processHandles = new NativeList<JobHandle>(Allocator.Temp);

        foreach (var kvp in _blobBuffers)
        {
            int blobHash = kvp.Key;
            var buffers = kvp.Value;
            var blob = GetBlobForHash(blobHash);

            for (int stateIdx = 0; stateIdx < buffers.StateCount; stateIdx++)
            {
                int count = buffers.EntityCounts[stateIdx];
                if (count == 0) continue;

                int sliceStart = buffers.GetSliceStart(stateIdx);
                ref var metadata = ref blob.Value.GroupMetadata[stateIdx];

                JobHandle handle = metadata.Type switch
                {
                    StateType.Single => new ProcessSingleClipStaticJob
                    {
                        DeltaTime = dt,
                        ClipDuration = metadata.ClipDuration,
                        SliceStart = sliceStart,
                        Count = count,
                        Times = buffers.Times,
                        Speeds = buffers.Speeds,
                        Loops = buffers.Loops,
                        NewTimes = buffers.NewTimes,
                        NewPreviousTimes = buffers.NewPreviousTimes,
                    }.Schedule(),

                    StateType.LinearBlend => new ProcessLinearBlendStaticJob
                    {
                        DeltaTime = dt,
                        ClipCount = metadata.ClipCount,
                        SliceStart = sliceStart,
                        Count = count,
                        ThresholdsSlice = blob.Value.AllThresholds.ToNativeArray()
                            .GetSubArray(metadata.ThresholdsOffset, metadata.ClipCount),
                        BlendRatios = buffers.BlendRatios,
                        AnimWeights = buffers.Weights,
                        Times = buffers.Times,
                        NewTimes = buffers.NewTimes,
                        NewPreviousTimes = buffers.NewPreviousTimes,
                        NewWeights = buffers.NewWeights,
                    }.Schedule(),

                    _ => default
                };

                processHandles.Add(handle);
            }
        }

        var allProcessed = JobHandle.CombineDependencies(processHandles.AsArray());

        // ═══════════════════════════════════════════════════════════════
        // PHASE 3: Write back results to entity buffers
        // ═══════════════════════════════════════════════════════════════
        state.Dependency = new WriteBackStaticJob
        {
            BlobBuffers = _blobBuffers
        }.ScheduleParallel(allProcessed);
    }
}
```

##### Fill Job (Parallel, Atomic Index)

```csharp
[BurstCompile]
internal partial struct FillStaticArraysJob : IJobEntity
{
    [NativeDisableParallelForRestriction]
    public NativeHashMap<int, StaticBatchBuffers> BlobBuffers;

    internal void Execute(
        Entity entity,
        in AnimationStateMachine stateMachine,
        in DynamicBuffer<AnimationState> animationStates,
        in DynamicBuffer<ClipSampler> samplers,
        in DynamicBuffer<FloatParameter> floatParams)
    {
        int blobHash = stateMachine.StateMachineBlob.GetHashCode();
        int stateIndex = stateMachine.CurrentState.StateIndex;

        var buffers = BlobBuffers[blobHash];

        // Atomic increment to get write slot - O(1), lock-free
        unsafe
        {
            int* countPtr = (int*)buffers.WriteIndices.GetUnsafePtr() + stateIndex;
            int localIndex = Interlocked.Increment(ref *countPtr) - 1;

            // Update entity count
            int* entityCountPtr = (int*)buffers.EntityCounts.GetUnsafePtr() + stateIndex;
            Interlocked.Increment(ref *entityCountPtr);

            // Bounds safety
            if (localIndex >= buffers.MaxEntitiesPerState) return;

            // Direct index into static array
            int writeIndex = buffers.GetSliceStart(stateIndex) + localIndex;

            // Extract entity data
            if (!animationStates.TryGetWithId(
                stateMachine.CurrentState.AnimationStateId, out var animState)) return;

            int samplerIndex = samplers.IdToIndex(animState.StartSamplerId);
            var sampler = samplers[samplerIndex];

            // Direct write to static arrays - no bounds check, no allocation
            buffers.Entities[writeIndex] = entity;
            buffers.Times[writeIndex] = sampler.Time;
            buffers.PreviousTimes[writeIndex] = sampler.PreviousTime;
            buffers.Speeds[writeIndex] = animState.Speed;
            buffers.Weights[writeIndex] = animState.Weight;
            buffers.SamplerIds[writeIndex] = sampler.Id;
            buffers.Loops[writeIndex] = animState.Loop ? (byte)1 : (byte)0;

            // Blend ratio for blend states
            ref var metadata = ref stateMachine.StateMachineBlob.Value.GroupMetadata[stateIndex];
            if (metadata.Type == StateType.LinearBlend)
            {
                ref var linearBlend = ref stateMachine.StateMachineBlob.Value
                    .LinearBlendStates[metadata.StateTypeIndex];
                float blendRatio = floatParams[linearBlend.BlendParameterIndex].Value;
                buffers.BlendRatios[writeIndex] = blendRatio;
            }
        }
    }
}
```

##### SIMD Processing Job (Static Arrays)

```csharp
[BurstCompile]
internal struct ProcessSingleClipStaticJob : IJob
{
    public float DeltaTime;
    public float ClipDuration;
    public int SliceStart;
    public int Count;

    [ReadOnly] public NativeArray<float> Times;
    [ReadOnly] public NativeArray<float> Speeds;
    [ReadOnly] public NativeArray<byte> Loops;

    [WriteOnly] [NativeDisableParallelForRestriction]
    public NativeArray<float> NewTimes;
    [WriteOnly] [NativeDisableParallelForRestriction]
    public NativeArray<float> NewPreviousTimes;

    public void Execute()
    {
        int end = SliceStart + Count;
        int simdEnd = SliceStart + (Count & ~3);  // Round down to multiple of 4

        float4 dt4 = DeltaTime;
        float4 duration4 = ClipDuration;

        // ═══════════════════════════════════════════════════════════════
        // SIMD LOOP: Process 4 entities per iteration
        // Memory access is contiguous → optimal cache utilization
        // ═══════════════════════════════════════════════════════════════
        for (int i = SliceStart; i < simdEnd; i += 4)
        {
            // Load 4 contiguous values (single cache line)
            float4 times = new float4(Times[i], Times[i+1], Times[i+2], Times[i+3]);
            float4 speeds = new float4(Speeds[i], Speeds[i+1], Speeds[i+2], Speeds[i+3]);
            float4 loops = new float4(Loops[i], Loops[i+1], Loops[i+2], Loops[i+3]);

            // Store previous times
            NewPreviousTimes[i] = times.x;
            NewPreviousTimes[i+1] = times.y;
            NewPreviousTimes[i+2] = times.z;
            NewPreviousTimes[i+3] = times.w;

            // SIMD compute: 4 entities in single operation
            float4 newTimes = times + dt4 * speeds;

            // Branchless loop handling
            float4 looped = math.fmod(newTimes, duration4);
            looped = math.select(looped, looped + duration4, looped < 0);
            newTimes = math.select(newTimes, looped, loops > 0.5f);

            // Store results (contiguous write)
            NewTimes[i] = newTimes.x;
            NewTimes[i+1] = newTimes.y;
            NewTimes[i+2] = newTimes.z;
            NewTimes[i+3] = newTimes.w;
        }

        // Scalar remainder (0-3 entities)
        for (int i = simdEnd; i < end; i++)
        {
            NewPreviousTimes[i] = Times[i];
            float newTime = Times[i] + DeltaTime * Speeds[i];
            if (Loops[i] > 0)
            {
                newTime = math.fmod(newTime, ClipDuration);
                if (newTime < 0) newTime += ClipDuration;
            }
            NewTimes[i] = newTime;
        }
    }
}
```

##### Write-Back Job

```csharp
[BurstCompile]
internal partial struct WriteBackStaticJob : IJobEntity
{
    [ReadOnly] public NativeHashMap<int, StaticBatchBuffers> BlobBuffers;

    internal void Execute(
        Entity entity,
        in AnimationStateMachine stateMachine,
        ref DynamicBuffer<ClipSampler> samplers,
        in DynamicBuffer<AnimationState> animationStates)
    {
        int blobHash = stateMachine.StateMachineBlob.GetHashCode();
        int stateIndex = stateMachine.CurrentState.StateIndex;

        var buffers = BlobBuffers[blobHash];
        int sliceStart = buffers.GetSliceStart(stateIndex);
        int count = buffers.EntityCounts[stateIndex];

        // Find this entity in the batch
        for (int i = 0; i < count; i++)
        {
            int idx = sliceStart + i;
            if (buffers.Entities[idx] == entity)
            {
                // Apply results to sampler
                byte samplerId = buffers.SamplerIds[idx];
                int samplerIndex = samplers.IdToIndex(samplerId);

                var sampler = samplers[samplerIndex];
                sampler.Time = buffers.NewTimes[idx];
                sampler.PreviousTime = buffers.NewPreviousTimes[idx];
                sampler.Weight = buffers.NewWeights[idx];
                samplers[samplerIndex] = sampler;

                return;
            }
        }
    }
}
```

##### Memory Layout Diagram

```
StaticBatchBuffers Memory Layout (10 states, 1024 max entities/state):

                    ┌─ State 0 ─┐┌─ State 1 ─┐┌─ State 2 ─┐     ┌─ State 9 ─┐
                    │           ││           ││           │     │           │
EntityCounts[10]:   [    500    ][    300    ][    200    ].....[    150    ]
                         │            │            │                  │
                         ▼            ▼            ▼                  ▼
                    ┌────────────────────────────────────────────────────────┐
Times[10240]:       │ S0: 500 used │ S1: 300 used │ S2: 200 used │...│ S9    │
                    │ [0..499]     │ [1024..1323] │ [2048..2247] │   │       │
                    └────────────────────────────────────────────────────────┘
                    ▲              ▲              ▲
                    │              │              │
Index calculation:  State N, Local M  →  Array[N × 1024 + M]
                    State 0, Entity 50 →  Times[0 × 1024 + 50] = Times[50]
                    State 1, Entity 50 →  Times[1 × 1024 + 50] = Times[1074]
                    State 2, Entity 50 →  Times[2 × 1024 + 50] = Times[2098]

Benefits:
  ✓ No pointer chasing (direct index math)
  ✓ Contiguous memory (cache-friendly)
  ✓ Fixed size (no reallocation)
  ✓ SIMD-aligned (NativeArray guarantees)
```

##### Memory Budget

```
Per StateMachineBlob (10 states, 1024 max entities/state = 10,240 slots):

Fixed Arrays:
┌─────────────────────┬───────────────┬────────────┐
│ Array               │ Size/Element  │ Total      │
├─────────────────────┼───────────────┼────────────┤
│ EntityCounts[10]    │ 4 bytes       │ 40 B       │
│ WriteIndices[10]    │ 4 bytes       │ 40 B       │
├─────────────────────┼───────────────┼────────────┤
│ Entities[10240]     │ 8 bytes       │ 80 KB      │
│ Times[10240]        │ 4 bytes       │ 40 KB      │
│ PreviousTimes[10240]│ 4 bytes       │ 40 KB      │
│ Speeds[10240]       │ 4 bytes       │ 40 KB      │
│ Weights[10240]      │ 4 bytes       │ 40 KB      │
│ BlendRatios[10240]  │ 4 bytes       │ 40 KB      │
│ SamplerIds[10240]   │ 1 byte        │ 10 KB      │
│ Loops[10240]        │ 1 byte        │ 10 KB      │
├─────────────────────┼───────────────┼────────────┤
│ NewTimes[10240]     │ 4 bytes       │ 40 KB      │
│ NewPreviousTimes    │ 4 bytes       │ 40 KB      │
│ NewWeights[10240]   │ 4 bytes       │ 40 KB      │
├─────────────────────┼───────────────┼────────────┤
│ TOTAL               │               │ ~420 KB    │
└─────────────────────┴───────────────┴────────────┘

Scaling:
  5,000 entities  → 420 KB (fixed)
  50,000 entities → 420 KB (increase MaxEntitiesPerState to 5000 → 4.2 MB)

Comparison to Dynamic:
  Dynamic NativeList: ~40 bytes overhead per element + resize allocations
  Static NativeArray: 0 bytes overhead, 0 allocations after init
```

##### Performance Comparison

| Aspect | Dynamic (NativeList) | Static (NativeArray) | Improvement |
|--------|---------------------|----------------------|-------------|
| Frame allocation | O(N) possible | **Zero** | ∞ |
| Bounds checking | Every access | **None** (controlled indices) | ~5% |
| Container overhead | 24 bytes/list | **0 bytes** | 100% |
| Cache locality | Good | **Optimal** (contiguous) | ~10-20% |
| Index calculation | Pointer + offset | **Direct arithmetic** | ~2% |
| Memory layout | Heap fragmented | **Flat, predictable** | Better prefetch |
| SIMD alignment | Maybe | **Guaranteed** | Consistent |

##### Baking the Group Registry

```csharp
// In StateMachineBlobConverter.BuildBlob()
private void BakeGroupRegistry(ref BlobBuilder builder, ref StateMachineBlob blob)
{
    int stateCount = blob.States.Length;
    var metadataBuilder = builder.Allocate(ref blob.GroupMetadata, stateCount);

    // Accumulate offsets for flattened threshold/speed arrays
    int thresholdOffset = 0;
    int speedOffset = 0;
    var allThresholds = new List<float>();
    var allSpeeds = new List<float>();

    for (int i = 0; i < stateCount; i++)
    {
        ref var state = ref blob.States[i];
        var metadata = new StateGroupMetadata { Type = state.StateType };

        switch (state.StateType)
        {
            case StateType.Single:
                ref var single = ref blob.SingleClipStates[state.StateTypeIndex];
                metadata.ClipDuration = single.ClipDuration;
                metadata.ClipIndex = single.ClipIndex;
                metadata.ClipCount = 1;
                break;

            case StateType.LinearBlend:
                ref var linear = ref blob.LinearBlendStates[state.StateTypeIndex];
                metadata.ClipCount = (byte)linear.SortedClipThresholds.Length;
                metadata.ThresholdsOffset = (ushort)thresholdOffset;
                metadata.SpeedsOffset = (ushort)speedOffset;

                // Flatten thresholds and speeds into blob arrays
                for (int j = 0; j < metadata.ClipCount; j++)
                {
                    allThresholds.Add(linear.SortedClipThresholds[j]);
                    allSpeeds.Add(linear.SortedClipSpeeds[j]);
                }

                thresholdOffset += metadata.ClipCount;
                speedOffset += metadata.ClipCount;
                break;

            case StateType.Directional2D:
                // Similar handling for 2D blend states
                break;
        }

        metadataBuilder[i] = metadata;
    }

    // Allocate flattened arrays
    var thresholdsBuilder = builder.Allocate(ref blob.AllThresholds, allThresholds.Count);
    var speedsBuilder = builder.Allocate(ref blob.AllClipSpeeds, allSpeeds.Count);

    for (int i = 0; i < allThresholds.Count; i++)
        thresholdsBuilder[i] = allThresholds[i];
    for (int i = 0; i < allSpeeds.Count; i++)
        speedsBuilder[i] = allSpeeds[i];
}
```

##### Implementation Priority Update

This static array architecture should be implemented as part of **Phase 4: State-Coherent Batch Processing**:

1. First implement dynamic version (validates correctness)
2. Profile to confirm batch processing gains
3. Convert to static arrays (eliminates allocation overhead)
4. Profile again to measure additional gains

**Expected Additional Gain from Static Arrays**: 10-20% over dynamic version (primarily from eliminated allocations and better cache behavior)

---

### 3. Batched Animation Event Processing

**Current**: Per-entity event checking in `RaiseAnimationEventsJob`

**Optimization**: Collect events in parallel, process in batch:

```csharp
[BurstCompile]
internal partial struct CollectAnimationEventsJob : IJobEntity
{
    public NativeQueue<RaisedAnimationEvent>.ParallelWriter EventQueue;

    internal void Execute(Entity entity, in DynamicBuffer<ClipSampler> samplers)
    {
        // Write to parallel queue instead of per-entity buffer
        // Reduces buffer resizing overhead
    }
}

[BurstCompile]
internal struct ProcessCollectedEventsJob : IJob
{
    public NativeQueue<RaisedAnimationEvent> EventQueue;
    // Process all events in single job
}
```

### 3. Clip Sampling Pre-calculation

**Current**: Each sampling job independently calculates sampler validity

**Optimization**: Pre-calculate active samplers in separate job:

```csharp
[BurstCompile]
internal partial struct PrepareActiveSamplersJob : IJobEntity
{
    [NativeDisableParallelForRestriction]
    public NativeArray<ActiveSamplerInfo> ActiveSamplers;
    public NativeCounter.Concurrent ActiveCount;

    internal void Execute(Entity entity, in DynamicBuffer<ClipSampler> samplers)
    {
        for (int i = 0; i < samplers.Length; i++)
        {
            if (!mathex.iszero(samplers[i].Weight) && samplers[i].Clips.IsCreated)
            {
                int index = ActiveCount.Increment();
                ActiveSamplers[index] = new ActiveSamplerInfo { Entity = entity, SamplerIndex = i };
            }
        }
    }
}

// Then sample only active samplers with IJobParallelFor
```

### 4. Background Blob Building (Baking Phase)

**Location**: `StateMachineBlobConverter.BuildBlob()` and `ClipEventsBlobConverter`

**Current**: Synchronous blob building during baking

> **⚠️ Implementation Challenge**: `StateMachineBlobConverter` contains managed references and non-blittable data that cannot be used directly in Burst-compiled jobs. Converting to async blob building requires extracting blittable data first.

**Optimization Approach** (if profiling shows baking is slow):

```csharp
// Step 1: Extract blittable data on main thread
public struct StateMachineBlobBuildData
{
    // Only blittable fields here - populated from StateMachineBlobConverter
    public NativeArray<StateData> States;
    public NativeArray<TransitionData> Transitions;
    public NativeArray<int> ParameterHashes;
    // ... other blittable data needed for blob building
}

// Step 2: Build blob in job using only blittable data
[BurstCompile]
internal struct BuildStateMachineBlobJob : IJob
{
    [ReadOnly] public StateMachineBlobBuildData BuildData;
    public NativeReference<BlobAssetReference<StateMachineBlob>> Result;

    public void Execute()
    {
        // Build blob using only blittable BuildData
        var builder = new BlobBuilder(Allocator.Temp);
        // ... populate blob from BuildData
        Result.Value = builder.CreateBlobAssetReference<StateMachineBlob>(Allocator.Persistent);
    }
}

// Step 3: Schedule from converter
public BlobAssetReference<StateMachineBlob> BuildBlobAsync()
{
    var buildData = ExtractBlittableData(); // Main thread extraction
    var result = new NativeReference<BlobAssetReference<StateMachineBlob>>(Allocator.TempJob);

    var job = new BuildStateMachineBlobJob
    {
        BuildData = buildData,
        Result = result
    }.Schedule();

    job.Complete(); // Or store handle for later completion
    return result.Value;
}
```

**Trade-off**: Significant refactoring required. Only worth pursuing if profiling shows baking is a bottleneck.

---

## Data Compaction

### 1. Parameter Struct Packing

**Current**:
```csharp
public struct BoolParameter : IBufferElementData  // 8 bytes (4 hash + 1 bool + 3 padding)
{
    public int Hash;    // 4 bytes
    public bool Value;  // 1 byte + 3 padding
}
```

**Optimized**:
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BoolParameter : IBufferElementData  // 5 bytes with explicit packing
{
    public int Hash;
    private byte _value;  // Store as byte internally

    public bool Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _value = value ? (byte)1 : (byte)0;
    }
}

// Or pack 32 bools into one int with bitfield
public struct PackedBoolParameters : IComponentData
{
    public uint Flags;  // 32 bools in 4 bytes

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Get(int index) => (Flags & (1u << index)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index, bool value)
    {
        if (value) Flags |= (1u << index);
        else Flags &= ~(1u << index);
    }
}
```

### 2. Animation State Compaction

**Current**:
```csharp
public struct AnimationState : IBufferElementData  // ~16 bytes
{
    public byte Id;           // 1 byte
    internal float Time;      // 4 bytes
    internal float Weight;    // 4 bytes
    internal float Speed;     // 4 bytes
    internal bool Loop;       // 1 byte
    internal byte StartSamplerId;  // 1 byte
    internal byte ClipCount;  // 1 byte
}
```

**Optimized** (16 bytes aligned):
```csharp
public struct AnimationState : IBufferElementData
{
    internal float Time;      // 4 bytes [0-3]
    internal float Weight;    // 4 bytes [4-7]
    internal float Speed;     // 4 bytes [8-11]
    public byte Id;           // 1 byte [12]
    internal byte StartSamplerId;  // 1 byte [13]
    internal byte ClipCount;  // 1 byte [14]
    internal byte Flags;      // 1 byte [15] - contains Loop bit

    internal bool Loop
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & 1) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Flags = (byte)(value ? (Flags | 1) : (Flags & ~1));
    }
}
```

### 3. ClipSampler Optimization

**Current**:
```csharp
public struct ClipSampler : IBufferElementData  // ~48+ bytes
{
    public byte Id;
    internal BlobAssetReference<SkeletonClipSetBlob> Clips;     // 8 bytes
    internal BlobAssetReference<ClipEventsBlob> ClipEventsBlob; // 8 bytes
    internal ushort ClipIndex;
    internal float PreviousTime;
    internal float Time;
    internal float Weight;
}
```

**Optimization Strategy**: Store blob references at entity level, not per-sampler:

```csharp
// Entity-level component (single instance per entity)
public struct ClipBlobReferences : IComponentData
{
    internal BlobAssetReference<SkeletonClipSetBlob> Clips;
    internal BlobAssetReference<ClipEventsBlob> ClipEventsBlob;
}

// Reduced per-sampler data (~16 bytes vs ~48)
public struct ClipSampler : IBufferElementData
{
    public byte Id;           // 1
    internal ushort ClipIndex; // 2
    internal byte _pad;       // 1
    internal float PreviousTime; // 4
    internal float Time;      // 4
    internal float Weight;    // 4
}
```

### 4. Transition Condition Bitfield

**Current**: Array of conditions evaluated individually

**Optimization**: Pack simple conditions into bitfield for vectorized evaluation:

```csharp
internal struct PackedTransitionConditions
{
    // For bool conditions: bits represent (paramIndex << 1) | expectedValue
    public ulong BoolConditionMask;    // Up to 32 bool conditions
    public ulong BoolConditionExpected;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EvaluateBools(uint boolParamBits)
    {
        // Single bitwise operation evaluates all bool conditions
        return ((boolParamBits ^ BoolConditionExpected) & BoolConditionMask) == 0;
    }
}
```

### 5. State Index Size Reduction

**Current**: Various sizes (short, ushort, int) for state indices

**Optimization**: Standardize on smallest sufficient type:

```csharp
// If state machines have < 256 states (typical)
internal struct StateMachineStateRef
{
    internal byte StateIndex;      // Was ushort
    internal sbyte AnimationStateId;
    // 2 bytes vs 4 bytes
}
```

### 6. Sampler ID Allocation Optimization

**Current**: Complex ID allocation with fragmentation handling

**Optimization**: Use generation-based IDs:

```csharp
internal struct SamplerHandle
{
    public ushort Index;      // Position in buffer
    public ushort Generation; // For validity checking
}

// Enables O(1) lookup without search
```

---

## Memory Layout Optimization

> **⚠️ General Warning**: The optimizations in this section involve significant architectural changes with high implementation complexity. Only pursue these after profiling confirms the current layout is a bottleneck, and carefully weigh the maintenance burden against performance gains.

### 1. Hot/Cold Data Splitting

Split frequently accessed data from rarely accessed:

```csharp
// HOT: Every frame
public struct AnimationStateHot : IBufferElementData
{
    internal float Time;
    internal float Weight;
    internal byte StartSamplerId;
    internal byte ClipCount;
}

// COLD: Only during transitions
public struct AnimationStateCold : IBufferElementData
{
    internal float Speed;
    internal bool Loop;
    public byte Id;
}
```

> **⚠️ Implementation Complexity**: Hot/Cold splitting introduces significant complexity:
> 1. **Index synchronization** - Must keep indices aligned between two separate buffers
> 2. **API surface changes** - All code accessing `AnimationState` must access two buffers instead of one
> 3. **Creation/deletion handling** - Adding or removing states requires coordinated updates to both buffers
> 4. **Bug surface area** - Index mismatches between hot/cold buffers cause subtle, hard-to-diagnose bugs
>
> **Recommendation**: Only implement if profiling shows cache misses on `AnimationState` are a significant bottleneck. The current unified struct is simpler and already reasonably cache-friendly at 16 bytes.

### 2. SoA vs AoS for Samplers

**Current**: Array of Structures (AoS)
```csharp
ClipSampler[] // Each element has Time, Weight, etc.
```

**Alternative**: Structure of Arrays (SoA) for better vectorization:
```csharp
public struct ClipSamplersSoA : IComponentData
{
    public NativeArray<float> Times;
    public NativeArray<float> PreviousTimes;
    public NativeArray<float> Weights;
    public NativeArray<ushort> ClipIndices;
}
```

> **⚠️ High-Risk Change**: Converting from AoS to SoA would:
> 1. **Break the existing `DynamicBuffer<ClipSampler>` API** used throughout the codebase
> 2. **Require updating all sampler access code** to work with separate arrays
> 3. **Change iteration patterns** - cannot iterate "per sampler" without zipping arrays
> 4. **Complicate ID-based lookups** - the current ID mechanism relies on buffer element access
> 5. **Require custom resize logic** - must keep all arrays in sync when adding/removing samplers
>
> **Trade-off**: Better SIMD vectorization potential, but **substantial refactoring cost** touching most of the animation system.
>
> **Recommendation**: This is a high-effort, high-risk optimization. Only consider if profiling shows sampler iteration is a dominant cost AND the typical sampler count is large enough (16+) to benefit from SIMD. For typical counts (2-8), AoS with good cache locality is often faster.

---

## Implementation Priority

### Phase 1: Quick Wins (1-2 days)

> **Note**: All jobs already have `[BurstCompile]` attributes. Static classes cannot have this attribute.

1. **Verify Burst configuration**
   - Ensure Burst is enabled for all relevant build targets
   - Enable safety checks only where needed to balance performance and debugging

2. **Add AggressiveInlining to hot methods**
   - All `Evaluate` methods in transitions
   - All `Update*` methods in state utils
   - Math-heavy helpers in utility classes (already called from Burst, but inlining helps)

3. **Reduce per-frame allocations in jobs**
   - Audit for `Allocator.Temp` usage in hot paths
   - Reuse native containers where possible
   - Move temporary allocations out of tight update loops

### Phase 2: Job Scheduling Audit (1-2 days)

> **Note**: The state type update jobs (SingleClip, LinearBlend, Directional2D) cannot be parallelized because they all write to `DynamicBuffer<ClipSampler>`. This is by design.

1. **Document job scheduling rationale** - Ensure comments explain why sequential scheduling is required
2. **Validate job safety guarantees** - Confirm all writes to shared buffers respect Unity's safety system
3. **Identify safe parallelism opportunities** - Look for jobs or stages that don't write to shared buffers:
   - Read-only query jobs
   - Pre-processing or post-processing stages
   - Event collection (write to separate queue, not shared buffer)

### Phase 3: Parallel State Processing (3-5 days) ⭐ HIGH IMPACT

> **Key Insight**: State machine blob data is read-only. Only `ClipSampler` fields are written. This enables parallel execution.

1. **Implement Queue-Based Parallel Processing (Option A)**
   - Create `SamplerUpdateCommand` struct
   - Convert update jobs to write commands to `NativeStream`
   - Implement `ApplySamplerUpdatesJob` to merge commands
   - Profile overhead vs parallel gains

2. **Alternative: Per-State-Type Buffers (Option B)**
   - If queue overhead is high, split into `SingleClipSamplerData`, `LinearBlendSamplerData`, etc.
   - Each job type writes to its own buffer
   - Sampling phase reads from all buffers

3. **Validation**
   - Verify no race conditions with stress tests
   - Profile with varying entity counts (100, 1000, 10000)
   - Compare sequential vs parallel throughput

**Expected Gain**: 1.5-2.5x throughput on multi-core systems

### Phase 4: State-Coherent Batch Processing (5-7 days) ⭐⭐ HIGHEST IMPACT

> **Key Insight**: One blob serves N entities. Group entities by (BlobHash, StateIndex, StateType) and batch-process with SIMD.

1. **Implement Entity Categorization**
   - Create `StateGroupKey` struct (BlobHash + StateIndex + StateType)
   - Implement `CategorizeEntitiesJob` to populate `NativeParallelMultiHashMap`
   - Dynamic threshold: skip batching if entity count < 200

2. **Implement SoA Batch Data Structures**
   - `SingleClipBatchData` with `NativeList<float>` for Times, Speeds, Weights
   - `LinearBlendBatchData` with flattened sampler arrays
   - Shared blob data read once per batch

3. **Implement SIMD Batch Processing Jobs**
   - `ProcessSingleClipBatchSIMDJob`: Process 4 entities per iteration with float4
   - `ProcessLinearBlendBatchSIMDJob`: Vectorized threshold search + weight calculation
   - Branchless loop handling with `math.select`

4. **Implement Write-Back**
   - `WriteBackBatchResultsJob`: Apply computed results to entity buffers
   - Consider combining with Phase 3's queue approach

**Expected Gain**: 3-5x throughput on 5000+ entities (4x SIMD + cache coherence + no branching)

### Phase 5: Data Compaction (3-5 days)

1. **Implement packed bool parameters** with bitfield
2. **Optimize AnimationState layout** with flags byte
3. **Extract blob references** to entity-level component
4. **Reduce ClipSampler size** from ~48 to ~16 bytes

### Phase 6: Additional Vectorization (3-5 days)

1. **Vectorize weight zeroing loops** with UnsafeUtility.MemClear
2. **Vectorize weight normalization** with float4 operations
3. **Batch angle calculations** with SIMD atan2
4. **Implement early-exit** in transition evaluation

### Phase 7: Advanced Optimizations (Optional)

1. **Per-layer sampler buffers** (if adding animation layer support)
2. **SoA layout for samplers** (if profiling shows benefit)
3. **Generation-based sampler handles**
4. **Packed transition conditions**
5. **Background blob building**

---

## Testing Requirements

1. **Performance Benchmarks**
   - Create profiling tests for each optimization
   - Measure before/after for:
     - Job completion time
     - Memory bandwidth
     - Cache misses

2. **Regression Tests**
   - All existing integration tests must pass
   - Animation playback visual verification
   - Event firing accuracy
   - Transition timing precision

3. **Stress Tests**
   - 1000+ entities with state machines
   - Deep blend trees (10+ clips)
   - Rapid state transitions
   - High parameter update frequency

---

## Dependencies

The following are **minimum supported versions** for optimizations in this plan. The plan is expected to work with any newer minor/patch release within the same major line. When upgrading to a new major version, re-validate the assumptions in this document.

| Package | Minimum Version | Notes |
|---------|-----------------|-------|
| Unity Entities | 1.0+ | Assumes 1.x Entities API surface |
| Unity Burst | 1.8+ | Assumes 1.x Burst compiler features |
| Unity Collections | 2.0+ | Assumes 2.x Collections API (NativeArray, etc.) |
| Latios Framework | (compatible) | Kinemation integration; version must match above packages |

**Note**: Some optimizations (e.g., specific Burst intrinsics) may require newer versions. Check Burst release notes if specific SIMD features don't compile.

---

## Notes

- Most authoring classes **cannot** be converted to structs due to Unity's ScriptableObject requirement
- The runtime is already highly optimized - these are incremental improvements
- Focus optimization effort on systems with highest entity counts
- Profile before and after each change to verify improvement

---

*Last Updated: 2026-02-02*
*Branch: claude/add-dmotion-optimization-plan-YL52I*
*Revision 5: Added performance extrapolation - projects 100k-200k entities @ 60fps (vs current ~15k-20k), ~35x faster than Mecanim*
