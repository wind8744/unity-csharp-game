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
            Assert.AreEqual(3500, lane.EffectiveRange(a)); // 4.5 - 1
        }

        [Test]
        public void AugmentsAreOfferedAtLevels1369WithoutPausing()
        {
            var m = new MatchSim(new MatchConfig(), 5);
            m.Step();
            var p = m.Player(0, 0);
            Assert.AreEqual(3, p.Offers.Count, "레벨 1: 시작하자마자 3장");
            Assert.IsFalse(m.IsPaused);
            m.Step(L(MatchCommand.Build(0, 0, 1, 0, 0)));
            Assert.AreEqual(1, m.OwnLane(0).Towers.Count, "고르는 동안에도 게임은 계속");
            m.Step(L(MatchCommand.PickAugment(0, 0, 1)));
            Assert.AreEqual(1, p.Augments.Count); Assert.AreEqual(0, p.Offers.Count);
            // 상대는 안 고르면 마감에 자동 선택
            var q = m.Player(1, 0);
            Assert.AreEqual(3, q.Offers.Count);
            for (int i = 0; i < m.Cfg.AugmentChoiceSeconds * m.Cfg.TicksPerSecond + 2; i++) m.Step();
            Assert.AreEqual(1, q.Augments.Count); Assert.AreEqual(0, q.Offers.Count);
            // 기다리는 동안 수입 경험치로 레벨 2 (증강 없음), 레벨 3 에 두 번째
            Assert.AreEqual(2, p.Level); Assert.AreEqual(0, p.Offers.Count, "레벨 2 엔 증강이 없다");
            p.Gold = 500;
            m.Step(L(MatchCommand.BuyXp(0, 0)));          // 2→3 (4 필요)
            Assert.AreEqual(3, p.Level); Assert.AreEqual(3, p.Offers.Count, "레벨 3: 두 번째 증강");
            m.Step(L(MatchCommand.PickAugment(0, 0, 0)));
            for (int i = 0; i < 60; i++) m.Step(L(MatchCommand.BuyXp(0, 0)));
            Assert.AreEqual(WaveCatalog.MaxLevel, p.Level);
            Assert.AreEqual(4, p.OfferedLevels.Count, "1·3·6·9 네 번");
            Assert.AreEqual(4, p.AugmentPicks + p.Offers.Count / 3);
        }

        [Test]
        public void LegacyGivesGoldAndMerchantCheapensDraw()
        {
            var m = new MatchSim(new MatchConfig(), 3);
            Run(m, 20);
            var p = m.Player(0, 0);
            p.Offers.Clear(); p.Offers.Add(AugmentId.Legacy); p.Offers.Add(AugmentId.Merchant); p.Offers.Add(AugmentId.Fortress);
            m.Step(L(MatchCommand.PickAugment(0, 0, 0)));
            Assert.AreEqual(m.Cfg.StartGold + 25, p.Gold);
            p.Augments.Add(AugmentId.Merchant);
            Run(m, 20);
            int before = p.Gold;
            m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(before - 3, p.Gold);
        }

        [Test]
        public void EventWindowStartsAndEnds()
        {
            var cfg = new MatchConfig { EventSeconds = new[] { 3 }, EventWarnSeconds = 1, EventDurationSeconds = 2 };
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
            Assert.GreaterOrEqual(a.Player(0, 0).AugmentPicks, 2, "봇도 레벨 1·3 은 넘는다");
            Assert.AreEqual(2, a.EventRound);
        }
    }
}
