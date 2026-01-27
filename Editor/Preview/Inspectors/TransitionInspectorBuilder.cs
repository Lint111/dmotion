using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Self-contained builder for transition inspector UI in the preview window.
    /// Handles regular transitions and Any State transitions with its own UI factories and cleanup.
    /// </summary>
    internal class TransitionInspectorBuilder
    {
        #region Constants
        
        private const string UssPath = "Packages/com.gamedevpro.dmotion/Editor/Preview/Inspectors/TransitionInspector.uss";
        private const float MaxTransitionDuration = 2f;
        private const float MaxExitTime = 5f;
        
        // Use shared constant
        private const float FloatFieldWidth = PreviewEditorConstants.FloatFieldWidth;
        
        #endregion
        
        #region State
        
        private StateMachineAsset currentStateMachine;
        private AnimationStateAsset transitionFrom;
        private AnimationStateAsset transitionTo;
        private bool isAnyStateTransition;
        private float transitionDuration;
        private float transitionExitTime;
        
        // Timeline (UI Toolkit based)
        private TransitionTimeline timeline;
        
        // Cached playback settings
        private bool cachedIsLooping;
        
        // Serialized data for undo support
        private SerializedObject cachedSerializedObject;
        private SerializedProperty cachedTransitionProperty;
        private SerializedProperty cachedDurationProperty;
        private SerializedProperty cachedExitTimeProperty;
        private SerializedProperty cachedBlendCurveProperty;
        
        // Cached blend curve for timeline
        private AnimationCurve cachedBlendCurve;
        
        // Cached keyframes for curve evaluation (converted from AnimationCurve)
        // Uses same Hermite evaluation as runtime for identical behavior
        private CurveKeyframe[] cachedBlendCurveKeyframes;
        
        // Blend space visual elements - using base class references (polymorphism)
        private BlendSpaceVisualElement fromBlendSpaceElement;
        private BlendSpaceVisualElement toBlendSpaceElement;
        
        // Cached event handlers for cleanup
        private Action<Vector2> fromBlendPositionHandler;
        private Action<Vector2> toBlendPositionHandler;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when the timeline time changes.
        /// </summary>
        public event Action<float> OnTimeChanged;
        
        /// <summary>
        /// Fired when the builder needs a repaint.
        /// </summary>
        public event Action OnRepaintRequested;
        
        /// <summary>
        /// Fired when the timeline play state changes.
        /// </summary>
        public event Action<bool> OnPlayStateChanged;
        
        /// <summary>
        /// Fired when transition properties (duration, exit time) change.
        /// Used to trigger ECS timeline rebuild.
        /// </summary>
        public event Action OnTransitionPropertiesChanged;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The transition timeline control.
        /// </summary>
        public TransitionTimeline Timeline => timeline;
        
        /// <summary>
        /// Whether the timeline is currently playing.
        /// </summary>
        public bool IsPlaying => timeline?.IsPlaying ?? false;
        
        /// <summary>
        /// Cached blend curve keyframes for evaluation using CurveUtils.
        /// Null if curve is linear (fast-path - use linear t instead).
        /// Uses same Hermite evaluation as runtime for identical preview behavior.
        /// </summary>
        public CurveKeyframe[] BlendCurveKeyframes => cachedBlendCurveKeyframes;
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Builds the inspector UI for a transition.
        /// </summary>
        public VisualElement Build(
            StateMachineAsset stateMachine,
            AnimationStateAsset fromState,
            AnimationStateAsset toState,
            bool isAnyState)
        {
            currentStateMachine = stateMachine;
            transitionFrom = fromState;
            transitionTo = toState;
            isAnyStateTransition = isAnyState;
            
            var container = new VisualElement();
            container.AddToClassList("transition-inspector");
            
            // Load stylesheet
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null && !container.styleSheets.Contains(uss))
            {
                container.styleSheets.Add(uss);
            }
            
            // Header with clickable state names
            var header = CreateTransitionHeader(fromState, toState, isAnyState);
            container.Add(header);
            
            // Find the transition and its serialized property
            var (transition, transitionProperty, sourceSerializedObject) = FindSelectedTransitionWithProperty();
            
            // Cache serialized data for timeline editing with undo support
            cachedSerializedObject = sourceSerializedObject;
            cachedTransitionProperty = transitionProperty;
            if (transitionProperty != null)
            {
                cachedDurationProperty = transitionProperty.FindPropertyRelative("TransitionDuration");
                cachedExitTimeProperty = transitionProperty.FindPropertyRelative("EndTime");
                cachedBlendCurveProperty = transitionProperty.FindPropertyRelative("BlendCurve");
            }
            
            // Store transition settings for playback
            transitionDuration = transition?.TransitionDuration ?? 0.25f;
            transitionExitTime = transition?.EndTime ?? 0.75f;  // Exit time = To bar position
            cachedBlendCurve = transition?.BlendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
            // Convert to keyframes for runtime-identical evaluation
            cachedBlendCurveKeyframes = CurveUtils.ConvertAnimationCurveManaged(cachedBlendCurve);
            
            // Properties section
            var propertiesSection = CreateSection("Properties");
            BuildPropertiesSection(propertiesSection, transition, transitionProperty);
            if (sourceSerializedObject != null)
            {
                container.Bind(sourceSerializedObject);
            }
            container.Add(propertiesSection);
            
            // From state blend controls (if from state is a blend state)
            if (IsBlendState(fromState))
            {
                var fromBlendSection = CreateSection("From State Blend");
                BuildBlendControls(fromBlendSection, fromState, isFromState: true);
                container.Add(fromBlendSection);
            }
            
            // To state blend controls (if to state is a blend state)
            if (IsBlendState(toState))
            {
                var toBlendSection = CreateSection("To State Blend");
                BuildBlendControls(toBlendSection, toState, isFromState: false);
                container.Add(toBlendSection);
            }
            
            // Timeline section
            var timelineSection = CreateSection("Timeline");
            BuildTimeline(timelineSection);
            container.Add(timelineSection);
            
            return container;
        }
        
        /// <summary>
        /// Cleans up event subscriptions and resources.
        /// </summary>
        public void Cleanup()
        {
            // Cleanup timeline
            if (timeline != null)
            {
                timeline.OnTimeChanged -= OnTimelineTimeChanged;
                timeline.OnPlayStateChanged -= OnTimelinePlayStateChanged;
                timeline.OnTransitionProgressChanged -= OnTimelineProgressChanged;
                timeline.OnExitTimeChanged -= OnTimelineExitTimeChanged;
                timeline.OnTransitionDurationChanged -= OnTimelineDurationChanged;
                timeline.OnTransitionOffsetChanged -= OnTimelineOffsetChanged;
                timeline = null;
            }

            // Cleanup blend space elements
            CleanupBlendSpaceElement(ref fromBlendSpaceElement, fromBlendPositionHandler);
            CleanupBlendSpaceElement(ref toBlendSpaceElement, toBlendPositionHandler);
            
            fromBlendPositionHandler = null;
            toBlendPositionHandler = null;
            
            // Clear cached serialized data
            cachedSerializedObject = null;
            cachedTransitionProperty = null;
            cachedDurationProperty = null;
            cachedExitTimeProperty = null;
            cachedBlendCurveProperty = null;
            cachedBlendCurve = null;
            cachedBlendCurveKeyframes = null;
            
            transitionFrom = null;
            transitionTo = null;
            currentStateMachine = null;
        }
        
        /// <summary>
        /// Ticks the timeline for playback. Call from Update loop.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // PlaybackSpeed is set by AnimationPreviewWindow.Update() 
            // which has access to PreviewRenderer for weighted clip speeds
            timeline?.Tick(deltaTime);
        }
        
        /// <summary>
        /// Builds the inspector UI for Any State (not a transition, just the node).
        /// </summary>
        public VisualElement BuildAnyState()
        {
            var container = new VisualElement();
            container.AddToClassList("transition-inspector");
            
            // Load stylesheet
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null && !container.styleSheets.Contains(uss))
            {
                container.styleSheets.Add(uss);
            }
            
            var header = CreateSectionHeader("Any State", "Global transition source");
            container.Add(header);
            
            var infoSection = CreateSection("Info");
            var infoLabel = new Label("Any State transitions can target any state in the machine.\nSelect a transition to see its properties.");
            infoLabel.AddToClassList("info-message");
            infoSection.Add(infoLabel);
            container.Add(infoSection);
            
            return container;
        }
        
        #endregion
        
        #region Private - UI Factories
        
        private VisualElement CreateSectionHeader(string type, string name)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");

            header.Add(typeLabel);
            header.Add(nameLabel);

            return header;
        }
        
        /// <summary>
        /// Creates a transition header with clickable state name links.
        /// </summary>
        private VisualElement CreateTransitionHeader(AnimationStateAsset fromState, AnimationStateAsset toState, bool isAnyState)
        {
            var header = new VisualElement();
            header.AddToClassList("section-header");
            header.AddToClassList("section-header--wrap");

            // Type label
            var typeLabel = new Label("Transition");
            typeLabel.AddToClassList("header-type");
            header.Add(typeLabel);

            // From state - clickable if it's an actual state (not Any State)
            string fromName = isAnyState ? "Any State" : (fromState?.name ?? "?");
            var fromLabel = new Label(fromName);
            fromLabel.AddToClassList("state-link");
            
            if (!isAnyState && fromState != null)
            {
                fromLabel.AddToClassList("state-link--from");
                fromLabel.tooltip = $"Click to preview {fromState.name}";
                fromLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    EditorState.Instance.SelectedState = fromState;
                    evt.StopPropagation();
                });
                fromLabel.RegisterCallback<MouseEnterEvent>(_ => fromLabel.EnableInClassList("state-link--hover", true));
                fromLabel.RegisterCallback<MouseLeaveEvent>(_ => fromLabel.EnableInClassList("state-link--hover", false));
            }
            else
            {
                fromLabel.AddToClassList("state-link--dim");
            }
            header.Add(fromLabel);

            // Arrow
            var arrowLabel = new Label(" -> ");
            arrowLabel.AddToClassList("arrow-label");
            header.Add(arrowLabel);

            // To state - clickable if it exists
            string toName = toState?.name ?? "(exit)";
            var toLabel = new Label(toName);
            toLabel.AddToClassList("state-link");
            
            if (toState != null)
            {
                toLabel.AddToClassList("state-link--to");
                toLabel.tooltip = $"Click to preview {toState.name}";
                toLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    EditorState.Instance.SelectedState = toState;
                    evt.StopPropagation();
                });
                toLabel.RegisterCallback<MouseEnterEvent>(_ => toLabel.EnableInClassList("state-link--hover", true));
                toLabel.RegisterCallback<MouseLeaveEvent>(_ => toLabel.EnableInClassList("state-link--hover", false));
            }
            else
            {
                toLabel.AddToClassList("state-link--dim");
            }
            header.Add(toLabel);

            return header;
        }
        
        private Foldout CreateSection(string title)
        {
            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("section-foldout");
            return foldout;
        }
        
        private VisualElement CreatePropertyRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("property-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");

            var valueElement = new Label(value);
            valueElement.AddToClassList("property-value");

            row.Add(labelElement);
            row.Add(valueElement);

            return row;
        }
        
        /// <summary>
        /// Creates a slider with float field, optionally returning references for external sync.
        /// </summary>
        private VisualElement CreateSliderWithField(string label, float min, float max, float value,
            out Slider outSlider, out FloatField outField, Action<float> onValueChanged, string suffix = "")
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.value = value;
            
            var field = new FloatField();
            field.AddToClassList("property-float-field");
            field.value = value;
            
            slider.RegisterValueChangedCallback(evt =>
            {
                field.SetValueWithoutNotify(evt.newValue);
                onValueChanged?.Invoke(evt.newValue);
            });
            
            field.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.Clamp(evt.newValue, min, max * 2);
                slider.SetValueWithoutNotify(Mathf.Clamp(clamped, min, max));
                onValueChanged?.Invoke(clamped);
            });
            
            valueContainer.Add(slider);
            valueContainer.Add(field);
            
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.AddToClassList("suffix-label");
                valueContainer.Add(suffixLabel);
            }
            
            container.Add(valueContainer);
            
            outSlider = slider;
            outField = field;
            
            return container;
        }
        
        /// <summary>
        /// Creates a slider with float field (convenience overload without output refs).
        /// </summary>
        private VisualElement CreateSliderWithField(string label, float min, float max, float value,
            Action<float> onValueChanged, string suffix = "")
        {
            return CreateSliderWithField(label, min, max, value, out _, out _, onValueChanged, suffix);
        }
        
        private VisualElement CreateBoundSliderWithField(string label, float min, float max, 
            SerializedProperty property, string suffix, Action<float> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.AddToClassList("slider-value-container");
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.BindProperty(property);
            
            if (onChanged != null)
            {
                slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            }
            
            var field = new FloatField();
            field.AddToClassList("property-float-field");
            field.BindProperty(property);
            
            valueContainer.Add(slider);
            valueContainer.Add(field);
            
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.AddToClassList("suffix-label");
                valueContainer.Add(suffixLabel);
            }
            
            container.Add(valueContainer);
            return container;
        }
        
        private VisualElement CreateToggleRow(string label, bool value, Action<bool> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);
            
            var toggle = new Toggle();
            toggle.value = value;
            toggle.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            container.Add(toggle);
            
            return container;
        }
        
        private VisualElement CreateBoundToggleRow(string label, SerializedProperty property)
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            container.Add(labelElement);
            
            var toggle = new Toggle();
            toggle.BindProperty(property);
            container.Add(toggle);
            
            return container;
        }
        
        #endregion
        
        #region Private - Properties Section
        
        private void BuildPropertiesSection(
            VisualElement section,
            StateOutTransition transition,
            SerializedProperty transitionProperty)
        {
            // Duration (editable)
            if (transitionProperty != null)
            {
                var durationProp = transitionProperty.FindPropertyRelative("TransitionDuration");
                if (durationProp != null)
                {
                    var durationRow = CreateBoundSliderWithField("Duration", 0f, MaxTransitionDuration, durationProp, "s",
                        newValue =>
                        {
                            transitionDuration = newValue;
                            if (timeline != null)
                                timeline.TransitionDuration = Mathf.Max(0.01f, newValue);
                        });
                    section.Add(durationRow);
                }
            }
            else
            {
                section.Add(CreatePropertyRow("Duration", transition?.TransitionDuration.ToString("F2") + "s" ?? "?"));
            }
            
            // Exit Time with tooltip (editable)
            if (transitionProperty != null)
            {
                var endTimeProp = transitionProperty.FindPropertyRelative("EndTime");
                if (endTimeProp != null)
                {
                    var exitTimeRow = CreateBoundSliderWithField("Exit Time", 0f, MaxExitTime, endTimeProp, "s",
                        newValue =>
                        {
                            transitionExitTime = newValue;
                            if (timeline != null)
                                timeline.ExitTime = newValue;
                        });
                    exitTimeRow.tooltip = "Time in the From state (seconds) when the transition begins. Drag the To bar in the timeline to adjust visually.";
                    section.Add(exitTimeRow);
                }
            }
            
            // Loop toggle (preview-only)
            var loopRow = CreateToggleRow("Loop Preview", false,
                newValue =>
                {
                    cachedIsLooping = newValue;
                    if (timeline != null)
                        timeline.IsLooping = newValue;
                });
            loopRow.tooltip = "Loop the transition preview playback";
            section.Add(loopRow);
            
            // State Info (collapsed foldout for read-only info)
            // Use effective speed/duration at current blend position
            var fromBlendPos = PreviewSettings.GetBlendPosition(transitionFrom);
            var toBlendPos = PreviewSettings.GetBlendPosition(transitionTo);
            float fromSpeed = transitionFrom?.GetEffectiveSpeed(fromBlendPos) ?? 1f;
            float toSpeed = transitionTo?.GetEffectiveSpeed(toBlendPos) ?? 1f;
            float fromDuration = transitionFrom?.GetEffectiveDuration(fromBlendPos) ?? 0f;
            float toDuration = transitionTo?.GetEffectiveDuration(toBlendPos) ?? 0f;
            
            var stateInfoFoldout = new Foldout { text = "State Info", value = false };
            stateInfoFoldout.AddToClassList("state-info-foldout");
            stateInfoFoldout.Add(CreatePropertyRow("From Speed", $"{fromSpeed:F2}x"));
            stateInfoFoldout.Add(CreatePropertyRow("To Speed", $"{toSpeed:F2}x"));
            if (fromDuration > 0) stateInfoFoldout.Add(CreatePropertyRow("From Duration", $"{fromDuration:F2}s"));
            if (toDuration > 0) stateInfoFoldout.Add(CreatePropertyRow("To Duration", $"{toDuration:F2}s"));
            section.Add(stateInfoFoldout);
            
            // Conditions (collapsed foldout, read-only)
            if (transition != null && transition.Conditions != null && transition.Conditions.Count > 0)
            {
                var conditionsFoldout = new Foldout { text = $"Conditions ({transition.Conditions.Count})", value = false };
                conditionsFoldout.AddToClassList("conditions-foldout");
                
                foreach (var condition in transition.Conditions)
                {
                    var paramName = condition.Parameter?.name ?? "(none)";
                    var conditionDesc = GetConditionDescription(condition);
                    conditionsFoldout.Add(CreatePropertyRow(paramName, conditionDesc));
                }
                section.Add(conditionsFoldout);
            }
            
            // Blend Curve - uses custom editor window with correct presets
            if (transitionProperty != null)
            {
                var blendCurveProp = transitionProperty.FindPropertyRelative("BlendCurve");
                if (blendCurveProp != null)
                {
                    var curveSection = new VisualElement();
                    curveSection.AddToClassList("curve-section");
                    
                    // Header row with label and edit button
                    var headerRow = new VisualElement();
                    headerRow.AddToClassList("curve-header-row");
                    
                    var curveLabel = new Label("Blend Curve");
                    curveLabel.AddToClassList("curve-label");
                    headerRow.Add(curveLabel);
                    
                    curveSection.Add(headerRow);
                    
                    // Curve preview (UIToolkit element with Painter2D)
                    var curvePreview = new CurvePreviewElement();
                    curvePreview.Curve = cachedBlendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
                    curvePreview.SetOnCurveChanged(newCurve =>
                    {
                        cachedBlendCurve = newCurve;
                        // Convert to keyframes for runtime-identical evaluation
                        cachedBlendCurveKeyframes = CurveUtils.ConvertAnimationCurveManaged(newCurve);
                        blendCurveProp.animationCurveValue = newCurve;
                        blendCurveProp.serializedObject.ApplyModifiedProperties();
                        if (timeline != null)
                            timeline.BlendCurve = newCurve;
                    });
                    curveSection.Add(curvePreview);
                    
                    section.Add(curveSection);
                }
            }
        }
        
        #endregion
        
        #region Private - Timeline Section
        
        private void BuildTimeline(VisualElement section)
        {
            timeline = new TransitionTimeline();
            timeline.Configure(
                transitionFrom,
                transitionTo,
                transitionExitTime,
                transitionDuration,
                0f, // transitionOffset
                cachedBlendCurve);
            timeline.IsLooping = cachedIsLooping;
            timeline.PlaybackSpeed = 1f; // Real-time playback, state speeds affect animation sampling
            
            // Subscribe to playback events
            timeline.OnTimeChanged += OnTimelineTimeChanged;
            timeline.OnPlayStateChanged += OnTimelinePlayStateChanged;
            timeline.OnTransitionProgressChanged += OnTimelineProgressChanged;
            
            // Subscribe to editing events (for dragging markers)
            timeline.OnExitTimeChanged += OnTimelineExitTimeChanged;
            timeline.OnTransitionDurationChanged += OnTimelineDurationChanged;
            timeline.OnTransitionOffsetChanged += OnTimelineOffsetChanged;
            
            section.Add(timeline);
        }
        
        private void OnTimelineTimeChanged(float time)
        {
            OnTimeChanged?.Invoke(time);
            // Update EditorState preview state
            EditorState.Instance.PreviewState.NormalizedTime = timeline.NormalizedTime;
            OnRepaintRequested?.Invoke();
        }
        
        private void OnTimelinePlayStateChanged(bool isPlaying)
        {
            EditorState.Instance.PreviewState.IsPlaying = isPlaying;
            OnPlayStateChanged?.Invoke(isPlaying);
            OnRepaintRequested?.Invoke();
        }
        
        private void OnTimelineProgressChanged(float progress)
        {
            EditorState.Instance.PreviewState.TransitionProgress = progress;
        }
        
        private void OnTimelineExitTimeChanged(float newExitTime)
        {
            if (cachedExitTimeProperty != null && cachedSerializedObject != null)
            {
                // Record undo and mark dirty for proper save/undo support
                Undo.RecordObject(cachedSerializedObject.targetObject, "Change Transition Exit Time");
                
                cachedSerializedObject.Update();
                cachedExitTimeProperty.floatValue = newExitTime;
                cachedSerializedObject.ApplyModifiedProperties();
                
                // Ensure asset is marked dirty for saving
                EditorUtility.SetDirty(cachedSerializedObject.targetObject);
                
                transitionExitTime = newExitTime;
            }
            OnRepaintRequested?.Invoke();
            OnTransitionPropertiesChanged?.Invoke();
        }
        
        private void OnTimelineDurationChanged(float newDuration)
        {
            if (cachedDurationProperty != null && cachedSerializedObject != null)
            {
                // Record undo and mark dirty for proper save/undo support
                Undo.RecordObject(cachedSerializedObject.targetObject, "Change Transition Duration");
                
                cachedSerializedObject.Update();
                cachedDurationProperty.floatValue = newDuration;
                cachedSerializedObject.ApplyModifiedProperties();
                
                // Ensure asset is marked dirty for saving
                EditorUtility.SetDirty(cachedSerializedObject.targetObject);
                
                transitionDuration = newDuration;
            }
            OnRepaintRequested?.Invoke();
            OnTransitionPropertiesChanged?.Invoke();
        }
        
        private void OnTimelineOffsetChanged(float newOffset)
        {
            // TransitionOffset is not currently stored in the transition data
            // This would need a new field in StateOutTransition if we want to persist it
            // For now, just update the preview
            OnRepaintRequested?.Invoke();
        }
        
        #endregion
        
        #region Private - Blend Controls (Polymorphic)
        
        private static bool IsBlendState(AnimationStateAsset state)
        {
            return AnimationStateUtils.IsBlendState(state);
        }
        
        /// <summary>
        /// Builds blend controls for a state using polymorphism.
        /// Creates the appropriate visual element type based on state type.
        /// Uses pure UIToolkit for consistent event handling.
        /// </summary>
        private void BuildBlendControls(VisualElement section, AnimationStateAsset state, bool isFromState)
        {
            if (state == null) return;
            
            // Create the appropriate visual element based on state type
            var (element, blendInfo) = CreateBlendSpaceElement(state);
            if (element == null) return;
            
            // Store reference
            if (isFromState)
                fromBlendSpaceElement = element;
            else
                toBlendSpaceElement = element;
            
            // Configure element
            element.ShowPreviewIndicator = true;
            element.EditMode = false;
            element.ShowModeToggle = false;
            
            // Build parameter info
            foreach (var (label, name) in blendInfo.ParameterNames)
            {
                section.Add(CreatePropertyRow(label, name));
            }
            
            // Build sliders based on dimensionality
            // Track slider/field references for bidirectional sync
            Slider xSlider = null, ySlider = null;
            FloatField xField = null, yField = null;
            
            // Restore persisted blend position for this state
            Vector2 currentPosition = blendInfo.Is2D 
                ? PreviewSettings.instance.GetBlendValue2D(state) 
                : new Vector2(PreviewSettings.instance.GetBlendValue1D(state), 0);
            element.PreviewPosition = currentPosition;
            
            if (blendInfo.Is2D)
            {
                // X slider - use persisted value
                var xRow = CreateSliderWithField("X", blendInfo.MinX, blendInfo.MaxX, currentPosition.x,
                    out xSlider, out xField,
                    newValue =>
                    {
                        currentPosition.x = newValue;
                        element.PreviewPosition = currentPosition;
                        SaveAndRaiseBlendPositionChanged(state, currentPosition, isFromState, true);
                    });
                section.Add(xRow);
                
                // Y slider - use persisted value
                var yRow = CreateSliderWithField("Y", blendInfo.MinY, blendInfo.MaxY, currentPosition.y,
                    out ySlider, out yField,
                    newValue =>
                    {
                        currentPosition.y = newValue;
                        element.PreviewPosition = currentPosition;
                        SaveAndRaiseBlendPositionChanged(state, currentPosition, isFromState, true);
                    });
                section.Add(yRow);
            }
            else
            {
                // Single blend value slider - use persisted value
                var sliderRow = CreateSliderWithField("Blend Value", blendInfo.MinX, blendInfo.MaxX, currentPosition.x,
                    out xSlider, out xField,
                    newValue =>
                    {
                        currentPosition = new Vector2(newValue, 0);
                        element.PreviewPosition = currentPosition;
                        SaveAndRaiseBlendPositionChanged(state, currentPosition, isFromState, false);
                    });
                section.Add(sliderRow);
            }
            
            // Add UIToolkit visual element directly (no IMGUIContainer wrapper needed)
            element.AddToClassList(blendInfo.Is2D ? "transition-blend-space-2d" : "transition-blend-space-1d");
            section.Add(element);
            
            // Wire up position change from visual element (bidirectional sync)
            Action<Vector2> positionHandler = pos =>
            {
                currentPosition = pos;
                
                // Sync sliders with new position from visual element
                xSlider?.SetValueWithoutNotify(pos.x);
                xField?.SetValueWithoutNotify(pos.x);
                ySlider?.SetValueWithoutNotify(pos.y);
                yField?.SetValueWithoutNotify(pos.y);
                
                SaveAndRaiseBlendPositionChanged(state, pos, isFromState, blendInfo.Is2D);
                OnRepaintRequested?.Invoke();
            };
            element.OnPreviewPositionChanged += positionHandler;
            
            // Store handler for cleanup
            if (isFromState)
            {
                fromBlendPositionHandler = positionHandler;
            }
            else
            {
                toBlendPositionHandler = positionHandler;
            }
        }
        
        /// <summary>
        /// Creates the appropriate blend space visual element and extracts blend info from the state.
        /// Uses pure UIToolkit elements for consistent event handling.
        /// </summary>
        private static (BlendSpaceVisualElement element, BlendInfo info) CreateBlendSpaceElement(AnimationStateAsset state)
        {
            switch (state)
            {
                case LinearBlendStateAsset linearBlend:
                    var element1D = new BlendSpace1DVisualElement();
                    element1D.SetTarget(linearBlend);
                    return (element1D, GetBlendInfo(linearBlend));
                    
                case Directional2DBlendStateAsset blend2D:
                    var element2D = new BlendSpace2DVisualElement();
                    element2D.SetTarget(blend2D);
                    return (element2D, GetBlendInfo(blend2D));
                    
                default:
                    return (null, default);
            }
        }
        
        /// <summary>
        /// Extracts blend range and parameter info from a 1D blend state.
        /// </summary>
        private static BlendInfo GetBlendInfo(LinearBlendStateAsset state)
        {
            float min = 0f, max = 1f;
            
            if (state.BlendClips != null && state.BlendClips.Length > 0)
            {
                min = float.MaxValue;
                max = float.MinValue;
                foreach (var clip in state.BlendClips)
                {
                    min = Mathf.Min(min, clip.Threshold);
                    max = Mathf.Max(max, clip.Threshold);
                }
                var range = max - min;
                if (range < 0.1f) range = 1f;
                min -= range * 0.1f;
                max += range * 0.1f;
            }
            
            return new BlendInfo
            {
                Is2D = false,
                MinX = min, MaxX = max,
                MinY = 0, MaxY = 0,
                ParameterNames = new[] { ("Parameter", state.BlendParameter?.name ?? "(none)") }
            };
        }
        
        /// <summary>
        /// Extracts blend range and parameter info from a 2D blend state.
        /// </summary>
        private static BlendInfo GetBlendInfo(Directional2DBlendStateAsset state)
        {
            float minX = -1f, maxX = 1f, minY = -1f, maxY = 1f;
            
            if (state.BlendClips != null && state.BlendClips.Length > 0)
            {
                minX = float.MaxValue; maxX = float.MinValue;
                minY = float.MaxValue; maxY = float.MinValue;
                foreach (var clip in state.BlendClips)
                {
                    minX = Mathf.Min(minX, clip.Position.x);
                    maxX = Mathf.Max(maxX, clip.Position.x);
                    minY = Mathf.Min(minY, clip.Position.y);
                    maxY = Mathf.Max(maxY, clip.Position.y);
                }
                var rangeX = maxX - minX;
                var rangeY = maxY - minY;
                if (rangeX < 0.1f) rangeX = 1f;
                if (rangeY < 0.1f) rangeY = 1f;
                minX -= rangeX * 0.1f;
                maxX += rangeX * 0.1f;
                minY -= rangeY * 0.1f;
                maxY += rangeY * 0.1f;
            }
            
            return new BlendInfo
            {
                Is2D = true,
                MinX = minX, MaxX = maxX,
                MinY = minY, MaxY = maxY,
                ParameterNames = new[]
                {
                    ("Parameter X", state.BlendParameterX?.name ?? "(none)"),
                    ("Parameter Y", state.BlendParameterY?.name ?? "(none)")
                }
            };
        }
        
        private void SaveAndRaiseBlendPositionChanged(AnimationStateAsset state, Vector2 position, bool isFromState, bool is2D)
        {
            // Persist to settings (shared across all previews of this state)
            if (is2D)
                PreviewSettings.instance.SetBlendValue2D(state, position);
            else
                PreviewSettings.instance.SetBlendValue1D(state, position.x);
            
            // Update EditorState preview state
            var float2Position = new Unity.Mathematics.float2(position.x, position.y);
            if (isFromState)
                EditorState.Instance.PreviewState.BlendPosition = float2Position;
            else
                EditorState.Instance.PreviewState.ToBlendPosition = float2Position;
        }
        
        private static void CleanupBlendSpaceElement(ref BlendSpaceVisualElement element, Action<Vector2> positionHandler)
        {
            if (element != null)
            {
                if (positionHandler != null) element.OnPreviewPositionChanged -= positionHandler;
                element = null;
            }
        }
        
        /// <summary>
        /// Blend space configuration extracted from a state.
        /// </summary>
        private struct BlendInfo
        {
            public bool Is2D;
            public float MinX, MaxX;
            public float MinY, MaxY;
            public (string label, string name)[] ParameterNames;
        }
        
        #endregion
        
        #region Private - Transition Lookup
        
        private (StateOutTransition transition, SerializedProperty property, SerializedObject serializedObject) FindSelectedTransitionWithProperty()
        {
            if (currentStateMachine == null)
                return (null, null, null);
            
            if (isAnyStateTransition && transitionTo != null)
            {
                var machineSerializedObject = new SerializedObject(currentStateMachine);
                var anyStateTransitionsProp = machineSerializedObject.FindProperty("AnyStateTransitions");
                
                if (anyStateTransitionsProp != null)
                {
                    for (int i = 0; i < currentStateMachine.AnyStateTransitions.Count; i++)
                    {
                        var t = currentStateMachine.AnyStateTransitions[i];
                        if (t.ToState == transitionTo)
                        {
                            var transitionProp = anyStateTransitionsProp.GetArrayElementAtIndex(i);
                            return (t, transitionProp, machineSerializedObject);
                        }
                    }
                }
            }
            else if (transitionFrom != null)
            {
                var stateSerializedObject = new SerializedObject(transitionFrom);
                var outTransitionsProp = stateSerializedObject.FindProperty("OutTransitions");
                
                if (outTransitionsProp != null)
                {
                    for (int i = 0; i < transitionFrom.OutTransitions.Count; i++)
                    {
                        var t = transitionFrom.OutTransitions[i];
                        if (t.ToState == transitionTo)
                        {
                            var transitionProp = outTransitionsProp.GetArrayElementAtIndex(i);
                            return (t, transitionProp, stateSerializedObject);
                        }
                    }
                }
            }
            
            return (null, null, null);
        }
        
        #endregion
        
        #region Private - Helpers
        
        private static string GetConditionDescription(TransitionCondition condition)
        {
            if (condition.Parameter is BoolParameterAsset)
            {
                var comparison = (BoolConditionComparison)condition.ComparisonMode;
                return comparison == BoolConditionComparison.True ? "== true" : "== false";
            }
            
            if (condition.Parameter is IntParameterAsset || condition.Parameter is EnumParameterAsset)
            {
                var comparison = (IntConditionComparison)condition.ComparisonMode;
                var value = (int)condition.ComparisonValue;
                return comparison switch
                {
                    IntConditionComparison.Equal => $"== {value}",
                    IntConditionComparison.NotEqual => $"!= {value}",
                    IntConditionComparison.Greater => $"> {value}",
                    IntConditionComparison.Less => $"< {value}",
                    IntConditionComparison.GreaterOrEqual => $">= {value}",
                    IntConditionComparison.LessOrEqual => $"<= {value}",
                    _ => comparison.ToString()
                };
            }
            
            if (condition.Parameter is FloatParameterAsset)
            {
                var comparison = (IntConditionComparison)condition.ComparisonMode;
                var value = condition.ComparisonValue;
                return comparison switch
                {
                    IntConditionComparison.Greater => $"> {value:F2}",
                    IntConditionComparison.Less => $"< {value:F2}",
                    _ => comparison.ToString()
                };
            }
            
            return "?";
        }
        
        #endregion
    }
}
