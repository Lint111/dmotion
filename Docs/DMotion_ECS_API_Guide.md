# DMotion ECS API Guide

## Overview

This guide explains how to interact with DMotion's animation system from your gameplay ECS systems. DMotion uses Unity DOTS (ECS) for high-performance animation, and all animation state is stored in ECS components and buffers.

**Key Concepts**:
- **Parameters** control animation behavior (stored in dynamic buffers)
- **State** tracks which animation is playing (stored in components)
- **Events** fire callbacks when animation events occur (stored in buffers)
- **Root Motion** provides character movement from animations (stored in components)

---

## Table of Contents

1. [ECS Components Overview](#ecs-components-overview)
2. [Setting Animation Parameters](#setting-animation-parameters)
3. [Querying Animation State](#querying-animation-state)
4. [Responding to Animation Events](#responding-to-animation-events)
5. [Playing One-Shot Animations](#playing-one-shot-animations)
6. [Root Motion Handling](#root-motion-handling)
7. [Complete Examples](#complete-examples)
8. [Performance Best Practices](#performance-best-practices)

---

## ECS Components Overview

### Animation Parameters (Dynamic Buffers)

Parameters control transitions and blend trees. Each entity has separate buffers for each parameter type:

```csharp
DynamicBuffer<FloatParameter>  // Float parameters
DynamicBuffer<IntParameter>    // Int parameters
DynamicBuffer<BoolParameter>   // Bool parameters (includes converted Triggers)
```

**Structure**:
```csharp
public struct FloatParameter : IBufferElementData
{
    public int Hash;      // Parameter name hash
    public float Value;   // Current value
}
```

**Key Points**:
- Parameters are identified by **hash** (not name) for performance
- Use `StateMachineParameterUtils.GetHashCode("ParameterName")` to get hash
- Each parameter has a fixed index in the buffer (set at bake time)

---

### Animation State Components

These components track the current animation state:

```csharp
AnimationStateMachine          // Internal: blob references and current state
AnimationCurrentState          // Internal: which state is active
AnimationStateTransitionRequest // Public: request state transition
DynamicBuffer<AnimationState>  // Internal: state instances
```

**You typically don't access these directly** - the state machine system manages them based on parameters.

---

### Animation Event Buffer

```csharp
DynamicBuffer<RaisedAnimationEvent> // Events raised this frame
```

**Structure**:
```csharp
public struct RaisedAnimationEvent : IBufferElementData
{
    public Entity Entity;     // Entity that raised the event
    public int EventHash;     // Event name hash
}
```

**Key Points**:
- Buffer is cleared each frame by DMotion
- Check for events in your systems before DMotion clears them
- Events are identified by hash for performance

---

### Root Motion Components

```csharp
RootDeltaTranslation  // Movement this frame
RootDeltaRotation     // Rotation this frame
```

**Structure**:
```csharp
public struct RootDeltaTranslation : IComponentData
{
    public float3 Value;  // Delta position in local space
}

public struct RootDeltaRotation : IComponentData
{
    public quaternion Value;  // Delta rotation
}
```

**Key Points**:
- Only present if `RootMotionMode` is set on authoring component
- DMotion writes these each frame
- Your system reads and applies them to entity transform

---

## Setting Animation Parameters

### Basic API: Extension Methods

Use extension methods from `StateMachineParameterUtils` for convenient parameter access:

```csharp
using DMotion;
using Unity.Collections;
using Unity.Entities;

partial struct MySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (floatParams, boolParams, intParams) in
                 SystemAPI.Query<DynamicBuffer<FloatParameter>,
                                 DynamicBuffer<BoolParameter>,
                                 DynamicBuffer<IntParameter>>())
        {
            // Set float parameter by name
            floatParams.SetValue("Speed", 5.0f);

            // Set bool parameter by name
            boolParams.SetValue("IsGrounded", true);

            // Set int parameter by name
            intParams.SetValue("StateID", 2);
        }
    }
}
```

**Available Extension Methods**:

```csharp
// Set value by name
buffer.SetValue("ParameterName", value);

// Set value by hash (faster, pre-compute hash)
int hash = StateMachineParameterUtils.GetHashCode("Speed");
buffer.SetValue(hash, value);

// Get value by name
if (buffer.TryGetValue("Speed", out float speed))
{
    // Use speed
}

// Get value by hash
float speed = buffer.GetValue<FloatParameter, float>(hash);
```

---

### Performance: Using Hashes

**Best Practice**: Pre-compute parameter hashes to avoid repeated string operations.

```csharp
[BurstCompile]
partial struct LocomotionSystem : ISystem
{
    // Pre-compute hashes (do once at system creation)
    private static readonly int SpeedHash =
        StateMachineParameterUtils.GetHashCode("Speed");
    private static readonly int IsGroundedHash =
        StateMachineParameterUtils.GetHashCode("IsGrounded");

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Use hashes in hot loop (much faster!)
        foreach (var floatParams in
                 SystemAPI.Query<DynamicBuffer<FloatParameter>>())
        {
            floatParams.SetValue(SpeedHash, 5.0f);
        }
    }
}
```

---

### Performance: Using Parameter References

For maximum performance, create parameter references that cache the buffer index:

```csharp
[BurstCompile]
partial struct LocomotionSystem : ISystem
{
    // Component to store parameter refs (added to entity)
    public struct LocomotionParams : IComponentData
    {
        public StateMachineParameterRef<FloatParameter, float> Speed;
        public StateMachineParameterRef<BoolParameter, bool> IsGrounded;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Fastest: direct index access via cached reference
        foreach (var (paramRefs, floatParams, boolParams) in
                 SystemAPI.Query<LocomotionParams,
                                 DynamicBuffer<FloatParameter>,
                                 DynamicBuffer<BoolParameter>>())
        {
            // SetValue uses cached index (no hash lookup!)
            paramRefs.Speed.SetValue(floatParams, 5.0f);
            paramRefs.IsGrounded.SetValue(boolParams, true);
        }
    }
}
```

**Creating Parameter References** (typically in OnCreate or baker):

```csharp
public void OnCreate(ref SystemState state)
{
    // Query entities to initialize parameter refs
    foreach (var (entity, floatParams, boolParams) in
             SystemAPI.Query<RefRW<Entity>,
                             DynamicBuffer<FloatParameter>,
                             DynamicBuffer<BoolParameter>>()
             .WithNone<LocomotionParams>())
    {
        // Create refs from parameter buffers
        var paramRefs = new LocomotionParams
        {
            Speed = floatParams.CreateRef<FloatParameter, float>(
                StateMachineParameterUtils.GetHashCode("Speed")),
            IsGrounded = boolParams.CreateRef<BoolParameter, bool>(
                StateMachineParameterUtils.GetHashCode("IsGrounded"))
        };

        state.EntityManager.AddComponentData(entity, paramRefs);
    }
}
```

---

### Example: Locomotion Parameters

Complete example setting locomotion parameters based on character velocity:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
partial struct CharacterLocomotionSystem : ISystem
{
    // Pre-computed hashes
    private static readonly int SpeedHash =
        StateMachineParameterUtils.GetHashCode("Speed");
    private static readonly int DirectionXHash =
        StateMachineParameterUtils.GetHashCode("DirectionX");
    private static readonly int DirectionYHash =
        StateMachineParameterUtils.GetHashCode("DirectionY");
    private static readonly int IsGroundedHash =
        StateMachineParameterUtils.GetHashCode("IsGrounded");

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dt = SystemAPI.Time.DeltaTime;

        foreach (var (velocity, isGrounded, floatParams, boolParams) in
                 SystemAPI.Query<RefRO<CharacterVelocity>,
                                 RefRO<CharacterGroundedFlag>,
                                 DynamicBuffer<FloatParameter>,
                                 DynamicBuffer<BoolParameter>>())
        {
            // Calculate speed (magnitude of horizontal velocity)
            float speed = math.length(velocity.ValueRO.Value.xz);

            // Calculate direction (normalized velocity)
            float3 dir = math.normalizesafe(velocity.ValueRO.Value);

            // Set animation parameters
            floatParams.SetValue(SpeedHash, speed);
            floatParams.SetValue(DirectionXHash, dir.x);
            floatParams.SetValue(DirectionYHash, dir.z);
            boolParams.SetValue(IsGroundedHash, isGrounded.ValueRO.Value);
        }
    }
}
```

---

## Querying Animation State

### Getting Current State Name (Debug Only)

In the editor or debug builds, you can access state names:

```csharp
#if UNITY_EDITOR || DEBUG
foreach (var (stateMachine, debug) in
         SystemAPI.Query<RefRO<AnimationStateMachine>,
                         AnimationStateMachineDebug>())
{
    var currentStateIndex = stateMachine.ValueRO.CurrentState.StateIndex;
    var currentState = debug.StateMachineAsset.States[currentStateIndex];

    UnityEngine.Debug.Log($"Current state: {currentState.name}");
}
#endif
```

**⚠️ Warning**: This is for debugging only. In release builds, state names are not available.

---

### Checking if State Has Changed

You can track state changes by comparing previous and current state:

```csharp
// Add component to track previous state
public struct PreviousAnimationState : IComponentData
{
    public ushort StateIndex;
}

[BurstCompile]
partial struct AnimationStateChangeSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (stateMachine, prevState, entity) in
                 SystemAPI.Query<RefRO<AnimationStateMachine>,
                                 RefRW<PreviousAnimationState>>()
                 .WithEntityAccess())
        {
            var currentIndex = stateMachine.ValueRO.CurrentState.StateIndex;

            if (currentIndex != prevState.ValueRO.StateIndex)
            {
                // State changed!
                OnStateChanged(entity, prevState.ValueRO.StateIndex, currentIndex);

                prevState.ValueRW.StateIndex = currentIndex;
            }
        }
    }

    private void OnStateChanged(Entity entity, ushort from, ushort to)
    {
        // Handle state change
    }
}
```

---

## Responding to Animation Events

### Basic Event Checking

Check if an event was raised this frame using `AnimationEventUtils`:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
partial struct FootstepSystem : ISystem
{
    // Pre-compute event hash
    private static readonly int FootstepLeftHash =
        new FixedString64Bytes("Footstep_Left").GetHashCode();
    private static readonly int FootstepRightHash =
        new FixedString64Bytes("Footstep_Right").GetHashCode();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (events, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>>()
                 .WithEntityAccess())
        {
            // Check if specific event was raised
            if (events.WasEventRaised(FootstepLeftHash))
            {
                PlayFootstepSound(entity, FootType.Left);
            }

            if (events.WasEventRaised(FootstepRightHash))
            {
                PlayFootstepSound(entity, FootType.Right);
            }
        }
    }

    private void PlayFootstepSound(Entity entity, FootType foot)
    {
        // Play footstep audio, spawn particles, etc.
    }
}
```

**Available Extension Methods**:

```csharp
// Check by name
if (events.WasEventRaised("EventName"))
{
    // Event was raised
}

// Check by hash (faster)
int hash = new FixedString64Bytes("EventName").GetHashCode();
if (events.WasEventRaised(hash))
{
    // Event was raised
}

// Get index of event in buffer
if (events.WasEventRaised(hash, out int index))
{
    var eventData = events[index];
    // Use event data
}
```

---

### Processing All Events

Loop through all events raised this frame:

```csharp
[BurstCompile]
partial struct AnimationEventLogSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (events, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>>()
                 .WithEntityAccess())
        {
            for (int i = 0; i < events.Length; i++)
            {
                var evt = events[i];
                HandleEvent(entity, evt.EventHash);
            }
        }
    }

    private void HandleEvent(Entity entity, int eventHash)
    {
        // Route event to appropriate handler based on hash
    }
}
```

---

### Event-Driven System Example

Complete example of event-driven weapon system:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateAfter(typeof(AnimationStateMachineSystem))]
partial struct WeaponEventSystem : ISystem
{
    // Pre-compute event hashes
    private static readonly int AttackConnectHash =
        new FixedString64Bytes("AttackConnect").GetHashCode();
    private static readonly int AttackEndHash =
        new FixedString64Bytes("AttackEnd").GetHashCode();
    private static readonly int WeaponTrailStartHash =
        new FixedString64Bytes("WeaponTrailStart").GetHashCode();
    private static readonly int WeaponTrailEndHash =
        new FixedString64Bytes("WeaponTrailEnd").GetHashCode();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (events, weapon, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>,
                                 RefRO<WeaponComponent>>()
                 .WithEntityAccess())
        {
            // Damage frame
            if (events.WasEventRaised(AttackConnectHash))
            {
                // Enable hitbox for damage detection
                ecb.SetComponentEnabled<WeaponHitbox>(entity, true);
            }

            // Attack finished
            if (events.WasEventRaised(AttackEndHash))
            {
                // Disable hitbox
                ecb.SetComponentEnabled<WeaponHitbox>(entity, false);

                // Reset attack state
                ecb.SetComponent(entity, new AttackState { IsAttacking = false });
            }

            // Visual effects
            if (events.WasEventRaised(WeaponTrailStartHash))
            {
                // Start weapon trail VFX
                ecb.SetComponentEnabled<WeaponTrailVFX>(entity, true);
            }

            if (events.WasEventRaised(WeaponTrailEndHash))
            {
                // Stop weapon trail VFX
                ecb.SetComponentEnabled<WeaponTrailVFX>(entity, false);
            }
        }

        ecb.Playback(state.EntityManager);
    }
}
```

---

## Playing One-Shot Animations

### Using PlaySingleClipRequest

Play a single animation clip outside the state machine (e.g., hit reactions, emotes):

```csharp
using DMotion;
using Unity.Entities;

partial struct PlayHitReactionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Query for entities that need to play hit reaction
        foreach (var (hitReaction, singleClipRef, entity) in
                 SystemAPI.Query<RefRO<HitReactionRequest>,
                                 RefRO<HitReactionClipRef>>()
                 .WithEntityAccess())
        {
            // Create play request
            var playRequest = PlaySingleClipRequest.New(
                in singleClipRef.ValueRO.ClipRef,
                loop: false,                    // One-shot
                transitionDuration: 0.1f        // Blend time
            );

            // Add request component
            ecb.SetComponent(entity, playRequest);

            // Remove trigger component
            ecb.RemoveComponent<HitReactionRequest>(entity);
        }

        ecb.Playback(state.EntityManager);
    }
}
```

**PlaySingleClipRequest Constructor Parameters**:

```csharp
PlaySingleClipRequest.New(
    BlobAssetReference<SkeletonClipSetBlob> clips,  // Clip set
    BlobAssetReference<ClipEventsBlob> clipEvents,  // Event set
    int clipIndex,                                   // Which clip
    float transitionDuration = 0.15f,               // Blend time
    float speed = 1f,                                // Playback speed
    bool loop = true                                 // Loop or one-shot
)
```

---

### One-Shot with State Machine Return

Play one-shot and return to state machine when done:

```csharp
// Component to track one-shot
public struct OneShotTracking : IComponentData
{
    public bool WaitingForReturn;
}

