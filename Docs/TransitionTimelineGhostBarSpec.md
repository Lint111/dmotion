# Transition Timeline Ghost Bar Specification

## Overview

The Transition Timeline visualizes a transition between two animation states. It shows:
- **From Bar**: The source animation state (one cycle)
- **To Bar**: The destination animation state
- **Overlap Region**: Where both animations blend (transition duration)
- **Ghost Bar(s)**: Visual context showing previous/additional cycles (preview only)

## Key Variables

| Variable | Description | Range |
|----------|-------------|-------|
| `exitTime` | When the transition starts (seconds) | `[0, fromStateDuration]` |
| `requestedExitTime` | Preserved exit time (can exceed duration) | `[0, ∞)` |
| `fromStateDuration` | Duration of one cycle of the from-state | Dynamic (blend position) |
| `toStateDuration` | Duration of one cycle of the to-state | Dynamic (blend position) |
| `transitionDuration` | How long the blend takes (overlap) | `fromStateDuration - exitTime` |
| `requestedTransitionDuration` | Preserved transition duration | `[0, ∞)` |

## Visual Layout

```
Normal case (no ghost bars):
┌─────────────────────────────────────────────────────────┐
│ [====== From Bar ======]                                │
│            [====== To Bar ======]                       │
│            [##Overlap##]                                │
│            ↑                                            │
│         exitTime                                        │
└─────────────────────────────────────────────────────────┘

With FROM ghost bar (from-duration shrunk below exit time):
┌─────────────────────────────────────────────────────────┐
│ [Ghost][====== From Bar ======]                         │
│                   [====== To Bar ======]                │
│                   [##Overlap##]                         │
│                   ↑                                     │
│                exitTime (within main bar)               │
└─────────────────────────────────────────────────────────┘

With TO ghost bar (to-duration shrunk below transition duration):
┌─────────────────────────────────────────────────────────┐
│ [====== From Bar ======]                                │
│            [====== To Bar ======][Ghost]                │
│            [######Overlap######]                        │
│            ↑                    ↑                       │
│         exitTime         to-state loops during blend   │
└─────────────────────────────────────────────────────────┘

With BOTH ghost bars:
┌─────────────────────────────────────────────────────────┐
│ [Ghost][====== From Bar ======]                         │
│              [====== To Bar ======][Ghost]              │
│              [######Overlap######]                      │
└─────────────────────────────────────────────────────────┘
```

---

## Scenarios & Expected Behavior

### Scenario 1: Initial Configuration

**Input**: Transition asset with `exitTime`, `transitionDuration`, blend positions

**Behavior**:
1. Calculate `fromStateDuration` and `toStateDuration` from blend positions
2. If `exitTime <= fromStateDuration`: No ghost bar needed
3. If `exitTime > fromStateDuration`: Ghost bar(s) appear to show the cycles needed

**Example**:
- Asset exitTime = 0.5s, fromStateDuration = 1.0s → No ghost bar
- Asset exitTime = 1.2s, fromStateDuration = 1.0s → 1 ghost bar (2 cycles needed)

---

### Scenario 2: Dragging To-Bar LEFT (Exit Time Decreases)

**Action**: User drags the to-bar towards the left (earlier in timeline)

**Behavior**:
1. `exitTime` decreases towards minimum
2. `transitionDuration` increases (more overlap)
3. Ghost bar: **Appears when exitTime reaches 0** (context for full overlap)
4. At `exitTime = 0`: Ghost bar shows previous cycle, full overlap with from-bar

**Constraints**:
- **Minimum exitTime = `max(0, fromStateDuration - toStateDuration)`**
- This ensures the to-bar always ends at or after the from-bar (must end in to-state)
- `requestedExitTime` updated to match `exitTime` during drag

**Minimum Exit Time Calculation**:
```
minExitTime = max(0, fromStateDuration - toStateDuration)

Example 1: fromDuration=1.0s, toDuration=1.0s → minExitTime = max(0, 0) = 0
Example 2: fromDuration=2.0s, toDuration=1.0s → minExitTime = max(0, 1.0) = 1.0s
Example 3: fromDuration=0.5s, toDuration=1.0s → minExitTime = max(0, -0.5) = 0
```

