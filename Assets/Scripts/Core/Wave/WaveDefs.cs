using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    public enum DefTribe { Forest, Fire, Machine }
    public enum DefJob { Warrior, Archer, Mage }
    public enum AtkTribe { Beast, Air, Giant, Dark }
    public enum Rarity { Common, Rare, Hero }

    /// <summary>타워 정의. 숫자는 core-rules-v0.3.md 6절. 타워는 체력이 없다.</summary>
    public sealed class TowerDef
    {
        public int Id; public string Name;
        public int Cost, Atk;
        public int AttacksPer10s;       // 공속 ×10 (1.0/초 = 10)
        public int Range10;             // 사거리 ×10 (1.5칸 = 15)
        public DefTribe Tribe; public DefJob Job;
        public bool AntiAir;
        public int SplashRadius10;      // 광역 반경 ×10
        public int BurnPerSec, BurnSeconds;
        public int SlowPercent, SlowSeconds;
        public int AuraAtkPercent;      // 드루이드: 사거리 안 아군 타워 공격 +%
        public int AuraSpeedPercentMachine; // 드론: 사거리 안 기계 타워 공속 +%
        public int AirMultiplier = 1, GiantMultiplier = 1;
        public bool IsSupport => Atk == 0;
    }

    /// <summary>공격 유닛 정의. 숫자는 core-rules-v0.3.md 7절. 공격력이 없다.</summary>
    public sealed class AttackerDef
    {
        public int Id; public string Name;
        public Rarity Rarity; public AtkTribe Tribe;
        public int SendCost, Hp;
        public int SpeedMilliPerSec;    // 칸/초 ×1000
        public int Leak, Income;
        public bool Flying;
        public int StealthSeconds;      // 암살자
        public int HealPerSec, HealRange10; // 어둠 사제
        public int SpawnSilenceRange10, SpawnSilenceTenths; // 드래곤: 등장 시 침묵 (초 ×10)
        public int PeriodicSilenceEverySec, PeriodicSilenceTenths; // 흑마법사
        public bool SlowImmune;         // 거대 골렘
    }

    public static class WaveCatalog
    {
        public static readonly TowerDef[] Towers =
        {
            new TowerDef { Id = 1, Name = "나무 궁수",    Cost = 6,  Atk = 4,  AttacksPer10s = 10, Range10 = 30, Tribe = DefTribe.Forest,  Job = DefJob.Archer,  AntiAir = true },
            new TowerDef { Id = 2, Name = "가시 덤불",    Cost = 6,  Atk = 3,  AttacksPer10s = 12, Range10 = 15, Tribe = DefTribe.Forest,  Job = DefJob.Warrior, SlowPercent = 30, SlowSeconds = 2 },
            new TowerDef { Id = 3, Name = "숲의 드루이드", Cost = 10, Atk = 0,  AttacksPer10s = 0,  Range10 = 20, Tribe = DefTribe.Forest,  Job = DefJob.Mage,    AuraAtkPercent = 15 },
            new TowerDef { Id = 4, Name = "화염 창탑",    Cost = 7,  Atk = 5,  AttacksPer10s = 10, Range10 = 20, Tribe = DefTribe.Fire,    Job = DefJob.Warrior, BurnPerSec = 2, BurnSeconds = 3 },
            new TowerDef { Id = 5, Name = "불꽃 술사",    Cost = 9,  Atk = 7,  AttacksPer10s = 6,  Range10 = 20, Tribe = DefTribe.Fire,    Job = DefJob.Mage,    SplashRadius10 = 10 },
            new TowerDef { Id = 6, Name = "불사조",      Cost = 14, Atk = 9,  AttacksPer10s = 9,  Range10 = 30, Tribe = DefTribe.Fire,    Job = DefJob.Archer,  AntiAir = true, AirMultiplier = 2 },
            new TowerDef { Id = 7, Name = "태엽 포탑",    Cost = 8,  Atk = 12, AttacksPer10s = 4,  Range10 = 40, Tribe = DefTribe.Machine, Job = DefJob.Archer },
            new TowerDef { Id = 8, Name = "강철 대포",    Cost = 15, Atk = 25, AttacksPer10s = 3,  Range10 = 30, Tribe = DefTribe.Machine, Job = DefJob.Warrior, GiantMultiplier = 2 },
            new TowerDef { Id = 9, Name = "수리 드론",    Cost = 8,  Atk = 0,  AttacksPer10s = 0,  Range10 = 20, Tribe = DefTribe.Machine, Job = DefJob.Mage,    AuraSpeedPercentMachine = 25 },
        };

        public static readonly AttackerDef[] Attackers =
        {
            new AttackerDef { Id = 1,  Name = "늑대",     Rarity = Rarity.Common, Tribe = AtkTribe.Beast, SendCost = 4,  Hp = 30,  SpeedMilliPerSec = 2000, Leak = 1, Income = 1 },
            new AttackerDef { Id = 2,  Name = "고블린",   Rarity = Rarity.Common, Tribe = AtkTribe.Beast, SendCost = 4,  Hp = 45,  SpeedMilliPerSec = 1200, Leak = 1, Income = 2 },
            new AttackerDef { Id = 3,  Name = "박쥐",     Rarity = Rarity.Common, Tribe = AtkTribe.Air,   SendCost = 4,  Hp = 25,  SpeedMilliPerSec = 2200, Leak = 1, Income = 1, Flying = true },
            new AttackerDef { Id = 4,  Name = "거북",     Rarity = Rarity.Common, Tribe = AtkTribe.Giant, SendCost = 5,  Hp = 100, SpeedMilliPerSec = 700,  Leak = 1, Income = 1 },
            new AttackerDef { Id = 5,  Name = "오우거",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Giant, SendCost = 8,  Hp = 180, SpeedMilliPerSec = 1000, Leak = 2, Income = 2 },
            new AttackerDef { Id = 6,  Name = "그리핀",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Air,   SendCost = 8,  Hp = 110, SpeedMilliPerSec = 1600, Leak = 2, Income = 2, Flying = true },
            new AttackerDef { Id = 7,  Name = "암살자",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Dark,  SendCost = 8,  Hp = 60,  SpeedMilliPerSec = 1800, Leak = 2, Income = 1, StealthSeconds = 3 },
            new AttackerDef { Id = 8,  Name = "어둠 사제", Rarity = Rarity.Rare,  Tribe = AtkTribe.Dark,  SendCost = 9,  Hp = 70,  SpeedMilliPerSec = 1000, Leak = 1, Income = 2, HealPerSec = 3, HealRange10 = 20 },
            new AttackerDef { Id = 9,  Name = "드래곤",   Rarity = Rarity.Hero,   Tribe = AtkTribe.Air,   SendCost = 14, Hp = 320, SpeedMilliPerSec = 1300, Leak = 3, Income = 4, Flying = true, SpawnSilenceRange10 = 20, SpawnSilenceTenths = 15 },
            new AttackerDef { Id = 10, Name = "거대 골렘", Rarity = Rarity.Hero,  Tribe = AtkTribe.Giant, SendCost = 14, Hp = 450, SpeedMilliPerSec = 600,  Leak = 4, Income = 3, SlowImmune = true },
            new AttackerDef { Id = 11, Name = "흑마법사", Rarity = Rarity.Hero,   Tribe = AtkTribe.Dark,  SendCost = 15, Hp = 150, SpeedMilliPerSec = 1000, Leak = 2, Income = 3, PeriodicSilenceEverySec = 3, PeriodicSilenceTenths = 20 },
        };

        static readonly Dictionary<int, TowerDef> _t = Index(Towers, x => x.Id);
        static readonly Dictionary<int, AttackerDef> _a = Index(Attackers, x => x.Id);
        public static TowerDef Tower(int id) => _t[id];
        public static AttackerDef Attacker(int id) => _a[id];

        static Dictionary<int, T> Index<T>(T[] arr, System.Func<T, int> key)
        {
            var d = new Dictionary<int, T>();
            foreach (var x in arr) d[key(x)] = x;
            return d;
        }

        /// <summary>기본 웨이브 시간표 (1v1 기준). (공격 유닛 id, 마리 수, 체력 배수)</summary>
        public static readonly (int id, int count, int hpMult)[][] BaseWaves =
        {
            new[] { (1, 4, 1) },
            new[] { (2, 5, 1) },
            new[] { (1, 4, 1), (3, 2, 1) },
            new[] { (5, 1, 3) },                         // 보스: 오우거 ×3
            new[] { (4, 3, 1), (3, 3, 1) },
            new[] { (2, 6, 1), (1, 4, 1) },
            new[] { (5, 2, 1), (1, 6, 1) },
            new[] { (10, 1, 1) },                        // 보스: 거대 골렘
            new[] { (6, 3, 1), (2, 6, 1) },
            new[] { (7, 3, 1), (4, 4, 1) },
            new[] { (5, 3, 1), (3, 6, 1) },
            new[] { (9, 1, 1) },                         // 보스: 드래곤
            new[] { (6, 4, 1), (1, 8, 1) },
            new[] { (10, 1, 1), (2, 8, 1) },
            new[] { (7, 4, 1), (6, 3, 1) },
            new[] { (11, 1, 3) },                        // 보스: 흑마법사 ×3
            new[] { (5, 4, 1), (9, 1, 1) },
            new[] { (4, 6, 1), (6, 4, 1), (7, 3, 1) },
            new[] { (9, 2, 1), (1, 10, 1) },
        };

        public static bool IsBossWave(int index) => index == 3 || index == 7 || index == 11 || index == 15;

        public static string Describe(int waveIndex)
        {
            if (waveIndex < 0 || waveIndex >= BaseWaves.Length) return "없음";
            var parts = new List<string>();
            foreach (var (id, n, mult) in BaseWaves[waveIndex]) parts.Add($"{Attacker(id).Name} {n}" + (mult > 1 ? $"(체력×{mult})" : ""));
            return (IsBossWave(waveIndex) ? "보스: " : "") + string.Join(", ", parts);
        }
    }
}
