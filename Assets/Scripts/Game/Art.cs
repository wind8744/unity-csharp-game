using System.Collections.Generic;
using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>Resources/Sprites 의 그림을 이름으로 꺼내 쓴다. 없으면 색 사각형으로 대신하고 한 번만 경고한다 (그림이 빠져도 게임은 돈다).</summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        static readonly HashSet<string> _warned = new HashSet<string>();
        static Sprite _square, _ring, _circle;
        public const float PixelsPerUnit = 64f;

        public static bool Has(string name) => Get(name, false) != null;

        public static Sprite Get(string name, bool fallback = true)
        {
            if (_cache.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("Sprites/" + name);
            if (s == null && fallback)
            {
                if (_warned.Add(name)) Debug.LogWarning($"그림 없음: {name} (대체 사각형 사용)");
                s = Square;
            }
            if (s != null) _cache[name] = s;
            return s;
        }

        public static Sprite[] Frames(string prefix, int count)
        {
            var arr = new Sprite[count];
            for (int i = 0; i < count; i++) arr[i] = Get($"{prefix}_{i}");
            return arr;
        }

        public static Sprite Tower(int defId, int frame) => Get($"tower_{defId}_{frame}");
        public static Sprite TowerAttack(int defId) => Get($"tower_{defId}_atk");
        public static Sprite Creep(int defId, int frame) => Get($"creep_{defId}_{frame}");

        /// <summary>4×4 흰 사각형 (PPU 4 → 1칸).</summary>
        public static Sprite Square
        {
            get
            {
                if (_square != null) return _square;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Point;
                _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                return _square;
            }
        }

        /// <summary>사거리 표시용 고리 (지름 1칸, 스케일로 키운다).</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring != null) return _ring;
                const int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = d > 0.94f && d <= 1f ? 1f : d > 0.9f ? 0.35f : d < 0.9f ? 0.08f : 0f;
                        px[y * n + x] = new Color(1, 1, 1, a);
                    }
                tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Bilinear;
                _ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
                return _ring;
            }
        }

        /// <summary>부드러운 원 (그림자, 빛).</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 3f));
                    }
                tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Bilinear;
                _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
                return _circle;
            }
        }
    }
}
