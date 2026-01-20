using DMotion.Authoring;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    [CustomEditor(typeof(AnimationClipAsset))]
    internal class AnimationClipAssetEditor : UnityEditor.Editor
    { 
        /// <summary>
        /// Right padding for the events drawer to prevent UI overlap with inspector edge.
        /// </summary>
        private const float EventsDrawerRightPadding = 60f;

        private SingleClipPreview preview;
        private AnimationClipAsset ClipTarget => (AnimationClipAsset)target;
        
        private SerializedProperty clipProperty;
        private AnimationEventsPropertyDrawer eventsPropertyDrawer;
        
        private void OnEnable()
        {
            preview = new SingleClipPreview(ClipTarget.Clip);
            preview.Initialize();
            clipProperty = serializedObject.FindProperty(nameof(AnimationClipAsset.Clip));
            eventsPropertyDrawer = new AnimationEventsPropertyDrawer(
                ClipTarget,
                serializedObject.FindProperty(nameof(AnimationClipAsset.Events)),
                preview);
        }
        
        private void OnDisable()
        {
            preview?.Dispose();
        }

        public override void OnInspectorGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview Object");
                preview.GameObject = (GameObject)EditorGUILayout.ObjectField(preview.GameObject, typeof(GameObject), false);
            }
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                EditorGUILayout.PropertyField(clipProperty, true);
                preview.Clip = ClipTarget.Clip;

                if (c.changed)
                {
                    serializedObject.ApplyAndUpdate();
                }
                
                var drawerRect = EditorGUILayout.GetControlRect();
                drawerRect.xMax -= EventsDrawerRightPadding;
                eventsPropertyDrawer.OnInspectorGUI(drawerRect);
            }
        }

        public override bool HasPreviewGUI()
        {
            return true;
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (AnimationMode.InAnimationMode())
            {
                preview?.DrawPreview(r, background);
            }
        }
    }
}