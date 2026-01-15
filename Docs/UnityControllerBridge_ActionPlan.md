# Unity Controller Bridge - Action Plan for Feature Parity

## Executive Summary

**Current Support**: 32% of Unity AnimatorController features
**After Phase 12**: ~40% (High-value improvements)
**After Phases 13-14**: ~70% (Requires DMotion changes)

This document provides a prioritized roadmap to improve Unity AnimatorController conversion fidelity.

---

## Current Status

### ✅ What Works Today (18 features)
- Float, Int, Bool parameters (Trigger→Bool conversion)
- Single clip states with speed and loop
- Transitions with conditions and exit time
- 1D blend trees
- Animation events (name + time)
- Base layer only (warns about multiple layers)

### ❌ Major Gaps (Top 5)
1. **2D Blend Trees** - Blocked by DMotion (critical for locomotion)
2. **Multiple Layers** - Blocked by DMotion (critical for AAA)
3. **Any State Transitions** - Not implemented (easy to add)
4. **Sub-State Machines** - Not implemented (medium to add)
5. **Speed Parameters** - Blocked by DMotion (useful for dynamic speed)

---

## Phase 12: Quick Wins (Near-Term)
**Timeline**: 1-2 weeks
**Dependencies**: None for items 12.4-12.5, DMotion for 12.1-12.3
**Impact**: High value, improves workflow significantly

### 12.1: Speed Parameter Support ⭐⭐⭐
**Value**: High | **Complexity**: Medium | **DMotion Changes Required**: Yes

Allow states to have speed controlled by parameters at runtime.

**What It Solves**:
- Dynamic animation speed (e.g., run speed based on velocity)
- Slope-based speed adjustment
- Time dilation effects

**Implementation**:
```csharp
// DMotion changes needed:
// 1. Add to AnimationStateAsset:
public FloatParameterAsset SpeedParameter;

// 2. Update runtime to multiply:
float finalSpeed = state.Speed * speedParameterValue;
```

**Bridge Changes**:
```csharp
// In ConvertState():
if (state.SpeedParameterActive)
{
    convertedState.SpeedParameterName = state.SpeedParameter;
    // Link to parameter in DMotionAssetBuilder
}
```

**Tests**:
- State with speed parameter
- Speed changes at runtime
- Null parameter handling

---

### 12.2: Cycle Offset Support ⭐⭐
**Value**: Medium | **Complexity**: Low | **DMotion Changes Required**: Yes

Start animations at a specific normalized time offset.

**What It Solves**:
- Stagger idle animations for crowds (each character at different offset)
- Synchronized starts (e.g., jump at apex)

**Implementation**:
```csharp
// DMotion changes:
// Add to AnimationStateAsset:
public float CycleOffset = 0f; // [0-1]

// Apply on state entry in runtime
```

**Bridge Changes**:
```csharp
// In DMotionAssetBuilder.CreateSingleClipState():
asset.CycleOffset = state.CycleOffset;
```

---

### 12.3: Transition Offset Support ⭐⭐
**Value**: Medium | **Complexity**: Low | **DMotion Changes Required**: Yes

Start destination state at a time offset.

**What It Solves**:
- Synchronized transitions (e.g., enter run at specific foot phase)
- Better blending between animations

**Implementation**:
```csharp
// DMotion changes:
// Add to StateOutTransition:
public float DestinationOffset = 0f;
```

---

### 12.4: Any State Expansion ⭐⭐⭐⭐
**Value**: High | **Complexity**: Medium | **DMotion Changes Required**: No

Convert Unity's "Any State" to explicit transitions from every state.

**What It Solves**:
- Global interrupts (e.g., "Hit" from any state)
- Death/stun animations
- Commonly used Unity pattern

**Trade-offs**:
- ✅ Functionally equivalent
- ❌ Verbose (100 states × 3 Any State transitions = 300 transitions)
- ❌ Asset size increase

**Implementation**:
```csharp
// In UnityControllerAdapter.ReadStateMachine():
private void ExpandAnyStateTransitions(StateMachineData data,
                                       AnimatorStateMachine unity)
{
    var anyTransitions = unity.anyStateTransitions;

    foreach (var state in data.States)
    {
        foreach (var anyTrans in anyTransitions)
        {
            var expanded = ReadTransition(anyTrans);
            expanded.DestinationStateName = GetDestinationName(anyTrans);
            state.Transitions.Add(expanded);
        }
    }

    _log.AddInfo($"Expanded {anyTransitions.Length} Any State " +
                 $"transitions to {data.States.Count * anyTransitions.Length} " +
                 $"explicit transitions");
}
```

**Tests**:
- Controller with Any State transitions
- Verify all states have copies
- Check condition preservation

---

### 12.5: Sub-State Machine Flattening ⭐⭐⭐
**Value**: High | **Complexity**: High | **DMotion Changes Required**: No

