# Unity Controller Bridge - Feature Parity Analysis

## Overview

This document provides a comprehensive analysis of Unity AnimatorController features and our current implementation status, along with a roadmap to achieve full feature parity.

**Last Updated**: January 2026

---

## Unity AnimatorController Feature Matrix

### Legend
- ✅ **Fully Supported**: Implemented and tested
- 🟡 **Partially Supported**: Basic implementation, missing advanced features
- ❌ **Not Supported**: Not implemented (blocked by Unity or DMotion limitations)
- 🔵 **Planned**: Not implemented yet, but feasible

---

## 1. Core Features

| Feature | Unity Support | DMotion Support | Bridge Status | Notes |
|---------|---------------|-----------------|---------------|-------|
| **Parameters** | ✅ | ✅ | ✅ | Fully supported |
| └─ Float | ✅ | ✅ | ✅ | Direct mapping |
| └─ Int | ✅ | ✅ | ✅ | Direct mapping |
| └─ Bool | ✅ | ✅ | ✅ | Direct mapping |
| └─ Trigger | ✅ | ❌ | 🟡 | Converted to Bool (manual reset required) |
| **States** | ✅ | ✅ | ✅ | Single clip states supported |
| **Transitions** | ✅ | ✅ | ✅ | Conditions and exit time supported |
| **State Machines** | ✅ | ✅ | ✅ | Base layer only |

---

## 2. Parameters (Detailed)

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| Float parameters | ✅ | ✅ | ✅ | `FloatParameterAsset` |
| Int parameters | ✅ | ✅ | ✅ | `IntParameterAsset` |
| Bool parameters | ✅ | ✅ | ✅ | `BoolParameterAsset` |
| Trigger parameters | ✅ | ❌ | 🟡 | Converted to Bool, auto-reset must be manual |
| Default values | ✅ | ✅ | ✅ | All parameter types support defaults |
| Parameter min/max range | ✅ | ❌ | ❌ | Unity editor UI only, not in runtime data |

### Trigger Parameter Conversion
**Current Behavior**: Triggers are converted to Bool parameters with default value `false`.
**Limitation**: Unity's auto-reset behavior (trigger resets after being consumed) must be implemented manually in gameplay code.
**Warning**: Conversion logs a warning message to inform users.

---

## 3. States

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **Single Clip States** | ✅ | ✅ | ✅ | `SingleClipStateAsset` |
| State name | ✅ | ✅ | ✅ | Direct mapping |
| Animation clip | ✅ | ✅ | ✅ | References Unity AnimationClip |
| Speed | ✅ | ✅ | ✅ | Float multiplier |
| Speed parameter | ✅ | ❌ | ❌ | Not supported in DMotion |
| Loop | ✅ | ✅ | ✅ | Bool flag |
| Cycle offset | ✅ | ❌ | ❌ | Not in DMotion's data model |
| Mirror | ✅ | ❌ | ❌ | Humanoid-specific feature |
| Foot IK | ✅ | ❌ | ❌ | Requires IK system |
| Write Defaults | ✅ | N/A | N/A | Unity-specific behavior |
| **Motion Time** | ✅ | ❌ | ❌ | Direct time control |
| **Tag** | ✅ | ❌ | ❌ | Unity gameplay feature |
| **State Machine Behaviors** | ✅ | ❌ | ❌ | C# scripts on states |

### State Features To Consider

#### Speed Parameter (🔵 Planned)
Unity allows state speed to be controlled by a parameter dynamically. DMotion would need to support this in its runtime.

**Unity Data**:
```csharp
state.speedParameterActive = true;
state.speedParameter = "RunSpeed";
```

**Required DMotion Changes**:
- Add `SpeedParameter` field to `AnimationStateAsset`
- Runtime system to multiply speed by parameter value

#### Cycle Offset (🔵 Planned)
Allows starting animation at a specific normalized time offset.

**Unity Data**: `state.cycleOffset = 0.5f` (start at 50% through animation)

**Required DMotion Changes**:
- Add `CycleOffset` to state data
- Apply offset when initializing state playback

