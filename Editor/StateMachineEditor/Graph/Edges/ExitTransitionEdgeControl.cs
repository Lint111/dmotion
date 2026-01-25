using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Custom edge control for exit transitions with distinctive orange appearance.
    /// Uses the same rendering as TransitionEdgeControl but with orange color.
    /// </summary>
    public class ExitTransitionEdgeControl : EdgeControl
    {
        internal TransitionEdge Edge;
        
        // Exit transition color (orange)
        private static readonly Color ExitTransitionColor = new Color(1f, 0.6f, 0.1f, 1f);

        // Rectangle for line + 3 triangles for arrow
        private Vertex[] vertices = new Vertex[4 + 3 * 3];

        private static readonly ushort[] indices =
        {
            // Rectangle (line)
            0, 1, 2, 2, 3, 0,
            // Arrows
            4, 5, 6,
            7, 8, 9,
            10, 11, 12
        };

        private Vector2[] localRect = new Vector2[4];
        private Matrix4x4 edgeLtw;
        private Matrix4x4 edgeInverseLtw;
        private bool isDirty = true;
        private Vector2 TopLeft => localRect[1];
        private Vector2 BottomRight => localRect[3];

        public ExitTransitionEdgeControl()
        {
            generateVisualContent = DrawEdge;
        }

        private void DrawEdge(MeshGenerationContext mgc)
        {
            if (edgeWidth <= 0)
            {
                return;
            }

            if (isDirty)
            {
                isDirty = false;
                var fromLocal = parent.ChangeCoordinatesTo(this, from);
                var toLocal = parent.ChangeCoordinatesTo(this, to);

                var v = toLocal - fromLocal;
                var angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                var lineStart = new Vector2(fromLocal.x, fromLocal.y);

                if (!Edge.isGhostEdge && Edge.input != null && Edge.output != null)
                {
                    // Shift lines on perpendicular direction so reversed transitions don't overlap
                    const float shiftAmount = 8f;
                    var shiftDir = ((Vector2)(Quaternion.Euler(0, 0, 90) * v)).normalized;
                    lineStart += shiftDir * shiftAmount;
                }

                // Set line vertices
                {
                    edgeLtw = Matrix4x4.TRS(lineStart, Quaternion.Euler(0, 0, angle), Vector3.one);
                    edgeInverseLtw = edgeLtw.inverse;
                    var left = 0;
                    var right = (toLocal - fromLocal).magnitude;
                    var top = -edgeWidth * 0.5f;
                    var bottom = edgeWidth * 0.5f;

                    localRect[0] = new Vector2(left, bottom);
                    localRect[1] = new Vector2(left, top);
                    localRect[2] = new Vector2(right, top);
                    localRect[3] = new Vector2(right, bottom);
                    for (var i = 0; i < 4; i++)
                    {
                        vertices[i].position = edgeLtw.MultiplyPoint3x4(localRect[i]);
                    }
                }

                // Set arrow vertices (1 transition = 1 arrow, >1 transitions = 3 arrows)
                {
                    const float arrowHalfHeight = 8f;
                    const float arrowHalfWidth = 7f;
                    var arrowOffset = Edge.Model.TransitionCount > 1 ? arrowHalfHeight * 2 : 0;
                    for (var i = 0; i < 3; i++)
                    {
                        var arrowIndex = 4 + 3 * i;
                        var offset = arrowOffset * i;
                        var midPoint = lineStart + v * 0.5f + v.normalized * offset;
                        var arrowLtw = Matrix4x4.TRS(midPoint, Quaternion.Euler(0, 0, angle - 90f), Vector3.one);
                        vertices[arrowIndex].position = arrowLtw.MultiplyPoint3x4(
                            new Vector2(-arrowHalfWidth, -arrowHalfHeight));
                        vertices[arrowIndex + 1].position = arrowLtw.MultiplyPoint3x4(
                            new Vector2(arrowHalfWidth, -arrowHalfHeight));
                        vertices[arrowIndex + 2].position = arrowLtw.MultiplyPoint3x4(
                            new Vector2(0, arrowHalfHeight));
                    }
                }
            }

            // Use orange color for exit transitions
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i].tint = ExitTransitionColor;
            }

            var mwd = mgc.Allocate(vertices.Length, indices.Length);
            mwd.SetAllVertices(vertices);
            mwd.SetAllIndices(indices);
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            var local = edgeInverseLtw.MultiplyPoint3x4(localPoint);
            const float expand = 5f;
            var rect = new Rect(Vector2.zero, (BottomRight - TopLeft) * expand);
            return rect.Contains(local);
        }

        protected override void ComputeControlPoints()
        {
            base.ComputeControlPoints();
            var v = to - from;
            const float boxExtent = 10f;
            var pv = ((Vector2)(Quaternion.Euler(0, 0, 90) * v)).normalized * boxExtent;

            controlPoints[0] = from - pv;
            controlPoints[1] = from + pv;
            controlPoints[2] = to - pv;
            controlPoints[3] = to + pv;
        }

        protected override void PointsChanged()
        {
            base.PointsChanged();
            isDirty = true;
        }

        public override void UpdateLayout()
        {
            base.UpdateLayout();
            isDirty = true;
        }
    }
}