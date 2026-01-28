using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal struct TransitionPair : IEquatable<TransitionPair>
    {
        internal AnimationStateAsset FromState;
        internal AnimationStateAsset ToState;

        public override int GetHashCode()
        {
            unchecked
            {
                // Use proper hash combining to avoid collisions
                int hash = 17;
                hash = hash * 31 + (FromState != null ? FromState.GetHashCode() : 0);
                hash = hash * 31 + (ToState != null ? ToState.GetHashCode() : 0);
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            return obj is TransitionPair other && Equals(other);
        }

        public bool Equals(TransitionPair other)
        {
            return FromState == other.FromState && ToState == other.ToState;
        }

        internal TransitionPair(AnimationStateAsset from, AnimationStateAsset to)
        {
            FromState = from;
            ToState = to;
        }

        internal TransitionPair(AnimationStateAsset state, int outTransitionIndex)
        {
            var outTransition = state.OutTransitions[outTransitionIndex];
            FromState = state;
            ToState = outTransition.ToState;
        }
    }

    [UxmlElement]
    public partial class AnimationStateMachineEditorView : GraphView
    {

        private StateMachineEditorViewModel model;

        private readonly Dictionary<AnimationStateAsset, StateNodeView> stateToView = new();

        private readonly Dictionary<TransitionPair, TransitionEdge> transitionToEdgeView = new();

        // NEW: Any State node and transitions
        private AnyStateNodeView anyStateNodeView;
        private readonly Dictionary<AnimationStateAsset, TransitionEdge> anyStateTransitionEdges = new();
        // Exit node for nested state machines
        private ExitNodeView exitNodeView;
        
        // Exit transitions from SubStateMachines
        private readonly Dictionary<TransitionPair, TransitionEdge> exitTransitionEdges = new();
        
        // Layer nodes (multi-layer mode only)
        private readonly Dictionary<LayerStateAsset, LayerStateNodeView> layerToView = new();
        
        // Cached list for deletion operations (avoids allocation per delete)
        private readonly List<GraphElement> _deleteElementsCache = new();

        // Search window for creating new states (opened with Space)
        private StateSearchWindowProvider searchWindowProvider;
        
        /// <summary>
        /// Whether current view is showing multi-layer root (layer nodes only, no states).
        /// </summary>
        internal bool IsMultiLayerRoot => model.StateMachineAsset?.IsMultiLayer == true;

        internal StateMachineAsset StateMachine => model.StateMachineAsset;
        internal VisualTreeAsset StateNodeXml => model.StateNodeXml;

        public AnimationStateMachineEditorView()
        {
            var gridBg = new GridBackground();
            gridBg.name = "sm-grid-bg";
            Insert(0, gridBg);
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new TransitionCutManipulator(this));
            
            // Initialize search window provider for Space shortcut
            searchWindowProvider = ScriptableObject.CreateInstance<StateSearchWindowProvider>();
            searchWindowProvider.Initialize(this);
            
            // Centralized keyboard handling for all nodes
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            
            // Clear inspector when clicking on empty space (background)
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            
            // Prevent deletion of special nodes (Any State, Exit)
            deleteSelection = OnDeleteSelection;
        }
        
        private void OnDeleteSelection(string operationName, AskUser askUser)
        {
            // Filter selection to exclude non-deletable special nodes
            _deleteElementsCache.Clear();
            foreach (var item in selection)
            {
                if (item is not GraphElement element) continue;
                if (element is AnyStateNodeView or ExitNodeView) continue;

                _deleteElementsCache.Add(element);

                // Also collect edges connected to deleted nodes
                if (element is Node node)
                {
                    CollectConnectedEdges(node, _deleteElementsCache);
                }
            }

            if (_deleteElementsCache.Count > 0)
            {
                DeleteElements(_deleteElementsCache);
            }
        }

        private void CollectConnectedEdges(Node node, List<GraphElement> elementsToDelete)
        {
            CollectEdgesFromPorts(node.inputContainer, elementsToDelete);
            CollectEdgesFromPorts(node.outputContainer, elementsToDelete);
        }

        private static void CollectEdgesFromPorts(VisualElement container, List<GraphElement> elementsToDelete)
        {
            foreach (var child in container.Children())
            {
                if (child is not Port port) continue;

                foreach (var edge in port.connections)
                {
                    if (!elementsToDelete.Contains(edge))
                        elementsToDelete.Add(edge);
                }
            }
        }
        
        // Navigation constants
        private const float PanStep = 50f;
        private const float ZoomStep = 0.1f;
        private const float MinZoom = 0.1f;
        private const float MaxZoom = 2f;
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (Application.isPlaying) return;
            
            // Ctrl+Shift+Space or Ctrl+Shift+N to toggle quick navigation
            if (evt.ctrlKey && evt.shiftKey && (evt.keyCode == KeyCode.Space || evt.keyCode == KeyCode.N))
            {
                ToggleQuickNavigation();
                evt.StopImmediatePropagation();
                return;
            }
            
            // Ctrl+Number to jump to layer (1-9)
            if (evt.ctrlKey && !evt.shiftKey && !evt.altKey && TryHandleLayerShortcut(evt.keyCode))
            {
                evt.StopImmediatePropagation();
                return;
            }
            
            // Space to open state creation search window (only without modifiers)
            if (evt.keyCode == KeyCode.Space && !evt.ctrlKey && !evt.shiftKey && model.StateMachineAsset != null)
            {
                OpenStateSearchWindow();
                evt.StopImmediatePropagation();
                return;
            }
            
            // Shift+Arrow keys to pan
            if (evt.shiftKey && !evt.ctrlKey && HandlePanKeys(evt.keyCode))
            {
                evt.StopImmediatePropagation();
                return;
            }
            
            // Shift+=/- to zoom
            if (evt.shiftKey && !evt.ctrlKey && HandleZoomKeys(evt.keyCode))
            {
                evt.StopImmediatePropagation();
                return;
            }
            
            // Shift+F to focus selection or reset view
            if (evt.shiftKey && !evt.ctrlKey && evt.keyCode == KeyCode.F)
            {
                FocusSelectionOrReset();
                evt.StopImmediatePropagation();
                return;
            }
            
            // F2 or Ctrl+R to rename selected node
            bool isRenameShortcut = evt.keyCode == KeyCode.F2 || 
                                    (evt.keyCode == KeyCode.R && evt.ctrlKey);
            
            if (isRenameShortcut)
            {
                HandleRenameShortcut();
                evt.StopImmediatePropagation();
            }
        }
        
        /// <summary>
        /// Tries to handle Ctrl+Number shortcut to jump to a layer.
        /// </summary>
        private bool TryHandleLayerShortcut(KeyCode keyCode)
        {
            // Map KeyCode to layer index (Ctrl+1 = Layer 0, Ctrl+2 = Layer 1, etc.)
            int layerIndex = keyCode switch
            {
                KeyCode.Alpha1 or KeyCode.Keypad1 => 0,
                KeyCode.Alpha2 or KeyCode.Keypad2 => 1,
                KeyCode.Alpha3 or KeyCode.Keypad3 => 2,
                KeyCode.Alpha4 or KeyCode.Keypad4 => 3,
                KeyCode.Alpha5 or KeyCode.Keypad5 => 4,
                KeyCode.Alpha6 or KeyCode.Keypad6 => 5,
                KeyCode.Alpha7 or KeyCode.Keypad7 => 6,
                KeyCode.Alpha8 or KeyCode.Keypad8 => 7,
                KeyCode.Alpha9 or KeyCode.Keypad9 => 8,
                KeyCode.Alpha0 or KeyCode.Keypad0 => 9, // Ctrl+0 = Layer 9 (10th layer)
                _ => -1
            };
            
            if (layerIndex < 0) return false;
            
            // Get root state machine
            var rootMachine = EditorState.Instance.RootStateMachine;
            if (rootMachine == null || !rootMachine.IsMultiLayer) return false;
            
            // Find layer at index
            int currentIndex = 0;
            foreach (var layer in rootMachine.GetLayers())
            {
                if (currentIndex == layerIndex)
                {
                    // Navigate to this layer
                    EditorState.Instance.EnterLayer(layer, layerIndex);
                    OnLayerNavigationRequested?.Invoke(layer, layerIndex);
                    return true;
                }
                currentIndex++;
            }
            
            return false; // Layer index out of range
        }
        
        /// <summary>
        /// Toggles the quick navigation overlay via the parent window.
        /// </summary>
        private void ToggleQuickNavigation()
        {
            // Find parent window and toggle its overlay
            var window = EditorWindow.GetWindow<AnimationStateMachineEditorWindow>();
            window?.ToggleQuickNavigationOverlay();
        }
        
        /// <summary>
        /// Event fired when layer navigation is requested via keyboard shortcut.
        /// </summary>
        internal Action<LayerStateAsset, int> OnLayerNavigationRequested;
        
        // GraphView.viewTransform.position/scale are marked obsolete but there's no alternative API
        // for reading the current view transform in GraphView. Suppress these warnings.
