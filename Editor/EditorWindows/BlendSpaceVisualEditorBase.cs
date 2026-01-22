using System;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for blend space visual editors (1D and 2D).
    /// Provides common zoom, pan, selection, and rendering functionality.
    /// </summary>
    internal abstract class BlendSpaceVisualEditorBase
    {
        // View state
        protected float zoom = 1f;
        protected Vector2 panOffset;
        protected int selectedClipIndex = -1;
        protected bool isDraggingClip;
        protected bool isPanning;
        protected Vector2 lastMousePos;
        
        // External pan trigger (for UI Toolkit integration where middle-click isn't forwarded)
        private bool externalPanActive;
        private Vector2 externalLastMousePos;
        private Vector2 pendingPanDelta;
        
        // Scroll ownership - prevents scroll hijacking when scrolling from outside
        private bool wasMouseInRect;
        private double mouseEnteredTime;
        private const double ScrollOwnershipDelay = 0.05; // Reduced from 0.25 - just enough to prevent accidental hijacking
        
        // Preview indicator state
        protected Vector2 previewPosition;
        protected bool showPreviewIndicator;
        protected bool isDraggingPreviewIndicator;
        protected int previewClipIndex = -1; // -1 = blended, >= 0 = individual clip
        protected const float PreviewIndicatorRadius = 12f;
        
        // Edit mode state
        protected bool editMode;
        protected bool showModeToggle = true; // Whether to show the Edit/Preview toggle button
        protected const float ClipHandleRadius = 8f;
        protected const float ClipLineHitRadius = 12f; // Click detection radius around lines
        
        // Visual settings
        protected const float MinZoom = 0.5f;
        protected const float MaxZoom = 3f;
        protected const float ClipCircleRadius = 10f;
        
        // UI overlay settings
        private const float ModeButtonSize = 20f;
        private const float OverlayPadding = 5f;
        private Rect modeButtonRect;
        
        // Cached GUIContent for mode button icons
        private static GUIContent editModeIcon;
        private static GUIContent previewModeIcon;
        private static GUIStyle modeButtonStyle;
        
        // Colors
        protected static readonly Color GridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        protected static readonly Color AxisColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color TickColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        protected static readonly Color MajorTickColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color SelectionColor = new Color(1f, 0.8f, 0f, 1f);
        protected static readonly Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        protected static readonly Color PreviewIndicatorColor = new Color(1f, 0.5f, 0f, 1f);
        private static readonly Color ModeButtonBgColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        private static readonly Color ModeButtonHoverColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);
        private static readonly Color EditModeAccentColor = new Color(0.4f, 0.7f, 1f, 1f);
        private static readonly Color PreviewModeAccentColor = new Color(1f, 0.5f, 0f, 1f);
        
        // Cached clip colors
        private static readonly Color[] ClipColors = GenerateClipColors(16);
        
        /// <summary>
        /// Currently selected clip index, or -1 if none selected.
        /// </summary>
        public int SelectedClipIndex => selectedClipIndex;
        
        /// <summary>
        /// Whether to show and allow dragging the preview position indicator.
        /// </summary>
        public bool ShowPreviewIndicator
        {
            get => showPreviewIndicator;
            set => showPreviewIndicator = value;
        }
        
        /// <summary>
        /// Whether clips can be edited (dragged to change thresholds/positions).
        /// When false, clips show as lines only. When true, clips show handles and are draggable.
        /// </summary>
        public bool EditMode
        {
            get => editMode;
            set
            {
                if (editMode != value)
                {
                    editMode = value;
                    OnEditModeChanged?.Invoke(editMode);
                }
            }
        }
        
        /// <summary>
        /// Whether to show the Edit/Preview mode toggle button.
        /// Set to false for standalone editor windows that should always be in edit mode.
        /// </summary>
        public bool ShowModeToggle
        {
            get => showModeToggle;
            set => showModeToggle = value;
        }
        
        /// <summary>
        /// Event fired when edit mode is toggled.
        /// </summary>
        public event Action<bool> OnEditModeChanged;
        
        /// <summary>
        /// Event fired when the editor needs to repaint.
        /// Host windows should subscribe to this and call their Repaint() method.
        /// </summary>
        public event Action OnRepaintRequested;
        
        /// <summary>
        /// Current preview position in blend space coordinates.
        /// For 1D, only X is used. Will be clamped to valid bounds.
        /// </summary>
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
                }
            }
        }
        
        /// <summary>
        /// Event fired when a clip is selected (for individual clip preview).
        /// Parameter is clip index, or -1 for blended preview.
        /// </summary>
        public event Action<int> OnClipSelectedForPreview;
        
        /// <summary>
        /// Event fired when selection changes.
        /// </summary>
        public event Action<int> OnSelectionChanged;
        
        /// <summary>
        /// Event fired when preview position changes (from dragging indicator).
        /// </summary>
        public event Action<Vector2> OnPreviewPositionChanged;

        #region Abstract Methods - Must be implemented by derived classes

        /// <summary>
        /// The title for this editor type (e.g., "Blend Track", "Blend Space 2D").
        /// </summary>
        public abstract string EditorTitle { get; }

        /// <summary>
        /// The target ScriptableObject being edited.
        /// </summary>
        public abstract UnityEngine.Object Target { get; }

        /// <summary>
        /// Draws the editor in the given rect. Called by external windows.
        /// </summary>
        public abstract void Draw(Rect rect, SerializedObject serializedObject);

        /// <summary>
        /// Draws the selected clip edit fields. Returns true if changes were made.
        /// </summary>
        public abstract bool DrawSelectedClipFields(SerializedObject serializedObject);

        /// <summary>
        /// Gets the number of clips in the current data.
        /// </summary>
        protected abstract int GetClipCount();

        /// <summary>
        /// Gets the name of the clip at the specified index.
        /// </summary>
        protected abstract string GetClipName(int index);

        /// <summary>
        /// Draws the background elements (grid, track, axes).
        /// </summary>
        protected abstract void DrawBackground(Rect rect);

        /// <summary>
        /// Draws all clips in the blend space.
        /// </summary>
        protected abstract void DrawClips(Rect rect);

        /// <summary>
        /// Gets the clip index at the given mouse position, or -1 if none.
        /// Default implementation checks clip visual and label.
        /// </summary>
        protected virtual int GetClipAtPosition(Rect rect, Vector2 mousePos)
        {
            var clipCount = GetClipCount();
            for (var i = clipCount - 1; i >= 0; i--)
            {
                // Check clip visual (circle)
                var screenPos = GetClipScreenPosition(i, rect);
                if (IsPointInCircle(mousePos, screenPos, ClipCircleRadius + 2))
                    return i;
                
                // Check label
                if (IsMouseOverClipLabel(i, rect, mousePos))
                    return i;
            }
            return -1;
        }
        
        /// <summary>
        /// Gets the screen position of a clip for hit testing.
        /// </summary>
        protected abstract Vector2 GetClipScreenPosition(int index, Rect rect);
        
        /// <summary>
        /// Gets the label rect for a clip (for hit testing clicks on names).
        /// Default implementation returns rect above the clip position.
        /// </summary>
        protected virtual Rect GetClipLabelRect(int index, Rect rect)
        {
            var screenPos = GetClipScreenPosition(index, rect);
            var clipName = GetClipName(index);
            var labelSize = EditorStyles.miniLabel.CalcSize(new GUIContent(clipName));
            
            // Default: label centered above clip
            return new Rect(
                screenPos.x - labelSize.x / 2 - 2,
                screenPos.y - ClipCircleRadius - labelSize.y - 4,
                labelSize.x + 4,
                labelSize.y + 2);
        }
        
        /// <summary>
        /// Checks if the mouse is over a clip's label.
        /// </summary>
        protected bool IsMouseOverClipLabel(int index, Rect rect, Vector2 mousePos)
        {
            var labelRect = GetClipLabelRect(index, rect);
            return labelRect.Contains(mousePos);
        }

        /// <summary>
        /// Handles zoom input, updating view state accordingly.
        /// </summary>
        protected abstract void HandleZoom(Rect rect, Event e);

        /// <summary>
        /// Handles dragging a clip to a new position.
        /// </summary>
        protected abstract void HandleClipDrag(Rect rect, Event e);

        /// <summary>
        /// Handles panning the view.
        /// </summary>
        protected abstract void HandlePan(Event e, Rect rect);

        /// <summary>
        /// Gets the help text for the current mode. 
        /// Call this and draw externally below the editor rect.
        /// </summary>
        public virtual string GetHelpText()
        {
            if (editMode)
                return "Drag clip to move  |  Shift: Snap to grid\nScroll: Zoom  |  MMB/Alt+Click: Pan";
            if (showPreviewIndicator)
                return "Drag to set blend position  |  Click clip to preview\nScroll: Zoom  |  MMB/Alt+Click: Pan";
            return "Scroll: Zoom  |  MMB/Alt+Click: Pan";
        }

        /// <summary>
        /// Converts a blend space position to screen coordinates.
        /// </summary>
        protected abstract Vector2 BlendSpaceToScreen(Vector2 blendPos, Rect rect);
        
        /// <summary>
        /// Converts screen coordinates to blend space position.
        /// </summary>
        protected abstract Vector2 ScreenToBlendSpace(Vector2 screenPos, Rect rect);
        
        /// <summary>
        /// Clamps a preview position to valid bounds.
        /// </summary>
        protected abstract Vector2 ClampPreviewPosition(Vector2 position);
        
        /// <summary>
        /// Gets the bounds of the blend space (min, max).
        /// </summary>
        protected abstract void GetBlendSpaceBounds(out Vector2 min, out Vector2 max);
        
        /// <summary>
        /// Applies pan delta from external UI Toolkit events.
        /// </summary>
        protected abstract void ApplyExternalPanDelta(Rect rect, Vector2 delta);
        
        /// <summary>
        /// Draws the preview position indicator.
        /// Only visible in preview mode (not edit mode).
        /// </summary>
        protected virtual void DrawPreviewIndicator(Rect rect)
        {
            // Only show in preview mode
            if (!showPreviewIndicator || editMode) return;
            
            var screenPos = BlendSpaceToScreen(previewPosition, rect);
            
            // Don't draw if outside the rect bounds (with some margin for the indicator radius)
            var margin = PreviewIndicatorRadius + 4;
            if (screenPos.x < rect.x - margin || screenPos.x > rect.xMax + margin ||
                screenPos.y < rect.y - margin || screenPos.y > rect.yMax + margin)
            {
                return;
            }
            
            // Draw glow/highlight when dragging
            if (isDraggingPreviewIndicator)
            {
                DrawCircle(screenPos, PreviewIndicatorRadius + 4, PreviewIndicatorColor * 0.5f);
            }
            
            // Draw indicator circle
            DrawCircle(screenPos, PreviewIndicatorRadius, PreviewIndicatorColor);
            
            // Draw white border
            Handles.BeginGUI();
            Handles.color = Color.white;
            Handles.DrawWireDisc(new Vector3(screenPos.x, screenPos.y, 0), Vector3.forward, PreviewIndicatorRadius);
            Handles.EndGUI();
        }
        
        /// <summary>
        /// Checks if the mouse is over the preview indicator.
        /// Returns false in edit mode since indicator is hidden.
        /// </summary>
        protected bool IsMouseOverPreviewIndicator(Rect rect, Vector2 mousePos)
        {
            if (!showPreviewIndicator || editMode) return false;
            var screenPos = BlendSpaceToScreen(previewPosition, rect);
            return IsPointInCircle(mousePos, screenPos, PreviewIndicatorRadius + 4);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Resets the view to default zoom and pan.
        /// </summary>
        public virtual void ResetView()
        {
            zoom = 1f;
            panOffset = Vector2.zero;
        }

        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection()
        {
            if (selectedClipIndex != -1)
            {
                selectedClipIndex = -1;
                RaiseSelectionChanged(-1);
            }
        }
        
        /// <summary>
        /// Starts an external pan operation (for UI Toolkit integration).
        /// Call this from MouseDownEvent when middle-click is detected.
        /// </summary>
        public void StartExternalPan(Vector2 mousePosition)
        {
            externalPanActive = true;
            externalLastMousePos = mousePosition;
            pendingPanDelta = Vector2.zero;
        }
        
        /// <summary>
        /// Updates an external pan operation with new mouse position.
        /// Call this from MouseMoveEvent during pan.
        /// </summary>
        public void UpdateExternalPan(Vector2 mousePosition)
        {
            if (externalPanActive)
            {
                pendingPanDelta += mousePosition - externalLastMousePos;
                externalLastMousePos = mousePosition;
            }
        }
        
        /// <summary>
        /// Ends the external pan operation.
        /// Call this from MouseUpEvent.
        /// </summary>
        public void EndExternalPan()
        {
            externalPanActive = false;
            pendingPanDelta = Vector2.zero;
        }
        
        /// <summary>
        /// Whether an external pan is currently active.
        /// </summary>
        public bool IsExternalPanning => externalPanActive;

        #endregion

        #region Core Draw Loop

        /// <summary>
        /// Core draw method that orchestrates the rendering.
        /// Call this from derived class Draw() after setting up clip data.
        /// </summary>
        protected void DrawCore(Rect rect, SerializedObject serializedObject)
        {
            // Process external pan (from UI Toolkit events that bypass IMGUI)
            if (externalPanActive && pendingPanDelta != Vector2.zero)
            {
                ApplyExternalPanDelta(rect, pendingPanDelta);
                pendingPanDelta = Vector2.zero;
            }
            
            // Draw background
            EditorGUI.DrawRect(rect, BackgroundColor);
            
            // Handle input (including mode button click)
            HandleInput(rect, serializedObject);
            
            // Draw background elements (grid/track/axes)
            DrawBackground(rect);
            
            // Draw clips
            DrawClips(rect);
            
            // Draw preview indicator (on top of clips)
            DrawPreviewIndicator(rect);
            
            // Draw overlay UI (mode button, zoom, help text)
            DrawOverlayUI(rect);
        }
        
        /// <summary>
        /// Draws all overlay UI elements (mode button, zoom indicator, info label).
        /// Note: Help text is intentionally NOT drawn here - use GetHelpText() and draw externally.
        /// </summary>
        private void DrawOverlayUI(Rect rect)
        {
            // Initialize icons if needed (only if mode toggle is shown)
            if (showModeToggle)
            {
                InitializeModeIcons();
                
                // Top-left: Mode toggle button
                DrawModeToggleButton(rect);
            }
            
            // Top-left (after button or at start): Info label (preview state or selection)
            DrawOverlayInfoLabel(rect);
            
            // Top-right: Zoom indicator
            DrawZoomIndicator(rect);
        }
        
        /// <summary>
        /// Initializes the mode button icons (lazy initialization).
        /// </summary>
        private static void InitializeModeIcons()
        {
            if (editModeIcon == null)
            {
                // Use built-in Unity icons
                var editIcon = EditorGUIUtility.IconContent("d_editicon.sml");
                editModeIcon = new GUIContent(editIcon.image, "Edit Mode - Drag clips to reposition");
                
                var previewIcon = EditorGUIUtility.IconContent("d_PlayButton");
                previewModeIcon = new GUIContent(previewIcon.image, "Preview Mode - Drag to set blend position");
            }
            
            if (modeButtonStyle == null)
            {
                modeButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    padding = new RectOffset(2, 2, 2, 2),
                    margin = new RectOffset(0, 0, 0, 0),
                    fixedWidth = ModeButtonSize,
                    fixedHeight = ModeButtonSize
                };
            }
        }
        
        /// <summary>
        /// Draws the edit/preview mode toggle button.
        /// </summary>
        private void DrawModeToggleButton(Rect rect)
        {
            modeButtonRect = new Rect(
                rect.x + OverlayPadding,
                rect.y + OverlayPadding,
                ModeButtonSize,
                ModeButtonSize);
            
            var isHovered = modeButtonRect.Contains(Event.current.mousePosition);
            var bgColor = isHovered ? ModeButtonHoverColor : ModeButtonBgColor;
            var accentColor = editMode ? EditModeAccentColor : PreviewModeAccentColor;
            
            // Draw button background with accent border
            EditorGUI.DrawRect(modeButtonRect, bgColor);
            
            // Draw accent border (2px on left side to indicate mode)
            var borderRect = new Rect(modeButtonRect.x, modeButtonRect.y, 3, modeButtonRect.height);
            EditorGUI.DrawRect(borderRect, accentColor);
            
            // Draw icon
            var icon = editMode ? editModeIcon : previewModeIcon;
            var iconRect = new Rect(
                modeButtonRect.x + 3,
                modeButtonRect.y + 2,
                ModeButtonSize - 5,
                ModeButtonSize - 4);
            
            GUI.color = accentColor;
            GUI.Label(iconRect, icon);
            GUI.color = Color.white;
            
            // Draw mode label next to button
            var modeText = editMode ? "Edit" : "Preview";
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = accentColor },
                fontStyle = FontStyle.Bold
            };
            var labelRect = new Rect(
                modeButtonRect.xMax + 4,
                modeButtonRect.y + 2,
                50,
                16);
            GUI.Label(labelRect, modeText, labelStyle);
        }

        #endregion

        #region Input Handling
        
        // Control ID for hotControl management
        private int blendSpaceControlId;

        /// <summary>
        /// Main input handler that delegates to specific handlers.
        /// </summary>
        protected void HandleInput(Rect rect, SerializedObject serializedObject)
        {
            var e = Event.current;
            var mousePos = e.mousePosition;
            
            // Get a consistent control ID for this editor
            blendSpaceControlId = GUIUtility.GetControlID(FocusType.Passive);
            
            // Track mouse entry for scroll ownership
            bool mouseInRect = rect.Contains(mousePos);
            if (mouseInRect && !wasMouseInRect)
            {
                // Mouse just entered - record the time
                mouseEnteredTime = EditorApplication.timeSinceStartup;
            }
            wasMouseInRect = mouseInRect;
            
            // Allow drag events to continue even if mouse moves outside rect
            var isActiveDrag = isPanning || isDraggingClip || isDraggingPreviewIndicator;
            if (!mouseInRect && e.type != EventType.MouseUp && !(e.type == EventType.MouseDrag && isActiveDrag))
                return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    // Only handle scroll if mouse has been in rect long enough
                    // This prevents scroll hijacking when scrolling from outside
                    double timeSinceEntry = EditorApplication.timeSinceStartup - mouseEnteredTime;
                    if (timeSinceEntry >= ScrollOwnershipDelay)
                    {
                        HandleZoom(rect, e);
                    }
                    // Note: We don't Use() the event if we ignore it, allowing parent to handle it
                    break;
                    
                case EventType.MouseDown:
                    HandleMouseDown(rect, e);
                    break;
                    
                case EventType.MouseDrag:
                    HandleMouseDrag(rect, e, serializedObject);
                    break;
                    
                case EventType.MouseUp:
                    HandleMouseUp();
                    break;
            }
            
            lastMousePos = mousePos;
        }

        /// <summary>
        /// Handles mouse down for selection and pan initiation.
        /// </summary>
        protected virtual void HandleMouseDown(Rect rect, Event e)
        {
            // Check for pan first (middle click or Alt+Left) - works in all modes
            if (e.button == 2 || (e.button == 0 && e.alt))
            {
                isPanning = true;
                GUIUtility.hotControl = blendSpaceControlId; // Claim exclusive input
                e.Use();
                return;
            }
            
            if (e.button == 0) // Left click
            {
                // Check mode button first (highest priority, only if shown)
                if (showModeToggle && modeButtonRect.Contains(e.mousePosition))
                {
                    EditMode = !editMode;
                    e.Use();
                    return;
                }
                
                // Check preview indicator (high priority when in preview mode)
                if (showPreviewIndicator && IsMouseOverPreviewIndicator(rect, e.mousePosition))
                {
                    isDraggingPreviewIndicator = true;
                    GUIUtility.hotControl = blendSpaceControlId; // Claim exclusive input
                    // Clicking the preview indicator switches to blended preview mode
                    SetPreviewClip(-1);
                    e.Use();
                    return;
                }
                
                var clickedIndex = GetClipAtPosition(rect, e.mousePosition);
                if (clickedIndex >= 0)
                {
                    SetSelection(clickedIndex);
                    
                    if (showPreviewIndicator && !editMode)
                    {
                        // In preview mode, clicking a clip selects it for individual preview
                        SetPreviewClip(clickedIndex);
                    }
                    else if (editMode)
                    {
                        // In edit mode, allow dragging clips
                        isDraggingClip = true;
                        GUIUtility.hotControl = blendSpaceControlId; // Claim exclusive input
                    }
                    
                    // Always request repaint after click detection
                    RequestRepaint();
                    e.Use();
                }
                else if (showPreviewIndicator && !editMode)
                {
                    // Click empty space to move preview indicator and return to blended preview
                    PreviewPosition = ScreenToBlendSpace(e.mousePosition, rect);
                    isDraggingPreviewIndicator = true;
                    GUIUtility.hotControl = blendSpaceControlId; // Claim exclusive input
                    SetPreviewClip(-1);
                    e.Use();
                }
                else
                {
                    SetSelection(-1);
                }
            }
        }

        /// <summary>
        /// Handles mouse drag for clip movement and panning.
        /// </summary>
        protected virtual void HandleMouseDrag(Rect rect, Event e, SerializedObject serializedObject)
        {
            if (isDraggingPreviewIndicator && !editMode)
            {
                // Derived classes should override to clamp to their specific bounds (trackRect for 1D, rect for 2D)
                PreviewPosition = ScreenToBlendSpace(e.mousePosition, rect);
                e.Use();
            }
            else if (isDraggingClip && selectedClipIndex >= 0 && editMode)
            {
                HandleClipDrag(rect, e);
                e.Use();
            }
            else if (isPanning)
            {
                HandlePan(e, rect);
                e.Use();
            }
        }

        /// <summary>
        /// Handles mouse up to end dragging/panning.
        /// </summary>
        protected void HandleMouseUp()
        {
            isDraggingClip = false;
            isDraggingPreviewIndicator = false;
            isPanning = false;
            
            // Release hotControl if we had it
            if (GUIUtility.hotControl == blendSpaceControlId)
            {
                GUIUtility.hotControl = 0;
            }
        }

        #endregion

        #region Selection

        /// <summary>
        /// Sets the selected clip index and raises the selection changed event.
        /// </summary>
        protected void SetSelection(int index)
        {
            if (selectedClipIndex == index) return;
            selectedClipIndex = index;
            RaiseSelectionChanged(index);
            RequestRepaint();
        }


        /// <summary>
        /// Sets the preview clip index and raises the clip selected for preview event.
        /// -1 = blended preview, >= 0 = individual clip preview.
        /// </summary>
        protected void SetPreviewClip(int index)
        {
            previewClipIndex = index;
            
            // Clear selection when switching to blended preview
            if (index < 0)
            {
                selectedClipIndex = -1;
            }
            
            OnClipSelectedForPreview?.Invoke(index);
            RequestRepaint();
        }

        /// <summary>
        /// Raises the OnSelectionChanged event.
        /// </summary>
        protected void RaiseSelectionChanged(int index)
        {
            OnSelectionChanged?.Invoke(index);
        }
        
        /// <summary>
        /// Requests an immediate repaint of the editor.
        /// </summary>
        private void RequestRepaint()
        {
            GUI.changed = true;
            OnRepaintRequested?.Invoke();
        }

        /// <summary>
        /// Draws the info label in the overlay (preview state or selection).
        /// </summary>
        protected virtual void DrawOverlayInfoLabel(Rect rect)
        {
            string infoText;
            Color textColor;
            
            if (showPreviewIndicator && !editMode)
            {
                // Preview mode: show what we're previewing
                if (previewClipIndex < 0)
                {
                    infoText = "Blended";
                    textColor = PreviewIndicatorColor; // Orange
                }
                else
                {
                    infoText = GetClipName(previewClipIndex);
                    textColor = new Color(0.4f, 0.8f, 1f); // Blue for individual clip
                }
            }
            else if (selectedClipIndex >= 0)
            {
                // Edit mode or no preview: show selection
                infoText = GetClipName(selectedClipIndex);
                textColor = SelectionColor; // Yellow
            }
            else
            {
                return; // Nothing to show
            }
            
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = textColor }
            };
            
            if (showModeToggle)
            {
                // Position after mode button and label (button + label ~= 75px)
                GUI.Label(new Rect(rect.x + 80, rect.y + OverlayPadding + 2, 150, 16), $"| {infoText}", style);
            }
            else
            {
                // Position at start (no mode button)
                GUI.Label(new Rect(rect.x + OverlayPadding, rect.y + OverlayPadding, 200, 16), infoText, style);
            }
        }

        #endregion

        #region Drawing Utilities

        /// <summary>
        /// Draws a filled circle at the specified position.
        /// </summary>
        protected void DrawCircle(Vector2 center, float radius, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, radius);
            Handles.EndGUI();
        }

        /// <summary>
        /// Draws a label with a dark background for readability.
        /// </summary>
        protected void DrawLabelWithBackground(Rect rect, string text, GUIStyle style = null)
        {
            style ??= EditorStyles.miniLabel;
            EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y, rect.width + 4, rect.height), 
                new Color(0, 0, 0, 0.7f));
            EditorGUI.LabelField(rect, text, style);
        }

        /// <summary>
        /// Draws the zoom indicator in the top-right corner.
        /// </summary>
        protected void DrawZoomIndicator(Rect rect)
        {
            var zoomText = $"{zoom:F1}x";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            
            // Draw background for readability
            var zoomRect = new Rect(rect.xMax - 35, rect.y + OverlayPadding, 30, 16);
            EditorGUI.DrawRect(new Rect(zoomRect.x - 2, zoomRect.y - 1, zoomRect.width + 4, zoomRect.height + 2), 
                new Color(0, 0, 0, 0.5f));
            GUI.Label(zoomRect, zoomText, style);
        }

        /// <summary>
        /// Draws a clip circle with optional selection ring and label.
        /// </summary>
        protected void DrawClipCircle(Vector2 screenPos, int clipIndex, string label, bool showValue = false, string valueText = null)
        {
            var isSelected = clipIndex == selectedClipIndex;
            var color = GetClipColor(clipIndex);
            
            // Draw selection ring
            if (isSelected)
            {
                DrawCircle(screenPos, ClipCircleRadius + 3, SelectionColor);
            }
            
            // Draw clip circle
            DrawCircle(screenPos, ClipCircleRadius, color);
            
            // Draw clip name above
            var labelRect = GetLabelRectAbove(screenPos, label);
            DrawLabelWithBackground(labelRect, label);
            
            // Draw value below for selected clip
            if (isSelected && showValue && !string.IsNullOrEmpty(valueText))
            {
                var valueRect = GetLabelRectBelow(screenPos, valueText);
                var selectedStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = SelectionColor }
                };
                GUI.Label(valueRect, valueText, selectedStyle);
            }
        }

        #endregion

        #region Geometry Utilities

        /// <summary>
        /// Gets the color for a clip based on its index.
        /// </summary>
        protected static Color GetClipColor(int index)
        {
            return ClipColors[index % ClipColors.Length];
        }

        /// <summary>
        /// Generates an array of evenly distributed colors.
        /// </summary>
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

        /// <summary>
        /// Calculates label rect centered above a point.
        /// </summary>
        protected Rect GetLabelRectAbove(Vector2 position, string text, float verticalOffset = 0)
        {
            var content = new GUIContent(text);
            var size = EditorStyles.miniLabel.CalcSize(content);
            return new Rect(
                position.x - size.x / 2,
                position.y - ClipCircleRadius - size.y - 2 - verticalOffset,
                size.x,
                size.y);
        }

        /// <summary>
        /// Calculates label rect centered below a point.
        /// </summary>
        protected Rect GetLabelRectBelow(Vector2 position, string text, float verticalOffset = 0)
        {
            var content = new GUIContent(text);
            var size = EditorStyles.miniLabel.CalcSize(content);
            return new Rect(
                position.x - size.x / 2,
                position.y + ClipCircleRadius + 2 + verticalOffset,
                size.x,
                size.y);
        }

        /// <summary>
        /// Checks if a point is within a circle.
        /// </summary>
        protected bool IsPointInCircle(Vector2 point, Vector2 center, float radius)
        {
            return Vector2.Distance(point, center) <= radius;
        }

        /// <summary>
        /// Applies zoom delta with clamping.
        /// </summary>
        protected float ApplyZoomDelta(float delta)
        {
            var newZoom = Mathf.Clamp(zoom + delta, MinZoom, MaxZoom);
            zoom = newZoom;
            return newZoom;
        }

        /// <summary>
        /// Standard zoom delta from scroll wheel event.
        /// </summary>
        protected float GetZoomDelta(Event e)
        {
            return -e.delta.y * 0.05f;
        }

        #endregion
    }
}
