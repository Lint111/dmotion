using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Visual 2D blend space editor for Directional2DBlendStateAsset.
    /// Displays clips as colored circles on a 2D grid with zoom/pan functionality.
    /// </summary>
    internal class BlendSpace2DVisualEditor
    {
        // View state
        private float zoom = 1f;
        private Vector2 panOffset = Vector2.zero;
        private int selectedClipIndex = -1;
        private bool isDraggingClip;
        private bool isPanning;
        private Vector2 lastMousePos;
        
        // Visual settings
        private const float MinZoom = 0.5f;
        private const float MaxZoom = 3f;
        private const float GridSize = 0.5f;
        private const float ClipCircleRadius = 12f;
        private const float AxisLabelOffset = 15f;
        
        // Colors
        private static readonly Color GridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        private static readonly Color AxisColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color SelectionColor = new Color(1f, 0.8f, 0f, 1f);
        private static readonly Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        
        // Cached clip colors (generated from index)
        private static readonly Color[] ClipColors = GenerateClipColors(16);
        
        /// <summary>
        /// Currently selected clip index, or -1 if none selected.
        /// </summary>
        public int SelectedClipIndex => selectedClipIndex;
        
        /// <summary>
        /// Event fired when a clip position is changed via dragging.
        /// </summary>
        public event Action<int, float2> OnClipPositionChanged;
        
        /// <summary>
        /// Event fired when selection changes.
        /// </summary>
        public event Action<int> OnSelectionChanged;

        /// <summary>
        /// Draws the 2D blend space editor.
        /// </summary>
        /// <param name="rect">The rect to draw in</param>
        /// <param name="clips">The clips to display</param>
        /// <param name="serializedObject">SerializedObject for undo support</param>
        public void Draw(Rect rect, Directional2DClipWithPosition[] clips, SerializedObject serializedObject)
        {
            if (clips == null) return;
            
            // Draw background
            EditorGUI.DrawRect(rect, BackgroundColor);
            
            // Handle input
            HandleInput(rect, clips, serializedObject);
            
            // Draw grid
            DrawGrid(rect);
            
            // Draw axes
            DrawAxes(rect);
            
            // Draw clips
            DrawClips(rect, clips);
            
            // Draw selection info
            if (selectedClipIndex >= 0 && selectedClipIndex < clips.Length)
            {
                DrawSelectionInfo(rect, clips[selectedClipIndex], selectedClipIndex);
            }
            
            // Draw zoom indicator
            DrawZoomIndicator(rect);
        }

        /// <summary>
        /// Draws position edit fields for the selected clip.
        /// Call this after Draw() to show editable fields below the visual editor.
        /// </summary>
        public bool DrawSelectedClipFields(Directional2DClipWithPosition[] clips, SerializedProperty clipsProperty)
        {
            if (selectedClipIndex < 0 || selectedClipIndex >= clips.Length)
            {
                EditorGUILayout.HelpBox("Click a clip in the blend space to select it.", MessageType.Info);
                return false;
            }

            var clip = clips[selectedClipIndex];
            var clipProperty = clipsProperty.GetArrayElementAtIndex(selectedClipIndex);
            var positionProperty = clipProperty.FindPropertyRelative("Position");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header with color swatch
            EditorGUILayout.BeginHorizontal();
            var colorRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(colorRect, GetClipColor(selectedClipIndex));
            EditorGUILayout.LabelField($"Clip {selectedClipIndex}: {(clip.Clip != null ? clip.Clip.name : "None")}", 
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            
            // Position fields
            EditorGUI.BeginChangeCheck();
            var newX = EditorGUILayout.FloatField("Position X", clip.Position.x);
            var newY = EditorGUILayout.FloatField("Position Y", clip.Position.y);
            
            if (EditorGUI.EndChangeCheck())
            {
                positionProperty.FindPropertyRelative("x").floatValue = newX;
                positionProperty.FindPropertyRelative("y").floatValue = newY;
                return true;
            }
            
            EditorGUILayout.EndVertical();
            return false;
        }

        /// <summary>
        /// Resets the view to default zoom and pan.
        /// </summary>
        public void ResetView()
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
                OnSelectionChanged?.Invoke(-1);
            }
        }

        private void HandleInput(Rect rect, Directional2DClipWithPosition[] clips, SerializedObject serializedObject)
        {
            var e = Event.current;
            var mousePos = e.mousePosition;
            
            if (!rect.Contains(mousePos) && e.type != EventType.MouseUp)
                return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    HandleZoom(rect, e);
                    break;
                    
                case EventType.MouseDown:
                    HandleMouseDown(rect, clips, e);
                    break;
                    
                case EventType.MouseDrag:
                    HandleMouseDrag(rect, clips, serializedObject, e);
                    break;
                    
                case EventType.MouseUp:
                    HandleMouseUp();
                    break;
            }
            
            lastMousePos = mousePos;
        }

        private void HandleZoom(Rect rect, Event e)
        {
            var zoomDelta = -e.delta.y * 0.05f;
            var newZoom = Mathf.Clamp(zoom + zoomDelta, MinZoom, MaxZoom);
            
            // Zoom toward mouse position
            var mouseBlendPos = ScreenToBlendSpace(e.mousePosition, rect);
            zoom = newZoom;
            var newMouseBlendPos = ScreenToBlendSpace(e.mousePosition, rect);
            panOffset += (Vector2)(mouseBlendPos - newMouseBlendPos) * zoom;
            
            e.Use();
        }

        private void HandleMouseDown(Rect rect, Directional2DClipWithPosition[] clips, Event e)
        {
            if (e.button == 0) // Left click
            {
                // Check for clip selection
                var clickedIndex = GetClipAtPosition(rect, clips, e.mousePosition);
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

        private void HandleMouseDrag(Rect rect, Directional2DClipWithPosition[] clips, 
            SerializedObject serializedObject, Event e)
        {
            if (isDraggingClip && selectedClipIndex >= 0)
            {
                var blendPos = ScreenToBlendSpace(e.mousePosition, rect);
                
                // Snap to grid if shift is held
                if (e.shift)
                {
                    blendPos.x = Mathf.Round(blendPos.x / GridSize) * GridSize;
                    blendPos.y = Mathf.Round(blendPos.y / GridSize) * GridSize;
                }
                
                // Update position via serialized property for undo support
                var clipsProperty = serializedObject.FindProperty("BlendClips");
                var clipProperty = clipsProperty.GetArrayElementAtIndex(selectedClipIndex);
                var positionProperty = clipProperty.FindPropertyRelative("Position");
                positionProperty.FindPropertyRelative("x").floatValue = blendPos.x;
                positionProperty.FindPropertyRelative("y").floatValue = blendPos.y;
                serializedObject.ApplyModifiedProperties();
                
                OnClipPositionChanged?.Invoke(selectedClipIndex, blendPos);
                e.Use();
            }
            else if (isPanning)
            {
                var delta = e.mousePosition - lastMousePos;
                panOffset -= delta / (100f * zoom);
                e.Use();
            }
        }

        private void HandleMouseUp()
        {
            isDraggingClip = false;
            isPanning = false;
        }

        private void DrawGrid(Rect rect)
        {
            Handles.BeginGUI();
            
            var gridSpacing = GridSize * 100f * zoom;
            var center = GetBlendSpaceCenter(rect);
            
            // Calculate visible range
            var minX = Mathf.Floor((rect.x - center.x) / gridSpacing) * gridSpacing;
            var maxX = Mathf.Ceil((rect.xMax - center.x) / gridSpacing) * gridSpacing;
            var minY = Mathf.Floor((rect.y - center.y) / gridSpacing) * gridSpacing;
            var maxY = Mathf.Ceil((rect.yMax - center.y) / gridSpacing) * gridSpacing;
            
            Handles.color = GridColor;
            
            // Vertical lines
            for (var x = minX; x <= maxX; x += gridSpacing)
            {
                var screenX = center.x + x;
                if (screenX >= rect.x && screenX <= rect.xMax)
                {
                    Handles.DrawLine(
                        new Vector3(screenX, rect.y, 0),
                        new Vector3(screenX, rect.yMax, 0));
                }
            }
            
            // Horizontal lines
            for (var y = minY; y <= maxY; y += gridSpacing)
            {
                var screenY = center.y + y;
                if (screenY >= rect.y && screenY <= rect.yMax)
                {
                    Handles.DrawLine(
                        new Vector3(rect.x, screenY, 0),
                        new Vector3(rect.xMax, screenY, 0));
                }
            }
            
            Handles.EndGUI();
        }

        private void DrawAxes(Rect rect)
        {
            Handles.BeginGUI();
            
            var center = GetBlendSpaceCenter(rect);
            Handles.color = AxisColor;
            
            // X axis
            if (center.y >= rect.y && center.y <= rect.yMax)
            {
                Handles.DrawLine(
                    new Vector3(rect.x, center.y, 0),
                    new Vector3(rect.xMax, center.y, 0));
                
                // X label
                GUI.Label(new Rect(rect.xMax - 20, center.y + 2, 20, 16), "X", EditorStyles.miniLabel);
            }
            
            // Y axis
            if (center.x >= rect.x && center.x <= rect.xMax)
            {
                Handles.DrawLine(
                    new Vector3(center.x, rect.y, 0),
                    new Vector3(center.x, rect.yMax, 0));
                
                // Y label
                GUI.Label(new Rect(center.x + 2, rect.y + 2, 20, 16), "Y", EditorStyles.miniLabel);
            }
            
            Handles.EndGUI();
        }

        private void DrawClips(Rect rect, Directional2DClipWithPosition[] clips)
        {
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                var screenPos = BlendSpaceToScreen(clip.Position, rect);
                
                if (!rect.Contains(screenPos))
                    continue;
                
                var isSelected = i == selectedClipIndex;
                var color = GetClipColor(i);
                
                // Draw selection ring
                if (isSelected)
                {
                    DrawCircle(screenPos, ClipCircleRadius + 3, SelectionColor);
                }
                
                // Draw clip circle
                DrawCircle(screenPos, ClipCircleRadius, color);
                
                // Draw clip name
                var clipName = clip.Clip != null ? clip.Clip.name : $"Clip {i}";
                var labelContent = new GUIContent(clipName);
                var labelSize = EditorStyles.miniLabel.CalcSize(labelContent);
                var labelRect = new Rect(
                    screenPos.x - labelSize.x / 2,
                    screenPos.y - ClipCircleRadius - labelSize.y - 2,
                    labelSize.x,
                    labelSize.y);
                
                // Background for readability
                EditorGUI.DrawRect(new Rect(labelRect.x - 2, labelRect.y, labelRect.width + 4, labelRect.height), 
                    new Color(0, 0, 0, 0.7f));
                GUI.Label(labelRect, labelContent, EditorStyles.miniLabel);
                
                // Draw position text for selected clip
                if (isSelected)
                {
                    var posText = $"({clip.Position.x:F2}, {clip.Position.y:F2})";
                    var posContent = new GUIContent(posText);
                    var posSize = EditorStyles.miniLabel.CalcSize(posContent);
                    var posRect = new Rect(
                        screenPos.x - posSize.x / 2,
                        screenPos.y + ClipCircleRadius + 2,
                        posSize.x,
                        posSize.y);
                    
                    EditorGUI.DrawRect(new Rect(posRect.x - 2, posRect.y, posRect.width + 4, posRect.height), 
                        new Color(0, 0, 0, 0.7f));
                    GUI.Label(posRect, posContent, EditorStyles.miniLabel);
                }
            }
        }

        private void DrawSelectionInfo(Rect rect, Directional2DClipWithPosition clip, int index)
        {
            var infoText = $"Selected: {(clip.Clip != null ? clip.Clip.name : "None")}";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = SelectionColor }
            };
            GUI.Label(new Rect(rect.x + 5, rect.y + 5, 200, 16), infoText, style);
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

        private int GetClipAtPosition(Rect rect, Directional2DClipWithPosition[] clips, Vector2 mousePos)
        {
            for (var i = clips.Length - 1; i >= 0; i--) // Reverse order for correct z-ordering
            {
                var clip = clips[i];
                var screenPos = BlendSpaceToScreen(clip.Position, rect);
                var distance = Vector2.Distance(mousePos, screenPos);
                
                if (distance <= ClipCircleRadius)
                    return i;
            }
            return -1;
        }

        private Vector2 GetBlendSpaceCenter(Rect rect)
        {
            return new Vector2(
                rect.center.x - panOffset.x * 100f * zoom,
                rect.center.y + panOffset.y * 100f * zoom);
        }

        private Vector2 BlendSpaceToScreen(float2 blendPos, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            return new Vector2(
                center.x + blendPos.x * 100f * zoom,
                center.y - blendPos.y * 100f * zoom); // Y is inverted
        }

        private float2 ScreenToBlendSpace(Vector2 screenPos, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            return new float2(
                (screenPos.x - center.x) / (100f * zoom),
                -(screenPos.y - center.y) / (100f * zoom)); // Y is inverted
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
                // Use HSV for evenly distributed colors
                var hue = (float)i / count;
                colors[i] = Color.HSVToRGB(hue, 0.7f, 0.9f);
            }
            return colors;
        }
    }
}
