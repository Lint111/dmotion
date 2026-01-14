using UnityEngine;
using System;
using UnityEditor;

namespace DMotion.Editor
{
    public static class EditorGUIUtils
    {
        public static int GenericEnumPopup(Rect r, Type enumType, int current)
        {
            return GenericEnumPopup(r, enumType, current, GUIContent.none);

        }
        public static int GenericEnumPopup(Rect r, Type enumType, int current, GUIContent label)
        {
            if (enumType is { IsEnum: true })
            {
                var enumValue = (Enum) Enum.GetValues(enumType).GetValue(current);
                var enumObj =(object)EditorGUI.EnumPopup(r, label, enumValue);
                return Convert.ToInt32(enumObj);
            }
            else
            {
                EditorGUI.LabelField(r, "Invalid type");
                return -1;
            }
        }
    }
}