Recursively flatten nested state machines with name prefixing.

**What It Solves**:
- Hierarchical state machines (e.g., Combat.LightAttack)
- Commonly used for organization
- Allows complex Unity controllers to work

**Example**:
```
Unity:
  Base Layer
    ├─ Idle
    ├─ Combat (sub-machine)
    │   ├─ LightAttack
    │   ├─ HeavyAttack
    │   └─ Block
    └─ Locomotion (sub-machine)
        ├─ Walk
        └─ Run

DMotion (flattened):
  ├─ Idle
  ├─ Combat.LightAttack
  ├─ Combat.HeavyAttack
  ├─ Combat.Block
  ├─ Locomotion.Walk
  └─ Locomotion.Run
```

**Implementation**:
```csharp
private List<StateData> FlattenStateMachine(
    AnimatorStateMachine machine,
    string prefix = "")
{
    var states = new List<StateData>();

    // Direct states
    foreach (var child in machine.states)
    {
        var state = ReadState(child.state, child.position);
        state.Name = AddPrefix(prefix, state.Name);
        states.Add(state);
    }

    // Recurse into sub-machines
    foreach (var sub in machine.stateMachines)
    {
        string subPrefix = AddPrefix(prefix, sub.stateMachine.name);
        states.AddRange(FlattenStateMachine(sub.stateMachine, subPrefix));
    }

    return states;
}

private string AddPrefix(string prefix, string name)
{
    return string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
}

// Handle Entry/Exit transitions:
private void ResolveSubMachineTransitions(
    List<StateData> allStates,
    AnimatorStateMachine machine,
    string prefix)
{
    // Entry transitions → transitions to entry state of sub-machine
    // Exit transitions → remove (or transition to next state)
    // Rewire all transition destination names with prefixes
}
```

**Challenges**:
1. Entry node transitions (which state to enter?)
   - Solution: Use sub-machine's default state
2. Exit node transitions (where to go?)
   - Solution: Find transitions from sub-machine node in parent
3. Name collisions (Combat.Attack + Attack?)
   - Solution: Always use fully qualified names

**Tests**:
- Nested sub-machines (2-3 levels deep)
- Entry/Exit transitions
- Transitions between sub-machines
- Prefixed name resolution

---

## Phase 13: Medium-Term Goals
**Timeline**: 1-3 months
**Dependencies**: DMotion runtime changes
**Impact**: Improve advanced animation scenarios

### 13.1: Float Conditions ⭐⭐
**Value**: Medium | **Complexity**: Low | **DMotion Changes**: Yes

Support float comparisons in transition conditions.

**What It Solves**:
- Blend parameter-based transitions
- Speed threshold transitions

**DMotion Changes**:
```csharp
// Add to TransitionCondition:
public FloatConditionComparison FloatComparisonMode;
public float FloatComparisonValue;
```

---

### 13.2: Transition Interruption ⭐
**Value**: Low | **Complexity**: High | **DMotion Changes**: Yes

Support Unity's transition interruption system.

**What It Solves**:
- Complex priority-based transitions
- Responsive animation (interrupt blend mid-way)

**Trade-off**: Very complex, low user demand. Defer until requested.

---

### 13.3: State Tags ⭐⭐
**Value**: Medium | **Complexity**: Low | **DMotion Changes**: Yes

Store and query state tags.

**What It Solves**:
- Query "is entity in combat state?" (check tag instead of name)
- Gameplay logic based on animation state type

**DMotion Changes**:
```csharp
// Add to AnimationStateAsset:
public string Tag;

// API:
bool HasTag(StateMachine machine, string tag);
```

---

## Phase 14: Long-Term Goals
**Timeline**: 6+ months
**Dependencies**: Major DMotion architecture changes
**Impact**: Critical for AAA-quality animation

### 14.1: 2D Blend Trees ⭐⭐⭐⭐⭐
**Value**: Very High | **Complexity**: Very High | **DMotion Changes**: Major

Support Unity's 2D blend trees (Simple Directional, Freeform, Cartesian).

**What It Solves**:
- Locomotion blending (forward speed + strafe speed)
- Industry standard for character movement
- Most requested missing feature

**DMotion Changes**:
1. New asset types:
   - `BlendTree2DSimpleDirectionalAsset`
   - `BlendTree2DFreeformDirectionalAsset`
   - `BlendTree2DFreeformCartesianAsset`
2. Runtime 2D blending algorithms
3. Parameter pair management
4. Editor visualization

**Complexity**: This is a major feature requiring months of work.

---

### 14.2: Multiple Layers ⭐⭐⭐⭐⭐
**Value**: Very High | **Complexity**: Very High | **DMotion Changes**: Major

Support Unity's animation layers with masking and blending.

