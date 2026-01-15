# Unity Controller Bridge - Implementation Plan

## Overview

The Unity Controller Bridge is an authoring-time tool that automatically converts Unity AnimatorController assets to DMotion's StateMachineAsset format. It acts as a transparent glue layer, monitoring controller changes and re-baking authoring assets in the background.

## Key Features

- **One Conversion Per Controller**: Multiple entities using the same controller benefit from a single conversion
- **Automatic Re-baking**: Detects controller changes and triggers background re-conversion
- **Debounced Updates**: Prevents thrashing during rapid edits (configurable debounce timer)
- **Play Mode Safety**: Blocks play mode entry if controllers are dirty, auto-converts first
- **Shared Resources**: SmartBaker automatically deduplicates blobs across entities
- **Subscene Compatible**: Works seamlessly with subscene baking
- **Runtime Ready**: Spawned prefabs use pre-converted assets

---

## Architecture

### Data Flow

```
AnimatorController (Unity asset, source of truth)
         ↓ (one-to-one mapping via registry)
UnityControllerBridgeAsset (ScriptableObject, project asset)
         ↓ (automatic conversion when dirty)
StateMachineAsset (Generated DMotion authoring, shared)
         ↓ (referenced by many authoring components)
AnimationStateMachineAuthoring × N (scene components)
         ↓ (SmartBaker deduplication)
StateMachineBlob (SHARED blob, automatic deduplication)
         ↓ (referenced by many entities)
Entity × N (each with own CurrentState, Parameters)
```

### Shared vs Per-Entity Data

#### SHARED DATA (BlobAssets - Immutable, Reference-Counted)

Created once via SmartBaker, referenced by many entities:

1. **StateMachineBlob**:
   - Default state index
   - All state definitions (types, speeds, loops)
   - All transition definitions (conditions, durations, targets)
   - Single clip and blend tree data

2. **SkeletonClipSetBlob** (Kinemation):
   - All animation clips for the skeleton
   - Compressed animation data

3. **ClipEventsBlob**:
   - Animation events for all clips
   - Event names and normalized times

**Deduplication**: SmartBaker automatically detects when multiple entities use the same StateMachineAsset and creates only one blob.

#### PER-ENTITY DATA (Components/Buffers - Mutable Runtime State)

Each entity has its own:

1. **AnimationStateMachine** (component):
   - Blob references (just pointers to shared data)
   - `CurrentState` (which state this entity is in)

2. **Parameter Buffers** (runtime values):
   - `DynamicBuffer<BoolParameter>` - this entity's bool values
   - `DynamicBuffer<IntParameter>` - this entity's int values
   - `DynamicBuffer<FloatParameter>` - this entity's float values

3. **State Tracking**:
   - `AnimationState`, `ClipSampler`, `AnimationStateTransition`
   - Per-entity animation playback state

---

## Component Details

### 1. UnityControllerBridgeAsset (ScriptableObject)

**Purpose**: Project-level asset that converts a Unity AnimatorController to DMotion StateMachineAsset.

**Location**: `Runtime/Authoring/UnityControllerBridgeAsset.cs`

**Key Members**:
```csharp
[SerializeField] private AnimatorController _sourceController;
[SerializeField] private StateMachineAsset _generatedStateMachine; // Output (read-only)
[SerializeField] private bool _isDirty = true;
[SerializeField] private string _lastConversionTime;
[SerializeField, HideInInspector] private string _cachedControllerHash;

public static event Action<UnityControllerBridgeAsset> OnBridgeRegistered;
public static event Action<UnityControllerBridgeAsset> OnBridgeUnregistered;
public static event Action<UnityControllerBridgeAsset> OnBridgeDirty;

public AnimatorController SourceController { get; }
public StateMachineAsset GeneratedStateMachine { get; }
public bool IsDirty { get; }
public bool CheckForChanges(); // Hash-based dirty detection
public void MarkClean();
public void MarkDirty();
```