#pragma warning disable CS0618
        
        private bool HandlePanKeys(KeyCode keyCode)
        {
            Vector3 offset = Vector3.zero;
            
            switch (keyCode)
            {
                case KeyCode.LeftArrow:
                    offset = new Vector3(PanStep, 0, 0);
                    break;
                case KeyCode.RightArrow:
                    offset = new Vector3(-PanStep, 0, 0);
                    break;
                case KeyCode.UpArrow:
                    offset = new Vector3(0, PanStep, 0);
                    break;
                case KeyCode.DownArrow:
                    offset = new Vector3(0, -PanStep, 0);
                    break;
                default:
                    return false;
            }
            
            var currentPos = viewTransform.position;
            UpdateViewTransform(currentPos + offset, viewTransform.scale);
            return true;
        }
        
        private bool HandleZoomKeys(KeyCode keyCode)
        {
            float zoomDelta = 0f;
            
            switch (keyCode)
            {
                case KeyCode.Equals: // + key (=/+)
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                    zoomDelta = ZoomStep;
                    break;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    zoomDelta = -ZoomStep;
                    break;
                default:
                    return false;
            }
            
            // Calculate new scale, clamped
            var currentScale = viewTransform.scale.x;
            var newScale = Mathf.Clamp(currentScale + zoomDelta, MinZoom, MaxZoom);
            
            // Zoom towards center of view
            var center = new Vector2(layout.width / 2, layout.height / 2);
            var worldCenter = contentViewContainer.WorldToLocal(this.LocalToWorld(center));
            
            var currentPos = viewTransform.position;
            UpdateViewTransform(currentPos, new Vector3(newScale, newScale, 1f));
            
            // Adjust position to keep center stable
            var newWorldCenter = contentViewContainer.WorldToLocal(this.LocalToWorld(center));
            var delta = (newWorldCenter - worldCenter) * newScale;
            UpdateViewTransform(currentPos + new Vector3(delta.x, delta.y, 0), new Vector3(newScale, newScale, 1f));
            
            return true;
        }
        
        private void FocusSelectionOrReset()
        {
            if (selection.Count > 0)
            {
                // Focus on selected elements
                FrameSelection();
            }
            else
            {
                // Reset to default view - center on origin
                var currentScale = viewTransform.scale;
                UpdateViewTransform(new Vector3(layout.width / 2, layout.height / 2, 0), currentScale);
            }
        }
        
        /// <summary>
        /// Centers the view on a specific state, placing it in the middle of the viewport.
        /// </summary>
        internal void CenterOnState(AnimationStateAsset state)
        {
            if (state == null) return;

            var stateView = GetViewForState(state);
            if (stateView == null) return;

            CenterOnNode(stateView);
        }

        /// <summary>
        /// Centers the view on a specific layer, placing it in the middle of the viewport.
        /// </summary>
        internal void CenterOnLayer(LayerStateAsset layer)
        {
            if (layer == null) return;

            if (!layerToView.TryGetValue(layer, out var layerView)) return;

            CenterOnNode(layerView);
        }

        /// <summary>
        /// Centers the view on a GraphElement node.
        /// </summary>
        private void CenterOnNode(Node node)
        {
            if (node == null) return;

            // Get the node's position in content space
            var nodeRect = node.GetPosition();
            var nodeCenter = nodeRect.center;

            // Calculate the view center in content space
            var viewportCenter = new Vector2(layout.width / 2f, layout.height / 2f);

            // Current scale (use x since we scale uniformly)
            var currentScale = viewTransform.scale.x;

            // Calculate the position offset needed to center the node
            // Position moves the content, so we need to offset by the difference
            // between where we want the node (viewport center) and where it is (node center)
            var newPosition = new Vector3(
                viewportCenter.x - (nodeCenter.x * currentScale),
                viewportCenter.y - (nodeCenter.y * currentScale),
                0f
            );

            UpdateViewTransform(newPosition, viewTransform.scale);
        }

