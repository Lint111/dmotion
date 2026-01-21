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
        
        private const float MaxTransitionDuration = 2f;
        private const float MaxExitTime = 5f;
        private const float BlendSpace1DHeight = 60f;
        private const float BlendSpace2DHeight = 150f;
        
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
        
        // Blend space editors - using base class references (polymorphism)
        private BlendSpaceVisualEditorBase fromBlendSpaceEditor;
        private BlendSpaceVisualEditorBase toBlendSpaceEditor;
        
        // Cached event handlers for cleanup
        private Action<Vector2> fromBlendPositionHandler;
        private Action<Vector2> toBlendPositionHandler;
        private Action fromRepaintHandler;
        private Action toRepaintHandler;
        
        // Cached arrays to avoid per-frame allocation in IMGUI callbacks
        private const int CurvePreviewSegments = 30;
        private static readonly Vector3[] cachedCurvePreviewPoints = new Vector3[CurvePreviewSegments + 1];
        private static GUIStyle cachedCurvePreviewLabelStyle;
        
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

            // Cleanup blend space editors

            CleanupBlendSpaceEditor(ref fromBlendSpaceEditor, fromBlendPositionHandler, fromRepaintHandler);
            CleanupBlendSpaceEditor(ref toBlendSpaceEditor, toBlendPositionHandler, toRepaintHandler);
            
            fromBlendPositionHandler = null;
            toBlendPositionHandler = null;
            fromRepaintHandler = null;
            toRepaintHandler = null;
            
            // Clear cached serialized data
            cachedSerializedObject = null;
            cachedTransitionProperty = null;
            cachedDurationProperty = null;
            cachedExitTimeProperty = null;
            cachedBlendCurveProperty = null;
            cachedBlendCurve = null;
            
            transitionFrom = null;
            transitionTo = null;
            currentStateMachine = null;
        }
        
        /// <summary>
        /// Ticks the timeline for playback. Call from Update loop.
        /// </summary>
        public void Tick(float deltaTime)
        {
            timeline?.Tick(deltaTime);
        }
        
        /// <summary>
        /// Builds the inspector UI for Any State (not a transition, just the node).
        /// </summary>
        public VisualElement BuildAnyState()
        {
            var container = new VisualElement();
            container.AddToClassList("transition-inspector");
            
            var header = CreateSectionHeader("Any State", "Global transition source");
            container.Add(header);
            
            var infoSection = CreateSection("Info");
            var infoLabel = new Label("Any State transitions can target any state in the machine.\nSelect a transition to see its properties.");
            infoLabel.AddToClassList("info-message");
            infoLabel.style.whiteSpace = WhiteSpace.Normal;
            infoLabel.style.color = PreviewEditorColors.DimText;
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
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingBottom = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = PreviewEditorColors.Border;
            header.style.marginBottom = 8;

            var typeLabel = new Label(type);
            typeLabel.AddToClassList("header-type");
            typeLabel.style.color = PreviewEditorColors.DimText;
            typeLabel.style.marginRight = 8;

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("header-name");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

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
            header.style.flexDirection = FlexDirection.Row;
            header.style.flexWrap = Wrap.Wrap;
            header.style.paddingBottom = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = PreviewEditorColors.Border;
            header.style.marginBottom = 8;

            // Type label
            var typeLabel = new Label("Transition");
            typeLabel.AddToClassList("header-type");
            typeLabel.style.color = PreviewEditorColors.DimText;
            typeLabel.style.marginRight = 8;
            header.Add(typeLabel);

            // From state - clickable if it's an actual state (not Any State)
            string fromName = isAnyState ? "Any State" : (fromState?.name ?? "?");
            var fromLabel = new Label(fromName);
            fromLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            
            if (!isAnyState && fromState != null)
            {
                fromLabel.style.color = PreviewEditorColors.FromState;
                fromLabel.style.cursor = StyleKeyword.None;
                fromLabel.tooltip = $"Click to preview {fromState.name}";
                fromLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    AnimationPreviewEvents.RaiseNavigateToState(fromState);
                    evt.StopPropagation();
                });
                fromLabel.RegisterCallback<MouseEnterEvent>(_ => fromLabel.style.color = PreviewEditorColors.FromStateHighlight);
                fromLabel.RegisterCallback<MouseLeaveEvent>(_ => fromLabel.style.color = PreviewEditorColors.FromState);
            }
            else
            {
                fromLabel.style.color = PreviewEditorColors.DimText;
            }
            header.Add(fromLabel);

            // Arrow
            var arrowLabel = new Label(" -> ");
            arrowLabel.style.color = PreviewEditorColors.DimText;
            header.Add(arrowLabel);

            // To state - clickable if it exists
            string toName = toState?.name ?? "(exit)";
            var toLabel = new Label(toName);
            toLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            
            if (toState != null)
            {
                toLabel.style.color = PreviewEditorColors.ToState;
                toLabel.style.cursor = StyleKeyword.None;
                toLabel.tooltip = $"Click to preview {toState.name}";
                toLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    AnimationPreviewEvents.RaiseNavigateToState(toState);
                    evt.StopPropagation();
                });
                toLabel.RegisterCallback<MouseEnterEvent>(_ => toLabel.style.color = PreviewEditorColors.ToStateHighlight);
                toLabel.RegisterCallback<MouseLeaveEvent>(_ => toLabel.style.color = PreviewEditorColors.ToState);
            }
            else
            {
                toLabel.style.color = PreviewEditorColors.DimText;
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
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;

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
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("property-label");
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;
            container.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;
            
            var slider = new Slider(min, max);
            slider.AddToClassList("property-slider");
            slider.style.flexGrow = 1;
            slider.value = value;
            
            var field = new FloatField();
            field.AddToClassList("property-float-field");
            field.style.width = FloatFieldWidth;
            field.style.marginLeft = 4;
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
                suffixLabel.style.marginLeft = 2;
                suffixLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
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
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;
            
            var labelElement = new Label(label);
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;
            container.Add(labelElement);
            
            var valueContainer = new VisualElement();
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.flexGrow = 1;
            
            var slider = new Slider(min, max);
            slider.style.flexGrow = 1;
            slider.BindProperty(property);
            
            if (onChanged != null)
            {
                slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            }
            
            var field = new FloatField();
            field.style.width = FloatFieldWidth;
            field.style.marginLeft = 4;
            field.BindProperty(property);
            
            valueContainer.Add(slider);
            valueContainer.Add(field);
            
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixLabel = new Label(suffix);
                suffixLabel.style.marginLeft = 2;
                valueContainer.Add(suffixLabel);
            }
            
            container.Add(valueContainer);
            return container;
        }
        
        private VisualElement CreateToggleRow(string label, bool value, Action<bool> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("property-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;
            
            var labelElement = new Label(label);
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;
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
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;
            
            var labelElement = new Label(label);
            labelElement.style.width = 100;
            labelElement.style.minWidth = 100;
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
            // Duration
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
            
            // State speeds (read-only info)
            float fromSpeed = transitionFrom?.Speed ?? 1f;
            float toSpeed = transitionTo?.Speed ?? 1f;
            section.Add(CreatePropertyRow("From Speed", $"{fromSpeed:F2}x"));
            section.Add(CreatePropertyRow("To Speed", $"{toSpeed:F2}x"));
            
            // Loop toggle (preview-only)
            var loopRow = CreateToggleRow("Loop", false,
                newValue =>
                {
                    cachedIsLooping = newValue;
                    if (timeline != null)
                        timeline.IsLooping = newValue;
                });
            section.Add(loopRow);
            
            // Exit Time (always shown - controlled by To bar position in timeline)
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
                    section.Add(exitTimeRow);
                }
            }
            
            // Conditions (read-only)
            if (transition != null && transition.Conditions != null && transition.Conditions.Count > 0)
            {
                var conditionsLabel = new Label($"Conditions: {transition.Conditions.Count}");
                conditionsLabel.style.marginTop = 8;
                conditionsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                section.Add(conditionsLabel);
                
                for (int i = 0; i < transition.Conditions.Count; i++)
                {
                    var condition = transition.Conditions[i];
                    var paramName = condition.Parameter?.name ?? "(none)";
                    var conditionDesc = GetConditionDescription(condition);
                    section.Add(CreatePropertyRow($"  {paramName}", conditionDesc));
                }
            }
            
            // Blend Curve - uses custom editor window with correct presets
            if (transitionProperty != null)
            {
                var blendCurveProp = transitionProperty.FindPropertyRelative("BlendCurve");
                if (blendCurveProp != null)
                {
                    var curveSection = new VisualElement();
                    curveSection.style.marginTop = 8;
                    
                    // Header row with label and edit button
                    var headerRow = new VisualElement();
                    headerRow.style.flexDirection = FlexDirection.Row;
                    headerRow.style.justifyContent = Justify.SpaceBetween;
                    headerRow.style.alignItems = Align.Center;
                    
                    var curveLabel = new Label("Blend Curve");
                    curveLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    headerRow.Add(curveLabel);
                    
                    curveSection.Add(headerRow);
                    
                    // Curve preview (IMGUI for drawing)
                    IMGUIContainer curvePreview = null;
                    curvePreview = new IMGUIContainer(() =>
                    {
                        var rect = curvePreview.contentRect;
                        if (rect.width < 10 || rect.height < 10) return;
                        
                        // Background
                        EditorGUI.DrawRect(rect, PreviewEditorColors.DarkBackground);
                        
                        // Draw curve - use cached arrays to avoid allocation
                        var curve = cachedBlendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
                        
                        Handles.BeginGUI();
                        Handles.color = PreviewEditorColors.CurveAccent;
                        
                        float padding = 4f;
                        float curveWidth = rect.width - padding * 2;
                        float curveHeight = rect.height - padding * 2;
                        
                        for (int i = 0; i <= CurvePreviewSegments; i++)
                        {
                            float t = i / (float)CurvePreviewSegments;
                            float value = curve.Evaluate(t);
                            float x = rect.x + padding + t * curveWidth;
                            float y = rect.y + padding + (1f - value) * curveHeight;
                            cachedCurvePreviewPoints[i] = new Vector3(x, y, 0);
                        }
                        
                        Handles.DrawAAPolyLine(2f, cachedCurvePreviewPoints);
                        Handles.EndGUI();
                        
                        // Labels - use cached style
                        cachedCurvePreviewLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
                        {
                            fontSize = 9,
                            normal = { textColor = PreviewEditorColors.DimText }
                        };
                        GUI.Label(new Rect(rect.x + 2, rect.y + 2, 30, 12), "From", cachedCurvePreviewLabelStyle);
                        GUI.Label(new Rect(rect.x + 2, rect.yMax - 14, 20, 12), "To", cachedCurvePreviewLabelStyle);
                        
                        // Click to edit
                        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                        {
                            BlendCurveEditorWindow.Show(
                                cachedBlendCurve ?? AnimationCurve.Linear(0f, 1f, 1f, 0f),
                                newCurve =>
                                {
                                    cachedBlendCurve = newCurve;
                                    blendCurveProp.animationCurveValue = newCurve;
                                    blendCurveProp.serializedObject.ApplyModifiedProperties();
                                    if (timeline != null)
                                        timeline.BlendCurve = newCurve;
                                },
                                curvePreview.worldBound);
                            Event.current.Use();
                        }
                    });
                    curvePreview.style.height = 60;
                    curvePreview.style.marginTop = 4;
                    curvePreview.style.cursor = StyleKeyword.None; // Will show as clickable
                    curvePreview.tooltip = "Click to edit curve";
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
            AnimationPreviewEvents.RaiseTransitionTimeChanged(transitionFrom, transitionTo, timeline.NormalizedTime);
            OnRepaintRequested?.Invoke();
        }
        
        private void OnTimelinePlayStateChanged(bool isPlaying)
        {
            AnimationPreviewEvents.RaiseTransitionPlayStateChanged(isPlaying);
            OnRepaintRequested?.Invoke();
        }
        
        private void OnTimelineProgressChanged(float progress)
        {
            AnimationPreviewEvents.RaiseTransitionProgressChanged(transitionFrom, transitionTo, progress);
        }
        
        private void OnTimelineExitTimeChanged(float newExitTime)
        {
            if (cachedExitTimeProperty != null && cachedSerializedObject != null)
            {
                cachedSerializedObject.Update();
                cachedExitTimeProperty.floatValue = newExitTime;
                cachedSerializedObject.ApplyModifiedProperties();
                
                transitionExitTime = newExitTime;
            }
            OnRepaintRequested?.Invoke();
        }
        
        private void OnTimelineDurationChanged(float newDuration)
        {
            if (cachedDurationProperty != null && cachedSerializedObject != null)
            {
                cachedSerializedObject.Update();
                cachedDurationProperty.floatValue = newDuration;
                cachedSerializedObject.ApplyModifiedProperties();
                
                transitionDuration = newDuration;
            }
            OnRepaintRequested?.Invoke();
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
            return state is LinearBlendStateAsset || state is Directional2DBlendStateAsset;
        }
        
        /// <summary>
        /// Builds blend controls for a state using polymorphism.
        /// Creates the appropriate editor type based on state type.
        /// </summary>
        private void BuildBlendControls(VisualElement section, AnimationStateAsset state, bool isFromState)
        {
            if (state == null) return;
            
            // Create the appropriate editor based on state type
            var (editor, blendInfo) = CreateBlendSpaceEditor(state);
            if (editor == null) return;
            
            // Store reference
            if (isFromState)
                fromBlendSpaceEditor = editor;
            else
                toBlendSpaceEditor = editor;
            
            // Configure editor
            editor.ShowPreviewIndicator = true;
            editor.EditMode = false;
            editor.ShowModeToggle = false;
            
            // Build parameter info
            foreach (var (label, name) in blendInfo.ParameterNames)
            {
                section.Add(CreatePropertyRow(label, name));
            }
            
            // Build sliders based on dimensionality
            // Track slider/field references for bidirectional sync
            Slider xSlider = null, ySlider = null;
            FloatField xField = null, yField = null;
            Vector2 currentPosition = Vector2.zero;
            
            if (blendInfo.Is2D)
            {
                // X slider
                var xRow = CreateSliderWithField("X", blendInfo.MinX, blendInfo.MaxX, 0f,
                    out xSlider, out xField,
                    newValue =>
                    {
                        currentPosition.x = newValue;
                        editor.PreviewPosition = currentPosition;
                        RaiseBlendPositionChanged(state, currentPosition, isFromState);
                    });
                section.Add(xRow);
                
                // Y slider
                var yRow = CreateSliderWithField("Y", blendInfo.MinY, blendInfo.MaxY, 0f,
                    out ySlider, out yField,
                    newValue =>
                    {
                        currentPosition.y = newValue;
                        editor.PreviewPosition = currentPosition;
                        RaiseBlendPositionChanged(state, currentPosition, isFromState);
                    });
                section.Add(yRow);
            }
            else
            {
                // Single blend value slider
                float defaultValue = (blendInfo.MinX + blendInfo.MaxX) / 2f;
                var sliderRow = CreateSliderWithField("Blend Value", blendInfo.MinX, blendInfo.MaxX, defaultValue,
                    out xSlider, out xField,
                    newValue =>
                    {
                        currentPosition = new Vector2(newValue, 0);
                        editor.PreviewPosition = currentPosition;
                        RaiseBlendPositionChanged(state, currentPosition, isFromState);
                    });
                section.Add(sliderRow);
            }
            
            // Create IMGUI container for visual editor
            var serializedObject = new SerializedObject(state);
            float editorHeight = blendInfo.Is2D ? BlendSpace2DHeight : BlendSpace1DHeight;
            
            IMGUIContainer blendSpaceContainer = null;
            blendSpaceContainer = new IMGUIContainer(() =>
            {
                if (state != null)
                {
                    var rect = new Rect(0, 0, blendSpaceContainer.contentRect.width, editorHeight);
                    if (rect.width > 10)
                    {
                        editor.Draw(rect, serializedObject);
                    }
                }
            });
            blendSpaceContainer.style.height = editorHeight;
            blendSpaceContainer.style.marginTop = 8;
            blendSpaceContainer.focusable = true;
            
            // Handle mouse events for panning (2D only)
            if (blendInfo.Is2D)
            {
                blendSpaceContainer.RegisterCallback<MouseDownEvent>(evt =>
                {
                    blendSpaceContainer.Focus();
                    if (evt.button == 2 || (evt.button == 0 && evt.altKey))
                    {
                        editor.StartExternalPan(evt.localMousePosition);
                        evt.StopPropagation();
                    }
                    blendSpaceContainer.MarkDirtyRepaint();
                });
                blendSpaceContainer.RegisterCallback<MouseMoveEvent>(evt =>
                {
                    if (editor.IsExternalPanning)
                    {
                        editor.UpdateExternalPan(evt.localMousePosition);
                        blendSpaceContainer.MarkDirtyRepaint();
                        evt.StopPropagation();
                    }
                    else if (evt.pressedButtons != 0)
                    {
                        blendSpaceContainer.MarkDirtyRepaint();
                    }
                });
                blendSpaceContainer.RegisterCallback<MouseUpEvent>(evt =>
                {
                    if (editor.IsExternalPanning)
                    {
                        editor.EndExternalPan();
                        blendSpaceContainer.MarkDirtyRepaint();
                    }
                });
                blendSpaceContainer.RegisterCallback<WheelEvent>(evt => blendSpaceContainer.MarkDirtyRepaint());
            }
            
            section.Add(blendSpaceContainer);
            
            // Wire up position change from visual editor (bidirectional sync)
            Action<Vector2> positionHandler = pos =>
            {
                currentPosition = pos;
                
                // Sync sliders with new position from visual editor
                xSlider?.SetValueWithoutNotify(pos.x);
                xField?.SetValueWithoutNotify(pos.x);
                ySlider?.SetValueWithoutNotify(pos.y);
                yField?.SetValueWithoutNotify(pos.y);
                
                RaiseBlendPositionChanged(state, pos, isFromState);
                OnRepaintRequested?.Invoke();
            };
            editor.OnPreviewPositionChanged += positionHandler;
            
            Action repaintHandler = () => OnRepaintRequested?.Invoke();
            editor.OnRepaintRequested += repaintHandler;
            
            // Store handlers for cleanup
            if (isFromState)
            {
                fromBlendPositionHandler = positionHandler;
                fromRepaintHandler = repaintHandler;
            }
            else
            {
                toBlendPositionHandler = positionHandler;
                toRepaintHandler = repaintHandler;
            }
        }
        
        /// <summary>
        /// Creates the appropriate blend space editor and extracts blend info from the state.
        /// </summary>
        private static (BlendSpaceVisualEditorBase editor, BlendInfo info) CreateBlendSpaceEditor(AnimationStateAsset state)
        {
            switch (state)
            {
                case LinearBlendStateAsset linearBlend:
                    var editor1D = new BlendSpace1DVisualEditor();
                    editor1D.SetTarget(linearBlend);
                    return (editor1D, GetBlendInfo(linearBlend));
                    
                case Directional2DBlendStateAsset blend2D:
                    var editor2D = new BlendSpace2DVisualEditor();
                    editor2D.SetTarget(blend2D);
                    return (editor2D, GetBlendInfo(blend2D));
                    
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
        
        private void RaiseBlendPositionChanged(AnimationStateAsset state, Vector2 position, bool isFromState)
        {
            if (isFromState)
                AnimationPreviewEvents.RaiseTransitionFromBlendPositionChanged(state, position);
            else
                AnimationPreviewEvents.RaiseTransitionToBlendPositionChanged(state, position);
        }
        
        private static void CleanupBlendSpaceEditor(ref BlendSpaceVisualEditorBase editor, Action<Vector2> positionHandler, Action repaintHandler)
        {
            if (editor != null)
            {
                if (positionHandler != null) editor.OnPreviewPositionChanged -= positionHandler;
                if (repaintHandler != null) editor.OnRepaintRequested -= repaintHandler;
                editor = null;
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
