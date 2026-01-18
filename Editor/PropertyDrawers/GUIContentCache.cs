using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Caches GUIContent instances to avoid per-frame allocations.
    /// All static labels used in editor GUI should be defined here.
    /// </summary>
    internal static class GUIContentCache
    {
        // Common buttons
        public static readonly GUIContent AddButton = new GUIContent("+ Add");
        public static readonly GUIContent DeleteButton = new GUIContent("X");
        public static readonly GUIContent PlusButton = new GUIContent("+");
        public static readonly GUIContent MinusButton = new GUIContent("-");
        public static readonly GUIContent ResolveButton = new GUIContent("Resolve");
        public static readonly GUIContent ResolveAllButton = new GUIContent("Resolve All");
        public static readonly GUIContent CleanUpButton = new GUIContent("Clean Up");
        public static readonly GUIContent CancelButton = new GUIContent("Cancel");
        public static readonly GUIContent SelectButton = new GUIContent("Select");

        // Section headers
        public static readonly GUIContent Parameters = new GUIContent("Parameters");
        public static readonly GUIContent Dependencies = new GUIContent("Dependencies");
        public static readonly GUIContent StateInspector = new GUIContent("State Inspector");
        public static readonly GUIContent Transitions = new GUIContent("Transitions");

        // Property labels
        public static readonly GUIContent Duration = new GUIContent("Duration (s)");
        public static readonly GUIContent HasExitTime = new GUIContent("Has Exit Time");
        public static readonly GUIContent ExitTime = new GUIContent("Exit Time (s)");
        public static readonly GUIContent BlendDuration = new GUIContent("Blend Duration (s)");
        public static readonly GUIContent Conditions = new GUIContent("Conditions");
        public static readonly GUIContent BlendParameter = new GUIContent("Blend Parameter");
        public static readonly GUIContent Min = new GUIContent("Min");
        public static readonly GUIContent Max = new GUIContent("Max");
        public static readonly GUIContent Name = new GUIContent("Name");
        public static readonly GUIContent NestedMachine = new GUIContent("Nested Machine");
        public static readonly GUIContent NestedStateMachine = new GUIContent("Nested State Machine");
        public static readonly GUIContent EntryState = new GUIContent("Entry State");

        // Status messages
        public static readonly GUIContent NoParameters = new GUIContent("No parameters defined. Add parameters to control transitions and blend trees.");
        public static readonly GUIContent NoBoolIntParameters = new GUIContent("No Bool/Int parameters defined");
        public static readonly GUIContent SelectOrCreate = new GUIContent("(Select or Create)");
        public static readonly GUIContent NoMatchesFound = new GUIContent("No matches found.");

        // Arrows and symbols
        public static readonly GUIContent Arrow = new GUIContent("->");

        // Reusable temporary content - use Temp() methods to avoid allocations
        // Thread-local would be safer but Unity editor is single-threaded
        private static readonly GUIContent _temp1 = new GUIContent();
        private static readonly GUIContent _temp2 = new GUIContent();
        private static readonly GUIContent _temp3 = new GUIContent();
        private static int _tempIndex;

        /// <summary>
        /// Gets a temporary GUIContent with the specified text.
        /// Cycles through 3 instances to allow multiple temps in same call.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent Temp(string text)
        {
            var temp = GetNextTemp();
            temp.text = text;
            temp.tooltip = null;
            temp.image = null;
            return temp;
        }

        /// <summary>
        /// Gets a temporary GUIContent with the specified text and tooltip.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent Temp(string text, string tooltip)
        {
            var temp = GetNextTemp();
            temp.text = text;
            temp.tooltip = tooltip;
            temp.image = null;
            return temp;
        }

        /// <summary>
        /// Gets a temporary GUIContent with the specified text and image.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent Temp(string text, Texture image)
        {
            var temp = GetNextTemp();
            temp.text = text;
            temp.tooltip = null;
            temp.image = image;
            return temp;
        }

        /// <summary>
        /// Gets a temporary GUIContent with all properties set.
        /// WARNING: Do not store the returned reference - it will be reused.
        /// </summary>
        public static GUIContent Temp(string text, string tooltip, Texture image)
        {
            var temp = GetNextTemp();
            temp.text = text;
            temp.tooltip = tooltip;
            temp.image = image;
            return temp;
        }

        private static GUIContent GetNextTemp()
        {
            _tempIndex = (_tempIndex + 1) % 3;
            return _tempIndex switch
            {
                0 => _temp1,
                1 => _temp2,
                _ => _temp3
            };
        }

        /// <summary>
        /// Resets temp index - call at start of OnGUI if using many temps.
        /// </summary>
        public static void ResetTemp()
        {
            _tempIndex = 0;
        }
    }
}
