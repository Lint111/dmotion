using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Layer Section Building
        
        private void BuildLayerSections(VisualElement container)
        {
            layerSections.Clear();
            
            if (compositionState?.Layers == null) return;
            
            for (int i = 0; i < compositionState.LayerCount; i++)
            {
                var layerState = compositionState.Layers[i];
                var section = CreateLayerSection(layerState, i);
                container.Add(section.Foldout);
                layerSections.Add(section);
            }
        }
        
        private LayerSectionData CreateLayerSection(LayerStateAsset layerState, int layerIndex)
        {
            var section = new LayerSectionData
            {
                LayerIndex = layerIndex,
                LayerAsset = layerState
            };

            // Use a wrapper element instead of Foldout to have full control over collapse behavior
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList("layer-section");
            section.Foldout.value = true;
            section.Foldout.text = "";

            // Create custom header as part of the Foldout's label area
            var header = CreateLayerHeader(section, layerState);

            // Put header in the Foldout's toggle area
            var toggle = section.Foldout.Q<Toggle>();
            if (toggle != null)
            {
                var checkmark = toggle.Q<VisualElement>(className: "unity-foldout__checkmark");
                if (checkmark != null)
                {
                    var toggleContainer = checkmark.parent;
                    var defaultLabel = toggle.Q<Label>(className: "unity-foldout__text");
                    if (defaultLabel != null)
                        defaultLabel.AddToClassList("hidden");

                    toggleContainer.Add(header);
                    header.AddToClassList("flex-grow");
                }
                else
                {
                    toggle.Add(header);
                    header.AddToClassList("flex-grow");
                }
            }

            // Content area
            section.Content = new VisualElement();
            section.Content.AddToClassList("layer-content");

            BuildLayerContent(section, layerState);

            section.Foldout.Add(section.Content);

            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSectionData section, LayerStateAsset layerState)
        {
            var header = new VisualElement();
            header.AddToClassList("layer-header");
            // Inline styles as fallback - Unity's Foldout has complex internal styling
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.AddToClassList("layer-enable-toggle");
            section.EnableToggle.value = layerState.IsEnabled;
            section.EnableToggle.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null) layer.IsEnabled = evt.newValue;
            });
            section.EnableToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            header.Add(section.EnableToggle);

            // Layer name container
            var nameContainer = new VisualElement();
            nameContainer.AddToClassList("layer-name-container");

            if (layerState.IsBaseLayer)
            {
                nameContainer.AddToClassList("base-layer-name");
            }

            var nameLabel = new Label($"Layer {layerState.LayerIndex}: {layerState.name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);

            // Blend mode control
            if (layerState.IsBaseLayer)
            {
                var blendModeLabel = new Label("Override");
                blendModeLabel.AddToClassList("layer-blend-mode");
                nameContainer.Add(blendModeLabel);
            }
            else
            {
                section.BlendModeField = new EnumField(layerState.BlendMode);
                section.BlendModeField.AddToClassList("layer-blend-mode-field");
                section.BlendModeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is LayerBlendMode blendMode)
                    {
                        var layer = compositionState?.GetLayer(section.LayerIndex);
                        if (layer != null)
                        {
                            layer.BlendMode = blendMode;
                            RefreshLayerSection(section);
                        }
                    }
                    evt.StopPropagation();
                });
                section.BlendModeField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                section.BlendModeField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                nameContainer.Add(section.BlendModeField);
            }

            header.Add(nameContainer);

            // Weight slider
            bool shouldShowWeight = ShouldShowWeightSlider(layerState, layerState.BlendMode);

            section.WeightContainer = new VisualElement();
            section.WeightContainer.AddToClassList("weight-container");
            section.WeightContainer.style.display = shouldShowWeight ? DisplayStyle.Flex : DisplayStyle.None;

            var weightLabel = new Label("Weight:");
            weightLabel.AddToClassList("weight-label");
            section.WeightContainer.Add(weightLabel);

            section.WeightSlider = new Slider(MinWeight, MaxWeight);
            section.WeightSlider.AddToClassList("weight-slider");
            section.WeightSlider.value = layerState.Weight;
            section.WeightSlider.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null && layer.CanModifyWeight)
                    layer.Weight = evt.newValue;
                if (section.WeightLabel != null)
                    section.WeightLabel.text = evt.newValue.ToString("F2");
            });
            section.WeightSlider.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            section.WeightSlider.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            section.WeightContainer.Add(section.WeightSlider);

            section.WeightLabel = new Label(layerState.Weight.ToString("F2"));
            section.WeightLabel.AddToClassList("weight-field");
            section.WeightContainer.Add(section.WeightLabel);

            header.Add(section.WeightContainer);

            // Navigate button
            section.NavigateButton = IconButton.CreatePingButton(
                    "Navigate to selection in graph",
                    () => OnNavigateToLayer?.Invoke(section.LayerIndex, section.LayerAsset))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.NavigateButton.AddToClassList("header-navigate-button");
            header.Add(section.NavigateButton);
            
            // Clear button
            section.ClearButton = IconButton.CreateClearButton(
                    "Clear layer assignment",
                    () => compositionState?.ClearLayerSelection(section.LayerIndex))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.ClearButton.AddToClassList("header-clear-button");
            header.Add(section.ClearButton);

            return header;
        }

        private static bool ShouldShowWeightSlider(LayerStateAsset layer, LayerBlendMode blendMode)
        {
            if (!layer.CanModifyWeight) return false;
            if (blendMode == LayerBlendMode.Override) return false;
            return true;
        }
        
        private void BuildLayerContent(LayerSectionData section, LayerStateAsset layerState)
        {
            // Selection row
            var selectionRow = new VisualElement();
            selectionRow.AddToClassList("selection-row");

            section.SelectionLabel = new Label(GetSelectionText(layerState));
            section.SelectionLabel.AddToClassList("selection-label");
            selectionRow.Add(section.SelectionLabel);

            section.Content.Add(selectionRow);
            
            // State controls
            section.StateControls = new VisualElement();
            section.StateControls.AddToClassList("state-controls");
            BuildLayerStateControls(section, layerState);
            section.Content.Add(section.StateControls);
            
            // Transition controls
            section.TransitionControls = new VisualElement();
            section.TransitionControls.AddToClassList("transition-controls");
            BuildLayerTransitionControls(section, layerState);
            section.Content.Add(section.TransitionControls);
            
            // Per-layer timeline
            var timelineSection = new VisualElement();
            timelineSection.AddToClassList("timeline-section");
            
            section.Timeline = new TimelineScrubber();
            section.Timeline.IsLooping = true;
            section.Timeline.ShowPlayButton = false;
            
            ConfigureLayerTimeline(section, layerState);
            
            // Subscribe to timeline events
            int layerIdx = section.LayerIndex;
            section.TimelineTimeChangedHandler = time => OnLayerTimeChanged(layerIdx, time);
            section.TimelinePlayStateChangedHandler = playing => OnLayerPlayStateChanged(layerIdx, playing);
            section.Timeline.OnTimeChanged += section.TimelineTimeChangedHandler;
            section.Timeline.OnPlayStateChanged += section.TimelinePlayStateChangedHandler;
            
            timelineSection.Add(section.Timeline);
            section.Content.Add(timelineSection);
            
            RefreshLayerSection(section);
        }
        
        private void BuildLayerStateControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Blend space container
            section.BlendSpaceContainer = new VisualElement();
            section.BlendSpaceContainer.name = "blend-space-container";
            section.StateControls.Add(section.BlendSpaceContainer);

            // Blend slider row
            var blendRow = new VisualElement();
            blendRow.name = "blend-row";
            blendRow.AddToClassList("property-row");

            var blendLabel = new Label("Blend");
            blendLabel.AddToClassList("property-label");
            blendRow.Add(blendLabel);

            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");

            section.BlendSlider = new Slider(0f, 1f);
            section.BlendSlider.AddToClassList("property-slider");
            section.BlendSlider.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendField?.SetValueWithoutNotify(evt.newValue);
            });

            section.BlendField = new FloatField();
            section.BlendField.AddToClassList("property-float-field");
            section.BlendField.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendSlider?.SetValueWithoutNotify(evt.newValue);
            });

            valueContainer.Add(section.BlendSlider);
            valueContainer.Add(section.BlendField);
            blendRow.Add(valueContainer);

            section.StateControls.Add(blendRow);
        }

        #endregion

        #region Transition Controls Building
        
        private void BuildLayerTransitionControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Controls row
            var controlsRow = new VisualElement();
            controlsRow.name = "transition-controls-row";
            controlsRow.AddToClassList("transition-controls-row");
            
            section.LoopModeField = new EnumField(layerState.TransitionLoopMode);
            section.LoopModeField.AddToClassList("loop-mode-field");
            section.LoopModeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is TransitionLoopMode mode)
                {
                    var layer = compositionState?.GetLayer(section.LayerIndex);
                    if (layer != null)
                    {
                        layer.TransitionLoopMode = mode;
                        layer.ResetTransition();
                        UpdateTransitionControlsState(section, layer);
                    }
                }
            });
            controlsRow.Add(section.LoopModeField);
            
            var capturedSection = section;
            section.TriggerButton = new Button(() => OnTriggerButtonClicked(capturedSection));
            section.TriggerButton.AddToClassList("trigger-button");
            section.TriggerButton.text = GetUnifiedButtonText(layerState);
            controlsRow.Add(section.TriggerButton);
            
            section.PlayStateLabel = null;
            section.TransitionControls.Add(controlsRow);
            
            // "From" state section
            var fromLabel = new Label("From State");
            fromLabel.AddToClassList("transition-from-label");
            section.TransitionControls.Add(fromLabel);

            section.FromBlendSpaceContainer = new VisualElement();
            section.FromBlendSpaceContainer.name = "from-blend-space-container";
            section.TransitionControls.Add(section.FromBlendSpaceContainer);

            var fromBlendRow = CreateSliderRow(
                "Blend", 0f, 1f, 0f,
                value => SetTransitionFromBlendValue(section, value),
                out section.FromBlendSlider,
                out section.FromBlendLabel);
            fromBlendRow.name = "from-blend-row";
            section.TransitionControls.Add(fromBlendRow);

            // Progress slider
            var progressRow = CreateSliderRow(
                "Progress", 0f, 1f,
                layerState.TransitionProgress,
                value => OnTransitionProgressChanged(capturedSection, value),
                out section.TransitionProgressSlider,
                out _);
            progressRow.name = "progress-row";
            progressRow.AddToClassList("progress-row");
            section.TransitionControls.Add(progressRow);

            // "To" state section
            var toLabel = new Label("To State");
            toLabel.AddToClassList("transition-to-label");
            section.TransitionControls.Add(toLabel);

            section.ToBlendSpaceContainer = new VisualElement();
            section.ToBlendSpaceContainer.name = "to-blend-space-container";
            section.TransitionControls.Add(section.ToBlendSpaceContainer);

            var toBlendRow = CreateSliderRow(
                "Blend", 0f, 1f, 0f,
                value => SetTransitionToBlendValue(section, value),
                out section.ToBlendSlider,
                out section.ToBlendLabel);
            toBlendRow.name = "to-blend-row";
            section.TransitionControls.Add(toBlendRow);
            
            UpdateTransitionControlsState(section, layerState);
        }
        
        private void OnTriggerButtonClicked(LayerSectionData section)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null || !layer.IsTransitionMode) return;
            
            layer.TriggerTransition();
            UpdateTransitionControlsState(section, layer);
        }
        
        private void UpdateTransitionControlsState(LayerSectionData section, LayerStateAsset layer)
        {
            if (layer == null) return;
            
            bool isTransitionLoop = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
            
            if (section.TriggerButton != null)
            {
                section.TriggerButton.text = GetUnifiedButtonText(layer);
                section.TriggerButton.style.display = isTransitionLoop ? DisplayStyle.None : DisplayStyle.Flex;
            }
            
            var progressRow = section.TransitionControls?.Q<VisualElement>("progress-row");
            bool showProgress = isTransitionLoop || layer.TransitionPlayState == TransitionPlayState.Transitioning;
            if (progressRow != null)
                progressRow.style.display = showProgress ? DisplayStyle.Flex : DisplayStyle.None;
        }
        
        private static string GetUnifiedButtonText(LayerStateAsset layer)
        {
            if (layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop)
            {
                return layer.TransitionPlayState switch
                {
                    TransitionPlayState.LoopingFrom => "▶ Looping FROM",
                    TransitionPlayState.Transitioning => "⟳ Blending...",
                    TransitionPlayState.LoopingTo => "▶ Looping TO",
                    _ => "⟳ Looping"
                };
            }
            
            if (layer.TransitionPending)
            {
                return layer.TransitionPlayState switch
                {
                    TransitionPlayState.LoopingFrom => "▶ FROM  ⏳ pending...",
                    TransitionPlayState.LoopingTo => "▶ TO  ⏳ pending...",
                    _ => "⏳ pending..."
                };
            }
            
            return layer.TransitionPlayState switch
            {
                TransitionPlayState.LoopingFrom => "▶ FROM  │  Trigger →",
                TransitionPlayState.Transitioning => "⟳ Blending...",
                TransitionPlayState.LoopingTo => "▶ TO  │  ↺ Reset",
                _ => "▶ Trigger"
            };
        }
        
        private void OnTransitionProgressChanged(LayerSectionData section, float progress)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null || !layer.IsTransitionMode) return;
            
            layer.TransitionProgress = progress;
            
            if (section.Timeline != null)
            {
                section.Timeline.NormalizedTime = progress;
            }
            
            var fromBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
            
            bool includeGhosts = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
            var config = CreateTransitionConfig(
                layer.TransitionFrom,
                layer.TransitionTo,
                new float2(fromBlendPos.x, fromBlendPos.y),
                new float2(toBlendPos.x, toBlendPos.y),
                includeGhostBars: includeGhosts);
            
            var snapshot = TransitionCalculator.CalculateStateFromProgress(in config, progress);
            
            preview?.SetLayerTransitionProgress(section.LayerIndex, snapshot.BlendWeight);
            
            float elapsedTime = progress * config.TransitionDuration;
            float fromDuration = config.FromStateDuration;
            float toDuration = config.ToStateDuration;
            float fromNormalized = fromDuration > 0.001f 
                ? (elapsedTime % fromDuration) / fromDuration 
                : 0f;
            float toNormalized = toDuration > 0.001f 
                ? (elapsedTime % toDuration) / toDuration 
                : 0f;
            
            preview?.SetLayerTransitionNormalizedTimes(section.LayerIndex, fromNormalized, toNormalized);
        }

        #endregion
    }
}
