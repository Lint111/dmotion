using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides pooled Rect arrays for layout calculations to avoid per-frame allocations.
    /// Uses fixed-size arrays that are reused across frames.
    /// </summary>
    internal static class RectPool
    {
        // Pre-allocated arrays for common sizes
        private static readonly Rect[] _pool2 = new Rect[2];
        private static readonly Rect[] _pool3 = new Rect[3];
        private static readonly Rect[] _pool4 = new Rect[4];
        private static readonly Rect[] _pool5 = new Rect[5];

        // Scratch arrays for width calculations
        private static readonly float[] _widths2 = new float[2];
        private static readonly float[] _widths3 = new float[3];
        private static readonly float[] _widths4 = new float[4];
        private static readonly float[] _widths5 = new float[5];

        /// <summary>
        /// Gets a pooled array of 2 Rects laid out horizontally.
        /// WARNING: Do not store the returned array - it will be reused.
        /// </summary>
        public static Rect[] HorizontalLayout2(Rect r, float w0, float w1)
        {
            _widths2[0] = w0;
            _widths2[1] = w1;
            FillHorizontalLayout(r, _widths2, _pool2);
            return _pool2;
        }

        /// <summary>
        /// Gets a pooled array of 3 Rects laid out horizontally.
        /// WARNING: Do not store the returned array - it will be reused.
        /// </summary>
        public static Rect[] HorizontalLayout3(Rect r, float w0, float w1, float w2)
        {
            _widths3[0] = w0;
            _widths3[1] = w1;
            _widths3[2] = w2;
            FillHorizontalLayout(r, _widths3, _pool3);
            return _pool3;
        }

        /// <summary>
        /// Gets a pooled array of 4 Rects laid out horizontally.
        /// WARNING: Do not store the returned array - it will be reused.
        /// </summary>
        public static Rect[] HorizontalLayout4(Rect r, float w0, float w1, float w2, float w3)
        {
            _widths4[0] = w0;
            _widths4[1] = w1;
            _widths4[2] = w2;
            _widths4[3] = w3;
            FillHorizontalLayout(r, _widths4, _pool4);
            return _pool4;
        }

        /// <summary>
        /// Gets a pooled array of 5 Rects laid out horizontally.
        /// WARNING: Do not store the returned array - it will be reused.
        /// </summary>
        public static Rect[] HorizontalLayout5(Rect r, float w0, float w1, float w2, float w3, float w4)
        {
            _widths5[0] = w0;
            _widths5[1] = w1;
            _widths5[2] = w2;
            _widths5[3] = w3;
            _widths5[4] = w4;
            FillHorizontalLayout(r, _widths5, _pool5);
            return _pool5;
        }

        /// <summary>
        /// Fills the output array with horizontal layout rects.
        /// Widths are normalized (can be ratios like 0.3f, 0.7f or pixel values).
        /// </summary>
        public static void FillHorizontalLayout(Rect r, float[] widths, Rect[] output)
        {
            if (widths.Length == 0 || output.Length == 0)
                return;

            // Calculate sum for normalization
            float sumWidths = 0f;
            for (int i = 0; i < widths.Length; i++)
            {
                sumWidths += widths[i];
            }

            // Normalize widths inline
            float invSum = 1f / sumWidths;

            var spacing = EditorGUIUtility.standardVerticalSpacing;
            float x = r.x;

            for (int i = 0; i < widths.Length && i < output.Length; i++)
            {
                float normalizedWidth = widths[i] * invSum;
                float w = r.width * normalizedWidth;

                if (i > 0)
                {
                    x += spacing;
                    w -= spacing;
                }

                output[i] = new Rect(x, r.y, w, r.height);
                x += r.width * normalizedWidth;
            }
        }

        /// <summary>
        /// Splits a rect horizontally into two parts at the given ratio (0-1).
        /// Returns left rect, and sets rightRect to the right portion.
        /// </summary>
        public static Rect SplitHorizontal(Rect r, float leftRatio, out Rect rightRect)
        {
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            float leftWidth = r.width * leftRatio;
            float rightWidth = r.width * (1f - leftRatio) - spacing;

            rightRect = new Rect(r.x + leftWidth + spacing, r.y, rightWidth, r.height);
            return new Rect(r.x, r.y, leftWidth, r.height);
        }

        /// <summary>
        /// Splits a rect into label and field portions using EditorGUIUtility.labelWidth.
        /// </summary>
        public static Rect SplitLabelField(Rect r, out Rect fieldRect)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            fieldRect = new Rect(r.x + labelWidth + spacing, r.y, r.width - labelWidth - spacing, r.height);
            return new Rect(r.x, r.y, labelWidth, r.height);
        }
    }
}
