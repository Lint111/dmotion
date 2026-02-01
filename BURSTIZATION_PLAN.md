# DMotion Burstization & Performance Optimization Plan

This document provides a comprehensive plan for improving Burst compatibility, vectorization, async job optimization, and data layout in the DMotion animation system.

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

DMotion is already well-optimized for DOTS with extensive `[BurstCompile]` usage across 30+ files. This plan identifies remaining optimization opportunities:

- **8 authoring classes** that could be converted to structs (editor-time only, low priority)
- **3 jobs** missing `[BurstCompile]` attribute
- **12+ vectorization opportunities** in loop-heavy operations
- **4 async job patterns** for workload distribution
- **6 data compaction opportunities** for better cache utilization

---

## Current State Analysis

### Burst Compatibility Status

| Category | Files with [BurstCompile] | Total Files | Coverage |
|----------|---------------------------|-------------|----------|
| Systems | 11 | 11 | 100% |
| Jobs (IJobEntity) | 17 | 20 | 85% |
| Utility Classes | 5 | 8 | 62% |
| Components | 6 | 16 | 37% |
| Total Runtime | 30 | ~86 | 35% |

**Note**: Many files don't require `[BurstCompile]` (interfaces, enums, authoring ScriptableObjects).

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

## Missing Burst Attributes

### Jobs Without [BurstCompile]

| Job | File | Line | Priority |
|-----|------|------|----------|
| `SampleNonOptimizedBones` | `SampleNonOptimizedBones.cs:18` | Missing attribute | HIGH |
| `SampleRootDeltasJob` | `SampleRootDeltasJob.cs:12` | Missing attribute | HIGH |
| `TransferRootMotionJob` | `TransferRootMotionJob.cs:17` | Missing attribute | HIGH |

**Fix**:
```csharp
// SampleNonOptimizedBones.cs - Add attribute
[BurstCompile]  // ADD THIS
[WithNone(typeof(SkeletonRootTag))]
internal partial struct SampleNonOptimizedBones : IJobEntity

// SampleRootDeltasJob.cs - Add attribute
[BurstCompile]  // ADD THIS
[WithAll(typeof(SkeletonRootTag))]
internal partial struct SampleRootDeltasJob : IJobEntity

// TransferRootMotionJob.cs - Add attribute
[BurstCompile]  // ADD THIS
[WithAll(typeof(TransferRootMotionToOwner))]
internal partial struct TransferRootMotionJob : IJobEntity
```

### Utility Methods Missing [BurstCompile]

| Method/Class | File | Action |
|--------------|------|--------|
| `Directional2DBlendUtils` | `Directional2DBlendUtils.cs` | Add `[BurstCompile]` to class |
| `LinearBlendStateUtils` | `LinearBlendStateUtils.cs` | Add `[BurstCompile]` to class |
| `SingleClipStateUtils` | `SingleClipStateUtils.cs` | Add `[BurstCompile]` to class |
| `Directional2DBlendStateUtils` | `Directional2DBlendStateUtils.cs` | Add `[BurstCompile]` to class |
| `CollectionUtils` | `CollectionUtils.cs` | Add `[BurstCompile]` to class |

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
    // ClipSampler.Weight offset from struct start
    const int weightOffset = /* calculate offset */;
    const int samplerStride = /* sizeof(ClipSampler) */;

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
```csharp
[BurstCompile]
internal static void NormalizeWeights(ref DynamicBuffer<ClipSampler> clipSamplers)
{
    int length = clipSamplers.Length;
    if (length == 0) return;

    // Process 4 elements at a time using float4
    float4 sum4 = float4.zero;
    int i = 0;

    // Vectorized sum (requires extracting weights to temp array first)
    // Or use math.csum for final reduction

    var sumWeights = 0.0f;
    for (i = 0; i < length; i++)
    {
        sumWeights += clipSamplers[i].Weight;
    }

    if (!mathex.approximately(sumWeights, 1f))
    {
        float invSum = 1.0f / sumWeights;
        for (i = 0; i < length; i++)
        {
            var sampler = clipSamplers[i];
            sampler.Weight *= invSum;
            clipSamplers[i] = sampler;
        }
    }
}
```

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
for (int i = 0; i < positions.Length; i += 4)
{
    // Process 4 positions at once
    float4 x = new float4(positions[i].x, positions[i+1].x, positions[i+2].x, positions[i+3].x);
    float4 y = new float4(positions[i].y, positions[i+1].y, positions[i+2].y, positions[i+3].y);
    float4 result = math.atan2(y, x);
    // Store results
}
```

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

### 1. Parallel State Type Processing

**Current**: Sequential job scheduling in `UpdateAnimationStatesSystem.cs`
```csharp
handle = new UpdateSingleClipStatesJob{}.ScheduleParallel(handle);
handle = new UpdateLinearBlendStateMachineStatesJob{}.ScheduleParallel(handle);
handle = new UpdateDirectional2DBlendStateMachineStatesJob{}.ScheduleParallel(handle);
```

**Optimization**: If entities have distinct archetypes (SingleClip vs LinearBlend vs Directional2D), these can run in parallel:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var dt = SystemAPI.Time.DeltaTime;

    // All update jobs can run in parallel (different entity archetypes)
    var singleHandle = new UpdateSingleClipStatesJob { DeltaTime = dt }
        .ScheduleParallel(state.Dependency);
    var linearHandle = new UpdateLinearBlendStateMachineStatesJob { DeltaTime = dt }
        .ScheduleParallel(state.Dependency);
    var dir2DHandle = new UpdateDirectional2DBlendStateMachineStatesJob { DeltaTime = dt }
        .ScheduleParallel(state.Dependency);

    var allUpdates = JobHandle.CombineDependencies(singleHandle, linearHandle, dir2DHandle);

    // Clean jobs must wait for all updates
    var cleanSingle = new CleanSingleClipStatesJob().ScheduleParallel(allUpdates);
    var cleanLinear = new CleanLinearBlendStatesJob().ScheduleParallel(allUpdates);
    var cleanDir2D = new CleanDirectional2DBlendStatesJob().ScheduleParallel(allUpdates);

    state.Dependency = JobHandle.CombineDependencies(cleanSingle, cleanLinear, cleanDir2D);
}
```

