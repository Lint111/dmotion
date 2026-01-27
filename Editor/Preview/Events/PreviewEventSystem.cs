using System;
using System.ComponentModel;
using DMotion.Authoring;
using Unity.Mathematics;

namespace DMotion.Editor
{
    /// <summary>
    /// Centralized event system for animation preview using property change notifications.
    /// Consolidates all preview-related events into a single, type-safe system.
    /// </summary>
    public static class PreviewEventSystem
    {
        #region Events
        
        /// <summary>
        /// Fired when any preview property changes.
        /// Use PropertyName and Context to determine what changed.
        /// </summary>
        public static event EventHandler<PreviewPropertyChangedEventArgs> PropertyChanged;
        
        #endregion
        
        #region Property Names
        
        /// <summary>
        /// Standard property names for preview events.
        /// </summary>
        public static class PropertyNames
        {
            // Selection properties
            public const string SelectedState = nameof(SelectedState);
            public const string SelectedTransition = nameof(SelectedTransition);
            public const string SelectionCleared = nameof(SelectionCleared);
            
            // Layer properties
            public const string LayerWeight = nameof(LayerWeight);
            public const string LayerEnabled = nameof(LayerEnabled);
            public const string LayerBlendMode = nameof(LayerBlendMode);
            
            // Time and playback properties
            public const string NormalizedTime = nameof(NormalizedTime);
            public const string IsPlaying = nameof(IsPlaying);
            public const string PlaybackSpeed = nameof(PlaybackSpeed);
            public const string SyncLayers = nameof(SyncLayers);
            
            // Blend properties
            public const string BlendPosition1D = nameof(BlendPosition1D);
            public const string BlendPosition2D = nameof(BlendPosition2D);
            public const string TransitionProgress = nameof(TransitionProgress);
            public const string TransitionFromBlendPosition = nameof(TransitionFromBlendPosition);
            public const string TransitionToBlendPosition = nameof(TransitionToBlendPosition);
            
            // Navigation properties
            public const string NavigationRequested = nameof(NavigationRequested);
            public const string LayerEntered = nameof(LayerEntered);
            public const string LayerExited = nameof(LayerExited);
            
            // Preview mode properties
            public const string PreviewMode = nameof(PreviewMode);
            public const string PreviewType = nameof(PreviewType);
            public const string PreviewModel = nameof(PreviewModel);
            
            // Clip properties
            public const string SoloClip = nameof(SoloClip);
            public const string ClipWeights = nameof(ClipWeights);
        }
        
        #endregion
        
        #region Raise Methods - Selection
        
