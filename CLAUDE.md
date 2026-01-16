# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DMotion is a high-performance animation state machine system for Unity DOTS (Data-Oriented Technology Stack). It provides a native ECS animation solution using Latios Framework's Kinemation for skeletal animation.

**Dependencies:** Unity 6000.0+, Latios Framework (Kinemation), Unity.Entities

## Related Repository: Mechination

**Location:** `C:\GitHub\mechination`

Mechination is the translation layer that converts Unity AnimatorControllers to DMotion StateMachineAssets. The relationship is:

```
Unity Mecanim (AnimatorController)
        │
        ▼ [Mechination - Translation Layer]
        │
        ▼
DMotion (StateMachineAsset) ──► Runtime (Flat ECS Blob)
```

**Responsibility Split:**
- **DMotion** = All capabilities, APIs, runtime behavior
- **Mechination** = Pure translation (Unity Mecanim → DMotion assets)

When modifying DMotion APIs, consider impact on mechination's `DMotionAssetBuilder.cs`.

## Architecture

### State Machine Hierarchy (Visual-Only)

SubStateMachines are **visual-only** (like Unity Mecanim):
- **Editor time:** Hierarchical structure in `SubStateMachineStateAsset.NestedStateMachine`
- **Runtime:** Flattened to single-level `StateMachineBlob` via `StateFlattener`

```
Authoring (Editor)              Runtime (Blob)
┌─────────────────┐            ┌─────────────┐
│ StateMachineAsset │            │ StateMachineBlob │
│  ├─ State A       │   ──►     │  States[0] = A   │
│  ├─ SubMachine B  │  Flatten  │  States[1] = C   │
│  │   └─ State C   │            │  States[2] = D   │
│  └─ State D       │            └─────────────┘
└─────────────────┘
```

### Hierarchy Query APIs (StateMachineAsset)

```csharp
// Get all leaf states with paths
IEnumerable<StateWithPath> GetAllLeafStates()

// Get states in a group
IEnumerable<AnimationStateAsset> GetStatesInGroup(SubStateMachineStateAsset group)

// Get state's hierarchical path (e.g., "Combat/Attack/Slash")
string GetStatePath(AnimationStateAsset state)

// Find states by path pattern ("Combat/*", "**/Slash")
IEnumerable<AnimationStateAsset> FindStatesByPath(string pattern)

// Get all SubStateMachine groups
IEnumerable<SubStateMachineStateAsset> GetAllGroups()
```

## Key Files

| Component | File | Purpose |
|-----------|------|---------|
| State Machine Blob | `Runtime/Components/StateMachineBlob.cs` | Runtime flat state data |
| State Flattener | `Runtime/Authoring/Conversion/StateFlattener.cs` | Hierarchy → flat conversion |
| Blob Converter | `Runtime/Authoring/Conversion/StateMachineBlobConverter.cs` | Builds runtime blob |
| Conversion Utils | `Runtime/Authoring/Conversion/AnimationStateMachineConversionUtils.cs` | Entity setup, blob creation |
| Update Job | `Runtime/Systems/UpdateStateMachineJob.cs` | Runtime state machine logic |
| SubStateMachine Asset | `Runtime/Authoring/AnimationStateMachine/SubStateMachineStateAsset.cs` | Visual hierarchy container |
| StateMachine Asset | `Runtime/Authoring/AnimationStateMachine/StateMachineAsset.cs` | Root asset + hierarchy APIs |

## Assembly Structure

- `DMotion` - Runtime components and systems
- `DMotion.Editor` - Editor tools and inspectors
- `DMotion.Tests.Runtime` - PlayMode tests
- `DMotion.Tests.Editor` - EditMode tests

## State Types

| Type | Runtime Enum | Description |
|------|--------------|-------------|
| SingleClipStateAsset | `StateType.Single` | Single animation clip |
| LinearBlendStateAsset | `StateType.LinearBlend` | 1D blend tree |
| SubStateMachineStateAsset | (flattened) | Visual grouping only |

## Conversion Flow

1. **Authoring:** `StateMachineAsset` with hierarchical `SubStateMachineStateAsset`
2. **Flatten:** `StateFlattener.FlattenStates()` collects leaf states with global indices
3. **Build:** `StateMachineBlobConverter.BuildBlob()` creates flat `BlobAssetReference<StateMachineBlob>`
4. **Runtime:** `UpdateStateMachineJob` operates on flat state array

## Running Tests

```bash
# In Unity Editor:
# Window > General > Test Runner > PlayMode > Run All

# Test locations:
Tests/IntegrationTests/    # Full pipeline tests
Tests/UnitTests/           # Unit tests
Tests/Performance/         # Performance benchmarks
```

## Namespaces

- `DMotion` - Runtime components
- `DMotion.Authoring` - Authoring assets and conversion
- `DMotion.Editor` - Editor tools
- `DMotion.Tests` - Test utilities
