using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides a context-aware searchable popup window for graph operations.
    /// At multi-layer root: shows "Add Layer" only.
    /// Inside layer/single-layer: shows state types (Single Clip, Blend Trees, SubStateMachine).
    /// </summary>
    internal class StateSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private AnimationStateMachineEditorView graphView;
        private Vector2 creationPosition;
        private IGraphContextProvider contextProvider;

        internal void Initialize(AnimationStateMachineEditorView view)
        {
            graphView = view;
        }

        internal void SetCreationPosition(Vector2 graphPosition)
        {
            creationPosition = graphPosition;
            // Update context provider each time position is set (context may have changed)
            contextProvider = GraphContextProviderFactory.GetProvider(graphView);
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            // Use context-aware provider
            contextProvider ??= GraphContextProviderFactory.GetProvider(graphView);
            return contextProvider.CreateSearchTree();
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            contextProvider ??= GraphContextProviderFactory.GetProvider(graphView);
            return contextProvider.OnSelectEntry(entry, graphView, creationPosition);
        }
    }
}