#pragma warning restore CS0618

        private void HandleRenameShortcut()
        {
            foreach (var item in selection)
            {
                // State nodes
                if (item is StateNodeView stateNode)
                {
                    stateNode.StartRename();
                    return;
                }
                
                // Layer nodes
                if (item is LayerStateNodeView layerNode)
                {
                    layerNode.StartRename();
                    return;
                }
            }
        }
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            // Only handle left-click on background (not on nodes/edges)
            if (evt.button != 0) return;
            if (model.StateMachineAsset == null) return;
            
            // Check if click target is the graph background (GridBackground or this GraphView)
            var target = evt.target as VisualElement;
            if (target == null) return;
            
            // If clicked on a node, edge, or port, don't clear selection
            // The target will be the GridBackground or the GraphView itself for empty space clicks
            bool isBackgroundClick = target is GridBackground || 
                                     target == this || 
                                     target == contentViewContainer;
            
            if (isBackgroundClick)
            {
                // Use schedule to allow the default selection behavior to complete first
                schedule.Execute(() =>
                {
                    // Only clear if nothing is selected after the click
                    if (selection.Count == 0)
                    {
                        EditorState.Instance.ClearSelection();
                    }
                });
            }
        }

        private void OpenStateSearchWindow()
        {
            // Capture mouse position in graph coordinates NOW (before search window opens)
            var mousePosition = Event.current.mousePosition;
            var graphPosition = contentViewContainer.WorldToLocal(mousePosition);
            
            // Check if mouse is within the graph view bounds
            var localMousePos = this.WorldToLocal(mousePosition);
            bool isMouseOverGraph = this.ContainsPoint(localMousePos);
            
            if (!isMouseOverGraph)
            {
                // Mouse not over graph - use center of visible area
                graphPosition = contentViewContainer.WorldToLocal(this.worldBound.center);
            }
            
            // Store position for when user selects an entry
            searchWindowProvider.SetCreationPosition(graphPosition);
            
            // Open search window at mouse screen position
            var screenMousePosition = GUIUtility.GUIToScreenPoint(mousePosition);
            SearchWindow.Open(new SearchWindowContext(screenMousePosition), searchWindowProvider);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (model.StateMachineAsset == null)
            {
                evt.StopPropagation();
                return;
            }
            
            var status = Application.isPlaying
                ? DropdownMenuAction.Status.Disabled
                : DropdownMenuAction.Status.Normal;

            // Context-aware menu based on whether we're at multi-layer root or inside a layer
            if (IsMultiLayerRoot)
            {
                // Multi-layer root: only allow adding layers
                evt.menu.AppendAction("Add Layer", a => CreateLayer(a), status);
            }
            else
            {
                // Inside layer or single-layer: normal state creation options
                evt.menu.AppendAction("New State", a => CreateState(a, typeof(SingleClipStateAsset)), status);
                evt.menu.AppendAction("New Blend Tree 1D", a => CreateState(a, typeof(LinearBlendStateAsset)), status);
                evt.menu.AppendAction("New Blend Tree 2D", a => CreateState(a, typeof(Directional2DBlendStateAsset)), status);
                evt.menu.AppendAction("New Sub-State Machine", a => CreateState(a, typeof(SubStateMachineStateAsset)), status);
            }

            evt.StopPropagation();
        }
        
        /// <summary>
        /// Creates a new layer at the context menu position (multi-layer root only).
        /// </summary>
        private void CreateLayer(DropdownMenuAction action)
        {
            var graphPos = contentViewContainer.WorldToLocal(action.eventInfo.mousePosition);
            
            Undo.RecordObject(model.StateMachineAsset, "Add Layer");
            var layer = model.StateMachineAsset.AddLayer();
            
            // Set position for the new layer
            layer.StateEditorData.GraphPosition = graphPos;
            EditorUtility.SetDirty(layer);
            
            // Notify state change
            EditorState.Instance.NotifyLayerAdded(layer);
            
            // Refresh the graph view
            RefreshAfterLayerChange();
        }
        
        /// <summary>
        /// Refreshes the graph view after a layer is added or removed.
        /// </summary>
        internal void RefreshAfterLayerChange()
        {
            // Re-populate the view to show the new layer
            PopulateView(model);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphviewchange)
        {
            if (Application.isPlaying)
                return graphviewchange;

            ProcessElementsToRemove(graphviewchange.elementsToRemove);
            ProcessEdgesToCreate(graphviewchange.edgesToCreate);

            return graphviewchange;
        }

        private void ProcessElementsToRemove(List<GraphElement> elementsToRemove)
        {
            if (elementsToRemove == null) return;

            foreach (var el in elementsToRemove)
            {
                if (el is StateNodeView stateView)
                {
                    DeleteState(stateView.State);
                    continue;
                }

                if (el is not TransitionEdge transition) continue;

                ProcessTransitionRemoval(transition);
            }
        }

        private void ProcessTransitionRemoval(TransitionEdge transition)
        {
            var outputNode = transition.output.node;
            var inputNode = transition.input.node;

            // State → State
            if (outputNode is StateNodeView from && inputNode is StateNodeView to)
            {
                DeleteAllOutTransitions(from.State, to.State);
                return;
            }

            // Any State → State
            if (outputNode is AnyStateNodeView && inputNode is StateNodeView toState)
            {
                DeleteAnyStateTransition(toState.State);
                return;
            }

            // State → Exit
            if (outputNode is StateNodeView exitFromState && inputNode is ExitNodeView)
            {
                RemoveExitState(exitFromState.State);
                return;
            }

            // Any State → Exit
            if (outputNode is AnyStateNodeView && inputNode is ExitNodeView)
            {
                RemoveAnyStateExitTransition();
                anyStateExitEdge = null;
            }
        }

        private void ProcessEdgesToCreate(List<Edge> edgesToCreate)
        {
            if (edgesToCreate == null) return;

            foreach (var edge in edgesToCreate)
            {
                if (edge is not TransitionEdge) continue;

                ProcessEdgeCreation(edge);
            }

            edgesToCreate.Clear();
        }

        private void ProcessEdgeCreation(Edge edge)
        {
            var outputNode = edge.output.node;
            var inputNode = edge.input.node;

            // State → State
            if (outputNode is StateNodeView fromStateView && inputNode is StateNodeView toStateView)
            {
                CreateOutTransition(fromStateView.State, toStateView.State);
                return;
            }

            // Any State → State
            if (outputNode is AnyStateNodeView && inputNode is StateNodeView anyStateTarget)
            {
                CreateAnyStateTransition(anyStateTarget.State);
                return;
            }

            // State → Exit
            if (outputNode is StateNodeView exitFromState && inputNode is ExitNodeView)
            {
                AddExitState(exitFromState.State);
            }
        }

        private void DeleteState(AnimationStateAsset state)
        {
            // Clean up visual dictionaries before deleting data
            stateToView.Remove(state);
            
            // Remove transition edges involving this state
            var pairsToRemove = ListPool<TransitionPair>.Get();
            foreach (var pair in transitionToEdgeView.Keys)
            {
                if (pair.FromState == state || pair.ToState == state)
                    pairsToRemove.Add(pair);
            }
            for (int i = 0; i < pairsToRemove.Count; i++)
            {
                transitionToEdgeView.Remove(pairsToRemove[i]);
            }
            ListPool<TransitionPair>.Return(pairsToRemove);
            
            // Remove any state transition edge to this state
            anyStateTransitionEdges.Remove(state);
            
            // Delete from data model
            model.StateMachineAsset.DeleteState(state);
            
            // Notify state change
            EditorState.Instance.NotifyStateRemoved(state);
        }

        private void DeleteAllOutTransitions(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            for (int i = fromState.OutTransitions.Count - 1; i >= 0; i--)
            {
                if (fromState.OutTransitions[i].ToState == toState)
                    fromState.OutTransitions.RemoveAt(i);
            }
            transitionToEdgeView.Remove(new TransitionPair(fromState, toState));
            
            EditorState.Instance.NotifyTransitionRemoved(fromState, toState);
        }

        private void CreateState(DropdownMenuAction action, Type stateType)
        {
            var graphPos = contentViewContainer.WorldToLocal(action.eventInfo.mousePosition);
            
            // SubStateMachine uses a popup for configuration
            if (stateType == typeof(SubStateMachineStateAsset))
            {
                SubStateMachineCreationPopup.Show(model.StateMachineAsset, graphPos, OnSubStateMachineCreated);
                return;
            }
            
            var state = model.StateMachineAsset.CreateState(stateType);
            state.StateEditorData.GraphPosition = graphPos;
            InstantiateStateView(state);
            
            // Notify state change
            EditorState.Instance.NotifyStateAdded(state);
        }

        /// <summary>
        /// Creates a state at a specific graph position. Called by StateSearchWindowProvider.
        /// </summary>
        internal void CreateStateAtPosition(Type stateType, Vector2 graphPosition)
        {
            // SubStateMachine uses a popup for configuration
            if (stateType == typeof(SubStateMachineStateAsset))
            {
                SubStateMachineCreationPopup.Show(model.StateMachineAsset, graphPosition, OnSubStateMachineCreated);
                return;
            }
            
            var state = model.StateMachineAsset.CreateState(stateType);
            state.StateEditorData.GraphPosition = graphPosition;
            InstantiateStateView(state);
            
            // Notify state change
            EditorState.Instance.NotifyStateAdded(state);
        }

        private void OnSubStateMachineCreated(SubStateMachineStateAsset subState)
        {
            if (subState == null) return;
            
            InstantiateStateView(subState);
            
            // Notify state change
            EditorState.Instance.NotifyStateAdded(subState);
        }

        private void CreateOutTransition(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            var outTransition = new StateOutTransition(toState);
            fromState.OutTransitions.Add(outTransition);
            InstantiateTransitionEdge(fromState, fromState.OutTransitions.Count - 1);
        }

        /// <summary>
        /// Creates a transition between two states. Called by TransitionDragManipulator.
        /// </summary>
        internal void CreateTransitionBetweenStates(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            if (fromState == null || toState == null) return;
            // Self-transitions are allowed (e.g., re-trigger attack, reset idle)
            
            Undo.RecordObject(fromState, "Create Transition");
            CreateOutTransition(fromState, toState);
            EditorUtility.SetDirty(fromState);
            
            EditorState.Instance.NotifyTransitionAdded(fromState, toState);
        }

        // Cached list for GetCompatiblePorts to avoid per-call allocations
        private List<Port> _compatiblePortsCache = new List<Port>();
        
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            _compatiblePortsCache.Clear();
            foreach (var port in ports)
            {
                if (port.direction != startPort.direction && port.node != startPort.node)
                {
                    _compatiblePortsCache.Add(port);
                }
            }
            return _compatiblePortsCache;
        }

        internal void UpdateView()
        {
            // Update state views
            foreach (var stateView in stateToView.Values)
            {
                stateView.UpdateView();
            }

            // Update transition views
            foreach (var transitions in transitionToEdgeView.Values)
            {
                transitions.UpdateView();
            }
            
            // Update layer views (multi-layer root)
            foreach (var layerView in layerToView.Values)
            {
                layerView.UpdateView();
            }
        }

        internal void PopulateView(in StateMachineEditorViewModel newModel)
        {
            model = newModel;
            stateToView.Clear();
            transitionToEdgeView.Clear();
            anyStateTransitionEdges.Clear();
            exitTransitionEdges.Clear();
            layerToView.Clear();
            anyStateExitEdge = null;

            graphViewChanged -= OnGraphViewChanged;
            // Collect elements to delete without LINQ
            var elementsToDelete = ListPool<GraphElement>.Get();
            foreach (var element in graphElements)
            {
                elementsToDelete.Add(element);
            }
            DeleteElements(elementsToDelete);
            ListPool<GraphElement>.Return(elementsToDelete);
            graphViewChanged += OnGraphViewChanged;

            // Multi-layer root: show only layer nodes, no Any State, no Exit, no transitions
            if (IsMultiLayerRoot)
            {
                PopulateMultiLayerRoot();
                return;
            }

            // Single-layer or inside a layer: normal state machine view
            PopulateSingleLayerView();
        }
        
        /// <summary>
        /// Populates the view for multi-layer root - shows layer nodes only.
        /// No Any State, no Exit, no transitions between layers.
        /// </summary>
        private void PopulateMultiLayerRoot()
        {
            foreach (var state in model.StateMachineAsset.States)
            {
                if (state is LayerStateAsset layer)
                {
                    InstantiateLayerView(layer);
                }
            }
            
            // No transitions at multi-layer root - layers run in parallel
        }
        
        /// <summary>
        /// Populates the view for single-layer or inside a layer's state machine.
        /// Shows Any State, Exit, states, and transitions.
        /// </summary>
        private void PopulateSingleLayerView()
        {
            // Create Any State node (always present in single-layer/layer content)
            InstantiateAnyStateNode();
            
            // Create Exit node (always present - for nested state machine support)
            InstantiateExitNode();

            foreach (var s in model.StateMachineAsset.States)
            {
                // Skip LayerStateAsset in single-layer view (shouldn't happen, but defensive)
                if (s is LayerStateAsset) continue;
                InstantiateStateView(s);
            }

            foreach (var t in model.StateMachineAsset.States)
            {
                if (t is LayerStateAsset) continue;
                for (var i = 0; i < t.OutTransitions.Count; i++)
                {
                    InstantiateTransitionEdge(t, i);
                }
            }

            // Create Any State transition edges
            foreach (var anyTransition in model.StateMachineAsset.AnyStateTransitions)
            {
                InstantiateAnyStateTransitionEdge(anyTransition);
            }
            
            // Create Any State -> Exit edge if exists
            if (model.StateMachineAsset.HasAnyStateExitTransition)
            {
                InstantiateAnyStateExitEdge();
            }

            // Create exit transition edges for SubStateMachines
            foreach (var state in model.StateMachineAsset.States)
            {
                if (state is SubStateMachineStateAsset subState)
                {
                    InstantiateSubStateMachineExitTransitions(subState);
                }
            }
            
            // Create exit state edges (states that connect to Exit node)
            foreach (var exitState in model.StateMachineAsset.ExitStates)
            {
                InstantiateExitStateEdge(exitState);
            }
        }
        
        /// <summary>
        /// Creates a visual node for a layer asset.
        /// </summary>
        private void InstantiateLayerView(LayerStateAsset layer)
        {
            var layerView = LayerStateNodeView.Create(layer, this);
            AddElement(layerView);
            layerToView.Add(layer, layerView);
        }

        internal StateNodeView GetViewForState(AnimationStateAsset state)
        {
            return state == null ? null : stateToView.TryGetValue(state, out var view) ? view : null;
        }

        /// <summary>
        /// Creates a visual transition edge for a state. Called by StateNodeView when creating transitions.
        /// </summary>
        internal void CreateTransitionEdgeForState(AnimationStateAsset state, int outTransitionIndex)
        {
            InstantiateTransitionEdge(state, outTransitionIndex);
        }

        private void InstantiateTransitionEdge(AnimationStateAsset state, int outTransitionIndex)
        {
            var transitionPair = new TransitionPair(state, outTransitionIndex);
            if (transitionToEdgeView.TryGetValue(transitionPair, out var existingEdge))
            {
                existingEdge.Model.TransitionCount++;
                existingEdge.MarkDirtyRepaint();
            }
            else
            {
                var fromStateView = GetViewForState(transitionPair.FromState);
                var toStateView = GetViewForState(transitionPair.ToState);
                var edge = fromStateView.output.ConnectTo<TransitionEdge>(toStateView.input);
                edge.Model = new TransitionEdgeModel()
                {
                    TransitionCount = 1,
                    StateMachineAsset = model.StateMachineAsset,
                    SelectedEntity = model.SelectedEntity
                };
                AddElement(edge);
                transitionToEdgeView.Add(transitionPair, edge);

                edge.TransitionSelectedEvent += OnTransitionSelected;
            }
        }

        private void InstantiateStateView(AnimationStateAsset state)
        {
            var stateView = StateNodeView.New(new StateNodeViewModel(this, state, model.SelectedEntity));
            AddElement(stateView);
            stateToView.Add(state, stateView);

            stateView.StateSelectedEvent += OnStateSelected;
            
            // Add right-click drag to create transitions
            stateView.AddManipulator(TransitionDragManipulator.ForStateNode(stateView, this));
        }

        private void OnStateSelected(StateNodeView obj)
        {
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            // Setting it here would overwrite the root with nested state machines when navigating layers
            EditorState.Instance.SelectedState = obj.State;
        }

        private void OnTransitionSelected(TransitionEdge obj)
        {
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            EditorState.Instance.SelectTransition(obj.FromState, obj.ToState);
        }

        // NEW: Any State support methods

        private void InstantiateAnyStateNode()
        {
            anyStateNodeView = new AnyStateNodeView(model.StateMachineAsset, this);
            AddElement(anyStateNodeView);
            anyStateNodeView.AnyStateSelectedEvent += OnAnyStateSelected;
            
            // Add right-click drag to create transitions
            anyStateNodeView.AddManipulator(
                TransitionDragManipulator.ForAnyStateNode(anyStateNodeView, this, model.StateMachineAsset));
        }

        private void InstantiateExitNode()
        {
            exitNodeView = new ExitNodeView(model.StateMachineAsset, this);
            AddElement(exitNodeView);
            exitNodeView.ExitSelectedEvent += OnExitNodeSelected;
        }

        private void OnExitNodeSelected(ExitNodeView obj)
        {
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            EditorState.Instance.IsExitNodeSelected = true;
        }

        private void InstantiateAnyStateTransitionEdge(StateOutTransition anyTransition)
        {
            var toStateView = GetViewForState(anyTransition.ToState);
            if (toStateView == null || anyStateNodeView == null)
                return;

            var edge = anyStateNodeView.output.ConnectTo<TransitionEdge>(toStateView.input);
            edge.Model = new TransitionEdgeModel()
            {
                TransitionCount = 1,
                StateMachineAsset = model.StateMachineAsset,
                SelectedEntity = model.SelectedEntity
            };
            AddElement(edge);
            anyStateTransitionEdges[anyTransition.ToState] = edge;

            edge.TransitionSelectedEvent += OnAnyStateTransitionSelected;
        }

        private void CreateAnyStateTransition(AnimationStateAsset toState)
        {
            var transition = new StateOutTransition(toState);
            model.StateMachineAsset.AnyStateTransitions.Add(transition);
            InstantiateAnyStateTransitionEdge(transition);
        }

        /// <summary>
        /// Creates an Any State transition to the specified state. Called by TransitionDragManipulator.
        /// </summary>
        internal void CreateAnyStateTransitionTo(AnimationStateAsset toState)
        {
            if (toState == null) return;
            
            // Check for duplicate
            if (model.StateMachineAsset.AnyStateTransitions.Exists(t => t.ToState == toState))
                return;
            
            Undo.RecordObject(model.StateMachineAsset, "Create Any State Transition");
            CreateAnyStateTransition(toState);
            EditorUtility.SetDirty(model.StateMachineAsset);
            
            EditorState.Instance.NotifyTransitionAdded(null, toState); // null from = Any State
        }

        private void DeleteAnyStateTransition(AnimationStateAsset toState)
        {
            var transitions = model.StateMachineAsset.AnyStateTransitions;
            for (int i = transitions.Count - 1; i >= 0; i--)
            {
                if (transitions[i].ToState == toState)
                    transitions.RemoveAt(i);
            }
            anyStateTransitionEdges.Remove(toState);
            
            EditorState.Instance.NotifyTransitionRemoved(null, toState); // null from = Any State
        }

        private void AddExitState(AnimationStateAsset state)
        {
            if (state == null) return;
            
            // Check for duplicate
            if (model.StateMachineAsset.ExitStates.Contains(state))
                return;
            
            Undo.RecordObject(model.StateMachineAsset, "Add Exit State");
            model.StateMachineAsset.ExitStates.Add(state);
            EditorUtility.SetDirty(model.StateMachineAsset);
            
            // Create visual edge
            InstantiateExitStateEdge(state);
            
            EditorState.Instance.NotifyStateMachineChanged();
        }
        
        /// <summary>
        /// Adds a state as an exit state. Called by TransitionDragManipulator when dropping on Exit node.
        /// </summary>
        internal void AddExitStateFromDrag(AnimationStateAsset state)
        {
            AddExitState(state);
        }
        
        /// <summary>
        /// Checks if a state is already an exit state.
        /// </summary>
        internal bool IsExitState(AnimationStateAsset state)
        {
            return model.StateMachineAsset.ExitStates.Contains(state);
        }

        private void RemoveExitState(AnimationStateAsset state)
        {
            Undo.RecordObject(model.StateMachineAsset, "Remove Exit State");
            model.StateMachineAsset.ExitStates.Remove(state);
            EditorUtility.SetDirty(model.StateMachineAsset);
            
            EditorState.Instance.NotifyStateMachineChanged();
        }

        private void InstantiateExitStateEdge(AnimationStateAsset state)
        {
            var stateView = GetViewForState(state);
            if (stateView == null || exitNodeView == null)
                return;

            var edge = stateView.output.ConnectTo<TransitionEdge>(exitNodeView.input);
            edge.Model = new TransitionEdgeModel()
            {
                TransitionCount = 1,
                StateMachineAsset = model.StateMachineAsset,
                SelectedEntity = model.SelectedEntity
            };
            AddElement(edge);
        }
        
        /// <summary>
        /// Checks if an Any State exit transition can be created (doesn't exist yet).
        /// </summary>
        internal bool CanCreateAnyStateExitTransition()
        {
            return !model.StateMachineAsset.HasAnyStateExitTransition;
        }
        
        /// <summary>
        /// Creates an Any State exit transition. Called by TransitionDragManipulator when dropping on Exit node.
        /// </summary>
        internal void CreateAnyStateExitTransition()
        {
            // Don't allow duplicate
            if (!CanCreateAnyStateExitTransition())
                return;
            
            Undo.RecordObject(model.StateMachineAsset, "Create Any State Exit Transition");
            model.StateMachineAsset.AnyStateExitTransition = new StateOutTransition(null)
            {
                Conditions = new System.Collections.Generic.List<TransitionCondition>()
            };
            EditorUtility.SetDirty(model.StateMachineAsset);
            AssetDatabase.SaveAssets();
            
            InstantiateAnyStateExitEdge();
            
            EditorState.Instance.NotifyStateMachineChanged();
        }
        
        private void RemoveAnyStateExitTransition()
        {
            Undo.RecordObject(model.StateMachineAsset, "Remove Any State Exit Transition");
            model.StateMachineAsset.AnyStateExitTransition = null;
            EditorUtility.SetDirty(model.StateMachineAsset);
            AssetDatabase.SaveAssets();
            
            EditorState.Instance.NotifyStateMachineChanged();
        }
        
        private TransitionEdge anyStateExitEdge;
        
        private void InstantiateAnyStateExitEdge()
        {
            if (anyStateNodeView == null || exitNodeView == null)
                return;
            
            // Don't create duplicate
            if (anyStateExitEdge != null)
                return;

            var edge = anyStateNodeView.output.ConnectTo<TransitionEdge>(exitNodeView.input);
            edge.Model = new TransitionEdgeModel()
            {
                TransitionCount = 1,
                StateMachineAsset = model.StateMachineAsset,
                SelectedEntity = model.SelectedEntity
            };
            AddElement(edge);
            anyStateExitEdge = edge;
            
            edge.TransitionSelectedEvent += OnAnyStateExitTransitionSelected;
        }
        
        private void OnAnyStateExitTransitionSelected(TransitionEdge obj)
        {
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            EditorState.Instance.SelectTransition(null, null, isAnyState: true); // Any State exit transition
        }

        private void OnAnyStateSelected(AnyStateNodeView obj)
        {
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            EditorState.Instance.IsAnyStateSelected = true;
        }

        private void OnAnyStateTransitionSelected(TransitionEdge obj)
        {
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            EditorState.Instance.SelectTransition(null, obj.ToState, isAnyState: true);
        }

        private void InstantiateSubStateMachineExitTransitions(SubStateMachineStateAsset subState)
        {
            for (int i = 0; i < subState.OutTransitions.Count; i++)
            {
                var exitTransition = subState.OutTransitions[i];
                InstantiateExitTransitionEdge(subState, exitTransition, i);
            }
        }

        private void InstantiateExitTransitionEdge(SubStateMachineStateAsset subState, StateOutTransition exitTransition, int exitTransitionIndex)
        {
            var transitionPair = new TransitionPair(subState, exitTransition.ToState);
            
            // Skip if target state doesn't exist in this state machine
            var toStateView = GetViewForState(exitTransition.ToState);
            if (toStateView == null) return;

            if (exitTransitionEdges.TryGetValue(transitionPair, out var existingEdge))
            {
                existingEdge.Model.TransitionCount++;
                existingEdge.MarkDirtyRepaint();
            }
            else
            {
                var fromStateView = GetViewForState(subState);
                var edge = fromStateView.output.ConnectTo<ExitTransitionEdge>(toStateView.input);
                edge.Model = new TransitionEdgeModel()
                {
                    TransitionCount = 1,
                    StateMachineAsset = model.StateMachineAsset,
                    SelectedEntity = model.SelectedEntity
                };
                edge.ExitTransition = exitTransition;
                edge.SubStateMachine = subState;
                edge.ExitTransitionIndex = exitTransitionIndex;
                
                AddElement(edge);
                exitTransitionEdges.Add(transitionPair, edge);

                edge.TransitionSelectedEvent += OnExitTransitionSelected;
            }
        }

        private void OnExitTransitionSelected(TransitionEdge obj)
        {
            if (obj is ExitTransitionEdge exitEdge)
            {
                // Set inspector for exit transition - for now, just show the SubStateMachine inspector
                var inspectorModel = new AnimationStateInspectorModel
                {
                    StateView = GetViewForState(exitEdge.SubStateMachine)
                };
                // For now, show the standard inspector for the SubStateMachine
                var subMachineView = GetViewForState(exitEdge.SubStateMachine);
                subMachineView?.OnSelected();
            }
        }
        
        /// <summary>
        /// Helper method to check if the state machine has any SubStateMachine states.
        /// Replaces LINQ Any() to avoid per-call allocations.
        /// </summary>
        private static bool HasAnySubStateMachine(StateMachineAsset stateMachine)
        {
            if (stateMachine == null) return false;
            var states = stateMachine.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] is SubStateMachineStateAsset)
                    return true;
            }
            return false;
        }
    }
}