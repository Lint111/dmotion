using System;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Interface for state-specific content builders.
    /// Each implementation handles UI for a specific state type.
    /// </summary>
    internal interface IStateContentBuilder
    {
        /// <summary>
        /// Builds the state-specific content (clip info, blend space, etc.).
        /// </summary>
        /// <param name="container">Parent container to add content to.</param>
        /// <param name="context">Shared context with dependencies.</param>
        void Build(VisualElement container, StateContentContext context);
        
        /// <summary>
        /// Configures the timeline for this state type.
        /// </summary>
        /// <param name="scrubber">Timeline scrubber to configure.</param>
        /// <param name="context">Shared context.</param>
        void ConfigureTimeline(TimelineScrubber scrubber, StateContentContext context);
        
        /// <summary>
        /// Cleans up event subscriptions and resources.
        /// </summary>
        void Cleanup();
    }
    
    /// <summary>
    /// Shared context passed to state content builders.
    /// Contains dependencies and state needed across builders.
    /// </summary>
    internal class StateContentContext
    {
        /// <summary>
        /// The state being built.
        /// </summary>
        public AnimationStateAsset State { get; }
        
        /// <summary>
        /// Serialized object for the state.
        /// </summary>
        public SerializedObject SerializedObject { get; }
        
        /// <summary>
        /// Factory for creating section headers.
        /// </summary>
        public Func<string, string, VisualElement> CreateSectionHeader { get; }
        
        /// <summary>
        /// Factory for creating collapsible sections.
        /// </summary>
        public Func<string, VisualElement> CreateSection { get; }
        
        /// <summary>
        /// Factory for creating property rows.
        /// </summary>
        public Func<string, string, VisualElement> CreatePropertyRow { get; }
        
        /// <summary>
        /// Callback when repaint is needed.
        /// </summary>
        public Action RequestRepaint { get; }
        
        public StateContentContext(
            AnimationStateAsset state,
            SerializedObject serializedObject,
            Func<string, string, VisualElement> createSectionHeader,
            Func<string, VisualElement> createSection,
            Func<string, string, VisualElement> createPropertyRow,
            Action requestRepaint)
        {
            State = state;
            SerializedObject = serializedObject;
            CreateSectionHeader = createSectionHeader;
            CreateSection = createSection;
            CreatePropertyRow = createPropertyRow;
            RequestRepaint = requestRepaint;
        }
    }
}
