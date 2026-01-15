# Unity Controller Bridge - Any State Feature Guide

## Overview

The Unity Controller Bridge automatically converts Unity's "Any State" transitions into explicit transitions that DMotion can understand. This guide explains how Any State expansion works and how to use it in your animation systems.

**Status**: ✅ Implemented in Phase 12.4

---

## What is Any State?

In Unity, **Any State** is a special node in the Animator Controller that allows you to create global transitions. Instead of manually adding a transition from every state to a destination state, you add one transition from Any State.

### Unity Example

```
States: Idle, Walk, Run, Jump, Combat

Any State → Hit (when Hit trigger is set)
```

This creates a transition from **every state** (Idle, Walk, Run, Jump, Combat) to Hit, without manually creating 5 separate transitions.

### Common Use Cases

1. **Hit Reactions**: Interrupt any animation when character is hit
2. **Death**: Transition to death animation from any state
3. **Stun/Knockback**: Global interrupt effects
4. **Global Abilities**: Dodge, block, parry from any state

---

## How Any State Expansion Works

### The Problem

DMotion doesn't have a concept of "Any State" - it only understands explicit state-to-state transitions. Unity's Any State is editor convenience that needs to be "expanded" to explicit transitions.

### The Solution

The bridge **automatically expands** Any State transitions during conversion:

```
Unity:
  Any State → Hit (1 transition)

DMotion (after expansion):
  Idle → Hit
  Walk → Hit
  Run → Hit
  Jump → Hit
  Combat → Hit
  (5 explicit transitions)
```

### Expansion Rules

1. **Each Any State transition** is copied to **every state** in the state machine
2. **Self-transitions are excluded**: State won't transition to itself
3. **Conditions are preserved**: All transition conditions are copied
4. **Transition properties preserved**: Duration, offset, exit time, etc.

---

## Usage

### Creating Any State Transitions in Unity

1. Open your AnimatorController in Unity
2. Right-click on the **Any State** node
3. Select **Make Transition**
4. Connect to your target state
5. Add conditions as normal

**Example**:
```csharp
// In Unity Animator window:
// Right-click "Any State" → Make Transition → Hit state
// Add condition: Hit trigger = true
// Set duration: 0.1s
```

### The Bridge Handles Everything

When you convert the controller, the bridge will automatically:
1. Detect Any State transitions
2. Expand them to all states
3. Log the expansion:
   ```
   [Unity Controller Bridge] Expanded 1 Any State transition(s)
   to 5 explicit transitions across 5 states
   ```

### Using the Converted Animations

From gameplay code, there's **no difference** between Any State transitions and regular transitions. Both work the same way:

```csharp
[BurstCompile]
partial struct HitReactionSystem : ISystem
{
    private static readonly int HitHash =
        StateMachineParameterUtils.GetHashCode("Hit");

    public void OnUpdate(ref SystemState state)
    {
        foreach (var (damage, boolParams) in
                 SystemAPI.Query<RefRO<TakeDamageEvent>,
                                 DynamicBuffer<BoolParameter>>())
        {
            // Set Hit trigger
            boolParams.SetValue(HitHash, true);

            // Animation will transition from ANY state to Hit
        }
    }
}
```

---

## Performance Considerations

### Memory Trade-off

**Before Expansion** (Unity):
- 1 Any State transition
- Stored once

**After Expansion** (DMotion):
- N explicit transitions (where N = number of states)
- Each state stores its own copy

**Example**:
```
Unity: 100 states + 1 Any State transition = 1 transition in memory
DMotion: 100 states × 1 transition each = 100 transitions in memory
```

### Is This a Problem?

**Usually NO**, because:

1. **Transitions are small**: A transition is ~50 bytes (destination index, conditions, duration)
2. **Most controllers have few Any States**: 1-3 is typical
3. **The tradeoff is worth it**: You get DMotion's performance benefits

**When to be careful**:
- 100+ states with 5+ Any State transitions = 500+ extra transitions
- Each transition adds ~50 bytes, so 500 transitions = ~25 KB

