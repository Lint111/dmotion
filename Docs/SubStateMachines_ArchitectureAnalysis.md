# Sub-State Machines - Architecture Analysis

## Executive Summary

**Feature**: Native hierarchical state machine support in DMotion
**Approach**: DMotion-First (native implementation, not workaround)
**Complexity**: High (but worth it)
**Impact**: Preserves Unity structure, enables clean organization, no name mangling

---

## 1. Unity's Sub-State Machine Architecture

### 1.1 What Are Sub-State Machines?

Unity's Animator Controller supports **hierarchical state machines** where a state can be:
- A regular animation state (single clip, blend tree)
- A **sub-state machine** containing its own states and transitions

```
Example Unity Hierarchy:
Base Layer
  ├─ Idle (regular state)
  ├─ Combat (sub-state machine)
  │   ├─ Entry → LightAttack
  │   ├─ LightAttack (state)
  │   ├─ HeavyAttack (state)
  │   ├─ Block (state)
  │   └─ Exit → [parent transitions]
  └─ Locomotion (sub-state machine)
      ├─ Entry → Walk
      ├─ Walk (state)
      ├─ Run (state)
      └─ Exit → [parent transitions]
```

### 1.2 Key Concepts

#### Entry Node
- Special node marking the default state when entering a sub-machine
- Transitions from Entry → DefaultState
- Analogous to the root state machine's default state

#### Exit Node
- Special node for transitioning OUT of a sub-machine
- State → Exit transitions trigger parent-level transitions
- Allows sub-machine to "complete" and return control to parent

#### Up Node (StateMachine)
- Reference to the parent state machine
- Used in transitions to navigate from child → parent
- Less commonly used than Entry/Exit

#### State Paths
- States have hierarchical paths: `Combat.LightAttack`
- Enables unique identification across hierarchy
- Used in transitions and debugging

---

## 2. Workaround Approach (Flattening)

### 2.1 How Flattening Works

The original Phase 12.5 plan proposed **flattening** - converting hierarchical structure to flat:

```csharp
// Unity Structure (Hierarchical)
StateMachine: Combat
  ├─ LightAttack
  ├─ HeavyAttack
  └─ Block

// DMotion After Flattening (Flat)
States:
  - Combat.LightAttack
  - Combat.HeavyAttack
  - Combat.Block
```

**Algorithm**:
1. Recursively traverse sub-machines
2. Prefix child state names with parent name (`Combat.LightAttack`)
3. Resolve Entry transitions to default state
4. Resolve Exit transitions to parent-level transitions
5. Flatten into single list of states

### 2.2 Flattening Trade-offs

#### ✅ Benefits
- No DMotion runtime changes required
- Fast implementation (bridge-only)
- Functionally equivalent (all transitions work)
- Can implement in 1-2 days

#### ❌ Drawbacks
- **Loses organizational structure** - all states in flat list
- **Name collision risks** - `Combat.Attack` + `Boss.Combat.Attack` = collision
- **Debugging difficulty** - harder to understand state hierarchy
- **No visual nesting** - editor can't show hierarchy
- **Cannot preserve Exit/Up semantics** - must resolve at conversion time
- **Not future-proof** - can't extend with hierarchy-aware features
- **Technical debt** - workaround instead of proper solution

### 2.3 Why Flattening Is Suboptimal

**Example Problem**: Exit Transitions

```
Unity:
Combat (sub-machine)
  ├─ Block → Exit (with conditions)
  └─ Parent has: Combat → Idle

Flattened (loses structure):
Block → Idle (copied from parent transition)
Problem: If parent adds more transitions, flattened version is stale
```

**Example Problem**: Name Collisions

```
Unity:
Character (root)
  ├─ Combat (sub-machine)
  │   └─ Attack (state)
  └─ Boss (sub-machine)
      └─ Combat (sub-machine)
          └─ Attack (state)

Flattened:
Combat.Attack  ← Which one?
Boss.Combat.Attack  ← Nested prefix gets messy
```

---

## 3. Native Approach (Hierarchical)

### 3.1 DMotion-First Architecture

Following the Any State success, implement **native hierarchical support**:

```
DMotion Native Structure:
StateMachineAsset (root)
  ├─ States: List<AnimationStateAsset>
  │   ├─ Idle (SingleClipStateAsset)
  │   ├─ Combat (SubStateMachineAsset) ← NEW type
  │   └─ Locomotion (SubStateMachineAsset)
  │
  └─ AnyStateTransitions: List<StateOutTransition>

SubStateMachineAsset : AnimationStateAsset
  ├─ NestedStateMachine: StateMachineAsset
  ├─ EntryState: AnimationStateAsset (reference)
  └─ ExitTransitions: List<StateOutTransition>
```

