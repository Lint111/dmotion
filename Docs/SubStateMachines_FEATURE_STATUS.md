# Sub-State Machines Feature Status

**Last Updated:** 2026-01-17
**Status:** Feature Complete

## Executive Summary

SubStateMachine support in DMotion follows Unity Mecanim's "visual-only" flattening pattern:
- **Editor Time:** Hierarchical `SubStateMachineStateAsset` with `NestedStateMachine`
- **Runtime:** Flat `StateMachineBlob` via `StateFlattener` - no nested blob structures

All core functionality is complete: runtime flattening, exit transition evaluation, authoring APIs, Editor UI with custom inspector and graph view exit transitions, and Mechination integration.

---

## Feature Completeness Matrix

| Area | Status | Notes |
|------|--------|-------|
| **Runtime** | ✅ 100% | Flat blob, no stack, efficient |
| **Authoring** | ✅ 100% | Hierarchy APIs, entry/exit state storage |
| **StateFlattener** | ✅ 100% | Recursive flattening with global indices |
| **BlobConverter** | ✅ 100% | Builds exit transition groups |
| **UpdateStateMachineJob** | ✅ 100% | Exit transitions fully evaluated at runtime |
| **Mechination** | ✅ 100% | Conversion and asset building complete |
| **Unit Tests** | ✅ 100% | 43+ tests passing |
| **Editor UI** | ✅ 100% | Custom inspector with exit state picker, orange exit transition edges |
| **Documentation** | ✅ 100% | Quickstart, API reference, and user guide complete |

---

## Detailed Status by Component

### 1. Runtime (`Runtime/`)

#### StateMachineBlob.cs ✅
```csharp
// Exit transition groups fully defined
internal BlobArray<ExitTransitionGroup> ExitTransitionGroups;

public struct ExitTransitionGroup
{
    internal short SubMachineIndex;
    internal BlobArray<short> ExitStateIndices;
    internal BlobArray<StateOutTransitionBlob> ExitTransitions;
}
```

#### AnimationStateBlob.cs ✅
```csharp
// Exit group index added
internal short ExitTransitionGroupIndex;  // -1 if not an exit state
```

#### UpdateStateMachineJob.cs ✅
- Normal transitions: ✅ Working
- Any-state transitions: ✅ Working  
- Exit transitions: ✅ **Fully implemented**

Exit transition evaluation is implemented with proper index encoding:
- Negative indices: Any State transitions
- 0-999: Regular state transitions
- 1000+: Exit transitions (encoded as ExitTransitionIndexOffset + index)

### 2. Authoring (`Runtime/Authoring/`)

#### SubStateMachineStateAsset.cs ✅
```csharp
public class SubStateMachineStateAsset : AnimationStateAsset
{
    public StateMachineAsset NestedStateMachine;
    public AnimationStateAsset EntryState;
    public List<AnimationStateAsset> ExitStates = new();
    // Transitions stored at parent level, not on this asset
}
```

#### StateMachineAsset.cs ✅
Hierarchy query APIs complete:
- `GetAllLeafStates()` - Returns `StateWithPath` with global indices
- `GetStatesInGroup(SubStateMachineStateAsset)` - Filter by group
- `GetStatePath(AnimationStateAsset)` - Hierarchical path string
- `FindStatesByPath(string pattern)` - Wildcard matching
- `GetAllGroups()` - All SubStateMachine containers

### 3. Conversion (`Runtime/Authoring/Conversion/`)

#### StateFlattener.cs ✅
```csharp
public struct FlattenedState
{
    public AnimationStateAsset Asset;
    public int GlobalIndex;
    public int ClipIndexOffset;
    public string Path;
    public int ExitGroupIndex;      // Which group this can exit from
    public bool IsExitState;         // Can trigger exit transitions
}

public class FlattenResult
{
    public List<FlattenedState> FlattenedStates;
    public Dictionary<AnimationStateAsset, int> AssetToGlobalIndex;
    public List<ExitTransitionGroup> ExitGroups;
}
```

#### StateMachineBlobConverter.cs ✅
- Builds flat state array from `FlattenResult`
- Creates `ExitTransitionGroup` blob array
- Links exit state indices to groups

### 4. Mechination Integration ✅

#### DMotionAssetBuilder.cs
- Creates `SubStateMachineStateAsset` from `ConvertedState`
- Recursively builds nested machine assets
- Populates `ExitStates` from `ExitStateNames`
- All sub-assets added to root asset

#### UnityControllerConverter.cs
- Reads Unity sub-state machines recursively
- Identifies exit states from transitions
- Links exit transitions in second pass

### 5. Tests ✅

| Test File | Tests | Status |
|-----------|-------|--------|
| `StateFlattenerShould.cs` | 12 | ✅ Pass |
| `SubStateMachineStateAssetShould.cs` | 8 | ✅ Pass |
| `SubStateMachineIntegrationTests.cs` | 6 | ✅ Pass |
| `SubStateMachineConversionShould.cs` | 7 | ✅ Pass |
| Mechination `BridgeTests.cs` | 10 | ✅ Pass |

**Total: 43+ tests, all passing**

### 6. Editor UI ✅

#### Custom Inspector (`SubStateMachineStateAssetEditor.cs`)
- **Entry state picker**: Dropdown to select entry state from nested machine
- **Exit state picker**: "Add Exit State" dropdown with remove buttons
- **Validation warnings**: Visual warnings for missing entry/exit states
- **Performance caching**: Cached state arrays for smooth UI

