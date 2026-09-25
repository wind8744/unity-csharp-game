using System;
using System.Collections.Generic;

namespace LaneBattle.Core
{
    /// <summary>한 플레이어의 결정을 내리는 에이전트. 공개 정보만 봐야 한다 (VisibleUnits 사용).</summary>
    public interface IAgent
    {
        string Name { get; }
        PlayerCommand Decide(GameEngine engine, int team, int player, Rng rng);
    }

    /// <summary>합법적인 수를 무작위로 두는 기준선.</summary>
    public sealed class RandomAgent : IAgent
    {
        public string Name => "Random";

        public PlayerCommand Decide(GameEngine engine, int team, int player, Rng rng)
        {
            var ps = engine.Player(team, player);
            var cmd = new PlayerCommand();
            if (ps.Offers.Count > 0) cmd.AugmentChoice = rng.Next(ps.Offers.Count);

            int mana = ps.Mana;
            var laneCount = new int[3];
            for (int l = 0; l < 3; l++) laneCount[l] = engine.LaneUnits(team, (Lane)l).Count;
            var hand = new List<int>(ps.Hand);
            rng.Shuffle(hand);
            foreach (var id in hand)
            {
                if (rng.Next(5) == 0) continue; // 가끔 아낀다
                var lane = (Lane)rng.Next(3);
                if (laneCount[(int)lane] >= engine.LaneCapacity(lane)) continue;
                int cost = engine.CostOf(team, id, lane);
                if (cost > mana) continue;
                mana -= cost;
                laneCount[(int)lane]++;
                cmd.Placements.Add(new Placement(id, lane));
            }
            return cmd;
        }
    }

    /// <summary>
    /// 단순 휴리스틱: 가장 급한 라인(내 타워가 위험하거나 상대 타워가 약한 곳)을 고르고,
    /// 비싼 유닛부터 그 라인에 채운다. 상대는 보이는 유닛만 평가한다.
    /// </summary>
    public sealed class GreedyAgent : IAgent
    {
        public string Name => "Greedy";

        static readonly AugmentId[] Preference =
        {
            AugmentId.Overload, AugmentId.Abundance, AugmentId.Bond, AugmentId.Gambler, AugmentId.Ambush,
            AugmentId.TopKeeper, AugmentId.Twins, AugmentId.Retreat, AugmentId.Scout, AugmentId.MindGames,
        };

        public PlayerCommand Decide(GameEngine engine, int team, int player, Rng rng)
        {
            var ps = engine.Player(team, player);
            var cmd = new PlayerCommand();
            if (ps.Offers.Count > 0)
            {
                int best = 0, bestRank = int.MaxValue;
                for (int i = 0; i < ps.Offers.Count; i++)
                {
                    int rank = Array.IndexOf(Preference, ps.Offers[i]);
                    if (rank < bestRank) { bestRank = rank; best = i; }
                }
                cmd.AugmentChoice = best;
            }

            int enemy = 1 - team;
            var me = engine.Team(team); var them = engine.Team(enemy);
            var score = new double[3];
            var laneCount = new int[3];
            for (int l = 0; l < 3; l++)
            {
                var lane = (Lane)l;
                laneCount[l] = engine.LaneUnits(team, lane).Count;
                if (them.TowerDestroyed[l]) { score[l] = -1000; continue; }
                double ours = 0, theirs = 0;
                foreach (var u in engine.LaneUnits(team, lane)) ours += engine.EffectiveAtk(u) + engine.Hp(u);
                foreach (var u in engine.VisibleUnits(enemy, lane)) theirs += engine.EffectiveAtk(u) + engine.Hp(u);
                score[l] = (theirs - ours) * 1.0
                         + (engine.Config.EffectiveTowerHp - me.TowerHp[l]) * 0.6
                         + (engine.Config.EffectiveTowerHp - them.TowerHp[l]) * 0.6
                         + (theirs == 0 ? 3 : 0);
                if (me.TowerDestroyed[l]) score[l] -= 500; // 이미 잃은 라인은 방어 의미 없음
                var rule = engine.State.RuleAt(lane);
                if (rule == LaneRuleId.Herald) score[l] += 8;
                if (rule == LaneRuleId.DragonNest) score[l] -= 2;
                score[l] += rng.Next(3) * 0.1; // 동률 깨기
            }
            var order = new List<int> { 0, 1, 2 };
            order.Sort((a, b) => score[b].CompareTo(score[a]));

            var hand = new List<int>(ps.Hand);
            hand.Sort((a, b) => Catalog.Unit(b).Cost.CompareTo(Catalog.Unit(a).Cost));
            int mana = ps.Mana;
            foreach (var id in hand)
            {
                foreach (var l in order)
                {
                    var lane = (Lane)l;
                    if (score[l] < -100) continue;
                    if (laneCount[l] >= engine.LaneCapacity(lane)) continue;
                    int cost = engine.CostOf(team, id, lane);
                    if (cost > mana) continue;
                    mana -= cost;
                    laneCount[l]++;
                    cmd.Placements.Add(new Placement(id, lane));
                    break;
                }
            }
            return cmd;
        }
    }
}

