using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Manages the inspector panel by subscribing to selection events.
    /// This decouples the GraphView from inspector management.
    /// </summary>
    internal class InspectorController
    {
        private readonly StateMachineInspectorView inspectorView;
        private readonly AnimationStateMachineEditorView graphView;
        private StateMachineAsset currentMachine;

        public InspectorController(StateMachineInspectorView inspectorView, AnimationStateMachineEditorView graphView)
        {
            this.inspectorView = inspectorView;
            this.graphView = graphView;
        }

        /// <summary>
        /// Subscribes to all selection events. Call when the editor window opens.
        /// </summary>
        public void Subscribe()
        {
            StateMachineEditorEvents.OnStateSelected += OnStateSelected;
            StateMachineEditorEvents.OnTransitionSelected += OnTransitionSelected;
            StateMachineEditorEvents.OnAnyStateSelected += OnAnyStateSelected;
            StateMachineEditorEvents.OnAnyStateTransitionSelected += OnAnyStateTransitionSelected;
            StateMachineEditorEvents.OnExitNodeSelected += OnExitNodeSelected;
            StateMachineEditorEvents.OnSelectionCleared += OnSelectionCleared;
        }

        /// <summary>
        /// Unsubscribes from all events. Call when the editor window closes.
        /// </summary>
        public void Unsubscribe()
        {
            StateMachineEditorEvents.OnStateSelected -= OnStateSelected;
            StateMachineEditorEvents.OnTransitionSelected -= OnTransitionSelected;
            StateMachineEditorEvents.OnAnyStateSelected -= OnAnyStateSelected;
            StateMachineEditorEvents.OnAnyStateTransitionSelected -= OnAnyStateTransitionSelected;
            StateMachineEditorEvents.OnExitNodeSelected -= OnExitNodeSelected;
            StateMachineEditorEvents.OnSelectionCleared -= OnSelectionCleared;
        }

        /// <summary>
        /// Sets the current state machine context.
        /// </summary>
        public void SetContext(StateMachineAsset machine)
        {
            currentMachine = machine;
        }

        private void OnStateSelected(StateMachineAsset machine, AnimationStateAsset state)
        {
            if (machine != currentMachine || state == null) return;
            
            var stateView = graphView.GetViewForState(state);
            if (stateView == null) return;

            var inspectorModel = new AnimationStateInspectorModel
            {
                StateView = stateView
            };

            switch (stateView)
            {
                case SingleClipStateNodeView:
                    inspectorView.SetInspector<SingleStateInspector, AnimationStateInspectorModel>(
                        state, inspectorModel);
                    break;
                case LinearBlendStateNodeView:
                    inspectorView.SetInspector<LinearBlendStateInspector, AnimationStateInspectorModel>(
                        state, inspectorModel);
                    break;
                case SubStateMachineStateNodeView:
                    inspectorView.SetInspector<SubStateMachineInspector, AnimationStateInspectorModel>(
                        state, inspectorModel);
                    break;
            }
        }

        private void OnTransitionSelected(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            if (machine != currentMachine) return;

            var inspectorModel = new TransitionGroupInspectorModel
            {
                FromState = fromState,
                ToState = toState
            };
            inspectorView.SetInspector<TransitionGroupInspector, TransitionGroupInspectorModel>(
                fromState, inspectorModel);
        }

        private void OnAnyStateSelected(StateMachineAsset machine)
        {
            if (machine != currentMachine) return;

            inspectorView.SetInspector<AnyStateTransitionsInspector, AnyStateInspectorModel>(
                machine, new AnyStateInspectorModel { ToState = null });
        }

        private void OnAnyStateTransitionSelected(StateMachineAsset machine, AnimationStateAsset toState)
        {
            if (machine != currentMachine) return;

            // toState is null for exit transition
            inspectorView.SetInspector<AnyStateTransitionsInspector, AnyStateInspectorModel>(
                machine, new AnyStateInspectorModel { ToState = toState });
        }

        private void OnExitNodeSelected(StateMachineAsset machine)
        {
            if (machine != currentMachine) return;
            inspectorView.Clear();
        }

        private void OnSelectionCleared(StateMachineAsset machine)
        {
            if (machine != currentMachine) return;
            inspectorView.Clear();
        }
    }
}
