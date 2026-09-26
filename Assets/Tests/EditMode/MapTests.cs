using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class MapTests
    {
        [Test]
        public void AsciiMapParsesPathFromStartToBase()
        {
            var m = MapDef.FromAscii("t", new[]
            {
                "....",
                "S##.",
                "..#.",
                "..#B",
            });
            Assert.AreEqual(4, m.W); Assert.AreEqual(4, m.H);
            Assert.AreEqual((0, 2), m.Start); Assert.AreEqual((3, 0), m.End);
            Assert.AreEqual(6, m.Path.Count);
            Assert.AreEqual(5000, m.LengthMilli);
            Assert.IsTrue(m.IsSlot(0, 3)); Assert.IsFalse(m.IsSlot(1, 2)); Assert.IsFalse(m.IsSlot(9, 9));
            Assert.AreEqual((500, 2500), m.PosAt(0));
            Assert.AreEqual((1500, 2500), m.PosAt(1000));
            Assert.AreEqual((2500, 2000), m.PosAt(2500), "모서리를 지나 아래로");
            Assert.AreEqual((3500, 500), m.PosAt(99999));
        }

        [Test]
        public void SnakeMapsAreValidAndRoundTrip()
        {
            foreach (var m in new[] { MapCatalog.OneVsOne, MapCatalog.TwoVsTwo, MapCatalog.ThreeVsThree })
            {
                Assert.Greater(m.LengthCells, 30, m.Name);
                Assert.Greater(m.SlotCount, 60, m.Name);
                var again = MapDef.FromAscii(m.Name, m.ToAscii());
                Assert.AreEqual(m.Path.Count, again.Path.Count);
                Assert.AreEqual(m.SlotCount, again.SlotCount);
                // 경로는 4방향으로 이어진다
                for (int i = 1; i < m.Path.Count; i++)
                    Assert.AreEqual(1, System.Math.Abs(m.Path[i].x - m.Path[i - 1].x) + System.Math.Abs(m.Path[i].y - m.Path[i - 1].y), m.Name);
            }
            Assert.AreEqual(41, MapCatalog.OneVsOne.LengthCells);
        }

        [Test]
        public void InnerCornersCoverMorePathThanEdges()
        {
            var m = MapCatalog.OneVsOne;
            // 첫 줄(y=7)과 둘째 줄(y=4) 사이 오른쪽 안쪽 구석 (11,5) 은 세 구간을 본다; 왼쪽 위 (0,8) 은 한 구간만
            Assert.IsTrue(m.IsSlot(11, 5)); Assert.IsTrue(m.IsPath[12, 5], "x=12 는 잇는 세로 경로");
            Assert.Greater(m.Coverage[11, 5], m.Coverage[0, 8]);
            Assert.Greater(m.Coverage[11, 5], 8);
        }

        [Test]
        public void CreepsFollowTheWindingPathAndLeakAtTheEnd()
        {
            var s = new LaneSim(new LaneConfig { AutoWaves = false }, 1);
            var c = s.Send(WaveCatalog.Attacker(1));
            s.Step();
            Assert.AreEqual(c.SpeedPerTick, c.Dist, "출발 틱에 한 걸음");
            var start = MapDef.Center(s.Map.Start);
            Assert.AreEqual(start.X + c.SpeedPerTick, c.X); Assert.AreEqual(start.Y, c.Y);
            int maxY = c.Y, minY = c.Y;
            while (c.Alive) { s.Step(); maxY = System.Math.Max(maxY, c.Y); minY = System.Math.Min(minY, c.Y); }
            Assert.AreEqual(1, s.Leaked);
            Assert.Greater(maxY - minY, 5000, "여러 줄을 지나 내려온다");
        }

        [Test]
        public void TowersOnlyBuildOnSlotsAndBotPrefersCorners()
        {
            var sim = new MatchSim(new MatchConfig(), 3);
            var lane = sim.OwnLane(0);
            var path = lane.Map.Path[5];
            Assert.IsNull(lane.Build(WaveCatalog.Tower(1), path.x, path.y), "경로 위엔 못 짓는다");
            Assert.IsNull(lane.Build(WaveCatalog.Tower(1), 99, 0));
            var bot = new SimpleBot();
            var cmds = new System.Collections.Generic.List<MatchCommand>();
            bot.Decide(sim, 0, 0, cmds);
            Assert.AreEqual(1, cmds.Count); Assert.AreEqual(CommandType.Build, cmds[0].Type);
            Assert.GreaterOrEqual(lane.Map.Coverage[cmds[0].B, cmds[0].C], 8, "봇은 경로를 많이 보는 자리부터");
        }
    }
}
