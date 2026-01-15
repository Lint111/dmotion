# Unity Controller Bridge - Feature TODOs

This document tracks Unity AnimatorController features that are currently not supported and don't yet have implementation plans in the Action Plan (Phases 12-15).

**Principle**: If a feature exists Unity-side and is currently a limitation, it should have a TODO task attached and not be accepted as an acceptable end state.

---

## High Priority TODOs

### TODO: Trigger Auto-Reset Behavior
**Status**: ⚠️ Partial Support (converted to Bool)
**Unity Feature**: Triggers automatically reset to `false` after being consumed by a transition
**Current Behavior**: Triggers are converted to Bool parameters with default value `false`
**Limitation**: Manual reset required in gameplay code

**Workaround**:
```csharp
// After setting trigger, manually reset it
boolParams.SetValue(triggerHash, true);
// ... animation transitions ...
boolParams.SetValue(triggerHash, false); // Manual reset
```

**Potential Solution**:
- Add trigger auto-reset logic to DMotion's transition evaluation system
- Track which triggers have been "consumed" by transitions
- Reset them automatically after evaluation
- Requires DMotion runtime changes

**References**: UnityControllerBridge_FeatureParity.md line 43-50

---

### TODO: Exit State / Exit Transitions
**Status**: ❌ Not Supported
**Unity Feature**: Exit node allows transitions to leave a sub-state machine
**Current Behavior**: Exit transitions are ignored during conversion

**Use Cases**:
- Sub-state machines with exit points
- Any State → Exit (leave current sub-machine)
- Complex hierarchical state machine navigation

**Dependency**: Sub-State Machine Flattening (Phase 12.5)
**Reason**: Exit states only make sense in the context of sub-machines. Once sub-machine flattening is implemented, exit transitions need to be resolved to the appropriate destination in the parent machine.

**Implementation Notes**:
- When flattening sub-machines, resolve Exit transitions to:
  - Transitions from the sub-machine node in the parent machine
  - Or, remove if no outgoing transitions exist
- Document the behavior in the Sub-State Machine flattening guide

**References**:
- UnityControllerBridge_AnyStateGuide.md line 329
- UnityControllerBridge_FeatureParity.md line 230, 259

---

## Medium Priority TODOs

### TODO: State Machine Behaviors (StateMachineBehaviour)
**Status**: ❌ Not Supported
**Unity Feature**: C# scripts attached to states that run callbacks on enter/exit/update
**Current Behavior**: Ignored during conversion

**Use Cases**:
- Custom logic on state enter/exit
- Per-frame state update logic
- Gameplay integration (e.g., enable weapon collider on attack state enter)

**Complexity**: High - requires callback system in DMotion
**Potential Solution**:
- Add event callbacks to AnimationStateAsset
- Fire events on state transitions
- Users implement logic in ECS systems responding to events
- Alternative: Document ECS patterns for achieving same results

**References**: UnityControllerBridge_FeatureParity.md line 70, 96-101

---

### TODO: Animation Event Function Parameters
**Status**: ⚠️ Partial Support (name + time only)
**Unity Feature**: AnimationEvents can pass string, int, float, or object parameters to callback functions
**Current Behavior**: Only event name and normalized time are supported

**Use Cases**:
- Footstep events with surface type parameter
- Attack events with damage value parameter
- Sound events with volume parameter

**Potential Solution**:
- Extend DMotion's `AnimationClipEvent` to support parameter data
- Add union/variant type for parameter values
- Update bridge to read and convert parameter data
- Requires DMotion runtime changes

**References**: UnityControllerBridge_FeatureParity.md line 297, 299-303

---

### TODO: Nested Blend Trees
**Status**: ❌ Not Supported
**Unity Feature**: Blend trees can contain other blend trees as children
**Current Behavior**: Nested blend trees are not converted

**Use Cases**:
- Complex locomotion (1D speed blending + 2D directional blending)
- Layered animation blending

**Dependency**: 2D Blend Trees (Phase 14.1)
**Reason**: Most nested blend tree use cases involve 2D blend trees. Solve 2D blend trees first, then add nesting support.

**References**: UnityControllerBridge_FeatureParity.md line 160

---

### TODO: Root Motion Per State
**Status**: ⚠️ Partial Support (global only)
**Unity Feature**: Each state can have individual root motion settings
**Current Behavior**: Root motion is a global setting in DMotion

**Use Cases**:
- Some states use root motion (walk, run), others don't (idles, attacks)
- Mixed locomotion systems

**Potential Solution**:
- Add `RootMotionMode` to `AnimationStateAsset`
- Runtime checks state setting instead of global setting
- Requires DMotion runtime changes

**References**: UnityControllerBridge_FeatureParity.md line 311

---

## Low Priority TODOs