#### State Machine Behaviors (❌ Blocked)
Unity's StateMachineBehaviour scripts run on state enter/exit/update. This is a gameplay feature that would require:
- A callback system in DMotion
- Managed code integration (anti-pattern for DOTS)
- Better handled at gameplay layer

---

## 4. Transitions

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **Basic Transition** | ✅ | ✅ | ✅ | `StateOutTransition` |
| Duration | ✅ | ✅ | ✅ | Blend duration in seconds |
| Fixed Duration | ✅ | ❌ | 🟡 | Always treated as fixed |
| Offset | ✅ | ❌ | ❌ | Start time offset in destination |
| **Exit Time** | ✅ | ✅ | ✅ | Converted normalized → absolute |
| Has Exit Time | ✅ | ✅ | ✅ | `HasEndTime` flag |
| Normalized Exit Time | ✅ | ✅ | ✅ | Converted to absolute based on clip length |
| **Conditions** | ✅ | ✅ | ✅ | All modes supported |
| └─ Bool If/IfNot | ✅ | ✅ | ✅ | `BoolConditionComparison` |
| └─ Int Equals | ✅ | ✅ | ✅ | `IntConditionComparison.Equal` |
| └─ Int NotEqual | ✅ | ✅ | ✅ | `IntConditionComparison.NotEqual` |
| └─ Int Greater | ✅ | ✅ | ✅ | `IntConditionComparison.Greater` |
| └─ Int Less | ✅ | ✅ | ✅ | `IntConditionComparison.Less` |
| └─ Float Greater/Less | ✅ | ❌ | ❌ | DMotion doesn't have float conditions |
| **Interruption** | ✅ | ❌ | ❌ | Advanced transition control |
| Can Transition To Self | ✅ | ❌ | ❌ | Not tested |
| Ordered Interruption | ✅ | ❌ | ❌ | Priority-based transition selection |

### Transition Offset (🔵 Planned)
Unity allows starting destination state at a time offset.

**Unity Data**: `transition.offset = 0.3f` (start dest at 30%)

**Use Case**: Synchronizing animations (e.g., starting run at foot-down frame)

**Required DMotion Changes**:
- Add `StartOffset` to `StateOutTransition`
- Apply offset when entering destination state

### Interruption System (❌ Complex)
Unity's interruption system allows transitions to be interrupted by higher-priority transitions. This is complex and would require:
- Transition priority system
- Interruption rules (Source/Destination/Both)
- Ordered interruption logic

**Recommendation**: Defer until user demand is established.

---

## 5. Blend Trees

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **1D Blend Tree** | ✅ | ✅ | ✅ | `LinearBlendStateAsset` |
| Blend parameter | ✅ | ✅ | ✅ | Float parameter |
| Clip thresholds | ✅ | ✅ | ✅ | `ClipWithThreshold` array |
| Child time scale | ✅ | ✅ | ✅ | Per-clip speed multiplier |
| Auto thresholds | ✅ | N/A | N/A | Editor convenience feature |
| **2D Simple Directional** | ✅ | ❌ | ❌ | Not supported by DMotion |
| **2D Freeform Directional** | ✅ | ❌ | ❌ | Not supported by DMotion |
| **2D Freeform Cartesian** | ✅ | ❌ | ❌ | Not supported by DMotion |
| **Direct Blend Tree** | ✅ | ❌ | ❌ | Not supported by DMotion |
| **Nested Blend Trees** | ✅ | ❌ | ❌ | Blend tree as blend tree child |

### 2D Blend Trees (❌ Blocked by DMotion)
Unity supports several 2D blend tree types that blend animations based on two parameters (e.g., forward speed + strafe speed).

**Types**:
1. **Simple Directional**: One animation per cardinal direction
2. **Freeform Directional**: Multiple animations per direction, direction-aware blending
3. **Freeform Cartesian**: Two independent parameters (e.g., angular + linear speed)

**DMotion Limitation**: Only `LinearBlendStateAsset` (1D) is supported.

**Future Work**: Would require new `2DBlendStateAsset` type and runtime blending system.

### Direct Blend Trees (❌ Blocked by DMotion)
Direct blend trees allow manual control of each animation's weight via parameters.

**Use Case**: Facial animation, layered idle animations, procedural blending

