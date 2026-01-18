using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    public static class EditorLayoutUtils
    {
        // Cached arrays to avoid allocations - supports up to 5 rects
        private static readonly Rect[] _cachedRects2 = new Rect[2];
        private static readonly Rect[] _cachedRects3 = new Rect[3];
        private static readonly Rect[] _cachedRects4 = new Rect[4];
        private static readonly Rect[] _cachedRects5 = new Rect[5];

        /// <summary>
        /// Lays out rects horizontally with the given width ratios.
        /// Returns a cached array - do not store the reference.
        /// Prefer using RectPool.HorizontalLayout2/3/4 for explicit sizing.
        /// </summary>
        public static Rect[] HorizontalLayout(this Rect r, params float[] widths)
        {
            if (widths.Length == 0)
            {
                return System.Array.Empty<Rect>();
            }

            // Get appropriate cached array
            var result = widths.Length switch
            {
                2 => _cachedRects2,
                3 => _cachedRects3,
                4 => _cachedRects4,
                5 => _cachedRects5,
                _ => new Rect[widths.Length] // Fallback for unusual sizes
            };

            // Calculate sum without LINQ (avoids boxing/allocation)
            float sumWidths = 0f;
            for (int i = 0; i < widths.Length; i++)
            {
                sumWidths += widths[i];
            }

            // Normalize and layout
            float invSum = 1f / sumWidths;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            float x = r.x;

            for (int i = 0; i < widths.Length; i++)
            {
                float normalizedWidth = widths[i] * invSum;
                float w = r.width * normalizedWidth;

                if (i > 0)
                {
                    x += spacing;
                    w -= spacing;
                }

                result[i] = new Rect(x, r.y, w, r.height);
                x += r.width * normalizedWidth;
            }

            return result;
        }

        /// <summary>
        /// Optimized 2-rect horizontal layout. Returns cached array.
        /// </summary>
        public static Rect[] HorizontalLayout2(this Rect r, float w0, float w1)
        {
            float sum = w0 + w1;
            float invSum = 1f / sum;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            float nw0 = w0 * invSum;
            float nw1 = w1 * invSum;

            _cachedRects2[0] = new Rect(r.x, r.y, r.width * nw0, r.height);
            _cachedRects2[1] = new Rect(r.x + r.width * nw0 + spacing, r.y, r.width * nw1 - spacing, r.height);

            return _cachedRects2;
        }

        /// <summary>
        /// Optimized 3-rect horizontal layout. Returns cached array.
        /// </summary>
        public static Rect[] HorizontalLayout3(this Rect r, float w0, float w1, float w2)
        {
            float sum = w0 + w1 + w2;
            float invSum = 1f / sum;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            float nw0 = w0 * invSum;
            float nw1 = w1 * invSum;
            float nw2 = w2 * invSum;

            float x = r.x;
            _cachedRects3[0] = new Rect(x, r.y, r.width * nw0, r.height);
            
            x += r.width * nw0 + spacing;
            _cachedRects3[1] = new Rect(x, r.y, r.width * nw1 - spacing, r.height);
            
            x += r.width * nw1;
            _cachedRects3[2] = new Rect(x, r.y, r.width * nw2 - spacing, r.height);

            return _cachedRects3;
        }
    }
}