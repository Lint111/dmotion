using System;
using DMotion.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// UIToolkit-based 1D blend space visual editor for LinearBlendStateAsset.
    /// Displays clips as colored circles on a horizontal track with zoom/pan functionality.
    /// 
    /// Uses pure UIElements with Painter2D for consistent event handling.
    /// </summary>
    [UxmlElement]
    internal partial class BlendSpace1DVisualElement : BlendSpaceVisualElement
    {
        #region Constants
        
        private const float TickSpacing = 0.25f;
        private const float TrackPadding = 8f;
        private const float DefaultRangePadding = 0.2f;
        private const float TrackLineHeight = 4f;
        
        private static readonly Color TrackColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        
        #endregion
        
        #region State
        
        private LinearBlendStateAsset targetState;
        private ClipWithThreshold[] clips;
        
        // Track geometry (calculated on layout)
        private Rect trackRect;
        private float lineY;
        
        // Stable base range (doesn't change during drag)
        private float baseMin;
        private float baseMax;
        
        // Clip label elements (pooled for performance)
        private readonly System.Collections.Generic.List<Label> clipLabels = new();
        
        #endregion
        
        #region Events
        
        /// <summary>Event fired when a clip threshold is changed via dragging.</summary>
        public event Action<int, float> OnClipThresholdChanged;
        
        #endregion
        
        #region Properties
        
        public override string EditorTitle => "Blend Track";
        
        /// <summary>The target state asset being edited.</summary>
        public LinearBlendStateAsset TargetState => targetState;
        
        #endregion
        
        #region Constructor
        
        public BlendSpace1DVisualElement()
        {
            AddToClassList("blend-space-1d");
            
            // Override geometry changed to recalculate track
            RegisterCallback<GeometryChangedEvent>(OnGeometryChangedUpdate);
        }
        
        private void OnGeometryChangedUpdate(GeometryChangedEvent evt)
        {
            UpdateTrackGeometry();
        }
        
        /// <summary>Ensures we have enough label elements for all clips.</summary>
        private void EnsureClipLabels(int count)
        {
            // Add labels if needed (to clipLabelContainer so they render behind HUD)
            while (clipLabels.Count < count)
            {
                var label = new Label();
                label.AddToClassList("blend-space__clip-label");
                label.pickingMode = PickingMode.Ignore;
                clipLabelContainer.Add(label);
                clipLabels.Add(label);
            }
            
            // Hide extra labels
            for (int i = 0; i < clipLabels.Count; i++)
            {
                clipLabels[i].style.display = i < count ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
        
        /// <summary>Updates clip label positions and text.</summary>
        private void UpdateClipLabels()
        {
            if (clips == null || clips.Length == 0)
            {
                EnsureClipLabels(0);
                return;
            }
            
            EnsureClipLabels(clips.Length);
            
            // Sort clips by threshold for proper staggering
            var sortedIndices = new int[clips.Length];
            for (var i = 0; i < clips.Length; i++) sortedIndices[i] = i;
            Array.Sort(sortedIndices, (a, b) => clips[a].Threshold.CompareTo(clips[b].Threshold));
            
            float lastLabelX = float.MinValue;
            bool alternateBelow = true;
            
            for (int idx = 0; idx < sortedIndices.Length; idx++)
            {
                var i = sortedIndices[idx];
                var clip = clips[i];
                var label = clipLabels[i];
                var screenX = ThresholdToScreen(clip.Threshold);
                
                // Hide if outside visible area
                if (screenX < trackRect.x - 20 || screenX > trackRect.xMax + 20)
                {
                    label.style.display = DisplayStyle.None;
                    continue;
                }
                
                label.style.display = DisplayStyle.Flex;
                label.text = GetClipName(i);
                
                var lineHeight = editMode ? 20 : 12;
                
                // Stagger labels to avoid overlap
                var labelOffset = 0f;
                if (screenX - lastLabelX < 50)
                {
                    labelOffset = alternateBelow ? 0 : 14;
                    alternateBelow = !alternateBelow;
                }
                else
                {
                    alternateBelow = true;
                }
                lastLabelX = screenX;
                
                float labelY;
                if (editMode)
                {
                    labelY = lineY + lineHeight + ClipHandleRadius + 4 + labelOffset;
                }
                else
                {
                    labelY = lineY + lineHeight + 6 + labelOffset;
                }
                
                // Position label centered on clip
                label.style.left = screenX;
                label.style.top = labelY;
                label.style.translate = new Translate(Length.Percent(-50), 0);
                
                // Highlight selected using CSS class
                var isSelected = i == selectedClipIndex;
                label.EnableInClassList("blend-space__clip-label--selected", isSelected);
            }
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>Sets the target state for this editor.</summary>
        public void SetTarget(LinearBlendStateAsset state)
        {
            targetState = state;
            clips = state?.BlendClips;
            UpdateBaseRange();
            UpdateTrackGeometry();
            MarkDirtyRepaint();
        }
        
        /// <summary>Sets the clip data directly (for preview without asset reference).</summary>
        public void SetClips(ClipWithThreshold[] clipData)
        {
            clips = clipData;
            UpdateBaseRange();
            UpdateTrackGeometry();
            MarkDirtyRepaint();
        }
        
        /// <summary>Updates clip data and refreshes the display.</summary>
        public void RefreshClips()
        {
            if (targetState != null)
            {
                clips = targetState.BlendClips;
            }
            if (!isDraggingClip && !isPanning)
            {
                UpdateBaseRange();
            }
            MarkDirtyRepaint();
        }
        
        /// <summary>Resets the view to default zoom and pan.</summary>
        public override void ResetView()
        {
            base.ResetView();
            UpdateBaseRange();
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
            // Draw track line
            DrawFilledRect(painter, new Rect(trackRect.x, lineY - 2, trackRect.width, TrackLineHeight), TrackColor);
            
            // Draw ticks
            DrawTicks(painter);
        }
        
        protected override void DrawClips(Painter2D painter, Rect rect)
        {
            if (clips == null)
            {
                ScheduleOnce("clip-labels", () => EnsureClipLabels(0));
                return;
            }
            
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold);
                
                // Skip if outside visible area
                if (screenX < trackRect.x - 10 || screenX > trackRect.xMax + 10)
                    continue;
                
                var clipColor = GetClipColor(i);
                var isSelected = i == selectedClipIndex;
                
                // Draw vertical line below track
                var lineHeight = editMode ? 20 : 12;
                DrawFilledRect(painter, new Rect(screenX - 1, lineY - 2, 2, lineHeight + 4), clipColor);
                
                if (editMode)
                {
                    // Edit mode: show draggable handle below track
                    var circleCenter = new Vector2(screenX, lineY + lineHeight + ClipHandleRadius);
                    
                    // Selection ring
                    if (isSelected)
                    {
                        DrawFilledCircle(painter, circleCenter, ClipHandleRadius + 3, SelectionColor);
                    }
                    
                    // Handle circle
                    DrawFilledCircle(painter, circleCenter, ClipHandleRadius, clipColor);
                }
                else
                {
                    // Preview mode: small tick below track
                    DrawFilledRect(painter, new Rect(screenX - 3, lineY + lineHeight, 6, 3), clipColor);
                }
            }
            
            // Schedule clip label updates (debounced)
            ScheduleOnce("clip-labels", UpdateClipLabels);
        }
        
        protected override void DrawPreviewIndicator(Painter2D painter, Rect rect)
        {
            if (!showPreviewIndicator || editMode) return;
            
            var screenPos = BlendSpaceToScreen(previewPosition, rect);
            
            // Stop at trackRect edges
            if (screenPos.x < trackRect.x || screenPos.x > trackRect.xMax)
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
            DrawCircleOutline(painter, screenPos, PreviewIndicatorRadius, Color.white, 1f);
        }
        
        protected override Vector2 GetClipScreenPosition(int index, Rect rect)
        {
            if (clips == null || index < 0 || index >= clips.Length)
                return Vector2.zero;
            
            var screenX = ThresholdToScreen(clips[index].Threshold);
            var lineHeight = editMode ? 20 : 12;
            
            if (editMode)
            {
                return new Vector2(screenX, lineY + lineHeight + ClipHandleRadius);
            }
            else
            {
                return new Vector2(screenX, lineY + lineHeight);
            }
        }
        
        protected override int GetClipAtPosition(Rect rect, Vector2 mousePos)
        {
            if (clips == null) return -1;
            
            for (var i = clips.Length - 1; i >= 0; i--)
            {
                var screenPos = GetClipScreenPosition(i, rect);
                var lineHeight = editMode ? 20 : 12;
                
                if (editMode)
                {
                    // Check handle circle
                    if (IsPointInCircle(mousePos, screenPos, ClipHandleRadius + 4))
                        return i;
                }
                else
                {
                    // Check line area (wider hit zone)
                    var screenX = screenPos.x;
                    var lineTop = lineY - 2;
                    var lineBottom = lineY + lineHeight + 5;
                    if (mousePos.x >= screenX - 12 && mousePos.x <= screenX + 12 &&
                        mousePos.y >= lineTop && mousePos.y <= lineBottom)
                        return i;
                }
                
                // Check label
                var labelRect = GetClipLabelRect(i, rect);
                if (labelRect.Contains(mousePos))
                    return i;
            }
            return -1;
        }
        
        protected override Rect GetClipLabelRect(int index, Rect rect)
        {
            var screenPos = GetClipScreenPosition(index, rect);
            var clipName = GetClipName(index);
            var labelWidth = clipName.Length * 7f + 8f;
            var labelHeight = 14f;
            
            if (editMode)
            {
                var labelY = screenPos.y + ClipHandleRadius + 2;
                return new Rect(screenPos.x - labelWidth / 2 - 2, labelY, labelWidth + 4, labelHeight + 2);
            }
            else
            {
                var labelY = screenPos.y + 5;
                return new Rect(screenPos.x - labelWidth / 2 - 2, labelY, labelWidth + 4, labelHeight + 12);
            }
        }
        
        protected override void HandleClipDrag(Vector2 mousePos, Rect rect)
        {
            if (selectedClipIndex < 0 || selectedClipIndex >= clips?.Length) return;
            
            var clampedX = Mathf.Clamp(mousePos.x, trackRect.x, trackRect.xMax);
            var threshold = ScreenToThreshold(clampedX);
            
            OnClipThresholdChanged?.Invoke(selectedClipIndex, threshold);
            MarkDirtyRepaint();
        }
        
        protected override void HandlePan(Vector2 delta, Rect rect)
        {
            var range = (baseMax - baseMin) / zoom;
            panOffset.x -= delta.x / trackRect.width * range;
        }
        
        protected override void HandleZoom(float delta, Vector2 mousePos, Rect rect)
        {
            var mouseThreshold = ScreenToThreshold(mousePos.x);
            ApplyZoomDelta(delta);
            var newMouseThreshold = ScreenToThreshold(mousePos.x);
            panOffset.x += (mouseThreshold - newMouseThreshold);
        }
        
        protected override Vector2 BlendSpaceToScreen(Vector2 blendPos, Rect rect)
        {
            return new Vector2(ThresholdToScreen(blendPos.x), lineY);
        }
        
        protected override Vector2 ScreenToBlendSpace(Vector2 screenPos, Rect rect)
        {
            return new Vector2(ScreenToThreshold(screenPos.x), 0);
        }
        
        protected override Vector2 ClampPreviewPosition(Vector2 position)
        {
            if (clips == null || clips.Length == 0)
                return position;
            
            float minThreshold = float.MaxValue, maxThreshold = float.MinValue;
            for (int i = 0; i < clips.Length; i++)
            {
                minThreshold = Mathf.Min(minThreshold, clips[i].Threshold);
                maxThreshold = Mathf.Max(maxThreshold, clips[i].Threshold);
            }
            
            return new Vector2(Mathf.Clamp(position.x, minThreshold, maxThreshold), 0);
        }
        
        protected override void GetBlendSpaceBounds(out Vector2 min, out Vector2 max)
        {
            if (clips == null || clips.Length == 0)
            {
                min = Vector2.zero;
                max = Vector2.one;
                return;
            }
            
            float minThreshold = float.MaxValue, maxThreshold = float.MinValue;
            for (int i = 0; i < clips.Length; i++)
            {
                minThreshold = Mathf.Min(minThreshold, clips[i].Threshold);
                maxThreshold = Mathf.Max(maxThreshold, clips[i].Threshold);
            }
            
            min = new Vector2(minThreshold, 0);
            max = new Vector2(maxThreshold, 0);
        }
        
        #endregion
        
        #region Drawing
        
        private void UpdateTrackGeometry()
        {
            var rect = contentRect;
            if (rect.width < 10 || rect.height < 10) return;
            
            var topPadding = 25f;
            var bottomPadding = editMode ? 55f : 45f;
            
            trackRect = new Rect(
                TrackPadding,
                topPadding,
                rect.width - TrackPadding * 2,
                rect.height - topPadding - bottomPadding);
            lineY = trackRect.y + trackRect.height / 2;
        }
        
        private void UpdateBaseRange()
        {
            if (clips == null || clips.Length == 0)
            {
                baseMin = 0f;
                baseMax = 1f;
            }
            else
            {
                baseMin = float.MaxValue;
                baseMax = float.MinValue;
                foreach (var clip in clips)
                {
                    baseMin = Mathf.Min(baseMin, clip.Threshold);
                    baseMax = Mathf.Max(baseMax, clip.Threshold);
                }
                
                var range = baseMax - baseMin;
                if (range < 0.1f)
                {
                    var center = (baseMin + baseMax) / 2f;
                    baseMin = center - 0.5f;
                    baseMax = center + 0.5f;
                    range = 1f;
                }
                
                baseMin -= range * DefaultRangePadding;
                baseMax += range * DefaultRangePadding;
            }
        }
        
        private void DrawTicks(Painter2D painter)
        {
            if (trackRect.width <= 0) return;
            
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var visibleMax = visibleMin + range;
            
            var tickStep = TickSpacing;
            while (tickStep * trackRect.width / range < 30) tickStep *= 2;
            
            var startTick = Mathf.Floor(visibleMin / tickStep) * tickStep;
            
            for (var t = startTick; t <= visibleMax + tickStep; t += tickStep)
            {
                var screenX = ThresholdToScreen(t);
                
                if (screenX < trackRect.x || screenX > trackRect.xMax)
                    continue;
                
                var isMajor = Mathf.Abs(t % (tickStep * 4)) < 0.001f || Mathf.Abs(t) < 0.001f;
                var tickHeight = isMajor ? 12 : 6;
                var tickColor = isMajor ? MajorTickColor : TickColor;
                
                DrawFilledRect(painter, new Rect(screenX - 1, lineY - tickHeight, 2, tickHeight * 2), tickColor);
            }
        }
        
        private float ThresholdToScreen(float threshold)
        {
            if (trackRect.width <= 0) return trackRect.x;
            
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var normalized = (threshold - visibleMin) / range;
            return trackRect.x + normalized * trackRect.width;
        }
        
        private float ScreenToThreshold(float screenX)
        {
            if (trackRect.width <= 0) return baseMin;
            
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var normalized = (screenX - trackRect.x) / trackRect.width;
            return visibleMin + normalized * range;
        }
        
        #endregion
    }
}
