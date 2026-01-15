# Sub-State Machines - DMotion-First Implementation Strategy

## Executive Summary

**Goal**: Implement native hierarchical state machine support in DMotion
**Approach**: DMotion-First (proven successful with Any State)
**Layers**: Runtime → Authoring → Baking → Bridge → Editor → Testing
**Timeline**: 3 weeks
**Result**: Zero technical debt, full Unity parity, superior UX

---

## 1. Implementation Philosophy

### 1.1 DMotion-First Principles

Following the Any State success, we apply the same principles:

```
1. Implement in DMotion FIRST (native support)
   ↓
2. Bridge becomes PURE TRANSLATION (no workarounds)
   ↓
3. Result: Clean architecture, no technical debt
```

### 1.2 Layer Order

**Critical**: Implement bottom-up (runtime → editor):

```
Layer 1: Runtime (ECS/DOTS)        ← Foundation
Layer 2: Authoring (ScriptableObjects)  ← Data model
Layer 3: Baking (Blob conversion)       ← Build-time
Layer 4: Bridge (Unity translation)     ← Integration
Layer 5: Editor (Visual tools)          ← User-facing
Layer 6: Testing & Validation           ← Quality
```

**Why This Order?**
- Foundation must be solid before building on top
- Each layer depends on previous layers
- Early validation catches issues quickly

---

## 2. Layer 1: Runtime Structures (ECS)

### 2.1 Goals
- ✅ Hierarchical state machine blob structure
- ✅ Stack-based context tracking
- ✅ Entry/exit semantics
- ✅ Burst-compatible

### 2.2 New Components

#### SubStateMachineBlob
```csharp
// File: Runtime/Components/StateMachineBlob.cs

/// <summary>
/// Runtime blob for a sub-state machine (state containing nested states).
/// Stores the nested state machine and entry/exit configuration.
/// </summary>
internal struct SubStateMachineBlob
{
    /// <summary>
    /// Nested state machine data (full StateMachineBlob).
    /// Contains states, transitions, parameters, etc.
    /// </summary>
    internal StateMachineBlob NestedStateMachine;

    /// <summary>
    /// Index of the entry state within the nested machine.
    /// This is the default state to transition to when entering the sub-machine.
    /// </summary>
    internal short EntryStateIndex;

    /// <summary>
    /// Transitions to evaluate when exiting the sub-machine.
    /// Triggered when a state within the nested machine reaches "exit".
    /// </summary>
    internal BlobArray<StateOutTransitionGroup> ExitTransitions;

    /// <summary>
    /// Optional: Name of this sub-machine (for debugging).
    /// </summary>
    internal FixedString64Bytes Name;
}
```

#### StateMachineStack Component
```csharp
// File: Runtime/Components/StateMachineComponents.cs

/// <summary>
/// Tracks the current position in the state machine hierarchy.
/// Each entity has its own stack for navigating nested state machines.
/// </summary>
public struct StateMachineStack : IComponentData
{
    /// <summary>
    /// Stack of state machine contexts (max depth: 8).
    /// Index [0] is root, Index [Depth-1] is current level.
    /// </summary>
    internal FixedList64Bytes<StateMachineContext> Contexts;

    /// <summary>
    /// Current depth in hierarchy (0 = root, 1 = first sub-machine, etc.).
    /// </summary>
    internal byte Depth;

    /// <summary>
    /// Maximum allowed depth (from blob, for validation).
    /// </summary>
    internal byte MaxDepth;

    /// <summary>
    /// Gets the current context (top of stack).
    /// </summary>
    internal ref StateMachineContext Current => ref Contexts.ElementAt(Depth);

    /// <summary>
    /// Gets the parent context (one level up), or null if at root.
    /// </summary>
    internal ref StateMachineContext Parent
    {
        get
        {
            Assert.IsTrue(Depth > 0, "Cannot get parent of root context");
            return ref Contexts.ElementAt(Depth - 1);
        }
    }
}

/// <summary>
/// Context for a single level in the state machine hierarchy.
/// </summary>
internal struct StateMachineContext
{
    /// <summary>Current state index at this level</summary>
    internal short CurrentStateIndex;

    /// <summary>
    /// Index of the sub-machine node in the parent level.
    /// -1 if this is the root level.
    /// </summary>
    internal short ParentSubMachineIndex;

    /// <summary>Hierarchy depth (0 = root)</summary>
    internal byte Level;
}
```

### 2.3 Modified Components

