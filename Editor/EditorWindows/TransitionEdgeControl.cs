using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    //TODO: This class needs some clean up pass. We need a Vector2.Rotate function and stop using 4x4 matrixes. Control aren't quite centered as well
    public class TransitionEdgeControl : EdgeControl
    {
        internal TransitionEdge Edge;
        
        /// <summary>
        /// Optional color override. When set, uses this instead of Edge.defaultColor.
        /// Set to null to use default color.
        /// </summary>
        internal Color? ColorOverride;

        //rectangle for line + 3 triangle for arrow
        private Vertex[] vertices = new Vertex[4 + 3*3];

        static ushort[] indices =
        {
            //rectangle (line)
            0, 1, 2, 2, 3, 0,
            //arrows
            4, 5, 6,
            7, 8, 9,
            10, 11, 12
        };

        private Vector2[] localRect = new Vector2[4];

        private Matrix4x4 edgeLtw;
        private Matrix4x4 edgeInverseLtw;
        protected bool isDirty = true;
        private Vector2 TopLeft => localRect[1];
        private Vector2 BottomRight => localRect[3];

        public TransitionEdgeControl()
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
                var isSelfLoop = v.magnitude < 1f; // Self-transition detection
                
                if (isSelfLoop)
                {
                    DrawSelfLoop(fromLocal);
                }
                else
                {
                    DrawNormalEdge(fromLocal, toLocal, v);
                }
            }

            var tintColor = ColorOverride ?? Edge.defaultColor;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i].tint = tintColor;
            }

            var mwd = mgc.Allocate(vertices.Length, indices.Length);
            mwd.SetAllVertices(vertices);
            mwd.SetAllIndices(indices);
        }

        private void DrawSelfLoop(Vector2 nodeCenter)
        {
            // Self-loop: Circular arc from right edge to top edge of the node
            // Arc goes: right edge -> curves up-right -> top edge (with arrow pointing into top)
            const float nodeHalfWidth = 75f;  // Approximate half-width of node
            const float nodeHalfHeight = 25f; // Approximate half-height of node
            const float arcRadius = 35f;      // Radius of the circular arc
            
            // Start point: middle of right edge
            var startPoint = nodeCenter + new Vector2(nodeHalfWidth, 0);
            // End point: middle of top edge  
            var endPoint = nodeCenter + new Vector2(0, -nodeHalfHeight);
            // Arc center: offset to upper-right to create the curve
            var arcCenter = nodeCenter + new Vector2(nodeHalfWidth, -nodeHalfHeight);
            
            // We approximate the arc with the 4 line vertices as a quadratic bezier-ish curve
            // Control point for the curve (upper-right corner area)
            var controlPoint = arcCenter + new Vector2(arcRadius * 0.7f, -arcRadius * 0.7f);
            
            var halfWidth = edgeWidth * 0.5f;
            
            // Calculate points along the curve (using quadratic interpolation)
            var p0 = startPoint;
            var p1 = controlPoint;
            var p2 = endPoint;
            
            // Get tangent at start for perpendicular offset
            var tangentStart = (p1 - p0).normalized;
            var perpStart = new Vector2(-tangentStart.y, tangentStart.x) * halfWidth;
            
            // Get tangent at end for perpendicular offset
            var tangentEnd = (p2 - p1).normalized;
            var perpEnd = new Vector2(-tangentEnd.y, tangentEnd.x) * halfWidth;
            
            // Create quad vertices for a thick curve (simplified as trapezoid)
            vertices[0].position = p0 + perpStart;
            vertices[1].position = p0 - perpStart;
            vertices[2].position = p2 - perpEnd;
            vertices[3].position = p2 + perpEnd;
            
            edgeLtw = Matrix4x4.TRS(nodeCenter, Quaternion.identity, Vector3.one);
            edgeInverseLtw = edgeLtw.inverse;
            localRect[0] = vertices[0].position;
            localRect[1] = vertices[1].position;
            localRect[2] = vertices[2].position;
            localRect[3] = vertices[3].position;

            // Arrow pointing down into the top edge of the node
            const float arrowHalfHeight = 8f;
            const float arrowHalfWidth = 7f;
            
            // Position arrow at end point (top edge), pointing down
            var arrowAngle = 90f; // Point down (into the node)
            
            var arrowOffset = Edge.Model.TransitionCount > 1 ? arrowHalfHeight * 2 : 0;
            for (var i = 0; i < 3; i++)
            {
                var arrowIndex = 4 + 3 * i;
                var offset = new Vector2(arrowOffset * (i - 1), 0);
                var arrowPos = endPoint + offset;
                var arrowLtw = Matrix4x4.TRS(arrowPos, Quaternion.Euler(0, 0, arrowAngle - 90f), Vector3.one);
                vertices[arrowIndex].position = arrowLtw.MultiplyPoint3x4(
                    new Vector2(-arrowHalfWidth, -arrowHalfHeight));
                vertices[arrowIndex + 1].position = arrowLtw.MultiplyPoint3x4(
                    new Vector2(arrowHalfWidth, -arrowHalfHeight));
                vertices[arrowIndex + 2].position = arrowLtw.MultiplyPoint3x4(
                    new Vector2(0, arrowHalfHeight));
            }
        }

        private void DrawNormalEdge(Vector2 fromLocal, Vector2 toLocal, Vector2 v)
        {
            var angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            var lineStart = new Vector2(fromLocal.x, fromLocal.y);

            if (!Edge.isGhostEdge && Edge.input != null && Edge.output != null)
            {
                //We shift the lines on their perpendicular direction. This is reversed transitions (i.e A -> B and B -> A) don't overlap
                {
                    const float shiftAmount = 8f;
                    var shiftDir = ((Vector2)(Quaternion.Euler(0, 0, 90) * v)).normalized;
                    lineStart += shiftDir * shiftAmount;
                }
            }

            //Set line vertices
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

            //Set arrow vertices (1 transition = 1 arrow, >1 transitions = 3 arrows)
            {
                const float arrowHalfHeight = 8f;
                const float arrowHalfWidth = 7f;
                var arrowOffset = Edge.Model.TransitionCount > 1 ? arrowHalfHeight * 2 : 0;
                for (var i = 0; i < 3; i++)
                {
                    var arrowIndex = 4 + 3 * i;
                    var offset = arrowOffset * i;
                    var midPoint = lineStart + v * 0.5f + v.normalized*offset;
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
            var pv = ((Vector2) (Quaternion.Euler(0, 0, 90) * v)).normalized * boxExtent;

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