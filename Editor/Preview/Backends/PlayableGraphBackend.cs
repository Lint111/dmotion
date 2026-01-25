using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview backend using Unity's PlayableGraph (Authoring mode).
    /// Wraps the existing PreviewRenderer for backward compatibility.
    /// </summary>
    internal class PlayableGraphBackend : IPreviewBackend
    {
        #region State
        
        private readonly PreviewRenderer renderer;
        private AnimationStateAsset currentState;
        private AnimationStateAsset transitionFromState;
        private AnimationStateAsset transitionToState;
        private float transitionProgress;
        private float normalizedTime;
        private float2 blendPosition;
        
        #endregion
        
        #region Constructor
        
        public PlayableGraphBackend()
        {
            renderer = new PreviewRenderer();
        }
        
        #endregion
        
        #region IPreviewBackend Properties
        
        public PreviewMode Mode => PreviewMode.Authoring;
        
        public bool IsInitialized => renderer.IsInitialized;
        
        public string ErrorMessage => renderer.ErrorMessage;
        
        public AnimationStateAsset CurrentState => currentState;
        
        public bool IsTransitionPreview => renderer.IsTransitionPreview;
        
        public PlayableGraphPreview.CameraState CameraState
        {
            get => renderer.CameraState;
            set => renderer.CameraState = value;
        }
        
        #endregion
        
        #region IPreviewBackend Initialization
        
        public void CreatePreviewForState(AnimationStateAsset state)
        {
            currentState = state;
            transitionFromState = null;
            transitionToState = null;
            
            renderer.CreatePreviewForState(state);
        }
        
        public void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration)
        {
            currentState = null;
            transitionFromState = fromState;
            transitionToState = toState;
            
            renderer.CreateTransitionPreview(fromState, toState, transitionDuration);
        }
        
        public void SetPreviewModel(GameObject model)
        {
            renderer.PreviewModel = model;
        }
        
        public void Clear()
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            
            renderer.Clear();
        }
        
        public void SetMessage(string message)
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            
            renderer.SetMessage(message);
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
            return renderer.Tick(deltaTime);
        }
        
        public void Draw(Rect rect)
        {
            renderer.Draw(rect);
        }
        
        public bool HandleInput(Rect rect)
        {
            return renderer.HandleInput(rect);
        }
        
        public void ResetCameraView()
        {
            renderer.ResetCameraView();
        }
        
        public PreviewSnapshot GetSnapshot()
        {
            var weights = renderer.GetCurrentBlendWeights();
            
            return new PreviewSnapshot
            {
                NormalizedTime = normalizedTime,
                BlendPosition = blendPosition,
                BlendWeights = weights,
                TransitionProgress = IsTransitionPreview ? renderer.GetTransitionProgress() : -1f,
                IsPlaying = false, // Playable backend doesn't track play state
                ErrorMessage = renderer.ErrorMessage,
                IsInitialized = renderer.IsInitialized
            };
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            renderer.Dispose();
        }
        
        #endregion
    }
}