**Dirty Detection Strategy**:
- Hash AnimatorController structure (parameters, states, transitions, blend trees)
- Compare with cached hash
- Fire `OnBridgeDirty` event when mismatch detected

**Lifecycle**:
- `OnEnable()`: Register with ControllerBridgeRegistry, fire OnBridgeRegistered
- `OnDisable()`: Unregister, fire OnBridgeUnregistered

---

### 2. ControllerBridgeRegistry (Static Singleton)

**Purpose**: Central registry mapping AnimatorControllers to bridge assets. Ensures only one bridge per controller.

**Location**: `Editor/UnityControllerBridge/ControllerBridgeRegistry.cs`

**Key Members**:
```csharp
private static Dictionary<AnimatorController, UnityControllerBridgeAsset> _controllerToBridge;
private static Dictionary<string, UnityControllerBridgeAsset> _guidToBridge;

public static UnityControllerBridgeAsset GetOrCreateBridge(AnimatorController controller);
public static List<AnimationStateMachineAuthoring> FindEntitiesUsingController(AnimatorController controller);
public static int GetReferenceCount(UnityControllerBridgeAsset bridge);
public static void Register(UnityControllerBridgeAsset bridge);
public static void Unregister(UnityControllerBridgeAsset bridge);
private static void RebuildCache();
```

**Auto-Creation**:
- When `GetOrCreateBridge()` called with controller that has no bridge
- Creates `{ControllerName}_Bridge.asset` next to controller
- Automatically triggers initial conversion

**Cache Rebuilding**:
- On `[InitializeOnLoad]`
- On `EditorApplication.projectChanged`
- Scans project for all `UnityControllerBridgeAsset` instances

---

### 3. ControllerBridgeConfig (ScriptableObject)

**Purpose**: Global configuration for bridge system.

**Location**: `Editor/UnityControllerBridge/ControllerBridgeConfig.cs`

**Settings**:
```csharp
// Conversion Triggers
public bool BakeOnPlayMode = true;
public bool BakeOnDirtyDebounced = true;
public float DebounceDuration = 2f; // seconds

// Conversion Options
public bool IncludeAnimationEvents = true;
public bool PreserveGraphLayout = true;
public bool LogWarnings = true;

// Output Settings
public string OutputPath = "DMotion/GeneratedStateMachines";
public string NamingPattern = "{0}_Generated";
```

**Singleton Pattern**:
- `GetOrCreateDefault()` finds existing or creates new config
- Stored at `Assets/DMotion/ControllerBridgeConfig.asset`

---

### 4. ControllerBridgeDirtyTracker (Static Tracker)

**Purpose**: Monitors all bridges for changes, implements debouncing, blocks play mode when dirty.

**Location**: `Editor/UnityControllerBridge/ControllerBridgeDirtyTracker.cs`

**Lifecycle**:
```csharp
[InitializeOnLoad]
static ControllerBridgeDirtyTracker()
{
    EditorApplication.hierarchyChanged += OnHierarchyChanged;
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    EditorApplication.update += OnEditorUpdate;

    UnityControllerBridgeAsset.OnBridgeRegistered += OnBridgeRegistered;
    UnityControllerBridgeAsset.OnBridgeUnregistered += OnBridgeUnregistered;
    UnityControllerBridgeAsset.OnBridgeDirty += OnBridgeMarkedDirty;
}
```

**Debouncing**:
1. Bridge marks itself dirty
2. Tracker starts debounce timer (2 seconds default)
3. Additional changes reset timer
4. After timer expires → queue for conversion
5. Queue processes asynchronously

**Play Mode Blocking**:
```csharp
OnPlayModeStateChanged(PlayModeStateChange.ExitingEditMode)
{
    if (Config.BakeOnPlayMode)
    {
        var dirtyBridges = GetDirtyBridges();
        if (dirtyBridges.Count > 0)
        {
            EditorApplication.isPlaying = false; // Block play mode
            // Queue all for immediate conversion
            // Resume play mode after conversion complete
        }
    }
}
```

---