**Visual**:
```
Case: fromDuration = toDuration (can drag to 0):
Before drag (exitTime = 0.7s):
[====== From Bar ======]
              [====== To Bar ======]
              
After drag left (exitTime = 0):
[Ghost][====== From Bar ======]
       [====== To Bar ======]
       [####Full Overlap####]

Case: fromDuration > toDuration (limited drag):
fromDuration = 2.0s, toDuration = 1.0s, minExitTime = 1.0s

Before drag (exitTime = 1.5s):
[============ From Bar ============]
                  [== To Bar ==]
                  
After drag left (exitTime = 1.0s, minimum reached, TO ghost appears):
[============ From Bar ============]
              [== To Bar ==][Ghost]
              ↑ Bars end together - TO ghost shows context
```

---

### Scenario 3: Dragging To-Bar RIGHT (Exit Time Increases)

**Action**: User drags the to-bar towards the right (later in timeline)

**Behavior**:
1. `exitTime` increases towards `fromStateDuration`
2. `transitionDuration` decreases (less overlap)
3. Ghost bar: **No change** (dragging stays within one cycle)
4. At `exitTime = fromStateDuration`: Clean cut, no overlap

**Constraints**:
- `exitTime` clamped to `[0, fromStateDuration]` during drag
- Cannot drag past the end of the from-bar

**Visual**:
```
Before drag (exitTime = 0.3s):
[====== From Bar ======]
      [====== To Bar ======]
      
After drag right (exitTime = 0.9s):
[====== From Bar ======]
                      [====== To Bar ======]
                      [##] ← Small overlap

At clean cut (exitTime = fromStateDuration):
[====== From Bar ======]
                        [====== To Bar ======]
                        ↑ No overlap
```

---

### Scenario 4: Blend Position Change - Duration INCREASES

**Action**: User changes blend position, causing `fromStateDuration` to increase

**Behavior**:
1. `exitTime` is **preserved** (absolute value unchanged)
2. Since duration increased, `exitTime` is still within valid range
3. Ghost bar: **Disappears** if it was showing (exit time now within one cycle)
4. Overlap recalculated based on new duration

**Example**:
- Before: exitTime = 0.8s, fromStateDuration = 0.5s → Ghost bar (0.8 > 0.5)
- After: exitTime = 0.8s, fromStateDuration = 1.2s → No ghost bar (0.8 < 1.2)

**Visual**:
```
Before (duration = 0.5s, exitTime = 0.8s):
[Ghost][From]
            [====== To Bar ======]
            
After duration increases to 1.2s:
[======== From Bar (longer) ========]
            [====== To Bar ======]
            ↑ exitTime still at 0.8s, now within single bar
```

---

### Scenario 5: Blend Position Change - Duration DECREASES

**Action**: User changes blend position, causing `fromStateDuration` to decrease

**Behavior**:
1. `exitTime` is **preserved** (absolute value unchanged)
2. If `exitTime > new fromStateDuration`: Ghost bar **appears**
3. For logic: clamp `exitTime` to `[0, fromStateDuration]`
4. For visual: show ghost bars to represent the full requested exit time

**Example**:
- Before: exitTime = 0.8s, fromStateDuration = 1.0s → No ghost bar
- After: exitTime = 0.8s, fromStateDuration = 0.5s → Ghost bar (0.8 > 0.5)

**Calculation**:
- Cycles needed = ceil(0.8 / 0.5) = ceil(1.6) = 2
- Ghost bars = 2 - 1 = 1

**Visual**:
```
Before (duration = 1.0s, exitTime = 0.8s):
[======== From Bar ========]
                  [====== To Bar ======]
                  ↑ exitTime at 0.8s
                  
After duration shrinks to 0.5s:
[Ghost][From]
          [====== To Bar ======]
          ↑ exitTime visually at 0.8s (0.3s into "From" = second cycle)
```

---

### Scenario 6: Clean Cut Transition

**Definition**: `exitTime = fromStateDuration` (no overlap)

**Behavior**:
1. `transitionDuration = 0` (no blend)
2. From-state completes fully before to-state starts
3. Ghost bar: **Never** (exit time equals duration, no excess)

**Visual**:
```
[====== From Bar ======][====== To Bar ======]
                        ↑ Clean cut, no overlap
```

---

