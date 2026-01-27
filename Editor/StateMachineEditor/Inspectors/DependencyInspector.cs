using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    internal struct DependencyInspectorModel
    {
        internal StateMachineAsset StateMachine;
    }

    /// <summary>
    /// Inspector for nested state machine parameter dependencies.
    /// Shows which parameters from nested state machines (SubStateMachines and Layers) need to be satisfied by the parent.
    /// </summary>
    internal class DependencyInspector : StateMachineInspector<DependencyInspectorModel>
    {
        private Dictionary<INestedStateMachineContainer, bool> _containerFoldouts = new();
        private List<NestedContainerDependencyInfo> _containerDependencies = new();
        private bool _needsRefresh = true;

        private void OnEnable()
        {
            StateMachineEditorEvents.OnStateMachineChanged += OnStateMachineChanged;
            StateMachineEditorEvents.OnParameterRemoved += OnParameterRemoved;
        }

        private void OnDisable()
        {
            StateMachineEditorEvents.OnStateMachineChanged -= OnStateMachineChanged;
            StateMachineEditorEvents.OnParameterRemoved -= OnParameterRemoved;
        }

        private void OnStateMachineChanged(StateMachineAsset machine)
        {
            if (machine == model.StateMachine)
            {
                _needsRefresh = true;
                Repaint();
            }
        }

        private void OnParameterRemoved(StateMachineAsset machine, AnimationParameterAsset param)
        {
            if (machine == model.StateMachine)
            {
                _needsRefresh = true;
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            if (model.StateMachine == null) return;

            if (_needsRefresh)
            {
                RefreshAnalysis();
                _needsRefresh = false;
            }

            // Show panel as long as there are nested containers (even with no params)
            if (_containerDependencies == null || _containerDependencies.Count == 0)
            {
                return;
            }

            DrawDependenciesSection();
        }

        private void RefreshAnalysis()
        {
            try
            {
                _containerDependencies = AnalyzeAllNestedContainers();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DMotion] Error analyzing dependencies: {e.Message}");
                _containerDependencies = new List<NestedContainerDependencyInfo>();
            }
        }

        private void ForceRefresh()
        {
            _needsRefresh = true;
            RefreshAnalysis();
            Repaint();
        }

        private void DrawDependenciesSection()
        {
            // Calculate totals without LINQ
            int totalRequired = 0;
            int totalResolved = 0;
            for (int i = 0; i < _containerDependencies.Count; i++)
            {
                totalRequired += _containerDependencies[i].TotalRequired;
                totalResolved += _containerDependencies[i].ResolvedCount;
            }
            var totalMissing = totalRequired - totalResolved;

            // Header with summary
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var statusIcon = totalMissing > 0 ? IconCache.WarnIcon : IconCache.CheckmarkIcon;

                GUILayout.Label(statusIcon, GUILayout.Width(18));
                EditorGUILayout.LabelField(GUIContentCache.Dependencies, EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                EditorGUILayout.LabelField(StringBuilderCache.FormatRatio(totalResolved, totalRequired), GUILayout.Width(45));

                if (totalMissing > 0 && GUILayout.Button(GUIContentCache.ResolveAllButton, EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    ResolveAllDependencies();
                }
            }

            // Draw each nested container's dependencies
            for (int i = 0; i < _containerDependencies.Count; i++)
            {
                DrawContainerDependencies(_containerDependencies[i]);
            }
        }

        // Cached tooltip strings
        private const string TooltipNoParams = "No parameters defined in nested machine";
        private const string TooltipMissingDeps = "Missing parameter dependencies";
        private const string TooltipAllResolved = "All dependencies resolved";
        private const string TooltipExplicitLink = "Explicit link";
        private const string TooltipAutoMatched = "Auto-matched by name";
        private const string TooltipMissing = "Missing - no matching parameter in parent";

        // Cached label
        private static readonly GUIContent NoParamsLabel = new GUIContent("(no params)");
        private static GUIContent _grayDotIcon;
        private static GUIContent GrayDotIcon => _grayDotIcon ??= EditorGUIUtility.IconContent("d_winbtn_mac_min");

        private void DrawContainerDependencies(NestedContainerDependencyInfo info)
        {
            var hasMissing = info.MissingCount > 0;

            // Get or create foldout state
            if (!_containerFoldouts.TryGetValue(info.Container, out var isExpanded))
            {
                isExpanded = hasMissing; // Auto-expand if has missing
                _containerFoldouts[info.Container] = isExpanded;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Container header row
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent statusIcon;
                    if (info.HasNoParams)
                    {
                        statusIcon = IconCache.TempIcon(GrayDotIcon.image, TooltipNoParams);
                    }
                    else if (hasMissing)
                    {
                        statusIcon = IconCache.WarnIconWithTooltip(TooltipMissingDeps);
                    }
                    else
                    {
                        statusIcon = IconCache.TempIcon(IconCache.CheckmarkTexture, TooltipAllResolved);
                    }
                    GUILayout.Label(statusIcon, GUILayout.Width(18));

                    // Show type indicator for layers vs submachines
                    var displayName = info.Container is LayerStateAsset ? $"[Layer] {info.Container.name}" : info.Container.name;
                    var newExpanded = EditorGUILayout.Foldout(isExpanded, displayName, true);
                    if (newExpanded != isExpanded)
                    {
                        _containerFoldouts[info.Container] = newExpanded;
                    }

                    GUILayout.FlexibleSpace();

                    if (info.HasNoParams)
                    {
                        EditorGUILayout.LabelField(NoParamsLabel, EditorStyles.miniLabel, GUILayout.Width(60));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(StringBuilderCache.FormatRatio(info.ResolvedCount, info.TotalRequired), 
                            EditorStyles.miniLabel, GUILayout.Width(40));

                        if (hasMissing && GUILayout.Button(GUIContentCache.ResolveButton, EditorStyles.miniButton, GUILayout.Width(55)))
                        {
                            ResolveContainerDependencies(info.Container);
                        }
                    }
                }

                // Expanded content
                if (_containerFoldouts[info.Container])
                {
                    EditorGUILayout.Space(4);

                    if (info.HasNoParams)
                    {
                        EditorGUILayout.HelpBox("No parameters defined in the nested state machine.", MessageType.Info);
                    }
                    else
                    {
                        // Show missing requirements first (more actionable)
                        if (info.MissingRequirements.Count > 0)
                        {
                            EditorGUILayout.LabelField(StringBuilderCache.FormatMissingCount(info.MissingRequirements.Count), EditorStyles.boldLabel);
                            for (int i = 0; i < info.MissingRequirements.Count; i++)
                            {
                                DrawMissingRequirementRow(info.MissingRequirements[i], info.Container);
                            }
                            EditorGUILayout.Space(4);
                        }

                        // Show resolved dependencies
                        if (info.ResolvedDependencies.Count > 0)
                        {
                            EditorGUILayout.LabelField(StringBuilderCache.FormatResolvedCount(info.ResolvedDependencies.Count), EditorStyles.boldLabel);
                            for (int i = 0; i < info.ResolvedDependencies.Count; i++)
                            {
                                DrawResolvedDependencyRow(info.ResolvedDependencies[i], info.Container);
                            }
                        }
                    }
                }
            }
        }

        // Cached fallback names
        private const string DeletedName = "(deleted)";
        private const string UnknownName = "(unknown)";

        private void DrawResolvedDependencyRow(ResolvedDependency resolved, INestedStateMachineContainer container)
        {
            // If source is null (deleted), this dependency is no longer valid - force refresh
            if (resolved.SourceParameter == null)
            {
                _needsRefresh = true;
                return;
            }
            
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var icon = resolved.IsExplicitLink 
                    ? EditorGUIUtility.IconContent("d_Linked")
                    : EditorGUIUtility.IconContent("d_Valid");
                icon.tooltip = resolved.IsExplicitLink ? "Explicit link (click X to remove)" : "Auto-matched by name (click X to break)";
                GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(18));

                var targetName = resolved.TargetParameter != null ? resolved.TargetParameter.name : "(unknown)";
                
                EditorGUILayout.LabelField(resolved.SourceParameter.name, EditorStyles.boldLabel, GUILayout.MinWidth(60));
                EditorGUILayout.LabelField("->", GUILayout.Width(20));
                EditorGUILayout.LabelField(targetName, GUILayout.MinWidth(60));
                
                GUILayout.FlexibleSpace();

                // Show X button for ALL resolved links (both explicit and auto-matched)
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    if (resolved.IsExplicitLink)
                    {
                        RemoveLink(resolved.SourceParameter, resolved.TargetParameter, container);
                    }
                    else
                    {
                        // For auto-matched links, create an exclusion to break the auto-match
                        CreateExclusion(resolved.TargetParameter, container);
                    }
                }
            }
        }

        private void CreateExclusion(AnimationParameterAsset target, INestedStateMachineContainer container)
        {
            Undo.SetCurrentGroupName("Break Auto-Match");
            var undoGroup = Undo.GetCurrentGroup();
            
            Undo.RecordObject(model.StateMachine, "Break Auto-Match");
            
            var exclusion = ParameterLink.Exclusion(target, container);
            model.StateMachine.AddLink(exclusion);
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(model.StateMachine);
            
            StateMachineEditorEvents.RaiseStateMachineChanged(model.StateMachine);
            ForceRefresh();
        }

        // Cached background color for missing rows
        private static readonly Color MissingRowBgColor = new Color(1f, 0.8f, 0.4f, 0.15f);

        // Cached options array to avoid allocations (resized as needed)
        private string[] _cachedOptions;
        private List<AnimationParameterAsset> _cachedCompatibleParams;

        private void DrawMissingRequirementRow(ParameterRequirement req, INestedStateMachineContainer container)
        {
            var rect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUI.DrawRect(rect, MissingRowBgColor);
            
            GUILayout.Label(IconCache.WarnIconWithTooltip(TooltipMissing), GUILayout.Width(18), GUILayout.Height(18));

            // Dropdown to select/create source parameter
            GetCompatibleParametersNonAlloc(req.Parameter);
            
            // Build options array (reuse if possible)
            int optionsCount = _cachedCompatibleParams.Count + 1;
            if (_cachedOptions == null || _cachedOptions.Length < optionsCount)
            {
                _cachedOptions = new string[optionsCount];
            }
            _cachedOptions[0] = "(Select or Create)";
            for (int i = 0; i < _cachedCompatibleParams.Count; i++)
            {
                _cachedOptions[i + 1] = _cachedCompatibleParams[i].name;
            }

            var dropdownRect = EditorGUILayout.GetControlRect(GUILayout.MinWidth(80), GUILayout.MaxWidth(120));
            // Need to pass correctly sized array to Popup
            if (_cachedOptions.Length > optionsCount)
            {
                // Clear extra entries to avoid showing stale data
                for (int j = optionsCount; j < _cachedOptions.Length; j++)
                    _cachedOptions[j] = null;
            }
            var newIndex = EditorGUI.Popup(dropdownRect, 0, _cachedOptions);

            if (newIndex > 0 && newIndex <= _cachedCompatibleParams.Count)
            {
                var selectedParam = _cachedCompatibleParams[newIndex - 1];
                CreateLink(selectedParam, req.Parameter, container);
            }
            
            EditorGUILayout.LabelField(GUIContentCache.Arrow, GUILayout.Width(20));
            EditorGUILayout.LabelField(req.Parameter.name, EditorStyles.boldLabel, GUILayout.MinWidth(60));
            EditorGUILayout.LabelField(StringBuilderCache.FormatTypeName(req.Parameter.ParameterTypeName), EditorStyles.miniLabel, GUILayout.Width(55));
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button(GUIContentCache.PlusButton, EditorStyles.miniButton, GUILayout.Width(20)))
            {
                CreateAndLinkParameter(req, container);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void GetCompatibleParametersNonAlloc(AnimationParameterAsset target)
        {
            if (_cachedCompatibleParams == null)
                _cachedCompatibleParams = new List<AnimationParameterAsset>(8);
            else
                _cachedCompatibleParams.Clear();

            var targetType = target.GetType();
            var parameters = model.StateMachine.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].GetType() == targetType)
                {
                    _cachedCompatibleParams.Add(parameters[i]);
                }
            }
        }

        #region Actions

        private void ResolveAllDependencies()
        {
            Undo.SetCurrentGroupName("Resolve All Parameter Dependencies");
            var undoGroup = Undo.GetCurrentGroup();
            
            Undo.RecordObject(model.StateMachine, "Resolve All Parameter Dependencies");

            foreach (var info in _containerDependencies)
            {
                if (info.MissingCount > 0)
                {
                    ResolveContainerDependenciesInternal(info.Container);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(model.StateMachine);
            ForceRefresh();
        }

        private void ResolveContainerDependencies(INestedStateMachineContainer container)
        {
            Undo.SetCurrentGroupName("Resolve Parameter Dependencies");
            var undoGroup = Undo.GetCurrentGroup();
            
            Undo.RecordObject(model.StateMachine, "Resolve Parameter Dependencies");
            ResolveContainerDependenciesInternal(container);
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(model.StateMachine);
            AssetDatabase.SaveAssets();
            ForceRefresh();
        }

        private void ResolveContainerDependenciesInternal(INestedStateMachineContainer container)
        {
            // First, remove any exclusions for this container so auto-matching works
            model.StateMachine.RemoveExclusionsForContainer(container);
            
            var result = ParameterDependencyAnalyzer.ResolveParameterDependencies(model.StateMachine, container);

            // Add links for parameters that can be matched
            if (result.HasLinks)
            {
                foreach (var link in result.ParameterLinks)
                {
                    model.StateMachine.AddLink(link);
                    StateMachineEditorEvents.RaiseLinkAdded(model.StateMachine, link);
                }
            }

            // Create missing parameters (user explicitly clicked Resolve)
            if (result.HasMissingParameters)
            {
                var assetPath = AssetDatabase.GetAssetPath(model.StateMachine);
                var created = ParameterDependencyAnalyzer.CreateMissingParameters(
                    model.StateMachine, container, result.MissingParameters, assetPath);

                for (int i = 0; i < created.Count; i++)
                {
                    StateMachineEditorEvents.RaiseParameterAdded(model.StateMachine, created[i]);
                }
            }
        }

        private void CreateLink(AnimationParameterAsset source, AnimationParameterAsset target, INestedStateMachineContainer container)
        {
            Undo.SetCurrentGroupName("Create Parameter Link");
            var undoGroup = Undo.GetCurrentGroup();
            
            Undo.RecordObject(model.StateMachine, "Create Parameter Link");
            Undo.RecordObject(source, "Create Parameter Link");
            
            // Remove any existing exclusion for this target
            model.StateMachine.RemoveLink(null, target, container);
            
            var link = ParameterLink.Direct(source, target, container);
            model.StateMachine.AddLink(link);
            
            // Track RequiredBy only for SubStateMachines (backwards compatibility)
            if (container is SubStateMachineStateAsset subMachine)
            {
                source.AddRequiredBy(subMachine);
            }
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(model.StateMachine);
            EditorUtility.SetDirty(source);
            
            StateMachineEditorEvents.RaiseLinkAdded(model.StateMachine, link);
            ForceRefresh();
        }

        // Cached single-item list for CreateAndLinkParameter
        private readonly List<ParameterRequirement> _singleReqList = new List<ParameterRequirement>(1);

        private void CreateAndLinkParameter(ParameterRequirement req, INestedStateMachineContainer container)
        {
            Undo.SetCurrentGroupName("Create and Link Parameter");
            var undoGroup = Undo.GetCurrentGroup();
            
            Undo.RecordObject(model.StateMachine, "Create and Link Parameter");

            var assetPath = AssetDatabase.GetAssetPath(model.StateMachine);
            
            // Reuse single-item list
            _singleReqList.Clear();
            _singleReqList.Add(req);
            
            var created = ParameterDependencyAnalyzer.CreateMissingParameters(
                model.StateMachine, container, _singleReqList, assetPath);

            if (created.Count > 0)
            {
                var link = ParameterLink.Direct(created[0], req.Parameter, container);
                model.StateMachine.AddLink(link);
                
                Undo.CollapseUndoOperations(undoGroup);
                EditorUtility.SetDirty(model.StateMachine);
                
                StateMachineEditorEvents.RaiseParameterAdded(model.StateMachine, created[0]);
                StateMachineEditorEvents.RaiseLinkAdded(model.StateMachine, link);
            }
            else
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private void RemoveLink(AnimationParameterAsset source, AnimationParameterAsset target, INestedStateMachineContainer container)
        {
            Undo.SetCurrentGroupName("Remove Parameter Link");
            var undoGroup = Undo.GetCurrentGroup();
            
            Undo.RecordObject(model.StateMachine, "Remove Parameter Link");
            if (source != null)
            {
                Undo.RecordObject(source, "Remove Parameter Link");
                // Track RequiredBy only for SubStateMachines (backwards compatibility)
                if (container is SubStateMachineStateAsset subMachine)
                {
                    source.RemoveRequiredBy(subMachine);
                }
            }
            
            var link = ParameterLink.Direct(source, target, container);
            model.StateMachine.RemoveLink(source, target, container);
            
            // Also add an exclusion to prevent auto-matching
            // (otherwise a compatible param with same name would immediately auto-match)
            var exclusion = ParameterLink.Exclusion(target, container);
            model.StateMachine.AddLink(exclusion);
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(model.StateMachine);
            
            StateMachineEditorEvents.RaiseLinkRemoved(model.StateMachine, link);
        }

        private List<NestedContainerDependencyInfo> AnalyzeAllNestedContainers()
        {
            var result = new List<NestedContainerDependencyInfo>();
            
            foreach (var container in model.StateMachine.GetAllNestedContainers())
            {
                // For SubStateMachines, analyze parameter requirements
                // For Layers, show nested state machine parameters
                var nestedMachine = container.NestedStateMachine;
                if (nestedMachine == null) continue;
                
                List<ParameterRequirement> requirements;
                if (container is SubStateMachineStateAsset subMachine)
                {
                    requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(subMachine);
                }
                else
                {
                    // For layers, treat all parameters in the nested machine as requirements
                    requirements = new List<ParameterRequirement>();
                    foreach (var param in nestedMachine.Parameters)
                    {
                        requirements.Add(new ParameterRequirement { Parameter = param });
                    }
                }
                
                // Include containers even if they have no params (show as "no params")
                var info = new NestedContainerDependencyInfo
                {
                    Container = container,
                    TotalRequired = requirements.Count,
                    HasNoParams = requirements.Count == 0,
                    MissingRequirements = new List<ParameterRequirement>(),
                    ResolvedDependencies = new List<ResolvedDependency>()
                };

                foreach (var req in requirements)
                {
                    ParameterLink? explicitLink = null;
                    bool hasExclusion = false;
                    
                    // Check links for this container
                    foreach (var link in model.StateMachine.ParameterLinks)
                    {
                        if (link.NestedContainer == container && link.TargetParameter == req.Parameter)
                        {
                            if (link.IsExclusion)
                            {
                                // Exclusion marker - prevents auto-matching
                                hasExclusion = true;
                            }
                            else
                            {
                                explicitLink = link;
                            }
                            break;
                        }
                    }

                    if (explicitLink.HasValue && explicitLink.Value.IsValid)
                    {
                        // Valid explicit link
                        info.ResolvedDependencies.Add(new ResolvedDependency
                        {
                            SourceParameter = explicitLink.Value.SourceParameter,
                            TargetParameter = req.Parameter,
                            IsExplicitLink = true
                        });
                    }
                    else if (hasExclusion)
                    {
                        // Excluded from auto-matching - show as missing
                        info.MissingRequirements.Add(req);
                    }
                    else
                    {
                        // Try auto-matching by name/type
                        var compatibleParam = ParameterDependencyAnalyzer.FindCompatibleParameter(model.StateMachine, req.Parameter);
                        if (compatibleParam != null)
                        {
                            info.ResolvedDependencies.Add(new ResolvedDependency
                            {
                                SourceParameter = compatibleParam,
                                TargetParameter = req.Parameter,
                                IsExplicitLink = false
                            });
                        }
                        else
                        {
                            info.MissingRequirements.Add(req);
                        }
                    }
                }

                info.ResolvedCount = info.ResolvedDependencies.Count;
                result.Add(info);
            }

            return result;
        }

        #endregion

        #region Data Types

        private struct NestedContainerDependencyInfo
        {
            public INestedStateMachineContainer Container;
            public int TotalRequired;
            public int ResolvedCount;
            public bool HasNoParams;
            public List<ParameterRequirement> MissingRequirements;
            public List<ResolvedDependency> ResolvedDependencies;
            public int MissingCount => MissingRequirements?.Count ?? 0;
        }

        private struct ResolvedDependency
        {
            public AnimationParameterAsset SourceParameter;
            public AnimationParameterAsset TargetParameter;
            public bool IsExplicitLink;
        }

        #endregion
    }
}