#### StateMachineBlob (Add Sub-Machine Support)
```csharp
// File: Runtime/Components/StateMachineBlob.cs

public struct StateMachineBlob
{
    internal short DefaultStateIndex;
    internal BlobArray<AnimationStateBlob> States;
    internal BlobArray<SingleClipStateBlob> SingleClipStates;
    internal BlobArray<LinearBlendStateBlob> LinearBlendStates;

    // NEW: Sub-state machine storage
    internal BlobArray<SubStateMachineBlob> SubStateMachines;

    internal BlobArray<AnyStateTransition> AnyStateTransitions;

    // NEW: Hierarchy metadata
    /// <summary>Maximum nesting depth in this state machine</summary>
    internal byte MaxNestingDepth;

    /// <summary>Total number of states across all levels (for validation)</summary>
    internal short TotalStateCount;
}
```

#### AnimationStateBlob (Add SubStateMachine Type)
```csharp
// File: Runtime/Components/AnimationTransition.cs

internal struct AnimationStateBlob
{
    internal AnimationStateId StateId;
    internal AnimationStateType Type; // Add: SubStateMachine
    internal short TypeIndex; // Index into appropriate array
    internal BlobArray<StateOutTransitionGroup> Transitions;
}

internal enum AnimationStateType : byte
{
    SingleClip = 0,
    LinearBlend = 1,
    SubStateMachine = 2, // NEW
}
```

### 2.4 Initialization System

```csharp
// File: Runtime/Systems/InitializeStateMachineSystem.cs

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct InitializeStateMachineStackSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationStateMachine>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Add StateMachineStack to entities that have AnimationStateMachine
        // but don't have stack yet
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (stateMachine, entity) in SystemAPI.Query<RefRO<AnimationStateMachine>>()
            .WithNone<StateMachineStack>()
            .WithEntityAccess())
        {
            var stack = new StateMachineStack
            {
                Depth = 0,
                MaxDepth = stateMachine.ValueRO.BlobAsset.Value.MaxNestingDepth
            };

            // Initialize root context
            stack.Contexts.Add(new StateMachineContext
            {
                CurrentStateIndex = stateMachine.ValueRO.CurrentState.StateIndex,
                ParentSubMachineIndex = -1,
                Level = 0
            });

            ecb.AddComponent(entity, stack);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
```

---

## 3. Layer 2: Runtime Evaluation

### 3.1 Goals
- ✅ Hierarchical transition evaluation
- ✅ Sub-machine entry/exit logic
- ✅ Stack management
- ✅ Entry state resolution

### 3.2 Modified UpdateStateMachineJob

