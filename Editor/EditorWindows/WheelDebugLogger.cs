using UnityEngine;
using UnityEngine;

namespace DMotion.Editor
{
    public static class WheelDebugLogger
    {
        private static int eventCounter = 0;

        public static void LogWheelEvent(string source, float delta, UnityEngine.Vector2 mousePos, UnityEngine.Rect bounds)
        {
            Debug.Log($"[WHEEL DEBUG #{++eventCounter}] {source}: delta={delta:F3}, mousePos=({mousePos.x:F1},{mousePos.y:F1}), bounds=({bounds.x:F1},{bounds.y:F1},{bounds.width:F1},{bounds.height:F1}), inBounds={bounds.Contains(mousePos)}");
        }
    }
}
