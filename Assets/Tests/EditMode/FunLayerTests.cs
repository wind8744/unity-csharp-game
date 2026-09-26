using System.Collections.Generic;
using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class FunLayerTests
    {
        static List<MatchCommand> L(params MatchCommand[] c) => new List<MatchCommand>(c);
        static void Run(MatchSim m, int ticks) { for (int i = 0; i < ticks; i++) m.Step(); }

        [Test]
        public void ForestThreeExtendsRangeAndArcherRowAddsMore()
        {
            var lane = new LaneSim(new LaneConfig { AutoWaves = false }, 1);
            var a = lane.Build(WaveCatalog.Tower(1), 0, 0); lane.Build(WaveCatalog.Tower(1), 1, 0); lane.Build(WaveCatalog.Tower(2), 2, 0);
            foreach (var t in lane.Towers) t.BuildLeft = 0;
            lane.Step();
            Assert.IsTrue(lane.Forest3);
            Assert.IsTrue(a.JobAdj);
            Assert.AreEqual((30 + 5 + 10) * 100, lane.EffectiveRange(a));
        }

        [Test]
        public void NightEventShrinksRange()
        {
            var lane = new LaneSim(new LaneConfig { AutoWaves = false }, 1);
            var a = lane.Build(WaveCatalog.Tower(7), 0, 0);
            lane.Mod = new LaneModifiers { TowerRangeDelta10 = -10 };
            Assert.AreEqual(3000, lane.EffectiveRange(a));
        }

        [Test]
        public void AugmentPauseOffersThreeAndResumes()
        {
            var cfg = new MatchConfig { AugmentSeconds = new[] { 2 }, AugmentPauseSeconds = 1 };
            var m = new MatchSim(cfg, 5);
            Run(m, 40);
            Assert.IsTrue(m.IsPaused);
            Assert.AreEqual(3, m.Player(0, 0).Offers.Count);
            int gameTickAtPause = m.GameTick;
            m.Step(L(MatchCommand.Build(0, 0, 1, 0, 0)));
            Assert.AreEqual(0, m.OwnLane(0).Towers.Count, "정지 중엔 짓기가 거부된다");
            m.Step(L(MatchCommand.PickAugment(0, 0, 0)));
            Assert.AreEqual(1, m.Player(0, 0).Augments.Count);
            Run(m, 20);
            Assert.IsFalse(m.IsPaused);
            Assert.AreEqual(1, m.Player(1, 0).Augments.Count, "안 고르면 자동 선택");
            Assert.Greater(m.GameTick, gameTickAtPause);
        }

        [Test]
        public void LegacyGivesGoldAndMerchantCheapensDraw()
        {
            var m = new MatchSim(new MatchConfig { AugmentSeconds = new[] { 1 }, AugmentPauseSeconds = 1 }, 3);
            Run(m, 20);
            var p = m.Player(0, 0);
            p.Offers.Clear(); p.Offers.Add(AugmentId.Legacy); p.Offers.Add(AugmentId.Merchant); p.Offers.Add(AugmentId.Fortress);
            m.Step(L(MatchCommand.PickAugment(0, 0, 0)));
            Assert.AreEqual(40 + 25, p.Gold);
            p.Augments.Add(AugmentId.Merchant);
            Run(m, 20);
            int before = p.Gold;
            m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(before - 3, p.Gold);
        }

        [Test]
        public void EventWindowStartsAndEnds()
        {
            var cfg = new MatchConfig { EventSeconds = new[] { 3 }, EventWarnSeconds = 1, EventDurationSeconds = 2, AugmentSeconds = new int[0] };
            var m = new MatchSim(cfg, 11);
            Assert.IsTrue(m.NextEvent.HasValue);
            var expected = m.NextEvent.Value;
            Run(m, 61);
            Assert.AreEqual(expected, m.ActiveEvent);
            Run(m, 45);
            Assert.IsNull(m.ActiveEvent);
        }

        [Test]
        public void HordeMissionCompletes()
        {
            var m = new MatchSim(new MatchConfig(), 2);
            var p = m.Player(0, 0);
            p.Mission = MissionId.Horde; p.Gold = 100;
            for (int i = 0; i < 5; i++) p.Hand.Add(1);
            for (int i = 0; i < 5; i++) m.Step(L(MatchCommand.Send(0, 0, 0)));
            Run(m, 25);
            Assert.IsTrue(p.MissionDone);
        }

        [Test]
        public void GroupSynergyBuffsThreeOfAKind()
        {
            var m = new MatchSim(new MatchConfig(), 4);
            var p = m.Player(0, 0);
            p.Gold = 100;
            for (int i = 0; i < 3; i++) p.Hand.Add(1); // 늑대 ×3
            for (int i = 0; i < 3; i++) m.Step(L(MatchCommand.Send(0, 0, 0)));
            int buffed = 0;
            foreach (var c in m.EnemyLane(0).Creeps) if (c.GroupNameBonus) buffed++;
            foreach (var c in new List<Creep>()) { }
            // 아직 대기열에 있을 수 있으니 FindCreep 으로 확인
            foreach (var (tick, defId, creepId) in m.Teams[0].RecentSends) if (m.EnemyLane(0).FindCreep(creepId).GroupNameBonus) buffed = System.Math.Max(buffed, 1);
            Assert.GreaterOrEqual(buffed, 1);
            Assert.AreEqual(36, m.EnemyLane(0).FindCreep(m.Teams[0].RecentSends[0].creepId).MaxHp); // 30 × 1.2
        }

        [Test]
        public void FullFunMatchIsDeterministicAndEnds()
        {
            var cfg = new MatchConfig();
            var a = MatchRunner.Play(cfg, 21, new IMatchAgent[] { new SimpleBot(), new SimpleBot { Aggression = 80 } });
            var b = MatchRunner.Play(cfg, 21, new IMatchAgent[] { new SimpleBot(), new SimpleBot { Aggression = 80 } });
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.IsTrue(a.IsOver);
            Assert.AreEqual(2, a.AugmentRound);
            Assert.GreaterOrEqual(a.Player(0, 0).Augments.Count, 2);
            Assert.AreEqual(2, a.EventRound);
        }
    }
}