### 3.2 Key Design Decisions

#### Decision 1: Recursive Structure
Sub-state machines contain full `StateMachineAsset` instances:
- Enables unlimited nesting depth
- Reuses existing state machine logic
- Clean separation of concerns

#### Decision 2: Entry State Resolution
Entry state is resolved at runtime:
- `SubStateMachineAsset.EntryState` points to default state
- When entering sub-machine, transition to entry state
- Same semantics as root default state

#### Decision 3: Exit Transitions
Exit transitions stored on parent node:
- `SubStateMachineAsset.ExitTransitions`
- Triggered when child state reaches "exit"
- Evaluated like regular transitions

#### Decision 4: State Path Tracking
Runtime tracks current position in hierarchy:
```csharp
struct StateMachineContext
{
    public short CurrentStateIndex;
    public short ParentStateMachineIndex; // -1 if root
    public short DepthLevel;
}
```

### 3.3 Runtime Evaluation Model

#### Hierarchical Transition Evaluation

```
1. Evaluate Any State transitions (global, current level)
2. Evaluate current state transitions (local)
3. If no match, check if we're in a sub-machine
4. If in sub-machine, check Exit transitions
5. If exiting, evaluate parent-level transitions
```

#### State Machine Stack

Maintain a stack of active state machines:
```csharp
struct StateMachineStack
{
    public BlobArray<StateMachineContext> Contexts; // Max depth: 8
    public byte Depth;
}
```

#### Entering Sub-State Machine

```csharp
void EnterSubStateMachine(ref SubStateMachineAsset subMachine)
{
    // Push current context onto stack
    stack.Push(currentContext);

    // Create new context for sub-machine
    currentContext = new StateMachineContext
    {
        CurrentStateIndex = subMachine.EntryState,
        ParentStateMachineIndex = previousStateIndex,
        DepthLevel = (short)(stack.Depth + 1)
    };

    // Transition to entry state
    TransitionToState(subMachine.EntryState);
}
```

#### Exiting Sub-State Machine

```csharp
void ExitSubStateMachine()
{
    // Pop parent context from stack
    currentContext = stack.Pop();

    // Evaluate exit transitions on parent sub-machine node
    EvaluateExitTransitions(currentSubMachine.ExitTransitions);
}
```

---

## 4. Data Structure Design

### 4.1 Runtime Structures (ECS)

#### SubStateMachineBlob (NEW)
```csharp
/// <summary>
/// Runtime blob for a sub-state machine (state that contains other states).
/// </summary>
internal struct SubStateMachineBlob
{
    /// <summary>Nested state machine data</summary>
    internal StateMachineBlob NestedStateMachine;

    /// <summary>Index of entry state within nested machine</summary>
    internal short EntryStateIndex;

    /// <summary>Transitions to take when exiting this sub-machine</summary>
    internal BlobArray<StateOutTransitionGroup> ExitTransitions;
}
```

#### StateMachineBlob (MODIFIED)
```csharp
public struct StateMachineBlob
{
    internal short DefaultStateIndex;
    internal BlobArray<AnimationStateBlob> States;
    internal BlobArray<SingleClipStateBlob> SingleClipStates;
    internal BlobArray<LinearBlendStateBlob> LinearBlendStates;

    // NEW: Sub-state machine support
    internal BlobArray<SubStateMachineBlob> SubStateMachines;

    internal BlobArray<AnyStateTransition> AnyStateTransitions;

    // NEW: Hierarchy metadata
    internal byte MaxNestingDepth; // For validation and stack allocation
}
```

#### AnimationStateBlob (MODIFIED)
```csharp
internal struct AnimationStateBlob
{
    internal AnimationStateId StateId;
    internal AnimationStateType Type; // Add: SubStateMachine
    internal short TypeIndex; // Index into SubStateMachines array
    internal BlobArray<StateOutTransitionGroup> Transitions;
}

internal enum AnimationStateType : byte
{
    SingleClip = 0,
    LinearBlend = 1,
    SubStateMachine = 2, // NEW
}
```

