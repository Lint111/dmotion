# DMotion Event System Migration Guide

## Overview

We are consolidating all DMotion editor events into a unified `PropertyChanged` pattern for better maintainability, type safety, and consistency. This migration maintains backward compatibility while introducing the new system.

## Migration Strategy

### Phase 1: Dual Event System (Current)
- Keep existing events for backward compatibility
- Add new unified events alongside legacy events
- Update key components to use new system
- Gradually migrate subscribers

### Phase 2: Deprecation Warnings
- Add `[Obsolete]` attributes to legacy events
- Update all internal components to use new system
- Provide migration helpers

### Phase 3: Legacy Removal
- Remove deprecated events
- Clean up migration code

## New Event System Benefits

### Before (Legacy)
```csharp
// Multiple event subscriptions
StateMachineEditorEvents.OnStateAdded += OnStateAdded;
StateMachineEditorEvents.OnStateRemoved += OnStateRemoved;
StateMachineEditorEvents.OnStateSelected += OnStateSelected;
AnimationPreviewEvents.OnBlendPosition1DChanged += OnBlendChanged;
AnimationPreviewEvents.OnPlayStateChanged += OnPlayStateChanged;
// ... 50+ more events

private void OnStateAdded(StateMachineAsset machine, AnimationStateAsset state) { }
private void OnStateRemoved(StateMachineAsset machine, AnimationStateAsset state) { }
private void OnStateSelected(StateMachineAsset machine, AnimationStateAsset state) { }
// ... 50+ more handlers
```

### After (Unified)
```csharp
// Single event subscription
DMotionEditorEventSystem.PropertyChanged += OnPropertyChanged;

private void OnPropertyChanged(object sender, DMotionPropertyChangedEventArgs e)
{
    // Filter by property and context
    switch (e.PropertyName)
    {
        case DMotionEditorEventSystem.PropertyNames.StateAdded:
            HandleStateAdded(e);
            break;
        case DMotionEditorEventSystem.PropertyNames.StateSelected:
            HandleStateSelected(e);
            break;
        case DMotionEditorEventSystem.PropertyNames.BlendPosition1D:
            HandleBlendPositionChanged(e);
            break;
    }
}

private void HandleStateAdded(DMotionPropertyChangedEventArgs e)
{
    var machine = e.Context.StateMachine;
    var state = e.NewValue as AnimationStateAsset;
    // Handle state added
}
```

## Key Improvements

### 1. Rich Context Information
```csharp
// Old: Limited context
OnStateSelected(StateMachineAsset machine, AnimationStateAsset state)

// New: Rich context with layer information
e.Context.StateMachine     // The state machine
e.Context.State           // The selected state  
e.Context.Layer           // The layer (if applicable)
e.Context.LayerIndex      // Layer index (if applicable)
e.Context.LayerStateMachine // Layer's state machine (if navigated)
```

### 2. Type-Safe Property Names
```csharp
// Old: String literals (error-prone)
if (propertyName == "StateSelected") { }

// New: Compile-time constants
if (e.PropertyName == DMotionEditorEventSystem.PropertyNames.StateSelected) { }
```

### 3. Old/New Value Tracking
```csharp
// Old: No value history
OnParameterChanged(machine, parameter)

// New: Value change tracking
e.OldValue  // Previous value
e.NewValue  // New value
e.Timestamp // When the change occurred
```

### 4. Centralized Event Flow
```csharp
// Easy to add logging, debugging, or analytics
DMotionEditorEventSystem.PropertyChanged += (sender, e) =>
{
    Debug.Log($"[{e.Timestamp}] {e.PropertyName} changed in {e.Context.StateMachine?.name}");
};
```

## Migration Examples

