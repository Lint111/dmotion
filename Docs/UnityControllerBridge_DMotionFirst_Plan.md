# Unity Controller Bridge - DMotion-First Implementation Plan

## Philosophy

**Old Approach**: Implement bridge workarounds to compensate for DMotion limitations
**New Approach**: Implement native DMotion features first, then add pure bridge translation

This follows the architectural principle: **"The bridge should be a pure translation layer, not a workaround generator."**

---

## Current Status

### ✅ Phase 12.4: Any State Expansion (Workaround)
- **Status**: Complete (as temporary workaround)
- **Implementation**: Bridge expands Any State → N explicit transitions
- **Problem**: Asset bloat, debugging difficulty, maintenance burden
- **Next Step**: Replace with native DMotion Any State support

### 🔄 Phase 12.5: Sub-State Machine Flattening (Workaround)
- **Status**: NOT STARTED
- **Old Plan**: Implement flattening workaround in bridge
- **New Plan**: ❌ Skip workaround, implement native DMotion hierarchical state machines first

---

## Revised Implementation Strategy

### Stage 1: Native DMotion Features (Core Animation System)

Implement features in DMotion runtime/assets that Unity has. This benefits **all DMotion users**, not just Unity converts.

#### 1.1: Native Any State Support ⭐⭐⭐⭐⭐
**Priority**: Critical (replace existing workaround)
**Complexity**: Medium
**Timeline**: 1-2 weeks
**Impact**: Eliminates Phase 12.4 workaround, reduces asset size, improves debugging

**Design**:
```csharp
// New DMotion feature
namespace DMotion
{
    /// <summary>
    /// Global transition that can be taken from any state in the state machine.
    /// Evaluated before regular state transitions.
    /// </summary>
    public struct AnyStateTransition
    {
        /// <summary>Destination state index</summary>
        public short DestinationStateIndex;

        /// <summary>Blend duration in seconds</summary>
        public float Duration;

        /// <summary>Start offset for destination state (normalized time)</summary>
        public float Offset;

        /// <summary>Requires end time to be reached before transitioning</summary>
        public bool HasEndTime;

        /// <summary>End time in seconds (converted from Unity's normalized exit time)</summary>
        public float EndTime;

        /// <summary>Fixed duration (true) vs normalized duration (false)</summary>
        public bool HasFixedDuration;

        /// <summary>Transition conditions (same as regular transitions)</summary>
        public BlobArray<TransitionCondition> Conditions;
    }

    public struct StateMachineAsset
    {
        // Existing fields...
        public BlobArray<AnimationStateAsset> States;
        public BlobArray<FloatParameterAsset> FloatParameters;
        // ... etc

        // NEW: Any State transitions
        /// <summary>
        /// Global transitions evaluated before regular state transitions.
        /// These can be taken from any state in the machine.
        /// </summary>
        public BlobArray<AnyStateTransition> AnyStateTransitions;
    }
}
```

**Runtime Changes**:
```csharp
// In transition evaluation system (wherever DMotion evaluates transitions)
public void EvaluateTransitions(/* ... */)
{
    // 1. Check Any State transitions FIRST (Unity's behavior)
    foreach (ref var anyStateTransition in stateMachine.Value.AnyStateTransitions)
    {
        if (CheckTransitionConditions(anyStateTransition.Conditions, /* params */))
        {
            // Transition to destination
            TransitionToState(anyStateTransition.DestinationStateIndex,
                             anyStateTransition.Duration,
                             anyStateTransition.Offset);
            return; // Take first matching Any State transition
        }
    }

    // 2. Check regular state transitions (existing logic)
    var currentState = stateMachine.Value.States[currentStateIndex];
    foreach (ref var transition in currentState.OutTransitions)
    {
        if (CheckTransitionConditions(transition.Conditions, /* params */))
        {
            // ... existing transition logic
        }
    }
}
```