**Prerequisite**: Verify entities with SingleClipState don't also have LinearBlendState (mutually exclusive archetypes).

### 2. Batched Animation Event Processing

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

**Optimization**: Schedule blob building as jobs:

```csharp
[BurstCompile]
internal struct BuildStateMachineBlobJob : IJob
{
    public StateMachineBlobConverter Converter;
    public NativeReference<BlobAssetReference<StateMachineBlob>> Result;

    public void Execute()
    {
        Result.Value = Converter.BuildBlob();
    }
}
```

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
public struct BoolParameter : IBufferElementData  // 5 bytes, pack multiple
{
    public int Hash;
    public byte Value;  // Use byte instead of bool
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

**Trade-off**: Better vectorization vs. more complex buffer management.

---

## Implementation Priority

### Phase 1: Quick Wins (1-2 days)

1. **Add missing [BurstCompile] attributes** (3 jobs)
   - `SampleNonOptimizedBones`
   - `SampleRootDeltasJob`
   - `TransferRootMotionJob`

2. **Add [BurstCompile] to utility classes**
   - `Directional2DBlendUtils`
   - `LinearBlendStateUtils`
   - `SingleClipStateUtils`
   - `Directional2DBlendStateUtils`
   - `CollectionUtils`

3. **Add AggressiveInlining to hot methods**
   - All `Evaluate` methods in transitions
   - All `Update*` methods in state utils

### Phase 2: Parallel Job Scheduling (2-3 days)

1. **Parallelize state type update jobs** in `UpdateAnimationStatesSystem`
2. **Parallelize clean jobs** after updates complete
3. **Verify archetype exclusivity** to ensure safe parallelism

### Phase 3: Data Compaction (3-5 days)

1. **Implement packed bool parameters** with bitfield
2. **Optimize AnimationState layout** with flags byte
3. **Extract blob references** to entity-level component
4. **Reduce ClipSampler size** from ~48 to ~16 bytes

### Phase 4: Vectorization (5-7 days)

1. **Vectorize weight zeroing loops** with UnsafeUtility.MemClear
2. **Vectorize weight normalization** with float4 operations
3. **Batch angle calculations** with SIMD atan2
4. **Implement early-exit** in transition evaluation

### Phase 5: Advanced Optimizations (Optional)

1. **SoA layout for samplers** (if profiling shows benefit)
2. **Generation-based sampler handles**
3. **Packed transition conditions**
4. **Background blob building**

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

- Unity Entities 1.0+
- Unity Burst 1.8+
- Unity Collections 2.0+
- Latios Framework (Kinemation)

---

## Notes

- Most authoring classes **cannot** be converted to structs due to Unity's ScriptableObject requirement
- The runtime is already highly optimized - these are incremental improvements
- Focus optimization effort on systems with highest entity counts
- Profile before and after each change to verify improvement

---

*Last Updated: 2026-02-01*
*Branch: claude/burst-vectorization-optimization-uzb1q*
