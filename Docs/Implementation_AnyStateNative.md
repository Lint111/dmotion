# Implementation Plan: Native Any State Support in DMotion

## Overview

Implement native Any State transitions in DMotion core, eliminating the need for the Phase 12.4 expansion workaround.

**Status**: Ready to implement
**Priority**: Critical (eliminates existing workaround)
**Complexity**: Medium
**Estimated Timeline**: 1-2 weeks

---

## Goals

1. Add `AnyStateTransition` struct to DMotion
2. Add `AnyStateTransitions` array to `StateMachineAsset`
3. Update runtime to evaluate Any State transitions first
4. Add authoring support for manual DMotion users
5. Update bridge to do 1:1 translation (replace workaround)
6. Verify performance improvement vs workaround
7. Document migration path for existing users

---

## Implementation Tasks

### Phase 1: Core Data Structures (DMotion Runtime)

#### Task 1.1: Add AnyStateTransition Struct
**File**: `Runtime/Assets/StateMachineAsset.cs` (or equivalent)

```csharp
namespace DMotion
{
    /// <summary>
    /// Global transition that can be taken from any state in the state machine.
    /// Evaluated before regular state transitions, matching Unity's behavior.
    /// </summary>
    public struct AnyStateTransition
    {
        /// <summary>Destination state index within the state machine</summary>
        public short DestinationStateIndex;

        /// <summary>Blend duration in seconds</summary>
        public float Duration;

        /// <summary>
        /// Start offset for destination state (normalized time [0-1]).
        /// 0 = start of animation, 0.5 = middle, 1 = end
        /// </summary>
        public float Offset;

        /// <summary>
        /// Requires current state to reach end time before transitioning.
        /// If false, transition can happen immediately when conditions are met.
        /// </summary>
        public bool HasEndTime;

        /// <summary>
        /// End time in seconds (converted from Unity's normalized exit time).
        /// Only checked if HasEndTime is true.
        /// </summary>
        public float EndTime;

        /// <summary>
        /// Fixed duration (true): Duration is in seconds regardless of animation length.
        /// Normalized duration (false): Duration is relative to animation length.
        /// </summary>
        public bool HasFixedDuration;

        /// <summary>
        /// Conditions that must be satisfied for transition to be taken.
        /// All conditions must be true (AND logic).
        /// </summary>
        public BlobArray<TransitionCondition> Conditions;
    }
}
```

**Test Checklist**:
- [ ] Struct size is reasonable (target: ~32 bytes)
- [ ] Can be stored in BlobArray
- [ ] Serializes/deserializes correctly

---

#### Task 1.2: Add AnyStateTransitions to StateMachineAsset
**File**: `Runtime/Assets/StateMachineAsset.cs`

```csharp
public struct StateMachineAsset
{
    // Existing fields
    public BlobArray<AnimationStateAsset> States;
    public int StartingStateIndex;
    public BlobArray<FloatParameterAsset> FloatParameters;
    public BlobArray<IntParameterAsset> IntParameters;
    public BlobArray<BoolParameterAsset> BoolParameters;
    // ... other existing fields

    /// <summary>
    /// Global transitions that can be taken from any state.
    /// Evaluated before regular state transitions.
    /// Empty array if no Any State transitions exist.
    /// </summary>
    public BlobArray<AnyStateTransition> AnyStateTransitions;
}
```

**Test Checklist**:
- [ ] Backward compatibility: Can load old assets without AnyStateTransitions
- [ ] Empty array defaults correctly
- [ ] BlobBuilder can allocate and populate array

---

### Phase 2: Runtime Transition Evaluation

#### Task 2.1: Update Transition Evaluation System
**File**: Find the system that evaluates transitions (likely in `Runtime/Systems/`)

**Current Logic** (pseudocode):
```csharp
// Evaluate regular state transitions
foreach (var transition in currentState.OutTransitions)
{
    if (ConditionsMet(transition.Conditions))
    {
        StartTransition(transition);
        return;
    }
}
```

**New Logic** (pseudocode):
```csharp
// 1. FIRST: Evaluate Any State transitions (Unity's behavior)
foreach (var anyStateTransition in stateMachine.AnyStateTransitions)
{
    if (ConditionsMet(anyStateTransition.Conditions))
    {
        // Check end time if required
        if (!anyStateTransition.HasEndTime ||
            currentStateTime >= anyStateTransition.EndTime)
        {
            StartAnyStateTransition(anyStateTransition);
            return; // Take first matching Any State transition
        }
    }
}

// 2. THEN: Evaluate regular state transitions (existing logic)
foreach (var transition in currentState.OutTransitions)
{
    if (ConditionsMet(transition.Conditions))
    {
        StartTransition(transition);
        return;
    }
}
```

