using System;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for graph nodes that can create outgoing transitions.
    /// Provides shared functionality: output port, selection events, context menu base.
    /// </summary>
    internal abstract class BaseNodeView : Node
    {
        public Port output;

        /// <summary>
        /// Default constructor for nodes that don't use UXML.
        /// </summary>
        protected BaseNodeView()
        {
        }

        /// <summary>
        /// Constructor for nodes that use a UXML template.
        /// </summary>
        protected BaseNodeView(string uiFile) : base(uiFile)
        {
        }
        
        /// <summary>
        /// Reference to the parent graph view for creating transitions and accessing state.
        /// </summary>
        protected AnimationStateMachineEditorView GraphView { get; private set; }
        
        /// <summary>
        /// Sets the parent graph view reference. Must be called after instantiation.
        /// </summary>
        internal void SetGraphView(AnimationStateMachineEditorView graphView)
        {
            GraphView = graphView;
        }

        protected void CreateOutputPort()
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

            BuildNodeContextMenu(evt, status);
            evt.StopPropagation();
        }

        /// <summary>
        /// Override to add node-specific context menu items.
        /// </summary>
        protected abstract void BuildNodeContextMenu(ContextualMenuPopulateEvent evt, DropdownMenuAction.Status status);
    }
}
