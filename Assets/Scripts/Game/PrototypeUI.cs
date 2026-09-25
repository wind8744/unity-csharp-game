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
    /// 1v1, 사람(팀0) 대 탐욕 AI(팀1).
    /// </summary>
    public sealed class PrototypeUI : MonoBehaviour
    {
        public MatchController Match { get; private set; }
        public ulong StartSeed = 1;

        const float W = 1280, H = 720;
        static readonly float[] LaneX = { 20, 440, 860 };
        const float LaneW = 400, TileW = 92, TileH = 62, TileGap = 6;

        Transform _root;
        Text _topInfo, _rightInfo, _log, _manaText;
        readonly Transform[] _enemyRows = new Transform[3], _myRows = new Transform[3];
        readonly Text[] _enemyTower = new Text[3], _myTower = new Text[3], _laneRule = new Text[3];
        readonly Button[] _placeButtons = new Button[3];
        Transform _hand, _augmentPanel, _overlay;
        Button _confirm, _undo;

        void Awake()
        {
            Match = new MatchController(StartSeed, 1);
            Match.Changed += Refresh;
            BuildStatic();
            Refresh();
        }

        void Start()
        {
            // 검증용: -screenshot <경로> 로 실행하면 첫 화면을 찍고 종료한다.
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-autoplay" && int.TryParse(args[i + 1], out int turns)) AutoPlay(turns);
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshot") StartCoroutine(ScreenshotAndQuit(args[i + 1]));
        }

        /// <summary>검증용: 사람 자리를 단순 규칙으로 N턴 자동 진행 (비싼 카드부터 빈 라인에).</summary>
        void AutoPlay(int turns)
        {
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
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.matchWidthOrHeight = 0.5f;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            _root = UiKit.PanelBox(canvasGo.transform, "Root", 0, 0, W, H, UiKit.Bg).transform;
            var rootRt = (RectTransform)_root;
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one; rootRt.sizeDelta = Vector2.zero; rootRt.anchoredPosition = Vector2.zero;

            _topInfo = UiKit.Label(_root, "TopInfo", 20, 12, 700, 48, "", 18);
            _rightInfo = UiKit.Label(_root, "RightInfo", 740, 12, 520, 48, "", 14, TextAnchor.UpperRight);

            for (int l = 0; l < 3; l++)
            {
                float x = LaneX[l];
                var col = UiKit.PanelBox(_root, "Lane" + l, x, 66, LaneW, 300, UiKit.Panel).transform;
                UiKit.Label(col, "Name", 0, 4, LaneW, 22, Names.Lane((Lane)l), 18, TextAnchor.MiddleCenter, UiKit.Accent);
                _enemyTower[l] = UiKit.Label(col, "EnemyTower", 8, 28, LaneW - 16, 20, "", 14, TextAnchor.MiddleLeft);
                _enemyRows[l] = UiKit.Rect(col, "EnemyRow", 8, 50, LaneW - 16, TileH);
                _laneRule[l] = UiKit.Label(col, "Rule", 8, 116, LaneW - 16, 20, "", 13, TextAnchor.MiddleCenter, UiKit.Accent);
                _myRows[l] = UiKit.Rect(col, "MyRow", 8, 140, LaneW - 16, TileH);
                _myTower[l] = UiKit.Label(col, "MyTower", 8, 206, LaneW - 16, 20, "", 14, TextAnchor.MiddleLeft);
                int lane = l;
                _placeButtons[l] = UiKit.ButtonBox(col, "Place", 8, 232, LaneW - 16, 40, "여기에 배치", () => Match.PlaceSelected((Lane)lane));
            }

            UiKit.PanelBox(_root, "LogBg", 20, 376, 880, 190, UiKit.Panel);
            _log = UiKit.Label(_root, "Log", 30, 382, 860, 180, "", 13);

            var side = UiKit.PanelBox(_root, "Side", 920, 376, 340, 190, UiKit.Panel).transform;
            _manaText = UiKit.Label(side, "Mana", 10, 8, 320, 40, "", 22, TextAnchor.MiddleCenter, UiKit.Accent);
            _confirm = UiKit.ButtonBox(side, "Confirm", 10, 56, 320, 56, "확정 (공개 & 전투)", () => Match.Confirm(), new Color(0.22f, 0.55f, 0.35f), 20);
            _undo = UiKit.ButtonBox(side, "Undo", 10, 120, 155, 40, "되돌리기", () => Match.UndoLast());
            UiKit.ButtonBox(side, "Restart", 175, 120, 155, 40, "새 판", () => Match.Restart(Match.Seed + 1));

            UiKit.PanelBox(_root, "HandBg", 20, 576, 1240, 130, UiKit.Panel);
            _hand = UiKit.Rect(_root, "Hand", 30, 582, 1220, 118);

            _augmentPanel = UiKit.PanelBox(_root, "AugmentPanel", 240, 150, 800, 300, new Color(0.1f, 0.1f, 0.12f, 0.97f)).transform;
            _overlay = UiKit.PanelBox(_root, "Overlay", 340, 200, 600, 260, new Color(0.08f, 0.08f, 0.1f, 0.97f)).transform;
        }

        // ─────────────────────────── 상태 → 화면 ───────────────────────────

        public void Refresh()
        {
            var e = Match.Engine; var s = e.State; var me = Match.Human;
            var myTeam = e.Team(MatchController.HumanTeam); var enemy = e.Team(1 - MatchController.HumanTeam);

            _topInfo.text = $"{s.Turn}턴 / {e.Config.Turns}턴     내 타워 {myTeam.TowerHpSum()}  vs  상대 타워 {enemy.TowerHpSum()}     사망 {s.TotalDeaths}";
            var sb = new StringBuilder();
            sb.Append("비밀 미션: ").Append(Names.Mission(me.Mission)).Append(me.MissionDone ? " (달성!)" : " — " + Names.MissionDesc(me.Mission, e.Config));
            if (me.Augments.Count > 0)
            {
                sb.Append("\n증강: ");
                foreach (var a in me.Augments) sb.Append(Names.Augment(a)).Append(' ');
            }
            _rightInfo.text = sb.ToString();

            for (int l = 0; l < 3; l++)
            {
                var lane = (Lane)l;
                _enemyTower[l].text = enemy.TowerDestroyed[l] ? "상대 타워: 파괴됨" : $"상대 타워: {enemy.TowerHp[l]}";
                _myTower[l].text = myTeam.TowerDestroyed[l] ? "내 타워: 파괴됨" : $"내 타워: {myTeam.TowerHp[l]}";
                var rule = s.RuleAt(lane);
                _laneRule[l].text = rule.HasValue ? $"[{Names.LaneRule(rule.Value)}] {Names.LaneRuleDesc(rule.Value)}" : "";

                UiKit.Clear(_enemyRows[l]);
                int i = 0;
                foreach (var u in e.VisibleUnits(1 - MatchController.HumanTeam, lane)) Tile(_enemyRows[l], i++, u, false);
                int hiddenCount = e.LaneUnits(1 - MatchController.HumanTeam, lane).Count - i;
                for (int h = 0; h < hiddenCount; h++) HiddenTile(_enemyRows[l], i++);

                UiKit.Clear(_myRows[l]);
                i = 0;
                foreach (var u in e.LaneUnits(MatchController.HumanTeam, lane)) Tile(_myRows[l], i++, u, false);
                foreach (var p in Match.Pending) if (p.Lane == lane) PendingTile(_myRows[l], i++, Catalog.Unit(p.UnitDefId));

                var hand = Match.AvailableHand();
                bool canPlace = Match.SelectedHandIndex >= 0 && Match.SelectedHandIndex < hand.Count
                                && Match.CanAddPending(hand[Match.SelectedHandIndex], lane);
                _placeButtons[l].interactable = canPlace && !Match.IsOver;
                _placeButtons[l].GetComponentInChildren<Text>().text = canPlace
                    ? $"여기에 배치 ({e.CostOf(MatchController.HumanTeam, hand[Match.SelectedHandIndex], lane)} 마나)"
                    : $"슬롯 {e.LaneUnits(MatchController.HumanTeam, lane).Count + Match.PendingCountInLane(lane)}/{e.LaneCapacity(lane)}";
            }

            _manaText.text = $"마나 {Match.ManaLeft} / {me.Mana}";
            _confirm.interactable = !Match.IsOver && !Match.NeedsAugmentChoice;
            _undo.interactable = Match.Pending.Count > 0;

            _log.text = Match.LastTurnLog.Count == 0
                ? "카드를 고르고 라인의 [여기에 배치]를 누르세요. 상대는 내 배치를 못 봅니다. 확정하면 동시에 공개되고 전투가 벌어집니다."
                : string.Join("\n", Tail(Match.LastTurnLog, 11)).Replace("T0P0", "나").Replace("T1P0", "상대").Replace("T0 ", "내 ").Replace("T1 ", "상대 ");

            BuildHand();
            BuildAugmentPanel();
            BuildOverlay();
        }

        void Tile(Transform row, int index, UnitInstance u, bool dim)
        {
            var e = Match.Engine;
            var img = UiKit.PanelBox(row, "Unit", index * (TileW + TileGap), 0, TileW, TileH, TribeColor(u.Def.Tribe));
            if (dim) img.color = new Color(img.color.r, img.color.g, img.color.b, 0.5f);
            string extra = u.Hidden ? " (숨김)" : u.Protected ? " (보호)" : u.NoAttackThisTurn ? " (늪)" : "";
            UiKit.Label(img.transform, "T", 4, 2, TileW - 8, TileH - 4,
                $"{u.Def.Name}{extra}\n공 {e.EffectiveAtk(u)} / 체 {e.Hp(u)}\n{Names.Tribe(u.Def.Tribe)}·{Names.Job(u.Def.Job)}", 12, TextAnchor.UpperLeft, Color.black);
        }

        void HiddenTile(Transform row, int index)
        {
            var img = UiKit.PanelBox(row, "Hidden", index * (TileW + TileGap), 0, TileW, TileH, new Color(0.4f, 0.4f, 0.45f));
            UiKit.Label(img.transform, "T", 4, 2, TileW - 8, TileH - 4, "?\n안개에 숨음", 12, TextAnchor.MiddleCenter, Color.black);
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
            for (int i = 0; i < hand.Count; i++)
            {
                var def = Catalog.Unit(hand[i]);
                int idx = i;
                bool selected = Match.SelectedHandIndex == i;
                var c = TribeColor(def.Tribe);
                var b = UiKit.ButtonBox(_hand, "Card", i * (cw + gap), selected ? 0 : 6, cw, ch, "", () => Match.SelectCard(idx), c, 12);
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
            UiKit.Label(_augmentPanel, "Title", 0, 16, 800, 40, "증강을 하나 고르세요 (상대는 내 선택을 모릅니다)", 20, TextAnchor.MiddleCenter, UiKit.Accent);
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
            bool show = Match.IsOver;
            _overlay.gameObject.SetActive(show);
            if (!show) return;
            var s = Match.Engine.State;
            string who = s.Winner < 0 ? "무승부" : s.Winner == MatchController.HumanTeam ? "승리!" : "패배";
            UiKit.Label(_overlay, "Title", 0, 30, 600, 60, who, 40, TextAnchor.MiddleCenter, UiKit.Accent);
            UiKit.Label(_overlay, "Reason", 0, 100, 600, 40, $"사유: {s.EndReason}   ({s.Turn}턴)", 18, TextAnchor.MiddleCenter);
            UiKit.ButtonBox(_overlay, "Again", 200, 170, 200, 50, "다음 판", () => Match.Restart(Match.Seed + 1), new Color(0.22f, 0.55f, 0.35f), 18);
        }

        static Color TribeColor(Tribe t) => t switch
        {
            Tribe.Forest => new Color(0.45f, 0.78f, 0.45f),
            Tribe.Fire => new Color(0.95f, 0.55f, 0.35f),
            _ => new Color(0.55f, 0.68f, 0.90f),
        };

        static IEnumerable<string> Tail(List<string> list, int n)
        {
            for (int i = System.Math.Max(0, list.Count - n); i < list.Count; i++) yield return list[i];
        }
    }
}
