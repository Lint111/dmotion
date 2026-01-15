# Sub-State Machines - QUICKSTART Implementation Guide

**Goal**: Implement native hierarchical state machine support in DMotion
**Pattern**: DMotion-First (same as Any State success)
**Timeline**: 3 weeks
**Layers**: Runtime → Authoring → Baking → Bridge → Editor

---

## Overview: 6 Steps

```
Step 1: Runtime Structures (2 days)     ← Foundation
Step 2: Runtime Evaluation (2 days)     ← Logic
Step 3: Authoring Support (2 days)      ← Data Model
Step 4: Baking Pipeline (2 days)        ← Build-Time
Step 5: Bridge Translation (2 days)     ← Unity Integration
Step 6: Editor Support (3 days)         ← User-Facing
```

---

## Step 1: Runtime Structures (Days 1-2)

### Goal
Create blob structures and stack component for hierarchical state machines.

### Files to Create/Modify

#### 1.1 Add SubStateMachineBlob
**File**: `Runtime/Components/StateMachineBlob.cs`

```csharp
// ADD: After StateMachineBlob struct definition

/// <summary>
/// Runtime blob for a sub-state machine (state containing nested states).
/// </summary>
internal struct SubStateMachineBlob
{
    /// <summary>Nested state machine data (full StateMachineBlob)</summary>
    internal StateMachineBlob NestedStateMachine;

    /// <summary>Entry state index within nested machine</summary>
    internal short EntryStateIndex;

    /// <summary>Exit transitions (evaluated when exiting sub-machine)</summary>
    internal BlobArray<StateOutTransitionGroup> ExitTransitions;

    /// <summary>Sub-machine name (for debugging)</summary>
    internal FixedString64Bytes Name;
}
```

#### 1.2 Modify StateMachineBlob
**File**: `Runtime/Components/StateMachineBlob.cs`

```csharp
// ADD to StateMachineBlob:
internal BlobArray<SubStateMachineBlob> SubStateMachines;
internal byte MaxNestingDepth;
internal short TotalStateCount;
```

#### 1.3 Modify AnimationStateBlob
**File**: `Runtime/Components/AnimationTransition.cs`

```csharp
// UPDATE AnimationStateType enum:
internal enum AnimationStateType : byte
{
    SingleClip = 0,
    LinearBlend = 1,
    SubStateMachine = 2, // NEW
}
```

#### 1.4 Create StateMachineStack Component
**File**: `Runtime/Components/StateMachineStack.cs` (NEW FILE)

```csharp
using Unity.Collections;
using Unity.Entities;

namespace DMotion
{
    /// <summary>
    /// Tracks the current position in the state machine hierarchy.
    /// </summary>
    public struct StateMachineStack : IComponentData
    {
        internal FixedList64Bytes<StateMachineContext> Contexts;
        internal byte Depth;
        internal byte MaxDepth;

        internal ref StateMachineContext Current => ref Contexts.ElementAt(Depth);
        internal ref StateMachineContext Parent => ref Contexts.ElementAt(Depth - 1);
    }

    internal struct StateMachineContext
    {
        internal short CurrentStateIndex;
        internal short ParentSubMachineIndex;
        internal byte Level;
    }
}
```

### Testing Step 1
```csharp
[Test]
public void SubStateMachineBlob_Structure_IsValid()
{
    // Create blob with sub-machine
    var builder = new BlobBuilder(Allocator.Temp);
    ref var root = ref builder.ConstructRoot<StateMachineBlob>();

    var subMachines = builder.Allocate(ref root.SubStateMachines, 1);
    subMachines[0].EntryStateIndex = 0;

    var blobRef = builder.CreateBlobAssetReference<StateMachineBlob>(Allocator.Temp);

    Assert.AreEqual(1, blobRef.Value.SubStateMachines.Length);
    blobRef.Dispose();
}
```

**✅ Checkpoint**: Structures compile, unit test passes

---

## Step 2: Runtime Evaluation (Days 3-4)

### Goal
Implement hierarchical transition evaluation and stack management.

### Files to Modify

#### 2.1 Update UpdateStateMachineJob
**File**: `Runtime/Systems/UpdateStateMachineJob.cs`

