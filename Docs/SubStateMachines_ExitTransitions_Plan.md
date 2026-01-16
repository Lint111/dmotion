# Sub-State Machine Exit Transitions - Implementation Plan

## Overview

Implement full sub-state machine exit transitions matching Unity Mecanim behavior.
Exit states are explicit markers - when a state is marked as an "exit point", its exit transitions are evaluated.

## Architecture

### Exit State Model

```
SubStateMachine "Combat"
├─ State "Attack_Start"      → transitions to Attack_Execute
├─ State "Attack_Execute"    → transitions to Attack_Recovery
├─ State "Attack_Recovery"   [EXIT STATE] → can exit to "Idle" (parent)
└─ State "Combat_Dodge"      [EXIT STATE] → can exit to "Idle" OR stay in Combat
```

**Key Concept:** Exit states are states where the runtime evaluates BOTH:
1. Normal outbound transitions (within sub-machine)
2. Exit transitions (to parent-level states)

### Data Flow

```
Unity Mecanim                         Mechination                          DMotion
─────────────────────────────────────────────────────────────────────────────────────
StateMachine.entryTransitions[]  →   SubStateMachineData.ExitTransitions  →  ExitTransitionBlob[]
  (confusingly named!)                                                       stored per sub-machine

State with exit transition       →   StateData + exit flag                →  StateBlob.IsExitState
                                                                             or ExitStateIndex list
```

## DMotion Changes

### 1. Add Exit Transition Storage to Blob

**File:** `Runtime/Components/StateMachineBlob.cs`

```csharp
public struct StateMachineBlob
{
    internal short DefaultStateIndex;
    internal BlobArray<AnimationStateBlob> States;
    internal BlobArray<SingleClipStateBlob> SingleClipStates;
    internal BlobArray<LinearBlendStateBlob> LinearBlendStates;
    internal BlobArray<AnyStateTransition> AnyStateTransitions;

    // NEW: Exit transition data
    internal BlobArray<ExitTransitionGroup> ExitTransitionGroups;
}

// NEW: Groups exit transitions by the sub-machine they belong to
public struct ExitTransitionGroup
{
    /// <summary>States that can trigger this group's exit transitions</summary>
    internal BlobArray<short> ExitStateIndices;

    /// <summary>Exit transitions for this group</summary>
    internal BlobArray<StateOutTransitionBlob> ExitTransitions;
}
```

### 2. Add IsExitState to AnimationStateBlob

**File:** `Runtime/Components/AnimationStateBlob.cs`

```csharp
public struct AnimationStateBlob
{
    internal StateType Type;
    internal ushort StateIndex;
    internal bool Loop;
    internal float Speed;
    internal ushort SpeedParameterIndex;
    internal BlobArray<StateOutTransitionBlob> Transitions;

    // NEW: Index into ExitTransitionGroups (-1 = not an exit state)
    internal short ExitTransitionGroupIndex;
}
```

### 3. Update StateFlattener to Track Exit States

**File:** `Runtime/Authoring/Conversion/StateFlattener.cs`

Add tracking for which flattened states are exit states:

```csharp
internal struct FlattenedState
{
    internal AnimationStateAsset Asset;
    internal int GlobalIndex;
    internal int ClipIndexOffset;
    internal string Path;

    // NEW: Which sub-machine this state can exit from (-1 = root level)
    internal int ExitGroupIndex;

    // NEW: Is this state an exit point for its sub-machine?
    internal bool IsExitState;
}
```

### 4. Update UpdateStateMachineJob for Exit Evaluation

**File:** `Runtime/Systems/UpdateStateMachineJob.cs`

```csharp
// In the transition evaluation section:
private bool TryEvaluateTransitions(...)
{
    // 1. Check normal state transitions first
    if (TryNormalTransition(...))
        return true;

    // 2. Check any-state transitions
    if (TryAnyStateTransition(...))
        return true;

    // 3. NEW: Check exit transitions if current state is an exit state
    var currentStateBlob = stateMachineBlob.States[currentStateIndex];
    if (currentStateBlob.ExitTransitionGroupIndex >= 0)
    {
        if (TryExitTransition(currentStateBlob.ExitTransitionGroupIndex, ...))
            return true;
    }

    return false;
}
```

### 5. Add Authoring Support for Exit States

**File:** `Runtime/Authoring/AnimationStateMachine/SubStateMachineStateAsset.cs`

```csharp
public class SubStateMachineStateAsset : AnimationStateAsset
{
    public StateMachineAsset NestedStateMachine;
    public AnimationStateAsset EntryState;

    [Header("Exit Configuration")]
    [Tooltip("States that can trigger exit transitions")]
    public List<AnimationStateAsset> ExitStates = new();

    [Tooltip("Transitions to evaluate when in an exit state")]
    public List<StateOutTransition> ExitTransitions = new();
}
```

## Mechination Changes

### 1. Read Exit Transitions from Unity

**File:** `Editor/Adapters/UnityControllerAdapter.cs`

Unity stores exit transitions on `AnimatorStateMachine.entryTransitions` (confusing name!).
These transitions go FROM the sub-machine exit pseudo-state TO parent states.