**Implementation Details**:
- Find where DMotion evaluates `currentState.OutTransitions`
- Add Any State loop **before** regular transitions
- Reuse existing `ConditionsMet()` logic (same condition types)
- Reuse existing transition start logic (same blend mechanics)

**Test Checklist**:
- [ ] Any State transitions evaluated before regular transitions
- [ ] First matching Any State transition is taken
- [ ] End time is checked if HasEndTime = true
- [ ] Conditions are evaluated correctly
- [ ] Blend starts correctly with specified duration

---

#### Task 2.2: Handle Any State Self-Transitions
**Special Case**: Any State can transition to the current state (self-transition)

```csharp
// When evaluating Any State transitions
foreach (var anyStateTransition in stateMachine.AnyStateTransitions)
{
    // Allow self-transitions (state → itself)
    // Unity supports this for animation restarts, reloads, etc.
    if (ConditionsMet(anyStateTransition.Conditions))
    {
        if (anyStateTransition.DestinationStateIndex == currentStateIndex)
        {
            // Self-transition: restart current animation
            RestartCurrentState(anyStateTransition.Duration);
        }
        else
        {
            // Regular transition to different state
            StartAnyStateTransition(anyStateTransition);
        }
        return;
    }
}
```

**Test Checklist**:
- [ ] Self-transitions work correctly (animation restarts)
- [ ] Blend duration applies to self-transitions
- [ ] State enter events fire on self-transition

---

### Phase 3: Authoring Support (For Manual DMotion Users)

#### Task 3.1: Add AnyStateTransitionAuthoring
**File**: `Authoring/StateMachineAuthoring.cs` (or create new file)

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Authoring component for Any State transitions.
    /// These are global transitions that can be taken from any state.
    /// </summary>
    [Serializable]
    public class AnyStateTransitionAuthoring
    {
        [Tooltip("Name of the destination state")]
        public string DestinationStateName;

        [Tooltip("Blend duration in seconds")]
        [Min(0f)]
        public float Duration = 0.15f;

        [Tooltip("Start offset for destination state (0-1, normalized time)")]
        [Range(0f, 1f)]
        public float Offset = 0f;

        [Tooltip("Requires current animation to reach exit time before transitioning")]
        public bool HasExitTime = false;

        [Tooltip("Exit time (normalized, 0-1). Only used if Has Exit Time is true")]
        [Range(0f, 1f)]
        public float ExitTime = 0.75f;

        [Tooltip("Fixed duration (checked) vs normalized duration (unchecked)")]
        public bool HasFixedDuration = true;

        [Tooltip("Conditions that must be satisfied for this transition")]
        public List<TransitionConditionAuthoring> Conditions = new List<TransitionConditionAuthoring>();
    }
}
```

**Test Checklist**:
- [ ] Serializes correctly in Unity Inspector
- [ ] Tooltips are helpful
- [ ] Validation (e.g., DestinationStateName not empty)

---

#### Task 3.2: Update StateMachineAuthoring
**File**: `Authoring/StateMachineAuthoring.cs`

```csharp
public class StateMachineAuthoring : MonoBehaviour
{
    // Existing fields
    public List<AnimationStateAuthoring> States;
    public List<ParameterAuthoring> Parameters;
    public int StartingStateIndex = 0;