[UpdateAfter(typeof(AnimationStateMachineSystem))]
partial struct OneShotSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Start one-shot
        foreach (var (emoteRequest, clipRef, entity) in
                 SystemAPI.Query<RefRO<PlayEmoteRequest>,
                                 RefRO<EmoteClipRef>>()
                 .WithEntityAccess()
                 .WithNone<OneShotTracking>())
        {
            var playRequest = PlaySingleClipRequest.New(
                in clipRef.ValueRO.ClipRef,
                loop: false,
                transitionDuration: 0.2f
            );

            ecb.SetComponent(entity, playRequest);
            ecb.AddComponent(entity, new OneShotTracking
            {
                WaitingForReturn = true
            });
            ecb.RemoveComponent<PlayEmoteRequest>(entity);
        }

        // Check if one-shot finished (AnimationState.Time > clip duration)
        // This requires checking clip length and comparing to state time
        // DMotion will automatically return to state machine when clip ends

        ecb.Playback(state.EntityManager);
    }
}
```

---

## Root Motion Handling

### Reading Root Motion

Read root motion deltas and apply to character controller:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateAfter(typeof(AnimationStateMachineSystem))]
partial struct ApplyRootMotionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rootTranslation, rootRotation, localTransform) in
                 SystemAPI.Query<RefRO<RootDeltaTranslation>,
                                 RefRO<RootDeltaRotation>,
                                 RefRW<LocalTransform>>())
        {
            // Apply root translation (in local space)
            localTransform.ValueRW.Position += rootTranslation.ValueRO.Value;

            // Apply root rotation
            localTransform.ValueRW.Rotation = math.mul(
                rootRotation.ValueRO.Value,
                localTransform.ValueRO.Rotation
            );
        }
    }
}
```

