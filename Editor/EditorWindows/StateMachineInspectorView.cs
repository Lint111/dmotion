using DMotion.Authoring;
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
        
        // UIToolkit inspector builder
        private StateInspectorUIToolkit stateInspectorBuilder;
        
        /// <summary>
        /// Sets the inspector using UIToolkit builder for a state.
        /// </summary>
        public void SetStateInspector(StateMachineAsset stateMachine, AnimationStateAsset state, StateNodeView nodeView = null)
        {
            Cleanup();
            
            stateInspectorBuilder ??= new StateInspectorUIToolkit();
            
            var content = stateInspectorBuilder.Build(stateMachine, state, nodeView);
            if (content != null)
            {
                // Wrap in scroll view
                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.style.flexGrow = 1;
                scrollView.Add(content);
                Add(scrollView);
            }
        }
        
        /// <summary>
        /// Sets the inspector using the legacy IMGUI approach.
        /// </summary>
        public void SetInspector<TEditor, TModel>(Object obj, TModel model)
            where TEditor : UnityEditor.Editor, IStateMachineInspector<TModel>
            where TModel : struct
        {
            Cleanup();
            
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
        
        /// <summary>
        /// Clears the view and cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            Object.DestroyImmediate(editor);
            editor = null;
            
            stateInspectorBuilder?.Cleanup();
            
            Clear();
        }
    }
}
