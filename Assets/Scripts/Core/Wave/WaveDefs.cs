using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    public enum DefTribe { Forest, Fire, Machine }
    public enum DefJob { Warrior, Archer, Mage }
    public enum AtkTribe { Beast, Air, Giant, Dark }
    public enum Rarity { Common, Rare, Hero }

    /// <summary>방어 유닛 정의. 숫자는 core-rules-v0.2.md 6절.</summary>
    public sealed class DefenderDef
    {
        public int Id; public string Name;
        public int Cost, Hp, Atk;
        public int AttacksPer10s;       // 공속 ×10 (1.0/초 = 10). 정수로만 다룬다
        public int Range;               // 칸. 1 = 근접
        public DefTribe Tribe; public DefJob Job;
        public bool AntiAir;
        public int SplashRadius;        // 칸. 0 = 단일 대상
        public int BurnPerSec, BurnSeconds;
        public int HealPerSec;          // 0 이면 치유 없음. 치유 유닛은 공격하지 않는다
        public bool HealMachineOnly;
        public int AirDamageMultiplier = 1;
        public bool Taunt;              // v0.2 미구현 표시용
    }

    /// <summary>공격 유닛 정의. 숫자는 core-rules-v0.2.md 7절.</summary>
    public sealed class AttackerDef
    {
        public int Id; public string Name;
        public Rarity Rarity; public AtkTribe Tribe;
        public int SendCost, Hp, Atk;
        public int SpeedMilliPerSec;    // 칸/초 ×1000
        public int Leak, Income;
        public bool Flying;
        public int Range = 1;           // 칸
        public int AttacksPer10s = 10;
        public int SplashRadius;
        public int HealPerSec;          // 어둠 사제
        public int ExtraBaseDamage;     // 거대 골렘
    }

    public static class WaveCatalog
    {
        public static readonly DefenderDef[] Defenders =
        {
            new DefenderDef { Id = 1, Name = "나무 궁수",   Cost = 6,  Hp = 40,  Atk = 4,  AttacksPer10s = 10, Range = 3, Tribe = DefTribe.Forest,  Job = DefJob.Archer,  AntiAir = true },
            new DefenderDef { Id = 2, Name = "멧돼지 전사", Cost = 6,  Hp = 70,  Atk = 6,  AttacksPer10s = 8,  Range = 1, Tribe = DefTribe.Forest,  Job = DefJob.Warrior },
            new DefenderDef { Id = 3, Name = "숲의 드루이드", Cost = 10, Hp = 50, Atk = 0,  AttacksPer10s = 0,  Range = 2, Tribe = DefTribe.Forest,  Job = DefJob.Mage,    HealPerSec = 4 },
            new DefenderDef { Id = 4, Name = "화염 창병",   Cost = 7,  Hp = 60,  Atk = 5,  AttacksPer10s = 10, Range = 1, Tribe = DefTribe.Fire,    Job = DefJob.Warrior, BurnPerSec = 2, BurnSeconds = 3 },
            new DefenderDef { Id = 5, Name = "불꽃 술사",   Cost = 9,  Hp = 35,  Atk = 7,  AttacksPer10s = 6,  Range = 2, Tribe = DefTribe.Fire,    Job = DefJob.Mage,    SplashRadius = 1 },
            new DefenderDef { Id = 6, Name = "불사조",     Cost = 14, Hp = 55,  Atk = 9,  AttacksPer10s = 9,  Range = 3, Tribe = DefTribe.Fire,    Job = DefJob.Archer,  AntiAir = true, AirDamageMultiplier = 2 },
            new DefenderDef { Id = 7, Name = "태엽 포탑",   Cost = 8,  Hp = 45,  Atk = 12, AttacksPer10s = 4,  Range = 4, Tribe = DefTribe.Machine, Job = DefJob.Archer },
            new DefenderDef { Id = 8, Name = "강철 골렘",   Cost = 15, Hp = 160, Atk = 8,  AttacksPer10s = 6,  Range = 1, Tribe = DefTribe.Machine, Job = DefJob.Warrior, Taunt = true },
            new DefenderDef { Id = 9, Name = "수리 드론",   Cost = 8,  Hp = 40,  Atk = 0,  AttacksPer10s = 0,  Range = 2, Tribe = DefTribe.Machine, Job = DefJob.Mage,    HealPerSec = 6, HealMachineOnly = true },
        };

        public static readonly AttackerDef[] Attackers =
        {
            new AttackerDef { Id = 1,  Name = "늑대",     Rarity = Rarity.Common, Tribe = AtkTribe.Beast, SendCost = 4,  Hp = 30,  Atk = 4,  SpeedMilliPerSec = 2000, Leak = 1, Income = 1 },
            new AttackerDef { Id = 2,  Name = "고블린",   Rarity = Rarity.Common, Tribe = AtkTribe.Beast, SendCost = 4,  Hp = 45,  Atk = 3,  SpeedMilliPerSec = 1200, Leak = 1, Income = 2 },
            new AttackerDef { Id = 3,  Name = "박쥐",     Rarity = Rarity.Common, Tribe = AtkTribe.Air,   SendCost = 4,  Hp = 25,  Atk = 2,  SpeedMilliPerSec = 2200, Leak = 1, Income = 1, Flying = true },
            new AttackerDef { Id = 4,  Name = "거북",     Rarity = Rarity.Common, Tribe = AtkTribe.Giant, SendCost = 5,  Hp = 100, Atk = 2,  SpeedMilliPerSec = 700,  Leak = 1, Income = 1 },
            new AttackerDef { Id = 5,  Name = "오우거",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Giant, SendCost = 8,  Hp = 160, Atk = 10, SpeedMilliPerSec = 1000, Leak = 2, Income = 2 },
            new AttackerDef { Id = 6,  Name = "그리핀",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Air,   SendCost = 8,  Hp = 110, Atk = 6,  SpeedMilliPerSec = 1600, Leak = 2, Income = 2, Flying = true },
            new AttackerDef { Id = 7,  Name = "암살자",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Dark,  SendCost = 8,  Hp = 60,  Atk = 12, SpeedMilliPerSec = 1800, Leak = 1, Income = 1 },
            new AttackerDef { Id = 8,  Name = "어둠 사제", Rarity = Rarity.Rare,  Tribe = AtkTribe.Dark,  SendCost = 9,  Hp = 70,  Atk = 0,  SpeedMilliPerSec = 1000, Leak = 1, Income = 2, HealPerSec = 3, Range = 2 },
            new AttackerDef { Id = 9,  Name = "드래곤",   Rarity = Rarity.Hero,   Tribe = AtkTribe.Air,   SendCost = 14, Hp = 300, Atk = 15, SpeedMilliPerSec = 1300, Leak = 3, Income = 4, Flying = true, Range = 2, SplashRadius = 1 },
            new AttackerDef { Id = 10, Name = "거대 골렘", Rarity = Rarity.Hero,  Tribe = AtkTribe.Giant, SendCost = 14, Hp = 420, Atk = 12, SpeedMilliPerSec = 600,  Leak = 3, Income = 3, ExtraBaseDamage = 1 },
            new AttackerDef { Id = 11, Name = "흑마법사", Rarity = Rarity.Hero,   Tribe = AtkTribe.Dark,  SendCost = 15, Hp = 150, Atk = 8,  SpeedMilliPerSec = 1000, Leak = 2, Income = 3, Range = 2 },
        };

        static readonly Dictionary<int, DefenderDef> _d = Index(Defenders, x => x.Id);
        static readonly Dictionary<int, AttackerDef> _a = Index(Attackers, x => x.Id);
        public static DefenderDef Defender(int id) => _d[id];
        public static AttackerDef Attacker(int id) => _a[id];

        static Dictionary<int, T> Index<T>(T[] arr, System.Func<T, int> key)
        {
            var d = new Dictionary<int, T>();
            foreach (var x in arr) d[key(x)] = x;
            return d;
        }

        /// <summary>기본 웨이브 (1v1 기준). (공격 유닛 id, 마리 수, 보스 여부)</summary>
        public static (int id, int count, bool boss)[] BaseWave(int round) => round switch
        {
            1 => new[] { (1, 4, false) },
            2 => new[] { (2, 5, false) },
            3 => new[] { (1, 4, false), (3, 2, false) },
            4 => new[] { (5, 1, true) },
            5 => new[] { (4, 3, false), (3, 3, false) },
            6 => new[] { (2, 6, false), (1, 4, false) },
            7 => new[] { (5, 2, false), (1, 6, false) },
            8 => new[] { (10, 1, true) },
            9 => new[] { (6, 3, false), (2, 6, false) },
            10 => new[] { (7, 3, false), (4, 4, false) },
            11 => new[] { (5, 3, false), (3, 6, false) },
            _ => new[] { (9, 1, true) },
        };
    }
}
