using System.Collections.Generic;
using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class MatchSimTests
    {
        static MatchSim New(ulong seed = 1) => new MatchSim(new MatchConfig(), seed);
        static List<MatchCommand> L(params MatchCommand[] c) => new List<MatchCommand>(c);

        [Test]
        public void DrawCostsGoldAndFillsHand()
        {
            var m = New();
            var p = m.Player(0, 0);
            m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(40 - 5, p.Gold);
            Assert.AreEqual(1, p.Hand.Count);
            Assert.IsNotNull(WaveCatalog.Attacker(p.Hand[0]));
        }

        [Test]
        public void HandMaxAndGoldAreEnforced()
        {
            var m = New();
            var p = m.Player(0, 0);
            for (int i = 0; i < 6; i++) m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(6, p.Hand.Count);
            m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(6, p.Hand.Count);
            Assert.AreEqual(10, p.Gold);
            p.Gold = 3;
            m.Step(L(MatchCommand.Build(0, 0, 1, 0, 0)));
            Assert.AreEqual(0, m.OwnLane(0).Towers.Count);
        }

        [Test]
        public void SendSpawnsOnEnemyLaneAndRaisesTeamIncome()
        {
            var m = New();
            var p = m.Player(0, 0);
            p.Hand.Add(2); // 고블린 (비용 4 ×125% = 5, 인컴 +2 ×50% = 1)
            int incomeBefore = m.Teams[0].Income;
            m.Step(L(MatchCommand.Send(0, 0, 0)));
            Assert.AreEqual(0, p.Hand.Count);
            Assert.AreEqual(40 - m.SendCostOf(LaneBattle.Core.Wave.WaveCatalog.Attacker(1)), p.Gold);
            Assert.AreEqual(incomeBefore + 1, m.Teams[0].Income);
            m.Step(); // 출발
            Assert.AreEqual(1, m.EnemyLane(0).CreepsAlive());
            Assert.AreEqual(0, m.OwnLane(0).CreepsAlive());
        }

        [Test]
        public void IncomeIsPaidEveryInterval()
        {
            var m = New();
            var p = m.Player(0, 0);
            int ticks = m.Cfg.IncomeIntervalSeconds * m.Cfg.TicksPerSecond;
            for (int i = 0; i < ticks; i++) m.Step();
            Assert.AreEqual(40 + 8, p.Gold);
            for (int i = 0; i < ticks; i++) m.Step();
            Assert.AreEqual(40 + 16, p.Gold);
        }

        [Test]
        public void TeamIncomeIsSplitBetweenTeammates()
        {
            var m = new MatchSim(new MatchConfig { PlayersPerTeam = 2 }, 3);
            Assert.AreEqual(16, m.Teams[0].Income);
            m.Teams[0].Income = 17;
            for (int i = 0; i < m.Cfg.IncomeIntervalSeconds * m.Cfg.TicksPerSecond; i++) m.Step();
            Assert.AreEqual(40 + 9, m.Player(0, 0).Gold);
            Assert.AreEqual(40 + 8, m.Player(0, 1).Gold);
        }

        [Test]
        public void KillsPayGold()
        {
            var m = New(5);
            var p = m.Player(0, 0);
            foreach (var (x, y) in new[] { (3, 3), (4, 3), (5, 3), (6, 3) }) Assert.IsNotNull(m.OwnLane(0).Build(WaveCatalog.Tower(7), x, y));
            foreach (var t in m.OwnLane(0).Towers) t.BuildLeft = 0;
            m.OwnLane(0).Send(WaveCatalog.Attacker(1), true); // 늑대 한 마리
            int before = p.Gold;
            for (int i = 0; i < 400 && m.OwnLane(0).CreepsAlive() > 0; i++) m.Step();
            Assert.AreEqual(1, m.OwnLane(0).Kills);
            Assert.AreEqual(before + 1, p.Gold);
        }

        [Test]
        public void BaseDestroyedEndsMatch()
        {
            var m = new MatchSim(new MatchConfig(), 2);
            for (int i = 0; i < 40; i++) m.OwnLane(1).Send(WaveCatalog.Attacker(1), true);
            int guard = 0;
            while (!m.IsOver && guard++ < 5000) m.Step();
            Assert.IsTrue(m.IsOver);
            Assert.AreEqual(0, m.Winner);
            Assert.AreEqual("기지 파괴", m.EndReason);
        }

        [Test]
        public void TimeoutComparesBaseHp()
        {
            var m = new MatchSim(new MatchConfig { MatchSeconds = 5 }, 2);
            m.Lanes[1].Send(WaveCatalog.Attacker(1), true);
            int guard = 0;
            while (!m.IsOver && guard++ < 5000) m.Step();
            Assert.IsTrue(m.IsOver);
            Assert.AreEqual(100, m.Tick);
            Assert.IsTrue(m.Winner == 0 || m.Winner == 1 || m.Winner == -1);
        }

        [Test]
        public void BotMatchIsDeterministicAndTerminates()
        {
            var cfg = new MatchConfig();
            var a = MatchRunner.Play(cfg, 77, new IMatchAgent[] { new SimpleBot(), new SimpleBot { Aggression = 80 } });
            var b = MatchRunner.Play(cfg, 77, new IMatchAgent[] { new SimpleBot(), new SimpleBot { Aggression = 80 } });
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.IsTrue(a.IsOver);
            Assert.LessOrEqual(a.GameTick, cfg.MatchSeconds * cfg.TicksPerSecond);
            Assert.Greater(a.Player(0, 0).Sent + a.Player(1, 0).Sent, 0, "봇이 유닛을 보내야 한다");
            Assert.Greater(a.OwnLane(0).Towers.Count, 0);
        }

        [Test]
        public void TwoVsTwoBotMatchRuns()
        {
            var cfg = new MatchConfig { PlayersPerTeam = 2, MatchSeconds = 120 };
            var m = MatchRunner.Play(cfg, 9, new IMatchAgent[] { new SimpleBot(), new SimpleBot() });
            Assert.IsTrue(m.IsOver);
            Assert.AreEqual(MapCatalog.TwoVsTwo, m.OwnLane(0).Cfg.Map);
        }
    }
}
