using DMotion.Authoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMotion.Editor
{
    /// <summary>
    /// Graph node view for animation layers in multi-layer state machines.
    /// Layers are displayed at the root level and can be entered like SubStateMachines.
    /// Unlike regular states, layers don't have transitions - they run in parallel.
    /// </summary>
    internal class LayerStateNodeView : Node
    {
        private new LayerStateAsset layer;  // 'new' to hide GraphElement.layer
        private AnimationStateMachineEditorView graphView;
        
        private Label weightLabel;
        private Label blendModeLabel;
        private Label stateCountLabel;
        private Button enterButton;
        
        // Rename support
        private Label titleLabel;
        private new VisualElement titleContainer;  // 'new' to hide Node.titleContainer
        private TextField renameField;
        private bool isRenaming;

        public LayerStateAsset Layer => layer;

        public LayerStateNodeView()
        {
            // Basic node setup
            AddToClassList("layer-node");
        }

        internal static LayerStateNodeView Create(LayerStateAsset layerAsset, AnimationStateMachineEditorView parentView)
        {
            var view = new LayerStateNodeView();
            view.layer = layerAsset;
            view.graphView = parentView;
            view.viewDataKey = layerAsset.StateEditorData.Guid;
            
            view.SetupUI();
            view.SetPosition(new Rect(layerAsset.StateEditorData.GraphPosition, Vector2.one));
            
            return view;
        }

        private void SetupUI()
        {
            // Title
            title = layer.name;
            
            // Style the node
            style.minWidth = 180;
            
            // Cache title elements for rename support
            titleContainer = this.Q("title");
            titleLabel = this.Q<Label>("title-label");
            
            // Add layer-specific styling
            if (titleContainer != null)
            {
                titleContainer.style.backgroundColor = new Color(0.3f, 0.4f, 0.5f, 1f);
            }
            
            // Enter button in title bar
            enterButton = new Button(OnEnterClicked)
            {
                text = ">>",
                tooltip = "Edit layer's state machine"
            };
            enterButton.style.position = Position.Absolute;
            enterButton.style.right = 4;
            enterButton.style.top = 4;
            enterButton.style.width = 28;
            enterButton.style.height = 20;
            enterButton.style.fontSize = 11;
            titleContainer?.Add(enterButton);
            
            // Content area with layer info
            var content = new VisualElement();
            content.style.paddingLeft = 8;
            content.style.paddingRight = 8;
            content.style.paddingTop = 4;
            content.style.paddingBottom = 8;
            
            // Weight display
            var weightRow = new VisualElement();
            weightRow.style.flexDirection = FlexDirection.Row;
            weightRow.style.justifyContent = Justify.SpaceBetween;
            weightRow.Add(new Label("Weight:") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            weightLabel = new Label(layer.Weight.ToString("F2"));
            weightRow.Add(weightLabel);
            content.Add(weightRow);
            
            // Blend mode display (skip for base layer - index 0)
            var blendRow = new VisualElement();
            blendRow.style.flexDirection = FlexDirection.Row;
            blendRow.style.justifyContent = Justify.SpaceBetween;
            blendRow.Add(new Label("Blend:") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            blendModeLabel = new Label(layer.BlendMode.ToString());
            blendRow.Add(blendModeLabel);
            content.Add(blendRow);
            
            // State count
            var stateCount = layer.NestedStateMachine?.States.Count ?? 0;
            var stateRow = new VisualElement();
            stateRow.style.flexDirection = FlexDirection.Row;
            stateRow.style.justifyContent = Justify.SpaceBetween;
            stateRow.style.marginTop = 4;
            stateRow.Add(new Label("States:"));
            stateCountLabel = new Label(stateCount.ToString());
            stateRow.Add(stateCountLabel);
            content.Add(stateRow);
            
            // Add content to extension container
            extensionContainer.Add(content);
            RefreshExpandedState();
            
            // Double-click handling
            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.clickCount != 2 || evt.button != 0) return;
            
            // Check if click is on title label
            bool isOnTitle = false;
            if (titleLabel != null && titleLabel.worldBound.Contains(evt.mousePosition))
            {
                isOnTitle = true;
            }
            else if (titleContainer != null && titleContainer.worldBound.Contains(evt.mousePosition))
            {
                // Also check title container (the whole title bar area)
                // But exclude the enter button
                if (enterButton == null || !enterButton.worldBound.Contains(evt.mousePosition))
                {
                    isOnTitle = true;
                }
            }
            
            if (isOnTitle)
            {
                // Double-click on title = rename
                StartRename();
            }
            else
            {
                // Double-click on body = enter layer
                OnEnterClicked();
            }
            evt.StopImmediatePropagation();
        }

        private void OnEnterClicked()
        {
            if (layer?.NestedStateMachine == null)
            {
                Debug.LogWarning("Layer has no state machine assigned.");
                return;
            }

            // Get root machine from graph view
            var rootMachine = graphView?.StateMachine;
            if (rootMachine != null)
            {
                StateMachineEditorEvents.RaiseLayerEntered(rootMachine, layer, layer.NestedStateMachine);
            }
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            if (layer != null)
            {
                layer.StateEditorData.GraphPosition = new Vector2(newPos.xMin, newPos.yMin);
                EditorUtility.SetDirty(layer);
            }
        }

        internal void UpdateView()
        {
            title = layer.name;
            weightLabel.text = layer.Weight.ToString("F2");
            blendModeLabel.text = layer.BlendMode.ToString();
            stateCountLabel.text = (layer.NestedStateMachine?.States.Count ?? 0).ToString();
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (Application.isPlaying)
            {
                evt.menu.AppendAction("Edit Layer", _ => OnEnterClicked(), DropdownMenuAction.Status.Normal);
                return;
            }

            evt.menu.AppendAction("Edit Layer", _ => OnEnterClicked(), DropdownMenuAction.Status.Normal);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Rename", _ => StartRename(), DropdownMenuAction.Status.Normal);
        }

        /// <summary>
        /// Starts inline rename mode. Called by keyboard shortcut (F2) or context menu.
        /// </summary>
        public void StartRename()
        {
            if (isRenaming) return;
            if (Application.isPlaying) return;
            
            isRenaming = true;
            
            // Create text field for editing
            renameField = new TextField();
            renameField.value = layer.name;
            
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
            
            // Hide the title label and insert text field
            if (titleLabel != null)
            {
                titleLabel.style.display = DisplayStyle.None;
            }
            
            if (titleContainer != null)
            {
                titleContainer.Insert(0, renameField);
            }
            else
            {
                Add(renameField);
            }
            
            // Select all text and focus after a frame
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
            
            if (!string.IsNullOrEmpty(newName) && newName != layer.name)
            {
                Undo.RecordObject(layer, "Rename Layer");
                layer.name = newName;
                title = newName;
                EditorUtility.SetDirty(layer);
                
                // Mark parent asset dirty too
                string assetPath = AssetDatabase.GetAssetPath(layer);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (mainAsset != null && mainAsset != layer)
                    {
                        EditorUtility.SetDirty(mainAsset);
                    }
                }
            }
            
            EndRename();
        }
        
        private void CancelRename()
        {
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
            if (renameField != null)
            {
                renameField.RemoveFromHierarchy();
                renameField = null;
            }
            
            if (titleLabel != null)
            {
                titleLabel.style.display = DisplayStyle.Flex;
            }
        }
    }
}
