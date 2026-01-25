# Feature Plan: ECS Preview World + Rig Binding

## Status: ✅ COMPLETE (DMotion Scope)

**Completed:** 7/8 phases (Phase 4 deferred, Phase 8 in Mechination repo)

| Phase | Status | Description |
|-------|--------|-------------|
| 0 | ✅ | Boot-Time Cleanup |
| 1 | ✅ | Rig Binding Data Model |
| 2 | ✅ | Preview Session Abstraction |
| 3 | ✅ | Rig-Aware Preview |
| 4 | ⏸️ | Asset Creation Workflow (deferred - low priority) |
| 5 | ✅ | ECS Preview World Service |
| 6 | ✅ | ECS Preview Rendering |
| 7 | ✅ | Mode Toggle UI |
| 8 | ⏸️ | Mechination Integration (out of scope - mechination repo) |

**See also:** [PreviewModes_GUIDE.md](../../Docs/PreviewModes_GUIDE.md) for user documentation.

---

## Relationship to AnimationPreviewWindow

This feature plan **builds on top of** the existing AnimationPreviewWindow work. See [AnimationPreviewWindow.md](./AnimationPreviewWindow.md) for the authoring preview implementation.

### Existing Foundation (Completed)

| Component | Status | Purpose |
|-----------|--------|---------|
| `AnimationPreviewWindow` | ✅ Complete | Main window, UI coordinator |
| `StateInspectorBuilder` | ✅ Complete | State info panel |
| `TransitionInspectorBuilder` | ✅ Complete | Transition info panel |
| `TimelineScrubber` | ✅ Complete | Play/pause/scrub controls |
| `PreviewRenderer` | ✅ Complete | 3D viewport orchestration |
| `PlayableGraphPreview` | ✅ Existing | Playables-based animation sampling |
| `SingleClipPreview` | ✅ Existing | Single clip 3D preview |
| Blend content builders | ✅ Complete | 1D/2D blend visualization |
| Menu reorganization | ✅ Complete | Window/DMotion/Open Workspace |

### What This Plan Adds

```
                    AnimationPreviewWindow (existing)
                              │
                      PreviewSession (new)
                         │         │
              ┌──────────┴─────────┴──────────┐
              ▼                               ▼
    PlayableGraphBackend (new)      EcsPreviewBackend (new)
              │                               │
    PlayableGraphPreview (existing)   EcsPreviewWorldService (new)
    + PreviewRenderer (existing)      + DMotion runtime systems
```

- **Session abstraction** - thin layer between UI and backends
- **ECS backend** - alternative engine using real runtime systems
- **Rig binding** - deterministic armature selection (benefits both modes)
- **Mode toggle** - user picks Authoring vs ECS preview

---

## Overview

This document outlines the implementation plan for:
1. **ECS Preview World** - Runtime-accurate animation preview in the editor, only active when the preview window is open
2. **Rig Binding System** - Deterministic armature/rig association for StateMachineAssets, with Mechination integration
3. **Boot-Time Cleanup** - Eliminating JobTempAlloc warnings by ensuring test bootstraps don't hijack editor startup

## Goals

- Preview DMotion state machines with runtime-accurate ECS behavior without entering Play Mode
- Deterministic rig binding: no silent guessing, explicit user decisions when ambiguous
- Support both authoring-time preview (PlayableGraph) and ECS-time preview (runtime systems)
- Mechination preserves Unity's rig decisions when converting AnimatorControllers
- No background ECS work unless preview window is open

## Non-Goals

- Replacing Unity's Animator window feature set
- Shipping preview-only systems in runtime builds
- Full Entities Graphics/BRG rendering in editor preview (may be added later as optional)

---

## Architecture

### Two Preview Modes (Unified Session API)

```
┌─────────────────────────────────────────────────────────────┐
│                  AnimationPreviewWindow                      │
│                         │                                    │
│                  PreviewSession                              │
│                    │         │                               │
│         ┌─────────┴─────────┴─────────┐                     │
│         ▼                             ▼                      │
│  PlayableGraphBackend          EcsPreviewBackend            │
│  (Authoring Preview)           (Runtime Preview)            │
│         │                             │                      │
│    AnimationMode              Isolated ECS World            │
│    + PlayableGraph            + DMotion Systems             │
│    + PreviewRenderUtility     + Kinemation Sampling         │
└─────────────────────────────────────────────────────────────┘
```

### ECS Preview World Lifecycle

