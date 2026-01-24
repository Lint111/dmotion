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
        
        // Blend positions (with smoothing)
        private float2 blendPosition;
        private float2 targetBlendPosition;
        private float2 toBlendPosition;
        private float2 targetToBlendPosition;
        private const float BlendSmoothSpeed = 8f;
        
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
        
        #endregion
        
        #region IPreviewBackend Initialization
        
        public void CreatePreviewForState(AnimationStateAsset state)
        {
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
            
            // Ensure we have an entity to work with
            if (!EnsureEntitySelected())
            {
                return;
            }
            
            // Initialize timeline control
            if (!InitializeTimelineHelper())
            {
                return;
            }
            
            // Setup timeline for state preview
            timelineHelper.SetupStatePreview(state, blendPosition);
            
            isInitialized = true;
        }
        
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float duration)
        {
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
            
            // Ensure we have an entity to work with
            if (!EnsureEntitySelected())
            {
                return;
            }
            
            // Initialize timeline control
            if (!InitializeTimelineHelper())
            {
                return;
            }
            
            // Find transition definition for curve lookup and timing
            var (transitionIndex, curveSource) = FindTransitionIndices(fromState, toState);
            var (transition, _, _, _) = GetTransitionInfo();
            
            // Setup timeline for transition preview - pass the transition directly
            // so TimelineControlHelper can extract all timing info
            timelineHelper.SetupTransitionPreview(
                fromState,
                toState,
                transition,
                transitionIndex,
                curveSource,
                blendPosition,
                toBlendPosition);
            
            isInitialized = true;
        }
        
        public void SetPreviewModel(GameObject model)
        {
            previewModel = model;
            TryInitializeRenderer();
        }
        
        public void Clear()
        {
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
            // The timeline controller handles this internally based on transition progress
            // For now, we map this to transition progress
            // TODO: Add more granular control if needed
        }
        
        public void SetTransitionProgress(float progress)
        {
            timelineHelper?.ScrubTransition(progress);
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
            targetBlendPosition = new float2(value, 0);
        }
        
        public void SetBlendPosition2D(float2 position)
        {
            targetBlendPosition = position;
        }
        
        public void SetBlendPosition1DImmediate(float value)
        {
            blendPosition = new float2(value, 0);
            targetBlendPosition = blendPosition;
            ApplyBlendPositionToTimeline();
        }
        
        public void SetBlendPosition2DImmediate(float2 position)
        {
            blendPosition = position;
            targetBlendPosition = position;
            ApplyBlendPositionToTimeline();
        }
        
        public void SetTransitionFromBlendPosition(float2 position)
        {
            blendPosition = position;
            targetBlendPosition = position;
            ApplyBlendPositionToTimeline();
        }
        
        public void SetTransitionToBlendPosition(float2 position)
        {
            toBlendPosition = position;
            targetToBlendPosition = position;
            ApplyBlendPositionToTimeline();
        }
        
        public void SetSoloClip(int clipIndex)
        {
            // TODO: Implement solo clip mode
        }
        
        private void ApplyBlendPositionToTimeline()
        {
            if (!isInitialized || timelineHelper == null) return;
            
            if (IsTransitionPreview)
            {
                var (transitionIndex, curveSource) = FindTransitionIndices(transitionFromState, transitionToState);
                timelineHelper.UpdateTransitionBlendPositions(
                    transitionFromState,
                    transitionToState,
                    GetTransitionDuration(),
                    transitionIndex,
                    curveSource,
                    blendPosition,
                    toBlendPosition);
            }
            else if (currentState != null)
            {
                timelineHelper.UpdateBlendPosition(currentState, blendPosition);
            }
        }
        
        #endregion
        
        #region IPreviewBackend Update & Render
        
        public bool Tick(float deltaTime)
        {
            bool needsRepaint = false;
            
            // Smooth blend position interpolation
            if (math.any(blendPosition != targetBlendPosition))
            {
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
                ApplyBlendPositionToTimeline();
                needsRepaint = true;
            }
            
            // Smooth to-state blend position interpolation
            if (math.any(toBlendPosition != targetToBlendPosition))
            {
                var diff = targetToBlendPosition - toBlendPosition;
                var maxStep = BlendSmoothSpeed * deltaTime;
                
                if (math.length(diff) <= maxStep)
                {
                    toBlendPosition = targetToBlendPosition;
                }
                else
                {
                    toBlendPosition += math.normalize(diff) * maxStep;
                }
                ApplyBlendPositionToTimeline();
                needsRepaint = true;
            }
            
            return needsRepaint;
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
        
        public PreviewSnapshot GetSnapshot()
        {
            var position = timelineHelper?.GetPosition() ?? default;
            var activeRequest = timelineHelper?.GetActiveRequest() ?? ActiveRenderRequest.None;
            
            return new PreviewSnapshot
            {
                IsInitialized = isInitialized,
                ErrorMessage = errorMessage,
                NormalizedTime = position.NormalizedTime,
                BlendPosition = blendPosition,
                TransitionProgress = activeRequest.Type == RenderRequestType.Transition 
                    ? position.SectionProgress 
                    : -1f
            };
        }
        
        #endregion
        
        #region Private Helpers
        
        /// <summary>
        /// Ensures an entity is selected for preview.
        /// </summary>
        private bool EnsureEntitySelected()
        {
            // Auto-setup preview scene if needed
            if (!sceneManager.IsSetup && previewModel != null)
            {
                sceneManager.Setup(stateMachineAsset, previewModel);
            }
            
            // Refresh and select entity
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
            
            if (!entityBrowser.HasSelection)
            {
                errorMessage = "No animation entity found.\nEnter Play mode with an animated character.";
                return false;
            }
            
            return true;
        }
        
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
                
                GUILayout.Label($"Time: {position.CurrentTime:F2}s / {position.TotalDuration:F2}s");
                GUILayout.Label($"Section: {position.CurrentSectionIndex}");
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
            Clear();
            timelineHelper?.Dispose();
            entityBrowser?.Dispose();
            worldService?.Dispose();
            sceneManager?.Dispose();
        }
        
        #endregion
    }
}