```csharp
private static SubStateMachineData ReadSubStateMachine(
    AnimatorStateMachine nestedMachine,
    Vector3 position)
{
    var data = new SubStateMachineData
    {
        NestedStateMachine = ReadStateMachine(nestedMachine),
        EntryStateName = nestedMachine.defaultState?.name,
        ExitTransitions = new List<TransitionData>(),
        ExitStateNames = new List<string>()  // NEW
    };

    // Read exit transitions (Unity calls these "entryTransitions" confusingly)
    if (nestedMachine.entryTransitions != null)
    {
        foreach (var exitTransition in nestedMachine.entryTransitions)
        {
            data.ExitTransitions.Add(ReadTransition(exitTransition));

            // Track which states have outbound exit transitions
            // This requires analyzing which states can reach the exit
        }
    }

    // Identify exit states (states with transitions leaving the sub-machine)
    IdentifyExitStates(nestedMachine, data);

    return data;
}
```

### 2. Identify Exit States

States are exit states if they have transitions targeting the sub-machine's exit pseudo-state
or if they have transitions to parent-level states.

```csharp
private static void IdentifyExitStates(
    AnimatorStateMachine nestedMachine,
    SubStateMachineData data)
{
    // In Unity, any state inside the sub-machine can have a transition
    // that targets states OUTSIDE the sub-machine

    foreach (var childState in nestedMachine.states)
    {
        foreach (var transition in childState.state.transitions)
        {
            // Check if transition target is outside this sub-machine
            if (!IsStateInMachine(transition.destinationState, nestedMachine))
            {
                data.ExitStateNames.Add(childState.state.name);
                break;
            }
        }
    }
}
```

### 3. Link Exit Transitions in Second Pass

**File:** `Editor/Conversion/UnityControllerConverter.cs`

Add recursive linking for exit transitions:

```csharp
private static void LinkExitTransitions(
    StateMachineAsset stateMachine,
    ConversionResult result,
    Dictionary<string, AnimationStateAsset> allStatesByName)
{
    foreach (var state in stateMachine.States)
    {
        if (state is SubStateMachineStateAsset subMachine)
        {
            // Link exit transitions
            foreach (var exitTransition in result.GetExitTransitionsFor(subMachine.name))
            {
                if (allStatesByName.TryGetValue(exitTransition.DestinationStateName, out var targetState))
                {
                    var outTransition = CreateTransition(exitTransition, allStatesByName, ...);
                    subMachine.ExitTransitions.Add(outTransition);
                }
            }

            // Mark exit states
            foreach (var exitStateName in result.GetExitStateNamesFor(subMachine.name))
            {
                var exitState = subMachine.NestedStateMachine.States
                    .FirstOrDefault(s => s.name == exitStateName);
                if (exitState != null)
                {
                    subMachine.ExitStates.Add(exitState);
                }
            }

            // Recurse into nested sub-machines
            if (subMachine.NestedStateMachine != null)
            {
                LinkExitTransitions(subMachine.NestedStateMachine, ...);
            }
        }
    }
}
```

### 4. Implement Transition Conditions

**File:** `Editor/Conversion/UnityControllerConverter.cs`

Currently stubbed - needs full implementation:

```csharp
private static TransitionCondition? CreateCondition(
    ConvertedCondition condition,
    List<AnimationParameterAsset> parameters)
{
    var param = parameters.FirstOrDefault(p => p.name == condition.ParameterName);
    if (param == null) return null;

    return condition.Mode switch
    {
        ConditionMode.If => CreateBoolCondition(param, true),
        ConditionMode.IfNot => CreateBoolCondition(param, false),
        ConditionMode.Greater => CreateIntCondition(param, condition.Threshold, IntConditionComparison.Greater),
        ConditionMode.Less => CreateIntCondition(param, condition.Threshold, IntConditionComparison.Less),
        ConditionMode.Equals => CreateIntCondition(param, condition.Threshold, IntConditionComparison.Equal),
        ConditionMode.NotEqual => CreateIntCondition(param, condition.Threshold, IntConditionComparison.NotEqual),
        _ => null
    };
}
```

## Task Breakdown

### DMotion Tasks
1. [ ] Add ExitTransitionGroup to StateMachineBlob
2. [ ] Add ExitTransitionGroupIndex to AnimationStateBlob
3. [ ] Update StateFlattener to track exit states
4. [ ] Update StateMachineBlobConverter to build exit transition data
5. [ ] Update UpdateStateMachineJob to evaluate exit transitions
6. [ ] Add ExitStates list to SubStateMachineStateAsset
7. [ ] Add validation (cycle detection, entry/exit state validation)
8. [ ] Write tests for exit transition behavior

### Mechination Tasks
1. [ ] Update ControllerData.SubStateMachineData with ExitStateNames
2. [ ] Implement IdentifyExitStates in UnityControllerAdapter
3. [ ] Implement LinkExitTransitions in UnityControllerConverter
4. [ ] Implement CreateCondition (full condition support)
5. [ ] Add recursive linking for nested sub-machines
6. [ ] Write integration tests for exit transitions

## Validation Requirements

1. **Cycle Detection:** Entry state chains must not be circular
2. **Exit State Validation:** Exit states must be in the sub-machine
3. **Exit Transition Targets:** Must be valid states at parent level or above
4. **Condition Parameters:** Must exist in parameter list

## Test Cases

1. Simple exit: State A in sub-machine exits to State B in parent
2. Conditional exit: Exit only when parameter X is true
3. Multiple exit states: Two different states can both exit
4. Nested exit: Sub-machine inside sub-machine, exit to grandparent
5. No exit: Sub-machine with no exit (warning? error?)
6. Exit time: Exit transition with exit time condition
