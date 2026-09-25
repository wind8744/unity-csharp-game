using System.Collections.Generic;

namespace LaneBattle.Core
{
    public sealed class UnitDef
    {
        public int Id;
        public string Name;
        public int Cost;
        public int Atk;
        public int Hp;
        public Tribe Tribe;
        public Job Job;
        public Ability Ability;

        public UnitDef(int id, string name, int cost, int atk, int hp, Tribe tribe, Job job, Ability ability)
        {
            Id = id; Name = name; Cost = cost; Atk = atk; Hp = hp; Tribe = tribe; Job = job; Ability = ability;
        }
    }

    /// <summary>v0.1 프로토타입 데이터. 밸런싱이 시작되면 ScriptableObject/JSON 으로 옮긴다.</summary>
    public static class Catalog
    {
        public static readonly UnitDef[] Units =
        {
            new UnitDef(1,  "다람쥐 정찰병", 1, 1, 1, Tribe.Forest,  Job.Archer,  Ability.DrawOnPlace),
            new UnitDef(2,  "멧돼지 돌격병", 2, 3, 2, Tribe.Forest,  Job.Warrior, Ability.None),
            new UnitDef(3,  "나무 수호자",   4, 2, 7, Tribe.Forest,  Job.Warrior, Ability.TowerGuard),
            new UnitDef(4,  "불꽃 임프",     1, 2, 1, Tribe.Fire,    Job.Mage,    Ability.ImpDeathSting),
            new UnitDef(5,  "화염 궁수",     3, 3, 2, Tribe.Fire,    Job.Archer,  Ability.TowerSniper),
            new UnitDef(6,  "불의 정령",     5, 5, 4, Tribe.Fire,    Job.Mage,    Ability.SpiritBurst),
            new UnitDef(7,  "태엽 병사",     2, 2, 3, Tribe.Machine, Job.Warrior, Ability.None),
            new UnitDef(8,  "톱니 궁수",     3, 2, 4, Tribe.Machine, Job.Archer,  Ability.Ramp),
            new UnitDef(9,  "수리 드론",     2, 0, 3, Tribe.Machine, Job.Mage,    Ability.RepairDrone),
            new UnitDef(10, "강철 거인",     6, 6, 6, Tribe.Machine, Job.Warrior, Ability.GiantDiscount),
        };

        static readonly Dictionary<int, UnitDef> ById = Build();

        static Dictionary<int, UnitDef> Build()
        {
            var d = new Dictionary<int, UnitDef>();
            foreach (var u in Units) d[u.Id] = u;
            return d;
        }

        public static UnitDef Unit(int id) => ById[id];

        public static readonly AugmentId[] Augments =
        {
            AugmentId.Abundance, AugmentId.Overload, AugmentId.TopKeeper, AugmentId.Retreat, AugmentId.Twins,
            AugmentId.Scout, AugmentId.Gambler, AugmentId.Bond, AugmentId.MindGames, AugmentId.Ambush,
        };

        public static readonly LaneRuleId[] LaneRules =
        {
            LaneRuleId.Swamp, LaneRuleId.Canyon, LaneRuleId.Jungle, LaneRuleId.DragonNest,
            LaneRuleId.Fog, LaneRuleId.HighGround, LaneRuleId.Herald,
        };

        public static readonly MissionId[] Missions =
        {
            MissionId.MidFocus, MissionId.Disguise, MissionId.Balance,
            MissionId.Restraint, MissionId.Purebred, MissionId.Attrition,
        };

        /// <summary>기본 덱: 10종 × 2장.</summary>
        public static List<int> DefaultDeck()
        {
            var deck = new List<int>(Units.Length * 2);
            foreach (var u in Units) { deck.Add(u.Id); deck.Add(u.Id); }
            return deck;
        }
    }
}