### Scenario 7: Maximum Overlap Transition (exitTime = 0)

**Definition**: `exitTime = 0` (to-bar starts at beginning)

**Constraint**: Only possible when `fromStateDuration <= toStateDuration`
- If `fromStateDuration > toStateDuration`, minimum exitTime = `fromStateDuration - toStateDuration`

**Behavior**:
1. `transitionDuration = min(fromStateDuration, toStateDuration)`
2. Both states play simultaneously from the start
3. Ghost bar: **YES** - Shows previous cycle for context

**Purpose of ghost bar at exitTime=0**:
- User can see what the from-animation looks like playing normally
- Then see where the transition kicks in (immediately at cycle start)
- Provides visual context: "animation plays... THEN transition starts here"

**Visual**:
```
Case: fromDuration <= toDuration (exitTime=0 allowed):
[Ghost][====== From Bar ======]
       [====== To Bar ======]
       [####Full Overlap####]
       ↑ Transition starts at cycle boundary

Case: fromDuration > toDuration (exitTime=0 NOT allowed):
[============ From Bar ============]
              [== To Bar ==]
              ↑ Minimum exitTime = fromDuration - toDuration
```

---

### Scenario 8: To-State Duration DECREASES Below Transition Duration

**Action**: User changes to-state blend position, causing `toStateDuration` to decrease

**Behavior**:
1. `transitionDuration` is **preserved** (absolute value unchanged)
2. If `transitionDuration > toStateDuration`: To ghost bar **appears** (to the RIGHT)
3. For logic: clamp `transitionDuration` to `[0, toStateDuration]`
4. For visual: show ghost bar(s) to represent the to-state looping during transition

**Example**:
- Before: transitionDuration = 0.5s, toStateDuration = 1.0s → No to-ghost bar
- After: transitionDuration = 0.5s, toStateDuration = 0.3s → To-ghost bar (0.5 > 0.3)

**Calculation**:
- Cycles needed = ceil(transitionDuration / toStateDuration) = ceil(0.5 / 0.3) = ceil(1.67) = 2
- To ghost bars = 2 - 1 = 1

**Visual**:
```
Before (toStateDuration = 1.0s, transitionDuration = 0.5s):
[====== From Bar ======]
          [====== To Bar ======]
          [##Overlap##]
                   
After toStateDuration shrinks to 0.3s:
[====== From Bar ======]
          [To][Ghost]
          [##Overlap##]
          ↑ To-state loops during transition
```

---

### Scenario 9: To-State Duration INCREASES Above Transition Duration

**Action**: User changes to-state blend position, causing `toStateDuration` to increase

**Behavior**:
1. `transitionDuration` is **preserved** (absolute value unchanged)
2. Since duration increased, transition fits within one cycle
3. To ghost bar: **Disappears** immediately

**Example**:
- Before: transitionDuration = 0.5s, toStateDuration = 0.3s → To-ghost bar (0.5 > 0.3)
- After: transitionDuration = 0.5s, toStateDuration = 1.0s → No to-ghost bar (0.5 < 1.0)

**Visual**:
```
Before (toStateDuration = 0.3s):
[====== From Bar ======]
          [To][Ghost]
          [##Overlap##]
             
After toStateDuration increases to 1.0s:
[====== From Bar ======]
          [====== To Bar (longer) ======]
          [##Overlap##]
          ↑ No more looping needed
```

---

## Ghost Bar Rules Summary

### FROM Ghost Bar (LEFT of from-bar)

**When it APPEARS:**
1. **exitTime == 0** (dragged to far left) - Shows previous cycle for context
   - Only possible when `fromStateDuration <= toStateDuration` (minExitTime == 0)
2. **requestedExitTime > fromStateDuration** (from-duration shrunk below exit time)

**When it DOES NOT APPEAR:**
1. `exitTime > 0` AND `requestedExitTime <= fromStateDuration`
2. Clean cut transitions (`exitTime == fromStateDuration`)
3. When `fromStateDuration > toStateDuration` - context ghost not possible (minExitTime > 0)

**Ghost Bar Count:**
```
if (exitTime == 0):
    fromGhostCount = 1  // One previous cycle for context
else if (requestedExitTime > fromStateDuration):
    fromGhostCount = ceil(requestedExitTime / fromStateDuration) - 1
else:
    fromGhostCount = 0
```

