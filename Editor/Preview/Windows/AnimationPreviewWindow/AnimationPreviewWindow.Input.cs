using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class AnimationPreviewWindow
    {
        #region Keyboard and Input Handling
        
        private void OnWindowKeyDown(KeyDownEvent evt)
        {
            // Forward keyboard events to the active timeline for consistent behavior
            // This ensures shortcuts work regardless of focus state
            
            // Handle layer composition mode separately
            if (currentPreviewType == PreviewType.LayerComposition)
            {
                HandleLayerCompositionKeyDown(evt);
                return;
            }
            
            TimelineBase activeTimeline = currentSelectionType switch
            {
                SelectionType.State => stateInspectorBuilder?.TimelineScrubber,
                SelectionType.Transition or SelectionType.AnyStateTransition => transitionInspectorBuilder?.Timeline,
                _ => null
            };
            
            if (activeTimeline == null) return;
            
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    activeTimeline.TogglePlayPause();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.LeftArrow:
                    activeTimeline.StepBackward();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    activeTimeline.StepForward();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Home:
                    activeTimeline.GoToStart();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.End:
                    activeTimeline.GoToEnd();
                    evt.StopPropagation();
                    break;
            }
        }
        
        private void HandleLayerCompositionKeyDown(KeyDownEvent evt)
        {
            if (layerCompositionBuilder == null || CompositionState == null) return;
            
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    // Toggle global playback
                    CompositionState.IsPlaying = !CompositionState.IsPlaying;
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.LeftArrow:
                    // Step backward (decrease master time)
                    CompositionState.MasterTime = Mathf.Max(0f, CompositionState.MasterTime - 0.01f);
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    // Step forward (increase master time)
                    CompositionState.MasterTime = Mathf.Min(1f, CompositionState.MasterTime + 0.01f);
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Home:
                    // Go to start
                    CompositionState.MasterTime = 0f;
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.End:
                    // Go to end
                    CompositionState.MasterTime = 1f;
                    evt.StopPropagation();
                    break;
            }
        }
        
        private void OnPreviewClicked(PointerDownEvent evt)
        {
            // Focus the appropriate timeline when clicking on the 3D preview
            // This enables keyboard shortcuts after interacting with the preview
            
            TimelineBase activeTimeline = currentSelectionType switch
            {
                SelectionType.State => stateInspectorBuilder?.TimelineScrubber,
                SelectionType.Transition or SelectionType.AnyStateTransition => transitionInspectorBuilder?.Timeline,
                _ => null
            };
            
            activeTimeline?.Focus();
        }
        
        #endregion
    }
}
