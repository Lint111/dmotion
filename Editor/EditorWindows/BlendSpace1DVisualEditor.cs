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
    internal class BlendSpace1DVisualEditor
    {
        // View state
        private float zoom = 1f;
        private float panOffset;
        private int selectedClipIndex = -1;
        private bool isDraggingClip;
        private bool isPanning;
        private Vector2 lastMousePos;
        
        // Stable base range (doesn't change during drag)
        private float baseMin;
        private float baseMax;
        private bool hasInitializedRange;
        
        // Visual settings
        private const float MinZoom = 0.5f;
        private const float MaxZoom = 3f;
        private const float TickSpacing = 0.25f;
        private const float ClipCircleRadius = 10f;
        private const float TrackHeight = 60f;
        private const float TrackPadding = 40f;
        private const float DefaultRangePadding = 0.2f;
        
        // Colors
        private static readonly Color TrackColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color TickColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        private static readonly Color MajorTickColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color SelectionColor = new Color(1f, 0.8f, 0f, 1f);
        private static readonly Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        
        // Cached clip colors
        private static readonly Color[] ClipColors = GenerateClipColors(16);
        
        /// <summary>
        /// Currently selected clip index, or -1 if none selected.
        /// </summary>
        public int SelectedClipIndex => selectedClipIndex;
        
        /// <summary>
        /// Event fired when a clip threshold is changed via dragging.
        /// </summary>
        public event Action<int, float> OnClipThresholdChanged;
        
        /// <summary>
        /// Event fired when selection changes.
        /// </summary>
        public event Action<int> OnSelectionChanged;

        /// <summary>
        /// Draws the 1D blend space editor.
        /// </summary>
        /// <param name="rect">The rect to draw in</param>
        /// <param name="clips">The clips to display</param>
        /// <param name="serializedObject">SerializedObject for undo support</param>
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
        /// Called automatically when not dragging, or manually via ResetView.
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
        /// Call this after Draw() to show editable field below the visual editor.
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

        /// <summary>
        /// Resets the view to default zoom and pan, and recalculates the base range.
        /// </summary>
        public void ResetView()
        {
            zoom = 1f;
            panOffset = 0f;
            hasInitializedRange = false; // Force recalculation on next draw
        }

        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection()
        {
            if (selectedClipIndex != -1)
            {
                selectedClipIndex = -1;
                OnSelectionChanged?.Invoke(-1);
            }
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
            var zoomDelta = -e.delta.y * 0.05f;
            var newZoom = Mathf.Clamp(zoom + zoomDelta, MinZoom, MaxZoom);
            
            // Zoom toward mouse position
            var mouseThreshold = ScreenToThreshold(e.mousePosition.x, trackRect);
            zoom = newZoom;
            var newMouseThreshold = ScreenToThreshold(e.mousePosition.x, trackRect);
            panOffset += (mouseThreshold - newMouseThreshold);
            
            e.Use();
        }

        private void HandleMouseDown(Rect trackRect, ClipWithThreshold[] clips, Event e)
        {
            var lineY = trackRect.y + trackRect.height / 2;
            
            if (e.button == 0) // Left click
            {
                // Check for clip selection
                var clickedIndex = GetClipAtPosition(trackRect, lineY, clips, e.mousePosition);
                if (clickedIndex >= 0)
                {
                    selectedClipIndex = clickedIndex;
                    isDraggingClip = true;
                    OnSelectionChanged?.Invoke(selectedClipIndex);
                    e.Use();
                }
                else
                {
                    // Clicked empty space - clear selection
                    if (selectedClipIndex != -1)
                    {
                        selectedClipIndex = -1;
                        OnSelectionChanged?.Invoke(-1);
                    }
                }
            }
            else if (e.button == 2 || (e.button == 0 && e.alt)) // Middle click or Alt+Left
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
                var range = (baseMax - baseMin) * zoom;
                panOffset -= delta / trackRect.width * range;
                e.Use();
            }
        }

        private void HandleMouseUp()
        {
            isDraggingClip = false;
            isPanning = false;
        }

        private void DrawTicks(Rect trackRect, float lineY)
        {
            var range = (baseMax - baseMin) * zoom;
            var visibleMin = baseMin + panOffset;
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
                
                // Draw tick
                EditorGUI.DrawRect(new Rect(screenX - 1, lineY - tickHeight, 2, tickHeight * 2), tickColor);
                
                // Draw label for major ticks
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
            // Sort clips by threshold for proper z-ordering (draw back to front)
            var sortedIndices = new int[clips.Length];
            for (var i = 0; i < clips.Length; i++) sortedIndices[i] = i;
            Array.Sort(sortedIndices, (a, b) => clips[a].Threshold.CompareTo(clips[b].Threshold));
            
            foreach (var i in sortedIndices)
            {
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold, trackRect);
                var screenPos = new Vector2(screenX, lineY);
                
                // Skip if outside visible area (with some margin for the circle)
                if (screenX < trackRect.x - ClipCircleRadius || screenX > trackRect.xMax + ClipCircleRadius)
                    continue;
                
                var isSelected = i == selectedClipIndex;
                var color = GetClipColor(i);
                
                // Draw vertical line from track to circle
                EditorGUI.DrawRect(new Rect(screenX - 1, lineY - 20, 2, 20), color);
                
                // Draw selection ring
                if (isSelected)
                {
                    DrawCircle(new Vector2(screenX, lineY - 25), ClipCircleRadius + 3, SelectionColor);
                }
                
                // Draw clip circle
                DrawCircle(new Vector2(screenX, lineY - 25), ClipCircleRadius, color);
                
                // Draw clip name
                var clipName = clip.Clip != null ? clip.Clip.name : $"Clip {i}";
                var labelContent = new GUIContent(clipName);
                var labelSize = EditorStyles.miniLabel.CalcSize(labelContent);
                var labelRect = new Rect(
                    screenX - labelSize.x / 2,
                    lineY - 25 - ClipCircleRadius - labelSize.y - 2,
                    labelSize.x,
                    labelSize.y);
                
                // Background for readability
                EditorGUI.DrawRect(new Rect(labelRect.x - 2, labelRect.y, labelRect.width + 4, labelRect.height), 
                    new Color(0, 0, 0, 0.7f));
                GUI.Label(labelRect, labelContent, EditorStyles.miniLabel);
                
                // Draw threshold value for selected clip
                if (isSelected)
                {
                    var thresholdText = clip.Threshold.ToString("F2");
                    var thresholdContent = new GUIContent(thresholdText);
                    var thresholdSize = EditorStyles.miniLabel.CalcSize(thresholdContent);
                    var thresholdRect = new Rect(
                        screenX - thresholdSize.x / 2,
                        lineY + 30,
                        thresholdSize.x,
                        thresholdSize.y);
                    
                    var selectedStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = SelectionColor }
                    };
                    GUI.Label(thresholdRect, thresholdContent, selectedStyle);
                }
            }
        }

        private void DrawZoomIndicator(Rect rect)
        {
            var zoomText = $"Zoom: {zoom:F1}x";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerRight
            };
            GUI.Label(new Rect(rect.xMax - 60, rect.yMax - 18, 55, 16), zoomText, style);
            
            // Instructions
            var helpText = "Scroll: Zoom | MMB: Pan | Shift+Drag: Snap";
            GUI.Label(new Rect(rect.x + 5, rect.yMax - 18, 250, 16), helpText, EditorStyles.miniLabel);
        }

        private void DrawCircle(Vector2 center, float radius, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, radius);
            Handles.EndGUI();
        }

        private int GetClipAtPosition(Rect trackRect, float lineY, ClipWithThreshold[] clips, Vector2 mousePos)
        {
            // Check in reverse order for correct z-ordering
            for (var i = clips.Length - 1; i >= 0; i--)
            {
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold, trackRect);
                var circleCenter = new Vector2(screenX, lineY - 25);
                var distance = Vector2.Distance(mousePos, circleCenter);
                
                if (distance <= ClipCircleRadius)
                    return i;
            }
            return -1;
        }

        private float ThresholdToScreen(float threshold, Rect trackRect)
        {
            var range = (baseMax - baseMin) * zoom;
            var visibleMin = baseMin + panOffset;
            var normalized = (threshold - visibleMin) / range;
            return trackRect.x + normalized * trackRect.width;
        }

        private float ScreenToThreshold(float screenX, Rect trackRect)
        {
            var range = (baseMax - baseMin) * zoom;
            var visibleMin = baseMin + panOffset;
            var normalized = (screenX - trackRect.x) / trackRect.width;
            return visibleMin + normalized * range;
        }

        private static Color GetClipColor(int index)
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
    }
}
