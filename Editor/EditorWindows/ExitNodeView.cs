using System;
using DMotion.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Special node representing "Exit" in the visual editor.
    /// Drawing a transition TO this node marks that state as an exit state.
    /// When this state machine is used as a nested machine, exit states can trigger
    /// the parent SubStateMachine's OutTransitions.
    /// </summary>
    internal class ExitNodeView : BaseNodeView
    {
        // Fixed position for static nodes (Any State at Y=50, Exit at Y=150)
        private const float FixedNodeX = 50f;
        private const float FixedNodeY = 150f;
        private static readonly Vector2 FixedPosition = new(FixedNodeX, FixedNodeY);
        internal Action<ExitNodeView> ExitSelectedEvent;
        internal StateMachineAsset StateMachine { get; }
        
        public Port input;

        public ExitNodeView(StateMachineAsset stateMachine, AnimationStateMachineEditorView graphView)
        {
            StateMachine = stateMachine;
            SetGraphView(graphView);
            
            title = "Exit";
            viewDataKey = "ExitNode"; // Fixed key for consistent positioning

            // Exit node has a distinct red/orange appearance (similar structure to Any State)
            AddToClassList("exitnode");

            // Set fixed position (below Any State node to form a column of static nodes)
            SetPosition(new Rect(FixedPosition, Vector2.one));

            // Only input port (states transition TO Exit, Exit doesn't transition out)
            CreateInputPort();

            // Add label explaining purpose
            var label = new Label("Triggers exit when nested");
            label.AddToClassList("exitnode-label");
            mainContainer.Add(label);

            // Make it non-movable (fixed position)
            capabilities &= ~Capabilities.Movable;
        }

        private void CreateInputPort()
        {
            input = Port.Create<TransitionEdge>(Orientation.Vertical, Direction.Input, Port.Capacity.Multi,
                typeof(bool));
            input.portName = "";
            inputContainer.Add(input);
        }

        protected override void BuildNodeContextMenu(ContextualMenuPopulateEvent evt, DropdownMenuAction.Status status)
        {
            // Show which states are currently marked as exit states
            if (StateMachine != null && StateMachine.ExitStates.Count > 0)
            {
                evt.menu.AppendAction("Exit States:", _ => { }, DropdownMenuAction.Status.Disabled);
                foreach (var exitState in StateMachine.ExitStates)
                {
                    if (exitState != null)
                    {
                        evt.menu.AppendAction($"  - {exitState.name}", 
                            _ => { }, DropdownMenuAction.Status.Normal);
                    }
                }
            }
            else
            {
                evt.menu.AppendAction("No exit states defined", _ => { }, DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Draw transitions TO this node to add exit states", _ => { }, DropdownMenuAction.Status.Disabled);
            }
        }

        public override void OnSelected()
        {
            base.OnSelected();
            ExitSelectedEvent?.Invoke(this);
        }

        // Override to prevent moving
        public override void SetPosition(Rect newPos)
        {
            // Keep fixed position (below Any State)
            base.SetPosition(new Rect(FixedPosition, Vector2.one));
        }
    }
}
