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

        internal StateMachineAsset StateMachine => model.StateMachineAsset;
        internal VisualTreeAsset StateNodeXml => model.StateNodeXml;
        
        /// <summary>
        /// Event fired when user double-clicks a SubStateMachine node to navigate into it.
        /// </summary>
        internal Action<StateMachineAsset> OnEnterSubStateMachine;

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
                if (item is GraphElement element && 
                    !(element is AnyStateNodeView) && 
                    !(element is ExitNodeView))
                {
                    elementsToDelete.Add(element);
                    
                    // Also collect edges connected to deleted nodes
                    if (element is Node node)
                    {
                        CollectConnectedEdges(node, elementsToDelete);
                    }
                }
            }
            
            if (elementsToDelete.Count > 0)
            {
                DeleteElements(elementsToDelete);
            }
        }
        
        private void CollectConnectedEdges(Node node, List<GraphElement> elementsToDelete)
        {
            // Collect edges from input ports
            foreach (var port in node.inputContainer.Children())
            {
                if (port is Port inputPort)
                {
                    foreach (var edge in inputPort.connections)
                    {
                        if (!elementsToDelete.Contains(edge))
                            elementsToDelete.Add(edge);
                    }
                }
            }
            
            // Collect edges from output ports
            foreach (var port in node.outputContainer.Children())
            {
                if (port is Port outputPort)
                {
                    foreach (var edge in outputPort.connections)
                    {
                        if (!elementsToDelete.Contains(edge))
                            elementsToDelete.Add(edge);
                    }
                }
            }
        }
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (Application.isPlaying) return;
            
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

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (model.StateMachineAsset != null)
            {
                var status = Application.isPlaying
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal;
                evt.menu.AppendAction("New State", a => CreateState(a, typeof(SingleClipStateAsset)), status);
                evt.menu.AppendAction("New Blend Tree 1D", a => CreateState(a, typeof(LinearBlendStateAsset)), status);
                evt.menu.AppendAction("New Sub-State Machine", a => CreateState(a, typeof(SubStateMachineStateAsset)), status);
            }

            evt.StopPropagation();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphviewchange)
        {
            if (Application.isPlaying)
            {
                return graphviewchange;
            }

            if (graphviewchange.elementsToRemove != null)
            {
                foreach (var el in graphviewchange.elementsToRemove)
                {
                    if (el is StateNodeView stateView)
                    {
                        DeleteState(stateView.State);
                    }
                    else if (el is TransitionEdge transition)
                    {
                        // Regular state → state transition
                        if (transition.output.node is StateNodeView from &&
                            transition.input.node is StateNodeView to)
                        {
                            DeleteAllOutTransitions(from.State, to.State);
                        }
                        // Any State → state transition
                        else if (transition.output.node is AnyStateNodeView &&
                                 transition.input.node is StateNodeView toState)
                        {
                            DeleteAnyStateTransition(toState.State);
                        }
                        // State → Exit node transition
                        else if (transition.output.node is StateNodeView exitFromState &&
                                 transition.input.node is ExitNodeView)
                        {
                            RemoveExitState(exitFromState.State);
                        }
                        // Any State → Exit node transition
                        else if (transition.output.node is AnyStateNodeView &&
                                 transition.input.node is ExitNodeView)
                        {
                            RemoveAnyStateExitTransition();
                            anyStateExitEdge = null;
                        }
                    }
                }
            }

            if (graphviewchange.edgesToCreate != null)
            {
                foreach (var edge in graphviewchange.edgesToCreate)
                {
                    if (edge is TransitionEdge)
                    {
                        // Regular state → state transition
                        if (edge.output.node is StateNodeView fromStateView &&
                            edge.input.node is StateNodeView toStateView)
                        {
                            CreateOutTransition(fromStateView.State, toStateView.State);
                        }
                        // Any State → state transition
                        else if (edge.output.node is AnyStateNodeView &&
                                 edge.input.node is StateNodeView anyStateTarget)
                        {
                            CreateAnyStateTransition(anyStateTarget.State);
                        }
                        // State → Exit node (marks state as exit state)
                        else if (edge.output.node is StateNodeView exitFromState &&
                                 edge.input.node is ExitNodeView)
                        {
                            AddExitState(exitFromState.State);
                        }
                    }
                }

                graphviewchange.edgesToCreate.Clear();
            }

            return graphviewchange;
        }

        private void DeleteState(AnimationStateAsset state)
        {
            var wasSubMachine = state is SubStateMachineStateAsset;
            
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
            
            // Raise event for other panels
            StateMachineEditorEvents.RaiseStateRemoved(model.StateMachineAsset, state);
            
            // Refresh dependencies panel if a SubMachine was deleted
            if (wasSubMachine)
            {
                RefreshDependenciesPanel();
            }
        }

        private void DeleteAllOutTransitions(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            for (int i = fromState.OutTransitions.Count - 1; i >= 0; i--)
            {
                if (fromState.OutTransitions[i].ToState == toState)
                    fromState.OutTransitions.RemoveAt(i);
            }
            transitionToEdgeView.Remove(new TransitionPair(fromState, toState));
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

        private void OnSubStateMachineCreated(SubStateMachineStateAsset subState)
        {
            if (subState == null) return;
            
            InstantiateStateView(subState);
            RefreshDependenciesPanel();
            
            // Raise event for other panels
            StateMachineEditorEvents.RaiseStateAdded(model.StateMachineAsset, subState);
        }

        /// <summary>
        /// Refreshes the dependencies panel visibility based on current SubMachines.
        /// </summary>
        internal void RefreshDependenciesPanel()
        {
            if (model.DependenciesInspectorView == null) return;
            
            var hasSubMachines = HasAnySubStateMachine(model.StateMachineAsset);
            
            if (hasSubMachines)
            {
                model.DependenciesInspectorView.style.display = DisplayStyle.Flex;
                model.DependenciesInspectorView.SetInspector<DependencyInspector, DependencyInspectorModel>(
                    model.StateMachineAsset, new DependencyInspectorModel()
                    {
                        StateMachine = model.StateMachineAsset
                    });
            }
            else
            {
                model.DependenciesInspectorView.style.display = DisplayStyle.None;
                model.DependenciesInspectorView.Clear();
            }
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

            model.ParametersInspectorView.SetInspector<ParametersInspector, ParameterInspectorModel>(
                model.StateMachineAsset, new ParameterInspectorModel()
                {
                    StateMachine = model.StateMachineAsset
                });

            // Set up dependencies inspector - only show if there are SubStateMachines
            if (model.DependenciesInspectorView != null)
            {
                var hasSubMachines = HasAnySubStateMachine(model.StateMachineAsset);
                
                if (hasSubMachines)
                {
                    model.DependenciesInspectorView.style.display = DisplayStyle.Flex;
                    model.DependenciesInspectorView.SetInspector<DependencyInspector, DependencyInspectorModel>(
                        model.StateMachineAsset, new DependencyInspectorModel()
                        {
                            StateMachine = model.StateMachineAsset
                        });
                }
                else
                {
                    model.DependenciesInspectorView.style.display = DisplayStyle.None;
                    model.DependenciesInspectorView.Clear();
                }
            }
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
            var inspectorModel = new AnimationStateInspectorModel
            {
                StateView = obj
            };
            switch (obj)
            {
                case SingleClipStateNodeView _:
                    model.InspectorView.SetInspector<SingleStateInspector, AnimationStateInspectorModel>
                        (inspectorModel.StateAsset, inspectorModel);
                    break;
                case LinearBlendStateNodeView _:
                    model.InspectorView.SetInspector<LinearBlendStateInspector, AnimationStateInspectorModel>
                        (inspectorModel.StateAsset, inspectorModel);
                    break;
                case SubStateMachineStateNodeView _:
                    model.InspectorView.SetInspector<SubStateMachineInspector, AnimationStateInspectorModel>
                        (inspectorModel.StateAsset, inspectorModel);
                    break;
                default:
                    // Fallback: just select the asset
                    Selection.activeObject = inspectorModel.StateAsset;
                    break;
            }
        }

        private void OnTransitionSelected(TransitionEdge obj)
        {
            var inspectorModel = new TransitionGroupInspectorModel()
            {
                FromState = obj.FromState,
                ToState = obj.ToState
            };
            model.InspectorView.SetInspector<TransitionGroupInspector, TransitionGroupInspectorModel>(
                inspectorModel.FromState, inspectorModel);
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
            // Clear the inspector or show exit state info
            model.InspectorView.Clear();
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
        }
        
        private void RemoveAnyStateExitTransition()
        {
            Undo.RecordObject(model.StateMachineAsset, "Remove Any State Exit Transition");
            model.StateMachineAsset.AnyStateExitTransition = null;
            EditorUtility.SetDirty(model.StateMachineAsset);
            AssetDatabase.SaveAssets();
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
            // Show Any State exit transition inspector
            model.InspectorView.SetInspector<AnyStateTransitionsInspector, AnyStateInspectorModel>(
                model.StateMachineAsset,
                new AnyStateInspectorModel
                {
                    ToState = null, // Will show exit transition when ToState is null and checking for exit
                });
        }

        private void OnAnyStateSelected(AnyStateNodeView obj)
        {
            // Show Any State transitions inspector (not parameters - those are in a separate panel)
            model.InspectorView.SetInspector<AnyStateTransitionsInspector, AnyStateInspectorModel>(
                model.StateMachineAsset, new AnyStateInspectorModel()
                {
                    ToState = null // Show all Any State transitions
                });
        }

        private void OnAnyStateTransitionSelected(TransitionEdge obj)
        {
            // Set inspector for any state transition - shows all transitions to this state
            model.InspectorView.SetInspector<AnyStateTransitionsInspector, AnyStateInspectorModel>(
                model.StateMachineAsset,
                new AnyStateInspectorModel
                {
                    ToState = obj.ToState
                });
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