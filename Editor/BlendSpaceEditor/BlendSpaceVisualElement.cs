using System;
using UnityEditor;
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
        // Note: ModeButtonBgColor, ModeButtonHoverColor, EditModeAccentColor, 
        // PreviewModeAccentColor, and LabelBackgroundColor moved to USS
        
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
        
        // Pointer capture
        private int capturedPointerId = -1;
        
        // Scroll capture - smart handling for nested scroll contexts
        private long pointerEnterTimeMs;
        private long lastWheelEventTimeMs;
        private bool hasDisabledParentScroll;
        private bool lastWheelWentToParent;
        private ScrollView cachedParentScrollView;
        private float cachedOriginalScrollSize;
        private const long ScrollCaptureDelayMs = 150; // Grace period after entering before capturing scroll
        private const long ScrollSessionTimeoutMs = 400; // Scroll session continues if wheel events within this time
        
        // UI Label elements (since Painter2D can't draw text)
        // Container for clip labels - added first so it renders behind HUD elements
        protected VisualElement clipLabelContainer;
        // HUD elements (rendered on top of clip labels)
        private VisualElement modeButton;
        private Label zoomLabel;
        private Label modeLabel;
        private Label infoLabel;
        
        // Scheduling utility state
        private readonly System.Collections.Generic.Dictionary<string, bool> scheduledActions = new();
        
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
            // Load stylesheet
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.gamedevpro.dmotion/Editor/EditorWindows/BlendSpaceVisualElement.uss");
            if (uss != null)
            {
                styleSheets.Add(uss);
            }
            
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
            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // Create clip label container FIRST (so it renders behind HUD elements)
            clipLabelContainer = new VisualElement();
            clipLabelContainer.name = "clip-labels";
            clipLabelContainer.AddToClassList("blend-space__clip-labels");
            clipLabelContainer.pickingMode = PickingMode.Ignore;
            Add(clipLabelContainer);
            
            // Create HUD overlay labels (renders on top of clip labels)
            CreateOverlayLabels();
        }
        
        /// <summary>
        /// Schedules an action to run after the current frame, with debouncing.
        /// Only one action per key will be scheduled at a time.
        /// </summary>
        protected void ScheduleOnce(string key, System.Action action)
        {
            if (scheduledActions.TryGetValue(key, out bool isScheduled) && isScheduled)
                return;
            
            scheduledActions[key] = true;
            schedule.Execute(() =>
            {
                scheduledActions[key] = false;
                action?.Invoke();
            });
        }
        
        private void CreateOverlayLabels()
        {
            // Mode toggle button (top-left) - UIToolkit element so it renders on top of clip labels
            modeButton = new VisualElement();
            modeButton.name = "mode-button";
            modeButton.AddToClassList("blend-space__mode-button");
            modeButton.AddToClassList("blend-space__mode-button--preview");
            modeButton.pickingMode = PickingMode.Position;
            modeButton.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0)
                {
                    EditMode = !editMode;
                    e.StopPropagation();
                }
            });
            
            // Icon inside button
            var iconLabel = new Label("▶");
            iconLabel.name = "mode-icon";
            iconLabel.AddToClassList("blend-space__mode-icon");
            iconLabel.AddToClassList("blend-space__mode-icon--preview");
            iconLabel.pickingMode = PickingMode.Ignore;
            modeButton.Add(iconLabel);
            Add(modeButton);
            
            // Zoom indicator label (top-right)
            zoomLabel = new Label("1.0x");
            zoomLabel.name = "zoom-label";
            zoomLabel.AddToClassList("blend-space__zoom-label");
            zoomLabel.pickingMode = PickingMode.Ignore;
            Add(zoomLabel);
            
            // Mode label (top-left, next to mode button)
            modeLabel = new Label("Preview");
            modeLabel.name = "mode-label";
            modeLabel.AddToClassList("blend-space__mode-label");
            modeLabel.AddToClassList("blend-space__mode-label--preview");
            modeLabel.pickingMode = PickingMode.Ignore;
            Add(modeLabel);
            
            // Info label (shows selected clip or blend state)
            infoLabel = new Label("");
            infoLabel.name = "info-label";
            infoLabel.AddToClassList("blend-space__info-label");
            infoLabel.pickingMode = PickingMode.Ignore;
            Add(infoLabel);
        }
        
        /// <summary>Updates overlay label text and visibility.</summary>
        protected void UpdateOverlayLabels()
        {
            // Update zoom label
            if (zoomLabel != null)
            {
                zoomLabel.text = $"{zoom:F1}x";
            }
            
            // Update mode button classes
            if (modeButton != null)
            {
                modeButton.EnableInClassList("blend-space__mode-button--edit", editMode);
                modeButton.EnableInClassList("blend-space__mode-button--preview", !editMode);
                modeButton.EnableInClassList("blend-space__mode-button--hidden", !showModeToggle);
                
                // Update icon
                var iconLabel = modeButton.Q<Label>("mode-icon");
                if (iconLabel != null)
                {
                    iconLabel.text = editMode ? "✎" : "▶";
                    iconLabel.EnableInClassList("blend-space__mode-icon--edit", editMode);
                    iconLabel.EnableInClassList("blend-space__mode-icon--preview", !editMode);
                }
            }
            
            // Update mode label classes
            if (modeLabel != null)
            {
                modeLabel.text = editMode ? "Edit" : "Preview";
                modeLabel.EnableInClassList("blend-space__mode-label--edit", editMode);
                modeLabel.EnableInClassList("blend-space__mode-label--preview", !editMode);
                modeLabel.EnableInClassList("blend-space__mode-label--hidden", !showModeToggle);
            }
            
            // Update info label classes
            if (infoLabel != null)
            {
                // Clear all state classes first
                infoLabel.RemoveFromClassList("blend-space__info-label--blended");
                infoLabel.RemoveFromClassList("blend-space__info-label--clip-preview");
                infoLabel.RemoveFromClassList("blend-space__info-label--selected");
                
                bool showInfo = false;
                if (showPreviewIndicator && !editMode)
                {
                    if (previewClipIndex < 0)
                    {
                        infoLabel.text = "| Blended";
                        infoLabel.AddToClassList("blend-space__info-label--blended");
                    }
                    else
                    {
                        infoLabel.text = $"| {GetClipName(previewClipIndex)}";
                        infoLabel.AddToClassList("blend-space__info-label--clip-preview");
                    }
                    showInfo = true;
                }
                else if (selectedClipIndex >= 0)
                {
                    infoLabel.text = $"| {GetClipName(selectedClipIndex)}";
                    infoLabel.AddToClassList("blend-space__info-label--selected");
                    showInfo = true;
                }
                
                infoLabel.EnableInClassList("blend-space__info-label--hidden", !showInfo);
            }
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
            
            // Schedule HUD updates (mode button and labels are UIToolkit elements)
            ScheduleOnce("overlay-labels", UpdateOverlayLabels);
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
        
        // Note: Mode button, info labels, and zoom indicator are now UIToolkit elements
        // They render on top of clip labels and are updated in UpdateOverlayLabels()
        
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
                TryCapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }
            
            if (evt.button == 0) // Left click
            {
                // Note: Mode button is now a UIToolkit element with its own click handler
                
                // Check preview indicator
                if (showPreviewIndicator && IsMouseOverPreviewIndicator(rect, mousePos))
                {
                    isDraggingPreviewIndicator = true;
                    lastMousePos = mousePos;
                    SetPreviewClip(-1);
                    TryCapturePointer(evt.pointerId);
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
                        TryCapturePointer(evt.pointerId);
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
                    TryCapturePointer(evt.pointerId);
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
            // Note: Mode button hover is now handled by the UIToolkit element
            
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
                TryReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }
        
        private void OnPointerEnter(PointerEnterEvent evt)
        {
            // Focus the element for keyboard input
            Focus();
            
            // Cache parent ScrollView and its original scroll size
            cachedParentScrollView = GetFirstAncestorOfType<ScrollView>();
            if (cachedParentScrollView != null)
            {
                cachedOriginalScrollSize = cachedParentScrollView.mouseWheelScrollSize;
            }
            
            // Note when we entered - used for smart scroll capture
            // We DON'T disable ScrollView here to allow scroll continuation from parent
            pointerEnterTimeMs = System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond;
            hasDisabledParentScroll = false;
            lastWheelWentToParent = true; // Assume any ongoing scroll was going to parent
        }
        
        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            // Restore parent ScrollView's wheel scrolling if we disabled it
            if (hasDisabledParentScroll && cachedParentScrollView != null)
            {
                cachedParentScrollView.mouseWheelScrollSize = cachedOriginalScrollSize;
                hasDisabledParentScroll = false;
            }
            
            // Reset scroll session state
            lastWheelWentToParent = false;
            cachedParentScrollView = null;
        }
        
        private void OnWheel(WheelEvent evt)
        {
            var rect = contentRect;
            var mousePos = evt.localMousePosition;
            
            // Only handle if mouse is in bounds
            if (!rect.Contains(mousePos)) return;
            
            var nowMs = System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond;
            var timeSinceEnter = nowMs - pointerEnterTimeMs;
            var timeSinceLastWheel = nowMs - lastWheelEventTimeMs;
            
            // Determine if this wheel event should go to parent or be captured
            // Pass to parent if:
            // 1. Mouse just entered (within grace period), OR
            // 2. Last wheel went to parent AND we're still in the scroll session (within timeout)
            bool shouldPassToParent = false;
            
            if (timeSinceEnter < ScrollCaptureDelayMs)
            {
                // Just entered - likely continuation of parent scroll
                shouldPassToParent = true;
            }
            else if (lastWheelWentToParent && timeSinceLastWheel < ScrollSessionTimeoutMs)
            {
                // Previous wheel went to parent and we're still in active scroll session
                shouldPassToParent = true;
            }
            
            // Update timestamp
            lastWheelEventTimeMs = nowMs;
            
            if (shouldPassToParent)
            {
                lastWheelWentToParent = true;
                
                // Ensure ScrollView is enabled for parent scroll
                if (hasDisabledParentScroll && cachedParentScrollView != null)
                {
                    cachedParentScrollView.mouseWheelScrollSize = cachedOriginalScrollSize;
                    hasDisabledParentScroll = false;
                }
                
                // Let event propagate to parent ScrollView
                return;
            }
            
            // Capture for zoom - this is our scroll session now
            lastWheelWentToParent = false;
            
            // Disable parent scroll only once per session
            if (!hasDisabledParentScroll && cachedParentScrollView != null)
            {
                cachedParentScrollView.mouseWheelScrollSize = 0;
                hasDisabledParentScroll = true;
            }
            
            // Stop propagation and handle zoom
            evt.StopImmediatePropagation();
            
            float delta = -evt.delta.y * 0.05f;
            HandleZoom(delta, mousePos, rect);
            
            MarkDirtyRepaint();
        }
        
        private void TryCapturePointer(int pointerId)
        {
            if (capturedPointerId < 0)
            {
                capturedPointerId = pointerId;
                PointerCaptureHelper.CapturePointer(this, pointerId);
            }
        }
        
        private void TryReleasePointer(int pointerId)
        {
            if (capturedPointerId == pointerId)
            {
                PointerCaptureHelper.ReleasePointer(this, pointerId);
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
