# State Machine Editor Guide

Complete guide to using the DMotion State Machine visual editor.

## Opening the Editor

1. **Double-click** any `StateMachineAsset` in the Project window
2. Or: `Window > DMotion > State Machine Editor`, then assign an asset

---

## Graph Overview

### Fixed Nodes

| Node | Position | Purpose |
|------|----------|---------|
| **Any State** | Top-left (50, 50) | Global transitions that can fire from any state |
| **Exit** | Below Any State (50, 150) | Mark states as exit states for nested machines |

### State Nodes

- **Single Clip State**: Single animation clip
- **Linear Blend State**: 1D blend tree with multiple clips
- **Directional 2D Blend State**: 2D blend tree for directional movement
- **SubStateMachine**: Contains nested state machine

---

## Creating States

### Single Clip / Blend States

1. Right-click empty space in graph
2. Select **"New State"**, **"New Blend Tree 1D"**, or **"New Blend Tree 2D"**
3. Or press **Space** to open searchable state menu
4. State appears at cursor position
5. Select state and configure in Inspector

### SubStateMachine

1. Right-click empty space in graph
2. Select **"New Sub-State Machine"**
3. Popup appears:
   - **Name**: Enter state name
   - **Create New Nested Machine**: Creates new StateMachineAsset as sub-asset
   - **Use Existing**: Browse for existing StateMachineAsset
4. Click **"Create"**

---

## Creating Transitions

### Method 1: Right-click Drag (Recommended)

1. Position cursor over source state/node
2. **Right-click and hold**
3. Drag toward target state
4. White line follows cursor
5. Valid targets highlight **green**
6. Invalid targets highlight **red**
7. Release on valid target to create transition

**Works from:**
- State → State (regular transition)
- Any State → State (global transition)
- State → Exit (marks as exit state)

### Method 2: Context Menu

1. Right-click on source state
2. Hover over **"Create Transition"** submenu
3. Click target state name

### Method 3: Inspector

1. Select source state
2. In Inspector, expand **"Out Transitions"**
3. Click **"+"** button
4. Select target state from dropdown

---

## Deleting Transitions

### Method 1: Cut Gesture (Recommended)

1. Position cursor in empty space
2. **Right-click and hold**
3. Drag across transition edges you want to delete
4. Red cut line appears
5. Crossed edges highlight **red**
6. Release to delete all crossed transitions

### Method 2: Select and Delete

1. Click on transition edge to select
2. Press **Delete** key

### Method 3: Inspector

1. Select source state
2. In Inspector, find transition in **"Out Transitions"**
3. Click **"-"** button to remove

---

## Navigating SubStateMachines

### Entering a SubStateMachine

1. **Double-click** on SubStateMachine node body (not title)
2. Graph view changes to show nested machine contents
3. Breadcrumb bar updates: `Root > SubMachineName`

### Exiting to Parent

1. **Click breadcrumb** segment to navigate to that level
2. Example: Click "Root" to return to root level
3. Click any intermediate level to go there

### Breadcrumb Examples

```
Root                          (at root level)
Root > Combat                 (inside Combat SubStateMachine)
Root > Combat > Combos        (inside Combos, which is inside Combat)
```

---

## Exit States & Transitions

### Understanding Exit States

Exit states are states within a SubStateMachine that can trigger transitions back to the parent level. This allows nested machines to "exit" based on conditions.

### Marking Exit States (Visual Method)

1. Navigate into the SubStateMachine
2. Create a transition from the state TO the **Exit** node
3. Right-click drag from state to Exit node (bottom-left)
4. State is now marked as an exit state

### Marking Exit States (Inspector Method)

1. Select the SubStateMachine node (at parent level)
2. In Inspector, find **"Exit States"** section
3. Click dropdown and add states that can trigger exit

### Configuring Exit Transitions

1. Select the SubStateMachine node (at parent level)
2. In Inspector, add **"Out Transitions"**
3. These transitions fire when:
   - Current state is an exit state of this SubStateMachine
   - Transition conditions are met

### Visual Feedback

- **Orange edges**: Exit transitions from SubStateMachine to target states
- **Exit node context menu**: Shows list of current exit states

---

## Renaming States

### Method 1: Double-click Title

1. Double-click directly on state's title text
2. Text field appears
3. Type new name
4. Press **Enter** to confirm or **Escape** to cancel

### Method 2: Keyboard Shortcut

1. Select state
2. Press **F2** or **Ctrl+R**
3. Text field appears
4. Type new name
5. Press **Enter** to confirm

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **F2** | Rename selected state |
| **Ctrl+R** | Rename selected state |
| **Delete** | Delete selected element |
| **Escape** | Cancel rename / Deselect |
| **Enter** | Confirm rename |

---

## Inspector Panels

### State Inspector

When a state is selected:

- **Loop**: Whether animation loops
- **Speed**: Playback speed multiplier
- **Speed Parameter**: Optional parameter to control speed at runtime
- **Out Transitions**: List of transitions from this state

### SubStateMachine Inspector

When a SubStateMachine is selected:

- **Nested State Machine**: Reference to the nested StateMachineAsset
- **Entry State**: Which state to enter when transitioning into this SubStateMachine
- **Exit States**: States that can trigger exit transitions
- **Out Transitions**: Transitions that fire from exit states

### Linear Blend Inspector

When a LinearBlend state is selected:

- **Blend Parameter**: Float or Int parameter for blending
- **Int Range Min/Max**: For Int parameters, defines the normalization range
- **Clips**: List of clips with thresholds

### Any State Inspector

When Any State is selected:

- **Any State Transitions**: Global transitions list

---

## Best Practices

### State Organization

1. **Group related states** into SubStateMachines (Combat, Locomotion, etc.)
2. **Keep root level clean** with 5-10 states max
3. **Name states clearly**: "Attack_Light", "Run_Forward", etc.

### Transition Management

1. **Use right-click drag** for quick transition creation
2. **Use cut gesture** to clean up multiple transitions at once
3. **Add conditions** in Inspector after creating transition

### SubStateMachine Design

1. **One responsibility per SubStateMachine**: Combat, Movement, Interaction
2. **Clear entry point**: Usually the "idle" state for that group
3. **Explicit exit states**: States that represent "done with this behavior"

### Performance Tips

1. **Minimize Any State transitions**: They're checked every frame
2. **Use exit transitions wisely**: Only mark states that truly need to exit
3. **Keep hierarchy depth reasonable**: 3-4 levels max for clarity

---

## Troubleshooting

### "Can't create transition to this state"

- Check if transition already exists (duplicates not allowed)
- Ensure target is a valid state (not Any State or same state)

### "SubStateMachine shows no states inside"

- Verify Nested State Machine is assigned in Inspector
- Check that nested machine has states added

### "Exit transitions not appearing"

- Ensure exit states are marked (via Exit node or Inspector)
- Verify Out Transitions are configured on SubStateMachine

### "Breadcrumb not showing"

- Breadcrumb only appears when inside a SubStateMachine
- At root level, breadcrumb shows just "Root"

### "State position not saving"

- Positions are saved in the asset
- Ensure asset is not read-only
- Try saving the project (Ctrl+S)
