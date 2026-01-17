using System;
using System.Linq;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Manipulator that allows right-click dragging from a node to create transitions.
    /// Shows a visual line while dragging and highlights valid drop targets.
    /// Works with both StateNodeView and AnyStateNodeView.
    /// </summary>
    internal class TransitionDragManipulator : MouseManipulator
    {
        private readonly AnimationStateMachineEditorView graphView;
        private readonly Node sourceNode;
        private readonly Func<AnimationStateAsset, bool> isValidTarget;
        private readonly Action<AnimationStateAsset> createTransition;
        
        private VisualElement dragLine;
        private StateNodeView hoveredNode;
        private bool isDragging;
        private bool didDrag;
        private Vector2 startMousePosition;
        private const float DragThreshold = 5f;

        // Visual feedback colors
        private static readonly Color ValidConnectionColor = new Color(0.3f, 0.8f, 0.3f, 1f);
        private static readonly Color InvalidConnectionColor = new Color(0.8f, 0.3f, 0.3f, 1f);
        private static readonly Color DragLineColor = new Color(1f, 1f, 1f, 0.8f);

        /// <summary>
        /// Creates a manipulator for dragging transitions from a state node.
        /// </summary>
        public static TransitionDragManipulator ForStateNode(StateNodeView node, AnimationStateMachineEditorView view)
        {
            return new TransitionDragManipulator(
                node,
                view,
                targetState => 
                {
                    // Cannot connect to self
                    if (targetState == node.State) return false;
                    // Cannot create duplicate transition
                    return !node.State.OutTransitions.Any(t => t.ToState == targetState);
                },
                targetState => view.CreateTransitionBetweenStates(node.State, targetState)
            );
        }

        /// <summary>
        /// Creates a manipulator for dragging transitions from the Any State node.
        /// </summary>
        public static TransitionDragManipulator ForAnyStateNode(AnyStateNodeView node, AnimationStateMachineEditorView view, StateMachineAsset stateMachine)
        {
            return new TransitionDragManipulator(
                node,
                view,
                targetState => 
                {
                    // Cannot create duplicate Any State transition
                    return !stateMachine.AnyStateTransitions.Any(t => t.ToState == targetState);
                },
                targetState => view.CreateAnyStateTransitionTo(targetState)
            );
        }

        private TransitionDragManipulator(
            Node sourceNode,
            AnimationStateMachineEditorView graphView,
            Func<AnimationStateAsset, bool> isValidTarget,
            Action<AnimationStateAsset> createTransition)
        {
            this.sourceNode = sourceNode;
            this.graphView = graphView;
            this.isValidTarget = isValidTarget;
            this.createTransition = createTransition;
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
            if (didDrag)
            {
                evt.StopImmediatePropagation();
            }
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;
            if (Application.isPlaying) return;

            isDragging = true;
            didDrag = false;
            startMousePosition = evt.mousePosition;
            
            target.CaptureMouse();
            evt.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!isDragging) return;

            var delta = evt.mousePosition - startMousePosition;
            if (!didDrag && delta.magnitude > DragThreshold)
            {
                didDrag = true;
                CreateDragLine();
            }

            if (!didDrag) return;

            UpdateDragLine(evt.mousePosition);
            
            var newHoveredNode = GetStateNodeAtPosition(evt.mousePosition);
            
            if (newHoveredNode != hoveredNode)
            {
                ClearHoverHighlight();
                hoveredNode = newHoveredNode;
                
                if (hoveredNode != null)
                {
                    ApplyHoverHighlight(isValidTarget(hoveredNode.State));
                }
            }
            
            UpdateDragLineColor(hoveredNode != null && isValidTarget(hoveredNode.State));

            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (!isDragging) return;
            if (!CanStopManipulation(evt)) return;

            bool wasActualDrag = didDrag;
            
            if (wasActualDrag)
            {
                var targetNode = GetStateNodeAtPosition(evt.mousePosition);
                
                if (targetNode != null && isValidTarget(targetNode.State))
                {
                    createTransition(targetNode.State);
                }
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

        private void CreateDragLine()
        {
            if (dragLine != null) return;
            
            dragLine = new VisualElement();
            dragLine.name = "transition-drag-line";
            dragLine.pickingMode = PickingMode.Ignore;
            dragLine.style.position = Position.Absolute;
            dragLine.style.backgroundColor = DragLineColor;
            dragLine.style.height = 2;
            dragLine.style.transformOrigin = new TransformOrigin(0, Length.Percent(50));
            
            graphView.contentViewContainer.Add(dragLine);
        }

        private void UpdateDragLine(Vector2 mousePosition)
        {
            if (dragLine == null) return;

            var nodeRect = sourceNode.GetPosition();
            var startPos = new Vector2(nodeRect.x + nodeRect.width / 2, nodeRect.y + nodeRect.height / 2);
            var endPos = graphView.contentViewContainer.WorldToLocal(mousePosition);
            
            if (hoveredNode != null && isValidTarget(hoveredNode.State))
            {
                var targetRect = hoveredNode.GetPosition();
                endPos = new Vector2(targetRect.x + targetRect.width / 2, targetRect.y + targetRect.height / 2);
            }
            
            var direction = endPos - startPos;
            var length = direction.magnitude;
            var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            dragLine.style.left = startPos.x;
            dragLine.style.top = startPos.y;
            dragLine.style.width = length;
            dragLine.style.rotate = new Rotate(angle);
        }

        private void UpdateDragLineColor(bool isValid)
        {
            if (dragLine == null) return;
            dragLine.style.backgroundColor = isValid ? ValidConnectionColor : DragLineColor;
        }

        private void RemoveDragLine()
        {
            if (dragLine != null)
            {
                dragLine.RemoveFromHierarchy();
                dragLine = null;
            }
        }

        private StateNodeView GetStateNodeAtPosition(Vector2 mousePosition)
        {
            var localPos = graphView.contentViewContainer.WorldToLocal(mousePosition);
            
            foreach (var element in graphView.graphElements)
            {
                if (element is StateNodeView nodeView)
                {
                    var nodeRect = nodeView.GetPosition();
                    if (nodeRect.Contains(localPos))
                    {
                        return nodeView;
                    }
                }
            }
            
            return null;
        }

        private void ApplyHoverHighlight(bool isValid)
        {
            if (hoveredNode == null) return;
            
            var color = isValid ? ValidConnectionColor : InvalidConnectionColor;
            hoveredNode.style.borderBottomColor = color;
            hoveredNode.style.borderTopColor = color;
            hoveredNode.style.borderLeftColor = color;
            hoveredNode.style.borderRightColor = color;
            hoveredNode.style.borderBottomWidth = 2;
            hoveredNode.style.borderTopWidth = 2;
            hoveredNode.style.borderLeftWidth = 2;
            hoveredNode.style.borderRightWidth = 2;
        }

        private void ClearHoverHighlight()
        {
            if (hoveredNode == null) return;
            
            hoveredNode.style.borderBottomColor = StyleKeyword.Null;
            hoveredNode.style.borderTopColor = StyleKeyword.Null;
            hoveredNode.style.borderLeftColor = StyleKeyword.Null;
            hoveredNode.style.borderRightColor = StyleKeyword.Null;
            hoveredNode.style.borderBottomWidth = StyleKeyword.Null;
            hoveredNode.style.borderTopWidth = StyleKeyword.Null;
            hoveredNode.style.borderLeftWidth = StyleKeyword.Null;
            hoveredNode.style.borderRightWidth = StyleKeyword.Null;
        }

        private void EndDrag()
        {
            isDragging = false;
            ClearHoverHighlight();
            RemoveDragLine();
            hoveredNode = null;
            
            if (target.HasMouseCapture())
            {
                target.ReleaseMouse();
            }
        }
    }
}
