using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for UIToolkit-based blend space visual editors (1D and 2D).
    /// Provides common zoom, pan, selection, and rendering functionality.
    /// 
    /// Uses pure UIElements with Painter2D for consistent event handling.
    /// </summary>
    [UxmlElement]
    internal abstract partial class BlendSpaceVisualElement : VisualElement
    {
        #region Constants
        
        protected const float MinZoom = 0.5f;
        protected const float MaxZoom = 3f;
        protected const float ClipCircleRadius = 10f;
        protected const float PreviewIndicatorRadius = 12f;
        protected const float ClipHandleRadius = 8f;
        protected const float ModeButtonSize = 20f;
        protected const float OverlayPadding = 5f;
        
        #endregion
        
        #region Colors
        
        protected static readonly Color GridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        protected static readonly Color AxisColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color TickColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        protected static readonly Color MajorTickColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color SelectionColor = new Color(1f, 0.8f, 0f, 1f);
        protected static readonly Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        protected static readonly Color PreviewIndicatorColor = new Color(1f, 0.5f, 0f, 1f);
        protected static readonly Color ModeButtonBgColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        protected static readonly Color ModeButtonHoverColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);
        protected static readonly Color EditModeAccentColor = new Color(0.4f, 0.7f, 1f, 1f);
        protected static readonly Color PreviewModeAccentColor = new Color(1f, 0.5f, 0f, 1f);
        protected static readonly Color LabelBackgroundColor = new Color(0, 0, 0, 0.7f);
        
        private static readonly Color[] ClipColors = GenerateClipColors(16);
        
        #endregion
        
        #region View State
        
        protected float zoom = 1f;
        protected Vector2 panOffset;
        protected int selectedClipIndex = -1;
        protected bool isDraggingClip;
        protected bool isPanning;
        protected Vector2 lastMousePos;
        
        // Preview indicator state
        protected Vector2 previewPosition;
        protected bool showPreviewIndicator;
        protected bool isDraggingPreviewIndicator;
        protected int previewClipIndex = -1;
        
        // Edit mode state
        protected bool editMode;
        protected bool showModeToggle = true;
        
        // Mode button hit rect (calculated during draw)
        protected Rect modeButtonRect;
        protected bool isModeButtonHovered;
        
        // Pointer capture
        private int capturedPointerId = -1;
        
        #endregion
        
        #region Properties
        
        /// <summary>Currently selected clip index, or -1 if none selected.</summary>
        public int SelectedClipIndex => selectedClipIndex;
        
        /// <summary>Whether to show and allow dragging the preview position indicator.</summary>
        public bool ShowPreviewIndicator
        {
            get => showPreviewIndicator;
            set
            {
                if (showPreviewIndicator != value)
                {
                    showPreviewIndicator = value;
                    MarkDirtyRepaint();
                }
            }
        }
        
        /// <summary>Whether clips can be edited (dragged to change thresholds/positions).</summary>
        public bool EditMode
        {
            get => editMode;
            set
            {
                if (editMode != value)
                {
                    editMode = value;
                    OnEditModeChanged?.Invoke(editMode);
                    MarkDirtyRepaint();
                }
            }
        }
        
        /// <summary>Whether to show the Edit/Preview mode toggle button.</summary>
        public bool ShowModeToggle
        {
            get => showModeToggle;
            set
            {
                if (showModeToggle != value)
                {
                    showModeToggle = value;
                    MarkDirtyRepaint();
                }
            }
        }
        
        /// <summary>Current preview position in blend space coordinates.</summary>
        public Vector2 PreviewPosition
        {
            get => previewPosition;
            set
            {
                var clampedValue = ClampPreviewPosition(value);
                if (previewPosition != clampedValue)
                {
                    previewPosition = clampedValue;
                    OnPreviewPositionChanged?.Invoke(previewPosition);
                    MarkDirtyRepaint();
                }
            }
        }
        
        #endregion
        
        #region Events
        
        /// <summary>Event fired when edit mode is toggled.</summary>
        public event Action<bool> OnEditModeChanged;
        
        /// <summary>Event fired when a clip is selected (for individual clip preview). -1 = blended.</summary>
        public event Action<int> OnClipSelectedForPreview;
        
        /// <summary>Event fired when selection changes.</summary>
        public event Action<int> OnSelectionChanged;
        
        /// <summary>Event fired when preview position changes (from dragging indicator).</summary>
        public event Action<Vector2> OnPreviewPositionChanged;
        
        #endregion
        
        #region Constructor
        
        public BlendSpaceVisualElement()
        {
            AddToClassList("blend-space");
            
            // Enable custom drawing
            generateVisualContent += OnGenerateVisualContent;
            
            // Ensure this element receives pointer/wheel events
            focusable = true;
            pickingMode = PickingMode.Position;
            
            // Register event handlers
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            // Wheel event for zoom - StopPropagation + PreventDefault prevents parent ScrollView from scrolling
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }
        
        #endregion
        
        #region Abstract Methods
        
        /// <summary>The title for this editor type (e.g., "Blend Track", "Blend Space 2D").</summary>
        public abstract string EditorTitle { get; }
        
        /// <summary>Gets the number of clips in the current data.</summary>
        protected abstract int GetClipCount();
        
        /// <summary>Gets the name of the clip at the specified index.</summary>
        protected abstract string GetClipName(int index);
        
        /// <summary>Draws the background elements (grid, track, axes).</summary>
        protected abstract void DrawBackground(Painter2D painter, Rect rect);
        
        /// <summary>Draws all clips in the blend space.</summary>
        protected abstract void DrawClips(Painter2D painter, Rect rect);
        
        /// <summary>Gets the screen position of a clip for hit testing.</summary>
        protected abstract Vector2 GetClipScreenPosition(int index, Rect rect);
        
        /// <summary>Handles dragging a clip to a new position.</summary>
        protected abstract void HandleClipDrag(Vector2 mousePos, Rect rect);
        
        /// <summary>Handles panning the view.</summary>
        protected abstract void HandlePan(Vector2 delta, Rect rect);
        
        /// <summary>Handles zooming at a specific mouse position.</summary>
        protected abstract void HandleZoom(float delta, Vector2 mousePos, Rect rect);
        
        /// <summary>Converts a blend space position to screen coordinates.</summary>
        protected abstract Vector2 BlendSpaceToScreen(Vector2 blendPos, Rect rect);
        
        /// <summary>Converts screen coordinates to blend space position.</summary>
        protected abstract Vector2 ScreenToBlendSpace(Vector2 screenPos, Rect rect);
        
        /// <summary>Clamps a preview position to valid bounds.</summary>
        protected abstract Vector2 ClampPreviewPosition(Vector2 position);
        
        /// <summary>Gets the bounds of the blend space (min, max).</summary>
        protected abstract void GetBlendSpaceBounds(out Vector2 min, out Vector2 max);
        
        #endregion
        
        #region Virtual Methods
        
        /// <summary>Gets the clip index at the given mouse position, or -1 if none.</summary>
        protected virtual int GetClipAtPosition(Rect rect, Vector2 mousePos)
        {
            var clipCount = GetClipCount();
            for (var i = clipCount - 1; i >= 0; i--)
            {
                var screenPos = GetClipScreenPosition(i, rect);
                if (IsPointInCircle(mousePos, screenPos, ClipCircleRadius + 2))
                    return i;
                
                // Check label
                var labelRect = GetClipLabelRect(i, rect);
                if (labelRect.Contains(mousePos))
                    return i;
            }
            return -1;
        }
        
        /// <summary>Gets the label rect for a clip (for hit testing clicks on names).</summary>
        protected virtual Rect GetClipLabelRect(int index, Rect rect)
        {
            var screenPos = GetClipScreenPosition(index, rect);
            var clipName = GetClipName(index);
            var labelWidth = clipName.Length * 7f + 8f; // Approximate
            var labelHeight = 14f;
            
            return new Rect(
                screenPos.x - labelWidth / 2 - 2,
                screenPos.y - ClipCircleRadius - labelHeight - 4,
                labelWidth + 4,
                labelHeight + 2);
        }
        
        /// <summary>Gets the help text for the current mode.</summary>
        public virtual string GetHelpText()
        {
            if (editMode)
                return "Drag clip to move  |  Shift: Snap to grid\nScroll: Zoom  |  MMB/Alt+Click: Pan";
            if (showPreviewIndicator)
                return "Drag to set blend position  |  Click clip to preview\nScroll: Zoom  |  MMB/Alt+Click: Pan";
            return "Scroll: Zoom  |  MMB/Alt+Click: Pan";
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>Resets the view to default zoom and pan.</summary>
        public virtual void ResetView()
        {
            zoom = 1f;
            panOffset = Vector2.zero;
            MarkDirtyRepaint();
        }
        
        /// <summary>Clears the current selection.</summary>
        public void ClearSelection()
        {
            if (selectedClipIndex != -1)
            {
                selectedClipIndex = -1;
                OnSelectionChanged?.Invoke(-1);
                MarkDirtyRepaint();
            }
        }
        
        /// <summary>Sets the selection to a specific clip index.</summary>
        public void SetSelection(int index)
        {
            if (selectedClipIndex != index)
            {
                selectedClipIndex = index;
                OnSelectionChanged?.Invoke(index);
                MarkDirtyRepaint();
            }
        }
        
        #endregion
        
        #region Drawing
        
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width < 10 || rect.height < 10) return;
            
            var painter = ctx.painter2D;
            
            // Draw background
            painter.fillColor = BackgroundColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, 0));
            painter.LineTo(new Vector2(rect.width, 0));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0, rect.height));
            painter.ClosePath();
            painter.Fill();
            
            // Draw background elements (grid/track/axes) - subclass specific
            DrawBackground(painter, rect);
            
            // Draw clips - subclass specific
            DrawClips(painter, rect);
            
            // Draw preview indicator
            DrawPreviewIndicator(painter, rect);
            
            // Draw overlay UI
            DrawOverlayUI(painter, rect);
        }
        
        /// <summary>Draws the preview position indicator.</summary>
        protected virtual void DrawPreviewIndicator(Painter2D painter, Rect rect)
        {
            if (!showPreviewIndicator || editMode) return;
            
            var screenPos = BlendSpaceToScreen(previewPosition, rect);
            
            // Don't draw if outside bounds
            var margin = PreviewIndicatorRadius + 4;
            if (screenPos.x < -margin || screenPos.x > rect.width + margin ||
                screenPos.y < -margin || screenPos.y > rect.height + margin)
            {
                return;
            }
            
            // Draw glow when dragging
            if (isDraggingPreviewIndicator)
            {
                var glowColor = PreviewIndicatorColor;
                glowColor.a = 0.5f;
                DrawFilledCircle(painter, screenPos, PreviewIndicatorRadius + 4, glowColor);
            }
            
            // Draw indicator circle
            DrawFilledCircle(painter, screenPos, PreviewIndicatorRadius, PreviewIndicatorColor);
            
            // Draw white border
            painter.strokeColor = Color.white;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.Arc(screenPos, PreviewIndicatorRadius, 0, 360);
            painter.Stroke();
        }
        
        /// <summary>Draws all overlay UI elements (mode button, zoom indicator).</summary>
        private void DrawOverlayUI(Painter2D painter, Rect rect)
        {
            if (showModeToggle)
            {
                DrawModeToggleButton(painter, rect);
            }
            
            DrawOverlayInfoLabel(painter, rect);
            DrawZoomIndicator(painter, rect);
        }
        
        /// <summary>Draws the edit/preview mode toggle button.</summary>
        private void DrawModeToggleButton(Painter2D painter, Rect rect)
        {
            modeButtonRect = new Rect(OverlayPadding, OverlayPadding, ModeButtonSize, ModeButtonSize);
            
            var bgColor = isModeButtonHovered ? ModeButtonHoverColor : ModeButtonBgColor;
            var accentColor = editMode ? EditModeAccentColor : PreviewModeAccentColor;
            
            // Draw button background
            painter.fillColor = bgColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(modeButtonRect.x, modeButtonRect.y));
            painter.LineTo(new Vector2(modeButtonRect.xMax, modeButtonRect.y));
            painter.LineTo(new Vector2(modeButtonRect.xMax, modeButtonRect.yMax));
            painter.LineTo(new Vector2(modeButtonRect.x, modeButtonRect.yMax));
            painter.ClosePath();
            painter.Fill();
            
            // Draw accent border (left side)
            painter.fillColor = accentColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(modeButtonRect.x, modeButtonRect.y));
            painter.LineTo(new Vector2(modeButtonRect.x + 3, modeButtonRect.y));
            painter.LineTo(new Vector2(modeButtonRect.x + 3, modeButtonRect.yMax));
            painter.LineTo(new Vector2(modeButtonRect.x, modeButtonRect.yMax));
            painter.ClosePath();
            painter.Fill();
            
            // Draw icon (simple shapes)
            var iconCenter = modeButtonRect.center;
            if (editMode)
            {
                // Edit icon - pencil shape
                painter.strokeColor = accentColor;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(iconCenter + new Vector2(-4, 4));
                painter.LineTo(iconCenter + new Vector2(4, -4));
                painter.Stroke();
            }
            else
            {
                // Preview icon - play triangle
                painter.fillColor = accentColor;
                painter.BeginPath();
                painter.MoveTo(iconCenter + new Vector2(-3, -5));
                painter.LineTo(iconCenter + new Vector2(5, 0));
                painter.LineTo(iconCenter + new Vector2(-3, 5));
                painter.ClosePath();
                painter.Fill();
            }
        }
        
        /// <summary>Draws the info label (preview state or selection).</summary>
        protected virtual void DrawOverlayInfoLabel(Painter2D painter, Rect rect)
        {
            string infoText;
            Color textColor;
            
            if (showPreviewIndicator && !editMode)
            {
                if (previewClipIndex < 0)
                {
                    infoText = "Blended";
                    textColor = PreviewIndicatorColor;
                }
                else
                {
                    infoText = GetClipName(previewClipIndex);
                    textColor = new Color(0.4f, 0.8f, 1f);
                }
            }
            else if (selectedClipIndex >= 0)
            {
                infoText = GetClipName(selectedClipIndex);
                textColor = SelectionColor;
            }
            else
            {
                return;
            }
            
            // Position after mode button
            float labelX = showModeToggle ? 80 : OverlayPadding;
            string displayText = showModeToggle ? $"| {infoText}" : infoText;
            
            // Note: Painter2D doesn't support text directly, we'd need a Label child element
            // For now, the info is available via GetHelpText() for external labels
        }
        
        /// <summary>Draws the zoom indicator in the top-right corner.</summary>
        private void DrawZoomIndicator(Painter2D painter, Rect rect)
        {
            var zoomText = $"{zoom:F1}x";
            var zoomRect = new Rect(rect.width - 35, OverlayPadding, 30, 16);
            
            // Draw background
            painter.fillColor = new Color(0, 0, 0, 0.5f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(zoomRect.x - 2, zoomRect.y - 1));
            painter.LineTo(new Vector2(zoomRect.xMax + 2, zoomRect.y - 1));
            painter.LineTo(new Vector2(zoomRect.xMax + 2, zoomRect.yMax + 1));
            painter.LineTo(new Vector2(zoomRect.x - 2, zoomRect.yMax + 1));
            painter.ClosePath();
            painter.Fill();
            
            // Note: Text would need a Label child element
        }
        
        #endregion
        
        #region Drawing Utilities
        
        /// <summary>Draws a filled circle at the specified position.</summary>
        protected void DrawFilledCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, 0, 360);
            painter.Fill();
        }
        
        /// <summary>Draws a circle outline at the specified position.</summary>
        protected void DrawCircleOutline(Painter2D painter, Vector2 center, float radius, Color color, float lineWidth = 1f)
        {
            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            painter.Arc(center, radius, 0, 360);
            painter.Stroke();
        }
        
        /// <summary>Draws a line between two points.</summary>
        protected void DrawLine(Painter2D painter, Vector2 from, Vector2 to, Color color, float lineWidth = 1f)
        {
            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }
        
        /// <summary>Draws a filled rectangle.</summary>
        protected void DrawFilledRect(Painter2D painter, Rect rect, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.x, rect.y));
            painter.LineTo(new Vector2(rect.xMax, rect.y));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.x, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }
        
        /// <summary>Draws a clip circle with optional selection ring.</summary>
        protected void DrawClipCircle(Painter2D painter, Vector2 screenPos, int clipIndex, bool showValue = false)
        {
            var isSelected = clipIndex == selectedClipIndex;
            var color = GetClipColor(clipIndex);
            
            // Draw selection ring
            if (isSelected)
            {
                DrawFilledCircle(painter, screenPos, ClipCircleRadius + 3, SelectionColor);
            }
            
            // Draw clip circle
            DrawFilledCircle(painter, screenPos, ClipCircleRadius, color);
        }
        
        /// <summary>Gets the color for a clip based on its index.</summary>
        protected static Color GetClipColor(int index)
        {
            return ClipColors[index % ClipColors.Length];
        }
        
        private static Color[] GenerateClipColors(int count)
        {
            var colors = new Color[count];
            for (var i = 0; i < count; i++)
            {
                var hue = (float)i / count;
                colors[i] = Color.HSVToRGB(hue, 0.7f, 0.9f);
            }
            return colors;
        }
        
        #endregion
        
        #region Geometry Utilities
        
        /// <summary>Checks if a point is within a circle.</summary>
        protected bool IsPointInCircle(Vector2 point, Vector2 center, float radius)
        {
            return Vector2.Distance(point, center) <= radius;
        }
        
        /// <summary>Checks if the mouse is over the preview indicator.</summary>
        protected bool IsMouseOverPreviewIndicator(Rect rect, Vector2 mousePos)
        {
            if (!showPreviewIndicator || editMode) return false;
            var screenPos = BlendSpaceToScreen(previewPosition, rect);
            return IsPointInCircle(mousePos, screenPos, PreviewIndicatorRadius + 4);
        }
        
        /// <summary>Applies zoom delta with clamping.</summary>
        protected float ApplyZoomDelta(float delta)
        {
            zoom = Mathf.Clamp(zoom + delta, MinZoom, MaxZoom);
            return zoom;
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            MarkDirtyRepaint();
        }
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            var rect = contentRect;
            var mousePos = (Vector2)evt.localPosition;
            
            Focus();
            
            // Check for pan (middle click or Alt+Left)
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                isPanning = true;
                lastMousePos = mousePos;
                CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }
            
            if (evt.button == 0) // Left click
            {
                // Check mode button first
                if (showModeToggle && modeButtonRect.Contains(mousePos))
                {
                    EditMode = !editMode;
                    evt.StopPropagation();
                    return;
                }
                
                // Check preview indicator
                if (showPreviewIndicator && IsMouseOverPreviewIndicator(rect, mousePos))
                {
                    isDraggingPreviewIndicator = true;
                    lastMousePos = mousePos;
                    SetPreviewClip(-1);
                    CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                    return;
                }
                
                // Check clips
                var clickedIndex = GetClipAtPosition(rect, mousePos);
                if (clickedIndex >= 0)
                {
                    SetSelection(clickedIndex);
                    
                    if (showPreviewIndicator && !editMode)
                    {
                        SetPreviewClip(clickedIndex);
                    }
                    else if (editMode)
                    {
                        isDraggingClip = true;
                        lastMousePos = mousePos;
                        CapturePointer(evt.pointerId);
                    }
                    
                    evt.StopPropagation();
                }
                else if (showPreviewIndicator && !editMode)
                {
                    // Click empty space to move preview indicator
                    PreviewPosition = ScreenToBlendSpace(mousePos, rect);
                    isDraggingPreviewIndicator = true;
                    lastMousePos = mousePos;
                    SetPreviewClip(-1);
                    CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
                else
                {
                    SetSelection(-1);
                }
            }
        }
        
        private void OnPointerMove(PointerMoveEvent evt)
        {
            var rect = contentRect;
            var mousePos = (Vector2)evt.localPosition;
            
            // Update mode button hover state
            bool wasHovered = isModeButtonHovered;
            isModeButtonHovered = showModeToggle && modeButtonRect.Contains(mousePos);
            if (wasHovered != isModeButtonHovered)
            {
                MarkDirtyRepaint();
            }
            
            if (isDraggingPreviewIndicator && !editMode)
            {
                var clampedMousePos = ClampMouseToRect(mousePos, rect);
                PreviewPosition = ScreenToBlendSpace(clampedMousePos, rect);
                evt.StopPropagation();
            }
            else if (isDraggingClip && selectedClipIndex >= 0 && editMode)
            {
                HandleClipDrag(mousePos, rect);
                lastMousePos = mousePos;
                evt.StopPropagation();
            }
            else if (isPanning)
            {
                var delta = mousePos - lastMousePos;
                HandlePan(delta, rect);
                lastMousePos = mousePos;
                MarkDirtyRepaint();
                evt.StopPropagation();
            }
        }
        
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (isDraggingClip || isDraggingPreviewIndicator || isPanning)
            {
                isDraggingClip = false;
                isDraggingPreviewIndicator = false;
                isPanning = false;
                ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }
        
        private void OnPointerEnter(PointerEnterEvent evt)
        {
            // Focus the element to capture wheel events
            Focus();
            
            // Disable parent ScrollView's wheel scrolling while mouse is over this element
            var scrollView = GetFirstAncestorOfType<ScrollView>();
            if (scrollView != null)
            {
                // Store the original value and set to 0 to disable wheel scrolling
                scrollView.userData = scrollView.mouseWheelScrollSize;
                scrollView.mouseWheelScrollSize = 0;
            }
        }
        
        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (isModeButtonHovered)
            {
                isModeButtonHovered = false;
                MarkDirtyRepaint();
            }
            
            // Restore parent ScrollView's wheel scrolling
            var scrollView = GetFirstAncestorOfType<ScrollView>();
            if (scrollView != null && scrollView.userData is float originalSize)
            {
                scrollView.mouseWheelScrollSize = originalSize;
                scrollView.userData = null;
            }
        }
        
        private void OnWheel(WheelEvent evt)
        {
            var rect = contentRect;
            var mousePos = evt.localMousePosition;
            
            // Only zoom if mouse is in bounds
            if (!rect.Contains(mousePos)) return;
            
            float delta = -evt.delta.y * 0.05f;
            HandleZoom(delta, mousePos, rect);
            
            MarkDirtyRepaint();
            
            // Use all available methods to prevent parent ScrollView from handling this event
            evt.StopImmediatePropagation();
            evt.PreventDefault();
        }
        
        private void CapturePointer(int pointerId)
        {
            if (capturedPointerId < 0)
            {
                capturedPointerId = pointerId;
                this.CapturePointer(pointerId);
            }
        }
        
        private void ReleasePointer(int pointerId)
        {
            if (capturedPointerId == pointerId)
            {
                this.ReleasePointer(pointerId);
                capturedPointerId = -1;
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>Clamps a mouse position to the rect bounds.</summary>
        protected Vector2 ClampMouseToRect(Vector2 mousePos, Rect rect)
        {
            return new Vector2(
                Mathf.Clamp(mousePos.x, rect.x, rect.xMax),
                Mathf.Clamp(mousePos.y, rect.y, rect.yMax));
        }
        
        /// <summary>Sets the preview clip index and fires event.</summary>
        protected void SetPreviewClip(int index)
        {
            previewClipIndex = index;
            
            if (index < 0)
            {
                selectedClipIndex = -1;
            }
            
            OnClipSelectedForPreview?.Invoke(index);
            MarkDirtyRepaint();
        }
        
        #endregion
    }
}
