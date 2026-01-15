using System.Collections;
using System.IO;
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
    /// Editor tests for Unity Controller Bridge system.
    /// Tests bridge asset creation, registry, dirty tracking, and conversion queue.
    /// </summary>
    public class UnityControllerBridgeTests
    {
        private const string TestAssetsFolder = "Assets/DMotion.Tests/UnityControllerBridge";
        private AnimatorController _testController;
        private UnityControllerBridgeAsset _testBridge;

        [SetUp]
        public void Setup()
        {
            // Ensure test folder exists
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

            // Create test AnimatorController
            _testController = AnimatorController.CreateAnimatorControllerAtPath(
                Path.Combine(TestAssetsFolder, "TestController.controller"));

            // Add a simple parameter
            _testController.AddParameter("TestBool", AnimatorControllerParameterType.Bool);

            // Add a state
            var rootStateMachine = _testController.layers[0].stateMachine;
            var state = rootStateMachine.AddState("TestState");
            rootStateMachine.defaultState = state;

            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test assets
            if (AssetDatabase.IsValidFolder(TestAssetsFolder))
            {
                AssetDatabase.DeleteAsset(TestAssetsFolder);
            }

            _testController = null;
            _testBridge = null;
        }

        #region Bridge Asset Tests

        [Test]
        public void BridgeAsset_CanBeCreated()
        {
            var bridge = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();
            Assert.IsNotNull(bridge);
        }

        [Test]
        public void BridgeAsset_HasUniqueId()
        {
            var bridge1 = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();
            var bridge2 = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();

            string id1 = bridge1.BridgeId;
            string id2 = bridge2.BridgeId;

            Assert.IsFalse(string.IsNullOrEmpty(id1));
            Assert.IsFalse(string.IsNullOrEmpty(id2));
            Assert.AreNotEqual(id1, id2, "Bridge IDs should be unique");
        }

        [Test]
        public void BridgeAsset_StartsAsDirty()
        {
            var bridge = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();
            Assert.IsTrue(bridge.IsDirty, "Bridge should start dirty");
        }

        [Test]
        public void BridgeAsset_CanBeMarkedClean()
        {
            var bridge = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();
            Assert.IsTrue(bridge.IsDirty);

            bridge.MarkClean();
            Assert.IsFalse(bridge.IsDirty, "Bridge should be clean after MarkClean");
        }

        [Test]
        public void BridgeAsset_CanBeMarkedDirty()
        {
            var bridge = ScriptableObject.CreateInstance<UnityControllerBridgeAsset>();
            bridge.MarkClean();
            Assert.IsFalse(bridge.IsDirty);

            bridge.MarkDirty();
            Assert.IsTrue(bridge.IsDirty, "Bridge should be dirty after MarkDirty");
        }

        #endregion

        #region Registry Tests

        [Test]
        public void Registry_CanGetOrCreateBridge()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            Assert.IsNotNull(bridge, "Registry should create bridge for controller");
            Assert.AreEqual(_testController, bridge.SourceController, "Bridge should reference correct controller");
        }

        [Test]
        public void Registry_ReturnsSameBridgeForSameController()
        {
            var bridge1 = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            var bridge2 = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            Assert.AreSame(bridge1, bridge2, "Registry should return same bridge for same controller");
        }

        [Test]
        public void Registry_TracksRegisteredBridges()
        {
            var bridgesBefore = ControllerBridgeRegistry.GetAllBridges().Count;

            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            var bridgesAfter = ControllerBridgeRegistry.GetAllBridges().Count;

            Assert.AreEqual(bridgesBefore + 1, bridgesAfter, "Registry should track new bridge");
        }

        [Test]
        public void Registry_CanFindBridgeForController()
        {
            var createdBridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            var foundBridge = ControllerBridgeRegistry.GetBridge(_testController);

            Assert.AreSame(createdBridge, foundBridge, "Registry should find created bridge");
        }

        #endregion

        #region Dirty Tracker Tests

        [Test]
        public void DirtyTracker_DetectsDirtyBridges()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            bridge.MarkDirty();

            var dirtyBridges = ControllerBridgeDirtyTracker.GetDirtyBridges();

            Assert.IsTrue(dirtyBridges.Contains(bridge), "Dirty tracker should detect dirty bridge");
        }

        [Test]
        public void DirtyTracker_IgnoresCleanBridges()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            bridge.MarkClean();

            var dirtyBridges = ControllerBridgeDirtyTracker.GetDirtyBridges();

            Assert.IsFalse(dirtyBridges.Contains(bridge), "Dirty tracker should ignore clean bridge");
        }

        [UnityTest]
        public IEnumerator DirtyTracker_ClearsQueueAfterDebounce()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            bridge.MarkDirty();

            // Wait for debounce to complete (config default is 2 seconds, add buffer)
            var config = ControllerBridgeConfig.GetOrCreateDefault();
            float waitTime = config.DebounceDuration + 0.5f;

            yield return new WaitForSecondsRealtime(waitTime);

            // Pending queue should be clear after debounce
            Assert.AreEqual(0, ControllerBridgeDirtyTracker.PendingConversionQueue.Count,
                "Pending queue should be clear after debounce");
        }

        #endregion

        #region Conversion Queue Tests

        [Test]
        public void ConversionQueue_CanEnqueueBridge()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            int countBefore = ControllerConversionQueue.QueueCount;
            ControllerConversionQueue.Enqueue(bridge);
            int countAfter = ControllerConversionQueue.QueueCount;

            Assert.AreEqual(countBefore + 1, countAfter, "Queue should contain enqueued bridge");
        }

        [Test]
        public void ConversionQueue_PreventsDuplicates()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            ControllerConversionQueue.Enqueue(bridge);
            int countAfterFirst = ControllerConversionQueue.QueueCount;

            ControllerConversionQueue.Enqueue(bridge); // Duplicate
            int countAfterSecond = ControllerConversionQueue.QueueCount;

            Assert.AreEqual(countAfterFirst, countAfterSecond,
                "Queue should prevent duplicate entries for same bridge");
        }

        [Test]
        public void ConversionQueue_CanBeStopped()
        {
            ControllerConversionQueue.StartProcessing();
            Assert.IsTrue(ControllerConversionQueue.IsProcessing, "Queue should be processing");

            ControllerConversionQueue.StopProcessing();
            Assert.IsFalse(ControllerConversionQueue.IsProcessing, "Queue should stop processing");
        }

        [Test]
        public void ConversionQueue_CanBeCleared()
        {
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            ControllerConversionQueue.Enqueue(bridge);

            Assert.Greater(ControllerConversionQueue.QueueCount, 0, "Queue should have items");

            ControllerConversionQueue.Clear();

            Assert.AreEqual(0, ControllerConversionQueue.QueueCount, "Queue should be empty after clear");
        }

        #endregion

        #region Integration Tests

        [UnityTest]
        public IEnumerator Integration_BridgeLifecycle()
        {
            // Create bridge
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);
            Assert.IsNotNull(bridge);
            Assert.IsTrue(bridge.IsDirty);

            // Mark dirty (simulate controller change)
            bridge.MarkDirty();

            // Wait for debounce
            var config = ControllerBridgeConfig.GetOrCreateDefault();
            yield return new WaitForSecondsRealtime(config.DebounceDuration + 0.5f);

            // Queue should have processed (or tried to)
            // Note: Actual conversion won't work until Phase 7 is implemented
        }

        [Test]
        public void Integration_RegistryAndDirtyTrackerWork Together()
        {
            // Create bridge via registry
            var bridge = ControllerBridgeRegistry.GetOrCreateBridge(_testController);

            // Mark dirty
            bridge.MarkDirty();

            // Dirty tracker should detect it
            var dirtyBridges = ControllerBridgeDirtyTracker.GetDirtyBridges();
            Assert.Contains(bridge, dirtyBridges);

            // Mark clean
            bridge.MarkClean();

            // Dirty tracker should not detect it
            dirtyBridges = ControllerBridgeDirtyTracker.GetDirtyBridges();
            Assert.IsFalse(dirtyBridges.Contains(bridge));
        }

        #endregion
    }
}
