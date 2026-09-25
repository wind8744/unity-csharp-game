using System.Collections.Generic;
using LaneBattle.Core.Wave;

namespace LaneBattle.Game
{
    /// <summary>1단계 프로토타입용 고정 시나리오. 룰 없이 "싸우는 모습"만 본다.</summary>
    public static class WaveScenarios
    {
        public sealed class Scenario
        {
            public string Name;
            public int Round;
            public LaneConfig Lane = new LaneConfig();
            public List<(int defId, int col, int row, bool upgraded)> Defenders = new List<(int, int, int, bool)>();
            public List<(int atkId, int count)> ExtraSends = new List<(int, int)>();
        }

        static List<(int, int, int, bool)> FullDefense() => new List<(int, int, int, bool)>
        {
            (2, 0, 0, false), (8, 1, 0, false), (4, 2, 0, false), (2, 3, 0, false),   // 앞줄: 멧돼지, 골렘, 창병, 멧돼지
            (1, 0, 1, false), (5, 1, 1, false), (6, 2, 1, false), (1, 3, 1, false),   // 중간: 궁수, 술사, 불사조, 궁수
            (7, 1, 2, false), (3, 2, 2, false),                                       // 뒷줄: 포탑, 드루이드
        };

        static List<(int, int, int, bool)> CheapDefense() => new List<(int, int, int, bool)>
        {
            (2, 1, 0, false), (2, 2, 0, false), (1, 1, 1, false), (7, 2, 2, false),
        };

        public static readonly Scenario[] All =
        {
            new Scenario { Name = "3라운드 · 싼 방어", Round = 3, Defenders = CheapDefense() },
            new Scenario { Name = "5라운드 · 꽉 찬 방어", Round = 5, Defenders = FullDefense() },
            new Scenario { Name = "7라운드 + 상대가 오우거 2 보냄", Round = 7, Defenders = FullDefense(), ExtraSends = { (5, 2) } },
            new Scenario { Name = "8라운드 보스 (거대 골렘)", Round = 8, Defenders = FullDefense() },
            new Scenario { Name = "9라운드 · 대공 없는 방어", Round = 9, Defenders = new List<(int, int, int, bool)> { (2, 0, 0, false), (8, 1, 0, false), (4, 2, 0, false), (2, 3, 0, false), (7, 1, 1, false), (7, 2, 1, false), (5, 1, 2, false) } },
            new Scenario { Name = "12라운드 보스 (드래곤)", Round = 12, Defenders = FullDefense() },
        };

        public static WaveSim Build(Scenario sc, ulong seed)
        {
            var sim = new WaveSim(sc.Lane, seed);
            foreach (var (id, col, row, up) in sc.Defenders) sim.AddDefender(WaveCatalog.Defender(id), col, row, 0, up);
            sim.QueueBaseWave(sc.Round);
            foreach (var (id, n) in sc.ExtraSends) for (int i = 0; i < n; i++) sim.QueueAttacker(WaveCatalog.Attacker(id));
            return sim;
        }
    }
}
