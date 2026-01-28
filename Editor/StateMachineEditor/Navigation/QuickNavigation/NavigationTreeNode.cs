using System.Collections.Generic;
using DMotion.Authoring;

namespace DMotion.Editor
{
    /// <summary>
    /// Type of navigation node in the quick navigation tree.
    /// </summary>
    public enum NavigationNodeType
    {
        /// <summary>Root state machine.</summary>
        Root,
        
        /// <summary>Layer in a multi-layer state machine.</summary>
        Layer,
        
        /// <summary>Sub-state machine container.</summary>
        SubStateMachine
    }
    
    /// <summary>
    /// Represents a node in the quick navigation tree.
    /// Only contains navigable containers (Root, Layers, SubStateMachines).
    /// </summary>
    public class NavigationTreeNode
    {
        #region Properties 
        
        /// <summary>
        /// Display name for the node.
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Type of navigation node.
        /// </summary>
        public NavigationNodeType NodeType { get; set; }
        
        /// <summary>
        /// The state machine this node represents or contains.
        /// For Root: the root state machine
        /// For Layer: the layer's nested state machine
        /// For SubStateMachine: the sub-state machine's nested state machine
        /// </summary>
        public StateMachineAsset StateMachine { get; set; }
        
        /// <summary>
        /// Layer asset (only for Layer nodes).
        /// </summary>
        public LayerStateAsset LayerAsset { get; set; }
        
        /// <summary>
        /// Sub-state machine asset (only for SubStateMachine nodes).
        /// </summary>
        public SubStateMachineStateAsset SubStateMachineAsset { get; set; }
        
        /// <summary>
        /// Index of layer (only for Layer nodes).
        /// </summary>
        public int LayerIndex { get; set; } = -1;
        
        /// <summary>
        /// Depth in the tree (0 = root).
        /// </summary>
        public int Depth { get; set; }
        
        /// <summary>
        /// Parent node (null for root).
        /// </summary>
        public NavigationTreeNode Parent { get; set; }
        
        /// <summary>
        /// Child nodes (layers or sub-state machines).
        /// </summary>
        public List<NavigationTreeNode> Children { get; } = new();
        
        /// <summary>
        /// Whether this node is currently selected in the UI.
        /// </summary>
        public bool IsSelected { get; set; }
        
        /// <summary>
        /// Whether this node's children are visible.
        /// </summary>
        public bool IsExpanded { get; set; } = true;
        
        /// <summary>
        /// Whether this node matches the current search filter.
        /// </summary>
        public bool MatchesFilter { get; set; } = true;
        
        /// <summary>
        /// Whether this node or any descendant matches the filter.
        /// </summary>
        public bool HasMatchingDescendant { get; set; } = true;
        
        #endregion
        
        #region Computed Properties
        
        /// <summary>
        /// Whether this node has children.
        /// </summary>
        public bool HasChildren => Children.Count > 0;
        
        /// <summary>
        /// Whether this node is the root.
        /// </summary>
        public bool IsRoot => Parent == null;
        
        /// <summary>
        /// Index of this node among its siblings.
        /// </summary>
        public int SiblingIndex
        {
            get
            {
                if (Parent == null) return 0;
                return Parent.Children.IndexOf(this);
            }
        }
        
        /// <summary>
        /// Number of siblings (including self).
        /// </summary>
        public int SiblingCount => Parent?.Children.Count ?? 1;
        
        /// <summary>
        /// Previous sibling (or null if first).
        /// </summary>
        public NavigationTreeNode PreviousSibling
        {
            get
            {
                if (Parent == null) return null;
                int index = SiblingIndex;
                return index > 0 ? Parent.Children[index - 1] : null;
            }
        }
        
        /// <summary>
        /// Next sibling (or null if last).
        /// </summary>
        public NavigationTreeNode NextSibling
        {
            get
            {
                if (Parent == null) return null;
                int index = SiblingIndex;
                return index < Parent.Children.Count - 1 ? Parent.Children[index + 1] : null;
            }
        }
        
        /// <summary>
        /// First visible child (respecting filter).
        /// </summary>
        public NavigationTreeNode FirstVisibleChild
        {
            get
            {
                if (!IsExpanded) return null;
                foreach (var child in Children)
                {
                    if (child.IsVisibleInTree) return child;
                }
                return null;
            }
        }
        
        /// <summary>
        /// Last visible child (respecting filter).
        /// </summary>
        public NavigationTreeNode LastVisibleChild
        {
            get
            {
                if (!IsExpanded) return null;
                for (int i = Children.Count - 1; i >= 0; i--)
                {
                    if (Children[i].IsVisibleInTree) return Children[i];
                }
                return null;
            }
        }
        
        /// <summary>
        /// Whether this node should be visible in the tree (matches filter or has matching descendant).
        /// </summary>
        public bool IsVisibleInTree => MatchesFilter || HasMatchingDescendant;
        
        #endregion
        
        #region Navigation Helpers
        
        /// <summary>
        /// Gets the full path to this node (e.g., "Root/Layer 0/Combat").
        /// </summary>
        public string GetPath()
        {
            if (Parent == null) return Name;
            return Parent.GetPath() + "/" + Name;
        }
        
        /// <summary>
        /// Finds the next visible node in tree traversal order (down).
        /// </summary>
        public NavigationTreeNode GetNextVisibleNode()
        {
            // First try children
            var firstChild = FirstVisibleChild;
            if (firstChild != null) return firstChild;
            
            // Then try next sibling
            var next = GetNextVisibleSibling();
            if (next != null) return next;
            
            // Then go up and try parent's next sibling
            var ancestor = Parent;
            while (ancestor != null)
            {
                var ancestorNext = ancestor.GetNextVisibleSibling();
                if (ancestorNext != null) return ancestorNext;
                ancestor = ancestor.Parent;
            }
            
            return null;
        }
        
        /// <summary>
        /// Finds the previous visible node in tree traversal order (up).
        /// </summary>
        public NavigationTreeNode GetPreviousVisibleNode()
        {
            // Try previous sibling's last descendant
            var prev = GetPreviousVisibleSibling();
            if (prev != null)
            {
                return prev.GetLastVisibleDescendant();
            }
            
            // Otherwise, parent
            return Parent;
        }
        
        private NavigationTreeNode GetNextVisibleSibling()
        {
            if (Parent == null) return null;
            int index = SiblingIndex;
            for (int i = index + 1; i < Parent.Children.Count; i++)
            {
                if (Parent.Children[i].IsVisibleInTree)
                    return Parent.Children[i];
            }
            return null;
        }
        
        private NavigationTreeNode GetPreviousVisibleSibling()
        {
            if (Parent == null) return null;
            int index = SiblingIndex;
            for (int i = index - 1; i >= 0; i--)
            {
                if (Parent.Children[i].IsVisibleInTree)
                    return Parent.Children[i];
            }
            return null;
        }
        
        private NavigationTreeNode GetLastVisibleDescendant()
        {
            var lastChild = LastVisibleChild;
            if (lastChild == null) return this;
            return lastChild.GetLastVisibleDescendant();
        }
        
        #endregion
    }
}