```csharp
// File: Runtime/Systems/UpdateStateMachineJob.cs

[BurstCompile]
public partial struct UpdateStateMachineJob : IJobEntity
{
    public void Execute(
        ref AnimationStateMachine stateMachine,
        ref StateMachineStack stack, // NEW parameter
        in DynamicBuffer<BoolParameter> boolParameters,
        in DynamicBuffer<IntParameter> intParameters)
    {
        // Get current blob at current hierarchy level
        ref var currentBlob = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
        ref var currentContext = ref stack.Current;

        // Evaluate transitions (hierarchical)
        {
            var currentStateAnimationState = /* ... */;

            // 1. Evaluate Any State transitions FIRST (current level only)
            var shouldStartTransition = EvaluateAnyStateTransitions(
                currentStateAnimationState,
                ref currentBlob,
                boolParameters,
                intParameters,
                out var transitionIndex);

            // 2. If no Any State match, check regular state transitions
            if (!shouldStartTransition)
            {
                shouldStartTransition = EvaluateTransitions(
                    currentStateAnimationState,
                    ref currentBlob.States[currentContext.CurrentStateIndex],
                    boolParameters,
                    intParameters,
                    out transitionIndex);
            }

            // 3. Handle transition (may enter/exit sub-machines)
            if (shouldStartTransition)
            {
                HandleTransition(
                    transitionIndex,
                    ref stateMachine,
                    ref stack,
                    ref currentBlob);
            }
        }
    }

    /// <summary>
    /// Gets the state machine blob at the current stack level.
    /// </summary>
    [BurstCompile]
    private ref StateMachineBlob GetCurrentBlob(
        ref StateMachineBlob rootBlob,
        ref StateMachineStack stack)
    {
        if (stack.Depth == 0)
            return ref rootBlob;

        // Traverse down the hierarchy
        ref var currentBlob = ref rootBlob;
        for (int i = 1; i <= stack.Depth; i++)
        {
            var context = stack.Contexts[i];
            var parentContext = stack.Contexts[i - 1];

            // Get parent sub-machine state
            ref var parentState = ref currentBlob.States[parentContext.CurrentStateIndex];
            Assert.IsTrue(parentState.Type == AnimationStateType.SubStateMachine);

            // Get nested blob
            ref var subMachine = ref currentBlob.SubStateMachines[parentState.TypeIndex];
            currentBlob = ref subMachine.NestedStateMachine;
        }

        return ref currentBlob;
    }

    /// <summary>
    /// Handles a transition, including sub-machine entry/exit.
    /// </summary>
    [BurstCompile]
    private void HandleTransition(
        short transitionIndex,
        ref AnimationStateMachine stateMachine,
        ref StateMachineStack stack,
        ref StateMachineBlob currentBlob)
    {
        ref var currentContext = ref stack.Current;

        // Get destination state
        ref var transition = ref currentBlob.States[currentContext.CurrentStateIndex]
            .Transitions[transitionIndex];
        short toStateIndex = transition.ToStateIndex;
        ref var toState = ref currentBlob.States[toStateIndex];

        // Check if destination is a sub-state machine
        if (toState.Type == AnimationStateType.SubStateMachine)
        {
            EnterSubStateMachine(toStateIndex, ref stateMachine, ref stack);
        }
        // Check if destination is Exit (pseudo-state)
        else if (toState.IsExit) // NEW: Exit state handling
        {
            ExitCurrentSubStateMachine(ref stateMachine, ref stack);
        }
        // Regular state transition
        else
        {
            TransitionToState(toStateIndex, ref stateMachine, ref stack);
        }
    }

    /// <summary>
    /// Enters a sub-state machine by pushing a new context.
    /// </summary>
    [BurstCompile]
    private void EnterSubStateMachine(
        short subMachineIndex,
        ref AnimationStateMachine stateMachine,
        ref StateMachineStack stack)
    {
        // Get sub-machine blob
        ref var currentBlob = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
        ref var subMachineState = ref currentBlob.States[subMachineIndex];
        ref var subMachineBlob = ref currentBlob.SubStateMachines[subMachineState.TypeIndex];

        // Validate depth
        if (stack.Depth + 1 >= stack.MaxDepth)
        {
            Debug.LogError($"State machine nesting too deep: {stack.Depth + 1} (max {stack.MaxDepth})");
            return;
        }

        // Push new context
        stack.Depth++;
        stack.Contexts.Add(new StateMachineContext
        {
            CurrentStateIndex = subMachineBlob.EntryStateIndex,
            ParentSubMachineIndex = subMachineIndex,
            Level = stack.Depth
        });

        // Transition to entry state
        TransitionToState(subMachineBlob.EntryStateIndex, ref stateMachine, ref stack);
    }

    /// <summary>
    /// Exits the current sub-state machine by popping the context.
    /// </summary>
    [BurstCompile]
    private void ExitCurrentSubStateMachine(
        ref AnimationStateMachine stateMachine,
        ref StateMachineStack stack)
    {
        if (stack.Depth == 0)
        {
            // Can't exit root state machine
            Debug.LogWarning("Attempted to exit root state machine");
            return;
        }

        // Get parent sub-machine blob (to evaluate exit transitions)
        var parentContext = stack.Parent;
        ref var parentBlob = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
        // Go up one level to get actual parent
        stack.Depth--;
        ref var parentBlob2 = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
        stack.Depth++; // Restore for now

        ref var parentState = ref parentBlob2.States[parentContext.CurrentStateIndex];
        ref var subMachineBlob = ref parentBlob2.SubStateMachines[parentState.TypeIndex];

        // Pop context
        stack.Contexts.RemoveAt(stack.Depth);
        stack.Depth--;

        // Evaluate exit transitions
        if (EvaluateExitTransitions(subMachineBlob.ExitTransitions, out var transitionIndex))
        {
            // Take exit transition
            ref var parentBlob3 = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
            HandleTransition(transitionIndex, ref stateMachine, ref stack, ref parentBlob3);
        }
    }

    /// <summary>
    /// Evaluates exit transitions on a sub-machine.
    /// </summary>
    [BurstCompile]
    private bool EvaluateExitTransitions(
        in BlobArray<StateOutTransitionGroup> exitTransitions,
        out short transitionIndex)
    {
        // Similar to regular transition evaluation
        for (short i = 0; i < exitTransitions.Length; i++)
        {
            ref var transitionGroup = ref exitTransitions[i];
            // Check conditions...
            if (/* conditions pass */)
            {
                transitionIndex = i;
                return true;
            }
        }

        transitionIndex = -1;
        return false;
    }
}
```

### 3.3 Stack Management Utilities

