using System;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for preview backends implementing unified state management.
    /// Subclasses implement the abstract template methods for their specific rendering approach.
    /// 
    /// This base class provides:
    /// - Unified PreviewTarget handling (states and transitions use the same code path)
    /// - Unified time state management
    /// - Unified parameter state management with smooth interpolation
    /// - Legacy IPreviewBackend implementation that delegates to unified methods
    /// </summary>
    public abstract class PreviewBackendBase : IPreviewBackend
    {
        #region Constants
        
        /// <summary>
        /// Speed of blend position interpolation.
        /// </summary>
        protected const float BlendSmoothSpeed = 8f;
        
        #endregion
        
        #region State
        
        /// <summary>
        /// Current preview target (state or transition).
        /// </summary>
        protected PreviewTarget currentTarget;
        
        /// <summary>
        /// Current time state.
        /// </summary>
        protected PreviewTimeState timeState = PreviewTimeState.Default;
        
        /// <summary>
        /// Current parameter state.
        /// </summary>
        protected PreviewParameterState parameterState = PreviewParameterState.Default;
        
        /// <summary>
        /// Error message if preview failed.
        /// </summary>
        protected string errorMessage;
        
        /// <summary>
        /// Whether the backend is initialized.
        /// </summary>
        protected bool isInitialized;
        
        /// <summary>
        /// Preview model (armature).
        /// </summary>
        protected GameObject previewModel;
        
        /// <summary>
        /// Camera state for persistence.
        /// </summary>
        protected PlayableGraphPreview.CameraState cameraState;
        
        #endregion
        
        #region IPreviewBackend Properties
        
        /// <summary>
        /// The preview mode this backend implements.
        /// </summary>
        public abstract PreviewMode Mode { get; }
        
        /// <summary>
        /// Whether the backend is initialized and ready for preview.
        /// </summary>
        public virtual bool IsInitialized => isInitialized;
        
        /// <summary>
        /// Error message if initialization or preview failed.
        /// </summary>
        public virtual string ErrorMessage => errorMessage;
        
        /// <summary>
        /// The currently previewed state (null if previewing a transition).
        /// </summary>
        public virtual AnimationStateAsset CurrentState => currentTarget?.PrimaryState;
        
        /// <summary>
        /// Whether currently previewing a transition.
        /// </summary>
        public virtual bool IsTransitionPreview => currentTarget?.IsTransition ?? false;
        
        /// <summary>
        /// Camera state for persistence across backend switches.
        /// </summary>
        public virtual PlayableGraphPreview.CameraState CameraState
        {
            get => cameraState;
            set => cameraState = value;
        }
        
        /// <summary>
        /// Layer composition preview interface. Returns null by default.
        /// Override in subclasses that support multi-layer preview.
        /// </summary>
        public virtual ILayerCompositionPreview LayerComposition => null;
        
        #endregion
        
        #region Unified API - Public Methods
        
        /// <summary>
        /// Creates a preview for the given target (state or transition).
        /// This is the unified entry point that replaces both CreatePreviewForState and CreateTransitionPreview.
        /// </summary>
        public virtual void CreatePreview(PreviewTarget target)
        {
            currentTarget = target;
            errorMessage = null;
            isInitialized = false;
            
            if (target == null || !target.IsValid)
            {
                errorMessage = "Invalid preview target";
                return;
            }
            
            // Initialize default time/parameter state based on target type
            if (target.IsTransition)
            {
                var transition = target as TransitionPreviewTarget;
                timeState = PreviewTimeState.ForTransition(0f, 0f, 0f);
                parameterState = PreviewParameterState.ForTransition(
                    transition?.FromBlendPosition ?? float2.zero,
                    transition?.ToBlendPosition ?? float2.zero);
            }
            else
            {
                timeState = PreviewTimeState.ForState(0f);
                parameterState = PreviewParameterState.ForState(float2.zero);
            }
            
            // Delegate to subclass for specific setup
            OnTargetChanged(target);
        }
        
        /// <summary>
        /// Sets the unified time state.
        /// </summary>
        public virtual void SetTimeState(PreviewTimeState newTimeState)
        {
            timeState = newTimeState;
            OnTimeStateChanged(timeState);
        }
        
        /// <summary>
        /// Sets the unified parameter state.
        /// </summary>
        public virtual void SetParameterState(PreviewParameterState newParameterState)
        {
            parameterState = newParameterState;
            OnParameterStateChanged(parameterState);
        }
        
        /// <summary>
        /// Gets a snapshot of the current preview state.
        /// </summary>
        public virtual StatePreviewSnapshot GetSnapshot()
        {
            return new StatePreviewSnapshot
            {
                NormalizedTime = timeState.NormalizedTime,
                BlendPosition = parameterState.BlendPosition,
                BlendWeights = GetCurrentBlendWeights(),
                TransitionProgress = IsTransitionPreview ? timeState.TransitionProgress : -1f,
                IsPlaying = timeState.IsPlaying
            };
        }
        
        #endregion
        
        #region Legacy IPreviewBackend Implementation (delegates to unified API)
        
        /// <summary>
        /// Creates a preview for the given state.
        /// Legacy method - use CreatePreview(PreviewTarget) instead.
        /// </summary>
        public virtual void CreatePreviewForState(AnimationStateAsset state)
        {
            if (state == null)
            {
                errorMessage = "No state selected";
                return;
            }
            
            var stateMachine = FindOwningStateMachine(state);
            if (stateMachine == null)
            {
                errorMessage = $"Could not find StateMachineAsset for state: {state.name}";
                return;
            }
            
            var target = new StatePreviewTarget(state, stateMachine);
            CreatePreview(target);
        }
        
        /// <summary>
        /// Creates a preview for a transition between two states.
        /// Legacy method - use CreatePreview(PreviewTarget) instead.
        /// </summary>
        public virtual void CreateTransitionPreview(AnimationStateAsset fromState, AnimationStateAsset toState, float transitionDuration)
        {
            if (toState == null)
            {
                errorMessage = "No to-state selected";
                return;
            }
            
            var stateMachine = FindOwningStateMachine(toState);
            if (stateMachine == null)
            {
                errorMessage = $"Could not find StateMachineAsset for state: {toState.name}";
                return;
            }
            
            var target = new TransitionPreviewTarget(fromState, toState, stateMachine, transitionDuration);
            CreatePreview(target);
        }
        
        /// <summary>
        /// Sets the preview model (armature with Animator).
        /// </summary>
        public virtual void SetPreviewModel(GameObject model)
        {
            previewModel = model;
            OnPreviewModelChanged(model);
        }
        
        /// <summary>
        /// Clears the current preview.
        /// </summary>
        public virtual void Clear()
        {
            currentTarget = null;
            errorMessage = null;
            isInitialized = false;
            timeState = PreviewTimeState.Default;
            parameterState = PreviewParameterState.Default;
            OnCleared();
        }
        
        /// <summary>
        /// Sets an info/error message without a preview.
        /// </summary>
        public virtual void SetMessage(string message)
        {
            Clear();
            errorMessage = message;
        }
        
        /// <summary>
        /// Sets the normalized sample time (0-1).
        /// </summary>
        public virtual void SetNormalizedTime(float normalizedTime)
        {
            timeState.NormalizedTime = normalizedTime;
            timeState.FromStateTime = normalizedTime;
            OnTimeStateChanged(timeState);
        }
        
        /// <summary>
        /// Sets per-state normalized times for transition preview.
        /// </summary>
        public virtual void SetTransitionStateNormalizedTimes(float fromNormalized, float toNormalized)
        {
            timeState.FromStateTime = fromNormalized;
            timeState.ToStateTime = toNormalized;
            OnTimeStateChanged(timeState);
        }
        
        /// <summary>
        /// Sets the transition progress (0 = from state, 1 = to state).
        /// </summary>
        public virtual void SetTransitionProgress(float progress)
        {
            timeState.TransitionProgress = progress;
            OnTimeStateChanged(timeState);
        }
        
        /// <summary>
        /// Sets the playback state.
        /// </summary>
        public virtual void SetPlaying(bool playing)
        {
            timeState.IsPlaying = playing;
            OnPlayStateChanged(playing);
        }
        
        /// <summary>
        /// Steps the animation by the given number of frames.
        /// Uses UnityEditor.AnimationUtility.GetAnimationClipSettings frame rate if available, otherwise defaults to 60fps.
        /// </summary>
        public virtual void StepFrames(int frameCount, float fps = 60f)
        {
            if (currentTarget == null) return;

            var frameDuration = 1f / fps;
            var totalDuration = currentTarget.Duration;
            if (totalDuration <= 0) return;

            var timeStep = (frameDuration * frameCount) / totalDuration;
            timeState.NormalizedTime = math.frac(timeState.NormalizedTime + timeStep);
            OnTimeStateChanged(timeState);
        }
        
        /// <summary>
        /// Sets the 1D blend position with smooth transition.
        /// </summary>
        public virtual void SetBlendPosition1D(float value)
        {
            parameterState.TargetBlendPosition = new float2(value, 0);
        }
        
        /// <summary>
        /// Sets the 2D blend position with smooth transition.
        /// </summary>
        public virtual void SetBlendPosition2D(float2 position)
        {
            parameterState.TargetBlendPosition = position;
        }
        
        /// <summary>
        /// Sets the 1D blend position immediately (no smoothing).
        /// </summary>
        public virtual void SetBlendPosition1DImmediate(float value)
        {
            parameterState.BlendPosition = new float2(value, 0);
            parameterState.TargetBlendPosition = parameterState.BlendPosition;
            OnParameterStateChanged(parameterState);
        }
        
        /// <summary>
        /// Sets the 2D blend position immediately (no smoothing).
        /// </summary>
        public virtual void SetBlendPosition2DImmediate(float2 position)
        {
            parameterState.BlendPosition = position;
            parameterState.TargetBlendPosition = position;
            OnParameterStateChanged(parameterState);
        }
        
        /// <summary>
        /// Sets the blend position for the "from" state in transition preview.
        /// </summary>
        public virtual void SetTransitionFromBlendPosition(float2 position)
        {
            parameterState.BlendPosition = position;
            parameterState.TargetBlendPosition = position;
            OnParameterStateChanged(parameterState);
        }
        
        /// <summary>
        /// Sets the blend position for the "to" state in transition preview.
        /// </summary>
        public virtual void SetTransitionToBlendPosition(float2 position)
        {
            parameterState.ToBlendPosition = position;
            parameterState.TargetToBlendPosition = position;
            OnParameterStateChanged(parameterState);
        }
        
        /// <summary>
        /// Rebuilds the transition timeline with current blend positions.
        /// Called when transition properties (duration, exit time) change.
        /// Base implementation just updates blend positions.
        /// </summary>
        public virtual void RebuildTransitionTimeline(float2 fromBlendPos, float2 toBlendPos)
        {
            // Base implementation - just update blend positions
            // Derived classes (like EcsPreviewBackend) can override for full rebuild
            SetTransitionFromBlendPosition(fromBlendPos);
            SetTransitionToBlendPosition(toBlendPos);
        }
        
        /// <summary>
        /// Sets the solo clip index (-1 for blended mode).
        /// </summary>
        public virtual void SetSoloClip(int clipIndex)
        {
            parameterState.SoloClipIndex = clipIndex;
            OnParameterStateChanged(parameterState);
        }
        
        /// <summary>
        /// Updates smooth transitions. Call every frame.
        /// </summary>
        public virtual bool Tick(float deltaTime)
        {
            bool needsRepaint = false;
            
            // Interpolate blend positions
            if (parameterState.NeedsInterpolation)
            {
                parameterState.Interpolate(BlendSmoothSpeed, deltaTime);
                OnParameterStateChanged(parameterState);
                needsRepaint = true;
            }
            
            // Delegate to subclass for additional tick logic
            needsRepaint |= OnTick(deltaTime);
            
            return needsRepaint;
        }
        
        /// <summary>
        /// Draws the preview in the given rect.
        /// </summary>
        public abstract void Draw(Rect rect);
        
        /// <summary>
        /// Handles camera input for the preview.
        /// </summary>
        public abstract bool HandleInput(Rect rect);
        
        /// <summary>
        /// Resets the camera to the default view.
        /// </summary>
        public abstract void ResetCameraView();
        
        /// <summary>
        /// Disposes resources.
        /// </summary>
        public virtual void Dispose()
        {
            Clear();
            OnDispose();
        }
        
        #endregion
        
        #region Template Methods (Override in Subclasses)
        
        /// <summary>
        /// Called when the preview target changes.
        /// Subclasses should set up rendering resources for the new target.
        /// </summary>
        protected abstract void OnTargetChanged(PreviewTarget target);
        
        /// <summary>
        /// Called when time state changes.
        /// Subclasses should update animation sampling.
        /// </summary>
        protected abstract void OnTimeStateChanged(PreviewTimeState timeState);
        
        /// <summary>
        /// Called when parameter state changes.
        /// Subclasses should update blend parameters.
        /// </summary>
        protected abstract void OnParameterStateChanged(PreviewParameterState parameterState);
        
        /// <summary>
        /// Called when play state changes.
        /// </summary>
        protected virtual void OnPlayStateChanged(bool isPlaying) { }
        
        /// <summary>
        /// Called when preview model changes.
        /// </summary>
        protected virtual void OnPreviewModelChanged(GameObject model) { }
        
        /// <summary>
        /// Called when preview is cleared.
        /// </summary>
        protected virtual void OnCleared() { }
        
        /// <summary>
        /// Called every tick for additional update logic.
        /// </summary>
        /// <returns>True if repaint is needed.</returns>
        protected virtual bool OnTick(float deltaTime) => false;
        
        /// <summary>
        /// Called on dispose for cleanup.
        /// </summary>
        protected virtual void OnDispose() { }
        
        /// <summary>
        /// Gets the current blend weights for the snapshot.
        /// </summary>
        protected virtual float[] GetCurrentBlendWeights() => null;
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Finds the StateMachineAsset that owns the given state.
        /// </summary>
        protected static StateMachineAsset FindOwningStateMachine(AnimationStateAsset state)
        {
            if (state == null) return null;
            
            // Try to find via AssetDatabase
            var path = UnityEditor.AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path)) return null;
            
            var mainAsset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
            return mainAsset as StateMachineAsset;
        }
        
        #endregion
    }
}
