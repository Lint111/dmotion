using System.Collections.Generic;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Builds a navigation tree from a StateMachineAsset hierarchy.
    /// Only includes navigable containers (Root, Layers, SubStateMachines).
    /// </summary>
    public static class NavigationTreeBuilder
    {
        /// <summary>
        /// Builds the complete navigation tree for a state machine.
        /// </summary>
        /// <param name="rootStateMachine">The root state machine to build from.</param>
        /// <returns>The root node of the navigation tree.</returns>
        public static NavigationTreeNode Build(StateMachineAsset rootStateMachine)
        {
            if (rootStateMachine == null) return null;
            
            var rootNode = new NavigationTreeNode
            {
                Name = rootStateMachine.name,
                NodeType = NavigationNodeType.Root,
                StateMachine = rootStateMachine,
                Depth = 0,
                Parent = null
            };
            
            // For multi-layer state machines, add layers as children
            if (rootStateMachine.IsMultiLayer)
            {
                BuildLayerNodes(rootNode, rootStateMachine);
            }
            else
            {
                // For single-layer, scan for sub-state machines directly
                BuildSubStateMachineNodes(rootNode, rootStateMachine);
            }
            
            return rootNode;
        }
        
        /// <summary>
        /// Builds layer nodes for a multi-layer state machine.
        /// </summary>
        private static void BuildLayerNodes(NavigationTreeNode parentNode, StateMachineAsset stateMachine)
        {
            int layerIndex = 0;
            foreach (var layer in stateMachine.GetLayers())
            {
                var layerNode = new NavigationTreeNode
                {
                    Name = layer.name ?? $"Layer {layerIndex}",
                    NodeType = NavigationNodeType.Layer,
                    StateMachine = layer.NestedStateMachine,
                    LayerAsset = layer,
                    LayerIndex = layerIndex,
                    Depth = parentNode.Depth + 1,
                    Parent = parentNode
                };
                
                parentNode.Children.Add(layerNode);
                
                // Scan the layer's nested state machine for sub-state machines
                if (layer.NestedStateMachine != null)
                {
                    BuildSubStateMachineNodes(layerNode, layer.NestedStateMachine);
                }
                
                layerIndex++;
            }
        }
        
        /// <summary>
        /// Builds sub-state machine nodes recursively.
        /// </summary>
        private static void BuildSubStateMachineNodes(NavigationTreeNode parentNode, StateMachineAsset stateMachine)
        {
            if (stateMachine?.States == null) return;
            
            foreach (var state in stateMachine.States)
            {
                if (state is SubStateMachineStateAsset subStateMachine)
                {
                    var subNode = new NavigationTreeNode
                    {
                        Name = subStateMachine.name ?? "SubStateMachine",
                        NodeType = NavigationNodeType.SubStateMachine,
                        StateMachine = subStateMachine.NestedStateMachine,
                        SubStateMachineAsset = subStateMachine,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode
                    };
                    
                    parentNode.Children.Add(subNode);
                    
                    // Recursively scan nested state machine
                    if (subStateMachine.NestedStateMachine != null)
                    {
                        BuildSubStateMachineNodes(subNode, subStateMachine.NestedStateMachine);
                    }
                }
            }
        }
        
        /// <summary>
        /// Finds a node in the tree that matches the given state machine.
        /// </summary>
        public static NavigationTreeNode FindNode(NavigationTreeNode root, StateMachineAsset stateMachine)
        {
            if (root == null || stateMachine == null) return null;
            
            if (root.StateMachine == stateMachine) return root;
            
            foreach (var child in root.Children)
            {
                var found = FindNode(child, stateMachine);
                if (found != null) return found;
            }
            
            return null;
        }
        
        /// <summary>
        /// Finds a node in the tree that matches the given layer.
        /// </summary>
        public static NavigationTreeNode FindNode(NavigationTreeNode root, LayerStateAsset layer)
        {
            if (root == null || layer == null) return null;
            
            if (root.LayerAsset == layer) return root;
            
            foreach (var child in root.Children)
            {
                var found = FindNode(child, layer);
                if (found != null) return found;
            }
            
            return null;
        }
        
        /// <summary>
        /// Applies a search filter to the tree, updating MatchesFilter and HasMatchingDescendant.
        /// </summary>
        /// <param name="root">Root node to filter.</param>
        /// <param name="searchText">Search text (case-insensitive).</param>
        public static void ApplyFilter(NavigationTreeNode root, string searchText)
        {
            if (root == null) return;
            
            bool hasFilter = !string.IsNullOrWhiteSpace(searchText);
            string filterLower = hasFilter ? searchText.ToLowerInvariant() : null;
            
            ApplyFilterRecursive(root, filterLower, hasFilter);
        }
        
        private static bool ApplyFilterRecursive(NavigationTreeNode node, string filterLower, bool hasFilter)
        {
            // Check if this node matches
            if (hasFilter)
            {
                node.MatchesFilter = node.Name.ToLowerInvariant().Contains(filterLower);
            }
            else
            {
                node.MatchesFilter = true;
            }
            
            // Check children
            bool hasMatchingChild = false;
            foreach (var child in node.Children)
            {
                if (ApplyFilterRecursive(child, filterLower, hasFilter))
                {
                    hasMatchingChild = true;
                }
            }
            
            node.HasMatchingDescendant = hasMatchingChild;
            
            // Auto-expand nodes with matching descendants when filtering
            if (hasFilter && hasMatchingChild)
            {
                node.IsExpanded = true;
            }
            
            return node.MatchesFilter || hasMatchingChild;
        }
        
        /// <summary>
        /// Clears the filter, showing all nodes.
        /// </summary>
        public static void ClearFilter(NavigationTreeNode root)
        {
            ApplyFilter(root, null);
        }
        
        /// <summary>
        /// Gets all nodes in the tree as a flat list (pre-order traversal).
        /// </summary>
        public static List<NavigationTreeNode> GetAllNodes(NavigationTreeNode root)
        {
            var result = new List<NavigationTreeNode>();
            CollectNodes(root, result);
            return result;
        }
        
        private static void CollectNodes(NavigationTreeNode node, List<NavigationTreeNode> result)
        {
            if (node == null) return;
            
            result.Add(node);
            foreach (var child in node.Children)
            {
                CollectNodes(child, result);
            }
        }
        
        /// <summary>
        /// Gets all visible nodes (respecting filter and expansion).
        /// </summary>
        public static List<NavigationTreeNode> GetVisibleNodes(NavigationTreeNode root)
        {
            var result = new List<NavigationTreeNode>();
            CollectVisibleNodes(root, result);
            return result;
        }
        
        private static void CollectVisibleNodes(NavigationTreeNode node, List<NavigationTreeNode> result)
        {
            if (node == null || !node.IsVisibleInTree) return;
            
            result.Add(node);
            
            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                {
                    CollectVisibleNodes(child, result);
                }
            }
        }
    }
}
