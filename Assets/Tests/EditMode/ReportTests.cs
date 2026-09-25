using LaneBattle.Core;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class ReportTests
    {
        [Test]
        public void ReportRecordsPlacementsCombatAndDeaths()
        {
            var e = new GameEngine(new GameConfig { PlayersPerTeam = 1 }, 5);
            e.DebugSpawn(1, 0, 7, Lane.Mid);              // 상대 태엽 2/3 이미 배치
            e.DebugSetHand(0, 0, 2, 2); e.DebugSetMana(0, 0, 4);
            var c = new TurnCommands(1);
            var pc = new PlayerCommand();
            pc.Placements.Add(new Placement(2, Lane.Mid)); pc.Placements.Add(new Placement(2, Lane.Mid));
            c.Set(0, 0, pc);
            e.ResolveTurn(c);
            var r = e.LastReport;
            Assert.AreEqual(1, r.Turn);
            Assert.AreEqual(2, r.PlacedCount(0, Lane.Mid));
            Assert.AreEqual(3, r.Combat[(int)Lane.Mid].ChainDamage[0]);   // 태엽 체력 3 만큼만 들어감
            Assert.AreEqual(0, r.Combat[(int)Lane.Mid].TowerDamage[0]);   // 이월은 타워로 안 감
            int deaths = 0; foreach (var d in r.DeathsIn(1, Lane.Mid, "combat")) deaths++;
            Assert.AreEqual(1, deaths);
            Assert.IsFalse(r.GameEnded);
        }

        [Test]
        public void StyleAgentsPlayFullGames()
        {
            foreach (AiStyle style in System.Enum.GetValues(typeof(AiStyle)))
            {
                var r = Simulator.Play(new GameConfig { PlayersPerTeam = 1 }, 11, new IAgent[] { new GreedyAgent(), new StyleAgent(style) });
                Assert.LessOrEqual(r.Turns, 7);
            }
        }
    }
}
