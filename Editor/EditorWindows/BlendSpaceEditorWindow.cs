using System;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Standalone editor window for blend space visual editors.
    /// Works with any BlendSpaceVisualEditorBase implementation.
    /// </summary>
    internal class BlendSpaceEditorWindow : EditorWindow
    {
        private BlendSpaceVisualEditorBase editor;
        private SerializedObject serializedObject;
        private Action onClosed;
        
        /// <summary>
        /// Opens a window for the given blend space editor.
        /// </summary>
        public static BlendSpaceEditorWindow Open(BlendSpaceVisualEditorBase editor, Action onClosed)
        {
            if (editor == null || editor.Target == null)
            {
                Debug.LogError("Cannot open BlendSpaceEditorWindow: editor or target is null");
                return null;
            }
            
            var window = CreateInstance<BlendSpaceEditorWindow>();
            window.Setup(editor, onClosed);
            window.Show();
            return window;
        }

        private void Setup(BlendSpaceVisualEditorBase editor, Action onClosed)
        {
            this.editor = editor;
            this.serializedObject = new SerializedObject(editor.Target);
            this.onClosed = onClosed;
            
            // Configure for standalone editing (always edit mode, no mode toggle)
            editor.EditMode = true;
            editor.ShowModeToggle = false;
            editor.ShowPreviewIndicator = false;
            
            titleContent = new GUIContent($"{editor.EditorTitle}: {editor.Target.name}");
            minSize = new Vector2(300, 200);
        }

        private void OnEnable()
        {
            // Validate on re-enable
            if (editor != null && (editor.Target == null || serializedObject?.targetObject == null))
            {
                Close();
            }
        }

        private void OnDisable()
        {
            onClosed?.Invoke();
        }

        private void OnGUI()
        {
            if (editor == null || serializedObject == null || serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox("Target state has been deleted.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            // Toolbar
            DrawToolbar();

            // Main editor area
            var editorRect = GUILayoutUtility.GetRect(
                GUIContent.none, 
                GUIStyle.none, 
                GUILayout.ExpandHeight(true), 
                GUILayout.ExpandWidth(true));
            
            editor.Draw(editorRect, serializedObject);

            // Help text below the editor (separate line)
            DrawHelpText();

            // Selection fields at bottom
            EditorGUILayout.Space(5);
            if (editor.DrawSelectedClipFields(serializedObject))
            {
                serializedObject.ApplyModifiedProperties();
                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawHelpText()
        {
            if (editor == null) return;
            
            var helpText = editor.GetHelpText();
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };
            EditorGUILayout.LabelField(helpText, style);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                editor?.ResetView();
            }
            
            GUILayout.FlexibleSpace();
            
            // State name
            if (editor?.Target != null)
            {
                GUILayout.Label(editor.Target.name, EditorStyles.toolbarButton);
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
