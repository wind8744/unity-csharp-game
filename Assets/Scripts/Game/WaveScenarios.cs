using System.Collections.Generic;
using LaneBattle.Core.Wave;

namespace LaneBattle.Game
{
    /// <summary>1단계 프로토타입용 시작 배치. 골드 없이 "싸우는 모습"만 본다.</summary>
    public static class WaveScenarios
    {
        public sealed class Preset
        {
            public string Name;
            public List<(int towerId, int col, int row)> Towers = new List<(int, int, int)>();
        }

        public static readonly Preset[] Presets =
        {
            new Preset { Name = "빈 라인" },
            new Preset { Name = "궁수 6 (36골드)", Towers = { (1, 0, 0), (1, 1, 0), (1, 2, 0), (1, 3, 0), (1, 1, 1), (1, 2, 1) } },
            new Preset { Name = "꽉 찬 방어", Towers = {
                (2, 0, 0), (8, 1, 0), (4, 2, 0), (2, 3, 0),
                (1, 0, 1), (5, 1, 1), (6, 2, 1), (1, 3, 1),
                (7, 1, 2), (3, 2, 2), (9, 0, 2), (7, 3, 2) } },
            new Preset { Name = "대공 없음", Towers = { (2, 0, 0), (8, 1, 0), (4, 2, 0), (2, 3, 0), (7, 1, 1), (7, 2, 1), (5, 1, 2) } },
        };

        /// <summary>"상대가 보냄" 버튼용 무리.</summary>
        public static readonly (string label, int atkId, int count)[] EnemySends =
        {
            ("늑대 ×3", 1, 3), ("고블린 ×4", 2, 4), ("박쥐 ×3", 3, 3), ("거북 ×2", 4, 2),
            ("오우거 ×2", 5, 2), ("그리핀 ×2", 6, 2), ("암살자 ×2", 7, 2), ("드래곤", 9, 1), ("거대 골렘", 10, 1), ("흑마법사", 11, 1),
        };

        public static LaneSim Build(Preset preset, ulong seed, bool instantBuild = true)
        {
            var sim = new LaneSim(new LaneConfig(), seed);
            foreach (var (id, col, row) in preset.Towers)
            {
                var t = sim.Build(WaveCatalog.Tower(id), col, row);
                if (instantBuild && t != null) t.BuildLeft = 0;
            }
            return sim;
        }
    }
}
