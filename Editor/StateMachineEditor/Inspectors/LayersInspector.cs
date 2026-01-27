using System;
using System.Linq;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Inspector for layers in a multi-layer StateMachineAsset.
    /// Shows layer list with controls for weight, blend mode, and navigation.
    /// </summary>
    internal class LayersInspector : StateMachineInspector<LayersInspectorModel>
    {
        private DockablePanelSection layersSection;
        private bool _needsRefresh = true;

        private void OnEnable()
        {
            StateMachineEditorEvents.OnStateMachineChanged += OnStateMachineChanged;
            StateMachineEditorEvents.OnLayerAdded += OnLayerChanged;
            StateMachineEditorEvents.OnLayerRemoved += OnLayerChanged;
            layersSection = new DockablePanelSection("Layers", "DMotion_Layers", true);
        }

        private void OnDisable()
        {
            StateMachineEditorEvents.OnStateMachineChanged -= OnStateMachineChanged;
            StateMachineEditorEvents.OnLayerAdded -= OnLayerChanged;
            StateMachineEditorEvents.OnLayerRemoved -= OnLayerChanged;
        }

        private void OnStateMachineChanged(StateMachineAsset machine)
        {
            if (machine != model.StateMachine) return; 
            
            _needsRefresh = true;
            Repaint();
        }

        private void OnLayerChanged(StateMachineAsset machine, LayerStateAsset layer)
        {
            if (machine != model.StateMachine) return; 
            
            _needsRefresh = true;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            if (model.StateMachine == null || serializedObject?.targetObject == null) return; 

            if (!model.StateMachine.IsMultiLayer)
            {
                DrawConvertToMultiLayerUI();
                return;
            }

            DrawLayersSection();
        }

        private void DrawConvertToMultiLayerUI()
        {
            EditorGUILayout.HelpBox(
                "This is a single-layer state machine. Convert to multi-layer to add overlay layers for upper body, face, etc.",
                MessageType.Info);

            if (!GUILayout.Button("Convert to Multi-Layer")) return; 

            if (EditorUtility.DisplayDialog(
                "Convert to Multi-Layer",
                "This will move all existing states into 'Base Layer' and enable multi-layer mode.\n\n" +
                "You can then add additional layers.\n\nContinue?",
                "Convert", "Cancel"))
            {
                Undo.RecordObject(model.StateMachine, "Convert to Multi-Layer");
                model.StateMachine.ConvertToMultiLayer();
                StateMachineEditorEvents.RaiseConvertedToMultiLayer(model.StateMachine);

            }
        }

        private void DrawLayersSection()
        {
            serializedObject.Update();

            // Header with Add button
            if (!layersSection.DrawHeader(() => DrawLayersToolbar(), showDockButton: false)) return; 

            var layers = model.StateMachine.GetLayers().ToList();

            if (layers.Count == 0)
            {
                EditorGUILayout.HelpBox("No layers defined. This shouldn't happen in multi-layer mode.", MessageType.Warning);
                return;
            }

            // Draw each layer
            for (int i = 0; i < layers.Count; i++)
            {
                DrawLayerElement(i, layers[i]);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLayersToolbar()
        {
            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus"), GUILayout.Width(24), GUILayout.Height(18)))
            {
                AddLayer();
            }
        }

        private void DrawLayerElement(int index, LayerStateAsset layer)
        {
            if (layer == null) return;

            bool isBaseLayer = index == 0;

            // Layer box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row: index, name, buttons
            EditorGUILayout.BeginHorizontal();

            // Index badge
            var indexStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isBaseLayer ? FontStyle.Bold : FontStyle.Normal
            };
            GUILayout.Label(index.ToString(), indexStyle, GUILayout.Width(20));

            // Layer name
            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.TextField(layer.name);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Rename Layer");
                layer.name = newName;
                EditorUtility.SetDirty(layer);
            }

            // Edit button - navigate into layer
            if (GUILayout.Button("Edit", GUILayout.Width(40)))
            {
                EditLayer(layer);
            }

            // Delete button
            EditorGUI.BeginDisabledGroup(model.StateMachine.LayerCount <= 1);
            if (GUILayout.Button("x", GUILayout.Width(20)))
            {
                RemoveLayer(layer);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Weight slider
            EditorGUI.BeginChangeCheck();
            var newWeight = EditorGUILayout.Slider("Weight", layer.Weight, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Change Layer Weight");
                layer.Weight = newWeight;
                EditorUtility.SetDirty(layer);
                StateMachineEditorEvents.RaiseLayerChanged(model.StateMachine, layer);
            }

            // Blend mode (disabled for base layer)
            EditorGUI.BeginDisabledGroup(isBaseLayer);
            EditorGUI.BeginChangeCheck();
            var newBlendMode = (LayerBlendMode)EditorGUILayout.EnumPopup("Blend Mode", layer.BlendMode);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Change Layer Blend Mode");
                layer.BlendMode = newBlendMode;
                EditorUtility.SetDirty(layer);
                StateMachineEditorEvents.RaiseLayerChanged(model.StateMachine, layer);
            }
            EditorGUI.EndDisabledGroup();

            if (isBaseLayer)
            {
                EditorGUILayout.LabelField("Base layer always uses Override", EditorStyles.miniLabel);
            }
            
            // Avatar Mask (disabled for base layer - base should be full body)
            EditorGUI.BeginDisabledGroup(isBaseLayer);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newMask = (AvatarMask)EditorGUILayout.ObjectField(
                "Avatar Mask", 
                layer.AvatarMask, 
                typeof(AvatarMask), 
                false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Change Layer Avatar Mask");
                layer.AvatarMask = newMask;
                EditorUtility.SetDirty(layer);
                StateMachineEditorEvents.RaiseLayerChanged(model.StateMachine, layer);
            }
            
            // Quick-create button
            if (GUILayout.Button(new GUIContent("+", "Create new Avatar Mask"), GUILayout.Width(20)))
            {
                var createdMask = AvatarMaskCreator.CreateMaskForAsset(layer, $"{layer.name}_Mask");
                if (createdMask != null)
                {
                    Undo.RecordObject(layer, "Create Avatar Mask");
                    layer.AvatarMask = createdMask;
                    EditorUtility.SetDirty(layer);
                    StateMachineEditorEvents.RaiseLayerChanged(model.StateMachine, layer);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            
            if (isBaseLayer)
            {
                EditorGUILayout.LabelField("Base layer affects full body", EditorStyles.miniLabel);
            }
            else if (layer.AvatarMask == null)
            {
                EditorGUILayout.LabelField("No mask = full body (click + to create)", EditorStyles.miniLabel);
            }

            // State count info
            var stateCount = layer.NestedStateMachine != null ? layer.NestedStateMachine.States.Count : 0;
            EditorGUILayout.LabelField($"States: {stateCount}", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void AddLayer()
        {
            Undo.RecordObject(model.StateMachine, "Add Layer");
            var layer = model.StateMachine.AddLayer();
            StateMachineEditorEvents.RaiseLayerAdded(model.StateMachine, layer);
        }

        private void RemoveLayer(LayerStateAsset layer)
        {
            if (EditorUtility.DisplayDialog(
                "Remove Layer",
                $"Remove layer '{layer.name}'?\n\nThis will delete all states in this layer.",
                "Remove", "Cancel"))
            {
                Undo.RecordObject(model.StateMachine, "Remove Layer");
                model.StateMachine.RemoveLayer(layer);
                StateMachineEditorEvents.RaiseLayerRemoved(model.StateMachine, layer);
            }
        }

        private void EditLayer(LayerStateAsset layer)
        {
            if (layer?.NestedStateMachine == null) return;

            // Notify via callback if available
            model.OnEditLayer?.Invoke(layer);

            // Also raise event for breadcrumb/navigation
            StateMachineEditorEvents.RaiseLayerEntered(model.StateMachine, layer, layer.NestedStateMachine);
        }
    }
}