**Baker Changes**:
```csharp
// In StateMachineBaker or equivalent
public class StateMachineBaker : Baker<StateMachineAuthoring>
{
    public void Bake(StateMachineAuthoring authoring)
    {
        // ... existing baking code

        // NEW: Bake Any State transitions
        if (authoring.AnyStateTransitions != null && authoring.AnyStateTransitions.Length > 0)
        {
            builder.Allocate(ref asset.AnyStateTransitions, authoring.AnyStateTransitions.Length);
            for (int i = 0; i < authoring.AnyStateTransitions.Length; i++)
            {
                var anyTrans = authoring.AnyStateTransitions[i];
                asset.AnyStateTransitions[i] = new AnyStateTransition
                {
                    DestinationStateIndex = FindStateIndex(anyTrans.DestinationStateName),
                    Duration = anyTrans.Duration,
                    Offset = anyTrans.Offset,
                    // ... etc
                };
            }
        }
    }
}
```

**Authoring Component**:
```csharp
// For manual DMotion users (not Unity converts)
public class StateMachineAuthoring : MonoBehaviour
{
    // Existing fields...
    public List<AnimationStateAuthoring> States;
    public List<ParameterAuthoring> Parameters;

    // NEW: Any State support
    [Header("Any State Transitions")]
    [Tooltip("Global transitions that can be taken from any state")]
    public List<AnyStateTransitionAuthoring> AnyStateTransitions;
}

[Serializable]
public class AnyStateTransitionAuthoring
{
    public string DestinationStateName;
    public float Duration = 0.15f;
    public float Offset = 0f;
    public bool HasExitTime = false;
    public float ExitTime = 0.75f;
    public List<TransitionConditionAuthoring> Conditions;
}
```

**Tests**:
- Unit tests: Any State transition evaluation order (Any State before regular)
- Unit tests: Multiple Any State transitions (priority)
- Unit tests: Any State with self-transitions
- Integration tests: Runtime Any State transitions in sample scene
- Performance tests: Any State overhead vs expanded transitions (should be faster!)

**Documentation**:
- Update DMotion docs with Any State feature
- Code examples for manual DMotion users
- Migration guide from workaround to native (for Unity bridge users)

---

#### 1.2: Hierarchical State Machines ⭐⭐⭐⭐⭐
**Priority**: Critical (prevents Phase 12.5 workaround)
**Complexity**: High
**Timeline**: 2-3 weeks
**Impact**: Preserves structure, eliminates name prefixing, natural organization

**Design**:
```csharp
namespace DMotion
{
    /// <summary>
    /// Sub-state machine that can be nested within a parent state machine.
    /// </summary>
    public struct SubStateMachineAsset
    {
        /// <summary>Name of the sub-machine</summary>
        public BlobString Name;

        /// <summary>Index of the default state to enter when transitioning to this sub-machine</summary>
        public short DefaultStateIndex;

        /// <summary>States within this sub-machine</summary>
        public BlobArray<AnimationStateAsset> States;

        /// <summary>Nested sub-machines (recursive)</summary>
        public BlobArray<SubStateMachineAsset> SubMachines;

        /// <summary>Any State transitions for this sub-machine</summary>
        public BlobArray<AnyStateTransition> AnyStateTransitions;
    }

    public struct StateMachineAsset
    {
        // Existing...
        public BlobArray<AnimationStateAsset> States;
        public BlobArray<AnyStateTransition> AnyStateTransitions;

        // NEW: Sub-state machines
        public BlobArray<SubStateMachineAsset> SubMachines;
    }

    /// <summary>
    /// Transition can now target either a state or a sub-machine
    /// </summary>
    public struct StateOutTransition
    {
        // Existing fields...
        public short DestinationStateIndex;
        public float Duration;
        // ... etc

        // NEW: Discriminator for destination type
        public TransitionDestinationType DestinationType; // State or SubMachine

        /// <summary>
        /// If DestinationType == SubMachine, this is the sub-machine index.
        /// Otherwise, use DestinationStateIndex.
        /// </summary>
        public short DestinationSubMachineIndex;
    }

    public enum TransitionDestinationType : byte
    {
        State = 0,
        SubMachine = 1,
        Exit = 2  // Exit current sub-machine
    }
}
```