    // NEW: Any State transitions
    [Header("Any State Transitions")]
    [Tooltip("Global transitions that can be taken from any state. Evaluated before regular transitions.")]
    public List<AnyStateTransitionAuthoring> AnyStateTransitions = new List<AnyStateTransitionAuthoring>();
}
```

**Editor Enhancement** (Optional but recommended):
```csharp
// Custom inspector to show warning if many Any State transitions
#if UNITY_EDITOR
[CustomEditor(typeof(StateMachineAuthoring))]
public class StateMachineAuthoringEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var authoring = (StateMachineAuthoring)target;
        if (authoring.AnyStateTransitions.Count > 5)
        {
            EditorGUILayout.HelpBox(
                $"Warning: {authoring.AnyStateTransitions.Count} Any State transitions. " +
                "Consider using regular transitions for rarely-used conditions.",
                MessageType.Warning);
        }
    }
}
#endif
```

**Test Checklist**:
- [ ] Any State transitions appear in Inspector
- [ ] Can add/remove Any State transitions
- [ ] Reordering works (first = highest priority)

---

### Phase 4: Baking (Authoring → Runtime Conversion)

#### Task 4.1: Update StateMachineBaker
**File**: Find baker for StateMachineAuthoring (likely `Authoring/StateMachineBaker.cs`)

```csharp
public class StateMachineBaker : Baker<StateMachineAuthoring>
{
    public override void Bake(StateMachineAuthoring authoring)
    {
        // ... existing baking code for states, parameters, etc.

        // NEW: Bake Any State transitions
        var anyStateCount = authoring.AnyStateTransitions?.Count ?? 0;
        if (anyStateCount > 0)
        {
            builder.Allocate(ref asset.AnyStateTransitions, anyStateCount);

            for (int i = 0; i < anyStateCount; i++)
            {
                var anyAuthoring = authoring.AnyStateTransitions[i];

                // Find destination state index
                int destIndex = FindStateIndex(authoring.States, anyAuthoring.DestinationStateName);
                if (destIndex < 0)
                {
                    Debug.LogError($"Any State transition references unknown state: {anyAuthoring.DestinationStateName}");
                    continue;
                }

                // Bake conditions
                var conditions = BakeConditions(anyAuthoring.Conditions);

                // Convert normalized exit time to absolute time
                float endTime = 0f;
                if (anyAuthoring.HasExitTime)
                {
                    var destState = authoring.States[destIndex];
                    endTime = anyAuthoring.ExitTime * GetStateDuration(destState);
                }

                // Create runtime Any State transition
                asset.AnyStateTransitions[i] = new AnyStateTransition
                {
                    DestinationStateIndex = (short)destIndex,
                    Duration = anyAuthoring.Duration,
                    Offset = anyAuthoring.Offset,
                    HasEndTime = anyAuthoring.HasExitTime,
                    EndTime = endTime,
                    HasFixedDuration = anyAuthoring.HasFixedDuration,
                    Conditions = conditions
                };
            }

            Debug.Log($"[DMotion] Baked {anyStateCount} Any State transition(s) for {authoring.name}");
        }
        else
        {
            // No Any State transitions, allocate empty array
            builder.Allocate(ref asset.AnyStateTransitions, 0);
        }
    }

    private int FindStateIndex(List<AnimationStateAuthoring> states, string stateName)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].Name == stateName)
                return i;
        }
        return -1;
    }
}
```

**Test Checklist**:
- [ ] Any State transitions bake correctly
- [ ] State name resolution works
- [ ] Conditions bake correctly
- [ ] Exit time conversion (normalized → absolute) is correct
- [ ] Error handling for missing state names

---

### Phase 5: Bridge Translation (Replace Workaround)

#### Task 5.1: Remove Any State Expansion Workaround
**File**: `Editor/UnityControllerBridge/Adapters/UnityControllerAdapter.cs`

**DELETE** (lines 85-139):
```csharp
// REMOVE THIS METHOD
private static void ExpandAnyStateTransitions(StateMachineData data, AnimatorStateMachine stateMachine)
{
    // ... 50+ lines of workaround code
}
```

**DELETE** call site (line 76):
```csharp
// REMOVE THIS LINE
ExpandAnyStateTransitions(data, stateMachine);
```

---

#### Task 5.2: Add Pure Any State Translation
**File**: `Editor/UnityControllerBridge/Adapters/UnityControllerAdapter.cs`

**ADD** to `ReadStateMachine()`:
```csharp
private static StateMachineData ReadStateMachine(AnimatorStateMachine stateMachine)
{
    var data = new StateMachineData();

    if (stateMachine.defaultState != null)
    {
        data.DefaultStateName = stateMachine.defaultState.name;
    }

    // Read states (existing code)
    foreach (var childState in stateMachine.states)
    {
        var stateData = ReadState(childState.state, childState.position);
        if (stateData != null)
        {
            data.States.Add(stateData);
        }
    }

    // NEW: Read Any State transitions (pure 1:1 translation)
    if (stateMachine.anyStateTransitions != null && stateMachine.anyStateTransitions.Length > 0)
    {
        foreach (var anyTransition in stateMachine.anyStateTransitions)
        {
            var transitionData = ReadTransition(anyTransition);
            if (transitionData != null)
            {
                data.AnyStateTransitions.Add(transitionData);
            }
        }

        UnityEngine.Debug.Log(
            $"[Unity Controller Bridge] Converted {data.AnyStateTransitions.Count} Any State transition(s) " +
            $"(native DMotion support, no expansion needed)"
        );
    }

    return data;
}
```

**ADD** `AnyStateTransitions` list to `StateMachineData`:
```csharp
public class StateMachineData
{
    public string DefaultStateName;
    public List<StateData> States = new List<StateData>();
    public List<TransitionData> AnyStateTransitions = new List<TransitionData>(); // NEW
}
```

**Test Checklist**:
- [ ] Bridge reads Unity Any State transitions
- [ ] 1 Unity Any State = 1 DMotion Any State (no expansion)
- [ ] Conditions preserved correctly
- [ ] Exit time converted correctly

---

#### Task 5.3: Update ConversionEngine to Bake Any State
**File**: `Editor/UnityControllerBridge/Core/ConversionEngine.cs` (or wherever baking happens)

```csharp
private StateMachineAsset ConvertStateMachineData(StateMachineData data)
{
    var builder = new BlobBuilder(Allocator.Temp);
    ref var asset = ref builder.ConstructRoot<StateMachineAsset>();

    // ... existing state conversion code

    // NEW: Convert Any State transitions
    if (data.AnyStateTransitions.Count > 0)
    {
        builder.Allocate(ref asset.AnyStateTransitions, data.AnyStateTransitions.Count);
        for (int i = 0; i < data.AnyStateTransitions.Count; i++)
        {
            var anyTrans = data.AnyStateTransitions[i];
            int destIndex = FindStateIndex(data.States, anyTrans.DestinationStateName);

            asset.AnyStateTransitions[i] = new AnyStateTransition
            {
                DestinationStateIndex = (short)destIndex,
                Duration = anyTrans.Duration,
                Offset = anyTrans.Offset,
                HasEndTime = anyTrans.HasExitTime,
                EndTime = anyTrans.ExitTime,
                HasFixedDuration = anyTrans.HasFixedDuration,
                Conditions = ConvertConditions(builder, anyTrans.Conditions)
            };
        }
    }
    else
    {
        builder.Allocate(ref asset.AnyStateTransitions, 0);
    }

    return builder.CreateBlobAssetReference<StateMachineAsset>(Allocator.Persistent);
}
```

---

### Phase 6: Testing

#### Task 6.1: Unit Tests (DMotion Core)
**File**: `Tests/Runtime/AnyStateTransitionTests.cs` (create new)

```csharp
[Test]
public void AnyStateTransition_EvaluatedBeforeRegularTransitions()
{
    // Setup: State A with regular transition to B, Any State transition to C
    // Both transitions have same condition
    // Expected: Any State wins (goes to C, not B)
}

