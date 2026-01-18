using System;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Centralized event system for the State Machine Editor.
    /// All panels subscribe to these events to stay synchronized.
    /// </summary>
    internal static class StateMachineEditorEvents
    {
        /// <summary>
        /// Fired when a state is added to the state machine.
        /// </summary>
        internal static event Action<StateMachineAsset, AnimationStateAsset> OnStateAdded;

        /// <summary>
        /// Fired when a state is removed from the state machine.
        /// </summary>
        internal static event Action<StateMachineAsset, AnimationStateAsset> OnStateRemoved;

        /// <summary>
        /// Fired when a parameter is added to the state machine.
        /// </summary>
        internal static event Action<StateMachineAsset, AnimationParameterAsset> OnParameterAdded;

        /// <summary>
        /// Fired when a parameter is removed from the state machine.
        /// </summary>
        internal static event Action<StateMachineAsset, AnimationParameterAsset> OnParameterRemoved;

        /// <summary>
        /// Fired when a parameter link is added.
        /// </summary>
        internal static event Action<StateMachineAsset, ParameterLink> OnLinkAdded;

        /// <summary>
        /// Fired when a parameter link is removed.
        /// </summary>
        internal static event Action<StateMachineAsset, ParameterLink> OnLinkRemoved;

        /// <summary>
        /// Fired when any change occurs that requires a full refresh.
        /// Use sparingly - prefer specific events.
        /// </summary>
        internal static event Action<StateMachineAsset> OnStateMachineChanged;

        // Raise methods

        internal static void RaiseStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnStateAdded?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        internal static void RaiseStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            OnStateRemoved?.Invoke(machine, state);
            OnStateMachineChanged?.Invoke(machine);
        }

        internal static void RaiseParameterAdded(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterAdded?.Invoke(machine, param);
            OnStateMachineChanged?.Invoke(machine);
        }

        internal static void RaiseParameterRemoved(StateMachineAsset machine, AnimationParameterAsset param)
        {
            OnParameterRemoved?.Invoke(machine, param);
            OnStateMachineChanged?.Invoke(machine);
        }

        internal static void RaiseLinkAdded(StateMachineAsset machine, ParameterLink link)
        {
            OnLinkAdded?.Invoke(machine, link);
            OnStateMachineChanged?.Invoke(machine);
        }

        internal static void RaiseLinkRemoved(StateMachineAsset machine, ParameterLink link)
        {
            OnLinkRemoved?.Invoke(machine, link);
            OnStateMachineChanged?.Invoke(machine);
        }

        internal static void RaiseStateMachineChanged(StateMachineAsset machine)
        {
            OnStateMachineChanged?.Invoke(machine);
        }
    }
}