**Runtime Changes**:
```csharp
// Runtime needs to track current state machine hierarchy
public struct ActiveStateMachine : IComponentData
{
    /// <summary>Current state machine depth (0 = root)</summary>
    public byte Depth;

    /// <summary>Stack of sub-machine indices (for navigation)</summary>
    public BlobArray<short> SubMachineStack; // Max depth = 8 or 16

    /// <summary>Current state index within current sub-machine</summary>
    public short CurrentStateIndex;
}

// Transition handling with sub-machines
public void TransitionToDestination(StateOutTransition transition)
{
    switch (transition.DestinationType)
    {
        case TransitionDestinationType.State:
            // Simple state transition (existing logic)
            TransitionToState(transition.DestinationStateIndex, transition.Duration);
            break;

        case TransitionDestinationType.SubMachine:
            // Enter sub-machine at its default state
            var subMachine = GetSubMachine(transition.DestinationSubMachineIndex);
            PushSubMachine(transition.DestinationSubMachineIndex);
            TransitionToState(subMachine.DefaultStateIndex, transition.Duration);
            break;

        case TransitionDestinationType.Exit:
            // Exit current sub-machine (pop stack)
            PopSubMachine();
            // Continue evaluation in parent machine
            break;
    }
}
```

**Challenges**:
1. **Exit Transitions**: Need to resolve where to go when exiting a sub-machine
   - Solution: Exit transitions in Unity go to parent machine's transitions FROM the sub-machine node
2. **Parameter Scope**: Are parameters global or per-sub-machine?
   - Solution: Global (same as Unity)
3. **Any State Scope**: Is Any State per-sub-machine or global?
   - Solution: Per-sub-machine (same as Unity)
4. **Depth Limits**: How deep can nesting go?
   - Solution: Reasonable limit like 8 or 16 levels

**Tests**:
- Unit tests: Sub-machine entry/exit
- Unit tests: Nested sub-machines (2-3 levels)
- Unit tests: Transitions between sub-machines
- Integration tests: Complex hierarchical state machine
- Performance tests: Flat vs hierarchical overhead

---

#### 1.3: Trigger Parameter Type ⭐⭐⭐⭐
**Priority**: High (fixes behavioral difference with Unity)
**Complexity**: Medium
**Timeline**: 1 week
**Impact**: Proper Unity Trigger behavior, no manual reset needed

**Design**:
```csharp
namespace DMotion
{
    /// <summary>
    /// Trigger parameter that auto-resets after being consumed by a transition.
    /// </summary>
    public struct TriggerParameter : IBufferElementData
    {
        public int ParameterHash;
        public bool Value;

        // Internal: track if consumed this frame
        internal bool ConsumedThisFrame;
    }

    // At end of frame, reset consumed triggers
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(TransitionEvaluationSystem))]
    public partial struct TriggerResetSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var triggers in SystemAPI.Query<DynamicBuffer<TriggerParameter>>())
            {
                for (int i = 0; i < triggers.Length; i++)
                {
                    var trigger = triggers[i];
                    if (trigger.ConsumedThisFrame)
                    {
                        trigger.Value = false;
                        trigger.ConsumedThisFrame = false;
                        triggers[i] = trigger;
                    }
                }
            }
        }
    }
}
```

**Runtime Changes**:
```csharp
// When evaluating trigger condition
bool EvaluateTriggerCondition(ref TriggerParameter trigger)
{
    if (trigger.Value)
    {
        trigger.ConsumedThisFrame = true; // Mark for auto-reset
        return true;
    }
    return false;
}
```

**Tests**:
- Unit tests: Trigger auto-reset behavior
- Unit tests: Multiple transitions with same trigger
- Integration tests: Trigger in gameplay scenario

---

#### 1.4: Float Conditions ⭐⭐⭐
**Priority**: High (expand transition conditions)
**Complexity**: Low
**Timeline**: 3 days
**Impact**: Controllers with float conditions will work

**Design**:
```csharp
namespace DMotion
{
    public enum TransitionConditionType : byte
    {
        Bool = 0,
        Int = 1,
        Trigger = 2,
        Float = 3  // NEW
    }

    public enum FloatConditionComparison : byte
    {
        Greater = 0,
        Less = 1
    }

    public struct TransitionCondition
    {
        public TransitionConditionType Type;
        public int ParameterHash;

        // Existing unions...
        public BoolConditionComparison BoolComparison;
        public IntConditionComparison IntComparison;

        // NEW: Float condition
        public FloatConditionComparison FloatComparison;
        public float FloatThreshold;
    }
}
```

