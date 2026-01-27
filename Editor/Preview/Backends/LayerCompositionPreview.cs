using System;
using System.Collections.Generic;
using System.Linq;
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
    /// </summary>
    internal class LayerCompositionPreview : ILayerCompositionPreview
    {
        #region Nested Types
        
        /// <summary>
        /// Internal state for a single layer in the preview.
        /// </summary>
        private class LayerData
        {
            public int Index;
            public string Name;
            public float Weight;
            public LayerBlendMode BlendMode;
            public bool IsEnabled;
            public AvatarMask AvatarMask;
            public AnimationStateAsset CurrentState;
            public float NormalizedTime;
            public float2 BlendPosition;
            
            // Playable graph components
            public AnimationMixerPlayable StateMixer;
            public AnimationClipPlayable[] ClipPlayables;
            public float[] ClipWeights;
            public float[] ClipDurations;
            public float[] ClipThresholds; // For 1D blend
            public float2[] ClipPositions; // For 2D blend
            public bool Is2DBlend;
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
            if (isInitialized && model != null)
            {
                // Rebuild with new model
                DestroyPreviewInstance();
                if (TryInstantiateModel(model))
                {
                    BuildPlayableGraph();
                    RestoreCameraState();
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
                BlendPosition = layer.BlendPosition
            };
        }
        
        #endregion
        
        #region Layer Weight Control
        
        public void SetLayerWeight(int layerIndex, float weight)
        {
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
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            var layer = layers[layerIndex];
            if (layer.CurrentState == state) return;
            
            layer.CurrentState = state;
            RebuildLayerPlayables(layer);
        }
        
        public void SetLayerNormalizedTime(int layerIndex, float normalizedTime)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            layers[layerIndex].NormalizedTime = Mathf.Clamp01(normalizedTime);
            SyncLayerClipTimes(layers[layerIndex]);
        }
        
        public void SetLayerBlendPosition(int layerIndex, float2 position)
        {
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            
            var layer = layers[layerIndex];
            layer.BlendPosition = position;
            UpdateLayerBlendWeights(layer);
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
                // Sample the graph
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
            if (!playableGraph.IsValid()) return;
            
            // Disconnect old playables
            if (layer.StateMixer.IsValid())
            {
                playableGraph.Disconnect(layerMixer, layer.Index);
                layer.StateMixer.Destroy();
            }
            
            if (layer.ClipPlayables != null)
            {
                foreach (var clip in layer.ClipPlayables)
                {
                    if (clip.IsValid()) clip.Destroy();
                }
            }
            
            var state = layer.CurrentState;
            if (state == null)
            {
                // Create empty playable
                layer.StateMixer = AnimationMixerPlayable.Create(playableGraph, 0);
                layer.ClipPlayables = Array.Empty<AnimationClipPlayable>();
                layer.ClipWeights = Array.Empty<float>();
                layer.ClipDurations = Array.Empty<float>();
                layer.ClipThresholds = Array.Empty<float>();
                layer.ClipPositions = Array.Empty<float2>();
                layer.Is2DBlend = false;
            }
            else
            {
                BuildStatePlayables(layer, state);
            }
            
            // Connect to layer mixer
            playableGraph.Connect(layer.StateMixer, 0, layerMixer, layer.Index);
            
            // Sync times
            SyncLayerClipTimes(layer);
        }
        
        private void BuildStatePlayables(LayerData layer, AnimationStateAsset state)
        {
            switch (state)
            {
                case SingleClipStateAsset singleClip:
                    BuildSingleClipPlayables(layer, singleClip);
                    break;
                    
                case LinearBlendStateAsset linearBlend:
                    BuildLinearBlendPlayables(layer, linearBlend);
                    break;
                    
                case Directional2DBlendStateAsset blend2D:
                    Build2DBlendPlayables(layer, blend2D);
                    break;
                    
                default:
                    // Unsupported state type - create empty
                    layer.StateMixer = AnimationMixerPlayable.Create(playableGraph, 0);
                    layer.ClipPlayables = Array.Empty<AnimationClipPlayable>();
                    layer.ClipWeights = Array.Empty<float>();
                    layer.ClipDurations = Array.Empty<float>();
                    layer.ClipThresholds = Array.Empty<float>();
                    layer.ClipPositions = Array.Empty<float2>();
                    layer.Is2DBlend = false;
                    break;
            }
        }
        
        private void BuildSingleClipPlayables(LayerData layer, SingleClipStateAsset state)
        {
            var clip = state.Clip?.Clip;
            
            layer.StateMixer = AnimationMixerPlayable.Create(playableGraph, 1);
            layer.Is2DBlend = false;
            
            if (clip != null)
            {
                var clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);
                layer.ClipPlayables = new[] { clipPlayable };
                layer.ClipWeights = new[] { 1f };
                layer.ClipDurations = new[] { clip.length };
                layer.ClipThresholds = new[] { 0f };
                layer.ClipPositions = new[] { float2.zero };
                
                playableGraph.Connect(clipPlayable, 0, layer.StateMixer, 0);
                layer.StateMixer.SetInputWeight(0, 1f);
            }
            else
            {
                layer.ClipPlayables = Array.Empty<AnimationClipPlayable>();
                layer.ClipWeights = Array.Empty<float>();
                layer.ClipDurations = Array.Empty<float>();
                layer.ClipThresholds = Array.Empty<float>();
                layer.ClipPositions = Array.Empty<float2>();
            }
        }
        
        private void BuildLinearBlendPlayables(LayerData layer, LinearBlendStateAsset state)
        {
            var blendClips = state.BlendClips?.Where(c => c.Clip?.Clip != null).ToArray() 
                             ?? Array.Empty<ClipWithThreshold>();
            
            layer.StateMixer = AnimationMixerPlayable.Create(playableGraph, blendClips.Length);
            layer.Is2DBlend = false;
            
            layer.ClipPlayables = new AnimationClipPlayable[blendClips.Length];
            layer.ClipWeights = new float[blendClips.Length];
            layer.ClipDurations = new float[blendClips.Length];
            layer.ClipThresholds = new float[blendClips.Length];
            layer.ClipPositions = new float2[blendClips.Length];
            
            for (int i = 0; i < blendClips.Length; i++)
            {
                var clip = blendClips[i].Clip.Clip;
                var clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);
                layer.ClipPlayables[i] = clipPlayable;
                layer.ClipDurations[i] = clip.length;
                layer.ClipThresholds[i] = blendClips[i].Threshold;
                layer.ClipPositions[i] = new float2(blendClips[i].Threshold, 0);
                
                playableGraph.Connect(clipPlayable, 0, layer.StateMixer, i);
            }
            
            UpdateLayerBlendWeights(layer);
        }
        
        private void Build2DBlendPlayables(LayerData layer, Directional2DBlendStateAsset state)
        {
            var blendClips = state.BlendClips?.Where(c => c.Clip?.Clip != null).ToArray() 
                             ?? Array.Empty<Directional2DClipWithPosition>();
            
            layer.StateMixer = AnimationMixerPlayable.Create(playableGraph, blendClips.Length);
            layer.Is2DBlend = true;
            
            layer.ClipPlayables = new AnimationClipPlayable[blendClips.Length];
            layer.ClipWeights = new float[blendClips.Length];
            layer.ClipDurations = new float[blendClips.Length];
            layer.ClipThresholds = new float[blendClips.Length];
            layer.ClipPositions = new float2[blendClips.Length];
            
            for (int i = 0; i < blendClips.Length; i++)
            {
                var clip = blendClips[i].Clip.Clip;
                var clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);
                layer.ClipPlayables[i] = clipPlayable;
                layer.ClipDurations[i] = clip.length;
                layer.ClipThresholds[i] = blendClips[i].Position.x;
                layer.ClipPositions[i] = blendClips[i].Position;
                
                playableGraph.Connect(clipPlayable, 0, layer.StateMixer, i);
            }
            
            UpdateLayerBlendWeights(layer);
        }
        
        #endregion
        
        #region Private - Weight Updates
        
        private void UpdateLayerMixerWeights()
        {
            if (!layerMixer.IsValid()) return;
            
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                float weight = layer.IsEnabled ? layer.Weight : 0f;
                layerMixer.SetInputWeight(i, weight);
            }
        }
        
        private void UpdateLayerBlendWeights(LayerData layer)
        {
            if (layer.ClipWeights == null || layer.ClipWeights.Length == 0) return;
            
            if (layer.Is2DBlend)
            {
                Directional2DBlendUtils.CalculateWeights(
                    layer.BlendPosition, 
                    layer.ClipPositions, 
                    layer.ClipWeights,
                    Blend2DAlgorithm.SimpleDirectional);
            }
            else
            {
                LinearBlendStateUtils.CalculateWeights(
                    layer.BlendPosition.x, 
                    layer.ClipThresholds, 
                    layer.ClipWeights);
            }
            
            // Apply weights to mixer
            if (layer.StateMixer.IsValid())
            {
                for (int i = 0; i < layer.ClipWeights.Length; i++)
                {
                    layer.StateMixer.SetInputWeight(i, layer.ClipWeights[i]);
                }
            }
        }
        
        private void SyncLayerClipTimes(LayerData layer)
        {
            if (layer.ClipPlayables == null) return;
            
            for (int i = 0; i < layer.ClipPlayables.Length; i++)
            {
                if (!layer.ClipPlayables[i].IsValid()) continue;
                
                float duration = layer.ClipDurations[i];
                if (duration <= 0) continue;
                
                float clipTime = layer.NormalizedTime * duration;
                layer.ClipPlayables[i].SetTime(clipTime);
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