---

### Root Motion with Character Controller

Integrate root motion with physics-based character controller:

```csharp
[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(CharacterControllerSystem))]
partial struct RootMotionToVelocitySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dt = SystemAPI.Time.DeltaTime;

        foreach (var (rootTranslation, rootRotation, velocity, transform) in
                 SystemAPI.Query<RefRO<RootDeltaTranslation>,
                                 RefRO<RootDeltaRotation>,
                                 RefRW<CharacterVelocity>,
                                 RefRO<LocalTransform>>())
        {
            // Convert root motion translation to velocity
            // (root motion is in local space, rotate to world space)
            float3 worldDelta = math.mul(
                transform.ValueRO.Rotation,
                rootTranslation.ValueRO.Value
            );

            velocity.ValueRW.Value = worldDelta / dt;

            // Apply root rotation directly to transform
            // (handled by separate system)
        }
    }
}
```

---

### Selective Root Motion

Apply only translation, ignore rotation (or vice versa):

```csharp
// Component to control root motion behavior
public struct RootMotionSettings : IComponentData
{
    public bool ApplyTranslation;
    public bool ApplyRotation;
}

[BurstCompile]
partial struct SelectiveRootMotionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rootTrans, rootRot, settings, transform) in
                 SystemAPI.Query<RefRO<RootDeltaTranslation>,
                                 RefRO<RootDeltaRotation>,
                                 RefRO<RootMotionSettings>,
                                 RefRW<LocalTransform>>())
        {
            if (settings.ValueRO.ApplyTranslation)
            {
                transform.ValueRW.Position += rootTrans.ValueRO.Value;
            }

            if (settings.ValueRO.ApplyRotation)
            {
                transform.ValueRW.Rotation = math.mul(
                    rootRot.ValueRO.Value,
                    transform.ValueRO.Rotation
                );
            }
        }
    }
}
```