```csharp
// File: Runtime/Components/StateMachineStack.cs

public static class StateMachineStackExtensions
{
    /// <summary>
    /// Gets the full state path (for debugging).
    /// Example: "Root.Combat.LightAttack"
    /// </summary>
    public static FixedString512Bytes GetStatePath(
        this ref StateMachineStack stack,
        ref StateMachineBlob rootBlob)
    {
        var path = new FixedString512Bytes();

        for (int i = 0; i <= stack.Depth; i++)
        {
            var context = stack.Contexts[i];
            var blob = GetBlobAtDepth(ref rootBlob, i);
            var state = blob.States[context.CurrentStateIndex];

            if (i > 0)
                path.Append('.');

            // Get state name from blob
            path.Append(GetStateName(ref state, ref blob));
        }

        return path;
    }

    /// <summary>
    /// Validates the stack (for debugging/testing).
    /// </summary>
    public static bool Validate(this ref StateMachineStack stack)
    {
        if (stack.Depth > stack.MaxDepth)
            return false;

        for (int i = 0; i <= stack.Depth; i++)
        {
            var context = stack.Contexts[i];
            if (context.Level != i)
                return false;
        }

        return true;
    }
}
```

---

## 4. Layer 3: Authoring Support

### 4.1 Goals
- ✅ SubStateMachineAsset class
- ✅ Nest StateMachineAsset instances
- ✅ Entry/exit configuration
- ✅ Validation

### 4.2 SubStateMachineAsset

```csharp
// File: Runtime/Authoring/AnimationStateMachine/SubStateMachineAsset.cs

using UnityEngine;
using System.Collections.Generic;

namespace DMotion.Authoring
{
    /// <summary>
    /// Authoring asset for a sub-state machine (state containing nested states).
    /// Represents a hierarchical state that contains its own state machine.
    /// </summary>
    [CreateAssetMenu(menuName = StateMachineEditorConstants.DMotionPath + "/Sub-State Machine")]
    public class SubStateMachineAsset : AnimationStateAsset
    {
        [Header("Nested State Machine")]
        [Tooltip("The state machine contained within this state")]
        public StateMachineAsset NestedStateMachine;

        [Header("Entry State")]
        [Tooltip("Default state to enter when entering this sub-machine. Must be a state in NestedStateMachine.")]
        public AnimationStateAsset EntryState;

        [Header("Exit Transitions")]
        [Tooltip("Transitions to evaluate when a state within this sub-machine reaches 'Exit'. Evaluated at the parent level.")]
        public List<StateOutTransition> ExitTransitions = new();

        // Inherited from AnimationStateAsset:
        // - Name (sub-machine name)
        // - Speed (multiplier for nested animations)
        // - Loop (N/A for sub-machines)
        // - OutTransitions (transitions TO this sub-machine from parent level)

        public override AnimationStateType StateType => AnimationStateType.SubStateMachine;

        /// <summary>
        /// Validates this sub-state machine configuration.
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            if (NestedStateMachine == null)
            {
                errorMessage = $"Sub-State Machine '{name}' has no nested state machine assigned";
                return false;
            }

            if (EntryState == null)
            {
                errorMessage = $"Sub-State Machine '{name}' has no entry state assigned";
                return false;
            }

            if (!NestedStateMachine.States.Contains(EntryState))
            {
                errorMessage = $"Sub-State Machine '{name}' entry state '{EntryState.name}' is not in nested state machine";
                return false;
            }

            // Check for circular references
            if (HasCircularReference(NestedStateMachine, new HashSet<StateMachineAsset>()))
            {
                errorMessage = $"Sub-State Machine '{name}' has circular reference";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool HasCircularReference(StateMachineAsset machine, HashSet<StateMachineAsset> visited)
        {
            if (visited.Contains(machine))
                return true;

            visited.Add(machine);

            foreach (var state in machine.States)
            {
                if (state is SubStateMachineAsset subMachine)
                {
                    if (HasCircularReference(subMachine.NestedStateMachine, visited))
                        return true;
                }
            }

            visited.Remove(machine);
            return false;
        }
    }
}
```

### 4.3 StateMachineAsset Extensions

