using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class LaneSimTests
    {
        static LaneSim Manual(ulong seed = 1) => new LaneSim(new LaneConfig { AutoWaves = false, Map = MapDef.Straight(20) }, seed);
        /// <summary>직선 테스트 맵: 경로는 y=1, 자리는 y=0·2. (c, r) → 경로 중간쯤 x = 8 + c, 위/아래.</summary>
        static Tower B(LaneSim s, int towerId, int c, int r) => s.Build(WaveCatalog.Tower(towerId), 8 + c, r == 1 ? 2 : 0);
        static void Instant(LaneSim s) { foreach (var t in s.Towers) t.BuildLeft = 0; }

        [Test]
        public void SameSeedSameResult()
        {
            string Run()
            {
                var s = Manual(42);
                B(s, 1, 0, 0); B(s, 5, 1, 1); B(s, 7, 2, 2);
                s.SpawnBaseWave(4); s.Send(WaveCatalog.Attacker(6)); s.Send(WaveCatalog.Attacker(7));
                s.RunUntilQuiet();
                return s.Hash();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void EmptyLaneLeaksEverythingIntoBase()
        {
            var s = Manual();
            s.SpawnBaseWave(0); // 늑대 4, 누수 1씩
            s.RunUntilQuiet();
            Assert.AreEqual(4, s.Leaked);
            Assert.AreEqual(s.Cfg.BaseHp - 4, s.BaseHp);
            Assert.AreEqual(0, s.Kills);
        }

        [Test]
        public void TowersAreNeverDamagedAndCreepsNeverStop()
        {
            var s = Manual(3);
            for (int c = 0; c < 4; c++) B(s, 2, c, 0); // 가시 덤불 앞줄 전부
            Instant(s);
            for (int i = 0; i < 3; i++) s.Send(WaveCatalog.Attacker(10)); // 거대 골렘 (감속 면역)
            s.RunUntilQuiet();
            Assert.AreEqual(4, s.Towers.Count);
            foreach (var t in s.Towers) Assert.IsTrue(t.Alive);
            Assert.AreEqual(3 * 4, s.Leaked); // 골렘은 안 죽고 다 샌다 (누수 4)
        }

        [Test]
        public void FlyersIgnoreNonAntiAir()
        {
            var s = Manual(7);
            for (int c = 0; c < 4; c++) B(s, 7, c, 1); // 포탑, 대공 불가
            Instant(s);
            for (int i = 0; i < 3; i++) s.Send(WaveCatalog.Attacker(3));
            s.RunUntilQuiet();
            Assert.AreEqual(3, s.Leaked); Assert.AreEqual(0, s.Kills);

            var t = Manual(7);
            for (int c = 0; c < 4; c++) B(t, 1, c, 1); // 나무 궁수, 대공
            Instant(t);
            for (int i = 0; i < 3; i++) t.Send(WaveCatalog.Attacker(3));
            t.RunUntilQuiet();
            Assert.GreaterOrEqual(t.Kills, 2);
        }

        [Test]
        public void BuildingTakesTime()
        {
            var s = Manual(5);
            var tower = B(s, 7, 1, 0);
            Assert.AreEqual(s.Cfg.BuildSeconds * s.Cfg.TicksPerSecond, tower.BuildLeft);
            s.Send(WaveCatalog.Attacker(4));
            int firedAt = -1;
            for (int i = 0; i < 400 && firedAt < 0; i++)
            {
                s.Step();
                foreach (var e in s.Events) if (e.Type == SimEventType.Attack) firedAt = s.Tick;
            }
            Assert.Greater(firedAt, s.Cfg.BuildSeconds * s.Cfg.TicksPerSecond);
        }

        [Test]
        public void StealthIsUntargetableAtFirst()
        {
            var s = new LaneSim(new LaneConfig { AutoWaves = false, Map = MapDef.Straight(20) }, 9);
            for (int c = 0; c < 4; c++) s.Build(WaveCatalog.Tower(7), 1 + c, 0); // 타워를 입구 바로 옆에
            Instant(s);
            var a = s.Send(WaveCatalog.Attacker(7)); // 암살자 3초 은신
            for (int i = 0; i < 40; i++) s.Step(); // 2초
            Assert.AreEqual(a.MaxHp, a.Hp);
            for (int i = 0; i < 40; i++) s.Step();
            Assert.Less(a.Hp, a.MaxHp);
        }

        [Test]
        public void SilenceStopsTowerFiring()
        {
            var s = Manual(11);
            var t = B(s, 7, 1, 0);
            Instant(s);
            t.SilenceLeft = 100;
            s.Send(WaveCatalog.Attacker(4));
            int attacks = 0;
            for (int i = 0; i < 90; i++) { s.Step(); foreach (var e in s.Events) if (e.Type == SimEventType.Attack) attacks++; }
            Assert.AreEqual(0, attacks);
        }

        [Test]
        public void AutoWavesFollowTimetable()
        {
            var s = new LaneSim(new LaneConfig { FirstWaveSeconds = 2, WaveIntervalSeconds = 2 }, 1);
            int starts = 0;
            for (int i = 0; i < 20 * 7; i++) { s.Step(); foreach (var e in s.Events) if (e.Type == SimEventType.WaveStart) starts++; }
            Assert.AreEqual(3, starts); // 2초, 4초, 6초
        }

        [Test]
        public void BaseHpZeroEndsMatch()
        {
            var s = new LaneSim(new LaneConfig { AutoWaves = false, BaseHp = 2 }, 1);
            s.Send(WaveCatalog.Attacker(1)); s.Send(WaveCatalog.Attacker(1)); s.Send(WaveCatalog.Attacker(1));
            s.RunUntilQuiet();
            Assert.IsTrue(s.IsOver);
            Assert.AreEqual(0, s.BaseHp);
        }

        [Test]
        public void SplashHitsGroup()
        {
            var s = Manual(13);
            for (int c = 0; c < 4; c++) B(s, 5, c, 1);
            Instant(s);
            for (int i = 0; i < 8; i++) s.Send(WaveCatalog.Attacker(1));
            // 기능 검증: 한 번의 공격(Attack)이 여러 유닛에 피해(Damage)를 준 적이 있어야 한다
            int bestSpread = 0;
            while (!s.IsOver && s.CreepsAlive() > 0)
            {
                s.Step();
                int attacks = 0, damages = 0;
                foreach (var e in s.Events) { if (e.Type == SimEventType.Attack) attacks++; if (e.Type == SimEventType.Damage && e.A > 0) damages++; }
                if (attacks == 1) bestSpread = System.Math.Max(bestSpread, damages);
            }
            Assert.GreaterOrEqual(bestSpread, 2);
        }

        [Test]
        public void AllBaseWavesTerminate()
        {
            for (int w = 0; w < WaveCatalog.BaseWaves.Length; w++)
            {
                var s = Manual((ulong)w + 100);
                B(s, 1, 0, 0); B(s, 6, 1, 0); B(s, 8, 2, 0); B(s, 1, 3, 0);
                B(s, 5, 1, 1); B(s, 7, 2, 1); B(s, 3, 1, 2);
                Instant(s);
                s.SpawnBaseWave(w);
                s.RunUntilQuiet();
                Assert.AreEqual(0, s.CreepsAlive(), $"wave {w + 1}");
            }
        }
    }
}
