using System.Collections.Generic;
using System.Text;

namespace LaneBattle.Core
{
    public sealed class MatchResult
    {
        public int Winner;
        public int Turns;
        public string EndReason;
        public int[] TowersDestroyedBy = new int[2];   // 각 팀이 파괴한 상대 타워 수
        public HashSet<int>[] UnitsPlaced = { new HashSet<int>(), new HashSet<int>() };
        public List<AugmentId>[] Augments = { new List<AugmentId>(), new List<AugmentId>() };
        public LaneRuleId?[] LaneRules = new LaneRuleId?[3];
        public List<(MissionId mission, bool done)>[] Missions = { new List<(MissionId, bool)>(), new List<(MissionId, bool)>() };
        public int TotalDeaths;
        public string StateHash;
    }

    public static class Simulator
    {
        public static MatchResult Play(GameConfig cfg, ulong seed, IAgent[] teamAgents)
        {
            var engine = new GameEngine(cfg, seed);
            var agentRng = new[] { new Rng(seed * 31 + 1), new Rng(seed * 31 + 2) };
            int guard = 0;
            while (!engine.State.IsOver && guard++ < 100)
            {
                var cmds = new TurnCommands(cfg.PlayersPerTeam);
                for (int t = 0; t < 2; t++)
                    for (int p = 0; p < cfg.PlayersPerTeam; p++)
                        cmds.Set(t, p, teamAgents[t].Decide(engine, t, p, agentRng[t]));
                engine.ResolveTurn(cmds);
            }
            return Collect(engine);
        }

        public static MatchResult Collect(GameEngine engine)
        {
            var s = engine.State;
            var r = new MatchResult
            {
                Winner = s.Winner, Turns = s.Turn, EndReason = s.EndReason, TotalDeaths = s.TotalDeaths,
            };
            for (int t = 0; t < 2; t++)
            {
                r.TowersDestroyedBy[t] = s.Teams[1 - t].TowersDestroyedCount();
                foreach (var id in s.Teams[t].UnitsPlaced) r.UnitsPlaced[t].Add(id);
                r.Augments[t].AddRange(s.Teams[t].AugmentsPicked);
                foreach (var p in s.Teams[t].Players) r.Missions[t].Add((p.Mission, p.MissionDone));
            }
            for (int l = 0; l < 3; l++) r.LaneRules[l] = s.LaneRules[l];
            r.StateHash = Hash(engine);
            return r;
        }

        /// <summary>결정론 검증용 상태 요약.</summary>
        public static string Hash(GameEngine engine)
        {
            var s = engine.State;
            var sb = new StringBuilder();
            sb.Append(s.Turn).Append('|').Append(s.Winner).Append('|').Append(s.TotalDeaths).Append('|');
            for (int t = 0; t < 2; t++)
            {
                var ts = s.Teams[t];
                for (int l = 0; l < 3; l++)
                {
                    sb.Append(ts.TowerHp[l]).Append(',');
                    foreach (var u in ts.Lanes[l]) sb.Append(u.Def.Id).Append(':').Append(u.Damage).Append(':').Append(u.BonusAtk).Append(' ');
                    sb.Append('/');
                }
                foreach (var p in ts.Players)
                {
                    sb.Append('h');
                    foreach (var c in p.Hand) sb.Append(c).Append('.');
                    sb.Append('m').Append(p.Mana);
                }
                sb.Append('|');
            }
            return sb.ToString();
        }
    }

    /// <summary>여러 판을 돌려 집계한다.</summary>
    public sealed class BatchStats
    {
        public string Label;
        public int Games;
        public int[] Wins = new int[2];
        public int Draws;
        public int TurnSum;
        public int EarlyEnds;
        public int TowersSum;
        public int DeathsSum;
        public Dictionary<string, int> EndReasons = new Dictionary<string, int>();
        public Dictionary<int, (int placedGames, int wonWhenPlaced)> Units = new Dictionary<int, (int, int)>();
        public Dictionary<AugmentId, (int picked, int won)> Augments = new Dictionary<AugmentId, (int, int)>();
        /// <summary>한 팀만 가진 판에서의 승률 (양 팀이 다 고르면 효과가 상쇄되므로 이쪽이 정직하다).</summary>
        public Dictionary<AugmentId, (int games, int won)> AugmentsExclusive = new Dictionary<AugmentId, (int, int)>();
        public Dictionary<LaneRuleId, (int games, int early)> LaneRules = new Dictionary<LaneRuleId, (int, int)>();
        public Dictionary<MissionId, (int assigned, int done)> Missions = new Dictionary<MissionId, (int, int)>();

        public static BatchStats Run(string label, GameConfig cfg, IAgent[] agents, int games, ulong seedBase)
        {
            var st = new BatchStats { Label = label, Games = games };
            for (int g = 0; g < games; g++)
            {
                var r = Simulator.Play(cfg, seedBase + (ulong)g, agents);
                st.Add(r);
            }
            return st;
        }

