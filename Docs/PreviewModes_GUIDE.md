# Animation Preview Modes

DMotion provides two preview modes for visualizing animation states and transitions in the editor. Each mode has distinct characteristics suited for different workflows.

## Quick Reference

| Feature | Authoring Mode | ECS Runtime Mode |
|---------|---------------|------------------|
| Speed | Fast | Moderate |
| Accuracy | Approximate | Runtime-accurate |
| Setup Required | None | Play Mode or SubScene |
| Best For | Quick iteration | Debugging runtime behavior |
| Blend Curves | Simulated | Actual runtime curves |
| State Machine Logic | Simplified | Full ECS systems |

## Accessing Preview Modes

1. Open the Animation Preview Window: **Window > DMotion > Open Workspace**
2. Click the **dropdown menu** in the toolbar (top-left)
3. Select either **Authoring** or **ECS Runtime**

The selected mode persists across sessions.

---

## Authoring Mode (Default)

**Best for:** Rapid iteration, clip inspection, blend space exploration

### How It Works

Authoring mode uses Unity's PlayableGraph API to sample animations directly. It doesn't require ECS infrastructure and provides instant feedback.

```
AnimationPreviewWindow
        │
        ▼
  PlayableGraphBackend
        │
        ▼
  PlayableGraph + AnimationMixerPlayable
        │
        ▼
  PreviewRenderUtility (3D rendering)
```

### Features

- **Instant preview** - No baking or Play Mode required
- **Blend space visualization** - Drag the indicator to explore blend positions
- **Timeline scrubbing** - Scrub through animation time
- **Transition preview** - Visualize blending between states

### Limitations

- Transition blend curves are approximated (not runtime-accurate)
- State machine logic (conditions, parameters) not evaluated
- May differ slightly from actual runtime behavior

### When to Use

- Designing new states and transitions
- Checking clip timing and events
- Exploring blend space configurations
- Quick visual feedback during authoring

---

## ECS Runtime Mode

**Best for:** Debugging, verifying runtime behavior, accurate transition curves

### How It Works

ECS Runtime mode uses the actual DMotion ECS systems to drive animation. It provides runtime-accurate behavior by executing the same code paths as Play Mode.

```
AnimationPreviewWindow
        │
        ▼
   EcsPreviewBackend
        │
        ▼
  EcsPreviewWorldService ─────► Live Play Mode World
        │                            (if available)
        ▼
  AnimationTimelineControllerSystem
        │
        ▼
  EcsHybridRenderer (PlayableGraph for pose sampling)
```

### Features

- **Runtime-accurate timing** - Uses actual DMotion systems
- **Real transition curves** - Blend curves from StateMachineAsset applied
- **Timeline control** - Full play/pause/scrub with ECS timeline controller
- **Ghost bars** - Visual indicators for animation cycles in transitions
- **Live entity inspection** - Connect to Play Mode entities

### Requirements

ECS Runtime mode requires one of:
1. **Play Mode** - Connect to live entities in the running game
2. **SubScene** - A baked SubScene with the state machine entity

### When to Use

- Debugging transition timing issues
- Verifying blend curve behavior
- Inspecting runtime state machine behavior
- Investigating animation glitches

---

## Transition Timeline (ECS Mode)

ECS Runtime mode features an enhanced transition timeline:

```
┌──────────────────────────────────────────────────────────┐
│  [Ghost From]  [From Bar]  [Transition]  [To Bar]  [Ghost To]  │
│      ░░░░         ████        ▓▓▓▓▓       ████       ░░░░     │
└──────────────────────────────────────────────────────────┘
```

### Timeline Sections

| Section | Color | Description |
|---------|-------|-------------|
| Ghost From | Faded | Previous cycle of FROM animation (context) |
| From Bar | Solid | FROM animation before transition starts |
| Transition | Gradient | Blend zone (FROM fading out, TO fading in) |
| To Bar | Solid | TO animation after transition completes |
| Ghost To | Faded | Next cycle of TO animation (context) |

### Ghost Bars

Ghost bars appear when:
- **Exit time = 0** - Shows previous FROM cycle for context
- **Bars end together** - Shows TO continuation for context
- **Duration shrink** - Animation duration changed below exit time

---

## Preview Model Selection

Both modes support explicit preview model selection:

1. Use the **Model** field in the toolbar to select a prefab
2. The model must have an **Animator** component
3. Selection persists per-StateMachineAsset

If no model is selected, DMotion attempts to extract one from the animation clips.

---

## Troubleshooting

### "No preview model found"
- Select a model with an Animator component in the toolbar
- Ensure animation clips have valid source avatars

### ECS Mode shows no animation
- Enter Play Mode with a SubScene containing the state machine
- Verify the entity has all required DMotion components

### Transition timing looks wrong
- Switch to ECS Runtime mode for accurate timing
- Check the blend curve in the transition asset

### Performance issues during scrubbing
- Authoring mode is faster for quick iteration
- ECS mode has more overhead but accurate results

---

## Related Documentation

- [StateMachineEditor_GUIDE.md](StateMachineEditor_GUIDE.md) - State machine editor usage
- [DMotion_ECS_API_Guide.md](DMotion_ECS_API_Guide.md) - ECS component reference
- [TransitionTimelineGhostBarSpec.md](TransitionTimelineGhostBarSpec.md) - Ghost bar technical spec
