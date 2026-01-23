using System;
using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DMotion.Editor
{
    /// <summary>
    /// Renders animation poses based on ECS-extracted sampler data.
    /// Uses PlayableGraph for actual pose sampling (hybrid approach:
    /// ECS drives state machine logic, PlayableGraph samples poses).
    /// </summary>
    internal class EcsHybridRenderer : IDisposable
    {
        #region State
        
        private PreviewRenderUtility previewRenderUtility;
        private GameObject previewInstance;
        private Animator animator;
        private SkinnedMeshRenderer skinnedMeshRenderer;
        private Mesh previewMesh;
        
        private PlayableGraph playableGraph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] clipPlayables;
        private AnimationClip[] clips; // Cached clips from StateMachineAsset
        
        // Camera state
        private float camDistance;
        private Vector3 camPivot;
        private Vector3 lookAtOffset;
        private Vector2 camEuler;
        private Vector2 lastMousePosition;
        private bool isMouseDrag;
        
        private bool isInitialized;
        
        #endregion
        
        #region Properties
        
        public bool IsInitialized => isInitialized;
        
        public PlayableGraphPreview.CameraState CameraState
        {
            get => new PlayableGraphPreview.CameraState
            {
                Distance = camDistance,
                Pivot = camPivot,
                LookAtOffset = lookAtOffset,
                Euler = camEuler,
                IsValid = isInitialized
            };
            set
            {
                if (!value.IsValid) return;
                camDistance = value.Distance;
                camPivot = value.Pivot;
                lookAtOffset = value.LookAtOffset;
                camEuler = value.Euler;
                UpdateCameraPosition();
            }
        }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes the renderer with clips from a StateMachineAsset and a preview model.
        /// </summary>
        /// <param name="stateMachine">The state machine containing animation clips.</param>
        /// <param name="model">The preview model (must have Animator and SkinnedMeshRenderer).</param>
        /// <returns>True if initialization succeeded.</returns>
        public bool Initialize(StateMachineAsset stateMachine, GameObject model)
        {
            Dispose();
            
            if (stateMachine == null || model == null)
            {
                return false;
            }
            
            // Extract all clips from the state machine
            var clipList = new List<AnimationClip>();
            foreach (var clipAsset in stateMachine.Clips)
            {
                if (clipAsset?.Clip != null)
                {
                    clipList.Add(clipAsset.Clip);
                }
                else
                {
                    // Add null placeholder to maintain index mapping
                    clipList.Add(null);
                }
            }
            clips = clipList.ToArray();
            
            if (clips.Length == 0)
            {
                return false;
            }
            
            // Create preview instance
            if (!CreatePreviewInstance(model))
            {
                return false;
            }
            
            // Build PlayableGraph
            if (!BuildPlayableGraph())
            {
                Dispose();
                return false;
            }
            
            // Create preview render utility
            CreatePreviewUtility();
            
            AnimationMode.StartAnimationMode();
            isInitialized = true;
            return true;
        }
        
        private bool CreatePreviewInstance(GameObject template)
        {
            if (template.GetComponentInChildren<Animator>() == null)
            {
                return false;
            }
            
            previewInstance = UnityEngine.Object.Instantiate(template, Vector3.zero, Quaternion.identity);
            AnimatorUtility.DeoptimizeTransformHierarchy(previewInstance);
            
            previewInstance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | 
                                        HideFlags.HideInHierarchy | HideFlags.HideInInspector | 
                                        HideFlags.NotEditable;
            
            animator = previewInstance.GetComponentInChildren<Animator>();
            skinnedMeshRenderer = previewInstance.GetComponentInChildren<SkinnedMeshRenderer>();
            previewInstance.SetActive(false);
            
            if (skinnedMeshRenderer != null)
            {
                skinnedMeshRenderer.transform.up = Vector3.up;
                skinnedMeshRenderer.transform.forward = Vector3.forward;
            }
            
            previewMesh = new Mesh();
            return skinnedMeshRenderer != null;
        }
        
        private bool BuildPlayableGraph()
        {
            if (animator == null || clips == null || clips.Length == 0)
            {
                return false;
            }
            
            playableGraph = PlayableGraph.Create("ECS Hybrid Preview");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            
            // Create mixer with inputs for all clips
            mixer = AnimationMixerPlayable.Create(playableGraph, clips.Length);
            
            // Create clip playables
            clipPlayables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    clipPlayables[i] = AnimationClipPlayable.Create(playableGraph, clips[i]);
                    playableGraph.Connect(clipPlayables[i], 0, mixer, i);
                }
                else
                {
                    // Create empty playable for null clips
                    clipPlayables[i] = AnimationClipPlayable.Create(playableGraph, null);
                    playableGraph.Connect(clipPlayables[i], 0, mixer, i);
                }
                mixer.SetInputWeight(i, 0);
            }
            
            // Create output
            var output = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
            output.SetSourcePlayable(mixer);
            
            return true;
        }
        
        private void CreatePreviewUtility()
        {
            previewRenderUtility?.Cleanup();
            previewRenderUtility = new PreviewRenderUtility();


            if (skinnedMeshRenderer == null) return;

            var bounds = skinnedMeshRenderer.bounds;
            camPivot = bounds.center;
            camDistance = bounds.size.magnitude * 2f;
            lookAtOffset = Vector3.up * bounds.size.y * 0.3f;
            camEuler = new Vector2(0, -45);
            UpdateCameraPosition();

            previewRenderUtility.camera.nearClipPlane = 0.3f;
            previewRenderUtility.camera.farClipPlane = 3000f;

            var light = previewRenderUtility.lights[0];
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1;
            light.transform.rotation = previewRenderUtility.camera.transform.rotation;
        }

        #endregion

        #region Rendering


        /// <summary>
        /// Updates the pose based on extracted sampler data and renders.
        /// </summary>
        /// <param name="samplers">Sampler data extracted from ECS.</param>
        /// <param name="rect">The rect to render into.</param>
        public void Render(EcsPreviewWorldService.ExtractedSampler[] samplers, Rect rect)
        {
            if (!isInitialized || samplers == null || samplers.Length == 0) return;
            
            // Update playable weights and times from sampler data
            UpdateFromSamplers(samplers);
            
            // Sample the animation
            AnimationMode.BeginSampling();
            AnimationMode.SamplePlayableGraph(playableGraph, 0, 0);
            AnimationMode.EndSampling();
            
            // Render
            DrawPreview(rect);
        }
        
        private void UpdateFromSamplers(EcsPreviewWorldService.ExtractedSampler[] samplers)
        {
            // Reset all weights
            for (int i = 0; i < clipPlayables.Length; i++)
            {
                mixer.SetInputWeight(i, 0);
            }
            
            // Apply sampler data
            foreach (var sampler in samplers)
            {
                if (sampler.ClipIndex >= clipPlayables.Length || clips[sampler.ClipIndex] == null) continue;

                // Set weight
                mixer.SetInputWeight(sampler.ClipIndex, sampler.Weight);

                // Set time
                clipPlayables[sampler.ClipIndex].SetTime(sampler.Time);
            }
        }
        
        private void DrawPreview(Rect rect)
        {
            if (skinnedMeshRenderer == null || previewMesh == null || previewRenderUtility == null) return;
            
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
        
        #endregion
        
        #region Camera Control
        
        public void HandleCamera()
        {
            if (Event.current == null || previewRenderUtility == null) return;
            
            // Scroll wheel zoom
            if (Event.current.type == EventType.ScrollWheel)
            {
                camDistance = Mathf.Max(0.5f, camDistance + Event.current.delta.y * 0.5f);
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
                lastMousePosition = Event.current.mousePosition;
        }
        
        private void UpdateCameraPosition()
        {
            if (previewRenderUtility == null) return;
            
            previewRenderUtility.camera.transform.position =
                Quaternion.Euler(-camEuler.y, camEuler.x, 0) * (Vector3.up * camDistance - camPivot) + camPivot;
            previewRenderUtility.camera.transform.LookAt(camPivot + lookAtOffset);
            previewRenderUtility.lights[0].transform.rotation = previewRenderUtility.camera.transform.rotation;
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
            if (isInitialized)
            {
                AnimationMode.StopAnimationMode();
            }
            
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }
            
            previewRenderUtility?.Cleanup();
            previewRenderUtility = null;
            
            if (previewInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(previewInstance);
                previewInstance = null;
            }
            
            if (previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMesh);
                previewMesh = null;
            }
            
            clipPlayables = null;
            clips = null;
            animator = null;
            skinnedMeshRenderer = null;
            isInitialized = false;
        }
        
        #endregion
    }
}
