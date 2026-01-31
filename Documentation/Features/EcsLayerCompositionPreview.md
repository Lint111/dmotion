# ECS Layer Composition Preview

## Overview

Extend the ECS runtime preview system to support multi-layer animation state machines, enabling accurate preview of layer composition as it would behave in-game.

**Status**: Planning  
**Priority**: High  
**Dependencies**: Existing ECS preview backend, Layer composition authoring preview

## Problem Statement

The current ECS preview (`EcsPreviewBackend`) only supports single-layer state machines. Multi-layer state machines use `AnimationStateMachineLayer` buffers and require:
- Per-layer timeline control
- Layer weight composition
- Bone mask handling
- Independent or synchronized time control

The authoring preview (`LayerCompositionPreview`) provides this functionality using `PlayableGraph`, but doesn't reflect actual ECS runtime behavior.

## Goals

1. **Runtime Accuracy**: Preview multi-layer animations using actual ECS systems
2. **Feature Parity**: Match authoring preview capabilities (state, transition, weight, mask, time control)
3. **Runtime Editing**: Enable layer weight/enabled editing to demonstrate ECS system interaction
4. **Unified Mask Handling**: Use `ILayerBoneMask` abstraction consistently across both preview backends

## Architecture

### Current State

```
AUTHORING PREVIEW                          ECS PREVIEW (Single-Layer Only)
┌────────────────────────────┐            ┌────────────────────────────┐
│  LayerCompositionPreview   │            │    EcsPreviewBackend       │
│  ├─ AnimationLayerMixer    │            │    ├─ TimelineControlHelper│
│  ├─ StatePlayableBuilder   │            │    ├─ EcsPreviewWorldService
│  └─ Direct AvatarMask      │            │    └─ EcsHybridRenderer    │
└────────────────────────────┘            └────────────────────────────┘
```

### Target State

```
AUTHORING PREVIEW                          ECS PREVIEW (Multi-Layer)
┌────────────────────────────┐            ┌─────────────────────────────┐
│  LayerCompositionPreview   │            │  EcsLayerCompositionPreview │
│  ├─ AnimationLayerMixer    │            │  ├─ MultiLayerTimelineHelper│
│  ├─ StatePlayableBuilder   │            │  ├─ EcsPreviewWorldService  │
│  └─ ILayerBoneMask         │            │  └─ EcsHybridRenderer (ML)  │
└────────────────────────────┘            └─────────────────────────────┘
         │                                           │
         └──────────── ILayerBoneMask ───────────────┘
                    (unified abstraction)
```

## Implementation Phases

### Phase 0: Bone Mask Abstraction Unification (Prerequisite)

**Goal**: Refactor authoring preview to use `ILayerBoneMask` instead of direct `AvatarMask` access.

#### Tasks

- [ ] Create `BoneMaskPlayableExtensions` utility class
  - `SetLayerMaskFromBoneMask(mixer, index, ILayerBoneMask, skeletonRoot)`
  - Handle `AvatarMaskBoneMask` directly
  - Handle `ExplicitBoneMask` by building temporary `AvatarMask`

- [ ] Update `LayerCompositionPreview.LayerData`
  - Replace `AvatarMask AvatarMask` with `ILayerBoneMask BoneMask`
  - Update `BuildLayerData()` to use `ILayerBoneMask` from `LayerStateAsset`

- [ ] Update mask application in `RebuildLayerPlayables()`
  - Use `BoneMaskPlayableExtensions.SetLayerMaskFromBoneMask()`

#### Files

| File | Action |
|------|--------|
| `Editor/Utilities/BoneMaskPlayableExtensions.cs` | Create |
| `Editor/Preview/Backends/LayerCompositionPreview.cs` | Modify |

#### Acceptance Criteria

- Authoring layer composition preview works identically after refactor
- Both `AvatarMaskBoneMask` and `ExplicitBoneMask` are supported
- No direct `AvatarMask` access in preview code

---

### Phase 1: ECS Multi-Layer Timeline Control Components

**Goal**: Extend timeline control system to support per-layer commands.

#### New Components

