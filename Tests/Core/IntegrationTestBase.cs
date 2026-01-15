using System.Collections;
using Latios.Kinemation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace DMotion.Tests
{
    /// <summary>
    /// Base class for integration tests that need real SmartBlobber-baked ACL clip data.
    ///
    /// Prerequisites:
    /// 1. Run "DMotion/Tests/Setup Test Scene" in editor
    /// 2. Open TestAnimationScene.unity to trigger baking
    /// 3. Build Settings must include the test scene for PlayMode tests
    ///
    /// Unlike ECSTestBase which creates an isolated world, these tests use
    /// DefaultGameObjectInjectionWorld with real baked entities.
    /// </summary>
    public abstract class IntegrationTestBase
    {
        protected World world;
        protected EntityManager manager;
        protected Entity bakedEntity;
        protected BlobAssetReference<SkeletonClipSetBlob> clipsBlob;

        private const string TestSceneName = "TestAnimationScene";
        private float elapsedTime;
        private NativeArray<SystemHandle> allSystems;
        private readonly BlobAndEntityTracker tracker = new BlobAndEntityTracker("IntegrationTestBase");

        /// <summary>
        /// Override to specify which systems to create for testing.
        /// </summary>
        protected virtual System.Type[] SystemTypes => System.Array.Empty<System.Type>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Check if test scene exists
            if (!PrebakedTestHelper.IsTestSceneSetup())
            {
                Assert.Ignore("Test scene not set up. Run 'DMotion/Tests/Setup Test Scene' first.");
                yield break;
            }

            // Load the pre-baked test scene
            yield return PrebakedTestHelper.LoadTestScene();

            // Get the default world with baked entities
            world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Assert.Fail("DefaultGameObjectInjectionWorld is null after loading test scene");
                yield break;
            }

            manager = world.EntityManager;

            // Get baked animation entities
            var entities = PrebakedTestHelper.GetAnimationEntities();
            if (entities.Length == 0)
            {
                Assert.Fail("No animation entities found in test scene. " +
                           "Ensure prefabs with AnimationStateMachineAuthoring are in the scene.");
                yield break;
            }

            bakedEntity = entities[0];
            Debug.Log($"[IntegrationTestBase] Found {entities.Length} baked entities, using: {bakedEntity}");

            // Extract the real ACL-baked clips blob
            if (manager.HasComponent<AnimationStateMachine>(bakedEntity))
            {
                var stateMachine = manager.GetComponentData<AnimationStateMachine>(bakedEntity);
                clipsBlob = stateMachine.ClipsBlob;
                Debug.Log($"[IntegrationTestBase] Got clips blob with {clipsBlob.Value.clips.Length} clips");
            }
            else
            {
                Assert.Fail("Baked entity missing AnimationStateMachine component");
            }

            // Create and track only the requested systems (not all systems in the world)
            if (SystemTypes.Length > 0)
            {
                allSystems = new NativeArray<SystemHandle>(SystemTypes.Length, Allocator.Persistent);
                for (int i = 0; i < SystemTypes.Length; i++)
                {
                    allSystems[i] = world.CreateSystem(SystemTypes[i]);
                }
            }

            elapsedTime = Time.time;

            // Allow subclass setup
            yield return OnSetUp();
        }

        /// <summary>
        /// Override for additional setup after scene load.
        /// </summary>
        protected virtual IEnumerator OnSetUp()
        {
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Allow subclass teardown
            yield return OnTearDown();

            // Dispose systems array first
            if (allSystems.IsCreated)
            {
                allSystems.Dispose();
            }

            // Clear references to baked data
            clipsBlob = default;
            bakedEntity = Entity.Null;

            if (world != null && world.IsCreated)
            {
                // Complete all tracked jobs before modifying buffers
                manager.CompleteAllTrackedJobs();

                // Clear animation buffers to remove blob refs that would become invalid during unload.
                // This prevents animation systems from crashing when they run during unload yields.
                // We do NOT destroy entities manually - the subscene unload handles entity destruction
                // in a coordinated way with blob data, avoiding Kinemation job race conditions.
                ClearAnimationBuffers();

                // Clean up tracked test resources
                tracker.Cleanup(manager);
            }

            // Unload scene - this destroys subscene entities along with their blob data
            yield return PrebakedTestHelper.UnloadTestScene();
        }

        /// <summary>
        /// Clears animation buffers to remove blob references.
        /// This prevents animation systems from accessing invalid blobs during teardown.
        /// </summary>
        private void ClearAnimationBuffers()
        {
            if (world == null || !world.IsCreated)
                return;

            var query = manager.CreateEntityQuery(typeof(ClipSampler));
            var entities = query.ToEntityArray(Allocator.TempJob);

            foreach (var entity in entities)
            {
                if (manager.Exists(entity))
                {
                    if (manager.HasBuffer<ClipSampler>(entity))
                        manager.GetBuffer<ClipSampler>(entity).Clear();
                    if (manager.HasBuffer<AnimationState>(entity))
                        manager.GetBuffer<AnimationState>(entity).Clear();
                    if (manager.HasBuffer<LinearBlendStateMachineState>(entity))
                        manager.GetBuffer<LinearBlendStateMachineState>(entity).Clear();
                    if (manager.HasBuffer<SingleClipState>(entity))
                        manager.GetBuffer<SingleClipState>(entity).Clear();
                }
            }

            entities.Dispose();
            manager.CompleteAllTrackedJobs();
        }

        /// <summary>
        /// Override for additional teardown before scene unload.
        /// </summary>
        protected virtual IEnumerator OnTearDown()
        {
            yield break;
        }

        /// <summary>
        /// Creates a new entity and tracks it for cleanup.
        /// </summary>
        protected Entity CreateEntity()
        {
            var entity = manager.CreateEntity();
            tracker.TrackEntity(entity);
            return entity;
        }

        /// <summary>
        /// Tracks an existing entity for cleanup during teardown.
        /// </summary>
        protected void TrackEntity(Entity entity)
        {
            tracker.TrackEntity(entity);
        }

        /// <summary>
        /// Tracks a BlobAssetReference for disposal during teardown.
        /// Use this for any blobs created with Allocator.Persistent in tests.
        /// </summary>
        protected void TrackBlob<T>(BlobAssetReference<T> blob) where T : unmanaged
        {
            tracker.TrackBlob(blob);
        }

        /// <summary>
        /// Updates the world by the specified delta time.
        /// </summary>
        protected void UpdateWorld(float deltaTime = 1.0f / 60.0f)
        {
            if (world != null && world.IsCreated)
            {
                elapsedTime += deltaTime;
                world.SetTime(new TimeData(elapsedTime, deltaTime));

                if (allSystems.IsCreated)
                {
                    foreach (var s in allSystems)
                    {
                        s.Update(world.Unmanaged);
                    }
                }

                manager.CompleteAllTrackedJobs();
            }
        }

        /// <summary>
        /// Gets the clip duration from the real baked clips.
        /// </summary>
        protected float GetClipDuration(int clipIndex = 0)
        {
            if (!clipsBlob.IsCreated || clipIndex >= clipsBlob.Value.clips.Length)
                return 1.0f;

            return clipsBlob.Value.clips[clipIndex].duration;
        }
    }
}