        public void Add(MatchResult r)
        {
            if (r.Winner < 0) Draws++; else Wins[r.Winner]++;
            TurnSum += r.Turns;
            if (r.EndReason != "파괴한 타워 수" && r.EndReason != "남은 타워 체력 합" && r.EndReason != "미드 타워 체력" && r.EndReason != "무승부") EarlyEnds++;
            TowersSum += r.TowersDestroyedBy[0] + r.TowersDestroyedBy[1];
            DeathsSum += r.TotalDeaths;
            EndReasons.TryGetValue(r.EndReason, out var er); EndReasons[r.EndReason] = er + 1;
            for (int t = 0; t < 2; t++)
            {
                bool won = r.Winner == t;
                foreach (var id in r.UnitsPlaced[t])
                {
                    Units.TryGetValue(id, out var u); Units[id] = (u.placedGames + 1, u.wonWhenPlaced + (won ? 1 : 0));
                }
                foreach (var a in r.Augments[t])
                {
                    Augments.TryGetValue(a, out var v); Augments[a] = (v.picked + 1, v.won + (won ? 1 : 0));
                }
                foreach (var (m, done) in r.Missions[t])
                {
                    Missions.TryGetValue(m, out var v); Missions[m] = (v.assigned + 1, v.done + (done ? 1 : 0));
                }
            }
            foreach (var a in Catalog.Augments)
            {
                bool t0 = r.Augments[0].Contains(a), t1 = r.Augments[1].Contains(a);
                if (t0 == t1) continue;
                int holder = t0 ? 0 : 1;
                AugmentsExclusive.TryGetValue(a, out var v);
                AugmentsExclusive[a] = (v.games + 1, v.won + (r.Winner == holder ? 1 : 0));
            }
            bool early = r.Turns < 7 || (r.EndReason == "타워 2개 파괴" || r.EndReason == "전령 타워 파괴");
            for (int l = 0; l < 3; l++)
                if (r.LaneRules[l].HasValue)
                {
                    LaneRules.TryGetValue(r.LaneRules[l].Value, out var v);
                    LaneRules[r.LaneRules[l].Value] = (v.games + 1, v.early + (early ? 1 : 0));
                }
        }

        public string ToMarkdown(bool detail)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### {Label} ({Games}판)");
            sb.AppendLine();
            sb.AppendLine("| 팀0 승 | 팀1 승 | 무승부 | 평균 턴 | 조기 종료 | 판당 파괴 타워 | 판당 사망 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            sb.AppendLine($"| {Pct(Wins[0])} | {Pct(Wins[1])} | {Pct(Draws)} | {(double)TurnSum / Games:F2} | {Pct(EarlyEnds)} | {(double)TowersSum / Games:F2} | {(double)DeathsSum / Games:F1} |");
            sb.AppendLine();
            sb.AppendLine("종료 사유: " + string.Join(", ", Sorted(EndReasons)));
            if (!detail) return sb.ToString();

            sb.AppendLine();
            sb.AppendLine("| 유닛 | 등장률 | 등장 시 승률 |");
            sb.AppendLine("|---|---|---|");
            foreach (var u in Catalog.Units)
            {
                Units.TryGetValue(u.Id, out var v);
                sb.AppendLine($"| {u.Name} | {Pct2(v.placedGames, Games * 2)} | {Pct2(v.wonWhenPlaced, v.placedGames)} |");
            }
            sb.AppendLine();
            sb.AppendLine("| 증강 | 선택 수 | 선택 시 승률 | 한 팀만 가진 판 수 | 그때 승률 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var a in Catalog.Augments)
            {
                Augments.TryGetValue(a, out var v);
                AugmentsExclusive.TryGetValue(a, out var x);
                sb.AppendLine($"| {a} | {v.picked} | {Pct2(v.won, v.picked)} | {x.games} | {Pct2(x.won, x.games)} |");
            }
            sb.AppendLine();
            sb.AppendLine("| 라인 규칙 | 등장 판 수 | 등장 시 조기 종료율 |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in Catalog.LaneRules)
            {
                LaneRules.TryGetValue(r, out var v);
                sb.AppendLine($"| {r} | {v.games} | {Pct2(v.early, v.games)} |");
            }
            sb.AppendLine();
            sb.AppendLine("| 미션 | 배정 수 | 달성률 |");
            sb.AppendLine("|---|---|---|");
            foreach (var m in Catalog.Missions)
            {
                Missions.TryGetValue(m, out var v);
                sb.AppendLine($"| {m} | {v.assigned} | {Pct2(v.done, v.assigned)} |");
            }
            return sb.ToString();
        }

        string Pct(int n) => $"{100.0 * n / Games:F1}%";
        static string Pct2(int n, int d) => d == 0 ? "-" : $"{100.0 * n / d:F1}%";
        static IEnumerable<string> Sorted(Dictionary<string, int> d)
        {
            var list = new List<KeyValuePair<string, int>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in list) yield return $"{kv.Key} {kv.Value}";
        }
    }
}