```csharp
// Runtime/Components/Core/LayerTimelineControl.cs

/// <summary>
/// Tag marking entity as under multi-layer timeline control.
/// Mutually exclusive with TimelineControlled (single-layer).
/// </summary>
public struct MultiLayerTimelineControlled : IComponentData { }

/// <summary>
/// Per-layer timeline command for preview control.
/// </summary>
public struct LayerTimelineCommand : IBufferElementData
{
    public byte LayerIndex;
    public TimelineCommandType Type;  // Play, Pause, ScrubState, ScrubTransition, StepFrame
    public float Value;               // Normalized time or transition progress
    public float2 BlendPosition;      // For blend states
}

/// <summary>
/// Per-layer current timeline position.
/// </summary>
public struct LayerTimelinePosition : IBufferElementData
{
    public byte LayerIndex;
    public float NormalizedTime;
    public float TransitionProgress;
    public bool IsTransitionMode;
}

/// <summary>
/// Per-layer render request generated from timeline commands.
/// </summary>
public struct LayerRenderRequest : IBufferElementData
{
    public byte LayerIndex;
    public LayerRenderRequestType Type;  // State or Transition
    public short StateIndex;
    public short FromStateIndex;
    public short ToStateIndex;
    public float NormalizedTime;
    public float TransitionWeight;
    public float2 BlendPosition;
}

/// <summary>
/// Runtime override for layer weight (for preview editing).
/// </summary>
public struct LayerWeightOverride : IBufferElementData
{
    public byte LayerIndex;
    public float Weight;
    public bool IsEnabled;
    public bool UseOverride;  // If false, use baked weight
}
```

#### System Updates

- [ ] Add `ProcessLayerTimelineCommandsJob` to `AnimationTimelineControllerSystem`
  - Process `LayerTimelineCommand` buffer
  - Update `LayerTimelinePosition` buffer
  - Generate `LayerRenderRequest` entries

- [ ] Add `ApplyLayerRenderRequestsJob`
  - Read `LayerRenderRequest` buffer
  - Update `AnimationState` and `ClipSampler` buffers respecting `LayerIndex`

- [ ] Add `ApplyLayerWeightOverridesJob`
  - Apply `LayerWeightOverride` to layer weights before composition

#### Files

| File | Action |
|------|--------|
| `Runtime/Components/Core/LayerTimelineControl.cs` | Create |
| `Runtime/Systems/Core/AnimationTimelineControllerSystem.cs` | Modify |
| `Runtime/Systems/Core/ApplyLayerRenderRequestSystem.cs` | Create or extend |

#### Acceptance Criteria

- Per-layer commands can control individual layer states/transitions
- Layer weight overrides work correctly
- Existing single-layer timeline control unchanged

---

### Phase 2: ECS Preview World Multi-Layer Setup

**Goal**: Enable `EcsPreviewWorldService` to create and manage multi-layer preview entities.

#### Tasks

- [ ] Add `CreateMultiLayerPreviewEntity()` method
  - Create entity with `AnimationStateMachineLayer` buffer
  - Add all layer-specific components
  - Bake layer blobs from `StateMachineAsset`

- [ ] Add `SetupLayerForPreview()` method
  - Initialize specific layer's `AnimationState` and `ClipSampler` entries
  - Handle state type (Single, LinearBlend, Directional2D)

- [ ] Add `GetActiveLayerSamplers()` method
  - Extract samplers grouped by layer index
  - Include layer metadata (weight, blend mode, mask)

- [ ] Create `MultiLayerTimelineControlHelper` class
  - Editor API for layer timeline commands
  - Methods: `SetupLayerStatePreview`, `SetupLayerTransitionPreview`, etc.
  - Master time synchronization support

#### Multi-Layer Entity Structure

```
Entity (Multi-Layer Preview)
├── MultiLayerTimelineControlled (tag)
├── DynamicBuffer<AnimationStateMachineLayer>
├── DynamicBuffer<AnimationLayerCurrentState>
├── DynamicBuffer<AnimationLayerTransition>
├── DynamicBuffer<AnimationState>           // All layers, tagged with LayerIndex
├── DynamicBuffer<ClipSampler>              // All layers, tagged with LayerIndex
├── DynamicBuffer<SingleClipState>
├── DynamicBuffer<LinearBlendStateMachineState>
├── DynamicBuffer<Directional2DBlendStateMachineState>
├── DynamicBuffer<LayerTimelineCommand>
├── DynamicBuffer<LayerTimelinePosition>
├── DynamicBuffer<LayerRenderRequest>
└── DynamicBuffer<LayerWeightOverride>
```

#### Files

| File | Action |
|------|--------|
| `Editor/Preview/EcsWorld/EcsPreviewWorldService.cs` | Modify |
| `Editor/Preview/EcsWorld/MultiLayerTimelineControlHelper.cs` | Create |

#### Acceptance Criteria

- Multi-layer entities created correctly from `StateMachineAsset`
- Per-layer state setup works for all state types
- Master time synchronization matches authoring preview behavior

