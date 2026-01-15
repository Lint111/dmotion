using System.Collections.Generic;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge.Core
{
    /// <summary>
    /// Result of a controller conversion.
    /// Contains all converted data needed to create DMotion assets.
    /// </summary>
    public class ConversionResult
    {
        public bool Success { get; set; }
        public string ControllerName { get; set; }
        public List<ConvertedParameter> Parameters { get; set; } = new();
        public List<ConvertedState> States { get; set; } = new();
        public string DefaultStateName { get; set; }

        /// <summary>
        /// Global transitions that can be taken from any state.
        /// Native DMotion support - no expansion needed.
        /// </summary>
        public List<ConvertedTransition> AnyStateTransitions { get; set; } = new();
    }

    /// <summary>
    /// Options for conversion.
    /// </summary>
    public class ConversionOptions
    {
        public bool IncludeAnimationEvents { get; set; } = true;
        public bool PreserveGraphLayout { get; set; } = true;
        public bool LogWarnings { get; set; } = true;
        public bool VerboseLogging { get; set; } = false;
    }

    /// <summary>
    /// Log of conversion messages (errors, warnings, info).
    /// </summary>
    public class ConversionLog
    {
        private readonly List<ConversionMessage> _messages = new();

        public IReadOnlyList<ConversionMessage> Messages => _messages;

        public void AddError(string message)
        {
            _messages.Add(new ConversionMessage { Level = MessageLevel.Error, Text = message });
        }

        public void AddWarning(string message)
        {
            _messages.Add(new ConversionMessage { Level = MessageLevel.Warning, Text = message });
        }

        public void AddInfo(string message)
        {
            _messages.Add(new ConversionMessage { Level = MessageLevel.Info, Text = message });
        }

        public int ErrorCount => _messages.FindAll(m => m.Level == MessageLevel.Error).Count;
        public int WarningCount => _messages.FindAll(m => m.Level == MessageLevel.Warning).Count;

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Conversion Log ({_messages.Count} messages):");
            foreach (var msg in _messages)
            {
                sb.AppendLine($"[{msg.Level}] {msg.Text}");
            }
            return sb.ToString();
        }
    }

    public class ConversionMessage
    {
        public MessageLevel Level { get; set; }
        public string Text { get; set; }
    }

    public enum MessageLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Converted parameter data.
    /// </summary>
    public class ConvertedParameter
    {
        public string Name { get; set; }
        public ParameterType OriginalType { get; set; }
        public DMotionParameterType TargetType { get; set; }
        public float DefaultFloatValue { get; set; }
        public int DefaultIntValue { get; set; }
        public bool DefaultBoolValue { get; set; }
    }

    public enum DMotionParameterType
    {
        Float,
        Int,
        Bool
    }

    /// <summary>
    /// Converted state data.
    /// </summary>
    public class ConvertedState
    {
        public string Name { get; set; }
        public ConvertedStateType StateType { get; set; }
        public float Speed { get; set; } = 1f;
        public bool Loop { get; set; } = true;
        public Vector2 GraphPosition { get; set; }

        // Speed parameter (optional runtime multiplier)
        public string SpeedParameterName { get; set; }

        // Single clip data
        public string ClipName { get; set; }
        public AnimationClip Clip { get; set; }
        public List<ConvertedAnimationEvent> AnimationEvents { get; set; } = new();

        // Blend tree data
        public string BlendParameterName { get; set; }
        public List<ConvertedBlendClip> BlendClips { get; set; } = new();

        // Sub-state machine data
        public ConversionResult NestedStateMachine { get; set; }
        public string EntryStateName { get; set; }
        public List<ConvertedTransition> ExitTransitions { get; set; } = new();

        // Transitions
        public List<ConvertedTransition> Transitions { get; set; } = new();
    }

    public enum ConvertedStateType
    {
        SingleClip,
        LinearBlend,
        SubStateMachine
    }

    /// <summary>
    /// Converted blend clip data.
    /// </summary>
    public class ConvertedBlendClip
    {
        public string ClipName { get; set; }
        public AnimationClip Clip { get; set; }
        public float Threshold { get; set; }
        public float Speed { get; set; } = 1f;
    }

    /// <summary>
    /// Converted animation event data.
    /// </summary>
    public class ConvertedAnimationEvent
    {
        public string FunctionName { get; set; }
        public float NormalizedTime { get; set; }
    }

    /// <summary>
    /// Converted transition data.
    /// </summary>
    public class ConvertedTransition
    {
        public string DestinationStateName { get; set; }
        public float Duration { get; set; }
        public bool HasEndTime { get; set; }
        public float NormalizedExitTime { get; set; }
        public List<ConvertedCondition> Conditions { get; set; } = new();
    }

    /// <summary>
    /// Converted condition data.
    /// </summary>
    public class ConvertedCondition
    {
        public string ParameterName { get; set; }
        public ConvertedConditionType ConditionType { get; set; }

        // Bool condition
        public bool BoolValue { get; set; }

        // Int condition
        public ConvertedIntConditionMode IntMode { get; set; }
        public int IntValue { get; set; }
    }

    public enum ConvertedConditionType
    {
        Bool,
        Int
    }

    public enum ConvertedIntConditionMode
    {
        Equal,
        NotEqual,
        Greater,
        Less,
        GreaterOrEqual,
        LessOrEqual
    }
}