#### Graph View Exit Transitions (`ExitTransitionEdge.cs`)
- **Orange colored edges**: Distinct from normal (white) transitions
- **Exit transition edges**: Drawn from SubStateMachine to target states
- **Click to select**: Shows SubStateMachine inspector on click

#### Validation (`SubStateMachineStateAsset.OnValidate()`)
- Auto-sets entry state to default if not set
- Warns if entry state not in nested machine
- Warns if exit states not in nested machine

### 7. Documentation ⚠️

| Document | Status | Notes |
|----------|--------|-------|
| `SubStateMachines_QUICKSTART.md` | ✅ Done | Basic usage |
| `SubStateMachines_USER_GUIDE.md` | ✅ Done | Exit transitions, testing guide included |
| `SubStateMachines_TESTING.md` | ✅ Done | Test patterns |
| `SubStateMachines_ArchitectureAnalysis.md` | ✅ Done | Design rationale |
| `SubStateMachines_ExitTransitions_Plan.md` | ✅ Done | Implementation plan |
| `SubStateMachines_API_REFERENCE.md` | ✅ Done | Complete API documentation |

---

## What's Left To Do

**Nothing!** The SubStateMachine feature is complete.

### Optional Future Enhancements

These are nice-to-have features that could be added later:

1. **Nested Graph Navigation** - Double-click SubStateMachine to enter
2. **Breadcrumb UI** - Show current path in graph editor
3. **Exit Transition Tooltip** - Show conditions on hover

---

## Architecture Notes

### Why Visual-Only Flattening?

1. **Performance:** Single flat array traversal at runtime
2. **Simplicity:** No stack management, no nested blob lookups
3. **Memory:** Single allocation for entire state machine
4. **Compatibility:** Matches Unity Mecanim mental model

### Exit Transition Encoding

In `UpdateStateMachineJob`, transition indices use ranges:
- **Negative indices:** Any State transitions
- **0-999:** Regular state transitions
- **1000+:** Exit transitions (subtract 1000, lookup in group)

### Blob Structure After Flattening

```
StateMachineBlob
├─ States[0..N]           // All leaf states, globally indexed
├─ SingleClipStates[]     // Indexed by StateBlob.StateIndex
├─ LinearBlendStates[]    // Indexed by StateBlob.StateIndex
├─ AnyStateTransitions[]  // Shared across all states
└─ ExitTransitionGroups[] // Indexed by StateBlob.ExitTransitionGroupIndex
    └─ [0] Group "Combat"
        ├─ ExitStateIndices[] = [5, 7]  // Global indices
        └─ ExitTransitions[]   // Conditions + destination indices
```

---

## Test Coverage

### Unit Tests
- StateFlattener isolation tests
- Asset hierarchy query tests
- Blob builder tests

### Integration Tests
- Full conversion pipeline (Mechination → DMotion)
- Round-trip: Create asset → Flatten → Verify indices
- Nested sub-machine (2+ levels deep)

### Runtime Tests
- Exit transition evaluation: ✅ Tested via integration tests
- Exit transition with conditions: ✅ Tested in SubStateMachineConversionShould
- Multi-exit state scenarios: ✅ Tested in SubStateMachineIntegrationTests

---

## Related Files Reference

### DMotion
| File | Purpose |
|------|---------|
| `Runtime/Components/StateMachineBlob.cs` | Runtime blob structures |
| `Runtime/Components/AnimationStateBlob.cs` | Per-state blob data |
| `Runtime/Authoring/Conversion/StateFlattener.cs` | Hierarchy → flat |
| `Runtime/Authoring/Conversion/StateMachineBlobConverter.cs` | Blob builder |
| `Runtime/Systems/UpdateStateMachineJob.cs` | Runtime evaluation (with exit transitions) |
| `Runtime/Authoring/AnimationStateMachine/SubStateMachineStateAsset.cs` | Authoring asset |
| `Runtime/Authoring/AnimationStateMachine/StateMachineAsset.cs` | Root + hierarchy APIs |
| `Editor/CustomEditors/SubStateMachineStateAssetEditor.cs` | Custom inspector with exit state picker |
| `Editor/EditorWindows/ExitTransitionEdge.cs` | Orange exit transition edge in graph view |
| `Editor/EditorWindows/ExitTransitionEdgeControl.cs` | Custom edge rendering for exit transitions |

### Mechination
| File | Purpose |
|------|---------|
| `Editor/Adapters/DMotionAssetBuilder.cs` | Creates DMotion assets |
| `Editor/Conversion/UnityControllerConverter.cs` | Unity → DMotion conversion |
| `Editor/Core/ConversionResult.cs` | Intermediate data model |

---

## Summary Checklist

### Complete ✅
- [x] StateFlattener with recursive flattening
- [x] Exit transition group storage in blob
- [x] ExitTransitionGroupIndex in AnimationStateBlob
- [x] SubStateMachineStateAsset with ExitStates list
- [x] StateMachineAsset hierarchy query APIs
- [x] StateMachineBlobConverter exit group building
- [x] Mechination conversion for sub-state machines
- [x] Unit tests (43+)
- [x] Quickstart documentation
- [x] Architecture documentation
- [x] Runtime exit transition evaluation in UpdateStateMachineJob
- [x] Exit state custom inspector (SubStateMachineStateAssetEditor)
- [x] Graph view exit transition drawing (orange ExitTransitionEdge)
- [x] Validation warnings in editor (OnValidate)

### All Complete ✅
- [x] API reference documentation (SubStateMachines_API_REFERENCE.md)
- [x] User guide exit transition section (SubStateMachines_USER_GUIDE.md)
