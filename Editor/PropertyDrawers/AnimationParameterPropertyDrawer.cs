using System;
using System.Linq;
using DMotion.Authoring;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
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
                    DrawBoolParameter(position, label, boolAsset, stateMachineAsset, selectedEntity);
                    break;
                case EnumParameterAsset enumAsset:
                    DrawEnumParameter(position, enumAsset, stateMachineAsset, selectedEntity);
                    break;
                case IntParameterAsset intAsset:
                    DrawIntParameter(position, label, intAsset, stateMachineAsset, selectedEntity);
                    break;
                case FloatParameterAsset floatAsset:
                    DrawFloatParameter(position, label, floatAsset, stateMachineAsset, selectedEntity);
                    break;
                default:
                    throw new NotImplementedException(
                        $"No handling for type {parameterAsset.GetType().Name}");
            }
        }

        private static int FindParameterIndex<TAsset>(StateMachineAsset stateMachine, TAsset asset)
            where TAsset : AnimationParameterAsset
        {
            return stateMachine.Parameters.OfType<TAsset>().FindIndex(p => p == asset);
        }

        private static void DrawBoolParameter(Rect position, GUIContent label, BoolParameterAsset asset,
            StateMachineAsset stateMachine, EntitySelectionProxyWrapper entity)
        {
            var index = FindParameterIndex(stateMachine, asset);
            var buffer = entity.GetBuffer<BoolParameter>();
            var param = buffer[index];
            param.Value = EditorGUI.Toggle(position, label, param.Value);
            buffer[index] = param;
        }

        private static void DrawIntParameter(Rect position, GUIContent label, IntParameterAsset asset,
            StateMachineAsset stateMachine, EntitySelectionProxyWrapper entity)
        {
            var index = FindParameterIndex(stateMachine, asset);
            var buffer = entity.GetBuffer<IntParameter>();
            var param = buffer[index];
            param.Value = EditorGUI.IntField(position, label, param.Value);
            buffer[index] = param;
        }

        private static void DrawEnumParameter(Rect position, EnumParameterAsset asset,
            StateMachineAsset stateMachine, EntitySelectionProxyWrapper entity)
        {
            // EnumParameterAsset is stored as IntParameter
            var index = FindParameterIndex<IntParameterAsset>(stateMachine, asset);
            var buffer = entity.GetBuffer<IntParameter>();
            var param = buffer[index];
            param.Value = EditorGUIUtils.GenericEnumPopup(position, asset.EnumType.Type, param.Value);
            buffer[index] = param;
        }

        private static void DrawFloatParameter(Rect position, GUIContent label, FloatParameterAsset asset,
            StateMachineAsset stateMachine, EntitySelectionProxyWrapper entity)
        {
            var index = FindParameterIndex(stateMachine, asset);
            var buffer = entity.GetBuffer<FloatParameter>();
            var param = buffer[index];
            param.Value = EditorGUI.FloatField(position, label, param.Value);
            buffer[index] = param;
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