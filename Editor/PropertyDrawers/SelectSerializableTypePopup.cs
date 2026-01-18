using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace DMotion.Editor
{
    public class SelectSerializableTypePopup : EditorWindow
    {
        private Type[] types = new Type[0];
        private GUIContent[] typeNames = new GUIContent[0];
        private string searchString = "";

        private static HashSet<Assembly> invalidAssemblies;

        private Vector2 scrollPos;

        private Type baseType;
        private Action<Type> onSelected;
        private Predicate<Type> filter;

        public void Show(Type currentType, Type baseType, Action<Type> onSelected, Predicate<Type> filter)
        {
            if (baseType == null)
            {
                baseType = typeof(object);
            }

            this.baseType = baseType;
            this.onSelected = onSelected;
            this.filter = filter;

            if (currentType != null)
            {
                searchString = currentType.Name;
            }

            UpdateTypes();

            base.Show();
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void RefreshInvalidAssemblies()
        {
            invalidAssemblies = new HashSet<Assembly>();
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < allAssemblies.Length; i++)
            {
                var a = allAssemblies[i];
                var fullName = a.FullName;
                if (fullName.StartsWith("System.", true, CultureInfo.CurrentCulture) ||
                    fullName.StartsWith("Unity.", true, CultureInfo.CurrentCulture) ||
                    fullName.StartsWith("com.unity", true, CultureInfo.CurrentCulture) ||
                    fullName.StartsWith("Microsoft") ||
                    fullName.StartsWith("Mono"))
                {
                    invalidAssemblies.Add(a);
                }
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawSearchField();
                DrawOptions();
            }
        }

        int selected = -1;

        private void DrawOptions()
        {
            using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    var prevSelected = selected;
                    selected = GUILayout.SelectionGrid(selected, typeNames, 1);
                    if (selected != prevSelected)
                    {
                        var selectedType = types[selected];
                        onSelected?.Invoke(selectedType);
                        Close();
                    }
                }

                scrollPos = scrollView.scrollPosition;
            }
        }

        private void DrawSearchField()
        {
            var prev = searchString;
            searchString = EditorGUILayout.TextField(searchString, EditorStyles.toolbarSearchField);
            if (prev != searchString)
            {
                UpdateTypes();
            }
        }

        // Reusable list for collecting filtered types
        private List<Type> _filteredTypes = new List<Type>();
        
        private void UpdateTypes()
        {
            _filteredTypes.Clear();
            var allTypes = TypeCache.GetTypesDerivedFrom(baseType);
            foreach (var t in allTypes)
            {
                if (IsValidType(t) && MatchesSearch(t))
                {
                    _filteredTypes.Add(t);
                }
            }
            
            // Sort by FullName
            _filteredTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            
            types = _filteredTypes.ToArray();
            typeNames = new GUIContent[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                typeNames[i] = new GUIContent(types[i].Name);
            }
        }

        private bool IsValidType(Type t)
        {
            var passesFilter = filter == null || filter(t);
            if (passesFilter)
            {
                return !invalidAssemblies.Contains(t.Assembly);
            }
            return false;
        }

        private bool MatchesSearch(Type t)
        {
            long score = 0;
            return FuzzySearch.FuzzyMatch(searchString, t.FullName, ref score);
        }
    }
}