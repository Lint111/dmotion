using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    //TODO: This class needs some clean up pass. We need a Vector2.Rotate function and stop using 4x4 matrixes. Control aren't quite centered as well
    public class TransitionEdgeControl : EdgeControl
    {
        // Self-loop geometry constants - shared with TransitionCutManipulator for hit detection
        internal const float SelfLoopWidth = 50f;       // How far right the loop extends from port
        internal const float SelfLoopHeight = 70f;      // How far up the loop goes from port
        internal const float SelfLoopArrowEntry = 25f;  // How far down from top the arrow enters
        internal TransitionEdge Edge;
        
        /// <summary>
        /// Optional color override. When set, uses this instead of Edge.defaultColor.
        /// Set to null to use default color.
        /// </summary>
        internal Color? ColorOverride;

        //rectangle for line + 3 triangle for arrow
        private Vertex[] vertices = new Vertex[4 + 3*3];
        
        // Self-loop: 4 segments = 4 quads (16 verts) + 1 arrow (3 verts)
        private Vertex[] selfLoopVertices = new Vertex[16 + 3];

        static ushort[] indices =
        {
            //rectangle (line)
            0, 1, 2, 2, 3, 0,
            //arrows
            4, 5, 6,
            7, 8, 9,
            10, 11, 12
        };
        
        static ushort[] selfLoopIndices =
        {
            // 4 quads for the loop segments
            0, 1, 2, 2, 3, 0,       // segment 1: right (horizontal)
            4, 5, 6, 6, 7, 4,       // segment 2: up (vertical)
            8, 9, 10, 10, 11, 8,    // segment 3: left (horizontal)
            12, 13, 14, 14, 15, 12, // segment 4: down (vertical)
            // 1 arrow
            16, 17, 18
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

        private bool isSelfLoop;
        
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
                isSelfLoop = v.magnitude < 1f; // Self-transition detection
                
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
            
            if (isSelfLoop)
            {
                for (var i = 0; i < selfLoopVertices.Length; i++)
                {
                    selfLoopVertices[i].tint = tintColor;
                }
                var mwd = mgc.Allocate(selfLoopVertices.Length, selfLoopIndices.Length);
                mwd.SetAllVertices(selfLoopVertices);
                mwd.SetAllIndices(selfLoopIndices);
            }
            else
            {
                for (var i = 0; i < vertices.Length; i++)
                {
                    vertices[i].tint = tintColor;
                }
                var mwd = mgc.Allocate(vertices.Length, indices.Length);
                mwd.SetAllVertices(vertices);
                mwd.SetAllIndices(indices);
            }
        }

        private void DrawSelfLoop(Vector2 portLocal)
        {
            // portLocal is the port position in edge-local coordinates
            // For self-loop, we draw: right -> up -> left -> down -> arrow
            
            var hw = edgeWidth * 0.5f;  // half width
            
            // Key points (forming a rectangle loop to the right and above the port):
            var p0 = portLocal;                                                        // Start at port
            var p1 = portLocal + new Vector2(SelfLoopWidth, 0);                        // Right
            var p2 = portLocal + new Vector2(SelfLoopWidth, -SelfLoopHeight);          // Top-right
            var p3 = portLocal + new Vector2(0, -SelfLoopHeight);                      // Top-left
            var p4 = portLocal + new Vector2(0, -SelfLoopHeight + SelfLoopArrowEntry); // Arrow entry
            
            // Each quad: bottom-left, top-left, top-right, bottom-right (matching normal edge winding)
            // Indices: 0,1,2, 2,3,0
            
            // Segment 1: P0 to P1 (horizontal, going right)
            // "bottom" = +y, "top" = -y for horizontal line
            selfLoopVertices[0].position = new Vector3(p0.x, p0.y + hw, 0);  // bottom-left
            selfLoopVertices[1].position = new Vector3(p0.x, p0.y - hw, 0);  // top-left
            selfLoopVertices[2].position = new Vector3(p1.x, p1.y - hw, 0);  // top-right
            selfLoopVertices[3].position = new Vector3(p1.x, p1.y + hw, 0);  // bottom-right
            
            // Segment 2: P1 to P2 (vertical, going up)
            // "bottom" = +x, "top" = -x for vertical line going up
            selfLoopVertices[4].position = new Vector3(p1.x + hw, p1.y, 0);  // bottom-left
            selfLoopVertices[5].position = new Vector3(p1.x - hw, p1.y, 0);  // top-left
            selfLoopVertices[6].position = new Vector3(p2.x - hw, p2.y, 0);  // top-right
            selfLoopVertices[7].position = new Vector3(p2.x + hw, p2.y, 0);  // bottom-right
            
            // Segment 3: P2 to P3 (horizontal, going left)
            selfLoopVertices[8].position = new Vector3(p2.x, p2.y - hw, 0);   // bottom-left
            selfLoopVertices[9].position = new Vector3(p2.x, p2.y + hw, 0);   // top-left
            selfLoopVertices[10].position = new Vector3(p3.x, p3.y + hw, 0);  // top-right
            selfLoopVertices[11].position = new Vector3(p3.x, p3.y - hw, 0);  // bottom-right
            
            // Segment 4: P3 to P4 (vertical, going down)
            selfLoopVertices[12].position = new Vector3(p3.x - hw, p3.y, 0);  // bottom-left
            selfLoopVertices[13].position = new Vector3(p3.x + hw, p3.y, 0);  // top-left
            selfLoopVertices[14].position = new Vector3(p4.x + hw, p4.y, 0);  // top-right
            selfLoopVertices[15].position = new Vector3(p4.x - hw, p4.y, 0);  // bottom-right
            
            // Update bounds for hit testing
            edgeLtw = Matrix4x4.TRS(portLocal, Quaternion.identity, Vector3.one);
            edgeInverseLtw = edgeLtw.inverse;
            localRect[0] = p0;
            localRect[1] = p1;
            localRect[2] = p2;
            localRect[3] = p3;

            // Arrow pointing down into the node
            const float arrowHalfHeight = 8f;
            const float arrowHalfWidth = 7f;
            
            // Arrow vertices - pointing down
            selfLoopVertices[16].position = new Vector3(p4.x - arrowHalfWidth, p4.y - arrowHalfHeight, 0);
            selfLoopVertices[17].position = new Vector3(p4.x + arrowHalfWidth, p4.y - arrowHalfHeight, 0);
            selfLoopVertices[18].position = new Vector3(p4.x, p4.y + arrowHalfHeight, 0);
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