#### StateMachineStack Component (NEW)
```csharp
/// <summary>
/// Tracks the current position in the state machine hierarchy.
/// Each entity has its own stack for navigating nested state machines.
/// </summary>
public struct StateMachineStack : IComponentData
{
    /// <summary>
    /// Stack of state machine contexts (max depth: 8).
    /// Index [0] is root, Index [Depth-1] is current.
    /// </summary>
    public FixedList64Bytes<StateMachineContext> Contexts;

    /// <summary>Current depth in hierarchy (0 = root)</summary>
    public byte Depth;

    /// <summary>Maximum allowed depth (from blob)</summary>
    public byte MaxDepth;
}

internal struct StateMachineContext
{
    public short CurrentStateIndex;
    public short ParentSubMachineIndex; // Which sub-machine node we're in
    public byte Level;
}
```

### 4.2 Authoring Structures

#### SubStateMachineAsset (NEW)
```csharp
/// <summary>
/// Authoring asset for a sub-state machine (state containing nested state machine).
/// </summary>
[CreateAssetMenu(menuName = "DMotion/Sub-State Machine")]
public class SubStateMachineAsset : AnimationStateAsset
{
    [Header("Nested State Machine")]
    [Tooltip("The state machine contained within this state")]
    public StateMachineAsset NestedStateMachine;

    [Header("Entry")]
    [Tooltip("Default state to enter when entering this sub-machine")]
    public AnimationStateAsset EntryState;

    [Header("Exit Transitions")]
    [Tooltip("Transitions to evaluate when exiting this sub-machine")]
    public List<StateOutTransition> ExitTransitions = new();

    // Inherited from AnimationStateAsset:
    // - Speed, Loop (applied to nested machine)
    // - OutTransitions (transitions TO this sub-machine from parent)

    public override AnimationStateType StateType => AnimationStateType.SubStateMachine;
}
```

#### StateMachineAsset (MODIFIED)
```csharp
public class StateMachineAsset : ScriptableObject
{
    public AnimationStateAsset DefaultState;

    // Can now contain SubStateMachineAsset instances
    public List<AnimationStateAsset> States = new();

    public List<AnimationParameterAsset> Parameters = new();
    public List<StateOutTransition> AnyStateTransitions = new();

    // NEW: Hierarchy validation
    public int GetNestingDepth()
    {
        int maxDepth = 0;
        foreach (var state in States)
        {
            if (state is SubStateMachineAsset subMachine)
            {
                int subDepth = subMachine.NestedStateMachine.GetNestingDepth();
                maxDepth = Math.Max(maxDepth, subDepth + 1);
            }
        }
        return maxDepth;
    }
}
```

---

## 5. Runtime Evaluation Algorithm

### 5.1 Hierarchical Transition Evaluation

```csharp
void EvaluateTransitions(
    ref StateMachineStack stack,
    ref StateMachineBlob rootBlob,
    /* parameters */)
{
    // Get current context
    ref var context = ref stack.Contexts[stack.Depth];
    ref var currentBlob = ref GetStateMachineBlobAtDepth(rootBlob, stack);

    // 1. Evaluate Any State transitions (current level)
    if (EvaluateAnyStateTransitions(currentBlob, out var transitionIndex))
    {
        HandleTransition(transitionIndex, ref stack, ref rootBlob);
        return;
    }

    // 2. Evaluate current state transitions
    ref var currentState = ref currentBlob.States[context.CurrentStateIndex];
    if (EvaluateStateTransitions(currentState, out transitionIndex))
    {
        HandleTransition(transitionIndex, ref stack, ref rootBlob);
        return;
    }

    // 3. Check if current state is "Exit" (pseudo-state)
    if (currentState.IsExit)
    {
        ExitCurrentSubStateMachine(ref stack, ref rootBlob);
        return;
    }
}

void HandleTransition(
    short transitionIndex,
    ref StateMachineStack stack,
    ref StateMachineBlob rootBlob)
{
    ref var context = ref stack.Contexts[stack.Depth];
    ref var currentBlob = ref GetStateMachineBlobAtDepth(rootBlob, stack);

    // Get destination state
    ref var transition = ref currentBlob.States[context.CurrentStateIndex]
        .Transitions[transitionIndex];
    short toStateIndex = transition.ToStateIndex;
    ref var toState = ref currentBlob.States[toStateIndex];

    // Check if destination is a sub-state machine
    if (toState.Type == AnimationStateType.SubStateMachine)
    {
        EnterSubStateMachine(toStateIndex, ref stack, ref rootBlob);
    }
    else
    {
        // Regular state transition
        TransitionToState(toStateIndex, ref stack, ref rootBlob);
    }
}

void EnterSubStateMachine(
    short subMachineIndex,
    ref StateMachineStack stack,
    ref StateMachineBlob rootBlob)
{
    // Get sub-machine blob
    ref var subMachineState = ref GetCurrentBlob(rootBlob, stack)
        .States[subMachineIndex];
    ref var subMachineBlob = ref GetCurrentBlob(rootBlob, stack)
        .SubStateMachines[subMachineState.TypeIndex];

    // Push new context
    stack.Depth++;
    Assert.IsTrue(stack.Depth < stack.MaxDepth, "State machine nesting too deep");

    stack.Contexts[stack.Depth] = new StateMachineContext
    {
        CurrentStateIndex = subMachineBlob.EntryStateIndex,
        ParentSubMachineIndex = subMachineIndex,
        Level = stack.Depth
    };

    // Transition to entry state
    TransitionToState(subMachineBlob.EntryStateIndex, ref stack, ref rootBlob);
}

void ExitCurrentSubStateMachine(
    ref StateMachineStack stack,
    ref StateMachineBlob rootBlob)
{
    if (stack.Depth == 0)
    {
        // Can't exit root state machine
        return;
    }

    // Get parent sub-machine blob
    var parentContext = stack.Contexts[stack.Depth - 1];
    ref var parentBlob = ref GetStateMachineBlobAtDepth(rootBlob, stack.Depth - 1);
    ref var parentState = ref parentBlob.States[parentContext.CurrentStateIndex];
    ref var subMachineBlob = ref parentBlob.SubStateMachines[parentState.TypeIndex];

    // Pop context
    stack.Depth--;

    // Evaluate exit transitions
    if (EvaluateExitTransitions(subMachineBlob.ExitTransitions, out var transitionIndex))
    {
        HandleTransition(transitionIndex, ref stack, ref rootBlob);
    }
}
```

