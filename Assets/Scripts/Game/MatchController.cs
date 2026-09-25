using System;
using System.Collections.Generic;
using LaneBattle.Core;

namespace LaneBattle.Game
{
    public enum MatchPhase { Plan, Reveal, Combat }

    /// <summary>
    /// 사람 한 명(팀0 P0)이 두는 한 판. 나머지 자리는 AI 가 채운다. Unity 의존 없음.
    /// 확정 → 공개 단계 → 전투 단계 → 다음 턴 계획 단계 순으로 UI 가 멈춰 보여줄 수 있게 단계를 관리한다.
    /// </summary>
    public sealed class MatchController
    {
        public GameEngine Engine { get; private set; }
        public GameConfig Config { get; }
        public ulong Seed { get; private set; }
        public const int HumanTeam = 0, HumanPlayer = 0, EnemyTeam = 1;

        public MatchPhase Phase { get; private set; } = MatchPhase.Plan;
        public TurnReport Report => Engine.LastReport;
        public List<Placement> Pending { get; } = new List<Placement>();
        public int SelectedHandIndex { get; private set; } = -1;
        public int AugmentChoice { get; private set; } = -1;
        public List<string> LastTurnLog { get; } = new List<string>();
        /// <summary>턴별 상대 배치 수 [탑, 미드, 봇]. 사람이 상대 성향을 읽는 재료.</summary>
        public List<int[]> EnemyHistory { get; } = new List<int[]>();
        public AiStyle EnemyStyle { get; private set; }
        public event Action Changed;

        IAgent _ai;
        Rng _aiRng;

        public MatchController(ulong seed, int playersPerTeam = 1)
        {
            Config = new GameConfig { PlayersPerTeam = playersPerTeam, EnableLog = true };
            Restart(seed);
        }

        public void Restart(ulong seed)
        {
            Seed = seed;
            Engine = new GameEngine(Config, seed);
            EnemyStyle = (AiStyle)(int)(seed % 3);
            _ai = new StyleAgent(EnemyStyle, (int)(seed % 7));
            _aiRng = new Rng(seed * 31 + 7);
            Phase = MatchPhase.Plan;
            Pending.Clear();
            EnemyHistory.Clear();
            SelectedHandIndex = -1;
            AugmentChoice = -1;
            LastTurnLog.Clear();
            Changed?.Invoke();
        }

        public PlayerState Human => Engine.Player(HumanTeam, HumanPlayer);
        public bool NeedsAugmentChoice => Phase == MatchPhase.Plan && Human.Offers.Count > 0 && AugmentChoice < 0;
        public bool IsOver => Engine.State.IsOver;
        /// <summary>화면에 표시할 턴 번호. 공개/전투 단계에서는 방금 해결한 턴.</summary>
        public int ShownTurn => Phase == MatchPhase.Plan ? Engine.State.Turn : Report.Turn;

        public List<int> AvailableHand()
        {
            var used = new List<int>();
            foreach (var p in Pending) used.Add(p.UnitDefId);
            var result = new List<int>();
            foreach (var id in Human.Hand)
            {
                int i = used.IndexOf(id);
                if (i >= 0) used.RemoveAt(i); else result.Add(id);
            }
            return result;
        }

        public int PendingCost()
        {
            int c = 0;
            foreach (var p in Pending) c += Engine.CostOf(HumanTeam, p.UnitDefId, p.Lane);
            return c;
        }

        public int ManaLeft => Human.Mana - PendingCost();

        public int PendingCountInLane(Lane lane)
        {
            int n = 0;
            foreach (var p in Pending) if (p.Lane == lane) n++;
            return n;
        }

        public bool CanAddPending(int unitDefId, Lane lane)
        {
            if (IsOver || Phase != MatchPhase.Plan || !AvailableHand().Contains(unitDefId)) return false;
            if (Engine.LaneUnits(HumanTeam, lane).Count + PendingCountInLane(lane) >= Engine.LaneCapacity(lane)) return false;
            return Engine.CostOf(HumanTeam, unitDefId, lane) <= ManaLeft;
        }

        public int? SelectedUnitDefId
        {
            get
            {
                var hand = AvailableHand();
                return SelectedHandIndex >= 0 && SelectedHandIndex < hand.Count ? hand[SelectedHandIndex] : (int?)null;
            }
        }

        public void SelectCard(int availableHandIndex)
        {
            if (Phase != MatchPhase.Plan) return;
            SelectedHandIndex = SelectedHandIndex == availableHandIndex ? -1 : availableHandIndex;
            Changed?.Invoke();
        }

        public bool PlaceSelected(Lane lane)
        {
            var id = SelectedUnitDefId;
            if (id == null || !CanAddPending(id.Value, lane)) return false;
            Pending.Add(new Placement(id.Value, lane));
            SelectedHandIndex = -1;
            Changed?.Invoke();
            return true;
        }

        public void UndoLast()
        {
            if (Pending.Count > 0) Pending.RemoveAt(Pending.Count - 1);
            Changed?.Invoke();
        }

        public void ChooseAugment(int index)
        {
            if (index < 0 || index >= Human.Offers.Count) return;
            AugmentChoice = index;
            Changed?.Invoke();
        }

        /// <summary>확정: 사람 명령 + AI 명령으로 한 턴을 해결하고 공개 단계로 간다.</summary>
        public bool Confirm()
        {
            if (IsOver || Phase != MatchPhase.Plan || NeedsAugmentChoice) return false;
            var cmds = new TurnCommands(Config.PlayersPerTeam);
            var mine = new PlayerCommand { AugmentChoice = Math.Max(0, AugmentChoice) };
            mine.Placements.AddRange(Pending);
            cmds.Set(HumanTeam, HumanPlayer, mine);
            for (int t = 0; t < 2; t++)
                for (int p = 0; p < Config.PlayersPerTeam; p++)
                    if (!(t == HumanTeam && p == HumanPlayer))
                        cmds.Set(t, p, _ai.Decide(Engine, t, p, _aiRng));

            int logStart = Engine.Log.Count;
            Engine.ResolveTurn(cmds);
            LastTurnLog.Clear();
            for (int i = logStart; i < Engine.Log.Count; i++) LastTurnLog.Add(Engine.Log[i]);

            var hist = new int[3];
            for (int l = 0; l < 3; l++) hist[l] = Report.PlacedCount(EnemyTeam, (Lane)l);
            EnemyHistory.Add(hist);

            Pending.Clear();
            SelectedHandIndex = -1;
            AugmentChoice = -1;
            Phase = MatchPhase.Reveal;
            Changed?.Invoke();
            return true;
        }

        /// <summary>공개 → 전투 → 다음 턴 계획으로 한 단계 진행.</summary>
        public void Advance()
        {
            if (Phase == MatchPhase.Reveal) Phase = MatchPhase.Combat;
            else if (Phase == MatchPhase.Combat) Phase = MatchPhase.Plan;
            Changed?.Invoke();
        }

        public string EnemyHistoryText(int lastN = 3)
        {
            if (EnemyHistory.Count == 0) return "아직 기록 없음";
            var parts = new List<string>();
            for (int i = Math.Max(0, EnemyHistory.Count - lastN); i < EnemyHistory.Count; i++)
                parts.Add($"{i + 1}턴 {EnemyHistory[i][0]}·{EnemyHistory[i][1]}·{EnemyHistory[i][2]}");
            return string.Join("  ", parts);
        }
    }
}
