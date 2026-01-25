namespace DMotion.Authoring
{
    /// <summary>
    /// Interface for state assets that contain a nested state machine.
    /// Implemented by both SubStateMachineStateAsset and LayerStateAsset.
    /// Used by the parameter dependency system to track dependencies across nesting boundaries.
    /// </summary>
    public interface INestedStateMachineContainer
    {
        /// <summary>
        /// The nested state machine contained by this asset.
        /// </summary>
        StateMachineAsset NestedStateMachine { get; }
        
        /// <summary>
        /// The display name of this container.
        /// </summary>
        string name { get; }
    }
}