---

## Complete Examples

### Example 1: Basic Character Controller

Complete system controlling character animation based on input:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
partial struct CharacterAnimationControllerSystem : ISystem
{
    // Pre-compute parameter hashes
    private static readonly int SpeedHash =
        StateMachineParameterUtils.GetHashCode("Speed");
    private static readonly int IsGroundedHash =
        StateMachineParameterUtils.GetHashCode("IsGrounded");
    private static readonly int JumpHash =
        StateMachineParameterUtils.GetHashCode("Jump");

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (velocity, grounded, input, floatParams, boolParams) in
                 SystemAPI.Query<RefRO<CharacterVelocity>,
                                 RefRO<IsGrounded>,
                                 RefRO<PlayerInput>,
                                 DynamicBuffer<FloatParameter>,
                                 DynamicBuffer<BoolParameter>>())
        {
            // Calculate speed
            float speed = math.length(velocity.ValueRO.Horizontal);

            // Set parameters
            floatParams.SetValue(SpeedHash, speed);
            boolParams.SetValue(IsGroundedHash, grounded.ValueRO.Value);

            // Trigger jump
            if (input.ValueRO.JumpPressed)
            {
                boolParams.SetValue(JumpHash, true);
            }
            else
            {
                boolParams.SetValue(JumpHash, false);
            }
        }
    }
}
```

---

### Example 2: Combat System with Events

Complete combat system using animation events:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateAfter(typeof(AnimationStateMachineSystem))]
partial struct CombatAnimationSystem : ISystem
{
    // Event hashes
    private static readonly int AttackStartHash =
        new FixedString64Bytes("AttackStart").GetHashCode();
    private static readonly int AttackHitHash =
        new FixedString64Bytes("AttackHit").GetHashCode();
    private static readonly int AttackEndHash =
        new FixedString64Bytes("AttackEnd").GetHashCode();

    // Parameter hashes
    private static readonly int AttackTriggerHash =
        StateMachineParameterUtils.GetHashCode("Attack");
    private static readonly int ComboCountHash =
        StateMachineParameterUtils.GetHashCode("ComboCount");

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (events, input, combatState, boolParams, intParams, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>,
                                 RefRO<CombatInput>,
                                 RefRW<CombatState>,
                                 DynamicBuffer<BoolParameter>,
                                 DynamicBuffer<IntParameter>>()
                 .WithEntityAccess())
        {
            // Input: trigger attack
            if (input.ValueRO.AttackPressed && !combatState.ValueRO.IsAttacking)
            {
                boolParams.SetValue(AttackTriggerHash, true);
                combatState.ValueRW.IsAttacking = true;
            }

            // Event: attack started
            if (events.WasEventRaised(AttackStartHash))
            {
                // Start attack VFX, audio
                ecb.AddComponent(entity, new AttackVFXRequest());
            }

            // Event: hit frame
            if (events.WasEventRaised(AttackHitHash))
            {
                // Enable hitbox for damage detection
                ecb.SetComponentEnabled<WeaponHitbox>(entity, true);
            }

            // Event: attack finished
            if (events.WasEventRaised(AttackEndHash))
            {
                // Disable hitbox
                ecb.SetComponentEnabled<WeaponHitbox>(entity, false);

                // Increment combo count
                int currentCombo = intParams.GetValue<IntParameter, int>(ComboCountHash);
                intParams.SetValue(ComboCountHash, currentCombo + 1);

                // Reset attack state
                combatState.ValueRW.IsAttacking = false;
                boolParams.SetValue(AttackTriggerHash, false);

                // Reset combo after delay (handled by separate system)
                ecb.AddComponent(entity, new ComboResetTimer { Time = 1.0f });
            }
        }

        ecb.Playback(state.EntityManager);
    }
}
```

