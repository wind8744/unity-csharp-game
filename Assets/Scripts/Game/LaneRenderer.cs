using System;
using System.Collections.Generic;
using LaneBattle.Core.Wave;
using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>
    /// LaneSim 하나를 월드 공간에 그린다: 바닥 타일·슬롯·성문·기지, 타워(대기/공격 프레임, 별·강화 배지, 건설·침묵 표시),
    /// 유닛(걷기 프레임, 그림자, 체력바, 상태 아이콘), 투사체, 타격/사망/합성 이펙트, 데미지 숫자, 효과음. MonoBehaviour 아님.
    /// </summary>
    public sealed class LaneRenderer
    {
        public LaneSim Sim { get; }
        public Vector2 Origin { get; }
        public bool IsMine { get; }
        public event Action<string> Banner;
        public bool PlaySounds = true;
        public int SelectedTowerId = -1;

        readonly Transform _root, _fxRoot;
        readonly Dictionary<int, CreepVisual> _creeps = new Dictionary<int, CreepVisual>();
        readonly Dictionary<int, TowerVisual> _towers = new Dictionary<int, TowerVisual>();
        readonly List<Projectile> _projectiles = new List<Projectile>();
        readonly List<FxAnim> _fx = new List<FxAnim>();
        readonly List<PopupText> _popups = new List<PopupText>();
        readonly Stack<TextMesh> _popupPool = new Stack<TextMesh>();
        readonly Dictionary<(int, int), SpriteRenderer> _slots = new Dictionary<(int, int), SpriteRenderer>();
        SpriteRenderer _base, _gate, _hoverSlot, _selectSlot, _rangeRing;
        Transform _baseRoot;
        Vector3 _baseAnchor;
        float _baseShake, _baseFlash, _time;
        (int col, int row) _hover = (-1, -1);
        readonly Sprite[] _fxHit, _fxBoom, _fxSmoke, _fxStar, _fxHeal;
        readonly Color _baseColor = Color.white;

        sealed class CreepVisual
        {
            public Creep Creep; public Transform Root; public SpriteRenderer Body, Shadow, HpBar, HpBack, Status, Status2; public Vector2 Prev, Curr;
            public float Jitter, Flash, WalkClock; public int Frame; public Sprite[] Frames; public float Size;
        }
        sealed class TowerVisual
        {
            public Tower Tower; public Transform Root, BodyRoot; public SpriteRenderer Body, Shadow, Star, Badge, BuildBar, BuildBack, Silence, Owner;
            public Sprite Idle0, Idle1, Attack; public float AttackLeft, Clock; public bool WasBuilding = true; public TextMesh OwnerLabel;
        }
        sealed class Projectile { public Transform T; public Vector3 From, To; public float Age, Life, Arc; public bool Spin; }
        sealed class FxAnim { public SpriteRenderer R; public Sprite[] Frames; public float Age, FrameTime; public Vector3 Drift; public bool Fade; }
        sealed class PopupText { public TextMesh T; public float Age, Life; public Vector3 Start; public Color Color; }

        public LaneRenderer(Transform parent, LaneSim sim, Vector2 origin, bool isMine, string title)
        {
            Sim = sim; Origin = origin; IsMine = isMine;
            _root = new GameObject(isMine ? "MyLane" : "EnemyLane").transform;
            _root.SetParent(parent, false);
            _fxRoot = new GameObject("Fx").transform; _fxRoot.SetParent(_root, false);
            _fxHit = Art.Frames("fx_hit", 3); _fxBoom = Art.Frames("fx_boom", 4); _fxSmoke = Art.Frames("fx_smoke", 3); _fxStar = Art.Frames("fx_star", 4); _fxHeal = Art.Frames("fx_heal", 3);
            BuildGround(sim.Cfg, title);
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

        public void SetHover(int col, int row)
        {
            if (_hover == (col, row)) return;
            _hover = (col, row);
            if (_hoverSlot != null)
            {
                bool on = col >= 0;
                _hoverSlot.enabled = on;
                if (on) _hoverSlot.transform.position = SlotCenter(col, row, 0.4f);
            }
        }

        public Vector3 SlotCenter(int col, int row, float z = 0) => W(Sim.Cfg.GridStartX + row + 0.5f, col + 0.5f, z);
        public Vector3 TowerWorld(Tower t) => W(t.X / 1000f, t.Y / 1000f);

        // ─────────────────────────── 매 프레임 ───────────────────────────

        public void UpdateFx(float dt)
        {
            _time += dt;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                p.Age += dt;
                float t = Mathf.Clamp01(p.Age / p.Life);
                var pos = Vector3.Lerp(p.From, p.To, t);
                pos.y += Mathf.Sin(t * Mathf.PI) * p.Arc;
                if (p.T == null) { _projectiles.RemoveAt(i); continue; }
                p.T.position = pos;
                if (p.Spin) p.T.Rotate(0, 0, 720f * dt);
                if (t >= 1f) { UnityEngine.Object.Destroy(p.T.gameObject); _projectiles.RemoveAt(i); }
            }
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.Age += dt;
                if (f.R == null) { _fx.RemoveAt(i); continue; }
                int frame = Mathf.FloorToInt(f.Age / f.FrameTime);
                if (frame >= f.Frames.Length) { UnityEngine.Object.Destroy(f.R.gameObject); _fx.RemoveAt(i); continue; }
                f.R.sprite = f.Frames[frame];
                f.R.transform.position += f.Drift * dt;
                if (f.Fade) f.R.color = new Color(1, 1, 1, 1f - f.Age / (f.FrameTime * f.Frames.Length));
            }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                float t = p.Age / p.Life;
                if (p.T == null) { _popups.RemoveAt(i); continue; }
                if (t >= 1f) { p.T.gameObject.SetActive(false); _popupPool.Push(p.T); _popups.RemoveAt(i); continue; }
                p.T.transform.position = p.Start + new Vector3(0, 0.35f + t * 0.5f, 0);
                var c = p.Color; c.a = 1f - Mathf.Clamp01((t - 0.55f) / 0.45f);
                p.T.color = c;
            }
            foreach (var v in _creeps.Values)
            {
                if (v.Flash > 0) v.Flash -= dt;
                v.WalkClock += dt;
            }
            foreach (var v in _towers.Values)
            {
                v.Clock += dt;
                if (v.AttackLeft > 0) v.AttackLeft -= dt;
            }
            if (_baseShake > 0)
            {
                _baseShake -= dt;
                _baseRoot.position = _baseAnchor + new Vector3(UnityEngine.Random.Range(-0.06f, 0.06f), UnityEngine.Random.Range(-0.04f, 0.04f), 0) * Mathf.Clamp01(_baseShake * 3);
                if (_baseShake <= 0) _baseRoot.position = _baseAnchor;
            }
            if (_baseFlash > 0) { _baseFlash -= dt; _base.color = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.45f), Mathf.Clamp01(_baseFlash * 3)); }
            if (_rangeRing != null)
            {
                var sel = SelectedTowerId >= 0 ? Sim.TowerAt(SelectedTowerId) : null;
                bool show = sel != null;
                _rangeRing.enabled = show; _selectSlot.enabled = show;
                if (show)
                {
                    float r = Sim.EffectiveRange(sel) / 1000f;
                    _rangeRing.transform.position = TowerWorld(sel) + new Vector3(0, 0, 0.3f);
                    _rangeRing.transform.localScale = Vector3.one * r * 2f;
                    _selectSlot.transform.position = SlotCenter(sel.Col, sel.Row, 0.4f);
                }
            }
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
                    if (IsMine) Sound(WaveCatalog.IsBossWave(ev.A) ? "boss" : "wave", 0.8f);
                    break;
                case SimEventType.Spawn:
                    if (_creeps.TryGetValue(ev.A, out var sp)) Fx(_fxSmoke, sp.Root.position + new Vector3(-0.2f, 0, 0), 0.07f, 0.8f, new Vector3(0, 0.6f, 0), true);
                    break;
                case SimEventType.Attack:
                    if (_towers.TryGetValue(ev.A, out var tv) && _creeps.TryGetValue(ev.B, out var cv)) Shoot(tv, cv);
                    break;
                case SimEventType.Damage:
                    if (_creeps.TryGetValue(ev.B, out var hit))
                    {
                        hit.Flash = 0.09f;
                        if (ev.A >= 0) Fx(_fxHit, hit.Root.position + new Vector3(UnityEngine.Random.Range(-0.15f, 0.15f), UnityEngine.Random.Range(-0.1f, 0.2f), -0.1f), 0.05f, 0.55f);
                        if (ev.A == -1) Fx(new[] { Art.Get("fx_burn") }, hit.Root.position + new Vector3(0, 0.25f, -0.1f), 0.2f, 0.4f, new Vector3(0, 0.8f, 0), true);
                        if (ev.A == -2) Fx(_fxBoom, hit.Root.position, 0.05f, 0.7f);
                        DamageNumber(hit.Root.position, ev.Value, ev.A == -1 ? new Color(1f, 0.6f, 0.3f) : Color.white, hit.Creep.Boss ? 0.05f : 0.04f);
                        if (ev.A >= 0) Sound(hit.Creep.Boss ? "hit_heavy" : "hit", 0.35f, 1f, 0.15f, 0.08f);
                    }
                    break;
                case SimEventType.Death:
                    if (_creeps.TryGetValue(ev.B, out var dead))
                    {
                        bool big = dead.Creep.Boss;
                        Fx(_fxBoom, dead.Root.position + new Vector3(0, 0.1f, -0.2f), 0.06f, big ? 1.6f : 0.9f);
                        for (int k = 0; k < (big ? 3 : 1); k++) Fx(_fxSmoke, dead.Root.position + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(-0.1f, 0.3f), -0.1f), 0.08f, 0.9f, new Vector3(0, 0.7f, 0), true);
                        Sound(big ? "death_big" : "death_small", big ? 0.9f : 0.5f, 1f, 0.12f, 0.06f);
                        if (!dead.Creep.NoKillGold) Popup(dead.Root.position + new Vector3(0.15f, 0.2f, 0), "+1", UiKit.Gold, 0.045f, 0.9f);
                        UnityEngine.Object.Destroy(dead.Root.gameObject);
                        _creeps.Remove(ev.B);
                    }
                    break;
                case SimEventType.Leak:
                    if (_creeps.TryGetValue(ev.A, out var leaker)) { UnityEngine.Object.Destroy(leaker.Root.gameObject); _creeps.Remove(ev.A); }
                    _baseFlash = 0.5f; _baseShake = 0.35f;
                    Popup(_baseRoot.position + new Vector3(0, 0.6f, 0), $"-{ev.Value}", UiKit.Red, 0.06f, 1.1f);
                    Fx(_fxBoom, _baseRoot.position + new Vector3(-0.2f, 0.2f, -0.2f), 0.06f, 0.9f);
                    Sound(IsMine ? "leak" : "base_hit", IsMine ? 0.8f : 0.5f, 1f, 0.05f, 0.15f);
                    break;
                case SimEventType.Built:
                    if (_towers.TryGetValue(ev.A, out var built)) { Fx(_fxSmoke, built.Root.position + new Vector3(0, -0.1f, -0.2f), 0.08f, 1.1f, Vector3.zero, true); if (IsMine) Sound("build", 0.7f); }
                    break;
                case SimEventType.Upgraded:
                    if (_towers.TryGetValue(ev.A, out var up)) { Fx(_fxStar, up.Root.position + new Vector3(0, 0.1f, -0.2f), 0.07f, 1.2f); Popup(up.Root.position + new Vector3(0, 0.3f, 0), "강화!", UiKit.Gold, 0.05f, 1f); if (IsMine) Sound("upgrade", 0.8f); }
                    break;
                case SimEventType.Merged:
                    if (_towers.TryGetValue(ev.A, out var merged))
                    {
                        Fx(_fxStar, merged.Root.position + new Vector3(0, 0.1f, -0.2f), 0.08f, 1.8f);
                        Popup(merged.Root.position + new Vector3(0, 0.35f, 0), ev.Value == 3 ? "★★★" : "★★", UiKit.Gold, 0.07f, 1.3f);
                        Sound("merge", 0.9f);
                    }
                    break;
                case SimEventType.Fused:
                    if (_towers.TryGetValue(ev.A, out var fused))
                    {
                        Fx(_fxStar, fused.Root.position + new Vector3(0, 0.1f, -0.2f), 0.08f, 2.0f);
                        for (int k = 0; k < 2; k++) Fx(_fxSmoke, fused.Root.position + new Vector3(k == 0 ? -0.3f : 0.3f, 0, -0.1f), 0.08f, 1f, new Vector3(0, 0.5f, 0), true);
                        Popup(fused.Root.position + new Vector3(0, 0.4f, 0), "합성! " + WaveCatalog.Tower(ev.Value).Name, new Color(1f, 0.6f, 0.9f), 0.055f, 1.5f);
                        Sound("fuse", 0.9f);
                    }
                    break;
                case SimEventType.Sold:
                    if (_towers.TryGetValue(ev.A, out var sold)) { UnityEngine.Object.Destroy(sold.Root.gameObject); _towers.Remove(ev.A); }
                    break;
                case SimEventType.Heal:
                    if (_creeps.TryGetValue(ev.B, out var healed)) Fx(_fxHeal, healed.Root.position + new Vector3(0, 0.2f, -0.1f), 0.08f, 0.6f, new Vector3(0, 0.5f, 0), true);
                    break;
                case SimEventType.Silence:
                    if (_towers.TryGetValue(ev.B, out var sil)) Popup(sil.Root.position + new Vector3(0, 0.3f, 0), "침묵", new Color(0.7f, 0.7f, 0.85f), 0.04f, 0.8f);
                    break;
            }
        }

        void Sound(string name, float vol = 1f, float pitch = 1f, float jitter = 0.06f, float minGap = 0.05f)
        {
            if (PlaySounds) Sfx.Play(name, vol * (IsMine ? 1f : 0.6f), pitch, jitter, minGap);
        }

        // ─────────────────────────── 투사체 ───────────────────────────

        static (string sprite, string sound, float speed, float arc, bool spin, float pitch) ProjectileStyle(TowerDef d)
        {
            switch (d.Id)
            {
                case 1: return ("proj_arrow", "shoot_arrow", 16f, 0f, false, 1f);
                case 2: return ("proj_thorn", "shoot_arrow", 12f, 0.1f, true, 0.8f);
                case 4: return ("proj_fireball", "shoot_fire", 12f, 0f, false, 1.1f);
                case 5: return ("proj_fireball", "shoot_magic", 9f, 0.25f, false, 1f);
                case 6: return ("proj_fireball", "shoot_fire", 14f, 0f, false, 1.3f);
                case 7: return ("proj_bullet", "shoot_laser", 20f, 0f, false, 0.9f);
                case 8: return ("proj_cannon", "shoot_cannon", 9f, 0.6f, false, 1f);
                case 10: return ("proj_fire_arrow", "shoot_fire", 17f, 0f, false, 1.2f);
                case 11: return ("proj_thorn", "shoot_magic", 10f, 0.3f, true, 0.85f);
                case 12: return ("proj_fireball", "shoot_fire", 10f, 0f, false, 0.9f);
                case 13: return ("proj_rock", "shoot_cannon", 8f, 0.9f, true, 0.8f);
                case 14: return ("proj_bolt", "shoot_bolt", 22f, 0f, false, 1f);
                case 15: return ("proj_bullet", "shoot_laser", 26f, 0f, false, 1.2f);
                case 16: return ("proj_thorn", "shoot_bolt", 14f, 0.15f, true, 0.7f);
                case 17: return ("proj_fireball", "shoot_magic", 10f, 0.2f, false, 1.15f);
                default: return ("proj_spark", "shoot_magic", 14f, 0f, false, 1f);
            }
        }

        void Shoot(TowerVisual tv, CreepVisual cv)
        {
            tv.AttackLeft = 0.16f;
            var style = ProjectileStyle(tv.Tower.Def);
            var from = tv.Root.position + new Vector3(0, 0.2f, -0.5f);
            var to = cv.Root.position + new Vector3(0, 0.1f, -0.5f);
            var go = new GameObject("Shot");
            go.transform.SetParent(_fxRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art.Get(style.sprite); sr.sortingOrder = 21;
            go.transform.position = from;
            var dir = to - from;
            go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            float dist = Mathf.Max(0.2f, dir.magnitude);
            _projectiles.Add(new Projectile { T = go.transform, From = from, To = to, Life = dist / style.speed, Arc = style.arc, Spin = style.spin });
            Sound(style.sound, 0.45f, style.pitch, 0.08f, 0.07f);
        }

        // ─────────────────────────── 이펙트·팝업 ───────────────────────────

        void Fx(Sprite[] frames, Vector3 pos, float frameTime, float scale, Vector3 drift = default, bool fade = false)
        {
            if (frames == null || frames.Length == 0) return;
            var go = new GameObject("Fx");
            go.transform.SetParent(_fxRoot, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0]; sr.sortingOrder = 20;
            _fx.Add(new FxAnim { R = sr, Frames = frames, FrameTime = frameTime, Drift = drift, Fade = fade });
        }

        void DamageNumber(Vector3 pos, int value, Color color, float size)
        {
            if (value <= 0) return;
            Popup(pos + new Vector3(UnityEngine.Random.Range(-0.15f, 0.15f), 0.1f, 0), value.ToString(), color, size, 0.65f);
        }

        void Popup(Vector3 pos, string text, Color color, float size, float life)
        {
            TextMesh tm;
            if (_popupPool.Count > 0) { tm = _popupPool.Pop(); tm.gameObject.SetActive(true); }
            else
            {
                var go = new GameObject("Popup"); go.transform.SetParent(_fxRoot, false);
                tm = go.AddComponent<TextMesh>();
                tm.font = UiKit.Font; tm.fontSize = 96; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.fontStyle = FontStyle.Bold;
                var mr = go.GetComponent<MeshRenderer>(); mr.material = UiKit.Font.material; mr.sortingOrder = 30;
            }
            if (_popups.Count > 60) { var old = _popups[0]; old.T.gameObject.SetActive(false); _popupPool.Push(old.T); _popups.RemoveAt(0); }
            tm.text = text; tm.characterSize = size * 48f / 96f; tm.color = color;
            tm.transform.position = pos + new Vector3(0, 0, -1f);
            _popups.Add(new PopupText { T = tm, Life = life, Start = pos + new Vector3(0, 0, -1f), Color = color });
        }

        // ─────────────────────────── 유닛 ───────────────────────────

        void EnsureCreep(Creep c)
        {
            if (_creeps.ContainsKey(c.Id) || !c.Alive) return;
            var root = new GameObject(c.Def.Name).transform;
            root.SetParent(_root, false);
            bool big = c.Def.Rarity == Rarity.Hero;
            float size = big ? 1.0f : 0.75f;
            var v = new CreepVisual { Creep = c, Root = root, Jitter = ((c.Id % 3) - 1) * 0.14f, Size = size, WalkClock = (c.Id % 7) * 0.05f };
            v.Frames = new[] { Art.Creep(c.Def.Id, 0), Art.Creep(c.Def.Id, 1) };
            v.Shadow = Sprite(root, "Shadow", Art.Get("fx_shadow"), new Color(1, 1, 1, 0.7f), 8);
            v.Shadow.transform.localPosition = new Vector3(0, c.Def.Flying ? -0.42f : -0.3f, 0.1f);
            v.Shadow.transform.localScale = Vector3.one * (big ? 1.4f : 1f);
            v.Body = Sprite(root, "Body", v.Frames[0], Color.white, 10);
            if (c.Boss && !big) v.Body.transform.localScale = Vector3.one * 1.25f;
            float barW = big ? 0.9f : 0.6f;
            v.HpBack = Square(root, "HpBack", barW, 0.07f, new Color(0, 0, 0, 0.6f), 11); v.HpBack.transform.localPosition = new Vector3(0, size / 2 + 0.08f, 0);
            v.HpBar = Square(root, "HpBar", barW, 0.07f, UiKit.Green, 12); v.HpBar.transform.localPosition = v.HpBack.transform.localPosition;
            v.Status = Sprite(root, "Status", null, Color.white, 12); v.Status.transform.localPosition = new Vector3(-0.2f, size / 2 + 0.28f, 0); v.Status.enabled = false;
            v.Status2 = Sprite(root, "Status2", null, Color.white, 12); v.Status2.transform.localPosition = new Vector3(0.2f, size / 2 + 0.28f, 0); v.Status2.enabled = false;
            if (c.Boss) { var lbl = TextLabel(root, c.Def.Name, 0.038f, new Vector3(0, -size / 2 - 0.02f, 0), TextAnchor.UpperCenter, 13); lbl.color = new Color(1f, 0.85f, 0.4f); }
            v.Prev = v.Curr = new Vector2(c.X / 1000f, c.Y / 1000f);
            ApplyCreep(v, 1f);
            _creeps[c.Id] = v;
        }

        void ApplyCreep(CreepVisual v, float t)
        {
            var c = v.Creep;
            var p = Vector2.Lerp(v.Prev, v.Curr, t);
            float bob = c.Def.Flying ? Mathf.Sin(_time * 5f + c.Id) * 0.08f + 0.25f : 0f;
            v.Root.position = W(p.x, p.y + v.Jitter, 0);
            int frame = Mathf.FloorToInt(v.WalkClock / (c.Def.Flying ? 0.12f : 0.18f)) % 2;
            v.Body.sprite = v.Frames[frame];
            float squash = v.Flash > 0 ? 0.85f : 1f;
            float baseScale = c.Boss && c.Def.Rarity != Rarity.Hero ? 1.25f : 1f;
            v.Body.transform.localScale = new Vector3(baseScale * (2f - squash), baseScale * squash, 1);
            v.Body.transform.localPosition = new Vector3(0, bob + (frame == 1 && !c.Def.Flying ? 0.03f : 0f), 0);
            float ratio = c.MaxHp > 0 ? Mathf.Clamp01(c.Hp / (float)c.MaxHp) : 0;
            float barW = v.HpBack.transform.localScale.x;
            v.HpBar.transform.localScale = new Vector3(barW * ratio, 0.07f, 1);
            v.HpBar.transform.localPosition = new Vector3(-barW * (1 - ratio) / 2, v.HpBack.transform.localPosition.y, 0);
            v.HpBar.color = ratio > 0.5f ? UiKit.Green : ratio > 0.25f ? UiKit.Gold : UiKit.Red;
            var col = Color.white;
            bool hidden = Sim.Mod.FogHalfLane && c.FromEnemy && IsMine && c.X < Sim.Cfg.Length * 500;
            if (hidden) col.a = 0f;
            else if (c.Stealthed) col.a = 0.35f;
            if (v.Flash > 0) col = new Color(1f, 0.55f, 0.55f, col.a);
            else if (c.SlowLeft > 0) col = new Color(0.7f, 0.85f, 1f, col.a);
            v.Body.color = col;
            v.Shadow.enabled = !hidden;
            v.HpBar.enabled = v.HpBack.enabled = !hidden;
            SetStatus(v.Status, c.SlowLeft > 0 && !c.Def.SlowImmune ? "fx_slow" : c.Stealthed ? "fx_stealth" : null, hidden);
            SetStatus(v.Status2, c.BurnLeft > 0 ? "fx_burn" : c.GroupNameBonus || c.GroupTribeBonus ? "icon_star" : null, hidden);
        }

        static void SetStatus(SpriteRenderer r, string sprite, bool hidden)
        {
            if (sprite == null || hidden) { r.enabled = false; return; }
            r.enabled = true; r.sprite = Art.Get(sprite);
        }

        // ─────────────────────────── 타워 ───────────────────────────

        void EnsureTower(Tower t)
        {
            if (_towers.ContainsKey(t.Id) || !t.Alive) return;
            var root = new GameObject(t.Def.Name).transform;
            root.SetParent(_root, false);
            root.position = W(t.X / 1000f, t.Y / 1000f);
            var v = new TowerVisual { Tower = t, Root = root, Clock = (t.Id % 5) * 0.1f };
            v.Idle0 = Art.Tower(t.Def.Id, 0); v.Idle1 = Art.Tower(t.Def.Id, 1); v.Attack = Art.TowerAttack(t.Def.Id);
            v.Shadow = Sprite(root, "Shadow", Art.Get("fx_shadow"), new Color(1, 1, 1, 0.55f), 4);
            v.Shadow.transform.localPosition = new Vector3(0, -0.3f, 0.1f);
            v.BodyRoot = new GameObject("BodyRoot").transform; v.BodyRoot.SetParent(root, false);
            v.Body = Sprite(v.BodyRoot, "Body", v.Idle0, Color.white, 5);
            v.Star = Sprite(root, "Star", null, Color.white, 6); v.Star.transform.localPosition = new Vector3(0, 0.46f, 0); v.Star.enabled = false;
            v.Badge = Sprite(root, "Badge", Art.Get("icon_sword"), Color.white, 6); v.Badge.transform.localPosition = new Vector3(0.32f, 0.3f, 0); v.Badge.transform.localScale = Vector3.one * 0.8f; v.Badge.enabled = false;
            v.BuildBack = Square(root, "BuildBack", 0.7f, 0.08f, new Color(0, 0, 0, 0.6f), 6); v.BuildBack.transform.localPosition = new Vector3(0, -0.36f, 0);
            v.BuildBar = Square(root, "BuildBar", 0.7f, 0.08f, UiKit.Gold, 7); v.BuildBar.transform.localPosition = v.BuildBack.transform.localPosition;
            v.Silence = Sprite(root, "Silence", Art.Get("fx_silence"), Color.white, 6); v.Silence.transform.localPosition = new Vector3(-0.3f, 0.3f, 0); v.Silence.enabled = false;
            if (t.Owner > 0)
            {
                v.OwnerLabel = TextLabel(root, $"P{t.Owner + 1}", 0.032f, new Vector3(-0.34f, -0.28f, 0), TextAnchor.MiddleCenter, 7);
                v.OwnerLabel.color = t.Owner == 1 ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.75f, 0.9f);
            }
            _towers[t.Id] = v;
            ApplyTower(v);
        }

        void ApplyTower(TowerVisual v)
        {
            var t = v.Tower;
            bool building = t.BuildLeft > 0;
            v.BuildBar.enabled = v.BuildBack.enabled = building;
            if (building)
            {
                float progress = 1f - t.BuildLeft / (float)Mathf.Max(1, Sim.Cfg.BuildSeconds * Sim.Cfg.TicksPerSecond);
                v.BuildBar.transform.localScale = new Vector3(0.7f * progress, 0.08f, 1);
                v.BuildBar.transform.localPosition = new Vector3(-0.7f * (1 - progress) / 2, -0.36f, 0);
                v.Body.color = new Color(1, 1, 1, 0.55f);
                v.Body.sprite = v.Idle0;
                v.BodyRoot.localScale = new Vector3(1f, 0.6f + 0.4f * progress, 1f);
                v.WasBuilding = true;
            }
            else
            {
                if (v.WasBuilding) { v.WasBuilding = false; if (_fxSmoke != null) Fx(_fxSmoke, v.Root.position + new Vector3(0, -0.15f, -0.2f), 0.07f, 1f, new Vector3(0, 0.4f, 0), true); }
                v.Body.color = t.SilenceLeft > 0 ? new Color(0.6f, 0.6f, 0.75f) : Color.white;
                if (v.AttackLeft > 0)
                {
                    v.Body.sprite = v.Attack;
                    float k = v.AttackLeft / 0.16f;
                    v.BodyRoot.localScale = new Vector3(1f + 0.12f * k, 1f - 0.08f * k, 1f);
                    v.BodyRoot.localPosition = new Vector3(-0.04f * k, 0, 0);
                }
                else
                {
                    bool blink = Mathf.FloorToInt(v.Clock / 0.55f) % 2 == 1;
                    v.Body.sprite = blink ? v.Idle1 : v.Idle0;
                    float breathe = 1f + Mathf.Sin(v.Clock * 3f) * 0.015f;
                    v.BodyRoot.localScale = new Vector3(1f, breathe, 1f);
                    v.BodyRoot.localPosition = Vector3.zero;
                }
            }
            v.Star.enabled = t.Star >= 2;
            if (t.Star >= 2) v.Star.sprite = Art.Get(t.Star >= 3 ? "star_3" : "star_2");
            v.Badge.enabled = t.Upgraded;
            v.Silence.enabled = t.SilenceLeft > 0;
        }

        // ─────────────────────────── 바닥 ───────────────────────────

        void BuildGround(LaneConfig cfg, string title)
        {
            var grass0 = Art.Get("tile_grass_0"); var grass1 = Art.Get("tile_grass_1"); var path = Art.Get("tile_path"); var slot = Art.Get("tile_slot");
            float tile = 0.5f; // 32px @ PPU 64
            var rng = new System.Random(IsMine ? 1 : 2);
            var groundRoot = new GameObject("Ground").transform; groundRoot.SetParent(_root, false);
            // 잔디: 라인 둘레 여백 포함
            for (float x = -1.5f; x < cfg.Length + 2.5f; x += tile)
                for (float y = -1.0f; y < cfg.Width + 1.5f; y += tile)
                {
                    bool onPath = x >= 0f && x < cfg.Length + 0.5f && y >= 0f && y < cfg.Width;
                    var sr = Sprite(groundRoot, "Tile", onPath ? path : (rng.Next(5) == 0 ? grass1 : grass0), Color.white, 0);
                    sr.transform.position = W(x + tile / 2, y + tile / 2, 2f);
                    if (onPath) sr.color = new Color(1f, 1f, 1f, 1f);
                }
            for (int c = 0; c < cfg.Width; c++)
                for (int r = 0; r < cfg.Rows; r++)
                {
                    var s = Sprite(groundRoot, "Slot", slot, IsMine ? Color.white : new Color(0.85f, 0.85f, 0.9f), 1);
                    s.transform.position = SlotCenter(c, r, 1.5f);
                    s.transform.localScale = Vector3.one * 1.9f; // 32px → 0.95칸
                    _slots[(c, r)] = s;
                }
            _hoverSlot = Sprite(groundRoot, "Hover", Art.Get("tile_slot_hover"), Color.white, 2); _hoverSlot.transform.localScale = Vector3.one * 1.9f; _hoverSlot.enabled = false;
            _selectSlot = Sprite(groundRoot, "Select", Art.Get("tile_slot_hover"), new Color(1f, 0.9f, 0.5f), 2); _selectSlot.transform.localScale = Vector3.one * 1.9f; _selectSlot.enabled = false;
            _rangeRing = Sprite(groundRoot, "Range", Art.Ring, new Color(1f, 0.95f, 0.6f, 0.9f), 3); _rangeRing.enabled = false;
            // 성문 (출발), 기지 (도착)
            _gate = Sprite(groundRoot, "Gate", Art.Get("gate"), Color.white, 2);
            _gate.transform.position = W(-0.1f, cfg.Width / 2f, 1f);
            _gate.transform.localScale = Vector3.one * Mathf.Max(1f, cfg.Width / 4f);
            _baseRoot = new GameObject("BaseRoot").transform; _baseRoot.SetParent(_root, false); _baseAnchor = W(cfg.Length + 0.6f, cfg.Width / 2f, 1f); _baseRoot.position = _baseAnchor;
            _base = Sprite(_baseRoot, "Base", Art.Get("base"), Color.white, 2);
            _base.transform.localScale = Vector3.one * Mathf.Max(1.1f, cfg.Width / 3.6f);
            var flag = Sprite(_baseRoot, "Flag", Art.Get(IsMine ? "flag_blue" : "flag_red"), Color.white, 3);
            flag.transform.localPosition = new Vector3(0.05f, 0.7f * Mathf.Max(1.1f, cfg.Width / 3.6f), -0.1f);
            // 제목
            var l = TextLabel(_root, title, 0.075f, W(0.1f, cfg.Width + 0.45f), TextAnchor.MiddleLeft, 5);
            l.anchor = TextAnchor.MiddleLeft; l.alignment = TextAlignment.Left; l.color = IsMine ? new Color(0.85f, 0.95f, 1f) : new Color(1f, 0.85f, 0.85f);
        }

        // ─────────────────────────── 도우미 ───────────────────────────

        SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.color = color; sr.sortingOrder = order;
            return sr;
        }

        SpriteRenderer Square(Transform parent, string name, float w, float h, Color color, int order)
        {
            var sr = Sprite(parent, name, Art.Square, color, order);
            sr.transform.localScale = new Vector3(w, h, 1);
            return sr;
        }

        TextMesh TextLabel(Transform parent, string text, float charSize, Vector3 localPos, TextAnchor anchor, int order)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = UiKit.Font; tm.fontSize = 96; tm.characterSize = charSize * 40f / 96f; tm.anchor = anchor; tm.alignment = TextAlignment.Center; tm.fontStyle = FontStyle.Bold;
            tm.text = text; tm.color = new Color(0.95f, 0.95f, 0.95f);
            var mr = go.GetComponent<MeshRenderer>(); mr.material = UiKit.Font.material; mr.sortingOrder = order;
            go.transform.localPosition = localPos;
            return tm;
        }

        public static Color TowerColor(TowerDef d) => d.Tribe switch
        {
            DefTribe.Forest => new Color(0.45f, 0.78f, 0.45f),
            DefTribe.Fire => new Color(0.95f, 0.55f, 0.35f),
            _ => new Color(0.55f, 0.68f, 0.90f),
        };

        public static Color CreepColor(AttackerDef d) => d.Tribe switch
        {
            AtkTribe.Beast => new Color(0.85f, 0.35f, 0.35f),
            AtkTribe.Air => new Color(0.75f, 0.45f, 0.85f),
            AtkTribe.Giant => new Color(0.6f, 0.4f, 0.3f),
            _ => new Color(0.4f, 0.28f, 0.5f),
        };

        public static Sprite MakeSquare() => Art.Square;
    }
}
