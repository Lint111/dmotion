using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DMotion.Editor
{
    internal interface IStateMachineInspector<T>
        where T : struct
    {
        internal void SetModel(T model);
    }

    [UxmlElement]
    internal partial class StateMachineInspectorView : VisualElement
    {

        private UnityEditor.Editor editor;
        private Vector2 scrollPos;

        public void SetInspector<TEditor, TModel>(Object obj, TModel model)
            where TEditor : UnityEditor.Editor, IStateMachineInspector<TModel>
            where TModel : struct
        {
            Object.DestroyImmediate(editor);

            Clear();
            editor = UnityEditor.Editor.CreateEditor(obj, typeof(TEditor));
            ((IStateMachineInspector<TModel>)editor).SetModel(model);

            var imgui = new IMGUIContainer(() =>
            {
                if (editor.target != null)
                {
                    using (var scope = new EditorGUILayout.ScrollViewScope(scrollPos))
                    {
                        editor.OnInspectorGUI();
                        scrollPos = scope.scrollPosition;
                        editor.serializedObject.ApplyAndUpdate();
                    }
                }
            });
            Add(imgui);
        }
    }
}
