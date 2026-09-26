using System.Collections.Generic;
using LaneBattle.Core.Wave;
using UnityEngine;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>
    /// 튜토리얼: 첫 판을 7단계로 안내한다. 각 단계는 실제 명령이 성공하면(이벤트) 넘어가고 게임은 계속 흐른다
    /// (상대 봇은 처음 75초 동안 유닛을 안 보내고, 첫 웨이브도 75초 뒤). 다음에 할 곳은 노란 테두리로 가리킨다.
    /// </summary>
    public sealed class TutorialGuide
    {
        public sealed class Step { public string Title, Text, Target; public System.Func<TutorialGuide, bool> Done; public float MinSeconds; }
        public readonly List<Step> Steps = new List<Step>();
        public int Index { get; private set; }
        public bool Finished => Index >= Steps.Count;
        public System.Action OnFinished;
        public bool Built, Drew, Sent, Fused, Merged;

        readonly MatchSim _sim; readonly int _team, _player;
        readonly Transform _ui, _panel; readonly Text _title, _text, _counter;
        readonly Dictionary<string, Rect> _targets;
        readonly List<Image> _frame = new List<Image>();
        float _pulse, _stepTime;

        /// <param name="targets">가리킬 화면 영역 (이름 → 픽셀 사각형): shop, draw, hand, xp</param>
        public TutorialGuide(Transform ui, MatchSim sim, int team, int player, Dictionary<string, Rect> targets, System.Action onSkip)
        {
            _ui = ui; _sim = sim; _team = team; _player = player; _targets = targets;
            _panel = UiKit.SpritePanel(ui, "Tutorial", 250, 58, 780, 84, "ui_panel_dark").transform;
            _counter = UiKit.Label(_panel, "Step", 12, 6, 90, 22, "", 12, TextAnchor.MiddleLeft, UiKit.Accent);
            _title = UiKit.Label(_panel, "Title", 70, 6, 600, 22, "", 15, TextAnchor.MiddleLeft, UiKit.Gold);
            _text = UiKit.Label(_panel, "Text", 12, 30, 690, 52, "", 12, TextAnchor.UpperLeft, Color.white);
            UiKit.SpriteButton(_panel, "Skip", 692, 8, 80, 28, "건너뛰기", () => { Skip(); onSkip?.Invoke(); }, "ui_button_grey", 11);
            BuildSteps();
            Refresh();
        }

        void BuildSteps()
        {
            var archer = WaveCatalog.Tower(1);
            TowerDef recipe = null, partner = null;
            foreach (var f in WaveCatalog.FusedTowers)
                if (f.RecipeA == archer.Id || f.RecipeB == archer.Id) { recipe = f; partner = WaveCatalog.Tower(f.RecipeA == archer.Id ? f.RecipeB : f.RecipeA); break; }
            Steps.Add(new Step
            {
                Title = "타워 짓기", Target = "shop", Done = g => g.Built,
                Text = $"아래 상점에서 [{archer.Name}]를 클릭하거나 B → Q 를 누르면 커서에 타워가 뜹니다. 왼쪽 내 진영에서 길 옆 빈 칸을 클릭해 지으세요.\n길 안쪽 구석 칸은 여러 구간을 한꺼번에 봅니다.",
            });
            Steps.Add(new Step
            {
                Title = "유닛 뽑기", Target = "draw", Done = g => g.Drew,
                Text = "D 또는 [뽑기] 버튼: 4골드로 공격 유닛 카드를 한 장 뽑습니다. 카드는 왼쪽 아래 손패에 들어갑니다.",
            });
            Steps.Add(new Step
            {
                Title = "보내기", Target = "hand", Done = g => g.Sent,
                Text = "손패의 카드를 클릭하거나 카드 위에서 W: 그 유닛이 오른쪽 상대 진영으로 걸어갑니다.\n보낼수록 팀 인컴(15초마다 들어오는 수입)이 오릅니다. 살아서 끝까지 가면 상대 기지가 깎입니다.",
            });
            if (recipe != null)
                Steps.Add(new Step
                {
                    Title = "합성", Target = "shop", Done = g => g.Fused,
                    Text = $"합성표의 짝 두 타워는 하나로 합쳐집니다. [{partner.Name}]를 지은 뒤 {archer.Name}나 {partner.Name} 위에 커서를 올리고 R (또는 클릭 → [R] 합성).\n결과: [{recipe.Name}]. 합성할 수 있는 타워엔 R 표시가 뜹니다.",
                });
            Steps.Add(new Step
            {
                Title = "★ 합치기", Target = "shop", Done = g => g.Merged,
                Text = "같은 타워 3개는 하나로 합쳐 ★2 (공격 ×2.2)가 됩니다. 같은 타워를 3개 지은 뒤 하나 위에 커서를 올리고 C.\n합칠 수 있는 타워엔 C 표시가 뜹니다. ★2 셋이면 ★3 (공격 ×5).",
            });
            Steps.Add(new Step
            {
                Title = "레벨업", Target = "xp", Done = g => g._sim.Player(g._team, g._player).Level >= 3,
                Text = "F (4골드 → 경험치 +4): 레벨이 오르면 뽑기에서 좋은 유닛이 잘 나오고 손패도 넓어집니다. 레벨 3을 찍어보세요.",
            });
            Steps.Add(new Step
            {
                Title = "이제 자유롭게!", Target = "", Done = g => true, MinSeconds = 12f,
                Text = "이 판은 5분. 상대 기지를 부수거나 끝났을 때 기지 체력이 더 많으면 승리.\n오는 유닛을 보고 막고, 남는 골드로 보내세요. 합성표는 위쪽 [합성표], 메뉴는 ESC.",
            });
        }

        public void OnEvent(MatchEvent ev)
        {
            if (ev.Team != _team || ev.Player != _player) return;
            switch (ev.Type)
            {
                case MatchEventType.Built: Built = true; break;
                case MatchEventType.Drew: Drew = true; break;
                case MatchEventType.Sent: Sent = true; break;
                case MatchEventType.Fused: Fused = true; break;
                case MatchEventType.Merged: Merged = true; break;
            }
        }

        public void Tick(float dt)
        {
            if (Finished) return;
            _stepTime += dt; _pulse += dt;
            var s = Steps[Index];
            if (_stepTime >= s.MinSeconds && s.Done(this)) { Advance(); return; }
            float k = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(_pulse * 4f));
            foreach (var img in _frame) img.color = new Color(1f, 0.85f, 0.3f, k);
        }

        void Advance()
        {
            Index++; _stepTime = 0;
            Sfx.Play("mission", 0.7f);
            Refresh();
            if (Finished) Finish();
        }

        public void Skip() { if (Finished) return; Index = Steps.Count; Finish(); }

        void Finish()
        {
            _panel.gameObject.SetActive(false);
            ClearFrame();
            OnFinished?.Invoke();
        }

        void ClearFrame() { foreach (var img in _frame) if (img != null) Object.Destroy(img.gameObject); _frame.Clear(); }

        void Refresh()
        {
            ClearFrame();
            if (Finished) return;
            var s = Steps[Index];
            _counter.text = $"{Index + 1} / {Steps.Count}"; _title.text = s.Title; _text.text = s.Text;
            if (!string.IsNullOrEmpty(s.Target) && _targets.TryGetValue(s.Target, out var r))
            {
                const float th = 3f, pad = 6f;
                float x = r.x - pad, y = r.y - pad, w = r.width + pad * 2, h = r.height + pad * 2;
                var col = new Color(1f, 0.85f, 0.3f, 1f);
                _frame.Add(UiKit.PanelBox(_ui, "TutFrameT", x, y, w, th, col));
                _frame.Add(UiKit.PanelBox(_ui, "TutFrameB", x, y + h - th, w, th, col));
                _frame.Add(UiKit.PanelBox(_ui, "TutFrameL", x, y, th, h, col));
                _frame.Add(UiKit.PanelBox(_ui, "TutFrameR", x + w - th, y, th, h, col));
                foreach (var img in _frame) img.raycastTarget = false;
            }
        }

        public void Destroy() { ClearFrame(); if (_panel != null) Object.Destroy(_panel.gameObject); }
    }

    /// <summary>
    /// 상황 팁: 지금 할 수 있는 것을 한 번씩 알려준다 (판마다 팁 종류별 한 번, 6초 간격).
    /// 타이틀 선택지 "도움말 팁" 으로 끌 수 있다 (프로필에 저장).
    /// </summary>
    public sealed class CoachTips
    {
        readonly MatchSim _sim; readonly int _team, _player; readonly System.Action<string> _show;
        readonly HashSet<string> _shown = new HashSet<string>();
        float _goldIdle, _sinceTip = 99f, _scan;
        bool _fusedOnce, _mergedOnce;

        public CoachTips(MatchSim sim, int team, int player, System.Action<string> show) { _sim = sim; _team = team; _player = player; _show = show; }

        public void OnEvent(MatchEvent ev)
        {
            if (ev.Team != _team || ev.Player != _player) return;
            if (ev.Type == MatchEventType.Fused) _fusedOnce = true;
            if (ev.Type == MatchEventType.Merged) _mergedOnce = true;
        }

        public void Tick(float dt)
        {
            _sinceTip += dt; _scan += dt;
            if (_scan < 0.5f) return;
            _scan = 0;
            var p = _sim.Player(_team, _player);
            var lane = _sim.OwnLane(_team);
            if (!_fusedOnce)
                foreach (var t in lane.Towers)
                    if (t.Alive && t.BuildLeft == 0 && _sim.CanUse(t, _player) && _sim.FuseOptions(_team, t).Count > 0) { Tip("fuse", "합성할 수 있는 짝이 있습니다 (R 표시): 타워 위에서 R, 또는 클릭 → [R] 합성"); break; }
            if (!_mergedOnce)
                foreach (var t in lane.Towers)
                    if (t.Alive && t.Star < 3 && _sim.CanUse(t, _player) && _sim.MergeCandidates(_team, t).Count >= 2) { Tip("merge", "같은 타워 3개 (C 표시)! 하나 위에서 C 를 누르면 ★2 — 공격 ×2.2"); break; }
            if (p.Hand.Count >= p.HandMax(_sim.Cfg)) Tip("hand", "손패가 가득 찼습니다. 카드를 클릭하거나 카드 위에서 W 로 보내면 인컴이 오릅니다");
            if (p.Gold >= 60) { _goldIdle += 0.5f; if (_goldIdle >= 15f) Tip("gold", "골드가 쌓였습니다: 타워를 짓거나(B), F 로 레벨업, 카드를 보내세요"); }
            else _goldIdle = 0;
            if (lane.Leaked > 0) Tip("leak", "유닛이 새어 나갔습니다. 길 안쪽 구석(여러 구간을 보는 칸)에 타워를 더 지으세요");
            if (p.Level >= 4 && p.Hand.Count == 0 && p.Gold >= 20) Tip("legend", "레벨 4부터는 뽑기에서 전설 유닛도 나옵니다 — D 로 뽑아 보세요");
        }

        void Tip(string key, string text)
        {
            if (_sinceTip < 6f || _shown.Contains(key)) return;
            _shown.Add(key); _sinceTip = 0;
            _show("팁: " + text);
        }
    }
}
