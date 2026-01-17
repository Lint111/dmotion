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
| Exit Transitions | Exit Transitions (fully supported) |
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

3. Configure **exit transitions** (see Exit Transitions section below)

---

## Exit Transitions

Exit transitions allow states within a SubStateMachine to trigger transitions back to the parent level. This is essential for creating self-contained behavior groups.

### Understanding Exit Transitions

DMotion uses a **visual-only flattening** approach (like Unity Mecanim):
- **Editor time:** Hierarchical structure with SubStateMachines
- **Runtime:** All states flattened to a single array for efficiency

Exit transitions are evaluated when the current state is marked as an "exit state" for a SubStateMachine.

### Configuring Exit Transitions

#### Step 1: Mark Exit States

In the SubStateMachineStateAsset inspector:

1. Select the SubStateMachine in the graph or hierarchy
2. In the **Exit Configuration** section:
   - Click **"Add Exit State"** dropdown
   - Select states that can trigger exits (e.g., "AttackFinished", "BlockEnd")
3. These states will now be able to trigger exit transitions

#### Step 2: Add Exit Transitions

Still in the SubStateMachineStateAsset inspector:

1. Expand **"Exit Transitions"** list
2. Click **"+"** to add a transition
3. Configure:
   - **To State**: Target state in parent level (e.g., "Idle")
   - **Transition Duration**: Blend time
   - **Conditions**: Bool/Int parameters that must be true

#### Step 3: Visualize in Graph View

Exit transitions appear as **orange edges** in the graph view:
- Regular transitions: White/gray edges
- Exit transitions: Orange edges from SubStateMachine to target

### Exit Transition Example

```
Combat (SubStateMachine)
├─ Attack (entry)
├─ Block
├─ Dodge
└─ CombatIdle (exit state)

Exit States: [CombatIdle]
Exit Transitions:
  CombatIdle → Idle (when "InCombat" = false, duration: 0.2s)
  CombatIdle → Walk (when "Speed" > 0.1, duration: 0.15s)
```

When an entity is in the "CombatIdle" state and the "InCombat" parameter becomes false, the exit transition fires and the entity transitions to "Idle".

### Transition Priority

Transitions are evaluated in this order:
1. **Any State transitions** (highest priority)
2. **Regular state transitions** (from current state)
3. **Exit transitions** (if current state is an exit state)

### Multiple Exit States

You can have multiple exit states in a single SubStateMachine:

```
Locomotion (SubStateMachine)
├─ Walk
├─ Run  
├─ Sprint
├─ StopWalk (exit state) → Idle
├─ StopRun (exit state) → Idle
└─ StopSprint (exit state) → Idle
```

Each exit state can have different exit transitions with different conditions.

---

## How It Works: Runtime Behavior

### Visual-Only Flattening

At conversion time, SubStateMachines are **flattened**:

```
Editor Structure:              Runtime Blob:
Root                           States[0] = Idle
├─ Idle                        States[1] = Walk
├─ Locomotion (Sub)            States[2] = Run
│  ├─ Walk                     States[3] = Sprint
│  ├─ Run                      States[4] = Attack
│  └─ Sprint                   States[5] = Block
└─ Combat (Sub)                States[6] = Dodge
   ├─ Attack
   ├─ Block
   └─ Dodge
```

This means:
- **No runtime stack** - all states are peers
- **Single array traversal** - O(1) state lookup
- **Efficient memory** - no nested blob structures

### Entering a Sub-State Machine

When a transition targets a `SubStateMachineStateAsset`:

1. The transition is **redirected to the entry state**
2. Example: "Idle → Combat" becomes "Idle → Attack" (if Attack is entry state)

### Navigating Within Sub-Machine

- States inside the SubStateMachine transition normally
- Parameters are **shared** across all levels
- Any State transitions work globally

### Exiting via Exit Transitions

When the current state is marked as an exit state:

1. After checking regular transitions, exit transitions are evaluated
2. If conditions match, transition fires to target state in parent
3. Exit transitions use the same condition system as regular transitions

---

## Technical Details

### Architecture: Visual-Only Flattening

Sub-state machines follow Unity Mecanim's pattern:

