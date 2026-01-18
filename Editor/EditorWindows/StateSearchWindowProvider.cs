using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides a searchable popup window for creating new state nodes.
    /// Similar to Unity Shader Graph's node creation experience.
    /// Press Space to open, then type to filter or click to select.
    /// </summary>
    internal class StateSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private AnimationStateMachineEditorView graphView;
        private Vector2 creationPosition;

        internal void Initialize(AnimationStateMachineEditorView view)
        {
            graphView = view;
        }

        internal void SetCreationPosition(Vector2 screenPosition)
        {
            creationPosition = screenPosition;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry>
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

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is Type stateType)
            {
                // Convert screen position to graph local position
                // First convert screen to window coordinates
                var editorWindow = EditorWindow.focusedWindow;
                if (editorWindow != null)
                {
                    var windowMousePosition = context.screenMousePosition - editorWindow.position.position;
                    var graphMousePosition = graphView.contentViewContainer.WorldToLocal(windowMousePosition);
                    graphView.CreateStateAtPosition(stateType, graphMousePosition);
                }
                else
                {
                    // Fallback: create at center of view
                    graphView.CreateStateAtPosition(stateType, Vector2.zero);
                }
                return true;
            }
            return false;
        }
    }
}