```csharp
// File: Runtime/Authoring/AnimationStateMachine/StateMachineAsset.cs

public class StateMachineAsset : ScriptableObject
{
    public AnimationStateAsset DefaultState;

    // Can now contain SubStateMachineAsset instances
    public List<AnimationStateAsset> States = new();

    public List<AnimationParameterAsset> Parameters = new();
    public List<StateOutTransition> AnyStateTransitions = new();

    // NEW: Hierarchy utilities

    /// <summary>
    /// Gets the maximum nesting depth of this state machine.
    /// </summary>
    public int GetNestingDepth()
    {
        return GetNestingDepthRecursive(new HashSet<StateMachineAsset>());
    }

    private int GetNestingDepthRecursive(HashSet<StateMachineAsset> visited)
    {
        if (visited.Contains(this))
            return 0; // Circular reference (should be caught in validation)

        visited.Add(this);

        int maxDepth = 0;
        foreach (var state in States)
        {
            if (state is SubStateMachineAsset subMachine && subMachine.NestedStateMachine != null)
            {
                int subDepth = subMachine.NestedStateMachine.GetNestingDepthRecursive(visited);
                maxDepth = Mathf.Max(maxDepth, subDepth + 1);
            }
        }

        visited.Remove(this);
        return maxDepth;
    }

    /// <summary>
    /// Gets all states across all hierarchy levels (for debugging).
    /// </summary>
    public List<AnimationStateAsset> GetAllStatesRecursive()
    {
        var result = new List<AnimationStateAsset>();
        CollectStatesRecursive(result, new HashSet<StateMachineAsset>());
        return result;
    }

    private void CollectStatesRecursive(List<AnimationStateAsset> result, HashSet<StateMachineAsset> visited)
    {
        if (visited.Contains(this))
            return;

        visited.Add(this);

        foreach (var state in States)
        {
            result.Add(state);

            if (state is SubStateMachineAsset subMachine && subMachine.NestedStateMachine != null)
            {
                subMachine.NestedStateMachine.CollectStatesRecursive(result, visited);
            }
        }

        visited.Remove(this);
    }

    /// <summary>
    /// Validates the entire hierarchy.
    /// </summary>
    public bool ValidateHierarchy(out List<string> errors)
    {
        errors = new List<string>();

        // Check max depth
        int depth = GetNestingDepth();
        if (depth > 8)
        {
            errors.Add($"State machine nesting too deep: {depth} levels (max 8)");
        }

        // Validate each sub-machine
        foreach (var state in States)
        {
            if (state is SubStateMachineAsset subMachine)
            {
                if (!subMachine.Validate(out string error))
                {
                    errors.Add(error);
                }

                // Recursively validate nested machine
                if (subMachine.NestedStateMachine != null)
                {
                    if (!subMachine.NestedStateMachine.ValidateHierarchy(out var subErrors))
                    {
                        errors.AddRange(subErrors);
                    }
                }
            }
        }

        return errors.Count == 0;
    }
}
```

---

## 5. Layer 4: Baking Pipeline

### 5.1 Goals
- ✅ Convert SubStateMachineAsset → SubStateMachineBlob
- ✅ Recursive blob building
- ✅ Entry state resolution
- ✅ Exit transition baking

### 5.2 SubStateMachineBlobConverter

```csharp
// File: Runtime/Authoring/Conversion/SubStateMachineBlobConverter.cs

using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Authoring.Conversion
{
    /// <summary>
    /// Converts SubStateMachineAsset to SubStateMachineBlob.
    /// </summary>
    internal struct SubStateMachineBlobConverter
    {
        internal short EntryStateIndex;
        internal UnsafeList<StateOutTransitionConversionData> ExitTransitions;
        internal StateMachineBlobConverter NestedMachineConverter; // Recursive!

        internal FixedString64Bytes Name;

        public void Dispose()
        {
            ExitTransitions.Dispose();
            NestedMachineConverter.Dispose();
        }
    }
}
```

### 5.3 Modified AnimationStateMachineConversionUtils

