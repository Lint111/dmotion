using System;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Marks a field to be loaded and converted to an Entity during test setup.
    /// The prefab is loaded via AssetDatabase and baked to create an Entity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ConvertGameObjectPrefab : Attribute
    {
        /// <summary>
        /// Name of the Entity field that will receive the baked entity.
        /// </summary>
        public string ToFieldName;

        /// <summary>
        /// Asset path relative to project (e.g., "Packages/com.gamedevpro.dmotion/Tests/Data/MyPrefab.prefab").
        /// If null, the field must be pre-assigned (legacy behavior for manual setup).
        /// </summary>
        public string AssetPath;

        public ConvertGameObjectPrefab(string toFieldName, string assetPath = null)
        {
            ToFieldName = toFieldName;
            AssetPath = assetPath;
        }
    }
}