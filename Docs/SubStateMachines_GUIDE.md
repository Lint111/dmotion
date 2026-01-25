# Sub-State Machines Guide

## What Are Sub-State Machines?

**Sub-state machines** allow you to organize complex animation logic hierarchically by nesting state machines within states. This is useful for:

- **Organizing related states**: Group combat moves, locomotion variants, or interaction animations
- **Reusable state groups**: Create modular behavior that can be used in multiple places
- **Cleaner graphs**: Reduce visual clutter by collapsing detailed logic into sub-machines

### Example: Character Controller
```
Root State Machine:
├─ Idle
├─ Locomotion (Sub-Machine)
│  ├─ Walk
│  ├─ Run
│  └─ Sprint
├─ Combat (Sub-Machine)
│  ├─ Attack
│  ├─ Block
│  └─ Dodge
└─ Death
```

---

## Creating Sub-State Machines

### Via Unity AnimatorController (Recommended)

1. Open your AnimatorController
2. Right-click > **Create Sub-State Machine**
3. Double-click to enter and add states
4. Convert via **Mechination** - SubStateMachines are preserved

### Via DMotion Editor

1. Open the State Machine Editor
2. Right-click in graph > **New Sub-State Machine**
3. Configure:
   - **Name**: SubStateMachine name
   - **Create New Nested Machine**: Auto-create a new StateMachineAsset
4. Double-click to enter the nested machine

---

## Graph Navigation

| Action | Result |
|--------|--------|
| Double-click SubStateMachine | Enter nested machine |
| Click breadcrumb segment | Navigate to that level |
| Double-click state title | Rename state |
| Right-click drag state→state | Create transition |
| Right-click drag across edges | Delete transitions (cut gesture) |

### Breadcrumb Bar

Visual path showing current location: `Root > Combat > Special`

---

## Exit Transitions

Exit transitions allow states within a SubStateMachine to trigger transitions back to the parent level.

### Configuring Exit Transitions

1. **Mark Exit States**: Drag a transition from a state TO the **Exit** node
2. **Add Exit Transitions**: In SubStateMachine inspector, add OutTransitions with target states
3. **Visual Feedback**: Orange edges show exit transitions in the graph

### Example

```
Combat (SubStateMachine)
├─ Attack (entry)
├─ Block
└─ CombatIdle (exit state)

Exit Transitions:
  CombatIdle → Idle (when "InCombat" = false)
```

### Transition Priority

1. **Any State transitions** (highest)
2. **Regular state transitions**
3. **Exit transitions** (if current state is exit state)

---

## Runtime Behavior

### Visual-Only Flattening

SubStateMachines are **flattened** at bake time (like Unity Mecanim):

```
Editor Structure:              Runtime Blob:
Root                           States[0] = Idle
├─ Idle                        States[1] = Walk
├─ Locomotion (Sub)            States[2] = Run
│  ├─ Walk                     States[3] = Attack
│  └─ Run                      States[4] = Block
└─ Combat (Sub)
   ├─ Attack
   └─ Block
```

**Benefits:**
- No runtime stack overhead
- O(1) state lookup
- Efficient memory (blob is shared)

---

## Best Practices

### When to Use

**Good:**
- Grouping 5+ related states
- Reusable behavior modules
- Simplifying visual complexity

**Avoid:**
- Single-state sub-machines
- Very shallow hierarchies (2-3 states)
- Excessive nesting (>5 levels)

### Parameter Organization

Parameters are **shared** across all levels:
```
✅ Good: "InCombat" (root level)
❌ Bad: "Combat_AttackType" (implied nesting)
```

---

## API Reference

### StateMachineAsset Hierarchy APIs

#### GetAllLeafStates()

```csharp
public IEnumerable<StateWithPath> GetAllLeafStates()
```

Gets all leaf states with their hierarchical paths.

```csharp
foreach (var stateWithPath in stateMachine.GetAllLeafStates())
{
    Debug.Log($"State: {stateWithPath.State.name}, Path: {stateWithPath.Path}");
}
// Output: "State: Walk, Path: Locomotion/Walk"
```

#### GetStatePath()

```csharp
public string GetStatePath(AnimationStateAsset state)
```

Gets the hierarchical path for a state (e.g., "Combat/Attack/Slash").

#### GetStatesInGroup()

```csharp
public IEnumerable<AnimationStateAsset> GetStatesInGroup(SubStateMachineStateAsset group)
```

Gets all leaf states belonging to a specific SubStateMachine group.

#### FindStatesByPath()

```csharp
public IEnumerable<AnimationStateAsset> FindStatesByPath(string pattern)
```

Finds states matching a path pattern with wildcard support:
- `*` - Matches single path segment
- `**` - Matches any number of segments

```csharp
stateMachine.FindStatesByPath("Combat/*");      // Direct children of Combat
stateMachine.FindStatesByPath("**/Attack");     // Any "Attack" state
stateMachine.FindStatesByPath("Combat/**");     // All states under Combat
```

#### GetAllGroups() / GetRootGroups()

```csharp
public IEnumerable<SubStateMachineStateAsset> GetAllGroups()
public IEnumerable<SubStateMachineStateAsset> GetRootGroups()
```

Gets all SubStateMachine groups, or just root-level groups.

### SubStateMachineStateAsset

| Property | Type | Description |
|----------|------|-------------|
| `NestedStateMachine` | `StateMachineAsset` | The nested state machine |
| `EntryState` | `AnimationStateAsset` | Entry state when entering |
| `ExitStates` | `List<AnimationStateAsset>` | States that can trigger exits |

#### IsValid()

```csharp
public bool IsValid()
```

Returns `true` if NestedStateMachine and EntryState are properly configured.

### StateWithPath Struct

```csharp
public readonly struct StateWithPath
{
    public readonly AnimationStateAsset State;    // The leaf state
    public readonly string Path;                   // Hierarchical path
    public readonly SubStateMachineStateAsset ParentGroup;  // Parent group or null
}
```

---

## FAQ

**Q: What's the maximum nesting depth?**
A: Unlimited. The system uses relationship-based connections.

**Q: Do parameters work in nested machines?**
A: Yes. Parameters are shared across all levels.

**Q: Can I nest sub-machines within sub-machines?**
A: Yes. Recursion is fully supported.

**Q: Is there performance overhead?**
A: Minimal. Flattening eliminates runtime hierarchy traversal.

---

## See Also

- [StateMachineEditor_GUIDE.md](StateMachineEditor_GUIDE.md) - Editor usage
- [DMotion_ECS_API_Guide.md](DMotion_ECS_API_Guide.md) - Runtime ECS API