**Add parameter**:
```csharp
public void Execute(
    ref AnimationStateMachine stateMachine,
    ref StateMachineStack stack, // NEW
    /* ... */
)
```

**Add helper methods**:
```csharp
// 1. Get current blob at stack level
private ref StateMachineBlob GetCurrentBlob(
    ref StateMachineBlob rootBlob,
    ref StateMachineStack stack)
{
    if (stack.Depth == 0) return ref rootBlob;

    ref var currentBlob = ref rootBlob;
    for (int i = 1; i <= stack.Depth; i++)
    {
        var context = stack.Contexts[i];
        var parentContext = stack.Contexts[i - 1];

        ref var parentState = ref currentBlob.States[parentContext.CurrentStateIndex];
        ref var subMachine = ref currentBlob.SubStateMachines[parentState.TypeIndex];
        currentBlob = ref subMachine.NestedStateMachine;
    }
    return ref currentBlob;
}

// 2. Enter sub-state machine
private void EnterSubStateMachine(
    short subMachineIndex,
    ref AnimationStateMachine stateMachine,
    ref StateMachineStack stack)
{
    ref var currentBlob = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
    ref var subMachineState = ref currentBlob.States[subMachineIndex];
    ref var subMachineBlob = ref currentBlob.SubStateMachines[subMachineState.TypeIndex];

    stack.Depth++;
    stack.Contexts.Add(new StateMachineContext
    {
        CurrentStateIndex = subMachineBlob.EntryStateIndex,
        ParentSubMachineIndex = subMachineIndex,
        Level = stack.Depth
    });

    TransitionToState(subMachineBlob.EntryStateIndex, ref stateMachine, ref stack);
}

// 3. Exit sub-state machine
private void ExitCurrentSubStateMachine(
    ref AnimationStateMachine stateMachine,
    ref StateMachineStack stack)
{
    if (stack.Depth == 0) return;

    // Get parent sub-machine for exit transitions
    var parentContext = stack.Parent;
    stack.Depth--;
    ref var parentBlob = ref GetCurrentBlob(ref stateMachine.BlobAsset.Value, ref stack);
    stack.Depth++;

    ref var parentState = ref parentBlob.States[parentContext.CurrentStateIndex];
    ref var subMachineBlob = ref parentBlob.SubStateMachines[parentState.TypeIndex];

    // Pop context
    stack.Contexts.RemoveAt(stack.Depth);
    stack.Depth--;

    // Evaluate exit transitions
    if (EvaluateExitTransitions(subMachineBlob.ExitTransitions, out var transitionIndex))
    {
        HandleTransition(transitionIndex, ref stateMachine, ref stack);
    }
}

// 4. Handle transitions (check if entering sub-machine)
private void HandleTransition(/* ... */)
{
    // Get destination state
    ref var toState = ref currentBlob.States[toStateIndex];

    if (toState.Type == AnimationStateType.SubStateMachine)
    {
        EnterSubStateMachine(toStateIndex, ref stateMachine, ref stack);
    }
    else
    {
        TransitionToState(toStateIndex, ref stateMachine, ref stack);
    }
}
```

#### 2.2 Create Stack Initialization System
**File**: `Runtime/Systems/InitializeStateMachineStackSystem.cs` (NEW FILE)

```csharp
[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct InitializeStateMachineStackSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
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

### Testing Step 2
```csharp
[Test]
public void SubStateMachine_EnterAndExit_WorksCorrectly()
{
    // Create state machine with sub-machine
    var root = CreateTestStateMachine();
    var stack = new StateMachineStack { Depth = 0 };

    // Test: Enter sub-machine
    EnterSubStateMachine(0, ref root, ref stack);
    Assert.AreEqual(1, stack.Depth);

    // Test: Exit sub-machine
    ExitSubStateMachine(ref root, ref stack);
    Assert.AreEqual(0, stack.Depth);
}
```

**✅ Checkpoint**: Evaluation works, stack management passes tests

---

## Step 3: Authoring Support (Days 5-6)

### Goal
Create authoring assets for sub-state machines.

### Files to Create

#### 3.1 Create SubStateMachineAsset
**File**: `Runtime/Authoring/AnimationStateMachine/SubStateMachineAsset.cs` (NEW FILE)

```csharp
using UnityEngine;
using System.Collections.Generic;