- **Created**: `AnimationPreviewWindow.OnEnable()`
- **Updated**: 
  - Playing: continuous tick via `EditorWindow.Update()`
  - Paused: poll tick detects dirty changes (selection/params/time/rig) and updates only when needed
- **Disposed**: `AnimationPreviewWindow.OnDisable()`
- **Never**: touches `World.DefaultGameObjectInjectionWorld`

### Rig Binding Data Model

```csharp
// On StateMachineAsset
public UnityEngine.Object BoundArmatureData;      // Avatar or other armature type
public RigBindingStatus RigBindingStatus;         // Resolved, UserOptedOut, Unresolved
public RigBindingSource RigBindingSource;         // FromAnimatorAvatar, FromImporterSourceAvatar, UserSelected, None
public string RigBindingFingerprint;              // Hash for change detection (Mechination use)
```

---

## Mechination Rig Resolution

### Resolution Algorithm (Priority Order)

1. **Animator Context Available** (`Animator.avatar != null`)
   - Bind that avatar directly
   - Source: `FromAnimatorAvatar`
   - Confidence: High

2. **Controller-Only Conversion**
   - Gather all clips from controller graph (states, blend trees, motions)
   - Find `ModelImporter.sourceAvatar` for each clip's owner FBX
   - If exactly one unique avatar candidate: bind it
   - Source: `FromImporterSourceAvatar`

3. **Unresolved**
   - Zero candidates OR multiple ambiguous candidates
   - Prompt user (see below)

### Prompt Behavior

**When to prompt:**
- Rig is unresolved AND asset is not opted-out
- OR asset is opted-out BUT fingerprint changed (new binding discovered)

**Prompt options:**
- Choose armature data asset (resolves binding)
- Opt out (skip binding, remember choice)

**Prompt memory:**
- `RigBindingStatus = UserOptedOut` + `RigBindingFingerprint` stored
- No re-prompt unless fingerprint changes

### Fingerprint Calculation (Mechination responsibility)

For Animator-based conversion:
- `Avatar GUID + fileID`

For Controller-only conversion:
- Sorted set of candidate avatar GUIDs
- Optionally include controller GUID + clip list version

---

## DMotion Preview Rig Resolution

Resolution order in preview window:

1. `StateMachineAsset.BoundArmatureData` (authoritative)
2. If missing and `RigBindingStatus == UserOptedOut`: show "No rig bound" message
3. If missing and `RigBindingStatus == Unresolved`: show picker UI
4. User can always override via preview window (saves back to asset or EditorPrefs)

---

## Phases

### Phase 0: Boot-Time Cleanup (Test Bootstrap Hygiene) ✅ COMPLETE

**Goal:** No Kinemation/BRG jobs on editor startup

**Problem:**
- `Tests/Core/TestBootstrap.cs` contains `TestEditorBootstrap : ICustomEditorBootstrap`
- This installs Kinemation into the default editor world at startup
- Causes `GenerateBrgDrawCommandsSystem` JobTempAlloc warnings

**Solution:**
- Ensure test bootstraps only compile/activate under `UNITY_INCLUDE_TESTS`
- Verify `DMotion.Tests.*.asmdef` has `defineConstraints: ["UNITY_INCLUDE_TESTS"]`
- Move `TestEditorBootstrap` to `DMotion.Tests.Editor` if needed
- Keep `TestBakingBootstrap` for test baking (baking worlds are separate)

**Acceptance Criteria:**
- [x] Unity editor startup produces no JobTempAlloc warnings from Kinemation
- [x] Tests still work when run via Test Runner

**Implementation Notes:**
- Removed `TestEditorBootstrap` from `Tests/Core/TestBootstrap.cs`
- Removed `LatiosEditorBootstrap` from `Samples~/All Samples/Common/Scripts/LatiosBootstrap.cs`
- Added explanatory comments in both files referencing this document
- Verified fix in test environment project - no more JobTempAlloc warnings
- Note: `ICustomBakingBootstrap` conflict warning remains (harmless, both do same thing)

### Phase 1: Rig Binding Data Model ✅ COMPLETE

**Goal:** StateMachineAsset stores rig binding metadata

**Changes:**
- Add fields to `StateMachineAsset`:
  - `UnityEngine.Object BoundArmatureData`
  - `RigBindingStatus` enum
  - `RigBindingSource` enum
  - `string RigBindingFingerprint`
- Add enums to `DMotion.Authoring` namespace

**Files to modify:**
- `Runtime/Authoring/AnimationStateMachine/StateMachineAsset.cs`