**Mitigation**:
- Limit Any State transitions to truly global interrupts
- Consider using regular transitions for less common cases

---

## Examples

### Example 1: Hit Reaction System

**Unity Setup**:
```
States: Idle, Walk, Run, Attack, Block
Any State → Hit (when Hit trigger)
Any State → Death (when Death trigger)
```

**After Conversion**:
- Each of the 5 states gets 2 transitions (Hit + Death)
- Total: 10 explicit transitions

**Gameplay Code**:
```csharp
[BurstCompile]
partial struct CombatDamageSystem : ISystem
{
    private static readonly int HitHash =
        StateMachineParameterUtils.GetHashCode("Hit");
    private static readonly int DeathHash =
        StateMachineParameterUtils.GetHashCode("Death");

    public void OnUpdate(ref SystemState state)
    {
        foreach (var (health, damage, boolParams, entity) in
                 SystemAPI.Query<RefRW<Health>,
                                 RefRO<DamageEvent>,
                                 DynamicBuffer<BoolParameter>>()
                 .WithEntityAccess())
        {
            health.ValueRW.Value -= damage.ValueRO.Amount;

            if (health.ValueRO.Value <= 0)
            {
                // Death (from any state)
                boolParams.SetValue(DeathHash, true);
            }
            else
            {
                // Hit reaction (from any state)
                boolParams.SetValue(HitHash, true);
            }
        }
    }
}
```

---

### Example 2: Dodge System

**Unity Setup**:
```
States: Idle, Walk, Run, Attack
Any State → Dodge (when Dodge trigger, can transition to self = false)
```

**After Conversion**:
- Idle → Dodge
- Walk → Dodge
- Run → Dodge
- Attack → Dodge

**Gameplay Code**:
```csharp
[BurstCompile]
partial struct DodgeInputSystem : ISystem
{
    private static readonly int DodgeHash =
        StateMachineParameterUtils.GetHashCode("Dodge");

    public void OnUpdate(ref SystemState state)
    {
        foreach (var (input, boolParams) in
                 SystemAPI.Query<RefRO<PlayerInput>,
                                 DynamicBuffer<BoolParameter>>())
        {
            if (input.ValueRO.DodgePressed)
            {
                boolParams.SetValue(DodgeHash, true);

                // Animation will interrupt whatever is playing
                // and transition to Dodge
            }
            else
            {
                boolParams.SetValue(DodgeHash, false);
            }
        }
    }
}
```

---

### Example 3: Ability System

**Unity Setup**:
```
States: Locomotion tree (20+ states for movement)
Any State → CastAbility (when CastAbility trigger)
```

**After Conversion**:
- All 20+ locomotion states get transition to CastAbility
- Player can cast abilities from any locomotion state

**Gameplay Code**:
```csharp
[BurstCompile]
partial struct AbilityCastSystem : ISystem
{
    private static readonly int CastAbilityHash =
        StateMachineParameterUtils.GetHashCode("CastAbility");

    public void OnUpdate(ref SystemState state)
    {
        foreach (var (ability, cooldown, boolParams) in
                 SystemAPI.Query<RefRO<ActiveAbility>,
                                 RefRO<AbilityCooldown>,
                                 DynamicBuffer<BoolParameter>>())
        {
            if (ability.ValueRO.WantsTocast && cooldown.ValueRO.IsReady)
            {
                // Trigger ability animation
                boolParams.SetValue(CastAbilityHash, true);
            }
            else
            {
                boolParams.SetValue(CastAbilityHash, false);
            }
        }
    }
}
```

---

## Debugging

### Viewing Expanded Transitions

In the Unity Editor, check the console log during conversion:

```
[Unity Controller Bridge] Expanded 2 Any State transition(s)
to 40 explicit transitions across 20 states
```

This tells you:
- **2 Any State transitions** were found
- **40 explicit transitions** were created
- **20 states** exist in the state machine

### Inspecting DMotion Assets

After conversion, you can inspect the generated `StateMachineAsset`:

