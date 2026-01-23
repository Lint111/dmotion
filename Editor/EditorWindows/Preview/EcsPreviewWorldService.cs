using System;
using Latios;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Manages an isolated ECS world for preview purposes.
    /// The world contains DMotion and Kinemation systems but is NOT connected
    /// to the player loop - it's updated manually when needed.
    /// </summary>
    internal class EcsPreviewWorldService : IDisposable
    {
        #region Constants
        
        private const string WorldName = "DMotion Preview World";
        
        #endregion
        
        #region State
        
        private LatiosWorld world;
        private bool isInitialized;
        private bool isDisposed;
        
        // Preview entity
        private Entity previewEntity;
        
        // Systems we need to update manually
        private SystemHandle animationStateMachineSystem;
        private SystemHandle blendAnimationStatesSystem;
        private SystemHandle updateAnimationStatesSystem;
        private SystemHandle clipSamplingSystem;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether the world is initialized and ready for preview.
        /// </summary>
        public bool IsInitialized => isInitialized && !isDisposed && world != null && world.IsCreated;
        
        /// <summary>
        /// The preview world's EntityManager.
        /// </summary>
        public EntityManager EntityManager => world?.EntityManager ?? default;
        
        /// <summary>
        /// The preview entity (animated character).
        /// </summary>
        public Entity PreviewEntity => previewEntity;
        
        #endregion
        
        #region Lifecycle
        
        /// <summary>
        /// Creates and initializes the preview world.
        /// </summary>
        public void CreateWorld()
        {
            if (isInitialized)
            {
                Debug.LogWarning("[EcsPreviewWorldService] World already created. Call DestroyWorld first.");
                return;
            }
            
            if (isDisposed)
            {
                Debug.LogError("[EcsPreviewWorldService] Cannot create world - service has been disposed.");
                return;
            }
            
            try
            {
                // Create a Latios World (NOT the default world)
                world = new LatiosWorld(WorldName);
                
                // Get all system types
                var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default);
                
                // Install Unity systems
                BootstrapTools.InjectUnitySystems(systems, world, world.simulationSystemGroup);
                
                // Install Kinemation (required for bone sampling)
                KinemationBootstrap.InstallKinemation(world);
                
                // Install user systems (includes DMotion systems)
                BootstrapTools.InjectUserSystems(systems, world, world.simulationSystemGroup);
                
                // Cache system handles for manual updates
                CacheSystemHandles();
                
                // DO NOT add to player loop - we update manually
                // ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                
                isInitialized = true;
                
                Debug.Log($"[EcsPreviewWorldService] Created preview world: {WorldName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EcsPreviewWorldService] Failed to create world: {e.Message}\n{e.StackTrace}");
                DestroyWorld();
            }
        }
        
        /// <summary>
        /// Destroys the preview world and releases all resources.
        /// </summary>
        public void DestroyWorld()
        {
            if (world != null && world.IsCreated)
            {
                try
                {
                    // Complete any pending jobs
                    world.EntityManager.CompleteAllTrackedJobs();
                    
                    // Destroy the preview entity if it exists
                    if (previewEntity != Entity.Null && world.EntityManager.Exists(previewEntity))
                    {
                        world.EntityManager.DestroyEntity(previewEntity);
                    }
                    
                    // Dispose the world
                    world.Dispose();
                    
                    Debug.Log($"[EcsPreviewWorldService] Destroyed preview world: {WorldName}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EcsPreviewWorldService] Error during world disposal: {e.Message}");
                }
            }
            
            world = null;
            previewEntity = Entity.Null;
            isInitialized = false;
        }
        
        private void CacheSystemHandles()
        {
            if (world == null) return;
            
            // Get handles to the systems we need to update
            // These will be used for targeted updates instead of full world.Update()
            animationStateMachineSystem = world.GetExistingSystem<AnimationStateMachineSystem>();
            blendAnimationStatesSystem = world.GetExistingSystem<BlendAnimationStatesSystem>();
            updateAnimationStatesSystem = world.GetExistingSystem<UpdateAnimationStatesSystem>();
            clipSamplingSystem = world.GetExistingSystem<ClipSamplingSystem>();
        }
        
        #endregion
        
        #region Update
        
        /// <summary>
        /// Updates the preview world with the given delta time.
        /// Call this during preview playback.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update.</param>
        public void Update(float deltaTime)
        {
            if (!IsInitialized) return;
            
            try
            {
                // Update time
                var timeData = new Unity.Core.TimeData(
                    (float)EditorApplication.timeSinceStartup,
                    deltaTime);
                world.SetTime(timeData);
                
                // Update the simulation system group (runs all animation systems)
                world.simulationSystemGroup.Update();
                
                // Complete jobs before accessing results
                world.EntityManager.CompleteAllTrackedJobs();
            }
            catch (Exception e)
            {
                Debug.LogError($"[EcsPreviewWorldService] Error during update: {e.Message}");
            }
        }
        
        /// <summary>
        /// Updates the world for paused preview (dirty check updates).
        /// Only updates if state has changed.
        /// </summary>
        public void UpdateWhilePaused()
        {
            if (!IsInitialized) return;
            
            // For paused updates, we still need to run systems but with zero delta time
            Update(0f);
        }
        
        /// <summary>
        /// Completes all pending jobs. Call before reading entity data.
        /// </summary>
        public void CompleteJobs()
        {
            if (!IsInitialized) return;
            world.EntityManager.CompleteAllTrackedJobs();
        }
        
        #endregion
        
        #region Entity Management
        
        /// <summary>
        /// Creates the preview entity from a state machine blob.
        /// Note: Full entity setup requires additional Kinemation components (skeleton, etc.)
        /// that are typically set up during baking. This method provides basic setup.
        /// </summary>
        /// <param name="stateMachineBlob">The baked state machine blob.</param>
        /// <param name="clipsBlob">The skeleton clip set blob (from Kinemation).</param>
        /// <param name="clipEventsBlob">Optional clip events blob.</param>
        /// <returns>The created entity, or Entity.Null if creation failed.</returns>
        public Entity CreatePreviewEntity(
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<Latios.Kinemation.SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEventsBlob = default)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[EcsPreviewWorldService] Cannot create entity - world not initialized.");
                return Entity.Null;
            }
            
            // Destroy existing preview entity
            if (previewEntity != Entity.Null && world.EntityManager.Exists(previewEntity))
            {
                world.EntityManager.DestroyEntity(previewEntity);
            }
            
            try
            {
                var em = world.EntityManager;
                
                // Create the entity with required components
                previewEntity = em.CreateEntity();
                
                // Add state machine component with all required blob references
                em.AddComponentData(previewEntity, new AnimationStateMachine
                {
                    ClipsBlob = clipsBlob,
                    ClipEventsBlob = clipEventsBlob,
                    StateMachineBlob = stateMachineBlob,
                    CurrentState = new StateMachineStateRef { StateIndex = 0, AnimationStateId = 0 }
                });
                
                // Add animation state buffer and components
                em.AddBuffer<AnimationState>(previewEntity);
                em.AddComponentData(previewEntity, AnimationStateTransition.Null);
                em.AddComponentData(previewEntity, AnimationStateTransitionRequest.Null);
                
                // Add parameter buffers
                em.AddBuffer<FloatParameter>(previewEntity);
                em.AddBuffer<IntParameter>(previewEntity);
                em.AddBuffer<BoolParameter>(previewEntity);
                
                // Add clip samplers buffer (needed for Kinemation)
                em.AddBuffer<ClipSampler>(previewEntity);
                
                // TODO: Additional Kinemation components may be needed:
                // - OptimizedBoneToRoot
                // - OptimizedSkeletonState
                // - etc.
                // These are typically set up during baking from the skeleton asset.
                
                Debug.Log($"[EcsPreviewWorldService] Created preview entity: {previewEntity}");
                return previewEntity;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EcsPreviewWorldService] Failed to create preview entity: {e.Message}");
                return Entity.Null;
            }
        }
        
        /// <summary>
        /// Destroys the current preview entity.
        /// </summary>
        public void DestroyPreviewEntity()
        {
            if (!IsInitialized) return;
            
            if (previewEntity != Entity.Null && world.EntityManager.Exists(previewEntity))
            {
                world.EntityManager.DestroyEntity(previewEntity);
                previewEntity = Entity.Null;
            }
        }
        
        #endregion
        
        #region Parameters
        
        /// <summary>
        /// Sets a float parameter on the preview entity.
        /// </summary>
        public void SetFloatParameter(int parameterHash, float value)
        {
            if (!IsInitialized || previewEntity == Entity.Null) return;
            
            var buffer = world.EntityManager.GetBuffer<FloatParameter>(previewEntity);
            buffer.SetValue(parameterHash, value);
        }
        
        /// <summary>
        /// Sets an int parameter on the preview entity.
        /// </summary>
        public void SetIntParameter(int parameterHash, int value)
        {
            if (!IsInitialized || previewEntity == Entity.Null) return;
            
            var buffer = world.EntityManager.GetBuffer<IntParameter>(previewEntity);
            buffer.SetValue(parameterHash, value);
        }
        
        /// <summary>
        /// Sets a bool parameter on the preview entity.
        /// </summary>
        public void SetBoolParameter(int parameterHash, bool value)
        {
            if (!IsInitialized || previewEntity == Entity.Null) return;
            
            var buffer = world.EntityManager.GetBuffer<BoolParameter>(previewEntity);
            buffer.SetValue(parameterHash, value);
        }
        
        #endregion
        
        #region Snapshot
        
        /// <summary>
        /// Gets a snapshot of the current preview state for UI display.
        /// </summary>
        public PreviewSnapshot GetSnapshot()
        {
            if (!IsInitialized || previewEntity == Entity.Null)
            {
                return new PreviewSnapshot
                {
                    IsInitialized = false,
                    ErrorMessage = "Preview world not initialized"
                };
            }
            
            try
            {
                var em = world.EntityManager;
                
                if (!em.Exists(previewEntity))
                {
                    return new PreviewSnapshot
                    {
                        IsInitialized = false,
                        ErrorMessage = "Preview entity no longer exists"
                    };
                }
                
                var stateMachine = em.GetComponentData<AnimationStateMachine>(previewEntity);
                
                // Get current state time and weights from AnimationState buffer
                float currentTime = 0f;
                float[] weights = null;
                if (em.HasBuffer<AnimationState>(previewEntity))
                {
                    var states = em.GetBuffer<AnimationState>(previewEntity);
                    if (states.Length > 0)
                    {
                        weights = new float[states.Length];
                        for (int i = 0; i < states.Length; i++)
                        {
                            weights[i] = states[i].Weight;
                            
                            // Get time from current state
                            if (states[i].Id == stateMachine.CurrentState.AnimationStateId)
                            {
                                currentTime = states[i].Time;
                            }
                        }
                    }
                }
                
                // Check for active transition
                float transitionProgress = -1f;
                if (em.HasComponent<AnimationStateTransition>(previewEntity))
                {
                    var activeTransition = em.GetComponentData<AnimationStateTransition>(previewEntity);
                    if (activeTransition.IsValid && activeTransition.TransitionDuration > 0)
                    {
                        // Find the transitioning state to get progress
                        if (em.HasBuffer<AnimationState>(previewEntity))
                        {
                            var states = em.GetBuffer<AnimationState>(previewEntity);
                            for (int i = 0; i < states.Length; i++)
                            {
                                if (states[i].Id == activeTransition.AnimationStateId)
                                {
                                    transitionProgress = states[i].Time / activeTransition.TransitionDuration;
                                    transitionProgress = Unity.Mathematics.math.saturate(transitionProgress);
                                    break;
                                }
                            }
                        }
                    }
                }
                
                return new PreviewSnapshot
                {
                    IsInitialized = true,
                    NormalizedTime = currentTime,
                    BlendWeights = weights,
                    TransitionProgress = transitionProgress,
                    IsPlaying = true // ECS is always "playing" when updated
                };
            }
            catch (Exception e)
            {
                return new PreviewSnapshot
                {
                    IsInitialized = false,
                    ErrorMessage = $"Error reading state: {e.Message}"
                };
            }
        }
        
        #endregion
        
        #region Sampler Extraction
        
        /// <summary>
        /// Extracted clip sampler data for driving external rendering.
        /// </summary>
        public struct ExtractedSampler
        {
            public ushort ClipIndex;
            public float Time;
            public float Weight;
        }
        
        /// <summary>
        /// Extracts active clip samplers from the preview entity.
        /// Used for hybrid rendering where ECS drives logic but PlayableGraph samples poses.
        /// </summary>
        /// <returns>Array of active samplers with clip index, time, and weight.</returns>
        public ExtractedSampler[] GetActiveSamplers()
        {
            if (!IsInitialized || previewEntity == Entity.Null)
            {
                return Array.Empty<ExtractedSampler>();
            }
            
            var em = world.EntityManager;
            if (!em.HasBuffer<ClipSampler>(previewEntity))
            {
                return Array.Empty<ExtractedSampler>();
            }
            
            var samplers = em.GetBuffer<ClipSampler>(previewEntity);
            if (samplers.Length == 0)
            {
                return Array.Empty<ExtractedSampler>();
            }
            
            var result = new ExtractedSampler[samplers.Length];
            for (int i = 0; i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                result[i] = new ExtractedSampler
                {
                    ClipIndex = sampler.ClipIndex,
                    Time = sampler.Time,
                    Weight = sampler.Weight
                };
            }
            
            return result;
        }
        
        #endregion
        
        #region Full Preview Entity (All States Pre-Initialized)
        
        /// <summary>
        /// Creates a preview entity with ALL animation states pre-initialized.
        /// This allows previewing any state or transition without waiting for ECS systems.
        /// </summary>
        /// <param name="stateMachineBlob">The baked state machine blob.</param>
        /// <param name="clipsBlob">The skeleton clip set blob.</param>
        /// <param name="clipEventsBlob">Optional clip events blob.</param>
        /// <returns>The created entity, or Entity.Null if creation failed.</returns>
        public Entity CreateFullPreviewEntity(
            BlobAssetReference<StateMachineBlob> stateMachineBlob,
            BlobAssetReference<SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEventsBlob = default)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[EcsPreviewWorldService] Cannot create entity - world not initialized.");
                return Entity.Null;
            }
            
            // Destroy existing preview entity
            if (previewEntity != Entity.Null && world.EntityManager.Exists(previewEntity))
            {
                world.EntityManager.DestroyEntity(previewEntity);
            }
            
            try
            {
                var em = world.EntityManager;
                ref var smBlob = ref stateMachineBlob.Value;
                
                // Create the entity
                previewEntity = em.CreateEntity();
                
                // Add state machine component
                em.AddComponentData(previewEntity, new AnimationStateMachine
                {
                    ClipsBlob = clipsBlob,
                    ClipEventsBlob = clipEventsBlob,
                    StateMachineBlob = stateMachineBlob,
                    CurrentState = new StateMachineStateRef { StateIndex = 0, AnimationStateId = 0 }
                });
                
                // Add required components
                em.AddComponentData(previewEntity, AnimationStateTransition.Null);
                em.AddComponentData(previewEntity, AnimationStateTransitionRequest.Null);
                em.AddComponentData(previewEntity, AnimationCurrentState.Null);
                
                // Add parameter buffers
                em.AddBuffer<FloatParameter>(previewEntity);
                em.AddBuffer<IntParameter>(previewEntity);
                em.AddBuffer<BoolParameter>(previewEntity);
                
                // Add animation state and sampler buffers
                var animationStates = em.AddBuffer<AnimationState>(previewEntity);
                var clipSamplers = em.AddBuffer<ClipSampler>(previewEntity);
                
                // Pre-create ALL animation states and their samplers
                int stateCount = smBlob.States.Length;
                byte nextSamplerId = 0;
                
                for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
                {
                    ref var stateBlob = ref smBlob.States[stateIndex];
                    
                    // Determine clip count and indices based on state type
                    int clipCount = GetClipCount(ref smBlob, ref stateBlob);
                    
                    // Create AnimationState entry
                    var animState = new AnimationState
                    {
                        Id = (byte)stateIndex,
                        Time = 0f,
                        Weight = stateIndex == 0 ? 1f : 0f, // Default state gets weight 1
                        Speed = stateBlob.Speed,
                        Loop = stateBlob.Loop,
                        StartSamplerId = nextSamplerId,
                        ClipCount = (byte)clipCount
                    };
                    animationStates.Add(animState);
                    
                    // Create ClipSampler entries for this state
                    CreateSamplersForState(ref smBlob, ref stateBlob, stateIndex, clipSamplers, clipsBlob, clipEventsBlob, ref nextSamplerId);
                }
                
                Debug.Log($"[EcsPreviewWorldService] Created full preview entity with {stateCount} states, {clipSamplers.Length} samplers");
                return previewEntity;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EcsPreviewWorldService] Failed to create full preview entity: {e.Message}\n{e.StackTrace}");
                return Entity.Null;
            }
        }
        
        private int GetClipCount(ref StateMachineBlob smBlob, ref AnimationStateBlob stateBlob)
        {
            switch (stateBlob.Type)
            {
                case StateType.Single:
                    return 1;
                    
                case StateType.LinearBlend:
                    ref var linearState = ref smBlob.LinearBlendStates[stateBlob.StateIndex];
                    return linearState.SortedClipIndexes.Length;
                    
                case StateType.Directional2DBlend:
                    ref var dir2DState = ref smBlob.Directional2DBlendStates[stateBlob.StateIndex];
                    return dir2DState.ClipIndexes.Length;
                    
                default:
                    return 1;
            }
        }
        
        private void CreateSamplersForState(
            ref StateMachineBlob smBlob,
            ref AnimationStateBlob stateBlob,
            int stateIndex,
            DynamicBuffer<ClipSampler> samplers,
            BlobAssetReference<SkeletonClipSetBlob> clipsBlob,
            BlobAssetReference<ClipEventsBlob> clipEventsBlob,
            ref byte nextSamplerId)
        {
            switch (stateBlob.Type)
            {
                case StateType.Single:
                {
                    ref var singleState = ref smBlob.SingleClipStates[stateBlob.StateIndex];
                    samplers.Add(new ClipSampler
                    {
                        Id = nextSamplerId++,
                        Clips = clipsBlob,
                        ClipEventsBlob = clipEventsBlob,
                        ClipIndex = singleState.ClipIndex,
                        Time = 0f,
                        PreviousTime = 0f,
                        Weight = stateIndex == 0 ? 1f : 0f
                    });
                    break;
                }
                
                case StateType.LinearBlend:
                {
                    ref var linearState = ref smBlob.LinearBlendStates[stateBlob.StateIndex];
                    for (int i = 0; i < linearState.SortedClipIndexes.Length; i++)
                    {
                        samplers.Add(new ClipSampler
                        {
                            Id = nextSamplerId++,
                            Clips = clipsBlob,
                            ClipEventsBlob = clipEventsBlob,
                            ClipIndex = (ushort)linearState.SortedClipIndexes[i],
                            Time = 0f,
                            PreviousTime = 0f,
                            Weight = 0f // Will be set by blend logic
                        });
                    }
                    break;
                }
                
                case StateType.Directional2DBlend:
                {
                    ref var dir2DState = ref smBlob.Directional2DBlendStates[stateBlob.StateIndex];
                    for (int i = 0; i < dir2DState.ClipIndexes.Length; i++)
                    {
                        samplers.Add(new ClipSampler
                        {
                            Id = nextSamplerId++,
                            Clips = clipsBlob,
                            ClipEventsBlob = clipEventsBlob,
                            ClipIndex = (ushort)dir2DState.ClipIndexes[i],
                            Time = 0f,
                            PreviousTime = 0f,
                            Weight = 0f // Will be set by blend logic
                        });
                    }
                    break;
                }
            }
        }
        
        /// <summary>
        /// Sets up preview for a single state (no transition).
        /// </summary>
        /// <param name="stateIndex">Index of the state to preview.</param>
        /// <param name="normalizedTime">Normalized playback time (0-1).</param>
        public void SetPreviewState(int stateIndex, float normalizedTime)
        {
            if (!IsInitialized || previewEntity == Entity.Null) return;
            
            var em = world.EntityManager;
            if (!em.HasBuffer<AnimationState>(previewEntity)) return;
            
            var states = em.GetBuffer<AnimationState>(previewEntity);
            var samplers = em.GetBuffer<ClipSampler>(previewEntity);
            
            // Set weights: target state = 1, others = 0
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                state.Weight = (state.Id == stateIndex) ? 1f : 0f;
                
                if (state.Id == stateIndex)
                {
                    // Set time on target state's samplers
                    SetStateSamplerTimes(samplers, state, normalizedTime);
                }
                
                states[i] = state;
            }
            
            // Clear any active transition
            em.SetComponentData(previewEntity, AnimationStateTransition.Null);
        }
        
        /// <summary>
        /// Sets up preview for a transition between two states.
        /// </summary>
        /// <param name="fromStateIndex">Index of the source state.</param>
        /// <param name="toStateIndex">Index of the target state.</param>
        /// <param name="progress">Transition progress (0 = fully from, 1 = fully to).</param>
        /// <param name="fromNormalizedTime">Normalized time in from-state.</param>
        /// <param name="toNormalizedTime">Normalized time in to-state.</param>
        public void SetPreviewTransition(int fromStateIndex, int toStateIndex, float progress, float fromNormalizedTime, float toNormalizedTime)
        {
            if (!IsInitialized || previewEntity == Entity.Null) return;
            
            var em = world.EntityManager;
            if (!em.HasBuffer<AnimationState>(previewEntity)) return;
            
            var states = em.GetBuffer<AnimationState>(previewEntity);
            var samplers = em.GetBuffer<ClipSampler>(previewEntity);
            
            float fromWeight = 1f - progress;
            float toWeight = progress;
            
            // Set weights and times
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                
                if (state.Id == fromStateIndex)
                {
                    state.Weight = fromWeight;
                    SetStateSamplerTimes(samplers, state, fromNormalizedTime);
                    SetStateSamplerWeights(samplers, state, fromWeight);
                }
                else if (state.Id == toStateIndex)
                {
                    state.Weight = toWeight;
                    SetStateSamplerTimes(samplers, state, toNormalizedTime);
                    SetStateSamplerWeights(samplers, state, toWeight);
                }
                else
                {
                    state.Weight = 0f;
                }
                
                states[i] = state;
            }
        }
        
        private void SetStateSamplerTimes(DynamicBuffer<ClipSampler> samplers, AnimationState state, float normalizedTime)
        {
            for (int i = 0; i < state.ClipCount; i++)
            {
                int samplerIndex = samplers.IdToIndex((byte)(state.StartSamplerId + i));
                if (samplerIndex < 0) continue;
                
                var sampler = samplers[samplerIndex];
                float clipDuration = sampler.Clips.Value.clips[sampler.ClipIndex].duration;
                sampler.PreviousTime = sampler.Time;
                sampler.Time = normalizedTime * clipDuration;
                samplers[samplerIndex] = sampler;
            }
        }
        
        private void SetStateSamplerWeights(DynamicBuffer<ClipSampler> samplers, AnimationState state, float stateWeight)
        {
            // For single clip states, weight = stateWeight
            // For blend states, individual clip weights should be calculated separately
            // This is a simplified version - blend weights need blend parameter calculation
            
            if (state.ClipCount == 1)
            {
                int samplerIndex = samplers.IdToIndex(state.StartSamplerId);
                if (samplerIndex >= 0)
                {
                    var sampler = samplers[samplerIndex];
                    sampler.Weight = stateWeight;
                    samplers[samplerIndex] = sampler;
                }
            }
            // For blend states, weights are handled separately via SetBlendWeights
        }
        
        /// <summary>
        /// Sets blend weights for a linear blend state.
        /// </summary>
        /// <param name="stateIndex">Index of the blend state.</param>
        /// <param name="blendParameter">Current blend parameter value.</param>
        /// <param name="stateWeight">Overall weight of this state (for transitions).</param>
        public void SetLinearBlendWeights(int stateIndex, float blendParameter, float stateWeight)
        {
            if (!IsInitialized || previewEntity == Entity.Null) return;
            
            var em = world.EntityManager;
            var sm = em.GetComponentData<AnimationStateMachine>(previewEntity);
            ref var smBlob = ref sm.StateMachineBlob.Value;
            
            if (stateIndex < 0 || stateIndex >= smBlob.States.Length) return;
            ref var stateBlob = ref smBlob.States[stateIndex];
            
            if (stateBlob.Type != StateType.LinearBlend) return;
            
            ref var linearState = ref smBlob.LinearBlendStates[stateBlob.StateIndex];
            
            var states = em.GetBuffer<AnimationState>(previewEntity);
            var samplers = em.GetBuffer<ClipSampler>(previewEntity);
            
            // Find the AnimationState
            int animStateIndex = states.IdToIndex((byte)stateIndex);
            if (animStateIndex < 0) return;
            
            var animState = states[animStateIndex];
            
            // Calculate blend weights using thresholds
            int clipCount = linearState.SortedClipIndexes.Length;
            float[] weights = new float[clipCount];
            CalculateLinearBlendWeights(ref linearState, blendParameter, weights);
            
            // Apply weights to samplers
            for (int i = 0; i < clipCount && i < animState.ClipCount; i++)
            {
                int samplerIndex = samplers.IdToIndex((byte)(animState.StartSamplerId + i));
                if (samplerIndex < 0) continue;
                
                var sampler = samplers[samplerIndex];
                sampler.Weight = weights[i] * stateWeight;
                samplers[samplerIndex] = sampler;
            }
        }
        
        private void CalculateLinearBlendWeights(ref LinearBlendStateBlob linearState, float blendParam, float[] outWeights)
        {
            int count = linearState.SortedClipThresholds.Length;
            if (count == 0) return;
            if (count == 1)
            {
                outWeights[0] = 1f;
                return;
            }
            
            // Clamp to threshold range
            float minThreshold = linearState.SortedClipThresholds[0];
            float maxThreshold = linearState.SortedClipThresholds[count - 1];
            blendParam = Unity.Mathematics.math.clamp(blendParam, minThreshold, maxThreshold);
            
            // Find surrounding thresholds and interpolate
            for (int i = 0; i < count - 1; i++)
            {
                float lowThreshold = linearState.SortedClipThresholds[i];
                float highThreshold = linearState.SortedClipThresholds[i + 1];
                
                if (blendParam >= lowThreshold && blendParam <= highThreshold)
                {
                    float range = highThreshold - lowThreshold;
                    float t = range > 0 ? (blendParam - lowThreshold) / range : 0f;
                    
                    outWeights[i] = 1f - t;
                    outWeights[i + 1] = t;
                    return;
                }
            }
            
            // Fallback: closest clip
            if (blendParam <= minThreshold)
                outWeights[0] = 1f;
            else
                outWeights[count - 1] = 1f;
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            if (isDisposed) return;
            
            DestroyWorld();
            isDisposed = true;
        }
        
        #endregion
    }
}
