using System;
using System.Collections.Generic;
using System.ComponentModel;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Central observable state for the DMotion editor.
    /// All editor components should bind to this state rather than using scattered events.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture:</b></para>
    /// <code>
    /// EditorState (singleton)
    /// ├── Selection (current state machine, state, transition)
    /// ├── Navigation (breadcrumb stack, current view)
    /// ├── PreviewState (single state preview)
    /// └── CompositionState (multi-layer preview)
    /// </code>
    ///
    /// <para><b>Usage:</b></para>
    /// <code>
    /// // Subscribe to changes
    /// EditorState.Instance.PropertyChanged += OnEditorStateChanged;
    /// EditorState.Instance.PreviewState.PropertyChanged += OnPreviewStateChanged;
    ///
    /// // Set properties (events fire automatically)
    /// EditorState.Instance.SelectedState = myState;
    /// EditorState.Instance.PreviewState.NormalizedTime = 0.5f;
    /// </code>
    /// </remarks>
    public class EditorState : ObservableObject, IDisposable
    {
        #region Singleton
        
        private static EditorState _instance;
        
        /// <summary>
        /// Global editor state instance.
        /// </summary>
        public static EditorState Instance => _instance ??= new EditorState();
        
        /// <summary>
        /// Resets the singleton instance (for testing or editor reload).
        /// </summary>
        public static void ResetInstance()
        {
            _instance?.Dispose();
            _instance = null;
        }
        
        #endregion
        
        #region Backing Fields
        
        // State Machine Context
        private StateMachineAsset _rootStateMachine;        // The root state machine asset that was opened
        private StateMachineAsset _currentViewStateMachine; // The state machine currently being viewed (may be nested)
        
        // Selection
        private AnimationStateAsset _selectedState;
        private AnimationStateAsset _selectedTransitionFrom;
        private AnimationStateAsset _selectedTransitionTo;
        private bool _isTransitionSelected;
        private bool _isAnyStateSelected;
        private bool _isExitNodeSelected;
        
        // Navigation
        private LayerStateAsset _currentLayer;
        private int _currentLayerIndex = -1;
        
        // Preview type (single-state vs layer composition)
        private EditorPreviewType _previewType = EditorPreviewType.SingleState;
        
        // Composed state objects
        private readonly ObservablePreviewState _previewState = new();
        private readonly ObservableCompositionState _compositionState = new();
        
        #endregion
        
        #region Cached Property Arrays (avoid allocation)
        
        // Cached arrays for OnPropertiesChanged calls to avoid per-call allocation
        private static readonly string[] SelectionChangedProperties = 
        {
            nameof(SelectedTransitionFrom),
            nameof(SelectedTransitionTo),
            nameof(IsTransitionSelected),
            nameof(IsAnyStateSelected),
            nameof(IsExitNodeSelected)
        };
        
        private static readonly string[] AllSelectionProperties = 
        {
            nameof(SelectedState),
            nameof(SelectedTransitionFrom),
            nameof(SelectedTransitionTo),
            nameof(IsTransitionSelected),
            nameof(IsAnyStateSelected),
            nameof(IsExitNodeSelected)
        };
        
        private static readonly string[] ClearSelectionProperties = 
        {
            nameof(SelectedState),
            nameof(SelectedTransitionFrom),
            nameof(SelectedTransitionTo),
            nameof(IsTransitionSelected),
            nameof(IsAnyStateSelected),
            nameof(IsExitNodeSelected),
            nameof(HasSelection)
        };
        
        #endregion
        
        #region Constructor
        
        private EditorState()
        {
            // Forward child state changes
            _previewState.PropertyChanged += OnPreviewStateChanged;
            _compositionState.PropertyChanged += OnCompositionStateChanged;
            _compositionState.LayerChanged += OnLayerChanged;
        }
        
        #endregion
        
        #region State Machine Properties
        
        /// <summary>
        /// The root state machine asset that was opened.
        /// This never changes when navigating into layers or sub-state-machines.
        /// Use this to determine if we're in a multi-layer context.
        /// </summary>
        public StateMachineAsset RootStateMachine
        {
            get => _rootStateMachine;
            set
            {
                if (SetProperty(ref _rootStateMachine, value))
                {
                    // Also set current view to root initially
                    _currentViewStateMachine = value;
                    OnPropertyChanged(nameof(CurrentViewStateMachine));
                    
                    // Clear selection when root changes
                    ClearSelection();
                    
                    // Clear layer navigation
                    _currentLayer = null;
                    _currentLayerIndex = -1;
                    OnPropertyChanged(nameof(CurrentLayer));
                    OnPropertyChanged(nameof(CurrentLayerIndex));
                    
                    // Initialize composition state if multi-layer
                    if (value != null && value.IsMultiLayer)
                    {
                        _compositionState.Initialize(value, this);
                        PreviewType = EditorPreviewType.LayerComposition;
                    }
                    else
                    {
                        _compositionState.Clear();
                        PreviewType = EditorPreviewType.SingleState;
                    }
                }
            }
        }
        
        /// <summary>
        /// The state machine currently being viewed in the editor.
        /// This changes when navigating into layers or sub-state-machines.
        /// </summary>
        public StateMachineAsset CurrentViewStateMachine
        {
            get => _currentViewStateMachine;
            set => SetProperty(ref _currentViewStateMachine, value);
        }
        
        /// <summary>
        /// Alias for RootStateMachine for backward compatibility.
        /// Prefer using RootStateMachine for clarity.
        /// </summary>
        [System.Obsolete("Use RootStateMachine instead for clarity")]
        public StateMachineAsset CurrentStateMachine
        {
            get => _rootStateMachine;
            set => RootStateMachine = value;
        }
        
        #endregion
        
        #region Selection Properties
        
        /// <summary>
        /// Currently selected state (null if transition or special node selected).
        /// </summary>
        public AnimationStateAsset SelectedState
        {
            get => _selectedState;
            set
            {
                if (SetProperty(ref _selectedState, value))
                {
                    // Clear other selections
                    _selectedTransitionFrom = null;
                    _selectedTransitionTo = null;
                    _isTransitionSelected = false;
                    _isAnyStateSelected = false;
                    _isExitNodeSelected = false;

                    // Update preview state
                    if (value != null)
                    {
                        _previewState.SelectState(value);
                    }

                    OnPropertiesChanged(SelectionChangedProperties);
                }
            }
        }
        
        /// <summary>
        /// From state of selected transition.
        /// </summary>
        public AnimationStateAsset SelectedTransitionFrom
        {
            get => _selectedTransitionFrom;
            private set => SetProperty(ref _selectedTransitionFrom, value);
        }
        
        /// <summary>
        /// To state of selected transition.
        /// </summary>
        public AnimationStateAsset SelectedTransitionTo
        {
            get => _selectedTransitionTo;
            private set => SetProperty(ref _selectedTransitionTo, value);
        }
        
        /// <summary>
        /// Whether a transition is currently selected.
        /// </summary>
        public bool IsTransitionSelected
        {
            get => _isTransitionSelected;
            private set => SetProperty(ref _isTransitionSelected, value);
        }
        
        /// <summary>
        /// Whether the Any State node is selected.
        /// </summary>
        public bool IsAnyStateSelected
        {
            get => _isAnyStateSelected;
            set
            {
                if (SetProperty(ref _isAnyStateSelected, value) && value)
                {
                    ClearSelectionExcept(nameof(IsAnyStateSelected));
                }
            }
        }
        
        /// <summary>
        /// Whether the Exit node is selected.
        /// </summary>
        public bool IsExitNodeSelected
        {
            get => _isExitNodeSelected;
            set
            {
                if (SetProperty(ref _isExitNodeSelected, value) && value)
                {
                    ClearSelectionExcept(nameof(IsExitNodeSelected));
                }
            }
        }
        
        /// <summary>
        /// Whether anything is currently selected.
        /// </summary>
        public bool HasSelection => 
            SelectedState != null || 
            IsTransitionSelected || 
            IsAnyStateSelected || 
            IsExitNodeSelected;
        
        #endregion
        
        #region Navigation Properties
        
        /// <summary>
        /// Currently entered layer (for multi-layer navigation).
        /// </summary>
        public LayerStateAsset CurrentLayer
        {
            get => _currentLayer;
            set => SetProperty(ref _currentLayer, value);
        }
        
        /// <summary>
        /// Index of currently entered layer (-1 if not in a layer).
        /// </summary>
        public int CurrentLayerIndex
        {
            get => _currentLayerIndex;
            set => SetProperty(ref _currentLayerIndex, value);
        }
        
        /// <summary>
        /// Whether currently viewing inside a layer.
        /// </summary>
        public bool IsInLayer => CurrentLayer != null;
        
        #endregion
        
        #region Preview Properties
        
        /// <summary>
        /// Current preview mode.
        /// </summary>
        public EditorPreviewType PreviewType
        {
            get => _previewType;
            set => SetProperty(ref _previewType, value);
        }
        
        /// <summary>
        /// Single state/transition preview state.
        /// </summary>
        public ObservablePreviewState PreviewState => _previewState;
        
        /// <summary>
        /// Multi-layer composition preview state.
        /// </summary>
        public ObservableCompositionState CompositionState => _compositionState;
        
        /// <summary>
        /// Whether the root state machine is multi-layer.
        /// </summary>
        public bool IsMultiLayer => RootStateMachine?.IsMultiLayer ?? false;
        
        #endregion
        
        #region Selection Methods
        
        /// <summary>
        /// Selects a transition.
        /// </summary>
        public void SelectTransition(AnimationStateAsset from, AnimationStateAsset to, bool isAnyState = false)
        {
            _selectedState = null;
            _selectedTransitionFrom = from;
            _selectedTransitionTo = to;
            _isTransitionSelected = true;
            _isAnyStateSelected = isAnyState && from == null;
            _isExitNodeSelected = false;

            // Update preview state
            _previewState.SelectTransition(from, to);

            OnPropertiesChanged(AllSelectionProperties);
        }
        
        /// <summary>
        /// Clears all selection.
        /// </summary>
        public void ClearSelection()
        {
            _selectedState = null;
            _selectedTransitionFrom = null;
            _selectedTransitionTo = null;
            _isTransitionSelected = false;
            _isAnyStateSelected = false;
            _isExitNodeSelected = false;

            _previewState.ClearSelection();

            OnPropertiesChanged(ClearSelectionProperties);
        }
        
        private void ClearSelectionExcept(string keepProperty)
        {
            if (keepProperty != nameof(SelectedState)) _selectedState = null;
            if (keepProperty != nameof(IsTransitionSelected))
            {
                _selectedTransitionFrom = null;
                _selectedTransitionTo = null;
                _isTransitionSelected = false;
            }
            if (keepProperty != nameof(IsAnyStateSelected)) _isAnyStateSelected = false;
            if (keepProperty != nameof(IsExitNodeSelected)) _isExitNodeSelected = false;
            
            OnPropertiesChanged(AllSelectionProperties);
        }
        
        #endregion
        
        #region Navigation Methods
        
        /// <summary>
        /// Enters a layer for editing.
        /// </summary>
        public void EnterLayer(LayerStateAsset layer, int layerIndex)
        {
            CurrentLayer = layer;
            CurrentLayerIndex = layerIndex;
            CurrentViewStateMachine = layer?.NestedStateMachine;
            ClearSelection();
        }
        
        /// <summary>
        /// Exits the current layer.
        /// </summary>
        public void ExitLayer()
        {
            CurrentLayer = null;
            CurrentLayerIndex = -1;
            CurrentViewStateMachine = RootStateMachine;
            ClearSelection();
        }
        
        /// <summary>
        /// Enters a sub-state machine.
        /// </summary>
        public void EnterSubStateMachine(StateMachineAsset subMachine)
        {
            CurrentViewStateMachine = subMachine;
            ClearSelection();
        }
        
        /// <summary>
        /// Navigates to the root state machine.
        /// </summary>
        public void NavigateToRoot()
        {
            CurrentLayer = null;
            CurrentLayerIndex = -1;
            CurrentViewStateMachine = RootStateMachine;
            ClearSelection();
        }
        
        #endregion
        
        #region Event Forwarding
        
        /// <summary>
        /// Fired when preview state changes.
        /// </summary>
        public event EventHandler<ObservablePropertyChangedEventArgs> PreviewStateChanged;
        
        /// <summary>
        /// Fired when composition state changes.
        /// </summary>
        public event EventHandler<ObservablePropertyChangedEventArgs> CompositionStateChanged;
        
        /// <summary>
        /// Fired when any layer in composition changes.
        /// </summary>
        public event EventHandler<LayerPropertyChangedEventArgs> LayerStateChanged;
        
        private void OnPreviewStateChanged(object sender, PropertyChangedEventArgs e)
        {
            var args = e as ObservablePropertyChangedEventArgs ?? new ObservablePropertyChangedEventArgs(e.PropertyName);
            PreviewStateChanged?.Invoke(this, args);
        }
        
        private void OnCompositionStateChanged(object sender, PropertyChangedEventArgs e)
        {
            var args = e as ObservablePropertyChangedEventArgs ?? new ObservablePropertyChangedEventArgs(e.PropertyName);
            CompositionStateChanged?.Invoke(this, args);
        }
        
        private void OnLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            LayerStateChanged?.Invoke(this, e);
        }
        
        #endregion
        
        #region Structure Change Events
        
        /// <summary>
        /// Fired when the state machine structure changes (states, transitions, parameters added/removed).
        /// </summary>
        public event EventHandler<StructureChangedEventArgs> StructureChanged;
        
        /// <summary>
        /// Notifies that a state was added.
        /// </summary>
        public void NotifyStateAdded(AnimationStateAsset state)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.StateAdded, state));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that a state was removed.
        /// </summary>
        public void NotifyStateRemoved(AnimationStateAsset state)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.StateRemoved, state));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that a transition was added.
        /// </summary>
        public void NotifyTransitionAdded(AnimationStateAsset from, AnimationStateAsset to)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.TransitionAdded, from, to));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that a transition was removed.
        /// </summary>
        public void NotifyTransitionRemoved(AnimationStateAsset from, AnimationStateAsset to)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.TransitionRemoved, from, to));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that a parameter was added.
        /// </summary>
        public void NotifyParameterAdded(AnimationParameterAsset parameter)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.ParameterAdded, parameter));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that a parameter was removed.
        /// </summary>
        public void NotifyParameterRemoved(AnimationParameterAsset parameter)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.ParameterRemoved, parameter));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that a layer was added.
        /// </summary>
        public void NotifyLayerAdded(LayerStateAsset layer)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.LayerAdded, layer));
            OnPropertyChanged(nameof(RootStateMachine));

            // Reinitialize composition state
            if (RootStateMachine != null && RootStateMachine.IsMultiLayer)
            {
                _compositionState.Initialize(RootStateMachine, this);
            }
        }
        
        /// <summary>
        /// Notifies that a layer was removed.
        /// </summary>
        public void NotifyLayerRemoved(LayerStateAsset layer)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.LayerRemoved, layer));
            OnPropertyChanged(nameof(RootStateMachine));

            // Reinitialize composition state
            if (RootStateMachine != null && RootStateMachine.IsMultiLayer)
            {
                _compositionState.Initialize(RootStateMachine, this);
            }
        }
        
        /// <summary>
        /// Notifies that the default state changed.
        /// </summary>
        public void NotifyDefaultStateChanged(AnimationStateAsset newDefault, AnimationStateAsset oldDefault)
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.DefaultStateChanged, newDefault, oldDefault));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        /// <summary>
        /// Notifies that the state machine was converted to multi-layer.
        /// </summary>
        public void NotifyConvertedToMultiLayer()
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.ConvertedToMultiLayer));
            OnPropertyChanged(nameof(RootStateMachine));
            OnPropertyChanged(nameof(IsMultiLayer));

            if (RootStateMachine != null && RootStateMachine.IsMultiLayer)
            {
                _compositionState.Initialize(RootStateMachine, this);
                PreviewType = EditorPreviewType.LayerComposition;
            }
        }
        
        /// <summary>
        /// Notifies that the graph needs to be repopulated (e.g., after undo/redo).
        /// </summary>
        public void NotifyGraphNeedsRepopulate()
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.GraphNeedsRepopulate));
        }
        
        /// <summary>
        /// Notifies a general state machine change.
        /// </summary>
        public void NotifyStateMachineChanged()
        {
            StructureChanged?.Invoke(this, new StructureChangedEventArgs(StructureChangeType.GeneralChange));
            OnPropertyChanged(nameof(RootStateMachine));
        }
        
        #endregion

        #region Navigation Events

        /// <summary>
        /// Fired when navigation to a specific state or transition is requested.
        /// The state machine editor window handles this to navigate through sub-state machines.
        /// </summary>
        public event EventHandler<NavigationRequestedEventArgs> NavigationRequested;

        /// <summary>
        /// Requests navigation to a specific state within a container.
        /// The state machine editor window will handle traversing sub-state machines and framing the selection.
        /// </summary>
        /// <param name="container">The container (layer or sub-state machine) containing the state.</param>
        /// <param name="layerIndex">Index of the layer (-1 if not in a layer).</param>
        /// <param name="targetState">The state to navigate to (may be nested in sub-state machines).</param>
        public void RequestNavigateToState(INestedStateMachineContainer container, int layerIndex, AnimationStateAsset targetState)
        {
            NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(container, layerIndex, targetState));
        }

        /// <summary>
        /// Requests navigation to a specific transition within a container.
        /// </summary>
        public void RequestNavigateToTransition(INestedStateMachineContainer container, int layerIndex, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(container, layerIndex, fromState, toState));
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Cleans up the editor state.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            // Unsubscribe from child state events
            _previewState.PropertyChanged -= OnPreviewStateChanged;
            _compositionState.PropertyChanged -= OnCompositionStateChanged;
            _compositionState.LayerChanged -= OnLayerChanged;
            _compositionState.Clear();

            // Events will be garbage collected when this instance is collected.
            // Subscribers are responsible for unsubscribing in their own cleanup.
        }

        #endregion
    }

    /// <summary>
    /// Event args for navigation requests.
    /// </summary>
    public class NavigationRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// The container (layer or sub-state machine) that holds the target.
        /// </summary>
        public INestedStateMachineContainer Container { get; }

        /// <summary>
        /// Index of the layer (-1 if not navigating within a layer context).
        /// </summary>
        public int LayerIndex { get; }

        /// <summary>
        /// The target state to navigate to.
        /// </summary>
        public AnimationStateAsset TargetState { get; }

        /// <summary>
        /// The source state of a transition (null if not a transition).
        /// </summary>
        public AnimationStateAsset TransitionFrom { get; }

        /// <summary>
        /// The destination state of a transition (null if not a transition).
        /// </summary>
        public AnimationStateAsset TransitionTo { get; }

        /// <summary>
        /// Whether this is a transition navigation request.
        /// </summary>
        public bool IsTransition => TransitionFrom != null && TransitionTo != null;

        /// <summary>
        /// Convenience property to get the container as a LayerStateAsset (if it is one).
        /// </summary>
        public LayerStateAsset LayerAsset => Container as LayerStateAsset;

        public NavigationRequestedEventArgs(INestedStateMachineContainer container, int layerIndex, AnimationStateAsset targetState)
        {
            Container = container;
            LayerIndex = layerIndex;
            TargetState = targetState;
        }

        public NavigationRequestedEventArgs(INestedStateMachineContainer container, int layerIndex, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            Container = container;
            LayerIndex = layerIndex;
            TransitionFrom = fromState;
            TransitionTo = toState;
        }
    }

    /// <summary>
    /// Preview mode enumeration.
    /// </summary>
    public enum EditorPreviewType
    {
        /// <summary>Single state or transition preview.</summary>
        SingleState,
        
        /// <summary>Multi-layer composition preview.</summary>
        LayerComposition
    }
    
    /// <summary>
    /// Types of structure changes in the state machine.
    /// </summary>
    public enum StructureChangeType
    {
        StateAdded,
        StateRemoved,
        TransitionAdded,
        TransitionRemoved,
        ParameterAdded,
        ParameterRemoved,
        ParameterChanged,
        LayerAdded,
        LayerRemoved,
        LayerChanged,
        DefaultStateChanged,
        ConvertedToMultiLayer,
        GraphNeedsRepopulate,
        GeneralChange
    }
    
    /// <summary>
    /// Event args for structure changes.
    /// </summary>
    public class StructureChangedEventArgs : EventArgs
    {
        public StructureChangeType ChangeType { get; }
        public AnimationStateAsset State { get; }
        public AnimationStateAsset FromState { get; }
        public AnimationStateAsset ToState { get; }
        public AnimationParameterAsset Parameter { get; }
        public LayerStateAsset Layer { get; }
        
        public StructureChangedEventArgs(StructureChangeType changeType)
        {
            ChangeType = changeType;
        }
        
        public StructureChangedEventArgs(StructureChangeType changeType, AnimationStateAsset state)
        {
            ChangeType = changeType;
            State = state;
        }
        
        public StructureChangedEventArgs(StructureChangeType changeType, AnimationStateAsset from, AnimationStateAsset to)
        {
            ChangeType = changeType;
            FromState = from;
            ToState = to;
        }
        
        public StructureChangedEventArgs(StructureChangeType changeType, AnimationParameterAsset parameter)
        {
            ChangeType = changeType;
            Parameter = parameter;
        }
        
        public StructureChangedEventArgs(StructureChangeType changeType, LayerStateAsset layer)
        {
            ChangeType = changeType;
            Layer = layer;
        }
    }
}