---

### TO Ghost Bar (RIGHT of to-bar)

**When it APPEARS:**
1. **requestedTransitionDuration > toStateDuration** (to-duration shrunk below transition duration)
2. **Both bars end together** (`exitTime + toStateDuration == fromStateDuration`)
   - This happens when exitTime is at minimum (fromDuration > toDuration)
   - Shows context for transition completing at bar boundary

**When it DOES NOT APPEAR:**
1. `requestedTransitionDuration <= toStateDuration` AND to-bar extends past from-bar

**Ghost Bar Count:**
```
if (requestedTransitionDuration > toStateDuration):
    toGhostCount = ceil(requestedTransitionDuration / toStateDuration) - 1
else if (exitTime + toStateDuration <= fromStateDuration + epsilon):
    toGhostCount = 1  // Context ghost when bars end together
else:
    toGhostCount = 0
```

---

### Ghost Bar Purpose:
- **Preview context only** - shows what happens during the transition
- **Not for logic** - actual transition logic uses clamped values
- Use cases:
  1. **From ghost at exitTime=0**: See full animation cycle before transition kicks in
  2. **From ghost when duration shrunk**: See that from-animation would loop to reach exit time
  3. **To ghost when duration shrunk**: See that to-animation would loop during transition blend

---

## State Diagrams

### FROM Ghost Bar Logic (LEFT of from-bar)
```
                    ┌─────────────────────────┐
                    │   requestedExitTime     │
                    │   (preserved value)     │
                    └───────────┬─────────────┘
                                │
                                ▼
                    ┌─────────────────────────┐
                    │ exitTime = clamp(       │
                    │   requestedExitTime,    │
                    │   0, fromStateDuration) │
                    └───────────┬─────────────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  requestedExitTime > fromDuration?  │
              └─────────────────┬───────────────────┘
                                │
                 No             │            Yes
                  │             │             │
                  ▼             │             ▼
        ┌─────────────────┐    │    ┌─────────────────────┐
        │ exitTime == 0?  │    │    │ fromGhostBars =     │
        └────────┬────────┘    │    │ ceil(req/dur) - 1   │
                 │             │    └─────────────────────┘
          Yes    │    No       │
           │     │     │       │
           ▼     │     ▼       │
   ┌────────────┐│┌────────────┐
   │fromGhost=1 │││fromGhost=0 │
   │(context)   │││(normal)    │
   └────────────┘│└────────────┘
```

### TO Ghost Bar Logic (RIGHT of to-bar)
```
                    ┌──────────────────────────────┐
                    │  requestedTransitionDuration │
                    │      (preserved value)       │
                    └───────────┬──────────────────┘
                                │
                                ▼
                    ┌──────────────────────────────┐
                    │ transitionDuration = clamp(  │
                    │   requestedTransitionDuration│
                    │   0, toStateDuration)        │
                    └───────────┬──────────────────┘
                                │
                                ▼
              ┌──────────────────────────────────────────┐
              │  requestedTransitionDur > toStateDur?   │
              └─────────────────┬────────────────────────┘
                                │
                 No             │            Yes
                  │             │             │
                  ▼             │             ▼
        ┌─────────────────┐    │    ┌─────────────────────┐
        │ toGhostBars = 0 │    │    │ toGhostBars =       │
        │ (normal)        │    │    │ ceil(req/dur) - 1   │
        └─────────────────┘    │    └─────────────────────┘
```

---

## Resolved Decisions

### 1. Exit Time Clamping (Option B)
- `exitTime` for logic is always clamped: `min(requestedExitTime, fromStateDuration)`
- `transitionDuration` for logic is always clamped: `min(requestedTransitionDuration, toStateDuration)`
- Logic never exceeds one bar/cycle
- NO modulo operation - just clamp to the end

### 2. Dragging Behavior (Option A + Context Rule)
- Dragging updates BOTH requested and effective values
- **From bar dragging LEFT**: exitTime decreases toward minimum
  - Minimum exitTime = `max(0, fromStateDuration - toStateDuration)`
  - This ensures to-bar always ends at or after from-bar (we must end in to-state)
  - At exitTime = 0: FROM ghost bar appears (context)
  - At exitTime > 0: No FROM ghost bar from dragging alone
