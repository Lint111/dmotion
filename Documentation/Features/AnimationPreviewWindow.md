# Animation Preview Window

## Status: In Development
## Priority: High
## Estimated Phases: 10

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

### File Structure

```
Editor/
├── EditorWindows/
│   ├── AnimationPreviewWindow.cs      # Main window
│   ├── AnimationPreviewWindow.uss     # Styles
│   └── Preview/
│       ├── TimelineScrubber.cs        # Timeline VisualElement
│       ├── BlendSpacePreview1D.cs     # 1D blend space
│       ├── BlendSpacePreview2D.cs     # 2D blend space
│       ├── StatePreviewController.cs  # State preview logic
│       └── TransitionPreviewController.cs
├── EditorPreview/
│   ├── PlayableGraphPreview.cs        # (existing)
│   ├── SingleClipPreview.cs           # (existing)
│   ├── BlendedClipPreview.cs          # (new)
│   └── TransitionPreview.cs           # (new)
```

---

## Implementation Phases

### Phase 1: Foundation & Layout Refactor
**Goal:** Clean slate with proper architecture for the new design.

- [ ] Delete current `AnimationPreviewWindow.cs` content
- [ ] Create new window with resizable split layout
- [ ] Left panel: Inspector area (properties, timeline, blend space)
- [ ] Right panel: 3D preview area (bottom-right positioning)
- [ ] Implement resizable divider with persistence
- [ ] Add minimum size constraints
- [ ] "No Preview Available" placeholder for 3D area
- [ ] Subscribe to `StateMachineEditorEvents` for selection
- [ ] Context switching based on selection type (state/transition/none)

**Acceptance Criteria:**
- Window opens with correct layout
- Resizing works and persists across sessions
- Selection changes update the inspector content area

---

### Phase 2: Editable State Properties
**Goal:** Allow editing Speed/Loop directly in the preview window.

- [ ] Create properties section for selected state
- [ ] Speed slider (0.0 - 3.0 range, editable)
- [ ] Loop toggle checkbox
- [ ] Wire to `SerializedObject` for the state asset
- [ ] Apply changes with Undo support
- [ ] Mark asset dirty on change
- [ ] Show state name and type in header

**Acceptance Criteria:**
- Selecting a state shows its properties
- Changing Speed/Loop modifies the actual asset
- Undo/Redo works correctly
- Asset shows as dirty (needs save)

---

### Phase 3: Timeline Scrubber
**Goal:** Visual timeline with draggable playhead for animation time control.

- [ ] Create `TimelineScrubber` VisualElement
- [ ] Horizontal track with current time indicator
- [ ] Draggable handle for scrubbing
- [ ] Time display: current / total (e.g., "1.2s / 3.0s")
- [ ] Normalized time display (0% - 100%)
- [ ] Event markers (visual dots/lines at event times)
- [ ] Tooltip on event marker hover showing event name
- [ ] Play/Pause button (future: auto-advance time)
- [ ] Loop indicator when state has Loop enabled

**Acceptance Criteria:**
- Timeline shows correct duration for selected clip
- Dragging scrubber updates time display
- Event markers appear at correct positions
- Events tooltip shows event name

---

### Phase 4: 3D Model Preview (Single Clip)
**Goal:** Render animated 3D model for single-clip states.

- [ ] Integrate existing `SingleClipPreview` class
- [ ] Create `IMGUIContainer` in right panel for preview rendering
- [ ] Auto-extract model from AnimationClip source avatar
- [ ] Sync preview time with timeline scrubber
- [ ] Camera orbit controls (drag to rotate)
- [ ] Zoom controls (scroll wheel)
- [ ] Reset view button
- [ ] Handle missing model gracefully (show message)
- [ ] Dispose preview properly on selection change

**Acceptance Criteria:**
- Selecting a SingleClipState shows 3D preview
- Timeline scrubbing updates the pose in real-time
- Camera can be rotated and zoomed
- No errors when clip has no associated model

---

### Phase 5: Blend Space Visualizer (1D)
**Goal:** Interactive 1D blend space for LinearBlendStateAsset.

- [ ] Create `BlendSpacePreview1D` VisualElement
- [ ] Horizontal track showing blend range
- [ ] Clip position circles at threshold values
- [ ] Clip names as labels (above or below circles)
- [ ] Draggable indicator circle for current blend position
- [ ] Toggle button: "Edit Clip Positions" (default: off)
- [ ] When editing enabled, clip circles become draggable
- [ ] Show current parameter value
- [ ] Visual feedback for active clips (highlight contributing clips)
- [ ] Clip selector dropdown: "Blended" + individual clips

**Acceptance Criteria:**
- 1D blend states show the blend space track
- Dragging indicator updates blend position
- Clip positions are visible but not editable by default
- Toggle enables clip position editing
- Dropdown allows previewing individual clips

---

### Phase 6: Blend Space Visualizer (2D)
**Goal:** Interactive 2D blend space for Directional2DBlendStateAsset.

- [ ] Create `BlendSpacePreview2D` VisualElement
- [ ] 2D grid with X/Y axes
- [ ] Clip position circles at (X, Y) coordinates
- [ ] Clip names as labels near circles
- [ ] Draggable indicator for current blend position
- [ ] Toggle button: "Edit Clip Positions"
- [ ] Grid lines and axis labels
- [ ] Show current X/Y parameter values
- [ ] Visual feedback for contributing clips (size/opacity based on weight)
- [ ] Zoom and pan controls for large blend spaces

**Acceptance Criteria:**
- 2D blend states show the blend space grid
- Dragging indicator updates X and Y parameters
- Clip positions visible, editable when toggled
- Contributing clips visually indicated

---

### Phase 7: Blended 3D Preview
**Goal:** 3D preview showing blended animation result.

- [ ] Create `BlendedClipPreview` class extending `PlayableGraphPreview`
- [ ] Build PlayableGraph with `AnimationMixerPlayable`
- [ ] Calculate blend weights from parameter position
- [ ] 1D: Linear interpolation between adjacent thresholds
- [ ] 2D: Gradient band interpolation (Unity's algorithm)
- [ ] Update mixer weights when blend position changes
- [ ] Sync with timeline for time-based scrubbing
- [ ] Handle edge cases (single clip, out of range)

**Acceptance Criteria:**
- Dragging blend space indicator updates 3D preview in real-time
- Blend weights correctly interpolate between clips
- Timeline scrubbing works with blending
- Matches Unity's blend behavior

---

### Phase 8: Transition Preview
**Goal:** Preview transition blending between two states.

- [ ] Create `TransitionPreviewController`
- [ ] Show "From State -> To State" header
- [ ] Editable Duration slider (modifies asset)
- [ ] Transition progress slider (0% - 100%)
- [ ] Create `TransitionPreview` class for blended playback
- [ ] Blend between from-state pose and to-state pose
- [ ] Show relevant condition parameters only
- [ ] Condition parameter editing (for testing)
- [ ] Visual indicator of transition in 3D preview

**Acceptance Criteria:**
- Selecting a transition shows its properties
- Duration editing modifies the asset
- Progress slider blends between states in 3D
- Only condition-relevant parameters shown

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
