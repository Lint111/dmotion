using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DMotion.Editor
{
    internal class OnAssetsChangedPostProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            ObjectReferencePopupSelector.IsDirty = true;
        }
    }
    
    internal class SubAssetReferencePopupSelector<T> : ObjectReferencePopupSelector<T>
        where T : Object
    {
        private Object target;
        private Type[] filterTypes;
        private string customNoOptionsMessage;

        internal SubAssetReferencePopupSelector(Object target, params Type[] filterTypes)
        {
            this.target = target;
            this.filterTypes = filterTypes;
        }

        internal SubAssetReferencePopupSelector(Object target, string noOptionsMessage, params Type[] filterTypes)
        {
            this.target = target;
            this.filterTypes = filterTypes;
            this.customNoOptionsMessage = noOptionsMessage;
        }

        protected override string NoOptionsMessage => 
            string.IsNullOrEmpty(customNoOptionsMessage) ? base.NoOptionsMessage : customNoOptionsMessage;
        
        protected override T[] CollectOptions()
        {
            var childs = AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(target))
                .OfType<T>();

            if (filterTypes != null && filterTypes.Length > 0)
            {
                childs = childs.Where(t => filterTypes.Any(f => f.IsInstanceOfType(t)));
            }

            return childs.ToArray();
        }
    }

    internal class ObjectReferencePopupSelector
    {
        internal static bool IsDirty;
    }
    internal class ObjectReferencePopupSelector<T> : ObjectReferencePopupSelector
        where T : Object
    {
        private T[] allAssets;
        private string[] allAssetNameOptions;
        
        internal bool HasOptions => Assets != null && Assets.Length > 0;
        
        private T[] Assets
        {
            get
            {
                if (allAssets == null || IsDirty)
                {
                    allAssets = CollectOptions();
                }
 
                return allAssets;
            }
        }
 
        private string[] AssetNameOptions
        {
            get
            {
                if (allAssetNameOptions == null || IsDirty)
                {
                    allAssetNameOptions = Assets.Select(e => e.name).ToArray();
                }
                return allAssetNameOptions;
            }
        }
        
        protected virtual T[] CollectOptions()
        {
            AssetDatabase.Refresh();
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Select(g =>
                    AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .ToArray();
        }

        /// <summary>
        /// Message to display when no options are available. Override to customize.
        /// </summary>
        protected virtual string NoOptionsMessage => "No options available";

        internal void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (label != GUIContent.none)
            {
                var prevXMax = position.xMax;
                position.width = EditorGUIUtility.labelWidth;
                
                EditorGUI.LabelField(position, label);
                position.xMin += EditorGUIUtility.labelWidth;
                
                position.xMax = prevXMax;
            }

            // Show helpful message when no options are available
            if (!HasOptions)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.Popup(position, 0, new[] { NoOptionsMessage });
                }
                IsDirty = false;
                return;
            }
             
            var currEventName = property.objectReferenceValue as T;
            var index = Array.FindIndex(Assets, e => e == currEventName);
            var newIndex = EditorGUI.Popup(position, index, AssetNameOptions);
            if (index != newIndex)
            {
                property.objectReferenceValue = Assets[newIndex];
            }

            IsDirty = false;
        }
    }
}