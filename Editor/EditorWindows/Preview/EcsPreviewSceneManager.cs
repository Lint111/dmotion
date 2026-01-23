using System;
using System.IO;
using DMotion.Authoring;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMotion.Editor
{
    /// <summary>
    /// Manages automatic creation and setup of preview scenes for ECS animation preview.
    /// Creates a temporary scene with SubScene containing the preview entity.
    /// Singleton to ensure all preview backends share the same scene.
    /// </summary>
    internal class EcsPreviewSceneManager : IDisposable
    {
        #region Singleton
        
        private static EcsPreviewSceneManager instance;
        private static readonly object lockObj = new object();
        private int referenceCount;
        
        /// <summary>
        /// Gets the singleton instance, creating it if necessary.
        /// Call Release() when done to allow cleanup.
        /// </summary>
        public static EcsPreviewSceneManager Instance
        {
            get
            {
                lock (lockObj)
                {
                    if (instance == null)
                    {
                        instance = new EcsPreviewSceneManager();
                    }
                    instance.referenceCount++;
                    return instance;
                }
            }
        }
        
        /// <summary>
        /// Releases a reference to the singleton.
        /// When all references are released, the instance can be cleaned up.
        /// </summary>
        public void Release()
        {
            lock (lockObj)
            {
                referenceCount--;
                if (referenceCount <= 0 && instance != null)
                {
                    instance.DisposeInternal();
                    instance = null;
                }
            }
        }
        
        #endregion
        
        #region Constants
        
        private const string PreviewFolderPath = "Assets/DMotion_PreviewTemp";
        private const string MainSceneName = "DMotion_Preview.unity";
        private const string ContentSceneName = "DMotion_Preview_Content.unity";
        
        #endregion
        
        #region State
        
        private string mainScenePath;
        private string contentScenePath;
        private Scene? previousScene;
        private bool isSetup;
        private GameObject previewInstance;
        private StateMachineAsset currentStateMachine;
        private GameObject currentModel;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether the preview scene is set up and ready.
        /// </summary>
        public bool IsSetup => isSetup;
        
        /// <summary>
        /// Path to the main preview scene.
        /// </summary>
        public string MainScenePath => mainScenePath;
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Sets up the preview scene with the given state machine and model.
        /// Creates scenes if needed, instantiates the model with authoring components.
        /// </summary>
        /// <param name="stateMachine">The state machine to preview.</param>
        /// <param name="model">The model prefab (must have Animator).</param>
        /// <returns>True if setup succeeded.</returns>
        public bool Setup(StateMachineAsset stateMachine, GameObject model)
        {
            if (stateMachine == null)
            {
                Debug.LogError("[EcsPreviewSceneManager] StateMachineAsset is required");
                return false;
            }
            
            if (model == null)
            {
                // Try to get model from state machine's bound armature
                model = TryGetModelFromStateMachine(stateMachine);
                if (model == null)
                {
                    Debug.LogError("[EcsPreviewSceneManager] No model provided and couldn't find one from StateMachineAsset");
                    return false;
                }
            }
            
            // Check if we need to recreate (different state machine or model)
            if (isSetup && currentStateMachine == stateMachine && currentModel == model)
            {
                return true; // Already set up with same assets
            }
            
            try
            {
                // Save current scene reference
                if (!isSetup)
                {
                    var activeScene = SceneManager.GetActiveScene();
                    if (activeScene.IsValid() && !activeScene.path.Contains("DMotion_Preview"))
                    {
                        previousScene = activeScene;
                    }
                }
                
                // Ensure folder exists
                EnsurePreviewFolder();
                
                // Create or update content scene
                CreateContentScene(stateMachine, model);
                
                // Create or update main scene with SubScene
                CreateMainScene();
                
                // Open the main scene
                EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);
                
                currentStateMachine = stateMachine;
                currentModel = model;
                isSetup = true;
                
                Debug.Log("[EcsPreviewSceneManager] Preview scene setup complete. SubScene will bake automatically.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EcsPreviewSceneManager] Setup failed: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Updates the preview entity with new state machine or model.
        /// </summary>
        public bool UpdatePreview(StateMachineAsset stateMachine, GameObject model)
        {
            return Setup(stateMachine, model);
        }
        
        /// <summary>
        /// Closes the preview scene and returns to the previous scene.
        /// </summary>
        public void Close()
        {
            if (!isSetup) return;
            
            try
            {
                // Return to previous scene if we have one
                if (previousScene.HasValue && previousScene.Value.IsValid())
                {
                    var path = previousScene.Value.path;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    }
                }
                
                isSetup = false;
                currentStateMachine = null;
                currentModel = null;
                
                Debug.Log("[EcsPreviewSceneManager] Preview scene closed");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EcsPreviewSceneManager] Error closing preview: {e.Message}");
            }
        }
        
        /// <summary>
        /// Forces reimport of the content scene to trigger rebaking.
        /// </summary>
        public void ForceBake()
        {
            if (!isSetup || string.IsNullOrEmpty(contentScenePath)) return;
            
            AssetDatabase.ImportAsset(contentScenePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[EcsPreviewSceneManager] Forced rebake of preview SubScene");
        }
        
        /// <summary>
        /// Cleans up all preview scene files.
        /// </summary>
        public void Cleanup()
        {
            Close();
            
            try
            {
                if (AssetDatabase.IsValidFolder(PreviewFolderPath))
                {
                    AssetDatabase.DeleteAsset(PreviewFolderPath);
                    AssetDatabase.Refresh();
                    Debug.Log("[EcsPreviewSceneManager] Cleaned up preview folder");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EcsPreviewSceneManager] Cleanup error: {e.Message}");
            }
        }
        
        #endregion
        
        #region Private - Scene Creation
        
        private void EnsurePreviewFolder()
        {
            if (!AssetDatabase.IsValidFolder(PreviewFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", "DMotion_PreviewTemp");
            }
            
            mainScenePath = Path.Combine(PreviewFolderPath, MainSceneName);
            contentScenePath = Path.Combine(PreviewFolderPath, ContentSceneName);
        }
        
        private void CreateContentScene(StateMachineAsset stateMachine, GameObject model)
        {
            // Create or open content scene
            Scene contentScene;
            if (File.Exists(contentScenePath))
            {
                contentScene = EditorSceneManager.OpenScene(contentScenePath, OpenSceneMode.Single);
                
                // Clear existing objects
                foreach (var go in contentScene.GetRootGameObjects())
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            else
            {
                contentScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            
            // Instantiate the model
            previewInstance = (GameObject)PrefabUtility.InstantiatePrefab(model, contentScene);
            if (previewInstance == null)
            {
                // Fallback to regular instantiate
                previewInstance = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(previewInstance, contentScene);
            }
            
            previewInstance.name = "PreviewEntity";
            previewInstance.transform.position = Vector3.zero;
            previewInstance.transform.rotation = Quaternion.identity;
            
            // Ensure it has an Animator
            var animator = previewInstance.GetComponent<Animator>();
            animator ??= previewInstance.GetComponentInChildren<Animator>();
            
            if (animator == null)
            {
                Debug.LogWarning("[EcsPreviewSceneManager] Model has no Animator component");
            }
            
            // Add or update AnimationStateMachineAuthoring
            var authoring = previewInstance.GetComponent<AnimationStateMachineAuthoring>();
            
            authoring ??= previewInstance.AddComponent<AnimationStateMachineAuthoring>();
            
            authoring.StateMachineAsset = stateMachine;
            
            // Add ground plane for reference
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2, 1, 2);
            var groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                // Create a simple dark material
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat != null)
                {
                    mat.color = new Color(0.2f, 0.2f, 0.2f);
                    groundRenderer.sharedMaterial = mat;
                }
            }
            SceneManager.MoveGameObjectToScene(ground, contentScene);
            
            // Add directional light
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            SceneManager.MoveGameObjectToScene(lightGO, contentScene);
            
            // Save the content scene
            EditorSceneManager.SaveScene(contentScene, contentScenePath);
            Debug.Log($"[EcsPreviewSceneManager] Created content scene: {contentScenePath}");
        }
        
        private void CreateMainScene()
        {
            // Create or open main scene
            Scene mainScene;
            if (File.Exists(mainScenePath))
            {
                mainScene = EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);
            }
            else
            {
                mainScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            
            // Find or create SubScene
            var existingSubScene = UnityEngine.Object.FindFirstObjectByType<SubScene>();
            if (existingSubScene != null)
            {
                // Update reference
                var contentSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(contentScenePath);
                if (contentSceneAsset != null)
                {
                    existingSubScene.SceneAsset = contentSceneAsset;
                }
            }
            else
            {
                // Create SubScene GameObject
                var subSceneGO = new GameObject("PreviewSubScene");
                var subScene = subSceneGO.AddComponent<SubScene>();
                
                var contentSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(contentScenePath);
                if (contentSceneAsset != null)
                {
                    subScene.SceneAsset = contentSceneAsset;
                }
            }
            
            // Add a camera if not present
            if (Camera.main == null)
            {
                var camGO = new GameObject("Main Camera")
                {
                    tag = "MainCamera"
                };
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                camGO.transform.position = new Vector3(0, 1.5f, -3f);
                camGO.transform.LookAt(Vector3.up * 0.8f);
            }
            
            // Save main scene
            EditorSceneManager.SaveScene(mainScene, mainScenePath);
            Debug.Log($"[EcsPreviewSceneManager] Created main scene: {mainScenePath}");
        }
        
        private GameObject TryGetModelFromStateMachine(StateMachineAsset stateMachine)
        {
            // Try to get from bound armature data
            if (stateMachine.BoundArmatureData != null)
            {
                // If it's an Avatar, try to find the source model
                if (stateMachine.BoundArmatureData is Avatar avatar)
                {
                    var avatarPath = AssetDatabase.GetAssetPath(avatar);
                    if (!string.IsNullOrEmpty(avatarPath))
                    {
                        // Try to load the FBX as a GameObject
                        var model = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
                        if (model != null && model.GetComponentInChildren<Animator>() != null)
                        {
                            return model;
                        }
                    }
                }
                
                // If it's already a GameObject
                if (stateMachine.BoundArmatureData is GameObject go)
                {
                    return go;
                }
            }
            
            // Try to find a model from the clips
            foreach (var clip in stateMachine.Clips)
            {
                if (clip?.Clip == null) continue;
                
                var clipPath = AssetDatabase.GetAssetPath(clip.Clip);
                if (string.IsNullOrEmpty(clipPath)) continue;
                
                // Try to load model from same FBX
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(clipPath);
                if (model != null && model.GetComponentInChildren<Animator>() != null)
                {
                    return model;
                }
            }
            
            return null;
        }
        
        #endregion
        
        #region IDisposable
        
        /// <summary>
        /// Releases a reference to the singleton. Use this instead of Dispose().
        /// </summary>
        public void Dispose()
        {
            Release();
        }
        
        /// <summary>
        /// Internal disposal - only called when all references are released.
        /// </summary>
        private void DisposeInternal()
        {
            Close();
        }
        
        #endregion
    }
}
