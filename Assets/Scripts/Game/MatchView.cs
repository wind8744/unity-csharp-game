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
        Text _hud, _banner, _msg, _selectedInfo, _handTitle, _mission, _synergy, _intel;
        Transform _hand, _augmentPanel;
        int _lastOfferVersion = -1;
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
            _enemyLane = new LaneRenderer(transform, _square, Sim.OwnLane(Enemy), new Vector2(0, gap), "상대 라인 ← 내가 보낸 유닛 출발", "상대 기지 →", "상대 타워");
            _myLane = new LaneRenderer(transform, _square, Sim.OwnLane(Me), Vector2.zero, "내 라인 ← 상대 유닛 진입", "내 기지 →", "내 타워: 클릭 짓기 · 재클릭 강화 · 우클릭 판매");
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
            int offerVersion = OfferVersion();
            if (offerVersion != _lastOfferVersion) BuildAugmentPanel();
        }

        int OfferVersion()
        {
            var p = Sim.Player(Me, 0);
            int v = p.Offers.Count * 7 + (Sim.IsPaused ? 1 : 0);
            foreach (var a in p.Offers) v = v * 31 + (int)a + 1;
            return v;
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
                case MatchEventType.AugmentOffer:
                    if (ev.Team == Me) ShowBanner("증강 선택! 10초 동안 게임이 멈춥니다");
                    break;
                case MatchEventType.AugmentPicked:
                    if (ev.Team == Me) ShowMsg($"증강 획득: {FunCatalog.AugmentName((AugmentId)ev.A)} — {FunCatalog.AugmentDesc((AugmentId)ev.A)}");
                    break;
                case MatchEventType.EventWarn:
                    ShowBanner($"{ev.B}초 뒤 이벤트: [{FunCatalog.EventName((EventId)ev.A)}] {FunCatalog.EventDesc((EventId)ev.A)}");
                    _bannerLeft = 5f;
                    break;
                case MatchEventType.EventStart:
                    ShowBanner($"이벤트 시작: [{FunCatalog.EventName((EventId)ev.A)}] {FunCatalog.EventDesc((EventId)ev.A)} ({ev.B}초)");
                    _bannerLeft = 4f;
                    break;
                case MatchEventType.EventEnd:
                    ShowMsg($"이벤트 종료: {FunCatalog.EventName((EventId)ev.A)}");
                    break;
                case MatchEventType.MissionDone:
                    if (ev.Team == Me) { ShowBanner($"★ 비밀 미션 달성: {FunCatalog.MissionName((MissionId)ev.A)}!"); _bannerLeft = 4f; }
                    else ShowMsg("상대가 비밀 미션을 달성했습니다");
                    break;
                case MatchEventType.GroupSynergy:
                    if (ev.Team == Me) ShowMsg(ev.B == 1 ? "무리 시너지: 같은 유닛 3마리 → 체력 +20%" : ev.B == 2 ? "무리 시너지: 야수 3 → 속도 +20%" : ev.B == 3 ? "무리 시너지: 공중 3 → 체력 +15%" : ev.B == 4 ? "무리 시너지: 거인 3 → 누수 +1" : "무리 시너지: 암흑 3 → 은신 +1초");
                    break;
            }
        }

        public void PickAugment(int index) { _pending.Add(MatchCommand.PickAugment(Me, 0, index)); }

        // ─────────────────────────── 입력 ───────────────────────────

        void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || Sim.IsOver || BotPlaysHuman || Sim.IsPaused) return;
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
            cam.orthographicSize = Mathf.Max((lane.Length + 2f) / cam.aspect / 2f, totalH / 2f + 2.9f);
            // 화면 세로: 위 HUD 약 50px, 아래 정보줄 518px 부터. 두 라인(월드 y -0.5 ~ totalH-0.5)이 그 사이 중앙에 오도록.
            float unitsPerPixel = cam.orthographicSize * 2f / 720f;
            float lanesCenterWorld = (totalH - 1f) / 2f;
            float targetCenterPixel = (50f + 518f) / 2f;
            cam.transform.position = new Vector3(lane.Length / 2f + 0.3f, lanesCenterWorld + (targetCenterPixel - 360f) * unitsPerPixel, -10);
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
            _mission = UiKit.Label(root, "Mission", 20, 518, 440, 38, "", 11, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.5f));
            _synergy = UiKit.Label(root, "Synergy", 470, 518, 480, 38, "", 11, TextAnchor.UpperLeft, new Color(0.7f, 0.9f, 0.7f));
            _intel = UiKit.Label(root, "Intel", 960, 518, 300, 38, "", 11, TextAnchor.UpperRight, new Color(0.8f, 0.85f, 1f));
            _augmentPanel = UiKit.PanelBox(root, "AugmentPanel", 190, 150, 900, 330, new Color(0.08f, 0.08f, 0.1f, 0.97f)).transform;
            _augmentPanel.gameObject.SetActive(false);
            _banner = UiKit.Label(root, "Banner", 0, 262, 1280, 24, "", 18, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.4f));
            _msg = UiKit.Label(root, "Msg", 0, 286, 1280, 18, "", 12, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 1f));

            // 손패
            _handTitle = UiKit.Label(root, "HandTitle", 20, 556, 700, 18, "", 12);
            _hand = UiKit.Rect(root, "Hand", 20, 574, 900, 58);
            UiKit.ButtonBox(root, "Draw", 940, 574, 150, 58, "뽑기 5골드", () => Draw(), new Color(0.55f, 0.4f, 0.7f), 15);
            UiKit.Label(root, "DrawInfo", 1100, 574, 170, 58, "일반 60% · 희귀 30%\n영웅 10%", 11);

            // 타워
            UiKit.Label(root, "TowerTitle", 20, 636, 700, 18, "지을 타워 (선택 후 내 라인의 빈 슬롯 클릭)", 12);
            float x = 20;
            foreach (var d in WaveCatalog.Towers)
            {
                int id = d.Id;
                var b = UiKit.ButtonBox(root, "T" + id, x, 654, 128, 34, $"{d.Name} {d.Cost}골드\n공{d.Atk} 사{d.Range10 / 10f:0.#}", () => { SelectedTowerId = id; RefreshTowerButtons(); }, LaneRenderer.TowerColor(d), 10);
                foreach (var txt in b.GetComponentsInChildren<Text>()) txt.color = Color.black;
                _towerButtons.Add(b);
                x += 134;
            }
            _selectedInfo = UiKit.Label(root, "Sel", 20, 692, 900, 24, "", 12);
            UiKit.ButtonBox(root, "Again", 940, 692, 100, 26, "새 판", () => { Seed++; Restart(); }, null, 12);
            UiKit.ButtonBox(root, "Pause", 1050, 692, 70, 26, "정지", () => _paused = !_paused, null, 12);
            UiKit.ButtonBox(root, "S1", 1130, 692, 40, 26, "1배", () => Speed = 1f, null, 11);
            UiKit.ButtonBox(root, "S2", 1175, 692, 40, 26, "2배", () => Speed = 2f, null, 11);
            UiKit.ButtonBox(root, "S4", 1220, 692, 40, 26, "4배", () => Speed = 4f, null, 11);
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
                var b = UiKit.ButtonBox(_hand, "C" + i, i * (cw + gap), 0, cw, 58, $"{d.Name} ({rar})\n보내기 {cost}골드 · 체력 {d.Hp}\n누수 {d.Leak} · 인컴 +{System.Math.Max(1, d.Income * Sim.Cfg.SendIncomePercent / 100)}{(d.Flying ? " · 공중" : "")}", () => SendCard(idx), color, 11);
                b.interactable = p.Gold >= cost;
            }
            if (p.Hand.Count == 0) UiKit.Label(_hand, "Empty", 0, 20, 600, 30, "손패가 비었습니다. [뽑기]로 공격 유닛을 뽑으세요.", 13);
        }

        void BuildAugmentPanel()
        {
            if (_augmentPanel == null) return;
            UiKit.Clear(_augmentPanel);
            var p = Sim.Player(Me, 0);
            _lastOfferVersion = OfferVersion();
            bool show = p.Offers.Count > 0 && !BotPlaysHuman;
            _augmentPanel.gameObject.SetActive(show);
            if (!show) return;
            _augmentPanel.SetAsLastSibling();
            string title = Sim.IsPaused ? $"증강을 하나 고르세요 — {Mathf.CeilToInt(Sim.PauseLeft / (float)Sim.Cfg.TicksPerSecond)}초 뒤 자동 선택 (양쪽 다 멈춤)" : "추가 증강 선택 (게임은 계속 진행)";
            UiKit.Label(_augmentPanel, "Title", 0, 16, 900, 36, title, 18, TextAnchor.MiddleCenter, UiKit.Accent);
            for (int i = 0; i < p.Offers.Count; i++)
            {
                int idx = i;
                var a = p.Offers[i];
                var b = UiKit.ButtonBox(_augmentPanel, "Offer" + i, 30 + i * 285, 70, 270, 220, "", () => PickAugment(idx), UiKit.PanelLight);
                b.GetComponentInChildren<Text>().text = "";
                UiKit.Label(b.transform, "G", 10, 10, 250, 22, $"[{FunCatalog.AugmentGroup(a)}]", 13, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));
                UiKit.Label(b.transform, "N", 10, 40, 250, 34, FunCatalog.AugmentName(a), 24, TextAnchor.MiddleCenter, UiKit.Accent);
                UiKit.Label(b.transform, "D", 14, 90, 242, 120, FunCatalog.AugmentDesc(a), 14, TextAnchor.UpperCenter);
            }
        }

        string MissionText()
        {
            var p = Sim.Player(Me, 0);
            string s = $"비밀 미션 [{FunCatalog.MissionName(p.Mission)}]: {FunCatalog.MissionDesc(p.Mission)}";
            if (p.MissionDone) s += "  ✔ 달성";
            if (p.FreeSends > 0) s += $"  (무료 보내기 {p.FreeSends}회)";
            if (p.Augments.Count > 0) { s += "\n내 증강: "; foreach (var a in p.Augments) s += FunCatalog.AugmentName(a) + " "; }
            return s;
        }

        string SynergyText()
        {
            var lane = Sim.OwnLane(Me);
            string tribe = $"숲 {lane.TribeCount[0]}{(lane.Forest5 ? "★★" : lane.Forest3 ? "★" : "")}  불 {lane.TribeCount[1]}{(lane.Fire5 ? "★★" : lane.Fire3 ? "★" : "")}  기계 {lane.TribeCount[2]}{(lane.Machine5 ? "★★" : lane.Machine3 ? "★" : "")}   (★3개 ★★5개)";
            var rows = new System.Collections.Generic.List<string>();
            for (int r = 0; r < 3; r++)
            {
                string tag = "";
                if (lane.RowWarrior2[r]) tag += "전사 ";
                if (lane.RowArcher2[r]) tag += "궁수 ";
                if (lane.RowMage2[r]) tag += "마법사 ";
                if (tag.Length > 0) rows.Add($"{(r == 0 ? "앞줄" : r == 1 ? "중간" : "뒷줄")}: {tag.Trim()}");
            }
            string job = rows.Count == 0 ? "같은 줄 같은 직업 2개 → 직업 시너지" : string.Join(" · ", rows);
            string ev = Sim.ActiveEvent.HasValue ? $"\n이벤트 진행 중: [{FunCatalog.EventName(Sim.ActiveEvent.Value)}] {FunCatalog.EventDesc(Sim.ActiveEvent.Value)}" : Sim.NextEvent.HasValue && Sim.EventRound < Sim.Cfg.EventSeconds.Length ? $"\n다음 이벤트 {Sim.Cfg.EventSeconds[Sim.EventRound] / 60}:{Sim.Cfg.EventSeconds[Sim.EventRound] % 60:00} (30초 전에 공개)" : "";
            return $"타워 시너지: {tribe}\n{job}{ev}";
        }

        string IntelText()
        {
            var p = Sim.Player(Me, 0); var e = Sim.Player(Enemy, 0);
            var sb = new System.Text.StringBuilder();
            if (p.Has(AugmentId.Accountant)) sb.Append($"[회계] 상대 골드 {e.Gold}\n");
            if (p.Has(AugmentId.Scout))
            {
                sb.Append("[정찰] 상대 손패: ");
                for (int i = 0; i < e.Hand.Count && i < 2; i++) sb.Append(WaveCatalog.Attacker(e.Hand[i]).Name).Append(i == 0 && e.Hand.Count > 1 ? ", " : "");
                if (e.Hand.Count == 0) sb.Append("없음");
                sb.Append('\n');
            }
            int enemyAA = 0; foreach (var t in Sim.OwnLane(Enemy).Towers) if (t.Alive && (t.Def.AntiAir || t.ForceAntiAir)) enemyAA++;
            sb.Append($"상대 대공 타워 {enemyAA}개 · 상대 증강 {e.Augments.Count}개");
            return sb.ToString();
        }

        void RefreshHud()
        {
            if (_hud == null || Sim == null) return;
            if (_mission != null) { _mission.text = MissionText(); _synergy.text = SynergyText(); _intel.text = IntelText(); }
            if (Sim.IsPaused && _augmentPanel.gameObject.activeSelf)
            {
                var title = _augmentPanel.Find("Title");
                if (title != null) title.GetComponent<Text>().text = $"증강을 하나 고르세요 — {Mathf.CeilToInt(Sim.PauseLeft / (float)Sim.Cfg.TicksPerSecond)}초 뒤 자동 선택 (양쪽 다 멈춤)";
            }
            var p = Sim.Player(Me, 0);
            var my = Sim.OwnLane(Me); var en = Sim.OwnLane(Enemy);
            int sec = Sim.Seconds;
            int nextIn = my.TicksToNextWave / my.Cfg.TicksPerSecond;
            int incomeIn = Sim.Cfg.IncomeIntervalSeconds - (sec % Sim.Cfg.IncomeIntervalSeconds);
            string nextWave = my.NextWaveIndex < WaveCatalog.BaseWaves.Length ? $"다음 웨이브 {my.NextWaveIndex + 1} ({nextIn}초 후): {WaveCatalog.Describe(my.NextWaveIndex)}" : "기본 웨이브 끝";
            _hud.text = $"{sec / 60}:{sec % 60:00} / {Sim.Cfg.MatchSeconds / 60}:00   |   내 기지 {my.BaseHp}   상대 기지 {en.BaseHp}   |   골드 {p.Gold}   팀 인컴 {Sim.Teams[Me].Income} (상대 {Sim.Teams[Enemy].Income}) · 다음 수입 {incomeIn}초   |   {Speed}배속{(_paused ? " 정지" : "")}{(Sim.IsPaused ? " [증강 선택 중]" : "")}{(BotPlaysHuman ? " [봇 대전]" : "")}\n{nextWave}   |   내 라인 처치 {my.Kills} 누수 {my.Leaked}  ·  상대 라인 처치 {en.Kills} 누수 {en.Leaked}";
        }
    }
}
