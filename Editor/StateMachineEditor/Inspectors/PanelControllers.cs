using System;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Manages breadcrumb bar navigation by subscribing to navigation events.
    /// Decouples breadcrumb UI from GraphView and EditorWindow.
    /// </summary>
    internal class BreadcrumbController
    {
        private readonly BreadcrumbBar breadcrumbBar;
        // Retained for domain reload state consistency
        private StateMachineAsset rootMachine;

        /// <summary>
        /// Fired when navigation should load a different state machine.
        /// The EditorWindow subscribes to this to actually load the machine.
        /// </summary>
        internal Action<StateMachineAsset> OnNavigationRequested;

        public BreadcrumbController(BreadcrumbBar breadcrumbBar)
        {
            this.breadcrumbBar = breadcrumbBar;
        }

        public void Subscribe()
        {
            StateMachineEditorEvents.OnSubStateMachineEntered += OnSubStateMachineEntered;
            StateMachineEditorEvents.OnSubStateMachineExited += OnSubStateMachineExited;
            
            // Wire up breadcrumb's internal navigation to raise events
            if (breadcrumbBar != null)
            {
                breadcrumbBar.OnNavigate += OnBreadcrumbClicked;
            }
        }

        public void Unsubscribe()
        {
            StateMachineEditorEvents.OnSubStateMachineEntered -= OnSubStateMachineEntered;
            StateMachineEditorEvents.OnSubStateMachineExited -= OnSubStateMachineExited;
            
            if (breadcrumbBar != null)
            {
                breadcrumbBar.OnNavigate -= OnBreadcrumbClicked;
            }
        }

        public void SetRoot(StateMachineAsset machine)
        {
            rootMachine = machine;
            breadcrumbBar?.SetRoot(machine);
        }

        private void OnSubStateMachineEntered(StateMachineAsset parent, StateMachineAsset entered)
        {
            breadcrumbBar?.Push(entered);
            OnNavigationRequested?.Invoke(entered);
        }

        private void OnSubStateMachineExited(StateMachineAsset returnedTo)
        {
            // Breadcrumb already handles its internal state via NavigateTo/NavigateBack
            // Just request the navigation
            OnNavigationRequested?.Invoke(returnedTo);
        }

        private void OnBreadcrumbClicked(int index)
        {
            var target = breadcrumbBar?.NavigationStack[index];
            if (target != null)
            {
                // Raise the global event for other listeners
                StateMachineEditorEvents.RaiseBreadcrumbNavigationRequested(target, index);
                // Request navigation
                OnNavigationRequested?.Invoke(target);
            }
        }
    }

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
