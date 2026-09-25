using System;
using System.Collections.Generic;
using LaneBattle.Core;

namespace LaneBattle.Game
{
    /// <summary>
    /// 사람 한 명(팀0 P0)이 두는 한 판. 나머지 자리는 AI 가 채운다. Unity 의존 없음.
    /// UI 는 이 클래스의 상태만 보고 그리고, 여기 메서드만 호출한다.
    /// </summary>
    public sealed class MatchController
    {
        public GameEngine Engine { get; private set; }
        public GameConfig Config { get; }
        public ulong Seed { get; private set; }
        public const int HumanTeam = 0, HumanPlayer = 0;

        public List<Placement> Pending { get; } = new List<Placement>();
        public int SelectedHandIndex { get; private set; } = -1;
        public int AugmentChoice { get; private set; } = -1;
        public List<string> LastTurnLog { get; } = new List<string>();
        public event Action Changed;

        readonly IAgent _ai = new GreedyAgent();
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
            _aiRng = new Rng(seed * 31 + 7);
            Pending.Clear();
            SelectedHandIndex = -1;
            AugmentChoice = -1;
            LastTurnLog.Clear();
            Changed?.Invoke();
        }

        public PlayerState Human => Engine.Player(HumanTeam, HumanPlayer);
        public bool NeedsAugmentChoice => Human.Offers.Count > 0 && AugmentChoice < 0;
        public bool IsOver => Engine.State.IsOver;

        /// <summary>아직 배치 예정에 쓰이지 않은 손패 (카드 id 목록, 손패 순서 유지).</summary>
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
            if (IsOver || !AvailableHand().Contains(unitDefId)) return false;
            if (Engine.LaneUnits(HumanTeam, lane).Count + PendingCountInLane(lane) >= Engine.LaneCapacity(lane)) return false;
            return Engine.CostOf(HumanTeam, unitDefId, lane) <= ManaLeft;
        }

        public void SelectCard(int availableHandIndex)
        {
            SelectedHandIndex = SelectedHandIndex == availableHandIndex ? -1 : availableHandIndex;
            Changed?.Invoke();
        }

        /// <summary>선택한 카드를 라인에 배치 예정으로 올린다. 성공하면 true.</summary>
        public bool PlaceSelected(Lane lane)
        {
            var hand = AvailableHand();
            if (SelectedHandIndex < 0 || SelectedHandIndex >= hand.Count) return false;
            int id = hand[SelectedHandIndex];
            if (!CanAddPending(id, lane)) return false;
            Pending.Add(new Placement(id, lane));
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

        /// <summary>확정: 사람 명령 + AI 명령으로 한 턴을 해결한다.</summary>
        public bool Confirm()
        {
            if (IsOver || NeedsAugmentChoice) return false;
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

            Pending.Clear();
            SelectedHandIndex = -1;
            AugmentChoice = -1;
            Changed?.Invoke();
            return true;
        }
    }
}
