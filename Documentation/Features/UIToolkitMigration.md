# UI Toolkit Migration - Editor Audit

## Status: ✅ COMPLETE
## Priority: High
## Last Audit: January 2026

---

## Problem Statement

The editor uses a hybrid IMGUI/UIToolkit approach that causes event propagation issues:
- **Zoom captured by parent ScrollView** - Wheel events intercepted before reaching blend space
- **Pan coordinate mismatches** - IMGUI and UIToolkit have different coordinate conventions
- **Focus/capture conflicts** - IMGUIContainer doesn't properly own input events

### Root Cause
`BlendSpaceVisualEditorBase` and derivatives are IMGUI classes wrapped in `IMGUIContainer`, embedded within UIToolkit `ScrollView`. This hybrid approach creates an event routing barrier where UIToolkit captures events before IMGUI can process them.

---

## Full Editor Audit

### IMGUIContainer Usage (5 locations)

| File | Line | Purpose | Action |
|------|------|---------|--------|
| `AnimationPreviewWindow.cs` | 43, 401 | 3D preview with GL/Handles | **Keep** - requires GL rendering |
| `TransitionInspectorBuilder.cs` | 643-644 | Blend curve preview | **Migrate** - can use Painter2D |
| `BlendContentBuilderBase.cs` | 153 | Comment only (already migrated) | ✅ Done |
| `StateMachineInspectorView.cs` | 31 | Wraps Unity Editor inspector | **Keep** - Unity Editor integration |

### IMGUI Drawing (EditorGUI/Handles/GUI)

#### Files Migrated to UIToolkit ✅

| File | Original IMGUI Usage | Current Status |
|------|---------------------|----------------|
| `TransitionInspectorBuilder.cs` | Curve preview drawing | ✅ Uses `CurvePreviewElement` (Painter2D) |
| `PreviewRenderer.cs` | EditorGUI.DrawRect, GUI.Label | ✅ Uses UIToolkit labels |

#### Files Using IMGUI Drawing - LEGACY (Kept for Inspectors)

| File | Status | Notes |
|------|--------|-------|
| `BlendSpaceVisualEditorBase.cs` | LEGACY | Replaced by `BlendSpaceVisualElement.cs` |
| `BlendSpace2DVisualEditor.cs` | LEGACY | Replaced by `BlendSpace2DVisualElement.cs` |
| `BlendSpace1DVisualEditor.cs` | LEGACY | Replaced by `BlendSpace1DVisualElement.cs` |

#### Files Using IMGUI Drawing - KEEP AS IMGUI

| File | Reason |
|------|--------|
| `BlendCurveEditorWindow.cs` | Standalone EditorWindow with complex curve editing - works fine as IMGUI |
| `Directional2DBlendStateInspector.cs` | Standard Unity Inspector with PropertyFields |
| `AnimationStateInspector.cs` | Standard Unity Inspector with PropertyFields |
| `BlendSpaceEditorWindow.cs` | Simple IMGUI window, wraps new UIToolkit element |
| `ParametersInspector.cs` | Toolbar/button IMGUI - low impact |
| `DependencyInspector.cs` | EditorGUI.Popup in ListView - acceptable |
| `SubStateMachineInspector.cs` | Standard Unity Inspector |
| `AnyStateTransitionsInspector.cs` | Standard Unity Inspector |
| `StateMachineEditorUtils.cs` | GUI.color for visual indicators - acceptable |

#### Property Drawers - KEEP AS IMGUI (Unity Standard)

| File | Notes |
|------|-------|
| `AnimationParameterPropertyDrawer.cs` | Unity PropertyDrawer - must use IMGUI |
| `TransitionConditionPropertyDrawer.cs` | Unity PropertyDrawer - must use IMGUI |
| `SerializableTypePropertyDrawer.cs` | Unity PropertyDrawer - must use IMGUI |
| `AnimationClipEventPropertyDrawer.cs` | Unity PropertyDrawer - must use IMGUI |
| `AnimationEventsPropertyDrawer.cs` | Complex event timeline drawer - acceptable |
| `ObjectReferencePopupSelector.cs` | EditorGUI.Popup helper - acceptable |
| `TypePopupSelector.cs` | EditorGUI.DropdownButton helper - acceptable |

#### Popup Windows - KEEP AS IMGUI (Simpler)

| File | Notes |
|------|-------|
| `SelectSerializableTypePopup.cs` | Popup window |
| `ParameterDependencyWindow.cs` | Analysis tool window |
| `SubStateMachineCreationPopup.cs` | Creation dialog |

---

## USS Style Audit

### Existing USS Files (7 files)

| File | Status | Notes |
|------|--------|-------|
| `BlendSpaceVisualElement.uss` | ✅ Complete | Blend space element styles |
| `AnimationPreviewWindow.uss` | ✅ Complete | Comprehensive styles |
| `AnimationStateMachineEditorWindow.uss` | ✅ Complete | Graph view styles |
| `StateNodeView.uss` | ✅ Complete | Node styles |
| `TransitionInspector.uss` | ✅ Complete | Transition inspector styles (136 lines) |
| `BlendContent.uss` | ✅ Complete | Blend content builder styles (108 lines) |
| `StateInspector.uss` | ✅ Complete | State inspector styles |