namespace DMotion.Authoring
{
    [CreateAssetMenu(menuName = "DMotion/Sub-State Machine")]
    public class SubStateMachineAsset : AnimationStateAsset
    {
        [Header("Nested State Machine")]
        public StateMachineAsset NestedStateMachine;

        [Header("Entry State")]
        public AnimationStateAsset EntryState;

        [Header("Exit Transitions")]
        public List<StateOutTransition> ExitTransitions = new();

        public override AnimationStateType StateType => AnimationStateType.SubStateMachine;

        public bool Validate(out string errorMessage)
        {
            if (NestedStateMachine == null)
            {
                errorMessage = "No nested state machine assigned";
                return false;
            }

            if (EntryState == null)
            {
                errorMessage = "No entry state assigned";
                return false;
            }

            if (!NestedStateMachine.States.Contains(EntryState))
            {
                errorMessage = "Entry state not in nested state machine";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
```

#### 3.2 Extend StateMachineAsset
**File**: `Runtime/Authoring/AnimationStateMachine/StateMachineAsset.cs`

```csharp
// ADD methods:

public int GetNestingDepth()
{
    int maxDepth = 0;
    foreach (var state in States)
    {
        if (state is SubStateMachineAsset subMachine && subMachine.NestedStateMachine != null)
        {
            int subDepth = subMachine.NestedStateMachine.GetNestingDepth();
            maxDepth = Mathf.Max(maxDepth, subDepth + 1);
        }
    }
    return maxDepth;
}

public bool ValidateHierarchy(out List<string> errors)
{
    errors = new List<string>();

    int depth = GetNestingDepth();
    if (depth > 8)
    {
        errors.Add($"Nesting too deep: {depth} levels (max 8)");
    }

    foreach (var state in States)
    {
        if (state is SubStateMachineAsset subMachine)
        {
            if (!subMachine.Validate(out string error))
            {
                errors.Add(error);
            }
        }
    }

    return errors.Count == 0;
}
```

### Testing Step 3
```csharp
[Test]
public void SubStateMachineAsset_Validation_WorksCorrectly()
{
    var subMachine = ScriptableObject.CreateInstance<SubStateMachineAsset>();

    // Invalid: No nested machine
    Assert.IsFalse(subMachine.Validate(out _));

    // Valid: Nested machine assigned
    subMachine.NestedStateMachine = CreateTestStateMachine();
    subMachine.EntryState = subMachine.NestedStateMachine.States[0];
    Assert.IsTrue(subMachine.Validate(out _));
}
```

**✅ Checkpoint**: Assets create successfully, validation works

---

## Step 4: Baking Pipeline (Days 7-8)

### Goal
Convert SubStateMachineAsset → SubStateMachineBlob (recursive).

### Files to Create/Modify

#### 4.1 Create SubStateMachineBlobConverter
**File**: `Runtime/Authoring/Conversion/SubStateMachineBlobConverter.cs` (NEW FILE)

```csharp
internal struct SubStateMachineBlobConverter
{
    internal short EntryStateIndex;
    internal UnsafeList<StateOutTransitionConversionData> ExitTransitions;
    internal StateMachineBlobConverter NestedMachineConverter; // RECURSIVE!
    internal FixedString64Bytes Name;

    public void Dispose()
    {
        ExitTransitions.Dispose();
        NestedMachineConverter.Dispose();
    }
}
```

#### 4.2 Modify StateMachineBlobConverter
**File**: `Runtime/Authoring/Conversion/StateMachineBlobConverter.cs`

```csharp
// ADD field:
internal UnsafeList<SubStateMachineBlobConverter> SubStateMachines;

// UPDATE BuildBlob():
// Build sub-state machines (RECURSIVE!)
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
    // ... build exit transitions ...

    // RECURSIVE: Build nested machine
    subMachines[i].NestedStateMachine =
        subConverter.NestedMachineConverter.BuildBlob(builder).Value;
}

// Calculate max depth
root.MaxNestingDepth = CalculateMaxDepth(ref root);
```

#### 4.3 Update AnimationStateMachineConversionUtils
**File**: `Runtime/Authoring/Conversion/AnimationStateMachineConversionUtils.cs`

```csharp
// ADD method:
private static void BuildSubStateMachines(
    StateMachineAsset stateMachineAsset,
    ref StateMachineBlobConverter converter,
    Allocator allocator)
{
    int subMachineCount = stateMachineAsset.States.Count(s => s is SubStateMachineAsset);

    converter.SubStateMachines = new UnsafeList<SubStateMachineBlobConverter>(
        subMachineCount, allocator);
    converter.SubStateMachines.Resize(subMachineCount);

    int index = 0;
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

                // RECURSIVE: Convert nested machine
                NestedMachineConverter = CreateConverter(
                    subMachineAsset.NestedStateMachine, allocator)
            };

            // Build exit transitions...

            converter.SubStateMachines[index] = subConverter;
            index++;
        }
    }
}

