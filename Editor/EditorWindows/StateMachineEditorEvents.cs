using System;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Centralized event system for the State Machine Editor.
    /// All panels subscribe to these events to stay synchronized.
    /// This decouples editor components and provides a clean public API for extensions.
    /// </summary>
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

        #endregion

        #region Raise Methods - States

        public static void RaiseStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnStateAdded?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnStateRemoved?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseDefaultStateChanged(StateMachineAsset machine, AnimationStateAsset newDefault, AnimationStateAsset previous)
        {
            OnDefaultStateChanged?.Invoke(machine, newDefault, previous);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Parameters

        public static void RaiseParameterAdded(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterAdded?.Invoke(machine, param);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseParameterRemoved(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterRemoved?.Invoke(machine, param);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseParameterChanged(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterChanged?.Invoke(machine, param);
        }

        #endregion

        #region Raise Methods - Transitions

        public static void RaiseTransitionAdded(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnTransitionAdded?.Invoke(machine, fromState, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseTransitionRemoved(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnTransitionRemoved?.Invoke(machine, fromState, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseAnyStateTransitionAdded(StateMachineAsset machine, AnimationStateAsset toState)
        {
            OnAnyStateTransitionAdded?.Invoke(machine, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseAnyStateTransitionRemoved(StateMachineAsset machine, AnimationStateAsset toState)
        {
            OnAnyStateTransitionRemoved?.Invoke(machine, toState);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Exit States

        public static void RaiseExitStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnExitStateAdded?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseExitStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnExitStateRemoved?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseAnyStateExitTransitionChanged(StateMachineAsset machine, bool hasExitTransition)
        {
            OnAnyStateExitTransitionChanged?.Invoke(machine, hasExitTransition);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Links

        public static void RaiseLinkAdded(StateMachineAsset machine, ParameterLink link)
        {
            OnLinkAdded?.Invoke(machine, link);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseLinkRemoved(StateMachineAsset machine, ParameterLink link)
        {
            OnLinkRemoved?.Invoke(machine, link);
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseDependenciesResolved(StateMachineAsset machine, SubStateMachineStateAsset subMachine, int resolvedCount)
        {
            OnDependenciesResolved?.Invoke(machine, subMachine, resolvedCount);
            OnStateMachineChanged?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - Selection

        public static void RaiseStateSelected(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnStateSelected?.Invoke(machine, state);
        }

        public static void RaiseTransitionSelected(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            OnTransitionSelected?.Invoke(machine, fromState, toState);
        }

        public static void RaiseAnyStateSelected(StateMachineAsset machine)
        {
            OnAnyStateSelected?.Invoke(machine);
        }

        public static void RaiseAnyStateTransitionSelected(StateMachineAsset machine, AnimationStateAsset toState)
        {
            OnAnyStateTransitionSelected?.Invoke(machine, toState);
        }

        public static void RaiseExitNodeSelected(StateMachineAsset machine)
        {
            OnExitNodeSelected?.Invoke(machine);
        }

        public static void RaiseSelectionCleared(StateMachineAsset machine)
        {
            OnSelectionCleared?.Invoke(machine);
        }

        #endregion

        #region Raise Methods - General

        public static void RaiseStateMachineChanged(StateMachineAsset machine)
        {
            OnStateMachineChanged?.Invoke(machine);
        }

        public static void RaiseGraphNeedsRepopulate(StateMachineAsset machine)
        {
            OnGraphNeedsRepopulate?.Invoke(machine);
        }

        public static void RaiseSubStateMachineEntered(StateMachineAsset parent, StateMachineAsset entered)
        {
            OnSubStateMachineEntered?.Invoke(parent, entered);
        }

        public static void RaiseSubStateMachineExited(StateMachineAsset returnedTo)
        {
            OnSubStateMachineExited?.Invoke(returnedTo);
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
            OnStateMachineChanged = null;
            OnGraphNeedsRepopulate = null;
            OnSubStateMachineEntered = null;
            OnSubStateMachineExited = null;
        }

        #endregion
    }
}
