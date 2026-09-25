using System.Collections.Generic;
using LaneBattle.Core.Wave;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>
    /// 라인 시뮬레이션(LaneSim)을 월드 공간에 그린다. 시뮬레이션은 순수 C#, 여기서는 보간·투사체·체력바·입력만.
    /// 1단계 프로토타입: 실시간으로 빈 슬롯을 눌러 타워를 짓고, 버튼으로 "상대가 보냄"을 흉내 내며 싸우는 모습을 본다.
    /// </summary>
    public sealed class WaveView : MonoBehaviour
    {
        public LaneSim Sim { get; private set; }
        public int PresetIndex = 1;
        public ulong Seed = 1;
        public float Speed = 1f;
        public int SelectedTowerId = 1;

        Sprite _square;
        Transform _world;
        readonly Dictionary<int, CreepVisual> _creeps = new Dictionary<int, CreepVisual>();
        readonly Dictionary<int, TowerVisual> _towers = new Dictionary<int, TowerVisual>();
        readonly List<Projectile> _projectiles = new List<Projectile>();
        readonly List<FadeOut> _fades = new List<FadeOut>();
        SpriteRenderer _base;
        float _baseFlash, _bannerLeft, _accumulator;
        Text _hud, _banner, _selectedInfo;
        readonly List<Button> _towerButtons = new List<Button>();
        bool _paused;

        sealed class CreepVisual { public Creep Creep; public Transform Root; public SpriteRenderer Body, HpBar, HpBack; public Vector2 Prev, Curr; public float Jitter; }
        sealed class TowerVisual { public Tower Tower; public Transform Root; public SpriteRenderer Body, Status, Outline; public TextMesh Label; }
        sealed class Projectile { public Transform T; public Vector3 From, To; public float Age, Life; }
        sealed class FadeOut { public Transform T; public float Age; }

        // ─────────────────────────── 수명 ───────────────────────────

        void Awake()
        {
            _square = MakeSquare();
            BuildHud();
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-preset" && int.TryParse(args[i + 1], out int idx)) PresetIndex = Mathf.Clamp(idx, 0, WaveScenarios.Presets.Length - 1);
            Restart();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-send" && int.TryParse(args[i + 1], out int sendIdx)) EnemySend(Mathf.Clamp(sendIdx, 0, WaveScenarios.EnemySends.Length - 1));
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-wavetime" && float.TryParse(args[i + 1], out float sec)) FastForward(sec);
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshot") StartCoroutine(ScreenshotAndQuit(args[i + 1]));
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            _paused = true;
            yield return new WaitForSeconds(1.0f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1.5f);
            Application.Quit();
        }

        public void Restart()
        {
            if (_world != null) Destroy(_world.gameObject);
            _creeps.Clear(); _towers.Clear(); _projectiles.Clear(); _fades.Clear();
            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            Sim = WaveScenarios.Build(WaveScenarios.Presets[PresetIndex], Seed);
            BuildCamera(Sim.Cfg);
            BuildLane(Sim.Cfg);
            foreach (var t in Sim.Towers) EnsureTower(t);
            _accumulator = 0; _paused = false; _bannerLeft = 0;
            RefreshHud();
        }

        public void FastForward(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds * Sim.Cfg.TicksPerSecond);
            for (int i = 0; i < ticks && !Sim.IsOver; i++) StepOnce();
            foreach (var v in _creeps.Values) { v.Prev = v.Curr; ApplyCreep(v, 1f); }
            foreach (var v in _towers.Values) ApplyTower(v);
        }

        void Update()
        {
            if (Sim == null) return;
            float dt = Time.deltaTime;
            HandleClick();
            if (!_paused && !Sim.IsOver)
            {
                _accumulator += dt * Speed;
                float tickLen = 1f / Sim.Cfg.TicksPerSecond;
                int steps = 0;
                while (_accumulator >= tickLen && steps++ < 8) { _accumulator -= tickLen; StepOnce(); }
                float t = Mathf.Clamp01(_accumulator / tickLen);
                foreach (var v in _creeps.Values) ApplyCreep(v, t);
                foreach (var v in _towers.Values) ApplyTower(v);
            }
            UpdateProjectiles(dt);
            UpdateFades(dt);
            if (_baseFlash > 0) { _baseFlash -= dt; _base.color = Color.Lerp(new Color(0.35f, 0.4f, 0.6f), new Color(1f, 0.3f, 0.3f), Mathf.Clamp01(_baseFlash * 3)); }
            if (_bannerLeft > 0) { _bannerLeft -= dt; if (_bannerLeft <= 0) _banner.text = ""; }
            RefreshHud();
        }

        void StepOnce()
        {
            foreach (var v in _creeps.Values) v.Prev = v.Curr;
            Sim.Step();
            foreach (var c in Sim.Creeps) EnsureCreep(c);
            foreach (var t in Sim.Towers) EnsureTower(t);
            foreach (var v in _creeps.Values) v.Curr = new Vector2(v.Creep.X / 1000f, v.Creep.Y / 1000f);
            foreach (var ev in Sim.Events) HandleEvent(ev);
        }

        // ─────────────────────────── 입력 ───────────────────────────

        void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || Sim.IsOver) return;
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
            if (!left && !right) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var cam = Camera.main; if (cam == null) return;
            var w = cam.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 10));
            int row = Mathf.FloorToInt(w.x) - Sim.Cfg.GridStartX;
            int col = Mathf.FloorToInt(w.y);
            if (row < 0 || row >= Sim.Cfg.Rows || col < 0 || col >= Sim.Cfg.Width) return;
            var existing = Sim.TowerAt(col, row);
            if (left)
            {
                if (existing == null) Sim.Build(WaveCatalog.Tower(SelectedTowerId), col, row);
                else Sim.Upgrade(existing.Id);
            }
            else if (existing != null) Sim.Sell(existing.Id);
            foreach (var t in Sim.Towers) EnsureTower(t);
            foreach (var ev in Sim.Events) HandleEvent(ev);
        }

        public void EnemySend(int index)
        {
            var (_, id, count) = WaveScenarios.EnemySends[index];
            for (int i = 0; i < count; i++) Sim.Send(WaveCatalog.Attacker(id), true, index + 1);
            ShowBanner($"상대가 {WaveCatalog.Attacker(id).Name} {count}마리를 보냈습니다!");
        }

        // ─────────────────────────── 이벤트 → 연출 ───────────────────────────

        void HandleEvent(SimEvent ev)
        {
            switch (ev.Type)
            {
                case SimEventType.WaveStart:
                    ShowBanner($"웨이브 {ev.A + 1}: {WaveCatalog.Describe(ev.A)}");
                    break;
                case SimEventType.Attack:
                    if (_towers.TryGetValue(ev.A, out var tv) && _creeps.TryGetValue(ev.B, out var cv)) SpawnProjectile(tv.Root.position, cv.Root.position, new Color(1f, 0.95f, 0.6f));
                    break;
                case SimEventType.Death:
                    if (_creeps.TryGetValue(ev.B, out var dead)) { _fades.Add(new FadeOut { T = dead.Root }); _creeps.Remove(ev.B); }
                    break;
                case SimEventType.Leak:
                    if (_creeps.TryGetValue(ev.A, out var leaker)) { Destroy(leaker.Root.gameObject); _creeps.Remove(ev.A); }
                    _baseFlash = 0.5f;
                    break;
                case SimEventType.Sold:
                    if (_towers.TryGetValue(ev.A, out var sold)) { Destroy(sold.Root.gameObject); _towers.Remove(ev.A); }
                    break;
                case SimEventType.MatchEnd:
                    ShowBanner(Sim.BaseHp <= 0 ? "기지 파괴! 패배" : $"10분 종료. 남은 기지 체력 {Sim.BaseHp}");
                    _bannerLeft = 999f;
                    break;
            }
        }

        void ShowBanner(string text) { _banner.text = text; _bannerLeft = 2.5f; }

        void SpawnProjectile(Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(_world, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _square; sr.color = color; sr.sortingOrder = 20;
            go.transform.localScale = Vector3.one * 0.13f;
            go.transform.position = from;
            _projectiles.Add(new Projectile { T = go.transform, From = from, To = to, Life = 0.12f });
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                p.Age += dt;
                float t = Mathf.Clamp01(p.Age / p.Life);
                p.T.position = Vector3.Lerp(p.From, p.To, t);
                if (t >= 1f) { Destroy(p.T.gameObject); _projectiles.RemoveAt(i); }
            }
        }

        void UpdateFades(float dt)
        {
            for (int i = _fades.Count - 1; i >= 0; i--)
            {
                var f = _fades[i];
                f.Age += dt;
                float t = Mathf.Clamp01(f.Age / 0.3f);
                if (f.T != null) f.T.localScale = Vector3.one * (1f - t);
                if (t >= 1f) { if (f.T != null) Destroy(f.T.gameObject); _fades.RemoveAt(i); }
            }
        }

        // ─────────────────────────── 시각물 ───────────────────────────

        void EnsureCreep(Creep c)
        {
            if (_creeps.ContainsKey(c.Id) || !c.Alive) return;
            var root = new GameObject(c.Def.Name).transform;
            root.SetParent(_world, false);
            var v = new CreepVisual { Creep = c, Root = root, Jitter = ((c.Id % 3) - 1) * 0.16f };
            float size = c.Boss ? 0.8f : 0.46f;
            v.Body = Square(root, "Body", size, size, CreepColor(c), 10);
            if (c.Def.Flying) { var wing = Square(root, "Wing", size + 0.3f, size * 0.35f, new Color(1, 1, 1, 0.35f), 9); wing.transform.localPosition = new Vector3(0, 0.05f, 0); }
            v.HpBack = Square(root, "HpBack", size, 0.07f, new Color(0, 0, 0, 0.6f), 11); v.HpBack.transform.localPosition = new Vector3(0, size / 2 + 0.06f, 0);
            v.HpBar = Square(root, "HpBar", size, 0.07f, Color.green, 12); v.HpBar.transform.localPosition = v.HpBack.transform.localPosition;
            var label = TextLabel(root, c.Def.Name, 0.034f, new Vector3(0, -size / 2 - 0.02f, 0), TextAnchor.UpperCenter, 13);
            v.Prev = v.Curr = new Vector2(c.X / 1000f, c.Y / 1000f);
            ApplyCreep(v, 1f);
            _creeps[c.Id] = v;
        }

        void ApplyCreep(CreepVisual v, float t)
        {
            var p = Vector2.Lerp(v.Prev, v.Curr, t);
            v.Root.position = new Vector3(p.x, p.y + v.Jitter, 0);
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
            root.SetParent(_world, false);
            root.position = new Vector3(t.X / 1000f, t.Y / 1000f, 0);
            var v = new TowerVisual { Tower = t, Root = root };
            v.Outline = Square(root, "Outline", 0.74f, 0.74f, new Color(1, 1, 1, 0), 9);
            v.Body = Square(root, "Body", 0.62f, 0.62f, TowerColor(t.Def), 10);
            v.Status = Square(root, "Status", 0.62f, 0.62f, new Color(0, 0, 0, 0), 11);
            v.Label = TextLabel(root, t.Def.Name, 0.036f, new Vector3(0, -0.33f, 0), TextAnchor.UpperCenter, 13);
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

        static Color TowerColor(TowerDef d) => d.Tribe switch
        {
            DefTribe.Forest => new Color(0.45f, 0.78f, 0.45f),
            DefTribe.Fire => new Color(0.95f, 0.55f, 0.35f),
            _ => new Color(0.55f, 0.68f, 0.90f),
        };

        static Color CreepColor(Creep c) => c.Def.Tribe switch
        {
            AtkTribe.Beast => new Color(0.85f, 0.35f, 0.35f),
            AtkTribe.Air => new Color(0.75f, 0.45f, 0.85f),
            AtkTribe.Giant => new Color(0.6f, 0.4f, 0.3f),
            _ => new Color(0.4f, 0.28f, 0.5f),
        };

        void BuildLane(LaneConfig cfg)
        {
            var bg = new GameObject("LaneBg"); bg.transform.SetParent(_world, false);
            var bgSr = bg.AddComponent<SpriteRenderer>(); bgSr.sprite = _square; bgSr.color = new Color(0.16f, 0.18f, 0.22f); bgSr.sortingOrder = 0;
            bg.transform.position = new Vector3(cfg.Length / 2f, cfg.Width / 2f, 1); bg.transform.localScale = new Vector3(cfg.Length + 1, cfg.Width + 0.6f, 1);
            for (int c = 0; c < cfg.Width; c++)
                for (int r = 0; r < cfg.Rows; r++)
                {
                    var slot = Square(_world, "Slot", 0.9f, 0.9f, new Color(0.24f, 0.27f, 0.33f), 1);
                    slot.transform.position = new Vector3(cfg.GridStartX + r + 0.5f, c + 0.5f, 0.5f);
                }
            var spawn = Square(_world, "Spawn", 0.3f, cfg.Width, new Color(0.5f, 0.25f, 0.25f), 1); spawn.transform.position = new Vector3(0, cfg.Width / 2f, 0.5f);
            _base = Square(_world, "Base", 0.6f, cfg.Width, new Color(0.35f, 0.4f, 0.6f), 1); _base.transform.position = new Vector3(cfg.Length + 0.3f, cfg.Width / 2f, 0.5f);
            TextLabel(_world, "← 상대 유닛 출발", 0.055f, new Vector3(0.2f, cfg.Width + 0.35f, 0), TextAnchor.MiddleLeft, 5).anchor = TextAnchor.MiddleLeft;
            TextLabel(_world, "타워 슬롯: 빈 칸 클릭 = 짓기, 다시 클릭 = 강화, 우클릭 = 판매", 0.05f, new Vector3(cfg.GridStartX + 1.5f, cfg.Width + 0.35f, 0), TextAnchor.MiddleCenter, 5);
            TextLabel(_world, "내 기지 →", 0.055f, new Vector3(cfg.Length + 0.3f, cfg.Width + 0.35f, 0), TextAnchor.MiddleRight, 5);
        }

        Sprite MakeSquare()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        void BuildCamera(LaneConfig lane)
        {
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera", typeof(Camera)); go.tag = "MainCamera"; cam = go.GetComponent<Camera>(); }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.transform.position = new Vector3(lane.Length / 2f + 0.3f, lane.Width / 2f - 1.2f, -10);
            cam.orthographicSize = Mathf.Max((lane.Length + 3f) / cam.aspect / 2f, lane.Width / 2f + 3f);
        }

        // ─────────────────────────── HUD ───────────────────────────

        void BuildHud()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);
            var root = canvasGo.transform;

            _hud = UiKit.Label(root, "Hud", 20, 10, 1240, 50, "", 17, TextAnchor.UpperLeft, UiKit.Accent);
            _banner = UiKit.Label(root, "Banner", 0, 62, 1280, 34, "", 22, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.4f));

            // 타워 선택
            UiKit.Label(root, "TowerTitle", 20, 500, 400, 22, "지을 타워 (클릭해서 선택)", 13);
            float x = 20;
            foreach (var d in WaveCatalog.Towers)
            {
                int id = d.Id;
                var b = UiKit.ButtonBox(root, "T" + id, x, 524, 128, 44, $"{d.Name}\n{d.Cost}골드 · 공{d.Atk} 사{d.Range10 / 10f:0.#}", () => { SelectedTowerId = id; RefreshTowerButtons(); }, TowerColor(d), 11);
                foreach (var txt in b.GetComponentsInChildren<Text>()) txt.color = Color.black;
                _towerButtons.Add(b);
                x += 134;
            }
            _selectedInfo = UiKit.Label(root, "Sel", 20, 572, 1240, 20, "", 12);

            // 상대가 보냄
            UiKit.Label(root, "SendTitle", 20, 598, 400, 22, "상대가 보냄 (내 라인에 들어옴)", 13);
            x = 20;
            for (int i = 0; i < WaveScenarios.EnemySends.Length; i++)
            {
                int idx = i;
                var (label, atkId, _) = WaveScenarios.EnemySends[i];
                UiKit.ButtonBox(root, "S" + i, x, 622, 112, 36, label, () => EnemySend(idx), null, 12);
                x += 118;
            }

            // 프리셋 / 배속
            x = 20;
            for (int i = 0; i < WaveScenarios.Presets.Length; i++)
            {
                int idx = i;
                var name = WaveScenarios.Presets[i].Name;
                float w = 30 + name.Length * 11;
                UiKit.ButtonBox(root, "P" + i, x, 668, w, 36, name, () => { PresetIndex = idx; Restart(); }, null, 12);
                x += w + 8;
            }
            UiKit.ButtonBox(root, "Again", 760, 668, 110, 36, "다시 (새 시드)", () => { Seed++; Restart(); }, null, 12);
            UiKit.ButtonBox(root, "Pause", 880, 668, 90, 36, "일시정지", () => _paused = !_paused, null, 12);
            UiKit.ButtonBox(root, "S1", 980, 668, 60, 36, "1배", () => Speed = 1f, null, 12);
            UiKit.ButtonBox(root, "S2", 1050, 668, 60, 36, "2배", () => Speed = 2f, null, 12);
            UiKit.ButtonBox(root, "S4", 1120, 668, 60, 36, "4배", () => Speed = 4f, null, 12);
            UiKit.ButtonBox(root, "S0", 1190, 668, 70, 36, "0.25배", () => Speed = 0.25f, null, 12);
            RefreshTowerButtons();
        }

        void RefreshTowerButtons()
        {
            for (int i = 0; i < _towerButtons.Count; i++)
            {
                var d = WaveCatalog.Towers[i];
                var img = _towerButtons[i].GetComponent<Image>();
                var c = TowerColor(d);
                img.color = d.Id == SelectedTowerId ? c : new Color(c.r, c.g, c.b, 0.45f);
            }
            var sel = WaveCatalog.Tower(SelectedTowerId);
            string extra = sel.AntiAir ? "대공 가능" : "대공 불가";
            if (sel.SplashRadius10 > 0) extra += " · 광역";
            if (sel.BurnPerSec > 0) extra += " · 화상";
            if (sel.SlowPercent > 0) extra += " · 감속";
            if (sel.AuraAtkPercent > 0) extra += " · 주변 타워 공격 +15%";
            if (sel.AuraSpeedPercentMachine > 0) extra += " · 주변 기계 타워 공속 +25%";
            if (sel.AirMultiplier > 1) extra += " · 공중에 2배";
            if (sel.GiantMultiplier > 1) extra += " · 거인에 2배";
            if (_selectedInfo != null) _selectedInfo.text = $"선택: {sel.Name} — {extra}   (초록=숲, 주황=불, 파랑=기계)";
        }

        void RefreshHud()
        {
            if (_hud == null || Sim == null) return;
            int sec = Sim.Seconds;
            int next = Sim.TicksToNextWave / Sim.Cfg.TicksPerSecond;
            string nextWave = Sim.NextWaveIndex < WaveCatalog.BaseWaves.Length ? $"다음 웨이브 {Sim.NextWaveIndex + 1} ({next}초 후): {WaveCatalog.Describe(Sim.NextWaveIndex)}" : "기본 웨이브 끝";
            _hud.text = $"{sec / 60}:{sec % 60:00} / {Sim.Cfg.MatchSeconds / 60}:00   |   기지 체력 {Sim.BaseHp} / {Sim.Cfg.BaseHp}   |   처치 {Sim.Kills}   누수 {Sim.Leaked}   |   {Speed}배속{(_paused ? " (일시정지)" : "")}\n{nextWave}";
        }
    }
}
