using System;
using DMotion;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Private - Layer Sections
        
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
            // This avoids issues with Foldout's internal toggle manipulation
            section.Foldout = new Foldout();
            section.Foldout.AddToClassList("layer-section");
            section.Foldout.value = true;

            // Keep the foldout's text empty since we're using custom header
            section.Foldout.text = "";

            // Create custom header as part of the Foldout's label area
            var header = CreateLayerHeader(section, layerState);

            // Instead of manipulating internal toggle, put header in the Foldout's toggle area properly
            var toggle = section.Foldout.Q<Toggle>();
            if (toggle != null)
            {
                // Add our header content to the toggle's visual tree directly
                // This keeps the header visible when foldout collapses
                var checkmark = toggle.Q<VisualElement>(className: "unity-foldout__checkmark");
                if (checkmark != null)
                {
                    // Insert header after checkmark, replacing the default label
                    var toggleContainer = checkmark.parent;
                    var defaultLabel = toggle.Q<Label>(className: "unity-foldout__text");
                    if (defaultLabel != null)
                        defaultLabel.style.display = DisplayStyle.None;

                    toggleContainer.Add(header);
                    header.style.flexGrow = 1;
                }
                else
                {
                    // Fallback: add header directly to toggle
                    toggle.Add(header);
                    header.style.flexGrow = 1;
                }
            }

            // Content area - this goes into the Foldout's collapsible section
            section.Content = new VisualElement();
            section.Content.AddToClassList("layer-content");
            section.Content.style.paddingLeft = 15;

            BuildLayerContent(section, layerState);

            section.Foldout.Add(section.Content);

            return section;
        }
        
        private VisualElement CreateLayerHeader(LayerSectionData section, LayerStateAsset layerState)
        {
            var header = new VisualElement();
            header.AddToClassList("layer-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = HeaderPaddingVertical;
            header.style.paddingBottom = HeaderPaddingVertical;

            // No manual arrow needed - Foldout's native checkmark handles collapse indicator

            // Enable toggle
            section.EnableToggle = new Toggle();
            section.EnableToggle.value = layerState.IsEnabled;
            section.EnableToggle.style.marginRight = 5;
            section.EnableToggle.style.marginLeft = 5;
            section.EnableToggle.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                if (layer != null) layer.IsEnabled = evt.newValue;
            });
            // Stop click propagation to prevent foldout toggle when clicking enable checkbox
            section.EnableToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            header.Add(section.EnableToggle);

            // Layer name container
            var nameContainer = new VisualElement();
            nameContainer.style.flexDirection = FlexDirection.Column;
            nameContainer.style.flexGrow = 1;

            // Base layer: shift name right since we don't have weight slider
            if (layerState.IsBaseLayer)
            {
                nameContainer.style.marginLeft = BaseLayerNameMarginLeft;
            }

            var nameLabel = new Label($"Layer {layerState.LayerIndex}: {layerState.name}");
            nameLabel.AddToClassList("layer-name");
            nameContainer.Add(nameLabel);

            // Blend mode control
            // Base layer: Read-only label (always Override)
            // Other layers: Editable dropdown
            if (layerState.IsBaseLayer)
            {
                var blendModeLabel = new Label("Override");
                blendModeLabel.AddToClassList("layer-blend-mode");
                blendModeLabel.style.fontSize = 10;
                nameContainer.Add(blendModeLabel);
            }
            else
            {
                section.BlendModeField = new EnumField(layerState.BlendMode);
                section.BlendModeField.AddToClassList("layer-blend-mode-field");
                section.BlendModeField.style.fontSize = 10;
                section.BlendModeField.style.maxWidth = 80;
                section.BlendModeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is LayerBlendMode blendMode)
                    {
                        var layer = compositionState?.GetLayer(section.LayerIndex);
                        if (layer != null)
                        {
                            layer.BlendMode = blendMode;
                            // Refresh to update weight slider visibility
                            RefreshLayerSection(section);
                        }
                    }
                    evt.StopPropagation();
                });
                // Stop click propagation to prevent foldout toggle
                section.BlendModeField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                section.BlendModeField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                nameContainer.Add(section.BlendModeField);
            }

            header.Add(nameContainer);

            // Weight slider - only for non-Layer 0 and Additive blend mode
            // Base layer weight locked to 1.0
            // Override blend mode always has effective weight 1.0
            bool shouldShowWeight = ShouldShowWeightSlider(layerState, layerState.BlendMode);

            section.WeightContainer = new VisualElement();
            section.WeightContainer.style.flexDirection = FlexDirection.Row;
            section.WeightContainer.style.alignItems = Align.Center;
            section.WeightContainer.style.minWidth = WeightContainerMinWidth;
            section.WeightContainer.style.display = shouldShowWeight ? DisplayStyle.Flex : DisplayStyle.None;

            var weightLabel = new Label("Weight:");
            weightLabel.style.minWidth = 45;
            section.WeightContainer.Add(weightLabel);

            section.WeightSlider = new Slider(MinWeight, MaxWeight);
            section.WeightSlider.style.flexGrow = 1;
            section.WeightSlider.value = layerState.Weight;

            section.WeightSlider.RegisterValueChangedCallback(evt =>
            {
                var layer = compositionState?.GetLayer(section.LayerIndex);
                // Base layer weight is locked (safety check)
                if (layer != null && layer.CanModifyWeight)
                    layer.Weight = evt.newValue;
                if (section.WeightLabel != null)
                    section.WeightLabel.text = evt.newValue.ToString("F2");
            });
            // Stop click propagation to prevent foldout toggle when interacting with slider
            section.WeightSlider.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            section.WeightSlider.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            section.WeightContainer.Add(section.WeightSlider);

            section.WeightLabel = new Label(layerState.Weight.ToString("F2"));
            section.WeightLabel.style.minWidth = BlendFieldMinWidth;
            section.WeightContainer.Add(section.WeightLabel);

            header.Add(section.WeightContainer);

            // Navigate button (↗) - jump to the selected state/transition in the graph
            section.NavigateButton = IconButton.CreatePingButton(
                    "Navigate to selection in graph",
                    () => OnNavigateToLayer?.Invoke(section.LayerIndex, section.LayerAsset))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.NavigateButton.style.marginLeft = 5;
            header.Add(section.NavigateButton);
            
            // Clear assignment button (X) - explicit action to unassign layer
            section.ClearButton = IconButton.CreateClearButton(
                    "Clear layer assignment",
                    () => compositionState?.ClearLayerSelection(section.LayerIndex))
                .StopClickPropagation()
                .SetVisible(layerState.IsAssigned);
            section.ClearButton.style.marginLeft = 2;
            header.Add(section.ClearButton);

            return header;
        }

        /// <summary>
        /// Determines whether the weight slider should be shown for a layer.
        /// Base layer: Never show (weight locked to 1.0).
        /// Override blend mode: Never show (weight is effectively 1.0).
        /// Additive blend mode: Show (weight is variable).
        /// </summary>
        private static bool ShouldShowWeightSlider(LayerStateAsset layer, LayerBlendMode blendMode)
        {
            // Base layer weight cannot be modified
            if (!layer.CanModifyWeight) return false;

            // Override mode: weight is effectively 1.0
            if (blendMode == LayerBlendMode.Override) return false;

            // Additive mode: weight is variable
            return true;
        }
        
        private void BuildLayerContent(LayerSectionData section, LayerStateAsset layerState)
        {
            // Current selection
            var selectionRow = new VisualElement();
            selectionRow.style.flexDirection = FlexDirection.Row;
            selectionRow.style.alignItems = Align.Center;
            selectionRow.style.marginBottom = SelectionRowMarginBottom;

            section.SelectionLabel = new Label(GetSelectionText(layerState));
            section.SelectionLabel.AddToClassList("selection-label");
            section.SelectionLabel.style.flexGrow = 1;
            section.SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            selectionRow.Add(section.SelectionLabel);

            // Note: Navigate button moved to header row (next to clear button)

            section.Content.Add(selectionRow);
            
            // State controls (shown when a state is selected)
            section.StateControls = new VisualElement();
            section.StateControls.AddToClassList("state-controls");
            BuildLayerStateControls(section, layerState);
            section.Content.Add(section.StateControls);
            
            // Transition controls (shown when a transition is selected)
            section.TransitionControls = new VisualElement();
            section.TransitionControls.AddToClassList("transition-controls");
            BuildLayerTransitionControls(section, layerState);
            section.Content.Add(section.TransitionControls);
            
            // Per-layer timeline
            var timelineSection = new VisualElement();
            timelineSection.style.marginTop = TimelineSectionMarginTop;
            
            section.Timeline = new TimelineScrubber();
            section.Timeline.IsLooping = true;
            // Hide per-layer play button - playback is controlled globally via Global Playback section
            section.Timeline.ShowPlayButton = false;
            
            // Configure timeline based on selected state
            ConfigureLayerTimeline(section, layerState);
            
            // Subscribe to timeline events
            // Store delegate references for proper unsubscription in Cleanup()
            int layerIdx = section.LayerIndex;
            section.TimelineTimeChangedHandler = time => OnLayerTimeChanged(layerIdx, time);
            section.TimelinePlayStateChangedHandler = playing => OnLayerPlayStateChanged(layerIdx, playing);
            section.Timeline.OnTimeChanged += section.TimelineTimeChangedHandler;
            section.Timeline.OnPlayStateChanged += section.TimelinePlayStateChangedHandler;
            
            timelineSection.Add(section.Timeline);
            section.Content.Add(timelineSection);
            
            // Update visibility based on current mode
            RefreshLayerSection(section);
        }
        
        private void BuildLayerStateControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Blend space visual element container - populated dynamically based on selected state
            section.BlendSpaceContainer = new VisualElement();
            section.BlendSpaceContainer.name = "blend-space-container";
            section.StateControls.Add(section.BlendSpaceContainer);

            // Blend slider row with float field - range is updated dynamically
            var blendRow = new VisualElement();
            blendRow.name = "blend-row";
            blendRow.AddToClassList("property-row");

            var blendLabel = new Label("Blend");
            blendLabel.AddToClassList("property-label");
            blendLabel.style.minWidth = 60;
            blendRow.Add(blendLabel);

            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;

            section.BlendSlider = new Slider(0f, 1f);
            section.BlendSlider.AddToClassList("property-slider");
            section.BlendSlider.style.flexGrow = 1;
            section.BlendSlider.RegisterValueChangedCallback(evt =>
            {
                SetLayerBlendValue(section, evt.newValue);
                section.BlendField?.SetValueWithoutNotify(evt.newValue);
            });

            section.BlendField = new FloatField();
            section.BlendField.AddToClassList("property-float-field");
            section.BlendField.style.minWidth = FloatFieldWidth;
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

        /// <summary>
        /// Creates or updates the blend space visual element for a layer based on its selected state.
        /// </summary>
        private void BindBlendSpaceElement(LayerSectionData section, AnimationStateAsset selectedState)
        {
            // Skip if already bound to this state AND element still exists in hierarchy
            if (section.BoundBlendState == selectedState &&
                section.BlendSpaceElement != null &&
                section.BlendSpaceElement.parent != null)
                return;

            // Cleanup previous element (also clears container defensively)
            UnbindBlendSpaceElement(section);

            section.BoundBlendState = selectedState;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(selectedState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persistedValue = result.InitialPosition;

            // Update slider range
            if (section.BlendSlider != null)
            {
                section.BlendSlider.lowValue = result.Range.MinX;
                section.BlendSlider.highValue = result.Range.MaxX;
            }
            section.BlendSlider?.SetValueWithoutNotify(persistedValue.x);
            section.BlendField?.SetValueWithoutNotify(persistedValue.x);

            // Sync to observable
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer != null)
                layer.BlendPosition = new Unity.Mathematics.float2(persistedValue.x, persistedValue.y);

            // Handle preview position changes from the visual element
            section.CachedPreviewPositionHandler = result.Is2D
                ? pos =>
                {
                    SetLayerBlendValue2D(section, pos);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                }
                : pos =>
                {
                    SetLayerBlendValue(section, pos.x);
                    section.BlendSlider?.SetValueWithoutNotify(pos.x);
                    section.BlendField?.SetValueWithoutNotify(pos.x);
                };
            element.OnPreviewPositionChanged += section.CachedPreviewPositionHandler;

            // Defensive: ensure container is clear before adding
            section.BlendSpaceContainer.Clear();
            section.BlendSpaceElement = element;
            section.BlendSpaceContainer.Add(element);
        }

        /// <summary>
        /// Removes and cleans up the current blend space visual element for a layer.
        /// </summary>
        private void UnbindBlendSpaceElement(LayerSectionData section)
        {
            if (section.BlendSpaceElement != null && section.CachedPreviewPositionHandler != null)
            {
                section.BlendSpaceElement.OnPreviewPositionChanged -= section.CachedPreviewPositionHandler;
                section.CachedPreviewPositionHandler = null;
            }

            section.BlendSpaceContainer?.Clear();
            section.BlendSpaceElement = null;
            section.BoundBlendState = null;
        }

        /// <summary>
        /// Binds blend space elements for transition from/to states.
        /// </summary>
        private void BindTransitionBlendElements(LayerSectionData section, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            // Bind "from" state blend element
            BindTransitionFromBlendElement(section, fromState);

            // Bind "to" state blend element
            BindTransitionToBlendElement(section, toState);
        }

        private void BindTransitionFromBlendElement(LayerSectionData section, AnimationStateAsset fromState)
        {
            // Skip if already bound to this state
            if (section.BoundFromState == fromState && section.FromBlendSpaceElement?.parent != null)
                return;

            UnbindTransitionFromBlendElement(section);
            section.BoundFromState = fromState;

            var fromBlendRow = section.TransitionControls?.Q<VisualElement>("from-blend-row");
            bool isBlendState = BlendSpaceUIBuilder.IsBlendState(fromState);

            if (fromBlendRow != null)
                fromBlendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;
            if (section.FromBlendSpaceContainer != null)
                section.FromBlendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(fromState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persisted = result.InitialPosition;

            // Wire up position change handler - propagate to backend and update UI
            section.CachedFromPreviewPositionHandler = pos =>
            {
                // Update slider UI
                section.FromBlendSlider?.SetValueWithoutNotify(pos.x);
                if (section.FromBlendLabel != null)
                    section.FromBlendLabel.text = pos.x.ToString("F2");
                
                // Propagate to backend (persists to PreviewSettings and updates playable graph)
                SetTransitionFromBlendValue(section, pos.x);
            };
            element.OnPreviewPositionChanged += section.CachedFromPreviewPositionHandler;

            section.FromBlendSpaceContainer?.Clear();
            section.FromBlendSpaceElement = element;
            section.FromBlendSpaceContainer?.Add(element);

            // Update slider range
            if (section.FromBlendSlider != null)
            {
                section.FromBlendSlider.lowValue = result.Range.MinX;
                section.FromBlendSlider.highValue = result.Range.MaxX;
            }
            section.FromBlendSlider?.SetValueWithoutNotify(persisted.x);
            if (section.FromBlendLabel != null)
                section.FromBlendLabel.text = persisted.x.ToString("F2");
        }

        private void BindTransitionToBlendElement(LayerSectionData section, AnimationStateAsset toState)
        {
            // Skip if already bound to this state
            if (section.BoundToState == toState && section.ToBlendSpaceElement?.parent != null)
                return;

            UnbindTransitionToBlendElement(section);
            section.BoundToState = toState;

            var toBlendRow = section.TransitionControls?.Q<VisualElement>("to-blend-row");
            bool isBlendState = BlendSpaceUIBuilder.IsBlendState(toState);

            if (toBlendRow != null)
                toBlendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;
            if (section.ToBlendSpaceContainer != null)
                section.ToBlendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

            // Create blend space element using the builder
            var result = BlendSpaceUIBuilder.CreateForPreview(toState);
            if (!result.IsValid) return;

            var element = result.Element;
            var persisted = result.InitialPosition;

            // Wire up position change handler - propagate to backend and update UI
            section.CachedToPreviewPositionHandler = pos =>
            {
                // Update slider UI
                section.ToBlendSlider?.SetValueWithoutNotify(pos.x);
                if (section.ToBlendLabel != null)
                    section.ToBlendLabel.text = pos.x.ToString("F2");
                
                // Propagate to backend (persists to PreviewSettings and updates playable graph)
                SetTransitionToBlendValue(section, pos.x);
            };
            element.OnPreviewPositionChanged += section.CachedToPreviewPositionHandler;

            section.ToBlendSpaceContainer?.Clear();
            section.ToBlendSpaceElement = element;
            section.ToBlendSpaceContainer?.Add(element);

            // Update slider range
            if (section.ToBlendSlider != null)
            {
                section.ToBlendSlider.lowValue = result.Range.MinX;
                section.ToBlendSlider.highValue = result.Range.MaxX;
            }
            section.ToBlendSlider?.SetValueWithoutNotify(persisted.x);
            if (section.ToBlendLabel != null)
                section.ToBlendLabel.text = persisted.x.ToString("F2");
        }

        /// <summary>
        /// Unbinds all transition blend elements.
        /// </summary>
        private void UnbindTransitionBlendElements(LayerSectionData section)
        {
            UnbindTransitionFromBlendElement(section);
            UnbindTransitionToBlendElement(section);
        }

        private void UnbindTransitionFromBlendElement(LayerSectionData section)
        {
            if (section.FromBlendSpaceElement != null && section.CachedFromPreviewPositionHandler != null)
            {
                section.FromBlendSpaceElement.OnPreviewPositionChanged -= section.CachedFromPreviewPositionHandler;
                section.CachedFromPreviewPositionHandler = null;
            }

            section.FromBlendSpaceContainer?.Clear();
            section.FromBlendSpaceElement = null;
            section.BoundFromState = null;
        }

        private void UnbindTransitionToBlendElement(LayerSectionData section)
        {
            if (section.ToBlendSpaceElement != null && section.CachedToPreviewPositionHandler != null)
            {
                section.ToBlendSpaceElement.OnPreviewPositionChanged -= section.CachedToPreviewPositionHandler;
                section.CachedToPreviewPositionHandler = null;
            }

            section.ToBlendSpaceContainer?.Clear();
            section.ToBlendSpaceElement = null;
            section.BoundToState = null;
        }

        private void SetLayerBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new Unity.Mathematics.float2(value, layer.BlendPosition.y);

            // Update visual element
            if (section.BlendSpaceElement != null)
                section.BlendSpaceElement.PreviewPosition = new Vector2(value, section.BlendSpaceElement.PreviewPosition.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Persist via PreviewSettings
            var selectedState = layer.SelectedState;
            if (selectedState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(selectedState, value);
            else if (selectedState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(selectedState, new Vector2(value, layer.BlendPosition.y));
            
            // Update timeline duration (changes with blend position for blend states)
            ConfigureLayerTimeline(section, layer);
        }

        private void SetLayerBlendValue2D(LayerSectionData section, Vector2 value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            layer.BlendPosition = new Unity.Mathematics.float2(value.x, value.y);

            // Propagate to preview backend
            preview?.SetLayerBlendPosition(section.LayerIndex, layer.BlendPosition);

            // Visual element already updated by the caller
            // Persist via PreviewSettings
            var selectedState = layer.SelectedState;
            if (selectedState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(selectedState, value);
            
            // Update timeline duration (changes with blend position for blend states)
            ConfigureLayerTimeline(section, layer);
        }

        private void BuildLayerTransitionControls(LayerSectionData section, LayerStateAsset layerState)
        {
            // Unified controls row with mode dropdown and action button
            var controlsRow = new VisualElement();
            controlsRow.name = "transition-controls-row";
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.alignItems = Align.Center;
            controlsRow.style.marginBottom = 5;
            
            // Loop mode dropdown - compact, no label (button provides context)
            section.LoopModeField = new EnumField(layerState.TransitionLoopMode);
            section.LoopModeField.style.minWidth = 120;
            section.LoopModeField.style.maxWidth = 140;
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
            
            // Unified action button - shows current state AND available action
            // e.g., "▶ FROM | Trigger →" or "⟳ Blending..." or "▶ TO | ↺ Reset"
            var capturedSection = section;
            section.TriggerButton = new Button(() => OnTriggerButtonClicked(capturedSection));
            section.TriggerButton.text = GetUnifiedButtonText(layerState);
            section.TriggerButton.style.marginLeft = 8;
            section.TriggerButton.style.minWidth = 140;
            section.TriggerButton.style.flexGrow = 1;
            controlsRow.Add(section.TriggerButton);
            
            // PlayStateLabel no longer used - state shown in button
            section.PlayStateLabel = null;
            
            section.TransitionControls.Add(controlsRow);
            
            // "From" state section
            var fromLabel = new Label("From State");
            fromLabel.AddToClassList("transition-state-label");
            fromLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            fromLabel.style.marginTop = 5;
            section.TransitionControls.Add(fromLabel);

            // From blend space container
            section.FromBlendSpaceContainer = new VisualElement();
            section.FromBlendSpaceContainer.name = "from-blend-space-container";
            section.TransitionControls.Add(section.FromBlendSpaceContainer);

            // From blend slider row
            var fromBlendRow = CreateSliderRow(
                "Blend",
                0f, 1f,
                0f,
                value => SetTransitionFromBlendValue(section, value),
                out section.FromBlendSlider,
                out section.FromBlendLabel);
            fromBlendRow.name = "from-blend-row";
            section.TransitionControls.Add(fromBlendRow);

            // Transition progress slider (only visible in TransitionLoop mode or during transition)
            var progressRow = CreateSliderRow(
                "Progress",
                0f, 1f,
                layerState.TransitionProgress,
                value => OnTransitionProgressChanged(capturedSection, value),
                out section.TransitionProgressSlider,
                out _);
            progressRow.name = "progress-row";
            progressRow.style.marginTop = 10;
            section.TransitionControls.Add(progressRow);

            // "To" state section
            var toLabel = new Label("To State");
            toLabel.AddToClassList("transition-state-label");
            toLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toLabel.style.marginTop = 10;
            section.TransitionControls.Add(toLabel);

            // To blend space container
            section.ToBlendSpaceContainer = new VisualElement();
            section.ToBlendSpaceContainer.name = "to-blend-space-container";
            section.TransitionControls.Add(section.ToBlendSpaceContainer);

            // To blend slider row
            var toBlendRow = CreateSliderRow(
                "Blend",
                0f, 1f,
                0f,
                value => SetTransitionToBlendValue(section, value),
                out section.ToBlendSlider,
                out section.ToBlendLabel);
            toBlendRow.name = "to-blend-row";
            section.TransitionControls.Add(toBlendRow);
            
            // Initial state update
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
            
            // Update unified button - hide entirely in TransitionLoop mode (no action needed)
            if (section.TriggerButton != null)
            {
                section.TriggerButton.text = GetUnifiedButtonText(layer);
                section.TriggerButton.style.display = isTransitionLoop ? DisplayStyle.None : DisplayStyle.Flex;
            }
            
            // Progress slider: always show in TransitionLoop, show during transition in other modes
            var progressRow = section.TransitionControls?.Q<VisualElement>("progress-row");
            bool showProgress = isTransitionLoop || layer.TransitionPlayState == TransitionPlayState.Transitioning;
            if (progressRow != null)
                progressRow.style.display = showProgress ? DisplayStyle.Flex : DisplayStyle.None;
        }
        
        /// <summary>
        /// Returns unified button text showing current state AND available action.
        /// Format: "State | Action" or just "State" when no action available.
        /// </summary>
        private static string GetUnifiedButtonText(LayerStateAsset layer)
        {
            // TransitionLoop mode: just show current state (button disabled, no action)
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
            
            // FromLoop/ToLoop modes: show state + action
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

        private void SetTransitionFromBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            // Update visual element
            if (section.FromBlendSpaceElement != null)
                section.FromBlendSpaceElement.PreviewPosition = new Vector2(value, section.FromBlendSpaceElement.PreviewPosition.y);

            // Persist via PreviewSettings for the from state
            var fromState = layer.TransitionFrom;
            if (fromState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(fromState, value);
            else if (fromState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(fromState, new Vector2(value, 0));

            // Propagate both blend positions to preview backend for transition
            var fromBlendPos = PreviewSettings.GetBlendPosition(fromState);
            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }

        private void SetTransitionToBlendValue(LayerSectionData section, float value)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null) return;

            // Update visual element
            if (section.ToBlendSpaceElement != null)
                section.ToBlendSpaceElement.PreviewPosition = new Vector2(value, section.ToBlendSpaceElement.PreviewPosition.y);

            // Persist via PreviewSettings for the to state
            var toState = layer.TransitionTo;
            if (toState is LinearBlendStateAsset)
                PreviewSettings.instance.SetBlendValue1D(toState, value);
            else if (toState is Directional2DBlendStateAsset)
                PreviewSettings.instance.SetBlendValue2D(toState, new Vector2(value, 0));

            // Propagate both blend positions to preview backend for transition
            var fromBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(toState);
            preview?.SetLayerTransitionBlendPositions(
                section.LayerIndex,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y));
        }
        
        private void OnTransitionProgressChanged(LayerSectionData section, float progress)
        {
            var layer = compositionState?.GetLayer(section.LayerIndex);
            if (layer == null || !layer.IsTransitionMode) return;
            
            // Update layer state
            layer.TransitionProgress = progress;
            
            // Update timeline to match progress
            if (section.Timeline != null)
            {
                section.Timeline.NormalizedTime = progress;
            }
            
            // Calculate transition state for this progress
            var fromBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(layer.TransitionTo);
            
            // Use real transition data, exclude ghost bars for triggered modes
            bool includeGhosts = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
            var config = CreateTransitionConfig(
                layer.TransitionFrom,
                layer.TransitionTo,
                new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
                includeGhostBars: includeGhosts);
            
            var snapshot = TransitionCalculator.CalculateStateFromProgress(in config, progress);
            
            // Propagate blend weight to backend
            preview?.SetLayerTransitionProgress(section.LayerIndex, snapshot.BlendWeight);
            
            // Calculate looping normalized times
            float elapsedTime = progress * config.TransitionDuration;
            float fromDuration = config.FromStateDuration;
            float toDuration = config.ToStateDuration;
            float fromNormalized = fromDuration > 0.001f 
                ? (elapsedTime % fromDuration) / fromDuration 
                : 0f;
            float toNormalized = toDuration > 0.001f 
                ? (elapsedTime % toDuration) / toDuration 
                : 0f;
            
            // Propagate to backend
            preview?.SetLayerTransitionNormalizedTimes(section.LayerIndex, fromNormalized, toNormalized);
        }
        
        private void ConfigureLayerTimeline(LayerSectionData section, LayerStateAsset layerState)
        {
            if (layerState.IsTransitionMode)
            {
                // Get blend positions for config
                var fromBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionFrom);
                var toBlendPos = PreviewSettings.GetBlendPosition(layerState.TransitionTo);
                
                // Timeline configuration depends on loop mode and play state
                switch (layerState.TransitionLoopMode)
                {
                    case TransitionLoopMode.TransitionLoop:
                        // Show full transition timeline with ghost bars
                        var loopConfig = CreateTransitionConfig(
                            layerState.TransitionFrom,
                            layerState.TransitionTo,
                            new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                            new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
                            includeGhostBars: true);
                        section.Timeline.Duration = loopConfig.Timing.TotalDuration;
                        section.Timeline.NormalizedTime = layerState.NormalizedTime;
                        break;
                        
                    case TransitionLoopMode.FromLoop:
                    case TransitionLoopMode.ToLoop:
                        // Show different timeline based on play state
                        switch (layerState.TransitionPlayState)
                        {
                            case TransitionPlayState.LoopingFrom:
                                float fromDuration = AnimationStateUtils.GetEffectiveDuration(layerState.TransitionFrom, fromBlendPos);
                                section.Timeline.Duration = fromDuration > 0 ? fromDuration : 1f;
                                section.Timeline.NormalizedTime = layerState.NormalizedTime;
                                break;
                                
                            case TransitionPlayState.Transitioning:
                                // Show transition timeline without ghost bars (looping states provide context)
                                var transConfig = CreateTransitionConfig(
                                    layerState.TransitionFrom,
                                    layerState.TransitionTo,
                                    new Unity.Mathematics.float2(fromBlendPos.x, fromBlendPos.y),
                                    new Unity.Mathematics.float2(toBlendPos.x, toBlendPos.y),
                                    includeGhostBars: false);
                                section.Timeline.Duration = transConfig.Timing.TotalDuration;
                                section.Timeline.NormalizedTime = layerState.NormalizedTime;
                                break;
                                
                            case TransitionPlayState.LoopingTo:
                                float toDuration = AnimationStateUtils.GetEffectiveDuration(layerState.TransitionTo, toBlendPos);
                                section.Timeline.Duration = toDuration > 0 ? toDuration : 1f;
                                section.Timeline.NormalizedTime = layerState.NormalizedTime;
                                break;
                        }
                        break;
                }
                return;
            }
            
            var selectedState = layerState.SelectedState;
            if (selectedState == null)
            {
                section.Timeline.Duration = 1f;
                return;
            }
            
            // Get effective duration at current blend position
            var blendPos = layerState.BlendPosition;
            var duration = selectedState.GetEffectiveDuration(blendPos);
            if (duration <= 0) duration = 1f;
            
            section.Timeline.Duration = duration;
            section.Timeline.NormalizedTime = layerState.NormalizedTime;
        }
        
        private void RefreshLayerSection(LayerSectionData section)
        {
            if (compositionState == null || section.LayerIndex >= compositionState.LayerCount)
                return;
            
            // Prevent recursive refresh (additional safety guard)
            if (_isRefreshingLayer) return;
            _isRefreshingLayer = true;
            try
            {
                RefreshLayerSectionCore(section);
            }
            finally
            {
                _isRefreshingLayer = false;
            }
        }
        
        private void RefreshLayerSectionCore(LayerSectionData section)
        {
            var layerState = compositionState.Layers[section.LayerIndex];

            // Update header controls
            section.EnableToggle?.SetValueWithoutNotify(layerState.IsEnabled);

            // Update blend mode field
            section.BlendModeField?.SetValueWithoutNotify(layerState.BlendMode);

            // Update weight slider visibility based on blend mode
            bool shouldShowWeight = ShouldShowWeightSlider(layerState, layerState.BlendMode);
            if (section.WeightContainer != null)
                section.WeightContainer.style.display = shouldShowWeight ? DisplayStyle.Flex : DisplayStyle.None;

            // Update weight values (only if visible)
            if (shouldShowWeight)
            {
                section.WeightSlider?.SetValueWithoutNotify(layerState.Weight);
                if (section.WeightLabel != null)
                    section.WeightLabel.text = layerState.Weight.ToString("F2");
            }

            // Check if layer is unassigned
            bool isAssigned = layerState.IsAssigned;

            // Update selection label text
            if (section.SelectionLabel != null)
            {
                section.SelectionLabel.text = GetSelectionText(layerState);
                section.SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            }

            // Show/hide navigation and clear buttons based on assignment status
            section.NavigateButton?.SetVisible(isAssigned);
            section.ClearButton?.SetVisible(isAssigned);

            // Enable/disable controls based on assignment
            if (shouldShowWeight)
                section.WeightSlider?.SetEnabled(isAssigned);
            section.EnableToggle?.SetEnabled(isAssigned);

            if (!isAssigned)
            {
                // Hide all controls for unassigned layers
                if (section.StateControls != null)
                    section.StateControls.style.display = DisplayStyle.None;
                if (section.TransitionControls != null)
                    section.TransitionControls.style.display = DisplayStyle.None;
                if (section.Timeline != null)
                    section.Timeline.style.display = DisplayStyle.None;
                return;
            }

            // Show timeline for assigned layers
            if (section.Timeline != null)
                section.Timeline.style.display = DisplayStyle.Flex;
            
            // Show/hide state vs transition controls
            bool isTransition = layerState.IsTransitionMode;
            bool hasState = layerState.SelectedState != null;
            
            if (section.StateControls != null)
                section.StateControls.style.display = (hasState && !isTransition) ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (section.TransitionControls != null)
                section.TransitionControls.style.display = isTransition ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Update state controls
            if (hasState && !isTransition)
            {
                // Show blend controls only for blend state types
                var selectedState = layerState.SelectedState;
                bool isBlendState = BlendSpaceUIBuilder.IsBlendState(selectedState);

                var blendRow = section.StateControls?.Q<VisualElement>("blend-row");
                if (blendRow != null)
                    blendRow.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

                var blendSpaceContainer = section.BlendSpaceContainer;
                if (blendSpaceContainer != null)
                    blendSpaceContainer.style.display = isBlendState ? DisplayStyle.Flex : DisplayStyle.None;

                if (isBlendState)
                {
                    // Bind/update the blend space visual element for this state
                    BindBlendSpaceElement(section, selectedState);

                    // Read persisted value
                    var persisted = PreviewSettings.GetBlendPosition(selectedState);
                    section.BlendSlider?.SetValueWithoutNotify(persisted.x);
                    section.BlendField?.SetValueWithoutNotify(persisted.x);
                }
                else
                {
                    // Unbind blend space when not a blend state
                    UnbindBlendSpaceElement(section);
                }
            }
            else
            {
                // Hide blend space when not in state mode
                if (section.BlendSpaceContainer != null)
                    section.BlendSpaceContainer.style.display = DisplayStyle.None;
            }
            
            // Update transition controls
            if (isTransition)
            {
                section.TransitionProgressSlider?.SetValueWithoutNotify(layerState.TransitionProgress);
                section.LoopModeField?.SetValueWithoutNotify(layerState.TransitionLoopMode);

                // Bind blend space elements for transition from/to states
                BindTransitionBlendElements(section, layerState.TransitionFrom, layerState.TransitionTo);
                
                // Update loop mode controls state
                UpdateTransitionControlsState(section, layerState);
            }
            else
            {
                // Unbind transition blend elements when not in transition mode
                UnbindTransitionBlendElements(section);
            }

            // Update timeline
            ConfigureLayerTimeline(section, layerState);
        }
        
        private static string GetSelectionText(LayerStateAsset layerState)
        {
            if (layerState.IsTransitionMode)
            {
                // Transition: show arrow to indicate flow
                var from = layerState.TransitionFrom?.name ?? "?";
                var to = layerState.TransitionTo?.name ?? "?";
                return $"{from} → {to}";
            }

            if (layerState.SelectedState != null)
            {
                // Simple state: no arrow needed
                return layerState.SelectedState.name;
            }

            // Layer is unassigned - not contributing to animation
            return "Unassigned";
        }
        
        private void OnLayerTimeChanged(int layerIndex, float time)
        {
            // Skip if we're ticking - the tick method is driving the updates
            if (_isTickingLayers) return;
            
            var layer = compositionState?.GetLayer(layerIndex);
            if (layer == null) return;
            
            // Suppress events to prevent recursive cascade
            using (SuppressEvents())
            {
                if (layer.IsTransitionMode)
                {
                    // Timeline now represents full timeline position (NormalizedTime)
                    layer.NormalizedTime = time;
                    
                    var fromBlendVec = PreviewSettings.GetBlendPosition(layer.TransitionFrom);
                    var toBlendVec = PreviewSettings.GetBlendPosition(layer.TransitionTo);
                    
                    // Use ghost bars only for TransitionLoop mode
                    bool includeGhosts = layer.TransitionLoopMode == TransitionLoopMode.TransitionLoop;
                    var config = CreateTransitionConfig(
                        layer.TransitionFrom,
                        layer.TransitionTo,
                        new Unity.Mathematics.float2(fromBlendVec.x, fromBlendVec.y),
                        new Unity.Mathematics.float2(toBlendVec.x, toBlendVec.y),
                        includeGhostBars: includeGhosts);
                    
                    // Calculate state at this timeline position
                    var snapshot = TransitionCalculator.CalculateState(in config, time);
                    
                    // Update progress slider
                    layer.TransitionProgress = snapshot.RawProgress;
                    var section = layerSections.Find(s => s.LayerIndex == layerIndex);
                    section?.TransitionProgressSlider?.SetValueWithoutNotify(snapshot.RawProgress);
                    
                    // Propagate to backend
                    preview?.SetLayerTransitionProgress(layerIndex, snapshot.BlendWeight);
                    preview?.SetLayerTransitionNormalizedTimes(
                        layerIndex, 
                        snapshot.FromStateNormalizedTime, 
                        snapshot.ToStateNormalizedTime);
                }
                else
                {
                    // Single-state mode: timeline represents normalized time
                    layer.NormalizedTime = time;
                    preview?.SetLayerNormalizedTime(layerIndex, time);
                }
            }
            
            OnTimeChanged?.Invoke(time);
        }
        
        private void OnLayerPlayStateChanged(int layerIndex, bool playing)
        {
            var layer = compositionState?.GetLayer(layerIndex);
            if (layer != null)
                layer.IsPlaying = playing;
        }
        
        #endregion
    }
}