```csharp
// File: Runtime/Authoring/Conversion/AnimationStateMachineConversionUtils.cs

internal static class AnimationStateMachineConversionUtils
{
    /// <summary>
    /// Creates a blob converter from a StateMachineAsset (recursive).
    /// </summary>
    internal static StateMachineBlobConverter CreateConverter(
        StateMachineAsset stateMachineAsset,
        Allocator allocator)
    {
        var converter = new StateMachineBlobConverter();

        // ... existing code for states, parameters, etc. ...

        // NEW: Build sub-state machines
        BuildSubStateMachines(stateMachineAsset, ref converter, allocator);

        return converter;
    }

    /// <summary>
    /// Builds sub-state machine converters (recursive).
    /// </summary>
    private static void BuildSubStateMachines(
        StateMachineAsset stateMachineAsset,
        ref StateMachineBlobConverter converter,
        Allocator allocator)
    {
        // Count sub-state machines
        int subMachineCount = stateMachineAsset.States
            .Count(s => s is SubStateMachineAsset);

        converter.SubStateMachines =
            new UnsafeList<SubStateMachineBlobConverter>(subMachineCount, allocator);
        converter.SubStateMachines.Resize(subMachineCount);

        int subMachineIndex = 0;
        foreach (var state in stateMachineAsset.States)
        {
            if (state is SubStateMachineAsset subMachineAsset)
            {
                var subConverter = new SubStateMachineBlobConverter
                {
                    Name = new FixedString64Bytes(subMachineAsset.name),
                    EntryStateIndex = (short)subMachineAsset.NestedStateMachine.States
                        .IndexOf(subMachineAsset.EntryState),
                    ExitTransitions = new UnsafeList<StateOutTransitionConversionData>(
                        subMachineAsset.ExitTransitions.Count, allocator),

                    // RECURSIVE: Convert nested state machine
                    NestedMachineConverter = CreateConverter(
                        subMachineAsset.NestedStateMachine,
                        allocator)
                };

                // Build exit transitions
                foreach (var exitTransition in subMachineAsset.ExitTransitions)
                {
                    // Convert exit transition...
                    subConverter.ExitTransitions.Add(/* ... */);
                }

                converter.SubStateMachines[subMachineIndex] = subConverter;
                subMachineIndex++;
            }
        }

        Debug.Log($"[DMotion] Built {subMachineCount} sub-state machine(s) for {stateMachineAsset.name}");
    }
}
```

### 5.4 Modified StateMachineBlobConverter.BuildBlob

```csharp
// File: Runtime/Authoring/Conversion/StateMachineBlobConverter.cs

internal struct StateMachineBlobConverter
{
    internal UnsafeList<SubStateMachineBlobConverter> SubStateMachines; // NEW

    internal BlobAssetReference<StateMachineBlob> BuildBlob(BlobBuilder builder)
    {
        ref var root = ref builder.ConstructRoot<StateMachineBlob>();

        // ... existing code ...

        // NEW: Build sub-state machines (RECURSIVE!)
        {
            var subMachines = builder.Allocate(ref root.SubStateMachines, SubStateMachines.Length);
            for (int i = 0; i < subMachines.Length; i++)
            {
                var subConverter = SubStateMachines[i];

                subMachines[i].Name = subConverter.Name;
                subMachines[i].EntryStateIndex = subConverter.EntryStateIndex;

                // Build exit transitions
                var exitTransitions = builder.Allocate(
                    ref subMachines[i].ExitTransitions,
                    subConverter.ExitTransitions.Length);

                for (int j = 0; j < exitTransitions.Length; j++)
                {
                    // Build exit transition blob...
                }

                // RECURSIVE: Build nested state machine blob
                var nestedMachineBlob = subConverter.NestedMachineConverter.BuildBlob(builder);
                subMachines[i].NestedStateMachine = nestedMachineBlob.Value;
            }
        }

        // Calculate max nesting depth
        root.MaxNestingDepth = CalculateMaxDepth(ref root);

        return builder.CreateBlobAssetReference<StateMachineBlob>(Allocator.Persistent);
    }

    private byte CalculateMaxDepth(ref StateMachineBlob blob)
    {
        byte maxDepth = 0;
        foreach (var subMachine in blob.SubStateMachines)
        {
            byte subDepth = CalculateMaxDepth(ref subMachine.NestedStateMachine);
            maxDepth = Math.Max(maxDepth, (byte)(subDepth + 1));
        }
        return maxDepth;
    }
}
```

---

## 6. Bridge Translation (Pure 1:1)

### 6.1 Goals
- ✅ Read Unity sub-state machines
- ✅ No flattening, no name mangling
- ✅ Pure structural translation
- ✅ Entry/exit resolution

### 6.2 Unity → ControllerData