**DMotion Limitation**: No equivalent asset type.

**Future Work**: Would require `DirectBlendStateAsset` with per-clip parameter mapping.

---

## 6. Layers

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **Multiple Layers** | ✅ | ❌ | 🟡 | Only base layer converted |
| Layer weight | ✅ | ❌ | ❌ | Blending between layers |
| Avatar mask | ✅ | ❌ | ❌ | Body part masking |
| Layer blending mode | ✅ | ❌ | ❌ | Override vs Additive |
| └─ Override | ✅ | ❌ | ❌ | Replace base layer animation |
| └─ Additive | ✅ | ❌ | ❌ | Add on top of base layer |
| Sync layer | ✅ | ❌ | ❌ | Mirror timing from another layer |
| IK Pass | ✅ | ❌ | ❌ | Per-layer IK enable |

### Multiple Layers (❌ Blocked by DMotion)
Unity's layer system allows separate state machines for different body parts (e.g., lower body locomotion + upper body aiming).

**Current Bridge Behavior**:
- Only converts base layer (index 0)
- Logs warning if multiple layers detected
- Other layers are ignored

**Why Blocked**: DMotion's `StateMachineAsset` has no concept of layers. The entire animation system operates on a single state machine.

**Future Work**: Would require major DMotion architecture changes:
1. Add `List<LayerData>` to `StateMachineAsset`
2. Runtime layer blending system
3. Avatar mask support for body part filtering

**Workaround**: Users can manually create separate state machines per layer and blend them in gameplay code.

### Avatar Masks (❌ Blocked by DMotion)
Avatar masks define which body parts a layer affects. Essential for upper-body animations layered over locomotion.

**Unity Data**: AvatarMask asset with humanoid bone toggles or transform paths

**DMotion Limitation**: No masking system in animation runtime.

---

## 7. Sub-State Machines

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **Sub-State Machines** | ✅ | ❌ | ❌ | Nested state machines |
| Entry node | ✅ | ❌ | ❌ | Enter sub-machine at specific state |
| Exit node | ✅ | ❌ | ❌ | Exit to parent machine |
| Up node | ✅ | ❌ | ❌ | Navigate to parent |

### Sub-State Machines (🔵 Flattening Possible)
Unity allows hierarchical state machines (e.g., a "Combat" sub-machine containing "LightAttack", "HeavyAttack", "Block" states).

**Current Bridge Behavior**: Not supported, ignored during conversion.

**Possible Solution**: **Flatten sub-state machines** during conversion:
1. Recursively collect all states from sub-machines
2. Prefix state names with sub-machine path (e.g., "Combat_LightAttack")
3. Convert transitions, updating destination state names
4. Preserve transitions to/from sub-machine entry/exit

**Implementation Complexity**: Medium
- Requires recursive traversal of state machine hierarchy
- Name collision resolution
- Entry/Exit transition rewiring

**User Impact**: State names would be verbose but functional

---

## 8. Special Transitions

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **Any State** | ✅ | ❌ | ❌ | Global transitions |
| **Entry State** | ✅ | ✅ | 🟡 | Default state only |
| **Exit State** | ✅ | ❌ | ❌ | Leave state machine |
| Up State (sub-machine) | ✅ | ❌ | ❌ | Exit to parent |

### Any State (🔵 Feasible)
Unity's "Any State" is a special node that allows transitions from any state without creating individual transitions.

**Use Case**: Global interrupt (e.g., "Hit" animation from any state)

**Current Bridge Behavior**: Not supported.

**Possible Solution**: **Expand Any State transitions** during conversion:
1. Find all "Any State" transitions
2. For each state in the machine, create a copy of the Any State transition
3. DMotion sees N explicit transitions instead of 1 Any State transition

**Trade-offs**:
- ✅ Functionally equivalent
- ❌ Verbose (100 states = 100 copies of each Any State transition)
- ❌ Harder to maintain/debug

**Implementation Complexity**: Low-Medium

### Exit State (🔵 Feasible with Convention)
Unity's "Exit" state terminates the state machine (used with sub-machines).

**Possible Solution**: Convert Exit to a special "terminated" state that sets a flag or component on the entity.

