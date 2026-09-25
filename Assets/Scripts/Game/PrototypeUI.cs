using System.Collections.Generic;
using System.Text;
using LaneBattle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>
    /// 색깔 네모로 그리는 첫 플레이어블 화면. 씬에는 이 컴포넌트 하나만 있으면 된다.
    /// 1v1, 사람(팀0) 대 성향 AI(팀1). 확정 후 공개 → 전투 → 다음 턴 순으로 멈춰 보여준다.
    /// </summary>
    public sealed class PrototypeUI : MonoBehaviour
    {
        public MatchController Match { get; private set; }
        public ulong StartSeed = 1;

        const float W = 1280, H = 720;
        static readonly float[] LaneX = { 20, 440, 860 };
        const float LaneW = 400, TileW = 92, TileH = 62, TileGap = 6;
        static readonly Color Red = new Color(1f, 0.45f, 0.4f);
        static readonly Color Green = new Color(0.55f, 0.9f, 0.55f);

        Transform _root;
        Text _phaseTitle, _goal, _rightInfo, _info, _synergy, _manaText;
        readonly Transform[] _enemyRows = new Transform[3], _myRows = new Transform[3];
        readonly Text[] _enemyTower = new Text[3], _myTower = new Text[3], _laneRule = new Text[3], _enemyHist = new Text[3], _combat = new Text[3];
        readonly Button[] _placeButtons = new Button[3];
        Transform _hand, _augmentPanel, _overlay, _help;
        Button _main, _undo;
        bool _helpShown;

        void Awake()
        {
            Match = new MatchController(StartSeed, 1);
            Match.Changed += Refresh;
            BuildStatic();
            _helpShown = PlayerPrefs.GetInt("help_shown", 0) == 1;
            Refresh();
        }

        void Start()
        {
            // 검증용: -autoplay N [-phase reveal|combat] [-screenshot 경로]
            var args = System.Environment.GetCommandLineArgs();
            string stopPhase = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-phase") stopPhase = args[i + 1];
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-autoplay" && int.TryParse(args[i + 1], out int turns)) AutoPlay(turns, stopPhase);
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshot") StartCoroutine(ScreenshotAndQuit(args[i + 1]));
        }

        void AutoPlay(int turns, string stopPhase)
        {
            DismissHelp();
            for (int t = 0; t < turns && !Match.IsOver; t++)
            {
                if (Match.NeedsAugmentChoice) Match.ChooseAugment(0);
                bool placed = true;
                while (placed)
                {
                    placed = false;
                    var hand = Match.AvailableHand();
                    for (int i = 0; i < hand.Count && !placed; i++)
                        for (int l = 0; l < 3 && !placed; l++)
                            if (Match.CanAddPending(hand[i], (Lane)l)) { Match.SelectCard(i); placed = Match.PlaceSelected((Lane)l); }
                }
                Match.Confirm();
                bool last = t == turns - 1;
                if (last && stopPhase == "reveal") return;
                Match.Advance();
                if (last && stopPhase == "combat") return;
                Match.Advance();
            }
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1.5f);
            Application.Quit();
        }

        void OnDestroy() { if (Match != null) Match.Changed -= Refresh; }

        // ─────────────────────────── 고정 레이아웃 ───────────────────────────

        void BuildStatic()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.matchWidthOrHeight = 0.5f;

            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);

            _root = UiKit.PanelBox(canvasGo.transform, "Root", 0, 0, W, H, UiKit.Bg).transform;
            var rootRt = (RectTransform)_root;
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one; rootRt.sizeDelta = Vector2.zero; rootRt.anchoredPosition = Vector2.zero;

            _phaseTitle = UiKit.Label(_root, "Phase", 20, 8, 720, 30, "", 22, TextAnchor.MiddleLeft, UiKit.Accent);
            _goal = UiKit.Label(_root, "Goal", 20, 38, 720, 24, "", 13, TextAnchor.MiddleLeft);
            _rightInfo = UiKit.Label(_root, "RightInfo", 760, 8, 500, 54, "", 13, TextAnchor.UpperRight);

            for (int l = 0; l < 3; l++)
            {
                float x = LaneX[l];
                var col = UiKit.PanelBox(_root, "Lane" + l, x, 66, LaneW, 330, UiKit.Panel).transform;
                UiKit.Label(col, "Name", 0, 4, LaneW, 22, Names.Lane((Lane)l), 18, TextAnchor.MiddleCenter, UiKit.Accent);
                _enemyTower[l] = UiKit.Label(col, "EnemyTower", 8, 28, LaneW - 16, 20, "", 14, TextAnchor.MiddleLeft);
                _enemyRows[l] = UiKit.Rect(col, "EnemyRow", 8, 50, LaneW - 16, TileH);
                _enemyHist[l] = UiKit.Label(col, "Hist", 8, 114, LaneW - 16, 18, "", 11, TextAnchor.MiddleLeft, new Color(0.75f, 0.78f, 0.85f));
                _laneRule[l] = UiKit.Label(col, "Rule", 8, 132, LaneW - 16, 20, "", 13, TextAnchor.MiddleCenter, UiKit.Accent);
                _myRows[l] = UiKit.Rect(col, "MyRow", 8, 154, LaneW - 16, TileH);
                _myTower[l] = UiKit.Label(col, "MyTower", 8, 220, LaneW - 16, 20, "", 14, TextAnchor.MiddleLeft);
                int lane = l;
                _placeButtons[l] = UiKit.ButtonBox(col, "Place", 8, 244, LaneW - 16, 36, "여기에 배치", () => Match.PlaceSelected((Lane)lane));
                _combat[l] = UiKit.Label(col, "Combat", 8, 284, LaneW - 16, 44, "", 12, TextAnchor.UpperLeft);
            }

            UiKit.PanelBox(_root, "InfoBg", 20, 406, 580, 160, UiKit.Panel);
            _info = UiKit.Label(_root, "Info", 30, 412, 560, 150, "", 13);
            UiKit.PanelBox(_root, "SynBg", 610, 406, 290, 160, UiKit.Panel);
            _synergy = UiKit.Label(_root, "Synergy", 618, 410, 276, 154, "", 12);

            var side = UiKit.PanelBox(_root, "Side", 920, 406, 340, 160, UiKit.Panel).transform;
            _manaText = UiKit.Label(side, "Mana", 10, 6, 320, 32, "", 20, TextAnchor.MiddleCenter, UiKit.Accent);
            _main = UiKit.ButtonBox(side, "Main", 10, 44, 320, 52, "", () => OnMain(), new Color(0.22f, 0.55f, 0.35f), 18);
            _undo = UiKit.ButtonBox(side, "Undo", 10, 104, 100, 44, "되돌리기", () => Match.UndoLast(), null, 14);
            UiKit.ButtonBox(side, "Restart", 120, 104, 100, 44, "새 판", () => { Match.Restart(Match.Seed + 1); }, null, 14);
            UiKit.ButtonBox(side, "Help", 230, 104, 100, 44, "도움말", () => ShowHelp(), null, 14);

            UiKit.PanelBox(_root, "HandBg", 20, 576, 1240, 130, UiKit.Panel);
            _hand = UiKit.Rect(_root, "Hand", 30, 582, 1220, 118);

            _augmentPanel = UiKit.PanelBox(_root, "AugmentPanel", 240, 150, 800, 300, new Color(0.1f, 0.1f, 0.12f, 0.97f)).transform;
            _overlay = UiKit.PanelBox(_root, "Overlay", 340, 200, 600, 260, new Color(0.08f, 0.08f, 0.1f, 0.97f)).transform;
            _help = UiKit.PanelBox(_root, "Help", 160, 60, 960, 600, new Color(0.08f, 0.08f, 0.1f, 0.98f)).transform;
            BuildHelp();
        }

        void OnMain()
        {
            if (Match.Phase == MatchPhase.Plan) Match.Confirm();
            else Match.Advance();
        }

        void ShowHelp() { _help.gameObject.SetActive(true); }
        void DismissHelp() { _helpShown = true; PlayerPrefs.SetInt("help_shown", 1); _help.gameObject.SetActive(false); }

        void BuildHelp()
        {
            UiKit.Label(_help, "T", 0, 20, 960, 40, "이 게임은 이렇게 합니다", 26, TextAnchor.MiddleCenter, UiKit.Accent);
            string body =
                "• 목표: 탑·미드·봇 세 라인에 있는 상대 타워 3개 중 2개를 먼저 부수면 승리. 7턴 안에 못 부수면 부순 수 → 남은 체력으로 판정.\n\n" +
                "• 마나는 턴 번호만큼 (1턴 1, 7턴 7). 카드 왼쪽 위 숫자가 코스트. 남은 마나는 사라지니 다 쓰는 게 보통 이득.\n\n" +
                "• 카드를 고르고 라인의 [여기에 배치]를 누른 뒤 [확정]. 상대도 같은 순간 몰래 놓습니다. 확정하면 둘 다 공개되고 라인마다 전투.\n\n" +
                "• 전투: 각 유닛이 상대 맨 앞 유닛을 때립니다. 상대 유닛이 없는 라인은 타워를 때립니다. 유닛은 죽을 때까지 그 라인에 남습니다.\n\n" +
                "• 시너지: 같은 계열(숲·불·기계) 3명·5명을 모으면 보너스, 같은 직업(전사·궁수·마법사) 2명이 같은 라인이면 보너스. 오른쪽 아래 현황판에 나옵니다.\n\n" +
                "• 2턴·5턴엔 증강(영구 버프)을 하나 고르고, 3턴·5턴엔 한 라인에 특별 규칙이 생깁니다.\n\n" +
                "• 비밀 미션을 달성하면 마나 +3, 타워 +2. 상대에게도 미션이 있어서 이상한 수를 둘 때가 있습니다.\n\n" +
                "핵심은 하나입니다. 상대가 어디에 놓을지 읽고, 내 힘을 어디에 걸지 정하세요. 상대의 지난 배치 기록이 각 라인에 표시됩니다.";
            UiKit.Label(_help, "B", 40, 70, 880, 460, body, 15);
            UiKit.ButtonBox(_help, "Ok", 380, 536, 200, 46, "시작하기", () => DismissHelp(), new Color(0.22f, 0.55f, 0.35f), 18);
        }

        // ─────────────────────────── 상태 → 화면 ───────────────────────────

        public void Refresh()
        {
            var e = Match.Engine; var s = e.State; var me = Match.Human;
            var myTeam = e.Team(MatchController.HumanTeam); var enemy = e.Team(MatchController.EnemyTeam);
            var phase = Match.Phase;
            var r = Match.Report;

            _phaseTitle.text = phase switch
            {
                MatchPhase.Plan => $"{Match.ShownTurn}턴 / {e.Config.Turns}턴  ·  배치 단계: 카드를 골라 라인에 놓으세요 (상대는 못 봅니다)",
                MatchPhase.Reveal => $"{Match.ShownTurn}턴  ·  공개! 양쪽 배치가 드러났습니다",
                _ => $"{Match.ShownTurn}턴  ·  전투 결과",
            };
            _goal.text = $"목표: 상대 타워 3개 중 2개 파괴   |   부순 타워 나 {enemy.TowersDestroyedCount()} : 상대 {myTeam.TowersDestroyedCount()}   |   타워 체력 합 나 {myTeam.TowerHpSum()} : 상대 {enemy.TowerHpSum()}";

            var sb = new StringBuilder();
            sb.Append("비밀 미션: ").Append(Names.Mission(me.Mission)).Append(me.MissionDone ? " (달성!)" : "\n" + Names.MissionDesc(me.Mission, e.Config));
            if (me.Augments.Count > 0)
            {
                sb.Append("\n내 증강: ");
                foreach (var a in me.Augments) sb.Append(Names.Augment(a)).Append(' ');
            }
            _rightInfo.text = sb.ToString();

            for (int l = 0; l < 3; l++)
            {
                var lane = (Lane)l;
                string eDelta = phase == MatchPhase.Combat && r.Combat[l].TowerDamage[0] > 0 ? $"   -{r.Combat[l].TowerDamage[0]}" : "";
                string mDelta = phase == MatchPhase.Combat && r.Combat[l].TowerDamage[1] > 0 ? $"   -{r.Combat[l].TowerDamage[1]}" : "";
                _enemyTower[l].text = enemy.TowerDestroyed[l] ? "상대 타워: 파괴됨" : $"상대 타워: {enemy.TowerHp[l]}{eDelta}";
                _enemyTower[l].color = eDelta.Length > 0 ? Green : UiKit.TextColor;
                _myTower[l].text = myTeam.TowerDestroyed[l] ? "내 타워: 파괴됨" : $"내 타워: {myTeam.TowerHp[l]}{mDelta}";
                _myTower[l].color = mDelta.Length > 0 ? Red : UiKit.TextColor;

                var rule = s.RuleAt(lane);
                _laneRule[l].text = rule.HasValue ? $"[{Names.LaneRule(rule.Value)}] {Names.LaneRuleDesc(rule.Value)}" : "";
                _enemyHist[l].text = Match.EnemyHistory.Count == 0 ? "상대 배치 기록: 아직 없음" : "상대 배치 기록: " + LaneHistory(l);

                UiKit.Clear(_enemyRows[l]);
                int i = 0;
                foreach (var u in e.VisibleUnits(MatchController.EnemyTeam, lane)) Tile(_enemyRows[l], i++, u, phase == MatchPhase.Reveal && u.PlacedTurn == r.Turn);
                int hiddenCount = e.LaneUnits(MatchController.EnemyTeam, lane).Count - i;
                for (int h = 0; h < hiddenCount; h++) HiddenTile(_enemyRows[l], i++);
                if (phase == MatchPhase.Combat) foreach (var d in r.DeathsIn(MatchController.EnemyTeam, lane)) GhostTile(_enemyRows[l], i++, d.UnitName);

                UiKit.Clear(_myRows[l]);
                i = 0;
                foreach (var u in e.LaneUnits(MatchController.HumanTeam, lane)) Tile(_myRows[l], i++, u, phase == MatchPhase.Reveal && u.PlacedTurn == r.Turn);
                if (phase == MatchPhase.Combat) foreach (var d in r.DeathsIn(MatchController.HumanTeam, lane)) GhostTile(_myRows[l], i++, d.UnitName);
                foreach (var p in Match.Pending) if (p.Lane == lane) PendingTile(_myRows[l], i++, Catalog.Unit(p.UnitDefId));

                var sel = Match.SelectedUnitDefId;
                bool canPlace = phase == MatchPhase.Plan && sel.HasValue && Match.CanAddPending(sel.Value, lane);
                _placeButtons[l].interactable = canPlace;
                _placeButtons[l].GetComponentInChildren<Text>().text = canPlace
                    ? $"여기에 배치 ({e.CostOf(MatchController.HumanTeam, sel.Value, lane)} 마나)"
                    : $"슬롯 {e.LaneUnits(MatchController.HumanTeam, lane).Count + Match.PendingCountInLane(lane)}/{e.LaneCapacity(lane)}";

                _combat[l].text = phase == MatchPhase.Combat ? CombatSummary(l) : phase == MatchPhase.Reveal ? RevealSummary(l) : "";
            }

            _manaText.text = phase == MatchPhase.Plan ? $"마나 {Match.ManaLeft} / {me.Mana}" : "";
            _main.GetComponentInChildren<Text>().text = phase switch
            {
                MatchPhase.Plan => "확정 (공개 & 전투)",
                MatchPhase.Reveal => "다음: 전투 결과 보기 ▶",
                _ => Match.IsOver ? "결과 보기 ▶" : "다음 턴으로 ▶",
            };
            _main.interactable = !(phase == MatchPhase.Plan && (Match.IsOver || Match.NeedsAugmentChoice));
            _undo.interactable = phase == MatchPhase.Plan && Match.Pending.Count > 0;

            _info.text = InfoText();
            _synergy.text = SynergyText();

            BuildHand();
            BuildAugmentPanel();
            BuildOverlay();
            _help.gameObject.SetActive(!_helpShown);
            _help.SetAsLastSibling();
        }

        string LaneHistory(int l)
        {
            var parts = new List<string>();
            var h = Match.EnemyHistory;
            for (int i = System.Math.Max(0, h.Count - 4); i < h.Count; i++) parts.Add($"{i + 1}턴 {h[i][l]}개");
            return string.Join(", ", parts);
        }

        string RevealSummary(int l)
        {
            var r = Match.Report; var lane = (Lane)l;
            var mine = new List<string>(); var theirs = new List<string>();
            foreach (var p in r.Placements) if (p.Lane == lane) (p.Team == MatchController.HumanTeam ? mine : theirs).Add(p.UnitName);
            string a = theirs.Count == 0 ? "상대: 배치 없음" : "상대: " + string.Join(", ", theirs);
            string b = mine.Count == 0 ? "나: 배치 없음" : "나: " + string.Join(", ", mine);
            return a + "\n" + b;
        }

        string CombatSummary(int l)
        {
            var r = Match.Report; var c = r.Combat[l]; var lane = (Lane)l;
            var sb = new StringBuilder();
            sb.Append("내 공격 → ");
            if (c.ChainDamage[0] > 0) sb.Append($"상대 유닛 {c.ChainDamage[0]} 피해");
            if (c.TowerDamage[0] > 0) sb.Append(c.ChainDamage[0] > 0 ? ", " : "").Append($"상대 타워 -{c.TowerDamage[0]}");
            if (c.ChainDamage[0] == 0 && c.TowerDamage[0] == 0) sb.Append("없음");
            sb.Append("\n상대 공격 → ");
            if (c.ChainDamage[1] > 0) sb.Append($"내 유닛 {c.ChainDamage[1]} 피해");
            if (c.TowerDamage[1] > 0) sb.Append(c.ChainDamage[1] > 0 ? ", " : "").Append($"내 타워 -{c.TowerDamage[1]}");
            if (c.ChainDamage[1] == 0 && c.TowerDamage[1] == 0) sb.Append("없음");
            var dead = new List<string>();
            foreach (var d in r.DeathsIn(MatchController.EnemyTeam, lane)) dead.Add("상대 " + d.UnitName);
            foreach (var d in r.DeathsIn(MatchController.HumanTeam, lane)) dead.Add("내 " + d.UnitName);
            if (dead.Count > 0) sb.Append("\n사망: ").Append(string.Join(", ", dead));
            if (c.TowerDestroyed[1] && c.TowerDamage[0] > 0) sb.Append("\n★ 상대 타워 파괴!");
            if (c.TowerDestroyed[0] && c.TowerDamage[1] > 0) sb.Append("\n✖ 내 타워 파괴됨");
            return sb.ToString();
        }

        string InfoText()
        {
            var e = Match.Engine; var r = Match.Report;
            switch (Match.Phase)
            {
                case MatchPhase.Reveal:
                {
                    var sb = new StringBuilder();
                    int total = 0; for (int l = 0; l < 3; l++) total += r.PlacedCount(MatchController.EnemyTeam, (Lane)l);
                    sb.Append($"상대가 이번 턴 놓은 유닛: {total}개  (탑 {r.PlacedCount(1, Lane.Top)} · 미드 {r.PlacedCount(1, Lane.Mid)} · 봇 {r.PlacedCount(1, Lane.Bot)})\n");
                    sb.Append("새로 놓인 유닛은 흰 테두리로 표시됩니다. 내 예측이 맞았는지 보고 [다음]을 누르세요.\n\n");
                    foreach (var (t, p, a) in r.AugmentsPicked) if (t == MatchController.HumanTeam) sb.Append($"내 증강 획득: {Names.Augment(a)}\n");
                    return sb.ToString();
                }
                case MatchPhase.Combat:
                {
                    var sb = new StringBuilder();
                    sb.Append("각 라인 아래에 전투 결과가 있습니다. 회색 타일은 이번 턴 죽은 유닛입니다.\n");
                    int endDeaths = 0; foreach (var d in r.Deaths) if (d.Phase == "end") endDeaths++;
                    if (endDeaths > 0) sb.Append($"턴 종료 효과(용의 둥지, 마법사 시너지 등)로 {endDeaths}명이 추가로 죽었습니다.\n");
                    if (r.RuleRevealed.HasValue) sb.Append($"\n새 라인 규칙: {Names.Lane(r.RuleLane)}에 [{Names.LaneRule(r.RuleRevealed.Value)}] — {Names.LaneRuleDesc(r.RuleRevealed.Value)}\n");
                    foreach (var (t, p, m) in r.MissionsCompleted) sb.Append(t == MatchController.HumanTeam ? $"\n★ 내 비밀 미션 [{Names.Mission(m)}] 달성! 다음 턴 마나 +3, 타워 +2\n" : "\n상대가 비밀 미션을 달성했습니다 (마나 +3, 타워 +2)\n");
                    if (!Match.IsOver) sb.Append($"\n다음은 {e.State.Turn}턴: 마나 {Match.Human.Mana}, 새 카드를 뽑았습니다.");
                    return sb.ToString();
                }
                default:
                {
                    var sel = Match.SelectedUnitDefId;
                    if (sel.HasValue) return CardDetail(Catalog.Unit(sel.Value));
                    if (e.State.Turn == 1) return "첫 턴입니다. 마나 1이니 1코스트 카드 하나를 골라 라인에 놓고 [확정]을 누르세요.\n\n어느 라인에 놓을지가 이 게임의 전부입니다. 상대 유닛이 없는 라인에 놓으면 타워를 때리고, 상대가 있으면 유닛끼리 싸웁니다.";
                    return $"{e.State.Turn}턴 배치 단계. 카드를 고르면 여기에 설명이 나옵니다.\n\n상대 배치 기록: {Match.EnemyHistoryText()}\n(탑·미드·봇 순서. 상대가 어디를 좋아하는지 보세요.)";
                }
            }
        }

        string CardDetail(UnitDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"{d.Name}  —  코스트 {d.Cost}, 공격 {d.Atk}, 체력 {d.Hp}\n");
            sb.Append($"계열 {Names.Tribe(d.Tribe)} · 직업 {Names.Job(d.Job)}\n");
            if (d.Ability != Ability.None) sb.Append($"효과: {Names.Ability(d.Ability)}\n");
            sb.Append('\n').Append(TribeSynergyDesc(d.Tribe)).Append('\n').Append(JobSynergyDesc(d.Job));
            sb.Append("\n\n라인의 [여기에 배치]를 눌러 놓으세요.");
            return sb.ToString();
        }

        static string TribeSynergyDesc(Tribe t) => t switch
        {
            Tribe.Forest => "숲 3명: 타워 전체 +3 (1회) / 5명: 매 턴 타워 +1",
            Tribe.Fire => "불 3명: 아군 공격력 +1 / 5명: 상대 유닛이 죽을 때마다 그 라인 상대 타워 -1",
            _ => "기계 3명: 기계 체력 +1 / 5명: 기계 코스트 -1",
        };

        static string JobSynergyDesc(Job j) => j switch
        {
            Job.Warrior => "전사 2명 같은 라인: 그 라인 전사 체력 +2",
            Job.Archer => "궁수 2명 같은 라인: 그 라인 궁수 공격력 +1",
            _ => "마법사 2명 같은 라인: 턴 종료 시 그 라인 상대 전원 1 피해",
        };

        string SynergyText()
        {
            var e = Match.Engine; int t = MatchController.HumanTeam;
            var sb = new StringBuilder("시너지 현황 (세 라인 합산)\n");
            foreach (Tribe tr in System.Enum.GetValues(typeof(Tribe)))
            {
                int n = e.TribeCount(t, tr); int t3 = e.TribeThreshold(t, 3), t5 = e.TribeThreshold(t, 5);
                string mark = n >= t5 ? " ★★" : n >= t3 ? " ★" : "";
                sb.Append($"{Names.Tribe(tr)} {n}/{t3}{mark}   ");
            }
            sb.Append("\n★ = 3명 보너스, ★★ = 5명 보너스\n\n직업 (같은 라인 2명)\n");
            foreach (Job j in System.Enum.GetValues(typeof(Job)))
            {
                var lanes = new List<string>();
                for (int l = 0; l < 3; l++) if (e.JobCountInLane(t, (Lane)l, j) >= 2) lanes.Add(Names.Lane((Lane)l));
                sb.Append($"{Names.Job(j)}: {(lanes.Count == 0 ? "-" : string.Join(",", lanes) + " ★")}   ");
            }
            return sb.ToString();
        }

        // ─────────────────────────── 타일 ───────────────────────────

        void Tile(Transform row, int index, UnitInstance u, bool isNew)
        {
            var e = Match.Engine;
            float x = index * (TileW + TileGap);
            if (isNew) UiKit.PanelBox(row, "New", x - 3, -3, TileW + 6, TileH + 6, Color.white);
            var img = UiKit.PanelBox(row, "Unit", x, 0, TileW, TileH, TribeColor(u.Def.Tribe));
            string extra = isNew ? " NEW" : u.Hidden ? " (숨김)" : u.Protected ? " (보호)" : u.NoAttackThisTurn ? " (늪)" : "";
            UiKit.Label(img.transform, "T", 4, 2, TileW - 8, TileH - 4,
                $"{u.Def.Name}{extra}\n공 {e.EffectiveAtk(u)} / 체 {e.Hp(u)}\n{Names.Tribe(u.Def.Tribe)}·{Names.Job(u.Def.Job)}", 12, TextAnchor.UpperLeft, Color.black);
        }

        void HiddenTile(Transform row, int index)
        {
            var img = UiKit.PanelBox(row, "Hidden", index * (TileW + TileGap), 0, TileW, TileH, new Color(0.4f, 0.4f, 0.45f));
            UiKit.Label(img.transform, "T", 4, 2, TileW - 8, TileH - 4, "?\n안개에 숨음", 12, TextAnchor.MiddleCenter, Color.black);
        }

        void GhostTile(Transform row, int index, string name)
        {
            var img = UiKit.PanelBox(row, "Dead", index * (TileW + TileGap), 0, TileW, TileH, new Color(0.3f, 0.3f, 0.32f));
            UiKit.Label(img.transform, "T", 4, 2, TileW - 8, TileH - 4, $"{name}\n✖ 사망", 12, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.85f));
        }

        void PendingTile(Transform row, int index, UnitDef def)
        {
            var c = TribeColor(def.Tribe);
            var img = UiKit.PanelBox(row, "Pending", index * (TileW + TileGap), 0, TileW, TileH, new Color(c.r, c.g, c.b, 0.45f));
            UiKit.Label(img.transform, "T", 4, 2, TileW - 8, TileH - 4, $"{def.Name}\n(배치 예정)\n공 {def.Atk} / 체 {def.Hp}", 12, TextAnchor.UpperLeft, Color.black);
        }

        void BuildHand()
        {
            UiKit.Clear(_hand);
            var hand = Match.AvailableHand();
            const float cw = 128, ch = 112, gap = 8;
            bool plan = Match.Phase == MatchPhase.Plan;
            for (int i = 0; i < hand.Count; i++)
            {
                var def = Catalog.Unit(hand[i]);
                int idx = i;
                bool selected = plan && Match.SelectedHandIndex == i;
                var c = TribeColor(def.Tribe);
                if (!plan) c = new Color(c.r, c.g, c.b, 0.6f);
                var b = UiKit.ButtonBox(_hand, "Card", i * (cw + gap), selected ? 0 : 6, cw, ch, "", () => Match.SelectCard(idx), c, 12);
                b.interactable = plan;
                b.GetComponentInChildren<Text>().text = "";
                UiKit.Label(b.transform, "Cost", 6, 4, 40, 22, $"{def.Cost}", 20, TextAnchor.MiddleLeft, Color.black);
                UiKit.Label(b.transform, "Name", 6, 28, cw - 12, 20, def.Name, 14, TextAnchor.MiddleLeft, Color.black);
                UiKit.Label(b.transform, "Stat", 6, 48, cw - 12, 18, $"공 {def.Atk} / 체 {def.Hp}   {Names.Tribe(def.Tribe)}·{Names.Job(def.Job)}", 11, TextAnchor.MiddleLeft, Color.black);
                UiKit.Label(b.transform, "Abil", 6, 66, cw - 12, 42, Names.Ability(def.Ability), 10, TextAnchor.UpperLeft, Color.black);
                if (selected) UiKit.PanelBox(b.transform, "Sel", 0, ch - 4, cw, 4, Color.white);
            }
            if (hand.Count == 0) UiKit.Label(_hand, "Empty", 0, 40, 400, 30, "손패 없음", 14);
        }

        void BuildAugmentPanel()
        {
            UiKit.Clear(_augmentPanel);
            bool show = Match.NeedsAugmentChoice && !Match.IsOver;
            _augmentPanel.gameObject.SetActive(show);
            if (!show) return;
            UiKit.Label(_augmentPanel, "Title", 0, 16, 800, 40, "증강을 하나 고르세요. 이 판 내내 유지되는 영구 버프입니다 (상대는 내 선택을 모릅니다)", 16, TextAnchor.MiddleCenter, UiKit.Accent);
            var offers = Match.Human.Offers;
            for (int i = 0; i < offers.Count; i++)
            {
                int idx = i;
                var a = offers[i];
                var b = UiKit.ButtonBox(_augmentPanel, "Offer", 30 + i * 250, 80, 240, 180, "", () => Match.ChooseAugment(idx), UiKit.PanelLight);
                b.GetComponentInChildren<Text>().text = "";
                UiKit.Label(b.transform, "N", 10, 12, 220, 30, Names.Augment(a), 20, TextAnchor.MiddleCenter, UiKit.Accent);
                UiKit.Label(b.transform, "D", 10, 56, 220, 110, Names.AugmentDesc(a), 14, TextAnchor.UpperCenter);
            }
        }

        void BuildOverlay()
        {
            UiKit.Clear(_overlay);
            bool show = Match.IsOver && Match.Phase == MatchPhase.Plan;
            _overlay.gameObject.SetActive(show);
            if (!show) return;
            var s = Match.Engine.State;
            string who = s.Winner < 0 ? "무승부" : s.Winner == MatchController.HumanTeam ? "승리!" : "패배";
            UiKit.Label(_overlay, "Title", 0, 30, 600, 60, who, 40, TextAnchor.MiddleCenter, UiKit.Accent);
            UiKit.Label(_overlay, "Reason", 0, 100, 600, 40, $"사유: {s.EndReason}   ({s.Turn}턴)", 18, TextAnchor.MiddleCenter);
            UiKit.Label(_overlay, "Style", 0, 135, 600, 30, $"이번 상대의 성향은 [{StyleName(Match.EnemyStyle)}] 이었습니다", 14, TextAnchor.MiddleCenter);
            UiKit.ButtonBox(_overlay, "Again", 200, 180, 200, 50, "다음 판", () => Match.Restart(Match.Seed + 1), new Color(0.22f, 0.55f, 0.35f), 18);
        }

        static string StyleName(AiStyle s) => s switch
        {
            AiStyle.Aggressor => "약한 타워 집중 공격",
            AiStyle.Mirror => "내 유닛이 많은 라인에 맞서기",
            _ => "라인 돌아가며 밀기",
        };

        static Color TribeColor(Tribe t) => t switch
        {
            Tribe.Forest => new Color(0.45f, 0.78f, 0.45f),
            Tribe.Fire => new Color(0.95f, 0.55f, 0.35f),
            _ => new Color(0.55f, 0.68f, 0.90f),
        };
    }
}