### 5.2 State Machine Blob Lookup

```csharp
ref StateMachineBlob GetStateMachineBlobAtDepth(
    ref StateMachineBlob rootBlob,
    int depth)
{
    if (depth == 0)
        return ref rootBlob;

    // Traverse down the hierarchy
    ref var currentBlob = ref rootBlob;
    for (int i = 1; i <= depth; i++)
    {
        var context = stack.Contexts[i];
        var parentContext = stack.Contexts[i - 1];

        ref var parentState = ref currentBlob.States[parentContext.CurrentStateIndex];
        ref var subMachine = ref currentBlob.SubStateMachines[parentState.TypeIndex];

        currentBlob = ref subMachine.NestedStateMachine;
    }

    return ref currentBlob;
}
```

---

## 6. Bridge Translation

### 6.1 Pure 1:1 Translation

Following the Any State pattern, the bridge performs **pure translation**:

```csharp
// In UnityControllerAdapter.ReadStateMachine():

private StateMachineData ReadStateMachine(AnimatorStateMachine unityMachine)
{
    var data = new StateMachineData();

    // Read regular states
    foreach (var childState in unityMachine.states)
    {
        var state = ReadState(childState.state);
        data.States.Add(state);
    }

    // NEW: Read sub-state machines (no flattening!)
    foreach (var childStateMachine in unityMachine.stateMachines)
    {
        var subMachine = ReadSubStateMachine(childStateMachine);
        data.SubStateMachines.Add(subMachine);
    }

    // Read Any State transitions
    data.AnyStateTransitions = ReadAnyStateTransitions(unityMachine);

    return data;
}

private SubStateMachineData ReadSubStateMachine(
    ChildAnimatorStateMachine unitySubMachine)
{
    var data = new SubStateMachineData
    {
        Name = unitySubMachine.stateMachine.name,
        Position = unitySubMachine.position,

        // Recursively read nested machine
        NestedMachine = ReadStateMachine(unitySubMachine.stateMachine),

        // Resolve entry state
        EntryState = unitySubMachine.stateMachine.defaultState?.name,

        // Read exit transitions
        ExitTransitions = ReadExitTransitions(unitySubMachine.stateMachine)
    };

    return data;
}
```

**No flattening, no name mangling, pure 1:1 structure preservation!**

---

## 7. Editor Support

### 7.1 Visual Nesting

The visual editor supports hierarchical navigation:

1. **Top-Level View**: Shows states and sub-machine nodes
2. **Sub-Machine Node**: Double-click to "dive in"
3. **Breadcrumb Navigation**: `Root > Combat > [current]`
4. **Entry/Exit Indicators**: Visual markers for entry state and exit transitions

