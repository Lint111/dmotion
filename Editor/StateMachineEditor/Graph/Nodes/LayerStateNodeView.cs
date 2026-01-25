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
        private LayerStateAsset layer;
        private AnimationStateMachineEditorView graphView;
        
        private Label weightLabel;
        private Label blendModeLabel;
        private Label stateCountLabel;
        private Button enterButton;

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
            
            // Add layer-specific styling
            var titleContainer = this.Q("title");
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
            
            // Double-click to enter
            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.clickCount == 2 && evt.button == 0)
            {
                OnEnterClicked();
                evt.StopImmediatePropagation();
            }
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

        private void StartRename()
        {
            // TODO: Implement inline rename similar to StateNodeView
            // For now, use a simple dialog
            var newName = EditorInputDialog.Show("Rename Layer", "Enter new name:", layer.name);
            if (!string.IsNullOrEmpty(newName) && newName != layer.name)
            {
                Undo.RecordObject(layer, "Rename Layer");
                layer.name = newName;
                title = newName;
                EditorUtility.SetDirty(layer);
            }
        }
    }

    /// <summary>
    /// Simple input dialog for renaming.
    /// </summary>
    internal static class EditorInputDialog
    {
        internal static string Show(string title, string message, string defaultValue)
        {
            // Unity doesn't have a built-in input dialog, so we use a workaround
            // In a full implementation, this would be a custom EditorWindow
            return defaultValue; // Placeholder - rename via inspector for now
        }
    }
}
