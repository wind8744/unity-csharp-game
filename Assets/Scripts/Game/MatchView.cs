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
    /// 2단계 프로토타입: 1v1, 사람(팀0) 대 봇(팀1). 위 = 상대 라인(내가 보낸 유닛이 걸어감), 아래 = 내 라인(상대가 보낸 유닛이 들어옴).
    /// 골드·인컴·뽑기·손패·보내기·10분 타이머·승패.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        public MatchSim Sim { get; private set; }
        public ulong Seed = 1;
        public float Speed = 1f;
        public int SelectedTowerId = 1;
        public bool BotPlaysHuman;
        public const int Me = 0, Enemy = 1;

        Sprite _square;
        LaneRenderer _myLane, _enemyLane;
        readonly List<MatchCommand> _pending = new List<MatchCommand>();
        readonly IMatchAgent _bot = new SimpleBot { Aggression = 65 };
        readonly IMatchAgent _humanBot = new SimpleBot();
        float _accumulator, _bannerLeft, _msgLeft;
        Text _hud, _banner, _msg, _selectedInfo, _handTitle;
        Transform _hand;
        readonly List<Button> _towerButtons = new List<Button>();
        bool _paused;
        int _lastHandVersion = -1;

        void Awake()
        {
            _square = LaneRenderer.MakeSquare();
            BuildHud();
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) if (args[i] == "-bots") BotPlaysHuman = true;
            Restart();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-matchtime" && float.TryParse(args[i + 1], out float sec)) FastForward(sec);
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
            _myLane?.Destroy(); _enemyLane?.Destroy();
            Sim = new MatchSim(new MatchConfig(), Seed);
            var lane = Sim.Lanes[0].Cfg;
            float gap = lane.Width + 1.8f;
            _enemyLane = new LaneRenderer(transform, _square, Sim.OwnLane(Enemy), new Vector2(0, gap), "상대 라인 ← 내가 보낸 유닛이 여기서 출발", "상대 기지 →", "상대 타워");
            _myLane = new LaneRenderer(transform, _square, Sim.OwnLane(Me), Vector2.zero, "내 라인 ← 상대가 보낸 유닛이 들어옴", "내 기지 →", "내 타워 슬롯: 빈 칸 클릭 = 짓기, 다시 클릭 = 강화, 우클릭 = 판매");
            _myLane.Banner += t => ShowBanner("내 라인 " + t);
            BuildCamera(lane, gap);
            _pending.Clear();
            _accumulator = 0; _paused = false; _bannerLeft = 0; _lastHandVersion = -1;
            RefreshHud(); BuildHand();
        }

        public void FastForward(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds * Sim.Cfg.TicksPerSecond);
            for (int i = 0; i < ticks && !Sim.IsOver; i++) StepOnce();
            _myLane.SnapAll(); _enemyLane.SnapAll();
            BuildHand();
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
                _myLane.Interpolate(t); _enemyLane.Interpolate(t);
            }
            _myLane.UpdateFx(dt); _enemyLane.UpdateFx(dt);
            if (_bannerLeft > 0) { _bannerLeft -= dt; if (_bannerLeft <= 0) _banner.text = ""; }
            if (_msgLeft > 0) { _msgLeft -= dt; if (_msgLeft <= 0) _msg.text = ""; }
            RefreshHud();
            int version = HandVersion();
            if (version != _lastHandVersion) BuildHand();
        }

        void StepOnce()
        {
            if (Sim.Tick % Sim.Cfg.TicksPerSecond == 0)
            {
                _bot.Decide(Sim, Enemy, 0, _pending);
                if (BotPlaysHuman) _humanBot.Decide(Sim, Me, 0, _pending);
            }
            _myLane.BeforeStep(); _enemyLane.BeforeStep();
            Sim.Step(_pending);
            _pending.Clear();
            _myLane.AfterStep(); _enemyLane.AfterStep();
            foreach (var ev in Sim.Events) HandleMatchEvent(ev);
        }

        void HandleMatchEvent(MatchEvent ev)
        {
            switch (ev.Type)
            {
                case MatchEventType.Sent:
                    if (ev.Team == Enemy) ShowBanner($"상대가 {WaveCatalog.Attacker(ev.A).Name}를 보냈습니다! (내 라인)");
                    break;
                case MatchEventType.Income:
                    if (ev.Team == Me) ShowMsg($"수입 +{ev.A} (팀 인컴 {ev.B})");
                    break;
                case MatchEventType.Rejected:
                    if (ev.Team == Me) ShowMsg(((CommandType)ev.A) switch
                    {
                        CommandType.Build => "골드가 부족하거나 자리가 찼습니다",
                        CommandType.Upgrade => "강화할 골드가 부족합니다",
                        CommandType.Draw => "뽑기 5골드가 없거나 손패가 가득 찼습니다",
                        CommandType.Send => "보낼 골드가 부족합니다",
                        _ => "할 수 없습니다",
                    });
                    break;
                case MatchEventType.MatchEnd:
                    ShowBanner(Sim.Winner == Me ? $"승리! ({Sim.EndReason})" : Sim.Winner == Enemy ? $"패배 ({Sim.EndReason})" : "무승부");
                    _bannerLeft = 999f;
                    break;
            }
        }

        // ─────────────────────────── 입력 ───────────────────────────

        void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || Sim.IsOver || BotPlaysHuman) return;
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
            if (!left && !right) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var cam = Camera.main; if (cam == null) return;
            var w = cam.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 10));
            if (!_myLane.WorldToSlot(w, out int col, out int row)) return;
            var existing = Sim.OwnLane(Me).TowerAt(col, row);
            if (left) _pending.Add(existing == null ? MatchCommand.Build(Me, 0, SelectedTowerId, col, row) : MatchCommand.Upgrade(Me, 0, existing.Id));
            else if (existing != null) _pending.Add(MatchCommand.Sell(Me, 0, existing.Id));
        }

        public void Draw() { if (!Sim.IsOver) _pending.Add(MatchCommand.Draw(Me, 0)); }
        public void SendCard(int handIndex) { if (!Sim.IsOver) _pending.Add(MatchCommand.Send(Me, 0, handIndex)); }

        // ─────────────────────────── HUD ───────────────────────────

        void ShowBanner(string text) { _banner.text = text; _bannerLeft = 2.5f; }
        void ShowMsg(string text) { _msg.text = text; _msgLeft = 2f; }

        int HandVersion()
        {
            var p = Sim.Player(Me, 0);
            int v = p.Hand.Count * 1000 + p.Gold;
            foreach (var h in p.Hand) v = v * 31 + h;
            return v;
        }

        void BuildCamera(LaneConfig lane, float gap)
        {
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera", typeof(Camera)); go.tag = "MainCamera"; cam = go.GetComponent<Camera>(); }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            float totalH = gap + lane.Width + 1f;
            cam.transform.position = new Vector3(lane.Length / 2f + 0.3f, totalH / 2f - 2.4f, -10);
            cam.orthographicSize = Mathf.Max((lane.Length + 2f) / cam.aspect / 2f, totalH / 2f + 2.4f);
        }

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

            _hud = UiKit.Label(root, "Hud", 20, 8, 1240, 44, "", 16, TextAnchor.UpperLeft, UiKit.Accent);
            _banner = UiKit.Label(root, "Banner", 0, 262, 1280, 30, "", 20, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.4f));
            _msg = UiKit.Label(root, "Msg", 0, 290, 1280, 24, "", 14, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 1f));

            // 손패
            _handTitle = UiKit.Label(root, "HandTitle", 20, 520, 700, 22, "", 13);
            _hand = UiKit.Rect(root, "Hand", 20, 544, 900, 70);
            UiKit.ButtonBox(root, "Draw", 940, 544, 150, 66, "뽑기\n5골드", () => Draw(), new Color(0.55f, 0.4f, 0.7f), 15);
            UiKit.Label(root, "DrawInfo", 1100, 544, 170, 66, "일반 60%\n희귀 30%\n영웅 10%", 12);

            // 타워
            UiKit.Label(root, "TowerTitle", 20, 618, 700, 20, "지을 타워 (선택 후 내 라인의 빈 슬롯 클릭)", 13);
            float x = 20;
            foreach (var d in WaveCatalog.Towers)
            {
                int id = d.Id;
                var b = UiKit.ButtonBox(root, "T" + id, x, 640, 128, 40, $"{d.Name} {d.Cost}골드\n공{d.Atk} 사{d.Range10 / 10f:0.#}", () => { SelectedTowerId = id; RefreshTowerButtons(); }, LaneRenderer.TowerColor(d), 11);
                foreach (var txt in b.GetComponentsInChildren<Text>()) txt.color = Color.black;
                _towerButtons.Add(b);
                x += 134;
            }
            _selectedInfo = UiKit.Label(root, "Sel", 20, 684, 900, 20, "", 12);
            UiKit.ButtonBox(root, "Again", 940, 684, 100, 30, "새 판", () => { Seed++; Restart(); }, null, 12);
            UiKit.ButtonBox(root, "Pause", 1050, 684, 70, 30, "정지", () => _paused = !_paused, null, 12);
            UiKit.ButtonBox(root, "S1", 1130, 684, 40, 30, "1배", () => Speed = 1f, null, 11);
            UiKit.ButtonBox(root, "S2", 1175, 684, 40, 30, "2배", () => Speed = 2f, null, 11);
            UiKit.ButtonBox(root, "S4", 1220, 684, 40, 30, "4배", () => Speed = 4f, null, 11);
            RefreshTowerButtons();
        }

        void RefreshTowerButtons()
        {
            for (int i = 0; i < _towerButtons.Count; i++)
            {
                var d = WaveCatalog.Towers[i];
                var c = LaneRenderer.TowerColor(d);
                _towerButtons[i].GetComponent<Image>().color = d.Id == SelectedTowerId ? c : new Color(c.r, c.g, c.b, 0.45f);
            }
            if (_selectedInfo != null) _selectedInfo.text = $"선택: {WaveCatalog.Tower(SelectedTowerId).Name} — {TowerInfo.Describe(WaveCatalog.Tower(SelectedTowerId))}";
        }

        void BuildHand()
        {
            if (_hand == null) return;
            UiKit.Clear(_hand);
            var p = Sim.Player(Me, 0);
            _lastHandVersion = HandVersion();
            _handTitle.text = $"내 손패 {p.Hand.Count}/{Sim.Cfg.HandMax} (클릭하면 상대 라인으로 보냄 · 보낼 때 팀 인컴이 오릅니다)";
            const float cw = 142, gap = 8;
            for (int i = 0; i < p.Hand.Count; i++)
            {
                var d = WaveCatalog.Attacker(p.Hand[i]);
                int idx = i;
                int cost = Sim.SendCostOf(d);
                var color = d.Tribe switch { AtkTribe.Beast => new Color(0.85f, 0.35f, 0.35f), AtkTribe.Air => new Color(0.75f, 0.45f, 0.85f), AtkTribe.Giant => new Color(0.6f, 0.4f, 0.3f), _ => new Color(0.45f, 0.32f, 0.55f) };
                string rar = d.Rarity == Rarity.Hero ? "★영웅" : d.Rarity == Rarity.Rare ? "희귀" : "일반";
                var b = UiKit.ButtonBox(_hand, "C" + i, i * (cw + gap), 0, cw, 66, $"{d.Name} ({rar})\n보내기 {cost}골드 · 체력 {d.Hp}\n누수 {d.Leak} · 인컴 +{System.Math.Max(1, d.Income * Sim.Cfg.SendIncomePercent / 100)}{(d.Flying ? " · 공중" : "")}", () => SendCard(idx), color, 11);
                b.interactable = p.Gold >= cost;
            }
            if (p.Hand.Count == 0) UiKit.Label(_hand, "Empty", 0, 20, 600, 30, "손패가 비었습니다. [뽑기]로 공격 유닛을 뽑으세요.", 13);
        }

        void RefreshHud()
        {
            if (_hud == null || Sim == null) return;
            var p = Sim.Player(Me, 0);
            var my = Sim.OwnLane(Me); var en = Sim.OwnLane(Enemy);
            int sec = Sim.Seconds;
            int nextIn = my.TicksToNextWave / my.Cfg.TicksPerSecond;
            int incomeIn = Sim.Cfg.IncomeIntervalSeconds - (sec % Sim.Cfg.IncomeIntervalSeconds);
            string nextWave = my.NextWaveIndex < WaveCatalog.BaseWaves.Length ? $"다음 웨이브 {my.NextWaveIndex + 1} ({nextIn}초 후): {WaveCatalog.Describe(my.NextWaveIndex)}" : "기본 웨이브 끝";
            _hud.text = $"{sec / 60}:{sec % 60:00} / {Sim.Cfg.MatchSeconds / 60}:00   |   내 기지 {my.BaseHp}   상대 기지 {en.BaseHp}   |   골드 {p.Gold}   팀 인컴 {Sim.Teams[Me].Income} (상대 {Sim.Teams[Enemy].Income}) · 다음 수입 {incomeIn}초   |   {Speed}배속{(_paused ? " 정지" : "")}{(BotPlaysHuman ? " [봇 대전]" : "")}\n{nextWave}   |   내 라인 처치 {my.Kills} 누수 {my.Leaked}  ·  상대 라인 처치 {en.Kills} 누수 {en.Leaked}";
        }
    }
}
