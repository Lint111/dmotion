# Sub-State Machines - User Guide

## What Are Sub-State Machines?

**Sub-state machines** allow you to organize complex animation logic hierarchically by nesting state machines within states. This is useful for:

- **Organizing related states**: Group combat moves, locomotion variants, or interaction animations
- **Reusable state groups**: Create modular behavior that can be used in multiple places
- **Cleaner graphs**: Reduce visual clutter by collapsing detailed logic into sub-machines

### Example: Character Controller
```
Root State Machine:
├─ Idle
├─ Locomotion (Sub-Machine)
│  ├─ Walk
│  ├─ Run
│  └─ Sprint
├─ Combat (Sub-Machine)
│  ├─ Attack
│  ├─ Block
│  └─ Dodge
└─ Death
```

---

## Usage: Unity Controller Bridge (Recommended)

The **easiest way** to use sub-state machines is through Unity's AnimatorController with automatic conversion.

### Step 1: Create Sub-State Machine in Unity

1. Open your AnimatorController
2. Right-click in the Animator window
3. Select **Create Sub-State Machine**
4. Name it (e.g., "Combat")
5. Double-click to enter the sub-machine
6. Add states inside (Attack, Block, Dodge)
7. Set the **default state** (entry point)
8. Go back to parent layer (click breadcrumb)

### Step 2: Convert to DMotion

1. Create Unity Controller Bridge Asset:
   ```
   Assets > Create > DMotion > Unity Controller Bridge
   ```

2. Assign your AnimatorController

3. Click **"Convert"**

4. ✅ Sub-state machine automatically becomes `SubStateMachineStateAsset`!

### What Gets Converted

| Unity Feature | DMotion Result |
|---------------|----------------|
| Sub-State Machine | `SubStateMachineStateAsset` |
| Default State | Entry State |
| Nested States | Recursive `StateMachineAsset` |
| Entry Transitions | Exit Transitions (future) |
| Unlimited Depth | ✅ Fully supported |

---

## Usage: Manual Authoring (Advanced)

For custom workflows without Unity AnimatorController.

### Step 1: Create Nested State Machine

1. Create inner state machine:
   ```
   Assets > Create > DMotion > State Machine
   ```
   Name: "CombatStateMachine"

2. Add states to it (Attack, Block, Dodge)

3. Set Default State

### Step 2: Create Sub-State Machine State

1. In your main StateMachineAsset, add a state to the States list

2. Change type from `SingleClipStateAsset` to `SubStateMachineStateAsset`

3. Configure:
   - **Nested State Machine**: Assign CombatStateMachine
   - **Entry State**: Select which state to start in (usually default state)
   - **Exit Transitions**: (optional) Transitions back to parent level

### Step 3: Setup Transitions

1. Create transitions **into** the sub-machine:
   ```
   Walk → Combat (on bool "InCombat" = true)
   ```

2. Create transitions **within** the sub-machine:
   ```
   Attack → Block (in CombatStateMachine)
   ```

3. Exit transitions (when implemented):
   ```
   Combat → Idle (on bool "InCombat" = false)
   ```

---

## How It Works: Runtime Behavior

### Entering a Sub-State Machine

When a transition targets a `SubStateMachineStateAsset`:

1. **Push Context**: Current position saved to stack
2. **Enter Nested Machine**: Transition to entry state
3. **Continue Evaluation**: Normal transition logic applies inside

```
Current Stack: [Root:Idle]
Transition to Combat →
Current Stack: [Root:Combat, Combat:Attack]
```

### Navigating Within Sub-Machine

- Transitions work normally inside the sub-machine
- Parameters are **shared** across all levels (root and nested)
- Any State transitions work at each level independently

```
[Root:Combat, Combat:Attack]
Transition Attack → Block →
[Root:Combat, Combat:Block]
```

### Exiting Sub-State Machine (Future)

Exit transitions will pop the stack:

```
[Root:Combat, Combat:Block]
Exit transition to Idle →
[Root:Idle]
```

> ⚠️ **Note**: Exit transitions are not yet implemented in runtime evaluation. Sub-machines can be entered but not exited via transitions. Current workaround: Use parameter-based transitions at parent level.

---

## Technical Details

### Architecture: DMotion-First

Sub-state machines are **natively supported** in DMotion runtime:

- ✅ No name mangling or flattening
- ✅ Unlimited nesting depth (relationship-based)
- ✅ Efficient stack-based hierarchy tracking
- ✅ Recursive blob building

### Data Structures

**Runtime**:
- `StateMachineContext` (IBufferElementData): Stack of current positions
- `SubStateMachineBlob`: Recursive blob structure
- `StateType.SubStateMachine`: New state type

**Authoring**:
- `SubStateMachineStateAsset`: Authoring asset
- Recursive `StateMachineAsset` references

### Performance

