using System;
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
        private readonly Action onDropOnExitNode; // Optional action for exit node drops
        private readonly Func<bool> isExitValid; // Optional check if exit drop is valid
        
        private VisualElement dragLine;
        private StateNodeView hoveredNode;
        private ExitNodeView hoveredExitNode;
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
                    // Self-transitions are allowed (e.g., re-trigger attack, reset idle)
                    // Cannot create duplicate transition to the same target
                    return !HasTransitionToState(node.State.OutTransitions, targetState);
                },
                targetState => view.CreateTransitionBetweenStates(node.State, targetState),
                () => view.AddExitStateFromDrag(node.State), // Exit node drop action
                () => !view.IsExitState(node.State) // Can't add if already an exit state
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
                    return !HasTransitionToState(stateMachine.AnyStateTransitions, targetState);
                },
                targetState => view.CreateAnyStateTransitionTo(targetState),
                () => view.CreateAnyStateExitTransition(), // Any State -> Exit for conditional exits
                () => view.CanCreateAnyStateExitTransition() // Can't add if exit transition already exists
            );
        }
        
        /// <summary>
        /// Checks if any transition in the list targets the specified state.
        /// Replaces LINQ .Any() to avoid allocations.
        /// </summary>
        private static bool HasTransitionToState(System.Collections.Generic.List<StateOutTransition> transitions, AnimationStateAsset targetState)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                if (transitions[i].ToState == targetState)
                    return true;
            }
            return false;
        }

        private TransitionDragManipulator(
            Node sourceNode,
            AnimationStateMachineEditorView graphView,
            Func<AnimationStateAsset, bool> isValidTarget,
            Action<AnimationStateAsset> createTransition,
            Action onDropOnExitNode,
            Func<bool> isExitValid)
        {
            this.sourceNode = sourceNode;
            this.graphView = graphView;
            this.isValidTarget = isValidTarget;
            this.createTransition = createTransition;
            this.onDropOnExitNode = onDropOnExitNode;
            this.isExitValid = isExitValid;
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
            var newHoveredExit = onDropOnExitNode != null ? GetExitNodeAtPosition(evt.mousePosition) : null;
            
            if (newHoveredNode != hoveredNode || newHoveredExit != hoveredExitNode)
            {
                ClearHoverHighlight();
                hoveredNode = newHoveredNode;
                hoveredExitNode = newHoveredExit;
                
                if (hoveredNode != null)
                {
                    ApplyHoverHighlight(isValidTarget(hoveredNode.State));
                }
                else if (hoveredExitNode != null)
                {
                    bool exitValid = isExitValid == null || isExitValid();
                    ApplyExitNodeHighlight(exitValid);
                }
            }
            
            bool exitIsValid = hoveredExitNode != null && (isExitValid == null || isExitValid());
            bool isValidDrop = (hoveredNode != null && isValidTarget(hoveredNode.State)) || exitIsValid;
            UpdateDragLineColor(isValidDrop);

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
                var targetExit = onDropOnExitNode != null ? GetExitNodeAtPosition(evt.mousePosition) : null;
                
                if (targetNode != null && isValidTarget(targetNode.State))
                {
                    createTransition(targetNode.State);
                }
                else if (targetExit != null && (isExitValid == null || isExitValid()))
                {
                    onDropOnExitNode();
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
            dragLine.style.height = 3;
            dragLine.style.transformOrigin = new TransformOrigin(0, Length.Percent(50));
            
            // Add directly to graph view (not contentViewContainer) - same as TransitionCutManipulator
            graphView.Add(dragLine);
        }

        private void UpdateDragLine(Vector2 mousePosition)
        {
            if (dragLine == null) return;

            // Get source node center in world coordinates, then convert to graphView local
            var nodeRect = sourceNode.GetPosition();
            var nodeCenterContent = new Vector2(nodeRect.x + nodeRect.width / 2, nodeRect.y + nodeRect.height / 2);
            var nodeCenterWorld = graphView.contentViewContainer.LocalToWorld(nodeCenterContent);
            var startPos = graphView.WorldToLocal(nodeCenterWorld);
            
            // End position: mouse in graphView local coordinates
            var endPos = graphView.WorldToLocal(mousePosition);
            
            // Snap to target node center if hovering valid target
            if (hoveredNode != null && isValidTarget(hoveredNode.State))
            {
                var targetRect = hoveredNode.GetPosition();
                var targetCenterContent = new Vector2(targetRect.x + targetRect.width / 2, targetRect.y + targetRect.height / 2);
                var targetCenterWorld = graphView.contentViewContainer.LocalToWorld(targetCenterContent);
                endPos = graphView.WorldToLocal(targetCenterWorld);
            }
            else if (hoveredExitNode != null)
            {
                var targetRect = hoveredExitNode.GetPosition();
                var targetCenterContent = new Vector2(targetRect.x + targetRect.width / 2, targetRect.y + targetRect.height / 2);
                var targetCenterWorld = graphView.contentViewContainer.LocalToWorld(targetCenterContent);
                endPos = graphView.WorldToLocal(targetCenterWorld);
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
        
        private ExitNodeView GetExitNodeAtPosition(Vector2 mousePosition)
        {
            var localPos = graphView.contentViewContainer.WorldToLocal(mousePosition);
            
            foreach (var element in graphView.graphElements)
            {
                if (element is ExitNodeView exitView)
                {
                    var nodeRect = exitView.GetPosition();
                    if (nodeRect.Contains(localPos))
                    {
                        return exitView;
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

        private void ApplyExitNodeHighlight(bool isValid)
        {
            if (hoveredExitNode == null) return;
            
            var color = isValid ? ValidConnectionColor : InvalidConnectionColor;
            hoveredExitNode.style.borderBottomColor = color;
            hoveredExitNode.style.borderTopColor = color;
            hoveredExitNode.style.borderLeftColor = color;
            hoveredExitNode.style.borderRightColor = color;
            hoveredExitNode.style.borderBottomWidth = 2;
            hoveredExitNode.style.borderTopWidth = 2;
            hoveredExitNode.style.borderLeftWidth = 2;
            hoveredExitNode.style.borderRightWidth = 2;
        }

        private void ClearHoverHighlight()
        {
            if (hoveredNode != null)
            {
                hoveredNode.style.borderBottomColor = StyleKeyword.Null;
                hoveredNode.style.borderTopColor = StyleKeyword.Null;
                hoveredNode.style.borderLeftColor = StyleKeyword.Null;
                hoveredNode.style.borderRightColor = StyleKeyword.Null;
                hoveredNode.style.borderBottomWidth = StyleKeyword.Null;
                hoveredNode.style.borderTopWidth = StyleKeyword.Null;
                hoveredNode.style.borderLeftWidth = StyleKeyword.Null;
                hoveredNode.style.borderRightWidth = StyleKeyword.Null;
            }
            
            if (hoveredExitNode != null)
            {
                hoveredExitNode.style.borderBottomColor = StyleKeyword.Null;
                hoveredExitNode.style.borderTopColor = StyleKeyword.Null;
                hoveredExitNode.style.borderLeftColor = StyleKeyword.Null;
                hoveredExitNode.style.borderRightColor = StyleKeyword.Null;
                hoveredExitNode.style.borderBottomWidth = StyleKeyword.Null;
                hoveredExitNode.style.borderTopWidth = StyleKeyword.Null;
                hoveredExitNode.style.borderLeftWidth = StyleKeyword.Null;
                hoveredExitNode.style.borderRightWidth = StyleKeyword.Null;
            }
        }

        private void EndDrag()
        {
            isDragging = false;
            ClearHoverHighlight();
            RemoveDragLine();
            hoveredNode = null;
            hoveredExitNode = null;
            
            if (target.HasMouseCapture())
            {
                target.ReleaseMouse();
            }
        }
    }
}
