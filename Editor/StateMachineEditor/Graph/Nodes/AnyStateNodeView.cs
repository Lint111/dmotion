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
    internal class AnyStateNodeView : BaseNodeView
    {
        // Fixed position for static nodes (Any State at Y=50, Exit at Y=150)
        private const float FixedNodeX = 50f;
        private const float FixedNodeY = 50f;
        private static readonly Vector2 FixedPosition = new(FixedNodeX, FixedNodeY);
        
        internal Action<AnyStateNodeView> AnyStateSelectedEvent;
        internal StateMachineAsset StateMachine { get; }

        public AnyStateNodeView(StateMachineAsset stateMachineAsset, AnimationStateMachineEditorView graphView)
        {
            StateMachine = stateMachineAsset;
            SetGraphView(graphView);
            
            title = "Any State";
            viewDataKey = "AnyState"; // Fixed key for consistent positioning

            // Any State has a distinct appearance
            AddToClassList("anystate");

            // Set fixed position (top-left corner)
            SetPosition(new Rect(FixedPosition, Vector2.one));

            // Only output port (Any State can only transition OUT, never IN)
            CreateOutputPort();

            // Add label
            var label = new Label("Global transitions from any state");
            label.AddToClassList("anystate-label");
            mainContainer.Add(label);

            // Make it non-movable (fixed position)
            capabilities &= ~Capabilities.Movable;
        }

        // Output port creation inherited from BaseNodeView

        protected override void BuildNodeContextMenu(ContextualMenuPopulateEvent evt, DropdownMenuAction.Status status)
        {
            // Build submenu with all available target states
            var sb = StringBuilderCache.Get();
            foreach (var targetState in StateMachine.States)
            {
                var target = targetState; // Capture for closure
                // Check if transition already exists
                bool exists = StateMachine.AnyStateTransitions.Exists(t => t.ToState == target);
                var itemStatus = exists ? DropdownMenuAction.Status.Disabled : status;
                
                sb.Clear().Append("Create Transition/").Append(target.name);
                evt.menu.AppendAction(sb.ToString(),
                    _ => GraphView.CreateAnyStateTransitionTo(target), itemStatus);
            }
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
            base.SetPosition(new Rect(FixedPosition, Vector2.one));
        }
    }
}
