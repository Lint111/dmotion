using System;
using System.Collections.Generic;
using DMotion.Authoring;
using Unity.Entities;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    internal class LinearBlendStateNodeView : StateNodeView<LinearBlendStateAsset>
    {
        public LinearBlendStateNodeView(VisualTreeAsset asset) : base(asset)
        {
        }
    }

    internal class Directional2DBlendStateNodeView : StateNodeView<Directional2DBlendStateAsset>
    {
        public Directional2DBlendStateNodeView(VisualTreeAsset asset) : base(asset)
        {
        }
    }

    internal class SingleClipStateNodeView : StateNodeView<SingleClipStateAsset>
    {
        public SingleClipStateNodeView(VisualTreeAsset asset) : base(asset)
        {
        }
    }

    internal class SubStateMachineStateNodeView : StateNodeView<SubStateMachineStateAsset>
    {
        private Button enterButton;
        
        public SubStateMachineStateNodeView(VisualTreeAsset asset) : base(asset)
        {
        }

        internal void SetupSubStateMachineUI()
        {
            // Add "Enter" button in the top-left corner
            enterButton = new Button(OnEnterButtonClicked)
            {
                text = ">>",
                tooltip = "Open nested state machine"
            };
            enterButton.AddToClassList("substatemachine-enter-button");
            enterButton.style.position = Position.Absolute;
            enterButton.style.left = 4;
            enterButton.style.top = 4;
            enterButton.style.width = 24;
            enterButton.style.height = 18;
            enterButton.style.fontSize = 10;
            enterButton.style.paddingLeft = 2;
            enterButton.style.paddingRight = 2;
            enterButton.style.paddingTop = 0;
            enterButton.style.paddingBottom = 0;
            enterButton.style.marginLeft = 0;
            enterButton.style.marginRight = 0;
            enterButton.style.marginTop = 0;
            enterButton.style.marginBottom = 0;
            
            Add(enterButton);
            
            // Add the substatemachine class for styling
            AddToClassList("substatemachine");
        }

        private void OnEnterButtonClicked()
        {
            OpenNestedStateMachine();
        }

        protected override void OnDoubleClickBody()
        {
            OpenNestedStateMachine();
        }

        private void OpenNestedStateMachine()
        {
            var subState = State as SubStateMachineStateAsset;
            if (subState?.NestedStateMachine == null)
            {
                Debug.LogWarning("No nested state machine assigned to this SubStateMachine node.");
                return;
            }

            // Raise navigation event - BreadcrumbController handles the actual navigation
            // Note: Don't set RootStateMachine here - it's set when opening the root asset
            // EnterSubStateMachine updates CurrentViewStateMachine appropriately
            var subStateMachine = subState.NestedStateMachine;
            if (subStateMachine != null)
            {
                EditorState.Instance.EnterSubStateMachine(subStateMachine);
            }
        }
    }

    internal class StateNodeView<T> : StateNodeView
        where T : AnimationStateAsset
    {
        public StateNodeView(VisualTreeAsset asset) : base(asset)
        {
        }
    }

    internal struct StateNodeViewModel
    {
        internal AnimationStateMachineEditorView ParentView;
        internal AnimationStateAsset StateAsset;
        internal EntitySelectionProxyWrapper SelectedEntity;

        internal StateNodeViewModel(AnimationStateMachineEditorView parentView,
            AnimationStateAsset stateAsset,
            EntitySelectionProxyWrapper selectedEntity)
        {
            ParentView = parentView;
            StateAsset = stateAsset;
            SelectedEntity = selectedEntity;
        }
    }

    internal struct AnimationStateStyle
    {
        internal string ClassName;
        internal static AnimationStateStyle Default => new() { ClassName = "defaultstate" };
        internal static AnimationStateStyle Normal => new() { ClassName = "normalstate" };
        internal static AnimationStateStyle Active => new() { ClassName = "activestate" };


        internal static IEnumerable<AnimationStateStyle> AllStyles
        {
            get
            {
                yield return Default;
                yield return Normal;
                yield return Active;
            }
        }

        public override int GetHashCode()
        {
            return ClassName.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationStateStyle other && ClassName == other.ClassName;
        }

        public static bool operator ==(AnimationStateStyle left, AnimationStateStyle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AnimationStateStyle left, AnimationStateStyle right)
        {
            return !left.Equals(right);
        }
    }

    internal abstract class StateNodeView : BaseNodeView
    {
        protected StateNodeViewModel model;

        internal Action<StateNodeView> StateSelectedEvent;
        public AnimationStateAsset State => model.StateAsset;
        public EntitySelectionProxyWrapper SelectedEntity => model.SelectedEntity;
        public StateMachineAsset StateMachine => model.ParentView.StateMachine;
        public Port input;
        private ProgressBar timelineBar;
        
        // Rename functionality
        private TextField renameField;
        private Label titleLabel;
        private VisualElement titleElement;
        private bool isRenaming;
        private int clickCount;
        private double lastClickTime;
        private const double DoubleClickThreshold = 0.4; // seconds

        protected StateNodeView(VisualTreeAsset asset) : base(AssetDatabase.GetAssetPath(asset))
        {
            // UXML-based construction - base handles the template loading
        }

        public static StateNodeView New(in StateNodeViewModel model)
        {
            StateNodeView view = model.StateAsset switch
            {
                SingleClipStateAsset _ => new SingleClipStateNodeView(model.ParentView.StateNodeXml),
                LinearBlendStateAsset _ => new LinearBlendStateNodeView(model.ParentView.StateNodeXml),
                Directional2DBlendStateAsset _ => new Directional2DBlendStateNodeView(model.ParentView.StateNodeXml),
                SubStateMachineStateAsset _ => new SubStateMachineStateNodeView(model.ParentView.StateNodeXml),
                _ => throw new NotImplementedException()
            };

            view.model = model;
            view.SetGraphView(model.ParentView);
            view.title = view.State.name;
            view.viewDataKey = view.State.StateEditorData.Guid;
            view.timelineBar = view.Q<ProgressBar>();

            view.SetPosition(new Rect(view.State.StateEditorData.GraphPosition, Vector2.one));

            view.CreateInputPort();
            view.CreateOutputPort();

            view.SetNodeStateStyle(view.GetStateStyle());
            
            // Setup rename functionality
            view.SetupRenameSupport();
            
            // Setup SubStateMachine-specific UI
            if (view is SubStateMachineStateNodeView subView)
            {
                subView.SetupSubStateMachineUI();
            }

            return view;
        }

        internal void UpdateView()
        {
            var style = GetStateStyle();
            SetNodeStateStyle(style);

            if (style == AnimationStateStyle.Active)
            {
                UpdateTimelineProgressBar();
            }
        }

        internal AnimationStateStyle GetStateStyle()
        {
            if (Application.isPlaying && SelectedEntity != null && SelectedEntity.Exists)
            {
                var stateMachine = SelectedEntity.GetComponent<AnimationStateMachine>();
                var currentAnimationState = SelectedEntity.GetComponent<AnimationCurrentState>();
                if (stateMachine.CurrentState.IsValid && stateMachine.CurrentState.AnimationStateId ==
                    currentAnimationState.AnimationStateId)
                {
                    var currentState = StateMachine.States[stateMachine.CurrentState.StateIndex];
                    if (currentState == State)
                    {
                        return AnimationStateStyle.Active;
                    }
                }
            }

            if (StateMachine.IsDefaultState(State))
            {
                return AnimationStateStyle.Default;
            }

            return AnimationStateStyle.Normal;
        }

        internal void SetNodeStateStyle(in AnimationStateStyle stateStyle)
        {
            foreach (var s in AnimationStateStyle.AllStyles)
            {
                RemoveFromClassList(s.ClassName);
            }

            AddToClassList(stateStyle.ClassName);

            timelineBar.style.display =
                stateStyle == AnimationStateStyle.Active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        protected override void BuildNodeContextMenu(ContextualMenuPopulateEvent evt, DropdownMenuAction.Status status)
        {
            // Build submenu with all available target states for transition creation
            // Self-transitions are allowed (e.g., re-trigger attack, reset idle)
            var sb = StringBuilderCache.Get();
            foreach (var targetState in StateMachine.States)
            {
                var target = targetState; // Capture for closure
                sb.Clear().Append("Create Transition/").Append(target.name);
                if (targetState == State)
                {
                    sb.Append(" (Self)");
                }
                evt.menu.AppendAction(sb.ToString(), _ => CreateTransitionTo(target), status);
            }

            var setDefaultStateMenuStatus = StateMachine.IsDefaultState(State) || Application.isPlaying
                ? DropdownMenuAction.Status.Disabled
                : DropdownMenuAction.Status.Normal;

            evt.menu.AppendAction("Set As Default State", OnContextMenuSetAsDefaultState, setDefaultStateMenuStatus);
            
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Rename", _ => StartRename(), status);
        }

        private void OnContextMenuSetAsDefaultState(DropdownMenuAction obj)
        {
            var previousState = model.ParentView.GetViewForState(StateMachine.DefaultState);
            if (previousState != null)
            {
                previousState.SetNodeStateStyle(AnimationStateStyle.Normal);
            }

            StateMachine.SetDefaultState(State);
            SetNodeStateStyle(AnimationStateStyle.Default);
        }

        /// <summary>
        /// Creates a transition from this state to the target state.
        /// Uses public GraphView APIs instead of reflection.
        /// </summary>
        private void CreateTransitionTo(AnimationStateAsset targetState)
        {
            var outTransition = new StateOutTransition(targetState);
            State.OutTransitions.Add(outTransition);
            EditorUtility.SetDirty(State);

            // Request the parent view to create the visual edge
            model.ParentView.CreateTransitionEdgeForState(State, State.OutTransitions.Count - 1);
        }

        protected void CreateInputPort()
        {
            input = Port.Create<TransitionEdge>(Orientation.Vertical, Direction.Input, Port.Capacity.Multi,
                typeof(bool));
            input.portName = "";
            inputContainer.Add(input);
        }

        // Output port creation inherited from BaseNodeView

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            State.StateEditorData.GraphPosition = new Vector2(newPos.xMin, newPos.yMin);
            EditorUtility.SetDirty(State);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            StateSelectedEvent?.Invoke(this);
        }

        #region Rename Support

        private void SetupRenameSupport()
        {
            // Find the title label and container in our custom UXML
            titleLabel = this.Q<Label>("title-label");
            titleElement = this.Q<VisualElement>("title");
            
            // Enable picking on title element so we can receive mouse events
            if (titleElement != null)
            {
                titleElement.pickingMode = PickingMode.Position;
            }
            
            // Also enable on the label
            if (titleLabel != null)
            {
                titleLabel.pickingMode = PickingMode.Position;
            }

            // Double-click to rename - use PointerDownEvent which fires before selection handling
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            
            // Register for undo/redo to refresh title
            Undo.undoRedoPerformed += OnUndoRedo;
            
            // Cleanup when detached
            RegisterCallback<DetachFromPanelEvent>(_ => 
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
            });
        }
        
        private void OnUndoRedo()
        {
            // Refresh the title in case name was changed by undo/redo
            if (State != null && title != State.name)
            {
                title = State.name;
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (Application.isPlaying) return;
            if (evt.button != 0) return; // Left click only
            
            // Check if click is on the title area
            bool isOnTitle = false;
            if (titleElement != null && titleElement.worldBound.Contains(evt.position))
            {
                isOnTitle = true;
            }
            else if (titleLabel != null && titleLabel.worldBound.Contains(evt.position))
            {
                isOnTitle = true;
            }
            
            // Use the event's click count for double-click detection
            if (evt.clickCount == 2)
            {
                if (isOnTitle)
                {
                    // Double-click on title = rename
                    StartRename();
                }
                else
                {
                    // Double-click outside title = custom action (e.g., open nested machine)
                    OnDoubleClickBody();
                }
                evt.StopImmediatePropagation();
            }
        }

        /// <summary>
        /// Called when user double-clicks on the node body (not the title).
        /// Override in derived classes for custom behavior.
        /// </summary>
        protected virtual void OnDoubleClickBody()
        {
            // Default: do nothing (or could also rename)
        }

        /// <summary>
        /// Starts the inline rename process.
        /// </summary>
        public void StartRename()
        {
            if (isRenaming) return;
            if (Application.isPlaying) return;
            
            isRenaming = true;

            // Create text field for editing
            renameField = new TextField();
            renameField.value = State.name;
            
            // Match the label's styling
            renameField.style.flexGrow = 1;
            renameField.style.marginLeft = 0;
            renameField.style.marginRight = 0;
            renameField.style.marginTop = 0;
            renameField.style.marginBottom = 0;
            renameField.style.paddingLeft = 0;
            renameField.style.paddingRight = 0;
            
            // Style the text input element inside TextField
            var textInput = renameField.Q("unity-text-input");
            if (textInput != null)
            {
                textInput.style.fontSize = 12;
                textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
                textInput.style.paddingLeft = 4;
                textInput.style.paddingRight = 4;
                textInput.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                textInput.style.color = Color.white;
            }

            // Hide the title label and insert the text field in its place
            if (titleLabel != null)
            {
                titleLabel.style.display = DisplayStyle.None;
            }

            // Add to the title element (our custom UXML element)
            if (titleElement != null)
            {
                titleElement.Add(renameField);
            }
            else if (titleContainer != null)
            {
                titleContainer.Add(renameField);
            }
            else
            {
                Add(renameField);
            }

            // Select all text and focus after a frame to ensure layout is complete
            renameField.schedule.Execute(() =>
            {
                renameField.Focus();
                renameField.SelectAll();
            });

            // Handle completion
            renameField.RegisterCallback<FocusOutEvent>(OnRenameFocusOut);
            renameField.RegisterCallback<KeyDownEvent>(OnRenameKeyDown);
        }
        
        private void OnRenameFocusOut(FocusOutEvent evt)
        {
            // Commit directly - if Escape was pressed, CancelRename would have already run
            if (isRenaming)
            {
                CommitRename();
            }
        }
        
        private void OnRenameKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                CommitRename();
                evt.StopImmediatePropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CancelRename();
                evt.StopImmediatePropagation();
            }
        }

        private void CommitRename()
        {
            if (!isRenaming) return;
            
            string newName = renameField?.value?.Trim();
            
            if (!string.IsNullOrEmpty(newName) && newName != State.name)
            {
                // Rename the asset
                Undo.RecordObject(State, "Rename State");
                State.name = newName;
                EditorUtility.SetDirty(State);
                
                // If it's a sub-asset, we need to mark the parent dirty too
                string assetPath = AssetDatabase.GetAssetPath(State);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (mainAsset != null && mainAsset != State)
                    {
                        EditorUtility.SetDirty(mainAsset);
                    }
                }
            }

            EndRename();
        }

        private void CancelRename()
        {
            // Set flag first to prevent CommitRename from running in FocusOut
            isRenaming = false;
            EndRenameUI();
        }

        private void EndRename()
        {
            if (!isRenaming) return;
            
            isRenaming = false;
            EndRenameUI();
        }
        
        private void EndRenameUI()
        {
            // Remove text field and its callbacks
            if (renameField != null)
            {
                renameField.UnregisterCallback<FocusOutEvent>(OnRenameFocusOut);
                renameField.UnregisterCallback<KeyDownEvent>(OnRenameKeyDown);
                renameField.RemoveFromHierarchy();
                renameField = null;
            }

            // Show the title label again and update its text
            if (titleLabel != null)
            {
                titleLabel.text = State.name;
                titleLabel.style.display = DisplayStyle.Flex;
            }
            
            // Also update the node's title property
            title = State.name;
        }

        #endregion

        private void UpdateTimelineProgressBar()
        {
            var stateMachine = SelectedEntity.GetComponent<AnimationStateMachine>();

            if (!stateMachine.CurrentState.IsValid)
            {
                return;
            }

            var animationStates = SelectedEntity.GetBuffer<AnimationState>();
            var samplers = SelectedEntity.GetBuffer<ClipSampler>();
            var animationState = animationStates.GetWithId((byte)stateMachine.CurrentState.AnimationStateId);

            var avgTime = 0.0f;
            var avgDuration = 0.0f;
            var startSamplerIndex = samplers.IdToIndex(animationState.StartSamplerId);
            for (var i = startSamplerIndex; i < startSamplerIndex + animationState.ClipCount; i++)
            {
                avgTime += samplers[i].Time;
                avgDuration += samplers[i].Duration;
            }

            avgTime /= animationState.ClipCount;
            avgDuration /= animationState.ClipCount;

            var percent = avgTime / avgDuration;
            timelineBar.value = percent;
        }
    }
}
