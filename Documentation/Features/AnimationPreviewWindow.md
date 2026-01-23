# Animation Preview Window

## Status: In Development (Phases 1-8 Complete)
## Priority: High
## Estimated Phases: 10

---

## Recent Changes (Session Summary)

### Completed Refactoring
The window has been refactored from a monolithic ~1900 line file into a clean architecture:

```
Editor/EditorWindows/AnimationPreviewWindow.cs (763 lines) - Main coordinator
Editor/EditorWindows/Preview/
├── AnimationPreviewEvents.cs (230 lines) - Centralized event system
├── IUIElementFactory.cs (58 lines) - UI factory interface
├── PreviewRenderer.cs (252 lines) - 3D preview management
├── PreviewWindowConstants.cs (69 lines) - Magic numbers centralized
├── StateInspectorBuilder.cs (277 lines) - State inspector coordinator
├── TimelineScrubber.cs (654 lines) - Timeline UI
├── TransitionInspectorBuilder.cs (314 lines) - Transition inspector
└── StateContent/
    ├── IStateContentBuilder.cs (86 lines) - Interface + context
    ├── BlendContentBuilderBase.cs (343 lines) - Shared blend UI logic
    ├── SingleClipContentBuilder.cs (78 lines) - Single clip states
    ├── LinearBlendContentBuilder.cs (173 lines) - 1D blend states
    └── Directional2DBlendContentBuilder.cs (224 lines) - 2D blend states
```

### Menu Reorganization
- Moved from `Tools/DMotion` to `Window/DMotion`
- Added `Window/DMotion/Open Workspace` (opens editor + preview together)
- Double-clicking a StateMachineAsset now opens the full workspace

### Architecture Patterns Used
1. **Coordinator Pattern**: AnimationPreviewWindow delegates to builders
2. **Strategy Pattern**: `IStateContentBuilder` for state-specific UI
3. **Template Method Pattern**: `BlendContentBuilderBase` with abstract methods
4. **Centralized Events**: `AnimationPreviewEvents` for cross-component communication

---

## Overview

A context-sensitive preview window that provides real-time visual feedback for animation states and transitions in DMotion. The window adapts its content based on the selected element in the State Machine Editor.

### Core Principles

1. **Context-Sensitive** - Content changes based on what's selected (state/transition)
2. **ECS-Focused** - Changes persist to assets (no runtime-only edits)
3. **Visual-First** - 3D preview is the primary feedback mechanism
4. **Non-Destructive Preview** - Blend space indicator is draggable, clip positions are view-only by default

---

## Architecture

### Components

| Component | Responsibility |
|-----------|---------------|
| `AnimationPreviewWindow` | Main EditorWindow, layout, mode switching |
| `AnimationPreviewRenderer` | 3D preview using `PreviewRenderUtility` |
| `TimelineScrubber` | VisualElement for timeline + event markers |
| `BlendSpacePreview1D` | 1D blend space visualization |
| `BlendSpacePreview2D` | 2D blend space visualization |
| `StatePreviewController` | Orchestrates preview for state selection |
| `TransitionPreviewController` | Orchestrates preview for transition selection |

### Class Hierarchy

```
PlayableGraphPreview (existing)
├── SingleClipPreview (existing)
├── BlendedClipPreview (new) ── for blend states
└── TransitionPreview (new) ── for transition blending
```

### File Structure (Current Implementation)

