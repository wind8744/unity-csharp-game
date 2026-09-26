using LaneBattle.Core;
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
            Assert.AreEqual(m.Cfg.StartGold - m.Cfg.DrawCost, p.Gold);
            Assert.AreEqual(1, p.Hand.Count);
            Assert.IsNotNull(WaveCatalog.Attacker(p.Hand[0]));
        }

        [Test]
        public void HandMaxAndGoldAreEnforced()
        {
            var m = New();
            var p = m.Player(0, 0);
            for (int i = 0; i < m.Cfg.HandMax; i++) m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(m.Cfg.HandMax, p.Hand.Count);
            m.Step(L(MatchCommand.Draw(0, 0)));
            Assert.AreEqual(m.Cfg.HandMax, p.Hand.Count);
            Assert.AreEqual(m.Cfg.StartGold - m.Cfg.HandMax * m.Cfg.DrawCost, p.Gold);
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
            Assert.AreEqual(m.Cfg.StartGold - m.SendCostOf(LaneBattle.Core.Wave.WaveCatalog.Attacker(2)), p.Gold);
            Assert.AreEqual(incomeBefore + 1, m.Teams[0].Income);
            m.Step(); // 출발
            Assert.AreEqual(1, m.EnemyLane(0).CreepsAlive());
            Assert.AreEqual(0, m.OwnLane(0).CreepsAlive());
        }

        [Test]
        public void IncomeIsPaidEveryInterval()
        {
            var m = new MatchSim(new MatchConfig { FunLayer = false }, 1);   // 증강(이자 등)이 골드에 끼지 않게
            var p = m.Player(0, 0);
            int ticks = m.Cfg.IncomeIntervalSeconds * m.Cfg.TicksPerSecond;
            for (int i = 0; i < ticks; i++) m.Step();
            Assert.AreEqual(m.Cfg.StartGold + m.Cfg.BaseIncomePerPlayer, p.Gold);
            for (int i = 0; i < ticks; i++) m.Step();
            Assert.AreEqual(m.Cfg.StartGold + 2 * m.Cfg.BaseIncomePerPlayer, p.Gold);
        }

        [Test]
        public void TeamIncomeIsSplitBetweenTeammates()
        {
            var m = new MatchSim(new MatchConfig { PlayersPerTeam = 2 }, 3);
            Assert.AreEqual(2 * m.Cfg.BaseIncomePerPlayer, m.Teams[0].Income);
            m.Teams[0].Income = 17;
            for (int i = 0; i < m.Cfg.IncomeIntervalSeconds * m.Cfg.TicksPerSecond; i++) m.Step();
            Assert.AreEqual(m.Cfg.StartGold + 9, m.Player(0, 0).Gold);
            Assert.AreEqual(m.Cfg.StartGold + 8, m.Player(0, 1).Gold);
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

        [Test]
        public void CommandEventsSurviveTheSameTickSoTheViewSeesThem()
        {
            var m = New(3);
            var lane = m.OwnLane(0);
            m.Step(L(MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 4, 3, 0)));
            Assert.AreEqual(2, lane.Events.FindAll(e => e.Type == SimEventType.Built).Count, "짓기 이벤트가 같은 틱에 남는다");
            var archer = lane.TowerAtCell(0, 0); var spear = lane.TowerAtCell(3, 0);
            m.Step(L(MatchCommand.Fuse(0, 0, archer.Id, spear.Id)));
            Assert.AreEqual(2, lane.Events.FindAll(e => e.Type == SimEventType.Sold).Count, "재료 둘의 사라짐 이벤트");
            Assert.IsTrue(lane.Events.Exists(e => e.Type == SimEventType.Fused));
            m.Step(L(MatchCommand.Sell(0, 0, lane.TowerAtCell(0, 0).Id)));
            Assert.IsTrue(lane.Events.Exists(e => e.Type == SimEventType.Sold), "판매 이벤트");
            m.Step();
            Assert.IsFalse(lane.Events.Exists(e => e.Type == SimEventType.Sold), "다음 틱엔 비워진다");
        }

        [Test]
        public void DrawOddsFollowPlayerLevel()
        {
            var lo = new Rng(7); var hi = new Rng(7);
            int legendsLo = 0, legendsHi = 0, commonLo = 0, commonHi = 0;
            for (int i = 0; i < 400; i++)
            {
                var a = MatchSim.RollAttacker(lo, 1); var b = MatchSim.RollAttacker(hi, 9);
                if (a.Rarity == Rarity.Legend) legendsLo++; if (b.Rarity == Rarity.Legend) legendsHi++;
                if (a.Rarity == Rarity.Common) commonLo++; if (b.Rarity == Rarity.Common) commonHi++;
            }
            Assert.AreEqual(0, legendsLo, "레벨 1 엔 전설이 없다");
            Assert.Greater(legendsHi, 50);
            Assert.Greater(commonLo, commonHi);
            Assert.AreEqual(4, System.Array.FindAll(WaveCatalog.Attackers, a => a.Rarity == Rarity.Legend).Length);
            for (int lv = 1; lv <= 9; lv++) { var (c, r, h, l) = WaveCatalog.DrawOdds(lv); Assert.AreEqual(100, c + r + h + l, "레벨 " + lv); }
        }

        [Test]
        public void XpComesFromIncomeAndGoldAndLevelsUp()
        {
            var m = new MatchSim(new MatchConfig { FunLayer = false }, 6);
            var p = m.Player(0, 0);
            Assert.AreEqual(1, p.Level);
            for (int i = 0; i < m.Cfg.IncomeIntervalSeconds * m.Cfg.TicksPerSecond; i++) m.Step();
            Assert.AreEqual(2, p.Level, "첫 수입의 자동 경험치 2 로 레벨 2");
            Assert.IsTrue(m.Events.Exists(e => e.Type == MatchEventType.LevelUp));
            int gold = p.Gold;
            m.Step(L(MatchCommand.BuyXp(0, 0)));
            Assert.AreEqual(gold - WaveCatalog.XpBuyCost, p.Gold);
            Assert.AreEqual(3, p.Level, "레벨 2 → 3 은 4 경험치");
            p.Gold = 1000;
            for (int i = 0; i < 60; i++) m.Step(L(MatchCommand.BuyXp(0, 0)));
            Assert.AreEqual(WaveCatalog.MaxLevel, p.Level);
            Assert.AreEqual(0, p.Xp);
            m.Step(L(MatchCommand.BuyXp(0, 0)));
            Assert.IsTrue(m.Events.Exists(e => e.Type == MatchEventType.Rejected), "최대 레벨에선 못 산다");
            Assert.AreEqual(2, m.Player(1, 0).Level, "상대는 자동 경험치만 받아 레벨 2");
        }

        [Test]
        public void SendUpgradesMakeSentUnitsTougherAndCostMore()
        {
            var m = new MatchSim(new MatchConfig { FunLayer = false }, 4);
            var p = m.Player(0, 0);
            Assert.AreEqual(25, MatchSim.UpgradeSendsCost(0)); Assert.AreEqual(40, MatchSim.UpgradeSendsCost(1)); Assert.AreEqual(-1, MatchSim.UpgradeSendsCost(MatchSim.MaxSendLevel));
            p.Gold = 100; int gold = p.Gold;
            m.Step(L(MatchCommand.UpgradeSends(0, 0), MatchCommand.UpgradeSends(0, 0)));
            Assert.AreEqual(2, m.Teams[0].SendLevel);
            Assert.AreEqual(gold - 25 - 40, p.Gold);
            Assert.IsTrue(m.Events.Exists(e => e.Type == MatchEventType.SendsUpgraded));
            p.Hand.Add(1);
            m.Step(L(MatchCommand.Send(0, 0, 0)));
            m.Step();
            var wolf = m.EnemyLane(0).Creeps[0];
            Assert.AreEqual(30 * (100 + 2 * MatchSim.SendLevelHpPercent) / 100, wolf.MaxHp, "레벨 2: 체력 +12%");
            Assert.AreEqual(0, m.Teams[1].SendLevel, "상대 팀은 그대로");
        }
    }
}
