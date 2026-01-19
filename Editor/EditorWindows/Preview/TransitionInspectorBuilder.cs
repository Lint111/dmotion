using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Builds inspector UI for transitions in the preview window.
    /// Handles regular transitions and Any State transitions.
    /// </summary>
    internal class TransitionInspectorBuilder
    {
        #region Dependencies
        
        private readonly Func<string, string, VisualElement> createSectionHeader;
        private readonly Func<string, VisualElement> createSection;
        private readonly Func<string, string, VisualElement> createPropertyRow;
        private readonly Func<SerializedProperty, string, float, float, string, VisualElement> createEditableSerializedFloatProperty;
        
        #endregion
        
        #region State
        
        private StateMachineAsset currentStateMachine;
        private AnimationStateAsset transitionFrom;
        private AnimationStateAsset transitionTo;
        private bool isAnyStateTransition;
        
        #endregion
        
        #region Constructor
        
        public TransitionInspectorBuilder(
            Func<string, string, VisualElement> createSectionHeader,
            Func<string, VisualElement> createSection,
            Func<string, string, VisualElement> createPropertyRow,
            Func<SerializedProperty, string, float, float, string, VisualElement> createEditableSerializedFloatProperty)
        {
            this.createSectionHeader = createSectionHeader;
            this.createSection = createSection;
            this.createPropertyRow = createPropertyRow;
            this.createEditableSerializedFloatProperty = createEditableSerializedFloatProperty;
        }
        
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
            
            string fromName = isAnyState ? "Any State" : (fromState?.name ?? "?");
            string toName = toState?.name ?? "(exit)";
            
            // Header
            var header = createSectionHeader("Transition", $"{fromName} -> {toName}");
            container.Add(header);
            
            // Find the transition and its serialized property
            var (transition, transitionProperty, sourceSerializedObject) = FindSelectedTransitionWithProperty();
            
            // Properties section
            var propertiesSection = createSection("Properties");
            
            if (transition != null && transitionProperty != null)
            {
                BuildEditableTransitionProperties(propertiesSection, transition, transitionProperty);
                
                // Bind to enable updates
                container.Bind(sourceSerializedObject);
            }
            else
            {
                propertiesSection.Add(createPropertyRow("Duration", transition?.TransitionDuration.ToString("F2") + "s" ?? "?"));
            }
            
            container.Add(propertiesSection);
            
            // Transition progress placeholder
            var progressSection = createSection("Transition Progress");
            var progressPlaceholder = new Label(PreviewWindowConstants.TransitionProgressPlaceholder);
            progressPlaceholder.AddToClassList("placeholder-message");
            progressSection.Add(progressPlaceholder);
            container.Add(progressSection);
            
            return container;
        }
        
        /// <summary>
        /// Builds the inspector UI for Any State (not a transition, just the node).
        /// </summary>
        public VisualElement BuildAnyState()
        {
            var container = new VisualElement();
            
            var header = createSectionHeader("Any State", "Global transition source");
            container.Add(header);
            
            var infoSection = createSection("Info");
            var infoLabel = new Label("Any State transitions can target any state in the machine.\nSelect a transition to see its properties.");
            infoLabel.AddToClassList("info-message");
            infoSection.Add(infoLabel);
            container.Add(infoSection);
            
            return container;
        }
        
        #endregion
        
        #region Private - UI Building
        
        private void BuildEditableTransitionProperties(
            VisualElement section,
            StateOutTransition transition,
            SerializedProperty transitionProperty)
        {
            // Editable Duration
            var durationProp = transitionProperty.FindPropertyRelative("TransitionDuration");
            if (durationProp != null)
            {
                var durationContainer = new VisualElement();
                durationContainer.AddToClassList("property-row");
                durationContainer.AddToClassList("editable-property");
                
                var durationLabel = new Label("Duration");
                durationLabel.AddToClassList("property-label");
                durationContainer.Add(durationLabel);
                
                var valueContainer = new VisualElement();
                valueContainer.AddToClassList("property-value-container");
                valueContainer.style.flexDirection = FlexDirection.Row;
                valueContainer.style.flexGrow = 1;
                
                var slider = new Slider(0f, PreviewWindowConstants.MaxTransitionDuration);
                slider.AddToClassList("property-slider");
                slider.style.flexGrow = 1;
                slider.BindProperty(durationProp);
                
                var floatField = new FloatField();
                floatField.AddToClassList("property-float-field");
                floatField.style.width = 50;
                floatField.style.marginLeft = 4;
                floatField.BindProperty(durationProp);
                
                var suffixLabel = new Label("s");
                suffixLabel.AddToClassList("property-suffix");
                suffixLabel.style.marginLeft = 2;
                
                valueContainer.Add(slider);
                valueContainer.Add(floatField);
                valueContainer.Add(suffixLabel);
                durationContainer.Add(valueContainer);
                
                section.Add(durationContainer);
            }
            
            // HasEndTime toggle
            var hasEndTimeProp = transitionProperty.FindPropertyRelative("HasEndTime");
            if (hasEndTimeProp != null)
            {
                var hasEndTimeContainer = new VisualElement();
                hasEndTimeContainer.AddToClassList("property-row");
                hasEndTimeContainer.AddToClassList("editable-property");
                
                var hasEndTimeLabel = new Label("Has Exit Time");
                hasEndTimeLabel.AddToClassList("property-label");
                hasEndTimeContainer.Add(hasEndTimeLabel);
                
                var hasEndTimeToggle = new Toggle();
                hasEndTimeToggle.AddToClassList("property-toggle");
                hasEndTimeToggle.BindProperty(hasEndTimeProp);
                hasEndTimeContainer.Add(hasEndTimeToggle);
                
                section.Add(hasEndTimeContainer);
            }
            
            // EndTime (only shown if HasEndTime is true)
            var endTimeProp = transitionProperty.FindPropertyRelative("EndTime");
            if (endTimeProp != null && transition.HasEndTime)
            {
                var endTimeContainer = createEditableSerializedFloatProperty(endTimeProp, "Exit Time", 0f, 5f, "s");
                section.Add(endTimeContainer);
            }
            
            // Conditions (read-only for now)
            if (transition.Conditions != null && transition.Conditions.Count > 0)
            {
                var conditionsLabel = new Label($"Conditions: {transition.Conditions.Count}");
                conditionsLabel.AddToClassList("subsection-label");
                section.Add(conditionsLabel);
                
                for (int i = 0; i < transition.Conditions.Count; i++)
                {
                    var condition = transition.Conditions[i];
                    var paramName = condition.Parameter?.name ?? "(none)";
                    var conditionDesc = GetConditionDescription(condition);
                    section.Add(createPropertyRow($"  {paramName}", conditionDesc));
                }
            }
        }
        
        #endregion
        
        #region Private - Transition Lookup
        
        private (StateOutTransition transition, SerializedProperty property, SerializedObject serializedObject) FindSelectedTransitionWithProperty()
        {
            if (currentStateMachine == null)
                return (null, null, null);
            
            if (isAnyStateTransition && transitionTo != null)
            {
                // Any State transitions are stored in StateMachineAsset.AnyStateTransitions
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
                // Regular transitions are stored in the source state's OutTransitions
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