### 5. ControllerAssetPostprocessor (Asset Monitor)

**Purpose**: Detects when .controller files are modified and triggers change detection.

**Location**: `Editor/UnityControllerBridge/ControllerAssetPostprocessor.cs`

**Implementation**:
```csharp
public class ControllerAssetPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        foreach (var assetPath in importedAssets)
        {
            if (assetPath.EndsWith(".controller"))
            {
                ControllerBridgeDirtyTracker.ForceCheckAllBridges();
                break;
            }
        }
    }
}
```

---

### 6. ControllerConversionQueue (Background Processor)

**Purpose**: Queues and processes conversion jobs without blocking editor.

**Location**: `Editor/UnityControllerBridge/ControllerConversionQueue.cs`

**Key Features**:
```csharp
public static event Action<string> OnConversionStarted;
public static event Action<string, bool> OnConversionFinished; // bridgeId, success
public static event Action OnConversionComplete; // All conversions done
public static event Action<float, string> OnProgressUpdated; // progress, message

public static void Enqueue(UnityControllerBridgeAsset bridge);
public static void StartProcessing();
public static void StopProcessing();
```

**Processing**:
- Uses `EditorApplication.update` for non-blocking processing
- Processes one conversion at a time
- Duplicate detection (same bridge queued multiple times → processed once)
- Progress reporting for UI integration

---

### 7. UnityControllerConverter (Conversion Logic)

**Purpose**: Actual conversion from Unity AnimatorController to DMotion StateMachineAsset.

**Location**: `Editor/UnityControllerBridge/UnityControllerConverter.cs`

**Main Method**:
```csharp
public static StateMachineAsset ConvertController(
    AnimatorController source,
    ControllerBridgeConfig config)
{
    // 1. Convert parameters
    var parameters = ConvertParameters(source);

    // 2. Convert clips
    var clipAssets = ConvertClips(source, config.IncludeAnimationEvents);

    // 3. Convert states
    var states = ConvertStates(source, clipAssets, parameters);

    // 4. Convert transitions
    ConvertTransitions(source, states, parameters);

    // 5. Assemble StateMachineAsset
    return AssembleStateMachine(states, parameters, source.layers[0].stateMachine.defaultState);
}
```

#### Parameter Conversion

**Unity → DMotion Mapping**:
- `AnimatorControllerParameterType.Float` → `FloatParameterAsset`
- `AnimatorControllerParameterType.Bool` → `BoolParameterAsset`
- `AnimatorControllerParameterType.Int` → `IntParameterAsset`
- `AnimatorControllerParameterType.Trigger` → `BoolParameterAsset` (with warning)

#### State Conversion

**Single Clip States**:
- `AnimatorState.motion` is `AnimationClip` → `SingleClipStateAsset`
- Properties: `Speed`, `Loop`

**Blend Tree States**:
- `AnimatorState.motion` is `BlendTree` with `blendType == Simple1D` → `LinearBlendStateAsset`
- Extract children motions and thresholds → `ClipWithThreshold[]`
- Set `BlendParameter` reference
- **Note**: Only 1D blend trees supported initially

#### Transition Conversion

**Unity Condition Modes → DMotion**:
- `AnimatorConditionMode.If` (1) → `BoolConditionComparison.True`
- `AnimatorConditionMode.IfNot` (2) → `BoolConditionComparison.False`
- `AnimatorConditionMode.Greater` (3) → `IntConditionComparison.Greater`
- `AnimatorConditionMode.Less` (4) → `IntConditionComparison.Less`
- `AnimatorConditionMode.Equals` (6) → `IntConditionComparison.Equal`
- `AnimatorConditionMode.NotEqual` (7) → `IntConditionComparison.NotEqual`

**Exit Time**:
- Unity: `m_HasExitTime` (bool) + `m_ExitTime` (normalized 0-1)
- DMotion: `HasEndTime` (bool) + `EndTime` (absolute seconds)
- Conversion: `EndTime = exitTime * clipDuration`

---