**Acceptance Criteria:**
- [x] Fields serialize correctly
- [x] Inspector shows binding info (read-only or editable)
- [x] Existing assets migrate gracefully (null/unset = Unresolved)

**Implementation Notes:**
- Created `Runtime/Authoring/AnimationStateMachine/RigBinding.cs` with enums
- Added rig binding region to `StateMachineAsset` with serialized fields
- Added helper methods: `BindRig()`, `ClearRigBinding()`, `HasResolvedRig`
- BoundArmatureData visible in inspector under "Rig Binding" header
- Status/Source/Fingerprint hidden in inspector (for programmatic use)

### Phase 2: Preview Session Abstraction ✅ COMPLETE

**Goal:** Unified API for preview backends

**New types:**
- `IPreviewBackend` interface
- `PreviewSession` (owns backend, dirty flags, time, parameters)
- `PlayableGraphBackend` (wraps existing preview code)

**Dirty tracking:**
- `SelectionDirty`
- `ParametersDirty`
- `TimeDirty`
- `RigDirty`

**Files added:**
- `Editor/EditorWindows/Preview/PreviewSession.cs`
- `Editor/EditorWindows/Preview/IPreviewBackend.cs`
- `Editor/EditorWindows/Preview/PlayableGraphBackend.cs`
- `Editor/EditorWindows/Preview/PreviewBackendBase.cs` (shared base class)

**Acceptance Criteria:**
- [x] Existing preview behavior unchanged
- [x] Code path routes through session abstraction

**Implementation Notes:**
- `PreviewSession` manages backend lifecycle and dirty flag tracking
- `IPreviewBackend` defines unified interface for both Authoring and ECS modes
- `PlayableGraphBackend` wraps existing PlayableGraph-based preview
- `PreviewBackendBase` provides shared camera state management

### Phase 3: Rig-Aware Preview ✅ COMPLETE

**Goal:** Preview uses BoundArmatureData deterministically

