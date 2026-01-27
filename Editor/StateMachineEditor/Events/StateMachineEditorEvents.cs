using System;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Centralized event system for the State Machine Editor.
    /// Provides a public API for extensions and tools to react to editor changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture:</b> All editor panels subscribe to these events to stay synchronized.
    /// The GraphView raises events, and controllers/panels respond independently.</para>
    /// 
    /// <para><b>Event Categories:</b></para>
    /// <list type="bullet">
    ///   <item><b>State Events</b> - State added/removed, default state changes</item>
    ///   <item><b>Parameter Events</b> - Parameter added/removed/changed</item>
    ///   <item><b>Transition Events</b> - Transitions and Any State transitions</item>
    ///   <item><b>Exit State Events</b> - Exit states and Any State exit transition</item>
    ///   <item><b>Link Events</b> - Parameter links for SubStateMachine dependencies</item>
    ///   <item><b>Selection Events</b> - Graph selection changes</item>
    ///   <item><b>Navigation Events</b> - SubStateMachine navigation</item>
    ///   <item><b>General Events</b> - Catch-all and repopulate requests</item>
    /// </list>
    /// 
    /// <para><b>Usage Example - Custom Extension:</b></para>
    /// <code>
    /// public class MyStateMachineAnalyzer
    /// {
    ///     public void Initialize()
    ///     {
    ///         StateMachineEditorEvents.OnStateAdded += OnStateAdded;
    ///         StateMachineEditorEvents.OnTransitionAdded += OnTransitionAdded;
    ///     }
    ///     
    ///     public void Cleanup()
    ///     {
    ///         StateMachineEditorEvents.OnStateAdded -= OnStateAdded;
    ///         StateMachineEditorEvents.OnTransitionAdded -= OnTransitionAdded;
    ///     }
    ///     
    ///     private void OnStateAdded(StateMachineAsset machine, AnimationStateAsset state)
    ///     {
    ///         Debug.Log($"State '{state.name}' added to '{machine.name}'");
    ///     }
    ///     
    ///     private void OnTransitionAdded(StateMachineAsset machine, 
    ///         AnimationStateAsset from, AnimationStateAsset to)
    ///     {
    ///         Debug.Log($"Transition: {from.name} -> {to.name}");
    ///     }
    /// }
    /// </code>
    /// 
    /// <para><b>Important:</b> Always unsubscribe when your extension is disposed to prevent memory leaks.
    /// The editor window calls <see cref="ClearAllSubscriptions"/> on close as a safety net.</para>
    /// </remarks>
    public static class StateMachineEditorEvents
    {
        #region State Events

        /// <summary>
        /// Fired when a state is added to the state machine.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnStateAdded;

        /// <summary>
        /// Fired when a state is removed from the state machine.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnStateRemoved;

        /// <summary>
        /// Fired when the default state changes.
        /// Args: (machine, newDefault, previousDefault)
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset, AnimationStateAsset> OnDefaultStateChanged;

        #endregion

        #region Parameter Events

        /// <summary>
        /// Fired when a parameter is added to the state machine.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationParameterAsset> OnParameterAdded;

        /// <summary>
        /// Fired when a parameter is removed from the state machine.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationParameterAsset> OnParameterRemoved;

        /// <summary>
        /// Fired when a parameter's value or properties change.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationParameterAsset> OnParameterChanged;

        #endregion

        #region Transition Events

        /// <summary>
        /// Fired when a transition is added between states.
        /// Args: (machine, fromState, toState)
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset, AnimationStateAsset> OnTransitionAdded;

        /// <summary>
        /// Fired when a transition is removed between states.
        /// Args: (machine, fromState, toState)
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset, AnimationStateAsset> OnTransitionRemoved;

        /// <summary>
        /// Fired when an Any State transition is added.
        /// Args: (machine, toState)
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnAnyStateTransitionAdded;

        /// <summary>
        /// Fired when an Any State transition is removed.
        /// Args: (machine, toState)
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnAnyStateTransitionRemoved;

        #endregion

        #region Exit State Events

        /// <summary>
        /// Fired when a state is marked as an exit state.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnExitStateAdded;

        /// <summary>
        /// Fired when a state is unmarked as an exit state.
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnExitStateRemoved;

        /// <summary>
        /// Fired when the Any State exit transition is created or removed.
        /// Args: (machine, hasExitTransition)
        /// </summary>
        public static event Action<StateMachineAsset, bool> OnAnyStateExitTransitionChanged;

        #endregion

        #region Parameter Link Events

        /// <summary>
        /// Fired when a parameter link is added.
        /// </summary>
        public static event Action<StateMachineAsset, ParameterLink> OnLinkAdded;

        /// <summary>
        /// Fired when a parameter link is removed.
        /// </summary>
        public static event Action<StateMachineAsset, ParameterLink> OnLinkRemoved;

        /// <summary>
        /// Fired when dependencies are resolved for a SubStateMachine.
        /// Args: (machine, subMachine, resolvedCount)
        /// </summary>
        public static event Action<StateMachineAsset, SubStateMachineStateAsset, int> OnDependenciesResolved;

        #endregion

        #region Selection Events

        /// <summary>
        /// Fired when a state node is selected in the graph.
        /// Args: (machine, selectedState) - state can be null for deselection
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnStateSelected;

        /// <summary>
        /// Fired when a transition edge is selected in the graph.
        /// Args: (machine, fromState, toState)
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset, AnimationStateAsset> OnTransitionSelected;

        /// <summary>
        /// Fired when the Any State node is selected.
        /// </summary>
        public static event Action<StateMachineAsset> OnAnyStateSelected;

        /// <summary>
        /// Fired when an Any State transition edge is selected.
        /// Args: (machine, toState) - toState is null for exit transition
        /// </summary>
        public static event Action<StateMachineAsset, AnimationStateAsset> OnAnyStateTransitionSelected;

        /// <summary>
        /// Fired when the Exit node is selected.
        /// </summary>
        public static event Action<StateMachineAsset> OnExitNodeSelected;

        /// <summary>
        /// Fired when selection is cleared.
        /// </summary>
        public static event Action<StateMachineAsset> OnSelectionCleared;

        #endregion

        #region Layer-Aware Selection Events

        /// <summary>
        /// Fired when a state is selected within a specific layer context.
        /// Args: (rootMachine, layer, layerMachine, selectedState)
        /// Use this for multi-layer preview to track per-layer selections.
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset, StateMachineAsset, AnimationStateAsset> OnLayerStateSelected;

        /// <summary>
        /// Fired when a transition is selected within a specific layer context.
        /// Args: (rootMachine, layer, layerMachine, fromState, toState)
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset, StateMachineAsset, AnimationStateAsset, AnimationStateAsset> OnLayerTransitionSelected;

        /// <summary>
        /// Fired when Any State is selected within a specific layer context.
        /// Args: (rootMachine, layer, layerMachine)
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset, StateMachineAsset> OnLayerAnyStateSelected;

        /// <summary>
        /// Fired when an Any State transition is selected within a specific layer context.
        /// Args: (rootMachine, layer, layerMachine, toState)
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset, StateMachineAsset, AnimationStateAsset> OnLayerAnyStateTransitionSelected;

        /// <summary>
        /// Fired when selection is cleared within a specific layer context.
        /// Args: (rootMachine, layer, layerMachine)
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset, StateMachineAsset> OnLayerSelectionCleared;

        #endregion

        #region Layer Events

        /// <summary>
        /// Fired when a layer is added to a multi-layer state machine.
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset> OnLayerAdded;

        /// <summary>
        /// Fired when a layer is removed from a multi-layer state machine.
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset> OnLayerRemoved;

        /// <summary>
        /// Fired when a layer's properties change (weight, blend mode).
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset> OnLayerChanged;

        /// <summary>
        /// Fired when user enters a layer for editing (navigation).
        /// Args: (rootMachine, layer, layerStateMachine)
        /// </summary>
        public static event Action<StateMachineAsset, LayerStateAsset, StateMachineAsset> OnLayerEntered;

        /// <summary>
        /// Fired when the state machine is converted to multi-layer mode.
        /// </summary>
        public static event Action<StateMachineAsset> OnConvertedToMultiLayer;

        #endregion

        #region General Events

        /// <summary>
        /// Fired when any change occurs that requires a full refresh.
        /// Use sparingly - prefer specific events.
        /// </summary>
        public static event Action<StateMachineAsset> OnStateMachineChanged;

        /// <summary>
        /// Fired when the graph view needs to repopulate (e.g., undo/redo).
        /// </summary>
        public static event Action<StateMachineAsset> OnGraphNeedsRepopulate;

        /// <summary>
        /// Fired when a SubStateMachine is entered (navigation).
        /// Args: (parentMachine, enteredSubMachine)
        /// </summary>
        public static event Action<StateMachineAsset, StateMachineAsset> OnSubStateMachineEntered;

        /// <summary>
        /// Fired when navigating back from a SubStateMachine.
        /// Args: (returnedToMachine)
        /// </summary>
        public static event Action<StateMachineAsset> OnSubStateMachineExited;

        /// <summary>
        /// Fired when user requests navigation to a specific level via breadcrumb.
        /// Args: (targetMachine, stackIndex)
        /// </summary>
        public static event Action<StateMachineAsset, int> OnBreadcrumbNavigationRequested;

        #endregion

        #region Raise Methods - States
        // These methods are called by editor components to fire events.
        // External code should subscribe to events, not call Raise methods directly.

        /// <summary>Raises <see cref="OnStateAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            // Legacy events (for backward compatibility)
            OnStateAdded?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
            
            // New unified event system
            DMotionEditorEventSystem.RaiseStateAdded(machine, state);
            DMotionEditorEventSystem.RaiseStateMachineChanged(machine);
        }

        /// <summary>Raises <see cref="OnStateRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            // Legacy events (for backward compatibility)
            OnStateRemoved?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
            
            // New unified event system
            DMotionEditorEventSystem.RaiseStateRemoved(machine, state);
            DMotionEditorEventSystem.RaiseStateMachineChanged(machine);
        }

        /// <summary>Raises <see cref="OnDefaultStateChanged"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseDefaultStateChanged(StateMachineAsset machine, AnimationStateAsset newDefault, AnimationStateAsset previous)
        {
            OnDefaultStateChanged?.Invoke(machine, newDefault, previous);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Parameters

        /// <summary>Raises <see cref="OnParameterAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseParameterAdded(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterAdded?.Invoke(machine, param);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnParameterRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseParameterRemoved(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterRemoved?.Invoke(machine, param);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnParameterChanged"/> only (no general change event).</summary>
        public static void RaiseParameterChanged(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterChanged?.Invoke(machine, param);
        }

        #endregion

        #region Raise Methods - Transitions

        /// <summary>Raises <see cref="OnTransitionAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseTransitionAdded(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnTransitionAdded?.Invoke(machine, fromState, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnTransitionRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseTransitionRemoved(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnTransitionRemoved?.Invoke(machine, fromState, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnAnyStateTransitionAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseAnyStateTransitionAdded(StateMachineAsset machine, AnimationStateAsset toState)
        {
            OnAnyStateTransitionAdded?.Invoke(machine, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnAnyStateTransitionRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseAnyStateTransitionRemoved(StateMachineAsset machine, AnimationStateAsset toState)
        {
            OnAnyStateTransitionRemoved?.Invoke(machine, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Exit States

        /// <summary>Raises <see cref="OnExitStateAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseExitStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnExitStateAdded?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnExitStateRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseExitStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnExitStateRemoved?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnAnyStateExitTransitionChanged"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseAnyStateExitTransitionChanged(StateMachineAsset machine, bool hasExitTransition)
        {
            OnAnyStateExitTransitionChanged?.Invoke(machine, hasExitTransition);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Links

        /// <summary>Raises <see cref="OnLinkAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseLinkAdded(StateMachineAsset machine, ParameterLink link)
        {
            OnLinkAdded?.Invoke(machine, link);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnLinkRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseLinkRemoved(StateMachineAsset machine, ParameterLink link)
        {
            OnLinkRemoved?.Invoke(machine, link);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnDependenciesResolved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseDependenciesResolved(StateMachineAsset machine, SubStateMachineStateAsset subMachine, int resolvedCount)
        {
            OnDependenciesResolved?.Invoke(machine, subMachine, resolvedCount);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Selection
        // Selection events do NOT trigger OnStateMachineChanged (selection is transient).

        /// <summary>Raises <see cref="OnStateSelected"/>.</summary>
        public static void RaiseStateSelected(StateMachineAsset machine, AnimationStateAsset state)
        {
            // Legacy events (for backward compatibility)
            OnStateSelected?.Invoke(machine, state);
            
            // New unified event system
            DMotionEditorEventSystem.RaiseStateSelected(machine, state);
        }

        /// <summary>Raises <see cref="OnTransitionSelected"/>.</summary>
        public static void RaiseTransitionSelected(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnTransitionSelected?.Invoke(machine, fromState, toState);
        }

        /// <summary>Raises <see cref="OnAnyStateSelected"/>.</summary>
        public static void RaiseAnyStateSelected(StateMachineAsset machine)
        {
            OnAnyStateSelected?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnAnyStateTransitionSelected"/>.</summary>
        public static void RaiseAnyStateTransitionSelected(StateMachineAsset machine, AnimationStateAsset toState)
        {
            OnAnyStateTransitionSelected?.Invoke(machine, toState);
        }

        /// <summary>Raises <see cref="OnExitNodeSelected"/>.</summary>
        public static void RaiseExitNodeSelected(StateMachineAsset machine)
        {
            OnExitNodeSelected?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnSelectionCleared"/>.</summary>
        public static void RaiseSelectionCleared(StateMachineAsset machine)
        {
            OnSelectionCleared?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Layer-Aware Selection
        // Layer-aware selection events do NOT trigger OnStateMachineChanged (selection is transient).

        /// <summary>Raises <see cref="OnLayerStateSelected"/>.</summary>
        public static void RaiseLayerStateSelected(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine, AnimationStateAsset state)
        {
            OnLayerStateSelected?.Invoke(rootMachine, layer, layerMachine, state);
        }

        /// <summary>Raises <see cref="OnLayerTransitionSelected"/>.</summary>
        public static void RaiseLayerTransitionSelected(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnLayerTransitionSelected?.Invoke(rootMachine, layer, layerMachine, fromState, toState);
        }

        /// <summary>Raises <see cref="OnLayerAnyStateSelected"/>.</summary>
        public static void RaiseLayerAnyStateSelected(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine)
        {
            OnLayerAnyStateSelected?.Invoke(rootMachine, layer, layerMachine);
        }

        /// <summary>Raises <see cref="OnLayerAnyStateTransitionSelected"/>.</summary>
        public static void RaiseLayerAnyStateTransitionSelected(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine, AnimationStateAsset toState)
        {
            OnLayerAnyStateTransitionSelected?.Invoke(rootMachine, layer, layerMachine, toState);
        }

        /// <summary>Raises <see cref="OnLayerSelectionCleared"/>.</summary>
        public static void RaiseLayerSelectionCleared(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine)
        {
            OnLayerSelectionCleared?.Invoke(rootMachine, layer, layerMachine);
        }

        #endregion

        #region Raise Methods - General
        // Navigation events do NOT trigger OnStateMachineChanged (navigation is view-only).

        /// <summary>Raises <see cref="OnStateMachineChanged"/>. Use sparingly.</summary>
        public static void RaiseStateMachineChanged(StateMachineAsset machine)
        {
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnGraphNeedsRepopulate"/>. Used after undo/redo.</summary>
        public static void RaiseGraphNeedsRepopulate(StateMachineAsset machine)
        {
            OnGraphNeedsRepopulate?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnSubStateMachineEntered"/>.</summary>
        public static void RaiseSubStateMachineEntered(StateMachineAsset parent, StateMachineAsset entered)
        {
            OnSubStateMachineEntered?.Invoke(parent, entered);
        }

        /// <summary>Raises <see cref="OnSubStateMachineExited"/>.</summary>
        public static void RaiseSubStateMachineExited(StateMachineAsset returnedTo)
        {
            OnSubStateMachineExited?.Invoke(returnedTo);
        }

        /// <summary>Raises <see cref="OnBreadcrumbNavigationRequested"/>.</summary>
        public static void RaiseBreadcrumbNavigationRequested(StateMachineAsset target, int stackIndex)
        {
            OnBreadcrumbNavigationRequested?.Invoke(target, stackIndex);
        }

        #endregion

        #region Raise Methods - Layers

        /// <summary>Raises <see cref="OnLayerAdded"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseLayerAdded(StateMachineAsset machine, LayerStateAsset layer)
        {
            // Legacy events (for backward compatibility)
            OnLayerAdded?.Invoke(machine, layer);
            OnStateMachineChanged?.Invoke(machine);
            
            // New unified event system
            var layerIndex = machine.GetLayers().IndexOf(layer);
            DMotionEditorEventSystem.RaiseLayerAdded(machine, layer, layerIndex);
            DMotionEditorEventSystem.RaiseStateMachineChanged(machine);
        }

        /// <summary>Raises <see cref="OnLayerRemoved"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseLayerRemoved(StateMachineAsset machine, LayerStateAsset layer)
        {
            OnLayerRemoved?.Invoke(machine, layer);
            OnStateMachineChanged?.Invoke(machine);
        }

        /// <summary>Raises <see cref="OnLayerChanged"/>.</summary>
        public static void RaiseLayerChanged(StateMachineAsset machine, LayerStateAsset layer)
        {
            OnLayerChanged?.Invoke(machine, layer);
        }

        /// <summary>Raises <see cref="OnLayerEntered"/>.</summary>
        public static void RaiseLayerEntered(StateMachineAsset rootMachine, LayerStateAsset layer, StateMachineAsset layerMachine)
        {
            OnLayerEntered?.Invoke(rootMachine, layer, layerMachine);
        }

        /// <summary>Raises <see cref="OnConvertedToMultiLayer"/> and <see cref="OnStateMachineChanged"/>.</summary>
        public static void RaiseConvertedToMultiLayer(StateMachineAsset machine)
        {
            OnConvertedToMultiLayer?.Invoke(machine);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Clears all event subscriptions. Call when editor window closes.
        /// </summary>
        public static void ClearAllSubscriptions()
        {
            OnStateAdded = null;
            OnStateRemoved = null;
            OnDefaultStateChanged = null;
            OnParameterAdded = null;
            OnParameterRemoved = null;
            OnParameterChanged = null;
            OnTransitionAdded = null;
            OnTransitionRemoved = null;
            OnAnyStateTransitionAdded = null;
            OnAnyStateTransitionRemoved = null;
            OnExitStateAdded = null;
            OnExitStateRemoved = null;
            OnAnyStateExitTransitionChanged = null;
            OnLinkAdded = null;
            OnLinkRemoved = null;
            OnDependenciesResolved = null;
            OnStateSelected = null;
            OnTransitionSelected = null;
            OnAnyStateSelected = null;
            OnAnyStateTransitionSelected = null;
            OnExitNodeSelected = null;
            OnSelectionCleared = null;
            OnLayerStateSelected = null;
            OnLayerTransitionSelected = null;
            OnLayerAnyStateSelected = null;
            OnLayerAnyStateTransitionSelected = null;
            OnLayerSelectionCleared = null;
            OnStateMachineChanged = null;
            OnGraphNeedsRepopulate = null;
            OnSubStateMachineEntered = null;
            OnSubStateMachineExited = null;
            OnBreadcrumbNavigationRequested = null;
            OnLayerAdded = null;
            OnLayerRemoved = null;
            OnLayerChanged = null;
            OnLayerEntered = null;
            OnConvertedToMultiLayer = null;
        }

        #endregion
    }
}
