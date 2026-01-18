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
        // Stable base range (doesn't change during drag)
        private float baseMin;
        private float baseMax;
        private bool hasInitializedRange;
        
        // 1D-specific visual settings
        private const float TickSpacing = 0.25f;
        private const float TrackHeight = 60f;
        private const float TrackPadding = 40f;
        private const float DefaultRangePadding = 0.2f;
        
        private static readonly Color TrackColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        
        /// <summary>
        /// Event fired when a clip threshold is changed via dragging.
        /// </summary>
        public event Action<int, float> OnClipThresholdChanged;

        /// <summary>
        /// Draws the 1D blend space editor.
        /// </summary>
        public void Draw(Rect rect, ClipWithThreshold[] clips, SerializedObject serializedObject)
        {
            if (clips == null) return;
            
            // Initialize or update base range only when not dragging
            if (!hasInitializedRange || (!isDraggingClip && !isPanning))
            {
                UpdateBaseRange(clips);
            }
            
            // Draw background
            EditorGUI.DrawRect(rect, BackgroundColor);
            
            // Calculate track rect (centered vertically)
            var trackRect = new Rect(
                rect.x + TrackPadding,
                rect.y + (rect.height - TrackHeight) / 2,
                rect.width - TrackPadding * 2,
                TrackHeight);
            
            // Handle input
            HandleInput(rect, trackRect, clips, serializedObject);
            
            // Draw track background
            var lineY = trackRect.y + trackRect.height / 2;
            EditorGUI.DrawRect(new Rect(trackRect.x, lineY - 2, trackRect.width, 4), TrackColor);
            
            // Draw ticks and labels
            DrawTicks(trackRect, lineY);
            
            // Draw clips
            DrawClips(trackRect, lineY, clips);
            
            // Draw zoom indicator
            DrawZoomIndicator(rect);
        }

        /// <summary>
        /// Updates the base range from clip positions.
        /// </summary>
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
                
                // Ensure minimum range
                var range = baseMax - baseMin;
                if (range < 0.1f)
                {
                    var center = (baseMin + baseMax) / 2f;
                    baseMin = center - 0.5f;
                    baseMax = center + 0.5f;
                    range = 1f;
                }
                
                // Add padding
                baseMin -= range * DefaultRangePadding;
                baseMax += range * DefaultRangePadding;
            }
            
            hasInitializedRange = true;
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
            EditorGUILayout.LabelField($"Clip {selectedClipIndex}: {(clip.Clip != null ? clip.Clip.name : "None")}", 
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
            hasInitializedRange = false; // Force recalculation on next draw
        }

        private void HandleInput(Rect rect, Rect trackRect, ClipWithThreshold[] clips, 
            SerializedObject serializedObject)
        {
            var e = Event.current;
            var mousePos = e.mousePosition;
            
            if (!rect.Contains(mousePos) && e.type != EventType.MouseUp)
                return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    HandleZoom(trackRect, e);
                    break;
                    
                case EventType.MouseDown:
                    HandleMouseDown(trackRect, clips, e);
                    break;
                    
                case EventType.MouseDrag:
                    HandleMouseDrag(trackRect, clips, serializedObject, e);
                    break;
                    
                case EventType.MouseUp:
                    HandleMouseUp();
                    break;
            }
            
            lastMousePos = mousePos;
        }

        private void HandleZoom(Rect trackRect, Event e)
        {
            // Zoom toward mouse position
            var mouseThreshold = ScreenToThreshold(e.mousePosition.x, trackRect);
            ApplyZoomDelta(GetZoomDelta(e));
            var newMouseThreshold = ScreenToThreshold(e.mousePosition.x, trackRect);
            panOffset.x += (mouseThreshold - newMouseThreshold);
            
            e.Use();
        }

        private void HandleMouseDown(Rect trackRect, ClipWithThreshold[] clips, Event e)
        {
            var lineY = trackRect.y + trackRect.height / 2;
            
            if (e.button == 0) // Left click
            {
                var clickedIndex = GetClipAtPosition(trackRect, lineY, clips, e.mousePosition);
                if (clickedIndex >= 0)
                {
                    SetSelection(clickedIndex);
                    isDraggingClip = true;
                    e.Use();
                }
                else
                {
                    SetSelection(-1);
                }
            }
            else if (e.button == 2 || (e.button == 0 && e.alt))
            {
                isPanning = true;
                e.Use();
            }
        }

        private void HandleMouseDrag(Rect trackRect, ClipWithThreshold[] clips, 
            SerializedObject serializedObject, Event e)
        {
            if (isDraggingClip && selectedClipIndex >= 0)
            {
                var threshold = ScreenToThreshold(e.mousePosition.x, trackRect);
                
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
                e.Use();
            }
            else if (isPanning)
            {
                var delta = e.mousePosition.x - lastMousePos.x;
                var range = (baseMax - baseMin) / zoom;
                panOffset.x -= delta / trackRect.width * range;
                e.Use();
            }
        }

        private void DrawTicks(Rect trackRect, float lineY)
        {
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var visibleMax = visibleMin + range;
            
            // Determine tick spacing based on zoom
            var tickStep = TickSpacing;
            while (tickStep * trackRect.width / range < 30) tickStep *= 2;
            
            var startTick = Mathf.Floor(visibleMin / tickStep) * tickStep;
            
            for (var t = startTick; t <= visibleMax + tickStep; t += tickStep)
            {
                var screenX = ThresholdToScreen(t, trackRect);
                
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

        private void DrawClips(Rect trackRect, float lineY, ClipWithThreshold[] clips)
        {
            // Sort clips by threshold for proper z-ordering
            var sortedIndices = new int[clips.Length];
            for (var i = 0; i < clips.Length; i++) sortedIndices[i] = i;
            Array.Sort(sortedIndices, (a, b) => clips[a].Threshold.CompareTo(clips[b].Threshold));
            
            foreach (var i in sortedIndices)
            {
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold, trackRect);
                
                if (screenX < trackRect.x - ClipCircleRadius || screenX > trackRect.xMax + ClipCircleRadius)
                    continue;
                
                var isSelected = i == selectedClipIndex;
                var color = GetClipColor(i);
                var circleCenter = new Vector2(screenX, lineY - 25);
                
                // Draw vertical line from track to circle
                EditorGUI.DrawRect(new Rect(screenX - 1, lineY - 20, 2, 20), color);
                
                // Draw selection ring
                if (isSelected)
                {
                    DrawCircle(circleCenter, ClipCircleRadius + 3, SelectionColor);
                }
                
                // Draw clip circle
                DrawCircle(circleCenter, ClipCircleRadius, color);
                
                // Draw clip name
                var clipName = clip.Clip != null ? clip.Clip.name : $"Clip {i}";
                var labelRect = GetLabelRectAbove(circleCenter, clipName, 10);
                DrawLabelWithBackground(labelRect, clipName);
                
                // Draw threshold value for selected clip
                if (isSelected)
                {
                    var thresholdText = clip.Threshold.ToString("F2");
                    var thresholdRect = GetLabelRectBelow(new Vector2(screenX, lineY), thresholdText, 15);
                    var selectedStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = SelectionColor }
                    };
                    GUI.Label(thresholdRect, thresholdText, selectedStyle);
                }
            }
        }

        private int GetClipAtPosition(Rect trackRect, float lineY, ClipWithThreshold[] clips, Vector2 mousePos)
        {
            for (var i = clips.Length - 1; i >= 0; i--)
            {
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold, trackRect);
                var circleCenter = new Vector2(screenX, lineY - 25);
                
                if (IsPointInCircle(mousePos, circleCenter, ClipCircleRadius))
                    return i;
            }
            return -1;
        }

        private float ThresholdToScreen(float threshold, Rect trackRect)
        {
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var normalized = (threshold - visibleMin) / range;
            return trackRect.x + normalized * trackRect.width;
        }

        private float ScreenToThreshold(float screenX, Rect trackRect)
        {
            var range = (baseMax - baseMin) / zoom;
            var visibleMin = baseMin + panOffset.x;
            var normalized = (screenX - trackRect.x) / trackRect.width;
            return visibleMin + normalized * range;
        }
    }
}
