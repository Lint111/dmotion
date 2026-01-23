using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// IMGUI utility for drawing blend curve previews.
    /// Matches the visual style of CurvePreviewElement (UIToolkit) for consistency.
    /// </summary>
    internal static class BlendCurveDrawer
    {
        private const int CurveSegments = 30;
        private const float Padding = 4f;
        private const float DefaultHeight = 50f;
        
        // Use shared colors for consistency with UIToolkit CurvePreviewElement
        private static readonly Color BackgroundColor = PreviewEditorColors.DarkBackground;
        private static readonly Color CurveColor = PreviewEditorColors.CurveAccent;
        private static readonly Color LabelColor = PreviewEditorColors.DimText;
        private static readonly Color BorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color HintTextColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
        
        // Cached arrays and styles to avoid allocation during repaint
        private static readonly Vector3[] CachedCurvePoints = new Vector3[CurveSegments + 1];
        private static GUIStyle _labelStyle;
        private static GUIStyle _hintStyle;
        
        // Cached default curve for comparison
        private static AnimationCurve _defaultLinearCurve;
        private static AnimationCurve DefaultLinearCurve
        {
            get
            {
                if (_defaultLinearCurve == null)
                    _defaultLinearCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                return _defaultLinearCurve;
            }
        }
        
        /// <summary>
        /// Draws a blend curve field with preview. Returns true if the curve was modified.
        /// </summary>
        /// <param name="position">The rect to draw in.</param>
        /// <param name="property">The SerializedProperty for the AnimationCurve.</param>
        /// <param name="label">The label to display.</param>
        /// <returns>True if the curve was modified.</returns>
        public static bool DrawCurveField(Rect position, SerializedProperty property, GUIContent label)
        {
            // Draw label
            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);
            
            // Draw curve preview area
            var curveRect = new Rect(
                position.x + EditorGUIUtility.labelWidth + 2,
                position.y,
                position.width - EditorGUIUtility.labelWidth - 2,
                DefaultHeight);
            
            var curve = property.animationCurveValue ?? DefaultLinearCurve;
            
            // Draw the curve preview
            DrawCurvePreview(curveRect, curve);
            
            // Handle click to open editor
            if (Event.current.type == EventType.MouseDown && 
                Event.current.button == 0 && 
                curveRect.Contains(Event.current.mousePosition))
            {
                BlendCurveEditorWindow.Show(
                    curve,
                    newCurve =>
                    {
                        property.serializedObject.Update();
                        property.animationCurveValue = newCurve;
                        property.serializedObject.ApplyModifiedProperties();
                    },
                    GUIUtility.GUIToScreenRect(curveRect));
                
                Event.current.Use();
            }
            
            // Change cursor on hover
            EditorGUIUtility.AddCursorRect(curveRect, MouseCursor.Link);
            
            return false;
        }
        
        /// <summary>
        /// Draws a blend curve field using layout. Returns true if modified.
        /// </summary>
        public static bool DrawCurveFieldLayout(SerializedProperty property, GUIContent label)
        {
            var rect = EditorGUILayout.GetControlRect(true, DefaultHeight);
            return DrawCurveField(rect, property, label);
        }
        
        /// <summary>
        /// Gets the height needed for the curve field.
        /// </summary>
        public static float GetPropertyHeight()
        {
            return DefaultHeight;
        }
        
        /// <summary>
        /// Draws the curve preview visualization.
        /// </summary>
        private static void DrawCurvePreview(Rect rect, AnimationCurve curve)
        {
            // Background
            EditorGUI.DrawRect(rect, BackgroundColor);
            
            // Border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), BorderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), BorderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), BorderColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), BorderColor);
            
            if (curve == null || curve.length == 0)
            {
                curve = DefaultLinearCurve;
            }
            
            // Draw curve using Handles - use cached array to avoid allocation
            var curveWidth = rect.width - Padding * 2;
            var curveHeight = rect.height - Padding * 2;
            
            Handles.BeginGUI();
            Handles.color = CurveColor;
            
            for (int i = 0; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float value = curve.Evaluate(t);
                float x = rect.x + Padding + t * curveWidth;
                float y = rect.y + Padding + (1f - value) * curveHeight;
                CachedCurvePoints[i] = new Vector3(x, y, 0);
            }
            
            Handles.DrawAAPolyLine(2f, CachedCurvePoints);
            Handles.EndGUI();
            
            // Draw labels - use cached style to avoid allocation
            _labelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = LabelColor },
                fontSize = 9
            };
            
            var fromRect = new Rect(rect.x + Padding, rect.y + Padding, 30, 12);
            var toRect = new Rect(rect.xMax - Padding - 20, rect.yMax - Padding - 12, 20, 12);
            
            GUI.Label(fromRect, "From", _labelStyle);
            GUI.Label(toRect, "To", _labelStyle);
            
            // Draw "Click to edit" hint when hovering - use cached style
            if (rect.Contains(Event.current.mousePosition))
            {
                _hintStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    normal = { textColor = HintTextColor }
                };
                var hintRect = new Rect(rect.x, rect.y + rect.height / 2 - 8, rect.width, 16);
                GUI.Label(hintRect, "Click to edit", _hintStyle);
            }
        }
    }
}
