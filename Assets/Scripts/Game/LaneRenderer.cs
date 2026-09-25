using System;
using System.Collections.Generic;
using LaneBattle.Core.Wave;
using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>LaneSim 하나를 월드 공간의 주어진 원점에 그린다. 보간·투사체·체력바·건설/침묵 표시 담당. MonoBehaviour 아님.</summary>
    public sealed class LaneRenderer
    {
        public LaneSim Sim { get; }
        public Vector2 Origin { get; }
        public event Action<string> Banner;

        readonly Transform _root;
        readonly Sprite _square;
        readonly Dictionary<int, CreepVisual> _creeps = new Dictionary<int, CreepVisual>();
        readonly Dictionary<int, TowerVisual> _towers = new Dictionary<int, TowerVisual>();
        readonly List<Projectile> _projectiles = new List<Projectile>();
        readonly List<FadeOut> _fades = new List<FadeOut>();
        SpriteRenderer _base;
        float _baseFlash;

        sealed class CreepVisual { public Creep Creep; public Transform Root; public SpriteRenderer Body, HpBar, HpBack; public Vector2 Prev, Curr; public float Jitter; }
        sealed class TowerVisual { public Tower Tower; public Transform Root; public SpriteRenderer Body, Status, Outline; }
        sealed class Projectile { public Transform T; public Vector3 From, To; public float Age, Life; }
        sealed class FadeOut { public Transform T; public float Age; }

        public LaneRenderer(Transform parent, Sprite square, LaneSim sim, Vector2 origin, string leftLabel, string rightLabel, string slotHint)
        {
            Sim = sim; Origin = origin; _square = square;
            _root = new GameObject("Lane").transform;
            _root.SetParent(parent, false);
            BuildLane(sim.Cfg, leftLabel, rightLabel, slotHint);
            foreach (var t in sim.Towers) EnsureTower(t);
            foreach (var c in sim.Creeps) EnsureCreep(c);
        }

        public void Destroy() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }

        public void BeforeStep() { foreach (var v in _creeps.Values) v.Prev = v.Curr; }

        public void AfterStep()
        {
            foreach (var c in Sim.Creeps) EnsureCreep(c);
            foreach (var t in Sim.Towers) EnsureTower(t);
            foreach (var v in _creeps.Values) v.Curr = new Vector2(v.Creep.X / 1000f, v.Creep.Y / 1000f);
            foreach (var ev in Sim.Events) HandleEvent(ev);
        }

        public void Interpolate(float t)
        {
            foreach (var v in _creeps.Values) ApplyCreep(v, t);
            foreach (var v in _towers.Values) ApplyTower(v);
        }

        public void SnapAll() { foreach (var v in _creeps.Values) v.Prev = v.Curr; Interpolate(1f); }

        public void UpdateFx(float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                p.Age += dt;
                float t = Mathf.Clamp01(p.Age / p.Life);
                p.T.position = Vector3.Lerp(p.From, p.To, t);
                if (t >= 1f) { UnityEngine.Object.Destroy(p.T.gameObject); _projectiles.RemoveAt(i); }
            }
            for (int i = _fades.Count - 1; i >= 0; i--)
            {
                var f = _fades[i];
                f.Age += dt;
                float t = Mathf.Clamp01(f.Age / 0.3f);
                if (f.T != null) f.T.localScale = Vector3.one * (1f - t);
                if (t >= 1f) { if (f.T != null) UnityEngine.Object.Destroy(f.T.gameObject); _fades.RemoveAt(i); }
            }
            if (_baseFlash > 0) { _baseFlash -= dt; _base.color = Color.Lerp(new Color(0.35f, 0.4f, 0.6f), new Color(1f, 0.3f, 0.3f), Mathf.Clamp01(_baseFlash * 3)); }
        }

        /// <summary>월드 좌표가 이 라인의 타워 슬롯 위면 (열, 줄)을 돌려준다.</summary>
        public bool WorldToSlot(Vector3 world, out int col, out int row)
        {
            row = Mathf.FloorToInt(world.x - Origin.x) - Sim.Cfg.GridStartX;
            col = Mathf.FloorToInt(world.y - Origin.y);
            return row >= 0 && row < Sim.Cfg.Rows && col >= 0 && col < Sim.Cfg.Width;
        }

        Vector3 W(float x, float y, float z = 0) => new Vector3(Origin.x + x, Origin.y + y, z);

        // ─────────────────────────── 이벤트 ───────────────────────────

        void HandleEvent(SimEvent ev)
        {
            switch (ev.Type)
            {
                case SimEventType.WaveStart:
                    Banner?.Invoke($"웨이브 {ev.A + 1}: {WaveCatalog.Describe(ev.A)}");
                    break;
                case SimEventType.Attack:
                    if (_towers.TryGetValue(ev.A, out var tv) && _creeps.TryGetValue(ev.B, out var cv)) SpawnProjectile(tv.Root.position, cv.Root.position);
                    break;
                case SimEventType.Death:
                    if (_creeps.TryGetValue(ev.B, out var dead)) { _fades.Add(new FadeOut { T = dead.Root }); _creeps.Remove(ev.B); }
                    break;
                case SimEventType.Leak:
                    if (_creeps.TryGetValue(ev.A, out var leaker)) { UnityEngine.Object.Destroy(leaker.Root.gameObject); _creeps.Remove(ev.A); }
                    _baseFlash = 0.5f;
                    break;
                case SimEventType.Sold:
                    if (_towers.TryGetValue(ev.A, out var sold)) { UnityEngine.Object.Destroy(sold.Root.gameObject); _towers.Remove(ev.A); }
                    break;
            }
        }

        void SpawnProjectile(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _square; sr.color = new Color(1f, 0.95f, 0.6f); sr.sortingOrder = 20;
            go.transform.localScale = Vector3.one * 0.13f;
            go.transform.position = from;
            _projectiles.Add(new Projectile { T = go.transform, From = from, To = to, Life = 0.12f });
        }

        // ─────────────────────────── 시각물 ───────────────────────────

        void EnsureCreep(Creep c)
        {
            if (_creeps.ContainsKey(c.Id) || !c.Alive) return;
            var root = new GameObject(c.Def.Name).transform;
            root.SetParent(_root, false);
            var v = new CreepVisual { Creep = c, Root = root, Jitter = ((c.Id % 3) - 1) * 0.16f };
            float size = c.Boss ? 0.8f : 0.46f;
            v.Body = Square(root, "Body", size, size, CreepColor(c), 10);
            if (c.Def.Flying) { var wing = Square(root, "Wing", size + 0.3f, size * 0.35f, new Color(1, 1, 1, 0.35f), 9); wing.transform.localPosition = new Vector3(0, 0.05f, 0); }
            v.HpBack = Square(root, "HpBack", size, 0.07f, new Color(0, 0, 0, 0.6f), 11); v.HpBack.transform.localPosition = new Vector3(0, size / 2 + 0.06f, 0);
            v.HpBar = Square(root, "HpBar", size, 0.07f, Color.green, 12); v.HpBar.transform.localPosition = v.HpBack.transform.localPosition;
            TextLabel(root, c.Def.Name, 0.034f, new Vector3(0, -size / 2 - 0.02f, 0), TextAnchor.UpperCenter, 13);
            v.Prev = v.Curr = new Vector2(c.X / 1000f, c.Y / 1000f);
            ApplyCreep(v, 1f);
            _creeps[c.Id] = v;
        }

        void ApplyCreep(CreepVisual v, float t)
        {
            var p = Vector2.Lerp(v.Prev, v.Curr, t);
            v.Root.position = W(p.x, p.y + v.Jitter);
            var c = v.Creep;
            float ratio = c.MaxHp > 0 ? Mathf.Clamp01(c.Hp / (float)c.MaxHp) : 0;
            float size = v.HpBack.transform.localScale.x;
            v.HpBar.transform.localScale = new Vector3(size * ratio, 0.07f, 1);
            v.HpBar.transform.localPosition = new Vector3(-size * (1 - ratio) / 2, v.HpBack.transform.localPosition.y, 0);
            v.HpBar.color = Color.Lerp(Color.red, Color.green, ratio);
            var col = CreepColor(c);
            if (c.Stealthed) col.a = 0.35f;
            else if (c.SlowLeft > 0) col = Color.Lerp(col, new Color(0.5f, 0.7f, 1f), 0.5f);
            v.Body.color = col;
        }

        void EnsureTower(Tower t)
        {
            if (_towers.ContainsKey(t.Id) || !t.Alive) return;
            var root = new GameObject(t.Def.Name).transform;
            root.SetParent(_root, false);
            root.position = W(t.X / 1000f, t.Y / 1000f);
            var v = new TowerVisual { Tower = t, Root = root };
            v.Outline = Square(root, "Outline", 0.74f, 0.74f, new Color(1, 1, 1, 0), 9);
            v.Body = Square(root, "Body", 0.62f, 0.62f, TowerColor(t.Def), 10);
            v.Status = Square(root, "Status", 0.62f, 0.62f, new Color(0, 0, 0, 0), 11);
            TextLabel(root, t.Def.Name, 0.036f, new Vector3(0, -0.33f, 0), TextAnchor.UpperCenter, 13);
            _towers[t.Id] = v;
            ApplyTower(v);
        }

        void ApplyTower(TowerVisual v)
        {
            var t = v.Tower;
            if (t.BuildLeft > 0)
            {
                float progress = 1f - t.BuildLeft / (float)(Sim.Cfg.BuildSeconds * Sim.Cfg.TicksPerSecond);
                v.Status.color = new Color(0, 0, 0, 0.55f);
                v.Status.transform.localScale = new Vector3(0.62f, 0.62f * (1f - progress), 1);
                v.Status.transform.localPosition = new Vector3(0, 0.31f * progress, 0);
            }
            else if (t.SilenceLeft > 0) { v.Status.color = new Color(0.5f, 0.5f, 0.6f, 0.7f); v.Status.transform.localScale = new Vector3(0.62f, 0.62f, 1); v.Status.transform.localPosition = Vector3.zero; }
            else v.Status.color = new Color(0, 0, 0, 0);
            v.Outline.color = t.Upgraded ? Color.white : new Color(1, 1, 1, 0);
        }

        SpriteRenderer Square(Transform parent, string name, float w, float h, Color color, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _square; sr.color = color; sr.sortingOrder = order;
            go.transform.localScale = new Vector3(w, h, 1);
            return sr;
        }

        TextMesh TextLabel(Transform parent, string text, float charSize, Vector3 localPos, TextAnchor anchor, int order)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = UiKit.Font; tm.fontSize = 40; tm.characterSize = charSize; tm.anchor = anchor; tm.alignment = TextAlignment.Center;
            tm.text = text; tm.color = new Color(0.95f, 0.95f, 0.95f);
            var mr = go.GetComponent<MeshRenderer>(); mr.material = UiKit.Font.material; mr.sortingOrder = order;
            go.transform.localPosition = localPos;
            return tm;
        }

        void BuildLane(LaneConfig cfg, string leftLabel, string rightLabel, string slotHint)
        {
            var bg = Square(_root, "LaneBg", cfg.Length + 1, cfg.Width + 0.6f, new Color(0.16f, 0.18f, 0.22f), 0);
            bg.transform.position = W(cfg.Length / 2f, cfg.Width / 2f, 1);
            for (int c = 0; c < cfg.Width; c++)
                for (int r = 0; r < cfg.Rows; r++)
                {
                    var slot = Square(_root, "Slot", 0.9f, 0.9f, new Color(0.24f, 0.27f, 0.33f), 1);
                    slot.transform.position = W(cfg.GridStartX + r + 0.5f, c + 0.5f, 0.5f);
                }
            var spawn = Square(_root, "Spawn", 0.3f, cfg.Width, new Color(0.5f, 0.25f, 0.25f), 1); spawn.transform.position = W(0, cfg.Width / 2f, 0.5f);
            _base = Square(_root, "Base", 0.6f, cfg.Width, new Color(0.35f, 0.4f, 0.6f), 1); _base.transform.position = W(cfg.Length + 0.3f, cfg.Width / 2f, 0.5f);
            var l = TextLabel(_root, leftLabel, 0.065f, W(0.2f, cfg.Width + 0.35f), TextAnchor.MiddleLeft, 5); l.anchor = TextAnchor.MiddleLeft; l.alignment = TextAlignment.Left;
            if (!string.IsNullOrEmpty(slotHint)) TextLabel(_root, slotHint, 0.055f, W(cfg.GridStartX + 1.5f, cfg.Width + 0.35f), TextAnchor.MiddleCenter, 5);
            var r2 = TextLabel(_root, rightLabel, 0.065f, W(cfg.Length + 0.3f, cfg.Width + 0.35f), TextAnchor.MiddleRight, 5); r2.anchor = TextAnchor.MiddleRight; r2.alignment = TextAlignment.Right;
        }

        public static Color TowerColor(TowerDef d) => d.Tribe switch
        {
            DefTribe.Forest => new Color(0.45f, 0.78f, 0.45f),
            DefTribe.Fire => new Color(0.95f, 0.55f, 0.35f),
            _ => new Color(0.55f, 0.68f, 0.90f),
        };

        public static Color CreepColor(Creep c) => c.Def.Tribe switch
        {
            AtkTribe.Beast => new Color(0.85f, 0.35f, 0.35f),
            AtkTribe.Air => new Color(0.75f, 0.45f, 0.85f),
            AtkTribe.Giant => new Color(0.6f, 0.4f, 0.3f),
            _ => new Color(0.4f, 0.28f, 0.5f),
        };

        public static Sprite MakeSquare()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
    }
}
