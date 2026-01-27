using DMotion.Authoring;
using System.ComponentModel;
 
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
            EditorState.Instance.PropertyChanged += OnEditorStatePropertyChanged;
        }

        /// <summary>
        /// Unsubscribes from all events. Call when the editor window closes.
        /// </summary>
        public void Unsubscribe()
        {
            EditorState.Instance.PropertyChanged -= OnEditorStatePropertyChanged;
        }

        /// <summary>
        /// Sets the current state machine context.
        /// </summary>
        public void SetContext(StateMachineAsset machine)
        {
            currentMachine = machine;
        }

        private void OnEditorStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // We care about selection changes only.
            switch (e.PropertyName)
            {
                case nameof(EditorState.SelectedState):
                case nameof(EditorState.IsTransitionSelected):
                case nameof(EditorState.IsAnyStateSelected):
                case nameof(EditorState.IsExitNodeSelected):
                case nameof(EditorState.HasSelection):
                case nameof(EditorState.SelectedTransitionFrom):
                case nameof(EditorState.SelectedTransitionTo):
                    break;
                default:
                    return;
            }

            var state = EditorState.Instance;
            if (state.RootStateMachine != currentMachine) return;

            // State selected
            if (state.SelectedState != null)
            {
                OnStateSelected(currentMachine, state.SelectedState);
                return;
            }

            // Transition selected
            if (state.IsTransitionSelected)
            {
                // Any State transition: from == null
                if (state.IsAnyStateSelected)
                {
                    OnAnyStateTransitionSelected(currentMachine, state.SelectedTransitionTo);
                }
                else
                {
                    OnTransitionSelected(currentMachine, state.SelectedTransitionFrom, state.SelectedTransitionTo);
                }
                return;
            }

            // Any State selected
            if (state.IsAnyStateSelected)
            {
                OnAnyStateSelected(currentMachine);
                return;
            }

            // Exit selected
            if (state.IsExitNodeSelected)
            {
                OnExitNodeSelected(currentMachine);
                return;
            }

            // Nothing selected
            OnSelectionCleared(currentMachine);
        }

        private void OnStateSelected(StateMachineAsset machine, AnimationStateAsset state)
        {
            if (machine != currentMachine || state == null) return;
            
            var stateView = graphView.GetViewForState(state);
            if (stateView == null) return;

            // Use UIToolkit for most state types
            switch (stateView)
            {
                case SingleClipStateNodeView:
                case LinearBlendStateNodeView:
                case Directional2DBlendStateNodeView:
                    // Use new UIToolkit inspector
                    inspectorView.SetStateInspector(machine, state, stateView);
                    break;
                    
                case SubStateMachineStateNodeView:
                    // Keep IMGUI for sub-state machine (more complex, migrate later)
                    var inspectorModel = new AnimationStateInspectorModel { StateView = stateView };
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
            inspectorView.Cleanup();
        }

        private void OnSelectionCleared(StateMachineAsset machine)
        {
            if (machine != currentMachine) return;
            inspectorView.Cleanup();
        }
    }
}