**Implementation Complexity**: Medium (requires DMotion runtime support)

---

## 9. Animation Events

| Feature | Unity | DMotion | Bridge | Implementation Notes |
|---------|-------|---------|--------|---------------------|
| **Animation Events** | ✅ | ✅ | ✅ | Fully supported |
| Event name | ✅ | ✅ | ✅ | `AnimationEventName` |
| Normalized time | ✅ | ✅ | ✅ | Float [0-1] |
| Function parameters | ✅ | ❌ | ❌ | String/int/float/object params |

### Function Parameters (❌ Blocked)
Unity AnimationEvents can pass parameters to callback functions. DMotion's `AnimationClipEvent` only has name and time.

**Workaround**: Encode data in event name (e.g., "Footstep_Left", "Footstep_Right") or use separate events.

---

## 10. Advanced Features

| Feature | Unity | DMotion | Bridge | Notes |
|---------|-------|---------|--------|-------|
| **Root Motion** | ✅ | ✅ | ✅ | Via `RootMotionMode` |
| Root motion per state | ✅ | ❌ | ❌ | Global setting only in DMotion |
| **Animator Override Controller** | ✅ | ❌ | ❌ | Runtime clip replacement |
| **Playable API** | ✅ | N/A | N/A | Low-level animation control |
| **Timeline Integration** | ✅ | ❌ | ❌ | Sequence-based animation |

### Root Motion
Unity and DMotion both support root motion, but DMotion's implementation is global per entity, not per-state.

**Unity**: Each state can enable/disable root motion individually
**DMotion**: `RootMotionMode` set on `AnimationStateMachineAuthoring` (applies to all states)

**Limitation**: Cannot mix root motion and non-root motion states in same machine.

---

## Current Implementation Status Summary

### ✅ Fully Supported (18 features)
1. Float parameters
2. Int parameters
3. Bool parameters
4. Single clip states
5. State speed
6. State loop flag
7. Basic transitions
8. Transition duration
9. Exit time (with conversion)
10. Bool conditions (If/IfNot)
11. Int conditions (all comparison modes)
12. 1D blend trees
13. Blend tree parameters
14. Blend tree thresholds
15. Animation clips
16. Animation events (name + time)
17. Default state
18. Root motion (global)

### 🟡 Partially Supported (4 features)
1. **Trigger parameters**: Converted to Bool (manual reset required)
2. **Multiple layers**: Only base layer converted, others ignored
3. **Entry state**: Default state only, no custom entry transitions
4. **Fixed duration**: Always treated as fixed (normalized not distinguished)

### ❌ Not Supported (35 features)

#### Blocked by DMotion Limitations (19)
1. 2D blend trees (all types)
2. Direct blend trees
3. Multiple animation layers
4. Avatar masks
5. Layer blending modes (Override/Additive)
6. Sync layers
7. IK Pass
8. State machine behaviors
9. Speed parameter (dynamic speed control)
10. Cycle offset
11. Mirror animations
12. Foot IK
13. Float conditions in transitions
14. Transition interruption
15. Ordered interruption
16. Can Transition To Self
17. Animation event parameters
18. Animator Override Controller
19. Per-state root motion

#### Not Yet Implemented but Feasible (11)
1. Sub-state machines (can flatten)
2. Any State transitions (can expand)
3. Exit state (can use convention)
4. Transition offset
5. Motion time control
6. State tags (can store as metadata)
7. Nested blend trees (can flatten)
8. Entry node transitions
9. Parameter min/max ranges (metadata only)
10. Write Defaults (may not be needed)
11. Timeline integration

#### Unity Editor Features (5)
1. Auto thresholds (editor convenience)
2. Visual graph layout (preserved but optional)
3. State/transition preview
4. Parameter debugging
5. Profiler integration

---

## Recommended Action Plan

### Phase 12: Near-Term Improvements (High Value, Low Complexity)

**Goal**: Address most impactful missing features that are feasible with current DMotion.

#### 12.1: Speed Parameter Support
**Complexity**: Medium
**Value**: High
**Blocked By**: DMotion runtime changes needed

