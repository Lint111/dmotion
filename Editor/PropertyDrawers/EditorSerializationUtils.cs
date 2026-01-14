using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace DMotion.Editor
{
    public static class EditorSerializationUtils
    {
        /// <summary>
        /// Applies modifications and updates the serialized object in one call.
        /// Use this instead of separate ApplyModifiedProperties() + Update() calls.
        /// </summary>
        public static void ApplyAndUpdate(this SerializedObject serializedObject)
        {
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
        }

        /// <summary>
        /// Applies modifications and updates the serialized object from a property.
        /// </summary>
        public static void ApplyAndUpdate(this SerializedProperty property)
        {
            property.serializedObject.ApplyModifiedProperties();
            property.serializedObject.Update();
        }

        private const BindingFlags AllBindingFlags = (BindingFlags)(-1);

        public static TAttribute GetAttribute<TAttribute>(this SerializedProperty serializedProperty,
            bool inherit = true)
            where TAttribute : Attribute
        {
            if (serializedProperty == null)
            {
                throw new ArgumentNullException(nameof(serializedProperty));
            }

            var targetObjectType = serializedProperty.serializedObject.targetObject.GetType();

            if (targetObjectType == null)
            {
                throw new ArgumentException($"Could not find the {nameof(targetObjectType)} of {nameof(serializedProperty)}");
            }

            foreach (var pathSegment in serializedProperty.propertyPath.Split('.'))
            {
                var fieldInfo = targetObjectType.GetField(pathSegment, AllBindingFlags);
                if (fieldInfo != null)
                {
                    return fieldInfo.GetCustomAttribute<TAttribute>(inherit);
                }

                var propertyInfo = targetObjectType.GetProperty(pathSegment, AllBindingFlags);
                if (propertyInfo != null)
                {
                    return propertyInfo.GetCustomAttribute<TAttribute>(inherit);
                }
            }

            throw new ArgumentException($"Could not find the field or property of {nameof(serializedProperty)}");
        }
        public static IEnumerable<TAttribute> GetAttributes<TAttribute>(this SerializedProperty serializedProperty, bool inherit = true)
            where TAttribute : Attribute
        {
            if (serializedProperty == null)
            {
                throw new ArgumentNullException(nameof(serializedProperty));
            }

            var targetObjectType = serializedProperty.serializedObject.targetObject.GetType();

            if (targetObjectType == null)
            {
                throw new ArgumentException($"Could not find the {nameof(targetObjectType)} of {nameof(serializedProperty)}");
            }

            foreach (var pathSegment in serializedProperty.propertyPath.Split('.'))
            {
                var fieldInfo = targetObjectType.GetField(pathSegment, AllBindingFlags);
                if (fieldInfo != null)
                {
                    return fieldInfo.GetCustomAttributes<TAttribute>(inherit);
                }

                var propertyInfo = targetObjectType.GetProperty(pathSegment, AllBindingFlags);
                if (propertyInfo != null)
                {
                    return propertyInfo.GetCustomAttributes<TAttribute>(inherit);
                }
            }

            throw new ArgumentException($"Could not find the field or property of {nameof(serializedProperty)}");
        }
    }
}