```
Graph View (Root Level):
┌─────────────────────────────┐
│  Idle                       │
│  [Single Clip]              │
└─────────────────────────────┘
           ↓
┌─────────────────────────────┐
│  Combat                     │
│  [Sub-State Machine] 📁     │  ← Double-click to open
└─────────────────────────────┘

Graph View (Combat Sub-Machine):
Breadcrumb: Root > Combat

┌─────────────────────────────┐
│  ⭐ LightAttack (Entry)      │
└─────────────────────────────┘
           ↓
┌─────────────────────────────┐
│  HeavyAttack                │
└─────────────────────────────┘
           ↓
┌─────────────────────────────┐
│  🚪 Exit                     │
└─────────────────────────────┘
```

### 7.2 SubStateMachineNodeView (NEW)

```csharp
internal class SubStateMachineNodeView : StateNodeView<SubStateMachineAsset>
{
    public SubStateMachineNodeView(VisualTreeAsset asset) : base(asset)
    {
        // Special styling for sub-machines
        AddToClassList("substatemachine");

        // Add "open" button
        var openButton = new Button(() => OpenSubMachine())
        {
            text = "Open ►"
        };
        mainContainer.Add(openButton);
    }

    private void OpenSubMachine()
    {
        // Navigate editor to show nested state machine
        var subMachine = (State as SubStateMachineAsset).NestedStateMachine;
        ParentView.NavigateToSubMachine(subMachine);
    }
}
```

---

## 8. Performance Considerations

### 8.1 Stack Depth Limit

**Max Depth**: 8 levels of nesting

**Rationale**:
- Unity typically uses 1-2 levels max
- 8 levels is more than sufficient
- Fixed-size stack avoids allocations
- `FixedList64Bytes<StateMachineContext>` fits in 64 bytes

**Validation**:
```csharp
// At baking time:
int depth = stateMachine.GetNestingDepth();
Assert.IsTrue(depth <= 8, $"State machine nesting too deep: {depth} levels (max 8)");
```

### 8.2 Blob Structure Overhead

**Memory Cost per Sub-Machine**:
- `SubStateMachineBlob`: ~16 bytes base
- `StateMachineBlob`: ~48 bytes for nested machine
- Total: ~64 bytes per sub-machine

**Example**:
- 100 states, 10 sub-machines = 640 bytes overhead
- Negligible compared to animation data

### 8.3 Runtime Performance

**Transition Evaluation**:
- No performance penalty for flat states
- Sub-machine entry: 1 stack push (~10 instructions)
- Sub-machine exit: 1 stack pop (~10 instructions)
- Hierarchical lookup: O(depth) pointer dereferences

**Expected Impact**: <1% performance difference for typical use cases

---

## 9. Comparison: Workaround vs Native

### 9.1 Feature Matrix

| Feature | Flattening Workaround | Native Support |
|---------|----------------------|----------------|
| **Structure Preservation** | ❌ Lost | ✅ Preserved |
| **Name Collisions** | ⚠️ Possible | ✅ None |
| **Visual Editor** | ❌ Flat list | ✅ Hierarchical |
| **Debugging** | ❌ Difficult | ✅ Easy (path tracking) |
| **Exit Semantics** | ⚠️ Resolved at conversion | ✅ Runtime evaluation |
| **Unity Parity** | ⚠️ Functional only | ✅ Full parity |
| **Implementation Time** | ✅ 1-2 days | ⚠️ 1-2 weeks |
| **DMotion Changes** | ✅ None | ❌ Major |
| **Technical Debt** | ❌ Workaround | ✅ Clean |
| **Future Extensions** | ❌ Limited | ✅ Unlimited |

### 9.2 Recommendation

**Choose Native Support** because:

1. ✅ **Proven Pattern**: Any State native implementation was highly successful
2. ✅ **No Technical Debt**: Clean architecture, no workarounds
3. ✅ **Better UX**: Visual nesting, proper debugging
4. ✅ **Future-Proof**: Enables hierarchy-aware features
5. ✅ **Unity Parity**: Full semantic equivalence

**Trade-off**: Higher upfront cost (1-2 weeks vs 1-2 days)

**ROI**: Long-term maintainability and user satisfaction far outweigh implementation time

---

## 10. Implementation Roadmap

### Phase 1: Runtime Foundation (3-4 days)
1. Add `SubStateMachineBlob` structure
2. Add `StateMachineStack` component
3. Modify `AnimationStateBlob` to support sub-machines
4. Implement stack management (push/pop)
5. Add hierarchical blob lookup

