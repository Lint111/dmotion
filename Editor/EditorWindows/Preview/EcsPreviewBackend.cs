using System;
using System.Linq;
using DMotion;
using DMotion.Authoring;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview backend using actual DMotion ECS systems (Runtime mode).
    /// Provides runtime-accurate preview behavior by running animation systems
    /// in an isolated ECS world.
    /// 
    /// Phase 6A: State machine logic validation (no actual animation sampling)
    /// - Creates StateMachineBlob from StateMachineAsset
    /// - Creates stub SkeletonClipSetBlob with correct clip count
    /// - Runs state machine systems to validate transitions/parameters
    /// - Displays state machine state info (no 3D rendering yet)
    /// </summary>
    internal class EcsPreviewBackend : IPreviewBackend
    {
        #region State
        
        private EcsPreviewWorldService worldService;
        private AnimationStateAsset currentState;
        private AnimationStateAsset transitionFromState;
        private AnimationStateAsset transitionToState;
        private float transitionDuration;
        
        // Cached references
        private StateMachineAsset stateMachineAsset;
        private BlobAssetReference<StateMachineBlob> stateMachineBlob;
        private BlobAssetReference<SkeletonClipSetBlob> clipsBlob;
        private BlobAssetReference<ClipEventsBlob> clipEventsBlob;
        
        // Preview state
        private float normalizedTime;
        private float2 blendPosition;
        private float2 targetBlendPosition;
        private float transitionProgress;
        private string errorMessage;
        private bool isInitialized;
        private bool entityCreated;
        
        // Blend smoothing
        private const float BlendSmoothSpeed = 8f; // Higher = faster interpolation
        
        // Playback control for entity browser mode
        private bool isPreviewPlaying = true;
        private bool isPreviewTimeControlled = false;
        private float previewControlledTime = 0f; // Time in seconds when preview controls playback
        
        // Hybrid renderer for 3D preview (legacy - for isolated preview)
        private EcsHybridRenderer hybridRenderer;
        private bool rendererInitialized;
        
        // Entity browser for live entity inspection
        private EcsEntityBrowser entityBrowser;
        private bool useEntityBrowserMode = true; // Default to entity browser mode
        
        // Scene manager for automatic SubScene setup
        private EcsPreviewSceneManager sceneManager;
        
        // Camera state
        private PlayableGraphPreview.CameraState cameraState;
        
        // Preview model
        private GameObject previewModel;
        
        #endregion
        
        #region Constructor
        
        public EcsPreviewBackend()
        {
            Debug.Log("[EcsPreviewBackend] Constructor called - ECS Runtime backend created");
            worldService = new EcsPreviewWorldService();
            entityBrowser = new EcsEntityBrowser();
            sceneManager = EcsPreviewSceneManager.Instance;
        }
        
        #endregion
        
        #region IPreviewBackend Properties
        
        public PreviewMode Mode => PreviewMode.EcsRuntime;
        
        public bool IsInitialized => isInitialized && (useEntityBrowserMode || worldService.IsInitialized);
        
        public string ErrorMessage => errorMessage;
        
        public AnimationStateAsset CurrentState => currentState;
        
        public bool IsTransitionPreview => transitionToState != null;
        
        public PlayableGraphPreview.CameraState CameraState
        {
            get => rendererInitialized ? hybridRenderer.CameraState : cameraState;
            set
            {
                cameraState = value;
                if (rendererInitialized)
                {
                    hybridRenderer.CameraState = value;
                }
            }
        }
        
        #endregion
        
        #region IPreviewBackend Initialization
        
        public void CreatePreviewForState(AnimationStateAsset state)
        {
            Debug.Log($"[EcsPreviewBackend] CreatePreviewForState: {state?.name ?? "null"}, type={state?.GetType().Name ?? "null"}");
            currentState = state;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = null;
            isInitialized = false;
            entityCreated = false;
            
            if (state == null)
            {
                errorMessage = "No state selected";
                return;
            }
            
            // Find the owning StateMachineAsset
            var newStateMachineAsset = FindOwningStateMachine(state);
            if (newStateMachineAsset == null)
            {
                errorMessage = $"Could not find StateMachineAsset\nfor state: {state.name}";
                return;
            }
            
            // Store the state machine for scene setup
            stateMachineAsset = newStateMachineAsset;
            
            // In entity browser mode, we set up the preview scene automatically
            if (useEntityBrowserMode)
            {
                isInitialized = true;
                
                // Auto-setup preview scene if not already set up
                if (!sceneManager.IsSetup)
                {
                    SetupPreviewScene();
                }
                return;
            }
            
            // Legacy isolated preview mode (below)
            // Create world if not already created
            if (!worldService.IsInitialized)
            {
                worldService.CreateWorld();
            }
            
            // Rebuild blobs if state machine changed
            DisposeBlobs();
            
            if (!TryCreateBlobs())
            {
                return;
            }
            
            // Create preview entity
            if (!TryCreatePreviewEntity())
            {
                return;
            }
            
            isInitialized = true;
        }
        
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float duration)
        {
            currentState = null;
            transitionFromState = fromState;
            transitionToState = toState;
            transitionDuration = duration;
            errorMessage = null;
            isInitialized = false;
            entityCreated = false;
            
            if (toState == null)
            {
                errorMessage = "No target state for transition";
                return;
            }
            
            // Find the owning StateMachineAsset
            var newStateMachineAsset = FindOwningStateMachine(toState);
            if (newStateMachineAsset == null)
            {
                errorMessage = $"Could not find StateMachineAsset\nfor state: {toState.name}";
                return;
            }
            
            // Store the state machine for scene setup
            stateMachineAsset = newStateMachineAsset;
            
            // In entity browser mode, handle transition preview on live entities
            if (useEntityBrowserMode)
            {
                isInitialized = true;
                
                // Auto-setup preview scene if not already set up
                if (!sceneManager.IsSetup)
                {
                    SetupPreviewScene();
                }
                
                // If we have a selected entity, trigger a transition on it
                if (entityBrowser.HasSelection)
                {
                    TriggerTransitionOnBrowserEntity();
                }
                return;
            }
            
            // Legacy isolated preview mode (below)
            // Create world if not already created
            if (!worldService.IsInitialized)
            {
                worldService.CreateWorld();
            }
            
            // Rebuild blobs if state machine changed
            DisposeBlobs();
            
            if (!TryCreateBlobs())
            {
                return;
            }
            
            // Create preview entity
            if (!TryCreatePreviewEntity())
            {
                return;
            }
            
            isInitialized = true;
        }
        
        public void SetPreviewModel(GameObject model)
        {
            previewModel = model;
            
            // Initialize or reinitialize the hybrid renderer with the new model
            if (entityCreated)
            {
                TryInitializeRenderer();
            }
        }
        
        public void Clear()
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = null;
            isInitialized = false;
            entityCreated = false;
            
            // Dispose renderer
            hybridRenderer?.Dispose();
            hybridRenderer = null;
            rendererInitialized = false;
            
            worldService.DestroyPreviewEntity();
        }
        
        public void SetMessage(string message)
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = message;
            isInitialized = false;
            entityCreated = false;
        }
        
        #endregion
        
        #region Blob Creation
        
        /// <summary>
        /// Finds the StateMachineAsset that owns the given state.
        /// </summary>
        private StateMachineAsset FindOwningStateMachine(AnimationStateAsset state)
        {
            if (state == null) return null;
            
            // Get the asset path and load all StateMachineAssets in the same file
            var path = UnityEditor.AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path)) return null;
            
            var mainAsset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
            if (mainAsset is StateMachineAsset sm)
            {
                return sm;
            }
            
            // Search all loaded StateMachineAssets
            var guids = UnityEditor.AssetDatabase.FindAssets("t:StateMachineAsset");
            foreach (var guid in guids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<StateMachineAsset>(assetPath);
                if (asset != null && asset.States.Contains(state))
                {
                    return asset;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Creates the StateMachineBlob and stub SkeletonClipSetBlob.
        /// </summary>
        private bool TryCreateBlobs()
        {
            if (stateMachineAsset == null)
            {
                errorMessage = "No StateMachineAsset";
                return false;
            }
            
            try
            {
                // Create StateMachineBlob from asset
                stateMachineBlob = AnimationStateMachineConversionUtils.CreateStateMachineBlob(stateMachineAsset);
                
                // Create stub SkeletonClipSetBlob with correct clip count
                // Note: This blob has no actual animation data - it's just for state machine logic validation
                var clipCount = stateMachineAsset.ClipCount;
                clipsBlob = CreateStubClipsBlob(clipCount);
                
                // Create empty clip events blob
                clipEventsBlob = CreateEmptyClipEventsBlob();
                
                return true;
            }
            catch (Exception e)
            {
                errorMessage = $"Failed to create blobs:\n{e.Message}";
                Debug.LogError($"[EcsPreviewBackend] {errorMessage}\n{e.StackTrace}");
                DisposeBlobs();
                return false;
            }
        }
        
        /// <summary>
        /// Creates a stub SkeletonClipSetBlob with the correct clip count but no animation data.
        /// This allows state machine systems to run without actual animation sampling.
        /// </summary>
        private BlobAssetReference<SkeletonClipSetBlob> CreateStubClipsBlob(int clipCount)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SkeletonClipSetBlob>();
            root.boneCount = 1; // Minimal bone count
            
            var clips = builder.Allocate(ref root.clips, clipCount);
            // Leave clips as default (zeroed) - no actual animation data
            
            return builder.CreateBlobAssetReference<SkeletonClipSetBlob>(Allocator.Persistent);
        }
        
        /// <summary>
        /// Creates an empty ClipEventsBlob.
        /// </summary>
        private BlobAssetReference<ClipEventsBlob> CreateEmptyClipEventsBlob()
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ClipEventsBlob>();
            builder.Allocate(ref root.ClipEvents, 0);
            
            return builder.CreateBlobAssetReference<ClipEventsBlob>(Allocator.Persistent);
        }
        
        /// <summary>
        /// Disposes all blob references.
        /// </summary>
        private void DisposeBlobs()
        {
            if (stateMachineBlob.IsCreated)
            {
                stateMachineBlob.Dispose();
                stateMachineBlob = default;
            }
            if (clipsBlob.IsCreated)
            {
                clipsBlob.Dispose();
                clipsBlob = default;
            }
            if (clipEventsBlob.IsCreated)
            {
                clipEventsBlob.Dispose();
                clipEventsBlob = default;
            }
            stateMachineAsset = null;
        }
        
        /// <summary>
        /// Creates the preview entity with state machine components.
        /// </summary>
        private bool TryCreatePreviewEntity()
        {
            if (!worldService.IsInitialized)
            {
                errorMessage = "ECS world not initialized";
                return false;
            }
            
            if (!stateMachineBlob.IsCreated || !clipsBlob.IsCreated)
            {
                errorMessage = "Blobs not created";
                return false;
            }
            
            try
            {
                var entity = worldService.CreatePreviewEntity(stateMachineBlob, clipsBlob, clipEventsBlob);
                if (entity == Entity.Null)
                {
                    errorMessage = "Failed to create preview entity";
                    return false;
                }
                
                entityCreated = true;
                
                // Try to initialize the hybrid renderer if we have a model
                TryInitializeRenderer();
                
                return true;
            }
            catch (Exception e)
            {
                errorMessage = $"Entity creation failed:\n{e.Message}";
                Debug.LogError($"[EcsPreviewBackend] {errorMessage}\n{e.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Tries to initialize the hybrid renderer for 3D preview.
        /// Requires both a StateMachineAsset and a preview model.
        /// </summary>
        private void TryInitializeRenderer()
        {
            // Dispose existing renderer
            if (hybridRenderer != null)
            {
                hybridRenderer.Dispose();
                hybridRenderer = null;
                rendererInitialized = false;
            }
            
            // Need both state machine and model
            if (stateMachineAsset == null || previewModel == null)
            {
                return;
            }
            
            try
            {
                hybridRenderer = new EcsHybridRenderer();
                rendererInitialized = hybridRenderer.Initialize(stateMachineAsset, previewModel);
                
                if (rendererInitialized && cameraState.IsValid)
                {
                    hybridRenderer.CameraState = cameraState;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EcsPreviewBackend] Failed to initialize hybrid renderer: {e.Message}");
                hybridRenderer?.Dispose();
                hybridRenderer = null;
                rendererInitialized = false;
            }
        }
        
        #endregion
        
        #region IPreviewBackend Time Control
        
        public void SetNormalizedTime(float time)
        {
            normalizedTime = time;
            
            // Enable preview time control and set sampler times
            if (useEntityBrowserMode && entityBrowser.HasSelection)
            {
                isPreviewTimeControlled = true;
                SetSamplerTimesOnBrowserEntity(time);
            }
        }
        
        public void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            if (!useEntityBrowserMode || !entityBrowser.HasSelection) return;
            if (!IsTransitionPreview) return;
            
            isPreviewTimeControlled = true;
            SetTransitionSamplerTimesOnBrowserEntity(fromNormalized, toNormalized);
        }
        
        public void SetTransitionProgress(float progress)
        {
            transitionProgress = progress;
            
            if (!useEntityBrowserMode || !entityBrowser.HasSelection) return;
            if (!IsTransitionPreview) return;
            
            isPreviewTimeControlled = true;
            SetTransitionProgressOnBrowserEntity(progress);
        }
        
        /// <summary>
        /// Triggers a state transition on the selected browser entity by setting
        /// the parameters to satisfy the transition conditions.
        /// </summary>
        private void TriggerTransitionOnBrowserEntity()
        {
            Debug.Log($"[EcsPreviewBackend] TriggerTransitionOnBrowserEntity called. HasSelection={entityBrowser.HasSelection}");
            
            if (!entityBrowser.HasSelection)
            {
                Debug.LogWarning("[EcsPreviewBackend] No entity selected in browser");
                return;
            }
            if (transitionToState == null || stateMachineAsset == null)
            {
                Debug.LogWarning($"[EcsPreviewBackend] Missing data: toState={transitionToState?.name}, stateMachine={stateMachineAsset?.name}");
                return;
            }
            
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (world == null || !world.IsCreated)
            {
                Debug.LogWarning("[EcsPreviewBackend] World not valid");
                return;
            }
            
            var em = world.EntityManager;
            if (!em.Exists(entity))
            {
                Debug.LogWarning("[EcsPreviewBackend] Entity doesn't exist");
                return;
            }
            
            // Find the transition definition
            StateOutTransition transition = FindTransitionDefinition();
            if (transition == null)
            {
                Debug.LogWarning($"[EcsPreviewBackend] Could not find transition from {transitionFromState?.name ?? "Any"} to {transitionToState.name}");
                return;
            }
            
            Debug.Log($"[EcsPreviewBackend] Found transition with {transition.Conditions?.Count ?? 0} conditions");
            
            // Set parameters to satisfy transition conditions
            SetTransitionConditionsOnEntity(em, entity, transition);
            
            Debug.Log($"[EcsPreviewBackend] Set conditions for transition to {transitionToState.name}");
        }
        
        /// <summary>
        /// Finds the StateOutTransition definition for the current transition preview.
        /// </summary>
        private StateOutTransition FindTransitionDefinition()
        {
            if (transitionToState == null) return null;
            
            // Check from state's out transitions
            if (transitionFromState != null && transitionFromState.OutTransitions != null)
            {
                foreach (var t in transitionFromState.OutTransitions)
                {
                    if (t.ToState == transitionToState)
                        return t;
                }
            }
            
            // Check any state transitions
            if (stateMachineAsset != null && stateMachineAsset.AnyStateTransitions != null)
            {
                foreach (var t in stateMachineAsset.AnyStateTransitions)
                {
                    if (t.ToState == transitionToState)
                        return t;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Sets parameters on the entity to satisfy transition conditions.
        /// </summary>
        private void SetTransitionConditionsOnEntity(EntityManager em, Entity entity, StateOutTransition transition)
        {
            if (transition.Conditions == null || transition.Conditions.Count == 0)
            {
                // No conditions - transition might be exit-time only
                return;
            }
            
            foreach (var condition in transition.Conditions)
            {
                if (condition.Parameter == null) continue;
                
                int paramHash = condition.Parameter.Hash;
                
                if (condition.Parameter is BoolParameterAsset)
                {
                    // Bool condition - set to the expected value
                    bool targetValue = (BoolConditionComparison)condition.ComparisonMode == BoolConditionComparison.True;
                    SetBoolParameterOnEntity(em, entity, paramHash, targetValue);
                }
                else if (condition.Parameter is IntParameterAsset)
                {
                    // Int condition - set to a value that satisfies the comparison
                    // ComparisonValue is stored as float in TransitionCondition, cast to int
                    int targetValue = GetSatisfyingIntValue((IntConditionComparison)condition.ComparisonMode, (int)condition.ComparisonValue);
                    SetIntParameterOnEntity(em, entity, paramHash, targetValue);
                }
            }
        }
        
        /// <summary>
        /// Gets an int value that satisfies the given comparison.
        /// </summary>
        private static int GetSatisfyingIntValue(IntConditionComparison comparison, int comparisonValue)
        {
            return comparison switch
            {
                IntConditionComparison.Equal => comparisonValue,
                IntConditionComparison.NotEqual => comparisonValue + 1,
                IntConditionComparison.Greater => comparisonValue + 1,
                IntConditionComparison.GreaterOrEqual => comparisonValue,
                IntConditionComparison.Less => comparisonValue - 1,
                IntConditionComparison.LessOrEqual => comparisonValue,
                _ => comparisonValue
            };
        }
        
        /// <summary>
        /// Sets a bool parameter on the entity by hash.
        /// </summary>
        private static void SetBoolParameterOnEntity(EntityManager em, Entity entity, int hash, bool value)
        {
            if (!em.HasBuffer<BoolParameter>(entity)) return;
            
            var buffer = em.GetBuffer<BoolParameter>(entity);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Hash == hash)
                {
                    var param = buffer[i];
                    param.Value = value;
                    buffer[i] = param;
                    return;
                }
            }
        }
        
        /// <summary>
        /// Sets an int parameter on the entity by hash.
        /// </summary>
        private static void SetIntParameterOnEntity(EntityManager em, Entity entity, int hash, int value)
        {
            if (!em.HasBuffer<IntParameter>(entity)) return;
            
            var buffer = em.GetBuffer<IntParameter>(entity);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Hash == hash)
                {
                    var param = buffer[i];
                    param.Value = value;
                    buffer[i] = param;
                    return;
                }
            }
        }
        
        /// <summary>
        /// Sets the transition progress on the browser entity by controlling animation state time.
        /// Progress is determined by toState.Time / transitionDuration.
        /// </summary>
        private void SetTransitionProgressOnBrowserEntity(float progress)
        {
            if (!entityBrowser.HasSelection) return;


            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;


            if (world == null || !world.IsCreated) return;


            var em = world.EntityManager;
            if (!em.Exists(entity)) return;

            // Get the current transition

            if (!em.HasComponent<AnimationStateTransition>(entity)) return;
            var transition = em.GetComponentData<AnimationStateTransition>(entity);


            if (!transition.IsValid) return;

            // Get animation states buffer

            if (!em.HasBuffer<AnimationState>(entity)) return;
            var animationStates = em.GetBuffer<AnimationState>(entity);

            // Find the "to" animation state

            int toStateIndex = animationStates.IdToIndex((byte)transition.AnimationStateId);
            if (toStateIndex < 0) return;

            // Set the time to control progress: time = progress * duration

            var toState = animationStates[toStateIndex];
            toState.Time = progress * transition.TransitionDuration;
            animationStates[toStateIndex] = toState;

            // Also update the sampler times for the to state

            if (!em.HasBuffer<ClipSampler>(entity)) return;


            var samplers = em.GetBuffer<ClipSampler>(entity);
            int startSamplerIndex = samplers.IdToIndex(toState.StartSamplerId);

            if (startSamplerIndex < 0) return; 

            // Set sampler times based on normalized time within the state
            // For now, use a simple approach - set all samplers to same normalized time
            for (int i = 0; i < toState.ClipCount && (startSamplerIndex + i) < samplers.Length; i++)
            {
                var sampler = samplers[startSamplerIndex + i];
                // Keep current normalized position within the clip
                sampler.PreviousTime = sampler.Time;
                samplers[startSamplerIndex + i] = sampler;

            }
        }


        /// <summary>
        /// Sets the sampler times for both from and to states during transition.
        /// </summary>
        private void SetTransitionSamplerTimesOnBrowserEntity(float fromNormalized, float toNormalized)
        {
            if (!entityBrowser.HasSelection) return;
            
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (world == null || !world.IsCreated) return;
            
            var em = world.EntityManager;
            if (!em.Exists(entity)) return;
            
            if (!em.HasBuffer<AnimationState>(entity)) return;
            if (!em.HasBuffer<ClipSampler>(entity)) return;
            
            var animationStates = em.GetBuffer<AnimationState>(entity);
            var samplers = em.GetBuffer<ClipSampler>(entity);
            
            // Get current state (from state)
            if (em.HasComponent<AnimationCurrentState>(entity))
            {
                var currentState = em.GetComponentData<AnimationCurrentState>(entity);
                if (currentState.IsValid)
                {
                    int fromStateIndex = animationStates.IdToIndex((byte)currentState.AnimationStateId);
                    if (fromStateIndex >= 0)
                    {
                        SetAnimationStateSamplerTimes(ref animationStates, ref samplers, fromStateIndex, fromNormalized);
                    }
                }
            }
            
            // Get transition state (to state)
            if (em.HasComponent<AnimationStateTransition>(entity))
            {
                var transition = em.GetComponentData<AnimationStateTransition>(entity);
                if (transition.IsValid)
                {
                    int toStateIndex = animationStates.IdToIndex((byte)transition.AnimationStateId);
                    if (toStateIndex >= 0)
                    {
                        SetAnimationStateSamplerTimes(ref animationStates, ref samplers, toStateIndex, toNormalized);
                    }
                }
            }
        }
        
        /// <summary>
        /// Sets sampler times for all clips in an animation state.
        /// </summary>
        private static void SetAnimationStateSamplerTimes(
            ref DynamicBuffer<AnimationState> animationStates,
            ref DynamicBuffer<ClipSampler> samplers,
            int animationStateIndex,
            float normalizedTime)
        {
            var animState = animationStates[animationStateIndex];
            int startIndex = samplers.IdToIndex(animState.StartSamplerId);
            
            if (startIndex < 0) return;
            
            for (int i = 0; i < animState.ClipCount && (startIndex + i) < samplers.Length; i++)
            {
                var sampler = samplers[startIndex + i];
                float clipDuration = sampler.Duration;


                if (clipDuration <= 0) continue; 

                sampler.PreviousTime = sampler.Time;
                sampler.Time = normalizedTime * clipDuration;
                samplers[startIndex + i] = sampler;
            }
        }
        
        /// <summary>
        /// Sets the playback state.
        /// When paused, sampler times are controlled by the preview timeline.
        /// </summary>
        public void SetPlaying(bool playing)
        {
            isPreviewPlaying = playing;


            if (playing)
            {
                // When playing, release time control
                isPreviewTimeControlled = false;
            }
            else
            {
                // When pausing, take control of time
                isPreviewTimeControlled = true;

                // Sync current entity time to preview
                if (!useEntityBrowserMode || !entityBrowser.HasSelection) return;


                float clipDuration = GetSelectedEntityClipDuration();


                if (clipDuration <= 0) return;

                var entity = entityBrowser.SelectedEntity;
                var world = entityBrowser.SelectedWorld;


                if (world == null || !world.IsCreated) return;

                var em = world.EntityManager;

                if (!em.Exists(entity) || !em.HasBuffer<ClipSampler>(entity)) return;

                var samplers = em.GetBuffer<ClipSampler>(entity);

                if (samplers.Length <= 0) return;

                previewControlledTime = samplers[0].Time;
                normalizedTime = previewControlledTime / clipDuration;
            }
        }


        /// <summary>
        /// Toggles play/pause for the preview.
        /// </summary>
        public void TogglePlayPause() => SetPlaying(!isPreviewPlaying);


        /// <summary>
        /// Steps the animation forward by the given number of frames.
        /// </summary>
        public void StepFrames(int frameCount, float fps = 30f)
        {
            if (!useEntityBrowserMode || !entityBrowser.HasSelection) return;


            isPreviewTimeControlled = true;
            isPreviewPlaying = false;


            float frameDuration = 1f / fps;
            float stepTime = frameCount * frameDuration;

            // Get current time and advance
            previewControlledTime += stepTime;

            // Get clip duration and normalize
            float clipDuration = GetSelectedEntityClipDuration();
            
            if (clipDuration <= 0) return;

            normalizedTime = (previewControlledTime % clipDuration) / clipDuration;
            SetSamplerTimesOnBrowserEntity(normalizedTime);
        }


        /// <summary>
        /// Releases preview time control, letting ECS systems drive animation.
        /// </summary>
        public void ReleaseTimeControl()
        {
            isPreviewTimeControlled = false;
            isPreviewPlaying = true;
        }
        
        /// <summary>
        /// Sets sampler times on the selected browser entity.
        /// </summary>
        private void SetSamplerTimesOnBrowserEntity(float normalizedTime)
        {
            if (!entityBrowser.HasSelection) return;


            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;


            if (world == null || !world.IsCreated) return;


            var em = world.EntityManager;
            if (!em.Exists(entity)) return;
            if (!em.HasBuffer<ClipSampler>(entity)) return;


            var samplers = em.GetBuffer<ClipSampler>(entity);

            for (int i = 0; i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                float clipDuration = sampler.Duration;

                if (clipDuration <= 0) continue;

                float newTime = normalizedTime * clipDuration;
                sampler.PreviousTime = sampler.Time;
                sampler.Time = newTime;
                samplers[i] = sampler;
            }

            // Store for step calculations

            if (samplers.Length <= 0) return;

            previewControlledTime = normalizedTime * samplers[0].Duration;
        }


        /// <summary>
        /// Gets the clip duration of the first sampler on the selected entity.
        /// </summary>
        private float GetSelectedEntityClipDuration()
        {
            if (!entityBrowser.HasSelection) return 0f;
            
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (world == null || !world.IsCreated) return 0f;
            
            var em = world.EntityManager;
            if (!em.Exists(entity)) return 0f;
            if (!em.HasBuffer<ClipSampler>(entity)) return 0f;
            
            var samplers = em.GetBuffer<ClipSampler>(entity);
            if (samplers.Length == 0) return 0f;
            
            return samplers[0].Duration;
        }

        #endregion

        #region IPreviewBackend Blend Control

        public void SetBlendPosition1D(float value)
        {
            Debug.Log($"[EcsPreviewBackend] SetBlendPosition1D called: value={value}");
            targetBlendPosition = new float2(value, 0);
        }

        public void SetBlendPosition2D(float2 position)
        {
            Debug.Log($"[EcsPreviewBackend] SetBlendPosition2D called: position={position}");
            targetBlendPosition = position;
        }
        
        public void SetBlendPosition1DImmediate(float value)
        {
            // Immediate - skip interpolation
            blendPosition = new float2(value, 0);
            targetBlendPosition = blendPosition;
            SetBlendParameters();
        }
        
        public void SetBlendPosition2DImmediate(float2 position)
        {
            // Immediate - skip interpolation
            blendPosition = position;
            targetBlendPosition = position;
            SetBlendParameters();
        }
        
        public void SetTransitionFromBlendPosition(float2 position)
        {
            // TODO: Phase 6 - Set from state blend position
        }
        
        public void SetTransitionToBlendPosition(float2 position)
        {
            // TODO: Phase 6 - Set to state blend position
        }
        
        public void SetSoloClip(int clipIndex)
        {
            // TODO: Phase 6 - Solo clip mode
        }
        
        private void SetBlendParameters()
        {
            // In entity browser mode, modify the selected entity directly
            if (useEntityBrowserMode)
            {
                SetBlendParametersOnBrowserEntity();
                return;
            }
            
            // Legacy isolated preview mode
            if (!worldService.IsInitialized) return;
            
            // TODO: Implement for isolated preview mode if needed
        }
        
        /// <summary>
        /// Sets blend parameters on the entity selected in the entity browser.
        /// </summary>
        private void SetBlendParametersOnBrowserEntity()
        {
            Debug.Log($"[EcsPreviewBackend] SetBlendParametersOnBrowserEntity called. currentState={currentState?.name ?? "null"}, hasSelection={entityBrowser.HasSelection}");
            
            if (!entityBrowser.HasSelection)
            {
                Debug.Log("[EcsPreviewBackend] SetBlendParameters: No entity selected");
                return;
            }
            
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (world == null || !world.IsCreated)
            {
                Debug.Log("[EcsPreviewBackend] SetBlendParameters: World not valid");
                return;
            }
            
            var em = world.EntityManager;
            if (!em.Exists(entity))
            {
                Debug.Log("[EcsPreviewBackend] SetBlendParameters: Entity doesn't exist");
                return;
            }
            
            // Get the blend parameter hash from the current state
            int paramHash = 0;
            bool isIntParam = false;
            int intRangeMin = 0;
            int intRangeMax = 1;
            
            if (currentState is LinearBlendStateAsset linearBlend)
            {
                if (linearBlend.BlendParameter == null)
                {
                    Debug.Log("[EcsPreviewBackend] SetBlendParameters: LinearBlend has no BlendParameter");
                    return;
                }

                paramHash = linearBlend.BlendParameter.Hash;
                isIntParam = linearBlend.UsesIntParameter;
                intRangeMin = linearBlend.IntRangeMin;
                intRangeMax = linearBlend.IntRangeMax;
                Debug.Log($"[EcsPreviewBackend] SetBlendParameters: LinearBlend hash={paramHash}, value={blendPosition.x}");
            }
            else if (currentState is Directional2DBlendStateAsset directional2D)
            {
                // For 2D blend, set both X and Y parameters
                if (directional2D.BlendParameterX != null && em.HasBuffer<FloatParameter>(entity))
                {
                    var buffer = em.GetBuffer<FloatParameter>(entity);
                    SetFloatParameterByHash(buffer, directional2D.BlendParameterX.Hash, blendPosition.x);
                }
                if (directional2D.BlendParameterY != null && em.HasBuffer<FloatParameter>(entity))
                {
                    var buffer = em.GetBuffer<FloatParameter>(entity);
                    SetFloatParameterByHash(buffer, directional2D.BlendParameterY.Hash, blendPosition.y);
                }
                return;
            }
            else
            {
                return; // Not a blend state
            }
            
            // Set the parameter value
            if (isIntParam)
            {
                if (!em.HasBuffer<IntParameter>(entity))
                {
                    Debug.Log("[EcsPreviewBackend] SetBlendParameters: No IntParameter buffer");
                    return;
                }
                var buffer = em.GetBuffer<IntParameter>(entity);
                
                // Convert blend position (0-1) to int range
                int intValue = (int)math.lerp(intRangeMin, intRangeMax, blendPosition.x);
                bool found = SetIntParameterByHash(buffer, paramHash, intValue);
                Debug.Log($"[EcsPreviewBackend] SetBlendParameters: IntParam hash={paramHash}, value={intValue}, found={found}");
            }
            else
            {
                if (!em.HasBuffer<FloatParameter>(entity))
                {
                    Debug.Log("[EcsPreviewBackend] SetBlendParameters: No FloatParameter buffer");
                    return;
                }
                var buffer = em.GetBuffer<FloatParameter>(entity);
                
                // Log existing parameters for debugging
                Debug.Log($"[EcsPreviewBackend] Entity has {buffer.Length} float params. Looking for hash={paramHash}");
                for (int i = 0; i < buffer.Length; i++)
                {
                    Debug.Log($"  Param[{i}]: Hash={buffer[i].Hash}, Value={buffer[i].Value}");
                }
                
                bool found = SetFloatParameterByHash(buffer, paramHash, blendPosition.x);
                Debug.Log($"[EcsPreviewBackend] SetBlendParameters: FloatParam hash={paramHash}, value={blendPosition.x}, found={found}");
            }
        }
        
        private static bool SetFloatParameterByHash(DynamicBuffer<FloatParameter> buffer, int hash, float value)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Hash != hash) continue; 
                
                var param = buffer[i];
                param.Value = value;
                buffer[i] = param;
                return true;
            }
            return false;
        }
        
        private static bool SetIntParameterByHash(DynamicBuffer<IntParameter> buffer, int hash, int value)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Hash != hash) continue;
                
                var param = buffer[i];
                param.Value = value;
                buffer[i] = param;
                return true;
            }
            return false;
        }
        
        #endregion
        
        #region IPreviewBackend Update & Render
        
        private static int tickLogCounter = 0;
        
        public bool Tick(float deltaTime)
        {
            bool needsRepaint = false;
            
            // Log occasionally to confirm Tick is running
            if (++tickLogCounter % 60 == 0)
            {
                Debug.Log($"[EcsPreviewBackend] Tick running. blendPos={blendPosition}, targetBlendPos={targetBlendPosition}, useEntityBrowserMode={useEntityBrowserMode}");
            }

            // Smooth blend position interpolation
            if (math.any(blendPosition != targetBlendPosition))
            {
                Debug.Log($"[EcsPreviewBackend] Blend interpolating: {blendPosition} -> {targetBlendPosition}");
                // Lerp towards target
                var diff = targetBlendPosition - blendPosition;
                var maxStep = BlendSmoothSpeed * deltaTime;


                if (math.length(diff) <= maxStep)
                {
                    blendPosition = targetBlendPosition;
                }
                else
                {
                    blendPosition += math.normalize(diff) * maxStep;
                }

                // Update entity parameters with new blend position

                SetBlendParameters();
                needsRepaint = true;
            }

            // Entity browser mode

            if (!useEntityBrowserMode)
            {
                // Legacy isolated preview mode
                if (!worldService.IsInitialized) return needsRepaint;

                worldService.Update(deltaTime);
                return needsRepaint;
            }

            // When preview controls time (paused or scrubbed), maintain the sampler times
            // This prevents ECS systems from advancing time
            if (isPreviewTimeControlled && !isPreviewPlaying && entityBrowser.HasSelection)
            {
                if (IsTransitionPreview)
                {
                    SetTransitionProgressOnBrowserEntity(transitionProgress);
                }
                else
                {
                    SetSamplerTimesOnBrowserEntity(normalizedTime);
                }
                needsRepaint = true;
            }
            else if (isPreviewPlaying && entityBrowser.HasSelection)
            {
                // When playing, sync state from entity for UI display
                SyncStateFromEntity();
                needsRepaint = true;
            }

            return needsRepaint;
        }
        
        /// <summary>
        /// Syncs normalizedTime, transitionProgress, and blendPosition from the entity.
        /// </summary>
        private void SyncStateFromEntity()
        {
            if (!entityBrowser.HasSelection) return;
            
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (world == null || !world.IsCreated) return;
            
            var em = world.EntityManager;
            if (!em.Exists(entity)) return;
            
            // Sync normalized time from samplers
            if (em.HasBuffer<ClipSampler>(entity))
            {
                var samplers = em.GetBuffer<ClipSampler>(entity);
                if (samplers.Length > 0)
                {
                    float clipDuration = samplers[0].Duration;
                    if (clipDuration > 0)
                    {
                        normalizedTime = samplers[0].Time / clipDuration;
                        normalizedTime = normalizedTime - math.floor(normalizedTime); // Wrap to 0-1
                    }
                }
            }
            
            // Sync transition progress if in transition
            if (IsTransitionPreview && em.HasComponent<AnimationStateTransition>(entity))
            {
                var transition = em.GetComponentData<AnimationStateTransition>(entity);
                if (transition.IsValid && em.HasBuffer<AnimationState>(entity))
                {
                    var animationStates = em.GetBuffer<AnimationState>(entity);
                    int toStateIndex = animationStates.IdToIndex((byte)transition.AnimationStateId);
                    
                    if (toStateIndex >= 0 && transition.TransitionDuration > 0)
                    {
                        var toState = animationStates[toStateIndex];
                        transitionProgress = math.clamp(toState.Time / transition.TransitionDuration, 0, 1);
                    }
                }
            }
            
            // Sync blend position from float parameters
            SyncBlendPositionFromEntity(em, entity);
        }
        
        /// <summary>
        /// Reads blend parameter values from the entity and updates local blendPosition.
        /// Also updates PreviewSettings so the UI reflects the current entity state.
        /// </summary>
        private void SyncBlendPositionFromEntity(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<FloatParameter>(entity)) return;
            
            var floatParams = em.GetBuffer<FloatParameter>(entity);
            bool updated = false;
            
            // Get blend parameter hash from current state
            if (currentState is LinearBlendStateAsset linearBlend && linearBlend.BlendParameter != null)
            {
                if (!linearBlend.UsesIntParameter)
                {
                    int hash = linearBlend.BlendParameter.Hash;
                    for (int i = 0; i < floatParams.Length; i++)
                    {
                        if (floatParams[i].Hash == hash)
                        {
                            blendPosition.x = floatParams[i].Value;
                            targetBlendPosition.x = blendPosition.x;
                            break;
                        }
                    }
                }
                else if (em.HasBuffer<IntParameter>(entity))
                {
                    // Int parameter - read and convert to normalized 0-1
                    var intParams = em.GetBuffer<IntParameter>(entity);
                    int hash = linearBlend.BlendParameter.Hash;
                    for (int i = 0; i < intParams.Length; i++)
                    {
                        if (intParams[i].Hash == hash)
                        {
                            int range = linearBlend.IntRangeMax - linearBlend.IntRangeMin;
                            if (range > 0)
                            {
                                blendPosition.x = (float)(intParams[i].Value - linearBlend.IntRangeMin) / range;
                                targetBlendPosition.x = blendPosition.x;
                            }
                            break;
                        }
                    }
                }
            }
            else if (currentState is Directional2DBlendStateAsset blend2D)
            {
                // 2D blend - read X and Y parameters
                if (blend2D.BlendParameterX != null)
                {
                    int hashX = blend2D.BlendParameterX.Hash;
                    for (int i = 0; i < floatParams.Length; i++)
                    {
                        if (floatParams[i].Hash == hashX)
                        {
                            blendPosition.x = floatParams[i].Value;
                            targetBlendPosition.x = blendPosition.x;
                            break;
                        }
                    }
                }
                if (blend2D.BlendParameterY != null)
                {
                    int hashY = blend2D.BlendParameterY.Hash;
                    for (int i = 0; i < floatParams.Length; i++)
                    {
                        if (floatParams[i].Hash == hashY)
                        {
                            blendPosition.y = floatParams[i].Value;
                            targetBlendPosition.y = blendPosition.y;
                            break;
                        }
                    }
                }
            }
        }


        public void Draw(Rect rect)
        {
            // Draw background
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            
            // Always show entity browser mode for live entity inspection
            if (useEntityBrowserMode)
            {
                DrawEntityBrowserMode(rect);
                return;
            }
            
            // Legacy isolated preview mode (below)
            // Show error/info message
            if (!string.IsNullOrEmpty(errorMessage))
            {
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
                };
                GUI.Label(rect, errorMessage, style);
            }
            else if (isInitialized && entityCreated && rendererInitialized)
            {
                // Phase 6B: 3D rendering via hybrid renderer
                var samplers = worldService.GetActiveSamplers();
                hybridRenderer.Render(samplers, rect);
            }
            else if (isInitialized && entityCreated)
            {
                // Phase 6A: Show state machine info (no model available for 3D rendering)
                DrawStateInfo(rect);
            }
            else if (isInitialized)
            {
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter
                };
                GUI.Label(rect, "ECS Preview\nInitializing...", style);
            }
        }
        
        /// <summary>
        /// Draws the entity browser mode UI.
        /// Shows list of animation entities and allows inspection/modification.
        /// </summary>
        private void DrawEntityBrowserMode(Rect rect)
        {
            // Toolbar at top
            var toolbarHeight = 22f;
            var toolbarRect = new Rect(rect.x, rect.y, rect.width, toolbarHeight);
            
            EditorGUI.DrawRect(toolbarRect, new Color(0.2f, 0.2f, 0.2f));
            
            GUILayout.BeginArea(toolbarRect);
            EditorGUILayout.BeginHorizontal();
            
            GUILayout.Label("ECS Entity Browser", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            // Setup preview scene button
            if (stateMachineAsset != null)
            {
                if (sceneManager.IsSetup)
                {
                    if (GUILayout.Button("Rebake", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        sceneManager.ForceBake();
                    }
                    if (GUILayout.Button("Close Scene", EditorStyles.miniButton, GUILayout.Width(80)))
                    {
                        sceneManager.Close();
                    }
                }
                else
                {
                    if (GUILayout.Button("Open Preview Scene", EditorStyles.miniButton, GUILayout.Width(120)))
                    {
                        SetupPreviewScene();
                    }
                }
            }
            
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
            
            // Split remaining area between browser and inspector
            var contentRect = new Rect(rect.x, rect.y + toolbarHeight, rect.width, rect.height - toolbarHeight);
            var splitRatio = 0.5f;
            
            var browserRect = new Rect(
                contentRect.x, 
                contentRect.y, 
                contentRect.width * splitRatio, 
                contentRect.height);
            
            var inspectorRect = new Rect(
                contentRect.x + contentRect.width * splitRatio, 
                contentRect.y, 
                contentRect.width * (1 - splitRatio), 
                contentRect.height);
            
            // Draw separator
            EditorGUI.DrawRect(new Rect(inspectorRect.x - 1, inspectorRect.y, 2, inspectorRect.height), 
                new Color(0.1f, 0.1f, 0.1f));
            
            // Draw browser and inspector
            entityBrowser.DrawBrowser(browserRect);
            entityBrowser.DrawInspector(inspectorRect);
        }
        
        /// <summary>
        /// Sets up the preview scene with the current state machine and model.
        /// </summary>
        private void SetupPreviewScene()
        {
            if (stateMachineAsset == null)
            {
                errorMessage = "No state machine selected";
                return;
            }
            
            // Use the preview model if set, otherwise let scene manager find one
            if (sceneManager.Setup(stateMachineAsset, previewModel))
            {
                // Refresh entity browser after scene setup
                entityBrowser.RefreshEntityList();
            }
            else
            {
                errorMessage = "Failed to set up preview scene.\nEnsure a preview model is assigned.";
            }
        }
        
        /// <summary>
        /// Draws state machine info panel (Phase 6A - logic validation mode).
        /// </summary>
        private void DrawStateInfo(Rect rect)
        {
            var snapshot = worldService.GetSnapshot();
            
            // Header style
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
            
            // Info style
            var infoStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
            };
            
            // Note style
            var noteStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerCenter,
                wordWrap = true,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
            
            // Layout - use manual positioning instead of GUILayout
            var padding = 10f;
            var lineHeight = 18f;
            var y = rect.y + padding;
            
            // Header
            GUI.Label(new Rect(rect.x, y, rect.width, lineHeight), "ECS Runtime Preview", headerStyle);
            y += lineHeight + 10f;
            
            // State info
            if (currentState != null)
            {
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    $"State: {currentState.name}", infoStyle);
                y += lineHeight;
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    $"Type: {currentState.Type}", infoStyle);
                y += lineHeight;
            }
            else if (transitionToState != null)
            {
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    "Transition Preview", infoStyle);
                y += lineHeight;
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    $"From: {transitionFromState?.name ?? "Any State"}", infoStyle);
                y += lineHeight;
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    $"To: {transitionToState.name}", infoStyle);
                y += lineHeight;
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    $"Duration: {transitionDuration:F2}s", infoStyle);
                y += lineHeight;
            }
            
            y += 10f;
            
            // Snapshot info
            if (snapshot.IsInitialized)
            {
                GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                    $"Time: {snapshot.NormalizedTime:F3}", infoStyle);
                y += lineHeight;
                
                if (snapshot.BlendWeights != null && snapshot.BlendWeights.Length > 0)
                {
                    var weightsStr = string.Join(", ", snapshot.BlendWeights.Select(w => w.ToString("F2")));
                    GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                        $"Weights: [{weightsStr}]", infoStyle);
                    y += lineHeight;
                }
                
                if (snapshot.TransitionProgress >= 0)
                {
                    GUI.Label(new Rect(rect.x + padding, y, rect.width - padding * 2, lineHeight), 
                        $"Transition: {snapshot.TransitionProgress:P0}", infoStyle);
                    y += lineHeight;
                }
            }
            
            // Note - positioned at ~70% down
            var noteHeight = 36f;
            var noteY = rect.y + rect.height * 0.7f;
            var noteRect = new Rect(rect.x, noteY, rect.width, noteHeight);
            GUI.Label(noteRect, "Drag a model prefab to the\nPreview Model field for 3D preview.", noteStyle);
        }
        
        public bool HandleInput(Rect rect)
        {
            if (!rendererInitialized) return false;
            
            // Forward camera controls to hybrid renderer
            if (rect.Contains(Event.current.mousePosition))
            {
                hybridRenderer.HandleCamera();
                
                if (Event.current.type == EventType.MouseDrag || Event.current.type == EventType.ScrollWheel)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        public void ResetCameraView()
        {
            cameraState = PlayableGraphPreview.CameraState.Invalid;

            if (!rendererInitialized) return;
            
            hybridRenderer.ResetCameraView();
        }


        public PreviewSnapshot GetSnapshot()
        {
            // Entity browser mode - return tracked state
            if (useEntityBrowserMode)
            {
                return new PreviewSnapshot
                {
                    IsInitialized = isInitialized,
                    NormalizedTime = normalizedTime,
                    BlendPosition = blendPosition,
                    TransitionProgress = IsTransitionPreview ? transitionProgress : -1f,
                    IsPlaying = isPreviewPlaying,
                    ErrorMessage = errorMessage
                };
            }
            
            // Legacy isolated preview mode
            if (!worldService.IsInitialized)
            {
                return new PreviewSnapshot
                {
                    IsInitialized = false,
                    ErrorMessage = errorMessage ?? "ECS world not initialized"
                };
            }
            
            // Get snapshot from the world service
            var snapshot = worldService.GetSnapshot();
            
            // Override with our tracked values if service isn't fully set up
            if (!snapshot.IsInitialized)
            {
                snapshot.NormalizedTime = normalizedTime;
                snapshot.BlendPosition = blendPosition;
                snapshot.TransitionProgress = IsTransitionPreview ? transitionProgress : -1f;
                snapshot.ErrorMessage = errorMessage;
                snapshot.IsInitialized = isInitialized;
            }
            
            return snapshot;
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            sceneManager?.Dispose();
            sceneManager = null;
            
            entityBrowser?.Dispose();
            entityBrowser = null;
            
            hybridRenderer?.Dispose();
            hybridRenderer = null;
            rendererInitialized = false;
            
            worldService?.Dispose();
            worldService = null;
            
            DisposeBlobs();
        }
        
        #endregion
    }
}
