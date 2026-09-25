using LaneBattle.Core;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class EngineTests
    {
        static GameEngine NewGame(int playersPerTeam = 2, ulong seed = 7) =>
            new GameEngine(new GameConfig { PlayersPerTeam = playersPerTeam, EnableLog = true }, seed);

        static TurnCommands Cmds(GameEngine e) => new TurnCommands(e.Config.PlayersPerTeam);

        /// <summary>이번 턴의 증강 제안을 비워서 자동 선택이 상태를 바꾸지 않게 한다.</summary>
        static void SkipAugments(GameEngine e)
        {
            foreach (var t in e.State.Teams) foreach (var p in t.Players) p.Offers.Clear();
        }

        static TurnCommands Place(GameEngine e, int team, int player, params (int id, Lane lane)[] placements)
        {
            var c = Cmds(e);
            var pc = new PlayerCommand();
            foreach (var (id, lane) in placements) pc.Placements.Add(new Placement(id, lane));
            c.Set(team, player, pc);
            return c;
        }

        [Test]
        public void SameSeedSameResult()
        {
            var cfg = new GameConfig();
            var agents = new IAgent[] { new GreedyAgent(), new RandomAgent() };
            var a = Simulator.Play(cfg, 12345, agents);
            var b = Simulator.Play(cfg, 12345, agents);
            Assert.AreEqual(a.StateHash, b.StateHash);
            Assert.AreEqual(a.Winner, b.Winner);
            Assert.AreEqual(a.Turns, b.Turns);
        }

        [Test]
        public void DifferentSeedsProduceDifferentGames()
        {
            var cfg = new GameConfig();
            var agents = new IAgent[] { new RandomAgent(), new RandomAgent() };
            var a = Simulator.Play(cfg, 1, agents);
            var b = Simulator.Play(cfg, 2, agents);
            Assert.AreNotEqual(a.StateHash, b.StateHash);
        }

        [Test]
        public void ManaEqualsTurnAndStartHandIsThree()
        {
            var e = NewGame();
            Assert.AreEqual(1, e.State.Turn);
            Assert.AreEqual(1, e.Player(0, 0).Mana);
            Assert.AreEqual(3 + 1, e.Player(0, 0).Hand.Count); // 시작 3장 + 1턴 드로우
            e.ResolveTurn(Cmds(e));
            Assert.AreEqual(2, e.State.Turn);
            Assert.AreEqual(2, e.Player(1, 1).Mana);
        }

        [Test]
        public void CannotPlaceBeyondManaOrLaneCap()
        {
            var e = NewGame();
            e.DebugSetHand(0, 0, 10, 2); // 강철 거인 6코, 멧돼지 2코
            e.DebugSetMana(0, 0, 2);
            Assert.IsFalse(e.CanPlace(0, 0, 10, Lane.Top));
            Assert.IsTrue(e.CanPlace(0, 0, 2, Lane.Top));
            for (int i = 0; i < 4; i++) e.DebugSpawn(0, 0, 7, Lane.Mid);
            Assert.IsFalse(e.CanPlace(0, 0, 2, Lane.Mid));
        }

        [Test]
        public void CombatDamageOverflowsToNextUnit()
        {
            var e = NewGame();
            // 팀0 미드: 멧돼지 3/2 두 마리 = 6 피해. 팀1 미드: 태엽 2/3, 태엽 2/3 → 첫 유닛 3, 이월 3 → 둘 다 죽음
            e.DebugSpawn(0, 0, 2, Lane.Mid); e.DebugSpawn(0, 0, 2, Lane.Mid);
            e.DebugSpawn(1, 0, 7, Lane.Mid); e.DebugSpawn(1, 0, 7, Lane.Mid);
            // 단, 태엽 병사 둘은 전사 시너지로 체력 5 → 첫 유닛 5, 이월 1 → 두 번째는 피해 1 로 생존
            e.ResolveTurn(Cmds(e));
            Assert.AreEqual(1, e.LaneUnits(1, Lane.Mid).Count);
            Assert.AreEqual(1, e.LaneUnits(1, Lane.Mid)[0].Damage);
            // 팀1 은 4 피해를 팀0 첫 멧돼지(체력 2+2 전사시너지)에 줌 → 죽음, 이월 0
            Assert.AreEqual(1, e.LaneUnits(0, Lane.Mid).Count);
            Assert.AreEqual(15, e.Team(1).TowerHp[(int)Lane.Mid]); // 상대 유닛이 있었으므로 타워 무피해
        }

        [Test]
        public void EmptyLaneHitsTowerAndGuardReduces()
        {
            var e = NewGame();
            e.DebugSpawn(0, 0, 2, Lane.Top);           // 3 공격
            e.DebugSpawn(1, 0, 3, Lane.Bot);           // 나무 수호자는 봇에, 탑은 비어 있음
            e.ResolveTurn(Cmds(e));
            Assert.AreEqual(15 - 3, e.Team(1).TowerHp[(int)Lane.Top]);
            // 수호자 2/7 는 봇에서 팀0 유닛이 없으니 타워를 2 때림
            Assert.AreEqual(15 - 2, e.Team(0).TowerHp[(int)Lane.Bot]);

            var g = NewGame();
            g.DebugSpawn(0, 0, 2, Lane.Top);           // 3 공격
            g.DebugSpawn(1, 0, 3, Lane.Top);           // 수호자가 같은 라인: 유닛이 있으니 체인 공격, 타워 무피해
            g.ResolveTurn(Cmds(g));
            Assert.AreEqual(15, g.Team(1).TowerHp[(int)Lane.Top]);
        }

        [Test]
        public void TowerSniperIgnoresUnitsAndGuardReducesHit()
        {
            var e = NewGame();
            e.DebugSpawn(0, 0, 5, Lane.Mid);   // 화염 궁수 3 → 항상 타워
            e.DebugSpawn(1, 0, 3, Lane.Mid);   // 수호자: 타워 피해 -1
            e.ResolveTurn(Cmds(e));
            Assert.AreEqual(15 - 2, e.Team(1).TowerHp[(int)Lane.Mid]);
        }

        [Test]
        public void TwoTowersDestroyedEndsGame()
        {
            var e = NewGame();
            e.Team(1).TowerHp[0] = 1; e.Team(1).TowerHp[1] = 1;
            e.DebugSpawn(0, 0, 2, Lane.Top);
            e.DebugSpawn(0, 0, 2, Lane.Mid);
            e.ResolveTurn(Cmds(e));
            Assert.IsTrue(e.State.IsOver);
            Assert.AreEqual(0, e.State.Winner);
            Assert.AreEqual("타워 2개 파괴", e.State.EndReason);
        }

        [Test]
        public void HeraldLaneIsInstantWin()
        {
            var e = NewGame();
            e.DebugSetLaneRule(Lane.Bot, LaneRuleId.Herald);
            e.Team(0).TowerHp[(int)Lane.Bot] = 2;
            e.DebugSpawn(1, 0, 2, Lane.Bot);
            e.ResolveTurn(Cmds(e));
            Assert.IsTrue(e.State.IsOver);
            Assert.AreEqual(1, e.State.Winner);
        }

        [Test]
        public void HighGroundCapsTowerDamagePerTurn()
        {
            var e = NewGame();
            e.DebugSetLaneRule(Lane.Top, LaneRuleId.HighGround);
            e.DebugSpawn(0, 0, 10, Lane.Top); e.DebugSpawn(0, 0, 2, Lane.Top); // 6 + 3 = 9
            e.ResolveTurn(Cmds(e));
            Assert.AreEqual(15 - 5, e.Team(1).TowerHp[(int)Lane.Top]);
        }

        [Test]
        public void FireSynergyCountsAcrossLanesAndTeammates()
        {
            var e = NewGame();
            e.DebugSpawn(0, 0, 4, Lane.Top);
            e.DebugSpawn(0, 1, 4, Lane.Mid);
            var archer = e.DebugSpawn(0, 1, 5, Lane.Bot);
            Assert.IsTrue(e.HasTribe(0, Tribe.Fire, 3));
            Assert.AreEqual(3 + 1, e.EffectiveAtk(archer));
        }

        [Test]
        public void BondLowersTribeThreshold()
        {
            var e = NewGame();
            e.DebugGrantAugment(0, 0, AugmentId.Bond);
            e.DebugSpawn(0, 0, 4, Lane.Top);
            e.DebugSpawn(0, 1, 4, Lane.Mid);
            Assert.IsTrue(e.HasTribe(0, Tribe.Fire, 3));
        }

        [Test]
        public void ForestThreeGivesTowerBonusOnce()
        {
            var e = NewGame();
            e.DebugSpawn(0, 0, 1, Lane.Top); e.DebugSpawn(0, 0, 2, Lane.Top); e.DebugSpawn(0, 1, 3, Lane.Mid);
            Assert.AreEqual(18, e.Team(0).TowerHp[0]);
            e.DebugSpawn(0, 1, 2, Lane.Bot);
            Assert.AreEqual(18, e.Team(0).TowerHp[0]);
        }

        [Test]
        public void WarriorPairInLaneGetsHp()
        {
            var e = NewGame();
            var a = e.DebugSpawn(0, 0, 2, Lane.Top);
            Assert.AreEqual(2, e.Hp(a));
            e.DebugSpawn(0, 1, 7, Lane.Top);
            Assert.AreEqual(4, e.Hp(a));
        }

        [Test]
        public void CanyonLimitsLaneToTwo()
        {
            var e = NewGame();
            e.DebugSetLaneRule(Lane.Mid, LaneRuleId.Canyon);
            e.DebugSpawn(0, 0, 7, Lane.Mid); e.DebugSpawn(0, 0, 7, Lane.Mid);
            e.DebugSetHand(0, 0, 7); e.DebugSetMana(0, 0, 9);
            Assert.IsFalse(e.CanPlace(0, 0, 7, Lane.Mid));
            Assert.IsTrue(e.CanPlace(0, 0, 7, Lane.Top));
        }

        [Test]
        public void SwampUnitsDoNotAttackOnPlacementTurn()
        {
            var e = NewGame();
            e.DebugSetLaneRule(Lane.Top, LaneRuleId.Swamp);
            e.DebugSetHand(0, 0, 2); e.DebugSetMana(0, 0, 2);
            e.ResolveTurn(Place(e, 0, 0, (2, Lane.Top)));
            Assert.AreEqual(15, e.Team(1).TowerHp[0]);
            SkipAugments(e);        // 2턴 증강(과부하 등)이 타워 체력을 건드리지 않도록
            e.ResolveTurn(Cmds(e)); // 다음 턴엔 공격
            Assert.AreEqual(12, e.Team(1).TowerHp[0]);
        }

        [Test]
        public void FogHidesUnitsUntilNextTurn()
        {
            var e = NewGame();
            e.DebugSetLaneRule(Lane.Bot, LaneRuleId.Fog);
            e.DebugSpawn(1, 0, 2, Lane.Bot); // 상대 3공
            e.DebugSetHand(0, 0, 7); e.DebugSetMana(0, 0, 2);
            e.ResolveTurn(Place(e, 0, 0, (7, Lane.Bot)));
            var mine = e.LaneUnits(0, Lane.Bot)[0];
            Assert.AreEqual(0, mine.Damage);                 // 숨어 있어 안 맞음
            Assert.AreEqual(15 - 3, e.Team(0).TowerHp[2]);   // 상대는 타워를 침
            Assert.IsFalse(mine.Hidden);                     // 다음 턴 시작 시 공개
        }

        [Test]
        public void GiantDiscountAppliesWithTwoMachinesInLane()
        {
            var e = NewGame();
            Assert.AreEqual(6, e.CostOf(0, 10, Lane.Top));
            e.DebugSpawn(0, 0, 7, Lane.Top); e.DebugSpawn(0, 1, 9, Lane.Top);
            Assert.AreEqual(4, e.CostOf(0, 10, Lane.Top));
            Assert.AreEqual(6, e.CostOf(0, 10, Lane.Mid));
        }

        [Test]
        public void ImpStingsFrontEnemyOnDeath()
        {
            var e = NewGame();
            e.DebugSpawn(0, 0, 4, Lane.Mid);           // 임프 2/1
            var tank = e.DebugSpawn(1, 0, 3, Lane.Mid); // 수호자 2/7: 임프를 죽이고, 임프 2 + 침 1 = 3 피해
            e.ResolveTurn(Cmds(e));
            Assert.AreEqual(0, e.LaneUnits(0, Lane.Mid).Count);
            Assert.AreEqual(3, tank.Damage);
        }

        [Test]
        public void BalanceMissionGrantsManaNextTurn()
        {
            var e = NewGame(seed: 3);
            e.Player(0, 0).Mission = MissionId.Balance;
            e.DebugSetHand(0, 0, 1, 4, 1); e.DebugSetMana(0, 0, 3);
            e.ResolveTurn(Place(e, 0, 0, (1, Lane.Top), (4, Lane.Mid), (1, Lane.Bot)));
            Assert.IsTrue(e.Player(0, 0).MissionDone);
            Assert.AreEqual(2 + 3, e.Player(0, 0).Mana);
        }

        [Test]
        public void HandNeverExceedsMax()
        {
            var e = NewGame();
            var ps = e.Player(0, 0);
            e.DebugSetHand(0, 0, 1, 1, 1, 1, 1, 1, 1);
            e.Draw(ps, 3);
            Assert.AreEqual(7, ps.Hand.Count);
        }

        [Test]
        public void GameAlwaysTerminates()
        {
            var cfg = new GameConfig();
            for (ulong s = 1; s <= 50; s++)
            {
                var r = Simulator.Play(cfg, s, new IAgent[] { new RandomAgent(), new GreedyAgent() });
                Assert.LessOrEqual(r.Turns, 7);
                Assert.IsTrue(r.Winner >= -1 && r.Winner <= 1);
            }
        }

        [Test]
        public void SoloModeWorks()
        {
            var r = Simulator.Play(new GameConfig { PlayersPerTeam = 1 }, 99, new IAgent[] { new GreedyAgent(), new GreedyAgent() });
            Assert.LessOrEqual(r.Turns, 7);
        }
    }
}
