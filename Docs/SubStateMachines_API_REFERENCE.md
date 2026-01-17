# Sub-State Machines API Reference

This document provides a comprehensive API reference for DMotion's SubStateMachine feature.

---

## Table of Contents

1. [StateMachineAsset Hierarchy APIs](#statemachineasset-hierarchy-apis)
2. [SubStateMachineStateAsset](#substatemachinestateasset)
3. [StateWithPath Struct](#statewithpath-struct)
4. [StateFlattener (Internal)](#stateflattener-internal)
5. [Runtime Components](#runtime-components)

---

## StateMachineAsset Hierarchy APIs

The `StateMachineAsset` class provides methods for querying the visual hierarchy of states. These APIs work at editor/authoring time - at runtime, all states are flattened.

### GetAllLeafStates()

```csharp
public IEnumerable<StateWithPath> GetAllLeafStates()
```

Gets all leaf states (SingleClipState or LinearBlendState) with their hierarchical paths. SubStateMachines are traversed recursively - their nested states are included with full paths.

**Returns:** `IEnumerable<StateWithPath>` - All leaf states with path information.

**Example:**
```csharp
var stateMachine = GetComponent<StateMachineAuthoring>().StateMachineAsset;
foreach (var stateWithPath in stateMachine.GetAllLeafStates())
{
    Debug.Log($"State: {stateWithPath.State.name}, Path: {stateWithPath.Path}");
}
// Output:
// State: Idle, Path: Idle
// State: Walk, Path: Locomotion/Walk
// State: Run, Path: Locomotion/Run
// State: Slash, Path: Combat/Attack/Slash
```

---

### GetStatesInGroup()

```csharp
public IEnumerable<AnimationStateAsset> GetStatesInGroup(SubStateMachineStateAsset group)
```

Gets all leaf states belonging to a specific SubStateMachine group. Includes states in nested SubStateMachines within the group.

**Parameters:**
- `group` - The SubStateMachine group to query.

**Returns:** `IEnumerable<AnimationStateAsset>` - All leaf states in the group.

**Example:**
```csharp
var combatGroup = stateMachine.GetRootGroups().First(g => g.name == "Combat");
foreach (var state in stateMachine.GetStatesInGroup(combatGroup))
{
    Debug.Log($"Combat state: {state.name}");
}
// Output: Attack, Block, Dodge (if these are leaf states in Combat)
```

---

### GetStatePath()

```csharp
public string GetStatePath(AnimationStateAsset state)
```

Gets the hierarchical path for a specific state (e.g., "Combat/Attack/Slash").

**Parameters:**
- `state` - The state to find the path for.

**Returns:** `string` - The full path, or `null` if the state is not found.

**Example:**
```csharp
var slashState = FindState("Slash");
var path = stateMachine.GetStatePath(slashState);
Debug.Log(path); // "Combat/Attack/Slash"
```

---

### GetParentGroup()

```csharp
public SubStateMachineStateAsset GetParentGroup(AnimationStateAsset state)
```

Gets the parent SubStateMachine group for a state.

**Parameters:**
- `state` - The state to find the parent for.

**Returns:** `SubStateMachineStateAsset` - The parent group, or `null` if the state is at root level or not found.

**Example:**
```csharp
var slashState = FindState("Slash");
var parent = stateMachine.GetParentGroup(slashState);
Debug.Log(parent?.name); // "Attack"
```

---

### FindStatesByPath()

```csharp
public IEnumerable<AnimationStateAsset> FindStatesByPath(string pattern)
```

Finds states matching a path pattern with wildcard support.

**Parameters:**
- `pattern` - Path pattern with optional wildcards.

**Wildcards:**
- `*` - Matches any single path segment (no slashes)
- `**` - Matches any number of segments (including slashes)

**Returns:** `IEnumerable<AnimationStateAsset>` - States matching the pattern.

**Examples:**
```csharp
// All states in Combat group (direct children only)
stateMachine.FindStatesByPath("Combat/*");

// All "Attack" states anywhere in hierarchy
stateMachine.FindStatesByPath("**/Attack");

// All states in Attack group at any depth
stateMachine.FindStatesByPath("Combat/Attack/**");

// All states named "Slash" at second level
stateMachine.FindStatesByPath("*/*/Slash");
```

---

### GetAllGroups()

```csharp
public IEnumerable<SubStateMachineStateAsset> GetAllGroups()
```

Gets all SubStateMachine groups in the hierarchy (depth-first order).

**Returns:** `IEnumerable<SubStateMachineStateAsset>` - All SubStateMachine groups.

**Example:**
```csharp
foreach (var group in stateMachine.GetAllGroups())
{
    Debug.Log($"Group: {group.name}");
}
// Output: Locomotion, Combat, Attack (depth-first)
```

---

### GetRootGroups()

```csharp
public IEnumerable<SubStateMachineStateAsset> GetRootGroups()
```

Gets direct child groups (SubStateMachines) at the root level only.

**Returns:** `IEnumerable<SubStateMachineStateAsset>` - Root-level SubStateMachine groups.

**Example:**
```csharp
foreach (var group in stateMachine.GetRootGroups())
{
    Debug.Log($"Root group: {group.name}");
}
// Output: Locomotion, Combat (only top-level groups)
```

---

### GetGroupHierarchy()

```csharp
public IEnumerable<(SubStateMachineStateAsset group, int depth)> GetGroupHierarchy()
```

Gets the group hierarchy as a tree structure for visualization.

**Returns:** Tuples of (group, depth) for tree visualization.

**Example:**
```csharp
foreach (var (group, depth) in stateMachine.GetGroupHierarchy())
{
    var indent = new string(' ', depth * 2);
    Debug.Log($"{indent}{group.name}");
}
// Output:
// Locomotion
// Combat
//   Attack
```

---

## SubStateMachineStateAsset

A visual grouping container for states. Flattened at conversion time - no runtime overhead.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `NestedStateMachine` | `StateMachineAsset` | The nested state machine containing child states |
| `EntryState` | `AnimationStateAsset` | The entry state when transitioning into this group |
| `ExitStates` | `List<AnimationStateAsset>` | States that can trigger exit transitions |
| `ExitTransitions` | `List<StateOutTransition>` | Transitions to evaluate when exiting |

### Methods

#### IsValid()

```csharp
public bool IsValid()
```

Validates that the nested machine and entry state are properly configured.

**Returns:** `true` if:
- `NestedStateMachine` is not null
- `EntryState` is not null  
- `EntryState` is contained in `NestedStateMachine.States`

**Example:**
```csharp
if (!subStateMachine.IsValid())
{
    Debug.LogWarning("SubStateMachine is not properly configured");
}
```

### Inherited Properties

From `AnimationStateAsset`:

| Property | Type | Description |
|----------|------|-------------|
| `Loop` | `bool` | Whether animations in this group should loop |
| `Speed` | `AnimationSpeed` | Speed configuration for animations |
| `OutTransitions` | `List<StateOutTransition>` | Transitions from this group (to sibling states) |

### Type Property

```csharp
public override StateType Type => throw new InvalidOperationException(...)
```

**Important:** The `Type` property throws an exception because SubStateMachines are flattened during conversion. Never access this property - use `StateFlattener` to get leaf states.

---

## StateWithPath Struct

Represents a leaf state with its hierarchical path information.

```csharp
public readonly struct StateWithPath
{
    public readonly AnimationStateAsset State;
    public readonly string Path;
    public readonly SubStateMachineStateAsset ParentGroup;
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `State` | `AnimationStateAsset` | The leaf state (Single or LinearBlend) |
| `Path` | `string` | Hierarchical path (e.g., "Combat/Attack/Slash") |
| `ParentGroup` | `SubStateMachineStateAsset` | Immediate parent group, or null if at root |

---

## StateFlattener (Internal)

The `StateFlattener` class converts hierarchical state structures to flat arrays for runtime efficiency. This is internal API but documented for extension purposes.

### Key Types

#### FlattenedState

```csharp
public struct FlattenedState
{
    public AnimationStateAsset Asset;
    public int GlobalIndex;
    public int ClipIndexOffset;
    public string Path;
    public int ExitGroupIndex;
    public bool IsExitState;
}
```

#### FlattenResult

```csharp
public class FlattenResult
{
    public List<FlattenedState> FlattenedStates;
    public Dictionary<AnimationStateAsset, int> AssetToGlobalIndex;
    public List<ExitTransitionGroup> ExitGroups;
}
```

### Usage

```csharp
// Internal usage during blob conversion
var result = StateFlattener.FlattenStates(stateMachineAsset);
foreach (var state in result.FlattenedStates)
{
    Debug.Log($"[{state.GlobalIndex}] {state.Path} (Exit: {state.IsExitState})");
}
```

---

## Runtime Components

### AnimationStateBlob

Per-state runtime data in the blob.

```csharp
internal struct AnimationStateBlob
{
    internal StateType Type;
    internal short StateIndex;
    internal BlobArray<StateOutTransitionGroup> Transitions;
    internal short ExitTransitionGroupIndex;  // -1 if not an exit state
    // ... other fields
}
```

### ExitTransitionGroup

Runtime storage for exit transitions.

```csharp
public struct ExitTransitionGroup
{
    internal short SubMachineIndex;
    internal BlobArray<short> ExitStateIndices;
    internal BlobArray<StateOutTransitionBlob> ExitTransitions;
}
```

### Transition Index Encoding

In `UpdateStateMachineJob`, transition indices are encoded as:
- **Negative indices (-1, -2, ...):** Any State transitions
- **0-999:** Regular state transitions
- **1000+:** Exit transitions (value - 1000 = exit transition index)

---

## Editor Components

### SubStateMachineStateAssetEditor

Custom inspector for `SubStateMachineStateAsset` providing:
- Entry state dropdown picker
- Exit state list with add/remove
- Validation warnings

### ExitTransitionEdge

Graph view edge for exit transitions with:
- Orange color to distinguish from regular transitions
- Click-to-select functionality
- Proper hit testing

---

## See Also

- [SubStateMachines_QUICKSTART.md](SubStateMachines_QUICKSTART.md) - Getting started guide
- [SubStateMachines_USER_GUIDE.md](SubStateMachines_USER_GUIDE.md) - Detailed usage guide
- [SubStateMachines_ArchitectureAnalysis.md](SubStateMachines_ArchitectureAnalysis.md) - Design rationale
