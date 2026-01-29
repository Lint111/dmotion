using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Inspector for layers in a multi-layer StateMachineAsset.
    /// Uses three separate containers for automatic grouping:
    /// 1. Base Layer (Layer 0) - non-draggable
    /// 2. Override Layers - ReorderableList for Override blend mode
    /// 3. Additive Layers - ReorderableList for Additive blend mode
    /// </summary>
    internal class LayersInspector : StateMachineInspector<LayersInspectorModel>
    {
        private DockablePanelSection layersSection;
        private bool _needsRefresh = true;

        // Separated layer containers
        private LayerStateAsset _baseLayer;
        private List<LayerStateAsset> _overrideLayers;
        private List<LayerStateAsset> _additiveLayers;

        // ReorderableLists for each blend mode group
        private ReorderableList _overrideReorderableList;
        private ReorderableList _additiveReorderableList;

        // Expansion state tracking (by layer instance ID for stability)
        private Dictionary<int, bool> _expandedStates = new Dictionary<int, bool>();

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
            if (_needsRefresh || _baseLayer == null)
            {
                RefreshLayerCache();
                _needsRefresh = false;
            }

            if (_baseLayer == null)
            {
                EditorGUILayout.HelpBox("No layers defined. This shouldn't happen in multi-layer mode.", MessageType.Warning);
                return;
            }

            // Draw Base Layer (non-draggable)
            EditorGUILayout.LabelField("Base Layer", EditorStyles.boldLabel);
            DrawBaseLayerElement();
            EditorGUILayout.Space(10);

            // Draw Override Layers
            if (_overrideReorderableList != null && _overrideLayers.Count > 0)
            {
                _overrideReorderableList.DoLayoutList();
                EditorGUILayout.Space(10);
            }
            else if (_overrideLayers != null && _overrideLayers.Count == 0)
            {
                EditorGUILayout.LabelField("Override Layers", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("No override layers. New layers default to Override mode.", MessageType.None);
                EditorGUILayout.Space(10);
            }

            // Draw Additive Layers
            if (_additiveReorderableList != null && _additiveLayers.Count > 0)
            {
                _additiveReorderableList.DoLayoutList();
            }
            else if (_additiveLayers != null && _additiveLayers.Count == 0)
            {
                EditorGUILayout.LabelField("Additive Layers", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("No additive layers. Change a layer's blend mode to Additive to add it here.", MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshLayerCache()
        {
            var allLayers = new List<LayerStateAsset>();
            foreach (var layer in model.StateMachine.GetLayers())
                allLayers.Add(layer);

            if (allLayers.Count == 0)
            {
                _baseLayer = null;
                return;
            }

            // Layer 0 is always the base layer
            _baseLayer = allLayers[0];

            // Initialize or clear categorized lists
            if (_overrideLayers == null) _overrideLayers = new List<LayerStateAsset>(8);
            else _overrideLayers.Clear();

            if (_additiveLayers == null) _additiveLayers = new List<LayerStateAsset>(8);
            else _additiveLayers.Clear();

            // Categorize layers 1+ by blend mode
            for (int i = 1; i < allLayers.Count; i++)
            {
                var layer = allLayers[i];
                if (layer.BlendMode == LayerBlendMode.Override)
                    _overrideLayers.Add(layer);
                else
                    _additiveLayers.Add(layer);
            }

            InitializeReorderableLists();
        }

        private void InitializeReorderableLists()
        {
            // Override layers list
            _overrideReorderableList = new ReorderableList(_overrideLayers, typeof(LayerStateAsset),
                draggable: true, displayHeader: true, displayAddButton: false, displayRemoveButton: false);

            _overrideReorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, $"Override Layers ({_overrideLayers.Count})", EditorStyles.boldLabel);
            };

            _overrideReorderableList.elementHeightCallback = (int index) =>
            {
                if (index < 0 || index >= _overrideLayers.Count) return EditorGUIUtility.singleLineHeight;
                return CalculateElementHeight(_overrideLayers[index]);
            };

            _overrideReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= 0 && index < _overrideLayers.Count)
                {
                    int actualIndex = GetActualLayerIndex(_overrideLayers[index]);
                    DrawLayerElementInRect(rect, actualIndex, _overrideLayers[index], isActive, isFocused, false);
                }
            };

            _overrideReorderableList.drawElementBackgroundCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (isActive)
                {
                    ReorderableList.defaultBehaviours.DrawElementBackground(rect, index, isActive, isFocused, true);
                }
            };

            _overrideReorderableList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
            {
                MoveLayerWithinBlendGroup(LayerBlendMode.Override, oldIndex, newIndex);
            };

            // Additive layers list
            _additiveReorderableList = new ReorderableList(_additiveLayers, typeof(LayerStateAsset),
                draggable: true, displayHeader: true, displayAddButton: false, displayRemoveButton: false);

            _additiveReorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, $"Additive Layers ({_additiveLayers.Count})", EditorStyles.boldLabel);
            };

            _additiveReorderableList.elementHeightCallback = (int index) =>
            {
                if (index < 0 || index >= _additiveLayers.Count) return EditorGUIUtility.singleLineHeight;
                return CalculateElementHeight(_additiveLayers[index]);
            };

            _additiveReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= 0 && index < _additiveLayers.Count)
                {
                    int actualIndex = GetActualLayerIndex(_additiveLayers[index]);
                    DrawLayerElementInRect(rect, actualIndex, _additiveLayers[index], isActive, isFocused, false);
                }
            };

            _additiveReorderableList.drawElementBackgroundCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (isActive)
                {
                    ReorderableList.defaultBehaviours.DrawElementBackground(rect, index, isActive, isFocused, true);
                }
            };

            _additiveReorderableList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
            {
                MoveLayerWithinBlendGroup(LayerBlendMode.Additive, oldIndex, newIndex);
            };
        }

        private float CalculateElementHeight(LayerStateAsset layer)
        {
            // Base height for header row
            float height = EditorGUIUtility.singleLineHeight + 4f;

            // Add height for expanded properties if shown
            int instanceId = layer.GetInstanceID();
            bool isExpanded = _expandedStates.TryGetValue(instanceId, out bool expanded) && expanded;
            if (isExpanded)
            {
                // Weight slider
                height += EditorGUIUtility.singleLineHeight + 2f;
                // Blend mode, avatar mask, mask hint
                height += (EditorGUIUtility.singleLineHeight + 2f) * 3;
                // State count info
                height += EditorGUIUtility.singleLineHeight + 2f;
            }

            // Padding
            height += 8f;
            return height;
        }

        private int GetActualLayerIndex(LayerStateAsset layer)
        {
            int index = 0;
            foreach (var l in model.StateMachine.GetLayers())
            {
                if (l == layer) return index;
                index++;
            }
            return -1;
        }

        private void MoveLayerWithinBlendGroup(LayerBlendMode blendMode, int fromIndex, int toIndex)
        {
            var groupLayers = blendMode == LayerBlendMode.Override ? _overrideLayers : _additiveLayers;

            if (fromIndex < 0 || fromIndex >= groupLayers.Count) return;
            if (toIndex < 0 || toIndex >= groupLayers.Count) return;
            if (fromIndex == toIndex) return;

            var allLayers = new List<LayerStateAsset>();
            foreach (var l in model.StateMachine.GetLayers())
                allLayers.Add(l);

            // Get actual indices in the States list
            var movingLayer = groupLayers[fromIndex];

            int actualFromIndex = model.StateMachine.States.IndexOf(movingLayer);
            if (actualFromIndex < 0) return;

            // Calculate target position based on the layer we're moving relative to
            var targetLayer = groupLayers[toIndex];
            int actualToIndex = model.StateMachine.States.IndexOf(targetLayer);
            if (actualToIndex < 0) return;

            // Perform the move in the States list
            Undo.RecordObject(model.StateMachine, "Reorder Layer");

            model.StateMachine.States.RemoveAt(actualFromIndex);

            // Recalculate target index after removal
            if (actualFromIndex < actualToIndex)
                actualToIndex--;

            model.StateMachine.States.Insert(actualToIndex, movingLayer);

            EditorUtility.SetDirty(model.StateMachine);
            AssetDatabase.SaveAssets();

            RefreshLayerCache();
            EditorState.Instance.NotifyStateMachineChanged();
        }

        private void DrawLayersToolbar()
        {
            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus"), GUILayout.Width(24), GUILayout.Height(18)))
            {
                AddLayer();
            }
        }

        private void DrawBaseLayerElement()
        {
            if (_baseLayer == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row
            EditorGUILayout.BeginHorizontal();

            // Lock icon + index
            var lockIcon = EditorGUIUtility.IconContent("d_AssemblyLock");
            GUILayout.Label(new GUIContent("0", lockIcon.image, "Base layer cannot be moved"),
                EditorStyleCache.IndexBold, GUILayout.Width(24));

            // Foldout
            int instanceId = _baseLayer.GetInstanceID();
            bool wasExpanded = _expandedStates.TryGetValue(instanceId, out bool expanded) && expanded;
            bool isExpanded = EditorGUILayout.Foldout(wasExpanded, "", true);
            if (isExpanded != wasExpanded)
            {
                _expandedStates[instanceId] = isExpanded;
            }

            // Layer name
            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.TextField(_baseLayer.name);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_baseLayer, "Rename Layer");
                _baseLayer.name = newName;
                EditorUtility.SetDirty(_baseLayer);
            }

            // Edit button
            if (GUILayout.Button("Edit", GUILayout.Width(40)))
            {
                EditLayer(_baseLayer);
            }

            // Delete button (disabled - can't delete base layer when it's the only layer)
            EditorGUI.BeginDisabledGroup(model.StateMachine.LayerCount <= 1);
            if (GUILayout.Button("x", GUILayout.Width(20)))
            {
                RemoveLayer(_baseLayer);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Expanded content
            if (isExpanded)
            {
                EditorGUI.indentLevel++;

                // Weight slider
                EditorGUI.BeginChangeCheck();
                var newWeight = EditorGUILayout.Slider("Weight", _baseLayer.Weight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_baseLayer, "Change Layer Weight");
                    _baseLayer.Weight = newWeight;
                    EditorUtility.SetDirty(_baseLayer);
                    EditorState.Instance.NotifyStateMachineChanged();
                }

                // Info labels for base layer (read-only)
                EditorGUILayout.LabelField("Blend Mode", "Override (base layer)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Avatar Mask", "Full body (base layer)", EditorStyles.miniLabel);

                // State count info
                var stateCount = _baseLayer.NestedStateMachine != null ? _baseLayer.NestedStateMachine.States.Count : 0;
                EditorGUILayout.LabelField($"States: {stateCount}", EditorStyles.miniLabel);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLayerElementInRect(Rect rect, int actualIndex, LayerStateAsset layer, bool isActive, bool isFocused, bool isBaseLayer)
        {
            if (layer == null) return;

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float padding = 2f;
            float yOffset = rect.y + padding;

            // Indent for drag handle
            float xOffset = rect.x + 4f;
            float availableWidth = rect.width - 8f;

            // Header row: index, foldout, name, buttons
            Rect headerRect = new Rect(xOffset, yOffset, availableWidth, lineHeight);

            // Index badge
            float indexWidth = 24f;
            Rect indexRect = new Rect(headerRect.x, headerRect.y, indexWidth, lineHeight);
            GUI.Label(indexRect, actualIndex.ToString(), EditorStyleCache.IndexNormal);

            // Foldout for expand/collapse
            float foldoutWidth = 16f;
            Rect foldoutRect = new Rect(indexRect.xMax, headerRect.y, foldoutWidth, lineHeight);
            int instanceId = layer.GetInstanceID();
            bool wasExpanded = _expandedStates.TryGetValue(instanceId, out bool expanded) && expanded;
            bool isExpanded = EditorGUI.Foldout(foldoutRect, wasExpanded, GUIContent.none, true);
            if (isExpanded != wasExpanded)
            {
                _expandedStates[instanceId] = isExpanded;
            }

            // Layer name field
            float buttonWidth = 44f;
            float deleteButtonWidth = 22f;
            float nameWidth = availableWidth - indexWidth - foldoutWidth - buttonWidth - deleteButtonWidth - 12f;
            Rect nameRect = new Rect(foldoutRect.xMax + 2f, headerRect.y, nameWidth, lineHeight);

            EditorGUI.BeginChangeCheck();
            var newName = EditorGUI.TextField(nameRect, layer.name);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layer, "Rename Layer");
                layer.name = newName;
                EditorUtility.SetDirty(layer);
            }

            // Edit button
            Rect editRect = new Rect(nameRect.xMax + 4f, headerRect.y, buttonWidth, lineHeight);
            if (GUI.Button(editRect, "Edit"))
            {
                EditLayer(layer);
            }

            // Delete button
            Rect deleteRect = new Rect(editRect.xMax + 2f, headerRect.y, deleteButtonWidth, lineHeight);
            EditorGUI.BeginDisabledGroup(model.StateMachine.LayerCount <= 1);
            if (GUI.Button(deleteRect, "x"))
            {
                RemoveLayer(layer);
            }
            EditorGUI.EndDisabledGroup();

            yOffset += lineHeight + padding;

            // Expanded content
            if (isExpanded)
            {
                float fieldWidth = availableWidth - 20f;

                // Weight slider
                Rect weightRect = new Rect(xOffset + 10f, yOffset, fieldWidth, lineHeight);
                EditorGUI.BeginChangeCheck();
                var newWeight = EditorGUI.Slider(weightRect, "Weight", layer.Weight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(layer, "Change Layer Weight");
                    layer.Weight = newWeight;
                    EditorUtility.SetDirty(layer);
                    EditorState.Instance.NotifyStateMachineChanged();
                }
                yOffset += lineHeight + padding;

                // Blend mode popup - changing this moves layer to different container
                Rect blendRect = new Rect(xOffset + 10f, yOffset, fieldWidth, lineHeight);
                EditorGUI.BeginChangeCheck();
                var newBlendMode = (LayerBlendMode)EditorGUI.EnumPopup(blendRect, "Blend Mode", layer.BlendMode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(layer, "Change Layer Blend Mode");
                    layer.BlendMode = newBlendMode;
                    EditorUtility.SetDirty(layer);
                    EditorState.Instance.NotifyStateMachineChanged();
                    // Trigger refresh to move layer to correct container
                    _needsRefresh = true;
                }
                yOffset += lineHeight + padding;

                // Avatar Mask field with quick-create button
                float createButtonWidth = 22f;
                Rect maskRect = new Rect(xOffset + 10f, yOffset, fieldWidth - createButtonWidth - 4f, lineHeight);
                EditorGUI.BeginChangeCheck();
                var newMask = (AvatarMask)EditorGUI.ObjectField(maskRect, "Avatar Mask", layer.AvatarMask, typeof(AvatarMask), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(layer, "Change Layer Avatar Mask");
                    layer.AvatarMask = newMask;
                    EditorUtility.SetDirty(layer);
                    EditorState.Instance.NotifyStateMachineChanged();
                }

                Rect createMaskRect = new Rect(maskRect.xMax + 2f, yOffset, createButtonWidth, lineHeight);
                if (GUI.Button(createMaskRect, new GUIContent("+", "Create new Avatar Mask")))
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
                yOffset += lineHeight + padding;

                // Mask hint
                Rect maskHintRect = new Rect(xOffset + 10f, yOffset, fieldWidth, lineHeight);
                if (layer.AvatarMask == null)
                {
                    EditorGUI.LabelField(maskHintRect, "No mask = full body (click + to create)", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUI.LabelField(maskHintRect, "", EditorStyles.miniLabel);
                }
                yOffset += lineHeight + padding;

                // State count info
                var stateCount = layer.NestedStateMachine != null ? layer.NestedStateMachine.States.Count : 0;
                Rect stateCountRect = new Rect(xOffset + 10f, yOffset, fieldWidth, lineHeight);
                EditorGUI.LabelField(stateCountRect, $"States: {stateCount}", EditorStyles.miniLabel);
            }
        }

        private void AddLayer()
        {
            Undo.RecordObject(model.StateMachine, "Add Layer");
            var layer = model.StateMachine.AddLayer();
            EditorState.Instance.NotifyLayerAdded(layer);
            _needsRefresh = true;
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
                _needsRefresh = true;
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