---

### Example 3: Procedural Animation Blending

Blend between state machine and procedural animation:

```csharp
using DMotion;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateAfter(typeof(AnimationStateMachineSystem))]
partial struct AimOffsetSystem : ISystem
{
    // Parameter hashes
    private static readonly int AimPitchHash =
        StateMachineParameterUtils.GetHashCode("AimPitch");
    private static readonly int AimYawHash =
        StateMachineParameterUtils.GetHashCode("AimYaw");

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (aimTarget, transform, floatParams) in
                 SystemAPI.Query<RefRO<AimTarget>,
                                 RefRO<LocalTransform>,
                                 DynamicBuffer<FloatParameter>>())
        {
            // Calculate aim direction
            float3 toTarget = math.normalize(
                aimTarget.ValueRO.Position - transform.ValueRO.Position
            );

            // Convert to pitch/yaw angles
            float pitch = math.asin(toTarget.y);
            float yaw = math.atan2(toTarget.x, toTarget.z);

            // Normalize to [-1, 1] range
            float pitchNorm = pitch / (math.PI / 2f);
            float yawNorm = yaw / math.PI;

            // Set aim parameters (controls 2D blend tree)
            floatParams.SetValue(AimPitchHash, pitchNorm);
            floatParams.SetValue(AimYawHash, yawNorm);
        }
    }
}
```