```
Editor/
├── EditorWindows/
│   ├── AnimationPreviewWindow.cs          # Main window coordinator (763 lines)
│   ├── AnimationPreviewWindow.uss         # Styles
│   └── Preview/
│       ├── AnimationPreviewEvents.cs      # Centralized event system
│       ├── IUIElementFactory.cs           # UI factory interface
│       ├── PreviewRenderer.cs             # 3D preview management
│       ├── PreviewWindowConstants.cs      # Magic numbers centralized
│       ├── StateInspectorBuilder.cs       # State inspector coordinator
│       ├── TimelineScrubber.cs            # Timeline VisualElement (654 lines)
│       ├── TransitionInspectorBuilder.cs  # Transition inspector
│       └── StateContent/
│           ├── IStateContentBuilder.cs            # Interface + context
│           ├── BlendContentBuilderBase.cs         # Shared blend UI logic
│           ├── SingleClipContentBuilder.cs        # Single clip states
│           ├── LinearBlendContentBuilder.cs       # 1D blend states
│           └── Directional2DBlendContentBuilder.cs # 2D blend states
├── EditorPreview/
│   ├── PlayableGraphPreview.cs            # Base preview class (existing)
│   ├── SingleClipPreview.cs               # Single clip preview (existing)
│   ├── BlendedClipPreview.cs              # (TODO - Phase 7)
│   └── TransitionPreview.cs               # (TODO - Phase 8)
```

---

## Implementation Phases

### Phase 1: Foundation & Layout Refactor ✅ COMPLETE
**Goal:** Clean slate with proper architecture for the new design.

- [x] Delete current `AnimationPreviewWindow.cs` content
- [x] Create new window with resizable split layout
- [x] Left panel: Inspector area (properties, timeline, blend space)
- [x] Right panel: 3D preview area (bottom-right positioning)
- [x] Implement resizable divider with persistence
- [x] Add minimum size constraints
- [x] "No Preview Available" placeholder for 3D area
- [x] Subscribe to `StateMachineEditorEvents` for selection
- [x] Context switching based on selection type (state/transition/none)

**Acceptance Criteria:**
- [x] Window opens with correct layout
- [x] Resizing works and persists across sessions
- [x] Selection changes update the inspector content area

---

### Phase 2: Editable State Properties ✅ COMPLETE
**Goal:** Allow editing Speed/Loop directly in the preview window.

- [x] Create properties section for selected state
- [x] Speed slider (0.0 - 3.0 range, editable)
- [x] Loop toggle checkbox
- [x] Wire to `SerializedObject` for the state asset
- [x] Apply changes with Undo support
- [x] Mark asset dirty on change
- [x] Show state name and type in header

**Acceptance Criteria:**
- [x] Selecting a state shows its properties
- [x] Changing Speed/Loop modifies the actual asset
- [x] Undo/Redo works correctly
- [x] Asset shows as dirty (needs save)

---

### Phase 3: Timeline Scrubber ✅ COMPLETE
**Goal:** Visual timeline with draggable playhead for animation time control.

- [x] Create `TimelineScrubber` VisualElement
- [x] Horizontal track with current time indicator
- [x] Draggable handle for scrubbing
- [x] Time display: current / total (e.g., "1.2s / 3.0s")
- [x] Normalized time display (0% - 100%)
- [x] Event markers (visual dots/lines at event times)
- [x] Tooltip on event marker hover showing event name
- [x] Play/Pause button with auto-advance time
- [x] Loop indicator when state has Loop enabled

**Acceptance Criteria:**
- [x] Timeline shows correct duration for selected clip
- [x] Dragging scrubber updates time display
- [x] Event markers appear at correct positions
- [x] Events tooltip shows event name

---

### Phase 4: 3D Model Preview (Single Clip) ✅ COMPLETE
**Goal:** Render animated 3D model for single-clip states.

- [x] Integrate existing `SingleClipPreview` class
- [x] Create `IMGUIContainer` in right panel for preview rendering
- [x] Auto-extract model from AnimationClip source avatar
- [x] Sync preview time with timeline scrubber
- [x] Camera orbit controls (drag to rotate)
- [x] Zoom controls (scroll wheel)
- [x] Reset view button
- [x] Handle missing model gracefully (show message)
- [x] Dispose preview properly on selection change

**Acceptance Criteria:**
- [x] Selecting a SingleClipState shows 3D preview
- [x] Timeline scrubbing updates the pose in real-time
- [x] Camera can be rotated and zoomed
- [x] No errors when clip has no associated model

---

### Phase 5: Blend Space Visualizer (1D) ✅ COMPLETE
**Goal:** Interactive 1D blend space for LinearBlendStateAsset.

