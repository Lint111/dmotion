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
        
        // Visual settings
        protected const float MinZoom = 0.5f;
        protected const float MaxZoom = 3f;
        protected const float ClipCircleRadius = 10f;
        
        // Colors
        protected static readonly Color GridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        protected static readonly Color AxisColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color TickColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        protected static readonly Color MajorTickColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color SelectionColor = new Color(1f, 0.8f, 0f, 1f);
        protected static readonly Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        
        // Cached clip colors (generated from index)
        private static readonly Color[] ClipColors = GenerateClipColors(16);
        
        /// <summary>
        /// Currently selected clip index, or -1 if none selected.
        /// </summary>
        public int SelectedClipIndex => selectedClipIndex;
        
        /// <summary>
        /// Event fired when selection changes.
        /// </summary>
        public event Action<int> OnSelectionChanged;

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
        /// Sets the selected clip index and raises the selection changed event.
        /// </summary>
        protected void SetSelection(int index)
        {
            if (selectedClipIndex != index)
            {
                selectedClipIndex = index;
                RaiseSelectionChanged(index);
            }
        }

        /// <summary>
        /// Raises the OnSelectionChanged event.
        /// </summary>
        protected void RaiseSelectionChanged(int index)
        {
            OnSelectionChanged?.Invoke(index);
        }

        /// <summary>
        /// Handles mouse up event - ends dragging and panning.
        /// </summary>
        protected void HandleMouseUp()
        {
            isDraggingClip = false;
            isPanning = false;
        }

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
            GUI.Label(rect, text, style);
        }

        /// <summary>
        /// Draws the zoom indicator in the bottom-right corner.
        /// </summary>
        protected void DrawZoomIndicator(Rect rect, string helpText = "Scroll: Zoom | MMB: Pan | Shift+Drag: Snap")
        {
            var zoomText = $"Zoom: {zoom:F1}x";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerRight
            };
            GUI.Label(new Rect(rect.xMax - 60, rect.yMax - 18, 55, 16), zoomText, style);
            
            // Instructions
            GUI.Label(new Rect(rect.x + 5, rect.yMax - 18, 250, 16), helpText, EditorStyles.miniLabel);
        }

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
    }
}
