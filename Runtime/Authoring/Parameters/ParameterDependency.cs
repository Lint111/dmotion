using System;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// How a parameter is used within the state machine.
    /// </summary>
    public enum ParameterUsageType
    {
        /// <summary>Used in a transition condition</summary>
        TransitionCondition,
        /// <summary>Used as a blend tree parameter</summary>
        BlendParameter,
        /// <summary>Used as a state speed multiplier</summary>
        SpeedParameter,
        /// <summary>Used in an Any State transition condition</summary>
        AnyStateCondition
    }

    /// <summary>
    /// Tracks a dependency relationship between a parameter and a SubStateMachine.
    /// When a SubStateMachine requires a parameter, this records why and where.
    /// </summary>
    [Serializable]
    public struct ParameterDependency
    {
        /// <summary>The parameter that is required</summary>
        public AnimationParameterAsset RequiredParameter;

        /// <summary>The SubStateMachine that requires this parameter</summary>
        public SubStateMachineStateAsset RequiringSubMachine;

        /// <summary>How this parameter is used</summary>
        public ParameterUsageType UsageType;

        /// <summary>
        /// Specific usage context for debugging/display.
        /// e.g., "IdleState -> WalkState" for transitions, "WalkBlend" for blend trees
        /// </summary>
        public string UsageContext;

        public ParameterDependency(
            AnimationParameterAsset parameter,
            SubStateMachineStateAsset subMachine,
            ParameterUsageType usageType,
            string context = null)
        {
            RequiredParameter = parameter;
            RequiringSubMachine = subMachine;
            UsageType = usageType;
            UsageContext = context ?? string.Empty;
        }

        public override string ToString()
        {
            var subMachineName = RequiringSubMachine != null ? RequiringSubMachine.name : "null";
            var paramName = RequiredParameter != null ? RequiredParameter.name : "null";
            return $"{paramName} <- {subMachineName} ({UsageType}: {UsageContext})";
        }
    }
}
