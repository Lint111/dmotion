using System;
using System.Linq;
using DMotion.Authoring;
using Unity.Entities;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Delegate for drawing a parameter field and returning the modified value.
    /// </summary>
    internal delegate TBuffer ParameterDrawer<TBuffer>(Rect position, GUIContent label, TBuffer param)
        where TBuffer : unmanaged;

    [CustomPropertyDrawer(typeof(AnimationParameterAsset))]
    internal class AnimationParameterPropertyDrawer : PropertyDrawer
    {
        private EnumTypePopupSelector enumTypePopupSelector;

        public AnimationParameterPropertyDrawer()
        {
            enumTypePopupSelector = new EnumTypePopupSelector();
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var parameterAsset = property.objectReferenceValue as AnimationParameterAsset;
            var stateMachineAsset = property.serializedObject.targetObject as StateMachineAsset;
            if (parameterAsset != null && stateMachineAsset != null)
            {
                if (IsAnimatorEntitySelected(stateMachineAsset))
                {
                    DrawParameterPlaymode(position, parameterAsset, stateMachineAsset);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        DrawPropertyEditorMode(position, parameterAsset, stateMachineAsset, property);
                    }
                }
            }
        }

        private static void DrawParameterPlaymode(Rect position, AnimationParameterAsset parameterAsset,
            StateMachineAsset stateMachineAsset)
        {
            if (!EntitySelectionProxyUtils.TryExtractEntitySelectionProxy(out var selectedEntity))
                return;

            var label = new GUIContent(parameterAsset.name);
            switch (parameterAsset)
            {
                case BoolParameterAsset boolAsset:
                    DrawParameter<BoolParameterAsset, BoolParameter>(
                        position, label, boolAsset, stateMachineAsset, selectedEntity,
                        (rect, lbl, param) =>
                        {
                            param.Value = EditorGUI.Toggle(rect, lbl, param.Value);
                            return param;
                        });
                    break;
                case EnumParameterAsset enumAsset:
                    // EnumParameterAsset is stored as IntParameter
                    DrawParameter<IntParameterAsset, IntParameter>(
                        position, label, enumAsset, stateMachineAsset, selectedEntity,
                        (rect, lbl, param) =>
                        {
                            param.Value = EditorGUIUtils.GenericEnumPopup(rect, enumAsset.EnumType.Type, param.Value);
                            return param;
                        });
                    break;
                case IntParameterAsset intAsset:
                    DrawParameter<IntParameterAsset, IntParameter>(
                        position, label, intAsset, stateMachineAsset, selectedEntity,
                        (rect, lbl, param) =>
                        {
                            param.Value = EditorGUI.IntField(rect, lbl, param.Value);
                            return param;
                        });
                    break;
                case FloatParameterAsset floatAsset:
                    DrawParameter<FloatParameterAsset, FloatParameter>(
                        position, label, floatAsset, stateMachineAsset, selectedEntity,
                        (rect, lbl, param) =>
                        {
                            param.Value = EditorGUI.FloatField(rect, lbl, param.Value);
                            return param;
                        });
                    break;
                default:
                    throw new NotImplementedException(
                        $"No handling for type {parameterAsset.GetType().Name}");
            }
        }

        /// <summary>
        /// Generic helper to draw a parameter in playmode.
        /// Eliminates duplicate lookup/get/set pattern across parameter types.
        /// </summary>
        private static void DrawParameter<TAsset, TBuffer>(
            Rect position,
            GUIContent label,
            AnimationParameterAsset asset,
            StateMachineAsset stateMachine,
            EntitySelectionProxyWrapper entity,
            ParameterDrawer<TBuffer> drawField)
            where TAsset : AnimationParameterAsset
            where TBuffer : unmanaged, IBufferElementData
        {
            var index = stateMachine.Parameters.OfType<TAsset>().FindIndex(p => p == asset);
            var buffer = entity.GetBuffer<TBuffer>();
            var param = buffer[index];
            buffer[index] = drawField(position, label, param);
        }

        private bool IsAnimatorEntitySelected(StateMachineAsset myStateMachineAsset)
        {
            return Application.isPlaying &&
                   EntitySelectionProxyUtils.TryExtractEntitySelectionProxy(out var entitySelectionProxy) &&
                   entitySelectionProxy.Exists && entitySelectionProxy.HasComponent<AnimationStateMachineDebug>()
                   && entitySelectionProxy
                       .GetManagedComponent<AnimationStateMachineDebug>()
                       .StateMachineAsset ==
                   myStateMachineAsset;
        }

        private void DrawPropertyEditorMode(Rect position, AnimationParameterAsset parameterAsset,
            StateMachineAsset stateMachine,
            SerializedProperty property)
        {
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                var labelWidth = EditorGUIUtility.labelWidth;
                var deleteButtonWidth = EditorGUIUtility.singleLineHeight;
                var typeWidth = position.width - labelWidth - deleteButtonWidth;
                var rects = position.HorizontalLayout(labelWidth, typeWidth, deleteButtonWidth).ToArray();

                //label
                {
                    var newName = EditorGUI.DelayedTextField(rects[0], parameterAsset.name);

                    if (newName != parameterAsset.name)
                    {
                        parameterAsset.name = newName;
                        EditorUtility.SetDirty(parameterAsset);
                        AssetDatabase.SaveAssetIfDirty(parameterAsset);
                        AssetDatabase.Refresh();
                    }
                }

                //type
                {
                    if (parameterAsset is EnumParameterAsset enumParameterAsset)
                    {
                        enumTypePopupSelector.DrawSelectionPopup(rects[1],
                            GUIContent.none,
                            enumParameterAsset.EnumType.Type,
                            newType =>
                            {
                                enumParameterAsset.EnumType.Type = newType;
                                EditorUtility.SetDirty(enumParameterAsset);
                            });
                    }
                    else
                    {
                        EditorGUI.LabelField(rects[1], $"({parameterAsset.ParameterTypeName})");
                    }
                }

                //delete
                {
                    if (GUI.Button(rects[2], "-"))
                    {
                        stateMachine.DeleteParameter(parameterAsset);
                        property.ApplyAndUpdate();
                    }
                }
            }
        }
    }
}
