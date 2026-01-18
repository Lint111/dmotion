# Phase 1: Feature Parity Implementation Plan

**Created:** 2026-01-17
**Target:** Unity Mecanim feature parity for DMotion + Mechination
**Total Timeline:** 18-22 weeks

---

## Executive Summary

Phase 1 delivers four critical features to achieve feature parity with Unity's Animator system:

| Phase | Feature | Timeline | Status |
|-------|---------|----------|--------|
| 1A | Parameter Propagation | 2-3 weeks | Not Started |
| 1B | 2D Blend Trees | 4-6 weeks | Not Started |
| 1C | Multiple Layers | 8-10 weeks | Not Started |
| 1D | Avatar Masks | 2-3 weeks | Not Started |

---

## Dependency Graph

```
Parameter Propagation (1A)
         │
         │ (independent, can start immediately)
         │
         ▼
2D Blend Trees (1B) ◄──── No hard dependencies
         │
         │ (can run in parallel with 1A)
         │
         ▼
Multiple Layers (1C) ◄──── Core architectural change
         │
         │ (required for 1D)
         │
         ▼
Avatar Masks (1D) ◄──── Requires multi-layer system
```

---

## Phase 1A: Parameter Propagation

**Goal:** Automatic parameter dependency management for SubStateMachines

**Problem Solved:**
- Manual parameter synchronization is error-prone
- No visibility into which params a SubStateMachine needs
- No cleanup when SubStateMachines are removed

### Features

1. **Dependency Tracking**
   - Analyze SubStateMachine parameter requirements
   - Track which params are auto-generated vs user-created
   - Mark dependencies on parameter assets

2. **Parameter Linking/Aliasing**
   - Link parent params to child requirements (e.g., "WalkSpeed" → "Speed")
   - Optional value transforms (scale, offset)
   - Runtime resolution during blob conversion

3. **Auto-Resolution**
   - When adding SubStateMachine: check required params
   - Auto-create missing params with dependency markers
   - Suggest linking to existing compatible params

4. **Orphan Cleanup**
   - Detect params no longer used anywhere
   - Safe deletion of auto-generated orphans
   - Warning for user-created orphans

5. **Editor UI**
   - Parameter Dependency Window (centralized view)
   - Enhanced SubStateMachine inspector
   - Visual dependency graph

### Files to Create (DMotion)

| File | Purpose |
|------|---------|
| `Runtime/Authoring/Parameters/ParameterDependency.cs` | Dependency data structures |
| `Runtime/Authoring/Parameters/ParameterLink.cs` | Linking/aliasing structures |
| `Editor/Parameters/ParameterDependencyAnalyzer.cs` | Requirement analysis |
| `Editor/Parameters/ParameterDependencyWindow.cs` | Centralized UI |
| `Editor/Parameters/ParameterCleanupUtils.cs` | Orphan detection/cleanup |

### Files to Modify (DMotion)

| File | Changes |
|------|---------|
| `StateMachineAsset.cs` | Add dependency tracking fields |
| `AnimationParameterAsset.cs` | Add auto-generated marker, required-by list |
| `SubStateMachineStateAsset.cs` | Hook for dependency resolution |
| `AnimationStateMachineConversionUtils.cs` | Parameter link resolution |

### Mechination Changes

None required - operates on DMotion's public API.

### Tasks

- [ ] **1A.1** Add ParameterDependency and ParameterLink data structures
- [ ] **1A.2** Implement ParameterDependencyAnalyzer
- [ ] **1A.3** Add dependency fields to StateMachineAsset
- [ ] **1A.4** Add auto-generated markers to AnimationParameterAsset
- [ ] **1A.5** Implement auto-resolution on SubStateMachine add
- [ ] **1A.6** Implement orphan detection and cleanup
- [ ] **1A.7** Create ParameterDependencyWindow
- [ ] **1A.8** Enhance SubStateMachineInspector with dependency UI
- [ ] **1A.9** Update blob conversion for parameter linking
- [ ] **1A.10** Write unit tests
- [ ] **1A.11** Write integration tests
- [ ] **1A.12** Update documentation

### Acceptance Criteria

- [ ] Adding SubStateMachine auto-detects required parameters
- [ ] Missing parameters are auto-created with dependency markers
- [ ] User can link existing params to SubStateMachine requirements
- [ ] Removing SubStateMachine identifies orphaned params
- [ ] Dependency Window shows full parameter hierarchy
- [ ] All existing tests pass
- [ ] New feature has >80% test coverage

---

## Phase 1B: 2D Blend Trees

**Goal:** Support 2D Simple Directional blend trees for locomotion

**Problem Solved:**
- Cannot blend animations based on 2D input (X/Y movement)
- Standard locomotion patterns require 2D blending

### Features

1. **Directional2DBlendStateAsset**
   - Two blend parameters (X and Y)
   - Clip positions in 2D space
   - Per-clip speed multipliers

2. **2D Weight Calculation**
   - Simple Directional algorithm
   - Find clips forming quad around input
   - Bilinear interpolation within quad