// Call from CreateConverter():
BuildSubStateMachines(stateMachineAsset, ref converter, allocator);
```

### Testing Step 4
```csharp
[Test]
public void SubStateMachine_Baking_CreatesValidBlob()
{
    var root = CreateTestStateMachineWithSubMachine();
    var blobRef = BakeStateMachine(root);

    Assert.AreEqual(1, blobRef.Value.SubStateMachines.Length);
    Assert.AreEqual(1, blobRef.Value.MaxNestingDepth);

    blobRef.Dispose();
}
```

**✅ Checkpoint**: Baking produces valid blobs, recursive conversion works

---

## Step 5: Bridge Translation (Days 9-10)

### Goal
Unity → DMotion translation (NO FLATTENING).

### Files to Modify

#### 5.1 Add SubStateMachineData
**File**: `Editor/UnityControllerBridge/Core/ControllerData.cs`

```csharp
public class StateMachineData
{
    public string Name { get; set; }
    public string DefaultStateName { get; set; }
    public List<StateData> States { get; set; } = new();

    // NEW: Sub-state machines (hierarchy preserved!)
    public List<SubStateMachineData> SubStateMachines { get; set; } = new();

    public List<TransitionData> AnyStateTransitions { get; set; } = new();
}

public class SubStateMachineData
{
    public string Name { get; set; }
    public Vector2 Position { get; set; }
    public StateMachineData NestedMachine { get; set; } // RECURSIVE!
    public string EntryStateName { get; set; }
    public List<TransitionData> ExitTransitions { get; set; } = new();
}
```

#### 5.2 Update UnityControllerAdapter
**File**: `Editor/UnityControllerBridge/Adapters/UnityControllerAdapter.cs`

```csharp
// UPDATE ReadStateMachine():
private StateMachineData ReadStateMachine(AnimatorStateMachine unityMachine)
{
    var data = new StateMachineData { /* ... */ };

    // Read states
    foreach (var childState in unityMachine.states)
    {
        data.States.Add(ReadState(childState.state));
    }

    // NEW: Read sub-state machines (RECURSIVE, NO FLATTENING!)
    foreach (var childStateMachine in unityMachine.stateMachines)
    {
        var subMachine = ReadSubStateMachine(childStateMachine);
        data.SubStateMachines.Add(subMachine);
    }

    // Read Any State
    data.AnyStateTransitions = ReadAnyStateTransitions(unityMachine);

    return data;
}

// ADD method:
private SubStateMachineData ReadSubStateMachine(
    ChildAnimatorStateMachine unitySubMachine)
{
    var data = new SubStateMachineData
    {
        Name = unitySubMachine.stateMachine.name,
        Position = unitySubMachine.position,

        // RECURSIVE: Read nested machine
        NestedMachine = ReadStateMachine(unitySubMachine.stateMachine),

        EntryStateName = unitySubMachine.stateMachine.defaultState?.name,
        ExitTransitions = ReadExitTransitions(unitySubMachine.stateMachine)
    };

    Debug.Log($"[Unity Bridge] Read sub-machine '{data.Name}' " +
             $"({data.NestedMachine.States.Count} states)");

    return data;
}

