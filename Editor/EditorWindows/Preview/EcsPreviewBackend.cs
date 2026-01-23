using System;
using DMotion.Authoring;
using Latios.Kinemation;
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
    /// </summary>
    internal class EcsPreviewBackend : IPreviewBackend
    {
        #region State
        
        private EcsPreviewWorldService worldService;
        private AnimationStateAsset currentState;
        private AnimationStateAsset transitionFromState;
        private AnimationStateAsset transitionToState;
        private float transitionDuration;
        
        // Cached blob references (from baking or manual creation)
        private BlobAssetReference<StateMachineBlob> stateMachineBlob;
        private BlobAssetReference<SkeletonClipSetBlob> clipsBlob;
        
        // Preview state
        private float normalizedTime;
        private float2 blendPosition;
        private float transitionProgress;
        private string errorMessage;
        private bool isInitialized;
        
        // Camera state (not used in ECS mode, but required by interface)
        private PlayableGraphPreview.CameraState cameraState;
        
        // Preview model
        private GameObject previewModel;
        
        #endregion
        
        #region Constructor
        
        public EcsPreviewBackend()
        {
            worldService = new EcsPreviewWorldService();
        }
        
        #endregion
        
        #region IPreviewBackend Properties
        
        public PreviewMode Mode => PreviewMode.EcsRuntime;
        
        public bool IsInitialized => isInitialized && worldService.IsInitialized;
        
        public string ErrorMessage => errorMessage;
        
        public AnimationStateAsset CurrentState => currentState;
        
        public bool IsTransitionPreview => transitionToState != null;
        
        public PlayableGraphPreview.CameraState CameraState
        {
            get => cameraState;
            set => cameraState = value;
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
            
            // TODO: Phase 6 - Full ECS preview rendering
            // For now, show a message indicating ECS preview is in development
            // The actual implementation requires:
            // 1. Baking the state machine to blob
            // 2. Creating skeleton components from the preview model
            // 3. Running the full animation pipeline
            // 4. Extracting bone transforms and rendering
            
            errorMessage = "ECS Runtime preview\nis in development.\n\n" +
                          $"State: {state.name}\n" +
                          "Switch to 'Authoring' mode\nfor preview.";
            
            // Create world if not already created
            if (!worldService.IsInitialized)
            {
                worldService.CreateWorld();
            }
            
            // Mark as "initialized" so the mode toggle works
            // Even though we're not fully rendering yet
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
            
            if (toState == null)
            {
                errorMessage = "No target state for transition";
                return;
            }
            
            // TODO: Phase 6 - Full ECS preview rendering
            errorMessage = "ECS Runtime preview\nis in development.\n\n" +
                          $"Transition:\n{fromState?.name ?? "Any State"}\n  ->\n{toState.name}\n\n" +
                          "Switch to 'Authoring' mode\nfor preview.";
            
            // Create world if not already created
            if (!worldService.IsInitialized)
            {
                worldService.CreateWorld();
            }
            
            isInitialized = true;
        }
        
        public void SetPreviewModel(GameObject model)
        {
            previewModel = model;
            // TODO: Phase 6 - Use model to set up skeleton components
        }
        
        public void Clear()
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = null;
            isInitialized = false;
            
            worldService.DestroyPreviewEntity();
        }
        
        public void SetMessage(string message)
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = message;
            isInitialized = false;
        }
        
        #endregion
        
        #region IPreviewBackend Time Control
        
        public void SetNormalizedTime(float time)
        {
            normalizedTime = time;
            // TODO: Phase 6 - Update ECS entity time
        }
        
        public void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            // TODO: Phase 6 - Update transition state times
        }
        
        public void SetTransitionProgress(float progress)
        {
            transitionProgress = progress;
            // TODO: Phase 6 - Update transition progress
        }
        
        #endregion
        
        #region IPreviewBackend Blend Control
        
        public void SetBlendPosition1D(float value)
        {
            blendPosition = new float2(value, 0);
            SetBlendParameters();
        }
        
        public void SetBlendPosition2D(float2 position)
        {
            blendPosition = position;
            SetBlendParameters();
        }
        
        public void SetBlendPosition1DImmediate(float value)
        {
            blendPosition = new float2(value, 0);
            SetBlendParameters();
        }
        
        public void SetBlendPosition2DImmediate(float2 position)
        {
            blendPosition = position;
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
            if (!worldService.IsInitialized) return;
            
            // TODO: Phase 6 - Set actual parameters on entity
            // This requires knowing the parameter hashes from the state asset
        }
        
        #endregion
        
        #region IPreviewBackend Update & Render
        
        public bool Tick(float deltaTime)
        {
            if (!worldService.IsInitialized) return false;
            
            worldService.Update(deltaTime);
            return false; // No smooth transitions in ECS mode (handled by systems)
        }
        
        public void Draw(Rect rect)
        {
            // Draw background
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            
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
            else if (isInitialized)
            {
                // TODO: Phase 6 - Render ECS-driven pose
                // Options:
                // 1. Extract bone transforms from ECS, apply to preview skeleton, use PreviewRenderUtility
                // 2. Use Entities Graphics (BRG) rendering in the preview world
                
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter
                };
                GUI.Label(rect, "ECS Preview Rendering\n(Phase 6 - TODO)", style);
            }
        }
        
        public bool HandleInput(Rect rect)
        {
            // Camera controls would go here
            // For now, no input handling in ECS mode
            return false;
        }
        
        public void ResetCameraView()
        {
            // Reset camera state
            cameraState = PlayableGraphPreview.CameraState.Invalid;
        }
        
        public PreviewSnapshot GetSnapshot()
        {
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
            worldService?.Dispose();
            worldService = null;
            
            // Dispose blob references if we own them
            // Note: In full implementation, these would come from baking
            // and their lifecycle would be managed accordingly
            if (stateMachineBlob.IsCreated)
            {
                stateMachineBlob.Dispose();
            }
            if (clipsBlob.IsCreated)
            {
                clipsBlob.Dispose();
            }
        }
        
        #endregion
    }
}
