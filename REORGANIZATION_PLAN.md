# DMotion Directory Reorganization Plan

## Summary

This plan reorganizes DMotion files into semantic groups while maintaining:
- Runtime/Editor assembly separation (Unity requirement)
- .meta file pairs (critical for Unity)
- Assembly definition files at assembly roots

---

## Current Structure Analysis

### Runtime (93 files)
- `Components/` - 18 files (mixed: ECS components + blobs)
- `Systems/` - 23 files (ECS systems + jobs + utilities)
- `Authoring/` - 36 files (assets, conversion, parameters)
- `Utils/` - 15 files (general utilities)
- `Debug/` - 1 file

### Editor (101 files)
- `EditorWindows/` - 71 files (massive, mixed concerns)
- `CustomEditors/` - 6 files
- `PropertyDrawers/` - 14 files (mixed: drawers + caches/pools)
- `EditorPreview/` - 4 files
- `Parameters/` - 2 files
- `UIElements/` - 2 files
- `Utilities/` - 6 files
- `Views/` - 1 file
- `Entities.Exposed/` - 1 file

---

## Proposed Runtime Structure

```
Runtime/
├── DMotion.Runtime.asmdef          (KEEP - assembly root)
├── DMotion.Runtime.cs              (KEEP)
├── AssemblyInfo.cs                 (KEEP)
│
├── Components/
│   ├── Core/                       # ECS component data
│   │   ├── AnimationCurrentState.cs    (from AnimationState.cs - split)
│   │   ├── AnimationStateTransition.cs (from AnimationState.cs - split)
│   │   ├── AnimationParameters.cs
│   │   ├── AnimationEvent.cs
│   │   ├── AnimatorEntity.cs
│   │   ├── RootMotion.cs
│   │   └── AnimationTimelineControl.cs
│   │
│   ├── StateMachine/               # State machine runtime data
│   │   ├── AnimationStateMachine.cs
│   │   ├── ClipSampler.cs
│   │   ├── SingleClipState.cs
│   │   ├── SingleClipRef.cs
│   │   ├── LinearBlendStateMachineState.cs
│   │   ├── Directional2DBlendStateMachineState.cs
│   │   ├── OneShotState.cs
│   │   └── PlaySingleClipRequest.cs
│   │
│   ├── Blobs/                      # Blob asset structs
│   │   ├── StateMachineBlob.cs
│   │   ├── AnimationStateBlob.cs
│   │   └── AnimationTransition.cs
│   │
│   └── Rendering/                  # Render request components
│       └── AnimationRenderRequests.cs
│
├── Systems/
│   ├── Core/                       # Main ECS systems
│   │   ├── AnimationStateMachineSystem.cs
│   │   ├── BlendAnimationStatesSystem.cs
│   │   ├── UpdateAnimationStatesSystem.cs
│   │   ├── ClipSamplingSystem.cs
│   │   ├── AnimationEventsSystem.cs
│   │   └── AnimationTimelineControllerSystem.cs
│   │
│   ├── StateMachine/               # State machine systems/jobs
│   │   ├── UpdateStateMachineJob.cs
│   │   ├── PlayOneShotSystem.cs
│   │   └── PlaySingleClipSystem.cs
│   │
│   ├── Sampling/                   # Bone/root sampling jobs
│   │   ├── SampleOptimizedBonesJob.cs
│   │   ├── SampleNonOptimizedBones.cs
│   │   ├── SampleRootDeltasJob.cs
│   │   └── NormalizedSamplersWeights.cs
│   │
│   ├── StateTypes/                 # Per-state-type jobs
│   │   ├── SingleClipStateJobs.cs
│   │   ├── LinearBlendStateMachineJobs.cs
│   │   ├── Directional2DBlendStateMachineJobs.cs
│   │   └── Directional2DBlendStateUtils.cs
│   │
│   ├── RootMotion/                 # Root motion systems
│   │   ├── ApplyRootMotionToEntityJob.cs
│   │   └── TransferRootMotionJob.cs
│   │
│   └── Rendering/                  # Render request systems
│       ├── ApplyStateRenderRequestSystem.cs
│       └── ApplyTransitionRenderRequestSystem.cs
│
├── Authoring/
│   ├── Assets/                     # ScriptableObject assets
│   │   ├── StateMachine/
│   │   │   ├── StateMachineAsset.cs
│   │   │   ├── SubStateMachineStateAsset.cs
│   │   │   └── AnimationTransitionGroup.cs
│   │   │
│   │   ├── States/
│   │   │   ├── AnimationStateAsset.cs
│   │   │   ├── SingleClipStateAsset.cs
│   │   │   ├── LinearBlendStateAsset.cs
│   │   │   └── Directional2DBlendStateAsset.cs
│   │   │
│   │   ├── Parameters/
│   │   │   ├── AnimationParameterAsset.cs
│   │   │   ├── BoolParameterAsset.cs
│   │   │   ├── FloatParameterAsset.cs
│   │   │   ├── IntParameterAsset.cs
│   │   │   └── EnumParameterAsset.cs
│   │   │
│   │   ├── Clips/
│   │   │   ├── AnimationClipAsset.cs
│   │   │   └── AnimationEventName.cs
│   │   │
│   │   └── Transitions/
│   │       ├── TransitionCondition.cs
│   │       └── RigBinding.cs
│   │
│   ├── MonoBehaviours/             # Authoring components
│   │   ├── AnimationStateMachineAuthoring.cs
│   │   └── PlayClipAuthoring.cs
│   │
│   ├── Baking/                     # Blob builders & bakers
│   │   ├── StateMachineBlobConverter.cs
│   │   ├── StateFlattener.cs
│   │   ├── AnimationStateMachineAssetBuilder.cs
│   │   ├── ClipEventsBlobConverter.cs
│   │   ├── AnimationStateMachineSmartBlobberSystem.cs
│   │   ├── ClipEventsSmartBlobberSystem.cs
│   │   ├── ClipEventsAuthoringUtils.cs
│   │   ├── AnimationStateMachineConversionUtils.cs
│   │   ├── AnimationStateMachineConversionExtensions.cs
│   │   ├── EntityCommands.cs
│   │   └── SingleClipRefsConverter.cs
│   │
│   ├── Parameters/                 # Parameter system
│   │   ├── ParameterLink.cs
│   │   └── ParameterDependency.cs
│   │
│   ├── Bootstrap/                  # Bootstrap systems
│   │   ├── DMotionBootstrap.cs
│   │   └── DMotionBakingBootstrap.cs
│   │
│   └── Types/                      # Utility types for authoring
│       ├── SerializableType.cs
│       └── RootMotionMode.cs
│
├── Utils/
│   ├── Animation/                  # Animation-specific utilities
│   │   ├── AnimationTimeUtils.cs
│   │   ├── AnimationBufferContext.cs
│   │   ├── AnimationEventUtils.cs
│   │   ├── ClipSamplerUtils.cs
│   │   └── ClipSamplingUtils.cs
│   │
│   ├── StateTypes/                 # State-type utilities
│   │   ├── SingleClipStateUtils.cs
│   │   ├── LinearBlendStateUtils.cs
│   │   ├── Directional2DBlendUtils.cs
│   │   └── StateMachineParameterUtils.cs
│   │
│   ├── Core/                       # General utilities
│   │   ├── mathex.cs
│   │   ├── CollectionUtils.cs
│   │   ├── IEnumerableUtils.cs
│   │   ├── CurveUtils.cs
│   │   ├── RectUtils.cs
│   │   └── ConversionUtils.cs
│   │
│   └── Baking/                     # Baking utilities
│       └── IBakerExtensions.cs
│
└── Debug/
    └── AnimationDebugSystem.cs     (KEEP)
```

