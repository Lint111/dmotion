using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Visual 1D blend space editor for LinearBlendStateAsset.
    /// Displays clips as colored circles on a horizontal line with zoom/pan functionality.
    /// </summary>
    internal class BlendSpace1DVisualEditor : BlendSpaceVisualEditorBase
    {
        // Target state
        private LinearBlendStateAsset targetState;
        
        // Current clip data (set during Draw)
        private ClipWithThreshold[] clips;
        private SerializedObject serializedObject;
        private SerializedProperty clipsProperty;
        private Rect trackRect;
        private float lineY;
        
        // Stable base range (doesn't change during drag)
        private float baseMin;
        private float baseMax;
        private bool hasInitializedRange;
        
        // 1D-specific visual settings
        private const float TickSpacing = 0.25f;
        private const float TrackHeight = 60f;
        private const float TrackPadding = 8f; // Minimal padding - track line extends nearly to edges
        private const float DefaultRangePadding = 0.2f;
        
        private static readonly Color TrackColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        
        /// <summary>
        /// Event fired when a clip threshold is changed via dragging.
        /// </summary>
        public event Action<int, float> OnClipThresholdChanged;

        #region Abstract Implementation

        public override string EditorTitle => "Blend Track";
        public override UnityEngine.Object Target => targetState;

        /// <summary>
        /// Sets the target state for this editor.
        /// </summary>
        public void SetTarget(LinearBlendStateAsset state)
        {
            targetState = state;
        }

        /// <inheritdoc/>
        public override void Draw(Rect rect, SerializedObject serializedObject)
        {
            if (targetState == null) return;
            Draw(rect, targetState.BlendClips, serializedObject);
        }

        /// <inheritdoc/>
        public override bool DrawSelectedClipFields(SerializedObject serializedObject)
        {
            if (targetState == null) return false;
            var clipsProperty = serializedObject.FindProperty("BlendClips");
            return DrawSelectedClipFields(targetState.BlendClips, clipsProperty);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Draws the 1D blend space editor.
        /// </summary>
        public void Draw(Rect rect, ClipWithThreshold[] clips, SerializedObject serializedObject)
        {
            if (clips == null) return;
            
            // Store references for use in abstract method implementations
            this.clips = clips;
            this.serializedObject = serializedObject;
            
            // Initialize or update base range only when not dragging
            if (!hasInitializedRange || (!isDraggingClip && !isPanning))
            {
                UpdateBaseRange(clips);
            }
            
            // Calculate track rect (positioned in upper third to leave room below for handles/labels)
            var topPadding = 25f; // Room for overlay UI
            var bottomPadding = editMode ? 55f : 45f; // Room for handles and labels below
            trackRect = new Rect(
                rect.x + TrackPadding,
                rect.y + topPadding,
                rect.width - TrackPadding * 2,
                rect.height - topPadding - bottomPadding);
            lineY = trackRect.y + trackRect.height / 2;
            
            // Use base class core draw loop
            DrawCore(rect, serializedObject);
        }

        /// <summary>
        /// Draws threshold edit field for the selected clip.
        /// </summary>
        public bool DrawSelectedClipFields(ClipWithThreshold[] clips, SerializedProperty clipsProperty)
        {
            if (selectedClipIndex < 0 || selectedClipIndex >= clips.Length)
            {
                EditorGUILayout.HelpBox("Click a clip on the track to select it.", MessageType.Info);
                return false;
            }

            var clip = clips[selectedClipIndex];
            var clipProperty = clipsProperty.GetArrayElementAtIndex(selectedClipIndex);
            var thresholdProperty = clipProperty.FindPropertyRelative("Threshold");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header with color swatch
            EditorGUILayout.BeginHorizontal();
            var colorRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(colorRect, GetClipColor(selectedClipIndex));
            EditorGUILayout.LabelField($"Clip {selectedClipIndex}: {GetClipName(selectedClipIndex)}", 
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            
            // Threshold field
            EditorGUI.BeginChangeCheck();
            var newThreshold = EditorGUILayout.FloatField("Threshold", clip.Threshold);
            
            if (EditorGUI.EndChangeCheck())
            {
                thresholdProperty.floatValue = newThreshold;
                return true;
            }
            
            EditorGUILayout.EndVertical();
            return false;
        }

        /// <inheritdoc/>
        public override void ResetView()
        {
            base.ResetView();
            hasInitializedRange = false;
        }

        #endregion

        #region Abstract Method Implementations

        protected override int GetClipCount() => clips?.Length ?? 0;

        protected override string GetClipName(int index)
        {
            if (clips == null || index < 0 || index >= clips.Length) return "None";
            return clips[index].Clip != null ? clips[index].Clip.name : $"Clip {index}";
        }

        protected override void DrawBackground(Rect rect)
        {
            // Draw track line - extends to full trackRect width
            EditorGUI.DrawRect(new Rect(trackRect.x, lineY - 2, trackRect.width, 4), TrackColor);
            
            // Draw ticks
            DrawTicks();
        }
        
        protected override void DrawPreviewIndicator(Rect rect)
        {
            // Only show in preview mode
            if (!showPreviewIndicator || editMode) return;
            
            var screenPos = BlendSpaceToScreen(previewPosition, rect);
            
            // Use same bounds as clips - stop when center reaches trackRect edge
            if (screenPos.x < trackRect.x || screenPos.x > trackRect.xMax)
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

        protected override void DrawClips(Rect rect)
        {
            if (clips == null) return;
            
            // Sort clips by threshold for proper z-ordering and label staggering
            var sortedIndices = new int[clips.Length];
            for (var i = 0; i < clips.Length; i++) sortedIndices[i] = i;
            Array.Sort(sortedIndices, (a, b) => clips[a].Threshold.CompareTo(clips[b].Threshold));
            
            // Track last label position to avoid overlap
            float lastLabelX = float.MinValue;
            bool alternateBelow = true;
            
            for (int idx = 0; idx < sortedIndices.Length; idx++)
            {
                var i = sortedIndices[idx];
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold);
                
                // Stop when center reaches trackRect edge (same as preview indicator)
                if (screenX < trackRect.x || screenX > trackRect.xMax)
                    continue;
                
                var clipColor = GetClipColor(i);
                var isSelected = i == selectedClipIndex;
                
                // Draw vertical line BELOW track (to keep within bounds)
                var lineHeight = editMode ? 20 : 12;
                EditorGUI.DrawRect(new Rect(screenX - 1, lineY - 2, 2, lineHeight + 4), clipColor);
                
                if (editMode)
                {
                    // Edit mode: show draggable handle BELOW track line
                    var circleCenter = new Vector2(screenX, lineY + lineHeight + ClipHandleRadius);
                    
                    // Selection ring
                    if (isSelected)
                    {
                        DrawCircle(circleCenter, ClipHandleRadius + 3, SelectionColor);
                    }
                    
                    // Handle circle
                    DrawCircle(circleCenter, ClipHandleRadius, clipColor);
                    
                    // Label below handle - stagger if too close to previous
                    var labelOffset = 0f;
                    if (screenX - lastLabelX < 50)
                    {
                        labelOffset = alternateBelow ? 0 : 12;
                        alternateBelow = !alternateBelow;
                    }
                    else
                    {
                        alternateBelow = true;
                    }
                    lastLabelX = screenX;
                    
                    var clipName = GetClipName(i);
                    var labelSize = EditorStyles.miniLabel.CalcSize(new GUIContent(clipName));
                    var labelY = circleCenter.y + ClipHandleRadius + 2 + labelOffset;
                    var labelRect = new Rect(screenX - labelSize.x / 2, labelY, labelSize.x, labelSize.y);
                    
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    if (isSelected) style.normal.textColor = SelectionColor;
                    DrawLabelWithBackground(labelRect, clipName, style);
                }
                else
                {
                    // Preview mode: small tick below track with staggered label
                    EditorGUI.DrawRect(new Rect(screenX - 3, lineY + lineHeight, 6, 3), clipColor);
                    
                    // Stagger labels to avoid overlap
                    var labelOffset = 0f;
                    if (screenX - lastLabelX < 50)
                    {
                        labelOffset = alternateBelow ? 0 : 12;
                        alternateBelow = !alternateBelow;
                    }
                    else
                    {
                        alternateBelow = true;
                    }
                    lastLabelX = screenX;
                    
                    var labelY = lineY + lineHeight + 5 + labelOffset;
                    var clipName = GetClipName(i);
                    var labelSize = EditorStyles.miniLabel.CalcSize(new GUIContent(clipName));
                    var labelRect = new Rect(screenX - labelSize.x / 2, labelY, labelSize.x, labelSize.y);
                    
                    // Highlight selected clip
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    if (isSelected)
                    {
                        style.normal.textColor = SelectionColor;
                    }
                    DrawLabelWithBackground(labelRect, clipName, style);
                }
            }
        }

        protected override Vector2 GetClipScreenPosition(int index, Rect rect)
        {
            if (clips == null || index < 0 || index >= clips.Length)
                return Vector2.zero;
            
            var screenX = ThresholdToScreen(clips[index].Threshold);
            var lineHeight = editMode ? 20 : 12;
            
            if (editMode)
            {
                // Handle position (below track)
                return new Vector2(screenX, lineY + lineHeight + ClipHandleRadius);
            }
            else
            {
                // Tick position (below track)
                return new Vector2(screenX, lineY + lineHeight);
            }
        }
        
        protected override Rect GetClipLabelRect(int index, Rect rect)
        {
            var screenPos = GetClipScreenPosition(index, rect);
            var clipName = GetClipName(index);
            var labelSize = EditorStyles.miniLabel.CalcSize(new GUIContent(clipName));
            
            if (editMode)
            {
                // Label below handle
                var labelY = screenPos.y + ClipHandleRadius + 2;
                return new Rect(screenPos.x - labelSize.x / 2 - 2, labelY, labelSize.x + 4, labelSize.y + 2);
            }
            else
            {
                // Label below tick
                var labelY = screenPos.y + 5;
                return new Rect(screenPos.x - labelSize.x / 2 - 2, labelY, labelSize.x + 4, labelSize.y + 12);
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
                    if (mousePos.x >= screenX - ClipLineHitRadius && mousePos.x <= screenX + ClipLineHitRadius &&
                        mousePos.y >= lineTop && mousePos.y <= lineBottom)
                        return i;
                }
                
                // Check label (both modes)
                if (IsMouseOverClipLabel(i, rect, mousePos))
                    return i;
            }
            return -1;
        }

        protected override void HandleZoom(Rect rect, Event e)
        {
            var mouseThreshold = ScreenToThreshold(e.mousePosition.x);
            ApplyZoomDelta(GetZoomDelta(e));
            var newMouseThreshold = ScreenToThreshold(e.mousePosition.x);
            panOffset.x += (mouseThreshold - newMouseThreshold);
            e.Use();
        }

        protected override void HandleMouseDrag(Rect rect, Event e, SerializedObject serializedObject)
        {
            if (isDraggingPreviewIndicator && !editMode)
            {
                // Clamp mouse position to trackRect bounds (not full rect) for 1D
                var clampedX = Mathf.Clamp(e.mousePosition.x, trackRect.x, trackRect.xMax);
                PreviewPosition = ScreenToBlendSpace(new Vector2(clampedX, lineY), rect);
                e.Use();
            }
            else
            {
                base.HandleMouseDrag(rect, e, serializedObject);
            }
        }
        
        protected override void HandleClipDrag(Rect rect, Event e)
        {
            // Clamp to trackRect for clip dragging too
            var clampedX = Mathf.Clamp(e.mousePosition.x, trackRect.x, trackRect.xMax);
            var threshold = ScreenToThreshold(clampedX);
            
            // Snap to ticks if shift is held
            if (e.shift)
            {
                threshold = Mathf.Round(threshold / TickSpacing) * TickSpacing;
            }
            
            // Update threshold via serialized property for undo support
            var clipsProperty = serializedObject.FindProperty("BlendClips");
            var clipProperty = clipsProperty.GetArrayElementAtIndex(selectedClipIndex);
            var thresholdProperty = clipProperty.FindPropertyRelative("Threshold");
            thresholdProperty.floatValue = threshold;
            serializedObject.ApplyModifiedProperties();
            
            OnClipThresholdChanged?.Invoke(selectedClipIndex, threshold);
        }

        protected override void HandlePan(Event e, Rect rect)
        {
            var delta = e.mousePosition.x - lastMousePos.x;
            var range = (baseMax - baseMin) / zoom;
            panOffset.x -= delta / trackRect.width * range;
        }
        
        protected override void ApplyExternalPanDelta(Rect rect, Vector2 delta)
        {
            var range = (baseMax - baseMin) / zoom;
            panOffset.x -= delta.x / trackRect.width * range;
        }

        #endregion

        #region Private Helpers

        private void UpdateBaseRange(ClipWithThreshold[] clips)
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
            
            hasInitializedRange = true;
        }

        private void DrawTicks()
        {
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
                
                EditorGUI.DrawRect(new Rect(screenX - 1, lineY - tickHeight, 2, tickHeight * 2), tickColor);
                
                if (isMajor)
                {
                    var label = t.ToString("F2");
                    var labelSize = EditorStyles.miniLabel.CalcSize(new GUIContent(label));
                    var labelRect = new Rect(screenX - labelSize.x / 2, lineY + 15, labelSize.x, labelSize.y);
                    GUI.Label(labelRect, label, EditorStyles.miniLabel);
                }
            }
        }

        private float ThresholdToScreen(float threshold)
        {
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var normalized = (threshold - visibleMin) / range;
            return trackRect.x + normalized * trackRect.width;
        }

        private float ScreenToThreshold(float screenX)
        {
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var normalized = (screenX - trackRect.x) / trackRect.width;
            return visibleMin + normalized * range;
        }
        
        protected override Vector2 BlendSpaceToScreen(Vector2 blendPos, Rect rect)
        {
            // For 1D, only X is used, Y is centered on the track
            return new Vector2(ThresholdToScreen(blendPos.x), lineY);
        }
        
        protected override Vector2 ScreenToBlendSpace(Vector2 screenPos, Rect rect)
        {
            // For 1D, only X matters
            return new Vector2(ScreenToThreshold(screenPos.x), 0);
        }
        
        protected override Vector2 ClampPreviewPosition(Vector2 position)
        {
            if (clips == null || clips.Length == 0)
                return position;
            
            // Clamp to clip threshold range
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
    }
}
