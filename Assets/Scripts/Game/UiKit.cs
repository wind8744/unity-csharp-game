using UnityEngine;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>코드로 uGUI 를 만드는 최소 도우미. 프로토타입 전용.</summary>
    public static class UiKit
    {
        static Font _font;
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = UnityEngine.Font.CreateDynamicFontFromOSFont(
                    new[] { "Apple SD Gothic Neo", "AppleGothic", "Malgun Gothic", "NanumGothic", "Noto Sans CJK KR", "Arial" }, 16);
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static readonly Color Bg = new Color(0.12f, 0.13f, 0.16f);
        public static readonly Color Panel = new Color(0.18f, 0.20f, 0.25f);
        public static readonly Color PanelLight = new Color(0.24f, 0.27f, 0.33f);
        public static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color Accent = new Color(1.0f, 0.78f, 0.25f);

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image PanelBox(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string name, float x, float y, float w, float h, string text,
            int size = 16, TextAnchor align = TextAnchor.UpperLeft, Color? color = null)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.text = text;
            t.alignment = align;
            t.color = color ?? TextColor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public static Button ButtonBox(Transform parent, string name, float x, float y, float w, float h, string text,
            System.Action onClick, Color? color = null, int size = 16)
        {
            var img = PanelBox(parent, name, x, y, w, h, color ?? PanelLight);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1, 1, 1, 0.85f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            b.colors = colors;
            var lbl = Label(img.transform, "Label", 0, 0, w, h, text, size, TextAnchor.MiddleCenter);
            lbl.raycastTarget = false;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