- [x] Create `BlendSpacePreview1D` VisualElement (via `LinearBlendContentBuilder`)
- [x] Horizontal track showing blend range
- [x] Clip position circles at threshold values
- [x] Clip names as labels (above or below circles)
- [x] Draggable indicator circle for current blend position
- [ ] Toggle button: "Edit Clip Positions" (deferred - view-only for now)
- [ ] When editing enabled, clip circles become draggable (deferred)
- [x] Show current parameter value
- [x] Visual feedback for active clips (highlight contributing clips)
- [ ] Clip selector dropdown: "Blended" + individual clips (deferred to Phase 9)

**Acceptance Criteria:**
- [x] 1D blend states show the blend space track
- [x] Dragging indicator updates blend position
- [x] Clip positions are visible
- [ ] Toggle enables clip position editing (deferred)
- [ ] Dropdown allows previewing individual clips (deferred)

---

### Phase 6: Blend Space Visualizer (2D) ✅ COMPLETE
**Goal:** Interactive 2D blend space for Directional2DBlendStateAsset.

- [x] Create `BlendSpacePreview2D` VisualElement (via `Directional2DBlendContentBuilder`)
- [x] 2D grid with X/Y axes
- [x] Clip position circles at (X, Y) coordinates
- [x] Clip names as labels near circles
- [x] Draggable indicator for current blend position
- [ ] Toggle button: "Edit Clip Positions" (deferred - view-only for now)
- [x] Grid lines and axis labels
- [x] Show current X/Y parameter values
- [x] Visual feedback for contributing clips (size/opacity based on weight)
- [ ] Zoom and pan controls for large blend spaces (deferred)

**Acceptance Criteria:**
- [x] 2D blend states show the blend space grid
- [x] Dragging indicator updates X and Y parameters
- [x] Clip positions visible
- [ ] Editable when toggled (deferred)
- [x] Contributing clips visually indicated

---

### Phase 7: Blended 3D Preview ✅ COMPLETE
**Goal:** 3D preview showing blended animation result.

