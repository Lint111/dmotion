using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Manipulator that allows right-click dragging on empty space to create a cutting line.
    /// Any transition edges that the line crosses will be deleted when the mouse is released.
    /// </summary>
    internal class TransitionCutManipulator : MouseManipulator
    {
        private readonly AnimationStateMachineEditorView graphView;
        
        private VisualElement cutLine;
        private bool isDragging;
        private bool didDrag;
        private Vector2 startPosition;
        private Vector2 currentPosition;
        private const float DragThreshold = 5f;

        // Visual feedback
        private static readonly Color CutLineColor = new Color(1f, 0.3f, 0.3f, 0.9f);
        private static readonly Color CutLineHighlightColor = new Color(1f, 0.5f, 0.2f, 1f);
        
        private HashSet<Edge> crossedEdges = new HashSet<Edge>();

        public TransitionCutManipulator(AnimationStateMachineEditorView graphView)
        {
            this.graphView = graphView;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.RightMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<MouseCaptureOutEvent>(OnMouseCaptureOut);
            target.RegisterCallback<ContextualMenuPopulateEvent>(OnContextMenu, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<MouseCaptureOutEvent>(OnMouseCaptureOut);
            target.UnregisterCallback<ContextualMenuPopulateEvent>(OnContextMenu);
        }

        private void OnContextMenu(ContextualMenuPopulateEvent evt)
        {
            // Suppress context menu if we did a drag cut
            if (didDrag)
            {
                evt.StopImmediatePropagation();
            }
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;
            if (Application.isPlaying) return;

            // Only start if clicking on empty space (not on a node or edge)
            var clickedElement = evt.target as VisualElement;
            if (IsClickOnNodeOrEdge(clickedElement)) return;

            isDragging = true;
            didDrag = false;
            startPosition = evt.mousePosition;
            currentPosition = evt.mousePosition;
            
            target.CaptureMouse();
            evt.StopPropagation();
        }

        private bool IsClickOnNodeOrEdge(VisualElement element)
        {
            while (element != null && element != graphView)
            {
                if (element is Node || element is Edge || element is Port)
                    return true;
                element = element.parent;
            }
            return false;
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!isDragging) return;

            currentPosition = evt.mousePosition;
            var delta = currentPosition - startPosition;
            
            if (!didDrag && delta.magnitude > DragThreshold)
            {
                didDrag = true;
                CreateCutLine();
            }

            if (!didDrag) return;

            UpdateCutLine();
            UpdateCrossedEdges();

            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (!isDragging) return;
            if (!CanStopManipulation(evt)) return;

            bool wasActualDrag = didDrag;
            
            if (wasActualDrag && crossedEdges.Count > 0)
            {
                DeleteCrossedEdges();
            }

            EndDrag();
            
            if (wasActualDrag)
            {
                evt.StopImmediatePropagation();
            }
            else
            {
                didDrag = false;
                evt.StopPropagation();
            }
        }

        private void OnMouseCaptureOut(MouseCaptureOutEvent evt)
        {
            if (isDragging)
            {
                EndDrag();
            }
        }

        private void CreateCutLine()
        {
            if (cutLine != null) return;
            
            cutLine = new VisualElement();
            cutLine.name = "transition-cut-line";
            cutLine.pickingMode = PickingMode.Ignore;
            cutLine.style.position = Position.Absolute;
            cutLine.style.backgroundColor = CutLineColor;
            cutLine.style.height = 3;
            cutLine.style.transformOrigin = new TransformOrigin(0, Length.Percent(50));
            
            graphView.Add(cutLine);
        }

        private void UpdateCutLine()
        {
            if (cutLine == null) return;

            var startLocal = graphView.WorldToLocal(startPosition);
            var endLocal = graphView.WorldToLocal(currentPosition);
            
            var direction = endLocal - startLocal;
            var length = direction.magnitude;
            var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            cutLine.style.left = startLocal.x;
            cutLine.style.top = startLocal.y;
            cutLine.style.width = length;
            cutLine.style.rotate = new Rotate(angle);
            
            // Change color based on whether we're crossing any edges
            cutLine.style.backgroundColor = crossedEdges.Count > 0 ? CutLineHighlightColor : CutLineColor;
        }

        private void RemoveCutLine()
        {
            if (cutLine != null)
            {
                cutLine.RemoveFromHierarchy();
                cutLine = null;
            }
        }

        private void UpdateCrossedEdges()
        {
            // Clear previous highlights
            foreach (var edge in crossedEdges)
            {
                if (edge is TransitionEdge te)
                {
                    ClearEdgeHighlight(te);
                }
            }
            crossedEdges.Clear();

            var startLocal = graphView.contentViewContainer.WorldToLocal(startPosition);
            var endLocal = graphView.contentViewContainer.WorldToLocal(currentPosition);

            // Check all edges for intersection
            foreach (var element in graphView.graphElements)
            {
                if (element is TransitionEdge edge)
                {
                    if (LineIntersectsEdge(startLocal, endLocal, edge))
                    {
                        crossedEdges.Add(edge);
                        HighlightEdgeForDeletion(edge);
                    }
                }
            }
        }

        private void HighlightEdgeForDeletion(TransitionEdge edge)
        {
            // Red tint to indicate deletion
            if (edge.edgeControl is TransitionEdgeControl tec)
            {
                tec.ColorOverride = new Color(1f, 0.3f, 0.3f, 1f);
                tec.MarkDirtyRepaint();
            }
        }

        private void ClearEdgeHighlight(TransitionEdge edge)
        {
            // Reset to default color
            if (edge.edgeControl is TransitionEdgeControl tec)
            {
                tec.ColorOverride = null;
                tec.MarkDirtyRepaint();
            }
        }

        private bool LineIntersectsEdge(Vector2 lineStart, Vector2 lineEnd, TransitionEdge edge)
        {
            // Get the edge's start and end points
            var edgeControl = edge.edgeControl;
            if (edgeControl == null) return false;

            // The edge goes from output port to input port
            var outputNode = edge.output?.node;
            var inputNode = edge.input?.node;
            
            if (outputNode == null || inputNode == null) return false;

            // Get node center positions
            var outputRect = outputNode.GetPosition();
            var inputRect = inputNode.GetPosition();
            
            var edgeStart = new Vector2(outputRect.x + outputRect.width / 2, outputRect.y + outputRect.height / 2);
            var edgeEnd = new Vector2(inputRect.x + inputRect.width / 2, inputRect.y + inputRect.height / 2);

            return LinesIntersect(lineStart, lineEnd, edgeStart, edgeEnd);
        }

        /// <summary>
        /// Check if two line segments intersect.
        /// </summary>
        private bool LinesIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Direction(p3, p4, p1);
            float d2 = Direction(p3, p4, p2);
            float d3 = Direction(p1, p2, p3);
            float d4 = Direction(p1, p2, p4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            // Check collinear cases
            if (d1 == 0 && OnSegment(p3, p4, p1)) return true;
            if (d2 == 0 && OnSegment(p3, p4, p2)) return true;
            if (d3 == 0 && OnSegment(p1, p2, p3)) return true;
            if (d4 == 0 && OnSegment(p1, p2, p4)) return true;

            return false;
        }

        private float Direction(Vector2 pi, Vector2 pj, Vector2 pk)
        {
            return (pk.x - pi.x) * (pj.y - pi.y) - (pj.x - pi.x) * (pk.y - pi.y);
        }

        private bool OnSegment(Vector2 pi, Vector2 pj, Vector2 pk)
        {
            return Mathf.Min(pi.x, pj.x) <= pk.x && pk.x <= Mathf.Max(pi.x, pj.x) &&
                   Mathf.Min(pi.y, pj.y) <= pk.y && pk.y <= Mathf.Max(pi.y, pj.y);
        }

        private void DeleteCrossedEdges()
        {
            if (crossedEdges.Count == 0) return;

            // Record undo for all affected states
            var affectedStates = new HashSet<Object>();
            foreach (var edge in crossedEdges)
            {
                if (edge is TransitionEdge te)
                {
                    if (te.FromState != null)
                        affectedStates.Add(te.FromState);
                    
                    // Check if it's an Any State transition
                    if (te.output?.node is AnyStateNodeView)
                        affectedStates.Add(graphView.StateMachine);
                }
            }

            foreach (var state in affectedStates)
            {
                Undo.RecordObject(state, "Cut Transitions");
            }

            // Delete the edges through the graph view
            graphView.DeleteElements(crossedEdges);

            foreach (var state in affectedStates)
            {
                EditorUtility.SetDirty(state);
            }
        }

        private void EndDrag()
        {
            isDragging = false;
            
            // Clear edge highlights
            foreach (var edge in crossedEdges)
            {
                if (edge is TransitionEdge te)
                {
                    ClearEdgeHighlight(te);
                }
            }
            crossedEdges.Clear();
            
            RemoveCutLine();
            
            if (target.HasMouseCapture())
            {
                target.ReleaseMouse();
            }
        }
    }
}