**Changes:**
- Preview window reads `StateMachineAsset.BoundArmatureData`
- If bound: use it (derive preview model from avatar's owner asset)
- If not bound: show appropriate UI based on status
- Add "Assign Rig" button when unresolved

**Files modified:**
- `Editor/EditorWindows/AnimationPreviewWindow.cs`
- `Editor/EditorWindows/Preview/PreviewRenderer.cs`

**Acceptance Criteria:**
- [x] Bound rig used without heuristics
- [x] Unbound asset shows clear UI
- [x] User can assign rig from preview window

**Implementation Notes:**
- Preview model selection persists per-asset via EditorPrefs (`DMotion.AnimationPreview.PreviewModel.{guid}`)
- ObjectField in toolbar allows direct model assignment
- Model extracted from clips when no explicit selection
- Camera state preserved across model/mode changes

### Phase 4: Asset Creation Workflow ⏸️ DEFERRED (Low Priority)

**Goal:** "Create DMotion Controller from Rig" context menu

**Changes:**
- Add context menu on `UnityEngine.Avatar` (and later other armature types):
  - `Create/DMotion/State Machine Asset`
- Creates new `StateMachineAsset` with:
  - `BoundArmatureData = selected`
  - `RigBindingStatus = Resolved`
  - `RigBindingSource = UserSelected`

**Files to add/modify:**
- `Editor/CustomEditors/ArmatureContextMenu.cs` (new)

**Acceptance Criteria:**
- [ ] Context menu appears on Avatar assets (including sub-assets)
- [ ] Created asset has correct binding
- [ ] Preview works immediately without prompts

**Deferral Reason:**
- Nice-to-have workflow enhancement
- Current workflow (create asset, assign model in preview) is functional
- Can be added later if requested

### Phase 5: ECS Preview World Service ✅ COMPLETE

**Goal:** Isolated ECS world, only when preview window open

**New types:**
- `EcsPreviewWorldService` (Editor assembly)
  - `CreateWorld()` / `DestroyWorld()`
  - `Update(deltaTime)` / `UpdateWhilePaused()`
  - `CompleteJobs()`
  - `GetSnapshot()` -> `PreviewSnapshot`

**PreviewSnapshot DTO:**
- Active state index/path
- Transition info (from/to/progress)
- Blend weights/position
- Normalized time
- Parameter values (for display)

**Update policy:**
- Playing: `Update(dt)` continuously
- Paused: `UpdateWhilePaused()` checks dirty flags, updates only if changed
- Always: `CompleteJobs()` before rendering/snapshot

**Files added:**
- `Editor/EditorWindows/Preview/EcsPreviewWorldService.cs`
- `Editor/EditorWindows/Preview/EcsPreviewBackend.cs`
- `Editor/EditorWindows/Preview/EcsPreviewSceneManager.cs`
- `Editor/EditorWindows/Preview/EcsEntityBrowser.cs`
- `Editor/EditorWindows/Preview/TimelineControlHelper.cs`

**Acceptance Criteria:**
- [x] World created on window open, destroyed on close
- [x] No world exists when window closed
- [x] Jobs complete before rendering (no TempAlloc warnings)
- [x] Snapshot data matches runtime behavior

**Implementation Notes:**
- `EcsPreviewWorldService` creates isolated LatiosWorld with Kinemation systems
- `EcsEntityBrowser` provides live entity selection from Play Mode world
- `EcsPreviewSceneManager` handles automatic SubScene setup for ECS preview
- `TimelineControlHelper` bridges editor UI to ECS timeline control components
- Supports both isolated world (editor-only) and live world (Play Mode) modes

### Phase 6: ECS Preview Rendering ✅ COMPLETE

**Goal:** Full pose rendering driven by ECS

**Approach (phased):**

**6A: Pose correctness (internal validation)** ✅
- Verify ECS-evaluated pose is correct in memory
- Debug snapshot shows expected state/time/blend values

**6B: Render via PreviewRenderUtility (lower risk)** ✅
- Extract pose from ECS world
- Apply to preview skeleton instance
- Bake mesh and draw via existing PreviewRenderUtility path
- Avoids BRG draw command systems

**6C: Optional Entities Graphics/BRG (higher risk, later)** ⏸️ DEFERRED
- Only if true runtime render parity is required
- Requires careful system selection to avoid allocation issues

**Files added:**
- `Editor/EditorWindows/Preview/EcsHybridRenderer.cs`
- `Editor/EditorWindows/Preview/TransitionTimeline.cs`

**Acceptance Criteria:**
- [x] ECS preview shows animated character
- [x] Pose matches Play Mode output for equivalent setup
- [x] No JobTempAlloc warnings during preview

**Implementation Notes:**
- `EcsHybridRenderer` uses PlayableGraph for pose sampling with ECS-driven state/timing
- Hybrid approach: ECS drives state machine logic, PlayableGraph samples poses
- `TransitionTimeline` provides visual timeline UI with ghost bars for transition visualization
- Camera state, zoom, orbit controls integrated with PreviewRenderUtility

### Phase 7: Mode Toggle UI ✅ COMPLETE

**Goal:** User can switch between Authoring and ECS preview

**Changes:**
- Add mode dropdown to preview window toolbar:
  - `Authoring (PlayableGraph)` - default
  - `ECS Runtime`
- Mode persists in EditorPrefs per asset (or globally)
- Switching modes preserves selection/time

**Files modified:**
- `Editor/EditorWindows/AnimationPreviewWindow.cs`

**Acceptance Criteria:**
- [x] Mode switch is visible and functional
- [x] Both modes produce correct output
- [x] No data loss on mode switch

**Implementation Notes:**
- Mode toggle in toolbar dropdown menu (Authoring / ECS Runtime)
- Mode persists globally via EditorPrefs (`DMotion.AnimationPreview.PreviewMode`)
- Camera state preserved across mode switches via `PreviewSession`
- State/transition selection preserved when switching modes

### Phase 8: Mechination Integration ⏸️ OUT OF SCOPE (Mechination Repo)

**Goal:** Mechination sets rig binding during conversion

**Changes (in mechination repo):**
- Implement rig resolution algorithm (see above)
- Compute and store fingerprint
- Prompt UI when unresolved (UI design TBD)
- Write `BoundArmatureData`, `RigBindingStatus`, `RigBindingSource`, `RigBindingFingerprint` to output asset

**Prompt memory:**
- Check existing status + fingerprint before prompting
- Only re-prompt if fingerprint changed AND new binding available

**Acceptance Criteria:**
- [ ] Converted assets have correct rig binding when resolvable
- [ ] Unresolved assets prompt user
- [ ] Opted-out assets don't re-prompt unless state changes
- [ ] Fingerprint detects new bindings correctly

**Note:** This phase is implemented in the Mechination repository, not DMotion.
The DMotion-side API (rig binding fields, enums) is complete in Phase 1.

---

## Type Extensibility

To support armature data types beyond `UnityEngine.Avatar`:

**Future interface:**
```csharp
public interface IArmatureDataAdapter
{
    bool CanHandle(UnityEngine.Object armatureData);
    bool ValidateCompatibility(UnityEngine.Object armatureData, StateMachineAsset asset);
    void SetupPreviewEntity(World world, Entity entity, UnityEngine.Object armatureData);
    GameObject GetDefaultPreviewModel(UnityEngine.Object armatureData);
}
```

**Implementations:**
- `AvatarArmatureAdapter` (Unity Avatar) - ships with DMotion
- `KinemationArmatureAdapter` (Latios rig assets) - future
- Custom studio adapters - extensible

---

## Validation Strategy

### Boot validation
- Start Unity editor fresh
- Confirm no Kinemation BRG JobTempAlloc warnings in console

### Functional validation
- Open workspace (state editor + preview)
- Switch between Authoring and ECS preview modes
- Play/pause/scrub produces stable results
- Parameter changes while paused trigger refresh

### Leak validation
- Open/close preview window repeatedly
- Ensure preview world count returns to baseline
- Jobs completed on close (no lingering allocations)

### Mechination validation
- Convert controller with clear rig -> binding set automatically
- Convert controller with ambiguous rig -> prompt appears
- Opt out -> no re-prompt on rebake
- Change avatar reference -> re-prompt on next bake

---

## Files Summary

### New Files
- `Editor/EditorWindows/Preview/PreviewSession.cs`
- `Editor/EditorWindows/Preview/IPreviewBackend.cs`
- `Editor/EditorWindows/Preview/PlayableGraphBackend.cs`
- `Editor/EditorWindows/Preview/EcsPreviewBackend.cs`
- `Editor/EditorWindows/Preview/EcsPreviewWorldService.cs`
- `Editor/EditorWindows/Preview/PreviewSnapshot.cs`
- `Editor/CustomEditors/ArmatureContextMenu.cs`

### Modified Files
- `Runtime/Authoring/AnimationStateMachine/StateMachineAsset.cs` (add rig binding fields)
- `Editor/EditorWindows/AnimationPreviewWindow.cs` (session integration, mode toggle)
- `Editor/EditorWindows/Preview/PreviewRenderer.cs` (rig-aware resolution)
- `Tests/Core/TestBootstrap.cs` (ensure test-only activation)

### Mechination Files (separate repo)
- `Editor/Conversion/DMotionAssetBuilder.cs` (rig resolution + fingerprint)
- TBD: prompt UI

---

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| BRG systems cause allocation warnings in ECS preview | Start with PreviewRenderUtility path; only add BRG later if needed |
| Test bootstraps still hijack editor after changes | Verify with fresh editor launch; add CI check |
| Mechination fingerprint doesn't detect all changes | Include controller GUID + clip list in fingerprint |
| Multiple armature types cause type confusion | Use `UnityEngine.Object` + adapter pattern |
| Preview world leaks on domain reload | Subscribe to `AssemblyReloadEvents`; dispose in `OnDisable` |

---

## Dependencies

- Unity 6000.0+
- Latios Framework (Kinemation)
- Unity.Entities
- Mechination (for conversion integration)

---

## Open Items (Deferred)

- Mechination prompt UI design (decided later when implementing Phase 8)
- Entities Graphics/BRG preview rendering (Phase 6C, optional)
- Custom armature adapter implementations beyond Avatar

---

## Implementation Sequencing

### Prerequisites (from AnimationPreviewWindow.md)
Before starting ECS preview work, complete these remaining authoring preview phases:
- **Phase 7**: Blended 3D Preview (`BlendedClipPreview`)
- **Phase 8**: Transition 3D Preview (`TransitionPreview`)

### Recommended Order
1. **Phase 0** (this doc): Boot-time cleanup - can start immediately, no dependencies
2. **Phase 1** (this doc): Rig binding data model - can start immediately
3. Complete AnimationPreviewWindow Phases 7-8 (authoring preview)
4. **Phase 2-7** (this doc): ECS preview implementation
5. **Phase 8** (this doc): Mechination integration

---

## Related Documents

- **[AnimationPreviewWindow.md](./AnimationPreviewWindow.md)** - Authoring preview implementation (foundation for this work)
- **[UIToolkitMigration.md](./UIToolkitMigration.md)** - UIToolkit migration audit (✅ Complete)
- **[TransitionBlendCurve.md](./TransitionBlendCurve.md)** - Transition curve runtime support (Planned)
