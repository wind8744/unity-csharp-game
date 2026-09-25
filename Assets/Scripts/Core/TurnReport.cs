using System.Collections.Generic;

namespace LaneBattle.Core
{
    /// <summary>한 턴 동안 일어난 일을 UI 가 단계별로 보여줄 수 있게 구조화한 기록.</summary>
    public sealed class TurnReport
    {
        public int Turn;

        public sealed class PlacementRecord { public int Team; public int Player; public string UnitName; public int UnitDefId; public Lane Lane; }
        public sealed class DeathRecord { public int Team; public string UnitName; public Lane Lane; public string Phase; } // "place" | "combat" | "end"
        public sealed class LaneCombat
        {
            public Lane Lane;
            public int[] ChainDamage = new int[2];   // 각 팀이 상대 유닛 줄에 넣은 피해
            public int[] TowerDamage = new int[2];   // 각 팀이 상대 타워에 넣은 피해 (규칙 적용 후)
            public int[] TowerHpAfter = new int[2];  // 각 팀 타워의 전투 후 체력
            public bool[] TowerDestroyed = new bool[2];
        }

        public List<PlacementRecord> Placements = new List<PlacementRecord>();
        public List<DeathRecord> Deaths = new List<DeathRecord>();
        public LaneCombat[] Combat = { new LaneCombat { Lane = Lane.Top }, new LaneCombat { Lane = Lane.Mid }, new LaneCombat { Lane = Lane.Bot } };
        public List<(int team, int player, AugmentId augment)> AugmentsPicked = new List<(int, int, AugmentId)>();
        public List<(int team, int player, MissionId mission)> MissionsCompleted = new List<(int, int, MissionId)>();
        public LaneRuleId? RuleRevealed;
        public Lane RuleLane;
        public bool GameEnded;
        public int Winner = -1;
        public string EndReason = "";

        public int PlacedCount(int team, Lane lane)
        {
            int n = 0;
            foreach (var p in Placements) if (p.Team == team && p.Lane == lane) n++;
            return n;
        }

        public IEnumerable<DeathRecord> DeathsIn(int team, Lane lane, string phase = null)
        {
            foreach (var d in Deaths) if (d.Team == team && d.Lane == lane && (phase == null || d.Phase == phase)) yield return d;
        }
    }
}
