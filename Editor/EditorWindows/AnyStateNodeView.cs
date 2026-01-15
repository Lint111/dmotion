using System;
using DMotion.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Special node representing "Any State" in the visual editor.
    /// Not an actual state - just a visual element for managing global transitions.
    /// </summary>
    internal class AnyStateNodeView : Node
    {
        internal Action<AnyStateNodeView> AnyStateSelectedEvent;
        private readonly StateMachineAsset stateMachine;
        public Port output;

        public AnyStateNodeView(StateMachineAsset stateMachineAsset)
        {
            stateMachine = stateMachineAsset;
            title = "Any State";
            viewDataKey = "AnyState"; // Fixed key for consistent positioning

            // Any State has a distinct appearance
            AddToClassList("anystate");

            // Set fixed position (top-left corner)
            SetPosition(new Rect(new Vector2(50, 50), Vector2.one));

            // Only output port (Any State can only transition OUT, never IN)
            CreateOutputPort();

            // Add label
            var label = new Label("Global transitions from any state");
            label.AddToClassList("anystate-label");
            mainContainer.Add(label);

            // Make it non-movable (fixed position)
            capabilities &= ~Capabilities.Movable;
        }

        private void CreateOutputPort()
        {
            output = Port.Create<TransitionEdge>(Orientation.Vertical, Direction.Output, Port.Capacity.Multi,
                typeof(bool));
            output.portName = "";
            outputContainer.Add(output);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            var status = Application.isPlaying
                ? DropdownMenuAction.Status.Disabled
                : DropdownMenuAction.Status.Normal;

            evt.menu.AppendAction("Create Any State Transition", OnContextMenuCreateTransition, status);
            evt.StopPropagation();
        }

        private void OnContextMenuCreateTransition(DropdownMenuAction obj)
        {
            // Trigger edge creation (same hack as regular state nodes)
            var ev = MouseDownEvent.GetPooled(Input.mousePosition, 0, 1, Vector2.zero);
            output.edgeConnector.GetType().GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(output.edgeConnector, new object[] { ev });
        }

        public override void OnSelected()
        {
            base.OnSelected();
            AnyStateSelectedEvent?.Invoke(this);
        }

        // Override to prevent moving
        public override void SetPosition(Rect newPos)
        {
            // Keep fixed position
            base.SetPosition(new Rect(new Vector2(50, 50), Vector2.one));
        }
    }
}