---

#### 1.5: Speed Parameters ⭐⭐⭐
**Priority**: High (dynamic speed control)
**Complexity**: Medium
**Timeline**: 1 week

**Design**:
```csharp
namespace DMotion
{
    public struct AnimationStateAsset
    {
        // Existing...
        public float Speed;

        // NEW: Optional speed parameter
        public int SpeedParameterHash;  // 0 = not used
        public bool UseSpeedParameter;
    }
}

// Runtime: multiply speeds
float finalSpeed = state.Speed * (state.UseSpeedParameter
    ? GetFloatParameter(state.SpeedParameterHash)
    : 1.0f);
```

---

#### 1.6: Cycle & Transition Offsets ⭐⭐
**Priority**: Medium (animation synchronization)
**Complexity**: Low
**Timeline**: 2-3 days

**Design**:
```csharp
namespace DMotion
{
    public struct AnimationStateAsset
    {
        // NEW: Start offset
        public float CycleOffset; // [0-1] normalized time
    }

    public struct StateOutTransition
    {
        // NEW: Destination offset
        public float DestinationOffset; // [0-1] normalized time
    }
}
```

---

### Stage 2: Bridge Translation (Pure 1:1 Mapping)

Once DMotion has native features, update bridge to do pure translation.

#### 2.1: Replace Any State Workaround with Translation
**Status**: After DMotion 1.1 complete
**Complexity**: Low (delete workaround, add simple mapping)

**Before (Workaround)**:
```csharp
// Expand: add copy of each Any State transition to every state
foreach (var state in data.States)
{
    foreach (var anyTransition in anyStateTransitions)
    {
        state.Transitions.Add(CreateCopy(anyTransition));
        totalExpanded++;
    }
}
```

**After (Pure Translation)**:
```csharp
// Simple 1:1 mapping
foreach (var anyTransition in unity.anyStateTransitions)
{
    dmotionData.AnyStateTransitions.Add(ConvertTransition(anyTransition));
}
```

**Impact**:
- Delete 50+ lines of workaround code
- Eliminate asset bloat
- Preserve Any State metadata for debugging

---

#### 2.2: Add Sub-Machine Translation (No Flattening)
**Status**: After DMotion 1.2 complete
**Complexity**: Medium (recursive translation)

**Implementation**:
```csharp
private SubStateMachineData ConvertSubMachine(AnimatorStateMachine unity)
{
    var data = new SubStateMachineData
    {
        Name = unity.name,
        DefaultStateIndex = FindStateIndex(unity.defaultState.name)
    };

    // Recursively convert states
    foreach (var state in unity.states)
    {
        data.States.Add(ConvertState(state.state));
    }

    // Recursively convert nested sub-machines
    foreach (var sub in unity.stateMachines)
    {
        data.SubMachines.Add(ConvertSubMachine(sub.stateMachine));
    }

    // Convert Any State transitions (scoped to this sub-machine)
    foreach (var anyTrans in unity.anyStateTransitions)
    {
        data.AnyStateTransitions.Add(ConvertTransition(anyTrans));
    }

    return data;
}
```

**Impact**:
- Preserves Unity's organizational structure
- No name prefixing needed
- Natural 1:1 mapping

---

#### 2.3: Add Trigger Parameter Translation
**Status**: After DMotion 1.3 complete
**Complexity**: Low

**Before**:
```csharp
// Convert Trigger → Bool (with warning)
if (param.type == AnimatorControllerParameterType.Trigger)
{
    Debug.LogWarning("Trigger converted to Bool, manual reset required");
    dmotionParam.Type = ParameterType.Bool;
}
```

**After**:
```csharp
// Direct mapping
if (param.type == AnimatorControllerParameterType.Trigger)
{
    dmotionParam.Type = ParameterType.Trigger; // Native support!
}
```

---