- **From bar dragging RIGHT**: exitTime increases toward fromStateDuration

### 3. Duration Change Behavior (Immediate Response)
**From State:**
- Duration INCREASES (becomes > exitTime): FROM ghost bars disappear immediately
- Duration DECREASES (becomes <= exitTime): FROM ghost bar appears immediately
- `requestedExitTime` is preserved, `exitTime` is re-clamped

**To State:**
- Duration INCREASES (becomes > transitionDuration): TO ghost bars disappear immediately
- Duration DECREASES (becomes <= transitionDuration): TO ghost bar appears immediately
- `requestedTransitionDuration` is preserved, `transitionDuration` is re-clamped

### 4. Maximum Ghost Bars
- Limit to 4 (MaxVisualCycles) per bar to prevent UI clutter

---

## Ghost Bar Trigger Conditions

### FROM Ghost Bar (LEFT)

| Condition | Trigger | Purpose |
|-----------|---------|---------|
| `exitTime == 0` | User dragged to far left | Context: show full cycle before transition |
| `requestedExitTime > fromStateDuration` | From-duration shrunk below exit time | Context: show from-animation looping |

Does **NOT** appear when:
- `exitTime > 0` AND `requestedExitTime <= fromStateDuration`

### TO Ghost Bar (RIGHT)

| Condition | Trigger | Purpose |
|-----------|---------|---------|
| `requestedTransitionDuration > toStateDuration` | To-duration shrunk below transition duration | Context: show to-animation looping during blend |
| `exitTime + toStateDuration <= fromStateDuration` | Both bars end together (at min exitTime) | Context: show to-animation continuing past boundary |

Does **NOT** appear when:
- `requestedTransitionDuration <= toStateDuration` AND to-bar extends past from-bar

---

## Implementation Notes

### Variables Needed:
```csharp
// FROM state
float requestedExitTime;      // Preserved value (can exceed duration)
float exitTime;               // Clamped for logic [0, fromStateDuration]
int fromVisualCycles;         // 1 = no ghost, 2+ = ghost bars (LEFT of from-bar)

// TO state  
float requestedTransitionDuration;  // Preserved value (can exceed to-duration)
float transitionDuration;           // Clamped for logic [0, toStateDuration]
int toVisualCycles;                 // 1 = no ghost, 2+ = ghost bars (RIGHT of to-bar)
```

### Key Methods:
```csharp
void CalculateFromVisualCycles()
{
    // Clamp exitTime for logic (never exceeds one cycle)
    exitTime = Mathf.Clamp(requestedExitTime, 0f, fromStateDuration);
    
    // FROM ghost bar appears in two cases:
    // 1. exitTime == 0 (context for full overlap)
    // 2. requestedExitTime > fromStateDuration (from-duration shrunk)
    
    bool showContextGhost = exitTime < 0.001f; // At zero
    bool showDurationGhost = requestedExitTime > fromStateDuration;
    
    if (showDurationGhost)
    {
        // Duration shrunk - show cycles needed to reach requested exit time
        fromVisualCycles = Mathf.CeilToInt(requestedExitTime / fromStateDuration);
    }
    else if (showContextGhost)
    {
        // At zero - show one previous cycle for context
        fromVisualCycles = 2;
    }
    else
    {
        // Normal case - no ghost bar
        fromVisualCycles = 1;
    }
    
    // Clamp to max
    fromVisualCycles = Mathf.Min(fromVisualCycles, MaxVisualCycles);
}

void CalculateToVisualCycles()
{
    // Clamp transitionDuration for logic (never exceeds one to-cycle)
    transitionDuration = Mathf.Clamp(requestedTransitionDuration, 0f, toStateDuration);
    
    // TO ghost bar appears when:
    // requestedTransitionDuration > toStateDuration (to-duration shrunk)
    
    if (requestedTransitionDuration > toStateDuration)
    {
        // Duration shrunk - show cycles needed for full transition
        toVisualCycles = Mathf.CeilToInt(requestedTransitionDuration / toStateDuration);
    }
    else
    {
        // Normal case - no ghost bar
        toVisualCycles = 1;
    }
    
    // Clamp to max
    toVisualCycles = Mathf.Min(toVisualCycles, MaxVisualCycles);
}
```