### 8. AnimationStateMachineAuthoring (Modified)

**Purpose**: Scene-level component that references StateMachineAsset (can be from bridge).

**Location**: `Runtime/Authoring/AnimationStateMachineAuthoring.cs`

**Current Structure**:
```csharp
public GameObject Owner;
public Animator Animator;
public StateMachineAsset StateMachineAsset;
public RootMotionMode RootMotionMode;
public bool EnableEvents = true;
```

**Proposed Modification**:
```csharp
public enum SourceMode
{
    DirectStateMachine,      // Use DMotion StateMachineAsset directly
    UnityControllerBridge    // Use bridge asset
}

[SerializeField] private SourceMode _sourceMode = SourceMode.DirectStateMachine;

// Direct mode
[SerializeField] private StateMachineAsset _directStateMachine;

// Bridge mode
[SerializeField] private UnityControllerBridgeAsset _bridgeAsset;

public StateMachineAsset GetStateMachine()
{
    return _sourceMode switch
    {
        SourceMode.DirectStateMachine => _directStateMachine,
        SourceMode.UnityControllerBridge => _bridgeAsset?.GeneratedStateMachine,
        _ => null
    };
}
```

**Baker Update**:
```csharp
public bool Bake(AnimationStateMachineAuthoring authoring, IBaker baker)
{
    var stateMachine = authoring.GetStateMachine(); // Use resolver
    if (stateMachine == null)
    {
        Debug.LogError("StateMachine not resolved!");
        return false;
    }

    ValidateStateMachine(authoring);
    // ... rest of baking
    clipsBlobHandle = baker.RequestCreateBlobAsset(authoring.Animator, stateMachine.Clips);
    stateMachineBlobHandle = baker.RequestCreateBlobAsset(stateMachine);
    // ...
}
```

---

## Unsupported Features

The following Unity Animator features will log warnings during conversion:

1. **2D Blend Trees** - DMotion only supports 1D currently
2. **Sub-State Machines** - Not supported in DMotion v0.3.4
3. **Multiple Layers** - Planned feature, not yet implemented
4. **Trigger Parameters** - Converted to Bool (auto-reset behavior must be manual)
5. **Avatar Masks** - Not supported yet
6. **Transition Offset** - No equivalent in DMotion
7. **Interruption Settings** - Not available in DMotion
8. **Cycle Offset** - No direct equivalent
9. **Speed Parameter Multiplier** - Requires manual adjustment post-import
10. **IK Pass** - Unity layer setting, not directly convertible

---

## Workflow Examples

### Scenario 1: 100 Characters with Same Controller

1. User has `HumanoidController.controller`
2. Creates first character GameObject:
   - Adds `AnimationStateMachineAuthoring`
   - Sets mode to `UnityControllerBridge`
   - Assigns/creates bridge for `HumanoidController.controller`
3. Bridge auto-creates if needed: `HumanoidController_Bridge.asset`
4. Initial conversion triggered → generates `HumanoidController_Generated.asset`
5. Creates 99 more characters:
   - All reference same bridge asset
   - Registry prevents duplicates
6. **Result**: One conversion, 100 entities share StateMachineBlob via SmartBaker

### Scenario 2: Controller Modified

1. User modifies `HumanoidController.controller` in Animator window
2. Controller asset saves
3. `ControllerAssetPostprocessor.OnPostprocessAllAssets` fires
4. Calls `ForceCheckAllBridges()`
5. Bridge's `CheckForChanges()` detects hash mismatch → marks dirty
6. `ControllerBridgeDirtyTracker` starts 2-second debounce
7. After debounce → bridge queued for reconversion
8. Queue processes → regenerates `HumanoidController_Generated.asset`
9. **All 100 entities automatically use updated asset** (SmartBaker re-bakes with new blob)

### Scenario 3: Play Mode with Dirty Controllers

