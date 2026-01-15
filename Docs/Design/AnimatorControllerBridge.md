# AnimatorController Bridge Feature

## Overview

A bridge system that allows users to use standard Unity AnimatorController assets with DMotion's ECS animation system. Users configure animations using the familiar Animator window, and DMotion handles the runtime execution in DOTS.

## Problem Statement

Current DMotion adoption barriers:
1. Users must learn custom state machine authoring workflow
2. Custom GUI panels for animation setup (unfamiliar)
3. Cannot reuse existing AnimatorController assets from projects
4. Steep learning curve for teams already familiar with Mecanim
5. Asset store animations often come with pre-configured AnimatorControllers

## Proposed Solution

New `AnimatorControllerBridgeAuthoring` component that:
1. References a standard Unity `RuntimeAnimatorController` asset
2. During baking, converts the controller graph to DMotion ECS structures
3. At runtime, ECS components drive the same logic with DOTS performance
4. Users keep familiar workflow, get ECS benefits

## Architecture

### Authoring Components

```csharp
public class AnimatorControllerBridgeAuthoring : MonoBehaviour
{
    public RuntimeAnimatorController Controller;
    public Avatar Avatar;
    
    // Optional: parameter defaults, layer masks, etc.
}
```

### Baked ECS Components

```csharp
// Core state machine (reuse existing DMotion components where possible)
public struct AnimatorBridge : IComponentData
{
    public BlobAssetReference<AnimatorControllerBlob> ControllerBlob;
    public int CurrentStateHash;
    public float NormalizedTime;
}

// Parameter storage
public struct AnimatorParameters : IComponentData
{
    public BlobAssetReference<AnimatorParametersBlob> ParametersBlob;
}

// Dynamic buffer for parameter values
public struct AnimatorParameterValue : IBufferElementData
{
    public int NameHash;
    public AnimatorParameterType Type;
    public float FloatValue;
    public int IntValue;
    public bool BoolValue;
}
```

### Blob Structures

```csharp
public struct AnimatorControllerBlob
{
    public BlobArray<AnimatorLayerBlob> Layers;
    public BlobArray<AnimatorParameterDefinition> Parameters;
}

public struct AnimatorLayerBlob
{
    public BlobArray<AnimatorStateBlob> States;
    public BlobArray<AnimatorTransitionBlob> AnyStateTransitions;
    public int DefaultStateIndex;
    public float DefaultWeight;
    public AnimatorLayerBlendingMode BlendingMode;
}

public struct AnimatorStateBlob
{
    public int NameHash;
    public int ClipIndex; // Index into SkeletonClipSetBlob
    public float Speed;
    public float CycleOffset;
    public bool Loop;
    public BlobArray<AnimatorTransitionBlob> Transitions;
    
    // For blend trees
    public bool IsBlendTree;
    public BlobAssetReference<BlendTreeBlob> BlendTree;
}

public struct AnimatorTransitionBlob
{
    public int DestinationStateIndex;
    public float Duration;
    public float Offset;
    public bool HasExitTime;
    public float ExitTime;
    public bool HasFixedDuration;
    public BlobArray<AnimatorConditionBlob> Conditions;
}

public struct AnimatorConditionBlob
{
    public int ParameterNameHash;
    public AnimatorConditionMode Mode;
    public float Threshold;
}
```

## Baking Process

### Baker Implementation

```csharp
public class AnimatorControllerBridgeBaker : Baker<AnimatorControllerBridgeAuthoring>
{
    public override void Bake(AnimatorControllerBridgeAuthoring authoring)
    {
        if (authoring.Controller == null) return;
        
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        
        // 1. Extract all clips from controller
        var clips = ExtractAnimationClips(authoring.Controller);
        
        // 2. Build clips blob (delegate to Kinemation SmartBlobber)
        // ... 
        
        // 3. Convert controller structure to blob
        var controllerBlob = ConvertControllerToBlob(authoring.Controller, clips);
        
        // 4. Add components
        AddComponent(entity, new AnimatorBridge { ControllerBlob = controllerBlob });
        AddComponent(entity, new AnimatorParameters { ... });
        AddBuffer<AnimatorParameterValue>(entity);
    }
}
```

### Conversion Mapping

| Mecanim Concept | DMotion Equivalent |
|-----------------|-------------------|
| AnimatorState | AnimatorStateBlob → ClipIndex |
| BlendTree 1D | LinearBlendBlob |
| BlendTree 2D | New: BlendTree2DBlob |
| Transition | AnimatorTransitionBlob |
| Parameter (Float) | AnimatorParameterValue buffer |
| Parameter (Bool) | AnimatorParameterValue buffer |
| Parameter (Int) | AnimatorParameterValue buffer |
| Parameter (Trigger) | AnimatorParameterValue + TriggerConsumed flag |
| Layer (Override) | Separate state machine, final pose override |
| Layer (Additive) | Additive blending post-process |
| AnyState transition | Checked every frame from any state |
| StateMachineBehaviour | NOT SUPPORTED (managed code) |
| AnimationEvent | Convert to DMotion AnimationEvent system |

