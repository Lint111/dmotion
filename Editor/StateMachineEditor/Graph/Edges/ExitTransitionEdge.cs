using DMotion.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Specialized transition edge for exit transitions from SubStateMachines.
    /// Visually distinct with orange dashed lines to indicate exit behavior.
    /// </summary>
    public class ExitTransitionEdge : TransitionEdge
    {
        internal StateOutTransition ExitTransition { get; set; }
        internal SubStateMachineStateAsset SubStateMachine { get; set; }
        internal int ExitTransitionIndex { get; set; }

        public ExitTransitionEdge()
        {
            AddToClassList("exit-transition");
        }

        protected override EdgeControl CreateEdgeControl()
        {
            return new ExitTransitionEdgeControl
            {
                Edge = this,
                capRadius = 4f,
                interceptWidth = 6f
            };
        }
    }
}