- [x] Create `BlendedClipPreview` class extending `PlayableGraphPreview`
- [x] Build PlayableGraph with `AnimationMixerPlayable`
- [x] Calculate blend weights from parameter position
- [x] 1D: Linear interpolation between adjacent thresholds
- [x] 2D: Inverse distance weighting (approximates Unity's algorithm)
- [x] Update mixer weights when blend position changes
- [x] Sync with timeline for time-based scrubbing
- [x] Handle edge cases (single clip, out of range)
- [x] Smooth blend position transitions (critically damped spring)

**Acceptance Criteria:**
- [x] Dragging blend space indicator updates 3D preview in real-time
- [x] Blend weights correctly interpolate between clips
- [x] Timeline scrubbing works with blending
- [x] Approximates Unity's blend behavior (inverse distance weighting)

---

### Phase 8: Transition Preview ✅ COMPLETE
**Goal:** Preview transition blending between two states.

- [x] Create `TransitionInspectorBuilder` (replaces TransitionPreviewController)
- [x] Show "From State -> To State" header
- [x] Editable Duration slider (modifies asset)
- [x] Transition progress slider (0% - 100%)
- [x] Create `TransitionPreview` class for blended playback
- [x] Blend between from-state pose and to-state pose
- [x] Show relevant condition parameters only
- [ ] Condition parameter editing (for testing) - deferred
- [ ] Visual indicator of transition in 3D preview - deferred

**Acceptance Criteria:**
- [x] Selecting a transition shows its properties
- [x] Duration editing modifies the asset
- [x] Progress slider blends between states in 3D
- [x] Only condition-relevant parameters shown

---

### Phase 9: Individual Clip Preview Mode
**Goal:** Allow previewing individual clips within a blend state.

- [ ] Add "Preview Clip" dropdown to blend state UI
- [ ] Options: "Blended (Default)" + list of individual clips
- [ ] When individual clip selected:
  - Hide blend space indicator
  - Show only that clip in 3D preview
  - Timeline reflects that clip's duration
- [ ] Highlight selected clip in blend space
- [ ] Quick switch back to blended mode

**Acceptance Criteria:**
- Dropdown allows selecting individual clips
- 3D preview shows only selected clip
- Easy to switch back to blended view

---

### Phase 10: Polish & UX
**Goal:** Final refinements for production quality.

- [ ] Minimum window size constraints
- [ ] Divider position persistence (EditorPrefs)
- [ ] Keyboard shortcuts:
  - Space: Play/Pause
  - Home: Go to start
  - End: Go to end
- [ ] Playback controls (Play/Pause/Stop buttons)
- [ ] Playback speed control
- [ ] Auto-play toggle (auto-advance time)
- [ ] Dark/light theme support in USS
- [ ] Performance optimization (throttle updates)
- [ ] Error handling and user feedback
- [ ] Help tooltips

**Acceptance Criteria:**
- Window feels polished and responsive
- Keyboard shortcuts work
- No performance issues during scrubbing
- Proper error messages for edge cases

---

## Technical Considerations

### Model Extraction
Use existing `TryFindSkeletonFromClip` in `PlayableGraphPreview`:
1. Get AnimationClip asset path
2. Load ModelImporter, get sourceAvatar
3. Get Avatar asset path, load GameObject
4. Validate has Animator component

### Blend Weight Calculation

**1D Linear Blend:**
```csharp
// Find surrounding clips
// weight = (value - lower.threshold) / (upper.threshold - lower.threshold)
// lower.weight = 1 - weight
// upper.weight = weight
```

**2D Directional Blend:**
- Use gradient band interpolation
- Reference Unity's BlendTree algorithm
- Consider using polar coordinates for directional blends

### Asset Modification Pattern
```csharp
var serializedObject = new SerializedObject(stateAsset);
var speedProp = serializedObject.FindProperty("Speed");
speedProp.floatValue = newSpeed;
serializedObject.ApplyModifiedProperties();
// Undo automatically recorded
```

### Performance
- Only render preview when window is visible/focused
- Throttle preview updates during rapid scrubbing (16ms debounce)
- Dispose PlayableGraph when switching selections
- Use object pooling for preview instances if needed

---

## UI Mockups

### Single Clip State
```
┌─────────────────────────────────────────────┐
│ [Dynamic ▼]                          Idle   │
├─────────────────────────┬───────────────────┤
│ ▼ Properties            │                   │
│   Speed  [====●====] 1.0│                   │
│   Loop   [✓]            │                   │
├─────────────────────────┤                   │
│ ▼ Timeline              │   ┌───────────┐   │
│   [|----●---------]     │   │           │   │
│   1.2s / 3.0s           │   │  3D Model │   │
│   ▲         ▲           │   │  Preview  │   │
│   step    footfall      │   │           │   │
│                         │   └───────────┘   │
└─────────────────────────┴───────────────────┘
```

### 1D Blend State
```
┌─────────────────────────────────────────────┐
│ [Dynamic ▼]                   LocomotionBlend│
├─────────────────────────┬───────────────────┤
│ ▼ Properties            │                   │
│   Speed  [====●====] 1.0│                   │
│   Loop   [✓]            │                   │
├─────────────────────────┤                   │
│ ▼ Blend Space           │   ┌───────────┐   │
│   Clip: [Blended     ▼] │   │           │   │
│   ○───○───●───○───○     │   │  3D Model │   │
│   idle walk ▲ run sprint│   │  Preview  │   │
│   [ ] Edit Positions    │   │           │   │
├─────────────────────────┤   └───────────┘   │
│ ▼ Blend Parameter       │                   │
│   MoveSpeed [===●===]0.5│                   │
└─────────────────────────┴───────────────────┘
```

### 2D Blend State
```
┌─────────────────────────────────────────────┐
│ [Dynamic ▼]                    MovementBlend │
├─────────────────────────┬───────────────────┤
│ ▼ Properties            │                   │
│   Speed  [====●====] 1.0│                   │
│   Loop   [✓]            │                   │
├─────────────────────────┤                   │
│ ▼ Blend Space 2D        │   ┌───────────┐   │
│   Clip: [Blended     ▼] │   │           │   │
│   ┌─────────────────┐   │   │  3D Model │   │
│   │ ○       ○       │   │   │  Preview  │   │
│   │     ●           │   │   │           │   │
│   │ ○       ○       │   │   └───────────┘   │
│   └─────────────────┘   │                   │
│   [ ] Edit Positions    │                   │
├─────────────────────────┤                   │
│ ▼ Blend Parameters      │                   │
│   VelX [===●===] 0.3    │                   │
│   VelY [===●===] 0.1    │                   │
└─────────────────────────┴───────────────────┘
```

### Transition Selected
```
┌─────────────────────────────────────────────┐
│ [Dynamic ▼]                   Idle → Walk   │
├─────────────────────────┬───────────────────┤
│ ▼ Properties            │                   │
│   Duration [==●==] 0.25s│                   │
├─────────────────────────┤                   │
│ ▼ Transition Progress   │   ┌───────────┐   │
│   [|=====●======] 50%   │   │           │   │
│   From: Idle            │   │  3D Model │   │
│   To: Walk              │   │  Preview  │   │
├─────────────────────────┤   │ (blending)│   │
│ ▼ Conditions            │   └───────────┘   │
│   IsMoving  [✓]         │                   │
│   Speed > [===●===] 0.1 │                   │
└─────────────────────────┴───────────────────┘
```

---

## Future Enhancements (Out of Scope)

- [ ] Event marker editing (add/remove/rename)
- [ ] Animation curve visualization
- [ ] Root motion preview toggle
- [ ] Multiple model preview (A/B comparison)
- [ ] Recording/export preview as video
- [ ] Onion skinning (ghost frames)
- [ ] IK preview

---

## Dependencies

- Existing `PlayableGraphPreview` infrastructure
- Existing `SingleClipPreview` class
- `StateMachineEditorEvents` for selection tracking
- Unity's `PreviewRenderUtility`
- Unity's `AnimationMode` for sampling

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Model extraction fails for some clips | Show clear error message, allow manual model selection (future) |
| Performance issues with complex blends | Throttle updates, optimize weight calculation |
| 2D blend algorithm complexity | Start with simple barycentric, refine later |
| Preview rendering in UI Toolkit | Use IMGUIContainer as bridge (proven pattern) |

---

## Definition of Done

- [ ] All phases completed and tested
- [ ] No compilation errors or warnings
- [ ] Works with all state types (Single, 1D Blend, 2D Blend)
- [ ] Works with transitions
- [ ] Asset changes persist correctly
- [ ] Undo/Redo works throughout
- [ ] Performance acceptable (60fps during scrubbing)
- [ ] Documentation updated

---

## What's Next (Priority Order)

### Authoring Preview Complete (Phases 1-8)
- Blended 3D Preview and Transition Preview are fully implemented
- `BlendedClipPreview` and `TransitionPreview` classes handle PlayableGraph blending

### Ready for ECS Preview Integration
1. **ECS Preview Integration** - See `EcsPreviewAndRigBinding.md` for the ECS preview world feature plan
   - Phase 0 (Boot-Time Cleanup) ✅ COMPLETE
   - Phase 1 (Rig Binding Data Model) ✅ COMPLETE
   - Phases 2-7: ECS preview world, mode toggle, Mechination integration

### Deferred Polish
2. **Phase 9: Individual Clip Preview Mode**
3. **Phase 10: Polish & UX**
4. **Clip position editing** (currently view-only)
5. **Zoom/pan for 2D blend spaces**
6. **Condition parameter editing** in transition inspector

---

## Related Documents

- **[EcsPreviewAndRigBinding.md](./EcsPreviewAndRigBinding.md)** - ECS preview world + rig binding feature plan (builds on this work)
- **[UIToolkitMigration.md](./UIToolkitMigration.md)** - UIToolkit migration audit (✅ Complete)
- **[TransitionBlendCurve.md](./TransitionBlendCurve.md)** - Transition curve runtime support (Planned)