### TODO: Motion Time (Direct Animation Time Control)
**Status**: ❌ Not Supported
**Unity Feature**: Directly set the playback time of an animation
**Current Behavior**: No API for direct time control

**Use Cases**:
- Scrubbing animations in cutscenes
- Synchronized multi-character animations
- Time-based gameplay mechanics

**Potential Solution**:
- Add `SetMotionTime(Entity entity, float normalizedTime)` API
- Runtime sets animation playback position
- Requires DMotion runtime changes

**References**: UnityControllerBridge_FeatureParity.md line 68

---

### TODO: Mirror (Humanoid Animation Mirroring)
**Status**: ❌ Not Supported
**Unity Feature**: Mirror humanoid animations (e.g., left kick → right kick)
**Current Behavior**: Mirror flag is ignored

**Use Cases**:
- Humanoid character animations
- Reusing animations for opposite side (left/right attacks)

**Complexity**: High - requires humanoid skeleton awareness
**Potential Solution**:
- Add mirror flag to AnimationStateAsset
- Runtime mirrors bone transforms for humanoid rigs
- Requires DMotion humanoid support

**References**: UnityControllerBridge_FeatureParity.md line 65

---

### TODO: Foot IK (Inverse Kinematics)
**Status**: ❌ Not Supported
**Unity Feature**: Automatically adjust foot placement to match ground
**Current Behavior**: Foot IK flag is ignored

**Use Cases**:
- Characters on uneven terrain
- Stairs, slopes, procedural ground adaptation

**Complexity**: Very High - requires full IK system
**Potential Solution**:
- Integrate with Kinemation IK (if available)
- Add foot IK solver to DMotion
- Defer until user demand is high

**References**: UnityControllerBridge_FeatureParity.md line 66

---

### TODO: Fixed Duration Toggle
**Status**: 🟡 Possibly Working (needs verification)
**Unity Feature**: Transitions can use fixed or normalized duration
**Current Behavior**: Documentation says "always treated as fixed"

**Action Required**:
- Verify current behavior - does DMotion support normalized duration?
- If yes, update documentation to reflect support
- If no, implement normalized duration support or document limitation with TODO

**References**: UnityControllerBridge_FeatureParity.md line 110

---

### TODO: Ordered Interruption (Transition Priority)
**Status**: ❌ Not Supported
**Unity Feature**: Transitions can have priority order for interruption
**Current Behavior**: No priority system

**Use Cases**:
- Complex transition hierarchies
- Higher-priority transitions can interrupt lower-priority ones

**Dependency**: Transition Interruption (Phase 13.2)
**Reason**: This is part of Unity's interruption system. Implement basic interruption first, then add ordered interruption if needed.

**References**: UnityControllerBridge_FeatureParity.md line 124

---

### TODO: Up State (Sub-State Machine Navigation)
**Status**: ❌ Not Supported
**Unity Feature**: Navigate from child state to parent state machine
**Current Behavior**: Ignored

**Dependency**: Sub-State Machine Flattening (Phase 12.5)
**Reason**: Only makes sense in context of sub-state machines. Once flattening is implemented, determine if Up State functionality is needed or if flattening makes it obsolete.

**References**: UnityControllerBridge_FeatureParity.md line 231, 260

---

## Documentation TODOs

### TODO: Add Phase Status to Action Plan
Update `/home/user/dmotion/Docs/UnityControllerBridge_ActionPlan.md` to mark:
- ✅ Phase 12.4 (Any State Expansion) - **COMPLETE**
- Add note about self-transition support

### TODO: Create Guide for State Machine Behaviors Alternative
Since State Machine Behaviors are not directly supported, create a guide showing:
- How to achieve similar functionality with ECS systems
- Responding to state change events
- Querying current state from systems
- Best practices for animation-driven gameplay logic

### TODO: Create Comprehensive Limitation Reference
Create a single reference page that lists all limitations with:
- Current status (Not Supported, Partial, Planned)
- Phase number if planned
- TODO reference if not planned
- Workarounds where available

---

## Review Checklist

When reviewing these TODOs:
- [ ] Verify each is truly a Unity feature (not editor-only convenience)
- [ ] Check if already implicitly supported (update docs if so)
- [ ] Determine if blocking other planned features
- [ ] Estimate user impact (how many people need this?)
- [ ] Identify DMotion runtime dependencies
- [ ] Document workarounds where possible

---

## Related Documents

- [Feature Parity Analysis](UnityControllerBridge_FeatureParity.md) - Comprehensive feature comparison
- [Action Plan](UnityControllerBridge_ActionPlan.md) - Prioritized implementation roadmap (Phases 12-15)
- [Any State Guide](UnityControllerBridge_AnyStateGuide.md) - Completed feature guide
- [DMotion ECS API Guide](DMotion_ECS_API_Guide.md) - How to use animations from gameplay code
