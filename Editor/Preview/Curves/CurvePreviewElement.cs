using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// UIToolkit element for displaying an animation curve preview.
    /// Uses Painter2D for drawing instead of IMGUI.
    /// </summary>
    [UxmlElement]
    internal partial class CurvePreviewElement : VisualElement
    {
        #region Constants
        
        private const int CurveSegments = 30;
        private const float Padding = 4f;
        
        private static readonly Color BackgroundColor = PreviewEditorColors.DarkBackground;
        private static readonly Color CurveColor = PreviewEditorColors.CurveAccent;
        // Note: Label color is now defined in AnimationPreviewWindow.uss (.curve-preview__label)
        
        #endregion
        
        #region State
        
        private AnimationCurve curve;
        private Action<AnimationCurve> onCurveChanged;
        
        // Labels (UIToolkit elements since Painter2D can't draw text)
        private Label fromLabel;
        private Label toLabel;
        
        #endregion
        
        #region Properties
        
        /// <summary>The animation curve to display.</summary>
        public AnimationCurve Curve
        {
            get => curve;
            set
            {
                curve = value ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
                MarkDirtyRepaint();
            }
        }
        
        #endregion
        
        #region Constructor
        
        public CurvePreviewElement()
        {
            AddToClassList("curve-preview");
            
            // Enable custom drawing
            generateVisualContent += OnGenerateVisualContent;
            
            // Enable click handling
            focusable = true;
            pickingMode = PickingMode.Position;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            
            // Default curve
            curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            
            // Create labels
            CreateLabels();
            
            tooltip = "Click to edit curve";
        }
        
        private void CreateLabels()
        {
            fromLabel = new Label("From");
            fromLabel.AddToClassList("curve-preview__label");
            fromLabel.AddToClassList("curve-preview__label--from");
            fromLabel.pickingMode = PickingMode.Ignore;
            Add(fromLabel);
            
            toLabel = new Label("To");
            toLabel.AddToClassList("curve-preview__label");
            toLabel.AddToClassList("curve-preview__label--to");
            toLabel.pickingMode = PickingMode.Ignore;
            Add(toLabel);
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Sets the callback for when the curve is edited.
        /// </summary>
        public void SetOnCurveChanged(Action<AnimationCurve> callback)
        {
            onCurveChanged = callback;
        }
        
        #endregion
        
        #region Drawing
        
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width < 10 || rect.height < 10) return;
            
            var painter = ctx.painter2D;
            
            // Background
            painter.fillColor = BackgroundColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, 0));
            painter.LineTo(new Vector2(rect.width, 0));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0, rect.height));
            painter.ClosePath();
            painter.Fill();
            
            // Draw curve
            DrawCurve(painter, rect);
        }
        
        private void DrawCurve(Painter2D painter, Rect rect)
        {
            if (curve == null) return;
            
            float curveWidth = rect.width - Padding * 2;
            float curveHeight = rect.height - Padding * 2;
            
            painter.strokeColor = CurveColor;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            
            for (int i = 0; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float value = curve.Evaluate(t);
                float x = Padding + t * curveWidth;
                float y = Padding + (1f - value) * curveHeight;
                
                if (i == 0)
                    painter.MoveTo(new Vector2(x, y));
                else
                    painter.LineTo(new Vector2(x, y));
            }
            
            painter.Stroke();
        }
        
        #endregion
        
        #region Event Handling
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            
            // Open curve editor window
            BlendCurveEditorWindow.Show(
                curve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f),
                newCurve =>
                {
                    curve = newCurve;
                    onCurveChanged?.Invoke(newCurve);
                    MarkDirtyRepaint();
                },
                worldBound);
            
            evt.StopPropagation();
        }
        
        #endregion
    }
}
