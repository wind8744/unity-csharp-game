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
                    var (x, y) = slots[0];
                    output.Add(MatchCommand.Build(team, player, towerId, x, y));
                    return;
                }
            }

            // 2. 슬롯이 꽉 찼으면 강화
            if (slots.Count == 0)
                foreach (var t in lane.Towers)
                    if (t.Alive && !t.Upgraded && t.Star >= 3 && p.Gold >= t.Def.Cost + reserve) { output.Add(MatchCommand.Upgrade(team, player, t.Id)); return; }

            // 2b. 합성은 공짜고 자리를 아끼니 짝이 생기면 바로 (2단계 둘 → 3단계 포함). 별 합치기는 타워가 8개 넘을 때.
            foreach (var t in lane.Towers)
            {
                if (!t.Alive || t.Owner != player || t.BuildLeft > 0) continue;
                var opts = sim.FuseOptions(team, t);
                if (opts.Count > 0) { output.Add(MatchCommand.Fuse(team, player, t.Id, opts[0].partnerId)); return; }
            }
            if (towers >= 8 || slots.Count <= 2)
                foreach (var t in lane.Towers)
                    if (t.Alive && t.Owner == player && sim.MergeMates(team, t) != null) { output.Add(MatchCommand.Merge(team, player, t.Id)); return; }

            // 2a'. 손패 조합이 완성돼 있으면 합친다 (히든이 더 세다)
            if (sim.HandRecipeReady(p) != null) { output.Add(MatchCommand.HandFuse(team, player)); return; }

            // 2b'. 경험치: 여유 골드가 있으면 30초마다 한 번 산다 (레벨이 시간에 뒤처지면 더 자주)
            if (p.Level < WaveCatalog.MaxLevel && p.Gold >= WaveCatalog.XpBuyCost + reserve + 10 && (seconds % 30 == 0 || p.Level < 2 + seconds / 60))
            { output.Add(MatchCommand.BuyXp(team, player)); return; }

            // 2c. 돌격 강화: 분당 1레벨 정도, 여유 골드가 있을 때
            {
                int lv = sim.Teams[team].SendLevel;
                int cost = MatchSim.UpgradeSendsCost(lv);
                if (sim.Cfg.SendUpgradesEnabled && cost > 0 && lv < seconds / 90 && p.Gold >= cost + reserve && Aggression >= 50) { output.Add(MatchCommand.UpgradeSends(team, player)); return; }
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

        /// <summary>기본 9종을 돌아가며 짓는다 (레시피 짝이 자연스럽게 생기도록). 처음 둘은 궁수.</summary>
        static readonly int[] BuildCycle = { 1, 1, 4, 7, 5, 9, 2, 3, 8, 6, 1, 4, 5, 7, 9, 2 };
        static int PickGeneral(int towers, int gold)
        {
            int id = BuildCycle[towers % BuildCycle.Length];
            if (gold >= WaveCatalog.Tower(id).Cost) return id;
            return 1;                                      // 궁수
        }

        /// <summary>빈 자리를 경로 커버 수(사거리 2.5 안 경로 칸 수)가 큰 순으로. 굽이 안쪽 구석이 먼저 나온다.</summary>
        static List<(int, int)> FreeSlots(LaneSim lane)
        {
            var map = lane.Map;
            var list = new List<(int x, int y, int cov)>();
            for (int x = 0; x < map.W; x++)
                for (int y = 0; y < map.H; y++)
                    if (lane.CanBuildAt(x, y) && map.Coverage[x, y] > 0) list.Add((x, y, map.Coverage[x, y]));
            list.Sort((a, b) => a.cov != b.cov ? b.cov.CompareTo(a.cov) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var result = new List<(int, int)>(list.Count);
            foreach (var (x, y, _) in list) result.Add((x, y));
            return result;
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
