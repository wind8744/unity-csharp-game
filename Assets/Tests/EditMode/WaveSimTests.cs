using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class WaveSimTests
    {
        static WaveSim Sim(ulong seed = 1) => new WaveSim(new LaneConfig(), seed);

        [Test]
        public void SameSeedSameWave()
        {
            string Run()
            {
                var s = Sim(42);
                s.AddDefender(WaveCatalog.Defender(2), 0, 0); s.AddDefender(WaveCatalog.Defender(1), 1, 1); s.AddDefender(WaveCatalog.Defender(7), 2, 2);
                s.QueueBaseWave(5);
                s.RunToEnd();
                return s.Hash();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void EmptyDefenseLeaksEverything()
        {
            var s = Sim();
            s.QueueBaseWave(1); // 늑대 4, 누수 1씩
            s.RunToEnd();
            Assert.AreEqual(4, s.Leaked);
            Assert.AreEqual(0, s.Kills);
            Assert.Less(s.Tick, s.Cfg.MaxSeconds * s.Cfg.TicksPerSecond);
        }

        [Test]
        public void MeleeDefenderBlocksItsColumnOnly()
        {
            var cfg = new LaneConfig { Width = 2 };
            var s = new WaveSim(cfg, 3);
            s.AddDefender(WaveCatalog.Defender(8), 0, 0); // 골렘, 0열 앞줄
            // 거북을 두 열에 하나씩 강제 배치하려면 시드에 의존하므로, 큐를 여러 개 넣고 열 분포를 본다
            for (int i = 0; i < 6; i++) s.QueueAttacker(WaveCatalog.Attacker(4));
            s.RunToEnd();
            // 골렘 열의 거북은 막혀서 골렘을 때리다 죽고, 다른 열은 샌다
            Assert.Greater(s.Kills, 0);
            Assert.Greater(s.Leaked, 0);
            Assert.AreEqual(6, s.Kills + s.Leaked);
        }

        [Test]
        public void FlyersIgnoreNonAntiAir()
        {
            var s = Sim(7);
            for (int c = 0; c < 4; c++) s.AddDefender(WaveCatalog.Defender(7), c, 0); // 포탑 4개, 대공 불가
            for (int i = 0; i < 3; i++) s.QueueAttacker(WaveCatalog.Attacker(3)); // 박쥐
            s.RunToEnd();
            Assert.AreEqual(3, s.Leaked);
            Assert.AreEqual(0, s.Kills);

            var t = Sim(7);
            for (int c = 0; c < 4; c++) t.AddDefender(WaveCatalog.Defender(1), c, 0); // 나무 궁수, 대공 가능
            for (int i = 0; i < 3; i++) t.QueueAttacker(WaveCatalog.Attacker(3));
            t.RunToEnd();
            Assert.GreaterOrEqual(t.Kills, 2); // 박쥐는 빨라서 하나쯤 샐 수 있다
            Assert.AreEqual(3, t.Kills + t.Leaked);
        }

        [Test]
        public void TimeoutLeaksHalf()
        {
            var cfg = new LaneConfig { MaxSeconds = 2 };
            var s = new WaveSim(cfg, 1);
            s.QueueAttacker(WaveCatalog.Attacker(10)); // 거대 골렘 속도 0.6칸/초 → 2초 안에 못 옴, 누수 3 → 절반 올림 2
            s.RunToEnd();
            Assert.IsTrue(s.IsOver);
            Assert.AreEqual(2, s.Leaked);
        }

        [Test]
        public void SplashHitsGroup()
        {
            var s = Sim(11);
            for (int c = 0; c < 4; c++) s.AddDefender(WaveCatalog.Defender(5), c, 0); // 불꽃 술사 광역
            for (int i = 0; i < 8; i++) s.QueueAttacker(WaveCatalog.Attacker(1));
            s.RunToEnd();
            Assert.AreEqual(8, s.Kills + s.Leaked);
            Assert.GreaterOrEqual(s.Kills, 6);
        }

        [Test]
        public void HealerRestoresDefender()
        {
            var s = Sim(5);
            var tank = s.AddDefender(WaveCatalog.Defender(8), 1, 0);
            s.AddDefender(WaveCatalog.Defender(3), 1, 1); // 드루이드 바로 뒤
            tank.Hp = 50;
            s.QueueAttacker(WaveCatalog.Attacker(4)); // 웨이브가 진행 중이어야 치유가 돈다
            for (int i = 0; i < 40; i++) s.Step();
            Assert.Greater(tank.Hp, 50);
        }

        [Test]
        public void AllBaseWavesTerminate()
        {
            for (int r = 1; r <= 12; r++)
            {
                var s = Sim((ulong)r);
                s.AddDefender(WaveCatalog.Defender(2), 0, 0); s.AddDefender(WaveCatalog.Defender(2), 3, 0);
                s.AddDefender(WaveCatalog.Defender(1), 1, 1); s.AddDefender(WaveCatalog.Defender(6), 2, 1);
                s.AddDefender(WaveCatalog.Defender(7), 1, 2);
                s.QueueBaseWave(r);
                s.RunToEnd();
                Assert.IsTrue(s.IsOver, $"round {r}");
            }
        }
    }
}
