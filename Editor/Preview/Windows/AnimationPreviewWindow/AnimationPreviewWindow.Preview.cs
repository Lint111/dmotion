using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class AnimationPreviewWindow
    {
        #region Preview Mode
        
        private void SetPreviewMode(PreviewMode mode)
        {
            if (previewSession == null) return;
            if (previewSession.Mode == mode) return;
            
            previewSession.Mode = mode;
            UpdateModeDropdownText();
            
            // Persist the selection
            EditorPrefs.SetInt(PreviewModePrefKey, (int)mode);
            
            // Recreate preview with new mode
            UpdateSelectionUI();
            Repaint();
        }
        
        private void UpdateModeDropdownText()
        {
            if (modeDropdown == null) return;
            
            var mode = previewSession?.Mode ?? PreviewMode.Authoring;
            modeDropdown.text = mode switch
            {
                PreviewMode.Authoring => "Authoring",
                PreviewMode.EcsRuntime => "ECS Runtime",
                _ => "Preview"
            };
        }
        
        private PreviewMode LoadSavedPreviewMode()
        {
            var savedMode = EditorPrefs.GetInt(PreviewModePrefKey, (int)PreviewMode.Authoring);
            return (PreviewMode)savedMode;
        }
        
        #endregion
        
        #region Preview Type
        
        private void SetPreviewType(PreviewType type)
        {
            if (currentPreviewType == type) return;
            
            // Check if layer composition is valid
            if (type == PreviewType.LayerComposition)
            {
                // Allow layer composition if:
                // 1. Current state machine is multi-layer (opening a new multi-layer machine), OR
                // 2. We're already in a multi-layer context (EditorState tracks the root)
                bool isMultiLayerContext = currentStateMachine?.IsMultiLayer == true ||
                                           (CompositionState?.RootStateMachine != null && 
                                            CompositionState.RootStateMachine.IsMultiLayer);
                
                if (!isMultiLayerContext)
                {
                    LogWarning("Layer Composition preview requires a multi-layer state machine.");
                    return;
                }
            }
            
            currentPreviewType = type;
            
            // Persist to EditorState (which saves to EditorPrefs)
            EditorState.Instance.PreviewType = type == PreviewType.LayerComposition 
                ? EditorPreviewType.LayerComposition 
                : EditorPreviewType.SingleState;
            
            UpdatePreviewTypeDropdownText();
            
            // Note: CompositionState is managed by EditorState - it's automatically initialized
            // when a multi-layer state machine is set as RootStateMachine
            
            // Update the UI to reflect the new preview type
            UpdateSelectionUI();
            Repaint();
        }
        
        private void UpdatePreviewTypeDropdownText()
        {
            if (previewTypeDropdown == null) return;
            
            previewTypeDropdown.text = currentPreviewType switch
            {
                PreviewType.SingleState => "Single State",
                PreviewType.LayerComposition => "Layer Composition",
                _ => "Preview Type"
            };
            
            // Update visibility based on root context (not current view)
            // Show dropdown if either current machine is multi-layer OR we're in a multi-layer context
            bool isMultiLayerContext = currentStateMachine?.IsMultiLayer == true ||
                                       (CompositionState?.RootStateMachine != null && 
                                        CompositionState.RootStateMachine.IsMultiLayer);
            
            previewTypeDropdown.style.display = isMultiLayerContext 
                ? DisplayStyle.Flex 
                : DisplayStyle.None;
        }
        
        #endregion

        #region Preview Rendering

        private void OnPreviewGUI()
        {
            if (previewSession == null) return;
            
            var rect = previewContainer.contentRect;
            
            // Sync time from state timeline scrubber
            // Only send time when paused/scrubbing - when playing, ECS advances time automatically
            var stateTimeline = stateInspectorBuilder?.TimelineScrubber;
            if (stateTimeline != null && currentSelectionType == SelectionType.State)
            {
                // Only sync when not playing (paused or scrubbing) to avoid ECS pause-on-scrub behavior
                if (!stateTimeline.IsPlaying || stateTimeline.IsDragging)
                {
                    previewSession.SetNormalizedTime(stateTimeline.NormalizedTime);
                }
            }
            
            // Sync time and progress from transition timeline
            var transitionTimeline = transitionInspectorBuilder?.Timeline;
            if (transitionTimeline != null && previewSession.IsTransitionPreview)
            {
                // Only sync when not playing (paused or scrubbing) to avoid ECS pause-on-scrub behavior
                // Both SetNormalizedTime and SetTransitionProgress send scrub commands that pause playback
                if (!transitionTimeline.IsPlaying || transitionTimeline.IsDragging)
                {
                    previewSession.SetNormalizedTime(transitionTimeline.NormalizedTime);
                    
                    // Use unified calculator's blend weight (already has curve applied)
                    float blendWeight = transitionTimeline.BlendWeight;
                    
                    // Set transition progress for blend weights (from→to crossfade)
                    previewSession.SetTransitionProgress(blendWeight);
                }
                
                // Set per-state normalized times for proper clip sampling (PlayableGraph backend only)
                previewSession.SetTransitionStateNormalizedTimes(
                    transitionTimeline.FromStateNormalizedTime,
                    transitionTimeline.ToStateNormalizedTime);
            }
            
            // Draw the preview
            previewSession.Draw(rect);
            
            // Handle camera input (but not if any timeline is dragging)
            bool transitionTimelineDragging = transitionInspectorBuilder?.Timeline?.IsDragging ?? false;
            bool stateTimelineDragging = stateInspectorBuilder?.TimelineScrubber?.IsDragging ?? false;
            if (!transitionTimelineDragging && !stateTimelineDragging && previewSession.HandleInput(rect))
            {
                // Save camera state after user interaction
                SaveCameraState();
                Repaint();
            }
        }

        private void OnResetViewClicked()
        {
            previewSession?.ResetCameraView();
            SaveCameraState();
            Repaint();
        }

        /// <summary>
        /// Saves the current camera state for persistence across focus changes and domain reloads.
        /// </summary>
        private void SaveCameraState()
        {
            if (previewSession == null) return;

            var camState = previewSession.CameraState;
            if (camState.IsValid)
            {
                savedCameraState = camState;
            }
        }
        
        private void OnPreviewModelChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            var newModel = evt.newValue as GameObject;
            
            // Validate the model has required components
            if (newModel != null)
            {
                var animator = newModel.GetComponentInChildren<Animator>();
                var skinnedMesh = newModel.GetComponentInChildren<SkinnedMeshRenderer>();
                
                if (animator == null || skinnedMesh == null)
                {
                    LogWarning("Preview model must have an Animator and SkinnedMeshRenderer.");
                    previewModelField.SetValueWithoutNotify(evt.previousValue);
                    return;
                }
            }
            
            // Update the session
            if (previewSession != null)
            {
                previewSession.PreviewModel = newModel;
            }
            
            // Save to EditorPrefs for this state machine
            SavePreviewModelPreference(newModel);
            
            // Recreate the preview with the new model
            UpdateSelectionUI();
        }

        #endregion
    }
}