---

### Phase 3: ECS Hybrid Renderer Multi-Layer Support

**Goal**: Update `EcsHybridRenderer` to render multiple layers with weights and masks.

#### Tasks

- [ ] Add `BuildMultiLayerPlayableGraph()` method
  - Create `AnimationLayerMixerPlayable` as root
  - Create per-layer `AnimationMixerPlayable` connected to layer mixer
  - Configure layer blend modes (Override/Additive)

- [ ] Add `UpdateFromMultiLayerSamplers()` method
  - Group samplers by layer index
  - Update each layer's playable structure
  - Handle varying clip counts per layer

- [ ] Add `ApplyLayerConfiguration()` method
  - Set layer weights from `LayerWeightOverride` or baked values
  - Apply bone masks using `BoneMaskPlayableExtensions`
  - Handle additive blend mode flag

- [ ] Store layer metadata for rendering
  - Cache `ILayerBoneMask` references
  - Cache blend mode per layer

#### PlayableGraph Structure

```
AnimationPlayableOutput
        │
AnimationLayerMixerPlayable (root)
├── [0] AnimationMixerPlayable (Base Layer, Override, full body)
│       ├── [0] AnimationClipPlayable
│       └── [1] AnimationClipPlayable
├── [1] AnimationMixerPlayable (Upper Body, Override, masked)
│       └── [0] AnimationClipPlayable
└── [2] AnimationMixerPlayable (Additive Layer, Additive, full body)
        └── [0] AnimationClipPlayable
```

#### Files

| File | Action |
|------|--------|
| `Editor/Preview/EcsWorld/EcsHybridRenderer.cs` | Modify |

#### Acceptance Criteria

- Multiple layers render correctly with proper blending
- Bone masks work (using `ILayerBoneMask`)
- Override and Additive blend modes work correctly
- Layer weights affect final pose

---

### Phase 4: ECS Layer Composition Preview Backend

**Goal**: Create `EcsLayerCompositionPreview` implementing `ILayerCompositionPreview`.

#### Class Design

```csharp
// Editor/Preview/Backends/EcsLayerCompositionPreview.cs

public class EcsLayerCompositionPreview : ILayerCompositionPreview, IDisposable
{
    // Dependencies
    private EcsPreviewWorldService worldService;
    private MultiLayerTimelineControlHelper timelineHelper;
    private EcsHybridRenderer hybridRenderer;
    
    // State
    private Entity previewEntity;
    private StateMachineAsset stateMachine;
    private List<EcsLayerData> layers;
    
    // Time control
    private float masterTime;
    private bool syncLayers;
    private bool isPlaying;
    
    // ILayerCompositionPreview implementation
    public int LayerCount { get; }
    public bool SyncLayers { get; set; }
    public float MasterTime { get; set; }
    public bool IsPlaying { get; set; }
    
    public void Initialize(StateMachineAsset sm, AnimatedPreviewModel model);
    public void Clear();
    public void Dispose();
    
    // Per-layer control
    public void SetLayerState(int index, AnimationStateAsset state);
    public void SetLayerTransition(int index, AnimationStateAsset from, AnimationStateAsset to);
    public void ClearLayerState(int index);
    public void SetLayerNormalizedTime(int index, float time);
    public void SetLayerTransitionProgress(int index, float progress);
    public void SetLayerBlendPosition(int index, float2 pos);
    public void SetLayerWeight(int index, float weight);
    public void SetLayerEnabled(int index, bool enabled);
    
    // Queries
    public AnimationStateAsset GetLayerState(int index);
    public float GetLayerNormalizedTime(int index);
    public float GetLayerWeight(int index);
    public bool IsLayerEnabled(int index);
    public bool IsLayerInTransition(int index);
    
    // Rendering
    public void Tick(float deltaTime);
    public void Draw(Rect rect);
}
```

#### Tasks

- [ ] Implement `EcsLayerCompositionPreview` class
- [ ] Implement all `ILayerCompositionPreview` methods
- [ ] Add master time synchronization logic (same as authoring preview)
- [ ] Add ECS world tick integration
- [ ] Integrate with `EcsPreviewBackend`

#### Integration with `EcsPreviewBackend`

```csharp
// In EcsPreviewBackend
public ILayerCompositionPreview LayerComposition
{
    get
    {
        if (currentStateMachine?.IsMultiLayer == true)
        {
            if (ecsLayerComposition == null)
            {
                ecsLayerComposition = new EcsLayerCompositionPreview(
                    worldService, 
                    hybridRenderer);
            }
            return ecsLayerComposition;
        }
        return null;
    }
}
```

