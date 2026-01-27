using System.Collections.Generic;
using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Analyzes parameter requirements for nested state machine containers (SubStateMachines and Layers)
    /// and provides auto-resolution and dependency tracking utilities.
    /// </summary>
    public static class ParameterDependencyAnalyzer
    {
        /// <summary>
        /// Analyzes a nested container and returns all parameters it requires.
        /// Recursively analyzes nested SubStateMachines.
        /// </summary>
        public static List<ParameterRequirement> AnalyzeRequiredParameters(INestedStateMachineContainer container)
        {
            var requirements = new List<ParameterRequirement>();

            if (container?.NestedStateMachine == null)
                return requirements;

            AnalyzeStateMachineRecursive(container.NestedStateMachine, requirements);

            // Deduplicate by parameter (keep first occurrence), filter out null/destroyed
            var seen = new HashSet<AnimationParameterAsset>();
            var deduplicated = new List<ParameterRequirement>();
            for (int i = 0; i < requirements.Count; i++)
            {
                var req = requirements[i];
                if (req.Parameter == null) continue; // Skip destroyed parameters
                if (seen.Add(req.Parameter))
                {
                    deduplicated.Add(req);
                }
            }
            return deduplicated;
        }
        
        /// <summary>
        /// Analyzes a SubStateMachine and returns all parameters it requires (backwards compatibility).
        /// </summary>
        public static List<ParameterRequirement> AnalyzeRequiredParameters(SubStateMachineStateAsset subMachine)
        {
            return AnalyzeRequiredParameters((INestedStateMachineContainer)subMachine);
        }

        private static void AnalyzeStateMachineRecursive(
            StateMachineAsset machine,
            List<ParameterRequirement> requirements)
        {
            // Use explicit Unity null check for destroyed objects
            if (machine == null || !machine) return;
            if (machine.States == null) return;

            // Analyze each state
            foreach (var state in machine.States)
            {
                // Use explicit Unity null check for destroyed objects
                if (state == null || !state) continue;
                
                // Speed parameter (with Unity null check)
                if (state.SpeedParameter != null && state.SpeedParameter)
                {
                    requirements.Add(new ParameterRequirement(
                        state.SpeedParameter,
                        ParameterUsageType.SpeedParameter,
                        state.name));
                }

                // Blend parameter (for LinearBlendState)
                if (state is LinearBlendStateAsset blendState && blendState.BlendParameter != null && blendState.BlendParameter)
                {
                    requirements.Add(new ParameterRequirement(
                        blendState.BlendParameter,
                        ParameterUsageType.BlendParameter,
                        state.name));
                }

                // Transition conditions
                if (state.OutTransitions != null)
                {
                    foreach (var transition in state.OutTransitions)
                    {
                        if (transition == null) continue;
                        AnalyzeTransitionConditions(transition, state.name, requirements);
                    }
                }

                // Recurse into nested SubStateMachines
                if (state is SubStateMachineStateAsset nestedSubMachine && nestedSubMachine.NestedStateMachine != null && nestedSubMachine.NestedStateMachine)
                {
                    AnalyzeStateMachineRecursive(nestedSubMachine.NestedStateMachine, requirements);
                }
            }

            // Any State transition conditions
            if (machine.AnyStateTransitions != null)
            {
                foreach (var anyTransition in machine.AnyStateTransitions)
                {
                    if (anyTransition == null) continue;
                    var toStateName = (anyTransition.ToState != null && anyTransition.ToState) ? anyTransition.ToState.name : "?";
                    var sb = StringBuilderCache.Get();
                    sb.Append("Any -> ").Append(toStateName);
                    AnalyzeTransitionConditions(anyTransition, sb.ToString(), requirements, isAnyState: true);
                }
            }
        }

        private static void AnalyzeTransitionConditions(
            StateOutTransition transition,
            string context,
            List<ParameterRequirement> requirements,
            bool isAnyState = false)
        {
            if (transition.Conditions == null) return;

            foreach (var condition in transition.Conditions)
            {
                if (condition.Parameter != null)
                {
                    requirements.Add(new ParameterRequirement(
                        condition.Parameter,
                        isAnyState ? ParameterUsageType.AnyStateCondition : ParameterUsageType.TransitionCondition,
                        context));
                }
            }
        }

        /// <summary>
        /// Finds a compatible parameter in the parent machine that can satisfy a requirement.
        /// Matches by name (case-insensitive) and type.
        /// </summary>
        public static AnimationParameterAsset FindCompatibleParameter(
            StateMachineAsset parentMachine,
            AnimationParameterAsset requiredParam)
        {
            if (parentMachine == null || requiredParam == null)
                return null;

            var requiredType = requiredParam.GetType();
            var requiredName = requiredParam.name.ToLowerInvariant();

            // First try exact name match with same type
            foreach (var param in parentMachine.Parameters)
            {
                if (param.GetType() == requiredType &&
                    param.name.ToLowerInvariant() == requiredName)
                {
                    return param;
                }
            }

            // Try similar names (e.g., "Speed" matches "MovementSpeed", "WalkSpeed", etc.)
            foreach (var param in parentMachine.Parameters)
            {
                if (param.GetType() == requiredType)
                {
                    var paramName = param.name.ToLowerInvariant();
                    if (paramName.Contains(requiredName) || requiredName.Contains(paramName))
                    {
                        return param;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Auto-resolves parameter dependencies when a nested container is added.
        /// Creates missing parameters and establishes links.
        /// Skips requirements that already have a link established.
        /// </summary>
        public static ParameterResolutionResult ResolveParameterDependencies(
            StateMachineAsset parentMachine,
            INestedStateMachineContainer container)
        {
            var result = new ParameterResolutionResult();
            var requirements = AnalyzeRequiredParameters(container);

            // Build set of already-linked target parameters for this container
            var linkedTargets = new HashSet<AnimationParameterAsset>();
            var parentLinks = parentMachine.ParameterLinks;
            for (int i = 0; i < parentLinks.Count; i++)
            {
                var link = parentLinks[i];
                if (link.NestedContainer == container && link.TargetParameter != null)
                {
                    linkedTargets.Add(link.TargetParameter);
                }
            }

            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                // Skip if already linked
                if (linkedTargets.Contains(requirement.Parameter))
                    continue;

                var existingParam = FindCompatibleParameter(parentMachine, requirement.Parameter);

                if (existingParam != null)
                {
                    // Create link to existing parameter
                    var link = ParameterLink.Direct(existingParam, requirement.Parameter, container);
                    result.ParameterLinks.Add(link);

                    // Track dependency (only for SubStateMachines for backwards compatibility)
                    if (container is SubStateMachineStateAsset subMachine)
                    {
                        existingParam.AddRequiredBy(subMachine);
                    }
                }
                else
                {
                    // Need to create a new parameter
                    result.MissingParameters.Add(requirement);
                }
            }

            return result;
        }

        /// <summary>
        /// Creates auto-generated parameters for missing requirements.
        /// Note: Caller should call Undo.RecordObject on parentMachine before calling this.
        /// </summary>
        public static List<AnimationParameterAsset> CreateMissingParameters(
            StateMachineAsset parentMachine,
            INestedStateMachineContainer container,
            List<ParameterRequirement> missingRequirements,
            string assetPath)
        {
            var created = new List<AnimationParameterAsset>();

            foreach (var requirement in missingRequirements)
            {
                var newParam = CreateParameterCopy(requirement.Parameter);
                if (newParam == null) continue;

                newParam.name = requirement.Parameter.name;
                newParam.IsAutoGenerated = true;
                
                // Track dependency (only for SubStateMachines for backwards compatibility)
                if (container is SubStateMachineStateAsset subMachine)
                {
                    newParam.AddRequiredBy(subMachine);
                }

                // Add to parent machine
                parentMachine.Parameters.Add(newParam);

                // Save as sub-asset
                AssetDatabase.AddObjectToAsset(newParam, assetPath);
                
                // Register created object for undo
                Undo.RegisterCreatedObjectUndo(newParam, "Create Parameter");

                created.Add(newParam);
            }

            if (created.Count > 0)
            {
                EditorUtility.SetDirty(parentMachine);
                AssetDatabase.SaveAssets();
            }

            return created;
        }

        private static AnimationParameterAsset CreateParameterCopy(AnimationParameterAsset source)
        {
            if (source is BoolParameterAsset)
                return ScriptableObject.CreateInstance<BoolParameterAsset>();
            if (source is IntParameterAsset)
                return ScriptableObject.CreateInstance<IntParameterAsset>();
            if (source is FloatParameterAsset)
                return ScriptableObject.CreateInstance<FloatParameterAsset>();
            if (source is EnumParameterAsset enumParam)
            {
                var newEnum = ScriptableObject.CreateInstance<EnumParameterAsset>();
                // Copy enum type if possible
                return newEnum;
            }

            return null;
        }

        /// <summary>
        /// Finds parameters that are no longer referenced by any states, transitions, or parameter links.
        /// Also considers parameters that implicitly satisfy SubMachine requirements (same name/type).
        /// </summary>
        public static List<AnimationParameterAsset> FindOrphanedParameters(StateMachineAsset machine)
        {
            // Use explicit Unity null check for destroyed objects
            if (machine == null || !machine) return new List<AnimationParameterAsset>();
            if (machine.States == null) return new List<AnimationParameterAsset>();

            var usedParameters = new HashSet<AnimationParameterAsset>();
            CollectUsedParametersRecursive(machine, usedParameters);

            // Also consider parameters used as source in explicit parameter links as "used"
            if (machine.ParameterLinks != null)
            {
                foreach (var link in machine.ParameterLinks)
                {
                    if (link.SourceParameter != null && link.SourceParameter)
                        usedParameters.Add(link.SourceParameter);
                }
            }

            // Also consider parameters that implicitly satisfy nested container requirements (same name/type)
            foreach (var state in machine.States)
            {
                // Use explicit Unity null check for destroyed objects
                if (state == null || !state) continue;
                
                INestedStateMachineContainer container = state switch
                {
                    SubStateMachineStateAsset subMachine => subMachine,
                    LayerStateAsset layer => layer,
                    _ => null
                };
                
                if (container != null)
                {
                    var requirements = AnalyzeRequiredParameters(container);
                    foreach (var req in requirements)
                    {
                        if (req.Parameter == null || !req.Parameter) continue;
                        var compatible = FindCompatibleParameter(machine, req.Parameter);
                        if (compatible != null && compatible)
                            usedParameters.Add(compatible);
                    }
                }
            }

            var orphaned = new List<AnimationParameterAsset>();
            var parameters = machine.Parameters;
            if (parameters == null) return orphaned;
            
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                // Use explicit Unity null check for destroyed parameters
                if (p == null || !p) continue;
                if (!usedParameters.Contains(p))
                {
                    orphaned.Add(p);
                }
            }
            return orphaned;
        }

        private static void CollectUsedParametersRecursive(
            StateMachineAsset machine,
            HashSet<AnimationParameterAsset> usedParameters)
        {
            // Use explicit Unity null check for destroyed objects
            if (machine == null || !machine) return;
            if (machine.States == null) return;

            foreach (var state in machine.States)
            {
                // Use explicit Unity null check for destroyed objects
                if (state == null || !state) continue;
                
                // Speed parameter (with Unity null check)
                if (state.SpeedParameter != null && state.SpeedParameter)
                    usedParameters.Add(state.SpeedParameter);

                // Blend parameter (with Unity null check)
                if (state is LinearBlendStateAsset blendState && blendState.BlendParameter != null && blendState.BlendParameter)
                    usedParameters.Add(blendState.BlendParameter);
                    
                // 2D Blend parameters (with Unity null check)
                if (state is Directional2DBlendStateAsset blend2D)
                {
                    if (blend2D.BlendParameterX != null && blend2D.BlendParameterX)
                        usedParameters.Add(blend2D.BlendParameterX);
                    if (blend2D.BlendParameterY != null && blend2D.BlendParameterY)
                        usedParameters.Add(blend2D.BlendParameterY);
                }

                // Transition conditions
                if (state.OutTransitions != null)
                {
                    foreach (var transition in state.OutTransitions)
                    {
                        if (transition == null || transition.Conditions == null) continue;
                        foreach (var condition in transition.Conditions)
                        {
                            if (condition.Parameter != null && condition.Parameter)
                                usedParameters.Add(condition.Parameter);
                        }
                    }
                }

                // Recurse into nested SubStateMachines (with Unity null check)
                if (state is SubStateMachineStateAsset subMachine && subMachine.NestedStateMachine != null && subMachine.NestedStateMachine)
                {
                    CollectUsedParametersRecursive(subMachine.NestedStateMachine, usedParameters);
                }
                
                // Recurse into LayerStateAssets (multi-layer support, with Unity null check)
                if (state is LayerStateAsset layer && layer.NestedStateMachine != null && layer.NestedStateMachine)
                {
                    CollectUsedParametersRecursive(layer.NestedStateMachine, usedParameters);
                }
            }

            // Any State transitions
            if (machine.AnyStateTransitions != null)
            {
                foreach (var anyTransition in machine.AnyStateTransitions)
                {
                    if (anyTransition?.Conditions == null) continue;
                    foreach (var condition in anyTransition.Conditions)
                    {
                        if (condition.Parameter != null && condition.Parameter)
                            usedParameters.Add(condition.Parameter);
                    }
                }
            }
            
            // Any State exit transition
            if (machine.AnyStateExitTransition?.Conditions != null)
            {
                foreach (var condition in machine.AnyStateExitTransition.Conditions)
                {
                    if (condition.Parameter != null && condition.Parameter)
                        usedParameters.Add(condition.Parameter);
                }
            }
        }

        /// <summary>
        /// Safely removes orphaned auto-generated parameters.
        /// Returns the list of removed parameters.
        /// Uses StateMachineEditorUtils.DeleteParameter for proper cleanup of all references.
        /// Note: Caller should call Undo.RecordObject on machine before calling this.
        /// </summary>
        public static List<AnimationParameterAsset> CleanupOrphanedParameters(
            StateMachineAsset machine,
            bool onlyAutoGenerated = true)
        {
            var orphaned = FindOrphanedParameters(machine);
            var removed = new List<AnimationParameterAsset>();

            foreach (var param in orphaned)
            {
                if (param == null) continue;
                if (onlyAutoGenerated && !param.IsAutoGenerated)
                    continue;

                removed.Add(param);
                
                // Use centralized deletion logic that cleans up all references
                machine.DeleteParameter(param, recursive: false);
            }

            if (removed.Count > 0)
            {
                EditorUtility.SetDirty(machine);
                AssetDatabase.SaveAssets();
            }

            return removed;
        }
    }

    /// <summary>
    /// Result of parameter dependency resolution.
    /// </summary>
    public class ParameterResolutionResult
    {
        /// <summary>Parameters that need to be created (not found in parent)</summary>
        public List<ParameterRequirement> MissingParameters { get; } = new();

        /// <summary>Links established between existing parameters</summary>
        public List<ParameterLink> ParameterLinks { get; } = new();

        /// <summary>Any warnings or notes about the resolution</summary>
        public List<string> Warnings { get; } = new();

        public bool HasMissingParameters => MissingParameters.Count > 0;
        public bool HasLinks => ParameterLinks.Count > 0;

        public override string ToString()
        {
            var sb = StringBuilderCache.Get();
            sb.Append("Resolution: ").Append(ParameterLinks.Count).Append(" links, ")
              .Append(MissingParameters.Count).Append(" missing");
            return sb.ToString();
        }
    }
}
