using System;
using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    public sealed class MatchConfig
    {
        public int PlayersPerTeam = 1;
        public int TicksPerSecond = 20;
        public int MatchSeconds = 600;
        public int StartGold = 40;
        public int BaseIncomePerPlayer = 8;
        public int IncomeIntervalSeconds = 20;
        public int DrawCost = 5;
        public int HandMax = 6;
        public int KillGold = 1;
        public int TowerDamagePercent = 150;   // 밸런스 전역 배율 (봇 대전 스윕으로 결정, 문서 3절)
        public int SendCostPercent = 125;      // 보내기 비용 배율
        public int SendIncomePercent = 50;     // 보낼 때 오르는 인컴 배율 (정수 나눗셈, 최소 1)
        public int BaseHpOverride = 0;          // 0 이면 인원수 기본값

        public int LaneWidth => PlayersPerTeam switch { 1 => 4, 2 => 6, _ => 8 };
        public int BaseHp => BaseHpOverride > 0 ? BaseHpOverride : PlayersPerTeam switch { 1 => 60, 2 => 90, _ => 120 };
        public int WaveScalePercent => PlayersPerTeam switch { 1 => 100, 2 => 160, _ => 220 };

        public LaneConfig MakeLaneConfig() => new LaneConfig
        {
            Width = LaneWidth, BaseHp = BaseHp, WaveScalePercent = WaveScalePercent,
            TicksPerSecond = TicksPerSecond, MatchSeconds = MatchSeconds, TowerDamagePercent = TowerDamagePercent,
        };
    }

    public sealed class PlayerEcon
    {
        public int Team, Index;
        public int Gold;
        public List<int> Hand = new List<int>();   // 공격 유닛 id
        public Rng DrawRng;
        public int Drawn, Sent, IncomeReceived;
        public int LastSendTick = -1000;
    }

    public sealed class TeamEcon
    {
        public int Index;
        public int Income;
        public int KillGoldCursor;
        public int TotalKills;
    }

    public enum CommandType { Build, Upgrade, Sell, Draw, Send, Transfer }

    /// <summary>플레이어 명령. 락스텝에서 틱 번호와 함께 교환되는 유일한 입력.</summary>
    public struct MatchCommand
    {
        public int Team, Player;
        public CommandType Type;
        public int A, B, C;     // Build: 타워 id, 열, 줄 / Upgrade·Sell: 타워 id / Send: 손패 인덱스 / Transfer: 받는 플레이어, 금액

        public static MatchCommand Build(int team, int player, int towerId, int col, int row) => new MatchCommand { Team = team, Player = player, Type = CommandType.Build, A = towerId, B = col, C = row };
        public static MatchCommand Upgrade(int team, int player, int towerId) => new MatchCommand { Team = team, Player = player, Type = CommandType.Upgrade, A = towerId };
        public static MatchCommand Sell(int team, int player, int towerId) => new MatchCommand { Team = team, Player = player, Type = CommandType.Sell, A = towerId };
        public static MatchCommand Draw(int team, int player) => new MatchCommand { Team = team, Player = player, Type = CommandType.Draw };
        public static MatchCommand Send(int team, int player, int handIndex) => new MatchCommand { Team = team, Player = player, Type = CommandType.Send, A = handIndex };
        public static MatchCommand Transfer(int team, int player, int toPlayer, int amount) => new MatchCommand { Team = team, Player = player, Type = CommandType.Transfer, A = toPlayer, B = amount };
    }

    public enum MatchEventType { Income, Drew, Sent, Built, Upgraded, Sold, KillGold, Rejected, MatchEnd }

    public struct MatchEvent
    {
        public MatchEventType Type;
        public int Team, Player, A, B;
        public int Tick;
    }

    /// <summary>
    /// 한 판 전체: 라인 두 개(팀별), 골드·인컴·손패, 시간표, 승패. 결정론.
    /// Lanes[t] 는 팀 t 의 라인 = 팀 t 의 타워가 서 있고, 팀 (1-t) 가 보낸 유닛과 기본 웨이브가 팀 t 기지로 걸어오는 곳.
    /// </summary>
    public sealed class MatchSim
    {
        public MatchConfig Cfg { get; }
        public LaneSim[] Lanes { get; } = new LaneSim[2];
        public TeamEcon[] Teams { get; } = new TeamEcon[2];
        public PlayerEcon[][] Players { get; } = new PlayerEcon[2][];
        public int Tick { get; private set; }
        public bool IsOver { get; private set; }
        public int Winner { get; private set; } = -1;
        public string EndReason { get; private set; } = "";
        public readonly List<MatchEvent> Events = new List<MatchEvent>();
        readonly int[] _lastKills = new int[2];

        public MatchSim(MatchConfig cfg, ulong seed)
        {
            Cfg = cfg;
            for (int t = 0; t < 2; t++)
            {
                Lanes[t] = new LaneSim(cfg.MakeLaneConfig(), seed * 7 + (ulong)t + 1);
                Teams[t] = new TeamEcon { Index = t, Income = cfg.BaseIncomePerPlayer * cfg.PlayersPerTeam };
                Players[t] = new PlayerEcon[cfg.PlayersPerTeam];
                for (int p = 0; p < cfg.PlayersPerTeam; p++)
                    Players[t][p] = new PlayerEcon { Team = t, Index = p, Gold = cfg.StartGold, DrawRng = new Rng(seed * 131 + (ulong)(t * 10 + p) + 17) };
            }
        }

        public int Seconds => Tick / Cfg.TicksPerSecond;
        public PlayerEcon Player(int team, int player) => Players[team][player];
        public LaneSim OwnLane(int team) => Lanes[team];
        public LaneSim EnemyLane(int team) => Lanes[1 - team];

        // ─────────────────────────── 진행 ───────────────────────────

        public void Step(IList<MatchCommand> commands = null)
        {
            if (IsOver) return;
            Events.Clear();
            Tick++;

            if (commands != null) foreach (var c in commands) Apply(c);

            for (int t = 0; t < 2; t++) Lanes[t].Step();

            for (int t = 0; t < 2; t++)
            {
                int kills = Lanes[t].Kills - _lastKills[t];
                _lastKills[t] = Lanes[t].Kills;
                for (int k = 0; k < kills; k++)
                {
                    var team = Teams[t];
                    var p = Players[t][team.KillGoldCursor % Cfg.PlayersPerTeam];
                    team.KillGoldCursor++;
                    team.TotalKills++;
                    p.Gold += Cfg.KillGold;
                    Emit(MatchEventType.KillGold, t, p.Index, Cfg.KillGold, 0);
                }
            }

            if (Tick % (Cfg.IncomeIntervalSeconds * Cfg.TicksPerSecond) == 0) PayIncome();

            for (int t = 0; t < 2; t++)
                if (Lanes[t].BaseHp <= 0) { End(1 - t, "기지 파괴"); return; }
            if (Tick >= Cfg.MatchSeconds * Cfg.TicksPerSecond) FinalScore();
        }

        void PayIncome()
        {
            for (int t = 0; t < 2; t++)
            {
                int n = Cfg.PlayersPerTeam;
                int share = Teams[t].Income / n, rem = Teams[t].Income % n;
                for (int p = 0; p < n; p++)
                {
                    int amount = share + (p < rem ? 1 : 0);
                    Players[t][p].Gold += amount;
                    Players[t][p].IncomeReceived += amount;
                    Emit(MatchEventType.Income, t, p, amount, Teams[t].Income);
                }
            }
        }

        void FinalScore()
        {
            int a = Lanes[0].BaseHp, b = Lanes[1].BaseHp;
            if (a != b) { End(a > b ? 0 : 1, "남은 기지 체력"); return; }
            if (Teams[0].Income != Teams[1].Income) { End(Teams[0].Income > Teams[1].Income ? 0 : 1, "팀 인컴"); return; }
            int ga = 0, gb = 0;
            foreach (var p in Players[0]) ga += p.Gold;
            foreach (var p in Players[1]) gb += p.Gold;
            if (ga != gb) { End(ga > gb ? 0 : 1, "팀 골드"); return; }
            End(-1, "무승부");
        }

        void End(int winner, string reason)
        {
            if (IsOver) return;
            IsOver = true; Winner = winner; EndReason = reason;
            Emit(MatchEventType.MatchEnd, winner, -1, 0, 0);
        }

        // ─────────────────────────── 명령 ───────────────────────────

        void Apply(MatchCommand c)
        {
            if (c.Team < 0 || c.Team > 1 || c.Player < 0 || c.Player >= Cfg.PlayersPerTeam) return;
            var p = Players[c.Team][c.Player];
            var lane = Lanes[c.Team];
            switch (c.Type)
            {
                case CommandType.Build:
                {
                    var def = WaveCatalog.Tower(c.A);
                    if (p.Gold < def.Cost || lane.TowerAt(c.B, c.C) != null) { Reject(c); return; }
                    var t = lane.Build(def, c.B, c.C, c.Player);
                    if (t == null) { Reject(c); return; }
                    p.Gold -= def.Cost;
                    Emit(MatchEventType.Built, c.Team, c.Player, t.Id, def.Id);
                    break;
                }
                case CommandType.Upgrade:
                {
                    var t = lane.TowerAt(c.A);
                    if (t == null || t.Upgraded || p.Gold < t.Def.Cost) { Reject(c); return; }
                    p.Gold -= t.Def.Cost;
                    lane.Upgrade(t.Id);
                    Emit(MatchEventType.Upgraded, c.Team, c.Player, t.Id, t.Def.Cost);
                    break;
                }
                case CommandType.Sell:
                {
                    var t = lane.TowerAt(c.A);
                    if (t == null) { Reject(c); return; }
                    int refund = (t.Def.Cost * (t.Upgraded ? 2 : 1)) * 80 / 100;
                    lane.Sell(t.Id);
                    p.Gold += refund;
                    Emit(MatchEventType.Sold, c.Team, c.Player, t.Id, refund);
                    break;
                }
                case CommandType.Draw:
                {
                    if (p.Gold < Cfg.DrawCost || p.Hand.Count >= Cfg.HandMax) { Reject(c); return; }
                    p.Gold -= Cfg.DrawCost;
                    var def = RollAttacker(p.DrawRng);
                    p.Hand.Add(def.Id);
                    p.Drawn++;
                    Emit(MatchEventType.Drew, c.Team, c.Player, def.Id, p.Hand.Count - 1);
                    break;
                }
                case CommandType.Send:
                {
                    if (c.A < 0 || c.A >= p.Hand.Count) { Reject(c); return; }
                    var def = WaveCatalog.Attacker(p.Hand[c.A]);
                    int cost = SendCostOf(def);
                    if (p.Gold < cost) { Reject(c); return; }
                    p.Gold -= cost;
                    p.Hand.RemoveAt(c.A);
                    Teams[c.Team].Income += Math.Max(1, def.Income * Cfg.SendIncomePercent / 100);
                    p.Sent++;
                    p.LastSendTick = Tick;
                    var creep = EnemyLane(c.Team).Send(def, true, c.Team * 100 + c.Player + 1);
                    Emit(MatchEventType.Sent, c.Team, c.Player, def.Id, creep.Id);
                    break;
                }
                case CommandType.Transfer:
                {
                    if (c.A < 0 || c.A >= Cfg.PlayersPerTeam || c.A == c.Player || c.B <= 0 || p.Gold < c.B) { Reject(c); return; }
                    p.Gold -= c.B;
                    Players[c.Team][c.A].Gold += c.B;
                    break;
                }
            }
        }

        void Reject(MatchCommand c) => Emit(MatchEventType.Rejected, c.Team, c.Player, (int)c.Type, c.A);

        public int SendCostOf(AttackerDef def) => Math.Max(1, def.SendCost * Cfg.SendCostPercent / 100);

        /// <summary>뽑기: 일반 60 / 희귀 30 / 영웅 10, 등급 안에서 균등.</summary>
        public static AttackerDef RollAttacker(Rng rng)
        {
            int roll = rng.Next(100);
            var rarity = roll < 60 ? Rarity.Common : roll < 90 ? Rarity.Rare : Rarity.Hero;
            var pool = new List<AttackerDef>();
            foreach (var a in WaveCatalog.Attackers) if (a.Rarity == rarity) pool.Add(a);
            return pool[rng.Next(pool.Count)];
        }

        void Emit(MatchEventType type, int team, int player, int a, int b) => Events.Add(new MatchEvent { Type = type, Team = team, Player = player, A = a, B = b, Tick = Tick });

        public string Hash()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Tick).Append('|').Append(Winner).Append('|');
            for (int t = 0; t < 2; t++)
            {
                sb.Append(Lanes[t].Hash()).Append('|').Append(Teams[t].Income).Append('|');
                foreach (var p in Players[t]) { sb.Append('g').Append(p.Gold).Append('h'); foreach (var h in p.Hand) sb.Append(h).Append('.'); }
                sb.Append('|');
            }
            return sb.ToString();
        }
    }
}