1. User modifies controller, hits Play immediately
2. `OnPlayModeStateChanged(ExitingEditMode)` fires
3. Tracker checks for dirty bridges → found
4. Blocks play mode: `EditorApplication.isPlaying = false`
5. Shows message: "Converting 1 dirty controller before play mode..."
6. Triggers immediate conversion (no debounce)
7. After conversion complete → resumes play mode automatically
8. Play mode starts with up-to-date assets

---

## Implementation Phases

### Phase 1: Bridge ScriptableObject Asset (6 tasks)
1.1. Create `UnityControllerBridgeAsset.cs` (ScriptableObject)
1.2. Add AnimatorController source reference
1.3. Add StateMachineAsset generated reference (output)
1.4. Implement hash-based dirty detection
1.5. Add static events (OnBridgeRegistered, OnBridgeUnregistered, OnBridgeDirty)
1.6. Implement OnEnable/OnDisable for registry integration

### Phase 2: Bridge Registry (7 tasks)
2.1. Create `ControllerBridgeRegistry.cs` [InitializeOnLoad]
2.2. Implement controller → bridge asset cache (Dictionary)
2.3. Add GetOrCreateBridge(AnimatorController) method
2.4. Implement auto-creation of bridge assets when needed
2.5. Add GetReferenceCount() - count entities using each bridge
2.6. Implement FindEntitiesUsingController() for debugging
2.7. Add RebuildCache() on project changes

### Phase 3: Configuration Asset (5 tasks)
3.1. Create `ControllerBridgeConfig.cs` (like SDFBakeConfig)
3.2. Add settings: BakeOnPlayMode, BakeOnDirtyDebounced, DebounceDuration
3.3. Add conversion options: IncludeAnimationEvents, PreserveGraphLayout
3.4. Add output path settings for generated StateMachineAssets
3.5. Implement GetOrCreateDefault() singleton pattern

### Phase 4: Dirty Tracking System (6 tasks)
4.1. Create `ControllerBridgeDirtyTracker.cs` [InitializeOnLoad]
4.2. Subscribe to UnityControllerBridgeAsset static events
4.3. Implement debouncing system (same as IslandDirtyTracker)
4.4. Add EditorApplication.update listener for debounce timer
4.5. Implement PlayModeStateChanged handler (block if dirty)
4.6. Queue dirty bridges to conversion queue after debounce

### Phase 5: Asset Change Detection (3 tasks)
5.1. Create `ControllerAssetPostprocessor : AssetPostprocessor`
5.2. Implement OnPostprocessAllAssets for .controller files
5.3. Trigger ForceCheckAllBridges() when controllers change

### Phase 6: Conversion Queue (6 tasks)
6.1. Create `ControllerConversionQueue.cs` (like SDFBakeQueue)
6.2. Implement Enqueue(UnityControllerBridgeAsset) with duplicate detection
6.3. Add StartProcessing() and StopProcessing()
6.4. Implement EditorApplication.update-based processing
6.5. Add events: OnConversionStarted, OnConversionFinished, OnConversionComplete
6.6. Add OnProgressUpdated event for UI

### Phase 7: Controller Converter (8 tasks)
7.1. Create `UnityControllerConverter.cs`
7.2. Implement ConvertController(AnimatorController) → StateMachineAsset
7.3. Convert parameters (Float, Bool, Int, Trigger→Bool)
7.4. Convert AnimationClips to AnimationClipAssets (with deduplication)
7.5. Extract and convert animation events
7.6. Convert states (single clips and 1D blend trees)
7.7. Convert transitions (conditions, exit time, duration)
7.8. Assemble and save StateMachineAsset with sub-assets

### Phase 8: Authoring Component Integration (6 tasks)
8.1. Modify AnimationStateMachineAuthoring to support multiple modes
8.2. Add SourceMode enum (Direct, Bridge)
8.3. Add UnityControllerBridgeAsset reference field
8.4. Implement GetStateMachine() resolver method
8.5. Update baker to use GetStateMachine() pattern
8.6. Update custom property drawer if needed

