using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DMotion.Tests
{
    public abstract class ECSTestBase : ECSTestsFixture
    {
        protected const float defaultDeltaTime = 1.0f / 60.0f;
        private float elapsedTime;
        private NativeArray<SystemHandle> allSystems;
        private BlobAssetStore blobAssetStore;

        public override void Setup()
        {
            base.Setup();
            elapsedTime = Time.time;
            //Create required systems
            {
                var requiredSystemsAttr = GetType().GetCustomAttribute<CreateSystemsForTest>();
                if (requiredSystemsAttr != null)
                {
                    var baseTypeManaged = typeof(SystemBase);
                    var baseType = typeof(ISystem);
                    foreach (var t in requiredSystemsAttr.SystemTypes)
                    {
                        var isValid = baseType.IsAssignableFrom(t) || baseTypeManaged.IsAssignableFrom(t);
                        Assert.IsTrue(isValid,
                            $"Expected {t.Name} to be a subclass of {baseType.Name} or {baseTypeManaged.Name}");
                        world.CreateSystem(t);
                    }

                    allSystems = world.Unmanaged.GetAllSystems(Allocator.Persistent);
                }
            }

            // Convert entity prefabs
            {
                var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var convertPrefabFields = GetType()
                    .GetFields(bindingFlags)
                    .Where(f => f.GetCustomAttribute<ConvertGameObjectPrefab>() != null).ToArray();

                if (convertPrefabFields.Length == 0)
                    return;

                var createdEntities = new NativeArray<Entity>(convertPrefabFields.Length, Allocator.Temp);

                blobAssetStore = new BlobAssetStore(128);
                var conversionWorld = new World("Test Conversion World");

                // Load and convert all prefabs
                for (var i = 0; i < convertPrefabFields.Length; i++)
                {
                    var f = convertPrefabFields[i];
                    var attr = f.GetCustomAttribute<ConvertGameObjectPrefab>();

                    GameObject go = null;

#if UNITY_EDITOR
                    // If asset path is specified, load via AssetDatabase
                    if (!string.IsNullOrEmpty(attr.AssetPath))
                    {
                        var loadedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(attr.AssetPath);
                        if (loadedAsset == null)
                        {
                            Debug.LogError($"[ECSTestBase] Failed to load prefab from path: {attr.AssetPath}");
                            Assert.Fail($"Failed to load prefab from path: {attr.AssetPath}");
                            return;
                        }
                        go = loadedAsset;
                    }
                    else
#endif
                    {
                        // Legacy behavior: use pre-assigned field value
                        var value = f.GetValue(this);
                        if (value == null)
                        {
                            Debug.LogError($"[ECSTestBase] Field {f.Name} has no AssetPath and no pre-assigned value");
                            Assert.Fail($"Field {f.Name} has no AssetPath and no pre-assigned value");
                            return;
                        }

                        if (value is GameObject g)
                            go = g;
                        else if (value is MonoBehaviour mono)
                            go = mono.gameObject;
                    }

                    if (go == null)
                    {
                        Debug.LogError($"[ECSTestBase] Could not get GameObject for field {f.Name}");
                        Assert.Fail($"Could not get GameObject for field {f.Name}");
                        return;
                    }

                    Debug.Log($"[ECSTestBase] Baking prefab: {go.name}");

                    Entity entity;
                    try
                    {
                        entity = BakingTestUtils.ConvertGameObject(conversionWorld, go, blobAssetStore);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[ECSTestBase] Exception during baking {go.name}: {ex}");
                        throw;
                    }

                    if (entity == Entity.Null)
                    {
                        Debug.LogError($"[ECSTestBase] Failed to convert prefab {go.name} to Entity");
                        Assert.Fail($"Failed to convert prefab {go.name} to Entity");
                        return;
                    }
                    createdEntities[i] = entity;
                }

                // Copy entities from conversionWorld to testWorld
                var outputEntities = new NativeArray<Entity>(createdEntities.Length, Allocator.Temp);
                manager.CopyEntitiesFrom(conversionWorld.EntityManager, createdEntities, outputEntities);

                // Assign converted entities to target fields
                for (var i = 0; i < convertPrefabFields.Length; i++)
                {
                    var f = convertPrefabFields[i];
                    var entity = outputEntities[i];
                    var attr = f.GetCustomAttribute<ConvertGameObjectPrefab>();
                    var receiveField = GetType().GetField(attr.ToFieldName, bindingFlags);
                    Assert.IsNotNull(receiveField,
                        $"Couldn't find field to receive entity prefab ({f.Name}, {attr.ToFieldName})");
                    receiveField.SetValue(this, entity);
                }

                conversionWorld.Dispose();
            }
        }

        public override void TearDown()
        {
            base.TearDown();
            if (allSystems.IsCreated)
            {
                allSystems.Dispose();
            }

            if (blobAssetStore.IsCreated)
            {
                blobAssetStore.Dispose();
            }
        }

        protected void UpdateWorld(float deltaTime = defaultDeltaTime)
        {
            if (world != null && world.IsCreated)
            {
                elapsedTime += deltaTime;
                world.SetTime(new TimeData(elapsedTime, deltaTime));
                foreach (var s in allSystems)
                {
                    s.Update(world.Unmanaged);
                }

                //We always want to complete all jobs after update world. Otherwise transformations that test expect to run may not have been run during Assert
                //This is also necessary for performance tests accuracy.
                manager.CompleteAllTrackedJobs();
            }
        }
    }
}