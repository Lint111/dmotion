using System;
using DMotion.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// UIToolkit-based 2D blend space visual editor for Directional2DBlendStateAsset.
    /// Displays clips as colored circles on a 2D grid with zoom/pan functionality.
    /// 
    /// Uses pure UIElements with Painter2D for consistent event handling.
    /// </summary>
    [UxmlElement]
    internal partial class BlendSpace2DVisualElement : BlendSpaceVisualElement
    {
        #region Constants
        
        private const float GridSize = 0.5f;
        private const float PixelsPerUnit = 100f;
        
        #endregion
        
        #region State
        
        private Directional2DBlendStateAsset targetState;
        private Directional2DClipWithPosition[] clips;
        
        #endregion
        
        #region Events
        
        /// <summary>Event fired when a clip position is changed via dragging.</summary>
        public event Action<int, Vector2> OnClipPositionChanged;
        
        #endregion
        
        #region Properties
        
        public override string EditorTitle => "Blend Space 2D";
        
        /// <summary>The target state asset being edited.</summary>
        public Directional2DBlendStateAsset TargetState => targetState;
        
        #endregion
        
        #region Constructor
        
        public BlendSpace2DVisualElement()
        {
            AddToClassList("blend-space-2d");
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>Sets the target state for this editor.</summary>
        public void SetTarget(Directional2DBlendStateAsset state)
        {
            targetState = state;
            clips = state?.BlendClips;
            MarkDirtyRepaint();
        }
        
        /// <summary>Sets the clip data directly (for preview without asset reference).</summary>
        public void SetClips(Directional2DClipWithPosition[] clipData)
        {
            clips = clipData;
            MarkDirtyRepaint();
        }
        
        /// <summary>Updates clip data and refreshes the display.</summary>
        public void RefreshClips()
        {
            if (targetState != null)
            {
                clips = targetState.BlendClips;
            }
            MarkDirtyRepaint();
        }
        
        #endregion
        
        #region Abstract Implementations
        
        protected override int GetClipCount() => clips?.Length ?? 0;
        
        protected override string GetClipName(int index)
        {
            if (clips == null || index < 0 || index >= clips.Length) return "None";
            return clips[index].Clip != null ? clips[index].Clip.name : $"Clip {index}";
        }
        
        protected override void DrawBackground(Painter2D painter, Rect rect)
        {
            DrawGrid(painter, rect);
            DrawAxes(painter, rect);
        }
        
        protected override void DrawClips(Painter2D painter, Rect rect)
        {
            if (clips == null) return;
            
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                var screenPos = BlendSpaceToScreen(clip.Position, rect);
                
                // Skip if outside visible area
                if (screenPos.x < -ClipCircleRadius || screenPos.x > rect.width + ClipCircleRadius ||
                    screenPos.y < -ClipCircleRadius || screenPos.y > rect.height + ClipCircleRadius)
                {
                    continue;
                }
                
                DrawClipCircle(painter, screenPos, i);
                DrawClipLabel(painter, screenPos, i, rect);
            }
        }
        
        protected override Vector2 GetClipScreenPosition(int index, Rect rect)
        {
            if (clips == null || index < 0 || index >= clips.Length)
                return Vector2.zero;
            
            return BlendSpaceToScreen(clips[index].Position, rect);
        }
        
        protected override void HandleClipDrag(Vector2 mousePos, Rect rect)
        {
            if (selectedClipIndex < 0 || selectedClipIndex >= clips?.Length) return;
            
            var clampedMousePos = ClampMouseToRect(mousePos, rect);
            var blendPos = ScreenToBlendSpace(clampedMousePos, rect);
            
            // Snap to grid if shift is held (check keyboard modifiers)
            // Note: In UIToolkit, we'd need to track modifier keys separately
            // For now, skip snapping in the UIToolkit version or add a separate mechanism
            
            OnClipPositionChanged?.Invoke(selectedClipIndex, blendPos);
            MarkDirtyRepaint();
        }
        
        protected override void HandlePan(Vector2 delta, Rect rect)
        {
            // X: subtract to follow mouse (drag right = view moves right)
            // Y: add because screen Y is inverted (drag down = view moves down)
            panOffset.x -= delta.x / (PixelsPerUnit * zoom);
            panOffset.y += delta.y / (PixelsPerUnit * zoom);
        }
        
        protected override void HandleZoom(float delta, Vector2 mousePos, Rect rect)
        {
            var mouseBlendPos = ScreenToBlendSpace(mousePos, rect);
            ApplyZoomDelta(delta);
            var newMouseBlendPos = ScreenToBlendSpace(mousePos, rect);
            panOffset += (mouseBlendPos - newMouseBlendPos) * zoom;
        }
        
        protected override Vector2 BlendSpaceToScreen(Vector2 blendPos, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            return new Vector2(
                center.x + blendPos.x * PixelsPerUnit * zoom,
                center.y - blendPos.y * PixelsPerUnit * zoom);
        }
        
        protected override Vector2 ScreenToBlendSpace(Vector2 screenPos, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            return new Vector2(
                (screenPos.x - center.x) / (PixelsPerUnit * zoom),
                -(screenPos.y - center.y) / (PixelsPerUnit * zoom));
        }
        
        protected override Vector2 ClampPreviewPosition(Vector2 position)
        {
            if (clips == null || clips.Length == 0)
                return position;
            
            Vector2 minBounds = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maxBounds = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < clips.Length; i++)
            {
                var pos = clips[i].Position;
                minBounds = Vector2.Min(minBounds, pos);
                maxBounds = Vector2.Max(maxBounds, pos);
            }
            
            return new Vector2(
                Mathf.Clamp(position.x, minBounds.x, maxBounds.x),
                Mathf.Clamp(position.y, minBounds.y, maxBounds.y));
        }
        
        protected override void GetBlendSpaceBounds(out Vector2 min, out Vector2 max)
        {
            if (clips == null || clips.Length == 0)
            {
                min = Vector2.zero;
                max = Vector2.one;
                return;
            }
            
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < clips.Length; i++)
            {
                var pos = clips[i].Position;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
            }
        }
        
        #endregion
        
        #region Drawing
        
        private Vector2 GetBlendSpaceCenter(Rect rect)
        {
            return new Vector2(
                rect.width / 2 - panOffset.x * PixelsPerUnit * zoom,
                rect.height / 2 + panOffset.y * PixelsPerUnit * zoom);
        }
        
        private void DrawGrid(Painter2D painter, Rect rect)
        {
            var gridSpacing = GridSize * PixelsPerUnit * zoom;
            var center = GetBlendSpaceCenter(rect);
            
            var minX = Mathf.Floor(-center.x / gridSpacing) * gridSpacing;
            var maxX = Mathf.Ceil((rect.width - center.x) / gridSpacing) * gridSpacing;
            var minY = Mathf.Floor(-center.y / gridSpacing) * gridSpacing;
            var maxY = Mathf.Ceil((rect.height - center.y) / gridSpacing) * gridSpacing;
            
            painter.strokeColor = GridColor;
            painter.lineWidth = 1f;
            
            // Vertical lines
            for (var x = minX; x <= maxX; x += gridSpacing)
            {
                var screenX = center.x + x;
                if (screenX >= 0 && screenX <= rect.width)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(screenX, 0));
                    painter.LineTo(new Vector2(screenX, rect.height));
                    painter.Stroke();
                }
            }
            
            // Horizontal lines
            for (var y = minY; y <= maxY; y += gridSpacing)
            {
                var screenY = center.y + y;
                if (screenY >= 0 && screenY <= rect.height)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(0, screenY));
                    painter.LineTo(new Vector2(rect.width, screenY));
                    painter.Stroke();
                }
            }
        }
        
        private void DrawAxes(Painter2D painter, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            
            painter.strokeColor = AxisColor;
            painter.lineWidth = 1.5f;
            
            // X axis (horizontal)
            if (center.y >= 0 && center.y <= rect.height)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, center.y));
                painter.LineTo(new Vector2(rect.width, center.y));
                painter.Stroke();
            }
            
            // Y axis (vertical)
            if (center.x >= 0 && center.x <= rect.width)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(center.x, 0));
                painter.LineTo(new Vector2(center.x, rect.height));
                painter.Stroke();
            }
        }
        
        private void DrawClipLabel(Painter2D painter, Vector2 screenPos, int clipIndex, Rect rect)
        {
            var clipName = GetClipName(clipIndex);
            var isSelected = clipIndex == selectedClipIndex;
            
            // Calculate label position (above clip)
            var labelWidth = clipName.Length * 7f + 8f;
            var labelHeight = 14f;
            var labelX = screenPos.x - labelWidth / 2;
            var labelY = screenPos.y - ClipCircleRadius - labelHeight - 4;
            
            // Draw label background
            var labelRect = new Rect(labelX - 2, labelY, labelWidth + 4, labelHeight + 2);
            DrawFilledRect(painter, labelRect, LabelBackgroundColor);
            
            // Note: Painter2D doesn't support text. Labels would need child Label elements
            // or we accept that clip names won't be drawn in the pure Painter2D version.
            // The clip colors and selection rings provide visual identification.
            
            // Draw value below selected clip
            if (isSelected && clips != null && clipIndex < clips.Length)
            {
                var clip = clips[clipIndex];
                var valueText = $"({clip.Position.x:F2}, {clip.Position.y:F2})";
                var valueWidth = valueText.Length * 6f + 4f;
                var valueX = screenPos.x - valueWidth / 2;
                var valueY = screenPos.y + ClipCircleRadius + 4;
                
                var valueRect = new Rect(valueX - 2, valueY, valueWidth + 4, 14);
                DrawFilledRect(painter, valueRect, LabelBackgroundColor);
            }
        }
        
        #endregion
    }
}
