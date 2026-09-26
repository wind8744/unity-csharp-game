using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    /// <summary>경기 중 한 자리를 조종하는 에이전트. 1초마다 호출된다.</summary>
    public interface IMatchAgent
    {
        void Decide(MatchSim sim, int team, int player, List<MatchCommand> output);
    }

    /// <summary>
    /// 단순 봇: 오는 유닛을 보고 타워를 짓고, 남는 골드로 뽑아서 보낸다. 상대 라인에 대공이 없으면 공중을 보낸다.
    /// 무작위 없음 → 결정론.
    /// </summary>
    public sealed class SimpleBot : IMatchAgent
    {
        static readonly (int col, int row)[] SlotOrder4 = { (1, 0), (2, 0), (1, 1), (2, 1), (0, 0), (3, 0), (0, 1), (3, 1), (1, 2), (2, 2), (0, 2), (3, 2) };

        public int Aggression = 50; // 0~100. 높을수록 보내기에 골드를 더 쓴다

        public void Decide(MatchSim sim, int team, int player, List<MatchCommand> output)
        {
            var p = sim.Player(team, player);
            if (p.Offers.Count > 0) { output.Add(MatchCommand.PickAugment(team, player, PickAugmentIndex(p))); return; }
            if (sim.IsPaused) return;
            var lane = sim.OwnLane(team);
            var enemyLane = sim.EnemyLane(team);
            int seconds = sim.Seconds;

            // 위협 파악
            int flyersIncoming = 0, giantsIncoming = 0, incoming = 0;
            foreach (var c in lane.Creeps) { if (!c.Alive) continue; incoming++; if (c.Def.Flying) flyersIncoming++; if (c.Def.Tribe == AtkTribe.Giant) giantsIncoming++; }
            int nextWave = lane.NextWaveIndex;
            if (nextWave < WaveCatalog.BaseWaves.Length)
                foreach (var (id, n, _) in WaveCatalog.BaseWaves[nextWave]) { var d = WaveCatalog.Attacker(id); if (d.Flying) flyersIncoming += n; if (d.Tribe == AtkTribe.Giant) giantsIncoming += n; }

            int towers = 0, antiAir = 0, cannons = 0;
            foreach (var t in lane.Towers) { if (!t.Alive) continue; towers++; if (t.Def.AntiAir) antiAir++; if (t.Def.GiantMultiplier > 1) cannons++; }

            // 방어 목표: 시간에 따라 늘어나는 최소 타워 수 + 위협 대응
            int wantTowers = 2 + seconds / 45;
            int reserve = 4 + seconds / 60 * 2;

            // 1. 타워 짓기
            var slots = FreeSlots(lane);
            if (slots.Count > 0)
            {
                int towerId = -1;
                if (flyersIncoming > 0 && antiAir < 1 + flyersIncoming / 3) towerId = p.Gold >= 14 && seconds > 120 ? 6 : 1;
                else if (giantsIncoming > 0 && cannons == 0 && p.Gold >= 15) towerId = 8;
                else if (towers < wantTowers) towerId = PickGeneral(towers, p.Gold);
                if (towerId > 0 && p.Gold >= WaveCatalog.Tower(towerId).Cost)
                {
                    var (col, row) = slots[0];
                    output.Add(MatchCommand.Build(team, player, towerId, col, row));
                    return;
                }
            }

            // 2. 슬롯이 꽉 찼으면 강화
            if (slots.Count == 0)
                foreach (var t in lane.Towers)
                    if (t.Alive && !t.Upgraded && p.Gold >= t.Def.Cost + reserve) { output.Add(MatchCommand.Upgrade(team, player, t.Id)); return; }

            // 2b. 자리가 2칸 이하로 남으면 같은 타워 3개 합치기 → 레시피 합성 (둘 다 공짜, 슬롯이 는다)
            if (slots.Count <= 2)
            {
                foreach (var t in lane.Towers)
                    if (t.Alive && t.Owner == player && sim.MergeMates(team, t) != null) { output.Add(MatchCommand.Merge(team, player, t.Id)); return; }
                foreach (var t in lane.Towers)
                {
                    if (!t.Alive || t.Owner != player || t.Def.IsFused) continue;
                    var opts = sim.FuseOptions(team, t);
                    if (opts.Count > 0) { output.Add(MatchCommand.Fuse(team, player, t.Id, opts[0].partnerId)); return; }
                }
            }

            // 3. 보내기: 상대 대공이 없으면 공중 우선
            int enemyAntiAir = 0;
            foreach (var t in enemyLane.Towers) if (t.Alive && t.Def.AntiAir) enemyAntiAir++;
            int spendable = p.Gold - reserve;
            if (spendable <= 0) return;
            if (towers < wantTowers && Aggression < 70) return; // 방어가 부족하면 아낀다

            int best = -1, bestScore = int.MinValue;
            for (int i = 0; i < p.Hand.Count; i++)
            {
                var d = WaveCatalog.Attacker(p.Hand[i]);
                if (sim.SendCostOf(d) > spendable) continue;
                int score = d.Income * 3 + d.Leak * 2;
                if (d.Flying) score += enemyAntiAir == 0 ? 10 : -6;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) { output.Add(MatchCommand.Send(team, player, best)); return; }

            // 4. 손패가 비었거나 다 비싸면 뽑기
            if (p.Hand.Count < sim.Cfg.HandMax && spendable >= sim.Cfg.DrawCost && p.Gold >= sim.Cfg.DrawCost + reserve)
                output.Add(MatchCommand.Draw(team, player));
        }

        static readonly AugmentId[] Preference = { AugmentId.Legacy, AugmentId.Mercenaries, AugmentId.Fortress, AugmentId.Venom, AugmentId.Interest, AugmentId.Elite, AugmentId.Merchant, AugmentId.AirNet, AugmentId.Curse, AugmentId.Corrosion, AugmentId.SilenceShell, AugmentId.Accountant, AugmentId.Scout, AugmentId.Wiretap };

        static int PickAugmentIndex(PlayerEcon p)
        {
            int best = 0, bestRank = int.MaxValue;
            for (int i = 0; i < p.Offers.Count; i++)
            {
                int rank = System.Array.IndexOf(Preference, p.Offers[i]);
                if (rank < bestRank) { bestRank = rank; best = i; }
            }
            return best;
        }

        static int PickGeneral(int towers, int gold)
        {
            if (towers % 4 == 3 && gold >= 8) return 7;   // 포탑
            if (towers % 4 == 2 && gold >= 7) return 4;   // 창탑
            if (towers % 4 == 1 && gold >= 9 && towers > 3) return 5; // 술사
            return 1;                                      // 궁수
        }

        static List<(int, int)> FreeSlots(LaneSim lane)
        {
            var list = new List<(int, int)>();
            if (lane.Cfg.Width == 4)
            {
                foreach (var (c, r) in SlotOrder4) if (lane.TowerAt(c, r) == null) list.Add((c, r));
                return list;
            }
            int mid = lane.Cfg.Width / 2;
            for (int r = 0; r < lane.Cfg.Rows; r++)
                for (int d = 0; d < lane.Cfg.Width; d++)
                {
                    int c = mid + (d % 2 == 0 ? d / 2 : -(d / 2 + 1));
                    if (c < 0 || c >= lane.Cfg.Width) continue;
                    if (lane.TowerAt(c, r) == null) list.Add((c, r));
                }
            return list;
        }
    }

    /// <summary>봇끼리 한 판을 돌린다 (테스트·밸런스용).</summary>
    public static class MatchRunner
    {
        public static MatchSim Play(MatchConfig cfg, ulong seed, IMatchAgent[] teamAgents, int decideEverySeconds = 1)
        {
            var sim = new MatchSim(cfg, seed);
            var cmds = new List<MatchCommand>();
            int every = decideEverySeconds * cfg.TicksPerSecond;
            while (!sim.IsOver)
            {
                cmds.Clear();
                if (sim.Tick % every == 0)
                    for (int t = 0; t < 2; t++)
                        for (int p = 0; p < cfg.PlayersPerTeam; p++)
                            teamAgents[t]?.Decide(sim, t, p, cmds);
                sim.Step(cmds);
            }
            return sim;
        }
    }
}
