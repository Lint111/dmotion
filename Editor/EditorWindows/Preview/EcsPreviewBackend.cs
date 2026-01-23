using System;
using System.Linq;
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
        private float transitionProgress;
        private string errorMessage;
        private bool isInitialized;
        private bool entityCreated;
        
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
            
            // Create world if not already created
            if (!worldService.IsInitialized)
            {
                worldService.CreateWorld();
            }
            
            // Rebuild blobs if state machine changed
            if (stateMachineAsset != newStateMachineAsset)
            {
                DisposeBlobs();
                stateMachineAsset = newStateMachineAsset;
                
                if (!TryCreateBlobs())
                {
                    return;
                }
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
            
            // Create world if not already created
            if (!worldService.IsInitialized)
            {
                worldService.CreateWorld();
            }
            
            // Rebuild blobs if state machine changed
            if (stateMachineAsset != newStateMachineAsset)
            {
                DisposeBlobs();
                stateMachineAsset = newStateMachineAsset;
                
                if (!TryCreateBlobs())
                {
                    return;
                }
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
            // Phase 6B: Use model to set up skeleton components for rendering
        }
        
        public void Clear()
        {
            currentState = null;
            transitionFromState = null;
            transitionToState = null;
            errorMessage = null;
            isInitialized = false;
            entityCreated = false;
            
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
                return true;
            }
            catch (Exception e)
            {
                errorMessage = $"Entity creation failed:\n{e.Message}";
                Debug.LogError($"[EcsPreviewBackend] {errorMessage}\n{e.StackTrace}");
                return false;
            }
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
            else if (isInitialized && entityCreated)
            {
                // Phase 6A: Show state machine info (no 3D rendering yet)
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
            
            // Note about Phase 6B - positioned at ~70% down
            var noteHeight = 36f;
            var noteY = rect.y + rect.height * 0.7f;
            var noteRect = new Rect(rect.x, noteY, rect.width, noteHeight);
            GUI.Label(noteRect, "State machine logic active.\n3D rendering coming in Phase 6B.", noteStyle);
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
            
            DisposeBlobs();
        }
        
        #endregion
    }
}
