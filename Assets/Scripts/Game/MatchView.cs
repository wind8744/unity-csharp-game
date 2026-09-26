using System.Collections.Generic;
using LaneBattle.Core.Net;
using LaneBattle.Core.Wave;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>
    /// 경기 화면. 위 = 상대 라인(내가 보낸 유닛이 걸어감), 아래 = 내 라인(상대 유닛이 들어옴).
    /// 사람은 팀 0 의 0번 자리. 나머지 자리(팀원·상대)는 봇. 1v1·2v2·3v3 모두 이 화면.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        public MatchSim Sim { get; private set; }
        public ulong Seed = 1;
        public int PlayersPerTeam = 1;
        public float Speed = 1f;
        public int SelectedTowerId = 1;      // 상점에서 고른 타워 정의
        public bool BotPlaysHuman;
        public System.Action OnExit;         // 타이틀로
        public int MyTeam = 0, MyPlayer = 0;
        public int EnemyTeam => 1 - MyTeam;
        public NetSession Net;               // 온라인이면 락스텝 세션 (명령은 여기로, 진행은 턴이 와야)

        LaneRenderer _myLane, _enemyLane;
        readonly List<MatchCommand> _pending = new List<MatchCommand>();
        IMatchAgent[][] _bots;                // [team][player], 사람 자리는 null (BotPlaysHuman 이면 봇)
        float _accumulator, _bannerLeft, _msgLeft, _hintLeft;
        Transform _ui, _hand, _shop, _actionPanel, _augmentPanel, _infoPanel, _resultPanel, _recipePanel, _menuPanel;
        Text _time, _wave, _myHp, _enemyHp, _gold, _income, _banner, _msg, _handTitle, _selectedInfo, _mission, _synergy, _eventText, _intel, _team;
        Image _myHpBar, _enemyHpBar;
        readonly List<Button> _shopButtons = new List<Button>();
        readonly List<Image> _shopImages = new List<Image>();
        int _lastHandVersion = -1, _lastOfferVersion = -1, _lastActionVersion = -1, _selectedTower = -1, _lastShopGold = -1;
        bool _paused, _menuOpen, _recipeOpen, _ended, _netLost;
        float _stallTime, _netLostAt;
        float _slowSpeed = 1f;

        void Awake()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-bots") BotPlaysHuman = true;
                if (args[i] == "-mode" && i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) PlayersPerTeam = Mathf.Clamp(m, 1, 3);
                if (args[i] == "-seed" && i + 1 < args.Length && ulong.TryParse(args[i + 1], out ulong sd)) Seed = sd;
            }
            if (GameSession.HumanIsBot) BotPlaysHuman = true;
            BuildHud();
            Restart();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-matchtime" && float.TryParse(args[i + 1], out float sec)) FastForward(sec);
            foreach (var a in args)
            {
                if (a == "-select") { foreach (var t in Sim.OwnLane(MyTeam).Towers) if (t.Alive && t.Owner == MyPlayer) { SelectTower(t.Id); break; } }
                if (a == "-recipes") ToggleRecipes();
                if (a == "-menu") ToggleMenu();
            }
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshot") StartCoroutine(ScreenshotAndQuit(args[i + 1]));
        }

        void OnDestroy()
        {
            _myLane?.Destroy(); _enemyLane?.Destroy();
            if (_ui != null) Destroy(_ui.gameObject);
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            yield return new WaitForSeconds(1.2f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1.5f);
            Application.Quit();
        }

        public void Restart()
        {
            _myLane?.Destroy(); _enemyLane?.Destroy();
            if (Net != null)
            {
                PlayersPerTeam = Net.Start.PlayersPerTeam; Seed = Net.Start.Seed;
                (MyTeam, MyPlayer) = Net.MyPos;
                Sim = Net.Sim ?? Net.CreateSim(GameSession.BotAggression);
                _bots = null;
                Net.Disconnected += reason => { ShowBanner(reason + " — 타이틀로 돌아갑니다", 999f); _netLost = true; };
                Net.Log += line => ShowMsg(line, 4f);
            }
            else
            {
                var cfg = new MatchConfig { PlayersPerTeam = PlayersPerTeam };
                Sim = new MatchSim(cfg, Seed);
                _bots = new IMatchAgent[2][];
                for (int t = 0; t < 2; t++)
                {
                    _bots[t] = new IMatchAgent[PlayersPerTeam];
                    for (int p = 0; p < PlayersPerTeam; p++)
                    {
                        bool human = t == MyTeam && p == MyPlayer && !BotPlaysHuman;
                        _bots[t][p] = human ? null : new SimpleBot { Aggression = t == EnemyTeam ? GameSession.BotAggression + p * 5 : 55 + p * 10 };
                    }
                }
            }
            var lane = Sim.Lanes[0].Cfg;
            float gap = lane.Width + 1.6f;
            _enemyLane = new LaneRenderer(transform, Sim.OwnLane(EnemyTeam), new Vector2(0, gap), false, "상대 라인  (내가 보낸 유닛 →)" + TeamNames(EnemyTeam));
            _myLane = new LaneRenderer(transform, Sim.OwnLane(MyTeam), Vector2.zero, true, "내 라인  (상대 유닛 →)  빈 칸 클릭: 짓기 · 타워 클릭: 강화/합성" + TeamNames(MyTeam));
            _myLane.LocalPlayer = _enemyLane.LocalPlayer = MyPlayer;
            _myLane.Banner += t => ShowBanner(t);
            _myLane.PlaySounds = _enemyLane.PlaySounds = !BotPlaysHuman || !Application.isBatchMode;
            BuildCamera(lane, gap);
            _pending.Clear();
            _accumulator = 0; _paused = false; _bannerLeft = 0; _lastHandVersion = -1; _lastOfferVersion = -1; _lastActionVersion = -1; _selectedTower = -1; _ended = false; _menuOpen = false;
            if (_resultPanel != null) _resultPanel.gameObject.SetActive(false);
            if (_menuPanel != null) _menuPanel.gameObject.SetActive(false);
            Sfx.Music("bgm_battle");
            RefreshHud(); BuildHand(); RefreshShop();
            ShowMsg("타워를 골라 내 라인 빈 칸에 짓고, [뽑기]로 뽑은 유닛을 보내 상대를 압박하세요. 같은 타워 3개는 합쳐서 ★2!", 8f);
        }

        public void FastForward(float seconds)
        {
            if (Net != null) return;
            int ticks = Mathf.RoundToInt(seconds * Sim.Cfg.TicksPerSecond);
            for (int i = 0; i < ticks && !Sim.IsOver; i++) StepOnce();
            _myLane.SnapAll(); _enemyLane.SnapAll();
            BuildHand();
        }

        void Update()
        {
            if (Sim == null) return;
            float dt = Time.deltaTime;
            HandleInput();
            if (Net != null)
            {
                Net.Poll();
                if (_pending.Count > 0) { Net.LocalCommands.AddRange(_pending); _pending.Clear(); }
                if (_netLost) { if (_netLostAt == 0) _netLostAt = Time.time; if (Time.time - _netLostAt > 3f) { OnExit?.Invoke(); return; } }
            }
            bool running = Net != null ? !_netLost : !_paused;
            if (running && !Sim.IsOver)
            {
                float speed = Net != null ? 1f : Speed;
                _accumulator += dt * speed;
                float tickLen = 1f / Sim.Cfg.TicksPerSecond;
                bool catchUp = Net != null && !Net.IsHost && Net.QueuedTurns > 3;
                int steps = 0, maxSteps = catchUp ? 16 : 8;
                if (catchUp) _accumulator = Mathf.Max(_accumulator, tickLen * 2);
                while (_accumulator >= tickLen && steps++ < maxSteps)
                {
                    if (!StepOnce()) { _accumulator = Mathf.Min(_accumulator, tickLen); _stallTime += dt; break; }
                    _accumulator -= tickLen; _stallTime = 0;
                }
                float t = Mathf.Clamp01(_accumulator / tickLen);
                _myLane.Interpolate(t); _enemyLane.Interpolate(t);
            }
            _myLane.UpdateFx(dt); _enemyLane.UpdateFx(dt);
            if (_bannerLeft > 0) { _bannerLeft -= dt; if (_bannerLeft <= 0) _banner.text = ""; }
            if (_msgLeft > 0) { _msgLeft -= dt; if (_msgLeft <= 0) _msg.text = ""; }
            RefreshHud();
            if (HandVersion() != _lastHandVersion) BuildHand();
            if (OfferVersion() != _lastOfferVersion) BuildAugmentPanel();
            if (ActionVersion() != _lastActionVersion) BuildActionPanel();
            if (Sim.Player(MyTeam, MyPlayer).Gold != _lastShopGold) RefreshShop();
            if (Sim.IsOver && !_ended) { _ended = true; ShowResult(); }
        }

        bool StepOnce()
        {
            _myLane.BeforeStep(); _enemyLane.BeforeStep();
            if (Net != null)
            {
                if (!Net.TryStep()) return false;
            }
            else
            {
                if (Sim.Tick % Sim.Cfg.TicksPerSecond == 0)
                    for (int t = 0; t < 2; t++)
                        for (int p = 0; p < PlayersPerTeam; p++)
                            _bots[t][p]?.Decide(Sim, t, p, _pending);
                Sim.Step(_pending);
                _pending.Clear();
            }
            _myLane.AfterStep(); _enemyLane.AfterStep();
            foreach (var ev in Sim.Events) HandleMatchEvent(ev);
            return true;
        }

        string TeamNames(int team)
        {
            if (Net == null) return "";
            var sb = new System.Text.StringBuilder("  [");
            for (int p = 0; p < PlayersPerTeam; p++)
            {
                int pid = Net.Lobby.SlotPlayer[team * PlayersPerTeam + p];
                sb.Append(pid < 0 ? "봇" : Net.Lobby.NameOf(pid)).Append(p < PlayersPerTeam - 1 ? ", " : "");
            }
            return sb.Append(']').ToString();
        }

        void HandleMatchEvent(MatchEvent ev)
        {
            bool mine = ev.Team == MyTeam && ev.Player == MyPlayer;
            switch (ev.Type)
            {
                case MatchEventType.Sent:
                    if (ev.Team == EnemyTeam) { ShowBanner($"상대가 {WaveCatalog.Attacker(ev.A).Name}를 보냈습니다!"); Sfx.Play("send", 0.5f, 0.9f); }
                    else if (mine) Sfx.Play("send", 0.7f);
                    break;
                case MatchEventType.Drew:
                    if (mine) Sfx.Play("draw", 0.8f);
                    break;
                case MatchEventType.Sold:
                    if (mine) Sfx.Play("sell", 0.8f);
                    break;
                case MatchEventType.Income:
                    if (mine) { ShowMsg($"수입 +{ev.A} 골드 (팀 인컴 {ev.B})"); Sfx.Play("income", 0.7f); }
                    break;
                case MatchEventType.KillGold:
                    if (mine) Sfx.Play("coin", 0.25f, 1.1f, 0.15f, 0.12f);
                    break;
                case MatchEventType.Rejected:
                    if (mine)
                    {
                        Sfx.Play("error", 0.6f);
                        ShowMsg(((CommandType)ev.A) switch
                        {
                            CommandType.Build => "골드가 부족하거나 자리가 찼습니다",
                            CommandType.Upgrade => "강화할 골드가 부족합니다",
                            CommandType.Draw => "뽑기 골드가 없거나 손패가 가득 찼습니다",
                            CommandType.Send => Sim.ActiveEvent == EventId.Storm ? "폭풍 중에는 공중 유닛을 못 보냅니다" : "보낼 골드가 부족합니다",
                            CommandType.Merge => "같은 타워·같은 별 3개가 필요합니다",
                            CommandType.Fuse => "합성 조합이 맞지 않습니다",
                            _ => "지금은 할 수 없습니다",
                        });
                    }
                    break;
                case MatchEventType.AugmentOffer:
                    if (mine) { ShowBanner("증강 선택! 10초 동안 양쪽 다 멈춥니다"); Sfx.Play("augment_open", 0.8f); }
                    break;
                case MatchEventType.AugmentPicked:
                    if (mine) { ShowMsg($"증강 획득: {FunCatalog.AugmentName((AugmentId)ev.A)} — {FunCatalog.AugmentDesc((AugmentId)ev.A)}", 4f); Sfx.Play("augment_pick", 0.8f); }
                    break;
                case MatchEventType.EventWarn:
                    ShowBanner($"{ev.B}초 뒤 이벤트 [{FunCatalog.EventName((EventId)ev.A)}]: {FunCatalog.EventDesc((EventId)ev.A)}", 5f);
                    Sfx.Play("event_warn", 0.8f);
                    break;
                case MatchEventType.EventStart:
                    ShowBanner($"이벤트 시작 [{FunCatalog.EventName((EventId)ev.A)}]: {FunCatalog.EventDesc((EventId)ev.A)} ({ev.B}초)", 4f);
                    Sfx.Play("event_start", 0.8f);
                    Sfx.Music("bgm_tension");
                    break;
                case MatchEventType.EventEnd:
                    ShowMsg($"이벤트 종료: {FunCatalog.EventName((EventId)ev.A)}");
                    if (!Sim.IsOver) Sfx.Music("bgm_battle");
                    break;
                case MatchEventType.MissionDone:
                    if (ev.Team == MyTeam) { ShowBanner($"★ 비밀 미션 달성: {FunCatalog.MissionName((MissionId)ev.A)}!", 4f); Sfx.Play("mission", 0.9f); }
                    else ShowMsg("상대가 비밀 미션을 달성했습니다");
                    break;
                case MatchEventType.GroupSynergy:
                    if (ev.Team == MyTeam) ShowMsg(ev.B == 1 ? "무리 시너지: 같은 유닛 3마리 → 체력 +20%" : ev.B == 2 ? "무리 시너지: 야수 3 → 속도 +20%" : ev.B == 3 ? "무리 시너지: 공중 3 → 체력 +15%" : ev.B == 4 ? "무리 시너지: 거인 3 → 누수 +1" : "무리 시너지: 암흑 3 → 은신 +1초");
                    break;
                case MatchEventType.Merged:
                    if (mine) ShowMsg($"★{ev.B} 합치기 성공! 공격력이 크게 오릅니다");
                    break;
                case MatchEventType.Fused:
                    if (mine) ShowMsg($"합성 성공: {WaveCatalog.Tower(ev.B).Name}!");
                    break;
            }
        }

        // ─────────────────────────── 입력 ───────────────────────────

        void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (_recipeOpen) ToggleRecipes();
                else if (_selectedTower >= 0) SelectTower(-1);
                else ToggleMenu();
            }
            if (kb != null && !_menuOpen && !Sim.IsOver)
            {
                if (kb.spaceKey.wasPressedThisFrame) TogglePause();
                if (kb.digit1Key.wasPressedThisFrame && Net == null) Speed = 1f;
                if (kb.digit2Key.wasPressedThisFrame && Net == null) Speed = 2f;
                if (kb.digit3Key.wasPressedThisFrame && Net == null) Speed = 4f;
                if (kb.dKey.wasPressedThisFrame) Draw();
                for (int i = 0; i < WaveCatalog.BasicTowers.Length; i++)
                {
                    var key = i switch { 0 => kb.qKey, 1 => kb.wKey, 2 => kb.eKey, 3 => kb.rKey, 4 => kb.tKey, 5 => kb.yKey, 6 => kb.uKey, 7 => kb.iKey, _ => kb.oKey };
                    if (key.wasPressedThisFrame) { SelectedTowerId = WaveCatalog.BasicTowers[i].Id; RefreshShop(); }
                }
            }
            var mouse = Mouse.current;
            if (mouse == null || Sim.IsOver || BotPlaysHuman || Sim.IsPaused || _menuOpen || _recipeOpen) { _myLane?.SetHover(-1, -1); return; }
            var cam = Camera.main; if (cam == null) return;
            var w = cam.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 10));
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            int col = -1, row = -1;
            bool onSlot = !overUi && _myLane.WorldToSlot(w, out col, out row);
            _myLane.SetHover(onSlot ? col : -1, onSlot ? row : -1);
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
            if (!left && !right || overUi) return;
            if (!onSlot) { if (left) SelectTower(-1); return; }
            var existing = Sim.OwnLane(MyTeam).TowerAt(col, row);
            if (left)
            {
                if (existing == null) { SelectTower(-1); _pending.Add(MatchCommand.Build(MyTeam, MyPlayer, SelectedTowerId, col, row)); }
                else if (existing.Owner == MyPlayer) { SelectTower(existing.Id == _selectedTower ? -1 : existing.Id); Sfx.Play("click", 0.5f); }
                else ShowMsg($"팀원 P{existing.Owner + 1}의 타워입니다");
            }
            else if (existing != null && existing.Owner == MyPlayer) { _pending.Add(MatchCommand.Sell(MyTeam, MyPlayer, existing.Id)); SelectTower(-1); }
        }

        void SelectTower(int id) { _selectedTower = id; _myLane.SelectedTowerId = id; }
        public void Draw() { if (!Sim.IsOver) _pending.Add(MatchCommand.Draw(MyTeam, MyPlayer)); }
        public void SendCard(int handIndex) { if (!Sim.IsOver) _pending.Add(MatchCommand.Send(MyTeam, MyPlayer, handIndex)); }
        public void PickAugment(int index) { _pending.Add(MatchCommand.PickAugment(MyTeam, MyPlayer, index)); }
        public void Upgrade() { if (_selectedTower >= 0) _pending.Add(MatchCommand.Upgrade(MyTeam, MyPlayer, _selectedTower)); }
        public void Sell() { if (_selectedTower >= 0) { _pending.Add(MatchCommand.Sell(MyTeam, MyPlayer, _selectedTower)); SelectTower(-1); } }
        public void Merge() { if (_selectedTower >= 0) { _pending.Add(MatchCommand.Merge(MyTeam, MyPlayer, _selectedTower)); SelectTower(-1); } }
        public void Fuse(int partnerId) { if (_selectedTower >= 0) { _pending.Add(MatchCommand.Fuse(MyTeam, MyPlayer, _selectedTower, partnerId)); SelectTower(-1); } }
        public void Transfer(int toPlayer, int amount) { _pending.Add(MatchCommand.Transfer(MyTeam, MyPlayer, toPlayer, amount)); }
        void TogglePause() { if (Net != null) { ShowMsg("온라인에서는 정지할 수 없습니다"); return; } _paused = !_paused; ShowMsg(_paused ? "정지 (스페이스로 계속)" : "계속"); }

        // ─────────────────────────── HUD ───────────────────────────

        void ShowBanner(string text, float seconds = 2.5f) { _banner.text = text; _bannerLeft = seconds; }
        void ShowMsg(string text, float seconds = 2.2f) { _msg.text = text; _msgLeft = seconds; }

        int HandVersion()
        {
            var p = Sim.Player(MyTeam, MyPlayer);
            int v = p.Hand.Count * 1000 + (p.Gold > 40 ? 40 : p.Gold) + (Sim.ActiveEvent.HasValue ? 7 : 0) + p.FreeSends * 3;
            foreach (var h in p.Hand) v = v * 31 + h;
            return v;
        }

        int OfferVersion()
        {
            var p = Sim.Player(MyTeam, MyPlayer);
            int v = p.Offers.Count * 7 + (Sim.IsPaused ? 1 : 0);
            foreach (var a in p.Offers) v = v * 31 + (int)a + 1;
            return v;
        }

        int ActionVersion()
        {
            var t = _selectedTower >= 0 ? Sim.OwnLane(MyTeam).TowerAt(_selectedTower) : null;
            if (t == null) return -1;
            int v = t.Id * 100 + t.Star * 10 + (t.Upgraded ? 1 : 0);
            var p = Sim.Player(MyTeam, MyPlayer);
            v = v * 7 + Mathf.Min(p.Gold, 30);
            var mates = Sim.MergeMates(MyTeam, t); v = v * 3 + (mates.HasValue ? 1 : 0);
            foreach (var o in Sim.FuseOptions(MyTeam, t)) v = v * 31 + o.result.Id;
            return v;
        }

        void BuildCamera(LaneConfig lane, float gap)
        {
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); go.tag = "MainCamera"; cam = go.GetComponent<Camera>(); }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.62f, 0.34f);
            float totalH = gap + lane.Width + 1.2f;               // 두 라인 + 제목 여백
            const float topPx = 56f, bottomPx = 500f;             // 라인이 들어갈 화면 세로 구간 (720 기준)
            float lanePx = bottomPx - topPx;
            float unitsPerPixelH = totalH / lanePx;
            float unitsPerPixelW = (lane.Length + 3.2f) / 1280f;
            float upp = Mathf.Max(unitsPerPixelH, unitsPerPixelW);
            cam.orthographicSize = upp * 720f / 2f;
            float lanesCenterWorld = (gap + lane.Width) / 2f - 0.2f;
            float targetCenterPixel = (topPx + bottomPx) / 2f;
            cam.transform.position = new Vector3(lane.Length / 2f + 0.5f, lanesCenterWorld + (targetCenterPixel - 360f) * upp, -10);
        }

        void BuildHud()
        {
            var root = UiKit.Canvas;
            _ui = UiKit.Rect(root, "MatchUI", 0, 0, 1280, 720);
            var ui = _ui;

            // 상단 바
            UiKit.SpritePanel(ui, "TopBar", -6, -6, 1292, 58, "ui_panel_dark");
            _time = UiKit.IconLabel(ui, "Time", 16, 6, 110, 20, "icon_clock", "", 15, Color.white);
            _wave = UiKit.Label(ui, "Wave", 16, 28, 560, 18, "", 11, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 1f));
            _myHp = UiKit.IconLabel(ui, "MyHp", 140, 6, 200, 20, "icon_heart", "", 14, new Color(0.7f, 0.9f, 1f));
            _myHpBar = HpBar(ui, "MyHpBar", 164, 25, 120, UiKit.Blue);
            _enemyHp = UiKit.IconLabel(ui, "EnemyHp", 300, 6, 200, 20, "icon_skull", "", 14, new Color(1f, 0.75f, 0.75f));
            _enemyHpBar = HpBar(ui, "EnemyHpBar", 324, 25, 120, UiKit.Red);
            _gold = UiKit.IconLabel(ui, "Gold", 470, 6, 160, 22, "icon_coin", "", 17, UiKit.Gold);
            _income = UiKit.IconLabel(ui, "Income", 600, 6, 300, 20, "icon_income", "", 13, new Color(0.7f, 1f, 0.7f));
            _team = UiKit.Label(ui, "Team", 600, 28, 400, 18, "", 11, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f));
            UiKit.SpriteButton(ui, "Recipes", 900, 8, 84, 34, "합성표", ToggleRecipes, "ui_button_blue", 13);
            UiKit.SpriteButton(ui, "S1", 992, 8, 40, 34, "1배", () => { if (Net == null) Speed = 1f; }, "ui_button_grey", 12);
            UiKit.SpriteButton(ui, "S2", 1036, 8, 40, 34, "2배", () => { if (Net == null) Speed = 2f; }, "ui_button_grey", 12);
            UiKit.SpriteButton(ui, "S4", 1080, 8, 40, 34, "4배", () => { if (Net == null) Speed = 4f; }, "ui_button_grey", 12);
            UiKit.SpriteButton(ui, "Pause", 1128, 8, 56, 34, "정지", TogglePause, "ui_button_grey", 12);
            UiKit.SpriteButton(ui, "Menu", 1192, 8, 76, 34, "메뉴", ToggleMenu, "ui_button_grey", 13);

            // 배너·메시지 (두 라인 사이)
            _banner = UiKit.OutlinedLabel(ui, "Banner", 0, 262, 1280, 26, "", 19, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            _msg = UiKit.OutlinedLabel(ui, "Msg", 0, 290, 1280, 18, "", 12, TextAnchor.MiddleCenter, new Color(0.9f, 0.95f, 1f));

            // 하단 패널
            UiKit.SpritePanel(ui, "Bottom", -6, 504, 1292, 222, "ui_panel");
            _handTitle = UiKit.Label(ui, "HandTitle", 16, 510, 540, 18, "", 12, TextAnchor.MiddleLeft, UiKit.Ink);
            _hand = UiKit.Rect(ui, "Hand", 16, 530, 540, 118);
            UiKit.SpriteButton(ui, "Draw", 16, 654, 150, 34, "뽑기 (D)", Draw, "ui_button", 14);
            UiKit.Label(ui, "DrawInfo", 176, 654, 380, 34, "일반 60% · 희귀 30% · 영웅 10%\n보낼 때 팀 인컴이 오릅니다 (20초마다 수입)", 11, TextAnchor.MiddleLeft, UiKit.InkSoft);

            UiKit.Label(ui, "ShopTitle", 574, 510, 450, 18, "타워 (Q~O 단축키) — 고른 뒤 내 라인 빈 칸 클릭", 12, TextAnchor.MiddleLeft, UiKit.Ink);
            _shop = UiKit.Rect(ui, "Shop", 574, 530, 450, 66);
            float x = 0;
            foreach (var d in WaveCatalog.BasicTowers)
            {
                int id = d.Id;
                var img = UiKit.SpritePanel(_shop, "T" + id, x, 0, 46, 66, "ui_slot");
                var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img;
                b.onClick.AddListener(() => { SelectedTowerId = id; Sfx.Play("click", 0.5f); RefreshShop(); });
                UiKit.IconSprite(img.transform, "Icon", 5, 3, 36, 36, Art.Tower(d.Id, 0));
                UiKit.IconLabel(img.transform, "Cost", 6, 42, 40, 18, "icon_coin", d.Cost.ToString(), 11, UiKit.Ink);
                _shopButtons.Add(b); _shopImages.Add(img);
                x += 50;
            }
            _selectedInfo = UiKit.Label(ui, "Sel", 574, 598, 450, 30, "", 11, TextAnchor.UpperLeft, UiKit.Ink);
            _actionPanel = UiKit.Rect(ui, "Action", 574, 630, 450, 84);

            _infoPanel = UiKit.SpritePanel(ui, "Info", 1032, 510, 236, 202, "ui_panel_dark").transform;
            _mission = UiKit.Label(_infoPanel, "Mission", 10, 6, 216, 60, "", 11, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.5f));
            _synergy = UiKit.Label(_infoPanel, "Synergy", 10, 68, 216, 48, "", 11, TextAnchor.UpperLeft, new Color(0.7f, 0.9f, 0.7f));
            _eventText = UiKit.Label(_infoPanel, "Event", 10, 118, 216, 36, "", 11, TextAnchor.UpperLeft, new Color(0.9f, 0.8f, 1f));
            _intel = UiKit.Label(_infoPanel, "Intel", 10, 156, 216, 44, "", 11, TextAnchor.UpperLeft, new Color(0.8f, 0.85f, 1f));

            _augmentPanel = UiKit.SpritePanel(ui, "AugmentPanel", 190, 120, 900, 330, "ui_panel_dark").transform;
            _augmentPanel.gameObject.SetActive(false);
            _recipePanel = UiKit.SpritePanel(ui, "RecipePanel", 240, 70, 800, 430, "ui_panel").transform;
            _recipePanel.gameObject.SetActive(false);
            _menuPanel = UiKit.SpritePanel(ui, "MenuPanel", 440, 200, 400, 300, "ui_panel_dark").transform;
            _menuPanel.gameObject.SetActive(false);
            _resultPanel = UiKit.SpritePanel(ui, "ResultPanel", 340, 120, 600, 400, "ui_panel_dark").transform;
            _resultPanel.gameObject.SetActive(false);
            BuildRecipePanel();
        }

        Image HpBar(Transform parent, string name, float x, float y, float w, Color color)
        {
            UiKit.PanelBox(parent, name + "Back", x, y, w, 8, new Color(0, 0, 0, 0.5f));
            return UiKit.PanelBox(parent, name, x, y, w, 8, color);
        }

        void RefreshShop()
        {
            var p = Sim.Player(MyTeam, MyPlayer);
            _lastShopGold = p.Gold;
            for (int i = 0; i < _shopImages.Count; i++)
            {
                var d = WaveCatalog.BasicTowers[i];
                bool sel = d.Id == SelectedTowerId, can = p.Gold >= d.Cost;
                _shopImages[i].color = sel ? new Color(1f, 0.95f, 0.6f) : can ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.8f);
                _shopImages[i].transform.localScale = Vector3.one * (sel ? 1.06f : 1f);
            }
            if (_selectedInfo != null) _selectedInfo.text = $"선택: {WaveCatalog.Tower(SelectedTowerId).Name} — {TowerInfo.Describe(WaveCatalog.Tower(SelectedTowerId))}";
        }

        void BuildHand()
        {
            if (_hand == null) return;
            UiKit.Clear(_hand);
            var p = Sim.Player(MyTeam, MyPlayer);
            _lastHandVersion = HandVersion();
            _handTitle.text = $"내 손패 {p.Hand.Count}/{Sim.Cfg.HandMax} — 카드를 누르면 상대 라인으로 보냅니다" + (p.FreeSends > 0 ? $"  (무료 보내기 {p.FreeSends}회)" : "");
            const float cw = 84, ch = 118, gap = 6;
            for (int i = 0; i < p.Hand.Count; i++)
            {
                var d = WaveCatalog.Attacker(p.Hand[i]);
                int idx = i;
                int cost = p.FreeSends > 0 ? 0 : Sim.SendCostOf(d);
                string frame = d.Rarity == Rarity.Hero ? "ui_card_hero" : d.Rarity == Rarity.Rare ? "ui_card_rare" : "ui_card";
                var img = UiKit.SpritePanel(_hand, "C" + i, i * (cw + gap), 0, cw, ch, frame);
                var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); SendCard(idx); });
                bool can = p.Gold >= cost && !(Sim.ActiveEvent == EventId.Storm && d.Flying);
                img.color = can ? Color.white : new Color(0.65f, 0.65f, 0.65f, 0.9f);
                UiKit.IconSprite(img.transform, "Portrait", 14, 8, 56, 52, Art.Creep(d.Id, 0));
                if (d.Flying) UiKit.Icon(img.transform, "Fly", 62, 6, 16, "icon_wing");
                UiKit.Label(img.transform, "Name", 0, 62, cw, 16, d.Name, 12, TextAnchor.MiddleCenter, UiKit.Ink);
                UiKit.IconLabel(img.transform, "Cost", 8, 78, 40, 16, "icon_coin", cost.ToString(), 12, UiKit.Ink);
                UiKit.IconLabel(img.transform, "Inc", 46, 78, 36, 16, "icon_income", $"+{System.Math.Max(1, d.Income * Sim.Cfg.SendIncomePercent / 100)}", 10, new Color(0.25f, 0.55f, 0.25f));
                UiKit.Label(img.transform, "Stat", 4, 95, cw - 8, 16, $"체력 {d.Hp} · 누수 {d.Leak}", 9, TextAnchor.MiddleCenter, UiKit.InkSoft);
            }
            if (p.Hand.Count == 0) UiKit.Label(_hand, "Empty", 0, 40, 540, 30, "손패가 비었습니다. [뽑기]로 공격 유닛을 뽑으세요.", 13, TextAnchor.MiddleLeft, UiKit.InkSoft);
        }

        void BuildActionPanel()
        {
            if (_actionPanel == null) return;
            UiKit.Clear(_actionPanel);
            _lastActionVersion = ActionVersion();
            var t = _selectedTower >= 0 ? Sim.OwnLane(MyTeam).TowerAt(_selectedTower) : null;
            if (t == null)
            {
                _selectedTower = -1;
                UiKit.Label(_actionPanel, "Hint", 0, 0, 450, 40, "내 타워를 클릭하면 강화 · 판매 · ★합치기 · 합성을 할 수 있습니다.\n같은 타워 3개 = ★2 (공격 ×2.2), 합성 조합은 [합성표] 참고. 우클릭 = 바로 판매.", 11, TextAnchor.UpperLeft, UiKit.InkSoft);
                return;
            }
            var p = Sim.Player(MyTeam, MyPlayer);
            string star = t.Star >= 2 ? new string('★', t.Star) : "";
            UiKit.Label(_actionPanel, "Name", 0, 0, 450, 18, $"{t.Def.Name} {star}{(t.Upgraded ? " [강화됨]" : "")} — {TowerInfo.Describe(t.Def)}", 11, TextAnchor.MiddleLeft, UiKit.Ink);
            float x = 0;
            int upCost = Sim.UpgradeCostOf(MyTeam, t);
            var up = UiKit.SpriteButton(_actionPanel, "Up", x, 22, 110, 30, t.Upgraded ? "강화 완료" : $"강화 {upCost}골드", Upgrade, "ui_button_green", 12);
            up.interactable = !t.Upgraded && p.Gold >= upCost; x += 116;
            UiKit.SpriteButton(_actionPanel, "Sell", x, 22, 100, 30, $"판매 +{MatchSim.SellValueOf(t)}", Sell, "ui_button_grey", 12); x += 106;
            var mates = Sim.MergeMates(MyTeam, t);
            var mg = UiKit.SpriteButton(_actionPanel, "Merge", x, 22, 110, 30, t.Star >= 3 ? "★★★ 최대" : $"★{t.Star + 1} 합치기", Merge, "ui_button", 12);
            mg.interactable = mates.HasValue; x += 116;
            var opts = Sim.FuseOptions(MyTeam, t);
            float fy = 56;
            if (opts.Count == 0)
                UiKit.Label(_actionPanel, "NoFuse", 0, fy, 450, 26, t.Def.IsFused ? "합성 타워는 더 합성할 수 없습니다 (같은 것 3개로 ★ 올리기는 가능)" : "합성 상대 없음 — 합성표에서 짝이 되는 타워를 지으세요", 10, TextAnchor.MiddleLeft, UiKit.InkSoft);
            for (int i = 0; i < opts.Count && i < 3; i++)
            {
                var (partner, result) = opts[i];
                var partnerTower = Sim.OwnLane(MyTeam).TowerAt(partner);
                UiKit.SpriteButton(_actionPanel, "Fuse" + i, i * 150, fy, 146, 28, $"합성 → {result.Name}", () => Fuse(partner), "ui_button_blue", 11);
            }
        }

        void BuildAugmentPanel()
        {
            if (_augmentPanel == null) return;
            UiKit.Clear(_augmentPanel);
            var p = Sim.Player(MyTeam, MyPlayer);
            _lastOfferVersion = OfferVersion();
            bool show = p.Offers.Count > 0 && !BotPlaysHuman;
            _augmentPanel.gameObject.SetActive(show);
            if (!show) return;
            _augmentPanel.SetAsLastSibling();
            UiKit.Label(_augmentPanel, "Title", 0, 16, 900, 36, "", 18, TextAnchor.MiddleCenter, UiKit.Accent);
            for (int i = 0; i < p.Offers.Count; i++)
            {
                int idx = i;
                var a = p.Offers[i];
                var img = UiKit.SpritePanel(_augmentPanel, "Offer" + i, 30 + i * 285, 70, 270, 230, "ui_panel");
                var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.6f); PickAugment(idx); });
                string icon = FunCatalog.AugmentGroup(a) switch { "자원" => "aug_resource", "강화" => "aug_power", "방해" => "aug_hinder", _ => "aug_info" };
                UiKit.Icon(img.transform, "Icon", 119, 12, 32, icon);
                UiKit.Label(img.transform, "G", 10, 48, 250, 20, $"[{FunCatalog.AugmentGroup(a)}]", 12, TextAnchor.MiddleCenter, UiKit.InkSoft);
                UiKit.Label(img.transform, "N", 10, 70, 250, 34, FunCatalog.AugmentName(a), 24, TextAnchor.MiddleCenter, UiKit.Ink);
                UiKit.Label(img.transform, "D", 16, 112, 238, 110, FunCatalog.AugmentDesc(a), 13, TextAnchor.UpperCenter, UiKit.Ink);
            }
            RefreshAugmentTitle();
        }

        void RefreshAugmentTitle()
        {
            if (!_augmentPanel.gameObject.activeSelf) return;
            UiKit.SetText(_augmentPanel, "Title", Sim.IsPaused ? $"증강을 하나 고르세요 — {Mathf.CeilToInt(Sim.PauseLeft / (float)Sim.Cfg.TicksPerSecond)}초 뒤 자동 선택 (양쪽 다 멈춤)" : "추가 증강 선택 (게임은 계속 진행)");
        }

        void BuildRecipePanel()
        {
            var r = _recipePanel;
            UiKit.Label(r, "Title", 0, 14, 800, 30, "합성표 — 재료 두 타워를 내 라인에 지은 뒤, 한쪽을 클릭해 [합성]", 16, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.Label(r, "Sub", 0, 42, 800, 20, "같은 타워 3개 → ★2 (공격 ×2.2, 사거리 +0.5) · ★2 3개 → ★3 (공격 ×5, 사거리 +1). 합성 타워도 ★을 올릴 수 있습니다.", 11, TextAnchor.MiddleCenter, UiKit.InkSoft);
            int i = 0;
            foreach (var f in WaveCatalog.FusedTowers)
            {
                float x = 24 + (i % 2) * 385, y = 70 + (i / 2) * 84;
                var a = WaveCatalog.Tower(f.RecipeA); var b = WaveCatalog.Tower(f.RecipeB);
                UiKit.SpritePanel(r, "Row" + i, x, y, 370, 76, "ui_slot");
                UiKit.IconSprite(r, "A" + i, x + 8, y + 6, 40, 40, Art.Tower(a.Id, 0));
                UiKit.Label(r, "An" + i, x, y + 48, 56, 16, a.Name, 9, TextAnchor.MiddleCenter, UiKit.InkSoft);
                UiKit.Label(r, "Plus" + i, x + 52, y + 12, 20, 30, "+", 20, TextAnchor.MiddleCenter, UiKit.Ink);
                UiKit.IconSprite(r, "B" + i, x + 74, y + 6, 40, 40, Art.Tower(b.Id, 0));
                UiKit.Label(r, "Bn" + i, x + 66, y + 48, 56, 16, b.Name, 9, TextAnchor.MiddleCenter, UiKit.InkSoft);
                UiKit.Label(r, "Arrow" + i, x + 120, y + 12, 24, 30, "→", 20, TextAnchor.MiddleCenter, UiKit.Ink);
                UiKit.IconSprite(r, "R" + i, x + 148, y + 4, 44, 44, Art.Tower(f.Id, 0));
                UiKit.Label(r, "Rn" + i, x + 198, y + 6, 170, 20, f.Name, 13, TextAnchor.MiddleLeft, UiKit.Ink);
                UiKit.Label(r, "Rd" + i, x + 198, y + 26, 170, 48, TowerInfo.Describe(f), 9, TextAnchor.UpperLeft, UiKit.InkSoft);
                i++;
            }
            UiKit.SpriteButton(r, "Close", 340, 396, 120, 30, "닫기 (ESC)", ToggleRecipes, "ui_button_grey", 12);
        }

        void ToggleRecipes()
        {
            _recipeOpen = !_recipeOpen;
            _recipePanel.gameObject.SetActive(_recipeOpen);
            if (_recipeOpen) _recipePanel.SetAsLastSibling();
        }

        void ToggleMenu()
        {
            _menuOpen = !_menuOpen;
            _menuPanel.gameObject.SetActive(_menuOpen);
            if (!_menuOpen) { _paused = false; return; }
            _paused = Net == null;
            _menuPanel.SetAsLastSibling();
            UiKit.Clear(_menuPanel);
            var m = _menuPanel;
            UiKit.Label(m, "Title", 0, 16, 400, 30, "메뉴 (게임 정지)", 18, TextAnchor.MiddleCenter, UiKit.Accent);
            UiKit.SpriteButton(m, "Resume", 100, 60, 200, 36, "계속하기", ToggleMenu, "ui_button_green", 14);
            Text sfxT = null, musT = null;
            UiKit.SpriteButton(m, "SfxDown", 60, 110, 40, 32, "−", () => { Sfx.SfxVolume -= 0.1f; sfxT.text = $"효과음 {Mathf.RoundToInt(Sfx.SfxVolume * 100)}%"; }, "ui_button_grey", 14);
            sfxT = UiKit.Label(m, "SfxT", 104, 110, 192, 32, $"효과음 {Mathf.RoundToInt(Sfx.SfxVolume * 100)}%", 14, TextAnchor.MiddleCenter, Color.white);
            UiKit.SpriteButton(m, "SfxUp", 300, 110, 40, 32, "+", () => { Sfx.SfxVolume += 0.1f; sfxT.text = $"효과음 {Mathf.RoundToInt(Sfx.SfxVolume * 100)}%"; }, "ui_button_grey", 14);
            UiKit.SpriteButton(m, "MusDown", 60, 150, 40, 32, "−", () => { Sfx.MusicVolume -= 0.1f; musT.text = $"음악 {Mathf.RoundToInt(Sfx.MusicVolume * 100)}%"; }, "ui_button_grey", 14);
            musT = UiKit.Label(m, "MusT", 104, 150, 192, 32, $"음악 {Mathf.RoundToInt(Sfx.MusicVolume * 100)}%", 14, TextAnchor.MiddleCenter, Color.white);
            UiKit.SpriteButton(m, "MusUp", 300, 150, 40, 32, "+", () => { Sfx.MusicVolume += 0.1f; musT.text = $"음악 {Mathf.RoundToInt(Sfx.MusicVolume * 100)}%"; }, "ui_button_grey", 14);
            if (Net == null) UiKit.SpriteButton(m, "Restart", 100, 200, 200, 34, "새 판 (다른 시드)", () => { Seed++; ToggleMenu(); Restart(); }, "ui_button_blue", 13);
            else UiKit.Label(m, "NetInfo", 0, 200, 400, 34, $"온라인 {(Net.IsHost ? "호스트" : "참가자")} · 핑 {Net.PingMs}ms", 13, TextAnchor.MiddleCenter, Color.white);
            UiKit.SpriteButton(m, "Title", 100, 244, 200, 34, "타이틀로", () => { OnExit?.Invoke(); }, "ui_button_grey", 13);
        }

        void ShowResult()
        {
            var r = _resultPanel;
            r.gameObject.SetActive(true);
            r.SetAsLastSibling();
            UiKit.Clear(r);
            bool win = Sim.Winner == MyTeam, draw = Sim.Winner < 0;
            _banner.text = ""; _msg.text = ""; _bannerLeft = _msgLeft = 0; SelectTower(-1);
            GameSession.MatchesPlayed++; if (win) GameSession.Wins++;
            Sfx.StopMusic();
            Sfx.Play(win ? "win" : "lose", 0.9f, 1f, 0f);
            UiKit.Label(r, "Title", 0, 20, 600, 50, draw ? "무승부" : win ? "승리!" : "패배...", 36, TextAnchor.MiddleCenter, win ? UiKit.Gold : draw ? Color.white : new Color(0.8f, 0.85f, 1f));
            UiKit.Label(r, "Reason", 0, 72, 600, 24, $"({Sim.EndReason})  {Sim.Seconds / 60}:{Sim.Seconds % 60:00}", 14, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.9f));
            var my = Sim.OwnLane(MyTeam); var en = Sim.OwnLane(EnemyTeam);
            int myStars = 0, myFused = 0; foreach (var t in my.Towers) if (t.Alive) { if (t.Star > 1) myStars++; if (t.Def.IsFused) myFused++; }
            var p = Sim.Player(MyTeam, MyPlayer);
            string stats =
                $"내 기지 {my.BaseHp} / 상대 기지 {en.BaseHp}\n" +
                $"내 라인 처치 {my.Kills} · 누수 {my.Leaked}      상대 라인 처치 {en.Kills} · 누수 {en.Leaked}\n" +
                $"보낸 유닛 {p.Sent} · 뽑기 {p.Drawn} · 받은 수입 {p.IncomeReceived}\n" +
                $"팀 인컴 {Sim.Teams[MyTeam].Income} (상대 {Sim.Teams[EnemyTeam].Income}) · ★타워 {myStars} · 합성 타워 {myFused}\n" +
                $"증강: {AugmentList(p)}   미션: {FunCatalog.MissionName(p.Mission)} {(p.MissionDone ? "달성" : "미달성")}";
            UiKit.Label(r, "Stats", 40, 110, 520, 150, stats, 13, TextAnchor.UpperLeft, Color.white);
            UiKit.Label(r, "Record", 0, 270, 600, 20, $"전적 {GameSession.Wins}승 {GameSession.MatchesPlayed - GameSession.Wins}패", 12, TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.9f));
            if (Net == null) UiKit.SpriteButton(r, "Again", 120, 320, 170, 40, "다시 하기", () => { Seed++; Restart(); }, "ui_button_green", 15);
            UiKit.SpriteButton(r, "Title", Net == null ? 310 : 215, 320, 170, 40, "타이틀로", () => OnExit?.Invoke(), "ui_button_grey", 15);
        }

        static string AugmentList(PlayerEcon p)
        {
            if (p.Augments.Count == 0) return "없음";
            var sb = new System.Text.StringBuilder();
            foreach (var a in p.Augments) sb.Append(FunCatalog.AugmentName(a)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        string MissionText()
        {
            var p = Sim.Player(MyTeam, MyPlayer);
            string s = $"비밀 미션 [{FunCatalog.MissionName(p.Mission)}]\n{FunCatalog.MissionDesc(p.Mission)}";
            if (p.MissionDone) s += "\n✔ 달성!";
            if (p.Augments.Count > 0) s += "\n내 증강: " + AugmentList(p);
            return s;
        }

        string SynergyText()
        {
            var lane = Sim.OwnLane(MyTeam);
            string tribe = $"숲 {lane.TribeCount[0]}{(lane.Forest5 ? "★★" : lane.Forest3 ? "★" : "")}  불 {lane.TribeCount[1]}{(lane.Fire5 ? "★★" : lane.Fire3 ? "★" : "")}  기계 {lane.TribeCount[2]}{(lane.Machine5 ? "★★" : lane.Machine3 ? "★" : "")}";
            var rows = new List<string>();
            for (int r = 0; r < 3; r++)
            {
                string tag = "";
                if (lane.RowWarrior2[r]) tag += "전사 ";
                if (lane.RowArcher2[r]) tag += "궁수 ";
                if (lane.RowMage2[r]) tag += "마법사 ";
                if (tag.Length > 0) rows.Add($"{(r == 0 ? "앞" : r == 1 ? "중" : "뒤")}:{tag.Trim()}");
            }
            string job = rows.Count == 0 ? "같은 줄 같은 직업 2개 → 직업 시너지" : string.Join(" · ", rows);
            return $"시너지: {tribe} (★3 ★★5)\n{job}";
        }

        string EventText()
        {
            if (Sim.ActiveEvent.HasValue)
            {
                int left = Mathf.Max(0, (Sim.EventEndTick - Sim.GameTick) / Sim.Cfg.TicksPerSecond);
                return $"이벤트 [{FunCatalog.EventName(Sim.ActiveEvent.Value)}] {left}초\n{FunCatalog.EventDesc(Sim.ActiveEvent.Value)}";
            }
            if (Sim.NextEvent.HasValue && Sim.EventRound < Sim.Cfg.EventSeconds.Length)
                return $"다음 이벤트 {Sim.Cfg.EventSeconds[Sim.EventRound] / 60}:{Sim.Cfg.EventSeconds[Sim.EventRound] % 60:00} (30초 전 공개)";
            return "이벤트 없음";
        }

        string IntelText()
        {
            var p = Sim.Player(MyTeam, MyPlayer); var e = Sim.Player(EnemyTeam, 0);
            var sb = new System.Text.StringBuilder();
            if (p.Has(AugmentId.Accountant)) sb.Append($"[회계] 상대 골드 {e.Gold}\n");
            if (p.Has(AugmentId.Scout))
            {
                sb.Append("[정찰] 상대 손패: ");
                for (int i = 0; i < e.Hand.Count && i < 2; i++) sb.Append(WaveCatalog.Attacker(e.Hand[i]).Name).Append(i == 0 && e.Hand.Count > 1 ? ", " : "");
                if (e.Hand.Count == 0) sb.Append("없음");
                sb.Append('\n');
            }
            int enemyAA = 0; foreach (var t in Sim.OwnLane(EnemyTeam).Towers) if (t.Alive && (t.Def.AntiAir || t.ForceAntiAir)) enemyAA++;
            sb.Append($"상대 대공 타워 {enemyAA}개 · 상대 증강 {e.Augments.Count}개");
            return sb.ToString();
        }

        string TeamText()
        {
            if (PlayersPerTeam == 1) return "";
            var sb = new System.Text.StringBuilder("팀원: ");
            for (int i = 0; i < PlayersPerTeam; i++) { if (i == MyPlayer) continue; var q = Sim.Player(MyTeam, i); sb.Append($"P{i + 1} 골드 {q.Gold} 손패 {q.Hand.Count}   "); }
            return sb.ToString();
        }

        void RefreshHud()
        {
            if (_time == null || Sim == null) return;
            _mission.text = MissionText(); _synergy.text = SynergyText(); _eventText.text = EventText(); _intel.text = IntelText(); _team.text = TeamText();
            RefreshAugmentTitle();
            var p = Sim.Player(MyTeam, MyPlayer);
            var my = Sim.OwnLane(MyTeam); var en = Sim.OwnLane(EnemyTeam);
            int sec = Sim.Seconds;
            int nextIn = my.TicksToNextWave / my.Cfg.TicksPerSecond;
            int incomeIn = Sim.Cfg.IncomeIntervalSeconds - (sec % Sim.Cfg.IncomeIntervalSeconds);
            _time.text = $"{sec / 60}:{sec % 60:00} / {Sim.Cfg.MatchSeconds / 60}:00" + (Speed != 1f && Net == null ? $" ×{Speed:0}" : "") + (_paused && !_menuOpen ? " ■" : "") + (Sim.IsPaused ? " 증강" : "") + (Net != null ? $"  핑 {Mathf.Max(0, Net.PingMs)}" : "") + (_stallTime > 0.5f ? " 대기…" : "") + (Net != null && Net.DesyncInfo != null ? " [어긋남!]" : "");
            _wave.text = my.NextWaveIndex < WaveCatalog.BaseWaves.Length ? $"다음 웨이브 {my.NextWaveIndex + 1} ({nextIn}초): {WaveCatalog.Describe(my.NextWaveIndex)}" : "기본 웨이브 끝 — 이제 보내기 싸움";
            _myHp.text = $"내 기지 {my.BaseHp}/{my.Cfg.BaseHp}";
            _enemyHp.text = $"상대 기지 {en.BaseHp}/{en.Cfg.BaseHp}";
            _myHpBar.rectTransform.sizeDelta = new Vector2(120f * Mathf.Clamp01(my.BaseHp / (float)my.Cfg.BaseHp), 8);
            _enemyHpBar.rectTransform.sizeDelta = new Vector2(120f * Mathf.Clamp01(en.BaseHp / (float)en.Cfg.BaseHp), 8);
            _gold.text = $"{p.Gold}";
            _income.text = $"팀 인컴 {Sim.Teams[MyTeam].Income} (상대 {Sim.Teams[EnemyTeam].Income}) · 다음 수입 {incomeIn}초 · 내 라인 처치 {my.Kills} 누수 {my.Leaked}";
        }
    }
}
