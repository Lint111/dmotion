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

        private Dictionary<AnimationStateAsset, StateNodeView> stateToView =
            new Dictionary<AnimationStateAsset, StateNodeView>();

        private Dictionary<TransitionPair, TransitionEdge> transitionToEdgeView =
            new Dictionary<TransitionPair, TransitionEdge>();

        // NEW: Any State node and transitions
        private AnyStateNodeView anyStateNodeView;
        private Dictionary<AnimationStateAsset, TransitionEdge> anyStateTransitionEdges =
            new Dictionary<AnimationStateAsset, TransitionEdge>();

        // Exit node for nested state machines
        private ExitNodeView exitNodeView;
        
        // Exit transitions from SubStateMachines
        private Dictionary<TransitionPair, TransitionEdge> exitTransitionEdges =
            new Dictionary<TransitionPair, TransitionEdge>();
        
        // Layer nodes (multi-layer mode only)
        private Dictionary<LayerStateAsset, LayerStateNodeView> layerToView =
            new Dictionary<LayerStateAsset, LayerStateNodeView>();

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
            
            // Prevent deletion of special nodes (Any State, Exit)
            deleteSelection = OnDeleteSelection;
        }
        
        private void OnDeleteSelection(string operationName, AskUser askUser)
        {
            // Filter selection to exclude non-deletable special nodes
            var elementsToDelete = new List<GraphElement>();
            foreach (var item in selection)
            {
                if (item is not GraphElement element) continue;
                if (element is AnyStateNodeView or ExitNodeView) continue;

                elementsToDelete.Add(element);

                // Also collect edges connected to deleted nodes
                if (element is Node node)
                {
                    CollectConnectedEdges(node, elementsToDelete);
                }
            }

            if (elementsToDelete.Count > 0)
            {
                DeleteElements(elementsToDelete);
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
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (Application.isPlaying) return;
            
            // Space to open state creation search window
            if (evt.keyCode == KeyCode.Space && model.StateMachineAsset != null)
            {
                OpenStateSearchWindow();
                evt.StopImmediatePropagation();
                return;
            }
            
            // F2 or Ctrl+R to rename selected node
            bool isRenameShortcut = evt.keyCode == KeyCode.F2 || 
                                    (evt.keyCode == KeyCode.R && evt.ctrlKey);
            
            if (isRenameShortcut)
            {
                // Find the selected state node and trigger rename
                StateNodeView selectedNode = null;
                foreach (var item in selection)
                {
                    if (item is StateNodeView stateNode)
                    {
                        selectedNode = stateNode;
                        break;
                    }
                }
                if (selectedNode != null)
                {
                    selectedNode.StartRename();
                    evt.StopImmediatePropagation();
                }
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
            if (model.StateMachineAsset != null)
            {
                var status = Application.isPlaying
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal;
                evt.menu.AppendAction("New State", a => CreateState(a, typeof(SingleClipStateAsset)), status);
                evt.menu.AppendAction("New Blend Tree 1D", a => CreateState(a, typeof(LinearBlendStateAsset)), status);
                evt.menu.AppendAction("New Blend Tree 2D", a => CreateState(a, typeof(Directional2DBlendStateAsset)), status);
                evt.menu.AppendAction("New Sub-State Machine", a => CreateState(a, typeof(SubStateMachineStateAsset)), status);
            }

            evt.StopPropagation();
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
            
            // Raise event - DependenciesPanelController handles refresh for SubMachines
            StateMachineEditorEvents.RaiseStateRemoved(model.StateMachineAsset, state);
        }

        private void DeleteAllOutTransitions(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            for (int i = fromState.OutTransitions.Count - 1; i >= 0; i--)
            {
                if (fromState.OutTransitions[i].ToState == toState)
                    fromState.OutTransitions.RemoveAt(i);
            }
            transitionToEdgeView.Remove(new TransitionPair(fromState, toState));
            
            StateMachineEditorEvents.RaiseTransitionRemoved(model.StateMachineAsset, fromState, toState);
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
            
            // Raise event for other panels
            StateMachineEditorEvents.RaiseStateAdded(model.StateMachineAsset, state);
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
            
            // Raise event for other panels
            StateMachineEditorEvents.RaiseStateAdded(model.StateMachineAsset, state);
        }

        private void OnSubStateMachineCreated(SubStateMachineStateAsset subState)
        {
            if (subState == null) return;
            
            InstantiateStateView(subState);
            
            // Raise event - DependenciesPanelController handles panel refresh
            StateMachineEditorEvents.RaiseStateAdded(model.StateMachineAsset, subState);
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
            
            StateMachineEditorEvents.RaiseTransitionAdded(model.StateMachineAsset, fromState, toState);
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
            foreach (var stateView in stateToView.Values)
            {
                stateView.UpdateView();
            }

            foreach (var transitions in transitionToEdgeView.Values)
            {
                transitions.UpdateView();
            }
        }

        internal void PopulateView(in StateMachineEditorViewModel newModel)
        {
            model = newModel;
            stateToView.Clear();
            transitionToEdgeView.Clear();
            anyStateTransitionEdges.Clear();
            exitTransitionEdges.Clear();
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

            // Create Any State node (always present)
            InstantiateAnyStateNode();
            
            // Create Exit node (always present - for nested state machine support)
            InstantiateExitNode();

            foreach (var s in model.StateMachineAsset.States)
            {
                InstantiateStateView(s);
            }

            foreach (var t in model.StateMachineAsset.States)
            {
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
            
            // Panel controllers handle Parameters and Dependencies panels via events
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
            // Raise event - InspectorController handles the inspector update
            StateMachineEditorEvents.RaiseStateSelected(model.StateMachineAsset, obj.State);
        }

        private void OnTransitionSelected(TransitionEdge obj)
        {
            // Raise event - InspectorController handles the inspector update
            StateMachineEditorEvents.RaiseTransitionSelected(model.StateMachineAsset, obj.FromState, obj.ToState);
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
            // Raise event - InspectorController handles the inspector update
            StateMachineEditorEvents.RaiseExitNodeSelected(model.StateMachineAsset);
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
            
            StateMachineEditorEvents.RaiseAnyStateTransitionAdded(model.StateMachineAsset, toState);
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
            
            StateMachineEditorEvents.RaiseAnyStateTransitionRemoved(model.StateMachineAsset, toState);
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
            
            StateMachineEditorEvents.RaiseExitStateAdded(model.StateMachineAsset, state);
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
            
            StateMachineEditorEvents.RaiseExitStateRemoved(model.StateMachineAsset, state);
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
            
            StateMachineEditorEvents.RaiseAnyStateExitTransitionChanged(model.StateMachineAsset, true);
        }
        
        private void RemoveAnyStateExitTransition()
        {
            Undo.RecordObject(model.StateMachineAsset, "Remove Any State Exit Transition");
            model.StateMachineAsset.AnyStateExitTransition = null;
            EditorUtility.SetDirty(model.StateMachineAsset);
            AssetDatabase.SaveAssets();
            
            StateMachineEditorEvents.RaiseAnyStateExitTransitionChanged(model.StateMachineAsset, false);
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
            // Raise event - InspectorController handles the inspector update
            // null toState indicates exit transition
            StateMachineEditorEvents.RaiseAnyStateTransitionSelected(model.StateMachineAsset, null);
        }

        private void OnAnyStateSelected(AnyStateNodeView obj)
        {
            // Raise event - InspectorController handles the inspector update
            StateMachineEditorEvents.RaiseAnyStateSelected(model.StateMachineAsset);
        }

        private void OnAnyStateTransitionSelected(TransitionEdge obj)
        {
            // Raise event - InspectorController handles the inspector update
            StateMachineEditorEvents.RaiseAnyStateTransitionSelected(model.StateMachineAsset, obj.ToState);
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