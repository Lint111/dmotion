# Unity Mecanim vs DMotion Feature Parity Analysis

**Generated:** 2026-01-17
**Purpose:** Track feature parity between Unity's native Animator system, DMotion capabilities, and Mechination translation support.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| Yes | Fully implemented/supported |
| Partial | Partially implemented |
| No | Not implemented |
| N/A | Not applicable |

---

## 1. State Types

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Single Clip State | Yes | Yes | - | Full support |
| 1D Blend Tree | Yes | Yes | - | `LinearBlendStateAsset` |
| 2D Simple Directional | No | No | HIGH | Common for locomotion |
| 2D Freeform Directional | No | No | MEDIUM | Multiple speeds per direction |
| 2D Freeform Cartesian | No | No | LOW | Non-directional 2D blend |
| Direct Blend Tree | No | No | LOW | Facial expressions, additive |
| Sub-State Machine | Yes | Yes | - | Visual-only, flattened at runtime |
| Empty State | N/A | No | LOW | Logic-only states (skipped) |
| Nested Blend Trees | No | No | MEDIUM | BlendTree containing BlendTrees |

---

## 2. State Properties

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Speed | Yes | Yes | - | Playback multiplier |
| Speed Parameter | Yes | Yes | - | Runtime speed control |
| Loop | Yes | Yes | - | On state asset |
| Motion Time Parameter | No | No | MEDIUM | Scrub through animation |
| Cycle Offset | No | No | LOW | Stagger multiple characters |
| Cycle Offset Parameter | No | No | LOW | Dynamic offset |
| Mirror | No | No | LOW | Humanoid only |
| Mirror Parameter | No | No | LOW | Dynamic mirroring |
| Foot IK | No | No | LOW | Humanoid foot grounding |
| Write Defaults | N/A | N/A | - | Not applicable (DOTS) |

---

## 3. Transitions

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Transition Duration | Yes | Yes | - | Blend time |
| Has Exit Time | Yes | Yes | - | Time-based trigger |
| Exit Time | Yes | Yes | - | Normalized time |
| Fixed Duration | Partial | Yes | LOW | Stored but behavior implicit |
| Transition Offset | No | No | MEDIUM | Start destination mid-way |
| Can Transition To Self | Yes | Yes | - | Any State self-loops |
| Interruption Source | No | No | HIGH | Which states can interrupt |
| Ordered Interruption | No | No | MEDIUM | Interrupt priority order |
| Solo/Mute | N/A | N/A | - | Editor preview only |

---

## 4. Parameters

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Float | Yes | Yes | - | Full support |
| Int | Yes | Yes | - | Full support |
| Bool | Yes | Yes | - | Full support |
| Trigger | No | Partial | MEDIUM | Converted to Bool (no auto-reset) |
| Enum | Yes | No | LOW | DMotion-specific extension |
| Default Values | Yes | Yes | - | Extracted and preserved |

---

## 5. Conditions

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Bool: If (true) | Yes | Yes | - | |
| Bool: IfNot (false) | Yes | Yes | - | |
| Int: Greater | Yes | Yes | - | |
| Int: Less | Yes | Yes | - | |
| Int: Equals | Yes | Yes | - | |
| Int: NotEqual | Yes | Yes | - | |
| Int: GreaterOrEqual | Yes | No | LOW | DMotion has it, translation missing |
| Int: LessOrEqual | Yes | No | LOW | DMotion has it, translation missing |
| Float: Greater | No | No | MEDIUM | Common for speed thresholds |
| Float: Less | No | No | MEDIUM | Common for speed thresholds |
| Multiple (AND) | Yes | Yes | - | All conditions must match |

---

## 6. Layers

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Multiple Layers | No | No | HIGH | Override blending, upper body |
| Layer Weight | No | No | HIGH | Dynamic layer influence |
| Override Blending | No | No | HIGH | Replace lower layer |
| Additive Blending | No | No | MEDIUM | Add to lower layer |
| Avatar Masks | No | No | HIGH | Per-layer bone filtering |
| Sync Layers | No | No | LOW | Mirror state machine structure |
| IK Pass | No | No | LOW | Per-layer IK enable |

---

## 7. Any State

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Any State Transitions | Yes | Yes | - | Native support |
| Can Transition To Self | Yes | Yes | - | Per-transition setting |
| Priority (evaluated first) | Yes | Yes | - | Before regular transitions |

---