private List<TransitionData> ReadExitTransitions(AnimatorStateMachine unityMachine)
{
    var exitTransitions = new List<TransitionData>();

    foreach (var childState in unityMachine.states)
    {
        foreach (var unityTransition in childState.state.transitions)
        {
            if (unityTransition.isExit)
            {
                var transition = ReadTransition(unityTransition);
                exitTransitions.Add(transition);
            }
        }
    }

    return exitTransitions;
}
```

#### 5.3 Update DMotionAssetBuilder
**File**: `Editor/UnityControllerBridge/Adapters/DMotionAssetBuilder.cs`

```csharp
// UPDATE BuildStateMachine():
public static StateMachineAsset BuildStateMachine(ConversionResult result, string outputPath)
{
    // ... existing code ...

    // Create state assets (including sub-machines)
    var stateAssets = CreateStates(result.States, result.SubStateMachines, /* ... */);

    // ... rest of method ...
}

// ADD to CreateStates():
private static List<AnimationStateAsset> CreateStates(
    List<ConvertedState> states,
    List<ConvertedSubStateMachine> subStateMachines, // NEW
    /* ... */)
{
    var assets = new List<AnimationStateAsset>();

    // Create regular states
    foreach (var state in states)
    {
        // ... existing code ...
        assets.Add(asset);
    }

    // NEW: Create sub-state machine assets
    foreach (var subMachine in subStateMachines)
    {
        var subAsset = CreateSubStateMachineAsset(subMachine, /* ... */);
        assets.Add(subAsset);
    }

    return assets;
}

private static SubStateMachineAsset CreateSubStateMachineAsset(
    ConvertedSubStateMachine subMachine,
    /* ... */)
{
    var asset = ScriptableObject.CreateInstance<SubStateMachineAsset>();
    asset.name = subMachine.Name;

    // RECURSIVE: Build nested state machine
    asset.NestedStateMachine = BuildNestedStateMachine(subMachine.NestedMachine);

    // Set entry state
    asset.EntryState = asset.NestedStateMachine.States
        .FirstOrDefault(s => s.name == subMachine.EntryStateName);

    // Create exit transitions
    // ... build exit transitions ...

    return asset;
}
```

### Testing Step 5
```csharp
[Test]
public void UnitySubStateMachine_ConvertsToDMotion_WithHierarchy()
{
    var controller = CreateUnityControllerWithSubMachine();
    var dMotionAsset = UnityControllerConverter.ConvertController(controller, "Test.asset");

    Assert.IsTrue(dMotionAsset.States.Any(s => s is SubStateMachineAsset));

    var subMachine = dMotionAsset.States.OfType<SubStateMachineAsset>().First();
    Assert.IsNotNull(subMachine.NestedStateMachine);
    Assert.AreEqual(2, subMachine.NestedStateMachine.States.Count);
}
```

**✅ Checkpoint**: Unity converts to DMotion with hierarchy preserved

---

## Step 6: Editor Support (Days 11-13)

### Goal
Visual hierarchical navigation in graph editor.

### Files to Create/Modify

#### 6.1 Create SubStateMachineNodeView
**File**: `Editor/EditorWindows/SubStateMachineNodeView.cs` (NEW FILE)

```csharp
internal class SubStateMachineNodeView : StateNodeView<SubStateMachineAsset>
{
    public SubStateMachineNodeView(VisualTreeAsset asset) : base(asset)
    {
        AddToClassList("substatemachine");

        var openButton = new Button(() => OpenSubMachine())
        {
            text = "Open ►"
        };
        mainContainer.Add(openButton);
    }

    private void OpenSubMachine()
    {
        var subMachine = (State as SubStateMachineAsset).NestedStateMachine;
        ParentView.NavigateToSubMachine(subMachine);
    }
}
```

#### 6.2 Update AnimationStateMachineEditorView
**File**: `Editor/EditorWindows/AnimationStateMachineEditorView.cs`

```csharp
// ADD fields:
private Stack<StateMachineAsset> navigationStack = new();
private Label breadcrumbLabel;

// ADD method:
internal void NavigateToSubMachine(StateMachineAsset subMachine)
{
    navigationStack.Push(model.StateMachineAsset);

    PopulateView(new StateMachineEditorViewModel
    {
        StateMachineAsset = subMachine,
        StateNodeXml = model.StateNodeXml,
        /* ... */
    });

    UpdateBreadcrumb();
}

