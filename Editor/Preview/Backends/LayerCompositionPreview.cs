using System;
using System.Collections.Generic;
using System.Linq;
using DMotion;
using DMotion.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DMotion.Editor
{
    /// <summary>
    /// Preview implementation for multi-layer animation composition.
    /// Uses AnimationLayerMixerPlayable to blend multiple layers with proper
    /// override/additive blending and avatar mask support.
    /// Supports both single-state and transition preview per layer.
    /// </summary>
    internal class LayerCompositionPreview : ILayerCompositionPreview
    {
        #region Nested Types
        
        /// <summary>
        /// Internal state for a single layer in the preview.
        /// Uses StatePlayableBuilder for playable management.
        /// </summary>
        private class LayerData
        {
            public int Index;
            public string Name;
            public float Weight;
            public LayerBlendMode BlendMode;
            public bool IsEnabled;
            public AvatarMask AvatarMask;
            
            // Single-state mode
            public AnimationStateAsset CurrentState;
            public float NormalizedTime;
            public float2 BlendPosition;
            public StatePlayableResult StateResult;
            
            // Transition mode
            public bool IsTransitionMode;
            public AnimationStateAsset FromState;
            public AnimationStateAsset ToState;
            public float TransitionProgress;
            public float FromNormalizedTime;
            public float ToNormalizedTime;
            public float2 FromBlendPosition;
            public float2 ToBlendPosition;
            public StatePlayableResult FromResult;
            public StatePlayableResult ToResult;
            public AnimationMixerPlayable TransitionMixer;
            
            /// <summary>
            /// The root playable for this layer (either StateResult.RootPlayable or TransitionMixer).
            /// </summary>
            public Playable RootPlayable => IsTransitionMode && TransitionMixer.IsValid() 
                ? TransitionMixer 
                : StateResult.RootPlayable;
        }
        
        #endregion
        
        #region Fields
        
        private PreviewRenderUtility previewRenderUtility;
        private GameObject gameObject;
        private Animator animator;
        private SkinnedMeshRenderer skinnedMeshRenderer;
        private Mesh previewMesh;
        private PlayableGraph playableGraph;
        private AnimationLayerMixerPlayable layerMixer;
        
        private StateMachineAsset stateMachine;
        private List<LayerData> layers = new();
        private bool isInitialized;
        private string errorMessage;
        
        // Camera state
        private float camDistance;
        private Vector3 camPivot;
        private Vector3 lookAtOffset;
        private Vector2 camEuler;
        private Vector2 lastMousePosition;
        private bool isMouseDrag;
        private PlayableGraphPreview.CameraState savedCameraState;
        
        private static readonly Color PreviewBackground = new(0.15f, 0.15f, 0.15f);
        private static readonly Color MessageTextColor = new(0.7f, 0.7f, 0.7f);
        
        #endregion
        
        #region IAnimationPreview Properties
        
        public bool IsInitialized => isInitialized;
        public string ErrorMessage => errorMessage;

        public void SetPlaying(bool playing)
        {
            // Layer composition uses external time control via SetGlobalNormalizedTime
            // or individual layer control via SetLayerNormalizedTime.
            // This method is required by IAnimationPreview but not directly used for
            // internal playback state in this implementation.
        }

        public void StepFrames(int frames, float frameRate)
        {
            // Frame stepping is handled by the higher-level controller which calls SetGlobalNormalizedTime
        }
        
        public PlayableGraphPreview.CameraState CameraState
        {
            get => new PlayableGraphPreview.CameraState
            {
                Distance = camDistance,
                Pivot = camPivot,
                LookAtOffset = lookAtOffset,
                Euler = camEuler,
                IsValid = previewRenderUtility != null
            };
            set
            {
                if (!value.IsValid) return;
                savedCameraState = value;
                camDistance = value.Distance;
                camPivot = value.Pivot;
                lookAtOffset = value.LookAtOffset;
                camEuler = value.Euler;
                UpdateCameraPosition();
            }
        }
        
        #endregion
        
        #region ILayerCompositionPreview Properties
        
        public int LayerCount => layers.Count;
        
        /// <summary>
        /// The state machine this preview is initialized for.
        /// Used to check if preview needs to be recreated for a different state machine.
        /// </summary>
        public StateMachineAsset StateMachine => stateMachine;
        
        #endregion
        
        #region Initialization
        
        public void Initialize(StateMachineAsset stateMachine)
        {
            Dispose();
            this.stateMachine = stateMachine;
            errorMessage = null;
            
            if (stateMachine == null)
            {
                errorMessage = "No state machine provided";
                return;
            }
            
            if (!stateMachine.IsMultiLayer)
            {
                errorMessage = "State machine is not multi-layer.\nUse state preview for single-layer machines.";
                return;
            }
            
            // Build layer data from state machine
            BuildLayerData();
            
            if (layers.Count == 0)
            {
                errorMessage = "No valid layers found";
                return;
            }
            
            // Find a model from the first layer's clips
            if (!TryFindAndSetupModel())
            {
                errorMessage = "Could not find model\nfor layer animations.\n\nDrag a model prefab to\nthe Preview Model field.";
                return;
            }
            
            isInitialized = true;
        }
        
        public void SetPreviewModel(GameObject model)
        {
            if (gameObject == model) return;

            gameObject = model;

            // If already initialized, rebuild with new model
            if (isInitialized && model != null)
            {
                DestroyPreviewInstance();
                if (TryInstantiateModel(model))
                {
                    BuildPlayableGraph();
                    RestoreCameraState();
                }
            }
            // If not initialized yet but we have a model and state machine, try to initialize now
            else if (!isInitialized && model != null && stateMachine != null)
            {
                // Clear error message since we now have a model
                errorMessage = null;

                // Try to complete initialization with the new model
                if (TryInstantiateModel(model))
                {
                    BuildPlayableGraph();
                    RestoreCameraState();
                    isInitialized = true;
                }
                else
                {
                    errorMessage = "Failed to instantiate preview model";
                }
            }
        }
        
        public void Clear()
        {
            Dispose();
            stateMachine = null;
            errorMessage = null;
        }
        
        public void SetMessage(string message)
        {
            Dispose();
            errorMessage = message;
        }
        
        #endregion
        
        #region Layer Info
        
        public LayerPreviewState[] GetLayerStates()
        {
            var states = new LayerPreviewState[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                states[i] = GetLayerState(i);
            }
            return states;
        }
        
        public LayerPreviewState GetLayerState(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count)
                return default;
                
            var layer = layers[layerIndex];
            return new LayerPreviewState
            {
                LayerIndex = layer.Index,
                Name = layer.Name,
                Weight = layer.Weight,
                BlendMode = layer.BlendMode,
                IsEnabled = layer.IsEnabled,
                HasBoneMask = layer.AvatarMask != null,
                CurrentState = layer.CurrentState,
                NormalizedTime = layer.NormalizedTime,
                BlendPosition = layer.BlendPosition,
                // Transition state
                IsTransitionMode = layer.IsTransitionMode,
                TransitionFromState = layer.FromState,
                TransitionToState = layer.ToState,
                TransitionProgress = layer.TransitionProgress
            };
        }
        
        #endregion
        
        #region Layer Weight Control
        
        public void SetLayerWeight(int layerIndex, float weight)
        {
            // Layer 0 is always weight 1.0 (base opaque layer)
            if (layerIndex == 0) return;

            if (layerIndex < 0 || layerIndex >= layers.Count) return;

            layers[layerIndex].Weight = Mathf.Clamp01(weight);
            UpdateLayerMixerWeights();
        }
        
        public float GetLayerWeight(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return 0f;
            return layers[layerIndex].Weight;
        }
        
        public void SetLayerEnabled(int layerIndex, bool enabled)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            layers[layerIndex].IsEnabled = enabled;
            UpdateLayerMixerWeights();
        }
        
        public bool IsLayerEnabled(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return false;
            return layers[layerIndex].IsEnabled;
        }
        
        #endregion
        
        #region Per-Layer Animation Control
        
        public void SetLayerState(int layerIndex, AnimationStateAsset state)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count)
            {
                Debug.LogWarning($"[LayerCompositionPreview] SetLayerState: Invalid layer index {layerIndex}, layers.Count={layers.Count}");
                return;
            }

            var layer = layers[layerIndex];
            
            // Clear transition mode when setting single state
            if (layer.IsTransitionMode)
            {
                ClearLayerTransitionInternal(layer);
            }
            
            // Always update and rebuild - don't early return on same state
            // This ensures state changes are always reflected in the playable graph
            bool stateChanged = layer.CurrentState != state;
            layer.CurrentState = state;
            
            if (stateChanged || state != null)
            {
                RebuildLayerPlayables(layer);
                UpdateLayerMixerWeights(); // Ensure unassigned layers don't affect blend
            }
        }
        
        public void SetLayerNormalizedTime(int layerIndex, float normalizedTime)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;

            var layer = layers[layerIndex];
            normalizedTime = Mathf.Clamp01(normalizedTime);
            
            if (layer.IsTransitionMode)
            {
                // In transition mode, set both from and to times
                layer.FromNormalizedTime = normalizedTime;
                layer.ToNormalizedTime = normalizedTime;
            }
            else
            {
                layer.NormalizedTime = normalizedTime;
            }
            
            SyncLayerClipTimes(layer);
        }
        
        public void SetLayerBlendPosition(int layerIndex, float2 position)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;

            var layer = layers[layerIndex];
            layer.BlendPosition = position;
            UpdateLayerBlendWeights(layer);
            
            // Sync clip times when blend position changes
            // This ensures newly-active clips (weight > 0) have correct times
            SyncLayerClipTimes(layer);
        }
        
        #endregion
        
        #region Per-Layer Transition Control
        
        public void SetLayerTransition(int layerIndex, AnimationStateAsset fromState, AnimationStateAsset toState)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count)
            {
                Debug.LogWarning($"[LayerCompositionPreview] SetLayerTransition: Invalid layer index {layerIndex}");
                return;
            }
            
            if (toState == null)
            {
                Debug.LogWarning($"[LayerCompositionPreview] SetLayerTransition: toState cannot be null");
                return;
            }
            
            var layer = layers[layerIndex];
            
            // Clear any existing state/transition
            ClearLayerTransitionInternal(layer);
            DisposeLayerPlayables(layer);
            
            // Set up transition mode
            layer.IsTransitionMode = true;
            layer.FromState = fromState;
            layer.ToState = toState;
            layer.TransitionProgress = 0f;
            layer.FromNormalizedTime = 0f;
            layer.ToNormalizedTime = 0f;
            layer.FromBlendPosition = float2.zero;
            layer.ToBlendPosition = float2.zero;
            layer.CurrentState = null; // Clear single-state mode
            
            // Build transition playables
            RebuildLayerTransitionPlayables(layer);
            UpdateLayerMixerWeights();
        }
        
        public void SetLayerTransitionProgress(int layerIndex, float progress)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            var layer = layers[layerIndex];
            if (!layer.IsTransitionMode) return;
            
            layer.TransitionProgress = Mathf.Clamp01(progress);
            UpdateTransitionMixerWeights(layer);
        }
        
        public void SetLayerTransitionBlendPositions(int layerIndex, float2 fromBlendPosition, float2 toBlendPosition)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            var layer = layers[layerIndex];
            if (!layer.IsTransitionMode) return;
            
            layer.FromBlendPosition = fromBlendPosition;
            layer.ToBlendPosition = toBlendPosition;
            
            // Update blend weights for both states
            StatePlayableBuilder.UpdateBlendWeights(ref layer.FromResult, fromBlendPosition);
            StatePlayableBuilder.UpdateBlendWeights(ref layer.ToResult, toBlendPosition);
            
            // Sync clip times after weight changes
            SyncLayerClipTimes(layer);
        }
        
        public void SetLayerTransitionNormalizedTimes(int layerIndex, float fromNormalizedTime, float toNormalizedTime)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            var layer = layers[layerIndex];
            if (!layer.IsTransitionMode) return;
            
            layer.FromNormalizedTime = Mathf.Clamp01(fromNormalizedTime);
            layer.ToNormalizedTime = Mathf.Clamp01(toNormalizedTime);
            
            SyncLayerClipTimes(layer);
        }
        
        public void ClearLayerTransition(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            var layer = layers[layerIndex];
            if (!layer.IsTransitionMode) return;
            
            ClearLayerTransitionInternal(layer);
            
            // Rebuild as single-state (will be empty if no CurrentState)
            RebuildLayerPlayables(layer);
            UpdateLayerMixerWeights();
        }
        
        private void ClearLayerTransitionInternal(LayerData layer)
        {
            if (!layer.IsTransitionMode) return;
            
            // Dispose transition playables
            StatePlayableBuilder.Dispose(ref layer.FromResult);
            StatePlayableBuilder.Dispose(ref layer.ToResult);
            
            if (layer.TransitionMixer.IsValid())
            {
                layer.TransitionMixer.Destroy();
                layer.TransitionMixer = default;
            }
            
            layer.IsTransitionMode = false;
            layer.FromState = null;
            layer.ToState = null;
            layer.TransitionProgress = 0f;
            layer.FromNormalizedTime = 0f;
            layer.ToNormalizedTime = 0f;
            layer.FromBlendPosition = float2.zero;
            layer.ToBlendPosition = float2.zero;
        }
        
        #endregion
        
        #region Global Time Control
        
        public void SetGlobalNormalizedTime(float normalizedTime)
        {
            normalizedTime = Mathf.Clamp01(normalizedTime);
            foreach (var layer in layers)
            {
                layer.NormalizedTime = normalizedTime;
                SyncLayerClipTimes(layer);
            }
        }
        
        #endregion
        
        #region Update & Render
        
        public bool Tick(float deltaTime)
        {
            // Currently no smooth transitions - could add blend position smoothing later
            return false;
        }
        
        public void Draw(Rect rect)
        {
            if (rect.width <= 0 || rect.height <= 0) return;

            EditorGUI.DrawRect(rect, PreviewBackground);

            if (isInitialized && skinnedMeshRenderer != null && playableGraph.IsValid())
            {
                // Ensure clip times and transition weights are synced immediately before sampling
                foreach (var layer in layers)
                {
                    SyncLayerClipTimes(layer);
                    if (layer.IsTransitionMode)
                    {
                        UpdateTransitionMixerWeights(layer);
                    }
                }
                
                // Sample the graph for rendering
                // Time parameter is 0 because we set individual clip times manually via SetTime()
                AnimationMode.BeginSampling();
                AnimationMode.SamplePlayableGraph(playableGraph, 0, 0);
                AnimationMode.EndSampling();
                
                // Render
                skinnedMeshRenderer.BakeMesh(previewMesh);
                previewRenderUtility.BeginPreview(rect, GUIStyle.none);
                
                for (int i = 0; i < previewMesh.subMeshCount; i++)
                {
                    previewRenderUtility.DrawMesh(previewMesh, Matrix4x4.identity,
                        skinnedMeshRenderer.sharedMaterials[i], i);
                }
                
                previewRenderUtility.camera.Render();
                var resultRender = previewRenderUtility.EndPreview();
                GUI.DrawTexture(rect, resultRender, ScaleMode.StretchToFill, false);
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = MessageTextColor }
                };
                GUI.Label(rect, errorMessage, style);
            }
        }
        
        public bool HandleInput(Rect rect)
        {
            var evt = Event.current;
            if (evt == null) return false;
            
            if (rect.Contains(evt.mousePosition) && isInitialized)
            {
                HandleCamera();
                
                if (evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        public void ResetCameraView()
        {
            if (skinnedMeshRenderer == null || previewRenderUtility == null) return;
            
            var bounds = skinnedMeshRenderer.bounds;
            camDistance = bounds.size.magnitude * 2f;
            camPivot = bounds.center;
            lookAtOffset = Vector3.zero;
            camEuler = new Vector2(0, -45);
            
            UpdateCameraPosition();
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            // Dispose layer playables before destroying graph
            foreach (var layer in layers)
            {
                DisposeLayerPlayables(layer);
            }
            
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }
            
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
            
            previewRenderUtility?.Cleanup();
            previewRenderUtility = null;
            
            DestroyPreviewInstance();
            
            layers.Clear();
            isInitialized = false;
        }
        
        #endregion
        
        #region Private - Layer Setup
        
        private void BuildLayerData()
        {
            layers.Clear();
            
            if (stateMachine == null) return;
            
            int index = 0;
            foreach (var layerAsset in stateMachine.GetLayers())
            {
                var layerData = new LayerData
                {
                    Index = index,
                    Name = layerAsset.name,
                    Weight = layerAsset.Weight,
                    BlendMode = layerAsset.BlendMode,
                    IsEnabled = true,
                    AvatarMask = layerAsset.AvatarMask,
                    CurrentState = layerAsset.NestedStateMachine?.DefaultState,
                    NormalizedTime = 0f,
                    BlendPosition = float2.zero
                };
                
                layers.Add(layerData);
                index++;
            }
        }
        
        private bool TryFindAndSetupModel()
        {
            // If we already have a model set, use it
            if (gameObject != null)
            {
                return TryInstantiateModel(gameObject);
            }
            
            // Try to find a model from layer clips
            foreach (var layer in layers)
            {
                if (layer.CurrentState == null) continue;
                
                foreach (var clipAsset in layer.CurrentState.Clips)
                {
                    if (clipAsset?.Clip == null) continue;
                    
                    if (TryFindModelFromClip(clipAsset.Clip, out var model))
                    {
                        gameObject = model;
                        return TryInstantiateModel(model);
                    }
                }
            }
            
            return false;
        }
        
        private bool TryFindModelFromClip(AnimationClip clip, out GameObject model)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            var owner = AssetDatabase.LoadMainAssetAtPath(path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            
            var avatar = importer?.sourceAvatar;
            if (avatar == null && owner is GameObject ownerGo && ownerGo.TryGetComponent<Animator>(out var anim))
            {
                avatar = anim.avatar;
            }
            
            if (avatar != null)
            {
                var avatarPath = AssetDatabase.GetAssetPath(avatar);
                var avatarOwner = AssetDatabase.LoadMainAssetAtPath(avatarPath);
                if (avatarOwner is GameObject go && go.GetComponentInChildren<Animator>() != null)
                {
                    model = go;
                    return true;
                }
            }
            
            model = null;
            return false;
        }
        
        private bool TryInstantiateModel(GameObject template)
        {
            if (template == null || template.GetComponentInChildren<Animator>() == null)
                return false;
            
            DestroyPreviewInstance();
            
            var instance = UnityEngine.Object.Instantiate(template, Vector3.zero, Quaternion.identity);
            AnimatorUtility.DeoptimizeTransformHierarchy(instance);
            
            instance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | 
                                 HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;
            
            animator = instance.GetComponentInChildren<Animator>();
            skinnedMeshRenderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            instance.SetActive(false);
            
            skinnedMeshRenderer.transform.up = Vector3.up;
            skinnedMeshRenderer.transform.forward = Vector3.forward;
            
            previewMesh = new Mesh();
            
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }
            
            BuildPlayableGraph();
            CreatePreviewUtility();
            
            return true;
        }
        
        private void DestroyPreviewInstance()
        {
            if (skinnedMeshRenderer != null)
            {
                UnityEngine.Object.DestroyImmediate(skinnedMeshRenderer.transform.root.gameObject);
                skinnedMeshRenderer = null;
                UnityEngine.Object.DestroyImmediate(previewMesh);
                previewMesh = null;
            }
        }
        
        #endregion
        
        #region Private - Playable Graph
        
        private void BuildPlayableGraph()
        {
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }
            
            playableGraph = PlayableGraph.Create("LayerCompositionPreview");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            // Note: Do NOT call Play() - manual time control via SetTime() on individual clip playables

            var output = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
            
            if (layers.Count == 0)
            {
                return;
            }
            
            // Create layer mixer
            layerMixer = AnimationLayerMixerPlayable.Create(playableGraph, layers.Count);
            output.SetSourcePlayable(layerMixer);
            
            // Build each layer's playables
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                RebuildLayerPlayables(layer);
                
                // Set layer properties
                layerMixer.SetLayerAdditive((uint)i, layer.BlendMode == LayerBlendMode.Additive);
                
                if (layer.AvatarMask != null)
                {
                    layerMixer.SetLayerMaskFromAvatarMask((uint)i, layer.AvatarMask);
                }
            }
            
            UpdateLayerMixerWeights();
        }
        
        private void RebuildLayerPlayables(LayerData layer)
        {
            if (!playableGraph.IsValid())
            {
                Debug.LogWarning($"[LayerCompositionPreview] RebuildLayerPlayables: Playable graph is not valid!");
                return;
            }
            
            // Dispose old playables
            DisposeLayerPlayables(layer);
            
            // Build new state playables using StatePlayableBuilder
            layer.StateResult = StatePlayableBuilder.BuildForState(playableGraph, layer.CurrentState);
            
            // Update blend weights if this is a blend state
            if (layer.StateResult.IsValid)
            {
                StatePlayableBuilder.UpdateBlendWeights(ref layer.StateResult, layer.BlendPosition);
            }
            
            // Connect to layer mixer
            if (layer.StateResult.IsValid)
            {
                playableGraph.Connect(layer.StateResult.RootPlayable, 0, layerMixer, layer.Index);
            }
            
            // Sync times
            SyncLayerClipTimes(layer);
        }
        
        private void RebuildLayerTransitionPlayables(LayerData layer)
        {
            if (!playableGraph.IsValid())
            {
                Debug.LogWarning($"[LayerCompositionPreview] RebuildLayerTransitionPlayables: Playable graph is not valid!");
                return;
            }
            
            // Build from and to state playables
            layer.FromResult = StatePlayableBuilder.BuildForState(playableGraph, layer.FromState);
            layer.ToResult = StatePlayableBuilder.BuildForState(playableGraph, layer.ToState);
            
            // Create transition mixer (2 inputs: from and to)
            layer.TransitionMixer = AnimationMixerPlayable.Create(playableGraph, 2);
            
            // Connect from and to playables to transition mixer
            if (layer.FromResult.IsValid)
            {
                playableGraph.Connect(layer.FromResult.RootPlayable, 0, layer.TransitionMixer, 0);
            }
            if (layer.ToResult.IsValid)
            {
                playableGraph.Connect(layer.ToResult.RootPlayable, 0, layer.TransitionMixer, 1);
            }
            
            // Set initial weights
            UpdateTransitionMixerWeights(layer);
            
            // Update blend weights for both states
            StatePlayableBuilder.UpdateBlendWeights(ref layer.FromResult, layer.FromBlendPosition);
            StatePlayableBuilder.UpdateBlendWeights(ref layer.ToResult, layer.ToBlendPosition);
            
            // Connect transition mixer to layer mixer
            playableGraph.Connect(layer.TransitionMixer, 0, layerMixer, layer.Index);
            
            // Sync times
            SyncLayerClipTimes(layer);
        }
        
        private void DisposeLayerPlayables(LayerData layer)
        {
            // Disconnect from layer mixer first
            if (layerMixer.IsValid())
            {
                playableGraph.Disconnect(layerMixer, layer.Index);
            }
            
            // Dispose single-state playables
            StatePlayableBuilder.Dispose(ref layer.StateResult);
            
            // Dispose transition playables
            StatePlayableBuilder.Dispose(ref layer.FromResult);
            StatePlayableBuilder.Dispose(ref layer.ToResult);
            
            if (layer.TransitionMixer.IsValid())
            {
                layer.TransitionMixer.Destroy();
                layer.TransitionMixer = default;
            }
        }
        
        private void UpdateTransitionMixerWeights(LayerData layer)
        {
            if (!layer.TransitionMixer.IsValid()) return;
            
            float progress = layer.TransitionProgress;
            layer.TransitionMixer.SetInputWeight(0, 1f - progress); // from fades out
            layer.TransitionMixer.SetInputWeight(1, progress);       // to fades in
        }
        
        #endregion
        
        #region Private - Weight Updates
        
        private void UpdateLayerMixerWeights()
        {
            if (!layerMixer.IsValid()) return;
            
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                // Layer is assigned if it has a state (single mode) or is in transition mode
                bool isAssigned = layer.CurrentState != null || layer.IsTransitionMode;
                float weight = (layer.IsEnabled && isAssigned) ? layer.Weight : 0f;
                layerMixer.SetInputWeight(i, weight);
            }
        }
        
        private void UpdateLayerBlendWeights(LayerData layer)
        {
            if (layer.IsTransitionMode)
            {
                // In transition mode, update both from and to states
                StatePlayableBuilder.UpdateBlendWeights(ref layer.FromResult, layer.FromBlendPosition);
                StatePlayableBuilder.UpdateBlendWeights(ref layer.ToResult, layer.ToBlendPosition);
            }
            else
            {
                // Single-state mode
                StatePlayableBuilder.UpdateBlendWeights(ref layer.StateResult, layer.BlendPosition);
            }
        }
        
        private void SyncLayerClipTimes(LayerData layer)
        {
            if (layer.IsTransitionMode)
            {
                // Sync both from and to states with their respective times
                StatePlayableBuilder.SyncClipTimes(ref layer.FromResult, layer.FromNormalizedTime);
                StatePlayableBuilder.SyncClipTimes(ref layer.ToResult, layer.ToNormalizedTime);
            }
            else
            {
                // Single-state mode
                StatePlayableBuilder.SyncClipTimes(ref layer.StateResult, layer.NormalizedTime);
            }
        }
        
        #endregion
        
        #region Private - Camera
        
        private void CreatePreviewUtility()
        {
            previewRenderUtility?.Cleanup();
            previewRenderUtility = new PreviewRenderUtility();
            
            var bounds = skinnedMeshRenderer.bounds;
            
            camPivot = bounds.center;
            camDistance = bounds.size.magnitude * 2f;
            lookAtOffset = Vector3.up * bounds.size.y * 0.3f;
            camEuler = new Vector2(0, -45);
            
            UpdateCameraPosition();
            
            previewRenderUtility.camera.nearClipPlane = 0.3f;
            previewRenderUtility.camera.farClipPlane = 3000f;
            
            var light1 = previewRenderUtility.lights[0];
            light1.type = LightType.Directional;
            light1.color = Color.white;
            light1.intensity = 1;
            light1.transform.rotation = previewRenderUtility.camera.transform.rotation;
        }
        
        private void UpdateCameraPosition()
        {
            if (previewRenderUtility == null) return;
            
            previewRenderUtility.camera.transform.position =
                Quaternion.Euler(-camEuler.y, camEuler.x, 0) * (Vector3.up * camDistance - camPivot) + camPivot;
            previewRenderUtility.camera.transform.LookAt(camPivot + lookAtOffset);
            
            previewRenderUtility.lights[0].transform.rotation = previewRenderUtility.camera.transform.rotation;
        }
        
        private void RestoreCameraState()
        {
            if (savedCameraState.IsValid)
            {
                CameraState = savedCameraState;
            }
        }
        
        private void HandleCamera()
        {
            if (Event.current == null || previewRenderUtility == null) return;
            
            // Scroll wheel zoom
            if (Event.current.type == EventType.ScrollWheel)
            {
                var zoomDelta = Event.current.delta.y * 0.5f;
                camDistance = Mathf.Max(0.5f, camDistance + zoomDelta);
                UpdateCameraPosition();
                Event.current.Use();
                GUI.changed = true;
                return;
            }
            
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            int currentHotControl = GUIUtility.hotControl;
            
            if (currentHotControl != 0 && currentHotControl != controlId)
            {
                if (isMouseDrag)
                {
                    isMouseDrag = false;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                }
                return;
            }
            
            if (Event.current.GetTypeForControl(controlId) == EventType.MouseDown && currentHotControl == 0)
                GUIUtility.hotControl = controlId;
            
            var isMouseDown = Event.current.type == EventType.MouseDown;
            var isMouseUp = Event.current.type == EventType.MouseUp;
            
            if (Event.current.type == EventType.MouseDrag)
                isMouseDrag = true;
            
            if (isMouseDrag)
            {
                Vector2 delta = Event.current.mousePosition - lastMousePosition;
                
                if (Mathf.Abs(delta.x) > Screen.width) delta.x = 0;
                if (Mathf.Abs(delta.y) > Screen.height) delta.y = 0;
                
                camEuler += delta;
                camEuler.y = Mathf.Clamp(camEuler.y, -179, -1);
                
                UpdateCameraPosition();
                GUI.changed = true;
            }
            
            if (isMouseDown) EditorGUIUtility.SetWantsMouseJumping(1);
            if (isMouseUp)
            {
                EditorGUIUtility.SetWantsMouseJumping(0);
                isMouseDrag = false;
                
                if (GUIUtility.hotControl == controlId)
                    GUIUtility.hotControl = 0;
            }
            
            if (isMouseDown || isMouseDrag)
            {
                lastMousePosition = Event.current.mousePosition;
            }
        }
        
        #endregion
    }
}