---

## Performance Best Practices

### 1. Pre-Compute Hashes

**Bad** (computes hash every frame):
```csharp
floatParams.SetValue("Speed", speed);  // String allocation + hash every frame!
```

**Good** (hash computed once):
```csharp
private static readonly int SpeedHash =
    StateMachineParameterUtils.GetHashCode("Speed");

floatParams.SetValue(SpeedHash, speed);  // Just index lookup
```

---

### 2. Use Parameter References for Hot Paths

**Good** (hash lookup every frame):
```csharp
floatParams.SetValue(SpeedHash, speed);
```

**Better** (cached index, no lookup):
```csharp
paramRefs.Speed.SetValue(floatParams, speed);
```

---

### 3. Batch Parameter Updates

**Bad** (query for each parameter type separately):
```csharp
foreach (var floats in SystemAPI.Query<DynamicBuffer<FloatParameter>>())
    floats.SetValue(SpeedHash, speed);

foreach (var bools in SystemAPI.Query<DynamicBuffer<BoolParameter>>())
    bools.SetValue(JumpHash, true);
```

**Good** (query once for all parameter types):
```csharp
foreach (var (floats, bools) in
         SystemAPI.Query<DynamicBuffer<FloatParameter>,
                         DynamicBuffer<BoolParameter>>())
{
    floats.SetValue(SpeedHash, speed);
    bools.SetValue(JumpHash, true);
}
```

