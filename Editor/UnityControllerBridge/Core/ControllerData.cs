using System.Collections.Generic;

namespace DMotion.Editor.UnityControllerBridge.Core
{
    /// <summary>
    /// Unity-agnostic representation of an AnimatorController.
    /// Pure data structure that can be tested without Unity.
    /// </summary>
    public class ControllerData
    {
        public string Name { get; set; }
        public List<ParameterData> Parameters { get; set; } = new();
        public List<LayerData> Layers { get; set; } = new();
    }

    /// <summary>
    /// Represents a controller parameter.
    /// </summary>
    public class ParameterData
    {
        public string Name { get; set; }
        public ParameterType Type { get; set; }
        public float DefaultFloat { get; set; }
        public int DefaultInt { get; set; }
        public bool DefaultBool { get; set; }
    }

    public enum ParameterType
    {
        Float,
        Int,
        Bool,
        Trigger
    }

    /// <summary>
    /// Represents an animator layer.
    /// </summary>
    public class LayerData
    {
        public string Name { get; set; }
        public StateMachineData StateMachine { get; set; }
        public float DefaultWeight { get; set; } = 1f;
    }

    /// <summary>
    /// Represents a state machine.
    /// </summary>
    public class StateMachineData
    {
        public List<StateData> States { get; set; } = new();
        public string DefaultStateName { get; set; }

        /// <summary>
        /// Global transitions that can be taken from any state.
        /// Native DMotion support - no expansion needed.
        /// </summary>
        public List<TransitionData> AnyStateTransitions { get; set; } = new();
    }

    /// <summary>
    /// Represents a sub-state machine (state containing a nested state machine).
    /// </summary>
    public class SubStateMachineData
    {
        public StateMachineData NestedStateMachine { get; set; }
        public string EntryStateName { get; set; }
        public List<TransitionData> ExitTransitions { get; set; } = new();
    }

    /// <summary>
    /// Represents an animation state (or sub-state machine).
    /// Can be either a regular state with Motion, or a sub-state machine.
    /// </summary>
    public class StateData
    {
        public string Name { get; set; }

        // For regular animation states
        public MotionData Motion { get; set; }
        public float Speed { get; set; } = 1f;
        public bool SpeedParameterActive { get; set; }
        public string SpeedParameter { get; set; }
        public float CycleOffset { get; set; }

        // For sub-state machines
        public SubStateMachineData SubStateMachine { get; set; }

        // Common properties
        public List<TransitionData> Transitions { get; set; } = new();
        public UnityEngine.Vector2 GraphPosition { get; set; }

        /// <summary>
        /// True if this is a sub-state machine rather than a regular animation state.
        /// </summary>
        public bool IsSubStateMachine => SubStateMachine != null;
    }

    /// <summary>
    /// Base class for motion data (clips or blend trees).
    /// </summary>
    public abstract class MotionData
    {
        public string Name { get; set; }
        public abstract MotionType Type { get; }
    }

    public enum MotionType
    {
        Clip,
        BlendTree1D,
        BlendTree2D,
        Direct
    }

    /// <summary>
    /// Represents a single animation clip.
    /// </summary>
    public class ClipMotionData : MotionData
    {
        public override MotionType Type => MotionType.Clip;
        public UnityEngine.AnimationClip Clip { get; set; }
    }

    /// <summary>
    /// Represents a 1D blend tree.
    /// </summary>
    public class BlendTree1DData : MotionData
    {
        public override MotionType Type => MotionType.BlendTree1D;
        public string BlendParameter { get; set; }
        public List<BlendTreeChildData> Children { get; set; } = new();
    }

    /// <summary>
    /// Represents a child in a blend tree.
    /// </summary>
    public class BlendTreeChildData
    {
        public MotionData Motion { get; set; }
        public float Threshold { get; set; }
        public float TimeScale { get; set; } = 1f;
    }

    /// <summary>
    /// Represents a state transition.
    /// </summary>
    public class TransitionData
    {
        public string DestinationStateName { get; set; }
        public float Duration { get; set; }
        public float Offset { get; set; }
        public bool HasExitTime { get; set; }
        public float ExitTime { get; set; }
        public bool HasFixedDuration { get; set; } = true;
        public List<ConditionData> Conditions { get; set; } = new();
    }

    /// <summary>
    /// Represents a transition condition.
    /// </summary>
    public class ConditionData
    {
        public string ParameterName { get; set; }
        public ConditionMode Mode { get; set; }
        public float Threshold { get; set; }
    }

    public enum ConditionMode
    {
        If = 1,
        IfNot = 2,
        Greater = 3,
        Less = 4,
        Equals = 6,
        NotEqual = 7
    }
}