```csharp
// File: Editor/UnityControllerBridge/Adapters/UnityControllerAdapter.cs

private StateMachineData ReadStateMachine(AnimatorStateMachine unityMachine)
{
    var data = new StateMachineData
    {
        Name = unityMachine.name,
        DefaultStateName = unityMachine.defaultState?.name
    };

    // Read regular states
    foreach (var childState in unityMachine.states)
    {
        var state = ReadState(childState.state, childState.position);
        data.States.Add(state);
    }

    // NEW: Read sub-state machines (RECURSIVE, NO FLATTENING!)
    foreach (var childStateMachine in unityMachine.stateMachines)
    {
        var subMachine = ReadSubStateMachine(childStateMachine);
        data.SubStateMachines.Add(subMachine);
    }

    // Read Any State transitions
    data.AnyStateTransitions = ReadAnyStateTransitions(unityMachine);

    return data;
}

/// <summary>
/// Reads a Unity sub-state machine (PURE 1:1 TRANSLATION).
/// </summary>
private SubStateMachineData ReadSubStateMachine(
    ChildAnimatorStateMachine unitySubMachine)
{
    var data = new SubStateMachineData
    {
        Name = unitySubMachine.stateMachine.name,
        Position = unitySubMachine.position,

        // RECURSIVE: Read nested state machine
        NestedMachine = ReadStateMachine(unitySubMachine.stateMachine),

        // Entry state
        EntryStateName = unitySubMachine.stateMachine.defaultState?.name,

        // Exit transitions (read from states with Exit destination)
        ExitTransitions = ReadExitTransitions(unitySubMachine.stateMachine)
    };

    Debug.Log($"[Unity Controller Bridge] Read sub-state machine '{data.Name}' " +
             $"(entry: '{data.EntryStateName}', {data.NestedMachine.States.Count} states, " +
             $"{data.ExitTransitions.Count} exit transitions)");

    return data;
}

/// <summary>
/// Reads exit transitions from Unity (transitions to Exit node).
/// </summary>
private List<TransitionData> ReadExitTransitions(AnimatorStateMachine unityMachine)
{
    var exitTransitions = new List<TransitionData>();

    foreach (var childState in unityMachine.states)
    {
        foreach (var unityTransition in childState.state.transitions)
        {
            // Check if destination is Exit node (special case in Unity)
            if (unityTransition.isExit)
            {
                var transition = ReadTransition(unityTransition);
                transition.SourceStateName = childState.state.name;
                exitTransitions.Add(transition);
            }
        }
    }

    return exitTransitions;
}
```

### 6.3 ControllerData Structures

```csharp
// File: Editor/UnityControllerBridge/Core/ControllerData.cs

public class StateMachineData
{
    public string Name { get; set; }
    public string DefaultStateName { get; set; }
    public List<StateData> States { get; set; } = new();

    // NEW: Sub-state machines (hierarchical structure preserved!)
    public List<SubStateMachineData> SubStateMachines { get; set; } = new();

    public List<TransitionData> AnyStateTransitions { get; set; } = new();
    public List<ParameterData> Parameters { get; set; } = new();
}

/// <summary>
/// Platform-agnostic sub-state machine data.
/// </summary>
public class SubStateMachineData
{
    public string Name { get; set; }
    public Vector2 Position { get; set; }

    /// <summary>Nested state machine (RECURSIVE!)</summary>
    public StateMachineData NestedMachine { get; set; }

    /// <summary>Name of entry state within nested machine</summary>
    public string EntryStateName { get; set; }

    /// <summary>Transitions to take when exiting this sub-machine</summary>
    public List<TransitionData> ExitTransitions { get; set; } = new();
}
```

---

## 7. Testing Strategy

### 7.1 Unit Tests

```csharp
// File: Tests/Runtime/SubStateMachineTests.cs

[TestFixture]
public class SubStateMachineTests
{
    [Test]
    public void SubStateMachine_EnterAndExit_WorksCorrectly()
    {
        // Create root state machine
        var root = CreateRootStateMachine();

        // Create sub-state machine with 2 states
        var subMachine = CreateSubStateMachine("Combat", "Idle", "Attack");
        root.States.Add(subMachine);

        // Bake to blob
        var blob = BakeStateMachine(root);

        // Test: Enter sub-machine
        var stack = new StateMachineStack { Depth = 0 };
        EnterSubStateMachine(0, ref blob, ref stack);

        Assert.AreEqual(1, stack.Depth, "Should push new context");
        Assert.AreEqual("Idle", GetCurrentStateName(ref blob, ref stack));

        // Test: Transition within sub-machine
        TransitionToState(1, ref blob, ref stack);
        Assert.AreEqual("Attack", GetCurrentStateName(ref blob, ref stack));

        // Test: Exit sub-machine
        ExitSubStateMachine(ref blob, ref stack);
        Assert.AreEqual(0, stack.Depth, "Should pop context");
    }

    [Test]
    public void SubStateMachine_NestingDepth_CalculatedCorrectly()
    {
        // Level 0: Root
        // Level 1: Combat (sub-machine)
        // Level 2: Attacks (sub-sub-machine)

        var attacks = CreateSubStateMachine("Attacks", "Light", "Heavy");
        var combat = CreateSubStateMachine("Combat", "Idle");
        combat.NestedStateMachine.States.Add(attacks);

        var root = CreateRootStateMachine();
        root.States.Add(combat);

        Assert.AreEqual(2, root.GetNestingDepth());
    }

    [Test]
    public void SubStateMachine_CircularReference_DetectedInValidation()
    {
        var machineA = CreateStateMachine("A");
        var machineB = CreateStateMachine("B");

        // A contains B, B contains A (circular!)
        machineA.States.Add(CreateSubStateMachineWrapper(machineB));
        machineB.States.Add(CreateSubStateMachineWrapper(machineA));

        Assert.IsFalse(machineA.ValidateHierarchy(out var errors));
        Assert.IsTrue(errors.Any(e => e.Contains("circular")));
    }
}
```