## Runtime Systems

### Parameter Update System

```csharp
[BurstCompile]
public partial struct AnimatorParameterSystem : ISystem
{
    // Provides API for setting parameters from gameplay code
    // Similar to Animator.SetFloat, SetBool, SetInteger, SetTrigger
}
```

### State Machine Evaluation System

```csharp
[BurstCompile]
public partial struct AnimatorBridgeStateMachineSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // For each entity with AnimatorBridge:
        // 1. Evaluate transitions from current state
        // 2. Check AnyState transitions
        // 3. Update current state, handle transition blending
        // 4. Update normalized time
    }
}
```

### Clip Sampling Integration

The existing DMotion systems (ClipSamplingSystem, BlendAnimationStatesSystem) should work with minimal modification since we're outputting to the same AnimationState structures.

## API for Gameplay Code

```csharp
// Extension methods for easy parameter access
public static class AnimatorBridgeExtensions
{
    public static void SetFloat(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash, float value);
    public static void SetBool(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash, bool value);
    public static void SetInteger(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash, int value);
    public static void SetTrigger(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash);
    
    public static float GetFloat(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash);
    public static bool GetBool(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash);
    public static int GetInteger(this DynamicBuffer<AnimatorParameterValue> buffer, int nameHash);
}

// Usage in gameplay systems:
var parameters = SystemAPI.GetBuffer<AnimatorParameterValue>(entity);
parameters.SetFloat(SpeedHash, currentSpeed);
parameters.SetBool(IsGroundedHash, isGrounded);
parameters.SetTrigger(JumpHash);
```

## Limitations & Non-Goals

### Not Supported (v1)
1. **StateMachineBehaviours** - Managed code, cannot run in Burst. Provide ECS alternatives.
2. **Sub-state machines** - Flatten during baking or defer to v2
3. **IK** - Separate system, out of scope
4. **Root motion** - Requires separate integration
5. **Animator.Play/CrossFade API** - May add later
6. **Layer sync** - Complex, defer

### Potential Future Enhancements (v2+)
1. Sub-state machine support
2. Direct BlendTree (no state machine)
3. Parameter drivers (auto-set parameters from components)
4. Animator override controllers
5. Runtime controller modification

## Implementation Phases

### Phase 1: Core Foundation
- [ ] AnimatorControllerBridgeAuthoring component
- [ ] Baker that extracts clips and builds SkeletonClipSetBlob
- [ ] Basic AnimatorControllerBlob with single layer, no blend trees
- [ ] Parameter components and buffer
- [ ] Basic state machine evaluation (no transitions)

### Phase 2: Transitions
- [ ] Transition blob structure
- [ ] Exit time support
- [ ] Condition evaluation (Float/Bool/Int comparisons)
- [ ] Transition blending
- [ ] AnyState transitions

### Phase 3: Blend Trees
- [ ] 1D Blend Tree → LinearBlend conversion
- [ ] 2D Blend Tree support (Simple Directional, Freeform)
- [ ] Nested blend trees

### Phase 4: Layers
- [ ] Multiple layer support
- [ ] Override blending mode
- [ ] Additive blending mode
- [ ] Layer weights

### Phase 5: Polish
- [ ] Trigger parameter support (auto-consume)
- [ ] Animation events conversion
- [ ] Error handling and validation
- [ ] Performance optimization
- [ ] Documentation and samples

## Testing Strategy

1. **Unit Tests** - Blob conversion, parameter evaluation, transition conditions
2. **Integration Tests** - Full baking pipeline with real AnimatorControllers
3. **Performance Tests** - Compare with existing DMotion, compare with Mecanim
4. **Sample Projects** - Port existing Mecanim setups to validate compatibility

## Open Questions

1. **How to handle clip references?** AnimatorController uses AnimationClip directly, need to ensure SmartBlobber processes them correctly.

2. **Parameter name hashing** - Use Animator.StringToHash for compatibility, or custom hashing?

3. **Transition interruption** - Support immediate interruption or wait for transition to complete?

4. **Layer mask on skeletal bones** - How to handle avatar masks for layers?

5. **Mirror parameter** - Support clip mirroring from controller settings?

## References

- Unity AnimatorController: https://docs.unity3d.com/Manual/class-AnimatorController.html
- Unity AnimatorControllerLayer: https://docs.unity3d.com/ScriptReference/Animations.AnimatorControllerLayer.html
- Existing DMotion architecture: `/Runtime/Systems/`, `/Runtime/Authoring/`
- Kinemation clip sampling: Latios.Kinemation

---

*Document created: 2026-01-13*
*Status: Draft*
*Author: AI-assisted design session*