### Phase 2: Evaluation Logic (2-3 days)
1. Implement `EnterSubStateMachine()`
2. Implement `ExitSubStateMachine()`
3. Modify `EvaluateTransitions()` for hierarchy
4. Add entry state resolution
5. Add exit transition evaluation

### Phase 3: Authoring & Baking (2-3 days)
1. Create `SubStateMachineAsset` class
2. Modify `StateMachineAsset` to support nesting
3. Add hierarchy validation
4. Implement `SubStateMachineBlobConverter`
5. Update baking pipeline

### Phase 4: Bridge Translation (2 days)
1. Modify `UnityControllerAdapter` to read sub-machines
2. Add `ReadSubStateMachine()` method
3. Implement entry/exit resolution
4. Update `ConversionEngine` for hierarchy
5. Update `DMotionAssetBuilder` to create sub-assets

### Phase 5: Editor Support (3-4 days)
1. Create `SubStateMachineNodeView`
2. Add breadcrumb navigation
3. Implement "dive in/out" navigation
4. Add entry/exit indicators
5. Update inspector for sub-machines

### Phase 6: Testing & Validation (2-3 days)
1. Unit tests for runtime evaluation
2. Integration tests for Unity conversion
3. Manual testing with complex hierarchies
4. Performance benchmarking
5. Documentation

**Total Estimate**: 14-19 days (~3 weeks)

---

## 11. Success Criteria

### Must Have ✅
1. ✅ Sub-state machines can contain states and other sub-machines
2. ✅ Entry state resolution works correctly
3. ✅ Exit transitions trigger parent-level transitions
4. ✅ Stack depth up to 8 levels supported
5. ✅ Unity → DMotion conversion preserves hierarchy (1:1)
6. ✅ Visual editor shows hierarchical structure
7. ✅ Breadcrumb navigation works
8. ✅ No name mangling or flattening
9. ✅ Runtime performance within 5% of flat structure
10. ✅ Zero technical debt

### Nice to Have 🎯
1. 🎯 Play mode debugging shows current hierarchy path
2. 🎯 Visual diff tool for Unity vs DMotion hierarchy
3. 🎯 Automatic cycle detection (circular sub-machines)
4. 🎯 Export hierarchy as text (for debugging)
5. 🎯 Performance profiler integration

---

## 12. Risks & Mitigations

### Risk 1: Complexity
**Concern**: Hierarchical evaluation is complex
**Mitigation**:
- Start with simple cases (1 level nesting)
- Extensive unit testing at each layer
- Reference Unity's behavior closely

### Risk 2: Performance
**Concern**: Stack traversal may be slow
**Mitigation**:
- Benchmark early and often
- Optimize hot paths with Burst
- Limit max depth to 8 levels

### Risk 3: Edge Cases
**Concern**: Complex transition scenarios (Any State + Exit + Hierarchy)
**Mitigation**:
- Define clear evaluation order
- Document edge case behavior
- Add validation and assertions

### Risk 4: Editor Complexity
**Concern**: Visual editor navigation may be confusing
**Mitigation**:
- Clear breadcrumb navigation
- Intuitive "dive in/out" UX
- User testing with real projects

---

## 13. Conclusion

### Why Native Sub-State Machines?

**The Any State Success**: We proved that native DMotion features are:
- ✅ Cleaner than workarounds
- ✅ More maintainable
- ✅ Better for users
- ✅ Worth the upfront investment

**Sub-State Machines Are Critical**:
- Used in 80%+ of Unity animator controllers
- Essential for organization
- Enable complex character systems

**The DMotion-First Philosophy**:
> "The bridge should be a pure translation layer, not a workaround generator"

By implementing native sub-state machine support, we:
1. Maintain architectural integrity
2. Provide full Unity parity
3. Enable future hierarchy-aware features
4. Deliver superior user experience

**Decision**: Proceed with native implementation following the Any State pattern.

---

## Related Documents

- [Any State Implementation Summary](AnyState_Complete_Implementation_Summary.md) - Reference for DMotion-First pattern
- [Action Plan Phase 12.5](UnityControllerBridge_ActionPlan.md#125-sub-state-machine-flattening) - Original workaround plan
- [Unity Animation Documentation](https://docs.unity3d.com/Manual/StateMachineBasics.html) - Unity's sub-state machine docs

---

*Document Status*: ✅ Complete - Ready for implementation planning

*Next Step*: Create DMotion-First implementation strategy document
