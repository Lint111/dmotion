using UnityEngine;

namespace DMotion.Samples.Common
{
    /// <summary>
    /// Displays control hints for DMotion samples using IMGUI.
    /// Add this component to any GameObject in the scene and configure the help text.
    /// </summary>
    public class SampleHelpUI : MonoBehaviour
    {
        [Header("Help Text")]
        [TextArea(3, 10)]
        public string helpText = "Sample Controls:\n- Press keys to interact";

        [Header("Display Settings")]
        public TextAnchor anchor = TextAnchor.UpperLeft;
        public int fontSize = 14;
        public Color backgroundColor = new Color(0, 0, 0, 0.7f);
        public Color textColor = Color.white;
        public float padding = 10f;
        public float margin = 10f;

        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private bool stylesInitialized;

        private void InitStyles()
        {
            if (stylesInitialized) return;

            boxStyle = new GUIStyle(GUI.skin.box);
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, backgroundColor);
            bgTex.Apply();
            boxStyle.normal.background = bgTex;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = fontSize;
            labelStyle.normal.textColor = textColor;
            labelStyle.wordWrap = true;
            labelStyle.richText = true;

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            var content = new GUIContent(helpText);
            var size = labelStyle.CalcSize(content);
            size.x = Mathf.Min(size.x, 300);
            size.y = labelStyle.CalcHeight(content, size.x);

            var boxWidth = size.x + padding * 2;
            var boxHeight = size.y + padding * 2;

            float x, y;

            // Horizontal position
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    x = margin;
                    break;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    x = Screen.width - boxWidth - margin;
                    break;
                default:
                    x = (Screen.width - boxWidth) / 2;
                    break;
            }

            // Vertical position
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    y = margin;
                    break;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    y = Screen.height - boxHeight - margin;
                    break;
                default:
                    y = (Screen.height - boxHeight) / 2;
                    break;
            }

            var boxRect = new Rect(x, y, boxWidth, boxHeight);
            var labelRect = new Rect(x + padding, y + padding, size.x, size.y);

            GUI.Box(boxRect, GUIContent.none, boxStyle);
            GUI.Label(labelRect, helpText, labelStyle);
        }
    }
}
