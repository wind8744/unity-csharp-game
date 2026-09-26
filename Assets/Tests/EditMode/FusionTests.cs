using System.Collections.Generic;
using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    /// <summary>별 합치기·레시피 합성 (문서 19절).</summary>
    public class FusionTests
    {
        static MatchSim NewMatch(int perTeam = 1) => new MatchSim(new MatchConfig { PlayersPerTeam = perTeam, FunLayer = false }, 5);

        static void Run(MatchSim sim, params MatchCommand[] cmds) { sim.Step(new List<MatchCommand>(cmds)); }

        [Test]
        public void ThreeSameTowersMergeIntoStar2()
        {
            var sim = NewMatch();
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 1, 1, 0), MatchCommand.Build(0, 0, 1, 2, 0));
            var lane = sim.OwnLane(0);
            var target = lane.TowerAtCell(1, 0);
            Assert.IsNotNull(sim.MergeMates(0, target));
            Run(sim, MatchCommand.Merge(0, 0, target.Id));
            int alive = 0; Tower star = null;
            foreach (var t in lane.Towers) if (t.Alive) { alive++; star = t; }
            Assert.AreEqual(1, alive);
            Assert.AreEqual(2, star.Star);
            Assert.AreEqual(1, star.Cx); Assert.AreEqual(0, star.Cy);
            Assert.AreEqual(0, star.BuildLeft, "합친 타워는 즉시 완성");
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Merged && e.A == star.Id && e.B == 2));
            Assert.IsNull(sim.MergeMates(0, star));
        }

        [Test]
        public void MergeNeedsSameDefinitionAndStar()
        {
            var sim = NewMatch();
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 1, 1, 0), MatchCommand.Build(0, 0, 2, 2, 0));
            var lane = sim.OwnLane(0);
            Run(sim, MatchCommand.Merge(0, 0, lane.TowerAtCell(0, 0).Id));
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Rejected));
            int alive = 0; foreach (var t in lane.Towers) if (t.Alive) alive++;
            Assert.AreEqual(3, alive);
        }

        [Test]
        public void Star2TowerHitsHarderAndReachesFurther()
        {
            var cfg = new LaneConfig { AutoWaves = false, TowerDamagePercent = 100, BuildSeconds = 0, Map = MapDef.Straight(20) };
            var one = new LaneSim(cfg, 1); var star = new LaneSim(cfg, 1);
            var a = one.Build(WaveCatalog.Tower(7), 0, 0);
            var b1 = star.Build(WaveCatalog.Tower(7), 0, 0); var b2 = star.Build(WaveCatalog.Tower(7), 1, 0); var b3 = star.Build(WaveCatalog.Tower(7), 2, 0);
            var merged = star.MergeStar(b1.Id, b2.Id, b3.Id);
            Assert.AreEqual(2, merged.Star);
            Assert.AreEqual(one.EffectiveRange(a) + 500, star.EffectiveRange(merged));
            one.Send(WaveCatalog.Attacker(4), true, 0, 1); star.Send(WaveCatalog.Attacker(4), true, 0, 1);
            int dmgOne = 0, dmgStar = 0;
            for (int i = 0; i < 800; i++)
            {
                one.Step(); star.Step();
                foreach (var e in one.Events) if (e.Type == SimEventType.Attack) { dmgOne = e.Value; break; }
                foreach (var e in star.Events) if (e.Type == SimEventType.Attack) { dmgStar = e.Value; break; }
                if (dmgOne > 0 && dmgStar > 0) break;
            }
            Assert.AreEqual(14, dmgOne);
            Assert.AreEqual(14 * 220 / 100, dmgStar);
        }

        [Test]
        public void RecipeFusesTwoTowersIntoTier2()
        {
            var sim = NewMatch();
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 4, 3, 0));
            var lane = sim.OwnLane(0);
            var archer = lane.TowerAtCell(0, 0); var spear = lane.TowerAtCell(3, 0);
            var opts = sim.FuseOptions(0, archer);
            Assert.AreEqual(1, opts.Count);
            Assert.AreEqual(10, opts[0].result.Id);
            Run(sim, MatchCommand.Fuse(0, 0, archer.Id, spear.Id));
            var fused = lane.TowerAtCell(0, 0);
            Assert.IsNotNull(fused);
            Assert.AreEqual("불화살 사수", fused.Def.Name);
            Assert.IsTrue(fused.Def.IsFused);
            Assert.IsNull(lane.TowerAtCell(3, 0), "재료는 사라진다");
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Fused && e.B == 10));
            Assert.AreEqual(13 * 80 / 100, MatchSim.SellValueOf(fused));
        }

        [Test]
        public void FuseRejectsPairsWithoutRecipe()
        {
            var sim = NewMatch();
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 2, 1, 0));
            var lane = sim.OwnLane(0);
            Run(sim, MatchCommand.Fuse(0, 0, lane.TowerAtCell(0, 0).Id, lane.TowerAtCell(1, 0).Id));
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Rejected));
            Assert.IsNull(WaveCatalog.FindRecipe(1, 2));
            Assert.IsNotNull(WaveCatalog.FindRecipe(4, 1), "순서 무관");
        }

        [Test]
        public void EveryFusedTowerHasTwoDistinctBasicIngredients()
        {
            foreach (var t in WaveCatalog.FusedTowers)
            {
                Assert.AreNotEqual(t.RecipeA, t.RecipeB, t.Name);
                Assert.AreEqual(1, WaveCatalog.Tower(t.RecipeA).Tier, t.Name);
                Assert.AreEqual(1, WaveCatalog.Tower(t.RecipeB).Tier, t.Name);
                Assert.AreEqual(WaveCatalog.Tower(t.RecipeA).Cost + WaveCatalog.Tower(t.RecipeB).Cost, t.Cost, t.Name + " 비용은 재료 합");
            }
            Assert.AreEqual(9, WaveCatalog.BasicTowers.Length);
        }

        [Test]
        public void SellValueGrowsWithStars()
        {
            var t = new Tower { Def = WaveCatalog.Tower(1), Star = 1 };
            Assert.AreEqual(6 * 80 / 100, MatchSim.SellValueOf(t));
            t.Star = 2; Assert.AreEqual(18 * 80 / 100, MatchSim.SellValueOf(t));
            t.Upgraded = true; Assert.AreEqual(36 * 80 / 100, MatchSim.SellValueOf(t));
        }

        [Test]
        public void TeammatesCannotMergeEachOthersTowers()
        {
            var sim = NewMatch(2);
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 1, 1, 0), MatchCommand.Build(0, 1, 1, 2, 0));
            var lane = sim.OwnLane(0);
            Assert.IsNull(sim.MergeMates(0, lane.TowerAtCell(0, 0)));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void BotMatchesWithMergingStayDeterministic(int perTeam)
        {
            var cfg = new MatchConfig { PlayersPerTeam = perTeam };
            var agents = new IMatchAgent[] { new SimpleBot(), new SimpleBot { Aggression = 80 } };
            var a = MatchRunner.Play(cfg, 11, agents);
            var b = MatchRunner.Play(cfg, 11, agents);
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.IsTrue(a.IsOver);
        }

        [Test]
        public void PlayerCanChooseWhichTowersToMergeAndWhichPartnerToFuse()
        {
            var sim = NewMatch();
            // 궁수 4개: 자동은 id 낮은 둘을 쓰지만, 직접 고르면 그 둘이 사라진다
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 1, 1, 0), MatchCommand.Build(0, 0, 1, 2, 0), MatchCommand.Build(0, 0, 1, 3, 0));
            var lane = sim.OwnLane(0);
            var a = lane.TowerAtCell(0, 0); var c = lane.TowerAtCell(2, 0); var d = lane.TowerAtCell(3, 0);
            Assert.AreEqual(3, sim.MergeCandidates(0, a).Count);
            Run(sim, MatchCommand.MergeWith(0, 0, a.Id, c.Id, d.Id));
            Assert.IsNotNull(lane.TowerAtCell(1, 0), "고르지 않은 궁수는 남는다");
            Assert.IsNull(lane.TowerAtCell(2, 0)); Assert.IsNull(lane.TowerAtCell(3, 0));
            Assert.AreEqual(2, lane.TowerAtCell(0, 0).Star);
            // 잘못 고르면(다른 정의) 거절
            Run(sim, MatchCommand.Build(0, 0, 2, 5, 0));
            Run(sim, MatchCommand.MergeWith(0, 0, lane.TowerAtCell(1, 0).Id, lane.TowerAtCell(5, 0).Id, lane.TowerAtCell(0, 0).Id));
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Rejected));
            // 합성 짝이 여럿: 원하는 짝을 고른다
            var sim2 = NewMatch();
            Run(sim2, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 4, 3, 0), MatchCommand.Build(0, 0, 4, 5, 0));
            var lane2 = sim2.OwnLane(0);
            var archer = lane2.TowerAtCell(0, 0);
            var partners = sim2.FusePartners(0, archer, 10);
            Assert.AreEqual(2, partners.Count);
            Run(sim2, MatchCommand.Fuse(0, 0, archer.Id, lane2.TowerAtCell(5, 0).Id));
            Assert.IsNotNull(lane2.TowerAtCell(3, 0), "고르지 않은 창탑은 남는다");
            Assert.IsNull(lane2.TowerAtCell(5, 0));
            Assert.AreEqual(10, lane2.TowerAtCell(0, 0).Def.Id);
        }
    }
}
