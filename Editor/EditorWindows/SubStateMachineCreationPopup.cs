using System;
using System.Collections.Generic;
using System.IO;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Popup window for creating a SubStateMachine node.
    /// Allows selecting an existing StateMachineAsset or creating a new one.
    /// </summary>
    internal class SubStateMachineCreationPopup : EditorWindow
    {
        private StateMachineAsset parentStateMachine;
        private Vector2 graphPosition;
        private Action<SubStateMachineStateAsset> onCreated;
        
        private StateMachineAsset[] availableStateMachines;
        private string[] stateMachineNames;
        private int selectedIndex = -1;
        private Vector2 scrollPosition;
        private string searchFilter = "";

        private const float WindowWidth = 300;
        private const float WindowHeight = 350;

        // Cached title content (only created once per window instance)
        private static readonly GUIContent WindowTitle = new GUIContent("Select State Machine");
        
        // Cached colors
        private static readonly Color SeparatorColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        private static readonly Color SelectionColor = new Color(0.24f, 0.48f, 0.9f, 0.5f);
        
        // Cached for circular reference check
        private HashSet<StateMachineAsset> _circularCheckVisited;
        
        // Cached filtered results
        private List<StateMachineAsset> _filteredMachinesCache;
        private string _lastSearchFilter;

        public static void Show(StateMachineAsset parent, Vector2 graphPos, Action<SubStateMachineStateAsset> callback)
        {
            var window = CreateInstance<SubStateMachineCreationPopup>();
            window.parentStateMachine = parent;
            window.graphPosition = graphPos;
            window.onCreated = callback;
            window.titleContent = WindowTitle;
            
            // Position near mouse (use screen center as fallback if Event.current is null)
            Vector2 screenPos;
            if (Event.current != null)
            {
                screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            }
            else
            {
                // Fallback to center of main window
                screenPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            }
            window.position = new Rect(screenPos.x - WindowWidth / 2, screenPos.y, WindowWidth, WindowHeight);
            
            window.RefreshStateMachineList();
            window.ShowPopup();
            window.Focus();
        }

        private void OnEnable()
        {
            RefreshStateMachineList();
        }

        private void RefreshStateMachineList()
        {
            // Find all StateMachineAssets in the project (no LINQ to avoid allocations)
            var guids = AssetDatabase.FindAssets("t:StateMachineAsset");
            
            // Use pooled list for intermediate results
            var tempList = ListPool<StateMachineAsset>.Get();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var sm = AssetDatabase.LoadAssetAtPath<StateMachineAsset>(path);
                    if (sm != null && !WouldCreateCircularReference(sm))
                    {
                        tempList.Add(sm);
                    }
                }
                
                // Sort by name
                tempList.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                
                // Convert to array
                availableStateMachines = tempList.ToArray();
                
                // Build names array
                stateMachineNames = new string[availableStateMachines.Length];
                for (int i = 0; i < availableStateMachines.Length; i++)
                {
                    stateMachineNames[i] = availableStateMachines[i].name;
                }
            }
            finally
            {
                ListPool<StateMachineAsset>.Return(tempList);
            }
        }

        /// <summary>
        /// Checks if assigning the candidate as a nested machine would create a circular reference.
        /// Returns true if it would be circular (candidate contains parent, or candidate IS parent).
        /// </summary>
        private bool WouldCreateCircularReference(StateMachineAsset candidate)
        {
            // Can't reference self
            if (candidate == parentStateMachine)
                return true;
            
            // Check if candidate already contains parentStateMachine (directly or nested)
            if (_circularCheckVisited == null)
                _circularCheckVisited = new HashSet<StateMachineAsset>();
            else
                _circularCheckVisited.Clear();
            
            return ContainsStateMachine(candidate, parentStateMachine, _circularCheckVisited);
        }

        /// <summary>
        /// Recursively checks if 'machine' contains 'target' as a nested state machine.
        /// </summary>
        private static bool ContainsStateMachine(StateMachineAsset machine, StateMachineAsset target, HashSet<StateMachineAsset> visited)
        {
            if (machine == null || target == null)
                return false;
            
            // Prevent infinite recursion from existing circular refs
            if (visited.Contains(machine))
                return false;
            visited.Add(machine);

            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subState && subState.NestedStateMachine != null)
                {
                    if (subState.NestedStateMachine == target)
                        return true;
                    
                    if (ContainsStateMachine(subState.NestedStateMachine, target, visited))
                        return true;
                }
            }

            return false;
        }

        private void OnGUI()
        {
            // Handle escape key
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                return;
            }

            EditorGUILayout.Space(5);
            
            // Header
            EditorGUILayout.LabelField("Create Sub-State Machine", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Create New button at top
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create New State Machine...", GUILayout.Height(28)))
                {
                    CreateNewStateMachine();
                }
            }

            EditorGUILayout.Space(5);
            
            // Separator
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, SeparatorColor);
            
            EditorGUILayout.Space(5);
            
            // Search field
            EditorGUILayout.LabelField("Or select existing:", EditorStyles.miniLabel);
            
            EditorGUI.BeginChangeCheck();
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                selectedIndex = -1;
            }

            EditorGUILayout.Space(3);

            // Filtered list (cached to avoid per-frame allocations)
            StateMachineAsset[] filteredMachines;
            if (string.IsNullOrEmpty(searchFilter))
            {
                filteredMachines = availableStateMachines;
            }
            else
            {
                // Only rebuild filter if search changed
                if (_filteredMachinesCache == null || _lastSearchFilter != searchFilter)
                {
                    if (_filteredMachinesCache == null)
                        _filteredMachinesCache = new List<StateMachineAsset>();
                    else
                        _filteredMachinesCache.Clear();
                    
                    for (int i = 0; i < availableStateMachines.Length; i++)
                    {
                        var sm = availableStateMachines[i];
                        if (sm.name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _filteredMachinesCache.Add(sm);
                        }
                    }
                    _lastSearchFilter = searchFilter;
                }
                filteredMachines = _filteredMachinesCache.Count > 0 ? _filteredMachinesCache.ToArray() : Array.Empty<StateMachineAsset>();
            }

            if (filteredMachines.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    availableStateMachines.Length == 0 
                        ? "No State Machine assets found in project.\nCreate a new one using the button above."
                        : "No matches found.",
                    MessageType.Info);
            }
            else
            {
                // Scrollable list of state machines
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                
                for (int i = 0; i < filteredMachines.Length; i++)
                {
                    var sm = filteredMachines[i];
                    var originalIndex = Array.IndexOf(availableStateMachines, sm);
                    
                    var isSelected = selectedIndex == originalIndex;
                    
                    // Draw selection background
                    var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
                    
                    if (isSelected)
                    {
                        EditorGUI.DrawRect(rowRect, SelectionColor);
                    }
                    
                    // Clickable row
                    if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                    {
                        if (isSelected)
                        {
                            // Double-click or click on selected - confirm
                            SelectStateMachine(sm);
                        }
                        else
                        {
                            selectedIndex = originalIndex;
                        }
                    }
                    
                    // State machine name
                    GUILayout.Space(8);
                    GUILayout.Label(sm.name, EditorStyles.label, GUILayout.ExpandWidth(true));
                    
                    // Show state count as info
                    var stateCount = sm.States?.Count ?? 0;
                    GUILayout.Label(StringBuilderCache.FormatCount(stateCount), EditorStyles.miniLabel, GUILayout.Width(35));
                    GUILayout.Space(4);
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(5);

            // Bottom buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                
                if (GUILayout.Button("Cancel", GUILayout.Width(80)))
                {
                    Close();
                }
                
                using (new EditorGUI.DisabledScope(selectedIndex < 0 || selectedIndex >= availableStateMachines.Length))
                {
                    if (GUILayout.Button("Select", GUILayout.Width(80)))
                    {
                        SelectStateMachine(availableStateMachines[selectedIndex]);
                    }
                }
            }
            
            EditorGUILayout.Space(5);
        }

        private void CreateNewStateMachine()
        {
            // Get directory of parent state machine
            var parentPath = AssetDatabase.GetAssetPath(parentStateMachine);
            var directory = string.IsNullOrEmpty(parentPath) ? "Assets" : Path.GetDirectoryName(parentPath);

            var path = EditorUtility.SaveFilePanelInProject(
                "Create New State Machine",
                "NewStateMachine",
                "asset",
                "Choose a location for the new State Machine",
                directory);

            if (string.IsNullOrEmpty(path))
                return;

            // Create the asset
            var newStateMachine = ScriptableObject.CreateInstance<StateMachineAsset>();
            AssetDatabase.CreateAsset(newStateMachine, path);
            AssetDatabase.SaveAssets();

            // Create the SubStateMachine node with this new asset
            CreateSubStateMachineWithAsset(newStateMachine);
            
            // Ping the new asset
            EditorGUIUtility.PingObject(newStateMachine);
            
            Close();
        }

        private void SelectStateMachine(StateMachineAsset stateMachine)
        {
            CreateSubStateMachineWithAsset(stateMachine);
            Close();
        }

        private void CreateSubStateMachineWithAsset(StateMachineAsset nestedMachine)
        {
            // Create the SubStateMachine state
            var state = parentStateMachine.CreateState(typeof(SubStateMachineStateAsset)) as SubStateMachineStateAsset;
            state.StateEditorData.GraphPosition = graphPosition;
            
            // Assign the nested machine
            state.NestedStateMachine = nestedMachine;
            
            // Auto-set entry state to default if available
            if (nestedMachine.DefaultState != null)
            {
                state.EntryState = nestedMachine.DefaultState;
            }
            
            // Name the node after the nested machine
            state.name = nestedMachine.name;
            
            // Auto-resolve parameter dependencies
            ResolveParameterDependencies(state);
            
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(parentStateMachine);
            
            onCreated?.Invoke(state);
        }

        private void ResolveParameterDependencies(SubStateMachineStateAsset subMachine)
        {
            var result = ParameterDependencyAnalyzer.ResolveParameterDependencies(parentStateMachine, subMachine);
            
            // Only auto-link parameters that have 1:1 name/type matches
            // Don't auto-create parameters - user should do this manually via Dependencies panel
            if (result.HasLinks)
            {
                parentStateMachine.AddParameterLinks(result.ParameterLinks);
                
                // Build names string without LINQ
                var sb = StringBuilderCache.Get();
                var links = result.ParameterLinks;
                for (int i = 0; i < links.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(links[i].SourceParameter.name);
                }
                Debug.Log($"[DMotion] Auto-linked {links.Count} parameter(s) for '{subMachine.name}': {sb}");
            }
            
            // Log missing parameters so user knows to create them
            if (result.HasMissingParameters)
            {
                var sb = StringBuilderCache.Get();
                var missing = result.MissingParameters;
                for (int i = 0; i < missing.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(missing[i].Parameter.name);
                }
                Debug.Log($"[DMotion] Missing parameters for '{subMachine.name}' - create via Dependencies panel: {sb}");
            }
        }

        private void OnLostFocus()
        {
            // Close when clicking outside
            Close();
        }
    }
}