3. **Runtime Integration**
   - New StateType enum value
   - Blob conversion for 2D states
   - Sampling job integration

4. **Mechination Translation**
   - Extract 2D BlendTree from Unity
   - Map positions and thresholds
   - Support SimpleDirectional2D type

### Files to Create (DMotion)

| File | Purpose |
|------|---------|
| `Runtime/Authoring/AnimationStateMachine/Directional2DBlendStateAsset.cs` | Authoring asset |
| `Runtime/Components/Directional2DBlendStateBlob.cs` | Runtime blob |
| `Runtime/Utils/Directional2DBlendUtils.cs` | Weight calculation |

### Files to Modify (DMotion)

| File | Changes |
|------|---------|
| `StateType.cs` | Add `Directional2DBlend = 2` |
| `StateMachineBlob.cs` | Add 2D blend state array |
| `StateMachineBlobConverter.cs` | 2D state conversion |
| `UpdateStateMachineJob.cs` | 2D blend evaluation |
| `AnimationStateConversionData.cs` | 2D conversion data |

### Files to Create (Mechination)

| File | Purpose |
|------|---------|
| `Editor/Core/BlendTree2DData.cs` | 2D blend intermediate data |

### Files to Modify (Mechination)

| File | Changes |
|------|---------|
| `ControllerData.cs` | Add 2D blend data structures |
| `UnityControllerAdapter.cs` | Extract 2D BlendTree |
| `ConversionEngine.cs` | Convert 2D blend data |
| `ConversionResult.cs` | 2D blend result structures |
| `DMotionAssetBuilder.cs` | Create 2D blend assets |

### Tasks

- [ ] **1B.1** Create Directional2DBlendStateAsset
- [ ] **1B.2** Create Directional2DBlendStateBlob
- [ ] **1B.3** Implement 2D weight calculation algorithm
- [ ] **1B.4** Add StateType.Directional2DBlend
- [ ] **1B.5** Update StateMachineBlobConverter for 2D states
- [ ] **1B.6** Update UpdateStateMachineJob for 2D evaluation
- [ ] **1B.7** Create editor inspector for 2D blend state
- [ ] **1B.8** Add 2D BlendTree extraction to UnityControllerAdapter
- [ ] **1B.9** Add 2D blend data to ControllerData/ConversionResult
- [ ] **1B.10** Update ConversionEngine for 2D blends
- [ ] **1B.11** Update DMotionAssetBuilder for 2D assets
- [ ] **1B.12** Write unit tests (weight calculation)
- [ ] **1B.13** Write integration tests (full pipeline)
- [ ] **1B.14** Performance benchmarking
- [ ] **1B.15** Update documentation

### Acceptance Criteria

- [ ] Can create 2D blend state in DMotion editor
- [ ] 2D weights calculate correctly for all quadrants
- [ ] Edge cases handled (corners, boundaries, degenerate)
- [ ] Mechination translates Unity 2D SimpleDirectional
- [ ] Performance acceptable (<500 cycles per evaluation)
- [ ] All existing tests pass
- [ ] New feature has >80% test coverage

---

## Phase 1C: Multiple Layers

**Goal:** Support multiple animation layers with override/additive blending

**Problem Solved:**
- Cannot have upper body play different animation than lower body
- Cannot add reactions/hit animations on top of base layer
- Single state machine limits animation complexity

### Features

1. **Multi-Layer Asset Structure**
   - MultiLayerStateMachineAsset container
   - Per-layer StateMachineAsset
   - Layer blend mode (Override/Additive)
   - Layer weight (0-1)

2. **Per-Layer State Evaluation**
   - Independent state machine per layer
   - Shared parameters across layers
   - Layer synchronization option

3. **Layer Blending**
   - Override: Replace previous layers
   - Additive: Add to previous layers
   - Weight-based influence

4. **Kinemation Integration**
   - Multi-layer animation state creation
   - Blended clip sampling
   - Performance optimization

5. **Mechination Translation**
   - Extract all layers from AnimatorController
   - Map blend modes
   - Handle synced layers

### Files to Create (DMotion)

| File | Purpose |
|------|---------|
| `Runtime/Authoring/MultiLayer/MultiLayerStateMachineAsset.cs` | Root asset |
| `Runtime/Authoring/MultiLayer/AnimationLayerAsset.cs` | Per-layer config |
| `Runtime/Components/MultiLayerStateMachineBlob.cs` | Runtime blob |
| `Runtime/Components/AnimationLayerState.cs` | Per-layer state buffer |
| `Runtime/Systems/UpdateMultiLayerStateMachineJob.cs` | Layer evaluation |
| `Runtime/Systems/BlendMultiLayerAnimationJob.cs` | Layer blending |
| `Editor/MultiLayer/MultiLayerStateMachineEditor.cs` | Custom editor |

### Files to Modify (DMotion)

| File | Changes |
|------|---------|
| `AnimationStateMachineConversionUtils.cs` | Multi-layer conversion |
| Various authoring bakers | Multi-layer component setup |

### Files to Modify (Mechination)