### 7.2 Integration Tests

```csharp
// File: Tests/Editor/SubStateMachineBridgeTests.cs

[TestFixture]
public class SubStateMachineBridgeTests
{
    [Test]
    public void UnitySubStateMachine_ConvertsToDMotion_WithHierarchyPreserved()
    {
        // Create Unity AnimatorController with sub-state machine
        var controller = CreateUnityController();
        var rootMachine = controller.layers[0].stateMachine;

        var subMachine = new AnimatorStateMachine
        {
            name = "Combat",
            defaultState = CreateUnityState("Idle")
        };
        subMachine.AddState("Attack", Vector3.zero);

        rootMachine.AddStateMachine("Combat", Vector3.zero);

        // Convert
        var dMotionAsset = UnityControllerConverter.ConvertController(
            controller,
            "Assets/Test.asset");

        // Verify hierarchy preserved
        Assert.IsNotNull(dMotionAsset);
        Assert.IsTrue(dMotionAsset.States.Any(s => s is SubStateMachineAsset));

        var subMachineAsset = dMotionAsset.States
            .OfType<SubStateMachineAsset>()
            .First();

        Assert.AreEqual("Combat", subMachineAsset.name);
        Assert.IsNotNull(subMachineAsset.NestedStateMachine);
        Assert.AreEqual(2, subMachineAsset.NestedStateMachine.States.Count);
    }
}
```

---

## 8. Success Criteria

### 8.1 Must Have ✅

1. ✅ Runtime can handle sub-state machines up to 8 levels deep
2. ✅ Entry state resolution works correctly
3. ✅ Exit transitions trigger and evaluate properly
4. ✅ Stack management (push/pop) works correctly
5. ✅ Unity → DMotion conversion preserves hierarchy (no flattening)
6. ✅ Visual editor shows hierarchical structure with navigation
7. ✅ No name mangling or prefixing
8. ✅ Circular reference detection
9. ✅ Performance within 5% of flat structure
10. ✅ Zero technical debt

### 8.2 Validation Checklist

```
Runtime Layer:
[ ] SubStateMachineBlob struct compiles
[ ] StateMachineStack component compiles
[ ] Stack push/pop logic works
[ ] Entry state resolution works
[ ] Exit transition evaluation works
[ ] Hierarchical blob lookup works
[ ] Max depth validation works
[ ] Burst compilation succeeds

Authoring Layer:
[ ] SubStateMachineAsset creates successfully
[ ] Circular reference detection works
[ ] Nesting depth calculation works
[ ] Validation reports errors correctly

Baking Layer:
[ ] SubStateMachineBlobConverter builds blobs
[ ] Recursive baking works
[ ] Exit transitions bake correctly
[ ] Max depth stored in blob

Bridge Layer:
[ ] Unity sub-machines read correctly
[ ] No flattening occurs
[ ] Hierarchy preserved 1:1
[ ] Entry/exit resolved correctly

Editor Layer:
[ ] SubStateMachineNodeView displays
[ ] "Dive in/out" navigation works
[ ] Breadcrumb shows hierarchy
[ ] Inspector shows sub-machine properties

Testing:
[ ] All unit tests pass
[ ] All integration tests pass
[ ] Manual testing passes
[ ] Performance benchmarks pass
```

---

## 9. Timeline

### Week 1: Foundation
- Day 1-2: Runtime structures (SubStateMachineBlob, StateMachineStack)
- Day 3-4: Runtime evaluation (enter/exit logic)
- Day 5: Testing and validation

### Week 2: Integration
- Day 1-2: Authoring support (SubStateMachineAsset)
- Day 3-4: Baking pipeline (recursive conversion)
- Day 5: Bridge translation (Unity reading)

### Week 3: Polish
- Day 1-3: Editor support (visual nesting)
- Day 4: Testing and bug fixes
- Day 5: Documentation and validation

---

## 10. Next Steps

1. ✅ Review this strategy document
2. Create QUICKSTART guide for implementation
3. Start with runtime structures (Layer 1)
4. Iterate layer by layer
5. Test continuously
6. Document as we go

---

**Status**: ✅ Ready for implementation

**Next Document**: `SubStateMachines_QUICKSTART.md`