1. Select the generated asset in Project window
2. In the Inspector, expand the States list
3. Each state shows its `OutTransitions` list
4. Verify each state has the expected transitions

---

## Limitations

### 1. Exit Transitions Not Supported

**Unity Feature**: Any State can transition to Exit (leave state machine)
**Bridge Behavior**: Exit transitions are ignored

**Reason**: DMotion doesn't have sub-state machines or exit nodes. This feature requires Sub-State Machine Flattening (Phase 12.5).

---

### 3. Memory Overhead

As discussed in Performance Considerations, Any State expansion creates N copies of each transition. For very large state machines (100+ states) with many Any State transitions (5+), this can add noticeable memory overhead.

**Solution**: Use Any State sparingly for truly global transitions only.

---

## Best Practices

### ✅ Good Uses of Any State

1. **Hit Reactions**: Character can be hit from any state
2. **Death**: Character can die from any state
3. **Global Abilities**: Dodge, block, parry
4. **Forced Transitions**: Cutscenes, scripted events

### ❌ Avoid Any State For

1. **Rare Transitions**: If only 2-3 states need the transition, use explicit transitions
2. **Complex Logic**: Multiple conditions that vary per state
3. **Performance-Critical Code**: If you have 100+ states and are worried about memory

---

## Comparison with Unity

| Feature | Unity | DMotion (After Bridge) |
|---------|-------|------------------------|
| Any State node | ✅ Special node | ❌ Expanded to explicit |
| Transition count | 1 per Any State | N per Any State (N=states) |
| Memory usage | Minimal | N × transition size |
| Runtime performance | Unity's overhead | DOTS performance |
| Behavior | Identical | Identical |
| Self-transitions | ✅ Supported | ✅ Supported |
| Exit transitions | ✅ Supported | ❌ Not supported |

---

## Advanced Topics

### Multiple Any State Transitions

Unity allows multiple Any State transitions. Each is expanded independently:

**Unity**:
```
Any State → Hit (when Hit trigger)
Any State → Death (when Death trigger)
Any State → Stun (when Stun trigger)
```

**DMotion**:
```
Each of N states gets 3 transitions:
  State → Hit
  State → Death
  State → Stun

Total: N × 3 transitions
```

### Transition Priority

Unity evaluates Any State transitions **before** regular transitions. After expansion, DMotion evaluates all transitions in order, so:

- Place Any State-like conditions first for higher priority
- The conversion engine doesn't enforce priority ordering
- Rely on condition specificity to avoid conflicts

### Combining with Regular Transitions

You can mix Any State and regular transitions:

**Example**:
```
States: Idle, Walk, Run

Regular: Idle → Walk (when Speed > 0.1)
Regular: Walk → Run (when Speed > 0.5)
Any State: Any → Hit (when Hit trigger)
```

After conversion, Walk state has:
- 1 regular transition (Walk → Run)
- 1 expanded transition (Walk → Hit)

**Total**: 2 transitions on Walk state

---

## Summary

**Key Points**:
- ✅ Any State transitions work automatically in the bridge
- ✅ Expansion is transparent (no code changes needed)
- ✅ Behavior is identical to Unity
- ✅ Self-transitions are supported (useful for animation restarts)
- ⚠️ Memory overhead: N transitions per Any State
- ✅ Use for global interrupts (hit, death, dodge)
- ❌ Avoid for rare or state-specific transitions

**Performance**:
- Memory: +50 bytes per state per Any State transition
- Runtime: No overhead (DMotion evaluates transitions normally)

**Best Practice**:
Limit Any State to 1-3 truly global transitions for best memory/usability balance.

---

## Related Documentation

- [Unity Controller Bridge Plan](/home/user/dmotion/Docs/UnityControllerBridge_Plan.md)
- [Feature Parity Analysis](/home/user/dmotion/Docs/UnityControllerBridge_FeatureParity.md)
- [Action Plan - Phase 12.4](/home/user/dmotion/Docs/UnityControllerBridge_ActionPlan.md)
- [DMotion ECS API Guide](/home/user/dmotion/Docs/DMotion_ECS_API_Guide.md)
