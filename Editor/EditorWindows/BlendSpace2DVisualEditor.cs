using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Visual 2D blend space editor for Directional2DBlendStateAsset.
    /// Displays clips as colored circles on a 2D grid with zoom/pan functionality.
    /// </summary>
    internal class BlendSpace2DVisualEditor : BlendSpaceVisualEditorBase
    {
        // Target state
        private Directional2DBlendStateAsset targetState;
        
        // Current clip data (set during Draw)
        private Directional2DClipWithPosition[] clips;
        private SerializedObject serializedObject;
        private Rect drawRect;
        
        // 2D-specific visual settings
        private const float GridSize = 0.5f;
        
        /// <summary>
        /// Event fired when a clip position is changed via dragging.
        /// </summary>
        public event Action<int, Vector2> OnClipPositionChanged;

        #region Abstract Implementation

        public override string EditorTitle => "Blend Space 2D";
        public override UnityEngine.Object Target => targetState;

        /// <summary>
        /// Sets the target state for this editor.
        /// </summary>
        public void SetTarget(Directional2DBlendStateAsset state)
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
        /// Draws the 2D blend space editor.
        /// </summary>
        public void Draw(Rect rect, Directional2DClipWithPosition[] clips, SerializedObject serializedObject)
        {
            if (clips == null) return;
            
            // Store references for use in abstract method implementations
            this.clips = clips;
            this.serializedObject = serializedObject;
            this.drawRect = rect;
            
            // Use base class core draw loop
            DrawCore(rect, serializedObject);
        }

        /// <summary>
        /// Draws position edit fields for the selected clip.
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
            EditorGUILayout.LabelField($"Clip {selectedClipIndex}: {GetClipName(selectedClipIndex)}", 
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
            DrawGrid(rect);
            DrawAxes(rect);
        }

        protected override void DrawClips(Rect rect)
        {
            if (clips == null) return;
            
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                var screenPos = BlendSpaceToScreen(new Vector2(clip.Position.x, clip.Position.y), rect);
                
                if (!rect.Contains(screenPos))
                    continue;
                
                var valueText = $"({clip.Position.x:F2}, {clip.Position.y:F2})";
                DrawClipCircle(screenPos, i, GetClipName(i), true, valueText);
            }
        }

        protected override int GetClipAtPosition(Rect rect, Vector2 mousePos)
        {
            if (clips == null) return -1;
            
            for (var i = clips.Length - 1; i >= 0; i--)
            {
                var clip = clips[i];
                var screenPos = BlendSpaceToScreen(new Vector2(clip.Position.x, clip.Position.y), rect);
                
                if (IsPointInCircle(mousePos, screenPos, ClipCircleRadius))
                    return i;
            }
            return -1;
        }

        protected override void HandleZoom(Rect rect, Event e)
        {
            var mouseBlendPos = ScreenToBlendSpace(e.mousePosition, rect);
            ApplyZoomDelta(GetZoomDelta(e));
            var newMouseBlendPos = ScreenToBlendSpace(e.mousePosition, rect);
            panOffset += (mouseBlendPos - newMouseBlendPos) * zoom;
            e.Use();
        }

        protected override void HandleClipDrag(Rect rect, Event e)
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
        }

        protected override void HandlePan(Event e, Rect rect)
        {
            var delta = e.mousePosition - lastMousePos;
            panOffset -= delta / (100f * zoom);
        }

        #endregion

        #region Private Helpers

        private void DrawGrid(Rect rect)
        {
            Handles.BeginGUI();
            
            var gridSpacing = GridSize * 100f * zoom;
            var center = GetBlendSpaceCenter(rect);
            
            var minX = Mathf.Floor((rect.x - center.x) / gridSpacing) * gridSpacing;
            var maxX = Mathf.Ceil((rect.xMax - center.x) / gridSpacing) * gridSpacing;
            var minY = Mathf.Floor((rect.y - center.y) / gridSpacing) * gridSpacing;
            var maxY = Mathf.Ceil((rect.yMax - center.y) / gridSpacing) * gridSpacing;
            
            Handles.color = GridColor;
            
            for (var x = minX; x <= maxX; x += gridSpacing)
            {
                var screenX = center.x + x;
                if (screenX >= rect.x && screenX <= rect.xMax)
                {
                    Handles.DrawLine(new Vector3(screenX, rect.y, 0), new Vector3(screenX, rect.yMax, 0));
                }
            }
            
            for (var y = minY; y <= maxY; y += gridSpacing)
            {
                var screenY = center.y + y;
                if (screenY >= rect.y && screenY <= rect.yMax)
                {
                    Handles.DrawLine(new Vector3(rect.x, screenY, 0), new Vector3(rect.xMax, screenY, 0));
                }
            }
            
            Handles.EndGUI();
        }

        private void DrawAxes(Rect rect)
        {
            Handles.BeginGUI();
            
            var center = GetBlendSpaceCenter(rect);
            Handles.color = AxisColor;
            
            if (center.y >= rect.y && center.y <= rect.yMax)
            {
                Handles.DrawLine(new Vector3(rect.x, center.y, 0), new Vector3(rect.xMax, center.y, 0));
                GUI.Label(new Rect(rect.xMax - 20, center.y + 2, 20, 16), "X", EditorStyles.miniLabel);
            }
            
            if (center.x >= rect.x && center.x <= rect.xMax)
            {
                Handles.DrawLine(new Vector3(center.x, rect.y, 0), new Vector3(center.x, rect.yMax, 0));
                GUI.Label(new Rect(center.x + 2, rect.y + 2, 20, 16), "Y", EditorStyles.miniLabel);
            }
            
            Handles.EndGUI();
        }

        private Vector2 GetBlendSpaceCenter(Rect rect)
        {
            return new Vector2(
                rect.center.x - panOffset.x * 100f * zoom,
                rect.center.y + panOffset.y * 100f * zoom);
        }

        private Vector2 BlendSpaceToScreen(Vector2 blendPos, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            return new Vector2(
                center.x + blendPos.x * 100f * zoom,
                center.y - blendPos.y * 100f * zoom);
        }

        private Vector2 ScreenToBlendSpace(Vector2 screenPos, Rect rect)
        {
            var center = GetBlendSpaceCenter(rect);
            return new Vector2(
                (screenPos.x - center.x) / (100f * zoom),
                -(screenPos.y - center.y) / (100f * zoom));
        }

        #endregion
    }
}