---

### 4. Process Events Early

Process animation events **before** DMotion clears the buffer (same frame):

```csharp
// BAD: Might run after DMotion clears events
[UpdateInGroup(typeof(SimulationSystemGroup))]
partial struct EventHandlerSystem : ISystem { }

// GOOD: Guaranteed to run right after DMotion
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AnimationStateMachineSystem))]
partial struct EventHandlerSystem : ISystem { }
```

---

### 5. Use Burst Compilation

Always use `[BurstCompile]` on systems and jobs that access animation data:

```csharp
[BurstCompile]  // ← CRITICAL for performance!
partial struct MyAnimationSystem : ISystem
{
    [BurstCompile]  // ← Also on OnUpdate!
    public void OnUpdate(ref SystemState state)
    {
        // ...
    }
}
```

---

### 6. Avoid String Allocations

**Bad** (allocates string every time):
```csharp
if (events.WasEventRaised("Footstep"))  // String allocation!
```

**Good** (uses fixed string, no allocation):
```csharp
private static readonly int FootstepHash =
    new FixedString64Bytes("Footstep").GetHashCode();

if (events.WasEventRaised(FootstepHash))  // No allocation
```

---

## Common Patterns

### Pattern 1: State-Based Behavior

React to entering/exiting animation states:

```csharp
public struct AnimationStateTracker : IComponentData
{
    public ushort CurrentStateIndex;
    public ushort PreviousStateIndex;
}

partial struct StateChangeReactionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (stateMachine, tracker) in
                 SystemAPI.Query<RefRO<AnimationStateMachine>,
                                 RefRW<AnimationStateTracker>>())
        {
            var current = stateMachine.ValueRO.CurrentState.StateIndex;

            if (current != tracker.ValueRO.CurrentStateIndex)
            {
                // State changed
                OnExitState(tracker.ValueRO.CurrentStateIndex);
                OnEnterState(current);

                tracker.ValueRW.PreviousStateIndex = tracker.ValueRO.CurrentStateIndex;
                tracker.ValueRW.CurrentStateIndex = current;
            }
        }
    }
}
```

---

### Pattern 2: Event-Driven State Machine

Use animation events to trigger state changes:

```csharp
partial struct AnimationDrivenStateMachine : ISystem
{
    private static readonly int TransitionCompleteHash =
        new FixedString64Bytes("TransitionComplete").GetHashCode();

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (events, gameState, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>,
                                 RefRW<GameplayState>>()
                 .WithEntityAccess())
        {
            if (events.WasEventRaised(TransitionCompleteHash))
            {
                // Animation signaled state transition is complete
                gameState.ValueRW.State = GameplayState.NextState(
                    gameState.ValueRO.State
                );
            }
        }

        ecb.Playback(state.EntityManager);
    }
}
```

---

### Pattern 3: Animation-Driven Timing

Use animation events for precise timing:

```csharp
partial struct AbilityTimingSystem : ISystem
{
    private static readonly int AbilityCastPointHash =
        new FixedString64Bytes("CastPoint").GetHashCode();
    private static readonly int AbilityRecoveryHash =
        new FixedString64Bytes("Recovery").GetHashCode();

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (events, ability, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>,
                                 RefRW<AbilityState>>()
                 .WithEntityAccess())
        {
            // Exact frame to apply ability effect
            if (events.WasEventRaised(AbilityCastPointHash))
            {
                ecb.AddComponent(entity, new ApplyAbilityEffectRequest());
            }

            // Ability cooldown starts
            if (events.WasEventRaised(AbilityRecoveryHash))
            {
                ability.ValueRW.CooldownRemaining = ability.ValueRO.CooldownDuration;
            }
        }

        ecb.Playback(state.EntityManager);
    }
}
```