        /// <summary>
        /// Raises a state selection event.
        /// </summary>
        public static void RaiseStateSelected(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerMachine, AnimationStateAsset state)
        {
            var context = PreviewContext.StateSelection(rootMachine, layer, layerIndex, layerMachine, state);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.SelectedState, context, null, state);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a transition selection event.
        /// </summary>
        public static void RaiseTransitionSelected(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerMachine, AnimationStateAsset fromState, AnimationStateAsset toState, bool isAnyState = false)
        {
            var context = PreviewContext.TransitionSelection(rootMachine, layer, layerIndex, layerMachine, fromState, toState, isAnyState);
            var transition = new TransitionSelection { FromState = fromState, ToState = toState, IsAnyState = isAnyState };
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.SelectedTransition, context, null, transition);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a selection cleared event.
        /// </summary>
        public static void RaiseSelectionCleared(StateMachineAsset rootMachine, LayerStateAsset layer = null, int? layerIndex = null, StateMachineAsset layerMachine = null)
        {
            var context = layer != null 
                ? PreviewContext.Layer(rootMachine, layer, layerIndex ?? -1, layerMachine)
                : PreviewContext.Global(rootMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.SelectionCleared, context);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Layer Properties
        
        /// <summary>
        /// Raises a layer weight changed event.
        /// </summary>
        public static void RaiseLayerWeightChanged(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, float oldWeight, float newWeight)
        {
            var context = PreviewContext.Layer(rootMachine, layer, layerIndex);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.LayerWeight, context, oldWeight, newWeight);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a layer enabled changed event.
        /// </summary>
        public static void RaiseLayerEnabledChanged(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, bool oldEnabled, bool newEnabled)
        {
            var context = PreviewContext.Layer(rootMachine, layer, layerIndex);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.LayerEnabled, context, oldEnabled, newEnabled);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Time and Playback
        
        /// <summary>
        /// Raises a normalized time changed event.
        /// </summary>
        public static void RaiseNormalizedTimeChanged(StateMachineAsset rootMachine, float oldTime, float newTime, LayerStateAsset layer = null, int? layerIndex = null)
        {
            var context = layer != null 
                ? PreviewContext.Layer(rootMachine, layer, layerIndex ?? -1)
                : PreviewContext.Global(rootMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.NormalizedTime, context, oldTime, newTime);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a playback state changed event.
        /// </summary>
        public static void RaisePlaybackStateChanged(StateMachineAsset rootMachine, bool oldPlaying, bool newPlaying)
        {
            var context = PreviewContext.Global(rootMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.IsPlaying, context, oldPlaying, newPlaying);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a sync layers changed event.
        /// </summary>
        public static void RaiseSyncLayersChanged(StateMachineAsset rootMachine, bool oldSync, bool newSync)
        {
            var context = PreviewContext.Global(rootMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.SyncLayers, context, oldSync, newSync);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Blend Properties
        
        /// <summary>
        /// Raises a 1D blend position changed event.
        /// </summary>
        public static void RaiseBlendPosition1DChanged(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, AnimationStateAsset state, float oldPosition, float newPosition)
        {
            var context = PreviewContext.StateSelection(rootMachine, layer, layerIndex, null, state);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.BlendPosition1D, context, oldPosition, newPosition);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a 2D blend position changed event.
        /// </summary>
        public static void RaiseBlendPosition2DChanged(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, AnimationStateAsset state, float2 oldPosition, float2 newPosition)
        {
            var context = PreviewContext.StateSelection(rootMachine, layer, layerIndex, null, state);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.BlendPosition2D, context, oldPosition, newPosition);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a transition progress changed event.
        /// </summary>
        public static void RaiseTransitionProgressChanged(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, AnimationStateAsset fromState, AnimationStateAsset toState, float oldProgress, float newProgress)
        {
            var context = PreviewContext.TransitionSelection(rootMachine, layer, layerIndex, null, fromState, toState);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.TransitionProgress, context, oldProgress, newProgress);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Navigation
        
        /// <summary>
        /// Raises a navigation requested event.
        /// </summary>
        public static void RaiseNavigationRequested(StateMachineAsset rootMachine, LayerStateAsset targetLayer, int layerIndex)
        {
            var context = PreviewContext.Layer(rootMachine, targetLayer, layerIndex);
            var navigationTarget = new NavigationTarget { Layer = targetLayer, LayerIndex = layerIndex };
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.NavigationRequested, context, null, navigationTarget);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a layer entered event.
        /// </summary>
        public static void RaiseLayerEntered(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, StateMachineAsset layerMachine)
        {
            var context = PreviewContext.Layer(rootMachine, layer, layerIndex, layerMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.LayerEntered, context, null, layer);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Preview Mode
        
        /// <summary>
        /// Raises a preview mode changed event.
        /// </summary>
        public static void RaisePreviewModeChanged(StateMachineAsset rootMachine, PreviewMode oldMode, PreviewMode newMode)
        {
            var context = PreviewContext.Global(rootMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.PreviewMode, context, oldMode, newMode);
            PropertyChanged?.Invoke(null, args);
        }
        
        /// <summary>
        /// Raises a preview type changed event.
        /// </summary>
        public static void RaisePreviewTypeChanged(StateMachineAsset rootMachine, PreviewType oldType, PreviewType newType)
        {
            var context = PreviewContext.Global(rootMachine);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.PreviewType, context, oldType, newType);
            PropertyChanged?.Invoke(null, args);
        }
        
        #endregion
        
        #region Raise Methods - Clip Properties
        
        /// <summary>
        /// Raises a solo clip changed event.
        /// </summary>
        public static void RaiseSoloClipChanged(StateMachineAsset rootMachine, LayerStateAsset layer, int layerIndex, AnimationStateAsset state, int oldClipIndex, int newClipIndex)
        {
            var context = PreviewContext.StateSelection(rootMachine, layer, layerIndex, null, state);
            var args = new PreviewPropertyChangedEventArgs(PropertyNames.SoloClip, context, oldClipIndex, newClipIndex);
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
    /// Represents a transition selection.
    /// </summary>
    public class TransitionSelection
    {
        public AnimationStateAsset FromState { get; set; }
        public AnimationStateAsset ToState { get; set; }
        public bool IsAnyState { get; set; }
    }
    
    /// <summary>
    /// Represents a navigation target.
    /// </summary>
    public class NavigationTarget
    {
        public LayerStateAsset Layer { get; set; }
        public int LayerIndex { get; set; }
    }
    
    /// <summary>
    /// Preview type enumeration.
    /// </summary>
    public enum PreviewType
    {
        SingleState,
        LayerComposition
    }
    
    #endregion
}