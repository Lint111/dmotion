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
| State Machine Blob | `Runtime/Components/Blobs/StateMachineBlob.cs` | Runtime flat state data |
| State Flattener | `Runtime/Authoring/Baking/StateFlattener.cs` | Hierarchy → flat conversion |
| Blob Converter | `Runtime/Authoring/Baking/StateMachineBlobConverter.cs` | Builds runtime blob |
| Conversion Utils | `Runtime/Authoring/Baking/AnimationStateMachineConversionUtils.cs` | Entity setup, blob creation |
| Update Job | `Runtime/Systems/StateMachine/UpdateStateMachineJob.cs` | Runtime state machine logic |
| SubStateMachine Asset | `Runtime/Authoring/Assets/States/SubStateMachineStateAsset.cs` | Visual hierarchy container |
| StateMachine Asset | `Runtime/Authoring/Assets/StateMachine/StateMachineAsset.cs` | Root asset + hierarchy APIs |

## Assembly Structure

- `DMotion` - Runtime components and systems
- `DMotion.Editor` - Editor tools and inspectors
- `DMotion.Tests.Runtime` - PlayMode tests
- `DMotion.Tests.Editor` - EditMode tests

## Folder Structure

### Runtime
```
Runtime/
├── Components/
│   ├── Core/           # AnimationState, ClipSampler, etc.
│   ├── StateMachine/   # StateMachineState, TransitionState, etc.
│   ├── Blobs/          # StateMachineBlob, ClipBlob, etc.
│   └── Rendering/      # MaterialProperty components
├── Systems/
│   ├── Core/           # Core animation systems
│   ├── StateMachine/   # UpdateStateMachineJob, transition logic
│   ├── Sampling/       # Clip sampling systems
│   ├── StateTypes/     # Single, LinearBlend, BlendSpace handlers
│   ├── RootMotion/     # Root motion systems
│   └── Rendering/      # Material property systems
├── Authoring/
│   ├── Assets/
│   │   ├── StateMachine/   # StateMachineAsset
│   │   ├── States/         # SingleClip, LinearBlend, SubStateMachine assets
│   │   ├── Parameters/     # Parameter assets
│   │   ├── Clips/          # Clip reference assets
│   │   └── Transitions/    # Transition assets
│   ├── Baking/         # StateFlattener, BlobConverter, ConversionUtils
│   ├── MonoBehaviours/ # AnimationStateMachineAuthoring
│   ├── Bootstrap/      # Bootstrap systems
│   └── Types/          # Shared authoring types
└── Utils/
    ├── Animation/      # Animation utilities
    ├── StateTypes/     # State type utilities
    ├── Core/           # Core utilities
    └── Baking/         # Baking utilities
```

### Editor
```
Editor/
├── StateMachineEditor/
│   ├── Windows/        # StateMachineEditorWindow
│   ├── Graph/
│   │   ├── Nodes/      # StateNode, TransitionNode, etc.
│   │   ├── Edges/      # TransitionEdge, etc.
│   │   └── Manipulators/
│   ├── Inspectors/     # State/transition inspectors
│   ├── Navigation/     # Breadcrumb, hierarchy navigation
│   ├── Popups/         # Context menus, search windows
│   └── Events/         # Editor events
├── Preview/
│   ├── Windows/        # AnimationPreviewWindow
│   ├── Backends/       # AuthoringPreviewBackend, EcsPreviewBackend
│   ├── EcsWorld/       # ECS world management for preview
│   ├── Rendering/      # Preview rendering
│   ├── Timeline/       # AnimationTimelineControl
│   ├── StateContent/   # State-specific preview content
│   ├── Inspectors/     # Preview inspectors
│   ├── State/          # Preview state management
│   ├── UI/             # Preview UI elements
│   ├── Events/         # Preview events
│   └── Curves/         # Curve visualization
├── BlendSpaceEditor/   # 2D blend space editor
├── LegacyPreview/      # Legacy preview system (deprecated)
├── PropertyDrawers/
│   ├── Drawers/        # Custom property drawers
│   ├── Selectors/      # Asset selectors
│   └── Caches/         # Editor caches
├── Utilities/          # AnimationStateUtils, TransitionTimingCalculator
├── CustomEditors/      # Custom inspectors, context menus
├── UIElements/         # Reusable UI elements
└── Integrations/       # Unity.Entities.Exposed integration
```

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
