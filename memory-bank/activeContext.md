# Active Context

## Current Focus
**Implement Animation Transition Offsets** (Runtime Logic Completed)

## Recent Changes
- **Runtime Implementation:**
    - Modified `AnimationState.cs` to support `initialTime`.
    - Updated `UpdateStateMachineJob.cs` to read `Offset` from blobs.
    - Implemented logic in `SingleClipStateUtils`, `LinearBlendStateUtils`, `Directional2DBlendStateUtils`.
    - Created `AnimationTimeUtils` for shared time/duration logic.
- **Edge Cases:**
    - Handled looping (wrap) vs non-looping (clamp) for offsets.
    - Implemented "effective duration" calculation for blend states to ensure correct time mapping.

## Active Status
- **Build:** Compiles successfully.
- **Tests:** `TransitionOffsetIntegrationTests` created (placeholder).
- **Git:** All changes committed to `feature/ecs-preview-infrastructure`.

## Next Steps
1. **Verify:** Manual verification in Unity Editor (Authoring -> Runtime).
2. **Future:** Sync feature integration (Phase 2).
3. **Tests:** Flesh out integration tests with proper blob mocking.

## Outstanding Questions
- Interaction with "Sync" feature (Phase 2) needs future verification.

## Useful Commands
- Build: (Standard Unity Build)
- Tests: (Unity Test Runner)