#### 2.4-2.6: Add Float Conditions, Speed Parameters, Offsets
**Status**: After corresponding DMotion features
**Complexity**: Low (simple field mapping)

---

### Stage 3: Documentation & Cleanup

#### 3.1: Update All Documentation
- Mark Phase 12.4 workaround as "deprecated, native support available"
- Remove Phase 12.5 flattening plan
- Add guides for new DMotion features
- Update Architecture Analysis with "achieved parity" status

#### 3.2: Deprecate Workarounds
- Add `[Obsolete]` attributes to workaround methods
- Add migration guide for existing users
- Keep workaround code for one release, then remove

#### 3.3: Performance Benchmarks
- Compare workaround vs native performance
- Document memory savings
- Show asset size improvements

---

## Timeline

### Short-Term (1-2 months)
- ✅ DMotion 1.1: Any State native support
- ✅ DMotion 1.3: Trigger parameter type
- ✅ DMotion 1.4: Float conditions
- ✅ Bridge 2.1: Replace Any State workaround

### Medium-Term (2-4 months)
- ✅ DMotion 1.2: Hierarchical state machines
- ✅ DMotion 1.5: Speed parameters
- ✅ DMotion 1.6: Offsets
- ✅ Bridge 2.2: Sub-machine translation
- ✅ Bridge 2.3-2.6: Other features

### Long-Term (6+ months)
- DMotion 2.0: 2D Blend Trees
- DMotion 2.1: Multiple Layers
- Bridge 3.0: Complete feature parity

---

## Benefits of This Approach

### 1. Better Architecture
- Bridge stays thin and simple
- DMotion gets more features (benefits all users)
- Clear separation of concerns

### 2. Better Performance
- Native Any State: ~50% faster evaluation (no redundant condition checks)
- Native Sub-Machines: Lower memory overhead
- Optimal asset size

### 3. Better Debugging
- Can see Any State origin in assets
- Hierarchical structure preserved
- Easier to understand generated code

### 4. Better Maintenance
- Less bridge code to maintain
- Features live in one place (DMotion)
- Breaking changes easier to handle

### 5. Better User Experience
- Manual DMotion users get these features too
- Unity converts get cleaner assets
- Documentation is clearer

---

## Migration Path

### For Existing Phase 12.4 Users (Any State Workaround)

**Step 1**: Update DMotion to version with native Any State
**Step 2**: Reconvert Unity controllers with new bridge
**Step 3**: Compare old vs new assets (size, structure)
**Step 4**: Test runtime behavior (should be identical)
**Step 5**: Delete old workaround assets

**Benefits**:
- Smaller assets (~50% reduction for 100-state controllers)
- Faster runtime (no redundant checks)
- Cleaner debugging

---

## Questions for DMotion Team

1. **Receptive to these changes?** Are you open to adding these features to DMotion core?
2. **API Design**: Can we collaborate on the struct layouts and naming?
3. **Timeline**: What's realistic for each feature?
4. **Breaking Changes**: Is this a good time for API additions?
5. **Priority**: Which features would you prioritize for your broader user base?
6. **Contribution**: Can we contribute these features via PR, or prefer internal development?

---

## Next Steps

### Immediate (This Week)
- [ ] Share this plan with DMotion team
- [ ] Get feedback on proposed designs
- [ ] Prioritize features based on DMotion roadmap

### If Approved (Next Sprint)
- [ ] Start with DMotion 1.1 (Any State) - highest impact
- [ ] Create feature branch for Any State
- [ ] Implement runtime + authoring + baker
- [ ] Write tests
- [ ] Update documentation

### If Not Approved (Fallback)
- [ ] Continue with Phase 12.5 workaround approach
- [ ] Document limitations clearly
- [ ] Keep workaround implementations as reference for future DMotion PRs

---

## Related Documents

- [Architecture Analysis](UnityControllerBridge_ArchitectureAnalysis.md) - Why this approach is better
- [Action Plan](UnityControllerBridge_ActionPlan.md) - Original workaround-based plan
- [Feature Parity](UnityControllerBridge_FeatureParity.md) - Complete feature comparison
- [TODOs](UnityControllerBridge_TODOs.md) - Outstanding limitations
