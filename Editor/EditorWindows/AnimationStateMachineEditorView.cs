using System;
using System.Collections.Generic;
using System.Linq;
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
            
            // Centralized keyboard handling for all nodes
            RegisterCallback<KeyDownEvent>(OnKeyDown);
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
                var selectedNode = selection.OfType<StateNodeView>().FirstOrDefault();
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
                    }
                }

                graphviewchange.edgesToCreate.Clear();
            }

            return graphviewchange;
        }

        private void DeleteState(AnimationStateAsset state)
        {
            model.StateMachineAsset.DeleteState(state);
            UpdateView();
        }

        private void DeleteAllOutTransitions(AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            fromState.OutTransitions.RemoveAll(t => t.ToState == toState);
            transitionToEdgeView.Remove(new TransitionPair(fromState, toState));
        }

        private void CreateState(DropdownMenuAction action, Type stateType)
        {
            var state = model.StateMachineAsset.CreateState(stateType);
            state.StateEditorData.GraphPosition = contentViewContainer.WorldToLocal(action.eventInfo.mousePosition);
            InstantiateStateView(state);
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
            if (fromState == toState) return; // No self-transitions
            
            Undo.RecordObject(fromState, "Create Transition");
            CreateOutTransition(fromState, toState);
            EditorUtility.SetDirty(fromState);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList()
                .Where((nap => nap.direction != startPort.direction &&
                               nap.node != startPort.node)).ToList();
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

            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements.ToList());
            graphViewChanged += OnGraphViewChanged;

            // Create Any State node (always present)
            InstantiateAnyStateNode();

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

            // Create exit transition edges for SubStateMachines
            foreach (var state in model.StateMachineAsset.States)
            {
                if (state is SubStateMachineStateAsset subState)
                {
                    InstantiateSubStateMachineExitTransitions(subState);
                }
            }

            model.ParametersInspectorView.SetInspector<ParametersInspector, ParameterInspectorModel>(
                model.StateMachineAsset, new ParameterInspectorModel()
                {
                    StateMachine = model.StateMachineAsset
                });
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
                    // SubStateMachine uses the custom editor defined in SubStateMachineStateAssetEditor
                    // Just select the asset in the inspector - Unity will use our custom editor
                    Selection.activeObject = inspectorModel.StateAsset;
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
            model.StateMachineAsset.AnyStateTransitions.RemoveAll(t => t.ToState == toState);
            anyStateTransitionEdges.Remove(toState);
        }

        private void OnAnyStateSelected(AnyStateNodeView obj)
        {
            // Show list of all Any State transitions in inspector
            model.InspectorView.SetInspector<ParametersInspector, ParameterInspectorModel>(
                model.StateMachineAsset, new ParameterInspectorModel()
                {
                    StateMachine = model.StateMachineAsset
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
    }
}