[Test]
public void AnyStateTransition_FirstMatchingTransitionTaken()
{
    // Setup: Multiple Any State transitions with different conditions
    // Expected: First matching transition is taken
}

[Test]
public void AnyStateTransition_SelfTransition_RestartsAnimation()
{
    // Setup: Any State transition to current state
    // Expected: Animation restarts from beginning
}

[Test]
public void AnyStateTransition_WithExitTime_WaitsForTime()
{
    // Setup: Any State with HasExitTime = true, condition met before exit time
    // Expected: Transition waits until exit time reached
}

[Test]
public void AnyStateTransition_NoAnyStateTransitions_BehavesNormally()
{
    // Setup: State machine with no Any State transitions
    // Expected: Regular transitions work as before
}
```

---

#### Task 6.2: Integration Tests (Bridge)
**File**: `Tests/Editor/UnityControllerBridgeAnyStateTests.cs`

```csharp
[Test]
public void Bridge_AnyStateTransitions_ConvertedCorrectly()
{
    // Create Unity controller with Any State transitions
    // Convert with bridge
    // Verify: DMotion asset has correct AnyStateTransitions array
}

[Test]
public void Bridge_AnyStateTransitions_AssetSize_SmallerThanWorkaround()
{
    // Create Unity controller with 100 states, 3 Any State transitions
    // Convert with bridge
    // Verify: Asset size is ~300x smaller than expansion workaround
}

[Test]
public void Bridge_AnyStateTransitions_NoExpansion()
{
    // Create Unity controller with Any State
    // Convert with bridge
    // Verify: 1 Unity Any State = 1 DMotion Any State (not N expanded)
}
```

---

#### Task 6.3: Performance Tests
**File**: `Tests/Performance/AnyStatePerformanceTests.cs`

```csharp
[Test, Performance]
public void AnyState_EvaluationPerformance_NativeVsWorkaround()
{
    // Setup: 100 entities with state machines
    // Measure: Transition evaluation time with native Any State
    // Compare: vs workaround (N expanded transitions)
    // Expected: Native is ~50% faster (fewer condition checks)
}

