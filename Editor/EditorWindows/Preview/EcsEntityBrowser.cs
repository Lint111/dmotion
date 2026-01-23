using System;
using System.Collections.Generic;
using DMotion.Authoring;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Browses and inspects ECS entities with AnimationStateMachine components.
    /// Allows viewing and modifying animation state in loaded SubScenes.
    /// </summary>
    internal class EcsEntityBrowser : IDisposable
    {
        #region Nested Types
        
        /// <summary>
        /// Cached info about an animation entity.
        /// </summary>
        public struct AnimationEntityInfo
        {
            public Entity Entity;
            public World World;
            public string Name;
            public int StateIndex;
            public string StateName;
            public bool IsTransitioning;
            public float NormalizedTime;
        }
        
        #endregion
        
        #region State
        
        private List<AnimationEntityInfo> cachedEntities = new();
        private Entity selectedEntity = Entity.Null;
        private World selectedWorld;
        private Vector2 entityListScroll;
        private Vector2 inspectorScroll;
        private double lastRefreshTime;
        private const double RefreshInterval = 0.5; // Refresh entity list every 0.5s
        
        // Parameter modification state
        private Dictionary<int, float> floatParamValues = new();
        private Dictionary<int, int> intParamValues = new();
        private Dictionary<int, bool> boolParamValues = new();
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The currently selected entity.
        /// </summary>
        public Entity SelectedEntity => selectedEntity;
        
        /// <summary>
        /// Whether an entity is selected.
        /// </summary>
        public bool HasSelection => selectedEntity != Entity.Null && selectedWorld != null;
        
        /// <summary>
        /// The world containing the selected entity.
        /// </summary>
        public World SelectedWorld => selectedWorld;
        
        /// <summary>
        /// Number of animation entities found.
        /// </summary>
        public int EntityCount => cachedEntities.Count;
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Refreshes the list of animation entities from all worlds.
        /// </summary>
        public void RefreshEntityList()
        {
            cachedEntities.Clear();
            
            // Check all worlds for animation entities
            foreach (var world in World.All)
            {
                if (world == null || !world.IsCreated) continue;
                
                // Skip certain world types
                if (world.Name.Contains("Preview") || 
                    world.Name.Contains("Conversion") ||
                    world.Name.Contains("Staging"))
                {
                    continue;
                }
                
                QueryAnimationEntities(world);
            }
            
            lastRefreshTime = EditorApplication.timeSinceStartup;
        }
        
        /// <summary>
        /// Selects an entity for inspection.
        /// </summary>
        public void SelectEntity(Entity entity, World world)
        {
            selectedEntity = entity;
            selectedWorld = world;
            
            // Cache current parameter values
            CacheParameterValues();
        }
        
        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection()
        {
            selectedEntity = Entity.Null;
            selectedWorld = null;
            floatParamValues.Clear();
            intParamValues.Clear();
            boolParamValues.Clear();
        }
        
        /// <summary>
        /// Draws the entity browser UI.
        /// </summary>
        public void DrawBrowser(Rect rect)
        {
            // Auto-refresh periodically
            if (EditorApplication.timeSinceStartup - lastRefreshTime > RefreshInterval)
            {
                RefreshEntityList();
            }
            
            GUILayout.BeginArea(rect);
            
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"Animation Entities ({cachedEntities.Count})", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RefreshEntityList();
            }
            EditorGUILayout.EndHorizontal();
            
            if (cachedEntities.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No animation entities found.\n\n" +
                    "To see entities:\n" +
                    "1. Open a scene with a SubScene\n" +
                    "2. Enter Play mode, or\n" +
                    "3. Enable 'Live Conversion' in SubScene",
                    MessageType.Info);
            }
            else
            {
                // Entity list
                entityListScroll = EditorGUILayout.BeginScrollView(entityListScroll);
                
                foreach (var info in cachedEntities)
                {
                    bool isSelected = info.Entity == selectedEntity && info.World == selectedWorld;
                    
                    var style = isSelected ? 
                        new GUIStyle(EditorStyles.selectionRect) : 
                        new GUIStyle(EditorStyles.label);
                    
                    EditorGUILayout.BeginHorizontal(style);
                    
                    // Entity name and state
                    string displayText = $"{info.Name}";
                    if (!string.IsNullOrEmpty(info.StateName))
                    {
                        displayText += $" [{info.StateName}]";
                    }
                    if (info.IsTransitioning)
                    {
                        displayText += " (transitioning)";
                    }
                    
                    if (GUILayout.Button(displayText, EditorStyles.label))
                    {
                        SelectEntity(info.Entity, info.World);
                    }
                    
                    // World indicator
                    GUILayout.Label(info.World.Name, EditorStyles.miniLabel, GUILayout.Width(100));
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.EndScrollView();
            }
            
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// Draws the entity inspector UI.
        /// </summary>
        public void DrawInspector(Rect rect)
        {
            GUILayout.BeginArea(rect);
            
            if (!HasSelection)
            {
                EditorGUILayout.HelpBox("Select an entity to inspect", MessageType.Info);
                GUILayout.EndArea();
                return;
            }
            
            // Validate entity still exists
            if (!selectedWorld.IsCreated || !selectedWorld.EntityManager.Exists(selectedEntity))
            {
                ClearSelection();
                EditorGUILayout.HelpBox("Selected entity no longer exists", MessageType.Warning);
                GUILayout.EndArea();
                return;
            }
            
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            
            var em = selectedWorld.EntityManager;
            
            // Entity header
            EditorGUILayout.LabelField("Entity", selectedEntity.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("World", selectedWorld.Name);
            EditorGUILayout.Space();
            
            // State Machine info
            if (em.HasComponent<AnimationStateMachine>(selectedEntity))
            {
                DrawStateMachineInfo(em);
            }
            
            EditorGUILayout.Space();
            
            // Parameters
            DrawParameters(em);
            
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region Private - Entity Query
        
        private void QueryAnimationEntities(World world)
        {
            if (!world.IsCreated) return;
            
            var em = world.EntityManager;
            
            // Query for entities with AnimationStateMachine
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<AnimationStateMachine>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            foreach (var entity in entities)
            {
                var info = new AnimationEntityInfo
                {
                    Entity = entity,
                    World = world,
                    Name = GetEntityName(em, entity),
                    StateIndex = -1,
                    StateName = "",
                    IsTransitioning = false,
                    NormalizedTime = 0
                };
                
                // Get state machine info
                if (em.HasComponent<AnimationStateMachine>(entity))
                {
                    var sm = em.GetComponentData<AnimationStateMachine>(entity);
                    info.StateIndex = sm.CurrentState.StateIndex;
                    
                    // Try to get state name from debug component
                    if (em.HasComponent<AnimationStateMachineDebug>(entity))
                    {
                        var debug = em.GetComponentObject<AnimationStateMachineDebug>(entity);
                        if (debug?.StateMachineAsset != null && 
                            info.StateIndex >= 0 && 
                            info.StateIndex < debug.StateMachineAsset.States.Count)
                        {
                            info.StateName = debug.StateMachineAsset.States[info.StateIndex]?.name ?? "";
                        }
                    }
                }
                
                // Check for transition
                if (em.HasComponent<AnimationStateTransition>(entity))
                {
                    var transition = em.GetComponentData<AnimationStateTransition>(entity);
                    info.IsTransitioning = transition.IsValid;
                }
                
                cachedEntities.Add(info);
            }
        }
        
        private string GetEntityName(EntityManager em, Entity entity)
        {
            // Try to get name from debug component
            if (em.HasComponent<AnimationStateMachineDebug>(entity))
            {
                var debug = em.GetComponentObject<AnimationStateMachineDebug>(entity);
                if (debug?.StateMachineAsset != null)
                {
                    return debug.StateMachineAsset.name;
                }
            }
            
            return $"Entity {entity.Index}:{entity.Version}";
        }
        
        #endregion
        
        #region Private - Inspector Drawing
        
        private void DrawStateMachineInfo(EntityManager em)
        {
            EditorGUILayout.LabelField("State Machine", EditorStyles.boldLabel);
            
            var sm = em.GetComponentData<AnimationStateMachine>(selectedEntity);
            
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Current State Index", sm.CurrentState.StateIndex.ToString());
            
            // Get state name if available
            if (em.HasComponent<AnimationStateMachineDebug>(selectedEntity))
            {
                var debug = em.GetComponentObject<AnimationStateMachineDebug>(selectedEntity);
                if (debug?.StateMachineAsset != null)
                {
                    EditorGUILayout.LabelField("Asset", debug.StateMachineAsset.name);
                    
                    if (sm.CurrentState.StateIndex >= 0 && 
                        sm.CurrentState.StateIndex < debug.StateMachineAsset.States.Count)
                    {
                        var state = debug.StateMachineAsset.States[sm.CurrentState.StateIndex];
                        EditorGUILayout.LabelField("State Name", state?.name ?? "(null)");
                    }
                }
            }
            
            // Transition info
            if (em.HasComponent<AnimationStateTransition>(selectedEntity))
            {
                var transition = em.GetComponentData<AnimationStateTransition>(selectedEntity);
                if (transition.IsValid)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("To State", transition.AnimationStateId.ToString());
                    EditorGUILayout.LabelField("Duration", $"{transition.TransitionDuration:F2}s");
                }
            }
            
            // Clip samplers
            if (em.HasBuffer<ClipSampler>(selectedEntity))
            {
                var samplers = em.GetBuffer<ClipSampler>(selectedEntity);
                if (samplers.Length > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField($"Active Clips ({samplers.Length})", EditorStyles.boldLabel);
                    
                    for (int i = 0; i < samplers.Length; i++)
                    {
                        var sampler = samplers[i];
                        EditorGUILayout.LabelField($"  Clip {sampler.ClipIndex}", 
                            $"Time: {sampler.Time:F2}, Weight: {sampler.Weight:F2}");
                    }
                }
            }
            
            EditorGUI.indentLevel--;
        }
        
        private void DrawParameters(EntityManager em)
        {
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            
            bool anyParams = false;
            
            // Float parameters
            if (em.HasBuffer<FloatParameter>(selectedEntity))
            {
                var buffer = em.GetBuffer<FloatParameter>(selectedEntity);
                if (buffer.Length > 0)
                {
                    anyParams = true;
                    EditorGUILayout.LabelField("Float Parameters");
                    EditorGUI.indentLevel++;
                    
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        var param = buffer[i];
                        
                        if (!floatParamValues.TryGetValue(i, out float currentValue))
                        {
                            currentValue = param.Value;
                            floatParamValues[i] = currentValue;
                        }
                        
                        EditorGUI.BeginChangeCheck();
                        float newValue = EditorGUILayout.FloatField($"[{i}] Hash: {param.Hash}", currentValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            floatParamValues[i] = newValue;
                            SetFloatParameter(em, i, newValue);
                        }
                    }
                    
                    EditorGUI.indentLevel--;
                }
            }
            
            // Int parameters
            if (em.HasBuffer<IntParameter>(selectedEntity))
            {
                var buffer = em.GetBuffer<IntParameter>(selectedEntity);
                if (buffer.Length > 0)
                {
                    anyParams = true;
                    EditorGUILayout.LabelField("Int Parameters");
                    EditorGUI.indentLevel++;
                    
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        var param = buffer[i];
                        
                        if (!intParamValues.TryGetValue(i, out int currentValue))
                        {
                            currentValue = param.Value;
                            intParamValues[i] = currentValue;
                        }
                        
                        EditorGUI.BeginChangeCheck();
                        int newValue = EditorGUILayout.IntField($"[{i}] Hash: {param.Hash}", currentValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            intParamValues[i] = newValue;
                            SetIntParameter(em, i, newValue);
                        }
                    }
                    
                    EditorGUI.indentLevel--;
                }
            }
            
            // Bool parameters
            if (em.HasBuffer<BoolParameter>(selectedEntity))
            {
                var buffer = em.GetBuffer<BoolParameter>(selectedEntity);
                if (buffer.Length > 0)
                {
                    anyParams = true;
                    EditorGUILayout.LabelField("Bool Parameters");
                    EditorGUI.indentLevel++;
                    
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        var param = buffer[i];
                        
                        if (!boolParamValues.TryGetValue(i, out bool currentValue))
                        {
                            currentValue = param.Value;
                            boolParamValues[i] = currentValue;
                        }
                        
                        EditorGUI.BeginChangeCheck();
                        bool newValue = EditorGUILayout.Toggle($"[{i}] Hash: {param.Hash}", currentValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            boolParamValues[i] = newValue;
                            SetBoolParameter(em, i, newValue);
                        }
                    }
                    
                    EditorGUI.indentLevel--;
                }
            }
            
            if (!anyParams)
            {
                EditorGUILayout.LabelField("No parameters", EditorStyles.miniLabel);
            }
        }
        
        #endregion
        
        #region Private - Parameter Modification
        
        private void CacheParameterValues()
        {
            floatParamValues.Clear();
            intParamValues.Clear();
            boolParamValues.Clear();
            
            if (!HasSelection || !selectedWorld.IsCreated) return;
            
            var em = selectedWorld.EntityManager;
            if (!em.Exists(selectedEntity)) return;
            
            // Cache float params
            if (em.HasBuffer<FloatParameter>(selectedEntity))
            {
                var buffer = em.GetBuffer<FloatParameter>(selectedEntity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    floatParamValues[i] = buffer[i].Value;
                }
            }
            
            // Cache int params
            if (em.HasBuffer<IntParameter>(selectedEntity))
            {
                var buffer = em.GetBuffer<IntParameter>(selectedEntity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    intParamValues[i] = buffer[i].Value;
                }
            }
            
            // Cache bool params
            if (em.HasBuffer<BoolParameter>(selectedEntity))
            {
                var buffer = em.GetBuffer<BoolParameter>(selectedEntity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    boolParamValues[i] = buffer[i].Value;
                }
            }
        }
        
        private void SetFloatParameter(EntityManager em, int index, float value)
        {
            if (!em.HasBuffer<FloatParameter>(selectedEntity)) return;
            
            var buffer = em.GetBuffer<FloatParameter>(selectedEntity);
            if (index < 0 || index >= buffer.Length) return;
            
            var param = buffer[index];
            param.Value = value;
            buffer[index] = param;
        }
        
        private void SetIntParameter(EntityManager em, int index, int value)
        {
            if (!em.HasBuffer<IntParameter>(selectedEntity)) return;
            
            var buffer = em.GetBuffer<IntParameter>(selectedEntity);
            if (index < 0 || index >= buffer.Length) return;
            
            var param = buffer[index];
            param.Value = value;
            buffer[index] = param;
        }
        
        private void SetBoolParameter(EntityManager em, int index, bool value)
        {
            if (!em.HasBuffer<BoolParameter>(selectedEntity)) return;
            
            var buffer = em.GetBuffer<BoolParameter>(selectedEntity);
            if (index < 0 || index >= buffer.Length) return;
            
            var param = buffer[index];
            param.Value = value;
            buffer[index] = param;
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            ClearSelection();
            cachedEntities.Clear();
        }
        
        #endregion
    }
}