**What It Solves**:
- Upper body + lower body animations
- AAA character animation (essential)
- Layered blending (e.g., aim while walking)

**DMotion Changes**:
1. `List<AnimationLayer>` in StateMachineAsset
2. Avatar mask system (body part filtering)
3. Layer blending modes (Override, Additive)
4. Layer weight control
5. Major runtime refactor

**Complexity**: This is the largest missing feature, requires complete rethink of DMotion's animation architecture.

---

### 14.3: Direct Blend Trees ⭐⭐
**Value**: Medium | **Complexity**: High | **DMotion Changes**: Major

Support Unity's direct blend trees (manual weight control).

**What It Solves**:
- Facial animation (smile, frown, blink - independent blending)
- Layered idles (sway, breathe, fidget)
- Procedural animation blending

**DMotion Changes**:
```csharp
public class DirectBlendStateAsset : AnimationStateAsset
{
    public List<DirectBlendClip> Clips;
}

public class DirectBlendClip
{
    public AnimationClipAsset Clip;
    public FloatParameterAsset WeightParameter; // Direct control
    public float Speed = 1f;
}
```

---

## Phase 15: Polish & Tooling
**Timeline**: Ongoing
**Impact**: User experience improvements

### 15.1: Conversion Report Generator
Generate detailed HTML/Markdown reports showing:
- What was converted
- What was skipped (with reasons)
- Feature comparison table
- Recommendations

### 15.2: Visual Diff Tool
Editor window showing:
- Unity controller (left)
- DMotion asset (right)
- Highlighting differences
- Missing features in red

### 15.3: Batch Conversion Tool
- Convert all controllers in project
- Progress bar
- Summary statistics
- Export reports

---

## Prioritization Matrix

| Feature | Value | Complexity | DMotion Needed | Priority |
|---------|-------|------------|----------------|----------|
| Any State Expansion | High | Medium | No | 🔥 **NOW** |
| Sub-Machine Flattening | High | High | No | 🔥 **NOW** |
| Speed Parameter | High | Medium | Yes | ⭐ High |
| 2D Blend Trees | Very High | Very High | Yes | ⭐ High |
| Multiple Layers | Very High | Very High | Yes | ⭐ High |
| Cycle Offset | Medium | Low | Yes | ✓ Medium |
| Transition Offset | Medium | Low | Yes | ✓ Medium |
| Float Conditions | Medium | Low | Yes | ✓ Medium |
| State Tags | Medium | Low | Yes | ✓ Medium |
| Direct Blend Trees | Medium | High | Yes | ○ Low |
| Interruption | Low | High | Yes | ○ Low |

---

## Recommended Implementation Order

### Stage 1: Bridge Improvements (No DMotion Changes)
1. **Any State Expansion** (1 week)
2. **Sub-Machine Flattening** (2 weeks)

**Outcome**: Significantly more Unity controllers will "just work"

---

### Stage 2: DMotion Runtime Enhancements
3. **Speed Parameter** (1 week)
4. **Cycle Offset** (2 days)
5. **Transition Offset** (2 days)
6. **Float Conditions** (3 days)
7. **State Tags** (3 days)

**Outcome**: More dynamic and expressive animations

---

### Stage 3: Major Features (Coordinate with DMotion Team)
8. **2D Blend Trees** (2-3 months)
9. **Multiple Layers** (3-4 months)

**Outcome**: AAA-quality animation parity with Unity

---

### Stage 4: Polish
10. **Conversion Reports**
11. **Visual Diff Tool**
12. **Batch Conversion**

**Outcome**: Professional-quality tooling

---

## Success Metrics

**Phase 12 Targets**:
- ✅ Any State support (test with 3+ real controllers)
- ✅ Sub-machine support (test with nested machines)
- ✅ No regressions in existing conversions
- ✅ Updated test coverage (95%+)

**Phase 13 Targets**:
- ✅ Speed parameters working in sample scene
- ✅ Float conditions tested
- ✅ Tags queryable from gameplay code

**Phase 14 Targets**:
- ✅ 2D locomotion blend tree working
- ✅ Upper body + lower body layering working
- ✅ StarterAssets conversion at 80%+ feature parity

**Overall Goal**: 70%+ of Unity AnimatorController features supported by end of Phase 14.

---

## Next Steps

1. **Review this plan** with team
2. **Prioritize Phase 12 items** based on user feedback
3. **Start with Any State Expansion** (highest value, no blockers)
4. **Create issues** for each task
5. **Coordinate with DMotion team** on runtime changes

---

## Questions for Discussion

1. **DMotion Roadmap**: Are 2D blend trees and layers planned?
2. **Breaking Changes**: Can we modify DMotion asset structures?
3. **User Priorities**: Which missing features cause most pain?
4. **Performance**: Are there concerns with expanded Any State transitions?
5. **Timeline**: What's realistic for Phase 12 completion?
