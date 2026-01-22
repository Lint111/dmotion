# UI Toolkit Migration - Blend Space Editors

## Status: In Progress (Phase 1)
## Priority: High
## Estimated Phases: 3

---

## Problem Statement

The editor uses a hybrid IMGUI/UIToolkit approach that causes event propagation issues:
- **Zoom captured by parent ScrollView** - Wheel events intercepted before reaching blend space
- **Pan coordinate mismatches** - IMGUI and UIToolkit have different coordinate conventions
- **Focus/capture conflicts** - IMGUIContainer doesn't properly own input events

### Root Cause
`BlendSpaceVisualEditorBase` and derivatives are IMGUI classes wrapped in `IMGUIContainer`, embedded within UIToolkit `ScrollView`. This hybrid approach creates an event routing barrier where UIToolkit captures events before IMGUI can process them.

---

## Migration Plan

### Phase 1: Convert Blend Space Editors (HIGH priority) ✅ In Progress

Convert IMGUI-based blend space editors to native UIToolkit VisualElements:

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

**Key Changes:**
- Use `generateVisualContent += OnGenerateVisualContent` for custom drawing
- Native UIToolkit event handlers (MouseDownEvent, WheelEvent, etc.)
- No IMGUIContainer wrapper needed
- Consistent coordinate system throughout

**Reference Implementation:** `TransitionTimeline.cs` - already demonstrates the pattern

### Phase 2: Update Builder Wrappers (MEDIUM priority)

Once blend space is UIToolkit-native:
- `BlendContentBuilderBase` - Replace `IMGUIContainer` with direct `BlendSpaceVisualElement`
- `TransitionInspectorBuilder` - Replace blend space `IMGUIContainer`

### Phase 3: Optional Cleanup (LOW priority)

- `BlendCurveEditorWindow` - Standalone window, works fine as IMGUI
- `AnimationPreviewWindow.previewContainer` - 3D preview uses GL/Handles, keep as IMGUI

---

## Components Affected

| Component | Current | Target | Lines | Status |
|-----------|---------|--------|-------|--------|
| `BlendSpaceVisualEditorBase` | IMGUI class | `BlendSpaceVisualElement` | ~900 | 🔄 In Progress |
| `BlendSpace2DVisualEditor` | IMGUI (extends base) | `BlendSpace2DVisualElement` | ~300 | ⏳ Pending |
| `BlendSpace1DVisualEditor` | IMGUI (extends base) | `BlendSpace1DVisualElement` | ~180 | ⏳ Pending |
| `BlendContentBuilderBase` | IMGUIContainer wrapper | Direct VisualElement | ~400 | ⏳ Phase 2 |
| `TransitionInspectorBuilder` | IMGUIContainer | Direct VisualElement | ~1000 | ⏳ Phase 2 |
| `BlendCurveEditorWindow` | Pure IMGUI EditorWindow | Keep as-is | ~300 | ✅ No change |
| `AnimationPreviewWindow` | IMGUIContainer for 3D | Keep as-is | ~50 | ✅ No change |

---

## UIToolkit Drawing Pattern

### Before (IMGUI)
```csharp
internal class BlendSpaceVisualEditorBase
{
    public void Draw(Rect rect, SerializedObject serializedObject)
    {
        // IMGUI drawing with EditorGUI, GUI, Handles
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
        generateVisualContent += OnGenerateVisualContent;
        RegisterCallback<MouseDownEvent>(OnMouseDown);
        RegisterCallback<WheelEvent>(OnWheel);
        // etc.
    }
    
    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        var painter = ctx.painter2D;
        
        // Background
        painter.fillColor = BackgroundColor;
        painter.BeginPath();
        painter.MoveTo(new Vector2(0, 0));
        painter.LineTo(new Vector2(contentRect.width, 0));
        // ...
        painter.Fill();
        
        // Lines
        painter.strokeColor = GridColor;
        painter.lineWidth = 1f;
        painter.BeginPath();
        painter.MoveTo(start);
        painter.LineTo(end);
        painter.Stroke();
    }
}
```

---

## Event Handling Comparison

### Before (IMGUI in IMGUIContainer)
```csharp
// In BlendSpaceVisualEditorBase
protected virtual void HandleInput(Event e, Rect rect)
{
    if (e.type == EventType.ScrollWheel)
    {
        zoom = Mathf.Clamp(zoom - e.delta.y * 0.1f, MinZoom, MaxZoom);
        e.Use();  // Often doesn't prevent parent ScrollView from scrolling
    }
}

// In BlendContentBuilderBase - wrapper tries to stop propagation
container.RegisterCallback<WheelEvent>(evt =>
{
    evt.StopPropagation();  // Doesn't always work due to event routing
    evt.PreventDefault();
    container.MarkDirtyRepaint();
}, TrickleDown.TrickleDown);
```

### After (Native UIToolkit)
```csharp
// In BlendSpaceVisualElement
private void OnWheel(WheelEvent evt)
{
    zoom = Mathf.Clamp(zoom - evt.delta.y * 0.1f, MinZoom, MaxZoom);
    evt.StopPropagation();  // Works correctly - native event system
    MarkDirtyRepaint();
}
```

---

## Migration Checklist

### Phase 1 Tasks

- [ ] Create `BlendSpaceVisualElement` base class
  - [ ] Port view state (zoom, panOffset, selection)
  - [ ] Port drawing code to `Painter2D`
  - [ ] Port event handlers to UIToolkit callbacks
  - [ ] Port edit mode / preview mode toggle
  - [ ] Port clip selection and dragging

- [ ] Create `BlendSpace2DVisualElement`
  - [ ] Port 2D-specific drawing (grid, clips, preview indicator)
  - [ ] Port coordinate transforms (BlendSpaceToScreen, ScreenToBlendSpace)
  - [ ] Port 2D-specific interaction (clip position editing)

- [ ] Create `BlendSpace1DVisualElement`
  - [ ] Port 1D-specific drawing (track, clips, threshold markers)
  - [ ] Port 1D-specific interaction (threshold editing)

- [ ] Update tests

### Phase 2 Tasks

- [ ] Update `BlendContentBuilderBase` to use `BlendSpaceVisualElement`
- [ ] Update `TransitionInspectorBuilder` to use `BlendSpaceVisualElement`
- [ ] Remove dead IMGUIContainer code
- [ ] Verify scroll/zoom works correctly in all contexts

---

## Files to Create

```
Editor/EditorWindows/
├── BlendSpaceVisualElement.cs (NEW - base class)
├── BlendSpace2DVisualElement.cs (NEW - 2D implementation)
├── BlendSpace1DVisualElement.cs (NEW - 1D implementation)
├── BlendSpaceVisualEditorBase.cs (DELETE after migration)
├── BlendSpace2DVisualEditor.cs (DELETE after migration)
└── BlendSpace1DVisualEditor.cs (DELETE after migration)
```

---

## Notes

- `TransitionTimeline.cs` is the reference implementation for UIToolkit drawing
- Keep the public API similar to minimize changes in consumers
- Use `MarkDirtyRepaint()` instead of relying on IMGUI's automatic repainting
- `Painter2D` replaces `EditorGUI.DrawRect`, `Handles.DrawLine`, etc.