namespace LaneBattle.Core
{
    public enum AiStyle { Aggressor, Mirror, Rotator }

    /// <summary>
    /// 읽을 수 있는 성향을 가진 상대. 사람이 지난 배치를 보고 패턴을 눈치채는 게 목적.
    /// Aggressor: 상대의 가장 약한 타워를 계속 민다. Mirror: 상대 유닛이 가장 많은 라인에 맞선다. Rotator: 라인을 돌아가며 민다.
    /// </summary>
    public sealed class StyleAgent : IAgent
    {
        public AiStyle Style { get; }
        public string Name => "Style:" + Style;
        readonly int _offset;

        public StyleAgent(AiStyle style, int offset = 0) { Style = style; _offset = offset; }

        public PlayerCommand Decide(GameEngine engine, int team, int player, Rng rng)
        {
            var ps = engine.Player(team, player);
            var cmd = new PlayerCommand();
            if (ps.Offers.Count > 0) cmd.AugmentChoice = rng.Next(ps.Offers.Count);

            int enemy = 1 - team;
            var them = engine.Team(enemy);
            int target = -1;
            switch (Style)
            {
                case AiStyle.Aggressor:
                    for (int l = 0; l < 3; l++)
                        if (!them.TowerDestroyed[l] && (target < 0 || them.TowerHp[l] < them.TowerHp[target])) target = l;
                    break;
                case AiStyle.Mirror:
                    int best = -1;
                    for (int l = 0; l < 3; l++)
                    {
                        if (them.TowerDestroyed[l]) continue;
                        int n = 0; foreach (var _ in engine.VisibleUnits(enemy, (Lane)l)) n++;
                        if (n > best) { best = n; target = l; }
                    }
                    break;
                case AiStyle.Rotator:
                    for (int k = 0; k < 3; k++)
                    {
                        int l = (engine.State.Turn + _offset + k) % 3;
                        if (!them.TowerDestroyed[l]) { target = l; break; }
                    }
                    break;
            }
            if (target < 0) return cmd;
            if (rng.Next(6) == 0) target = (target + 1 + rng.Next(2)) % 3; // 가끔 딴 데

            var order = new List<int> { target };
            for (int l = 0; l < 3; l++) if (l != target && !them.TowerDestroyed[l]) order.Add(l);

            var laneCount = new int[3];
            for (int l = 0; l < 3; l++) laneCount[l] = engine.LaneUnits(team, (Lane)l).Count;
            var hand = new List<int>(ps.Hand);
            hand.Sort((a, b) => Catalog.Unit(b).Cost.CompareTo(Catalog.Unit(a).Cost));
            int mana = ps.Mana;
            foreach (var id in hand)
                foreach (var l in order)
                {
                    var lane = (Lane)l;
                    if (laneCount[l] >= engine.LaneCapacity(lane)) continue;
                    int cost = engine.CostOf(team, id, lane);
                    if (cost > mana) continue;
                    mana -= cost; laneCount[l]++;
                    cmd.Placements.Add(new Placement(id, lane));
                    break;
                }
            return cmd;
        }
    }
}
