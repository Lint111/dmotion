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
            EditorState.Instance.StructureChanged += OnStructureChanged;
        }

        private void OnDisable()
        {
            EditorState.Instance.StructureChanged -= OnStructureChanged;
        }

        private void OnStructureChanged(object sender, StructureChangedEventArgs e)
        {
            if (e.ChangeType == StructureChangeType.GeneralChange ||
                e.ChangeType == StructureChangeType.ParameterRemoved)
            {
                if (EditorState.Instance.RootStateMachine == model.StateMachine)
                {
                    _needsRefresh = true;
                    Repaint();
                }
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
            // Early return if state machine is null or destroyed
            if (model.StateMachine == null || !model.StateMachine)
            {
                _containerDependencies = new List<NestedContainerDependencyInfo>();
                return;
            }
            
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
        
        private static Texture2D _grayDotTexture;
        private static Texture2D GrayDotTexture
        {
            get
            {
                if (_grayDotTexture != null) return _grayDotTexture;
                
                // Try built-in small dot icon
                var content = EditorGUIUtility.IconContent("sv_icon_dot0_sml");
                if (content?.image is Texture2D tex)
                {
                    _grayDotTexture = tex;
                    return _grayDotTexture;
                }
                
                // Create a simple gray dot texture as fallback
                _grayDotTexture = new Texture2D(12, 12);
                var pixels = new Color[144];
                var center = new Vector2(5.5f, 5.5f);
                for (int y = 0; y < 12; y++)
                {
                    for (int x = 0; x < 12; x++)
                    {
                        var dist = Vector2.Distance(new Vector2(x, y), center);
                        pixels[y * 12 + x] = dist < 4.5f ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.clear;
                    }
                }
                _grayDotTexture.SetPixels(pixels);
                _grayDotTexture.Apply();
                return _grayDotTexture;
            }
        }

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
                        statusIcon = IconCache.TempIcon(GrayDotTexture, TooltipNoParams);
                    }
                    else if (hasMissing)
                    {
                        statusIcon = IconCache.WarnIconWithTooltip(TooltipMissingDeps);
                    }
                    else
                    {
                        statusIcon = IconCache.TempIcon(IconCache.CheckmarkTexture, TooltipAllResolved);
                    }
                    GUILayout.Label(statusIcon, EditorStyleCache.CenteredIcon, GUILayout.Width(18), GUILayout.Height(18));

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
            using (UndoScope.Begin("Break Auto-Match", model.StateMachine))
            {
                var exclusion = ParameterLink.Exclusion(target, container);
                model.StateMachine.AddLink(exclusion);
            }
            
            EditorUtility.SetDirty(model.StateMachine);
            EditorState.Instance.NotifyStateMachineChanged();
            ForceRefresh();
        }

        // Cached background color for missing rows
        private static readonly Color MissingRowBgColor = new Color(1f, 0.8f, 0.4f, 0.15f);

        // Cached options array to avoid allocations (resized as needed)
        private string[] _cachedOptions;
        private List<AnimationParameterAsset> _cachedCompatibleParams;

        private void DrawMissingRequirementRow(ParameterRequirement req, INestedStateMachineContainer container)
        {
            // Skip if the parameter has been destroyed
            if (req.Parameter == null)
                return;
                
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

            if (target == null || model.StateMachine == null) return;
            
            var targetType = target.GetType();
            var parameters = model.StateMachine.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                if (param != null && param.GetType() == targetType)
                {
                    _cachedCompatibleParams.Add(param);
                }
            }
        }

        #region Actions

        private void ResolveAllDependencies()
        {
            using (UndoScope.Begin("Resolve All Parameter Dependencies", model.StateMachine))
            {
                foreach (var info in _containerDependencies)
                {
                    if (info.MissingCount > 0)
                    {
                        ResolveContainerDependenciesInternal(info.Container);
                    }
                }
            }
            
            EditorUtility.SetDirty(model.StateMachine);
            ForceRefresh();
        }

        private void ResolveContainerDependencies(INestedStateMachineContainer container)
        {
            using (UndoScope.Begin("Resolve Parameter Dependencies", model.StateMachine))
            {
                ResolveContainerDependenciesInternal(container);
            }
            
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
                    // Links are structure changes - notify via EditorState
                    EditorState.Instance.NotifyStateMachineChanged();
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
                    EditorState.Instance.NotifyParameterAdded(created[i]);
                }
            }
        }

        private void CreateLink(AnimationParameterAsset source, AnimationParameterAsset target, INestedStateMachineContainer container)
        {
            ParameterLink link;
            using (UndoScope.Begin("Create Parameter Link", model.StateMachine, source))
            {
                // Remove any existing exclusion for this target
                model.StateMachine.RemoveLink(null, target, container);
                
                link = ParameterLink.Direct(source, target, container);
                model.StateMachine.AddLink(link);
                
                // Track RequiredBy only for SubStateMachines (backwards compatibility)
                if (container is SubStateMachineStateAsset subMachine)
                {
                    source.AddRequiredBy(subMachine);
                }
            }
            
            EditorUtility.SetDirty(model.StateMachine);
            EditorUtility.SetDirty(source);
            
            EditorState.Instance.NotifyStateMachineChanged();
            ForceRefresh();
        }

        // Cached single-item list for CreateAndLinkParameter
        private readonly List<ParameterRequirement> _singleReqList = new List<ParameterRequirement>(1);

        private void CreateAndLinkParameter(ParameterRequirement req, INestedStateMachineContainer container)
        {
            var assetPath = AssetDatabase.GetAssetPath(model.StateMachine);
            
            // Reuse single-item list
            _singleReqList.Clear();
            _singleReqList.Add(req);
            
            List<AnimationParameterAsset> created;
            ParameterLink link = default;
            
            using (UndoScope.Begin("Create and Link Parameter", model.StateMachine))
            {
                created = ParameterDependencyAnalyzer.CreateMissingParameters(
                    model.StateMachine, container, _singleReqList, assetPath);

                if (created.Count > 0)
                {
                    link = ParameterLink.Direct(created[0], req.Parameter, container);
                    model.StateMachine.AddLink(link);
                }
            }
            
            if (created.Count > 0)
            {
                EditorUtility.SetDirty(model.StateMachine);
                EditorState.Instance.NotifyParameterAdded(created[0]);
                EditorState.Instance.NotifyStateMachineChanged();
            }
        }

        private void RemoveLink(AnimationParameterAsset source, AnimationParameterAsset target, INestedStateMachineContainer container)
        {
            var link = ParameterLink.Direct(source, target, container);
            
            using (UndoScope.Begin("Remove Parameter Link", model.StateMachine, source))
            {
                if (source != null)
                {
                    // Track RequiredBy only for SubStateMachines (backwards compatibility)
                    if (container is SubStateMachineStateAsset subMachine)
                    {
                        source.RemoveRequiredBy(subMachine);
                    }
                }
                
                model.StateMachine.RemoveLink(source, target, container);
                
                // Also add an exclusion to prevent auto-matching
                // (otherwise a compatible param with same name would immediately auto-match)
                var exclusion = ParameterLink.Exclusion(target, container);
                model.StateMachine.AddLink(exclusion);
            }
            
            EditorUtility.SetDirty(model.StateMachine);
            EditorState.Instance.NotifyStateMachineChanged();
            ForceRefresh();
        }

        private List<NestedContainerDependencyInfo> AnalyzeAllNestedContainers()
        {
            var result = new List<NestedContainerDependencyInfo>();
            
            // Check for null/destroyed state machine
            if (model.StateMachine == null || !model.StateMachine)
                return result;
            
            foreach (var container in model.StateMachine.GetAllNestedContainers())
            {
                // Check for null/destroyed container (Unity object check)
                if (container == null) continue;
                if (container is UnityEngine.Object unityObj && !unityObj) continue;
                
                // For SubStateMachines, analyze parameter requirements
                // For Layers, show nested state machine parameters
                var nestedMachine = container.NestedStateMachine;
                // Use explicit Unity null check for destroyed objects
                if (nestedMachine == null || !nestedMachine) continue;
                
                List<ParameterRequirement> requirements;
                if (container is SubStateMachineStateAsset subMachine)
                {
                    requirements = ParameterDependencyAnalyzer.AnalyzeRequiredParameters(subMachine);
                }
                else
                {
                    // For layers, treat all parameters in the nested machine as requirements
                    requirements = new List<ParameterRequirement>();
                    if (nestedMachine.Parameters != null)
                    {
                        foreach (var param in nestedMachine.Parameters)
                        {
                            // Use explicit Unity null check for destroyed parameters
                            if (param == null || !param) continue;
                            requirements.Add(new ParameterRequirement { Parameter = param });
                        }
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
                    // Skip if the parameter has been destroyed (Unity null check)
                    if (req.Parameter == null || !req.Parameter) continue;
                    
                    ParameterLink? explicitLink = null;
                    bool hasExclusion = false;
                    
                    // Check links for this container
                    if (model.StateMachine.ParameterLinks != null)
                    {
                        foreach (var link in model.StateMachine.ParameterLinks)
                        {
                            // Check for destroyed link targets
                            if (link.TargetParameter == null || !link.TargetParameter) continue;
                            
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
                    }

                    if (explicitLink.HasValue && explicitLink.Value.IsValid)
                    {
                        // Valid explicit link (with Unity null check on source)
                        var sourceParam = explicitLink.Value.SourceParameter;
                        if (sourceParam != null && sourceParam)
                        {
                            info.ResolvedDependencies.Add(new ResolvedDependency
                            {
                                SourceParameter = sourceParam,
                                TargetParameter = req.Parameter,
                                IsExplicitLink = true
                            });
                        }
                        else
                        {
                            // Source was destroyed, treat as missing
                            info.MissingRequirements.Add(req);
                        }
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
                        if (compatibleParam != null && compatibleParam)
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
