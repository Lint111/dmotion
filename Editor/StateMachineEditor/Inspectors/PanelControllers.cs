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
        private EditorState _subscribedEditorState;

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
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.PropertyChanged += OnEditorStatePropertyChanged;

            // Wire up breadcrumb's internal navigation to raise events
            if (breadcrumbBar != null)
            {
                breadcrumbBar.OnNavigate += OnBreadcrumbClicked;
            }
        }

        public void Unsubscribe()
        {
            if (_subscribedEditorState != null)
            {
                _subscribedEditorState.PropertyChanged -= OnEditorStatePropertyChanged;
                _subscribedEditorState = null;
            }

            if (breadcrumbBar != null)
            {
                breadcrumbBar.OnNavigate -= OnBreadcrumbClicked;
            }
        }
        
        private void OnEditorStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(EditorState.CurrentViewStateMachine):
                    var machine = EditorState.Instance.CurrentViewStateMachine;
                    if (machine != null)
                    {
                        breadcrumbBar?.Push(machine);
                        OnNavigationRequested?.Invoke(machine);
                    }
                    break;
                    
                case nameof(EditorState.CurrentLayer):
                    var layer = EditorState.Instance.CurrentLayer;
                    if (layer?.NestedStateMachine != null)
                    {
                        breadcrumbBar?.Push(layer.NestedStateMachine);
                        OnNavigationRequested?.Invoke(layer.NestedStateMachine);
                    }
                    break;
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

        private void OnBreadcrumbClicked(int index)
        {
            var target = breadcrumbBar?.NavigationStack[index];
            if (target != null)
            {
                // Clear selection when navigating - inspector should not persist
                EditorState.Instance.ClearSelection();
                // Update current view state machine
                EditorState.Instance.CurrentViewStateMachine = target;
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
        private EditorState _subscribedEditorState;

        public ParametersPanelController(StateMachineInspectorView panelView)
        {
            this.panelView = panelView;
        }

        public void Subscribe()
        {
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.StructureChanged += OnStructureChanged;
        }

        public void Unsubscribe()
        {
            if (_subscribedEditorState == null) return; 
            _subscribedEditorState.StructureChanged -= OnStructureChanged;
            _subscribedEditorState = null;
        }

        public void SetContext(StateMachineAsset machine)
        {
            currentMachine = machine;
            RefreshPanel();
        }

        private void OnStructureChanged(object sender, StructureChangedEventArgs e)
        {
            if (e.ChangeType == StructureChangeType.ParameterAdded ||
                e.ChangeType == StructureChangeType.ParameterRemoved)
            {
                // Only check that we have an active machine context
                if (currentMachine == null) return;
                // Panel auto-refreshes via SerializedObject, but we could force refresh here if needed
            }
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
        private StateMachineAsset rootMachine;
        private EditorState _subscribedEditorState;

        public DependenciesPanelController(StateMachineInspectorView panelView)
        {
            this.panelView = panelView;
        }

        public void Subscribe()
        {
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.StructureChanged += OnStructureChanged;
        }

        public void Unsubscribe()
        {
            if (_subscribedEditorState == null) return; 
            _subscribedEditorState.StructureChanged -= OnStructureChanged;
            _subscribedEditorState = null;
        }

        public void SetContext(StateMachineAsset machine, StateMachineAsset root = null)
        {
            currentMachine = machine;
            rootMachine = root ?? machine;
            RefreshPanel();
        }

        private void OnStructureChanged(object sender, StructureChangedEventArgs e)
        {
            // Only respond to events for our root machine context
            if (rootMachine == null || EditorState.Instance.RootStateMachine != rootMachine) return;
            
            switch (e.ChangeType)
            {
                case StructureChangeType.StateAdded:
                case StructureChangeType.StateRemoved:
                    if (e.State is INestedStateMachineContainer)
                    {
                        RefreshPanel();
                    }
                    break;
                    
                case StructureChangeType.ParameterAdded:
                case StructureChangeType.ParameterRemoved:
                case StructureChangeType.ParameterChanged:
                case StructureChangeType.LayerAdded:
                case StructureChangeType.LayerRemoved:
                case StructureChangeType.ConvertedToMultiLayer:
                case StructureChangeType.GeneralChange:
                    RefreshPanel();
                    break;
            }
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
        private EditorState _subscribedEditorState;

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
            _subscribedEditorState = EditorState.Instance;
            _subscribedEditorState.StructureChanged += OnStructureChanged;
        }

        public void Unsubscribe()
        {
            if (_subscribedEditorState == null) return;
            _subscribedEditorState.StructureChanged -= OnStructureChanged;
            _subscribedEditorState = null;
        }

        public void SetContext(StateMachineAsset machine, StateMachineAsset root = null)
        {
            currentMachine = machine;
            rootMachine = root ?? machine;
            RefreshPanel();
        }

        private void OnStructureChanged(object sender, StructureChangedEventArgs e)
        {
            if (EditorState.Instance.RootStateMachine != rootMachine) return;
            
            switch (e.ChangeType)
            {
                case StructureChangeType.LayerAdded:
                case StructureChangeType.LayerRemoved:
                case StructureChangeType.LayerChanged:
                case StructureChangeType.ConvertedToMultiLayer:
                    RefreshPanel();
                    break;
            }
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
