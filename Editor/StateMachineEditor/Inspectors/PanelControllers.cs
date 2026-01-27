using System;
using System.Linq;
using DMotion.Authoring;
using UnityEngine.UIElements;

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
            StateMachineEditorEvents.OnLayerEntered += OnLayerEntered;
            
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
            StateMachineEditorEvents.OnLayerEntered -= OnLayerEntered;
            
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
        
        /// <summary>
        /// Pushes a state machine onto the breadcrumb stack.
        /// Used for layer navigation where the event is handled externally.
        /// </summary>
        public void Push(StateMachineAsset machine)
        {
            breadcrumbBar?.Push(machine);
        }

        private void OnSubStateMachineEntered(StateMachineAsset parent, StateMachineAsset entered)
        {
            breadcrumbBar?.Push(entered);
            OnNavigationRequested?.Invoke(entered);
        }
        
        private void OnLayerEntered(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine)
        {
            // Handle layer navigation from graph node double-click
            // Push to breadcrumb and request navigation (same as SubStateMachine)
            breadcrumbBar?.Push(layerMachine);
            OnNavigationRequested?.Invoke(layerMachine);
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
                // Clear selection when navigating - inspector should not persist
                StateMachineEditorEvents.RaiseSelectionCleared(target);
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
            StateMachineEditorEvents.OnLayerAdded += OnLayerAdded;
            StateMachineEditorEvents.OnLayerRemoved += OnLayerRemoved;
            StateMachineEditorEvents.OnConvertedToMultiLayer += OnConvertedToMultiLayer;
            StateMachineEditorEvents.OnParameterRemoved += OnParameterRemoved;
        }

        public void Unsubscribe()
        {
            StateMachineEditorEvents.OnStateAdded -= OnStateAdded;
            StateMachineEditorEvents.OnStateRemoved -= OnStateRemoved;
            StateMachineEditorEvents.OnLinkAdded -= OnLinkChanged;
            StateMachineEditorEvents.OnLinkRemoved -= OnLinkChanged;
            StateMachineEditorEvents.OnDependenciesResolved -= OnDependenciesResolved;
            StateMachineEditorEvents.OnLayerAdded -= OnLayerAdded;
            StateMachineEditorEvents.OnLayerRemoved -= OnLayerRemoved;
            StateMachineEditorEvents.OnConvertedToMultiLayer -= OnConvertedToMultiLayer;
            StateMachineEditorEvents.OnParameterRemoved -= OnParameterRemoved;
        }
        
        private void OnParameterRemoved(StateMachineAsset machine, AnimationParameterAsset param)
        {
            if (machine != currentMachine) return;
            RefreshPanel();
        }
        
        private void OnLayerAdded(StateMachineAsset machine, LayerStateAsset layer)
        {
            if (machine != currentMachine) return;
            RefreshPanel();
        }
        
        private void OnLayerRemoved(StateMachineAsset machine, LayerStateAsset layer)
        {
            if (machine != currentMachine) return;
            RefreshPanel();
        }
        
        private void OnConvertedToMultiLayer(StateMachineAsset machine)
        {
            if (machine != currentMachine) return;
            RefreshPanel();
        }

        public void SetContext(StateMachineAsset machine)
        {
            currentMachine = machine;
            RefreshPanel();
        }

        private void OnStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            if (machine != currentMachine) return;
            if (state is INestedStateMachineContainer)
            {
                RefreshPanel();
            }
        }

        private void OnStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            if (machine != currentMachine) return;
            if (state is INestedStateMachineContainer)
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

            var hasNestedContainers = currentMachine != null && currentMachine.HasNestedContainers;

            if (hasNestedContainers)
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
    }

    /// <summary>
    /// Manages the layers panel visibility and content for multi-layer state machines.
    /// Shows when the root state machine is in multi-layer mode.
    /// </summary>
    internal class LayersPanelController
    {
        private readonly StateMachineInspectorView panelView;
        private StateMachineAsset currentMachine;
        private StateMachineAsset rootMachine;

        /// <summary>
        /// Fired when user requests to edit a layer's state machine.
        /// </summary>
        internal Action<LayerStateAsset> OnEditLayerRequested;

        public LayersPanelController(StateMachineInspectorView panelView)
        {
            this.panelView = panelView;
        }

        public void Subscribe()
        {
            StateMachineEditorEvents.OnLayerAdded += OnLayerAdded;
            StateMachineEditorEvents.OnLayerRemoved += OnLayerRemoved;
            StateMachineEditorEvents.OnLayerChanged += OnLayerChanged;
            StateMachineEditorEvents.OnConvertedToMultiLayer += OnConvertedToMultiLayer;
        }

        public void Unsubscribe()
        {
            StateMachineEditorEvents.OnLayerAdded -= OnLayerAdded;
            StateMachineEditorEvents.OnLayerRemoved -= OnLayerRemoved;
            StateMachineEditorEvents.OnLayerChanged -= OnLayerChanged;
            StateMachineEditorEvents.OnConvertedToMultiLayer -= OnConvertedToMultiLayer;
        }

        public void SetContext(StateMachineAsset machine, StateMachineAsset root = null)
        {
            currentMachine = machine;
            rootMachine = root ?? machine;
            RefreshPanel();
        }

        private void OnLayerAdded(StateMachineAsset machine, LayerStateAsset layer)
        {
            if (machine != rootMachine) return;
            RefreshPanel();
        }

        private void OnLayerRemoved(StateMachineAsset machine, LayerStateAsset layer)
        {
            if (machine != rootMachine) return;
            RefreshPanel();
        }

        private void OnLayerChanged(StateMachineAsset machine, LayerStateAsset layer)
        {
            if (machine != rootMachine) return;
            RefreshPanel();
        }

        private void OnConvertedToMultiLayer(StateMachineAsset machine)
        {
            if (machine != rootMachine) return;
            RefreshPanel();
        }

        private void RefreshPanel()
        {
            if (panelView == null) return;

            // Show panel when viewing root machine (allows convert to multi-layer)
            // Hide when navigated into a layer's nested state machine
            bool isAtRoot = rootMachine != null && currentMachine == rootMachine;

            if (isAtRoot)
            {
                panelView.style.display = DisplayStyle.Flex;
                panelView.SetInspector<LayersInspector, LayersInspectorModel>(
                    rootMachine, new LayersInspectorModel
                    {
                        StateMachine = rootMachine,
                        OnEditLayer = OnEditLayerRequested
                    });
            }
            else
            {
                // Hide when inside a layer's state machine
                panelView.style.display = DisplayStyle.None;
                panelView.Clear();
            }
        }
    }

    /// <summary>
    /// Model for the layers inspector.
    /// </summary>
    internal struct LayersInspectorModel
    {
        public StateMachineAsset StateMachine;
        public Action<LayerStateAsset> OnEditLayer;
    }
}