**Memory**:
- Stack context: ~12 bytes per nesting level
- Typical usage (1-2 levels): ~24-48 bytes per entity
- Blobs: Shared immutable data (no per-entity cost)

**CPU**:
- Hierarchy traversal: O(depth) pointer chasing
- Typical depth 1-3: negligible overhead (~0.01ms)
- Burst-compiled for efficiency

---

## Best Practices

### When to Use Sub-State Machines

✅ **Good Use Cases**:
- Grouping 5+ related states (Combat, Locomotion, Climbing)
- Reusable behavior modules (Item Interactions, Vehicle Controls)
- Simplifying visual complexity (collapse 20 states into 4 groups)

❌ **Avoid**:
- Single-state sub-machines (unnecessary overhead)
- Very shallow hierarchies (2-3 total states)
- Excessively deep nesting (>5 levels hard to debug)

### Naming Conventions

```
Root States: PascalCase (Idle, Jump, Death)
Sub-Machines: PascalCase (Combat, Locomotion)
Nested States: PascalCase (Attack, Block)
```

Avoid prefixing nested state names with parent (Unity does this, we don't need to).

### Parameter Organization

Keep parameters at **root level**:
```
✅ Good: "InCombat" (root), "AttackType" (root)
❌ Bad: "Combat_AttackType" (implied nesting)
```

Parameters are shared across all levels automatically.

### Debugging Tips

1. **Check Stack Context**: Use Entity Inspector to see `StateMachineContext` buffer
2. **Verify Entry State**: Ensure entry state is in nested machine's States list
3. **Log Transitions**: Add debug logs in `EnterSubStateMachine()`
4. **Validate Assets**: Call `SubStateMachineStateAsset.IsValid()` in tests

---

## Limitations & Roadmap

### Current Limitations

1. **No Exit Transitions**: Can't exit sub-machines via "Up" node
   - **Workaround**: Use parent-level parameter transitions

2. **No Visual Editor Enhancements**: Sub-machines render as regular states
   - **Workaround**: Use Unity AnimatorController for visual editing

3. **No Breadcrumb UI**: Can't see "Root > Combat > Attack" in inspector
   - **Workaround**: Check StateMachineContext buffer manually

### Planned Features

- [ ] Exit transition runtime support
- [ ] Visual hierarchy in DMotion editor
- [ ] Current state path display
- [ ] Circular reference validation
- [ ] Performance profiling tools

---

## Examples

### Example 1: Simple Combat System

```
Root:
├─ Idle
├─ Walk
├─ Combat (Sub-Machine)
│  ├─ Attack (entry)
│  ├─ Block
│  └─ Dodge
└─ Death

Transitions:
Walk → Combat (InCombat = true)
Combat:Attack → Combat:Block (DefendPressed = true)
Combat:Block → Combat:Dodge (RollPressed = true)
```

### Example 2: Layered Locomotion

```
Root:
├─ Idle
├─ Ground (Sub-Machine)
│  ├─ Walk (entry)
│  ├─ Run
│  └─ Sprint
├─ Air (Sub-Machine)
│  ├─ Jump (entry)
│  ├─ Fall
│  └─ Glide
└─ Climbing (Sub-Machine)
   ├─ ClimbIdle (entry)
   ├─ ClimbUp
   └─ ClimbDown

Transitions:
Idle → Ground (Speed > 0.1)
Ground → Air (IsGrounded = false)
Air → Ground (IsGrounded = true)
```

### Example 3: Deep Nesting (Vehicles)

```
Root:
├─ OnFoot
└─ InVehicle (Sub-Machine)
   ├─ Entering
   ├─ Driving (Sub-Machine)
   │  ├─ Forward (entry)
   │  ├─ Reverse
   │  └─ Drifting
   ├─ Crashed
   └─ Exiting
```

---

## FAQ

**Q: What's the maximum nesting depth?**
A: Unlimited! The system uses relationship-based connections, not fixed depths.

**Q: Do parameters work in nested machines?**
A: Yes! Parameters are shared across all levels automatically.

**Q: Can I nest sub-machines within sub-machines?**
A: Yes! Recursion is fully supported.

**Q: How do I exit a sub-machine?**
A: Currently: Use parent-level transitions. Future: Exit transitions will be supported.

**Q: Does this work with Any State transitions?**
A: Yes! Any State transitions work at each level independently.

**Q: Is there performance overhead?**
A: Minimal. Each level adds ~12 bytes and ~0.001ms traversal time.

---

## Support & Feedback

**Issues**: GitHub Issues
**Docs**: `Docs/SubStateMachines_*.md`
**Tests**: `Tests/UnitTests/SubStateMachine*.cs`

**Architecture Docs**:
- `SubStateMachines_ArchitectureAnalysis.md` - Deep dive into design
- `SubStateMachines_DMotionFirst_Strategy.md` - Implementation approach
- `SubStateMachines_QUICKSTART.md` - Developer quick start

