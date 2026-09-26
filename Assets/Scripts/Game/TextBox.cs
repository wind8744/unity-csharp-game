using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>아주 작은 글자 입력 칸 (새 Input System 의 onTextInput 사용). 클릭하면 포커스, Enter 로 제출.</summary>
    public sealed class TextBox : MonoBehaviour
    {
        public Text Display;
        public string Value = "";
        public int MaxLength = 32;
        public bool Focused;
        public System.Action OnSubmit;
        public static TextBox Active;
        float _blink;

        void OnEnable() { if (Keyboard.current != null) Keyboard.current.onTextInput += OnChar; }
        void OnDisable() { if (Keyboard.current != null) Keyboard.current.onTextInput -= OnChar; if (Active == this) Active = null; }

        void OnChar(char c)
        {
            if (!Focused || char.IsControl(c)) return;
            if (Value.Length < MaxLength) Value += c;
            Refresh();
        }

        public void Focus()
        {
            if (Active != null && Active != this) { Active.Focused = false; Active.Refresh(); }
            Active = this; Focused = true; Refresh();
        }

        void Update()
        {
            if (!Focused) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.backspaceKey.wasPressedThisFrame && Value.Length > 0) Value = Value.Substring(0, Value.Length - 1);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) OnSubmit?.Invoke();
            if (kb.escapeKey.wasPressedThisFrame) { Focused = false; if (Active == this) Active = null; }
            if (kb.vKey.wasPressedThisFrame && (kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed || kb.leftCtrlKey.isPressed))
            {
                string clip = GUIUtility.systemCopyBuffer ?? "";
                foreach (var c in clip) if (!char.IsControl(c) && Value.Length < MaxLength) Value += c;
            }
            _blink += Time.deltaTime;
            Refresh();
        }

        public void Refresh()
        {
            if (Display != null) Display.text = Value + (Focused && Mathf.FloorToInt(_blink * 2f) % 2 == 0 ? "|" : "");
        }

        /// <summary>패널 + 글자 + 클릭 포커스.</summary>
        public static TextBox Create(Transform parent, string name, float x, float y, float w, float h, string initial, int size = 14)
        {
            var img = UiKit.SpritePanel(parent, name, x, y, w, h, "ui_slot");
            var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var text = UiKit.Label(img.transform, "Text", 8, 0, w - 16, h, initial, size, TextAnchor.MiddleLeft, UiKit.Ink);
            var tb = img.gameObject.AddComponent<TextBox>();
            tb.Display = text; tb.Value = initial;
            b.onClick.AddListener(tb.Focus);
            tb.Refresh();
            return tb;
        }
    }
}
