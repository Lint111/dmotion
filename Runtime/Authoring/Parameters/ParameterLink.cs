using System;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Optional transformation applied when linking parameters.
    /// Allows value scaling/offset between linked parameters.
    /// </summary>
    [Serializable]
    public struct ParameterTransform
    {
        /// <summary>Whether any transformation is applied</summary>
        public bool HasTransform;

        /// <summary>Multiplier applied to the source value</summary>
        public float Scale;

        /// <summary>Offset added after scaling</summary>
        public float Offset;

        public static ParameterTransform Identity => new ParameterTransform
        {
            HasTransform = false,
            Scale = 1f,
            Offset = 0f
        };

        public static ParameterTransform Create(float scale, float offset = 0f)
        {
            return new ParameterTransform
            {
                HasTransform = true,
                Scale = scale,
                Offset = offset
            };
        }

        /// <summary>Apply the transform to a value</summary>
        public float Apply(float value)
        {
            return HasTransform ? (value * Scale) + Offset : value;
        }

        /// <summary>Apply the transform to an int value (rounds result)</summary>
        public int Apply(int value)
        {
            return HasTransform ? Mathf.RoundToInt((value * Scale) + Offset) : value;
        }
    }

    /// <summary>
    /// Links a source parameter in the parent scope to a target parameter in a child SubStateMachine.
    /// This allows reusing parameters across hierarchy levels without duplication.
    /// 
    /// Example: Parent has "MovementSpeed", child SubStateMachine needs "Speed"
    /// A ParameterLink maps MovementSpeed -> Speed so they share the same runtime value.
    /// </summary>
    [Serializable]
    public struct ParameterLink
    {
        /// <summary>
        /// The source parameter in the parent/root state machine.
        /// This is the "real" parameter that holds the runtime value.
        /// </summary>
        public AnimationParameterAsset SourceParameter;

        /// <summary>
        /// The target parameter in the child SubStateMachine.
        /// At runtime, this will resolve to the source parameter's index.
        /// </summary>
        public AnimationParameterAsset TargetParameter;

        /// <summary>
        /// The SubStateMachine where this link applies.
        /// </summary>
        public SubStateMachineStateAsset SubMachine;

        /// <summary>
        /// Optional value transformation (scale, offset).
        /// Useful when parameters have different ranges.
        /// </summary>
        public ParameterTransform Transform;

        public ParameterLink(
            AnimationParameterAsset source,
            AnimationParameterAsset target,
            SubStateMachineStateAsset subMachine,
            ParameterTransform transform = default)
        {
            SourceParameter = source;
            TargetParameter = target;
            SubMachine = subMachine;
            Transform = transform.HasTransform ? transform : ParameterTransform.Identity;
        }

        /// <summary>
        /// Creates a direct 1:1 link without transformation.
        /// </summary>
        public static ParameterLink Direct(
            AnimationParameterAsset source,
            AnimationParameterAsset target,
            SubStateMachineStateAsset subMachine)
        {
            return new ParameterLink(source, target, subMachine, ParameterTransform.Identity);
        }

        /// <summary>
        /// True if this is a valid link (has source, target, and submachine).
        /// </summary>
        public bool IsValid =>
            SourceParameter != null &&
            TargetParameter != null &&
            SubMachine != null;

        /// <summary>
        /// True if this is an exclusion marker (null source means "don't auto-match this target").
        /// </summary>
        public bool IsExclusion =>
            SourceParameter == null &&
            TargetParameter != null &&
            SubMachine != null;

        /// <summary>
        /// Creates an exclusion marker that prevents auto-matching for a target parameter.
        /// </summary>
        public static ParameterLink Exclusion(
            AnimationParameterAsset target,
            SubStateMachineStateAsset subMachine)
        {
            return new ParameterLink(null, target, subMachine, ParameterTransform.Identity);
        }

        public override string ToString()
        {
            var sourceName = SourceParameter != null ? SourceParameter.name : "null";
            var targetName = TargetParameter != null ? TargetParameter.name : "null";
            var subMachineName = SubMachine != null ? SubMachine.name : "null";
            var transformStr = Transform.HasTransform ? $" (x{Transform.Scale}+{Transform.Offset})" : "";
            return $"{sourceName} -> {targetName} in {subMachineName}{transformStr}";
        }
    }

    /// <summary>
    /// Result of parameter requirement analysis for a SubStateMachine.
    /// </summary>
    [Serializable]
    public struct ParameterRequirement
    {
        /// <summary>The parameter that is required</summary>
        public AnimationParameterAsset Parameter;

        /// <summary>How the parameter is used</summary>
        public ParameterUsageType UsageType;

        /// <summary>Context description for debugging/display</summary>
        public string UsageContext;

        public ParameterRequirement(
            AnimationParameterAsset parameter,
            ParameterUsageType usageType,
            string context)
        {
            Parameter = parameter;
            UsageType = usageType;
            UsageContext = context ?? string.Empty;
        }
    }
}