internal void NavigateBack()
{
    if (navigationStack.Count == 0) return;

    var parent = navigationStack.Pop();
    PopulateView(new StateMachineEditorViewModel
    {
        StateMachineAsset = parent,
        /* ... */
    });

    UpdateBreadcrumb();
}

private void UpdateBreadcrumb()
{
    var path = string.Join(" > ", navigationStack.Select(s => s.name));
    path += " > " + model.StateMachineAsset.name;
    breadcrumbLabel.text = path;
}

// UPDATE InstantiateStateView():
private void InstantiateStateView(AnimationStateAsset state)
{
    StateNodeView stateView;

    if (state is SubStateMachineAsset)
    {
        stateView = new SubStateMachineNodeView(StateNodeXml);
    }
    else if (state is SingleClipStateAsset)
    {
        stateView = new SingleClipStateNodeView(StateNodeXml);
    }
    else if (state is LinearBlendStateAsset)
    {
        stateView = new LinearBlendStateNodeView(StateNodeXml);
    }

    // ... rest of method ...
}
```

#### 6.3 Create SubStateMachineInspector
**File**: `Editor/EditorWindows/SubStateMachineInspector.cs` (NEW FILE)

```csharp
internal class SubStateMachineInspector : StateMachineInspector<AnimationStateInspectorModel>
{
    public override void OnInspectorGUI()
    {
        var subMachine = model.StateAsset as SubStateMachineAsset;

        EditorGUILayout.LabelField("Sub-State Machine", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(nameof(SubStateMachineAsset.NestedStateMachine)));

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(nameof(SubStateMachineAsset.EntryState)));

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(nameof(SubStateMachineAsset.ExitTransitions)));

        // Show validation errors
        if (!subMachine.Validate(out string error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
```

### Testing Step 6
Manual testing:
1. Open state machine in editor
2. Add sub-state machine node
3. Double-click to "dive in"
4. Verify breadcrumb shows hierarchy
5. Navigate back
6. Edit sub-machine properties in inspector

**✅ Checkpoint**: Editor shows hierarchy, navigation works

---

## Final Validation

### Checklist

**Runtime**:
- [ ] SubStateMachineBlob compiles
- [ ] StateMachineStack component works
- [ ] Stack push/pop works correctly
- [ ] Entry state resolution works
- [ ] Exit transitions evaluate correctly
- [ ] Burst compilation succeeds

**Authoring**:
- [ ] SubStateMachineAsset creates successfully
- [ ] Validation detects errors
- [ ] Nesting depth calculates correctly

**Baking**:
- [ ] Recursive blob building works
- [ ] Max depth stored in blob

**Bridge**:
- [ ] Unity sub-machines read correctly
- [ ] No flattening occurs
- [ ] Hierarchy preserved 1:1

**Editor**:
- [ ] Sub-machine nodes display
- [ ] "Dive in/out" navigation works
- [ ] Breadcrumb shows path
- [ ] Inspector edits properties

**Testing**:
- [ ] All unit tests pass
- [ ] Integration tests pass
- [ ] Manual testing passes

---

## Success Metrics

- ✅ Max nesting depth: 8 levels
- ✅ No flattening or name mangling
- ✅ Visual hierarchical navigation
- ✅ Performance within 5% of flat structure
- ✅ Zero technical debt

---

## Quick Reference

### Key Structures

```csharp
// Runtime
SubStateMachineBlob
StateMachineStack
StateMachineContext

// Authoring
SubStateMachineAsset

// Conversion
SubStateMachineBlobConverter

// Bridge
SubStateMachineData

// Editor
SubStateMachineNodeView
SubStateMachineInspector
```

### Key Methods

```csharp
// Evaluation
GetCurrentBlob()
EnterSubStateMachine()
ExitCurrentSubStateMachine()

// Baking
BuildSubStateMachines()

// Bridge
ReadSubStateMachine()
ReadExitTransitions()

// Editor
NavigateToSubMachine()
NavigateBack()
```

---

## Next: Start Implementation

Begin with **Step 1: Runtime Structures** (Days 1-2).

Follow the pattern from Any State:
1. Create structures
2. Write unit tests
3. Validate compilation
4. Move to next step

**Let's code!** 🚀
