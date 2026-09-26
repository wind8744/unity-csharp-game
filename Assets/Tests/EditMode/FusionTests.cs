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
            var target = lane.TowerAt(1, 0);
            Assert.IsNotNull(sim.MergeMates(0, target));
            Run(sim, MatchCommand.Merge(0, 0, target.Id));
            int alive = 0; Tower star = null;
            foreach (var t in lane.Towers) if (t.Alive) { alive++; star = t; }
            Assert.AreEqual(1, alive);
            Assert.AreEqual(2, star.Star);
            Assert.AreEqual(1, star.Col); Assert.AreEqual(0, star.Row);
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
            Run(sim, MatchCommand.Merge(0, 0, lane.TowerAt(0, 0).Id));
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Rejected));
            int alive = 0; foreach (var t in lane.Towers) if (t.Alive) alive++;
            Assert.AreEqual(3, alive);
        }

        [Test]
        public void Star2TowerHitsHarderAndReachesFurther()
        {
            var cfg = new LaneConfig { AutoWaves = false, TowerDamagePercent = 100, BuildSeconds = 0 };
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
            Assert.AreEqual(12, dmgOne);
            Assert.AreEqual(12 * 220 / 100, dmgStar);
        }

        [Test]
        public void RecipeFusesTwoTowersIntoTier2()
        {
            var sim = NewMatch();
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 4, 1, 1));
            var lane = sim.OwnLane(0);
            var archer = lane.TowerAt(0, 0); var spear = lane.TowerAt(1, 1);
            var opts = sim.FuseOptions(0, archer);
            Assert.AreEqual(1, opts.Count);
            Assert.AreEqual(10, opts[0].result.Id);
            Run(sim, MatchCommand.Fuse(0, 0, archer.Id, spear.Id));
            var fused = lane.TowerAt(0, 0);
            Assert.IsNotNull(fused);
            Assert.AreEqual("불화살 사수", fused.Def.Name);
            Assert.IsTrue(fused.Def.IsFused);
            Assert.IsNull(lane.TowerAt(1, 1), "재료는 사라진다");
            Assert.IsTrue(sim.Events.Exists(e => e.Type == MatchEventType.Fused && e.B == 10));
            Assert.AreEqual(13 * 80 / 100, MatchSim.SellValueOf(fused));
        }

        [Test]
        public void FuseRejectsPairsWithoutRecipe()
        {
            var sim = NewMatch();
            Run(sim, MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 2, 1, 0));
            var lane = sim.OwnLane(0);
            Run(sim, MatchCommand.Fuse(0, 0, lane.TowerAt(0, 0).Id, lane.TowerAt(1, 0).Id));
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
            Assert.IsNull(sim.MergeMates(0, lane.TowerAt(0, 0)));
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
    }
}