**Tasks**:
1. Extend DMotion `AnimationStateAsset` to support speed parameter reference
2. Update runtime to multiply state speed by parameter value each frame
3. Update bridge to convert `state.speedParameterActive` and `state.speedParameter`
4. Add tests for dynamic speed control

#### 12.2: Cycle Offset Support
**Complexity**: Low
**Value**: Medium
**Blocked By**: DMotion runtime changes needed

**Tasks**:
1. Add `CycleOffset` field to DMotion `AnimationStateAsset`
2. Update runtime to apply offset on state entry
3. Update bridge to convert `state.cycleOffset`
4. Add tests for offset behavior

#### 12.3: Transition Offset Support
**Complexity**: Low
**Value**: Medium
**Blocked By**: DMotion runtime changes needed

**Tasks**:
1. Add `StartOffset` to DMotion `StateOutTransition`
2. Update runtime to apply offset when entering destination state
3. Update bridge to convert `transition.offset`
4. Add tests for transition offset

#### 12.4: Any State Expansion
**Complexity**: Medium
**Value**: High
**Blocked By**: None (pure conversion logic)

**Tasks**:
1. Detect Any State node in `UnityControllerAdapter`
2. Expand Any State transitions to explicit transitions for each state
3. Log info message about expansion (N states → N transitions)
4. Add tests with Any State
5. Document trade-offs (verbosity vs functionality)

**Implementation**:
```csharp
// In UnityControllerAdapter.ReadStateMachine()
private static void ExpandAnyStateTransitions(StateMachineData data, AnimatorStateMachine unityStateMachine)
{
    // Read Any State transitions
    var anyStateTransitions = unityStateMachine.anyStateTransitions;

    // For each normal state
    foreach (var state in data.States)
    {
        // Create copies of Any State transitions
        foreach (var anyTransition in anyStateTransitions)
        {
            var expandedTransition = ReadTransition(anyTransition);
            state.Transitions.Add(expandedTransition);
        }
    }

    _log.AddInfo($"Expanded {anyStateTransitions.Length} Any State transition(s) to {data.States.Count * anyStateTransitions.Length} explicit transitions");
}
```

#### 12.5: Sub-State Machine Flattening
**Complexity**: High
**Value**: Medium
**Blocked By**: None (pure conversion logic)

**Tasks**:
1. Recursively traverse sub-state machines
2. Flatten hierarchy with name prefixing (e.g., "Combat.LightAttack")
3. Rewire transitions to flattened names
4. Handle entry/exit nodes
5. Add tests with nested sub-machines
6. Document flattening behavior

**Implementation Sketch**:
```csharp
private static List<StateData> FlattenStateMachine(AnimatorStateMachine machine, string prefix = "")
{
    var flatStates = new List<StateData>();

    // Add direct states
    foreach (var childState in machine.states)
    {
        var state = ReadState(childState.state, childState.position);
        state.Name = string.IsNullOrEmpty(prefix) ? state.Name : $"{prefix}.{state.Name}";
        flatStates.Add(state);
    }

    // Recursively flatten sub-machines
    foreach (var subMachine in machine.stateMachines)
    {
        string subPrefix = string.IsNullOrEmpty(prefix)
            ? subMachine.stateMachine.name
            : $"{prefix}.{subMachine.stateMachine.name}";
        flatStates.AddRange(FlattenStateMachine(subMachine.stateMachine, subPrefix));
    }

    return flatStates;
}
```

---

### Phase 13: Medium-Term Goals (Require DMotion Changes)

**Goal**: Features that require DMotion runtime/data structure changes.

#### 13.1: Float Conditions
**Blocked By**: DMotion `TransitionCondition` doesn't support float comparisons

**Required DMotion Changes**:
1. Add float comparison support to `TransitionCondition`
2. Update runtime transition evaluation

#### 13.2: Transition Interruption
**Blocked By**: DMotion has no interruption system

**Required DMotion Changes**:
1. Add interruption mode to transitions
2. Add transition priority system
3. Update runtime to check for interruptions during blend

#### 13.3: State Tags
**Blocked By**: DMotion has no tag system

**Required DMotion Changes**:
1. Add `string Tag` to `AnimationStateAsset`
2. Provide API to query states by tag