### Files with Inline Styles - Status

#### Migrated to USS ✅

| File | USS File | Status |
|------|----------|--------|
| `TransitionInspectorBuilder.cs` | `TransitionInspector.uss` | ✅ Complete |
| `BlendContentBuilderBase.cs` | `BlendContent.uss` | ✅ Complete |
| `LinearBlendContentBuilder.cs` | `BlendContent.uss` | ✅ Complete |
| `Directional2DBlendContentBuilder.cs` | `BlendContent.uss` | ✅ Complete |

#### Dynamic Styles (Acceptable - Stay Inline)

| File | Inline Style Count | Notes |
|------|-------------------|-------|
| `BlendSpace1DVisualElement.cs` | 5+ | Dynamic positioning (runtime calculated) |
| `BlendSpace2DVisualElement.cs` | 5+ | Dynamic positioning (runtime calculated) |
| `AnimationPreviewWindow.cs` | 30+ | Layout computed at runtime |
| `TransitionTimeline.cs` | 20+ | Timeline positions calculated |
| `TimelineScrubber.cs` | 10+ | Scrubber positions calculated |

#### Low Priority (Functional, No Action Needed)

| File | Notes |
|------|-------|
| `StateInspectorBuilder.cs` | Uses StateInspector.uss + minor positioning |
| `PreviewUIFactory.cs` | Factory helper |
| `DockablePanelSection.cs` | Panel layout |

### Inline Styles That SHOULD Stay Inline

Dynamic styles that depend on runtime values should remain inline:
- `style.left = screenX` (calculated positions)
- `style.display = condition ? DisplayStyle.Flex : DisplayStyle.None` (visibility toggles)
- `style.translate = new Translate(...)` (transforms based on data)

---

## Migration Plan

### Phase 1: Convert Blend Space Editors ✅ COMPLETE

```
BEFORE (IMGUI):
BlendSpaceVisualEditorBase (class)
├─ BlendSpace2DVisualEditor : BlendSpaceVisualEditorBase
└─ BlendSpace1DVisualEditor : BlendSpaceVisualEditorBase

AFTER (UIToolkit):
BlendSpaceVisualElement : VisualElement (base class)
├─ BlendSpace2DVisualElement : BlendSpaceVisualElement
└─ BlendSpace1DVisualElement : BlendSpaceVisualElement
```

### Phase 2: Update Builder Wrappers ✅ COMPLETE

- `BlendContentBuilderBase` - Using direct `BlendSpaceVisualElement`
- `TransitionInspectorBuilder` - Using direct `BlendSpaceVisualElement`

### Phase 3: Cleanup & Polish ✅ COMPLETE

| Task | Priority | Status |
|------|----------|--------|
| Keep legacy IMGUI blend space files (used by inspectors) | High | ✅ Documented |
| Migrate curve preview to Painter2D | Medium | ✅ Complete |
| Add curve preview styles to USS | Medium | ✅ Complete |
| Create `TransitionInspector.uss` | Medium | ✅ Complete |
| Create `BlendContent.uss` | Low | ✅ Complete |
| Clean up redundant inline styles | Low | ✅ Complete |

### Phase 4: Optional Future Work

| Task | Priority | Notes |
|------|----------|-------|
| Migrate `BlendCurveEditorWindow` | Low | Works fine as IMGUI |
| Consider `TimelineScrubber.uss` | Low | Currently functional |

---

## Components Status Summary

| Component | Current | Target | Status |
|-----------|---------|--------|--------|
| `BlendSpaceVisualElement` | UIToolkit | UIToolkit | ✅ Complete |
| `BlendSpace2DVisualElement` | UIToolkit | UIToolkit | ✅ Complete |
| `BlendSpace1DVisualElement` | UIToolkit | UIToolkit | ✅ Complete |
| `BlendSpaceVisualElement.uss` | USS | USS | ✅ Complete |
| `TransitionInspector.uss` | USS | USS | ✅ Complete |
| `BlendContent.uss` | USS | USS | ✅ Complete |
| `BlendContentBuilderBase` | UIToolkit | UIToolkit | ✅ Complete |
| `TransitionInspectorBuilder` (blend) | UIToolkit | UIToolkit | ✅ Complete |
| `TransitionInspectorBuilder` (curve) | UIToolkit | UIToolkit | ✅ Complete |
| `CurvePreviewElement` | UIToolkit | UIToolkit | ✅ Complete |
| Legacy blend space files | IMGUI | Keep (inspectors) | ✅ Documented |
| `BlendCurveEditorWindow` | IMGUI | Keep | ✅ No change |
| `AnimationPreviewWindow` (3D) | IMGUI | Keep | ✅ No change |
| `StateMachineInspectorView` | IMGUI | Keep | ✅ No change |
| Property Drawers | IMGUI | Keep | ✅ No change |

---