---

## Debugging Tips

### 1. Visualize Parameters

Create a debug system to log parameter values:

```csharp
#if UNITY_EDITOR || DEBUG
partial struct DebugParameterSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (floatParams, boolParams, entity) in
                 SystemAPI.Query<DynamicBuffer<FloatParameter>,
                                 DynamicBuffer<BoolParameter>>()
                 .WithEntityAccess())
        {
            UnityEngine.Debug.Log($"Entity {entity.Index} parameters:");

            for (int i = 0; i < floatParams.Length; i++)
            {
                UnityEngine.Debug.Log($"  Float[{i}]: {floatParams[i].Value}");
            }

            for (int i = 0; i < boolParams.Length; i++)
            {
                UnityEngine.Debug.Log($"  Bool[{i}]: {boolParams[i].Value}");
            }
        }
    }
}
#endif
```

---

### 2. Log Animation Events

Track which events are firing:

```csharp
#if UNITY_EDITOR || DEBUG
partial struct DebugEventsSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (events, entity) in
                 SystemAPI.Query<DynamicBuffer<RaisedAnimationEvent>>()
                 .WithEntityAccess())
        {
            if (events.Length > 0)
            {
                UnityEngine.Debug.Log($"Entity {entity.Index} raised {events.Length} events:");
                for (int i = 0; i < events.Length; i++)
                {
                    UnityEngine.Debug.Log($"  Event hash: {events[i].EventHash}");
                }
            }
        }
    }
}
#endif
```

---

### 3. Visualize State Machine

Use Gizmos to visualize current animation state:

```csharp
#if UNITY_EDITOR
partial struct AnimationDebugGizmosSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (stateMachine, debug, transform) in
                 SystemAPI.Query<RefRO<AnimationStateMachine>,
                                 AnimationStateMachineDebug,
                                 RefRO<LocalTransform>>())
        {
            var currentStateIndex = stateMachine.ValueRO.CurrentState.StateIndex;
            var currentState = debug.StateMachineAsset.States[currentStateIndex];

            // Draw state name above character
            var pos = transform.ValueRO.Position;
            UnityEngine.Debug.DrawLine(pos, pos + new float3(0, 3, 0), UnityEngine.Color.green);
            // Use Handles.Label in OnDrawGizmos for text
        }
    }
}
#endif
```

---

## Additional Resources

- **DMotion Source**: `/home/user/dmotion/Runtime/`
- **Component Definitions**: `/home/user/dmotion/Runtime/Components/`
- **Utility Functions**: `/home/user/dmotion/Runtime/Utils/`
- **Example Tests**: `/home/user/dmotion/Tests/Performance/StateMachinePerformanceTestSystem.cs`

---

## Summary

**Key Takeaways**:

1. **Parameters** are stored in dynamic buffers, accessed by hash
2. **Pre-compute hashes** for performance (avoid string operations in hot paths)
3. **Events** must be processed the same frame (before DMotion clears buffer)
4. **Root motion** is provided as delta components (translation + rotation)
5. **One-shot animations** use `PlaySingleClipRequest` component
6. **Always use Burst** for animation systems

**Performance Hierarchy** (fastest to slowest):
1. `paramRef.SetValue(buffer, value)` - Cached index ⚡
2. `buffer.SetValue(hash, value)` - Pre-computed hash ⚡
3. `buffer.SetValue("Name", value)` - String hash every frame 🐢

**Recommended System Update Order**:
1. Input → Parameters
2. DMotion (AnimationStateMachineSystem)
3. Event Handlers (immediately after DMotion)
4. Root Motion Application
5. Other gameplay systems

Follow these patterns and your DMotion animations will be fast, responsive, and maintainable!