---

## Proposed Editor Structure

```
Editor/
├── DMotion.Editor.asmdef           (KEEP - assembly root)
├── ExternalUtilityTools.cs         (KEEP at root)
│
├── StateMachineEditor/             # Main state machine editor
│   ├── Windows/
│   │   ├── AnimationStateMachineEditorWindow.cs
│   │   └── AnimationStateMachineEditorView.cs
│   │
│   ├── Graph/                      # GraphView components
│   │   ├── Nodes/
│   │   │   ├── BaseNodeView.cs
│   │   │   ├── StateNodeView.cs
│   │   │   ├── AnyStateNodeView.cs
│   │   │   └── ExitNodeView.cs
│   │   │
│   │   ├── Edges/
│   │   │   ├── TransitionEdge.cs
│   │   │   ├── TransitionEdgeControl.cs
│   │   │   ├── ExitTransitionEdge.cs
│   │   │   └── ExitTransitionEdgeControl.cs
│   │   │
│   │   └── Manipulators/
│   │       ├── TransitionDragManipulator.cs
│   │       └── TransitionCutManipulator.cs
│   │
│   ├── Inspectors/                 # Side panel inspectors
│   │   ├── StateMachineInspectorView.cs
│   │   ├── InspectorController.cs
│   │   ├── PanelControllers.cs
│   │   ├── AnimationStateInspector.cs
│   │   ├── StateInspectorUIToolkit.cs
│   │   ├── TransitionGroupInspector.cs
│   │   ├── AnyStateTransitionsInspector.cs
│   │   ├── SubStateMachineInspector.cs
│   │   ├── Directional2DBlendStateInspector.cs
│   │   ├── ParametersInspector.cs
│   │   └── DependencyInspector.cs
│   │
│   ├── Navigation/                 # Navigation components
│   │   ├── BreadcrumbBar.cs
│   │   └── StateSearchWindowProvider.cs
│   │
│   ├── Popups/
│   │   └── SubStateMachineCreationPopup.cs
│   │
│   └── Events/
│       └── StateMachineEditorEvents.cs
│
├── BlendSpaceEditor/               # Blend space visualization
│   ├── BlendSpaceEditorWindow.cs
│   ├── BlendSpaceVisualEditorBase.cs
│   ├── BlendSpaceVisualElement.cs
│   ├── BlendSpace1DVisualEditor.cs
│   ├── BlendSpace1DVisualElement.cs
│   ├── BlendSpace2DVisualEditor.cs
│   └── BlendSpace2DVisualElement.cs
│
├── Preview/                        # Animation preview system
│   ├── Windows/
│   │   └── AnimationPreviewWindow.cs
│   │
│   ├── Backends/
│   │   ├── IPreviewBackend.cs
│   │   ├── PreviewBackendBase.cs
│   │   ├── EcsPreviewBackend.cs
│   │   └── PlayableGraphBackend.cs
│   │
│   ├── EcsWorld/                   # ECS preview world management
│   │   ├── EcsPreviewWorldService.cs
│   │   ├── EcsPreviewSceneManager.cs
│   │   ├── EcsHybridRenderer.cs
│   │   └── EcsEntityBrowser.cs
│   │
│   ├── Rendering/
│   │   └── PreviewRenderer.cs
│   │
│   ├── Timeline/
│   │   ├── TimelineBase.cs
│   │   ├── TimelineScrubber.cs
│   │   ├── TimelineControlHelper.cs
│   │   └── TransitionTimeline.cs
│   │
│   ├── StateContent/               # State-specific content builders
│   │   ├── IStateContentBuilder.cs
│   │   ├── BlendContentBuilderBase.cs
│   │   ├── SingleClipContentBuilder.cs
│   │   ├── LinearBlendContentBuilder.cs
│   │   └── Directional2DBlendContentBuilder.cs
│   │
│   ├── Inspectors/
│   │   ├── StateInspectorBuilder.cs
│   │   └── TransitionInspectorBuilder.cs
│   │
│   ├── State/                      # Preview state management
│   │   ├── PreviewSession.cs
│   │   ├── PreviewState.cs
│   │   ├── PreviewTarget.cs
│   │   ├── PreviewSettings.cs
│   │   └── PreviewTimeStateExtensions.cs
│   │
│   ├── UI/
│   │   ├── PreviewUIFactory.cs
│   │   ├── IUIElementFactory.cs
│   │   ├── PreviewEditorColors.cs
│   │   └── PreviewEditorConstants.cs
│   │
│   ├── Events/
│   │   └── AnimationPreviewEvents.cs
│   │
│   └── Curves/
│       ├── BlendCurveEditorWindow.cs
│       └── CurvePreviewElement.cs
│
├── LegacyPreview/                  # PlayableGraph-based preview (deprecated?)
│   ├── PlayableGraphPreview.cs
│   ├── SingleClipPreview.cs
│   ├── BlendedClipPreview.cs
│   └── TransitionPreview.cs
│
├── CustomEditors/                  # Unity custom editors
│   ├── StateMachineAssetEditor.cs
│   ├── StateMachineSubAssetEditor.cs
│   ├── SubStateMachineStateAssetEditor.cs
│   ├── AnimationClipAssetEditor.cs
│   ├── ArmatureContextMenu.cs
│   └── RectElement.cs
│
├── PropertyDrawers/
│   ├── Drawers/                    # Actual property drawers
│   │   ├── AnimationParameterPropertyDrawer.cs
│   │   ├── AnimationEventsPropertyDrawer.cs
│   │   ├── AnimationClipEventPropertyDrawer.cs
│   │   ├── TransitionConditionPropertyDrawer.cs
│   │   ├── SerializableTypePropertyDrawer.cs
│   │   └── BlendCurveDrawer.cs
│   │
│   ├── Selectors/                  # Popup selectors
│   │   ├── TypePopupSelector.cs
│   │   ├── SelectSerializableTypePopup.cs
│   │   └── ObjectReferencePopupSelector.cs
│   │
│   └── Caches/                     # Drawing caches & pools
│       ├── GUIContentCache.cs
│       ├── IconCache.cs
│       ├── StringBuilderCache.cs
│       ├── ListPool.cs
│       └── RectPool.cs
│
├── Parameters/                     # Parameter analysis tools
│   ├── ParameterDependencyAnalyzer.cs
│   └── ParameterDependencyWindow.cs
│
├── UIElements/                     # Reusable UI elements
│   ├── DockableFoldout.cs
│   ├── SubAssetPopupField.cs
│   ├── SplitView.cs                (from Views/)
│   └── DockablePanelSection.cs     (from EditorWindows/)
│
├── Utilities/
│   ├── AnimationStateUtils.cs
│   ├── StateMachineEditorUtils.cs
│   ├── EditorGUIUtils.cs
│   ├── EditorLayoutUtils.cs
│   ├── EditorSerializationUtils.cs
│   └── TransitionTimingCalculator.cs
│
└── Integrations/
    └── EntitySelectionProxyUtils.cs (from Entities.Exposed/)
```

