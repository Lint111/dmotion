using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Defines the context for graph operations (search, context menu, etc.).
    /// Different contexts provide different available actions.
    /// </summary>
    internal enum GraphContext
    {
        /// <summary>Multi-layer root - can only add layers</summary>
        MultiLayerRoot,
        
        /// <summary>Inside a layer or single-layer machine - can add states, transitions</summary>
        StateLayer,
        
        /// <summary>Inside a SubStateMachine - same as StateLayer</summary>
        SubStateMachine
    }

    /// <summary>
    /// Provides context-aware search tree entries for the graph editor.
    /// </summary>
    internal interface IGraphContextProvider
    {
        /// <summary>
        /// Gets the current context type.
        /// </summary>
        GraphContext Context { get; }
        
        /// <summary>
        /// Creates search tree entries appropriate for the current context.
        /// </summary>
        List<SearchTreeEntry> CreateSearchTree();
        
        /// <summary>
        /// Handles selection of a search entry.
        /// </summary>
        /// <param name="entry">The selected entry</param>
        /// <param name="graphView">The graph view to modify</param>
        /// <param name="position">Position to create the element</param>
        /// <returns>True if handled</returns>
        bool OnSelectEntry(SearchTreeEntry entry, AnimationStateMachineEditorView graphView, Vector2 position);
    }

    /// <summary>
    /// Context provider for multi-layer root - only allows adding layers.
    /// </summary>
    internal class MultiLayerRootContextProvider : IGraphContextProvider
    {
        public GraphContext Context => GraphContext.MultiLayerRoot;

        public List<SearchTreeEntry> CreateSearchTree()
        {
            return new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Add Layer"), 0),
                new SearchTreeEntry(new GUIContent("New Layer", EditorGUIUtility.IconContent("AnimatorController Icon").image))
                {
                    level = 1,
                    userData = "AddLayer"
                }
            };
        }

        public bool OnSelectEntry(SearchTreeEntry entry, AnimationStateMachineEditorView graphView, Vector2 position)
        {
            if (entry.userData as string == "AddLayer")
            {
                var stateMachine = graphView.StateMachine;
                if (stateMachine == null) return false;

                Undo.RecordObject(stateMachine, "Add Layer");
                var layer = stateMachine.AddLayer();
                
                // Set position for the new layer
                layer.StateEditorData.GraphPosition = position;
                EditorUtility.SetDirty(layer);
                
                // Raise event to refresh view
                StateMachineEditorEvents.RaiseLayerAdded(stateMachine, layer);
                
                // Refresh the graph view
                graphView.RefreshAfterLayerChange();
                
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Context provider for state layer - allows adding states and SubStateMachines.
    /// </summary>
    internal class StateLayerContextProvider : IGraphContextProvider
    {
        public GraphContext Context => GraphContext.StateLayer;

        public List<SearchTreeEntry> CreateSearchTree()
        {
            return new List<SearchTreeEntry>
            {
                // Root group header
                new SearchTreeGroupEntry(new GUIContent("Create State"), 0),
                
                // Basic States category
                new SearchTreeGroupEntry(new GUIContent("States"), 1),
                new SearchTreeEntry(new GUIContent("Single Clip", EditorGUIUtility.IconContent("AnimationClip Icon").image))
                {
                    level = 2,
                    userData = typeof(SingleClipStateAsset)
                },
                
                // Blend Trees category
                new SearchTreeGroupEntry(new GUIContent("Blend Trees"), 1),
                new SearchTreeEntry(new GUIContent("Blend Tree 1D", EditorGUIUtility.IconContent("BlendTree Icon").image))
                {
                    level = 2,
                    userData = typeof(LinearBlendStateAsset)
                },
                new SearchTreeEntry(new GUIContent("Blend Tree 2D (Simple Directional)", EditorGUIUtility.IconContent("BlendTree Icon").image))
                {
                    level = 2,
                    userData = typeof(Directional2DBlendStateAsset)
                },
                
                // Organization category
                new SearchTreeGroupEntry(new GUIContent("Organization"), 1),
                new SearchTreeEntry(new GUIContent("Sub-State Machine", EditorGUIUtility.IconContent("AnimatorController Icon").image))
                {
                    level = 2,
                    userData = typeof(SubStateMachineStateAsset)
                }
            };
        }

        public bool OnSelectEntry(SearchTreeEntry entry, AnimationStateMachineEditorView graphView, Vector2 position)
        {
            if (entry.userData is Type stateType)
            {
                graphView.CreateStateAtPosition(stateType, position);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Factory for creating context providers based on graph state.
    /// </summary>
    internal static class GraphContextProviderFactory
    {
        private static readonly MultiLayerRootContextProvider multiLayerProvider = new();
        private static readonly StateLayerContextProvider stateLayerProvider = new();

        /// <summary>
        /// Gets the appropriate context provider for the current state machine.
        /// </summary>
        public static IGraphContextProvider GetProvider(StateMachineAsset stateMachine)
        {
            if (stateMachine == null)
                return stateLayerProvider;

            if (stateMachine.IsMultiLayer)
                return multiLayerProvider;

            return stateLayerProvider;
        }
        
        /// <summary>
        /// Gets the appropriate context provider for a graph view.
        /// </summary>
        public static IGraphContextProvider GetProvider(AnimationStateMachineEditorView graphView)
        {
            return GetProvider(graphView?.StateMachine);
        }
    }
}
