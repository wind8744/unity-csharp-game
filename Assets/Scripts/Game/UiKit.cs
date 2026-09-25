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
        public static readonly Color Ink = new Color(0.23f, 0.16f, 0.11f);        // 크림 패널 위 글자
        public static readonly Color InkSoft = new Color(0.45f, 0.36f, 0.28f);
        public static readonly Color Gold = new Color(1.0f, 0.82f, 0.30f);
        public static readonly Color Red = new Color(0.95f, 0.35f, 0.32f);
        public static readonly Color Green = new Color(0.45f, 0.80f, 0.40f);
        public static readonly Color Blue = new Color(0.45f, 0.65f, 0.95f);

        static Transform _canvas;
        /// <summary>공용 캔버스 (1280×720 기준 스케일). 없으면 만든다.</summary>
        public static Transform Canvas
        {
            get
            {
                if (_canvas != null) return _canvas;
                var existing = Object.FindFirstObjectByType<UnityEngine.Canvas>();
                if (existing != null && existing.name == "Canvas") { _canvas = existing.transform; return _canvas; }
                var canvasGo = new GameObject("Canvas", typeof(UnityEngine.Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.GetComponent<UnityEngine.Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.matchWidthOrHeight = 0.5f;
                if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                    new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                _canvas = canvasGo.transform;
                return _canvas;
            }
        }

        /// <summary>9분할 스프라이트 패널 (ui_panel, ui_panel_dark, ui_button…).</summary>
        public static Image SpritePanel(Transform parent, string name, float x, float y, float w, float h, string sprite = "ui_panel", Color? tint = null)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.Get(sprite);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = tint ?? Color.white;
            return img;
        }

        public static Image Icon(Transform parent, string name, float x, float y, float size, string sprite, Color? tint = null)
        {
            var rt = Rect(parent, name, x, y, size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.Get(sprite);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = tint ?? Color.white;
            return img;
        }

        public static Image IconSprite(Transform parent, string name, float x, float y, float w, float h, Sprite sprite)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>아이콘 + 글자 한 줄.</summary>
        public static Text IconLabel(Transform parent, string name, float x, float y, float w, float h, string icon, string text, int size = 14, Color? color = null)
        {
            Icon(parent, name + "Icon", x, y + (h - size - 2) / 2, size + 2, icon);
            return Label(parent, name, x + size + 8, y, w - size - 8, h, text, size, TextAnchor.MiddleLeft, color);
        }

        /// <summary>테두리 있는 글자 (배경 위 가독성).</summary>
        public static Text OutlinedLabel(Transform parent, string name, float x, float y, float w, float h, string text, int size = 16, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null, Color? outline = null)
        {
            var t = Label(parent, name, x, y, w, h, text, size, align, color);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = outline ?? new Color(0, 0, 0, 0.85f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
            return t;
        }

        /// <summary>스프라이트 버튼 (클릭 소리 포함).</summary>
        public static Button SpriteButton(Transform parent, string name, float x, float y, float w, float h, string text, System.Action onClick,
            string sprite = "ui_button", int size = 15, Color? textColor = null, string sound = "click")
        {
            var img = SpritePanel(parent, name, x, y, w, h, sprite);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.9f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.8f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            b.colors = colors;
            var lbl = OutlinedLabel(img.transform, "Label", 0, 0, w, h, text, size, TextAnchor.MiddleCenter, textColor ?? Color.white, new Color(0.25f, 0.12f, 0.05f, 0.9f));
            lbl.raycastTarget = false;
            b.onClick.AddListener(() => { if (!string.IsNullOrEmpty(sound)) Sfx.Play(sound, 0.7f); onClick?.Invoke(); });
            return b;
        }

        public static void SetText(Transform parent, string path, string text)
        {
            var t = parent.Find(path);
            if (t != null) { var tx = t.GetComponent<Text>(); if (tx != null) tx.text = text; }
        }

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