## Legacy IMGUI Files (Keep for Inspectors)

The following IMGUI files are still required for Unity custom inspectors:

```
Editor/EditorWindows/
├── BlendSpaceVisualEditorBase.cs   ← KEEP (used by AnimationStateInspector, Directional2DBlendStateInspector)
├── BlendSpace2DVisualEditor.cs     ← KEEP (used by Directional2DBlendStateInspector)
├── BlendSpace1DVisualEditor.cs     ← KEEP (used by LinearBlendStateInspector)
└── BlendSpaceEditorWindow.cs       ← KEEP (standalone window for legacy editors)
```

**Note:** These can be removed if/when the inspectors are migrated to UIToolkit using `CreateInspectorGUI()`.

---

## Files Created (New UIToolkit)

```
Editor/EditorWindows/
├── BlendSpaceVisualElement.cs      ✅ Complete (~900 lines)
├── BlendSpaceVisualElement.uss     ✅ Complete (~180 lines)
├── BlendSpace2DVisualElement.cs    ✅ Complete (~400 lines)
└── BlendSpace1DVisualElement.cs    ✅ Complete (~570 lines)

Editor/EditorWindows/Preview/
├── CurvePreviewElement.cs          ✅ Complete (~180 lines)
├── TransitionInspector.uss         ✅ Complete (136 lines)
└── StateContent/
    └── BlendContent.uss            ✅ Complete (108 lines)
```

---

## USS Files Created ✅

### TransitionInspector.uss (136 lines)

Located at: `Editor/EditorWindows/Preview/TransitionInspector.uss`

Key classes:
- `.section-header`, `.header-type`, `.header-name`
- `.state-link`, `.state-link--from`, `.state-link--to`
- `.property-row`, `.property-label`
- `.curve-section`, `.curve-header-row`
- `.transition-blend-space-1d`, `.transition-blend-space-2d`

### BlendContent.uss (108 lines)

Located at: `Editor/EditorWindows/Preview/StateContent/BlendContent.uss`

Key classes:
- `.blend-space-help`
- `.clip-edit-container`, `.clip-edit-hint`, `.clip-edit-fields`
- `.clip-edit-row`, `.clip-edit-row__label`, `.clip-edit-row__field`
- `.blend-content__property-label`, `.blend-content__value-container`
- `.blend-space-1d-preview`, `.blend-space-2d-preview`

---

## UIToolkit Drawing Pattern Reference

### Before (IMGUI)
```csharp
internal class BlendSpaceVisualEditorBase
{
    public void Draw(Rect rect, SerializedObject serializedObject)
    {
        EditorGUI.DrawRect(rect, BackgroundColor);
        Handles.DrawLine(start, end);
        GUI.Label(rect, text, style);
    }
}
```

### After (UIToolkit)
```csharp
[UxmlElement]
internal partial class BlendSpaceVisualElement : VisualElement
{
    public BlendSpaceVisualElement()
    {
        // Load USS
        var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>("path/to/styles.uss");
        if (uss != null) styleSheets.Add(uss);
        
        // Use CSS classes instead of inline styles
        AddToClassList("blend-space");
        
        generateVisualContent += OnGenerateVisualContent;
        RegisterCallback<WheelEvent>(OnWheel);
    }
    
    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        var painter = ctx.painter2D;
        
        // Background
        painter.fillColor = BackgroundColor;
        painter.BeginPath();
        painter.MoveTo(new Vector2(0, 0));
        // ...
        painter.Fill();
        
        // Lines
        painter.strokeColor = GridColor;
        painter.BeginPath();
        painter.MoveTo(start);
        painter.LineTo(end);
        painter.Stroke();
    }
}
```

---

## Summary

**UIToolkit migration is complete.** The editor preview system now uses native UIToolkit with Painter2D for custom drawing.

### Key Patterns Used

- `TransitionTimeline.cs` is the reference implementation for UIToolkit drawing
- `CurvePreviewElement.cs` demonstrates Painter2D curve rendering
- `BlendSpaceVisualElement.cs` shows complex interactive visualization
- Use `MarkDirtyRepaint()` instead of relying on IMGUI's automatic repainting
- `Painter2D` replaces `EditorGUI.DrawRect`, `Handles.DrawLine`, etc.

### Intentionally Kept as IMGUI

- **Property Drawers** - Unity limitation, must use IMGUI
- **3D preview** - Requires GL/Handles for rendering (IMGUIContainer)
- **Legacy inspectors** - Standard Unity inspectors work fine with IMGUI

### Style Guidelines

- Static styles go in USS files
- Dynamic positioning (runtime calculated) stays inline
- Use CSS classes for theming consistency

---

## Related Documents

- **[AnimationPreviewWindow.md](./AnimationPreviewWindow.md)** - Preview window implementation using UIToolkit elements
- **[EcsPreviewAndRigBinding.md](./EcsPreviewAndRigBinding.md)** - ECS preview world feature plan
- **[TransitionBlendCurve.md](./TransitionBlendCurve.md)** - Transition curve runtime support (uses CurvePreviewElement)
