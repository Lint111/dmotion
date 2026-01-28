using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Overlay for quick keyboard navigation through the state machine hierarchy.
    /// Shows a tree view of all navigable containers (Root, Layers, SubStateMachines).
    /// </summary>
    public class QuickNavigationOverlay : VisualElement
    {
        #region Constants
        
        private const string UssClassName = "quick-navigation-overlay";
        private const string ContainerClassName = "quick-nav-container";
        private const string HeaderClassName = "quick-nav-header";
        private const string TitleClassName = "quick-nav-title";
        private const string HintClassName = "quick-nav-hint";
        private const string SearchBarClassName = "quick-nav-search";
        private const string TreeContainerClassName = "quick-nav-tree";
        private const string NodeClassName = "quick-nav-node";
        private const string NodeSelectedClassName = "quick-nav-node--selected";
        private const string NodeMatchClassName = "quick-nav-node--match";
        private const string FooterClassName = "quick-nav-footer";
        private const string IconClassName = "quick-nav-icon";
        private const string LabelClassName = "quick-nav-label";
        private const string ExpandIconClassName = "quick-nav-expand";
        private const string TypeHintClassName = "quick-nav-type-hint";
        
        // USS path - try package path first, then local development path
        private const string UssPath = "Packages/com.gamedevpro.dmotion/Editor/StateMachineEditor/Navigation/QuickNavigation/QuickNavigationOverlay.uss";
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when user confirms navigation (Enter key).
        /// </summary>
        public event Action<NavigationTreeNode> OnNavigate;
        
        /// <summary>
        /// Fired when overlay is closed (Esc key or outside click).
        /// </summary>
        public event Action OnClosed;
        
        #endregion
        
        #region Fields
        
        private NavigationTreeNode rootNode;
        private NavigationTreeNode selectedNode;
        private NavigationTreeNode nodeBeforeSearch; // Track where user was when entering search
        private TextField searchField;
        private ScrollView treeScrollView;
        private VisualElement treeContainer;
        private Label footerLabel;
        private bool isSearchFocused;
        
        private readonly Dictionary<NavigationTreeNode, VisualElement> nodeToElement = new();
        
        #endregion
        
        #region Constructor
        
        public QuickNavigationOverlay()
        {
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }
            
            AddToClassList(UssClassName);
            
            // Create container
            var container = new VisualElement();
            container.AddToClassList(ContainerClassName);
            Add(container);
            
            // Header
            var header = new VisualElement();
            header.AddToClassList(HeaderClassName);
            
            var title = new Label("Quick Navigation");
            title.AddToClassList(TitleClassName);
            header.Add(title);
            
            var escHint = new Label("[Esc to close]");
            escHint.AddToClassList(HintClassName);
            header.Add(escHint);
            
            container.Add(header);
            
            // Search bar (initially hidden, shown when navigating up from first item)
            searchField = new TextField();
            searchField.AddToClassList(SearchBarClassName);
            searchField.style.display = DisplayStyle.None; // Initially hidden - toggled via code
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            // Use TrickleDown to capture key events before TextField processes them
            searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);
            searchField.RegisterCallback<FocusInEvent>(_ => isSearchFocused = true);
            searchField.RegisterCallback<FocusOutEvent>(_ => isSearchFocused = false);
            container.Add(searchField);
            
            // Tree scroll view
            treeScrollView = new ScrollView(ScrollViewMode.Vertical);
            treeScrollView.AddToClassList(TreeContainerClassName);
            container.Add(treeScrollView);
            
            treeContainer = new VisualElement();
            treeScrollView.Add(treeContainer);
            
            // Footer
            footerLabel = new Label("↑↓ Navigate  ←→ Collapse/Expand  Enter Select  Space Search");
            footerLabel.AddToClassList(FooterClassName);
            container.Add(footerLabel);
            
            // Capture keyboard events
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            
            // Close on background click
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == this)
                {
                    Close();
                }
            });
            
            focusable = true;
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Shows the overlay for the given state machine.
        /// </summary>
        /// <param name="stateMachine">Root state machine to navigate.</param>
        /// <param name="currentStateMachine">Currently viewed state machine (for initial selection).</param>
        public void Show(StateMachineAsset stateMachine, StateMachineAsset currentStateMachine = null)
        {
            rootNode = NavigationTreeBuilder.Build(stateMachine);
            if (rootNode == null)
            {
                Close();
                return;
            }
            
            // Build the tree UI
            RebuildTree();
            
            // Select the current location or root
            if (currentStateMachine != null)
            {
                var currentNode = NavigationTreeBuilder.FindNode(rootNode, currentStateMachine);
                if (currentNode != null)
                {
                    SelectNode(currentNode);
                }
                else
                {
                    SelectNode(rootNode);
                }
            }
            else
            {
                SelectNode(rootNode);
            }
            
            // Reset search
            searchField.value = "";
            searchField.style.display = DisplayStyle.None;
            isSearchFocused = false;
            
            style.display = DisplayStyle.Flex;
            Focus();
        }
        
        /// <summary>
        /// Closes the overlay.
        /// </summary>
        public void Close()
        {
            style.display = DisplayStyle.None;
            OnClosed?.Invoke();
        }
        
        /// <summary>
        /// Whether the overlay is currently visible.
        /// </summary>
        public bool IsVisible => style.display == DisplayStyle.Flex;
        
        #endregion
        
        #region Tree Building
        
        private void RebuildTree()
        {
            treeContainer.Clear();
            nodeToElement.Clear();
            
            if (rootNode == null) return;
            
            BuildNodeElements(rootNode, 0);
        }
        
        private void BuildNodeElements(NavigationTreeNode node, int depth)
        {
            if (!node.IsVisibleInTree) return;
            
            var nodeElement = CreateNodeElement(node, depth);
            treeContainer.Add(nodeElement);
            nodeToElement[node] = nodeElement;
            
            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                {
                    BuildNodeElements(child, depth + 1);
                }
            }
        }
        
        private VisualElement CreateNodeElement(NavigationTreeNode node, int depth)
        {
            var element = new VisualElement();
            element.AddToClassList(NodeClassName);
            element.style.marginLeft = depth * 20; // Indentation based on depth
            element.userData = node;
            
            // Expand/collapse icon (if has children)
            var expandIcon = new Label(node.HasChildren ? (node.IsExpanded ? "▼" : "▶") : "  ");
            expandIcon.AddToClassList(ExpandIconClassName);
            element.Add(expandIcon);
            
            // Type icon
            var icon = new Label(GetNodeIcon(node.NodeType));
            icon.AddToClassList(IconClassName);
            element.Add(icon);
            
            // Name label
            var label = new Label(node.Name);
            label.AddToClassList(LabelClassName);
            element.Add(label);
            
            // Apply match highlight via class
            if (node.MatchesFilter && !string.IsNullOrEmpty(searchField.value))
            {
                element.AddToClassList(NodeMatchClassName);
            }
            
            // Type hint
            var typeHint = new Label(GetNodeTypeHint(node));
            typeHint.AddToClassList(TypeHintClassName);
            element.Add(typeHint);
            
            // Click handler
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                SelectNode(node);
                if (evt.clickCount == 2)
                {
                    NavigateToSelected();
                }
            });
            
            return element;
        }
        
        private string GetNodeIcon(NavigationNodeType nodeType)
        {
            return nodeType switch
            {
                NavigationNodeType.Root => "◉",
                NavigationNodeType.Layer => "☰",
                NavigationNodeType.SubStateMachine => "◫",
                _ => "•"
            };
        }
        
        private string GetNodeTypeHint(NavigationTreeNode node)
        {
            return node.NodeType switch
            {
                NavigationNodeType.Root => node.StateMachine?.IsMultiLayer == true ? "Multi-Layer" : "Root",
                NavigationNodeType.Layer => $"Layer {node.LayerIndex}",
                NavigationNodeType.SubStateMachine => "SubState",
                _ => ""
            };
        }
        
        #endregion
        
        #region Selection
        
        private void SelectNode(NavigationTreeNode node)
        {
            if (node == null) return;
            
            // Deselect previous
            if (selectedNode != null && nodeToElement.TryGetValue(selectedNode, out var prevElement))
            {
                prevElement.RemoveFromClassList(NodeSelectedClassName);
            }
            
            selectedNode = node;
            selectedNode.IsSelected = true;
            
            // Select new
            if (nodeToElement.TryGetValue(selectedNode, out var element))
            {
                element.AddToClassList(NodeSelectedClassName);
                
                // Scroll into view
                treeScrollView.ScrollTo(element);
            }
        }
        
        private void NavigateToSelected()
        {
            if (selectedNode == null) return;
            
            OnNavigate?.Invoke(selectedNode);
            Close();
        }
        
        #endregion
        
        #region Keyboard Handling
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (isSearchFocused) return; // Let search field handle its own keys
            
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    NavigateToSelected();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.UpArrow:
                    NavigateUp();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.DownArrow:
                    NavigateDown();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.LeftArrow:
                    CollapseOrNavigateToParent();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.RightArrow:
                    ExpandOrNavigateToChild();
                    evt.StopPropagation();
                    break;
                    
                case KeyCode.Slash:
                case KeyCode.Question: // Shift+/ on US keyboards
                case KeyCode.Space: // Space also opens search
                    ShowSearchBar();
                    evt.StopImmediatePropagation();
                    break;
            }
        }
        
        private void NavigateUp()
        {
            if (selectedNode == null)
            {
                SelectNode(rootNode);
                return;
            }
            
            var prev = selectedNode.GetPreviousVisibleNode();
            if (prev != null)
            {
                SelectNode(prev);
            }
            else
            {
                // At top - show search bar
                ShowSearchBar();
            }
        }
        
        private void NavigateDown()
        {
            if (selectedNode == null)
            {
                SelectNode(rootNode);
                return;
            }
            
            var next = selectedNode.GetNextVisibleNode();
            if (next != null)
            {
                SelectNode(next);
            }
        }
        
        private void CollapseOrNavigateToParent()
        {
            if (selectedNode == null) return;
            
            if (selectedNode.IsExpanded && selectedNode.HasChildren)
            {
                // Collapse
                selectedNode.IsExpanded = false;
                RebuildTree();
                SelectNode(selectedNode);
            }
            else if (selectedNode.Parent != null)
            {
                // Navigate to parent
                SelectNode(selectedNode.Parent);
            }
        }
        
        private void ExpandOrNavigateToChild()
        {
            if (selectedNode == null) return;
            
            if (!selectedNode.IsExpanded && selectedNode.HasChildren)
            {
                // Expand
                selectedNode.IsExpanded = true;
                RebuildTree();
                SelectNode(selectedNode);
            }
            else if (selectedNode.FirstVisibleChild != null)
            {
                // Navigate to first child
                SelectNode(selectedNode.FirstVisibleChild);
            }
        }
        
        private void ShowSearchBar()
        {
            // Remember where we were before entering search
            nodeBeforeSearch = selectedNode;
            
            // Clear any existing text and show
            searchField.value = "";
            searchField.style.display = DisplayStyle.Flex;
            
            // Schedule focus for next frame to ensure field is ready
            searchField.schedule.Execute(() =>
            {
                searchField.Focus();
                // Clear again in case space was typed during the transition
                if (searchField.value == " ")
                {
                    searchField.value = "";
                }
            });
            
            isSearchFocused = true;
        }
        
        private void HideSearchBar(bool returnToPreviousNode = false)
        {
            searchField.style.display = DisplayStyle.None;
            searchField.value = "";
            isSearchFocused = false;
            NavigationTreeBuilder.ClearFilter(rootNode);
            RebuildTree();
            
            // Return to where we were before search, or stay at current selection
            if (returnToPreviousNode && nodeBeforeSearch != null)
            {
                SelectNode(nodeBeforeSearch);
            }
            else if (selectedNode != null)
            {
                // Re-select current node after tree rebuild
                SelectNode(selectedNode);
            }
            
            nodeBeforeSearch = null;
            Focus();
        }
        
        #endregion
        
        #region Search
        
        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            var searchText = evt.newValue?.Trim();
            
            NavigationTreeBuilder.ApplyFilter(rootNode, searchText);
            RebuildTree();
            
            // Select first matching node (only if we have actual search text)
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var visibleNodes = NavigationTreeBuilder.GetVisibleNodes(rootNode);
                var firstMatch = visibleNodes.Find(n => n.MatchesFilter);
                if (firstMatch != null)
                {
                    SelectNode(firstMatch);
                }
            }
            else
            {
                // No search text - keep current selection or select root
                if (selectedNode != null && nodeToElement.ContainsKey(selectedNode))
                {
                    SelectNode(selectedNode);
                }
            }
        }
        
        private void OnSearchKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    // Cancel search and return to where we were
                    HideSearchBar(returnToPreviousNode: true);
                    evt.StopImmediatePropagation();
                    break;
                    
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Navigate to the selected (possibly filtered) result
                    NavigateToSelected();
                    evt.StopImmediatePropagation();
                    break;
                    
                case KeyCode.DownArrow:
                    if (string.IsNullOrWhiteSpace(searchField.value))
                    {
                        // No search text - exit search and return to where we were
                        HideSearchBar(returnToPreviousNode: true);
                    }
                    else
                    {
                        // Has search text - navigate down through filtered results
                        NavigateDownInSearch();
                    }
                    evt.StopImmediatePropagation();
                    break;
                    
                case KeyCode.UpArrow:
                    if (!string.IsNullOrWhiteSpace(searchField.value))
                    {
                        // Navigate up through filtered results
                        NavigateUpInSearch();
                    }
                    evt.StopImmediatePropagation();
                    break;
            }
        }
        
        private void NavigateDownInSearch()
        {
            if (selectedNode == null) return;
            
            var next = selectedNode.GetNextVisibleNode();
            if (next != null)
            {
                SelectNode(next);
            }
        }
        
        private void NavigateUpInSearch()
        {
            if (selectedNode == null) return;
            
            var prev = selectedNode.GetPreviousVisibleNode();
            if (prev != null)
            {
                SelectNode(prev);
            }
            // If at top, stay in search bar (don't exit)
        }
        
        #endregion
    }
}
