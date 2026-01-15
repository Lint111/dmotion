using System.Collections;
using System.IO;
using DMotion.Authoring;
using DMotion.Authoring.UnityControllerBridge;
using DMotion.Editor.UnityControllerBridge;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.Tests.Editor
{
    /// <summary>
    /// End-to-end integration tests for the complete Unity Controller Bridge workflow.
    /// Tests the full pipeline from AnimatorController to baked ECS entities.
    /// </summary>
    public class UnityControllerBridgeIntegrationTests
    {
        private const string TestAssetsFolder = "Assets/DMotion.Tests/UnityControllerBridge/Integration";
        private AnimatorController _testController;
        private UnityControllerBridgeAsset _testBridge;
        private GameObject _testGameObject;
        private AnimationStateMachineAuthoring _testAuthoring;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Ensure test folder exists
            EnsureTestFolder();
        }

        [SetUp]
        public void Setup()
        {
            // Create test AnimatorController with realistic content
            _testController = CreateRealisticTestController();

            // Create test GameObject with authoring component
            _testGameObject = new GameObject("TestEntity");
            _testAuthoring = _testGameObject.AddComponent<AnimationStateMachineAuthoring>();

            // Add Animator
            var animator = _testGameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = _testController;
            _testAuthoring.Animator = animator;
            _testAuthoring.Owner = _testGameObject;
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test objects
            if (_testGameObject != null)
            {
                Object.DestroyImmediate(_testGameObject);
            }

            _testController = null;
            _testBridge = null;
            _testAuthoring = null;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Clean up test assets
            if (AssetDatabase.IsValidFolder(TestAssetsFolder))
            {
                AssetDatabase.DeleteAsset(TestAssetsFolder);
            }
        }

        #region End-to-End Tests

        [UnityTest]
        public IEnumerator E2E_CreateBridge_Convert_UseInAuthoring()
        {
            // Step 1: Create bridge from controller
            _testBridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            Assert.IsNotNull(_testBridge, "Bridge should be created");
            Assert.IsTrue(_testBridge.IsDirty, "New bridge should be dirty");

            // Step 2: Convert bridge
            bool conversionComplete = false;
            bool conversionSuccess = false;

            ControllerConversionQueue.OnConversionFinished += (bridgeId, success) =>
            {
                if (bridgeId == _testBridge.BridgeId)
                {
                    conversionComplete = true;
                    conversionSuccess = success;
                }
            };

            ControllerConversionQueue.Enqueue(_testBridge);
            ControllerConversionQueue.StartProcessing();

            // Wait for conversion
            float timeout = 10f;
            float elapsed = 0f;
            while (!conversionComplete && elapsed < timeout)
            {
                yield return new WaitForSecondsRealtime(0.1f);
                elapsed += 0.1f;
            }

            Assert.IsTrue(conversionComplete, "Conversion should complete within timeout");
            Assert.IsTrue(conversionSuccess, "Conversion should succeed");
            Assert.IsFalse(_testBridge.IsDirty, "Bridge should be clean after conversion");
            Assert.IsNotNull(_testBridge.GeneratedStateMachine, "Bridge should have generated StateMachine");

            // Step 3: Configure authoring to use bridge
            _testAuthoring.SourceMode = StateMachineSourceMode.UnityControllerBridge;
            _testAuthoring.ControllerBridge = _testBridge;

            // Step 4: Verify authoring can resolve StateMachine
            var resolvedStateMachine = _testAuthoring.GetStateMachine();
            Assert.IsNotNull(resolvedStateMachine, "Authoring should resolve StateMachine from bridge");
            Assert.AreSame(_testBridge.GeneratedStateMachine, resolvedStateMachine, "Should resolve to bridge's StateMachine");

            // Step 5: Verify StateMachine content
            Assert.Greater(resolvedStateMachine.Parameters.Count, 0, "StateMachine should have parameters");
            Assert.Greater(resolvedStateMachine.States.Count, 0, "StateMachine should have states");
            Assert.IsNotNull(resolvedStateMachine.DefaultState, "StateMachine should have default state");

            Debug.Log($"[E2E Test] Complete workflow successful:");
            Debug.Log($"  - Bridge created: {_testBridge.BridgeId}");
            Debug.Log($"  - Conversion succeeded: {conversionSuccess}");
            Debug.Log($"  - Generated asset: {AssetDatabase.GetAssetPath(resolvedStateMachine)}");
            Debug.Log($"  - Parameters: {resolvedStateMachine.Parameters.Count}");
            Debug.Log($"  - States: {resolvedStateMachine.States.Count}");
        }

        [Test]
        public void E2E_BridgeInDirectMode_UsesDirectAsset()
        {
            // Create a direct StateMachineAsset (mock)
            var directAsset = ScriptableObject.CreateInstance<StateMachineAsset>();
            directAsset.name = "DirectAsset";

            // Configure authoring in Direct mode
            _testAuthoring.SourceMode = StateMachineSourceMode.Direct;
            _testAuthoring.StateMachineAsset = directAsset;

            // Verify resolution
            var resolved = _testAuthoring.GetStateMachine();
            Assert.AreSame(directAsset, resolved, "Direct mode should resolve to direct asset");

            Object.DestroyImmediate(directAsset);
        }

        [UnityTest]
        public IEnumerator E2E_DirtyBridge_AutoConvertsBeforePlayMode()
        {
            // Create and convert bridge initially
            _testBridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            bool conversionComplete = false;
            ControllerConversionQueue.OnConversionFinished += (bridgeId, success) =>
            {
                if (bridgeId == _testBridge.BridgeId) conversionComplete = true;
            };

            ControllerConversionQueue.Enqueue(_testBridge);
            ControllerConversionQueue.StartProcessing();

            yield return new WaitUntil(() => conversionComplete);

            // Mark bridge dirty (simulate controller change)
            _testBridge.MarkDirty();
            Assert.IsTrue(_testBridge.IsDirty);

            // Force check should trigger reconversion
            ControllerBridgeDirtyTracker.ForceCheckAllBridges();

            // Wait for debounce and conversion
            var config = ControllerBridgeConfig.GetOrCreateDefault();
            yield return new WaitForSecondsRealtime(config.DebounceDuration + 1f);

            // Bridge should be clean after auto-conversion
            Assert.IsFalse(_testBridge.IsDirty, "Bridge should auto-convert when dirty");
        }

        [UnityTest]
        public IEnumerator E2E_MultipleEntities_ShareSameBridge()
        {
            // Create bridge and convert
            _testBridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            bool conversionComplete = false;
            ControllerConversionQueue.OnConversionFinished += (bridgeId, success) =>
            {
                if (bridgeId == _testBridge.BridgeId) conversionComplete = true;
            };

            ControllerConversionQueue.Enqueue(_testBridge);
            ControllerConversionQueue.StartProcessing();

            yield return new WaitUntil(() => conversionComplete);

            // Configure first authoring
            _testAuthoring.SourceMode = StateMachineSourceMode.UnityControllerBridge;
            _testAuthoring.ControllerBridge = _testBridge;

            // Create second entity
            var entity2 = new GameObject("TestEntity2");
            var authoring2 = entity2.AddComponent<AnimationStateMachineAuthoring>();
            var animator2 = entity2.AddComponent<Animator>();
            animator2.runtimeAnimatorController = _testController;
            authoring2.Animator = animator2;
            authoring2.Owner = entity2;
            authoring2.SourceMode = StateMachineSourceMode.UnityControllerBridge;
            authoring2.ControllerBridge = _testBridge;

            // Verify both resolve to same StateMachine
            var resolved1 = _testAuthoring.GetStateMachine();
            var resolved2 = authoring2.GetStateMachine();

            Assert.AreSame(resolved1, resolved2, "Multiple entities should share same StateMachine");
            Assert.AreSame(_testBridge.GeneratedStateMachine, resolved1, "Should resolve to bridge's StateMachine");

            // Verify reference count
            int refCount = ControllerBridgeRegistry.GetReferenceCount(_testBridge);
            Assert.AreEqual(2, refCount, "Bridge should track 2 entities using it");

            Object.DestroyImmediate(entity2);
        }

        [Test]
        public void E2E_RegistryPrevents_DuplicateBridges()
        {
            // Create first bridge
            var bridge1 = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            // Try to create second bridge for same controller
            var bridge2 = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            // Should return same bridge
            Assert.AreSame(bridge1, bridge2, "Registry should prevent duplicate bridges");
        }

        [UnityTest]
        public IEnumerator E2E_RealController_StarterAssets()
        {
            // Load real StarterAssetsThirdPerson controller
            string controllerPath = "Assets/DMotion.Tests/Data/Animations/StarterAssetsThirdPerson.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
            {
                Assert.Inconclusive($"StarterAssetsThirdPerson controller not found at {controllerPath}");
                yield break;
            }

            // Create bridge
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(controller);
            Assert.IsNotNull(bridge);

            // Convert
            bool complete = false;
            bool success = false;
            ControllerConversionQueue.OnConversionFinished += (id, s) =>
            {
                if (id == bridge.BridgeId) { complete = true; success = s; }
            };

            ControllerConversionQueue.Enqueue(bridge);
            ControllerConversionQueue.StartProcessing();

            yield return new WaitUntil(() => complete);

            Assert.IsTrue(success, "StarterAssets conversion should succeed");
            Assert.IsNotNull(bridge.GeneratedStateMachine);

            // Verify content
            var stateMachine = bridge.GeneratedStateMachine;
            Assert.Greater(stateMachine.Parameters.Count, 0, "Should have parameters");
            Assert.Greater(stateMachine.States.Count, 0, "Should have states");

            Debug.Log($"[E2E Test] StarterAssets conversion successful:");
            Debug.Log($"  - Parameters: {stateMachine.Parameters.Count}");
            Debug.Log($"  - States: {stateMachine.States.Count}");
            Debug.Log($"  - Default State: {stateMachine.DefaultState?.name ?? "none"}");
        }

        #endregion

        #region Helper Methods

        private void EnsureTestFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetsFolder))
            {
                var parts = TestAssetsFolder.Split('/');
                var currentPath = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var nextPath = currentPath + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(nextPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    }
                    currentPath = nextPath;
                }
            }
        }

        private AnimatorController CreateRealisticTestController()
        {
            string path = Path.Combine(TestAssetsFolder, "RealisticTestController.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Add parameters
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            // Get base layer
            var rootStateMachine = controller.layers[0].stateMachine;

            // Create states
            var idleState = rootStateMachine.AddState("Idle");
            var walkState = rootStateMachine.AddState("Walk");
            var jumpState = rootStateMachine.AddState("Jump");

            // Set default state
            rootStateMachine.defaultState = idleState;

            // Add transitions
            var idleToWalk = idleState.AddTransition(walkState);
            idleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            idleToWalk.duration = 0.25f;

            var walkToIdle = walkState.AddTransition(idleState);
            walkToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            walkToIdle.duration = 0.25f;

            var idleToJump = idleState.AddTransition(jumpState);
            idleToJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            idleToJump.duration = 0.1f;

            var jumpToIdle = jumpState.AddTransition(idleState);
            jumpToIdle.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            jumpToIdle.duration = 0.2f;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return controller;
        }

        #endregion
    }
}
