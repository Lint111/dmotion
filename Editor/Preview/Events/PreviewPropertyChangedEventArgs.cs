using System;
using System.ComponentModel;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Enhanced property changed event args for preview system.
    /// Provides context about what changed and where.
    /// </summary>
    public class PreviewPropertyChangedEventArgs : PropertyChangedEventArgs
    {
        /// <summary>
        /// The context where the property changed.
        /// </summary>
        public PreviewContext Context { get; }
        
        /// <summary>
        /// The old value (if applicable).
        /// </summary>
        public object OldValue { get; }
        
        /// <summary>
        /// The new value (if applicable).
        /// </summary>
        public object NewValue { get; }
        
        public PreviewPropertyChangedEventArgs(string propertyName, PreviewContext context, object oldValue = null, object newValue = null)
            : base(propertyName)
        {
            Context = context;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
    
    /// <summary>
    /// Context information for preview property changes.
    /// Identifies where the change occurred in the preview hierarchy.
    /// </summary>
    public class PreviewContext
    {
        /// <summary>
        /// The root state machine being previewed.
        /// </summary>
        public StateMachineAsset RootStateMachine { get; set; }
        
        /// <summary>
        /// The layer where the change occurred (null for global changes).
        /// </summary>
        public LayerStateAsset Layer { get; set; }
        
        /// <summary>
        /// The layer index (if applicable).
        /// </summary>
        public int? LayerIndex { get; set; }
        
        /// <summary>
        /// The current layer state machine (if navigated into a layer).
        /// </summary>
        public StateMachineAsset LayerStateMachine { get; set; }
        
        /// <summary>
        /// The selected state (if applicable).
        /// </summary>
        public AnimationStateAsset SelectedState { get; set; }
        
        /// <summary>
        /// The transition from state (if applicable).
        /// </summary>
        public AnimationStateAsset TransitionFrom { get; set; }
        
        /// <summary>
        /// The transition to state (if applicable).
        /// </summary>
        public AnimationStateAsset TransitionTo { get; set; }
        
        /// <summary>
        /// Whether this is an Any State context.
        /// </summary>
        public bool IsAnyState { get; set; }
        
        /// <summary>
        /// Creates a global context (no layer).
        /// </summary>
        public static PreviewContext Global(StateMachineAsset rootStateMachine)
        {
            return new PreviewContext { RootStateMachine = rootStateMachine };
        }
        
        /// <summary>
        /// Creates a layer context.
        /// </summary>
        public static PreviewContext Layer(StateMachineAsset rootStateMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerStateMachine = null)
        {
            return new PreviewContext 
            { 
                RootStateMachine = rootStateMachine,
                Layer = layer,
                LayerIndex = layerIndex,
                LayerStateMachine = layerStateMachine
            };
        }
        
        /// <summary>
        /// Creates a state selection context.
        /// </summary>
        public static PreviewContext StateSelection(StateMachineAsset rootStateMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerStateMachine, AnimationStateAsset selectedState)
        {
            return new PreviewContext 
            { 
                RootStateMachine = rootStateMachine,
                Layer = layer,
                LayerIndex = layerIndex,
                LayerStateMachine = layerStateMachine,
                SelectedState = selectedState
            };
        }
        
        /// <summary>
        /// Creates a transition selection context.
        /// </summary>
        public static PreviewContext TransitionSelection(StateMachineAsset rootStateMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerStateMachine, AnimationStateAsset fromState, AnimationStateAsset toState, bool isAnyState = false)
        {
            return new PreviewContext 
            { 
                RootStateMachine = rootStateMachine,
                Layer = layer,
                LayerIndex = layerIndex,
                LayerStateMachine = layerStateMachine,
                TransitionFrom = fromState,
                TransitionTo = toState,
                IsAnyState = isAnyState
            };
        }
    }
}