- ✅ **Visual-only hierarchy** - organization at edit time
- ✅ **Flat runtime blob** - no nested structures
- ✅ **Exit transition groups** - stored efficiently in blob
- ✅ **Single array traversal** - O(1) state access

### Data Structures

**Runtime (StateMachineBlob)**:
- `States[]`: Flat array of all leaf states
- `ExitTransitionGroups[]`: Exit transitions per SubStateMachine
- `AnimationStateBlob.ExitTransitionGroupIndex`: Links exit states to groups

**Authoring**:
- `SubStateMachineStateAsset`: Visual hierarchy container
- `StateMachineAsset`: Nested machine reference
- `ExitStates`: States that can trigger exits
- `ExitTransitions`: Transition conditions and targets

### Performance

**Memory**:
- Flat blob: No per-entity hierarchy overhead
- Exit groups: Shared across all entities
- Typical overhead: ~0 bytes per entity (blob is shared)

**CPU**:
- Flat array access: O(1)
- Exit transition check: Only if current state is exit state
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

## Limitations & Known Issues

### Current Limitations

1. **No Nested Graph Navigation**: Can't double-click to enter SubStateMachine in graph view
   - **Workaround**: Use Unity AnimatorController for visual editing, or edit nested StateMachineAsset directly

2. **No Breadcrumb UI**: Can't see "Root > Combat > Attack" path in graph
   - **Workaround**: Use `StateMachineAsset.GetStatePath()` in code

3. **Exit Transitions at Parent Level Only**: Exit transitions go to sibling states, not grandparent
   - **Design Choice**: Keeps flattening simple and predictable

### Completed Features

- [x] Exit transition runtime support (fully implemented)
- [x] Exit state custom inspector with picker UI
- [x] Orange exit transition edges in graph view
- [x] Validation warnings for misconfigured SubStateMachines
- [x] API reference documentation

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
A: Mark states as "exit states" in the SubStateMachine inspector, then add exit transitions with conditions. When conditions are met, the entity transitions to the target state.

**Q: Does this work with Any State transitions?**
A: Yes! Any State transitions work at each level independently.

**Q: Is there performance overhead?**
A: Minimal. Each level adds ~12 bytes and ~0.001ms traversal time.

---

---

## Testing SubStateMachines in the Editor

### Quick Test: Create a SubStateMachine

1. **Create a State Machine Asset**
   - Right-click in Project: `Create > DMotion > State Machine`
   - Name it "TestStateMachine"

2. **Open the State Machine Editor**
   - Double-click the StateMachineAsset
   - Or: `Window > DMotion > State Machine Editor`

3. **Create a SubStateMachine**
   - Right-click in graph: `New Sub-State Machine`
   - Name it "Combat"

4. **Create a Nested State Machine**
   - Right-click in Project: `Create > DMotion > State Machine`
   - Name it "CombatStateMachine"
   - Add states: "Attack", "Block", "Dodge"
   - Set "Attack" as Default State

5. **Configure the SubStateMachine**
   - Select the "Combat" node in graph
   - In Inspector:
     - Assign "CombatStateMachine" to **Nested State Machine**
     - **Entry State** dropdown: Select "Attack"
     - **Add Exit State**: Select "Dodge" (or any state)
     - **Exit Transitions**: Add transition to "Idle" with conditions

6. **Add States at Root Level**
   - Right-click: `New State` → Name it "Idle"
   - Create transition from "Idle" to "Combat"

7. **Verify Exit Transitions**
   - You should see **orange edges** from "Combat" to exit targets
   - Select the orange edge to see the SubStateMachine in inspector

### Verify in Play Mode

1. Create a test scene with an entity using the state machine
2. Add parameters needed for transitions
3. Enter Play Mode
4. Use Entity Inspector to watch state changes
5. Trigger exit transition conditions and observe state change

---

## Support & Feedback

**Issues**: GitHub Issues
**Docs**: `Docs/SubStateMachines_*.md`
**Tests**: `Tests/UnitTests/SubStateMachine*.cs`

**Documentation**:
- `SubStateMachines_API_REFERENCE.md` - Complete API documentation
- `SubStateMachines_ArchitectureAnalysis.md` - Deep dive into design
- `SubStateMachines_QUICKSTART.md` - Developer quick start
- `SubStateMachines_FEATURE_STATUS.md` - Current implementation status