| File | Changes |
|------|---------|
| `ControllerData.cs` | Layer data structures |
| `UnityControllerAdapter.cs` | Extract all layers |
| `ConversionEngine.cs` | Multi-layer conversion |
| `ConversionResult.cs` | Layer results |
| `DMotionAssetBuilder.cs` | Create layer assets |
| `UnityControllerConverter.cs` | Orchestrate multi-layer |

### Tasks

- [ ] **1C.1** Design multi-layer component architecture
- [ ] **1C.2** Create MultiLayerStateMachineAsset
- [ ] **1C.3** Create AnimationLayerAsset
- [ ] **1C.4** Create multi-layer blob structures
- [ ] **1C.5** Implement AnimationLayerState buffer
- [ ] **1C.6** Implement UpdateMultiLayerStateMachineJob
- [ ] **1C.7** Implement Override blending
- [ ] **1C.8** Implement Additive blending
- [ ] **1C.9** Integrate with Kinemation sampling
- [ ] **1C.10** Implement layer synchronization
- [ ] **1C.11** Create multi-layer editor UI
- [ ] **1C.12** Add layer extraction to UnityControllerAdapter
- [ ] **1C.13** Update Mechination conversion pipeline
- [ ] **1C.14** Write unit tests
- [ ] **1C.15** Write integration tests
- [ ] **1C.16** Performance optimization
- [ ] **1C.17** Update documentation

### Acceptance Criteria

- [ ] Can create multi-layer state machine in editor
- [ ] Layers evaluate independently
- [ ] Override blending replaces lower layers
- [ ] Additive blending adds to lower layers
- [ ] Layer weights affect blend influence
- [ ] Mechination translates multi-layer controllers
- [ ] Performance acceptable (linear with layer count)
- [ ] All existing tests pass
- [ ] New feature has >80% test coverage

---

## Phase 1D: Avatar Masks

**Goal:** Filter which bones a layer affects

**Problem Solved:**
- Cannot have upper body layer affect only upper body
- All animations affect entire skeleton
- No partial body blending

### Features

1. **BoneMaskAsset**
   - List of active bone paths
   - Inclusive vs exclusive mode
   - Humanoid body part shortcuts

2. **Per-Layer Mask Assignment**
   - Layer references optional mask
   - Mask applied during sampling

3. **Filtered Bone Sampling**
   - Sparse bone index array
   - Only sample/apply masked bones
   - Efficient runtime filtering

4. **Mechination Translation**
   - Extract AvatarMask from Unity
   - Map humanoid regions to bones
   - Handle transform masks

### Files to Create (DMotion)

| File | Purpose |
|------|---------|
| `Runtime/Authoring/Masks/BoneMaskAsset.cs` | Mask definition |
| `Runtime/Components/BoneMaskBlob.cs` | Runtime mask |
| `Editor/Masks/BoneMaskEditor.cs` | Mask editor |

### Files to Modify (DMotion)

| File | Changes |
|------|---------|
| `AnimationLayerAsset.cs` | Add mask reference |
| `BlendMultiLayerAnimationJob.cs` | Apply mask filtering |
| Blob conversion | Convert mask to bone indices |

### Files to Modify (Mechination)

| File | Changes |
|------|---------|
| `ControllerData.cs` | Mask data structures |
| `UnityControllerAdapter.cs` | Extract AvatarMask |
| `DMotionAssetBuilder.cs` | Create mask assets |

### Tasks

- [ ] **1D.1** Create BoneMaskAsset
- [ ] **1D.2** Create BoneMaskBlob
- [ ] **1D.3** Implement bone path to index resolution
- [ ] **1D.4** Add mask reference to AnimationLayerAsset
- [ ] **1D.5** Implement filtered bone sampling
- [ ] **1D.6** Add humanoid body part support
- [ ] **1D.7** Create mask editor UI
- [ ] **1D.8** Add AvatarMask extraction to Mechination
- [ ] **1D.9** Write unit tests
- [ ] **1D.10** Write integration tests
- [ ] **1D.11** Update documentation

### Acceptance Criteria

- [ ] Can create bone mask asset
- [ ] Mask correctly filters bone sampling
- [ ] Layer with mask only affects specified bones
- [ ] Humanoid shortcuts work correctly
- [ ] Mechination translates Unity AvatarMasks
- [ ] Performance impact <15% for masked layers
- [ ] All existing tests pass
- [ ] New feature has >80% test coverage

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Kinemation integration complexity | Early prototype, consult Latios docs |
| Performance degradation | Benchmark each phase, optimize hot paths |
| Breaking changes | Feature flags, parallel systems during transition |
| Mechination translation edge cases | Comprehensive Unity test controllers |
| Scope creep | Strict task definitions, defer nice-to-haves |

---

## Success Metrics

- All four features fully implemented
- 100% backward compatibility with existing DMotion projects
- <20% performance overhead for new features
- >80% test coverage on new code
- Documentation updated for all features
- Mechination translates all supported Unity features

---

## Notes

- Each phase should be committed incrementally
- Create feature branch for each phase
- Update FeatureParity.md as features complete
- Consider preview/beta releases between phases
