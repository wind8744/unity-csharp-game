using System.Collections.Generic;
using LaneBattle.Core.Wave;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>
    /// 웨이브 시뮬레이션을 월드 공간에 그린다. 시뮬레이션은 순수 C#(WaveSim), 여기서는 보간·투사체·체력바만 담당.
    /// 1단계 프로토타입: 시나리오를 고르고 싸우는 모습을 본다.
    /// </summary>
    public sealed class WaveView : MonoBehaviour
    {
        public WaveSim Sim { get; private set; }
        public int ScenarioIndex = 1;
        public ulong Seed = 1;
        public float Speed = 1f;

        Sprite _square;
        Transform _world;
        readonly Dictionary<int, UnitVisual> _visuals = new Dictionary<int, UnitVisual>();
        readonly List<Projectile> _projectiles = new List<Projectile>();
        readonly List<FadeOut> _fades = new List<FadeOut>();
        SpriteRenderer _base;
        float _baseFlash;
        float _accumulator;
        Text _hud, _legend;
        Canvas _canvas;
        bool _paused;

        sealed class UnitVisual
        {
            public SimUnit Unit;
            public Transform Root;
            public SpriteRenderer Body, HpBar, HpBack, Wing;
            public TextMesh Label;
            public Vector2 Prev, Curr;
        }

        sealed class Projectile { public Transform T; public Vector3 From, To; public float Age, Life; }
        sealed class FadeOut { public Transform T; public float Age; }

        // ─────────────────────────── 수명 ───────────────────────────

        void Awake()
        {
            _square = MakeSquare();
            BuildCamera();
            BuildHud();
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-wave" && int.TryParse(args[i + 1], out int idx)) ScenarioIndex = Mathf.Clamp(idx, 0, WaveScenarios.All.Length - 1);
            Restart();
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
            _visuals.Clear(); _projectiles.Clear(); _fades.Clear();
            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            var sc = WaveScenarios.All[ScenarioIndex];
            Sim = WaveScenarios.Build(sc, Seed);
            BuildLane(sc.Lane);
            foreach (var u in Sim.Units) EnsureVisual(u);
            _accumulator = 0;
            _paused = false;
            RefreshHud();
        }

        /// <summary>검증용: 초 단위로 빨리 감기 (그리기 없이 시뮬만 진행 후 화면 갱신).</summary>
        public void FastForward(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds * Sim.Cfg.TicksPerSecond);
            for (int i = 0; i < ticks && !Sim.IsOver; i++) StepOnce();
            foreach (var v in _visuals.Values) { v.Prev = v.Curr; ApplyInterpolated(v, 1f); }
        }

        void Update()
        {
            if (Sim == null) return;
            float dt = Time.deltaTime;
            if (!_paused && !Sim.IsOver)
            {
                _accumulator += dt * Speed;
                float tickLen = 1f / Sim.Cfg.TicksPerSecond;
                int steps = 0;
                while (_accumulator >= tickLen && steps++ < 8) { _accumulator -= tickLen; StepOnce(); }
                float t = Mathf.Clamp01(_accumulator / tickLen);
                foreach (var v in _visuals.Values) ApplyInterpolated(v, t);
            }
            UpdateProjectiles(dt);
            UpdateFades(dt);
            if (_baseFlash > 0) { _baseFlash -= dt; _base.color = Color.Lerp(new Color(0.35f, 0.4f, 0.6f), new Color(1f, 0.3f, 0.3f), Mathf.Clamp01(_baseFlash * 3)); }
            RefreshHud();
        }

        void StepOnce()
        {
            foreach (var v in _visuals.Values) v.Prev = v.Curr;
            Sim.Step();
            foreach (var u in Sim.Units) EnsureVisual(u);
            foreach (var v in _visuals.Values) { v.Curr = new Vector2(v.Unit.X / 1000f, v.Unit.Y / 1000f); }
            foreach (var ev in Sim.Events) HandleEvent(ev);
        }

        // ─────────────────────────── 이벤트 → 연출 ───────────────────────────

        void HandleEvent(SimEvent ev)
        {
            switch (ev.Type)
            {
                case SimEventType.Attack:
                    if (_visuals.TryGetValue(ev.A, out var from) && _visuals.TryGetValue(ev.B, out var to))
                    {
                        bool ranged = from.Unit.RangeMilli >= 2000;
                        if (ranged) SpawnProjectile(from, to);
                        else Punch(from, to);
                    }
                    break;
                case SimEventType.Death:
                    if (_visuals.TryGetValue(ev.B, out var dead))
                    {
                        _fades.Add(new FadeOut { T = dead.Root, Age = 0 });
                        _visuals.Remove(ev.B);
                    }
                    break;
                case SimEventType.Leak:
                case SimEventType.Timeout:
                    if (_visuals.TryGetValue(ev.A, out var leaker)) { Destroy(leaker.Root.gameObject); _visuals.Remove(ev.A); }
                    _baseFlash = 0.5f;
                    break;
            }
        }

        void SpawnProjectile(UnitVisual from, UnitVisual to)
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(_world, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _square; sr.color = from.Unit.Kind == UnitKind.Defender ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 0.5f, 0.7f);
            sr.sortingOrder = 20;
            go.transform.localScale = Vector3.one * 0.14f;
            go.transform.position = from.Root.position + Vector3.up * 0.1f;
            _projectiles.Add(new Projectile { T = go.transform, From = go.transform.position, To = to.Root.position, Age = 0, Life = 0.12f });
        }

        void Punch(UnitVisual from, UnitVisual to)
        {
            var dir = (to.Root.position - from.Root.position).normalized;
            from.Body.transform.localPosition = dir * 0.18f; // Update 에서 서서히 복귀
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
            foreach (var v in _visuals.Values)
                v.Body.transform.localPosition = Vector3.Lerp(v.Body.transform.localPosition, Vector3.zero, dt * 12f);
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

        void EnsureVisual(SimUnit u)
        {
            if (_visuals.ContainsKey(u.Id) || !u.Alive) return;
            var root = new GameObject(u.Name).transform;
            root.SetParent(_world, false);
            var v = new UnitVisual { Unit = u, Root = root };

            var bodyGo = new GameObject("Body"); bodyGo.transform.SetParent(root, false);
            v.Body = bodyGo.AddComponent<SpriteRenderer>();
            v.Body.sprite = _square; v.Body.sortingOrder = 10;
            v.Body.color = UnitColor(u);
            float size = u.Boss ? 0.8f : u.Kind == UnitKind.Defender ? 0.62f : 0.46f;
            bodyGo.transform.localScale = new Vector3(size, size, 1);

            if (u.Flying)
            {
                var wingGo = new GameObject("Wing"); wingGo.transform.SetParent(root, false);
                v.Wing = wingGo.AddComponent<SpriteRenderer>();
                v.Wing.sprite = _square; v.Wing.sortingOrder = 9; v.Wing.color = new Color(1, 1, 1, 0.35f);
                wingGo.transform.localScale = new Vector3(size + 0.25f, size * 0.4f, 1);
                wingGo.transform.localPosition = new Vector3(0, 0.05f, 0);
            }

            var backGo = new GameObject("HpBack"); backGo.transform.SetParent(root, false);
            v.HpBack = backGo.AddComponent<SpriteRenderer>();
            v.HpBack.sprite = _square; v.HpBack.sortingOrder = 11; v.HpBack.color = new Color(0, 0, 0, 0.6f);
            backGo.transform.localScale = new Vector3(size, 0.07f, 1);
            backGo.transform.localPosition = new Vector3(0, size / 2 + 0.06f, 0);

            var barGo = new GameObject("HpBar"); barGo.transform.SetParent(root, false);
            v.HpBar = barGo.AddComponent<SpriteRenderer>();
            v.HpBar.sprite = _square; v.HpBar.sortingOrder = 12; v.HpBar.color = Color.green;
            barGo.transform.localScale = new Vector3(size, 0.07f, 1);
            barGo.transform.localPosition = new Vector3(0, size / 2 + 0.06f, 0);

            var labelGo = new GameObject("Label"); labelGo.transform.SetParent(root, false);
            v.Label = labelGo.AddComponent<TextMesh>();
            v.Label.font = UiKit.Font; v.Label.fontSize = 40; v.Label.characterSize = 0.038f;
            v.Label.anchor = TextAnchor.UpperCenter; v.Label.alignment = TextAlignment.Center;
            v.Label.text = u.Name; v.Label.color = new Color(0.95f, 0.95f, 0.95f);
            labelGo.GetComponent<MeshRenderer>().sortingOrder = 13;
            labelGo.GetComponent<MeshRenderer>().material = UiKit.Font.material;
            labelGo.transform.localPosition = new Vector3(0, -size / 2 - 0.02f, 0);

            v.Prev = v.Curr = new Vector2(u.X / 1000f, u.Y / 1000f);
            ApplyInterpolated(v, 1f);
            _visuals[u.Id] = v;
        }

        void ApplyInterpolated(UnitVisual v, float t)
        {
            var p = Vector2.Lerp(v.Prev, v.Curr, t);
            // 같은 열에 겹친 공격 유닛이 구분되도록 id 에 따라 살짝 위아래로 비껴 그린다 (시뮬레이션 좌표는 그대로)
            float jitter = v.Unit.Kind == UnitKind.Attacker ? ((v.Unit.Id % 3) - 1) * 0.16f : 0f;
            v.Root.position = new Vector3(p.x, p.y + jitter, 0);
            float ratio = v.Unit.MaxHp > 0 ? Mathf.Clamp01(v.Unit.Hp / (float)v.Unit.MaxHp) : 0;
            float size = v.HpBack.transform.localScale.x;
            v.HpBar.transform.localScale = new Vector3(size * ratio, 0.07f, 1);
            v.HpBar.transform.localPosition = new Vector3(-size * (1 - ratio) / 2, v.HpBack.transform.localPosition.y, 0);
            v.HpBar.color = Color.Lerp(Color.red, Color.green, ratio);
            if (v.Unit.State == UnitState.Attacking && v.Unit.Kind == UnitKind.Attacker) v.Body.color = Color.Lerp(UnitColor(v.Unit), Color.white, 0.25f);
            else v.Body.color = UnitColor(v.Unit);
        }

        static Color UnitColor(SimUnit u)
        {
            if (u.Kind == UnitKind.Defender)
                return u.Def.Tribe switch
                {
                    DefTribe.Forest => new Color(0.45f, 0.78f, 0.45f),
                    DefTribe.Fire => new Color(0.95f, 0.55f, 0.35f),
                    _ => new Color(0.55f, 0.68f, 0.90f),
                };
            return u.Atk.Tribe switch
            {
                AtkTribe.Beast => new Color(0.85f, 0.35f, 0.35f),
                AtkTribe.Air => new Color(0.75f, 0.45f, 0.85f),
                AtkTribe.Giant => new Color(0.6f, 0.4f, 0.3f),
                _ => new Color(0.35f, 0.25f, 0.45f),
            };
        }

        void BuildLane(LaneConfig cfg)
        {
            var bg = Quad("LaneBg", new Vector3(cfg.Length / 2f, cfg.Width / 2f, 1), new Vector3(cfg.Length + 1, cfg.Width + 0.6f, 1), new Color(0.16f, 0.18f, 0.22f), 0);
            for (int c = 0; c < cfg.Width; c++)
                for (int r = 0; r < cfg.Rows; r++)
                    Quad("Slot", new Vector3(cfg.GridStartX + r + 0.5f, c + 0.5f, 0.5f), new Vector3(0.9f, 0.9f, 1), new Color(0.24f, 0.27f, 0.33f), 1);
            Quad("Spawn", new Vector3(0, cfg.Width / 2f, 0.5f), new Vector3(0.3f, cfg.Width, 1), new Color(0.5f, 0.25f, 0.25f), 1);
            _base = Quad("Base", new Vector3(cfg.Length + 0.3f, cfg.Width / 2f, 0.5f), new Vector3(0.6f, cfg.Width, 1), new Color(0.35f, 0.4f, 0.6f), 1);
            Label("← 상대 유닛 출발", new Vector3(0.2f, cfg.Width + 0.35f, 0), TextAnchor.MiddleLeft);
            Label("방어 슬롯 (앞줄 → 뒷줄)", new Vector3(cfg.GridStartX + 1.5f, cfg.Width + 0.35f, 0), TextAnchor.MiddleCenter);
            Label("내 기지 →", new Vector3(cfg.Length + 0.3f, cfg.Width + 0.35f, 0), TextAnchor.MiddleRight);
        }

        SpriteRenderer Quad(string name, Vector3 pos, Vector3 scale, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_world, false);
            go.transform.position = pos; go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _square; sr.color = color; sr.sortingOrder = order;
            return sr;
        }

        void Label(string text, Vector3 pos, TextAnchor anchor)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(_world, false);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = UiKit.Font; tm.fontSize = 48; tm.characterSize = 0.06f; tm.anchor = anchor; tm.text = text;
            tm.color = new Color(0.8f, 0.82f, 0.9f);
            go.GetComponent<MeshRenderer>().material = UiKit.Font.material;
            go.GetComponent<MeshRenderer>().sortingOrder = 5;
        }

        Sprite MakeSquare()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        void BuildCamera()
        {
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera", typeof(Camera)); go.tag = "MainCamera"; cam = go.GetComponent<Camera>(); }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            var lane = WaveScenarios.All[ScenarioIndex].Lane;
            cam.transform.position = new Vector3(lane.Length / 2f + 0.3f, lane.Width / 2f - 0.6f, -10);
            cam.orthographicSize = Mathf.Max((lane.Length + 3f) / cam.aspect / 2f, lane.Width / 2f + 2.5f);
        }

        // ─────────────────────────── HUD ───────────────────────────

        void BuildHud()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);

            var root = canvasGo.transform;
            _hud = UiKit.Label(root, "Hud", 20, 10, 800, 60, "", 18, TextAnchor.UpperLeft, UiKit.Accent);
            float x = 20;
            for (int i = 0; i < WaveScenarios.All.Length; i++)
            {
                int idx = i;
                var name = WaveScenarios.All[i].Name;
                float w = 24 + name.Length * 11;
                UiKit.ButtonBox(root, "Sc" + i, x, 640, w, 40, name, () => { ScenarioIndex = idx; Restart(); }, null, 13);
                x += w + 8;
            }
            UiKit.ButtonBox(root, "Again", 1020, 590, 110, 40, "다시 (새 시드)", () => { Seed++; Restart(); }, null, 13);
            UiKit.ButtonBox(root, "Pause", 1140, 590, 120, 40, "일시정지", () => _paused = !_paused, null, 13);
            UiKit.ButtonBox(root, "S1", 1020, 640, 70, 40, "1배", () => Speed = 1f, null, 13);
            UiKit.ButtonBox(root, "S2", 1100, 640, 70, 40, "2배", () => Speed = 2f, null, 13);
            UiKit.ButtonBox(root, "S4", 1180, 640, 80, 40, "0.25배", () => Speed = 0.25f, null, 13);
            _legend = UiKit.Label(root, "Legend", 20, 590, 900, 44,
                "방어 유닛 큰 네모: 초록=숲 주황=불 파랑=기계  |  공격 유닛 작은 네모: 빨강=야수 보라=공중(흰 날개) 갈색=거인 진보라=암흑  |  노란 점=방어 사격, 분홍 점=공격 유닛 사격", 12);
        }

        void RefreshHud()
        {
            if (_hud == null || Sim == null) return;
            var sc = WaveScenarios.All[ScenarioIndex];
            float sec = Sim.Tick / (float)Sim.Cfg.TicksPerSecond;
            int alive = 0; foreach (var u in Sim.Units) if (u.Kind == UnitKind.Attacker && u.Alive) alive++;
            _hud.text = $"{sc.Name}   |   {sec:F1}초 / {Sim.Cfg.MaxSeconds}초   |   처치 {Sim.Kills}   누수 {Sim.Leaked}   방어 유닛 손실 {Sim.DefendersLost}   남은 적 {alive}"
                      + (Sim.IsOver ? "   |   웨이브 종료" : _paused ? "   |   일시정지" : $"   |   {Speed}배속");
        }
    }
}