#### Files

| File | Action |
|------|--------|
| `Editor/Preview/Backends/EcsLayerCompositionPreview.cs` | Create |
| `Editor/Preview/Backends/EcsPreviewBackend.cs` | Modify |

#### Acceptance Criteria

- `ILayerCompositionPreview` fully implemented
- Layer states, transitions, weights work via ECS
- Master time sync matches authoring preview
- Existing inspector UI works without modification

---

### Phase 5: Parameter Integration

**Goal**: Enable parameter editing in ECS layer composition preview.

#### Tasks

- [ ] Extend `EcsEntityBrowser` to work with multi-layer entities
  - Show per-layer state info
  - Show per-layer clip samplers

- [ ] Add parameter override support
  - Reuse existing parameter editing from single-layer preview
  - Parameters are global (not per-layer)

- [ ] Add ECS debug panel for layer composition
  - Show `AnimationStateMachineLayer` buffer state
  - Show layer weight composition results
  - Show active render requests

#### Files

| File | Action |
|------|--------|
| `Editor/Preview/EcsWorld/EcsEntityBrowser.cs` | Modify |
| `Editor/Preview/Inspectors/EcsLayerDebugPanel.cs` | Create (optional) |

#### Acceptance Criteria

- Parameters can be edited while previewing layers
- Layer debug info visible in inspector

---

## Time Synchronization Design

### Master Time Behavior

When `SyncLayers = true`:
1. `MasterTime` advances in seconds (or normalized 0-1)
2. Each layer calculates its normalized time: `layerTime = MasterTime / layerClipDuration`
3. All layers stay in sync relative to master time

When `SyncLayers = false`:
1. Each layer has independent `NormalizedTime`
2. Each layer ticks independently

### Future: Animation Offset

Support per-layer time offset for staggered animations:
```csharp
public struct LayerTimeOffset : IBufferElementData
{
    public byte LayerIndex;
    public float Offset;  // Added to calculated time
}
```

---

## File Summary

| Phase | File | Action | Priority |
|-------|------|--------|----------|
| 0 | `Editor/Utilities/BoneMaskPlayableExtensions.cs` | Create | High |
| 0 | `Editor/Preview/Backends/LayerCompositionPreview.cs` | Modify | High |
| 1 | `Runtime/Components/Core/LayerTimelineControl.cs` | Create | High |
| 1 | `Runtime/Systems/Core/AnimationTimelineControllerSystem.cs` | Modify | High |
| 2 | `Editor/Preview/EcsWorld/EcsPreviewWorldService.cs` | Modify | High |
| 2 | `Editor/Preview/EcsWorld/MultiLayerTimelineControlHelper.cs` | Create | High |
| 3 | `Editor/Preview/EcsWorld/EcsHybridRenderer.cs` | Modify | High |
| 4 | `Editor/Preview/Backends/EcsLayerCompositionPreview.cs` | Create | High |
| 4 | `Editor/Preview/Backends/EcsPreviewBackend.cs` | Modify | Medium |
| 5 | `Editor/Preview/EcsWorld/EcsEntityBrowser.cs` | Modify | Low |

---

## Testing Strategy

### Unit Tests

- [ ] `LayerTimelineControl` component serialization
- [ ] `BoneMaskPlayableExtensions` mask conversion
- [ ] Master time calculation per layer

### Integration Tests

- [ ] Multi-layer entity creation from `StateMachineAsset`
- [ ] Per-layer state/transition preview
- [ ] Layer weight composition
- [ ] Bone mask application

### Manual Testing

- [ ] Compare authoring vs ECS preview for same state machine
- [ ] Verify visual parity
- [ ] Test all state types (Single, LinearBlend, Directional2D)
- [ ] Test override and additive blend modes
- [ ] Test bone masks

---

## Open Questions

1. **Blend space preview**: Should 2D blend spaces have interactive position control in ECS preview? (Currently supported in authoring)

2. **Events**: Should animation events fire during ECS layer composition preview?

3. **Performance**: Should we add throttling for ECS world updates in preview to avoid editor lag?

---

## References

- `Editor/Preview/Backends/LayerCompositionPreview.cs` - Authoring implementation
- `Editor/Preview/Backends/EcsPreviewBackend.cs` - Current ECS preview
- `Runtime/Systems/StateMachine/UpdateMultiLayerStateMachineJob.cs` - Runtime layer update
- `Runtime/Systems/Core/LayerWeightCompositionSystem.cs` - Runtime weight composition
