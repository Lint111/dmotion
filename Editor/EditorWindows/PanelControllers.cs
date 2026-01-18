using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Manages the parameters panel by subscribing to state machine events.
    /// </summary>
    internal class ParametersPanelController
    {
        private readonly StateMachineInspectorView panelView;
        private StateMachineAsset currentMachine;

        public ParametersPanelController(StateMachineInspectorView panelView)
        {
            this.panelView = panelView;
        }

        public void Subscribe()
        {
            StateMachineEditorEvents.OnParameterAdded += OnParameterChanged;
            StateMachineEditorEvents.OnParameterRemoved += OnParameterChanged;
        }

        public void Unsubscribe()
        {
            StateMachineEditorEvents.OnParameterAdded -= OnParameterChanged;
            StateMachineEditorEvents.OnParameterRemoved -= OnParameterChanged;
        }

        public void SetContext(StateMachineAsset machine)
        {
            currentMachine = machine;
            RefreshPanel();
        }

        private void OnParameterChanged(StateMachineAsset machine, AnimationParameterAsset param)
        {
            if (machine != currentMachine) return;
            // Panel auto-refreshes via SerializedObject, but we could force refresh here if needed
        }

        private void RefreshPanel()
        {
            if (currentMachine == null || panelView == null) return;
            
            panelView.SetInspector<ParametersInspector, ParameterInspectorModel>(
                currentMachine, new ParameterInspectorModel
                {
                    StateMachine = currentMachine
                });
        }
    }

    /// <summary>
    /// Manages the dependencies panel visibility and content based on SubStateMachine presence.
    /// </summary>
    internal class DependenciesPanelController
    {
        private readonly StateMachineInspectorView panelView;
        private StateMachineAsset currentMachine;

        public DependenciesPanelController(StateMachineInspectorView panelView)
        {
            this.panelView = panelView;
        }

        public void Subscribe()
        {
            StateMachineEditorEvents.OnStateAdded += OnStateAdded;
            StateMachineEditorEvents.OnStateRemoved += OnStateRemoved;
            StateMachineEditorEvents.OnLinkAdded += OnLinkChanged;
            StateMachineEditorEvents.OnLinkRemoved += OnLinkChanged;
            StateMachineEditorEvents.OnDependenciesResolved += OnDependenciesResolved;
        }

        public void Unsubscribe()
        {
            StateMachineEditorEvents.OnStateAdded -= OnStateAdded;
            StateMachineEditorEvents.OnStateRemoved -= OnStateRemoved;
            StateMachineEditorEvents.OnLinkAdded -= OnLinkChanged;
            StateMachineEditorEvents.OnLinkRemoved -= OnLinkChanged;
            StateMachineEditorEvents.OnDependenciesResolved -= OnDependenciesResolved;
        }

        public void SetContext(StateMachineAsset machine)
        {
            currentMachine = machine;
            RefreshPanel();
        }

        private void OnStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            if (machine != currentMachine) return;
            if (state is SubStateMachineStateAsset)
            {
                RefreshPanel();
            }
        }

        private void OnStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            if (machine != currentMachine) return;
            if (state is SubStateMachineStateAsset)
            {
                RefreshPanel();
            }
        }

        private void OnLinkChanged(StateMachineAsset machine, ParameterLink link)
        {
            if (machine != currentMachine) return;
            RefreshPanel();
        }

        private void OnDependenciesResolved(StateMachineAsset machine, SubStateMachineStateAsset subMachine, int count)
        {
            if (machine != currentMachine) return;
            RefreshPanel();
        }

        private void RefreshPanel()
        {
            if (panelView == null) return;

            var hasSubMachines = HasAnySubStateMachine(currentMachine);

            if (hasSubMachines)
            {
                panelView.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
                panelView.SetInspector<DependencyInspector, DependencyInspectorModel>(
                    currentMachine, new DependencyInspectorModel
                    {
                        StateMachine = currentMachine
                    });
            }
            else
            {
                panelView.style.display = UnityEngine.UIElements.DisplayStyle.None;
                panelView.Clear();
            }
        }

        private static bool HasAnySubStateMachine(StateMachineAsset machine)
        {
            if (machine == null) return false;
            var states = machine.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] is SubStateMachineStateAsset)
                    return true;
            }
            return false;
        }
    }
}