## 8. Sub-State Machines

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Unlimited Nesting | Yes | Yes | - | Visual hierarchy |
| Entry State | Yes | Yes | - | Default entry point |
| Exit Node | Yes | Yes | - | Exit states defined |
| Exit Transitions | Yes | Yes | - | OutTransitions on sub-machine |
| Transitions Between Sub-Machines | Yes | Yes | - | Flattened at conversion |
| Entry Transitions (conditional) | No | No | LOW | Multiple entry points |

---

## 9. Animation Events

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Clip Events | Yes | Partial | MEDIUM | Extracted but pending API verify |
| Event Time | Yes | Partial | - | Normalized time |
| Event Parameters | Partial | No | LOW | Hash only (no float/int/string) |
| Loop Event Handling | Yes | N/A | - | DMotion handles loops |
| StateMachineBehaviour | N/A | N/A | - | Unity-specific, not applicable |

---

## 10. Root Motion

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Apply Root Motion | Yes | N/A | - | `RootMotionMode` enum |
| Delta Position | Yes | N/A | - | `RootDeltaTranslation` |
| Delta Rotation | Yes | N/A | - | `RootDeltaRotation` |
| Manual Mode | Yes | N/A | - | Delta only, manual apply |
| Bake Into Pose | No | No | LOW | Per-clip settings |

---

## 11. IK System

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| IK Goals (hands/feet) | No | No | MEDIUM | Weapon holding, foot grounding |
| IK Hints (elbows/knees) | No | No | LOW | Joint direction |
| Look At | No | No | MEDIUM | Head/eye tracking |
| OnAnimatorIK | N/A | N/A | - | Unity callback, not applicable |

---

## 12. Runtime API

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| SetFloat/Int/Bool | Yes | N/A | - | `StateMachineParameterUtils` |
| SetTrigger | No | N/A | MEDIUM | No trigger type |
| GetFloat/Int/Bool | Yes | N/A | - | By hash or name |
| Play (immediate) | Partial | N/A | LOW | OneShot/SingleClip APIs |
| CrossFade | Partial | N/A | LOW | Via transitions only |
| GetCurrentStateInfo | Partial | N/A | MEDIUM | Limited internal access |
| IsInTransition | No | N/A | MEDIUM | Query transition state |
| Force State Change | No | N/A | MEDIUM | Direct state control |

---

## 13. Animator Override Controller

| Unity Feature | DMotion | Mechination | Priority | Notes |
|---------------|---------|-------------|----------|-------|
| Runtime Clip Swapping | No | No | MEDIUM | Character variants |
| Preserve State | No | No | MEDIUM | Swap without reset |

---

## Priority Summary

### HIGH Priority (Critical for common use cases)

1. **2D Simple Directional Blend Tree** - Standard locomotion pattern
2. **Multiple Layers** - Upper body overrides, additive reactions
3. **Avatar Masks** - Per-layer bone filtering
4. **Transition Interruption** - Combat responsiveness

### MEDIUM Priority (Important for advanced use cases)

1. **Float Conditions** - Speed-based transitions
2. **Trigger Parameter** - Auto-reset event handling
3. **Transition Offset** - Start destination mid-animation
4. **IK Goals** - Weapon/foot placement
5. **Look At IK** - Head tracking
6. **Animation Events** (complete translation)
7. **Motion Time Parameter** - Aim poses, animation scrubbing
8. **Nested Blend Trees** - Complex blend hierarchies
9. **Runtime State Query** - IsInTransition, current state info

### LOW Priority (Niche use cases)

1. **2D Freeform blend types** - Less common
2. **Direct Blend Tree** - Facial, additive
3. **Cycle Offset** - Multi-character sync
4. **Mirror** - Humanoid handedness
5. **Sync Layers** - Shared structure layers
6. **Entry Transitions** - Conditional sub-machine entry

---

## Implementation Roadmap

### Phase 1: Core Gaps (Recommended Next)
- [ ] 2D Blend Trees (Simple Directional)
- [ ] Multiple Layers + Override Blending
- [ ] Avatar Masks

### Phase 2: Transition Polish
- [ ] Transition Interruption Source
- [ ] Transition Offset
- [ ] Float Conditions

### Phase 3: Advanced Features
- [ ] Trigger Parameter (with auto-reset)
- [ ] IK Goals/Look At
- [ ] Motion Time Parameter

### Phase 4: Completion
- [ ] Remaining 2D blend types
- [ ] Nested Blend Trees
- [ ] Full runtime API parity
