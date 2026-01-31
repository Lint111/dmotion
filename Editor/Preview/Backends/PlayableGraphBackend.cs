using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview backend using Unity's PlayableGraph (Authoring mode).
    /// Wraps the existing PreviewRenderer for backward compatibility.
    /// Supports multi-layer preview via LayerCompositionPreview.
    /// </summary>
    internal class PlayableGraphBackend : IPreviewBackend
    {
        #region State
        
        private readonly PreviewRenderer renderer;
        private LayerCompositionPreview layerCompositionPreview;
        private AnimationStateAsset currentState;
        private AnimationStateAsset transitionFromState;
        private AnimationStateAsset transitionToState;
        private float transitionProgress;
        private float normalizedTime;
        private float2 blendPosition;
        private GameObject previewModel;
        private bool needsRebuildAfterPlayMode;
        private StateMachineAsset cachedStateMachineForRebuild;
        
        #endregion
        
        #region Constructor
        
        public PlayableGraphBackend()
        {
            renderer = new PreviewRenderer();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // About to enter Play mode - cache what we need to rebuild
                    if (layerCompositionPreview != null)
                    {
                        cachedStateMachineForRebuild = layerCompositionPreview.StateMachine;
                        needsRebuildAfterPlayMode = true;
                    }
                    else if (currentState != null)
                    {
                        needsRebuildAfterPlayMode = true;
                    }
                    break;
                    
                case PlayModeStateChange.EnteredEditMode:
                    // Exited Play mode - rebuild if needed
                    if (needsRebuildAfterPlayMode)
                    {
                        needsRebuildAfterPlayMode = false;
                        
                        // The preview model reference is now invalid (destroyed during play mode)
                        // Clear it so SetPreviewModel will recreate
                        previewModel = null;
                        
                        // LayerCompositionPreview needs to be recreated
                        // The caller (AnimationPreviewWindow) should detect IsInitialized=false and rebuild
                        layerCompositionPreview?.Dispose();
                        layerCompositionPreview = null;
                        
                        // PreviewRenderer also needs cleanup
                        renderer.Dispose();
                    }
                    break;
            }
        }
        
        #endregion
        
        #region IPreviewBackend Properties
        
        public PreviewMode Mode => PreviewMode.Authoring;
        
        public bool IsInitialized => layerCompositionPreview?.IsInitialized ?? renderer.IsInitialized;
        
        public string ErrorMessage => layerCompositionPreview?.ErrorMessage ?? renderer.ErrorMessage;
        
        public AnimationStateAsset CurrentState => currentState;
        
        public bool IsTransitionPreview => renderer.IsTransitionPreview;
        
        public PlayableGraphPreview.CameraState CameraState
        {
            get => layerCompositionPreview?.CameraState ?? renderer.CameraState;
            set
            {
                if (layerCompositionPreview != null)
                    layerCompositionPreview.CameraState = value;
                else
                    renderer.CameraState = value;
            }
        }
        
        /// <summary>
        /// Layer composition preview interface for multi-layer state machines.
        /// Returns null for single-layer state machines.
        /// </summary>
        public ILayerCompositionPreview LayerComposition => layerCompositionPreview;
        
        #endregion
        
        #region IPreviewBackend Initialization
        
        public void CreatePreviewForState(AnimationStateAsset state)
        {
            // Save camera state before clearing
            var savedCameraState = CameraState;

            // Clear layer composition if switching to state preview
            DisposeLayerComposition();

            currentState = state;
            transitionFromState = null;
            transitionToState = null;

            renderer.CreatePreviewForState(state);

            // Restore camera state after preview creation
            if (savedCameraState.IsValid)
            {
                renderer.CameraState = savedCameraState;
            }
        }
        
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration)
        {
            // Save camera state before clearing
            var savedCameraState = CameraState;

            // Clear layer composition if switching to transition preview
            DisposeLayerComposition();

            currentState = null;
            transitionFromState = fromState;
            transitionToState = toState;

            renderer.CreateTransitionPreview(fromState, toState, transitionDuration);

            // Restore camera state after preview creation
            if (savedCameraState.IsValid)
            {
                renderer.CameraState = savedCameraState;
            }
        }
        
        /// <summary>
        /// Creates a multi-layer composition preview for the given state machine.
        /// The state machine must be in multi-layer mode (IsMultiLayer == true).
        /// </summary>
        /// <param name="stateMachine">The multi-layer state machine to preview.</param>
        public void CreateLayerCompositionPreview(StateMachineAsset stateMachine)
        {
            // Save camera state from current preview before clearing
            var savedCameraState = CameraState;

            // Clear single-state preview
            renderer.Clear();
            currentState = null;
            transitionFromState = null;
            transitionToState = null;

            // Create or reuse layer composition preview
            if (layerCompositionPreview == null)
            {
                layerCompositionPreview = new LayerCompositionPreview();
            }

            // Set model if we have one
            if (previewModel != null)
            {
                layerCompositionPreview.SetPreviewModel(previewModel);
            }

            layerCompositionPreview.Initialize(stateMachine);

            // Restore camera state after initialization
            if (savedCameraState.IsValid)
            {
                layerCompositionPreview.CameraState = savedCameraState;
            }
        }
        
        public void SetPreviewModel(GameObject model)
        {
            previewModel = model;
            renderer.PreviewModel = model;
            layerCompositionPreview?.SetPreviewModel(model);
        }
        
        public void Clear()
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            
            renderer.Clear();
            layerCompositionPreview?.Clear();
        }
        
        public void SetMessage(string message)
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            
            DisposeLayerComposition();
            renderer.SetMessage(message);
        }
        
        private void DisposeLayerComposition()
        {
            layerCompositionPreview?.Dispose();
            layerCompositionPreview = null;
        }
        
        #endregion
        
        #region IPreviewBackend Time Control
        
        public void SetNormalizedTime(float time)
        {
            normalizedTime = time;
            
            if (IsTransitionPreview)
            {
                renderer.SetTransitionNormalizedTime(time);
            }
            else
            {
                renderer.SetNormalizedTime(time);
            }
        }
        
        public void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            renderer.SetTransitionStateNormalizedTimes(fromNormalized, toNormalized);
        }
        
        public void SetTransitionProgress(float progress)
        {
            transitionProgress = progress;
            renderer.SetTransitionProgress(progress);
        }
        
        public void SetPlaying(bool playing)
        {
            // PlayableGraph backend doesn't need special handling - 
            // time is controlled by timeline calling SetNormalizedTime
        }
        
        public void StepFrames(int frameCount, float fps = 30f)
        {
            // PlayableGraph backend doesn't need special handling -
            // time is controlled by timeline
        }
        
        #endregion
        
        #region IPreviewBackend Blend Control
        
        public void SetBlendPosition1D(float value)
        {
            blendPosition = new float2(value, 0);
            renderer.SetBlendPosition1D(value);
        }
        
        public void SetBlendPosition2D(float2 position)
        {
            blendPosition = position;
            renderer.SetBlendPosition2D(new Vector2(position.x, position.y));
        }
        
        public void SetBlendPosition1DImmediate(float value)
        {
            blendPosition = new float2(value, 0);
            renderer.SetBlendPosition1DImmediate(value);
        }
        
        public void SetBlendPosition2DImmediate(float2 position)
        {
            blendPosition = position;
            renderer.SetBlendPosition2DImmediate(new Vector2(position.x, position.y));
        }
        
        public void SetTransitionFromBlendPosition(float2 position)
        {
            renderer.SetTransitionFromBlendPosition(new Vector2(position.x, position.y));
        }
        
        public void SetTransitionToBlendPosition(float2 position)
        {
            renderer.SetTransitionToBlendPosition(new Vector2(position.x, position.y));
        }
        
        public void RebuildTransitionTimeline(float2 fromBlendPos, float2 toBlendPos)
        {
            // PlayableGraph backend doesn't need explicit rebuild - just update blend positions
            renderer.SetTransitionFromBlendPosition(new Vector2(fromBlendPos.x, fromBlendPos.y));
            renderer.SetTransitionToBlendPosition(new Vector2(toBlendPos.x, toBlendPos.y));
        }
        
        public void SetSoloClip(int clipIndex)
        {
            renderer.SetSoloClip(clipIndex);
        }
        
        #endregion
        
        #region IPreviewBackend Update & Render
        
        public bool Tick(float deltaTime)
        {
            if (layerCompositionPreview != null)
            {
                return layerCompositionPreview.Tick(deltaTime);
            }
            return renderer.Tick(deltaTime);
        }
        
        public void Draw(Rect rect)
        {
            if (layerCompositionPreview != null)
            {
                layerCompositionPreview.Draw(rect);
            }
            else
            {
                renderer.Draw(rect);
            }
        }
        
        public bool HandleInput(Rect rect)
        {
            if (layerCompositionPreview != null)
            {
                return layerCompositionPreview.HandleInput(rect);
            }
            return renderer.HandleInput(rect);
        }
        
        public void ResetCameraView()
        {
            if (layerCompositionPreview != null)
            {
                layerCompositionPreview.ResetCameraView();
            }
            else
            {
                renderer.ResetCameraView();
            }
        }
        
        public StatePreviewSnapshot GetSnapshot()
        {
            return new StatePreviewSnapshot
            {
                NormalizedTime = normalizedTime,
                BlendPosition = blendPosition,
                BlendWeights = renderer.GetCurrentBlendWeights(),
                TransitionProgress = IsTransitionPreview ? renderer.GetTransitionProgress() : -1f,
                IsPlaying = false
            };
        }
        
        /// <summary>
        /// Whether the backend is currently in layer composition mode.
        /// </summary>
        public bool IsLayerCompositionMode => layerCompositionPreview != null && layerCompositionPreview.IsInitialized;
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            renderer.Dispose();
            DisposeLayerComposition();
        }
        
        #endregion
    }
}