### Component Migration
```csharp
public class MyEditorComponent
{
    // OLD WAY
    private void OnEnable()
    {
        StateMachineEditorEvents.OnStateAdded += OnStateAdded;
        StateMachineEditorEvents.OnStateRemoved += OnStateRemoved;
        AnimationPreviewEvents.OnBlendPosition1DChanged += OnBlendChanged;
    }
    
    private void OnDisable()
    {
        StateMachineEditorEvents.OnStateAdded -= OnStateAdded;
        StateMachineEditorEvents.OnStateRemoved -= OnStateRemoved;
        AnimationPreviewEvents.OnBlendPosition1DChanged -= OnBlendChanged;
    }
    
    // NEW WAY
    private void OnEnable()
    {
        DMotionEditorEventSystem.PropertyChanged += OnPropertyChanged;
    }
    
    private void OnDisable()
    {
        DMotionEditorEventSystem.PropertyChanged -= OnPropertyChanged;
    }
    
    private void OnPropertyChanged(object sender, DMotionPropertyChangedEventArgs e)
    {
        // Only handle events for our state machine
        if (e.Context.StateMachine != currentStateMachine) return;
        
        switch (e.PropertyName)
        {
            case DMotionEditorEventSystem.PropertyNames.StateAdded:
                HandleStateAdded(e.NewValue as AnimationStateAsset);
                break;
                
            case DMotionEditorEventSystem.PropertyNames.StateRemoved:
                HandleStateRemoved(e.OldValue as AnimationStateAsset);
                break;
                
            case DMotionEditorEventSystem.PropertyNames.BlendPosition1D:
                if (e.Context.State == targetState)
                    HandleBlendChanged((float)e.NewValue);
                break;
        }
    }
}
```

### Event Raising Migration
```csharp
// OLD WAY
StateMachineEditorEvents.RaiseStateAdded(machine, state);

// NEW WAY (both for backward compatibility)
public static void RaiseStateAdded(StateMachineAsset machine, AnimationStateAsset state)
{
    // Legacy events (for backward compatibility)
    OnStateAdded?.Invoke(machine, state);
    OnStateMachineChanged?.Invoke(machine);
    
    // New unified event system
    DMotionEditorEventSystem.RaiseStateAdded(machine, state);
    DMotionEditorEventSystem.RaiseStateMachineChanged(machine);
}
```

## Property Name Reference

### State Machine Structure
- `StateAdded`, `StateRemoved`, `DefaultStateChanged`
- `TransitionAdded`, `TransitionRemoved`
- `ParameterAdded`, `ParameterRemoved`, `ParameterChanged`
- `LayerAdded`, `LayerRemoved`, `LayerWeight`, `LayerEnabled`

### Selection & Navigation
- `StateSelected`, `TransitionSelected`, `SelectionCleared`
- `LayerEntered`, `LayerExited`, `NavigateToState`

### Preview & Playback
- `NormalizedTime`, `IsPlaying`, `IsLooping`, `PlaybackSpeed`
- `BlendPosition1D`, `BlendPosition2D`, `TransitionProgress`
- `PreviewCreated`, `PreviewDisposed`, `PreviewError`

### UI State
- `EditMode`, `ClipSelectedForPreview`, `RepaintRequested`

## Context Filtering Patterns

### Filter by State Machine
```csharp
if (e.Context.StateMachine != myStateMachine) return;
```

### Filter by Layer
```csharp
if (e.Context.LayerIndex != myLayerIndex) return;
```

### Filter by Component
```csharp
if (e.Context.ComponentId != myComponentId) return;
```

### Filter by Property Group
```csharp
var blendProperties = new[] {
    PropertyNames.BlendPosition1D,
    PropertyNames.BlendPosition2D,
    PropertyNames.TransitionProgress
};

if (blendProperties.Contains(e.PropertyName))
{
    HandleBlendPropertyChanged(e);
}
```

## Migration Timeline

- ✅ **Phase 1a**: Create unified event system
- ✅ **Phase 1b**: Update key StateMachineEditorEvents methods
- 🚧 **Phase 1c**: Update AnimationPreviewEvents methods
- ⏳ **Phase 1d**: Update component-specific events
- ⏳ **Phase 2**: Add deprecation warnings and migrate all subscribers
- ⏳ **Phase 3**: Remove legacy events

## Testing Strategy

1. **Dual Event Verification**: Ensure both legacy and new events fire correctly
2. **Context Validation**: Verify rich context information is accurate
3. **Performance Testing**: Ensure unified system doesn't impact performance
4. **Migration Testing**: Test components using both old and new systems