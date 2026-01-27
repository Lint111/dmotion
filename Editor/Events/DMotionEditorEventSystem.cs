using System;
using System.ComponentModel;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Unified event system for all DMotion editor components.
    /// Consolidates StateMachineEditorEvents, AnimationPreviewEvents, and component-specific events
    /// into a single PropertyChanged pattern for better maintainability and type safety.
    /// </summary>
    public static class DMotionEditorEventSystem
    {
        #region Events
        
        /// <summary>
        /// Fired when any DMotion editor property changes.
        /// Use PropertyName and Context to determine what changed and where.
        /// </summary>
        public static event EventHandler<DMotionPropertyChangedEventArgs> PropertyChanged;
        
        #endregion
        
        #region Property Names
        
        /// <summary>
        /// Standard property names for DMotion editor events.
        /// Organized by functional area for easy discovery.
        /// </summary>
        public static class PropertyNames
        {
            #region State Machine Structure
            
            public const string StateAdded = nameof(StateAdded);
            public const string StateRemoved = nameof(StateRemoved);
            public const string DefaultStateChanged = nameof(DefaultStateChanged);
            public const string TransitionAdded = nameof(TransitionAdded);
            public const string TransitionRemoved = nameof(TransitionRemoved);
            public const string AnyStateTransitionAdded = nameof(AnyStateTransitionAdded);
            public const string AnyStateTransitionRemoved = nameof(AnyStateTransitionRemoved);
            public const string ExitStateAdded = nameof(ExitStateAdded);
            public const string ExitStateRemoved = nameof(ExitStateRemoved);
            public const string AnyStateExitTransitionChanged = nameof(AnyStateExitTransitionChanged);
            
            #endregion
            
            #region Parameters
            
            public const string ParameterAdded = nameof(ParameterAdded);
            public const string ParameterRemoved = nameof(ParameterRemoved);
            public const string ParameterChanged = nameof(ParameterChanged);
            public const string ParameterValueChanged = nameof(ParameterValueChanged);
            
            #endregion
            
            #region Layers
            
            public const string LayerAdded = nameof(LayerAdded);
            public const string LayerRemoved = nameof(LayerRemoved);
            public const string LayerChanged = nameof(LayerChanged);
            public const string LayerWeight = nameof(LayerWeight);
            public const string LayerEnabled = nameof(LayerEnabled);
            public const string LayerBlendMode = nameof(LayerBlendMode);
            public const string ConvertedToMultiLayer = nameof(ConvertedToMultiLayer);
            
            #endregion
            
            #region Selection
            
            public const string StateSelected = nameof(StateSelected);
            public const string TransitionSelected = nameof(TransitionSelected);
            public const string AnyStateSelected = nameof(AnyStateSelected);
            public const string AnyStateTransitionSelected = nameof(AnyStateTransitionSelected);
            public const string ExitNodeSelected = nameof(ExitNodeSelected);
            public const string SelectionCleared = nameof(SelectionCleared);
            
            #endregion
            
            #region Navigation
            
            public const string LayerEntered = nameof(LayerEntered);
            public const string LayerExited = nameof(LayerExited);
            public const string SubStateMachineEntered = nameof(SubStateMachineEntered);
            public const string SubStateMachineExited = nameof(SubStateMachineExited);
            public const string BreadcrumbNavigationRequested = nameof(BreadcrumbNavigationRequested);
            public const string NavigateToState = nameof(NavigateToState);
            public const string NavigateToTransition = nameof(NavigateToTransition);
            
            #endregion
            
            #region Preview and Playback
            
            public const string PreviewCreated = nameof(PreviewCreated);
            public const string PreviewDisposed = nameof(PreviewDisposed);
            public const string PreviewError = nameof(PreviewError);
            public const string PreviewMode = nameof(PreviewMode);
            public const string PreviewType = nameof(PreviewType);
            public const string PreviewModel = nameof(PreviewModel);
            public const string NormalizedTime = nameof(NormalizedTime);
            public const string IsPlaying = nameof(IsPlaying);
            public const string IsLooping = nameof(IsLooping);
            public const string PlaybackSpeed = nameof(PlaybackSpeed);
            public const string SyncLayers = nameof(SyncLayers);
            
            #endregion
            
            #region Blend Properties
            
            public const string BlendPosition1D = nameof(BlendPosition1D);
            public const string BlendPosition2D = nameof(BlendPosition2D);
            public const string BlendState = nameof(BlendState);
            public const string SoloClip = nameof(SoloClip);
            public const string ClipWeights = nameof(ClipWeights);
            public const string ClipThreshold = nameof(ClipThreshold);
            public const string ClipPosition = nameof(ClipPosition);
            
            #endregion
            
            #region Transitions
            
            public const string TransitionProgress = nameof(TransitionProgress);
            public const string TransitionTime = nameof(TransitionTime);
            public const string TransitionDuration = nameof(TransitionDuration);
            public const string TransitionOffset = nameof(TransitionOffset);
            public const string ExitTime = nameof(ExitTime);
            public const string TransitionFromBlendPosition = nameof(TransitionFromBlendPosition);
            public const string TransitionToBlendPosition = nameof(TransitionToBlendPosition);
            
            #endregion
            
            #region UI State
            
            public const string EditMode = nameof(EditMode);
            public const string BlendSpaceEditMode = nameof(BlendSpaceEditMode);
            public const string SelectionChanged = nameof(SelectionChanged);
            public const string ClipSelectedForPreview = nameof(ClipSelectedForPreview);
            public const string PreviewPositionChanged = nameof(PreviewPositionChanged);
            public const string RepaintRequested = nameof(RepaintRequested);
            
            #endregion
            
            #region Docking
            
            public const string Undock = nameof(Undock);
            public const string Dock = nameof(Dock);
            
            #endregion
            
            #region Parameter Links
            
            public const string LinkAdded = nameof(LinkAdded);
            public const string LinkRemoved = nameof(LinkRemoved);
            public const string DependenciesResolved = nameof(DependenciesResolved);
            
            #endregion
            
            #region General
            
            public const string StateMachineChanged = nameof(StateMachineChanged);
            public const string GraphNeedsRepopulate = nameof(GraphNeedsRepopulate);
            
            #endregion
        }
        
        #endregion
        
        #region Context Types
        
        /// <summary>
        /// Context information for DMotion editor property changes.
        /// Provides rich information about where and what changed.
        /// </summary>
        public class DMotionEditorContext
        {
            // Core assets
            public StateMachineAsset StateMachine { get; set; }
            public AnimationStateAsset State { get; set; }
            public AnimationParameterAsset Parameter { get; set; }
            public LayerStateAsset Layer { get; set; }
            public ParameterLink Link { get; set; }
            
            // Layer context
            public int? LayerIndex { get; set; }
            public StateMachineAsset LayerStateMachine { get; set; }
            
            // Transition context
            public AnimationStateAsset TransitionFrom { get; set; }
            public AnimationStateAsset TransitionTo { get; set; }
            public bool IsAnyState { get; set; }
            
            // UI context
            public string ComponentId { get; set; }
            public int? ClipIndex { get; set; }
            public int? StackIndex { get; set; }
            
            // Factory methods for common contexts
            public static DMotionEditorContext StateMachine(StateMachineAsset stateMachine) =>
                new() { StateMachine = stateMachine };
                
            public static DMotionEditorContext State(StateMachineAsset stateMachine, AnimationStateAsset state) =>
                new() { StateMachine = stateMachine, State = state };
                
            public static DMotionEditorContext Layer(StateMachineAsset stateMachine, LayerStateAsset layer, int layerIndex) =>
                new() { StateMachine = stateMachine, Layer = layer, LayerIndex = layerIndex };
                
            public static DMotionEditorContext Transition(StateMachineAsset stateMachine, AnimationStateAsset from, AnimationStateAsset to, bool isAnyState = false) =>
                new() { StateMachine = stateMachine, TransitionFrom = from, TransitionTo = to, IsAnyState = isAnyState };
                
            public static DMotionEditorContext Parameter(StateMachineAsset stateMachine, AnimationParameterAsset parameter) =>
                new() { StateMachine = stateMachine, Parameter = parameter };
                
            public static DMotionEditorContext Component(string componentId) =>
                new() { ComponentId = componentId };
        }
        
        #endregion
        
        #region Event Args
        
        /// <summary>
        /// Enhanced property changed event args for DMotion editor events.
        /// </summary>
        public class DMotionPropertyChangedEventArgs : PropertyChangedEventArgs
        {
            public DMotionEditorContext Context { get; }
            public object OldValue { get; }
            public object NewValue { get; }
            public DateTime Timestamp { get; }
            
            public DMotionPropertyChangedEventArgs(string propertyName, DMotionEditorContext context, object oldValue = null, object newValue = null)
                : base(propertyName)
            {
                Context = context ?? new DMotionEditorContext();
                OldValue = oldValue;
                NewValue = newValue;
                Timestamp = DateTime.UtcNow;
            }
        }
        
        #endregion
        
        #region Raise Methods - State Machine Structure
        
        public static void RaiseStateAdded(StateMachineAsset machine, AnimationStateAsset state)
        {
            var context = DMotionEditorContext.State(machine, state);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.StateAdded, context, null, state);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseStateRemoved(StateMachineAsset machine, AnimationStateAsset state)
        {
            var context = DMotionEditorContext.State(machine, state);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.StateRemoved, context, state, null);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseDefaultStateChanged(StateMachineAsset machine, AnimationStateAsset newDefault, AnimationStateAsset oldDefault)
        {
            var context = DMotionEditorContext.StateMachine(machine);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.DefaultStateChanged, context, oldDefault, newDefault);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseTransitionAdded(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            var context = DMotionEditorContext.Transition(machine, fromState, toState);
            var transition = new TransitionInfo { FromState = fromState, ToState = toState };
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.TransitionAdded, context, null, transition);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseTransitionRemoved(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            var context = DMotionEditorContext.Transition(machine, fromState, toState);
            var transition = new TransitionInfo { FromState = fromState, ToState = toState };
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.TransitionRemoved, context, transition, null);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Parameters
        
        public static void RaiseParameterAdded(StateMachineAsset machine, AnimationParameterAsset parameter)
        {
            var context = DMotionEditorContext.Parameter(machine, parameter);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.ParameterAdded, context, null, parameter);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseParameterRemoved(StateMachineAsset machine, AnimationParameterAsset parameter)
        {
            var context = DMotionEditorContext.Parameter(machine, parameter);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.ParameterRemoved, context, parameter, null);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseParameterChanged(StateMachineAsset machine, AnimationParameterAsset parameter, object oldValue = null, object newValue = null)
        {
            var context = DMotionEditorContext.Parameter(machine, parameter);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.ParameterChanged, context, oldValue, newValue);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Layers
        
        public static void RaiseLayerAdded(StateMachineAsset machine, LayerStateAsset layer, int layerIndex)
        {
            var context = DMotionEditorContext.Layer(machine, layer, layerIndex);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.LayerAdded, context, null, layer);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseLayerRemoved(StateMachineAsset machine, LayerStateAsset layer, int layerIndex)
        {
            var context = DMotionEditorContext.Layer(machine, layer, layerIndex);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.LayerRemoved, context, layer, null);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseLayerWeightChanged(StateMachineAsset machine, LayerStateAsset layer, int layerIndex, float oldWeight, float newWeight)
        {
            var context = DMotionEditorContext.Layer(machine, layer, layerIndex);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.LayerWeight, context, oldWeight, newWeight);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseLayerEnabledChanged(StateMachineAsset machine, LayerStateAsset layer, int layerIndex, bool oldEnabled, bool newEnabled)
        {
            var context = DMotionEditorContext.Layer(machine, layer, layerIndex);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.LayerEnabled, context, oldEnabled, newEnabled);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Selection
        
        public static void RaiseStateSelected(StateMachineAsset machine, AnimationStateAsset state, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? DMotionEditorContext.Layer(machine, layer, layerIndex ?? -1) 
                : DMotionEditorContext.StateMachine(machine);
            context.State = state;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.StateSelected, context, null, state);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseTransitionSelected(StateMachineAsset machine, AnimationStateAsset fromState, AnimationStateAsset toState, bool isAnyState = false, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? DMotionEditorContext.Layer(machine, layer, layerIndex ?? -1) 
                : DMotionEditorContext.Transition(machine, fromState, toState, isAnyState);
            context.TransitionFrom = fromState;
            context.TransitionTo = toState;
            context.IsAnyState = isAnyState;
            var transition = new TransitionInfo { FromState = fromState, ToState = toState, IsAnyState = isAnyState };
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.TransitionSelected, context, null, transition);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseSelectionCleared(StateMachineAsset machine, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? DMotionEditorContext.Layer(machine, layer, layerIndex ?? -1) 
                : DMotionEditorContext.StateMachine(machine);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.SelectionCleared, context);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Preview
        
        public static void RaiseNormalizedTimeChanged(StateMachineAsset machine, float oldTime, float newTime, AnimationStateAsset state = null, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? DMotionEditorContext.Layer(machine, layer, layerIndex ?? -1)
                : state != null 
                    ? DMotionEditorContext.State(machine, state)
                    : DMotionEditorContext.StateMachine(machine);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.NormalizedTime, context, oldTime, newTime);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaisePlaybackStateChanged(StateMachineAsset machine, bool oldPlaying, bool newPlaying, AnimationStateAsset state = null)
        {
            var context = state != null 
                ? DMotionEditorContext.State(machine, state)
                : DMotionEditorContext.StateMachine(machine);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.IsPlaying, context, oldPlaying, newPlaying);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseBlendPosition1DChanged(StateMachineAsset machine, AnimationStateAsset state, float oldPosition, float newPosition, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? DMotionEditorContext.Layer(machine, layer, layerIndex ?? -1)
                : DMotionEditorContext.State(machine, state);
            context.State = state;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.BlendPosition1D, context, oldPosition, newPosition);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseBlendPosition2DChanged(StateMachineAsset machine, AnimationStateAsset state, Vector2 oldPosition, Vector2 newPosition, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? DMotionEditorContext.Layer(machine, layer, layerIndex ?? -1)
                : DMotionEditorContext.State(machine, state);
            context.State = state;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.BlendPosition2D, context, oldPosition, newPosition);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Navigation
        
        public static void RaiseNavigateToState(AnimationStateAsset state, StateMachineAsset machine = null)
        {
            var context = DMotionEditorContext.State(machine, state);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.NavigateToState, context, null, state);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseLayerEntered(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerMachine)
        {
            var context = DMotionEditorContext.Layer(rootMachine, layer, layerIndex);
            context.LayerStateMachine = layerMachine;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.LayerEntered, context, null, layer);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - UI Components
        
        public static void RaiseClipSelectedForPreview(AnimationStateAsset state, int oldClipIndex, int newClipIndex, string componentId = null)
        {
            var context = DMotionEditorContext.Component(componentId);
            context.State = state;
            context.ClipIndex = newClipIndex;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.ClipSelectedForPreview, context, oldClipIndex, newClipIndex);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseEditModeChanged(bool oldEditMode, bool newEditMode, string componentId = null, AnimationStateAsset state = null)
        {
            var context = DMotionEditorContext.Component(componentId);
            context.State = state;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.EditMode, context, oldEditMode, newEditMode);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseRepaintRequested(string componentId = null, AnimationStateAsset state = null)
        {
            var context = DMotionEditorContext.Component(componentId);
            context.State = state;
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.RepaintRequested, context);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - General
        
        public static void RaiseStateMachineChanged(StateMachineAsset machine)
        {
            var context = DMotionEditorContext.StateMachine(machine);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.StateMachineChanged, context);
            PropertyChanged?.Invoke(null, args);
        }
        
        public static void RaiseGraphNeedsRepopulate(StateMachineAsset machine)
        {
            var context = DMotionEditorContext.StateMachine(machine);
            var args = new DMotionPropertyChangedEventArgs(PropertyNames.GraphNeedsRepopulate, context);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Utility
        
        /// <summary>
        /// Clears all event subscriptions.
        /// </summary>
        public static void ClearAllSubscriptions()
        {
            PropertyChanged = null;
        }
        
        #endregion
    }
    
    #region Helper Types
    
    /// <summary>
    /// Represents transition information.
    /// </summary>
    public class TransitionInfo
    {
        public AnimationStateAsset FromState { get; set; }
        public AnimationStateAsset ToState { get; set; }
        public bool IsAnyState { get; set; }
    }
    
    #endregion
}