[Test, Performance]
public void AnyState_AssetSize_NativeVsWorkaround()
{
    // Setup: State machine with 100 states, 3 Any State transitions
    // Measure: Asset size in bytes
    // Expected: Native = ~3 transitions, Workaround = ~300 transitions
    //           Native should be ~100x smaller
}
```

---

### Phase 7: Documentation

#### Task 7.1: Update DMotion Documentation
**File**: Create `Docs/AnyStateTransitions.md` (DMotion docs)

```markdown
# Any State Transitions

Any State transitions are global transitions that can be taken from any state in your state machine.

## Use Cases
- Hit reactions (can be hit from any state)
- Death (can die from any state)
- Global abilities (dodge, block from any state)

## Creating Any State Transitions

### In Code (Manual DMotion Users)
[Code examples with StateMachineAuthoring]

### From Unity (Unity Controller Bridge)
[Automatic conversion examples]

## Behavior
- Evaluated **before** regular state transitions
- First matching Any State transition is taken
- Can transition to the same state (self-transition for restarts)
- Supports all condition types (Bool, Int, Float, Trigger)

## Performance
- More efficient than manually adding transitions to every state
- Single condition check per Any State (not per state)
```

---

#### Task 7.2: Update Bridge Documentation
**File**: `Docs/UnityControllerBridge_AnyStateGuide.md`

**ADD** section at top:
```markdown
## 🎉 Native Support Available!

As of DMotion v[VERSION], Any State transitions are **natively supported**.

**Old Approach** (Phase 12.4 workaround):
- Bridge expanded 1 Any State → N explicit transitions
- Asset bloat, debugging difficulty

**New Approach** (Native):
- Bridge does pure 1:1 translation
- Optimal asset size, cleaner debugging
- Better performance (~50% faster evaluation)

### Migration
If you're using the workaround version, reconvert your Unity controllers
to get the benefits of native Any State support.
```

---

#### Task 7.3: Create Migration Guide
**File**: `Docs/Migration_AnyStateWorkaroundToNative.md`

```markdown
# Migrating from Any State Workaround to Native Support

If you previously used the Unity Controller Bridge with Phase 12.4 workaround...

## Steps
1. Update DMotion to v[VERSION] or later
2. Reconvert your Unity AnimatorControllers
3. Compare old vs new assets (use AssetComparison tool)
4. Test runtime behavior (should be identical)
5. Delete old workaround assets

## Benefits
- Asset size reduction: ~50-100x smaller
- Faster evaluation: ~50% performance improvement
- Cleaner debugging: Can see Any State origin
```

---

### Phase 8: Rollout

#### Task 8.1: Create Feature Branch
```bash
git checkout -b feature/native-any-state-support
```

#### Task 8.2: Implement in Order
1. Core data structures (Task 1.1-1.2)
2. Runtime evaluation (Task 2.1-2.2)
3. Authoring support (Task 3.1-3.2)
4. Baking (Task 4.1)
5. Unit tests (Task 6.1)
6. Bridge translation (Task 5.1-5.3)
7. Integration tests (Task 6.2)
8. Performance tests (Task 6.3)
9. Documentation (Task 7.1-7.3)

#### Task 8.3: Code Review & Merge
- Create PR with comprehensive description
- Include performance benchmarks
- Show before/after asset size comparison
- Get review from DMotion team
- Merge to main

#### Task 8.4: Release
- Include in DMotion changelog
- Announce native Any State support
- Share migration guide
- Update Unity Controller Bridge documentation

---

## Success Criteria

### Functionality
- ✅ Any State transitions work identically to Unity
- ✅ Backward compatibility: Old assets still load
- ✅ Manual DMotion users can use Any State
- ✅ Bridge does 1:1 translation (no workaround)

### Performance
- ✅ ~50% faster evaluation vs workaround
- ✅ ~100x smaller asset size (100 states, 3 Any State)
- ✅ No runtime overhead vs workaround

### Quality
- ✅ 100% test coverage for Any State code paths
- ✅ Performance benchmarks pass
- ✅ Documentation complete and clear
- ✅ Migration guide available

---

## Risks & Mitigation

### Risk 1: Breaking Changes
**Mitigation**: Keep workaround code for one release with deprecation warning

### Risk 2: Performance Regression
**Mitigation**: Comprehensive performance tests before merge

### Risk 3: Unity Parity Issues
**Mitigation**: Test against Unity sample projects with Any State

### Risk 4: Complex State Machines
**Mitigation**: Test with real-world controllers (100+ states)

---

## Related Documents

- [DMotion-First Plan](UnityControllerBridge_DMotionFirst_Plan.md) - Overall strategy
- [Architecture Analysis](UnityControllerBridge_ArchitectureAnalysis.md) - Why native support is better
- [Any State Guide](UnityControllerBridge_AnyStateGuide.md) - Current workaround documentation