---

## Detailed Migration Plan

### Runtime Migrations

| Current Location | New Location | Rationale |
|-----------------|--------------|-----------|
| **Components/** | | |
| `AnimationState.cs` | `Components/Core/` | Core ECS component |
| `AnimationParameters.cs` | `Components/Core/` | Core ECS component |
| `AnimationEvent.cs` | `Components/Core/` | Core ECS component |
| `AnimatorEntity.cs` | `Components/Core/` | Core ECS component |
| `RootMotion.cs` | `Components/Core/` | Core ECS component |
| `AnimationTimelineControl.cs` | `Components/Core/` | Core ECS component |
| `AnimationStateMachine.cs` | `Components/StateMachine/` | State machine component |
| `ClipSampler.cs` | `Components/StateMachine/` | Sampling component |
| `SingleClipState.cs` | `Components/StateMachine/` | State type component |
| `SingleClipRef.cs` | `Components/StateMachine/` | State type component |
| `LinearBlendStateMachineState.cs` | `Components/StateMachine/` | State type component |
| `Directional2DBlendStateMachineState.cs` | `Components/StateMachine/` | State type component |
| `OneShotState.cs` | `Components/StateMachine/` | State type component |
| `PlaySingleClipRequest.cs` | `Components/StateMachine/` | Request component |
| `StateMachineBlob.cs` | `Components/Blobs/` | Blob asset struct |
| `AnimationStateBlob.cs` | `Components/Blobs/` | Blob asset struct |
| `AnimationTransition.cs` | `Components/Blobs/` | Blob asset struct |
| `AnimationRenderRequests.cs` | `Components/Rendering/` | Render requests |
| **Systems/** | | |
| `AnimationStateMachineSystem.cs` | `Systems/Core/` | Core system |
| `BlendAnimationStatesSystem.cs` | `Systems/Core/` | Core system |
| `UpdateAnimationStatesSystem.cs` | `Systems/Core/` | Core system |
| `ClipSamplingSystem.cs` | `Systems/Core/` | Core system |
| `AnimationEventsSystem.cs` | `Systems/Core/` | Core system |
| `AnimationTimelineControllerSystem.cs` | `Systems/Core/` | Core system |
| `UpdateStateMachineJob.cs` | `Systems/StateMachine/` | SM job |
| `PlayOneShotSystem.cs` | `Systems/StateMachine/` | SM system |
| `PlaySingleClipSystem.cs` | `Systems/StateMachine/` | SM system |
| `SampleOptimizedBonesJob.cs` | `Systems/Sampling/` | Sampling job |
| `SampleNonOptimizedBones.cs` | `Systems/Sampling/` | Sampling job |
| `SampleRootDeltasJob.cs` | `Systems/Sampling/` | Sampling job |
| `NormalizedSamplersWeights.cs` | `Systems/Sampling/` | Sampling utility |
| `SingleClipStateJobs.cs` | `Systems/StateTypes/` | State type job |
| `LinearBlendStateMachineJobs.cs` | `Systems/StateTypes/` | State type job |
| `Directional2DBlendStateMachineJobs.cs` | `Systems/StateTypes/` | State type job |
| `Directional2DBlendStateUtils.cs` | `Systems/StateTypes/` | State type utility |
| `ApplyRootMotionToEntityJob.cs` | `Systems/RootMotion/` | Root motion job |
| `TransferRootMotionJob.cs` | `Systems/RootMotion/` | Root motion job |
| `ApplyStateRenderRequestSystem.cs` | `Systems/Rendering/` | Render request system |
| `ApplyTransitionRenderRequestSystem.cs` | `Systems/Rendering/` | Render request system |
| **Authoring/** | | |
| `AnimationStateMachine/StateMachineAsset.cs` | `Authoring/Assets/StateMachine/` | SM asset |
| `AnimationStateMachine/SubStateMachineStateAsset.cs` | `Authoring/Assets/StateMachine/` | SM asset |
| `AnimationStateMachine/AnimationTransitionGroup.cs` | `Authoring/Assets/StateMachine/` | SM asset |
| `AnimationStateMachine/AnimationStateAsset.cs` | `Authoring/Assets/States/` | State asset |
| `AnimationStateMachine/SingleClipStateAsset.cs` | `Authoring/Assets/States/` | State asset |
| `AnimationStateMachine/LinearBlendStateAsset.cs` | `Authoring/Assets/States/` | State asset |
| `AnimationStateMachine/Directional2DBlendStateAsset.cs` | `Authoring/Assets/States/` | State asset |
| `AnimationStateMachine/AnimationParameterAsset.cs` | `Authoring/Assets/Parameters/` | Parameter asset |
| `AnimationStateMachine/BoolParameterAsset.cs` | `Authoring/Assets/Parameters/` | Parameter asset |
| `AnimationStateMachine/FloatParameterAsset.cs` | `Authoring/Assets/Parameters/` | Parameter asset |
| `AnimationStateMachine/IntParameterAsset.cs` | `Authoring/Assets/Parameters/` | Parameter asset |
| `AnimationStateMachine/EnumParameterAsset.cs` | `Authoring/Assets/Parameters/` | Parameter asset |
| `AnimationStateMachine/AnimationClipAsset.cs` | `Authoring/Assets/Clips/` | Clip asset |
| `AnimationStateMachine/AnimationEventName.cs` | `Authoring/Assets/Clips/` | Clip asset |
| `AnimationStateMachine/TransitionCondition.cs` | `Authoring/Assets/Transitions/` | Transition asset |
| `AnimationStateMachine/RigBinding.cs` | `Authoring/Assets/Transitions/` | Binding asset |
| `AnimationStateMachine/SerializableType.cs` | `Authoring/Types/` | Utility type |
| `AnimationStateMachineAuthoring.cs` | `Authoring/MonoBehaviours/` | Authoring component |
| `PlayClipAuthoring.cs` | `Authoring/MonoBehaviours/` | Authoring component |
| `RootMotionMode.cs` | `Authoring/Types/` | Utility type |
| `Conversion/*` | `Authoring/Baking/` | Rename folder |
| `SingleClipRefsConverter.cs` | `Authoring/Baking/` | Baker |
| `DMotionBootstrap.cs` | `Authoring/Bootstrap/` | Bootstrap |
| `DMotionBakingBootstrap.cs` | `Authoring/Bootstrap/` | Bootstrap |
| `Parameters/ParameterLink.cs` | `Authoring/Parameters/` | Keep in place |
| `Parameters/ParameterDependency.cs` | `Authoring/Parameters/` | Keep in place |
| **Utils/** | | |
| `AnimationTimeUtils.cs` | `Utils/Animation/` | Animation utility |
| `AnimationBufferContext.cs` | `Utils/Animation/` | Animation utility |
| `AnimationEventUtils.cs` | `Utils/Animation/` | Animation utility |
| `ClipSamplerUtils.cs` | `Utils/Animation/` | Animation utility |
| `ClipSamplingUtils.cs` | `Utils/Animation/` | Animation utility |
| `SingleClipStateUtils.cs` | `Utils/StateTypes/` | State type utility |
| `LinearBlendStateUtils.cs` | `Utils/StateTypes/` | State type utility |
| `Directional2DBlendUtils.cs` | `Utils/StateTypes/` | State type utility |
| `StateMachineParameterUtils.cs` | `Utils/StateTypes/` | State type utility |
| `mathex.cs` | `Utils/Core/` | General utility |
| `CollectionUtils.cs` | `Utils/Core/` | General utility |
| `IEnumerableUtils.cs` | `Utils/Core/` | General utility |
| `CurveUtils.cs` | `Utils/Core/` | General utility |
| `RectUtils.cs` | `Utils/Core/` | General utility |
| `ConversionUtils.cs` | `Utils/Core/` | General utility |
| `Baking.Extensions/IBakerExtensions.cs` | `Utils/Baking/` | Baking utility |

### Editor Migrations

| Current Location | New Location | Rationale |
|-----------------|--------------|-----------|
| **EditorWindows/** | | |
| `AnimationStateMachineEditorWindow.cs` | `StateMachineEditor/Windows/` | Main editor window |
| `AnimationStateMachineEditorView.cs` | `StateMachineEditor/Windows/` | Main editor view |
| `BaseNodeView.cs` | `StateMachineEditor/Graph/Nodes/` | Graph node |
| `StateNodeView.cs` | `StateMachineEditor/Graph/Nodes/` | Graph node |
| `AnyStateNodeView.cs` | `StateMachineEditor/Graph/Nodes/` | Graph node |
| `ExitNodeView.cs` | `StateMachineEditor/Graph/Nodes/` | Graph node |
| `TransitionEdge.cs` | `StateMachineEditor/Graph/Edges/` | Graph edge |
| `TransitionEdgeControl.cs` | `StateMachineEditor/Graph/Edges/` | Graph edge |
| `ExitTransitionEdge.cs` | `StateMachineEditor/Graph/Edges/` | Graph edge |
| `ExitTransitionEdgeControl.cs` | `StateMachineEditor/Graph/Edges/` | Graph edge |
| `TransitionDragManipulator.cs` | `StateMachineEditor/Graph/Manipulators/` | Manipulator |
| `TransitionCutManipulator.cs` | `StateMachineEditor/Graph/Manipulators/` | Manipulator |
| `StateMachineInspectorView.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `InspectorController.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `PanelControllers.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `AnimationStateInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `StateInspectorUIToolkit.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `TransitionGroupInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `AnyStateTransitionsInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `SubStateMachineInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `Directional2DBlendStateInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `ParametersInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `DependencyInspector.cs` | `StateMachineEditor/Inspectors/` | Inspector |
| `BreadcrumbBar.cs` | `StateMachineEditor/Navigation/` | Navigation |
| `StateSearchWindowProvider.cs` | `StateMachineEditor/Navigation/` | Navigation |
| `SubStateMachineCreationPopup.cs` | `StateMachineEditor/Popups/` | Popup |
| `StateMachineEditorEvents.cs` | `StateMachineEditor/Events/` | Events |
| `DockablePanelSection.cs` | `UIElements/` | Reusable UI element |
| **EditorWindows/Preview/** | | |
| `AnimationPreviewWindow.cs` | `Preview/Windows/` | Preview window |
| `IPreviewBackend.cs` | `Preview/Backends/` | Backend interface |
| `PreviewBackendBase.cs` | `Preview/Backends/` | Backend base |
| `EcsPreviewBackend.cs` | `Preview/Backends/` | ECS backend |
| `PlayableGraphBackend.cs` | `Preview/Backends/` | Legacy backend |
| `EcsPreviewWorldService.cs` | `Preview/EcsWorld/` | ECS world service |
| `EcsPreviewSceneManager.cs` | `Preview/EcsWorld/` | ECS scene manager |
| `EcsHybridRenderer.cs` | `Preview/EcsWorld/` | ECS renderer |
| `EcsEntityBrowser.cs` | `Preview/EcsWorld/` | Entity browser |
| `PreviewRenderer.cs` | `Preview/Rendering/` | Rendering |
| `TimelineBase.cs` | `Preview/Timeline/` | Timeline |
| `TimelineScrubber.cs` | `Preview/Timeline/` | Timeline |
| `TimelineControlHelper.cs` | `Preview/Timeline/` | Timeline |
| `TransitionTimeline.cs` | `Preview/Timeline/` | Timeline |
| `StateContent/*.cs` | `Preview/StateContent/` | Keep structure |
| `StateInspectorBuilder.cs` | `Preview/Inspectors/` | Inspector builder |
| `TransitionInspectorBuilder.cs` | `Preview/Inspectors/` | Inspector builder |
| `PreviewSession.cs` | `Preview/State/` | State management |
| `PreviewState.cs` | `Preview/State/` | State management |
| `PreviewTarget.cs` | `Preview/State/` | State management |
| `PreviewSettings.cs` | `Preview/State/` | State management |
| `PreviewTimeStateExtensions.cs` | `Preview/State/` | State management |
| `PreviewUIFactory.cs` | `Preview/UI/` | UI factory |
| `IUIElementFactory.cs` | `Preview/UI/` | UI interface |
| `PreviewEditorColors.cs` | `Preview/UI/` | UI constants |
| `PreviewEditorConstants.cs` | `Preview/UI/` | UI constants |
| `AnimationPreviewEvents.cs` | `Preview/Events/` | Events |
| `BlendCurveEditorWindow.cs` | `Preview/Curves/` | Curve editor |
| `CurvePreviewElement.cs` | `Preview/Curves/` | Curve preview |
| **Blend Space Editor** | | |
| `BlendSpaceEditorWindow.cs` | `BlendSpaceEditor/` | Blend space |
| `BlendSpaceVisualEditorBase.cs` | `BlendSpaceEditor/` | Blend space |
| `BlendSpaceVisualElement.cs` | `BlendSpaceEditor/` | Blend space |
| `BlendSpace1DVisualEditor.cs` | `BlendSpaceEditor/` | Blend space |
| `BlendSpace1DVisualElement.cs` | `BlendSpaceEditor/` | Blend space |
| `BlendSpace2DVisualEditor.cs` | `BlendSpaceEditor/` | Blend space |
| `BlendSpace2DVisualElement.cs` | `BlendSpaceEditor/` | Blend space |
| **EditorPreview/** | | |
| `*.cs` | `LegacyPreview/` | Legacy PlayableGraph preview |
| **PropertyDrawers/** | | |
| `AnimationParameterPropertyDrawer.cs` | `PropertyDrawers/Drawers/` | Drawer |
| `AnimationEventsPropertyDrawer.cs` | `PropertyDrawers/Drawers/` | Drawer |
| `AnimationClipEventPropertyDrawer.cs` | `PropertyDrawers/Drawers/` | Drawer |
| `TransitionConditionPropertyDrawer.cs` | `PropertyDrawers/Drawers/` | Drawer |
| `SerializableTypePropertyDrawer.cs` | `PropertyDrawers/Drawers/` | Drawer |
| `BlendCurveDrawer.cs` | `PropertyDrawers/Drawers/` | Drawer |
| `TypePopupSelector.cs` | `PropertyDrawers/Selectors/` | Selector |
| `SelectSerializableTypePopup.cs` | `PropertyDrawers/Selectors/` | Selector |
| `ObjectReferencePopupSelector.cs` | `PropertyDrawers/Selectors/` | Selector |
| `GUIContentCache.cs` | `PropertyDrawers/Caches/` | Cache |
| `IconCache.cs` | `PropertyDrawers/Caches/` | Cache |
| `StringBuilderCache.cs` | `PropertyDrawers/Caches/` | Cache |
| `ListPool.cs` | `PropertyDrawers/Caches/` | Pool |
| `RectPool.cs` | `PropertyDrawers/Caches/` | Pool |
| **Other** | | |
| `Views/SplitView.cs` | `UIElements/SplitView.cs` | Reusable UI |
| `Entities.Exposed/EntitySelectionProxyUtils.cs` | `Integrations/` | Integration |

---

## Files to Keep in Place

| File | Location | Reason |
|------|----------|--------|
| `DMotion.Runtime.asmdef` | `Runtime/` | Assembly definition root |
| `DMotion.Editor.asmdef` | `Editor/` | Assembly definition root |
| `DMotion.Runtime.cs` | `Runtime/` | Assembly constants |
| `AssemblyInfo.cs` | `Runtime/` | Assembly attributes |
| `ExternalUtilityTools.cs` | `Editor/` | Root-level utility |
| `AnimationDebugSystem.cs` | `Runtime/Debug/` | Already well-placed |

---

## Benefits of Reorganization

1. **Discoverability**: Related files are grouped together
2. **Scalability**: Clear places to add new features
3. **Maintainability**: Easier to understand codebase structure
4. **Separation of Concerns**: Clear boundaries between subsystems
5. **Navigation**: IDE navigation becomes more intuitive

## Risks & Considerations

1. **Meta Files**: Must move together with .cs files
2. **Git History**: Prefer `git mv` to preserve history
3. **Namespaces**: May want to update namespaces to match folder structure
4. **Mechination**: Check if external project references specific paths
5. **UXML/USS**: Check for hardcoded paths in UI templates

---

## Implementation Order

1. **Runtime first** - Less complex, establishes patterns
2. **Test after each major move** - Ensure compilation
3. **Editor second** - More files, more complexity
4. **Update namespaces** - Optional, can be done later
5. **Update CLAUDE.md** - Document new structure

---

## Estimated Impact

- **Files to move**: ~180 total
- **New folders to create**: ~45
- **Compilation risk**: Low (if done carefully)
- **Time estimate**: 2-4 hours with testing
