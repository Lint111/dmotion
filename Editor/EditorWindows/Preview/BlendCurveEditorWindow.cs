using System;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Custom curve editor window for transition blend curves.
    /// Shows correctly-oriented presets (1→0) and provides simple curve editing.
    /// </summary>
    internal class BlendCurveEditorWindow : EditorWindow
    {
        #region Static
        
        private static BlendCurveEditorWindow instance;
        
        // EditorPrefs keys for remembering window position (size is fixed)
        private const string PrefKeyPosX = "DMotion.BlendCurveEditor.PosX";
        private const string PrefKeyPosY = "DMotion.BlendCurveEditor.PosY";
        private const string PrefKeyHasSavedPos = "DMotion.BlendCurveEditor.HasSavedPos";
        
        // Fixed window size (shows all content without scrolling)
        private static readonly Vector2 FixedWindowSize = new Vector2(320, 380);
        
        /// <summary>
        /// Opens the blend curve editor for the given curve.
        /// </summary>
        public static void Show(AnimationCurve curve, Action<AnimationCurve> onCurveChanged, Rect buttonRect)
        {
            // Close existing instance
            if (instance != null)
            {
                instance.Close();
                instance = null;
            }
            
            instance = GetWindow<BlendCurveEditorWindow>(true, "Blend Curve Editor", true);
            instance.Initialize(curve, onCurveChanged);
            
            // Fixed size - not resizable
            instance.minSize = FixedWindowSize;
            instance.maxSize = FixedWindowSize;
            
            // Restore saved position or use button position for first open
            if (EditorPrefs.GetBool(PrefKeyHasSavedPos, false))
            {
                float x = EditorPrefs.GetFloat(PrefKeyPosX, 100);
                float y = EditorPrefs.GetFloat(PrefKeyPosY, 100);
                instance.position = new Rect(x, y, FixedWindowSize.x, FixedWindowSize.y);
            }
            else
            {
                // First open - position near the button
                var screenPos = GUIUtility.GUIToScreenPoint(new Vector2(buttonRect.x, buttonRect.yMax + 5));
                instance.position = new Rect(screenPos, FixedWindowSize);
            }
            
            instance.Show();
            instance.Focus();
        }
        
        private void SaveWindowPosition()
        {
            // Only save position (x, y), not size since it's fixed
            var pos = position;
            EditorPrefs.SetFloat(PrefKeyPosX, pos.x);
            EditorPrefs.SetFloat(PrefKeyPosY, pos.y);
            EditorPrefs.SetBool(PrefKeyHasSavedPos, true);
        }
        
        #endregion
        
        #region Constants
        
        private const float KeyframeHandleSize = 10f;
        private const float TangentHandleSize = 7f;
        private const float TangentLineLength = 50f;
        
        // Use shared colors for consistency
        private static Color CurveColor => PreviewEditorColors.CurveAccent;
        private static Color KeyframeColor => PreviewEditorColors.Keyframe;
        private static Color SelectedKeyframeColor => PreviewEditorColors.SelectionHighlight;
        private static Color TangentColor => PreviewEditorColors.Tangent;
        private static Color GridColor => PreviewEditorColors.Grid;
        private static Color BackgroundColor => PreviewEditorColors.DarkBackground;
        
        #endregion
        
        #region State
        
        private AnimationCurve curve;
        private Action<AnimationCurve> onCurveChanged;
        
        private int selectedKeyIndex = -1;
        private bool isDraggingKey;
        private bool isDraggingInTangent;
        private bool isDraggingOutTangent;
        
        private Rect curveAreaRect;
        
        // Cached to avoid per-frame allocation
        private static GUIStyle cachedHelpStyle;
        private static GUIStyle cachedLabelStyle;
        private const int CurveSegments = 50;
        private readonly Vector3[] cachedCurvePoints = new Vector3[CurveSegments + 1];
        
        #endregion
        
        #region Initialization
        
        private void Initialize(AnimationCurve sourceCurve, Action<AnimationCurve> callback)
        {
            // Deep copy the curve
            if (sourceCurve != null && sourceCurve.length >= 2)
            {
                curve = new AnimationCurve(sourceCurve.keys);
            }
            else
            {
                curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            }
            curve.preWrapMode = WrapMode.Clamp;
            curve.postWrapMode = WrapMode.Clamp;
            
            onCurveChanged = callback;
            selectedKeyIndex = -1;
        }
        
        #endregion
        
        #region GUI
        
        private void OnGUI()
        {
            if (curve == null)
            {
                Close();
                return;
            }
            
            EditorGUILayout.Space(8);
            
            // Preset buttons
            EditorGUILayout.LabelField("Presets (From=1 → To=0)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Linear", GUILayout.Height(24)))
                ApplyPreset(AnimationCurve.Linear(0f, 1f, 1f, 0f));
            
            if (GUILayout.Button("Ease In", GUILayout.Height(24)))
                ApplyPreset(CreateEaseIn());
            
            if (GUILayout.Button("Ease Out", GUILayout.Height(24)))
                ApplyPreset(CreateEaseOut());
            
            if (GUILayout.Button("Ease In-Out", GUILayout.Height(24)))
                ApplyPreset(CreateEaseInOut());
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(12);
            
            // Curve editor area
            EditorGUILayout.LabelField("Curve Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            
            curveAreaRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, 
                GUILayout.ExpandWidth(true), GUILayout.Height(150));
            
            DrawCurveEditor(curveAreaRect);
            HandleCurveInput(curveAreaRect);
            
            EditorGUILayout.Space(8);
            
            // Instructions - use cached style
            cachedHelpStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = PreviewEditorColors.DimText }
            };
            EditorGUILayout.LabelField("• Click keyframe to select  • Drag to move\n• Drag tangent handles to adjust curve shape\n• Double-click curve to add keyframe\n• Delete key to remove (except endpoints)", cachedHelpStyle);
            
            EditorGUILayout.Space(8);
            
            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(24)))
            {
                Close();
            }
            
            GUILayout.Space(8);
            
            if (GUILayout.Button("Apply", GUILayout.Width(80), GUILayout.Height(24)))
            {
                onCurveChanged?.Invoke(curve);
                Close();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(8);
            
            // Handle keyboard
            HandleKeyboard();
        }
        
        private void DrawCurveEditor(Rect rect)
        {
            // Background
            EditorGUI.DrawRect(rect, BackgroundColor);
            
            // Border
            Handles.BeginGUI();
            Handles.color = PreviewEditorColors.MediumBorder;
            Handles.DrawLine(new Vector3(rect.x, rect.y), new Vector3(rect.xMax, rect.y));
            Handles.DrawLine(new Vector3(rect.xMax, rect.y), new Vector3(rect.xMax, rect.yMax));
            Handles.DrawLine(new Vector3(rect.xMax, rect.yMax), new Vector3(rect.x, rect.yMax));
            Handles.DrawLine(new Vector3(rect.x, rect.yMax), new Vector3(rect.x, rect.y));
            
            // Grid lines
            Handles.color = GridColor;
            for (float t = 0.25f; t < 1f; t += 0.25f)
            {
                float x = rect.x + t * rect.width;
                Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            }
            for (float v = 0.25f; v < 1f; v += 0.25f)
            {
                float y = rect.y + (1f - v) * rect.height;
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            }
            
            Handles.EndGUI();
            
            // Labels - use cached style
            cachedLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                normal = { textColor = PreviewEditorColors.LightDimText }
            };
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, 50, 12), "From (1)", cachedLabelStyle);
            GUI.Label(new Rect(rect.x + 2, rect.yMax - 14, 40, 12), "To (0)", cachedLabelStyle);
            GUI.Label(new Rect(rect.xMax - 20, rect.yMax - 14, 20, 12), "1.0", cachedLabelStyle);
            
            // Draw curve - use cached points array
            Handles.BeginGUI();
            Handles.color = CurveColor;
            
            for (int i = 0; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float value = curve.Evaluate(t);
                cachedCurvePoints[i] = CurveToScreen(rect, t, value);
            }
            Handles.DrawAAPolyLine(3f, cachedCurvePoints);
            
            // Draw keyframes and tangent handles
            for (int i = 0; i < curve.length; i++)
            {
                var key = curve.keys[i];
                var keyPos = CurveToScreen(rect, key.time, key.value);
                bool isSelected = (i == selectedKeyIndex);
                
                // Tangent handles (only for selected keyframe)
                if (isSelected)
                {
                    Handles.color = TangentColor;
                    
                    // In tangent (if not first key)
                    if (i > 0)
                    {
                        var inTangentEnd = GetTangentHandlePosition(rect, key, true);
                        Handles.DrawLine(keyPos, inTangentEnd);
                        Handles.DrawSolidDisc(inTangentEnd, Vector3.forward, TangentHandleSize / 2);
                    }
                    
                    // Out tangent (if not last key)
                    if (i < curve.length - 1)
                    {
                        var outTangentEnd = GetTangentHandlePosition(rect, key, false);
                        Handles.DrawLine(keyPos, outTangentEnd);
                        Handles.DrawSolidDisc(outTangentEnd, Vector3.forward, TangentHandleSize / 2);
                    }
                }
                
                // Keyframe handle
                Handles.color = isSelected ? SelectedKeyframeColor : KeyframeColor;
                Handles.DrawSolidDisc(keyPos, Vector3.forward, KeyframeHandleSize / 2);
            }
            
            Handles.EndGUI();
        }
        
        private void HandleCurveInput(Rect rect)
        {
            Event e = Event.current;
            if (e == null) return;
            
            Vector2 mousePos = e.mousePosition;
            
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && rect.Contains(mousePos):
                    HandleMouseDown(rect, mousePos, e);
                    break;
                    
                case EventType.MouseDrag when e.button == 0:
                    HandleMouseDrag(rect, mousePos, e);
                    break;
                    
                case EventType.MouseUp when e.button == 0:
                    HandleMouseUp(e);
                    break;
            }
        }
        
        private void HandleMouseDown(Rect rect, Vector2 mousePos, Event e)
        {
            // Check tangent handles first (for selected keyframe)
            if (selectedKeyIndex >= 0 && selectedKeyIndex < curve.length)
            {
                var key = curve.keys[selectedKeyIndex];
                
                if (selectedKeyIndex > 0)
                {
                    var inTangentPos = GetTangentHandlePosition(rect, key, true);
                    if (Vector2.Distance(mousePos, inTangentPos) < TangentHandleSize + 4)
                    {
                        isDraggingInTangent = true;
                        e.Use();
                        return;
                    }
                }
                
                if (selectedKeyIndex < curve.length - 1)
                {
                    var outTangentPos = GetTangentHandlePosition(rect, key, false);
                    if (Vector2.Distance(mousePos, outTangentPos) < TangentHandleSize + 4)
                    {
                        isDraggingOutTangent = true;
                        e.Use();
                        return;
                    }
                }
            }
            
            // Check keyframes
            for (int i = 0; i < curve.length; i++)
            {
                var key = curve.keys[i];
                var keyPos = CurveToScreen(rect, key.time, key.value);
                
                if (Vector2.Distance(mousePos, keyPos) < KeyframeHandleSize + 4)
                {
                    selectedKeyIndex = i;
                    isDraggingKey = true;
                    e.Use();
                    Repaint();
                    return;
                }
            }
            
            // Double-click to add keyframe
            if (e.clickCount == 2)
            {
                var curvePos = ScreenToCurve(rect, mousePos);
                float t = Mathf.Clamp(curvePos.x, 0.01f, 0.99f);
                
                // Don't add too close to existing keyframes
                bool tooClose = false;
                for (int i = 0; i < curve.length; i++)
                {
                    if (Mathf.Abs(curve.keys[i].time - t) < 0.05f)
                    {
                        tooClose = true;
                        break;
                    }
                }
                
                if (!tooClose)
                {
                    float v = curve.Evaluate(t); // Add on the curve
                    int newIndex = curve.AddKey(t, v);
                    selectedKeyIndex = newIndex;
                    e.Use();
                    Repaint();
                }
            }
            else
            {
                selectedKeyIndex = -1;
                e.Use();
                Repaint();
            }
        }
        
        private void HandleMouseDrag(Rect rect, Vector2 mousePos, Event e)
        {
            if (isDraggingKey && selectedKeyIndex >= 0 && selectedKeyIndex < curve.length)
            {
                var curvePos = ScreenToCurve(rect, mousePos);
                var key = curve.keys[selectedKeyIndex];
                
                float newTime = curvePos.x;
                float newValue = curvePos.y;
                
                // Constrain endpoints
                if (selectedKeyIndex == 0)
                {
                    newTime = 0f;
                    newValue = 1f;
                }
                else if (selectedKeyIndex == curve.length - 1)
                {
                    newTime = 1f;
                    newValue = 0f;
                }
                else
                {
                    float minTime = curve.keys[selectedKeyIndex - 1].time + 0.02f;
                    float maxTime = curve.keys[selectedKeyIndex + 1].time - 0.02f;
                    newTime = Mathf.Clamp(newTime, minTime, maxTime);
                    newValue = Mathf.Clamp01(newValue);
                }
                
                curve.MoveKey(selectedKeyIndex, new Keyframe(newTime, newValue, key.inTangent, key.outTangent));
                e.Use();
                Repaint();
            }
            else if (isDraggingInTangent && selectedKeyIndex >= 0)
            {
                UpdateTangentFromMouse(rect, mousePos, true);
                e.Use();
                Repaint();
            }
            else if (isDraggingOutTangent && selectedKeyIndex >= 0)
            {
                UpdateTangentFromMouse(rect, mousePos, false);
                e.Use();
                Repaint();
            }
        }
        
        private void HandleMouseUp(Event e)
        {
            if (isDraggingKey || isDraggingInTangent || isDraggingOutTangent)
            {
                isDraggingKey = false;
                isDraggingInTangent = false;
                isDraggingOutTangent = false;
                e.Use();
            }
        }
        
        private void HandleKeyboard()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                if (selectedKeyIndex > 0 && selectedKeyIndex < curve.length - 1)
                {
                    curve.RemoveKey(selectedKeyIndex);
                    selectedKeyIndex = -1;
                    e.Use();
                    Repaint();
                }
            }
        }
        
        #endregion
        
        #region Helpers
        
        private Vector3 CurveToScreen(Rect rect, float time, float value)
        {
            float x = rect.x + time * rect.width;
            float y = rect.y + (1f - value) * rect.height;
            return new Vector3(x, y, 0);
        }
        
        private Vector2 ScreenToCurve(Rect rect, Vector2 screenPos)
        {
            float t = (screenPos.x - rect.x) / rect.width;
            float v = 1f - (screenPos.y - rect.y) / rect.height;
            return new Vector2(t, v);
        }
        
        private Vector3 GetTangentHandlePosition(Rect rect, Keyframe key, bool isInTangent)
        {
            float tangent = isInTangent ? key.inTangent : key.outTangent;
            float direction = isInTangent ? -1f : 1f;
            
            float dx = direction * 0.15f;
            float dy = tangent * dx;
            
            float pixelDx = dx * rect.width;
            float pixelDy = dy * rect.height;
            float length = Mathf.Sqrt(pixelDx * pixelDx + pixelDy * pixelDy);
            
            if (length > 0.001f)
            {
                float scale = TangentLineLength / length;
                pixelDx *= scale;
                pixelDy *= scale;
            }
            
            var keyPos = CurveToScreen(rect, key.time, key.value);
            return new Vector3(keyPos.x + pixelDx, keyPos.y - pixelDy, 0);
        }
        
        private void UpdateTangentFromMouse(Rect rect, Vector2 mousePos, bool isInTangent)
        {
            var key = curve.keys[selectedKeyIndex];
            var keyPos = CurveToScreen(rect, key.time, key.value);
            
            float dx = (mousePos.x - keyPos.x) / rect.width;
            float dy = -(mousePos.y - keyPos.y) / rect.height;
            
            if (Mathf.Abs(dx) > 0.001f)
            {
                float newTangent = dy / dx;
                newTangent = Mathf.Clamp(newTangent, -10f, 10f);
                
                if (isInTangent)
                    curve.MoveKey(selectedKeyIndex, new Keyframe(key.time, key.value, newTangent, key.outTangent));
                else
                    curve.MoveKey(selectedKeyIndex, new Keyframe(key.time, key.value, key.inTangent, newTangent));
            }
        }
        
        private void ApplyPreset(AnimationCurve preset)
        {
            curve = preset;
            selectedKeyIndex = -1;
            Repaint();
        }
        
        private static AnimationCurve CreateEaseIn()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),
                new Keyframe(1f, 0f, -2f, 0f));
        }
        
        private static AnimationCurve CreateEaseOut()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, -2f),
                new Keyframe(1f, 0f, 0f, 0f));
        }
        
        private static AnimationCurve CreateEaseInOut()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),
                new Keyframe(0.5f, 0.5f, -1f, -1f),
                new Keyframe(1f, 0f, 0f, 0f));
        }
        
        #endregion
        
        #region Lifecycle
        
        private void OnDestroy()
        {
            SaveWindowPosition();
            instance = null;
        }
        
        #endregion
    }
}
