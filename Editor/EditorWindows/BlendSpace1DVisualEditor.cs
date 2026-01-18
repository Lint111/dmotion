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
        private const float TrackPadding = 40f;
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
            
            // Calculate track rect (centered vertically)
            trackRect = new Rect(
                rect.x + TrackPadding,
                rect.y + (rect.height - TrackHeight) / 2,
                rect.width - TrackPadding * 2,
                TrackHeight);
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
            // Draw track line
            EditorGUI.DrawRect(new Rect(trackRect.x, lineY - 2, trackRect.width, 4), TrackColor);
            
            // Draw ticks
            DrawTicks();
        }

        protected override void DrawClips(Rect rect)
        {
            if (clips == null) return;
            
            // Sort clips by threshold for proper z-ordering
            var sortedIndices = new int[clips.Length];
            for (var i = 0; i < clips.Length; i++) sortedIndices[i] = i;
            Array.Sort(sortedIndices, (a, b) => clips[a].Threshold.CompareTo(clips[b].Threshold));
            
            foreach (var i in sortedIndices)
            {
                var clip = clips[i];
                var screenX = ThresholdToScreen(clip.Threshold);
                
                if (screenX < trackRect.x - ClipCircleRadius || screenX > trackRect.xMax + ClipCircleRadius)
                    continue;
                
                var circleCenter = new Vector2(screenX, lineY - 25);
                
                // Draw vertical line from track to circle
                EditorGUI.DrawRect(new Rect(screenX - 1, lineY - 20, 2, 20), GetClipColor(i));
                
                // Draw clip circle with label
                DrawClipCircle(circleCenter, i, GetClipName(i), true, clip.Threshold.ToString("F2"));
            }
        }

        protected override int GetClipAtPosition(Rect rect, Vector2 mousePos)
        {
            if (clips == null) return -1;
            
            for (var i = clips.Length - 1; i >= 0; i--)
            {
                var screenX = ThresholdToScreen(clips[i].Threshold);
                var circleCenter = new Vector2(screenX, lineY - 25);
                
                if (IsPointInCircle(mousePos, circleCenter, ClipCircleRadius))
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

        protected override void HandleClipDrag(Rect rect, Event e)
        {
            var threshold = ScreenToThreshold(e.mousePosition.x);
            
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

        #endregion
    }
}
