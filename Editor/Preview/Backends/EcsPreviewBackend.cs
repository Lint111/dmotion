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
    /// 
    /// Uses the AnimationTimelineControllerSystem to provide native timeline control:
    /// - Play/Pause/Scrub through the ECS system
    /// - State preview with blend position control
    /// - Transition preview with ghost bars
    /// 
    /// The timeline controller outputs render requests that downstream systems
    /// (ApplyStateRenderRequestSystem, ApplyTransitionRenderRequestSystem) apply
    /// to the animation state buffers.
    /// </summary>
    internal class EcsPreviewBackend : IPreviewBackend
    {
        #region State
        
        // Core state
        private AnimationStateAsset currentState;
        private AnimationStateAsset transitionFromState;
        private AnimationStateAsset transitionToState;
        private StateMachineAsset stateMachineAsset;
        private string errorMessage;
        private bool isInitialized;
        
        // Pending setup state (coroutine-based entity waiting)
        private enum PendingSetupType { None, State, Transition }
        private PendingSetupType pendingSetup = PendingSetupType.None;
        private float setupStartTime;
        private const float SetupTimeoutSeconds = 30f;
        private bool isSetupCoroutineRunning;
        
        // Timeline control
        private TimelineControlHelper timelineHelper;
        
        // Entity browser for live entity selection
        private EcsEntityBrowser entityBrowser;
        
        // Scene manager for automatic SubScene setup
        private EcsPreviewSceneManager sceneManager;
        
        // Hybrid renderer for 3D preview
        private EcsHybridRenderer hybridRenderer;
        private bool rendererInitialized;
        
        // Legacy world service (for isolated preview mode)
        private EcsPreviewWorldService worldService;
        
        // Camera and model
        private PlayableGraphPreview.CameraState cameraState;
        private GameObject previewModel;
        
        #endregion
        
        #region Constructor
        
        public EcsPreviewBackend()
        {
            entityBrowser = new EcsEntityBrowser();
            sceneManager = EcsPreviewSceneManager.Instance;
            worldService = new EcsPreviewWorldService();
            timelineHelper = new TimelineControlHelper();
            
            // Listen for Play mode changes to re-trigger setup
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // World was recreated - need to re-setup
                isInitialized = false;
                timelineHelper?.Dispose();
                timelineHelper = new TimelineControlHelper();
                entityBrowser?.Dispose();
                entityBrowser = new EcsEntityBrowser();
                
                // Re-trigger setup with cached state/transition
                if (transitionToState != null)
                {
                    StartSetupCoroutine(PendingSetupType.Transition);
                }
                else if (currentState != null)
                {
                    StartSetupCoroutine(PendingSetupType.State);
                }
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // About to exit - stop any pending setup
                StopSetupCoroutine();
                isInitialized = false;
            }
        }
        
        #endregion
        
        #region IPreviewBackend Properties
        
        public PreviewMode Mode => PreviewMode.EcsRuntime;
        
        public bool IsInitialized => isInitialized;
        
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
        
        /// <summary>
        /// Layer composition preview interface. Returns null - not yet implemented for ECS backend.
        /// </summary>
        public ILayerCompositionPreview LayerComposition => null;
        
        #endregion
        
        #region IPreviewBackend Initialization
        
        public void CreatePreviewForState(AnimationStateAsset state)
        {
            // Cancel any pending setup
            CancelPendingSetup();
            
            currentState = state;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = null;
            isInitialized = false;
            
            if (state == null)
            {
                errorMessage = "No state selected";
                return;
            }
            
            // Find the owning StateMachineAsset
            stateMachineAsset = FindOwningStateMachine(state);
            if (stateMachineAsset == null)
            {
                errorMessage = $"Could not find StateMachineAsset\nfor state: {state.name}";
                return;
            }
            
            // Only auto-setup in Play mode - ECS world doesn't exist in Edit mode
            if (!Application.isPlaying)
            {
                errorMessage = "Enter Play mode to preview\nECS animations";
                return;
            }
            
            // Start setup coroutine
            StartSetupCoroutine(PendingSetupType.State);
        }
        
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float duration)
        {
            // Cancel any pending setup
            CancelPendingSetup();
            
            currentState = null;
            transitionFromState = fromState;
            transitionToState = toState;
            errorMessage = null;
            isInitialized = false;
            
            if (toState == null)
            {
                errorMessage = "No target state for transition";
                return;
            }
            
            // Find the owning StateMachineAsset
            stateMachineAsset = FindOwningStateMachine(toState);
            if (stateMachineAsset == null)
            {
                errorMessage = $"Could not find StateMachineAsset\nfor state: {toState.name}";
                return;
            }
            
            // Only auto-setup in Play mode - ECS world doesn't exist in Edit mode
            if (!Application.isPlaying)
            {
                errorMessage = "Enter Play mode to preview\nECS animations";
                return;
            }
            
            // Start setup coroutine
            StartSetupCoroutine(PendingSetupType.Transition);
        }
        
        /// <summary>
        /// Starts the setup coroutine using EditorApplication.update.
        /// </summary>
        private void StartSetupCoroutine(PendingSetupType setupType)
        {
            pendingSetup = setupType;
            setupStartTime = (float)EditorApplication.timeSinceStartup;
            errorMessage = "Waiting for animation entity...";
            
            if (!isSetupCoroutineRunning)
            {
                isSetupCoroutineRunning = true;
                EditorApplication.update += SetupCoroutineUpdate;
            }
        }
        
        /// <summary>
        /// Coroutine update called by EditorApplication.update.
        /// Polls for entity availability and completes setup when ready.
        /// </summary>
        private void SetupCoroutineUpdate()
        {
            if (pendingSetup == PendingSetupType.None)
            {
                StopSetupCoroutine();
                return;
            }
            
            // Check timeout
            float elapsed = (float)EditorApplication.timeSinceStartup - setupStartTime;
            if (elapsed > SetupTimeoutSeconds)
            {
                errorMessage = "No animation entity found.\nEnter Play mode with an animated character.";
                StopSetupCoroutine();
                return;
            }
            
            // Try to select entity
            if (!TrySelectEntity())
            {
                return; // Keep waiting
            }
            
            // Entity found - complete setup based on type
            bool success = pendingSetup switch
            {
                PendingSetupType.State => TryCompleteStateSetup(),
                PendingSetupType.Transition => TryCompleteTransitionSetup(),
                _ => false
            };
            
            if (success)
            {
                StopSetupCoroutine();
            }
        }
        
        /// <summary>
        /// Stops the setup coroutine.
        /// </summary>
        private void StopSetupCoroutine()
        {
            if (isSetupCoroutineRunning)
            {
                EditorApplication.update -= SetupCoroutineUpdate;
                isSetupCoroutineRunning = false;
            }
            pendingSetup = PendingSetupType.None;
        }
        
        /// <summary>
        /// Completes state preview setup after entity is found.
        /// </summary>
        private bool TryCompleteStateSetup()
        {
            if (!InitializeTimelineHelper())
            {
                Debug.LogWarning("[EcsPreviewBackend] TryCompleteStateSetup: InitializeTimelineHelper failed");
                errorMessage = "Failed to initialize timeline control";
                return true;
            }
            
            // Get blend position from state asset (via PreviewSettings)
            var blendPos = GetBlendPositionForState(currentState);
            
            timelineHelper.SetupStatePreview(currentState, blendPos);
            isInitialized = true;
            errorMessage = null;
            return true;
        }
        
        /// <summary>
        /// Completes transition preview setup after entity is found.
        /// </summary>
        private bool TryCompleteTransitionSetup()
        {
            if (!InitializeTimelineHelper())
            {
                Debug.LogWarning("[EcsPreviewBackend] TryCompleteTransitionSetup: InitializeTimelineHelper failed");
                errorMessage = "Failed to initialize timeline control";
                return true;
            }
            
            // Get blend positions from state assets (via PreviewSettings)
            var fromBlendPos = GetBlendPositionForState(transitionFromState);
            var toBlendPos = GetBlendPositionForState(transitionToState);
            
            var (transitionIndex, curveSource) = FindTransitionIndices(transitionFromState, transitionToState);
            var (transition, _, _, _) = GetTransitionInfo();
            
            timelineHelper.SetupTransitionPreview(
                transitionFromState,
                transitionToState,
                transition,
                transitionIndex,
                curveSource,
                fromBlendPos,
                toBlendPos);
            
            isInitialized = true;
            errorMessage = null;
            return true;
        }
        
        /// <summary>
        /// Tries to select an animation entity.
        /// </summary>
        private bool TrySelectEntity()
        {
            // Auto-setup preview scene if needed
            if (!sceneManager.IsSetup && previewModel != null)
            {
                sceneManager.Setup(stateMachineAsset, previewModel);
            }
            
            // Refresh and try to select entity
            entityBrowser.RefreshEntityList();
            
            if (!entityBrowser.HasSelection)
            {
                if (stateMachineAsset != null)
                {
                    entityBrowser.SelectByStateMachine(stateMachineAsset);
                }
                
                if (!entityBrowser.HasSelection)
                {
                    entityBrowser.AutoSelectFirst();
                }
            }
            
            return entityBrowser.HasSelection;
        }
        
        /// <summary>
        /// Cancels any pending setup.
        /// </summary>
        private void CancelPendingSetup()
        {
            StopSetupCoroutine();
        }
        
        public void SetPreviewModel(GameObject model)
        {
            previewModel = model;
            TryInitializeRenderer();
        }
        
        public void Clear()
        {
            CancelPendingSetup();
            
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = null;
            isInitialized = false;
            
            timelineHelper?.Deactivate();
            
            hybridRenderer?.Dispose();
            hybridRenderer = null;
            rendererInitialized = false;
        }
        
        public void SetMessage(string message)
        {
            Clear();
            errorMessage = message;
        }
        
        #endregion
        
        #region IPreviewBackend Time Control
        
        public void SetNormalizedTime(float time)
        {
            timelineHelper?.ScrubToTime(time);
        }
        
        public void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            // For ECS backend, per-state normalized times are handled internally by the timeline controller.
            // The overall timeline position is set via SetNormalizedTime() which is called separately.
            // This method is primarily used by the PlayableGraph backend for direct clip time control.
        }
        
        public void SetTransitionProgress(float progress)
        {
            // For ECS backend, transition progress is handled by SetNormalizedTime which
            // correctly positions within the appropriate section based on overall timeline time.
            // ScrubTransition would conflict with ScrubState commands.
            // This method is primarily for PlayableGraph backend which doesn't use sections.
        }
        
        public void SetPlaying(bool playing)
        {
            if (playing)
            {
                timelineHelper?.Play();
            }
            else
            {
                timelineHelper?.Pause();
            }
        }
        
        public void StepFrames(int frameCount, float fps = 30f)
        {
            timelineHelper?.StepFrames(frameCount, fps);
        }
        
        #endregion
        
        #region IPreviewBackend Blend Control
        
        public void SetBlendPosition1D(float value)
        {
            RebuildTimelineForState(new float2(value, 0));
        }
        
        public void SetBlendPosition2D(float2 position)
        {
            RebuildTimelineForState(position);
        }
        
        public void SetBlendPosition1DImmediate(float value)
        {
            RebuildTimelineForState(new float2(value, 0));
        }
        
        public void SetBlendPosition2DImmediate(float2 position)
        {
            RebuildTimelineForState(position);
        }
        
        public void SetTransitionFromBlendPosition(float2 position)
        {
            RebuildTimelineForTransition(position, GetBlendPositionForState(transitionToState));
        }
        
        public void SetTransitionToBlendPosition(float2 position)
        {
            RebuildTimelineForTransition(GetBlendPositionForState(transitionFromState), position);
        }
        
        public void RebuildTransitionTimeline(float2 fromBlendPos, float2 toBlendPos)
        {
            RebuildTimelineForTransition(fromBlendPos, toBlendPos);
        }
        
        public void SetSoloClip(int clipIndex)
        {
            // Solo clip mode not supported in ECS preview.
            // Would require modifying clip weights in the ECS sampler buffers.
            // For now, solo clip only works in PlayableGraph preview mode.
        }
        
        /// <summary>
        /// Gets the blend position for a state from PreviewSettings.
        /// </summary>
        private static float2 GetBlendPositionForState(AnimationStateAsset state)
        {
            if (state == null) return float2.zero;
            var pos = PreviewSettings.GetBlendPosition(state);
            return new float2(pos.x, pos.y);
        }
        
        /// <summary>
        /// Rebuilds the timeline for a single state preview with the given blend position.
        /// </summary>
        private void RebuildTimelineForState(float2 blendPos)
        {
            if (!isInitialized || timelineHelper == null || currentState == null)
            {
                return;
            }
            
            timelineHelper.UpdateBlendPosition(currentState, blendPos);
        }
        
        /// <summary>
        /// Rebuilds the timeline for a transition preview with the given blend positions.
        /// </summary>
        private void RebuildTimelineForTransition(float2 fromBlendPos, float2 toBlendPos)
        {
            if (!isInitialized || timelineHelper == null)
            {
                return;
            }
            
            if (!IsTransitionPreview)
            {
                return;
            }
            
            var (transitionIndex, curveSource) = FindTransitionIndices(transitionFromState, transitionToState);
            var (transition, _, _, _) = GetTransitionInfo();
            
            timelineHelper.UpdateTransitionBlendPositions(
                transitionFromState,
                transitionToState,
                transition,
                transitionIndex,
                curveSource,
                fromBlendPos,
                toBlendPos);
        }
        
        #endregion
        
        #region IPreviewBackend Update & Render
        
        public bool Tick(float deltaTime)
        {
            // If setup coroutine is in progress, just request repaint to update waiting message
            if (pendingSetup != PendingSetupType.None)
            {
                return true;
            }
            
            // Tick ECS world ONLY in Edit mode - in Play mode, Unity handles updates automatically
            // Ticking in Play mode causes double-update which leads to stuttering/flickering
            if (!UnityEngine.Application.isPlaying)
            {
                TickEcsWorld(deltaTime);
            }
            
            // Always repaint - ECS systems may have changed animation state
            return true;
        }
        
        /// <summary>
        /// Ticks the ECS world to run animation systems.
        /// This is required for the AnimationTimelineControllerSystem to process commands
        /// and for the Apply*RenderRequestSystem to update animation state.
        /// </summary>
        private void TickEcsWorld(float deltaTime)
        {
            if (timelineHelper?.TargetWorld == null || !timelineHelper.TargetWorld.IsCreated)
            {
                return;
            }
            
            var world = timelineHelper.TargetWorld;
            
            try
            {
                // Set delta time for the world
                var timeData = new Unity.Core.TimeData(
                    (float)EditorApplication.timeSinceStartup,
                    deltaTime);
                world.SetTime(timeData);
                
                // Update the simulation system group - this runs:
                // - AnimationTimelineControllerSystem (processes commands, generates render requests)
                // - ApplyStateRenderRequestSystem (applies state weights to samplers)
                // - ApplyTransitionRenderRequestSystem (applies transition weights to samplers)
                // - ClipSamplingSystem (samples clips based on sampler weights)
                var simGroup = world.GetExistingSystemManaged<SimulationSystemGroup>();
                if (simGroup != null)
                {
                    simGroup.Update();
                }
                
                // Complete jobs before accessing results
                world.EntityManager.CompleteAllTrackedJobs();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[EcsPreviewBackend] Error ticking ECS world: {e.Message}\n{e.StackTrace}");
            }
        }
        
        public void Draw(Rect rect)
        {
            // Draw entity browser
            entityBrowser.DrawBrowser(rect);
            
            // Draw selection info overlay
            if (entityBrowser.HasSelection)
            {
                DrawSelectionOverlay(rect);
            }
            
            // Draw 3D preview if available
            if (rendererInitialized && hybridRenderer != null)
            {
                var samplers = worldService.GetActiveSamplers();
                hybridRenderer.Render(samplers, rect);
            }
        }
        
        public bool HandleInput(Rect rect)
        {
            if (rendererInitialized && hybridRenderer != null)
            {
                hybridRenderer.HandleCamera();
                return GUI.changed;
            }
            return false;
        }
        
        public void ResetCameraView()
        {
            hybridRenderer?.ResetCameraView();
        }
        
        public StatePreviewSnapshot GetSnapshot()
        {
            var position = timelineHelper?.GetPosition() ?? default;
            var activeRequest = timelineHelper?.GetActiveRequest() ?? ActiveRenderRequest.None;
            
            // Get current blend position from state asset
            var currentBlendPos = IsTransitionPreview 
                ? GetBlendPositionForState(transitionFromState)
                : GetBlendPositionForState(currentState);
            
            return new StatePreviewSnapshot
            {
                NormalizedTime = position.NormalizedTime,
                BlendPosition = currentBlendPos,
                TransitionProgress = activeRequest.Type == RenderRequestType.Transition 
                    ? position.SectionProgress 
                    : -1f,
                IsPlaying = false
            };
        }
        
        #endregion
        
        #region Private Helpers
        
        /// <summary>
        /// Initializes the timeline helper for the selected entity.
        /// </summary>
        private bool InitializeTimelineHelper()
        {
            if (!entityBrowser.HasSelection)
            {
                errorMessage = "No entity selected";
                return false;
            }
            
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (!timelineHelper.Initialize(entity, world, stateMachineAsset))
            {
                errorMessage = "Failed to initialize timeline control";
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Finds the StateMachineAsset that owns the given state.
        /// </summary>
        private StateMachineAsset FindOwningStateMachine(AnimationStateAsset state)
        {
            if (state == null) return null;
            
            var path = AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path)) return null;
            
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (mainAsset is StateMachineAsset sm)
            {
                return sm;
            }
            
            // Search all StateMachineAssets
            var guids = AssetDatabase.FindAssets("t:StateMachineAsset");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<StateMachineAsset>(assetPath);
                if (asset != null && asset.States.Contains(state))
                {
                    return asset;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Finds transition index and curve source for a transition.
        /// </summary>
        private (short transitionIndex, TransitionSource curveSource) FindTransitionIndices(
            AnimationStateAsset fromState, 
            AnimationStateAsset toState)
        {
            if (toState == null || stateMachineAsset == null)
            {
                return (-1, TransitionSource.State);
            }
            
            // Check from state's out transitions
            if (fromState?.OutTransitions != null)
            {
                for (int i = 0; i < fromState.OutTransitions.Count; i++)
                {
                    if (fromState.OutTransitions[i].ToState == toState)
                    {
                        return ((short)i, TransitionSource.State);
                    }
                }
            }
            
            // Check any state transitions
            if (stateMachineAsset.AnyStateTransitions != null)
            {
                for (int i = 0; i < stateMachineAsset.AnyStateTransitions.Count; i++)
                {
                    if (stateMachineAsset.AnyStateTransitions[i].ToState == toState)
                    {
                        return ((short)i, TransitionSource.AnyState);
                    }
                }
            }
            
            return (-1, TransitionSource.State);
        }
        
        /// <summary>
        /// Gets the transition duration from the transition definition.
        /// </summary>
        private float GetTransitionDuration()
        {
            var transition = FindTransition();
            return transition?.TransitionDuration ?? 0.25f;
        }
        
        /// <summary>
        /// Finds the StateOutTransition for the current from/to state pair.
        /// Returns null if not found.
        /// </summary>
        private StateOutTransition FindTransition()
        {
            if (transitionToState == null) 
                return null;
            
            // Check from state's transitions
            if (transitionFromState?.OutTransitions != null)
            {
                foreach (var t in transitionFromState.OutTransitions)
                {
                    if (t.ToState == transitionToState)
                        return t;
                }
            }
            
            // Check any state transitions
            if (stateMachineAsset?.AnyStateTransitions != null)
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
        /// Gets full transition info for compatibility.
        /// </summary>
        private (StateOutTransition transition, float duration, float exitTime, bool hasExitTime) GetTransitionInfo()
        {
            var t = FindTransition();
            if (t != null)
                return (t, t.TransitionDuration, t.EndTime, t.HasEndTime);
            return (null, 0.25f, 0f, false);
        }
        
        /// <summary>
        /// Tries to initialize the hybrid renderer for 3D preview.
        /// </summary>
        private void TryInitializeRenderer()
        {
            hybridRenderer?.Dispose();
            hybridRenderer = null;
            rendererInitialized = false;
            
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
        
        /// <summary>
        /// Draws the selection info overlay.
        /// </summary>
        private void DrawSelectionOverlay(Rect rect)
        {
            var entity = entityBrowser.SelectedEntity;
            var world = entityBrowser.SelectedWorld;
            
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(entity))
            {
                return;
            }
            
            var inspectorRect = new Rect(rect.x + rect.width - 260, rect.y + 10, 250, 180);
            EditorGUI.DrawRect(inspectorRect, new Color(0, 0, 0, 0.7f));
            
            using (new GUILayout.AreaScope(inspectorRect))
            {
                GUILayout.Space(10);
                GUILayout.Label("Timeline Control", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                var position = timelineHelper?.GetPosition() ?? default;
                var activeRequest = timelineHelper?.GetActiveRequest() ?? ActiveRenderRequest.None;
                var currentSectionType = timelineHelper?.GetCurrentSectionType() ?? TimelineSectionType.State;
                
                GUILayout.Label($"Time: {position.CurrentTime:F2}s / {position.TotalDuration:F2}s");
                GUILayout.Label($"Section: [{position.CurrentSectionIndex}] {currentSectionType}");
                GUILayout.Label($"Progress: {position.SectionProgress:P0}");
                GUILayout.Label($"Request Type: {activeRequest.Type}");
                
                GUILayout.Space(10);
                
                if (activeRequest.Type == RenderRequestType.State)
                {
                    var stateReq = timelineHelper?.GetStateRequest() ?? AnimationStateRenderRequest.None;
                    GUILayout.Label($"State: {stateReq.StateIndex}");
                    GUILayout.Label($"Normalized Time: {stateReq.NormalizedTime:F2}");
                    GUILayout.Label($"Section Type: {stateReq.SectionType}");
                }
                else if (activeRequest.Type == RenderRequestType.Transition)
                {
                    var transReq = timelineHelper?.GetTransitionRequest() ?? AnimationTransitionRenderRequest.None;
                    GUILayout.Label($"From: {transReq.FromStateIndex} -> To: {transReq.ToStateIndex}");
                    GUILayout.Label($"Blend: {transReq.BlendWeight:P0}");
                }
            }
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Clear();
            timelineHelper?.Dispose();
            entityBrowser?.Dispose();
            worldService?.Dispose();
            sceneManager?.Dispose();
        }
        
        #endregion
    }
}