### Phase 9: Editor UI (6 tasks)
9.1. Create menu item 'Create Unity Controller Bridge' for selected controllers
9.2. Create custom inspector for UnityControllerBridgeAsset
9.3. Show dirty status, last conversion time, reference count
9.4. Add 'Force Reconvert' button
9.5. Create custom inspector for AnimationStateMachineAuthoring (show mode options)
9.6. Create ControllerBridgeManagerWindow (list all bridges, stats)

### Phase 10: Testing (5 tasks)
10.1. Test 100 entities with same controller → one conversion
10.2. Test registry auto-creates bridge when needed
10.3. Test controller modification triggers reconversion
10.4. Test subscene entities properly resolve bridges
10.5. Test reference counting is accurate

### Phase 11: Documentation (3 tasks)
11.1. Add XML documentation to all public APIs
11.2. Create usage guide (setup, multiple entities, workflows)
11.3. Document unsupported features and limitations

---

## File Structure

```
Runtime/
  Authoring/
    AnimationStateMachineAuthoring.cs (MODIFIED - add bridge support)
    UnityControllerBridgeAsset.cs (NEW)

Editor/
  UnityControllerBridge/
    ControllerBridgeRegistry.cs          [InitializeOnLoad]
    ControllerBridgeDirtyTracker.cs      [InitializeOnLoad]
    ControllerAssetPostprocessor.cs      AssetPostprocessor
    ControllerConversionQueue.cs         Background processor
    ControllerBridgeConfig.cs            ScriptableObject config
    UnityControllerConverter.cs          Actual conversion logic
    UnityControllerBridgeEditor.cs       Custom inspector (bridge asset)
    AnimationStateMachineAuthoringEditor.cs  Custom inspector (authoring)
    ControllerBridgeManagerWindow.cs     Editor window
    ControllerBridgeMenuItems.cs         Menu commands

Tests/
  Editor/
    UnityControllerBridgeTests.cs

Docs/
  UnityControllerBridge_Plan.md (this file)
  UnityControllerBridge_Usage.md (to be created)
```

---

## Testing Strategy

### Unit Tests
- Parameter conversion (Float, Bool, Int, Trigger)
- State conversion (single clip, 1D blend tree)
- Transition conversion (conditions, exit time)
- Hash calculation consistency
- Registry operations (add, remove, lookup)

### Integration Tests
- Convert StarterAssetsThirdPerson.controller
- Verify generated StateMachineAsset structure
- Verify runtime behavior matches Unity controller

### Performance Tests
- 100 entities with same controller → verify one blob created
- Modify controller → verify single reconversion
- Measure conversion time for large controllers

### Edge Cases
- Empty controllers
- Controllers with no states
- Self-transitions
- Multiple transitions from same state
- Unused parameters
- Missing motion references

---

## Success Criteria

✅ Successfully imports StarterAssetsThirdPerson.controller
✅ Converts parameters (Float, Bool, Int)
✅ Converts single clip states
✅ Converts 1D blend trees
✅ Converts transitions with conditions and exit times
✅ Creates valid DMotion StateMachineAsset
✅ Opens in DMotion visual editor without errors
✅ 100 entities with same controller = 1 conversion
✅ Controller modification triggers automatic reconversion
✅ Play mode blocks when controllers dirty (with auto-conversion)
✅ Subscene entities properly resolve bridges
✅ Reference counting accurate
✅ Debouncing prevents thrashing

---

## Future Enhancements

- Support for 2D blend trees (when DMotion adds support)
- Multiple layer conversion (when DMotion adds support)
- Sub-state machine flattening
- Avatar mask support
- Batch conversion tool (convert multiple controllers at once)
- Import presets (save/load conversion settings)
- Diff viewer (show changes between Unity controller and DMotion asset)

---

## Notes

- Architecture inspired by FogOfWarSystem's Island Baking System
- Leverages Latios SmartBaker for automatic blob deduplication
- All conversions happen at edit-time, not runtime
- Generated assets are read-only (regenerated on controller changes)
- Bridge assets should be committed to version control
- Generated StateMachineAssets should be committed (like imported assets)
