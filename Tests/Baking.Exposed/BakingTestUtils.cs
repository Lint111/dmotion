using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace DMotion.Tests
{
    public static class BakingTestUtils
    {
        public static void BakeGameObjects(World conversionWorld, GameObject[] rootGameObjects,
            BlobAssetStore blobAssetStore)
        {
            Debug.Log($"[BakingTestUtils] Starting bake for {rootGameObjects.Length} GameObjects");
            var settings = new BakingSettings(BakingUtility.BakingFlags.AssignName, blobAssetStore);
            BakingUtility.BakeGameObjects(conversionWorld, rootGameObjects, settings);
            Debug.Log($"[BakingTestUtils] Bake completed");
        }

        public static Entity ConvertGameObject(World world, GameObject go, BlobAssetStore store)
        {
            Debug.Log($"[BakingTestUtils] ConvertGameObject: {go.name}");
            BakeGameObjects(world, new[] { go }, store);
            var entity = GetEntityForGameObject(world, go);
            Debug.Log($"[BakingTestUtils] ConvertGameObject result: {entity}");
            return entity;
        }

        public static Entity GetEntityForGameObject(World world, GameObject go)
        {
            var bakingSystem = world.GetOrCreateSystemManaged<BakingSystem>();
            var bakedEntityData = bakingSystem.GetBakeEntityData();
            return bakedEntityData.GetEntity(go);
        }

        internal static BakedEntityData GetBakeEntityData(this BakingSystem bakingSystem)
        {
            // Try multiple possible field names - Unity versions may differ
            var fieldNames = new[] { "_BakedEntities", "m_BakedEntities", "_bakedEntities" };

            foreach (var fieldName in fieldNames)
            {
                var reflectedBakingEntitiesField =
                    typeof(BakingSystem).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (reflectedBakingEntitiesField != null)
                {
                    Debug.Log($"[BakingTestUtils] Found baked entities field: {fieldName}");
                    return (BakedEntityData)reflectedBakingEntitiesField.GetValue(bakingSystem);
                }
            }

            Debug.LogError("[BakingTestUtils] Couldn't find BakedEntities field using reflection. " +
                          "Available fields: " + string.Join(", ",
                              System.Array.ConvertAll(
                                  typeof(BakingSystem).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
                                  f => f.Name)));

            Assert.IsTrue(false, "Couldn't find BakedEntities field using reflection");
            return default; // Return default struct value (will never reach here due to Assert)
        }
    }
}