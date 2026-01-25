using System.Text;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides cached StringBuilder instances for string formatting without per-frame allocations.
    /// Note: The resulting strings still allocate, but this avoids intermediate allocations
    /// from string concatenation and interpolation.
    /// </summary>
    internal static class StringBuilderCache
    {
        // Thread-local would be safer, but Unity editor is single-threaded
        private static readonly StringBuilder _sb1 = new StringBuilder(64);
        private static readonly StringBuilder _sb2 = new StringBuilder(64);
        private static int _index;

        /// <summary>
        /// Gets a cleared StringBuilder ready for use.
        /// Alternates between two instances to allow nested usage.
        /// </summary>
        public static StringBuilder Get()
        {
            _index = (_index + 1) % 2;
            var sb = _index == 0 ? _sb1 : _sb2;
            sb.Clear();
            return sb;
        }

        /// <summary>
        /// Formats "{count} orphaned" pattern.
        /// </summary>
        public static string FormatOrphaned(int count)
        {
            var sb = Get();
            sb.Append(count).Append(" orphaned");
            return sb.ToString();
        }

        /// <summary>
        /// Formats "{count} orphaned ({autoGen} auto-gen)" pattern.
        /// </summary>
        public static string FormatOrphanedWithAutoGen(int count, int autoGen)
        {
            var sb = Get();
            sb.Append(count).Append(" orphaned (").Append(autoGen).Append(" auto-gen)");
            return sb.ToString();
        }

        /// <summary>
        /// Formats "{resolved}/{total}" pattern for dependency counts.
        /// </summary>
        public static string FormatRatio(int resolved, int total)
        {
            var sb = Get();
            sb.Append(resolved).Append('/').Append(total);
            return sb.ToString();
        }

        /// <summary>
        /// Formats "({count})" pattern for state counts.
        /// </summary>
        public static string FormatCount(int count)
        {
            var sb = Get();
            sb.Append('(').Append(count).Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Formats "({typeName})" pattern for parameter types.
        /// </summary>
        public static string FormatTypeName(string typeName)
        {
            var sb = Get();
            sb.Append('(').Append(typeName).Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Formats "-> {name}" pattern for transitions.
        /// </summary>
        public static string FormatTransitionTarget(string name)
        {
            var sb = Get();
            sb.Append("-> ").Append(name);
            return sb.ToString();
        }

        /// <summary>
        /// Formats "{name} ({typeName})" pattern.
        /// </summary>
        public static string FormatNameWithType(string name, string typeName)
        {
            var sb = Get();
            sb.Append(name).Append(" (").Append(typeName).Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Formats "{resolved}/{total} parameters resolved" pattern.
        /// </summary>
        public static string FormatParametersResolved(int resolved, int total)
        {
            var sb = Get();
            sb.Append(resolved).Append('/').Append(total).Append(" parameters resolved");
            return sb.ToString();
        }

        /// <summary>
        /// Formats "Missing ({count}):" pattern.
        /// </summary>
        public static string FormatMissingCount(int count)
        {
            var sb = Get();
            sb.Append("Missing (").Append(count).Append("):");
            return sb.ToString();
        }

        /// <summary>
        /// Formats "Resolved ({count}):" pattern.
        /// </summary>
        public static string FormatResolvedCount(int count)
        {
            var sb = Get();
            sb.Append("Resolved (").Append(count).Append("):");
            return sb.ToString();
        }

        /// <summary>
        /// Formats "Used by {count} SubMachine(s)" pattern.
        /// </summary>
        public static string FormatUsedByCount(int count)
        {
            var sb = Get();
            sb.Append("Used by ").Append(count).Append(" SubMachine(s)");
            return sb.ToString();
        }

        /// <summary>
        /// Formats "in {name}" pattern.
        /// </summary>
        public static string FormatInName(string name)
        {
            var sb = Get();
            sb.Append("in ").Append(name);
            return sb.ToString();
        }

        /// <summary>
        /// Formats "(x{scale}+{offset})" pattern for transforms.
        /// </summary>
        public static string FormatTransform(float scale, float offset)
        {
            var sb = Get();
            sb.Append("(x").Append(scale).Append('+').Append(offset).Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Formats "New State {number}" pattern.
        /// </summary>
        public static string FormatNewState(int number)
        {
            var sb = Get();
            sb.Append("New State ").Append(number);
            return sb.ToString();
        }

        /// <summary>
        /// Formats "New Parameter {number}" pattern.
        /// </summary>
        public static string FormatNewParameter(int number)
        {
            var sb = Get();
            sb.Append("New Parameter ").Append(number);
            return sb.ToString();
        }

        /// <summary>
        /// Formats "Int value {min} = 0.0, {max} = 1.0" pattern.
        /// </summary>
        public static string FormatIntRange(int min, int max)
        {
            var sb = Get();
            sb.Append("Int value ").Append(min).Append(" = 0.0, ").Append(max).Append(" = 1.0");
            return sb.ToString();
        }
    }
}