---

### Phase 14: Long-Term Goals (Major Features)

**Goal**: Large features requiring significant DMotion architecture changes.

#### 14.1: 2D Blend Trees
**Blocked By**: DMotion only supports 1D blending

**Required DMotion Changes**:
1. New asset types: `2DBlendStateAsset` (Simple/Freeform/Cartesian)
2. 2D blending runtime system
3. Parameter pair management

**Complexity**: Very High
**Value**: High (critical for locomotion)

#### 14.2: Multiple Layers
**Blocked By**: DMotion's single state machine design

**Required DMotion Changes**:
1. `List<LayerData>` in `StateMachineAsset`
2. Layer blending system (Override/Additive)
3. Avatar mask support
4. Per-layer weight control
5. Major runtime refactor

**Complexity**: Very High
**Value**: High (critical for AAA animation)

#### 14.3: Direct Blend Trees
**Blocked By**: No equivalent in DMotion

**Required DMotion Changes**:
1. New `DirectBlendStateAsset` type
2. Per-clip parameter binding
3. Runtime direct blending

**Complexity**: High
**Value**: Medium (niche use cases)

---

### Phase 15: Polish & Optimization

**Goal**: Improve bridge quality and user experience.

#### 15.1: Better Error Messages
- Detailed conversion logs per feature
- Suggestions for unsupported features
- Links to documentation

#### 15.2: Conversion Report
- HTML/Markdown report of what was converted
- Feature comparison: Unity vs DMotion
- Warnings and recommendations

#### 15.3: Visual Diff Tool
- Show Unity controller vs DMotion asset side-by-side
- Highlight missing features
- Editor window for comparison

#### 15.4: Batch Conversion
- Convert all controllers in project at once
- Progress bar and statistics
- Export report for all conversions

---

## References

Based on Unity 6000.3 Documentation (January 2026):

- [Animation State Machines](https://docs.unity3d.com/6000.3/Documentation/Manual/AnimationStateMachines.html)
- [Animator Controller](https://docs.unity3d.com/6000.3/Documentation/Manual/class-AnimatorController.html)
- [Animation Parameters](https://docs.unity3d.com/6000.3/Documentation/Manual/AnimationParameters.html)
- [Animation Layers](https://docs.unity3d.com/6000.3/Documentation/Manual/AnimationLayers.html)
- [Animation States](https://docs.unity3d.com/6000.3/Documentation/Manual/class-State.html)
- [State Machine Transitions](https://docs.unity3d.com/Manual/StateMachineTransitions.html)
- [Sub-State Machines](https://docs.unity3d.com/Manual/NestedStateMachines.html)
- [2D Blending](https://docs.unity3d.com/6000.3/Documentation/Manual/BlendTree-2DBlending.html)
- [Direct Blending](https://docs.unity3d.com/2022.3/Documentation/Manual//BlendTree-DirectBlending.html)
- [Avatar Mask Window](https://docs.unity3d.com/Manual/class-AvatarMask.html)
- [Inverse Kinematics](https://docs.unity3d.com/6000.2/Documentation/Manual/InverseKinematics.html)
- [Scripting Root Motion](https://docs.unity3d.com/6000.3/Documentation/Manual/ScriptingRootMotion.html)

---

## Summary Statistics

**Total Unity Features Analyzed**: 57

**Feature Support Breakdown**:
- ✅ Fully Supported: 18 (32%)
- 🟡 Partially Supported: 4 (7%)
- ❌ Not Supported: 35 (61%)

**Not Supported Breakdown**:
- Blocked by DMotion: 19 (33%)
- Feasible to Add: 11 (19%)
- Editor Features: 5 (9%)

**Near-Term Implementation Potential**:
- Phase 12 could add 5 major features (Any State, Sub-machines, Speed Param, Cycle Offset, Transition Offset)
- Would increase support to ~40% of Unity features
- All implementable without waiting for DMotion changes (except speed/offset features)

**Long-Term Goals**:
- 2D Blend Trees and Multiple Layers are highest-value missing features
- Both require major DMotion architecture changes
- Would bring support to ~70% with those two alone
