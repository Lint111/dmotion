using System;
using System.Collections.Generic;
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
        
        // Cached to avoid allocations in OnGUI
        private List<LayerStateAsset> _cachedLayers;

        private void OnEnable()
        {
            EditorState.Instance.StructureChanged += OnStructureChanged;
            layersSection = new DockablePanelSection("Layers", "DMotion_Layers", true);
        }

        private void OnDisable()
        {
            EditorState.Instance.StructureChanged -= OnStructureChanged;
        }

        private void OnStructureChanged(object sender, StructureChangedEventArgs e)
        {
            if (EditorState.Instance.RootStateMachine != model.StateMachine) return;
            
            // Refresh on relevant structure changes
            if (e.ChangeType == StructureChangeType.LayerAdded ||
                e.ChangeType == StructureChangeType.LayerRemoved ||
                e.ChangeType == StructureChangeType.LayerChanged ||
                e.ChangeType == StructureChangeType.ConvertedToMultiLayer ||
                e.ChangeType == StructureChangeType.GeneralChange)
            {
                _needsRefresh = true;
                Repaint();
            }
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
                EditorState.Instance.NotifyConvertedToMultiLayer();
            }
        }

        private void DrawLayersSection()
        {
            serializedObject.Update();

            // Header with Add button
            if (!layersSection.DrawHeader(() => DrawLayersToolbar(), showDockButton: false)) return; 

            // Refresh cached layers only when needed (or first time)
            if (_needsRefresh || _cachedLayers == null)
            {
                if (_cachedLayers == null)
                    _cachedLayers = new List<LayerStateAsset>(8);
                else
                    _cachedLayers.Clear();
                
                foreach (var layer in model.StateMachine.GetLayers())
                    _cachedLayers.Add(layer);
                    
                _needsRefresh = false;
            }

            if (_cachedLayers.Count == 0)
            {
                EditorGUILayout.HelpBox("No layers defined. This shouldn't happen in multi-layer mode.", MessageType.Warning);
                return;
            }

            // Draw each layer
            for (int i = 0; i < _cachedLayers.Count; i++)
            {
                DrawLayerElement(i, _cachedLayers[i]);
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

            // Index badge (use centralized style cache)
            var indexStyle = isBaseLayer ? EditorStyleCache.IndexBold : EditorStyleCache.IndexNormal;
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
                EditorState.Instance.NotifyStateMachineChanged();
            }

            // Base layer: show info messages instead of disabled controls
            if (isBaseLayer)
            {
                EditorGUILayout.LabelField("Blend Mode", "Override (base layer)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Avatar Mask", "Full body (base layer)", EditorStyles.miniLabel);
            }
            else
            {
                // Blend mode
                EditorGUI.BeginChangeCheck();
                var newBlendMode = EnumPopupCache.LayerBlendModePopup("Blend Mode", layer.BlendMode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(layer, "Change Layer Blend Mode");
                    layer.BlendMode = newBlendMode;
                    EditorUtility.SetDirty(layer);
                    EditorState.Instance.NotifyStateMachineChanged();
                }
                
                // Avatar Mask with quick-create button
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
                    EditorState.Instance.NotifyStateMachineChanged();
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
                        EditorState.Instance.NotifyStateMachineChanged();
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                if (layer.AvatarMask == null)
                {
                    EditorGUILayout.LabelField("No mask = full body (click + to create)", EditorStyles.miniLabel);
                }
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
            EditorState.Instance.NotifyLayerAdded(layer);
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
                EditorState.Instance.NotifyLayerRemoved(layer);
            }
        }

        private void EditLayer(LayerStateAsset layer)
        {
            if (layer?.NestedStateMachine == null) return;

            // Notify via callback if available
            model.OnEditLayer?.Invoke(layer);

            // Navigate into layer - find index without allocation
            int layerIndex = 0;
            foreach (var l in model.StateMachine.GetLayers())
            {
                if (l == layer) break;
                layerIndex++;
            }
            EditorState.Instance.EnterLayer(layer, layerIndex);
        }
    }
}
