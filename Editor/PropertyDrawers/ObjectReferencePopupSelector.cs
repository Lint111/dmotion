using System;
using System.Collections.Generic;
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
            var allRepresentations = AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(target));
            var result = new List<T>();
            
            for (int i = 0; i < allRepresentations.Length; i++)
            {
                if (allRepresentations[i] is T typed)
                {
                    // Check filter types if specified
                    if (filterTypes != null && filterTypes.Length > 0)
                    {
                        bool passesFilter = false;
                        for (int j = 0; j < filterTypes.Length; j++)
                        {
                            if (filterTypes[j].IsInstanceOfType(typed))
                            {
                                passesFilter = true;
                                break;
                            }
                        }
                        if (!passesFilter) continue;
                    }
                    result.Add(typed);
                }
            }
            
            return result.ToArray();
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
        
        // Cached single-element array for "no options" popup
        private string[] _noOptionsArray;
        
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
                    var assets = Assets;
                    allAssetNameOptions = new string[assets.Length];
                    for (int i = 0; i < assets.Length; i++)
                    {
                        allAssetNameOptions[i] = assets[i].name;
                    }
                }
                return allAssetNameOptions;
            }
        }
        
        // Cached type filter string to avoid per-call allocation
        private string _typeFilterString;
        
        protected virtual T[] CollectOptions()
        {
            AssetDatabase.Refresh();
            
            // Cache the type filter string
            if (_typeFilterString == null)
            {
                var sb = StringBuilderCache.Get();
                sb.Append("t:").Append(typeof(T).Name);
                _typeFilterString = sb.ToString();
            }
            
            var guids = AssetDatabase.FindAssets(_typeFilterString);
            var result = new T[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                result[i] = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
            }
            return result;
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
                // Cache the no-options array to avoid per-frame allocation
                if (_noOptionsArray == null || _noOptionsArray[0] != NoOptionsMessage)
                {
                    _noOptionsArray = new[] { NoOptionsMessage };
                }
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.Popup(position, 0, _noOptionsArray);
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