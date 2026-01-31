using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    internal partial class AnimationPreviewWindow
    {
        #region Selection Event Handlers (Internal)

        private void SetContext(StateMachineAsset machine)
        {
            if (machine != null && machine != currentStateMachine)
            {
                currentStateMachine = machine;
                serializedStateMachine = machine; // Persist for domain reload

                // Update EditorState root state machine to trigger CompositionState initialization
                // This ensures layer selections are restored from PreviewSettings
                if (EditorState.Instance.RootStateMachine != machine)
                {
                    EditorState.Instance.RootStateMachine = machine;
                }

                // Check if we're in a multi-layer context by looking at the composition state's root
                // This handles the case where we're navigating inside layers of a multi-layer machine
                bool isInMultiLayerContext = CompositionState?.RootStateMachine != null &&
                                              CompositionState.RootStateMachine.IsMultiLayer;

                // Update preview type dropdown visibility based on root context
                UpdatePreviewTypeDropdownText();

                // Auto-switch to appropriate preview type
                if (machine.IsMultiLayer && currentPreviewType == PreviewType.SingleState)
                {
                    // Opening a new multi-layer machine - switch to layer composition
                    SetPreviewType(PreviewType.LayerComposition);
                }
                else if (!machine.IsMultiLayer && currentPreviewType == PreviewType.LayerComposition && !isInMultiLayerContext)
                {
                    // Only switch to single state if we're NOT navigating within a multi-layer context
                    // (i.e., this is a completely different single-layer state machine)
                    SetPreviewType(PreviewType.SingleState);
                }

                // Load saved preview model for this state machine
                LoadPreviewModelPreference();
            }
        }
        
        private void SavePreviewModelPreference(GameObject model)
        {
            if (currentStateMachine == null) return;
            
            var assetPath = AssetDatabase.GetAssetPath(currentStateMachine);
            if (string.IsNullOrEmpty(assetPath)) return;
            
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var prefKey = PreviewModelPrefKeyPrefix + guid;
            
            if (model != null)
            {
                var modelPath = AssetDatabase.GetAssetPath(model);
                EditorPrefs.SetString(prefKey, modelPath);
            }
            else
            {
                EditorPrefs.DeleteKey(prefKey);
            }
        }
        
        private void LoadPreviewModelPreference()
        {
            if (currentStateMachine == null)
            {
                UpdatePreviewModelField(null);
                return;
            }
            
            var assetPath = AssetDatabase.GetAssetPath(currentStateMachine);
            if (string.IsNullOrEmpty(assetPath))
            {
                UpdatePreviewModelField(null);
                return;
            }
            
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var prefKey = PreviewModelPrefKeyPrefix + guid;
            
            var modelPath = EditorPrefs.GetString(prefKey, null);
            if (!string.IsNullOrEmpty(modelPath))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                UpdatePreviewModelField(model);
            }
            else
            {
                UpdatePreviewModelField(null);
            }
        }
        
        private void UpdatePreviewModelField(GameObject model)
        {
            if (previewModelField != null)
            {
                previewModelField.SetValueWithoutNotify(model);
            }
            
            if (previewSession != null)
            {
                previewSession.PreviewModel = model;
            }
        }

        private void ClearSelection()
        {
            selectedState = null;
            selectedTransitionFrom = null;
            selectedTransitionTo = null;
            isAnyStateSelected = false;
            currentSelectionType = SelectionType.None;
        }

        #endregion

        #region UI Updates

        private void UpdateSelectionUI()
        {
            UpdateSelectionLabel();
            
            // Handle preview creation based on preview type 
            // IMPORTANT: Create backend preview BEFORE building inspector UI
            if (currentPreviewType == PreviewType.LayerComposition)
            {
                // Layer composition mode - create multi-layer preview in backend FIRST
                // (inspector needs the preview to be created)
                EnsureLayerCompositionPreview();
            }
            else
            {
                // Single state mode - create single state/transition preview
                // Note: PreviewSession reads initial blend positions from PreviewSettings internally
                if (currentSelectionType == SelectionType.State && selectedState != null)
                {
                    previewSession.CreatePreviewForState(selectedState);
                }
                else if (currentSelectionType == SelectionType.Transition || currentSelectionType == SelectionType.AnyStateTransition)
                {
                    CreateTransitionPreviewForSelection();
                }
                else
                {
                    var message = currentSelectionType switch
                    {
                        SelectionType.AnyState => 
                            "Select a state to\npreview animation",
                        _ => null
                    };
                    
                    if (message != null)
                        previewSession.SetMessage(message);
                    else
                        previewSession.Clear();
                }
            }
            
            // Build inspector UI AFTER preview is created
            UpdateInspectorContent();
            UpdatePreviewVisibility();
        }

        private void UpdateSelectionLabel()
        {
            if (selectionLabel == null) return;

            if (currentPreviewType == PreviewType.LayerComposition)
            {
                // Show root state machine name when in layer composition mode
                var rootName = CompositionState?.RootStateMachine?.name ?? currentStateMachine?.name;
                selectionLabel.text = rootName ?? "Layer Composition";
            }
            else
            {
                selectionLabel.text = currentSelectionType switch
                {
                    SelectionType.State => selectedState?.name ?? "Unknown State",
                    SelectionType.Transition => $"{selectedTransitionFrom?.name ?? "?"} -> {selectedTransitionTo?.name ?? "?"}",
                    SelectionType.AnyState => "Any State",
                    SelectionType.AnyStateTransition => $"Any State -> {selectedTransitionTo?.name ?? "?"}",
                    _ => "No Selection"
                };
            }
        }

        private void UpdateInspectorContent()
        {
            if (inspectorContent == null) return;
            
            // Cleanup previous builders
            stateInspectorBuilder?.Cleanup();
            transitionInspectorBuilder?.Cleanup();
            layerCompositionBuilder?.Cleanup();
            
            inspectorContent.Clear();

            if (currentPreviewType == PreviewType.LayerComposition)
            {
                BuildLayerCompositionInspector();
            }
            else
            {
                switch (currentSelectionType)
                {
                    case SelectionType.State:
                        BuildStateInspector();
                        break;
                    case SelectionType.Transition:
                    case SelectionType.AnyStateTransition:
                        BuildTransitionInspector();
                        break;
                    case SelectionType.AnyState:
                        BuildAnyStateInspector();
                        break;
                    default:
                        BuildNoSelectionInspector();
                        break;
                }
            }
        }

        private void BuildNoSelectionInspector()
        {
            var message = new Label("Select a state or transition in the State Machine Editor to preview.");
            message.AddToClassList("no-selection-message");
            inspectorContent.Add(message);
        }

        private void BuildStateInspector()
        {
            if (selectedState == null || stateInspectorBuilder == null) return;
            
            var content = stateInspectorBuilder.Build(currentStateMachine, selectedState);
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void BuildTransitionInspector()
        {
            if (transitionInspectorBuilder == null) return;
            
            var content = transitionInspectorBuilder.Build(
                currentStateMachine,
                selectedTransitionFrom,
                selectedTransitionTo,
                isAnyStateSelected);
            
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void BuildAnyStateInspector()
        {
            if (transitionInspectorBuilder == null) return;
            
            var content = transitionInspectorBuilder.BuildAnyState();
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }

        private void BuildLayerCompositionInspector()
        {
            if (layerCompositionBuilder == null) return;
            
            // Cleanup previous build
            layerCompositionBuilder.Cleanup();
            
            // Get the layer composition preview from the backend
            var backend = previewSession?.Backend as PlayableGraphBackend;
            var layerPreview = backend?.LayerComposition;
            
            // Sync backend state from CompositionState (restores selections after domain reload)
            SyncLayerPreviewFromCompositionState(layerPreview);
            
            // Build the inspector UI using the builder pattern
            var content = layerCompositionBuilder.Build(
                CompositionState?.RootStateMachine ?? currentStateMachine,
                CompositionState,
                layerPreview,
                _logger);  // Pass parent logger for hierarchical logging
            
            if (content != null)
            {
                inspectorContent.Add(content);
            }
        }
        
        /// <summary>
        /// Syncs the layer preview backend state from ObservableCompositionState.
        /// Called after domain reload or play mode exit to restore layer selections.
        /// </summary>
        private void SyncLayerPreviewFromCompositionState(ILayerCompositionPreview layerPreview)
        {
            if (layerPreview == null || CompositionState == null) return;
            
            int layerCount = Math.Min(layerPreview.LayerCount, CompositionState.LayerCount);
            LogDebug($"SyncLayerPreviewFromCompositionState: Syncing {layerCount} layers");
            
            for (int i = 0; i < layerCount; i++)
            {
                var layerState = CompositionState.GetLayer(i);
                if (layerState == null) continue;
                
                // Sync enabled state and weight
                layerPreview.SetLayerEnabled(i, layerState.IsEnabled);
                layerPreview.SetLayerWeight(i, layerState.Weight);
                
                if (layerState.IsTransitionMode)
                {
                    // Restore transition mode
                    LogDebug($"  Layer {i}: Restoring transition {layerState.TransitionFrom?.name} -> {layerState.TransitionTo?.name}");
                    layerPreview.SetLayerTransition(i, layerState.TransitionFrom, layerState.TransitionTo);
                    layerPreview.SetLayerTransitionProgress(i, layerState.TransitionProgress);
                    
                    // Restore blend positions for both states
                    var fromBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
                    var toBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
                    layerPreview.SetLayerTransitionBlendPositions(i, 
                        new float2(fromBlendPos.x, fromBlendPos.y),
                        new float2(toBlendPos.x, toBlendPos.y));
                }
                else if (layerState.SelectedState != null)
                {
                    // Restore single state mode
                    LogDebug($"  Layer {i}: Restoring state {layerState.SelectedState.name}");
                    layerPreview.SetLayerState(i, layerState.SelectedState);
                    layerPreview.SetLayerNormalizedTime(i, layerState.NormalizedTime);
                    
                    // Restore blend position
                    var blendPos = PreviewSettings.GetBlendPosition(layerState.SelectedState);
                    layerPreview.SetLayerBlendPosition(i, new float2(blendPos.x, blendPos.y));
                }
            }
        }

        private void CreateTransitionPreviewForSelection()
        {
            if (selectedTransitionTo == null)
            {
                previewSession.SetMessage("No target state\nfor transition");
                return;
            }
            
            // Find the transition to get its duration
            float transitionDuration = 0.25f; // Default
            
            if (isAnyStateSelected && currentStateMachine != null)
            {
                // Find in AnyStateTransitions
                foreach (var t in currentStateMachine.AnyStateTransitions)
                {
                    if (t.ToState == selectedTransitionTo)
                    {
                        transitionDuration = t.TransitionDuration;
                        break;
                    }
                }
            }
            else if (selectedTransitionFrom != null)
            {
                // Find in OutTransitions
                foreach (var t in selectedTransitionFrom.OutTransitions)
                {
                    if (t.ToState == selectedTransitionTo)
                    {
                        transitionDuration = t.TransitionDuration;
                        break;
                    }
                }
            }
            
            // For Any State transitions, fromState is null
            var fromState = isAnyStateSelected ? null : selectedTransitionFrom;
            previewSession.CreateTransitionPreview(fromState, selectedTransitionTo, transitionDuration);
        }
        
        private void UpdatePreviewVisibility()
        {
            if (previewPlaceholder == null || previewContainer == null) return;

            bool hasPreview = previewSession?.HasContent ?? false;

            previewPlaceholder.EnableInClassList("preview-placeholder--hidden", hasPreview);
            previewContainer.EnableInClassList("preview-imgui--hidden", !hasPreview);
        }

        #endregion
    }
}
